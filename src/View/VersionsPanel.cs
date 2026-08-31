using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Le panneau VERSIONS (batch 38, lot D) — le quatrième de la
    /// colonne de droite, même famille que Correction, Annotations et
    /// Recherche : les instantanés de l'item courant (date, libellé,
    /// origine, compte de mots et écart avec l'état actuel), prendre un
    /// instantané, comparer (à l'état actuel, ou deux versions entre elles),
    /// restaurer, renommer, supprimer ; le poids occupé (item et projet) et
    /// les purges explicites. Ne modifie jamais un document lui-même : il
    /// demande (Compare/Restore) à la coquille.</summary>
    public class VersionsPanel : DockPanel
    {
        private readonly TextBlock _itemLabel, _weight, _hint;
        private readonly Button _take, _purge, _compareTwo;
        private readonly ComboBox _compareA, _compareB;
        private readonly StackPanel _list;
        private readonly TextBlock _notice;

        private Project _project;
        private BinderItem _current;
        private List<Snapshot> _shown = new List<Snapshot>();
        private int _currentWords;

        public event Action CloseRequested;
        public event Action SnapshotsChanged;                  // pris, renommé, supprimé, purgé
        public event Action<Snapshot, Snapshot> CompareRequested; // (from, to) — to null = état actuel
        public event Action<Snapshot> RestoreRequested;

        public VersionsPanel()
        {
            Background = Chrome.BarBgLight;
            var frame = new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(12, 10, 12, 10)
            };
            var panel = new DockPanel();

            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(head, Dock.Top);
            var titleRow = new DockPanel();
            var close = new Button { Content = "✕", Width = 22, Padding = new Thickness(0), FontSize = 11, ToolTip = "Fermer le panneau des versions", Focusable = false };
            close.Click += delegate { OnCloseRequested(); };
            DockPanel.SetDock(close, Dock.Right);
            titleRow.Children.Add(close);
            titleRow.Children.Add(new TextBlock
            {
                Text = "Versions",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            head.Children.Add(titleRow);
            _itemLabel = new TextBlock { Foreground = Chrome.Ink, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 6, 0, 0) };
            head.Children.Add(_itemLabel);
            _weight = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            head.Children.Add(_weight);

            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            _take = new Button
            {
                Content = Icons.Label("plus-bold", "Prendre un instantané…", 10, Chrome.Ink),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 4, 3),
                FontSize = 11,
                Focusable = false,
                ToolTip = "Figer l'état actuel de cet écrit, avec un libellé"
            };
            _take.Click += delegate { AskAndTake(); };
            actions.Children.Add(_take);
            _purge = new Button
            {
                Content = new TextBlock { Text = "Purger…", FontSize = 11 },
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(0, 0, 4, 3),
                Focusable = false,
                ToolTip = "Retirer des instantanés — jamais automatiquement"
            };
            _purge.Click += delegate { ShowPurgeMenu(); };
            actions.Children.Add(_purge);
            head.Children.Add(actions);

            var compareRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            compareRow.Children.Add(new TextBlock { Text = "Comparer", Foreground = Chrome.SoftText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 3) });
            _compareA = new ComboBox { FontSize = 11, MinWidth = 96, Margin = new Thickness(0, 0, 4, 3), ToolTip = "La version la plus ancienne" };
            compareRow.Children.Add(_compareA);
            compareRow.Children.Add(new TextBlock { Text = "à", Foreground = Chrome.SoftText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 3) });
            _compareB = new ComboBox { FontSize = 11, MinWidth = 96, Margin = new Thickness(0, 0, 4, 3), ToolTip = "La version la plus récente, ou l'état actuel" };
            compareRow.Children.Add(_compareB);
            _compareTwo = new Button { Content = new TextBlock { Text = "Voir", FontSize = 11 }, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 0, 3), Focusable = false };
            _compareTwo.Click += delegate { CompareChosen(); };
            compareRow.Children.Add(_compareTwo);
            head.Children.Add(compareRow);

            _hint = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            head.Children.Add(_hint);
            _notice = new TextBlock { Foreground = Chrome.Ink, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            head.Children.Add(_notice);
            panel.Children.Add(head);

            _list = new StackPanel();
            panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
            frame.Child = panel;
            Children.Add(frame);
        }

        private void OnCloseRequested()
        {
            var handler = CloseRequested;
            if (handler != null) handler();
        }

        // ================================================== API de la coquille

        public void SetProject(Project project)
        {
            _project = project;
            _current = null;
            Refresh();
        }

        public void SetCurrent(BinderItem current)
        {
            _current = current;
            Refresh();
        }

        public void SetNotice(string text)
        {
            _notice.Text = text ?? "";
            _notice.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public List<Snapshot> Shown { get { return _shown; } }

        private bool HasDocument
        {
            get { return _current != null && _current.Kind == ItemKind.Text; } // plus de versions de fiche (b43)
        }

        /// <summary>« 12/03/2026 14:05 » depuis « 2026-03-12 14:05:33 ».</summary>
        public static string FormatDate(string date)
        {
            DateTime parsed;
            if (DateTime.TryParseExact(date ?? "", new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                return parsed.ToString("dd/MM/yyyy HH:mm");
            return date ?? "";
        }

        /// <summary>Prend un instantané manuel de l'item courant (public : la
        /// sonde). Rend l'instantané, ou null (rien à figer : contenu déjà
        /// capturé tel quel).</summary>
        public Snapshot TakeSnapshot(string label)
        {
            if (_project == null || !HasDocument) return null;
            var snapshot = SnapshotStore.Capture(_project, _current, label ?? "", SnapshotOrigin.Manual, Settings.AppSettings.SnapshotCap);
            if (snapshot == null) SetNotice("Rien à figer : cet état est déjà le dernier instantané.");
            else { SetNotice(null); NotifyChanged(); }
            Refresh();
            return snapshot;
        }

        private void AskAndTake()
        {
            if (!HasDocument) return;
            var label = InputDialog.Ask(Window.GetWindow(this), "Prendre un instantané",
                "Libellé (facultatif — ex. « avant la réécriture du chapitre 3 ») :", "");
            if (label == null) return;
            TakeSnapshot(label.Trim());
        }

        public void Refresh()
        {
            _list.Children.Clear();
            _shown = new List<Snapshot>();
            _compareA.Items.Clear();
            _compareB.Items.Clear();
            if (_project == null || _current == null)
            {
                _itemLabel.Text = "";
                _weight.Text = _project == null ? "" : "Projet : " + SnapshotStore.Weight(_project, null);
                _hint.Text = "Ouvrez un écrit ou une fiche pour voir ses versions.";
                _take.IsEnabled = false;
                _compareTwo.IsEnabled = false;
                return;
            }
            _itemLabel.Text = _current.Title;
            if (!HasDocument)
            {
                _weight.Text = "Projet : " + SnapshotStore.Weight(_project, null);
                _hint.Text = "Cet élément n'a pas de versions : les instantanés concernent les écrits et les fiches.";
                _take.IsEnabled = false;
                _compareTwo.IsEnabled = false;
                return;
            }
            _take.IsEnabled = true;
            _shown = SnapshotStore.Of(_project, _current.Id);
            _currentWords = Correction.TextStats.Compute(_current.Document.ToPlainText()).Words;
            _weight.Text = SnapshotStore.Weight(_project, _current.Id) + " — projet : " + SnapshotStore.Weight(_project, null);
            _hint.Text = _shown.Count == 0
                ? "Aucune version encore. « Prendre un instantané » fige l'état actuel ; les passes typographiques, les remplacements et la première modification du jour en prennent d'eux-mêmes."
                : "";
            foreach (var snapshot in _shown)
            {
                _compareA.Items.Add(snapshot.DisplayLabel + " · " + FormatDate(snapshot.Date));
                _compareB.Items.Add(snapshot.DisplayLabel + " · " + FormatDate(snapshot.Date));
            }
            _compareB.Items.Add("État actuel");
            if (_shown.Count > 0) { _compareA.SelectedIndex = Math.Min(1, _shown.Count - 1); _compareB.SelectedIndex = 0; }
            _compareTwo.IsEnabled = _shown.Count > 0;
            foreach (var snapshot in _shown) _list.Children.Add(Row(snapshot));
        }

        // ================================================== rendu

        private UIElement Row(Snapshot snapshot)
        {
            var stack = new StackPanel();
            var first = new DockPanel();
            var origin = new Border
            {
                Background = snapshot.IsAutomatic ? Chrome.BarBg : Chrome.Accent,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = snapshot.IsAutomatic ? "auto" : "manuel",
                    FontSize = 9,
                    Foreground = snapshot.IsAutomatic ? Chrome.SoftText : Chrome.PaperBg
                },
                ToolTip = SnapshotOrigin.Label(snapshot.Origin)
            };
            DockPanel.SetDock(origin, Dock.Right);
            first.Children.Add(origin);
            first.Children.Add(new TextBlock
            {
                Text = snapshot.DisplayLabel,
                Foreground = Chrome.Ink,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(first);
            var delta = _currentWords - snapshot.Words;
            var deltaText = delta == 0 ? "même nombre de mots" : (delta > 0 ? "+" : "−") + Math.Abs(delta) + (Math.Abs(delta) == 1 ? " mot" : " mots") + " depuis";
            stack.Children.Add(new TextBlock
            {
                Text = FormatDate(snapshot.Date) + " · " + snapshot.Words + (snapshot.Words == 1 ? " mot" : " mots") + " · " + deltaText,
                Foreground = Chrome.SoftText,
                FontSize = 10,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var actions = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            var compare = Small("Comparer", "Lire les différences entre cette version et l'état actuel");
            compare.Click += delegate { var h = CompareRequested; if (h != null) h(snapshot, null); };
            actions.Children.Add(compare);
            var restore = Small("Restaurer", "Remettre l'écrit dans cet état (annulable ; l'état actuel est d'abord figé)");
            restore.Click += delegate { var h = RestoreRequested; if (h != null) h(snapshot); };
            actions.Children.Add(restore);
            var rename = Small("Renommer", "Changer le libellé");
            rename.Click += delegate
            {
                var label = InputDialog.Ask(Window.GetWindow(this), "Renommer la version", "Libellé :", snapshot.Label);
                if (label == null) return;
                Rename(snapshot, label.Trim());
            };
            actions.Children.Add(rename);
            var delete = Small("Supprimer", "Retirer cette version (définitif)");
            delete.Click += delegate
            {
                var answer = MessageDialog.Show(Window.GetWindow(this), "Supprimer la version « " + snapshot.DisplayLabel + " » ?",
                    "Versions", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes) Delete(snapshot);
            };
            actions.Children.Add(delete);
            stack.Children.Add(actions);
            return new Border
            {
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 3),
                CornerRadius = new CornerRadius(4),
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                Child = stack
            };
        }

        private static Button Small(string label, string tooltip)
        {
            return new Button
            {
                Content = new TextBlock { Text = label, FontSize = 10 },
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(0, 0, 3, 2),
                ToolTip = tooltip,
                Focusable = false
            };
        }

        // ================================================== actions

        public void Rename(Snapshot snapshot, string label)
        {
            if (snapshot == null) return;
            snapshot.Label = label ?? "";
            NotifyChanged();
            Refresh();
        }

        public void Delete(Snapshot snapshot)
        {
            if (_project == null || snapshot == null) return;
            _project.Snapshots.Remove(snapshot);
            NotifyChanged();
            Refresh();
        }

        private void CompareChosen()
        {
            if (_shown.Count == 0) return;
            var a = _compareA.SelectedIndex;
            var b = _compareB.SelectedIndex;
            if (a < 0 || a >= _shown.Count) return;
            var from = _shown[a];
            var to = b < 0 || b >= _shown.Count ? null : _shown[b];
            if (to == from) { SetNotice("Choisissez deux versions différentes."); return; }
            // La plus ancienne d'abord (l'ordre de Of est du plus récent au plus ancien).
            if (to != null && string.CompareOrdinal(from.Date, to.Date) > 0) { var swap = from; from = to; to = swap; }
            var handler = CompareRequested;
            if (handler != null) handler(from, to);
        }

        private void ShowPurgeMenu()
        {
            if (_project == null) return;
            var menu = new ContextMenu { PlacementTarget = _purge };
            if (HasDocument)
            {
                var item = _current;
                Add(menu, "Les automatiques de cet écrit", delegate { return SnapshotStore.PurgeAutomatic(_project, item.Id); });
                Add(menu, "Toutes les versions de cet écrit", delegate { return SnapshotStore.PurgeItem(_project, item.Id); });
                menu.Items.Add(new Separator());
            }
            Add(menu, "Les automatiques de tout le projet", delegate { return SnapshotStore.PurgeAutomatic(_project, null); });
            Add(menu, "Tout ce qui a plus de 30 jours", delegate { return SnapshotStore.PurgeOlderThan(_project, 30); });
            Add(menu, "Tout ce qui a plus de 90 jours", delegate { return SnapshotStore.PurgeOlderThan(_project, 90); });
            menu.IsOpen = true;
        }

        private void Add(ContextMenu menu, string header, Func<int> purge)
        {
            var entry = new MenuItem { Header = header };
            entry.Click += delegate
            {
                var answer = MessageDialog.Show(Window.GetWindow(this), "Purger : " + header.ToLowerInvariant() + " ?\nC'est définitif.",
                    "Versions", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                var removed = purge();
                SetNotice(removed == 0 ? "Rien à purger." : removed + (removed == 1 ? " version retirée." : " versions retirées."));
                if (removed > 0) NotifyChanged();
                Refresh();
            };
            menu.Items.Add(entry);
        }

        private void NotifyChanged()
        {
            var handler = SnapshotsChanged;
            if (handler != null) handler();
        }
    }
}

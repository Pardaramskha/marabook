using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>La palette « Ouvrir par le nom » (1.1.0, 29/10, Rémi) :
    /// Ctrl+, pose un VOILE sombre sur toute la fenêtre et, dessus, une
    /// barre de recherche qui cherche les éléments de la Pile PAR LEUR NOM
    /// seulement (même règle que la recherche de la Pile : sans casse ni
    /// accents, corbeille exclue). Flèches et Entrée choisissent ; un clic
    /// sur un résultat ouvre et referme ; un clic sur le voile ou Échap
    /// referme sans rien faire. Le voile vit dans la grille de la coquille,
    /// au-dessus de tout — un Popup ne pouvait pas assombrir le reste.</summary>
    public sealed class QuickOpenPalette
    {
        private const int Cap = 60;
        private readonly Grid _overlay;
        private readonly TextBox _box;
        private readonly ListBox _list;
        private readonly TextBlock _hint;
        private Project _project;
        private Action<string> _open;

        public QuickOpenPalette(Panel host)
        {
            _box = new TextBox
            {
                Watermark = "Nom d'un écrit, d'une fiche, d'un plan, d'un média…",
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _list = new ListBox
            {
                MaxHeight = 320,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                IsVisible = false
            };
            _hint = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 11,
                Margin = new Thickness(2, 4, 0, 0),
                Text = IdleHint
            };
            var stack = new StackPanel { Width = 520 };
            stack.Children.Add(_box);
            stack.Children.Add(_list);
            stack.Children.Add(_hint);
            var frame = new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                Child = stack,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 120, 0, 0), // au milieu en largeur, dans le tiers haut : la liste grandit sans faire sauter la barre
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 10, Blur = 32, Color = Color.FromArgb(0x60, 0, 0, 0) })
            };
            // Le voile : tout le reste s'assombrit, un clic dessus referme.
            var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0x6A, 0x10, 0x14, 0x1C)) };
            backdrop.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { Close(); e.Handled = true; };
            _overlay = new Grid { IsVisible = false, Tag = "quick-open-overlay" };
            _overlay.Children.Add(backdrop);
            _overlay.Children.Add(frame);
            host.Children.Add(_overlay);

            _box.TextChanged += delegate { Refresh(); };
            _box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Down) { Move(1); e.Handled = true; }
                else if (e.Key == Key.Up) { Move(-1); e.Handled = true; }
                else if (e.Key == Key.Return || e.Key == Key.Enter) { ChooseSelected(); e.Handled = true; }
            };
            // Un clic sur un résultat : ouvre et referme (la sélection seule
            // ne suffit pas — les flèches la déplacent sans ouvrir).
            _list.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (_list.SelectedItem != null) { ChooseSelected(); e.Handled = true; }
            };
        }

        private const string IdleHint = "Tapez un nom — ↑ ↓ pour choisir, Entrée pour ouvrir, Échap ou un clic à côté pour fermer.";

        public bool IsOpen { get { return _overlay.IsVisible; } }

        /// <summary>Montre la palette (vide) sur ce projet ; open reçoit l'id choisi.</summary>
        public void Show(Project project, Action<string> open)
        {
            _project = project;
            _open = open;
            if (_project == null) return;
            _box.Text = "";
            Refresh();
            _overlay.IsVisible = true;
            Dispatcher.UIThread.Post(delegate { _box.Focus(); _box.SelectAll(); }, DispatcherPriority.Input);
        }

        public void Close()
        {
            if (!_overlay.IsVisible) return;
            _overlay.IsVisible = false;
            _box.Text = "";
        }

        private void Refresh()
        {
            var query = (_box.Text ?? "").Trim();
            _list.Items.Clear();
            if (query.Length == 0 || _project == null)
            {
                _list.IsVisible = false;
                _hint.Text = IdleHint;
                return;
            }
            var matches = BinderView.NameMatches(_project, query, SearchKind.All, Cap);
            foreach (var item in matches) _list.Items.Add(Row(item));
            _list.IsVisible = matches.Count > 0;
            if (matches.Count > 0) _list.SelectedIndex = 0;
            _hint.Text = matches.Count == 0 ? "Aucun nom ne correspond."
                : matches.Count == 1 ? "1 élément — Entrée pour l'ouvrir."
                : matches.Count >= Cap ? Cap + " premiers éléments — précisez le nom."
                : matches.Count + " éléments — ↑ ↓ puis Entrée, ou un clic.";
        }

        private static ListBoxItem Row(BinderItem item)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = ItemIcons.Render(item, 13, Chrome.SoftText);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 8, 0);
            line.Children.Add(icon);
            line.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var path = BinderView.PathOf(item);
            if (path.Length > 0)
                line.Children.Add(new TextBlock
                {
                    Text = "  " + path,
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            return new ListBoxItem { Content = line, Tag = item.Id, Padding = new Thickness(8, 4, 8, 4) };
        }

        private void Move(int delta)
        {
            if (_list.Items.Count == 0) return;
            var index = _list.SelectedIndex + delta;
            if (index < 0) index = _list.Items.Count - 1;
            if (index >= _list.Items.Count) index = 0;
            _list.SelectedIndex = index;
            _list.ScrollIntoView(index);
        }

        private void ChooseSelected()
        {
            var entry = (_list.SelectedItem ?? (_list.Items.Count > 0 ? _list.Items[0] : null)) as ListBoxItem;
            if (entry == null || entry.Tag == null) return;
            var id = (string)entry.Tag;
            Close();
            var open = _open;
            if (open != null) open(id);
        }

        // — Sonde (29/10).
        internal void TypeForProbe(string text) { _box.Text = text; }
        internal int ResultCountForProbe { get { return _list.Items.Count; } }
        internal string FirstResultIdForProbe { get { var entry = _list.Items.Count > 0 ? _list.Items[0] as ListBoxItem : null; return entry == null ? null : entry.Tag as string; } }
        internal void ChooseForProbe() { ChooseSelected(); }
        internal bool BackdropShownForProbe { get { return _overlay.IsVisible && _overlay.Children.Count > 1 && _overlay.Children[0].IsVisible; } }
    }
}

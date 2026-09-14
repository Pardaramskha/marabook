using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>L'Accueil (batch 41) : la vue de la racine « Accueil » de la
    /// Pile — un point d'entrée qui rassemble ce qu'on reprend, ce qu'on
    /// épingle et où l'on en est (les raccourcis « Commencer » vivent dans le
    /// panneau Général du rail depuis le batch 43). Rien de neuf : tout
    /// ce qu'il affiche est déjà calculé ailleurs. Trois blocs FIXES, dans
    /// un ordre fixe, sur une grille de deux colonnes qui ne retombe en une
    /// seule que sous 520 px ; cartes raised sur le ground de la zone
    /// centrale. Les états vides sont un livrable : chaque bloc a son
    /// invite.</summary>
    public class HomeView : ScrollViewer
    {
        // Deux colonnes (2-1 : Reprendre et Épinglés côte à côte, « Où j'en
        // suis » en dessous sur toute la largeur) tant que la FENÊTRE fait
        // 1 400 px ou plus ; une seule colonne en dessous (14/09 — la largeur
        // de la vue seule variait avec la Pile et le rail, d'où deux
        // dispositions à même écran).
        private const double WideWindowFrom = 1400;
        private bool _windowHooked;

        private Project _project;
        private readonly Grid _grid;
        private readonly StackPanel _resumeList, _pinnedList, _progressList;
        private readonly TextBlock _title;

        public event Action<BinderItem> OpenRequested;   // clic sur un récent ou un épinglé
        public event Action RenameRequested;             // le crayon à côté du nom du projet (b43)

        // Fournis par la coquille (déjà calculés là-bas) : l'objectif de
        // session en cours et les mots du projet.
        public Func<int> SessionGoal;
        public Func<int> SessionBaseWords;
        public Func<int> ProjectWords;

        public HomeView()
        {
            Background = Chrome.WindowBg;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Focusable = true;

            var column = new StackPanel { Margin = new Thickness(28, 22, 28, 28), MaxWidth = 1100 };
            _title = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center
            };
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(4, 0, 0, 14)
            };
            titleRow.Children.Add(_title);
            var rename = Buttons.Icon("pencil-simple-line", "Renommer le projet", Buttons.Compact, Buttons.Look.Calm);
            rename.Margin = new Thickness(8, 4, 0, 0);
            rename.VerticalAlignment = VerticalAlignment.Center;
            rename.Click += delegate { Raise(RenameRequested); };
            titleRow.Children.Add(rename);
            column.Children.Add(titleRow);

            _grid = new Grid();
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 3; i++) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _resumeList = new StackPanel();
            _pinnedList = new StackPanel();
            _progressList = new StackPanel();
            _grid.Children.Add(Card("Reprendre", _resumeList, 0));
            _grid.Children.Add(Card("Épinglés", _pinnedList, 1));
            _grid.Children.Add(Card("Où j'en suis", _progressList, 2));
            column.Children.Add(_grid);
            Content = column;
            SizeChanged += delegate { Reflow(); };
            Loaded += delegate { HookWindow(); Reflow(); };
        }

        /// <summary>La carte d'un bloc : titre en ink-faint, contenu.</summary>
        private static Border Card(string title, UIElement content, int index)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.FaintText,
                Margin = new Thickness(0, 0, 0, 8)
            });
            body.Children.Add(content);
            var card = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 14),
                Margin = new Thickness(4),
                Child = body,
                Tag = index
            };
            return card;
        }

        /// <summary>Deux colonnes quand la place le permet, une seule sinon —
        /// l'ordre des blocs ne change jamais ; un dernier bloc orphelin
        /// s'étale sur les deux colonnes.</summary>
        private void Reflow()
        {
            var window = Window.GetWindow(this);
            var two = (window != null && window.ActualWidth > 0 ? window.ActualWidth : ActualWidth) >= WideWindowFrom;
            var count = _grid.Children.Count;
            foreach (UIElement child in _grid.Children)
            {
                var index = (int)((FrameworkElement)child).Tag;
                var orphan = index == count - 1 && count % 2 == 1;
                Grid.SetColumn(child, two ? index % 2 : 0);
                Grid.SetRow(child, two ? index / 2 : index);
                Grid.SetColumnSpan(child, !two || orphan ? 2 : 1);
            }
        }

        public void Load(Project project)
        {
            _project = project;
            HookWindow();
            Refresh();
            Reflow();
        }

        /// <summary>La disposition suit la FENÊTRE (14/09) : accroché une fois,
        /// au chargement de la vue ou à sa première ouverture.</summary>
        private void HookWindow()
        {
            if (_windowHooked) return;
            var window = Window.GetWindow(this);
            if (window == null) return;
            _windowHooked = true;
            window.SizeChanged += delegate { Reflow(); };
        }

        public void Clear()
        {
            _project = null;
            _resumeList.Children.Clear();
            _pinnedList.Children.Clear();
            _progressList.Children.Clear();
        }

        /// <summary>Recalcule les trois blocs depuis le projet — appelé à
        /// l'affichage et après une épingle. Rien n'est gardé ici.</summary>
        public void Refresh()
        {
            if (_project == null) return;
            _title.Text = string.IsNullOrEmpty(_project.Name) ? "Accueil" : _project.Name;
            FillResume();
            FillPinned();
            FillProgress();
            Reflow();
        }

        // ------------------------------------------------------------ les blocs

        private void FillResume()
        {
            _resumeList.Children.Clear();
            var valid = Recents.Valid(_project);
            if (valid.Count == 0)
            {
                _resumeList.Children.Add(Prompt("Les écrits ouverts récemment apparaîtront ici."));
                return;
            }
            var now = DateTime.Now;
            for (var i = 0; i < valid.Count && i < 6; i++)
            {
                var item = _project.FindById(valid[i].ItemId);
                _resumeList.Children.Add(ItemRow(item, Recents.Elapsed(valid[i].Date, now)));
            }
        }

        private void FillPinned()
        {
            _pinnedList.Children.Clear();
            var any = false;
            foreach (var item in _project.AllItems())
            {
                if (!item.Pinned || item.IsCategory || Recents.InTrash(item)) continue; // même filtre que Reprendre (A3)
                _pinnedList.Children.Add(ItemRow(item, null));
                any = true;
            }
            if (!any) _pinnedList.Children.Add(Prompt("Clic droit sur un élément → Épingler."));
        }

        /// <summary>Rien de neuf : les mots du jour et l'objectif journalier
        /// (WritingJournal), l'objectif de session en cours (la coquille),
        /// une barre par livre (BookProgress, le dessin du batch 32).</summary>
        private void FillProgress()
        {
            _progressList.Children.Clear();
            var any = false;
            var culture = System.Globalization.CultureInfo.CurrentCulture;

            var journal = _project.Journal;
            var today = journal.WordsOn(WritingJournal.Today());
            if (journal.DailyGoal > 0)
            {
                var bar = new BookProgressBar { Margin = new Thickness(0, 0, 0, 10) };
                var ratio = (double)today / journal.DailyGoal;
                bar.ShowRatio("Aujourd'hui : " + today.ToString("N0", culture) + " / "
                    + journal.DailyGoal.ToString("N0", culture) + " mots"
                    + (today >= journal.DailyGoal ? " — objectif atteint !" : ""),
                    ratio, today >= journal.DailyGoal ? 1 : 0);
                _progressList.Children.Add(bar);
                any = true;
            }
            else if (today > 0)
            {
                _progressList.Children.Add(Line("Aujourd'hui : " + today.ToString("N0", culture)
                    + (today > 1 ? " mots écrits" : " mot écrit")));
                any = true;
            }

            var sessionGoal = SessionGoal == null ? 0 : SessionGoal();
            if (sessionGoal > 0 && SessionBaseWords != null && ProjectWords != null)
            {
                var written = System.Math.Max(0, ProjectWords() - SessionBaseWords());
                var bar = new BookProgressBar { Margin = new Thickness(0, 0, 0, 10) };
                bar.ShowRatio("Session : " + written.ToString("N0", culture) + " / "
                    + sessionGoal.ToString("N0", culture) + " mots"
                    + (written >= sessionGoal ? " — objectif atteint !" : ""),
                    (double)written / sessionGoal, written >= sessionGoal ? 1 : 0);
                _progressList.Children.Add(bar);
                any = true;
            }

            foreach (var item in _project.AllItems())
            {
                if (item.Kind != ItemKind.Book || Recents.InTrash(item)) continue;
                var progress = BookProgress.Of(item);
                var bar = new BookProgressBar { Margin = new Thickness(0, 0, 0, 10) };
                var title = string.IsNullOrEmpty(item.Title) ? "Livre" : item.Title;
                bar.Show(progress, title + " — pas d'objectif de chapitres");
                if (progress.HasGoal) bar.Label.Text = title + " — " + BookProgressBar.Describe(progress);
                _progressList.Children.Add(bar);
                any = true;
            }

            if (!any) _progressList.Children.Add(Prompt("Définis un objectif pour suivre ta progression."));
        }

        private static TextBlock Line(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 6)
            };
        }

        // ------------------------------------------------------------ pièces

        private static TextBlock Prompt(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.FaintText,
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            };
        }

        /// <summary>Une ligne d'item : icône, titre, mention à droite ; clic = ouvrir.</summary>
        private Border ItemRow(BinderItem item, string mention)
        {
            var row = new DockPanel { LastChildFill = true };
            var icon = ItemIcons.Render(item, 14, Chrome.SoftText) as FrameworkElement;
            if (icon != null)
            {
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);
            }
            if (!string.IsNullOrEmpty(mention))
            {
                var when = new TextBlock
                {
                    Text = mention,
                    Foreground = Chrome.FaintText,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                DockPanel.SetDock(when, Dock.Right);
                row.Children.Add(when);
            }
            row.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(item.Title) ? "Sans titre" : item.Title,
                Foreground = Chrome.Ink,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            var host = new Border
            {
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = row,
                Tag = item
            };
            host.MouseEnter += delegate { host.Background = Chrome.AccentTint; };
            host.MouseLeave += delegate { host.Background = Brushes.Transparent; };
            host.MouseLeftButtonUp += delegate { Open(item); };
            return host;
        }

        private void Open(BinderItem item)
        {
            var handler = OpenRequested;
            if (handler != null) handler(item);
        }

        private static void Raise(Action handler)
        {
            if (handler != null) handler();
        }

        // Pour les tests et les sondes : les lignes cliquables d'un bloc,
        // les invites d'état vide affichées.
        public List<BinderItem> ResumeItems { get { return ItemsOf(_resumeList); } }
        public List<BinderItem> PinnedItems { get { return ItemsOf(_pinnedList); } }
        public int ProgressBars { get { return CountOf(_progressList, typeof(BookProgressBar)); } }

        public List<string> Prompts
        {
            get
            {
                var prompts = new List<string>();
                foreach (var list in new[] { _resumeList, _pinnedList, _progressList })
                    foreach (UIElement child in list.Children)
                    {
                        var text = child as TextBlock;
                        if (text != null && text.FontStyle == FontStyles.Italic) prompts.Add(text.Text);
                    }
                return prompts;
            }
        }

        private static int CountOf(Panel list, Type type)
        {
            var count = 0;
            foreach (UIElement child in list.Children) if (child.GetType() == type) count++;
            return count;
        }

        private static List<BinderItem> ItemsOf(Panel list)
        {
            var items = new List<BinderItem>();
            foreach (UIElement child in list.Children)
            {
                var host = child as Border;
                if (host != null && host.Tag is BinderItem) items.Add((BinderItem)host.Tag);
            }
            return items;
        }

        /// <summary>Le clic d'une ligne, pour les sondes (jamais de clic synthétique).</summary>
        public void ClickRow(BinderItem item)
        {
            Open(item);
        }
    }
}

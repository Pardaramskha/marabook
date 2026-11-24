using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>L'état du sprint en cours, servi par la coquille (b48).</summary>
    public class SprintStatus
    {
        public bool Active, Finished;
        public int Written, Goal;
        public int Minutes;      // prévues, 0 = libre
        public int Elapsed;      // minutes écoulées
        public int SecondsLeft;  // -1 = libre
    }

    /// <summary>L'Accueil (batch 41) : la vue de la racine « Accueil » de la
    /// Pile — un point d'entrée qui rassemble ce qu'on reprend, ce qu'on
    /// épingle et où l'on en est (les raccourcis « Commencer » vivent dans le
    /// panneau Général du rail depuis le batch 43). Rien de neuf : tout
    /// ce qu'il affiche est déjà calculé ailleurs.
    /// REFAIT LE 24/11/2026 AU SOIR (Rémi : « le look donné à la page d'accueil
    /// dans l'Atelier ») — sous TOUS les skins, de haut en bas :
    ///  · « OÙ J'EN SUIS » : l'anneau de l'objectif du jour, les mots du jour en
    ///    grand, l'objectif, la série et le meilleur jour sur une ligne, « Lancer
    ///    un sprint » et « Régler l'objectif » ; à droite, la carte du sprint tant
    ///    qu'il y en a un ;
    ///  · « REPRENDRE » et « ÉPINGLÉS », côte à côte quand la fenêtre est large ;
    ///  · « LIVRES » : une carte par livre — titre, chapitres, pourcentage des
    ///    chapitres terminés en grand, la BARRE À JALONS (25 % premier jet, 60 %
    ///    bêta, 95 % relu), les jalons franchis, le rythme (échéance, taille).
    /// Les skins « Museum » y ajoutent leurs titres, le filet en orbite et la
    /// plaque d'un livre terminé. Les états vides sont un livrable : chaque bloc
    /// a son invite.</summary>
    public class HomeView : ScrollViewer
    {
        /// <summary>Le gabarit du ScrollViewer se cherche par type (27/09).</summary>
        protected override Type StyleKeyOverride { get { return typeof(ScrollViewer); } }

        // Deux colonnes (Reprendre et Épinglés côte à côte, deux livres par
        // rangée) tant que la FENÊTRE fait 1 400 px ou plus ; une seule en
        // dessous (14/09 — la largeur de la vue seule variait avec la Pile et
        // le rail, d'où deux dispositions à même écran).
        private const double WideWindowFrom = 1400;
        private bool _windowHooked;

        private Project _project;
        private readonly Grid _grid;
        private readonly StackPanel _resumeList, _pinnedList;
        private readonly Grid _hero;
        private readonly Border _heroCard;
        private readonly Avalonia.Controls.Primitives.UniformGrid _books;
        private readonly StackPanel _booksHost;
        private readonly TextBlock _title;
        private readonly List<TextBlock> _labels = new List<TextBlock>();
        private GoalRing _ring;
        private TextBlock _heroTitle;
        private Border _sprintCard;

        public event Action<BinderItem> OpenRequested;   // clic sur un récent, un épinglé ou un livre
        public Func<BinderItem, ContextMenu> MenuProvider; // clic droit : le menu de la Pile (14/09)
        public event Action RenameRequested;             // le crayon à côté du nom du projet (b43)
        public event Action SprintRequested;             // « Lancer un sprint » (24/11)
        public event Action GoalRequested;               // « Régler l'objectif » : le Journal, où il se règle (24/11)

        // Fournis par la coquille (déjà calculés là-bas, b48) : le sprint en
        // cours, les mots et caractères d'un écrit (pour le rythme des livres).
        public Func<SprintStatus> Sprint;
        public Func<BinderItem, int> WordsOf;
        public Func<BinderItem, int> CharsOf;
        /// <summary>Faux quand le profil cache les sprints : le bouton disparaît.</summary>
        public Func<bool> SprintsEnabled;

        public HomeView()
        {
            Skin.Ground(this); // Chrome.WindowBg, ou transparent sur le fond d'un skin (21/11)
            ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
            Focusable = true;

            var column = new StackPanel { Margin = new Thickness(28, 22, 28, 28), MaxWidth = 1100 };
            _title = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeight.SemiBold,
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

            // — « Où j'en suis » : l'anneau, les mots du jour, le sprint. Un filet en orbite autour
            // (série « Museum » ; posé une fois pour toutes, il ne dessine rien sous un autre skin).
            _hero = new Grid();
            _hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _heroCard = Frame(null, new Thickness(18, 16, 18, 16));
            _heroCard.Tag = "home-hero";
            var layers = new Panel();
            layers.Children.Add(_hero);
            layers.Children.Add(Skin.OrbitInside(_heroCard));
            _heroCard.Child = layers;
            column.Children.Add(_heroCard);

            _grid = new Grid();
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 2; i++) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _resumeList = new StackPanel();
            _pinnedList = new StackPanel();
            _grid.Children.Add(Card("Reprendre", _resumeList, 0));
            _grid.Children.Add(Card("Épinglés", _pinnedList, 1));
            column.Children.Add(_grid);

            // — « Livres » : une carte par livre, à barre à jalons.
            _books = new Avalonia.Controls.Primitives.UniformGrid { Columns = 1, Margin = new Thickness(-5, 0, -5, -5) };
            _booksHost = new StackPanel();
            _booksHost.Children.Add(_books);
            var booksCard = Card("Livres", _booksHost, 2);
            booksCard.Tag = "home-books";
            column.Children.Add(booksCard);

            Content = column;
            Ui.OnSizeChanged(this, delegate { Reflow(); });
            Loaded += delegate { HookWindow(); Reflow(); };
        }

        /// <summary>L'étiquette d'un bloc : petites capitales espacées — de métal sous un skin
        /// « Museum », en encre pâle ailleurs (la couleur est reposée à chaque Refresh).</summary>
        private TextBlock Label(string text)
        {
            var label = new TextBlock
            {
                Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 1.4,
                Foreground = Skin.Museum ? (IBrush)Chrome.Accent : Chrome.FaintText,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _labels.Add(label);
            return label;
        }

        private static Border Frame(Control child, Thickness padding)
        {
            return new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = padding,
                Margin = new Thickness(4),
                Child = child
            };
        }

        /// <summary>La carte d'un bloc : son étiquette, son contenu.</summary>
        private Border Card(string title, Control content, int index)
        {
            var body = new StackPanel();
            body.Children.Add(Label(title));
            body.Children.Add(content);
            var card = Frame(body, new Thickness(16, 12, 16, 14));
            card.Tag = index;
            return card;
        }

        /// <summary>Deux colonnes quand la place le permet, une seule sinon —
        /// l'ordre des blocs ne change jamais.</summary>
        private void Reflow()
        {
            var window = Ui.OwnerOf(this);
            var two = (window != null && window.Bounds.Width > 0 ? window.Bounds.Width : Bounds.Width) >= WideWindowFrom;
            foreach (Control child in _grid.Children)
            {
                var index = (int)((Control)child).Tag;
                Grid.SetColumn(child, two ? index % 2 : 0);
                Grid.SetRow(child, two ? 0 : index);
                Grid.SetColumnSpan(child, two ? 1 : 2);
            }
            _books.Columns = two && _books.Children.Count > 1 ? 2 : 1;
            // La carte du sprint : à droite quand il y a la place, sous le reste sinon.
            if (_sprintCard != null)
            {
                var beside = Bounds.Width <= 0 || Bounds.Width >= 760;
                Grid.SetColumn(_sprintCard, beside ? 2 : 0);
                Grid.SetRow(_sprintCard, beside ? 0 : 1);
                Grid.SetColumnSpan(_sprintCard, beside ? 1 : 3);
                _sprintCard.Margin = beside ? new Thickness(18, 0, 0, 0) : new Thickness(0, 14, 0, 0);
                _sprintCard.Width = beside ? 210 : double.NaN;
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
            var window = Ui.OwnerOf(this);
            if (window == null) return;
            _windowHooked = true;
            Ui.OnSizeChanged(window, delegate { Reflow(); });
        }

        public void Clear()
        {
            _project = null;
            _resumeList.Children.Clear();
            _pinnedList.Children.Clear();
            _hero.Children.Clear();
            _books.Children.Clear();
            _sprintCard = null;
        }

        /// <summary>Recalcule les blocs depuis le projet — appelé à
        /// l'affichage et après une épingle. Rien n'est gardé ici.</summary>
        public void Refresh()
        {
            if (_project == null) return;
            _title.Text = string.IsNullOrEmpty(_project.Name) ? "Accueil" : _project.Name;
            FillResume();
            FillPinned();
            FillHero();
            FillBooks();
            foreach (var label in _labels) label.Foreground = Skin.Museum ? (IBrush)Chrome.Accent : Chrome.FaintText;
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
            var shown = 0;
            for (var i = 0; i < valid.Count && shown < 6; i++)
            {
                var item = _project.FindById(valid[i].ItemId);
                if (item != null && !ItemTiles.ShownByProfile(item)) continue; // fiches ou plans cachés par le profil (1.1.0)
                _resumeList.Children.Add(ItemRow(item, Recents.Elapsed(valid[i].Date, now)));
                shown++;
            }
            if (shown == 0) _resumeList.Children.Add(Prompt("Les écrits ouverts récemment apparaîtront ici."));
        }

        private void FillPinned()
        {
            _pinnedList.Children.Clear();
            var any = false;
            foreach (var item in _project.AllItems())
            {
                if (!item.Pinned || item.IsCategory || Recents.InTrash(item) || !ItemTiles.ShownByProfile(item)) continue; // même filtre que Reprendre (A3) ; profil (1.1.0)
                _pinnedList.Children.Add(ItemRow(item, null));
                any = true;
            }
            if (!any) _pinnedList.Children.Add(Prompt("Clic droit sur un élément → Épingler."));
        }

        /// <summary>« Où j'en suis » : rien de neuf — les mots du jour, l'objectif journalier, la
        /// série et le meilleur jour (WritingJournal), le sprint (la coquille).</summary>
        private void FillHero()
        {
            _hero.Children.Clear();
            _sprintCard = null;
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var journal = _project.Journal;
            var today = journal.WordsOn(WritingJournal.Today());
            var goal = journal.DailyGoal;

            _ring = new GoalRing { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 20, 0) };
            if (goal > 0) _ring.Show((double)today / goal, Math.Min(999, (int)Math.Floor(100.0 * today / goal)).ToString(culture) + " %", "DU JOUR");
            else _ring.Show(0, "—", "À RÉGLER");
            Grid.SetColumn(_ring, 0);
            _hero.Children.Add(_ring);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Label("Où j'en suis"));
            _labels.RemoveAt(_labels.Count - 1); // rebâtie à chaque passage : sa couleur est déjà la bonne
            _heroTitle = new TextBlock
            {
                Text = today.ToString("N0", culture) + (today > 1 ? " mots aujourd'hui" : " mot aujourd'hui"),
                FontSize = 24,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.Ink,
                TextWrapping = TextWrapping.Wrap
            };
            Skin.Title(_heroTitle);
            text.Children.Add(_heroTitle);

            var parts = new List<string>();
            if (goal > 0) parts.Add("Objectif " + goal.ToString("N0", culture) + (today >= goal ? " — atteint !" : ""));
            var streak = journal.Streak();
            if (streak > 0) parts.Add(streak > 1 ? "série de " + streak + " jours" : "série d'un jour");
            var best = 0;
            foreach (var day in journal.Days) if (day.Words > best) best = day.Words;
            if (best > 0) parts.Add("meilleur jour " + best.ToString("N0", culture));
            text.Children.Add(new TextBlock
            {
                Text = parts.Count == 0 ? "Définis un objectif pour suivre ta progression." : string.Join(" · ", parts.ToArray()),
                Foreground = Chrome.SoftText,
                FontSize = 12.5,
                FontStyle = parts.Count == 0 ? FontStyle.Italic : FontStyle.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

            var sprint = Sprint == null ? null : Sprint();
            var running = sprint != null && (sprint.Active || sprint.Finished);
            var actions = new WrapPanel { Margin = new Thickness(0, 11, 0, 0) };
            if (SprintsEnabled == null || SprintsEnabled())
            {
                var launch = Buttons.Text(sprint != null && sprint.Active ? "Relancer un sprint" : "Lancer un sprint", null, Buttons.Bar, Buttons.Look.Primary);
                launch.Margin = new Thickness(0, 0, 8, 4);
                launch.Tag = "home-sprint";
                launch.Click += delegate { Raise(SprintRequested); };
                actions.Children.Add(launch);
            }
            var tune = Buttons.Text("Régler l'objectif", null, Buttons.Bar, Buttons.Look.Outline);
            tune.Margin = new Thickness(0, 0, 8, 4);
            tune.Tag = "home-goal";
            tune.Click += delegate { Raise(GoalRequested); };
            actions.Children.Add(tune);
            text.Children.Add(actions);
            Grid.SetColumn(text, 1);
            _hero.Children.Add(text);

            if (running)
            {
                _sprintCard = SprintCard(sprint, culture);
                _hero.Children.Add(_sprintCard);
            }
        }

        /// <summary>La carte du sprint (b48) : en cours ou fini, tant qu'il n'est pas refermé. Sous un
        /// skin « Museum », la tranche de métal d'un carton à sa gauche.</summary>
        private Border SprintCard(SprintStatus sprint, System.Globalization.CultureInfo culture)
        {
            var body = new StackPanel { Margin = new Thickness(Skin.Museum ? 22 : 12, 10, 12, 10), VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(Label(sprint.Finished ? "Sprint terminé" : "Sprint en cours"));
            _labels.RemoveAt(_labels.Count - 1);
            string big;
            if (sprint.Finished) big = sprint.Written.ToString("N0", culture) + (sprint.Written > 1 ? " mots" : " mot");
            else if (sprint.SecondsLeft >= 0) big = (sprint.SecondsLeft / 60).ToString("00", culture) + ":" + (sprint.SecondsLeft % 60).ToString("00", culture);
            else big = sprint.Elapsed + " min";
            var title = new TextBlock { Text = big, FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink };
            Skin.Title(title);
            body.Children.Add(title);
            var reached = sprint.Goal > 0 && sprint.Written >= sprint.Goal;
            string sub;
            if (sprint.Finished) sub = "en " + sprint.Elapsed + " min" + (sprint.Goal > 0 ? " · objectif " + sprint.Goal.ToString("N0", culture) : "");
            else sub = (sprint.Minutes > 0 ? "sur " + sprint.Minutes + " min" : "sprint libre") + " · " + sprint.Written.ToString("N0", culture)
                + (sprint.Goal > 0 ? " / " + sprint.Goal.ToString("N0", culture) : "") + (sprint.Written > 1 ? " mots" : " mot");
            if (reached) sub += " — objectif atteint !";
            body.Children.Add(new TextBlock { Text = sub, Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (sprint.Goal > 0)
            {
                var track = new Grid { Height = 5, Margin = new Thickness(0, 9, 0, 0) };
                var ratio = Math.Max(0, Math.Min(1.0, (double)sprint.Written / sprint.Goal));
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
                var back = new Border { Background = Chrome.Border, CornerRadius = new CornerRadius(2.5) };
                Grid.SetColumnSpan(back, 2);
                track.Children.Add(back);
                track.Children.Add(new Border { Background = reached ? (IBrush)Chrome.Ok : Chrome.Accent, CornerRadius = new CornerRadius(2.5) });
                body.Children.Add(track);
            }
            var layers = new Panel();
            if (Skin.Museum)
                layers.Children.Add(new Border { Width = 12, HorizontalAlignment = HorizontalAlignment.Left, Background = Skin.MetalBrush(Chrome.Spine.Color), IsHitTestVisible = false });
            layers.Children.Add(body);
            return new Border
            {
                Background = Chrome.CardBg,
                BorderBrush = Chrome.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = layers,
                Tag = "home-sprint-card"
            };
        }

        /// <summary>« Livres » : une carte par livre (BookProgress, BookPace — rien de neuf).</summary>
        private void FillBooks()
        {
            _books.Children.Clear();
            while (_booksHost.Children.Count > 1) _booksHost.Children.RemoveAt(1);
            foreach (var item in _project.AllItems())
            {
                if (item.Kind != ItemKind.Book || Recents.InTrash(item)) continue;
                _books.Children.Add(BookCard(item));
            }
            _books.IsVisible = _books.Children.Count > 0;
            if (_books.Children.Count == 0) _booksHost.Children.Add(Prompt("Les livres du projet apparaîtront ici, avec leur avancement."));
        }

        /// <summary>La part d'un livre qui est TERMINÉE : ses chapitres à l'état « Terminé », sur son
        /// objectif de chapitres — ou, sans objectif, sur les chapitres qu'il a.</summary>
        internal static double DoneShare(BookProgress progress)
        {
            var whole = progress.HasGoal ? progress.Goal : progress.Present;
            return whole <= 0 ? 0 : Math.Min(1.0, (double)progress.Done / whole);
        }

        private Border BookCard(BinderItem book)
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var progress = BookProgress.Of(book);
            var share = DoneShare(progress);
            var done = share >= 0.999 && progress.Present > 0;

            var body = new StackPanel();
            var head = new DockPanel { LastChildFill = true };
            var percent = new TextBlock
            {
                Text = Math.Floor(share * 100).ToString("0", culture) + " %",
                FontSize = 21,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.Accent,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(12, 0, 0, 0)
            };
            Skin.Title(percent);
            DockPanel.SetDock(percent, Dock.Right);
            head.Children.Add(percent);
            var names = new StackPanel();
            var title = new TextBlock
            {
                Text = string.IsNullOrEmpty(book.Title) ? "Livre" : book.Title,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = Chrome.Ink,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Skin.Title(title);
            names.Children.Add(title);
            var chapters = progress.HasGoal
                ? progress.Present.ToString("N0", culture) + " / " + progress.Goal.ToString("N0", culture) + " chapitres"
                : progress.Present.ToString("N0", culture) + (progress.Present > 1 ? " chapitres" : " chapitre");
            var finished = progress.Present > 0 && progress.Done >= progress.Present && !progress.HasGoal ? "tous terminés"
                : progress.Done.ToString("N0", culture) + (progress.Done > 1 ? " terminés" : " terminé");
            names.Children.Add(new TextBlock { Text = chapters + " · " + finished, Foreground = Chrome.FaintText, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
            head.Children.Add(names);
            body.Children.Add(head);

            var bar = new MilestoneBar { Margin = new Thickness(0, 8, 0, 0) };
            bar.Show(share);
            body.Children.Add(bar);

            // Les jalons franchis, puis « Terminé ».
            var passed = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 14, Margin = new Thickness(0, 2, 0, 0) };
            var glyphs = new[] { "✦ ", "○ ", "▣ " };
            var count = Skins.MilestonesPassed(share);
            for (var i = 0; i < count; i++)
            {
                if (i > 0) passed.Children.Add(Stage("|", Chrome.FaintText, new Thickness(7, 0, 7, 0)));
                passed.Children.Add(Stage(glyphs[i] + Skins.BookMilestonePassed[i].ToUpper(culture), Chrome.Accent, new Thickness(0)));
            }
            if (done)
            {
                if (count > 0) passed.Children.Add(Stage("|", Chrome.FaintText, new Thickness(7, 0, 7, 0)));
                passed.Children.Add(Stage("✓ TERMINÉ", Chrome.Ok, new Thickness(0)));
            }
            body.Children.Add(passed);

            // Le rythme (b48) : échéance et taille, s'ils sont réglés.
            var pace = BookPace.Of(book, WordsOf, CharsOf, DateTime.Today);
            if (pace.HasSizeGoal)
            {
                var sizeBar = new BookProgressBar { Margin = new Thickness(0, 6, 0, 0) };
                sizeBar.ShowRatio(pace.Describe(culture), pace.Ratio, pace.Reached ? 1 : 0);
                body.Children.Add(sizeBar);
            }
            else if (pace.HasDeadline)
                body.Children.Add(new TextBlock { Text = pace.Describe(culture), Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });

            var layers = new Panel();
            // Un livre TERMINÉ : la plaque de métal par-dessus le fond (série « Museum » ; rien ailleurs).
            var plaque = done ? Skin.Plaque() : null;
            if (plaque != null) layers.Children.Add(new Border { Background = plaque, IsHitTestVisible = false, Margin = new Thickness(-14, -10, -14, -9) });
            layers.Children.Add(body);
            var card = new Border
            {
                Background = Chrome.CardBg,
                BorderBrush = done && plaque != null ? (IBrush)Chrome.Accent : Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10, 14, 9),
                Margin = new Thickness(5),
                ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = layers,
                Tag = book
            };
            card.PointerEntered += delegate { card.BorderBrush = Skin.Museum ? (IBrush)Chrome.Hover : Chrome.Accent; };
            card.PointerExited += delegate { card.BorderBrush = done && plaque != null ? (IBrush)Chrome.Accent : Chrome.Border; };
            card.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton == MouseButton.Left) { Open(book); return; }
                if (e.InitialPressMouseButton != MouseButton.Right) return;
                var menu = MenuProvider == null ? null : MenuProvider(book);
                if (menu == null) return;
                menu.PlacementTarget = card;
                Ui.ShowMenu(menu, card);
                e.Handled = true;
            };
            return card;
        }

        private static TextBlock Stage(string text, IBrush brush, Thickness margin)
        {
            return new TextBlock { Text = text, Foreground = brush, FontSize = 9.5, FontWeight = FontWeight.Medium, LetterSpacing = 1.1, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        }

        // ------------------------------------------------------------ pièces

        private static TextBlock Prompt(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.FaintText,
                FontSize = 12,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2)
            };
        }

        /// <summary>Une ligne d'item : icône, titre, mention à droite ; clic = ouvrir.</summary>
        private Border ItemRow(BinderItem item, string mention)
        {
            var row = new DockPanel { LastChildFill = true };
            var icon = ItemIcons.Render(item, 14, Chrome.SoftText) as Control;
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
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = row,
                Tag = item
            };
            host.PointerEntered += delegate { host.Background = Chrome.AccentTint; };
            host.PointerExited += delegate { host.Background = Brushes.Transparent; };
            // Seul le clic GAUCHE ouvre (29/09) : le droit ne fait que le menu.
            host.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton == MouseButton.Left) Open(item); };
            host.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton != MouseButton.Right) return;
                var menu = MenuProvider == null ? null : MenuProvider(item);
                if (menu == null) return;
                menu.PlacementTarget = host;
                Ui.ShowMenu(menu, host);
                e.Handled = true;
            };
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
        // les invites d'état vide affichées, les cartes de livre.
        public List<BinderItem> ResumeItems { get { return ItemsOf(_resumeList); } }
        public List<BinderItem> PinnedItems { get { return ItemsOf(_pinnedList); } }
        public List<BinderItem> BookItems { get { return ItemsOf(_books); } }
        internal string HeroTitleForProbe { get { return _heroTitle == null ? "" : _heroTitle.Text; } }
        internal double RingRatioForProbe { get { return _ring == null ? -1 : _ring.Ratio; } }
        internal string RingTextForProbe { get { return _ring == null ? "" : _ring.Text; } }
        internal bool SprintCardShownForProbe { get { return _sprintCard != null; } }
        internal Border HeroCardForProbe { get { return _heroCard; } }
        internal void RaiseSprintForProbe() { Raise(SprintRequested); }
        internal void RaiseGoalForProbe() { Raise(GoalRequested); }

        /// <summary>Sonde : la barre à jalons de la carte de ce livre (null s'il n'en a pas).</summary>
        internal MilestoneBar MilestonesOfForProbe(BinderItem book)
        {
            foreach (Control child in _books.Children)
            {
                var card = child as Border;
                if (card == null || !ReferenceEquals(card.Tag, book)) continue;
                foreach (var visual in card.GetVisualDescendants()) if (visual is MilestoneBar) return (MilestoneBar)visual;
                foreach (var logical in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(card)) if (logical is MilestoneBar) return (MilestoneBar)logical;
            }
            return null;
        }

        public List<string> Prompts
        {
            get
            {
                var prompts = new List<string>();
                foreach (var list in new Panel[] { _resumeList, _pinnedList, _booksHost })
                    foreach (Control child in list.Children)
                    {
                        var text = child as TextBlock;
                        if (text != null && text.FontStyle == FontStyle.Italic) prompts.Add(text.Text);
                    }
                return prompts;
            }
        }

        private static List<BinderItem> ItemsOf(Panel list)
        {
            var items = new List<BinderItem>();
            foreach (Control child in list.Children)
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

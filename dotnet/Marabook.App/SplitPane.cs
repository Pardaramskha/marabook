using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.History;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LA VUE SCINDÉE (21/11/2026, Rémi) : la moitié droite de la
    /// zone centrale, pour TRAVAILLER sur deux éléments à la fois — un écrit
    /// ou une fiche, ÉDITABLE, à côté de celui qu'on a déjà ouvert. Ce n'est
    /// pas l'épinglé du rail, qui sert à garder des notes sous les yeux en
    /// lecture seule.
    ///
    /// Un écrit s'y édite dans une vraie surface composée (les mêmes pages
    /// qu'à gauche, réduites à la largeur du volet) : frappe, sélection,
    /// raccourcis de mise en forme, typographie à la frappe, annulation
    /// propre au volet. Une fiche s'y édite dans une seconde vue de fiche.
    /// Le volet n'a ni ruban ni correcteur : ceux de la fenêtre suivent
    /// l'élément de gauche.
    ///
    /// Sa croix est EN BAS À DROITE, dans son pied, avec le titre de ce
    /// qu'il montre — en haut, elle aurait gêné la rangée des onglets.</summary>
    internal sealed class SplitPane : DockPanel
    {
        private readonly ComposedView _text;
        private readonly SheetView _sheet;
        private readonly Panel _host;
        private readonly TextBlock _title, _kind;
        private readonly Button _close;
        private Project _project;
        private PageSetup _setup;

        public BinderItem Item { get; private set; }

        /// <summary>Le contenu du volet vient d'être modifié.</summary>
        public event Action Edited;
        public event Action CloseRequested;
        /// <summary>Un lien, une relation suivis depuis le volet : l'élément s'ouvre à GAUCHE.</summary>
        public event Action<BinderItem> NavigateRequested;
        public event Action<string> LinkClicked;

        public SplitPane()
        {
            Tag = "split-pane";
            Skin.Ground(this); // Chrome.WindowBg, ou transparent sur le fond d'un skin (21/11)

            // — Le pied : le titre à gauche, la croix en bas à droite.
            _title = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Tag = "split-title" };
            _kind = new TextBlock { FontSize = 11.5, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _close = Buttons.Icon("x-bold", "Fermer la vue scindée", Buttons.Compact, Buttons.Look.Calm);
            _close.Tag = "split-close";
            _close.Click += delegate { var handler = CloseRequested; if (handler != null) handler(); };
            var foot = new DockPanel { Margin = new Thickness(12, 3, 8, 3), LastChildFill = true };
            SetDock(_close, Dock.Right);
            foot.Children.Add(_close);
            var names = new StackPanel { Orientation = Orientation.Horizontal };
            names.Children.Add(_title);
            names.Children.Add(_kind);
            foot.Children.Add(names);
            var footBar = new Border { Child = foot, BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, 1, 0, 0), Background = Chrome.BarBgLight };
            SetDock(footBar, Dock.Bottom);
            Children.Add(footBar);

            _text = new ComposedView { IsVisible = false, Tag = "split-text" };
            _text.Edited += delegate { RaiseEdited(); };
            _text.LinkClicked += delegate(string target) { var handler = LinkClicked; if (handler != null) handler(target); };
            _sheet = new SheetView { IsVisible = false, Tag = "split-sheet" };
            _sheet.Edited += delegate { RaiseEdited(); };
            _sheet.LinkClicked += delegate(string target) { var handler = LinkClicked; if (handler != null) handler(target); };
            _sheet.NavigateRequested += delegate(BinderItem target) { var handler = NavigateRequested; if (handler != null) handler(target); };
            _sheet.RenameRequested += delegate { _sheet.RefreshTitle(); };
            _sheet.BackShown = false; // « Retour » n'a pas où mener dans le volet, et son bandeau est étroit
            // Le caret du volet ne clignote que si le clavier est dans le volet (21/11, Rémi).
            _text.CaretHidden = delegate { return ForcedKeyboard.HasValue ? !ForcedKeyboard.Value : !_text.IsKeyboardFocusWithin; };
            _text.GotFocus += delegate { _text.RefreshCaretNow(); var handler = KeyboardMoved; if (handler != null) handler(); };
            _text.LostFocus += delegate { _text.RefreshCaretNow(); var handler = KeyboardMoved; if (handler != null) handler(); };
            _sheet.GotFocus += delegate { var handler = KeyboardMoved; if (handler != null) handler(); };
            _sheet.LostFocus += delegate { var handler = KeyboardMoved; if (handler != null) handler(); };

            _host = new Panel();
            _host.Children.Add(_text);
            _host.Children.Add(_sheet);
            Children.Add(new Border { Child = _host, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1, 0, 0, 0) });
            Ui.OnSizeChanged(_host, delegate { FitText(); });
        }

        private void RaiseEdited()
        {
            var handler = Edited;
            if (handler != null) handler();
        }

        /// <summary>Peut-on ouvrir cet élément dans le volet ? Un écrit ou une fiche.</summary>
        public static bool Accepts(BinderItem item)
        {
            return item != null && (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet) && !item.IsCategory;
        }

        /// <summary>Montre cet élément (un autre le remplace).</summary>
        public void Show(BinderItem item, Project project, HistoryManager history)
        {
            Commit();
            _project = project;
            Item = item;
            _title.Text = item.Title;
            if (item.Kind == ItemKind.Sheet)
            {
                _text.IsVisible = false;
                if (_text.HasItem) _text.Detach();
                var category = project.SheetCategoryOf(item);
                _kind.Text = category != null ? "Fiche " + project.CategoryPath(category) : "Fiche";
                _sheet.History = history;
                _sheet.SetProject(project);
                _sheet.SetStyleSheet(project.Styles);
                _sheet.LoadItem(item, project.TemplateOf(item));
                _sheet.IsVisible = true;
                return;
            }
            _sheet.IsVisible = false;
            if (_sheet.HasItem) _sheet.Clear();
            var book = item.EnclosingBook();
            _kind.Text = book != null ? "Écrit — " + book.Title : "Écrit";
            // Une page dynamique (table des matières, index…) se régénère : elle se lit, elle ne s'écrit pas.
            _text.ReadOnly = ExtraPages.IsDynamic(item);
            _text.LiveTypography = Settings.AppSettings.TypographyLiveEnabled ? Settings.AppSettings.TypographyLive : null;
            _text.AutoCapitalize = Settings.AppSettings.AutoCapitalize;
            _text.FolioOffset = 0;
            _text.Decor = null; // ni en-tête ni pied : le volet montre le texte, sur sa vraie page
            _page = item.Page ?? project.Page;
            _column = false;
            _setup = ChooseSetup();
            _text.Attach(item, project.Styles.EffectiveFor(item), _setup, project);
            _text.IsVisible = true;
            FitText();
        }

        private PageSetup _page;   // la vraie page de l'écrit
        private bool _column;      // le volet montre une colonne de lecture, pas la page

        /// <summary>En deçà, la vraie page serait trop réduite pour qu'on y travaille.</summary>
        public const double MinPageZoom = 0.85;

        /// <summary>LA PAGE OU LA COLONNE. Le volet montre la VRAIE page de
        /// l'écrit tant qu'elle tient à 85 % au moins dans sa largeur. Plus
        /// étroit, le texte deviendrait illisible : il passe en COLONNE — le
        /// même texte, les mêmes styles, sur une feuille étroite à petites
        /// marges, sans numéro de page. La pagination n'y est donc pas celle
        /// du livre ; elle l'est toujours à gauche.</summary>
        private PageSetup ChooseSetup()
        {
            var width = _host.Bounds.Width;
            _column = width > 0 && (width - 56) / Math.Max(100, _page.PageWidthPx) < MinPageZoom;
            if (!_column) return _page;
            var column = _page.Clone();
            column.PageWidthMm = 140;
            column.PageHeightMm = Math.Round(140 * 297.0 / 210.0, 1);
            column.MarginTopMm = 10;
            column.MarginBottomMm = 10;
            column.MarginLeftMm = 9;
            column.MarginRightMm = 9;
            column.Columns = 1;
            column.ShowMarginGuides = false;
            column.LineNumbers = false;
            column.FooterPageNumbers = false;
            return column;
        }

        /// <summary>La page (ou la colonne) tient dans la largeur du volet :
        /// réduite au besoin, jamais agrandie. Si le volet a assez changé de
        /// largeur pour passer de la page à la colonne, ou l'inverse, l'écrit
        /// est recomposé sur l'autre.</summary>
        private void FitText()
        {
            if (!_text.HasItem || _setup == null || _page == null || _host.Bounds.Width <= 0) return;
            var wanted = (_host.Bounds.Width - 56) / Math.Max(100, _page.PageWidthPx) < MinPageZoom;
            if (wanted != _column && Item != null && _project != null)
            {
                int paragraph, offset;
                _text.GetCaret(out paragraph, out offset);
                _setup = ChooseSetup();
                _text.Attach(Item, _project.Styles.EffectiveFor(Item), _setup, _project);
                _text.PlaceCaretForProbe(paragraph, offset);
            }
            var zoom = (_host.Bounds.Width - 56) / Math.Max(100, _setup.PageWidthPx);
            _text.SetZoom(Math.Max(0.5, Math.Min(1.0, zoom)));
        }

        internal bool ColumnForProbe { get { return _column; } }

        /// <summary>La feuille de styles, la page ou le texte ont changé ailleurs : le volet se recompose.</summary>
        public void Reload()
        {
            if (Item == null || _project == null) return;
            if (Item.Kind == ItemKind.Sheet) { _sheet.SetStyleSheet(_project.Styles); return; }
            if (!_text.HasItem) return;
            _page = Item.Page ?? _project.Page;
            _setup = ChooseSetup();
            _text.Attach(Item, _project.Styles.EffectiveFor(Item), _setup, _project);
            FitText();
        }

        public void RefreshTitle()
        {
            if (Item == null) return;
            _title.Text = Item.Title;
            if (Item.Kind == ItemKind.Sheet && _sheet.HasItem) _sheet.RefreshTitle();
        }

        /// <summary>Ce qu'une fiche garde en attente dans ses champs rejoint le modèle (avant un enregistrement).</summary>
        public void Commit()
        {
            if (_sheet.HasItem) _sheet.Commit();
        }

        public void Clear()
        {
            Commit();
            if (_text.HasItem) _text.Detach();
            if (_sheet.HasItem) _sheet.Clear();
            _text.IsVisible = false;
            _sheet.IsVisible = false;
            Item = null;
            _title.Text = "";
            _kind.Text = "";
        }

        /// <summary>Le clavier est-il dans le volet ? Ctrl+Z lui revient alors.</summary>
        public bool HasKeyboard { get { return Item != null && (ForcedKeyboard ?? IsKeyboardFocusWithin); } }
        /// <summary>Le clavier vient d'entrer dans le volet ou d'en sortir : l'autre côté revoit son caret.</summary>
        public event Action KeyboardMoved;
        /// <summary>Sonde : le clavier « est » (vrai) ou « n'est pas » (faux) dans le volet, sans vrai focus.</summary>
        internal bool? ForcedKeyboard;

        public bool TryUndo()
        {
            if (Item == null) return false;
            return Item.Kind == ItemKind.Sheet ? _sheet.TryUndo() : _text.HasItem && _text.Undo();
        }

        public bool TryRedo()
        {
            if (Item == null) return false;
            return Item.Kind == ItemKind.Sheet ? _sheet.TryRedo() : _text.HasItem && _text.Redo();
        }

        public void FocusContent()
        {
            if (Item == null) return;
            if (Item.Kind == ItemKind.Text) _text.FocusSurface(); else _sheet.Focus();
        }

        // — Sonde.
        internal ComposedView TextForProbe { get { return _text; } }
        internal SheetView SheetForProbe { get { return _sheet; } }
        internal Button CloseForProbe { get { return _close; } }
        internal string TitleForProbe { get { return _title.Text; } }
    }
}

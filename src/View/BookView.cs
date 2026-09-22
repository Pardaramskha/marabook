using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marabook.History;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>LA PAGE LIVRE (22/09) : cinq onglets. « Textes » (icône
    /// document) : le corkboard des écrits et les boutons d'ajout de pages ;
    /// « Édition » (icône livre) : métadonnées, couverture, présentation —
    /// l'ancien panneau Métadonnées et l'ancien panneau Édition du rail
    /// fondus, sans accordéon ; « Gabarits &amp; Format » (icône gabarit) :
    /// le format d'impression (format des pages, marges, fond perdu) et les
    /// gabarits de pages, « Nouveau gabarit » en bouton principal ;
    /// « Styles » (icône style de caractère) : l'outil de gestion des styles
    /// à la portée du livre et le séparateur de texte du livre ;
    /// « Publication » : Publier, la check-list et les tailles.</summary>
    public class BookView : Grid
    {
        private readonly TabControl _tabs;
        private readonly CorkboardView _texts, _templates;
        private readonly BookEditionTab _edition;
        private readonly BookFormatPanel _format;
        private readonly BookPublicationTab _publication;
        private readonly StackPanel _stylesHost;
        private readonly TabItem _stylesTab, _publicationTab;
        private BinderItem _item;
        private Project _project;
        private HistoryManager _history;
        private static int _lastTab; // l'onglet où l'on était, d'un livre à l'autre

        public event Action<BinderItem> Navigate;
        public event Action Changed;              // corkboard, métadonnées, format édités
        public event Action<BinderItem> ExportRequested;      // relais corkboard
        public event Action<BinderItem> DeleteRequested;
        public event Action<BinderItem> RenameRequested;      // (b43)
        public event Action<BinderItem, bool> CardImageRequested; // image de tuile / couverture : (élément, retirer)
        public event Action<List<BinderItem>> ApplyTemplateRequested;
        public event Action<BinderItem> NewTemplateRequested;    // book
        public event Action<BinderItem> ExportTemplateRequested; // gabarit
        public event Action<BinderItem> ImportTemplateRequested; // book
        public event Action<BinderItem> CopyTemplateRequested;   // gabarit
        public event Action<BinderItem, string> NewDocumentRequested; // livre, sorte extra
        public event Action<BinderItem> PublishRequested;        // « Publier… »
        public event Action<BinderItem> EpubRequested;           // « Créer un EPUB… » (22/09)
        public event Action StylesChanged;                       // l'onglet Styles a édité la feuille

        public BookView()
        {
            Focusable = true; // reçoit le focus logique quand la vue s'affiche
            Background = Chrome.WindowBg;

            _tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(0) };

            _texts = new CorkboardView { BookTemplates = false };
            Wire(_texts);
            _tabs.Items.Add(Tab("document", "Textes", _texts));

            _edition = new BookEditionTab();
            _edition.Changed += delegate { RaiseChanged(); };
            _edition.CoverRequested += delegate(BinderItem book, bool remove)
            { var h = CardImageRequested; if (h != null) h(book, remove); };
            _tabs.Items.Add(Tab("book-bold", "Édition", Scrolled(_edition)));

            var formatTab = new DockPanel();
            _format = new BookFormatPanel { Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 0) };
            _format.Changed += delegate
            {
                _texts.Refresh(); // les icônes d'alerte des tuiles
                RaiseChanged();
            };
            var formatSection = new StackPanel();
            formatSection.Children.Add(BookPanelParts.Caption("Format d'impression", 0));
            formatSection.Children.Add(_format);
            formatSection.Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 0);
            _format.Margin = new Thickness(0);
            DockPanel.SetDock(formatSection, Dock.Top);
            formatTab.Children.Add(formatSection);
            _templates = new CorkboardView { BookTexts = false };
            Wire(_templates);
            formatTab.Children.Add(_templates);
            _tabs.Items.Add(Tab("blueprint-bold", "Gabarits & Format", formatTab));

            _stylesHost = new StackPanel { Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 24) };
            _stylesTab = Tab("text-a-underline-bold", "Styles", Scrolled(_stylesHost));
            _tabs.Items.Add(_stylesTab);

            _publication = new BookPublicationTab();
            _publication.PublishRequested += delegate(BinderItem book)
            { var h = PublishRequested; if (h != null) h(book); };
            _publication.EpubRequested += delegate(BinderItem book)
            { var h = EpubRequested; if (h != null) h(book); };
            _publicationTab = Tab("book-open-text-bold", "Publication", Scrolled(_publication));
            _tabs.Items.Add(_publicationTab);

            _tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e)
            {
                if (!ReferenceEquals(e.Source, _tabs)) return;
                _lastTab = _tabs.SelectedIndex;
                if (_tabs.SelectedItem == _publicationTab) _publication.Refresh();
            };
            Children.Add(_tabs);
        }

        /// <summary>Un onglet du livre : l'en-tête (icône + libellé) suit la
        /// sélection — BLANC sur la pastille d'accent quand il est actif
        /// (22/09), encre sinon. Icons.Label fige sa couleur, le déclencheur
        /// du thème ne peut pas l'atteindre : l'en-tête est refait.</summary>
        private static TabItem Tab(string icon, string label, UIElement content)
        {
            var item = new TabItem
            {
                Header = Icons.Label(icon, label, 13, Chrome.Ink),
                Content = content
            };
            Action sync = delegate
            {
                item.Header = Icons.Label(icon, label, 13, item.IsSelected ? Brushes.White : Chrome.Ink);
            };
            item.AddHandler(System.Windows.Controls.Primitives.Selector.SelectedEvent, new RoutedEventHandler(delegate { sync(); }));
            item.AddHandler(System.Windows.Controls.Primitives.Selector.UnselectedEvent, new RoutedEventHandler(delegate { sync(); }));
            item.Loaded += delegate { sync(); };
            return item;
        }

        private static ScrollViewer Scrolled(UIElement content)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };
        }

        /// <summary>Les deux corkboards (textes, gabarits) relaient les mêmes événements.</summary>
        private void Wire(CorkboardView corkboard)
        {
            corkboard.Navigate += delegate(BinderItem item)
            {
                var handler = Navigate;
                if (handler != null) handler(item);
            };
            corkboard.Changed += delegate { RaiseChanged(); };
            corkboard.ExportRequested += delegate(BinderItem item)
            { var h = ExportRequested; if (h != null) h(item); };
            corkboard.DeleteRequested += delegate(BinderItem item)
            { var h = DeleteRequested; if (h != null) h(item); };
            corkboard.RenameRequested += delegate(BinderItem item)
            { var h = RenameRequested; if (h != null) h(item); };
            corkboard.CardImageRequested += delegate(BinderItem item, bool remove)
            { var h = CardImageRequested; if (h != null) h(item, remove); };
            corkboard.ApplyTemplateRequested += delegate(List<BinderItem> items)
            { var h = ApplyTemplateRequested; if (h != null) h(items); };
            corkboard.NewTemplateRequested += delegate(BinderItem book)
            { var h = NewTemplateRequested; if (h != null) h(book); };
            corkboard.ImportTemplateRequested += delegate(BinderItem book)
            { var h = ImportTemplateRequested; if (h != null) h(book); };
            corkboard.ExportTemplateRequested += delegate(BinderItem gabarit)
            { var h = ExportTemplateRequested; if (h != null) h(gabarit); };
            corkboard.CopyTemplateRequested += delegate(BinderItem gabarit)
            { var h = CopyTemplateRequested; if (h != null) h(gabarit); };
            corkboard.NewDocumentRequested += delegate(BinderItem book, string kind)
            { var h = NewDocumentRequested; if (h != null) h(book, kind); };
        }

        // ============================================================ lifecycle

        public void Load(BinderItem book, HistoryManager history, Project project)
        {
            var sameBook = ReferenceEquals(_item, book);
            _item = book;
            _project = project;
            _history = history;
            if (book.Book == null)
            {
                book.Book = new BookInfo();
                Defaults.Seed(book.Book); // éditeur et collection par défaut (Préférences › Auteur)
            }
            _texts.Load(book, history, project);
            _templates.Load(book, history, project);
            // Les formulaires ne se resynchronisent JAMAIS pendant la frappe
            // (le curseur sauterait) : au changement de livre seulement.
            if (!sameBook)
            {
                _edition.Load(book, project);
                _format.Load(book, project);
            }
            else _format.Sync();
            _publication.Load(book, project, PageCounter);
            BuildStylesTab();
            if (_tabs.SelectedIndex != _lastTab && _lastTab >= 0 && _lastTab < _tabs.Items.Count)
                _tabs.SelectedIndex = _lastTab;
            if (_tabs.SelectedItem == _publicationTab) _publication.Refresh();
        }

        public void Clear()
        {
            _item = null;
            _texts.Clear();
            _templates.Clear();
            _edition.Clear();
            _format.Clear();
            _publication.Clear();
        }

        public bool ShowsItem(BinderItem item) { return _item == item; }

        /// <summary>Le menu de la Pile pour les tuiles du livre (14/09) — les
        /// épingles y manquaient : le livre a son propre corkboard.</summary>
        public Func<BinderItem, ContextMenu> MenuProvider
        {
            set { _texts.MenuProvider = value; _templates.MenuProvider = value; }
        }

        /// <summary>Relais du compteur de pages vers le corkboard du livre
        /// (tri « Pages » des filtres, batch 28) et la check-list.</summary>
        public Func<BinderItem, int> PageCounter
        {
            get { return _texts.PageCounter; }
            set { _texts.PageCounter = value; _templates.PageCounter = value; }
        }

        /// <summary>Redessine les cartes du livre (état/couleur édités dans
        /// l'inspecteur pendant que la vue est affichée) et la couverture.</summary>
        public void RefreshCards()
        {
            _texts.Refresh();
            _templates.Refresh();
            _edition.RefreshCover();
            _format.Sync();
        }

        /// <summary>L'onglet courant (les sondes) : 0 Textes … 4 Publication.</summary>
        public int SelectedTab
        {
            get { return _tabs.SelectedIndex; }
            set { _tabs.SelectedIndex = value; }
        }

        // ============================================================ styles (22/09)

        /// <summary>L'onglet Styles : l'outil de gestion des styles sur la
        /// feuille du projet, à la portée de ce livre (les globaux et les
        /// siens), puis le séparateur de texte — le global, ou celui du livre
        /// s'il le remplace. Rebâti à chaque chargement : la feuille du projet
        /// peut avoir été remplacée par le dialogue des styles.</summary>
        private void BuildStylesTab()
        {
            _stylesHost.Children.Clear();
            if (_item == null || _project == null) return;
            var sheet = _project.Styles;
            _stylesHost.Children.Add(BookPanelParts.Caption("Styles de paragraphe", 0));
            _stylesHost.Children.Add(new TextBlock
            {
                Text = "Les styles globaux (Préférences) et ceux de ce livre ; un style neuf naît à la portée du livre, « Portée » en bas le change.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var panel = new StylesPanel(sheet, StyleScopeContext.ForBook(_project, _item)) { Height = 400 };
            panel.Changed += delegate { RaiseStylesChanged(); };
            _stylesHost.Children.Add(panel);

            _stylesHost.Children.Add(BookPanelParts.Caption("Séparateur de texte", 18));
            var separator = sheet.SeparatorFor(_item);
            var own = separator.Scope == ParagraphStyle.ScopeBook;
            var replace = new CheckBox
            {
                Content = "Ce livre remplace le séparateur global par le sien",
                IsChecked = own,
                Margin = new Thickness(0, 2, 0, 8)
            };
            var editor = new SeparatorEditor(separator) { IsEnabled = own };
            editor.Changed += delegate { RaiseStylesChanged(); };
            replace.Click += delegate
            {
                if (replace.IsChecked == true)
                {
                    var copy = sheet.SeparatorFor(null).Clone();
                    copy.Id = Guid.NewGuid().ToString("N");
                    copy.Name = "Séparateur — " + _item.Title;
                    copy.Scope = ParagraphStyle.ScopeBook;
                    copy.OwnerId = _item.Id;
                    sheet.Styles.Add(copy);
                    editor.Load(copy);
                    editor.IsEnabled = true;
                }
                else
                {
                    var mine = sheet.SeparatorFor(_item);
                    if (mine.Scope == ParagraphStyle.ScopeBook) sheet.Styles.Remove(mine);
                    editor.Load(sheet.SeparatorFor(_item));
                    editor.IsEnabled = false;
                }
                RaiseStylesChanged();
            };
            _stylesHost.Children.Add(replace);
            _stylesHost.Children.Add(new TextBlock
            {
                Text = own ? "Les séparateurs déjà insérés dans ce livre suivent ce format."
                    : "Le séparateur global (Préférences › Styles globaux) — en lecture, tant que ce livre ne le remplace pas.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            _stylesHost.Children.Add(editor);
        }

        private void RaiseStylesChanged()
        {
            var handler = StylesChanged;
            if (handler != null) handler();
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using UniversSale.History;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>The Book view: the corkboard of its children (reading order),
    /// full width. Les métadonnées et la publication (gabarit, fond perdu,
    /// « Publier… ») vivent depuis le batch 32 dans l'inspecteur, sous les
    /// dates, dépliées par leurs deux boutons (BookMetadataPanel,
    /// BookPublicationPanel).</summary>
    public class BookView : Grid
    {
        private readonly CorkboardView _corkboard;
        private BinderItem _item;

        public event Action<BinderItem> Navigate;
        public event Action Changed;              // corkboard edited
        public event Action<BinderItem> ExportRequested;      // relais corkboard
        public event Action<BinderItem> DeleteRequested;
        public event Action<BinderItem> RenameRequested;      // (b43)
        public event Action<BinderItem, bool> CardImageRequested; // image de tuile (12/09) : (élément, retirer)
        public event Action<System.Collections.Generic.List<BinderItem>> ApplyTemplateRequested;
        public event Action<BinderItem> NewTemplateRequested;    // book
        public event Action<BinderItem> ExportTemplateRequested; // gabarit
        public event Action<BinderItem> ImportTemplateRequested; // book
        public event Action<BinderItem> CopyTemplateRequested;   // gabarit
        public event Action<BinderItem, string> NewDocumentRequested; // livre, sorte extra

        public BookView()
        {
            Focusable = true; // reçoit le focus logique quand la vue s'affiche
            Background = Chrome.WindowBg;

            _corkboard = new CorkboardView();
            _corkboard.Navigate += delegate(BinderItem item)
            {
                var handler = Navigate;
                if (handler != null) handler(item);
            };
            _corkboard.Changed += delegate { RaiseChanged(); };
            _corkboard.ExportRequested += delegate(BinderItem item)
            { var h = ExportRequested; if (h != null) h(item); };
            _corkboard.DeleteRequested += delegate(BinderItem item)
            { var h = DeleteRequested; if (h != null) h(item); };
            _corkboard.RenameRequested += delegate(BinderItem item)
            { var h = RenameRequested; if (h != null) h(item); };
            _corkboard.CardImageRequested += delegate(BinderItem item, bool remove)
            { var h = CardImageRequested; if (h != null) h(item, remove); };
            _corkboard.ApplyTemplateRequested += delegate(System.Collections.Generic.List<BinderItem> items)
            { var h = ApplyTemplateRequested; if (h != null) h(items); };
            _corkboard.NewTemplateRequested += delegate(BinderItem book)
            { var h = NewTemplateRequested; if (h != null) h(book); };
            _corkboard.ImportTemplateRequested += delegate(BinderItem book)
            { var h = ImportTemplateRequested; if (h != null) h(book); };
            _corkboard.ExportTemplateRequested += delegate(BinderItem gabarit)
            { var h = ExportTemplateRequested; if (h != null) h(gabarit); };
            _corkboard.CopyTemplateRequested += delegate(BinderItem gabarit)
            { var h = CopyTemplateRequested; if (h != null) h(gabarit); };
            _corkboard.NewDocumentRequested += delegate(BinderItem book, string kind)
            { var h = NewDocumentRequested; if (h != null) h(book, kind); };
            Children.Add(_corkboard);
        }

        // ============================================================ lifecycle

        public void Load(BinderItem book, HistoryManager history, Project project)
        {
            _item = book;
            if (book.Book == null) book.Book = new BookInfo();
            _corkboard.Load(book, history, project);
        }

        public void Clear()
        {
            _item = null;
            _corkboard.Clear();
        }

        public bool ShowsItem(BinderItem item) { return _item == item; }

        /// <summary>Le menu de la Pile pour les tuiles du livre (14/09) — les
        /// épingles y manquaient : le livre a son propre corkboard.</summary>
        public Func<BinderItem, ContextMenu> MenuProvider
        {
            set { _corkboard.MenuProvider = value; }
        }

        /// <summary>Relais du compteur de pages vers le corkboard du livre
        /// (tri « Pages » des filtres, batch 28).</summary>
        public System.Func<BinderItem, int> PageCounter
        {
            set { _corkboard.PageCounter = value; }
        }

        /// <summary>Redessine les cartes du livre (état/couleur édités dans
        /// l'inspecteur pendant que la vue est affichée).</summary>
        public void RefreshCards()
        {
            _corkboard.Refresh();
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

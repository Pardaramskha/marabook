using System;
using System.Collections.Generic;

namespace UniversSale.Model
{
    public enum ItemKind
    {
        Category, // one of the four fixed roots of the Binder ("la Pile")
        Folder,
        Text,
        Sheet,    // a template-based wiki card (fiche)
        Media,    // an imported file (research material)
        Book,     // Écrits only: metadata + gabarit + « Publier »
        PageTemplate, // gabarit de pages d'un livre : deux pages vis-à-vis,
                      // en-têtes/pieds recto-verso, pastille de couleur
        Plan          // racine Plans : colonnes, éléments d'intensité, notes (batch 35)
    }

    /// <summary>Les états d'avancement d'un texte : clés stables persistées,
    /// libellés français, couleur de pastille.</summary>
    public static class TextStatus
    {
        public static readonly string[] Keys =
        { "todo", "draft", "revise", "correct", "beta", "done" };

        public static string Label(string key)
        {
            switch (key ?? "")
            {
                case "todo": return "À écrire";
                case "draft": return "Brouillon";
                case "revise": return "À réviser";
                case "correct": return "À corriger";
                case "beta": return "Bêta";
                case "done": return "Terminé";
                default: return "";
            }
        }

        /// <summary>"#RRGGBB" de la pastille d'état.</summary>
        public static string ColorOf(string key)
        {
            switch (key ?? "")
            {
                case "todo": return "#7F8C8D";
                case "draft": return "#2980B9";
                case "revise": return "#E67E22";
                case "correct": return "#C0392B";
                case "beta": return "#8E44AD";
                case "done": return "#27AE60";
                default: return "#7F8C8D";
            }
        }
    }

    /// <summary>A node of the Binder tree. Categories are fixed roots (cannot be
    /// renamed, moved or deleted). Text items hold a pivot TextDocument.</summary>
    public class BinderItem
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "";
        public ItemKind Kind = ItemKind.Text;
        public string CategoryKey; // categories only: "writings" | "research" | "sheets" | "trash"
        public string Synopsis = "";
        public string Notes = ""; // texts and sheets: working notes (corkboard cards show them first)
        public string Icon; // null = default; "glyph:<char>" preset or "file:<name>" custom (app icons folder)

        // État d'avancement du texte ("todo"|"draft"|"revise"|"correct"|
        // "beta"|"done", null = aucun) et couleur de carte au corkboard
        // ("#RRGGBB", null = neutre). Pour un DOSSIER de livre, la couleur
        // teinte sa boîte (bordure + fond éclairci).
        public string Status;
        public string CardColor;
        public TextDocument Document = new TextDocument(); // text items and sheet bodies

        // Sheets only: main image (wiki portrait), stored in the project image store.
        public string ImageId;

        // Sheet items only.
        public string TemplateId;
        public string CategoryId; // catégorie de fiches (batch 31), null = sans
        public Dictionary<string, string> FieldValues = new Dictionary<string, string>();
        public List<InfoEntry> FreeInfo = new List<InfoEntry>();
        public List<SheetRelation> Relations = new List<SheetRelation>(); // fiches (batch 34)

        // Media items only: raw bytes, written to the zip on save.
        public byte[] MediaBytes;
        public string MediaExtension; // includes the dot, e.g. ".png"

        // Books only: metadata + gabarit.
        public BookInfo Book;

        // Plans only (batch 35): colonnes, briques, liens.
        public PlanInfo Plan;

        // Texts only: the document's own page setup. Null = project default.
        // Documents created inside a book copy the book's gabarit here.
        public PageSetup Page;

        // Texts only: header/footer applied to every page (menu Gabarit de
        // l'éditeur), and the id of the applied page gabarit — which then
        // wins over Header/Footer with its recto/verso pairs.
        public HeaderFooter Header, Footer;
        public string PageTemplateId;

        // Pages extra (liminaires, TdM, page éditeur…) : hors du flux du
        // récit — sans folio par défaut, exclues de la table des matières.
        public bool IsExtraPage;
        public bool IsToc; // table des matières, régénérée dynamiquement

        // PageTemplate items only: color chip + the four recto/verso slots.
        public string TemplateColor; // "#RRGGBB", null = pas de pastille
        public HeaderFooter HeaderRecto, FooterRecto, HeaderVerso, FooterVerso;
        public double HeaderGapMm, FooterGapMm;      // écart au bloc de texte (0 = centré marge)
        public bool HeaderHideFirst, FooterHideFirst; // masqués sur la 1re page du document

        public List<BinderItem> Children = new List<BinderItem>();

        // Runtime only, rebuilt after load — never serialized.
        public BinderItem Parent;

        // Runtime only, set at load — never serialized. True when this item's
        // text entry was unreadable in the .plot: the document opened empty
        // (title preserved) instead of failing the whole project.
        public bool LoadDamaged;

        public bool IsCategory { get { return Kind == ItemKind.Category; } }

        /// <summary>Items that may hold children. Texts qualify (Scrivener
        /// model: a document can carry sub-documents); clicking one still opens
        /// the text — the corkboard is reserved to true containers.</summary>
        public bool CanHaveChildren
        {
            get
            {
                return Kind == ItemKind.Category || Kind == ItemKind.Folder
                    || Kind == ItemKind.Text || Kind == ItemKind.Book;
            }
        }

        /// <summary>True containers (category, folder, book): show the
        /// corkboard on click and receive new items created while selected.</summary>
        public bool IsContainer
        {
            get
            {
                return Kind == ItemKind.Category || Kind == ItemKind.Folder
                    || Kind == ItemKind.Book;
            }
        }

        /// <summary>The book this item lives in (itself included), or null.</summary>
        public BinderItem EnclosingBook()
        {
            var item = this;
            while (item != null)
            {
                if (item.Kind == ItemKind.Book) return item;
                item = item.Parent;
            }
            return null;
        }

        /// <summary>Everything searchable about this item, for the project-wide
        /// search and the reference scanner: title, synopsis, body, fields,
        /// free info, footnotes.</summary>
        public string SearchText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Title).Append('\n').Append(Synopsis).Append('\n').Append(Notes).Append('\n');
            if (Kind == ItemKind.Text || Kind == ItemKind.Sheet)
            {
                sb.Append(Document.ToPlainText()).Append('\n');
                foreach (var note in Document.Footnotes) sb.Append(note.Text).Append('\n');
            }
            if (Kind == ItemKind.Sheet)
            {
                foreach (var value in FieldValues.Values) sb.Append(value).Append('\n');
                foreach (var entry in FreeInfo)
                    sb.Append(entry.Title).Append('\n').Append(entry.Value).Append('\n');
            }
            return sb.ToString();
        }

        public bool IsDescendantOf(BinderItem other)
        {
            var p = Parent;
            while (p != null)
            {
                if (p == other) return true;
                p = p.Parent;
            }
            return false;
        }

        /// <summary>The category this item ultimately lives under (itself if a category).</summary>
        public BinderItem RootCategory()
        {
            var item = this;
            while (item.Parent != null) item = item.Parent;
            return item;
        }

        public void RelinkChildren()
        {
            foreach (var child in Children)
            {
                child.Parent = this;
                child.RelinkChildren();
            }
        }
    }
}

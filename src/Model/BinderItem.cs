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
        Media     // an imported file (research material)
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
        public TextDocument Document = new TextDocument(); // text items and sheet bodies

        // Sheets only: main image (wiki portrait), stored in the project image store.
        public string ImageId;

        // Sheet items only.
        public string TemplateId;
        public Dictionary<string, string> FieldValues = new Dictionary<string, string>();
        public List<InfoEntry> FreeInfo = new List<InfoEntry>();

        // Media items only: raw bytes, written to the zip on save.
        public byte[] MediaBytes;
        public string MediaExtension; // includes the dot, e.g. ".png"

        public List<BinderItem> Children = new List<BinderItem>();

        // Runtime only, rebuilt after load — never serialized.
        public BinderItem Parent;

        public bool IsCategory { get { return Kind == ItemKind.Category; } }

        /// <summary>Items that may hold children. Texts qualify (Scrivener
        /// model: a document can carry sub-documents); clicking one still opens
        /// the text — the corkboard is reserved to true containers.</summary>
        public bool CanHaveChildren
        {
            get
            {
                return Kind == ItemKind.Category || Kind == ItemKind.Folder
                    || Kind == ItemKind.Text;
            }
        }

        /// <summary>True containers (category, folder): show the corkboard on
        /// click and receive new items created while they are selected.</summary>
        public bool IsContainer
        {
            get { return Kind == ItemKind.Category || Kind == ItemKind.Folder; }
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

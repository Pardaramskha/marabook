using System;
using System.Collections.Generic;

namespace UniversSale.Model
{
    /// <summary>A .plot project: metadata plus the Binder tree, whose four fixed
    /// roots are the categories Écrits / Recherche / Fiches / Corbeille.</summary>
    public class Project
    {
        public const string KeyWritings = "writings";
        public const string KeyResearch = "research";
        public const string KeySheets = "sheets";
        public const string KeyTrash = "trash";

        public string Name = "Sans titre";
        public string Author = "";
        public string CreatedAt = "";
        public string ModifiedAt = "";
        public StyleSheet Styles = StyleSheet.CreateDefault();
        public List<SheetTemplate> Templates = SheetTemplate.CreateDefaults();
        public List<BinderItem> Roots = new List<BinderItem>();

        public SheetTemplate FindTemplate(string id)
        {
            foreach (var template in Templates)
                if (template.Id == id) return template;
            return null;
        }

        /// <summary>First item whose title matches (case- and accent-insensitive),
        /// for [[wiki link]] navigation. Sheets win over other kinds.</summary>
        public BinderItem FindByTitle(string title)
        {
            BinderItem fallback = null;
            foreach (var item in AllItems())
            {
                if (item.IsCategory) continue;
                if (string.Compare(item.Title, title,
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Globalization.CompareOptions.IgnoreCase
                    | System.Globalization.CompareOptions.IgnoreNonSpace) != 0) continue;
                if (item.Kind == ItemKind.Sheet) return item;
                if (fallback == null) fallback = item;
            }
            return fallback;
        }

        public static Project CreateNew()
        {
            var project = new Project();
            project.Roots.Add(MakeCategory("Écrits", KeyWritings));
            project.Roots.Add(MakeCategory("Recherche", KeyResearch));
            project.Roots.Add(MakeCategory("Fiches", KeySheets));
            project.Roots.Add(MakeCategory("Corbeille", KeyTrash));

            var first = new BinderItem();
            first.Kind = ItemKind.Text;
            first.Title = "Nouvel écrit";
            var writings = project.Category(KeyWritings);
            first.Parent = writings;
            writings.Children.Add(first);
            return project;
        }

        private static BinderItem MakeCategory(string title, string key)
        {
            var category = new BinderItem();
            category.Kind = ItemKind.Category;
            category.Title = title;
            category.CategoryKey = key;
            category.Id = key; // stable id: categories are addressable across versions
            return category;
        }

        public BinderItem Category(string key)
        {
            foreach (var root in Roots)
                if (root.CategoryKey == key) return root;
            return null;
        }

        public BinderItem Trash { get { return Category(KeyTrash); } }

        public BinderItem FindById(string id)
        {
            foreach (var item in AllItems())
                if (item.Id == id) return item;
            return null;
        }

        public IEnumerable<BinderItem> AllItems()
        {
            var queue = new Queue<BinderItem>(Roots);
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                yield return item;
                foreach (var child in item.Children) queue.Enqueue(child);
            }
        }

        /// <summary>Rebuilds Parent pointers after deserialization.</summary>
        public void RelinkParents()
        {
            foreach (var root in Roots)
            {
                root.Parent = null;
                root.RelinkChildren();
            }
        }
    }
}

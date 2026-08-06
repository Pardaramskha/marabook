using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UniversSale.Model;

namespace UniversSale.Exchange
{
    /// <summary>Scrivener project import. A .scriv is a folder: project.scrivx
    /// (XML binder tree) plus per-item RTF content. Scrivener 3 stores
    /// Files/Data/&lt;UUID&gt;/content.rtf + synopsis.txt; Scrivener 1/2 used
    /// Files/Docs/&lt;ID&gt;.rtf + &lt;ID&gt;_synopsis.txt — both are handled.
    /// DraftFolder lands in Écrits, ResearchFolder in Recherche, TrashFolder is
    /// skipped; non-RTF research files become media items.</summary>
    public static class Scrivener
    {
        public const string Filter = "Projet Scrivener (*.scrivx)|*.scrivx";

        public static Project Import(string scrivxPath)
        {
            var root = Path.GetDirectoryName(scrivxPath);
            var xml = new XmlDocument();
            xml.Load(scrivxPath);

            var project = Project.CreateNew();
            // Drop the starter text: this project is born from a migration.
            project.Category(Project.KeyWritings).Children.Clear();
            project.Name = Path.GetFileNameWithoutExtension(scrivxPath);

            var binder = xml.SelectSingleNode("//Binder");
            if (binder == null)
                throw new InvalidDataException("Pas de nœud <Binder> : projet Scrivener illisible.");

            foreach (XmlNode node in binder.ChildNodes)
            {
                if (node.LocalName != "BinderItem") continue;
                var type = AttrValue(node, "Type");
                if (type == "TrashFolder") continue;
                BinderItem destination;
                if (type == "DraftFolder") destination = project.Category(Project.KeyWritings);
                else if (type == "ResearchFolder") destination = project.Category(Project.KeyResearch);
                else destination = project.Category(Project.KeyWritings);

                if (type == "DraftFolder" || type == "ResearchFolder")
                {
                    // Unwrap the container: import its children directly.
                    var children = node.SelectSingleNode("Children");
                    if (children != null)
                        foreach (XmlNode child in children.ChildNodes)
                            AppendItem(child, destination, root, project);
                }
                else
                    AppendItem(node, destination, root, project);
            }
            project.RelinkParents();
            return project;
        }

        private static string AttrValue(XmlNode node, string name)
        {
            if (node.Attributes == null) return null;
            var attr = node.Attributes[name];
            return attr == null ? null : attr.Value;
        }

        private static void AppendItem(XmlNode node, BinderItem parent, string root, Project project)
        {
            if (node.LocalName != "BinderItem") return;
            var type = AttrValue(node, "Type");
            var uuid = AttrValue(node, "UUID");   // Scrivener 3
            var id = AttrValue(node, "ID");       // Scrivener 1/2
            var titleNode = node.SelectSingleNode("Title");
            var title = titleNode == null ? "(sans titre)" : titleNode.InnerText;

            var children = node.SelectSingleNode("Children");
            var hasChildren = children != null && children.ChildNodes.Count > 0;

            BinderItem item;
            if (type == "Folder")
            {
                item = new BinderItem { Kind = ItemKind.Folder, Title = title };
                // A Scrivener folder can carry its own text: imported as a
                // first child to keep the content.
                var ownText = LoadContent(root, uuid, id, project);
                if (ownText != null)
                {
                    var own = new BinderItem { Kind = ItemKind.Text, Title = title, Document = ownText };
                    own.Parent = item;
                    item.Children.Add(own);
                }
            }
            else if (hasChildren)
            {
                // A text with children stays a text (our documents carry
                // sub-documents too since v0.6) — clicking it opens the text.
                item = new BinderItem
                {
                    Kind = ItemKind.Text,
                    Title = title,
                    Document = LoadContent(root, uuid, id, project)
                        ?? new TextDocument { Paragraphs = { new TextParagraph() } }
                };
            }
            else
            {
                var media = TryLoadMedia(root, uuid, id);
                if (media != null)
                {
                    item = media;
                    item.Title = title;
                }
                else
                {
                    item = new BinderItem
                    {
                        Kind = ItemKind.Text,
                        Title = title,
                        Document = LoadContent(root, uuid, id, project)
                            ?? new TextDocument { Paragraphs = { new TextParagraph() } }
                    };
                }
            }
            item.Synopsis = LoadSynopsis(root, uuid, id) ?? "";
            item.Parent = parent;
            parent.Children.Add(item);

            if (children != null)
                foreach (XmlNode child in children.ChildNodes)
                    AppendItem(child, item.CanHaveChildren ? item : parent, root, project);
        }

        private static TextDocument LoadContent(string root, string uuid, string id, Project project)
        {
            var rtfPath = uuid != null
                ? Path.Combine(root, "Files", "Data", uuid, "content.rtf")
                : id != null ? Path.Combine(root, "Files", "Docs", id + ".rtf") : null;
            if (rtfPath == null || !File.Exists(rtfPath)) return null;
            try
            {
                using (var stream = new FileStream(rtfPath, FileMode.Open, FileAccess.Read))
                    return Rtf.ImportStream(stream, project.Styles);
            }
            catch
            {
                return null;
            }
        }

        private static string LoadSynopsis(string root, string uuid, string id)
        {
            var path = uuid != null
                ? Path.Combine(root, "Files", "Data", uuid, "synopsis.txt")
                : id != null ? Path.Combine(root, "Files", "Docs", id + "_synopsis.txt") : null;
            if (path == null || !File.Exists(path)) return null;
            try { return File.ReadAllText(path).Trim(); }
            catch { return null; }
        }

        /// <summary>Research items that are not RTF (images, PDFs…): Scrivener 3
        /// stores them as Files/Data/&lt;UUID&gt;/content.&lt;ext&gt;.</summary>
        private static BinderItem TryLoadMedia(string root, string uuid, string id)
        {
            string folder = null;
            if (uuid != null) folder = Path.Combine(root, "Files", "Data", uuid);
            if (folder == null || !Directory.Exists(folder)) return null;
            foreach (var file in Directory.GetFiles(folder, "content.*"))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".rtf" || ext == ".txt" || ext == ".styles" || ext == ".comments")
                    continue;
                try
                {
                    return new BinderItem
                    {
                        Kind = ItemKind.Media,
                        MediaExtension = ext,
                        MediaBytes = File.ReadAllBytes(file)
                    };
                }
                catch { return null; }
            }
            return null;
        }
    }
}

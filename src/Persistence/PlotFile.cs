using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UniversSale.Model;

namespace UniversSale.Persistence
{
    /// <summary>Reads/writes .plot files: a ZIP archive holding manifest.json
    /// (version, metadata, Binder tree), styles.json (the project style sheet)
    /// and one texts/&lt;id&gt;.json pivot document per text item.
    /// v1 stored plain .txt texts and no styles — still loads forever.
    /// Doctrine inherited from Mental-o's .tea: integer version field, perpetual
    /// backward compatibility, atomic writes with a rolling .bak.</summary>
    public static class PlotFile
    {
        public const string Extension = ".plot";
        public const string OpenFilter = "Projets Univers Sale (*.plot)|*.plot|Tous les fichiers (*.*)|*.*";
        public const string SaveFilter = "Projet Univers Sale (*.plot)|*.plot|Tous les fichiers (*.*)|*.*";
        // v2: pivot + styles; v3: sheets, templates, media;
        // v4: notes, per-item icons, image store, lists, page breaks, page setup.
        private const int FormatVersion = 4;

        // ------------------------------------------------------- writing

        public static void Save(Project project, string path)
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (string.IsNullOrEmpty(project.CreatedAt)) project.CreatedAt = now;
            project.ModifiedAt = now;

            // Write to a temporary file first, then swap in atomically so a crash
            // mid-save can never corrupt the project. The previous version becomes .bak.
            var tempPath = path + ".tmp";
            project.PurgeUnusedImages();
            using (var stream = new FileStream(tempPath, FileMode.Create))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.json", Json.Write(BuildManifest(project)));
                WriteEntry(archive, "styles.json", Json.Write(BuildStyles(project.Styles)));
                WriteEntry(archive, "sheets/templates.json", Json.Write(BuildTemplates(project.Templates)));
                foreach (var kv in project.Images)
                {
                    if (kv.Value.Bytes == null) continue;
                    var imageEntry = archive.CreateEntry("images/" + kv.Key + (kv.Value.Extension ?? ""));
                    using (var imageStream = imageEntry.Open())
                        imageStream.Write(kv.Value.Bytes, 0, kv.Value.Bytes.Length);
                }
                foreach (var item in project.AllItems())
                {
                    if (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet)
                        WriteEntry(archive, "texts/" + item.Id + ".json",
                            Json.Write(BuildDocument(item.Document)));
                    if (item.Kind == ItemKind.Media && item.MediaBytes != null)
                    {
                        var media = archive.CreateEntry("research/" + item.Id + (item.MediaExtension ?? ""));
                        using (var mediaStream = media.Open())
                            mediaStream.Write(item.MediaBytes, 0, item.MediaBytes.Length);
                    }
                }
            }

            if (File.Exists(path))
                File.Replace(tempPath, path, path + ".bak");
            else
                File.Move(tempPath, path);
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        private static Dictionary<string, object> BuildManifest(Project project)
        {
            var manifest = new Dictionary<string, object>();
            manifest["version"] = FormatVersion;
            manifest["name"] = project.Name;
            manifest["author"] = project.Author;
            manifest["separatorText"] = project.SeparatorText;
            if (project.SeparatorFont != null) manifest["separatorFont"] = project.SeparatorFont;
            manifest["separatorSizePt"] = project.SeparatorSizePt;
            if (project.CustomColors.Count > 0)
                manifest["customColors"] = new List<object>(project.CustomColors.ToArray());
            manifest["createdAt"] = project.CreatedAt;
            manifest["modifiedAt"] = project.ModifiedAt;
            manifest["page"] = BuildPageSetup(project.Page);
            var roots = new List<object>();
            foreach (var root in project.Roots) roots.Add(BuildNode(root));
            manifest["binder"] = roots;
            return manifest;
        }

        private static Dictionary<string, object> BuildPageSetup(PageSetup page)
        {
            var p = new Dictionary<string, object>();
            p["widthMm"] = page.PageWidthMm;
            p["heightMm"] = page.PageHeightMm;
            p["marginTopMm"] = page.MarginTopMm;
            p["marginBottomMm"] = page.MarginBottomMm;
            p["marginLeftMm"] = page.MarginLeftMm;
            p["marginRightMm"] = page.MarginRightMm;
            p["columns"] = (double)page.Columns;
            p["showMargins"] = page.ShowMarginGuides;
            p["lineNumbers"] = page.LineNumbers;
            p["hyphenation"] = page.Hyphenation;
            p["footerNumbers"] = page.FooterPageNumbers;
            p["footerFont"] = page.FooterFont;
            p["footerSizePt"] = page.FooterSizePt;
            return p;
        }

        private static Dictionary<string, object> BuildNode(BinderItem item)
        {
            var node = new Dictionary<string, object>();
            node["id"] = item.Id;
            node["title"] = item.Title;
            node["kind"] = item.Kind == ItemKind.Category ? "category"
                         : item.Kind == ItemKind.Folder ? "folder"
                         : item.Kind == ItemKind.Sheet ? "sheet"
                         : item.Kind == ItemKind.Media ? "media" : "text";
            if (item.CategoryKey != null) node["category"] = item.CategoryKey;
            if (!string.IsNullOrEmpty(item.Synopsis)) node["synopsis"] = item.Synopsis;
            if (!string.IsNullOrEmpty(item.Notes)) node["notes"] = item.Notes;
            if (item.Icon != null) node["icon"] = item.Icon;
            if (item.ImageId != null) node["image"] = item.ImageId;
            if (item.Kind == ItemKind.Sheet)
            {
                if (item.TemplateId != null) node["template"] = item.TemplateId;
                if (item.FieldValues.Count > 0)
                {
                    var fields = new Dictionary<string, object>();
                    foreach (var kv in item.FieldValues)
                        if (!string.IsNullOrEmpty(kv.Value)) fields[kv.Key] = kv.Value;
                    if (fields.Count > 0) node["fields"] = fields;
                }
                if (item.FreeInfo.Count > 0)
                {
                    var info = new List<object>();
                    foreach (var entry in item.FreeInfo)
                    {
                        var e = new Dictionary<string, object>();
                        e["id"] = entry.Id;
                        e["title"] = entry.Title;
                        e["value"] = entry.Value;
                        info.Add(e);
                    }
                    node["info"] = info;
                }
            }
            if (item.Kind == ItemKind.Media && item.MediaExtension != null)
                node["mediaExt"] = item.MediaExtension;
            if (item.Children.Count > 0)
            {
                var children = new List<object>();
                foreach (var child in item.Children) children.Add(BuildNode(child));
                node["children"] = children;
            }
            return node;
        }

        private static Dictionary<string, object> BuildStyles(StyleSheet sheet)
        {
            var root = new Dictionary<string, object>();
            var list = new List<object>();
            foreach (var style in sheet.Styles)
            {
                var s = new Dictionary<string, object>();
                s["id"] = style.Id;
                s["name"] = style.Name;
                s["font"] = style.FontFamily;
                s["size"] = style.FontSize;
                if (style.Bold) s["bold"] = true;
                if (style.Italic) s["italic"] = true;
                if (style.Color != null) s["color"] = style.Color;
                s["align"] = style.Align;
                if (style.SpaceBefore != 0) s["spaceBefore"] = style.SpaceBefore;
                if (style.SpaceAfter != 0) s["spaceAfter"] = style.SpaceAfter;
                if (style.FirstLineIndent != 0) s["firstIndent"] = style.FirstLineIndent;
                if (style.LeftIndent != 0) s["leftIndent"] = style.LeftIndent;
                if (style.RightIndent != 0) s["rightIndent"] = style.RightIndent;
                if (style.LastLineIndent != 0) s["lastIndent"] = style.LastLineIndent;
                s["lineHeight"] = style.LineHeight;
                if (!style.Ligatures) s["ligatures"] = false;
                if (!style.HyphenationEnabled) s["hyphen"] = false;
                if (style.HyphenMinWordLength != 5) s["hyphenWord"] = (double)style.HyphenMinWordLength;
                if (style.HyphenMinBefore != 2) s["hyphenBefore"] = (double)style.HyphenMinBefore;
                if (style.HyphenMinAfter != 2) s["hyphenAfter"] = (double)style.HyphenMinAfter;
                if (style.HyphenConsecutiveLimit != 3) s["hyphenLimit"] = (double)style.HyphenConsecutiveLimit;
                if (style.JustifyWordMin != 80) s["jWordMin"] = style.JustifyWordMin;
                if (style.JustifyWordOpt != 100) s["jWordOpt"] = style.JustifyWordOpt;
                if (style.JustifyWordMax != 115) s["jWordMax"] = style.JustifyWordMax;
                if (style.JustifyLetterMin != 0) s["jLetterMin"] = style.JustifyLetterMin;
                if (style.JustifyLetterOpt != 0) s["jLetterOpt"] = style.JustifyLetterOpt;
                if (style.JustifyLetterMax != 0) s["jLetterMax"] = style.JustifyLetterMax;
                if (style.JustifyGlyphMin != 100) s["jGlyphMin"] = style.JustifyGlyphMin;
                if (style.JustifyGlyphOpt != 100) s["jGlyphOpt"] = style.JustifyGlyphOpt;
                if (style.JustifyGlyphMax != 100) s["jGlyphMax"] = style.JustifyGlyphMax;
                if (style.AutoLeadingPercent != 120) s["autoLeading"] = style.AutoLeadingPercent;
                if (!style.KeepWithPrevious) s["keepPrev"] = false;
                if (style.KeepNextLines != 0) s["keepNext"] = (double)style.KeepNextLines;
                if (style.KeepLinesTogether) s["keepLines"] = true;
                list.Add(s);
            }
            root["styles"] = list;
            return root;
        }

        private static Dictionary<string, object> BuildTemplates(List<SheetTemplate> templates)
        {
            var root = new Dictionary<string, object>();
            var list = new List<object>();
            foreach (var template in templates)
            {
                var t = new Dictionary<string, object>();
                t["id"] = template.Id;
                t["name"] = template.Name;
                var fields = new List<object>();
                foreach (var field in template.Fields)
                {
                    var f = new Dictionary<string, object>();
                    f["id"] = field.Id;
                    f["name"] = field.Name;
                    if (field.Kind != "text") f["kind"] = field.Kind;
                    fields.Add(f);
                }
                t["fields"] = fields;
                list.Add(t);
            }
            root["templates"] = list;
            return root;
        }

        private static Dictionary<string, object> BuildDocument(TextDocument document)
        {
            var root = new Dictionary<string, object>();
            var paragraphs = new List<object>();
            foreach (var paragraph in document.Paragraphs)
            {
                var p = new Dictionary<string, object>();
                if (paragraph.StyleId != "body") p["style"] = paragraph.StyleId;
                if (paragraph.AlignOverride != null) p["align"] = paragraph.AlignOverride;
                if (paragraph.ListKind != null) p["list"] = paragraph.ListKind;
                if (paragraph.PageBreakBefore) p["pb"] = true;
                var runs = new List<object>();
                foreach (var run in paragraph.Runs)
                {
                    var r = new Dictionary<string, object>();
                    if (run.IsLineBreak) { r["br"] = true; runs.Add(r); continue; }
                    if (run.FootnoteId != null) { r["fn"] = run.FootnoteId; runs.Add(r); continue; }
                    if (run.ImageId != null) { r["img"] = run.ImageId; runs.Add(r); continue; }
                    if (run.IsRule) { r["hr"] = true; runs.Add(r); continue; }
                    r["t"] = run.Text;
                    if (run.Bold.HasValue) r["b"] = run.Bold.Value;
                    if (run.Italic.HasValue) r["i"] = run.Italic.Value;
                    if (run.Underline.HasValue) r["u"] = run.Underline.Value;
                    if (run.Strike.HasValue) r["st"] = run.Strike.Value;
                    if (run.Weight != null) r["w"] = run.Weight;
                    if (run.FontFamily != null) r["font"] = run.FontFamily;
                    if (run.FontSize.HasValue) r["size"] = run.FontSize.Value;
                    if (run.Color != null) r["color"] = run.Color;
                    if (run.Highlight != null) r["hl"] = run.Highlight;
                    runs.Add(r);
                }
                p["runs"] = runs;
                paragraphs.Add(p);
            }
            root["paragraphs"] = paragraphs;
            if (document.Footnotes.Count > 0)
            {
                var notes = new List<object>();
                foreach (var note in document.Footnotes)
                {
                    var n = new Dictionary<string, object>();
                    n["id"] = note.Id;
                    n["text"] = note.Text;
                    notes.Add(n);
                }
                root["footnotes"] = notes;
            }
            return root;
        }

        // ------------------------------------------------------- reading

        public static Project Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry == null)
                    throw new InvalidDataException(
                        "Le fichier ne contient pas de manifest.json : ce n'est pas un projet Univers Sale valide.");

                var manifest = Json.AsObject(Json.Parse(ReadEntry(manifestEntry)));
                if (manifest == null)
                    throw new InvalidDataException("Le manifeste du projet est illisible.");

                var project = new Project();
                project.Name = Json.AsString(Json.Field(manifest, "name")) ?? "Sans titre";
                project.Author = Json.AsString(Json.Field(manifest, "author")) ?? "";
                project.SeparatorText = Json.AsString(Json.Field(manifest, "separatorText")) ?? "***";
                project.SeparatorFont = Json.AsString(Json.Field(manifest, "separatorFont"));
                project.SeparatorSizePt = Json.AsDouble(Json.Field(manifest, "separatorSizePt"), 12);
                var customColors = Json.AsList(Json.Field(manifest, "customColors"));
                if (customColors != null)
                    foreach (var entry in customColors)
                        if (entry is string) project.CustomColors.Add((string)entry);
                project.CreatedAt = Json.AsString(Json.Field(manifest, "createdAt")) ?? "";
                project.ModifiedAt = Json.AsString(Json.Field(manifest, "modifiedAt")) ?? "";

                var stylesEntry = archive.GetEntry("styles.json");
                if (stylesEntry != null)
                    project.Styles = ReadStyles(ReadEntry(stylesEntry));

                var templatesEntry = archive.GetEntry("sheets/templates.json");
                if (templatesEntry != null)
                    project.Templates = ReadTemplates(ReadEntry(templatesEntry));

                var page = Json.AsObject(Json.Field(manifest, "page"));
                if (page != null) project.Page = ReadPageSetup(page);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith("images/", StringComparison.Ordinal)) continue;
                    var file = entry.FullName.Substring("images/".Length);
                    var dot = file.IndexOf('.');
                    var id = dot < 0 ? file : file.Substring(0, dot);
                    if (id.Length == 0) continue;
                    using (var imageStream = entry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        imageStream.CopyTo(buffer);
                        project.Images[id] = new ProjectImage
                        {
                            Bytes = buffer.ToArray(),
                            Extension = dot < 0 ? "" : file.Substring(dot)
                        };
                    }
                }

                var roots = Json.AsList(Json.Field(manifest, "binder"));
                if (roots != null)
                    foreach (var root in roots)
                    {
                        var item = ReadNode(root, archive);
                        if (item != null) project.Roots.Add(item);
                    }

                // A valid project always has its four categories, whatever the file says.
                EnsureCategory(project, "Écrits", Project.KeyWritings);
                EnsureCategory(project, "Recherche", Project.KeyResearch);
                EnsureCategory(project, "Fiches", Project.KeySheets);
                EnsureCategory(project, "Corbeille", Project.KeyTrash);

                project.RelinkParents();
                return project;
            }
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static StyleSheet ReadStyles(string json)
        {
            var sheet = new StyleSheet();
            var list = Json.AsList(Json.Field(Json.Parse(json), "styles"));
            if (list != null)
                foreach (var entry in list)
                {
                    var s = Json.AsObject(entry);
                    if (s == null) continue;
                    var style = new ParagraphStyle();
                    var id = Json.AsString(Json.Field(s, "id"));
                    if (!string.IsNullOrEmpty(id)) style.Id = id;
                    style.Name = Json.AsString(Json.Field(s, "name")) ?? "Style";
                    style.FontFamily = Json.AsString(Json.Field(s, "font")) ?? "Georgia";
                    style.FontSize = Json.AsDouble(Json.Field(s, "size"), 15);
                    style.Bold = Json.AsBool(Json.Field(s, "bold"), false);
                    style.Italic = Json.AsBool(Json.Field(s, "italic"), false);
                    style.Color = Json.AsString(Json.Field(s, "color"));
                    style.Align = Json.AsString(Json.Field(s, "align")) ?? "left";
                    style.SpaceBefore = Json.AsDouble(Json.Field(s, "spaceBefore"), 0);
                    style.SpaceAfter = Json.AsDouble(Json.Field(s, "spaceAfter"), 0);
                    style.FirstLineIndent = Json.AsDouble(Json.Field(s, "firstIndent"), 0);
                    style.LeftIndent = Json.AsDouble(Json.Field(s, "leftIndent"), 0);
                    style.RightIndent = Json.AsDouble(Json.Field(s, "rightIndent"), 0);
                    style.LastLineIndent = Json.AsDouble(Json.Field(s, "lastIndent"), 0);
                    // v4 files carry no lineHeight: 0 = "auto" (WPF default),
                    // preserving their look; new sheets get the 14.4 pt default.
                    style.LineHeight = Json.AsDouble(Json.Field(s, "lineHeight"), 0);
                    style.Ligatures = Json.AsBool(Json.Field(s, "ligatures"), true);
                    style.HyphenationEnabled = Json.AsBool(Json.Field(s, "hyphen"), true);
                    style.HyphenMinWordLength = (int)Json.AsDouble(Json.Field(s, "hyphenWord"), 5);
                    style.HyphenMinBefore = (int)Json.AsDouble(Json.Field(s, "hyphenBefore"), 2);
                    style.HyphenMinAfter = (int)Json.AsDouble(Json.Field(s, "hyphenAfter"), 2);
                    style.HyphenConsecutiveLimit = (int)Json.AsDouble(Json.Field(s, "hyphenLimit"), 3);
                    style.JustifyWordMin = Json.AsDouble(Json.Field(s, "jWordMin"), 80);
                    style.JustifyWordOpt = Json.AsDouble(Json.Field(s, "jWordOpt"), 100);
                    style.JustifyWordMax = Json.AsDouble(Json.Field(s, "jWordMax"), 115);
                    style.JustifyLetterMin = Json.AsDouble(Json.Field(s, "jLetterMin"), 0);
                    style.JustifyLetterOpt = Json.AsDouble(Json.Field(s, "jLetterOpt"), 0);
                    style.JustifyLetterMax = Json.AsDouble(Json.Field(s, "jLetterMax"), 0);
                    style.JustifyGlyphMin = Json.AsDouble(Json.Field(s, "jGlyphMin"), 100);
                    style.JustifyGlyphOpt = Json.AsDouble(Json.Field(s, "jGlyphOpt"), 100);
                    style.JustifyGlyphMax = Json.AsDouble(Json.Field(s, "jGlyphMax"), 100);
                    style.AutoLeadingPercent = Json.AsDouble(Json.Field(s, "autoLeading"), 120);
                    style.KeepWithPrevious = Json.AsBool(Json.Field(s, "keepPrev"), true);
                    style.KeepNextLines = (int)Json.AsDouble(Json.Field(s, "keepNext"), 0);
                    style.KeepLinesTogether = Json.AsBool(Json.Field(s, "keepLines"), false);
                    sheet.Styles.Add(style);
                }
            if (sheet.Styles.Count == 0) return StyleSheet.CreateDefault();
            if (sheet.Find("body") == null) // pathological file: still guarantee a body
                sheet.Styles.Insert(0, StyleSheet.CreateDefault().Body);
            return sheet;
        }

        private static PageSetup ReadPageSetup(Dictionary<string, object> p)
        {
            var page = new PageSetup();
            page.PageWidthMm = Json.AsDouble(Json.Field(p, "widthMm"), 210);
            page.PageHeightMm = Json.AsDouble(Json.Field(p, "heightMm"), 297);
            page.MarginTopMm = Json.AsDouble(Json.Field(p, "marginTopMm"), 25);
            page.MarginBottomMm = Json.AsDouble(Json.Field(p, "marginBottomMm"), 25);
            page.MarginLeftMm = Json.AsDouble(Json.Field(p, "marginLeftMm"), 25);
            page.MarginRightMm = Json.AsDouble(Json.Field(p, "marginRightMm"), 25);
            page.Columns = (int)Json.AsDouble(Json.Field(p, "columns"), 1);
            if (page.Columns < 1) page.Columns = 1;
            if (page.Columns > 3) page.Columns = 3;
            page.ShowMarginGuides = Json.AsBool(Json.Field(p, "showMargins"), true);
            page.LineNumbers = Json.AsBool(Json.Field(p, "lineNumbers"), false);
            page.Hyphenation = Json.AsBool(Json.Field(p, "hyphenation"), false);
            page.FooterPageNumbers = Json.AsBool(Json.Field(p, "footerNumbers"), true);
            page.FooterFont = Json.AsString(Json.Field(p, "footerFont")) ?? "Times New Roman";
            page.FooterSizePt = Json.AsDouble(Json.Field(p, "footerSizePt"), 10);
            return page;
        }

        private static List<SheetTemplate> ReadTemplates(string json)
        {
            var templates = new List<SheetTemplate>();
            var list = Json.AsList(Json.Field(Json.Parse(json), "templates"));
            if (list != null)
                foreach (var entry in list)
                {
                    var t = Json.AsObject(entry);
                    if (t == null) continue;
                    var template = new SheetTemplate();
                    var id = Json.AsString(Json.Field(t, "id"));
                    if (!string.IsNullOrEmpty(id)) template.Id = id;
                    template.Name = Json.AsString(Json.Field(t, "name")) ?? "Modèle";
                    var fields = Json.AsList(Json.Field(t, "fields"));
                    if (fields != null)
                        foreach (var fieldEntry in fields)
                        {
                            var f = Json.AsObject(fieldEntry);
                            if (f == null) continue;
                            var field = new SheetField();
                            var fieldId = Json.AsString(Json.Field(f, "id"));
                            if (!string.IsNullOrEmpty(fieldId)) field.Id = fieldId;
                            field.Name = Json.AsString(Json.Field(f, "name")) ?? "Champ";
                            field.Kind = Json.AsString(Json.Field(f, "kind")) ?? "text";
                            template.Fields.Add(field);
                        }
                    templates.Add(template);
                }
            return templates; // an empty list is legitimate (user deleted them all)
        }

        private static BinderItem ReadNode(object node, ZipArchive archive)
        {
            var obj = Json.AsObject(node);
            if (obj == null) return null;

            var item = new BinderItem();
            var id = Json.AsString(Json.Field(obj, "id"));
            if (!string.IsNullOrEmpty(id)) item.Id = id;
            item.Title = Json.AsString(Json.Field(obj, "title")) ?? "";
            item.Synopsis = Json.AsString(Json.Field(obj, "synopsis")) ?? "";
            item.Notes = Json.AsString(Json.Field(obj, "notes")) ?? "";
            item.Icon = Json.AsString(Json.Field(obj, "icon"));
            item.ImageId = Json.AsString(Json.Field(obj, "image"));
            item.CategoryKey = Json.AsString(Json.Field(obj, "category"));

            var kind = Json.AsString(Json.Field(obj, "kind"));
            item.Kind = kind == "category" ? ItemKind.Category
                      : kind == "folder" ? ItemKind.Folder
                      : kind == "sheet" ? ItemKind.Sheet
                      : kind == "media" ? ItemKind.Media : ItemKind.Text;

            if (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet)
                item.Document = ReadDocument(item.Id, archive);

            if (item.Kind == ItemKind.Sheet)
            {
                item.TemplateId = Json.AsString(Json.Field(obj, "template"));
                var fields = Json.AsObject(Json.Field(obj, "fields"));
                if (fields != null)
                    foreach (var kv in fields)
                        if (kv.Value is string) item.FieldValues[kv.Key] = (string)kv.Value;
                var info = Json.AsList(Json.Field(obj, "info"));
                if (info != null)
                    foreach (var infoEntry in info)
                    {
                        var e = Json.AsObject(infoEntry);
                        if (e == null) continue;
                        var entry = new InfoEntry();
                        var entryId = Json.AsString(Json.Field(e, "id"));
                        if (!string.IsNullOrEmpty(entryId)) entry.Id = entryId;
                        entry.Title = Json.AsString(Json.Field(e, "title")) ?? "";
                        entry.Value = Json.AsString(Json.Field(e, "value")) ?? "";
                        item.FreeInfo.Add(entry);
                    }
            }

            if (item.Kind == ItemKind.Media)
            {
                item.MediaExtension = Json.AsString(Json.Field(obj, "mediaExt"));
                var media = archive.GetEntry("research/" + item.Id + (item.MediaExtension ?? ""));
                if (media != null)
                    using (var mediaStream = media.Open())
                    using (var buffer = new MemoryStream())
                    {
                        mediaStream.CopyTo(buffer);
                        item.MediaBytes = buffer.ToArray();
                    }
            }

            var children = Json.AsList(Json.Field(obj, "children"));
            if (children != null)
                foreach (var child in children)
                {
                    var childItem = ReadNode(child, archive);
                    if (childItem != null) item.Children.Add(childItem);
                }
            return item;
        }

        private static TextDocument ReadDocument(string id, ZipArchive archive)
        {
            var jsonEntry = archive.GetEntry("texts/" + id + ".json");
            if (jsonEntry != null)
                return ParseDocument(ReadEntry(jsonEntry));

            // v1 fallback: plain text entry.
            var txtEntry = archive.GetEntry("texts/" + id + ".txt");
            if (txtEntry != null)
                return TextDocument.FromPlainText(ReadEntry(txtEntry));

            return new TextDocument { Paragraphs = { new TextParagraph() } };
        }

        private static TextDocument ParseDocument(string json)
        {
            var document = new TextDocument();
            var root = Json.Parse(json);
            var paragraphs = Json.AsList(Json.Field(root, "paragraphs"));
            if (paragraphs != null)
                foreach (var entry in paragraphs)
                {
                    var p = Json.AsObject(entry);
                    if (p == null) continue;
                    var paragraph = new TextParagraph();
                    paragraph.StyleId = Json.AsString(Json.Field(p, "style")) ?? "body";
                    paragraph.AlignOverride = Json.AsString(Json.Field(p, "align"));
                    paragraph.ListKind = Json.AsString(Json.Field(p, "list"));
                    paragraph.PageBreakBefore = Json.AsBool(Json.Field(p, "pb"), false);
                    var runs = Json.AsList(Json.Field(p, "runs"));
                    if (runs != null)
                        foreach (var runEntry in runs)
                        {
                            var r = Json.AsObject(runEntry);
                            if (r == null) continue;
                            var run = new TextRun();
                            if (Json.AsBool(Json.Field(r, "br"), false))
                            {
                                run.IsLineBreak = true;
                            }
                            else if (Json.Field(r, "fn") != null)
                            {
                                run.FootnoteId = Json.AsString(Json.Field(r, "fn"));
                            }
                            else if (Json.Field(r, "img") != null)
                            {
                                run.ImageId = Json.AsString(Json.Field(r, "img"));
                            }
                            else if (Json.AsBool(Json.Field(r, "hr"), false))
                            {
                                run.IsRule = true;
                            }
                            else
                            {
                                run.Text = Json.AsString(Json.Field(r, "t")) ?? "";
                                run.Bold = OptBool(r, "b");
                                run.Italic = OptBool(r, "i");
                                run.Underline = OptBool(r, "u");
                                run.Strike = OptBool(r, "st");
                                run.Weight = Json.AsString(Json.Field(r, "w"));
                                run.FontFamily = Json.AsString(Json.Field(r, "font"));
                                var size = Json.Field(r, "size");
                                if (size is double) run.FontSize = (double)size;
                                run.Color = Json.AsString(Json.Field(r, "color"));
                                run.Highlight = Json.AsString(Json.Field(r, "hl"));
                            }
                            paragraph.Runs.Add(run);
                        }
                    document.Paragraphs.Add(paragraph);
                }
            if (document.Paragraphs.Count == 0)
                document.Paragraphs.Add(new TextParagraph());

            var notes = Json.AsList(Json.Field(root, "footnotes"));
            if (notes != null)
                foreach (var entry in notes)
                {
                    var n = Json.AsObject(entry);
                    if (n == null) continue;
                    var note = new Footnote();
                    var noteId = Json.AsString(Json.Field(n, "id"));
                    if (!string.IsNullOrEmpty(noteId)) note.Id = noteId;
                    note.Text = Json.AsString(Json.Field(n, "text")) ?? "";
                    document.Footnotes.Add(note);
                }
            return document;
        }

        private static bool? OptBool(Dictionary<string, object> obj, string name)
        {
            object v;
            if (obj.TryGetValue(name, out v) && v is bool) return (bool)v;
            return null;
        }

        private static void EnsureCategory(Project project, string title, string key)
        {
            if (project.Category(key) != null) return;
            var category = new BinderItem();
            category.Kind = ItemKind.Category;
            category.Title = title;
            category.CategoryKey = key;
            category.Id = key;
            // Keep the canonical order: insert trash last, others before it.
            if (key == Project.KeyTrash || project.Trash == null)
                project.Roots.Add(category);
            else
                project.Roots.Insert(project.Roots.IndexOf(project.Trash), category);
        }
    }
}

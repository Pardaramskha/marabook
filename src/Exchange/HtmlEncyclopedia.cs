using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>L'ENCYCLOPÉDIE EN HTML (1.1.0, 31/10, Rémi) : un DOSSIER,
    /// lisible sans JavaScript — index.html (couverture, titre, les
    /// catégories et sous chacune ses fiches, le Lexique), fiches/&lt;catégorie&gt;/
    /// &lt;fiche&gt;.html (le découpage du wiki : titre, portrait et infobox en
    /// encart, corps Markdown, Relations, Évolution et présence, radar en SVG,
    /// tags, navigation), lexique.html (un dictionnaire par lettre),
    /// encyclopedie.css (bâti sur les options : polices, taille, accent,
    /// clair ou sombre) et images/. Écrit à la main, sans bibliothèque,
    /// comme l'EPUB. Les liens internes ne visent que des fiches exportées.</summary>
    public static class HtmlEncyclopedia
    {
        public const string IndexFile = "index.html", LexiconFile = "lexique.html", CssFile = "encyclopedie.css", SheetsFolder = "fiches", ImagesFolder = "images";

        /// <summary>Écrit le dossier ; rend les chemins écrits (index en tête).</summary>
        public static List<string> Write(string folder, EncyclopediaDocument document, Project project)
        {
            if (document == null) throw new ArgumentNullException("document");
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            var index = Path.Combine(folder, IndexFile);
            File.WriteAllText(index, Index(document), new UTF8Encoding(false));
            written.Add(index);
            var css = Path.Combine(folder, CssFile);
            File.WriteAllText(css, Css(document.Options), new UTF8Encoding(false));
            written.Add(css);
            if (document.Options.IncludeLexicon)
            {
                var lexicon = Path.Combine(folder, LexiconFile);
                File.WriteAllText(lexicon, Lexicon(document), new UTF8Encoding(false));
                written.Add(lexicon);
            }
            var all = document.AllSheets();
            for (var i = 0; i < all.Count; i++)
            {
                var sheet = all[i];
                var dir = Path.Combine(folder, SheetsFolder, SectionFolder(sheet.Section));
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, sheet.Slug + ".html");
                File.WriteAllText(path, Sheet(document, sheet, i > 0 ? all[i - 1] : null, i + 1 < all.Count ? all[i + 1] : null), new UTF8Encoding(false));
                written.Add(path);
            }
            if (project != null && document.Images.Count > 0)
            {
                var imagesDir = Path.Combine(folder, ImagesFolder);
                Directory.CreateDirectory(imagesDir);
                foreach (var pair in document.Images)
                {
                    var image = project.FindImage(pair.Key);
                    if (image == null || image.Bytes == null) continue;
                    var bytes = image.Bytes;
                    var extension = pair.Value;
                    var maxWidth = pair.Key == document.CoverImageId ? Encyclopedia.CoverMaxWidth : Encyclopedia.PortraitMaxWidth;
                    foreach (var sheet in all) if (sheet.ImageId != pair.Key && sheet.BodyMarkdown.Contains(MarkdownDialect.ImageAddress(pair.Key))) maxWidth = Math.Max(maxWidth, Encyclopedia.BodyImageMaxWidth);
                    var shrink = Encyclopedia.ShrinkImage;
                    if (shrink != null)
                    {
                        byte[] smaller = null;
                        try { smaller = shrink(bytes, maxWidth); } catch (Exception) { smaller = null; }
                        if (smaller != null) { bytes = smaller; extension = ".png"; }
                    }
                    var path = Path.Combine(imagesDir, ImageFileName(pair.Key, extension));
                    File.WriteAllBytes(path, bytes);
                    written.Add(path);
                }
            }
            return written;
        }

        // ================================================================ chemins

        /// <summary>Le dossier d'une section sous fiches/ : la catégorie, ou
        /// « catégorie/sous-catégorie ».</summary>
        internal static string SectionFolder(EncyclopediaSection section)
        {
            if (section == null) return "sans-categorie";
            return section.Parent == null ? section.Slug : section.Parent.Slug + "/" + section.Slug;
        }

        /// <summary>Le chemin relatif d'une page de fiche depuis la racine.</summary>
        internal static string SheetPath(EncyclopediaSheet sheet)
        {
            return SheetsFolder + "/" + SectionFolder(sheet.Section) + "/" + sheet.Slug + ".html";
        }

        /// <summary>Le préfixe pour remonter à la racine depuis une page de fiche.</summary>
        private static string RootOf(EncyclopediaSheet sheet)
        {
            return sheet.Section != null && sheet.Section.Parent != null ? "../../../" : "../../";
        }

        private static string ImageFileName(string imageId, string extension)
        {
            var ext = (extension ?? ".png").ToLowerInvariant();
            if (ext == ".jpeg") ext = ".jpg";
            return imageId + ext;
        }

        private static string ImageSrc(EncyclopediaDocument document, string imageId, string root)
        {
            string extension;
            if (!document.Images.TryGetValue(imageId, out extension)) return null;
            if (Encyclopedia.ShrinkImage != null) extension = ".png"; // réduites en PNG (voir Write)
            return root + ImagesFolder + "/" + ImageFileName(imageId, extension);
        }

        // ================================================================ l'accueil

        public static string Index(EncyclopediaDocument document)
        {
            var sb = new StringBuilder();
            Head(sb, document, document.Title, "", "");
            sb.Append("<header class=\"hero\">\n");
            if (document.CoverImageId != null)
            {
                var src = ImageSrc(document, document.CoverImageId, "");
                if (src != null) sb.Append("<img class=\"cover\" src=\"").Append(Esc(src)).Append("\" alt=\"\">\n");
            }
            sb.Append("<h1>").Append(Esc(document.Title)).Append("</h1>\n");
            if (document.Subtitle.Length > 0) sb.Append("<p class=\"subtitle\">").Append(Esc(document.Subtitle)).Append("</p>\n");
            if (document.Author.Length > 0) sb.Append("<p class=\"author\">").Append(Esc(document.Author)).Append("</p>\n");
            sb.Append("<p class=\"count\">").Append(document.SheetCount).Append(document.SheetCount > 1 ? " fiches" : " fiche");
            if (document.Options.IncludeLexicon && document.Lexicon.Count > 0) sb.Append(" · <a href=\"").Append(LexiconFile).Append("\">Lexique</a> (").Append(document.Lexicon.Count).Append(document.Lexicon.Count > 1 ? " mots)" : " mot)");
            sb.Append("</p>\n</header>\n<main class=\"home\">\n");
            if (document.Sections.Count > 1)
            {
                sb.Append("<nav class=\"toc\" aria-label=\"Catégories\"><ul>\n");
                foreach (var section in document.Sections)
                    sb.Append("<li><a href=\"#").Append(Esc(section.Slug)).Append("\">").Append(Esc(section.Title)).Append("</a> <span class=\"n\">").Append(section.Count).Append("</span></li>\n");
                sb.Append("</ul></nav>\n");
            }
            foreach (var section in document.Sections) IndexSection(sb, section, 2, "");
            sb.Append("</main>\n");
            Foot(sb, document);
            return sb.ToString();
        }

        private static void IndexSection(StringBuilder sb, EncyclopediaSection section, int level, string root)
        {
            var tag = "h" + Math.Min(4, level);
            sb.Append("<section class=\"category\" id=\"").Append(Esc(section.Slug)).Append("\">\n<").Append(tag);
            if (section.Color != null) sb.Append(" style=\"--cat:").Append(Esc(section.Color)).Append("\"");
            sb.Append(">").Append(Esc(section.Title)).Append(" <span class=\"n\">").Append(section.Count).Append("</span></").Append(tag).Append(">\n");
            if (section.Sheets.Count > 0)
            {
                sb.Append("<ul class=\"sheets\">\n");
                foreach (var sheet in section.Sheets)
                {
                    sb.Append("<li><a href=\"").Append(Esc(root + SheetPath(sheet))).Append("\">").Append(Esc(sheet.Title)).Append("</a>");
                    if (sheet.Subtitle.Length > 0 && !string.Equals(sheet.Subtitle, section.Title, StringComparison.CurrentCultureIgnoreCase) && (section.Parent == null || !string.Equals(sheet.Subtitle, section.Parent.Title, StringComparison.CurrentCultureIgnoreCase)))
                        sb.Append(" <span class=\"kind\">").Append(Esc(sheet.Subtitle)).Append("</span>");
                    sb.Append("</li>\n");
                }
                sb.Append("</ul>\n");
            }
            foreach (var sub in section.Subsections) IndexSection(sb, sub, level + 1, root);
            sb.Append("</section>\n");
        }

        // ================================================================ une fiche

        public static string Sheet(EncyclopediaDocument document, EncyclopediaSheet sheet, EncyclopediaSheet previous, EncyclopediaSheet next)
        {
            var root = RootOf(sheet);
            var options = document.Options;
            var sb = new StringBuilder();
            Head(sb, document, sheet.Title + " — " + document.Title, root, "sheet portrait-" + Esc(options.PortraitSide ?? "right"));
            sb.Append("<nav class=\"crumbs\"><a href=\"").Append(root).Append(IndexFile).Append("\">").Append(Esc(document.Title)).Append("</a>");
            if (sheet.Section != null)
            {
                if (sheet.Section.Parent != null) sb.Append(" › <a href=\"").Append(root).Append(IndexFile).Append("#").Append(Esc(sheet.Section.Parent.Slug)).Append("\">").Append(Esc(sheet.Section.Parent.Title)).Append("</a>");
                sb.Append(" › <a href=\"").Append(root).Append(IndexFile).Append("#").Append(Esc(sheet.Section.Slug)).Append("\">").Append(Esc(sheet.Section.Title)).Append("</a>");
            }
            if (options.IncludeLexicon && document.Lexicon.Count > 0) sb.Append(" <span class=\"sep\">·</span> <a href=\"").Append(root).Append(LexiconFile).Append("\">Lexique</a>");
            sb.Append("</nav>\n<main class=\"sheet-page\">\n<article>\n<header><h1>").Append(Esc(sheet.Title)).Append("</h1>");
            if (sheet.Subtitle.Length > 0) sb.Append("<p class=\"kind\">").Append(Esc(sheet.Subtitle)).Append("</p>");
            if (sheet.Tags.Count > 0)
            {
                sb.Append("<p class=\"tags\">");
                foreach (var tag in sheet.Tags) sb.Append("<span class=\"tag\">").Append(Esc(tag)).Append("</span> ");
                sb.Append("</p>");
            }
            sb.Append("</header>\n");
            var hasAside = sheet.ImageId != null || sheet.Groups.Count > 0 || sheet.Radar != null;
            if (hasAside)
            {
                sb.Append("<aside class=\"infobox\">\n");
                if (sheet.ImageId != null)
                {
                    var src = ImageSrc(document, sheet.ImageId, root);
                    if (src != null) sb.Append("<img class=\"portrait\" src=\"").Append(Esc(src)).Append("\" alt=\"").Append(Esc(sheet.Title)).Append("\">\n");
                }
                foreach (var group in sheet.Groups)
                {
                    sb.Append("<section class=\"group\"><h3>").Append(Esc(group.Title)).Append("</h3>\n<dl>\n");
                    foreach (var field in group.Fields)
                    {
                        sb.Append("<dt>").Append(Esc(field.Label)).Append("</dt><dd>");
                        if (field.Kind == FieldKinds.List && field.Items.Count > 0)
                        {
                            sb.Append("<ul>");
                            foreach (var item in field.Items) sb.Append("<li>").Append(Linked(document, item, root)).Append("</li>");
                            sb.Append("</ul>");
                        }
                        else if (field.Kind == FieldKinds.Sheet)
                        {
                            EncyclopediaSheet target;
                            if (field.TargetId != null && document.ById.TryGetValue(field.TargetId, out target))
                                sb.Append("<a href=\"").Append(root).Append(Esc(SheetPath(target))).Append("\">").Append(Esc(field.Value)).Append("</a>");
                            else sb.Append(Esc(field.Value));
                        }
                        else if (field.Kind == FieldKinds.Rating) sb.Append("<span class=\"rating\">").Append(Esc(field.Value)).Append("</span>");
                        else sb.Append(Linked(document, field.Value, root));
                        sb.Append("</dd>\n");
                    }
                    sb.Append("</dl></section>\n");
                }
                if (sheet.Radar != null) Radar(sb, sheet.Radar);
                sb.Append("</aside>\n");
            }
            sb.Append("<div class=\"body\">\n");
            if (sheet.BodyMarkdown.Trim().Length > 0) MarkdownHtml(sb, sheet.BodyMarkdown, document, root);
            if (sheet.Relations.Count > 0)
            {
                sb.Append("<section class=\"relations\"><h2>Relations</h2><ul>\n");
                foreach (var relation in sheet.Relations)
                {
                    sb.Append("<li>");
                    EncyclopediaSheet target;
                    if (relation.TargetId != null && document.ById.TryGetValue(relation.TargetId, out target))
                        sb.Append("<a href=\"").Append(root).Append(Esc(SheetPath(target))).Append("\">").Append(Esc(relation.Label)).Append("</a>");
                    else sb.Append(Esc(relation.Label));
                    if (relation.Kind.Length > 0) sb.Append(" <span class=\"kind\">(").Append(Esc(relation.Kind)).Append(")</span>");
                    sb.Append("</li>\n");
                }
                sb.Append("</ul></section>\n");
            }
            if (sheet.Evolution.Count > 0)
            {
                sb.Append("<section class=\"evolution\"><h2>Évolution</h2><dl>\n");
                foreach (var step in sheet.Evolution)
                {
                    if (step.Label.Length > 0) sb.Append("<dt>").Append(Esc(step.Label)).Append("</dt>");
                    if (step.Note.Length > 0) sb.Append("<dd>").Append(Esc(step.Note)).Append("</dd>");
                    sb.Append("\n");
                }
                sb.Append("</dl></section>\n");
            }
            if (sheet.Presence.Count > 0)
            {
                sb.Append("<section class=\"presence\"><h2>Présence</h2><ul>\n");
                foreach (var row in sheet.Presence)
                    sb.Append("<li>").Append(Esc(row.TextTitle)).Append(" <span class=\"n\">· ").Append(row.Count).Append("</span></li>\n");
                sb.Append("</ul></section>\n");
            }
            sb.Append("</div>\n</article>\n");
            sb.Append("<nav class=\"pager\">");
            if (previous != null) sb.Append("<a class=\"prev\" href=\"").Append(root).Append(Esc(SheetPath(previous))).Append("\">← ").Append(Esc(previous.Title)).Append("</a>");
            sb.Append("<a class=\"up\" href=\"").Append(root).Append(IndexFile).Append(sheet.Section != null ? "#" + Esc(sheet.Section.Slug) : "").Append("\">Accueil</a>");
            if (next != null) sb.Append("<a class=\"next\" href=\"").Append(root).Append(Esc(SheetPath(next))).Append("\">").Append(Esc(next.Title)).Append(" →</a>");
            sb.Append("</nav>\n</main>\n");
            Foot(sb, document);
            return sb.ToString();
        }

        /// <summary>Le radar en SVG : la toile (anneaux et rayons), le polygone
        /// des valeurs, les libellés des axes.</summary>
        private static void Radar(StringBuilder sb, EncyclopediaRadar radar)
        {
            var n = radar.Axes.Count;
            if (n < 3) return;
            const double size = 240, cx = 120, cy = 120, r = 82;
            var ci = CultureInfo.InvariantCulture;
            sb.Append("<section class=\"group radar\"><h3>").Append(Esc(radar.Label)).Append("</h3>\n");
            sb.Append("<svg viewBox=\"0 0 ").Append(size.ToString(ci)).Append(' ').Append(size.ToString(ci)).Append("\" role=\"img\" aria-label=\"").Append(Esc(radar.Label)).Append("\">\n");
            for (var ring = 1; ring <= 4; ring++)
            {
                var rr = r * ring / 4;
                sb.Append("<polygon class=\"ring\" points=\"");
                for (var i = 0; i < n; i++) { var a = -Math.PI / 2 + 2 * Math.PI * i / n; sb.Append((cx + rr * Math.Cos(a)).ToString("0.#", ci)).Append(',').Append((cy + rr * Math.Sin(a)).ToString("0.#", ci)).Append(' '); }
                sb.Append("\"/>\n");
            }
            for (var i = 0; i < n; i++)
            {
                var a = -Math.PI / 2 + 2 * Math.PI * i / n;
                sb.Append("<line class=\"ray\" x1=\"").Append(cx.ToString(ci)).Append("\" y1=\"").Append(cy.ToString(ci)).Append("\" x2=\"").Append((cx + r * Math.Cos(a)).ToString("0.#", ci)).Append("\" y2=\"").Append((cy + r * Math.Sin(a)).ToString("0.#", ci)).Append("\"/>\n");
            }
            sb.Append("<polygon class=\"value\" points=\"");
            for (var i = 0; i < n; i++)
            {
                var a = -Math.PI / 2 + 2 * Math.PI * i / n;
                var v = r * Math.Max(0, Math.Min(1, radar.Axes[i].Value / radar.Max));
                sb.Append((cx + v * Math.Cos(a)).ToString("0.#", ci)).Append(',').Append((cy + v * Math.Sin(a)).ToString("0.#", ci)).Append(' ');
            }
            sb.Append("\"/>\n");
            for (var i = 0; i < n; i++)
            {
                var a = -Math.PI / 2 + 2 * Math.PI * i / n;
                var lx = cx + (r + 16) * Math.Cos(a);
                var ly = cy + (r + 16) * Math.Sin(a);
                var anchor = Math.Abs(Math.Cos(a)) < 0.2 ? "middle" : Math.Cos(a) > 0 ? "start" : "end";
                sb.Append("<text x=\"").Append(lx.ToString("0.#", ci)).Append("\" y=\"").Append((ly + 4).ToString("0.#", ci)).Append("\" text-anchor=\"").Append(anchor).Append("\">").Append(Esc(radar.Axes[i].Key)).Append(" <tspan class=\"v\">").Append(radar.Axes[i].Value.ToString("0.#", CultureInfo.CurrentCulture)).Append("</tspan></text>\n");
            }
            sb.Append("</svg></section>\n");
        }

        // ================================================================ le Lexique

        public static string Lexicon(EncyclopediaDocument document)
        {
            var sb = new StringBuilder();
            Head(sb, document, "Lexique — " + document.Title, "", "lexicon");
            sb.Append("<nav class=\"crumbs\"><a href=\"").Append(IndexFile).Append("\">").Append(Esc(document.Title)).Append("</a> › Lexique</nav>\n<main class=\"lexicon-page\">\n<h1>Lexique</h1>\n");
            if (document.Lexicon.Count == 0)
            {
                sb.Append("<p class=\"empty\">Aucun mot défini.</p>\n</main>\n");
                Foot(sb, document);
                return sb.ToString();
            }
            var letters = new List<string>();
            foreach (var entry in document.Lexicon) if (!letters.Contains(entry.Letter)) letters.Add(entry.Letter);
            sb.Append("<nav class=\"letters\">");
            foreach (var letter in letters) sb.Append("<a href=\"#l-").Append(Esc(letter == "#" ? "autres" : letter)).Append("\">").Append(Esc(letter)).Append("</a> ");
            sb.Append("</nav>\n");
            string current = null;
            foreach (var entry in document.Lexicon)
            {
                if (entry.Letter != current)
                {
                    if (current != null) sb.Append("</dl></section>\n");
                    current = entry.Letter;
                    sb.Append("<section class=\"letter\" id=\"l-").Append(Esc(current == "#" ? "autres" : current)).Append("\"><h2>").Append(Esc(current)).Append("</h2><dl>\n");
                }
                sb.Append("<dt><b>").Append(Esc(entry.Word)).Append("</b>");
                if (entry.ClassLabel.Length > 0) sb.Append(" <i class=\"class\">").Append(Esc(entry.ClassLabel.ToLowerInvariant())).Append("</i>");
                if (entry.Forms.Count > 0) sb.Append(" <span class=\"forms\">(").Append(Esc(string.Join(", ", entry.Forms.ToArray()))).Append(")</span>");
                sb.Append("</dt>\n");
                if (entry.Definition.Length > 0 || entry.Note.Length > 0)
                {
                    sb.Append("<dd>").Append(Esc(entry.Definition));
                    if (entry.Note.Length > 0) sb.Append(entry.Definition.Length > 0 ? " <span class=\"note\">— " : "<span class=\"note\">").Append(Esc(entry.Note)).Append("</span>");
                    sb.Append("</dd>\n");
                }
            }
            if (current != null) sb.Append("</dl></section>\n");
            sb.Append("</main>\n");
            Foot(sb, document);
            return sb.ToString();
        }

        // ================================================================ Markdown → HTML

        /// <summary>Le corps d'une fiche : les blocs du dialecte Markdown de
        /// Marabook (titres, paragraphes, listes, tâches, citations, code,
        /// tableaux, filets), les [[liens]] vers les fiches exportées, les
        /// images stockées.</summary>
        public static void MarkdownHtml(StringBuilder sb, string markdown, EncyclopediaDocument document, string root)
        {
            var blocks = MarkdownDialect.Parse(markdown ?? "");
            var listOpen = new List<bool>(); // la pile des listes ouvertes : ordonnée ?
            Action closeLists = delegate
            {
                for (var i = listOpen.Count - 1; i >= 0; i--) sb.Append(listOpen[i] ? "</ol>\n" : "</ul>\n");
                listOpen.Clear();
            };
            foreach (var block in blocks)
            {
                if (block.Kind != MdBlockKind.ListItem) closeLists();
                switch (block.Kind)
                {
                    case MdBlockKind.Heading:
                    {
                        var level = Math.Max(2, Math.Min(5, block.HeadingLevel + 1)); // h1 = le titre de la fiche
                        sb.Append("<h").Append(level).Append('>').Append(Inlines(block.Inlines, document, root)).Append("</h").Append(level).Append(">\n");
                        break;
                    }
                    case MdBlockKind.Paragraph:
                        sb.Append("<p>").Append(Inlines(block.Inlines, document, root)).Append("</p>\n");
                        break;
                    case MdBlockKind.Rule:
                        sb.Append("<hr>\n");
                        break;
                    case MdBlockKind.Quote:
                        sb.Append("<blockquote>");
                        if (block.QuoteLines != null) foreach (var line in block.QuoteLines) sb.Append("<p>").Append(Inlines(line, document, root)).Append("</p>");
                        sb.Append("</blockquote>\n");
                        break;
                    case MdBlockKind.Code:
                        sb.Append("<pre><code>").Append(Esc(block.CodeText ?? "")).Append("</code></pre>\n");
                        break;
                    case MdBlockKind.ListItem:
                    {
                        var depth = Math.Max(0, block.Indent) + 1;
                        while (listOpen.Count > depth) { sb.Append(listOpen[listOpen.Count - 1] ? "</ol>\n" : "</ul>\n"); listOpen.RemoveAt(listOpen.Count - 1); }
                        if (listOpen.Count == depth && listOpen[depth - 1] != block.Ordered) { sb.Append(listOpen[depth - 1] ? "</ol>\n" : "</ul>\n"); listOpen.RemoveAt(depth - 1); }
                        while (listOpen.Count < depth) { sb.Append(block.Ordered ? "<ol>\n" : "<ul>\n"); listOpen.Add(block.Ordered); }
                        sb.Append("<li").Append(block.IsTask ? (block.TaskChecked ? " class=\"task done\"" : " class=\"task\"") : "").Append('>');
                        if (block.IsTask) sb.Append(block.TaskChecked ? "<span class=\"box\">☑</span> " : "<span class=\"box\">☐</span> ");
                        sb.Append(Inlines(block.Inlines, document, root)).Append("</li>\n");
                        break;
                    }
                    case MdBlockKind.Table:
                    {
                        sb.Append("<table>");
                        if (block.TableHeader != null)
                        {
                            sb.Append("<thead><tr>");
                            for (var c = 0; c < block.TableHeader.Count; c++) sb.Append("<th").Append(Align(block, c)).Append('>').Append(Inlines(block.TableHeader[c], document, root)).Append("</th>");
                            sb.Append("</tr></thead>");
                        }
                        if (block.TableRows != null)
                        {
                            sb.Append("<tbody>");
                            foreach (var row in block.TableRows)
                            {
                                sb.Append("<tr>");
                                for (var c = 0; c < row.Count; c++) sb.Append("<td").Append(Align(block, c)).Append('>').Append(Inlines(row[c], document, root)).Append("</td>");
                                sb.Append("</tr>");
                            }
                            sb.Append("</tbody>");
                        }
                        sb.Append("</table>\n");
                        break;
                    }
                }
            }
            closeLists();
        }

        private static string Align(MdBlock block, int column)
        {
            if (block.TableAligns == null || column >= block.TableAligns.Count) return "";
            return block.TableAligns[column] == 1 ? " class=\"c\"" : block.TableAligns[column] == 2 ? " class=\"r\"" : "";
        }

        private static string Inlines(List<MdInline> inlines, EncyclopediaDocument document, string root)
        {
            var sb = new StringBuilder();
            if (inlines == null) return "";
            foreach (var inline in inlines)
            {
                if (inline.ImageUrl != null)
                {
                    var stored = MarkdownDialect.StoredImageId(inline.ImageUrl);
                    var src = stored != null ? ImageSrc(document, stored, root) : null;
                    if (src == null && stored == null && (inline.ImageUrl.StartsWith("http://") || inline.ImageUrl.StartsWith("https://"))) src = inline.ImageUrl;
                    if (src != null) sb.Append("<img src=\"").Append(Esc(src)).Append("\" alt=\"").Append(Esc(inline.Text ?? "")).Append("\">");
                    else if (!string.IsNullOrEmpty(inline.Text)) sb.Append(Esc(inline.Text));
                    continue;
                }
                var text = Esc(inline.Text ?? "");
                if (inline.Code) text = "<code>" + text + "</code>";
                if (inline.Bold) text = "<strong>" + text + "</strong>";
                if (inline.Italic) text = "<em>" + text + "</em>";
                if (inline.Strike) text = "<s>" + text + "</s>";
                if (inline.Underline) text = "<u>" + text + "</u>";
                if (inline.WikiTarget != null)
                {
                    var target = document.FindByTitle(inline.WikiTarget);
                    text = target != null ? "<a href=\"" + root + Esc(SheetPath(target)) + "\">" + text + "</a>" : text;
                }
                else if (inline.LinkUrl != null && (inline.LinkUrl.StartsWith("http://") || inline.LinkUrl.StartsWith("https://") || inline.LinkUrl.StartsWith("mailto:")))
                    text = "<a href=\"" + Esc(inline.LinkUrl) + "\" rel=\"noopener\">" + text + "</a>";
                sb.Append(text);
            }
            return sb.ToString();
        }

        /// <summary>Un texte de champ avec ses [[liens]] : la cible exportée
        /// devient un lien, les autres leur texte affiché.</summary>
        private static string Linked(EncyclopediaDocument document, string value, string root)
        {
            var links = Links.Find(value ?? "");
            if (links.Count == 0) return Esc(value ?? "");
            var sb = new StringBuilder();
            var cursor = 0;
            foreach (var link in links)
            {
                if (link.Start > cursor) sb.Append(Esc(value.Substring(cursor, link.Start - cursor)));
                var shown = value.Substring(link.TextStart, link.TextEnd - link.TextStart);
                var target = document.FindByTitle(link.Target);
                if (target != null) sb.Append("<a href=\"").Append(root).Append(Esc(SheetPath(target))).Append("\">").Append(Esc(shown)).Append("</a>");
                else sb.Append(Esc(shown));
                cursor = link.End;
            }
            if (cursor < value.Length) sb.Append(Esc(value.Substring(cursor)));
            return sb.ToString();
        }

        // ================================================================ squelette, pied, CSS

        private static void Head(StringBuilder sb, EncyclopediaDocument document, string title, string root, string bodyClass)
        {
            sb.Append("<!doctype html>\n<html lang=\"fr\"").Append(document.Options.Dark ? " data-theme=\"dark\"" : "").Append(">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            sb.Append("<title>").Append(Esc(title)).Append("</title>\n");
            sb.Append("<meta name=\"generator\" content=\"Marabook ").Append(Esc(AppInfo.Version)).Append("\">\n");
            sb.Append("<link rel=\"stylesheet\" href=\"").Append(root).Append(CssFile).Append("\">\n</head>\n<body class=\"").Append(bodyClass).Append("\">\n");
        }

        private static void Foot(StringBuilder sb, EncyclopediaDocument document)
        {
            sb.Append("<footer>");
            sb.Append(Esc(document.Title));
            if (document.Author.Length > 0) sb.Append(" — ").Append(Esc(document.Author));
            if (document.Options.ShowDates) sb.Append(" — exportée le ").Append(Esc(document.Date));
            sb.Append(" avec Marabook</footer>\n</body>\n</html>\n");
        }

        /// <summary>La feuille de styles bâtie sur les options : polices, taille,
        /// accent, clair ou sombre, l'encart à droite, à gauche ou au-dessus.</summary>
        public static string Css(EncyclopediaOptions options)
        {
            var ci = CultureInfo.InvariantCulture;
            var body = FontStack(options.BodyFont, "Georgia, \"Times New Roman\", serif");
            var heading = FontStack(options.HeadingFont, "\"Segoe UI\", Helvetica, Arial, sans-serif");
            var accent = EncyclopediaOptions.SafeColor(options.Accent, "#5B67D8");
            var sb = new StringBuilder();
            sb.Append(":root{--accent:").Append(accent).Append(";--bg:#FBFAF7;--paper:#FFFFFF;--ink:#1E2128;--soft:#5F6472;--line:#E3E1DB;--tint:#F1F0FB;--body:").Append(body).Append(";--heading:").Append(heading).Append(";--size:").Append(options.BaseSize.ToString("0.#", ci)).Append("px}\n");
            sb.Append("[data-theme=dark]{--bg:#17181C;--paper:#1F2127;--ink:#ECEAE4;--soft:#A3A7B3;--line:#33363F;--tint:#262A38}\n");
            sb.Append("*{box-sizing:border-box}html{font-size:var(--size)}body{margin:0;background:var(--bg);color:var(--ink);font-family:var(--body);line-height:1.55}\n");
            sb.Append("a{color:var(--accent);text-decoration:none}a:hover{text-decoration:underline}\n");
            sb.Append("h1,h2,h3,h4{font-family:var(--heading);line-height:1.2;margin:1.2em 0 .4em}h1{font-size:2.2rem;margin-top:.4em}h2{font-size:1.4rem;border-bottom:1px solid var(--line);padding-bottom:.2em}h3{font-size:1.05rem;color:var(--soft);text-transform:uppercase;letter-spacing:.06em}\n");
            sb.Append("main{max-width:1100px;margin:0 auto;padding:0 20px 40px}\n");
            sb.Append(".hero{text-align:center;padding:48px 20px 16px}.hero .cover{max-width:min(100%,520px);max-height:60vh;border-radius:12px;box-shadow:0 8px 30px rgba(0,0,0,.15)}.hero .subtitle{color:var(--soft);font-size:1.2rem;margin:.2em 0}.hero .author{color:var(--soft)}.hero .count{color:var(--soft);font-size:.9rem}\n");
            sb.Append(".toc ul{list-style:none;padding:0;display:flex;flex-wrap:wrap;gap:8px 18px;justify-content:center;margin:0 0 24px}.n{color:var(--soft);font-size:.85em}.kind{color:var(--soft);font-size:.85em}\n");
            sb.Append(".category{background:var(--paper);border:1px solid var(--line);border-radius:12px;padding:8px 22px 14px;margin:0 0 18px}.category h2,.category h3,.category h4{border:0;text-transform:none;letter-spacing:0;color:var(--ink);font-size:1.3rem;padding-left:14px;border-left:4px solid var(--cat,var(--accent))}.category h3{font-size:1.1rem}.category h4{font-size:1rem}\n");
            sb.Append(".sheets{list-style:none;padding:0;margin:0 0 6px;columns:3 220px;column-gap:24px}.sheets li{break-inside:avoid;padding:3px 0}\n");
            sb.Append(".crumbs{max-width:1100px;margin:0 auto;padding:14px 20px 0;color:var(--soft);font-family:var(--heading);font-size:.9rem}.crumbs .sep{margin:0 .3em}\n");
            sb.Append(".sheet-page article{display:grid;grid-template-columns:minmax(0,1fr) 300px;grid-template-areas:\"head head\" \"body aside\";gap:0 32px}.sheet-page header{grid-area:head}.sheet-page .infobox{grid-area:aside}.sheet-page .body{grid-area:body;min-width:0}\n");
            sb.Append(".portrait-left article{grid-template-columns:300px minmax(0,1fr);grid-template-areas:\"head head\" \"aside body\"}.portrait-top article{grid-template-columns:minmax(0,1fr);grid-template-areas:\"head\" \"aside\" \"body\"}.portrait-top .infobox{display:flex;flex-wrap:wrap;gap:16px}.portrait-top .infobox .portrait{max-width:280px}\n");
            sb.Append("@media (max-width:760px){.sheet-page article{grid-template-columns:minmax(0,1fr);grid-template-areas:\"head\" \"aside\" \"body\"}}\n");
            sb.Append(".sheet-page header .kind{margin:-.6em 0 .4em;color:var(--soft);font-family:var(--heading)}.tags{margin:.2em 0 0}.tag{display:inline-block;background:var(--tint);color:var(--accent);border-radius:999px;padding:1px 10px;font-size:.8rem;font-family:var(--heading);margin:0 4px 4px 0}\n");
            sb.Append(".infobox{background:var(--paper);border:1px solid var(--line);border-radius:12px;padding:14px 16px;align-self:start;font-size:.92rem}.infobox .portrait{display:block;width:100%;border-radius:8px;margin:0 0 10px}.infobox h3{margin:.8em 0 .3em}.infobox dl{margin:0}.infobox dt{font-family:var(--heading);font-weight:700;color:var(--soft);font-size:.78rem;margin-top:.5em}.infobox dd{margin:0}.infobox dd ul{margin:.1em 0 0;padding-left:1.1em}.rating{color:var(--accent);letter-spacing:.1em}\n");
            sb.Append(".radar svg{width:100%;height:auto;display:block}.radar .ring{fill:none;stroke:var(--line)}.radar .ray{stroke:var(--line)}.radar .value{fill:var(--accent);fill-opacity:.25;stroke:var(--accent);stroke-width:2}.radar text{font-family:var(--heading);font-size:10px;fill:var(--soft)}.radar .v{fill:var(--ink);font-weight:700}\n");
            sb.Append(".body img{max-width:100%;height:auto;border-radius:8px}.body blockquote{margin:1em 0;padding:.2em 1em;border-left:3px solid var(--accent);color:var(--soft)}.body pre{background:var(--tint);padding:10px 12px;border-radius:8px;overflow:auto}.body table{border-collapse:collapse;margin:1em 0}.body th,.body td{border:1px solid var(--line);padding:4px 10px}.body .c{text-align:center}.body .r{text-align:right}.body .task .box{color:var(--accent)}.body .done{color:var(--soft);text-decoration:line-through}\n");
            sb.Append(".relations ul,.presence ul{padding-left:1.2em}.evolution dt{font-weight:700;margin-top:.4em}.evolution dd{margin:0 0 0 1em;color:var(--soft)}\n");
            sb.Append(".pager{display:flex;justify-content:space-between;gap:16px;margin:36px 0 0;padding-top:14px;border-top:1px solid var(--line);font-family:var(--heading);font-size:.95rem}.pager .up{color:var(--soft)}\n");
            sb.Append(".letters{display:flex;flex-wrap:wrap;gap:6px 12px;font-family:var(--heading);margin:0 0 18px}.letter{background:var(--paper);border:1px solid var(--line);border-radius:12px;padding:6px 22px 14px;margin:0 0 16px}.letter h2{border:0;color:var(--accent)}.letter dt{margin-top:.6em}.letter .class{color:var(--soft);font-size:.9em}.letter .forms{color:var(--soft);font-size:.85em}.letter dd{margin:0 0 0 1.2em}.letter .note{color:var(--soft)}\n");
            sb.Append("footer{text-align:center;color:var(--soft);font-size:.85rem;padding:24px 20px 40px;font-family:var(--heading)}\n");
            return sb.ToString();
        }

        private static string FontStack(string family, string fallback)
        {
            var name = (family ?? "").Trim();
            if (name.Length == 0) return fallback;
            return "\"" + name.Replace("\"", "") + "\", " + fallback;
        }

        public static string Esc(string text)
        {
            return (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }
    }
}

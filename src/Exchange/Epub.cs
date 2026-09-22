using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Les options d'un EPUB (22/09) : ce que le dialogue laisse
    /// changer — titre, métadonnées, couverture, titres de chapitres.</summary>
    public class EpubOptions
    {
        public string Title = "";
        public string Subtitle = "";
        public string Author = "";
        public string Publisher = "";
        public string Identifier = "";   // ISBN ; vide = un urn:uuid
        public string Year = "";
        public string Language = "fr";
        public bool IncludeCover = true;
        public bool ChapterHeadings = true; // le titre de l'écrit en tête de chaque chapitre
    }

    /// <summary>Ce que l'EPUB contiendra, décidé AVANT le dialogue : les
    /// écrits dans l'ordre du livre, les pages dynamiques écartées (table des
    /// matières, index, notes de fin, glossaire — leurs folios n'ont pas de
    /// sens dans un texte qui recoule), la couverture possible.</summary>
    public class EpubPlan
    {
        public BinderItem Root;                                   // le livre, ou l'écrit seul
        public readonly List<BinderItem> Chapters = new List<BinderItem>();
        public readonly List<BinderItem> Skipped = new List<BinderItem>();
        public bool HasCover;                                     // l'image du livre (ou de l'écrit)
        public bool IsBook { get { return Root != null && Root.Kind == ItemKind.Book; } }
    }

    /// <summary>L'ÉCRIVAIN EPUB (22/09) : un EPUB 3 « reflowable » écrit à la
    /// main, comme le docx et l'odt — aucune bibliothèque. Un zip dont le
    /// premier fichier est « mimetype » non compressé, le container, le
    /// paquet OPF (métadonnées, manifeste, ordre de lecture), la navigation
    /// EPUB 3 (nav.xhtml) et son double NCX pour les vieilles liseuses, une
    /// feuille CSS bâtie sur la feuille de styles effective du livre, un
    /// XHTML par écrit, les images du projet et la couverture. Ce qui est
    /// mise en page fixe (folios, gabarits, marges, césure fine, veuves et
    /// orphelines) n'a pas cours : le lecteur recoule le texte.</summary>
    public static class Epub
    {
        private const string Mimetype = "application/epub+zip";
        private const string XhtmlNs = "http://www.w3.org/1999/xhtml";
        private const string OpsNs = "http://www.idpf.org/2007/ops";

        // ============================================================ le plan

        /// <summary>Les écrits d'un livre (dossiers traversés, pages
        /// dynamiques écartées) ou l'écrit seul.</summary>
        public static EpubPlan Plan(Project project, BinderItem root)
        {
            var plan = new EpubPlan { Root = root };
            if (root == null) return plan;
            if (root.Kind == ItemKind.Text) plan.Chapters.Add(root);
            else Collect(root, plan);
            plan.HasCover = root.ImageId != null && project != null && project.FindImage(root.ImageId) != null;
            return plan;
        }

        private static void Collect(BinderItem item, EpubPlan plan)
        {
            foreach (var child in item.Children)
            {
                if (child.Kind == ItemKind.Text)
                {
                    if (ExtraPages.IsDynamic(child)) plan.Skipped.Add(child);
                    else plan.Chapters.Add(child);
                }
                else if (child.Kind == ItemKind.Folder) Collect(child, plan);
            }
        }

        /// <summary>Les options pré-remplies depuis le livre (ou l'écrit) et
        /// les défauts de l'auteur.</summary>
        public static EpubOptions DefaultOptions(Project project, BinderItem root)
        {
            var options = new EpubOptions();
            if (root == null) return options;
            options.Title = root.Title;
            var info = root.Kind == ItemKind.Book ? root.Book : null;
            if (info != null)
            {
                options.Subtitle = info.Subtitle;
                options.Publisher = info.Publisher;
                options.Identifier = info.Isbn;
                options.Year = info.Year;
            }
            options.Author = Defaults.Or(info != null ? info.AuthorOverride : "",
                Defaults.Or(project != null ? project.Author : "", Defaults.Author));
            if (info != null && options.Publisher.Length == 0) options.Publisher = Defaults.Publisher;
            return options;
        }

        // ============================================================ l'écriture

        /// <summary>Écrit l'EPUB. Rend le nombre de chapitres écrits.</summary>
        public static int Write(string path, Project project, BinderItem root, EpubOptions options)
        {
            var plan = Plan(project, root);
            return Write(path, project, plan, options);
        }

        public static int Write(string path, Project project, EpubPlan plan, EpubOptions options)
        {
            if (plan == null || plan.Root == null) throw new ArgumentException("Rien à exporter.");
            if (options == null) options = DefaultOptions(project, plan.Root);
            var styles = project.Styles.EffectiveFor(plan.Root);
            var writer = new Builder(project, plan, options, styles);
            writer.Build();
            if (File.Exists(path)) File.Delete(path);
            // Le zip est écrit à la main : ZipArchive (.NET 4) laisse le
            // « mimetype » en méthode Deflate même sans compression, et
            // l'EPUB exige la méthode Stored pour ce premier fichier.
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                var zip = new ZipWriter(stream);
                zip.Add("mimetype", Encoding.ASCII.GetBytes(Mimetype), false);
                foreach (var file in writer.Files) zip.Add(file.Key, file.Value, true);
                zip.Finish();
            }
            return plan.Chapters.Count;
        }

        // ============================================================ le zip

        /// <summary>Un écrivain zip minimal : entrées Stored ou Deflate, en-têtes
        /// locaux, répertoire central, fin de répertoire — noms en UTF-8.</summary>
        private class ZipWriter
        {
            private readonly Stream _stream;
            private readonly List<byte[]> _central = new List<byte[]>();
            private static readonly uint[] CrcTable = BuildCrcTable();

            public ZipWriter(Stream stream) { _stream = stream; }

            public void Add(string name, byte[] data, bool deflate)
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                var crc = Crc32(data);
                var payload = data;
                if (deflate)
                {
                    using (var buffer = new MemoryStream())
                    {
                        using (var packer = new DeflateStream(buffer, CompressionMode.Compress, true))
                            packer.Write(data, 0, data.Length);
                        payload = buffer.ToArray();
                    }
                    if (payload.Length >= data.Length) { payload = data; deflate = false; }
                }
                var method = (ushort)(deflate ? 8 : 0);
                var offset = (uint)_stream.Position;
                var stamp = DosStamp(DateTime.Now);
                // En-tête local.
                var local = new MemoryStream();
                W32(local, 0x04034b50); W16(local, 20); W16(local, 0x0800); W16(local, method);
                W32(local, stamp); W32(local, crc); W32(local, (uint)payload.Length); W32(local, (uint)data.Length);
                W16(local, (ushort)nameBytes.Length); W16(local, 0);
                local.Write(nameBytes, 0, nameBytes.Length);
                var localBytes = local.ToArray();
                _stream.Write(localBytes, 0, localBytes.Length);
                _stream.Write(payload, 0, payload.Length);
                // Entrée du répertoire central.
                var entry = new MemoryStream();
                W32(entry, 0x02014b50); W16(entry, 20); W16(entry, 20); W16(entry, 0x0800); W16(entry, method);
                W32(entry, stamp); W32(entry, crc); W32(entry, (uint)payload.Length); W32(entry, (uint)data.Length);
                W16(entry, (ushort)nameBytes.Length); W16(entry, 0); W16(entry, 0); W16(entry, 0); W16(entry, 0);
                W32(entry, 0); W32(entry, offset);
                entry.Write(nameBytes, 0, nameBytes.Length);
                _central.Add(entry.ToArray());
            }

            public void Finish()
            {
                var start = (uint)_stream.Position;
                var size = 0u;
                foreach (var entry in _central) { _stream.Write(entry, 0, entry.Length); size += (uint)entry.Length; }
                var end = new MemoryStream();
                W32(end, 0x06054b50); W16(end, 0); W16(end, 0); W16(end, (ushort)_central.Count); W16(end, (ushort)_central.Count);
                W32(end, size); W32(end, start); W16(end, 0);
                var endBytes = end.ToArray();
                _stream.Write(endBytes, 0, endBytes.Length);
                _stream.Flush();
            }

            private static uint DosStamp(DateTime time)
            {
                if (time.Year < 1980) time = new DateTime(1980, 1, 1);
                var date = (uint)(((time.Year - 1980) << 9) | (time.Month << 5) | time.Day);
                var clock = (uint)((time.Hour << 11) | (time.Minute << 5) | (time.Second / 2));
                return (date << 16) | clock;
            }

            private static void W16(Stream s, ushort v) { s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)(v >> 8)); }
            private static void W32(Stream s, uint v) { W16(s, (ushort)(v & 0xFFFF)); W16(s, (ushort)(v >> 16)); }

            private static uint[] BuildCrcTable()
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    var c = n;
                    for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                return table;
            }

            private static uint Crc32(byte[] data)
            {
                var c = 0xFFFFFFFF;
                foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
                return c ^ 0xFFFFFFFF;
            }
        }

        // ============================================================ le bâtisseur

        private class ImageFile
        {
            public string Id;        // id du manifeste
            public string Name;      // images/…
            public string MediaType;
            public byte[] Bytes;
        }

        private class Builder
        {
            private readonly Project _project;
            private readonly EpubPlan _plan;
            private readonly EpubOptions _options;
            private readonly StyleSheet _styles;
            public readonly List<KeyValuePair<string, byte[]>> Files = new List<KeyValuePair<string, byte[]>>();
            private readonly List<ImageFile> _images = new List<ImageFile>();
            private readonly Dictionary<string, ImageFile> _imageByProjectId = new Dictionary<string, ImageFile>();
            private ImageFile _cover;
            private readonly List<string> _chapterFiles = new List<string>();
            private readonly string _uid;
            private readonly string _lang;

            public Builder(Project project, EpubPlan plan, EpubOptions options, StyleSheet styles)
            {
                _project = project;
                _plan = plan;
                _options = options;
                _styles = styles;
                _lang = string.IsNullOrEmpty(options.Language) ? "fr" : options.Language.Trim();
                var isbn = (options.Identifier ?? "").Trim();
                _uid = isbn.Length > 0 ? "urn:isbn:" + isbn.Replace(" ", "") : "urn:uuid:" + Guid.NewGuid().ToString("D");
            }

            public void Build()
            {
                Add("META-INF/container.xml", Container());
                Add("OEBPS/style.css", Css());
                if (_options.IncludeCover && _plan.HasCover)
                {
                    _cover = ImageFor(_plan.Root.ImageId, "cover");
                    if (_cover != null) Add("OEBPS/cover.xhtml", CoverPage());
                }
                for (var i = 0; i < _plan.Chapters.Count; i++)
                {
                    var name = "chapter-" + (i + 1).ToString("000", CultureInfo.InvariantCulture) + ".xhtml";
                    _chapterFiles.Add(name);
                    Add("OEBPS/" + name, Chapter(_plan.Chapters[i], i + 1));
                }
                foreach (var image in _images) Files.Add(new KeyValuePair<string, byte[]>("OEBPS/" + image.Name, image.Bytes));
                Add("OEBPS/nav.xhtml", Nav());
                Add("OEBPS/toc.ncx", Ncx());
                Add("OEBPS/content.opf", Opf());
            }

            private void Add(string name, string text)
            {
                Files.Add(new KeyValuePair<string, byte[]>(name, new UTF8Encoding(false).GetBytes(text)));
            }

            // ---------------------------------------------------------- container / opf

            private static string Container()
            {
                return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                    + "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">"
                    + "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles>"
                    + "</container>";
            }

            private string Opf()
            {
                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
                sb.Append("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\" xml:lang=\"").Append(Esc(_lang)).Append("\">\n");
                sb.Append("<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n");
                sb.Append("<dc:identifier id=\"bookid\">").Append(Esc(_uid)).Append("</dc:identifier>\n");
                var title = (_options.Title ?? "").Trim();
                if (title.Length == 0) title = _plan.Root.Title;
                sb.Append("<dc:title id=\"title\">").Append(Esc(title)).Append("</dc:title>\n");
                sb.Append("<meta refines=\"#title\" property=\"title-type\">main</meta>\n");
                var subtitle = (_options.Subtitle ?? "").Trim();
                if (subtitle.Length > 0)
                {
                    sb.Append("<dc:title id=\"subtitle\">").Append(Esc(subtitle)).Append("</dc:title>\n");
                    sb.Append("<meta refines=\"#subtitle\" property=\"title-type\">subtitle</meta>\n");
                }
                sb.Append("<dc:language>").Append(Esc(_lang)).Append("</dc:language>\n");
                var author = (_options.Author ?? "").Trim();
                if (author.Length > 0)
                {
                    sb.Append("<dc:creator id=\"creator\">").Append(Esc(author)).Append("</dc:creator>\n");
                    sb.Append("<meta refines=\"#creator\" property=\"role\" scheme=\"marc:relators\">aut</meta>\n");
                }
                var publisher = (_options.Publisher ?? "").Trim();
                if (publisher.Length > 0) sb.Append("<dc:publisher>").Append(Esc(publisher)).Append("</dc:publisher>\n");
                var year = (_options.Year ?? "").Trim();
                if (year.Length > 0) sb.Append("<dc:date>").Append(Esc(year)).Append("</dc:date>\n");
                sb.Append("<meta property=\"dcterms:modified\">").Append(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append("</meta>\n");
                sb.Append("<meta name=\"generator\" content=\"Marabook\"/>\n");
                if (_cover != null) sb.Append("<meta name=\"cover\" content=\"").Append(_cover.Id).Append("\"/>\n");
                sb.Append("</metadata>\n");

                sb.Append("<manifest>\n");
                sb.Append("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n");
                sb.Append("<item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>\n");
                sb.Append("<item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>\n");
                if (_cover != null) sb.Append("<item id=\"cover-page\" href=\"cover.xhtml\" media-type=\"application/xhtml+xml\"/>\n");
                for (var i = 0; i < _chapterFiles.Count; i++)
                    sb.Append("<item id=\"ch").Append(i + 1).Append("\" href=\"").Append(_chapterFiles[i]).Append("\" media-type=\"application/xhtml+xml\"/>\n");
                foreach (var image in _images)
                {
                    sb.Append("<item id=\"").Append(image.Id).Append("\" href=\"").Append(image.Name).Append("\" media-type=\"").Append(image.MediaType).Append("\"");
                    if (image == _cover) sb.Append(" properties=\"cover-image\"");
                    sb.Append("/>\n");
                }
                sb.Append("</manifest>\n");

                sb.Append("<spine toc=\"ncx\">\n");
                if (_cover != null) sb.Append("<itemref idref=\"cover-page\"/>\n");
                for (var i = 0; i < _chapterFiles.Count; i++) sb.Append("<itemref idref=\"ch").Append(i + 1).Append("\"/>\n");
                sb.Append("</spine>\n");
                sb.Append("</package>\n");
                return sb.ToString();
            }

            // ---------------------------------------------------------- navigation

            /// <summary>Les entrées de navigation : le récit seul (les
            /// liminaires, pages de fin et annexes sont hors table, comme au
            /// papier — décision du 21/09) ; un écrit seul est sa propre entrée.</summary>
            private List<KeyValuePair<string, string>> NavEntries()
            {
                var entries = new List<KeyValuePair<string, string>>();
                for (var i = 0; i < _plan.Chapters.Count; i++)
                {
                    var chapter = _plan.Chapters[i];
                    if (_plan.IsBook && (chapter.IsExtraPage || chapter.IsToc)) continue;
                    entries.Add(new KeyValuePair<string, string>(_chapterFiles[i], chapter.Title));
                }
                if (entries.Count == 0 && _chapterFiles.Count > 0)
                    entries.Add(new KeyValuePair<string, string>(_chapterFiles[0], _plan.Chapters[0].Title));
                return entries;
            }

            private string Nav()
            {
                var sb = new StringBuilder();
                Head(sb, "Table des matières");
                sb.Append("<nav epub:type=\"toc\" id=\"toc\"><h1>Table des matières</h1><ol>\n");
                foreach (var entry in NavEntries())
                    sb.Append("<li><a href=\"").Append(entry.Key).Append("\">").Append(Esc(entry.Value)).Append("</a></li>\n");
                sb.Append("</ol></nav>\n");
                sb.Append("<nav epub:type=\"landmarks\" hidden=\"hidden\"><ol>\n");
                if (_cover != null) sb.Append("<li><a epub:type=\"cover\" href=\"cover.xhtml\">Couverture</a></li>\n");
                sb.Append("<li><a epub:type=\"toc\" href=\"nav.xhtml\">Table des matières</a></li>\n");
                var body = BodyStart();
                if (body != null) sb.Append("<li><a epub:type=\"bodymatter\" href=\"").Append(body).Append("\">Début du texte</a></li>\n");
                sb.Append("</ol></nav>\n");
                sb.Append("</body></html>\n");
                return sb.ToString();
            }

            /// <summary>Le premier fichier du récit (après les liminaires).</summary>
            private string BodyStart()
            {
                for (var i = 0; i < _plan.Chapters.Count; i++)
                    if (!_plan.Chapters[i].IsExtraPage && !_plan.Chapters[i].IsToc) return _chapterFiles[i];
                return _chapterFiles.Count > 0 ? _chapterFiles[0] : null;
            }

            private string Ncx()
            {
                var sb = new StringBuilder();
                sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
                sb.Append("<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">\n");
                sb.Append("<head><meta name=\"dtb:uid\" content=\"").Append(Esc(_uid)).Append("\"/>")
                  .Append("<meta name=\"dtb:depth\" content=\"1\"/><meta name=\"dtb:totalPageCount\" content=\"0\"/><meta name=\"dtb:maxPageNumber\" content=\"0\"/></head>\n");
                var title = (_options.Title ?? "").Trim();
                if (title.Length == 0) title = _plan.Root.Title;
                sb.Append("<docTitle><text>").Append(Esc(title)).Append("</text></docTitle>\n<navMap>\n");
                var order = 1;
                foreach (var entry in NavEntries())
                {
                    sb.Append("<navPoint id=\"np").Append(order).Append("\" playOrder=\"").Append(order).Append("\"><navLabel><text>")
                      .Append(Esc(entry.Value)).Append("</text></navLabel><content src=\"").Append(entry.Key).Append("\"/></navPoint>\n");
                    order++;
                }
                sb.Append("</navMap>\n</ncx>\n");
                return sb.ToString();
            }

            // ---------------------------------------------------------- css

            private string Css()
            {
                var sb = new StringBuilder();
                var body = _styles.Body;
                sb.Append("body { margin: 0; padding: 0 1em; }\n");
                sb.Append("p { margin: 0; }\n");
                foreach (var style in _styles.Styles)
                {
                    sb.Append(".").Append(ClassOf(style.Id)).Append(" { ");
                    sb.Append("font-family: ").Append(FontFamilyCss(style.FontFamily)).Append("; ");
                    sb.Append("font-size: ").Append(Pt(style.FontSize)).Append("pt; ");
                    if (style.Bold) sb.Append("font-weight: bold; ");
                    if (style.Italic) sb.Append("font-style: italic; ");
                    if (style.Color != null) sb.Append("color: ").Append(style.Color).Append("; ");
                    sb.Append("text-align: ").Append(style.Align == "justify" ? "justify" : style.Align == "center" ? "center" : style.Align == "right" ? "right" : "left").Append("; ");
                    if (style.FirstLineIndent > 0) sb.Append("text-indent: ").Append(Pt(style.FirstLineIndent)).Append("pt; ");
                    if (style.LeftIndent > 0) sb.Append("margin-left: ").Append(Pt(style.LeftIndent)).Append("pt; ");
                    if (style.RightIndent > 0) sb.Append("margin-right: ").Append(Pt(style.RightIndent)).Append("pt; ");
                    sb.Append("margin-top: ").Append(Pt(style.SpaceBefore)).Append("pt; ");
                    sb.Append("margin-bottom: ").Append(Pt(style.SpaceAfter)).Append("pt; ");
                    sb.Append("line-height: ").Append(Ratio(LineHeightRatio(style))).Append("; ");
                    sb.Append("font-variant-ligatures: ").Append(style.Ligatures ? "common-ligatures" : "none").Append("; ");
                    if (style.HyphenationEnabled) sb.Append("hyphens: auto; -webkit-hyphens: auto; ");
                    else sb.Append("hyphens: manual; -webkit-hyphens: manual; ");
                    sb.Append("}\n");
                }
                sb.Append("h1.chapter { font-family: ").Append(FontFamilyCss(body.FontFamily)).Append("; font-size: 1.6em; font-weight: bold; text-align: center; margin: 2em 0 1.2em 0; page-break-after: avoid; }\n");
                sb.Append("li { margin: 0.2em 0; }\n");
                sb.Append("hr.rule { border: 0; border-top: 1px solid currentColor; margin: 1em 20%; }\n");
                sb.Append("p.figure { text-align: center; text-indent: 0; margin: 1em 0; }\n");
                sb.Append("img { max-width: 100%; height: auto; }\n");
                sb.Append("a.noteref { vertical-align: super; font-size: 0.75em; text-decoration: none; }\n");
                sb.Append("aside.footnote { font-size: 0.85em; margin-top: 1em; }\n");
                sb.Append("section.cover { text-align: center; margin: 0; padding: 0; }\n");
                sb.Append("section.cover img { max-height: 100%; }\n");
                return sb.ToString();
            }

            /// <summary>La valeur d'interligne du style rapportée à sa taille — un
            /// nombre sans unité pour CSS (0 = auto : le pourcentage du style).</summary>
            private static double LineHeightRatio(ParagraphStyle style)
            {
                if (style.FontSize <= 0) return 1.2;
                if (style.LineHeight > 1) return style.LineHeight / style.FontSize;
                return Math.Max(100, style.AutoLeadingPercent) / 100.0;
            }

            private static string FontFamilyCss(string family)
            {
                var name = (family ?? "").Trim();
                if (name.Length == 0) return "serif";
                var lower = name.ToLowerInvariant();
                var generic = lower.Contains("arial") || lower.Contains("helvetica") || lower.Contains("segoe") || lower.Contains("calibri")
                    || lower.Contains("verdana") || lower.Contains("sans") ? "sans-serif"
                    : lower.Contains("courier") || lower.Contains("consolas") || lower.Contains("mono") ? "monospace" : "serif";
                return "\"" + name.Replace("\"", "") + "\", " + generic;
            }

            // ---------------------------------------------------------- pages

            private void Head(StringBuilder sb, string title)
            {
                sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
                sb.Append("<html xmlns=\"").Append(XhtmlNs).Append("\" xmlns:epub=\"").Append(OpsNs).Append("\" xml:lang=\"").Append(Esc(_lang)).Append("\" lang=\"").Append(Esc(_lang)).Append("\">\n");
                sb.Append("<head><meta charset=\"utf-8\"/><title>").Append(Esc(title)).Append("</title><link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/></head>\n<body>\n");
            }

            private string CoverPage()
            {
                var sb = new StringBuilder();
                Head(sb, "Couverture");
                sb.Append("<section epub:type=\"cover\" class=\"cover\"><img src=\"").Append(_cover.Name).Append("\" alt=\"Couverture\"/></section>\n");
                sb.Append("</body></html>\n");
                return sb.ToString();
            }

            private string Chapter(BinderItem item, int number)
            {
                var document = Links.Strip(item.Document ?? new TextDocument());
                var sb = new StringBuilder();
                Head(sb, item.Title);
                var type = _plan.IsBook && (item.IsExtraPage || item.IsToc)
                    ? (ExtraPages.SectionOf(item) == ExtraPages.SectionFront ? "frontmatter" : "backmatter")
                    : "chapter";
                sb.Append("<section epub:type=\"").Append(type).Append("\" id=\"ch").Append(number).Append("\">\n");
                if (_options.ChapterHeadings) sb.Append("<h1 class=\"chapter\">").Append(Esc(item.Title)).Append("</h1>\n");

                var notes = new List<Footnote>();
                string openList = null;
                foreach (var paragraph in document.Paragraphs)
                {
                    var list = paragraph.ListKind == "number" ? "ol" : paragraph.ListKind != null ? "ul" : null;
                    if (list != openList)
                    {
                        if (openList != null) sb.Append("</").Append(openList).Append(">\n");
                        if (list != null) sb.Append("<").Append(list).Append(">\n");
                        openList = list;
                    }
                    ParagraphXhtml(sb, paragraph, document, list != null, notes);
                }
                if (openList != null) sb.Append("</").Append(openList).Append(">\n");

                if (notes.Count > 0)
                {
                    sb.Append("<hr class=\"rule\"/>\n");
                    for (var i = 0; i < notes.Count; i++)
                        sb.Append("<aside epub:type=\"footnote\" class=\"footnote\" id=\"fn").Append(i + 1).Append("\"><p>")
                          .Append("<a href=\"#fnref").Append(i + 1).Append("\">").Append(i + 1).Append(".</a> ")
                          .Append(Esc(notes[i].Text ?? "")).Append("</p></aside>\n");
                }
                sb.Append("</section>\n</body></html>\n");
                return sb.ToString();
            }

            private void ParagraphXhtml(StringBuilder sb, TextParagraph paragraph, TextDocument document, bool inList, List<Footnote> notes)
            {
                // Un filet : un paragraphe à lui seul.
                foreach (var run in paragraph.Runs)
                    if (run.IsRule) { sb.Append("<hr class=\"rule\"/>\n"); return; }
                var style = _styles.Find(paragraph.StyleId);
                var onlyImage = paragraph.Runs.Count == 1 && paragraph.Runs[0].ImageId != null;
                var tag = inList ? "li" : "p";
                sb.Append("<").Append(tag).Append(" class=\"").Append(onlyImage ? "figure" : ClassOf(style.Id)).Append("\"");
                var inline = new StringBuilder();
                if (paragraph.AlignOverride != null)
                    inline.Append("text-align: ").Append(paragraph.AlignOverride == "justify" ? "justify" : paragraph.AlignOverride).Append("; ");
                if (paragraph.Indent.HasValue || paragraph.FirstIndent.HasValue)
                {
                    double left, first;
                    paragraph.EffectiveIndents(style, out left, out first);
                    inline.Append("margin-left: ").Append(Pt(left)).Append("pt; text-indent: ").Append(Pt(first - left)).Append("pt; ");
                }
                if (Math.Abs(document.LineSpacing - 1) > 0.001)
                    inline.Append("line-height: ").Append(Ratio(LineHeightRatio(style) * document.LineSpacing)).Append("; ");
                if (inline.Length > 0) sb.Append(" style=\"").Append(inline.ToString().TrimEnd()).Append("\"");
                sb.Append(">");
                var any = false;
                foreach (var run in paragraph.Runs)
                {
                    if (run.IsLineBreak) { sb.Append("<br/>"); any = true; continue; }
                    if (run.ImageId != null)
                    {
                        var image = ImageFor(run.ImageId, null);
                        if (image != null) { sb.Append("<img src=\"").Append(image.Name).Append("\" alt=\"\"/>"); any = true; }
                        continue;
                    }
                    if (run.FootnoteId != null)
                    {
                        var note = document.FindFootnote(run.FootnoteId);
                        if (note == null) continue;
                        notes.Add(note);
                        var n = notes.Count;
                        sb.Append("<a class=\"noteref\" epub:type=\"noteref\" id=\"fnref").Append(n).Append("\" href=\"#fn").Append(n).Append("\">").Append(n).Append("</a>");
                        any = true;
                        continue;
                    }
                    if (string.IsNullOrEmpty(run.Text)) continue;
                    var css = RunCss(run);
                    if (css.Length > 0) sb.Append("<span style=\"").Append(css).Append("\">").Append(Esc(run.Text)).Append("</span>");
                    else sb.Append(Esc(run.Text));
                    any = true;
                }
                if (!any) sb.Append(" "); // un paragraphe vide garde sa hauteur
                sb.Append("</").Append(tag).Append(">\n");
            }

            private static string RunCss(TextRun run)
            {
                var sb = new StringBuilder();
                if (run.Bold == true) sb.Append("font-weight: bold; ");
                else if (run.Bold == false) sb.Append("font-weight: normal; ");
                if (!string.IsNullOrEmpty(run.Weight)) sb.Append("font-weight: ").Append(WeightCss(run.Weight)).Append("; ");
                if (run.Italic == true) sb.Append("font-style: italic; ");
                else if (run.Italic == false) sb.Append("font-style: normal; ");
                var decorations = "";
                if (run.Underline == true) decorations += " underline";
                if (run.Strike == true) decorations += " line-through";
                if (decorations.Length > 0) sb.Append("text-decoration:").Append(decorations).Append("; ");
                if (run.FontFamily != null) sb.Append("font-family: ").Append(FontFamilyCss(run.FontFamily)).Append("; ");
                if (run.FontSize.HasValue) sb.Append("font-size: ").Append(Pt(run.FontSize.Value)).Append("pt; ");
                if (run.Color != null) sb.Append("color: ").Append(run.Color).Append("; ");
                if (run.Highlight != null) sb.Append("background-color: ").Append(run.Highlight).Append("; ");
                if (run.Tracking.HasValue && Math.Abs(run.Tracking.Value) > 0.01)
                    sb.Append("letter-spacing: ").Append((run.Tracking.Value / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)).Append("em; ");
                return sb.ToString().TrimEnd();
            }

            private static string WeightCss(string name)
            {
                switch (name)
                {
                    case "Thin": return "100";
                    case "Light": return "300";
                    case "Medium": return "500";
                    case "SemiBold": return "600";
                    case "Bold": return "700";
                    case "Black": return "900";
                    default: return "400";
                }
            }

            // ---------------------------------------------------------- images

            /// <summary>L'image du projet dans le manifeste (une fois) : PNG,
            /// JPEG et GIF tels quels ; tout autre format (BMP…) converti en PNG.</summary>
            private ImageFile ImageFor(string projectImageId, string forcedName)
            {
                if (projectImageId == null) return null;
                ImageFile known;
                if (forcedName == null && _imageByProjectId.TryGetValue(projectImageId, out known)) return known;
                var source = _project.FindImage(projectImageId);
                if (source == null || source.Bytes == null || source.Bytes.Length == 0) return null;
                var extension = (source.Extension ?? ".png").ToLowerInvariant();
                var bytes = source.Bytes;
                string mediaType;
                if (extension == ".jpg" || extension == ".jpeg") mediaType = "image/jpeg";
                else if (extension == ".gif") mediaType = "image/gif";
                else if (extension == ".png") mediaType = "image/png";
                else
                {
                    bytes = ToPng(bytes);
                    if (bytes == null) return null;
                    extension = ".png";
                    mediaType = "image/png";
                }
                var number = _imageByProjectId.Count + 1;
                var image = new ImageFile
                {
                    Id = forcedName ?? "img" + number,
                    Name = "images/" + (forcedName ?? "img" + number) + extension,
                    MediaType = mediaType,
                    Bytes = bytes
                };
                _images.Add(image);
                if (forcedName == null) _imageByProjectId[projectImageId] = image;
                return image;
            }

            private static byte[] ToPng(byte[] bytes)
            {
                try
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(new MemoryStream(bytes),
                        System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(decoder.Frames[0]);
                    using (var output = new MemoryStream())
                    {
                        encoder.Save(output);
                        return output.ToArray();
                    }
                }
                catch { return null; }
            }

            // ---------------------------------------------------------- helpers

            private static string ClassOf(string styleId)
            {
                var sb = new StringBuilder("s-");
                foreach (var c in styleId ?? "body")
                    sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
                return sb.ToString();
            }

            private static string Pt(double px)
            {
                return (px * 0.75).ToString("0.##", CultureInfo.InvariantCulture);
            }

            private static string Ratio(double value)
            {
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>L'échappement XML — et les caractères de contrôle interdits
        /// dans un XML (sauf tabulation et retours) tombent.</summary>
        public static string Esc(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length + 8);
            foreach (var c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default:
                        if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') break;
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

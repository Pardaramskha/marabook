using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using UniversSale.Model;

namespace UniversSale.Exchange
{
    /// <summary>Native .odt reader/writer (ODF). Same declared scope as Docx:
    /// named paragraph styles, run overrides, footnotes, line breaks. Sizes are
    /// written in points (our px * 0.75).</summary>
    public static class Odt
    {
        public const string Filter = "Document OpenDocument (*.odt)|*.odt";

        private static string Pt(double px)
        {
            return (px * 0.75).ToString("0.##", CultureInfo.InvariantCulture) + "pt";
        }

        private static double PxFromLength(string length)
        {
            if (string.IsNullOrEmpty(length)) return 0;
            double value;
            var text = length.Trim();
            if (text.EndsWith("pt") &&
                double.TryParse(text.Substring(0, text.Length - 2), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value)) return value / 0.75;
            if (text.EndsWith("cm") &&
                double.TryParse(text.Substring(0, text.Length - 2), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value)) return value * 96 / 2.54;
            if (text.EndsWith("in") &&
                double.TryParse(text.Substring(0, text.Length - 2), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value)) return value * 96;
            return 0;
        }

        // ------------------------------------------------------- export

        public static void Export(TextDocument document, StyleSheet styles, string path)
        {
            using (var stream = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                // The mimetype entry must be first and uncompressed.
                var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
                using (var writer = new StreamWriter(mimetype.Open(), new UTF8Encoding(false)))
                    writer.Write("application/vnd.oasis.opendocument.text");

                WriteEntry(zip, "META-INF/manifest.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.2\">" +
                    "<manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.text\"/>" +
                    "<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/>" +
                    "<manifest:file-entry manifest:full-path=\"styles.xml\" manifest:media-type=\"text/xml\"/>" +
                    "</manifest:manifest>");
                WriteEntry(zip, "styles.xml", StylesXml(styles));
                WriteEntry(zip, "content.xml", ContentXml(document, styles));
            }
        }

        private static void WriteEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        private const string OfficeNs =
            "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
            "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
            "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
            "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
            "office:version=\"1.2\"";

        private static string StyleProps(ParagraphStyle style)
        {
            var sb = new StringBuilder();
            sb.Append("<style:paragraph-properties fo:text-align=\"").Append(FoAlign(style.Align))
              .Append("\" fo:margin-top=\"").Append(Pt(style.SpaceBefore))
              .Append("\" fo:margin-bottom=\"").Append(Pt(style.SpaceAfter))
              .Append("\" fo:margin-left=\"").Append(Pt(style.LeftIndent))
              .Append("\" fo:text-indent=\"").Append(Pt(style.FirstLineIndent))
              .Append("\"/>");
            sb.Append("<style:text-properties style:font-name=\"").Append(Esc(style.FontFamily))
              .Append("\" fo:font-size=\"").Append(Pt(style.FontSize)).Append("\"");
            if (style.Bold) sb.Append(" fo:font-weight=\"bold\"");
            if (style.Italic) sb.Append(" fo:font-style=\"italic\"");
            if (style.Color != null) sb.Append(" fo:color=\"").Append(style.Color).Append("\"");
            sb.Append("/>");
            return sb.ToString();
        }

        private static string StylesXml(StyleSheet styles)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
              .Append("<office:document-styles ").Append(OfficeNs).Append("><office:styles>");
            foreach (var style in styles.Styles)
            {
                sb.Append("<style:style style:family=\"paragraph\" style:name=\"US_")
                  .Append(Esc(style.Id)).Append("\" style:display-name=\"").Append(Esc(style.Name)).Append("\">")
                  .Append(StyleProps(style))
                  .Append("</style:style>");
            }
            sb.Append("</office:styles></office:document-styles>");
            return sb.ToString();
        }

        private static string ContentXml(TextDocument document, StyleSheet styles)
        {
            // Automatic text styles: one per distinct run-override combination.
            var autoStyles = new StringBuilder();
            var autoKeys = new Dictionary<string, string>();

            var body = new StringBuilder();
            var noteNumber = 0;
            foreach (var paragraph in document.Paragraphs)
            {
                var style = styles.Find(paragraph.StyleId);
                var styleName = "US_" + style.Id;
                if (paragraph.AlignOverride != null || paragraph.PageBreakBefore)
                {
                    // Per-paragraph automatic style deriving from the named one.
                    var key = "P|" + style.Id + "|" + paragraph.AlignOverride + "|" + paragraph.PageBreakBefore;
                    string autoName;
                    if (!autoKeys.TryGetValue(key, out autoName))
                    {
                        autoName = "PA" + (autoKeys.Count + 1);
                        autoKeys[key] = autoName;
                        autoStyles.Append("<style:style style:family=\"paragraph\" style:name=\"")
                          .Append(autoName).Append("\" style:parent-style-name=\"US_").Append(Esc(style.Id))
                          .Append("\"><style:paragraph-properties");
                        if (paragraph.AlignOverride != null)
                            autoStyles.Append(" fo:text-align=\"").Append(FoAlign(paragraph.AlignOverride)).Append("\"");
                        if (paragraph.PageBreakBefore)
                            autoStyles.Append(" fo:break-before=\"page\"");
                        autoStyles.Append("/></style:style>");
                    }
                    styleName = autoName;
                }

                body.Append("<text:p text:style-name=\"").Append(styleName).Append("\">");
                foreach (var run in paragraph.Runs)
                {
                    if (run.IsLineBreak) { body.Append("<text:line-break/>"); continue; }
                    if (run.FootnoteId != null)
                    {
                        var note = document.FindFootnote(run.FootnoteId);
                        noteNumber++;
                        body.Append("<text:note text:id=\"ftn").Append(noteNumber)
                            .Append("\" text:note-class=\"footnote\"><text:note-citation>")
                            .Append(noteNumber).Append("</text:note-citation><text:note-body><text:p>")
                            .Append(Esc(note == null ? "" : note.Text))
                            .Append("</text:p></text:note-body></text:note>");
                        continue;
                    }
                    var spanKey = RunKey(run);
                    if (spanKey == null)
                    {
                        AppendText(body, run.Text);
                        continue;
                    }
                    string spanName;
                    if (!autoKeys.TryGetValue(spanKey, out spanName))
                    {
                        spanName = "T" + (autoKeys.Count + 1);
                        autoKeys[spanKey] = spanName;
                        autoStyles.Append("<style:style style:family=\"text\" style:name=\"")
                          .Append(spanName).Append("\"><style:text-properties");
                        if (run.Bold.HasValue)
                            autoStyles.Append(" fo:font-weight=\"").Append(run.Bold.Value ? "bold" : "normal").Append("\"");
                        if (run.Italic.HasValue)
                            autoStyles.Append(" fo:font-style=\"").Append(run.Italic.Value ? "italic" : "normal").Append("\"");
                        if (run.Underline == true)
                            autoStyles.Append(" style:text-underline-style=\"solid\"");
                        if (run.Strike == true)
                            autoStyles.Append(" style:text-line-through-style=\"solid\"");
                        if (run.FontFamily != null)
                            autoStyles.Append(" style:font-name=\"").Append(Esc(run.FontFamily)).Append("\"");
                        if (run.FontSize.HasValue)
                            autoStyles.Append(" fo:font-size=\"").Append(Pt(run.FontSize.Value)).Append("\"");
                        if (run.Color != null)
                            autoStyles.Append(" fo:color=\"").Append(run.Color).Append("\"");
                        if (run.Highlight != null)
                            autoStyles.Append(" fo:background-color=\"").Append(run.Highlight).Append("\"");
                        autoStyles.Append("/></style:style>");
                    }
                    body.Append("<text:span text:style-name=\"").Append(spanName).Append("\">");
                    AppendText(body, run.Text);
                    body.Append("</text:span>");
                }
                body.Append("</text:p>");
            }

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
              .Append("<office:document-content ").Append(OfficeNs).Append(">")
              .Append("<office:automatic-styles>").Append(autoStyles).Append("</office:automatic-styles>")
              .Append("<office:body><office:text>").Append(body).Append("</office:text></office:body>")
              .Append("</office:document-content>");
            return sb.ToString();
        }

        private static string RunKey(TextRun run)
        {
            if (run.Bold == null && run.Italic == null && run.Underline == null
                && run.Strike == null && run.FontFamily == null && run.FontSize == null
                && run.Color == null && run.Highlight == null) return null;
            return "T|" + run.Bold + "|" + run.Italic + "|" + run.Underline + "|" + run.Strike
                 + "|" + run.FontFamily + "|" + run.FontSize + "|" + run.Color + "|" + run.Highlight;
        }

        private static void AppendText(StringBuilder sb, string text)
        {
            foreach (var c in text ?? "")
            {
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '\t') sb.Append("<text:tab/>");
                else if (c == '\n' || c == '\r') sb.Append(' ');
                else sb.Append(c);
            }
        }

        private static string Esc(string text)
        {
            return (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
                .Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string FoAlign(string align)
        {
            if (align == "center") return "center";
            if (align == "right") return "end";
            if (align == "justify") return "justify";
            return "start";
        }

        // ------------------------------------------------------- import

        public static TextDocument Import(string path, StyleSheet projectStyles)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var contentEntry = zip.GetEntry("content.xml");
                if (contentEntry == null)
                    throw new InvalidDataException("content.xml introuvable : .odt invalide.");

                // Style catalog: named styles (styles.xml) + automatic styles (content.xml).
                var catalog = new Dictionary<string, OdtStyle>();
                var stylesEntry = zip.GetEntry("styles.xml");
                if (stylesEntry != null) ReadStyleCatalog(LoadXml(stylesEntry), catalog);
                var content = LoadXml(contentEntry);
                ReadStyleCatalog(content, catalog);

                // Map paragraph styles to project styles (create missing named ones).
                var map = new Dictionary<string, string>();
                foreach (var kv in catalog)
                {
                    var odt = kv.Value;
                    if (odt.Family != "paragraph" || odt.Automatic) continue;
                    ParagraphStyle target = null;
                    var wanted = odt.DisplayName ?? odt.Name;
                    if (odt.Name.StartsWith("US_"))
                        target = FindById(projectStyles, odt.Name.Substring(3));
                    if (target == null)
                        foreach (var existing in projectStyles.Styles)
                            if (string.Equals(existing.Name, wanted, StringComparison.CurrentCultureIgnoreCase))
                            { target = existing; break; }
                    if (target == null && odt.Name != "Standard")
                    {
                        target = new ParagraphStyle { Name = wanted };
                        odt.ApplyTo(target);
                        projectStyles.Styles.Add(target);
                    }
                    map[odt.Name] = target == null ? "body" : target.Id;
                }

                var ns = Ns(content);
                var document = new TextDocument();
                var textRoot = content.SelectSingleNode("//office:body/office:text", ns);
                if (textRoot != null)
                    foreach (XmlNode child in textRoot.ChildNodes)
                        ReadTextNode(child, ns, document, projectStyles, catalog, map);
                if (document.Paragraphs.Count == 0) document.Paragraphs.Add(new TextParagraph());
                return document;
            }
        }

        private sealed class OdtStyle
        {
            public string Name, DisplayName, Family, Parent;
            public bool Automatic;
            public bool? Bold, Italic, Underline, Strike;
            public string FontFamily, Color, Highlight, Align;
            public double FontSize, SpaceBefore, SpaceAfter, FirstIndent, LeftIndent;

            public void ApplyTo(ParagraphStyle target)
            {
                if (FontFamily != null) target.FontFamily = FontFamily;
                if (FontSize > 0) target.FontSize = FontSize;
                target.Bold = Bold == true;
                target.Italic = Italic == true;
                if (Color != null) target.Color = Color;
                if (Align != null) target.Align = Align;
                target.SpaceBefore = SpaceBefore;
                target.SpaceAfter = SpaceAfter;
                target.FirstLineIndent = FirstIndent;
                target.LeftIndent = LeftIndent;
            }
        }

        private static ParagraphStyle FindById(StyleSheet styles, string id)
        {
            foreach (var style in styles.Styles)
                if (style.Id == id) return style;
            return null;
        }

        private static XmlDocument LoadXml(ZipArchiveEntry entry)
        {
            var xml = new XmlDocument();
            using (var reader = entry.Open()) xml.Load(reader);
            return xml;
        }

        private static XmlNamespaceManager Ns(XmlDocument xml)
        {
            var ns = new XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("office", "urn:oasis:names:tc:opendocument:xmlns:office:1.0");
            ns.AddNamespace("style", "urn:oasis:names:tc:opendocument:xmlns:style:1.0");
            ns.AddNamespace("text", "urn:oasis:names:tc:opendocument:xmlns:text:1.0");
            ns.AddNamespace("fo", "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0");
            return ns;
        }

        private static string Attr(XmlNode node, string localName, string nsUri)
        {
            if (node == null || node.Attributes == null) return null;
            foreach (XmlAttribute attr in node.Attributes)
                if (attr.LocalName == localName && (nsUri == null || attr.NamespaceURI == nsUri))
                    return attr.Value;
            return null;
        }

        private const string StyleUri = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
        private const string FoUri = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
        private const string TextUri = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

        private static void ReadStyleCatalog(XmlDocument xml, Dictionary<string, OdtStyle> catalog)
        {
            var ns = Ns(xml);
            foreach (XmlNode styleNode in xml.SelectNodes("//style:style", ns))
            {
                var style = new OdtStyle
                {
                    Name = Attr(styleNode, "name", StyleUri),
                    DisplayName = Attr(styleNode, "display-name", StyleUri),
                    Family = Attr(styleNode, "family", StyleUri),
                    Parent = Attr(styleNode, "parent-style-name", StyleUri),
                    Automatic = styleNode.ParentNode != null
                        && styleNode.ParentNode.LocalName == "automatic-styles"
                };
                if (style.Name == null) continue;

                var textProps = styleNode.SelectSingleNode("style:text-properties", ns);
                if (textProps != null)
                {
                    var weight = Attr(textProps, "font-weight", FoUri);
                    if (weight != null) style.Bold = weight == "bold" || weight == "bolder";
                    var fontStyle = Attr(textProps, "font-style", FoUri);
                    if (fontStyle != null) style.Italic = fontStyle == "italic" || fontStyle == "oblique";
                    var underline = Attr(textProps, "text-underline-style", StyleUri);
                    if (underline != null && underline != "none") style.Underline = true;
                    var strike = Attr(textProps, "text-line-through-style", StyleUri);
                    if (strike != null && strike != "none") style.Strike = true;
                    style.FontFamily = Attr(textProps, "font-name", StyleUri)
                        ?? Attr(textProps, "font-family", FoUri);
                    if (style.FontFamily != null) style.FontFamily = style.FontFamily.Trim('\'', '"');
                    style.FontSize = PxFromLength(Attr(textProps, "font-size", FoUri));
                    var color = Attr(textProps, "color", FoUri);
                    if (color != null && color.StartsWith("#")) style.Color = color.ToUpperInvariant();
                    var background = Attr(textProps, "background-color", FoUri);
                    if (background != null && background.StartsWith("#"))
                        style.Highlight = background.ToUpperInvariant();
                }
                var paraProps = styleNode.SelectSingleNode("style:paragraph-properties", ns);
                if (paraProps != null)
                {
                    var align = Attr(paraProps, "text-align", FoUri);
                    if (align != null) style.Align = FromFoAlign(align);
                    style.SpaceBefore = PxFromLength(Attr(paraProps, "margin-top", FoUri));
                    style.SpaceAfter = PxFromLength(Attr(paraProps, "margin-bottom", FoUri));
                    style.FirstIndent = PxFromLength(Attr(paraProps, "text-indent", FoUri));
                    style.LeftIndent = PxFromLength(Attr(paraProps, "margin-left", FoUri));
                }
                catalog[style.Name] = style;
            }
        }

        private static string FromFoAlign(string align)
        {
            if (align == "center") return "center";
            if (align == "end" || align == "right") return "right";
            if (align == "justify") return "justify";
            return "left";
        }

        private static void ReadTextNode(XmlNode node, XmlNamespaceManager ns,
            TextDocument document, StyleSheet projectStyles,
            Dictionary<string, OdtStyle> catalog, Dictionary<string, string> map)
        {
            if (node.LocalName == "p" || node.LocalName == "h")
            {
                document.Paragraphs.Add(ReadParagraph(node, ns, document, projectStyles, catalog, map));
                return;
            }
            if (node.LocalName == "list" || node.LocalName == "list-item"
                || node.LocalName == "section" || node.LocalName == "table"
                || node.LocalName == "table-row" || node.LocalName == "table-cell")
                foreach (XmlNode child in node.ChildNodes)
                    ReadTextNode(child, ns, document, projectStyles, catalog, map);
        }

        private static TextParagraph ReadParagraph(XmlNode p, XmlNamespaceManager ns,
            TextDocument document, StyleSheet projectStyles,
            Dictionary<string, OdtStyle> catalog, Dictionary<string, string> map)
        {
            var paragraph = new TextParagraph();
            var styleName = Attr(p, "style-name", TextUri);

            // Automatic paragraph styles derive from a named parent.
            OdtStyle auto = null;
            if (styleName != null && catalog.TryGetValue(styleName, out auto) && auto.Automatic
                && auto.Parent != null)
                styleName = auto.Parent;
            else
                auto = null;

            string ourId;
            paragraph.StyleId = styleName != null && map.TryGetValue(styleName, out ourId)
                ? ourId : "body";
            var style = projectStyles.Find(paragraph.StyleId);
            if (auto != null && auto.Align != null && auto.Align != style.Align)
                paragraph.AlignOverride = auto.Align;

            ReadInlines(p, ns, paragraph, document, style, catalog);
            return paragraph;
        }

        private static void ReadInlines(XmlNode container, XmlNamespaceManager ns,
            TextParagraph paragraph, TextDocument document, ParagraphStyle style,
            Dictionary<string, OdtStyle> catalog, OdtStyle inheritedSpan = null)
        {
            foreach (XmlNode child in container.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Text)
                {
                    AddRun(paragraph, child.Value, style, inheritedSpan);
                    continue;
                }
                if (child.LocalName == "line-break")
                {
                    paragraph.Runs.Add(new TextRun { IsLineBreak = true });
                    continue;
                }
                if (child.LocalName == "tab")
                {
                    AddRun(paragraph, "\t", style, inheritedSpan);
                    continue;
                }
                if (child.LocalName == "s")
                {
                    int count;
                    if (!int.TryParse(Attr(child, "c", TextUri) ?? "1", out count)) count = 1;
                    AddRun(paragraph, new string(' ', Math.Max(1, count)), style, inheritedSpan);
                    continue;
                }
                if (child.LocalName == "note")
                {
                    var noteBody = child.SelectSingleNode("text:note-body", ns);
                    var note = new Footnote { Text = noteBody == null ? "" : noteBody.InnerText.Trim() };
                    document.Footnotes.Add(note);
                    paragraph.Runs.Add(new TextRun { FootnoteId = note.Id });
                    continue;
                }
                if (child.LocalName == "span")
                {
                    OdtStyle span = null;
                    var name = Attr(child, "style-name", TextUri);
                    if (name != null) catalog.TryGetValue(name, out span);
                    ReadInlines(child, ns, paragraph, document, style, catalog, span ?? inheritedSpan);
                    continue;
                }
                if (child.LocalName == "a") // hyperlink: keep the text
                    ReadInlines(child, ns, paragraph, document, style, catalog, inheritedSpan);
            }
        }

        private static void AddRun(TextParagraph paragraph, string text,
            ParagraphStyle style, OdtStyle span)
        {
            if (string.IsNullOrEmpty(text)) return;
            var run = new TextRun { Text = text };
            if (span != null)
            {
                if (span.Bold.HasValue && span.Bold.Value != style.Bold) run.Bold = span.Bold;
                if (span.Italic.HasValue && span.Italic.Value != style.Italic) run.Italic = span.Italic;
                if (span.Underline == true) run.Underline = true;
                if (span.Strike == true) run.Strike = true;
                if (span.FontFamily != null && span.FontFamily != style.FontFamily)
                    run.FontFamily = span.FontFamily;
                if (span.FontSize > 0 && Math.Abs(span.FontSize - style.FontSize) > 0.1)
                    run.FontSize = span.FontSize;
                if (span.Color != null && span.Color != (style.Color ?? "")) run.Color = span.Color;
                if (span.Highlight != null) run.Highlight = span.Highlight;
            }
            paragraph.Runs.Add(run);
        }
    }
}

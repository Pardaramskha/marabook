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
    /// <summary>Native .docx reader/writer — plain OOXML over ZipArchive, no
    /// dependency. The pivot was modeled on docx semantics precisely so this
    /// mapping stays direct: named paragraph styles, run overrides, footnotes,
    /// line and page breaks. Declared scope (PLAN §3): styles, character
    /// formatting, footnotes. Images, tables and fields are out of scope —
    /// tables are flattened to paragraphs on import.</summary>
    public static class Docx
    {
        public const string Filter = "Document Word (*.docx)|*.docx";

        // Unit conversions: our sizes are WPF pixels (96 dpi).
        // 1 px = 0.75 pt; w:sz is half-points; spacing/indents are twips (pt*20).
        private static int HalfPoints(double px) { return (int)Math.Round(px * 1.5); }
        private static double PxFromHalfPoints(double halfPoints) { return halfPoints / 1.5; }
        private static int Twips(double px) { return (int)Math.Round(px * 15); }
        private static double PxFromTwips(double twips) { return twips / 15.0; }

        // ------------------------------------------------------- export

        public static void Export(TextDocument document, StyleSheet styles, string path)
        {
            using (var stream = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "[Content_Types].xml", ContentTypes(document.Footnotes.Count > 0));
                WriteEntry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                    "</Relationships>");
                WriteEntry(zip, "word/_rels/document.xml.rels", DocumentRels(document.Footnotes.Count > 0));
                WriteEntry(zip, "word/styles.xml", StylesXml(styles));
                if (document.Footnotes.Count > 0)
                    WriteEntry(zip, "word/footnotes.xml", FootnotesXml(document));
                WriteEntry(zip, "word/document.xml", DocumentXml(document, styles));
            }
        }

        private static void WriteEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        private static string ContentTypes(bool footnotes)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">")
              .Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>")
              .Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>")
              .Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>")
              .Append("<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>");
            if (footnotes)
                sb.Append("<Override PartName=\"/word/footnotes.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string DocumentRels(bool footnotes)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">")
              .Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            if (footnotes)
                sb.Append("<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes\" Target=\"footnotes.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

        private static string StylesXml(StyleSheet styles)
        {
            var body = styles.Body;
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<w:styles ").Append(W).Append(">")
              .Append("<w:docDefaults><w:rPrDefault><w:rPr>")
              .Append("<w:rFonts w:ascii=\"").Append(Esc(body.FontFamily))
              .Append("\" w:hAnsi=\"").Append(Esc(body.FontFamily)).Append("\"/>")
              .Append("<w:sz w:val=\"").Append(HalfPoints(body.FontSize)).Append("\"/>")
              .Append("</w:rPr></w:rPrDefault></w:docDefaults>");
            foreach (var style in styles.Styles)
            {
                sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"").Append(Esc(style.Id)).Append("\">")
                  .Append("<w:name w:val=\"").Append(Esc(style.Name)).Append("\"/>")
                  .Append("<w:pPr>");
                if (style.SpaceBefore > 0 || style.SpaceAfter > 0)
                    sb.Append("<w:spacing w:before=\"").Append(Twips(style.SpaceBefore))
                      .Append("\" w:after=\"").Append(Twips(style.SpaceAfter)).Append("\"/>");
                if (style.FirstLineIndent > 0 || style.LeftIndent > 0)
                    sb.Append("<w:ind w:left=\"").Append(Twips(style.LeftIndent))
                      .Append("\" w:firstLine=\"").Append(Twips(style.FirstLineIndent)).Append("\"/>");
                sb.Append("<w:jc w:val=\"").Append(Jc(style.Align)).Append("\"/>")
                  .Append("</w:pPr><w:rPr>")
                  .Append("<w:rFonts w:ascii=\"").Append(Esc(style.FontFamily))
                  .Append("\" w:hAnsi=\"").Append(Esc(style.FontFamily)).Append("\"/>");
                if (style.Bold) sb.Append("<w:b/>");
                if (style.Italic) sb.Append("<w:i/>");
                if (style.Color != null)
                    sb.Append("<w:color w:val=\"").Append(HexVal(style.Color)).Append("\"/>");
                sb.Append("<w:sz w:val=\"").Append(HalfPoints(style.FontSize)).Append("\"/>")
                  .Append("</w:rPr></w:style>");
            }
            sb.Append("</w:styles>");
            return sb.ToString();
        }

        private static string FootnotesXml(TextDocument document)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<w:footnotes ").Append(W).Append(">")
              .Append("<w:footnote w:type=\"separator\" w:id=\"0\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>")
              .Append("<w:footnote w:type=\"continuationSeparator\" w:id=\"1\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>");
            for (var i = 0; i < document.Footnotes.Count; i++)
            {
                sb.Append("<w:footnote w:id=\"").Append(i + 2).Append("\"><w:p><w:r>")
                  .Append("<w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:footnoteRef/></w:r>")
                  .Append("<w:r><w:t xml:space=\"preserve\"> ")
                  .Append(Esc(document.Footnotes[i].Text)).Append("</w:t></w:r></w:p></w:footnote>");
            }
            sb.Append("</w:footnotes>");
            return sb.ToString();
        }

        private static string DocumentXml(TextDocument document, StyleSheet styles)
        {
            // Footnote id by note id (docx numbers them 2+).
            var noteIds = new Dictionary<string, int>();
            for (var i = 0; i < document.Footnotes.Count; i++)
                noteIds[document.Footnotes[i].Id] = i + 2;

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<w:document ").Append(W).Append("><w:body>");
            foreach (var paragraph in document.Paragraphs)
            {
                var style = styles.Find(paragraph.StyleId);
                sb.Append("<w:p><w:pPr><w:pStyle w:val=\"").Append(Esc(style.Id)).Append("\"/>");
                if (paragraph.PageBreakBefore) sb.Append("<w:pageBreakBefore/>");
                if (paragraph.AlignOverride != null)
                    sb.Append("<w:jc w:val=\"").Append(Jc(paragraph.AlignOverride)).Append("\"/>");
                sb.Append("</w:pPr>");
                foreach (var run in paragraph.Runs)
                {
                    if (run.IsLineBreak) { sb.Append("<w:r><w:br/></w:r>"); continue; }
                    if (run.FootnoteId != null)
                    {
                        int id;
                        if (noteIds.TryGetValue(run.FootnoteId, out id))
                            sb.Append("<w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr>")
                              .Append("<w:footnoteReference w:id=\"").Append(id).Append("\"/></w:r>");
                        continue;
                    }
                    sb.Append("<w:r>");
                    var props = RunProps(run);
                    if (props.Length > 0) sb.Append("<w:rPr>").Append(props).Append("</w:rPr>");
                    sb.Append("<w:t xml:space=\"preserve\">").Append(Esc(run.Text)).Append("</w:t></w:r>");
                }
                sb.Append("</w:p>");
            }
            // Minimal section: A4, 2.5 cm margins.
            sb.Append("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>")
              .Append("<w:pgMar w:top=\"1417\" w:right=\"1417\" w:bottom=\"1417\" w:left=\"1417\"/></w:sectPr>")
              .Append("</w:body></w:document>");
            return sb.ToString();
        }

        private static string RunProps(TextRun run)
        {
            var sb = new StringBuilder();
            if (run.FontFamily != null)
                sb.Append("<w:rFonts w:ascii=\"").Append(Esc(run.FontFamily))
                  .Append("\" w:hAnsi=\"").Append(Esc(run.FontFamily)).Append("\"/>");
            if (run.Bold.HasValue) sb.Append(run.Bold.Value ? "<w:b/>" : "<w:b w:val=\"0\"/>");
            if (run.Italic.HasValue) sb.Append(run.Italic.Value ? "<w:i/>" : "<w:i w:val=\"0\"/>");
            if (run.Strike == true) sb.Append("<w:strike/>");
            if (run.Color != null)
                sb.Append("<w:color w:val=\"").Append(HexVal(run.Color)).Append("\"/>");
            if (run.FontSize.HasValue)
                sb.Append("<w:sz w:val=\"").Append(HalfPoints(run.FontSize.Value)).Append("\"/>");
            if (run.Underline == true) sb.Append("<w:u w:val=\"single\"/>");
            if (run.Highlight != null)
                sb.Append("<w:shd w:val=\"clear\" w:fill=\"").Append(HexVal(run.Highlight)).Append("\"/>");
            return sb.ToString();
        }

        private static string Jc(string align)
        {
            if (align == "center") return "center";
            if (align == "right") return "right";
            if (align == "justify") return "both";
            return "left";
        }

        private static string HexVal(string color)
        {
            return color != null && color.StartsWith("#") ? color.Substring(1) : color;
        }

        private static string Esc(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in text ?? "")
            {
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '"') sb.Append("&quot;");
                else if (c == '\n' || c == '\r') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------- import

        /// <summary>Imports a .docx into a pivot document. Named paragraph styles
        /// missing from the project's sheet are created there (merge).</summary>
        public static TextDocument Import(string path, StyleSheet projectStyles)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var styleMap = ReadStyles(zip, projectStyles);
                var footnotes = ReadFootnotes(zip);
                var documentEntry = zip.GetEntry("word/document.xml");
                if (documentEntry == null)
                    throw new InvalidDataException("word/document.xml introuvable : .docx invalide.");

                var xml = LoadXml(documentEntry);
                var ns = Ns(xml);
                var document = new TextDocument();
                foreach (var note in footnotes.Values) document.Footnotes.Add(note);

                var body = xml.SelectSingleNode("//w:body", ns);
                if (body != null) ReadBlock(body, ns, document, projectStyles, styleMap, footnotes);
                if (document.Paragraphs.Count == 0) document.Paragraphs.Add(new TextParagraph());

                // Keep only referenced notes, in reference order.
                var ordered = new List<Footnote>();
                foreach (var paragraph in document.Paragraphs)
                    foreach (var run in paragraph.Runs)
                        if (run.FootnoteId != null)
                        {
                            var note = document.FindFootnote(run.FootnoteId);
                            if (note != null && !ordered.Contains(note)) ordered.Add(note);
                        }
                document.Footnotes = ordered;
                return document;
            }
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
            ns.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
            return ns;
        }

        private static string Attr(XmlNode node, string name)
        {
            if (node == null || node.Attributes == null) return null;
            var attr = node.Attributes["w:val"];
            if (name != "w:val") attr = node.Attributes[name];
            return attr == null ? null : attr.Value;
        }

        /// <summary>docx styleId -> our style id; unknown styles are added to the
        /// project sheet with their basic properties.</summary>
        private static Dictionary<string, string> ReadStyles(ZipArchive zip, StyleSheet projectStyles)
        {
            var map = new Dictionary<string, string>();
            var entry = zip.GetEntry("word/styles.xml");
            if (entry == null) return map;
            var xml = LoadXml(entry);
            var ns = Ns(xml);
            foreach (XmlNode styleNode in xml.SelectNodes("//w:style[@w:type='paragraph']", ns))
            {
                var docxId = Attr(styleNode, "w:styleId");
                if (docxId == null) continue;
                var name = Attr(styleNode.SelectSingleNode("w:name", ns), "w:val") ?? docxId;

                // Match by our id first (round-trip of our own files), then by name.
                ParagraphStyle target = null;
                foreach (var existing in projectStyles.Styles)
                    if (string.Equals(existing.Id, docxId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(existing.Name, name, StringComparison.CurrentCultureIgnoreCase))
                    { target = existing; break; }

                if (target == null && !name.StartsWith("TOC") && docxId != "Normal")
                {
                    target = new ParagraphStyle { Name = name };
                    var rPr = styleNode.SelectSingleNode("w:rPr", ns);
                    if (rPr != null)
                    {
                        var fonts = Attr(rPr.SelectSingleNode("w:rFonts", ns), "w:ascii");
                        if (!string.IsNullOrEmpty(fonts)) target.FontFamily = fonts;
                        double sz;
                        if (double.TryParse(Attr(rPr.SelectSingleNode("w:sz", ns), "w:val") ?? "",
                            NumberStyles.Float, CultureInfo.InvariantCulture, out sz) && sz > 0)
                            target.FontSize = PxFromHalfPoints(sz);
                        target.Bold = IsOn(rPr.SelectSingleNode("w:b", ns));
                        target.Italic = IsOn(rPr.SelectSingleNode("w:i", ns));
                        var color = Attr(rPr.SelectSingleNode("w:color", ns), "w:val");
                        if (!string.IsNullOrEmpty(color) && color != "auto") target.Color = "#" + color;
                    }
                    var pPr = styleNode.SelectSingleNode("w:pPr", ns);
                    if (pPr != null)
                    {
                        target.Align = FromJc(Attr(pPr.SelectSingleNode("w:jc", ns), "w:val"));
                        double v;
                        var spacing = pPr.SelectSingleNode("w:spacing", ns);
                        if (double.TryParse(Attr(spacing, "w:before") ?? "", NumberStyles.Float,
                            CultureInfo.InvariantCulture, out v)) target.SpaceBefore = PxFromTwips(v);
                        if (double.TryParse(Attr(spacing, "w:after") ?? "", NumberStyles.Float,
                            CultureInfo.InvariantCulture, out v)) target.SpaceAfter = PxFromTwips(v);
                        var ind = pPr.SelectSingleNode("w:ind", ns);
                        if (double.TryParse(Attr(ind, "w:firstLine") ?? "", NumberStyles.Float,
                            CultureInfo.InvariantCulture, out v)) target.FirstLineIndent = PxFromTwips(v);
                        if (double.TryParse(Attr(ind, "w:left") ?? "", NumberStyles.Float,
                            CultureInfo.InvariantCulture, out v)) target.LeftIndent = PxFromTwips(v);
                    }
                    projectStyles.Styles.Add(target);
                }
                map[docxId] = target == null ? "body" : target.Id;
            }
            return map;
        }

        private static Dictionary<string, Footnote> ReadFootnotes(ZipArchive zip)
        {
            var notes = new Dictionary<string, Footnote>();
            var entry = zip.GetEntry("word/footnotes.xml");
            if (entry == null) return notes;
            var xml = LoadXml(entry);
            var ns = Ns(xml);
            foreach (XmlNode noteNode in xml.SelectNodes("//w:footnote", ns))
            {
                var type = Attr(noteNode, "w:type");
                if (type == "separator" || type == "continuationSeparator") continue;
                var id = Attr(noteNode, "w:id");
                if (id == null) continue;
                var sb = new StringBuilder();
                foreach (XmlNode textNode in noteNode.SelectNodes(".//w:t", ns))
                    sb.Append(textNode.InnerText);
                notes[id] = new Footnote { Text = sb.ToString().Trim() };
            }
            return notes;
        }

        private static void ReadBlock(XmlNode container, XmlNamespaceManager ns,
            TextDocument document, StyleSheet projectStyles,
            Dictionary<string, string> styleMap, Dictionary<string, Footnote> footnotes)
        {
            foreach (XmlNode child in container.ChildNodes)
            {
                if (child.LocalName == "p")
                    document.Paragraphs.Add(ReadParagraph(child, ns, projectStyles, styleMap, footnotes));
                else if (child.LocalName == "tbl" || child.LocalName == "tc"
                    || child.LocalName == "tr" || child.LocalName == "sdt"
                    || child.LocalName == "sdtContent")
                    ReadBlock(child, ns, document, projectStyles, styleMap, footnotes); // flatten
            }
        }

        private static TextParagraph ReadParagraph(XmlNode p, XmlNamespaceManager ns,
            StyleSheet projectStyles, Dictionary<string, string> styleMap,
            Dictionary<string, Footnote> footnotes)
        {
            var paragraph = new TextParagraph();
            var pPr = p.SelectSingleNode("w:pPr", ns);
            var docxStyle = Attr(pPr == null ? null : pPr.SelectSingleNode("w:pStyle", ns), "w:val");
            string ourId;
            paragraph.StyleId = docxStyle != null && styleMap.TryGetValue(docxStyle, out ourId)
                ? ourId : "body";
            var style = projectStyles.Find(paragraph.StyleId);
            var jc = Attr(pPr == null ? null : pPr.SelectSingleNode("w:jc", ns), "w:val");
            if (jc != null && FromJc(jc) != style.Align) paragraph.AlignOverride = FromJc(jc);

            ReadRuns(p, ns, paragraph, style, footnotes);
            return paragraph;
        }

        private static void ReadRuns(XmlNode container, XmlNamespaceManager ns,
            TextParagraph paragraph, ParagraphStyle style, Dictionary<string, Footnote> footnotes)
        {
            foreach (XmlNode child in container.ChildNodes)
            {
                if (child.LocalName == "hyperlink" || child.LocalName == "smartTag"
                    || child.LocalName == "ins")
                {
                    ReadRuns(child, ns, paragraph, style, footnotes);
                    continue;
                }
                if (child.LocalName != "r") continue;

                var reference = child.SelectSingleNode("w:footnoteReference", ns);
                if (reference != null)
                {
                    var id = Attr(reference, "w:id");
                    Footnote note;
                    if (id != null && footnotes.TryGetValue(id, out note))
                        paragraph.Runs.Add(new TextRun { FootnoteId = note.Id });
                    continue;
                }

                var sb = new StringBuilder();
                foreach (XmlNode part in child.ChildNodes)
                {
                    if (part.LocalName == "t") sb.Append(part.InnerText);
                    else if (part.LocalName == "tab") sb.Append('\t');
                    else if (part.LocalName == "br")
                    {
                        FlushRun(paragraph, sb, child, ns, style);
                        paragraph.Runs.Add(new TextRun { IsLineBreak = true });
                    }
                }
                FlushRun(paragraph, sb, child, ns, style);
            }
        }

        private static void FlushRun(TextParagraph paragraph, StringBuilder sb,
            XmlNode r, XmlNamespaceManager ns, ParagraphStyle style)
        {
            if (sb.Length == 0) return;
            var run = new TextRun { Text = sb.ToString() };
            sb.Length = 0;

            var rPr = r.SelectSingleNode("w:rPr", ns);
            if (rPr != null)
            {
                if (rPr.SelectSingleNode("w:b", ns) != null)
                {
                    var bold = IsOn(rPr.SelectSingleNode("w:b", ns));
                    if (bold != style.Bold) run.Bold = bold;
                }
                if (rPr.SelectSingleNode("w:i", ns) != null)
                {
                    var italic = IsOn(rPr.SelectSingleNode("w:i", ns));
                    if (italic != style.Italic) run.Italic = italic;
                }
                if (IsOn(rPr.SelectSingleNode("w:u", ns))
                    && Attr(rPr.SelectSingleNode("w:u", ns), "w:val") != "none")
                    run.Underline = true;
                if (IsOn(rPr.SelectSingleNode("w:strike", ns))) run.Strike = true;
                var fonts = Attr(rPr.SelectSingleNode("w:rFonts", ns), "w:ascii");
                if (!string.IsNullOrEmpty(fonts) && fonts != style.FontFamily) run.FontFamily = fonts;
                double sz;
                if (double.TryParse(Attr(rPr.SelectSingleNode("w:sz", ns), "w:val") ?? "",
                    NumberStyles.Float, CultureInfo.InvariantCulture, out sz) && sz > 0
                    && Math.Abs(PxFromHalfPoints(sz) - style.FontSize) > 0.1)
                    run.FontSize = PxFromHalfPoints(sz);
                var color = Attr(rPr.SelectSingleNode("w:color", ns), "w:val");
                if (!string.IsNullOrEmpty(color) && color != "auto"
                    && "#" + color != (style.Color ?? "")) run.Color = "#" + color;
                var shd = Attr(rPr.SelectSingleNode("w:shd", ns), "w:fill");
                if (!string.IsNullOrEmpty(shd) && shd != "auto") run.Highlight = "#" + shd;
                var highlight = Attr(rPr.SelectSingleNode("w:highlight", ns), "w:val");
                if (highlight != null) run.Highlight = NamedHighlight(highlight) ?? run.Highlight;
            }
            paragraph.Runs.Add(run);
        }

        private static bool IsOn(XmlNode toggle)
        {
            if (toggle == null) return false;
            var val = Attr(toggle, "w:val");
            return val == null || (val != "0" && val != "false" && val != "none");
        }

        private static string FromJc(string jc)
        {
            if (jc == "center") return "center";
            if (jc == "right" || jc == "end") return "right";
            if (jc == "both" || jc == "distribute") return "justify";
            return "left";
        }

        private static string NamedHighlight(string name)
        {
            switch (name)
            {
                case "yellow": return "#FFFF00";
                case "green": return "#00FF00";
                case "cyan": return "#00FFFF";
                case "magenta": return "#FF00FF";
                case "red": return "#FF0000";
                case "blue": return "#0000FF";
                case "lightGray": return "#D3D3D3";
                case "darkGray": return "#A9A9A9";
                default: return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Native .docx reader/writer — plain OOXML over ZipArchive, no
    /// dependency. The pivot was modeled on docx semantics precisely so this
    /// mapping stays direct: named paragraph styles, run overrides, footnotes,
    /// line and page breaks. Declared scope (PLAN §3): styles, character
    /// formatting, footnotes. Tables and fields are out of scope — tables
    /// are flattened to paragraphs on import. IMAGES (23/09/2026) : lues à
    /// l'import quand un projet est fourni (word/media via les relations,
    /// dessins inline ou ancrés, et les vieux w:pict) ; l'export ne les
    /// écrit pas encore.</summary>
    public static class Docx
    {
        public const string Filter = "Document Word (*.docx)|*.docx";

        // Unit conversions: our sizes are WPF pixels (96 dpi).
        // 1 px = 0.75 pt; w:sz is half-points; spacing/indents are twips (pt*20).
        private static int HalfPoints(double px) { return (int)Math.Round(px * 1.5); }
        private static double PxFromHalfPoints(double halfPoints) { return halfPoints / 1.5; }
        private static int Twips(double px) { return (int)Math.Round(px * 15); }
        private static double PxFromTwips(double twips) { return twips / 15.0; }
        private static int TwipsFromMm(double mm) { return (int)Math.Round(mm * 1440.0 / 25.4); }

        // ------------------------------------------------------- export

        /// <summary>commentsAuthor (b49) : les annotations non résolues
        /// partent en COMMENTAIRES WORD sous ce nom (l'auteur du projet,
        /// sinon l'utilisateur de la machine).</summary>
        public static void Export(TextDocument document, StyleSheet styles, string path,
            PageSetup setup = null, string commentsAuthor = null)
        {
            var hasLists = false;
            foreach (var paragraph in document.Paragraphs)
                if (paragraph.ListKind != null) { hasLists = true; break; }
            var hasFooter = setup != null && setup.FooterPageNumbers;
            var hasSettings = setup != null && setup.Hyphenation;
            var comments = CollectComments(document);

            using (var stream = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "[Content_Types].xml",
                    ContentTypes(document.Footnotes.Count > 0, hasLists, hasFooter, hasSettings, comments.Count > 0));
                WriteEntry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                    "</Relationships>");
                WriteEntry(zip, "word/_rels/document.xml.rels",
                    DocumentRels(document.Footnotes.Count > 0, hasLists, hasFooter, hasSettings, comments.Count > 0));
                WriteEntry(zip, "word/styles.xml", StylesXml(styles));
                if (comments.Count > 0)
                    WriteEntry(zip, "word/comments.xml", CommentsXml(comments, commentsAuthor));
                if (document.Footnotes.Count > 0)
                    WriteEntry(zip, "word/footnotes.xml", FootnotesXml(document));
                if (hasLists) WriteEntry(zip, "word/numbering.xml", NumberingXml());
                if (hasFooter) WriteEntry(zip, "word/footer1.xml", FooterXml(setup));
                if (hasSettings) WriteEntry(zip, "word/settings.xml", SettingsXml(styles));
                WriteEntry(zip, "word/document.xml", DocumentXml(document, styles, setup, hasFooter, comments));
            }
        }

        // ------------------------------------------------------- commentaires (b49)

        /// <summary>Une annotation qui part en commentaire : son numéro Word
        /// et le premier/dernier run qu'elle couvre.</summary>
        private sealed class ExportComment
        {
            public int Number;
            public Annotation Annotation;
            public int FirstParagraph = -1, FirstRun, LastParagraph = -1, LastRun;
        }

        /// <summary>Les annotations NON RÉSOLUES portées par au moins un run,
        /// dans l'ordre du texte.</summary>
        private static List<ExportComment> CollectComments(TextDocument document)
        {
            var list = new List<ExportComment>();
            var byId = new Dictionary<string, ExportComment>();
            for (var p = 0; p < document.Paragraphs.Count; p++)
                for (var r = 0; r < document.Paragraphs[p].Runs.Count; r++)
                {
                    var run = document.Paragraphs[p].Runs[r];
                    if (run.AnnotationId == null) continue;
                    ExportComment comment;
                    if (!byId.TryGetValue(run.AnnotationId, out comment))
                    {
                        var annotation = document.FindAnnotation(run.AnnotationId);
                        if (annotation == null || annotation.Resolved) continue;
                        comment = new ExportComment
                        {
                            Number = list.Count,
                            Annotation = annotation,
                            FirstParagraph = p,
                            FirstRun = r
                        };
                        byId[run.AnnotationId] = comment;
                        list.Add(comment);
                    }
                    comment.LastParagraph = p;
                    comment.LastRun = r;
                }
            return list;
        }

        private static string CommentsXml(List<ExportComment> comments, string author)
        {
            author = (author ?? "").Trim();
            if (author.Length == 0)
            {
                try { author = Environment.UserName; } catch { }
                if (string.IsNullOrEmpty(author)) author = "Marabook";
            }
            var initials = new StringBuilder();
            foreach (var word in author.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
                if (initials.Length < 3) initials.Append(char.ToUpperInvariant(word[0]));
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<w:comments ").Append(W).Append(">");
            foreach (var comment in comments)
            {
                sb.Append("<w:comment w:id=\"").Append(comment.Number)
                  .Append("\" w:author=\"").Append(Esc(author))
                  .Append("\" w:date=\"").Append(CommentMerge.ToIsoDate(comment.Annotation.Created))
                  .Append("\" w:initials=\"").Append(Esc(initials.ToString())).Append("\">");
                var lines = (comment.Annotation.Text ?? "").Replace("\r\n", "\n").Split('\n');
                foreach (var line in lines)
                    sb.Append("<w:p><w:r><w:t xml:space=\"preserve\">").Append(Esc(line)).Append("</w:t></w:r></w:p>");
                sb.Append("</w:comment>");
            }
            sb.Append("</w:comments>");
            return sb.ToString();
        }

        /// <summary>Un run du corps : saut de ligne, appel de note, ou texte
        /// avec ses propriétés ; les images sortent du périmètre docx.</summary>
        private static string RunXml(TextRun run, Dictionary<string, int> noteIds)
        {
            if (run.IsLineBreak) return "<w:r><w:br/></w:r>";
            if (run.ImageId != null) return ""; // images: out of docx scope (PLAN §3)
            if (run.FootnoteId != null)
            {
                int id;
                return noteIds.TryGetValue(run.FootnoteId, out id)
                    ? "<w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr>"
                        + "<w:footnoteReference w:id=\"" + id + "\"/></w:r>"
                    : "";
            }
            var sb = new StringBuilder("<w:r>");
            var props = RunProps(run);
            if (props.Length > 0) sb.Append("<w:rPr>").Append(props).Append("</w:rPr>");
            sb.Append("<w:t xml:space=\"preserve\">").Append(Esc(run.Text)).Append("</w:t></w:r>");
            return sb.ToString();
        }

        private static void WriteEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        /// <summary>Document-level hyphenation, tuned by the body style's
        /// Césure tab (Word has no per-style hyphenation zone).</summary>
        private static string SettingsXml(StyleSheet styles)
        {
            var body = styles.Body;
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<w:settings ").Append(W).Append(">")
              .Append("<w:autoHyphenation/>")
              .Append("<w:consecutiveHyphenLimit w:val=\"")
              .Append(Math.Max(0, body.HyphenConsecutiveLimit)).Append("\"/>")
              .Append("<w:doNotHyphenateCaps/>")
              .Append("</w:settings>");
            return sb.ToString();
        }

        private static string ContentTypes(bool footnotes, bool lists, bool footer, bool settings, bool comments = false)
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
            if (lists)
                sb.Append("<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>");
            if (footer)
                sb.Append("<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>");
            if (settings)
                sb.Append("<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
            if (comments)
                sb.Append("<Override PartName=\"/word/comments.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml\"/>");
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string DocumentRels(bool footnotes, bool lists, bool footer, bool settings, bool comments = false)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">")
              .Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            if (footnotes)
                sb.Append("<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes\" Target=\"footnotes.xml\"/>");
            if (lists)
                sb.Append("<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>");
            if (footer)
                sb.Append("<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>");
            if (settings)
                sb.Append("<Relationship Id=\"rId5\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>");
            if (comments)
                sb.Append("<Relationship Id=\"rId6\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments\" Target=\"comments.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        private const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
        private const string R = "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

        /// <summary>Two fixed numbering definitions: numId 1 = bullets,
        /// numId 2 = decimal. Level 0 only — the pivot has flat lists.</summary>
        private static string NumberingXml()
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<w:numbering ").Append(W).Append(">")
              .Append("<w:abstractNum w:abstractNumId=\"0\">")
              .Append("<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/>")
              .Append("<w:lvlText w:val=\"•\"/><w:lvlJc w:val=\"left\"/>")
              .Append("<w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl>")
              .Append("</w:abstractNum>")
              .Append("<w:abstractNum w:abstractNumId=\"1\">")
              .Append("<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/>")
              .Append("<w:lvlText w:val=\"%1.\"/><w:lvlJc w:val=\"left\"/>")
              .Append("<w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl>")
              .Append("</w:abstractNum>")
              .Append("<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>")
              .Append("<w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>")
              .Append("</w:numbering>");
            return sb.ToString();
        }

        /// <summary>Centered page number, footer font per the page setup
        /// (defaults: Times New Roman 10).</summary>
        private static string FooterXml(PageSetup setup)
        {
            var font = Esc(setup.FooterFont ?? "Times New Roman");
            var halfPoints = (int)Math.Round(setup.FooterSizePt * 2);
            var runProps = "<w:rPr><w:rFonts w:ascii=\"" + font + "\" w:hAnsi=\"" + font + "\"/>"
                + "<w:sz w:val=\"" + halfPoints + "\"/></w:rPr>";
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<w:ftr ").Append(W).Append(">")
              .Append("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr>")
              .Append("<w:r>").Append(runProps).Append("<w:fldChar w:fldCharType=\"begin\"/></w:r>")
              .Append("<w:r>").Append(runProps).Append("<w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>")
              .Append("<w:r>").Append(runProps).Append("<w:fldChar w:fldCharType=\"separate\"/></w:r>")
              .Append("<w:r>").Append(runProps).Append("<w:t>1</w:t></w:r>")
              .Append("<w:r>").Append(runProps).Append("<w:fldChar w:fldCharType=\"end\"/></w:r>")
              .Append("</w:p></w:ftr>");
            return sb.ToString();
        }

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
                if (style.SpaceBefore > 0 || style.SpaceAfter > 0 || style.LineHeight > 1)
                {
                    sb.Append("<w:spacing w:before=\"").Append(Twips(style.SpaceBefore))
                      .Append("\" w:after=\"").Append(Twips(style.SpaceAfter)).Append("\"");
                    if (style.LineHeight > 1)
                        sb.Append(" w:line=\"").Append(Twips(style.LineHeight))
                          .Append("\" w:lineRule=\"atLeast\"");
                    sb.Append("/>");
                }
                if (style.FirstLineIndent > 0 || style.LeftIndent > 0 || style.RightIndent > 0)
                    sb.Append("<w:ind w:left=\"").Append(Twips(style.LeftIndent))
                      .Append("\" w:right=\"").Append(Twips(style.RightIndent))
                      .Append("\" w:firstLine=\"").Append(Twips(style.FirstLineIndent)).Append("\"/>");
                if (!style.HyphenationEnabled)
                    sb.Append("<w:suppressAutoHyphens/>");
                if (style.KeepLinesTogether) sb.Append("<w:keepLines/>");
                if (style.KeepNextLines > 0) sb.Append("<w:keepNext/>");
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
            var none = new Dictionary<string, int>(); // pas d'appel de note dans une note
            for (var i = 0; i < document.Footnotes.Count; i++)
            {
                // Le paragraphe porte le style « footnote » de la feuille
                // (0.50.0, écrit dans styles.xml avec les autres) et ses runs
                // gardent leurs formats.
                sb.Append("<w:footnote w:id=\"").Append(i + 2).Append("\"><w:p>")
                  .Append("<w:pPr><w:pStyle w:val=\"").Append(Esc(StyleSheet.FootnoteId)).Append("\"/></w:pPr><w:r>")
                  .Append("<w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:footnoteRef/></w:r>")
                  .Append("<w:r><w:t xml:space=\"preserve\"> </w:t></w:r>");
                foreach (var run in document.Footnotes[i].Runs) sb.Append(RunXml(run, none));
                sb.Append("</w:p></w:footnote>");
            }
            sb.Append("</w:footnotes>");
            return sb.ToString();
        }

        private static string DocumentXml(TextDocument document, StyleSheet styles,
            PageSetup setup, bool footer, List<ExportComment> comments)
        {
            // Footnote id by note id (docx numbers them 2+).
            var noteIds = new Dictionary<string, int>();
            for (var i = 0; i < document.Footnotes.Count; i++)
                noteIds[document.Footnotes[i].Id] = i + 2;

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<w:document ").Append(W).Append(" ").Append(R).Append("><w:body>");
            for (var pIndex = 0; pIndex < document.Paragraphs.Count; pIndex++)
            {
                var paragraph = document.Paragraphs[pIndex];
                var isRule = false;
                foreach (var probe in paragraph.Runs) if (probe.IsRule) { isRule = true; break; }

                var style = styles.Find(paragraph.StyleId);
                sb.Append("<w:p><w:pPr><w:pStyle w:val=\"").Append(Esc(style.Id)).Append("\"/>");
                if (paragraph.ListKind != null)
                    sb.Append("<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"")
                      .Append(paragraph.ListKind == "number" ? 2 : 1).Append("\"/></w:numPr>");
                if (isRule)
                    sb.Append("<w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"auto\"/></w:pBdr>");
                if (paragraph.PageBreakBefore) sb.Append("<w:pageBreakBefore/>");
                if (Math.Abs(document.LineSpacing - 1) > 0.001) // interligne du document (22/09)
                {
                    var leading = style.LineHeight > 1 ? style.LineHeight : style.FontSize * Math.Max(100, style.AutoLeadingPercent) / 100.0;
                    sb.Append("<w:spacing w:line=\"").Append(Twips(leading * document.LineSpacing)).Append("\" w:lineRule=\"atLeast\"/>");
                }
                if (paragraph.AlignOverride != null)
                    sb.Append("<w:jc w:val=\"").Append(Jc(paragraph.AlignOverride)).Append("\"/>");
                if (paragraph.Indent.HasValue || paragraph.FirstIndent.HasValue) // décalage du bloc et/ou de la première ligne
                {
                    double left, first;
                    paragraph.EffectiveIndents(style, out left, out first);
                    sb.Append("<w:ind w:left=\"").Append(Twips(left));
                    if (first >= left) sb.Append("\" w:firstLine=\"").Append(Twips(first - left));
                    else sb.Append("\" w:hanging=\"").Append(Twips(left - first));
                    sb.Append("\"/>");
                }
                sb.Append("</w:pPr>");
                if (isRule) { sb.Append("</w:p>"); continue; } // the border IS the rule
                for (var r = 0; r < paragraph.Runs.Count; r++)
                {
                    // Les commentaires (b49) : la plage ouvre avant le premier
                    // run annoté, se ferme après le dernier, l'appel suit.
                    foreach (var comment in comments)
                        if (comment.FirstParagraph == pIndex && comment.FirstRun == r)
                            sb.Append("<w:commentRangeStart w:id=\"").Append(comment.Number).Append("\"/>");
                    sb.Append(RunXml(paragraph.Runs[r], noteIds));
                    foreach (var comment in comments)
                        if (comment.LastParagraph == pIndex && comment.LastRun == r)
                            sb.Append("<w:commentRangeEnd w:id=\"").Append(comment.Number).Append("\"/>")
                              .Append("<w:r><w:commentReference w:id=\"").Append(comment.Number).Append("\"/></w:r>");
                }
                sb.Append("</w:p>");
            }

            // Section: page size, margins, columns and line numbers follow the
            // project's page setup (defaults: A4, 2.5 cm).
            var page = setup ?? new PageSetup();
            sb.Append("<w:sectPr>");
            if (footer) sb.Append("<w:footerReference w:type=\"default\" r:id=\"rId4\"/>");
            sb.Append("<w:pgSz w:w=\"").Append(TwipsFromMm(page.PageWidthMm))
              .Append("\" w:h=\"").Append(TwipsFromMm(page.PageHeightMm)).Append("\"/>")
              .Append("<w:pgMar w:top=\"").Append(TwipsFromMm(page.MarginTopMm))
              .Append("\" w:right=\"").Append(TwipsFromMm(page.MarginRightMm))
              .Append("\" w:bottom=\"").Append(TwipsFromMm(page.MarginBottomMm))
              .Append("\" w:left=\"").Append(TwipsFromMm(page.MarginLeftMm))
              .Append("\" w:footer=\"").Append(TwipsFromMm(Math.Max(5, page.MarginBottomMm / 2)))
              .Append("\"/>");
            if (page.Columns > 1)
                sb.Append("<w:cols w:num=\"").Append(page.Columns).Append("\" w:space=\"708\"/>");
            if (page.LineNumbers)
                sb.Append("<w:lnNumType w:countBy=\"1\" w:restart=\"continuous\"/>");
            sb.Append("</w:sectPr>")
              .Append("</w:body></w:document>");
            return sb.ToString();
        }

        private static string RunProps(TextRun run)
        {
            var sb = new StringBuilder();
            // w:noProof d'abord : l'ordre canonique OOXML le place en tête.
            if (run.NoProof) sb.Append("<w:noProof/>");
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
        /// <summary>project (23/09) : le magasin d'images qui reçoit les images
        /// du document ; null = les images sont ignorées (comme avant).</summary>
        public static TextDocument Import(string path, StyleSheet projectStyles, Project project = null)
        {
            List<DocxComment> ignored;
            return ImportWithComments(path, projectStyles, out ignored, project);
        }

        /// <summary>L'état d'une lecture (b49) : les commentaires du fichier,
        /// ceux dont la plage est ouverte, et les annotations créées pour eux ;
        /// (23/09) les relations d'image du document et le magasin qui les reçoit.</summary>
        private sealed class ImportContext
        {
            public Dictionary<string, DocxComment> Comments = new Dictionary<string, DocxComment>();
            public readonly List<string> Active = new List<string>();
            public readonly Dictionary<string, Annotation> Annotations = new Dictionary<string, Annotation>();
            public readonly List<DocxComment> Used = new List<DocxComment>();
            public TextDocument Document;
            public ImportedImages Images;
            public Dictionary<string, string> ImageRels = new Dictionary<string, string>(); // rId → entrée de l'archive

            /// <summary>L'id d'image du projet pour une relation (r:embed,
            /// r:id), ou null.</summary>
            public string ImageFor(string relationId)
            {
                string entry;
                if (Images == null || relationId == null || !ImageRels.TryGetValue(relationId, out entry)) return null;
                return Images.Store(entry);
            }

            /// <summary>L'annotation d'un commentaire, créée à sa première plage.</summary>
            public Annotation AnnotationFor(string commentId)
            {
                Annotation annotation;
                if (Annotations.TryGetValue(commentId, out annotation)) return annotation;
                DocxComment comment;
                if (!Comments.TryGetValue(commentId, out comment)) return null;
                annotation = new Annotation
                {
                    Text = CommentMerge.Label(comment.Author, comment.Text),
                    Created = CommentMerge.ToCreated(comment.Date)
                };
                Annotations[commentId] = annotation;
                Document.Annotations.Add(annotation);
                Used.Add(comment);
                return annotation;
            }
        }

        /// <summary>Lit le document ET ses commentaires Word (b49) : chaque
        /// commentaire devient une annotation sur les runs de sa plage ;
        /// la liste rend en plus le passage commenté et son paragraphe
        /// (l'empreinte pour CommentMerge).</summary>
        public static TextDocument ImportWithComments(string path, StyleSheet projectStyles,
            out List<DocxComment> comments, Project project = null)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var styleMap = ReadStyles(zip, projectStyles);
                var footnotes = ReadFootnotes(zip, projectStyles);
                var numbering = ReadNumbering(zip);
                var documentEntry = zip.GetEntry("word/document.xml");
                if (documentEntry == null)
                    throw new InvalidDataException("word/document.xml introuvable : .docx invalide.");

                var xml = LoadXml(documentEntry);
                var ns = Ns(xml);
                var document = new TextDocument();
                foreach (var note in footnotes.Values) document.Footnotes.Add(note);
                var ctx = new ImportContext { Comments = ReadComments(zip), Document = document };
                if (project != null)
                {
                    ctx.Images = new ImportedImages(project, zip);
                    ctx.ImageRels = ReadImageRels(zip);
                }

                var body = xml.SelectSingleNode("//w:body", ns);
                if (body != null) ReadBlock(body, ns, document, projectStyles, styleMap, footnotes, numbering, ctx);
                if (document.Paragraphs.Count == 0) document.Paragraphs.Add(new TextParagraph());

                comments = new List<DocxComment>();
                foreach (var comment in ctx.Used)
                {
                    int first;
                    comment.Anchor = CommentMerge.AnchorOf(document, ctx.Annotations[comment.Id].Id, out first);
                    comment.ParagraphIndex = first;
                    comments.Add(comment);
                }

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

        /// <summary>word/comments.xml (b49) : id → auteur, date, texte (les
        /// paragraphes du commentaire joints par un saut de ligne).</summary>
        private static Dictionary<string, DocxComment> ReadComments(ZipArchive zip)
        {
            var comments = new Dictionary<string, DocxComment>();
            var entry = zip.GetEntry("word/comments.xml");
            if (entry == null) return comments;
            var xml = LoadXml(entry);
            var ns = Ns(xml);
            foreach (XmlNode node in xml.SelectNodes("//w:comment", ns))
            {
                var id = Attr(node, "w:id");
                if (id == null) continue;
                var lines = new List<string>();
                foreach (XmlNode p in node.SelectNodes(".//w:p", ns))
                {
                    var sb = new StringBuilder();
                    foreach (XmlNode textNode in p.SelectNodes(".//w:t", ns)) sb.Append(textNode.InnerText);
                    lines.Add(sb.ToString());
                }
                comments[id] = new DocxComment
                {
                    Id = id,
                    Author = Attr(node, "w:author") ?? "",
                    Date = Attr(node, "w:date") ?? "",
                    Text = string.Join("\n", lines.ToArray()).Trim()
                };
            }
            return comments;
        }

        /// <summary>Les notes du document, runs et formats compris (0.50.0) :
        /// chaque w:p de la note est lu comme un paragraphe (les formats se
        /// mesurent au style « Notes de bas de page » de la feuille cible), les
        /// paragraphes d'une même note se suivent par un saut de ligne.</summary>
        private static Dictionary<string, Footnote> ReadFootnotes(ZipArchive zip, StyleSheet projectStyles)
        {
            var notes = new Dictionary<string, Footnote>();
            var entry = zip.GetEntry("word/footnotes.xml");
            if (entry == null) return notes;
            var xml = LoadXml(entry);
            var ns = Ns(xml);
            var noteStyle = projectStyles != null ? projectStyles.FootnoteStyle() : StyleSheet.DefaultFootnote(null);
            var noNotes = new Dictionary<string, Footnote>();
            var noteCtx = new ImportContext(); // ni commentaires ni images dans une note
            foreach (XmlNode noteNode in xml.SelectNodes("//w:footnote", ns))
            {
                var type = Attr(noteNode, "w:type");
                if (type == "separator" || type == "continuationSeparator") continue;
                var id = Attr(noteNode, "w:id");
                if (id == null) continue;
                var body = new TextParagraph();
                var first = true;
                foreach (XmlNode p in noteNode.SelectNodes("w:p", ns))
                {
                    if (!first) body.Runs.Add(new TextRun { IsLineBreak = true });
                    first = false;
                    ReadRuns(p, ns, body, noteStyle, noNotes, noteCtx);
                }
                // L'espace qui suit l'appel de note (w:footnoteRef) ne fait
                // pas partie du texte.
                while (body.Runs.Count > 0 && !PivotEdit.IsElement(body.Runs[0]))
                {
                    body.Runs[0].Text = body.Runs[0].Text.TrimStart();
                    if (body.Runs[0].Text.Length > 0) break;
                    body.Runs.RemoveAt(0);
                }
                if (body.Runs.Count > 0 && !PivotEdit.IsElement(body.Runs[body.Runs.Count - 1]))
                    body.Runs[body.Runs.Count - 1].Text = body.Runs[body.Runs.Count - 1].Text.TrimEnd();
                var note = new Footnote();
                note.SetRuns(body.Runs);
                notes[id] = note;
            }
            return notes;
        }

        /// <summary>numId → "bullet" | "number", resolved through abstractNum
        /// (level 0's numFmt decides).</summary>
        private static Dictionary<string, string> ReadNumbering(ZipArchive zip)
        {
            var kinds = new Dictionary<string, string>();
            var entry = zip.GetEntry("word/numbering.xml");
            if (entry == null) return kinds;
            try
            {
                var xml = LoadXml(entry);
                var ns = Ns(xml);
                var abstractKinds = new Dictionary<string, string>();
                foreach (XmlNode abstractNode in xml.SelectNodes("//w:abstractNum", ns))
                {
                    var id = Attr(abstractNode, "w:abstractNumId");
                    if (id == null) continue;
                    var format = Attr(abstractNode.SelectSingleNode("w:lvl/w:numFmt", ns), "w:val");
                    abstractKinds[id] = format == "bullet" ? "bullet" : "number";
                }
                foreach (XmlNode numNode in xml.SelectNodes("//w:num", ns))
                {
                    var numId = Attr(numNode, "w:numId");
                    var abstractId = Attr(numNode.SelectSingleNode("w:abstractNumId", ns), "w:val");
                    string kind;
                    if (numId != null && abstractId != null
                        && abstractKinds.TryGetValue(abstractId, out kind))
                        kinds[numId] = kind;
                }
            }
            catch { }
            return kinds;
        }

        private static void ReadBlock(XmlNode container, XmlNamespaceManager ns,
            TextDocument document, StyleSheet projectStyles,
            Dictionary<string, string> styleMap, Dictionary<string, Footnote> footnotes,
            Dictionary<string, string> numbering, ImportContext ctx)
        {
            foreach (XmlNode child in container.ChildNodes)
            {
                if (child.LocalName == "p")
                    document.Paragraphs.Add(ReadParagraph(child, ns, projectStyles, styleMap, footnotes, numbering, ctx));
                else if (child.LocalName == "tbl" || child.LocalName == "tc"
                    || child.LocalName == "tr" || child.LocalName == "sdt"
                    || child.LocalName == "sdtContent")
                    ReadBlock(child, ns, document, projectStyles, styleMap, footnotes, numbering, ctx); // flatten
            }
        }

        private static TextParagraph ReadParagraph(XmlNode p, XmlNamespaceManager ns,
            StyleSheet projectStyles, Dictionary<string, string> styleMap,
            Dictionary<string, Footnote> footnotes, Dictionary<string, string> numbering,
            ImportContext ctx)
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
            // Retrait gauche posé sur le paragraphe lui-même : notre décalage.
            var ind = pPr == null ? null : pPr.SelectSingleNode("w:ind", ns);
            var indLeft = Attr(ind, "w:left");
            double indTwips;
            if (indLeft != null && double.TryParse(indLeft, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out indTwips))
                paragraph.Indent = Math.Max(0, PxFromTwips(indTwips));
            // La première ligne posée sur le paragraphe lui-même (21/09) :
            // alinéa (firstLine) ou retrait suspendu (hanging), depuis le bloc.
            var firstLine = Attr(ind, "w:firstLine");
            var hanging = Attr(ind, "w:hanging");
            double firstTwips;
            if (firstLine != null && double.TryParse(firstLine, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out firstTwips))
                paragraph.FirstIndent = Math.Max(0, (paragraph.Indent ?? style.LeftIndent) + PxFromTwips(firstTwips));
            else if (hanging != null && double.TryParse(hanging, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out firstTwips))
                paragraph.FirstIndent = Math.Max(0, (paragraph.Indent ?? style.LeftIndent) - PxFromTwips(firstTwips));
            if (paragraph.FirstIndent.HasValue)
            {
                // Rien à garder si c'est déjà ce que le style (ou le bloc) donne.
                double left, first;
                var saved = paragraph.FirstIndent;
                paragraph.FirstIndent = null;
                paragraph.EffectiveIndents(style, out left, out first);
                if (Math.Abs(first - saved.Value) > 0.5) paragraph.FirstIndent = saved;
            }
            if (pPr != null && pPr.SelectSingleNode("w:pageBreakBefore", ns) != null)
                paragraph.PageBreakBefore = true;

            var numId = Attr(pPr == null ? null : pPr.SelectSingleNode("w:numPr/w:numId", ns), "w:val");
            if (numId != null)
            {
                string kind;
                paragraph.ListKind = numbering.TryGetValue(numId, out kind) ? kind : "bullet";
            }

            ReadRuns(p, ns, paragraph, style, footnotes, ctx);

            // An empty paragraph carrying only a bottom border is a horizontal rule.
            if (paragraph.Runs.Count == 0 && pPr != null
                && pPr.SelectSingleNode("w:pBdr/w:bottom", ns) != null)
                paragraph.Runs.Add(new TextRun { IsRule = true });
            return paragraph;
        }

        private static void ReadRuns(XmlNode container, XmlNamespaceManager ns,
            TextParagraph paragraph, ParagraphStyle style, Dictionary<string, Footnote> footnotes,
            ImportContext ctx)
        {
            foreach (XmlNode child in container.ChildNodes)
            {
                if (child.LocalName == "hyperlink" || child.LocalName == "smartTag"
                    || child.LocalName == "ins")
                {
                    ReadRuns(child, ns, paragraph, style, footnotes, ctx);
                    continue;
                }
                // Les plages de commentaires (b49) : ouverte, tout run lu
                // jusqu'à sa fermeture porte l'annotation du commentaire.
                if (child.LocalName == "commentRangeStart")
                {
                    var id = Attr(child, "w:id");
                    if (id != null && ctx.Comments.ContainsKey(id) && !ctx.Active.Contains(id)) ctx.Active.Add(id);
                    continue;
                }
                if (child.LocalName == "commentRangeEnd")
                {
                    var id = Attr(child, "w:id");
                    if (id != null) ctx.Active.Remove(id);
                    continue;
                }
                if (child.LocalName != "r") continue;

                var commentReference = child.SelectSingleNode("w:commentReference", ns);
                if (commentReference != null)
                {
                    // Un appel sans plage (commentaire posé sur un point) :
                    // le run qui précède le porte, faute de mieux.
                    var id = Attr(commentReference, "w:id");
                    if (id != null && !ctx.Annotations.ContainsKey(id) && paragraph.Runs.Count > 0)
                    {
                        var last = paragraph.Runs[paragraph.Runs.Count - 1];
                        var annotation = PivotEdit.IsElement(last) ? null : ctx.AnnotationFor(id);
                        if (annotation != null && last.AnnotationId == null) last.AnnotationId = annotation.Id;
                    }
                    continue;
                }

                var reference = child.SelectSingleNode("w:footnoteReference", ns);
                if (reference != null)
                {
                    var id = Attr(reference, "w:id");
                    Footnote note;
                    if (id != null && footnotes.TryGetValue(id, out note))
                        paragraph.Runs.Add(new TextRun { FootnoteId = note.Id });
                    continue;
                }

                // Les images du run (23/09) : chaque dessin (w:drawing inline
                // ou ancré, dans un mc:AlternateContent ou non) et chaque
                // vieux w:pict deviennent un run image, à leur place dans le
                // texte — une même image citée deux fois par le run (Choice +
                // Fallback) n'entre qu'une fois.
                if (ctx != null && ctx.Images != null)
                    foreach (var imageId in ImagesOf(child, ctx))
                        paragraph.Runs.Add(new TextRun { ImageId = imageId });

                var sb = new StringBuilder();
                foreach (XmlNode part in child.ChildNodes)
                {
                    if (part.LocalName == "t") sb.Append(part.InnerText);
                    else if (part.LocalName == "tab") sb.Append('\t');
                    else if (part.LocalName == "br")
                    {
                        FlushRun(paragraph, sb, child, ns, style, ctx);
                        paragraph.Runs.Add(new TextRun { IsLineBreak = true });
                    }
                }
                FlushRun(paragraph, sb, child, ns, style, ctx);
            }
        }

        /// <summary>Les ids d'image d'un run, dans l'ordre et sans doublon :
        /// les a:blip (r:embed) des dessins DrawingML, puis les v:imagedata
        /// (r:id) des dessins VML — une image liée hors du fichier (r:link
        /// seul) n'a pas d'octets, elle est ignorée.</summary>
        private static List<string> ImagesOf(XmlNode run, ImportContext ctx)
        {
            var ids = new List<string>();
            foreach (var pair in new[] { new[] { "blip", "embed" }, new[] { "imagedata", "id" } })
            {
                var nodes = run.SelectNodes(".//*[local-name()='" + pair[0] + "']");
                if (nodes == null) continue;
                foreach (XmlNode node in nodes)
                {
                    var id = ctx.ImageFor(LocalAttr(node, pair[1]));
                    if (id != null && !ids.Contains(id)) ids.Add(id);
                }
            }
            return ids;
        }

        private static string LocalAttr(XmlNode node, string localName)
        {
            if (node == null || node.Attributes == null) return null;
            foreach (XmlAttribute attr in node.Attributes)
                if (attr.LocalName == localName) return attr.Value;
            return null;
        }

        /// <summary>Les relations d'image de word/document.xml : rId → entrée
        /// de l'archive (word/media/…). Une cible externe (TargetMode
        /// External) est laissée de côté.</summary>
        private static Dictionary<string, string> ReadImageRels(ZipArchive zip)
        {
            var rels = new Dictionary<string, string>();
            var entry = zip.GetEntry("word/_rels/document.xml.rels");
            if (entry == null) return rels;
            var xml = LoadXml(entry);
            var nodes = xml.SelectNodes("//*[local-name()='Relationship']");
            if (nodes == null) return rels;
            foreach (XmlNode node in nodes)
            {
                var type = LocalAttr(node, "Type") ?? "";
                if (!type.EndsWith("/image", StringComparison.Ordinal)) continue;
                if (string.Equals(LocalAttr(node, "TargetMode"), "External", StringComparison.OrdinalIgnoreCase)) continue;
                var id = LocalAttr(node, "Id");
                var target = ImportedImages.Resolve("word/", LocalAttr(node, "Target"));
                if (id != null && target != null) rels[id] = target;
            }
            return rels;
        }

        private static void FlushRun(TextParagraph paragraph, StringBuilder sb,
            XmlNode r, XmlNamespaceManager ns, ParagraphStyle style, ImportContext ctx)
        {
            if (sb.Length == 0) return;
            var run = new TextRun { Text = sb.ToString() };
            sb.Length = 0;
            // Dans une plage de commentaire (b49) : le premier ouvert gagne
            // (un run ne porte qu'une annotation).
            if (ctx != null && ctx.Active.Count > 0)
            {
                var annotation = ctx.AnnotationFor(ctx.Active[0]);
                if (annotation != null) run.AnnotationId = annotation.Id;
            }

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
                if (IsOn(rPr.SelectSingleNode("w:noProof", ns))) run.NoProof = true;
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

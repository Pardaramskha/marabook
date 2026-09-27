using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Model;

namespace Marabook.Print
{
    /// <summary>PDF export options — the « BAT » knobs. Lengths in mm.</summary>
    public class PdfExportOptions
    {
        public double BleedMm;      // fond perdu autour du format fini
        public bool CropMarks;      // traits de coupe
        public bool BleedGuides;    // repères : cadre cyan sur la zone de fond perdu
        public bool Cmyk;           // sortie DeviceCMYK + OutputIntent FOGRA39
                                    // (noir texte = 0/0/0/1) ; sinon RVB
        public bool Booklet;        // 4b-3 : imposition en cahier (livret à
                                    // cheval, 2 pages par face, complété à un
                                    // multiple de 4 ; fond perdu/traits ignorés)
        public string Title = "";   // métadonnées du document
    }

    /// <summary>4b-2 : the home-grown PDF writer. Draws the pages EXACTLY as
    /// the composition engine placed them — same lines, same justified glyph
    /// runs, bottom-of-page footnotes, folio, line numbers — into a
    /// print-ready PDF: TrueType fonts embedded as sparse subsets
    /// (CIDFontType2, Identity-H — our glyph runs already speak glyph ids),
    /// FlateDecode streams, MediaBox/BleedBox/TrimBox, bleed and crop marks.
    /// Zero dependency, like everything else.</summary>
    public static class PdfWriter
    {
        public static void Write(string path, Composition composition, PdfExportOptions options)
        {
            var builder = new PdfBuilder(composition, options ?? new PdfExportOptions());
            var bytes = builder.Build();
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
                file.Write(bytes, 0, bytes.Length);
        }
    }

    internal sealed class PdfBuilder
    {
        private const double PxToPt = 72.0 / 96.0;
        private const double MmToPt = 72.0 / 25.4;
        private const double MarkZoneMm = 5;   // room around the bleed for marks

        private readonly Composition _composition;
        private readonly PdfExportOptions _options;

        // Page geometry (points).
        private readonly double _trimW, _trimH;   // finished format
        private readonly double _margin;          // bleed + mark zone
        private readonly double _mediaW, _mediaH;

        private sealed class FontEntry
        {
            public FaceInfo Face;
            public TrueTypeFont Data;
            public HashSet<ushort> Used = new HashSet<ushort>();
            public string Res;                       // /F1
            public int Type0Id, CidId, DescId, FileId, ToUnicodeId;

            // The embedded file only carries its own design (variable fonts:
            // the default instance). A heavier requested weight — WPF bold
            // simulation OR a variable-font instance — is emulated by
            // stroking the fill (Tr 2); simulated italic by a shear matrix.
            public bool EmulateBold;
            public bool EmulateItalic;
        }

        private sealed class ImageEntry
        {
            public string Res;                       // /Im1
            public int Id;
            public int W, H;                         // pixels
            public byte[] Rgb;                       // raw RGB24
        }

        // PIÈGE : GlyphTypeface.Equals compare le FICHIER de police — pour une
        // fonte variable, la Regular et la Bold sont « égales » alors que
        // leurs métriques diffèrent (bug des mots collés du chapitre 1 : le
        // /W du corps venait de l'instance grasse du titre). Clé composite
        // fichier+graisse+style+simulations obligatoire.
        private readonly Dictionary<string, FontEntry> _fontEntries =
            new Dictionary<string, FontEntry>();
        private readonly List<FontEntry> _fontList = new List<FontEntry>();
        private readonly Dictionary<ProjectImage, ImageEntry> _imageMap =
            new Dictionary<ProjectImage, ImageEntry>();
        private readonly List<ImageEntry> _images = new List<ImageEntry>();

        /// <summary>Le moteur de polices de la composition (P0) : faces,
        /// glyphes, fichiers à embarquer, repli rastérisé.</summary>
        private readonly IFontEngine _fonts;

        // Graphics state trackers (per content stream). La sentinelle
        // (alpha 0) : aucune encre ne l'égale, la première est toujours écrite.
        private static readonly Ink Sentinel = Ink.Argb(0, 0, 0, 0);
        private Ink _fill;
        private Ink _stroke;
        private double _tz;

        // Réglages effectifs : l'imposition en cahier neutralise fond perdu et
        // traits (le livret plié se coupe au format fini, dos au pli).
        private readonly bool _booklet;
        private readonly double _bleedPt;
        private readonly bool _cropMarks, _bleedGuides;

        public PdfBuilder(Composition composition, PdfExportOptions options)
        {
            _composition = composition;
            _options = options;
            if (composition.Fonts == null)
                throw new InvalidOperationException("La composition n'a pas de moteur de polices (Composition.Fonts).");
            _fonts = composition.Fonts;
            _booklet = options.Booklet;
            _bleedPt = _booklet ? 0 : options.BleedMm * MmToPt;
            _cropMarks = !_booklet && options.CropMarks;
            _bleedGuides = !_booklet && options.BleedGuides;
            _trimW = composition.PageWidthPx * PxToPt;
            _trimH = composition.PageHeightPx * PxToPt;
            _margin = _bleedPt + (_cropMarks ? MarkZoneMm * MmToPt : 0);
            _mediaW = _trimW + 2 * _margin;
            _mediaH = _trimH + 2 * _margin;
        }

        // ============================================================ build

        public byte[] Build()
        {
            // Pass 1: page contents (registers fonts, used glyphs, images).
            var composedCount = _composition.Pages.Count;
            var pageOps = new List<string>();
            for (var k = 0; k < composedCount; k++)
                pageOps.Add(BuildPageContent(k));

            // 4b-3 — imposition en cahier : deux pages composées par face,
            // complété en pages blanches à un multiple de 4. Feuille s :
            // recto = [N−2s | 2s+1], verso = [2s+2 | N−2s−1] (folios 1-based) —
            // plié en deux, le livret se lit dans l'ordre.
            var contents = new List<byte[]>();
            if (_booklet)
            {
                var padded = ((composedCount + 3) / 4) * 4;
                for (var s = 0; s < padded / 4; s++)
                {
                    contents.Add(BookletSheet(pageOps, padded - 1 - 2 * s, 2 * s));
                    contents.Add(BookletSheet(pageOps, 2 * s + 1, padded - 2 - 2 * s));
                }
            }
            else
                foreach (var ops in pageOps) contents.Add(Latin1(ops));
            var pageCount = contents.Count;

            // Ids: 1 catalog, 2 pages, 3 resources, then page/content pairs,
            // then images, fonts, info.
            var next = 4 + 2 * pageCount;
            foreach (var image in _images) image.Id = next++;
            foreach (var font in _fontList)
            {
                font.Type0Id = next++;
                font.CidId = next++;
                font.DescId = next++;
                font.FileId = next++;
                font.ToUnicodeId = next++;
            }
            var infoId = next++;

            var output = new MemoryStream();
            var offsets = new long[next]; // index = id, [0] unused
            WriteRaw(output, "%PDF-1.4\n");
            output.WriteByte((byte)'%');
            output.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' }, 0, 5);

            // 1 — catalog (CMYK output declares its printing condition:
            // FOGRA39 is a registered identifier, no ICC blob needed)
            offsets[1] = output.Position;
            var intent = _options.Cmyk
                ? " /OutputIntents [<< /Type /OutputIntent /S /GTS_PDFX"
                    + " /OutputConditionIdentifier (FOGRA39)"
                    + " /OutputCondition (Coated FOGRA39 \\(ISO 12647-2:2004\\))"
                    + " /RegistryName (http://www.color.org) >>]"
                : "";
            WriteRaw(output, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R" + intent + " >>\nendobj\n");

            // 2 — pages tree
            offsets[2] = output.Position;
            var kids = new StringBuilder();
            for (var k = 0; k < pageCount; k++)
                kids.Append(4 + 2 * k).Append(" 0 R ");
            WriteRaw(output, "2 0 obj\n<< /Type /Pages /Count " + pageCount
                + " /Kids [" + kids + "] >>\nendobj\n");

            // 3 — shared resources
            offsets[3] = output.Position;
            var resources = new StringBuilder("3 0 obj\n<< /ProcSet [/PDF /Text /ImageC]");
            if (_fontList.Count > 0)
            {
                resources.Append(" /Font <<");
                foreach (var font in _fontList)
                    resources.Append(" /").Append(font.Res).Append(" ")
                        .Append(font.Type0Id).Append(" 0 R");
                resources.Append(" >>");
            }
            if (_images.Count > 0)
            {
                resources.Append(" /XObject <<");
                foreach (var image in _images)
                    resources.Append(" /").Append(image.Res).Append(" ")
                        .Append(image.Id).Append(" 0 R");
                resources.Append(" >>");
            }
            resources.Append(" >>\nendobj\n");
            WriteRaw(output, resources.ToString());

            // pages + contents
            var bleed = _bleedPt;
            var boxes = _booklet
                ? " /MediaBox [0 0 " + N(2 * _trimW) + " " + N(_trimH) + "]"
                : " /MediaBox [0 0 " + N(_mediaW) + " " + N(_mediaH) + "]"
                + " /BleedBox [" + N(_margin - bleed) + " " + N(_margin - bleed) + " "
                    + N(_margin + _trimW + bleed) + " " + N(_margin + _trimH + bleed) + "]"
                + " /TrimBox [" + N(_margin) + " " + N(_margin) + " "
                    + N(_margin + _trimW) + " " + N(_margin + _trimH) + "]";
            for (var k = 0; k < pageCount; k++)
            {
                var pageId = 4 + 2 * k;
                offsets[pageId] = output.Position;
                WriteRaw(output, pageId + " 0 obj\n<< /Type /Page /Parent 2 0 R"
                    + boxes + " /Resources 3 0 R /Contents " + (pageId + 1) + " 0 R >>\nendobj\n");
                offsets[pageId + 1] = output.Position;
                WriteStream(output, pageId + 1, "", contents[k]);
            }

            foreach (var image in _images)
            {
                offsets[image.Id] = output.Position;
                var pixels = image.Rgb;
                var space = "/DeviceRGB";
                if (_options.Cmyk)
                {
                    space = "/DeviceCMYK";
                    pixels = new byte[image.W * image.H * 4];
                    var o = 0;
                    for (var i = 0; i < image.Rgb.Length; i += 3)
                    {
                        double c, m, y, k;
                        RgbToCmyk(image.Rgb[i] / 255.0, image.Rgb[i + 1] / 255.0,
                            image.Rgb[i + 2] / 255.0, out c, out m, out y, out k);
                        pixels[o++] = (byte)Math.Round(c * 255);
                        pixels[o++] = (byte)Math.Round(m * 255);
                        pixels[o++] = (byte)Math.Round(y * 255);
                        pixels[o++] = (byte)Math.Round(k * 255);
                    }
                }
                WriteStream(output, image.Id,
                    " /Type /XObject /Subtype /Image /Width " + image.W
                    + " /Height " + image.H
                    + " /ColorSpace " + space + " /BitsPerComponent 8",
                    pixels);
            }

            foreach (var font in _fontList)
                WriteFont(output, offsets, font);

            offsets[infoId] = output.Position;
            WriteRaw(output, infoId + " 0 obj\n<< /Title " + Utf16String(_options.Title ?? "")
                + " /Producer " + Utf16String("Marabook") + " >>\nendobj\n");

            // xref + trailer
            var xref = output.Position;
            var sb = new StringBuilder();
            sb.Append("xref\n0 ").Append(next).Append("\n");
            sb.Append("0000000000 65535 f \n");
            for (var id = 1; id < next; id++)
                sb.Append(offsets[id].ToString("0000000000", CultureInfo.InvariantCulture))
                    .Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(next)
                .Append(" /Root 1 0 R /Info ").Append(infoId).Append(" 0 R >>\n")
                .Append("startxref\n").Append(xref).Append("\n%%EOF\n");
            WriteRaw(output, sb.ToString());
            return output.ToArray();
        }

        // ============================================================ content

        /// <summary>Une face de cahier : la page de gauche telle quelle, celle
        /// de droite translatée d'une largeur de page. Un index au-delà des
        /// pages composées est une page de complément, blanche.</summary>
        private byte[] BookletSheet(List<string> pageOps, int leftIndex, int rightIndex)
        {
            var sb = new StringBuilder();
            if (leftIndex < pageOps.Count)
                sb.Append("q\n").Append(pageOps[leftIndex]).Append("\nQ\n");
            if (rightIndex < pageOps.Count)
                sb.Append("q 1 0 0 1 ").Append(N(_trimW)).Append(" 0 cm\n")
                    .Append(pageOps[rightIndex]).Append("\nQ\n");
            return Latin1(sb.ToString());
        }

        private string BuildPageContent(int index)
        {
            // Sentinel trackers: the first ink of the page is always written
            // out — a FOGRA39 proof must carry its 0/0/0/1 explicitly, not
            // ride on the DeviceGray default.
            _fill = Sentinel;
            _stroke = Sentinel;
            _tz = 100;
            var setup = _composition.Setup;
            var page = _composition.Pages[index];
            var left = _composition.LeftPxFor(index); // marges en miroir
            var ops = new StringBuilder();

            if (_cropMarks) EmitCropMarks(ops);
            if (_bleedGuides) EmitBleedGuides(ops);

            // Les images de la page (0.50.0), sous le texte.
            foreach (var image in page.Images)
            {
                if (!image.Readable || image.Stored == null) continue;
                var entry = RegisterImage(image.Stored, image.Rect.Width);
                if (entry != null)
                    EmitImage(ops, entry, image.Rect.X, image.Rect.Y, image.Rect.Width, image.Rect.Height);
            }

            foreach (var placed in page.Lines)
                EmitLine(ops, placed.Line, left, placed.Y);

            if (page.NoteLines.Count > 0)
            {
                if (page.NotesRuleY >= 0)
                {
                    var ruleWidth = Math.Min(160, Math.Max(40, setup.ContentWidthPx / 3));
                    EmitRect(ops, left, page.NotesRuleY, ruleWidth, 0.8, Ink.Black);
                }
                foreach (var placed in page.NoteLines)
                    EmitLine(ops, placed.Line, left, placed.Y);
            }

            if (setup.LineNumbers)
            {
                var number = 0;
                var face = _fonts.Resolve("Segoe UI", TextWeights.Normal, false);
                foreach (var placed in page.Lines)
                {
                    number++;
                    var line = placed.Line;
                    var label = _fonts.Measure(face, number.ToString(), 9);
                    EmitSimpleText(ops, number.ToString(), face, 9,
                        Math.Max(2, left - 8 - label.Width),
                        placed.Y + Math.Max(0, (line.Height - label.Height) / 2) + label.Baseline,
                        Ink.Gray);
                }
            }

            // En-tête / pied personnalisés (décor du document ou du gabarit),
            // pied par défaut = folio ; pages blanches d'imposition nues.
            var blank = page.Lines.Count == 0 && page.NoteLines.Count == 0;
            if (!blank)
            {
                var decor = _composition.DecorOf(index);
                var recto = _composition.FolioOf(index) % 2 == 1;
                var firstOfDoc = index == 0
                    || !ReferenceEquals(decor, _composition.DecorOf(index - 1));
                var header = decor == null ? null : (recto ? decor.HeaderRecto : decor.HeaderVerso);
                var footer = decor == null ? null : (recto ? decor.FooterRecto : decor.FooterVerso);
                if (decor != null && decor.HeaderHideFirst && firstOfDoc) header = null;
                var footerHidden = decor != null && decor.FooterHideFirst && firstOfDoc;
                var title = decor == null ? "" : decor.Title;
                var book = decor == null ? "" : decor.BookTitle;
                if (header != null && !header.IsEmpty)
                    EmitDecor(ops, header, index, title, book, left, true,
                        decor == null ? 0 : decor.HeaderGapMm);
                if (footer != null && !footer.IsEmpty)
                {
                    if (!footerHidden)
                        EmitDecor(ops, footer, index, title, book, left, false,
                            decor == null ? 0 : decor.FooterGapMm);
                }
                else if (setup.FooterPageNumbers && !footerHidden
                    && (decor == null || !decor.SuppressFolio))
                {
                    var label = _composition.FolioOf(index).ToString();
                    var face = _fonts.Resolve(setup.FooterFont ?? "Times New Roman", TextWeights.Normal, false);
                    var size = Math.Max(6, setup.FooterSizePt * 4.0 / 3.0);
                    var folio = _fonts.Measure(face, label, size);
                    EmitSimpleText(ops, label, face, size,
                        (_composition.PageWidthPx - folio.Width) / 2,
                        _composition.PageHeightPx - _composition.BottomPx / 2
                            - folio.Height / 2 + folio.Baseline,
                        Ink.Black);
                }
            }
            return ops.ToString();
        }

        private sealed class DecorRun
        {
            public string Text;
            public FaceInfo Face;
            public double SizePx;
            public Ink Ink;
            public TextExtent Measured;
        }

        private void EmitDecor(StringBuilder ops, HeaderFooter decor, int index,
            string title, string book, double left, bool isHeader, double gapMm)
        {
            var setup = _composition.Setup;
            var folio = _composition.FolioOf(index);
            var pages = _composition.Pages.Count + _composition.FolioOffset;
            var runs = new List<DecorRun>();
            var align = decor.Align;
            if (decor.Rich != null)
            {
                if (decor.Rich.AlignOverride != null) align = decor.Rich.AlignOverride;
                foreach (var run in decor.Rich.Runs)
                {
                    var text = new HeaderFooter { Text = run.Text }.Expand(folio, pages, title, book);
                    if (text.Length == 0) continue;
                    runs.Add(new DecorRun
                    {
                        Text = text,
                        Face = _fonts.Resolve(
                            run.FontFamily ?? setup.FooterFont ?? "Times New Roman",
                            run.Weight != null ? TextWeights.Parse(run.Weight)
                                : run.Bold == true ? TextWeights.Bold : TextWeights.Normal,
                            run.Italic == true),
                        SizePx = run.FontSize ?? Math.Max(6, decor.SizePt * 4.0 / 3.0),
                        Ink = run.Color != null ? Ink.Parse(run.Color) : Ink.Black
                    });
                }
            }
            else
            {
                var text = decor.Expand(folio, pages, title, book);
                if (text.Trim().Length == 0) return;
                runs.Add(new DecorRun
                {
                    Text = text,
                    Face = _fonts.Resolve(
                        decor.FontFamily ?? setup.FooterFont ?? "Times New Roman",
                        decor.Bold ? TextWeights.Bold : TextWeights.Normal,
                        decor.Italic),
                    SizePx = Math.Max(6, decor.SizePt * 4.0 / 3.0),
                    Ink = Ink.Black
                });
            }
            if (runs.Count == 0) return;

            double totalWidth = 0, maxHeight = 0, maxBaseline = 0;
            foreach (var run in runs)
            {
                run.Measured = _fonts.Measure(run.Face, run.Text, run.SizePx);
                totalWidth += run.Measured.Width;
                if (run.Measured.Height > maxHeight) maxHeight = run.Measured.Height;
                if (run.Measured.Baseline > maxBaseline) maxBaseline = run.Measured.Baseline;
            }
            var contentWidth = setup.ContentWidthPx;
            var x = align == "left" ? left
                  : align == "right" ? left + contentWidth - totalWidth
                  : left + (contentWidth - totalWidth) / 2;
            var top = _composition.TopPx;
            var height = _composition.PageHeightPx;
            var bottom = _composition.BottomPx;
            // Écart signé et continu depuis la position centrée (voir
            // ComposedRenderer — mêmes formules).
            var gap = gapMm * PageSetup.PxPerMm;
            var y = isHeader
                ? Math.Max(2, top / 2 - maxHeight / 2 - gap)
                : Math.Min(height - maxHeight - 2,
                    height - bottom / 2 - maxHeight / 2 + gap);
            foreach (var run in runs)
            {
                EmitSimpleText(ops, run.Text, run.Face, run.SizePx,
                    x, y + maxBaseline, run.Ink);
                x += run.Measured.Width;
            }
        }

        private void EmitLine(StringBuilder ops, ComposedLine line, double leftPx, double topPx)
        {
            var baseline = topPx + line.Ascent;

            // Highlights first, behind the ink (spaces included). Les teintes
            // d'annotation (semi-transparentes) ne vont jamais au papier.
            foreach (var piece in line.Pieces)
            {
                if (!piece.Highlight.HasValue) continue;
                if (piece.Highlight.Value.IsTranslucent) continue;
                var w = piece.VisualWidth();
                if (w < 0.1) continue;
                var size = piece.FontSizePx > 0 ? piece.FontSizePx : 16;
                EmitRect(ops, leftPx + piece.Origin.X,
                    baseline + piece.Origin.Y - size * 0.8, w, size * 1.05,
                    piece.Highlight.Value);
            }

            foreach (var piece in line.Pieces)
            {
                EmitDecorations(ops, piece, leftPx, baseline);
                if (piece.IsSpace) continue;
                if (piece.IsRule)
                {
                    EmitRect(ops, leftPx + piece.Rect.X, topPx + line.Height / 2,
                        piece.Rect.Width, piece.Rect.Height, Ink.Black);
                    continue;
                }
                if (piece.IsAnchor) continue; // l'ancre d'une image : rien sur le papier
                if (piece.IsGlyphs)
                {
                    EmitGlyphs(ops, piece, leftPx, baseline);
                    continue;
                }
                if (piece.IsFallback)
                    EmitFallback(ops, piece, leftPx, baseline);
            }
        }

        /// <summary>Underline / strikethrough — same geometry as the screen
        /// renderer (font metrics when available, ratios otherwise).</summary>
        private void EmitDecorations(StringBuilder ops, ComposedPiece piece,
            double leftPx, double baselinePx)
        {
            if (!piece.Underline && !piece.Strike) return;
            var w = piece.VisualWidth();
            if (w < 0.1) return;
            var size = piece.FontSizePx > 0 ? piece.FontSizePx : 16;
            var face = piece.IsGlyphs ? piece.Face : null;
            var ink = piece.Ink;
            var x = leftPx + piece.Origin.X;
            var y = baselinePx + piece.Origin.Y;
            if (piece.Underline)
            {
                var offset = face != null ? -face.UnderlinePosition * size : size * 0.09;
                var thickness = face != null
                    ? Math.Max(0.8, face.UnderlineThickness * size)
                    : Math.Max(0.8, size * 0.05);
                EmitRect(ops, x, y + offset, w, thickness, ink);
            }
            if (piece.Strike)
            {
                var offset = face != null ? -face.StrikethroughPosition * size : -size * 0.3;
                var thickness = face != null
                    ? Math.Max(0.8, face.StrikethroughThickness * size)
                    : Math.Max(0.8, size * 0.05);
                EmitRect(ops, x, y + offset, w, thickness, ink);
            }
        }

        /// <summary>A justified glyph run: the piece's advances are the FINAL
        /// screen advances (letter-spacing baked in, glyph scaling via Tz), so
        /// each TJ adjustment is natural width minus wanted advance.</summary>
        private void EmitGlyphs(StringBuilder ops, ComposedPiece piece,
            double leftPx, double baselinePx)
        {
            var font = GetFont(piece.Face);
            var sizePx = piece.EmSize;
            if (sizePx <= 0) return;

            var ink = piece.Ink;
            SetFill(ops, ink);
            ops.Append("BT /").Append(font.Res).Append(" ")
                .Append(N(sizePx * PxToPt)).Append(" Tf\n");
            var tz = piece.ScaleX * 100;
            if (Math.Abs(tz - _tz) > 0.01)
            {
                ops.Append(N(tz)).Append(" Tz\n");
                _tz = tz;
            }
            if (font.EmulateBold)
            {
                // The embedded outlines carry the file's own weight (variable
                // fonts: the default instance) — thicken by stroking the fill.
                SetStroke(ops, ink);
                ops.Append("2 Tr ").Append(N(Math.Max(0.2, sizePx * PxToPt * 0.028)))
                    .Append(" w\n");
            }
            var x = XPt(leftPx + piece.Origin.X);
            var y = YPt(baselinePx + piece.Origin.Y);
            if (font.EmulateItalic)
                ops.Append("1 0 0.2126 1 ").Append(N(x)).Append(" ")
                    .Append(N(y)).Append(" Tm\n[<");
            else
                ops.Append(N(x)).Append(" ").Append(N(y)).Append(" Td\n[<");

            var indices = piece.GlyphIndices;
            for (var i = 0; i < indices.Length; i++)
            {
                var glyph = indices[i];
                font.Used.Add(glyph);
                ops.Append(glyph.ToString("X4"));
                var natural = Math.Round(_fonts.GlyphAdvance(piece.Face, glyph) * 1000);
                var wanted = piece.Advances[i] * 1000.0 / sizePx;
                var adjust = (int)Math.Round(natural - wanted);
                if (adjust != 0 && i < indices.Length - 1)
                    ops.Append("> ").Append(adjust).Append(" <");
            }
            ops.Append(">] TJ\n");
            if (font.EmulateBold) ops.Append("0 Tr\n");
            ops.Append("ET\n");
        }

        /// <summary>Characters outside the font (the composer's platform
        /// fallback) are rasterized by the font engine — rare, and paper-exact.</summary>
        private void EmitFallback(StringBuilder ops, ComposedPiece piece,
            double leftPx, double baselinePx)
        {
            const double scale = 3.0;
            var wPx = piece.FallbackWidth;
            var hPx = piece.FallbackHeight;
            if (wPx < 0.1 || hPx < 0.1) return;
            int pixelW, pixelH;
            var rgb = _fonts.RasterizeText(piece.Face, piece.Text, piece.EmSize, piece.Ink, scale,
                out pixelW, out pixelH);
            if (rgb == null) return;
            var entry = AddImage(rgb, pixelW, pixelH);
            if (entry == null) return;
            var x = leftPx + piece.Origin.X;
            var top = baselinePx + piece.Origin.Y - piece.FallbackBaseline;
            EmitImage(ops, entry, x, top, wPx * piece.ScaleX, hPx);
        }

        /// <summary>Plain label (folio, line numbers): natural advances, no
        /// justification. baselinePx matches the on-screen measure.</summary>
        private void EmitSimpleText(StringBuilder ops, string text, FaceInfo face,
            double sizePx, double xPx, double baselinePx, Ink color)
        {
            if (face == null || !face.HasGlyphs) return;
            var font = GetFont(face);
            var hex = new StringBuilder();
            foreach (var c in text)
            {
                ushort glyph;
                if (!_fonts.TryGlyph(face, c, out glyph)) continue;
                font.Used.Add(glyph);
                hex.Append(glyph.ToString("X4"));
            }
            if (hex.Length == 0) return;
            SetFill(ops, color);
            if (Math.Abs(_tz - 100) > 0.01) { ops.Append("100 Tz\n"); _tz = 100; }
            ops.Append("BT /").Append(font.Res).Append(" ").Append(N(sizePx * PxToPt))
                .Append(" Tf\n").Append(N(XPt(xPx))).Append(" ").Append(N(YPt(baselinePx)))
                .Append(" Td\n<").Append(hex).Append("> Tj\nET\n");
        }

        private void EmitRect(StringBuilder ops, double xPx, double yPx,
            double wPx, double hPx, Ink color)
        {
            SetFill(ops, color);
            ops.Append(N(XPt(xPx))).Append(" ").Append(N(YPt(yPx + hPx))).Append(" ")
                .Append(N(wPx * PxToPt)).Append(" ").Append(N(hPx * PxToPt))
                .Append(" re f\n");
        }

        private void EmitImage(StringBuilder ops, ImageEntry image,
            double xPx, double yPx, double wPx, double hPx)
        {
            ops.Append("q ").Append(N(wPx * PxToPt)).Append(" 0 0 ")
                .Append(N(hPx * PxToPt)).Append(" ")
                .Append(N(XPt(xPx))).Append(" ").Append(N(YPt(yPx + hPx)))
                .Append(" cm /").Append(image.Res).Append(" Do Q\n");
        }

        private void EmitCropMarks(StringBuilder ops)
        {
            var gap = _options.BleedMm * MmToPt + 2;
            var len = MarkZoneMm * MmToPt - 3;
            if (len <= 1) return;
            var x0 = _margin;
            var x1 = _margin + _trimW;
            var y0 = _margin;
            var y1 = _margin + _trimH;
            ops.Append("0.25 w ").Append(_options.Cmyk ? "0 0 0 1 K\n" : "0 G\n");
            _stroke = Sentinel; // tracker invalidated
            // horizontal marks (left/right of the trim corners)
            AppendMark(ops, x0 - gap - len, y0, x0 - gap, y0);
            AppendMark(ops, x0 - gap - len, y1, x0 - gap, y1);
            AppendMark(ops, x1 + gap, y0, x1 + gap + len, y0);
            AppendMark(ops, x1 + gap, y1, x1 + gap + len, y1);
            // vertical marks (below/above the trim corners)
            AppendMark(ops, x0, y0 - gap - len, x0, y0 - gap);
            AppendMark(ops, x1, y0 - gap - len, x1, y0 - gap);
            AppendMark(ops, x0, y1 + gap, x0, y1 + gap + len);
            AppendMark(ops, x1, y1 + gap, x1, y1 + gap + len);
        }

        /// <summary>Bleed guides: a cyan frame on the bleed box (and, when a
        /// bleed is set, a second one on the trim box) so the zone reads at a
        /// glance on the proof.</summary>
        private void EmitBleedGuides(StringBuilder ops)
        {
            var bleed = _options.BleedMm * MmToPt;
            ops.Append("0.5 w ").Append(_options.Cmyk ? "1 0 0 0 K\n" : "0.24 0.77 0.9 RG\n");
            ops.Append(N(_margin - bleed)).Append(" ").Append(N(_margin - bleed)).Append(" ")
                .Append(N(_trimW + 2 * bleed)).Append(" ").Append(N(_trimH + 2 * bleed))
                .Append(" re S\n");
            if (bleed > 0.1)
                ops.Append(N(_margin)).Append(" ").Append(N(_margin)).Append(" ")
                    .Append(N(_trimW)).Append(" ").Append(N(_trimH)).Append(" re S\n");
            // Invalidate the stroke tracker (sentinel no ink will ever equal).
            _stroke = Sentinel;
        }

        private static void AppendMark(StringBuilder ops,
            double xa, double ya, double xb, double yb)
        {
            ops.Append(N(xa)).Append(" ").Append(N(ya)).Append(" m ")
                .Append(N(xb)).Append(" ").Append(N(yb)).Append(" l S\n");
        }

        private void SetFill(StringBuilder ops, Ink color)
        {
            if (color == _fill) return;
            _fill = color;
            ops.Append(ColorOps(color, false));
        }

        private void SetStroke(StringBuilder ops, Ink color)
        {
            if (color == _stroke) return;
            _stroke = color;
            ops.Append(ColorOps(color, true));
        }

        /// <summary>rg/RG in RGB output, k/K in CMYK output. The CMYK
        /// conversion keeps true black on the K channel alone (0 0 0 1) —
        /// what a printer expects for text.</summary>
        private string ColorOps(Ink color, bool stroke)
        {
            if (!_options.Cmyk)
                return N(color.R / 255.0) + " " + N(color.G / 255.0) + " "
                    + N(color.B / 255.0) + (stroke ? " RG\n" : " rg\n");
            double c, m, y, k;
            RgbToCmyk(color.R / 255.0, color.G / 255.0, color.B / 255.0,
                out c, out m, out y, out k);
            return N(c) + " " + N(m) + " " + N(y) + " " + N(k)
                + (stroke ? " K\n" : " k\n");
        }

        private static void RgbToCmyk(double r, double g, double b,
            out double c, out double m, out double y, out double k)
        {
            k = 1 - Math.Max(r, Math.Max(g, b));
            if (k > 0.999) { c = 0; m = 0; y = 0; k = 1; return; }
            c = (1 - r - k) / (1 - k);
            m = (1 - g - k) / (1 - k);
            y = (1 - b - k) / (1 - k);
        }

        // px → pt with the bleed/marks offset; PDF y grows upward.
        private double XPt(double xPx) { return _margin + xPx * PxToPt; }
        private double YPt(double yPx)
        {
            return _margin + (_composition.PageHeightPx - yPx) * PxToPt;
        }

        // ============================================================ fonts

        private FontEntry GetFont(FaceInfo face)
        {
            // La clé de la face (fichier + graisse + style + simulations) :
            // deux instances d'une fonte variable ne se confondent pas.
            var key = face.Key;
            FontEntry entry;
            if (_fontEntries.TryGetValue(key, out entry)) return entry;
            int faceIndex;
            string fallbackName;
            var file = _fonts.FontFile(face, out faceIndex, out fallbackName);
            entry = new FontEntry
            {
                Face = face,
                Data = TrueTypeFont.Load(file, faceIndex, fallbackName),
                Res = "F" + (_fontList.Count + 1)
            };
            var fileWeight = entry.Data != null ? entry.Data.WeightClass : 400;
            // La graisse SERVIE (pas la demandée) : une instance de fonte
            // variable plus lourde que le fichier s'épaissit ; un demi-gras
            // rendu par le fichier regular reste regular, comme à l'écran.
            entry.EmulateBold = face.SimulatedBold || face.ActualWeight >= fileWeight + 150;
            entry.EmulateItalic = face.SimulatedItalic;
            _fontEntries[key] = entry;
            _fontList.Add(entry);
            return entry;
        }

        private void WriteFont(MemoryStream output, long[] offsets, FontEntry font)
        {
            var face = font.Face;
            var data = font.Data;
            var scale = data != null ? 1000.0 / data.UnitsPerEm : 1.0;
            var psName = data != null ? data.PostScriptName : "Embedded";
            // Variable-font instances share one PostScript name; suffix the
            // requested weight so the two faces stay distinct for viewers.
            if (data != null && face.ActualWeight != data.WeightClass)
                psName += "-W" + face.ActualWeight;
            var baseName = SubsetTag(font) + "+" + psName;
            var italic = face.ActualItalic;

            byte[] fontFile = null;
            var fontFileKey = "/FontFile2";
            if (data != null)
            {
                if (data.IsCff)
                {
                    fontFile = data.Bytes;
                    fontFileKey = "/FontFile3";
                }
                else fontFile = data.Subset(font.Used);
            }

            // Type0
            offsets[font.Type0Id] = output.Position;
            WriteRaw(output, font.Type0Id + " 0 obj\n<< /Type /Font /Subtype /Type0 /BaseFont /"
                + baseName + " /Encoding /Identity-H /DescendantFonts [" + font.CidId
                + " 0 R] /ToUnicode " + font.ToUnicodeId + " 0 R >>\nendobj\n");

            // CIDFontType2 + widths
            offsets[font.CidId] = output.Position;
            var w = new StringBuilder();
            var used = new List<ushort>(font.Used);
            used.Sort();
            var i = 0;
            while (i < used.Count)
            {
                var j = i;
                while (j + 1 < used.Count && used[j + 1] == used[j] + 1) j++;
                w.Append(used[i]).Append(" [");
                for (var g = i; g <= j; g++)
                {
                    if (g > i) w.Append(" ");
                    w.Append(N(Math.Round(_fonts.GlyphAdvance(face, used[g]) * 1000)));
                }
                w.Append("] ");
                i = j + 1;
            }
            WriteRaw(output, font.CidId + " 0 obj\n<< /Type /Font /Subtype /CIDFontType2 /BaseFont /"
                + baseName + " /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity)"
                + " /Supplement 0 >> /FontDescriptor " + font.DescId
                + " 0 R /DW 1000 /W [" + w + "] /CIDToGIDMap /Identity >>\nendobj\n");

            // Descriptor
            offsets[font.DescId] = output.Position;
            var flags = 4 | (italic ? 64 : 0); // symbolic (+ italic)
            var bold = face.ActualWeight >= 600;
            var descriptor = new StringBuilder();
            descriptor.Append(font.DescId).Append(" 0 obj\n<< /Type /FontDescriptor /FontName /")
                .Append(baseName).Append(" /Flags ").Append(flags);
            if (data != null)
                descriptor.Append(" /FontBBox [").Append(N(data.XMin * scale)).Append(" ")
                    .Append(N(data.YMin * scale)).Append(" ").Append(N(data.XMax * scale))
                    .Append(" ").Append(N(data.YMax * scale)).Append("]")
                    .Append(" /ItalicAngle ").Append(N(data.ItalicAngle))
                    .Append(" /Ascent ").Append(N(data.Ascender * scale))
                    .Append(" /Descent ").Append(N(data.Descender * scale))
                    .Append(" /CapHeight ").Append(N(data.CapHeight * scale));
            else
                descriptor.Append(" /FontBBox [-1000 -300 2000 1000] /ItalicAngle 0")
                    .Append(" /Ascent 800 /Descent -200 /CapHeight 700");
            descriptor.Append(" /StemV ").Append(bold ? 160 : 80);
            if (fontFile != null)
                descriptor.Append(" ").Append(fontFileKey).Append(" ")
                    .Append(font.FileId).Append(" 0 R");
            descriptor.Append(" >>\nendobj\n");
            WriteRaw(output, descriptor.ToString());

            // Font program
            offsets[font.FileId] = output.Position;
            if (fontFile != null)
                WriteStream(output, font.FileId,
                    fontFileKey == "/FontFile3"
                        ? " /Subtype /OpenType"
                        : " /Length1 " + fontFile.Length,
                    fontFile);
            else
                WriteRaw(output, font.FileId + " 0 obj\nnull\nendobj\n");

            // ToUnicode
            offsets[font.ToUnicodeId] = output.Position;
            WriteStream(output, font.ToUnicodeId, "", Latin1(BuildToUnicode(font)));
        }

        private static string SubsetTag(FontEntry font)
        {
            var hash = 5381;
            var name = (font.Face.File ?? font.Face.Key) + font.Res;
            foreach (var c in name) hash = hash * 33 + c;
            var tag = new char[6];
            for (var i = 0; i < 6; i++)
            {
                tag[i] = (char)('A' + (hash & 0x7FFFFFFF) % 26);
                hash = hash * 31 + 7;
            }
            return new string(tag);
        }

        private string BuildToUnicode(FontEntry font)
        {
            // Reverse cmap: glyph id → first Unicode that produces it.
            var reverse = new Dictionary<ushort, int>();
            foreach (var pair in _fonts.CharacterMap(font.Face))
                if (!reverse.ContainsKey(pair.Value)) reverse[pair.Value] = pair.Key;

            var entries = new List<string>();
            foreach (var glyph in font.Used)
            {
                int code;
                if (!reverse.TryGetValue(glyph, out code) || code > 0xFFFF) continue;
                entries.Add("<" + glyph.ToString("X4") + "> <" + code.ToString("X4") + ">");
            }

            var sb = new StringBuilder();
            sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
            sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
            sb.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
            sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
            for (var i = 0; i < entries.Count; i += 100)
            {
                var batch = Math.Min(100, entries.Count - i);
                sb.Append(batch).Append(" beginbfchar\n");
                for (var j = 0; j < batch; j++) sb.Append(entries[i + j]).Append("\n");
                sb.Append("endbfchar\n");
            }
            sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
            return sb.ToString();
        }

        // ============================================================ images

        private ImageEntry RegisterImage(ProjectImage stored, double targetWidthPx)
        {
            ImageEntry entry;
            if (_imageMap.TryGetValue(stored, out entry)) return entry;
            // Cap the embedded resolution at ~3× the placed size (≈ 290 dpi) :
            // le codec de la plate-forme décode, réduit et aplatit sur blanc.
            int width, height;
            var rgb = Platform.Images.ToRgb24(stored.Bytes, targetWidthPx * 3.0, out width, out height);
            if (rgb == null) return null;
            entry = AddImage(rgb, width, height);
            if (entry != null) _imageMap[stored] = entry;
            return entry;
        }

        /// <summary>RGB24 over white (paper), Flate-compressed at write time.</summary>
        private ImageEntry AddImage(byte[] rgb, int width, int height)
        {
            if (rgb == null || width <= 0 || height <= 0 || rgb.Length < width * height * 3) return null;
            var entry = new ImageEntry
            {
                Res = "Im" + (_images.Count + 1),
                W = width,
                H = height,
                Rgb = rgb
            };
            _images.Add(entry);
            return entry;
        }

        // ============================================================ low level

        private static void WriteRaw(MemoryStream output, string text)
        {
            var bytes = Latin1(text);
            output.Write(bytes, 0, bytes.Length);
        }

        private static void WriteStream(MemoryStream output, int id, string extraDict, byte[] data)
        {
            var deflated = Zlib(data);
            WriteRaw(output, id + " 0 obj\n<<" + extraDict + " /Filter /FlateDecode /Length "
                + deflated.Length + " >>\nstream\n");
            output.Write(deflated, 0, deflated.Length);
            WriteRaw(output, "\nendstream\nendobj\n");
        }

        /// <summary>zlib envelope around .NET's raw deflate (PDF FlateDecode
        /// wants the header and the Adler-32 trailer).</summary>
        private static byte[] Zlib(byte[] data)
        {
            using (var buffer = new MemoryStream())
            {
                buffer.WriteByte(0x78);
                buffer.WriteByte(0x9C);
                using (var deflate = new DeflateStream(buffer, CompressionMode.Compress, true))
                    deflate.Write(data, 0, data.Length);
                uint a = 1, b = 0;
                foreach (var value in data)
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }
                var adler = (b << 16) | a;
                buffer.WriteByte((byte)(adler >> 24));
                buffer.WriteByte((byte)(adler >> 16));
                buffer.WriteByte((byte)(adler >> 8));
                buffer.WriteByte((byte)adler);
                return buffer.ToArray();
            }
        }

        private static byte[] Latin1(string text)
        {
            return Encoding.GetEncoding(28591).GetBytes(text);
        }

        /// <summary>UTF-16BE hex string with BOM — no escaping headaches.</summary>
        private static string Utf16String(string text)
        {
            var sb = new StringBuilder("<FEFF");
            foreach (var c in text) sb.Append(((int)c).ToString("X4"));
            return sb.Append(">").ToString();
        }

        private static string N(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}

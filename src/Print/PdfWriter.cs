using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UniversSale.Model;

namespace UniversSale.Print
{
    /// <summary>PDF export options — the « BAT » knobs. Lengths in mm.</summary>
    public class PdfExportOptions
    {
        public double BleedMm;      // fond perdu autour du format fini
        public bool CropMarks;      // traits de coupe
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
            public GlyphTypeface Typeface;
            public TrueTypeFont Data;
            public HashSet<ushort> Used = new HashSet<ushort>();
            public string Res;                       // /F1
            public int Type0Id, CidId, DescId, FileId, ToUnicodeId;
        }

        private sealed class ImageEntry
        {
            public string Res;                       // /Im1
            public int Id;
            public int W, H;                         // pixels
            public byte[] Rgb;                       // raw RGB24
        }

        private readonly Dictionary<GlyphTypeface, FontEntry> _fonts =
            new Dictionary<GlyphTypeface, FontEntry>();
        private readonly List<FontEntry> _fontList = new List<FontEntry>();
        private readonly Dictionary<ImageSource, ImageEntry> _imageMap =
            new Dictionary<ImageSource, ImageEntry>();
        private readonly List<ImageEntry> _images = new List<ImageEntry>();

        // GlyphTypeface instances are NOT canonical: TryGetGlyphTypeface can
        // hand a fresh object each call, and _fonts keys by reference. Labels
        // (folio, line numbers) resolve through this cache instead.
        private readonly Dictionary<string, GlyphTypeface> _labelTypefaces =
            new Dictionary<string, GlyphTypeface>();

        // Graphics state trackers (per content stream).
        private Color _fill;
        private double _tz;

        public PdfBuilder(Composition composition, PdfExportOptions options)
        {
            _composition = composition;
            _options = options;
            _trimW = composition.PageWidthPx * PxToPt;
            _trimH = composition.PageHeightPx * PxToPt;
            _margin = options.BleedMm * MmToPt
                + (options.CropMarks ? MarkZoneMm * MmToPt : 0);
            _mediaW = _trimW + 2 * _margin;
            _mediaH = _trimH + 2 * _margin;
        }

        // ============================================================ build

        public byte[] Build()
        {
            // Pass 1: page contents (registers fonts, used glyphs, images).
            var pageCount = _composition.Pages.Count;
            var contents = new List<byte[]>();
            for (var k = 0; k < pageCount; k++)
                contents.Add(Latin1(BuildPageContent(k)));

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

            // 1 — catalog
            offsets[1] = output.Position;
            WriteRaw(output, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

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
            var bleed = _options.BleedMm * MmToPt;
            var boxes = " /MediaBox [0 0 " + N(_mediaW) + " " + N(_mediaH) + "]"
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
                WriteStream(output, image.Id,
                    " /Type /XObject /Subtype /Image /Width " + image.W
                    + " /Height " + image.H
                    + " /ColorSpace /DeviceRGB /BitsPerComponent 8",
                    image.Rgb);
            }

            foreach (var font in _fontList)
                WriteFont(output, offsets, font);

            offsets[infoId] = output.Position;
            WriteRaw(output, infoId + " 0 obj\n<< /Title " + Utf16String(_options.Title ?? "")
                + " /Producer " + Utf16String("Univers Sale") + " >>\nendobj\n");

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

        private string BuildPageContent(int index)
        {
            _fill = Colors.Black;
            _tz = 100;
            var setup = _composition.Setup;
            var page = _composition.Pages[index];
            var left = _composition.LeftPx;
            var ops = new StringBuilder();

            if (_options.CropMarks) EmitCropMarks(ops);

            foreach (var placed in page.Lines)
                EmitLine(ops,
                    _composition.Paragraphs[placed.ParagraphIndex].Lines[placed.LineIndex],
                    left, placed.Y);

            if (page.NoteLines.Count > 0)
            {
                if (page.NotesRuleY >= 0)
                {
                    var right = setup.MarginRightMm * PageSetup.PxPerMm;
                    var ruleWidth = Math.Min(160,
                        Math.Max(40, (_composition.PageWidthPx - left - right) / 3));
                    EmitRect(ops, left, page.NotesRuleY, ruleWidth, 0.8, Colors.Black);
                }
                foreach (var placed in page.NoteLines)
                    EmitLine(ops,
                        _composition.NoteParagraphs[placed.ParagraphIndex].Lines[placed.LineIndex],
                        left, placed.Y);
            }

            if (setup.LineNumbers)
            {
                var number = 0;
                var typeface = new Typeface("Segoe UI");
                foreach (var placed in page.Lines)
                {
                    number++;
                    var line = _composition.Paragraphs[placed.ParagraphIndex].Lines[placed.LineIndex];
                    var label = new FormattedText(
                        number.ToString(), CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight, typeface, 9, Brushes.Gray, 1.0);
                    EmitSimpleText(ops, number.ToString(), typeface, 9,
                        Math.Max(2, left - 8 - label.Width),
                        placed.Y + Math.Max(0, (line.Height - label.Height) / 2) + label.Baseline,
                        Colors.Gray);
                }
            }

            if (setup.FooterPageNumbers)
            {
                var typeface = new Typeface(setup.FooterFont ?? "Times New Roman");
                var size = Math.Max(6, setup.FooterSizePt * 4.0 / 3.0);
                var folio = new FormattedText((index + 1).ToString(),
                    CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, size, Brushes.Black, 1.0);
                EmitSimpleText(ops, (index + 1).ToString(), typeface, size,
                    (_composition.PageWidthPx - folio.Width) / 2,
                    _composition.PageHeightPx - _composition.BottomPx / 2
                        - folio.Height / 2 + folio.Baseline,
                    Colors.Black);
            }
            return ops.ToString();
        }

        private void EmitLine(StringBuilder ops, ComposedLine line, double leftPx, double topPx)
        {
            var baseline = topPx + line.Ascent;
            foreach (var piece in line.Pieces)
            {
                if (piece.IsSpace) continue;
                if (piece.IsRule)
                {
                    EmitRect(ops, leftPx + piece.Rect.X, topPx + line.Height / 2,
                        piece.Rect.Width, piece.Rect.Height, Colors.Black);
                    continue;
                }
                if (piece.Image != null)
                {
                    var entry = RegisterImage(piece.Image, piece.Rect.Width);
                    if (entry != null)
                        EmitImage(ops, entry, leftPx + piece.Rect.X, topPx + piece.Rect.Y,
                            piece.Rect.Width, piece.Rect.Height);
                    continue;
                }
                if (piece.Glyphs != null)
                {
                    EmitGlyphs(ops, piece, leftPx, baseline);
                    continue;
                }
                if (piece.Fallback != null)
                    EmitFallback(ops, piece, leftPx, baseline);
            }
        }

        /// <summary>A justified glyph run: the piece's advances are the FINAL
        /// screen advances (letter-spacing baked in, glyph scaling via Tz), so
        /// each TJ adjustment is natural width minus wanted advance.</summary>
        private void EmitGlyphs(StringBuilder ops, ComposedPiece piece,
            double leftPx, double baselinePx)
        {
            var run = piece.Glyphs;
            var font = GetFont(run.GlyphTypeface);
            var sizePx = run.FontRenderingEmSize;
            if (sizePx <= 0) return;

            SetFill(ops, InkColor(piece.Ink));
            ops.Append("BT /").Append(font.Res).Append(" ")
                .Append(N(sizePx * PxToPt)).Append(" Tf\n");
            var tz = piece.ScaleX * 100;
            if (Math.Abs(tz - _tz) > 0.01)
            {
                ops.Append(N(tz)).Append(" Tz\n");
                _tz = tz;
            }
            ops.Append(N(XPt(leftPx + piece.Origin.X))).Append(" ")
                .Append(N(YPt(baselinePx + piece.Origin.Y))).Append(" Td\n[<");

            for (var i = 0; i < run.GlyphIndices.Count; i++)
            {
                var glyph = run.GlyphIndices[i];
                font.Used.Add(glyph);
                ops.Append(glyph.ToString("X4"));
                var natural = Math.Round(run.GlyphTypeface.AdvanceWidths[glyph] * 1000);
                var wanted = run.AdvanceWidths[i] * 1000.0 / sizePx;
                var adjust = (int)Math.Round(natural - wanted);
                if (adjust != 0 && i < run.GlyphIndices.Count - 1)
                    ops.Append("> ").Append(adjust).Append(" <");
            }
            ops.Append(">] TJ\nET\n");
        }

        /// <summary>Characters outside the font (the composer's FormattedText
        /// fallback) are rasterized — rare, and paper-exact.</summary>
        private void EmitFallback(StringBuilder ops, ComposedPiece piece,
            double leftPx, double baselinePx)
        {
            var text = piece.Fallback;
            const double scale = 3.0;
            var wPx = text.WidthIncludingTrailingWhitespace;
            var hPx = text.Height;
            if (wPx < 0.1 || hPx < 0.1) return;
            var pixelW = Math.Max(1, (int)Math.Ceiling(wPx * scale));
            var pixelH = Math.Max(1, (int)Math.Ceiling(hPx * scale));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawText(text, new Point(0, 0));
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(pixelW, pixelH, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var entry = AddImage(bitmap);
            if (entry == null) return;
            var x = leftPx + piece.Origin.X;
            var top = baselinePx + piece.Origin.Y - text.Baseline;
            EmitImage(ops, entry, x, top, wPx * piece.ScaleX, hPx);
        }

        /// <summary>Plain label (folio, line numbers): natural advances, no
        /// justification. baselinePx matches the on-screen FormattedText.</summary>
        private void EmitSimpleText(StringBuilder ops, string text, Typeface typeface,
            double sizePx, double xPx, double baselinePx, Color color)
        {
            var key = typeface.FontFamily.Source ?? "";
            GlyphTypeface glyphs;
            if (!_labelTypefaces.TryGetValue(key, out glyphs))
            {
                if (!typeface.TryGetGlyphTypeface(out glyphs)) glyphs = null;
                _labelTypefaces[key] = glyphs;
            }
            if (glyphs == null) return;
            var font = GetFont(glyphs);
            var hex = new StringBuilder();
            foreach (var c in text)
            {
                ushort glyph;
                if (!glyphs.CharacterToGlyphMap.TryGetValue(c, out glyph)) continue;
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
            double wPx, double hPx, Color color)
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
            ops.Append("0.25 w 0 G\n");
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

        private static void AppendMark(StringBuilder ops,
            double xa, double ya, double xb, double yb)
        {
            ops.Append(N(xa)).Append(" ").Append(N(ya)).Append(" m ")
                .Append(N(xb)).Append(" ").Append(N(yb)).Append(" l S\n");
        }

        private void SetFill(StringBuilder ops, Color color)
        {
            if (color == _fill) return;
            _fill = color;
            ops.Append(N(color.R / 255.0)).Append(" ").Append(N(color.G / 255.0))
                .Append(" ").Append(N(color.B / 255.0)).Append(" rg\n");
        }

        private static Color InkColor(Brush ink)
        {
            var solid = ink as SolidColorBrush;
            return solid == null ? Colors.Black : solid.Color;
        }

        // px → pt with the bleed/marks offset; PDF y grows upward.
        private double XPt(double xPx) { return _margin + xPx * PxToPt; }
        private double YPt(double yPx)
        {
            return _margin + (_composition.PageHeightPx - yPx) * PxToPt;
        }

        // ============================================================ fonts

        private FontEntry GetFont(GlyphTypeface typeface)
        {
            FontEntry entry;
            if (_fonts.TryGetValue(typeface, out entry)) return entry;
            entry = new FontEntry
            {
                Typeface = typeface,
                Data = TrueTypeFont.Load(typeface),
                Res = "F" + (_fontList.Count + 1)
            };
            _fonts[typeface] = entry;
            _fontList.Add(entry);
            return entry;
        }

        private void WriteFont(MemoryStream output, long[] offsets, FontEntry font)
        {
            var typeface = font.Typeface;
            var data = font.Data;
            var scale = data != null ? 1000.0 / data.UnitsPerEm : 1.0;
            var baseName = SubsetTag(font) + "+"
                + (data != null ? data.PostScriptName : "Embedded");
            var italic = typeface.Style != FontStyles.Normal;

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
                    w.Append(N(Math.Round(typeface.AdvanceWidths[used[g]] * 1000)));
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
            var bold = typeface.Weight.ToOpenTypeWeight() >= 600;
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
            var name = font.Typeface.FontUri.ToString() + font.Res;
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
            foreach (var pair in font.Typeface.CharacterToGlyphMap)
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

        private ImageEntry RegisterImage(ImageSource source, double targetWidthPx)
        {
            ImageEntry entry;
            if (_imageMap.TryGetValue(source, out entry)) return entry;
            var bitmap = source as BitmapSource;
            if (bitmap == null) return null;
            // Cap the embedded resolution at ~3× the placed size (≈ 290 dpi).
            var factor = Math.Min(1.0, targetWidthPx * 3.0 / Math.Max(1, bitmap.PixelWidth));
            BitmapSource frame = bitmap;
            if (factor < 0.999)
                frame = new TransformedBitmap(bitmap, new ScaleTransform(factor, factor));
            entry = AddImage(frame);
            if (entry != null) _imageMap[source] = entry;
            return entry;
        }

        /// <summary>RGB24 over white (paper), Flate-compressed at write time.</summary>
        private ImageEntry AddImage(BitmapSource source)
        {
            try
            {
                var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
                var width = converted.PixelWidth;
                var height = converted.PixelHeight;
                var stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);
                var rgb = new byte[width * height * 3];
                var o = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var alpha = 255 - pixels[i + 3]; // premultiplied: add the white
                    rgb[o++] = (byte)Math.Min(255, pixels[i + 2] + alpha);
                    rgb[o++] = (byte)Math.Min(255, pixels[i + 1] + alpha);
                    rgb[o++] = (byte)Math.Min(255, pixels[i] + alpha);
                }
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
            catch { return null; }
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

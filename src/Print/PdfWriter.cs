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
        public string Author = "";
        // --- PDF/X (20/11)
        /// <summary>Le fichier se déclare PDF/X-1a:2003 (ne vaut qu'en CMJN) :
        /// version, piégeage, condition d'impression, pas de lien cliquable.</summary>
        public bool PdfX;
        /// <summary>Le profil ICC de la condition d'impression, à embarquer
        /// (null : la condition n'est déclarée que par son nom enregistré,
        /// ce que PDF/X-1a admet pour FOGRA39).</summary>
        public byte[] IccProfile;
        /// <summary>L'habillage d'un skin (21/11) — le « PDF Encyclopédique »
        /// seulement ; null partout ailleurs : rien ne change alors.</summary>
        public PdfSkin Skin;
    }

    /// <summary>Un repère d'une partie de PDF (gestionnaire d'export, 18/11) :
    /// la destination d'un lien (Name) et/ou un signet (Title, Level 0 = premier
    /// rang), au paragraphe donné — négatif : le haut de la première page.</summary>
    public class PdfMark
    {
        public int Paragraph = -1;
        public string Name;
        public string Title;
        public int Level;
    }

    /// <summary>Une plage de texte cliquable : elle mène au repère Target.</summary>
    public class PdfLink
    {
        public int Paragraph, Start, Length;
        public string Target;
    }

    /// <summary>UNE PARTIE D'UN PDF : des pages composées (leur propre
    /// format), ou UNE page-image à sa taille (un plan, une carte mentale).
    /// Un PDF en plusieurs parties n'a donc pas toutes ses pages au même
    /// format — le « Big Fat PDF ».</summary>
    public class PdfPart
    {
        public Composition Composition;
        // Page-image : les pixels RVB (sur blanc) et la taille de la page, en mm.
        public byte[] ImageRgb;
        public int ImageWidth, ImageHeight;
        public double PageWidthMm, PageHeightMm;
        public readonly List<PdfMark> Marks = new List<PdfMark>();
        public readonly List<PdfLink> Links = new List<PdfLink>();
        public bool IsImage { get { return Composition == null; } }
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

        /// <summary>Un PDF en plusieurs parties (18/11) : formats de page
        /// mêlés, pages-images, liens internes et signets.</summary>
        public static void Write(string path, IList<PdfPart> parts, PdfExportOptions options)
        {
            var bytes = Build(parts, options);
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
                file.Write(bytes, 0, bytes.Length);
        }

        public static byte[] Build(IList<PdfPart> parts, PdfExportOptions options)
        {
            return new PdfBuilder(parts, options ?? new PdfExportOptions()).Build();
        }
    }

    internal sealed class PdfBuilder
    {
        private const double PxToPt = 72.0 / 96.0;
        private const double MmToPt = 72.0 / 25.4;
        private const double MarkZoneMm = 5;   // room around the bleed for marks

        private Composition _composition;         // la partie en cours
        private readonly PdfExportOptions _options;
        private readonly List<PdfPart> _parts = new List<PdfPart>();

        // Page geometry (points) — celle de la partie en cours (Enter).
        private double _trimW, _trimH;            // finished format
        private readonly double _margin;          // bleed + mark zone
        private double _mediaW, _mediaH;
        private double _pageHPx;                  // hauteur de page en px (YPt)

        /// <summary>Une page du fichier : son contenu, ses boîtes, ses liens.</summary>
        private sealed class PageOut
        {
            public byte[] Content;
            public string Boxes;
            public readonly List<string> Annots = new List<string>();
        }

        /// <summary>Un lien relevé pendant le dessin : sa page, son rectangle (points), sa cible.</summary>
        private sealed class LinkOut
        {
            public int Page;
            public double X1, Y1, X2, Y2;
            public string Target;
        }

        /// <summary>Une destination : la page du fichier et la hauteur (points) où l'on arrive.</summary>
        private sealed class DestOut
        {
            public int Page;
            public double Top;
        }

        private sealed class OutlineOut
        {
            public string Title;
            public int Level;
            public DestOut Dest;
            public int Id;
            public OutlineOut Parent, Prev, Next, First, Last;
            public int Children;
        }

        private readonly List<PageOut> _pages = new List<PageOut>();
        private readonly List<LinkOut> _links = new List<LinkOut>();
        private readonly Dictionary<string, DestOut> _dests = new Dictionary<string, DestOut>();
        private readonly HashSet<string> _knownTargets = new HashSet<string>();
        private readonly List<OutlineOut> _outline = new List<OutlineOut>();
        private static readonly Ink LinkInk = Ink.Argb(255, 0x2F, 0x5F, 0xC8);

        private sealed class FontEntry
        {
            public FaceInfo Face;
            public TrueTypeFont Data;
            public HashSet<ushort> Used = new HashSet<ushort>();
            /// <summary>Le texte que porte un glyphe posé par le façonnage (ligature, petite capitale).</summary>
            public Dictionary<ushort, string> Carried = new Dictionary<ushort, string>();
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
        private readonly bool _x;                 // PDF/X-1a:2003
        private readonly double _bleedPt;
        private readonly bool _cropMarks, _bleedGuides;

        public PdfBuilder(Composition composition, PdfExportOptions options)
            : this(new[] { new PdfPart { Composition = composition } }, options)
        {
        }

        public PdfBuilder(IList<PdfPart> parts, PdfExportOptions options)
        {
            _options = options;
            if (parts != null) foreach (var part in parts) if (part != null) _parts.Add(part);
            if (_parts.Count == 0) throw new InvalidOperationException("Rien à écrire dans le PDF.");
            foreach (var part in _parts)
            {
                if (part.IsImage) continue;
                if (part.Composition.Fonts == null)
                    throw new InvalidOperationException("La composition n'a pas de moteur de polices (Composition.Fonts).");
                if (_fonts == null) _fonts = part.Composition.Fonts;
            }
            // Le cahier ne vaut que pour UN texte composé, sans liens : il replie les pages deux à deux.
            _x = options.PdfX && options.Cmyk;
            _booklet = options.Booklet && _parts.Count == 1 && !_parts[0].IsImage && _parts[0].Links.Count == 0;
            _bleedPt = _booklet ? 0 : options.BleedMm * MmToPt;
            _cropMarks = !_booklet && options.CropMarks;
            _bleedGuides = !_booklet && options.BleedGuides;
            _margin = _bleedPt + (_cropMarks ? MarkZoneMm * MmToPt : 0);
            foreach (var part in _parts)
                foreach (var mark in part.Marks)
                    if (mark.Name != null) _knownTargets.Add(mark.Name);
            Enter(_parts[0]);
        }

        /// <summary>Entre dans une partie : sa géométrie devient celle du dessin.</summary>
        private void Enter(PdfPart part)
        {
            _composition = part.Composition;
            var widthPx = part.IsImage ? part.PageWidthMm * PageSetup.PxPerMm : part.Composition.PageWidthPx;
            _pageHPx = part.IsImage ? part.PageHeightMm * PageSetup.PxPerMm : part.Composition.PageHeightPx;
            _trimW = widthPx * PxToPt;
            _trimH = _pageHPx * PxToPt;
            _mediaW = _trimW + 2 * _margin;
            _mediaH = _trimH + 2 * _margin;
        }

        private string Boxes()
        {
            var bleed = _bleedPt;
            return " /MediaBox [0 0 " + N(_mediaW) + " " + N(_mediaH) + "]"
                + " /BleedBox [" + N(_margin - bleed) + " " + N(_margin - bleed) + " "
                    + N(_margin + _trimW + bleed) + " " + N(_margin + _trimH + bleed) + "]"
                + " /TrimBox [" + N(_margin) + " " + N(_margin) + " "
                    + N(_margin + _trimW) + " " + N(_margin + _trimH) + "]";
        }

        // ============================================================ build

        public byte[] Build()
        {
            // Pass 1: page contents (registers fonts, used glyphs, images).
            if (_booklet)
            {
                // 4b-3 — imposition en cahier : deux pages composées par face,
                // complété en pages blanches à un multiple de 4. Feuille s :
                // recto = [N−2s | 2s+1], verso = [2s+2 | N−2s−1] (folios 1-based) —
                // plié en deux, le livret se lit dans l'ordre.
                var composedCount = _composition.Pages.Count;
                var pageOps = new List<string>();
                for (var k = 0; k < composedCount; k++)
                    pageOps.Add(BuildPageContent(k, null));
                var sheetBoxes = " /MediaBox [0 0 " + N(2 * _trimW) + " " + N(_trimH) + "]"
                    + " /TrimBox [0 0 " + N(2 * _trimW) + " " + N(_trimH) + "]"; // PDF/X veut une boîte de coupe à chaque page
                var padded = ((composedCount + 3) / 4) * 4;
                for (var s = 0; s < padded / 4; s++)
                {
                    _pages.Add(new PageOut { Content = BookletSheet(pageOps, padded - 1 - 2 * s, 2 * s), Boxes = sheetBoxes });
                    _pages.Add(new PageOut { Content = BookletSheet(pageOps, 2 * s + 1, padded - 2 - 2 * s), Boxes = sheetBoxes });
                }
            }
            else
                foreach (var part in _parts)
                {
                    Enter(part);
                    var firstPage = _pages.Count;
                    if (part.IsImage)
                    {
                        _fill = Sentinel; _stroke = Sentinel; _tz = 100;
                        var ops = new StringBuilder();
                        if (_cropMarks) EmitCropMarks(ops);
                        var entry = AddImage(part.ImageRgb, part.ImageWidth, part.ImageHeight);
                        if (entry != null) EmitImage(ops, entry, 0, 0, part.PageWidthMm * PageSetup.PxPerMm, _pageHPx);
                        _pages.Add(new PageOut { Content = Latin1(ops.ToString()), Boxes = Boxes() });
                    }
                    else
                        for (var k = 0; k < _composition.Pages.Count; k++)
                        {
                            var page = new PageOut { Boxes = Boxes() };
                            _pages.Add(page);
                            page.Content = Latin1(BuildPageContent(k, part));
                        }
                    foreach (var mark in part.Marks) PlaceMark(part, mark, firstPage);
                }
            var pageCount = _pages.Count;

            // Les liens : une annotation par rectangle, vers la destination de la cible.
            // (PDF/X interdit toute annotation dans la page : les liens y tombent.)
            foreach (var link in _links)
            {
                if (_x) break;
                DestOut dest;
                if (!_dests.TryGetValue(link.Target, out dest)) continue;
                _pages[link.Page].Annots.Add("<< /Type /Annot /Subtype /Link /Rect [" + N(link.X1) + " " + N(link.Y1) + " " + N(link.X2) + " " + N(link.Y2)
                    + "] /Border [0 0 0] /Dest [" + (4 + 2 * dest.Page) + " 0 R /XYZ null " + N(dest.Top) + " null] >>");
            }

            // Ids: 1 catalog, 2 pages, 3 resources, then page/content pairs,
            // then images, fonts, info, outline.
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
            var metadataId = next++;
            var iccId = _options.Cmyk && _options.IccProfile != null && _options.IccProfile.Length > 128 ? next++ : 0;
            var outlineRootId = 0;
            if (_outline.Count > 0)
            {
                LinkOutline();
                outlineRootId = next++;
                foreach (var node in _outline) node.Id = next++;
            }

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
                    + " /Info (Coated FOGRA39 \\(ISO 12647-2:2004\\))"
                    + " /RegistryName (http://www.color.org)"
                    + (iccId > 0 ? " /DestOutputProfile " + iccId + " 0 R" : "") + " >>]"
                : "";
            intent += " /Metadata " + metadataId + " 0 R";
            var outlines = outlineRootId > 0 ? " /Outlines " + outlineRootId + " 0 R /PageMode /UseOutlines" : "";
            WriteRaw(output, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R" + intent + outlines + " >>\nendobj\n");

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
            // La couleur de repérage (20/11) : les traits de coupe sortent sur TOUTES les plaques.
            if (_options.Cmyk && _cropMarks)
                resources.Append(" /ColorSpace << /Reg [/Separation /All /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [1 1 1 1] /N 1 >>] >>");
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
            for (var k = 0; k < pageCount; k++)
            {
                var pageId = 4 + 2 * k;
                offsets[pageId] = output.Position;
                var annots = _pages[k].Annots.Count == 0 ? "" : " /Annots [" + string.Join(" ", _pages[k].Annots.ToArray()) + "]";
                WriteRaw(output, pageId + " 0 obj\n<< /Type /Page /Parent 2 0 R"
                    + _pages[k].Boxes + " /Resources 3 0 R /Contents " + (pageId + 1) + " 0 R" + annots + " >>\nendobj\n");
                offsets[pageId + 1] = output.Position;
                WriteStream(output, pageId + 1, "", _pages[k].Content);
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

            // Les informations du fichier : dates, piégeage, version PDF/X — et leur double XMP.
            var now = DateTime.Now;
            var zone = TimeZoneInfo.Local.GetUtcOffset(now);
            var sign = zone < TimeSpan.Zero ? "-" : "+";
            var pdfDate = "D:" + now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + sign + Math.Abs(zone.Hours).ToString("00") + "'" + Math.Abs(zone.Minutes).ToString("00") + "'";
            var xmpDate = now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + sign + Math.Abs(zone.Hours).ToString("00") + ":" + Math.Abs(zone.Minutes).ToString("00");
            offsets[infoId] = output.Position;
            WriteRaw(output, infoId + " 0 obj\n<< /Title " + Utf16String(_options.Title ?? "")
                + (string.IsNullOrEmpty(_options.Author) ? "" : " /Author " + Utf16String(_options.Author))
                + " /Producer " + Utf16String("Marabook") + " /Creator " + Utf16String("Marabook")
                + " /CreationDate (" + pdfDate + ") /ModDate (" + pdfDate + ") /Trapped /False"
                + (_x ? " /GTS_PDFXVersion (PDF/X-1a:2003)" : "") + " >>\nendobj\n");

            offsets[metadataId] = output.Position;
            var xmp = new UTF8Encoding(false).GetBytes(Xmp(xmpDate));
            WriteRaw(output, metadataId + " 0 obj\n<< /Type /Metadata /Subtype /XML /Length " + xmp.Length + " >>\nstream\n");
            output.Write(xmp, 0, xmp.Length);
            WriteRaw(output, "\nendstream\nendobj\n");

            if (iccId > 0)
            {
                offsets[iccId] = output.Position;
                WriteStream(output, iccId, " /N 4 /Alternate /DeviceCMYK", _options.IccProfile);
            }

            // Les signets : la racine, puis un objet par entrée.
            if (outlineRootId > 0)
            {
                OutlineOut first = null, last = null;
                var top = 0;
                foreach (var node in _outline)
                    if (node.Parent == null) { if (first == null) first = node; last = node; top++; }
                offsets[outlineRootId] = output.Position;
                WriteRaw(output, outlineRootId + " 0 obj\n<< /Type /Outlines /First " + first.Id + " 0 R /Last " + last.Id + " 0 R /Count " + top + " >>\nendobj\n");
                foreach (var node in _outline)
                {
                    offsets[node.Id] = output.Position;
                    var entry = new StringBuilder();
                    entry.Append(node.Id).Append(" 0 obj\n<< /Title ").Append(Utf16String(node.Title ?? ""))
                      .Append(" /Parent ").Append(node.Parent != null ? node.Parent.Id : outlineRootId).Append(" 0 R");
                    if (node.Prev != null) entry.Append(" /Prev ").Append(node.Prev.Id).Append(" 0 R");
                    if (node.Next != null) entry.Append(" /Next ").Append(node.Next.Id).Append(" 0 R");
                    if (node.First != null)
                        entry.Append(" /First ").Append(node.First.Id).Append(" 0 R /Last ").Append(node.Last.Id).Append(" 0 R /Count -").Append(node.Children); // replié
                    entry.Append(" /Dest [").Append(4 + 2 * node.Dest.Page).Append(" 0 R /XYZ null ").Append(N(node.Dest.Top)).Append(" null] >>\nendobj\n");
                    WriteRaw(output, entry.ToString());
                }
            }

            // xref + trailer
            var xref = output.Position;
            var sb = new StringBuilder();
            sb.Append("xref\n0 ").Append(next).Append("\n");
            sb.Append("0000000000 65535 f \n");
            for (var id = 1; id < next; id++)
                sb.Append(offsets[id].ToString("0000000000", CultureInfo.InvariantCulture))
                    .Append(" 00000 n \n");
            // L'identifiant du fichier : l'empreinte de tout ce qui précède.
            string fileId;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                fileId = BitConverter.ToString(md5.ComputeHash(output.ToArray())).Replace("-", "");
            sb.Append("trailer\n<< /Size ").Append(next)
                .Append(" /Root 1 0 R /Info ").Append(infoId).Append(" 0 R /ID [<").Append(fileId).Append("> <").Append(fileId).Append(">] >>\n")
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

        /// <summary>Pose un repère : la page du fichier où tombe son
        /// paragraphe, et la hauteur où l'on arrive (un peu au-dessus de sa
        /// première ligne) ; sans paragraphe, le haut de la première page.</summary>
        private void PlaceMark(PdfPart part, PdfMark mark, int firstPage)
        {
            var dest = new DestOut { Page = firstPage, Top = _mediaH };
            if (!part.IsImage && mark.Paragraph >= 0)
            {
                var found = false;
                for (var k = 0; k < _composition.Pages.Count && !found; k++)
                    foreach (var placed in _composition.Pages[k].Lines)
                    {
                        if (placed.ParagraphIndex < mark.Paragraph) continue;
                        dest.Page = firstPage + k;
                        dest.Top = Math.Min(_mediaH, YPt(placed.Y) + 12);
                        found = true;
                        break;
                    }
            }
            if (dest.Page >= _pages.Count) dest.Page = Math.Max(0, _pages.Count - 1);
            if (mark.Name != null && !_dests.ContainsKey(mark.Name)) _dests[mark.Name] = dest;
            if (!string.IsNullOrEmpty(mark.Title)) _outline.Add(new OutlineOut { Title = mark.Title, Level = Math.Max(0, mark.Level), Dest = dest });
        }

        /// <summary>Chaîne les signets : le parent d'une entrée est la dernière
        /// entrée d'un rang moindre qui la précède.</summary>
        private void LinkOutline()
        {
            var open = new List<OutlineOut>();
            foreach (var node in _outline)
            {
                while (open.Count > 0 && open[open.Count - 1].Level >= node.Level) open.RemoveAt(open.Count - 1);
                node.Parent = open.Count > 0 ? open[open.Count - 1] : null;
                open.Add(node);
            }
            var lastTop = (OutlineOut)null;
            foreach (var node in _outline)
            {
                if (node.Parent == null)
                {
                    if (lastTop != null) { lastTop.Next = node; node.Prev = lastTop; }
                    lastTop = node;
                    continue;
                }
                var parent = node.Parent;
                if (parent.First == null) parent.First = node;
                else { parent.Last.Next = node; node.Prev = parent.Last; }
                parent.Last = node;
                parent.Children++;
            }
        }

        /// <summary>Les liens d'une page composée : un rectangle par ligne
        /// touchée (annotation), et un filet fin sous les mots pour qu'on
        /// voie où cliquer.</summary>
        private void EmitLinks(StringBuilder ops, PdfPart part, int index, double left)
        {
            var page = _composition.Pages[index];
            var pageOut = _pages.Count - 1;
            foreach (var link in part.Links)
            {
                if (link.Target == null || !_knownTargets.Contains(link.Target) || link.Length <= 0) continue;
                foreach (var placed in page.Lines)
                {
                    if (placed.ParagraphIndex != link.Paragraph) continue;
                    double minX = double.MaxValue, maxX = double.MinValue;
                    foreach (var piece in placed.Line.Pieces)
                    {
                        if (piece.SourceStart < 0 || piece.IsAnchor) continue;
                        var start = piece.SourceStart;
                        var end = start + Math.Max(1, piece.SourceLength);
                        if (end <= link.Start || start >= link.Start + link.Length) continue;
                        var x = left + piece.Origin.X;
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x + piece.VisualWidth());
                    }
                    if (maxX - minX < 0.5) continue;
                    var line = placed.Line;
                    var under = placed.Y + line.Ascent + Math.Max(1.5, line.Height * 0.09);
                    EmitRect(ops, minX, under, maxX - minX, 0.6, LinkInk);
                    _links.Add(new LinkOut
                    {
                        Page = pageOut, Target = link.Target,
                        X1 = XPt(minX), X2 = XPt(maxX), Y1 = YPt(placed.Y + line.Height), Y2 = YPt(placed.Y)
                    });
                }
            }
        }

        private string BuildPageContent(int index, PdfPart part)
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

            if (_options.Skin != null) EmitSkinPaper(ops, setup.PageWidthPx, setup.PageHeightMm * PageSetup.PxPerMm);
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
            if (part != null && part.Links.Count > 0) EmitLinks(ops, part, index, left);

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
                EmitRect(ops, x, y + offset - thickness / 2, w, thickness, ink); // le trait centré sur la position (30/09)
            }
            if (piece.Strike)
            {
                var offset = face != null ? -face.StrikethroughPosition * size : -size * 0.3;
                var thickness = face != null
                    ? Math.Max(0.8, face.StrikethroughThickness * size)
                    : Math.Max(0.8, size * 0.05);
                EmitRect(ops, x, y + offset - thickness / 2, w, thickness, ink);
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
            var textAt = 0;
            for (var i = 0; i < indices.Length; i++)
            {
                var glyph = indices[i];
                font.Used.Add(glyph);
                if (piece.GlyphChars != null && piece.Text != null && i < piece.GlyphChars.Length)
                {
                    var carried = Math.Min(piece.GlyphChars[i], piece.Text.Length - textAt);
                    // Seuls les glyphes SUBSTITUÉS : une ligature (plusieurs caractères), ou un
                    // glyphe qui n'est pas celui que la police donne à ce caractère (petite capitale).
                    ushort plain;
                    var substituted = carried > 1 || (carried == 1 && !char.IsWhiteSpace(piece.Text[textAt])
                        && _fonts.TryGlyph(piece.Face, piece.Text[textAt], out plain) && plain != glyph);
                    if (substituted && !font.Carried.ContainsKey(glyph)) font.Carried[glyph] = piece.Text.Substring(textAt, carried);
                    textAt += Math.Max(0, carried);
                }
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

        private ImageEntry _skinTexture;
        private bool _skinTextureTried;

        /// <summary>Le papier d'un skin, sous tout le reste de la page : une
        /// texture, un dégradé en bandes ou un aplat ; puis le filet de cadre.
        /// Les couleurs passent par les couleurs BRUTES (le papier n'est pas de l'encre à traduire).</summary>
        private void EmitSkinPaper(StringBuilder ops, double widthPx, double heightPx)
        {
            var skin = _options.Skin;
            if (skin.Texture && skin.TextureRgb != null && !_skinTextureTried)
            {
                _skinTextureTried = true;
                _skinTexture = AddImage(skin.TextureRgb, skin.TextureWidth, skin.TextureHeight);
            }
            if (skin.Texture && _skinTexture != null) EmitImage(ops, _skinTexture, 0, 0, widthPx, heightPx);
            else if (skin.Banded)
            {
                const int bands = 32;
                for (var i = 0; i < bands; i++)
                {
                    var t = i / (double)(bands - 1);
                    var color = Ink.Rgb((byte)(skin.Paper.R + (skin.PaperEnd.R - skin.Paper.R) * t), (byte)(skin.Paper.G + (skin.PaperEnd.G - skin.Paper.G) * t), (byte)(skin.Paper.B + (skin.PaperEnd.B - skin.Paper.B) * t));
                    RawRect(ops, 0, heightPx * i / bands, widthPx, heightPx / bands + 0.6, color);
                }
            }
            else RawRect(ops, 0, 0, widthPx, heightPx, skin.Paper);
            if (skin.HasFrame)
            {
                const double inset = 22, thick = 1.1;
                RawRect(ops, inset, inset, widthPx - 2 * inset, thick, skin.Frame);
                RawRect(ops, inset, heightPx - inset - thick, widthPx - 2 * inset, thick, skin.Frame);
                RawRect(ops, inset, inset, thick, heightPx - 2 * inset, skin.Frame);
                RawRect(ops, widthPx - inset - thick, inset, thick, heightPx - 2 * inset, skin.Frame);
            }
            _fill = Sentinel; // la première encre de la page se réécrit
        }

        private void RawRect(StringBuilder ops, double xPx, double yPx, double wPx, double hPx, Ink color)
        {
            ops.Append(RawColorOps(color, false));
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
            ops.Append("0.25 w ").Append(_options.Cmyk ? "/Reg CS 1 SCN\n" : "0 G\n");
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
            // Sous un skin, le noir et les gris du texte prennent son encre (21/11).
            return RawColorOps(_options.Skin == null ? color : _options.Skin.Map(color), stroke);
        }

        private string RawColorOps(Ink color, bool stroke)
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
            return _margin + (_pageHPx - yPx) * PxToPt;
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
                // Une ligature ou une petite capitale (20/11) : le texte qu'elle porte — sans quoi
                // la recherche et le copier-coller du PDF perdraient « fi », « fl »…
                string carried;
                if (font.Carried.TryGetValue(glyph, out carried) && carried.Length > 0)
                {
                    var hex = new StringBuilder();
                    foreach (var c in carried) hex.Append(((int)c).ToString("X4"));
                    entries.Add("<" + glyph.ToString("X4") + "> <" + hex + ">");
                    continue;
                }
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

        private static string XmlText(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in text ?? "")
            {
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c >= 0x20 || c == '\t') sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Les métadonnées XMP : le double des informations du
        /// fichier, que les contrôles d'imprimeur lisent en premier.</summary>
        private string Xmp(string date)
        {
            var sb = new StringBuilder();
            sb.Append("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
            sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");
            sb.Append("<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\"");
            if (_x) sb.Append(" xmlns:pdfx=\"http://ns.adobe.com/pdfx/1.3/\" xmlns:pdfxid=\"http://www.npes.org/pdfx/ns/id/\"");
            sb.Append(">\n");
            sb.Append("<dc:format>application/pdf</dc:format>\n");
            sb.Append("<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">").Append(XmlText(_options.Title)).Append("</rdf:li></rdf:Alt></dc:title>\n");
            if (!string.IsNullOrEmpty(_options.Author))
                sb.Append("<dc:creator><rdf:Seq><rdf:li>").Append(XmlText(_options.Author)).Append("</rdf:li></rdf:Seq></dc:creator>\n");
            sb.Append("<xmp:CreateDate>").Append(date).Append("</xmp:CreateDate>\n<xmp:ModifyDate>").Append(date).Append("</xmp:ModifyDate>\n<xmp:MetadataDate>").Append(date).Append("</xmp:MetadataDate>\n");
            sb.Append("<xmp:CreatorTool>Marabook</xmp:CreatorTool>\n<pdf:Producer>Marabook</pdf:Producer>\n<pdf:Trapped>False</pdf:Trapped>\n");
            if (_x) sb.Append("<pdfx:GTS_PDFXVersion>PDF/X-1a:2003</pdfx:GTS_PDFXVersion>\n<pdfxid:GTS_PDFXVersion>PDF/X-1a:2003</pdfxid:GTS_PDFXVersion>\n");
            sb.Append("</rdf:Description>\n</rdf:RDF>\n</x:xmpmeta>\n");
            for (var i = 0; i < 12; i++) sb.Append("                                                                                \n"); // la marge que la norme réserve aux retouches
            sb.Append("<?xpacket end=\"w\"?>");
            return sb.ToString();
        }

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

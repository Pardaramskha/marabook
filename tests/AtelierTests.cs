using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Exchange;
using Marabook.Print;

namespace Marabook.Tests
{
    /// <summary>C90 — 2.0.0, lot 29 (21/11) : l'Atelier vu du cœur — le paquet
    /// PDFium (source, empreintes, refus), la lecture des PDF modernes (flux
    /// d'objets), le contrôle d'impression (fonds perdus, taille, homogénéité,
    /// polices, fichier verrouillé) et le plan de recomposition de Chop-Chop.</summary>
    public static class AtelierTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C90 — lot 29 : Atelier (paquet PDFium, PDF modernes, contrôle d'impression, recomposition)");
            Package(t);
            ObjectStreams(t);
            PrintCheck(t);
            Assembly(t);
        }

        // ------------------------------------------------------------ le paquet PDFium

        private static void Package(Harness t)
        {
            t.Equal(6, PdfiumPackage.Targets.Length, "PDFium est prévu pour six systèmes : Windows, Linux et macOS, en x64 et en arm64");
            var seen = new HashSet<string>();
            var allGood = true;
            foreach (var target in PdfiumPackage.Targets)
            {
                var uri = new Uri(target.Url);
                if (uri.Scheme != "https" || uri.Host != PdfiumPackage.Host || !target.Url.EndsWith("." + PdfiumPackage.Version + ".nupkg", StringComparison.Ordinal)) allGood = false;
                if (target.FileSha256.Length != 64 || Convert.FromBase64String(target.PackageSha512).Length != 64 || target.PackageSize < 1000000) allGood = false;
                if (!target.Entry.StartsWith("runtimes/" + target.Rid + "/native/", StringComparison.Ordinal) || !seen.Add(target.Rid) || !seen.Add(target.FileSha256)) allGood = false;
            }
            t.Check(allGood, "chaque cible a UNE adresse HTTPS sur api.nuget.org à la version fixée, une taille, une empreinte d'archive (SHA-512) et une empreinte de fichier (SHA-256) qui lui est propre");
            var win = PdfiumPackage.For("win-x64");
            t.Check(win != null && win.File == "pdfium.dll" && PdfiumPackage.For("osx-arm64").File == "libpdfium.dylib" && PdfiumPackage.For("linux-x64").File == "libpdfium.so" && PdfiumPackage.For("freebsd-x64") == null,
                "la bibliothèque porte le nom de son système ; un système non prévu n'a pas de cible");
            t.Check(PdfiumPackage.Prompt(win).Contains(PdfiumPackage.Version) && PdfiumPackage.Prompt(win).Contains("api.nuget.org") && PdfiumPackage.Prompt(win).Contains(win.DownloadMegabytes + " Mo"), "la question posée dit la source, la version et le poids");

            var folder = Path.Combine(Path.GetTempPath(), "marabook-tests-c90-" + Guid.NewGuid().ToString("N"));
            try
            {
                t.Check(PdfiumPackage.Verify(win, null) != null && PdfiumPackage.Verify(win, new byte[10]) != null && PdfiumPackage.Verify(null, new byte[10]) != null, "une archive vide, de mauvaise taille, ou pour un système inconnu est refusée");
                var fake = new byte[win.PackageSize];
                var refusal = PdfiumPackage.Install(win, fake, folder);
                t.Check(refusal != null && refusal.Contains("empreinte") && !Directory.Exists(folder), "une archive de la bonne taille mais d'une autre empreinte est refusée, et rien n'est écrit");
                t.Check(!PdfiumPackage.IsInstalled(win, folder), "rien n'est « installé » tant que le fichier n'y est pas");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, win.File), new byte[] { 1, 2, 3 });
                t.Check(!PdfiumPackage.IsInstalled(win, folder), "un fichier du bon nom mais d'une autre empreinte n'est PAS une installation : il ne sera jamais chargé");
                t.Check(PdfiumPackage.Remove(folder) && !Directory.Exists(folder) && PdfiumPackage.Remove(folder), "la dépendance se retire (et la retirer deux fois ne gêne pas)");
                t.Check(PdfiumPackage.FolderIn("/donnees").Replace('\\', '/').EndsWith("/dependances/pdfium-" + PdfiumPackage.Version), "elle s'installe dans un dossier à sa version, à côté de PdfPig");
            }
            finally { try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException) { } }

            var real = Environment.GetEnvironmentVariable("MARABOOK_PDFIUM_NUPKG");
            if (string.IsNullOrEmpty(real) || !File.Exists(real)) { Console.WriteLine("  archive PDFium absente (MARABOOK_PDFIUM_NUPKG) : l'installation réelle n'est pas jouée."); return; }
            var bytes = File.ReadAllBytes(real);
            PdfiumPackage.Target match = null;
            foreach (var target in PdfiumPackage.Targets) if (target.PackageSize == bytes.LongLength && PdfiumPackage.Verify(target, bytes) == null) { match = target; break; }
            t.Check(match != null, "l'archive réelle de NuGet a la taille et l'empreinte notées dans le code");
            if (match == null) return;
            var installed = Path.Combine(Path.GetTempPath(), "marabook-tests-c90-reel-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var target in PdfiumPackage.Targets)
                {
                    if (target.Package != match.Package) continue;
                    var into = Path.Combine(installed, target.Rid);
                    var problem = PdfiumPackage.Install(target, bytes, into);
                    t.Check(problem == null && PdfiumPackage.IsInstalled(target, into) && Directory.GetFiles(into).Length == 1, "…" + target.Rid + " : UN fichier en est tiré, à son empreinte (" + (problem ?? "installé") + ")");
                }
            }
            finally { try { Directory.Delete(installed, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        // ------------------------------------------------------------ des PDF d'essai, écrits à la main

        private sealed class Pdf
        {
            private readonly MemoryStream _out = new MemoryStream();
            private readonly Encoding _latin = Encoding.GetEncoding(28591);
            public Pdf(string version) { Write("%PDF-" + version + "\n"); }
            public void Write(string text) { var bytes = _latin.GetBytes(text); _out.Write(bytes, 0, bytes.Length); }
            public void Object(int number, string body) { Write(number + " 0 obj\n" + body + "\nendobj\n"); }
            public void Stream(int number, string dict, byte[] data)
            {
                Write(number + " 0 obj\n<< " + dict + " /Length " + data.Length + " >>\nstream\n");
                _out.Write(data, 0, data.Length);
                Write("\nendstream\nendobj\n");
            }
            public byte[] End(string trailer) { Write("trailer\n<< " + trailer + " >>\n%%EOF\n"); return _out.ToArray(); }
        }

        private static byte[] Zlib(string text)
        {
            using (var buffer = new MemoryStream())
            {
                buffer.WriteByte(0x78);
                buffer.WriteByte(0x9C);
                using (var deflate = new DeflateStream(buffer, CompressionMode.Compress, true))
                {
                    var bytes = Encoding.GetEncoding(28591).GetBytes(text);
                    deflate.Write(bytes, 0, bytes.Length);
                }
                return buffer.ToArray();
            }
        }

        private static string Pt(double mm) { return (mm * 72 / 25.4).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>Une page : son format (mm), sa coupe et son fond perdu en retrait (mm ; négatif = non déclaré), ses polices.</summary>
        private static string PageBody(double width, double height, double trimInset, double bleedInset, string fonts)
        {
            var sb = new StringBuilder("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + Pt(width) + " " + Pt(height) + "]");
            if (trimInset >= 0) sb.Append(" /TrimBox [" + Pt(trimInset) + " " + Pt(trimInset) + " " + Pt(width - trimInset) + " " + Pt(height - trimInset) + "]");
            if (bleedInset >= 0) sb.Append(" /BleedBox [" + Pt(bleedInset) + " " + Pt(bleedInset) + " " + Pt(width - bleedInset) + " " + Pt(height - bleedInset) + "]");
            sb.Append(" /Resources << /Font << " + fonts + " >> >> >>");
            return sb.ToString();
        }

        /// <summary>Un PDF d'imprimeur : pages A5 + 5 mm de fond perdu tout autour (coupe à 5 mm, fond perdu à « bleedInset »).</summary>
        private static byte[] PressPdf(int pages, double bleedInset, bool embedded)
        {
            var pdf = new Pdf("1.4");
            pdf.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            var kids = new StringBuilder();
            for (var i = 0; i < pages; i++) kids.Append(10 + i).Append(" 0 R ");
            pdf.Object(2, "<< /Type /Pages /Count " + pages + " /Kids [" + kids + "] >>");
            pdf.Object(3, "<< /Type /Font /Subtype /TrueType /BaseFont /ABCDEF+Garamond /FontDescriptor 4 0 R >>");
            pdf.Object(4, embedded ? "<< /Type /FontDescriptor /FontName /ABCDEF+Garamond /FontFile2 5 0 R >>" : "<< /Type /FontDescriptor /FontName /ABCDEF+Garamond >>");
            pdf.Stream(5, "", new byte[] { 0, 1, 0, 0 });
            for (var i = 0; i < pages; i++) pdf.Object(10 + i, PageBody(158, 220, 5, bleedInset, "/F1 3 0 R"));
            return pdf.End("/Root 1 0 R /Size 40");
        }

        private static PrintCheckLine Line(PrintCheckReport report, string check, int level)
        {
            foreach (var line in report.Lines) if (line.Check == check && line.Level == level) return line;
            return null;
        }

        // ------------------------------------------------------------ les flux d'objets (PDF 1.5)

        private static void ObjectStreams(Harness t)
        {
            // Le catalogue, l'arbre des pages et la page elle-même sont rangés dans un flux d'objets compressé.
            var objects = new[]
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 419.528 595.276] /TrimBox [0 0 419.528 595.276] /Resources << /Font << /F1 6 0 R >> >> >>"
            };
            var table = new StringBuilder();
            var body = new StringBuilder();
            for (var i = 0; i < objects.Length; i++) { table.Append(i + 1).Append(' ').Append(body.Length).Append(' '); body.Append(objects[i]).Append('\n'); }
            var pdf = new Pdf("1.6");
            pdf.Stream(7, "/Type /ObjStm /N 3 /First " + table.Length + " /Filter /FlateDecode", Zlib(table.ToString() + body));
            pdf.Object(6, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            var bytes = pdf.End("/Root 1 0 R /Size 8");

            var lite = PdfLite.Read(bytes);
            t.Check(lite != null && lite.HasObjectStreams && lite.ObjectStreamsRead && lite.Objects.ContainsKey(1) && lite.Objects.ContainsKey(3), "les objets rangés dans un flux compressé (PDF 1.5 et après) se lisent comme les autres");
            var catalog = lite.Dict(lite.Trailer["Root"]);
            t.Check(catalog != null && lite.Name(catalog, "Type") == "Catalog" && lite.Dict(catalog["Pages"]) != null, "…le catalogue et l'arbre des pages s'y retrouvent");
            var report = PdfPrintCheck.Inspect(bytes, new PrintCheckOptions());
            t.Check(report.Readable && report.Pages == 1 && Line(report, PdfPrintCheck.CheckSize, PrintCheckLine.Ok) != null && Line(report, PdfPrintCheck.CheckSize, PrintCheckLine.Ok).Text.Contains("148 × 210 mm"),
                "le contrôle d'impression lit donc un PDF moderne : 1 page, A5 (" + report.Summary() + ")");
            t.Check(Line(report, PdfPrintCheck.CheckFonts, PrintCheckLine.Problem) != null && Line(report, PdfPrintCheck.CheckFonts, PrintCheckLine.Problem).Text.Contains("Helvetica"), "…et y voit la police non incorporée");

            // Un flux abîmé : le reste du fichier se lit, et le lecteur dit qu'il n'a pas tout ouvert.
            var broken = new Pdf("1.6");
            broken.Stream(7, "/Type /ObjStm /N 3 /First 20 /Filter /FlateDecode", new byte[] { 0x78, 0x9C, 1, 2, 3, 4, 5, 6, 7, 8 });
            broken.Object(6, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            var hurt = PdfLite.Read(broken.End("/Root 1 0 R /Size 8"));
            t.Check(hurt != null && hurt.HasObjectStreams && !hurt.ObjectStreamsRead && hurt.Objects.ContainsKey(6), "un flux d'objets illisible ne casse pas la lecture : le lecteur dit qu'il n'a pas tout ouvert");
            var unreadable = PdfPrintCheck.Inspect(broken.End(""), new PrintCheckOptions());
            t.Check(!unreadable.Readable && unreadable.Lines.Count == 1, "…et le contrôle d'impression dit qu'il n'a pas trouvé les pages, plutôt que de se taire");
            // Les PDF d'avant (sans flux d'objets) ne changent pas.
            var old = PdfLite.Read(PressPdf(2, 2, true));
            t.Check(old != null && !old.HasObjectStreams && !old.ObjectStreamsRead && old.Objects.Count >= 6, "un PDF 1.4 sans flux d'objets se lit comme avant");
        }

        // ------------------------------------------------------------ le contrôle d'impression

        private static void PrintCheck(Harness t)
        {
            // Un bon fichier d'imprimeur : A5, 3 mm de fond perdu déclaré, police incorporée.
            var good = PdfPrintCheck.Inspect(PressPdf(4, 2, true), new PrintCheckOptions());
            t.Check(good.Readable && good.Pages == 4 && good.Problems == 0 && good.Fonts == 1, "un PDF d'imprimeur en règle : rien à signaler (" + good.Summary() + ")");
            t.Check(Line(good, PdfPrintCheck.CheckBleed, PrintCheckLine.Ok) != null && Line(good, PdfPrintCheck.CheckBleed, PrintCheckLine.Ok).Text.Contains("3 mm"), "…fonds perdus : au moins 3 mm de chaque côté");
            t.Check(Line(good, PdfPrintCheck.CheckSize, PrintCheckLine.Ok) != null && good.Lines[0].Text.Contains("148 × 210 mm") && good.Lines[0].Text.Contains("zone de coupe"), "…taille : le format FINI (la zone de coupe), 148 × 210 mm, pas le format de la feuille");
            t.Check(Line(good, PdfPrintCheck.CheckConsistency, PrintCheckLine.Ok) != null && Line(good, PdfPrintCheck.CheckFonts, PrintCheckLine.Ok) != null, "…homogénéité et polices : bonnes");
            t.Check(good.ToText().Contains("[bon] Fonds perdus") && good.Summary().StartsWith("4 pages"), "le rapport se copie en texte");

            // Le fond perdu : trop mince, puis absent, puis non mesurable.
            var thin = PdfPrintCheck.Inspect(PressPdf(3, 4, true), new PrintCheckOptions());
            var thinLine = Line(thin, PdfPrintCheck.CheckBleed, PrintCheckLine.Problem);
            t.Check(thinLine != null && thinLine.Text.Contains("moins de 3 mm") && thinLine.Text.Contains("pages 1 à 3"), "1 mm de fond perdu pour 3 demandés : signalé, pages 1 à 3 (" + (thinLine == null ? "-" : thinLine.Text) + ")");
            t.Check(PdfPrintCheck.Inspect(PressPdf(3, 4, true), new PrintCheckOptions { MinBleedMm = 1 }).Problems == 0, "…le même fichier passe si l'on ne demande que 1 mm");
            var none = PdfPrintCheck.Inspect(PressPdf(2, 5, true), new PrintCheckOptions());
            t.Check(Line(none, PdfPrintCheck.CheckBleed, PrintCheckLine.Problem) != null && Line(none, PdfPrintCheck.CheckBleed, PrintCheckLine.Problem).Text.Contains("aucun fond perdu"), "un fond perdu qui s'arrête à la coupe : « aucun fond perdu »");
            var implied = PdfPrintCheck.Inspect(PressPdf(2, -1, true), new PrintCheckOptions());
            t.Check(implied.Problems == 0 && Line(implied, PdfPrintCheck.CheckBleed, PrintCheckLine.Note) != null && Line(implied, PdfPrintCheck.CheckBleed, PrintCheckLine.Ok).Text.Contains("5 mm"),
                "sans zone de fond perdu déclarée, la mesure se prend entre la coupe et le bord de la page — et le rapport le dit");

            // Un PDF de lecture : ni coupe, ni fond perdu.
            var reading = new Pdf("1.4");
            reading.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            reading.Object(2, "<< /Type /Pages /Count 3 /Kids [10 0 R 11 0 R 12 0 R] /MediaBox [0 0 " + Pt(210) + " " + Pt(297) + "] >>");
            reading.Object(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman >>");
            reading.Object(4, "<< /Type /Font /Subtype /Type0 /BaseFont /XYZ+Noto /DescendantFonts [5 0 R] >>");
            reading.Object(5, "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /XYZ+Noto /FontDescriptor 6 0 R >>");
            reading.Object(6, "<< /Type /FontDescriptor /FontFile2 7 0 R >>");
            reading.Stream(7, "", new byte[] { 0, 1 });
            reading.Object(8, "<< /Type /Font /Subtype /Type3 /Resources << /Font << /F9 3 0 R >> >> >>");
            reading.Object(10, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >>");
            reading.Object(11, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + Pt(297) + " " + Pt(210) + "] /Resources << /Font << /F3 8 0 R >> /XObject << /X1 13 0 R >> >> >>");
            reading.Object(12, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + Pt(148) + " " + Pt(210) + "] /Resources << >> >>");
            reading.Stream(13, "/Type /XObject /Subtype /Form /Resources << /Font << /F5 14 0 R >> >>", new byte[] { 32 });
            reading.Object(14, "<< /Type /Font /Subtype /TrueType /BaseFont /Arial >>");
            var readingBytes = reading.End("/Root 1 0 R /Size 20");
            var loose = PdfPrintCheck.Inspect(readingBytes, new PrintCheckOptions { WidthMm = 210, HeightMm = 297 });
            t.Check(Line(loose, PdfPrintCheck.CheckBleed, PrintCheckLine.Problem) != null && Line(loose, PdfPrintCheck.CheckBleed, PrintCheckLine.Problem).Text.Contains("zone de coupe"), "un PDF de lecture ne déclare pas de zone de coupe : le fond perdu ne se mesure pas, et c'est dit");
            var turned = false;
            var off = false;
            foreach (var line in loose.Lines)
            {
                if (line.Check == PdfPrintCheck.CheckSize && line.Level == PrintCheckLine.Problem && line.Text.Contains("autre sens") && line.Text.Contains("page 2")) turned = true;
                if (line.Check == PdfPrintCheck.CheckSize && line.Level == PrintCheckLine.Problem && line.Text.Contains("pas la taille attendue") && line.Text.Contains("page 3") && line.Text.Contains("148 × 210")) off = true;
            }
            t.Check(turned && off, "taille attendue A4 : la page en paysage est dite « dans l'autre sens », la page A5 « pas la taille attendue », chacune à son numéro");
            var consistency = Line(loose, PdfPrintCheck.CheckConsistency, PrintCheckLine.Problem);
            t.Check(consistency != null && consistency.Text.Contains("3 tailles"), "homogénéité : trois tailles dans le fichier (" + (consistency == null ? "-" : consistency.Text) + ")");
            var mixed = false;
            foreach (var line in loose.Lines) if (line.Check == PdfPrintCheck.CheckConsistency && line.Text.Contains("portrait (2)") && line.Text.Contains("paysage (1)")) mixed = true;
            t.Check(mixed, "…et un mélange de portrait et de paysage");
            var fonts = Line(loose, PdfPrintCheck.CheckFonts, PrintCheckLine.Problem);
            t.Check(loose.Fonts == 4 && fonts != null && fonts.Text.Contains("2 polices sur 4") && fonts.Text.Contains("Times-Roman (page 1)") && fonts.Text.Contains("Arial (page 2)") && !fonts.Text.Contains("Noto"),
                "polices : la composite incorporée et la Type 3 sont bonnes ; Times (page 1) et Arial, cachée dans un dessin réutilisé de la page 2, sont signalées (" + (fonts == null ? "-" : fonts.Text) + ")");

            // On ne vérifie que ce qui est coché.
            var only = PdfPrintCheck.Inspect(readingBytes, new PrintCheckOptions { Bleed = false, Size = false, Consistency = false, Fonts = true });
            var others = false;
            foreach (var line in only.Lines) if (line.Check != PdfPrintCheck.CheckFonts) others = true;
            t.Check(!others && only.Lines.Count == 1, "une seule case cochée : une seule vérification dans le rapport");
            var nothing = PdfPrintCheck.Inspect(readingBytes, new PrintCheckOptions { Bleed = false, Size = false, Consistency = false, Fonts = false });
            t.Check(nothing.Problems == 0 && nothing.Lines.Count == 1 && nothing.Lines[0].Text.Contains("Rien n'est coché"), "rien de coché : le rapport dit que rien n'a été vérifié");

            // Une rotation de page : la taille se lit tournée.
            var rotated = new Pdf("1.4");
            rotated.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            rotated.Object(2, "<< /Type /Pages /Count 1 /Kids [10 0 R] >>");
            rotated.Object(10, "<< /Type /Page /Parent 2 0 R /Rotate 90 /MediaBox [0 0 " + Pt(210) + " " + Pt(148) + "] /Resources << >> >>");
            var rotatedReport = PdfPrintCheck.Inspect(rotated.End("/Root 1 0 R /Size 20"), new PrintCheckOptions { WidthMm = 148, HeightMm = 210 });
            t.Check(Line(rotatedReport, PdfPrintCheck.CheckSize, PrintCheckLine.Problem) == null && Line(rotatedReport, PdfPrintCheck.CheckFonts, PrintCheckLine.Note) != null, "une page tournée d'un quart de tour se mesure tournée ; un fichier sans police le dit en « à savoir »");

            // Verrouillé, ou pas un PDF.
            var locked = new Pdf("1.4");
            locked.Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            locked.Object(2, "<< /Type /Pages /Count 0 /Kids [] >>");
            locked.Object(9, "<< /Filter /Standard /V 1 /R 2 /P -3904 >>");
            var lockedReport = PdfPrintCheck.Inspect(locked.End("/Root 1 0 R /Encrypt 9 0 R /Size 20"), new PrintCheckOptions());
            t.Check(!lockedReport.Readable && lockedReport.Locked && lockedReport.Lines[0].Text.Contains("Unlocker"), "un PDF verrouillé ne se contrôle pas : le rapport renvoie à l'Unlocker");
            var junk = PdfPrintCheck.Inspect(Encoding.ASCII.GetBytes(new string('x', 200)), new PrintCheckOptions());
            t.Check(!junk.Readable && !junk.Locked && junk.Lines[0].Text == "Ce n'est pas un PDF." && !PdfPrintCheck.Inspect(null, null).Readable, "un fichier qui n'est pas un PDF (ou vide) est dit tel");

            t.Equal("3, 5 à 8, 12", PdfPrintCheck.Ranges(new List<int> { 3, 5, 6, 7, 8, 12 }), "les pages se disent en plages");
            t.Equal("1, 2", PdfPrintCheck.Ranges(new List<int> { 1, 2 }), "…deux pages qui se suivent : « 1, 2 »");
            t.Check(PdfPrintCheck.Formats.Length >= 6 && PdfPrintCheck.Formats[1][0].StartsWith("A5"), "les formats proposés comptent l'A5");

            // Un vrai PDF de Marabook, traits de coupe et fond perdu de 5 mm.
            var ours = PdfWriter.Build(FineTypographyTests.PartsForTests("Un texte d'essai."), new PdfExportOptions { Title = "Essai", BleedMm = 5, CropMarks = true });
            var oursReport = PdfPrintCheck.Inspect(ours, new PrintCheckOptions());
            t.Check(oursReport.Readable && oursReport.Pages >= 1 && Line(oursReport, PdfPrintCheck.CheckBleed, PrintCheckLine.Problem) == null, "un PDF écrit par Marabook avec 5 mm de fond perdu passe le contrôle des fonds perdus (" + oursReport.ToText().Replace("\r", "").Replace("\n", " | ") + ")");
            var plain = PdfPrintCheck.Inspect(PdfWriter.Build(FineTypographyTests.PartsForTests("Un texte d'essai."), new PdfExportOptions { Title = "Essai" }), new PrintCheckOptions());
            t.Check(plain.Readable && Line(plain, PdfPrintCheck.CheckBleed, PrintCheckLine.Problem) != null, "…le même sans fond perdu est signalé");
        }

        // ------------------------------------------------------------ la recomposition (Chop-Chop)

        private static string Order(PdfAssembly plan)
        {
            var parts = new List<string>();
            foreach (var page in plan.Pages) parts.Add((char)('A' + page.Source) + (page.Page + 1).ToString());
            return string.Join(" ", parts.ToArray());
        }

        private static List<double[]> Sizes(int count)
        {
            var sizes = new List<double[]>();
            for (var i = 0; i < count; i++) sizes.Add(new[] { 419.5, 595.3 });
            return sizes;
        }

        private static void Assembly(Harness t)
        {
            var plan = new PdfAssembly();
            t.Equal(0, plan.Insert("a.pdf", Sizes(5), -1), "un PDF ouvert : ses pages, à la suite");
            t.Equal("A1 A2 A3 A4 A5", Order(plan), "…dans leur ordre");
            t.Check(plan.Untouched(5) && plan.Runs().Count == 1 && plan.Runs()[0].Pages.Count == 5, "rien n'a bougé : le plan est « tel que le fichier », d'un seul tenant");

            t.Equal(2, plan.Remove(new[] { 1, 3, 3, 99, -1 }), "retirer les pages 2 et 4 (un rang hors liste ou répété ne compte pas)");
            t.Equal("A1 A3 A5", Order(plan), "…elles ne sont plus du plan");
            t.Check(!plan.Untouched(5), "…et le plan n'est plus « tel que le fichier »");

            t.Equal(1, plan.Insert("b.pdf", Sizes(2), 1), "ajouter un autre PDF après la première page");
            t.Equal("A1 B1 B2 A3 A5", Order(plan), "…ses pages s'y glissent");
            var runs = plan.Runs();
            t.Check(runs.Count == 3 && runs[0].Source == 0 && runs[1].Source == 1 && runs[1].Pages.Count == 2 && runs[2].Pages[0] == 2 && runs[2].Pages[1] == 4, "les pages à recopier se groupent par suites tirées d'un même fichier : A1 / B1 B2 / A3 A5");
            t.Equal(5, plan.Insert("c.pdf", Sizes(1), 99), "un rang trop grand : à la fin");
            t.Equal("A1 B1 B2 A3 A5 C1", Order(plan), "…");

            t.Equal(0, plan.Move(new[] { 3, 5 }, 0), "glisser A3 et C1 tout au début");
            t.Equal("A3 C1 A1 B1 B2 A5", Order(plan), "…ensemble, dans leur ordre");
            t.Equal(4, plan.Move(new[] { 0, 1 }, 6), "les reglisser tout à la fin");
            t.Equal("A1 B1 B2 A5 A3 C1", Order(plan), "…");
            t.Equal(1, plan.Move(new[] { 3 }, 1), "glisser une page avant une autre, vers le début");
            t.Equal("A1 A5 B1 B2 A3 C1", Order(plan), "…");
            t.Equal(3, plan.Move(new[] { 1 }, 4), "…et vers la fin : elle se pose AVANT la page visée, en tenant compte de sa propre place");
            t.Equal("A1 B1 B2 A5 A3 C1", Order(plan), "…");
            t.Equal(-1, plan.Move(new int[0], 2), "rien à déplacer : rien ne bouge");

            var nudged = plan.Nudge(new[] { 1, 2 }, 1);
            t.Check(nudged.Count == 2 && nudged[0] == 2 && nudged[1] == 3 && Order(plan) == "A1 A5 B1 B2 A3 C1", "« Reculer » : les deux pages choisies passent derrière la suivante, ensemble (" + Order(plan) + ")");
            nudged = plan.Nudge(new[] { 2, 3 }, -1);
            t.Check(nudged[0] == 1 && Order(plan) == "A1 B1 B2 A5 A3 C1", "« Avancer » les ramène");
            nudged = plan.Nudge(new[] { 0 }, -1);
            t.Check(nudged[0] == 0 && Order(plan) == "A1 B1 B2 A5 A3 C1", "la première page n'avance pas plus loin");
            nudged = plan.Nudge(new[] { 5 }, 1);
            t.Check(nudged[0] == 5 && Order(plan) == "A1 B1 B2 A5 A3 C1", "la dernière ne recule pas plus loin");

            // Les doubles pages.
            var spreads = PdfAssembly.Spreads(6);
            t.Check(spreads.Count == 4 && spreads[0][0] == -1 && spreads[0][1] == 0 && spreads[1][0] == 1 && spreads[1][1] == 2 && spreads[3][0] == 5 && spreads[3][1] == -1,
                "vis-à-vis : la page 1 seule à droite, puis 2-3, 4-5, et la 6 seule à gauche — pair à gauche, impair à droite");
            t.Check(PdfAssembly.Spreads(1).Count == 1 && PdfAssembly.Spreads(0).Count == 0 && PdfAssembly.Spreads(5).Count == 3 && PdfAssembly.Spreads(5)[2][1] == 4, "…une page, aucune, un nombre impair");
            t.Check(PdfAssembly.Side(0) == "impaire, à droite" && PdfAssembly.Side(1) == "paire, à gauche", "chaque page dit son côté dans le livre");
        }
    }
}

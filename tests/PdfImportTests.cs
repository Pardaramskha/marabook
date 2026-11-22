using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Exchange;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C86 — 2.0.0 (21/11) : l'import PDF. Les paragraphes refaits
    /// depuis des mots posés sur des pages (sans PdfPig : les pages sont
    /// fabriquées ici), et les garde-fous de la dépendance téléchargée.</summary>
    public static class PdfImportTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C86 — import PDF : paragraphes déduits, dépendance PdfPig vérifiée");
            Paragraphs(t);
            Package(t);
        }

        // Une page A5 d'essai : marge gauche 60, droite 360, corps 11, interligne 14.
        private const double Left = 60, Right = 360, Body = 11, Leading = 14;

        private sealed class Typist
        {
            public readonly List<PdfPageWords> Pages = new List<PdfPageWords>();
            private double _y;
            public void NewPage() { Pages.Add(new PdfPageWords { Width = 420, Height = 595 }); _y = 540; }

            /// <summary>Une ligne : ses mots étalés de « left » à « right » (justifiée) ou serrés (courte).</summary>
            public void Line(string text, double left = Left, bool full = true, double size = Body, bool bold = false, bool italic = false, double gap = Leading)
            {
                _y -= gap;
                var words = text.Split(' ');
                var width = full ? (Right - left) / words.Length : size * 3.2;
                for (var i = 0; i < words.Length; i++)
                {
                    var x = left + i * width;
                    var it = italic && words[i].StartsWith("_", StringComparison.Ordinal);
                    Pages[Pages.Count - 1].Words.Add(new PdfWord { Text = words[i].Trim('_'), Left = x, Right = x + (full ? width - 3 : size * 2.8), Bottom = _y, Top = _y + size * 0.7, Size = size, Bold = bold, Italic = it });
                }
            }
        }

        private static string Text(TextParagraph paragraph)
        {
            var sb = new StringBuilder();
            foreach (var run in paragraph.Runs) sb.Append(run.Text);
            return sb.ToString();
        }

        private static void Paragraphs(Harness t)
        {
            var typist = new Typist();
            for (var p = 0; p < 3; p++)
            {
                typist.NewPage();
                typist.Line("Le Serment — chapitre un", Left + 80, false, 9);        // titre courant, en tête de chaque page
                if (p == 0)
                {
                    typist.Line("Chapitre premier", Left, false, 18, true, false, 40);
                    typist.Line("Keira descendit vers le port avant que le jour ne se", Left + 14, true, Body, false, false, 30); // alinéa
                    typist.Line("lève. Elle por-");
                    typist.Line("tait la lettre du marabout.", Left, false);                 // ligne courte : fin de paragraphe
                    typist.Line("Le quai était _désert_ et les barques dormaient sous", Left + 14, true, Body, false, true);
                    typist.Line("la brume, serrées les unes contre les autres comme");
                }
                else if (p == 1)
                {
                    typist.Line("des bêtes. Rien ne bougeait.", Left, false);               // suite du paragraphe de la page d'avant
                    typist.Line("Elle attendit longtemps, assise sur une bitte, que", Left + 14);
                    typist.Line("quelqu'un vienne. Demi-");
                    typist.Line("Lune se levait.", Left, false);                              // majuscule après le trait : pas un mot coupé
                }
                else
                {
                    typist.Line("Puis le vent tourna.", Left + 14, false);
                    typist.Line("Un écart plus grand que l'interligne sépare aussi.", Left, false, Body, false, false, 30);
                }
                typist.Line((p + 1).ToString(), Left + 150, false, 9, false, false, 60);     // folio, en pied
            }
            var result = PdfImport.Build(typist.Pages);
            var paragraphs = result.Document.Paragraphs;
            var texts = new List<string>();
            foreach (var paragraph in paragraphs) texts.Add(Text(paragraph));
            var dump = string.Join(" ¶ ", texts.ToArray());

            t.Check(!result.NoText && result.Pages == 3, "trois pages lues");
            t.Equal(6, result.DroppedLines, "les titres courants (au fil des pages) et les folios sont écartés");
            t.Check(!dump.Contains("chapitre un") && !texts.Contains("1") && !texts.Contains("2"), "…ils ne sont plus dans le texte");
            t.Check(paragraphs.Count == 6, "six paragraphes refaits (" + paragraphs.Count + ") : " + dump);
            if (paragraphs.Count != 6) return;
            t.Check(texts[0] == "Chapitre premier" && paragraphs[0].StyleId == "title1" && result.Headings == 1, "une ligne nettement plus grosse que le corps devient un titre");
            t.Equal("Keira descendit vers le port avant que le jour ne se lève. Elle portait la lettre du marabout.", texts[1], "les lignes d'un paragraphe sont réunies, le mot coupé en fin de ligne recollé");
            t.Check(paragraphs[1].StyleId == "body" && result.Rejoined == 1, "…un seul mot recollé dans tout le texte");
            t.Equal("Le quai était désert et les barques dormaient sous la brume, serrées les unes contre les autres comme des bêtes. Rien ne bougeait.", texts[2], "un alinéa ouvre un paragraphe, et il se poursuit sur la page suivante");
            var italic = paragraphs[2].Runs.Find(delegate(TextRun r) { return r.Italic == true; });
            t.Check(italic != null && italic.Text.Trim() == "désert" && paragraphs[2].Runs.Count == 3, "le mot en italique fait un passage à part (" + paragraphs[2].Runs.Count + " passages)");
            t.Equal("Elle attendit longtemps, assise sur une bitte, que quelqu'un vienne. Demi- Lune se levait.", texts[3], "un trait d'union suivi d'une majuscule n'est pas pris pour une coupe");
            t.Equal("Puis le vent tourna.", texts[4], "une ligne courte fait son paragraphe");
            t.Equal("Un écart plus grand que l'interligne sépare aussi.", texts[5 + 0], "un écart vertical ouvre un paragraphe");
            t.Check(PdfImport.Summary(result).Contains("3 pages") && PdfImport.Summary(result).Contains("relisez"), "le compte rendu dit ce qui a été déduit : " + PdfImport.Summary(result));

            // Un PDF scanné : des pages, aucun mot.
            var scanned = PdfImport.Build(new List<PdfPageWords> { new PdfPageWords { Width = 420, Height = 595 }, new PdfPageWords() });
            t.Check(scanned.NoText && PdfImport.Summary(scanned).Contains("scanné"), "un PDF sans texte est reconnu comme scanné");
            t.Check(PdfImport.Build(null).NoText && PdfImport.Build(new List<PdfPageWords>()).NoText, "rien : rien, sans erreur");

            // Les lignes : deux mots de même pied sur la même ligne, même posés dans le désordre.
            var page = new PdfPageWords();
            page.Words.Add(new PdfWord { Text = "monde", Left = 100, Right = 140, Bottom = 500.4, Top = 508, Size = 11 });
            page.Words.Add(new PdfWord { Text = "Bonjour", Left = 50, Right = 95, Bottom = 500, Top = 508, Size = 11 });
            page.Words.Add(new PdfWord { Text = "Dessous", Left = 50, Right = 95, Bottom = 486, Top = 494, Size = 11 });
            var lines = PdfImport.Lines(page, 0);
            t.Check(lines.Count == 2 && lines[0].Text == "Bonjour monde" && lines[1].Text == "Dessous", "les mots se rangent en lignes, de gauche à droite et de haut en bas");

            bool bold, slanted;
            PdfImport.StyleOfFont("ABCDEF+EBGaramond-BoldItalic", out bold, out slanted);
            t.Check(bold && slanted, "« …-BoldItalic » : grasse et italique");
            PdfImport.StyleOfFont("Georgia", out bold, out slanted);
            t.Check(!bold && !slanted, "un nom nu : ni l'un ni l'autre");
            PdfImport.StyleOfFont("XYZ+Minion-It", out bold, out slanted);
            t.Check(!bold && slanted, "« -It » : italique");
        }

        // ------------------------------------------------------------ la dépendance

        private static void Package(Harness t)
        {
            Uri uri;
            t.Check(Uri.TryCreate(PdfPigPackage.Url, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Host == PdfPigPackage.Host && PdfPigPackage.Url.Contains("/" + PdfPigPackage.Version + "/"),
                "la source est en HTTPS, sur le dépôt officiel de NuGet, à une version fixée");
            t.Check(Convert.FromBase64String(PdfPigPackage.PackageSha512).Length == 64, "l'empreinte de l'archive est un SHA-512");
            t.Check(PdfPigPackage.Files.Count == 6 && PdfPigPackage.Files.ContainsKey(PdfPigPackage.MainAssembly), "six bibliothèques nommées, dont la principale");
            foreach (var pair in PdfPigPackage.Files) t.Check(pair.Value.Length == 64 && pair.Key.EndsWith(".dll", StringComparison.Ordinal) && pair.Key.IndexOfAny(new[] { '/', '\\' }) < 0, "« " + pair.Key + " » a son empreinte SHA-256, et pas de chemin");
            t.Check(PdfPigPackage.Prompt.StartsWith("Afin d'importer des fichiers PDF (de manière encore assez approximative), PdfPig doit être téléchargée comme dépendance de code.", StringComparison.Ordinal)
                && PdfPigPackage.Prompt.EndsWith("Voulez-vous continuer ?", StringComparison.Ordinal) && PdfPigPackage.Prompt.Contains(PdfPigPackage.Host), "la question dit ce qui sera téléchargé, d'où, et demande");

            t.Check(PdfPigPackage.Verify(null) != null && PdfPigPackage.Verify(new byte[0]) != null, "une archive vide est refusée");
            t.Check((PdfPigPackage.Verify(new byte[1000]) ?? "").Contains("taille"), "une archive de la mauvaise taille est refusée");
            var sameSize = new byte[PdfPigPackage.PackageSize];
            t.Check((PdfPigPackage.Verify(sameSize) ?? "").Contains("empreinte"), "une archive de la bonne taille mais d'une autre empreinte est refusée");

            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c86-" + Guid.NewGuid().ToString("N"));
            try
            {
                var folder = PdfPigPackage.FolderIn(dir);
                t.Check(folder.StartsWith(dir, StringComparison.Ordinal) && folder.EndsWith("pdfpig-" + PdfPigPackage.Version, StringComparison.Ordinal), "la dépendance a son dossier, par version");
                t.Check(!PdfPigPackage.IsInstalled(folder), "rien d'installé : non");
                t.Check(PdfPigPackage.Install(sameSize, folder) != null && !Directory.Exists(folder), "une archive refusée n'écrit rien");
                Directory.CreateDirectory(folder);
                foreach (var pair in PdfPigPackage.Files) File.WriteAllBytes(Path.Combine(folder, pair.Key), new byte[] { 1, 2, 3 });
                t.Check(!PdfPigPackage.IsInstalled(folder), "des fichiers du bon nom mais d'un autre contenu ne valent pas installation");
                PdfPigPackage.Remove(folder);
                t.Check(!Directory.Exists(folder), "la dépendance se retire");

                // Avec la vraie archive, si l'on en a une sous la main (MARABOOK_PDFPIG_NUPKG) : installation complète.
                var real = Environment.GetEnvironmentVariable("MARABOOK_PDFPIG_NUPKG");
                if (!string.IsNullOrEmpty(real) && File.Exists(real))
                {
                    var bytes = File.ReadAllBytes(real);
                    t.Check(PdfPigPackage.Verify(bytes) == null, "la vraie archive de NuGet est reconnue");
                    t.Check(PdfPigPackage.Install(bytes, folder) == null && PdfPigPackage.IsInstalled(folder) && Directory.GetFiles(folder).Length == 6, "…elle s'installe : six fichiers, pas un de plus");
                    var main = Path.Combine(folder, PdfPigPackage.MainAssembly);
                    var tampered = File.ReadAllBytes(main);
                    tampered[tampered.Length / 2] ^= 0x01;
                    File.WriteAllBytes(main, tampered);
                    t.Check(!PdfPigPackage.IsInstalled(folder), "…et un seul bit changé dans une bibliothèque installée la fait refuser");
                }
                else Console.WriteLine("  archive PdfPig absente (MARABOOK_PDFPIG_NUPKG) : l'installation réelle n'est pas jouée.");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}

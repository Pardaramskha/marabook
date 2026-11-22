using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Model;
using Marabook.Print;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C91 — 2.0.0, lot 30 (22/11) : ce que le lot met dans le cœur —
    /// la palette nommée (couleurs d'origine, couleurs personnalisées, reprise
    /// de celles d'un projet), les listes de pages des outils PDF, le
    /// compacteur de PDF (orphelins, doublons, flux compressés) et le verrou
    /// par mot de passe (AES-128, relu par son propre contrôle).</summary>
    public static class Lot30Tests
    {
        public static void Run(Harness t)
        {
            t.Suite("C91 — lot 30 : palette nommée, listes de pages, compacteur de PDF, verrou par mot de passe");
            Palette(t);
            Pages(t);
            Compact(t);
            Lock(t);
        }

        // ------------------------------------------------------------ la palette

        private static void Palette(Harness t)
        {
            var customBefore = new List<PaletteColor>(ColorPalette.Custom);
            var dismissedBefore = new List<string>(ColorPalette.Dismissed);
            try
            {
                ColorPalette.Custom.Clear();
                ColorPalette.Dismissed.Clear();
                t.Equal("Indigo", ColorPalette.Label("#5B67D8"), "une couleur d'origine paraît sous son nom");
                t.Equal("Rouge", ColorPalette.Label("#c0392b"), "…quelle que soit la casse de son code");
                t.Equal("#123456", ColorPalette.Label("#123456"), "une couleur inconnue garde son code");
                t.Equal("Sarcelle — #16A085", ColorPalette.Describe("#16a085"), "l'infobulle donne le nom et le code");
                var names = new HashSet<string>();
                var allNamed = true;
                var count = 0;
                foreach (var group in ColorPalette.Groups)
                    foreach (var color in group.Colors)
                    {
                        count++;
                        if (string.IsNullOrWhiteSpace(color.Name) || ColorPalette.Normalize(color.Hex) != color.Hex) allNamed = false;
                        names.Add(group.Name + "/" + color.Name);
                    }
                t.Check(allNamed && names.Count == count && count >= 30 && ColorPalette.Groups.Length == 4, "quatre nuanciers d'origine, chaque couleur nommée, sans doublon de nom dans un nuancier (" + count + " couleurs)");
                t.Check(ColorPalette.Normalize("5b67d8") == "#5B67D8" && ColorPalette.Normalize("#5B67D") == null && ColorPalette.Normalize("rouge") == null && ColorPalette.Normalize(null) == null, "un code se normalise (« #RRGGBB »), ou n'en est pas un");

                var changes = 0;
                Action counter = delegate { changes++; };
                ColorPalette.Changed += counter;
                try
                {
                    var mine = ColorPalette.Add("#1b6e5a", "  Vert sapin ");
                    t.Check(mine != null && mine.Hex == "#1B6E5A" && mine.Name == "Vert sapin" && ColorPalette.Label("#1B6E5A") == "Vert sapin" && changes == 1, "une couleur ajoutée entre dans la palette, sous son nom");
                    t.Check(ColorPalette.Add("#1B6E5A", null) == mine && ColorPalette.Custom.Count == 1 && ColorPalette.Add("pas une couleur", "x") == null, "l'ajouter deux fois ne la double pas ; un mauvais code est refusé");
                    var second = ColorPalette.Add("#222222", "");
                    t.Check(ColorPalette.Custom[0] == second && ColorPalette.Label("#222222") == "#222222", "une couleur sans nom se dit par son code ; la dernière ajoutée vient en tête");
                    t.Check(!ColorPalette.Recolor(second, "#1B6E5A") && ColorPalette.Recolor(second, "#333333") && ColorPalette.FindCustom("#222222") == null, "changer le code d'une couleur : refusé si une autre l'a déjà");
                    ColorPalette.Rename(mine, "Sapin");
                    t.Equal("Sapin", ColorPalette.Label("#1b6e5a"), "renommée, elle change de nom partout");
                    // Une couleur d'origine renommée par l'auteur : son nom à lui l'emporte.
                    var indigo = ColorPalette.Add("#5B67D8", "Bleu de travail");
                    t.Check(ColorPalette.Label("#5B67D8") == "Bleu de travail" && ColorPalette.IsDefault("#5B67D8"), "une couleur d'origine peut recevoir un nom à soi");
                    ColorPalette.Remove(indigo);
                    t.Equal("Indigo", ColorPalette.Label("#5B67D8"), "…retirée, elle retrouve son nom d'origine");

                    // Les couleurs d'un projet d'avant la palette.
                    t.Check(ColorPalette.Absorb(new[] { "#abcdef", "#1B6E5A", "n'importe quoi" }) && ColorPalette.FindCustom("#ABCDEF") != null && ColorPalette.Custom.Count == 3, "les couleurs personnalisées d'un projet sont reprises : les inconnues seulement");
                    t.Check(!ColorPalette.Absorb(new[] { "#ABCDEF" }), "…une seule fois");
                    ColorPalette.Remove(ColorPalette.FindCustom("#ABCDEF"));
                    t.Check(!ColorPalette.Absorb(new[] { "#ABCDEF" }) && ColorPalette.FindCustom("#ABCDEF") == null, "une couleur retirée ne revient pas d'elle-même à la prochaine ouverture du projet");
                    t.Check(ColorPalette.Add("#ABCDEF", "Ciel d'hiver") != null && !ColorPalette.Dismissed.Contains("#ABCDEF"), "…mais on peut la rajouter");

                    // L'aller-retour par les réglages.
                    ColorPalette.Remove(ColorPalette.FindCustom("#333333"));
                    var json = Json.Write(new Dictionary<string, object> { { "palette", ColorPalette.ToJson() }, { "gone", new List<object>(new List<string>(ColorPalette.Dismissed).ToArray()) } });
                    var order = ColorPalette.Custom[0].Hex + " " + ColorPalette.Custom[1].Hex;
                    ColorPalette.Custom.Clear();
                    ColorPalette.Dismissed.Clear();
                    var root = Json.Parse(json);
                    ColorPalette.FromJson(Json.Field(root, "palette"), Json.Field(root, "gone"));
                    t.Check(ColorPalette.Custom.Count == 2 && ColorPalette.Custom[0].Hex + " " + ColorPalette.Custom[1].Hex == order && ColorPalette.Label("#ABCDEF") == "Ciel d'hiver" && ColorPalette.Dismissed.Contains("#333333"), "la palette se range dans les réglages et s'en relit : couleurs, noms, ordre, couleurs retirées");
                    ColorPalette.FromJson(Json.Parse("[{\"hex\":\"zzz\"},{\"hex\":\"#010203\",\"name\":\"A\"},{\"hex\":\"#010203\",\"name\":\"B\"},42]"), null);
                    t.Check(ColorPalette.Custom.Count == 1 && ColorPalette.Custom[0].Name == "A", "des réglages abîmés ne cassent rien : les entrées illisibles et les doublons tombent");
                }
                finally { ColorPalette.Changed -= counter; }
                t.Check(changes > 8, "chaque changement de la palette s'annonce (" + changes + ")");
            }
            finally
            {
                ColorPalette.Custom.Clear();
                ColorPalette.Custom.AddRange(customBefore);
                ColorPalette.Dismissed.Clear();
                foreach (var hex in dismissedBefore) ColorPalette.Dismissed.Add(hex);
            }
        }

        // ------------------------------------------------------------ les listes de pages

        private static void Pages(Harness t)
        {
            string problem;
            var pages = PdfPages.Parse("1-3, 7", 10, out problem);
            t.Check(pages != null && problem == null && string.Join(",", pages) == "0,1,2,6", "« 1-3, 7 » : les pages 1, 2, 3 et 7 (comptées à partir de 0)");
            t.Equal("1-3, 5, 9-10", PdfPages.Describe(PdfPages.Parse("5 ; 3-1,9-", 10, out problem)), "ordre libre, « 3-1 » lu comme « 1-3 », « 9- » jusqu'à la dernière ; rendu trié");
            t.Equal("1-2", PdfPages.Describe(PdfPages.Parse("-2", 10, out problem)), "« -2 » part de la première page");
            t.Equal("4", PdfPages.Describe(PdfPages.Parse("4,4, 4–4", 10, out problem)), "les doublons tombent ; le tiret demi-cadratin est accepté");
            t.Check(PdfPages.Parse("1-3, 12", 10, out problem) == null && problem.Contains("10 pages"), "une page hors du PDF : refusé, avec le nombre de pages");
            t.Check(PdfPages.Parse("0", 10, out problem) == null && PdfPages.Parse("deux", 10, out problem) == null && problem.Contains("deux") && PdfPages.Parse("  ", 10, out problem) == null && PdfPages.Parse("1-a", 10, out problem) == null, "zéro, un mot, rien, une suite bancale : refusés");
        }

        // ------------------------------------------------------------ des PDF d'essai

        private static byte[] Pdf(List<byte[]> objects, string trailerExtra)
        {
            var latin = Encoding.GetEncoding(28591);
            var output = new MemoryStream();
            var offsets = new List<long>();
            Action<string> write = delegate(string text) { var bytes = latin.GetBytes(text); output.Write(bytes, 0, bytes.Length); };
            write("%PDF-1.4\n");
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                write((i + 1) + " 0 obj\n");
                output.Write(objects[i], 0, objects[i].Length);
                write("\nendobj\n");
            }
            var xref = output.Position;
            write("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
            foreach (var offset in offsets) write(offset.ToString("0000000000") + " 00000 n \n");
            write("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R" + (trailerExtra ?? "") + " >>\nstartxref\n" + xref + "\n%%EOF\n");
            return output.ToArray();
        }

        private static byte[] Stream(string dict, byte[] data)
        {
            var latin = Encoding.GetEncoding(28591);
            var buffer = new MemoryStream();
            var head = latin.GetBytes("<< " + dict + " /Length " + data.Length + " >>\nstream\n");
            buffer.Write(head, 0, head.Length);
            buffer.Write(data, 0, data.Length);
            var tail = latin.GetBytes("\nendstream");
            buffer.Write(tail, 0, tail.Length);
            return buffer.ToArray();
        }

        /// <summary>Trois pages au même contenu (écrit trois fois, sans compression), un titre, et un gros objet que rien ne cite.</summary>
        private static byte[] Sample()
        {
            var latin = Encoding.GetEncoding(28591);
            var content = new StringBuilder();
            for (var i = 0; i < 300; i++) content.Append("0.2 0.3 0.6 rg ").Append(20 + i % 37 * 6).Append(" ").Append(20 + i % 53 * 9).Append(" 14 9 re f\n");
            var bytes = latin.GetBytes(content.ToString());
            var objects = new List<byte[]>();
            objects.Add(latin.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(latin.GetBytes("<< /Type /Pages /Count 3 /Kids [3 0 R 5 0 R 7 0 R] >>"));
            for (var i = 0; i < 3; i++)
            {
                objects.Add(latin.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 520] /Resources << >> /Contents " + (4 + 2 * i) + " 0 R >>"));
                objects.Add(Stream("", bytes));
            }
            objects.Add(latin.GetBytes("<< /Title (Le marabout \\(et la mer\\)) /Author <FEFF005200E9006D0069> /Producer /Marabook#20Tests >>"));
            var junk = new byte[20000];
            for (var i = 0; i < junk.Length; i++) junk[i] = (byte)('A' + i % 23);
            objects.Add(Stream("/Marabook /Lest", junk));
            return Pdf(objects, " /Info 9 0 R");
        }

        // ------------------------------------------------------------ le compacteur

        private static void Compact(Harness t)
        {
            var sample = Sample();
            PdfCompactReport report;
            var shrunk = PdfCompact.Shrink(sample, out report);
            t.Check(shrunk != null && report.Problem == null && report.Before == sample.Length && report.After == shrunk.Length, "le compacteur rend le PDF réécrit, et dit les deux poids");
            t.Check(report.Orphans == 1 && report.Duplicates == 2 && report.Deflated == 1, "un objet que rien ne citait écarté, deux contenus identiques ramenés au premier, un flux compressé (" + report.Orphans + ", " + report.Duplicates + ", " + report.Deflated + ")");
            t.Check(shrunk.Length < sample.Length / 5 && report.Percent >= 80, "le PDF d'essai perd plus de 80 % de son poids (" + sample.Length + " → " + shrunk.Length + " octets)");
            var read = PdfLite.Read(shrunk);
            var root = read == null ? null : read.Dict(read.Trailer["Root"]);
            var pagesNode = read == null ? null : read.Dict(read.Get(root, "Pages") == null ? null : root["Pages"]);
            var kids = read == null ? null : read.Get(pagesNode, "Kids") as List<object>;
            t.Check(read != null && kids != null && kids.Count == 3 && read.Objects.Count == 7, "il se relit : un catalogue, trois pages, un seul contenu, sa fiche d'informations — sept objets au lieu de dix");
            var contents = new HashSet<int>();
            byte[] decoded = null;
            if (kids != null)
                foreach (var kid in kids)
                {
                    var page = read.Dict(kid);
                    var reference = page == null ? null : page["Contents"] as PdfRef;
                    if (reference == null) continue;
                    contents.Add(reference.Number);
                    decoded = read.Decode(read.Objects[reference.Number] as PdfStream);
                }
            t.Check(contents.Count == 1 && decoded != null && decoded.Length > 5000 && Encoding.ASCII.GetString(decoded).StartsWith("0.2 0.3 0.6 rg 20 20 14 9 re f"), "les trois pages citent le même contenu, qui se décompresse à l'identique");
            var info = read == null ? null : read.Dict(read.Trailer.ContainsKey("Info") ? read.Trailer["Info"] : null);
            var title = info == null ? null : info["Title"] as PdfText;
            var author = info == null ? null : info["Author"] as PdfText;
            var producer = info == null ? null : info["Producer"] as PdfName;
            t.Check(title != null && Encoding.ASCII.GetString(title.Bytes) == "Le marabout (et la mer)" && author != null && author.Bytes.Length == 10 && author.Bytes[0] == 0xFE && producer != null && producer.Value == "Marabook Tests",
                "les chaînes passent intactes : parenthèses échappées, texte en UTF-16, nom à espace");
            PdfCompactReport again;
            var twice = PdfCompact.Shrink(shrunk, out again);
            t.Check(twice != null && again.Orphans == 0 && again.Duplicates == 0 && again.Deflated == 0 && Math.Abs(twice.Length - shrunk.Length) < 16, "le repasser n'y trouve plus rien à faire");
            t.Check(PdfCompact.Shrink(new byte[] { 1, 2, 3 }, out report) == null && report.Problem != null && PdfCompact.Shrink(Encoding.ASCII.GetBytes("%PDF-1.4\nrien du tout, pas de catalogue\n%%EOF\n"), out report) == null && report.Problem.Contains("catalogue"), "ce qui n'est pas un PDF, ou n'a pas de catalogue, est refusé avec la raison");
        }

        // ------------------------------------------------------------ le verrou

        private static void Lock(Harness t)
        {
            var sample = Sample();
            string problem;
            var locked = PdfLock.Encrypt(sample, "Un mot de passe à moi", out problem);
            t.Check(locked != null && problem == null && PdfLock.IsLocked(locked) && !PdfLock.IsLocked(sample), "la copie verrouillée porte un verrou ; l'original, non");
            t.Check(PdfLock.Opens(locked, "Un mot de passe à moi") && !PdfLock.Opens(locked, "Un mot de passe a moi") && !PdfLock.Opens(locked, "") && !PdfLock.Opens(locked, null) && !PdfLock.Opens(sample, "x"), "elle reconnaît son mot de passe — accents compris —, et lui seul");
            var text = Encoding.GetEncoding(28591).GetString(locked);
            t.Check(text.StartsWith("%PDF-1.6") && text.Contains("/Filter /Standard") && text.Contains("/V 4") && text.Contains("/CFM /AESV2") && text.Contains("/Length 128") && text.Contains("/Encrypt "), "c'est le verrou standard de PDF 1.6 : AES 128 bits (AESV2)");
            t.Check(!text.Contains("marabout") && !text.Contains("0.2 0.3 0.6 rg") && !text.Contains("AAAA") && !text.Contains("BCDEFGHIJ"), "ni les chaînes ni les contenus ne se lisent plus en clair");
            var read = PdfLite.Read(locked);
            t.Check(read != null && read.Objects.Count == 11 && read.Trailer.ContainsKey("ID") && read.Trailer.ContainsKey("Info"), "tous les objets y sont (les dix d'origine et le verrou), avec un identifiant de fichier");
            var other = PdfLock.Encrypt(sample, "Un mot de passe à moi", out problem);
            t.Check(other != null && Convert.ToBase64String(other) != Convert.ToBase64String(locked), "deux copies du même fichier sous le même mot de passe ne sont pas les mêmes octets (sel et vecteurs tirés au hasard)");
            t.Check(PdfLock.Encrypt(locked, "encore", out problem) == null && problem.Contains("déjà verrouillé"), "un PDF déjà verrouillé n'est pas reverrouillé : on renvoie à l'Unlocker");
            t.Check(PdfLock.Encrypt(sample, "", out problem) == null && problem != null && PdfLock.Encrypt(sample, "mot€", out problem) == null && problem.Contains("€") && PdfLock.Encrypt(sample, new string('a', 33), out problem) == null && problem.Contains("32"), "un mot de passe vide, à caractère hors du jeu, ou de plus de 32 caractères est refusé, avec la raison");
            t.Check(PdfLock.Encrypt(new byte[] { 1, 2, 3 }, "x", out problem) == null && problem != null, "ce qui n'est pas un PDF n'est pas verrouillé");
            t.Check(PdfLock.PasswordProblem("Une phrase longue, c'est mieux !") == null, "une phrase de 32 caractères convient");
            // Un PDF verrouillé ne se compacte pas : son contenu est chiffré.
            PdfCompactReport report;
            t.Check(PdfCompact.Shrink(locked, out report) == null && report.Problem.Contains("verrouillé"), "le compacteur refuse un PDF verrouillé, et renvoie à l'Unlocker");
        }
    }
}

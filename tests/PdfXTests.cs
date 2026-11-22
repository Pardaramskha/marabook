using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Print;

namespace Marabook.Tests
{
    /// <summary>C85 — 2.0.0 (21/11) : le profil ICC livré avec Marabook, le
    /// petit lecteur de PDF, et le contrôle PDF/X-1a:2003 refait — éprouvé
    /// sur des fichiers écrits à la main, chacun fautif d'une seule façon.</summary>
    public static class PdfXTests
    {
        private static readonly Encoding Latin = Encoding.GetEncoding(28591);

        public static void Run(Harness t)
        {
            t.Suite("C85 — PDF/X : profil ICC livré, lecteur de PDF, contrôle des objets");
            EmbeddedProfile(t);
            Reader(t);
            Conforming(t);
            Defects(t);
        }

        // ------------------------------------------------------------ le profil livré

        private static void EmbeddedProfile(Harness t)
        {
            var profile = IccProfiles.Embedded();
            t.Check(profile != null && profile.Bytes != null, "Marabook livre son profil ICC");
            if (profile == null) return;
            t.Equal(1052608, profile.Bytes.Length, "sa taille est celle du fichier vérifié");
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(profile.Bytes)).Replace("-", "").ToLowerInvariant();
            t.Equal(IccProfiles.EmbeddedSha256, hash, "son empreinte SHA-256 est celle notée à l'approvisionnement");
            t.Check(IccProfiles.IsCmykPrinterProfile(profile.Bytes) && IccProfiles.SizeMatches(profile.Bytes), "c'est un profil d'imprimante CMJN, entier");
            t.Check(IccProfiles.HasTag(profile.Bytes, "A2B1") && IccProfiles.HasTag(profile.Bytes, "B2A1") && !IccProfiles.HasTag(profile.Bytes, "zzzz"), "il porte la table colorimétrique que PDF/X exige");
            t.Equal("ISO Coated v2 (basICColor)", IccProfiles.DescriptionOf(profile.Bytes), "sa description");
            t.Check(ReferenceEquals(IccProfiles.FindFogra39(), profile), "c'est lui que l'export prend, avant tout profil de la machine");
            using (var licence = typeof(IccProfiles).Assembly.GetManifestResourceStream("icc/LICENCE-basICColor.txt"))
            using (var reader = licence == null ? null : new StreamReader(licence))
                t.Check(reader != null && reader.ReadToEnd().Contains("basICColor GmbH"), "sa licence voyage avec lui");
            var truncated = new byte[profile.Bytes.Length - 10];
            Array.Copy(profile.Bytes, truncated, truncated.Length);
            t.Check(!IccProfiles.SizeMatches(truncated), "un profil tronqué se reconnaît");
        }

        // ------------------------------------------------------------ le lecteur

        private static void Reader(Harness t)
        {
            var lexer = new PdfLexer(Latin.GetBytes("<< /A 12 /B [1 2.5 (a\\(b\\)c) <4142>] /C 7 0 R /D /Nom#20long /E true /F null >> suite"), 0, true);
            var dict = lexer.Read() as Dictionary<string, object>;
            t.Check(dict != null && dict.Count == 6, "un dictionnaire se lit, clé par clé");
            if (dict == null) return;
            var list = dict["B"] as List<object>;
            t.Check((double)dict["A"] == 12 && list != null && list.Count == 4 && (double)list[1] == 2.5, "nombres et tableaux");
            t.Check(list != null && ((PdfText)list[2]).Value == "a(b)c" && ((PdfText)list[3]).Value == "AB", "chaînes entre parenthèses (échappées) et en hexadécimal");
            t.Check(dict["C"] is PdfRef && ((PdfRef)dict["C"]).Number == 7, "une référence « 7 0 R »");
            t.Check(((PdfName)dict["D"]).Value == "Nom long" && (bool)dict["E"] && dict["F"] == null, "un nom à caractère codé, un booléen, un nul");
            t.Check(((PdfKeyword)lexer.Read()).Value == "suite", "le mot qui suit est rendu tel quel");
            var content = new PdfLexer(Latin.GetBytes("1 0 0 rg 12 0 R"), 0, false);
            var tokens = new List<object>();
            while (!content.AtEnd) tokens.Add(content.Read());
            t.Check(tokens.Count == 7 && tokens[3] is PdfKeyword && tokens[6] is PdfKeyword, "dans un flux de page, « 12 0 R » reste trois mots : pas de référence");

            var pdf = PdfLite.Read(Build(Minimal()));
            t.Check(pdf != null && pdf.Version == "1.4" && pdf.Objects.Count == 9, "un fichier se lit : sa version, ses objets (" + (pdf == null ? 0 : pdf.Objects.Count) + ")");
            if (pdf == null) return;
            var catalog = pdf.Dict(pdf.Trailer["Root"]);
            t.Check(catalog != null && pdf.Name(catalog, "Type") == "Catalog", "le dictionnaire de fin mène au catalogue");
            PdfStream page = null;
            foreach (var value in pdf.Objects.Values) { var stream = value as PdfStream; if (stream != null && !stream.Dict.ContainsKey("Length1") && !stream.Dict.ContainsKey("Subtype")) page = stream; }
            t.Check(page != null && Latin.GetString(pdf.Decode(page)).Contains("0 0 0 1 k"), "un flux compressé se décompresse");
            t.Check(PdfLite.Read(new byte[40]) == null && PdfLite.Read(null) == null, "ce qui n'est pas un PDF : rien, sans erreur");
        }

        // ------------------------------------------------------------ fabriquer des PDF à la main

        /// <summary>Les objets d'un PDF/X-1a minimal et juste, par numéro :
        /// 1 catalogue, 2 pages, 3 page, 4 contenu, 5 police, 6 descripteur,
        /// 7 fichier de police, 8 informations, 9 ressources.</summary>
        private static Dictionary<int, string> Minimal()
        {
            var objects = new Dictionary<int, string>();
            objects[1] = "<< /Type /Catalog /Pages 2 0 R /OutputIntents [<< /Type /OutputIntent /S /GTS_PDFX /OutputConditionIdentifier (FOGRA39) /OutputCondition (Coated FOGRA39) /RegistryName (http://www.color.org) >>] >>";
            objects[2] = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>";
            objects[3] = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 700] /BleedBox [10 10 490 690] /TrimBox [20 20 480 680] /Resources 9 0 R /Contents 4 0 R >>";
            objects[4] = "STREAM:0 0 0 1 k /F1 12 Tf (x) Tj";
            objects[5] = "<< /Type /Font /Subtype /TrueType /BaseFont /ABCDEF+Essai /FontDescriptor 6 0 R >>";
            objects[6] = "<< /Type /FontDescriptor /FontName /ABCDEF+Essai /FontFile2 7 0 R >>";
            objects[7] = "RAW:<< /Length1 4 /Length 4 >>\nstream\nabcd\nendstream";
            objects[8] = "<< /Title (Essai) /CreationDate (D:20261122) /ModDate (D:20261122) /Trapped /False /GTS_PDFXVersion (PDF/X-1a:2003) >>";
            objects[9] = "<< /Font << /F1 5 0 R >> >>";
            return objects;
        }

        private static byte[] Deflate(string content)
        {
            using (var buffer = new MemoryStream())
            {
                buffer.WriteByte(0x78); buffer.WriteByte(0x9C);
                using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, true))
                {
                    var bytes = Latin.GetBytes(content);
                    deflate.Write(bytes, 0, bytes.Length);
                }
                return buffer.ToArray();
            }
        }

        private static byte[] Build(Dictionary<int, string> objects, string trailerExtra = "", string version = "1.4")
        {
            using (var output = new MemoryStream())
            {
                Action<string> write = delegate(string text) { var bytes = Latin.GetBytes(text); output.Write(bytes, 0, bytes.Length); };
                write("%PDF-" + version + "\n");
                var numbers = new List<int>(objects.Keys);
                numbers.Sort();
                foreach (var number in numbers)
                {
                    var body = objects[number];
                    write(number + " 0 obj\n");
                    if (body.StartsWith("STREAM:", StringComparison.Ordinal))
                    {
                        var data = Deflate(body.Substring(7));
                        write("<< /Filter /FlateDecode /Length " + data.Length + " >>\nstream\n");
                        output.Write(data, 0, data.Length);
                        write("\nendstream");
                    }
                    else if (body.StartsWith("RAW:", StringComparison.Ordinal)) write(body.Substring(4));
                    else write(body);
                    write("\nendobj\n");
                }
                write("trailer\n<< /Size " + (numbers.Count + 1) + " /Root 1 0 R /Info 8 0 R /ID [<0011> <0011>]" + trailerExtra + " >>\n%%EOF\n");
                return output.ToArray();
            }
        }

        // ------------------------------------------------------------ un fichier juste

        private static void Conforming(Harness t)
        {
            var report = PdfXInspector.Inspect(Build(Minimal()));
            t.Check(report.Conforms, "un PDF/X-1a minimal écrit à la main passe le contrôle" + (report.Problems.Count > 0 ? " — " + report.Problems[0] : ""));
            t.Check(report.Pages == 1 && report.Fonts == 1 && report.Images == 0 && report.Rules == PdfXInspector.RuleCount, "le compte rendu dit ce qui a été regardé : " + report.Summary());

            // Avec le vrai profil incorporé, en place du registre.
            var profile = IccProfiles.Embedded();
            if (profile != null)
            {
                var objects = Minimal();
                objects[1] = objects[1].Replace("/RegistryName (http://www.color.org)", "/DestOutputProfile 10 0 R");
                var head = "<< /N 4 /Length " + profile.Bytes.Length + " >>\nstream\n";
                var whole = new List<byte>(Build(objects));
                // l'objet 10, ajouté à la main avant le dictionnaire de fin
                var text = Latin.GetString(whole.ToArray());
                var cut = text.LastIndexOf("trailer", StringComparison.Ordinal);
                var bytes = new List<byte>(whole.GetRange(0, cut));
                bytes.AddRange(Latin.GetBytes("10 0 obj\n" + head));
                bytes.AddRange(profile.Bytes);
                bytes.AddRange(Latin.GetBytes("\nendstream\nendobj\n"));
                bytes.AddRange(whole.GetRange(cut, whole.Count - cut));
                var withProfile = PdfXInspector.Inspect(bytes.ToArray());
                t.Check(withProfile.Conforms, "…et avec le profil de Marabook incorporé" + (withProfile.Problems.Count > 0 ? " — " + withProfile.Problems[0] : ""));
            }

            // Le BAT que Marabook écrit lui-même, profil livré compris.
            var options = new PdfExportOptions { Title = "Le livre", Cmyk = true, PdfX = true, CropMarks = true, BleedMm = 5, IccProfile = profile == null ? null : profile.Bytes };
            var own = PdfXInspector.Inspect(PdfWriter.Build(FineTypographyTests.PartsForTests("Un texte d'essai, assez long pour faire une ligne."), options));
            t.Check(own.Conforms && own.Pages >= 1, "le BAT de Marabook passe le contrôle refait (" + own.Summary() + ")" + (own.Problems.Count > 0 ? " — " + own.Problems[0] : ""));
        }

        // ------------------------------------------------------------ un défaut à la fois

        private static void Expect(Harness t, string label, string expected, Dictionary<int, string> objects, string trailerExtra = "", string version = "1.4")
        {
            var report = PdfXInspector.Inspect(Build(objects, trailerExtra, version));
            var found = report.Problems.Exists(delegate(string p) { return p.Contains(expected); });
            t.Check(found && report.Problems.Count == 1, label + (found ? (report.Problems.Count == 1 ? "" : " — mais " + report.Problems.Count + " défauts : " + string.Join(" | ", report.Problems.ToArray())) : " — relevé : " + string.Join(" | ", report.Problems.ToArray())));
        }

        private static Dictionary<int, string> With(int number, string body)
        {
            var objects = Minimal();
            objects[number] = body;
            return objects;
        }

        private static Dictionary<int, string> Replaced(int number, string old, string replacement)
        {
            var objects = Minimal();
            if (!objects[number].Contains(old)) throw new InvalidOperationException("motif absent : " + old);
            objects[number] = objects[number].Replace(old, replacement);
            return objects;
        }

        private static void Defects(Harness t)
        {
            // — le document
            Expect(t, "un PDF 1.6 est refusé", "PDF 1.4 au plus", Minimal(), "", "1.6");
            Expect(t, "un fichier chiffré", "chiffré", Minimal(), " /Encrypt << /Filter /Standard >>");
            Expect(t, "pas de GTS_PDFXVersion", "ne se déclare pas PDF/X-1a", Replaced(8, " /GTS_PDFXVersion (PDF/X-1a:2003)", ""));
            Expect(t, "un titre vide", "pas de titre", Replaced(8, "/Title (Essai)", "/Title ()"));
            Expect(t, "pas de date de modification", "date de création ou de modification", Replaced(8, " /ModDate (D:20261122)", ""));
            Expect(t, "un piégeage « Unknown »", "piégeage", Replaced(8, "/Trapped /False", "/Trapped /Unknown"));
            Expect(t, "un piégeage écrit en chaîne, pas en nom", "piégeage", Replaced(8, "/Trapped /False", "/Trapped (False)"));
            Expect(t, "aucune condition d'impression", "condition d'impression n'est pas déclarée", With(1, "<< /Type /Catalog /Pages 2 0 R >>"));
            Expect(t, "une condition sans registre ni profil", "ni son registre", Replaced(1, " /RegistryName (http://www.color.org)", ""));
            Expect(t, "une condition sans identifiant", "pas d'identifiant (OutputConditionIdentifier)", Replaced(1, " /OutputConditionIdentifier (FOGRA39)", ""));
            Expect(t, "du JavaScript à l'ouverture", "JavaScript", Replaced(1, "/Pages 2 0 R", "/Pages 2 0 R /OpenAction << /S /JavaScript /JS (app.alert\\(1\\)) >>"));
            Expect(t, "des fichiers joints", "fichiers joints", Replaced(1, "/Pages 2 0 R", "/Pages 2 0 R /Names << /EmbeddedFiles << /Names [] >> >>"));
            var noId = PdfXInspector.Inspect(Latin.GetBytes(Latin.GetString(Build(Minimal())).Replace(" /ID [<0011> <0011>]", "")));
            t.Check(noId.Problems.Count == 1 && noId.Problems[0].Contains("identifiant (/ID)"), "pas d'identifiant de fichier");

            // — les boîtes
            Expect(t, "une page sans boîte de coupe", "boîte de coupe (TrimBox)", Replaced(3, " /TrimBox [20 20 480 680]", ""));
            Expect(t, "TrimBox et ArtBox ensemble", "à la fois", Replaced(3, "/TrimBox [20 20 480 680]", "/TrimBox [20 20 480 680] /ArtBox [30 30 470 670]"));
            Expect(t, "une ArtBox seule vaut une TrimBox", "RVB", Replaced(3, "/TrimBox [20 20 480 680]", "/ArtBox [20 20 480 680] /Group << /S /Transparency >>").ReplaceIn(3, " /Group << /S /Transparency >>", "").ReplaceIn(4, "0 0 0 1 k", "1 0 0 rg"));
            Expect(t, "la coupe qui dépasse le fond perdu", "dépasse sa boîte de fond perdu", Replaced(3, "/TrimBox [20 20 480 680]", "/TrimBox [5 20 480 680]"));
            Expect(t, "le fond perdu qui dépasse le format", "fond perdu d'une page dépasse son format", Replaced(3, "/BleedBox [10 10 490 690]", "/BleedBox [-5 10 490 690]"));
            Expect(t, "le format hérité du nœud parent est reconnu", "RVB", Replaced(3, " /MediaBox [0 0 500 700]", "").ReplaceIn(2, "/Count 1", "/Count 1 /MediaBox [0 0 500 700]").ReplaceIn(4, "0 0 0 1 k", "0 1 0 RG"));

            // — annotations, actions, transparence
            Expect(t, "un lien cliquable dans la page", "liens cliquables", Replaced(3, "/Contents 4 0 R", "/Contents 4 0 R /Annots [<< /Type /Annot /Subtype /Link /Rect [100 100 200 120] >>]"));
            t.Check(PdfXInspector.Inspect(Build(Replaced(3, "/Contents 4 0 R", "/Contents 4 0 R /Annots [<< /Type /Annot /Subtype /Text /Rect [492 692 499 699] >> << /Type /Annot /Subtype /PrinterMark /Rect [2 2 8 8] >>]"))).Conforms,
                "une annotation hors du fond perdu, un repère d'impression hors de la coupe : admis");
            Expect(t, "un repère d'impression dans la page", "se trouve dans la page", Replaced(3, "/Contents 4 0 R", "/Contents 4 0 R /Annots [<< /Type /Annot /Subtype /PrinterMark /Rect [100 100 110 110] >>]"));
            Expect(t, "un groupe de transparence sur la page", "transparence", Replaced(3, "/Contents 4 0 R", "/Contents 4 0 R /Group << /S /Transparency /CS /DeviceCMYK >>"));
            Expect(t, "une opacité de 50 %", "transparence", With(9, "<< /Font << /F1 5 0 R >> /ExtGState << /G1 << /ca 0.5 >> >> >>"));
            Expect(t, "un mode de fusion", "transparence", With(9, "<< /Font << /F1 5 0 R >> /ExtGState << /G1 << /BM /Multiply >> >> >>"));
            t.Check(PdfXInspector.Inspect(Build(With(9, "<< /Font << /F1 5 0 R >> /ExtGState << /G1 << /ca 1 /CA 1.0 /BM /Normal /SMask /None /OP true /TR2 /Default >> >> >>"))).Conforms,
                "un état graphique sans transparence (opacité 1, fusion normale, surimpression) : admis");
            Expect(t, "une courbe de transfert", "courbe de transfert", With(9, "<< /Font << /F1 5 0 R >> /ExtGState << /G1 << /TR /Identity >> >> >>"));
            Expect(t, "une trame nommée", "par son nom", With(9, "<< /Font << /F1 5 0 R >> /ExtGState << /G1 << /HT << /HalftoneType 1 /HalftoneName (x) >> >> >> >>"));

            // — les couleurs
            Expect(t, "un opérateur RVB dans le flux compressé de la page", "dessinée avec des couleurs RVB", With(4, "STREAM:0.2 0.4 0.6 rg 0 0 10 10 re f"));
            Expect(t, "« /DeviceRGB cs » dans le flux", "dessinée avec des couleurs RVB", With(4, "STREAM:/DeviceRGB cs 1 0 0 sc"));
            Expect(t, "un espace RVB dans les ressources", "en RVB", With(9, "<< /Font << /F1 5 0 R >> /ColorSpace << /C1 /DeviceRGB >> >>"));
            Expect(t, "un espace à profil ICC", "propre profil ICC", With(9, "<< /Font << /F1 5 0 R >> /ColorSpace << /C1 [/ICCBased 7 0 R] >> >>"));
            Expect(t, "du Lab", "Lab", With(9, "<< /Font << /F1 5 0 R >> /ColorSpace << /C1 [/Lab << /WhitePoint [1 1 1] >>] >> >>"));
            Expect(t, "une palette indexée sur du RVB", "en RVB", With(9, "<< /Font << /F1 5 0 R >> /ColorSpace << /C1 [/Indexed /DeviceRGB 1 <000000FFFFFF>] >> >>"));
            Expect(t, "un ton direct replié sur du RVB", "ton direct", With(9, "<< /Font << /F1 5 0 R >> /ColorSpace << /C1 [/Separation /Or /DeviceRGB << /FunctionType 2 /Domain [0 1] /N 1 >>] >> >>"));
            t.Check(PdfXInspector.Inspect(Build(With(9, "<< /Font << /F1 5 0 R >> /ColorSpace << /C1 [/Separation /All /DeviceCMYK << /FunctionType 2 /Domain [0 1] /N 1 >>] /C2 [/Indexed /DeviceCMYK 1 <0000000000000000>] /C3 /DeviceGray >> >>"))).Conforms,
                "repérage, palette CMJN, gris : admis");

            // — les images
            var image = Minimal();
            image[9] = "<< /Font << /F1 5 0 R >> /XObject << /I1 10 0 R >> >>";
            image[10] = "RAW:<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceCMYK /Length 4 >>\nstream\nabcd\nendstream";
            var fine = PdfXInspector.Inspect(Build(image));
            t.Check(fine.Conforms && fine.Images == 1, "une image CMJN en 8 bits : admise, et comptée");
            Action<string, string, string, string> badImage = delegate(string label, string expected, string old, string replacement)
            {
                var objects = new Dictionary<int, string>(image);
                objects[10] = objects[10].Replace(old, replacement);
                Expect(t, label, expected, objects);
            };
            badImage("une image RVB", "en RVB", "/ColorSpace /DeviceCMYK", "/ColorSpace /DeviceRGB");
            badImage("une image à masque de transparence", "transparence", "/ColorSpace /DeviceCMYK", "/ColorSpace /DeviceCMYK /SMask 7 0 R");
            badImage("une image en 16 bits", "16 bits", "/BitsPerComponent 8", "/BitsPerComponent 16");
            badImage("une image en LZW", "LZW", "/ColorSpace /DeviceCMYK", "/ColorSpace /DeviceCMYK /Filter /LZWDecode");
            badImage("une image en JPEG 2000", "JPEG 2000", "/ColorSpace /DeviceCMYK", "/ColorSpace /DeviceGray /Filter /JPXDecode");
            badImage("une image en JBIG2", "JBIG2", "/ColorSpace /DeviceCMYK", "/ColorSpace /DeviceGray /Filter [/JBIG2Decode]");
            badImage("un renvoi OPI", "OPI", "/ColorSpace /DeviceCMYK", "/ColorSpace /DeviceCMYK /OPI << /1.3 << >> >>");
            Expect(t, "une image RVB écrite dans le flux même", "en RVB", With(4, "STREAM:q BI /W 1 /H 1 /BPC 8 /CS /RGB ID abc EI Q 0 0 0 1 k"));
            t.Check(PdfXInspector.Inspect(Build(With(4, "STREAM:q BI /W 1 /H 1 /BPC 8 /CS /CMYK ID 1 0 0 rg EI Q 0 0 0 1 k"))).Conforms, "les octets d'une image écrite dans le flux ne sont pas pris pour des opérateurs");

            // — un formulaire emboîté : ses ressources et son contenu sont suivis
            var form = Minimal();
            form[9] = "<< /Font << /F1 5 0 R >> /XObject << /X1 10 0 R >> >>";
            form[10] = "RAW:<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Resources << /ColorSpace << /C1 /DeviceRGB >> >> /Length 9 >>\nstream\n0 0 0 1 k\nendstream";
            Expect(t, "un espace RVB dans un formulaire emboîté", "en RVB", form);
            form[10] = "RAW:<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Length 8 >>\nstream\n1 0 0 rg\nendstream";
            Expect(t, "un opérateur RVB dans un formulaire emboîté", "dessinée avec des couleurs RVB", form);
            form[10] = "RAW:<< /Type /XObject /Subtype /PS /Length 4 >>\nstream\nabcd\nendstream";
            Expect(t, "du PostScript incorporé", "PostScript", form);

            // — les polices
            Expect(t, "une police non incorporée", "polices ne sont pas incorporées", Replaced(6, " /FontFile2 7 0 R", ""));
            Expect(t, "une police standard sans descripteur", "polices ne sont pas incorporées", With(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
            var composite = Minimal();
            composite[5] = "<< /Type /Font /Subtype /Type0 /BaseFont /ABCDEF+Essai /Encoding /Identity-H /DescendantFonts [10 0 R] >>";
            composite[10] = "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /ABCDEF+Essai /FontDescriptor 6 0 R >>";
            t.Check(PdfXInspector.Inspect(Build(composite)).Conforms, "une police composite incorporée par son descendant : admise");
            composite[6] = composite[6].Replace(" /FontFile2 7 0 R", "");
            Expect(t, "une police composite non incorporée", "polices ne sont pas incorporées", composite);

            // — plusieurs défauts, et les répétitions
            var many = Minimal();
            many[3] = many[3].Replace("/Type /Page ", "/Type /Page /Extra 1 ");
            many[2] = "<< /Type /Pages /Kids [3 0 R 10 0 R 11 0 R] /Count 3 >>";
            many[10] = many[3];
            many[11] = many[3];
            many[4] = "STREAM:1 0 0 rg";
            var repeated = PdfXInspector.Inspect(Build(many));
            t.Check(repeated.Pages == 3 && repeated.Problems.Count == 1 && repeated.Problems[0].Contains("(page 1, et 2 autres fois)"), "un même défaut sur trois pages n'est dit qu'une fois, avec sa première page (" + (repeated.Problems.Count > 0 ? repeated.Problems[0] : "-") + ")");
            t.Check(PdfXCheck.Verify(Build(many)).Count == 1 && PdfXCheck.Verify(new byte[10]).Count == 1, "l'ancienne porte d'entrée rend les mêmes défauts");
        }
    }

    internal static class PdfXTestExtensions
    {
        public static Dictionary<int, string> ReplaceIn(this Dictionary<int, string> objects, int number, string old, string replacement)
        {
            if (!objects[number].Contains(old)) throw new InvalidOperationException("motif absent : " + old);
            objects[number] = objects[number].Replace(old, replacement);
            return objects;
        }
    }
}

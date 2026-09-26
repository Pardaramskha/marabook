using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Exchange;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C44 — les images dans les échanges (0.50.0, fin de patch) :
    /// l'export docx écrit les images posées dans word/media avec leur
    /// ANCRAGE (wp:anchor : position dans la zone de texte, habillage) et
    /// l'import les relit tels quels ; sans magasin, rien ne sort (comme
    /// avant) ; le Markdown importe « ![alt](fichier) » quand le fichier est
    /// là ; le RTF fait l'aller-retour de ses images par WPF.</summary>
    public static class ExchangeImagesTests
    {
        // Un PNG 1×1 valide (IHDR 1×1 RGBA, IDAT, IEND).
        private static readonly byte[] Png =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
            0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
            0x44, 0xAE, 0x42, 0x60, 0x82
        };

        public static void Run(Harness t)
        {
            t.Suite("C44 — images dans les échanges (0.50.0) : docx ancré, Markdown, RTF");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-exchange-images-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                DocxRoundTrip(t, dir);
                Markdown(t, dir);
                RtfRoundTrip(t, dir);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static TextParagraph Text(string text)
        {
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = text });
            return paragraph;
        }

        private static List<TextRun> ImageRuns(TextDocument document)
        {
            var runs = new List<TextRun>();
            foreach (var paragraph in document.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.ImageId != null) runs.Add(run);
            return runs;
        }

        private static string Entry(string path, string name)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var entry = zip.GetEntry(name);
                if (entry == null) return null;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        private static void DocxRoundTrip(Harness t, string dir)
        {
            var project = Project.CreateNew();
            var id = project.AddImage(Png, ".png");
            var styles = StyleSheet.CreateDefault();
            var document = new TextDocument();
            var anchored = Text("Avant ");
            anchored.Runs.Add(new TextRun
            {
                ImageId = id,
                Image = new ImageLayout { Name = "photo.png", Width = 100, Height = 50, X = 10, Y = 20, Wrap = ImageLayout.WrapAround }
            });
            anchored.Runs.Add(new TextRun { Text = " après." });
            document.Paragraphs.Add(anchored);
            var attached = Text("Ligne avec image attachée ");
            attached.Runs.Add(new TextRun { ImageId = id, Image = new ImageLayout { Width = 80, Height = 40 } });
            document.Paragraphs.Add(attached);

            var path = Path.Combine(dir, "images.docx");
            Docx.Export(document, styles, path, new PageSetup(), null, project);
            var media = Entry(path, "word/media/image1.png");
            t.Check(media != null, "l'image est écrite dans word/media/image1.png (une fois pour deux runs)");
            using (var zip = ZipFile.OpenRead(path))
            {
                var entry = zip.GetEntry("word/media/image1.png");
                t.Check(entry != null && entry.Length == Png.Length, "…avec ses octets d'origine");
            }
            var rels = Entry(path, "word/_rels/document.xml.rels");
            t.Check(rels != null && rels.Contains("Target=\"media/image1.png\"") && rels.Contains("relationships/image"), "la relation d'image est déclarée");
            var types = Entry(path, "[Content_Types].xml");
            t.Check(types != null && types.Contains("Extension=\"png\"") && types.Contains("image/png"), "le type de contenu png est déclaré");
            var xml = Entry(path, "word/document.xml");
            t.Check(xml != null && xml.Contains("<wp:anchor") && xml.Contains("<wp:wrapSquare wrapText=\"bothSides\"/>")
                && xml.Contains("<wp:posOffset>" + (long)Math.Round(10 * 9525.0) + "</wp:posOffset>")
                && xml.Contains("<wp:posOffset>" + (long)Math.Round(20 * 9525.0) + "</wp:posOffset>")
                && xml.Contains("cx=\"" + (long)Math.Round(100 * 9525.0) + "\""), "le dessin est ancré : offsets dans la zone de texte, habillage de part et d'autre, taille");
            t.Check(xml.Contains("<wp:wrapTopAndBottom/>") && xml.Contains("relativeFrom=\"line\"") && xml.Contains("<wp:align>center</wp:align>"),
                "l'image attachée sort sous sa ligne, centrée, texte au-dessus et en dessous");
            t.Check(xml.Contains("name=\"photo.png\""), "le nom de l'image est gardé");

            var back = Project.CreateNew();
            var imported = Docx.Import(path, StyleSheet.CreateDefault(), back);
            var runs = ImageRuns(imported);
            t.Equal(2, runs.Count, "l'import relit les deux images");
            t.Equal(1, back.Images.Count, "…une seule stockée (même média)");
            var first = runs.Count > 0 ? runs[0].Image : null;
            t.Check(first != null && first.X.HasValue && Math.Abs(first.X.Value - 10) < 0.01 && first.Y.HasValue && Math.Abs(first.Y.Value - 20) < 0.01
                && Math.Abs(first.Width - 100) < 0.01 && Math.Abs(first.Height - 50) < 0.01 && first.Wrap == ImageLayout.WrapAround,
                "la première revient avec sa position, sa taille et son habillage");
            var second = runs.Count > 1 ? runs[1].Image : null;
            t.Check(second != null && second.IsAttached && !second.X.HasValue && second.Wrap == ImageLayout.WrapExclude
                && Math.Abs(second.Width - 80) < 0.01, "la seconde revient attachée, centrée, texte au-dessus et en dessous, à sa taille");
            t.Equal("Avant  après.", imported.Paragraphs[0].ToPlainText(), "le texte autour est intact");

            var plainPath = Path.Combine(dir, "sans-magasin.docx");
            Docx.Export(document, styles, plainPath);
            var plainXml = Entry(plainPath, "word/document.xml");
            t.Check(plainXml != null && !plainXml.Contains("<wp:anchor") && Entry(plainPath, "word/media/image1.png") == null,
                "sans magasin : aucune image ne sort (comme avant)");
        }

        private static void Markdown(Harness t, string dir)
        {
            File.WriteAllBytes(Path.Combine(dir, "a.png"), Png);
            var markdown = "Voici ![une photo](a.png) ici.\n\nAbsente ![alt seul](nulle-part.png) fin.";
            var project = Project.CreateNew();
            var document = MarkdownExchange.Import(markdown, dir, project);
            t.Equal(3, document.Paragraphs.Count, "trois paragraphes (la ligne vide en fait un, comme avant)");
            var runs = document.Paragraphs[0].Runs;
            t.Check(runs.Count == 3 && runs[1].ImageId != null && runs[1].Image != null && runs[1].Image.Name == "a.png",
                "« ![alt](a.png) » devient un run image, à sa place, nommé a.png");
            t.Equal(1, project.Images.Count, "l'image est dans le magasin");
            t.Equal("Voici  ici.", document.Paragraphs[0].ToPlainText(), "le texte autour est intact");
            t.Equal("Absente alt seul fin.", document.Paragraphs[2].ToPlainText(), "fichier introuvable : l'alt reste en texte");
            var without = MarkdownExchange.Import(markdown);
            t.Equal("Voici une photo ici.", without.Paragraphs[0].ToPlainText(), "sans projet : l'alt reste en texte, comme avant");
        }

        private static void RtfRoundTrip(Harness t, string dir)
        {
            var project = Project.CreateNew();
            var id = project.AddImage(Png, ".png");
            var document = new TextDocument();
            var paragraph = Text("Texte ");
            paragraph.Runs.Add(new TextRun { ImageId = id, Image = new ImageLayout { Width = 60, Height = 30 } });
            paragraph.Runs.Add(new TextRun { Text = " suite." });
            document.Paragraphs.Add(paragraph);
            var path = Path.Combine(dir, "image.rtf");
            Rtf.Export(document, StyleSheet.CreateDefault(), path, project);
            var back = Project.CreateNew();
            var imported = Rtf.Import(path, StyleSheet.CreateDefault(), back);
            var runs = ImageRuns(imported);
            t.Check(runs.Count == 1 && back.Images.Count == 1, "rtf : l'image fait l'aller-retour (WPF \\pict), dans le magasin du projet (" + runs.Count + ")");
            t.Check(runs.Count == 1 && runs[0].Image != null && Math.Abs(runs[0].Image.Width - 60) < 1.5 && Math.Abs(runs[0].Image.Height - 30) < 1.5,
                "rtf : la taille voyage (" + (runs.Count == 1 && runs[0].Image != null ? runs[0].Image.Width.ToString("0.#") + "×" + runs[0].Image.Height.ToString("0.#") : "-") + ")");
            var plain = Rtf.Import(path, StyleSheet.CreateDefault());
            t.Equal(0, ImageRuns(plain).Count, "rtf sans projet : aucune image, comme avant");
        }
    }
}

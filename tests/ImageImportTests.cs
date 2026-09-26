using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C39 — les images d'un document importé (23/09/2026, premier
    /// retour de testeur) : un .docx entre avec ses images (dessins inline,
    /// ancrés, AlternateContent, vieux w:pict), une image citée deux fois
    /// n'est stockée qu'une fois, un format illisible est laissé de côté,
    /// sans projet rien ne change ; même chose pour un .odt (draw:frame
    /// inline ou entre les paragraphes) ; les octets font l'aller-retour
    /// .plot sans être recompressés ; l'en-tête d'image donne sa largeur
    /// sans décoder (ImageCache).</summary>
    public static class ImageImportTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C39 — images des documents importés (23/09)");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-images");
            Directory.CreateDirectory(dir);
            try
            {
                Docx(t, dir);
                Odt(t, dir);
                Headers(t);
                Paths(t);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        // Un PNG 1×1 valide (IHDR 1×1 RGBA, IDAT, IEND).
        private static readonly byte[] Png =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
            0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
            0x44, 0xAE, 0x42, 0x60, 0x82
        };

        private const string WNs = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
            + " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\""
            + " xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\""
            + " xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\""
            + " xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\""
            + " xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\""
            + " xmlns:v=\"urn:schemas-microsoft-com:vml\"";

        private static string Drawing(string rId, bool anchored)
        {
            var wrap = anchored ? "wp:anchor" : "wp:inline";
            return "<w:drawing><" + wrap + "><wp:extent cx=\"914400\" cy=\"914400\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
                + "<pic:pic><pic:blipFill><a:blip r:embed=\"" + rId + "\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></" + wrap + "></w:drawing>";
        }

        private static void Zip(string path, Dictionary<string, byte[]> entries)
        {
            using (var stream = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                foreach (var kv in entries)
                {
                    var entry = zip.CreateEntry(kv.Key);
                    using (var s = entry.Open()) s.Write(kv.Value, 0, kv.Value.Length);
                }
        }

        private static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text); }

        private static List<TextRun> ImageRuns(TextDocument document)
        {
            var runs = new List<TextRun>();
            foreach (var paragraph in document.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.ImageId != null) runs.Add(run);
            return runs;
        }

        private static void Docx(Harness t, string dir)
        {
            var body = "<w:body>"
                // 1. inline, entre deux textes
                + "<w:p><w:r><w:t xml:space=\"preserve\">Avant </w:t></w:r><w:r>" + Drawing("rId1", false) + "</w:r><w:r><w:t>après</w:t></w:r></w:p>"
                // 2. AlternateContent : Choice (dessin) + Fallback (VML) de la MÊME image → un seul run
                + "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\">" + Drawing("rId1", true) + "</mc:Choice>"
                + "<mc:Fallback><w:pict><v:shape><v:imagedata r:id=\"rId1\"/></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>"
                // 3. un EMF : format illisible, laissé de côté
                + "<w:p><w:r>" + Drawing("rId2", false) + "</w:r><w:r><w:t>schéma</w:t></w:r></w:p>"
                // 4. une image liée hors du fichier, et une relation inconnue
                + "<w:p><w:r>" + Drawing("rId3", false) + Drawing("rId9", false) + "</w:r></w:p>"
                // 5. le vieux w:pict seul, même image que 1
                + "<w:p><w:r><w:pict><v:shape><v:imagedata r:id=\"rId1\"/></v:shape></w:pict></w:r></w:p>"
                + "</w:body>";
            var rels = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/>"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"/word/media/image2.emf\"/>"
                + "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"http://exemple.test/x.png\" TargetMode=\"External\"/>"
                + "<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                + "</Relationships>";
            var path = Path.Combine(dir, "images.docx");
            Zip(path, new Dictionary<string, byte[]>
            {
                { "[Content_Types].xml", Utf8("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/></Types>") },
                { "_rels/.rels", Utf8("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>") },
                { "word/document.xml", Utf8("<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document " + WNs + ">" + body + "</w:document>") },
                { "word/_rels/document.xml.rels", Utf8(rels) },
                { "word/media/image1.png", Png },
                { "word/media/image2.emf", new byte[] { 1, 0, 0, 0, 108, 0, 0, 0 } }
            });

            var styles = StyleSheet.CreateDefault();
            var project = Project.CreateNew();
            var document = Exchange.Docx.Import(path, styles, project);
            var runs = ImageRuns(document);
            t.Equal(3, runs.Count, "trois runs image : l'inline, l'AlternateContent (une seule fois), le w:pict (obtenu : " + runs.Count + ")");
            t.Equal(1, project.Images.Count, "…mais UNE image stockée : la même entrée partage son id");
            t.Check(runs.Count == 3 && runs[0].ImageId == runs[1].ImageId && runs[1].ImageId == runs[2].ImageId, "les trois runs citent le même id");
            var stored = runs.Count > 0 ? project.FindImage(runs[0].ImageId) : null;
            t.Check(stored != null && stored.Extension == ".png" && stored.Bytes.Length == Png.Length && stored.Bytes[0] == 0x89,
                "les octets du PNG sont ceux du fichier, extension .png");
            t.Equal("Avant après", document.Paragraphs[0].ToPlainText(), "le texte autour de l'image est intact");
            t.Check(document.Paragraphs[0].Runs.Count == 3 && document.Paragraphs[0].Runs[1].ImageId != null,
                "l'image est à sa place, entre « Avant » et « après »");
            t.Equal("schéma", document.Paragraphs[2].ToPlainText(), "l'EMF est laissé de côté, son texte reste");
            // Le placement (0.50.0) : nom de l'entrée et taille du dessin (914 400 EMU = 96 px).
            var layout = runs.Count > 0 ? runs[0].Image : null;
            t.Check(layout != null && layout.Name != null && layout.Name.EndsWith(".png") && Math.Abs(layout.Width - 96) < 0.01
                && Math.Abs(layout.Height - 96) < 0.01 && layout.IsAttached, "docx : le run image porte le nom du fichier et la taille du wp:extent");
            t.Equal(0, document.Paragraphs[3].Runs.Count, "image externe et relation inconnue : rien");

            // Sans projet : comme avant, aucun run image, pas de plantage.
            var plain = Exchange.Docx.Import(path, StyleSheet.CreateDefault());
            t.Equal(0, ImageRuns(plain).Count, "sans magasin d'images, le document se lit sans image");
            t.Equal("Avant après", plain.Paragraphs[0].ToPlainText(), "…et le texte est le même");

            // Aller-retour .plot : l'image est écrite telle quelle, sans recompression.
            var item = new BinderItem { Kind = ItemKind.Text, Title = "Importé", Document = document };
            project.Category(Project.KeyWritings).Children.Add(item);
            project.RelinkParents();
            var plot = Path.Combine(dir, "images.plot");
            PlotFile.Save(project, plot);
            using (var zip = ZipFile.OpenRead(plot))
            {
                var entry = zip.GetEntry("images/" + runs[0].ImageId + ".png");
                t.Check(entry != null, "le .plot porte l'entrée images/<id>.png");
                // PIÈGE .NET 4 : « NoCompression » reste un flux Deflate à blocs
                // stockés (5 octets par bloc de 64 Ko), pas une entrée Stored.
                t.Check(entry != null && entry.CompressedLength >= entry.Length && entry.CompressedLength <= entry.Length + 16,
                    "…écrite sans recompression (un PNG l'est déjà ; Deflate à blocs stockés, à quelques octets près)");
            }
            var loaded = PlotFile.Load(plot);
            var back = loaded.FindImage(runs[0].ImageId);
            t.Check(back != null && back.Bytes.Length == Png.Length, "les octets reviennent du .plot");
            t.Equal(3, ImageRuns(loaded.FindById(item.Id).Document).Count, "les runs image aussi");
        }

        private static void Odt(Harness t, string dir)
        {
            const string ns = "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\""
                + " xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\""
                + " xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\""
                + " xmlns:xlink=\"http://www.w3.org/1999/xlink\""
                + " xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\""
                + " xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\"";
            var content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document-content " + ns + "><office:body><office:text>"
                + "<text:p>Photo : <draw:frame draw:name=\"Image1\" text:anchor-type=\"as-char\"><draw:image xlink:href=\"Pictures/photo.png\" xlink:type=\"simple\"/></draw:frame> voilà.</text:p>"
                + "<draw:frame draw:name=\"Image2\" text:anchor-type=\"page\"><draw:image xlink:href=\"./Pictures/photo.png\"/></draw:frame>"
                + "<text:p><draw:frame><draw:image xlink:href=\"http://exemple.test/loin.png\"/></draw:frame>Lien externe.</text:p>"
                + "</office:text></office:body></office:document-content>";
            var path = Path.Combine(dir, "images.odt");
            Zip(path, new Dictionary<string, byte[]>
            {
                { "mimetype", Utf8("application/vnd.oasis.opendocument.text") },
                { "content.xml", Utf8(content) },
                { "Pictures/photo.png", Png }
            });
            var project = Project.CreateNew();
            var document = Exchange.Odt.Import(path, StyleSheet.CreateDefault(), project);
            var runs = ImageRuns(document);
            t.Equal(2, runs.Count, "odt : l'image inline et le cadre entre les paragraphes (obtenu : " + runs.Count + ")");
            t.Equal(1, project.Images.Count, "…une seule image stockée (« ./Pictures » = « Pictures »)");
            t.Equal("Photo :  voilà.", document.Paragraphs[0].ToPlainText(), "le texte autour reste");
            t.Check(document.Paragraphs.Count == 3 && document.Paragraphs[1].Runs.Count == 1 && document.Paragraphs[1].Runs[0].ImageId != null,
                "le cadre ancré à la page fait son propre paragraphe");
            t.Equal("Lien externe.", document.Paragraphs[2].ToPlainText(), "une image liée en http est ignorée");
            t.Check(runs.Count > 0 && runs[0].Image != null && runs[0].Image.Name != null && runs[0].Image.Name.Length > 0,
                "odt : le run image porte le nom du fichier (" + (runs.Count > 0 && runs[0].Image != null ? runs[0].Image.Name : "-") + ")");
            t.Equal(0, ImageRuns(Exchange.Odt.Import(path, StyleSheet.CreateDefault())).Count, "sans projet : aucune image, comme avant");
        }

        private static void Headers(Harness t)
        {
            t.Equal(1, View.ImageCache.PixelWidthOf(Png), "PNG : la largeur lue dans IHDR");
            var wide = (byte[])Png.Clone();
            wide[16] = 0; wide[17] = 0; wide[18] = 0x0F; wide[19] = 0xA0; // 4000
            t.Equal(4000, View.ImageCache.PixelWidthOf(wide), "…grand format reconnu sans décoder");
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x0B, 0x08, 0x02, 0x00, 0x03, 0x00, 0x03, 0x00, 0x00, 0x00 };
            t.Equal(768, View.ImageCache.PixelWidthOf(jpeg), "JPEG : la largeur du SOF0 après un APP0");
            var gif = Utf8("GIF89a"); Array.Resize(ref gif, 13); gif[6] = 0x40; gif[7] = 0x01;
            t.Equal(320, View.ImageCache.PixelWidthOf(gif), "GIF : largeur en petit-boutien");
            var bmp = new byte[26]; bmp[0] = (byte)'B'; bmp[1] = (byte)'M'; bmp[18] = 0xE8; bmp[19] = 0x03;
            t.Equal(1000, View.ImageCache.PixelWidthOf(bmp), "BMP : largeur du DIB");
            t.Equal(0, View.ImageCache.PixelWidthOf(new byte[] { 1, 2, 3 }), "inconnu : 0");
            t.Equal(0, View.ImageCache.PixelWidthOf(null), "null : 0");
            t.Check(View.ImageCache.For(null) == null && View.ImageCache.For(new ProjectImage()) == null, "pas d'octets : pas d'image");
        }

        private static void Paths(Harness t)
        {
            t.Equal("word/media/a.png", ImportedImages.Resolve("word/", "media/a.png"), "cible relative à word/");
            t.Equal("word/media/a.png", ImportedImages.Resolve("word/", "/word/media/a.png"), "cible absolue");
            t.Equal("media/a.png", ImportedImages.Resolve("word/", "../media/a.png"), "« .. » résolu");
            t.Equal("Pictures/a.png", ImportedImages.Resolve("", "./Pictures/a.png"), "« ./ » ôté");
            t.Equal(null, ImportedImages.Resolve("word/", ""), "vide : null");
        }
    }
}

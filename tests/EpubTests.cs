using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Marabook.Exchange;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C35 — l'EPUB (22/09) : le plan (écrits dans l'ordre, pages
    /// dynamiques écartées, couverture), le zip (mimetype premier et non
    /// compressé, container, OPF, nav, NCX, CSS, un XHTML par écrit, images),
    /// chaque XML bien formé, les métadonnées, l'ordre de lecture, la table
    /// de navigation (récit seul), la mise en forme (gras, italique, listes,
    /// filet, notes, images, séparateur, interligne du document,
    /// échappement) et l'écrit seul.</summary>
    public static class EpubTests
    {
        private static readonly byte[] Pixel = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        public static void Run(Harness t)
        {
            t.Suite("C35 — EPUB");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c35-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                BinderItem book, chapter1, chapter3, toc, single;
                var project = Fixture(out book, out chapter1, out chapter3, out toc, out single);
                PlanAndOptions(t, project, book, toc, single);
                Book(t, project, book, Path.Combine(dir, "livre.epub"));
                Single(t, project, single, Path.Combine(dir, "ecrit.epub"));
                Escaping(t);
                // Un exemplaire stable pour le contrôle Calibre à la main.
                Epub.Write(Path.Combine(Path.GetTempPath(), "marabook-c35-livre.epub"), project, book, Epub.DefaultOptions(project, book));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static Project Fixture(out BinderItem book, out BinderItem chapter1, out BinderItem chapter3, out BinderItem toc, out BinderItem single)
        {
            var project = Project.CreateNew();
            project.Author = "Autrice d'essai";
            var writings = project.Category(Project.KeyWritings);
            single = writings.Children[0];
            single.Title = "Nouvelle seule";
            single.Document = TextDocument.FromPlainText("Une nouvelle sans livre.");

            book = new BinderItem { Kind = ItemKind.Book, Title = "Le marabout & la rivière", Book = new BookInfo() };
            book.Book.Subtitle = "Roman";
            book.Book.Isbn = "978-2-1234-5680-3";
            book.Book.Publisher = "Éditions de la sonde";
            book.Book.Year = "2026";
            book.ImageId = project.AddImage(Pixel, ".png");

            var titlePage = Text("Page de titre", "Le marabout & la rivière");
            titlePage.IsExtraPage = true; titlePage.ExtraSection = ExtraPages.SectionFront; titlePage.ExtraKind = ExtraPages.KindTitle;
            book.Children.Add(titlePage);
            toc = Text("Table des matières", "");
            toc.IsExtraPage = true; toc.IsToc = true; toc.ExtraSection = ExtraPages.SectionFront; toc.ExtraKind = ExtraPages.KindToc;
            book.Children.Add(toc);

            chapter1 = Text("Chapitre 1", "Fish & Chips <3 \"guillemets\".");
            var p = chapter1.Document.Paragraphs[0];
            p.Runs.Add(new TextRun { Text = " Gras", Bold = true });
            p.Runs.Add(new TextRun { Text = " italique", Italic = true, Color = "#AA0000" });
            var note = new Footnote { Text = "La note & sa suite." };
            chapter1.Document.Footnotes.Add(note);
            p.Runs.Add(new TextRun { Text = "1", FootnoteId = note.Id });
            var bullet1 = new TextParagraph { ListKind = "bullet" }; bullet1.Runs.Add(new TextRun { Text = "Premier point" });
            var bullet2 = new TextParagraph { ListKind = "bullet" }; bullet2.Runs.Add(new TextRun { Text = "Second point" });
            chapter1.Document.Paragraphs.Add(bullet1);
            chapter1.Document.Paragraphs.Add(bullet2);
            var rule = new TextParagraph(); rule.Runs.Add(new TextRun { IsRule = true });
            chapter1.Document.Paragraphs.Add(rule);
            var separator = new TextParagraph { StyleId = StyleSheet.SeparatorId }; separator.Runs.Add(new TextRun { Text = "***" });
            chapter1.Document.Paragraphs.Add(separator);
            var image = new TextParagraph(); image.Runs.Add(new TextRun { ImageId = project.AddImage(Pixel, ".png") });
            chapter1.Document.Paragraphs.Add(image);
            var after = new TextParagraph { AlignOverride = "center" }; after.Runs.Add(new TextRun { Text = "Centré à la main" });
            chapter1.Document.Paragraphs.Add(after);
            book.Children.Add(chapter1);

            var chapter2 = Text("Chapitre 2", "Interligne large.");
            chapter2.Document.LineSpacing = 1.5;
            book.Children.Add(chapter2);
            var part = new BinderItem { Kind = ItemKind.Folder, Title = "Partie II" };
            chapter3 = Text("Chapitre 3", "Dans la partie.");
            part.Children.Add(chapter3);
            book.Children.Add(part);
            var thanks = Text("Remerciements", "Merci.");
            thanks.IsExtraPage = true; thanks.ExtraSection = ExtraPages.SectionBack; thanks.ExtraKind = ExtraPages.KindThanks;
            book.Children.Add(thanks);
            var index = Text("Index", "INDEX");
            index.IsExtraPage = true; index.ExtraSection = ExtraPages.SectionAnnex; index.ExtraKind = ExtraPages.KindIndex;
            book.Children.Add(index);
            writings.Children.Add(book);
            project.RelinkParents();
            return project;
        }

        private static BinderItem Text(string title, string body)
        {
            var item = new BinderItem { Kind = ItemKind.Text, Title = title };
            item.Document = TextDocument.FromPlainText(body);
            return item;
        }

        private static void PlanAndOptions(Harness t, Project project, BinderItem book, BinderItem toc, BinderItem single)
        {
            var plan = Epub.Plan(project, book);
            t.Equal("Page de titre|Chapitre 1|Chapitre 2|Chapitre 3|Remerciements", Titles(plan.Chapters), "le plan suit l'ordre du livre, parties traversées, pages dynamiques écartées");
            t.Equal("Table des matières|Index", Titles(plan.Skipped), "…et nomme ce qu'il écarte (TdM, index)");
            t.Check(plan.HasCover && plan.IsBook, "le livre a une couverture");
            var options = Epub.DefaultOptions(project, book);
            t.Check(options.Title == book.Title && options.Subtitle == "Roman" && options.Identifier == "978-2-1234-5680-3"
                && options.Publisher == "Éditions de la sonde" && options.Year == "2026" && options.Author == "Autrice d'essai",
                "les options se pré-remplissent depuis le livre (auteur du projet à défaut d'override)");
            var alone = Epub.Plan(project, single);
            t.Check(alone.Chapters.Count == 1 && alone.Skipped.Count == 0 && !alone.HasCover && !alone.IsBook, "un écrit seul : un chapitre, rien d'écarté");
            t.Equal("Nouvelle seule", Epub.DefaultOptions(project, single).Title, "…son titre pré-rempli");
        }

        private static void Book(Harness t, Project project, BinderItem book, string path)
        {
            var written = Epub.Write(path, project, book, Epub.DefaultOptions(project, book));
            t.Equal(5, written, "cinq chapitres écrits");
            using (var zip = ZipFile.OpenRead(path))
            {
                var first = zip.Entries[0];
                t.Check(first.FullName == "mimetype" && first.CompressedLength == first.Length && Read(first) == "application/epub+zip",
                    "« mimetype » est le premier fichier, non compressé, au bon contenu");
                var names = new List<string>();
                foreach (var entry in zip.Entries) names.Add(entry.FullName);
                t.Check(names.Contains("META-INF/container.xml") && names.Contains("OEBPS/content.opf") && names.Contains("OEBPS/nav.xhtml")
                    && names.Contains("OEBPS/toc.ncx") && names.Contains("OEBPS/style.css") && names.Contains("OEBPS/cover.xhtml"),
                    "container, OPF, nav, NCX, CSS et page de couverture sont là");
                t.Check(names.Contains("OEBPS/images/cover.png") && names.Contains("OEBPS/images/img1.png"), "la couverture et l'image du chapitre sont dans images/");

                // Chaque XML est bien formé.
                var malformed = new List<string>();
                foreach (var entry in zip.Entries)
                    if (entry.FullName.EndsWith(".xml") || entry.FullName.EndsWith(".xhtml") || entry.FullName.EndsWith(".opf") || entry.FullName.EndsWith(".ncx"))
                        if (!WellFormed(Read(entry))) malformed.Add(entry.FullName);
                t.Check(malformed.Count == 0, "tous les XML sont bien formés" + (malformed.Count > 0 ? " — cassés : " + string.Join(", ", malformed.ToArray()) : ""));

                var opf = Read(zip.GetEntry("OEBPS/content.opf"));
                t.Check(opf.Contains("<dc:identifier id=\"bookid\">urn:isbn:978-2-1234-5680-3</dc:identifier>")
                    && opf.Contains("<dc:title id=\"title\">Le marabout &amp; la rivière</dc:title>")
                    && opf.Contains("<dc:creator id=\"creator\">Autrice d'essai</dc:creator>")
                    && opf.Contains("<dc:publisher>Éditions de la sonde</dc:publisher>") && opf.Contains("<dc:date>2026</dc:date>")
                    && opf.Contains("<dc:language>fr</dc:language>"),
                    "les métadonnées OPF : ISBN en identifiant, titre échappé, auteur, éditeur, année, langue");
                t.Check(opf.Contains("properties=\"cover-image\"") && opf.Contains("<meta name=\"cover\" content=\"cover\"/>"), "la couverture est déclarée");
                var spine = opf.Substring(opf.IndexOf("<spine"));
                t.Check(spine.IndexOf("cover-page") < spine.IndexOf("\"ch1\"") && spine.IndexOf("\"ch1\"") < spine.IndexOf("\"ch5\""),
                    "l'ordre de lecture : couverture puis les cinq chapitres");

                var nav = Read(zip.GetEntry("OEBPS/nav.xhtml"));
                t.Check(nav.Contains(">Chapitre 1<") && nav.Contains(">Chapitre 3<") && !nav.Contains(">Page de titre<") && !nav.Contains(">Remerciements<"),
                    "la table de navigation liste le récit seul (liminaires et pages de fin hors table)");
                t.Check(nav.Contains("epub:type=\"bodymatter\" href=\"chapter-002.xhtml\""), "le repère « début du texte » vise le premier chapitre du récit");
                var ncx = Read(zip.GetEntry("OEBPS/toc.ncx"));
                t.Check(ncx.Contains("<text>Chapitre 2</text>") && ncx.Contains("chapter-003.xhtml"), "le NCX double la navigation");

                var css = Read(zip.GetEntry("OEBPS/style.css"));
                t.Check(css.Contains(".s-body {") && css.Contains("text-align: justify") && css.Contains(".s-separator {") && css.Contains("line-height: 1.2"),
                    "la CSS vient de la feuille de styles : corps justifié, séparateur, interligne 14,4 / 12 = 1,2");

                var ch1 = Read(zip.GetEntry("OEBPS/chapter-002.xhtml"));
                t.Check(ch1.Contains("<h1 class=\"chapter\">Chapitre 1</h1>"), "le titre de l'écrit en tête du chapitre");
                t.Check(ch1.Contains("Fish &amp; Chips &lt;3 &quot;guillemets&quot;."), "le texte est échappé");
                t.Check(ch1.Contains("<span style=\"font-weight: bold;\"> Gras</span>") && ch1.Contains("font-style: italic; color: #AA0000"), "gras et italique coloré en spans");
                t.Check(ch1.Contains("epub:type=\"noteref\"") && ch1.Contains("<aside epub:type=\"footnote\" class=\"footnote\" id=\"fn1\">") && ch1.Contains("La note &amp; sa suite."),
                    "la note de bas de page : renvoi et note EPUB 3");
                t.Check(ch1.Contains("<ul>\n<li class=\"s-body\">Premier point</li>\n<li class=\"s-body\">Second point</li>\n</ul>"), "la liste à puces");
                t.Check(ch1.Contains("<hr class=\"rule\"/>") && ch1.Contains("<p class=\"s-separator\">***</p>"), "le filet et le séparateur de scène (paragraphe stylé)");
                t.Check(ch1.Contains("<p class=\"figure\"><img src=\"images/img1.png\" alt=\"\"/></p>"), "l'image en figure");
                t.Check(ch1.Contains("<p class=\"s-body\" style=\"text-align: center;\">Centré à la main</p>"), "l'alignement posé à la main passe en style inline");
                t.Check(ch1.Contains("epub:type=\"chapter\""), "un chapitre du récit est un chapter");
                var title = Read(zip.GetEntry("OEBPS/chapter-001.xhtml"));
                t.Check(title.Contains("epub:type=\"frontmatter\""), "une liminaire est frontmatter");
                var ch2 = Read(zip.GetEntry("OEBPS/chapter-003.xhtml"));
                t.Check(ch2.Contains("line-height: 1.8"), "l'interligne du document (1,5 × 1,2) est posé sur ses paragraphes");
                t.Check(Read(zip.GetEntry("OEBPS/chapter-005.xhtml")).Contains("epub:type=\"backmatter\""), "une page de fin est backmatter");
                var everything = "";
                foreach (var entry in zip.Entries) if (entry.FullName.Contains("chapter-")) everything += Read(entry);
                t.Check(!everything.Contains("INDEX") && !everything.Contains(">Table des matières</h1>"), "les pages dynamiques ne sont pas dans l'EPUB");
            }
        }

        private static void Single(Harness t, Project project, BinderItem single, string path)
        {
            var options = Epub.DefaultOptions(project, single);
            options.ChapterHeadings = false;
            t.Equal(1, Epub.Write(path, project, single, options), "un écrit seul : un chapitre");
            using (var zip = ZipFile.OpenRead(path))
            {
                var names = new List<string>();
                foreach (var entry in zip.Entries) names.Add(entry.FullName);
                t.Check(!names.Contains("OEBPS/cover.xhtml") && names.Contains("OEBPS/chapter-001.xhtml"), "pas de couverture, un chapitre");
                var chapter = Read(zip.GetEntry("OEBPS/chapter-001.xhtml"));
                t.Check(!chapter.Contains("<h1") && chapter.Contains("Une nouvelle sans livre."), "sans titre de chapitre quand l'option est décochée");
                var opf = Read(zip.GetEntry("OEBPS/content.opf"));
                t.Check(opf.Contains("urn:uuid:") && opf.Contains("<dc:title id=\"title\">Nouvelle seule</dc:title>"), "sans ISBN : un identifiant unique ; le titre de l'écrit");
                t.Check(Read(zip.GetEntry("OEBPS/nav.xhtml")).Contains(">Nouvelle seule<"), "la navigation a son entrée");
            }
        }

        private static void Escaping(Harness t)
        {
            t.Equal("a &amp; b &lt;c&gt; &quot;d&quot;", Epub.Esc("a & b <c> \"d\""), "l'échappement XML");
            t.Equal("tab\tok", Epub.Esc("tab\t\u0001ok"), "les caractères de contrôle interdits tombent, la tabulation reste");
        }

        private static string Titles(List<BinderItem> items)
        {
            var titles = new List<string>();
            foreach (var item in items) titles.Add(item.Title);
            return string.Join("|", titles.ToArray());
        }

        private static string Read(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static bool WellFormed(string xml)
        {
            try
            {
                var document = new XmlDocument();
                document.LoadXml(xml);
                return true;
            }
            catch (XmlException) { return false; }
        }
    }
}

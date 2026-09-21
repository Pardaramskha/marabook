using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C32 — les sections des pages extra (b49 suite) : la section
    /// de chaque sorte, le placement automatique à la création (liminaire en
    /// tête, page de fin après le corps, annexe en queue), les pages
    /// dynamiques (index, notes de fin, glossaire) et la persistance v26 —
    /// une vieille page extra sans section est une liminaire.</summary>
    public static class ExtraPagesTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C32 — liminaires, pages de fin et annexes (b49)");

            // — La section d'une sorte.
            t.Equal(ExtraPages.SectionFront, ExtraPages.SectionOfKind(ExtraPages.KindDedication), "dédicace : liminaire");
            t.Equal(ExtraPages.SectionFront, ExtraPages.SectionOfKind(ExtraPages.KindToc), "table des matières : liminaire");
            t.Equal(ExtraPages.SectionBack, ExtraPages.SectionOfKind(ExtraPages.KindTocBack), "…ou page de fin par le menu de fin");
            t.Equal(ExtraPages.SectionBack, ExtraPages.SectionOfKind(ExtraPages.KindPostface), "postface : page de fin");
            t.Equal(ExtraPages.SectionAnnex, ExtraPages.SectionOfKind(ExtraPages.KindIndex), "index : annexe");
            t.Equal(ExtraPages.SectionFront, ExtraPages.SectionOfKind("inconnue"), "sorte inconnue : liminaire");
            t.Check(ExtraPages.IsDynamicKind(ExtraPages.KindIndex) && ExtraPages.IsDynamicKind(ExtraPages.KindEndnotes)
                && ExtraPages.IsDynamicKind(ExtraPages.KindGlossary) && ExtraPages.IsDynamicKind(ExtraPages.KindToc)
                && !ExtraPages.IsDynamicKind(ExtraPages.KindChronology), "dynamiques : TdM, index, notes de fin, glossaire");
            t.Equal("Page de fin", ExtraPages.SectionLabel(ExtraPages.SectionBack), "libellé de la section de fin");

            // — Une page d'avant les sections est une liminaire ; une page du
            //   récit n'a pas de section.
            var legacy = new BinderItem { Kind = ItemKind.Text, IsExtraPage = true };
            t.Equal(ExtraPages.SectionFront, ExtraPages.SectionOf(legacy), "page extra sans section : liminaire");
            t.Check(ExtraPages.SectionOf(new BinderItem { Kind = ItemKind.Text }) == null, "page du récit : pas de section");

            // — Le placement à la création.
            var project = Project.CreateNew();
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Tome", Book = new BookInfo() };
            project.Category(Project.KeyWritings).Children.Add(book);
            project.RelinkParents();
            var title = ExtraPages.Create(ExtraPages.KindTitle, book, project);
            var ch1 = new BinderItem { Kind = ItemKind.Text, Title = "Un", Document = TextDocument.FromPlainText("Léa dort.") };
            var ch2 = new BinderItem { Kind = ItemKind.Text, Title = "Deux", Document = TextDocument.FromPlainText("Léa à Paris.") };
            book.Children.Add(title);
            book.Children.Add(ch1);
            book.Children.Add(ch2);
            t.Equal(1, ExtraPages.InsertIndex(book, ExtraPages.SectionFront), "une liminaire va après les liminaires de tête, avant le premier écrit");
            t.Equal(3, ExtraPages.InsertIndex(book, ExtraPages.SectionBack), "une page de fin va en queue quand il n'y a pas d'annexe");
            t.Equal(3, ExtraPages.InsertIndex(book, ExtraPages.SectionAnnex), "une annexe va en queue");
            var index = ExtraPages.Create(ExtraPages.KindIndex, book, project);
            book.Children.Insert(ExtraPages.InsertIndex(book, ExtraPages.SectionAnnex), index);
            t.Equal(3, ExtraPages.InsertIndex(book, ExtraPages.SectionBack), "…puis la page de fin se glisse avant la première annexe");
            var thanks = ExtraPages.Create(ExtraPages.KindThanks, book, project);
            book.Children.Insert(ExtraPages.InsertIndex(book, ExtraPages.SectionBack), thanks);
            t.Equal("Pages de titre,Un,Deux,Remerciements,Index", Titles(book.Children), "l'ordre du livre après trois créations");
            t.Equal(ExtraPages.SectionAnnex, index.ExtraSection, "la page créée porte sa section");
            t.Equal(ExtraPages.KindThanks, thanks.ExtraKind, "…et sa sorte");
            t.Check(title.IsExtraPage && thanks.IsExtraPage && index.IsExtraPage, "toutes sont des pages extra (sans folio, hors table des matières)");
            var story = new List<BinderItem>();
            BookProgress.StoryTexts(book, story);
            t.Equal("Un,Deux", Titles(story), "le récit (la table des matières) ignore les trois sections");
            var backToc = ExtraPages.Create(ExtraPages.KindTocBack, book, project);
            t.Check(backToc.IsToc && backToc.ExtraSection == ExtraPages.SectionBack && backToc.ExtraKind == ExtraPages.KindToc,
                "la table des matières de fin est une TdM dynamique en section de fin");
            t.Check(ExtraPages.IsDynamic(backToc) && ExtraPages.IsDynamic(index) && !ExtraPages.IsDynamic(thanks), "IsDynamic sur les pages");

            // — Les pages dynamiques.
            var lines = 40;
            var document = new TextDocument();
            ExtraPages.FillIndex(document, new List<ExtraPages.IndexEntry>
            {
                new ExtraPages.IndexEntry { Name = "Paris", Category = "Lieux", Folios = { 12 } },
                new ExtraPages.IndexEntry { Name = "Zoé", Category = "Personnages", Folios = { 3, 40 } },
                new ExtraPages.IndexEntry { Name = "Léa", Category = "Personnages", Folios = { 1 } }
            }, lines);
            var plain = document.ToPlainText();
            t.Check(plain.IndexOf("LIEUX") < plain.IndexOf("PERSONNAGES") || (plain.IndexOf("Lieux") < plain.IndexOf("Personnages")),
                "index : les catégories dans l'ordre alphabétique");
            t.Check(plain.IndexOf("Léa") < plain.IndexOf("Zoé"), "index : les noms triés dans leur catégorie");
            t.Check(plain.Contains("3, 40"), "index : les folios à la suite");
            ExtraPages.FillIndex(document, new List<ExtraPages.IndexEntry>(), lines);
            t.Check(document.ToPlainText().Contains("se remplit"), "index vide : l'invite");

            ExtraPages.FillEndnotes(document, new List<ExtraPages.EndnoteChapter>
            {
                new ExtraPages.EndnoteChapter { Title = "Un", Notes = { "première", "seconde" } },
                new ExtraPages.EndnoteChapter { Title = "Sans note" },
                new ExtraPages.EndnoteChapter { Title = "Trois", Notes = { "troisième" } }
            }, lines);
            plain = document.ToPlainText();
            t.Check(plain.Contains("1. première") && plain.Contains("2. seconde") && plain.Contains("3. troisième"),
                "notes de fin : numérotées à la suite d'un écrit à l'autre");
            t.Check(!plain.Contains("Sans note"), "notes de fin : un écrit sans note n'apparaît pas");

            ExtraPages.FillGlossary(document, new List<ExtraPages.GlossaryEntry>
            {
                new ExtraPages.GlossaryEntry { Word = "spren", Definition = "esprit élémentaire" },
                new ExtraPages.GlossaryEntry { Word = "chasme", Definition = "faille du plateau" }
            }, lines);
            plain = document.ToPlainText();
            t.Check(plain.IndexOf("chasme") < plain.IndexOf("spren") && plain.Contains("spren — esprit élémentaire"),
                "glossaire : mots triés, définition à la suite");

            // — Persistance v26 : section et sorte survivent ; un fichier
            //   d'avant (sans section) rend des liminaires.
            var path = Path.Combine(Path.GetTempPath(), "marabook-c32-" + Guid.NewGuid().ToString("N") + ".plot");
            try
            {
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                BinderItem loadedThanks = null, loadedTitle = null;
                foreach (var item in loaded.AllItems())
                {
                    if (item.Title == "Remerciements") loadedThanks = item;
                    if (item.Title == "Pages de titre") loadedTitle = item;
                }
                t.Check(loadedThanks != null && loadedThanks.ExtraSection == ExtraPages.SectionBack && loadedThanks.ExtraKind == ExtraPages.KindThanks,
                    "section et sorte relues");
                t.Check(loadedTitle != null && loadedTitle.ExtraSection == ExtraPages.SectionFront, "les pages de titre restent liminaires");
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static string Titles(List<BinderItem> items)
        {
            var titles = new List<string>();
            foreach (var item in items) titles.Add(item.Title);
            return string.Join(",", titles.ToArray());
        }
    }
}

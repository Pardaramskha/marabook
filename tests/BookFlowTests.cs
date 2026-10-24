using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C51 — l'enchaînement des pages d'un livre (18/10, 1.0.6) :
    /// chaque document ouvre un recto ; un chapitre qui finit sur une page
    /// impaire est signalé, le livre entier aussi ; « Unifier » ajoute la
    /// page blanche ; l'avertissement se débraye par livre.</summary>
    public static class BookFlowTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C51 — enchaînement des pages d'un livre (1.0.6)");

            var book = new BinderItem { Kind = ItemKind.Book, Title = "Tome", Book = new BookInfo() };
            var a = new BinderItem { Kind = ItemKind.Text, Title = "A", Document = TextDocument.FromPlainText("a") };
            var b = new BinderItem { Kind = ItemKind.Text, Title = "B", Document = TextDocument.FromPlainText("b") };
            var part = new BinderItem { Kind = ItemKind.Folder, Title = "Partie" };
            var c = new BinderItem { Kind = ItemKind.Text, Title = "C", Document = TextDocument.FromPlainText("c") };
            var outOfBook = new BinderItem { Kind = ItemKind.Folder, Title = BinderItem.OutOfBookTitle, IsOutOfBook = true };
            var scrap = new BinderItem { Kind = ItemKind.Text, Title = "Brouillon", Document = TextDocument.FromPlainText("x") };
            book.Children.Add(a);
            book.Children.Add(b);
            book.Children.Add(part);
            part.Children.Add(c);
            book.Children.Add(outOfBook);
            outOfBook.Children.Add(scrap);
            var pages = new Dictionary<string, int> { { a.Id, 3 }, { b.Id, 2 }, { c.Id, 2 }, { scrap.Id, 7 } };
            Func<BinderItem, int> counter = delegate(BinderItem text) { return pages[text.Id]; };

            var texts = new List<BinderItem>();
            BookFlow.Texts(book, texts);
            t.Equal("A,B,C", Titles(texts), "les textes dans l'ordre des pages, le Hors-livre exclu");

            // A : p. 1-3 (finit impair) ; verso blanc p. 4 ; B : p. 5-6 (pair) ; C : p. 7-8 (pair). Total 8.
            var report = BookFlow.Analyse(book, counter);
            t.Equal(8, report.Total, "total : 3 + verso blanc + 2 + 2");
            t.Equal("A", Titles(report.OddChapters), "A finit sur la page 3, impaire ; B et C sur une page paire");
            t.Equal(3, report.OddEnd(a), "le folio impair de la dernière page de A");
            t.Equal(0, report.OddEnd(b), "rien pour B (p. 6)");
            t.Equal(0, report.OddEnd(scrap), "rien pour le Hors-livre");
            t.Equal(8, report.LastPage[c.Id], "la dernière page de C est la 8");
            t.Check(!report.BookEndsOdd, "le livre finit sur une page paire");

            // C passe à 3 pages : le livre finit sur la page 9, impaire —
            // le dernier chapitre et le livre sont signalés.
            pages[c.Id] = 3;
            report = BookFlow.Analyse(book, counter);
            t.Equal("A,C", Titles(report.OddChapters), "C finit sur la page 9 : signalé");
            t.Check(report.BookEndsOdd && report.Total == 9, "…et le livre entier finit sur une page impaire (9)");

            // Un texte sans page (vide, 0) n'entre pas dans le compte.
            pages[b.Id] = 0;
            report = BookFlow.Analyse(book, counter);
            t.Equal(7, report.Total, "un texte à zéro page ne compte pas (3 + blanc + 3)");
            t.Check(!report.LastPage.ContainsKey(b.Id), "…et n'a pas de dernière page");
            pages[b.Id] = 2;

            // « Unifier » : un paragraphe vide avec saut de page, au style du
            // dernier paragraphe.
            a.Document.Paragraphs[a.Document.Paragraphs.Count - 1].StyleId = "quote";
            var before = a.Document.Paragraphs.Count;
            var blank = BookFlow.Unify(a.Document);
            t.Check(a.Document.Paragraphs.Count == before + 1 && ReferenceEquals(a.Document.Paragraphs[before], blank), "Unifier ajoute un paragraphe en fin de document");
            t.Check(blank.PageBreakBefore && blank.Runs.Count == 0, "…vide, avec un saut de page devant : une page blanche");
            t.Equal("quote", blank.StyleId, "…au style du dernier paragraphe (le gabarit du document suit)");

            // L'avertissement se débraye par livre.
            t.Check(BookFlow.Enabled(book), "activé par défaut");
            book.Book.FlowWarnings = false;
            t.Check(!BookFlow.Enabled(book), "débrayé sur le livre");
            t.Check(BookFlow.Enabled(new BinderItem { Kind = ItemKind.Book }), "un livre sans BookInfo suit le défaut : activé");
            t.Check(!BookFlow.Enabled(null) && !BookFlow.Enabled(new BinderItem { Kind = ItemKind.Folder }), "pas un livre : rien");
        }

        private static string Titles(List<BinderItem> items)
        {
            var titles = new List<string>();
            foreach (var item in items) titles.Add(item.Title);
            return string.Join(",", titles.ToArray());
        }
    }
}

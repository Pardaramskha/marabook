using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C94 — le graphique des statistiques d'un livre (27/11, 2.0.0) :
    /// les textes écartés du graphique (par livre, relus du .plot), le
    /// partage tracés / écartés, et le plafond rond de l'échelle.</summary>
    public static class BookChartTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C94 — graphique des statistiques d'un livre (2.0.0)");

            var project = Project.CreateNew();
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Tome", Book = new BookInfo() };
            var title = new BinderItem { Kind = ItemKind.Text, Title = "Page de titre", Document = TextDocument.FromPlainText("Tome") };
            var one = new BinderItem { Kind = ItemKind.Text, Title = "Un", Document = TextDocument.FromPlainText("un long chapitre") };
            var two = new BinderItem { Kind = ItemKind.Text, Title = "Deux", Document = TextDocument.FromPlainText("un autre chapitre") };
            book.Children.Add(title);
            book.Children.Add(one);
            book.Children.Add(two);
            project.Category(Project.KeyWritings).Children.Add(book);

            t.Check(book.Book.ChartExcluded.Count == 0 && !BookChart.IsExcluded(book, title), "d'office, aucun texte n'est écarté du graphique");
            t.Check(BookChart.SetExcluded(book, title, true), "écarter la page de titre change le livre");
            t.Check(!BookChart.SetExcluded(book, title, true), "…l'écarter deux fois ne change plus rien");
            t.Check(BookChart.IsExcluded(book, title) && !BookChart.IsExcluded(book, one), "seule la page de titre est écartée");

            var texts = new List<BinderItem>();
            BookFlow.Texts(book, texts);
            var shown = new List<BinderItem>();
            var away = new List<BinderItem>();
            t.Check(!BookChart.Split(book, texts, shown, away), "le partage ne touche pas au livre quand tout existe encore");
            t.Equal("Un,Deux", Titles(shown), "les textes tracés, dans l'ordre des pages");
            t.Equal("Page de titre", Titles(away), "le texte écarté");

            // Un identifiant écarté dont le texte a disparu s'oublie.
            book.Book.ChartExcluded.Add("fantome");
            shown.Clear(); away.Clear();
            t.Check(BookChart.Split(book, texts, shown, away), "un texte écarté qui a quitté le livre : le partage le signale");
            t.Equal(1, book.Book.ChartExcluded.Count, "…et l'oublie");

            // Relu depuis le .plot.
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c94-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "graphique.plot");
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                var again = loaded.FindById(book.Id);
                t.Check(again != null && again.Book.ChartExcluded.Count == 1 && again.Book.ChartExcluded[0] == title.Id, "relus du .plot, les textes écartés sont là");
                t.Check(again != null && BookChart.IsExcluded(again, again.Children[0]) && !BookChart.IsExcluded(again, again.Children[1]), "…sur le même texte (l'identifiant tient)");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            t.Check(BookChart.SetExcluded(book, title, false) && !BookChart.IsExcluded(book, title), "le remettre le retire de la liste");
            t.Check(!BookChart.SetExcluded(book, title, false), "…le remettre deux fois ne change plus rien");

            // Le plafond de l'échelle : 1, 2, 2,5, 5 × 10ⁿ.
            t.Equal(10.0, BookChart.Ceiling(0), "plafond d'un livre vide : 10");
            t.Equal(10.0, BookChart.Ceiling(7), "7 → 10");
            t.Equal(20.0, BookChart.Ceiling(11), "11 → 20");
            t.Equal(2500.0, BookChart.Ceiling(2100), "2 100 → 2 500");
            t.Equal(5000.0, BookChart.Ceiling(2501), "2 501 → 5 000");
            t.Equal(20000.0, BookChart.Ceiling(18340), "18 340 → 20 000");
            t.Equal(50000.0, BookChart.Ceiling(50000), "50 000 pile → 50 000");
            t.Equal(100000.0, BookChart.Ceiling(50001), "50 001 → 100 000");
            t.Check(BookChart.Ceiling(18340) / BookChart.Divisions == 4000, "…et ses cinq divisions tombent rond (4 000)");
        }

        private static string Titles(List<BinderItem> items)
        {
            var names = new List<string>();
            foreach (var item in items) names.Add(item.Title);
            return string.Join(",", names.ToArray());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using UniversSale.Model;

namespace UniversSale.Tests
{
    /// <summary>C26 — objectifs et temps (b48) : le rythme d'un livre
    /// (échéance, taille, par jour), les sprints du journal, la moyenne.</summary>
    public static class PaceTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C26 — objectifs et temps (b48)");
            var culture = CultureInfo.GetCultureInfo("fr-FR");
            var today = new DateTime(2026, 9, 14);

            var book = new BinderItem { Kind = ItemKind.Book, Title = "Tome", Book = new BookInfo() };
            var one = new BinderItem { Kind = ItemKind.Text, Title = "1" };
            var two = new BinderItem { Kind = ItemKind.Text, Title = "2" };
            var extra = new BinderItem { Kind = ItemKind.Text, Title = "Titre", IsExtraPage = true };
            var part = new BinderItem { Kind = ItemKind.Folder, Title = "Partie" };
            part.Children.Add(two);
            book.Children.Add(extra);
            book.Children.Add(one);
            book.Children.Add(part);
            book.RelinkChildren();
            var words = new Dictionary<string, int> { { one.Id, 12000 }, { two.Id, 8000 }, { extra.Id, 500 } };
            Func<BinderItem, int> wordsOf = delegate(BinderItem item) { return words[item.Id]; };
            Func<BinderItem, int> charsOf = delegate(BinderItem item) { return words[item.Id] * 6; };

            var none = BookPace.Of(book, wordsOf, charsOf, today);
            t.Check(!none.HasDeadline && !none.HasSizeGoal && none.Describe(culture) == "", "sans échéance ni taille : rien à dire");
            t.Equal(20000, none.Done, "les mots du récit sont comptés, la liminaire non, la partie traversée");

            book.Book.SizeGoal = 80000;
            book.Book.Deadline = "2026-10-14"; // dans 30 jours
            var pace = BookPace.Of(book, wordsOf, charsOf, today);
            t.Check(pace.HasDeadline && pace.HasSizeGoal, "échéance et taille lues");
            t.Equal(30, pace.DaysLeft, "30 jours restants");
            t.Equal(60000, pace.Remaining, "60 000 mots restants");
            t.Check(Math.Abs(pace.PerDay - 60000.0 / 31) < 1e-9, "par jour = restant / (jours + aujourd'hui)");
            t.Check(Math.Abs(pace.Ratio - 0.25) < 1e-9, "un quart fait");
            var text = pace.Describe(culture);
            t.Check(text.Contains("14/10/2026") && text.Contains("30 jours") && text.Contains("restants") && text.Contains("par jour"), "la phrase : " + text);

            book.Book.SizeUnit = "chars";
            pace = BookPace.Of(book, wordsOf, charsOf, today);
            t.Equal(120000, pace.Done, "en caractères, le compteur de caractères");
            t.Check(pace.Reached, "120 000 caractères ≥ 80 000 : atteint");
            t.Check(pace.Describe(culture).Contains("atteint"), "…et la phrase le dit");

            book.Book.SizeUnit = "words";
            book.Book.Deadline = "2026-09-11";
            pace = BookPace.Of(book, wordsOf, charsOf, today);
            t.Check(pace.Overdue && pace.DaysLeft == -3, "échéance dépassée de 3 jours");
            t.Check(pace.Describe(culture).StartsWith("Échéance dépassée de 3 jours"), "la phrase de la dépassée");
            t.Equal(60000.0, pace.PerDay, "dépassée : tout le reste");

            book.Book.Deadline = "2026-09-14";
            pace = BookPace.Of(book, wordsOf, charsOf, today);
            t.Equal(0, pace.DaysLeft, "échéance aujourd'hui");
            t.Check(pace.Describe(culture).StartsWith("Échéance aujourd'hui"), "…dit aujourd'hui");
            t.Equal(60000.0, pace.PerDay, "aujourd'hui : tout le reste en un jour");

            book.Book.Deadline = "n'importe quoi";
            book.Book.SizeGoal = 0;
            pace = BookPace.Of(book, wordsOf, charsOf, today);
            t.Check(!pace.HasDeadline && !pace.HasSizeGoal, "une échéance illisible vaut aucune");
            t.Check(BookPace.Of(null, wordsOf, charsOf, today).Done == 0, "livre nul : vide");

            // — Les sprints et la moyenne du journal.
            var journal = new WritingJournal();
            journal.Sprints.Add(new SprintRecord { Date = "2026-09-14 10:00", Minutes = 25, Elapsed = 25, Words = 412, Goal = 500 });
            journal.Sprints.Add(new SprintRecord { Date = "2026-09-14 11:00", Minutes = 0, Elapsed = 40, Words = 900, Goal = 800 });
            journal.Sprints.Add(new SprintRecord { Date = "2026-09-14 12:00", Minutes = 15, Elapsed = 15, Words = 100, Goal = 0 });
            var last = journal.LastSprints(2);
            t.Equal(2, last.Count, "les deux derniers");
            t.Equal("2026-09-14 12:00", last[0].Date, "le plus récent en tête");
            t.Check(!journal.Sprints[0].Reached && journal.Sprints[1].Reached && !journal.Sprints[2].Reached, "objectif atteint seulement quand fixé et dépassé");
            journal.Add(WritingJournal.Today(), 300);
            journal.Add(DateTime.Now.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 100);
            t.Check(Math.Abs(journal.AverageOverDays(4) - 100) < 1e-9, "moyenne sur quatre jours, vides compris");
            t.Equal(0.0, journal.AverageOverDays(0), "moyenne sur zéro jour = 0");
        }
    }
}

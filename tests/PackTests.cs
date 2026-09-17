using System;
using System.Collections.Generic;
using Marabook.History;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C20 — le pack de correctifs du 12/09/2026 : le tri
    /// alphabétique de la bibliothèque de fiches (accents et casse ignorés,
    /// stable), l'image de tuile d'un écrit ou d'un livre (annulable), les
    /// mots d'un nom de fiche pour l'indicateur de dictionnaire.</summary>
    public static class PackTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C20 — pack de correctifs du 12/09");
            SortByTitle(t);
            CardImageUndo(t);
            NameWords(t);
            DisplayDates(t);
        }

        /// <summary>Les dates à l'écran : JJ/MM/AAAA, l'heure gardée quand
        /// elle est enregistrée, une valeur illisible rendue telle quelle.</summary>
        private static void DisplayDates(Harness t)
        {
            t.Equal("12/09/2026 19:30", Marabook.View.Dates.Display("2026-09-12 19:30"), "date et heure");
            t.Equal("12/09/2026 19:30", Marabook.View.Dates.Display("2026-09-12 19:30:45"), "les secondes tombent");
            t.Equal("12/09/2026", Marabook.View.Dates.Display("2026-09-12"), "date seule");
            t.Equal("", Marabook.View.Dates.Display(null), "vide reste vide");
            t.Equal("hier soir", Marabook.View.Dates.Display("hier soir"), "une valeur illisible passe telle quelle");
        }

        private static BinderItem Sheet(string title)
        {
            return new BinderItem { Kind = ItemKind.Sheet, Title = title };
        }

        /// <summary>Ordre à la française : « Élise » entre « Damien » et
        /// « fabien » (accents et casse ignorés), homonymes dans l'ordre
        /// d'arrivée.</summary>
        private static void SortByTitle(Harness t)
        {
            var second = Sheet("Zoé");
            var first = Sheet("Zoé");
            var list = new List<BinderItem>
            {
                Sheet("fabien"), second, Sheet("Élise"), first, Sheet("Damien"), Sheet("éric")
            };
            Marabook.View.SheetLibraryView.SortByTitle(list);
            t.Equal("Damien", list[0].Title, "Damien d'abord");
            t.Equal("Élise", list[1].Title, "É compte comme E");
            t.Equal("éric", list[2].Title, "la casse n'ordonne pas");
            t.Equal("fabien", list[3].Title, "puis fabien");
            t.Check(ReferenceEquals(list[4], second) && ReferenceEquals(list[5], first),
                "les homonymes gardent leur ordre d'arrivée (tri stable)");
            var empty = new List<BinderItem>();
            Marabook.View.SheetLibraryView.SortByTitle(empty);
            t.Equal(0, empty.Count, "une liste vide reste vide");
        }

        /// <summary>L'image de tuile passe par l'historique : pose, retrait,
        /// Ctrl+Z, Ctrl+Y.</summary>
        private static void CardImageUndo(Harness t)
        {
            var project = Project.CreateNew();
            var chapter = project.Category(Project.KeyWritings).Children[0];
            var id = project.AddImage(new byte[] { 1, 2, 3 }, ".png");
            var history = new HistoryManager();
            history.Run(new ChangeImageAction(chapter, id));
            t.Equal(id, chapter.ImageId, "l'image est posée");
            history.Undo();
            t.Check(chapter.ImageId == null, "Ctrl+Z la retire");
            history.Redo();
            t.Equal(id, chapter.ImageId, "Ctrl+Y la repose");
            history.Run(new ChangeImageAction(chapter, null));
            t.Check(chapter.ImageId == null, "« Retirer l'image » vide le champ");
            history.Undo();
            t.Equal(id, chapter.ImageId, "…et s'annule");
            t.Check(project.FindImage(id) != null, "les octets restent dans le magasin jusqu'à la purge");
        }

        /// <summary>Les mots d'un nom de fiche : lettres, apostrophes et traits
        /// d'union ; la ponctuation et les chiffres séparent.</summary>
        private static void NameWords(Harness t)
        {
            var words = Marabook.View.SheetView.NameWords("Keira Varenh");
            t.Equal(2, words.Count, "deux mots");
            t.Equal("Keira", words[0], "le prénom");
            t.Equal("Varenh", words[1], "le nom");
            words = Marabook.View.SheetView.NameWords("Jean-Luc d'Aubigné (le vieux), 3e");
            t.Equal(5, words.Count, "trait d'union et apostrophe gardés, parenthèses et chiffres écartés");
            t.Equal("Jean-Luc", words[0], "le composé reste entier");
            t.Equal("d'Aubigné", words[1], "l'élision reste attachée");
            t.Equal("e", words[4], "le « e » ordinal reste un mot (à la charge du dictionnaire)");
            t.Equal(0, Marabook.View.SheetView.NameWords("  ").Count, "un nom vide n'a pas de mot");
            t.Equal(0, Marabook.View.SheetView.NameWords(null).Count, "null non plus");
        }
    }
}

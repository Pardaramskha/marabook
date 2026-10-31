using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>L'ENCHAÎNEMENT des pages d'un livre (18/10, 1.0.6). Chaque
    /// document ouvre un recto : un chapitre qui finit sur une page impaire
    /// (un recto) laisse un verso blanc derrière lui, et le livre entier qui
    /// finit sur une page impaire a un nombre de pages impair — ce que
    /// l'imprimeur refuse. L'avertissement se débraye par livre
    /// (BookInfo.FlowWarnings) ; « Unifier » ajoute la page blanche à la fin
    /// du chapitre, comme un saut de page après sa dernière ligne.</summary>
    public static class BookFlow
    {
        public sealed class Report
        {
            /// <summary>Les pages du livre, versos blancs d'ouverture compris.</summary>
            public int Total;
            /// <summary>Les chapitres dont la dernière page est impaire, dans l'ordre du livre.</summary>
            public List<BinderItem> OddChapters = new List<BinderItem>();
            /// <summary>Le folio de la dernière page de chaque texte (id → folio).</summary>
            public Dictionary<string, int> LastPage = new Dictionary<string, int>();
            public bool BookEndsOdd { get { return Total % 2 == 1; } }

            /// <summary>Le folio de la dernière page de ce texte s'il est impair, 0 sinon.</summary>
            public int OddEnd(BinderItem text)
            {
                int last;
                if (text == null || !LastPage.TryGetValue(text.Id, out last)) return 0;
                return last % 2 == 1 ? last : 0;
            }
        }

        /// <summary>L'avertissement vaut pour ce livre (option « Contenu &
        /// Fichiers », vraie par défaut) — un livre sans BookInfo encore posé
        /// (jamais ouvert dans sa page) suit le défaut.</summary>
        public static bool Enabled(BinderItem book)
        {
            // Le profil Écrivain·e (1.1.0) cache « Gabarits & Format » et, avec
            // lui, les avertissements d'enchaînement : le réglage du livre reste.
            return book != null && book.Kind == ItemKind.Book && (book.Book == null || book.Book.FlowWarnings)
                && Settings.Profiles.Enabled(Settings.Profiles.BookFormat);
        }

        /// <summary>Les textes du livre dans l'ordre des pages — le Hors-livre
        /// exclu (ni folios, ni pages, ni statistiques).</summary>
        public static void Texts(BinderItem root, List<BinderItem> texts)
        {
            if (root == null) return;
            foreach (var child in root.Children)
            {
                if (child.IsOutOfBook) continue;
                if (child.Kind == ItemKind.Text) texts.Add(child);
                Texts(child, texts);
            }
        }

        public static Report Analyse(BinderItem book, Func<BinderItem, int> pages)
        {
            var texts = new List<BinderItem>();
            Texts(book, texts);
            return Analyse(texts, pages);
        }

        /// <summary>La règle : un compte impair avant un document reçoit un
        /// verso blanc (le document ouvre un recto) ; la dernière page de
        /// chaque document est relevée, impaire = à unifier.</summary>
        public static Report Analyse(IList<BinderItem> texts, Func<BinderItem, int> pages)
        {
            var report = new Report();
            var total = 0;
            foreach (var text in texts)
            {
                var count = pages == null ? 0 : Math.Max(0, pages(text));
                if (count == 0) continue;
                if (total % 2 == 1) total++;
                total += count;
                report.LastPage[text.Id] = total;
                if (total % 2 == 1) report.OddChapters.Add(text);
            }
            report.Total = total;
            return report;
        }

        /// <summary>« Unifier » : un paragraphe vide avec saut de page en fin
        /// de document — une page blanche qui suit le gabarit du document,
        /// exactement comme un saut de page posé après sa dernière ligne.
        /// Rend le paragraphe ajouté (pour l'annulation).</summary>
        public static TextParagraph Unify(TextDocument document)
        {
            var last = document.Paragraphs.Count > 0 ? document.Paragraphs[document.Paragraphs.Count - 1] : null;
            var blank = new TextParagraph { PageBreakBefore = true, StyleId = last != null ? last.StyleId : "body" };
            document.Paragraphs.Add(blank);
            return blank;
        }
    }
}

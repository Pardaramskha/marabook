using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Le GRAPHIQUE des statistiques d'un livre (27/11, 2.0.0) :
    /// onglet Statistiques › « Graphique » trace les signes de chaque texte.
    /// Un texte s'en EXCLUT (BookInfo.ChartExcluded) — une page de titre ou
    /// un entre-chapitre, bien plus courts qu'un chapitre, écraseraient
    /// l'échelle ; l'exclusion ne touche ni la liste, ni les tailles, ni la
    /// publication. Ici, ce qui se teste sans écran : le tri des textes
    /// tracés et écartés, et le plafond « rond » de l'échelle.</summary>
    public static class BookChart
    {
        /// <summary>Les divisions de l'échelle : le plafond se lit en cinq pas égaux.</summary>
        public const int Divisions = 5;

        public static bool IsExcluded(BinderItem book, BinderItem text)
        {
            return book != null && book.Book != null && text != null && book.Book.ChartExcluded.Contains(text.Id);
        }

        /// <summary>Écarte un texte du graphique, ou l'y remet. Vrai si le livre a changé.</summary>
        public static bool SetExcluded(BinderItem book, BinderItem text, bool excluded)
        {
            if (book == null || book.Book == null || text == null) return false;
            var list = book.Book.ChartExcluded;
            if (excluded)
            {
                if (list.Contains(text.Id)) return false;
                list.Add(text.Id);
                return true;
            }
            return list.Remove(text.Id);
        }

        /// <summary>Partage les textes du livre, dans leur ordre : ceux que le
        /// graphique trace, ceux qu'il écarte. Un identifiant écarté dont le
        /// texte a quitté le livre est oublié au passage (vrai en retour).</summary>
        public static bool Split(BinderItem book, IList<BinderItem> texts, List<BinderItem> shown, List<BinderItem> excluded)
        {
            var known = new List<string>();
            foreach (var text in texts)
            {
                if (IsExcluded(book, text)) { excluded.Add(text); known.Add(text.Id); }
                else shown.Add(text);
            }
            if (book == null || book.Book == null || book.Book.ChartExcluded.Count == known.Count) return false;
            book.Book.ChartExcluded.Clear();
            book.Book.ChartExcluded.AddRange(known);
            return true;
        }

        /// <summary>Le plafond de l'échelle : le premier nombre « rond »
        /// (1, 2, 2,5 ou 5 × une puissance de dix) au moins égal au plus grand
        /// texte — ses cinq divisions tombent alors sur des nombres ronds
        /// eux aussi. Jamais sous 10 (un livre vide garde une échelle).</summary>
        public static double Ceiling(double max)
        {
            if (max <= 10 || double.IsNaN(max) || double.IsInfinity(max)) return 10;
            var power = Math.Pow(10, Math.Floor(Math.Log10(max)));
            double[] steps = { 1, 2, 2.5, 5, 10 };
            foreach (var step in steps)
                if (step * power >= max) return step * power;
            return 10 * power;
        }
    }
}

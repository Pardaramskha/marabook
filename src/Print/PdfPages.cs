using System;
using System.Collections.Generic;
using System.Globalization;

namespace Marabook.Print
{
    /// <summary>« 1-3, 7, 10- » : une liste de pages telle qu'on la tape (22/11/2026,
    /// les outils de PDFaah). Les pages se comptent à partir de 1 ; « 4-2 » se
    /// lit comme « 2-4 » ; « 10- » va jusqu'à la dernière ; « -3 » part de la
    /// première.</summary>
    public static class PdfPages
    {
        /// <summary>Les pages (indices à partir de 0, triés, sans doublon) ; null — et « problem » dit
        /// pourquoi — si le texte ne se lit pas ou ne désigne aucune page.</summary>
        public static List<int> Parse(string text, int count, out string problem)
        {
            problem = null;
            var chosen = new SortedSet<int>();
            var parts = (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { problem = "Indiquez des pages : par exemple « 1-3, 7 »."; return null; }
            foreach (var part in parts)
            {
                var piece = part.Replace('–', '-').Replace('—', '-').Trim();
                int first, last;
                var dash = piece.IndexOf('-');
                if (dash < 0)
                {
                    if (!int.TryParse(piece, NumberStyles.None, CultureInfo.InvariantCulture, out first)) { problem = "« " + part + " » n'est pas un numéro de page."; return null; }
                    last = first;
                }
                else
                {
                    var left = piece.Substring(0, dash).Trim();
                    var right = piece.Substring(dash + 1).Trim();
                    first = 1;
                    last = count;
                    if (left.Length > 0 && !int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out first)) { problem = "« " + part + " » n'est pas une suite de pages."; return null; }
                    if (right.Length > 0 && !int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out last)) { problem = "« " + part + " » n'est pas une suite de pages."; return null; }
                    if (first > last) { var swap = first; first = last; last = swap; }
                }
                if (first < 1 || last > count) { problem = "Ce PDF a " + (count == 1 ? "1 page" : count + " pages") + " : « " + part + " » en sort."; return null; }
                for (var page = first; page <= last; page++) chosen.Add(page - 1);
            }
            if (chosen.Count == 0) { problem = "Aucune page n'est désignée."; return null; }
            return new List<int>(chosen);
        }

        /// <summary>« 1-3, 7 » : la liste, telle qu'on la dirait.</summary>
        public static string Describe(IList<int> pages)
        {
            var parts = new List<string>();
            var i = 0;
            while (i < pages.Count)
            {
                var j = i;
                while (j + 1 < pages.Count && pages[j + 1] == pages[j] + 1) j++;
                parts.Add(j == i ? (pages[i] + 1).ToString(CultureInfo.InvariantCulture) : (pages[i] + 1).ToString(CultureInfo.InvariantCulture) + "-" + (pages[j] + 1).ToString(CultureInfo.InvariantCulture));
                i = j + 1;
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}

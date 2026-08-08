using System;
using System.Collections.Generic;

namespace UniversSale.Model
{
    /// <summary>Recherche sur le PIVOT (batch 26, lot B.3) : indépendante de
    /// la surface d'édition — la vue Composition sélectionne les résultats
    /// sans jamais quitter le composé (Ctrl+F forçait la sortie), et la
    /// recherche projet du batch 27 s'appuiera dessus. Offsets plats de
    /// PivotEdit (U+FFFC pour les éléments, qui ne matche jamais un texte
    /// cherché). Pas de regex ici : périmètre du batch 27.</summary>
    public static class PivotSearch
    {
        public class Match
        {
            public int ParagraphIndex;
            public int Start;
            public int Length;
        }

        /// <summary>Toutes les occurrences, dans l'ordre du texte.</summary>
        public static List<Match> FindAll(TextDocument document, string query,
            bool matchCase, bool wholeWord)
        {
            var matches = new List<Match>();
            if (document == null || string.IsNullOrEmpty(query)) return matches;
            // ORDINAL, jamais culturel : la comparaison de culture ignore les
            // caractères « sans poids » (U+FFFC des éléments, entre autres)
            // et rend des empans d'une AUTRE longueur que la requête — des
            // offsets faux. Attrapé par C6.
            var comparison = matchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var text = PivotEdit.FlatText(document.Paragraphs[p]);
                // « Mot entier » parle la langue du TOKENISEUR UNIQUE
                // (batch 27) : le match doit coïncider avec un token entier
                // ou avec son CŒUR — « père » ne trouve plus « grand-père »
                // (c'était un bug : IsLetterOrDigit ignorait le trait
                // d'union), « dit » trouve encore le « dit » de « dit-il »,
                // « il » n'y trouve rien.
                List<Correction.Token> tokens = null;
                if (wholeWord)
                    tokens = Correction.FrenchTokenizer.Tokenize(text);
                var index = 0;
                while (index <= text.Length - query.Length
                    && (index = text.IndexOf(query, index, comparison)) >= 0)
                {
                    if (!wholeWord || IsWholeToken(tokens, index, query.Length))
                        matches.Add(new Match
                        {
                            ParagraphIndex = p,
                            Start = index,
                            Length = query.Length
                        });
                    index += 1;
                }
            }
            return matches;
        }

        private static bool IsWholeToken(List<Correction.Token> tokens,
            int start, int length)
        {
            var end = start + length;
            foreach (var token in tokens)
            {
                if (token.Start > start) break;
                if (token.Start == start && token.Start + token.Length == end)
                    return true; // le token entier
                if (token.CoreStart == start && token.CoreEnd == end)
                    return true; // son cœur (« dit » dans « dit-il »)
            }
            return false;
        }
    }
}

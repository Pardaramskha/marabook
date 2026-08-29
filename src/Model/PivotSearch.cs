using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UniversSale.Model
{
    /// <summary>Une expression régulière trop coûteuse (explosion
    /// combinatoire) : le délai par texte est dépassé. La recherche projet
    /// l'attrape, garde ce qu'elle a trouvé et le dit.</summary>
    public class SearchTimeoutException : Exception
    {
        public SearchTimeoutException(string message) : base(message) { }
    }

    /// <summary>La carte de correspondance entre un texte PLIÉ (casse,
    /// accents, œ/æ, apostrophes — FrenchTokenizer.Fold, la seule définition)
    /// et les offsets du texte d'origine (batch 37, sur le principe de
    /// l'OffsetMapper du batch 29) : le texte d'origine est découpé en
    /// SÉQUENCES stables (une base + ses marques combinantes, paires de
    /// substitution entières, U+FFFC seul), chaque séquence est pliée seule,
    /// et chaque unité du texte plié connaît le début et la fin de sa
    /// séquence d'origine. Un empan plié revient ainsi toujours sur des
    /// frontières de caractères complets, marques comprises ; « œ » → « oe »
    /// change la longueur sans perdre personne.</summary>
    public sealed class FoldMap
    {
        public string Folded = "";
        // Par unité pliée : le DÉBUT de sa séquence d'origine (un seul entier
        // par unité — la fin et les bornes pliées de la séquence se déduisent
        // des voisins, les séquences étant contiguës et courtes). Null = carte
        // identité (texte ASCII déjà dans la casse voulue : rien à plier).
        private int[] _origin;
        private int _originalLength;

        public bool IsIdentity { get { return _origin == null; } }

        public static FoldMap Build(string original, bool lowerCase)
        {
            var map = new FoldMap();
            original = original ?? "";
            map._originalLength = original.Length;
            if (IsPlain(original, lowerCase))
            {
                map.Folded = original;
                return map;
            }
            var sb = new StringBuilder(original.Length + 8);
            var origin = new int[original.Length + 8];
            var count = 0;
            var i = 0;
            while (i < original.Length)
            {
                var sequenceStart = i;
                var c = original[i];
                i += char.IsHighSurrogate(c) && i + 1 < original.Length && char.IsLowSurrogate(original[i + 1]) ? 2 : 1;
                while (i < original.Length && IsMark(original[i])) i++;
                var sequenceEnd = i;
                if (sequenceEnd - sequenceStart == 1 && c < 0x80)
                {
                    // Voie rapide ASCII : aucune allocation (le gros d'un texte).
                    sb.Append(lowerCase && c >= 'A' && c <= 'Z' ? (char)(c + 32) : c);
                }
                else
                    sb.Append(Correction.FrenchTokenizer.Fold(
                        original.Substring(sequenceStart, sequenceEnd - sequenceStart), lowerCase));
                if (sb.Length > origin.Length)
                {
                    var grown = new int[Math.Max(sb.Length, origin.Length * 2)];
                    Array.Copy(origin, grown, count);
                    origin = grown;
                }
                while (count < sb.Length) origin[count++] = sequenceStart;
            }
            map.Folded = sb.ToString();
            if (count != origin.Length)
            {
                var trimmed = new int[count];
                Array.Copy(origin, trimmed, count);
                origin = trimmed;
            }
            map._origin = origin;
            return map;
        }

        /// <summary>Vrai si le texte est de l'ASCII pur déjà dans la casse
        /// voulue : le pli serait l'identité.</summary>
        private static bool IsPlain(string text, bool lowerCase)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c >= 0x80) return false;
                if (lowerCase && c >= 'A' && c <= 'Z') return false;
            }
            return true;
        }

        private static bool IsMark(char c)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpacingCombiningMark
                || category == UnicodeCategory.EnclosingMark;
        }

        private int SequenceFoldedStart(int index)
        {
            var start = _origin[index];
            while (index > 0 && _origin[index - 1] == start) index--;
            return index;
        }

        private int SequenceFoldedEnd(int index)
        {
            var start = _origin[index];
            while (index < _origin.Length && _origin[index] == start) index++;
            return index;
        }

        /// <summary>Reporte un empan du texte plié vers le texte d'origine.</summary>
        public void MapRange(int start, int length, out int originStart, out int originLength)
        {
            if (_origin == null)
            {
                originStart = Math.Max(0, Math.Min(start, _originalLength));
                originLength = Math.Max(0, Math.Min(length, _originalLength - originStart));
                return;
            }
            if (length <= 0 || start < 0 || start + length > Folded.Length)
            {
                originStart = start >= 0 && start < Folded.Length ? _origin[start] : _originalLength;
                originLength = 0;
                return;
            }
            originStart = _origin[start];
            var lastEnd = SequenceFoldedEnd(start + length - 1);
            var originEnd = lastEnd < _origin.Length ? _origin[lastEnd] : _originalLength;
            originLength = originEnd - originStart;
        }

        /// <summary>Vrai si l'empan plié ne COUPE aucune séquence : il commence
        /// sur la première unité d'une séquence et finit sur la dernière d'une
        /// autre. « o » sur « œ » (plié « oe ») n'est pas exact : le remplacer
        /// détruirait la ligature ; « oe » sur « œ » l'est.</summary>
        public bool IsExact(int start, int length)
        {
            if (length <= 0 || start < 0 || start + length > Folded.Length) return false;
            if (_origin == null) return true;
            return SequenceFoldedStart(start) == start && SequenceFoldedEnd(start + length - 1) == start + length;
        }
    }

    /// <summary>Une requête de recherche compilée une fois (batch 37) :
    /// littérale ou regex, casse, mot entier (sans objet en regex),
    /// insensible aux accents (FoldMap). Une regex invalide ne lève pas :
    /// Error le dit. Le matchTimeout (250 ms par texte) borne l'explosion
    /// combinatoire : SearchTimeoutException, que l'appelant attrape.
    /// U+FFFC (image, filet, note) est soumis à l'expression comme « \n » :
    /// « . » et « \w » ne le traversent pas, « \b » s'y arrête, et tout empan
    /// qui en contiendrait un après reprojection est rejeté (garde-fou).
    /// Les empans rendus ne se recouvrent pas (un remplacement s'y appuie) ;
    /// le Ctrl+F historique garde ses recouvrements (FindAll, C6).</summary>
    public sealed class SearchQuery
    {
        public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        public readonly string Pattern;
        public readonly bool MatchCase, WholeWord, IgnoreAccents, UseRegex;
        public readonly string Error; // null = requête valide
        private readonly Regex _regex;
        private readonly string _needle;
        private readonly StringComparison _comparison;

        public struct Span
        {
            public int Start, Length;
            public bool Exact; // faux = l'empan coupe une séquence pliée (ligature) : pas remplaçable
        }

        private SearchQuery(string pattern, bool matchCase, bool wholeWord, bool ignoreAccents, bool useRegex,
            Regex regex, string needle, string error)
        {
            Pattern = pattern;
            MatchCase = matchCase;
            WholeWord = wholeWord && !useRegex;
            IgnoreAccents = ignoreAccents;
            UseRegex = useRegex;
            _regex = regex;
            _needle = needle;
            Error = error;
            _comparison = matchCase || ignoreAccents ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        }

        public bool IsEmpty { get { return Pattern.Length == 0; } }
        public bool IsValid { get { return Error == null && Pattern.Length > 0; } }

        private Regex _anchored, _anchoredFolded;

        /// <summary>Le texte de remplacement d'UN empan : littéral tel quel ;
        /// en regex, les groupes de capture ($1, ${nom}) sont résolus en
        /// rejouant le motif sur l'empan D'ORIGINE (ancré) — et, sans accents,
        /// sur l'empan plié si l'original ne se laisse pas relire (les groupes
        /// rendent alors du texte plié). Sans correspondance : le remplacement
        /// tel quel.</summary>
        public string ReplacementFor(string matched, string replacement)
        {
            replacement = replacement ?? "";
            if (!UseRegex || _regex == null || matched == null) return replacement;
            var options = RegexOptions.CultureInvariant | (MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            try
            {
                if (_anchored == null) _anchored = new Regex("^(?:" + Pattern + ")$", options, RegexTimeout);
                var match = _anchored.Match(matched);
                if (match.Success) return match.Result(replacement);
                if (IgnoreAccents)
                {
                    if (_anchoredFolded == null)
                        _anchoredFolded = new Regex("^(?:" + Correction.FrenchTokenizer.Fold(Pattern, false) + ")$", options, RegexTimeout);
                    var folded = _anchoredFolded.Match(Correction.FrenchTokenizer.Fold(matched, !MatchCase));
                    if (folded.Success) return folded.Result(replacement);
                }
            }
            catch (ArgumentException) { }
            catch (RegexMatchTimeoutException) { }
            return replacement;
        }

        public static SearchQuery Create(string pattern, bool matchCase, bool wholeWord, bool ignoreAccents, bool useRegex)
        {
            pattern = pattern ?? "";
            Regex regex = null;
            string error = null;
            var needle = pattern;
            if (useRegex && pattern.Length > 0)
            {
                // Le motif est plié SANS la casse (« \W » ne doit pas devenir
                // « \w ») ; la casse passe par RegexOptions.
                var source = ignoreAccents ? Correction.FrenchTokenizer.Fold(pattern, false) : pattern;
                var options = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
                try { regex = new Regex(source, options, RegexTimeout); }
                catch (ArgumentException e) { error = "Expression invalide : " + e.Message; }
            }
            else if (ignoreAccents) needle = Correction.FrenchTokenizer.Fold(pattern, !matchCase);
            return new SearchQuery(pattern, matchCase, wholeWord, ignoreAccents, useRegex, regex, needle, error);
        }

        /// <summary>Toutes les occurrences dans un texte plat, sans recouvrement,
        /// offsets dans le texte D'ORIGINE.</summary>
        public List<Span> FindInText(string text)
        {
            return FindInText(text, false);
        }

        public List<Span> FindInText(string text, bool overlapping)
        {
            if (!IsValid || string.IsNullOrEmpty(text)) return new List<Span>();
            return FindInText(text, IgnoreAccents ? FoldMap.Build(text, !MatchCase) : null, overlapping);
        }

        /// <summary>Un champ cherchable : sa carte de pli est mise en cache
        /// dans le champ (une frappe n'en rebâtit aucune).</summary>
        public List<Span> FindInField(SearchField field)
        {
            if (!IsValid || field == null || string.IsNullOrEmpty(field.Text)) return new List<Span>();
            return FindInText(field.Text, IgnoreAccents ? field.FoldMapFor(!MatchCase) : null, false);
        }

        private List<Span> FindInText(string text, FoldMap map, bool overlapping)
        {
            var spans = new List<Span>();
            var subject = map != null ? map.Folded : text;
            if (subject.IndexOf('￼') >= 0) subject = subject.Replace('￼', '\n');
            List<Correction.Token> tokens = null;
            if (WholeWord) tokens = Correction.FrenchTokenizer.Tokenize(text);
            var index = 0;
            while (index <= subject.Length)
            {
                int found, length;
                if (_regex != null)
                {
                    Match match;
                    try { match = _regex.Match(subject, index); }
                    catch (RegexMatchTimeoutException)
                    {
                        throw new SearchTimeoutException("Expression trop coûteuse : plus de "
                            + RegexTimeout.TotalMilliseconds + " ms sur un seul texte.");
                    }
                    if (!match.Success) break;
                    if (match.Length == 0) { index = match.Index + 1; continue; } // jamais d'empan vide
                    found = match.Index;
                    length = match.Length;
                }
                else
                {
                    if (_needle.Length == 0 || index > subject.Length - _needle.Length) break;
                    found = subject.IndexOf(_needle, index, _comparison);
                    if (found < 0) break;
                    length = _needle.Length;
                }
                int originStart, originLength;
                var exact = true;
                if (map != null)
                {
                    map.MapRange(found, length, out originStart, out originLength);
                    exact = map.IsExact(found, length);
                }
                else
                {
                    originStart = found;
                    originLength = length;
                }
                var admitted = originLength > 0
                    && text.IndexOf('￼', originStart, originLength) < 0
                    && (tokens == null || PivotSearch.IsWholeToken(tokens, originStart, originLength));
                if (admitted) spans.Add(new Span { Start = originStart, Length = originLength, Exact = exact });
                index = overlapping || !admitted ? found + 1 : found + length;
            }
            return spans;
        }
    }

    /// <summary>Recherche sur le PIVOT (batch 26, lot B.3 ; regex, accents et
    /// requête compilée au batch 37) : indépendante de la surface d'édition —
    /// la vue Composition sélectionne les résultats sans jamais quitter le
    /// composé. Offsets plats de PivotEdit (U+FFFC pour les éléments, qui ne
    /// matche jamais un texte cherché).</summary>
    public static class PivotSearch
    {
        public class Match
        {
            public int ParagraphIndex;
            public int Start;
            public int Length;
            public bool Exact = true;
        }

        /// <summary>Toutes les occurrences littérales, dans l'ordre du texte —
        /// l'API du Ctrl+F du document (recouvrements d'un caractère, C6).</summary>
        public static List<Match> FindAll(TextDocument document, string query,
            bool matchCase, bool wholeWord)
        {
            // ORDINAL, jamais culturel : la comparaison de culture ignore les
            // caractères « sans poids » (U+FFFC des éléments, entre autres)
            // et rend des empans d'une AUTRE longueur que la requête — des
            // offsets faux. Attrapé par C6. « Mot entier » parle la langue du
            // TOKENISEUR UNIQUE (batch 27) : le match doit coïncider avec un
            // token entier ou avec son CŒUR — « père » ne trouve plus
            // « grand-père », « dit » trouve encore le « dit » de « dit-il ».
            return FindAll(document, SearchQuery.Create(query, matchCase, wholeWord, false, false), true);
        }

        /// <summary>Toutes les occurrences d'une requête compilée, sans
        /// recouvrement (recherche projet, remplacement).</summary>
        public static List<Match> FindAll(TextDocument document, SearchQuery query)
        {
            return FindAll(document, query, false);
        }

        private static List<Match> FindAll(TextDocument document, SearchQuery query, bool overlapping)
        {
            var matches = new List<Match>();
            if (document == null || query == null || !query.IsValid) return matches;
            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var text = PivotEdit.FlatText(document.Paragraphs[p]);
                foreach (var span in query.FindInText(text, overlapping))
                    matches.Add(new Match
                    {
                        ParagraphIndex = p,
                        Start = span.Start,
                        Length = span.Length,
                        Exact = span.Exact
                    });
            }
            return matches;
        }

        internal static bool IsWholeToken(List<Correction.Token> tokens,
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

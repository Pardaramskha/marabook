using System;
using System.Collections.Generic;
using Marabook.Correction;

namespace Marabook.Model
{
    /// <summary>Un nom propre relevé dans un texte importé (b49) : le nom
    /// tel qu'écrit, son nombre d'occurrences, un bout de phrase.</summary>
    public class NameCandidate
    {
        public string Name = "";
        public int Count;
        public string Context = "";
    }

    /// <summary>L'EXTRACTION DE PERSONNAGES à l'import (b49) — sans IA : un
    /// mot à majuscule qui n'ouvre pas une phrase, revu assez souvent, qui
    /// n'est ni un mot outil, ni un jour, un mois ou une civilité, ni le
    /// nom d'une fiche existante. Deux majuscules qui se suivent (« Jean
    /// Valjean », « Anne de Bretagne ») font un seul nom, et leurs parts ne
    /// comptent plus pour elles-mêmes. Pur, testable en console (C31).</summary>
    public static class NameExtractor
    {
        public const int DefaultMinCount = 3;

        // Majuscules ordinaires du français qui ne nomment personne.
        private static readonly HashSet<string> Ordinary = new HashSet<string>(StringComparer.Ordinal)
        {
            "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche",
            "janvier", "fevrier", "mars", "avril", "mai", "juin", "juillet", "aout",
            "septembre", "octobre", "novembre", "decembre",
            "monsieur", "madame", "mademoiselle", "messieurs", "mesdames", "mme", "mlle", "mm",
            "dieu", "seigneur", "dame", "sire", "majeste", "altesse", "excellence", "monseigneur",
            "oui", "non", "ah", "oh", "eh", "he", "ho", "hein", "bon", "bien", "mais", "et", "puis",
            "alors", "enfin", "tout", "rien", "merci", "pardon", "adieu", "bonjour", "bonsoir",
            "salut", "nord", "sud", "est", "ouest", "noel", "paques", "chapitre", "partie",
            "prologue", "epilogue", "fin", "livre", "tome", "acte", "scene", "je", "tu", "il",
            "elle", "nous", "vous", "ils", "elles", "on", "ce", "ca", "cela", "voila", "voici",
            "quand", "comment", "pourquoi", "ou", "que", "qui", "quoi", "dont", "si", "car",
            "or", "ni", "ne", "pas", "plus", "moins", "tres", "trop", "peu", "beaucoup",
            "internet", "web", "ok"
        };

        // Les particules admises ENTRE deux majuscules d'un même nom.
        private static readonly HashSet<string> Particles = new HashSet<string>(StringComparer.Ordinal)
        {
            "de", "du", "des", "le", "la", "les", "van", "von", "di", "da", "del", "della", "der", "den", "d"
        };

        private sealed class Hit
        {
            public string Name;
            public bool SentenceStart;
            public int Start, End;
        }

        /// <summary>Les noms candidats d'un texte, les plus fréquents d'abord.
        /// knownNames : les noms déjà portés par des fiches (exclus, parts
        /// comprises).</summary>
        public static List<NameCandidate> Extract(string text, IEnumerable<string> knownNames, int minCount = DefaultMinCount)
        {
            var result = new List<NameCandidate>();
            if (string.IsNullOrEmpty(text)) return result;
            if (minCount < 1) minCount = 1;
            var known = new HashSet<string>(StringComparer.Ordinal);
            if (knownNames != null)
                foreach (var name in knownNames)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    known.Add(Key(name));
                    foreach (var part in name.Split(' ', '\t', '-'))
                        if (part.Trim().Length > 1) known.Add(Key(part.Trim()));
                }

            var tokens = FrenchTokenizer.Tokenize(text);
            var hits = new List<Hit>();
            var i = 0;
            while (i < tokens.Count)
            {
                var token = tokens[i];
                if (!IsCapital(token)) { i++; continue; }
                var start = token.CoreStart;
                var end = token.CoreEnd;
                var name = token.CoreSurface;
                var sentenceStart = IsSentenceStart(text, token.Start);
                // Prolonge le nom : majuscule (particule) majuscule…
                var j = i + 1;
                while (j < tokens.Count)
                {
                    var next = tokens[j];
                    if (!Adjacent(text, end, next.Start)) break;
                    if (IsCapital(next))
                    {
                        name += text.Substring(end, next.CoreStart - end) + next.CoreSurface;
                        end = next.CoreEnd;
                        j++;
                        continue;
                    }
                    // Une particule, si une majuscule suit.
                    if (j + 1 < tokens.Count && Particles.Contains(FrenchTokenizer.Fold(next.CoreSurface))
                        && next.Shape == CaseShape.Lower && IsCapital(tokens[j + 1])
                        && Adjacent(text, next.CoreEnd, tokens[j + 1].Start))
                    {
                        var after = tokens[j + 1];
                        name += text.Substring(end, after.CoreStart - end) + after.CoreSurface;
                        end = after.CoreEnd;
                        j += 2;
                        continue;
                    }
                    break;
                }
                hits.Add(new Hit { Name = Normalize(name), SentenceStart = sentenceStart, Start = start, End = end });
                i = j;
            }

            // Comptes par nom : total, et hors début de phrase.
            var total = new Dictionary<string, int>(StringComparer.Ordinal);
            var inner = new Dictionary<string, int>(StringComparer.Ordinal);
            var first = new Dictionary<string, Hit>(StringComparer.Ordinal);
            foreach (var hit in hits)
            {
                // « Monsieur Dupont » → « Dupont » ; « Lundi », « Oui » → rien.
                var trimmed = TrimOrdinary(hit.Name);
                if (trimmed == null) continue;
                // « Puis Anne » en tête de phrase : c'est « Puis » qui ouvre la
                // phrase, pas Anne — le nom raccourci n'est plus au début.
                if (trimmed.Length < hit.Name.Length) hit.SentenceStart = false;
                hit.Name = trimmed;
                var key = Key(trimmed);
                if (known.Contains(key)) continue;
                Bump(total, key);
                if (!hit.SentenceStart)
                {
                    Bump(inner, key);
                    if (!first.ContainsKey(key)) first[key] = hit;
                }
                else if (!first.ContainsKey(key)) first[key] = hit;
            }
            foreach (var pair in total)
            {
                int innerCount;
                inner.TryGetValue(pair.Key, out innerCount);
                if (pair.Value < minCount || innerCount < Math.Min(2, minCount)) continue;
                var hit = first[pair.Key];
                result.Add(new NameCandidate { Name = hit.Name, Count = pair.Value, Context = ContextOf(text, hit) });
            }
            result.Sort(delegate(NameCandidate a, NameCandidate b)
            {
                var byCount = b.Count.CompareTo(a.Count);
                return byCount != 0 ? byCount : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });
            return result;
        }

        private static void Bump(Dictionary<string, int> counts, string key)
        {
            int n;
            counts.TryGetValue(key, out n);
            counts[key] = n + 1;
        }

        /// <summary>La clé de regroupement : accents pliés, casse ignorée,
        /// espaces normalisés.</summary>
        public static string Key(string name)
        {
            var parts = (name ?? "").Split(new[] { ' ', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var k = 0; k < parts.Length; k++) parts[k] = FrenchTokenizer.Fold(parts[k]);
            return string.Join(" ", parts);
        }

        private static string Normalize(string name)
        {
            return string.Join(" ", (name ?? "").Split(new[] { ' ', '\t', ' ', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool IsOrdinaryWord(string part)
        {
            var folded = FrenchTokenizer.Fold(part);
            return Ordinary.Contains(folded) || FrenchStopWords.Contains(folded);
        }

        private static bool IsParticle(string part)
        {
            return Particles.Contains(FrenchTokenizer.Fold(part));
        }

        /// <summary>Ôte en tête les mots ordinaires (civilités, « Le »,
        /// « Oui ») et les particules orphelines ; un mot ordinaire au cœur
        /// du nom disqualifie tout. Null = rien de nommable.</summary>
        public static string TrimOrdinary(string name)
        {
            var parts = new List<string>((name ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            while (parts.Count > 0 && (IsParticle(parts[0]) || IsOrdinaryWord(parts[0]))) parts.RemoveAt(0);
            while (parts.Count > 0 && IsParticle(parts[parts.Count - 1])) parts.RemoveAt(parts.Count - 1);
            foreach (var part in parts)
                if (!IsParticle(part) && IsOrdinaryWord(part)) return null;
            return parts.Count == 0 ? null : string.Join(" ", parts.ToArray());
        }

        private static bool IsCapital(Token token)
        {
            return token.Kind == TokenKind.Word && token.Shape == CaseShape.Capitalized
                && token.CoreLength >= 2;
        }

        /// <summary>Entre deux parts d'un nom : des blancs seulement (pas de
        /// ponctuation, pas de saut de ligne).</summary>
        private static bool Adjacent(string text, int from, int to)
        {
            if (to < from || to - from > 3) return false;
            for (var k = from; k < to; k++)
                if (text[k] != ' ' && text[k] != ' ' && text[k] != '\t') return false;
            return true;
        }

        /// <summary>Le mot ouvre-t-il une phrase ? On remonte les blancs,
        /// guillemets, tirets et parenthèses ouvrantes ; en tête de texte ou
        /// de ligne, ou après . ! ? … : c'est un début de phrase — la
        /// majuscule n'y prouve rien.</summary>
        public static bool IsSentenceStart(string text, int position)
        {
            var k = position - 1;
            while (k >= 0)
            {
                var c = text[k];
                if (c == ' ' || c == ' ' || c == '\t' || c == '«' || c == '"' || c == '“' || c == '‘'
                    || c == '\'' || c == '(' || c == '—' || c == '–' || c == '-' || c == '*' || c == '_')
                { k--; continue; }
                if (c == '\n' || c == '\r') return true;
                return c == '.' || c == '!' || c == '?' || c == '…' || c == ':' || c == '»' || c == '”';
            }
            return true;
        }

        private static string ContextOf(string text, Hit hit)
        {
            var from = Math.Max(0, hit.Start - 40);
            var to = Math.Min(text.Length, hit.End + 40);
            // Aux frontières de mots, et jamais au-delà de la ligne.
            while (from > 0 && from < hit.Start && !char.IsWhiteSpace(text[from - 1])) from--;
            while (to < text.Length && to > hit.End && !char.IsWhiteSpace(text[to])) to++;
            var lineStart = text.LastIndexOf('\n', Math.Max(0, hit.Start - 1));
            if (lineStart >= from) from = lineStart + 1;
            var lineEnd = text.IndexOf('\n', hit.End);
            if (lineEnd >= 0 && lineEnd < to) to = lineEnd;
            var slice = text.Substring(from, Math.Max(0, to - from)).Trim();
            if (from > 0 && from > (lineStart + 1)) slice = "…" + slice;
            if (to < text.Length && (lineEnd < 0 || to < lineEnd)) slice += "…";
            return slice;
        }
    }
}

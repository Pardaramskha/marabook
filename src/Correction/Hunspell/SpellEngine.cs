using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversSale.Correction.Hunspell
{
    /// <summary>Le moteur Hunspell maison (batch 27, lot C) — JAMAIS
    /// d'expansion du lexique : les 87 105 radicaux sont indexés tels quels
    /// et les affixes se retirent À LA VOLÉE à la recherche (essai des
    /// suffixes/préfixes applicables, radical vérifié, drapeaux contrôlés).
    /// Gère FLAG long, NEEDAFFIX, FORBIDDENWORD, KEEPCASE, NOSUGGEST,
    /// CIRCUMFIX (déclaré mais inerte dans le .aff 7.7), FULLSTRIP, ICONV,
    /// les règles de casse (initiale capitale acceptée si la minuscule
    /// l'est ; l'inverse jamais pour un nom propre). Ni COMPOUND ni PHONE
    /// (doctrine). Aucune dépendance WPF.</summary>
    public sealed class SpellEngine
    {
        private readonly AffixFile _affix;
        // radical → jeux de drapeaux (une liste : les homonymes existent).
        private readonly Dictionary<string, List<string[]>> _stems;
        // index des entrées d'affixes par APPEND (la clé de la désaffixation).
        private readonly Dictionary<string, List<AffixHit>> _suffixByAppend
            = new Dictionary<string, List<AffixHit>>();
        private readonly Dictionary<string, List<AffixHit>> _prefixByAppend
            = new Dictionary<string, List<AffixHit>>();
        private readonly List<int> _suffixLengths = new List<int>();
        private readonly List<int> _prefixLengths = new List<int>();

        private sealed class AffixHit
        {
            public AffixRule Rule;
            public AffixEntry Entry;
        }

        public int StemCount { get { return _stems.Count; } }
        public AffixFile Affix { get { return _affix; } }

        // Voisinage clavier (KEY, batch 29 0.6) : deux caractères adjacents
        // sur une même rangée déclarée valent une substitution privilégiée —
        // « parlee » tapé « parlwe » vient d'un doigt qui a glissé.
        private readonly Dictionary<char, string> _keyNeighbors
            = new Dictionary<char, string>();

        private SpellEngine(AffixFile affix, Dictionary<string, List<string[]>> stems)
        {
            _affix = affix;
            _stems = stems;
            IndexAffixes(_affix.Suffixes, _suffixByAppend, _suffixLengths);
            IndexAffixes(_affix.Prefixes, _prefixByAppend, _prefixLengths);
            foreach (var row in _affix.KeyRows.Split('|'))
                for (var i = 0; i < row.Length; i++)
                {
                    if (i > 0) AddKeyNeighbor(row[i], row[i - 1]);
                    if (i < row.Length - 1) AddKeyNeighbor(row[i], row[i + 1]);
                }
        }

        private void AddKeyNeighbor(char key, char neighbor)
        {
            string current;
            if (!_keyNeighbors.TryGetValue(key, out current))
                _keyNeighbors[key] = neighbor.ToString();
            else if (current.IndexOf(neighbor) < 0)
                _keyNeighbors[key] = current + neighbor;
        }

        public static SpellEngine Load(string affPath, string dicPath)
        {
            var affix = AffixFile.Load(affPath);
            var stems = new Dictionary<string, List<string[]>>(
                100000, StringComparer.Ordinal);
            var lines = File.ReadAllLines(dicPath, Encoding.UTF8);
            // Première ligne = compte annoncé ; on la lit sans s'y fier.
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0) continue;
                // Champs morphologiques éventuels après tab/espace : ignorés.
                var cut = line.IndexOf('\t');
                if (cut >= 0) line = line.Substring(0, cut);
                var slash = line.IndexOf('/');
                string word;
                string[] flags;
                if (slash >= 0)
                {
                    word = line.Substring(0, slash).Replace("\\/", "/");
                    flags = affix.ParseFlags(line.Substring(slash + 1));
                }
                else
                {
                    word = line.Replace("\\/", "/");
                    flags = AffixEntry.None;
                }
                if (word.Length == 0) continue;
                List<string[]> sets;
                if (!stems.TryGetValue(word, out sets))
                {
                    sets = new List<string[]>(1);
                    stems[word] = sets;
                }
                sets.Add(flags);
            }
            return new SpellEngine(affix, stems);
        }

        private static void IndexAffixes(Dictionary<string, AffixRule> rules,
            Dictionary<string, List<AffixHit>> byAppend, List<int> lengths)
        {
            var seen = new HashSet<int>();
            foreach (var rule in rules.Values)
                foreach (var entry in rule.Entries)
                {
                    List<AffixHit> list;
                    if (!byAppend.TryGetValue(entry.Append, out list))
                    {
                        list = new List<AffixHit>();
                        byAppend[entry.Append] = list;
                    }
                    list.Add(new AffixHit { Rule = rule, Entry = entry });
                    seen.Add(entry.Append.Length);
                }
            foreach (var length in seen) lengths.Add(length);
            lengths.Sort();
        }

        // ============================================================ recherche

        private enum Verdict { None, Accept, Forbidden }

        /// <summary>Le mot est-il correct ? Applique ICONV puis les règles de
        /// casse : la forme exacte d'abord, puis la minuscule pour une
        /// initiale capitale (début de phrase), puis minuscule ET Capitalisée
        /// pour un tout-capitales — jamais l'inverse (un nom propre en
        /// minuscules reste faux).</summary>
        public bool Accepts(string word)
        {
            bool noSuggest;
            return AcceptsDetail(word, out noSuggest);
        }

        /// <summary>Comme Accepts, en disant AUSSI si toutes les voies
        /// passent par NOSUGGEST (formes valides jamais proposées).</summary>
        public bool AcceptsDetail(string word, out bool noSuggestOnly)
        {
            noSuggestOnly = false;
            if (string.IsNullOrEmpty(word)) return false;
            var w = ApplyIconv(word);
            bool ns;
            var exact = CheckForm(w, false, out ns);
            if (exact == Verdict.Forbidden) return false;
            if (exact == Verdict.Accept) { noSuggestOnly = ns; return true; }

            var shape = ShapeOf(w);
            if (shape == Shape.Capitalized)
            {
                var lower = Lower(w);
                var folded = CheckForm(lower, true, out ns);
                if (folded == Verdict.Forbidden) return false;
                if (folded == Verdict.Accept) { noSuggestOnly = ns; return true; }
            }
            else if (shape == Shape.AllCaps)
            {
                // batch 29, 0.7 : Forbidden se teste ici aussi — un mot
                // interdit tapé en capitales était accepté (la branche
                // Capitalized, elle, le refusait déjà).
                var lower = Lower(w);
                var folded = CheckForm(lower, true, out ns);
                if (folded == Verdict.Forbidden) return false;
                if (folded == Verdict.Accept) { noSuggestOnly = ns; return true; }
                var capital = Capitalize(lower);
                folded = CheckForm(capital, true, out ns);
                if (folded == Verdict.Forbidden) return false;
                if (folded == Verdict.Accept) { noSuggestOnly = ns; return true; }
            }
            return false;
        }

        /// <summary>Une forme donnée, dans une casse donnée. caseChanged :
        /// la forme diffère de la surface tapée — les entrées KEEPCASE ne
        /// comptent plus (Po/|| n'autorise ni PO ni po).</summary>
        private Verdict CheckForm(string form, bool caseChanged, out bool noSuggestOnly)
        {
            noSuggestOnly = false;
            var sawNoSuggest = false;
            var sawPlain = false;
            var verdict = Verdict.None;

            List<string[]> sets;
            if (_stems.TryGetValue(form, out sets))
                foreach (var flags in sets)
                {
                    if (Has(flags, _affix.ForbiddenFlag)) return Verdict.Forbidden;
                    if (Has(flags, _affix.NeedAffixFlag)) continue;
                    if (caseChanged && Has(flags, _affix.KeepCaseFlag)) continue;
                    verdict = Verdict.Accept;
                    if (Has(flags, _affix.NoSuggestFlag)) sawNoSuggest = true;
                    else sawPlain = true;
                }
            if (verdict == Verdict.Accept)
            {
                noSuggestOnly = sawNoSuggest && !sawPlain;
                return verdict;
            }

            // — Suffixe seul, puis préfixe seul, puis préfixe + suffixe.
            if (TrySuffixes(form, caseChanged, null, null)) return Accept(out noSuggestOnly);
            foreach (var length in _prefixLengths)
            {
                if (length > form.Length) break;
                List<AffixHit> hits;
                if (!_prefixByAppend.TryGetValue(form.Substring(0, length), out hits))
                    continue;
                foreach (var hit in hits)
                {
                    var rest = hit.Entry.Strip
                        + form.Substring(length, form.Length - length);
                    if (rest.Length == 0 && !_affix.FullStrip) continue;
                    if (!MatchesStart(rest, hit.Entry.Condition)) continue;
                    // préfixe direct sur le radical
                    if (StemHasFlag(rest, hit.Rule.Flag, caseChanged,
                        RequiresCircumfixPartner(hit.Entry) ? _affix.CircumfixFlag : null))
                        return Accept(out noSuggestOnly);
                    // croisé : préfixe + suffixe
                    if (hit.Rule.CrossProduct
                        && TrySuffixes(rest, caseChanged, hit.Rule.Flag, hit.Entry))
                        return Accept(out noSuggestOnly);
                }
            }
            return Verdict.None;
        }

        private static Verdict Accept(out bool noSuggestOnly)
        {
            noSuggestOnly = false; // une voie affixée est toujours suggérable
            return Verdict.Accept;
        }

        /// <summary>Les voies suffixées d'une forme. prefixFlag/prefixEntry :
        /// non nuls en croisement — le préfixe déjà retiré doit être autorisé
        /// par le radical OU par la continuation du suffixe, et les deux
        /// moitiés d'un circonfixe se réclament l'une l'autre.</summary>
        private bool TrySuffixes(string form, bool caseChanged,
            string prefixFlag, AffixEntry prefixEntry)
        {
            foreach (var length in _suffixLengths)
            {
                if (length > form.Length) break;
                List<AffixHit> hits;
                if (!_suffixByAppend.TryGetValue(
                    form.Substring(form.Length - length), out hits)) continue;
                foreach (var hit in hits)
                {
                    if (prefixFlag != null && !hit.Rule.CrossProduct) continue;
                    var stem = form.Substring(0, form.Length - length)
                        + hit.Entry.Strip;
                    if (stem.Length == 0 && !_affix.FullStrip) continue;
                    if (!MatchesEnd(stem, hit.Entry.Condition)) continue;
                    // Circonfixe : un suffixe marqué seul est invalide, et en
                    // croisement les deux côtés doivent l'être (inerte en 7.7).
                    var suffixCircum = RequiresCircumfixPartner(hit.Entry);
                    if (prefixFlag == null && suffixCircum) continue;
                    if (prefixFlag != null)
                    {
                        var prefixCircum = RequiresCircumfixPartner(prefixEntry);
                        if (suffixCircum != prefixCircum) continue;
                    }
                    List<string[]> sets;
                    if (!_stems.TryGetValue(stem, out sets)) continue;
                    foreach (var flags in sets)
                    {
                        if (!Has(flags, hit.Rule.Flag)) continue;
                        if (Has(flags, _affix.ForbiddenFlag)) continue;
                        if (caseChanged && Has(flags, _affix.KeepCaseFlag)) continue;
                        if (prefixFlag != null && !Has(flags, prefixFlag)
                            && !hit.Entry.ContinuationHas(prefixFlag)) continue;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool StemHasFlag(string stem, string flag, bool caseChanged,
            string forbiddenAlone)
        {
            if (forbiddenAlone != null) return false; // circonfixe seul : non
            List<string[]> sets;
            if (!_stems.TryGetValue(stem, out sets)) return false;
            foreach (var flags in sets)
            {
                if (!Has(flags, flag)) continue;
                if (Has(flags, _affix.ForbiddenFlag)) continue;
                if (caseChanged && Has(flags, _affix.KeepCaseFlag)) continue;
                return true;
            }
            return false;
        }

        private bool RequiresCircumfixPartner(AffixEntry entry)
        {
            return _affix.CircumfixFlag != null
                && entry.ContinuationHas(_affix.CircumfixFlag);
        }

        private static bool Has(string[] flags, string flag)
        {
            if (flag == null) return false;
            foreach (var f in flags) if (f == flag) return true;
            return false;
        }

        private static bool MatchesEnd(string stem, ConditionItem[] condition)
        {
            if (condition == null) return true;
            if (stem.Length < condition.Length) return false;
            var offset = stem.Length - condition.Length;
            for (var i = 0; i < condition.Length; i++)
                if (!condition[i].Matches(stem[offset + i])) return false;
            return true;
        }

        private static bool MatchesStart(string stem, ConditionItem[] condition)
        {
            if (condition == null) return true;
            if (stem.Length < condition.Length) return false;
            for (var i = 0; i < condition.Length; i++)
                if (!condition[i].Matches(stem[i])) return false;
            return true;
        }

        private string ApplyIconv(string word)
        {
            var touched = false;
            foreach (var pair in _affix.InputConversions)
                if (word.IndexOf(pair[0], StringComparison.Ordinal) >= 0)
                { touched = true; break; }
            if (!touched) return word;
            foreach (var pair in _affix.InputConversions)
                word = word.Replace(pair[0], pair[1]);
            return word;
        }

        // ================================================================ casse

        private enum Shape { Lower, Capitalized, AllCaps, Mixed }

        private static Shape ShapeOf(string word)
        {
            var letters = 0;
            var upper = 0;
            var firstUpper = false;
            foreach (var c in word)
            {
                if (!char.IsLetter(c)) continue;
                if (letters == 0) firstUpper = char.IsUpper(c);
                letters++;
                if (char.IsUpper(c)) upper++;
            }
            if (upper == 0) return Shape.Lower;
            if (upper == letters && letters > 1) return Shape.AllCaps;
            if (upper == 1 && firstUpper) return Shape.Capitalized;
            if (upper == letters) return Shape.AllCaps;
            return Shape.Mixed;
        }

        private static string Lower(string word)
        {
            return word.ToLowerInvariant();
        }

        private static string Capitalize(string lower)
        {
            if (lower.Length == 0) return lower;
            return char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        // ========================================================== suggestions

        /// <summary>Suggestions plafonnées à 10, dans l'ordre de confiance :
        /// REP (les confusions typiques du français), MAP (les variantes
        /// accentuées), Damerau-Levenshtein 1 sur l'alphabet TRY, puis une
        /// distance 2 bornée si la moisson est maigre. La casse du mot
        /// d'origine est rétablie. PAS de n-grammes (doctrine : backlog).</summary>
        public List<string> Suggest(string word)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(word)) return results;
            var w = ApplyIconv(word);
            var shape = ShapeOf(w);
            var work = shape == Shape.Capitalized || shape == Shape.AllCaps
                ? Lower(w) : w;
            var seen = new HashSet<string> { work };
            var repCandidates = new List<string>();

            // 1. REP — avec ancres ^ et $.
            foreach (var pair in _affix.Replacements)
            {
                var from = pair[0];
                var to = pair[1].Replace("_", " ");
                if (from.StartsWith("^", StringComparison.Ordinal))
                {
                    var body = from.Substring(1);
                    var anchoredEnd = body.EndsWith("$", StringComparison.Ordinal);
                    if (anchoredEnd) body = body.Substring(0, body.Length - 1);
                    // batch 29, 0.4 : la branche non ancrée passait un
                    // exactOverride égal au candidat, ce qui court-circuitait
                    // Restore — « Ecole » corrigé par REP ^e é sortait
                    // « école » au lieu d'« École ». Seule la branche
                    // pleine-correspondance garde le remplacement verbatim.
                    if (anchoredEnd
                        ? work == body
                        : work.StartsWith(body, StringComparison.Ordinal))
                        Offer(results, seen, to + work.Substring(body.Length), shape,
                            anchoredEnd ? to : null);
                    continue;
                }
                if (from.EndsWith("$", StringComparison.Ordinal))
                {
                    var body = from.Substring(0, from.Length - 1);
                    if (work.EndsWith(body, StringComparison.Ordinal))
                        Offer(results, seen,
                            work.Substring(0, work.Length - body.Length) + to, shape, null);
                    continue;
                }
                var index = 0;
                while ((index = work.IndexOf(from, index, StringComparison.Ordinal)) >= 0)
                {
                    var candidate = work.Substring(0, index) + to
                        + work.Substring(index + from.Length);
                    repCandidates.Add(candidate);
                    Offer(results, seen, candidate, shape, null);
                    index++;
                }
            }

            // 2. MAP — un caractère remplacé par un autre de sa classe, puis
            // DEUX (theatre → théâtre, epee → épée : les accents vont par
            // paires en français), puis MAP après UNE délétion/transposition
            // (rivierre → riviere → rivière). Bon marché : les classes sont
            // petites, tout passe par le cache d'unicité.
            var mapOnce = new List<string>();
            CollectMapEdits(work, mapOnce);
            foreach (var candidate in mapOnce)
                Offer(results, seen, candidate, shape, null);
            if (results.Count < 10)
                foreach (var first in mapOnce)
                {
                    var mapTwice = new List<string>();
                    CollectMapEdits(first, mapTwice);
                    foreach (var candidate in mapTwice)
                        Offer(results, seen, candidate, shape, null);
                    if (results.Count >= 10) break;
                }
            if (results.Count < 10)
            {
                var slides = new List<string>();
                for (var i = 0; i < work.Length; i++)
                    slides.Add(work.Substring(0, i) + work.Substring(i + 1));
                for (var i = 0; i < work.Length - 1; i++)
                    slides.Add(work.Substring(0, i) + work[i + 1] + work[i]
                        + work.Substring(i + 2));
                foreach (var slide in slides)
                {
                    Offer(results, seen, slide, shape, null);
                    var chained = new List<string>();
                    CollectMapEdits(slide, chained);
                    foreach (var candidate in chained)
                        Offer(results, seen, candidate, shape, null);
                    if (results.Count >= 10) break;
                }
            }

            // 2 ter. Voisinage clavier (KEY, batch 29 0.6) : la touche d'à
            // côté sur la même rangée, essayée AVANT l'alphabet TRY entier —
            // c'est la coquille physique la plus probable après la confusion
            // orthographique (REP) et l'accent (MAP).
            for (var i = 0; i < work.Length; i++)
            {
                string near;
                if (!_keyNeighbors.TryGetValue(work[i], out near)) continue;
                foreach (var c in near)
                    Offer(results, seen, work.Substring(0, i) + c
                        + work.Substring(i + 1), shape, null);
            }

            // 3. Damerau-Levenshtein 1 sur TRY.
            var alphabet = _affix.TryChars.Length > 0
                ? _affix.TryChars : "esntiarulodcpm";
            EditsInto(work, alphabet, shape, results, seen);

            // 4. REP enchaîné d'une édition : la confusion typique PUIS la
            // coquille (developement → REP e→é → + p → développement).
            if (results.Count < 5)
            {
                var narrow = alphabet.Length > 30
                    ? alphabet.Substring(0, 30) : alphabet;
                foreach (var rep in repCandidates)
                {
                    var chained = new List<string>();
                    CollectEdits(rep, narrow, chained);
                    foreach (var candidate in chained)
                        Offer(results, seen, candidate, shape, null);
                    if (results.Count >= 10) break;
                }
            }

            // 5. Distance 2, bornée : alphabet réduit aux 30 lettres les plus
            // fréquentes du TRY (accents è/ê/à compris), expansion plafonnée
            // — un secours pour les mots massacrés, jamais une seconde.
            if (results.Count < 3)
            {
                var narrow = alphabet.Length > 30
                    ? alphabet.Substring(0, 30) : alphabet;
                var firstPass = new List<string>();
                CollectEdits(work, narrow, firstPass);
                var budget = 0;
                foreach (var candidate in firstPass)
                {
                    if (results.Count >= 10 || budget > 120000) break;
                    var secondPass = new List<string>();
                    CollectEdits(candidate, narrow, secondPass);
                    foreach (var second in secondPass)
                    {
                        budget++;
                        Offer(results, seen, second, shape, null);
                        if (results.Count >= 10 || budget > 120000) break;
                    }
                }
            }
            return results;
        }

        private void EditsInto(string work, string alphabet, Shape shape,
            List<string> results, HashSet<string> seen)
        {
            var edits = new List<string>();
            CollectEdits(work, alphabet, edits);
            foreach (var candidate in edits)
            {
                if (results.Count >= 10) return;
                Offer(results, seen, candidate, shape, null);
            }
        }

        private void CollectMapEdits(string work, List<string> into)
        {
            for (var i = 0; i < work.Length; i++)
                foreach (var cls in _affix.MapClasses)
                {
                    if (cls.IndexOf(work[i]) < 0) continue;
                    foreach (var other in cls)
                        if (other != work[i])
                            into.Add(work.Substring(0, i) + other
                                + work.Substring(i + 1));
                }
        }

        /// <summary>Éditions à distance 1, du plus probable au moins probable
        /// pour une coquille : délétions, transpositions, INSERTIONS (lettre
        /// doublée oubliée), remplacements — l'ordre compte, la distance 2
        /// expanse les premières d'abord.</summary>
        private static void CollectEdits(string work, string alphabet,
            List<string> into)
        {
            for (var i = 0; i < work.Length; i++)
                into.Add(work.Substring(0, i) + work.Substring(i + 1)); // délétion
            for (var i = 0; i < work.Length - 1; i++)
                into.Add(work.Substring(0, i) + work[i + 1] + work[i]
                    + work.Substring(i + 2)); // transposition
            for (var i = 0; i <= work.Length; i++)
                foreach (var c in alphabet)
                    into.Add(work.Substring(0, i) + c + work.Substring(i));
            for (var i = 0; i < work.Length; i++)
                foreach (var c in alphabet)
                    if (c != work[i])
                        into.Add(work.Substring(0, i) + c + work.Substring(i + 1));
        }

        private void Offer(List<string> results, HashSet<string> seen,
            string candidate, Shape shape, string exactOverride)
        {
            if (results.Count >= 10) return;
            if (candidate.Length == 0 || !seen.Add(candidate)) return;
            bool noSuggest;
            string restored;
            if (AcceptsDetail(candidate, out noSuggest) && !noSuggest)
                restored = exactOverride ?? Restore(candidate, shape);
            else
            {
                // batch 29, 0.5 : le travail se fait en minuscules, donc un
                // nom propre du dictionnaire (Paris, Kevlar) était
                // structurellement insuggérable. La forme Capitalisée du
                // candidat se défend elle-même — au prix d'une seconde
                // recherche sur les seuls candidats rejetés.
                var capital = Capitalize(candidate);
                if (capital == candidate) return;
                if (!AcceptsDetail(capital, out noSuggest) || noSuggest) return;
                restored = shape == Shape.AllCaps
                    ? capital.ToUpperInvariant() : capital;
            }
            restored = ApplyOconv(restored);
            if (!results.Contains(restored)) results.Add(restored);
        }

        private string ApplyOconv(string word)
        {
            foreach (var pair in _affix.OutputConversions)
                word = word.Replace(pair[0], pair[1]);
            return word;
        }

        private static string Restore(string candidate, Shape shape)
        {
            if (shape == Shape.Capitalized) return Capitalize(candidate);
            if (shape == Shape.AllCaps) return candidate.ToUpperInvariant();
            return candidate;
        }
    }
}

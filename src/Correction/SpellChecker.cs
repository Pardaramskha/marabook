using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Correction.Hunspell;
using Marabook.Model;

namespace Marabook.Correction
{
    /// <summary>Le point de chargement unique du dictionnaire embarqué
    /// (dict/fr-toutesvariantes à côté de l'exe). Chargé UNE fois,
    /// paresseusement ; null si les fichiers manquent (le vérificateur
    /// d'orthographe se retire alors sans bruit ni plantage).</summary>
    public static class SpellDictionary
    {
        private static SpellEngine _engine;
        private static bool _tried;

        public static SpellEngine Default
        {
            get
            {
                if (_tried) return _engine;
                _tried = true;
                try
                {
                    var folder = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "dict");
                    var aff = Path.Combine(folder, "fr-toutesvariantes.aff");
                    var dic = Path.Combine(folder, "fr-toutesvariantes.dic");
                    // 1.0.5 : le complément maison (registre familier,
                    // onomatopées, mots rares absents de Grammalecte) se
                    // charge à côté — voir dict/fr-complement.dic.
                    var complement = Path.Combine(folder, "fr-complement.dic");
                    if (File.Exists(aff) && File.Exists(dic))
                        _engine = SpellEngine.Load(aff, dic, new[] { complement });
                }
                catch { _engine = null; }
                return _engine;
            }
        }
    }

    /// <summary>Le correcteur orthographique français (batch 27, lot C.4) —
    /// catégorie Spelling, portée ParagraphLocal (le cache du pilote fait le
    /// reste). Alimenté par le TOKENISEUR UNIQUE : il ne voit que des cœurs
    /// (sans élision ni enclitique), ne signale ni les nombres (nature
    /// Number), ni les sigles (tout-capitales inconnu = silence, règle du
    /// prompt), ni les initiales isolées. NoProof et les ignorés sont
    /// l'affaire du PILOTE — le vérificateur reste bête. Un composé absent
    /// du dictionnaire est vérifié segment par segment (la composition
    /// française est productive : wagon-citerne ne rougit pas) et seul un
    /// segment fautif est signalé. Les dictionnaires personnels (projet +
    /// global, lot D) sont des mots ENSEIGNÉS : comparés par la clé pliée.</summary>
    public class SpellChecker : IChecker
    {
        private readonly SpellEngine _engine;
        private readonly Dictionary<string, List<string>> _suggestionCache
            = new Dictionary<string, List<string>>();

        /// <summary>Dictionnaire personnel du PROJET (Project.Lexicon) — des
        /// entrées à nature grammaticale : toutes leurs FORMES sont acceptées.</summary>
        public List<LexiconEntry> ProjectWords = new List<LexiconEntry>();

        /// <summary>Dictionnaire personnel GLOBAL (AppSettings.Lexicon).</summary>
        public List<LexiconEntry> GlobalWords = new List<LexiconEntry>();

        /// <summary>Les NÉOLOGISMES (1.0.5) : vrai, un mot inconnu mais bien
        /// formé (Neologisms.Explain) est relevé dans sa catégorie, en
        /// indice, au lieu d'une faute ; faux, il rougit comme avant.</summary>
        public bool Neologisms = true;

        /// <summary>Enseigne un mot nu (entrée « autre ») dans la liste donnée.</summary>
        public static void Teach(List<LexiconEntry> list, string word)
        {
            if (LexiconEntry.Find(list, word) == null) list.Add(LexiconEntry.Simple(word));
        }

        public SpellChecker(SpellEngine engine)
        {
            _engine = engine;
        }

        public string Id { get { return "spelling"; } }
        public string Label { get { return "Orthographe"; } }
        public FindingCategory Category { get { return FindingCategory.Spelling; } }

        /// <summary>Locale par définition : un mot est juste ou faux dans son
        /// paragraphe — le cache du pilote ne revérifie que ce qui change.</summary>
        public CheckerScope Scope { get { return CheckerScope.ParagraphLocal; } }

        public List<Finding> Check(TextDocument document, StyleSheet styles)
        {
            throw new NotSupportedException(
                "SpellChecker est ParagraphLocal : le pilote appelle CheckParagraph.");
        }

        public List<Finding> CheckParagraph(TextParagraph paragraph, StyleSheet styles)
        {
            return CheckText(PivotEdit.FlatText(paragraph));
        }

        /// <summary>Les mêmes règles sur un TEXTE PLAT (1.0.4 : le corps
        /// Markdown des fiches, dans sa zone de texte) — les offsets sont
        /// ceux de la chaîne donnée.</summary>
        public List<Finding> CheckText(string text)
        {
            var findings = new List<Finding>();
            if (_engine == null || string.IsNullOrEmpty(text)) return findings;
            var tokens = FrenchTokenizer.Tokenize(text);
            // Le familier (1.0.5) : les contractions de l'oral ont leur propre
            // vérificateur ; l'orthographe ne les rougit pas.
            var familiar = Familiar.Spans(text);
            foreach (var token in tokens)
            {
                if (token.Kind != TokenKind.Word) continue;
                var core = token.CoreSurface;
                if (LetterCount(core) < 2) continue; // initiales (M.), résidus
                if (familiar.Count > 0 && Familiar.Covers(familiar, token.Start, token.Start + token.Length)) continue;
                // Le moteur D'ABORD, les appris ENSUITE (batch 29, 0.3) :
                // même sémantique — un mot que le moteur accepte n'a pas
                // besoin d'être appris — mais Learned sort du chemin des
                // 99 % de mots corrects.
                if (_engine.Accepts(core)) continue;
                if (Learned(core)) continue;
                // Sigle : un tout-capitales inconnu se tait (SNCF).
                if (token.Shape == CaseShape.AllCaps) continue;
                // Les licences de la fiction (1.0.5, relevées sur « Le serment
                // des gardiens du feu ») — jamais une faute :
                // — un mot COUPÉ par des points de suspension (« je vole
                //   vraim… ») : la parole s'interrompt, le fragment se tait ;
                // — un ALLONGEMENT expressif (« chuuuuut », « trooooop ») : les
                //   lettres triplées ramenées à une ou deux donnent un mot
                //   connu ;
                // — un mot DÉTACHÉ en syllabes (« gé-nial », « Vi-si-ter ») :
                //   les segments recollés donnent un mot connu.
                if (CutByEllipsis(text, token.CoreEnd)) continue;
                if (Lengthened(core)) continue;
                if (token.CoreParts.Length > 1 && Known(core.Replace("-", "").Replace("\u2011", ""))) continue;

                if (token.CoreParts.Length > 1)
                {
                    // Composé absent en bloc : chaque segment se défend seul,
                    // aux offsets D'ORIGINE portés par le token (batch 29,
                    // 0.2 — additionner des longueurs NFC décalait l'ondulé
                    // sur un texte décomposé).
                    for (var p = 0; p < token.CoreParts.Length; p++)
                    {
                        var part = token.CoreParts[p];
                        if (LetterCount(part) >= 2 && !_engine.Accepts(part)
                            && !Learned(part))
                            findings.Add(Report(part, token.CorePartStarts[p],
                                token.CorePartLengths[p]));
                    }
                }
                else
                    findings.Add(Report(core, token.CoreStart, token.CoreLength));
            }
            return findings;
        }

        /// <summary>Le mot est-il connu — moteur ou appris ? (la base des
        /// règles de néologisme)</summary>
        public bool Knows(string word)
        {
            return !string.IsNullOrEmpty(word) && (_engine.Accepts(word) || Learned(word));
        }

        private bool Known(string word)
        {
            return word.Length >= 2 && (_engine.Accepts(word) || Learned(word));
        }

        /// <summary>Des points de suspension collés à la fin du mot : « … »
        /// ou « ... » juste après, sans espace.</summary>
        private static bool CutByEllipsis(string text, int end)
        {
            if (end >= text.Length) return false;
            if (text[end] == '\u2026') return true;
            return end + 2 < text.Length && text[end] == '.' && text[end + 1] == '.' && text[end + 2] == '.';
        }

        /// <summary>Une lettre répétée trois fois ou plus : les formes
        /// ramenées à une lettre, puis à deux, sont-elles connues ?</summary>
        private bool Lengthened(string word)
        {
            var triple = false;
            for (var i = 2; i < word.Length && !triple; i++)
                if (word[i] == word[i - 1] && word[i] == word[i - 2]) triple = true;
            if (!triple) return false;
            return Known(Collapse(word, 1)) || Known(Collapse(word, 2));
        }

        /// <summary>Les séquences de trois lettres identiques ou plus ramenées
        /// à « keep » lettres (les doubles restent des doubles).</summary>
        public static string Collapse(string word, int keep)
        {
            var sb = new System.Text.StringBuilder(word.Length);
            var i = 0;
            while (i < word.Length)
            {
                var j = i;
                while (j < word.Length && word[j] == word[i]) j++;
                var run = j - i;
                var kept = run >= 3 ? Math.Min(keep, run) : run;
                for (var k = 0; k < kept; k++) sb.Append(word[i]);
                i = j;
            }
            return sb.ToString();
        }

        /// <summary>Les signalements naissent SANS suggestions : les calculer
        /// pour chaque mot inconnu d'une passe coûterait des secondes sur un
        /// texte très fautif (mesuré : ~1,6 ms par mot). L'interface les
        /// demande à Suggestions() au moment où elle les MONTRE (menu
        /// contextuel, panneau) — avec cache par mot.</summary>
        private Finding Report(string word, int start, int length)
        {
            // Un néologisme bien formé (1.0.5) : sa catégorie, en indice, la
            // raison dans le message — le menu propose toujours de l'apprendre.
            var why = Neologisms ? Correction.Neologisms.Explain(word, Knows) : null;
            if (why != null)
                return new Finding
                {
                    Start = start,
                    Length = length,
                    Category = FindingCategory.Neologism,
                    Severity = FindingSeverity.Hint,
                    Message = "« " + word + " » : néologisme — " + why + " ; absent du dictionnaire",
                    RuleId = Correction.Neologisms.Rule,
                    CheckerId = Id,
                    Word = word,
                    Suggests = SuggestionSource.Spelling
                };
            return new Finding
            {
                Start = start,
                Length = length,
                Category = FindingCategory.Spelling,
                Severity = FindingSeverity.Error,
                Message = "« " + word + " » est inconnu du dictionnaire",
                RuleId = "spelling",
                CheckerId = Id,
                Word = word,
                Suggests = SuggestionSource.Spelling
            };
        }

        /// <summary>Suggestions pour un mot signalé, à la demande, en cache.
        /// Les mots APPRIS proches passent en tête (batch 29, 0.5) : sur un
        /// roman, le premier service du menu contextuel est de rattraper un
        /// nom de personnage mal tapé — et ces noms vivent dans le
        /// dictionnaire personnel, pas dans le .dic.</summary>
        public List<string> Suggestions(string word)
        {
            List<string> suggestions;
            if (!_suggestionCache.TryGetValue(word, out suggestions))
            {
                suggestions = _engine == null
                    ? new List<string>() : _engine.Suggest(word);
                InsertLearnedMatches(word, suggestions);
                _suggestionCache[word] = suggestions;
            }
            return suggestions;
        }

        /// <summary>Les suggestions SI ELLES SONT PRÊTES, null sinon —
        /// jamais de calcul Suggest ici (batch 30) : le panneau Correction
        /// se construit sans payer Hunspell, la file d'arrière-plan chauffe
        /// le mémo du moteur, et la reconstruction suivante trouve tout.
        /// L'insertion des mots appris, marginale, se fait au passage.</summary>
        public List<string> CachedSuggestions(string word)
        {
            List<string> suggestions;
            if (_suggestionCache.TryGetValue(word, out suggestions))
                return suggestions;
            if (_engine == null || !_engine.HasSuggestMemo(word)) return null;
            suggestions = _engine.Suggest(word); // mémo : copie immédiate
            InsertLearnedMatches(word, suggestions);
            _suggestionCache[word] = suggestions;
            return suggestions;
        }

        /// <summary>Les mots appris à distance d'édition ≤ 2 du mot fautif
        /// (clés pliées : « kaladinn » trouve « Kaladin »), insérés en tête
        /// dans leur casse d'origine. Les listes sont petites (centaines au
        /// plus), le balayage est marginal à côté de Suggest().</summary>
        private void InsertLearnedMatches(string word, List<string> suggestions)
        {
            var key = FrenchTokenizer.Fold(word);
            var inserted = 0;
            for (var source = 0; source < 2 && inserted < 3; source++)
                foreach (var entry in source == 0 ? ProjectWords : GlobalWords)
                {
                    foreach (var form in entry.Forms())
                    {
                        var entryKey = FrenchTokenizer.Fold(form);
                        if (entryKey == key) continue; // déjà accepté par Learned
                        if (Math.Abs(entryKey.Length - key.Length) > 2) continue;
                        if (EditDistanceAtMost2(key, entryKey) > 2) continue;
                        if (!suggestions.Contains(form))
                            suggestions.Insert(inserted++, form);
                        if (inserted >= 3) break;
                    }
                    if (inserted >= 3) break;
                }
        }

        /// <summary>Damerau-Levenshtein plafonné : rend 0, 1, 2 ou 3 (= « plus
        /// de 2 »), bandes inutiles non calculées.</summary>
        private static int EditDistanceAtMost2(string a, string b)
        {
            var la = a.Length;
            var lb = b.Length;
            var previous2 = new int[lb + 1];
            var previous = new int[lb + 1];
            var current = new int[lb + 1];
            for (var j = 0; j <= lb; j++) previous[j] = j;
            for (var i = 1; i <= la; i++)
            {
                current[0] = i;
                var rowMin = current[0];
                for (var j = 1; j <= lb; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    var best = Math.Min(
                        Math.Min(previous[j] + 1, current[j - 1] + 1),
                        previous[j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2]
                        && a[i - 2] == b[j - 1])
                        best = Math.Min(best, previous2[j - 2] + 1);
                    current[j] = best;
                    if (best < rowMin) rowMin = best;
                }
                if (rowMin > 2) return 3; // la ligne entière dépasse : stop
                var swap = previous2;
                previous2 = previous;
                previous = current;
                current = swap;
            }
            return previous[lb];
        }

        /// <summary>Un mot enseigné (projet ou global), clé pliée — enseigner
        /// « Batiatus » couvre « batiatus » et « BATIATUS ». Public : c'est
        /// la moitié « mots appris » du prédicat KnownWord du tokeniseur
        /// (batch 29, amendement A1 — un « Vaux-le-Vicomte » enseigné ne doit
        /// pas se faire découper à son « -le »).</summary>
        public bool IsLearned(string word)
        {
            return Learned(word);
        }

        // Les clés pliées des deux listes, reconstruites à la demande —
        // batch 29, 0.3 : le balayage linéaire refaisait Fold() sur CHAQUE
        // entrée pour CHAQUE mot du texte (400 appris × 300 mots = 120 000
        // Fold par revérification de paragraphe).
        private HashSet<string> _learnedKeys;

        /// <summary>À appeler quand les listes de mots appris changent (mot
        /// ajouté, retiré dans les Préférences, projet chargé) — les mêmes
        /// moments que CheckerHost.InvalidateCache().</summary>
        public void InvalidateLearned()
        {
            _learnedKeys = null;
            // Les suggestions aussi : un nom appris depuis doit apparaître.
            _suggestionCache.Clear();
        }

        private bool Learned(string word)
        {
            if (_learnedKeys == null)
            {
                _learnedKeys = new HashSet<string>(StringComparer.Ordinal);
                // Batch 33 : toutes les FORMES d'une entrée (pluriel,
                // féminin, conjugaison) — pas seulement le mot.
                foreach (var entry in ProjectWords)
                    foreach (var form in entry.Forms())
                        _learnedKeys.Add(FrenchTokenizer.Fold(form));
                foreach (var entry in GlobalWords)
                    foreach (var form in entry.Forms())
                        _learnedKeys.Add(FrenchTokenizer.Fold(form));
            }
            return _learnedKeys.Contains(FrenchTokenizer.Fold(word));
        }

        private static int LetterCount(string word)
        {
            var count = 0;
            foreach (var c in word) if (char.IsLetter(c)) count++;
            return count;
        }
    }
}

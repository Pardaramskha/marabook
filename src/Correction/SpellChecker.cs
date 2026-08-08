using System;
using System.Collections.Generic;
using System.IO;
using UniversSale.Correction.Hunspell;
using UniversSale.Model;

namespace UniversSale.Correction
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
                    if (File.Exists(aff) && File.Exists(dic))
                        _engine = SpellEngine.Load(aff, dic);
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

        /// <summary>Dictionnaire personnel du PROJET (Project.LearnedWords).</summary>
        public List<string> ProjectWords = new List<string>();

        /// <summary>Dictionnaire personnel GLOBAL (AppSettings.LearnedWords).</summary>
        public List<string> GlobalWords = new List<string>();

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
            var findings = new List<Finding>();
            if (_engine == null) return findings;
            var tokens = FrenchTokenizer.Tokenize(PivotEdit.FlatText(paragraph));
            foreach (var token in tokens)
            {
                if (token.Kind != TokenKind.Word) continue;
                var core = token.CoreSurface;
                if (LetterCount(core) < 2) continue; // initiales (M.), résidus
                if (Learned(core)) continue;
                if (_engine.Accepts(core)) continue;
                // Sigle : un tout-capitales inconnu se tait (SNCF).
                if (token.Shape == CaseShape.AllCaps) continue;

                if (token.CoreParts.Length > 1)
                {
                    // Composé absent en bloc : chaque segment se défend seul.
                    var offset = token.CoreStart;
                    foreach (var part in token.CoreParts)
                    {
                        if (part.Length >= 2 && !Learned(part)
                            && !_engine.Accepts(part))
                            findings.Add(Report(part, offset, part.Length));
                        offset += part.Length + 1; // + le trait d'union
                    }
                }
                else
                    findings.Add(Report(core, token.CoreStart, token.CoreLength));
            }
            return findings;
        }

        /// <summary>Les signalements naissent SANS suggestions : les calculer
        /// pour chaque mot inconnu d'une passe coûterait des secondes sur un
        /// texte très fautif (mesuré : ~1,6 ms par mot). L'interface les
        /// demande à Suggestions() au moment où elle les MONTRE (menu
        /// contextuel, panneau) — avec cache par mot.</summary>
        private Finding Report(string word, int start, int length)
        {
            return new Finding
            {
                Start = start,
                Length = length,
                Category = FindingCategory.Spelling,
                Severity = FindingSeverity.Error,
                Message = "« " + word + " » est inconnu du dictionnaire",
                RuleId = "spelling",
                CheckerId = Id,
                Word = word
            };
        }

        /// <summary>Suggestions pour un mot signalé, à la demande, en cache.</summary>
        public List<string> Suggestions(string word)
        {
            List<string> suggestions;
            if (!_suggestionCache.TryGetValue(word, out suggestions))
            {
                suggestions = _engine == null
                    ? new List<string>() : _engine.Suggest(word);
                _suggestionCache[word] = suggestions;
            }
            return suggestions;
        }

        /// <summary>Un mot enseigné (projet ou global), clé pliée — enseigner
        /// « Batiatus » couvre « batiatus » et « BATIATUS ».</summary>
        private bool Learned(string word)
        {
            var key = FrenchTokenizer.Fold(word);
            foreach (var entry in ProjectWords)
                if (FrenchTokenizer.Fold(entry) == key) return true;
            foreach (var entry in GlobalWords)
                if (FrenchTokenizer.Fold(entry) == key) return true;
            return false;
        }

        private static int LetterCount(string word)
        {
            var count = 0;
            foreach (var c in word) if (char.IsLetter(c)) count++;
            return count;
        }
    }
}

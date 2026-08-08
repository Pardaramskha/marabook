using System;
using System.Collections.Generic;
using UniversSale.Model;

namespace UniversSale.Correction
{
    /// <summary>Le pilote de correction : exécute les vérificateurs actifs,
    /// agrège, trie par position, et filtre — plages NoProof, mots ignorés
    /// dans le projet (persistés au .plot), mots ignorés partout (réglages),
    /// signalements ignorés « ici » (session seulement : une position exacte
    /// ne survit pas honnêtement aux éditions, tranché et documenté).
    /// Aucune dépendance WPF.</summary>
    public class CheckerHost
    {
        private readonly List<IChecker> _checkers = new List<IChecker>();

        /// <summary>« Ignorer dans ce projet » — brancher Project.ProofIgnored
        /// (la liste vit et se persiste avec le projet).</summary>
        public List<string> ProjectIgnored = new List<string>();

        /// <summary>« Ignorer partout » — brancher AppSettings.ProofIgnored.</summary>
        public List<string> GlobalIgnored = new List<string>();

        // « Ignorer ici » : clé règle|paragraphe|début|mot, session seulement.
        private readonly HashSet<string> _here = new HashSet<string>();

        public List<IChecker> Checkers { get { return _checkers; } }

        public void Add(IChecker checker)
        {
            _checkers.Add(checker);
        }

        // Cache des vérificateurs LOCAUX (batch 27, lot B) : par
        // (vérificateur, index de paragraphe), l'empreinte du texte plat et
        // les signalements produits — un paragraphe inchangé n'est jamais
        // revérifié. Les vérificateurs globaux (répétitions) repassent
        // entiers à chaque cycle, par nature.
        private sealed class CacheEntry
        {
            public long Fingerprint;
            public List<Finding> Findings;
        }
        private readonly Dictionary<string, CacheEntry> _cache
            = new Dictionary<string, CacheEntry>();
        private int _cachedParagraphCount;

        /// <summary>Tous les signalements du document, triés par position,
        /// filtrés (NoProof, ignorés). C'est LA sortie du pilote.</summary>
        public List<Finding> Run(TextDocument document, StyleSheet styles)
        {
            var findings = new List<Finding>();
            foreach (var checker in _checkers)
                if (checker.Scope == CheckerScope.WholeDocument)
                    findings.AddRange(checker.Check(document, styles));
                else
                    findings.AddRange(RunLocal(checker, document, styles));
            var kept = new List<Finding>();
            foreach (var finding in findings)
                if (!IsFiltered(document, finding)) kept.Add(finding);
            // Tri TOTAL (batch 27, lot 0.4) : List.Sort est un introsort
            // INSTABLE — deux signalements au même (paragraphe, offset), cas
            // normal en typographie, s'ordonnaient arbitrairement. Le
            // départage descend jusqu'à la longueur pour que deux passes
            // rendent toujours le même ordre.
            kept.Sort(delegate(Finding a, Finding b)
            {
                if (a.ParagraphIndex != b.ParagraphIndex)
                    return a.ParagraphIndex.CompareTo(b.ParagraphIndex);
                if (a.Start != b.Start) return a.Start.CompareTo(b.Start);
                var checker = string.CompareOrdinal(a.CheckerId, b.CheckerId);
                if (checker != 0) return checker;
                var rule = string.CompareOrdinal(a.RuleId, b.RuleId);
                if (rule != 0) return rule;
                return a.Length.CompareTo(b.Length);
            });
            return kept;
        }

        /// <summary>La passe d'un vérificateur LOCAL sous cache : seuls les
        /// paragraphes dont l'empreinte a changé sont revérifiés ; les
        /// signalements mis en cache portent des offsets locaux, leur
        /// ParagraphIndex est (re)posé ici — un paragraphe qui se déplace
        /// dans le document garde son cache tant que son texte est le même
        /// à l'index près.</summary>
        private List<Finding> RunLocal(IChecker checker, TextDocument document,
            StyleSheet styles)
        {
            // Le document a rétréci : les entrées au-delà meurent.
            if (document.Paragraphs.Count < _cachedParagraphCount)
            {
                var stale = new List<string>();
                foreach (var key in _cache.Keys)
                {
                    var separator = key.LastIndexOf('|');
                    int index;
                    if (int.TryParse(key.Substring(separator + 1), out index)
                        && index >= document.Paragraphs.Count)
                        stale.Add(key);
                }
                foreach (var key in stale) _cache.Remove(key);
            }
            _cachedParagraphCount = document.Paragraphs.Count;

            var results = new List<Finding>();
            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var paragraph = document.Paragraphs[p];
                var fingerprint = FingerprintOf(paragraph);
                var key = checker.Id + "|" + p;
                CacheEntry entry;
                if (!_cache.TryGetValue(key, out entry)
                    || entry.Fingerprint != fingerprint)
                {
                    entry = new CacheEntry
                    {
                        Fingerprint = fingerprint,
                        Findings = checker.CheckParagraph(paragraph, styles)
                            ?? new List<Finding>()
                    };
                    _cache[key] = entry;
                }
                foreach (var finding in entry.Findings)
                {
                    finding.ParagraphIndex = p;
                    results.Add(finding);
                }
            }
            return results;
        }

        /// <summary>FNV-1a du texte plat (+ nombre de runs). NoProof ne
        /// change pas l'empreinte À DESSEIN : le filtrage NoProof relit le
        /// document VIVANT après cache, jamais les signalements gelés.</summary>
        private static long FingerprintOf(TextParagraph paragraph)
        {
            var text = PivotEdit.FlatText(paragraph);
            unchecked
            {
                var hash = (long)1469598103934665603;
                foreach (var c in text)
                {
                    hash ^= c;
                    hash *= 1099511628211;
                }
                hash ^= paragraph.Runs.Count * 397;
                return hash;
            }
        }

        /// <summary>« Ignorer ici » : ce signalement précis, cette session.
        /// Un déplacement du texte AVANT la plage change les offsets et fait
        /// réapparaître le signalement — limite assumée, testée en C5.</summary>
        public void IgnoreHere(Finding finding)
        {
            _here.Add(HereKey(finding));
        }

        /// <summary>« Ignorer dans ce projet » : le mot, insensible à la
        /// casse, pour tous les vérificateurs.</summary>
        public void IgnoreInProject(string word)
        {
            if (!ContainsWord(ProjectIgnored, word)) ProjectIgnored.Add(word);
        }

        public void IgnoreEverywhere(string word)
        {
            if (!ContainsWord(GlobalIgnored, word)) GlobalIgnored.Add(word);
        }

        private bool IsFiltered(TextDocument document, Finding finding)
        {
            if (_here.Contains(HereKey(finding))) return true;
            if (finding.Word.Length > 0
                && (ContainsWord(ProjectIgnored, finding.Word)
                    || ContainsWord(GlobalIgnored, finding.Word))) return true;
            return OverlapsNoProof(document, finding);
        }

        /// <summary>Vrai si la plage du signalement touche un run marqué
        /// « ne pas corriger » — mêmes unités plates que PivotEdit.</summary>
        private static bool OverlapsNoProof(TextDocument document, Finding finding)
        {
            if (finding.ParagraphIndex < 0
                || finding.ParagraphIndex >= document.Paragraphs.Count) return false;
            var cursor = 0;
            foreach (var run in document.Paragraphs[finding.ParagraphIndex].Runs)
            {
                var length = PivotEdit.IsElement(run) ? 1 : run.Text.Length;
                if (run.NoProof && cursor < finding.End && finding.Start < cursor + length)
                    return true;
                cursor += length;
            }
            return false;
        }

        private static string HereKey(Finding finding)
        {
            return finding.RuleId + "|" + finding.ParagraphIndex + "|"
                + finding.Start + "|" + finding.Word;
        }

        /// <summary>0.3 (batch 27) : la clé d'ignoré est LA MÊME normalisation
        /// que celle des vérificateurs — FrenchTokenizer.Fold (casse ET
        /// accents pliés). « Ignorer » COEUR fait taire cœur, Cœur et coeur ;
        /// OrdinalIgnoreCase ne pliait que la casse.</summary>
        private static bool ContainsWord(List<string> list, string word)
        {
            var key = FrenchTokenizer.Fold(word);
            foreach (var entry in list)
                if (FrenchTokenizer.Fold(entry) == key)
                    return true;
            return false;
        }
    }
}

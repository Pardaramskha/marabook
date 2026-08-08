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

        /// <summary>Tous les signalements du document, triés par position,
        /// filtrés (NoProof, ignorés). C'est LA sortie du pilote.</summary>
        public List<Finding> Run(TextDocument document, StyleSheet styles)
        {
            var findings = new List<Finding>();
            foreach (var checker in _checkers)
                findings.AddRange(checker.Check(document, styles));
            var kept = new List<Finding>();
            foreach (var finding in findings)
                if (!IsFiltered(document, finding)) kept.Add(finding);
            kept.Sort(delegate(Finding a, Finding b)
            {
                if (a.ParagraphIndex != b.ParagraphIndex)
                    return a.ParagraphIndex.CompareTo(b.ParagraphIndex);
                if (a.Start != b.Start) return a.Start.CompareTo(b.Start);
                return string.CompareOrdinal(a.CheckerId, b.CheckerId);
            });
            return kept;
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

        private static bool ContainsWord(List<string> list, string word)
        {
            foreach (var entry in list)
                if (string.Equals(entry, word, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}

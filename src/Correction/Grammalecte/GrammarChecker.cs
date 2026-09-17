using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marabook.Model;

namespace Marabook.Correction.Grammalecte
{
    /// <summary>Le vérificateur grammatical (batch 29, lot B.3) — catégorie
    /// Grammar, portée ParagraphLocal, exécution DIFFÉRÉE : chaque paragraphe
    /// part au pont Grammalecte (l'accord a besoin de la phrase entière, le
    /// paragraphe correspond au cache d'empreinte du pilote), la réponse
    /// revient quand elle revient. Les suggestions de Grammalecte alimentent
    /// directement le menu contextuel ; sRuleId va dans Finding.RuleId, ce
    /// qui donne « ignorer cette règle » (filtré par le pilote). Dégradation
    /// propre : pont indisponible, timeout, réponse illisible → la tâche
    /// échoue et le pilote fait comme si le vérificateur n'existait pas.</summary>
    public class GrammarChecker : IDeferredChecker
    {
        private readonly GrammalecteBridge _bridge;

        /// <summary>Les choix utilisateur (nom d'option Grammalecte → actif),
        /// par-dessus la politique du lot C — brancher les réglages.</summary>
        public Dictionary<string, bool> UserOptions;

        /// <summary>Les deux interrupteurs des Options du correcteur (batch
        /// 33) : la grammaire proprement dite, et la typographie servie par
        /// les règles typographiques de Grammalecte.</summary>
        public bool GrammarEnabled = true;
        public bool TypographyEnabled;

        public GrammarChecker(GrammalecteBridge bridge)
        {
            _bridge = bridge;
        }

        public string Id { get { return "grammar"; } }
        public string Label { get { return "Grammaire"; } }
        public FindingCategory Category { get { return FindingCategory.Grammar; } }
        public CheckerScope Scope { get { return CheckerScope.ParagraphLocal; } }

        public List<Finding> Check(TextDocument document, StyleSheet styles)
        {
            throw new NotSupportedException("GrammarChecker est différé.");
        }

        public List<Finding> CheckParagraph(TextParagraph paragraph, StyleSheet styles)
        {
            throw new NotSupportedException(
                "GrammarChecker est différé : le pilote appelle CheckParagraphAsync.");
        }

        public async Task<List<Finding>> CheckParagraphAsync(
            TextParagraph paragraph, StyleSheet styles, CancellationToken token)
        {
            var flat = PivotEdit.FlatText(paragraph);
            if (flat.Trim().Length == 0) return new List<Finding>();
            var mapper = OffsetMapper.Build(flat);
            var errors = await _bridge.CheckAsync(mapper.Sent,
                GrammalecteOptions.Effective(UserOptions, GrammarEnabled, TypographyEnabled), token)
                .ConfigureAwait(false);
            var findings = ToFindings(errors, mapper);
            // Les interrupteurs filtrent aussi la sortie (une option rallumée
            // à la main dans les Préférences ne passe pas outre).
            findings.RemoveAll(delegate(Finding f)
            {
                return f.Category == FindingCategory.Typography ? !TypographyEnabled : !GrammarEnabled;
            });
            return findings;
        }

        /// <summary>Les erreurs du pont → des signalements aux offsets du
        /// PIVOT (via la carte). Statique et sans pont : c'est la moitié
        /// testable en console sur des réponses JSON en dur (suite C9).</summary>
        public static List<Finding> ToFindings(List<BridgeError> errors,
            OffsetMapper mapper)
        {
            var findings = new List<Finding>();
            foreach (var error in errors)
            {
                int start;
                int length;
                if (!mapper.MapRange(error.Start, error.End, out start, out length)
                    || length <= 0)
                    continue; // borne aberrante : jetée, jamais un plantage
                var finding = new Finding
                {
                    Start = start,
                    Length = length,
                    // Une règle typographique de Grammalecte (sType) signale
                    // en TYPOGRAPHIE — filtre du panneau, couleur d'ondulé.
                    Category = GrammalecteOptions.IsTypography(error.Option)
                        ? FindingCategory.Typography : FindingCategory.Grammar,
                    Severity = FindingSeverity.Warning,
                    Message = error.Message,
                    RuleId = error.RuleId,
                    CheckerId = "grammar"
                };
                foreach (var suggestion in error.Suggestions)
                    finding.Suggestions.Add(suggestion);
                findings.Add(finding);
            }
            return findings;
        }
    }
}

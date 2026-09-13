using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversSale.Model;

namespace UniversSale.Correction.Grammalecte
{
    /// <summary>Le vérificateur de STYLE morphologique (batch 44) — deuxième
    /// étage du style après les répétitions : les ADVERBES EN -MENT et les
    /// VERBES TERNES. Catégorie Style, portée ParagraphLocal, exécution
    /// DIFFÉRÉE par le même pont que la grammaire : c'est le dictionnaire
    /// morphologique de Grammalecte qui tranche (« moment » n'est pas un
    /// adverbe, « fait » après un déterminant est un nom, « avait mangé »
    /// est un auxiliaire) — une terminaison ou une liste de formes en C#
    /// n'y arriverait pas sans lemmatisation. Rien de nouveau n'est
    /// embarqué : marabook_style.py exploite ce que le paquet contenait.
    /// Dégradation propre comme la grammaire : pont absent, il se tait.
    ///
    /// Les deux relevés sont des INDICES (Hint), jamais des fautes : un
    /// adverbe ou un « être » n'est pas une erreur, c'est un endroit où
    /// l'auteur peut regarder deux fois. Les suggestions (synonymes fléchis)
    /// sont servies À LA DEMANDE par SynonymProvider, jamais pendant la
    /// passe — même doctrine que l'orthographe.</summary>
    public class StyleChecker : IDeferredChecker
    {
        /// <summary>Les verbes ternes du roman, à peu près la liste
        /// d'Antidote — le défaut des réglages, modifiable par l'auteur.</summary>
        public static readonly string[] DefaultDullVerbs =
        {
            "être", "avoir", "faire", "dire", "mettre", "aller", "voir",
            "donner", "prendre", "pouvoir", "vouloir", "savoir", "falloir"
        };

        public const string AdverbRule = "style-adverb";
        public const string DullVerbRule = "style-dull-verb";

        private readonly GrammalecteBridge _bridge;

        public bool AdverbsEnabled = true;
        public bool DullVerbsEnabled = true;
        public List<string> DullVerbs = new List<string>(DefaultDullVerbs);

        public StyleChecker(GrammalecteBridge bridge)
        {
            _bridge = bridge;
        }

        public string Id { get { return "style"; } }
        public string Label { get { return "Style (adverbes, verbes ternes)"; } }
        public FindingCategory Category { get { return FindingCategory.Style; } }
        public CheckerScope Scope { get { return CheckerScope.ParagraphLocal; } }

        /// <summary>Vrai s'il reste quelque chose à relever — sinon le
        /// vérificateur n'a pas sa place dans le pilote.</summary>
        public bool Wanted { get { return AdverbsEnabled || DullVerbsEnabled; } }

        public List<Finding> Check(TextDocument document, StyleSheet styles)
        {
            throw new NotSupportedException("StyleChecker est différé.");
        }

        public List<Finding> CheckParagraph(TextParagraph paragraph, StyleSheet styles)
        {
            throw new NotSupportedException(
                "StyleChecker est différé : le pilote appelle CheckParagraphAsync.");
        }

        public async Task<List<Finding>> CheckParagraphAsync(
            TextParagraph paragraph, StyleSheet styles, CancellationToken token)
        {
            var flat = PivotEdit.FlatText(paragraph);
            if (flat.Trim().Length == 0 || !Wanted) return new List<Finding>();
            var mapper = OffsetMapper.Build(flat);
            var items = await _bridge.AnalyzeStyleAsync(mapper.Sent, BuildOptions(), token)
                .ConfigureAwait(false);
            return ToFindings(items, mapper, AdverbsEnabled, DullVerbsEnabled);
        }

        /// <summary>Le jeu d'options envoyé au pont — complet à chaque
        /// requête (le pont reste sans état, comme pour la grammaire).</summary>
        public Dictionary<string, object> BuildOptions()
        {
            var verbs = new List<object>();
            foreach (var verb in DullVerbs) verbs.Add(verb);
            return new Dictionary<string, object>
            {
                { "adverbs", AdverbsEnabled },
                { "dull", DullVerbsEnabled },
                { "dullVerbs", verbs }
            };
        }

        /// <summary>Les relevés du pont → des signalements aux offsets du
        /// PIVOT (via la carte). Statique et sans pont : la moitié testable
        /// en console (suite C22). Les interrupteurs filtrent aussi la
        /// sortie — une réponse partie avant un changement d'option ne
        /// passe pas outre.</summary>
        public static List<Finding> ToFindings(List<StyleItem> items,
            OffsetMapper mapper, bool adverbs, bool dullVerbs)
        {
            var findings = new List<Finding>();
            foreach (var item in items)
            {
                if (item.Kind == "adverb" && !adverbs) continue;
                if (item.Kind == "dull" && !dullVerbs) continue;
                int start;
                int length;
                if (!mapper.MapRange(item.Start, item.End, out start, out length)
                    || length <= 0)
                    continue; // borne aberrante : jetée, jamais un plantage
                var finding = new Finding
                {
                    Start = start,
                    Length = length,
                    Category = FindingCategory.Style,
                    Severity = FindingSeverity.Hint,
                    CheckerId = "style",
                    Word = item.Word
                };
                if (item.Kind == "adverb")
                {
                    finding.RuleId = AdverbRule;
                    finding.Message = "Adverbe en -ment : « " + item.Word + " »";
                    finding.Detail = "Les adverbes en -ment alourdissent la phrase ; "
                        + "un verbe plus précis ou une tournure directe les remplace souvent.";
                }
                else
                {
                    // Un verbe terne appelle des synonymes ; un adverbe non
                    // (un autre adverbe en -ment n'allégerait rien).
                    finding.Suggests = SuggestionSource.Synonyms;
                    finding.RuleId = DullVerbRule;
                    finding.Message = "Verbe terne : « " + item.Word + " »"
                        + (item.Lemma.Length > 0 && item.Lemma != item.Word
                            ? " (" + item.Lemma + ")" : "");
                    finding.Detail = "Un verbe passe-partout ; un verbe plus expressif "
                        + "dit davantage — les synonymes sont proposés au clic droit.";
                }
                findings.Add(finding);
            }
            return findings;
        }

        /// <summary>La liste des verbes ternes tapée par l'auteur (virgules,
        /// points-virgules, retours ou espaces) → une liste propre : en
        /// minuscules, sans doublon, sans vide.</summary>
        public static List<string> ParseDullVerbs(string text)
        {
            var verbs = new List<string>();
            if (string.IsNullOrEmpty(text)) return verbs;
            foreach (var raw in text.Split(new[] { ',', ';', '\n', '\r', '\t', ' ' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                var verb = raw.Trim().ToLowerInvariant();
                if (verb.Length == 0 || verbs.Contains(verb)) continue;
                verbs.Add(verb);
            }
            return verbs;
        }

        public static string JoinDullVerbs(List<string> verbs)
        {
            return verbs == null ? "" : string.Join(", ", verbs.ToArray());
        }
    }
}

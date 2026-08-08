using System.Collections.Generic;
using UniversSale.Model;

namespace UniversSale.Correction
{
    /// <summary>Le détecteur de répétitions — premier étage de l'analyse de
    /// style. Signale un mot qui réapparaît dans un rayon de N mots, fenêtre
    /// glissante TRAVERSANT les paragraphes. Depuis le batch 27, il consomme
    /// le TOKENISEUR UNIQUE : le cœur des tokens est comparé (sans élision
    /// ni pronom enclitique — « dit-il » compte pour « dit », qui est
    /// mot-outil : silence sur l'incise ; « répondit-elle » compte pour
    /// « répondit », signalé s'il se répète), les nombres et romains ne
    /// comptent jamais, la clé de comparaison est FrenchTokenizer.Fold
    /// (casse et accents pliés, cœur = coeur). L'ondulé se pose sur le CŒUR
    /// (le verbe, pas le pronom). Limite assumée : formes exactes — la
    /// lemmatisation (cheval/chevaux) attend le .dic.</summary>
    public class RepetitionChecker : IChecker
    {
        /// <summary>Le rayon en MOTS. Défaut 100 : sur un roman, 200 traque
        /// si large qu'il noie l'utile ; 100 signale ce qu'une oreille de
        /// relecteur entend, et le réglage reste ouvert à la hausse.</summary>
        public int Radius = 100;

        /// <summary>Jamais de signalement sous N lettres (clé pliée) —
        /// « va », « eu » : le bruit pur.</summary>
        public int MinLength = 3;

        public string Id { get { return "repetition"; } }
        public string Label { get { return "Répétitions"; } }
        public FindingCategory Category { get { return FindingCategory.Style; } }

        public List<Finding> Check(TextDocument document, StyleSheet styles)
        {
            var findings = new List<Finding>();
            var lastSeen = new Dictionary<string, int>();
            var wordIndex = 0;

            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var tokens = FrenchTokenizer.Tokenize(
                    PivotEdit.FlatText(document.Paragraphs[p]));
                foreach (var token in tokens)
                {
                    if (token.Kind != TokenKind.Word) continue;
                    var key = token.Folded;
                    if (key.Length < MinLength || FrenchStopWords.Contains(key))
                        continue;

                    wordIndex++;
                    int previous;
                    if (lastSeen.TryGetValue(key, out previous)
                        && wordIndex - previous <= Radius)
                    {
                        var distance = wordIndex - previous;
                        findings.Add(new Finding
                        {
                            ParagraphIndex = p,
                            Start = token.CoreStart,
                            Length = token.CoreLength,
                            Category = FindingCategory.Style,
                            Severity = FindingSeverity.Hint,
                            Message = "« " + token.CoreSurface + " » déjà employé "
                                + (distance == 1 ? "juste avant"
                                    : distance + " mots plus haut"),
                            RuleId = "repetition",
                            CheckerId = Id,
                            Word = token.CoreSurface
                        });
                    }
                    lastSeen[key] = wordIndex;
                }
            }
            return findings;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UniversSale.Model;

namespace UniversSale.Correction
{
    /// <summary>Le détecteur de répétitions — premier étage de l'analyse de
    /// style (l'axe où aucun outil libre français n'existe) et vérificateur
    /// de preuve de la chaîne complète du batch 26. Signale un mot qui
    /// réapparaît dans un rayon de N mots, fenêtre glissante TRAVERSANT les
    /// paragraphes. Comparaison insensible à la casse et aux accents
    /// (cœur = coeur = Coeur), affichage dans la casse d'origine. Les
    /// mots-outils français sont ignorés. Limite assumée : formes exactes
    /// seulement — la lemmatisation (cheval/chevaux) attend le .dic du
    /// batch 27, qui fournira les lemmes.</summary>
    public class RepetitionChecker : IChecker
    {
        /// <summary>Le rayon en MOTS (pas en caractères). Défaut 100 :
        /// sur un roman, 200 traque si large qu'il noie l'utile sous les
        /// faux positifs ; 100 signale ce qu'une oreille de relecteur
        /// entend, et le réglage reste ouvert à la hausse.</summary>
        public int Radius = 100;

        /// <summary>Ne jamais signaler les mots de moins de N lettres même
        /// hors liste (« va », « eu »…) — le bruit pur.</summary>
        public int MinLength = 3;

        public string Id { get { return "repetition"; } }
        public string Label { get { return "Répétitions"; } }
        public FindingCategory Category { get { return FindingCategory.Style; } }

        public List<Finding> Check(TextDocument document, StyleSheet styles)
        {
            var findings = new List<Finding>();
            // Dernière occurrence de chaque forme normalisée : index global de
            // mot (la fenêtre traverse les paragraphes) + position d'affichage.
            var lastSeen = new Dictionary<string, int>();
            var wordIndex = 0;

            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var text = PivotEdit.FlatText(document.Paragraphs[p]);
                var i = 0;
                while (i < text.Length)
                {
                    if (!IsWordChar(text[i])) { i++; continue; }
                    var start = i;
                    while (i < text.Length && IsWordChar(text[i])) i++;
                    // Apostrophes et traits d'union INTÉRIEURS prolongent le
                    // mot (aujourd'hui, porte-plume) — jamais en bordure.
                    while (i < text.Length - 1 && IsJoiner(text[i])
                        && IsWordChar(text[i + 1]))
                    {
                        i++;
                        while (i < text.Length && IsWordChar(text[i])) i++;
                    }
                    var word = text.Substring(start, i - start);
                    var key = StripElision(Normalize(word));
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
                            Start = start,
                            Length = word.Length,
                            Category = FindingCategory.Style,
                            Severity = FindingSeverity.Hint,
                            Message = "« " + word + " » déjà employé "
                                + (distance == 1 ? "juste avant"
                                    : distance + " mots plus haut"),
                            RuleId = "repetition",
                            CheckerId = Id,
                            Word = word
                        });
                    }
                    lastSeen[key] = wordIndex;
                }
            }
            return findings;
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c);
        }

        private static bool IsJoiner(char c)
        {
            return c == '\'' || c == '’' || c == '-';
        }

        // Préfixes d'élision français, du plus long au plus court : « l'homme »
        // et « homme » sont le même mot ; « aujourd'hui » reste entier.
        private static readonly string[] Elisions =
        {
            "jusqu'", "lorsqu'", "puisqu'", "quoiqu'", "qu'",
            "l'", "d'", "j'", "n'", "s'", "m'", "t'", "c'"
        };

        private static string StripElision(string key)
        {
            foreach (var prefix in Elisions)
                if (key.StartsWith(prefix, StringComparison.Ordinal)
                    && key.Length > prefix.Length)
                    return key.Substring(prefix.Length);
            return key;
        }

        /// <summary>Minuscules sans diacritiques (décomposition Unicode),
        /// œ→oe et æ→ae — la clé de comparaison, jamais affichée.</summary>
        public static string Normalize(string word)
        {
            var lowered = word.ToLowerInvariant()
                .Replace("œ", "oe").Replace("æ", "ae")
                .Replace("’", "'");
            var decomposed = lowered.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(c)
                    != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}

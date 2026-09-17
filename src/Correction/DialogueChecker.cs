using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Marabook.Model;

namespace Marabook.Correction
{
    /// <summary>Les VERBES DE DIALOGUE (b45) — les incises « dit-il »,
    /// « répondit-elle », « lança-t-il » qui suivent les répliques. Ce que
    /// l'auteur veut savoir, en mots simples : « le même verbe revient trop
    /// souvent dans mes dialogues ». Le vérificateur relève une incise dont
    /// le verbe (à la racine près : répondit/répondirent) a déjà servi dans
    /// les N incises précédentes — fenêtre en INCISES, pas en mots, car les
    /// répliques sont courtes et nombreuses. Global (WholeDocument) : les
    /// répliques traversent les paragraphes. Sans Python : l'incise se
    /// reconnaît à sa forme (verbe + trait d'union + pronom inversé), et la
    /// racine vient du moteur d'orthographe (Lemma) quand il est là.
    /// L'inventaire complet (Inventory) nourrit le bilan de style.</summary>
    public class DialogueChecker : IChecker
    {
        public const string Rule = "style-dialogue";

        /// <summary>Combien d'incises en arrière on regarde. Défaut 6 : deux
        /// « dit-il » à trois répliques d'écart, ça s'entend.</summary>
        public int Window = 6;

        /// <summary>La racine d'un mot (répondit → répondre), ou null si
        /// inconnue — brancher le moteur d'orthographe.</summary>
        public Func<string, string> Lemma;

        // verbe + (-t-)? + pronom inversé, en un seul mot avec traits d'union
        private static readonly Regex Incise = new Regex(
            @"(?<![\p{L}\-])(?<verb>\p{L}{2,})(?:-t)?-(?<pronoun>il|elle|ils|elles|on|je|tu|nous|vous)(?![\p{L}\-])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // ce qui ressemble à une incise sans en être une
        private static readonly HashSet<string> NotVerbs = new HashSet<string>
        {
            "peut", "va", "y", "là", "ci", "celui", "celle", "ceux", "celles",
            "vas", "allons", "allez", "soi", "moi", "toi", "lui", "eux"
        };

        public string Id { get { return "dialogue"; } }
        public string Label { get { return "Verbes de dialogue"; } }
        public FindingCategory Category { get { return FindingCategory.Style; } }
        public CheckerScope Scope { get { return CheckerScope.WholeDocument; } }

        public List<Finding> CheckParagraph(TextParagraph paragraph, StyleSheet styles)
        {
            throw new NotSupportedException("DialogueChecker est WholeDocument.");
        }

        public sealed class Incidence
        {
            public int Paragraph;
            public int Start;
            public int Length;
            public string Surface = "";  // « répondit »
            public string Key = "";      // la racine pliée
        }

        /// <summary>Toutes les incises du document, dans l'ordre.</summary>
        public List<Incidence> Collect(TextDocument document)
        {
            var list = new List<Incidence>();
            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var text = PivotEdit.FlatText(document.Paragraphs[p]);
                if (!LooksLikeDialogue(text)) continue;
                foreach (Match match in Incise.Matches(text))
                {
                    var verb = match.Groups["verb"];
                    var lower = verb.Value.ToLowerInvariant();
                    if (NotVerbs.Contains(lower)) continue;
                    list.Add(new Incidence
                    {
                        Paragraph = p,
                        Start = verb.Index,
                        Length = verb.Length,
                        Surface = verb.Value,
                        Key = KeyOf(verb.Value)
                    });
                }
            }
            return list;
        }

        /// <summary>Une réplique : tiret cadratin ou demi-cadratin en tête,
        /// ou des guillemets français dans le paragraphe.</summary>
        private static bool LooksLikeDialogue(string text)
        {
            var trimmed = text.TrimStart(' ', ' ', ' ', ' ');
            if (trimmed.StartsWith("—") || trimmed.StartsWith("–") || trimmed.StartsWith("- ")) return true;
            return text.IndexOf('«') >= 0 || text.IndexOf('»') >= 0;
        }

        private string KeyOf(string surface)
        {
            var lemma = Lemma == null ? null : Lemma(surface);
            return FrenchTokenizer.Fold(string.IsNullOrEmpty(lemma) ? surface : lemma);
        }

        public List<Finding> Check(TextDocument document, StyleSheet styles)
        {
            var findings = new List<Finding>();
            var incises = Collect(document);
            for (var i = 0; i < incises.Count; i++)
            {
                var current = incises[i];
                for (var back = Math.Max(0, i - Window); back < i; back++)
                {
                    if (incises[back].Key != current.Key) continue;
                    var distance = i - back;
                    findings.Add(new Finding
                    {
                        ParagraphIndex = current.Paragraph,
                        Start = current.Start,
                        Length = current.Length,
                        Category = FindingCategory.Style,
                        Severity = FindingSeverity.Hint,
                        Message = "Verbe de dialogue « " + current.Surface + " » déjà employé "
                            + (distance == 1 ? "dans l'incise précédente" : distance + " incises plus haut"),
                        Detail = "Le même verbe revient dans les incises (« dit-il », « répondit-elle »…) : "
                            + "varier, ou laisser la réplique sans incise quand on sait qui parle.",
                        RuleId = Rule,
                        CheckerId = Id,
                        Word = current.Surface,
                        Suggests = SuggestionSource.Synonyms
                    });
                    break;
                }
            }
            return findings;
        }

        /// <summary>L'inventaire des verbes de dialogue : racine → nombre,
        /// avec la forme la plus fréquente — pour le bilan de style.</summary>
        public List<KeyValuePair<string, int>> Inventory(TextDocument document)
        {
            var counts = new Dictionary<string, int>();
            var faces = new Dictionary<string, Dictionary<string, int>>();
            foreach (var incise in Collect(document))
            {
                int n;
                counts.TryGetValue(incise.Key, out n);
                counts[incise.Key] = n + 1;
                Dictionary<string, int> forms;
                if (!faces.TryGetValue(incise.Key, out forms))
                    faces[incise.Key] = forms = new Dictionary<string, int>();
                var surface = incise.Surface.ToLowerInvariant();
                int m;
                forms.TryGetValue(surface, out m);
                forms[surface] = m + 1;
            }
            var result = new List<KeyValuePair<string, int>>();
            foreach (var pair in counts)
            {
                var best = "";
                var bestCount = -1;
                foreach (var form in faces[pair.Key])
                    if (form.Value > bestCount) { best = form.Key; bestCount = form.Value; }
                result.Add(new KeyValuePair<string, int>(best, pair.Value));
            }
            result.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                var byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
            });
            return result;
        }
    }
}

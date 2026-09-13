using System;
using System.Collections.Generic;
using System.Text;
using UniversSale.Model;

namespace UniversSale.Correction
{
    /// <summary>Le BILAN DE STYLE d'un écrit (b45) — des mesures qu'un auteur
    /// comprend sans vocabulaire de linguiste : combien de phrases, de quelle
    /// longueur, où le rythme devient monotone, la place du dialogue, la
    /// variété des mots, les tics relevés (adverbes en -ment, verbes ternes,
    /// répétitions, verbes de dialogue) et les paragraphes à revoir. Calcul
    /// pur, sans WPF (testé en C23) ; les relevés différés (adverbes, verbes
    /// ternes) sont ceux de la dernière passe du pilote, tels que fournis.</summary>
    public sealed class StyleReport
    {
        public sealed class Section
        {
            public string Title = "";
            public List<string> Lines = new List<string>();
            public bool Good; // un point fort mesuré
        }

        public sealed class MonotonousRun
        {
            public int Paragraph;
            public int Sentences;
            public int Words; // longueur commune approximative
        }

        public int Words;
        public int Sentences;
        public double AverageSentence;
        public int LongestSentence;
        public string LongestExcerpt = "";
        public int ShortestSentence;
        public List<MonotonousRun> Monotonous = new List<MonotonousRun>();
        public double DialogueShare;      // 0..1, en caractères
        public int DistinctPerThousand;   // mots différents pour 1 000 mots (racines)
        public int Adverbs;
        public int DullVerbs;
        public int Repetitions;
        public int DialogueVerbFindings;
        public List<KeyValuePair<string, int>> TopAdverbs = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<string, int>> TopDullVerbs = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<string, int>> TopRepeated = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<string, int>> DialogueVerbs = new List<KeyValuePair<string, int>>();
        public List<KeyValuePair<int, int>> ParagraphsToReview = new List<KeyValuePair<int, int>>(); // (paragraphe, relevés)
        public bool DeferredPending; // adverbes/verbes ternes pas encore tous arrivés

        /// <summary>Le calcul. findings : la dernière passe du pilote (peut
        /// être vide) ; lemma : la racine d'un mot (ou null) ; dialogueVerbs :
        /// l'inventaire du DialogueChecker (peut être null).</summary>
        public static StyleReport Compute(TextDocument document, List<Finding> findings,
            Func<string, string> lemma, List<KeyValuePair<string, int>> dialogueVerbs)
        {
            var report = new StyleReport();
            var stems = new Dictionary<string, int>();
            var sentenceLengths = new List<int>();
            var sentenceParagraph = new List<int>();
            var totalChars = 0;
            var dialogueChars = 0;
            var wordsSoFar = 0;

            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var text = PivotEdit.FlatText(document.Paragraphs[p]);
                if (text.Trim().Length == 0) continue;
                totalChars += text.Length;
                if (IsDialogue(text)) dialogueChars += text.Length;
                foreach (var sentence in SplitSentences(text))
                {
                    var count = 0;
                    foreach (var token in FrenchTokenizer.Tokenize(sentence))
                    {
                        if (token.Kind != TokenKind.Word) continue;
                        count++;
                        // la richesse se mesure sur les 1 000 premiers mots,
                        // pour comparer des écrits de longueurs différentes
                        if (wordsSoFar < 1000)
                        {
                            var root = lemma == null ? null : lemma(token.CoreSurface);
                            var key = FrenchTokenizer.Fold(string.IsNullOrEmpty(root) ? token.CoreSurface : root);
                            int n;
                            stems.TryGetValue(key, out n);
                            stems[key] = n + 1;
                        }
                        wordsSoFar++;
                    }
                    if (count == 0) continue;
                    sentenceLengths.Add(count);
                    sentenceParagraph.Add(p);
                    if (count > report.LongestSentence)
                    {
                        report.LongestSentence = count;
                        report.LongestExcerpt = Excerpt(sentence, 90);
                    }
                    if (report.ShortestSentence == 0 || count < report.ShortestSentence)
                        report.ShortestSentence = count;
                }
            }
            report.Words = wordsSoFar;
            report.Sentences = sentenceLengths.Count;
            if (report.Sentences > 0)
            {
                var sum = 0;
                foreach (var n in sentenceLengths) sum += n;
                report.AverageSentence = Math.Round(sum / (double)report.Sentences, 1);
            }
            report.DialogueShare = totalChars == 0 ? 0 : dialogueChars / (double)totalChars;
            var sampled = Math.Min(1000, wordsSoFar);
            report.DistinctPerThousand = sampled == 0 ? 0
                : (int)Math.Round(stems.Count * 1000.0 / sampled);

            // Le rythme : quatre phrases de suite (ou plus) de longueur
            // presque égale (à 20 % près, et jamais sous 6 mots).
            var runStart = 0;
            for (var i = 1; i <= sentenceLengths.Count; i++)
            {
                var continues = i < sentenceLengths.Count
                    && Similar(sentenceLengths[i], sentenceLengths[runStart]);
                if (continues) continue;
                var length = i - runStart;
                if (length >= 4 && sentenceLengths[runStart] >= 6)
                    report.Monotonous.Add(new MonotonousRun
                    {
                        Paragraph = sentenceParagraph[runStart],
                        Sentences = length,
                        Words = sentenceLengths[runStart]
                    });
                runStart = i;
            }

            // Les relevés de la dernière passe.
            var adverbs = new Dictionary<string, int>();
            var dull = new Dictionary<string, int>();
            var repeated = new Dictionary<string, int>();
            var perParagraph = new Dictionary<int, int>();
            if (findings != null)
                foreach (var finding in findings)
                {
                    if (finding.Category != FindingCategory.Style) continue;
                    int n;
                    perParagraph.TryGetValue(finding.ParagraphIndex, out n);
                    perParagraph[finding.ParagraphIndex] = n + 1;
                    var word = FrenchTokenizer.Fold(finding.Word);
                    if (finding.RuleId == "style-adverb") { report.Adverbs++; Bump(adverbs, word); }
                    else if (finding.RuleId == "style-dull-verb")
                    {
                        report.DullVerbs++;
                        var root = lemma == null ? null : lemma(finding.Word);
                        Bump(dull, FrenchTokenizer.Fold(string.IsNullOrEmpty(root) ? finding.Word : root));
                    }
                    else if (finding.RuleId == "repetition") { report.Repetitions++; Bump(repeated, word); }
                    else if (finding.RuleId == DialogueChecker.Rule) report.DialogueVerbFindings++;
                }
            report.TopAdverbs = Top(adverbs, 5);
            report.TopDullVerbs = Top(dull, 5);
            report.TopRepeated = Top(repeated, 8);
            if (dialogueVerbs != null) report.DialogueVerbs = dialogueVerbs;
            var review = new List<KeyValuePair<int, int>>();
            foreach (var pair in perParagraph)
                if (pair.Value >= 3) review.Add(new KeyValuePair<int, int>(pair.Key, pair.Value));
            review.Sort(delegate(KeyValuePair<int, int> a, KeyValuePair<int, int> b)
            {
                var byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : a.Key.CompareTo(b.Key);
            });
            if (review.Count > 5) review.RemoveRange(5, review.Count - 5);
            report.ParagraphsToReview = review;
            return report;
        }

        private static bool Similar(int a, int b)
        {
            var max = Math.Max(a, b);
            return max == 0 || Math.Abs(a - b) <= Math.Max(1, max * 0.2);
        }

        private static void Bump(Dictionary<string, int> counts, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int n;
            counts.TryGetValue(key, out n);
            counts[key] = n + 1;
        }

        private static List<KeyValuePair<string, int>> Top(Dictionary<string, int> counts, int cap)
        {
            var list = new List<KeyValuePair<string, int>>(counts);
            list.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                var byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
            });
            if (list.Count > cap) list.RemoveRange(cap, list.Count - cap);
            return list;
        }

        private static bool IsDialogue(string text)
        {
            var trimmed = text.TrimStart(' ', ' ', ' ', ' ');
            return trimmed.StartsWith("—") || trimmed.StartsWith("–") || trimmed.StartsWith("- ")
                || text.IndexOf('«') >= 0;
        }

        /// <summary>Découpe en phrases : un point, un point d'exclamation ou
        /// d'interrogation, des points de suspension, suivis d'un blanc ou de
        /// la fin. « M. », « Mme », « etc. » et une initiale ne coupent pas.</summary>
        public static List<string> SplitSentences(string text)
        {
            var sentences = new List<string>();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c != '.' && c != '!' && c != '?' && c != '…') continue;
                var end = i + 1;
                // Les signes qui prolongent la fin de phrase : d'autres
                // points, une fermante — précédée ou non d'un blanc (« ! »
                // à la française : espace, puis »).
                while (end < text.Length)
                {
                    var c2 = text[end];
                    if (c2 == '.' || c2 == '!' || c2 == '?' || c2 == '…' || c2 == '»' || c2 == '"' || c2 == ')')
                    { end++; continue; }
                    if (char.IsWhiteSpace(c2))
                    {
                        var after = end + 1;
                        while (after < text.Length && char.IsWhiteSpace(text[after])) after++;
                        if (after < text.Length && (text[after] == '»' || text[after] == '"' || text[after] == ')'))
                        { end = after + 1; continue; }
                    }
                    break;
                }
                if (end < text.Length && !char.IsWhiteSpace(text[end])) continue;
                if (c == '.' && IsAbbreviation(text, i)) continue;
                var sentence = text.Substring(start, end - start).Trim();
                if (sentence.Length > 0) sentences.Add(sentence);
                start = end;
                i = end - 1;
            }
            var tail = text.Substring(start).Trim();
            if (tail.Length > 0) sentences.Add(tail);
            return sentences;
        }

        private static readonly string[] Abbreviations = { "M", "Mme", "Mlle", "Dr", "Pr", "etc", "cf", "p", "St", "Ste" };

        private static bool IsAbbreviation(string text, int dot)
        {
            var start = dot;
            while (start > 0 && char.IsLetter(text[start - 1])) start--;
            var word = text.Substring(start, dot - start);
            if (word.Length == 1 && char.IsUpper(word[0])) return true; // initiale
            foreach (var abbreviation in Abbreviations)
                if (string.Equals(word, abbreviation, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Excerpt(string sentence, int max)
        {
            sentence = sentence.Trim();
            return sentence.Length <= max ? sentence : sentence.Substring(0, max).TrimEnd() + "…";
        }

        // ------------------------------------------------- en mots simples

        /// <summary>Le bilan raconté : des sections, des phrases courtes,
        /// aucun terme de linguiste. Ce que la fenêtre affiche.</summary>
        public List<Section> Narrate()
        {
            var sections = new List<Section>();

            var size = new Section { Title = "La taille" };
            size.Lines.Add(Words + " mots, " + Sentences + " phrases.");
            if (Sentences > 0)
            {
                size.Lines.Add("Une phrase fait en moyenne " + AverageSentence + " mots ; la plus courte en fait "
                    + ShortestSentence + ", la plus longue " + LongestSentence + ".");
                if (LongestSentence >= 45)
                    size.Lines.Add("La plus longue mérite un œil : « " + LongestExcerpt + " »");
            }
            sections.Add(size);

            var rhythm = new Section { Title = "Le rythme" };
            if (Sentences < 8)
                rhythm.Lines.Add("Trop court pour juger le rythme.");
            else if (Monotonous.Count == 0)
            {
                rhythm.Good = true;
                rhythm.Lines.Add("Les phrases alternent bien les longueurs : aucune suite de phrases toutes de la même taille.");
            }
            else
            {
                rhythm.Lines.Add(Monotonous.Count + (Monotonous.Count == 1
                    ? " endroit où quatre phrases de suite (ou plus) font à peu près la même longueur — ça ronronne :"
                    : " endroits où quatre phrases de suite (ou plus) font à peu près la même longueur — ça ronronne :"));
                foreach (var run in Monotonous)
                    rhythm.Lines.Add("• paragraphe " + (run.Paragraph + 1) + " : " + run.Sentences
                        + " phrases d'environ " + run.Words + " mots.");
            }
            sections.Add(rhythm);

            var dialogue = new Section { Title = "Le dialogue" };
            var share = (int)Math.Round(DialogueShare * 100);
            dialogue.Lines.Add(share == 0 ? "Pas de dialogue dans cet écrit."
                : "Le dialogue occupe environ " + share + " % du texte.");
            if (DialogueVerbs.Count > 0)
            {
                var parts = new List<string>();
                for (var i = 0; i < DialogueVerbs.Count && i < 6; i++)
                    parts.Add(DialogueVerbs[i].Key + " (" + DialogueVerbs[i].Value + ")");
                dialogue.Lines.Add("Les verbes des incises, du plus fréquent au plus rare : " + string.Join(", ", parts.ToArray()) + ".");
                if (DialogueVerbFindings > 0)
                    dialogue.Lines.Add(DialogueVerbFindings + (DialogueVerbFindings == 1
                        ? " incise reprend un verbe déjà employé juste avant."
                        : " incises reprennent un verbe déjà employé juste avant."));
                else { dialogue.Good = true; dialogue.Lines.Add("Les incises varient : rien à redire."); }
            }
            sections.Add(dialogue);

            var words = new Section { Title = "Les mots" };
            if (Words >= 200)
            {
                words.Lines.Add("Sur 1 000 mots, " + DistinctPerThousand + " sont différents"
                    + (Words < 1000 ? " (ramené à 1 000 : l'écrit en compte " + Words + ")" : "") + ".");
                if (DistinctPerThousand >= 420) { words.Good = true; words.Lines.Add("C'est un vocabulaire varié."); }
                else if (DistinctPerThousand < 300) words.Lines.Add("C'est peu : les mêmes mots reviennent beaucoup.");
            }
            else words.Lines.Add("Trop court pour juger la variété des mots.");
            if (TopRepeated.Count > 0)
                words.Lines.Add("Répétitions relevées : " + Repetitions + ". Les mots qui reviennent le plus : "
                    + Join(TopRepeated) + ".");
            else words.Lines.Add("Aucune répétition relevée.");
            sections.Add(words);

            var tics = new Section { Title = "Les tics" };
            if (DeferredPending) tics.Lines.Add("L'analyse des adverbes et des verbes ternes est encore en cours : revenez dans un instant.");
            tics.Lines.Add(Adverbs == 0 ? "Aucun adverbe en -ment relevé (les « rapidement », « vraiment »…)."
                : Adverbs + (Adverbs == 1 ? " adverbe en -ment" : " adverbes en -ment") + " (les « rapidement », « vraiment »…)"
                    + (Words > 0 ? ", soit " + Math.Round(Adverbs * 1000.0 / Words, 1) + " pour 1 000 mots" : "")
                    + ". Les plus fréquents : " + Join(TopAdverbs) + ".");
            tics.Lines.Add(DullVerbs == 0 ? "Aucun verbe terne relevé (les passe-partout : être, avoir, faire, dire…)."
                : DullVerbs + (DullVerbs == 1 ? " verbe terne" : " verbes ternes") + " (les passe-partout : être, avoir, faire, dire…)"
                    + (Words > 0 ? ", soit " + Math.Round(DullVerbs * 1000.0 / Words, 1) + " pour 1 000 mots" : "")
                    + ". Les plus fréquents : " + Join(TopDullVerbs) + ".");
            if (Words > 0 && Adverbs * 1000.0 / Words < 4 && DullVerbs * 1000.0 / Words < 15 && !DeferredPending)
            {
                tics.Good = true;
                tics.Lines.Add("Peu de tics : le texte est tenu.");
            }
            sections.Add(tics);

            var review = new Section { Title = "À revoir en premier" };
            if (ParagraphsToReview.Count == 0)
                review.Lines.Add("Aucun paragraphe ne concentre les relevés.");
            else
                foreach (var pair in ParagraphsToReview)
                    review.Lines.Add("• paragraphe " + (pair.Key + 1) + " : " + pair.Value + " relevés de style.");
            sections.Add(review);
            return sections;
        }

        private static string Join(List<KeyValuePair<string, int>> pairs)
        {
            var parts = new List<string>();
            foreach (var pair in pairs) parts.Add(pair.Key + " (" + pair.Value + ")");
            return parts.Count == 0 ? "—" : string.Join(", ", parts.ToArray());
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            foreach (var section in Narrate())
            {
                sb.AppendLine(section.Title.ToUpperInvariant());
                foreach (var line in section.Lines) sb.AppendLine(line);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}

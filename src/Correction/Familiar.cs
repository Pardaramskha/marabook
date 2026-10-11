using System.Collections.Generic;
using System.Text.RegularExpressions;
using Marabook.Model;

namespace Marabook.Correction
{
    /// <summary>LE FAMILIER (1.0.5, Rémi) : les élisions et contractions de
    /// l'oral que le romancier pose exprès dans ses dialogues — et que
    /// l'orthographe ne doit pas rougir. Relevées dans leur catégorie, en
    /// indice, jamais en faute :
    /// — « y’a », « y’avait » (il y a) ;
    /// — « t’as », « t’es », « t’étais »… (tu + être/avoir) ;
    /// — une élision devant CONSONNE : « j’suis », « j’sais », « m’sieur »,
    ///   « p’tit », « v’là », « r’garde », « d’jà », « qu’tu » (l'élision
    ///   régulière ne se fait que devant voyelle ou h muet) ;
    /// — une aphérèse : « ’tain », « ’videmment », « ’spèce » ;
    /// — les soudures : « chuis », « chais », « chépa ».
    /// Une seule définition des plages (Spans) : le vérificateur les relève,
    /// l'orthographe les tait.</summary>
    public static class Familiar
    {
        public const string Rule = "familiar";

        private const string Apostrophe = "[’']";
        private const string Vowel = "aàâeéèêëiîïoôuùûüyœæhAÀÂEÉÈÊËIÎÏOÔUÙÛÜYŒÆH";
        private static readonly Regex Pattern = new Regex(
            // y’a, y’avait, y’aura…
            @"(?<![\p{L}’'])(?:[yY]" + Apostrophe + @"(?:a|avait|aura|aurait|ait|avaient|auront|en)" +
            // t’as, t’es, t’étais, t’avais, t’auras, t’aurais, t’iras…
            @"|[tT]" + Apostrophe + @"(?:as|es|étais|étions|avais|auras|aurais|aies|iras|irais|sais|peux|veux|fais|dis|vas|vois|crois|inquiète|inquiètes)" +
            // une élision devant consonne : j’suis, m’sieur, p’tit, v’là, r’garde, d’jà, qu’tu, l’tien
            @"|(?:[jJmMpPvVrRdDtTlLnNsScC]|[qQ]u)" + Apostrophe + @"(?=[^\s" + Vowel + @"\p{P}\p{N}])\p{L}+" +
            // une aphérèse : ’tain, ’videmment, ’spèce
            @"|(?<=^|[\s(«“—–])" + Apostrophe + @"\p{L}{2,}" +
            // les soudures
            @"|[cC]huis|[cC]hais|[cC]hépa|[cC]hépas" +
            @")(?![\p{L}’'])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Les plages familières d'un texte plat, dans l'ordre.</summary>
        public static List<Match> Spans(string text)
        {
            var result = new List<Match>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (Match match in Pattern.Matches(text)) result.Add(match);
            return result;
        }

        /// <summary>Une plage [start, end) touche-t-elle une forme familière ?</summary>
        public static bool Covers(List<Match> spans, int start, int end)
        {
            foreach (var span in spans)
                if (start < span.Index + span.Length && end > span.Index) return true;
            return false;
        }
    }

    /// <summary>Le vérificateur du familier : un signalement en indice par
    /// forme relevée (Familiar.Spans), locale au paragraphe.</summary>
    public class FamiliarChecker : IChecker
    {
        public string Id { get { return Familiar.Rule; } }
        public string Label { get { return "Familier"; } }
        public FindingCategory Category { get { return FindingCategory.Familiar; } }
        public CheckerScope Scope { get { return CheckerScope.ParagraphLocal; } }

        public List<Finding> Check(TextDocument document, StyleSheet styles)
        {
            throw new System.NotSupportedException("FamiliarChecker est ParagraphLocal : le pilote appelle CheckParagraph.");
        }

        public List<Finding> CheckParagraph(TextParagraph paragraph, StyleSheet styles)
        {
            return CheckText(PivotEdit.FlatText(paragraph));
        }

        public List<Finding> CheckText(string text)
        {
            var findings = new List<Finding>();
            foreach (var span in Familiar.Spans(text))
                findings.Add(new Finding
                {
                    Start = span.Index,
                    Length = span.Length,
                    Category = FindingCategory.Familiar,
                    Severity = FindingSeverity.Hint,
                    Message = "« " + span.Value + " » : contraction familière (l'oral)",
                    RuleId = Familiar.Rule,
                    CheckerId = Id,
                    Word = span.Value
                });
            return findings;
        }
    }
}

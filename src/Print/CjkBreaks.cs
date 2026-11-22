using System.Collections.Generic;

namespace Marabook.Print
{
    /// <summary>OÙ COUPER UN TEXTE EN IDÉOGRAMMES (21/11/2026). Le chinois et
    /// le japonais s'écrivent sans espaces : la ligne peut se couper entre
    /// deux signes — sauf devant une ponctuation fermante (、。」…), qui ne
    /// commence jamais une ligne, et après une ponctuation ouvrante (「（…),
    /// qui n'en finit jamais une. C'est la règle de base (kinsoku), pas toute
    /// la norme : pas de traitement des petits kana ni des nombres.
    /// Le coréen, qui sépare ses mots par des espaces, n'est pas découpé.</summary>
    public static class CjkBreaks
    {
        private const string NoBreakBefore = "、。，．：；！？）］｝」』】〕〉》〗〙〟’”ー・…‥々〜";
        private const string NoBreakAfter = "（［｛「『【〔〈《〖〘〝‘“";

        /// <summary>Un signe entre lesquels la ligne peut se couper : kana,
        /// idéogrammes, ponctuation et formes pleine chasse.</summary>
        public static bool IsIdeographic(char c)
        {
            return (c >= '⺀' && c <= '鿿')      // radicaux, ponctuation CJC, hiragana, katakana, idéogrammes unifiés
                || (c >= '豈' && c <= '﫿')      // idéogrammes de compatibilité
                || (c >= '＀' && c <= '￯')      // formes pleine chasse et demi-chasse
                || char.IsSurrogate(c);                   // les extensions (plans supplémentaires), par paires
        }

        public static bool Contains(string text)
        {
            if (text == null) return false;
            foreach (var c in text) if (c >= '⺀' && IsIdeographic(c) && !char.IsSurrogate(c)) return true;
            return false;
        }

        /// <summary>Les débuts des tronçons d'un mot : [0, …]. Null si le mot
        /// ne porte aucun idéogramme (il reste un seul atome, comme avant).</summary>
        public static List<int> Split(string word)
        {
            if (!Contains(word)) return null;
            var starts = new List<int> { 0 };
            for (var i = 1; i < word.Length; i++)
            {
                var c = word[i];
                var previous = word[i - 1];
                if (char.IsLowSurrogate(c)) continue;                           // jamais au milieu d'une paire
                if (!IsIdeographic(c) && !IsIdeographic(previous)) continue;     // du latin dans le mot : il reste entier
                if (NoBreakBefore.IndexOf(c) >= 0) continue;                     // une fermante suit son signe
                if (NoBreakAfter.IndexOf(previous) >= 0) continue;               // une ouvrante garde le sien
                starts.Add(i);
            }
            return starts.Count == 1 ? null : starts;
        }
    }
}

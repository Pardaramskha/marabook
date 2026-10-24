using System;
using System.Globalization;

namespace Marabook.Correction
{
    /// <summary>La MAJUSCULE AUTOMATIQUE en début de phrase (24/10, 1.0.6) :
    /// un mot tapé sans majuscule en tête de paragraphe, de réplique ou après
    /// une ponctuation de fin de phrase (. ! ? …) prend sa capitale, accents
    /// compris (é → É), quand l'auteur le termine : espace, ponctuation ou
    /// retour à la ligne (pas lettre à lettre — la capitale doit rester le
    /// DERNIER cran d'annulation, pour que Ctrl+Z la refuse sans défaire la
    /// frappe). Vaut aussi après la fermeture d'une réplique (« ! » dit-il) —
    /// c'est à Ctrl+Z de refuser la capitale d'une incise ; le refus tient
    /// tant que la lettre reste là.</summary>
    public static class AutoCapital
    {
        /// <summary>L'offset de la lettre à passer en capitale après une
        /// frappe qui laisse le caret à <paramref name="caret"/>, ou -1 si
        /// rien ne change ; <paramref name="upper"/> reçoit la capitale.</summary>
        public static int Find(string flat, int caret, out char upper)
        {
            upper = '\0';
            if (flat == null || caret <= 0 || caret > flat.Length) return -1;
            var i = caret - 1;
            // Le mot se termine : un séparateur vient d'être tapé, le mot est
            // juste avant lui. En cours de frappe (une lettre), rien encore.
            if (IsWordChar(flat[i])) return -1;
            i--;
            if (i < 0 || !IsWordChar(flat[i])) return -1;
            var start = i;
            while (start > 0 && IsWordChar(flat[start - 1])) start--;
            var first = flat[start];
            if (!char.IsLower(first)) return -1;
            if (!SentenceStart(flat, start)) return -1;
            upper = char.ToUpper(first, CultureInfo.InvariantCulture);
            return upper == first ? -1 : start;
        }

        /// <summary>Lettre, chiffre, apostrophe ou trait d'union intérieur :
        /// « l'été » et « peut-être » sont un mot.</summary>
        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '\'' || c == '’' || c == '-';
        }

        /// <summary>Le mot ouvre-t-il une phrase ? Derrière lui, on saute les
        /// espaces, les guillemets, tirets et parenthèses ; le début du
        /// paragraphe ou une ponctuation de fin de phrase disent oui.</summary>
        private static bool SentenceStart(string flat, int start)
        {
            var j = start - 1;
            while (j >= 0)
            {
                var c = flat[j];
                if (c == ' ' || c == ' ' || c == ' ' || c == '\t' || c == '\n'
                    || c == '«' || c == '»' || c == '"' || c == '“' || c == '”' || c == '‘' || c == '’' || c == '\''
                    || c == '—' || c == '–' || c == '-' || c == '(' || c == ')' || c == '[' || c == ']')
                {
                    j--;
                    continue;
                }
                return c == '.' || c == '!' || c == '?' || c == '…';
            }
            return true;
        }
    }
}

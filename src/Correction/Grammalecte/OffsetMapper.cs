using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Marabook.Correction.Grammalecte
{
    /// <summary>La correspondance d'offsets entre le pivot et Grammalecte
    /// (batch 29, lot B.2) — le point dur du pont, traité explicitement :
    ///
    /// 1. NORMALISATION. Les règles de Grammalecte supposent du précomposé
    ///    et son moteur ne normalise PAS (le unicodedata.normalize de
    ///    gc_engine est commenté dans le source 2.3.0). On envoie donc du
    ///    NFC — mais le pivot peut être en NFD (import ODT, texte macOS) :
    ///    chaque offset rendu doit revenir aux unités du texte D'ORIGINE.
    /// 2. POINTS DE CODE contre UTF-16. Python compte les chaînes en points
    ///    de code, C# en unités UTF-16 : un caractère astral (emoji…) avant
    ///    l'erreur décale tout d'une unité si on l'oublie. La carte est
    ///    construite par point de code envoyé, valeur en unités d'origine.
    /// 3. U+FFFC. Les éléments (image, filet, note) partent TELS QUELS : un
    ///    point de code, aucun remplacement d'une autre longueur.
    ///
    /// Construction : le texte d'origine est découpé en séquences stables
    /// (un caractère de base + ses marques combinantes, paires de
    /// substitution entières), chaque séquence est normalisée NFC seule et
    /// tous ses points de code envoyés pointent vers le DÉBUT de la séquence
    /// d'origine — une borne (nStart ou nEnd) tombe ainsi toujours sur une
    /// frontière de caractère complète, marques comprises. Sans WPF,
    /// testable en console (C9) sur des réponses enregistrées en dur.</summary>
    public sealed class OffsetMapper
    {
        /// <summary>Le texte réellement envoyé au pont (NFC).</summary>
        public string Sent = "";

        // Par POINT DE CODE de Sent, l'offset UTF-16 du début de la séquence
        // d'origine dont il provient ; une sentinelle finale porte la
        // longueur totale d'origine (pour nEnd exclusif).
        private int[] _origin;

        public static OffsetMapper Build(string original)
        {
            var mapper = new OffsetMapper();
            if (string.IsNullOrEmpty(original))
            {
                mapper._origin = new[] { 0 };
                return mapper;
            }
            var sent = new StringBuilder(original.Length);
            var origin = new List<int>(original.Length + 1);
            var i = 0;
            while (i < original.Length)
            {
                // La séquence stable : le caractère de base (paire de
                // substitution entière) suivi de ses marques combinantes.
                var runStart = i;
                i += char.IsHighSurrogate(original[i])
                    && i + 1 < original.Length
                    && char.IsLowSurrogate(original[i + 1]) ? 2 : 1;
                while (i < original.Length && IsCombining(original[i])) i++;
                var run = original.Substring(runStart, i - runStart);
                string normalized;
                try { normalized = run.Normalize(NormalizationForm.FormC); }
                catch (ArgumentException) { normalized = run; } // séquence invalide : telle quelle
                sent.Append(normalized);
                // Un entrée de carte PAR POINT DE CODE envoyé.
                var k = 0;
                while (k < normalized.Length)
                {
                    origin.Add(runStart);
                    k += char.IsHighSurrogate(normalized[k])
                        && k + 1 < normalized.Length
                        && char.IsLowSurrogate(normalized[k + 1]) ? 2 : 1;
                }
            }
            origin.Add(original.Length); // la sentinelle de fin
            mapper.Sent = sent.ToString();
            mapper._origin = origin.ToArray();
            return mapper;
        }

        private static bool IsCombining(char c)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpacingCombiningMark
                || category == UnicodeCategory.EnclosingMark;
        }

        /// <summary>Reporte une plage [nStart, nEnd) en POINTS DE CODE du
        /// texte envoyé vers les unités UTF-16 du texte d'origine. Rend faux
        /// (et une plage vide) si les bornes sont hors du texte — une réponse
        /// aberrante du pont vaut « pas de signalement », jamais un plantage.</summary>
        public bool MapRange(int nStart, int nEnd, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (nStart < 0 || nEnd < nStart || nEnd > _origin.Length - 1)
                return false;
            start = _origin[nStart];
            length = _origin[nEnd] - start;
            return length >= 0;
        }
    }
}

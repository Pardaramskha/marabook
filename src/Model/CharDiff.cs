using System;
using System.Collections.Generic;

namespace UniversSale.Model
{
    // Extrait de Correction/Typography.cs au batch 38 (lot A) : un
    // utilitaire général — le diff de caractères sert à la passe
    // typographique (redistribution sur les runs) ET à la comparaison de
    // versions (DocumentDiff, au second étage : les paragraphes modifiés
    // seulement). Comportement inchangé : Myers, plafond de 800 éditions,
    // null au-delà (C13 le prouve).

    /// <summary>Une opération du diff de caractères : ' ' conservé, '-'
    /// supprimé, '+' inséré.</summary>
    public struct CharOp
    {
        public char Type;
        public char Char;
        public CharOp(char type, char c) { Type = type; Char = c; }
    }

    /// <summary>Diff de caractères (Myers) — le pont entre le texte corrigé
    /// et les runs formatés du pivot. Null au-delà de 800 éditions.</summary>
    public static class CharDiff
    {
        public static List<CharOp> Diff(string a, string b)
        {
            var n = a.Length;
            var m = b.Length;
            var max = n + m;
            if (max == 0) return new List<CharOp>();
            var limit = Math.Min(max, 800);
            var offset = max;
            var v = new int[2 * max + 2];
            var trace = new List<int[]>();
            for (var d = 0; d <= limit; d++)
            {
                trace.Add((int[])v.Clone());
                for (var k = -d; k <= d; k += 2)
                {
                    int x;
                    if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])) x = v[offset + k + 1];
                    else x = v[offset + k - 1] + 1;
                    var y = x - k;
                    while (x < n && y < m && a[x] == b[y]) { x++; y++; }
                    v[offset + k] = x;
                    if (x >= n && y >= m) return Backtrack(a, b, trace, offset);
                }
            }
            return null;
        }

        private static List<CharOp> Backtrack(string a, string b, List<int[]> trace, int offset)
        {
            var ops = new List<CharOp>();
            var x = a.Length;
            var y = b.Length;
            for (var d = trace.Count - 1; d >= 0; d--)
            {
                var v = trace[d];
                var k = x - y;
                int prevK;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])) prevK = k + 1;
                else prevK = k - 1;
                var prevX = v[offset + prevK];
                var prevY = prevX - prevK;
                while (x > prevX && y > prevY) { ops.Add(new CharOp(' ', a[x - 1])); x--; y--; }
                if (d > 0)
                {
                    if (x == prevX) { ops.Add(new CharOp('+', b[y - 1])); y--; }
                    else { ops.Add(new CharOp('-', a[x - 1])); x--; }
                }
            }
            ops.Reverse();
            return ops;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Marabook.Correction;
using Marabook.Model;

namespace Marabook.Tests
{
    public static partial class DebtTests
    {
        // ------------------------------------------------------------ la typographie à la frappe, en fenêtre

        private static void TypographyWindow(Harness t)
        {
            t.Check(TypographyLive.WindowFor(new string('a', TypographyLive.WindowThreshold), 100) == null, "un paragraphe ordinaire se relit en entier");
            var flat = Filler(60000, 9);
            var caret = 30000;
            var window = TypographyLive.WindowFor(flat, caret);
            t.Check(window != null && window[0] < caret && window[1] >= caret && window[1] - window[0] < 6000, "un très gros paragraphe : une fenêtre de quelques milliers de signes autour du curseur");
            if (window == null) return;
            t.Check((window[0] == 0 || flat[window[0] - 1] == ' ') && (window[1] == flat.Length || flat[window[1]] == ' '), "…calée sur des blancs, des deux côtés");
            var atStart = TypographyLive.WindowFor(flat, 10);
            var atEnd = TypographyLive.WindowFor(flat, flat.Length);
            t.Check(atStart[0] == 0 && atEnd[1] == flat.Length, "au début et à la fin du paragraphe, elle s'arrête au bord");

            // Les opérations d'une fenêtre, étendues au paragraphe : le reste est conservé.
            var slice = flat.Substring(window[0], window[1] - window[0]);
            var changed = slice.Substring(0, 100) + "’" + slice.Substring(101);
            var ops = CharDiff.Diff(slice, changed);
            var wide = TypographyLive.Widen(ops, flat, window[0], window[1]);
            var rebuilt = new StringBuilder();
            var kept = 0;
            foreach (var op in wide) { if (op.Type != '-') rebuilt.Append(op.Char); if (op.Type == ' ') kept++; }
            t.Check(rebuilt.ToString() == flat.Substring(0, window[0]) + changed + flat.Substring(window[1]) && kept == flat.Length - 1, "une correction dans la fenêtre s'applique au paragraphe entier, à sa place");

            var spans = new List<int[]> { new[] { 10, 5 }, new[] { window[0] - 3, 10 }, new[] { window[1] - 2, 50 }, new[] { flat.Length - 4, 4 } };
            var shifted = TypographyLive.ShiftSpans(spans, window[0], window[1] - window[0]);
            t.Check(shifted != null && shifted.Count == 2 && shifted[0][0] == 0 && shifted[0][1] == 7 && shifted[1][0] == window[1] - window[0] - 2 && shifted[1][1] == 2, "les passages « sans correction » sont ramenés à la fenêtre, rognés à ses bords");
            t.Check(TypographyLive.ShiftSpans(null, 0, 10) == null && TypographyLive.ShiftSpans(new List<int[]> { new[] { 0, 3 } }, 100, 10) == null, "aucun passage dans la fenêtre : rien");
        }

    }
}

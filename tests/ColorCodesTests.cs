using System;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C92 — 2.0.0, lot 31 (25/11) : les codes d'une couleur du sélecteur de couleur
    /// personnalisée — RVB, HSL, CMJN, TSV, aller et retour — et la lecture d'un code tapé à la
    /// main (HEX à trois ou six chiffres, « rgb( ) », trois nombres).</summary>
    public static class ColorCodesTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C92 — lot 31 : codes de couleur (RVB, HSL, CMJN, TSV, lecture HEX et RVB)");
            int r, g, b;
            double h, s, l, v, c, m, y, k;

            // — HEX.
            t.Check(ColorCodes.TryParse("#5B67D8", out r, out g, out b) && r == 0x5B && g == 0x67 && b == 0xD8, "un code HEX à six chiffres se lit");
            t.Check(ColorCodes.TryParse(" 5b67d8 ", out r, out g, out b) && r == 0x5B && b == 0xD8, "…sans croisillon, en minuscules, entouré d'espaces aussi");
            t.Check(ColorCodes.TryParse("#abc", out r, out g, out b) && r == 0xAA && g == 0xBB && b == 0xCC, "…et la forme courte à trois chiffres (#ABC = #AABBCC)");
            t.Check(!ColorCodes.TryParse("#5B67D", out r, out g, out b) && !ColorCodes.TryParse("#GG0000", out r, out g, out b) && !ColorCodes.TryParse("", out r, out g, out b) && !ColorCodes.TryParse(null, out r, out g, out b),
                "un code incomplet, un chiffre qui n'en est pas un, un texte vide sont refusés");
            t.Check(!ColorCodes.TryParse("#5B67D880", out r, out g, out b) && !ColorCodes.TryParse("rgba(1,2,3,0.5)", out r, out g, out b) && !ColorCodes.TryParse("1,2,3,4", out r, out g, out b),
                "…une transparence aussi : huit chiffres, « rgba », quatre nombres");
            // — RVB.
            t.Check(ColorCodes.TryParse("rgb(91, 103, 216)", out r, out g, out b) && r == 91 && g == 103 && b == 216, "« rgb(91, 103, 216) » se lit");
            t.Check(ColorCodes.TryParse("91,103,216", out r, out g, out b) && r == 91 && ColorCodes.TryParse("91 103 216", out r, out g, out b) && b == 216 && ColorCodes.TryParse("RGB(0;0;0)", out r, out g, out b) && r + g + b == 0,
                "…trois nombres séparés de virgules, d'espaces ou de points-virgules aussi");
            t.Check(!ColorCodes.TryParse("256, 0, 0", out r, out g, out b) && !ColorCodes.TryParse("rgb(1,2)", out r, out g, out b) && !ColorCodes.TryParse("rgb 1,2,3", out r, out g, out b) && !ColorCodes.TryParse("-1 2 3", out r, out g, out b),
                "une composante hors de 0–255, deux nombres, des parenthèses qui manquent sont refusés");
            // « 123 » seul : trois chiffres HEX, pas un nombre.
            t.Check(ColorCodes.TryParse("123", out r, out g, out b) && r == 0x11 && g == 0x22 && b == 0x33, "« 123 » seul est un code HEX court, pas une composante");
            t.Check(ColorCodes.Hex(91, 103, 216) == "#5B67D8" && ColorCodes.Hex(300, -4, 0) == "#FF0000", "le code HEX d'une couleur s'écrit en capitales, borné");

            // — HSL.
            ColorCodes.RgbToHsl(255, 0, 0, out h, out s, out l);
            t.Check(Near(h, 0) && Near(s, 100) && Near(l, 50), "le rouge pur : teinte 0°, saturation 100 %, luminosité 50 %");
            ColorCodes.RgbToHsl(0, 0, 255, out h, out s, out l);
            t.Check(Near(h, 240) && Near(s, 100) && Near(l, 50), "le bleu pur : teinte 240°");
            ColorCodes.RgbToHsl(128, 128, 128, out h, out s, out l);
            t.Check(Near(s, 0) && Math.Abs(l - 50.2) < 0.1, "un gris : saturation nulle");
            ColorCodes.HslToRgb(120, 100, 25, out r, out g, out b);
            t.Check(r == 0 && g == 128 && b == 0, "HSL 120°, 100 %, 25 % rend le vert sombre (0, 128, 0)");
            // — CMJN.
            ColorCodes.RgbToCmyk(255, 0, 0, out c, out m, out y, out k);
            t.Check(Near(c, 0) && Near(m, 100) && Near(y, 100) && Near(k, 0), "le rouge pur : magenta et jaune à 100 %");
            ColorCodes.RgbToCmyk(0, 0, 0, out c, out m, out y, out k);
            t.Check(Near(c, 0) && Near(m, 0) && Near(y, 0) && Near(k, 100), "le noir : 100 % de noir, rien d'autre");
            ColorCodes.CmykToRgb(100, 0, 0, 0, out r, out g, out b);
            t.Check(r == 0 && g == 255 && b == 255, "100 % de cyan rend (0, 255, 255)");
            ColorCodes.CmykToRgb(0, 0, 0, 50, out r, out g, out b);
            t.Check(r == 128 && g == 128 && b == 128, "50 % de noir rend le gris moyen");
            // — TSV (le carré chromatique).
            ColorCodes.RgbToHsv(255, 128, 0, out h, out s, out v);
            t.Check(Math.Abs(h - 30.1) < 0.2 && Near(s, 1) && Near(v, 1), "un orange vif est dans le coin du carré : saturation et valeur pleines");
            ColorCodes.HsvToRgb(0, 0, 1, out r, out g, out b);
            t.Check(r == 255 && g == 255 && b == 255, "le coin haut gauche du carré est blanc");
            ColorCodes.HsvToRgb(200, 0.7, 0, out r, out g, out b);
            t.Check(r + g + b == 0, "le bas du carré est noir, quelle que soit la teinte");

            // — Aller et retour : chaque métrique rend la couleur dont elle vient.
            var drift = 0;
            var samples = new[] { 0x5B67D8, 0xC9A227, 0x000000, 0xFFFFFF, 0x7F8C8D, 0x16A085, 0xFFD6E7, 0x703C2F, 0x010203, 0xFEFDFC };
            foreach (var sample in samples)
            {
                int sr = (sample >> 16) & 255, sg = (sample >> 8) & 255, sb = sample & 255;
                ColorCodes.RgbToHsl(sr, sg, sb, out h, out s, out l);
                ColorCodes.HslToRgb(h, s, l, out r, out g, out b);
                drift = Math.Max(drift, Math.Max(Math.Abs(r - sr), Math.Max(Math.Abs(g - sg), Math.Abs(b - sb))));
                ColorCodes.RgbToCmyk(sr, sg, sb, out c, out m, out y, out k);
                ColorCodes.CmykToRgb(c, m, y, k, out r, out g, out b);
                drift = Math.Max(drift, Math.Max(Math.Abs(r - sr), Math.Max(Math.Abs(g - sg), Math.Abs(b - sb))));
                ColorCodes.RgbToHsv(sr, sg, sb, out h, out s, out v);
                ColorCodes.HsvToRgb(h, s, v, out r, out g, out b);
                drift = Math.Max(drift, Math.Max(Math.Abs(r - sr), Math.Max(Math.Abs(g - sg), Math.Abs(b - sb))));
            }
            t.Check(drift == 0, "RVB → HSL, CMJN, TSV → RVB : dix couleurs reviennent identiques (écart " + drift + ")");
        }

        private static bool Near(double a, double b) { return Math.Abs(a - b) < 0.01; }
    }
}

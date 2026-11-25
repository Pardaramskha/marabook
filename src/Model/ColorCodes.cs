using System;
using System.Globalization;

namespace Marabook.Model
{
    /// <summary>LES CODES D'UNE COULEUR (2.0.0, 25/11/2026, Rémi) — ce que le sélecteur de couleur
    /// personnalisée sait lire et écrire : RVB (0–255), HSL (teinte 0–360°, saturation et
    /// luminosité 0–100 %), CMJN (0–100 %, sans profil : la conversion arithmétique, celle des
    /// nuanciers d'écran) et TSV, le repère du carré chromatique (saturation et valeur 0–1).
    /// Et la lecture d'un code tapé à la main : « #5B67D8 », « 5B67D8 », « #ABC »,
    /// « rgb(91, 103, 216) », « 91, 103, 216 », « 91 103 216 ». Pas de transparence : une
    /// couleur de Marabook est opaque (un code à huit chiffres est refusé).</summary>
    public static class ColorCodes
    {
        private static int Byte(double value)
        {
            var v = (int)Math.Round(value);
            return v < 0 ? 0 : v > 255 ? 255 : v;
        }

        private static double Clamp(double value, double min, double max) { return value < min ? min : value > max ? max : value; }

        public static string Hex(int r, int g, int b)
        {
            return "#" + Byte(r).ToString("X2", CultureInfo.InvariantCulture) + Byte(g).ToString("X2", CultureInfo.InvariantCulture) + Byte(b).ToString("X2", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ TSV (le carré)

        /// <summary>h en degrés (0–360), s et v de 0 à 1.</summary>
        public static void RgbToHsv(int r, int g, int b, out double h, out double s, out double v)
        {
            double rf = Byte(r) / 255.0, gf = Byte(g) / 255.0, bf = Byte(b) / 255.0;
            var max = Math.Max(rf, Math.Max(gf, bf));
            var min = Math.Min(rf, Math.Min(gf, bf));
            var delta = max - min;
            v = max;
            s = max <= 0 ? 0 : delta / max;
            h = HueOf(rf, gf, bf, max, delta);
        }

        public static void HsvToRgb(double h, double s, double v, out int r, out int g, out int b)
        {
            s = Clamp(s, 0, 1);
            v = Clamp(v, 0, 1);
            var c = v * s;
            double rf, gf, bf;
            Sector(h, c, out rf, out gf, out bf);
            var m = v - c;
            r = Byte((rf + m) * 255);
            g = Byte((gf + m) * 255);
            b = Byte((bf + m) * 255);
        }

        // ------------------------------------------------------------------ HSL

        /// <summary>h en degrés (0–360), s et l en pour cent (0–100).</summary>
        public static void RgbToHsl(int r, int g, int b, out double h, out double s, out double l)
        {
            double rf = Byte(r) / 255.0, gf = Byte(g) / 255.0, bf = Byte(b) / 255.0;
            var max = Math.Max(rf, Math.Max(gf, bf));
            var min = Math.Min(rf, Math.Min(gf, bf));
            var delta = max - min;
            var light = (max + min) / 2;
            var sat = delta < 1e-9 ? 0 : delta / (1 - Math.Abs(2 * light - 1));
            h = HueOf(rf, gf, bf, max, delta);
            s = Clamp(sat, 0, 1) * 100;
            l = light * 100;
        }

        public static void HslToRgb(double h, double s, double l, out int r, out int g, out int b)
        {
            var sat = Clamp(s, 0, 100) / 100;
            var light = Clamp(l, 0, 100) / 100;
            var c = (1 - Math.Abs(2 * light - 1)) * sat;
            double rf, gf, bf;
            Sector(h, c, out rf, out gf, out bf);
            var m = light - c / 2;
            r = Byte((rf + m) * 255);
            g = Byte((gf + m) * 255);
            b = Byte((bf + m) * 255);
        }

        // ------------------------------------------------------------------ CMJN

        /// <summary>Cyan, magenta, jaune, noir en pour cent (0–100).</summary>
        public static void RgbToCmyk(int r, int g, int b, out double c, out double m, out double y, out double k)
        {
            double rf = Byte(r) / 255.0, gf = Byte(g) / 255.0, bf = Byte(b) / 255.0;
            var black = 1 - Math.Max(rf, Math.Max(gf, bf));
            if (black >= 1 - 1e-9) { c = 0; m = 0; y = 0; k = 100; return; }
            c = (1 - rf - black) / (1 - black) * 100;
            m = (1 - gf - black) / (1 - black) * 100;
            y = (1 - bf - black) / (1 - black) * 100;
            k = black * 100;
        }

        public static void CmykToRgb(double c, double m, double y, double k, out int r, out int g, out int b)
        {
            var black = Clamp(k, 0, 100) / 100;
            r = Byte(255 * (1 - Clamp(c, 0, 100) / 100) * (1 - black));
            g = Byte(255 * (1 - Clamp(m, 0, 100) / 100) * (1 - black));
            b = Byte(255 * (1 - Clamp(y, 0, 100) / 100) * (1 - black));
        }

        // ------------------------------------------------------------------ communs

        private static double HueOf(double rf, double gf, double bf, double max, double delta)
        {
            if (delta < 1e-9) return 0;
            double h;
            if (max == rf) h = 60 * (((gf - bf) / delta) % 6);
            else if (max == gf) h = 60 * ((bf - rf) / delta + 2);
            else h = 60 * ((rf - gf) / delta + 4);
            if (h < 0) h += 360;
            return h >= 360 ? h - 360 : h;
        }

        private static void Sector(double h, double c, out double r, out double g, out double b)
        {
            var hp = ((h % 360) + 360) % 360 / 60.0;
            var x = c * (1 - Math.Abs(hp % 2 - 1));
            if (hp < 1) { r = c; g = x; b = 0; }
            else if (hp < 2) { r = x; g = c; b = 0; }
            else if (hp < 3) { r = 0; g = c; b = x; }
            else if (hp < 4) { r = 0; g = x; b = c; }
            else if (hp < 5) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
        }

        // ------------------------------------------------------------------ lecture d'un code

        /// <summary>Lit un code HEX (« #5B67D8 », « 5b67d8 », « #ABC ») ou RVB (« rgb(91, 103, 216) »,
        /// « 91, 103, 216 », « 91 103 216 », « 91;103;216 »). Faux si le texte n'est ni l'un ni l'autre —
        /// un code incomplet, une composante hors de 0–255, une transparence.</summary>
        public static bool TryParse(string text, out int r, out int g, out int b)
        {
            r = g = b = 0;
            var t = (text ?? "").Trim();
            if (t.Length == 0) return false;
            var lower = t.ToLowerInvariant();
            // — RVB : trois nombres, avec ou sans « rgb( ) ».
            var body = lower;
            var named = false;
            if (body.StartsWith("rgb", StringComparison.Ordinal))
            {
                named = true;
                body = body.Substring(3).Trim();
                if (!body.StartsWith("(", StringComparison.Ordinal) || !body.EndsWith(")", StringComparison.Ordinal)) return false;
                body = body.Substring(1, body.Length - 2);
            }
            var parts = body.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3 && (named || body.IndexOfAny(new[] { ',', ';', ' ', '\t' }) >= 0))
            {
                var values = new int[3];
                for (var i = 0; i < 3; i++)
                {
                    if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]) || values[i] > 255) return false;
                }
                r = values[0]; g = values[1]; b = values[2];
                return true;
            }
            if (named) return false;
            // — HEX : trois ou six chiffres, le croisillon facultatif.
            var hex = lower.StartsWith("#", StringComparison.Ordinal) ? lower.Substring(1) : lower;
            if (hex.Length != 3 && hex.Length != 6) return false;
            foreach (var c in hex) if (!Uri.IsHexDigit(c)) return false;
            if (hex.Length == 3) hex = new string(new[] { hex[0], hex[0], hex[1], hex[1], hex[2], hex[2] });
            r = Convert.ToInt32(hex.Substring(0, 2), 16);
            g = Convert.ToInt32(hex.Substring(2, 2), 16);
            b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return true;
        }
    }
}

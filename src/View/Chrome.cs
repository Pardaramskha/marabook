using System.Windows.Media;
using UniversSale.Settings;

namespace UniversSale.View
{
    /// <summary>Shared brushes for the interface "chrome" (bars, panels, labels).
    /// They are mutable: toggling the theme recolors the whole application at once,
    /// since every element references the same instances. Ported from Mental-o;
    /// palette is Marabook's own (neutral grays, indigo accent). The accent hue
    /// and the "white paper under the dark theme" option come from AppSettings
    /// (Préférences → Personnalisation) — keep all colors here.</summary>
    public static class Chrome
    {
        public static readonly SolidColorBrush WindowBg = Brush(0xF4, 0xF5, 0xF7);
        public static readonly SolidColorBrush BarBg = Brush(0xEC, 0xED, 0xF1);
        public static readonly SolidColorBrush BarBgLight = Brush(0xF1, 0xF2, 0xF5);
        public static readonly SolidColorBrush Border = Brush(0xD7, 0xDA, 0xE0);
        public static readonly SolidColorBrush SoftText = Brush(0x6B, 0x72, 0x80);
        public static readonly SolidColorBrush Ink = Brush(0x23, 0x26, 0x2A);
        public static readonly SolidColorBrush PanelBg = BrushAlpha(0xF2, 0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush PaperBg = Brush(0xFF, 0xFF, 0xFF);
        public static readonly SolidColorBrush Accent = Brush(0x5B, 0x67, 0xD8);

        // Paper surfaces have their own inks: with « papier blanc en mode
        // sombre », the page stays light while the chrome darkens — the text
        // written ON the page can no longer share the chrome's Ink. PaperInk is
        // also the identity of the "automatic" text color (FlowConverter,
        // Printing compare by reference).
        public static readonly SolidColorBrush PaperInk = Brush(0x23, 0x26, 0x2A);
        public static readonly SolidColorBrush PaperSoftInk = Brush(0x6B, 0x72, 0x80);

        // Cards (corkboard, media previews) follow the theme even when the
        // writing paper is forced white — they are chrome, not manuscript.
        public static readonly SolidColorBrush CardBg = Brush(0xFF, 0xFF, 0xFF);

        // Teinte des passages annotés (révision) : or semi-transparent, lisible
        // sur papier blanc comme sombre. Cosmétique — jamais persistée, jamais
        // imprimée (les surlignages réels sont opaques, elle non).
        public static readonly SolidColorBrush AnnotationTint =
            BrushAlpha(0x55, 0xF1, 0xC4, 0x0F);

        public const string DefaultAccent = "#5B67D8";

        public static void Toggle(bool dark)
        {
            var accent = ParseAccent();
            if (dark)
            {
                WindowBg.Color = Rgb(0x1B, 0x1E, 0x23);
                BarBg.Color = Rgb(0x20, 0x23, 0x2A);
                BarBgLight.Color = Rgb(0x24, 0x27, 0x2E);
                Border.Color = Rgb(0x38, 0x3D, 0x46);
                SoftText.Color = Rgb(0x9A, 0xA1, 0xAC);
                Ink.Color = Rgb(0xE6, 0xE8, 0xEC);
                PanelBg.Color = Color.FromArgb(0xF0, 0x20, 0x23, 0x2A);
                CardBg.Color = Rgb(0x23, 0x26, 0x2C);
                Accent.Color = accent.HasValue
                    ? Lighten(accent.Value, 0.22)
                    : Rgb(0x7B, 0x86, 0xE8);
                if (AppSettings.WhitePaperInDark)
                {
                    PaperBg.Color = Rgb(0xFF, 0xFF, 0xFF);
                    PaperInk.Color = Rgb(0x23, 0x26, 0x2A);
                    PaperSoftInk.Color = Rgb(0x6B, 0x72, 0x80);
                }
                else
                {
                    PaperBg.Color = Rgb(0x23, 0x26, 0x2C);
                    PaperInk.Color = Rgb(0xE6, 0xE8, 0xEC);
                    PaperSoftInk.Color = Rgb(0x9A, 0xA1, 0xAC);
                }
            }
            else
            {
                WindowBg.Color = Rgb(0xF4, 0xF5, 0xF7);
                BarBg.Color = Rgb(0xEC, 0xED, 0xF1);
                BarBgLight.Color = Rgb(0xF1, 0xF2, 0xF5);
                Border.Color = Rgb(0xD7, 0xDA, 0xE0);
                SoftText.Color = Rgb(0x6B, 0x72, 0x80);
                Ink.Color = Rgb(0x23, 0x26, 0x2A);
                PanelBg.Color = Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF);
                PaperBg.Color = Rgb(0xFF, 0xFF, 0xFF);
                PaperInk.Color = Rgb(0x23, 0x26, 0x2A);
                PaperSoftInk.Color = Rgb(0x6B, 0x72, 0x80);
                CardBg.Color = Rgb(0xFF, 0xFF, 0xFF);
                Accent.Color = accent.HasValue ? accent.Value : Rgb(0x5B, 0x67, 0xD8);
            }
        }

        /// <summary>The customized accent, or null when the default indigo applies.</summary>
        public static Color? ParseAccent()
        {
            var hex = AppSettings.AccentColor;
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return null;
            try
            {
                return Color.FromRgb(
                    System.Convert.ToByte(hex.Substring(1, 2), 16),
                    System.Convert.ToByte(hex.Substring(3, 2), 16),
                    System.Convert.ToByte(hex.Substring(5, 2), 16));
            }
            catch { return null; }
        }

        /// <summary>Blend toward white — the same move that turns the light
        /// indigo into its dark-theme sibling (readability on dark grounds).</summary>
        public static Color Lighten(Color color, double amount)
        {
            return Blend(color, Colors.White, amount);
        }

        public static Color Blend(Color from, Color to, double amount)
        {
            if (amount < 0) amount = 0;
            if (amount > 1) amount = 1;
            return Color.FromRgb(
                (byte)(from.R + (to.R - from.R) * amount),
                (byte)(from.G + (to.G - from.G) * amount),
                (byte)(from.B + (to.B - from.B) * amount));
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private static SolidColorBrush BrushAlpha(byte a, byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }

        private static Color Rgb(byte r, byte g, byte b)
        {
            return Color.FromRgb(r, g, b);
        }
    }
}

using System.Windows.Media;

namespace UniversSale.View
{
    /// <summary>Shared brushes for the interface "chrome" (bars, panels, labels).
    /// They are mutable: toggling the theme recolors the whole application at once,
    /// since every element references the same instances. Ported from Mental-o;
    /// palette is Univers Sale's own (neutral grays, indigo accent). The palette
    /// becomes user-customizable in a later phase — keep all colors here.</summary>
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

        public static void Toggle(bool dark)
        {
            if (dark)
            {
                WindowBg.Color = Rgb(0x1B, 0x1E, 0x23);
                BarBg.Color = Rgb(0x20, 0x23, 0x2A);
                BarBgLight.Color = Rgb(0x24, 0x27, 0x2E);
                Border.Color = Rgb(0x38, 0x3D, 0x46);
                SoftText.Color = Rgb(0x9A, 0xA1, 0xAC);
                Ink.Color = Rgb(0xE6, 0xE8, 0xEC);
                PanelBg.Color = Color.FromArgb(0xF0, 0x20, 0x23, 0x2A);
                PaperBg.Color = Rgb(0x23, 0x26, 0x2C);
                Accent.Color = Rgb(0x7B, 0x86, 0xE8);
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
                Accent.Color = Rgb(0x5B, 0x67, 0xD8);
            }
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

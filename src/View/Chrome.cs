using System;
using System.Windows.Media;
using Marabook.Settings;

namespace Marabook.View
{
    /// <summary>LA source de vérité de la couleur (batch 40). Brosses
    /// partagées et mutables : basculer le thème recolore toute l'application
    /// d'un coup, chaque élément référençant la même instance ; Theme.cs
    /// engendre sa palette XAML à partir d'elles au lieu de les redéclarer.
    ///
    /// Quatre surfaces, du plus enfoncé au plus haut — ground (fond de la
    /// zone centrale), chrome (barres, Pile, rail), raised (panneaux de
    /// droite, cartes, dialogues), paper (la page, et rien d'autre) —
    /// séparées d'au moins huit unités de clarté (test C12). Trois encres,
    /// une échelle d'accent à quatre pas dérivée de la teinte de l'accent
    /// courant, trois couleurs de sens. Les noms historiques (WindowBg,
    /// BarBg, BarBgLight…) sont gardés : WindowBg = ground, BarBg = chrome,
    /// BarBgLight = RaisedBg = CardBg = raised.</summary>
    public static class Chrome
    {
        // ---- surfaces
        // Clair : ground 231, chrome 239, raised 247, paper 255 en clarté
        // moyenne — des marches de 8 exactement (chrome et raised sont une
        // unité sous les valeurs du jeu de jetons pour que les trois marches
        // tiennent entre ground et le blanc).
        public static readonly SolidColorBrush WindowBg = Brush(0xE2, 0xE5, 0xEE);   // ground
        public static readonly SolidColorBrush BarBg = Brush(0xEC, 0xEE, 0xF3);      // chrome
        public static readonly SolidColorBrush BarBgLight = Brush(0xF5, 0xF6, 0xFA); // raised (panneaux, papers)
        public static readonly SolidColorBrush RaisedBg = Brush(0xF5, 0xF6, 0xFA);   // raised (dialogues)
        public static readonly SolidColorBrush PaperBg = Brush(0xFF, 0xFF, 0xFF);    // paper
        public static readonly SolidColorBrush Border = Brush(0xD3, 0xD7, 0xE3);       // line : filets
        public static readonly SolidColorBrush BorderStrong = Brush(0xBF, 0xC5, 0xD8); // line-strong : bordures de contrôles

        // ---- encres
        public static readonly SolidColorBrush Ink = Brush(0x1E, 0x21, 0x28);
        public static readonly SolidColorBrush SoftText = Brush(0x5A, 0x61, 0x72);  // ink-soft : texte secondaire
        public static readonly SolidColorBrush FaintText = Brush(0x8A, 0x91, 0xA3); // ink-faint : en-têtes de panneau, compteurs, invites

        // Paper surfaces have their own inks: with « papier blanc en mode
        // sombre », the page stays light while the chrome darkens — the text
        // written ON the page can no longer share the chrome's Ink. PaperInk is
        // also the identity of the "automatic" text color (FlowConverter,
        // Printing compare by reference).
        public static readonly SolidColorBrush PaperInk = Brush(0x1E, 0x21, 0x28);
        public static readonly SolidColorBrush PaperSoftInk = Brush(0x5A, 0x61, 0x72);

        // Cards (corkboard, media previews) follow the theme even when the
        // writing paper is forced white — they are chrome (raised), not manuscript.
        public static readonly SolidColorBrush CardBg = Brush(0xF5, 0xF6, 0xFA);

        // ---- l'échelle d'accent : quatre pas, un état survolé, un état
        // sélectionné et une action principale ne partagent pas une couleur.
        public static readonly SolidColorBrush AccentTint = Brush(0xED, 0xEF, 0xFC);   // survol, ligne sélectionnée, bascule active
        public static readonly SolidColorBrush AccentSoft = Brush(0xC4, 0xCA, 0xF2);   // bordures d'état actif, pastilles calmes
        public static readonly SolidColorBrush Accent = Brush(0x5B, 0x67, 0xD8);       // action principale, onglet actif
        public static readonly SolidColorBrush AccentStrong = Brush(0x45, 0x4F, 0xB8); // enfoncé, texte posé sur accent-tint

        // ---- couleurs de sens : séparées de l'accent, jamais décoratives.
        public static readonly SolidColorBrush Ok = Brush(0x2E, 0x9E, 0x6B);
        public static readonly SolidColorBrush Warn = Brush(0xC4, 0x82, 0x0E);
        public static readonly SolidColorBrush Danger = Brush(0xCE, 0x46, 0x46);

        // Teinte des passages annotés (révision) : or semi-transparent, lisible
        // sur papier blanc comme sombre. Cosmétique — jamais persistée, jamais
        // imprimée (les surlignages réels sont opaques, elle non).
        public static readonly SolidColorBrush AnnotationTint =
            BrushAlpha(0x55, 0xF1, 0xC4, 0x0F);

        public const string DefaultAccent = "#5B67D8";

        public static void Toggle(bool dark)
        {
            var accent = ParseAccent() ?? Rgb(0x5B, 0x67, 0xD8);
            var hue = HueOf(accent);
            if (dark)
            {
                WindowBg.Color = Rgb(0x14, 0x16, 0x1B);
                BarBg.Color = Rgb(0x1B, 0x1E, 0x25);
                var raised = Rgb(0x22, 0x26, 0x2F);
                BarBgLight.Color = raised;
                RaisedBg.Color = raised;
                CardBg.Color = raised;
                Border.Color = Rgb(0x2E, 0x33, 0x40);
                BorderStrong.Color = Rgb(0x3D, 0x43, 0x54);
                Ink.Color = Rgb(0xE7, 0xE9, 0xF0);
                SoftText.Color = Rgb(0x99, 0xA0, 0xB2);
                FaintText.Color = Rgb(0x6E, 0x76, 0x88);
                if (AppSettings.WhitePaperInDark)
                {
                    PaperBg.Color = Rgb(0xFF, 0xFF, 0xFF);
                    PaperInk.Color = Rgb(0x1E, 0x21, 0x28);
                    PaperSoftInk.Color = Rgb(0x5A, 0x61, 0x72);
                }
                else
                {
                    PaperBg.Color = raised;
                    PaperInk.Color = Ink.Color;
                    PaperSoftInk.Color = SoftText.Color;
                }
                // Sur fond sombre l'accent lui-même est éclairci ; les pas
                // gardent la teinte de l'utilisateur.
                Accent.Color = Tint(hue, 0.73, 0.73);
                AccentTint.Color = Tint(hue, 0.30, 0.21);
                AccentSoft.Color = Tint(hue, 0.33, 0.35);
                AccentStrong.Color = Tint(hue, 0.75, 0.79);
                Ok.Color = Rgb(0x4F, 0xBF, 0x8B);
                Warn.Color = Rgb(0xE0, 0xA6, 0x3C);
                Danger.Color = Rgb(0xE8, 0x70, 0x6F);
            }
            else
            {
                WindowBg.Color = Rgb(0xE2, 0xE5, 0xEE);
                BarBg.Color = Rgb(0xEC, 0xEE, 0xF3);
                var raised = Rgb(0xF5, 0xF6, 0xFA);
                BarBgLight.Color = raised;
                RaisedBg.Color = raised;
                CardBg.Color = raised;
                Border.Color = Rgb(0xD3, 0xD7, 0xE3);
                BorderStrong.Color = Rgb(0xBF, 0xC5, 0xD8);
                Ink.Color = Rgb(0x1E, 0x21, 0x28);
                SoftText.Color = Rgb(0x5A, 0x61, 0x72);
                FaintText.Color = Rgb(0x8A, 0x91, 0xA3);
                PaperBg.Color = Rgb(0xFF, 0xFF, 0xFF);
                PaperInk.Color = Ink.Color;
                PaperSoftInk.Color = SoftText.Color;
                Accent.Color = accent; // l'identité de Marabook, telle quelle
                AccentTint.Color = Tint(hue, 0.71, 0.96);
                AccentSoft.Color = Tint(hue, 0.64, 0.86);
                AccentStrong.Color = Tint(hue, 0.45, 0.50);
                Ok.Color = Rgb(0x2E, 0x9E, 0x6B);
                Warn.Color = Rgb(0xC4, 0x82, 0x0E);
                Danger.Color = Rgb(0xCE, 0x46, 0x46);
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

        /// <summary>Blend toward white.</summary>
        public static Color Lighten(Color color, double amount)
        {
            return Blend(color, Colors.White, amount);
        }

        /// <summary>Blend toward black — le pendant de Lighten.</summary>
        public static Color Darken(Color color, double amount)
        {
            return Blend(color, Colors.Black, amount);
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

        /// <summary>La teinte (0–360) d'une couleur — ce que l'accent
        /// personnalisé transmet à ses pas.</summary>
        public static double HueOf(Color color)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            if (delta < 1e-6) return 0;
            double hue;
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
            if (hue < 0) hue += 360;
            return hue;
        }

        /// <summary>Une couleur de teinte donnée à saturation et clarté fixées
        /// (HSL) : chaque pas de l'échelle d'accent est « la teinte de
        /// l'accent, à cette hauteur ».</summary>
        public static Color Tint(double hue, double saturation, double lightness)
        {
            var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            var h = (hue % 360) / 60.0;
            var x = c * (1 - Math.Abs(h % 2 - 1));
            double r, g, b;
            if (h < 1) { r = c; g = x; b = 0; }
            else if (h < 2) { r = x; g = c; b = 0; }
            else if (h < 3) { r = 0; g = c; b = x; }
            else if (h < 4) { r = 0; g = x; b = c; }
            else if (h < 5) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            var m = lightness - c / 2;
            return Color.FromRgb(Channel(r + m), Channel(g + m), Channel(b + m));
        }

        /// <summary>Clarté (0–255) : la moyenne des trois canaux — l'unité
        /// dans laquelle le jeu de jetons est spécifié (les « marches » entre
        /// surfaces) ; le test de séparation des surfaces s'en sert.</summary>
        public static double Luma(Color color)
        {
            return (color.R + color.G + color.B) / 3.0;
        }

        public static string Hex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        private static byte Channel(double value)
        {
            var v = (int)Math.Round(value * 255);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)v;
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

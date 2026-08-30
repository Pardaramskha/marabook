using System;
using System.Windows.Media;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale.Tests
{
    /// <summary>C12 — le thème (batch 34) : les deux palettes SE PARSENT.
    /// Theme.Switch avale toute erreur XAML pour garder l'application
    /// utilisable en style classique — ce filet rend la faute visible ici
    /// plutôt qu'en « tout blanc » chez l'utilisateur. Batch 40 : le
    /// garde-fou contre la bouillie (surfaces ordonnées et séparées, encres
    /// contrastées), l'échelle d'accent dérivée qui tombe juste sur l'indigo
    /// par défaut, et les couleurs de sens tenues à l'écart de l'accent.</summary>
    public static class ThemeTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C12 — thème");
            var savedWhite = AppSettings.WhitePaperInDark;
            var savedAccent = AppSettings.AccentColor;
            try
            {
                AppSettings.WhitePaperInDark = false;
                AppSettings.AccentColor = null;
                var light = Theme.SelfCheck(false);
                t.Check(light == null, "la palette claire se parse" + (light == null ? "" : " — " + light));
                var dark = Theme.SelfCheck(true);
                t.Check(dark == null, "la palette sombre se parse" + (dark == null ? "" : " — " + dark));
                var accent = Theme.SelfCheck(false, "#E67E22");
                t.Check(accent == null, "un accent personnalisé se parse" + (accent == null ? "" : " — " + accent));

                // — Les surfaces : ordonnées, séparées d'au moins 8 unités, encre lisible.
                var surfacesLight = Theme.SurfaceCheck(false);
                t.Check(surfacesLight == null, "thème clair : quatre surfaces séparées, encres contrastées" + (surfacesLight == null ? "" : " — " + surfacesLight));
                var surfacesDark = Theme.SurfaceCheck(true);
                t.Check(surfacesDark == null, "thème sombre : quatre surfaces séparées, encres contrastées" + (surfacesDark == null ? "" : " — " + surfacesDark));
                AppSettings.WhitePaperInDark = true;
                var surfacesWhite = Theme.SurfaceCheck(true);
                t.Check(surfacesWhite == null, "papier blanc en mode sombre : le papier reste blanc, l'encre du papier reste sombre" + (surfacesWhite == null ? "" : " — " + surfacesWhite));
                Chrome.Toggle(true);
                t.Check(Chrome.PaperBg.Color == Colors.White && Chrome.Luma(Chrome.PaperInk.Color) < 80 && Chrome.Luma(Chrome.Ink.Color) > 200,
                    "…avec ses encres propres, distinctes de l'encre du chrome");
                AppSettings.WhitePaperInDark = false;

                // — L'échelle d'accent tombe juste sur l'indigo par défaut (± 4 par canal).
                Chrome.Toggle(false);
                Near(t, "#5B67D8", Chrome.Accent.Color, "clair : accent");
                Near(t, "#EDEFFC", Chrome.AccentTint.Color, "clair : accent-tint");
                Near(t, "#C4CAF2", Chrome.AccentSoft.Color, "clair : accent-soft");
                Near(t, "#454FB8", Chrome.AccentStrong.Color, "clair : accent-strong");
                Chrome.Toggle(true);
                Near(t, "#8792EC", Chrome.Accent.Color, "sombre : accent");
                Near(t, "#262C46", Chrome.AccentTint.Color, "sombre : accent-tint");
                Near(t, "#3C4576", Chrome.AccentSoft.Color, "sombre : accent-soft");
                Near(t, "#A3ACF2", Chrome.AccentStrong.Color, "sombre : accent-strong");

                // — Un accent personnalisé transmet sa teinte aux quatre pas.
                AppSettings.AccentColor = "#E67E22";
                Chrome.Toggle(false);
                var hue = Chrome.HueOf(Chrome.Accent.Color);
                t.Check(Chrome.Accent.Color == Color.FromRgb(0xE6, 0x7E, 0x22), "clair : l'accent personnalisé est repris tel quel");
                t.Check(Math.Abs(Chrome.HueOf(Chrome.AccentTint.Color) - hue) < 3 && Math.Abs(Chrome.HueOf(Chrome.AccentStrong.Color) - hue) < 3,
                    "…et ses pas gardent sa teinte (" + hue.ToString("0") + "°)");
                t.Check(Chrome.Luma(Chrome.AccentTint.Color) > Chrome.Luma(Chrome.AccentSoft.Color)
                    && Chrome.Luma(Chrome.AccentSoft.Color) > Chrome.Luma(Chrome.Accent.Color)
                    && Chrome.Luma(Chrome.Accent.Color) > Chrome.Luma(Chrome.AccentStrong.Color),
                    "…ordonnés du plus clair au plus foncé");
                AppSettings.AccentColor = null;

                // — Les couleurs de sens ne sont pas l'accent.
                Chrome.Toggle(false);
                foreach (var pair in new[] { new object[] { "ok", Chrome.Ok }, new object[] { "warn", Chrome.Warn }, new object[] { "danger", Chrome.Danger } })
                {
                    var sense = ((SolidColorBrush)pair[1]).Color;
                    var gap = Math.Abs(Chrome.HueOf(sense) - Chrome.HueOf(Chrome.Accent.Color));
                    if (gap > 180) gap = 360 - gap;
                    t.Check(gap > 40, "« " + pair[0] + " » est loin de la teinte de l'accent (" + gap.ToString("0") + "°)");
                }
                t.Check(Chrome.Hex(Chrome.Warn.Color) == "#C4820E" && Chrome.Hex(Chrome.Danger.Color) == "#CE4646" && Chrome.Hex(Chrome.Ok.Color) == "#2E9E6B",
                    "les couleurs de sens claires sont celles du jeu de jetons");
            }
            finally
            {
                AppSettings.WhitePaperInDark = savedWhite;
                AppSettings.AccentColor = savedAccent;
                Chrome.Toggle(AppSettings.DarkTheme);
            }
        }

        private static void Near(Harness t, string expected, Color actual, string label)
        {
            var r = Convert.ToInt32(expected.Substring(1, 2), 16);
            var g = Convert.ToInt32(expected.Substring(3, 2), 16);
            var b = Convert.ToInt32(expected.Substring(5, 2), 16);
            var ok = Math.Abs(r - actual.R) <= 4 && Math.Abs(g - actual.G) <= 4 && Math.Abs(b - actual.B) <= 4;
            t.Check(ok, label + " ≈ " + expected + " (obtenu " + Chrome.Hex(actual) + ")");
        }
    }
}

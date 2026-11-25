using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LA SÉRIE « MUSEUM » (2.0.0, 24/11/2026, Rémi) — quatre skins PREMIUM :
    /// « Gardien du Feu », « Bois Blanc », « Assermenté », et « Ascension » (le soir du 24/11 : bleu
    /// nuit et or, angles droits, titres en capitales). Conçus dans l'Atelier Muséum
    /// (le dossier de passation du 24/11) : chaque panneau est un cartel posé sur une
    /// vitrine — fond froid, métal chaud réservé à ce qui informe ou réagit.
    /// Les TROIS OPTIONS de ces skins :
    ///  · l'ACCENT : celui de l'application (AppSettings.AccentColor) ; sans choix de
    ///    l'auteur, celui du skin (Skins.MuseumAccent, par Chrome.ParseAccent). Seule sa
    ///    TEINTE est gardée : la saturation et la clarté sont celles du skin ;
    ///  · le FOND (AppSettings.SkinBackground) : toutes les surfaces en sont décalées
    ///    (Shift), sa clarté est bornée par le mode (Skins.MuseumBackgroundFor) ;
    ///  · l'EFFET VERRE (AppSettings.SkinGlass, 0 à 100) : la transparence des barres
    ///    sur le fond peint (65 % d'origine) ; et le FLOU de ce qui est derrière une fenêtre de
    ///    dialogue (AppSettings.SkinBlur, 15 px d'origine ; ModalChrome).
    /// Par-dessus la palette : la PLAQUE (un reflet de métal posé sur ce qui est obtenu),
    /// le FILET EN ORBITE (un segment lumineux qui fait le tour d'un cadre en 5 s) et la
    /// police des TITRES. REPASSE DU 24/11 AU SOIR (la spécification finale de l'Atelier) : le
    /// SURVOL a sa couleur (Chrome.Hover), le « validé » la sienne (Chrome.Ok) ; la ligne choisie
    /// est une PILULE de verre (pas sous « Ascension ») ; le MENU CONTEXTUEL a ses coins coupés
    /// et un double filet (CutFrame) ; les grands dialogues ont une silhouette GALBÉE (GalbeFrame,
    /// ModalChrome). Le 25/11 : la Pile et le rail portent un VERRE DÉPOLI (une texture sous leur couleur,
    /// Skin.Glass.cs) — pas sous « Ascension ». Tout le reste suit le chemin commun des skins (Skin.cs).</summary>
    internal static partial class Skin
    {
        /// <summary>Vrai sous un skin de la série « Museum ».</summary>
        public static bool Museum { get { return Skins.IsMuseum(Current); } }

        private static Color _museumHighlight = Color.FromRgb(0xDB, 0x97, 0x3F);

        // Une ligne par skin et par mode. Les six premières triplettes décalent le FOND
        // (teinte en degrés, saturation ×, clarté +) : barre, relief, carte, champ, toile, filet.
        // Puis, depuis la teinte de l'ACCENT (décalage, saturation, clarté) : le filet fort,
        // l'accent, l'accent fort, le filet chaud, la tranche, le surlignage.
        private static readonly double[] FireDark = {
            1, 1.025, 0.037,   0, 0.781, 0.071,   0, 0.68, 0.088,   2, 0.587, 0.116,   0, 1.023, 0.02,   2, 0.467, 0.151,
            9, 0.331, 0.537,   0, 0.684, 0.616,   0, 0.679, 0.731,   10, 0.97, 0.869,   5, 0.548, 0.567,   9, 0.684, 0.553 };
        private static readonly double[] FireLight = {
            -1, 0.971, 0.045,   0, 1.035, 0.078,   0, 0.937, 0.088,   160, 0, 0.116,   0, 0.937, 0.102,   0, 0.418, -0.069,
            9, 0.28, 0.463,   0, 0.682, 0.42,   0, 0.68, 0.294,   0, 0.685, 0.361,   5, 0.548, 0.567,   9, 0.686, 0.449 };
        private static readonly double[] WoodDark = {
            -2, 1.01, 0.039,   -3, 0.761, 0.069,   -3, 0.684, 0.084,   -1, 0.571, 0.106,   0, 1.006, 0.02,   -3, 0.514, 0.135,
            -96, 0.193, 0.355,   0, 0.225, 0.661,   0, 0.223, 0.763,   0, 0.217, 0.82,   -96, 0.222, 0.567,   -102, 0.569, 0.6 };
        private static readonly double[] WoodLight = {
            0, 1.722, 0.116,   3, 1.565, 0.151,   -2, 1.614, 0.163,   -42, 0, 0.194,   3, 2.152, 0.178,   -2, 0.71, 0.016,
            -96, 0.227, 0.645,   0, 0.225, 0.4,   1, 0.231, 0.28,   4, 0.319, 0.363,   -96, 0.222, 0.567,   -102, 0.57, 0.508 };
        private static readonly double[] OathDark = {
            2, 0.895, 0.045,   4, 0.664, 0.078,   4, 0.599, 0.094,   4, 0.497, 0.12,   2, 0.903, 0.024,   6, 0.418, 0.155,
            -10, 0.356, 0.353,   0, 0.502, 0.575,   0, 0.5, 0.702,   5, 0.808, 0.755,   -1, 0.492, 0.363,   2, 0.701, 0.62 };
        private static readonly double[] OathLight = {
            0, 0.971, 0.045,   4, 1.035, 0.078,   1, 0.937, 0.088,   11, 0, 0.116,   11, 0.937, 0.102,   7, 0.36, -0.063,
            -9, 0.3, 0.647,   0, 0.505, 0.42,   0, 0.507, 0.294,   0, 0.5, 0.361,   -1, 0.492, 0.363,   2, 0.703, 0.449 };

        // « Ascension » (la Citadelle de l'Atelier) : bleu nuit, or ; le surlignage est un bleu-vert (135° plus loin).
        private static readonly double[] AscentDark = {
            10, 0.667, 0.059,   11, 0.471, 0.09,   9, 0.39, 0.106,   10, 0.31, 0.129,   8, 0.741, 0.031,   11, 0.24, 0.161,
            -3, 0.5, 0.314,   0, 0.56, 0.51,   0, 0.563, 0.659,   -1, 0.5, 0.882,   -1, 0.45, 0.608,   135, 0.905, 0.412 };
        private static readonly double[] AscentLight = {
            1, 0.971, 0.045,   7, 1.035, 0.078,   0, 0.937, 0.088,   140, 0, 0.116,   0, 0.937, 0.102,   2, 0.243, -0.061,
            -3, 0.425, 0.686,   0, 0.561, 0.42,   0, 0.56, 0.294,   0, 0.563, 0.359,   -1, 0.45, 0.608,   134, 0.904, 0.451 };

        /// <summary>Le fond effectif : celui choisi (borné par le mode), sinon celui du skin.</summary>
        private static Color MuseumGround(bool dark)
        {
            return Color.Parse(Skins.MuseumBackgroundFor(Current, dark, AppSettings.SkinBackground));
        }

        private static Color Hsl(double hue, double saturation, double lightness)
        {
            return Chrome.Tint(((hue % 360) + 360) % 360, Math.Max(0, Math.Min(1, saturation)), Math.Max(0, Math.Min(1, lightness)));
        }

        /// <summary>Une surface du skin : le fond, tourné de dh degrés, saturation × sk, clarté + dl.</summary>
        private static Color Shift(Color ground, double[] row, int index)
        {
            double h, s, l;
            Skins.ToHsl(Chrome.Hex(ground), out h, out s, out l);
            return Hsl(h + row[index * 3], s * row[index * 3 + 1], l + row[index * 3 + 2]);
        }

        private static Color Metal(double hue, double[] row, int index)
        {
            return Hsl(hue + row[index * 3], row[index * 3 + 1], row[index * 3 + 2]);
        }

        /// <summary>La palette d'un skin « Museum » (appelée par TunePalette).</summary>
        private static void TuneMuseum(string id, bool dark, double hue)
        {
            double[] row;
            int ink, soft, faint, hover, ok;
            if (id == Skins.MuseumWood)
            {
                row = dark ? WoodDark : WoodLight;
                if (dark) { ink = 0xDEDAD5; soft = 0xB7B0A6; faint = 0x8A8376; } else { ink = 0x2B2620; soft = 0x5A5247; faint = 0x8B8273; }
                hover = dark ? 0xB8E0C7 : 0x2F6B45; ok = 0x4F7D5B;
            }
            else if (id == Skins.MuseumOath)
            {
                row = dark ? OathDark : OathLight;
                if (dark) { ink = 0xF6EEE4; soft = 0xD8C3B6; faint = 0x9C8478; } else { ink = 0x1A130A; soft = 0x46352A; faint = 0x847066; }
                hover = dark ? 0xF3D98E : 0xAF8713; ok = 0xB8C08A;
            }
            else if (id == Skins.MuseumAscent)
            {
                row = dark ? AscentDark : AscentLight;
                // Le gris pâle du dessin (5B5A56) ne se lisait pas sur le bleu nuit : relevé d'un cran.
                if (dark) { ink = 0xF0E6D2; soft = 0xA09B8C; faint = 0x7A776F; } else { ink = 0x2B2211; soft = 0x726D61; faint = 0x8F8D86; }
                hover = dark ? 0xCDFAFA : 0x12B0B0; ok = 0x0AC8B9;
            }
            else
            {
                row = dark ? FireDark : FireLight;
                if (dark) { ink = 0xFBFDFF; soft = 0xC9D8E2; faint = 0x7F97A3; } else { ink = 0x020F1C; soft = 0x1F2C34; faint = 0x5F737D; }
                hover = dark ? 0xFEE2BD : 0xBF6E03; ok = 0x9EAE8F;
            }
            var ground = MuseumGround(dark);
            Surfaces(ground, Shift(ground, row, 0), Shift(ground, row, 1), Shift(ground, row, 2), Shift(ground, row, 3), Shift(ground, row, 4), Shift(ground, row, 5),
                Metal(hue, row, 6), Rgb(ink), Rgb(soft), Rgb(faint));
            Chrome.Accent.Color = Metal(hue, row, 7); // l'accent choisi, à la clarté du skin
            Chrome.AccentStrong.Color = Metal(hue, row, 8);
            Chrome.AccentTint.Color = Chrome.Blend(Chrome.BarBg.Color, Chrome.Accent.Color, 0.18);
            Chrome.AccentSoft.Color = Chrome.Blend(Chrome.BarBg.Color, Chrome.Accent.Color, 0.40);
            Chrome.Glow.Color = Metal(hue, row, 9);
            Chrome.Spine.Color = Metal(hue, row, 10);
            _museumHighlight = Metal(hue, row, 11);
            // Le survol : une couleur FIXE du skin (elle ne suit pas l'accent). Le « validé » (✓ Terminé,
            // états « terminé ») : la sauge, le vert, le bleu-vert du skin — sa clarté ramenée dans celle
            // du mode, pour rester lisible sur le fond.
            Chrome.Hover.Color = Rgb(hover);
            double oh, os, ol;
            Skins.ToHsl(Chrome.Hex(Rgb(ok)), out oh, out os, out ol);
            Chrome.Ok.Color = Hsl(oh, os, dark ? Math.Max(ol, 0.56) : Math.Min(ol, 0.36));
        }

        /// <summary>Le fond peint derrière tout : le fond du skin, un peu plus sombre vers le coin haut gauche.</summary>
        private static IBrush MuseumBackdrop(bool dark)
        {
            var ground = MuseumGround(dark);
            return Linear(Chrome.Blend(ground, Colors.Black, dark ? 0.22 : 0.08), ground, true);
        }

        /// <summary>Le verre des barres : l'option « Effet verre » (0 = opaque, 100 = très transparent).</summary>
        private static IBrush MuseumVeil(Color bar)
        {
            return new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * BarOpacity(Skins.MuseumGlassFor(Current, AppSettings.SkinGlass))), bar.R, bar.G, bar.B));
        }

        /// <summary>L'opacité des barres pour un effet verre donné : pleine à 0 %, 35 % à 100 % (accentué le 24/11).</summary>
        internal static double BarOpacity(int glass) { return 1 - Math.Max(0, Math.Min(100, glass)) * 0.0065; }

        /// <summary>L'opacité du fond des FENÊTRES DE DIALOGUE : de verre elles aussi, un peu plus que les
        /// barres — on voit l'application, voilée, au travers (ModalChrome).</summary>
        internal static double MuseumDialogGlass
        {
            get { return 1 - Math.Max(0, Math.Min(100, Skins.MuseumGlassFor(Current, AppSettings.SkinGlass))) * 0.0072; }
        }

        /// <summary>LE MÉTAL : un dégradé vertical clair en haut, la couleur au tiers, sombre en bas — le
        /// reflet de la tranche des cartons. Pour ce qui est PLEIN (bouton principal, onglet choisi).</summary>
        public static IBrush MetalBrush(Color color)
        {
            var dark = Chrome.Dark;
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            brush.GradientStops.Add(new GradientStop(Chrome.Blend(color, Colors.White, dark ? 0.34 : 0.20), 0));
            brush.GradientStops.Add(new GradientStop(Chrome.Blend(color, Colors.White, dark ? 0.10 : 0.04), 0.14));
            brush.GradientStops.Add(new GradientStop(color, 0.38));
            brush.GradientStops.Add(new GradientStop(Chrome.Blend(color, Colors.Black, dark ? 0.34 : 0.26), 1));
            return brush;
        }

        /// <summary>LA TRANCHE EN S d'une tuile colorée (à la place du dégradé des autres skins) : au bord
        /// droit de la barre de titre, une tranche de métal de la couleur de la tuile, aux angles en S,
        /// doublée d'un filet clair ; vers le titre, la couleur se fond jusqu'à disparaître. Null hors de
        /// la série « Museum » : l'appelant garde son dégradé.</summary>
        public static IBrush TileFade(Color color)
        {
            if (!Museum) return null;
            const double W = 210, H = 30, A = 17, B = 7, C = 21;
            var group = new DrawingGroup();
            // Le cadre de référence : toute la barre (le pinceau s'étire dessus).
            group.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, W, H)), Brush = Brushes.Transparent });
            // Le fondu vers le titre.
            var fade = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative) };
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, color.R, color.G, color.B), 1));
            group.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(W * 0.42, 0, W * 0.58, H)), Brush = fade });
            // La tranche : clair en haut, la couleur au tiers, sombre en bas.
            var metal = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            metal.GradientStops.Add(new GradientStop(Chrome.Blend(color, Colors.White, 0.45), 0));
            metal.GradientStops.Add(new GradientStop(color, 0.35));
            metal.GradientStops.Add(new GradientStop(Chrome.Blend(color, Colors.Black, 0.58), 1));
            var spine = new[]
            {
                new Point(W, 0), new Point(W - A, 0), new Point(W - A, H * 0.16), new Point(W - B, H * 0.30), new Point(W - B, H * 0.62),
                new Point(W - C, H * 0.80), new Point(W - C, H), new Point(W, H)
            };
            group.Children.Add(new GeometryDrawing { Geometry = new PolylineGeometry(spine, true), Brush = metal });
            // Le filet clair, parallèle, 4 px vers le titre.
            var line = new[]
            {
                new Point(W - A - 4, 0), new Point(W - A - 4, H * 0.19), new Point(W - B - 4, H * 0.33), new Point(W - B - 4, H * 0.59),
                new Point(W - C - 4, H * 0.77), new Point(W - C - 4, H)
            };
            var light = Chrome.Blend(color, Colors.White, 0.55);
            group.Children.Add(new GeometryDrawing { Geometry = new PolylineGeometry(line, false), Pen = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, light.R, light.G, light.B)), 1) });
            return new DrawingBrush(group) { Stretch = Stretch.Fill };
        }

        private static double MuseumRound(string id, double radius)
        {
            if (id == Skins.MuseumWood) return radius >= 8 ? 10 : Math.Min(radius, 3);
            if (id == Skins.MuseumOath) return radius >= 8 ? 6 : Math.Min(radius, 2);
            if (id == Skins.MuseumAscent) return 0; // angles droits
            return radius >= 8 ? 12 : Math.Min(radius, 4);
        }

        private static FontFamily MuseumUiFont(string id)
        {
            if (id == Skins.MuseumAscent) return new FontFamily("Source Sans 3, Source Sans Pro, Segoe UI, Helvetica Neue, DejaVu Sans");
            if (id == Skins.MuseumWood) return new FontFamily("Work Sans, Segoe UI, Helvetica Neue, DejaVu Sans");
            if (id == Skins.MuseumOath) return new FontFamily("IBM Plex Sans, Segoe UI, Helvetica Neue, DejaVu Sans");
            return new FontFamily("Inter, Segoe UI, Helvetica Neue, DejaVu Sans");
        }

        /// <summary>La police des TITRES (toasts, cartels), ou null hors de la série « Museum ». Les
        /// polices du dessin (Cormorant Garamond, EB Garamond, Libre Caslon Text, licence OFL) ne sont
        /// pas embarquées : installées sur la machine, elles sont prises ; sinon une garalde du système.</summary>
        public static FontFamily TitleFont
        {
            get
            {
                var id = Current;
                if (id == Skins.MuseumFire) return new FontFamily("Cormorant Garamond, Garamond, Palatino Linotype, Georgia, DejaVu Serif");
                if (id == Skins.MuseumWood) return new FontFamily("EB Garamond, Garamond, Palatino Linotype, Georgia, DejaVu Serif");
                if (id == Skins.MuseumOath) return new FontFamily("Libre Caslon Text, Book Antiqua, Palatino Linotype, Georgia, DejaVu Serif");
                if (id == Skins.MuseumAscent) return new FontFamily("Cinzel, Trajan Pro, Palatino Linotype, Book Antiqua, Georgia, DejaVu Serif");
                return null;
            }
        }

        /// <summary>Vrai sous un skin dont les titres sont en CAPITALES espacées (« Ascension »).</summary>
        public static bool Caps { get { return Skins.MuseumCaps(Current); } }

        /// <summary>Le texte d'un titre tel que le skin l'écrit : en capitales sous « Ascension ».</summary>
        public static string TitleText(string text)
        {
            return Caps && text != null ? text.ToUpper(System.Globalization.CultureInfo.CurrentCulture) : text;
        }

        /// <summary>Un titre prend la police des titres du skin, un peu plus grande (une garalde
        /// a l'œil petit) ; en capitales espacées sous « Ascension » (le texte déjà posé est
        /// converti : un texte posé APRÈS passe par TitleText). Rien hors de la série « Museum ».</summary>
        public static void Title(TextBlock block)
        {
            var font = TitleFont;
            if (font == null || block == null) return;
            block.FontFamily = font;
            block.FontWeight = FontWeight.Medium;
            if (Caps)
            {
                block.Text = TitleText(block.Text);
                block.LetterSpacing = block.FontSize * 0.05;
                block.FontSize = block.FontSize + 1;
            }
            else block.FontSize = block.FontSize + 2.5;
        }

        /// <summary>LA PLAQUE : un reflet de métal en diagonale, translucide, à poser PAR-DESSUS un
        /// fond pour ce qui est obtenu, validé, terminé. Null hors de la série « Museum ».</summary>
        public static IBrush Plaque()
        {
            if (!Museum) return null;
            var metal = Chrome.Accent.Color;
            var gold = _museumHighlight;
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x52, metal.R, metal.G, metal.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x2D, gold.R, gold.G, gold.B), 0.36));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 0.50));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x29, gold.R, gold.G, gold.B), 0.64));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x45, metal.R, metal.G, metal.B), 1));
            return brush;
        }

        /// <summary>Le filet en orbite d'un cadre, à poser dans le cadre (Child) à côté de son contenu :
        /// la marge négative le ramène sur la bordure. Il ne dessine rien hors de la série « Museum ».</summary>
        public static Control OrbitInside(Border frame)
        {
            var padding = frame.Padding;
            var line = frame.BorderThickness;
            return new OrbitLine(frame) { Margin = new Thickness(-(padding.Left + line.Left), -(padding.Top + line.Top), -(padding.Right + line.Right), -(padding.Bottom + line.Bottom)) };
        }

        /// <summary>Les styles de la série « Museum », par-dessus ceux du thème.</summary>
        private static void AddMuseumStyles(Styles styles)
        {
            var dark = Chrome.Dark;
            var bar = Chrome.BarBg.Color;
            var glow = Chrome.Glow.Color;
            var none = new BoxShadows();
            // Une touche de clavier de vitrine : le contour s'éclaire sous le pointeur.
            // Le survol a SA couleur, fixe (24/11 au soir) : le contour la prend, une lueur douce autour.
            var hover = Chrome.Hover.Color;
            if (Current == Skins.MuseumAscent)
                // « Ascension » : les filets sont en dégradé, de l'or vers le bronze.
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)), new Setter(TemplatedControl.BorderBrushProperty, Linear(Chrome.Accent.Color, Chrome.BorderStrong.Color, false))));
            styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Class(":pointerover"), new Setter(TemplatedControl.BorderBrushProperty, Chrome.Hover)));
            styles.Add(Rule(x => x.OfType<Button>().Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, none)));
            styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Class(":pointerover").Template().OfType<Border>().Name("Bg"),
                new Setter(Border.BoxShadowProperty, new BoxShadows(new BoxShadow { Blur = 10, Color = Color.FromArgb(0x59, hover.R, hover.G, hover.B) }))));
            // Ce qui est PLEIN est de métal (24/11, Rémi) : le bouton principal, l'onglet choisi.
            var metal = MetalBrush(Chrome.Accent.Color);
            var metalHot = MetalBrush(Chrome.Blend(Chrome.Accent.Color, glow, 0.35));
            var metalEdge = new SolidColorBrush(Chrome.Blend(Chrome.Accent.Color, Colors.Black, 0.30));
            if (!Skins.MuseumPill(Current))
            {
                // « Ascension » garde le métal plein.
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary), new Setter(TemplatedControl.BackgroundProperty, metal), new Setter(TemplatedControl.BorderBrushProperty, metalEdge)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Class(":pointerover"), new Setter(TemplatedControl.BackgroundProperty, metalHot), new Setter(TemplatedControl.BorderBrushProperty, Chrome.Hover)));
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":selected"), new Setter(TemplatedControl.BackgroundProperty, metal), new Setter(TemplatedControl.BorderBrushProperty, metalEdge)));
            }
            else
            {
                // LES RETOUCHES DU 24/11 AU SOIR (RETOUCHES.md, pièces 1 et 2 — Feu, Bois, Serment).
                // 1. LE BOUTON PRINCIPAL : « métal sombre à liseré ». Le corps est un métal noirci teinté de
                //    l'accent ; le métal clair ne vit plus que dans le liseré et dans le texte, qui luit.
                //    Le corps reste sombre au clair aussi (choix du dessin) : le texte y est donc toujours pâle.
                double ah, asat, al;
                Skins.ToHsl(Chrome.Hex(Chrome.Accent.Color), out ah, out asat, out al);
                var sat = Math.Min(asat, 0.6) * 0.78;
                var shineInk = dark ? glow : Hsl(ah, sat * 0.6, 0.86); // ce qui luit : le filet chaud ; au clair, sa version pâle
                var darkBody = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
                darkBody.GradientStops.Add(new GradientStop(Hsl(ah, sat * 0.30, 0.27), 0));
                darkBody.GradientStops.Add(new GradientStop(Hsl(ah, sat * 0.25, 0.20), 0.48));
                darkBody.GradientStops.Add(new GradientStop(Hsl(ah, sat * 0.25, 0.16), 0.50)); // l'horizon, discret
                darkBody.GradientStops.Add(new GradientStop(Hsl(ah, sat * 0.30, 0.20), 1));
                var rim = Rim(shineInk, Chrome.Accent.Color, Chrome.Blend(Chrome.Accent.Color, Colors.Black, 0.45));
                var rimHover = Rim(Colors.White, dark ? hover : shineInk, Chrome.Accent.Color);
                var accentHex = Chrome.Hex(Chrome.Accent.Color).Substring(1);
                var shineHex = Chrome.Hex(shineInk).Substring(1);
                var hoverHex = Chrome.Hex(dark ? hover : shineInk).Substring(1);
                var shadow = BoxShadows.Parse("inset 0 1 0 0 #1FFFFFFF, inset 0 0 12 0 #38" + accentHex + ", 0 1 2 0 #99000000");
                var shadowHover = BoxShadows.Parse("inset 0 1 0 0 #29FFFFFF, inset 0 0 14 0 #42" + shineHex + ", 0 0 12 0 #4D" + hoverHex);
                var shadowPressed = BoxShadows.Parse("inset 0 2 6 0 #8C000000, inset 0 0 10 0 #38" + accentHex);
                var shineBrush = new SolidColorBrush(shineInk);
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary),
                    new Setter(TemplatedControl.BackgroundProperty, darkBody), new Setter(TemplatedControl.BorderBrushProperty, rim), new Setter(TemplatedControl.ForegroundProperty, shineBrush)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, shadow)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Class(":pointerover"),
                    new Setter(TemplatedControl.BackgroundProperty, darkBody), new Setter(TemplatedControl.BorderBrushProperty, rimHover), new Setter(TemplatedControl.ForegroundProperty, Brushes.White)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Class(":pointerover").Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, shadowHover)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Class(":pressed").Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, shadowPressed)));
                // 2. L'ONGLET CHOISI : il garde le verre sombre et ne prend du métal que le BORD — un éclat
                //    au centre du haut, une lumière qui tombe dedans, le texte qui luit.
                var tabInk = dark ? (IBrush)Chrome.Glow : Chrome.AccentStrong;
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":selected"),
                    new Setter(TemplatedControl.BackgroundProperty, TabFill()), new Setter(TemplatedControl.BorderBrushProperty, TabRim()), new Setter(TemplatedControl.ForegroundProperty, tabInk)));
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":selected").Template().OfType<Border>().Name("Bg"),
                    new Setter(Avalonia.Controls.Documents.TextElement.ForegroundProperty, tabInk),
                    new Setter(Border.BoxShadowProperty, BoxShadows.Parse("inset 0 8 14 -10 #8C" + Chrome.Hex(glow).Substring(1)))));
            }
            // Un bouton ACTIF (alignement, gras, bascules) s'allume de l'intérieur (24/11, Rémi : « on a du
            // mal à distinguer les boutons actifs ») : un fond doré, un contour chaud, une lueur en dedans.
            var lit = Linear(Chrome.Blend(bar, glow, dark ? 0.40 : 0.34), Chrome.Blend(bar, Chrome.Accent.Color, dark ? 0.20 : 0.16), false);
            var litHot = Linear(Chrome.Blend(bar, glow, dark ? 0.52 : 0.44), Chrome.Blend(bar, Chrome.Accent.Color, dark ? 0.30 : 0.24), false);
            var inner = new BoxShadows(new BoxShadow { IsInset = true, Blur = 10, Spread = 1, Color = Color.FromArgb(dark ? (byte)0x8C : (byte)0x66, glow.R, glow.G, glow.B) });
            styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, none)));
            styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":checked"),
                new Setter(TemplatedControl.BackgroundProperty, lit), new Setter(TemplatedControl.BorderBrushProperty, Chrome.Glow),
                new Setter(TemplatedControl.ForegroundProperty, dark ? (IBrush)Chrome.Glow : Chrome.AccentStrong)));
            styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":checked").Class(":pointerover"), new Setter(TemplatedControl.BackgroundProperty, litHot)));
            styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":checked").Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, inner)));
            // Les barres de défilement : un pouce de métal, clair en haut, sombre en bas.
            var strong = Chrome.BorderStrong.Color;
            styles.Add(Rule(x => x.OfType<ScrollBar>().Template().OfType<Thumb>(),
                new Setter(TemplatedControl.BackgroundProperty, Linear(Chrome.Blend(strong, Colors.White, dark ? 0.38 : 0.30), Chrome.Blend(strong, Colors.Black, dark ? 0.45 : 0.30), false))));
            styles.Add(Rule(x => x.OfType<ScrollBar>().Template().OfType<Thumb>().Class(":pointerover"), new Setter(TemplatedControl.BackgroundProperty, metal)));
            // Menus et infobulles : un filet de métal.
            // LE MENU CONTEXTUEL (24/11 au soir, les deux variantes réunies) : bords droits, coins haut
            // droit et bas gauche coupés, et un second filet de métal, écarté, qui suit la découpe.
            styles.Add(Rule(x => x.OfType<ContextMenu>(),
                new Setter(TemplatedControl.BorderBrushProperty, Current == Skins.MuseumAscent ? Linear(Chrome.Accent.Color, Chrome.BorderStrong.Color, false) : (IBrush)Chrome.BorderStrong),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(5, 7, 5, 7)),
                new Setter(TemplatedControl.TemplateProperty, new Avalonia.Controls.Templates.FuncControlTemplate<ContextMenu>(CutMenuFace))));
            // Menus déroulés et listes déroulantes : un filet de métal, et la place du LOSANGE DU PIED
            // (FootDiamond, 25/11), à cheval sur leur bord bas.
            var footRoom = new Thickness(0, 0, 0, FootDiamond.Room);
            styles.Add(Rule(x => x.OfType<MenuItem>().Template().OfType<Popup>().Child().OfType<Border>(), new Setter(Border.BorderBrushProperty, Chrome.BorderStrong), new Setter(Layoutable.MarginProperty, footRoom)));
            styles.Add(Rule(x => x.OfType<ComboBox>().Template().OfType<Popup>().Child().OfType<Border>(), new Setter(Border.BorderBrushProperty, Chrome.BorderStrong), new Setter(Layoutable.MarginProperty, footRoom)));
            styles.Add(Rule(x => x.OfType<MenuFlyoutPresenter>().Template().OfType<Border>(), new Setter(Border.BorderBrushProperty, Chrome.BorderStrong), new Setter(Layoutable.MarginProperty, footRoom)));
            // L'INFOBULLE (25/11, Rémi) : elle se pare de la couleur dominante du skin — l'accent,
            // forcé — et son texte prend l'encre qui s'y lit le mieux, claire ou sombre (TipInk) ;
            // l'ombre portée est posée par le gabarit (Theme.TipFace).
            styles.Add(Rule(x => x.OfType<ToolTip>(),
                new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Chrome.Accent.Color)),
                new Setter(TemplatedControl.ForegroundProperty, new SolidColorBrush(TipInk(Chrome.Accent.Color)))));
            styles.Add(Rule(x => x.OfType<Separator>(), new Setter(TemplatedControl.BackgroundProperty, Chrome.BorderStrong), new Setter(Visual.OpacityProperty, 0.55)));
            if (!Skins.MuseumPill(Current))
            {
                // « Ascension » : la ligne choisie d'un arbre garde une barre de métal à gauche.
                styles.Add(Rule(x => x.OfType<TreeViewItem>().Class(":selected").Template().OfType<Border>().Name("SelectionBorder"),
                    new Setter(Border.BorderBrushProperty, Chrome.Accent), new Setter(Border.BorderThicknessProperty, new Thickness(2, 0, 0, 0))));
                return;
            }
            // LA PILULE (24/11 au soir, d'après l'écran titre du jeu) : la ligne choisie ou survolée de
            // la Pile, d'un menu, d'une liste est une pilule de verre — filet de métal, reflet clair au
            // milieu du bord haut, et pour la ligne CHOISIE une perle en losange à gauche.
            var pill = new CornerRadius(14 * CornerScale);
            var raised = Chrome.RaisedBg.Color;
            var body = Chrome.Blend(raised, hover, dark ? 0.10 : 0.14);
            var edgeColor = Chrome.Accent.Color;
            var edge = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.34, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.22, RelativeUnit.Relative) // resserré (24/11 au soir) : un reflet net
            };
            edge.GradientStops.Add(new GradientStop(glow, 0));
            edge.GradientStops.Add(new GradientStop(Color.FromArgb(0xA6, edgeColor.R, edgeColor.G, edgeColor.B), 1));
            // Le SURVOL est la même pilule, plus transparente (24/11 au soir) : verre, bord radial, lueur.
            var edgeSoft = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.34, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.22, RelativeUnit.Relative)
            };
            edgeSoft.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, glow.R, glow.G, glow.B), 0));
            edgeSoft.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, edgeColor.R, edgeColor.G, edgeColor.B), 1));
            var glowHex = Chrome.Hex(glow).Substring(1);
            var shine = BoxShadows.Parse("0 0 12 0 #29" + glowHex + ", inset 0 1 0 0 #12FFFFFF");
            var shineOver = BoxShadows.Parse("0 0 10 0 #1A" + glowHex + ", inset 0 1 0 0 #0CFFFFFF");
            // La PERLE de la Pile et des menus est posée à cheval sur le bord (PillPearl) : il lui faut
            // un peu de place à gauche, sans quoi le défilement de l'arbre ou du menu la rognerait.
            var pearlRoom = new Thickness(PillPearl.Room, 0, 0, 0);
            var glassPlain = new SolidColorBrush(Color.FromArgb(0xE6, body.R, body.G, body.B));
            var glassOver = new SolidColorBrush(Color.FromArgb(0x8C, body.R, body.G, body.B));
            // Les listes (25/11) : le verre seul — leur perle n'est plus peinte par le fond (elle tombait
            // sur l'icône de la ligne), elle est posée à cheval sur le bord par le gabarit (PillListFace).
            var glassOverList = glassOver;
            var glassOn = glassPlain;
            var thin = new Thickness(1);
            // — La Pile et les arbres.
            // Coins et épaisseur se règlent sur la LIGNE (le gabarit les lie à sa bande) : un style sans
            // état ne l'emporte pas sur la liaison d'un gabarit — seuls les styles à état le font.
            styles.Add(Rule(x => x.OfType<TreeViewItem>(),
                new Setter(TemplatedControl.CornerRadiusProperty, pill), new Setter(TemplatedControl.BorderThicknessProperty, thin), new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent)));
            styles.Add(Rule(x => x.OfType<TreeViewItem>().Template().OfType<Border>().Name("SelectionBorder"),
                new Setter(Border.CornerRadiusProperty, pill), new Setter(Border.BorderThicknessProperty, thin), new Setter(Border.BorderBrushProperty, Brushes.Transparent)));
            styles.Add(Rule(x => x.OfType<TreeViewItem>().Template().OfType<Border>().Name("SelectionBorder").Class(":pointerover"),
                new Setter(Border.BackgroundProperty, glassOver), new Setter(Border.BorderBrushProperty, edgeSoft), new Setter(Border.CornerRadiusProperty, pill), new Setter(Border.BorderThicknessProperty, thin), new Setter(Border.BoxShadowProperty, shineOver)));
            foreach (var focus in new[] { false, true })
                foreach (var over in new[] { false, true })
                {
                    var withFocus = focus;
                    var withOver = over;
                    styles.Add(Rule(delegate(Selector x)
                    {
                        x = x.OfType<TreeViewItem>().Class(":selected");
                        if (withFocus) x = x.Class(":focus");
                        x = x.Template().OfType<Border>().Name("SelectionBorder");
                        return withOver ? x.Class(":pointerover") : x;
                    }, new Setter(Border.BackgroundProperty, glassPlain), new Setter(Border.BorderBrushProperty, edge), new Setter(Border.CornerRadiusProperty, pill), new Setter(Border.BorderThicknessProperty, thin), new Setter(Border.BoxShadowProperty, shine)));
                }
            // — Les listes (« Ouvrir par le nom », les fenêtres à liste).
            styles.Add(Rule(x => x.OfType<ListBoxItem>(),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ListBoxItem>(PillListFace)),
                new Setter(TemplatedControl.CornerRadiusProperty, pill), new Setter(TemplatedControl.BorderThicknessProperty, thin), new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent)));
            styles.Add(Rule(x => x.OfType<ListBoxItem>().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                new Setter(Layoutable.MarginProperty, pearlRoom),
                new Setter(ContentPresenter.CornerRadiusProperty, pill), new Setter(ContentPresenter.BorderThicknessProperty, thin), new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent)));
            styles.Add(Rule(x => x.OfType<ListBoxItem>().Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                new Setter(ContentPresenter.BackgroundProperty, glassOverList), new Setter(ContentPresenter.BorderBrushProperty, edgeSoft), new Setter(ContentPresenter.CornerRadiusProperty, pill), new Setter(ContentPresenter.BorderThicknessProperty, thin), new Setter(ContentPresenter.BoxShadowProperty, shineOver)));
            foreach (var focus in new[] { false, true })
                foreach (var over in new[] { false, true })
                {
                    var withFocus = focus;
                    var withOver = over;
                    styles.Add(Rule(delegate(Selector x)
                    {
                        x = x.OfType<ListBoxItem>().Class(":selected");
                        if (withFocus) x = x.Class(":focus");
                        if (withOver) x = x.Class(":pointerover");
                        return x.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
                    }, new Setter(ContentPresenter.BackgroundProperty, glassOn), new Setter(ContentPresenter.BorderBrushProperty, edge), new Setter(ContentPresenter.CornerRadiusProperty, pill), new Setter(ContentPresenter.BorderThicknessProperty, thin), new Setter(ContentPresenter.BoxShadowProperty, shine)));
                }
            // — Les menus : l'entrée sous le pointeur (ou le clavier).
            styles.Add(Rule(x => x.OfType<MenuItem>(), new Setter(TemplatedControl.CornerRadiusProperty, pill)));
            styles.Add(Rule(x => x.OfType<MenuItem>().Class(":selected").Template().OfType<Border>().Name("root"),
                new Setter(Border.BackgroundProperty, glassPlain), new Setter(Border.BorderBrushProperty, edge), new Setter(Border.CornerRadiusProperty, pill), new Setter(Border.BoxShadowProperty, shine)));
            styles.Add(Rule(x => x.OfType<TreeViewItem>().Template().OfType<Border>().Name("SelectionBorder"), new Setter(Layoutable.MarginProperty, pearlRoom), new Setter(Border.BoxShadowProperty, none)));
            styles.Add(Rule(x => x.OfType<MenuItem>().Template().OfType<Border>().Name("root"), new Setter(Layoutable.MarginProperty, pearlRoom), new Setter(Border.BoxShadowProperty, none)));
            styles.Add(Rule(x => x.OfType<ListBoxItem>().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"), new Setter(ContentPresenter.BoxShadowProperty, none)));
        }

        /// <summary>Le gabarit d'une ligne de LISTE sous un skin à pilule (25/11) : celui du thème —
        /// un présentateur qui suit le fond, le filet, les coins et les marges de la ligne — plus la
        /// PERLE (PillPearl), à cheval sur le bord gauche de la pilule comme dans la Pile et les menus.</summary>
        private static Control PillListFace(ListBoxItem item, INameScope scope)
        {
            var presenter = new ContentPresenter { Name = "PART_ContentPresenter" };
            var priority = Avalonia.Data.BindingPriority.Template;
            presenter.Bind(ContentPresenter.BackgroundProperty, new Avalonia.Data.TemplateBinding(TemplatedControl.BackgroundProperty), priority);
            presenter.Bind(ContentPresenter.BorderBrushProperty, new Avalonia.Data.TemplateBinding(TemplatedControl.BorderBrushProperty), priority);
            presenter.Bind(ContentPresenter.BorderThicknessProperty, new Avalonia.Data.TemplateBinding(TemplatedControl.BorderThicknessProperty), priority);
            presenter.Bind(ContentPresenter.CornerRadiusProperty, new Avalonia.Data.TemplateBinding(TemplatedControl.CornerRadiusProperty), priority);
            presenter.Bind(ContentPresenter.ContentProperty, new Avalonia.Data.TemplateBinding(ContentControl.ContentProperty), priority);
            presenter.Bind(ContentPresenter.ContentTemplateProperty, new Avalonia.Data.TemplateBinding(ContentControl.ContentTemplateProperty), priority);
            presenter.Bind(ContentPresenter.PaddingProperty, new Avalonia.Data.TemplateBinding(TemplatedControl.PaddingProperty), priority);
            presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, new Avalonia.Data.TemplateBinding(ContentControl.HorizontalContentAlignmentProperty), priority);
            presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, new Avalonia.Data.TemplateBinding(ContentControl.VerticalContentAlignmentProperty), priority);
            presenter.RegisterInNameScope(scope);
            var host = new Panel();
            host.Children.Add(presenter);
            host.Children.Add(PillPearl.ForList(item, presenter));
            return host;
        }

        /// <summary>L'ILLUMINATION D'« ASCENSION » (25/11, Rémi) : le fond des tableaux de liège et de
        /// l'éditeur de texte prend un léger dégradé doré — la couleur dominante — qui part du coin bas
        /// droit vers le haut gauche et revient au fond habituel vers le quart du chemin, comme une
        /// faible lueur. Transparent au-delà : le fond peint du skin reste dessous. Null sous tout
        /// autre skin.</summary>
        internal static IBrush AscentGlow()
        {
            if (Current != Skins.MuseumAscent) return null;
            var gold = Chrome.Accent.Color;
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(1, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(Chrome.Dark ? (byte)0x3D : (byte)0x33, gold.R, gold.G, gold.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(Chrome.Dark ? (byte)0x14 : (byte)0x10, gold.R, gold.G, gold.B), AscentGlowReach * 0.55));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, gold.R, gold.G, gold.B), AscentGlowReach));
            return brush;
        }

        /// <summary>Où la lueur d'« Ascension » a rejoint le fond habituel, sur le chemin du coin bas droit au coin haut gauche.</summary>
        internal const double AscentGlowReach = 0.25;

        /// <summary>L'encre d'une infobulle posée sur <paramref name="ground"/> : la plus lisible des
        /// deux, presque blanche ou presque noire (rapport de contraste WCAG). L'une des deux passe
        /// toujours 4,5 : c'est la police qui s'adapte, jamais la couleur du skin.</summary>
        internal static Color TipInk(Color ground)
        {
            var pale = Color.FromRgb(0xFF, 0xFF, 0xFF);
            var deep = Color.FromRgb(0x14, 0x12, 0x10);
            return Chrome.ContrastRatio(pale, ground) >= Chrome.ContrastRatio(deep, ground) ? pale : deep;
        }

        private static LinearGradientBrush Rim(Color top, Color middle, Color bottom)
        {
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            brush.GradientStops.Add(new GradientStop(top, 0));
            brush.GradientStops.Add(new GradientStop(middle, 0.40));
            brush.GradientStops.Add(new GradientStop(bottom, 1));
            return brush;
        }

        /// <summary>L'ONGLET CHOISI des skins Feu, Bois, Serment (24/11 au soir) : son fond — le fond de
        /// la fenêtre, éclairé du haut par l'accent. Null ailleurs : l'appelant garde le sien.</summary>
        public static IBrush TabFill()
        {
            if (!Skins.MuseumPill(Current)) return null;
            var ground = Chrome.WindowBg.Color;
            return Linear(Chrome.Blend(ground, Chrome.Accent.Color, 0.22), ground, false);
        }

        /// <summary>…son contour : du métal, un éclat au centre du bord haut. Null ailleurs.</summary>
        public static IBrush TabRim()
        {
            if (!Skins.MuseumPill(Current)) return null;
            var metal = Chrome.Accent.Color;
            var rim = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.70, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(1.40, RelativeUnit.Relative)
            };
            rim.GradientStops.Add(new GradientStop(Chrome.Glow.Color, 0));
            rim.GradientStops.Add(new GradientStop(metal, 0.45));
            // Le dessin le fondait jusqu'à disparaître ; Rémi veut la bordure DORÉE jusqu'au pied (24/11).
            rim.GradientStops.Add(new GradientStop(Chrome.BorderStrong.Color, 1));
            return rim;
        }

        /// <summary>…et son pied, qui recouvre le filet de la rangée : le fond de la fenêtre. Null ailleurs.</summary>
        public static IBrush TabFoot() { return Skins.MuseumPill(Current) ? Chrome.WindowBg : null; }

        /// <summary>Le gabarit du menu contextuel sous un skin « Museum » : le cadre à coins coupés
        /// (CutFrame) à la place du cadre rond du thème ; les entrées, leur défilement et le clavier
        /// sont ceux du menu d'origine.</summary>
        private static Control CutMenuFace(ContextMenu menu, INameScope scope)
        {
            var items = new ItemsPresenter { Name = "PART_ItemsPresenter" };
            items.Bind(ItemsPresenter.ItemsPanelProperty, new Avalonia.Data.TemplateBinding(ItemsControl.ItemsPanelProperty), Avalonia.Data.BindingPriority.Template);
            Avalonia.Input.KeyboardNavigation.SetTabNavigation(items, Avalonia.Input.KeyboardNavigationMode.Continue);
            items.RegisterInNameScope(scope);
            var scroll = new ScrollViewer { Content = items };
            ScrollViewer.SetHorizontalScrollBarVisibility(scroll, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(scroll, ScrollBarVisibility.Auto);
            var frame = new CutFrame { Child = scroll, Tag = "cut-menu" };
            frame.Bind(CutFrame.BackgroundProperty, new Avalonia.Data.TemplateBinding(TemplatedControl.BackgroundProperty), Avalonia.Data.BindingPriority.Template);
            frame.Bind(CutFrame.BorderBrushProperty, new Avalonia.Data.TemplateBinding(TemplatedControl.BorderBrushProperty), Avalonia.Data.BindingPriority.Template);
            frame.Bind(Decorator.PaddingProperty, new Avalonia.Data.TemplateBinding(TemplatedControl.PaddingProperty), Avalonia.Data.BindingPriority.Template);
            return frame;
        }

        // — Sonde.
        internal static Color MuseumGroundForProbe { get { return MuseumGround(Chrome.Dark); } }
        internal static byte BarAlphaForProbe { get { var bar = ResourceForProbe(BarKey) as ISolidColorBrush; return bar == null ? (byte)255 : bar.Color.A; } }
    }

    /// <summary>LE FILET EN ORBITE (série « Museum », 24/11) : un segment lumineux de 16 % du
    /// périmètre glisse le long de la bordure d'un cadre et en fait le tour en 5 s. Posé
    /// PAR-DESSUS le cadre, il ne prend pas la souris. Il ne dessine — et ne redessine —
    /// que sous un skin « Museum », animations permises (Préférences) : ailleurs, il dort.
    /// FLUIDE (24/11 soir, Rémi : « rame pas mal ») : la position vient de l'horloge, image par
    /// image, au rythme de l'écran (RequestAnimationFrame) — plus d'un minuteur à 30 images
    /// par seconde qui passait après tout le reste ; et le filet est peint par QUATRE BANDES
    /// (une par bord) : seules celles qu'il traverse sont repeintes, pas tout le cadre et ce
    /// qu'il contient.</summary>
    internal sealed class OrbitLine : Panel
    {
        internal const double Thickness = 2.2, Turn = 5.0, Share = 0.16;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private readonly Border _frame;
        private readonly Band[] _bands = new Band[4];
        private DispatcherTimer _watch; // endormi : un coup d'œil deux fois par seconde, pour se réveiller si le skin change
        private bool _running, _attached;
        private Point[] _points = new Point[0];
        private Rect _box;

        public OrbitLine(Border frame)
        {
            _frame = frame;
            IsHitTestVisible = false;
            Tag = "orbit";
            for (var i = 0; i < 4; i++)
            {
                _bands[i] = new Band(this, i);
                Children.Add(_bands[i]);
            }
        }

        internal static bool Active { get { return Skin.Museum && AppSettings.Animations; } }

        /// <summary>Le filet ne tourne que tant que cette condition tient (25/11 : une tuile de profil
        /// ne l'allume qu'au survol). Null : toujours. Qui la fait basculer appelle Nudge().</summary>
        internal Func<bool> Gate;

        private bool Live { get { return Active && (Gate == null || Gate()); } }

        /// <summary>La condition vient de changer : le filet part tout de suite, sans attendre son coup d'œil.</summary>
        internal void Nudge() { Wake(); }

        private double Radius
        {
            get
            {
                double width = Bounds.Width - Thickness, height = Bounds.Height - Thickness;
                return Math.Max(0, Math.Min(_frame == null ? 0 : _frame.CornerRadius.TopLeft, Math.Min(width, height) / 2));
            }
        }

        /// <summary>La largeur d'une bande : le trait, et la courbe du coin qu'elle doit pouvoir peindre.</summary>
        private double BandSize { get { return Math.Ceiling(Radius + Thickness + 2); } }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = Math.Min(BandSize, Math.Min(finalSize.Width, finalSize.Height));
            _bands[0].Arrange(new Rect(0, 0, finalSize.Width, size));
            _bands[1].Arrange(new Rect(Math.Max(0, finalSize.Width - size), 0, size, finalSize.Height));
            _bands[2].Arrange(new Rect(0, Math.Max(0, finalSize.Height - size), finalSize.Width, size));
            _bands[3].Arrange(new Rect(0, 0, size, finalSize.Height));
            return finalSize;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var band in _bands) band.Measure(availableSize);
            return new Size(0, 0);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            if (_watch == null)
            {
                _watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _watch.Tick += delegate { Wake(); };
            }
            _watch.Start();
            Wake();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            if (_watch != null) _watch.Stop();
            base.OnDetachedFromVisualTree(e);
        }

        private void Wake()
        {
            if (_running || !_attached) return;
            if (!Live || !IsEffectivelyVisible) { Clear(); return; }
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            _running = true;
            top.RequestAnimationFrame(Frame);
        }

        private void Frame(TimeSpan time)
        {
            _running = false;
            if (!_attached) return;
            if (!Live || !IsEffectivelyVisible) { Clear(); return; }
            Advance();
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            _running = true;
            top.RequestAnimationFrame(Frame);
        }

        private void Clear()
        {
            if (_points.Length == 0) return;
            _points = new Point[0];
            _box = default(Rect);
            foreach (var band in _bands) band.Show(false);
        }

        /// <summary>Le segment de cet instant : ses points le long du périmètre, et les bandes qu'il touche.</summary>
        private void Advance()
        {
            double width = Bounds.Width - Thickness, height = Bounds.Height - Thickness;
            if (width < 12 || height < 12) { Clear(); return; }
            var radius = Radius;
            var perimeter = 2 * (width + height) - 8 * radius + 2 * Math.PI * radius;
            var start = (Clock.Elapsed.TotalSeconds / Turn % 1.0) * perimeter;
            var length = perimeter * Share;
            var count = Math.Max(2, (int)Math.Ceiling(length / 2.5) + 1);
            var points = new Point[count];
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            for (var i = 0; i < count; i++)
            {
                var point = At((start + length * i / (count - 1)) % perimeter, width, height, radius);
                point = new Point(point.X + Thickness / 2, point.Y + Thickness / 2);
                points[i] = point;
                if (point.X < left) left = point.X;
                if (point.X > right) right = point.X;
                if (point.Y < top) top = point.Y;
                if (point.Y > bottom) bottom = point.Y;
            }
            _points = points;
            _box = new Rect(left - Thickness, top - Thickness, right - left + 2 * Thickness, bottom - top + 2 * Thickness);
            foreach (var band in _bands) band.Show(band.Bounds.Intersects(_box));
        }

        /// <summary>Le point à la distance s sur le tour du cadre (sens des aiguilles, depuis la fin du coin haut gauche).</summary>
        private static Point At(double s, double width, double height, double radius)
        {
            double flatX = width - 2 * radius, flatY = height - 2 * radius, arc = Math.PI * radius / 2;
            if (s < flatX) return new Point(radius + s, 0);
            s -= flatX;
            if (s < arc) return Arc(width - radius, radius, radius, -90, s / Math.Max(arc, 0.0001));
            s -= arc;
            if (s < flatY) return new Point(width, radius + s);
            s -= flatY;
            if (s < arc) return Arc(width - radius, height - radius, radius, 0, s / Math.Max(arc, 0.0001));
            s -= arc;
            if (s < flatX) return new Point(width - radius - s, height);
            s -= flatX;
            if (s < arc) return Arc(radius, height - radius, radius, 90, s / Math.Max(arc, 0.0001));
            s -= arc;
            if (s < flatY) return new Point(0, height - radius - s);
            s -= flatY;
            return Arc(radius, radius, radius, 180, Math.Min(1, s / Math.Max(arc, 0.0001)));
        }

        private static Point Arc(double centerX, double centerY, double radius, double fromDegrees, double share)
        {
            var angle = (fromDegrees + 90 * share) * Math.PI / 180;
            return new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
        }

        internal bool DrawnForProbe { get { return _points.Length > 0; } }
        internal int BandsShownForProbe { get { var count = 0; foreach (var band in _bands) if (band.Shown) count++; return count; } }
        internal void AdvanceForProbe() { if (Live) Advance(); else Clear(); }

        /// <summary>Une bande du filet : elle peint la part du segment qui passe chez elle.</summary>
        private sealed class Band : Control
        {
            private readonly OrbitLine _owner;
            public bool Shown;

            public Band(OrbitLine owner, int index)
            {
                _owner = owner;
                IsHitTestVisible = false;
                ClipToBounds = true;
            }

            /// <summary>Repeinte si le segment passe (il a bougé), ou s'il vient de la quitter (un dernier passage l'efface).</summary>
            public void Show(bool shown)
            {
                if (!shown && !Shown) return;
                Shown = shown;
                InvalidateVisual();
            }

            public override void Render(DrawingContext context)
            {
                var points = _owner._points;
                if (!Shown || points.Length < 2) return;
                var origin = Bounds.Position;
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(points[0] - origin, false);
                    for (var i = 1; i < points.Length; i++) path.LineTo(points[i] - origin);
                    path.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(Chrome.Glow, Thickness) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
            }
        }
    }
}

using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE VERRE DÉPOLI DES COLONNES (2.0.0, 25/11/2026, Rémi) — sous « Gardien du Feu »,
    /// « Bois Blanc » et « Assermenté », la Pile et le rail (sa rangée d'onglets et ses panneaux) ne
    /// sont plus un aplat : une TEXTURE de verre rayé (assets/textures/verre.jpg, en niveaux de gris)
    /// est posée dessous, floutée, et la couleur de la barre passe par-dessus, à l'opacité de
    /// l'option « Effet verre ». De quoi donner un peu de relief à ces hautes zones d'une seule couleur.
    ///
    /// De la texture, seule la CLARTÉ sert, et seulement son écart à la moyenne : ce qui est plus
    /// clair que la moyenne éclaire, ce qui est plus sombre assombrit — la teinte et la clarté
    /// d'ensemble de la colonne restent celles du skin (une photo sombre posée telle quelle aurait
    /// noirci le parchemin de « Bois Blanc »). Au clair, le relief est retourné : une rayure y est
    /// une ombre. Texture, flou et couleur sont cuits en UNE image, posée en carreaux retournés d'un
    /// carreau à l'autre (la photo ne se raccorde pas d'elle-même) : aucune couche de plus à dessiner.
    ///
    /// Les deux réglages du skin s'y lisent : « Effet verre » à 0 % = une barre pleine, la texture ne
    /// se voit plus ; « Flou » règle le dépoli (0 px : les rayures nettes).
    /// Deux ressources : Side (Pile, rangée d'onglets du rail — translucides, comme les barres) et
    /// SidePanel (les panneaux du rail — opaques, comme avant). Sous « Ascension », c'est sa texture de
    /// VITRAIL que ces colonnes dépolissent (VitrailHaze). Ailleurs (les autres skins, « modern »), elles
    /// valent exactement ce que ces zones portaient : Bar et BarBgLight.</summary>
    internal static partial class Skin
    {
        public const string SideKey = "MbSide", SidePanelKey = "MbSidePanel";

        private const int TileWidth = 960, TileHeight = 700; // le carreau, en pixels cuits comme en unités d'écran
        private const double Relief = 0.50;      // la force du relief sous le verre (l'alpha du clair/sombre à l'écart maximal)
        private const double PanelGain = 0.5;    // la part du relief gardée sur les panneaux du rail
        private const double BlurShare = 0.2;    // la part du « Flou » du skin appliquée à la texture (15 px → 3 px)

        /// <summary>Vrai sous un skin dont les colonnes portent le verre dépoli.</summary>
        public static bool FrostedSides
        {
            get { var id = Current; return id == Skins.MuseumFire || id == Skins.MuseumWood || id == Skins.MuseumOath; }
        }

        /// <summary>Le fond de la Pile et de la rangée d'onglets du rail.</summary>
        public static void Side(Control control) { BindBackground(control, SideKey); }
        /// <summary>Le fond d'un panneau du rail (Général, Recherche, Versions, Lexique, Carnet…).</summary>
        public static void SidePanel(Control control) { BindBackground(control, SidePanelKey); }
        public static IBinding SideBinding { get { return new DynamicResourceExtension(SideKey); } }
        public static IBinding SidePanelBinding { get { return new DynamicResourceExtension(SidePanelKey); } }

        /// <summary>Publie les deux fonds (appelée par PublishResources, après Bar).</summary>
        private static void PublishSides(Application app, IBrush bar)
        {
            IBrush side = null, panel = null;
            _sideRelief = 0;
            if (FrostedSides)
            {
                var veil = bar as ISolidColorBrush;
                var glass = veil == null ? 1.0 : veil.Color.A / 255.0;
                var blur = Skins.MuseumBlurFor(Current, AppSettings.SkinBlur) * BlurShare;
                // « Bois Blanc » (25/11 au soir, Rémi) : sa texture est un MARBRE (tracé, Marble()), plus marqué que le verre rayé.
                var marble = Current == Skins.MuseumWood;
                var relief = Frost(blur, marble);
                var gain = marble ? MarbleGain : 1.0;
                // Verre à 0 % : la barre est pleine, il n'y a rien à voir dessous — les aplats d'avant.
                if (relief != null && veil != null && glass < 0.995)
                {
                    var light = !Chrome.Dark;
                    side = Tiled(Bake(relief, veil.Color, glass, glass, light, gain), 0, 0);
                    // Les panneaux restent opaques ; décalés d'un demi-carreau, ils ne répètent pas la Pile.
                    // Moitié moins de relief (25/11, Rémi : « un poil trop prononcée dans le rail ») : un panneau est plus clair que
                    // la Pile et porte du texte et des champs sur toute sa hauteur — le même écart y marquait bien plus.
                    panel = Tiled(Bake(relief, Chrome.BarBgLight.Color, 1, glass, light, PanelGain * gain), -TileWidth * 0.5, -TileHeight * 0.42);
                }
            }
            else if (AscensionArt.On)
            {
                // « ASCENSION » (25/11, Rémi : « la texture derrière la Pile est trop uniforme ») : la Pile et le rail
                // imposent leur verre à la texture de vitrail — sous eux elle n'est plus peinte nette par la fenêtre
                // (MainWindow.ClipVitrail) ; ils portent la leur, floutée, sous leur couleur.
                var veil = bar as ISolidColorBrush;
                var glass = veil == null ? 1.0 : veil.Color.A / 255.0;
                var haze = VitrailHaze(Skins.MuseumBlurFor(Current, AppSettings.SkinBlur) * BlurShare);
                if (haze != null && veil != null && glass < 0.995)
                {
                    var tone = AscensionArt.VitrailInk(Chrome.Dark);
                    side = HazeTiled(BakeHaze(haze, tone, veil.Color, glass, HazeStrength, 1 - glass));
                    panel = HazeTiled(BakeHaze(haze, tone, Chrome.BarBgLight.Color, 1, HazeStrength * 0.7, 1 - glass));
                }
            }
            app.Resources[SideKey] = side ?? bar;
            app.Resources[SidePanelKey] = panel ?? Chrome.BarBgLight;
        }

        private static IBrush Tiled(Bitmap image, double x, double y)
        {
            if (image == null) return null;
            return new ImageBrush(image)
            {
                TileMode = TileMode.FlipXY,
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                DestinationRect = new RelativeRect(x, y, TileWidth, TileHeight, RelativeUnit.Absolute)
            };
        }

        // ------------------------------------------------------------ la texture, floutée

        private static float[] _glassLight;   // la clarté de la photo, de 0 à 1 (lue une fois)
        private static bool _glassTried;
        private static float[] _frost;        // son écart à la moyenne, flouté, de −1 à 1
        private static bool _frostMarble;     // …cuit depuis le marbre (« Bois Blanc ») ou depuis le verre
        private static float[] _marble;
        private const double MarbleGain = 1.15, MarbleBlur = 0.3;

        /// <summary>LE MARBRE DE « BOIS BLANC » (25/11/2026 au soir, Rémi) : pas de photo — un marbre TRACÉ, de
        /// la taille du carreau. Des nuages lents (le grain de la pierre) et deux familles de VEINES : des
        /// bandes de sinus tordues par une turbulence, dont on ne garde que la crête. La clarté rendue
        /// vaut 0,5 pour la pierre nue ; une veine y monte vers 1 — claire au sombre, et retournée au
        /// clair (Bake) : des veines grises sur le parchemin. Le carreau se pose en miroir : aucun raccord.</summary>
        private static float[] Marble()
        {
            if (_marble != null) return _marble;
            var light = new float[TileWidth * TileHeight];
            for (var y = 0; y < TileHeight; y++)
                for (var x = 0; x < TileWidth; x++)
                {
                    var warp = Turbulence(x * 0.0042, y * 0.0042, 5, 11);
                    var fine = Turbulence(x * 0.011, y * 0.011, 4, 47);
                    var clouds = Turbulence(x * 0.0021, y * 0.0021, 4, 83) - 0.5;
                    // Les grandes veines, en diagonale ; les veinules, à contre-fil.
                    var major = 1 - Math.Abs(Math.Sin((x * 0.0019 + y * 0.0034) * Math.PI + warp * 5.0));
                    var minor = 1 - Math.Abs(Math.Sin((x * -0.0046 + y * 0.0027) * Math.PI + fine * 6.0 + 1.7));
                    var vein = Math.Max(Math.Pow(major, 38) * (0.5 + 0.5 * fine), Math.Pow(minor, 60) * 0.45 * warp);
                    // Un voile autour des grandes veines : la pierre s'éclaircit à leur approche.
                    var bloom = Math.Pow(major, 6) * 0.035;
                    light[y * TileWidth + x] = (float)Math.Max(0, Math.Min(1, 0.5 + clouds * 0.16 + bloom + vein * 0.5));
                }
            return _marble = light;
        }

        /// <summary>Un bruit de valeurs lissé, en octaves : de 0 à 1.</summary>
        private static double Turbulence(double x, double y, int octaves, int seed)
        {
            double sum = 0, weight = 0.5, total = 0;
            for (var octave = 0; octave < octaves; octave++)
            {
                sum += ValueNoise(x, y, seed + octave * 131) * weight;
                total += weight;
                x *= 2.03; y *= 2.03; weight *= 0.5;
            }
            return sum / total;
        }

        private static double ValueNoise(double x, double y, int seed)
        {
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            double a = Lattice(x0, y0, seed), b = Lattice(x0 + 1, y0, seed), c = Lattice(x0, y0 + 1, seed), d = Lattice(x0 + 1, y0 + 1, seed);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        private static double Lattice(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0xFFFFFF;
            }
        }
        private static double _frostBlur = -1;
        private static double _sideRelief;    // sonde : l'alpha le plus fort posé par le relief au dernier passage

        private static float[] GlassLight()
        {
            if (_glassTried) return _glassLight;
            _glassTried = true;
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("textures/verre.jpg"))
                using (var source = stream == null ? null : SkiaSharp.SKBitmap.Decode(stream))
                {
                    if (source == null) return null;
                    using (var scaled = source.Resize(new SkiaSharp.SKImageInfo(TileWidth, TileHeight, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul), SkiaSharp.SKFilterQuality.High))
                    {
                        if (scaled == null) return null;
                        var pixels = scaled.Bytes;
                        var light = new float[TileWidth * TileHeight];
                        for (var i = 0; i < light.Length; i++) light[i] = (float)((pixels[i * 4] * 0.114 + pixels[i * 4 + 1] * 0.587 + pixels[i * 4 + 2] * 0.299) / 255.0);
                        _glassLight = light;
                    }
                }
            }
            catch (Exception) { _glassLight = null; }
            return _glassLight;
        }

        /// <summary>Le relief : la clarté floutée de « blur » unités, ramenée autour de zéro. Gardé tant
        /// que le flou ne change pas.</summary>
        private static float[] Frost(double blur, bool marble = false)
        {
            var light = marble ? Marble() : GlassLight();
            if (light == null) return null;
            if (marble) blur *= MarbleBlur; // ses veines sont fines : un flou plein les effacerait
            if (_frost != null && Math.Abs(_frostBlur - blur) < 0.01 && _frostMarble == marble) return _frost;
            _frostMarble = marble;
            var frost = (float[])light.Clone();
            // Trois passes de moyenne glissante valent un flou gaussien ; les bords en miroir, comme les carreaux.
            var radius = (int)Math.Round(blur);
            if (radius > 0)
            {
                var spare = new float[frost.Length];
                for (var pass = 0; pass < 3; pass++)
                {
                    Smooth(frost, spare, TileWidth, TileHeight, radius, true);
                    Smooth(spare, frost, TileWidth, TileHeight, radius, false);
                }
            }
            double sum = 0, squares = 0;
            for (var i = 0; i < frost.Length; i++) { sum += frost[i]; squares += frost[i] * frost[i]; }
            var mean = sum / frost.Length;
            // L'écart type de la photo NETTE sert d'étalon : flouter adoucit le relief, il ne le regonfle pas.
            double rawSum = 0, rawSquares = 0;
            for (var i = 0; i < light.Length; i++) { rawSum += light[i]; rawSquares += light[i] * light[i]; }
            var rawMean = rawSum / light.Length;
            var spread = Math.Sqrt(Math.Max(1e-9, rawSquares / light.Length - rawMean * rawMean)) * 2.2;
            for (var i = 0; i < frost.Length; i++) frost[i] = (float)Math.Max(-1, Math.Min(1, (frost[i] - mean) / spread));
            _frostBlur = blur;
            return _frost = frost;
        }

        private static void Smooth(float[] from, float[] to, int width, int height, int radius, bool horizontal)
        {
            var count = horizontal ? width : height;
            var lines = horizontal ? height : width;
            var step = horizontal ? 1 : width;
            var span = radius * 2 + 1;
            for (var line = 0; line < lines; line++)
            {
                var start = horizontal ? line * width : line;
                double sum = 0;
                for (var k = -radius; k <= radius; k++) sum += from[start + Reflect(k, count) * step];
                for (var i = 0; i < count; i++)
                {
                    to[start + i * step] = (float)(sum / span);
                    sum += from[start + Reflect(i + radius + 1, count) * step] - from[start + Reflect(i - radius, count) * step];
                }
            }
        }

        private static int Reflect(int index, int count)
        {
            if (index < 0) index = -index - 1;
            if (index >= count) index = 2 * count - index - 1;
            return Math.Max(0, Math.Min(count - 1, index));
        }

        // ------------------------------------------------------------ la cuisson

        /// <summary>L'image d'une colonne : le relief (blanc ou noir, à l'alpha de son écart) SOUS la
        /// couleur « color » d'opacité « opacity ». « glass » est l'opacité du verre du skin : c'est elle
        /// qui dit combien du relief traverse (un panneau opaque garde le même relief que la Pile).</summary>
        private static Bitmap Bake(float[] relief, Color color, double opacity, double glass, bool invert, double gain)
        {
            try
            {
                var through = Math.Max(0, 1 - glass);
                // Au clair, un cran plus doux : sur un fond pâle, le même écart marque davantage (il veinait le parchemin).
                var strength = (invert ? Relief * 0.72 : Relief) * gain;
                var bytes = new byte[TileWidth * TileHeight * 4];
                for (var i = 0; i < relief.Length; i++)
                {
                    var gap = invert ? -relief[i] : relief[i];
                    var under = Math.Abs(gap) * strength;    // l'alpha du relief, sous le verre
                    var tone = gap > 0 ? 255.0 : 0.0;
                    var seen = under * through;              // ce qu'il en reste à travers le verre
                    if (seen > _sideRelief) _sideRelief = seen;
                    double alpha, r, g, b;
                    if (opacity >= 0.999)
                    {
                        alpha = 1;
                        r = color.R + (tone - color.R) * seen;
                        g = color.G + (tone - color.G) * seen;
                        b = color.B + (tone - color.B) * seen;
                    }
                    else
                    {
                        // Le verre par-dessus le relief, en couleurs prémultipliées.
                        alpha = opacity + under * (1 - opacity);
                        r = color.R * opacity + tone * under * (1 - opacity);
                        g = color.G * opacity + tone * under * (1 - opacity);
                        b = color.B * opacity + tone * under * (1 - opacity);
                    }
                    bytes[i * 4] = (byte)Math.Round(Math.Min(255, b));
                    bytes[i * 4 + 1] = (byte)Math.Round(Math.Min(255, g));
                    bytes[i * 4 + 2] = (byte)Math.Round(Math.Min(255, r));
                    bytes[i * 4 + 3] = (byte)Math.Round(255 * alpha);
                }
                var image = new WriteableBitmap(new PixelSize(TileWidth, TileHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using (var locked = image.Lock())
                {
                    if (locked.RowBytes == TileWidth * 4) Marshal.Copy(bytes, 0, locked.Address, bytes.Length);
                    else for (var y = 0; y < TileHeight; y++) Marshal.Copy(bytes, y * TileWidth * 4, locked.Address + y * locked.RowBytes, TileWidth * 4);
                }
                return image;
            }
            // Le moteur de rendu pas encore prêt (tout début du lancement) : l'aplat, on republiera.
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------ « Ascension » : le vitrail sous le verre

        private const int HazeSide = 360;          // la tuile de 180 unités, cuite au double
        private const double HazeStrength = 0.34;  // l'alpha du trait flouté, là où il est le plus dense, sous le verre
        private static float[] _hazeSharp, _haze;
        private static double _hazeBlur = -1;

        /// <summary>La tuile de vitrail, FLOUTÉE de « blur » unités : la densité du trait, de 0 à 1 (le plus dense
        /// vaut 1). La tuile est symétrique : les bords en miroir du flou valent sa répétition.</summary>
        private static float[] VitrailHaze(double blur)
        {
            try
            {
                if (_hazeSharp == null)
                {
                    using (var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(HazeSide, HazeSide, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul)))
                    using (var canvas = new SkiaSharp.SKCanvas(bitmap))
                    using (var paint = new SkiaSharp.SKPaint { IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 1, Color = SkiaSharp.SKColors.White })
                    using (var path = SkiaSharp.SKPath.ParseSvgPathData(AscensionArt.VitrailPath))
                    {
                        canvas.Clear(SkiaSharp.SKColors.Transparent);
                        canvas.Scale(HazeSide / (float)AscensionArt.TextureTile);
                        canvas.DrawPath(path, paint);
                        canvas.DrawOval(new SkiaSharp.SKRect(30, 30, 150, 150), paint);
                        canvas.DrawOval(new SkiaSharp.SKRect(2, 2, 178, 178), paint);
                        canvas.Flush();
                        var pixels = bitmap.Bytes;
                        var sharp = new float[HazeSide * HazeSide];
                        for (var i = 0; i < sharp.Length; i++) sharp[i] = pixels[i * 4 + 3] / 255f;
                        _hazeSharp = sharp;
                    }
                }
            }
            catch (Exception) { return null; }
            if (_haze != null && Math.Abs(_hazeBlur - blur) < 0.01) return _haze;
            var haze = (float[])_hazeSharp.Clone();
            var radius = (int)Math.Round(blur * HazeSide / AscensionArt.TextureTile);
            if (radius > 0)
            {
                var spare = new float[haze.Length];
                for (var pass = 0; pass < 3; pass++)
                {
                    Smooth(haze, spare, HazeSide, HazeSide, radius, true);
                    Smooth(spare, haze, HazeSide, HazeSide, radius, false);
                }
            }
            float top = 0;
            for (var i = 0; i < haze.Length; i++) if (haze[i] > top) top = haze[i];
            if (top > 0) for (var i = 0; i < haze.Length; i++) haze[i] /= top;
            _hazeBlur = blur;
            return _haze = haze;
        }

        /// <summary>L'image d'une colonne d'« Ascension » : le trait flouté (« tone », à l'alpha de sa densité)
        /// SOUS la couleur « color » d'opacité « opacity » ; « through » dit ce qu'un fond opaque en laisse voir.</summary>
        private static Bitmap BakeHaze(float[] haze, Color tone, Color color, double opacity, double strength, double through)
        {
            try
            {
                var bytes = new byte[HazeSide * HazeSide * 4];
                for (var i = 0; i < haze.Length; i++)
                {
                    var under = haze[i] * strength;
                    var seen = under * through;
                    if (seen > _sideRelief) _sideRelief = seen;
                    double alpha, r, g, b;
                    if (opacity >= 0.999)
                    {
                        alpha = 1;
                        r = color.R + (tone.R - color.R) * seen;
                        g = color.G + (tone.G - color.G) * seen;
                        b = color.B + (tone.B - color.B) * seen;
                    }
                    else
                    {
                        alpha = opacity + under * (1 - opacity);
                        r = color.R * opacity + tone.R * under * (1 - opacity);
                        g = color.G * opacity + tone.G * under * (1 - opacity);
                        b = color.B * opacity + tone.B * under * (1 - opacity);
                    }
                    bytes[i * 4] = (byte)Math.Round(Math.Min(255, b));
                    bytes[i * 4 + 1] = (byte)Math.Round(Math.Min(255, g));
                    bytes[i * 4 + 2] = (byte)Math.Round(Math.Min(255, r));
                    bytes[i * 4 + 3] = (byte)Math.Round(255 * alpha);
                }
                var image = new WriteableBitmap(new PixelSize(HazeSide, HazeSide), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                using (var locked = image.Lock())
                {
                    if (locked.RowBytes == HazeSide * 4) Marshal.Copy(bytes, 0, locked.Address, bytes.Length);
                    else for (var y = 0; y < HazeSide; y++) Marshal.Copy(bytes, y * HazeSide * 4, locked.Address + y * locked.RowBytes, HazeSide * 4);
                }
                return image;
            }
            catch (Exception) { return null; }
        }

        private static IBrush HazeTiled(Bitmap image)
        {
            if (image == null) return null;
            return new ImageBrush(image)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                DestinationRect = new RelativeRect(0, 0, AscensionArt.TextureTile, AscensionArt.TextureTile, RelativeUnit.Absolute)
            };
        }

        // — Sonde.
        internal static bool GlassTextureFoundForProbe { get { return GlassLight() != null; } }
        internal static double SideReliefForProbe { get { return _sideRelief; } }
        internal static bool MarbleSidesForProbe { get { return _frost != null && _frostMarble; } }
    }
}

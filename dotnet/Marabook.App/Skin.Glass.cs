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
    /// SidePanel (les panneaux du rail — opaques, comme avant). Ailleurs (« Ascension », les autres
    /// skins, « modern »), elles valent exactement ce que ces zones portaient : Bar et BarBgLight.</summary>
    internal static partial class Skin
    {
        public const string SideKey = "MbSide", SidePanelKey = "MbSidePanel";

        private const int TileWidth = 960, TileHeight = 700; // le carreau, en pixels cuits comme en unités d'écran
        private const double Relief = 0.50;      // la force du relief sous le verre (l'alpha du clair/sombre à l'écart maximal)
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
                var relief = Frost(blur);
                // Verre à 0 % : la barre est pleine, il n'y a rien à voir dessous — les aplats d'avant.
                if (relief != null && veil != null && glass < 0.995)
                {
                    var light = !Chrome.Dark;
                    side = Tiled(Bake(relief, veil.Color, glass, glass, light), 0, 0);
                    // Les panneaux restent opaques ; décalés d'un demi-carreau, ils ne répètent pas la Pile.
                    panel = Tiled(Bake(relief, Chrome.BarBgLight.Color, 1, glass, light), -TileWidth * 0.5, -TileHeight * 0.42);
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
        private static float[] Frost(double blur)
        {
            var light = GlassLight();
            if (light == null) return null;
            if (_frost != null && Math.Abs(_frostBlur - blur) < 0.01) return _frost;
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
        private static Bitmap Bake(float[] relief, Color color, double opacity, double glass, bool invert)
        {
            try
            {
                var through = Math.Max(0, 1 - glass);
                // Au clair, un cran plus doux : sur un fond pâle, le même écart marque davantage (il veinait le parchemin).
                var strength = invert ? Relief * 0.72 : Relief;
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

        // — Sonde.
        internal static bool GlassTextureFoundForProbe { get { return GlassLight() != null; } }
        internal static double SideReliefForProbe { get { return _sideRelief; } }
    }
}

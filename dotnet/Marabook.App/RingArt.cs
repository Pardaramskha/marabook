using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LES ANNEAUX D'UN ASTRE (lot 16, 17/11/2026, Rémi : « on distingue une coupure en rayon
    /// au niveau du centre des cercles, et l'épaisseur ne reste pas constante selon l'angle »).
    ///
    /// Les anneaux ne sont plus des ellipses tracées au pinceau, moitié par moitié : c'est un DISQUE
    /// plat, peint une fois de face dans une image (ses bandes, sa division, son grain), puis posé dans
    /// l'espace — l'image est écrasée et tournée comme l'est le plan des anneaux vu d'où l'on regarde.
    /// La largeur des bandes est donc juste sous tous les angles, et il n'y a plus de raccord : la passe
    /// de derrière dessine le disque ENTIER (l'astre, opaque, le recouvre), la passe de devant ne
    /// redessine que ce qui passe devant l'astre, à l'intérieur de son disque.
    ///
    /// Le GRAIN vient de la texture fournie par Rémi (assets/textures/anneaux.jpg) : seule sa clarté
    /// sert — elle éclaircit, assombrit et ajoure la teinte des anneaux, sans jamais la changer.</summary>
    internal static class RingArt
    {
        // ------------------------------------------------------------ le grain

        private static float[] _grain;
        private const int GrainSide = 512;

        /// <summary>La clarté de la texture, ramenée autour de zéro (de −1 à 1) : ce qui s'écarte de la
        /// moyenne fait le grain. Sans la texture (ressource absente), un grain nul : des bandes lisses.</summary>
        private static float[] Grain()
        {
            if (_grain != null) return _grain;
            var grain = new float[GrainSide * GrainSide];
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("textures/anneaux.jpg"))
                using (var source = stream == null ? null : SkiaSharp.SKBitmap.Decode(stream))
                {
                    if (source != null)
                        using (var scaled = source.Resize(new SkiaSharp.SKImageInfo(GrainSide, GrainSide, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul), SkiaSharp.SKFilterQuality.High))
                        {
                            var pixels = scaled.Bytes;
                            double sum = 0, squares = 0;
                            for (var i = 0; i < grain.Length; i++)
                            {
                                var light = (pixels[i * 4] * 0.114 + pixels[i * 4 + 1] * 0.587 + pixels[i * 4 + 2] * 0.299) / 255.0;
                                grain[i] = (float)light;
                                sum += light;
                                squares += light * light;
                            }
                            var mean = sum / grain.Length;
                            var spread = Math.Sqrt(Math.Max(1e-9, squares / grain.Length - mean * mean)) * 2.4;
                            for (var i = 0; i < grain.Length; i++) grain[i] = (float)Math.Max(-1, Math.Min(1, (grain[i] - mean) / spread));
                        }
                }
            }
            catch (Exception) { Array.Clear(grain, 0, grain.Length); }
            return _grain = grain;
        }

        /// <summary>Le grain en un point du plan des anneaux — la texture est posée en miroir d'un carreau
        /// à l'autre (elle ne se raccorde pas d'elle-même : sans cela, on verrait ses bords).</summary>
        private static float GrainAt(float[] grain, double u, double v)
        {
            u = Mirror(u);
            v = Mirror(v);
            var x = u * (GrainSide - 1);
            var y = v * (GrainSide - 1);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(GrainSide - 1, x0 + 1), y1 = Math.Min(GrainSide - 1, y0 + 1);
            double fx = x - x0, fy = y - y0;
            var top = grain[y0 * GrainSide + x0] * (1 - fx) + grain[y0 * GrainSide + x1] * fx;
            var bottom = grain[y1 * GrainSide + x0] * (1 - fx) + grain[y1 * GrainSide + x1] * fx;
            return (float)(top * (1 - fy) + bottom * fy);
        }

        private static double Mirror(double t)
        {
            t = t - 2 * Math.Floor(t / 2);
            return t > 1 ? 2 - t : t;
        }

        // ------------------------------------------------------------ les bandes

        private static double Step(double edge, double t) { return Math.Max(0, Math.Min(1, (t - edge) / 0.012 + 0.5)); }

        /// <summary>L'opacité des anneaux du bord intérieur (0) au bord extérieur (1) : un anneau pâle,
        /// le grand anneau, une division, un anneau moyen fendu d'un sillon, un vide, un liseré.</summary>
        internal static double Band(double t)
        {
            if (t < 0 || t > 1) return 0;
            var alpha = 0.34 + (0.92 - 0.34) * Step(0.17, t);
            alpha += (0.07 - alpha) * Step(0.56, t);
            alpha += (0.66 - alpha) * Step(0.63, t);
            alpha += (0.22 - alpha) * Step(0.835, t) * (1 - Step(0.855, t));
            alpha += (0.0 - alpha) * Step(0.92, t);
            alpha += (0.30 - alpha) * Step(0.955, t);
            // De fins sillons concentriques : les anneaux sont faits de milliers d'annelets.
            return alpha * (0.87 + 0.13 * Math.Sin(t * 96 + 0.7) * Math.Sin(t * 37));
        }

        // ------------------------------------------------------------ l'image de face

        private sealed class Face
        {
            public string Key;
            public Bitmap Image;
            public int[] Buffer;
        }

        private static readonly List<Face> Faces = new List<Face>();
        private static int _built;
        internal static int FacesBuiltForProbe { get { return _built; } }

        /// <summary>Le disque des anneaux vu de face, de `pixels` de côté : son bord est le bord EXTÉRIEUR
        /// des anneaux. Gardé d'une image à l'autre tant que rien de ce qui le dessine ne change.</summary>
        internal static Bitmap FaceOf(BodyLook look, Color color, int pixels) { return FaceEntry(look, color, pixels).Image; }

        private static Face FaceEntry(BodyLook look, Color color, int pixels)
        {
            double inner, outer;
            look.RingSpan(out inner, out outer);
            var inv = CultureInfo.InvariantCulture;
            var key = pixels + "|" + color + "|" + inner.ToString("R", inv) + "|" + outer.ToString("R", inv) + "|" + (look.RingTexture ? look.RingGrain.ToString("R", inv) : "-");
            for (var i = 0; i < Faces.Count; i++)
                if (Faces[i].Key == key)
                {
                    var found = Faces[i];
                    if (i > 0) { Faces.RemoveAt(i); Faces.Insert(0, found); }
                    return found;
                }

            var buffer = new int[pixels * pixels];
            var grain = look.RingTexture ? Grain() : null;
            var half = pixels / 2.0;
            var hole = inner / outer;                       // le bord intérieur, en part du rayon de l'image
            var edge = 1.0 / half;                          // un pixel, en part du rayon
            // Le grain : à l'échelle 1, la texture couvre deux rayons de l'astre.
            var grainScale = outer / (2.0 * Math.Max(0.25, Math.Min(4, look.RingGrain)));
            System.Threading.Tasks.Parallel.For(0, pixels, delegate(int py)
            {
                var y = (py + 0.5 - half) / half;
                var row = py * pixels;
                for (var px = 0; px < pixels; px++)
                {
                    var x = (px + 0.5 - half) / half;
                    var r = Math.Sqrt(x * x + y * y);
                    if (r >= 1 || r <= hole) continue;
                    var alpha = Band((r - hole) / (1 - hole));
                    // Les deux bords s'estompent sur un pixel : pas d'escalier.
                    alpha *= Math.Min(1, (1 - r) / edge) * Math.Min(1, (r - hole) / edge);
                    var light = 1.0;
                    if (grain != null)
                    {
                        var g = GrainAt(grain, x * grainScale + 0.5, y * grainScale + 0.5);
                        light = 1 + 0.55 * g;               // la clarté seule : la teinte reste celle des anneaux
                        alpha *= Math.Max(0.25, Math.Min(1.25, 0.86 + 0.6 * g));
                    }
                    if (alpha <= 0.004) continue;
                    if (alpha > 1) alpha = 1;
                    var a = (int)(alpha * 255);
                    var red = (int)Math.Min(255, color.R * light * alpha);
                    var green = (int)Math.Min(255, color.G * light * alpha);
                    var blue = (int)Math.Min(255, color.B * light * alpha);
                    buffer[row + px] = (a << 24) | (Math.Min(a, red) << 16) | (Math.Min(a, green) << 8) | Math.Min(a, blue);
                }
            });
            var image = new WriteableBitmap(new PixelSize(pixels, pixels), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var frame = image.Lock())
            {
                if (frame.RowBytes == pixels * 4) Marshal.Copy(buffer, 0, frame.Address, buffer.Length);
                else for (var py = 0; py < pixels; py++) Marshal.Copy(buffer, py * pixels, frame.Address + py * frame.RowBytes, pixels);
            }
            _built++;
            var made = new Face { Key = key, Image = image, Buffer = buffer };
            Faces.Insert(0, made);
            if (Faces.Count > 24) Faces.RemoveAt(Faces.Count - 1);
            return made;
        }

        // ------------------------------------------------------------ dans l'espace

        /// <summary>Ce qui entoure les anneaux (lot 17) : un repère fixé à l'astre, pour qu'ils tournent avec
        /// lui quand on le fait pivoter, et la direction du soleil, pour leurs ombres. Tout est dit dans le
        /// repère de l'écran : x vers la droite, y vers le haut, z vers nous.</summary>
        internal sealed class Scene
        {
            public bool HasRef;                  // une direction du plan des anneaux, attachée à l'astre
            public double Rx, Ry, Rz;
            public bool HasSun;                  // d'où vient la lumière
            public double Sx, Sy, Sz;
        }

        /// <summary>La clarté des anneaux vus par leur face que le soleil n'éclaire pas, et dans l'ombre de l'astre.</summary>
        internal const double UnlitFace = 0.5, InShadow = 0.14;

        internal static int ShadowsDrawnForProbe { get; private set; }
        internal static double LastTurnForProbe { get; private set; }
        internal static double LastFaceLightForProbe { get; private set; }

        private static double Smooth(double from, double to, double t)
        {
            t = Math.Max(0, Math.Min(1, (t - from) / (to - from)));
            return t * t * (3 - 2 * t);
        }

        /// <summary>La clarté de la face qu'on voit, selon la hauteur du soleil au-dessus d'elle (de −1 à 1).
        /// LOT 18 (Rémi : « ils changent brutalement de seuil de luminosité ») : elle glisse d'une clarté à
        /// l'autre tant que le soleil est près du plan des anneaux, au lieu de basculer d'un coup.</summary>
        internal static double FaceLight(double height)
        {
            return UnlitFace + (1 - UnlitFace) * Smooth(-0.32, 0.32, height);
        }

        /// <summary>L'ombre de l'astre en un point du plan de ses anneaux (0 = en pleine lumière, 1 = dans
        /// l'ombre). `back` : la distance derrière l'astre, à l'opposé du soleil ; `aside` : l'écart à
        /// l'axe de l'ombre ; `along` : la hauteur du soleil sur le plan. LOT 18 (Rémi : « un poil trop
        /// nette ») : ses bords sont fondus, et de plus en plus à mesure qu'on s'éloigne de l'astre.</summary>
        internal static double ShadowAt(double back, double aside, double along)
        {
            if (back <= 0) return 0;
            var off = Math.Sqrt(aside * aside + back * back * along * along);
            var soft = 0.07 + 0.05 * Math.Min(3, back);
            return 1 - Smooth(1 - soft, 1 + soft, off);
        }

        // Les anneaux ÉCLAIRÉS d'un astre : l'image de face, reprise pixel par pixel (lot 18). Une image
        // par astre, repeinte quand la lumière ou la rotation change.
        private sealed class Lit
        {
            public WriteableBitmap Image;
            public int[] Buffer;
            public string Key;
        }

        private static readonly Dictionary<string, Lit> Lits = new Dictionary<string, Lit>();

        private static Bitmap LitFace(BodyLook look, Face face, int pixels, double outer, double faceLight, bool shadow, double dx, double dy, double along)
        {
            var inv = CultureInfo.InvariantCulture;
            var owner = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(look) + "/" + pixels;
            var key = face.Key + "|" + faceLight.ToString("0.###", inv) + "|" + (shadow ? dx.ToString("0.####", inv) + "/" + dy.ToString("0.####", inv) + "/" + along.ToString("0.####", inv) : "-");
            Lit lit;
            if (!Lits.TryGetValue(owner, out lit))
            {
                if (Lits.Count > 24) Lits.Clear();
                lit = new Lit();
                Lits[owner] = lit;
            }
            if (lit.Key == key && lit.Image != null) return lit.Image;
            if (lit.Image == null || lit.Image.PixelSize.Width != pixels)
            {
                lit.Image = new WriteableBitmap(new PixelSize(pixels, pixels), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                lit.Buffer = new int[pixels * pixels];
            }
            int[] source = face.Buffer, target = lit.Buffer;
            var half = pixels / 2.0;
            System.Threading.Tasks.Parallel.For(0, pixels, delegate(int py)
            {
                var y = (py + 0.5 - half) / half * outer;
                var row = py * pixels;
                for (var px = 0; px < pixels; px++)
                {
                    var packed = source[row + px];
                    if (packed == 0) { target[row + px] = 0; continue; }
                    var light = faceLight;
                    if (shadow)
                    {
                        var x = (px + 0.5 - half) / half * outer;
                        var back = -(x * dx + y * dy);
                        if (back > 0) light += (InShadow - light) * ShadowAt(back, y * dx - x * dy, along);
                    }
                    var k = (int)(light * 256);
                    target[row + px] = (packed & unchecked((int)0xFF000000)) | ((((packed >> 16) & 0xFF) * k >> 8) << 16) | ((((packed >> 8) & 0xFF) * k >> 8) << 8) | ((packed & 0xFF) * k >> 8);
                }
            });
            using (var frame = lit.Image.Lock())
            {
                if (frame.RowBytes == pixels * 4) Marshal.Copy(target, 0, frame.Address, target.Length);
                else for (var py = 0; py < pixels; py++) Marshal.Copy(target, py * pixels, frame.Address + py * frame.RowBytes, pixels);
            }
            lit.Key = key;
            return lit.Image;
        }

        /// <summary>Dessine les anneaux d'un astre de rayon `radius` à l'écran, centré en `centre`.
        /// (nx, ny, nz) est la NORMALE au plan des anneaux, vue de l'écran : x vers la droite, y vers le
        /// haut, z vers nous. `spin` les fait tourner dans leur plan. Passe de derrière (front faux) : le
        /// disque entier, à dessiner AVANT l'astre. Passe de devant : la part qui passe devant lui.
        /// Avec une scène (lot 17) : ils suivent l'astre quand il pivote, l'astre y porte son ombre, et la
        /// face que le soleil n'éclaire pas est plus sombre.</summary>
        internal static void Draw(DrawingContext context, Point centre, double radius, double nx, double ny, double nz, BodyLook look, Color color, double spin, bool front, int pixels, Scene scene = null)
        {
            if (look == null || !look.Rings || radius < 0.5) return;
            double inner, outer;
            look.RingSpan(out inner, out outer);
            var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length < 1e-9) return;
            nx /= length; ny /= length; nz /= length;
            // Vus exactement par la tranche, des anneaux sans épaisseur disparaîtraient : un fil reste.
            if (Math.Abs(nz) < 0.012) nz = nz < 0 ? -0.012 : 0.012;
            var flat = Math.Sqrt(nx * nx + ny * ny);
            // Le grand axe de l'ellipse : dans le plan de l'écran, perpendiculaire à la normale.
            double ax = flat < 1e-9 ? 1 : -ny / flat, ay = flat < 1e-9 ? 0 : nx / flat;
            // Le petit axe : normale × grand axe. Sa composante vers nous est positive : le côté « +v » est DEVANT.
            double bx = -nz * ay, by = nz * ax, bz = nx * ay - ny * ax;
            var space = new Matrix(radius * ax, -radius * ay, radius * bx, -radius * by, centre.X, centre.Y);

            // LOT 17 (Rémi : « les anneaux restent fixés malgré le fait de pivoter ») : le disque est posé
            // de sorte que son repère suive celui de l'astre — son grain tourne donc avec lui sous la souris.
            if (scene != null && scene.HasRef)
            {
                var turn = Math.Atan2(scene.Rx * bx + scene.Ry * by + scene.Rz * bz, scene.Rx * ax + scene.Ry * ay);
                LastTurnForProbe = turn;
                spin += turn;
            }

            var face = FaceEntry(look, color, pixels);
            Bitmap image = face.Image;
            // LOT 17 puis 18 : la lumière. La face tournée vers nous est-elle celle que le soleil éclaire ?
            // Et où tombe l'ombre de l'astre ? L'image de face est reprise pixel par pixel : l'ombre a des
            // bords fondus, et la clarté de la face change en douceur.
            if (scene != null && scene.HasSun)
            {
                var along = scene.Sx * nx + scene.Sy * ny + scene.Sz * nz;      // le soleil au-dessus (+) ou au-dessous du plan
                var faceLight = FaceLight(nz < 0 ? -along : along);
                LastFaceLightForProbe = faceLight;
                double su = scene.Sx * ax + scene.Sy * ay, sv = scene.Sx * bx + scene.Sy * by + scene.Sz * bz;
                var slant = Math.Sqrt(su * su + sv * sv);
                var shadow = slant > 0.02;
                double dx = 0, dy = 0;
                if (shadow)
                {
                    // La direction du soleil dans le plan, ramenée dans le repère de l'image (qui tourne de `spin`).
                    double ux = su / slant, uy = sv / slant, cos = Math.Cos(spin), sin = Math.Sin(spin);
                    dx = ux * cos + uy * sin;
                    dy = -ux * sin + uy * cos;
                    if (!front) ShadowsDrawnForProbe++;
                }
                image = LitFace(look, face, pixels, outer, faceLight, shadow, dx, dy, along);
            }
            else LastFaceLightForProbe = 1;

            var rect = new Rect(-outer, -outer, outer * 2, outer * 2);
            var rotation = Matrix.CreateRotation(spin);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            {
                if (!front)
                {
                    using (context.PushTransform(space))
                    using (context.PushTransform(rotation))
                        context.DrawImage(image, rect);
                    return;
                }
                // Devant : seulement à l'intérieur du disque de l'astre (dehors, la passe de derrière a déjà
                // tout dessiné), et seulement la moitié proche.
                using (context.PushGeometryClip(new EllipseGeometry(new Rect(centre.X - radius + 0.4, centre.Y - radius + 0.4, radius * 2 - 0.8, radius * 2 - 0.8))))
                using (context.PushTransform(space))
                using (context.PushClip(new Rect(-outer - 1, 0, outer * 2 + 2, outer + 1)))
                using (context.PushTransform(rotation))
                    context.DrawImage(image, rect);
            }
        }

        /// <summary>L'ombre des anneaux sur leur astre (lot 17) : la part de lumière que garde un point de
        /// sa surface. Tout est dans le même repère (peu importe lequel) : le point (px, py, pz) sur la
        /// sphère de rayon 1, le soleil (sx, sy, sz), la normale des anneaux (nx, ny, nz). `bands` : Bands().</summary>
        internal static float ShadowOn(float px, float py, float pz, float sx, float sy, float sz, float nx, float ny, float nz, float inner, float outer, float[] bands, float density)
        {
            var down = sx * nx + sy * ny + sz * nz;
            if (down > -1e-4f && down < 1e-4f) return 1f;
            var t = -(px * nx + py * ny + pz * nz) / down;
            if (t <= 0) return 1f;
            float hx = px + t * sx, hy = py + t * sy, hz = pz + t * sz;
            var r = (float)Math.Sqrt(hx * hx + hy * hy + hz * hz);
            if (r <= inner || r >= outer) return 1f;
            return 1f - density * bands[(int)((r - inner) / (outer - inner) * (bands.Length - 1))];
        }

        private static float[] _bands;

        /// <summary>L'opacité des bandes, du bord intérieur au bord extérieur, en 512 pas.</summary>
        internal static float[] Bands()
        {
            if (_bands != null) return _bands;
            var bands = new float[512];
            // Ramenée à 1 dans le grand anneau (à pleine densité, son ombre vaut une nuit) ; les deux bords fondus.
            for (var i = 0; i < bands.Length; i++) bands[i] = (float)(Math.Min(1, Band(i / (double)(bands.Length - 1)) / 0.9) * Math.Min(1, Math.Min(i, bands.Length - 1 - i) / 10.0));
            return _bands = bands;
        }

        /// <summary>La normale des anneaux d'un petit astre du ciel (un soleil, un satellite) : vus un peu
        /// de dessus, penchés de tant de degrés dans le plan de l'écran.</summary>
        internal static void DotNormal(double degrees, out double nx, out double ny, out double nz)
        {
            const double lift = 0.3469;                  // l'arc sinus de 0,34 : le petit axe vaut un tiers du grand
            var turn = degrees * Math.PI / 180;
            nx = -Math.Sin(turn) * Math.Cos(lift);
            ny = Math.Cos(turn) * Math.Cos(lift);
            nz = Math.Sin(lift);
        }
    }
}

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
        }

        private static readonly List<Face> Faces = new List<Face>();
        private static int _built;
        internal static int FacesBuiltForProbe { get { return _built; } }

        /// <summary>Le disque des anneaux vu de face, de `pixels` de côté : son bord est le bord EXTÉRIEUR
        /// des anneaux. Gardé d'une image à l'autre tant que rien de ce qui le dessine ne change.</summary>
        internal static Bitmap FaceOf(BodyLook look, Color color, int pixels)
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
                    return found.Image;
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
            Faces.Insert(0, new Face { Key = key, Image = image });
            if (Faces.Count > 10) Faces.RemoveAt(Faces.Count - 1);
            return image;
        }

        // ------------------------------------------------------------ dans l'espace

        /// <summary>Dessine les anneaux d'un astre de rayon `radius` à l'écran, centré en `centre`.
        /// (nx, ny, nz) est la NORMALE au plan des anneaux, vue de l'écran : x vers la droite, y vers le
        /// haut, z vers nous. `spin` les fait tourner dans leur plan. Passe de derrière (front faux) : le
        /// disque entier, à dessiner AVANT l'astre. Passe de devant : la part qui passe devant lui.</summary>
        internal static void Draw(DrawingContext context, Point centre, double radius, double nx, double ny, double nz, BodyLook look, Color color, double spin, bool front, int pixels)
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
            double bx = -nz * ay, by = nz * ax;
            var space = new Matrix(radius * ax, -radius * ay, radius * bx, -radius * by, centre.X, centre.Y);
            var face = FaceOf(look, color, pixels);
            var rect = new Rect(-outer, -outer, outer * 2, outer * 2);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            {
                if (!front)
                {
                    using (context.PushTransform(space))
                    using (context.PushTransform(Matrix.CreateRotation(spin)))
                        context.DrawImage(face, rect);
                    return;
                }
                // Devant : seulement à l'intérieur du disque de l'astre (dehors, la passe de derrière a déjà
                // tout dessiné), et seulement la moitié proche.
                using (context.PushGeometryClip(new EllipseGeometry(new Rect(centre.X - radius + 0.4, centre.Y - radius + 0.4, radius * 2 - 0.8, radius * 2 - 0.8))))
                using (context.PushTransform(space))
                using (context.PushClip(new Rect(-outer - 1, 0, outer * 2 + 2, outer + 1)))
                using (context.PushTransform(Matrix.CreateRotation(spin)))
                    context.DrawImage(face, rect);
            }
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

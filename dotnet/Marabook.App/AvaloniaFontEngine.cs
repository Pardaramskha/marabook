using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.App
{
    /// <summary>Le moteur de polices du cœur sur Avalonia (P2) : Skia et
    /// HarfBuzz derrière IGlyphTypeface — les mêmes services que
    /// WpfFontEngine (faces, glyphes, avances, repli mesuré, fichier à
    /// embarquer, repli rastérisé), pour le compositeur, le rendu et le PDF.
    /// Les métriques sont en unités de dessin de la face, ramenées au
    /// cadratin comme WPF les donnait ; l'ascendante d'Avalonia est
    /// négative (au-dessus de la ligne de base), on la redresse.</summary>
    public sealed class AvaloniaFontEngine : IFontEngine
    {
        /// <summary>La poignée Avalonia d'une face : le IGlyphTypeface (GlyphRun)
        /// et le Typeface (FormattedText).</summary>
        public sealed class Handle
        {
            public IGlyphTypeface Glyphs;
            public Typeface Typeface;
        }

        private static readonly Dictionary<string, FaceInfo> _faces = new Dictionary<string, FaceInfo>();
        private static readonly Dictionary<string, Dictionary<ushort, double>> _advances = new Dictionary<string, Dictionary<ushort, double>>();

        public static Handle HandleOf(FaceInfo face)
        {
            return face == null ? null : face.Native as Handle;
        }

        public static IGlyphTypeface GlyphsOf(FaceInfo face)
        {
            var handle = HandleOf(face);
            return handle == null ? null : handle.Glyphs;
        }

        public FaceInfo Resolve(string family, int weight, bool italic)
        {
            var key = family + "|" + weight + "|" + italic;
            FaceInfo face;
            lock (_faces)
            {
                if (_faces.TryGetValue(key, out face)) return face;
            }
            var typeface = new Typeface(new FontFamily(family ?? "Times New Roman"),
                italic ? FontStyle.Italic : FontStyle.Normal, (FontWeight)Math.Max(1, Math.Min(999, weight)));
            IGlyphTypeface glyphs = null;
            try
            {
                if (!FontManager.Current.TryGetGlyphTypeface(typeface, out glyphs)) glyphs = null;
            }
            catch { glyphs = null; }
            face = new FaceInfo
            {
                Family = family,
                Weight = weight,
                Italic = italic,
                ActualWeight = weight,
                ActualItalic = italic,
                HasGlyphs = glyphs != null,
                Baseline = 0.8,
                Native = new Handle { Glyphs = glyphs, Typeface = typeface }
            };
            if (glyphs != null)
            {
                var metrics = glyphs.Metrics;
                var em = metrics.DesignEmHeight > 0 ? (double)metrics.DesignEmHeight : 1000.0;
                face.ActualWeight = (int)glyphs.Weight;
                face.ActualItalic = glyphs.Style != FontStyle.Normal;
                face.Baseline = Math.Abs(metrics.Ascent) / em;
                face.UnderlinePosition = metrics.UnderlinePosition / em;
                face.UnderlineThickness = metrics.UnderlineThickness / em;
                face.StrikethroughPosition = metrics.StrikethroughPosition / em;
                face.StrikethroughThickness = metrics.StrikethroughThickness / em;
                face.SimulatedBold = (glyphs.FontSimulations & FontSimulations.Bold) != 0;
                face.SimulatedItalic = (glyphs.FontSimulations & FontSimulations.Oblique) != 0;
                face.File = glyphs.FamilyName + "|" + (int)glyphs.Weight + "|" + glyphs.Style + "|" + glyphs.Stretch;
                face.Key = face.File + "|" + (int)glyphs.FontSimulations;
            }
            else face.Key = key;
            lock (_faces)
            {
                _faces[key] = face;
            }
            return face;
        }

        public bool TryGlyph(FaceInfo face, char c, out ushort glyph)
        {
            glyph = 0;
            var glyphs = GlyphsOf(face);
            if (glyphs == null) return false;
            // Un caractère que la face n'a pas rend le glyphe 0 (.notdef).
            if (!glyphs.TryGetGlyph((uint)c, out glyph)) { glyph = 0; return false; }
            return glyph != 0;
        }

        public double GlyphAdvance(FaceInfo face, ushort glyph)
        {
            var glyphs = GlyphsOf(face);
            if (glyphs == null) return 0;
            Dictionary<ushort, double> cache;
            lock (_advances)
            {
                if (!_advances.TryGetValue(face.Key, out cache))
                {
                    cache = new Dictionary<ushort, double>();
                    _advances[face.Key] = cache;
                }
            }
            double advance;
            lock (cache)
            {
                if (cache.TryGetValue(glyph, out advance)) return advance;
                var em = glyphs.Metrics.DesignEmHeight > 0 ? (double)glyphs.Metrics.DesignEmHeight : 1000.0;
                advance = glyphs.GetGlyphAdvance(glyph) / em;
                cache[glyph] = advance;
            }
            return advance;
        }

        public IEnumerable<KeyValuePair<int, ushort>> CharacterMap(FaceInfo face)
        {
            var glyphs = GlyphsOf(face);
            if (glyphs == null) yield break;
            // Pas de table cmap exposée : le plan multilingue de base,
            // caractère par caractère (une fois par police embarquée).
            for (var code = 0x20; code < 0xFFFF; code++)
            {
                if (code >= 0xD800 && code <= 0xDFFF) continue;
                ushort glyph;
                if (glyphs.TryGetGlyph((uint)code, out glyph) && glyph != 0)
                    yield return new KeyValuePair<int, ushort>(code, glyph);
            }
        }

        public TextExtent Measure(FaceInfo face, string text, double emSize)
        {
            var formatted = Format(face, text ?? "", emSize, Brushes.Black);
            return new TextExtent
            {
                Width = formatted.WidthIncludingTrailingWhitespace,
                Height = formatted.Height,
                Baseline = formatted.Baseline
            };
        }

        /// <summary>Le FormattedText d'un repli — la même construction pour la
        /// mesure et le dessin.</summary>
        public static FormattedText Format(FaceInfo face, string text, double emSize, IBrush ink)
        {
            var handle = HandleOf(face);
            var typeface = handle != null ? handle.Typeface : new Typeface(face.Family ?? "Times New Roman");
            return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, emSize, ink);
        }

        public byte[] FontFile(FaceInfo face, out int faceIndex, out string fallbackName)
        {
            faceIndex = 0;
            fallbackName = null;
            var glyphs = GlyphsOf(face);
            if (glyphs == null) return null;
            try
            {
                fallbackName = glyphs.FamilyName;
                // Le fichier de la face : IGlyphTypeface2.TryGetStream rend le
                // flux OpenType entier (que le PDF sous-ensemble) — l'interface
                // est interne dans Avalonia 11, publique en 12 : par réflexion.
                var stream = FontStreamOf(glyphs);
                if (stream == null) return null;
                using (stream)
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
            catch { return null; }
        }

        private static Stream FontStreamOf(IGlyphTypeface glyphs)
        {
            try
            {
                foreach (var contract in glyphs.GetType().GetInterfaces())
                {
                    if (contract.Name != "IGlyphTypeface2") continue;
                    var method = contract.GetMethod("TryGetStream");
                    if (method == null) return null;
                    var args = new object[] { null };
                    var ok = method.Invoke(glyphs, args) as bool?;
                    return ok == true ? args[0] as Stream : null;
                }
            }
            catch { }
            return null;
        }

        public byte[] RasterizeText(FaceInfo face, string text, double emSize, Ink ink, double scale,
            out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                var brush = new SolidColorBrush(Chrome.ToColor(ink));
                var formatted = Format(face, text, emSize, brush);
                var wPx = formatted.WidthIncludingTrailingWhitespace;
                var hPx = formatted.Height;
                if (wPx < 0.1 || hPx < 0.1) return null;
                var pixelW = Math.Max(1, (int)Math.Ceiling(wPx * scale));
                var pixelH = Math.Max(1, (int)Math.Ceiling(hPx * scale));
                using (var bitmap = new RenderTargetBitmap(new PixelSize(pixelW, pixelH), new Vector(96 * scale, 96 * scale)))
                {
                    using (var dc = bitmap.CreateDrawingContext())
                    {
                        dc.FillRectangle(Brushes.White, new Rect(0, 0, wPx + 1, hPx + 1));
                        dc.DrawText(formatted, new Point(0, 0));
                    }
                    return Rgb24OverWhite(bitmap, out width, out height);
                }
            }
            catch { return null; }
        }

        /// <summary>Les pixels d'une bitmap Avalonia en RVB24 sur blanc.</summary>
        public static byte[] Rgb24OverWhite(Bitmap bitmap, out int width, out int height)
        {
            width = bitmap.PixelSize.Width;
            height = bitmap.PixelSize.Height;
            var stride = width * 4;
            var size = stride * height;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, width, height), buffer, size, stride);
                var pixels = new byte[size];
                Marshal.Copy(buffer, pixels, 0, size);
                var rgb = new byte[width * height * 3];
                var o = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    // BGRA prémultiplié : le blanc manquant s'ajoute.
                    var alpha = 255 - pixels[i + 3];
                    rgb[o++] = (byte)Math.Min(255, pixels[i + 2] + alpha);
                    rgb[o++] = (byte)Math.Min(255, pixels[i + 1] + alpha);
                    rgb[o++] = (byte)Math.Min(255, pixels[i] + alpha);
                }
                return rgb;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        // ------------------------------------------------------------ IGlyphMetrics

        public double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, char c)
        {
            return AdvanceWidth(fontFamily, emSize, weight, italic, c.ToString());
        }

        public double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, string s)
        {
            var face = Resolve(fontFamily, weight, italic);
            if (face.HasGlyphs)
            {
                double width = 0;
                var complete = true;
                foreach (var c in s)
                {
                    ushort glyph;
                    if (TryGlyph(face, c, out glyph)) width += GlyphAdvance(face, glyph) * emSize;
                    else { complete = false; break; }
                }
                if (complete) return width;
            }
            return Measure(face, s, emSize).Width;
        }

        public bool HasGlyph(string fontFamily, int weight, bool italic, char c)
        {
            ushort glyph;
            return TryGlyph(Resolve(fontFamily, weight, italic), c, out glyph);
        }

        public Marabook.Print.FontMetrics Metrics(string fontFamily, double emSize, int weight, bool italic)
        {
            var face = Resolve(fontFamily, weight, italic);
            // La règle historique du compositeur : ascendante = Baseline × em,
            // hauteur de ligne = 1,25 × em.
            var ascent = face.Baseline * emSize;
            return new Marabook.Print.FontMetrics { Ascent = ascent, Descent = emSize * 1.25 - ascent, LineGap = 0 };
        }
    }
}

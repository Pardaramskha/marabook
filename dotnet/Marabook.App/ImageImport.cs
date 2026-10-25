using System;
using System.IO;
using SkiaSharp;

namespace Marabook.App
{
    /// <summary>L'optimisation des images À L'IMPORT (1.0.6, 14/10) : les
    /// images déjà enregistrées dans un .plot ne bougent JAMAIS ; les nouvelles
    /// sont ramenées à au plus MaxSide px de côté et réencodées — JPEG de
    /// qualité 85 quand l'image est opaque, PNG quand elle a de la
    /// transparence. Les portraits de Rémi arrivaient en 3 000 × 4 000 px et
    /// 5 à 6 Mo chacun : vingt fiches, 43 Mo de .plot et une bibliothèque qui
    /// ramait. Le GIF (animé) et ce qui ne se décode pas restent tels quels.
    /// Partagé par la fenêtre de recadrage, les sites d'import de l'app et
    /// le codec du cœur (.docx, Markdown).</summary>
    public static class ImageImport
    {
        public const int MaxSide = 2048;          // le plus grand côté après import
        public const int JpegQuality = 85;
        public const int ReencodeAbove = 1200 * 1024; // au-delà d'1,2 Mo, on réencode même sans réduire

        public sealed class Result
        {
            public byte[] Bytes;
            public string Extension; // « .jpg » ou « .png », ou celle d'origine si rien n'a bougé
            public int Width;
            public int Height;
            public bool Changed;     // faux : les octets d'origine, tels quels
        }

        /// <summary>Optimise des octets d'image ; rend toujours quelque chose
        /// d'utilisable (au pire l'original).</summary>
        public static Result Optimize(byte[] bytes, string extension)
        {
            var untouched = new Result { Bytes = bytes, Extension = Normalize(extension), Changed = false };
            if (bytes == null || bytes.Length == 0) return untouched;
            if (untouched.Extension == ".gif" || untouched.Extension == ".svg") return untouched;
            try
            {
                using (var bitmap = SKBitmap.Decode(bytes))
                {
                    if (bitmap == null) return untouched;
                    untouched.Width = bitmap.Width;
                    untouched.Height = bitmap.Height;
                    var needsResize = Math.Max(bitmap.Width, bitmap.Height) > MaxSide;
                    if (!needsResize && bytes.Length <= ReencodeAbove) return untouched;
                    var encoded = Encode(needsResize ? Resize(bitmap, MaxSide) : bitmap, needsResize);
                    if (encoded == null) return untouched;
                    // Sans réduction, on ne garde le réencodage que s'il fait maigrir.
                    if (!needsResize && encoded.Bytes.Length >= bytes.Length) return untouched;
                    return encoded;
                }
            }
            catch (Exception) { return untouched; }
        }

        /// <summary>Rotation (quarts de tour horaires) puis recadrage (en
        /// pixels de l'image TOURNÉE ; vide = tout) puis optimisation. Null
        /// si l'image ne se décode pas.</summary>
        public static Result Apply(byte[] bytes, string extension, int quarterTurns, SKRectI crop)
        {
            if (bytes == null) return null;
            try
            {
                using (var decoded = SKBitmap.Decode(bytes))
                {
                    if (decoded == null) return null;
                    var turned = Rotate(decoded, quarterTurns);
                    var cropped = crop.IsEmpty ? turned : Crop(turned, crop);
                    var changed = quarterTurns % 4 != 0 || cropped != turned;
                    var needsResize = Math.Max(cropped.Width, cropped.Height) > MaxSide;
                    if (!changed && !needsResize && bytes.Length <= ReencodeAbove)
                        return new Result { Bytes = bytes, Extension = Normalize(extension), Width = cropped.Width, Height = cropped.Height, Changed = false };
                    var result = Encode(needsResize ? Resize(cropped, MaxSide) : cropped, true);
                    if (cropped != turned) cropped.Dispose();
                    if (turned != decoded) turned.Dispose();
                    return result;
                }
            }
            catch (Exception) { return null; }
        }

        public static string Normalize(string extension)
        {
            var ext = (extension ?? "").Trim().ToLowerInvariant();
            if (ext == ".jpeg") ext = ".jpg";
            return ext.Length == 0 ? ".png" : ext;
        }

        /// <summary>L'image a-t-elle un pixel non opaque ? (Le type alpha d'un
        /// PNG décodé dit « Unpremul » même pour une image pleine.)</summary>
        public static bool HasTransparency(SKBitmap bitmap)
        {
            if (bitmap.AlphaType == SKAlphaType.Opaque) return false;
            var pixels = bitmap.Pixels;
            for (var i = 0; i < pixels.Length; i++)
                if (pixels[i].Alpha < 255) return true;
            return false;
        }

        public static SKBitmap Resize(SKBitmap bitmap, int maxSide)
        {
            var scale = (double)maxSide / Math.Max(bitmap.Width, bitmap.Height);
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            var info = new SKImageInfo(width, height, bitmap.ColorType, bitmap.AlphaType);
            return bitmap.Resize(info, SKFilterQuality.High) ?? bitmap;
        }

        public static SKBitmap Rotate(SKBitmap bitmap, int quarterTurns)
        {
            var turns = ((quarterTurns % 4) + 4) % 4;
            if (turns == 0) return bitmap;
            var swap = turns == 1 || turns == 3;
            var rotated = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height, bitmap.ColorType, bitmap.AlphaType);
            using (var canvas = new SKCanvas(rotated))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
                canvas.RotateDegrees(90 * turns);
                canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
                canvas.DrawBitmap(bitmap, 0, 0);
            }
            return rotated;
        }

        private static SKBitmap Crop(SKBitmap bitmap, SKRectI crop)
        {
            var rect = SKRectI.Intersect(crop, new SKRectI(0, 0, bitmap.Width, bitmap.Height));
            if (rect.IsEmpty || (rect.Width == bitmap.Width && rect.Height == bitmap.Height)) return bitmap;
            var cropped = new SKBitmap(rect.Width, rect.Height, bitmap.ColorType, bitmap.AlphaType);
            if (!bitmap.ExtractSubset(cropped, rect))
            {
                cropped.Dispose();
                cropped = new SKBitmap(rect.Width, rect.Height, bitmap.ColorType, bitmap.AlphaType);
                using (var canvas = new SKCanvas(cropped))
                    canvas.DrawBitmap(bitmap, new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), new SKRect(0, 0, rect.Width, rect.Height));
                return cropped;
            }
            // ExtractSubset partage les pixels : une copie indépendante avant que l'original ne parte.
            var copy = cropped.Copy();
            cropped.Dispose();
            return copy;
        }

        /// <summary>JPEG 85 si opaque, PNG sinon.</summary>
        private static Result Encode(SKBitmap bitmap, bool changed)
        {
            var transparent = HasTransparency(bitmap);
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(transparent ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, transparent ? 100 : JpegQuality))
            {
                if (data == null) return null;
                return new Result
                {
                    Bytes = data.ToArray(),
                    Extension = transparent ? ".png" : ".jpg",
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    Changed = changed
                };
            }
        }

        /// <summary>Une image de synthèse (sonde) : pleine couleur, ou avec un
        /// coin transparent.</summary>
        public static byte[] Synthetic(int width, int height, bool transparentCorner, bool png)
        {
            using (var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul))
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                using (var paint = new SKPaint { Color = new SKColor(90, 110, 220) })
                    canvas.DrawRect(transparentCorner ? width / 4 : 0, 0, width, height, paint);
                using (var paint = new SKPaint { Color = new SKColor(230, 60, 60) })
                    canvas.DrawCircle(width * 0.7f, height * 0.3f, Math.Min(width, height) / 5f, paint);
                using (var image = SKImage.FromBitmap(bitmap))
                using (var data = image.Encode(png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 92))
                    return data.ToArray();
            }
        }
    }
}

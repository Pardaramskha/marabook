using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Marabook.App
{
    /// <summary>LES IMAGES DES OUTILS PDF (22/11/2026 — PDFaah) : encoder en
    /// JPEG ce que PDFium a dessiné ou lu (une page, une image à alléger), et
    /// lire l'image d'une signature. Skia fait le travail : il est déjà là,
    /// c'est lui qui dessine l'application.</summary>
    internal static class PdfImages
    {
        /// <summary>Un JPEG à partir de points rangés à la manière de PDFium — format 1 : gris (1 octet),
        /// 2 : BGR (3 octets), 3 : BGRx, 4 : BGRA — ramené à newWidth × newHeight. null si ça ne se fait pas.</summary>
        public static byte[] EncodeJpeg(byte[] pixels, int width, int height, int stride, int format, int newWidth, int newHeight, int quality)
        {
            if (pixels == null || width <= 0 || height <= 0 || format < 1 || format > 4) return null;
            try
            {
                var gray = format == 1;
                var info = new SKImageInfo(width, height, gray ? SKColorType.Gray8 : SKColorType.Bgra8888, SKAlphaType.Opaque);
                using (var bitmap = new SKBitmap(info))
                {
                    var target = bitmap.GetPixels();
                    var rowBytes = bitmap.RowBytes;
                    if (gray)
                    {
                        for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * stride, IntPtr.Add(target, y * rowBytes), width);
                    }
                    else if (format == 2)
                    {
                        var row = new byte[width * 4];
                        for (var y = 0; y < height; y++)
                        {
                            var from = y * stride;
                            for (var x = 0; x < width; x++)
                            {
                                row[x * 4] = pixels[from + x * 3];
                                row[x * 4 + 1] = pixels[from + x * 3 + 1];
                                row[x * 4 + 2] = pixels[from + x * 3 + 2];
                                row[x * 4 + 3] = 255;
                            }
                            Marshal.Copy(row, 0, IntPtr.Add(target, y * rowBytes), width * 4);
                        }
                    }
                    else
                    {
                        var row = new byte[width * 4];
                        for (var y = 0; y < height; y++)
                        {
                            Array.Copy(pixels, y * stride, row, 0, width * 4);
                            for (var x = 0; x < width; x++) row[x * 4 + 3] = 255;
                            Marshal.Copy(row, 0, IntPtr.Add(target, y * rowBytes), width * 4);
                        }
                    }
                    if (newWidth <= 0 || newHeight <= 0 || (newWidth == width && newHeight == height)) return Encode(bitmap, quality);
                    using (var smaller = bitmap.Resize(new SKImageInfo(newWidth, newHeight, info.ColorType, SKAlphaType.Opaque), SKFilterQuality.High))
                        return smaller == null ? null : Encode(smaller, quality);
                }
            }
            catch (Exception) { return null; }
        }

        private static byte[] Encode(SKBitmap bitmap, int quality)
        {
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Max(10, Math.Min(100, quality))))
                return data == null ? null : data.ToArray();
        }

        /// <summary>Lit une image (PNG, JPG…) en BGRA à alpha DROIT, le haut d'abord — ce que PDFium attend
        /// d'une image à poser. Faux si le fichier ne se lit pas.</summary>
        public static bool Decode(byte[] file, out byte[] bgra, out int width, out int height)
        {
            bgra = null;
            width = 0;
            height = 0;
            try
            {
                using (var decoded = SKBitmap.Decode(file))
                {
                    if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0) return false;
                    var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                    width = decoded.Width;
                    height = decoded.Height;
                    bgra = new byte[width * height * 4];
                    var pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
                    try
                    {
                        // Skia convertit en passant : BGRA, alpha droit, quel que soit ce que le fichier portait.
                        using (var pixmap = decoded.PeekPixels())
                            if (pixmap == null || !pixmap.ReadPixels(info, pin.AddrOfPinnedObject(), width * 4)) { bgra = null; return false; }
                    }
                    finally { pin.Free(); }
                }
                return true;
            }
            catch (Exception) { bgra = null; return false; }
        }

        /// <summary>Le papier d'une signature photographiée : ce qui est presque blanc devient transparent,
        /// le trait garde sa couleur — plus il est sombre, plus il est opaque.</summary>
        public static byte[] WhiteToAlpha(byte[] bgra)
        {
            var result = (byte[])bgra.Clone();
            for (var i = 0; i + 3 < result.Length; i += 4)
            {
                var light = (result[i] + result[i + 1] + result[i + 2]) / 3;
                var alpha = light >= 236 ? 0 : Math.Min(255, (255 - light) * 3);
                result[i + 3] = (byte)Math.Min(result[i + 3], alpha);
            }
            return result;
        }

        /// <summary>Rogne une image BGRA à ce qui n'est pas transparent (plus une marge). Rend l'image telle
        /// quelle si tout est transparent.</summary>
        public static byte[] Trim(byte[] bgra, int width, int height, int margin, out int newWidth, out int newHeight)
        {
            int left = width, top = height, right = -1, bottom = -1;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (bgra[(y * width + x) * 4 + 3] > 8)
                    {
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
            if (right < left || bottom < top) { newWidth = width; newHeight = height; return bgra; }
            left = Math.Max(0, left - margin);
            top = Math.Max(0, top - margin);
            right = Math.Min(width - 1, right + margin);
            bottom = Math.Min(height - 1, bottom + margin);
            newWidth = right - left + 1;
            newHeight = bottom - top + 1;
            var result = new byte[newWidth * newHeight * 4];
            for (var y = 0; y < newHeight; y++) Array.Copy(bgra, ((top + y) * width + left) * 4, result, y * newWidth * 4, newWidth * 4);
            return result;
        }
    }
}

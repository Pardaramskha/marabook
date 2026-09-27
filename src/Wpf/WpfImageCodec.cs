using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Marabook.Model;

namespace Marabook.Wpf
{
    /// <summary>Le codec d'images WPF (portage Avalonia, P0) : ce que le PDF et
    /// l'EPUB demandaient directement à BitmapDecoder passe par IImageCodec.
    /// Même pipeline qu'avant : décodage plafonné à ImageCache.MaxDecodeWidth,
    /// réduction TransformedBitmap, aplatissement RVB24 sur blanc.</summary>
    public sealed class WpfImageCodec : IImageCodec
    {
        public bool TryGetSize(byte[] bytes, out int width, out int height)
        {
            if (ImageHeader.TryReadSize(bytes, out width, out height) && height > 0) return true;
            var bitmap = bytes == null ? null : View.MediaView.TryImage(bytes, 0) as BitmapSource;
            if (bitmap == null) return false;
            width = bitmap.PixelWidth;
            height = bitmap.PixelHeight;
            return width > 0 && height > 0;
        }

        public byte[] ToRgb24(byte[] bytes, double maxWidthPx, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (bytes == null) return null;
            var natural = ImageHeader.PixelWidth(bytes);
            var bitmap = View.MediaView.TryImage(bytes,
                natural > View.ImageCache.MaxDecodeWidth ? View.ImageCache.MaxDecodeWidth : 0) as BitmapSource;
            if (bitmap == null) return null;
            var factor = Math.Min(1.0, maxWidthPx / Math.Max(1, bitmap.PixelWidth));
            BitmapSource frame = bitmap;
            if (factor < 0.999)
                frame = new TransformedBitmap(bitmap, new ScaleTransform(factor, factor));
            return Rgb24OverWhite(frame, out width, out height);
        }

        /// <summary>RVB24 sur blanc (le papier) : l'alpha prémultiplié reçoit
        /// le blanc manquant.</summary>
        public static byte[] Rgb24OverWhite(BitmapSource source, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
                width = converted.PixelWidth;
                height = converted.PixelHeight;
                var stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);
                var rgb = new byte[width * height * 3];
                var o = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var alpha = 255 - pixels[i + 3];
                    rgb[o++] = (byte)Math.Min(255, pixels[i + 2] + alpha);
                    rgb[o++] = (byte)Math.Min(255, pixels[i + 1] + alpha);
                    rgb[o++] = (byte)Math.Min(255, pixels[i] + alpha);
                }
                return rgb;
            }
            catch { return null; }
        }

        public byte[] ToPng(byte[] bytes)
        {
            try
            {
                var decoder = BitmapDecoder.Create(new MemoryStream(bytes),
                    BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(decoder.Frames[0]);
                using (var output = new MemoryStream())
                {
                    encoder.Save(output);
                    return output.ToArray();
                }
            }
            catch { return null; }
        }
    }
}

using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using Marabook.Exchange;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LA VIGNETTE DE L'APERÇU (gestionnaire d'export, 20/11/2026) :
    /// une page de l'export telle qu'elle sortira dans le PDF — la page
    /// composée (mêmes lignes, mêmes folios, mêmes en-têtes) ou la page-image
    /// d'un plan —, réduite à la place qu'on lui laisse, sur papier blanc.</summary>
    internal sealed class ExportPagePreview : Control
    {
        private Print.PdfPart _part;
        private int _page;
        private Bitmap _image;
        private Print.PdfPart _imageOf;

        public void Show(Print.PdfPart part, int page)
        {
            _part = part;
            _page = page;
            InvalidateVisual();
        }

        private static Size PageSize(Print.PdfPart part)
        {
            if (part.IsImage) return new Size(part.PageWidthMm * PageSetup.PxPerMm, part.PageHeightMm * PageSetup.PxPerMm);
            return new Size(part.Composition.PageWidthPx, part.Composition.PageHeightPx);
        }

        /// <summary>« A5 », « 148 × 210 mm », « page-image »…</summary>
        public static string Describe(Print.PdfPart part)
        {
            if (part == null) return "";
            if (part.IsImage) return "page-image, " + part.PageWidthMm.ToString("0") + " × " + part.PageHeightMm.ToString("0") + " mm";
            return ExportPages.Describe(part.Composition.Setup);
        }

        /// <summary>Les pixels RVB d'une page-image, en image Avalonia (faite une fois par page).</summary>
        private Bitmap ImageOf(Print.PdfPart part)
        {
            if (ReferenceEquals(_imageOf, part) && _image != null) return _image;
            if (_image != null) { _image.Dispose(); _image = null; }
            _imageOf = part;
            int width = part.ImageWidth, height = part.ImageHeight;
            if (part.ImageRgb == null || width < 1 || height < 1 || part.ImageRgb.Length < width * height * 3) return null;
            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock())
            {
                var row = new byte[width * 4];
                for (var y = 0; y < height; y++)
                {
                    var source = y * width * 3;
                    for (var x = 0; x < width; x++)
                    {
                        row[x * 4] = part.ImageRgb[source + x * 3 + 2];
                        row[x * 4 + 1] = part.ImageRgb[source + x * 3 + 1];
                        row[x * 4 + 2] = part.ImageRgb[source + x * 3];
                        row[x * 4 + 3] = 255;
                    }
                    Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
                }
            }
            return _image = bitmap;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_part == null) return;
            var size = PageSize(_part);
            if (size.Width < 1 || size.Height < 1) return;
            var scale = Math.Min((Bounds.Width - 12) / size.Width, (Bounds.Height - 12) / size.Height);
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) return;
            var x = Math.Round((Bounds.Width - size.Width * scale) / 2);
            var y = Math.Round((Bounds.Height - size.Height * scale) / 2);
            var paper = new Rect(x, y, size.Width * scale, size.Height * scale);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, paper.Translate(new Vector(2, 3)));
            context.DrawRectangle(Brushes.White, new Pen(Chrome.Border, 1), paper);
            if (_part.IsImage)
            {
                var image = ImageOf(_part);
                if (image != null) context.DrawImage(image, paper);
                return;
            }
            if (_page < 0 || _page >= _part.Composition.Pages.Count) return;
            // Un texte réduit lissé en sous-pixels (ClearType) se borde de couleurs : niveaux de gris ici.
            using (context.PushRenderOptions(new RenderOptions { TextRenderingMode = TextRenderingMode.Antialias, BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            using (context.PushClip(paper))
            using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y)))
                ComposedRenderer.DrawPage(context, _part.Composition, _page, false);
        }
    }
}

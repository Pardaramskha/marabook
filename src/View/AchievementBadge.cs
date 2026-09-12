using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>L'image d'un succès (12/09/2026) : le PNG
    /// assets\achievements\&lt;id&gt;.png à côté de l'exécutable s'il existe
    /// (les visuels arrivent plus tard), sinon un écusson dessiné. Verrouillé
    /// = en gris et atténué ; débloqué = en couleurs.</summary>
    public static class AchievementBadge
    {
        public static string ImagePath(string id)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine("assets", Path.Combine("achievements", id + ".png")));
        }

        public static FrameworkElement Build(Achievement achievement, bool unlocked, double size)
        {
            var path = ImagePath(achievement.Id);
            BitmapSource source = null;
            if (File.Exists(path))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = (int)(size * 2);
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    source = bitmap;
                }
                catch (Exception) { source = null; }
            }

            var frame = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.22),
                ClipToBounds = true,
                Opacity = unlocked ? 1 : 0.45
            };
            if (source != null)
            {
                if (!unlocked)
                {
                    var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
                    gray.Freeze();
                    source = gray;
                }
                frame.Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
                return frame;
            }
            // L'écusson provisoire : dégradé accent (ou gris) et une étoile.
            frame.Background = unlocked
                ? (Brush)new LinearGradientBrush(Chrome.Accent.Color, Chrome.AccentStrong.Color, 90)
                : new SolidColorBrush(Chrome.Blend(Chrome.SoftText.Color, Chrome.WindowBg.Color, 0.5));
            frame.Child = new TextBlock
            {
                Text = "★",
                FontSize = size * 0.5,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return frame;
        }
    }
}

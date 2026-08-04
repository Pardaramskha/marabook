using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Viewer for research media: inline preview for images and plain
    /// text, an info card plus "open externally" for everything else.</summary>
    public class MediaView : DockPanel
    {
        private readonly Border _content;
        private BinderItem _item;

        public MediaView()
        {
            var header = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 8, 24, 8)
            };
            SetDock(header, Dock.Top);
            var headerRow = new DockPanel();
            var open = new Button { Content = "Ouvrir dans l'application associée" };
            open.Click += delegate { OpenExternal(); };
            DockPanel.SetDock(open, Dock.Right);
            headerRow.Children.Add(open);
            headerRow.Children.Add(new TextBlock
            {
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Text = "Document de recherche"
            });
            header.Child = headerRow;
            Children.Add(header);

            _content = new Border { Padding = new Thickness(24) };
            Children.Add(_content);
        }

        public static bool IsImage(string extension)
        {
            if (extension == null) return false;
            var ext = extension.ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif"
                || ext == ".bmp" || ext == ".webp" || ext == ".tiff";
        }

        public static bool IsPlainText(string extension)
        {
            if (extension == null) return false;
            var ext = extension.ToLowerInvariant();
            return ext == ".txt" || ext == ".md" || ext == ".csv" || ext == ".log";
        }

        public static ImageSource TryImage(byte[] bytes, int decodeWidth)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(bytes);
                if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        public void LoadItem(BinderItem item)
        {
            _item = item;
            if (item.MediaBytes == null)
            {
                _content.Child = InfoCard("(contenu introuvable)");
                return;
            }

            if (IsImage(item.MediaExtension))
            {
                var source = TryImage(item.MediaBytes, 0);
                if (source != null)
                {
                    _content.Child = new Border
                    {
                        Background = Chrome.PaperBg,
                        BorderBrush = Chrome.Border,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12),
                        Child = new Image { Source = source, Stretch = Stretch.Uniform }
                    };
                    return;
                }
            }

            if (IsPlainText(item.MediaExtension))
            {
                string text;
                try { text = Encoding.UTF8.GetString(item.MediaBytes); }
                catch { text = "(contenu illisible)"; }
                _content.Child = new Border
                {
                    Background = Chrome.PaperBg,
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(16),
                    Child = new TextBox
                    {
                        Text = text,
                        IsReadOnly = true,
                        TextWrapping = TextWrapping.Wrap,
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent,
                        Foreground = Chrome.Ink,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    }
                };
                return;
            }

            var size = item.MediaBytes.LongLength;
            var sizeLabel = size > 1024 * 1024
                ? (size / (1024.0 * 1024.0)).ToString("0.0") + " Mo"
                : (size / 1024.0).ToString("0.0") + " Ko";
            _content.Child = InfoCard(item.Title + (item.MediaExtension ?? "")
                + "\n" + sizeLabel + "\n\nAperçu indisponible pour ce format.");
        }

        public void Clear()
        {
            _item = null;
            _content.Child = null;
        }

        private UIElement InfoCard(string text)
        {
            return new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(30),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Chrome.SoftText,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420
                }
            };
        }

        /// <summary>Extracts the media to a temp file and hands it to Windows.</summary>
        private void OpenExternal()
        {
            if (_item == null || _item.MediaBytes == null) return;
            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "UniversSale");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, SafeName(_item.Title) + (_item.MediaExtension ?? ""));
                File.WriteAllBytes(path, _item.MediaBytes);
                System.Diagnostics.Process.Start(path);
            }
            catch (Exception error)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "Impossible d'ouvrir le fichier :\n" + error.Message,
                    "Univers Sale", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string SafeName(string title)
        {
            var sb = new StringBuilder();
            foreach (var c in title)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.Length == 0 ? "media" : sb.ToString();
        }
    }
}

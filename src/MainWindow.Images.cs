using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Marabook.Model;
using Marabook.View;

namespace Marabook
{
    /// <summary>MainWindow, partie « images » (refonte des images, 0.50.0) :
    /// quand une image est sélectionnée dans l'écrit, le panneau Général du
    /// rail montre PROVISOIREMENT ses propriétés — nom, taille, format — et
    /// « Enregistrer l'image… » ; la désélection rend le Général.</summary>
    public partial class MainWindow
    {
        private UIElement _inspectorDefaultChild; // le Général ordinaire

        private void UpdateInspectorForImage()
        {
            if (_inspector == null) return;
            if (_inspectorDefaultChild == null) _inspectorDefaultChild = _inspector.Child;
            var info = _editor == null || _editor.Visibility != Visibility.Visible ? null : _editor.SelectedImageInfo();
            if (info == null)
            {
                if (!ReferenceEquals(_inspector.Child, _inspectorDefaultChild)) _inspector.Child = _inspectorDefaultChild;
                return;
            }
            _inspector.Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = BuildImagePanel(info)
            };
        }

        private static TextBlock InspectorLabel(string text, double topMargin)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, topMargin, 0, 4)
            };
        }

        private static TextBlock InspectorValue(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.Ink,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static string Cm(double px)
        {
            return (px / PageSetup.PxPerMm / 10).ToString("0.0", CultureInfo.CurrentCulture);
        }

        private static string FormatLabel(string extension)
        {
            switch ((extension ?? "").ToLowerInvariant())
            {
                case ".png": return "PNG";
                case ".jpg": case ".jpeg": return "JPEG";
                case ".gif": return "GIF";
                case ".bmp": return "BMP";
                case ".tif": case ".tiff": return "TIFF";
                case "": return "inconnu";
                default: return extension.TrimStart('.').ToUpperInvariant();
            }
        }

        private static string Weight(byte[] bytes)
        {
            if (bytes == null) return "";
            if (bytes.Length >= 1024 * 1024)
                return (bytes.Length / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.CurrentCulture) + " Mo";
            return Math.Max(1, bytes.Length / 1024) + " Ko";
        }

        private StackPanel BuildImagePanel(SelectedImageInfo info)
        {
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock
            {
                Text = "Image",
                Foreground = Chrome.Ink,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Sélectionnée dans l'écrit — un clic ailleurs sur la page rend le Général",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 12)
            });

            panel.Children.Add(InspectorLabel("Nom", 0));
            var name = new TextBox
            {
                Text = info.Name ?? "",
                ToolTip = "Le nom de l'image (celui du fichier importé) — modifiable"
            };
            name.TextChanged += delegate { _editor.RenameSelectedImage(name.Text); MarkDirty(); };
            panel.Children.Add(name);

            panel.Children.Add(InspectorLabel("Taille affichée", 10));
            panel.Children.Add(InspectorValue(Cm(info.WidthPx) + " × " + Cm(info.HeightPx) + " cm"));
            if (info.PixelWidth > 0)
            {
                panel.Children.Add(InspectorLabel("Pixels", 10));
                panel.Children.Add(InspectorValue(info.PixelWidth + " × " + info.PixelHeight + " px"));
            }
            panel.Children.Add(InspectorLabel("Format", 10));
            var format = FormatLabel(info.Extension);
            var weight = Weight(info.Bytes);
            panel.Children.Add(InspectorValue(weight.Length > 0 ? format + " — " + weight : format));

            var save = Buttons.IconText("file-arrow-down-bold", "Enregistrer l'image…",
                "Enregistre l'image elle-même (ses octets d'origine) dans un fichier",
                Buttons.Bar, Buttons.Look.Outline);
            save.Margin = new Thickness(0, 16, 0, 0);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.IsEnabled = info.Bytes != null;
            save.Click += delegate { SaveImageToDisk(info); };
            panel.Children.Add(save);
            return panel;
        }

        private void SaveImageToDisk(SelectedImageInfo info)
        {
            if (info.Bytes == null) return;
            var extension = string.IsNullOrEmpty(info.Extension) ? ".png" : info.Extension.ToLowerInvariant();
            var suggested = string.IsNullOrEmpty(info.Name) ? "image" + extension : info.Name;
            if (!suggested.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                suggested = Path.GetFileNameWithoutExtension(suggested) + extension;
            foreach (var bad in Path.GetInvalidFileNameChars()) suggested = suggested.Replace(bad, '_');
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = suggested,
                Filter = FormatLabel(extension) + " (*" + extension + ")|*" + extension + "|Tous les fichiers|*.*",
                Title = "Enregistrer l'image"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllBytes(dialog.FileName, info.Bytes);
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Impossible d'enregistrer l'image :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

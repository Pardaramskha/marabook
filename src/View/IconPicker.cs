using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Per-item Binder icons. Icon tokens: null (default per kind),
    /// "svg:&lt;name&gt;" or "svg:&lt;name&gt;:#RRGGBB" (embedded Phosphor icon,
    /// tintable), "glyph:&lt;char&gt;" (legacy Segoe MDL2), "file:&lt;name&gt;"
    /// (user image in %APPDATA%\Univers Sale\icons — app-local by design).</summary>
    public static class ItemIcons
    {
        private static readonly Dictionary<string, ImageSource> _fileCache
            = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Preset SVG icons offered by the picker.</summary>
        public static readonly string[] PresetSvgs =
        {
            "file-text-bold", "file-dashed-bold", "folder-bold", "book-bold",
            "books-bold", "book-open-text-bold", "files-bold", "article-bold",
            "image-square-bold", "magnifying-glass-bold", "paragraph-bold",
            "list-dashes-bold", "list-numbers-bold", "check-square-bold",
            "text-t-bold", "text-columns-bold", "file-arrow-down-bold", "trash-bold",
            "document", "ecrits", "fiches-menu", "fiche-individual",
            "tableau-recherche", "folder-open", "extra-document", "trash",
            "journal-perso", "apercu-wiki", "palette", "symbol", "kerning"
        };

        /// <summary>Tint swatches for SVG icons (null = ink color).</summary>
        public static readonly string[] TintSwatches =
        {
            null, "#5B67D8", "#C0392B", "#E67E22", "#C9A227",
            "#27AE60", "#16A085", "#2980B9", "#8E44AD", "#7F8C8D"
        };

        public static string IconsFolder()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Marabook", "icons");
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>Default embedded SVG icon for an item, by kind/category.
        /// Null = no SVG default (non-image media keep an MDL2 glyph).</summary>
        public static string DefaultSvg(BinderItem item)
        {
            if (item.IsCategory)
            {
                // Les icônes des grandes catégories de la Pile : le jeu livré
                // par l'utilisateur (batch 35, assets/icons/pile-*.svg).
                if (item.CategoryKey == Project.KeyHome) return "apercu-wiki"; // b41 — provisoire : pas d'icône « accueil » dans le jeu
                if (item.CategoryKey == Project.KeyWritings) return "pile-ecrits";
                if (item.CategoryKey == Project.KeyResearch) return "pile-recherche";
                if (item.CategoryKey == Project.KeySheets) return "pile-fiches";
                if (item.CategoryKey == Project.KeyPlans) return "pile-plans";
                if (item.CategoryKey == Project.KeyDictionary) return "pile-dictionnaire";
                if (item.CategoryKey == Project.KeyTrash) return "pile-corbeille";
            }
            if (item.Kind == ItemKind.Folder) return "folder-open";
            if (item.Kind == ItemKind.Book) return "book-bold";
            if (item.Kind == ItemKind.PageTemplate) return "article-bold";
            if (item.Kind == ItemKind.Plan) return "plan"; // plan.svg livré (b36)
            if (item.Kind == ItemKind.Sheet) return "fiche-individual";
            if (item.Kind == ItemKind.Media)
                return MediaView.IsImage(item.MediaExtension) ? "image-square-bold" : null;
            if (item.IsExtraPage || item.IsToc) return "extra-document";
            return "document";
        }

        /// <summary>Builds the small icon element shown in the Binder tree,
        /// search results and corkboard cards.</summary>
        public static UIElement Render(BinderItem item, double size, Brush glyphBrush)
        {
            var icon = item.Icon;
            if (icon != null && icon.StartsWith("file:", StringComparison.Ordinal))
            {
                var source = LoadCustom(icon.Substring(5));
                if (source != null)
                    return new Image
                    {
                        Source = source,
                        Width = size + 2,
                        Height = size + 2,
                        Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                icon = null; // file gone: fall back to the default
            }
            if (icon != null && icon.StartsWith("svg:", StringComparison.Ordinal))
            {
                var token = icon.Substring(4);
                var colon = token.IndexOf(':');
                var name = colon < 0 ? token : token.Substring(0, colon);
                var brush = glyphBrush;
                if (colon > 0)
                {
                    var tinted = new SolidColorBrush(
                        FlowConverter.ParseColor(token.Substring(colon + 1)));
                    tinted.Freeze();
                    brush = tinted;
                }
                if (Icons.Has(name)) return WrapIcon(Icons.Make(name, size + 1, brush));
                icon = null;
            }
            if (icon != null && icon.StartsWith("glyph:", StringComparison.Ordinal)
                && icon.Length > 6)
                return MakeGlyph(icon.Substring(6), size, glyphBrush);

            var svg = DefaultSvg(item);
            if (svg != null) return WrapIcon(Icons.Make(svg, size + 1, glyphBrush));
            return MakeGlyph("\uE723", size, glyphBrush); // MDL2 attach (rare media)
        }

        private static UIElement WrapIcon(UIElement icon)
        {
            var element = icon as FrameworkElement;
            if (element != null)
            {
                element.VerticalAlignment = VerticalAlignment.Center;
                element.Margin = new Thickness(0, 0, 6, 0);
            }
            return icon;
        }

        private static UIElement MakeGlyph(string glyph, double size, Brush brush)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = size,
                Foreground = brush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
        }

        public static ImageSource LoadCustom(string fileName)
        {
            ImageSource cached;
            if (_fileCache.TryGetValue(fileName, out cached)) return cached;
            try
            {
                var path = Path.Combine(IconsFolder(), fileName);
                if (!File.Exists(path)) return null;
                var source = MediaView.TryImage(File.ReadAllBytes(path), 48);
                _fileCache[fileName] = source;
                return source;
            }
            catch
            {
                return null;
            }
        }

        public static void ForgetCache()
        {
            _fileCache.Clear();
        }
    }

    /// <summary>Icon chooser: the embedded SVG set — tintable via the color
    /// row — plus the user's custom images. Returns the icon token, "" for
    /// « par défaut », or null on cancel.</summary>
    public class IconPickerDialog : Window
    {
        private string _result;
        private bool _accepted;
        private string _tint; // null = ink
        private readonly WrapPanel _presetPanel;
        private readonly WrapPanel _customPanel;

        private IconPickerDialog(Window owner)
        {
            Title = "Changer l'icône";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 430;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16) };

            panel.Children.Add(new TextBlock
            {
                Text = "Couleur (icônes vectorielles)",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });
            var tints = new WrapPanel();
            foreach (var hex in ItemIcons.TintSwatches)
                tints.Children.Add(TintButton(hex));
            panel.Children.Add(tints);

            panel.Children.Add(new TextBlock
            {
                Text = "Icônes proposées",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 12, 0, 6)
            });
            _presetPanel = new WrapPanel();
            panel.Children.Add(_presetPanel);
            FillPresets();

            panel.Children.Add(new TextBlock
            {
                Text = "Icônes personnalisées (images)",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 12, 0, 6)
            });
            _customPanel = new WrapPanel();
            panel.Children.Add(_customPanel);
            FillCustom();

            var addCustom = new Button
            {
                Content = Icons.Label("plus-bold", "Ajouter une image d'icône…", 11, Chrome.Ink),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "Une image (PNG, JPG…) copiée dans le dossier d'icônes de l'application"
            };
            addCustom.Click += delegate { AddCustom(); };
            panel.Children.Add(addCustom);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var reset = new Button { Content = "Icône par défaut", MinWidth = 110 };
            reset.Click += delegate { _result = ""; _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(reset);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
        }

        public static string Ask(Window owner)
        {
            var dialog = new IconPickerDialog(owner);
            dialog.ShowDialog();
            return dialog._accepted ? dialog._result : null;
        }

        private System.Windows.Controls.Primitives.ToggleButton TintButton(string hex)
        {
            var button = new System.Windows.Controls.Primitives.ToggleButton
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                IsChecked = hex == _tint,
                ToolTip = hex ?? "Couleur d'encre du thème",
                Content = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    Background = hex == null
                        ? (Brush)Chrome.Ink
                        : new SolidColorBrush(FlowConverter.ParseColor(hex))
                }
            };
            button.Click += delegate
            {
                _tint = hex;
                // radio behavior + live preview refresh
                foreach (var child in ((WrapPanel)button.Parent).Children)
                {
                    var other = child as System.Windows.Controls.Primitives.ToggleButton;
                    if (other != null) other.IsChecked = ReferenceEquals(other, button);
                }
                FillPresets();
            };
            return button;
        }

        private void FillPresets()
        {
            _presetPanel.Children.Clear();
            var brush = _tint == null
                ? (Brush)Chrome.Ink
                : new SolidColorBrush(FlowConverter.ParseColor(_tint));
            foreach (var name in ItemIcons.PresetSvgs)
            {
                var button = new Button
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(2),
                    ToolTip = name,
                    Content = Icons.Make(name, 18, brush)
                };
                var nameRef = name;
                button.Click += delegate
                {
                    _result = "svg:" + nameRef + (_tint != null ? ":" + _tint : "");
                    _accepted = true;
                    Close();
                };
                _presetPanel.Children.Add(button);
            }
        }

        private void FillCustom()
        {
            _customPanel.Children.Clear();
            string[] files;
            try { files = Directory.GetFiles(ItemIcons.IconsFolder()); }
            catch { files = new string[0]; }
            foreach (var path in files)
            {
                var name = Path.GetFileName(path);
                var source = ItemIcons.LoadCustom(name);
                if (source == null) continue;
                var button = new Button
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(2),
                    ToolTip = name,
                    Content = new Image { Source = source, Stretch = Stretch.Uniform }
                };
                var nameRef = name;
                button.Click += delegate { _result = "file:" + nameRef; _accepted = true; Close(); };
                _customPanel.Children.Add(button);
            }
            if (_customPanel.Children.Count == 0)
                _customPanel.Children.Add(new TextBlock
                {
                    Text = "(aucune pour l'instant)",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Margin = new Thickness(2)
                });
        }

        private void AddCustom()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
                Multiselect = true
            };
            if (dialog.ShowDialog(this) != true) return;
            foreach (var path in dialog.FileNames)
            {
                try
                {
                    var destination = Path.Combine(ItemIcons.IconsFolder(), Path.GetFileName(path));
                    var stem = Path.GetFileNameWithoutExtension(destination);
                    var ext = Path.GetExtension(destination);
                    var counter = 1;
                    while (File.Exists(destination))
                        destination = Path.Combine(ItemIcons.IconsFolder(),
                            stem + " (" + (++counter) + ")" + ext);
                    File.Copy(path, destination);
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, "Icône non ajoutée :\n" + error.Message,
                        "Icônes", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            ItemIcons.ForgetCache();
            FillCustom();
        }
    }
}

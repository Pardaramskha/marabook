using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Model;

namespace Marabook.App
{
    /// <summary>Per-item Binder icons. Icon tokens: null (default per kind),
    /// "svg:&lt;name&gt;" or "svg:&lt;name&gt;:#RRGGBB" (embedded Phosphor icon,
    /// tintable), "glyph:&lt;char&gt;" (legacy Segoe MDL2), "file:&lt;name&gt;"
    /// (user image in %APPDATA%\Marabook\icons — app-local by design).</summary>
    public static class ItemIcons
    {
        private static readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap> _fileCache
            = new Dictionary<string, Avalonia.Media.Imaging.Bitmap>(StringComparer.OrdinalIgnoreCase);

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
            var folder = System.IO.Path.Combine(
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
                if (item.CategoryKey == Project.KeyMindMaps) return "connection"; // cartes mentales (22/09)
                if (item.CategoryKey == Project.KeyDictionary) return "pile-dictionnaire";
                if (item.CategoryKey == Project.KeyTrash) return "pile-corbeille";
            }
            if (item.Kind == ItemKind.Folder) return "folder-open";
            if (item.Kind == ItemKind.Book) return "book-bold";
            if (item.Kind == ItemKind.PageTemplate) return "article-bold";
            if (item.Kind == ItemKind.Plan) return "plan"; // plan.svg livré (b36)
            if (item.Kind == ItemKind.MindMap) return "git-branch"; // carte mentale (22/09)
            if (item.Kind == ItemKind.Sheet) return "fiche-individual";
            if (item.Kind == ItemKind.Media)
                return MediaView.IsImage(item.MediaExtension) ? "image-square-bold" : null;
            if (item.IsExtraPage || item.IsToc) return "extra-document";
            return "document";
        }

        /// <summary>Builds the small icon element shown in the Binder tree,
        /// search results and corkboard cards.</summary>
        public static Control Render(BinderItem item, double size, IBrush glyphBrush)
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
                        Ink.Parse(token.Substring(colon + 1)).ToColor());
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

        private static Control WrapIcon(Control icon)
        {
            var element = icon as Control;
            if (element != null)
            {
                element.VerticalAlignment = VerticalAlignment.Center;
                element.Margin = new Thickness(0, 0, 6, 0);
            }
            return icon;
        }

        private static Control MakeGlyph(string glyph, double size, IBrush brush)
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

        public static Avalonia.Media.Imaging.Bitmap LoadCustom(string fileName)
        {
            Avalonia.Media.Imaging.Bitmap cached;
            if (_fileCache.TryGetValue(fileName, out cached)) return cached;
            try
            {
                var path = System.IO.Path.Combine(IconsFolder(), fileName);
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
            CanResize = false;
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
                [ToolTip.TipProperty] = "Une image (PNG, JPG…) copiée dans le dossier d'icônes de l'application"
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

        public static async Task<string> Ask(Window owner)
        {
            var dialog = new IconPickerDialog(owner);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._accepted ? dialog._result : null;
        }

        private ToggleButton TintButton(string hex)
        {
            var button = new ToggleButton
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                IsChecked = hex == _tint,
                [ToolTip.TipProperty] = hex ?? "Couleur d'encre du thème",
                Content = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(7),
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    Background = hex == null
                        ? (IBrush)Chrome.Ink
                        : new SolidColorBrush(Ink.Parse(hex).ToColor())
                }
            };
            button.Click += delegate
            {
                _tint = hex;
                // radio behavior + live preview refresh
                foreach (var child in ((WrapPanel)button.Parent).Children)
                {
                    var other = child as ToggleButton;
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
                ? (IBrush)Chrome.Ink
                : new SolidColorBrush(Ink.Parse(_tint).ToColor());
            foreach (var name in ItemIcons.PresetSvgs)
            {
                var button = new Button
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(2),
                    [ToolTip.TipProperty] = name,
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
                var name = System.IO.Path.GetFileName(path);
                var source = ItemIcons.LoadCustom(name);
                if (source == null) continue;
                var button = new Button
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(2),
                    [ToolTip.TipProperty] = name,
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

        private async void AddCustom()
        {
            var dialogPaths = await Ui.PickOpenFiles(this, "", "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp");
            if (dialogPaths == null || dialogPaths.Length == 0) return;
            foreach (var path in dialogPaths)
            {
                try
                {
                    var destination = System.IO.Path.Combine(ItemIcons.IconsFolder(), System.IO.Path.GetFileName(path));
                    var stem = System.IO.Path.GetFileNameWithoutExtension(destination);
                    var ext = System.IO.Path.GetExtension(destination);
                    var counter = 1;
                    while (File.Exists(destination))
                        destination = System.IO.Path.Combine(ItemIcons.IconsFolder(),
                            stem + " (" + (++counter) + ")" + ext);
                    File.Copy(path, destination);
                }
                catch (Exception error)
                {
                    MessageDialog.Show(this, "Icône non ajoutée :\n" + error.Message,
                        "Icônes", MessageButtons.OK, MessageIcon.Warning);
                }
            }
            ItemIcons.ForgetCache();
            FillCustom();
        }
    }
}

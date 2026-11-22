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
        public static readonly string[] TintSwatches = ColorMenus.GeneralWithNone(); // le nuancier général de la palette (22/11)

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
                if (item.CategoryKey == Project.KeyTimelines) return "pile-chronologie"; // clockwork.svg de Rémi (16/11) : les frises ont leur racine
                if (item.CategoryKey == Project.KeyAstrolabe) return "pile-frises"; // astrolabe.svg de Rémi (1.2.0)
                if (item.CategoryKey == Project.KeyMaps) return "pile-cartes"; // world.svg de Rémi (16/11)
                if (item.CategoryKey == Project.KeyMindMaps) return "mental-o"; // cartes mentales : l'icône de Mental-O (26/10, de Rémi)
                if (item.CategoryKey == Project.KeyDictionary) return "pile-dictionnaire";
                if (item.CategoryKey == Project.KeyTrash) return "pile-corbeille";
            }
            if (item.Kind == ItemKind.Folder) return "folder-open";
            if (item.Kind == ItemKind.Book) return "book-bold";
            if (item.Kind == ItemKind.PageTemplate) return "article-bold";
            if (item.Kind == ItemKind.Plan) return "plan"; // plan.svg livré (b36)
            if (item.Kind == ItemKind.Timeline) return "frise"; // diagram.svg de Rémi (1.2.0)
            if (item.Kind == ItemKind.Map) return "pile-cartes"; // une carte : le monde (16/11)
            if (item.Kind == ItemKind.MindMap) return "diagram"; // une carte mentale : diagram.png de Rémi (26/10)
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

    /// <summary>Le choix de l'icône : le jeu vectoriel livré — qui se TEINTE —
    /// et les images de l'auteur. Rend le jeton d'icône, "" pour « par
    /// défaut », null si annulé.
    /// 22/11/2026 (Rémi) : l'icône ET sa couleur se règlent dans cette même
    /// fenêtre (l'entrée « Couleur de l'icône » des menus a disparu). Elle
    /// s'ouvre sur l'icône et la couleur en cours, montre ce que ça donne, et
    /// ne s'applique qu'à « Appliquer » : on peut changer la couleur seule,
    /// l'icône seule, ou les deux. Les couleurs sont celles de la palette.</summary>
    public class IconPickerDialog : Window
    {
        private const string DefaultChoice = ""; // l'icône d'origine de l'élément
        private bool _accepted;
        private string _tint;   // null = l'encre du thème
        private string _choice; // "" = d'origine ; « svg:nom » ; « file:nom » ; null = rien de choisi (lot)
        private readonly BinderItem _item;
        private readonly Project _project;
        private readonly string _defaultSvg;
        private readonly WrapPanel _tints, _presetPanel, _customPanel;
        private readonly Border _preview;
        private readonly TextBlock _previewNote;
        private readonly Button _apply;

        private IconPickerDialog(Window owner, Project project, BinderItem item)
        {
            _item = item;
            _project = project;
            _defaultSvg = item == null ? null : ItemIcons.DefaultSvg(item);
            Title = "Icône";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 450;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            Tag = "icon-dialog";

            // L'état en cours : l'icône de l'élément et sa teinte.
            _choice = item == null ? null : DefaultChoice;
            var current = item == null ? null : item.Icon;
            if (current != null && current.StartsWith("svg:", StringComparison.Ordinal))
            {
                var token = current.Substring(4);
                var colon = token.IndexOf(':');
                _choice = "svg:" + (colon < 0 ? token : token.Substring(0, colon));
                _tint = colon < 0 ? null : ColorPalette.Normalize(token.Substring(colon + 1));
            }
            else if (current != null && (current.StartsWith("file:", StringComparison.Ordinal) || current.StartsWith("glyph:", StringComparison.Ordinal))) _choice = current;

            var panel = new StackPanel { Margin = new Thickness(16) };

            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            _preview = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(8), Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = "icon-preview" };
            DockPanel.SetDock(_preview, Dock.Left);
            head.Children.Add(_preview);
            var headText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            headText.Children.Add(new TextBlock { Text = item == null ? "Icône de la sélection" : item.Title, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, TextTrimming = TextTrimming.CharacterEllipsis });
            _previewNote = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Tag = "icon-note" };
            headText.Children.Add(_previewNote);
            head.Children.Add(headText);
            panel.Children.Add(head);

            panel.Children.Add(Section("Couleur", 0));
            _tints = new WrapPanel { Tag = "icon-tints" };
            panel.Children.Add(_tints);

            panel.Children.Add(Section("Icônes proposées", 8));
            _presetPanel = new WrapPanel { Tag = "icon-presets" };
            panel.Children.Add(_presetPanel);

            panel.Children.Add(Section("Icônes personnalisées (images)", 12));
            _customPanel = new WrapPanel();
            panel.Children.Add(_customPanel);

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
                Margin = new Thickness(0, 16, 0, 0)
            };
            var reset = new Button { Content = "Icône par défaut", MinWidth = 110, Tag = "icon-reset" };
            ToolTip.SetTip(reset, "Revenir à l'icône d'origine, sans couleur");
            reset.Click += delegate { _choice = DefaultChoice; _tint = null; Refresh(); };
            _apply = new Button { Content = "Appliquer", IsDefault = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Tag = "icon-apply" };
            _apply.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); }; // IsCancel ne ferme pas la fenêtre sur Avalonia (28/09)
            buttons.Children.Add(reset);
            buttons.Children.Add(_apply);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, _apply); // validation à droite, principale (30/09)
            panel.Children.Add(buttons);

            Content = panel;
            FillCustom();
            Refresh();
        }

        private static TextBlock Section(string text, double top)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, top, 0, 6) };
        }

        /// <summary>Sans élément (le lot, les options du livre) : on choisit une icône, teintée ou non.</summary>
        public static Task<string> Ask(Window owner) { return Ask(owner, null, null); }

        /// <summary>Pour CET élément : la fenêtre s'ouvre sur son icône et sa couleur.</summary>
        public static async Task<string> Ask(Window owner, Project project, BinderItem item)
        {
            var dialog = new IconPickerDialog(owner, project, item);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._accepted ? dialog.Result() : null;
        }

        /// <summary>Le nom de l'icône vectorielle en jeu (celle choisie, ou celle d'origine), ou null.</summary>
        private string SvgName()
        {
            if (_choice == DefaultChoice) return _defaultSvg;
            return _choice != null && _choice.StartsWith("svg:", StringComparison.Ordinal) ? _choice.Substring(4) : null;
        }

        /// <summary>Le jeton à ranger : "" = d'origine ; null = rien à faire.</summary>
        internal string Result()
        {
            if (_choice == null) return null;
            var name = SvgName();
            if (name == null) return _choice; // une image, un glyphe : pas de teinte
            if (_choice == DefaultChoice) return _tint == null ? "" : "svg:" + name + ":" + _tint; // l'icône d'origine, teintée
            return "svg:" + name + (_tint != null ? ":" + _tint : "");
        }

        private void Refresh()
        {
            var brush = _tint == null ? (IBrush)Chrome.Ink : new SolidColorBrush(Ink.Parse(_tint).ToColor());
            var name = SvgName();
            // L'aperçu.
            Control shown = null;
            if (name != null && Icons.Has(name)) shown = Icons.Make(name, 28, brush);
            else if (_choice != null && _choice.StartsWith("file:", StringComparison.Ordinal))
            {
                var source = ItemIcons.LoadCustom(_choice.Substring(5));
                if (source != null) shown = new Image { Source = source, Width = 32, Height = 32, Stretch = Stretch.Uniform };
            }
            if (shown != null) { shown.HorizontalAlignment = HorizontalAlignment.Center; shown.VerticalAlignment = VerticalAlignment.Center; }
            _preview.Child = shown;
            _previewNote.Text = _choice == null ? "Choisissez une icône."
                : name == null ? "Une image ne se teinte pas : la couleur vaut pour les icônes proposées."
                : (_choice == DefaultChoice ? "Icône d'origine" : "Icône choisie") + (_tint == null ? ", à l'encre du thème." : " — " + ColorPalette.Label(_tint) + ".");
            _apply.IsEnabled = _choice != null;

            ColorMenus.FillSwatches(_tints, this, _project, _tint, true, "L'encre du thème : pas de couleur", delegate(string hex) { _tint = hex; Refresh(); });

            _presetPanel.Children.Clear();
            foreach (var preset in ItemIcons.PresetSvgs)
            {
                var presetName = preset;
                var chosen = _choice == "svg:" + presetName;
                var button = new Button
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(2),
                    BorderBrush = chosen ? (IBrush)Chrome.Accent : Chrome.Border,
                    BorderThickness = new Thickness(chosen ? 2 : 1),
                    Background = chosen ? (IBrush)Chrome.AccentTint : Chrome.FieldBg,
                    Content = Icons.Make(presetName, 18, brush),
                    Tag = "icon-preset:" + presetName
                };
                button.Click += delegate { _choice = "svg:" + presetName; Refresh(); };
                button.DoubleTapped += delegate { _choice = "svg:" + presetName; _accepted = true; Close(); };
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
                button.Click += delegate { _choice = "file:" + nameRef; Refresh(); };
                button.DoubleTapped += delegate { _choice = "file:" + nameRef; _accepted = true; Close(); };
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

        // — Sonde.
        internal void ChooseForProbe(string preset, string tint) { if (preset != null) _choice = "svg:" + preset; _tint = tint; Refresh(); }
        internal void ResetForProbe() { _choice = DefaultChoice; _tint = null; Refresh(); }
        internal static IconPickerDialog ForProbe(Window owner, Project project, BinderItem item) { return new IconPickerDialog(owner, project, item); }
    }
}

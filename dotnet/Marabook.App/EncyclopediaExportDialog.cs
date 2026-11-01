using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marabook.Exchange;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE DIALOGUE DE L'ENCYCLOPÉDIE (1.1.0, 31/10, Rémi) : à gauche
    /// l'arbre cochable des fiches (catégories, sous-catégories, fiches —
    /// préréglé selon l'origine : tout, une catégorie, une fiche) ; à droite
    /// les options (format, titres, polices, taille, accent, clair ou sombre,
    /// encarts, Lexique) ; en bas « Exporter… » et « ouvrir l'accueil ». Rend
    /// la sélection et les options, ou null.</summary>
    public class EncyclopediaExportDialog : Window
    {
        private readonly Project _project;
        private readonly List<CheckBox> _sheetBoxes = new List<CheckBox>();
        private readonly Dictionary<string, CheckBox> _categoryBoxes = new Dictionary<string, CheckBox>();
        private readonly TextBlock _countLabel;
        private readonly TextBox _title, _subtitle, _author;
        private readonly Button _accentButton;
        private readonly Border _accentDot;
        private readonly TextBlock _accentText;
        private string _accent;          // l'accent, « #RRGGBB » (1/11 : le nuancier de Marabook plutôt qu'un champ hex)
        private readonly FontPicker _bodyFont, _headingFont;
        private readonly ComboBox _size, _portrait, _format;
        private readonly CheckBox _dark, _infobox, _relations, _presence, _radar, _tags, _dates, _lexicon, _lexiconUndefined, _lexiconNoNames, _lexiconGlobal, _emptySheets, _open;
        private readonly Image _coverPreview;
        private readonly TextBlock _coverLabel;
        private readonly Button _coverRemove;
        private string _coverImageId;   // la couverture de l'accueil (1/11) : une image du projet, ou null
        private bool _accepted, _syncing;

        public sealed class Answer
        {
            public EncyclopediaSelection Selection;
            public EncyclopediaOptions Options;
            public bool OpenAfter;
        }

        private EncyclopediaExportDialog(Window owner, Project project, EncyclopediaSelection preset, EncyclopediaOptions defaults)
        {
            _project = project;
            Title = "Exporter l'Encyclopédie";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new StackPanel { Margin = new Thickness(16), MinWidth = 760, MaxWidth = 860 };
            root.Children.Add(new TextBlock
            {
                Text = "Les fiches choisies deviennent un dossier HTML navigable : une page d'accueil par catégorie et par fiche, le Lexique en dictionnaire, lisible dans tout navigateur et prêt à partager.",
                Foreground = Chrome.SoftText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("320,24,*") };

            // ---- la sélection
            var left = new StackPanel();
            left.Children.Add(Caption("Fiches à exporter"));
            var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
            var all = Buttons.Text("Toutes", "Cocher toutes les fiches", Buttons.Compact, Buttons.Look.Calm);
            all.Click += delegate { SetAll(true); };
            var none = Buttons.Text("Aucune", "Décocher toutes les fiches", Buttons.Compact, Buttons.Look.Calm);
            none.Click += delegate { SetAll(false); };
            quick.Children.Add(all);
            quick.Children.Add(none);
            _countLabel = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            quick.Children.Add(_countLabel);
            left.Children.Add(quick);
            var tree = new StackPanel { Spacing = 2 };
            // Depuis le menu Fichier (préréglage « tout »), les fiches cochées au dernier export reviennent (1/11) ; depuis une fiche ou une catégorie, c'est elle.
            var remembered = preset == null || preset.Scope == EncyclopediaSelection.ScopeAll ? RememberedSelection(defaults) : null;
            BuildTree(tree, remembered ?? preset);
            left.Children.Add(new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Background = Chrome.FieldBg,
                Padding = new Thickness(8),
                Child = new ScrollViewer { Content = tree, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            });
            Grid.SetColumn(left, 0);
            columns.Children.Add(left);

            // ---- les options
            var right = new StackPanel();
            right.Children.Add(Caption("Format"));
            _format = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            _format.Items.Add(new ComboBoxItem { Content = "HTML — un dossier navigable" });
            _format.Items.Add(new ComboBoxItem { Content = "PDF — bientôt", IsEnabled = false }); // grisé (1/11, Rémi) : le lot PDF n'est pas livré
            _format.SelectedIndex = 0;
            ToolTip.SetTip(_format, "Le PDF arrive dans un prochain lot : le HTML est prêt");
            _format.SelectionChanged += delegate { if (_format.SelectedIndex != 0) _format.SelectedIndex = 0; };
            right.Children.Add(_format);

            right.Children.Add(Caption("Titres", 12));
            _title = Field(right, "Titre", defaults.Title, "Vide : « Encyclopédie de <projet> » (du, des, d'… selon le nom)");
            _subtitle = Field(right, "Sous-titre", defaults.Subtitle, null);
            _author = Field(right, "Auteur·ice", defaults.Author, "Vide : l'auteur·ice du projet");

            // La couverture (1/11) : une image du disque, recadrable, en tête de l'accueil ; retenue dans le projet avec les options.
            var coverRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var coverCaption = new TextBlock { Text = "Couverture", Width = 90, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(coverCaption, Dock.Left);
            coverRow.Children.Add(coverCaption);
            _coverPreview = new Image { Stretch = Stretch.UniformToFill };
            var frame = new Border
            {
                Width = 40, Height = 54, CornerRadius = new CornerRadius(4), ClipToBounds = true,
                Background = Chrome.FieldBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0), Child = _coverPreview
            };
            DockPanel.SetDock(frame, Dock.Left);
            coverRow.Children.Add(frame);
            var coverButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(coverButtons, Dock.Right);
            var pickCover = Buttons.Text("Choisir…", "Une image du disque, en grand sur l'accueil", Buttons.Compact, Buttons.Look.Calm);
            pickCover.Click += async delegate { await PickCover(); };
            _coverRemove = Buttons.Text("Retirer", "Plus de couverture", Buttons.Compact, Buttons.Look.Calm);
            _coverRemove.Click += delegate { SetCover(null); };
            coverButtons.Children.Add(pickCover);
            coverButtons.Children.Add(_coverRemove);
            coverRow.Children.Add(coverButtons);
            _coverLabel = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
            coverRow.Children.Add(_coverLabel);
            right.Children.Add(coverRow);
            SetCover(defaults.CoverImageId);

            right.Children.Add(Caption("Apparence", 12));
            var fonts = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 0, 0, 4) };
            _bodyFont = new FontPicker { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };
            if (!string.IsNullOrEmpty(defaults.BodyFont)) _bodyFont.Select(defaults.BodyFont);
            ToolTip.SetTip(_bodyFont, "La police du texte ; vide : une serif du lecteur");
            _headingFont = new FontPicker { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };
            if (!string.IsNullOrEmpty(defaults.HeadingFont)) _headingFont.Select(defaults.HeadingFont);
            ToolTip.SetTip(_headingFont, "La police des titres et de la navigation ; vide : une sans-serif du lecteur");
            _size = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var px in new[] { 14, 15, 16, 17, 18, 20 }) _size.Items.Add(px + " px");
            _size.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { 14, 15, 16, 17, 18, 20 }, (int)Math.Round(defaults.BaseSize)));
            if (_size.SelectedIndex < 0) _size.SelectedIndex = 2;
            // L'accent (1/11, Rémi : « personne ne connaît les couleurs hex par cœur ») : la pastille et le nom, le nuancier partagé au clic (ColorMenus : nuancier, couleurs du projet, « Nouvelle couleur… »).
            _accent = EncyclopediaOptions.SafeColor(defaults.Accent, "#5B67D8");
            _accentDot = ColorMenus.Dot(_accent, 1, Chrome.Border);
            _accentText = new TextBlock { Text = _accent, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var accentContent = new StackPanel { Orientation = Orientation.Horizontal };
            accentContent.Children.Add(_accentDot);
            accentContent.Children.Add(_accentText);
            _accentButton = new Button { Content = accentContent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
            ToolTip.SetTip(_accentButton, "La couleur des liens, des titres de section et des tags");
            _accentButton.Click += delegate
            {
                var menu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = _accentButton };
                ColorMenus.Fill(menu, this, _project, _accent, delegate(string value)
                {
                    _accent = EncyclopediaOptions.SafeColor(value, _accent);
                    _accentDot.Background = new SolidColorBrush(Ink.Parse(_accent).ToColor());
                    _accentText.Text = _accent;
                }, false);
                Ui.ShowMenu(menu, _accentButton);
            };
            _portrait = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };
            _portrait.Items.Add("À droite du texte");
            _portrait.Items.Add("À gauche du texte");
            _portrait.Items.Add("Au-dessus du texte");
            _portrait.SelectedIndex = defaults.PortraitSide == "left" ? 1 : defaults.PortraitSide == "top" ? 2 : 0;
            AddRow(fonts, 0, "Texte", _bodyFont);
            AddRow(fonts, 1, "Titres", _headingFont);
            AddRow(fonts, 2, "Taille", _size);
            AddRow(fonts, 3, "Accent", _accentButton);
            AddRow(fonts, 4, "Portrait", _portrait);
            right.Children.Add(fonts);
            _dark = new CheckBox { Content = "Thème sombre", IsChecked = defaults.Dark };
            right.Children.Add(_dark);

            right.Children.Add(Caption("Encarts des fiches", 12));
            var encarts = new WrapPanel();
            _infobox = Check(encarts, "Infobox (champs)", defaults.ShowInfobox);
            _relations = Check(encarts, "Relations", defaults.ShowRelations);
            _presence = Check(encarts, "Évolution et présence", defaults.ShowPresence);
            _radar = Check(encarts, "Radar", defaults.ShowRadar);
            _tags = Check(encarts, "Tags", defaults.ShowTags);
            _dates = Check(encarts, "Date d'export", defaults.ShowDates);
            right.Children.Add(encarts);
            _emptySheets = new CheckBox { Content = "Garder les fiches vides (titre seul)", IsChecked = defaults.IncludeEmptySheets, Margin = new Thickness(0, 4, 0, 0) };
            right.Children.Add(_emptySheets);

            right.Children.Add(Caption("Lexique", 12));
            // Le Dictionnaire (1/11, Rémi) : celui de CE projet, les mots sans définition d'office, les prénoms et noms de famille à écarter, tous les projets en option.
            _lexicon = new CheckBox { Content = "Exporter le Dictionnaire de ce projet (page « Lexique »)", IsChecked = defaults.IncludeLexicon };
            _lexiconUndefined = new CheckBox { Content = "Exporter les mots sans définition", IsChecked = defaults.LexiconUndefined, Margin = new Thickness(24, 0, 0, 0) };
            _lexiconNoNames = new CheckBox { Content = "Exclure les prénoms et noms de famille", IsChecked = defaults.LexiconExcludeNames, Margin = new Thickness(24, 0, 0, 0) };
            ToolTip.SetTip(_lexiconNoNames, "Les noms propres notés « Prénom » ou « Nom de famille » dans le Dictionnaire restent au vestiaire");
            _lexiconGlobal = new CheckBox { Content = "Ajouter les mots du dictionnaire de tous les projets", IsChecked = defaults.LexiconAllProjects, Margin = new Thickness(24, 0, 0, 0) };
            _lexicon.IsCheckedChanged += delegate { _lexiconUndefined.IsEnabled = _lexiconNoNames.IsEnabled = _lexiconGlobal.IsEnabled = _lexicon.IsChecked == true; };
            _lexiconUndefined.IsEnabled = _lexiconNoNames.IsEnabled = _lexiconGlobal.IsEnabled = _lexicon.IsChecked == true;
            right.Children.Add(_lexicon);
            right.Children.Add(_lexiconUndefined);
            right.Children.Add(_lexiconNoNames);
            right.Children.Add(_lexiconGlobal);
            Grid.SetColumn(right, 2);
            columns.Children.Add(right);
            root.Children.Add(columns);

            _open = new CheckBox { Content = "Ouvrir l'accueil dans le navigateur une fois l'export fait", IsChecked = defaults.OpenAfter, Margin = new Thickness(0, 14, 0, 0) };
            root.Children.Add(_open);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = "Exporter…", IsDefault = true, MinWidth = 120 };
            ok.Click += delegate
            {
                if (SelectedIds().Count == 0) { MessageDialog.Show(this, "Cochez au moins une fiche.", "Encyclopédie", MessageButtons.OK, MessageIcon.Information); return; }
                _accepted = true;
                Close();
            };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, ok);
            root.Children.Add(buttons);
            Content = root;
            UpdateCount();
        }

        // ---------------------------------------------------------------- l'arbre

        /// <summary>La sélection du dernier export, si elle vise encore au
        /// moins une fiche du projet ; sinon null (tout).</summary>
        private EncyclopediaSelection RememberedSelection(EncyclopediaOptions defaults)
        {
            if (defaults == null || defaults.SheetIds == null || defaults.SheetIds.Count == 0) return null;
            var selection = new EncyclopediaSelection { Scope = EncyclopediaSelection.ScopeSheets };
            foreach (var id in defaults.SheetIds) if (_project.FindById(id) != null) selection.Ids.Add(id);
            return selection.Ids.Count == 0 ? null : selection;
        }

        private void BuildTree(StackPanel tree, EncyclopediaSelection preset)
        {
            var sheets = Encyclopedia.SelectSheets(_project, EncyclopediaSelection.All(), new EncyclopediaOptions());
            var wanted = new HashSet<string>();
            foreach (var item in Encyclopedia.SelectSheets(_project, preset ?? EncyclopediaSelection.All(), new EncyclopediaOptions())) wanted.Add(item.Id);
            var byCategory = new Dictionary<string, List<BinderItem>>();
            var loose = new List<BinderItem>();
            foreach (var item in sheets)
            {
                if (item.CategoryId != null && _project.FindSheetCategory(item.CategoryId) != null)
                {
                    List<BinderItem> list;
                    if (!byCategory.TryGetValue(item.CategoryId, out list)) byCategory[item.CategoryId] = list = new List<BinderItem>();
                    list.Add(item);
                }
                else loose.Add(item);
            }
            foreach (var category in _project.SheetCategories)
            {
                if (category.IsSub) continue;
                var subs = new List<SheetCategory>();
                foreach (var sub in _project.SheetCategories) if (sub.ParentId == category.Id) subs.Add(sub);
                var count = byCategory.ContainsKey(category.Id) ? byCategory[category.Id].Count : 0;
                foreach (var sub in subs) if (byCategory.ContainsKey(sub.Id)) count += byCategory[sub.Id].Count;
                if (count == 0) continue;
                var box = CategoryBox(tree, category, 0);
                AddSheets(tree, byCategory, category.Id, 1, wanted, box);
                foreach (var sub in subs)
                {
                    if (!byCategory.ContainsKey(sub.Id)) continue;
                    var subBox = CategoryBox(tree, sub, 1);
                    AddSheets(tree, byCategory, sub.Id, 2, wanted, subBox);
                }
            }
            if (loose.Count > 0)
            {
                var box = new CheckBox { Content = Encyclopedia.UncategorizedTitle, FontWeight = FontWeight.SemiBold, Tag = "cat:" };
                tree.Children.Add(box);
                _categoryBoxes[""] = box;
                var children = new List<CheckBox>();
                foreach (var item in loose) children.Add(SheetBox(tree, item, 1, wanted.Contains(item.Id)));
                Bind(box, children);
            }
        }

        private CheckBox CategoryBox(StackPanel tree, SheetCategory category, int depth)
        {
            var box = new CheckBox { Content = category.Name, FontWeight = depth == 0 ? FontWeight.SemiBold : FontWeight.Normal, Margin = new Thickness(depth * 18, depth == 0 ? 4 : 0, 0, 0), Tag = "cat:" + category.Id };
            tree.Children.Add(box);
            _categoryBoxes[category.Id] = box;
            return box;
        }

        private void AddSheets(StackPanel tree, Dictionary<string, List<BinderItem>> byCategory, string categoryId, int depth, HashSet<string> wanted, CheckBox parent)
        {
            List<BinderItem> list;
            var children = new List<CheckBox>();
            if (byCategory.TryGetValue(categoryId, out list))
            {
                list.Sort(delegate(BinderItem a, BinderItem b) { return string.Compare(a.Title, b.Title, CultureInfo.GetCultureInfo("fr-FR"), CompareOptions.IgnoreCase); });
                foreach (var item in list) children.Add(SheetBox(tree, item, depth, wanted.Contains(item.Id)));
            }
            // La case d'une catégorie coche aussi ses sous-catégories (cases « cat: » déjà liées à leurs fiches).
            Bind(parent, children);
        }

        private CheckBox SheetBox(StackPanel tree, BinderItem item, int depth, bool checkedNow)
        {
            var box = new CheckBox { Content = string.IsNullOrEmpty(item.Title) ? "Sans titre" : item.Title, IsChecked = checkedNow, Margin = new Thickness(depth * 18, 0, 0, 0), Tag = "sheet:" + item.Id };
            tree.Children.Add(box);
            _sheetBoxes.Add(box);
            box.IsCheckedChanged += delegate { if (!_syncing) UpdateCount(); };
            return box;
        }

        /// <summary>La case d'un groupe reflète ses fiches (toutes, aucune, ou
        /// indéterminée) et les coche ou décoche toutes d'un coup.</summary>
        private void Bind(CheckBox parent, List<CheckBox> children)
        {
            if (children.Count == 0) { parent.IsEnabled = false; return; }
            parent.IsThreeState = true;
            Action reflect = delegate
            {
                var on = 0;
                foreach (var child in children) if (child.IsChecked == true) on++;
                _syncing = true;
                parent.IsChecked = on == 0 ? false : on == children.Count ? (bool?)true : null;
                _syncing = false;
            };
            reflect();
            foreach (var child in children) child.IsCheckedChanged += delegate { if (!_syncing) reflect(); };
            parent.IsCheckedChanged += delegate
            {
                if (_syncing) return;
                var state = parent.IsChecked;
                if (state == null) { _syncing = true; parent.IsChecked = true; _syncing = false; state = true; } // le cycle à trois états : indéterminé → tout
                _syncing = true;
                foreach (var child in children) child.IsChecked = state == true;
                _syncing = false;
                UpdateCount();
            };
            // Une catégorie d'ensemble coche aussi ses sous-catégories.
            var tag = parent.Tag as string ?? "";
            var categoryId = tag.StartsWith("cat:") ? tag.Substring(4) : null;
            if (categoryId != null && categoryId.Length > 0)
                parent.IsCheckedChanged += delegate
                {
                    if (_syncing || parent.IsChecked == null) return;
                    foreach (var pair in _categoryBoxes)
                    {
                        var sub = _project.FindSheetCategory(pair.Key);
                        if (sub != null && sub.ParentId == categoryId && pair.Value.IsEnabled) pair.Value.IsChecked = parent.IsChecked;
                    }
                };
        }

        private void SetAll(bool on)
        {
            _syncing = true;
            foreach (var box in _sheetBoxes) box.IsChecked = on;
            foreach (var pair in _categoryBoxes) if (pair.Value.IsEnabled) pair.Value.IsChecked = on;
            _syncing = false;
            UpdateCount();
        }

        private List<string> SelectedIds()
        {
            var ids = new List<string>();
            foreach (var box in _sheetBoxes) if (box.IsChecked == true) ids.Add(((string)box.Tag).Substring("sheet:".Length));
            return ids;
        }

        private void UpdateCount()
        {
            var n = SelectedIds().Count;
            _countLabel.Text = n + (n > 1 ? " fiches" : " fiche") + " sur " + _sheetBoxes.Count;
        }

        // ---------------------------------------------------------------- la couverture

        /// <summary>Une image du disque, passée par l'éditeur (rotation,
        /// recadrage, optimisation), ajoutée au projet : elle n'y restera à
        /// l'enregistrement que si l'export la retient (UsedImageIds).</summary>
        private async Task PickCover()
        {
            var path = await Ui.PickOpenFile(this, "La couverture de l'Encyclopédie", "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp");
            if (path == null || _project == null) return;
            try
            {
                var info = new System.IO.FileInfo(path);
                if (info.Length > 20 * 1024 * 1024) throw new InvalidOperationException("image de plus de 20 Mo — réduisez-la d'abord.");
                var bytes = System.IO.File.ReadAllBytes(path);
                var edited = await ImageEditDialog.Show(this, bytes, System.IO.Path.GetExtension(path));
                if (edited == null) return;
                SetCover(_project.AddImage(edited.Bytes, edited.Extension));
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Image refusée : " + error.Message, "Encyclopédie", MessageButtons.OK, MessageIcon.Warning);
            }
        }

        private void SetCover(string imageId)
        {
            var image = imageId == null || _project == null ? null : _project.FindImage(imageId);
            _coverImageId = image == null ? null : imageId;
            Bitmap bitmap = null;
            if (image != null && image.Bytes != null)
            {
                try { using (var stream = new System.IO.MemoryStream(image.Bytes)) bitmap = new Bitmap(stream); }
                catch (Exception) { bitmap = null; }
            }
            var previous = _coverPreview.Source as Bitmap;
            _coverPreview.Source = bitmap;
            if (previous != null && !ReferenceEquals(previous, bitmap)) previous.Dispose();
            _coverLabel.Text = _coverImageId == null ? "Aucune : l'accueil commence par le titre" : bitmap == null ? "Une image du projet" : "Image du projet, " + bitmap.PixelSize.Width + " × " + bitmap.PixelSize.Height + " px";
            _coverRemove.IsEnabled = _coverImageId != null;
        }

        // ---------------------------------------------------------------- aides

        private static TextBlock Caption(string text, double top = 0)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, top, 0, 6) };
        }

        private static TextBox Field(Panel panel, string label, string value, string tip)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var caption = new TextBlock { Text = label, Width = 90, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            var box = new TextBox { Text = value ?? "" };
            if (tip != null) ToolTip.SetTip(box, tip);
            row.Children.Add(box);
            panel.Children.Add(row);
            return box;
        }

        private static void AddRow(Grid grid, int row, string label, Control control)
        {
            var caption = new TextBlock { Text = label, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(caption, row);
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        private static CheckBox Check(Panel panel, string label, bool value)
        {
            var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 14, 2) };
            panel.Children.Add(box);
            return box;
        }

        /// <summary>Sonde (1/11) : le dialogue construit sans être montré —
        /// fiches cochées, fiches en tout, texte du bouton d'accent, état de
        /// la couverture.</summary>
        internal static string SnapshotForProbe(Window owner, Project project, EncyclopediaSelection preset, EncyclopediaOptions defaults)
        {
            var dialog = new EncyclopediaExportDialog(owner, project, preset, defaults ?? new EncyclopediaOptions());
            return dialog.SelectedIds().Count + "/" + dialog._sheetBoxes.Count + " " + dialog._accentText.Text + " " + (dialog._coverImageId == null ? "sans-couverture" : "couverture") + " " + (dialog._open.IsChecked == true ? "ouvre" : "n-ouvre-pas");
        }

        // ---------------------------------------------------------------- réponse

        public static async Task<Answer> Ask(Window owner, Project project, EncyclopediaSelection preset, EncyclopediaOptions defaults)
        {
            var dialog = new EncyclopediaExportDialog(owner, project, preset, defaults ?? new EncyclopediaOptions());
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return null;
            var selection = new EncyclopediaSelection { Scope = EncyclopediaSelection.ScopeSheets };
            selection.Ids.AddRange(dialog.SelectedIds());
            var sizes = new[] { 14, 15, 16, 17, 18, 20 };
            var options = new EncyclopediaOptions
            {
                Format = EncyclopediaOptions.FormatHtml,
                Title = (dialog._title.Text ?? "").Trim(),
                Subtitle = (dialog._subtitle.Text ?? "").Trim(),
                Author = (dialog._author.Text ?? "").Trim(),
                BodyFont = dialog._bodyFont.SelectedFontName ?? "",
                HeadingFont = dialog._headingFont.SelectedFontName ?? "",
                BaseSize = sizes[Math.Max(0, Math.Min(sizes.Length - 1, dialog._size.SelectedIndex))],
                Accent = EncyclopediaOptions.SafeColor(dialog._accent, "#5B67D8"),
                Dark = dialog._dark.IsChecked == true,
                PortraitSide = dialog._portrait.SelectedIndex == 1 ? "left" : dialog._portrait.SelectedIndex == 2 ? "top" : "right",
                ShowInfobox = dialog._infobox.IsChecked == true,
                ShowRelations = dialog._relations.IsChecked == true,
                ShowPresence = dialog._presence.IsChecked == true,
                ShowRadar = dialog._radar.IsChecked == true,
                ShowTags = dialog._tags.IsChecked == true,
                ShowDates = dialog._dates.IsChecked == true,
                IncludeLexicon = dialog._lexicon.IsChecked == true,
                LexiconUndefined = dialog._lexiconUndefined.IsChecked == true,
                LexiconExcludeNames = dialog._lexiconNoNames.IsChecked == true,
                LexiconAllProjects = dialog._lexiconGlobal.IsChecked == true,
                IncludeEmptySheets = dialog._emptySheets.IsChecked == true,
                CoverImageId = dialog._coverImageId,
                OpenAfter = dialog._open.IsChecked == true
            };
            // Les fiches cochées sont retenues avec les options (vide si tout est coché : « toutes », même les fiches à venir).
            if (selection.Ids.Count < dialog._sheetBoxes.Count) options.SheetIds.AddRange(selection.Ids);
            return new Answer { Selection = selection, Options = options, OpenAfter = options.OpenAfter };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UniversSale.History;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>La bibliothèque de fiches (batch 31) — la vue de la catégorie
    /// « Fiches » de la Pile : une RANGÉE par catégorie de fiches, séparées
    /// par des filets ; dans chaque rangée, les cartes (photo en haut, nom en
    /// dessous, emplacement réservé sans photo). Depuis chaque rangée : créer
    /// une fiche, modifier la catégorie (nom, modèle de base — une fiche dont
    /// le modèle diffère porte une puce). Une barre de recherche filtre les
    /// fiches par nom, toutes catégories confondues.</summary>
    public class SheetLibraryView : DockPanel
    {
        private Project _project;
        private HistoryManager _history;
        private BinderItem _scope; // un dossier de Fiches (14/09), null = la racine
        private readonly TextBox _searchBox;
        private readonly StackPanel _rows;
        private readonly ScrollViewer _scroll;
        private readonly Button _back;
        private readonly TextBlock _scopeLabel;

        /// <summary>Le menu contextuel d'une tuile : celui de la Pile pour le
        /// même item (BinderView.BuildContextMenu), posé par la coquille.</summary>
        public Func<BinderItem, ContextMenu> MenuProvider;

        public event Action<BinderItem> Navigate; // ouvrir une fiche
        public event Action Changed;              // structure/projet modifiés
        public event Action<string> AchievementEvent; // succès à événement (12/09)

        private bool HasAnySheet()
        {
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Sheet && item.RootCategory() != null
                    && item.RootCategory().CategoryKey != Project.KeyTrash) return true;
            return false;
        }

        public SheetLibraryView()
        {
            Background = Chrome.WindowBg;
            Focusable = true;

            // — La rangée du haut, alignée sur celle d'Écrits (14/09) : pas de
            // barre, les boutons à gauche (« Nouvelle fiche » en principal,
            // puis catégorie et modèles), la recherche contre le bord droit.
            var toolbar = new DockPanel { Margin = new Thickness(24, 10, 24, 0) };
            SetDock(toolbar, Dock.Top);
            _searchBox = new TextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(6, 3, 4, 3),
                MinWidth = 200,
                ToolTip = "Rechercher une fiche par nom, toutes catégories confondues"
            };
            _searchBox.TextChanged += delegate
            {
                RebuildRows();
                // « Crétin des alpes » (12/09) : chercher une fiche sans en avoir.
                if (_searchBox.Text.Trim().Length > 0 && _project != null && !HasAnySheet())
                {
                    var handler = AchievementEvent;
                    if (handler != null) handler(Achievements.Cretin);
                }
            };
            var search = SearchField(_searchBox);
            DockPanel.SetDock(search, Dock.Right);
            toolbar.Children.Add(search);

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            _back = Buttons.Icon("arrow-left-bold", "Revenir à la bibliothèque", Buttons.Bar, Buttons.Look.Calm);
            _back.Margin = new Thickness(0, 0, 6, 0);
            _back.Visibility = Visibility.Collapsed;
            _back.Click += delegate
            {
                var handler = Navigate;
                if (handler != null && _project != null)
                    handler(_scope != null && _scope.Parent != null ? _scope.Parent : _project.Category(Project.KeySheets));
            };
            left.Children.Add(_back);
            _scopeLabel = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
                Visibility = Visibility.Collapsed,
                MaxWidth = 320,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            left.Children.Add(_scopeLabel);
            // « Nouvelle fiche » : UN bouton principal (pack du 12/09/2026) —
            // la catégorie se choisit dans le dialogue avec le nom.
            var newSheet = Buttons.IconText("plus-bold", "Nouvelle fiche",
                "Créer une fiche — le nom et la catégorie se choisissent ensemble",
                Buttons.Bar, Buttons.Look.Primary);
            newSheet.Click += delegate { NewSheet(); };
            left.Children.Add(newSheet);
            var newCategory = Buttons.IconText("plus-bold", "Nouvelle catégorie",
                "Une catégorie de fiches, avec son modèle", Buttons.Bar, Buttons.Look.Outline);
            newCategory.Margin = new Thickness(8, 0, 0, 0);
            newCategory.Click += delegate { NewCategory(); };
            left.Children.Add(newCategory);
            var templates = Buttons.IconText("pencil-simple-line", "Éditeur de modèles",
                "Sections, champs, natures et radar des modèles de fiches", Buttons.Bar, Buttons.Look.Outline);
            templates.Margin = new Thickness(8, 0, 0, 0);
            templates.Click += delegate { EditTemplates(); };
            left.Children.Add(templates);
            toolbar.Children.Add(left);
            Children.Add(toolbar);

            _rows = new StackPanel { Margin = new Thickness(16, 12, 16, 24) };
            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _rows
            };
            Children.Add(_scroll);
        }

        /// <summary>La bibliothèque de la racine Fiches (scope null) ou d'un
        /// DOSSIER de Fiches (14/09) : mêmes tuiles, mêmes catégories, les
        /// sous-dossiers en rangée « Dossiers ».</summary>
        public void Load(Project project, HistoryManager history, BinderItem scope)
        {
            _project = project;
            _history = history;
            _scope = scope != null && scope.Kind == ItemKind.Folder ? scope : null;
            _back.Visibility = _scope != null ? Visibility.Visible : Visibility.Collapsed;
            _scopeLabel.Visibility = _back.Visibility;
            _scopeLabel.Text = _scope != null ? _scope.Title : "";
            RebuildRows();
        }

        public bool ShowsFolder(BinderItem folder) { return folder != null && _scope == folder; }

        /// <summary>Le champ de recherche « contre le rebord » (14/09) : un
        /// cadre, la zone de texte, la loupe à droite — partagé avec le
        /// Dictionnaire.</summary>
        public static Border SearchField(TextBox box)
        {
            var row = new DockPanel();
            var glass = Icons.Make("magnifying-glass-bold", 12, Chrome.SoftText) as FrameworkElement;
            if (glass != null)
            {
                glass.VerticalAlignment = VerticalAlignment.Center;
                glass.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(glass, Dock.Right);
                row.Children.Add(glass);
            }
            row.Children.Add(box);
            return new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Width = 260,
                VerticalAlignment = VerticalAlignment.Center,
                Child = row
            };
        }

        /// <summary>Les fiches de la portée : toutes (racine), ou celles du
        /// dossier et de ses sous-dossiers.</summary>
        private IEnumerable<BinderItem> Source()
        {
            if (_scope == null) return _project.AllItems();
            var list = new List<BinderItem>();
            Collect(_scope, list);
            return list;
        }

        private static void Collect(BinderItem parent, List<BinderItem> list)
        {
            foreach (var child in parent.Children)
            {
                list.Add(child);
                Collect(child, list);
            }
        }

        public void Refresh() { RebuildRows(); }

        // ---------------------------------------------------------- rangées

        private void RebuildRows()
        {
            _rows.Children.Clear();
            if (_project == null) return;
            var needle = Correction.FrenchTokenizer.Fold(_searchBox.Text.Trim());

            // Les fiches par catégorie (une passe), plus les sans-catégorie.
            var byCategory = new Dictionary<string, List<BinderItem>>();
            var uncategorized = new List<BinderItem>();
            foreach (var item in Source())
            {
                if (item.Kind != ItemKind.Sheet) continue;
                if (item.RootCategory() != null
                    && item.RootCategory().CategoryKey == Project.KeyTrash) continue;
                if (needle.Length > 0 && !Correction.FrenchTokenizer
                    .Fold(item.Title).Contains(needle)) continue;
                var category = _project.SheetCategoryOf(item);
                if (category == null) { uncategorized.Add(item); continue; }
                List<BinderItem> list;
                if (!byCategory.TryGetValue(category.Id, out list))
                    byCategory[category.Id] = list = new List<BinderItem>();
                list.Add(item);
            }

            // Dans la bibliothèque, l'ordre est TOUJOURS alphabétique, que la
            // fiche vive à la racine ou dans un dossier de la Pile (pack du
            // 12/09/2026) — l'ordre de la Pile reste le sien.
            foreach (var list in byCategory.Values) SortByTitle(list);
            SortByTitle(uncategorized);

            var searching = needle.Length > 0;
            var first = true;
            // Les DOSSIERS (14/09) : leur propre rangée, en tête — ceux de la
            // racine Fiches, ou les sous-dossiers du dossier ouvert.
            var parent = _scope ?? _project.Category(Project.KeySheets);
            var folders = new List<BinderItem>();
            if (parent != null && !searching)
                foreach (var child in parent.Children)
                    if (child.Kind == ItemKind.Folder) folders.Add(child);
            if (folders.Count > 0)
            {
                AddHeader("Dossiers", null, -1);
                var wrap = new WrapPanel();
                foreach (var folder in folders) wrap.Children.Add(BuildFolderCard(folder));
                _rows.Children.Add(wrap);
                first = false;
            }
            foreach (var category in _project.SheetCategories)
            {
                List<BinderItem> sheets;
                byCategory.TryGetValue(category.Id, out sheets);
                // En recherche, une catégorie muette disparaît ; au repos,
                // toutes les rangées s'affichent (même vides : on y crée).
                if (searching && (sheets == null || sheets.Count == 0)) continue;
                AddCategoryRow(category, sheets ?? new List<BinderItem>(), first);
                first = false;
            }
            if (uncategorized.Count > 0)
            {
                AddDivider(first);
                AddHeader("Sans catégorie", null, uncategorized.Count);
                AddCards(uncategorized, null);
            }
            if (searching && _rows.Children.Count == 0)
                _rows.Children.Add(new TextBlock
                {
                    Text = "Aucune fiche ne porte ce nom.",
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(4, 16, 0, 0)
                });
        }

        /// <summary>Tri alphabétique à la française (accents et casse
        /// ignorés, ordre stable pour les homonymes).</summary>
        public static void SortByTitle(List<BinderItem> sheets)
        {
            var indexed = new List<KeyValuePair<int, BinderItem>>();
            for (var i = 0; i < sheets.Count; i++)
                indexed.Add(new KeyValuePair<int, BinderItem>(i, sheets[i]));
            indexed.Sort(delegate (KeyValuePair<int, BinderItem> a, KeyValuePair<int, BinderItem> b)
            {
                var byName = string.Compare(a.Value.Title ?? "", b.Value.Title ?? "",
                    System.Globalization.CultureInfo.GetCultureInfo("fr-FR"),
                    System.Globalization.CompareOptions.IgnoreCase);
                return byName != 0 ? byName : a.Key.CompareTo(b.Key);
            });
            sheets.Clear();
            foreach (var pair in indexed) sheets.Add(pair.Value);
        }

        private void AddCategoryRow(SheetCategory category,
            List<BinderItem> sheets, bool first)
        {
            AddDivider(first);
            AddHeader(category.Name, category, sheets.Count);
            AddCards(sheets, category);
        }

        /// <summary>Le filet séparateur entre catégories.</summary>
        private void AddDivider(bool first)
        {
            if (first) return;
            _rows.Children.Add(new Border
            {
                Height = 1,
                Background = Chrome.Border,
                Margin = new Thickness(0, 16, 0, 0)
            });
        }

        /// <summary>L'éditeur de modèles (sections, champs, natures) ; les
        /// modèles édités remplacent ceux du projet, la bibliothèque se redessine.</summary>
        private void EditTemplates()
        {
            var edited = TemplatesDialog.Show(Window.GetWindow(this), _project.Templates);
            if (edited == null) return;
            _project.Templates = edited;
            NotifyChanged();
            RebuildRows();
        }

        private void AddHeader(string name, SheetCategory category, int count)
        {
            var header = new DockPanel { Margin = new Thickness(0, 14, 0, 8) };

            if (category != null)
            {
                // Le menu de la catégorie : un bouton à trois points, sans
                // texte (b42 bis). Le « + Nouvelle fiche » par rangée est parti
                // dans la barre du haut (pack du 12/09/2026).
                var categoryRef = category;
                var edit = Buttons.Icon("dots-three-vertical-bold", "Modifier la catégorie…", Buttons.Compact, Buttons.Look.Calm);
                edit.Click += delegate { ShowCategoryMenu(edit, categoryRef); };
                DockPanel.SetDock(edit, Dock.Right);
                header.Children.Add(edit);
            }

            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center
            });
            if (count >= 0) title.Children.Add(new TextBlock
            {
                Text = count == 0 ? "aucune fiche"
                    : count == 1 ? "1 fiche" : count + " fiches",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (category != null)
            {
                var template = _project.FindTemplate(category.TemplateId);
                if (template != null)
                    title.Children.Add(new TextBlock
                    {
                        Text = "· modèle " + template.Name,
                        FontSize = 11,
                        Foreground = Chrome.SoftText,
                        Margin = new Thickness(10, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
            }
            header.Children.Add(title);
            _rows.Children.Add(header);
        }

        // ------------------------------------------------------------ cartes

        private void AddCards(List<BinderItem> sheets, SheetCategory category)
        {
            var wrap = new WrapPanel();
            foreach (var sheet in sheets)
                wrap.Children.Add(BuildCard(sheet, category));
            if (sheets.Count == 0)
                wrap.Children.Add(new TextBlock
                {
                    Text = "Rien ici — « + Nouvelle fiche » pour commencer.",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Margin = new Thickness(4, 2, 0, 2)
                });
            _rows.Children.Add(wrap);
        }

        /// <summary>Une tuile de dossier (14/09) : l'icône, le nom, le compte
        /// de fiches qu'il contient ; un clic l'ouvre (même bibliothèque,
        /// à sa portée), le clic droit offre le menu de la Pile.</summary>
        private UIElement BuildFolderCard(BinderItem folder)
        {
            var count = 0;
            var inside = new List<BinderItem>();
            Collect(folder, inside);
            foreach (var item in inside) if (item.Kind == ItemKind.Sheet) count++;
            var layout = new StackPanel { Width = 132 };
            var icon = Icons.Make("folder-bold", 34, Chrome.Accent) as FrameworkElement;
            if (icon != null)
            {
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.Margin = new Thickness(0, 22, 0, 6);
                layout.Children.Add(icon);
            }
            layout.Children.Add(new TextBlock
            {
                Text = folder.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Chrome.Ink,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                Margin = new Thickness(6, 0, 6, 2)
            });
            layout.Children.Add(new TextBlock
            {
                Text = count == 0 ? "vide" : count == 1 ? "1 fiche" : count + " fiches",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(6, 0, 6, 10)
            });
            var card = new Border
            {
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = Cursors.Hand,
                Child = layout
            };
            var folderRef = folder;
            card.MouseLeftButtonUp += delegate { var handler = Navigate; if (handler != null) handler(folderRef); };
            card.MouseRightButtonUp += delegate
            {
                var menu = MenuProvider == null ? null : MenuProvider(folderRef);
                if (menu == null) return;
                menu.PlacementTarget = card;
                menu.IsOpen = true;
            };
            card.MouseEnter += delegate { card.BorderBrush = Chrome.Accent; };
            card.MouseLeave += delegate { card.BorderBrush = Chrome.Border; };
            CardLift.Attach(card);
            return card;
        }

        /// <summary>Une carte-fiche : PHOTO EN HAUT (ou emplacement réservé),
        /// NOM EN DESSOUS (batch 31). La puce en coin signale une fiche dont
        /// le modèle diffère du modèle de base de sa catégorie.</summary>
        private UIElement BuildCard(BinderItem sheet, SheetCategory category)
        {
            var layout = new StackPanel { Width = 132 };

            // — la photo, ou son emplacement réservé. Batch 36 : l'image est
            // CENTRÉE dans son cadre (l'équivalent de background-position:
            // center center — un Image UniformToFill s'alignait en haut à
            // gauche), et le cadre est découpé selon ses coins arrondis :
            // ClipToBounds ne coupe qu'au rectangle, l'image mordait les
            // arrondis de la carte.
            UIElement pictureContent = null;
            var image = _project.FindImage(sheet.ImageId);
            if (image != null)
            {
                var source = MediaView.TryImage(image.Bytes, 260);
                if (source != null)
                    pictureContent = new Rectangle
                    {
                        Fill = new ImageBrush(source)
                        {
                            Stretch = Stretch.UniformToFill,
                            AlignmentX = AlignmentX.Center,
                            AlignmentY = AlignmentY.Center
                        }
                    };
            }
            if (pictureContent == null)
                pictureContent = new TextBlock
                {
                    Text = "🖼",
                    FontSize = 30,
                    Foreground = Chrome.SoftText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            var picture = new Border
            {
                Height = 108,
                Background = Chrome.BarBgLight,
                CornerRadius = new CornerRadius(3, 3, 0, 0),
                Child = pictureContent
            };
            picture.SizeChanged += delegate { picture.Clip = TopRoundedClip(picture.ActualWidth, picture.ActualHeight, 3); };

            var grid = new Grid();
            grid.Children.Add(picture);
            // — la puce « modèle différent du modèle de base »
            if (category != null && sheet.TemplateId != category.TemplateId)
                grid.Children.Add(new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = Chrome.AccentSoft, // pastille calme (b40)
                    BorderBrush = Chrome.PaperBg,
                    BorderThickness = new Thickness(1.5),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 6, 0),
                    ToolTip = "Cette fiche n'utilise pas le modèle de base "
                        + "de la catégorie"
                });
            layout.Children.Add(grid);

            layout.Children.Add(new TextBlock
            {
                Text = sheet.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Chrome.Ink,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                Margin = new Thickness(6, 5, 6, 7)
            });

            var card = new Border
            {
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 10, 10),
                Cursor = Cursors.Hand,
                Child = layout
            };
            var sheetRef = sheet;
            card.MouseLeftButtonUp += delegate
            {
                var handler = Navigate;
                if (handler != null) handler(sheetRef);
            };
            card.MouseRightButtonUp += delegate { ShowCardMenu(card, sheetRef); };
            card.MouseEnter += delegate { card.BorderBrush = Chrome.Accent; };
            card.MouseLeave += delegate { card.BorderBrush = Chrome.Border; };
            CardLift.Attach(card); // soulèvement au survol (b35)
            return card;
        }

        /// <summary>Un clip aux coins HAUTS arrondis : le rectangle déborde
        /// du bas de « radius » pour y garder des coins droits (le bas de la
        /// photo touche le nom, pas le bord de la carte).</summary>
        public static Geometry TopRoundedClip(double width, double height, double radius)
        {
            if (width <= 0 || height <= 0) return null;
            var geometry = new RectangleGeometry(new Rect(0, 0, width, height + radius), radius, radius);
            geometry.Freeze();
            return geometry;
        }

        /// <summary>Le menu d'une tuile (14/09) : Ouvrir, Changer de catégorie,
        /// puis TOUT ce que la Pile offre sur la même fiche (épingler à
        /// l'accueil, épingler au rail, renommer, icône, supprimer…).</summary>
        private void ShowCardMenu(UIElement anchor, BinderItem sheet)
        {
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Ouvrir" };
            open.Click += delegate
            {
                var handler = Navigate;
                if (handler != null) handler(sheet);
            };
            menu.Items.Add(open);

            var move = new MenuItem { Header = "Changer de catégorie" };
            foreach (var category in _project.SheetCategories)
            {
                var entry = new MenuItem
                {
                    Header = category.Name,
                    IsChecked = _project.SheetCategoryOf(sheet) == category
                };
                var categoryRef = category;
                entry.Click += delegate
                {
                    sheet.CategoryId = categoryRef.Id;
                    NotifyChanged();
                    RebuildRows();
                };
                move.Items.Add(entry);
            }
            menu.Items.Add(move);
            var provided = MenuProvider == null ? null : MenuProvider(sheet);
            if (provided != null && provided.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
                var items = new List<object>();
                foreach (var entry in provided.Items) items.Add(entry);
                provided.Items.Clear(); // un MenuItem n'a qu'un parent
                foreach (var entry in items)
                    if (!(entry is Separator && menu.Items[menu.Items.Count - 1] is Separator))
                        menu.Items.Add(entry);
            }
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        // ------------------------------------------------------- catégories

        /// <summary>Nom et catégorie dans le même dialogue (le même que la
        /// Pile) ; la fiche naît dans la racine Fiches avec le modèle de base
        /// de sa catégorie, puis s'ouvre.</summary>
        private void NewSheet()
        {
            string title, categoryId;
            if (!NewSheetDialog.Ask(Window.GetWindow(this), _project, out title, out categoryId))
                return;
            var category = _project.FindSheetCategory(categoryId);
            var item = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = title,
                TemplateId = category != null ? category.TemplateId : null,
                CategoryId = category != null ? category.Id : null
            };
            _history.Run(new AddItemAction(
                _scope ?? _project.Category(Project.KeySheets), item, -1));
            NotifyChanged();
            RebuildRows();
            var handler = Navigate; // la fiche neuve s'ouvre, prête à remplir
            if (handler != null) handler(item);
        }

        private void ShowCategoryMenu(UIElement anchor, SheetCategory category)
        {
            var menu = new ContextMenu();

            var rename = new MenuItem { Header = "Renommer…" };
            rename.Click += delegate
            {
                var name = InputDialog.Ask(Window.GetWindow(this),
                    "Renommer la catégorie", "Nom de la catégorie :", category.Name);
                if (name == null || name.Trim().Length == 0) return;
                category.Name = name.Trim();
                NotifyChanged();
                RebuildRows();
            };
            menu.Items.Add(rename);

            var baseTemplate = new MenuItem
            {
                Header = "Modèle de base",
                ToolTip = "Le modèle des NOUVELLES fiches de la catégorie — les "
                    + "fiches existantes gardent le leur (puce sur leur carte)"
            };
            foreach (var template in _project.Templates)
            {
                var entry = new MenuItem
                {
                    Header = template.Name,
                    IsChecked = template.Id == category.TemplateId
                };
                var templateRef = template;
                entry.Click += delegate
                {
                    category.TemplateId = templateRef.Id;
                    NotifyChanged();
                    RebuildRows();
                };
                baseTemplate.Items.Add(entry);
            }
            menu.Items.Add(baseTemplate);

            var editTemplates = new MenuItem
            {
                Header = "Modifier les modèles…",
                ToolTip = "Champs, groupes et types — l'éditeur de modèles"
            };
            editTemplates.Click += delegate { EditTemplates(); };
            menu.Items.Add(editTemplates);

            menu.Items.Add(new Separator());
            var remove = new MenuItem { Header = "Supprimer la catégorie" };
            remove.Click += delegate { DeleteCategory(category); };
            menu.Items.Add(remove);

            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        private void NewCategory()
        {
            var name = InputDialog.Ask(Window.GetWindow(this),
                "Nouvelle catégorie", "Nom de la catégorie (ex. « Religion », "
                + "« Faction ») :", "Catégorie");
            if (name == null || name.Trim().Length == 0) return;
            // La catégorie naît avec son propre modèle, sobre — à détailler
            // dans « Modifier les modèles… ».
            var template = new SheetTemplate { Name = name.Trim() };
            template.Fields.Add(new SheetField
            {
                Name = "Description",
                Kind = "multiline"
            });
            _project.Templates.Add(template);
            _project.SheetCategories.Add(new SheetCategory
            {
                Name = name.Trim(),
                TemplateId = template.Id
            });
            NotifyChanged();
            RebuildRows();
        }

        private void DeleteCategory(SheetCategory category)
        {
            var used = 0;
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Sheet
                    && _project.SheetCategoryOf(item) == category) used++;
            if (used > 0)
            {
                MessageDialog.Show(Window.GetWindow(this),
                    "La catégorie « " + category.Name + " » contient " + used
                    + (used == 1 ? " fiche" : " fiches") + ".\nDéplacez-les "
                    + "d'abord (clic droit sur une carte → Changer de catégorie).",
                    "Marabook", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var answer = MessageDialog.Show(Window.GetWindow(this),
                "Supprimer la catégorie « " + category.Name + " » ?\n"
                + "Son modèle reste dans l'éditeur de modèles.",
                "Marabook", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            _project.SheetCategories.Remove(category);
            NotifyChanged();
            RebuildRows();
        }

        private void NotifyChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

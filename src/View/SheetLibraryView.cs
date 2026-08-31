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
        private readonly TextBox _searchBox;
        private readonly StackPanel _rows;
        private readonly ScrollViewer _scroll;

        public event Action<BinderItem> Navigate; // ouvrir une fiche
        public event Action Changed;              // structure/projet modifiés

        public SheetLibraryView()
        {
            Background = Chrome.WindowBg;
            Focusable = true;

            // — Barre du haut : recherche + nouvelle catégorie.
            var bar = new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8)
            };
            SetDock(bar, Dock.Top);
            var barRow = new DockPanel();

            var newCategory = new Button
            {
                Content = Icons.Label("plus-bold", "Nouvelle catégorie", 11, Chrome.Ink),
                Padding = new Thickness(10, 3, 10, 3)
            };
            newCategory.Click += delegate { NewCategory(); };
            DockPanel.SetDock(newCategory, Dock.Right);
            barRow.Children.Add(newCategory);
            // L'éditeur de modèles, tout en haut, à côté de « Nouvelle
            // catégorie » (b42 bis) — le même que « Modifier les modèles… ».
            var templates = new Button
            {
                Content = Icons.Label("pencil-simple-line", "Éditeur de modèles", 11, Chrome.Ink),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Sections, champs et natures des modèles de fiches"
            };
            templates.Click += delegate { EditTemplates(); };
            DockPanel.SetDock(templates, Dock.Right);
            barRow.Children.Add(templates);

            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
            var glass = new TextBlock
            {
                Text = "🔍",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            DockPanel.SetDock(glass, Dock.Left);
            searchRow.Children.Add(glass);
            _searchBox = new TextBox
            {
                MaxWidth = 340,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 220,
                Padding = new Thickness(6, 3, 6, 3),
                ToolTip = "Rechercher une fiche par nom, toutes catégories confondues"
            };
            _searchBox.TextChanged += delegate { RebuildRows(); };
            searchRow.Children.Add(_searchBox);
            barRow.Children.Add(searchRow);
            bar.Child = barRow;
            Children.Add(bar);

            _rows = new StackPanel { Margin = new Thickness(16, 12, 16, 24) };
            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _rows
            };
            Children.Add(_scroll);
        }

        public void Load(Project project, HistoryManager history)
        {
            _project = project;
            _history = history;
            RebuildRows();
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
            foreach (var item in _project.AllItems())
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

            var searching = needle.Length > 0;
            var first = true;
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
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                var newSheet = new Button
                {
                    Content = Icons.Label("plus-bold", "Nouvelle fiche", 10, Chrome.Ink),
                    Padding = new Thickness(8, 2, 8, 2),
                    ToolTip = "Créer une fiche « " + name + " » (modèle de base "
                        + "de la catégorie)"
                };
                var categoryRef = category;
                newSheet.Click += delegate { NewSheet(categoryRef); };
                buttons.Children.Add(newSheet);

                // Le menu de la catégorie : un bouton à trois points, sans texte (b42 bis).
                var edit = Buttons.Icon("dots-three-vertical-bold", "Modifier la catégorie…", Buttons.Compact, Buttons.Look.Calm);
                edit.Margin = new Thickness(6, 0, 0, 0);
                edit.Click += delegate { ShowCategoryMenu(edit, categoryRef); };
                buttons.Children.Add(edit);
                DockPanel.SetDock(buttons, Dock.Right);
                header.Children.Add(buttons);
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
            title.Children.Add(new TextBlock
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
            menu.PlacementTarget = anchor;
            menu.IsOpen = true;
        }

        // ------------------------------------------------------- catégories

        private void NewSheet(SheetCategory category)
        {
            var title = InputDialog.Ask(Window.GetWindow(this),
                "Nouvelle fiche " + category.Name, "Nom de la fiche :",
                "Nouvelle fiche");
            if (title == null) return;
            var item = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = title,
                TemplateId = category.TemplateId,
                CategoryId = category.Id
            };
            _history.Run(new AddItemAction(
                _project.Category(Project.KeySheets), item, -1));
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

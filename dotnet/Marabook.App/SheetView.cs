using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    /// <summary>La fiche (refonte batch 34, onglets batch 36) — des « papers »
    /// (cadres arrondis à ombre légère) répartis sur deux onglets : « Général »
    /// = trois colonnes, l'image et les informations, l'apparence et les
    /// relations (nature choisie dans un sélecteur, reflétée sur la fiche
    /// liée), la troisième vide pour l'instant ; « Texte libre » = l'éditeur
    /// markdown pleine largeur, dans la police du wiki, avec la barre de
    /// formatage de Markdown We Go. En bandeau : retour, nom, mode wiki,
    /// généalogie (le paper flottant GenealogyWindow).</summary>
    public class SheetView : DockPanel
    {
        private const double BodyFontSize = 14.5; // Georgia, comme l'aperçu wiki

        private readonly TextBlock _titleLabel, _categoryLabel;
        private readonly ToggleButton _previewToggle;
        private readonly Grid _body;
        private readonly TabControl _tabs;         // Général | Texte libre (b36) | Radar (b47 bis, si le modèle l'active)
        private readonly TabItem _radarTab;
        private readonly StackPanel _radarHost;
        // Les modules (DLC, 22/09) : un bouton « Créer une fiche X » dans le
        // bandeau tant que la fiche n'en a pas, un onglet par fiche créée.
        private readonly StackPanel _moduleButtons;
        private readonly List<TabItem> _moduleTabs = new List<TabItem>();
        private Canvas _radarCanvas;
        private Border _dictDot;                   // indicateur de dictionnaire (12/09)
        private TextBlock _dictState;
        private Button _dictAdd;
        private string _focusFieldId;              // le champ libre qui vient d'être ajouté — lui seul prend le focus
        private readonly Button _genealogyButton;
        private GenealogyWindow _genealogy;        // le paper flottant (b36)
        private readonly ScrollViewer _preview;
        private readonly StackPanel _relationsPanel;
        // Les papers de l'onglet Général (batch 42) : l'image, une section
        // par nom (« » = Informations), les relations si le modèle les veut.
        private readonly Border _imagePaper, _relationsPaper;
        // Troisième colonne (batch 47) : où la fiche apparaît, et ses étapes —
        // deux SECTIONS EXTRAS (21/09) que le modèle active ; le suivi se
        // filtre par écrit, groupe ou livre depuis l'en-tête de son paper.
        private readonly Border _presencePaper, _evolutionPaper;
        private readonly StackPanel _presencePanel, _evolutionPanel;
        private readonly ComboBox _presenceFilter;
        private string _presenceFilterId;   // null = toute l'amplitude ; sinon l'id d'un écrit, groupe ou livre
        private bool _syncingFilter, _showTracking, _showEvolution;
        private readonly Grid _papersGrid;
        private readonly StackPanel[] _columns;
        private readonly Dictionary<string, StackPanel> _sectionPanels = new Dictionary<string, StackPanel>();
        private readonly List<Border> _sectionPapers = new List<Border>();
        private bool _showRelations;
        private int _layoutMode = -1;
        private readonly Image _portrait;
        private readonly StackPanel _portraitPlaceholder;
        private readonly ContentControl _portraitIcon; // le symbole de la catégorie (29/09)
        private readonly Button _removePortrait;
        private readonly TextBox _bodyBox;
        // La vue miroir du texte libre (14/09) : le rendu wiki à droite de
        // l'éditeur, rafraîchi un peu après la frappe ; le choix vaut pour
        // la session, fiche après fiche.
        private readonly Grid _bodySplit;
        private readonly ScrollViewer _mirror;
        private readonly ToggleButton _editToggle, _mirrorToggle;
        private readonly DispatcherTimer _mirrorTimer;
        private static bool _mirrorMode;
        private readonly DockPanel _findBar;
        private readonly TextBox _findBox;

        private BinderItem _item;
        private SheetTemplate _template;
        private Project _project;
        private StyleSheet _styles = StyleSheet.CreateDefault();
        private bool _loading;
        private double _zoom = 1.0;

        public event Action Edited;
        public event Action<string> LinkClicked;
        public event Action<int> ZoomStepRequested;
        public event Action BackRequested;                 // ← retour (b34)
        public event Action<BinderItem> NavigateRequested; // ouvrir une fiche liée (b34)
        public event Action RenameRequested;               // le crayon du bandeau (b43)
        public event Action LexiconChanged;                // le nom ajouté au dictionnaire du projet (12/09)

        public SheetView()
        {
            Background = Chrome.WindowBg;

            // ================================================== le bandeau
            var banner = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8)
            };
            SetDock(banner, Dock.Top);
            var bannerRow = new DockPanel();
            var back = new Button
            {
                Content = Icons.Label("arrow-left-bold", "Retour", 12, Chrome.Ink),
                Padding = new Thickness(10, 4, 12, 4),
                [ToolTip.TipProperty] = "Revenir au tableau de la fiche",
                VerticalAlignment = VerticalAlignment.Center
            };
            back.Click += delegate { var h = BackRequested; if (h != null) h(); };
            DockPanel.SetDock(back, Dock.Left);
            bannerRow.Children.Add(back);

            var rightTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _moduleButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            rightTools.Children.Add(_moduleButtons);
            _previewToggle = new ToggleButton
            {
                Classes = { Marabook.App.Theme.Owned },
                Content = "📖  Mode wiki",
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(12, 4, 12, 4),
                [ToolTip.TipProperty] = "Voir la fiche en lecture, comme une page de wiki"
            };
            _previewToggle.Checked += delegate { ShowPreview(); };
            _previewToggle.Unchecked += delegate { HidePreview(); };
            rightTools.Children.Add(_previewToggle);
            _genealogyButton = new Button
            {
                Content = Icons.Label("tree-bold", "Généalogie", 13, Chrome.Ink),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(8, 0, 0, 0),
                [ToolTip.TipProperty] = "L'arbre généalogique de la fiche, d'après ses relations (section flottante)"
            };
            _genealogyButton.Click += delegate { ShowGenealogy(); };
            rightTools.Children.Add(_genealogyButton);
            DockPanel.SetDock(rightTools, Dock.Right);
            bannerRow.Children.Add(rightTools);

            var titles = new StackPanel { Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            _titleLabel = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            // Le crayon pour renommer la fiche sans passer par la Pile (b43).
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(_titleLabel);
            var renameButton = Buttons.Icon("pencil-simple-line", "Renommer la fiche", Buttons.Compact, Buttons.Look.Calm);
            renameButton.Margin = new Thickness(6, 0, 0, 0);
            renameButton.Click += delegate { var h = RenameRequested; if (h != null) h(); };
            titleRow.Children.Add(renameButton);
            _categoryLabel = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11 };
            titles.Children.Add(titleRow);
            titles.Children.Add(_categoryLabel);
            bannerRow.Children.Add(titles);
            banner.Child = bannerRow;
            Children.Add(banner);

            // ================================================== le corps
            // Batch 36 : deux onglets. « Général » = tout ce qui n'est PAS
            // l'éditeur — l'image et les informations (1re colonne), l'apparence
            // et les relations (2e), une 3e colonne vide, réservée ; « Texte
            // libre » = l'éditeur markdown pleine largeur.
            _tabs = new TabControl { Margin = new Thickness(8, 4, 8, 0) };

            // Chaque colonne est une pile indépendante (un Grid à rangées
            // partagées laissait un vide sous un paper court quand son voisin
            // était haut).
            var papers = new Grid { Margin = new Thickness(2, 4, 2, 10) };
            var columns = new StackPanel[3];
            for (var i = 0; i < 3; i++)
            {
                papers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                columns[i] = new StackPanel();
                Grid.SetColumn(columns[i], i);
                papers.Children.Add(columns[i]);
            }

            // Image + options.
            _portrait = new Image { MaxHeight = 260, Stretch = Stretch.Uniform, IsVisible = false };
            // Sans image (29/09) : le symbole de la catégorie de la fiche
            // (personnage, lieu…), posé à l'ouverture, puis « Aucune image ».
            _portraitIcon = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
            _portraitPlaceholder = new StackPanel { Margin = new Thickness(0, 22, 0, 22) };
            _portraitPlaceholder.Children.Add(_portraitIcon);
            _portraitPlaceholder.Children.Add(new TextBlock
            {
                Text = "Aucune image",
                TextAlignment = TextAlignment.Center,
                Foreground = Chrome.SoftText,
                FontSize = 13
            });
            var portraitStack = new StackPanel();
            portraitStack.Children.Add(_portrait);
            portraitStack.Children.Add(_portraitPlaceholder);
            var portraitOptions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var setPortrait = new Button { Content = "Image principale…", Padding = new Thickness(8, 3, 8, 3), [ToolTip.TipProperty] = "Portrait affiché sur la fiche et sur sa carte" };
            setPortrait.Click += delegate { ChoosePortrait(); };
            portraitOptions.Children.Add(setPortrait);
            _removePortrait = new Button { Content = "Retirer", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0), IsVisible = false };
            _removePortrait.Click += delegate { SetPortrait(null); };
            portraitOptions.Children.Add(_removePortrait);
            var imageContent = new StackPanel();
            imageContent.Children.Add(portraitStack);
            imageContent.Children.Add(portraitOptions);

            _imagePaper = Paper("Image", imageContent, null);
            _papersGrid = papers;
            _columns = columns;

            // Les sections (Informations, puis celles du modèle — batch 42)
            // sont des papers bâtis par RebuildPapers à chaque fiche chargée.

            // Relations — le paper n'est là que si le modèle le demande (ou
            // si la fiche en porte déjà).
            _relationsPanel = new StackPanel();
            var addRelation = AddButton("Ajouter une relation");
            addRelation.Click += delegate { AddRelation(); };
            _relationsPaper = Paper("Relations", _relationsPanel, addRelation);

            // Présence dans les écrits (b47) : dérivée, jamais saisie — les
            // écrits où le titre, le nom, le prénom ou un alias apparaissent.
            _presencePanel = new StackPanel();
            _presenceFilter = new ComboBox
            {
                MinWidth = 150,
                MaxWidth = 220,
                FontSize = 12,
                [ToolTip.TipProperty] = "Filtrer le suivi : toute l'amplitude, ou un livre, un groupe, un écrit"
            };
            _presenceFilter.SelectionChanged += delegate
            {
                if (_syncingFilter || _item == null) return;
                var entry = _presenceFilter.SelectedItem as ComboBoxItem;
                _presenceFilterId = entry == null ? null : entry.Tag as string;
                RebuildPresence();
            };
            _presencePaper = Paper("Suivi", _presencePanel, _presenceFilter);
            // Évolution (b47) : les étapes du personnage, chacune liée à un
            // écrit (ou libre), dans l'ordre du récit.
            _evolutionPanel = new StackPanel();
            var addStep = AddButton("Ajouter une étape");
            addStep.Click += delegate { AddStep(); };
            _evolutionPaper = Paper("Évolution", _evolutionPanel, addStep);

            // Trois modes selon la largeur des papers : large (≥ 900 px) =
            // les trois colonnes ; moyen (≥ 560 px) = deux colonnes (image +
            // informations | autres sections + relations, la colonne vide
            // cède) ; étroit = une colonne, les papers empilés.
            Ui.OnSizeChanged(papers, delegate { LayoutPapers(false); });
            // Un paper n'est pas cliquable : pas de curseur main hérité (b42).
            var generalScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = papers, Cursor = new Cursor(StandardCursorType.Arrow) };
            _tabs.Items.Add(new TabItem { Header = "Général", Content = generalScroll });

            // — Texte libre : l'éditeur markdown.
            _bodyBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(18, 12, 18, 14),
                FontFamily = new FontFamily("Georgia"),
                FontSize = BodyFontSize,
                Background = Chrome.PaperBg,
                Foreground = Chrome.PaperInk,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            _bodyBox.TextChanged += delegate
            {
                if (_loading || _item == null) return;
                _item.Document = TextDocument.FromPlainText(_bodyBox.Text);
                NotifyEdited();
            };
            _bodyBox.AddHandler(InputElement.PointerWheelChangedEvent, delegate(object sender, PointerWheelEventArgs e)
            {
                if (!Ui.HasCommand(e.KeyModifiers)) return;
                e.Handled = true;
                var handler = ZoomStepRequested;
                if (handler != null) handler(Ui.Wheel(e) > 0 ? 10 : -10);
            }, RoutingStrategies.Tunnel);
            _bodyBox.KeyDown += OnBodyKeyDown;

            _findBox = new TextBox { Width = 200, Margin = new Thickness(6, 0, 0, 0) };
            _findBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
                else if (e.Key == Key.Escape) { HideSearch(); e.Handled = true; }
            };
            var findNext = Buttons.Icon("next", "Occurrence suivante (Entrée)", Buttons.Compact, Buttons.Look.Outline);
            findNext.Margin = new Thickness(6, 0, 0, 0);
            findNext.Click += delegate { FindNext(); };
            var findClose = new Button { Content = "✕", Width = 24, Margin = new Thickness(6, 0, 0, 0) };
            findClose.Click += delegate { HideSearch(); };
            _findBar = new DockPanel { Margin = new Thickness(14, 6, 14, 2), IsVisible = false, LastChildFill = false };
            _findBar.Children.Add(new TextBlock { Text = "Rechercher :", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center });
            _findBar.Children.Add(_findBox);
            _findBar.Children.Add(findNext);
            _findBar.Children.Add(findClose);

            var editorStack = new DockPanel();
            var toolbar = BuildMarkdownToolbar();
            // À droite de la barre (14/09) : vue édition / vue miroir, les
            // icônes de Markdown We Go, sans texte.
            var toolRow = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
            var modes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 8, 0, 0) };
            _editToggle = ModeToggle("editeur", "Vue édition — le texte seul");
            _mirrorToggle = ModeToggle("miroir", "Vue miroir — le rendu wiki à côté du texte, mis à jour en écrivant");
            _editToggle.Checked += delegate { SetMirror(false); };
            _mirrorToggle.Checked += delegate { SetMirror(true); };
            modes.Children.Add(_editToggle);
            modes.Children.Add(_mirrorToggle);
            // Tout à droite (18/09) : « i » — ce que le texte libre accepte
            // (markdown, vue wiki, prévisualisation) et où en lire plus.
            var info = Buttons.Icon("info-bold", "À propos du texte libre", Buttons.Compact, Buttons.Look.Calm);
            info.Margin = new Thickness(8, 0, 0, 0);
            info.Click += delegate { InfoDialog.ShowMarkdownHelp(Ui.OwnerOf(this)); };
            modes.Children.Add(info);
            DockPanel.SetDock(modes, Dock.Right);
            toolRow.Children.Add(modes);
            toolRow.Children.Add(toolbar);
            DockPanel.SetDock(toolRow, Dock.Top);
            editorStack.Children.Add(toolRow);
            DockPanel.SetDock(_findBar, Dock.Top);
            editorStack.Children.Add(_findBar);
            _mirror = new ScrollViewer
            {
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
                Background = Chrome.FieldBg, // le rendu wiki : la surface du wiki (30/09)
                Padding = new Thickness(14, 8, 14, 12),
                IsVisible = false
            };
            _mirror.AddHandler(InputElement.PointerWheelChangedEvent, delegate(object sender, PointerWheelEventArgs e)
            {
                _mirror.Offset = new Vector(_mirror.Offset.X, _mirror.Offset.Y - Ui.Wheel(e));
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            _bodySplit = new Grid();
            _bodySplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _bodySplit.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _bodySplit.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            Grid.SetColumn(_bodyBox, 0);
            _bodySplit.Children.Add(_bodyBox);
            var divider = new Border { Width = 1, Background = Chrome.Border, IsVisible = false };
            Grid.SetColumn(divider, 1);
            _bodySplit.Children.Add(divider);
            Grid.SetColumn(_mirror, 2);
            _bodySplit.Children.Add(_mirror);
            Ui.OnVisibilityChanged(_mirror, delegate { divider.IsVisible = _mirror.IsVisible; });
            editorStack.Children.Add(_bodySplit);
            _mirrorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _mirrorTimer.Tick += delegate { _mirrorTimer.Stop(); RefreshMirror(); };
            _bodyBox.TextChanged += delegate { if (_mirrorMode && !_loading) { _mirrorTimer.Stop(); _mirrorTimer.Start(); } };
            // L'ombre sur un cadre vide dessous, le contenu net par-dessus
            // (voir Lifted) — l'éditeur s'étire, lui.
            var editorShadow = new Border { Background = Chrome.PaperBg, CornerRadius = new CornerRadius(8), Effect = Shadow() };
            var editorContent = new Border { Background = Chrome.PaperBg, CornerRadius = new CornerRadius(8), Child = editorStack };
            var editorHost = new Grid();
            editorHost.Children.Add(editorShadow);
            editorHost.Children.Add(editorContent);
            var editorPaper = new Border
            {
                Margin = new Thickness(8, 12, 8, 14),
                Child = editorHost,
                ClipToBounds = false,
                Cursor = new Cursor(StandardCursorType.Arrow) // pas de curseur main hérité de l'onglet (b42)
            };
            _tabs.Items.Add(new TabItem { Header = "Texte libre", Content = editorPaper });

            // — Radar (b47 bis) : l'onglet n'existe que si le modèle l'active
            // (RebuildRadar l'ajoute ou le retire).
            _radarHost = new StackPanel { Margin = new Thickness(2, 4, 2, 10) };
            _radarTab = new TabItem
            {
                Header = "Radar",
                Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _radarHost, Cursor = new Cursor(StandardCursorType.Arrow) }
            };

            _body = new Grid();
            _body.Children.Add(_tabs);
            _body.Children.Add(BuildDictionaryBadge()); // bout droit de la rangée d'onglets

            _preview = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsVisible = false };
            // La molette sur le rendu wiki (14/09) : même remède que l'épinglé.
            _preview.AddHandler(InputElement.PointerWheelChangedEvent, delegate(object sender, PointerWheelEventArgs e)
            {
                _preview.Offset = new Vector(_preview.Offset.X, _preview.Offset.Y - Ui.Wheel(e));
                e.Handled = true;
            }, RoutingStrategies.Tunnel);

            var center = new Grid();
            center.Children.Add(_body);
            center.Children.Add(_preview);
            Children.Add(center);
        }

        /// <summary>Le bouton « + … » d'un paper (icône plus livrée, b36).</summary>
        private static Button AddButton(string label)
        {
            return new Button
            {
                Content = Icons.Label("plus-bold", label, 10, Chrome.Ink),
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        /// <summary>Montre l'onglet « Texte libre » (une action d'édition du
        /// markdown y ramène toujours : barre, Ctrl+F, insertions).</summary>
        private void ShowTextTab()
        {
            if (_tabs.SelectedIndex != 1) _tabs.SelectedIndex = 1;
        }

        // ================================================== papers

        private static DropShadowEffect Shadow()
        {
            return new DropShadowEffect { BlurRadius = 10, OffsetY = 1, Opacity = 0.14, Color = Colors.Black };
        }

        internal static Border Paper(string caption, Control content, Control action)
        {
            var stack = new StackPanel();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock
            {
                Text = caption,
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(title, 0);
            head.Children.Add(title);
            if (action != null)
            {
                Grid.SetColumn(action, 1);
                head.Children.Add(action);
            }
            stack.Children.Add(head);
            stack.Children.Add(content);
            return Lifted(new Border
            {
                Background = Chrome.CardBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12),
                Child = stack
            }, Chrome.CardBg, new Thickness(6, 8, 6, 8));
        }

        /// <summary>Un paper à ombre portée SANS flou de texte (b42 bis) : un
        /// DropShadowEffect posé sur le paper lui-même rend tout son contenu
        /// dans une surface intermédiaire — plus de ClearType, texte brouillé
        /// (le paper Relations, avec ses sélecteurs, le montrait le plus).
        /// L'ombre est donc portée par un cadre vide DESSOUS ; le contenu se
        /// dessine par-dessus, net.</summary>
        private static Border Lifted(Border content, IBrush background, Thickness margin)
        {
            var shadow = new Border
            {
                Background = background,
                CornerRadius = content.CornerRadius,
                Effect = Shadow()
            };
            var host = new Grid();
            host.Children.Add(shadow);
            host.Children.Add(content);
            return new Border
            {
                Margin = margin,
                Child = host,
                VerticalAlignment = VerticalAlignment.Top,
                ClipToBounds = false
            };
        }

        // ================================================== sections

        /// <summary>Les papers de la fiche chargée : Informations, puis les
        /// sections du modèle (plus toute section qu'une info libre nomme
        /// encore — rien n'est caché), puis Relations si le modèle le veut
        /// ou si la fiche en porte déjà.</summary>
        private void RebuildPapers()
        {
            _sectionPanels.Clear();
            _sectionPapers.Clear();
            var names = new List<string> { "" };
            if (_template != null)
                foreach (var section in _template.Sections)
                    if (SectionKey(names, section) == null) names.Add(section);
            if (_item != null)
                foreach (var entry in _item.FreeInfo)
                    if (SectionKey(names, entry.Group) == null) names.Add(entry.Group ?? "");
            foreach (var name in names)
            {
                var panel = new StackPanel();
                _sectionPanels[name] = panel;
                var add = AddButton("Ajouter un champ");
                var nameRef = name;
                add.Click += delegate { AddFreeField(nameRef); };
                _sectionPapers.Add(Paper(name.Length == 0 ? SheetDefaults.DefaultSectionLabel : name, panel, add));
            }
            _showRelations = (_template != null && _template.Relations)
                || (_item != null && _item.Relations.Count > 0);
            // Les sections extras (21/09) : le suivi si le modèle le veut ;
            // l'évolution de même, ou si la fiche porte déjà des étapes.
            _showTracking = _template != null && _template.Tracking;
            _showEvolution = (_template != null && _template.Evolution)
                || (_item != null && _item.Evolution.Count > 0);
            // Sans paper Relations, pas de généalogie (batch 43).
            _genealogyButton.IsEnabled = _showRelations;
            ToolTip.SetTip(_genealogyButton, _showRelations
                ? "L'arbre généalogique de la fiche, d'après ses relations (section flottante)"
                : "Généalogie indisponible : le modèle de cette fiche n'active pas les relations");
            LayoutPapers(true);
        }

        /// <summary>Le nom de section déjà connu qui correspond (sans casse), ou null.</summary>
        private static string SectionKey(List<string> names, string group)
        {
            var wanted = group ?? "";
            foreach (var name in names)
                if (string.Equals(name, wanted, StringComparison.CurrentCultureIgnoreCase)) return name;
            return null;
        }

        private StackPanel PanelFor(string group)
        {
            var key = SectionKey(new List<string>(_sectionPanels.Keys), group);
            return _sectionPanels[key ?? ""];
        }

        private void LayoutPapers(bool force)
        {
            var width = _papersGrid.Bounds.Width;
            var wantMode = width >= 900 ? 3 : width >= 560 ? 2 : 1;
            if (wantMode == _layoutMode && !force) return;
            _layoutMode = wantMode;
            _papersGrid.ColumnDefinitions[1].Width = wantMode >= 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _papersGrid.ColumnDefinitions[2].Width = wantMode >= 3 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            foreach (var column in _columns) column.Children.Clear();
            _columns[0].Children.Add(_imagePaper);
            if (_sectionPapers.Count > 0) _columns[0].Children.Add(_sectionPapers[0]);
            var second = wantMode == 1 ? _columns[0] : _columns[1];
            for (var i = 1; i < _sectionPapers.Count; i++) second.Children.Add(_sectionPapers[i]);
            if (_showRelations) second.Children.Add(_relationsPaper);
            // La troisième colonne (b47) : Présence puis Évolution ; en deux
            // colonnes ils suivent les relations, en une ils ferment la pile.
            var third = wantMode == 3 ? _columns[2] : second;
            if (_showTracking) third.Children.Add(_presencePaper);
            if (_showEvolution) third.Children.Add(_evolutionPaper);
            // Sans section extra, la troisième colonne se ferme.
            if (wantMode == 3 && !_showTracking && !_showEvolution)
                _papersGrid.ColumnDefinitions[2].Width = new GridLength(0);
        }

        // ================================================== présence + évolution (b47)

        /// <summary>Une ligne par écrit où un nom de la fiche apparaît, dans
        /// l'ordre du récit : le titre (cliquable), le livre, le compte et
        /// une barre à l'échelle du plus présent.</summary>
        private void RebuildPresence()
        {
            _presencePanel.Children.Clear();
            if (_item == null) return;
            FillPresenceFilter();
            var rows = Presence.Of(_item, _template, _project);
            var chosen = _presenceFilterId == null || _project == null ? null : _project.FindById(_presenceFilterId);
            if (chosen != null)
                rows.RemoveAll(delegate(PresenceRow row) { return row.Text != chosen && !row.Text.IsDescendantOf(chosen); });
            if (rows.Count == 0)
            {
                _presencePanel.Children.Add(Hint(Presence.NamesOf(_item, _template).Count == 0
                    ? "Donnez un nom à la fiche : ses apparitions dans les écrits se compteront ici."
                    : chosen != null ? "Aucun écrit de « " + chosen.Title + " » ne nomme cette fiche."
                    : "Aucun écrit ne nomme cette fiche pour l'instant (titre, nom, prénom, alias)."));
                return;
            }
            var max = 0;
            foreach (var row in rows) if (row.Count > max) max = row.Count;
            foreach (var row in rows)
            {
                var rowRef = row;
                var line = new StackPanel { Margin = new Thickness(0, 0, 0, 7), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, [ToolTip.TipProperty] = "Ouvrir l'écrit" };
                var head = new DockPanel();
                var count = new TextBlock
                {
                    Text = row.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                DockPanel.SetDock(count, Dock.Right);
                head.Children.Add(count);
                var title = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                title.Inlines.Add(new Avalonia.Controls.Documents.Run(row.Text.Title) { Foreground = Chrome.Accent });
                if (row.Book != null)
                    title.Inlines.Add(new Avalonia.Controls.Documents.Run("  " + row.Book.Title) { Foreground = Chrome.FaintText, FontSize = 11 });
                head.Children.Add(title);
                line.Children.Add(head);
                var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Chrome.Border, Margin = new Thickness(0, 3, 0, 0) };
                var fill = new Border { CornerRadius = new CornerRadius(2), Background = Chrome.Accent, HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.7 };
                track.Child = fill;
                Ui.OnSizeChanged(track, delegate { fill.Width = Math.Max(3, track.Bounds.Width * rowRef.Count / max); });
                line.Children.Add(track);
                line.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { var h = NavigateRequested; if (h != null) h(rowRef.Text); };
                _presencePanel.Children.Add(line);
            }
        }

        /// <summary>L'en-tête du suivi (21/09) : « Tout le livre » (ou
        /// l'amplitude du modèle quand elle est réduite), puis la Pile des
        /// écrits suivis — les livres et groupes qui en contiennent, les
        /// écrits — indentés par profondeur. Le choix tient d'une fiche à
        /// l'autre tant que l'élément existe encore.</summary>
        private void FillPresenceFilter()
        {
            _syncingFilter = true;
            _presenceFilter.Items.Clear();
            var scope = _template == null ? new List<string>() : _template.TrackingScope;
            var all = "Tout le livre";
            if (scope.Count > 0)
            {
                var only = scope.Count == 1 && _project != null ? _project.FindById(scope[0]) : null;
                all = only != null ? only.Title : "Amplitude du suivi";
            }
            _presenceFilter.Items.Add(new ComboBoxItem { Content = all, Tag = null });
            var root = _project == null ? null : _project.Category(Project.KeyWritings);
            var tracked = new HashSet<BinderItem>(Presence.Writings(_project, _template));
            if (root != null) AddFilterRows(root, 0, tracked);
            var index = 0;
            for (var i = 1; i < _presenceFilter.Items.Count; i++)
                if ((string)((ComboBoxItem)_presenceFilter.Items[i]).Tag == _presenceFilterId) index = i;
            if (index == 0) _presenceFilterId = null;
            _presenceFilter.SelectedIndex = index;
            _syncingFilter = false;
        }

        /// <summary>Ajoute les entrées du filtre sous un conteneur ; rend vrai
        /// si un écrit suivi y figure (un conteneur vide n'est pas listé).</summary>
        private bool AddFilterRows(BinderItem parent, int depth, HashSet<BinderItem> tracked)
        {
            var any = false;
            foreach (var child in parent.Children)
            {
                var kind = child.Kind;
                if (kind != ItemKind.Book && kind != ItemKind.Folder && kind != ItemKind.Text) continue;
                if (kind == ItemKind.Text && !tracked.Contains(child)) continue;
                var entry = new ComboBoxItem
                {
                    Content = child.Title,
                    Tag = child.Id,
                    Padding = new Thickness(6 + depth * 14, 2, 6, 2),
                    FontWeight = kind == ItemKind.Text ? FontWeight.Normal : FontWeight.SemiBold
                };
                var marker = _presenceFilter.Items.Count;
                _presenceFilter.Items.Add(entry);
                if (kind == ItemKind.Text) { any = true; continue; }
                if (AddFilterRows(child, depth + 1, tracked)) any = true;
                else _presenceFilter.Items.RemoveAt(marker);
            }
            return any;
        }

        /// <summary>Les étapes, dans l'ordre du récit — le même que le wiki
        /// (Presence.OrderedSteps), rebâti dès qu'une étape change d'écrit.</summary>
        private void RebuildEvolution()
        {
            _evolutionPanel.Children.Clear();
            if (_item == null) return;
            var writings = Presence.Writings(_project);
            foreach (var step in Presence.OrderedSteps(_item, _project))
                _evolutionPanel.Children.Add(EvolutionRow(step, writings));
            if (_item.Evolution.Count == 0)
                _evolutionPanel.Children.Add(Hint("Aucune étape — ce qui change pour cette fiche, écrit par écrit ou en étape libre nommée : « perd son bras », « apprend la vérité »…"));
        }

        private const string FreeStepEntry = "— étape libre —";

        /// <summary>Une étape : l'écrit (sélecteur dans l'ordre de la Pile,
        /// « Livre › Titre » dans un livre, ou libre — et alors un champ pour
        /// la nommer, 21/09), la croix, la note.</summary>
        private Control EvolutionRow(EvolutionEntry step, List<BinderItem> writings)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var remove = new Button { Content = "✕", Width = 26, Margin = new Thickness(6, 0, 0, 0), [ToolTip.TipProperty] = "Supprimer cette étape" };
            remove.Click += delegate
            {
                _item.Evolution.Remove(step);
                RebuildEvolution();
                NotifyEdited();
            };
            DockPanel.SetDock(remove, Dock.Right);
            head.Children.Add(remove);
            var free = step.TextId == null;
            var combo = new ComboBox { [ToolTip.TipProperty] = "L'écrit où cette étape se joue, ou une étape libre à nommer" };
            if (free)
            {
                // Étape libre : le sélecteur se resserre, le nom prend la place.
                combo.Width = 150;
                DockPanel.SetDock(combo, Dock.Left);
            }
            combo.Items.Add(FreeStepEntry);
            var selected = 0;
            for (var i = 0; i < writings.Count; i++)
            {
                var book = writings[i].EnclosingBook();
                combo.Items.Add(book != null ? book.Title + " › " + writings[i].Title : writings[i].Title);
                if (writings[i].Id == step.TextId) selected = i + 1;
            }
            if (step.TextId != null && selected == 0)
            {
                combo.Items.Add("(écrit disparu)");
                selected = combo.Items.Count - 1;
            }
            combo.SelectedIndex = selected;
            combo.SelectionChanged += delegate
            {
                if (_loading || _item == null) return;
                var index = combo.SelectedIndex;
                if (index < 0 || index > writings.Count) return;
                step.TextId = index == 0 ? null : writings[index - 1].Id;
                NotifyEdited();
                // L'étape reprend sa place dans l'ordre du récit et sa forme
                // (nom libre ou non) — après le geste, hors du sélecteur.
                Ui.Post(DispatcherPriority.Input, new Action(delegate
                {
                    if (_item == null || !_item.Evolution.Contains(step)) return;
                    RebuildEvolution();
                    Control focus;
                    if (step.TextId == null && _fieldBoxes.TryGetValue("evolution-title:" + step.Id, out focus)) focus.Focus();
                }));
            };
            head.Children.Add(combo);
            if (free)
            {
                var title = new TextBox
                {
                    Text = step.Title ?? "",
                    Margin = new Thickness(6, 0, 0, 0),
                    [ToolTip.TipProperty] = "Le nom de cette étape libre : « Enfance », « Après la guerre »…"
                };
                _fieldBoxes["evolution-title:" + step.Id] = title;
                title.TextChanged += delegate
                {
                    if (_loading || _item == null) return;
                    step.Title = title.Text;
                    NotifyEdited();
                };
                head.Children.Add(title);
            }
            row.Children.Add(head);
            var note = new TextBox
            {
                Text = step.Note,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 40,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            _fieldBoxes["evolution:" + step.Id] = note;
            note.TextChanged += delegate
            {
                if (_loading || _item == null) return;
                step.Note = note.Text;
                NotifyEdited();
            };
            row.Children.Add(note);
            return row;
        }

        private void AddStep()
        {
            if (_item == null) return;
            var step = new EvolutionEntry();
            _item.Evolution.Add(step);
            RebuildEvolution();
            NotifyEdited();
            Control box; // une étape naît libre : son nom d'abord
            if (_fieldBoxes.TryGetValue("evolution-title:" + step.Id, out box)) box.Focus();
        }

        // ================================================== champs

        // Les zones de saisie par identifiant (champ de modèle, info libre,
        // relation) — la navigation d'une occurrence les retrouve (b37).
        private readonly Dictionary<string, Control> _fieldBoxes = new Dictionary<string, Control>();
        private readonly Dictionary<string, Control> _relationBoxes = new Dictionary<string, Control>();

        /// <summary>Va à une occurrence de la recherche projet : un paragraphe
        /// du corps (onglet Texte libre, sélection exacte), ou un champ
        /// (onglet Général, zone amenée à l'écran et sélectionnée).</summary>
        public void GoTo(SearchField field, int start, int length)
        {
            if (_item == null || field == null) return;
            if (_previewToggle.IsChecked == true) _previewToggle.IsChecked = false;
            if (field.IsParagraph)
            {
                _tabs.SelectedIndex = 1;
                UpdateLayout();
                var index = Math.Min(PlainOffset(field.ParagraphIndex) + start, (_bodyBox.Text ?? "").Length);
                _bodyBox.Focus();
                Ui.Select(_bodyBox, index, Math.Max(0, Math.Min(length, (_bodyBox.Text ?? "").Length - index)));
                Ui.ScrollToLine(_bodyBox, Math.Max(0, Ui.LineOf(_bodyBox, index)));
                return;
            }
            _tabs.SelectedIndex = 0;
            UpdateLayout();
            Control box = null;
            if (field.RefId != null && !_fieldBoxes.TryGetValue(field.RefId, out box)) _relationBoxes.TryGetValue(field.RefId, out box);
            if (box == null) return;
            box.BringIntoView();
            box.Focus();
            var textBox = box as TextBox;
            if (textBox != null)
            {
                var max = (textBox.Text ?? "").Length;
                var at = Math.Min(start, max);
                Ui.Select(textBox, at, Math.Max(0, Math.Min(length, max - at)));
            }
        }

        /// <summary>L'offset, dans le texte plein du corps, du début d'un
        /// paragraphe (les mêmes règles que TextDocument.ToPlainText).</summary>
        private int PlainOffset(int paragraphIndex)
        {
            var offset = 0;
            var paragraphs = _item.Document.Paragraphs;
            for (var i = 0; i < paragraphIndex && i < paragraphs.Count; i++)
            {
                foreach (var run in paragraphs[i].Runs)
                {
                    if (run.FootnoteId != null || run.ImageId != null || run.IsRule) continue;
                    offset += run.IsLineBreak ? 1 : (run.Text ?? "").Length;
                }
                offset++; // le saut de ligne entre paragraphes
            }
            return offset;
        }

        private void RebuildFields()
        {
            foreach (var panel in _sectionPanels.Values) panel.Children.Clear();
            _fieldBoxes.Clear();
            if (_item == null) return;
            if (_template != null)
                foreach (var field in _template.Fields)
                {
                    string value;
                    _item.FieldValues.TryGetValue(field.Id, out value);
                    var fieldRef = field;
                    // La nature du champ choisit l'éditeur (b47 bis).
                    var editor = Editor(field.Kind, value ?? "", field.Options, delegate(string text)
                    {
                        _item.FieldValues[fieldRef.Id] = text;
                    }, field.Id);
                    PanelFor(field.Group).Children.Add(FieldRow(field.Name, editor, null));
                }
            foreach (var entry in _item.FreeInfo)
                PanelFor(entry.Group).Children.Add(FreeFieldRow(entry));
            foreach (var pair in _sectionPanels)
                if (pair.Value.Children.Count == 0)
                    pair.Value.Children.Add(Hint(pair.Key.Length == 0
                        ? "Aucune information — ajoutez un champ."
                        : "Aucun champ dans cette section — ajoutez-en un."));
        }

        private static TextBlock Hint(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        }

        /// <summary>Une rangée d'un paper (batch 42) : le nom du champ en haut,
        /// petit, l'éditeur sur la ligne du dessous. remove = bouton ✕ optionnel.</summary>
        private static Control FieldRow(string label, Control editor, Button remove)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            if (remove != null) { DockPanel.SetDock(remove, Dock.Right); head.Children.Add(remove); }
            head.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Chrome.FaintText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(head);
            row.Children.Add(editor);
            return row;
        }

        /// <summary>L'éditeur d'une valeur selon sa nature (FieldEditors, b47
        /// bis) : le changement est ignoré pendant le chargement, sinon
        /// appliqué puis signalé ; la zone est enregistrée pour la recherche.</summary>
        private Control Editor(string kind, string value, List<string> options, Action<string> onChanged, string refId)
        {
            var editor = FieldEditors.Build(kind, value, options, _project, _item, delegate(string text)
            {
                if (_loading || _item == null) return;
                onChanged(text);
                NotifyEdited();
            }, delegate(BinderItem target) { var h = NavigateRequested; if (h != null) h(target); });
            var focus = FieldEditors.FocusTarget(editor);
            if (refId != null && focus != null) _fieldBoxes[refId] = focus;
            return editor;
        }

        /// <summary>Un champ propre à la fiche : son nom, une fois renseigné,
        /// s'affiche en texte simple avec un crayon à côté qui rouvre la zone
        /// de saisie pour le changer ; la valeur dessous (batch 42).</summary>
        private Control FreeFieldRow(InfoEntry entry)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var remove = Buttons.Icon("trash", "Supprimer ce champ", Buttons.Compact, Buttons.Look.Calm);
            remove.Click += delegate
            {
                _item.FreeInfo.Remove(entry);
                RebuildFields();
                NotifyEdited();
            };
            DockPanel.SetDock(remove, Dock.Right);
            head.Children.Add(remove);

            var titleText = new TextBlock
            {
                Foreground = Chrome.FaintText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var titleBox = new TextBox { [ToolTip.TipProperty] = "Nom du champ — Entrée pour valider", MaxWidth = 240, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 120 };
            var rename = Buttons.Icon("pencil-simple-line", "Changer le nom du champ", Buttons.Compact, Buttons.Look.Calm);
            // La saisie du nom n'est ouverte que pour le champ que l'on VIENT
            // d'ajouter (12/09, seconde passe) : un champ enregistré sans nom
            // ou resté « Champ » s'affiche comme les autres, crayon compris.
            var editing = entry.Id == _focusFieldId;
            Action sync = delegate
            {
                titleText.Text = string.IsNullOrEmpty((entry.Title ?? "").Trim()) ? "(sans nom)" : entry.Title;
                titleText.IsVisible = editing ? false : true;
                rename.IsVisible = editing ? false : true;
                titleBox.IsVisible = editing ? true : false;
            };
            titleBox.Text = entry.Title;
            titleBox.TextChanged += delegate
            {
                if (_loading) return;
                entry.Title = titleBox.Text;
                NotifyEdited();
            };
            Action close = delegate
            {
                if (string.IsNullOrEmpty(entry.Title.Trim())) return; // sans nom : la saisie reste
                editing = false;
                sync();
            };
            titleBox.LostFocus += delegate { close(); };
            titleBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Escape) { close(); e.Handled = true; }
            };
            rename.Click += delegate
            {
                editing = true;
                sync();
                titleBox.Focus();
                titleBox.SelectAll();
            };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(titleText);
            titleRow.Children.Add(titleBox);
            rename.Margin = new Thickness(4, 0, 0, 0);
            titleRow.Children.Add(rename);
            // La nature du champ libre (b47 bis) : un petit sélecteur ; un
            // « Choix » demande ses options (bouton « … » pour les revoir).
            var kindCombo = new ComboBox
            {
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 96,
                [ToolTip.TipProperty] = "La nature de ce champ : texte, nombre, date, note, liste, choix, fiche liée"
            };
            foreach (var kind in FieldKinds.All) kindCombo.Items.Add(FieldKinds.Label(kind));
            kindCombo.SelectedIndex = Array.IndexOf(FieldKinds.All, FieldKinds.Normalize(entry.Kind));
            var optionsButton = new Button
            {
                Content = "…",
                Width = 24,
                Padding = new Thickness(0, 1, 0, 1),
                Margin = new Thickness(4, 0, 0, 0),
                [ToolTip.TipProperty] = "Les options de ce choix (séparées par des virgules)",
                IsVisible = FieldKinds.Normalize(entry.Kind) == FieldKinds.Choice ? true : false
            };
            Action askOptions = async delegate
            {
                var text = await InputDialog.Ask(Ui.OwnerOf(this), "Options du choix",
                    "Les options, séparées par des virgules :", FieldKinds.JoinOptions(entry.Options));
                if (text == null) return;
                entry.Options = FieldKinds.ListItems(text);
                NotifyEdited();
                RebuildFields();
            };
            optionsButton.Click += delegate { askOptions(); };
            kindCombo.SelectionChanged += delegate
            {
                if (_loading || kindCombo.SelectedIndex < 0) return;
                var chosen = FieldKinds.All[kindCombo.SelectedIndex];
                if (chosen == entry.Kind) return;
                entry.Kind = chosen;
                NotifyEdited();
                // Reconstruction différée : on ne détruit pas le sélecteur
                // pendant son propre événement.
                Ui.Post(DispatcherPriority.Background, new Action(delegate
                {
                    if (chosen == FieldKinds.Choice && entry.Options.Count == 0) askOptions();
                    else RebuildFields();
                }));
            };
            titleRow.Children.Add(kindCombo);
            titleRow.Children.Add(optionsButton);
            head.Children.Add(titleRow);
            sync();
            row.Children.Add(head);
            row.Children.Add(Editor(entry.Kind, entry.Value, entry.Options, delegate(string text) { entry.Value = text; }, entry.Id));
            // Seul le champ que l'on VIENT d'ajouter prend le focus (pack du
            // 12/09/2026) : un champ resté « Champ » dans une fiche enregistrée
            // se retrouvait sélectionné à chaque ouverture de la fiche.
            if (editing && entry.Id == _focusFieldId)
            {
                _focusFieldId = null;
                row.Loaded += delegate { if (titleBox.IsVisible && titleBox.Text == "Champ") { titleBox.Focus(); titleBox.SelectAll(); } };
            }
            return row;
        }

        private void AddFreeField(string group)
        {
            if (_item == null) return;
            var entry = new InfoEntry { Title = "Champ", Group = group ?? "" };
            _item.FreeInfo.Add(entry);
            _focusFieldId = entry.Id;
            RebuildFields();
            NotifyEdited();
        }

        // ================================================== dictionnaire

        /// <summary>L'indicateur « présent dans / absent du dictionnaire » au
        /// bout droit de la rangée d'onglets (pack du 12/09/2026) : vert quand
        /// CHAQUE mot du nom de la fiche est une entrée (ou une forme d'entrée)
        /// du dictionnaire du projet ou de tous les projets ; sinon un bouton
        /// à l'icône du dictionnaire ouvre une nouvelle entrée, le nom de la
        /// fiche prérempli (à raccourcir à la main : un clic par mot).</summary>
        private Control BuildDictionaryBadge()
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 16, 0)
            };
            _dictDot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            row.Children.Add(_dictDot);
            _dictState = new TextBlock
            {
                FontSize = 11,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(_dictState);
            _dictAdd = Buttons.Icon("pile-dictionnaire", "Ajouter le nom de la fiche au dictionnaire du projet",
                Buttons.Compact, Buttons.Look.Calm);
            _dictAdd.Margin = new Thickness(6, 0, 0, 0);
            _dictAdd.Click += delegate { AddNameToDictionary(); };
            row.Children.Add(_dictAdd);
            return row;
        }

        /// <summary>Les mots du nom de la fiche (lettres, apostrophes et traits
        /// d'union ; la ponctuation sépare).</summary>
        public static List<string> NameWords(string title)
        {
            var words = new List<string>();
            var current = new System.Text.StringBuilder();
            foreach (var c in (title ?? "") + " ")
            {
                if (char.IsLetter(c) || c == '\'' || c == '’' || c == '-')
                {
                    current.Append(c);
                    continue;
                }
                var word = current.ToString().Trim('\'', '’', '-');
                if (word.Length > 0) words.Add(word);
                current.Length = 0;
            }
            return words;
        }

        /// <summary>Les mots du nom qui ne sont ni une entrée ni une forme
        /// d'entrée (accents et casse ignorés).</summary>
        private List<string> MissingWords()
        {
            var missing = new List<string>();
            if (_item == null) return missing;
            var known = new HashSet<string>();
            var sources = new List<List<LexiconEntry>>();
            if (_project != null) sources.Add(_project.Lexicon);
            sources.Add(Settings.AppSettings.Lexicon);
            foreach (var entries in sources)
                foreach (var entry in entries)
                    foreach (var form in entry.Forms())
                        known.Add(Correction.FrenchTokenizer.Fold(form));
            // Le dictionnaire général compte aussi (29/09) : « de », « la », un
            // prénom courant que le correcteur connaît déjà n'ont rien à
            // apprendre — seuls les mots qu'il soulignerait manquent.
            var engine = Correction.SpellDictionary.Default;
            foreach (var word in NameWordsAndFields())
            {
                if (known.Contains(Correction.FrenchTokenizer.Fold(word))) continue;
                if (engine != null && engine.Accepts(word)) continue;
                missing.Add(word);
            }
            return missing;
        }

        /// <summary>Les mots que le dictionnaire doit connaître (29/09) : ceux du
        /// titre, et ceux des champs Nom, Prénom, Alias d'un personnage.</summary>
        private List<string> NameWordsAndFields()
        {
            return _item == null ? new List<string>() : DictionarySuggestions();
        }

        private void RefreshDictionaryBadge()
        {
            if (_dictState == null) return;
            var words = NameWordsAndFields();
            if (_item == null || words.Count == 0)
            {
                _dictDot.IsVisible = false;
                _dictState.IsVisible = false;
                _dictAdd.IsVisible = false;
                return;
            }
            var missing = MissingWords();
            var present = missing.Count == 0;
            // Partiellement présent (29/09) : une partie des mots manque —
            // un prénom connu mais pas le nom, l'alias oublié…
            var partial = !present && missing.Count < words.Count;
            _dictDot.IsVisible = true;
            _dictState.IsVisible = true;
            _dictDot.Background = present ? Chrome.Ok : Chrome.Warn;
            _dictState.Text = present ? "Présent dans le dictionnaire"
                : partial ? "Partiellement présent dans le dictionnaire" : "Absent du dictionnaire";
            ToolTip.SetTip(_dictState, present
                ? "Chaque mot du nom, du prénom et de l'alias est connu du dictionnaire"
                : "Manque : " + string.Join(", ", missing.ToArray()));
            _dictAdd.IsVisible = present ? false : true;
        }

        /// <summary>Les chips du dialogue d'ajout (29/09) : chaque mot du titre
        /// de la fiche, puis — champs « Nom », « Prénom », « Alias » du modèle
        /// (personnages) — leurs valeurs, mot par mot ; sans doublon, dans
        /// l'ordre, jamais un mot d'une lettre.</summary>
        private List<string> DictionarySuggestions()
        {
            var words = new List<string>();
            var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            Action<string> add = delegate(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                foreach (var raw in text.Split(new[] { ' ', ' ', ',', ';', '/', '(', ')', '«', '»', '"', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var word = raw.Trim('.', '…', '!', '?', ':', '\'', '’');
                    if (word.Length < 2 || !seen.Add(word)) continue;
                    words.Add(word);
                }
            };
            add(_item.Title);
            if (_template != null)
                foreach (var field in _template.Fields)
                {
                    var name = (field.Name ?? "").Trim();
                    if (name != "Nom" && name != "Prénom" && name != "Alias") continue;
                    string value;
                    if (_item.FieldValues.TryGetValue(field.Id, out value)) add(value);
                }
            return words;
        }

        /// <summary>Nouvelle entrée du dictionnaire DU PROJET, le nom de la
        /// fiche prérempli et modifiable ; une entrée du même mot est remplacée.</summary>
        private async void AddNameToDictionary()
        {
            if (_item == null || _project == null) return;
            // Les propositions (30/09) : les mots qui MANQUENT seulement — ce
            // qui est déjà au dictionnaire n'est plus proposé (doublons d'un
            // « partiellement présent ») ; le titre reste prérempli tant qu'un
            // de ses mots manque, sinon le premier mot manquant.
            var missing = MissingWords();
            var titleMissing = missing.Count == 0;
            foreach (var word in missing)
                if (_item.Title.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0) { titleMissing = true; break; }
            var entry = await LexiconEntryDialog.AskForWord(Ui.OwnerOf(this), titleMissing ? _item.Title : missing[0], true, missing);
            if (entry == null) return;
            var existing = LexiconEntry.Find(_project.Lexicon, entry.Word);
            if (existing != null) _project.Lexicon.Remove(existing);
            _project.Lexicon.Add(entry);
            RefreshDictionaryBadge();
            var handler = LexiconChanged;
            if (handler != null) handler();
        }

        // ================================================== relations

        private List<BinderItem> OtherSheets()
        {
            var sheets = new List<BinderItem>();
            if (_project == null) return sheets;
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Sheet && item != _item
                    && item.RootCategory().CategoryKey != Project.KeyTrash)
                    sheets.Add(item);
            sheets.Sort(delegate(BinderItem a, BinderItem b)
            { return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase); });
            return sheets;
        }

        private void RebuildRelations()
        {
            _relationsPanel.Children.Clear();
            _relationBoxes.Clear();
            if (_item == null) return;
            var sheets = OtherSheets();
            foreach (var relation in _item.Relations)
                _relationsPanel.Children.Add(RelationRow(relation, sheets));
            if (_item.Relations.Count == 0)
                _relationsPanel.Children.Add(Hint("Aucune relation — « frère », « mentor », « rivale »… vers une fiche ou un nom."));
        }

        private const string NewKindEntry = "＋  Nouvelle nature…";

        /// <summary>Une rangée de relation (b36) : nature (sélecteur éditable —
        /// natures livrées, personnalisées du projet, « Nouvelle nature… »),
        /// flèche, cible (fiche du projet ou nom libre), ouvrir, supprimer.
        /// Une cible-fiche reçoit la relation réciproque (RelationSync).</summary>
        private Control RelationRow(SheetRelation relation, List<BinderItem> sheets)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var remove = new Button { Content = "✕", Width = 26, Margin = new Thickness(6, 0, 0, 0), [ToolTip.TipProperty] = "Supprimer cette relation (et son reflet sur la fiche liée)" };
            remove.Click += delegate
            {
                RelationSync.Unmirror(_project, _item, relation.TargetId, relation.Kind);
                _item.Relations.Remove(relation);
                RebuildRelations();
                NotifyEdited();
            };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);

            var kindBox = new ComboBox
            {
                IsEditable = true,
                Width = 124,
                Margin = new Thickness(0, 0, 6, 0),
                [ToolTip.TipProperty] = "Nature de la relation — une nature livrée, une nature du projet, ou « Nouvelle nature… » pour en créer une"
            };
            if (_project != null) foreach (var kind in _project.AllRelationKinds()) kindBox.Items.Add(kind);
            else foreach (var kind in RelationKinds.Defaults) kindBox.Items.Add(kind);
            kindBox.Items.Add(NewKindEntry);
            kindBox.Text = relation.Kind;
            // L'autocomplétion (29/09) : les natures qui contiennent la frappe.
            Suggestions.Attach(kindBox,
                delegate { return _project != null ? (IEnumerable<string>)_project.AllRelationKinds() : RelationKinds.Defaults; },
                delegate(string chosen) { kindBox.Text = chosen; CommitRelationKind(relation, kindBox); });
            kindBox.SelectionChanged += delegate
            {
                if (_loading || kindBox.SelectedIndex < 0) return;
                var chosen = kindBox.SelectedItem as string;
                Ui.Post(DispatcherPriority.Background, new Action(delegate
                    {
                        if (chosen == NewKindEntry) PromptNewKind(relation, kindBox);
                        else CommitRelationKind(relation, kindBox);
                    }));
            };
            kindBox.LostFocus += delegate
            {
                if ((kindBox.Text ?? "") != NewKindEntry) CommitRelationKind(relation, kindBox);
            };
            DockPanel.SetDock(kindBox, Dock.Left);
            row.Children.Add(kindBox);
            var arrow = Icons.Make("arrow-right-bold", 11, Chrome.SoftText) as Control;
            if (arrow != null)
            {
                arrow.VerticalAlignment = VerticalAlignment.Center;
                arrow.Margin = new Thickness(0, 0, 6, 0);
                DockPanel.SetDock(arrow, Dock.Left);
                row.Children.Add(arrow);
            }

            // Ouvrir la fiche liée (quand la cible en est une).
            var target = _project == null || relation.TargetId == null ? null : _project.FindById(relation.TargetId);
            var open = new Button
            {
                Content = Icons.Make("arrow-up-right-bold", 11, Chrome.Ink),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(6, 0, 0, 0),
                IsVisible = target != null ? true : false,
                [ToolTip.TipProperty] = "Ouvrir la fiche liée"
            };
            open.Click += delegate
            {
                var linked = _project == null || relation.TargetId == null ? null : _project.FindById(relation.TargetId);
                var handler = NavigateRequested;
                if (linked != null && handler != null) handler(linked);
            };
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);

            // La cible : une fiche existante (liste) ou un nom libre (saisie).
            var combo = new ComboBox { IsEditable = true, [ToolTip.TipProperty] = "Une fiche du projet, ou un nom libre" };
            _relationBoxes[relation.Id] = combo;
            foreach (var sheet in sheets) combo.Items.Add(sheet.Title);
            combo.Text = target != null ? target.Title : relation.Name;
            // L'autocomplétion (29/09) : les fiches dont le titre contient la frappe.
            Suggestions.Attach(combo,
                delegate { var titles = new List<string>(); foreach (var sheet in sheets) titles.Add(sheet.Title); return titles; },
                delegate(string chosen) { combo.Text = chosen; CommitRelationTarget(relation, combo, sheets, open); });
            combo.LostFocus += delegate { CommitRelationTarget(relation, combo, sheets, open); };
            combo.SelectionChanged += delegate
            {
                if (_loading || combo.SelectedIndex < 0) return;
                Ui.Post(DispatcherPriority.Background, new Action(delegate { CommitRelationTarget(relation, combo, sheets, open); }));
            };
            row.Children.Add(combo);
            return row;
        }

        private void CommitRelationKind(SheetRelation relation, ComboBox kindBox)
        {
            if (_loading || _item == null) return;
            var text = RelationKinds.Canonical(kindBox.Text ?? "");
            if (text == NewKindEntry) return;
            if (text == relation.Kind) return;
            var previous = relation.Kind;
            relation.Kind = text;
            if (kindBox.Text != text) kindBox.Text = text;
            if (relation.TargetId != null) RelationSync.Mirror(_project, _item, relation, previous);
            NotifyEdited();
        }

        /// <summary>« Nouvelle nature… » : demande un nom, l'enregistre dans
        /// le projet (disponible sur toutes les fiches) et l'applique.</summary>
        private async void PromptNewKind(SheetRelation relation, ComboBox kindBox)
        {
            var name = _project == null ? null : await InputDialog.Ask(Ui.OwnerOf(this),
                "Nouvelle nature de relation", "Nom de la nature (ex. « Mentor », « Rivale ») :", "");
            if (name == null || name.Trim().Length == 0)
            {
                kindBox.Text = relation.Kind;
                return;
            }
            var kind = _project.AddRelationKind(name);
            var previous = relation.Kind;
            relation.Kind = kind;
            if (relation.TargetId != null) RelationSync.Mirror(_project, _item, relation, previous);
            RebuildRelations(); // tous les sélecteurs proposent la nature neuve
            NotifyEdited();
        }

        private void CommitRelationTarget(SheetRelation relation, ComboBox combo, List<BinderItem> sheets, Button open)
        {
            if (_loading || _item == null) return;
            var text = (combo.Text ?? "").Trim();
            BinderItem match = null;
            foreach (var sheet in sheets)
                if (string.Equals(sheet.Title, text, StringComparison.CurrentCultureIgnoreCase)) { match = sheet; break; }
            var newId = match != null ? match.Id : null;
            var newName = match != null ? "" : text;
            if (newId == relation.TargetId && newName == relation.Name) return;
            if (relation.TargetId != null && relation.TargetId != newId)
                RelationSync.Unmirror(_project, _item, relation.TargetId, relation.Kind);
            relation.TargetId = newId;
            relation.Name = newName;
            if (newId != null) RelationSync.Mirror(_project, _item, relation, null);
            open.IsVisible = match != null ? true : false;
            NotifyEdited();
        }

        private void AddRelation()
        {
            if (_item == null) return;
            _item.Relations.Add(new SheetRelation { Kind = "" });
            RebuildRelations();
            NotifyEdited();
        }

        // ================================================== généalogie

        /// <summary>Le paper flottant de l'arbre (b36) — une seule fenêtre,
        /// rechargée à chaque fiche, rafraîchie à chaque relation modifiée.</summary>
        /// <summary>Resynchronise le titre du bandeau après un renommage (b43).</summary>
        public void RefreshTitle()
        {
            if (_item != null) _titleLabel.Text = _item.Title;
            RefreshDictionaryBadge();
        }

        private void ShowGenealogy()
        {
            if (_item == null) return;
            if (_genealogy == null)
            {
                _genealogy = new GenealogyWindow(Ui.OwnerOf(this));
                _genealogy.NavigateRequested += delegate(BinderItem target)
                {
                    var handler = NavigateRequested;
                    if (handler != null) handler(target);
                };
                _genealogy.Closed += delegate { _genealogy = null; };
            }
            _genealogy.Load(_project, _item);
            if (!_genealogy.IsVisible) _genealogy.Show();
            else _genealogy.Activate();
        }

        private void SyncGenealogy()
        {
            if (_genealogy == null) return;
            if (_item == null) { _genealogy.Close(); return; }
            if (_genealogy.Shows(_item)) _genealogy.Refresh();
            else _genealogy.Load(_project, _item);
        }

        // ================================================== barre markdown

        private Control BuildMarkdownToolbar()
        {
            // Les icônes de l'éditeur de texte, en boutons CARRÉS (batch 42) ;
            // ce qui n'a pas d'icône dans le jeu reste en lettres, carré aussi.
            var bar = new WrapPanel { Margin = new Thickness(10, 8, 10, 4) };
            bar.Children.Add(IconTool("bold", "Gras (" + Ui.Keys("Ctrl+B") + ")", delegate { Wrap("**", "**"); }));
            bar.Children.Add(IconTool("italic", "Italique (" + Ui.Keys("Ctrl+I") + ")", delegate { Wrap("*", "*"); }));
            bar.Children.Add(IconTool("underline", "Souligné (" + Ui.Keys("Ctrl+U") + ")", delegate { Wrap("<u>", "</u>"); }));
            bar.Children.Add(IconTool("strikethrough", "Barré", delegate { Wrap("~~", "~~"); }));
            bar.Children.Add(Gap());
            bar.Children.Add(TextTool("H1", "Titre de niveau 1", delegate { ApplyHeading(1); }));
            bar.Children.Add(TextTool("H2", "Titre de niveau 2", delegate { ApplyHeading(2); }));
            bar.Children.Add(TextTool("H3", "Titre de niveau 3", delegate { ApplyHeading(3); }));
            bar.Children.Add(Gap());
            bar.Children.Add(IconTool("list", "Liste à puces", delegate { ApplyList("puce"); }));
            bar.Children.Add(IconTool("list-numbers-bold", "Liste numérotée", delegate { ApplyList("num"); }));
            bar.Children.Add(IconTool("list-dashes-bold", "Liste à tirets", delegate { ApplyList("tiret"); }));
            bar.Children.Add(IconTool("list-check", "Liste de tâches (cases à cocher)", delegate { ApplyList("case"); }));
            bar.Children.Add(TextTool("❝", "Citation", delegate { ApplyQuote(); }));
            bar.Children.Add(Gap());
            bar.Children.Add(IconTool("tableau-recherche", "Insérer un tableau", delegate { InsertTable(); }));
            bar.Children.Add(IconTool("horizontal-rule", "Filet horizontal", delegate { InsertRule(); }));
            bar.Children.Add(TextTool("🔗", "Lien hypertexte", delegate { InsertLink(); }));
            bar.Children.Add(IconTool("image-square-bold", "Image", delegate { InsertImage(); }));
            bar.Children.Add(IconTool("fiche-individual", "Lien vers une fiche (" + Ui.Keys("Ctrl+K") + ")", delegate { Wrap("[[", "]]"); }));
            return bar;
        }

        /// <summary>Un bouton de mode de la barre (édition / miroir), icône
        /// seule, accent quand actif.</summary>
        private static ToggleButton ModeToggle(string icon, string tooltip)
        {
            return new ToggleButton
            {
                Classes = { Marabook.App.Theme.Owned },
                Content = Icons.Make(icon, 14, Chrome.Ink),
                Width = Buttons.Compact,
                Height = Buttons.Compact,
                Padding = new Thickness(0),
                Margin = new Thickness(2, 0, 0, 0),
                [ToolTip.TipProperty] = tooltip,
                Focusable = false
            };
        }

        /// <summary>Vue miroir ou vue édition — les deux bascules restent
        /// cohérentes, le rendu se (re)fait à l'ouverture du miroir.</summary>
        private void SetMirror(bool on)
        {
            _mirrorMode = on;
            if (_editToggle.IsChecked != !on) _editToggle.IsChecked = !on;
            if (_mirrorToggle.IsChecked != on) _mirrorToggle.IsChecked = on;
            _bodySplit.ColumnDefinitions[2].Width = on ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _mirror.IsVisible = on ? true : false;
            if (on) RefreshMirror();
            else _mirror.Content = null;
        }

        private void RefreshMirror()
        {
            if (!_mirrorMode || _item == null) { _mirror.Content = null; return; }
            var flow = MarkdownRender.Build(_bodyBox.Text,
                delegate(string target) { var handler = LinkClicked; if (handler != null) handler(target); },
                ToggleTask);
            _mirror.Content = new ScrollViewer { Content = flow,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
                Focusable = false
            };
        }

        private static Control Gap()
        {
            return new Border { Width = 1, Height = 18, Background = Chrome.Border, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        }

        private Button IconTool(string icon, string tooltip, Action action)
        {
            var button = Buttons.Icon(icon, tooltip, Buttons.Compact, Buttons.Look.Calm);
            button.Margin = new Thickness(0, 0, 2, 3);
            button.Click += delegate { if (_item != null) action(); };
            return button;
        }

        private Button TextTool(string label, string tooltip, Action action)
        {
            var button = Buttons.Text(label, tooltip, Buttons.Compact, Buttons.Look.Calm);
            button.Width = Buttons.Compact; // carré, comme les icônes
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(0, 0, 2, 3);
            button.Click += delegate { if (_item != null) action(); };
            return button;
        }

        private void OnBodyKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && ContinueList()) { e.Handled = true; return; }
            if (!Ui.HasCommand(e.KeyModifiers)) return;
            if (e.Key == Key.B) { Wrap("**", "**"); e.Handled = true; }
            else if (e.Key == Key.I) { Wrap("*", "*"); e.Handled = true; }
            else if (e.Key == Key.U) { Wrap("<u>", "</u>"); e.Handled = true; }
        }

        /// <summary>La sélection ORDONNÉE du corps (hotfix 1.0.3-a) : tirée de
        /// la fin vers le début, SelectionStart (l'ancre) dépasse SelectionEnd
        /// et la longueur « fin − début » était négative — Ctrl+B croyait la
        /// sélection vide et remplaçait le passage par les seules marques.</summary>
        private void OrderedSelection(out int start, out int length)
        {
            var a = _bodyBox.SelectionStart;
            var b = _bodyBox.SelectionEnd;
            start = Math.Min(a, b);
            length = Math.Abs(b - a);
        }

        /// <summary>Entrée dans une liste (hotfix 1.0.3-a) : la ligne suivante
        /// reçoit la même puce (ou le numéro suivant, ou une case vide) ;
        /// Entrée sur une puce encore vide la retire et ramène au début de
        /// la ligne. Faux hors d'une liste : le TextBox fait l'Entrée.</summary>
        internal bool ContinueList()
        {
            if (_item == null) return false;
            int start, length;
            OrderedSelection(out start, out length);
            if (length > 0) return false;
            var text = _bodyBox.Text ?? "";
            var lineStart = LineStart(text, start);
            var lineEnd = LineEnd(text, start);
            if (start != lineEnd) return false; // au milieu de la ligne : une Entrée ordinaire
            var line = text.Substring(lineStart, lineEnd - lineStart);
            var m = Regex.Match(line, @"^(\s*)([-*+]|\d+[.)])(\s+)(\[[ xX]\]\s+)?(.*)$");
            if (!m.Success) return false;
            var indent = m.Groups[1].Value;
            var marker = m.Groups[2].Value;
            var task = m.Groups[4].Success;
            var rest = m.Groups[5].Value;
            if (rest.Trim().Length == 0)
            {
                // Puce vide : on la retire, la ligne redevient nue.
                Ui.Select(_bodyBox, lineStart, lineEnd - lineStart);
                _bodyBox.SelectedText = indent;
                Ui.Select(_bodyBox, lineStart + indent.Length, 0);
                return true;
            }
            var next = marker;
            var number = Regex.Match(marker, @"^(\d+)([.)])$");
            if (number.Success) next = (int.Parse(number.Groups[1].Value) + 1) + number.Groups[2].Value;
            var inserted = "\n" + indent + next + " " + (task ? "[ ] " : "");
            _bodyBox.SelectedText = inserted;
            Ui.Select(_bodyBox, start + inserted.Length, 0);
            return true;
        }

        /// <summary>Entoure la sélection de marqueurs — ou les retire s'ils
        /// sont déjà là, dedans ou juste autour (port de MWG).</summary>
        internal void Wrap(string before, string after)
        {
            LeavePreview();
            int start, length;
            OrderedSelection(out start, out length);
            var text = _bodyBox.Text;
            if (length > 0)
            {
                var selected = _bodyBox.SelectedText;
                if (selected.Length >= before.Length + after.Length
                    && selected.StartsWith(before, StringComparison.Ordinal) && selected.EndsWith(after, StringComparison.Ordinal))
                {
                    _bodyBox.SelectedText = selected.Substring(before.Length, selected.Length - before.Length - after.Length);
                    Ui.Select(_bodyBox, start, length - before.Length - after.Length);
                }
                else if (start >= before.Length && start + length + after.Length <= text.Length
                    && text.Substring(start - before.Length, before.Length) == before
                    && text.Substring(start + length, after.Length) == after)
                {
                    Ui.Select(_bodyBox, start - before.Length, length + before.Length + after.Length);
                    _bodyBox.SelectedText = selected;
                    Ui.Select(_bodyBox, start - before.Length, length);
                }
                else
                {
                    _bodyBox.SelectedText = before + selected + after;
                    Ui.Select(_bodyBox, start + before.Length, length);
                }
            }
            else
            {
                _bodyBox.SelectedText = before + after;
                Ui.Select(_bodyBox, start + before.Length, 0);
            }
            _bodyBox.Focus();
        }

        private int[] LineBlock()
        {
            var text = _bodyBox.Text;
            int start, length;
            OrderedSelection(out start, out length);
            var blockStart = LineStart(text, start);
            var end = start + length;
            if (length > 0 && end > blockStart && LineStart(text, end) == end) end--;
            return new[] { blockStart, LineEnd(text, end) };
        }

        private static int LineStart(string text, int position)
        {
            var i = Math.Min(position, text.Length);
            while (i > 0 && text[i - 1] != '\n') i--;
            return i;
        }

        private static int LineEnd(string text, int position)
        {
            var i = Math.Max(0, Math.Min(position, text.Length));
            while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;
            return i;
        }

        private void TransformLines(Func<string, string> transform)
        {
            LeavePreview();
            var block = LineBlock();
            var text = _bodyBox.Text;
            int selStart, length;
            OrderedSelection(out selStart, out length);
            var lines = text.Substring(block[0], block[1] - block[0]).Split('\n');
            for (var i = 0; i < lines.Length; i++) lines[i] = transform(lines[i].TrimEnd('\r'));
            var replaced = string.Join("\n", lines);
            Ui.Select(_bodyBox, block[0], block[1] - block[0]);
            _bodyBox.SelectedText = replaced;
            if (length > 0) Ui.Select(_bodyBox, block[0], replaced.Length);
            else Ui.Select(_bodyBox, block[0] + replaced.Length, 0);
            _bodyBox.Focus();
        }

        private void ApplyHeading(int level)
        {
            var prefix = new string('#', level) + " ";
            TransformLines(delegate(string line)
            {
                var m = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
                var rest = m.Success ? m.Groups[2].Value : line;
                if (m.Success && m.Groups[1].Value.Length == level) return rest;
                return prefix + rest;
            });
        }

        private void ApplyList(string type)
        {
            var n = 0;
            TransformLines(delegate(string line)
            {
                var m = Regex.Match(line, @"^(\s*)(?:[-*+]|\d+[.)])\s+(?:\[[ xX]\]\s+)?(.*)$");
                var indent = m.Success ? m.Groups[1].Value : Regex.Match(line, @"^\s*").Value;
                var rest = m.Success ? m.Groups[2].Value : line.TrimStart();
                n++;
                if (type == "num") return indent + n + ". " + rest;
                if (type == "tiret") return indent + "- " + rest;
                if (type == "case") return indent + "- [ ] " + rest;
                return indent + "* " + rest;
            });
        }

        private void ApplyQuote()
        {
            TransformLines(delegate(string line)
            {
                return line.StartsWith("> ", StringComparison.Ordinal) ? line.Substring(2) : "> " + line;
            });
        }

        private async void InsertTable()
        {
            var values = await NumbersDialog.Ask(Ui.OwnerOf(this), "Tableau", new[] { "Colonnes", "Lignes" }, new[] { 3.0, 2.0 }, 1, 12);
            if (values == null) return;
            var columns = (int)Math.Round(values[0]);
            var rows = (int)Math.Round(values[1]);
            var sb = new System.Text.StringBuilder();
            sb.Append('\n');
            for (var c = 1; c <= columns; c++) sb.Append("| Colonne " + c + " ");
            sb.Append("|\n");
            for (var c = 1; c <= columns; c++) sb.Append("| --- ");
            sb.Append("|\n");
            for (var r = 0; r < rows; r++)
            {
                for (var c = 1; c <= columns; c++) sb.Append("|   ");
                sb.Append("|\n");
            }
            InsertAtCaret(sb.ToString());
        }

        private void InsertLink()
        {
            LeavePreview();
            int start, length;
            OrderedSelection(out start, out length);
            var selected = _bodyBox.SelectedText;
            if (selected.Length > 0)
            {
                _bodyBox.SelectedText = "[" + selected + "]()";
                Ui.Select(_bodyBox, start + selected.Length + 3, 0);
            }
            else
            {
                _bodyBox.SelectedText = "[]()";
                Ui.Select(_bodyBox, start + 1, 0);
            }
            _bodyBox.Focus();
        }

        private void LeavePreview()
        {
            if (_previewToggle.IsChecked == true) _previewToggle.IsChecked = false;
            ShowTextTab();
        }

        // ================================================== API de la coquille

        public bool HasItem { get { return _item != null; } }
        internal TextBox BodyBox { get { return _bodyBox; } } // sonde (hotfix 1.0.3-a)
        public bool ShowsItem(BinderItem item) { return _item == item; }
        public void SetStyleSheet(StyleSheet styles) { _styles = styles; }
        public void SetProject(Model.Project project) { _project = project; }

        public void LoadItem(BinderItem item, SheetTemplate template)
        {
            _item = item;
            _template = template;
            _loading = true;
            var category = _project == null ? null : _project.SheetCategoryOf(item);
            _portraitIcon.Content = SheetLibraryView.CategoryPlaceholder(category, 48);
            _titleLabel.Text = item.Title;
            _categoryLabel.Text = (category != null ? "Fiche " + category.Name : "Fiche")
                + (template != null ? " — modèle " + template.Name : " (modèle introuvable — champs libres uniquement)");
            RebuildPapers();
            RebuildFields();
            RebuildRelations();
            RebuildPresence();
            RebuildEvolution();
            RebuildRadar();
            RebuildModules();
            RefreshPortrait();
            _bodyBox.Text = item.Document.ToPlainText();
            // La pile d'annulation du TextBox repart de zéro avec le texte
            // chargé (un remplacement projet recharge la fiche : jamais un
            // Ctrl+Z local qui ressusciterait l'état d'avant, b37).
            _bodyBox.IsUndoEnabled = false;
            _bodyBox.IsUndoEnabled = true;
            _loading = false;
            SetMirror(_mirrorMode); // le mode choisi suit d'une fiche à l'autre
            RefreshDictionaryBadge();
            if (_previewToggle.IsChecked == true) ShowPreview();
            SyncGenealogy();
        }

        public void Commit()
        {
            if (_item == null) return;
            _item.Document = TextDocument.FromPlainText(_bodyBox.Text);
        }

        public void Clear()
        {
            _item = null;
            _template = null;
            _loading = true;
            _bodyBox.Text = "";
            _loading = false;
            _portrait.IsVisible = false;
            _portraitPlaceholder.IsVisible = true;
            _removePortrait.IsVisible = false;
            _preview.Content = null;
            RebuildPapers();
            _presencePanel.Children.Clear();
            _evolutionPanel.Children.Clear();
            RebuildRadar();
            RebuildModules();
            SyncGenealogy();
        }

        // ================================================== modules (DLC, 22/09)

        /// <summary>Les modules qui visent la catégorie de la fiche : un
        /// onglet par fiche de module déjà créée, sinon un bouton « Créer une
        /// fiche X » dans le bandeau. Sans module installé : rien.</summary>
        private void RebuildModules()
        {
            foreach (var tab in _moduleTabs)
            {
                if (_tabs.SelectedItem == tab) _tabs.SelectedIndex = 0;
                _tabs.Items.Remove(tab);
            }
            _moduleTabs.Clear();
            _moduleButtons.Children.Clear();
            if (_item == null) return;
            foreach (var module in Modules.ForSheet(_project, _item))
            {
                var moduleRef = module;
                if (Modules.HasSheet(module, _item))
                {
                    var view = new ModuleSheetView();
                    view.Load(module, _item, NotifyEdited);
                    var tab = new TabItem
                    {
                        Header = module.Tab,
                        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = view, Cursor = new Cursor(StandardCursorType.Arrow) }
                    };
                    _tabs.Items.Add(tab);
                    _moduleTabs.Add(tab);
                    continue;
                }
                var create = new Button
                {
                    Content = Icons.Label("plus-bold", module.Button, 13, Chrome.Ink),
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 0, 8, 0),
                    [ToolTip.TipProperty] = module.Title + " — la fiche avancée du module " + module.Name + ", dans un nouvel onglet"
                };
                create.Click += delegate { CreateModuleSheet(moduleRef); };
                _moduleButtons.Children.Add(create);
            }
        }

        /// <summary>« Créer une fiche X » : la fiche naît vide, l'onglet
        /// s'ouvre dessus, le projet est modifié.</summary>
        public void CreateModuleSheet(ModuleInfo module)
        {
            if (_item == null || module == null || Modules.HasSheet(module, _item)) return;
            Modules.ValuesOf(module, _item, true);
            NotifyEdited();
            RebuildModules();
            foreach (var tab in _moduleTabs)
                if ((string)tab.Header == module.Tab) { _tabs.SelectedItem = tab; break; }
        }

        // ================================================== radar (b47 bis)

        /// <summary>L'onglet Radar : présent si le modèle l'active (trois axes
        /// au moins). La toile à gauche, un curseur par axe à droite ; la
        /// toile se redessine à chaque cran.</summary>
        private void RebuildRadar()
        {
            _radarHost.Children.Clear();
            var shown = _item != null && _template != null && _template.ShowsRadar;
            if (!shown)
            {
                if (_tabs.Items.Contains(_radarTab))
                {
                    if (_tabs.SelectedItem == _radarTab) _tabs.SelectedIndex = 0;
                    _tabs.Items.Remove(_radarTab);
                }
                return;
            }
            if (!_tabs.Items.Contains(_radarTab)) _tabs.Items.Add(_radarTab);
            _radarTab.Header = _template.RadarLabel; // le nom du radar du modèle (14/09)
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _radarCanvas = new Canvas { Margin = new Thickness(6) };
            RadarChart.Draw(_radarCanvas, _template, _item.RadarValues, 380, true);
            var web = Paper("Toile", _radarCanvas, null);
            web.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(web, 0);
            layout.Children.Add(web);

            var rows = new StackPanel();
            var max = Math.Max(1, _template.RadarMax);
            foreach (var axis in _template.RadarAxes)
            {
                var axisRef = axis;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var valueLabel = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, MinWidth = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(valueLabel, Dock.Right);
                row.Children.Add(valueLabel);
                var name = new TextBlock { Text = axis.Name, Foreground = Chrome.Ink, FontSize = 12, Width = 120, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                DockPanel.SetDock(name, Dock.Left);
                row.Children.Add(name);
                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = max,
                    TickFrequency = 0.5, // par pas de 0,5 (14/09)
                    IsSnapToTickEnabled = true,
                    SmallChange = 0.5,
                    LargeChange = 1,
                    Value = RadarChart.ValueOf(_item.RadarValues, axis.Id, max),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 10, 0)
                };
                Action relabel = delegate { valueLabel.Text = RadarChart.Format(slider.Value) + " / " + max; };
                relabel();
                slider.ValueChanged += delegate
                {
                    if (_loading || _item == null) return;
                    var value = Math.Round(slider.Value * 2) / 2;
                    if (value == 0) _item.RadarValues.Remove(axisRef.Id);
                    else _item.RadarValues[axisRef.Id] = value;
                    relabel();
                    RadarChart.Draw(_radarCanvas, _template, _item.RadarValues, 380, true);
                    NotifyEdited();
                };
                row.Children.Add(slider);
                rows.Children.Add(row);
            }
            rows.Children.Add(Hint("Chaque axe de 0 à " + max + ", par demi-point — le nom, l'échelle et les axes se règlent dans l'éditeur de modèles."));
            var values = Paper("Valeurs", rows, null);
            values.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(values, 1);
            layout.Children.Add(values);
            _radarHost.Children.Add(layout);
        }

        // ------------------------------------------------------- image

        private async void ChoosePortrait()
        {
            if (_item == null || _project == null) return;
            var dialogPath = await Ui.PickOpenFile(this, "", "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp");
            if (dialogPath == null) return;
            try
            {
                var info = new System.IO.FileInfo(dialogPath);
                if (info.Length > 20 * 1024 * 1024)
                    throw new InvalidOperationException("image de plus de 20 Mo — réduisez-la d'abord.");
                var bytes = System.IO.File.ReadAllBytes(dialogPath);
                SetPortrait(_project.AddImage(bytes, System.IO.Path.GetExtension(dialogPath)));
            }
            catch (Exception error)
            {
                MessageDialog.Show(Ui.OwnerOf(this), "Image refusée : " + error.Message,
                    "Marabook", MessageButtons.OK, MessageIcon.Warning);
            }
        }

        private void SetPortrait(string imageId)
        {
            if (_item == null) return;
            _item.ImageId = imageId; // les octets abandonnés sont purgés à l'enregistrement
            RefreshPortrait();
            NotifyEdited();
            if (_previewToggle.IsChecked == true) ShowPreview();
        }

        private void RefreshPortrait()
        {
            var image = _project == null || _item == null ? null : _project.FindImage(_item.ImageId);
            var source = image == null ? null : MediaView.TryImage(image.Bytes, 320);
            _portrait.Source = source;
            _portrait.IsVisible = source == null ? false : true;
            _portraitPlaceholder.IsVisible = source == null ? true : false;
            _removePortrait.IsVisible = _portrait.IsVisible;
        }

        // ------------------------------------------------------- mode wiki

        private void ShowPreview()
        {
            if (_item == null) return;
            _preview.Content = BuildPreviewContent();
            _preview.IsVisible = true;
            _body.IsVisible = false;
        }

        private void HidePreview()
        {
            _preview.IsVisible = false;
            _preview.Content = null;
            _body.IsVisible = true;
        }

        /// <summary>Rendu wiki (SheetWiki, partagé avec l'épinglé du rail — b47) :
        /// grand titre, infobox à droite, corps markdown.</summary>
        private Control BuildPreviewContent()
        {
            return SheetWiki.Build(_item, _template, _project, _bodyBox.Text, false,
                delegate(BinderItem target) { var h = NavigateRequested; if (h != null) h(target); },
                delegate(string target) { var handler = LinkClicked; if (handler != null) handler(target); },
                ToggleTask);
        }

        private void ToggleTask(int index)
        {
            var position = MarkdownDialect.FindTask(_bodyBox.Text, index);
            if (position < 0 || position >= (_bodyBox.Text ?? "").Length) return;
            var text = _bodyBox.Text;
            var current = text[position];
            _bodyBox.Text = text.Substring(0, position) + (current == ' ' ? 'x' : ' ') + text.Substring(position + 1);
            if (_previewToggle.IsChecked == true) ShowPreview();
        }

        // ------------------------------------------------------- surface

        public void ReloadBody()
        {
            if (_previewToggle.IsChecked == true) ShowPreview();
        }

        public string BodyPlainText() { return _bodyBox.Text; }

        public void SetZoom(double factor)
        {
            _zoom = Math.Max(0.5, Math.Min(3.0, factor));
            _bodyBox.FontSize = BodyFontSize * _zoom;
        }

        public bool TryUndo()
        {
            if (!_bodyBox.IsFocused || !_bodyBox.CanUndo) return false;
            _bodyBox.Undo();
            return true;
        }

        public bool TryRedo()
        {
            if (!_bodyBox.IsFocused || !_bodyBox.CanRedo) return false;
            _bodyBox.Redo();
            return true;
        }

        public void ApplyPageSetup(PageSetup setup) { }
        public void SetFormattingMarks(bool visible) { }
        public void UpdateRulers() { }
        public void SetCalm(bool calm) { }

        public void ShowSearch()
        {
            LeavePreview();
            _findBar.IsVisible = true;
            _findBox.Focus();
            _findBox.SelectAll();
        }

        private void HideSearch()
        {
            _findBar.IsVisible = false;
            _bodyBox.Focus();
        }

        private void FindNext()
        {
            var needle = _findBox.Text;
            if (needle.Length == 0) return;
            var from = _bodyBox.SelectionStart + (_bodyBox.SelectionEnd - _bodyBox.SelectionStart);
            var index = (_bodyBox.Text ?? "").IndexOf(needle, from, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) index = (_bodyBox.Text ?? "").IndexOf(needle, 0, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) return;
            _bodyBox.Focus();
            Ui.Select(_bodyBox, index, needle.Length);
            var line = Ui.LineOf(_bodyBox, index);
            Ui.ScrollToLine(_bodyBox, Math.Max(0, line));
            _findBox.Focus();
        }

        public void InsertFootnote() { } // les fiches n'ont pas de notes de bas de page
        /// <summary>Le [[lien]] garde l'expression sélectionnée comme texte
        /// (« [[Cible|expression]] », 18/09).</summary>
        public void InsertWikiLink(string title) { InsertAtCaret(Links.Markup(title, _bodyBox.SelectedText)); }
        public void InsertImage() { InsertAtCaret("![description](adresse)"); }
        public void InsertRule() { InsertAtCaret("\n---\n"); }
        public void InsertSeparator()
        {
            // Le contenu du séparateur global (22/09) — une fiche n'est dans aucun livre.
            var separator = _styles == null ? null : _styles.SeparatorFor(null);
            InsertAtCaret("\n" + (separator == null || separator.Content == null ? "***" : separator.Content) + "\n");
        }

        private void InsertAtCaret(string text)
        {
            if (_item == null) return;
            LeavePreview();
            var at = _bodyBox.SelectionStart;
            _bodyBox.Text = (_bodyBox.Text ?? "").Substring(0, at) + text + (_bodyBox.Text ?? "").Substring(at + (_bodyBox.SelectionEnd - _bodyBox.SelectionStart));
            _bodyBox.SelectionStart = at + text.Length;
            _bodyBox.Focus();
        }

        private void NotifyEdited()
        {
            if (_loading) return;
            var handler = Edited;
            if (handler != null) handler();
            if (_genealogy != null && _item != null && _genealogy.Shows(_item)) _genealogy.Refresh();
        }
    }
}

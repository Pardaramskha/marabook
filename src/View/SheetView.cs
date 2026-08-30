using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using UniversSale.Model;

namespace UniversSale.View
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
        private readonly TabControl _tabs;         // Général | Texte libre (b36)
        private readonly Button _genealogyButton;
        private GenealogyWindow _genealogy;        // le paper flottant (b36)
        private readonly ScrollViewer _preview;
        private readonly StackPanel _infoFields, _looksFields, _relationsPanel;
        private readonly Image _portrait;
        private readonly TextBlock _portraitPlaceholder;
        private readonly Button _removePortrait;
        private readonly TextBox _bodyBox;
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
                ToolTip = "Revenir au tableau (corkboard) de la fiche",
                VerticalAlignment = VerticalAlignment.Center
            };
            back.Click += delegate { var h = BackRequested; if (h != null) h(); };
            DockPanel.SetDock(back, Dock.Left);
            bannerRow.Children.Add(back);

            var rightTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _previewToggle = new ToggleButton
            {
                Content = "📖  Mode wiki",
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(12, 4, 12, 4),
                ToolTip = "Voir la fiche en lecture, comme une page de wiki"
            };
            _previewToggle.Checked += delegate { ShowPreview(); };
            _previewToggle.Unchecked += delegate { HidePreview(); };
            rightTools.Children.Add(_previewToggle);
            _genealogyButton = new Button
            {
                Content = Icons.Label("tree-bold", "Généalogie", 13, Chrome.Ink),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "L'arbre généalogique de la fiche, d'après ses relations (paper flottant)"
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
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _categoryLabel = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11 };
            titles.Children.Add(_titleLabel);
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
            _portrait = new Image { MaxHeight = 260, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
            _portraitPlaceholder = new TextBlock
            {
                Text = "🖼\nAucune image",
                TextAlignment = TextAlignment.Center,
                Foreground = Chrome.SoftText,
                FontSize = 13,
                Margin = new Thickness(0, 26, 0, 26)
            };
            var portraitStack = new StackPanel();
            portraitStack.Children.Add(_portrait);
            portraitStack.Children.Add(_portraitPlaceholder);
            var portraitOptions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var setPortrait = new Button { Content = "Image principale…", Padding = new Thickness(8, 3, 8, 3), ToolTip = "Portrait affiché sur la fiche et sur sa carte" };
            setPortrait.Click += delegate { ChoosePortrait(); };
            portraitOptions.Children.Add(setPortrait);
            _removePortrait = new Button { Content = "Retirer", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed };
            _removePortrait.Click += delegate { SetPortrait(null); };
            portraitOptions.Children.Add(_removePortrait);
            var imageContent = new StackPanel();
            imageContent.Children.Add(portraitStack);
            imageContent.Children.Add(portraitOptions);
            var imagePaper = Paper("Image", imageContent, null);

            // Informations.
            _infoFields = new StackPanel();
            var addInfo = AddButton("Ajouter un champ");
            addInfo.Click += delegate { AddFreeField(""); };
            var infoPaper = Paper("Informations", _infoFields, addInfo);

            // Apparence.
            _looksFields = new StackPanel();
            var addLooks = AddButton("Ajouter un champ");
            addLooks.Click += delegate { AddFreeField(SheetDefaults.GroupLooks); };
            var looksPaper = Paper("Apparence", _looksFields, addLooks);

            // Relations.
            _relationsPanel = new StackPanel();
            var addRelation = AddButton("Ajouter une relation");
            addRelation.Click += delegate { AddRelation(); };
            var relationsPaper = Paper("Relations", _relationsPanel, addRelation);

            // Trois modes selon la largeur des papers : large (≥ 900 px) =
            // les trois colonnes ; moyen (≥ 560 px) = deux colonnes (image +
            // informations | apparence + relations, la colonne vide cède) ;
            // étroit = une colonne, les papers empilés — trois colonnes de
            // 200 px ne montreraient plus rien.
            var mode = -1;
            papers.SizeChanged += delegate
            {
                var wantMode = papers.ActualWidth >= 900 ? 3 : papers.ActualWidth >= 560 ? 2 : 1;
                if (wantMode == mode) return;
                mode = wantMode;
                papers.ColumnDefinitions[1].Width = mode >= 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                papers.ColumnDefinitions[2].Width = mode >= 3 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                foreach (var column in columns) column.Children.Clear();
                columns[0].Children.Add(imagePaper);
                columns[0].Children.Add(infoPaper);
                var second = mode == 1 ? columns[0] : columns[1];
                second.Children.Add(looksPaper);
                second.Children.Add(relationsPaper);
            };
            var generalScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = papers };
            _tabs.Items.Add(new TabItem { Header = "Général", Content = generalScroll });

            // — Texte libre : l'éditeur markdown.
            _bodyBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
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
            _bodyBox.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                e.Handled = true;
                var handler = ZoomStepRequested;
                if (handler != null) handler(e.Delta > 0 ? 10 : -10);
            };
            _bodyBox.PreviewKeyDown += OnBodyKeyDown;

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
            _findBar = new DockPanel { Margin = new Thickness(14, 6, 14, 2), Visibility = Visibility.Collapsed, LastChildFill = false };
            _findBar.Children.Add(new TextBlock { Text = "Rechercher :", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center });
            _findBar.Children.Add(_findBox);
            _findBar.Children.Add(findNext);
            _findBar.Children.Add(findClose);

            var editorStack = new DockPanel();
            var toolbar = BuildMarkdownToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            editorStack.Children.Add(toolbar);
            DockPanel.SetDock(_findBar, Dock.Top);
            editorStack.Children.Add(_findBar);
            editorStack.Children.Add(_bodyBox);
            var editorPaper = new Border
            {
                Background = Chrome.PaperBg,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(8, 12, 8, 14),
                Effect = Shadow(),
                Child = editorStack,
                ClipToBounds = false
            };
            _tabs.Items.Add(new TabItem { Header = "Texte libre", Content = editorPaper });

            _body = new Grid();
            _body.Children.Add(_tabs);

            _preview = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };

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
            return new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.14, Color = Colors.Black };
        }

        private static Border Paper(string caption, UIElement content, UIElement action)
        {
            var stack = new StackPanel();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock
            {
                Text = caption,
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
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
            return new Border
            {
                Background = Chrome.CardBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(6, 8, 6, 8),
                Effect = Shadow(),
                Child = stack,
                VerticalAlignment = VerticalAlignment.Top
            };
        }

        // ================================================== champs

        private static bool IsLooks(string group)
        {
            return string.Equals(group, SheetDefaults.GroupLooks, StringComparison.CurrentCultureIgnoreCase);
        }

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
                var index = Math.Min(PlainOffset(field.ParagraphIndex) + start, _bodyBox.Text.Length);
                _bodyBox.Focus();
                _bodyBox.Select(index, Math.Max(0, Math.Min(length, _bodyBox.Text.Length - index)));
                _bodyBox.ScrollToLine(Math.Max(0, _bodyBox.GetLineIndexFromCharacterIndex(index)));
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
                var max = textBox.Text.Length;
                var at = Math.Min(start, max);
                textBox.Select(at, Math.Max(0, Math.Min(length, max - at)));
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
                    offset += run.IsLineBreak ? 1 : run.Text.Length;
                }
                offset++; // le saut de ligne entre paragraphes
            }
            return offset;
        }

        private void RebuildFields()
        {
            _infoFields.Children.Clear();
            _looksFields.Children.Clear();
            _fieldBoxes.Clear();
            if (_item == null) return;
            if (_template != null)
                foreach (var field in _template.Fields)
                {
                    string value;
                    _item.FieldValues.TryGetValue(field.Id, out value);
                    var fieldRef = field;
                    var row = FieldRow(field.Name, value ?? "", field.Kind == "multiline", delegate(string text)
                    {
                        _item.FieldValues[fieldRef.Id] = text;
                    }, null, field.Id);
                    (IsLooks(field.Group) ? _looksFields : _infoFields).Children.Add(row);
                }
            foreach (var entry in _item.FreeInfo)
                (IsLooks(entry.Group) ? _looksFields : _infoFields).Children.Add(FreeFieldRow(entry));
            if (_infoFields.Children.Count == 0) _infoFields.Children.Add(Hint("Aucune information — ajoutez un champ."));
            if (_looksFields.Children.Count == 0) _looksFields.Children.Add(Hint("Aucun trait d'apparence — ajoutez un champ."));
        }

        private static TextBlock Hint(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        }

        /// <summary>Une rangée libellé / valeur ; remove = bouton ✕ optionnel.</summary>
        private UIElement FieldRow(string label, string value, bool multiline, Action<string> onChanged, Button remove, string refId)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            if (remove != null) { DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove); }
            var caption = new TextBlock
            {
                Text = label,
                Width = 92,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            var box = new TextBox { Text = value };
            if (refId != null) _fieldBoxes[refId] = box;
            if (multiline)
            {
                box.AcceptsReturn = true;
                box.TextWrapping = TextWrapping.Wrap;
                box.MinHeight = 52;
                box.VerticalContentAlignment = VerticalAlignment.Top;
            }
            box.TextChanged += delegate
            {
                if (_loading || _item == null) return;
                onChanged(box.Text);
                NotifyEdited();
            };
            row.Children.Add(box);
            return row;
        }

        private UIElement FreeFieldRow(InfoEntry entry)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var remove = new Button { Content = "✕", Width = 26, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Supprimer ce champ" };
            remove.Click += delegate
            {
                _item.FreeInfo.Remove(entry);
                RebuildFields();
                NotifyEdited();
            };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            var titleBox = new TextBox { Text = entry.Title, Width = 92, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Nom du champ" };
            titleBox.TextChanged += delegate
            {
                if (_loading) return;
                entry.Title = titleBox.Text;
                NotifyEdited();
            };
            DockPanel.SetDock(titleBox, Dock.Left);
            row.Children.Add(titleBox);
            var valueBox = new TextBox { Text = entry.Value };
            _fieldBoxes[entry.Id] = valueBox;
            valueBox.TextChanged += delegate
            {
                if (_loading) return;
                entry.Value = valueBox.Text;
                NotifyEdited();
            };
            row.Children.Add(valueBox);
            return row;
        }

        private void AddFreeField(string group)
        {
            if (_item == null) return;
            var entry = new InfoEntry { Title = "Champ", Group = group ?? "" };
            _item.FreeInfo.Add(entry);
            RebuildFields();
            NotifyEdited();
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
        private UIElement RelationRow(SheetRelation relation, List<BinderItem> sheets)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var remove = new Button { Content = "✕", Width = 26, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Supprimer cette relation (et son reflet sur la fiche liée)" };
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
                ToolTip = "Nature de la relation — une nature livrée, une nature du projet, ou « Nouvelle nature… » pour en créer une"
            };
            if (_project != null) foreach (var kind in _project.AllRelationKinds()) kindBox.Items.Add(kind);
            else foreach (var kind in RelationKinds.Defaults) kindBox.Items.Add(kind);
            kindBox.Items.Add(NewKindEntry);
            kindBox.Text = relation.Kind;
            kindBox.SelectionChanged += delegate
            {
                if (_loading || kindBox.SelectedIndex < 0) return;
                var chosen = kindBox.SelectedItem as string;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(delegate
                    {
                        if (chosen == NewKindEntry) PromptNewKind(relation, kindBox);
                        else CommitRelationKind(relation, kindBox);
                    }));
            };
            kindBox.LostKeyboardFocus += delegate
            {
                if ((kindBox.Text ?? "") != NewKindEntry) CommitRelationKind(relation, kindBox);
            };
            DockPanel.SetDock(kindBox, Dock.Left);
            row.Children.Add(kindBox);
            var arrow = Icons.Make("arrow-right-bold", 11, Chrome.SoftText) as FrameworkElement;
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
                Visibility = target != null ? Visibility.Visible : Visibility.Collapsed,
                ToolTip = "Ouvrir la fiche liée"
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
            var combo = new ComboBox { IsEditable = true, ToolTip = "Une fiche du projet, ou un nom libre" };
            _relationBoxes[relation.Id] = combo;
            foreach (var sheet in sheets) combo.Items.Add(sheet.Title);
            combo.Text = target != null ? target.Title : relation.Name;
            combo.LostKeyboardFocus += delegate { CommitRelationTarget(relation, combo, sheets, open); };
            combo.SelectionChanged += delegate
            {
                if (_loading || combo.SelectedIndex < 0) return;
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(delegate { CommitRelationTarget(relation, combo, sheets, open); }));
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
        private void PromptNewKind(SheetRelation relation, ComboBox kindBox)
        {
            var name = _project == null ? null : InputDialog.Ask(Window.GetWindow(this),
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
            open.Visibility = match != null ? Visibility.Visible : Visibility.Collapsed;
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
        private void ShowGenealogy()
        {
            if (_item == null) return;
            if (_genealogy == null)
            {
                _genealogy = new GenealogyWindow(Window.GetWindow(this));
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

        private UIElement BuildMarkdownToolbar()
        {
            var bar = new WrapPanel { Margin = new Thickness(10, 8, 10, 4) };
            bar.Children.Add(Tool("G", "Gras (Ctrl+B)", FontWeights.Bold, FontStyles.Normal, delegate { Wrap("**", "**"); }));
            bar.Children.Add(Tool("I", "Italique (Ctrl+I)", FontWeights.Normal, FontStyles.Italic, delegate { Wrap("*", "*"); }));
            bar.Children.Add(Tool("S", "Souligné (Ctrl+U)", FontWeights.Normal, FontStyles.Normal, delegate { Wrap("<u>", "</u>"); }, TextDecorations.Underline));
            bar.Children.Add(Tool("B", "Barré", FontWeights.Normal, FontStyles.Normal, delegate { Wrap("~~", "~~"); }, TextDecorations.Strikethrough));
            bar.Children.Add(Gap());
            bar.Children.Add(Tool("H1", "Titre de niveau 1", FontWeights.SemiBold, FontStyles.Normal, delegate { ApplyHeading(1); }));
            bar.Children.Add(Tool("H2", "Titre de niveau 2", FontWeights.SemiBold, FontStyles.Normal, delegate { ApplyHeading(2); }));
            bar.Children.Add(Tool("H3", "Titre de niveau 3", FontWeights.SemiBold, FontStyles.Normal, delegate { ApplyHeading(3); }));
            bar.Children.Add(Gap());
            bar.Children.Add(Tool("•", "Liste à puces", FontWeights.Normal, FontStyles.Normal, delegate { ApplyList("puce"); }));
            bar.Children.Add(Tool("1.", "Liste numérotée", FontWeights.Normal, FontStyles.Normal, delegate { ApplyList("num"); }));
            bar.Children.Add(Tool("–", "Liste à tirets", FontWeights.Normal, FontStyles.Normal, delegate { ApplyList("tiret"); }));
            bar.Children.Add(Tool("☑", "Liste de tâches (cases à cocher)", FontWeights.Normal, FontStyles.Normal, delegate { ApplyList("case"); }));
            bar.Children.Add(Tool("❝", "Citation", FontWeights.Normal, FontStyles.Normal, delegate { ApplyQuote(); }));
            bar.Children.Add(Gap());
            bar.Children.Add(Tool("⊞", "Insérer un tableau", FontWeights.Normal, FontStyles.Normal, delegate { InsertTable(); }));
            bar.Children.Add(Tool("―", "Filet horizontal", FontWeights.Normal, FontStyles.Normal, delegate { InsertRule(); }));
            bar.Children.Add(Tool("🔗", "Lien hypertexte", FontWeights.Normal, FontStyles.Normal, delegate { InsertLink(); }));
            bar.Children.Add(Tool("🖼", "Image", FontWeights.Normal, FontStyles.Normal, delegate { InsertImage(); }));
            bar.Children.Add(Tool("[[ ]]", "Lien vers une fiche (Ctrl+K)", FontWeights.Normal, FontStyles.Normal, delegate { Wrap("[[", "]]"); }));
            return bar;
        }

        private static UIElement Gap()
        {
            return new Border { Width = 1, Height = 18, Background = Chrome.Border, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        }

        private Button Tool(string label, string tooltip, FontWeight weight, FontStyle style, Action action)
        {
            return Tool(label, tooltip, weight, style, action, null);
        }

        private Button Tool(string label, string tooltip, FontWeight weight, FontStyle style, Action action, TextDecorationCollection decorations)
        {
            var text = new TextBlock { Text = label, FontWeight = weight, FontStyle = style, FontSize = 12 };
            if (decorations != null) text.TextDecorations = decorations;
            var button = new Button
            {
                Content = text,
                MinWidth = 28,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 3, 3),
                ToolTip = tooltip,
                Focusable = false
            };
            button.Click += delegate { if (_item != null) action(); };
            return button;
        }

        private void OnBodyKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.B) { Wrap("**", "**"); e.Handled = true; }
            else if (e.Key == Key.I) { Wrap("*", "*"); e.Handled = true; }
            else if (e.Key == Key.U) { Wrap("<u>", "</u>"); e.Handled = true; }
        }

        /// <summary>Entoure la sélection de marqueurs — ou les retire s'ils
        /// sont déjà là, dedans ou juste autour (port de MWG).</summary>
        private void Wrap(string before, string after)
        {
            LeavePreview();
            var start = _bodyBox.SelectionStart;
            var length = _bodyBox.SelectionLength;
            var text = _bodyBox.Text;
            if (length > 0)
            {
                var selected = _bodyBox.SelectedText;
                if (selected.Length >= before.Length + after.Length
                    && selected.StartsWith(before, StringComparison.Ordinal) && selected.EndsWith(after, StringComparison.Ordinal))
                {
                    _bodyBox.SelectedText = selected.Substring(before.Length, selected.Length - before.Length - after.Length);
                    _bodyBox.Select(start, length - before.Length - after.Length);
                }
                else if (start >= before.Length && start + length + after.Length <= text.Length
                    && text.Substring(start - before.Length, before.Length) == before
                    && text.Substring(start + length, after.Length) == after)
                {
                    _bodyBox.Select(start - before.Length, length + before.Length + after.Length);
                    _bodyBox.SelectedText = selected;
                    _bodyBox.Select(start - before.Length, length);
                }
                else
                {
                    _bodyBox.SelectedText = before + selected + after;
                    _bodyBox.Select(start + before.Length, length);
                }
            }
            else
            {
                _bodyBox.SelectedText = before + after;
                _bodyBox.Select(start + before.Length, 0);
            }
            _bodyBox.Focus();
        }

        private int[] LineBlock()
        {
            var text = _bodyBox.Text;
            var start = _bodyBox.SelectionStart;
            var length = _bodyBox.SelectionLength;
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
            var length = _bodyBox.SelectionLength;
            var lines = text.Substring(block[0], block[1] - block[0]).Split('\n');
            for (var i = 0; i < lines.Length; i++) lines[i] = transform(lines[i].TrimEnd('\r'));
            var replaced = string.Join("\n", lines);
            _bodyBox.Select(block[0], block[1] - block[0]);
            _bodyBox.SelectedText = replaced;
            if (length > 0) _bodyBox.Select(block[0], replaced.Length);
            else _bodyBox.Select(block[0] + replaced.Length, 0);
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

        private void InsertTable()
        {
            var values = NumbersDialog.Ask(Window.GetWindow(this), "Tableau", new[] { "Colonnes", "Lignes" }, new[] { 3.0, 2.0 }, 1, 12);
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
            var start = _bodyBox.SelectionStart;
            var selected = _bodyBox.SelectedText;
            if (selected.Length > 0)
            {
                _bodyBox.SelectedText = "[" + selected + "]()";
                _bodyBox.Select(start + selected.Length + 3, 0);
            }
            else
            {
                _bodyBox.SelectedText = "[]()";
                _bodyBox.Select(start + 1, 0);
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
        public bool ShowsItem(BinderItem item) { return _item == item; }
        public void SetStyleSheet(StyleSheet styles) { _styles = styles; }
        public void SetProject(Model.Project project) { _project = project; }

        public void LoadItem(BinderItem item, SheetTemplate template)
        {
            _item = item;
            _template = template;
            _loading = true;
            var category = _project == null ? null : _project.SheetCategoryOf(item);
            _titleLabel.Text = item.Title;
            _categoryLabel.Text = (category != null ? "Fiche " + category.Name : "Fiche")
                + (template != null ? " — modèle " + template.Name : " (modèle introuvable — champs libres uniquement)");
            RebuildFields();
            RebuildRelations();
            RefreshPortrait();
            _bodyBox.Text = item.Document.ToPlainText();
            // La pile d'annulation du TextBox repart de zéro avec le texte
            // chargé (un remplacement projet recharge la fiche : jamais un
            // Ctrl+Z local qui ressusciterait l'état d'avant, b37).
            _bodyBox.IsUndoEnabled = false;
            _bodyBox.IsUndoEnabled = true;
            _loading = false;
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
            _portrait.Visibility = Visibility.Collapsed;
            _portraitPlaceholder.Visibility = Visibility.Visible;
            _removePortrait.Visibility = Visibility.Collapsed;
            _preview.Content = null;
            SyncGenealogy();
        }

        // ------------------------------------------------------- image

        private void ChoosePortrait()
        {
            if (_item == null || _project == null) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var info = new System.IO.FileInfo(dialog.FileName);
                if (info.Length > 20 * 1024 * 1024)
                    throw new InvalidOperationException("image de plus de 20 Mo — réduisez-la d'abord.");
                var bytes = System.IO.File.ReadAllBytes(dialog.FileName);
                SetPortrait(_project.AddImage(bytes, System.IO.Path.GetExtension(dialog.FileName)));
            }
            catch (Exception error)
            {
                MessageBox.Show(Window.GetWindow(this), "Image refusée : " + error.Message,
                    "Marabook", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            _portrait.Visibility = source == null ? Visibility.Collapsed : Visibility.Visible;
            _portraitPlaceholder.Visibility = source == null ? Visibility.Visible : Visibility.Collapsed;
            _removePortrait.Visibility = _portrait.Visibility;
        }

        // ------------------------------------------------------- mode wiki

        private void ShowPreview()
        {
            if (_item == null) return;
            _preview.Content = BuildPreviewContent();
            _preview.Visibility = Visibility.Visible;
            _body.Visibility = Visibility.Collapsed;
        }

        private void HidePreview()
        {
            _preview.Visibility = Visibility.Collapsed;
            _preview.Content = null;
            _body.Visibility = Visibility.Visible;
        }

        /// <summary>Rendu wiki : grand titre, infobox à droite (portrait,
        /// champs remplis groupés, champs libres, relations), corps markdown.</summary>
        private UIElement BuildPreviewContent()
        {
            var page = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(24, 20, 24, 20),
                Padding = new Thickness(28),
                MaxWidth = 900
            };
            var layout = new DockPanel { LastChildFill = true };

            var infobox = new StackPanel();
            var image = _project == null ? null : _project.FindImage(_item.ImageId);
            var source = image == null ? null : MediaView.TryImage(image.Bytes, 480);
            if (source != null)
                infobox.Children.Add(new Image { Source = source, Stretch = Stretch.Uniform, MaxHeight = 240, Margin = new Thickness(0, 0, 0, 10) });
            if (_template != null)
            {
                string lastGroup = null;
                foreach (var field in _template.Fields)
                {
                    string value;
                    _item.FieldValues.TryGetValue(field.Id, out value);
                    if (string.IsNullOrEmpty(value)) continue;
                    if (field.Group.Length > 0 && field.Group != lastGroup)
                        infobox.Children.Add(GroupCaption(field.Group));
                    lastGroup = field.Group.Length > 0 ? field.Group : lastGroup;
                    AddInfoboxRow(infobox, field.Name, value);
                }
            }
            foreach (var entry in _item.FreeInfo)
                if (!string.IsNullOrEmpty(entry.Value))
                    AddInfoboxRow(infobox, entry.Title, entry.Value);
            if (_item.Relations.Count > 0)
            {
                // Format wiki (b36) : NOM (NATURE) — le nom cliquable quand
                // c'est une fiche.
                infobox.Children.Add(GroupCaption("Relations"));
                foreach (var relation in _item.Relations)
                {
                    var target = _project == null || relation.TargetId == null ? null : _project.FindById(relation.TargetId);
                    var label = target != null ? target.Title : relation.Name;
                    var kind = RelationKinds.Canonical(relation.Kind);
                    if (label.Length == 0 && kind.Length == 0) continue;
                    var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                    if (target != null)
                    {
                        var anchor = new System.Windows.Documents.Run(label) { Foreground = Chrome.Accent, Cursor = Cursors.Hand };
                        var targetRef = target;
                        anchor.MouseLeftButtonDown += delegate { var h = NavigateRequested; if (h != null) h(targetRef); };
                        line.Inlines.Add(anchor);
                    }
                    else line.Inlines.Add(new System.Windows.Documents.Run(label) { Foreground = Chrome.Ink });
                    if (kind.Length > 0)
                        line.Inlines.Add(new System.Windows.Documents.Run((label.Length > 0 ? " (" : "(") + kind + ")") { Foreground = Chrome.SoftText });
                    infobox.Children.Add(line);
                }
            }
            if (infobox.Children.Count > 0)
            {
                var infoboxFrame = new Border
                {
                    Background = Chrome.BarBgLight,
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12),
                    Width = 250,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(20, 6, 0, 0),
                    Child = infobox
                };
                DockPanel.SetDock(infoboxFrame, Dock.Right);
                layout.Children.Add(infoboxFrame);
            }

            var main = new StackPanel();
            main.Children.Add(new TextBlock { Text = _item.Title, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Chrome.PaperInk, TextWrapping = TextWrapping.Wrap });
            var category = _project == null ? null : _project.SheetCategoryOf(_item);
            main.Children.Add(new TextBlock
            {
                Text = category != null ? category.Name : _template != null ? _template.Name : "Fiche",
                FontSize = 12,
                Foreground = Chrome.PaperSoftInk,
                Margin = new Thickness(0, 2, 0, 8)
            });
            main.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 0, 0, 12) });
            var flow = MarkdownRender.Build(_bodyBox.Text,
                delegate(string target) { var handler = LinkClicked; if (handler != null) handler(target); },
                ToggleTask);
            main.Children.Add(new FlowDocumentScrollViewer
            {
                Document = flow,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsToolBarVisible = false,
                Focusable = false
            });
            layout.Children.Add(main);
            page.Child = layout;
            return page;
        }

        private static TextBlock GroupCaption(string text)
        {
            return new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Chrome.Accent, Margin = new Thickness(0, 8, 0, 0) };
        }

        private void ToggleTask(int index)
        {
            var position = MarkdownDialect.FindTask(_bodyBox.Text, index);
            if (position < 0 || position >= _bodyBox.Text.Length) return;
            var text = _bodyBox.Text;
            var current = text[position];
            _bodyBox.Text = text.Substring(0, position) + (current == ' ' ? 'x' : ' ') + text.Substring(position + 1);
            if (_previewToggle.IsChecked == true) ShowPreview();
        }

        private static void AddInfoboxRow(StackPanel infobox, string label, string value)
        {
            infobox.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Chrome.SoftText, Margin = new Thickness(0, 4, 0, 0) });
            infobox.Children.Add(new TextBlock { Text = value, FontSize = 12, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap });
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
            _findBar.Visibility = Visibility.Visible;
            _findBox.Focus();
            _findBox.SelectAll();
        }

        private void HideSearch()
        {
            _findBar.Visibility = Visibility.Collapsed;
            _bodyBox.Focus();
        }

        private void FindNext()
        {
            var needle = _findBox.Text;
            if (needle.Length == 0) return;
            var from = _bodyBox.SelectionStart + _bodyBox.SelectionLength;
            var index = _bodyBox.Text.IndexOf(needle, from, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) index = _bodyBox.Text.IndexOf(needle, 0, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) return;
            _bodyBox.Focus();
            _bodyBox.Select(index, needle.Length);
            var line = _bodyBox.GetLineIndexFromCharacterIndex(index);
            _bodyBox.ScrollToLine(Math.Max(0, line));
            _findBox.Focus();
        }

        public void InsertFootnote() { } // les fiches n'ont pas de notes de bas de page
        public void InsertWikiLink(string title) { InsertAtCaret("[[" + title + "]]"); }
        public void InsertImage() { InsertAtCaret("![description](adresse)"); }
        public void InsertRule() { InsertAtCaret("\n---\n"); }
        public void InsertSeparator() { InsertAtCaret("\n***\n"); }

        private void InsertAtCaret(string text)
        {
            if (_item == null) return;
            LeavePreview();
            var at = _bodyBox.SelectionStart;
            _bodyBox.Text = _bodyBox.Text.Substring(0, at) + text + _bodyBox.Text.Substring(at + _bodyBox.SelectionLength);
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

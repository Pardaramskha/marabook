using System;
using System.Threading.Tasks;
using System.Collections.Generic;
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
    /// <summary>L'écran d'un PLAN (batch 35) : des colonnes de gauche à
    /// droite (défilement horizontal), chacune titrée et reliable à un
    /// écrit ; dedans, des ÉLÉMENTS (brique colorée, intensité 1–5) et des
    /// NOTES (post-it), réordonnés à la souris ; en tête, le nom, le lien du
    /// plan vers un livre ou un dossier, et le graphique d'intensité.</summary>
    public class PlanView : DockPanel
    {
        private const string DragFormat = "MarabookPlanEntry";
        private static readonly IBrush NoteBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xA8));
        private static readonly IBrush NoteInk = new SolidColorBrush(Color.FromRgb(0x5C, 0x4A, 0x10));

        private readonly TextBlock _title;
        private readonly ComboBox _linkCombo;
        private readonly Button _openLinked;
        private readonly StackPanel _columns;
        private readonly ScrollViewer _scroll;
        private readonly PlanChart _chart;           // la vue « Intensité » (30/09)
        private readonly ToggleButton _chartToggle;

        private BinderItem _item;
        private Project _project;
        private bool _loading;
        private List<BinderItem> _linkTargets = new List<BinderItem>();
        private List<BinderItem> _texts = new List<BinderItem>();

        // Glisser d'une brique : candidate au clic, partie au-delà du seuil.
        private PlanEntry _dragCandidate;
        private PlanColumn _dragColumn;
        private Point _dragStart;
        private Border _dropTarget;
        private InsertionAdorner _dropAdorner;

        public event Action Edited;                          // le modèle a changé
        public event Action<BinderItem> NavigateRequested;   // ouvrir un écrit / livre / dossier
        public event Action BackRequested;                   // ← la carte des plans
        public event Action RenameRequested;                 // le crayon à côté du nom (14/09)

        public PlanView()
        {
            Background = Chrome.WindowBg;
            Focusable = true;

            // ================================================== en-tête
            var head = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8)
            };
            SetDock(head, Dock.Top);
            var row = new DockPanel();

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var addColumn = new Button { Content = Icons.Label("plus-bold", "Nouvelle colonne", 11, Chrome.Ink), Padding = new Thickness(10, 4, 10, 4) };
            addColumn.Click += delegate { AddColumn(); };
            right.Children.Add(addColumn);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var back = new Button
            {
                Content = Icons.Make("arrow-left-bold", 12, Chrome.Ink),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 12, 0),
                [ToolTip.TipProperty] = "Revenir à la carte des plans",
                VerticalAlignment = VerticalAlignment.Center
            };
            back.Click += delegate { var h = BackRequested; if (h != null) h(); };
            left.Children.Add(back);
            _title = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 17,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 360,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            left.Children.Add(_title);
            // Le crayon (14/09) : renommer le plan sans passer par la Pile,
            // comme sur une fiche.
            var rename = Buttons.Icon("pencil-simple-line", "Renommer le plan", Buttons.Compact, Buttons.Look.Calm);
            rename.Margin = new Thickness(6, 0, 0, 0);
            rename.VerticalAlignment = VerticalAlignment.Center;
            rename.Click += delegate { var h = RenameRequested; if (h != null) h(); };
            left.Children.Add(rename);
            // La bascule « Intensité » (30/09) : le graphique est une VUE du
            // plan, comme le mode wiki d'une fiche — plus une fenêtre à part.
            _chartToggle = new ToggleButton
            {
                Classes = { Marabook.App.Theme.Owned },
                Content = "📈  Intensité",
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(12, 0, 0, 0),
                [ToolTip.TipProperty] = "Voir l'intensité du plan entier, colonne par colonne — un clic de plus ramène les colonnes"
            };
            _chartToggle.IsCheckedChanged += delegate { ApplyChartMode(); };
            left.Children.Add(_chartToggle);
            left.Children.Add(new TextBlock
            {
                Text = "Relié à :",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 6, 0)
            });
            _linkCombo = new ComboBox { MinWidth = 180, [ToolTip.TipProperty] = "Le livre ou le dossier que ce plan décrit" };
            _linkCombo.SelectionChanged += delegate
            {
                if (_loading || _item == null) return;
                var index = _linkCombo.SelectedIndex;
                _item.Plan.LinkedItemId = index <= 0 || index - 1 >= _linkTargets.Count ? null : _linkTargets[index - 1].Id;
                _openLinked.IsVisible = _item.Plan.LinkedItemId != null ? true : false;
                NotifyEdited();
            };
            left.Children.Add(_linkCombo);
            _openLinked = new Button
            {
                Content = Icons.Label("arrow-up-right-bold", "Ouvrir", 11, Chrome.Ink),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                IsVisible = false,
                [ToolTip.TipProperty] = "Ouvrir le livre ou le dossier relié"
            };
            _openLinked.Click += delegate
            {
                var target = _item == null || _project == null ? null : _project.FindById(_item.Plan.LinkedItemId);
                var handler = NavigateRequested;
                if (target != null && handler != null) handler(target);
            };
            left.Children.Add(_openLinked);
            row.Children.Add(left);
            head.Child = row;
            Children.Add(head);

            // ================================================== les colonnes
            _columns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 14, 16, 14) };
            _scroll = new ScrollViewer
            {
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                Content = _columns
            };
            _scroll.AddHandler(InputElement.PointerWheelChangedEvent, delegate(object sender, PointerWheelEventArgs e)
            {
                // Molette seule = défilement HORIZONTAL (les colonnes vont de
                // gauche à droite) ; Maj+molette rend le vertical.
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0 || Ui.HasCommand(e.KeyModifiers)) return;
                _scroll.Offset = new Vector(_scroll.Offset.X - Ui.Wheel(e), _scroll.Offset.Y);
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            _chart = new PlanChart { IsVisible = false };
            var center = new Grid();
            center.Children.Add(_scroll);
            center.Children.Add(_chart);
            // La hauteur du graphique suit la place, plafonnée : une courbe,
            // pas un mur (30/09). PIÈGE : sans hauteur explicite, une grille de
            // Canvas alignée en haut mesure 0 — rien ne se traçait chez Rémi.
            Ui.OnSizeChanged(center, delegate { _chart.Height = Math.Max(0, Math.Min(560, center.Bounds.Height)); });
            Children.Add(center);
        }

        /// <summary>Colonnes ou graphique, selon la bascule.</summary>
        private void ApplyChartMode()
        {
            var chart = _chartToggle.IsChecked == true;
            _scroll.IsVisible = !chart;
            _chart.IsVisible = chart;
            _chart.Show(chart ? _item : null);
        }

        // ================================================== cycle de vie

        public bool HasItem { get { return _item != null; } }
        public bool ShowsItem(BinderItem item) { return _item == item; }

        public void Load(BinderItem plan, Project project)
        {
            _item = plan;
            _project = project;
            if (plan.Plan == null) plan.Plan = new PlanInfo();
            _loading = true;
            _title.Text = plan.Title;
            CollectTargets();
            _linkCombo.Items.Clear();
            _linkCombo.Items.Add("— aucun —");
            foreach (var target in _linkTargets)
                _linkCombo.Items.Add((target.Kind == ItemKind.Book ? "📘 " : "📁 ") + target.Title);
            var linked = plan.Plan.LinkedItemId == null ? -1 : _linkTargets.FindIndex(delegate(BinderItem b) { return b.Id == plan.Plan.LinkedItemId; });
            _linkCombo.SelectedIndex = linked < 0 ? 0 : linked + 1;
            _openLinked.IsVisible = linked >= 0 ? true : false;
            _loading = false;
            Rebuild();
        }

        public void Clear()
        {
            _item = null;
            _columns.Children.Clear();
            _chart.Show(null);
        }

        public void Refresh()
        {
            if (_item != null) { _title.Text = _item.Title; Rebuild(); }
        }

        private void CollectTargets()
        {
            _linkTargets = new List<BinderItem>();
            _texts = new List<BinderItem>();
            if (_project == null) return;
            foreach (var item in _project.AllItems())
            {
                if (item.RootCategory().CategoryKey == Project.KeyTrash) continue;
                if (item.Kind == ItemKind.Book || item.Kind == ItemKind.Folder) _linkTargets.Add(item);
                if (item.Kind == ItemKind.Text && !item.IsExtraPage) _texts.Add(item);
            }
        }

        // ================================================== colonnes

        // Registres pour la navigation d'une occurrence (b37).
        private readonly Dictionary<string, TextBox> _columnBoxes = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, Border> _bricks = new Dictionary<string, Border>();

        /// <summary>Va à une colonne (titre sélectionné) ou à une brique
        /// (amenée à l'écran, contour d'accent un instant) — recherche projet.</summary>
        public void GoTo(SearchField field, int start, int length)
        {
            if (_item == null || field == null || field.RefId == null) return;
            _chartToggle.IsChecked = false; // la recherche mène aux colonnes
            if (field.Kind == SearchField.KindColumn)
            {
                TextBox box;
                if (!_columnBoxes.TryGetValue(field.RefId, out box)) return;
                box.BringIntoView();
                box.Focus();
                var max = (box.Text ?? "").Length;
                var at = Math.Min(start, max);
                Ui.Select(box, at, Math.Max(0, Math.Min(length, max - at)));
                return;
            }
            Border brick;
            if (!_bricks.TryGetValue(field.RefId, out brick)) return;
            brick.BringIntoView();
            Flash(brick);
        }

        private static void Flash(Border element)
        {
            var previous = element.BorderBrush;
            var thickness = element.BorderThickness;
            element.BorderBrush = Chrome.Accent;
            element.BorderThickness = new Thickness(2);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += delegate
            {
                timer.Stop();
                element.BorderBrush = previous;
                element.BorderThickness = thickness;
            };
            timer.Start();
        }

        private void Rebuild()
        {
            _columns.Children.Clear();
            _columnBoxes.Clear();
            _bricks.Clear();
            if (_item == null) return;
            if (_chartToggle.IsChecked == true) _chart.Show(_item);
            var index = 0;
            foreach (var column in _item.Plan.Columns)
                _columns.Children.Add(BuildColumn(column, index++));
            if (_item.Plan.Columns.Count == 0)
                _columns.Children.Add(new TextBlock
                {
                    Text = "Un plan vide. « + Nouvelle colonne » pose la première : un titre, un écrit relié si vous voulez, "
                        + "puis des éléments (avec leur intensité) et des notes.",
                    Foreground = Chrome.SoftText,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                    Margin = new Thickness(8)
                });
        }

        /// <summary>Rebâtit UNE colonne en place (hotfix 1.0.3-a) : une brique
        /// ajoutée, modifiée, déplacée ou retirée ne touche que la sienne.</summary>
        private void RebuildColumn(PlanColumn column)
        {
            if (_item == null) { Rebuild(); return; }
            var index = _item.Plan.Columns.IndexOf(column);
            if (index < 0 || index >= _columns.Children.Count || !(_columns.Children[index] is Border)) { Rebuild(); return; }
            foreach (var entry in column.Entries) _bricks.Remove(entry.Id);
            _columns.Children[index] = BuildColumn(column, index);
            if (_chartToggle.IsChecked == true) _chart.Show(_item);
        }

        internal void ProbeAddColumn() { AddColumn(); }
        internal void ProbeShowChart(bool on) { _chartToggle.IsChecked = on; }
        internal PlanChart Chart { get { return _chart; } }
        internal int ColumnControls { get { return _columns.Children.Count; } }

        private Control BuildColumn(PlanColumn column, int index)
        {
            var body = new StackPanel();

            // — Tête de colonne : titre, lien vers un écrit, menu.
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var menu = Buttons.Icon("dots-three-vertical-bold", "Déplacer, supprimer la colonne", Buttons.Compact, Buttons.Look.Calm);
            var columnRef = column;
            menu.Click += delegate { Ui.ShowMenu(ColumnMenu(columnRef), menu); }; // une cible, sinon Avalonia lève (28/09)
            DockPanel.SetDock(menu, Dock.Right);
            titleRow.Children.Add(menu);
            var titleBox = new TextBox
            {
                Text = column.Title,
                FontWeight = FontWeight.SemiBold,
                [ToolTip.TipProperty] = "Titre de la colonne"
            };
            _columnBoxes[column.Id] = titleBox;
            titleBox.TextChanged += delegate
            {
                if (_loading) return;
                columnRef.Title = titleBox.Text;
                NotifyEdited();
            };
            titleRow.Children.Add(titleBox);
            body.Children.Add(titleRow);

            var linkRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var open = new Button
            {
                Content = Icons.Make("arrow-up-right-bold", 11, Chrome.Ink),
                Width = 26,
                Margin = new Thickness(4, 0, 0, 0),
                [ToolTip.TipProperty] = "Ouvrir l'écrit relié",
                IsVisible = column.LinkedTextId != null ? true : false
            };
            open.Click += delegate
            {
                var target = _project == null || columnRef.LinkedTextId == null ? null : _project.FindById(columnRef.LinkedTextId);
                var handler = NavigateRequested;
                if (target != null && handler != null) handler(target);
            };
            DockPanel.SetDock(open, Dock.Right);
            linkRow.Children.Add(open);
            var textCombo = new ComboBox { FontSize = 11, [ToolTip.TipProperty] = "L'écrit que cette colonne raconte (facultatif)" };
            // La liste des écrits n'est remplie qu'à l'OUVERTURE du combo
            // (hotfix 1.0.3-a) : vingt colonnes × tous les écrits du projet,
            // c'était le gros du temps de construction. Fermé, il ne montre
            // que son choix.
            var linkedIndex = column.LinkedTextId == null ? -1 : _texts.FindIndex(delegate(BinderItem t) { return t.Id == columnRef.LinkedTextId; });
            var filled = false;
            var filling = false;
            textCombo.Items.Add(linkedIndex < 0 ? "— aucun écrit —" : _texts[linkedIndex].Title);
            textCombo.SelectedIndex = 0;
            textCombo.DropDownOpened += delegate
            {
                if (filled) return;
                filled = true;
                filling = true;
                var current = columnRef.LinkedTextId == null ? -1 : _texts.FindIndex(delegate(BinderItem t) { return t.Id == columnRef.LinkedTextId; });
                textCombo.Items.Clear();
                textCombo.Items.Add("— aucun écrit —");
                foreach (var text in _texts) textCombo.Items.Add(text.Title);
                textCombo.SelectedIndex = current < 0 ? 0 : current + 1;
                filling = false;
            };
            textCombo.SelectionChanged += delegate
            {
                if (_loading || filling || !filled) return;
                var i = textCombo.SelectedIndex;
                columnRef.LinkedTextId = i <= 0 || i - 1 >= _texts.Count ? null : _texts[i - 1].Id;
                open.IsVisible = columnRef.LinkedTextId != null ? true : false;
                NotifyEdited();
            };
            linkRow.Children.Add(textCombo);
            body.Children.Add(linkRow);

            // — Les briques.
            var bricks = new StackPanel();
            foreach (var entry in column.Entries) bricks.Children.Add(BuildBrick(column, entry));
            body.Children.Add(bricks);

            // — Ajouts.
            var adds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var addElement = new Button { Content = Icons.Label("plus-bold", "Élément", 10, Chrome.Ink), Padding = new Thickness(8, 2, 8, 2), FontSize = 11 };
            addElement.Click += delegate { AddEntry(columnRef, PlanEntry.KindElement); };
            adds.Children.Add(addElement);
            var addNote = new Button { Content = Icons.Label("plus-bold", "Note", 10, Chrome.Ink), Padding = new Thickness(8, 2, 8, 2), FontSize = 11, Margin = new Thickness(6, 0, 0, 0) };
            addNote.Click += delegate { AddEntry(columnRef, PlanEntry.KindNote); };
            adds.Children.Add(addNote);
            body.Children.Add(adds);

            var frame = new Border
            {
                Width = 250,
                Background = Chrome.CardBg,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top,
                // Une ombre de cadre (hotfix 1.0.3-a), pas un effet : l'effet
                // rendait chaque colonne dans sa propre couche floutée — quinze
                // colonnes, et le défilement traînait.
                BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 1, Blur = 10, Color = Color.FromArgb(0x1F, 0, 0, 0) }),
                Child = body,
                [DragDrop.AllowDropProperty] = true,
                Tag = column
            };
            // Déposer dans le vide de la colonne : en fin de liste.
            frame.AddHandler(DragDrop.DragOverEvent, delegate(object sender, DragEventArgs e)
            {
                e.DragEffects = CanAccept(e, columnRef) ? DragDropEffects.Move : DragDropEffects.None;
                if (e.DragEffects == DragDropEffects.Move) HideDropBar(); // le vide : dépôt en fin de liste
                e.Handled = true;
            });
            frame.AddHandler(DragDrop.DropEvent, delegate(object sender, DragEventArgs e)
            {
                if (!CanAccept(e, columnRef)) return;
                MoveEntry(columnRef, DraggedEntry(e), columnRef.Entries.Count);
                e.Handled = true;
            });
            return frame;
        }

        private ContextMenu ColumnMenu(PlanColumn column)
        {
            var menu = new ContextMenu();
            var index = _item.Plan.Columns.IndexOf(column);
            var left = new MenuItem { Header = "Déplacer à gauche", IsEnabled = index > 0 };
            left.Click += delegate { MoveColumn(column, -1); };
            menu.Items.Add(left);
            var right = new MenuItem { Header = "Déplacer à droite", IsEnabled = index < _item.Plan.Columns.Count - 1 };
            right.Click += delegate { MoveColumn(column, 1); };
            menu.Items.Add(right);
            menu.Items.Add(new Separator());
            var remove = new MenuItem { Header = "Supprimer la colonne" };
            remove.Click += async delegate
            {
                if (column.Entries.Count > 0
                    && await MessageDialog.Show(Ui.OwnerOf(this),
                        "Supprimer la colonne « " + column.Title + " » et ses " + column.Entries.Count + " brique(s) ?",
                        "Plan", MessageButtons.YesNo, MessageIcon.Question) != MessageResult.Yes)
                    return;
                _item.Plan.Columns.Remove(column);
                Rebuild();
                NotifyEdited();
            };
            menu.Items.Add(remove);
            return menu;
        }

        private void AddColumn()
        {
            if (_item == null) return;
            var column = new PlanColumn { Title = _item.Plan.NextColumnTitle() };
            _item.Plan.Columns.Add(column);
            // La colonne seule est bâtie et ajoutée (hotfix 1.0.3-a) : rebâtir
            // tout le plan à chaque ajout coûtait la seconde au-delà de dix
            // colonnes.
            if (_item.Plan.Columns.Count == 1) _columns.Children.Clear(); // le mot du plan vide
            _columns.Children.Add(BuildColumn(column, _item.Plan.Columns.Count - 1));
            if (_chartToggle.IsChecked == true) _chart.Show(_item);
            NotifyEdited();
            _scroll.Offset = new Vector(_scroll.Extent.Width, _scroll.Offset.Y);
        }

        private void MoveColumn(PlanColumn column, int delta)
        {
            var columns = _item.Plan.Columns;
            var index = columns.IndexOf(column);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= columns.Count) return;
            columns.RemoveAt(index);
            columns.Insert(target, column);
            Rebuild();
            NotifyEdited();
        }

        // ================================================== briques

        private static Color ParseOrDefault(string hex, Color fallback)
        {
            try { return string.IsNullOrEmpty(hex) ? fallback : Ink.Parse(hex).ToColor(); }
            catch { return fallback; }
        }

        private Control BuildBrick(PlanColumn column, PlanEntry entry)
        {
            var content = new StackPanel();
            var text = new TextBlock
            {
                Text = (entry.Text ?? "").Length == 0 ? (entry.IsNote ? "(note vide)" : "(sans nom)") : entry.Text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = entry.IsNote ? NoteInk : Chrome.Ink
            };
            if (entry.IsNote) text.FontStyle = FontStyle.Italic;
            content.Children.Add(text);
            if (entry.IsElement)
            {
                var meter = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
                for (var i = 1; i <= PlanIntensity.Max; i++)
                    meter.Children.Add(new Ellipse
                    {
                        Width = 7,
                        Height = 7,
                        Margin = new Thickness(0, 0, 3, 0),
                        Fill = i <= entry.Intensity ? Chrome.Ink : Brushes.Transparent,
                        Stroke = Chrome.SoftText,
                        StrokeThickness = 0.8,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                meter.Children.Add(new TextBlock
                {
                    Text = PlanIntensity.Label(entry.Intensity),
                    FontSize = 10,
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                content.Children.Add(meter);
            }

            var brick = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                BorderThickness = new Thickness(entry.IsElement && entry.Color != null ? 4 : 1, 1, 1, 1),
                Cursor = new Cursor(StandardCursorType.Hand),
                [DragDrop.AllowDropProperty] = true,
                Tag = entry,
                Child = content,
                [ToolTip.TipProperty] = entry.IsNote ? "Note — clic : modifier, glisser : réordonner"
                    : "Élément — clic : modifier, glisser : réordonner"
            };
            _bricks[entry.Id] = brick;
            if (entry.IsNote)
            {
                brick.Background = NoteBg;
                brick.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xD3, 0x7A));
                brick.BorderThickness = new Thickness(1);
            }
            else if (entry.Color != null)
            {
                var color = ParseOrDefault(entry.Color, Color.FromRgb(0x5B, 0x67, 0xD8));
                // La teinte sur le fond des champs (30/09) : au sombre, une
                // brique colorée reste sombre — le blanc figeait les briques
                // au clair.
                brick.Background = new SolidColorBrush(Chrome.Blend(color, Chrome.FieldBg.Color, 0.78));
                brick.BorderBrush = new SolidColorBrush(color);
            }
            else
            {
                brick.Background = Chrome.FieldBg;
                brick.BorderBrush = Chrome.Border;
            }

            var columnRef = column;
            var entryRef = entry;
            brick.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                _dragCandidate = entryRef;
                _dragColumn = columnRef;
                _dragStart = e.GetPosition(this);
                if (e.ClickCount == 2) { _dragCandidate = null; EditEntry(columnRef, entryRef); e.Handled = true; }
            };
            brick.PointerMoved += OnBrickMouseMove;
            brick.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) {
                if (_dragCandidate != entryRef) return; // un glisser est parti
                _dragCandidate = null;
                EditEntry(columnRef, entryRef);
            };
            brick.AddHandler(DragDrop.DragOverEvent, delegate(object sender, DragEventArgs e)
            {
                e.DragEffects = CanAccept(e, columnRef) ? DragDropEffects.Move : DragDropEffects.None;
                if (e.DragEffects == DragDropEffects.Move) ShowDropBar(brick);
                e.Handled = true;
            });
            // Pas de DragLeave : il part dès que le pointeur passe d'un enfant
            // de la brique à un autre — l'indicateur clignotait. Il se retire
            // quand un AUTRE point d'insertion est survolé, au dépôt, ou à la
            // fin du glisser.
            brick.AddHandler(DragDrop.DropEvent, delegate(object sender, DragEventArgs e)
            {
                HideDropBar();
                if (!CanAccept(e, columnRef)) return;
                MoveEntry(columnRef, DraggedEntry(e), columnRef.Entries.IndexOf(entryRef));
                e.Handled = true;
            });
            brick.ContextMenu = BrickMenu(columnRef, entryRef);
            return brick;
        }

        private ContextMenu BrickMenu(PlanColumn column, PlanEntry entry)
        {
            var menu = new ContextMenu();
            var edit = new MenuItem { Header = "Modifier…" };
            edit.Click += delegate { EditEntry(column, entry); };
            menu.Items.Add(edit);
            var up = new MenuItem { Header = "Monter" };
            up.Click += delegate { MoveEntry(column, entry, Math.Max(0, column.Entries.IndexOf(entry) - 1)); };
            menu.Items.Add(up);
            var down = new MenuItem { Header = "Descendre" };
            down.Click += delegate { MoveEntry(column, entry, Math.Min(column.Entries.Count, column.Entries.IndexOf(entry) + 2)); };
            menu.Items.Add(down);
            menu.Items.Add(new Separator());
            var remove = new MenuItem { Header = "Supprimer" };
            remove.Click += delegate
            {
                column.Entries.Remove(entry);
                RebuildColumn(column);
                NotifyEdited();
            };
            menu.Items.Add(remove);
            return menu;
        }

        private async void AddEntry(PlanColumn column, string kind)
        {
            var entry = new PlanEntry { Kind = kind, Intensity = 1 };
            if (!await PlanEntryDialog.Ask(Ui.OwnerOf(this), entry)) return;
            column.Entries.Add(entry);
            RebuildColumn(column);
            NotifyEdited();
        }

        private async void EditEntry(PlanColumn column, PlanEntry entry)
        {
            if (!await PlanEntryDialog.Ask(Ui.OwnerOf(this), entry)) return;
            RebuildColumn(column);
            NotifyEdited();
        }

        /// <summary>Insère la brique AVANT l'index donné dans SA colonne
        /// (public : la sonde réordonne sans souris).</summary>
        public void MoveEntry(PlanColumn column, PlanEntry entry, int index)
        {
            if (column == null || entry == null || !column.Entries.Contains(entry)) return;
            var old = column.Entries.IndexOf(entry);
            column.Entries.RemoveAt(old);
            if (old < index) index--;
            index = Math.Max(0, Math.Min(column.Entries.Count, index));
            column.Entries.Insert(index, entry);
            RebuildColumn(column);
            NotifyEdited();
        }

        // ---------------------------------------------------------- glisser

        private void OnBrickMouseMove(object sender, PointerEventArgs e)
        {
            if (_dragCandidate == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var position = e.GetPosition(this);
            if (Math.Abs(position.X - _dragStart.X) < 4.0
                && Math.Abs(position.Y - _dragStart.Y) < 4.0) return;
            var dragged = _dragCandidate;
            _dragCandidate = null;
            DragDrop.DoDragDrop(e, Ui.DataOf(DragFormat, _dragColumn.Id + "|" + dragged.Id), DragDropEffects.Move);
            HideDropBar();
        }

        /// <summary>Le réordonnancement reste DANS la colonne d'origine.</summary>
        private bool CanAccept(DragEventArgs e, PlanColumn column)
        {
            if (!e.Data.Contains(DragFormat)) return false;
            var payload = (string)e.Data.Get(DragFormat);
            return payload != null && payload.StartsWith(column.Id + "|", StringComparison.Ordinal);
        }

        private PlanEntry DraggedEntry(DragEventArgs e)
        {
            var payload = (string)e.Data.Get(DragFormat);
            var parts = payload.Split('|');
            var column = _item.Plan.FindColumn(parts[0]);
            if (column == null) return null;
            foreach (var entry in column.Entries) if (entry.Id == parts[1]) return entry;
            return null;
        }

        /// <summary>Le trait d'insertion au-dessus de la brique visée : un
        /// ADORNER, dessiné par-dessus sans toucher à la mise en page — un
        /// indicateur qui déplace la brique sous le curseur fait alterner
        /// DragOver/DragLeave à chaque pixel (le clignotement du batch 35).</summary>
        private void ShowDropBar(Border brick)
        {
            if (_dropTarget == brick) return;
            HideDropBar();
            var layer = AdornerLayer.GetAdornerLayer(brick);
            if (layer == null) return;
            _dropTarget = brick;
            _dropAdorner = new InsertionAdorner(brick);
            layer.Children.Add(_dropAdorner);
        }

        private void HideDropBar()
        {
            if (_dropTarget == null) return;
            var layer = AdornerLayer.GetAdornerLayer(_dropTarget);
            if (layer != null && _dropAdorner != null) layer.Children.Remove(_dropAdorner);
            _dropAdorner = null;
            _dropTarget = null;
        }

        /// <summary>Un trait accent de 3 px au bord haut de l'élément orné.</summary>
        private sealed class InsertionAdorner : Control
        {
            private readonly Control _adorned;

            public InsertionAdorner(Control adorned)
            {
                _adorned = adorned;
                IsHitTestVisible = false;
                AdornerLayer.SetAdornedElement(this, adorned);
            }

            public override void Render(DrawingContext dc)
            {
                var width = _adorned.Bounds.Width;
                dc.DrawRectangle(Chrome.Accent, null, new Rect(-2, -4, width + 4, 3), 1.5, 1.5);
                dc.DrawEllipse(Chrome.Accent, null, new Point(-2, -2.5), 3.5, 3.5);
            }
        }

        private void NotifyEdited()
        {
            if (_loading) return;
            if (_chartToggle.IsChecked == true && _item != null) _chart.Show(_item); // la courbe suit
            var handler = Edited;
            if (handler != null) handler();
        }
    }

    /// <summary>Le dialogue d'une brique : le nom (une phrase), la couleur et
    /// l'intensité pour un élément ; le texte seul pour une note.</summary>
    public class PlanEntryDialog : Window
    {
        private readonly TextBox _text;
        private readonly ComboBox _intensity;
        private readonly WrapPanel _swatches;
        private string _color;
        private bool _accepted;

        private PlanEntryDialog(Window owner, PlanEntry entry)
        {
            Title = entry.IsNote ? "Note du plan" : "Élément du plan";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;
            _color = entry.Color;

            var panel = new StackPanel { Margin = new Thickness(16), Width = 380 };
            panel.Children.Add(Label(entry.IsNote ? "Texte de la note :" : "Nom de l'élément (une phrase suffit) :"));
            _text = new TextBox
            {
                Text = entry.Text,
                AcceptsReturn = entry.IsNote,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = entry.IsNote ? 72 : 28,
                // Une ligne : centrée comme les autres champs (le texte collait
                // au bord haut, 30/09) ; la note, elle, commence en haut.
                VerticalContentAlignment = entry.IsNote ? VerticalAlignment.Top : VerticalAlignment.Center
            };
            panel.Children.Add(_text);

            _swatches = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            _intensity = new ComboBox();
            if (entry.IsElement)
            {
                panel.Children.Add(Label("Couleur :"));
                RebuildSwatches();
                panel.Children.Add(_swatches);
                panel.Children.Add(Label("Intensité — l'implication du lecteur, du plus bas au plus haut :"));
                for (var i = PlanIntensity.Min; i <= PlanIntensity.Max; i++)
                    _intensity.Items.Add(i + " — " + PlanIntensity.Label(i));
                _intensity.SelectedIndex = PlanIntensity.Clamp(entry.Intensity) - 1;
                panel.Children.Add(_intensity);
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = !entry.IsNote, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); }; // IsCancel ne ferme pas la fenêtre sur Avalonia (28/09)
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
            Loaded += delegate { _text.Focus(); _text.SelectAll(); };
        }

        private void RebuildSwatches()
        {
            _swatches.Children.Clear();
            foreach (var swatch in ItemIcons.TintSwatches)
            {
                var value = swatch;
                var active = _color == value;
                var chip = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Margin = new Thickness(0, 0, 6, 4),
                    Background = value == null ? Brushes.Transparent : new SolidColorBrush(Ink.Parse(value).ToColor()),
                    BorderBrush = active ? (IBrush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(active ? 2.4 : 1),
                    [ToolTip.TipProperty] = value == null ? "Neutre" : value,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                chip.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { _color = value; RebuildSwatches(); };
                _swatches.Children.Add(chip);
            }
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 8, 0, 3) };
        }

        /// <summary>Vrai si validé : l'entrée est mise à jour en place.</summary>
        public static async Task<bool> Ask(Window owner, PlanEntry entry)
        {
            var dialog = new PlanEntryDialog(owner, entry);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return false;
            entry.Text = (dialog._text.Text ?? "").Trim();
            if (entry.IsElement)
            {
                entry.Color = dialog._color;
                entry.Intensity = dialog._intensity.SelectedIndex + 1;
            }
            return true;
        }
    }

    /// <summary>Le graphique d'intensité du plan : une VUE du plan (bascule
    /// « Intensité » du bandeau, comme le mode wiki d'une fiche — avant, une
    /// fenêtre à part ; 30/09). L'échelle des cinq niveaux à gauche, fixe ; la
    /// courbe défile à l'horizontale quand les colonnes sont nombreuses.
    /// Hotfix 1.0.3-a : le point d'une colonne est à la MOYENNE des
    /// intensités de ses éléments (PlanIntensity.MeanProfile), l'échelle va
    /// de 0 à 6 (une marge sous le niveau 1 et au-dessus du 5, le niveau 0
    /// « sans élément » ne colle plus à l'axe), les titres des colonnes sont
    /// tournés à −90° sous leur point (LayoutTransformControl : la rotation
    /// est une transformation de DISPOSITION, le bloc tourné se mesure
    /// tourné), et un zoom (−, +, Tout, Ctrl+molette) resserre ou écarte
    /// les colonnes — « Tout » fait tenir le plan entier dans la fenêtre.</summary>
    public sealed class PlanChart : Grid
    {
        private const double AxisWidth = 190;
        private const double MinStep = 120;
        private const double FitMinStep = 14;
        private const double Top = 38;
        private const double CaptionBand = 112;
        private const double ZoomMin = 0.15, ZoomMax = 3;

        private readonly Canvas _axis = new Canvas { Width = AxisWidth };
        private readonly Canvas _plot = new Canvas();
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _tools;
        private readonly TextBlock _zoomLabel;
        private BinderItem _plan;
        private double _zoom = 1;
        private bool _fitAll;

        public PlanChart()
        {
            Margin = new Thickness(16, 14, 16, 14);
            VerticalAlignment = VerticalAlignment.Top; // la hauteur : posée par PlanView selon la place (Height), sinon une grille de Canvas ne mesure rien
            ColumnDefinitions = new ColumnDefinitions("Auto,*");
            _scroll = new ScrollViewer
            {
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
                Content = _plot
            };
            _scroll.AddHandler(InputElement.PointerWheelChangedEvent, delegate(object sender, PointerWheelEventArgs e)
            {
                // Ctrl+molette = zoom ; molette seule = défilement horizontal, comme les colonnes.
                if (Ui.HasCommand(e.KeyModifiers)) { ZoomStep(Ui.Wheel(e) > 0 ? 1 : -1); e.Handled = true; return; }
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0) return;
                _scroll.Offset = new Vector(_scroll.Offset.X - Ui.Wheel(e), _scroll.Offset.Y);
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            Grid.SetColumn(_scroll, 1);
            Children.Add(_axis);
            Children.Add(_scroll);

            // Le zoom, en haut de l'axe : −, +, Tout, et l'échelle.
            _tools = new StackPanel { Orientation = Orientation.Horizontal };
            var zoomOut = Buttons.Text("−", "Resserrer les colonnes (Ctrl+molette)", Buttons.Compact, Buttons.Look.Outline);
            zoomOut.Width = Buttons.Compact; zoomOut.Padding = new Thickness(0);
            zoomOut.Click += delegate { ZoomStep(-1); };
            var zoomIn = Buttons.Text("+", "Écarter les colonnes (Ctrl+molette)", Buttons.Compact, Buttons.Look.Outline);
            zoomIn.Width = Buttons.Compact; zoomIn.Padding = new Thickness(0); zoomIn.Margin = new Thickness(4, 0, 0, 0);
            zoomIn.Click += delegate { ZoomStep(1); };
            var fit = Buttons.Text("Tout", "Faire tenir tout le plan dans la fenêtre", Buttons.Compact, Buttons.Look.Outline);
            fit.Margin = new Thickness(4, 0, 0, 0);
            fit.Click += delegate { FitAll(); };
            _zoomLabel = new TextBlock { FontSize = 11, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _tools.Children.Add(zoomOut);
            _tools.Children.Add(zoomIn);
            _tools.Children.Add(fit);
            _tools.Children.Add(_zoomLabel);
            Canvas.SetLeft(_tools, 0);
            Canvas.SetTop(_tools, 0);
            Ui.OnSizeChanged(this, Draw);
        }

        /// <summary>Montre ce plan (null : rien) et trace.</summary>
        public void Show(BinderItem plan)
        {
            _plan = plan;
            Draw();
        }

        /// <summary>Un cran de zoom : × 1,25 par cran, borné.</summary>
        public void ZoomStep(int direction)
        {
            _fitAll = false;
            _zoom = Math.Max(ZoomMin, Math.Min(ZoomMax, _zoom * (direction > 0 ? 1.25 : 0.8)));
            Draw();
        }

        /// <summary>Tout le plan dans la fenêtre (les colonnes au plus serré s'il le faut).</summary>
        public void FitAll()
        {
            _fitAll = true;
            Draw();
        }

        /// <summary>Sonde : la courbe tient-elle dans le viseur sans défiler ?</summary>
        internal bool PlotFitsViewport { get { return _plot.Width <= Bounds.Width - AxisWidth + 0.5; } }

        public void Draw()
        {
            _axis.Children.Clear();
            _plot.Children.Clear();
            var height = Bounds.Height - Marabook.App.Theme.ScrollBarSize;
            var viewport = Bounds.Width - AxisWidth;
            if (_plan == null || _plan.Plan == null || height < 80 || viewport < 50) return;
            var columns = _plan.Plan.Columns;
            var profile = PlanIntensity.MeanProfile(_plan.Plan);
            var bottom = height - CaptionBand;
            var fitStep = columns.Count == 0 ? viewport : viewport / columns.Count;
            var step = columns.Count == 0 ? viewport
                : _fitAll ? Math.Max(FitMinStep, fitStep)
                : Math.Max(MinStep * _zoom, fitStep);
            var width = Math.Max(viewport, step * columns.Count);
            _plot.Width = width;
            _plot.Height = height;
            _axis.Height = height;
            _axis.Children.Add(_tools);
            _zoomLabel.Text = _fitAll ? "tout" : Math.Round(_zoom * 100) + " %";

            // L'échelle de 0 à 6 : les cinq niveaux ont leur libellé, leur repère
            // et leur ligne de fond ; 0 et 6 sont la marge.
            for (var level = PlanIntensity.Min; level <= PlanIntensity.Max; level++)
            {
                var y = LevelY(level, bottom);
                _plot.Children.Add(new Line { StartPoint = new Point(0, y), EndPoint = new Point(width, y), Stroke = Chrome.Border, StrokeThickness = 1 });
                var label = new TextBlock
                {
                    Text = level + " · " + PlanIntensity.Label(level),
                    FontSize = 11,
                    Foreground = Chrome.SoftText,
                    Width = AxisWidth - 14,
                    TextAlignment = TextAlignment.Right,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 8);
                _axis.Children.Add(label);
                _axis.Children.Add(new Line { StartPoint = new Point(AxisWidth - 8, y), EndPoint = new Point(AxisWidth, y), Stroke = Chrome.BorderStrong, StrokeThickness = 1 });
            }
            _plot.Children.Add(new Line { StartPoint = new Point(0, bottom), EndPoint = new Point(width, bottom), Stroke = Chrome.BorderStrong, StrokeThickness = 1 });
            if (columns.Count == 0)
            {
                var empty = new TextBlock { Text = "Aucune colonne : la courbe suit les éléments du plan.", Foreground = Chrome.SoftText };
                Canvas.SetLeft(empty, 12);
                Canvas.SetTop(empty, Top);
                _plot.Children.Add(empty);
                return;
            }

            var dense = step < 40; // trop serré pour les libellés au-dessus des points
            var points = new Points();
            var area = new Points();
            for (var i = 0; i < columns.Count; i++)
            {
                var x = step * (i + 0.5);
                var value = profile[i];
                var y = LevelY(value, bottom);
                if (i == 0) area.Add(new Point(x, bottom));
                points.Add(new Point(x, y));
                area.Add(new Point(x, y));
                if (i == columns.Count - 1) area.Add(new Point(x, bottom));
                var title = columns[i].Title.Length == 0 ? "(sans titre)" : columns[i].Title;
                var nearest = PlanIntensity.Clamp((int)Math.Round(value));
                _plot.Children.Add(new Line { StartPoint = new Point(x, bottom), EndPoint = new Point(x, bottom + 6), Stroke = Chrome.BorderStrong, StrokeThickness = 1 });
                var size = dense ? 8 : 12;
                var dot = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = value <= 0 ? (IBrush)Chrome.Border : Chrome.Accent,
                    Stroke = Chrome.WindowBg,
                    StrokeThickness = 2,
                    [ToolTip.TipProperty] = title + " — " + (value <= 0 ? "aucun élément" : PlanIntensity.Label(nearest) + " (moyenne " + value.ToString("0.0") + ")")
                };
                Canvas.SetLeft(dot, x - size / 2.0);
                Canvas.SetTop(dot, y - size / 2.0);
                _plot.Children.Add(dot);
                if (value > 0 && !dense)
                {
                    // Le niveau le plus proche en toutes lettres au-dessus du point.
                    var tag = new TextBlock { Text = PlanIntensity.Label(nearest), FontSize = 10, Foreground = Chrome.SoftText, Width = step, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                    Canvas.SetLeft(tag, x - step / 2);
                    Canvas.SetTop(tag, y - 26);
                    _plot.Children.Add(tag);
                }
                // Le titre tourné à −90° sous son point : sa fin touche l'axe,
                // il se lit de bas en haut ; au plus serré, une colonne sur n.
                var every = Math.Max(1, (int)Math.Ceiling(16 / step));
                if (i % every != 0) continue;
                var caption = new LayoutTransformControl
                {
                    LayoutTransform = new RotateTransform(-90),
                    Child = new TextBlock
                    {
                        Text = title,
                        FontSize = 11,
                        Foreground = Chrome.Ink,
                        Width = CaptionBand - 16,
                        TextAlignment = TextAlignment.Right,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        [ToolTip.TipProperty] = title
                    }
                };
                Canvas.SetLeft(caption, x - 8);
                Canvas.SetTop(caption, bottom + 10);
                _plot.Children.Add(caption);
            }
            var accent = ((SolidColorBrush)Chrome.Accent).Color;
            _plot.Children.Insert(0, new Polygon { Points = area, Fill = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)) });
            _plot.Children.Insert(1, new Polyline { Points = points, Stroke = Chrome.Accent, StrokeThickness = 2.4, StrokeJoin = PenLineJoin.Round });
        }

        /// <summary>L'ordonnée d'un niveau sur l'échelle 0–6 : 0 sur l'axe,
        /// 6 en haut — une marge d'un niveau de chaque côté des cinq.</summary>
        private static double LevelY(double level, double bottom)
        {
            return bottom - level * (bottom - Top) / (PlanIntensity.Max + 1);
        }
    }
}

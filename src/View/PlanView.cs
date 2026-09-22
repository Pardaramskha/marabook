using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>L'écran d'un PLAN (batch 35) : des colonnes de gauche à
    /// droite (défilement horizontal), chacune titrée et reliable à un
    /// écrit ; dedans, des ÉLÉMENTS (brique colorée, intensité 1–5) et des
    /// NOTES (post-it), réordonnés à la souris ; en tête, le nom, le lien du
    /// plan vers un livre ou un dossier, et le graphique d'intensité.</summary>
    public class PlanView : DockPanel
    {
        private const string DragFormat = "MarabookPlanEntry";
        private static readonly Brush NoteBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xA8));
        private static readonly Brush NoteInk = new SolidColorBrush(Color.FromRgb(0x5C, 0x4A, 0x10));

        private readonly TextBlock _title;
        private readonly ComboBox _linkCombo;
        private readonly Button _openLinked;
        private readonly StackPanel _columns;
        private readonly ScrollViewer _scroll;

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
                ToolTip = "Revenir à la carte des plans",
                VerticalAlignment = VerticalAlignment.Center
            };
            back.Click += delegate { var h = BackRequested; if (h != null) h(); };
            left.Children.Add(back);
            _title = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
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
            var chart = new Button
            {
                Content = "📈  Graphique d'intensité",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(12, 0, 0, 0),
                ToolTip = "L'intensité du plan entier, colonne par colonne"
            };
            chart.Click += delegate { if (_item != null) PlanChartWindow.Show(Window.GetWindow(this), _item); };
            left.Children.Add(chart);
            left.Children.Add(new TextBlock
            {
                Text = "Relié à :",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 6, 0)
            });
            _linkCombo = new ComboBox { MinWidth = 180, ToolTip = "Le livre ou le dossier que ce plan décrit" };
            _linkCombo.SelectionChanged += delegate
            {
                if (_loading || _item == null) return;
                var index = _linkCombo.SelectedIndex;
                _item.Plan.LinkedItemId = index <= 0 || index - 1 >= _linkTargets.Count ? null : _linkTargets[index - 1].Id;
                _openLinked.Visibility = _item.Plan.LinkedItemId != null ? Visibility.Visible : Visibility.Collapsed;
                NotifyEdited();
            };
            left.Children.Add(_linkCombo);
            _openLinked = new Button
            {
                Content = Icons.Label("arrow-up-right-bold", "Ouvrir", 11, Chrome.Ink),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = Visibility.Collapsed,
                ToolTip = "Ouvrir le livre ou le dossier relié"
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
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _columns
            };
            _scroll.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                // Molette seule = défilement HORIZONTAL (les colonnes vont de
                // gauche à droite) ; Maj+molette rend le vertical.
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
                _scroll.ScrollToHorizontalOffset(_scroll.HorizontalOffset - e.Delta);
                e.Handled = true;
            };
            Children.Add(_scroll);
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
            _openLinked.Visibility = linked >= 0 ? Visibility.Visible : Visibility.Collapsed;
            _loading = false;
            Rebuild();
        }

        public void Clear()
        {
            _item = null;
            _columns.Children.Clear();
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
            if (field.Kind == SearchField.KindColumn)
            {
                TextBox box;
                if (!_columnBoxes.TryGetValue(field.RefId, out box)) return;
                box.BringIntoView();
                box.Focus();
                var max = box.Text.Length;
                var at = Math.Min(start, max);
                box.Select(at, Math.Max(0, Math.Min(length, max - at)));
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
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
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

        private UIElement BuildColumn(PlanColumn column, int index)
        {
            var body = new StackPanel();

            // — Tête de colonne : titre, lien vers un écrit, menu.
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var menu = Buttons.Icon("dots-three-vertical-bold", "Déplacer, supprimer la colonne", Buttons.Compact, Buttons.Look.Calm);
            var columnRef = column;
            menu.Click += delegate { ColumnMenu(columnRef).IsOpen = true; };
            DockPanel.SetDock(menu, Dock.Right);
            titleRow.Children.Add(menu);
            var titleBox = new TextBox
            {
                Text = column.Title,
                FontWeight = FontWeights.SemiBold,
                ToolTip = "Titre de la colonne"
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
                ToolTip = "Ouvrir l'écrit relié",
                Visibility = column.LinkedTextId != null ? Visibility.Visible : Visibility.Collapsed
            };
            open.Click += delegate
            {
                var target = _project == null || columnRef.LinkedTextId == null ? null : _project.FindById(columnRef.LinkedTextId);
                var handler = NavigateRequested;
                if (target != null && handler != null) handler(target);
            };
            DockPanel.SetDock(open, Dock.Right);
            linkRow.Children.Add(open);
            var textCombo = new ComboBox { FontSize = 11, ToolTip = "L'écrit que cette colonne raconte (facultatif)" };
            textCombo.Items.Add("— aucun écrit —");
            foreach (var text in _texts) textCombo.Items.Add(text.Title);
            var linkedIndex = column.LinkedTextId == null ? -1 : _texts.FindIndex(delegate(BinderItem t) { return t.Id == columnRef.LinkedTextId; });
            textCombo.SelectedIndex = linkedIndex < 0 ? 0 : linkedIndex + 1;
            textCombo.SelectionChanged += delegate
            {
                if (_loading) return;
                var i = textCombo.SelectedIndex;
                columnRef.LinkedTextId = i <= 0 || i - 1 >= _texts.Count ? null : _texts[i - 1].Id;
                open.Visibility = columnRef.LinkedTextId != null ? Visibility.Visible : Visibility.Collapsed;
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
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.12, Color = Colors.Black },
                Child = body,
                AllowDrop = true,
                Tag = column
            };
            // Déposer dans le vide de la colonne : en fin de liste.
            frame.DragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = CanAccept(e, columnRef) ? DragDropEffects.Move : DragDropEffects.None;
                if (e.Effects == DragDropEffects.Move) HideDropBar(); // le vide : dépôt en fin de liste
                e.Handled = true;
            };
            frame.Drop += delegate(object sender, DragEventArgs e)
            {
                if (!CanAccept(e, columnRef)) return;
                MoveEntry(columnRef, DraggedEntry(e), columnRef.Entries.Count);
                e.Handled = true;
            };
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
            remove.Click += delegate
            {
                if (column.Entries.Count > 0
                    && MessageDialog.Show(Window.GetWindow(this),
                        "Supprimer la colonne « " + column.Title + " » et ses " + column.Entries.Count + " brique(s) ?",
                        "Plan", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
            Rebuild();
            NotifyEdited();
            _scroll.ScrollToRightEnd();
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
            try { return string.IsNullOrEmpty(hex) ? fallback : FlowConverter.ParseColor(hex); }
            catch { return fallback; }
        }

        private UIElement BuildBrick(PlanColumn column, PlanEntry entry)
        {
            var content = new StackPanel();
            var text = new TextBlock
            {
                Text = entry.Text.Length == 0 ? (entry.IsNote ? "(note vide)" : "(sans nom)") : entry.Text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = entry.IsNote ? NoteInk : Chrome.Ink
            };
            if (entry.IsNote) text.FontStyle = FontStyles.Italic;
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
                Cursor = Cursors.Hand,
                AllowDrop = true,
                Tag = entry,
                Child = content,
                ToolTip = entry.IsNote ? "Note — clic : modifier, glisser : réordonner"
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
                brick.Background = new SolidColorBrush(Chrome.Blend(color, Colors.White, 0.78));
                brick.BorderBrush = new SolidColorBrush(color);
            }
            else
            {
                brick.Background = Chrome.PaperBg;
                brick.BorderBrush = Chrome.Border;
            }

            var columnRef = column;
            var entryRef = entry;
            brick.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                _dragCandidate = entryRef;
                _dragColumn = columnRef;
                _dragStart = e.GetPosition(this);
                if (e.ClickCount == 2) { _dragCandidate = null; EditEntry(columnRef, entryRef); e.Handled = true; }
            };
            brick.MouseMove += OnBrickMouseMove;
            brick.MouseLeftButtonUp += delegate
            {
                if (_dragCandidate != entryRef) return; // un glisser est parti
                _dragCandidate = null;
                EditEntry(columnRef, entryRef);
            };
            brick.DragOver += delegate(object sender, DragEventArgs e)
            {
                e.Effects = CanAccept(e, columnRef) ? DragDropEffects.Move : DragDropEffects.None;
                if (e.Effects == DragDropEffects.Move) ShowDropBar(brick);
                e.Handled = true;
            };
            // Pas de DragLeave : il part dès que le pointeur passe d'un enfant
            // de la brique à un autre — l'indicateur clignotait. Il se retire
            // quand un AUTRE point d'insertion est survolé, au dépôt, ou à la
            // fin du glisser.
            brick.Drop += delegate(object sender, DragEventArgs e)
            {
                HideDropBar();
                if (!CanAccept(e, columnRef)) return;
                MoveEntry(columnRef, DraggedEntry(e), columnRef.Entries.IndexOf(entryRef));
                e.Handled = true;
            };
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
                Rebuild();
                NotifyEdited();
            };
            menu.Items.Add(remove);
            return menu;
        }

        private void AddEntry(PlanColumn column, string kind)
        {
            var entry = new PlanEntry { Kind = kind, Intensity = 1 };
            if (!PlanEntryDialog.Ask(Window.GetWindow(this), entry)) return;
            column.Entries.Add(entry);
            Rebuild();
            NotifyEdited();
        }

        private void EditEntry(PlanColumn column, PlanEntry entry)
        {
            if (!PlanEntryDialog.Ask(Window.GetWindow(this), entry)) return;
            Rebuild();
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
            Rebuild();
            NotifyEdited();
        }

        // ---------------------------------------------------------- glisser

        private void OnBrickMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(this);
            if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var dragged = _dragCandidate;
            _dragCandidate = null;
            DragDrop.DoDragDrop(this, new DataObject(DragFormat, _dragColumn.Id + "|" + dragged.Id), DragDropEffects.Move);
            HideDropBar();
        }

        /// <summary>Le réordonnancement reste DANS la colonne d'origine.</summary>
        private bool CanAccept(DragEventArgs e, PlanColumn column)
        {
            if (!e.Data.GetDataPresent(DragFormat)) return false;
            var payload = (string)e.Data.GetData(DragFormat);
            return payload != null && payload.StartsWith(column.Id + "|", StringComparison.Ordinal);
        }

        private PlanEntry DraggedEntry(DragEventArgs e)
        {
            var payload = (string)e.Data.GetData(DragFormat);
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
            layer.Add(_dropAdorner);
        }

        private void HideDropBar()
        {
            if (_dropTarget == null) return;
            var layer = AdornerLayer.GetAdornerLayer(_dropTarget);
            if (layer != null && _dropAdorner != null) layer.Remove(_dropAdorner);
            _dropAdorner = null;
            _dropTarget = null;
        }

        /// <summary>Un trait accent de 3 px au bord haut de l'élément orné.</summary>
        private sealed class InsertionAdorner : Adorner
        {
            public InsertionAdorner(UIElement adorned) : base(adorned)
            {
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext dc)
            {
                var element = AdornedElement as FrameworkElement;
                if (element == null) return;
                var width = element.ActualWidth;
                dc.DrawRoundedRectangle(Chrome.Accent, null, new Rect(-2, -4, width + 4, 3), 1.5, 1.5);
                dc.DrawEllipse(Chrome.Accent, null, new Point(-2, -2.5), 3.5, 3.5);
            }
        }

        private void NotifyEdited()
        {
            if (_loading) return;
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
            ResizeMode = ResizeMode.NoResize;
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
                VerticalContentAlignment = VerticalAlignment.Top
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
                    Background = value == null ? Brushes.Transparent : new SolidColorBrush(FlowConverter.ParseColor(value)),
                    BorderBrush = active ? (Brush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(active ? 2.4 : 1),
                    ToolTip = value == null ? "Neutre" : value,
                    Cursor = Cursors.Hand
                };
                chip.MouseLeftButtonUp += delegate { _color = value; RebuildSwatches(); };
                _swatches.Children.Add(chip);
            }
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 8, 0, 3) };
        }

        /// <summary>Vrai si validé : l'entrée est mise à jour en place.</summary>
        public static bool Ask(Window owner, PlanEntry entry)
        {
            var dialog = new PlanEntryDialog(owner, entry);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return false;
            entry.Text = dialog._text.Text.Trim();
            if (entry.IsElement)
            {
                entry.Color = dialog._color;
                entry.Intensity = dialog._intensity.SelectedIndex + 1;
            }
            return true;
        }
    }

    /// <summary>Le graphique d'intensité du plan entier : une courbe, une
    /// valeur par colonne (le pic de ses éléments), l'échelle des cinq
    /// niveaux à gauche, les titres de colonnes en bas.</summary>
    public class PlanChartWindow : Window
    {
        private PlanChartWindow(Window owner, BinderItem plan)
        {
            Title = "Intensité — " + plan.Title;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = Math.Max(640, Math.Min(1200, 160 + plan.Plan.Columns.Count * 110));
            Height = 440;
            Background = Chrome.WindowBg;
            var canvas = new Canvas { Margin = new Thickness(16) };
            Content = canvas;
            SizeChanged += delegate { Draw(canvas, plan); };
            Loaded += delegate { Draw(canvas, plan); };
        }

        /// <summary>Le tracé (public et statique : la sonde dessine hors fenêtre).</summary>
        public static void Draw(Canvas canvas, BinderItem plan)
        {
            canvas.Children.Clear();
            var width = canvas.ActualWidth;
            var height = canvas.ActualHeight;
            if (width < 50 || height < 50) return;
            var left = 190.0;
            var right = width - 20;
            var top = 20.0;
            // Les noms de colonnes se lisent à la verticale (22/09) : la
            // bande du bas leur laisse la place.
            var captionLength = Math.Max(60, Math.Min(150, height * 0.32));
            var bottom = height - captionLength - 20;
            var profile = PlanIntensity.Profile(plan.Plan);
            var columns = plan.Plan.Columns;

            // Les cinq niveaux : lignes de fond + libellés.
            for (var level = PlanIntensity.Min; level <= PlanIntensity.Max; level++)
            {
                var y = bottom - (level - 1) * (bottom - top) / (PlanIntensity.Max - 1);
                canvas.Children.Add(new Line { X1 = left, X2 = right, Y1 = y, Y2 = y, Stroke = Chrome.Border, StrokeThickness = 1 });
                var label = new TextBlock { Text = level + " · " + PlanIntensity.Label(level), FontSize = 11, Foreground = Chrome.SoftText, Width = left - 16, TextAlignment = TextAlignment.Right };
                Canvas.SetLeft(label, 4);
                Canvas.SetTop(label, y - 8);
                canvas.Children.Add(label);
            }
            if (columns.Count == 0)
            {
                var empty = new TextBlock { Text = "Aucune colonne.", Foreground = Chrome.SoftText };
                Canvas.SetLeft(empty, left + 10);
                Canvas.SetTop(empty, top + 10);
                canvas.Children.Add(empty);
                return;
            }

            var step = columns.Count == 1 ? 0 : (right - left) / (columns.Count - 1);
            var points = new PointCollection();
            var area = new PointCollection();
            area.Add(new Point(left, bottom));
            for (var i = 0; i < columns.Count; i++)
            {
                var x = columns.Count == 1 ? (left + right) / 2 : left + i * step;
                var value = profile[i];
                var y = value <= 0 ? bottom : bottom - (value - 1) * (bottom - top) / (PlanIntensity.Max - 1);
                points.Add(new Point(x, y));
                area.Add(new Point(x, y));
                var dot = new Ellipse { Width = 10, Height = 10, Fill = value <= 0 ? (Brush)Chrome.Border : Chrome.Accent, ToolTip = columns[i].Title + " — " + (value <= 0 ? "aucun élément" : PlanIntensity.Label(value)) };
                Canvas.SetLeft(dot, x - 5);
                Canvas.SetTop(dot, y - 5);
                canvas.Children.Add(dot);
                // Tourné d'un quart de tour vers la gauche : le nom se lit de
                // bas en haut, sa fin (à droite avant rotation) touche l'axe.
                var caption = new TextBlock
                {
                    Text = columns[i].Title.Length == 0 ? "(sans titre)" : columns[i].Title,
                    FontSize = 11,
                    Foreground = Chrome.Ink,
                    Width = captionLength,
                    Height = 16,
                    TextAlignment = TextAlignment.Right,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    LayoutTransform = new RotateTransform(-90),
                    ToolTip = columns[i].Title
                };
                Canvas.SetLeft(caption, x - 8);
                Canvas.SetTop(caption, bottom + 10);
                canvas.Children.Add(caption);
            }
            area.Add(new Point(points[points.Count - 1].X, bottom));
            var accent = ((SolidColorBrush)Chrome.Accent).Color;
            canvas.Children.Insert(0, new Polygon { Points = area, Fill = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)) });
            canvas.Children.Add(new Polyline { Points = points, Stroke = Chrome.Accent, StrokeThickness = 2.4, StrokeLineJoin = PenLineJoin.Round });
        }

        public static void Show(Window owner, BinderItem plan)
        {
            if (plan.Plan == null) plan.Plan = new PlanInfo();
            Dialogs.ShowModal(new PlanChartWindow(owner, plan));
        }
    }
}

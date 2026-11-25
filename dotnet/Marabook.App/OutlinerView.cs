using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE TABLEAU — l'outliner (2.0.0, 21/11/2026) : l'état réel d'un
    /// manuscrit, aligné et comparable. Une ligne par écrit sous son dossier,
    /// une ligne par scène sous un écrit qui en a plusieurs ; en colonnes,
    /// l'état, les mots, le point de vue, le lieu, le moment, les personnages
    /// et le synopsis. L'état et le synopsis se changent sur place ; un clic
    /// sur une cellule de scène ouvre sa fiche. Les tris ne touchent pas à
    /// l'ordre du livre. Il vit à côté des cartes : le bouton « Tableau » d'un
    /// livre ou d'un dossier d'écrits passe de l'un à l'autre.</summary>
    internal sealed class OutlinerView : StackPanel
    {
        // Les colonnes S'AJUSTENT à la largeur offerte (21/11, Rémi : un tableau à
        // largeur fixe débordait rail ouvert, et laissait un blanc rail fermé) :
        // chacune a un plancher et une part du reste ; sous la somme des
        // planchers seulement, le tableau défile à l'horizontale.
        private static readonly double[] Floors = { 190, 100, 58, 104, 112, 96, 130, 170 };
        private static readonly double[] Shares = { 2.2, 0, 0, 1, 1.1, 0.9, 1.4, 3 };
        private static double FloorTotal { get { double total = 0; foreach (var floor in Floors) total += floor; return total; } }
        private ScrollViewer _scroll;

        private readonly Grid _table;
        private readonly CheckBox _scenes;
        private readonly ComboBox _sort, _status;
        private readonly TextBlock _totals, _empty;
        private Project _project;
        private BinderItem _folder;
        private List<OutlineRow> _rows = new List<OutlineRow>();
        private bool _syncing;

        public event Action<BinderItem> Navigate;
        /// <summary>Ouvrir l'écrit AU DÉBUT de cette scène : (écrit, paragraphe).</summary>
        public event Action<BinderItem, int> NavigateScene;
        /// <summary>Renseigner cette scène : (écrit, numéro de scène).</summary>
        public event Action<BinderItem, int> SceneEditRequested;
        /// <summary>Un état ou un synopsis changé sur place.</summary>
        public event Action<BinderItem> ItemChanged;

        public OutlinerView()
        {
            Tag = "outliner";
            Margin = new Thickness(24, 8, 24, 20);

            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _scenes = new CheckBox { Content = "Une ligne par scène", IsChecked = AppSettings.OutlinerScenes, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 4), Tag = "outliner-scenes" };
            ToolTip.SetTip(_scenes, "Sous chaque écrit coupé par des séparateurs de scène, une ligne par scène, avec sa propre fiche");
            _scenes.IsCheckedChanged += delegate { if (_syncing) return; AppSettings.OutlinerScenes = _scenes.IsChecked == true; AppSettings.Save(); Rebuild(); };
            bar.Children.Add(_scenes);
            _sort = new ComboBox { MinWidth = 190, Margin = new Thickness(0, 0, 10, 4), Tag = "outliner-sort" };
            ToolTip.SetTip(_sort, "L'ordre des lignes. Un tri met les écrits à plat le temps de regarder : l'ordre du livre ne change pas.");
            foreach (var sort in Outline.Sorts) _sort.Items.Add(Outline.SortLabel(sort));
            _sort.SelectedIndex = 0;
            _sort.SelectionChanged += delegate { if (!_syncing) Rebuild(); };
            bar.Children.Add(_sort);
            _status = new ComboBox { MinWidth = 150, Margin = new Thickness(0, 0, 10, 4), Tag = "outliner-status" };
            ToolTip.SetTip(_status, "Ne montrer que les écrits dans cet état");
            _status.Items.Add("Tous les états");
            foreach (var key in TextStatus.Keys) _status.Items.Add(TextStatus.Label(key));
            _status.Items.Add("Sans état");
            _status.SelectedIndex = 0;
            _status.SelectionChanged += delegate { if (!_syncing) Rebuild(); };
            bar.Children.Add(_status);
            var export = Buttons.Text("Exporter…", "Le tableau tel qu'il est affiché, en CSV (pour un tableur) ou en Markdown", Buttons.Bar, Buttons.Look.Outline);
            export.Tag = "outliner-export";
            export.Margin = new Thickness(0, 0, 0, 4);
            export.Click += async delegate
            {
                if (_folder == null) return;
                var path = await Ui.PickSaveFile(this, "Exporter le tableau", "Tableur (CSV)|*.csv|Markdown|*.md", SafeName(_folder.Title) + " - tableau.csv");
                if (path == null) return;
                try { System.IO.File.WriteAllText(path, ExportText(path), new UTF8Encoding(true)); }
                catch (Exception error) { MessageDialog.Show(Ui.OwnerOf(this), "Le tableau n'a pas pu être écrit : " + error.Message, "Marabook", MessageButtons.OK, MessageIcon.Warning); }
            };
            bar.Children.Add(export);
            Children.Add(bar);

            _table = new Grid { Tag = "outliner-table", HorizontalAlignment = HorizontalAlignment.Left, MinWidth = FloorTotal };
            for (var i = 0; i < Floors.Length; i++)
                _table.ColumnDefinitions.Add(Shares[i] > 0
                    ? new ColumnDefinition(Shares[i], GridUnitType.Star) { MinWidth = Floors[i] }
                    : new ColumnDefinition(Floors[i], GridUnitType.Pixel));
            var scroll = new ScrollViewer
            {
                Content = _table,
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled
            };
            _scroll = scroll;
            // Le tableau prend toute la largeur offerte, jamais moins que ses planchers.
            scroll.SizeChanged += delegate { FitWidth(); };
            // Maj + molette (ou la molette horizontale, le pavé tactile) fait défiler
            // le tableau de côté, où que soit le pointeur dessus (21/11, Rémi : la
            // barre n'était atteignable qu'en bas du tableau).
            AddHandler(InputElement.PointerWheelChangedEvent, delegate(object sender, PointerWheelEventArgs e)
            {
                var room = _scroll.Extent.Width - _scroll.Viewport.Width;
                if (room <= 0.5) return;
                var sideways = Math.Abs(e.Delta.X) > 0.001 ? e.Delta.X : (e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) == Avalonia.Input.KeyModifiers.Shift ? e.Delta.Y : 0;
                if (Math.Abs(sideways) < 0.001) return;
                ScrollSideways(-sideways * SmoothScroll.StepPx());
                e.Handled = true;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            // Fond de CHAMP, pas de papier (21/11, Rémi) : avec « papier blanc malgré
            // le mode sombre », le papier reste blanc sous une encre claire — illisible.
            Children.Add(new Border { Child = scroll, Background = Chrome.FieldBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true });
            _empty = new TextBlock { Text = "Aucun écrit à montrer.", Foreground = Chrome.SoftText, Margin = new Thickness(2, 10, 0, 0), IsVisible = false };
            Children.Add(_empty);
            _totals = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(2, 8, 0, 0), Tag = "outliner-totals" };
            Children.Add(_totals);
        }

        private void FitWidth()
        {
            var offered = _scroll.Bounds.Width;
            if (offered <= 0) return;
            var width = Math.Max(FloorTotal, Math.Floor(offered));
            if (Math.Abs(_table.Width - width) > 0.5 || double.IsNaN(_table.Width)) _table.Width = width;
        }

        internal void ScrollSideways(double by)
        {
            var room = Math.Max(0, _scroll.Extent.Width - _scroll.Viewport.Width);
            var x = Math.Max(0, Math.Min(room, _scroll.Offset.X + by));
            _scroll.Offset = new Vector(x, _scroll.Offset.Y);
        }

        private static string SafeName(string name)
        {
            return Persistence.FileNames.Clean(name, "manuscrit");
        }

        internal string ExportText(string path)
        {
            return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? Outline.Markdown(_folder.Title, _rows) : Outline.Csv(_rows);
        }

        public void Load(BinderItem folder, Project project)
        {
            _folder = folder;
            _project = project;
            Rebuild();
        }

        public void Clear()
        {
            _folder = null;
            _project = null;
            _rows = new List<OutlineRow>();
            _table.Children.Clear();
            _table.RowDefinitions.Clear();
        }

        public void Refresh() { if (_folder != null && IsVisible) Rebuild(); }

        private OutlineOptions Options()
        {
            var options = new OutlineOptions { Scenes = _scenes.IsChecked == true };
            var sort = _sort.SelectedIndex;
            options.Sort = sort >= 0 && sort < Outline.Sorts.Length ? Outline.Sorts[sort] : Outline.SortBook;
            var status = _status.SelectedIndex;
            options.Status = status <= 0 ? null : status <= TextStatus.Keys.Length ? TextStatus.Keys[status - 1] : "";
            return options;
        }

        private void Rebuild()
        {
            _table.Children.Clear();
            _table.RowDefinitions.Clear();
            if (_folder == null || _project == null) return;
            var totals = new OutlineTotals();
            _rows = Outline.Build(_project, _folder, Options(), totals);

            AddRow();
            for (var c = 0; c < Outline.Columns.Length; c++)
                Place(new TextBlock
                {
                    Text = Outline.Columns[c], FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Chrome.FaintText,
                    Margin = new Thickness(10, 7, 8, 7), HorizontalAlignment = c == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left
                }, 0, c);
            Back(0, Chrome.BarBgLight, null);

            for (var i = 0; i < _rows.Count; i++) AddLine(_rows[i], i + 1);
            _empty.IsVisible = _rows.Count == 0;
            var culture = CultureInfo.CurrentCulture;
            _totals.Text = (totals.Texts == 1 ? "1 écrit" : totals.Texts.ToString("N0", culture) + " écrits") + " · " + (totals.Scenes == 1 ? "1 scène" : totals.Scenes.ToString("N0", culture) + " scènes")
                + " · " + totals.Words.ToString("N0", culture) + (totals.Words > 1 ? " mots" : " mot");
        }

        private void AddRow() { _table.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); }

        private void Place(Control control, int row, int column)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            _table.Children.Add(control);
        }

        /// <summary>Le fond d'une ligne (derrière ses cellules) et son filet du haut.</summary>
        private Border Back(int row, IBrush background, object tag)
        {
            var back = new Border { Background = background ?? Brushes.Transparent, BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, row == 0 ? 0 : 1, 0, 0), Tag = tag, ZIndex = -1 };
            Grid.SetRow(back, row);
            Grid.SetColumnSpan(back, Floors.Length);
            _table.Children.Insert(0, back);
            return back;
        }

        private void AddLine(OutlineRow row, int at)
        {
            AddRow();
            var folder = row.Kind == OutlineRowKind.Folder;
            var scene = row.Kind == OutlineRowKind.Scene;
            Back(at, folder ? Chrome.BarBgLight : null, "outliner-row");
            var item = row.Item;

            // — Titre : en retrait selon la profondeur ; le double-clic ouvre.
            var title = new TextBlock
            {
                Text = row.Title.Length == 0 ? "(sans titre)" : row.Title,
                FontSize = scene ? 12 : 12.5, FontWeight = folder ? FontWeight.SemiBold : FontWeight.Normal, FontStyle = scene ? FontStyle.Italic : FontStyle.Normal,
                Foreground = scene ? Chrome.SoftText : Chrome.Ink, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(10 + row.Depth * 16, 6, 8, 6), Background = Brushes.Transparent, Tag = "outliner-title",
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(title, scene ? "Double-clic : ouvrir l'écrit au début de cette scène" : "Double-clic : ouvrir");
            title.DoubleTapped += delegate
            {
                if (scene) { var handler = NavigateScene; if (handler != null) handler(item, Math.Max(0, row.Scene.First)); }
                else { var handler = Navigate; if (handler != null) handler(item); }
            };
            Place(title, at, 0);

            // — État : une pastille ; un clic le change.
            if (row.Kind == OutlineRowKind.Text) Place(StatusCell(item), at, 1);

            // — Mots.
            var culture = CultureInfo.CurrentCulture;
            Place(new TextBlock
            {
                Text = row.Words.ToString("N0", culture), FontSize = 12, Foreground = folder ? Chrome.Ink : Chrome.SoftText, FontWeight = folder ? FontWeight.SemiBold : FontWeight.Normal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 6, 10, 6)
            }, at, 2);
            if (folder) return;

            // — La scène : point de vue, lieu, moment, personnages. Un clic ouvre sa fiche.
            var summary = row.Kind == OutlineRowKind.Text && row.SceneCount > 1;
            var values = new[] { row.Pov, row.Place, row.When, row.Characters };
            for (var c = 0; c < values.Length; c++)
            {
                var cell = new TextBlock
                {
                    Text = values[c].Length == 0 ? (summary ? "" : "—") : values[c], FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = values[c].Length == 0 ? Chrome.FaintText : summary ? Chrome.SoftText : Chrome.Ink,
                    Margin = new Thickness(10, 6, 8, 6), Background = Brushes.Transparent, Tag = "outliner-scene-cell"
                };
                if (summary) ToolTip.SetTip(cell, (values[c].Length > 0 ? values[c] + "\n" : "") + "Cet écrit a " + row.SceneCount + " scènes : chacune se renseigne sur sa ligne (cochez « Une ligne par scène »), ou dans le rail.");
                else
                {
                    cell.Cursor = new Cursor(StandardCursorType.Hand);
                    ToolTip.SetTip(cell, (values[c].Length > 0 ? values[c] + "\n" : "") + "Clic : renseigner la scène");
                    cell.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                    {
                        if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                        e.Handled = true;
                        var handler = SceneEditRequested;
                        if (handler != null) handler(item, row.SceneIndex);
                    };
                }
                Place(cell, at, 3 + c);
            }

            // — Synopsis de l'écrit (modifiable sur place) ; note de la scène.
            if (scene)
            {
                var note = new TextBlock { Text = row.Note, FontSize = 12, FontStyle = FontStyle.Italic, Foreground = Chrome.SoftText, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 6, 10, 6) };
                if (row.Note.Length > 0) ToolTip.SetTip(note, row.Note);
                Place(note, at, 7);
            }
            else Place(SynopsisCell(item), at, 7);
        }

        private Control StatusCell(BinderItem item)
        {
            var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var label = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 6, 6), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Tag = "outliner-state" };
            row.Children.Add(dot);
            row.Children.Add(label);
            Action paint = delegate
            {
                var has = !string.IsNullOrEmpty(item.Status);
                dot.Background = has ? new SolidColorBrush(Color.Parse(TextStatus.ColorOf(item.Status))) : Chrome.Border;
                label.Text = has ? TextStatus.Label(item.Status) : "—";
                label.Foreground = has ? Chrome.Ink : Chrome.FaintText;
            };
            paint();
            ToolTip.SetTip(row, "Clic : changer l'état de l'écrit");
            row.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                var menu = new ContextMenu();
                var keys = new List<string>(TextStatus.Keys);
                keys.Add(null);
                foreach (var key in keys)
                {
                    var captured = key;
                    var entry = new MenuItem { Header = key == null ? "Aucun état" : TextStatus.Label(key) };
                    entry.Click += delegate { SetStatus(item, captured); paint(); };
                    menu.Items.Add(entry);
                }
                menu.Open(row);
            };
            return row;
        }

        internal void SetStatus(BinderItem item, string status)
        {
            if ((item.Status ?? "") == (status ?? "")) return;
            item.Status = status;
            var handler = ItemChanged;
            if (handler != null) handler(item);
        }

        /// <summary>Le synopsis : un texte ; un clic en fait un champ, Entrée ou la sortie du champ le valide.</summary>
        private Control SynopsisCell(BinderItem item)
        {
            var host = new Panel { Tag = "outliner-synopsis" };
            var text = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 6, 10, 6), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Ibeam) };
            Action paint = delegate
            {
                var synopsis = (item.Synopsis ?? "").Replace("\r", "").Replace("\n", " ").Trim();
                text.Text = synopsis.Length == 0 ? "Écrire un synopsis…" : synopsis;
                text.Foreground = synopsis.Length == 0 ? Chrome.FaintText : Chrome.Ink;
                ToolTip.SetTip(text, synopsis.Length == 0 ? "Clic : écrire le synopsis de cet écrit" : item.Synopsis);
            };
            paint();
            host.Children.Add(text);
            text.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(text).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                var box = new TextBox { Text = item.Synopsis ?? "", FontSize = 12, AcceptsReturn = false, Margin = new Thickness(4, 2, 4, 2), Padding = new Thickness(6, 3, 6, 3), MinHeight = 0, Tag = "outliner-synopsis-box" };
                var done = false;
                Action<bool> close = delegate(bool keep)
                {
                    if (done) return;
                    done = true;
                    if (keep) SetSynopsis(item, box.Text ?? "");
                    host.Children.Remove(box);
                    text.IsVisible = true;
                    paint();
                };
                box.KeyDown += delegate(object s, KeyEventArgs k)
                {
                    if (k.Key == Key.Enter) { k.Handled = true; close(true); }
                    else if (k.Key == Key.Escape) { k.Handled = true; close(false); }
                };
                box.LostFocus += delegate { close(true); };
                text.IsVisible = false;
                host.Children.Add(box);
                Avalonia.Threading.Dispatcher.UIThread.Post(delegate { box.Focus(); box.CaretIndex = (box.Text ?? "").Length; });
            };
            return host;
        }

        internal void SetSynopsis(BinderItem item, string synopsis)
        {
            if ((item.Synopsis ?? "") == (synopsis ?? "")) return;
            item.Synopsis = synopsis ?? "";
            var handler = ItemChanged;
            if (handler != null) handler(item);
        }

        // — Sonde.
        internal List<OutlineRow> RowsForProbe { get { return _rows; } }
        internal string TotalsForProbe { get { return _totals.Text ?? ""; } }
        internal void SortForProbe(string sort) { _sort.SelectedIndex = Array.IndexOf(Outline.Sorts, sort); }
        internal void ScenesForProbe(bool shown) { _scenes.IsChecked = shown; }
        internal void StatusForProbe(int index) { _status.SelectedIndex = index; }
        internal void RaiseSceneEditForProbe(BinderItem item, int scene) { var handler = SceneEditRequested; if (handler != null) handler(item, scene); }
        internal double TableWidthForProbe { get { return _table.Bounds.Width; } }
        internal double OfferedWidthForProbe { get { return _scroll.Bounds.Width; } }
        internal double SidewaysOffsetForProbe { get { return _scroll.Offset.X; } }
        internal static double FloorTotalForProbe { get { return FloorTotal; } }
        internal IBrush TableBackgroundForProbe { get { return ((Border)_scroll.Parent).Background; } }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE DIAGRAMME RELATIONNEL (1.1.0, 27/10/2026) : l'éditeur des
    /// natures de relation du projet, en plein écran, pensé comme Mental-O —
    /// une grille aimantée à points, des tuiles arrondies, les réglages de la
    /// tuile choisie dans le rail de droite. Au centre, le « Personnage »
    /// (génération 0, ligne directe) ; autour, une tuile par FAMILLE de
    /// relation (« Père · Mère · Parent »), à sa génération (les rangées :
    /// aïeux en haut, descendants en bas) et dans sa voie (les bandes :
    /// collatéraux, ligne directe, partenaires, autres relations). Glisser une
    /// tuile change sa génération et sa voie ; la poignée au bord d'une tuile,
    /// tirée sur une autre, fixe la réciproque (« Père » répond « Fils ») ; un
    /// double-clic sur une case vide crée une relation. « Appliquer et
    /// fermer » remplace le schéma du projet — et renomme dans les fiches les
    /// natures renommées, après accord (« X personnages utilisent la relation…
    /// »). Les relations des fiches ne sont jamais touchées autrement : le
    /// schéma dit ce qui se propose et où ça se dessine.</summary>
    public class RelationsEditorWindow : Window
    {
        private const double CellW = 160, CellH = 104, TileW = 136, TileH = 64, LabelW = 130, HeaderH = 40, Dot = 20, Handle = 12;
        private const int Generations = 7; // -3 … +3
        private static readonly RelationLane[] Bands = { RelationLane.Side, RelationLane.Direct, RelationLane.Partner, RelationLane.Other };
        private static readonly int[] DefaultBandSize = { 3, 3, 4, 3 };
        private readonly int[] _bandSize = { 3, 3, 4, 3 }; // extensibles : « + » sur chaque bande (27/10 soir)
        private static readonly string[] BandNames = { "Collatéraux", "Ligne directe", "Partenaires", "Autres relations" };

        private readonly Project _project;
        private readonly RelationSchema _schema;
        private readonly Canvas _canvas;
        private readonly StackPanel _rail;
        private readonly Dictionary<string, Border> _tiles = new Dictionary<string, Border>();
        private readonly Dictionary<string, Rect> _rects = new Dictionary<string, Rect>();
        private Border _selfTile;
        private string _selected;
        // Glisser une tuile / tirer une poignée
        private string _dragging, _linking;
        private Point _dragOffset, _pressAt;
        private bool _moved;
        private Line _linkLine;
        private bool _syncing;

        public static Task<bool> Open(Window owner, Project project)
        {
            return new RelationsEditorWindow(project).ShowDialog<bool>(owner);
        }

        /// <summary>Sonde : le schéma en cours d'édition.</summary>
        internal RelationSchema SchemaForProbe { get { return _schema; } }
        internal int TileCountForProbe { get { return _tiles.Count; } }
        internal string SelectedForProbe { get { return _selected; } }

        public RelationsEditorWindow(Project project)
        {
            _project = project;
            _schema = project.RelationSchema.Clone();
            // Les bandes : leur taille livrée, ou plus si le schéma a déjà des cases au-delà.
            foreach (var kind in _schema.Kinds)
            {
                var band = BandOf(kind.Lane);
                if (band >= 0 && kind.Slot + 1 > _bandSize[band]) _bandSize[band] = kind.Slot + 1;
            }
            Title = "Diagramme relationnel";
            WindowState = WindowState.Maximized;
            MinWidth = 900;
            MinHeight = 600;
            Background = Chrome.WindowBg;

            var root = new DockPanel();

            // ---- la bannière
            var banner = new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8)
            };
            DockPanel.SetDock(banner, Dock.Top);
            var bannerRow = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icons.Make("family-tree", 18, Chrome.Accent);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 10, 0);
            bannerRow.Children.Add(icon);
            bannerRow.Children.Add(new TextBlock { Text = "Diagramme relationnel", Foreground = Chrome.Ink, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            bannerRow.Children.Add(new TextBlock
            {
                Text = "les natures de relation du projet et leur place dans l'arbre · glisser une tuile la déplace · la poignée fixe la réciproque · double-clic sur une case vide : nouvelle relation",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 2, 0, 0)
            });
            banner.Child = bannerRow;
            root.Children.Add(banner);

            // ---- le pied : Annuler, Appliquer et fermer
            var foot = new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            DockPanel.SetDock(foot, Dock.Bottom);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = Buttons.Text("Annuler", "Fermer sans rien changer", Buttons.Bar, Buttons.Look.Outline);
            cancel.MinWidth = 100;
            cancel.IsCancel = true;
            cancel.Click += delegate { Close(false); };
            var apply = Buttons.Text("Appliquer et fermer", "Remplacer les natures de relation du projet par celles-ci", Buttons.Bar, Buttons.Look.Primary);
            apply.MinWidth = 160;
            apply.Tag = "relations-apply"; // sonde
            apply.Click += async delegate { await Apply(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(apply);
            Dialogs.Arrange(buttons, apply);
            foot.Child = buttons;
            root.Children.Add(foot);

            // ---- le rail : les réglages de la tuile choisie
            _rail = new StackPanel { Margin = new Thickness(14) };
            var railBorder = new Border
            {
                Width = 290,
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _rail }
            };
            DockPanel.SetDock(railBorder, Dock.Right);
            root.Children.Add(railBorder);

            // ---- la toile : grille à points, bandes et rangées, tuiles
            _canvas = new Canvas { Background = Brushes.Transparent };
            _canvas.PointerPressed += OnPressed;
            _canvas.PointerMoved += OnMoved;
            _canvas.PointerReleased += OnReleased;
            _dots = new DotGrid();
            Resize();
            var stage = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            stage.Children.Add(_dots);
            stage.Children.Add(_canvas);
            root.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Chrome.PaperBg,
                Content = stage
            });
            Content = root;
            Redraw();
            RefreshRail();
        }

        // ------------------------------------------------------------ la grille

        private DotGrid _dots;

        /// <summary>La toile à la taille des bandes (elles s'élargissent au « + »).</summary>
        private void Resize()
        {
            _canvas.Width = LabelW + Columns() * CellW + 40;
            _canvas.Height = HeaderH + Generations * CellH + 40;
            _dots.Width = _canvas.Width;
            _dots.Height = _canvas.Height;
        }

        /// <summary>Une colonne de plus dans une bande (27/10 soir, Rémi) : la place
        /// ne manque plus pour de nouveaux niveaux ; les bandes suivantes glissent.</summary>
        internal void AddColumn(RelationLane lane)
        {
            var band = BandOf(lane);
            if (band < 0) return;
            _bandSize[band]++;
            Resize();
            Redraw();
        }

        /// <summary>La dernière colonne d'une bande, si elle est vide et au-delà du livré.</summary>
        internal void RemoveColumn(RelationLane lane)
        {
            var band = BandOf(lane);
            if (band < 0 || _bandSize[band] <= DefaultBandSize[band]) return;
            var last = _bandSize[band] - 1;
            foreach (var kind in _schema.Kinds) if (kind.Lane == lane && kind.Slot >= last) return;
            _bandSize[band]--;
            Resize();
            Redraw();
        }

        internal int BandSizeForProbe(RelationLane lane) { return _bandSize[BandOf(lane)]; }

        private int Columns()
        {
            var n = 0;
            foreach (var size in _bandSize) n += size;
            return n;
        }

        private int BandStart(int band)
        {
            var start = 0;
            for (var i = 0; i < band; i++) start += _bandSize[i];
            return start;
        }

        private static int BandOf(RelationLane lane)
        {
            return Array.IndexOf(Bands, lane);
        }

        /// <summary>La colonne d'une case : la ligne directe met sa case 0 au
        /// centre de sa bande (sur le personnage), les autres se suivent.</summary>
        private int ColumnOf(RelationLane lane, int slot)
        {
            var band = BandOf(lane);
            var size = _bandSize[band];
            slot = Math.Max(0, Math.Min(size - 1, slot));
            if (lane == RelationLane.Direct) slot = slot == 0 ? 1 : slot == 1 ? 0 : 2;
            return BandStart(band) + slot;
        }

        private static int RowOf(int generation)
        {
            return Math.Max(0, Math.Min(Generations - 1, generation + 3));
        }

        private static Rect CellRect(int column, int row)
        {
            return new Rect(LabelW + column * CellW + (CellW - TileW) / 2, HeaderH + row * CellH + (CellH - TileH) / 2, TileW, TileH);
        }

        /// <summary>La case sous un point : voie, case dans la voie, génération — ou faux hors grille.</summary>
        private bool CellAt(Point point, out RelationLane lane, out int slot, out int generation)
        {
            lane = RelationLane.Other; slot = 0; generation = 0;
            var column = (int)Math.Floor((point.X - LabelW) / CellW);
            var row = (int)Math.Floor((point.Y - HeaderH) / CellH);
            if (column < 0 || column >= Columns() || row < 0 || row >= Generations) return false;
            var band = 0;
            while (band < Bands.Length - 1 && column >= BandStart(band + 1)) band++;
            lane = Bands[band];
            slot = column - BandStart(band);
            if (lane == RelationLane.Direct) slot = slot == 1 ? 0 : slot == 0 ? 1 : 2;
            generation = row - 3;
            return true;
        }

        private string FamilyAt(RelationLane lane, int slot, int generation, string except)
        {
            foreach (var kind in _schema.Kinds)
                if (kind.Family != except && kind.Lane == lane && kind.Slot == slot && kind.Generation == generation) return kind.Family;
            return null;
        }

        private bool IsSelfCell(RelationLane lane, int slot, int generation)
        {
            return lane == RelationLane.Direct && slot == 0 && generation == 0;
        }

        /// <summary>Une case libre dans cette voie et cette génération (-1 : pleine).</summary>
        private int FreeSlot(RelationLane lane, int generation, string except)
        {
            var size = _bandSize[BandOf(lane)];
            for (var slot = 0; slot < size; slot++)
                if (!IsSelfCell(lane, slot, generation) && FamilyAt(lane, slot, generation, except) == null) return slot;
            return -1;
        }

        // ------------------------------------------------------------ le dessin

        private void Redraw()
        {
            _canvas.Children.Clear();
            _tiles.Clear();
            _rects.Clear();

            // Les en-têtes des bandes, les libellés des rangées.
            for (var band = 0; band < Bands.Length; band++)
            {
                var x = LabelW + BandStart(band) * CellW;
                var width = _bandSize[band] * CellW;
                // L'en-tête : le nom de la bande, « − » et « + » pour ses colonnes.
                var headRow = new Grid();
                headRow.Children.Add(new TextBlock
                {
                    Text = BandNames[band] + "  ·  " + _bandSize[band] + (_bandSize[band] > 1 ? " colonnes" : " colonne"),
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var columnButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                var laneOfBand = Bands[band];
                var fewer = Buttons.Icon("minus", "Retirer la dernière colonne de cette bande (si elle est vide)", 22, Buttons.Look.Calm);
                fewer.IsVisible = _bandSize[band] > DefaultBandSize[band];
                fewer.Click += delegate { RemoveColumn(laneOfBand); };
                columnButtons.Children.Add(fewer);
                var more = Buttons.Icon("plus-bold", "Une colonne de plus dans cette bande : de la place pour de nouveaux niveaux", 22, Buttons.Look.Calm);
                more.Tag = "band-plus:" + laneOfBand; // sonde
                more.Click += delegate { AddColumn(laneOfBand); };
                columnButtons.Children.Add(more);
                headRow.Children.Add(columnButtons);
                var header = new Border
                {
                    Width = width - 12,
                    Height = HeaderH - 10,
                    Margin = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    Background = Chrome.BarBgLight,
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    Child = headRow
                };
                Canvas.SetLeft(header, x + 6);
                Canvas.SetTop(header, 5);
                _canvas.Children.Add(header);
                if (band > 0)
                {
                    var rule = new Line { StartPoint = new Point(x, HeaderH), EndPoint = new Point(x, HeaderH + Generations * CellH), Stroke = Chrome.Border, StrokeThickness = 1, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 4 } };
                    _canvas.Children.Add(rule);
                }
            }
            for (var row = 0; row < Generations; row++)
            {
                var caption = new TextBlock
                {
                    Text = GenealogyView.GenerationCaption(row - 3),
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    Width = LabelW - 20,
                    TextWrapping = TextWrapping.Wrap
                };
                Canvas.SetLeft(caption, 10);
                Canvas.SetTop(caption, HeaderH + row * CellH + CellH / 2 - 10);
                _canvas.Children.Add(caption);
            }

            // Les positions des tuiles (une par famille), le personnage.
            var selfRect = CellRect(ColumnOf(RelationLane.Direct, 0), RowOf(0));
            foreach (var family in _schema.Families())
            {
                var first = _schema.OfFamily(family)[0];
                _rects[family] = CellRect(ColumnOf(first.Lane, first.Slot), RowOf(first.Generation));
            }

            // Les liens, SOUS les tuiles : du personnage à chaque tuile (la nature
            // suit la voie), puis les réciproques en pointillé d'accent.
            var selfCenter = selfRect.Center;
            foreach (var pair in _rects)
            {
                var first = _schema.OfFamily(pair.Key)[0];
                var center = pair.Value.Center;
                if (first.Lane == RelationLane.Partner) DrawPartner(selfCenter, center);
                else if (first.Lane == RelationLane.Direct) DrawElbow(selfCenter, center, first.Generation < 0);
                else if (first.Lane == RelationLane.Side) DrawSide(selfCenter, center);
            }
            var drawn = new HashSet<string>();
            foreach (var family in _schema.Families())
            {
                var first = _schema.OfFamily(family)[0];
                var reciprocal = string.IsNullOrEmpty(first.Reciprocal) ? family : first.Reciprocal;
                if (reciprocal == family || !_rects.ContainsKey(reciprocal)) continue;
                var key = string.CompareOrdinal(family, reciprocal) < 0 ? family + "|" + reciprocal : reciprocal + "|" + family;
                if (!drawn.Add(key)) continue;
                var a = _rects[family].Center; var b = _rects[reciprocal].Center;
                _canvas.Children.Add(new Line
                {
                    StartPoint = a, EndPoint = b,
                    Stroke = Chrome.Accent, StrokeThickness = 1.5, Opacity = 0.7,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 3 }
                });
            }

            // Le personnage, puis les tuiles et leurs poignées.
            _selfTile = SelfTile();
            Canvas.SetLeft(_selfTile, selfRect.X);
            Canvas.SetTop(_selfTile, selfRect.Y);
            _canvas.Children.Add(_selfTile);
            foreach (var pair in _rects)
            {
                var tile = Tile(pair.Key, pair.Key == _selected);
                Canvas.SetLeft(tile, pair.Value.X);
                Canvas.SetTop(tile, pair.Value.Y);
                _canvas.Children.Add(tile);
                _tiles[pair.Key] = tile;
                var handle = new Ellipse
                {
                    Width = Handle, Height = Handle,
                    Fill = Chrome.Accent, Stroke = Chrome.PaperBg, StrokeThickness = 2,
                    Cursor = new Cursor(StandardCursorType.Cross),
                    [ToolTip.TipProperty] = "Tirer sur une autre tuile : elle devient la réciproque (ce que l'autre fiche déclare en retour) ; lâcher dans le vide : la relation se répond à elle-même"
                };
                Canvas.SetLeft(handle, pair.Value.Right - Handle / 2);
                Canvas.SetTop(handle, pair.Value.Y + TileH / 2 - Handle / 2);
                _canvas.Children.Add(handle);
            }
        }

        private Border SelfTile()
        {
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            column.Children.Add(new TextBlock { Text = "Personnage", Foreground = Chrome.AccentStrong, FontSize = 14, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center });
            column.Children.Add(new TextBlock { Text = "la fiche", Foreground = Chrome.SoftText, FontSize = 11, TextAlignment = TextAlignment.Center });
            return new Border
            {
                Width = TileW, Height = TileH,
                Background = Chrome.AccentTint,
                BorderBrush = Chrome.Accent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(12),
                Child = column,
                Tag = "self-tile", // sonde
                [ToolTip.TipProperty] = "Le personnage de la fiche : tout s'ordonne autour de lui"
            };
        }

        /// <summary>Le libellé d'une famille : ses formes, « · » entre elles.</summary>
        private string FamilyLabel(string family)
        {
            var names = new List<string>();
            foreach (var kind in _schema.OfFamily(family)) if (!string.IsNullOrEmpty(kind.Name)) names.Add(kind.Name);
            return names.Count == 0 ? "(sans nom)" : string.Join(" · ", names.ToArray());
        }

        private Border Tile(string family, bool selected)
        {
            var tile = new Border
            {
                Width = TileW, Height = TileH,
                Background = selected ? Chrome.AccentTint : (IBrush)Chrome.CardBg,
                BorderBrush = selected ? Chrome.Accent : (IBrush)Chrome.Border,
                BorderThickness = new Thickness(selected ? 2 : 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 4, 8, 4),
                Child = TileContent(family),
                Cursor = new Cursor(StandardCursorType.Hand),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, OffsetY = 2, Color = Color.FromArgb(0x22, 0, 0, 0) }),
                Tag = "family:" + family // sonde
            };
            ToolTip.SetTip(tile, FamilyLabel(family) + " — glisser pour changer de génération ou de voie, cliquer pour régler");
            return tile;
        }

        /// <summary>Le contenu d'une tuile : ses formes, puis sa réciproque.</summary>
        private StackPanel TileContent(string family)
        {
            var kinds = _schema.OfFamily(family);
            var first = kinds[0];
            var reciprocal = string.IsNullOrEmpty(first.Reciprocal) ? family : first.Reciprocal;
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            column.Children.Add(new TextBlock
            {
                Text = FamilyLabel(family),
                Foreground = Chrome.Ink,
                FontSize = 12.5,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 34
            });
            column.Children.Add(new TextBlock
            {
                Text = reciprocal == family ? "↔ elle-même" : "↔ " + FamilyLabel(reciprocal),
                Foreground = Chrome.SoftText,
                FontSize = 10.5,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            return column;
        }

        private void DrawElbow(Point from, Point to, bool upward)
        {
            var startY = upward ? from.Y - TileH / 2 : from.Y + TileH / 2;
            var endY = upward ? to.Y + TileH / 2 : to.Y - TileH / 2;
            var midY = (startY + endY) / 2;
            AddLine(from.X, startY, from.X, midY);
            AddLine(from.X, midY, to.X, midY);
            AddLine(to.X, midY, to.X, endY);
        }

        private void DrawSide(Point from, Point to)
        {
            var busY = from.Y - TileH / 2 - 14;
            if (Math.Abs(to.Y - from.Y) < 1)
            {
                AddLine(from.X, from.Y - TileH / 2, from.X, busY);
                AddLine(from.X, busY, to.X, busY);
                AddLine(to.X, busY, to.X, to.Y - TileH / 2);
            }
            else DrawElbow(from, to, to.Y < from.Y);
        }

        private void DrawPartner(Point from, Point to)
        {
            var left = Math.Min(from.X, to.X) + TileW / 2;
            var right = Math.Max(from.X, to.X) - TileW / 2;
            if (Math.Abs(to.Y - from.Y) < 1)
            {
                AddLine(left, from.Y - 3, right, from.Y - 3);
                AddLine(left, from.Y + 3, right, from.Y + 3);
            }
            else DrawElbow(from, to, to.Y < from.Y);
        }

        private void AddLine(double x1, double y1, double x2, double y2)
        {
            _canvas.Children.Add(new Line { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), Stroke = Chrome.SoftText, StrokeThickness = 1.5, Opacity = 0.6 });
        }

        // ------------------------------------------------------------ la souris

        private string FamilyUnder(Point point)
        {
            foreach (var pair in _rects) if (pair.Value.Contains(point)) return pair.Key;
            return null;
        }

        private string HandleUnder(Point point)
        {
            foreach (var pair in _rects)
            {
                var center = new Point(pair.Value.Right, pair.Value.Y + TileH / 2);
                if (Math.Abs(point.X - center.X) <= Handle && Math.Abs(point.Y - center.Y) <= Handle) return pair.Key;
            }
            return null;
        }

        private void OnPressed(object sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(_canvas).Properties;
            if (!props.IsLeftButtonPressed) return;
            var point = e.GetPosition(_canvas);
            _pressAt = point;
            _moved = false;
            var handle = HandleUnder(point);
            if (handle != null)
            {
                _linking = handle;
                var from = new Point(_rects[handle].Right, _rects[handle].Y + TileH / 2);
                _linkLine = new Line { StartPoint = from, EndPoint = point, Stroke = Chrome.Accent, StrokeThickness = 2, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 } };
                _canvas.Children.Add(_linkLine);
                e.Pointer.Capture(_canvas);
                e.Handled = true;
                return;
            }
            var family = FamilyUnder(point);
            if (family != null)
            {
                _dragging = family;
                _dragOffset = new Point(point.X - _rects[family].X, point.Y - _rects[family].Y);
                Select(family);
                e.Pointer.Capture(_canvas);
                e.Handled = true;
                return;
            }
            if (e.ClickCount == 2)
            {
                RelationLane lane; int slot, generation;
                if (CellAt(point, out lane, out slot, out generation) && !IsSelfCell(lane, slot, generation) && FamilyAt(lane, slot, generation, null) == null)
                    CreateFamily(lane, slot, generation);
                e.Handled = true;
                return;
            }
            Select(null);
        }

        private void OnMoved(object sender, PointerEventArgs e)
        {
            var point = e.GetPosition(_canvas);
            if (_linking != null && _linkLine != null)
            {
                _linkLine.EndPoint = point;
                return;
            }
            if (_dragging == null) return;
            if (!_moved && ((Vector)(point - _pressAt)).Length < 4) return;
            _moved = true;
            Border tile;
            if (_tiles.TryGetValue(_dragging, out tile))
            {
                Canvas.SetLeft(tile, point.X - _dragOffset.X);
                Canvas.SetTop(tile, point.Y - _dragOffset.Y);
                tile.ZIndex = 10;
            }
        }

        private void OnReleased(object sender, PointerReleasedEventArgs e)
        {
            var point = e.GetPosition(_canvas);
            if (_linking != null)
            {
                var source = _linking;
                _linking = null;
                if (_linkLine != null) { _canvas.Children.Remove(_linkLine); _linkLine = null; }
                var target = FamilyUnder(point);
                SetReciprocal(source, target ?? source);
                e.Pointer.Capture(null);
                return;
            }
            if (_dragging == null) return;
            var family = _dragging;
            _dragging = null;
            e.Pointer.Capture(null);
            if (!_moved) { Redraw(); return; }
            var dropAt = new Point(point.X - _dragOffset.X + TileW / 2, point.Y - _dragOffset.Y + TileH / 2);
            RelationLane lane; int slot, generation;
            if (CellAt(dropAt, out lane, out slot, out generation) && !IsSelfCell(lane, slot, generation))
                MoveFamily(family, lane, slot, generation);
            Redraw();
            RefreshRail();
        }

        /// <summary>Pose une famille sur une case : la famille qui l'occupait prend
        /// l'ancienne case (échange) — ou la première case libre de la voie.</summary>
        internal void MoveFamily(string family, RelationLane lane, int slot, int generation)
        {
            var kinds = _schema.OfFamily(family);
            if (kinds.Count == 0) return;
            var oldLane = kinds[0].Lane; var oldSlot = kinds[0].Slot; var oldGeneration = kinds[0].Generation;
            if (oldLane == lane && oldSlot == slot && oldGeneration == generation) return;
            var occupant = FamilyAt(lane, slot, generation, family);
            foreach (var kind in kinds) { kind.Lane = lane; kind.Slot = slot; kind.Generation = generation; }
            if (occupant != null)
                foreach (var kind in _schema.OfFamily(occupant)) { kind.Lane = oldLane; kind.Slot = oldSlot; kind.Generation = oldGeneration; }
        }

        internal void SetReciprocal(string family, string reciprocal)
        {
            var kinds = _schema.OfFamily(family);
            if (kinds.Count == 0) return;
            // L'ancienne réciproque, si elle nous répondait, se répond désormais à elle-même.
            var previous = string.IsNullOrEmpty(kinds[0].Reciprocal) ? family : kinds[0].Reciprocal;
            if (previous != family && previous != reciprocal)
                foreach (var kind in _schema.OfFamily(previous)) if (kind.Reciprocal == family) kind.Reciprocal = previous;
            foreach (var kind in kinds) kind.Reciprocal = reciprocal;
            if (reciprocal != family)
                foreach (var kind in _schema.OfFamily(reciprocal)) kind.Reciprocal = family;
            Redraw();
            RefreshRail();
        }

        /// <summary>Une relation neuve sur une case : une forme neutre « Nouvelle
        /// relation », réciproque d'elle-même ; choisie, le rail attend son nom.</summary>
        internal string CreateFamily(RelationLane lane, int slot, int generation)
        {
            var n = 1;
            string name;
            do { name = "Nouvelle relation" + (n == 1 ? "" : " " + n); n++; } while (_schema.Find(name) != null);
            var family = _schema.FreeFamilyKey(name);
            _schema.Kinds.Add(new RelationKindInfo { Name = name, Family = family, Gender = 'n', Generation = generation, Lane = lane, Slot = slot, Reciprocal = family });
            Select(family);
            return family;
        }

        private void Select(string family)
        {
            _selected = family;
            Redraw();
            RefreshRail();
        }

        // ------------------------------------------------------------ le rail

        private void RefreshRail()
        {
            _rail.Children.Clear();
            _rail.Children.Add(new TextBlock { Text = "Général", Foreground = Chrome.Ink, FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            _rail.Children.Add(new TextBlock { Text = "Diagramme relationnel", Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 0, 0, 12) });

            var create = Buttons.IconText("plus-bold", "Nouvelle relation", "Une relation neuve dans « Autres relations » (ou double-clic sur une case vide)", Buttons.Bar, Buttons.Look.Primary);
            create.HorizontalAlignment = HorizontalAlignment.Stretch;
            create.Margin = new Thickness(0, 0, 0, 12);
            create.Click += delegate
            {
                for (var generation = 0; generation <= 3; generation++)
                {
                    var free = FreeSlot(RelationLane.Other, generation, null);
                    if (free >= 0) { CreateFamily(RelationLane.Other, free, generation); return; }
                }
                for (var generation = -1; generation >= -3; generation--)
                {
                    var free = FreeSlot(RelationLane.Other, generation, null);
                    if (free >= 0) { CreateFamily(RelationLane.Other, free, generation); return; }
                }
            };
            _rail.Children.Add(create);
            _rail.Children.Add(Divider());

            if (_selected == null || _schema.OfFamily(_selected).Count == 0)
            {
                _rail.Children.Add(new TextBlock
                {
                    Text = "Cliquez une relation pour la régler : ses noms (masculin, féminin, neutre), sa voie, sa génération, sa réciproque.\n\nLe Personnage ne se règle pas : tout s'ordonne autour de lui.",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            var family = _selected;
            var kinds = _schema.OfFamily(family);
            var first = kinds[0];
            _syncing = true;
            try
            {
                _rail.Children.Add(Caption("Relation"));
                _rail.Children.Add(NameField("Masculin", family, 'm', "« Père », « Frère »… — vide : pas de forme masculine"));
                _rail.Children.Add(NameField("Féminin", family, 'f', "« Mère », « Sœur »… — vide : pas de forme féminine"));
                _rail.Children.Add(NameField("Neutre", family, 'n', "« Parent », « Adelphe »… — la forme proposée quand le genre est inconnu"));
                _rail.Children.Add(Note("Au moins une forme ; la fiche d'en face reçoit la forme qui va avec le genre du personnage."));

                _rail.Children.Add(Divider());
                _rail.Children.Add(Caption("Dans l'arbre"));
                _rail.Children.Add(Label("Voie"));
                var lane = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                for (var i = 0; i < Bands.Length; i++) lane.Items.Add(new ComboBoxItem { Content = BandNames[i], Tag = Bands[i] });
                lane.SelectedIndex = BandOf(first.Lane);
                ToolTip.SetTip(lane, "La bande du diagramme — ligne directe, collatéraux, partenaires ; « Autres relations » = une nature libre, hors généalogie");
                lane.SelectionChanged += delegate
                {
                    if (_syncing) return;
                    var choice = lane.SelectedItem as ComboBoxItem;
                    if (choice == null) return;
                    var wanted = (RelationLane)choice.Tag;
                    var kindsNow = _schema.OfFamily(family);
                    if (kindsNow.Count == 0 || kindsNow[0].Lane == wanted) return;
                    var free = FreeSlot(wanted, kindsNow[0].Generation, family);
                    if (free < 0) free = _bandSize[BandOf(wanted)] - 1;
                    MoveFamily(family, wanted, free, kindsNow[0].Generation);
                    Redraw();
                    RefreshRail();
                };
                _rail.Children.Add(lane);
                _rail.Children.Add(Label("Génération"));
                var generation = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                for (var g = -3; g <= 3; g++) generation.Items.Add(new ComboBoxItem { Content = (g > 0 ? "+" : "") + g + " — " + GenealogyView.GenerationCaption(g), Tag = g });
                generation.SelectedIndex = RowOf(first.Generation);
                ToolTip.SetTip(generation, "La rangée du diagramme : négatif = ascendants, positif = descendants");
                generation.SelectionChanged += delegate
                {
                    if (_syncing) return;
                    var choice = generation.SelectedItem as ComboBoxItem;
                    if (choice == null) return;
                    var wanted = (int)choice.Tag;
                    var kindsNow = _schema.OfFamily(family);
                    if (kindsNow.Count == 0 || kindsNow[0].Generation == wanted) return;
                    var free = FreeSlot(kindsNow[0].Lane, wanted, family);
                    if (free < 0) free = _bandSize[BandOf(kindsNow[0].Lane)] - 1;
                    MoveFamily(family, kindsNow[0].Lane, free, wanted);
                    Redraw();
                    RefreshRail();
                };
                _rail.Children.Add(generation);

                _rail.Children.Add(Label("Réciproque"));
                var reciprocal = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                reciprocal.Items.Add(new ComboBoxItem { Content = "Elle-même", Tag = family });
                var current = string.IsNullOrEmpty(first.Reciprocal) ? family : first.Reciprocal;
                reciprocal.SelectedIndex = 0;
                foreach (var other in _schema.Families())
                {
                    if (other == family) continue;
                    var item = new ComboBoxItem { Content = FamilyLabel(other), Tag = other };
                    reciprocal.Items.Add(item);
                    if (other == current) reciprocal.SelectedItem = item;
                }
                ToolTip.SetTip(reciprocal, "Ce que la fiche d'en face déclare en retour : « Père » répond « Fils » (ou « Fille », « Enfant » selon le genre)");
                reciprocal.SelectionChanged += delegate
                {
                    if (_syncing) return;
                    var choice = reciprocal.SelectedItem as ComboBoxItem;
                    if (choice != null) SetReciprocal(family, (string)choice.Tag);
                };
                _rail.Children.Add(reciprocal);

                _rail.Children.Add(Divider());
                var used = UsersOf(family);
                _rail.Children.Add(Note(used == 0 ? "Aucune fiche n'emploie cette relation."
                    : used + (used > 1 ? " fiches emploient" : " fiche emploie") + " cette relation : la renommer les modifiera, après accord."));
                var remove = Buttons.IconText("trash-bold", "Supprimer la relation", "Retirer cette relation des choix proposés — les fiches qui l'emploient la gardent, en nature libre", Buttons.Bar, Buttons.Look.Outline);
                remove.HorizontalAlignment = HorizontalAlignment.Stretch;
                remove.Background = new SolidColorBrush(Chrome.Warn.Color, 0.16);
                remove.BorderBrush = new SolidColorBrush(Chrome.Warn.Color, 0.7);
                remove.Click += delegate
                {
                    _schema.Kinds.RemoveAll(k => k.Family == family);
                    foreach (var kind in _schema.Kinds) if (kind.Reciprocal == family) kind.Reciprocal = kind.Family;
                    Select(null);
                };
                _rail.Children.Add(remove);
            }
            finally { _syncing = false; }
        }

        /// <summary>Le champ d'une forme (masculin, féminin, neutre) : taper renomme la
        /// forme, vider la retire (sauf la dernière), remplir un vide la crée.</summary>
        private Control NameField(string label, string family, char gender, string tip)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(Label(label));
            RelationKindInfo existing = null;
            foreach (var kind in _schema.OfFamily(family)) if (kind.Gender == gender) existing = kind;
            var box = new TextBox { Text = existing == null ? "" : existing.Name, Watermark = "—", Tag = "name:" + gender };
            ToolTip.SetTip(box, tip);
            box.TextChanged += delegate
            {
                if (_syncing) return;
                var text = (box.Text ?? "").Trim();
                var kinds = _schema.OfFamily(family);
                RelationKindInfo mine = null;
                foreach (var kind in kinds) if (kind.Gender == gender) mine = kind;
                if (text == (mine == null ? "" : mine.Name)) return; // rien de neuf (l'événement différé du gabarit)
                if (kinds.Count == 0) return;
                if (text.Length == 0)
                {
                    if (mine != null && kinds.Count > 1) _schema.Kinds.Remove(mine);
                    else if (mine != null) mine.Name = ""; // la dernière forme : vide tant qu'on tape
                }
                else if (mine != null) mine.Name = text;
                else
                {
                    var model = kinds[0];
                    _schema.Kinds.Insert(_schema.Kinds.IndexOf(model) + 1, new RelationKindInfo
                    {
                        Name = text, Family = family, Gender = gender,
                        Generation = model.Generation, Lane = model.Lane, Slot = model.Slot, Reciprocal = model.Reciprocal
                    });
                }
                RefreshTile(family);
            };
            row.Children.Add(box);
            return row;
        }

        /// <summary>Le libellé d'une tuile seul, sans tout redessiner (la frappe dans le rail).</summary>
        private void RefreshTile(string family)
        {
            Border tile;
            if (!_tiles.TryGetValue(family, out tile)) return;
            tile.Child = TileContent(family);
            ToolTip.SetTip(tile, FamilyLabel(family) + " — glisser pour changer de génération ou de voie, cliquer pour régler");
            // Les tuiles qui répondent à celle-ci portent son nom.
            foreach (var other in _schema.Families())
            {
                if (other == family) continue;
                var first = _schema.OfFamily(other)[0];
                Border otherTile;
                if (first.Reciprocal == family && _tiles.TryGetValue(other, out otherTile)) otherTile.Child = TileContent(other);
            }
        }

        private static Control Caption(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.Ink, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        }

        private static Control Label(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 10, Margin = new Thickness(2, 4, 0, 1) };
        }

        private static Control Note(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        }

        private static Control Divider()
        {
            return new Border { Height = 1, Background = Chrome.Ink, Opacity = 0.3, Margin = new Thickness(0, 6, 0, 10) };
        }

        // ------------------------------------------------------------ appliquer

        /// <summary>Les fiches qui emploient une forme de cette famille (par son nom actuel ou d'origine).</summary>
        private int UsersOf(string family)
        {
            var names = new List<string>();
            foreach (var kind in _schema.OfFamily(family)) names.Add(kind.Name);
            foreach (var kind in _project.RelationSchema.OfFamily(family)) if (!names.Contains(kind.Name)) names.Add(kind.Name);
            return CountUsers(names);
        }

        private int CountUsers(List<string> names)
        {
            var count = 0;
            foreach (var item in _project.AllItems())
            {
                if (item.Kind != ItemKind.Sheet) continue;
                var uses = false;
                foreach (var relation in item.Relations)
                    foreach (var name in names)
                        if (name.Length > 0 && RelationKinds.Same(relation.Kind, name)) { uses = true; break; }
                if (uses) count++;
            }
            return count;
        }

        /// <summary>Les natures renommées ou supprimées qu'une fiche emploie :
        /// une question par nature ; non = rien n'est appliqué. Oui : les fiches
        /// prennent le nouveau nom (une nature supprimée reste telle quelle, en
        /// nature libre), le schéma remplace celui du projet.</summary>
        private async Task Apply()
        {
            // Une forme sans nom (vidée dans le rail) disparaît ; une famille vidée aussi.
            _schema.Kinds.RemoveAll(k => string.IsNullOrEmpty(k.Name));
            var original = _project.RelationSchema;
            var renames = new List<KeyValuePair<string, string>>();
            var removed = new List<string>();
            foreach (var old in original.Kinds)
            {
                RelationKindInfo match = null;
                foreach (var kind in _schema.Kinds) if (kind.Family == old.Family && kind.Gender == old.Gender) { match = kind; break; }
                if (match == null) removed.Add(old.Name);
                else if (match.Name != old.Name) renames.Add(new KeyValuePair<string, string>(old.Name, match.Name));
            }
            foreach (var rename in renames)
            {
                var users = CountUsers(new List<string> { rename.Key });
                if (users == 0) continue;
                if (!await Confirm(users, rename.Key)) return;
            }
            foreach (var name in removed)
            {
                var users = CountUsers(new List<string> { name });
                if (users == 0) continue;
                if (!await Confirm(users, name)) return;
            }
            foreach (var item in _project.AllItems())
            {
                if (item.Kind != ItemKind.Sheet) continue;
                foreach (var relation in item.Relations)
                    foreach (var rename in renames)
                        if (RelationKinds.Same(relation.Kind, rename.Key)) { relation.Kind = rename.Value; break; }
            }
            _project.RelationSchema = _schema;
            _project.RelationKinds = _schema.FreeNames(); // l'ancienne liste suit (sans les natures retirées ni les anciens noms)
            Close(true);
        }

        private async Task<bool> Confirm(int users, string name)
        {
            var text = users + (users > 1 ? " personnages utilisent" : " personnage utilise") + " la relation « " + name + " ». La changer modifiera également "
                + (users > 1 ? "leurs fiches" : "sa fiche") + ". Souhaitez-vous continuer ?";
            var answer = await MessageDialog.Show(this, text, "Diagramme relationnel", MessageButtons.YesNo, MessageIcon.Question);
            return answer == MessageResult.Yes;
        }

        /// <summary>Sonde : appliquer sans dialogue (aucune nature employée ne change).</summary>
        internal async Task<bool> ApplyForProbe()
        {
            await Apply();
            return _project.RelationSchema == _schema;
        }
    }
}

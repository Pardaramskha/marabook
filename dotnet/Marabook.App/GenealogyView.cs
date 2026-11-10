using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LA GÉNÉALOGIE D'UNE FICHE, EN VUE (1.1.0, 27/10/2026 — elle était
    /// une fenêtre flottante, batch 36) : comme le mode wiki, elle prend la
    /// place des papers de la fiche (F2). Même design que le diagramme
    /// relationnel — grille à points, tuiles arrondies, le personnage en
    /// accent au centre — et un arbre qui se lit comme un arbre : chaque couple
    /// joint par un trait d'union, une branche qui descend de son milieu vers
    /// un bus d'où chaque enfant DE CE COUPLE pend à la verticale (1.1.1 : le
    /// demi-frère pend du milieu de SES deux parents, pas du parent commun) ;
    /// le personnage et ses partenaires de même, leurs enfants dessous. Les
    /// familles s'élargissent de fiche en fiche (Genealogy.Build : le père
    /// déclaré sur la fiche du grand-père est l'arrière-grand-père) ; la photo
    /// d'une fiche se pose au-dessus de son nom. Ce que l'arbre ne sait pas
    /// placer pend en pointillé de la fiche qui le déclare. Les natures libres
    /// en bande, en bas. Un nœud lié s'ouvre au clic.</summary>
    public class GenealogyView : Border
    {
        private const double TileW = 136, TileHPlain = 64, TileHPhoto = 112, PhotoH = 44, RowGap = 46, ColumnGap = 28, LabelW = 130, HeaderH = 36;

        private readonly Canvas _canvas;
        private readonly DotGrid _dots;
        private Project _project;
        private BinderItem _item;
        private double _tileH = TileHPlain;

        public event Action<BinderItem> NavigateRequested;

        public GenealogyView()
        {
            Background = Chrome.PaperBg;
            _canvas = new Canvas { Background = Brushes.Transparent };
            _dots = new DotGrid();
            var stage = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            stage.Children.Add(_dots);
            stage.Children.Add(_canvas);
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = stage
            };
        }

        /// <summary>Charge (ou recharge) l'arbre d'une fiche.</summary>
        public void Load(Project project, BinderItem item)
        {
            _project = project;
            _item = item;
            Draw();
        }

        public bool Shows(BinderItem item) { return _item == item; }

        public void Refresh() { Draw(); }

        /// <summary>Le libellé d'une génération vue depuis le personnage.</summary>
        public static string GenerationCaption(int generation)
        {
            switch (generation)
            {
                case -3: return "Arrière-grands-parents";
                case -2: return "Grands-parents";
                case -1: return "Parents · oncles et tantes";
                case 0: return "Partenaires · fratrie · cousins";
                case 1: return "Enfants · neveux et nièces";
                case 2: return "Petits-enfants";
                case 3: return "Arrière-petits-enfants";
                default: return generation < 0 ? "Aïeux" : "Descendants";
            }
        }

        // ------------------------------------------------------------ dessin

        private sealed class Placed
        {
            public GenealogyNode Node;
            public Point Center;
            public double Top;
            public double Bottom;
            public double Left { get { return Center.X - TileW / 2; } }
            public double Right { get { return Center.X + TileW / 2; } }
        }

        private void Draw()
        {
            _canvas.Children.Clear();
            if (_item == null)
            {
                SetSize(360, 140);
                _canvas.Children.Add(Caption("Aucune fiche ouverte.", 20, 40));
                return;
            }
            var nodes = Genealogy.Build(_project, _item);
            var rows = Genealogy.Rows(nodes);
            var couples = Genealogy.Couples(nodes, rows);
            var pairs = Genealogy.PartnerPairs(nodes);
            var others = new List<GenealogyNode>();
            foreach (var node in nodes) if (node.Lane == RelationLane.Other && !node.IsSelf) others.Add(node);

            // — Les photos (1.1.1) : dès qu'une fiche de l'arbre en a une, toutes
            //   les tuiles prennent la hauteur avec photo (l'arbre reste aligné).
            var photos = false;
            foreach (var node in nodes) if (ImageOf(node) != null) { photos = true; break; }
            _tileH = photos ? TileHPhoto : TileHPlain;
            var cellH = _tileH + RowGap;

            var widest = 1;
            foreach (var row in rows) if (row.Nodes.Count > widest) widest = row.Nodes.Count;
            var width = Math.Max(720, LabelW + 60 + widest * (TileW + ColumnGap));
            var centerX = LabelW + (width - LabelW) / 2;
            var height = HeaderH + rows.Count * cellH + (others.Count > 0 ? cellH + 20 : 20);
            SetSize(width, height);

            var placed = new Dictionary<string, Placed>(StringComparer.Ordinal);
            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                var y = HeaderH + r * cellH + cellH / 2;
                var totalWidth = row.Nodes.Count * TileW + (row.Nodes.Count - 1) * ColumnGap;
                var x = centerX - totalWidth / 2 + TileW / 2;
                foreach (var node in row.Nodes)
                {
                    placed[node.Key] = new Placed { Node = node, Center = new Point(x, y), Top = y - _tileH / 2, Bottom = y + _tileH / 2 };
                    x += TileW + ColumnGap;
                }
                _canvas.Children.Add(Caption(GenerationCaption(row.Generation), 12, y - 10));
            }

            // — Les traits, SOUS les tuiles.
            Placed self = null;
            foreach (var p in placed.Values) if (p.Node.IsSelf) { self = p; break; }
            var linked = new HashSet<string>(StringComparer.Ordinal);
            var unions = new HashSet<string>(StringComparer.Ordinal);
            if (self != null) linked.Add(self.Node.Key);
            // Les partenaires déclarés : un trait d'union même sans enfant
            // (double avec le personnage).
            foreach (var pair in pairs)
            {
                Placed a, b;
                if (!placed.TryGetValue(pair.Key.Key, out a) || !placed.TryGetValue(pair.Value.Key, out b)) continue;
                Union(a, b, a.Node.IsSelf || b.Node.IsSelf, unions);
                linked.Add(a.Node.Key); linked.Add(b.Node.Key);
            }
            // Les couples et leurs enfants, rangée par rangée ; les bus d'une
            // même rangée se décalent un peu pour ne pas se confondre.
            var perRow = new Dictionary<int, int>();
            foreach (var couple in couples)
            {
                var group = new List<Placed>();
                foreach (var parent in couple.Parents) { Placed p; if (placed.TryGetValue(parent.Key, out p)) group.Add(p); }
                var children = new List<Placed>();
                foreach (var child in couple.Children) { Placed p; if (placed.TryGetValue(child.Key, out p)) children.Add(p); }
                if (group.Count == 0 || children.Count == 0) continue;
                int index;
                perRow.TryGetValue(group[0].Node.Generation, out index);
                perRow[group[0].Node.Generation] = index + 1;
                Couple(group, children, couple.WithSelf, linked, unions, ((index % 3) - 1) * 7);
            }
            // Ce que l'arbre ne sait pas placer pend en pointillé de la fiche
            // qui le déclare (le personnage, à défaut).
            foreach (var p in placed.Values)
            {
                if (linked.Contains(p.Node.Key)) continue;
                Placed from;
                if (p.Node.ViaKey == null || !placed.TryGetValue(p.Node.ViaKey, out from)) from = self;
                if (from != null && from != p) DashedTo(from, p);
            }

            foreach (var p in placed.Values) _canvas.Children.Add(Tile(p));

            // — Les natures libres : une bande en bas, sans trait.
            if (others.Count > 0)
            {
                var y = HeaderH + rows.Count * cellH + 10;
                _canvas.Children.Add(Caption("Autres relations", 12, y + 20));
                var x = LabelW + 30;
                foreach (var node in others)
                {
                    var p = new Placed { Node = node, Center = new Point(x + TileW / 2, y + 10 + _tileH / 2), Top = y + 10, Bottom = y + 10 + _tileH };
                    _canvas.Children.Add(Tile(p));
                    x += TileW + ColumnGap;
                }
                if (x + 30 > width) SetSize(x + 30, height);
            }
        }

        private void SetSize(double width, double height)
        {
            _canvas.Width = width; _canvas.Height = height;
            _dots.Width = width; _dots.Height = height;
        }

        /// <summary>Le trait d'union entre deux membres d'un couple (double pour
        /// le personnage et ses partenaires), une seule fois par paire.</summary>
        private void Union(Placed a, Placed b, bool doubled, HashSet<string> unions)
        {
            var left = a.Center.X <= b.Center.X ? a : b;
            var right = left == a ? b : a;
            var signature = left.Node.Key + "\u0001" + right.Node.Key;
            if (!unions.Add(signature)) return;
            var y = left.Center.Y;
            if (doubled) { AddLine(left.Right, y - 3, right.Left, y - 3); AddLine(left.Right, y + 3, right.Left, y + 3); }
            else AddLine(left.Right, y, right.Left, y);
        }

        /// <summary>Un couple et ses enfants : le trait d'union entre les membres
        /// du groupe, la branche qui descend du MILIEU du couple (ou du seul
        /// membre), le bus à mi-hauteur, un pied vers chaque enfant.</summary>
        private void Couple(List<Placed> group, List<Placed> children, bool doubled, HashSet<string> linked, HashSet<string> unions, double stagger)
        {
            group.Sort(delegate(Placed a, Placed b) { return a.Center.X.CompareTo(b.Center.X); });
            var y = group[0].Center.Y;
            for (var i = 0; i + 1 < group.Count; i++) Union(group[i], group[i + 1], doubled, unions);
            double dropX, dropY;
            if (group.Count == 1) { dropX = group[0].Center.X; dropY = group[0].Bottom; }
            else
            {
                dropX = (group[0].Center.X + group[group.Count - 1].Center.X) / 2;
                dropY = y + (doubled ? 3 : 0);
                foreach (var member in group) if (dropX >= member.Left && dropX <= member.Right) { dropY = member.Bottom; break; }
            }
            var childTop = children[0].Top;
            var busY = (group[0].Bottom + childTop) / 2 + stagger;
            AddLine(dropX, dropY, dropX, busY);
            var minX = dropX; var maxX = dropX;
            foreach (var child in children) { minX = Math.Min(minX, child.Center.X); maxX = Math.Max(maxX, child.Center.X); }
            AddLine(minX, busY, maxX, busY);
            foreach (var child in children)
            {
                AddLine(child.Center.X, busY, child.Center.X, child.Top);
                linked.Add(child.Node.Key);
            }
            foreach (var member in group) linked.Add(member.Node.Key);
        }

        private void DashedTo(Placed from, Placed to)
        {
            _canvas.Children.Add(new Line
            {
                StartPoint = from.Center, EndPoint = to.Center,
                Stroke = Chrome.SoftText, StrokeThickness = 1.2, Opacity = 0.5,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 4 }
            });
        }

        private void AddLine(double x1, double y1, double x2, double y2)
        {
            _canvas.Children.Add(new Line { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2), Stroke = Chrome.SoftText, StrokeThickness = 1.5, Opacity = 0.75 });
        }

        /// <summary>La photo de la fiche d'un nœud (vignette du cache), null sans image.</summary>
        private Avalonia.Media.Imaging.Bitmap ImageOf(GenealogyNode node)
        {
            if (node == null || node.ImageId == null || _project == null) return null;
            try
            {
                var image = _project.FindImage(node.ImageId);
                return image == null ? null : ImageCache.Thumbnail(image, (int)(PhotoH * 3));
            }
            catch (Exception) { return null; }
        }

        private Control Tile(Placed p)
        {
            var node = p.Node;
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var photo = ImageOf(node);
            if (_tileH == TileHPhoto)
            {
                // La photo au-dessus du nom (1.1.1) — ou, sans photo, son
                // emplacement vide, pour que les noms restent alignés.
                var frame = new Border
                {
                    Width = PhotoH, Height = PhotoH,
                    CornerRadius = new CornerRadius(PhotoH / 2),
                    ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 4),
                    Background = photo != null ? (IBrush)Brushes.Transparent : null,
                    Tag = photo != null ? "genealogy-photo" : null // sonde
                };
                if (photo != null)
                    frame.Child = new Image { Source = photo, Stretch = Stretch.UniformToFill, Width = PhotoH, Height = PhotoH };
                column.Children.Add(frame);
            }
            column.Children.Add(new TextBlock
            {
                Text = node.Label,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = node.IsSelf ? Chrome.AccentStrong : node.TargetId != null ? Chrome.Accent : Chrome.Ink,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            column.Children.Add(new TextBlock
            {
                Text = node.IsSelf ? "la fiche" : node.Kind.Length > 0 ? node.Kind : "relation",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var tile = new Border
            {
                Width = TileW,
                Height = _tileH,
                Background = node.IsSelf ? Chrome.AccentTint : (IBrush)Chrome.CardBg,
                BorderBrush = node.IsSelf ? Chrome.Accent : (IBrush)Chrome.Border,
                BorderThickness = new Thickness(node.IsSelf ? 2 : 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 4, 8, 4),
                Child = column,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, OffsetY = 2, Color = Color.FromArgb(0x22, 0, 0, 0) }),
                Tag = node.IsSelf ? "genealogy-self" : "genealogy-node", // sonde
                [ToolTip.TipProperty] = node.IsSelf ? "Le personnage de la fiche"
                    : node.TargetId != null ? "Ouvrir la fiche « " + node.Label + " »" + (node.Inferred ? " (trouvée par rebond, depuis une fiche liée)" : "")
                    : "Nom libre (aucune fiche liée)"
            };
            if (node.Inferred && !node.IsSelf) tile.Opacity = 0.92;
            if (node.TargetId != null && !node.IsSelf)
            {
                tile.Cursor = new Cursor(StandardCursorType.Hand);
                var targetId = node.TargetId;
                tile.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
                {
                    if (e.InitialPressMouseButton != MouseButton.Left) return;
                    var target = _project == null ? null : _project.FindById(targetId);
                    var handler = NavigateRequested;
                    if (target != null && handler != null) handler(target);
                };
            }
            Canvas.SetLeft(tile, p.Left);
            Canvas.SetTop(tile, p.Top);
            return tile;
        }

        private Control Caption(string text, double x, double y)
        {
            var caption = new TextBlock { Text = text, FontSize = 11, Foreground = Chrome.SoftText, Width = LabelW - 24, TextWrapping = TextWrapping.Wrap };
            Canvas.SetLeft(caption, x);
            Canvas.SetTop(caption, y);
            return caption;
        }
    }

    /// <summary>Le fond à points (design fixe, comme la toile de Mental-O) : un
    /// point tous les 20 px, dans le filet du thème — partagé par le diagramme
    /// relationnel et la généalogie.</summary>
    internal sealed class DotGrid : Control
    {
        public const double Step = 20;

        public override void Render(DrawingContext context)
        {
            IBrush brush = new SolidColorBrush(Chrome.Border.Color);
            for (var y = Step; y < Bounds.Height; y += Step)
                for (var x = Step; x < Bounds.Width; x += Step)
                    context.DrawEllipse(brush, null, new Point(x, y), 1.2, 1.2);
        }
    }
}

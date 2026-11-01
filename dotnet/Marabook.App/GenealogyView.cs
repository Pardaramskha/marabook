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
    /// accent au centre — et un arbre qui se lit comme un arbre : les deux
    /// parents joints par un trait d'union, une branche qui descend de son
    /// milieu vers un bus d'où chaque enfant pend à la verticale ; le
    /// personnage et ses partenaires de même, leurs enfants dessous ; les
    /// grands-parents vers les parents et les oncles, les oncles vers les
    /// cousins, la fratrie vers les neveux. Ce que l'arbre ne sait pas placer
    /// pend du personnage en pointillé. Les natures libres en bande, en bas.
    /// Peuplée par les relations de la fiche (Genealogy.Build, le schéma du
    /// projet pour les générations et les voies) ; un nœud lié s'ouvre au clic.</summary>
    public class GenealogyView : Border
    {
        private const double TileW = 136, TileH = 64, CellH = 110, ColumnGap = 28, LabelW = 130, HeaderH = 36;

        private readonly Canvas _canvas;
        private readonly DotGrid _dots;
        private Project _project;
        private BinderItem _item;

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
                case -3: return "Aïeux";
                case -2: return "Grands-parents";
                case -1: return "Parents · oncles et tantes";
                case 0: return "Partenaires · fratrie · cousins";
                case 1: return "Enfants · neveux et nièces";
                case 2: return "Petits-enfants";
                case 3: return "Descendants";
                default: return generation < 0 ? "Ascendants" : "Descendants";
            }
        }

        // ------------------------------------------------------------ dessin

        private sealed class Placed
        {
            public GenealogyNode Node;
            public string Family = "";
            public Point Center;
            public double Top { get { return Center.Y - TileH / 2; } }
            public double Bottom { get { return Center.Y + TileH / 2; } }
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
            var generations = Genealogy.Generations(nodes);
            var schema = RelationKinds.Schema;
            var placed = new List<Placed>();
            var others = new List<Placed>();
            foreach (var node in nodes)
            {
                var info = node.IsSelf ? null : schema.Find(node.Kind);
                var p = new Placed { Node = node, Family = info == null ? "" : info.Family };
                if (node.Lane == RelationLane.Other && !node.IsSelf) others.Add(p); else placed.Add(p);
            }

            // — Les rangées : une par génération présente ; partenaires, le
            // personnage, la ligne directe, puis les collatéraux.
            var rows = new Dictionary<int, List<Placed>>();
            foreach (var generation in generations) rows[generation] = new List<Placed>();
            foreach (var p in placed) rows[p.Node.Generation].Add(p);
            var widest = 1;
            foreach (var row in rows.Values)
            {
                row.Sort(delegate(Placed a, Placed b)
                {
                    var ra = Rank(a); var rb = Rank(b);
                    if (ra != rb) return ra.CompareTo(rb);
                    return string.Compare(a.Node.Label, b.Node.Label, StringComparison.CurrentCultureIgnoreCase);
                });
                if (row.Count > widest) widest = row.Count;
            }
            var width = Math.Max(720, LabelW + 60 + widest * (TileW + ColumnGap));
            var centerX = LabelW + (width - LabelW) / 2;
            var height = HeaderH + generations.Count * CellH + (others.Count > 0 ? CellH + 20 : 20);
            SetSize(width, height);

            for (var r = 0; r < generations.Count; r++)
            {
                var generation = generations[r];
                var row = rows[generation];
                var y = HeaderH + r * CellH + CellH / 2;
                var totalWidth = row.Count * TileW + (row.Count - 1) * ColumnGap;
                var x = centerX - totalWidth / 2 + TileW / 2;
                foreach (var p in row)
                {
                    p.Center = new Point(x, y);
                    x += TileW + ColumnGap;
                }
                _canvas.Children.Add(Caption(GenerationCaption(generation), 12, y - 10));
            }

            // — Les traits, SOUS les tuiles.
            Placed self = null;
            foreach (var p in placed) if (p.Node.IsSelf) { self = p; break; }
            var linked = new HashSet<Placed>();
            if (self != null)
            {
                linked.Add(self);
                var partners = Where(placed, delegate(Placed p) { return !p.Node.IsSelf && p.Node.Lane == RelationLane.Partner && p.Node.Generation == 0; });
                var siblings = Where(placed, delegate(Placed p) { return p.Node.Lane == RelationLane.Side && p.Node.Generation == 0 && p.Family == "sibling"; });
                var cousins = Where(placed, delegate(Placed p) { return p.Node.Lane == RelationLane.Side && p.Node.Generation == 0 && p.Family == "cousin"; });
                var uncles = Where(placed, delegate(Placed p) { return p.Node.Lane == RelationLane.Side && p.Node.Generation == -1 && p.Family == "uncle"; });
                var nephews = Where(placed, delegate(Placed p) { return p.Node.Lane == RelationLane.Side && p.Node.Generation == 1 && p.Family == "nephew"; });

                // Les ascendants : chaque génération directe forme un couple qui
                // nourrit la suivante (les grands-parents : parents et oncles ; les
                // parents : le personnage et sa fratrie).
                var minGeneration = generations[0];
                for (var g = minGeneration; g <= -1; g++)
                {
                    var group = Direct(placed, g);
                    List<Placed> children;
                    if (g == -1) { children = new List<Placed> { self }; children.AddRange(siblings); }
                    else { children = Direct(placed, g + 1); if (g + 1 == -1) children.AddRange(uncles); }
                    Couple(group, children, false, linked);
                }
                // Le personnage et ses partenaires, leurs enfants dessous.
                var couple = new List<Placed>(partners);
                couple.Add(self);
                Couple(couple, Direct(placed, 1), true, linked);
                foreach (var partner in partners) linked.Add(partner);
                // Les descendants, de génération en génération.
                var maxGeneration = generations[generations.Count - 1];
                for (var g = 1; g < maxGeneration; g++) Couple(Direct(placed, g), Direct(placed, g + 1), false, linked);
                // Les oncles vers les cousins, la fratrie vers les neveux.
                Couple(uncles, cousins, false, linked);
                Couple(siblings, nephews, false, linked);
                // Ce que l'arbre ne sait pas placer pend du personnage, en pointillé.
                foreach (var p in placed)
                    if (!linked.Contains(p)) DashedTo(self, p);
            }

            foreach (var p in placed) _canvas.Children.Add(Tile(p));

            // — Les natures libres : une bande en bas, sans trait.
            if (others.Count > 0)
            {
                var y = HeaderH + generations.Count * CellH + 10;
                _canvas.Children.Add(Caption("Autres relations", 12, y + 20));
                var x = LabelW + 30;
                foreach (var p in others)
                {
                    p.Center = new Point(x + TileW / 2, y + 10 + TileH / 2);
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

        private static List<Placed> Where(List<Placed> all, Func<Placed, bool> test)
        {
            var list = new List<Placed>();
            foreach (var p in all) if (test(p)) list.Add(p);
            return list;
        }

        private static List<Placed> Direct(List<Placed> all, int generation)
        {
            return Where(all, delegate(Placed p) { return !p.Node.IsSelf && p.Node.Lane == RelationLane.Direct && p.Node.Generation == generation; });
        }

        private static int Rank(Placed p)
        {
            if (p.Node.Lane == RelationLane.Partner) return 0;
            if (p.Node.IsSelf) return 1;
            if (p.Node.Lane == RelationLane.Direct) return 2;
            return 3;
        }

        /// <summary>Un couple et ses enfants : le trait d'union entre les membres du
        /// groupe (double pour les partenaires), la branche qui descend du milieu
        /// (ou du seul membre), le bus à mi-hauteur, un pied vers chaque enfant.</summary>
        private void Couple(List<Placed> group, List<Placed> children, bool doubled, HashSet<Placed> linked)
        {
            if (group.Count == 0 || children.Count == 0) return;
            group.Sort(delegate(Placed a, Placed b) { return a.Center.X.CompareTo(b.Center.X); });
            var y = group[0].Center.Y;
            for (var i = 0; i + 1 < group.Count; i++)
            {
                if (doubled) { AddLine(group[i].Right, y - 3, group[i + 1].Left, y - 3); AddLine(group[i].Right, y + 3, group[i + 1].Left, y + 3); }
                else AddLine(group[i].Right, y, group[i + 1].Left, y);
            }
            double dropX, dropY;
            if (group.Count == 1) { dropX = group[0].Center.X; dropY = group[0].Bottom; }
            else
            {
                dropX = (group[0].Center.X + group[group.Count - 1].Center.X) / 2;
                dropY = y + (doubled ? 3 : 0);
                foreach (var member in group) if (dropX >= member.Left && dropX <= member.Right) { dropY = member.Bottom; break; }
            }
            var childTop = children[0].Top;
            var busY = (group[0].Bottom + childTop) / 2;
            AddLine(dropX, dropY, dropX, busY);
            var minX = dropX; var maxX = dropX;
            foreach (var child in children) { minX = Math.Min(minX, child.Center.X); maxX = Math.Max(maxX, child.Center.X); }
            AddLine(minX, busY, maxX, busY);
            foreach (var child in children)
            {
                AddLine(child.Center.X, busY, child.Center.X, child.Top);
                linked.Add(child);
            }
            foreach (var member in group) linked.Add(member);
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

        private Control Tile(Placed p)
        {
            var node = p.Node;
            var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
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
                Height = TileH,
                Background = node.IsSelf ? Chrome.AccentTint : (IBrush)Chrome.CardBg,
                BorderBrush = node.IsSelf ? Chrome.Accent : (IBrush)Chrome.Border,
                BorderThickness = new Thickness(node.IsSelf ? 2 : 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 4, 8, 4),
                Child = column,
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, OffsetY = 2, Color = Color.FromArgb(0x22, 0, 0, 0) }),
                Tag = node.IsSelf ? "genealogy-self" : "genealogy-node", // sonde
                [ToolTip.TipProperty] = node.IsSelf ? "Le personnage de la fiche"
                    : node.TargetId != null ? "Ouvrir la fiche « " + node.Label + " »"
                    : "Nom libre (aucune fiche liée)"
            };
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

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Le paper flottant de généalogie (batch 36) : l'arbre d'une
    /// fiche, dessiné en diagramme — le personnage au centre, ses ascendants
    /// au-dessus, ses descendants au-dessous, partenaires et collatéraux à
    /// ses côtés, les natures libres dans une bande « autres relations ».
    /// Peuplé par les relations de la fiche (Genealogy.Build) ; un nœud lié à
    /// une fiche s'ouvre au clic et l'arbre suit. Une seule fenêtre par vue
    /// fiche, rafraîchie à chaque modification des relations.</summary>
    public class GenealogyWindow : Window
    {
        private const double BoxWidth = 150, BoxHeight = 50, ColumnGap = 22, RowGap = 96;
        private const double MarginX = 120, MarginY = 36;

        private readonly TextBlock _title;
        private readonly Canvas _canvas;
        private Project _project;
        private BinderItem _item;

        public event Action<BinderItem> NavigateRequested;

        public GenealogyWindow(Window owner)
        {
            Owner = owner;
            Title = "Généalogie";
            Width = 820;
            Height = 560;
            MinWidth = 420;
            MinHeight = 300;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Chrome.WindowBg;

            var root = new DockPanel();
            var banner = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8)
            };
            DockPanel.SetDock(banner, Dock.Top);
            var bannerRow = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icons.Make("tree-bold", 18, Chrome.Accent) as FrameworkElement;
            if (icon != null)
            {
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Margin = new Thickness(0, 0, 10, 0);
                bannerRow.Children.Add(icon);
            }
            _title = new TextBlock { Foreground = Chrome.Ink, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            bannerRow.Children.Add(_title);
            bannerRow.Children.Add(new TextBlock
            {
                Text = "arbre peuplé par les relations de la fiche · cliquer une fiche liée l'ouvre",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 2, 0, 0)
            });
            banner.Child = bannerRow;
            root.Children.Add(banner);

            _canvas = new Canvas { Background = Chrome.PaperBg };
            var paper = new Border
            {
                Background = Chrome.PaperBg,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(18),
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.14, Color = Colors.Black },
                Child = _canvas,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            root.Children.Add(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = paper
            });
            Content = root;
        }

        /// <summary>Charge (ou recharge) l'arbre d'une fiche.</summary>
        public void Load(Project project, BinderItem item)
        {
            _project = project;
            _item = item;
            Title = item == null ? "Généalogie" : "Généalogie — " + item.Title;
            _title.Text = item == null ? "Généalogie" : item.Title;
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

        private void Draw()
        {
            _canvas.Children.Clear();
            if (_item == null)
            {
                _canvas.Width = 320; _canvas.Height = 120;
                _canvas.Children.Add(Caption("Aucune fiche ouverte.", 20, 40));
                return;
            }
            var nodes = Genealogy.Build(_project, _item);
            var generations = Genealogy.Generations(nodes);
            var others = new List<GenealogyNode>();
            foreach (var node in nodes) if (node.Lane == RelationLane.Other) others.Add(node);

            // — Les rangées : une par génération présente, ordonnée partenaires
            // / ligne directe (le personnage au milieu) / collatéraux.
            var rows = new Dictionary<int, List<GenealogyNode>>();
            foreach (var generation in generations) rows[generation] = new List<GenealogyNode>();
            foreach (var node in nodes)
                if (node.Lane != RelationLane.Other) rows[node.Generation].Add(node);
            var widest = 1;
            foreach (var row in rows.Values)
            {
                row.Sort(delegate(GenealogyNode a, GenealogyNode b)
                {
                    var ra = Rank(a); var rb = Rank(b);
                    if (ra != rb) return ra.CompareTo(rb);
                    return string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
                });
                if (row.Count > widest) widest = row.Count;
            }
            var width = Math.Max(560, MarginX * 2 + widest * (BoxWidth + ColumnGap));
            var centerX = width / 2;
            var height = MarginY * 2 + generations.Count * RowGap + (others.Count > 0 ? 80 : 0);
            _canvas.Width = width;
            _canvas.Height = height;

            // — Positions (centre de chaque boîte), puis les traits SOUS les boîtes.
            var centers = new Dictionary<GenealogyNode, Point>();
            var rowY = new Dictionary<int, double>();
            for (var r = 0; r < generations.Count; r++)
            {
                var generation = generations[r];
                var row = rows[generation];
                var y = MarginY + r * RowGap + BoxHeight / 2;
                rowY[generation] = y;
                var totalWidth = row.Count * BoxWidth + (row.Count - 1) * ColumnGap;
                var x = centerX - totalWidth / 2 + BoxWidth / 2;
                foreach (var node in row)
                {
                    centers[node] = new Point(x, y);
                    x += BoxWidth + ColumnGap;
                }
                _canvas.Children.Add(Caption(GenerationCaption(generation), 12, y - 7));
            }

            GenealogyNode self = null;
            foreach (var node in nodes) if (node.IsSelf) { self = node; break; }
            if (self != null)
            {
                var origin = centers[self];
                foreach (var node in nodes)
                {
                    if (node.IsSelf || node.Lane == RelationLane.Other) continue;
                    var point = centers[node];
                    if (node.Lane == RelationLane.Partner) DrawPartner(origin, point);
                    else if (node.Generation == 0) DrawSibling(origin, point);
                    else DrawElbow(origin, point, node.Generation < 0);
                }
            }
            foreach (var pair in centers) _canvas.Children.Add(Box(pair.Key, pair.Value));

            // — Les natures libres : une bande en bas, sans trait.
            if (others.Count > 0)
            {
                var y = MarginY + generations.Count * RowGap + 6;
                _canvas.Children.Add(Caption("Autres relations", 12, y + 8));
                var x = MarginX;
                foreach (var node in others)
                {
                    var box = Box(node, new Point(x + BoxWidth / 2, y + 12 + BoxHeight / 2));
                    _canvas.Children.Add(box);
                    x += BoxWidth + ColumnGap;
                }
                if (x + MarginX > _canvas.Width) _canvas.Width = x + MarginX;
            }
        }

        private static int Rank(GenealogyNode node)
        {
            if (node.Lane == RelationLane.Partner) return 0;
            if (node.IsSelf) return 1;
            if (node.Lane == RelationLane.Direct) return 2;
            return 3;
        }

        private UIElement Box(GenealogyNode node, Point center)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = node.Label,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = node.TargetId != null && !node.IsSelf ? Chrome.Accent : Chrome.Ink,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!node.IsSelf)
                stack.Children.Add(new TextBlock
                {
                    Text = node.Kind.Length > 0 ? node.Kind : "relation",
                    FontSize = 11,
                    Foreground = Chrome.SoftText,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            var box = new Border
            {
                Width = BoxWidth,
                Height = BoxHeight,
                Background = node.IsSelf ? Chrome.BarBgLight : Chrome.CardBg,
                BorderBrush = node.IsSelf ? Chrome.Accent : Chrome.Border,
                BorderThickness = new Thickness(node.IsSelf ? 2 : 1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                Child = stack,
                ToolTip = node.IsSelf ? "Le personnage de la fiche"
                    : node.TargetId != null ? "Ouvrir la fiche « " + node.Label + " »"
                    : "Nom libre (aucune fiche liée)"
            };
            if (node.TargetId != null && !node.IsSelf)
            {
                box.Cursor = Cursors.Hand;
                var targetId = node.TargetId;
                box.MouseLeftButtonUp += delegate
                {
                    var target = _project == null ? null : _project.FindById(targetId);
                    var handler = NavigateRequested;
                    if (target != null && handler != null) handler(target);
                };
            }
            Canvas.SetLeft(box, center.X - BoxWidth / 2);
            Canvas.SetTop(box, center.Y - BoxHeight / 2);
            return box;
        }

        private UIElement Caption(string text, double x, double y)
        {
            var caption = new TextBlock { Text = text, FontSize = 10, Foreground = Chrome.SoftText, Width = MarginX - 20, TextWrapping = TextWrapping.Wrap };
            Canvas.SetLeft(caption, x);
            Canvas.SetTop(caption, y);
            return caption;
        }

        private void DrawElbow(Point from, Point to, bool upward)
        {
            var startY = upward ? from.Y - BoxHeight / 2 : from.Y + BoxHeight / 2;
            var endY = upward ? to.Y + BoxHeight / 2 : to.Y - BoxHeight / 2;
            var midY = (startY + endY) / 2;
            AddLine(from.X, startY, from.X, midY);
            AddLine(from.X, midY, to.X, midY);
            AddLine(to.X, midY, to.X, endY);
        }

        private void DrawSibling(Point from, Point to)
        {
            var busY = from.Y - BoxHeight / 2 - 14;
            AddLine(from.X, from.Y - BoxHeight / 2, from.X, busY);
            AddLine(from.X, busY, to.X, busY);
            AddLine(to.X, busY, to.X, to.Y - BoxHeight / 2);
        }

        private void DrawPartner(Point from, Point to)
        {
            var left = Math.Min(from.X, to.X) + BoxWidth / 2;
            var right = Math.Max(from.X, to.X) - BoxWidth / 2;
            AddLine(left, from.Y - 3, right, from.Y - 3);
            AddLine(left, from.Y + 3, right, from.Y + 3);
        }

        private void AddLine(double x1, double y1, double x2, double y2)
        {
            _canvas.Children.Add(new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = Chrome.SoftText,
                StrokeThickness = 1.5,
                SnapsToDevicePixels = true
            });
        }
    }
}

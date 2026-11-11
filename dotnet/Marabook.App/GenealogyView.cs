using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LA GÉNÉALOGIE D'UNE FICHE, EN VUE (1.1.0, 27/10/2026 — elle était
    /// une fenêtre flottante, batch 36) : comme le mode wiki, elle prend la
    /// place des papers de la fiche (F2). Même design que le diagramme
    /// relationnel — grille à points, tuiles arrondies, le personnage en
    /// accent — et un arbre qui se lit comme un arbre : chaque couple joint
    /// par un trait d'union, une branche qui descend de son milieu vers un bus
    /// d'où chaque enfant DE CE COUPLE pend à la verticale. Les familles
    /// s'élargissent de fiche en fiche (Genealogy.Build) ; la photo d'une
    /// fiche se pose au-dessus de son nom.
    ///
    /// 1.1.1 (11/11, Rémi) : l'arbre est CENTRÉ dans la page et se déplace
    /// comme une carte de Mental-O (clic molette, ou Espace + glisser ; la
    /// molette défile) ; un CODE COULEUR discret — la teinte de la tuile dit
    /// la nature (ligne directe, fratrie, conjoints, collatéraux), la teinte
    /// des traits dit la lignée (un couple et sa descendance) ; les étapes
    /// manquantes de la ligne directe sont des cases grises « ??? » ; ce que
    /// l'arbre ne sait pas placer pend en TIRETS coudés, bien visibles, de la
    /// fiche qui le déclare ; et, ouvert depuis une fiche, l'arbre trace ses
    /// liens progressivement à partir du personnage (Préférences › Général ›
    /// Animations). Les natures libres en bande, en bas. Un nœud lié s'ouvre
    /// au clic.</summary>
    public class GenealogyView : Border
    {
        private const double TileW = 136, TileHPlain = 64, TileHPhoto = 112, PhotoH = 44, RowGap = 58, ColumnGap = 28, LabelW = 130, Pad = 28;

        private readonly Panel _viewport;
        private readonly Canvas _canvas;
        private readonly Canvas _labels;
        private readonly DotGrid _dots;
        private readonly Border _legend;
        private readonly TranslateTransform _move = new TranslateTransform();
        private readonly TranslateTransform _labelsMove = new TranslateTransform();
        private Project _project;
        private BinderItem _item;
        private double _tileH = TileHPlain;

        // Le contenu (coordonnées de l'arbre : x = 0 au milieu des rangées, y = 0 en haut)
        private double _contentW = 360, _contentH = 140;
        private Point _selfAt;
        // La vue : où tombe l'origine de l'arbre dans la page ; centrée tant qu'on n'y a pas touché
        private Point _origin;
        private bool _userMoved;
        private bool _panning, _spaceHeld;
        private Point _panLast;

        // Les traits, et leur tracé progressif
        private readonly List<Segment> _segments = new List<Segment>();
        private DispatcherTimer _timer;
        private Stopwatch _clock;
        private double _reachTotal, _duration;
        private int _dashedCount; // les tirets se décalent d'un cran l'un de l'autre

        public event Action<BinderItem> NavigateRequested;

        public GenealogyView()
        {
            Background = Chrome.PaperBg;
            _dots = new DotGrid();
            _canvas = new Canvas { RenderTransform = _move };
            _labels = new Canvas { RenderTransform = _labelsMove, IsHitTestVisible = false };
            _legend = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12),
                Padding = new Thickness(10, 7, 10, 7),
                CornerRadius = new CornerRadius(10),
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Tag = "genealogy-legend" // sonde
            };
            _viewport = new Panel { ClipToBounds = true, Background = Brushes.Transparent, Focusable = true, FocusAdorner = null };
            _viewport.Children.Add(_dots);
            _viewport.Children.Add(_canvas);
            _viewport.Children.Add(_labels);
            _viewport.Children.Add(_legend);
            _viewport.PointerPressed += OnPressed;
            _viewport.PointerMoved += OnMoved;
            _viewport.PointerReleased += OnReleased;
            _viewport.PointerCaptureLost += delegate { EndPan(); };
            _viewport.PointerWheelChanged += OnWheel;
            _viewport.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Space) return;
                _spaceHeld = true;
                SyncCursor();
                e.Handled = true;
            };
            _viewport.KeyUp += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Space) return;
                _spaceHeld = false;
                SyncCursor();
                e.Handled = true;
            };
            _viewport.LostFocus += delegate { _spaceHeld = false; SyncCursor(); };
            _viewport.SizeChanged += delegate { if (!_userMoved) Center(); };
            Child = _viewport;
        }

        /// <summary>Charge l'arbre d'une fiche. animate : l'arbre s'ouvre DEPUIS
        /// la fiche (bouton, F2) — ses liens se tracent à partir du personnage ;
        /// passer d'un arbre à l'autre ne rejoue pas le tracé.</summary>
        public void Load(Project project, BinderItem item, bool animate = false)
        {
            _project = project;
            _item = item;
            _userMoved = false;
            Draw(animate && Marabook.Settings.AppSettings.Animations);
            if (animate) Dispatcher.UIThread.Post(delegate { if (IsVisible) _viewport.Focus(); }, DispatcherPriority.Background); // Espace déplace sans clic préalable
        }

        public bool Shows(BinderItem item) { return _item == item; }

        /// <summary>Redessine (une relation a changé) : la vue reste où elle est.</summary>
        public void Refresh() { Draw(false); }

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

        // ----------------------------------------------------- le code couleur

        /// <summary>La teinte d'une nature : des tons moyens, peu saturés, lisibles
        /// sur papier clair comme sombre (pas de sapin de Noël).</summary>
        internal static Color NatureColor(GenealogyNature nature)
        {
            switch (nature)
            {
                case GenealogyNature.Direct: return Color.FromRgb(0x6E, 0x8F, 0xB5);     // bleu ardoise
                case GenealogyNature.Sibling: return Color.FromRgb(0x7F, 0xA0, 0x7A);    // sauge
                case GenealogyNature.Partner: return Color.FromRgb(0xC0, 0x84, 0x97);    // vieux rose
                case GenealogyNature.Collateral: return Color.FromRgb(0xB5, 0x97, 0x5F); // ocre
                case GenealogyNature.Self: return Chrome.Accent.Color;
                default: return Chrome.FaintText.Color;
            }
        }

        private static string NatureName(GenealogyNature nature)
        {
            switch (nature)
            {
                case GenealogyNature.Direct: return "Ligne directe";
                case GenealogyNature.Sibling: return "Fratrie";
                case GenealogyNature.Partner: return "Conjoints";
                case GenealogyNature.Collateral: return "Collatéraux";
                case GenealogyNature.Missing: return "Étape manquante";
                default: return "";
            }
        }

        private static readonly Color[] LineageColors =
        {
            Color.FromRgb(0x9A, 0x86, 0xB8), // lavande
            Color.FromRgb(0x5F, 0x9E, 0x9A), // sarcelle
            Color.FromRgb(0xB9, 0x85, 0x74), // terre cuite
            Color.FromRgb(0x87, 0x95, 0xA8), // gris-bleu
            Color.FromRgb(0x9A, 0x9A, 0x68)  // olive
        };

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

        /// <summary>Un trait droit de l'arbre. TileA / TileB : la tuile que touche
        /// chaque bout (le tracé progressif traverse les tuiles) ; DA / DB : la
        /// distance de chaque bout au personnage, le long des traits.</summary>
        private sealed class Segment
        {
            public Point A, B;
            public string TileA, TileB;
            public IBrush Brush;
            public bool Dashed;
            public Line Line;
            public double DA = double.PositiveInfinity, DB = double.PositiveInfinity;
            public double Length { get { return Math.Abs(B.X - A.X) + Math.Abs(B.Y - A.Y); } }
        }

        private void Draw(bool animate)
        {
            StopTimer();
            _canvas.Children.Clear();
            _labels.Children.Clear();
            _segments.Clear();
            _reachTotal = 0;
            _dashedCount = 0;
            if (_item == null)
            {
                _legend.IsVisible = false;
                var none = new TextBlock { Text = "Aucune fiche ouverte.", FontSize = 12, Foreground = Chrome.SoftText };
                Canvas.SetLeft(none, -60);
                Canvas.SetTop(none, 40);
                _canvas.Children.Add(none);
                _contentW = 360; _contentH = 140; _selfAt = new Point(0, 70);
                if (!_userMoved) Center(); else ApplyOrigin();
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

            // — Les places : chaque rangée centrée sur x = 0.
            var placed = new Dictionary<string, Placed>(StringComparer.Ordinal);
            var widest = TileW;
            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                var y = r * cellH + _tileH / 2;
                var totalWidth = row.Nodes.Count * TileW + (row.Nodes.Count - 1) * ColumnGap;
                if (totalWidth > widest) widest = totalWidth;
                var x = -totalWidth / 2 + TileW / 2;
                foreach (var node in row.Nodes)
                {
                    placed[node.Key] = new Placed { Node = node, Center = new Point(x, y), Top = y - _tileH / 2, Bottom = y + _tileH / 2 };
                    x += TileW + ColumnGap;
                }
                AddLabel(GenerationCaption(row.Generation), y);
            }
            Placed self = null;
            foreach (var p in placed.Values) if (p.Node.IsSelf) { self = p; break; }
            _selfAt = self != null ? self.Center : new Point(0, _tileH / 2);

            // — Les traits, SOUS les tuiles. Les couples et leurs enfants d'abord,
            //   une teinte par lignée ; les bus d'une même rangée se décalent un
            //   peu pour ne pas se confondre.
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in pairs) declared.Add(PairKey(pair.Key.Key, pair.Value.Key));
            var linked = new HashSet<string>(StringComparer.Ordinal);
            var unions = new HashSet<string>(StringComparer.Ordinal);
            if (self != null) linked.Add(self.Node.Key);
            var partnerBrush = new SolidColorBrush(NatureColor(GenealogyNature.Partner));
            var perRow = new Dictionary<int, int>();
            var lineage = 0;
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
                var brush = new SolidColorBrush(LineageColors[lineage++ % LineageColors.Length]);
                Couple(group, children, brush, partnerBrush, declared, linked, unions, ((index % 3) - 1) * 9);
            }
            // Les partenaires déclarés sans enfant : un trait d'union, simple
            // (le trait double du personnage se lisait comme un lien en trop).
            foreach (var pair in pairs)
            {
                Placed a, b;
                if (!placed.TryGetValue(pair.Key.Key, out a) || !placed.TryGetValue(pair.Value.Key, out b)) continue;
                Union(a, b, partnerBrush, unions);
                linked.Add(a.Node.Key); linked.Add(b.Node.Key);
            }
            // Ce que l'arbre ne sait pas placer pend en tirets coudés de la fiche
            // qui le déclare (le personnage, à défaut), dans la teinte de sa nature.
            var dashed = false;
            foreach (var p in placed.Values)
            {
                if (linked.Contains(p.Node.Key)) continue;
                Placed from;
                if (p.Node.ViaKey == null || !placed.TryGetValue(p.Node.ViaKey, out from)) from = self;
                if (from == null || from == p) continue;
                DashedTo(from, p, new SolidColorBrush(NatureColor(Genealogy.NatureOf(p.Node))));
                dashed = true;
            }
            Materialize();

            var natures = new List<GenealogyNature>();
            foreach (var p in placed.Values)
            {
                _canvas.Children.Add(Tile(p));
                var nature = Genealogy.NatureOf(p.Node);
                if (nature != GenealogyNature.Self && nature != GenealogyNature.Other && !natures.Contains(nature)) natures.Add(nature);
            }

            // — Les natures libres : une bande en bas, sans trait.
            var height = rows.Count * cellH - RowGap;
            if (others.Count > 0)
            {
                var top = rows.Count * cellH;
                var totalWidth = others.Count * TileW + (others.Count - 1) * ColumnGap;
                if (totalWidth > widest) widest = totalWidth;
                var x = -totalWidth / 2 + TileW / 2;
                AddLabel("Autres relations", top + _tileH / 2);
                foreach (var node in others)
                {
                    _canvas.Children.Add(Tile(new Placed { Node = node, Center = new Point(x, top + _tileH / 2), Top = top, Bottom = top + _tileH }));
                    x += TileW + ColumnGap;
                }
                height = top + _tileH;
            }
            _contentW = widest;
            _contentH = height;

            FillLegend(natures, lineage > 1, dashed);
            if (!_userMoved) Center(); else ApplyOrigin();

            Measure(self != null ? self.Node.Key : null);
            if (animate && _reachTotal > 0) StartTimer();
            else Reveal(double.PositiveInfinity);
        }

        private static string PairKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) < 0 ? a + "\u0001" + b : b + "\u0001" + a;
        }

        private void AddLabel(string text, double centerY)
        {
            var caption = new Border
            {
                Background = new SolidColorBrush(Chrome.PaperBg.Color, 0.85),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5, 2, 5, 2),
                MaxWidth = LabelW - 14,
                Child = new TextBlock { Text = text, FontSize = 11, Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap }
            };
            Canvas.SetLeft(caption, 8);
            Canvas.SetTop(caption, centerY - 11);
            _labels.Children.Add(caption);
        }

        /// <summary>Le trait d'union entre deux membres d'un couple, une seule fois
        /// par paire. Voisins : droit, de bord à bord ; séparés par d'autres
        /// tuiles : un pont par-dessus, pour ne pas passer derrière elles.</summary>
        private void Union(Placed a, Placed b, IBrush brush, HashSet<string> unions)
        {
            var left = a.Center.X <= b.Center.X ? a : b;
            var right = left == a ? b : a;
            if (!unions.Add(PairKey(left.Node.Key, right.Node.Key))) return;
            var y = left.Center.Y;
            if (Adjacent(left, right))
            {
                Add(new Point(left.Right, y), new Point(right.Left, y), left.Node.Key, right.Node.Key, brush, false);
                return;
            }
            var x1 = left.Center.X + TileW / 4; var x2 = right.Center.X - TileW / 4;
            var bridge = left.Top - 12;
            Add(new Point(x1, left.Top), new Point(x1, bridge), left.Node.Key, null, brush, false);
            Add(new Point(x1, bridge), new Point(x2, bridge), null, null, brush, false);
            Add(new Point(x2, bridge), new Point(x2, right.Top), null, right.Node.Key, brush, false);
        }

        private static bool Adjacent(Placed left, Placed right)
        {
            return right.Left - left.Right <= ColumnGap + 1;
        }

        /// <summary>Un couple et ses enfants : le trait d'union entre les membres
        /// du groupe (rose pour des conjoints déclarés, sinon la teinte de la
        /// lignée), la branche qui descend du MILIEU du couple (ou du seul
        /// membre), le bus à mi-hauteur, un pied vers chaque enfant.</summary>
        private void Couple(List<Placed> group, List<Placed> children, IBrush brush, IBrush partnerBrush, HashSet<string> declared,
            HashSet<string> linked, HashSet<string> unions, double stagger)
        {
            group.Sort(delegate(Placed a, Placed b) { return a.Center.X.CompareTo(b.Center.X); });
            var y = group[0].Center.Y;
            for (var i = 0; i + 1 < group.Count; i++)
                Union(group[i], group[i + 1], declared.Contains(PairKey(group[i].Node.Key, group[i + 1].Node.Key)) ? partnerBrush : brush, unions);
            // D'où part la branche : du bas du seul parent ; du milieu du trait
            // d'union de deux voisins ; du bas du membre qui tient le milieu ;
            // à défaut (le milieu tombe sous une tuile étrangère), du premier.
            var dropX = group[0].Center.X; var dropY = group[0].Bottom;
            var dropTile = group[0].Node.Key;
            if (group.Count > 1)
            {
                var middle = (group[0].Center.X + group[group.Count - 1].Center.X) / 2;
                var found = false;
                foreach (var member in group)
                    if (middle >= member.Left && middle <= member.Right) { dropX = middle; dropY = member.Bottom; dropTile = member.Node.Key; found = true; break; }
                if (!found)
                    for (var i = 0; i + 1 < group.Count; i++)
                        if (middle > group[i].Right && middle < group[i + 1].Left && Adjacent(group[i], group[i + 1]))
                        {
                            dropX = middle; dropY = y; dropTile = null;
                            break;
                        }
            }
            var busY = (group[0].Bottom + children[0].Top) / 2 + stagger;
            Add(new Point(dropX, dropY), new Point(dropX, busY), dropTile, null, brush, false);
            var minX = dropX; var maxX = dropX;
            foreach (var child in children) { minX = Math.Min(minX, child.Center.X); maxX = Math.Max(maxX, child.Center.X); }
            Add(new Point(minX, busY), new Point(maxX, busY), null, null, brush, false);
            foreach (var child in children)
            {
                Add(new Point(child.Center.X, busY), new Point(child.Center.X, child.Top), null, child.Node.Key, brush, false);
                linked.Add(child.Node.Key);
            }
            foreach (var member in group) linked.Add(member.Node.Key);
        }

        /// <summary>Un lien que l'arbre ne sait pas placer (la tante sans les
        /// grands-parents, le cousin sans l'oncle) : des tirets COUDÉS, de bord
        /// de tuile à bord de tuile — par-dessus pour deux voisins de rangée,
        /// par la marge entre les rangées sinon —, nets et dans la teinte de la
        /// nature (ils étaient une diagonale pâle passant derrière les tuiles).</summary>
        private void DashedTo(Placed from, Placed to, IBrush brush)
        {
            var shift = (_dashedCount++ % 3) * 5; // deux liens en tirets ne partagent pas leur palier
            if (Math.Abs(from.Center.Y - to.Center.Y) < 1)
            {
                var side = to.Center.X >= from.Center.X ? 1 : -1;
                var x1 = from.Center.X + side * TileW / 4; var x2 = to.Center.X - side * TileW / 4;
                var over = from.Top - 16 + shift;
                Add(new Point(x1, from.Top), new Point(x1, over), from.Node.Key, null, brush, true);
                Add(new Point(x1, over), new Point(x2, over), null, null, brush, true);
                Add(new Point(x2, over), new Point(x2, to.Top), null, to.Node.Key, brush, true);
                return;
            }
            var upper = from.Center.Y < to.Center.Y ? from : to;
            var lower = upper == from ? to : from;
            var dx = lower.Center.X - upper.Center.X;
            if (Math.Abs(dx) < TileW / 2)
            {
                var x = (upper.Center.X + lower.Center.X) / 2;
                Add(new Point(x, upper.Bottom), new Point(x, lower.Top), upper.Node.Key, lower.Node.Key, brush, true);
                return;
            }
            var toward = dx > 0 ? 1 : -1;
            var xa = upper.Center.X + toward * TileW / 4; var xb = lower.Center.X - toward * TileW / 4;
            var lane = lower.Top - 16 + shift;
            Add(new Point(xa, upper.Bottom), new Point(xa, lane), upper.Node.Key, null, brush, true);
            Add(new Point(xa, lane), new Point(xb, lane), null, null, brush, true);
            Add(new Point(xb, lane), new Point(xb, lower.Top), null, lower.Node.Key, brush, true);
        }

        private void Add(Point a, Point b, string tileA, string tileB, IBrush brush, bool dashed)
        {
            if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < 0.5)
            {
                // Un trait nul (un seul enfant, juste sous la branche) : rien à
                // dessiner, mais la continuité du tracé passe par lui.
                if (tileA == null && tileB == null) return;
            }
            _segments.Add(new Segment { A = a, B = b, TileA = tileA, TileB = tileB, Brush = brush, Dashed = dashed });
        }

        /// <summary>Coupe les traits là où un autre s'y greffe (la branche au
        /// milieu du trait d'union, les pieds sur le bus) — le tracé progressif
        /// suit alors les embranchements —, puis pose les lignes.</summary>
        private void Materialize()
        {
            for (var guard = 0; guard < 4000; guard++)
            {
                var cut = false;
                for (var i = 0; i < _segments.Count && !cut; i++)
                {
                    var s = _segments[i];
                    foreach (var other in _segments)
                    {
                        if (other == s) continue;
                        Point at;
                        if (Inside(s, other.A)) at = other.A;
                        else if (Inside(s, other.B)) at = other.B;
                        else continue;
                        _segments.Add(new Segment { A = at, B = s.B, TileB = s.TileB, Brush = s.Brush, Dashed = s.Dashed });
                        s.B = at; s.TileB = null;
                        cut = true;
                        break;
                    }
                }
                if (!cut) break;
            }
            foreach (var s in _segments)
            {
                s.Line = new Line
                {
                    StartPoint = s.A, EndPoint = s.B,
                    Stroke = s.Brush, StrokeThickness = s.Dashed ? 1.7 : 1.6, Opacity = 0.95,
                    Tag = s.Dashed ? "genealogy-dashed" : null // sonde
                };
                if (s.Dashed) s.Line.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4.5, 3 };
                _canvas.Children.Add(s.Line);
            }
        }

        /// <summary>Le point est-il sur le trait, strictement entre ses bouts ?</summary>
        private static bool Inside(Segment s, Point p)
        {
            const double eps = 0.25;
            if (Math.Abs(s.A.Y - s.B.Y) < eps)
                return Math.Abs(p.Y - s.A.Y) < eps && p.X > Math.Min(s.A.X, s.B.X) + 0.5 && p.X < Math.Max(s.A.X, s.B.X) - 0.5;
            if (Math.Abs(s.A.X - s.B.X) < eps)
                return Math.Abs(p.X - s.A.X) < eps && p.Y > Math.Min(s.A.Y, s.B.Y) + 0.5 && p.Y < Math.Max(s.A.Y, s.B.Y) - 0.5;
            return false;
        }

        // ------------------------------------------------ le tracé progressif

        private static string PointKey(Point p)
        {
            return Math.Round(p.X * 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Math.Round(p.Y * 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>La distance de chaque bout de trait au personnage, le long des
        /// traits (une tuile se traverse sans coût : ce qui arrive par un bord
        /// repart aussitôt par les autres). Ce qui n'est pas relié au personnage
        /// apparaît à la fin.</summary>
        private void Measure(string selfKey)
        {
            _reachTotal = 0;
            if (selfKey == null || _segments.Count == 0) return;
            var distance = new Dictionary<string, double>(StringComparer.Ordinal);
            distance["T:" + selfKey] = 0;
            for (var pass = 0; pass < 2000; pass++)
            {
                var changed = false;
                foreach (var s in _segments)
                {
                    var a = "P:" + PointKey(s.A); var b = "P:" + PointKey(s.B);
                    if (s.TileA != null) changed |= Relax(distance, "T:" + s.TileA, a, 0);
                    if (s.TileB != null) changed |= Relax(distance, "T:" + s.TileB, b, 0);
                    changed |= Relax(distance, a, b, s.Length);
                }
                if (!changed) break;
            }
            foreach (var s in _segments)
            {
                double d;
                if (distance.TryGetValue("P:" + PointKey(s.A), out d)) s.DA = d;
                if (distance.TryGetValue("P:" + PointKey(s.B), out d)) s.DB = d;
                if (!double.IsInfinity(s.DA) && !double.IsInfinity(s.DB)) _reachTotal = Math.Max(_reachTotal, Math.Max(s.DA, s.DB));
            }
            foreach (var s in _segments)
                if (double.IsInfinity(s.DA) || double.IsInfinity(s.DB)) { s.DA = _reachTotal; s.DB = _reachTotal; }
        }

        private static bool Relax(Dictionary<string, double> distance, string u, string v, double weight)
        {
            double du, dv;
            var hasU = distance.TryGetValue(u, out du);
            var hasV = distance.TryGetValue(v, out dv);
            var changed = false;
            if (hasU && (!hasV || du + weight < dv - 0.001)) { distance[v] = du + weight; dv = du + weight; hasV = true; changed = true; }
            if (hasV && (!hasU || dv + weight < du - 0.001)) { distance[u] = dv + weight; changed = true; }
            return changed;
        }

        /// <summary>Montre les traits jusqu'à la distance « reach » du personnage.</summary>
        private void Reveal(double reach)
        {
            foreach (var s in _segments)
            {
                if (s.Line == null) continue;
                var length = s.Length;
                var forward = s.DA <= s.DB;
                var start = forward ? s.A : s.B;
                var end = forward ? s.B : s.A;
                var shown = reach - Math.Min(s.DA, s.DB);
                if (double.IsInfinity(reach) || shown >= length) { s.Line.StartPoint = start; s.Line.EndPoint = end; s.Line.IsVisible = true; continue; }
                if (shown <= 0 || length <= 0) { s.Line.StartPoint = start; s.Line.EndPoint = start; s.Line.IsVisible = false; continue; }
                var ratio = shown / length;
                s.Line.StartPoint = start;
                s.Line.EndPoint = new Point(start.X + (end.X - start.X) * ratio, start.Y + (end.Y - start.Y) * ratio);
                s.Line.IsVisible = true;
            }
        }

        private void StartTimer()
        {
            _duration = Math.Max(0.7, Math.Min(2.0, _reachTotal / 900.0));
            _clock = Stopwatch.StartNew();
            Reveal(0);
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, delegate
            {
                var t = _clock.Elapsed.TotalSeconds / _duration;
                if (t >= 1 || !IsVisible) { StopTimer(); Reveal(double.PositiveInfinity); return; }
                var eased = t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);
                Reveal(_reachTotal * eased);
            });
            _timer.Start();
        }

        private void StopTimer()
        {
            if (_timer != null) { _timer.Stop(); _timer = null; }
        }

        /// <summary>Sonde : le tracé est-il en cours ; le figer à une fraction (1 = tout).</summary>
        internal bool AnimatingForProbe { get { return _timer != null; } }
        internal void SetProgressForProbe(double fraction)
        {
            StopTimer();
            Reveal(fraction >= 1 ? double.PositiveInfinity : _reachTotal * fraction);
        }
        internal Point OriginForProbe { get { return _origin; } }
        internal Point SelfCenterForProbe { get { return new Point(_origin.X + _selfAt.X, _origin.Y + _selfAt.Y); } }
        internal Size ContentSizeForProbe { get { return new Size(_contentW, _contentH); } }
        internal Size ViewportSizeForProbe { get { return _viewport.Bounds.Size; } }
        internal void PanForProbe(double dx, double dy) { PanBy(dx, dy); }

        // --------------------------------------------- centrer, déplacer la vue

        /// <summary>Centre l'arbre dans la page. Trop grand pour elle, c'est le
        /// personnage qui prend le milieu (sans laisser de vide au bord).</summary>
        private void Center()
        {
            var vw = _viewport.Bounds.Width; var vh = _viewport.Bounds.Height;
            if (vw <= 0 || vh <= 0) { ApplyOrigin(); return; }
            double x, y;
            if (_contentW <= vw - 2 * LabelW) x = vw / 2;                               // au milieu de la page
            else if (_contentW <= vw - LabelW - 24) x = LabelW + 12 + _contentW / 2;     // à droite des libellés
            else x = Clamp((LabelW + vw) / 2 - _selfAt.X, vw - 12 - _contentW / 2, LabelW + 12 + _contentW / 2);
            if (_contentH <= vh - 2 * Pad) y = (vh - _contentH) / 2;
            else y = Clamp(vh / 2 - _selfAt.Y, vh - Pad - _contentH, Pad);
            _origin = new Point(Math.Round(x), Math.Round(y));
            ApplyOrigin();
        }

        private static double Clamp(double value, double min, double max)
        {
            if (min > max) return max;
            return value < min ? min : value > max ? max : value;
        }

        private void ApplyOrigin()
        {
            _move.X = _origin.X; _move.Y = _origin.Y;
            _labelsMove.Y = _origin.Y;
            _dots.Offset = _origin;
        }

        private void PanBy(double dx, double dy)
        {
            _origin = new Point(_origin.X + dx, _origin.Y + dy);
            _userMoved = true;
            ApplyOrigin();
        }

        private void SyncCursor()
        {
            _viewport.Cursor = _panning || _spaceHeld ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }

        /// <summary>Comme la toile de Mental-O : le clic molette déplace la vue,
        /// Espace maintenu aussi (avec le clic gauche).</summary>
        private void OnPressed(object sender, PointerPressedEventArgs e)
        {
            if (!_viewport.IsKeyboardFocusWithin) _viewport.Focus();
            var props = e.GetCurrentPoint(_viewport).Properties;
            if (!props.IsMiddleButtonPressed && !(props.IsLeftButtonPressed && _spaceHeld)) return;
            _panning = true;
            _panLast = e.GetPosition(_viewport);
            e.Pointer.Capture(_viewport);
            SyncCursor();
            e.Handled = true;
        }

        private void OnMoved(object sender, PointerEventArgs e)
        {
            if (!_panning) return;
            var p = e.GetPosition(_viewport);
            PanBy(p.X - _panLast.X, p.Y - _panLast.Y);
            _panLast = p;
        }

        private void OnReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!_panning) return;
            EndPan();
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void EndPan()
        {
            if (!_panning) return;
            _panning = false;
            SyncCursor();
        }

        private void OnWheel(object sender, PointerWheelEventArgs e)
        {
            var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0 || e.Delta.X != 0) PanBy(delta * 80, 0);
            else PanBy(0, delta * 80);
            e.Handled = true;
        }

        // ------------------------------------------------------------ la légende

        private void FillLegend(List<GenealogyNature> natures, bool lineages, bool dashed)
        {
            var column = new StackPanel { Spacing = 4 };
            if (natures.Count > 0)
            {
                natures.Sort();
                var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                foreach (var nature in natures)
                {
                    var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
                    var color = NatureColor(nature);
                    chip.Children.Add(new Border
                    {
                        Width = 11, Height = 11, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center,
                        Background = new SolidColorBrush(Chrome.Blend(Chrome.CardBg.Color, color, nature == GenealogyNature.Missing ? 0.15 : 0.45)),
                        BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1)
                    });
                    chip.Children.Add(new TextBlock { Text = NatureName(nature), FontSize = 11, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center });
                    chips.Children.Add(chip);
                }
                column.Children.Add(chips);
            }
            var notes = new List<string>();
            if (lineages) notes.Add("traits : une teinte par lignée");
            if (dashed) notes.Add("tirets : lien déclaré, que l'arbre ne sait pas rattacher");
            notes.Add("déplacer : clic molette, ou Espace + glisser");
            column.Children.Add(new TextBlock { Text = string.Join("  ·  ", notes.ToArray()), FontSize = 10.5, Foreground = Chrome.FaintText });
            _legend.Child = column;
            _legend.IsVisible = true;
        }

        // ------------------------------------------------------------ les tuiles

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
            var nature = Genealogy.NatureOf(node);
            var missing = nature == GenealogyNature.Missing;
            var tinted = nature == GenealogyNature.Direct || nature == GenealogyNature.Sibling || nature == GenealogyNature.Partner || nature == GenealogyNature.Collateral;
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
                Foreground = missing ? Chrome.FaintText : node.IsSelf ? Chrome.AccentStrong : node.TargetId != null ? Chrome.Accent : Chrome.Ink,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            column.Children.Add(new TextBlock
            {
                Text = node.IsSelf ? "la fiche" : node.Kind.Length > 0 ? (missing ? node.Kind.ToLowerInvariant() : node.Kind) : "relation",
                FontSize = 11,
                Foreground = missing ? Chrome.FaintText : Chrome.SoftText,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var color = NatureColor(nature);
            var tile = new Border
            {
                Width = TileW,
                Height = _tileH,
                Background = node.IsSelf ? Chrome.AccentTint
                    : missing ? new SolidColorBrush(Chrome.Blend(Chrome.PaperBg.Color, Chrome.CardBg.Color, 0.6))
                    : tinted ? new SolidColorBrush(Chrome.Blend(Chrome.CardBg.Color, color, 0.13))
                    : (IBrush)Chrome.CardBg,
                BorderBrush = node.IsSelf ? Chrome.Accent
                    : tinted ? new SolidColorBrush(Chrome.Blend(Chrome.Border.Color, color, 0.85))
                    : (IBrush)Chrome.Border,
                BorderThickness = new Thickness(node.IsSelf ? 2 : missing ? 0 : tinted ? 1.5 : 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 4, 8, 4),
                Child = column,
                Tag = node.IsSelf ? "genealogy-self" : missing ? "genealogy-missing" : "genealogy-node", // sonde
                [ToolTip.TipProperty] = node.IsSelf ? "Le personnage de la fiche"
                    : missing ? "Étape manquante : aucune fiche ne dit qui relie ces deux générations"
                    : node.TargetId != null ? "Ouvrir la fiche « " + node.Label + " »" + (node.Inferred ? " (trouvée par rebond, depuis une fiche liée)" : "")
                    : "Nom libre (aucune fiche liée)"
            };
            if (missing)
            {
                // La case grise : un contour en tirets, pas d'ombre.
                var frame = new Grid();
                frame.Children.Add(new Rectangle
                {
                    RadiusX = 12, RadiusY = 12,
                    Stroke = Chrome.FaintText, StrokeThickness = 1.2, Opacity = 0.8,
                    StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 }
                });
                column.Margin = new Thickness(8, 4, 8, 4);
                tile.Child = null;
                frame.Children.Add(column);
                tile.Padding = new Thickness(0);
                tile.Child = frame;
            }
            else tile.BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, OffsetY = 2, Color = Color.FromArgb(0x22, 0, 0, 0) });
            if (node.Inferred && !node.IsSelf) tile.Opacity = 0.92;
            if (node.TargetId != null && !node.IsSelf)
            {
                tile.Cursor = new Cursor(StandardCursorType.Hand);
                var targetId = node.TargetId;
                tile.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
                {
                    if (e.InitialPressMouseButton != MouseButton.Left || _panning || _spaceHeld) return;
                    var target = _project == null ? null : _project.FindById(targetId);
                    var handler = NavigateRequested;
                    if (target != null && handler != null) handler(target);
                };
            }
            Canvas.SetLeft(tile, p.Left);
            Canvas.SetTop(tile, p.Top);
            return tile;
        }
    }

    /// <summary>Le fond à points (design fixe, comme la toile de Mental-O) : un
    /// point tous les 20 px, dans le filet du thème — partagé par le diagramme
    /// relationnel et la généalogie. Offset : le décalage de la vue (la grille
    /// suit l'arbre qu'on déplace).</summary>
    internal sealed class DotGrid : Control
    {
        public const double Step = 20;
        private Point _offset;

        public Point Offset
        {
            get { return _offset; }
            set { if (_offset == value) return; _offset = value; InvalidateVisual(); }
        }

        public override void Render(DrawingContext context)
        {
            IBrush brush = new SolidColorBrush(Chrome.Border.Color);
            var startX = ((_offset.X % Step) + Step) % Step;
            var startY = ((_offset.Y % Step) + Step) % Step;
            if (startX == 0) startX = Step;
            if (startY == 0) startY = Step;
            for (var y = startY; y < Bounds.Height; y += Step)
                for (var x = startX; x < Bounds.Width; x += Step)
                    context.DrawEllipse(brush, null, new Point(x, y), 1.2, 1.2);
        }
    }
}

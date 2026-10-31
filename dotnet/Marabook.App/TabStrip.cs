using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using AppSettings = Marabook.Settings.AppSettings;

namespace Marabook.App
{
    /// <summary>LA RANGÉE D'ONGLETS (1.1.0, 28/10, Rémi) : en haut de la zone
    /// centrale, à la hauteur du champ de recherche de la Pile. Le dessin est
    /// celui de Chrome : l'onglet actif est une languette aux coins hauts
    /// arrondis et aux pieds évasés, de la couleur du fond, ouverte en bas
    /// (elle recouvre le filet de la rangée) ; les autres sont de simples
    /// libellés séparés d'un trait, qui prennent la languette au survol ; un
    /// « + » suit le dernier. Un clic active, la molette (bouton du milieu)
    /// ferme, le clic droit propose Fermer / les autres / à droite ; un
    /// onglet se GLISSE ailleurs dans la rangée (31/10, Rémi), un trait
    /// d'accent marque le créneau où il tombera.
    /// La rangée ne décide rien : elle reflète un WorkTabs et lève des
    /// événements que la fenêtre traite.</summary>
    public class TabStrip : Border
    {
        public event Action<int> Activated;
        public event Action<int> CloseRequested;
        public event Action<int> CloseOthersRequested;
        public event Action<int> CloseRightRequested;
        public event Action NewRequested;
        public event Action<int, int> MoveRequested; // (onglet, créneau 0..Count) — glisser-déposer (31/10)

        /// <summary>L'élément d'un onglet (null s'il a disparu : la fenêtre
        /// élague avant de rebâtir, mais la rangée reste sûre).</summary>
        public Func<string, BinderItem> Resolve;

        public const double TabMaxWidth = 220, TabMinWidth = 48, PlusGap = 4;
        public const double Overlap = 8; // les pieds de deux voisins se chevauchent d'autant
        private const double Corner = 8;     // le rayon des coins et des pieds
        private const double TopPad = 6;     // le même que la barre de recherche de la Pile
        private const double SidePad = 6;

        private WorkTabs _model;
        private readonly TabsPanel _panel;
        private readonly Button _plus;
        private readonly List<TabView> _views = new List<TabView>();
        // Le glisser-déposer (31/10) : la pression est retenue par la RANGÉE
        // (capture du pointeur), pas par l'onglet — l'activation au clic
        // rebâtit les onglets, la rangée, elle, reste.
        private readonly Border _dropMark;
        private int _pressIndex = -1;
        private Point _pressAt;
        private bool _dragging;
        private const double DragThreshold = 6;

        public TabStrip()
        {
            Background = Chrome.BarBg;
            Height = 44; // en attendant la mesure de la barre de recherche de la Pile (AlignTabStripWith)
            Focusable = false;
            var host = new Grid();
            // Le filet du bas, SOUS les onglets : l'actif le recouvre de son fond.
            host.Children.Add(new Border { Height = 1, Background = Chrome.Border, VerticalAlignment = VerticalAlignment.Bottom });
            _plus = Buttons.Icon("plus-bold", "", Buttons.Compact, Buttons.Look.Calm);
            _plus.Focusable = false;
            _plus.Click += delegate { var handler = NewRequested; if (handler != null) handler(); };
            _panel = new TabsPanel(_plus) { Margin = new Thickness(SidePad, TopPad, SidePad, 0) };
            host.Children.Add(_panel);
            // Le repère du dépôt : un trait d'accent vertical au créneau visé.
            _dropMark = new Border
            {
                Width = 3,
                Background = Chrome.Accent,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, TopPad, 0, 3),
                IsVisible = false,
                IsHitTestVisible = false,
                ZIndex = 5
            };
            host.Children.Add(_dropMark);
            Child = host;
            PointerMoved += OnDragMoved;
            PointerReleased += OnDragReleased;
            PointerCaptureLost += delegate { CancelDrag(); };
        }

        // ---------------------------------------------------------------- glisser-déposer

        /// <summary>Un onglet pressé (bouton gauche) : la rangée capte le
        /// pointeur et attend de voir si c'est un clic ou un glisser.</summary>
        internal void BeginDrag(int index, PointerPressedEventArgs e)
        {
            _pressIndex = index;
            _dragging = false;
            _pressAt = e.GetPosition(this);
            e.Pointer.Capture(this);
        }

        private void OnDragMoved(object sender, PointerEventArgs e)
        {
            if (_pressIndex < 0 || !ReferenceEquals(e.Pointer.Captured, this)) return;
            var at = e.GetPosition(this);
            if (!_dragging && Math.Abs(at.X - _pressAt.X) < DragThreshold) return;
            _dragging = true;
            ShowDropMark(SlotAt(at.X));
        }

        private void OnDragReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_pressIndex < 0) return;
            var from = _pressIndex;
            var dragging = _dragging;
            var slot = SlotAt(e.GetPosition(this).X);
            CancelDrag();
            if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null);
            if (!dragging) return;
            e.Handled = true;
            if (slot != from && slot != from + 1) { var handler = MoveRequested; if (handler != null) handler(from, slot); }
        }

        private void CancelDrag()
        {
            _pressIndex = -1;
            _dragging = false;
            _dropMark.IsVisible = false;
        }

        /// <summary>Le créneau (0 = avant le premier, Count = après le dernier)
        /// le plus proche de cette abscisse de la rangée.</summary>
        private int SlotAt(double x)
        {
            var count = _views.Count;
            var pitch = _panel.TabPitch;
            if (count == 0 || pitch <= 0) return 0;
            var slot = (int)Math.Round((x - SidePad) / pitch);
            return Math.Max(0, Math.Min(count, slot));
        }

        private void ShowDropMark(int slot)
        {
            var pitch = _panel.TabPitch;
            var x = SidePad + slot * pitch + (slot == _views.Count && _views.Count > 0 ? Overlap : 0) - 1.5;
            _dropMark.Margin = new Thickness(Math.Max(0, x), TopPad, 0, 3);
            _dropMark.IsVisible = true;
        }

        public bool DropMarkShownForProbe { get { return _dropMark.IsVisible; } }
        public void MoveForProbe(int from, int slot) { var handler = MoveRequested; if (handler != null) handler(from, slot); }

        public void Bind(WorkTabs model)
        {
            _model = model;
            Rebuild();
        }

        /// <summary>Rebâtit la rangée d'après le modèle : un TabView par onglet,
        /// le « + » éteint au plafond.</summary>
        public void Rebuild()
        {
            _panel.Children.Clear();
            _views.Clear();
            if (_model == null) return;
            for (var i = 0; i < _model.Count; i++)
            {
                var tab = _model[i];
                var item = Resolve == null ? null : Resolve(tab.ItemId);
                var view = new TabView(this, i, item, i == _model.ActiveIndex);
                view.ZIndex = i == _model.ActiveIndex ? 1 : 0; // l'actif devant ses voisins chevauchés
                _views.Add(view);
                _panel.Children.Add(view);
            }
            // Plus de séparateurs (28/10, Rémi) : chaque onglet a son contour.
            _panel.Children.Add(_plus);
            var gesture = AppSettings.DisplayGesture(AppSettings.Gesture("new-tab") ?? "");
            _plus.IsEnabled = _model.CanOpen;
            ToolTip.SetTip(_plus, _model.CanOpen ? "Nouvel onglet" + (gesture.Length > 0 ? " (" + gesture + ")" : "") : "Eh, ça va les onglets ? (" + _model.Limit + ")");
        }

        /// <summary>Le libellé d'un onglet : le titre de l'élément, ou un repère
        /// si l'élément n'est plus là.</summary>
        internal static string TitleOf(BinderItem item)
        {
            if (item == null) return "(élément supprimé)";
            var title = item.Title;
            return string.IsNullOrEmpty(title) ? "Sans titre" : title;
        }

        internal void OnActivate(int index) { var handler = Activated; if (handler != null) handler(index); }
        internal void OnClose(int index) { var handler = CloseRequested; if (handler != null) handler(index); }

        internal void OnMenu(TabView view, int index)
        {
            var menu = new ContextMenu();
            var close = new MenuItem { Header = "Fermer l'onglet" };
            close.Click += delegate { OnClose(index); };
            menu.Items.Add(close);
            var others = new MenuItem { Header = "Fermer les autres onglets", IsEnabled = _model != null && _model.Count > 1 };
            others.Click += delegate { var handler = CloseOthersRequested; if (handler != null) handler(index); };
            menu.Items.Add(others);
            var right = new MenuItem { Header = "Fermer les onglets à droite", IsEnabled = _model != null && index + 1 < _model.Count };
            right.Click += delegate { var handler = CloseRightRequested; if (handler != null) handler(index); };
            menu.Items.Add(right);
            Ui.ShowMenu(menu, view);
        }

        // ---------------------------------------------------------------- sonde

        public List<string> TitlesForProbe()
        {
            var titles = new List<string>();
            foreach (var view in _views) titles.Add(view.Title);
            return titles;
        }

        public int ActiveIndexForProbe
        {
            get { for (var i = 0; i < _views.Count; i++) if (_views[i].IsActive) return i; return -1; }
        }

        public bool PlusEnabledForProbe { get { return _plus.IsEnabled; } }
        public double TabWidthForProbe { get { return _views.Count == 0 ? 0 : _views[0].Bounds.Width; } }
        public TabView TabForProbe(int index) { return index >= 0 && index < _views.Count ? _views[index] : null; }

        // ---------------------------------------------------------------- le panneau

        /// <summary>Range les onglets côte à côte, à largeur égale bornée
        /// (48–220 px), puis le « + » juste après le dernier.</summary>
        private sealed class TabsPanel : Panel
        {
            private readonly Button _plus;
            /// <summary>Le pas d'un onglet au dernier arrangement (largeur moins
            /// le chevauchement) — le glisser-déposer s'y repère.</summary>
            public double TabPitch;

            public TabsPanel(Button plus) { _plus = plus; }

            // Des onglets LARGES à parts égales (48–220 px), qui se
            // CHEVAUCHENT par leurs pieds (28/10, Rémi : « les rapprocher
            // légèrement ») — l'actif passe devant par son ZIndex.
            private int TabCount()
            {
                return Children.Count - (Children.Contains(_plus) ? 1 : 0);
            }

            private double TabWidth(double available)
            {
                var tabs = TabCount();
                if (tabs <= 0) return 0;
                var room = Math.Max(0, available - (Buttons.Compact + PlusGap)) + Overlap * (tabs - 1);
                return Math.Max(TabMinWidth, Math.Min(TabMaxWidth, Math.Floor(room / tabs)));
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                var width = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
                var height = double.IsInfinity(availableSize.Height) ? 32 : availableSize.Height;
                var tabWidth = TabWidth(width);
                double x = 0;
                foreach (var child in Children)
                {
                    if (ReferenceEquals(child, _plus)) { child.Measure(new Size(Buttons.Compact, height)); x += PlusGap + child.DesiredSize.Width; }
                    else { child.Measure(new Size(tabWidth, height)); x += tabWidth - Overlap; }
                }
                if (TabCount() > 0) x += Overlap; // le dernier onglet ne cède rien
                return new Size(Math.Min(width, x), height);
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                var tabWidth = TabWidth(finalSize.Width);
                TabPitch = tabWidth - Overlap;
                double x = 0;
                foreach (var child in Children)
                {
                    if (ReferenceEquals(child, _plus))
                    {
                        var h = child.DesiredSize.Height;
                        var y = Math.Max(0, (finalSize.Height - h) / 2);
                        child.Arrange(new Rect(x + Overlap + PlusGap, y, Buttons.Compact, h));
                        x += Overlap + PlusGap + Buttons.Compact;
                    }
                    else
                    {
                        child.Arrange(new Rect(x, 0, tabWidth, finalSize.Height));
                        x += tabWidth - Overlap;
                    }
                }
                return finalSize;
            }
        }

        // ---------------------------------------------------------------- un onglet

        /// <summary>Un onglet : la languette (dessinée dans Render, sous le
        /// contenu), l'icône de l'élément, son titre, la croix.</summary>
        public sealed class TabView : Grid
        {
            private readonly TabStrip _strip;
            private readonly int _index;
            private readonly Border _close;
            private readonly TextBlock _title;
            private readonly TabFace _face;
            private bool _hover;
            public readonly bool IsActive;
            public bool ShowSeparator;
            public string Title { get { return _title.Text; } }

            public TabView(TabStrip strip, int index, BinderItem item, bool active)
            {
                _strip = strip;
                _index = index;
                IsActive = active;
                Focusable = false;
                Background = Brushes.Transparent; // reçoit le pointeur sur toute sa surface
                Cursor = new Cursor(StandardCursorType.Arrow);
                ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                Margin = new Thickness(0);
                _face = new TabFace(this);
                Grid.SetColumnSpan(_face, 3);
                Children.Add(_face); // premier enfant : sous l'icône, le titre et la croix

                var ink = active ? (IBrush)Chrome.Ink : Chrome.SoftText;
                Control icon = null;
                try { icon = item == null ? Icons.Make("trash", 14, Chrome.FaintText) : ItemIcons.Render(item, 14, ink); }
                catch (Exception) { icon = null; }
                if (icon != null)
                {
                    icon.VerticalAlignment = VerticalAlignment.Center;
                    icon.Margin = new Thickness(Corner + 4, 0, 6, 0);
                    icon.IsHitTestVisible = false;
                    Grid.SetColumn(icon, 0);
                    Children.Add(icon);
                }
                _title = new TextBlock
                {
                    Text = TitleOf(item),
                    FontSize = 12,
                    Foreground = ink,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(icon == null ? Corner + 4 : 0, 0, 4, 0),
                    IsHitTestVisible = false
                };
                Grid.SetColumn(_title, 1);
                Children.Add(_title);
                // Pas d'infobulle au survol d'un onglet (28/10, Rémi).

                // La croix (28/10, Rémi) : un poil plus petite, et seulement sur
                // l'actif ou l'onglet survolé — elle garde sa place (opacité,
                // pas de visibilité) pour que le titre ne bouge pas.
                _close = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = Brushes.Transparent,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, Corner + 2, 0),
                    Child = Icons.Make("x-bold", 8, ink),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Opacity = active ? 1 : 0,
                    IsHitTestVisible = active
                };
                _close.PointerEntered += delegate { _close.Background = Chrome.Border; };
                _close.PointerExited += delegate { _close.Background = Brushes.Transparent; };
                _close.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { e.Handled = true; };
                _close.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
                {
                    if (e.InitialPressMouseButton != MouseButton.Left) return;
                    e.Handled = true;
                    _strip.OnClose(_index);
                };
                Grid.SetColumn(_close, 2);
                Children.Add(_close);

                PointerEntered += delegate { _hover = true; if (!IsActive) { _close.Opacity = 1; _close.IsHitTestVisible = true; } _face.InvalidateVisual(); };
                PointerExited += delegate { _hover = false; if (!IsActive) { _close.Opacity = 0; _close.IsHitTestVisible = false; } _face.InvalidateVisual(); };
                PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                {
                    var point = e.GetCurrentPoint(this);
                    if (point.Properties.IsMiddleButtonPressed) { e.Handled = true; _strip.OnClose(_index); return; }
                    if (point.Properties.IsRightButtonPressed) { e.Handled = true; _strip.OnMenu(this, _index); return; }
                    if (point.Properties.IsLeftButtonPressed)
                    {
                        e.Handled = true;
                        _strip.BeginDrag(_index, e); // la rangée capte : un glisser peut suivre (31/10)
                        if (!IsActive) _strip.OnActivate(_index);
                    }
                };
            }

            internal bool Hover { get { return _hover; } }
        }

        /// <summary>La languette de Chrome, dessinée sous le contenu de
        /// l'onglet (le Render d'un Panel est scellé) : pieds évasés, coins
        /// hauts arrondis, ouverte en bas. Pleine du fond de la fenêtre et
        /// cernée d'un filet pour l'actif ; d'un ton plus clair, sans
        /// filet, au survol d'un inactif ; rien sinon — qu'un trait.</summary>
        private sealed class TabFace : Control
        {
            private readonly TabView _tab;

            public TabFace(TabView tab)
            {
                _tab = tab;
                IsHitTestVisible = false;
            }

            public override void Render(DrawingContext context)
            {
                base.Render(context);
                var w = Bounds.Width;
                var h = Bounds.Height;
                if (w <= 0 || h <= 0) return;
                var IsActive = _tab.IsActive;
                var _hover = _tab.Hover;
                {
                    var r = Math.Min(Corner, Math.Min(w / 4, h / 2));
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(0, h), true);
                        ctx.QuadraticBezierTo(new Point(r, h), new Point(r, h - r));
                        ctx.LineTo(new Point(r, r));
                        ctx.QuadraticBezierTo(new Point(r, 0), new Point(2 * r, 0));
                        ctx.LineTo(new Point(w - 2 * r, 0));
                        ctx.QuadraticBezierTo(new Point(w - r, 0), new Point(w - r, r));
                        ctx.LineTo(new Point(w - r, h - r));
                        ctx.QuadraticBezierTo(new Point(w - r, h), new Point(w, h));
                        ctx.EndFigure(false);
                    }
                    if (IsActive)
                    {
                        // L'actif (28/10, Rémi) : fond à la teinte d'accent,
                        // contour d'accent doux ; le fond descend d'un pixel
                        // sous la rangée et efface le filet du bas sous lui.
                        context.DrawGeometry(Chrome.AccentTint, new Pen(Chrome.AccentSoft, 1), geometry);
                        context.FillRectangle(Chrome.AccentTint, new Rect(r, h - 1, Math.Max(0, w - 2 * r), 1.5));
                    }
                    else
                    {
                        // Un inactif garde un contour contrasté (28/10, Rémi :
                        // « sinon ils se fondent dans le fond ») ; il s'éclaire au survol.
                        context.DrawGeometry(_hover ? (IBrush)Chrome.BarBgLight : Chrome.BarBg, new Pen(Chrome.BorderStrong, 1), geometry);
                    }
                }
            }
        }
    }
}

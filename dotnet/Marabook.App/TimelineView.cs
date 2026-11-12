using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>UNE FRISE CHRONOLOGIQUE (1.2.0, 12/11/2026, Rémi) : pas un
    /// tableau de tuiles — un axe du temps gradué dans la notation de son
    /// système, des briques posées dessus (un point pour l'événement ponctuel,
    /// une barre pour celui qui dure), rangées en couloirs. Un onglet par
    /// segment de la temporalité (les ères) en plus de « Tout » ; les types
    /// d'événement se filtrent d'un clic ; la molette zoome sous le pointeur,
    /// glisser déplace ; la minimap, en bas, montre toute l'étendue de
    /// l'onglet et le cadre de ce qu'on regarde. Le Général du rail reçoit le
    /// compte des événements, la durée de la temporalité et celle de l'ère.</summary>
    public class TimelineView : DockPanel
    {
        private const double RulerH = 34, BandH = 22, LaneH = 30, SidePad = 36, LabelPad = 190;

        private readonly TextBlock _title;
        private readonly Button _systemButton;
        private readonly TextBox _goTo;
        private readonly StackPanel _tabs;
        private readonly Border _tabsBar;
        private readonly WrapPanel _filters;
        private readonly Surface _surface;
        private readonly Minimap _minimap;
        private readonly TextBlock _empty;
        private readonly StackPanel _inspector = new StackPanel();

        private BinderItem _item;
        private Project _project;
        private TimeSystem _system = TimeSystem.Gregorian();
        private TimelineStats _stats = new TimelineStats();
        private int _tab = -1;                       // −1 = « Tout », sinon l'index du segment
        private readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.Ordinal); // les types masqués
        private TimelineEvent _selected;

        // La vue : l'année au bord gauche, les pixels par année, le défilement des couloirs.
        private double _viewStart, _scale = 1, _scrollY;
        private double _rangeMin, _rangeMax = 10;
        private bool _fitPending = true;
        private TimePoint _flash;                    // la date d'un « aller à » (un trait qui s'éteint)
        private DispatcherTimer _flashTimer;

        public event Action Edited;
        public event Action<BinderItem> NavigateRequested;
        public event Action BackRequested;
        public event Action RenameRequested;
        public event Action<TimeSystem> SystemEditRequested;
        public event Action InspectorChanged;

        public TimelineView()
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

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 6 };
            _goTo = new TextBox { Width = 150, Watermark = "Aller à une date", VerticalAlignment = VerticalAlignment.Center, Tag = "timeline-goto" };
            ToolTip.SetTip(_goTo, "Écrire une date dans la notation de la frise, puis Entrée");
            _goTo.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter && e.Key != Key.Return) return;
                e.Handled = true;
                TimePoint point;
                if (_system.TryParse(_goTo.Text, out point)) { GoTo(point); _goTo.Text = ""; _goTo.ClearValue(TextBox.ForegroundProperty); }
                else _goTo.Foreground = Chrome.Danger;
            };
            _goTo.TextChanged += delegate { _goTo.ClearValue(TextBox.ForegroundProperty); };
            right.Children.Add(_goTo);
            var zoomOut = Buttons.Text("−", "Dézoomer", Buttons.Compact, Buttons.Look.Outline);
            zoomOut.Click += delegate { ZoomBy(1 / 1.5, _surface.Bounds.Width / 2); };
            var zoomIn = Buttons.Text("+", "Zoomer", Buttons.Compact, Buttons.Look.Outline);
            zoomIn.Click += delegate { ZoomBy(1.5, _surface.Bounds.Width / 2); };
            var fit = Buttons.Text("Tout voir", "Ramener tout l'onglet dans la page", Buttons.Compact, Buttons.Look.Outline);
            fit.Click += delegate { FitAll(); };
            right.Children.Add(zoomOut);
            right.Children.Add(zoomIn);
            right.Children.Add(fit);
            var add = Buttons.IconText("plus-bold", "Nouvel événement", "Poser un événement sur la frise (ou double-clic sur la frise, à la date voulue)", Buttons.Compact, Buttons.Look.Primary);
            add.Tag = "timeline-add";
            add.Click += delegate { NewEvent(null); };
            right.Children.Add(add);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var back = new Button
            {
                Content = Icons.Make("arrow-left-bold", 12, Chrome.Ink),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 12, 0),
                [ToolTip.TipProperty] = "Revenir aux frises",
                VerticalAlignment = VerticalAlignment.Center
            };
            back.Click += delegate { var h = BackRequested; if (h != null) h(); };
            left.Children.Add(back);
            _title = new TextBlock
            {
                Foreground = Chrome.Ink, FontSize = 17, FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, MaxWidth = 340, TextTrimming = TextTrimming.CharacterEllipsis
            };
            left.Children.Add(_title);
            var rename = Buttons.Icon("pencil-simple-line", "Renommer la frise", Buttons.Compact, Buttons.Look.Calm);
            rename.Margin = new Thickness(6, 0, 0, 0);
            rename.VerticalAlignment = VerticalAlignment.Center;
            rename.Click += delegate { var h = RenameRequested; if (h != null) h(); };
            left.Children.Add(rename);
            _systemButton = Buttons.IconText("pile-frises", "Système", "Le système temporel de cette frise : segments, notation, calendrier", Buttons.Compact, Buttons.Look.Outline);
            _systemButton.Margin = new Thickness(14, 0, 0, 0);
            _systemButton.Tag = "timeline-system";
            _systemButton.Click += delegate { var h = SystemEditRequested; if (h != null && _item != null) h(_system); };
            left.Children.Add(_systemButton);
            row.Children.Add(left);
            head.Child = row;
            Children.Add(head);

            // ================================================== onglets (les segments)
            _tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            _tabsBar = new Border
            {
                Background = Chrome.BarBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 6, 16, 6),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _tabs
                }
            };
            SetDock(_tabsBar, Dock.Top);
            Children.Add(_tabsBar);

            // ================================================== filtres (les types)
            _filters = new WrapPanel { Orientation = Orientation.Horizontal };
            var filterBar = new Border { Padding = new Thickness(16, 6, 16, 2), Child = _filters };
            SetDock(filterBar, Dock.Top);
            Children.Add(filterBar);

            // ================================================== minimap
            _minimap = new Minimap(this) { Height = 46, Margin = new Thickness(16, 0, 16, 12) };
            SetDock(_minimap, Dock.Bottom);
            Children.Add(_minimap);

            // ================================================== la frise
            _surface = new Surface(this) { Margin = new Thickness(16, 8, 16, 8) };
            _empty = new TextBlock
            {
                Text = "Aucun événement pour l'instant.\nDouble-cliquez sur la frise à la date voulue, ou « Nouvel événement ».",
                Foreground = Chrome.SoftText, FontSize = 13, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false, Margin = new Thickness(0, 60, 0, 0)
            };
            var center = new Grid();
            center.Children.Add(_surface);
            center.Children.Add(_empty);
            Children.Add(center);
            Ui.OnSizeChanged(_surface, delegate
            {
                if (_fitPending && _surface.Bounds.Width > 0) FitAll();
                else { ClampView(); Redraw(); }
            });
        }

        // ================================================== cycle de vie

        public bool HasItem { get { return _item != null; } }
        public bool ShowsItem(BinderItem item) { return _item == item; }
        public BinderItem Item { get { return _item; } }
        public TimeSystem System { get { return _system; } }
        public TimelineEvent Selected { get { return _selected; } }
        /// <summary>Le panneau que le Général du rail héberge tant que la frise est ouverte.</summary>
        public Control Inspector { get { return _inspector; } }

        public void Load(BinderItem timeline, Project project)
        {
            var same = _item == timeline;
            _item = timeline;
            _project = project;
            if (timeline.Timeline == null) timeline.Timeline = new TimelineInfo { Types = TimelineInfo.DefaultTypes() };
            if (!same)
            {
                _tab = -1;
                _hidden.Clear();
                _selected = null;
                _scrollY = 0;
                _fitPending = true;
            }
            Refresh();
            if (_fitPending && _surface.Bounds.Width > 0) FitAll();
        }

        public void Clear()
        {
            _item = null;
            _selected = null;
            StopFlash();
        }

        /// <summary>Le modèle a changé (une brique, le système, le titre) : tout se relit.</summary>
        public void Refresh()
        {
            if (_item == null) return;
            _system = Timelines.SystemOf(_project, _item);
            _stats = Timelines.Stats(_system, _item.Timeline);
            if (_tab >= _stats.Segments.Count) _tab = -1;
            if (_selected != null && _item.Timeline.FindEvent(_selected.Id) == null) _selected = null;
            _title.Text = _item.Title;
            _systemButton.Content = Icons.Label("pile-frises", _system.Name, 13, Chrome.Ink);
            _goTo.Watermark = "Aller à… (" + _system.Example() + ")";
            RebuildTabs();
            RebuildFilters();
            ComputeRange();
            ClampView();
            Redraw();
            RefreshInspector();
        }

        public void OnThemeChanged() { Redraw(); if (_item != null) { RebuildFilters(); RefreshInspector(); } }

        private void NotifyEdited()
        {
            Refresh();
            var handler = Edited;
            if (handler != null) handler();
        }

        private void Redraw()
        {
            _empty.IsVisible = _item != null && _item.Timeline != null && _item.Timeline.Events.Count == 0;
            _surface.Relayout();
            _surface.InvalidateVisual();
            _minimap.InvalidateVisual();
        }

        // ================================================== onglets et filtres

        private void RebuildTabs()
        {
            _tabs.Children.Clear();
            _tabsBar.IsVisible = _system.HasSegments;
            if (!_system.HasSegments) return;
            _tabs.Children.Add(TabButton(-1, "Tout", TimeSystem.YearsLabel(_stats.TotalYears)));
            for (var i = 0; i < _stats.Segments.Count; i++)
            {
                var segment = _stats.Segments[i];
                _tabs.Children.Add(TabButton(i, segment.Name, TimeSystem.YearsLabel(segment.Length) + (segment.Open ? " (en cours)" : "")));
            }
        }

        private ToggleButton TabButton(int index, string name, string duration)
        {
            var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            label.Inlines.Add(new Run(name) { FontWeight = FontWeight.SemiBold });
            label.Inlines.Add(new Run("  " + duration) { Foreground = Chrome.SoftText, FontSize = 11 });
            var button = new ToggleButton
            {
                Classes = { Marabook.App.Theme.Owned },
                Content = label,
                IsChecked = _tab == index,
                Padding = new Thickness(10, 4, 10, 4),
                Tag = "timeline-tab",
                [ToolTip.TipProperty] = index < 0 ? "Toute la temporalité, segments cumulés" : _system.SegmentLabel + " " + (index + 1) + " — " + name
            };
            button.Click += delegate { SelectTab(index); };
            return button;
        }

        private void SelectTab(int index)
        {
            _tab = index;
            _scrollY = 0;
            RebuildTabs();
            ComputeRange();
            FitAll();
            RefreshInspector();
        }

        private void RebuildFilters()
        {
            _filters.Children.Clear();
            if (_item == null) return;
            _filters.Children.Add(new TextBlock { Text = "Types :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) });
            foreach (var type in _item.Timeline.Types)
            {
                var captured = type;
                var count = 0;
                foreach (var entry in _item.Timeline.Events) if (entry.TypeId == type.Id) count++;
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                content.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(ColorOf(type)), VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new TextBlock { Text = type.Name + "  " + count, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
                var chip = new ToggleButton
                {
                    Classes = { Marabook.App.Theme.Owned },
                    Content = content,
                    IsChecked = !_hidden.Contains(type.Id),
                    Padding = new Thickness(8, 3, 9, 3),
                    Margin = new Thickness(0, 0, 6, 6),
                    Tag = "timeline-filter",
                    [ToolTip.TipProperty] = "Montrer ou masquer les événements « " + type.Name + " »"
                };
                chip.Click += delegate
                {
                    if (!_hidden.Remove(captured.Id)) _hidden.Add(captured.Id);
                    Redraw();
                    RefreshInspector();
                };
                _filters.Children.Add(chip);
            }
            var manage = Buttons.Text("Gérer les types…", "Renommer, recolorer, ajouter ou retirer des types d'événement", Buttons.Compact, Buttons.Look.Calm);
            manage.Margin = new Thickness(4, 0, 0, 6);
            manage.Click += async delegate
            {
                if (_item == null) return;
                if (await TimelineTypesDialog.Edit(Ui.OwnerOf(this), _project, _item.Timeline)) NotifyEdited();
            };
            _filters.Children.Add(manage);
        }

        internal static Color ColorOf(TimelineEventType type)
        {
            Color color;
            if (type != null && Color.TryParse(type.Color, out color)) return color;
            return Chrome.Accent.Color;
        }

        private bool Shown(TimelineEvent entry)
        {
            if (entry.Start == null) return false;
            if (entry.TypeId != null && _hidden.Contains(entry.TypeId)) return false;
            if (_tab < 0 || _tab >= _stats.Segments.Count) return true;
            var segment = _stats.Segments[_tab];
            return Timelines.StartOf(_system, entry) < segment.Start + segment.Length && Timelines.EndOf(_system, entry) > segment.Start;
        }

        // ================================================== l'étendue, le zoom

        private void ComputeRange()
        {
            if (_tab >= 0 && _tab < _stats.Segments.Count)
            {
                _rangeMin = _stats.Segments[_tab].Start;
                _rangeMax = _rangeMin + Math.Max(1, _stats.Segments[_tab].Length);
            }
            else
            {
                _rangeMin = _stats.Min;
                _rangeMax = _stats.Max;
                if (_rangeMax - _rangeMin < 1) { _rangeMin = Math.Floor(_rangeMin); _rangeMax = _rangeMin + 10; }
            }
        }

        private double MinScale { get { return Math.Max(1e-9, Math.Max(40, _surface.Bounds.Width - SidePad - LabelPad) / Math.Max(1e-6, _rangeMax - _rangeMin) * 0.6); } }
        private double MaxScale { get { return Math.Max(MinScale * 2, _system.BaseDays * 48.0); } }

        public void FitAll()
        {
            var width = _surface.Bounds.Width;
            if (width <= 0) { _fitPending = true; return; }
            _fitPending = false;
            // À droite, la place du libellé des dernières briques.
            _scale = Math.Max(40, width - SidePad - LabelPad) / Math.Max(1e-6, _rangeMax - _rangeMin);
            _viewStart = _rangeMin - SidePad / _scale;
            Redraw();
        }

        private void ZoomBy(double factor, double anchorX)
        {
            var anchor = _viewStart + anchorX / _scale;
            _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));
            _viewStart = anchor - anchorX / _scale;
            _fitPending = false;
            ClampView();
            Redraw();
        }

        private void PanBy(double dx, double dy)
        {
            _viewStart -= dx / _scale;
            _scrollY = Math.Max(0, Math.Min(_surface.MaxScroll, _scrollY - dy));
            _fitPending = false;
            ClampView();
            Redraw();
        }

        private void ClampView()
        {
            var width = Math.Max(1, _surface.Bounds.Width);
            if (_scale <= 0) _scale = 1;
            if (_surface.Bounds.Width > 0) _scale = Math.Max(MinScale, Math.Min(MaxScale, _scale));
            var span = width / _scale;
            var min = _rangeMin - span * 0.6;
            var max = _rangeMax - span * 0.4;
            if (max < min) max = min;
            _viewStart = Math.Max(min, Math.Min(max, _viewStart));
        }

        private void CenterOn(double axis)
        {
            _viewStart = axis - _surface.Bounds.Width / 2 / _scale;
            _fitPending = false;
            ClampView();
            Redraw();
        }

        /// <summary>Montre une date (un lien de date suivi, « Aller à ») : son
        /// onglet, la vue centrée dessus, un trait qui s'éteint.</summary>
        public void GoTo(TimePoint point)
        {
            if (_item == null || point == null) return;
            var axis = _system.AxisMid(point);
            if (_tab >= 0 && _tab < _stats.Segments.Count)
            {
                var segment = _stats.Segments[_tab];
                if (axis < segment.Start || axis >= segment.Start + segment.Length) { _tab = -1; RebuildTabs(); ComputeRange(); RefreshInspector(); }
            }
            if (axis < _rangeMin) _rangeMin = Math.Floor(axis) - 1;
            if (axis > _rangeMax) _rangeMax = Math.Ceiling(axis) + 1;
            // Assez près pour distinguer l'année visée de ses voisines.
            if (_surface.Bounds.Width > 0 && _scale < 12) _scale = Math.Min(MaxScale, Math.Max(MinScale, 12));
            _flash = point;
            StopFlashTimer();
            _flashTimer = new DispatcherTimer(TimeSpan.FromSeconds(4), DispatcherPriority.Background, delegate { StopFlash(); Redraw(); });
            _flashTimer.Start();
            if (_surface.Bounds.Width > 0) CenterOn(axis);
            else { _fitPending = false; _viewStart = axis - 30; }
        }

        private void StopFlashTimer() { if (_flashTimer != null) { _flashTimer.Stop(); _flashTimer = null; } }
        private void StopFlash() { StopFlashTimer(); _flash = null; }

        /// <summary>Choisit une brique (la recherche, un clic du rail) et la montre.</summary>
        public void SelectEvent(string eventId)
        {
            if (_item == null) return;
            var entry = _item.Timeline.FindEvent(eventId);
            if (entry == null) return;
            if (entry.TypeId != null && _hidden.Remove(entry.TypeId)) RebuildFilters();
            _selected = entry;
            if (_surface.Bounds.Width > 0) CenterOn((Timelines.StartOf(_system, entry) + Timelines.EndOf(_system, entry)) / 2);
            RefreshInspector();
        }

        // ================================================== les briques

        private async void NewEvent(TimePoint at)
        {
            if (_item == null) return;
            var entry = new TimelineEvent { Start = at ?? DefaultDate() };
            if (_item.Timeline.Types.Count > 0) entry.TypeId = _item.Timeline.Types[0].Id;
            var result = await TimelineEventDialog.Ask(Ui.OwnerOf(this), _project, _system, _item.Timeline, entry, true);
            if (result == null || result.Deleted || _item == null) return;
            _item.Timeline.Events.Add(result.Event);
            _selected = result.Event;
            if (result.Event.TypeId != null) _hidden.Remove(result.Event.TypeId);
            NotifyEdited();
        }

        /// <summary>La date proposée à un nouvel événement : le milieu de ce qu'on regarde.</summary>
        private TimePoint DefaultDate()
        {
            var width = _surface.Bounds.Width;
            var axis = width > 0 ? _viewStart + width / 2 / _scale : _rangeMin;
            return _system.FromAxis(axis, PrecisionForScale());
        }

        /// <summary>La précision qu'un double-clic peut honnêtement donner à ce zoom.</summary>
        private int PrecisionForScale()
        {
            if (_scale / _system.BaseDays >= 6) return 2;
            if (_scale / Math.Max(1, _system.Months.Count) >= 24) return 1;
            return 0;
        }

        private async void EditEvent(TimelineEvent entry)
        {
            if (_item == null || entry == null) return;
            var result = await TimelineEventDialog.Ask(Ui.OwnerOf(this), _project, _system, _item.Timeline, entry.Clone(), false);
            if (result == null || _item == null) return;
            var index = _item.Timeline.Events.IndexOf(entry);
            if (index < 0) return;
            if (result.Deleted)
            {
                _item.Timeline.Events.RemoveAt(index);
                if (_selected == entry) _selected = null;
            }
            else
            {
                _item.Timeline.Events[index] = result.Event;
                if (_selected == entry) _selected = result.Event;
            }
            NotifyEdited();
        }

        private async void DeleteEvent(TimelineEvent entry)
        {
            if (_item == null || entry == null) return;
            var answer = await MessageDialog.Show(Ui.OwnerOf(this), "Retirer « " + entry.Title + " » de la frise ?", "Retirer l'événement",
                MessageButtons.YesNo, MessageIcon.Question);
            if (answer != MessageResult.Yes || _item == null) return;
            _item.Timeline.Events.Remove(entry);
            if (_selected == entry) _selected = null;
            NotifyEdited();
        }

        private void DuplicateEvent(TimelineEvent entry)
        {
            if (_item == null || entry == null) return;
            var copy = entry.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Title = entry.Title + " (copie)";
            _item.Timeline.Events.Add(copy);
            _selected = copy;
            NotifyEdited();
        }

        private void OpenAssigned(TimelineEvent entry)
        {
            var target = entry == null || _project == null ? null : _project.FindById(entry.LinkId);
            var handler = NavigateRequested;
            if (target != null && handler != null) handler(target);
        }

        private ContextMenu MenuFor(TimelineEvent entry, TimePoint at)
        {
            var menu = new ContextMenu();
            if (entry != null)
            {
                var edit = new MenuItem { Header = "Modifier…" };
                edit.Click += delegate { EditEvent(entry); };
                menu.Items.Add(edit);
                var target = _project == null ? null : _project.FindById(entry.LinkId);
                if (target != null)
                {
                    var open = new MenuItem { Header = "Ouvrir « " + target.Title + " »" };
                    open.Click += delegate { OpenAssigned(entry); };
                    menu.Items.Add(open);
                }
                var copy = new MenuItem { Header = "Dupliquer" };
                copy.Click += delegate { DuplicateEvent(entry); };
                menu.Items.Add(copy);
                menu.Items.Add(new Separator());
                var remove = new MenuItem { Header = "Retirer de la frise…" };
                remove.Click += delegate { DeleteEvent(entry); };
                menu.Items.Add(remove);
                return menu;
            }
            var create = new MenuItem { Header = "Nouvel événement ici (" + _system.Format(at) + ")…" };
            create.Click += delegate { NewEvent(at); };
            menu.Items.Add(create);
            var fit = new MenuItem { Header = "Tout voir" };
            fit.Click += delegate { FitAll(); };
            menu.Items.Add(fit);
            return menu;
        }

        // ================================================== le Général du rail

        private static TextBlock Caption(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
        }

        private static TextBlock Line(string label, string value)
        {
            var line = new TextBlock { FontSize = 12, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
            line.Inlines.Add(new Run(label + " ") { Foreground = Chrome.SoftText });
            line.Inlines.Add(new Run(value) { FontWeight = FontWeight.SemiBold });
            return line;
        }

        /// <summary>Le nombre d'événements, la durée totale de la temporalité,
        /// celle de l'ère de l'onglet — puis la brique choisie.</summary>
        private void RefreshInspector()
        {
            _inspector.Children.Clear();
            if (_item == null) return;
            var shown = 0;
            foreach (var entry in _item.Timeline.Events) if (Shown(entry)) shown++;
            _inspector.Children.Add(new TextBlock { Text = "FRISE", Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var events = TimeSystem.Count(_stats.Events, "événement", "événements");
            if (_stats.Events > 0) events += " (" + _stats.Points + " ponctuel" + (_stats.Points > 1 ? "s" : "") + ", " + _stats.Spans + " sur la durée)";
            var count = Line("Événements :", events);
            count.Tag = "timeline-stat-events";
            _inspector.Children.Add(count);
            if (shown != _stats.Events) _inspector.Children.Add(Line("Affichés :", shown.ToString(CultureInfo.CurrentCulture)));
            var total = Line("Durée totale de la temporalité :", TimeSystem.YearsLabel(_stats.TotalYears));
            total.Tag = "timeline-stat-total";
            _inspector.Children.Add(total);
            _inspector.Children.Add(Line("Système :", _system.Name));
            _inspector.Children.Add(Line("Notation :", _system.Example()));
            if (_system.HasSegments)
            {
                if (_tab >= 0 && _tab < _stats.Segments.Count)
                {
                    var segment = _stats.Segments[_tab];
                    _inspector.Children.Add(Caption(_system.SegmentLabel.ToUpper(CultureInfo.CurrentCulture) + " OUVERTE"));
                    var era = Line(segment.Name + " :", TimeSystem.YearsLabel(segment.Length) + (segment.Open ? " (en cours)" : ""));
                    era.Tag = "timeline-stat-segment";
                    _inspector.Children.Add(era);
                    _inspector.Children.Add(Line("Événements :", segment.Events.ToString(CultureInfo.CurrentCulture)));
                    _inspector.Children.Add(Line("De", _system.FormatYear(segment.Start) + " à " + _system.FormatYear(segment.Start + Math.Max(1, segment.Length) - 1)));
                }
                _inspector.Children.Add(Caption("DURÉE PAR " + _system.SegmentLabel.ToUpper(CultureInfo.CurrentCulture)));
                foreach (var segment in _stats.Segments)
                    _inspector.Children.Add(Line(segment.Name + " :", TimeSystem.YearsLabel(segment.Length) + (segment.Open ? " (en cours)" : "")
                        + " · " + TimeSystem.Count(segment.Events, "événement", "événements")));
            }
            if (_selected != null)
            {
                var entry = _selected;
                var type = _item.Timeline.FindType(entry.TypeId);
                _inspector.Children.Add(Caption("ÉVÉNEMENT CHOISI"));
                _inspector.Children.Add(new TextBlock { Text = entry.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, Tag = "timeline-selected-title" });
                _inspector.Children.Add(new TextBlock { Text = Timelines.WhenLabel(_system, entry), FontSize = 12, Foreground = Chrome.Time, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
                var weekDay = entry.IsSpan ? null : _system.WeekDayOf(entry.Start);
                if (weekDay != null) _inspector.Children.Add(Line("Jour :", weekDay));
                if (type != null) _inspector.Children.Add(Line("Type :", type.Name));
                var target = _project == null ? null : _project.FindById(entry.LinkId);
                if (target != null)
                {
                    var open = Buttons.IconText("arrow-up-right-bold", TimelineLinks.Label(entry.LinkKind) + " : " + target.Title, "Ouvrir l'élément assigné à cet événement", Buttons.Compact, Buttons.Look.Outline);
                    open.HorizontalAlignment = HorizontalAlignment.Left;
                    open.Margin = new Thickness(0, 4, 0, 4);
                    open.Click += delegate { OpenAssigned(entry); };
                    _inspector.Children.Add(open);
                }
                if (!string.IsNullOrEmpty(entry.Description))
                    _inspector.Children.Add(new TextBlock { Text = entry.Description, FontSize = 12, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
                var edit = Buttons.Text("Modifier…", "Modifier cet événement", Buttons.Compact, Buttons.Look.Outline);
                edit.HorizontalAlignment = HorizontalAlignment.Left;
                edit.Margin = new Thickness(0, 4, 0, 0);
                edit.Click += delegate { EditEvent(entry); };
                _inspector.Children.Add(edit);
            }
            var handler = InspectorChanged;
            if (handler != null) handler();
        }

        /// <summary>La ligne courte de la barre d'état.</summary>
        public string StatusLabel
        {
            get
            {
                if (_item == null) return "";
                return TimeSystem.Count(_stats.Events, "événement", "événements") + " · " + TimeSystem.YearsLabel(_stats.TotalYears);
            }
        }

        // ================================================== sonde

        internal int TabForProbe { get { return _tab; } }
        internal int TabCountForProbe { get { return _tabs.Children.Count; } }
        internal void SelectTabForProbe(int index) { SelectTab(index); }
        internal double ScaleForProbe { get { return _scale; } }
        internal void ZoomForProbe(double factor) { ZoomBy(factor, _surface.Bounds.Width / 2); }
        internal void ToggleTypeForProbe(string typeId) { if (!_hidden.Remove(typeId)) _hidden.Add(typeId); Redraw(); RefreshInspector(); }
        internal int PlacedCountForProbe { get { return _surface.PlacedCount; } }
        internal bool MinimapShownForProbe { get { return _minimap.IsVisible && _minimap.Bounds.Width > 0; } }
        internal double ViewStartForProbe { get { return _viewStart; } }
        internal void MinimapClickForProbe(double fraction) { _minimap.MoveTo(fraction * _minimap.Bounds.Width); }
        internal List<string> RulerLabelsForProbe() { return _surface.RulerLabels(); }
        internal List<string> InspectorTextsForProbe()
        {
            var texts = new List<string>();
            foreach (var child in _inspector.Children)
            {
                var block = child as TextBlock;
                if (block == null) continue;
                if (block.Inlines != null && block.Inlines.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var inline in block.Inlines) { var run = inline as Run; if (run != null) sb.Append(run.Text); }
                    texts.Add(sb.ToString());
                }
                else texts.Add(block.Text ?? "");
            }
            return texts;
        }

        // ================================================== la surface dessinée

        private sealed class Tick
        {
            public double Axis;
            public string Label;
            public bool Major;
        }

        private sealed class Placed
        {
            public TimelineEvent Event;
            public Rect Bounds;      // la zone cliquable (marque + libellé)
            public double X1, X2;    // le début et la fin sur l'axe, en pixels
            public int Lane;
            public FormattedText Label;
            public bool LabelInside;
            public Color Color;
        }

        /// <summary>Les graduations de ce qu'on voit : les jours, les mois ou des
        /// années par pas « ronds » — ronds dans la NOTATION (E2.100, E2.200),
        /// donc recalés à chaque segment qui repart de zéro.</summary>
        private List<Tick> Ticks(double viewEnd)
        {
            var ticks = new List<Tick>();
            var system = _system;
            var perDay = _scale / system.BaseDays;
            var perMonth = _scale / Math.Max(1, system.Months.Count);
            var firstYear = (long)Math.Floor(_viewStart);
            var lastYear = (long)Math.Floor(viewEnd);
            if (perDay >= 22 || perMonth >= 64)
            {
                var days = perDay >= 22;
                for (var year = firstYear; year <= lastYear && ticks.Count < 2000; year++)
                    for (var month = 1; month <= system.Months.Count; month++)
                    {
                        var start = system.AxisStart(new TimePoint(year, month, 0));
                        var end = system.AxisEnd(new TimePoint(year, month, 0));
                        if (end < _viewStart || start > viewEnd) continue;
                        ticks.Add(new Tick { Axis = start, Major = true, Label = (system.Months[month - 1].Name ?? "") + " " + system.FormatYear(year) });
                        if (!days) continue;
                        var inMonth = system.DaysInMonth(year, month);
                        for (var day = 2; day <= inMonth; day++)
                        {
                            var axis = system.AxisStart(new TimePoint(year, month, day));
                            if (axis < _viewStart || axis > viewEnd) continue;
                            ticks.Add(new Tick { Axis = axis, Label = day.ToString(CultureInfo.InvariantCulture) });
                        }
                    }
                return ticks;
            }
            long step = 1;
            var wanted = 84 / _scale;
            var steps = new long[] { 1, 2, 5 };
            long magnitude = 1;
            while (step < wanted && magnitude <= 1000000000000L)
            {
                foreach (var candidate in steps)
                {
                    step = candidate * magnitude;
                    if (step >= wanted) break;
                }
                magnitude *= 10;
            }
            // Les régions : avant l'origine, puis chaque segment (ou tout l'axe).
            var first = system.FirstYear;
            if (_viewStart < 0) AddYearTicks(ticks, Math.Max(firstYear, long.MinValue / 4), Math.Min(-1, lastYear), 0, step, false);
            if (system.HasSegments)
            {
                for (var i = 0; i < _stats.Segments.Count; i++)
                {
                    var start = _stats.Segments[i].Start;
                    var end = i == _stats.Segments.Count - 1 ? long.MaxValue / 4 : start + _stats.Segments[i].Length - 1;
                    var from = Math.Max(start, firstYear);
                    var to = Math.Min(end, lastYear);
                    if (from > to) continue;
                    AddYearTicks(ticks, from, to, (system.Cumulative ? 0 : start) - first, step, true);
                    if (start >= firstYear && start <= lastYear) AddBoundary(ticks, start);
                }
            }
            else if (lastYear >= 0) AddYearTicks(ticks, Math.Max(0, firstYear), lastYear, -first, step, true);
            return ticks;
        }

        private void AddYearTicks(List<Tick> ticks, long from, long to, long origin, long step, bool positive)
        {
            // Les années t telles que (t − origin) soit un multiple du pas.
            var k = (long)Math.Ceiling((from - origin) / (double)step);
            for (var year = origin + k * step; year <= to && ticks.Count < 2000; year += step)
            {
                if (year < from) continue;
                if (positive && step > 1 && year - origin == 0) continue; // « l'an 0 » d'un segment qui commence à 1 n'existe pas
                ticks.Add(new Tick { Axis = year, Label = _system.FormatYear(year), Major = step == 1 || LeapRule.Mod(year - origin, step * 5) == 0 });
            }
        }

        private void AddBoundary(List<Tick> ticks, long year)
        {
            foreach (var tick in ticks) if (Math.Abs(tick.Axis - year) < 1e-9) { tick.Major = true; return; }
            ticks.Add(new Tick { Axis = year, Label = _system.FormatYear(year), Major = true });
        }

        private static Color SegmentTint(int index)
        {
            var tints = new[] { Color.FromRgb(0x5B, 0x67, 0xD8), Color.FromRgb(0x3E, 0x9C, 0x7A), Color.FromRgb(0xB5, 0x84, 0x3A), Color.FromRgb(0x8E, 0x6B, 0xBF), Color.FromRgb(0xC0, 0x50, 0x4D), Color.FromRgb(0x0E, 0x7C, 0x7B) };
            return tints[index % tints.Length];
        }

        private Color SegmentColor(int index)
        {
            Color color;
            if (index >= 0 && index < _system.Segments.Count && Color.TryParse(_system.Segments[index].Color, out color)) return color;
            return SegmentTint(index);
        }

        private sealed class Surface : Control
        {
            private readonly TimelineView _owner;
            private readonly List<Placed> _placed = new List<Placed>();
            private bool _panning, _moved;
            private Point _last;
            private TimelineEvent _hover;

            public double MaxScroll;
            public int PlacedCount { get { return _placed.Count; } }

            public Surface(TimelineView owner)
            {
                _owner = owner;
                ClipToBounds = true;
                Focusable = true;
            }

            private Typeface Face(FontWeight weight)
            {
                return new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, weight);
            }

            private FormattedText Text(string text, double size, IBrush brush, FontWeight weight)
            {
                return new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face(weight), size, brush);
            }

            private double X(double axis) { return (axis - _owner._viewStart) * _owner._scale; }
            private double AxisAt(double x) { return _owner._viewStart + x / _owner._scale; }

            public List<string> RulerLabels()
            {
                var labels = new List<string>();
                if (_owner._item == null || Bounds.Width <= 0) return labels;
                foreach (var tick in _owner.Ticks(AxisAt(Bounds.Width))) labels.Add(tick.Label);
                return labels;
            }

            /// <summary>Range les briques visibles en couloirs : chacune prend
            /// le premier couloir où sa marque ET son libellé tiennent.</summary>
            public void Relayout()
            {
                _placed.Clear();
                MaxScroll = 0;
                var owner = _owner;
                if (owner._item == null || owner._item.Timeline == null || Bounds.Width <= 0) return;
                var events = new List<TimelineEvent>();
                foreach (var entry in owner._item.Timeline.Events) if (owner.Shown(entry)) events.Add(entry);
                var system = owner._system;
                events.Sort(delegate(TimelineEvent a, TimelineEvent b)
                {
                    var order = Timelines.StartOf(system, a).CompareTo(Timelines.StartOf(system, b));
                    return order != 0 ? order : string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
                });
                var laneEnds = new List<double>();
                foreach (var entry in events)
                {
                    var type = owner._item.Timeline.FindType(entry.TypeId);
                    var x1 = X(Timelines.StartOf(system, entry));
                    var x2 = X(Timelines.EndOf(system, entry));
                    var title = string.IsNullOrEmpty(entry.Title) ? "(sans titre)" : entry.Title;
                    if (entry.LinkId != null && owner._project != null && owner._project.FindById(entry.LinkId) != null) title += "  ↗";
                    var label = Text(title, 12, Chrome.Ink, FontWeight.Medium);
                    label.MaxLineCount = 1;
                    label.Trimming = TextTrimming.CharacterEllipsis;
                    label.MaxTextWidth = 240;
                    var placed = new Placed { Event = entry, X1 = x1, X2 = x2, Label = label, Color = ColorOf(type) };
                    double left, right;
                    if (entry.IsSpan)
                    {
                        var barRight = Math.Max(x2, x1 + 8);
                        placed.LabelInside = barRight - x1 >= label.Width + 16;
                        left = x1;
                        right = placed.LabelInside ? barRight : barRight + 8 + label.Width;
                        placed.X2 = barRight;
                    }
                    else
                    {
                        left = x1 - 8;
                        right = x1 + 12 + label.Width;
                    }
                    var lane = 0;
                    while (lane < laneEnds.Count && laneEnds[lane] > left - 10) lane++;
                    if (lane == laneEnds.Count) laneEnds.Add(right); else laneEnds[lane] = right;
                    placed.Lane = lane;
                    placed.Bounds = new Rect(left, 0, Math.Max(8, right - left), LaneH - 6); // l'ordonnée se pose au dessin
                    _placed.Add(placed);
                }
                var needed = RulerH + BandH + 10 + laneEnds.Count * LaneH + 12;
                MaxScroll = Math.Max(0, needed - Bounds.Height);
                if (owner._scrollY > MaxScroll) owner._scrollY = MaxScroll;
            }

            private double LaneTop(int lane) { return RulerH + BandH + 10 + lane * LaneH - _owner._scrollY; }

            public override void Render(DrawingContext context)
            {
                var owner = _owner;
                var width = Bounds.Width;
                var height = Bounds.Height;
                context.DrawRectangle(Chrome.CanvasBg, new Pen(Chrome.Border, 1), new Rect(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1)), 8, 8);
                if (owner._item == null || width <= 0 || height <= 0) return;
                var system = owner._system;
                var viewEnd = AxisAt(width);

                // — Avant l'origine : un voile.
                if (owner._viewStart < 0)
                    context.DrawRectangle(new SolidColorBrush(Chrome.Border.Color, 0.28), null, new Rect(0, RulerH, Math.Min(width, X(0)), height - RulerH));

                // — Les segments : une bande teintée et son nom.
                if (system.HasSegments)
                    for (var i = 0; i < owner._stats.Segments.Count; i++)
                    {
                        var segment = owner._stats.Segments[i];
                        var x1 = X(segment.Start);
                        var x2 = X(segment.Start + segment.Length);
                        if (x2 < 0 || x1 > width) continue;
                        var tint = owner.SegmentColor(i);
                        var left = Math.Max(0, x1);
                        var right = Math.Min(width, x2);
                        context.DrawRectangle(new SolidColorBrush(tint, i % 2 == 0 ? 0.07 : 0.035), null, new Rect(left, RulerH, right - left, height - RulerH));
                        context.DrawRectangle(new SolidColorBrush(tint, 0.20), null, new Rect(left, RulerH, right - left, BandH));
                        var name = Text(segment.Name + "  ·  " + TimeSystem.YearsLabel(segment.Length), 11, Chrome.Ink, FontWeight.SemiBold);
                        name.MaxLineCount = 1;
                        name.Trimming = TextTrimming.CharacterEllipsis;
                        name.MaxTextWidth = Math.Max(10, right - left - 16);
                        if (right - left > 40) context.DrawText(name, new Point(left + 8, RulerH + (BandH - name.Height) / 2));
                        if (x1 >= 0 && x1 <= width) context.DrawLine(new Pen(new SolidColorBrush(tint, 0.7), 1.5), new Point(x1, RulerH), new Point(x1, height));
                    }

                // — La règle et ses graduations.
                var ticks = owner.Ticks(viewEnd);
                var gridPen = new Pen(new SolidColorBrush(Chrome.Border.Color, 0.6), 1);
                var majorPen = new Pen(Chrome.BorderStrong, 1);
                context.DrawRectangle(Chrome.BarBgLight, null, new Rect(1, 1, Math.Max(0, width - 2), RulerH - 1), 7, 7);
                context.DrawLine(new Pen(Chrome.Border, 1), new Point(0, RulerH), new Point(width, RulerH));
                foreach (var tick in ticks)
                {
                    var x = Math.Round(X(tick.Axis)) + 0.5;
                    if (x < -1 || x > width + 1) continue;
                    context.DrawLine(tick.Major ? majorPen : gridPen, new Point(x, tick.Major ? RulerH - 12 : RulerH - 6), new Point(x, RulerH));
                    context.DrawLine(gridPen, new Point(x, RulerH + (system.HasSegments ? BandH : 0)), new Point(x, height));
                }
                // Les libellés : les graduations fortes d'abord, les autres là où il reste de la place.
                var occupied = new List<Rect>();
                for (var pass = 0; pass < 2; pass++)
                    foreach (var tick in ticks)
                    {
                        if (tick.Major != (pass == 0)) continue;
                        var x = X(tick.Axis);
                        if (x < -60 || x > width + 10) continue;
                        var label = Text(tick.Label, 11, tick.Major ? (IBrush)Chrome.Ink : Chrome.SoftText, tick.Major ? FontWeight.SemiBold : FontWeight.Normal);
                        var box = new Rect(x + 4, 5, label.Width, label.Height);
                        var free = true;
                        foreach (var other in occupied) if (other.Inflate(new Thickness(8, 0, 8, 0)).Intersects(box)) { free = false; break; }
                        if (!free || box.Right > width - 2 || box.X < 2) continue;
                        occupied.Add(box);
                        context.DrawText(label, box.TopLeft);
                    }

                // — Les briques.
                using (context.PushClip(new Rect(0, RulerH + (system.HasSegments ? BandH : 0), width, Math.Max(0, height - RulerH - (system.HasSegments ? BandH : 0)))))
                {
                    for (var i = 0; i < _placed.Count; i++)
                    {
                        var placed = _placed[i];
                        var top = LaneTop(placed.Lane);
                        placed.Bounds = new Rect(placed.Bounds.X, top, placed.Bounds.Width, LaneH - 6);
                        if (top > height || top + LaneH < RulerH || placed.Bounds.Right < 0 || placed.Bounds.X > width) continue;
                        var selected = owner._selected == placed.Event;
                        var hovered = _hover == placed.Event;
                        var color = placed.Color;
                        var middle = top + (LaneH - 6) / 2;
                        if (placed.Event.IsSpan)
                        {
                            var bar = new Rect(placed.X1, top + 2, Math.Max(8, placed.X2 - placed.X1), LaneH - 10);
                            context.DrawRectangle(new SolidColorBrush(color, hovered ? 0.42 : 0.28), new Pen(selected ? (IBrush)Chrome.Ink : new SolidColorBrush(color), selected ? 2 : 1.3), bar, 6, 6);
                            if (placed.LabelInside)
                            {
                                var from = Math.Max(bar.X, 0) + 8; // le libellé reste lisible quand la barre déborde à gauche
                                if (from + placed.Label.Width > bar.Right - 6) from = bar.Right - 6 - placed.Label.Width;
                                context.DrawText(placed.Label, new Point(from, middle - placed.Label.Height / 2));
                            }
                            else context.DrawText(placed.Label, new Point(bar.Right + 8, middle - placed.Label.Height / 2));
                        }
                        else
                        {
                            context.DrawLine(new Pen(new SolidColorBrush(color, 0.45), 1), new Point(placed.X1, RulerH), new Point(placed.X1, middle));
                            context.DrawEllipse(new SolidColorBrush(color), new Pen(selected ? (IBrush)Chrome.Ink : Chrome.CanvasBg, selected ? 2 : 1.5), new Point(placed.X1, middle), hovered ? 7 : 6, hovered ? 7 : 6);
                            context.DrawText(placed.Label, new Point(placed.X1 + 12, middle - placed.Label.Height / 2));
                        }
                    }
                }

                // — L'époque active, si elle se lit dans ce système.
                TimeSystem epochSystem;
                BinderItem epochTimeline;
                var epoch = Epochs.Current(owner._project, out epochSystem, out epochTimeline);
                if (epoch != null && epochSystem.Id == system.Id) Marker(context, system.AxisMid(epoch), "Époque · " + system.Format(epoch), Chrome.Warn, width, height);
                if (owner._flash != null) Marker(context, system.AxisMid(owner._flash), system.Format(owner._flash), Chrome.Time, width, height);
            }

            private void Marker(DrawingContext context, double axis, string caption, IBrush brush, double width, double height)
            {
                var x = Math.Round(X(axis)) + 0.5;
                if (x < 0 || x > width) return;
                context.DrawLine(new Pen(brush, 2), new Point(x, RulerH), new Point(x, height));
                var label = Text(caption, 11, Brushes.White, FontWeight.SemiBold);
                var boxWidth = label.Width + 12;
                var left = Math.Min(Math.Max(2, x - boxWidth / 2), Math.Max(2, width - boxWidth - 2));
                context.DrawRectangle(brush, null, new Rect(left, height - label.Height - 12, boxWidth, label.Height + 6), 5, 5);
                context.DrawText(label, new Point(left + 6, height - label.Height - 9));
            }

            private Placed Hit(Point point)
            {
                for (var i = _placed.Count - 1; i >= 0; i--)
                    if (_placed[i].Bounds.Inflate(3).Contains(point)) return _placed[i];
                return null;
            }

            protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
            {
                base.OnPointerWheelChanged(e);
                if (_owner._item == null) return;
                var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0) _owner.PanBy(delta * 90, 0);
                else if ((e.KeyModifiers & KeyModifiers.Alt) != 0) _owner.PanBy(0, delta * 60);
                else _owner.ZoomBy(Math.Pow(1.25, delta), e.GetPosition(this).X);
                e.Handled = true;
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                base.OnPointerPressed(e);
                if (_owner._item == null) return;
                Focus();
                var point = e.GetPosition(this);
                var properties = e.GetCurrentPoint(this).Properties;
                var hit = Hit(point);
                if (properties.IsRightButtonPressed)
                {
                    if (hit != null) { _owner._selected = hit.Event; _owner.RefreshInspector(); InvalidateVisual(); }
                    Ui.ShowMenu(_owner.MenuFor(hit == null ? null : hit.Event, _owner._system.FromAxis(AxisAt(point.X), _owner.PrecisionForScale())), this);
                    e.Handled = true;
                    return;
                }
                if (properties.IsLeftButtonPressed && e.ClickCount >= 2)
                {
                    if (hit != null) _owner.EditEvent(hit.Event);
                    else if (point.Y > RulerH) _owner.NewEvent(_owner._system.FromAxis(AxisAt(point.X), _owner.PrecisionForScale()));
                    e.Handled = true;
                    return;
                }
                if (properties.IsLeftButtonPressed && hit != null)
                {
                    if (_owner._selected != hit.Event) { _owner._selected = hit.Event; _owner.RefreshInspector(); InvalidateVisual(); }
                    e.Handled = true;
                    return;
                }
                if (properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed)
                {
                    _panning = true;
                    _moved = false;
                    _last = point;
                    e.Pointer.Capture(this);
                    e.Handled = true;
                }
            }

            protected override void OnPointerMoved(PointerEventArgs e)
            {
                base.OnPointerMoved(e);
                var point = e.GetPosition(this);
                if (_panning)
                {
                    if (Math.Abs(point.X - _last.X) + Math.Abs(point.Y - _last.Y) > 0.5) _moved = true;
                    _owner.PanBy(point.X - _last.X, point.Y - _last.Y);
                    _last = point;
                    Cursor = new Cursor(StandardCursorType.SizeAll);
                    return;
                }
                var hit = Hit(point);
                var over = hit == null ? null : hit.Event;
                if (over == _hover) return;
                _hover = over;
                Cursor = over != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
                ToolTip.SetTip(this, over == null ? null : _owner.TipFor(over));
                InvalidateVisual();
            }

            protected override void OnPointerReleased(PointerReleasedEventArgs e)
            {
                base.OnPointerReleased(e);
                if (!_panning) return;
                _panning = false;
                e.Pointer.Capture(null);
                Cursor = Cursor.Default;
                // Un clic sans glisser sur le fond : plus de brique choisie.
                if (!_moved && _owner._selected != null) { _owner._selected = null; _owner.RefreshInspector(); InvalidateVisual(); }
            }

            protected override void OnPointerExited(PointerEventArgs e)
            {
                base.OnPointerExited(e);
                if (_hover == null) return;
                _hover = null;
                ToolTip.SetTip(this, null);
                InvalidateVisual();
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (_owner._item == null) return;
                if (e.Key == Key.Delete && _owner._selected != null) { _owner.DeleteEvent(_owner._selected); e.Handled = true; }
                else if ((e.Key == Key.Enter || e.Key == Key.Return) && _owner._selected != null) { _owner.EditEvent(_owner._selected); e.Handled = true; }
                else if (e.Key == Key.Left) { _owner.PanBy(120, 0); e.Handled = true; }
                else if (e.Key == Key.Right) { _owner.PanBy(-120, 0); e.Handled = true; }
                else if (e.Key == Key.Add || e.Key == Key.OemPlus) { _owner.ZoomBy(1.5, Bounds.Width / 2); e.Handled = true; }
                else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) { _owner.ZoomBy(1 / 1.5, Bounds.Width / 2); e.Handled = true; }
            }
        }

        private string TipFor(TimelineEvent entry)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(string.IsNullOrEmpty(entry.Title) ? "(sans titre)" : entry.Title).Append('\n').Append(Timelines.WhenLabel(_system, entry));
            var type = _item == null ? null : _item.Timeline.FindType(entry.TypeId);
            if (type != null) sb.Append('\n').Append(type.Name);
            var target = _project == null ? null : _project.FindById(entry.LinkId);
            if (target != null) sb.Append('\n').Append(TimelineLinks.Label(entry.LinkKind)).Append(" : ").Append(target.Title);
            if (!string.IsNullOrEmpty(entry.Description))
                sb.Append("\n\n").Append(entry.Description.Length > 220 ? entry.Description.Substring(0, 220).TrimEnd() + "…" : entry.Description);
            return sb.ToString();
        }

        // ================================================== la minimap

        /// <summary>Toute l'étendue de l'onglet en une bande : les segments, un
        /// trait par brique, le cadre de ce qu'on regarde — cliquer ou glisser
        /// y déplace la vue.</summary>
        private sealed class Minimap : Control
        {
            private readonly TimelineView _owner;
            private bool _dragging;

            public Minimap(TimelineView owner)
            {
                _owner = owner;
                ClipToBounds = true;
                Cursor = new Cursor(StandardCursorType.Hand);
                ToolTip.SetTip(this, "Toute l'étendue de l'onglet : cliquer ou glisser déplace la vue");
                Tag = "timeline-minimap";
            }

            private double Span { get { return Math.Max(1e-6, _owner._rangeMax - _owner._rangeMin); } }
            private double X(double axis) { return 6 + (axis - _owner._rangeMin) / Span * Math.Max(1, Bounds.Width - 12); }

            public override void Render(DrawingContext context)
            {
                var width = Bounds.Width;
                var height = Bounds.Height;
                context.DrawRectangle(Chrome.BarBgLight, new Pen(Chrome.Border, 1), new Rect(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1)), 6, 6);
                var owner = _owner;
                if (owner._item == null || width <= 12) return;
                var system = owner._system;
                if (system.HasSegments)
                    for (var i = 0; i < owner._stats.Segments.Count; i++)
                    {
                        var segment = owner._stats.Segments[i];
                        var x1 = Math.Max(6, X(segment.Start));
                        var x2 = Math.Min(width - 6, X(segment.Start + segment.Length));
                        if (x2 <= x1) continue;
                        context.DrawRectangle(new SolidColorBrush(owner.SegmentColor(i), 0.16), null, new Rect(x1, 4, x2 - x1, height - 8));
                    }
                var row = 0;
                foreach (var entry in owner._item.Timeline.Events)
                {
                    if (!owner.Shown(entry)) continue;
                    var color = ColorOf(owner._item.Timeline.FindType(entry.TypeId));
                    var x1 = X(Timelines.StartOf(system, entry));
                    var x2 = X(Timelines.EndOf(system, entry));
                    var y = 8 + (row++ % 5) * ((height - 20) / 5);
                    if (x2 < 6 || x1 > width - 6) continue;
                    context.DrawRectangle(new SolidColorBrush(color), null, new Rect(Math.Max(6, x1 - 1), y, Math.Max(3, Math.Min(width - 6, x2) - Math.Max(6, x1 - 1)), 4), 2, 2);
                }
                var viewEnd = owner._viewStart + owner._surface.Bounds.Width / Math.Max(1e-9, owner._scale);
                var left = Math.Max(1.5, X(owner._viewStart));
                var right = Math.Min(width - 1.5, X(viewEnd));
                if (right > left)
                    context.DrawRectangle(new SolidColorBrush(Chrome.Accent.Color, 0.14), new Pen(Chrome.Accent, 1.5), new Rect(left, 2, right - left, height - 4), 4, 4);
            }

            public void MoveTo(double x)
            {
                if (_owner._item == null || Bounds.Width <= 12) return;
                var axis = _owner._rangeMin + (x - 6) / Math.Max(1, Bounds.Width - 12) * Span;
                _owner.CenterOn(axis);
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                base.OnPointerPressed(e);
                _dragging = true;
                e.Pointer.Capture(this);
                MoveTo(e.GetPosition(this).X);
                e.Handled = true;
            }

            protected override void OnPointerMoved(PointerEventArgs e)
            {
                base.OnPointerMoved(e);
                if (_dragging) MoveTo(e.GetPosition(this).X);
            }

            protected override void OnPointerReleased(PointerReleasedEventArgs e)
            {
                base.OnPointerReleased(e);
                _dragging = false;
                e.Pointer.Capture(null);
            }
        }
    }
}

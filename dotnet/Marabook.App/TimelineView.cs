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
using Marabook.History;
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
    /// compte des événements, la durée de la temporalité et celle de l'ère.
    ///
    /// Lot 2 (12/11, Rémi) : chaque modification s'annule (Ctrl+Z) ; l'onglet
    /// d'une ère ne montre QUE cette ère, sur toute la largeur ; les briques
    /// qui se gênent s'empilent en escalier, de la plus ancienne à la plus
    /// récente ; « Naissance » et « Décès » naissent des dates liées sur les
    /// fiches (le décès sur la ligne de la naissance) ; plus d'infobulle au
    /// survol ; les briques que la temporalité ne sait plus placer attendent
    /// dans « Problèmes », au Général du rail.</summary>
    public class TimelineView : DockPanel
    {
        private const double RulerH = 34, BandH = 22, LaneH = 30, SidePad = 36, LabelPad = 190, EdgePad = 12; // EdgePad : la marge d'une ère seule, pour ne pas couper ses marques de bord

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
        private HistoryManager _history;
        private TimeSystem _system = TimeSystem.Gregorian();
        private TimelineStats _stats = new TimelineStats();
        // Ce que la frise montre : ses briques datées et celles nées des fiches ;
        // ce qu'elle ne sait plus placer ; les dates de fiche illisibles.
        private List<TimelineEvent> _events = new List<TimelineEvent>();
        private List<TimelineEvent> _pending = new List<TimelineEvent>();
        private List<LifeProblem> _lifeProblems = new List<LifeProblem>();
        private int _tab = -1;                       // −1 = « Tout », sinon l'index du segment
        private readonly HashSet<string> _hidden = new HashSet<string>(StringComparer.Ordinal); // les types masqués
        private TimelineEvent _selected;

        // La vue : l'année au bord gauche, les pixels par année, le défilement des couloirs.
        private double _viewStart, _scale = 1, _scrollY;
        private double _rangeMin, _rangeMax = 10;
        private bool _fitPending = true;
        private TimePoint _flash;                    // la date d'un « aller à » (un trait qui s'éteint)

        // LA VUE CALENDRIER (14/11, Rémi) : la même frise, un mois à la fois, en cases
        // (TimelineCalendar). Les boutons de zoom, les onglets d'ère et la minimap
        // n'y ont pas de sens : ils se cachent.
        private readonly TimelineCalendar _calendar;
        private readonly ToggleButton _modeTimeline, _modeCalendar;
        private readonly List<Control> _timelineOnly = new List<Control>();
        private bool _calendarMode;
        private bool _calendarPlaced;                // le calendrier a déjà son mois pour cette frise
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
            _timelineOnly.Add(zoomOut);
            _timelineOnly.Add(zoomIn);
            _timelineOnly.Add(fit);
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
                [ToolTip.TipProperty] = "Revenir à l'Astrolabe",
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
            _modeTimeline = Buttons.TextToggle("Frise", null, Buttons.Compact, Buttons.Look.Outline);
            _modeTimeline.Tag = "timeline-mode-timeline";
            _modeTimeline.Click += delegate { SetCalendarMode(false); };
            _modeCalendar = Buttons.TextToggle("Calendrier", null, Buttons.Compact, Buttons.Look.Outline);
            _modeCalendar.Margin = new Thickness(4, 0, 0, 0);
            _modeCalendar.Tag = "timeline-mode-calendar";
            _modeCalendar.Click += delegate { SetCalendarMode(true); };
            _modeTimeline.IsChecked = true;
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
            // Frise | Calendrier : au bout de la ligne des types, visible dans les deux vues
            // (dans l'en-tête, les boutons se chevauchaient dès que la page était étroite).
            var modes = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
            modes.Children.Add(_modeTimeline);
            modes.Children.Add(_modeCalendar);
            DockPanel.SetDock(modes, Dock.Right);
            var filterRow = new DockPanel();
            filterRow.Children.Add(modes);
            filterRow.Children.Add(_filters);
            var filterBar = new Border { Padding = new Thickness(16, 6, 16, 2), Child = filterRow };
            SetDock(filterBar, Dock.Top);
            Children.Add(filterBar);

            // ================================================== minimap
            _minimap = new Minimap(this) { Height = 46, Margin = new Thickness(16, 0, 16, 12) };
            SetDock(_minimap, Dock.Bottom);
            Children.Add(_minimap);
            _timelineOnly.Add(_minimap);

            // ================================================== la frise
            _surface = new Surface(this) { Margin = new Thickness(16, 8, 16, 8) };
            _empty = new TextBlock
            {
                Text = "Aucun événement pour l'instant.\nDouble-cliquez sur la frise à la date voulue, ou « Nouvel événement ».",
                Foreground = Chrome.SoftText, FontSize = 13, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false, Margin = new Thickness(0, 60, 0, 0)
            };
            _calendar = new TimelineCalendar { IsVisible = false };
            _calendar.Events = delegate
            {
                var shown = new List<TimelineEvent>();
                foreach (var entry in _events) if (ShownByType(entry)) shown.Add(entry);
                return shown;
            };
            _calendar.ColorOf = delegate(TimelineEvent entry) { return _item == null ? Chrome.Accent.Color : ColorOf(_item.Timeline.FindType(entry.TypeId)); };
            _calendar.Selected = delegate { return _selected; };
            _calendar.Select = delegate(TimelineEvent entry) { _selected = entry; RefreshInspector(); _calendar.Refresh(); };
            _calendar.Edit = delegate(TimelineEvent entry) { if (!entry.Derived) EditEvent(entry); else OpenAssigned(entry); };
            _calendar.Create = delegate(TimePoint at) { NewEvent(at); };
            _calendar.Menu = MenuFor;
            var center = new Grid();
            center.Children.Add(_surface);
            center.Children.Add(_empty);
            center.Children.Add(_calendar);
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

        public void Load(BinderItem timeline, Project project, HistoryManager history = null)
        {
            var same = _item == timeline;
            _item = timeline;
            _project = project;
            _history = history;
            if (timeline.Timeline == null) timeline.Timeline = new TimelineInfo { Types = TimelineInfo.DefaultTypes() };
            if (!same)
            {
                _tab = -1;
                _hidden.Clear();
                _selected = null;
                _scrollY = 0;
                _fitPending = true;
                _calendarPlaced = false;
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

        /// <summary>Le modèle a changé (une brique, le système, le titre, une
        /// annulation, une date liée sur une fiche) : tout se relit.</summary>
        public void Refresh()
        {
            if (_item == null) return;
            _system = Timelines.SystemOf(_project, _item);
            var life = Timelines.LifeEventsOf(_project, _item, _system);
            _events = new List<TimelineEvent>();
            _pending = new List<TimelineEvent>();
            foreach (var entry in _item.Timeline.Events)
            {
                if (entry.IsPending) _pending.Add(entry);
                else if (entry.Start != null) _events.Add(entry);
            }
            _events.AddRange(life.Events);
            _lifeProblems = life.Problems;
            _stats = Timelines.Stats(_system, _events);
            if (_tab >= _stats.Segments.Count) _tab = -1;
            // La brique choisie est retrouvée par son id (une annulation remplace les objets).
            if (_selected != null)
            {
                var id = _selected.Id;
                _selected = _events.Find(delegate(TimelineEvent e) { return e.Id == id; });
            }
            _title.Text = _item.Title;
            _systemButton.Content = Icons.Label("pile-frises", _system.Name, 13, Chrome.Ink);
            _goTo.Watermark = "Aller à… (" + _system.Example() + ")";
            RebuildTabs();
            RebuildFilters();
            ComputeRange();
            ClampView();
            Redraw();
            RefreshInspector();
            _calendar.SetSystem(_system);
            if (_calendarMode)
            {
                if (!_calendarPlaced) PlaceCalendar();
                else _calendar.Refresh();
            }
            ApplyMode();
        }

        // ================================================== frise ou calendrier

        public bool CalendarMode { get { return _calendarMode; } }

        public void SetCalendarMode(bool on)
        {
            _calendarMode = on;
            if (on && _item != null)
            {
                _calendar.SetSystem(_system);
                if (!_calendarPlaced) PlaceCalendar();
                else _calendar.Refresh();
            }
            ApplyMode();
            Redraw();
        }

        /// <summary>Le premier mois montré : celui de la brique choisie, sinon
        /// du premier événement daté au jour, sinon le milieu de ce que la frise regarde.</summary>
        private void PlaceCalendar()
        {
            _calendarPlaced = true;
            TimePoint at = _selected != null ? _selected.Start : null;
            if (at == null)
            {
                TimelineEvent first = null;
                foreach (var entry in _events)
                    if (TimeMath.IsDayPrecise(entry) && (first == null || _system.Compare(entry.Start, first.Start) < 0)) first = entry;
                if (first != null) at = first.Start;
            }
            if (at == null && _events.Count > 0) at = _system.FromAxis(_surface.Bounds.Width > 0 ? _viewStart + _surface.Bounds.Width / 2 / _scale : _rangeMin, 1);
            _calendar.Show(at ?? new TimePoint(0, 1, 1));
        }

        private void ApplyMode()
        {
            _modeTimeline.IsChecked = !_calendarMode;
            _modeCalendar.IsChecked = _calendarMode;
            _calendar.IsVisible = _calendarMode;
            _surface.IsVisible = !_calendarMode;
            foreach (var control in _timelineOnly) control.IsVisible = !_calendarMode;
            _tabsBar.IsVisible = !_calendarMode && _system.HasSegments;
        }

        /// <summary>Le filtre des types seul (le calendrier ignore l'onglet d'ère : il a sa propre navigation).</summary>
        private bool ShownByType(TimelineEvent entry)
        {
            return entry.TypeId == null || !_hidden.Contains(entry.TypeId);
        }

        public void OnThemeChanged() { Redraw(); if (_item != null) { RebuildFilters(); RefreshInspector(); } }

        /// <summary>Une modification faite : elle entre dans l'historique (Ctrl+Z
        /// rend la frise d'avant), la vue se relit, la fenêtre est prévenue.</summary>
        private void Commit(TimelineInfo before)
        {
            if (_item == null) return;
            if (_history != null && before != null) _history.Push(new TimelineEditAction(_item, before, _item.Timeline.Clone()));
            Refresh();
            var handler = Edited;
            if (handler != null) handler();
        }

        private void Redraw()
        {
            _empty.IsVisible = _item != null && _events.Count == 0 && !_calendarMode;
            _surface.Relayout();
            _surface.InvalidateVisual();
            _minimap.InvalidateVisual();
        }

        // ================================================== onglets et filtres

        private bool SegmentOnly { get { return _tab >= 0 && _tab < _stats.Segments.Count; } }

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
                [ToolTip.TipProperty] = index < 0 ? "Toute la temporalité, segments cumulés" : _system.SegmentLabel + " " + (index + 1) + " — " + name + " : elle seule, sur toute la frise"
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
            var types = new List<TimelineEventType>(_item.Timeline.Types);
            // « Naissance » et « Décès » : les dates liées sur les fiches (lot 2).
            types.Add(TimelineInfo.BuiltInType(TimelineInfo.BirthTypeId));
            types.Add(TimelineInfo.BuiltInType(TimelineInfo.DeathTypeId));
            foreach (var type in types)
            {
                var captured = type;
                var count = 0;
                foreach (var entry in _events) if (entry.TypeId == type.Id) count++;
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                content.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(ColorOf(type)), VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new TextBlock { Text = type.Name + "  " + count, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
                var builtIn = TimelineInfo.BuiltInType(type.Id) != null;
                var chip = new ToggleButton
                {
                    Classes = { Marabook.App.Theme.Owned },
                    Content = content,
                    IsChecked = !_hidden.Contains(type.Id),
                    Padding = new Thickness(8, 3, 9, 3),
                    Margin = new Thickness(0, 0, 6, 6),
                    Tag = "timeline-filter",
                    [ToolTip.TipProperty] = builtIn
                        ? "Montrer ou masquer les « " + type.Name + " » : les dates de " + (type.Id == TimelineInfo.BirthTypeId ? "naissance" : "décès") + " liées à cette frise sur les fiches"
                        : "Montrer ou masquer les événements « " + type.Name + " »"
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
                var before = _item.Timeline.Clone();
                if (await TimelineTypesDialog.Edit(Ui.OwnerOf(this), _project, _item.Timeline)) Commit(before);
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
            if (entry.Start == null || entry.IsPending) return false;
            if (entry.TypeId != null && _hidden.Contains(entry.TypeId)) return false;
            if (!SegmentOnly) return true;
            var segment = _stats.Segments[_tab];
            return Timelines.StartOf(_system, entry) < segment.Start + segment.Length && Timelines.EndOf(_system, entry) > segment.Start;
        }

        // ================================================== l'étendue, le zoom

        private void ComputeRange()
        {
            if (SegmentOnly)
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

        private double Span { get { return Math.Max(1e-6, _rangeMax - _rangeMin); } }

        /// <summary>Le zoom le plus large : l'onglet d'une ère la tient juste dans
        /// la page (rien d'autre ne paraît) ; « Tout » peut reculer un peu.</summary>
        private double MinScale
        {
            get
            {
                var width = _surface.Bounds.Width;
                if (SegmentOnly) return Math.Max(1e-9, Math.Max(40, width - 2 * EdgePad) / Span);
                return Math.Max(1e-9, Math.Max(40, width - SidePad - LabelPad) / Span * 0.6);
            }
        }

        private double MaxScale { get { return Math.Max(MinScale * 2, _system.BaseDays * 48.0); } }

        public void FitAll()
        {
            var width = _surface.Bounds.Width;
            if (width <= 0) { _fitPending = true; return; }
            _fitPending = false;
            if (SegmentOnly)
            {
                _scale = Math.Max(40, width - 2 * EdgePad) / Span;
                _viewStart = _rangeMin - EdgePad / _scale;
            }
            else
            {
                // À droite, la place du libellé des dernières briques.
                _scale = Math.Max(40, width - SidePad - LabelPad) / Span;
                _viewStart = _rangeMin - SidePad / _scale;
            }
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
            double min, max;
            if (SegmentOnly)
            {
                // L'ère seule : la vue ne sort jamais de ses bornes.
                min = _rangeMin - EdgePad / _scale;
                max = Math.Max(min, _rangeMax + EdgePad / _scale - span);
            }
            else
            {
                min = _rangeMin - span * 0.6;
                max = _rangeMax - span * 0.4;
                if (max < min) max = min;
            }
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
            if (_calendarMode) { _calendarPlaced = true; _calendar.Show(point); return; }
            var axis = _system.AxisMid(point);
            if (SegmentOnly)
            {
                var segment = _stats.Segments[_tab];
                if (axis < segment.Start || axis >= segment.Start + segment.Length) { _tab = -1; RebuildTabs(); ComputeRange(); RefreshInspector(); }
            }
            if (!SegmentOnly)
            {
                if (axis < _rangeMin) _rangeMin = Math.Floor(axis) - 1;
                if (axis > _rangeMax) _rangeMax = Math.Ceiling(axis) + 1;
            }
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
            var entry = _events.Find(delegate(TimelineEvent e) { return e.Id == eventId; });
            if (entry == null) return;
            if (entry.TypeId != null && _hidden.Remove(entry.TypeId)) RebuildFilters();
            _selected = entry;
            if (SegmentOnly && !Shown(entry)) { _tab = -1; RebuildTabs(); ComputeRange(); }
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
            var before = _item.Timeline.Clone();
            _item.Timeline.Events.Add(result.Event);
            _selected = result.Event;
            if (result.Event.TypeId != null) _hidden.Remove(result.Event.TypeId);
            Commit(before);
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
            // Une naissance, un décès : la date vit sur la fiche.
            if (entry.Derived) { OpenAssigned(entry); return; }
            var result = await TimelineEventDialog.Ask(Ui.OwnerOf(this), _project, _system, _item.Timeline, entry.Clone(), false);
            if (result == null || _item == null) return;
            var index = _item.Timeline.Events.FindIndex(delegate(TimelineEvent e) { return e.Id == entry.Id; });
            if (index < 0) return;
            var before = _item.Timeline.Clone();
            if (result.Deleted)
            {
                _item.Timeline.Events.RemoveAt(index);
                if (_selected != null && _selected.Id == entry.Id) _selected = null;
            }
            else
            {
                _item.Timeline.Events[index] = result.Event;
                _selected = result.Event;
            }
            Commit(before);
        }

        private void DeleteEvent(TimelineEvent entry)
        {
            if (_item == null || entry == null || entry.Derived) return;
            var index = _item.Timeline.Events.FindIndex(delegate(TimelineEvent e) { return e.Id == entry.Id; });
            if (index < 0) return;
            // Sans confirmation depuis le lot 2 : Ctrl+Z rend la brique.
            var before = _item.Timeline.Clone();
            _item.Timeline.Events.RemoveAt(index);
            if (_selected != null && _selected.Id == entry.Id) _selected = null;
            Commit(before);
        }

        private void DuplicateEvent(TimelineEvent entry)
        {
            if (_item == null || entry == null || entry.Derived) return;
            var before = _item.Timeline.Clone();
            var copy = entry.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Title = entry.Title + " (copie)";
            _item.Timeline.Events.Add(copy);
            _selected = copy;
            Commit(before);
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
                var target = _project == null ? null : _project.FindById(entry.LinkId);
                if (!entry.Derived)
                {
                    var edit = new MenuItem { Header = "Modifier…" };
                    edit.Click += delegate { EditEvent(entry); };
                    menu.Items.Add(edit);
                }
                if (target != null)
                {
                    var open = new MenuItem { Header = "Ouvrir « " + target.Title + " »" };
                    open.Click += delegate { OpenAssigned(entry); };
                    menu.Items.Add(open);
                }
                AddCalculator(menu, entry.Start);
                if (entry.Derived) return menu;
                var copy = new MenuItem { Header = "Dupliquer" };
                copy.Click += delegate { DuplicateEvent(entry); };
                menu.Items.Add(copy);
                menu.Items.Add(new Separator());
                var remove = new MenuItem { Header = "Retirer de la frise" };
                remove.Click += delegate { DeleteEvent(entry); };
                menu.Items.Add(remove);
                return menu;
            }
            var create = new MenuItem { Header = "Nouvel événement ici (" + _system.Format(at) + ")…" };
            create.Click += delegate { NewEvent(at); };
            menu.Items.Add(create);
            AddCalculator(menu, at);
            if (_calendarMode) return menu;
            var fit = new MenuItem { Header = "Tout voir" };
            fit.Click += delegate { FitAll(); };
            menu.Items.Add(fit);
            return menu;
        }

        /// <summary>« Calculateur de dates » depuis un point de la frise (14/11, Rémi) : il s'ouvre à cette date.</summary>
        private void AddCalculator(ContextMenu menu, TimePoint at)
        {
            var item = new MenuItem { Header = "Calculateur de dates…", Tag = "timeline-menu-calculator" };
            item.Click += delegate { DateCalculatorDialog.Open(TopLevel.GetTopLevel(this) as Window, _system, at); };
            menu.Items.Add(item);
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

        /// <summary>Une ligne cliquable de « Problèmes ».</summary>
        private static Border ProblemRow(string title, string detail, string tip, Action click)
        {
            var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            text.Inlines.Add(new Run(title) { FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
            text.Inlines.Add(new Run("\n" + detail) { Foreground = Chrome.SoftText, FontSize = 11 });
            var row = new Border
            {
                Child = text, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 2, 0, 2), CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Chrome.Danger.Color, 0.10), BorderBrush = new SolidColorBrush(Chrome.Danger.Color, 0.55), BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand), Tag = "timeline-problem",
                [ToolTip.TipProperty] = tip
            };
            row.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton == MouseButton.Left) click();
            };
            return row;
        }

        /// <summary>Le nombre d'événements, la durée totale de la temporalité,
        /// celle de l'ère de l'onglet — les problèmes — puis la brique choisie.</summary>
        private void RefreshInspector()
        {
            _inspector.Children.Clear();
            if (_item == null) return;
            var shown = 0;
            foreach (var entry in _events) if (Shown(entry)) shown++;
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
            if (_system.HasSegments)
            {
                // Les années DANS les segments seulement : rien d'avant l'origine (14/11, Rémi).
                var inSegments = Line("Années dans les segments :", TimeSystem.YearsLabel(_stats.SegmentYears));
                inSegments.Tag = "timeline-stat-segment-years";
                _inspector.Children.Add(inSegments);
            }
            _inspector.Children.Add(Line("Système :", _system.Name));
            _inspector.Children.Add(Line("Notation :", _system.Example()));
            if (_system.HasSegments)
            {
                if (SegmentOnly)
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

            // — Problèmes : ce que la temporalité ne sait plus placer.
            if (_pending.Count > 0 || _lifeProblems.Count > 0)
            {
                var problems = Caption("PROBLÈMES");
                problems.Foreground = Chrome.Danger;
                problems.Tag = "timeline-problems";
                _inspector.Children.Add(problems);
                if (_pending.Count > 0)
                {
                    _inspector.Children.Add(new TextBlock
                    {
                        Text = "Événements en attente (" + _pending.Count + ")", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, Margin = new Thickness(0, 0, 0, 2),
                        Tag = "timeline-pending"
                    });
                    _inspector.Children.Add(new TextBlock
                    {
                        Text = "Leur date ne se lit plus dans la temporalité. Cliquez pour en donner une nouvelle.",
                        FontSize = 11, Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3)
                    });
                    foreach (var entry in _pending)
                    {
                        var captured = entry;
                        var was = entry.PendingStart + (entry.PendingEnd != null ? " → " + entry.PendingEnd : "");
                        _inspector.Children.Add(ProblemRow(string.IsNullOrEmpty(entry.Title) ? "(sans titre)" : entry.Title, "était daté : " + was,
                            "Rouvrir cet événement pour lui donner une nouvelle date", delegate { EditEvent(captured); }));
                    }
                }
                if (_lifeProblems.Count > 0)
                {
                    _inspector.Children.Add(new TextBlock
                    {
                        Text = "Dates de fiche illisibles (" + _lifeProblems.Count + ")", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, Margin = new Thickness(0, 6, 0, 2)
                    });
                    foreach (var problem in _lifeProblems)
                    {
                        var sheet = problem.Sheet;
                        _inspector.Children.Add(ProblemRow(sheet.Title, problem.Field + " : « " + problem.Notation + " »",
                            "Ouvrir la fiche pour corriger sa date", delegate { var h = NavigateRequested; if (h != null) h(sheet); }));
                    }
                }
            }

            // — La brique choisie, dans son cadre.
            if (_selected != null)
            {
                var entry = _selected;
                var type = _item.Timeline.FindType(entry.TypeId);
                _inspector.Children.Add(Caption("ÉVÉNEMENT CHOISI"));
                var card = new StackPanel();
                card.Children.Add(new TextBlock { Text = entry.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, Tag = "timeline-selected-title" });
                card.Children.Add(new TextBlock { Text = Timelines.WhenLabel(_system, entry), FontSize = 12, Foreground = Chrome.Time, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
                var weekDay = entry.IsSpan ? null : _system.WeekDayOf(entry.Start);
                if (weekDay != null) card.Children.Add(Line("Jour :", weekDay));
                if (type != null) card.Children.Add(Line("Type :", type.Name));
                var target = _project == null ? null : _project.FindById(entry.LinkId);
                if (target != null)
                {
                    var open = Buttons.IconText("arrow-up-right-bold", TimelineLinks.Label(entry.LinkKind) + " : " + target.Title, "Ouvrir l'élément assigné à cet événement", Buttons.Compact, Buttons.Look.Outline);
                    open.HorizontalAlignment = HorizontalAlignment.Left;
                    open.Margin = new Thickness(0, 4, 0, 4);
                    open.Click += delegate { OpenAssigned(entry); };
                    card.Children.Add(open);
                }
                if (!string.IsNullOrEmpty(entry.Description))
                    card.Children.Add(new TextBlock { Text = entry.Description, FontSize = 12, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
                if (entry.Derived)
                    card.Children.Add(new TextBlock
                    {
                        Text = "Né de la fiche : la date se change dans son champ « " + (entry.TypeId == TimelineInfo.BirthTypeId ? SheetDefaults.FieldBirthDate : SheetDefaults.FieldDeathDate) + " ».",
                        FontSize = 11, Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
                    });
                else
                {
                    var edit = Buttons.Text("Modifier…", "Modifier cet événement", Buttons.Compact, Buttons.Look.Outline);
                    edit.HorizontalAlignment = HorizontalAlignment.Left;
                    edit.Margin = new Thickness(0, 4, 0, 0);
                    edit.Click += delegate { EditEvent(entry); };
                    card.Children.Add(edit);
                }
                _inspector.Children.Add(new Border
                {
                    Child = card, Padding = new Thickness(10, 8, 10, 10), CornerRadius = new CornerRadius(10),
                    BorderBrush = new SolidColorBrush(ColorOf(type)), BorderThickness = new Thickness(1.5),
                    Background = new SolidColorBrush(ColorOf(type), 0.07), Tag = "timeline-selected-card"
                });
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
                return TimeSystem.Count(_stats.Events, "événement", "événements") + " · " + TimeSystem.YearsLabel(_stats.TotalYears)
                    + (_pending.Count > 0 ? " · " + _pending.Count + " en attente" : "");
            }
        }

        // ================================================== sonde

        internal int TabForProbe { get { return _tab; } }
        internal string TitleForProbe { get { return _title.Text; } }
        internal int TabCountForProbe { get { return _tabs.Children.Count; } }
        internal void SelectTabForProbe(int index) { SelectTab(index); }
        internal double ScaleForProbe { get { return _scale; } }
        internal void ZoomForProbe(double factor) { ZoomBy(factor, _surface.Bounds.Width / 2); }
        internal void ToggleTypeForProbe(string typeId) { if (!_hidden.Remove(typeId)) _hidden.Add(typeId); Redraw(); RefreshInspector(); }
        internal int PlacedCountForProbe { get { return _surface.PlacedCount; } }
        internal bool MinimapShownForProbe { get { return _minimap.IsVisible && _minimap.Bounds.Width > 0; } }
        internal double ViewStartForProbe { get { return _viewStart; } }
        internal double ViewEndForProbe { get { return _viewStart + _surface.Bounds.Width / Math.Max(1e-9, _scale); } }
        internal void MinimapClickForProbe(double fraction) { _minimap.MoveTo(fraction * _minimap.Bounds.Width); }
        internal List<string> RulerLabelsForProbe() { return _surface.RulerLabels(); }
        internal int PendingCountForProbe { get { return _pending.Count; } }
        internal TimelineCalendar CalendarForProbe { get { return _calendar; } }
        internal bool SurfaceShownForProbe { get { return _surface.IsVisible && _minimap.IsVisible; } }
        /// <summary>Sonde : le curseur posé sur une année de l'axe (null : il sort).</summary>
        internal void PointAtForProbe(double? axis) { _surface.PointForProbe(axis == null ? (Point?)null : new Point(_surface.XOfForProbe(axis.Value), 120)); }
        internal string PointedLabelForProbe { get { return _surface.PointedLabel(); } }
        internal List<string> PointedEventsForProbe() { return _surface.PointedEvents(); }
        internal ContextMenu MenuForProbe(TimePoint at) { return MenuFor(null, at); }
        internal bool SurfaceHasTipForProbe { get { return ToolTip.GetTip(_surface) != null; } }
        internal void SelectForProbe(string eventId) { SelectEvent(eventId); }
        internal void DeleteSelectedForProbe() { if (_selected != null) DeleteEvent(_selected); }
        /// <summary>Le couloir de chaque brique posée, par id — de gauche à droite.</summary>
        internal List<KeyValuePair<string, int>> LanesForProbe() { return _surface.Lanes(); }
        internal bool InspectorHasTagForProbe(string tag)
        {
            foreach (var child in _inspector.Children)
                if (child is Control && Equals(((Control)child).Tag, tag)) return true;
            return false;
        }
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
            public FormattedText Label; // null : la marque seule
            public bool LabelInside, LabelLeft;
            public Color Color;
            public double LifeTo = double.NaN; // une naissance : l'abscisse du décès, sur la même ligne
        }

        /// <summary>Les graduations de ce qu'on voit : les jours, les mois ou des
        /// années par pas « ronds » — ronds dans la NOTATION (E2.100, E2.200),
        /// donc recalés à chaque segment qui repart de zéro. Dans l'onglet
        /// d'une ère, celles de cette ère seulement.</summary>
        private List<Tick> Ticks(double viewEnd)
        {
            var ticks = new List<Tick>();
            var system = _system;
            var perDay = _scale / system.BaseDays;
            var perMonth = _scale / Math.Max(1, system.Months.Count);
            var firstYear = (long)Math.Floor(_viewStart);
            var lastYear = (long)Math.Floor(viewEnd);
            if (SegmentOnly)
            {
                firstYear = Math.Max(firstYear, (long)_rangeMin);
                lastYear = Math.Min(lastYear, (long)_rangeMax - 1);
            }
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
            if (_viewStart < 0 && !SegmentOnly) AddYearTicks(ticks, Math.Max(firstYear, long.MinValue / 4), Math.Min(-1, lastYear), 0, step, false);
            if (system.HasSegments)
            {
                for (var i = 0; i < _stats.Segments.Count; i++)
                {
                    if (SegmentOnly && i != _tab) continue;
                    var start = _stats.Segments[i].Start;
                    var end = i == _stats.Segments.Count - 1 && !SegmentOnly ? long.MaxValue / 4 : start + _stats.Segments[i].Length - 1;
                    var from = Math.Max(start, firstYear);
                    var to = Math.Min(end, lastYear);
                    if (from > to) continue;
                    AddYearTicks(ticks, from, to, system.Cumulative ? -first : start - system.FirstYearOf(i), step, true);
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
            // LE REPÈRE DU SURVOL (14/11, Rémi) : sous le curseur, l'année pointée ;
            // un trait vertical à cet endroit ; les briques de cette année s'éclairent.
            private Point? _cursor;

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

            private FormattedText EventLabel(string title)
            {
                var label = Text(title, 12, Chrome.Ink, FontWeight.Medium);
                label.MaxLineCount = 1;
                label.Trimming = TextTrimming.CharacterEllipsis;
                label.MaxTextWidth = 240;
                return label;
            }

            private double X(double axis) { return (axis - _owner._viewStart) * _owner._scale; }
            private double AxisAt(double x) { return _owner._viewStart + x / _owner._scale; }
            private double Top { get { return RulerH + (_owner._system.HasSegments ? BandH : 0) + 10; } }

            public List<string> RulerLabels()
            {
                var labels = new List<string>();
                if (_owner._item == null || Bounds.Width <= 0) return labels;
                foreach (var tick in _owner.Ticks(AxisAt(Bounds.Width))) labels.Add(tick.Label);
                return labels;
            }

            public List<KeyValuePair<string, int>> Lanes()
            {
                var lanes = new List<KeyValuePair<string, int>>();
                var sorted = new List<Placed>(_placed);
                sorted.Sort(delegate(Placed a, Placed b) { return a.X1.CompareTo(b.X1); });
                foreach (var placed in sorted) lanes.Add(new KeyValuePair<string, int>(placed.Event.Id, placed.Lane));
                return lanes;
            }

            /// <summary>Un groupe posé d'un bloc sur un couloir : une brique, ou la
            /// naissance et le décès d'une même fiche.</summary>
            private sealed class Unit
            {
                public List<Placed> Members = new List<Placed>();
                public double Left = double.PositiveInfinity, Right = double.NegativeInfinity;
            }

            /// <summary>Range les briques visibles en couloirs. Lot 2 (Rémi) : EN
            /// ESCALIER — tant que des briques se gênent, chacune descend d'un
            /// cran sous la précédente, de la plus ancienne à la plus récente ;
            /// dès que l'une a toute la place, elle reprend en haut. Le décès
            /// d'une fiche se pose sur la ligne de sa naissance.</summary>
            public void Relayout()
            {
                _placed.Clear();
                MaxScroll = 0;
                var owner = _owner;
                if (owner._item == null || owner._item.Timeline == null || Bounds.Width <= 0) return;
                var events = new List<TimelineEvent>();
                foreach (var entry in owner._events) if (owner.Shown(entry)) events.Add(entry);
                var system = owner._system;
                events.Sort(delegate(TimelineEvent a, TimelineEvent b)
                {
                    var order = Timelines.StartOf(system, a).CompareTo(Timelines.StartOf(system, b));
                    return order != 0 ? order : string.Compare(a.Title, b.Title, StringComparison.CurrentCulture);
                });
                // Les décès qui ont leur naissance à l'écran s'y accrochent.
                var births = new Dictionary<string, TimelineEvent>(StringComparer.Ordinal);
                var deaths = new Dictionary<string, TimelineEvent>(StringComparer.Ordinal);
                foreach (var entry in events)
                {
                    if (!entry.Derived || entry.LinkId == null) continue;
                    if (entry.TypeId == TimelineInfo.BirthTypeId) births[entry.LinkId] = entry;
                    else if (entry.TypeId == TimelineInfo.DeathTypeId) deaths[entry.LinkId] = entry;
                }
                // En onglet d'ère, un libellé qui dépasserait la fin de l'ère passe à gauche de sa marque.
                var edge = owner.SegmentOnly ? X(owner._rangeMax) : double.PositiveInfinity;
                var units = new List<Unit>();
                foreach (var entry in events)
                {
                    var paired = entry.Derived && entry.LinkId != null && births.ContainsKey(entry.LinkId) && deaths.ContainsKey(entry.LinkId);
                    if (paired && entry.TypeId == TimelineInfo.DeathTypeId) continue; // posé avec sa naissance
                    var unit = new Unit();
                    if (paired)
                    {
                        var death = deaths[entry.LinkId];
                        var sheet = owner._project == null ? null : owner._project.FindById(entry.LinkId);
                        var name = sheet == null ? entry.Title : sheet.Title;
                        var xb = X(Timelines.StartOf(system, entry));
                        var xd = X(Timelines.StartOf(system, death));
                        var type = owner._item.Timeline.FindType(entry.TypeId);
                        var birth = new Placed { Event = entry, X1 = xb, X2 = xb, Color = ColorOf(type), LifeTo = xd };
                        var nameLabel = EventLabel(name);
                        // Le nom après la naissance s'il tient avant le décès ; toujours après le décès.
                        if (xd - xb > nameLabel.Width + 34) birth.Label = nameLabel;
                        Extent(birth, edge);
                        var end = new Placed { Event = death, X1 = xd, X2 = xd, Color = ColorOf(owner._item.Timeline.FindType(death.TypeId)), Label = EventLabel(name + "  †") };
                        Extent(end, edge);
                        unit.Members.Add(birth);
                        unit.Members.Add(end);
                    }
                    else
                    {
                        var type = owner._item.Timeline.FindType(entry.TypeId);
                        var title = string.IsNullOrEmpty(entry.Title) ? "(sans titre)" : entry.Title;
                        if (!entry.Derived && entry.LinkId != null && owner._project != null && owner._project.FindById(entry.LinkId) != null) title += "  ↗";
                        var placed = new Placed
                        {
                            Event = entry, X1 = X(Timelines.StartOf(system, entry)), X2 = X(Timelines.EndOf(system, entry)),
                            Label = EventLabel(title), Color = ColorOf(type)
                        };
                        Extent(placed, edge);
                        unit.Members.Add(placed);
                    }
                    foreach (var member in unit.Members)
                    {
                        unit.Left = Math.Min(unit.Left, member.Bounds.X);
                        unit.Right = Math.Max(unit.Right, member.Bounds.Right);
                    }
                    units.Add(unit);
                }

                var laneEnds = new List<double>();
                var maxLanes = Math.Max(3, (int)Math.Floor((Bounds.Height - Top - 12) / LaneH));
                var lastLane = -1;
                var clusterRight = double.NegativeInfinity;
                const double gap = 10;
                foreach (var unit in units)
                {
                    int lane;
                    if (unit.Left > clusterRight + gap) lane = 0; // plus rien ne gêne : on reprend en haut
                    else
                    {
                        lane = lastLane + 1;
                        if (lane >= maxLanes)
                        {
                            // Le bas de la page : le premier couloir libre depuis le haut, sinon on continue de descendre.
                            var free = -1;
                            for (var i = 0; i < laneEnds.Count; i++) if (laneEnds[i] <= unit.Left - gap) { free = i; break; }
                            if (free >= 0) lane = free;
                        }
                        while (lane < laneEnds.Count && laneEnds[lane] > unit.Left - gap) lane++;
                    }
                    while (laneEnds.Count <= lane) laneEnds.Add(double.NegativeInfinity);
                    laneEnds[lane] = unit.Right;
                    lastLane = lane;
                    clusterRight = Math.Max(clusterRight, unit.Right);
                    foreach (var member in unit.Members) { member.Lane = lane; _placed.Add(member); }
                }
                var needed = Top + laneEnds.Count * LaneH + 12;
                MaxScroll = Math.Max(0, needed - Bounds.Height);
                if (owner._scrollY > MaxScroll) owner._scrollY = MaxScroll;
            }

            /// <summary>La place d'une brique sur son couloir : sa marque et son libellé.</summary>
            private static void Extent(Placed placed, double edge)
            {
                var labelWidth = placed.Label == null ? 0 : placed.Label.Width;
                double left, right;
                if (placed.Event.IsSpan)
                {
                    var barRight = Math.Max(placed.X2, placed.X1 + 8);
                    placed.X2 = barRight;
                    placed.LabelInside = barRight - placed.X1 >= labelWidth + 16;
                    left = placed.X1;
                    right = barRight;
                    if (!placed.LabelInside)
                    {
                        if (barRight + 8 + labelWidth > edge) { placed.LabelLeft = true; left = placed.X1 - 8 - labelWidth; }
                        else right = barRight + 8 + labelWidth;
                    }
                }
                else
                {
                    left = placed.X1 - 8;
                    right = placed.X1 + 8;
                    if (placed.Label != null)
                    {
                        if (placed.X1 + 12 + labelWidth > edge) { placed.LabelLeft = true; left = placed.X1 - 12 - labelWidth; }
                        else right = placed.X1 + 12 + labelWidth;
                    }
                }
                placed.Bounds = new Rect(left, 0, Math.Max(8, right - left), LaneH - 6); // l'ordonnée se pose au dessin
            }

            private double LaneTop(int lane) { return Top + lane * LaneH - _owner._scrollY; }

            public override void Render(DrawingContext context)
            {
                var owner = _owner;
                var width = Bounds.Width;
                var height = Bounds.Height;
                context.DrawRectangle(Chrome.CanvasBg, new Pen(Chrome.Border, 1), new Rect(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1)), 8, 8);
                if (owner._item == null || width <= 0 || height <= 0) return;
                var system = owner._system;
                var viewEnd = AxisAt(width);
                var bandH = system.HasSegments ? BandH : 0;

                // — Avant l'origine : un voile (jamais dans l'onglet d'une ère).
                if (owner._viewStart < 0 && !owner.SegmentOnly)
                    context.DrawRectangle(new SolidColorBrush(Chrome.Border.Color, 0.28), null, new Rect(0, RulerH, Math.Min(width, X(0)), height - RulerH));

                // — Les segments : une bande teintée et son nom. L'onglet d'une ère ne montre qu'elle.
                if (system.HasSegments)
                    for (var i = 0; i < owner._stats.Segments.Count; i++)
                    {
                        if (owner.SegmentOnly && i != owner._tab) continue;
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
                        if (!owner.SegmentOnly && x1 >= 0 && x1 <= width) context.DrawLine(new Pen(new SolidColorBrush(tint, 0.7), 1.5), new Point(x1, RulerH), new Point(x1, height));
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
                    context.DrawLine(gridPen, new Point(x, RulerH + bandH), new Point(x, height));
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
                using (context.PushClip(new Rect(0, RulerH + bandH, width, Math.Max(0, height - RulerH - bandH))))
                {
                    for (var i = 0; i < _placed.Count; i++)
                    {
                        var placed = _placed[i];
                        var top = LaneTop(placed.Lane);
                        placed.Bounds = new Rect(placed.Bounds.X, top, placed.Bounds.Width, LaneH - 6);
                        var reach = double.IsNaN(placed.LifeTo) ? placed.Bounds.Right : Math.Max(placed.Bounds.Right, placed.LifeTo);
                        if (top > height || top + LaneH < RulerH || reach < 0 || placed.Bounds.X > width) continue;
                        var selected = owner._selected != null && owner._selected.Id == placed.Event.Id;
                        var pointed = _cursor != null && TimeMath.TouchesYear(placed.Event, PointedYear());
                        var hovered = _hover == placed.Event || pointed;
                        var color = placed.Color;
                        var middle = top + (LaneH - 6) / 2;
                        if (placed.Event.IsSpan)
                        {
                            var bar = new Rect(placed.X1, top + 2, Math.Max(8, placed.X2 - placed.X1), LaneH - 10);
                            if (pointed) context.DrawRectangle(null, new Pen(new SolidColorBrush(color, 0.45), 3), bar.Inflate(2.5), 8, 8);
                            context.DrawRectangle(new SolidColorBrush(color, hovered ? 0.42 : 0.28), new Pen(selected ? (IBrush)Chrome.Ink : new SolidColorBrush(color), selected ? 2 : 1.3), bar, 6, 6);
                            if (placed.LabelInside)
                            {
                                var from = Math.Max(bar.X, 0) + 8; // le libellé reste lisible quand la barre déborde à gauche
                                if (from + placed.Label.Width > bar.Right - 6) from = bar.Right - 6 - placed.Label.Width;
                                context.DrawText(placed.Label, new Point(from, middle - placed.Label.Height / 2));
                            }
                            else if (placed.LabelLeft) context.DrawText(placed.Label, new Point(bar.X - 8 - placed.Label.Width, middle - placed.Label.Height / 2));
                            else context.DrawText(placed.Label, new Point(bar.Right + 8, middle - placed.Label.Height / 2));
                        }
                        else
                        {
                            // La vie d'une fiche : un trait de la naissance au décès, sur la même ligne.
                            if (!double.IsNaN(placed.LifeTo))
                                context.DrawLine(new Pen(new SolidColorBrush(color, 0.55), 2), new Point(placed.X1, middle), new Point(placed.LifeTo, middle));
                            context.DrawLine(new Pen(new SolidColorBrush(color, 0.45), 1), new Point(placed.X1, RulerH), new Point(placed.X1, middle));
                            if (pointed) context.DrawEllipse(new SolidColorBrush(color, 0.28), null, new Point(placed.X1, middle), 12, 12);
                            context.DrawEllipse(new SolidColorBrush(color), new Pen(selected ? (IBrush)Chrome.Ink : Chrome.CanvasBg, selected ? 2 : 1.5), new Point(placed.X1, middle), hovered ? 7 : 6, hovered ? 7 : 6);
                            if (placed.Label != null)
                            {
                                if (placed.LabelLeft) context.DrawText(placed.Label, new Point(placed.X1 - 12 - placed.Label.Width, middle - placed.Label.Height / 2));
                                else
                                {
                                    // Sur le trait d'une vie, le nom reçoit un fond pour rester lisible.
                                    if (!double.IsNaN(placed.LifeTo))
                                        context.DrawRectangle(Chrome.CanvasBg, null, new Rect(placed.X1 + 9, middle - placed.Label.Height / 2, placed.Label.Width + 6, placed.Label.Height));
                                    context.DrawText(placed.Label, new Point(placed.X1 + 12, middle - placed.Label.Height / 2));
                                }
                            }
                        }
                    }
                }

                // — L'époque active, si elle se lit dans ce système.
                TimeSystem epochSystem;
                BinderItem epochTimeline;
                var epoch = Epochs.Current(owner._project, out epochSystem, out epochTimeline);
                if (epoch != null && epochSystem.Id == system.Id) Marker(context, system.AxisMid(epoch), "Époque · " + system.Format(epoch), Chrome.Warn, width, height);
                if (owner._flash != null) Marker(context, system.AxisMid(owner._flash), system.Format(owner._flash), Chrome.Time, width, height);

                // — Le repère du survol : le trait, puis l'année sous le curseur (une étiquette sans flèche).
                if (_cursor != null && !_panning)
                {
                    var at = _cursor.Value;
                    var x = Math.Round(at.X) + 0.5;
                    context.DrawLine(new Pen(new SolidColorBrush(Chrome.Ink.Color, 0.55), 1), new Point(x, 0), new Point(x, height));
                    var caption = Text(PointedLabel(), 11.5, Chrome.PaperBg, FontWeight.SemiBold);
                    var boxWidth = caption.Width + 14;
                    var boxHeight = caption.Height + 8;
                    var left = at.X + 12;
                    if (left + boxWidth > width - 2) left = at.X - 12 - boxWidth; // au bord droit : de l'autre côté du trait
                    var top = at.Y + 20;
                    if (top + boxHeight > height - 2) top = at.Y - 12 - boxHeight;
                    context.DrawRectangle(Chrome.Ink, null, new Rect(Math.Max(2, left), Math.Max(2, top), boxWidth, boxHeight), 5, 5);
                    context.DrawText(caption, new Point(Math.Max(2, left) + 7, Math.Max(2, top) + 4));
                }
            }

            /// <summary>L'année absolue sous le curseur.</summary>
            private long PointedYear()
            {
                return _cursor == null ? long.MinValue : (long)Math.Floor(AxisAt(_cursor.Value.X));
            }

            /// <summary>Ce que dit l'étiquette : l'année pointée — le mois ou le jour quand le zoom les distingue.</summary>
            public string PointedLabel()
            {
                if (_cursor == null) return null;
                return _owner._system.Format(_owner._system.FromAxis(AxisAt(_cursor.Value.X), _owner.PrecisionForScale()));
            }

            public List<string> PointedEvents()
            {
                var ids = new List<string>();
                if (_cursor == null) return ids;
                foreach (var placed in _placed) if (TimeMath.TouchesYear(placed.Event, PointedYear())) ids.Add(placed.Event.Id);
                return ids;
            }

            public void PointForProbe(Point? at) { _cursor = at; InvalidateVisual(); }
            public double XOfForProbe(double axis) { return X(axis); }

            private void Marker(DrawingContext context, double axis, string caption, IBrush brush, double width, double height)
            {
                if (_owner.SegmentOnly && (axis < _owner._rangeMin || axis > _owner._rangeMax)) return;
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

            private void Select(TimelineEvent entry)
            {
                if (_owner._selected == entry) return;
                _owner._selected = entry;
                _owner.RefreshInspector();
                InvalidateVisual();
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
                    if (hit != null) Select(hit.Event);
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
                    Select(hit.Event);
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
                _cursor = _owner._item == null ? (Point?)null : point;
                InvalidateVisual(); // le repère suit le curseur
                // Le survol marque la brique (curseur, marque grossie) — sans infobulle (lot 2, Rémi).
                var hit = Hit(point);
                var over = hit == null ? null : hit.Event;
                if (over == _hover) return;
                _hover = over;
                Cursor = over != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
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
                if (!_moved && _owner._selected != null) Select(null);
            }

            protected override void OnPointerExited(PointerEventArgs e)
            {
                base.OnPointerExited(e);
                if (_hover == null && _cursor == null) return;
                _hover = null;
                _cursor = null;
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

            private double X(double axis) { return 6 + (axis - _owner._rangeMin) / _owner.Span * Math.Max(1, Bounds.Width - 12); }

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
                        if (owner.SegmentOnly && i != owner._tab) continue;
                        var segment = owner._stats.Segments[i];
                        var x1 = Math.Max(6, X(segment.Start));
                        var x2 = Math.Min(width - 6, X(segment.Start + segment.Length));
                        if (x2 <= x1) continue;
                        context.DrawRectangle(new SolidColorBrush(owner.SegmentColor(i), 0.16), null, new Rect(x1, 4, x2 - x1, height - 8));
                    }
                var row = 0;
                foreach (var entry in owner._events)
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
                var axis = _owner._rangeMin + (x - 6) / Math.Max(1, Bounds.Width - 12) * _owner.Span;
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

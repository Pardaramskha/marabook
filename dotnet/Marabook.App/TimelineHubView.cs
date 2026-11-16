using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LA PAGE DE L'ASTROLABE (1.2.0) : ce qu'ouvre la racine
    /// « Astrolabe » de la Pile.
    ///
    /// 16/11/2026 (Rémi) : la page ASSUME la dépendance entre ses trois objets,
    /// et la montre dans l'ordre où ils se créent —
    ///   1. le SYSTÈME TEMPOREL, la fondation : comment le temps se compte
    ///      (calendrier, segments, notation). Tout le reste s'y appuie : le
    ///      modifier relit en cascade les dates des frises et du monde ;
    ///   2. les FRISES, posées sur un système ;
    ///   3. les MONDES, facultatifs : le ciel (saisons, lunes, soleils) que
    ///      voient les calendriers qui y vivent.
    /// Sans système, « Nouvelle frise » et « Nouveau monde » sont bloqués et
    /// disent pourquoi. Un système porte la liste de ce qui dépend de lui, et
    /// ne se retire que lorsque plus rien n'en dépend.</summary>
    public class TimelineHubView : DockPanel
    {
        private readonly StackPanel _timelines, _systems, _worlds, _chain;
        private readonly Button _newTimeline, _newWorld, _newSystem, _ourSystem;
        private readonly Control _timelinesLock, _worldsLock;
        private readonly Border _stepSystems, _stepTimelines, _stepWorlds;
        private Project _project;

        public event Action<BinderItem> OpenRequested;
        public event Action NewTimelineRequested;
        public event Action NewSystemRequested;
        public event Action OurSystemRequested;               // le calendrier de notre monde, en un clic
        public event Action<TimeSystem> EditSystemRequested;
        public event Action<TimeSystem> RemoveSystemRequested;
        public event Action<TimeSystem> CalculatorRequested;  // le Calculateur de dates du rail
        public event Action<World> EditWorldRequested;        // null : un nouveau monde
        public event Action<World> RemoveWorldRequested;

        public TimelineHubView()
        {
            Background = Chrome.WindowBg;
            Focusable = true;
            var head = new Border
            {
                Background = Chrome.BarBgLight, BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8)
            };
            SetDock(head, Dock.Top);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Icons.Make("pile-frises", 20, Chrome.Accent));
            title.Children.Add(new TextBlock { Text = Project.TimelinesRootTitle, Foreground = Chrome.Ink, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = "Le temps de votre univers : comment il se compte, ce qui s'y passe, sous quel ciel.", Foreground = Chrome.SoftText, FontSize = 12.5, Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            head.Child = title;
            Children.Add(head);

            // LA CHAÎNE : les trois étapes, dans l'ordre, et qui dépend de qui.
            _chain = new StackPanel { Orientation = Orientation.Horizontal, Tag = "hub-chain" };
            var chainBox = new Border
            {
                Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _chain },
                Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 18)
            };

            _systems = new StackPanel { Spacing = 8 };
            _timelines = new StackPanel { Spacing = 8 };
            _worlds = new StackPanel { Spacing = 8 };

            // 1 — Les systèmes temporels.
            _newSystem = Buttons.IconText("plus-bold", "Nouveau système", "Un calendrier, ses segments (ères) et sa notation", Buttons.Compact, Buttons.Look.Outline);
            _newSystem.Tag = "hub-new-system";
            _newSystem.Click += delegate { var h = NewSystemRequested; if (h != null) h(); };
            _ourSystem = Buttons.Text("Calendrier de notre monde", "Ajouter tel quel le calendrier grégorien : douze mois, années bissextiles, semaine de sept jours", Buttons.Compact, Buttons.Look.Outline);
            _ourSystem.Tag = "hub-our-system";
            _ourSystem.Click += delegate { var h = OurSystemRequested; if (h != null) h(); };
            var systemButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            systemButtons.Children.Add(_newSystem);
            systemButtons.Children.Add(_ourSystem);
            var systemsColumn = new StackPanel();
            systemsColumn.Children.Add(Heading("1", "Systèmes temporels", "La fondation : comment le temps se compte. Frises et mondes s'y appuient.", out _stepSystems));
            systemsColumn.Children.Add(_systems);
            systemsColumn.Children.Add(systemButtons);

            // 3 — Les mondes (sous les systèmes : ils leur donnent un ciel).
            _newWorld = Buttons.IconText("plus-bold", "Nouveau monde", "Un monde : sa taille, ses saisons, ses soleils, ses lunes", Buttons.Compact, Buttons.Look.Outline);
            _newWorld.HorizontalAlignment = HorizontalAlignment.Left;
            _newWorld.Margin = new Thickness(0, 8, 0, 0);
            _newWorld.Tag = "hub-new-world";
            _newWorld.Click += delegate { var h = EditWorldRequested; if (h != null) h(null); };
            _worldsLock = LockLine("Créez d'abord un système temporel (étape 1) : les saisons et les lunes d'un monde se comptent dans un calendrier.");
            var worldsHeading = Heading("3", "Mondes", "Facultatif : le ciel que voient les calendriers — saisons, soleils, lunes, éclipses.", out _stepWorlds);
            worldsHeading.Margin = new Thickness(0, 26, 0, 10);
            systemsColumn.Children.Add(worldsHeading);
            systemsColumn.Children.Add(_worlds);
            systemsColumn.Children.Add(_worldsLock);
            systemsColumn.Children.Add(_newWorld);

            // 2 — Les frises.
            _newTimeline = Buttons.IconText("plus-bold", "Nouvelle frise", "Une frise chronologique posée sur un système temporel", Buttons.Compact, Buttons.Look.Primary);
            _newTimeline.Tag = "hub-new-timeline";
            _newTimeline.HorizontalAlignment = HorizontalAlignment.Left;
            _newTimeline.Margin = new Thickness(0, 8, 0, 0);
            _newTimeline.Click += delegate { var h = NewTimelineRequested; if (h != null) h(); };
            _timelinesLock = LockLine("Créez d'abord un système temporel (étape 1) : une frise écrit ses dates dans la notation d'un système.");
            var timelinesColumn = new StackPanel();
            timelinesColumn.Children.Add(Heading("2", "Frises", "Les chronologies : chaque frise pose ses événements sur un système temporel.", out _stepTimelines));
            timelinesColumn.Children.Add(_timelines);
            timelinesColumn.Children.Add(_timelinesLock);
            timelinesColumn.Children.Add(_newTimeline);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,28,3*") };
            Grid.SetColumn(timelinesColumn, 2);
            grid.Children.Add(systemsColumn);
            grid.Children.Add(timelinesColumn);
            var page = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            page.Children.Add(chainBox);
            page.Children.Add(grid);
            Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page });
        }

        /// <summary>L'en-tête d'une étape : sa pastille numérotée, son nom, ce qu'elle est en une ligne.</summary>
        private static Control Heading(string number, string title, string subtitle, out Border step)
        {
            step = AstrolabeUi.Step(number, false);
            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            text.Children.Add(new TextBlock { Text = title, Foreground = Chrome.Ink, FontSize = 15, FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = subtitle, Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            step.VerticalAlignment = VerticalAlignment.Top;
            step.Margin = new Thickness(0, 1, 0, 0);
            DockPanel.SetDock(step, Dock.Left);
            row.Children.Add(step);
            row.Children.Add(text);
            return row;
        }

        /// <summary>La ligne qui dit pourquoi une étape est bloquée (un cadenas, la raison).</summary>
        private static Control LockLine(string text, bool faint = false)
        {
            var tone = faint ? Chrome.FaintText : Chrome.SoftText;
            var icon = Icons.Make("lock", 13, tone);
            icon.Margin = new Thickness(0, 1, 7, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 0), IsVisible = false, Tag = "hub-lock" };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = text, Foreground = tone, FontSize = faint ? 11 : 12, TextWrapping = TextWrapping.Wrap });
            return row;
        }

        private static void SetStep(Border step, bool done)
        {
            step.Background = done ? Chrome.Accent : Brushes.Transparent;
            step.BorderBrush = done ? Chrome.Accent : Chrome.BorderStrong;
            ((TextBlock)step.Child).Foreground = done ? Brushes.White : Chrome.SoftText;
        }

        public void Load(Project project)
        {
            _project = project;
            Refresh();
        }

        public void Refresh()
        {
            _timelines.Children.Clear();
            _systems.Children.Clear();
            _worlds.Children.Clear();
            _chain.Children.Clear();
            if (_project == null) return;
            var all = Timelines.All(_project);
            var hasSystem = _project.TimeSystems.Count > 0;

            // La chaîne et les étapes : ce qui est fait, ce qui attend.
            SetStep(_stepSystems, hasSystem);
            SetStep(_stepTimelines, all.Count > 0);
            SetStep(_stepWorlds, _project.Worlds.Count > 0);
            ChainNode("1", "Système temporel", hasSystem ? TimeSystem.Count(_project.TimeSystems.Count, "système", "systèmes") : "à créer en premier", hasSystem, true);
            ChainArrow("porte");
            ChainNode("2", "Frises", all.Count > 0 ? TimeSystem.Count(all.Count, "frise", "frises") : hasSystem ? "aucune pour l'instant" : "bloqué sans système", all.Count > 0, hasSystem);
            ChainArrow("et, au choix");
            ChainNode("3", "Monde", _project.Worlds.Count > 0 ? TimeSystem.Count(_project.Worlds.Count, "monde", "mondes") : hasSystem ? "facultatif" : "bloqué sans système", _project.Worlds.Count > 0, hasSystem);
            _chain.Children.Add(new TextBlock
            {
                Text = "Le système temporel est le point de référence : le modifier relit toutes les dates qui s'y appuient.",
                Foreground = Chrome.SoftText, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0)
            });

            // Les actions bloquées tant que la fondation manque.
            _newTimeline.IsEnabled = hasSystem;
            _newWorld.IsEnabled = hasSystem;
            _timelinesLock.IsVisible = !hasSystem;
            _worldsLock.IsVisible = !hasSystem;
            _ourSystem.IsVisible = !_project.TimeSystems.Exists(delegate(TimeSystem s) { return s.Name == TimeSystem.GregorianName; });

            foreach (var system in _project.TimeSystems) _systems.Children.Add(SystemCard(system));
            if (!hasSystem)
                _systems.Children.Add(Empty("Aucun système temporel. Commencez ici : prenez le calendrier de notre monde tel quel, ou composez le vôtre — ses mois, ses ères, sa notation."));
            foreach (var timeline in all) _timelines.Children.Add(TimelineRow(timeline));
            if (all.Count == 0 && hasSystem)
                _timelines.Children.Add(Empty("Aucune frise pour l'instant. « Nouvelle frise » vous demandera sur quel système temporel la poser."));
            foreach (var world in _project.Worlds) _worlds.Children.Add(WorldCard(world));
            if (_project.Worlds.Count == 0 && hasSystem)
                _worlds.Children.Add(Empty("Aucun monde. Un monde donne aux calendriers qui y vivent leurs saisons, leurs lunes, leurs éclipses — et sa taille."));
        }

        private static Border Empty(string text)
        {
            return new Border
            {
                Padding = new Thickness(14, 11, 14, 12), CornerRadius = new CornerRadius(10), BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = "hub-empty",
                Child = new TextBlock { Text = text, Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 }
            };
        }

        private void ChainNode(string number, string title, string state, bool done, bool open)
        {
            var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, Foreground = open ? Chrome.Ink : Chrome.FaintText, FontSize = 12.5, FontWeight = FontWeight.SemiBold });
            text.Children.Add(new TextBlock { Text = state, Foreground = open ? Chrome.SoftText : Chrome.FaintText, FontSize = 11 });
            var node = new StackPanel { Orientation = Orientation.Horizontal, Tag = "hub-chain-node" };
            node.Children.Add(AstrolabeUi.Step(number, done));
            node.Children.Add(text);
            _chain.Children.Add(node);
        }

        private void ChainArrow(string verb)
        {
            var arrow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 16, 0) };
            arrow.Children.Add(new TextBlock { Text = verb, Foreground = Chrome.FaintText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            arrow.Children.Add(Icons.Make("arrow-right-bold", 12, Chrome.FaintText));
            _chain.Children.Add(arrow);
        }

        private Control TimelineRow(BinderItem timeline)
        {
            var system = Timelines.SystemOf(_project, timeline);
            var events = new List<TimelineEvent>();
            foreach (var entry in timeline.Timeline.Events) if (!entry.IsPending) events.Add(entry);
            events.AddRange(Timelines.LifeEventsOf(_project, timeline, system).Events); // les naissances et les décès nés des fiches
            var stats = Timelines.Stats(system, events);
            var body = new StackPanel();
            var top = new DockPanel();
            var meta = new TextBlock
            {
                Text = TimeSystem.Count(stats.Events, "événement", "événements") + "  ·  " + TimeSystem.YearsLabel(stats.TotalYears),
                Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(meta, Dock.Right);
            top.Children.Add(meta);
            var name = new StackPanel { Orientation = Orientation.Horizontal };
            name.Children.Add(Icons.Make("frise", 16, Chrome.Accent));
            name.Children.Add(new TextBlock { Text = timeline.Title, Foreground = Chrome.Ink, FontSize = 14, FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            top.Children.Add(name);
            body.Children.Add(top);
            // Sur quoi elle repose, dit comme une dépendance.
            body.Children.Add(new TextBlock { Text = "Posée sur « " + system.Name + " »  ·  " + TimeSystemDialog.Summary(system), Foreground = Chrome.SoftText, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 6) });
            body.Children.Add(new Strip(system, timeline.Timeline, events, stats) { Height = 26 });
            var row = new Border
            {
                Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10), Child = body, Cursor = new Cursor(StandardCursorType.Hand),
                Tag = "hub-timeline",
                [ToolTip.TipProperty] = "Ouvrir la frise « " + timeline.Title + " »"
            };
            row.PointerEntered += delegate { row.BorderBrush = Chrome.Accent; };
            row.PointerExited += delegate { row.BorderBrush = Chrome.Border; };
            row.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                var handler = OpenRequested;
                if (handler != null) handler(timeline);
            };
            return row;
        }

        /// <summary>Une ligne « ↳ … » : ce dont la tuile dépend, ou ce qui dépend d'elle.</summary>
        private static TextBlock Dependency(string text, bool faint = false)
        {
            return new TextBlock { Text = "↳  " + text, Foreground = faint ? Chrome.FaintText : Chrome.SoftText, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = "hub-dependency" };
        }

        private Control WorldCard(World world)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = world.Name, Foreground = Chrome.Ink, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(new TextBlock { Text = Astro.Summary(world), Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            var systems = Astro.SystemsOf(_project, world);
            var names = new List<string>();
            foreach (var system in systems) names.Add(system.Name);
            if (names.Count == 0)
            {
                var orphan = AstrolabeUi.Notice("Aucun calendrier n'y vit : son ciel ne s'affiche nulle part. « Modifier… » pour lui en donner un.", false, "hub-world-orphan");
                orphan.Margin = new Thickness(0, 8, 0, 0);
                body.Children.Add(orphan);
            }
            else body.Children.Add(Dependency("Donne son ciel à : " + string.Join(", ", names.ToArray())));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            var edit = Buttons.Text("Modifier…", "Taille du monde, saisons, soleils, lunes, phénomènes", Buttons.Compact, Buttons.Look.Outline);
            edit.Tag = "hub-world-edit";
            edit.Click += delegate { var h = EditWorldRequested; if (h != null) h(world); };
            buttons.Children.Add(edit);
            var remove = Buttons.Text("Retirer", null, Buttons.Compact, Buttons.Look.Calm);
            remove.Click += delegate { var h = RemoveWorldRequested; if (h != null) h(world); };
            buttons.Children.Add(remove);
            body.Children.Add(buttons);
            return new Border
            {
                Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10), Child = body, Tag = "hub-world"
            };
        }

        private Control SystemCard(TimeSystem system)
        {
            var uses = Timelines.UsesOf(_project, system);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = system.Name, Foreground = Chrome.Ink, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(new TextBlock { Text = TimeSystemDialog.Summary(system), Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            // CE QUI S'APPUIE SUR LUI : ses frises, son monde — ce qu'un changement relira.
            var carried = new List<string>();
            foreach (var timeline in Timelines.All(_project))
                if (timeline.Timeline != null && timeline.Timeline.SystemId == system.Id) carried.Add(timeline.Title);
            var home = Astro.WorldOf(_project, system);
            if (carried.Count > 0) body.Children.Add(Dependency((carried.Count > 1 ? "Porte les frises : " : "Porte la frise : ") + string.Join(", ", carried.ToArray())));
            else body.Children.Add(Dependency("Aucune frise ne s'y appuie", true));
            if (home != null) body.Children.Add(Dependency("Vit dans le monde : " + home.Name));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            var edit = Buttons.Text("Modifier…", uses > 0 || home != null ? "Segments, notation, calendrier — ses dates seront relues partout où il sert" : "Segments, notation, calendrier", Buttons.Compact, Buttons.Look.Outline);
            edit.Tag = "hub-system-edit";
            edit.Click += delegate { var h = EditSystemRequested; if (h != null) h(system); };
            buttons.Children.Add(edit);
            // Le calculateur de dates du système (14/11, Rémi).
            var calculator = Buttons.Text("Calculateur de dates", "Ajouter une durée à une date, compter le temps entre deux dates", Buttons.Compact, Buttons.Look.Outline);
            calculator.Tag = "hub-system-calculator";
            calculator.Click += delegate { var h = CalculatorRequested; if (h != null) h(system); };
            buttons.Children.Add(calculator);
            // « Retirer » reste visible : bloqué — et la raison dite — tant qu'une frise s'y appuie.
            var remove = Buttons.Text("Retirer", uses == 0 ? "Retirer ce système du projet (aucune frise ne s'y appuie)" : null, Buttons.Compact, Buttons.Look.Calm);
            remove.Tag = "hub-system-remove";
            remove.IsEnabled = uses == 0;
            remove.Click += delegate { var h = RemoveSystemRequested; if (h != null) h(system); };
            buttons.Children.Add(remove);
            body.Children.Add(buttons);
            if (uses > 0)
            {
                var locked = LockLine("Ne se retire pas tant que " + (uses > 1 ? uses + " frises s'y appuient" : "une frise s'y appuie") + ".", true);
                locked.IsVisible = true;
                locked.Margin = new Thickness(0, 6, 0, 0);
                locked.Tag = "hub-system-locked";
                body.Children.Add(locked);
            }
            return new Border
            {
                Background = Chrome.CardBg, BorderBrush = uses > 0 || home != null ? Chrome.AccentSoft : Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10), Child = body, Tag = "hub-system"
            };
        }

        internal int TimelineRowsForProbe { get { var n = 0; foreach (var child in _timelines.Children) if (Equals((child as Control).Tag, "hub-timeline")) n++; return n; } }
        internal int WorldCardsForProbe { get { var n = 0; foreach (var child in _worlds.Children) if (Equals((child as Control).Tag, "hub-world")) n++; return n; } }
        internal int SystemCardsForProbe { get { var n = 0; foreach (var child in _systems.Children) if (Equals((child as Control).Tag, "hub-system")) n++; return n; } }
        internal bool NewTimelineEnabledForProbe { get { return _newTimeline.IsEnabled; } }
        internal bool NewWorldEnabledForProbe { get { return _newWorld.IsEnabled; } }
        internal bool LocksShownForProbe { get { return _timelinesLock.IsVisible && _worldsLock.IsVisible; } }
        internal int ChainNodesForProbe { get { var n = 0; foreach (var child in _chain.Children) if (Equals((child as Control).Tag, "hub-chain-node")) n++; return n; } }
        internal bool OurSystemOfferedForProbe { get { return _ourSystem.IsVisible; } }
        internal void AddOurSystemForProbe() { var h = OurSystemRequested; if (h != null) h(); }

        /// <summary>La bande miniature d'une frise : ses segments, un trait par événement.</summary>
        private sealed class Strip : Control
        {
            private readonly TimeSystem _system;
            private readonly TimelineInfo _timeline;
            private readonly TimelineStats _stats;
            private readonly List<TimelineEvent> _events;

            public Strip(TimeSystem system, TimelineInfo timeline, List<TimelineEvent> events, TimelineStats stats)
            {
                _events = events;
                _system = system;
                _timeline = timeline;
                _stats = stats;
                IsHitTestVisible = false;
            }

            public override void Render(DrawingContext context)
            {
                var width = Bounds.Width;
                var height = Bounds.Height;
                context.DrawRectangle(Chrome.CanvasBg, new Pen(Chrome.Border, 1), new Rect(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1)), 5, 5);
                var span = Math.Max(1e-6, _stats.Max - _stats.Min);
                Func<double, double> x = delegate(double axis) { return 4 + (axis - _stats.Min) / span * Math.Max(1, width - 8); };
                var tints = new[] { Color.FromRgb(0x5B, 0x67, 0xD8), Color.FromRgb(0x3E, 0x9C, 0x7A), Color.FromRgb(0xB5, 0x84, 0x3A), Color.FromRgb(0x8E, 0x6B, 0xBF), Color.FromRgb(0xC0, 0x50, 0x4D), Color.FromRgb(0x0E, 0x7C, 0x7B) };
                for (var i = 0; i < _stats.Segments.Count; i++)
                {
                    var segment = _stats.Segments[i];
                    Color tint;
                    if (i >= _system.Segments.Count || !Color.TryParse(_system.Segments[i].Color, out tint)) tint = tints[i % tints.Length];
                    var x1 = x(segment.Start);
                    var x2 = x(segment.Start + segment.Length);
                    context.DrawRectangle(new SolidColorBrush(tint, 0.18), null, new Rect(x1, 3, Math.Max(0, x2 - x1), height - 6));
                }
                if (_timeline == null) return;
                var row = 0;
                foreach (var entry in _events)
                {
                    if (entry.Start == null) continue;
                    var color = TimelineView.ColorOf(_timeline.FindType(entry.TypeId));
                    var x1 = x(Timelines.StartOf(_system, entry));
                    var x2 = x(Timelines.EndOf(_system, entry));
                    var y = 5 + (row++ % 3) * ((height - 14) / 3);
                    context.DrawRectangle(new SolidColorBrush(color), null, new Rect(x1 - 1, y, Math.Max(3, x2 - x1 + 2), 4), 2, 2);
                }
            }
        }
    }
}

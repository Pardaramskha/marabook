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
    /// <summary>LA PAGE DES FRISES (1.2.0) : ce qu'ouvre la racine « Frises »
    /// de la Pile. Pas un tableau de tuiles : à gauche les frises, chacune en
    /// une ligne avec sa bande miniature (segments et événements) ; à droite
    /// les systèmes temporels du projet — segments, notation, calendrier —
    /// sur lesquels les frises se posent.</summary>
    public class TimelineHubView : DockPanel
    {
        private readonly StackPanel _timelines, _systems;
        private Project _project;

        public event Action<BinderItem> OpenRequested;
        public event Action NewTimelineRequested;
        public event Action NewSystemRequested;
        public event Action<TimeSystem> EditSystemRequested;
        public event Action<TimeSystem> RemoveSystemRequested;

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
            var row = new DockPanel();
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var newSystem = Buttons.IconText("plus-bold", "Nouveau système temporel", "Un calendrier, ses segments (ères) et sa notation", Buttons.Compact, Buttons.Look.Outline);
            newSystem.Click += delegate { var h = NewSystemRequested; if (h != null) h(); };
            var newTimeline = Buttons.IconText("plus-bold", "Nouvelle frise", "Une frise chronologique posée sur un système temporel", Buttons.Compact, Buttons.Look.Primary);
            newTimeline.Tag = "hub-new-timeline";
            newTimeline.Click += delegate { var h = NewTimelineRequested; if (h != null) h(); };
            right.Children.Add(newSystem);
            right.Children.Add(newTimeline);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Icons.Make("pile-frises", 20, Chrome.Accent));
            title.Children.Add(new TextBlock { Text = "Frises", Foreground = Chrome.Ink, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(title);
            head.Child = row;
            Children.Add(head);

            _timelines = new StackPanel { Spacing = 8 };
            _systems = new StackPanel { Spacing = 8 };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,24,2*"), Margin = new Thickness(20, 16, 20, 16) };
            var left = new StackPanel();
            left.Children.Add(Heading("FRISES"));
            left.Children.Add(_timelines);
            var side = new StackPanel();
            side.Children.Add(Heading("SYSTÈMES TEMPORELS"));
            side.Children.Add(_systems);
            Grid.SetColumn(side, 2);
            grid.Children.Add(left);
            grid.Children.Add(side);
            Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid });
        }

        private static TextBlock Heading(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
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
            if (_project == null) return;
            var all = Timelines.All(_project);
            foreach (var timeline in all) _timelines.Children.Add(TimelineRow(timeline));
            if (all.Count == 0)
                _timelines.Children.Add(new TextBlock
                {
                    Text = "Aucune frise pour l'instant.\n« Nouvelle frise » commence par le système de notation du temps : celui de notre monde, ou le vôtre — ères, calendrier, notation.",
                    Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 4, 0, 0)
                });
            foreach (var system in _project.TimeSystems) _systems.Children.Add(SystemCard(system));
            if (_project.TimeSystems.Count == 0)
                _systems.Children.Add(new TextBlock
                {
                    Text = "Aucun système : il se crée avec la première frise.",
                    Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, FontSize = 12
                });
        }

        private Control TimelineRow(BinderItem timeline)
        {
            var system = Timelines.SystemOf(_project, timeline);
            var stats = Timelines.Stats(system, timeline.Timeline);
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
            body.Children.Add(new TextBlock { Text = system.Name + "  ·  " + TimeSystemDialog.Summary(system), Foreground = Chrome.SoftText, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 6) });
            body.Children.Add(new Strip(system, timeline.Timeline, stats) { Height = 26 });
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

        private Control SystemCard(TimeSystem system)
        {
            var uses = Timelines.UsesOf(_project, system);
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = system.Name, Foreground = Chrome.Ink, FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(new TextBlock { Text = TimeSystemDialog.Summary(system), Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            body.Children.Add(new TextBlock
            {
                Text = uses == 0 ? "Aucune frise ne l'utilise" : uses + (uses > 1 ? " frises l'utilisent" : " frise l'utilise"),
                Foreground = Chrome.FaintText, FontSize = 11, Margin = new Thickness(0, 3, 0, 6)
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var edit = Buttons.Text("Modifier…", "Temporalité par segments, notation, calendrier", Buttons.Compact, Buttons.Look.Outline);
            edit.Tag = "hub-system-edit";
            edit.Click += delegate { var h = EditSystemRequested; if (h != null) h(system); };
            buttons.Children.Add(edit);
            if (uses == 0)
            {
                var remove = Buttons.Text("Retirer", "Retirer ce système du projet (aucune frise ne l'utilise)", Buttons.Compact, Buttons.Look.Calm);
                remove.Click += delegate { var h = RemoveSystemRequested; if (h != null) h(system); };
                buttons.Children.Add(remove);
            }
            body.Children.Add(buttons);
            return new Border
            {
                Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10), Child = body, Tag = "hub-system"
            };
        }

        internal int TimelineRowsForProbe { get { var n = 0; foreach (var child in _timelines.Children) if (child is Border) n++; return n; } }
        internal int SystemCardsForProbe { get { var n = 0; foreach (var child in _systems.Children) if (child is Border) n++; return n; } }

        /// <summary>La bande miniature d'une frise : ses segments, un trait par événement.</summary>
        private sealed class Strip : Control
        {
            private readonly TimeSystem _system;
            private readonly TimelineInfo _timeline;
            private readonly TimelineStats _stats;

            public Strip(TimeSystem system, TimelineInfo timeline, TimelineStats stats)
            {
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
                foreach (var entry in _timeline.Events)
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

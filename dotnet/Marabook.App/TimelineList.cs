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
    /// <summary>LA VUE LISTE d'une frise (20/11) : ses événements dans
    /// l'ordre du temps, un par ligne — date, type, titre, élément assigné —
    /// avec un champ de recherche. Elle ne possède rien : la frise lui donne
    /// les événements (filtres des types et de la fiche déjà passés) et
    /// reçoit le choix, l'édition, le menu.</summary>
    internal sealed class TimelineList : DockPanel
    {
        /// <summary>Au-delà, la liste demande d'affiner : mille lignes ne se lisent pas.</summary>
        public const int MaxRows = 400;

        public Func<List<TimelineEvent>> Events;
        public Func<List<TimelineEvent>, string, List<TimelineEvent>> Search;
        public Func<TimelineEvent, string> When;
        public Func<TimelineEvent, string> TypeName;
        public Func<TimelineEvent, Color> ColorOf;
        public Func<TimelineEvent, string> Linked;
        public Func<TimelineEvent> Selected;
        public Action<TimelineEvent> Select, Edit;
        public Func<TimelineEvent, TimePoint, ContextMenu> Menu;

        private readonly TextBox _search;
        private readonly TextBlock _count;
        private readonly StackPanel _rows;
        private readonly Dictionary<string, Border> _byId = new Dictionary<string, Border>(StringComparer.Ordinal);
        private List<TimelineEvent> _shown = new List<TimelineEvent>();

        public TimelineList()
        {
            Tag = "timeline-list";
            Margin = new Thickness(16, 8, 16, 8);
            _search = new TextBox { Watermark = "Chercher dans les événements : titre, description, type, fiche, date…", Tag = "timeline-list-search", MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 320 };
            _search.TextChanged += delegate { Refresh(); };
            _count = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), Tag = "timeline-list-count" };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(_search);
            head.Children.Add(_count);
            SetDock(head, Dock.Top);
            Children.Add(head);
            var titles = Row("Date", null, "Type", "Événement", "Assigné à", true);
            titles.Margin = new Thickness(0, 0, 0, 2);
            SetDock(titles, Dock.Top);
            Children.Add(titles);
            _rows = new StackPanel();
            Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _rows });
        }

        public string Query { get { return _search.Text ?? ""; } set { _search.Text = value ?? ""; } }

        /// <summary>Une ligne : la date, la pastille et le nom du type, le titre, l'élément assigné.</summary>
        private static Grid Row(string when, Color? tint, string type, string title, string linked, bool heading)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,1.2*,3*,1.6*") };
            var ink = heading ? Chrome.FaintText : Chrome.Ink;
            var size = heading ? 11.0 : 12.5;
            var weight = heading ? FontWeight.SemiBold : FontWeight.Normal;
            var date = new TextBlock { Text = heading ? when.ToUpperInvariant() : when, Foreground = heading ? ink : Chrome.SoftText, FontSize = size, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(date);
            var kind = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            if (tint.HasValue) kind.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(tint.Value), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            kind.Children.Add(new TextBlock { Text = heading ? type.ToUpperInvariant() : type, Foreground = heading ? ink : Chrome.SoftText, FontSize = size, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(kind, 1);
            grid.Children.Add(kind);
            var name = new TextBlock { Text = heading ? title.ToUpperInvariant() : title, Foreground = ink, FontSize = size, FontWeight = heading ? weight : FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 2);
            grid.Children.Add(name);
            var link = new TextBlock { Text = heading ? linked.ToUpperInvariant() : linked, Foreground = heading ? ink : Chrome.SoftText, FontSize = size, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(link, 3);
            grid.Children.Add(link);
            return grid;
        }

        /// <summary>Relit les événements et refait les lignes.</summary>
        public void Refresh()
        {
            _rows.Children.Clear();
            _byId.Clear();
            var all = Events == null ? new List<TimelineEvent>() : Events();
            _shown = Search == null ? all : Search(all, Query);
            _count.Text = _shown.Count == all.Count
                ? TimeSystem.Count(all.Count, "événement", "événements")
                : _shown.Count + " sur " + TimeSystem.Count(all.Count, "événement", "événements");
            if (_shown.Count == 0)
            {
                _rows.Children.Add(new TextBlock
                {
                    Text = all.Count == 0 ? "Aucun événement à lister : la frise est vide, ou les filtres écartent tout." : "Aucun événement ne répond à cette recherche.",
                    Foreground = Chrome.SoftText, FontSize = 13, Margin = new Thickness(8, 24, 8, 0), Tag = "timeline-list-empty"
                });
                return;
            }
            var chosen = Selected == null ? null : Selected();
            for (var i = 0; i < _shown.Count && i < MaxRows; i++)
            {
                var entry = _shown[i];
                var title = Links.Strip(entry.Title ?? "");
                var grid = Row(When == null ? "" : When(entry), ColorOf == null ? (Color?)null : ColorOf(entry), TypeName == null ? "" : TypeName(entry),
                    title.Length == 0 ? "(sans titre)" : title, Linked == null ? "" : Linked(entry) ?? "", false);
                var row = new Border
                {
                    Child = grid, Padding = new Thickness(0, 6, 0, 6), CornerRadius = new CornerRadius(4), Tag = "timeline-list-row",
                    Background = chosen != null && chosen.Id == entry.Id ? Chrome.AccentTint : Brushes.Transparent,
                    BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, 0, 0, 1), Cursor = new Cursor(StandardCursorType.Hand)
                };
                var description = Links.Strip(entry.Description ?? "").Trim();
                if (description.Length > 0) ToolTip.SetTip(row, description.Length > 400 ? description.Substring(0, 400) + "…" : description);
                var captured = entry;
                row.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                {
                    var point = e.GetCurrentPoint(row).Properties;
                    if (point.IsRightButtonPressed)
                    {
                        Choose(captured);
                        var menu = Menu == null ? null : Menu(captured, captured.Start);
                        if (menu != null) Ui.ShowMenu(menu, row);
                        e.Handled = true;
                        return;
                    }
                    if (!point.IsLeftButtonPressed) return;
                    Choose(captured);
                    if (e.ClickCount >= 2 && Edit != null) Edit(captured);
                    e.Handled = true;
                };
                _byId[entry.Id ?? ""] = row;
                _rows.Children.Add(row);
            }
            if (_shown.Count > MaxRows)
                _rows.Children.Add(new TextBlock { Text = "… et " + (_shown.Count - MaxRows) + " de plus : affinez la recherche ou les filtres.", Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(8, 10, 8, 10), Tag = "timeline-list-more" });
        }

        private void Choose(TimelineEvent entry)
        {
            if (Select != null) Select(entry);
            Paint();
        }

        /// <summary>Repeint la ligne choisie, sans refaire la liste.</summary>
        public void Paint()
        {
            var chosen = Selected == null ? null : Selected();
            foreach (var pair in _byId)
                pair.Value.Background = chosen != null && chosen.Id == pair.Key ? Chrome.AccentTint : Brushes.Transparent;
        }

        // ---- sonde
        internal int RowCountForProbe { get { return _byId.Count; } }
        internal List<string> TitlesForProbe()
        {
            var titles = new List<string>();
            foreach (var entry in _shown) titles.Add(entry.Title ?? "");
            return titles;
        }
        internal string CountForProbe { get { return _count.Text; } }
        internal string RowTextForProbe(int index)
        {
            if (index < 0 || index >= _shown.Count) return null;
            Border row;
            if (!_byId.TryGetValue(_shown[index].Id ?? "", out row)) return null;
            var parts = new List<string>();
            Collect(row.Child, parts);
            return string.Join(" | ", parts.ToArray());
        }
        private static void Collect(Control control, List<string> parts)
        {
            var text = control as TextBlock;
            if (text != null) { parts.Add(text.Text ?? ""); return; }
            var panel = control as Panel;
            if (panel != null) foreach (var child in panel.Children) Collect(child, parts);
        }
        internal void ClickForProbe(int index, bool twice)
        {
            if (index < 0 || index >= _shown.Count) return;
            Choose(_shown[index]);
            if (twice && Edit != null) Edit(_shown[index]);
        }
    }
}

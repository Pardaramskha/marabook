using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using Marabook.Correction;

namespace Marabook.App
{
    /// <summary>Le BILAN DE STYLE au rail (09/10) : les sections racontées par
    /// StyleReport.Narrate, en phrases simples, dans un onglet de la colonne
    /// de droite — plus une fenêtre. Une ligne qui désigne un paragraphe se
    /// clique : la vue y va. Ouvert le temps du bilan : la croix, le repli du
    /// rail ou un autre écrit le ferment.</summary>
    public class StyleReportPanel : DockPanel
    {
        private readonly TextBlock _title;
        private readonly StackPanel _list;
        private StyleReport _report;

        public event Action CloseRequested;
        public event Action<int> ParagraphRequested;

        public StyleReportPanel()
        {
            Background = Chrome.BarBgLight;
            var head = new DockPanel { Margin = new Thickness(12, 10, 12, 6) };
            SetDock(head, Dock.Top);
            var close = Buttons.Icon("exclude", "Fermer le bilan (le rail se replie)", Buttons.Compact, Buttons.Look.Calm);
            close.Click += delegate { var h = CloseRequested; if (h != null) h(); };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            var copy = Buttons.Icon("copy-simple-bold", "Copier le bilan dans le presse-papiers", Buttons.Compact, Buttons.Look.Calm);
            copy.Click += delegate { if (_report != null) { try { Ui.SetClipboardText(this, _report.ToText()); } catch { } } };
            DockPanel.SetDock(copy, Dock.Right);
            head.Children.Add(copy);
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = "Bilan de style", Foreground = Chrome.SoftText, FontSize = 12, FontWeight = FontWeight.SemiBold });
            _title = new TextBlock { Foreground = Chrome.Ink, FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            titles.Children.Add(_title);
            titles.Children.Add(new TextBlock
            {
                Text = "Des mesures, pas des notes : chaque ligne dit ce qu'on a compté et où regarder.",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
            head.Children.Add(titles);
            Children.Add(head);
            _list = new StackPanel { Margin = new Thickness(12, 0, 12, 12) };
            Children.Add(new ScrollViewer
            {
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                Content = _list
            });
        }

        public void Show(string title, StyleReport report)
        {
            _report = report;
            _title.Text = title ?? "";
            _list.Children.Clear();
            if (report == null) return;
            foreach (var section in report.Narrate())
            {
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 3) };
                titleRow.Children.Add(ComposedRenderer.FindingDot(
                    section.Good ? ComposedRenderer.FindingPen(FindingCategory.Style)
                        : ComposedRenderer.FindingPen(FindingCategory.Typography), 8));
                titleRow.Children.Add(new TextBlock
                {
                    Text = section.Title,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Chrome.Ink,
                    VerticalAlignment = VerticalAlignment.Center
                });
                _list.Children.Add(titleRow);
                for (var i = 0; i < section.Lines.Count; i++)
                {
                    int paragraph;
                    var linked = section.ParagraphLinks.TryGetValue(i, out paragraph);
                    var line = new TextBlock
                    {
                        Text = section.Lines[i],
                        FontSize = 12,
                        Foreground = linked ? Chrome.Accent : Chrome.Ink,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(14, 1, 0, 2)
                    };
                    if (linked)
                    {
                        // La ligne désigne un paragraphe (09/10) : un clic y mène.
                        var target = paragraph;
                        line.Cursor = new Cursor(StandardCursorType.Hand);
                        line.TextDecorations = TextDecorations.Underline;
                        ToolTip.SetTip(line, "Aller au paragraphe " + (target + 1));
                        line.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                        {
                            if (!e.GetCurrentPoint(line).Properties.IsLeftButtonPressed) return;
                            e.Handled = true;
                            var h = ParagraphRequested;
                            if (h != null) h(target);
                        };
                    }
                    _list.Children.Add(line);
                }
            }
        }

        /// <summary>Sonde (09/10) : le nombre de lignes cliquables.</summary>
        public int LinkedLinesForProbe
        {
            get
            {
                var n = 0;
                foreach (var child in _list.Children) if (child is TextBlock && ((TextBlock)child).Cursor != null && ((TextBlock)child).TextDecorations != null) n++;
                return n;
            }
        }
        public void ClickLinkedLineForProbe(int index)
        {
            var n = 0;
            foreach (var child in _list.Children)
            {
                var block = child as TextBlock;
                if (block == null || block.TextDecorations == null) continue;
                if (n++ == index)
                {
                    var tip = ToolTip.GetTip(block) as string;
                    int paragraph;
                    if (tip != null && int.TryParse(tip.Substring(tip.LastIndexOf(' ') + 1), out paragraph))
                    { var h = ParagraphRequested; if (h != null) h(paragraph - 1); }
                    return;
                }
            }
        }
    }
}

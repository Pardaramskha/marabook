using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marabook.App
{
    /// <summary>La FENÊTRE du tutoriel (1.1.0, 29/10, Rémi) : sans cadre, à
    /// bords arrondis, largeur fixe, déplaçable par son en-tête. En tête, le
    /// titre du chapitre et deux flèches de chapitre ; au corps, le texte de
    /// l'étape (gras et italique du Markdown) ; en pied, Fermer à gauche,
    /// Précédent et Suivant (ou Terminer) à droite. Elle ne sait rien du
    /// script : la fenêtre principale la nourrit et écoute ses boutons.</summary>
    public sealed class TutorialWindow : Window
    {
        public const double FixedWidth = 440;

        public event Action NextRequested, PreviousRequested, NextChapterRequested, PreviousChapterRequested, CloseRequested;

        private readonly TextBlock _chapter, _progress, _body;
        private readonly Button _previous, _next, _previousChapter, _nextChapter;
        private bool _userMoved;

        public TutorialWindow(Window owner)
        {
            Title = "Tutoriel";
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Width = FixedWidth;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            ShowActivated = true;
            Topmost = true; // TOUJOURS au-dessus des fenêtres que le tuto ouvre (29/10, Rémi)
            WindowStartupLocation = WindowStartupLocation.Manual;

            var frame = new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(0),
                Margin = new Thickness(12), // la place de l'ombre
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 8, Blur = 24, Color = Color.FromArgb(0x55, 0, 0, 0) })
            };
            var column = new DockPanel();

            // — L'en-tête : titre du chapitre, flèches de chapitre ; on le tire pour déplacer.
            var header = new Border
            {
                Background = Chrome.BarBg,
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Padding = new Thickness(16, 12, 12, 10),
                Cursor = new Cursor(StandardCursorType.SizeAll)
            };
            var headerRow = new DockPanel();
            var arrows = new StackPanel { Orientation = Orientation.Horizontal };
            _previousChapter = Buttons.Icon("caret-left-bold", "Chapitre précédent", Buttons.Compact, Buttons.Look.Calm);
            _previousChapter.Click += delegate { var h = PreviousChapterRequested; if (h != null) h(); };
            _nextChapter = Buttons.Icon("caret-right-bold", "Chapitre suivant", Buttons.Compact, Buttons.Look.Calm);
            _nextChapter.Margin = new Thickness(2, 0, 0, 0);
            _nextChapter.Click += delegate { var h = NextChapterRequested; if (h != null) h(); };
            arrows.Children.Add(_previousChapter);
            arrows.Children.Add(_nextChapter);
            DockPanel.SetDock(arrows, Dock.Right);
            headerRow.Children.Add(arrows);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _chapter = new TextBlock { Foreground = Chrome.Ink, FontSize = 14, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
            _progress = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
            titles.Children.Add(_chapter);
            titles.Children.Add(_progress);
            headerRow.Children.Add(titles);
            header.Child = headerRow;
            header.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (e.Source is Button || !e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
                _userMoved = true;
                BeginMoveDrag(e);
            };
            DockPanel.SetDock(header, Dock.Top);
            column.Children.Add(header);

            // — Le pied : Fermer à gauche, Précédent et Suivant à droite.
            var foot = new DockPanel { Margin = new Thickness(16, 10, 16, 14) };
            var close = Buttons.Text("Fermer", "Mettre fin au tutoriel (sans le succès)", Buttons.Bar, Buttons.Look.Outline);
            close.Click += delegate { var h = CloseRequested; if (h != null) h(); };
            DockPanel.SetDock(close, Dock.Left);
            foot.Children.Add(close);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _previous = Buttons.Text("Précédent", "L'étape précédente", Buttons.Bar, Buttons.Look.Outline);
            _previous.Click += delegate { var h = PreviousRequested; if (h != null) h(); };
            _next = Buttons.Text("Suivant", "L'étape suivante", Buttons.Bar, Buttons.Look.Primary);
            _next.MinWidth = 96;
            _next.Margin = new Thickness(8, 0, 0, 0);
            _next.IsDefault = true;
            _next.Click += delegate { var h = NextRequested; if (h != null) h(); };
            right.Children.Add(_previous);
            right.Children.Add(_next);
            foot.Children.Add(right);
            DockPanel.SetDock(foot, Dock.Bottom);
            column.Children.Add(foot);

            // — Le corps : le texte de l'étape, qui défile s'il est long.
            _body = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 13.5,
                LineHeight = 21,
                TextWrapping = TextWrapping.Wrap
            };
            column.Children.Add(new ScrollViewer
            {
                Content = new Border { Child = _body, Padding = new Thickness(16, 14, 16, 4) },
                MaxHeight = 380,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            });
            frame.Child = column;
            Content = frame;

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { var h = CloseRequested; if (h != null) h(); e.Handled = true; }
                else if (e.Key == Key.Right || e.Key == Key.PageDown) { var h = NextRequested; if (h != null) h(); e.Handled = true; }
                else if (e.Key == Key.Left || e.Key == Key.PageUp) { var h = PreviousRequested; if (h != null) h(); e.Handled = true; }
            };
        }

        /// <summary>L'utilisateur a déplacé la fenêtre : on ne la replace que si elle gêne.</summary>
        public bool UserMoved { get { return _userMoved; } }

        /// <summary>Une étape : le chapitre, la progression (« 2 / 4 »), le texte
        /// Markdown, et l'état des boutons (première, dernière étape).</summary>
        public void Present(string chapterTitle, string progress, string markdown, bool first, bool last, bool firstChapter, bool lastChapter)
        {
            _chapter.Text = chapterTitle ?? "";
            _progress.Text = progress ?? "";
            _body.Inlines = Inlines(markdown ?? "");
            _previous.IsEnabled = !first;
            _previousChapter.IsEnabled = !firstChapter;
            _nextChapter.IsEnabled = !lastChapter;
            _next.Content = last ? "Terminer" : "Suivant";
            ToolTip.SetTip(_next, last ? "Terminer le tutoriel et revenir à l'accueil" : "L'étape suivante");
        }

        /// <summary>Le Markdown léger de Rémi : **gras**, *italique*, le reste tel quel.</summary>
        public static InlineCollection Inlines(string markdown)
        {
            var inlines = new InlineCollection();
            var text = markdown.Replace("\\n", "\n");
            var pattern = new Regex(@"\*\*(.+?)\*\*|\*(.+?)\*", RegexOptions.Singleline);
            var at = 0;
            foreach (Match match in pattern.Matches(text))
            {
                if (match.Index > at) inlines.Add(new Run(text.Substring(at, match.Index - at)));
                if (match.Groups[1].Success) inlines.Add(new Run(match.Groups[1].Value) { FontWeight = FontWeight.Bold });
                else inlines.Add(new Run(match.Groups[2].Value) { FontStyle = FontStyle.Italic, Foreground = Chrome.SoftText });
                at = match.Index + match.Length;
            }
            if (at < text.Length) inlines.Add(new Run(text.Substring(at)));
            return inlines;
        }

        // — Sonde (29/10).
        internal string ChapterForProbe { get { return _chapter.Text; } }
        internal string NextLabelForProbe { get { return _next.Content as string; } }
        internal bool PreviousEnabledForProbe { get { return _previous.IsEnabled; } }
        internal void ClickNextForProbe() { var h = NextRequested; if (h != null) h(); }
        internal void ClickPreviousForProbe() { var h = PreviousRequested; if (h != null) h(); }
        internal void ClickNextChapterForProbe() { var h = NextChapterRequested; if (h != null) h(); }
        internal void ClickCloseForProbe() { var h = CloseRequested; if (h != null) h(); }
        internal void CaptureForProbe(string path)
        {
            var size = new PixelSize(Math.Max(1, (int)Bounds.Width), Math.Max(1, (int)Bounds.Height));
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96)))
            {
                bitmap.Render(this);
                bitmap.Save(path);
            }
        }
    }
}

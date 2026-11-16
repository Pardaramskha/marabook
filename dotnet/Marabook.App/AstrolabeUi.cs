using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LES BRIQUES D'INTERFACE DE L'ASTROLABE (16/11/2026, Rémi : « plus
    /// intuitive, moins austère, des séparations de sous-sections plus claires ») :
    /// la SECTION (une carte à titre), l'ACCORDÉON (une carte qui se déplie),
    /// l'encadré d'EXEMPLE (ce que la notation donne, teinté pour ne plus se
    /// fondre dans le texte), le BANDEAU d'avertissement, la carte d'un
    /// COMPORTEMENT du ciel et le CADRAN de l'horloge.</summary>
    internal static class AstrolabeUi
    {
        private static IBrush CardFill { get { return new SolidColorBrush(Chrome.FieldBg.Color, Chrome.Dark ? 0.45 : 0.7); } }

        /// <summary>Une sous-section : son titre, ce qu'elle règle en une ligne, son contenu — dans une carte.</summary>
        public static Border Section(string title, string subtitle, Control body, string tag = null)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, Foreground = Chrome.Ink, FontSize = 13.5, FontWeight = FontWeight.SemiBold });
            if (!string.IsNullOrEmpty(subtitle))
                stack.Children.Add(new TextBlock { Text = subtitle, Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (body != null)
            {
                body.Margin = new Thickness(0, 9, 0, 0);
                stack.Children.Add(body);
            }
            return new Border
            {
                Child = stack, Padding = new Thickness(14, 11, 14, 13), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(10),
                Background = CardFill, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = tag
            };
        }

        /// <summary>L'encadré d'un exemple : teinté d'accent, en demi-gras — il ne se confond plus avec les libellés.</summary>
        public static Border Example(TextBlock text, string tag = null)
        {
            text.Foreground = Chrome.AccentStrong;
            text.FontWeight = FontWeight.SemiBold;
            text.TextWrapping = TextWrapping.Wrap;
            var box = new Border
            {
                Child = text, Padding = new Thickness(10, 5, 10, 6), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left,
                Background = Chrome.AccentTint, BorderBrush = Chrome.AccentSoft, BorderThickness = new Thickness(1), Tag = tag,
                IsVisible = !string.IsNullOrEmpty(text.Text)
            };
            // Sans texte, pas d'encadré vide.
            text.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == TextBlock.TextProperty) box.IsVisible = !string.IsNullOrEmpty(text.Text); };
            return box;
        }

        /// <summary>Un bandeau qui prévient (ambre) ou qui bloque (rouge) : une icône, un texte.</summary>
        public static Border Notice(string text, bool danger = false, string tag = null)
        {
            var tone = danger ? Chrome.Danger : Chrome.Warn;
            var row = new DockPanel();
            var icon = Icons.Make("warning-fill", 15, tone);
            icon.Margin = new Thickness(0, 1, 9, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = text, Foreground = Chrome.Ink, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            return new Border
            {
                Child = row, Background = new SolidColorBrush(tone.Color, 0.12), BorderBrush = new SolidColorBrush(tone.Color, 0.7), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 14, 10), Margin = new Thickness(0, 0, 0, 12), Tag = tag
            };
        }

        /// <summary>La pastille numérotée d'une étape (« 1 », « 2 », « 3 »).</summary>
        public static Border Step(string number, bool done)
        {
            return new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = done ? Chrome.Accent : Brushes.Transparent,
                BorderBrush = done ? Chrome.Accent : Chrome.BorderStrong, BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = number, Foreground = done ? Brushes.White : Chrome.SoftText, FontSize = 12, FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        /// <summary>Les comportements du ciel (Astro.SunBehaviors, MoonBehaviors) :
        /// un par ligne, le titre en gras, une puce d'accent pour ce qui sort de l'ordinaire.</summary>
        public static void FillBehaviors(StackPanel panel, List<SkyBehavior> behaviors)
        {
            panel.Children.Clear();
            foreach (var behavior in behaviors)
            {
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = behavior.Title, Foreground = Chrome.Ink, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(new TextBlock { Text = behavior.Text, Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
                var dot = new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 5, 9, 0), VerticalAlignment = VerticalAlignment.Top,
                    Background = behavior.Notable ? Chrome.Accent : Chrome.BorderStrong
                };
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Tag = behavior.Notable ? "sky-behavior-notable" : "sky-behavior" };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(text);
                panel.Children.Add(row);
            }
        }
    }

    /// <summary>UN ACCORDÉON : une carte aux bords arrondis dont l'en-tête
    /// (un chevron, un titre, un résumé à droite) déplie ou replie le contenu.
    /// Replié par défaut : les outils d'appoint (le Répartiteur, le calculateur
    /// de circonférence) ne chargent plus la fenêtre.</summary>
    internal sealed class Accordion : Border
    {
        private readonly Control _body;
        private readonly Border _caret;
        private readonly TextBlock _summary;
        private bool _open;

        public Accordion(string title, string summary, Control body, bool open = false, string tag = null)
        {
            _body = body;
            Tag = tag;
            CornerRadius = new CornerRadius(10);
            BorderBrush = Chrome.BorderStrong;
            BorderThickness = new Thickness(1);
            Background = new SolidColorBrush(Chrome.FieldBg.Color, Chrome.Dark ? 0.45 : 0.7);
            Margin = new Thickness(0, 0, 0, 10);
            ClipToBounds = true;

            _caret = new Border { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _summary = new TextBlock { Text = summary ?? "", Foreground = Chrome.SoftText, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) };
            var head = new DockPanel();
            DockPanel.SetDock(_caret, Dock.Left);
            head.Children.Add(_caret);
            var name = new TextBlock { Text = title, Foreground = Chrome.Ink, FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(name, Dock.Left);
            head.Children.Add(name);
            head.Children.Add(_summary);
            var header = new Border { Child = head, Padding = new Thickness(12, 9, 14, 9), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Tag = "accordion-header" };
            header.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                IsOpen = !IsOpen;
            };
            header.PointerEntered += delegate { header.Background = Chrome.AccentTint; };
            header.PointerExited += delegate { header.Background = Brushes.Transparent; };
            _body.Margin = new Thickness(14, 2, 14, 13);
            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(_body);
            Child = stack;
            _open = !open;
            IsOpen = open;
        }

        public string Summary { get { return _summary.Text; } set { _summary.Text = value ?? ""; } }

        public bool IsOpen
        {
            get { return _open; }
            set
            {
                if (_open == value) return;
                _open = value;
                _body.IsVisible = value;
                _caret.Child = Icons.Make(value ? "caret-down-bold" : "caret-right-bold", 12, Chrome.SoftText);
            }
        }
    }

    /// <summary>LE CADRAN DE L'HORLOGE (16/11, Rémi) : un tour = un jour. La
    /// couronne extérieure porte les heures du jour, la suivante les minutes
    /// d'une heure, la plus petite les secondes d'une minute ; trois aiguilles
    /// montrent une heure d'exemple. Tout se redessine quand un nombre change.</summary>
    internal sealed class ClockDial : Control
    {
        private int _hours = 24, _minutes = 60, _seconds = 60;

        public ClockDial()
        {
            Width = 210;
            Height = 210;
        }

        public void Set(int hours, int minutes, int seconds)
        {
            _hours = Math.Max(1, hours);
            _minutes = Math.Max(1, minutes);
            _seconds = Math.Max(1, seconds);
            InvalidateVisual();
        }

        internal string MetricsForProbe { get { return _hours + "/" + _minutes + "/" + _seconds; } }

        /// <summary>Le pas des graduations chiffrées : au plus une douzaine de nombres autour du cadran.</summary>
        private static int LabelStep(int count)
        {
            foreach (var step in new[] { 1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 25, 30, 50, 60, 100, 200, 250, 500 })
                if (count / (double)step <= 12) return step;
            return Math.Max(1, count / 10);
        }

        private static Point At(Point center, double radius, double turn)
        {
            var angle = turn * 2 * Math.PI - Math.PI / 2; // zéro en haut, dans le sens des aiguilles
            return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        }

        private void Ring(DrawingContext context, Point center, double radius, int count, IBrush ink, double tick, double fontSize, bool labels)
        {
            var pen = new Pen(Chrome.BorderStrong, 1);
            var strong = new Pen(ink, 1.3);
            context.DrawEllipse(null, new Pen(Chrome.Border, 1), center, radius, radius);
            var step = LabelStep(count);
            // Trop de graduations pour le périmètre : une sur « thin » seulement.
            var thin = Math.Max(1, (int)Math.Ceiling(count / (radius * 2 * Math.PI / 3.5)));
            for (var i = 0; i < count; i++)
            {
                var major = i % step == 0;
                if (!major && i % thin != 0) continue;
                var turn = i / (double)count;
                context.DrawLine(major ? strong : pen, At(center, radius, turn), At(center, radius - (major ? tick : tick * 0.55), turn));
                if (!major || !labels) continue;
                var text = new FormattedText(i.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, fontSize, ink);
                var spot = At(center, radius - tick - fontSize * 0.85, turn);
                context.DrawText(text, new Point(spot.X - text.Width / 2, spot.Y - text.Height / 2));
            }
        }

        public override void Render(DrawingContext context)
        {
            var size = Math.Min(Bounds.Width, Bounds.Height);
            if (size < 40) return;
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            var outer = size / 2 - 2;
            context.DrawEllipse(Chrome.FieldBg, new Pen(Chrome.BorderStrong, 1.5), center, outer, outer);
            Ring(context, center, outer - 3, _hours, Chrome.Ink, 8, 10.5, true);
            Ring(context, center, outer * 0.62, _minutes, Chrome.Time, 6, 8.5, true);
            Ring(context, center, outer * 0.30, _seconds, Chrome.Accent, 4, 7, false);
            // L'heure d'exemple : un peu après la mi-journée, à la demie, aux trois quarts de la minute.
            var hour = Math.Min(_hours - 1, (int)Math.Floor(_hours * 14 / 24.0));
            var minute = _minutes / 2;
            var second = _seconds * 3 / 4;
            var hourTurn = (hour + minute / (double)_minutes) / _hours;
            context.DrawLine(new Pen(Chrome.Ink, 3, lineCap: PenLineCap.Round), center, At(center, outer * 0.80, hourTurn));
            context.DrawLine(new Pen(Chrome.Time, 2, lineCap: PenLineCap.Round), center, At(center, outer * 0.58, minute / (double)_minutes));
            context.DrawLine(new Pen(Chrome.Accent, 1.2, lineCap: PenLineCap.Round), center, At(center, outer * 0.28, second / (double)_seconds));
            context.DrawEllipse(Chrome.Ink, null, center, 3.2, 3.2);
        }

        /// <summary>L'heure d'exemple que montrent les aiguilles : « 14h30m45 ».</summary>
        public static string Sample(int hours, int minutes, int seconds)
        {
            hours = Math.Max(1, hours); minutes = Math.Max(1, minutes); seconds = Math.Max(1, seconds);
            var hour = Math.Min(hours - 1, (int)Math.Floor(hours * 14 / 24.0));
            return hour + "h" + (minutes / 2).ToString("00", CultureInfo.InvariantCulture) + "m" + (seconds * 3 / 4).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}

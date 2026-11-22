using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Marabook.App
{
    /// <summary>LE PAVÉ DE SIGNATURE (2.0.0, 22/11/2026 — PDFaah › Signer) :
    /// on trace sa signature à la souris ou au stylet, sur une feuille. Le
    /// tracé est rendu en image à fond TRANSPARENT (encre bleu nuit ou noire),
    /// rognée à ce qui est tracé : l'outil la pose ensuite sur la page.</summary>
    public class SignaturePadDialog : Window
    {
        public sealed class Drawing
        {
            public byte[] Pixels; // BGRA, alpha droit, le haut d'abord
            public int Width, Height;
        }

        private const double PadWidth = 560, PadHeight = 220;
        private readonly Canvas _pad;
        private readonly List<Polyline> _strokes = new List<Polyline>();
        private readonly Button _use;
        private Polyline _current;
        private IBrush _ink = new SolidColorBrush(Color.FromRgb(0x14, 0x1E, 0x4A));
        private Drawing _result;

        private SignaturePadDialog(Window owner)
        {
            Title = "Tracer une signature";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            Tag = "signature-pad";

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "Signez dans le cadre, d'un trait continu ou en plusieurs fois.", Foreground = Chrome.SoftText, Margin = new Thickness(0, 0, 0, 8) });
            // La feuille : blanche quel que soit le thème, c'est du papier.
            _pad = new Canvas { Width = PadWidth, Height = PadHeight, Background = Chrome.PrintPaper, Cursor = new Cursor(StandardCursorType.Cross), ClipToBounds = true, Tag = "signature-canvas" };
            _pad.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(_pad).Properties.IsLeftButtonPressed) return;
                Begin(e.GetPosition(_pad));
                e.Pointer.Capture(_pad);
                e.Handled = true;
            };
            _pad.PointerMoved += delegate(object sender, PointerEventArgs e)
            {
                if (_current == null || !e.GetCurrentPoint(_pad).Properties.IsLeftButtonPressed) return;
                Extend(e.GetPosition(_pad));
            };
            _pad.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                _current = null;
                e.Pointer.Capture(null);
            };
            panel.Children.Add(new Border { BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Child = _pad });

            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            tools.Children.Add(new TextBlock { Text = "Encre :", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var blue = new RadioButton { GroupName = "ink", Content = "Bleu nuit", IsChecked = true, Margin = new Thickness(0, 0, 14, 0) };
            var black = new RadioButton { GroupName = "ink", Content = "Noire" };
            blue.IsCheckedChanged += delegate { if (blue.IsChecked == true) SetInk(Color.FromRgb(0x14, 0x1E, 0x4A)); };
            black.IsCheckedChanged += delegate { if (black.IsChecked == true) SetInk(Color.FromRgb(0x10, 0x10, 0x10)); };
            tools.Children.Add(blue);
            tools.Children.Add(black);
            panel.Children.Add(tools);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var clear = new Button { Content = "Effacer", MinWidth = 90, Tag = "signature-clear" };
            clear.Click += delegate { _strokes.Clear(); _pad.Children.Clear(); _current = null; _use.IsEnabled = false; };
            _use = new Button { Content = "Utiliser cette signature", IsDefault = true, MinWidth = 150, Margin = new Thickness(8, 0, 0, 0), IsEnabled = false, Tag = "signature-use" };
            _use.Click += delegate { _result = Render(); Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(clear);
            buttons.Children.Add(_use);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, _use);
            panel.Children.Add(buttons);
            Content = panel;
        }

        private void SetInk(Color color)
        {
            _ink = new SolidColorBrush(color);
            foreach (var stroke in _strokes) stroke.Stroke = _ink;
        }

        private void Begin(Point point)
        {
            _current = new Polyline { Stroke = _ink, StrokeThickness = 2.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Points = new List<Point> { point, new Point(point.X + 0.1, point.Y + 0.1) } };
            _strokes.Add(_current);
            _pad.Children.Add(_current);
            _use.IsEnabled = true;
        }

        private void Extend(Point point)
        {
            var points = new List<Point>(_current.Points);
            var last = points[points.Count - 1];
            if (Math.Abs(last.X - point.X) + Math.Abs(last.Y - point.Y) < 0.8) return;
            points.Add(point);
            _current.Points = points; // une liste neuve : le tracé se redessine
        }

        /// <summary>Le tracé, en image à fond transparent rognée à ce qui est tracé. null s'il n'y a rien.</summary>
        private Drawing Render()
        {
            if (_strokes.Count == 0) return null;
            // Rendu deux fois plus fin que l'écran : la signature tient l'agrandissement.
            const int scale = 3;
            var host = new Canvas { Width = PadWidth, Height = PadHeight, Background = null };
            foreach (var stroke in _strokes)
                host.Children.Add(new Polyline { Stroke = stroke.Stroke, StrokeThickness = stroke.StrokeThickness, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Points = new List<Point>(stroke.Points) });
            host.Measure(new Size(PadWidth, PadHeight));
            host.Arrange(new Rect(0, 0, PadWidth, PadHeight));
            using (var target = new RenderTargetBitmap(new PixelSize((int)PadWidth * scale, (int)PadHeight * scale), new Vector(96 * scale, 96 * scale)))
            using (var buffer = new MemoryStream())
            {
                target.Render(host);
                target.Save(buffer);
                byte[] pixels;
                int width, height;
                if (!PdfImages.Decode(buffer.ToArray(), out pixels, out width, out height)) return null;
                int trimmedWidth, trimmedHeight;
                var trimmed = PdfImages.Trim(pixels, width, height, 6 * scale, out trimmedWidth, out trimmedHeight);
                return new Drawing { Pixels = trimmed, Width = trimmedWidth, Height = trimmedHeight };
            }
        }

        public static async Task<Drawing> Ask(Window owner)
        {
            var dialog = new SignaturePadDialog(owner);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }

        // — Sonde : un tracé sans souris.
        internal static SignaturePadDialog ForProbe(Window owner) { return new SignaturePadDialog(owner); }
        internal Drawing DrawForProbe(params Point[] points)
        {
            if (points.Length == 0) return null;
            Begin(points[0]);
            for (var i = 1; i < points.Length; i++) Extend(points[i]);
            _current = null;
            return Render();
        }
    }
}

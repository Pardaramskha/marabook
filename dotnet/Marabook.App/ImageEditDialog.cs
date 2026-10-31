using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Marabook.App
{
    /// <summary>La fenêtre d'import d'une image (1.0.6, 14/10) : l'image telle
    /// qu'elle entrera dans le projet, à PIVOTER (quarts de tour) et à
    /// RECADRER (le cadre se tire par ses coins, se déplace de l'intérieur,
    /// se redessine depuis l'extérieur), puis « Importer » applique le tout et
    /// l'optimisation (ImageImport). « Annuler » n'importe rien. Ouverte par
    /// chaque site d'import à la main (portrait et corps de fiche, image de
    /// carte, image d'écrit) ; le glisser-déposer et les imports en lot
    /// n'optimisent qu'en silence.</summary>
    public class ImageEditDialog : Window
    {
        private const double StageWidth = 760;
        private const double StageHeight = 500;
        private const double HandleSize = 12;
        private const double MinCropPx = 24; // en pixels d'écran

        private readonly byte[] _original;
        private readonly string _extension;
        private SKBitmap _decoded;     // l'image d'origine, décodée une fois
        private SKBitmap _turned;      // l'image tournée (pleine taille) — _decoded quand rien n'a tourné
        private int _turns;
        private SKRectI _crop;         // en pixels de l'image tournée
        private double _scale;         // pixels d'écran par pixel d'image
        private Rect _imageRect;       // où l'image est posée dans la scène
        private readonly Canvas _stage = new Canvas { Width = StageWidth, Height = StageHeight, Background = Brushes.Transparent, ClipToBounds = true };
        private readonly Image _picture = new Image { Stretch = Stretch.Uniform };
        private readonly Rectangle[] _dim = new Rectangle[4];
        private readonly Border _frame = new Border { BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), IsHitTestVisible = false };
        private readonly Border[] _handles = new Border[4];
        private readonly TextBlock _info = new TextBlock { FontSize = 12 };
        private ImageImport.Result _result;

        // le geste en cours
        private enum Drag { None, Move, Corner, Draw }
        private Drag _drag;
        private int _corner;           // 0 = haut-gauche, 1 = haut-droite, 2 = bas-droite, 3 = bas-gauche
        private Point _down;
        private SKRectI _cropAtDown;

        public ImageEditDialog(Window owner, byte[] bytes, string extension)
        {
            _original = bytes;
            _extension = ImageImport.Normalize(extension);
            Title = "Importer une image";
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Width = StageWidth + 48;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(24, 18, 24, 16) };
            panel.Children.Add(new TextBlock { Text = "Recadrer et pivoter", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
            panel.Children.Add(new TextBlock
            {
                Text = "Tirez les coins du cadre pour recadrer, déplacez-le de l'intérieur, ou dessinez un nouveau cadre. L'image sera réduite à " + ImageImport.MaxSide + " px de côté au plus.",
                FontSize = 12,
                Foreground = Chrome.SoftText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 10)
            });

            // ---- la scène : l'image, les quatre voiles hors cadre, le cadre, les poignées
            _stage.Children.Add(_picture);
            for (var i = 0; i < 4; i++)
            {
                _dim[i] = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0x10, 0x12, 0x18)), IsHitTestVisible = false };
                _stage.Children.Add(_dim[i]);
            }
            _stage.Children.Add(_frame);
            for (var i = 0; i < 4; i++)
            {
                _handles[i] = new Border
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Background = Brushes.White,
                    BorderBrush = Chrome.Accent,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(2),
                    IsHitTestVisible = false
                };
                _stage.Children.Add(_handles[i]);
            }
            _stage.PointerPressed += OnPressed;
            _stage.PointerMoved += OnMoved;
            _stage.PointerReleased += OnReleased;
            panel.Children.Add(new Border
            {
                Background = Chrome.Dark ? Chrome.WindowBg : (IBrush)new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x36)),
                CornerRadius = new CornerRadius(6),
                Child = _stage,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // ---- la ligne d'infos et les boutons
            _info.Foreground = Chrome.SoftText;
            _info.Margin = new Thickness(0, 10, 0, 0);
            panel.Children.Add(_info);

            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            var left = Buttons.Text("↺ Pivoter à gauche", null, Buttons.Bar, Buttons.Look.Outline);
            left.Click += delegate { Turn(-1); };
            tools.Children.Add(left);
            var right = Buttons.Text("↻ Pivoter à droite", null, Buttons.Bar, Buttons.Look.Outline);
            right.Margin = new Thickness(8, 0, 0, 0);
            right.Click += delegate { Turn(1); };
            tools.Children.Add(right);
            var whole = Buttons.Text("Toute l'image", null, Buttons.Bar, Buttons.Look.Calm);
            whole.Margin = new Thickness(8, 0, 0, 0);
            whole.Click += delegate { _crop = Full(); Layout(); };
            tools.Children.Add(whole);
            DockPanel.SetDock(tools, Dock.Left);
            row.Children.Add(tools);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 88 };
            cancel.Click += delegate { Close(false); };
            buttons.Children.Add(cancel);
            var import = new Button { Content = "Importer", IsDefault = true, MinWidth = 88 };
            ToolTip.SetTip(import, "Applique la rotation et le recadrage, optimise l'image et l'ajoute au projet");
            import.Click += delegate { Import(); };
            buttons.Children.Add(import);
            Dialogs.Arrange(buttons, import);
            row.Children.Add(buttons);
            panel.Children.Add(row);
            Content = panel;

            Decode();
            Opened += delegate { Layout(); };
            Closed += delegate
            {
                if (_turned != null && _turned != _decoded) _turned.Dispose();
                if (_decoded != null) _decoded.Dispose();
            };
        }

        /// <summary>L'image choisie, prête : null si l'utilisateur a renoncé ou
        /// si elle ne se décode pas (le site d'import la prend alors telle quelle).</summary>
        public ImageImport.Result Result { get { return _result; } }
        public bool Decodable { get { return _decoded != null; } }

        private void Decode()
        {
            try { _decoded = SKBitmap.Decode(_original); }
            catch (Exception) { _decoded = null; }
            _turned = _decoded;
            _crop = Full();
            RefreshPicture();
        }

        private SKRectI Full()
        {
            return _turned == null ? SKRectI.Empty : new SKRectI(0, 0, _turned.Width, _turned.Height);
        }

        private void Turn(int direction)
        {
            if (_decoded == null) return;
            _turns = ((_turns + direction) % 4 + 4) % 4;
            if (_turned != null && _turned != _decoded) _turned.Dispose();
            _turned = ImageImport.Rotate(_decoded, _turns);
            _crop = Full();
            RefreshPicture();
            Layout();
        }

        /// <summary>L'aperçu : l'image tournée, réduite à la scène (jamais plus
        /// de 1 400 px), passée à Avalonia par un PNG en mémoire.</summary>
        private void RefreshPicture()
        {
            _picture.Source = null;
            if (_turned == null) { _info.Text = "Cette image ne se décode pas : elle sera importée telle quelle."; return; }
            var preview = Math.Max(_turned.Width, _turned.Height) > 1400 ? ImageImport.Resize(_turned, 1400) : _turned;
            try
            {
                using (var image = SKImage.FromBitmap(preview))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var stream = new MemoryStream(data.ToArray()))
                    _picture.Source = new Bitmap(stream);
            }
            finally { if (preview != _turned) preview.Dispose(); }
        }

        /// <summary>Pose l'image, les voiles, le cadre et les poignées.</summary>
        private void Layout()
        {
            if (_turned == null) return;
            _scale = Math.Min(StageWidth / _turned.Width, StageHeight / _turned.Height);
            var width = _turned.Width * _scale;
            var height = _turned.Height * _scale;
            _imageRect = new Rect((StageWidth - width) / 2, (StageHeight - height) / 2, width, height);
            _picture.Width = width;
            _picture.Height = height;
            Canvas.SetLeft(_picture, _imageRect.X);
            Canvas.SetTop(_picture, _imageRect.Y);
            var crop = ToScreen(_crop);
            // les voiles : au-dessus, en dessous, à gauche, à droite du cadre
            Place(_dim[0], _imageRect.X, _imageRect.Y, width, Math.Max(0, crop.Y - _imageRect.Y));
            Place(_dim[1], _imageRect.X, crop.Bottom, width, Math.Max(0, _imageRect.Bottom - crop.Bottom));
            Place(_dim[2], _imageRect.X, crop.Y, Math.Max(0, crop.X - _imageRect.X), crop.Height);
            Place(_dim[3], crop.Right, crop.Y, Math.Max(0, _imageRect.Right - crop.Right), crop.Height);
            Place(_frame, crop.X, crop.Y, crop.Width, crop.Height);
            var corners = Corners(crop);
            for (var i = 0; i < 4; i++)
                Place(_handles[i], corners[i].X - HandleSize / 2, corners[i].Y - HandleSize / 2, HandleSize, HandleSize);
            var whole = _crop.Width == _turned.Width && _crop.Height == _turned.Height;
            _info.Text = _decoded.Width + " × " + _decoded.Height + " px"
                + (_turns != 0 ? ", tournée de " + (_turns * 90) + "°" : "")
                + (whole ? ", toute l'image" : ", recadrée en " + _crop.Width + " × " + _crop.Height + " px")
                + (Math.Max(_crop.Width, _crop.Height) > ImageImport.MaxSide ? " — réduite à " + ImageImport.MaxSide + " px de côté à l'import" : "");
        }

        private static void Place(Control control, double x, double y, double width, double height)
        {
            Canvas.SetLeft(control, x);
            Canvas.SetTop(control, y);
            control.Width = Math.Max(0, width);
            control.Height = Math.Max(0, height);
        }

        private Rect ToScreen(SKRectI crop)
        {
            return new Rect(_imageRect.X + crop.Left * _scale, _imageRect.Y + crop.Top * _scale, crop.Width * _scale, crop.Height * _scale);
        }

        private static Point[] Corners(Rect r)
        {
            return new[] { new Point(r.X, r.Y), new Point(r.Right, r.Y), new Point(r.Right, r.Bottom), new Point(r.X, r.Bottom) };
        }

        // ------------------------------------------------------------ le geste

        private void OnPressed(object sender, PointerPressedEventArgs e)
        {
            if (_turned == null || !e.GetCurrentPoint(_stage).Properties.IsLeftButtonPressed) return;
            _down = e.GetPosition(_stage);
            _cropAtDown = _crop;
            var crop = ToScreen(_crop);
            var corners = Corners(crop);
            _drag = Drag.None;
            for (var i = 0; i < 4; i++)
                if (Math.Abs(corners[i].X - _down.X) <= HandleSize && Math.Abs(corners[i].Y - _down.Y) <= HandleSize) { _drag = Drag.Corner; _corner = i; break; }
            if (_drag == Drag.None)
                _drag = crop.Contains(_down) ? Drag.Move : _imageRect.Contains(_down) ? Drag.Draw : Drag.None;
            if (_drag != Drag.None) { e.Pointer.Capture(_stage); e.Handled = true; }
        }

        private void OnMoved(object sender, PointerEventArgs e)
        {
            if (_drag == Drag.None || _turned == null) return;
            var at = e.GetPosition(_stage);
            var dx = (at.X - _down.X) / _scale;
            var dy = (at.Y - _down.Y) / _scale;
            var min = (int)Math.Ceiling(MinCropPx / _scale);
            var full = Full();
            var c = _cropAtDown;
            switch (_drag)
            {
                case Drag.Move:
                    {
                        var left = Clamp((int)Math.Round(c.Left + dx), 0, full.Width - c.Width);
                        var top = Clamp((int)Math.Round(c.Top + dy), 0, full.Height - c.Height);
                        _crop = new SKRectI(left, top, left + c.Width, top + c.Height);
                        break;
                    }
                case Drag.Corner:
                    {
                        var left = c.Left; var top = c.Top; var right = c.Right; var bottom = c.Bottom;
                        if (_corner == 0 || _corner == 3) left = Clamp((int)Math.Round(c.Left + dx), 0, right - min);
                        else right = Clamp((int)Math.Round(c.Right + dx), left + min, full.Width);
                        if (_corner == 0 || _corner == 1) top = Clamp((int)Math.Round(c.Top + dy), 0, bottom - min);
                        else bottom = Clamp((int)Math.Round(c.Bottom + dy), top + min, full.Height);
                        _crop = new SKRectI(left, top, right, bottom);
                        break;
                    }
                case Drag.Draw:
                    {
                        var x0 = (_down.X - _imageRect.X) / _scale;
                        var y0 = (_down.Y - _imageRect.Y) / _scale;
                        var x1 = (at.X - _imageRect.X) / _scale;
                        var y1 = (at.Y - _imageRect.Y) / _scale;
                        var left = Clamp((int)Math.Round(Math.Min(x0, x1)), 0, full.Width);
                        var right = Clamp((int)Math.Round(Math.Max(x0, x1)), 0, full.Width);
                        var top = Clamp((int)Math.Round(Math.Min(y0, y1)), 0, full.Height);
                        var bottom = Clamp((int)Math.Round(Math.Max(y0, y1)), 0, full.Height);
                        if (right - left >= min && bottom - top >= min) _crop = new SKRectI(left, top, right, bottom);
                        break;
                    }
            }
            Layout();
        }

        private void OnReleased(object sender, PointerReleasedEventArgs e)
        {
            if (_drag == Drag.None) return;
            _drag = Drag.None;
            e.Pointer.Capture(null);
        }

        private static int Clamp(int value, int min, int max) { return value < min ? min : value > max ? max : value; }

        private void Import()
        {
            if (_turned == null)
            {
                _result = new ImageImport.Result { Bytes = _original, Extension = _extension, Changed = false };
                Close(true);
                return;
            }
            var whole = _crop.Width == _turned.Width && _crop.Height == _turned.Height;
            _result = ImageImport.Apply(_original, _extension, _turns, whole ? SKRectI.Empty : _crop)
                ?? new ImageImport.Result { Bytes = _original, Extension = _extension, Changed = false };
            Close(true);
        }

        // ------------------------------------------------------------ la sonde

        public int TurnsForProbe { get { return _turns; } }
        public SKRectI CropForProbe { get { return _crop; } }
        public void TurnForProbe(int direction) { Turn(direction); }
        public void SetCropForProbe(SKRectI crop) { _crop = SKRectI.Intersect(crop, Full()); Layout(); }
        public void ImportForProbe() { Import(); }

        /// <summary>Montre la fenêtre ; le résultat (null = annulé).</summary>
        public static async Task<ImageImport.Result> Show(Window owner, byte[] bytes, string extension)
        {
            var dialog = new ImageEditDialog(owner, bytes, extension);
            var chosen = await Dialogs.ShowModal(dialog, owner);
            return chosen == true ? dialog.Result : null;
        }
    }
}

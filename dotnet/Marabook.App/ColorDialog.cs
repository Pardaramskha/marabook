using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE SÉLECTEUR DE COULEUR PERSONNALISÉE. Refait le 25/11/2026 (Rémi : « on devrait
    /// avoir plusieurs options ») — avant : un nuancier, trois curseurs RVB, un champ HEX.
    /// Désormais, pour la même couleur, quatre façons de la régler, toujours d'accord entre elles :
    ///  · le CARRÉ CHROMATIQUE (saturation en largeur, valeur en hauteur) et sa bande de teinte ;
    ///  · les CURSEURS d'une métrique au choix — RVB, CMJN ou HSL (la liste déroulante) —, chacun
    ///    doublé d'un champ où taper sa valeur ;
    ///  · le champ CODE : un HEX (« #5B67D8 », « 5B67D8 », « #ABC ») ou un RVB (« rgb(91, 103, 216) »,
    ///    « 91, 103, 216 ») tapé ou collé ;
    ///  · le NUANCIER de base, d'un clic.
    /// Pas de transparence : une couleur de Marabook est opaque. Les conversions sont celles du cœur
    /// (Model.ColorCodes). Rend « #RRGGBB » ou null ; la couleur rejoint la palette de l'auteur (Ask).
    /// La couleur est TENUE en teinte / saturation / valeur : un gris ou un noir ne perdent pas la
    /// teinte que l'on réglait. Les curseurs que l'on est en train de bouger ne sont pas relus depuis
    /// la couleur (un aller-retour CMJN ou HSL les ferait sauter sous la main).</summary>
    public class ColorDialog : Window
    {
        private const int ModeRgb = 0, ModeCmyk = 1, ModeHsl = 2;
        private static readonly string[] ModeNames = { "RVB", "CMJN", "HSL" };
        private static readonly string[][] ModeLabels = { new[] { "R", "V", "B" }, new[] { "C", "M", "J", "N" }, new[] { "T", "S", "L" } };
        private static readonly string[][] ModeTips =
        {
            new[] { "Rouge (0 à 255)", "Vert (0 à 255)", "Bleu (0 à 255)" },
            new[] { "Cyan (0 à 100 %)", "Magenta (0 à 100 %)", "Jaune (0 à 100 %)", "Noir (0 à 100 %)" },
            new[] { "Teinte (0 à 360°)", "Saturation (0 à 100 %)", "Luminosité (0 à 100 %)" }
        };
        private static int _lastMode; // la métrique choisie la dernière fois, pour la session

        private readonly Border _preview;
        private readonly TextBox _codeBox, _nameBox;
        private readonly ComboBox _modeBox;
        private readonly ChromaSquare _square;
        private readonly HueStrip _strip;
        private readonly Slider[] _sliders = new Slider[4];
        private readonly TextBox[] _fields = new TextBox[4];
        private readonly TextBlock[] _labels = new TextBlock[4];
        private readonly Control[] _rows = new Control[4];
        private double _hue, _saturation = 1, _value = 1; // la couleur tenue (TSV)
        private int _mode;
        private bool _accepted, _syncing;

        /// <summary>22/11 (la palette) : un champ NOM, facultatif — sans nom, la couleur s'affiche par son code.</summary>
        private ColorDialog(Window owner, string title, string startHex, string name, string okLabel)
        {
            Title = title;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), Width = 330 };

            // — Le carré chromatique et sa bande de teinte.
            _square = new ChromaSquare { Height = 168, Tag = "color-square" };
            _square.Picked += delegate(double saturation, double value) { _saturation = saturation; _value = value; Show(_square); };
            panel.Children.Add(_square);
            _strip = new HueStrip { Height = 16, Margin = new Thickness(0, 8, 0, 10), Tag = "color-hue" };
            _strip.Picked += delegate(double hue) { _hue = hue; Show(_strip); };
            panel.Children.Add(_strip);

            // — La métrique des curseurs.
            var modeRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var modeLabel = new TextBlock { Text = "Curseurs", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(modeLabel, Dock.Left);
            modeRow.Children.Add(modeLabel);
            _modeBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 96, Tag = "color-mode" };
            ToolTip.SetTip(_modeBox, "La métrique des curseurs : rouge-vert-bleu, cyan-magenta-jaune-noir, ou teinte-saturation-luminosité");
            foreach (var mode in ModeNames) _modeBox.Items.Add(mode);
            _mode = _lastMode;
            _modeBox.SelectedIndex = _mode;
            _modeBox.SelectionChanged += delegate
            {
                if (_modeBox.SelectedIndex < 0 || _modeBox.SelectedIndex == _mode) return;
                _mode = _lastMode = _modeBox.SelectedIndex;
                DressSliders();
                Show(null);
            };
            modeRow.Children.Add(_modeBox);
            panel.Children.Add(modeRow);
            for (var i = 0; i < 4; i++) panel.Children.Add(MakeSlider(i));

            // — Le code, et l'aperçu.
            var codeRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var codeLabel = new TextBlock { Text = "Code", Width = 40, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(codeLabel, Dock.Left);
            codeRow.Children.Add(codeLabel);
            _preview = new Border
            {
                Width = 46,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8, 0, 0, 0),
                Tag = "color-preview"
            };
            DockPanel.SetDock(_preview, Dock.Right);
            codeRow.Children.Add(_preview);
            _codeBox = new TextBox { Text = "#5B67D8", Watermark = "#RRGGBB ou R, V, B", Tag = "color-code" };
            ToolTip.SetTip(_codeBox, "Un code HEX (#5B67D8, 5B67D8, #ABC) ou RVB (rgb(91, 103, 216) ; 91, 103, 216)");
            _codeBox.TextChanged += delegate { if (!_syncing) SyncFromCode(); };
            // En quittant le champ : il reprend l'écriture canonique de la couleur tenue.
            _codeBox.LostFocus += delegate { _syncing = true; _codeBox.Text = Hex; _syncing = false; };
            codeRow.Children.Add(_codeBox);
            panel.Children.Add(codeRow);

            var nameRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var nameLabel = new TextBlock { Text = "Nom", Width = 40, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(nameLabel, Dock.Left);
            nameRow.Children.Add(nameLabel);
            _nameBox = new TextBox { Text = name ?? "", Watermark = "Facultatif", Tag = "color-name" };
            nameRow.Children.Add(_nameBox);
            panel.Children.Add(nameRow);

            // — Le nuancier de base.
            var grid = new WrapPanel { Margin = new Thickness(-2, 10, -2, 0) };
            string[] bases =
            {
                "#000000", "#404040", "#7F8C8D", "#BDC3C7", "#FFFFFF",
                "#C0392B", "#E74C3C", "#E67E22", "#F1C40F", "#C9A227",
                "#27AE60", "#2ECC71", "#16A085", "#1ABC9C", "#2980B9",
                "#3498DB", "#5B67D8", "#8E44AD", "#9B59B6", "#703C2F",
                "#FFF3A3", "#FFD9A8", "#D3F8D3", "#D0E8FF", "#FFD6E7"
            };
            foreach (var hex in bases)
            {
                var swatch = new Button
                {
                    Width = 21.5,
                    Height = 21.5,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(Ink.Parse(hex).ToColor()),
                    [ToolTip.TipProperty] = ColorPalette.Describe(hex)
                };
                var hexRef = hex;
                swatch.Click += delegate { SetColor(hexRef); };
                grid.Children.Add(swatch);
            }
            panel.Children.Add(grid);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = okLabel, IsDefault = true, MinWidth = 90 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); }; // IsCancel ne ferme pas la fenêtre sur Avalonia (28/09)
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, ok); // validation à droite, principale (30/09)
            panel.Children.Add(buttons);

            Content = panel;
            DressSliders();
            SetColor(ColorPalette.Normalize(startHex) ?? "#5B67D8");
        }

        /// <summary>Une rangée : le nom de la composante, son curseur, le champ où taper sa valeur.</summary>
        private Control MakeSlider(int index)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var caption = _labels[index] = new TextBlock { Width = 18, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            var field = _fields[index] = new TextBox { Width = 52, Margin = new Thickness(8, 0, 0, 0), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Tag = "color-field-" + index };
            DockPanel.SetDock(field, Dock.Right);
            row.Children.Add(field);
            var slider = _sliders[index] = new Slider { Minimum = 0, Maximum = 255, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Tag = "color-slider-" + index };
            slider.ValueChanged += delegate
            {
                if (_syncing) return;
                _syncing = true;
                field.Text = Math.Round(slider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _syncing = false;
                SyncFromSliders();
            };
            // La valeur tapée à la main : un entier dans les bornes du curseur ; le reste attend (on tape encore).
            field.TextChanged += delegate
            {
                if (_syncing) return;
                int typed;
                if (!int.TryParse((field.Text ?? "").Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out typed)) return;
                if (typed < slider.Minimum || typed > slider.Maximum) return;
                // PIÈGE : Avalonia lève TextChanged APRÈS coup — le champ que nous venons de remplir
                // nous-mêmes arrive ici, la garde levée. Une valeur déjà celle du curseur n'est pas une frappe :
                // la relire arrondirait la couleur (un CMJN en entiers ne rend pas tout le RVB).
                if (typed == (int)Math.Round(slider.Value)) return;
                _syncing = true;
                slider.Value = typed;
                _syncing = false;
                SyncFromSliders();
            };
            field.LostFocus += delegate
            {
                _syncing = true;
                field.Text = Math.Round(slider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _syncing = false;
            };
            row.Children.Add(slider);
            _rows[index] = row;
            return row;
        }

        /// <summary>Les curseurs prennent les noms et les bornes de la métrique choisie (le quatrième : le noir du CMJN).</summary>
        private void DressSliders()
        {
            _syncing = true;
            var labels = ModeLabels[_mode];
            for (var i = 0; i < 4; i++)
            {
                _rows[i].IsVisible = i < labels.Length;
                if (i >= labels.Length) continue;
                _labels[i].Text = labels[i];
                ToolTip.SetTip(_rows[i], ModeTips[_mode][i]);
                _sliders[i].Maximum = _mode == ModeRgb ? 255 : _mode == ModeHsl && i == 0 ? 360 : 100;
            }
            _syncing = false;
        }

        private string Hex
        {
            get
            {
                int r, g, b;
                ColorCodes.HsvToRgb(_hue, _saturation, _value, out r, out g, out b);
                return ColorCodes.Hex(r, g, b);
            }
        }

        /// <summary>La couleur tenue est montrée partout — sauf dans <paramref name="source"/>, ce que
        /// l'auteur est en train de manier (le champ où il tape, les curseurs qu'il bouge).</summary>
        private void Show(object source)
        {
            _syncing = true;
            int r, g, b;
            ColorCodes.HsvToRgb(_hue, _saturation, _value, out r, out g, out b);
            _preview.Background = new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
            _square.Show(_hue, _saturation, _value);
            _strip.Show(_hue);
            if (!ReferenceEquals(source, _codeBox)) _codeBox.Text = ColorCodes.Hex(r, g, b);
            if (!ReferenceEquals(source, _sliders))
            {
                var values = new double[4];
                if (_mode == ModeRgb) { values[0] = r; values[1] = g; values[2] = b; }
                else if (_mode == ModeCmyk) ColorCodes.RgbToCmyk(r, g, b, out values[0], out values[1], out values[2], out values[3]);
                else
                {
                    ColorCodes.RgbToHsl(r, g, b, out values[0], out values[1], out values[2]);
                    values[0] = _hue; // la teinte tenue : un gris garde celle que l'on réglait
                }
                for (var i = 0; i < ModeLabels[_mode].Length; i++)
                {
                    _sliders[i].Value = Math.Round(values[i]);
                    _fields[i].Text = Math.Round(values[i]).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            _syncing = false;
        }

        private void Hold(int r, int g, int b)
        {
            double h, s, v;
            ColorCodes.RgbToHsv(r, g, b, out h, out s, out v);
            if (s > 1e-6 && v > 1e-6) _hue = h; // un gris, un noir : la teinte tenue reste
            _saturation = s;
            _value = v;
        }

        private void SetColor(string hex)
        {
            int r, g, b;
            if (!ColorCodes.TryParse(hex, out r, out g, out b)) return;
            Hold(r, g, b);
            Show(null);
        }

        private void SyncFromSliders()
        {
            int r, g, b;
            if (_mode == ModeRgb) { r = (int)Math.Round(_sliders[0].Value); g = (int)Math.Round(_sliders[1].Value); b = (int)Math.Round(_sliders[2].Value); }
            else if (_mode == ModeCmyk) ColorCodes.CmykToRgb(_sliders[0].Value, _sliders[1].Value, _sliders[2].Value, _sliders[3].Value, out r, out g, out b);
            else
            {
                ColorCodes.HslToRgb(_sliders[0].Value, _sliders[1].Value, _sliders[2].Value, out r, out g, out b);
                _hue = _sliders[0].Value >= 360 ? 0 : _sliders[0].Value;
            }
            Hold(r, g, b);
            Show(_sliders);
        }

        private void SyncFromCode()
        {
            int r, g, b;
            if (!ColorCodes.TryParse(_codeBox.Text, out r, out g, out b)) return; // on tape encore
            // Le code que nous venons d'écrire nous-mêmes (TextChanged arrive après coup) : rien à relire —
            // sans quoi les curseurs que l'auteur tient seraient recalculés sous sa main.
            if (string.Equals(ColorCodes.Hex(r, g, b), Hex, StringComparison.Ordinal)) return;
            Hold(r, g, b);
            Show(_codeBox);
        }

        // — Sonde.
        internal static ColorDialog NewForProbe(Window owner, string hex) { return new ColorDialog(owner, "Nouvelle couleur", hex, null, "Ajouter"); }
        internal string HexForProbe { get { return Hex; } }
        internal void TypeCodeForProbe(string text) { _codeBox.Text = text; }
        internal string CodeForProbe { get { return _codeBox.Text; } }
        internal void SetModeForProbe(int mode) { _modeBox.SelectedIndex = mode; }
        internal int ShownSlidersForProbe { get { var count = 0; foreach (var row in _rows) if (row.IsVisible) count++; return count; } }
        internal string LabelsForProbe { get { var text = ""; for (var i = 0; i < 4; i++) if (_rows[i].IsVisible) text += _labels[i].Text; return text; } }
        internal void SetSliderForProbe(int index, double value) { _sliders[index].Value = value; }
        internal double SliderForProbe(int index) { return _sliders[index].Value; }
        internal void TypeFieldForProbe(int index, string text) { _fields[index].Text = text; }
        internal string FieldForProbe(int index) { return _fields[index].Text; }
        internal void PickSquareForProbe(double saturation, double value) { _square.PickForProbe(saturation, value); }
        internal void PickHueForProbe(double hue) { _strip.PickForProbe(hue); }
        internal Color PreviewForProbe { get { var brush = _preview.Background as ISolidColorBrush; return brush == null ? Colors.Transparent : brush.Color; } }

        /// <summary>« Nouvelle couleur… » : la couleur choisie rejoint la PALETTE de l'auteur
        /// (Model.ColorPalette — avec son nom s'il en donne un), et son code est rendu.
        /// null si annulé.</summary>
        public static async Task<string> Ask(Window owner)
        {
            var picked = await Edit(owner, "Nouvelle couleur", null, null, "Ajouter");
            if (picked == null) return null;
            ColorPalette.Add(picked[0], picked[1]);
            Marabook.Settings.AppSettings.Save();
            return picked[0];
        }

        /// <summary>Le dialogue seul : { code, nom } — rien n'est rangé. null si annulé.</summary>
        public static async Task<string[]> Edit(Window owner, string title, string hex, string name, string okLabel)
        {
            var dialog = new ColorDialog(owner, title, hex, name, okLabel);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return null;
            var text = ColorPalette.Normalize(dialog.Hex);
            return text == null ? null : new[] { text, (dialog._nameBox.Text ?? "").Trim() };
        }
    }

    /// <summary>LE CARRÉ CHROMATIQUE (25/11) : pour une teinte, toutes ses nuances — la saturation de
    /// gauche (blanc) à droite (la teinte pure), la valeur de haut (claire) en bas (noir). On clique
    /// ou l'on tire ; un anneau marque la couleur tenue.</summary>
    internal sealed class ChromaSquare : Control
    {
        private double _hue, _saturation = 1, _value = 1;
        public event Action<double, double> Picked;

        public ChromaSquare()
        {
            Cursor = new Cursor(StandardCursorType.Cross);
            PointerPressed += delegate(object sender, PointerPressedEventArgs e) { e.Pointer.Capture(this); Pick(e.GetPosition(this)); e.Handled = true; };
            PointerMoved += delegate(object sender, PointerEventArgs e) { if (ReferenceEquals(e.Pointer.Captured, this)) Pick(e.GetPosition(this)); };
            PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null); };
        }

        public void Show(double hue, double saturation, double value)
        {
            _hue = hue; _saturation = saturation; _value = value;
            InvalidateVisual();
        }

        private void Pick(Point at)
        {
            if (Bounds.Width < 2 || Bounds.Height < 2) return;
            PickForProbe(at.X / Bounds.Width, 1 - at.Y / Bounds.Height);
        }

        internal void PickForProbe(double saturation, double value)
        {
            _saturation = Math.Max(0, Math.Min(1, saturation));
            _value = Math.Max(0, Math.Min(1, value));
            InvalidateVisual();
            var handler = Picked;
            if (handler != null) handler(_saturation, _value);
        }

        public override void Render(DrawingContext context)
        {
            var box = new Rect(Bounds.Size);
            if (box.Width < 2 || box.Height < 2) return;
            int r, g, b;
            ColorCodes.HsvToRgb(_hue, 1, 1, out r, out g, out b);
            var pure = Color.FromRgb((byte)r, (byte)g, (byte)b);
            var across = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            across.GradientStops.Add(new GradientStop(Colors.White, 0));
            across.GradientStops.Add(new GradientStop(pure, 1));
            var down = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            down.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            down.GradientStops.Add(new GradientStop(Colors.Black, 1));
            context.DrawRectangle(across, null, box, 5, 5);
            context.DrawRectangle(down, new Pen(Chrome.BorderStrong, 1), box.Deflate(0.5), 5, 5);
            var at = new Point(_saturation * box.Width, (1 - _value) * box.Height);
            context.DrawEllipse(null, new Pen(Brushes.Black, 1), at, 6.5, 6.5);
            context.DrawEllipse(null, new Pen(Brushes.White, 1.6), at, 5.5, 5.5);
        }
    }

    /// <summary>LA BANDE DE TEINTE (25/11) : le tour du cercle chromatique, de 0 à 360°, à plat.</summary>
    internal sealed class HueStrip : Control
    {
        private double _hue;
        public event Action<double> Picked;

        public HueStrip()
        {
            Cursor = new Cursor(StandardCursorType.Hand);
            PointerPressed += delegate(object sender, PointerPressedEventArgs e) { e.Pointer.Capture(this); Pick(e.GetPosition(this)); e.Handled = true; };
            PointerMoved += delegate(object sender, PointerEventArgs e) { if (ReferenceEquals(e.Pointer.Captured, this)) Pick(e.GetPosition(this)); };
            PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { if (ReferenceEquals(e.Pointer.Captured, this)) e.Pointer.Capture(null); };
        }

        public void Show(double hue) { _hue = hue; InvalidateVisual(); }

        private void Pick(Point at)
        {
            if (Bounds.Width < 2) return;
            PickForProbe(at.X / Bounds.Width * 360);
        }

        internal void PickForProbe(double hue)
        {
            _hue = Math.Max(0, Math.Min(359.999, hue));
            InvalidateVisual();
            var handler = Picked;
            if (handler != null) handler(_hue);
        }

        public override void Render(DrawingContext context)
        {
            var box = new Rect(Bounds.Size);
            if (box.Width < 2 || box.Height < 2) return;
            var rainbow = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            for (var step = 0; step <= 6; step++)
            {
                int r, g, b;
                ColorCodes.HsvToRgb(step * 60, 1, 1, out r, out g, out b);
                rainbow.GradientStops.Add(new GradientStop(Color.FromRgb((byte)r, (byte)g, (byte)b), step / 6.0));
            }
            context.DrawRectangle(rainbow, new Pen(Chrome.BorderStrong, 1), box.Deflate(0.5), 4, 4);
            var x = Math.Max(2, Math.Min(box.Width - 2, _hue / 360 * box.Width));
            context.DrawRectangle(null, new Pen(Brushes.Black, 1), new Rect(x - 3.5, -0.5, 7, box.Height + 1), 2, 2);
            context.DrawRectangle(null, new Pen(Brushes.White, 1.6), new Rect(x - 2.5, 0.5, 5, box.Height - 1), 2, 2);
        }
    }
}

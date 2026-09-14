using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace UniversSale.View
{
    /// <summary>Champ numérique « à la Adobe » : petits boutons ▲▼ verticaux
    /// à gauche d'un champ de valeur. Entrée ou perte de focus applique ;
    /// les flèches répètent au maintien.</summary>
    public class SpinnerField : StackPanel
    {
        private readonly TextBox _box;
        private double _value;

        public double Minimum;
        public double Maximum = 100;
        public double Step = 1;

        public event Action<double> ValueChanged;

        public SpinnerField(double value, double minimum, double maximum, double step,
            string tooltip)
        {
            Orientation = Orientation.Horizontal;
            Minimum = minimum;
            Maximum = maximum;
            Step = step;
            _value = Clamp(value);

            var spinner = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var up = new RepeatButton
            {
                Content = "▲",
                FontSize = 6,
                Width = 15,
                Height = 11,
                Padding = new Thickness(0, -1, 0, 0),
                Focusable = false
            };
            up.Click += delegate { SetValue(_value + Step, true); };
            var down = new RepeatButton
            {
                Content = "▼",
                FontSize = 6,
                Width = 15,
                Height = 11,
                Padding = new Thickness(0, -1, 0, 0),
                Focusable = false
            };
            down.Click += delegate { SetValue(_value - Step, true); };
            spinner.Children.Add(up);
            spinner.Children.Add(down);
            Children.Add(spinner);

            _box = new TextBox
            {
                Width = 44,
                Margin = new Thickness(1, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Text = Format(_value),
                ToolTip = tooltip
            };
            _box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                CommitText();
            };
            _box.LostKeyboardFocus += delegate { CommitText(); };
            Children.Add(_box);
        }

        /// <summary>La largeur de la zone de saisie (44 par défaut) — à élargir
        /// pour les grands nombres (objectif de taille d'un livre, b48).</summary>
        public double BoxWidth
        {
            get { return _box.Width; }
            set { _box.Width = value; }
        }

        public double Value
        {
            get { return _value; }
            set { SetValue(value, false); }
        }

        private void CommitText()
        {
            double parsed;
            if (double.TryParse(_box.Text.Trim().Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                SetValue(parsed, true);
            else
                _box.Text = Format(_value); // saisie illisible : on revient
        }

        private void SetValue(double value, bool notify)
        {
            var clamped = Clamp(value);
            var changed = Math.Abs(clamped - _value) > 0.0001;
            _value = clamped;
            _box.Text = Format(_value);
            if (notify && changed)
            {
                var handler = ValueChanged;
                if (handler != null) handler(_value);
            }
        }

        private double Clamp(double value)
        {
            return Math.Max(Minimum, Math.Min(Maximum, value));
        }

        private static string Format(double value)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }
    }
}

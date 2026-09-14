using System.Windows;
using System.Windows.Controls;

namespace UniversSale.View
{
    /// <summary>« Lancer un sprint » (b48) : une durée — 15, 25, 45 minutes
    /// ou libre — et un objectif de mots (0 = sans). Le dernier choix est
    /// proposé au suivant. Rend null si annulé.</summary>
    public class SprintDialog : Window
    {
        public class Choice
        {
            public int Minutes; // 0 = libre
            public int Goal;    // 0 = sans objectif
        }

        private static int _lastMinutes = 25, _lastGoal = 500, _lastCustom = 60;

        private readonly RadioButton[] _durations;
        private readonly RadioButton _custom;   // « Personnalisé » + minutes (14/09)
        private readonly SpinnerField _customMinutes;
        private readonly SpinnerField _goal;
        private bool _accepted;

        private SprintDialog(Window owner)
        {
            Title = "Lancer un sprint";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 320 };
            panel.Children.Add(Label("Durée :"));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var minutes = new[] { 15, 25, 45, 0 };
            var preset = false;
            _durations = new RadioButton[minutes.Length];
            for (var i = 0; i < minutes.Length; i++)
            {
                _durations[i] = new RadioButton
                {
                    Content = minutes[i] == 0 ? "Libre" : minutes[i] + " min",
                    Tag = minutes[i],
                    Margin = new Thickness(0, 0, 14, 0),
                    IsChecked = minutes[i] == _lastMinutes,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (minutes[i] == _lastMinutes) preset = true;
                row.Children.Add(_durations[i]);
            }
            panel.Children.Add(row);
            // La durée personnalisée : un bouton radio de plus, avec ses minutes.
            var customRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            _custom = new RadioButton { Content = "Personnalisé :", IsChecked = !preset, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            customRow.Children.Add(_custom);
            _customMinutes = new SpinnerField(_lastCustom, 1, 480, 5, "La durée du sprint, en minutes (1 à 480)");
            _customMinutes.VerticalAlignment = VerticalAlignment.Center;
            customRow.Children.Add(_customMinutes);
            customRow.Children.Add(new TextBlock { Text = "min", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            panel.Children.Add(customRow);

            panel.Children.Add(Label("Objectif de mots (0 = sans objectif) :"));
            _goal = new SpinnerField(_lastGoal, 0, 20000, 50, "Les mots nets à écrire pendant le sprint");
            _goal.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(_goal);
            panel.Children.Add(new TextBlock
            {
                Text = "Une pastille en haut de la zone d'écriture suit le temps et les mots ; "
                    + "à la fin, le sprint est consigné dans le Journal perso.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 340,
                Margin = new Thickness(0, 8, 0, 0)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = new Button { Content = "Lancer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 10, 0, 4) };
        }

        public static Choice Ask(Window owner)
        {
            var dialog = new SprintDialog(owner);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return null;
            var minutes = 0;
            foreach (var radio in dialog._durations)
                if (radio.IsChecked == true) minutes = (int)radio.Tag;
            if (dialog._custom.IsChecked == true)
            {
                minutes = System.Math.Max(1, (int)System.Math.Round(dialog._customMinutes.Value));
                _lastCustom = minutes;
            }
            _lastMinutes = minutes;
            _lastGoal = System.Math.Max(0, (int)System.Math.Round(dialog._goal.Value));
            return new Choice { Minutes = minutes, Goal = _lastGoal };
        }
    }
}

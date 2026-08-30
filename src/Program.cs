using System;
using System.IO;
using System.Windows;

namespace UniversSale
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Settings.AppSettings.Load();
            // Tooltips réactifs façon web : apparition rapide, ré-apparition
            // immédiate en balayant une barre d'outils (fenêtre BetweenShow).
            System.Windows.Controls.ToolTipService.InitialShowDelayProperty
                .OverrideMetadata(typeof(System.Windows.DependencyObject),
                    new System.Windows.FrameworkPropertyMetadata(180));
            System.Windows.Controls.ToolTipService.BetweenShowDelayProperty
                .OverrideMetadata(typeof(System.Windows.DependencyObject),
                    new System.Windows.FrameworkPropertyMetadata(150));
            // Ancrés SOUS la cible (et non au curseur) pour que la flèche du
            // template (Theme) pointe vers l'élément concerné.
            System.Windows.Controls.ToolTipService.PlacementProperty
                .OverrideMetadata(typeof(System.Windows.DependencyObject),
                    new System.Windows.FrameworkPropertyMetadata(
                        System.Windows.Controls.Primitives.PlacementMode.Bottom));
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ToolTip),
                System.Windows.Controls.ToolTip.OpenedEvent,
                new RoutedEventHandler(OnToolTipOpened));
            View.Chrome.Toggle(Settings.AppSettings.DarkTheme);
            var application = new Application();
            View.Theme.Apply(application);
            var window = new MainWindow();
            if (args.Length > 0 && File.Exists(args[0]))
                window.OpenFile(args[0]);
            application.Run(window);
        }

        /// <summary>Centre la bulle sur sa cible (le placement Bottom aligne
        /// les bords gauches) et oriente la flèche : un popup retourné
        /// au-dessus de la cible (bord d'écran) pointe vers le bas.</summary>
        private static void OnToolTipOpened(object sender, RoutedEventArgs e)
        {
            var tip = sender as System.Windows.Controls.ToolTip;
            var target = tip == null ? null : tip.PlacementTarget as FrameworkElement;
            if (target == null) return;
            try
            {
                // À GAUCHE de la cible (les onglets du rail, b39) : centrée
                // verticalement, la flèche pointe vers la droite.
                if (tip.Placement == System.Windows.Controls.Primitives.PlacementMode.Left)
                {
                    tip.VerticalOffset = (target.ActualHeight - tip.ActualHeight) / 2;
                    tip.Tag = "beside";
                    return;
                }
                tip.HorizontalOffset = (target.ActualWidth - tip.ActualWidth) / 2;
                var tipTop = tip.PointToScreen(new Point(0, 0)).Y;
                var targetTop = target.PointToScreen(new Point(0, 0)).Y;
                tip.Tag = tipTop < targetTop ? "above" : null;
            }
            catch { tip.Tag = null; }
        }
    }
}

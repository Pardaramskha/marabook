using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;

namespace Marabook.App
{
    /// <summary>L'ouverture des dialogues (porté de View/Dialogs.cs). Sur
    /// Avalonia le dialogue modal est asynchrone : ShowModal attend la
    /// fermeture et rend le résultat posé par Close(result). Les parades
    /// WPF contre le « flash » d'une fenêtre à SizeToContent n'ont plus
    /// d'objet (Avalonia mesure le contenu avant de montrer la fenêtre).</summary>
    public static class Dialogs
    {
        public static Task<bool?> ShowModal(Window window, Window owner)
        {
            if (owner == null) owner = App.MainWindowOrNull;
            FitToScreen(window, owner);
            if (owner == null) { window.Show(); return Task.FromResult<bool?>(null); }
            return window.ShowDialog<bool?>(owner);
        }

        /// <summary>La hauteur de cadre (barre de titre) que la zone de
        /// travail de l'écran ne compte pas dans Height : retranchée pour
        /// que la fenêtre entière tienne.</summary>
        private const double FrameAllowance = 40;

        /// <summary>Les fenêtres déjà ancrées à leur écran (une seule fois).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> Anchored =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Window, object>();

        /// <summary>Une fenêtre qui TIENT DANS L'ÉCRAN (18/10, 1.0.6) : sur un
        /// petit écran, le dialogue d'entrée du dictionnaire et les autres
        /// fenêtres qui s'étirent à la verticale dépassaient par le bas, à
        /// repositionner à la main. La hauteur est plafonnée à la zone de
        /// travail de l'écran de la fenêtre parente ; une fenêtre dimensionnée
        /// par son contenu reçoit un ScrollViewer autour de lui, et le reste
        /// défile. À l'ouverture et à chaque changement de taille, la fenêtre
        /// est ramenée dans l'écran si elle en sort.</summary>
        public static void FitToScreen(Window window, Window owner)
        {
            if (window == null) return;
            Screen screen = null;
            try
            {
                var reference = owner ?? window;
                screen = reference.Screens.ScreenFromWindow(reference) ?? reference.Screens.Primary;
            }
            catch (Exception) { }
            if (screen == null) return;
            var scaling = screen.Scaling > 0.5 ? screen.Scaling : 1.0;
            Fit(window, screen.WorkingArea.Height / scaling - FrameAllowance);
            object anchored;
            if (!Anchored.TryGetValue(window, out anchored))
            {
                Anchored.Add(window, string.Empty);
                window.Opened += delegate { KeepOnScreen(window); };
                window.SizeChanged += delegate { if (window.IsVisible) KeepOnScreen(window); };
            }
        }

        /// <summary>La hauteur disponible (px logiques) pour une fenêtre sur
        /// l'écran de <paramref name="owner"/> : la zone de travail moins le
        /// cadre. 0 si l'écran est inconnu. (Les Préférences divisaient la
        /// zone par RenderScaling : sur mac, Avalonia la donne déjà en points
        /// — la fenêtre sortait à mi-hauteur, 24/10.)</summary>
        public static double AvailableHeight(Window owner)
        {
            var screen = ScreenOf(owner);
            if (screen == null) return 0;
            return screen.WorkingArea.Height / (screen.Scaling > 0.5 ? screen.Scaling : 1.0) - FrameAllowance;
        }

        public static double AvailableWidth(Window owner)
        {
            var screen = ScreenOf(owner);
            if (screen == null) return 0;
            return screen.WorkingArea.Width / (screen.Scaling > 0.5 ? screen.Scaling : 1.0) - 40;
        }

        private static Screen ScreenOf(Window owner)
        {
            try
            {
                if (owner == null) owner = App.MainWindowOrNull;
                if (owner == null) return null;
                return owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Le plafond de hauteur (px logiques) posé sur la fenêtre,
        /// et le contenu rendu défilable quand la fenêtre se dimensionne sur
        /// lui. Public pour la sonde.</summary>
        public static void Fit(Window window, double availableHeight)
        {
            if (window == null || availableHeight < 200) return;
            var sizesToHeight = window.SizeToContent == SizeToContent.Height
                || window.SizeToContent == SizeToContent.WidthAndHeight;
            if (sizesToHeight)
            {
                var content = window.Content as Control;
                if (content != null && !(content is ScrollViewer))
                {
                    window.Content = null;
                    window.Content = new ScrollViewer
                    {
                        Content = content,
                        Tag = "fit-scroll",
                        [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                        [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled
                    };
                }
            }
            else if (!double.IsNaN(window.Height) && window.Height > availableHeight)
                window.Height = availableHeight;
            if (window.MinHeight > availableHeight) window.MinHeight = availableHeight;
            if (double.IsInfinity(window.MaxHeight) || double.IsNaN(window.MaxHeight) || window.MaxHeight > availableHeight)
                window.MaxHeight = availableHeight;
        }

        /// <summary>Ramène la fenêtre dans la zone de travail de son écran
        /// (pixels) si elle en déborde.</summary>
        public static void KeepOnScreen(Window window)
        {
            try
            {
                var screen = window.Screens.ScreenFromWindow(window);
                if (screen == null) return;
                var area = screen.WorkingArea;
                var scaling = screen.Scaling > 0.5 ? screen.Scaling : 1.0;
                var frame = window.FrameSize ?? window.Bounds.Size;
                var width = (int)Math.Ceiling(frame.Width * scaling);
                var height = (int)Math.Ceiling(frame.Height * scaling);
                var x = Math.Max(area.X, Math.Min(window.Position.X, area.Right - width));
                var y = Math.Max(area.Y, Math.Min(window.Position.Y, area.Bottom - height));
                if (x != window.Position.X || y != window.Position.Y) window.Position = new PixelPoint(x, y);
            }
            catch (Exception) { }
        }

        /// <summary>La règle des dialogues (30/09) : le bouton de VALIDATION est
        /// principal (couleur pleine) et le plus à DROITE ; les autres (Annuler,
        /// Fermer, options) restent en contour à sa gauche, 8 px entre chaque.
        /// À appeler une fois la rangée remplie.</summary>
        public static void Arrange(Panel row, Button validation)
        {
            if (row == null || validation == null) return;
            if (row.Children.Contains(validation)) row.Children.Remove(validation);
            row.Children.Add(validation);
            if (!validation.Classes.Contains(Theme.Primary)) validation.Classes.Add(Theme.Primary);
            validation.Focusable = true;
            for (var i = 0; i < row.Children.Count; i++)
            {
                var control = row.Children[i] as Control;
                if (control == null) continue;
                control.Margin = new Thickness(i == 0 ? 0 : 8, control.Margin.Top, 0, control.Margin.Bottom);
                var button = control as Button;
                if (button != null && button != validation && button.Classes.Contains(Theme.Calm)) button.Classes.Remove(Theme.Calm);
            }
        }
    }
}

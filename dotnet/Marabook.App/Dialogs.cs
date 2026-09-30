using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

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
            if (owner == null) { window.Show(); return Task.FromResult<bool?>(null); }
            return window.ShowDialog<bool?>(owner);
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

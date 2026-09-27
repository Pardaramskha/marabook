using System.Threading.Tasks;
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
    }
}

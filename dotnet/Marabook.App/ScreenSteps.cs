using System;
using Avalonia.Controls;

namespace Marabook.App
{
    /// <summary>LES ÉCRANS BASCULÉS (2.0.0, 21/11/2026, Rémi) : une vue qui
    /// remplace son contenu par un autre écran — le Tableau à la place des
    /// cartes, la liste des fiches à la place des tuiles, le calendrier ou la
    /// liste d'une frise, la vue planétaire d'une carte — le dit ici. La
    /// coquille en fait un pas de son historique : le bouton « Précédent » de
    /// la souris défait la bascule avant de quitter le panneau, « Suivant » la
    /// refait. Seules les bascules faites À LA MAIN s'annoncent ; une vue qui
    /// retrouve l'écran retenu d'une session à l'autre ne dit rien.</summary>
    internal static class ScreenSteps
    {
        /// <summary>(la vue, défaire, refaire) — posé par la fenêtre principale.</summary>
        public static Action<Control, Action, Action> Recorder;

        public static void Record(Control view, Action back, Action forward)
        {
            var recorder = Recorder;
            if (recorder != null) recorder(view, back, forward);
        }
    }
}

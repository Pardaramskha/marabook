using System;
using Avalonia;
using Avalonia.Controls;

namespace Marabook.App
{
    /// <summary>LE JOURNAL PERSO EN FENÊTRE (2.0.0, 25/11/2026, Rémi) : le Journal n'est plus une
    /// feuille qui se glisse sur la zone centrale et en chasse la vue en cours — c'est une fenêtre
    /// posée par-dessus l'application, comme les Préférences : ce qui était ouvert reste ouvert
    /// dessous, voilé. Elle se ferme par sa croix ou par Échap (ModalChrome s'en charge, comme pour
    /// tout dialogue). Pour l'instant une fenêtre SIMPLE — le panneau droit des dialogues ; sa
    /// forme propre viendra plus tard.
    /// La fenêtre ne fait qu'héberger la vue du Journal (JournalView), que la coquille garde d'une
    /// ouverture à l'autre : à la fermeture, la vue lui est rendue.</summary>
    internal sealed class JournalDialog : Window
    {
        public JournalDialog(Window owner, JournalView view)
        {
            Title = "Journal perso";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var availableWidth = Dialogs.AvailableWidth(owner);
            var availableHeight = Dialogs.AvailableHeight(owner);
            Width = Math.Min(980, Math.Max(520, availableWidth > 0 ? availableWidth - 80 : 900));
            Height = Math.Min(820, Math.Max(420, availableHeight > 0 ? availableHeight - 60 : 680));
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            // La vue est posée dans un CADRE à nous, pas directement dans la fenêtre : l'accessibilité
            // (zoom de l'interface) et Dialogs.Fit enveloppent le contenu d'une fenêtre — vider
            // Content à la fermeture laisserait la vue prisonnière de leur enveloppe, et la
            // réouverture lèverait « The Control already has a parent ». Le cadre, lui, la rend.
            var holder = new Border { Child = view, Tag = "journal-holder" };
            Content = holder;
            Closed += delegate { holder.Child = null; }; // la vue retourne à la coquille, prête pour la prochaine ouverture
        }
    }
}

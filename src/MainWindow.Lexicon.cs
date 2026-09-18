using System;
using System.Windows;
using System.Windows.Controls;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook
{
    /// <summary>Le panneau LEXIQUE de la colonne de droite (18/09) : la
    /// définition d'un mot du dictionnaire personnel. Ouvert le temps d'une
    /// définition (clic droit › « Afficher la définition »), ou épinglé au
    /// rail pour de bon — depuis le panneau, ou le menu options du
    /// Dictionnaire (réglage persisté).</summary>
    public partial class MainWindow
    {
        private View.LexiconPanel _lexiconPanel;
        private Border _lexiconHost;
        private bool _lexiconShown; // ouvert sans épingle, jusqu'à la croix

        /// <summary>L'onglet Lexique existe : épinglé, ou ouvert pour une définition.</summary>
        private bool HasLexiconPanel
        {
            get { return AppSettings.LexiconPinned || _lexiconShown; }
        }

        private void BuildLexiconPanel(Grid grid)
        {
            _lexiconPanel = new View.LexiconPanel();
            _lexiconPanel.SetPinned(AppSettings.LexiconPinned);
            _lexiconPanel.PinToggled += SetLexiconPinned;
            _lexiconPanel.CloseRequested += CloseLexicon;
            _lexiconPanel.EditRequested += EditLexiconEntry;
            _lexiconHost = new Border { Child = _lexiconPanel };
            Grid.SetColumn(_lexiconHost, 4);
            grid.Children.Add(_lexiconHost);
        }

        /// <summary>Clic droit sur un mot du dictionnaire personnel : le
        /// panneau montre l'entrée et prend la colonne.</summary>
        private void ShowDefinition(LexiconEntry entry, bool projectScope)
        {
            if (_project == null || entry == null) return;
            _lexiconPanel.Show(entry, projectScope);
            _lexiconShown = true;
            SetRightPanel(RightPanel.Lexicon);
            UpdateRail();
        }

        /// <summary>L'épingle, depuis le panneau ou le Dictionnaire.</summary>
        private void SetLexiconPinned(bool pinned)
        {
            AppSettings.LexiconPinned = pinned;
            AppSettings.Save();
            _lexiconPanel.SetPinned(pinned);
            if (pinned && _project != null && AppSettings.RightPanel != RightPanel.Lexicon
                && _dictionaryView.Visibility == Visibility.Visible)
                SetRightPanel(RightPanel.Lexicon); // épinglé depuis le Dictionnaire : on le voit
            else if (!pinned && !_lexiconShown && AppSettings.RightPanel == RightPanel.Lexicon)
                SetRightPanel(RightPanel.Inspector);
            else
                ApplyPanelVisibility();
        }

        /// <summary>La croix : sans épingle, l'onglet s'en va avec le panneau.</summary>
        private void CloseLexicon()
        {
            _lexiconShown = false;
            if (AppSettings.RightPanel == RightPanel.Lexicon) SetRightPanel(RightPanel.Inspector);
            ApplyPanelVisibility();
        }

        /// <summary>Le crayon du panneau : le dialogue d'entrée du
        /// Dictionnaire, qui prévient la coquille (MarkDirty, correcteur).</summary>
        private void EditLexiconEntry(LexiconEntry entry, bool projectScope)
        {
            if (_project == null) return;
            var scope = projectScope;
            var edited = _dictionaryView.Edit(entry, ref scope);
            if (edited == null) return;
            _lexiconPanel.Show(edited, scope);
        }
    }
}

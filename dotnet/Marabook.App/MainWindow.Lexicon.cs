using System;
using System.Collections.Generic;
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
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Le panneau LEXIQUE de la colonne de droite (18/09) : la
    /// définition d'un mot du dictionnaire personnel. Ouvert le temps d'une
    /// définition (clic droit › « Afficher la définition »), ou épinglé au
    /// rail pour de bon — depuis le panneau, ou le menu options du
    /// Dictionnaire (réglage persisté).</summary>
    public partial class MainWindow
    {
        private LexiconPanel _lexiconPanel;
        private Border _lexiconHost;
        private bool _lexiconShown; // ouvert sans épingle, jusqu'à la croix

        /// <summary>L'onglet Lexique existe : épinglé, ou ouvert pour une définition.</summary>
        private bool HasLexiconPanel
        {
            get { return AppSettings.LexiconPinned || _lexiconShown; }
        }

        private void BuildLexiconPanel(Grid grid)
        {
            _lexiconPanel = new LexiconPanel();
            _lexiconPanel.SetPinned(AppSettings.LexiconPinned);
            _lexiconPanel.PinToggled += SetLexiconPinned;
            _lexiconPanel.CloseRequested += CloseLexicon;
            _lexiconPanel.EditRequested += EditLexiconEntry;
            _lexiconPanel.EntriesProvider = delegate
            {
                var entries = new List<KeyValuePair<LexiconEntry, bool>>();
                if (_project != null) foreach (var entry in _project.Lexicon) entries.Add(new KeyValuePair<LexiconEntry, bool>(entry, true));
                foreach (var entry in AppSettings.Lexicon) entries.Add(new KeyValuePair<LexiconEntry, bool>(entry, false));
                return entries;
            };
            _lexiconHost = new Border { Child = _lexiconPanel };
            Grid.SetColumn(_lexiconHost, 4);
            grid.Children.Add(_lexiconHost);
        }

        /// <summary>Clic droit sur un mot du dictionnaire personnel : le
        /// panneau montre l'entrée et prend la colonne.</summary>
        private MenuItem _conjugatorMenu;
        private MenuItem _readAloudMenu;

        /// <summary>Édition › Lire à voix haute (21/11) : la voix du système lit
        /// la sélection, ou la suite de l'écrit depuis le curseur ; la même
        /// commande l'arrête.</summary>
        private void ToggleReadAloud()
        {
            if (ReadAloud.IsReading) { ReadAloud.Stop(); return; }
            if (_editor == null || !TextActive()) return;
            var prepared = Correction.ReadAloudText.Prepare(_editor.TextToRead());
            var problem = ReadAloud.Start(prepared);
            if (problem != null) { ShowToast(null, "Lecture à voix haute", null, problem, 5); return; }
            ShowToast(null, "Lecture à voix haute", _current == null ? null : _current.Title,
                Correction.ReadAloudText.Duration(prepared) + " — Édition › « Arrêter la lecture » pour l'interrompre.", 4);
        }

        private void UpdateReadAloudMenu()
        {
            if (_readAloudMenu != null) _readAloudMenu.Header = ReadAloud.IsReading ? "Arrêter la lecture" : "Lire à voix haute";
        }

        internal string ReadAloudHeaderForProbe { get { return _readAloudMenu == null ? null : _readAloudMenu.Header as string; } }
        internal void ToggleReadAloudForProbe() { ToggleReadAloud(); UpdateReadAloudMenu(); }

        /// <summary>Le conjugueur (20/11) : une fenêtre à côté du texte, sur
        /// ce mot — ou vide, à remplir.</summary>
        private void OpenConjugator(string word)
        {
            ConjugatorDialog.Open(this, _editor == null ? null : _editor.GrammarBridge, word);
        }

        internal ConjugatorDialog OpenConjugatorForProbe(string word) { return ConjugatorDialog.Open(this, _editor == null ? null : _editor.GrammarBridge, word); }

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
                && _dictionaryView.IsVisible)
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
        private async void EditLexiconEntry(LexiconEntry entry, bool projectScope)
        {
            if (_project == null) return;
            var choice = await _dictionaryView.Edit(entry, projectScope);
            if (choice == null) return;
            _lexiconPanel.Show(choice.Entry, choice.ProjectScope);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Marabook.History;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LES SCÈNES dans la coquille (2.0.0, 21/11/2026) : le panneau
    /// « Scène » du rail suit le curseur de l'écrit ouvert ; la fiche d'une
    /// scène s'ouvre depuis le rail ou depuis le Tableau ; tout passe par
    /// l'annulation.</summary>
    public partial class MainWindow
    {
        private ScenePanel _scenePanel;

        private Control BuildScenePanel()
        {
            _scenePanel = new ScenePanel { IsVisible = false };
            _scenePanel.EditRequested += delegate(BinderItem item, int scene) { var ignored = EditScene(item, scene); };
            _scenePanel.GoToParagraph += delegate(int paragraph)
            {
                if (_current == null || InspectedItem != _current || !_editor.IsVisible) return;
                _editor.Composed.PlaceCaret(paragraph, 0, false);
                _editor.FocusEditor();
            };
            _scenePanel.Navigate += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _scenePanel.OrphansChanged += delegate(BinderItem item, List<SceneMeta> before)
            {
                _history.Push(new SceneEditAction(item, before));
                MarkDirty();
                RefreshOutliners();
            };
            // Le curseur change de scène, un séparateur est posé ou retiré : la fiche suit.
            _editor.Composed.SelectionStateChanged += delegate
            {
                if (!_scenePanel.IsVisible || _current == null || InspectedItem != _current) return;
                _scenePanel.Follow(CaretParagraph());
            };
            return _scenePanel;
        }

        private int CaretParagraph()
        {
            if (_current == null || _current.Kind != ItemKind.Text || !_editor.IsVisible) return 0;
            int paragraph, offset;
            _editor.Composed.GetCaret(out paragraph, out offset);
            return paragraph;
        }

        /// <summary>« Scène » se montre pour un écrit (pas une page d'éditeur) ;
        /// pour celui qui est ouvert, elle suit le curseur.</summary>
        private void UpdateScenePanel()
        {
            var item = InspectedItem;
            var isText = item != null && item.Kind == ItemKind.Text && !item.IsExtraPage;
            _scenePanel.IsVisible = isText;
            if (!isText) return;
            _scenePanel.Load(_project, item, item == _current ? CaretParagraph() : 0);
        }

        /// <summary>Les fiches de scène ont changé : le rail et le Tableau se relisent.</summary>
        private void ScenesChanged()
        {
            if (_scenePanel.IsVisible) _scenePanel.Refresh();
            RefreshOutliners();
        }

        private void RefreshOutliners()
        {
            if (_corkboard.IsVisible) _corkboard.RefreshOutliner();
            if (_bookView.IsVisible) _bookView.RefreshOutliner();
        }

        /// <summary>Ouvre la fiche d'une scène ; rend vrai si elle a changé.</summary>
        internal async Task<bool> EditScene(BinderItem item, int index)
        {
            if (item == null || item.Kind != ItemKind.Text) return false;
            CommitActive(); // le texte ouvert est à jour avant d'être découpé
            var before = Scenes.CloneAll(item.Scenes);
            var scenes = Scenes.Of(item);
            if (index < 0 || index >= scenes.Count) return false;
            var scene = scenes[index];
            var result = await SceneDialog.Ask(this, _project, item, scene, scenes.Count);
            if (result == null) return false;
            var at = item.Scenes.IndexOf(scene.Meta);
            if (at < 0) return false;
            result.Anchor = scene.Opening;
            item.Scenes[at] = result;
            _history.Push(new SceneEditAction(item, before));
            MarkDirty();
            ScenesChanged();
            return true;
        }

        /// <summary>Ouvre un écrit, le curseur au début de ce paragraphe (une scène du Tableau).</summary>
        private void OpenAtParagraph(BinderItem item, int paragraph)
        {
            if (item == null) return;
            _binder.SelectItem(item.Id);
            if (_current != item || !_editor.IsVisible) return;
            Ui.Post(Avalonia.Threading.DispatcherPriority.Background, new Action(delegate
            {
                if (_current != item || item.Document == null) return;
                _editor.Composed.PlaceCaret(Math.Max(0, Math.Min(paragraph, item.Document.Paragraphs.Count - 1)), 0, false);
                _editor.FocusEditor();
            }));
        }

        // — Sonde.
        internal void ToggleInspectorForProbe() { ToggleInspector(); }
        internal void ApplyAppearanceForProbe() { ApplyAppearance(); }
        internal bool RedoForProbe() { if (!_history.CanRedo) return false; _history.Redo(); return true; }
        internal ScenePanel ScenePanelForProbe { get { return _scenePanel; } }
        internal CorkboardView CorkboardForProbe { get { return _corkboard; } }
        internal BookView BookViewForProbe { get { return _bookView; } }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LA VUE SCINDÉE dans la fenêtre (21/11/2026) : la zone
    /// centrale coupée en deux par le milieu, l'élément ouvert à gauche, un
    /// écrit ou une fiche ÉDITABLE à droite (SplitPane). Elle s'ouvre par le
    /// clic droit d'un élément de la Pile ou par Affichage › Vue scindée…,
    /// se ferme par la croix au bas du volet ou par le même menu. Elle ne
    /// vit que le temps de la session : rien n'en est écrit dans le projet.</summary>
    public partial class MainWindow
    {
        private Grid _splitGrid;
        private SplitPane _split;
        private GridSplitter _splitBar;
        private MenuItem _splitMenu;

        /// <summary>Enveloppe la zone centrale : [ vues | barre | volet ]. Fermé, le volet et sa barre n'ont pas de largeur.</summary>
        private Control BuildSplitHost(Control center)
        {
            _splitGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,0,0") };
            _splitGrid.Children.Add(center);
            _splitBar = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch, IsVisible = false, Tag = "split-bar" };
            Grid.SetColumn(_splitBar, 1);
            _splitGrid.Children.Add(_splitBar);
            _split = new SplitPane { IsVisible = false };
            Grid.SetColumn(_split, 2);
            _splitGrid.Children.Add(_split);
            _split.CloseRequested += delegate { CloseSplit(); };
            _split.KeyboardMoved += delegate { _editor.Composed.RefreshCaretNow(); };
            _split.Edited += OnSplitEdited;
            _split.NavigateRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _split.LinkClicked += NavigateToTitle;
            return _splitGrid;
        }

        internal bool SplitOpen { get { return _split != null && _split.Item != null; } }
        internal BinderItem SplitItem { get { return _split == null ? null : _split.Item; } }

        /// <summary>Ouvre cet élément dans la moitié droite. Faux s'il ne s'y
        /// prête pas — ni écrit ni fiche, ou déjà ouvert à gauche : un même
        /// élément ne s'édite pas des deux côtés à la fois.</summary>
        internal bool OpenSplit(BinderItem item)
        {
            if (_project == null || _split == null || !SplitPane.Accepts(item)) return false;
            var root = item.RootCategory();
            if (root != null && root.CategoryKey == Project.KeyTrash) return false;
            if (ReferenceEquals(item, _current))
            {
                ShowToast(null, "Vue scindée", item.Title, "Cet élément est déjà ouvert à gauche : choisissez-en un autre pour travailler sur les deux.", 4);
                return false;
            }
            CommitActive();
            _split.Show(item, _project, _history);
            _splitGrid.ColumnDefinitions = new ColumnDefinitions("*,6,*"); // par le milieu ; la barre se tire ensuite
            _splitBar.IsVisible = true;
            _split.IsVisible = true;
            UpdateSplitMenu();
            _split.FocusContent();
            Ui.Post(Avalonia.Threading.DispatcherPriority.Background, new Action(FitLeftForSplit));
            // Un seul caret : celui de gauche se tait tant que le clavier est dans le volet.
            _editor.Composed.CaretHidden = delegate { return SplitOpen && _split.HasKeyboard; };
            return true;
        }

        private bool _splitZoomed;

        /// <summary>La page de GAUCHE ne tient plus à son zoom dans une demi-largeur : tant que
        /// la vue est scindée, elle est réduite pour tenir (jamais agrandie). Le zoom choisi par
        /// l'auteur n'est pas touché : il revient à la fermeture, ou dès qu'il bouge le curseur.</summary>
        private void FitLeftForSplit()
        {
            if (!SplitOpen || _current == null || _current.Kind != ItemKind.Text || !_editor.IsVisible || _centerHost.Bounds.Width <= 0) return;
            var page = _current.Page ?? _project.Page;
            var fit = Math.Floor((_centerHost.Bounds.Width - 70) / Math.Max(100, page.PageWidthPx) * 20) * 5; // par pas de 5 %
            fit = Math.Max(50, Math.Min(AppSettings.Zoom, fit));
            if (fit >= AppSettings.Zoom) return;
            _editor.SetZoom(fit / 100.0);
            ShowZoom(fit);
            _splitZoomed = true;
        }

        private void RestoreLeftZoom()
        {
            if (!_splitZoomed) return;
            _splitZoomed = false;
            _editor.SetZoom(AppSettings.Zoom / 100.0);
            ShowZoom(AppSettings.Zoom);
        }

        internal bool LeftZoomedForProbe { get { return _splitZoomed; } }

        internal void CloseSplit()
        {
            if (_split == null || _split.Item == null) return;
            _split.Clear();
            _split.IsVisible = false;
            _splitBar.IsVisible = false;
            _splitGrid.ColumnDefinitions = new ColumnDefinitions("*,0,0");
            UpdateSplitMenu();
            RestoreLeftZoom();
            _editor.Composed.CaretHidden = null;
            _editor.Composed.RefreshCaretNow();
            if (_editor.IsVisible) _editor.FocusEditor();
        }

        private void UpdateSplitMenu()
        {
            if (_splitMenu != null) _splitMenu.Header = SplitOpen ? "Fermer la vue scindée" : "Vue scindée…";
        }

        /// <summary>Affichage › Vue scindée… : ouverte, elle se ferme ; fermée, on choisit quoi y mettre.</summary>
        private async void ToggleSplit()
        {
            if (SplitOpen) { CloseSplit(); return; }
            if (_project == null) return;
            var item = await ItemPickerDialog.Ask(this, _project, "Ouvrir dans la vue scindée", new[] { "texte", "fiche" });
            if (item != null) OpenSplit(item);
        }

        /// <summary>Une frappe dans le volet : le projet est modifié, les compteurs et le miroir épinglé suivent.</summary>
        private void OnSplitEdited()
        {
            MarkDirty();
            var item = _split.Item;
            if (item == null) return;
            _pageCountCache.Remove(item.Id);
            if (_sidePin != null && _sidePin == item) { _pinTimer.Stop(); _pinTimer.Start(); }
            _statsTimer.Stop();
            _statsTimer.Start();
        }

        /// <summary>L'élément du volet va s'ouvrir à gauche : le volet se ferme (il ne s'édite pas des deux côtés).</summary>
        private void SplitYieldTo(BinderItem item)
        {
            if (SplitOpen && ReferenceEquals(item, _split.Item)) CloseSplit();
        }

        /// <summary>La structure a changé : un élément supprimé ou jeté à la Corbeille quitte le volet ; un renommé y change de titre.</summary>
        private void SplitPrune()
        {
            if (!SplitOpen) return;
            var item = _split.Item;
            var root = item.RootCategory();
            if (_project.FindById(item.Id) == null || (root != null && root.CategoryKey == Project.KeyTrash)) { CloseSplit(); return; }
            _split.RefreshTitle();
        }

        /// <summary>Les styles du projet ont changé : le volet se recompose.</summary>
        private void SplitReload()
        {
            if (SplitOpen) _split.Reload();
        }

        // — Sonde.
        internal SplitPane SplitForProbe { get { return _split; } }
        internal string SplitMenuForProbe { get { return _splitMenu == null ? null : _splitMenu.Header as string; } }
        internal double SplitShareForProbe { get { return _splitGrid == null || _splitGrid.Bounds.Width <= 0 || !SplitOpen ? 0 : _split.Bounds.Width / _splitGrid.Bounds.Width; } }
    }
}

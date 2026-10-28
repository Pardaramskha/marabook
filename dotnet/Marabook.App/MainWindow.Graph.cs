using System;
using Avalonia.Controls;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE GRAPHE RELATIONNEL dans la fenêtre (1.1.0, 28/10) : une
    /// entrée fixe de la Pile (section Encyclopédie, sous le Dictionnaire),
    /// une entrée du menu Édition › Fiches ; la vue au centre, hors de
    /// l'arbre comme le Journal — tout clic dans la Pile reprend la main.
    /// Un nœud cliqué passe au Général (InspectCard), un double-clic ouvre
    /// l'élément dans l'onglet courant, le menu dans un onglet neuf. Caché
    /// pour le profil Éditeur·ice (Profiles.Graph).</summary>
    public partial class MainWindow
    {
        private GraphView _graphView;
        private MenuItem _graphMenu;

        private Control BuildGraphView()
        {
            _graphView = new GraphView { IsVisible = false };
            _graphView.NodeSelected += InspectCard;
            _graphView.NavigateRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id, true); };
            _graphView.NewTabRequested += OpenInNewTab;
            _graphView.LayoutChanged += MarkDirty; // un nœud tiré : sa place est au projet
            return _graphView;
        }

        /// <summary>Ouvre le graphe au centre : la photographie du projet se
        /// prend maintenant, le calcul tourne sous le voile de la vue.</summary>
        private void ShowGraph()
        {
            if (_project == null || !Profiles.Enabled(Profiles.Graph)) return;
            if (_graphView.IsVisible)
            {
                _graphView.Refresh();
                return;
            }
            _navigating = true;
            try
            {
                CommitActive();
                SlideJournalOut();
                HideCenterViews();
                _current = null; _inspected = null; _inspectedGroup = null;
                _statusPages.Text = ""; _statusBook.Text = ""; _docPages = 0;
                _graphView.IsVisible = true;
                _graphView.Load(_project);
            }
            finally
            {
                _navigating = false;
            }
            UpdateInspector();
            UpdateStats();
        }

        // ---------------------------------------------------------------- sonde

        internal GraphView GraphViewForProbe { get { return _graphView; } }
        internal bool DirtyForProbe { get { return _dirty; } }
        internal void ShowGraphForProbe() { ShowGraph(); }
        internal bool GraphMenuShownForProbe { get { return _graphMenu != null && _graphMenu.IsVisible; } }
    }
}

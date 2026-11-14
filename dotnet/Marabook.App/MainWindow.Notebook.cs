using Avalonia.Controls;

using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Le panneau CARNET de la colonne de droite (1.2.0, 14/11/2026,
    /// Rémi) : des notes en Markdown à l'échelle de l'application, toujours au
    /// rail, sous le Lexique (NotebookPanel, Settings.Notebook).</summary>
    public partial class MainWindow
    {
        private NotebookPanel _notebookPanel;
        private Border _notebookHost;

        private void BuildNotebookPanel(Grid grid)
        {
            _notebookPanel = new NotebookPanel();
            _notebookPanel.LinkClicked += NavigateToTitle; // un [[lien]] du carnet mène à la fiche du projet ouvert
            _notebookPanel.ConfirmRemove = async delegate
            {
                return await MessageDialog.Show(this, "Supprimer « " + Notebook.TitleOf(Notebook.Current.Text) + " » du carnet ?\n\nLa note est effacée pour de bon.",
                    "Carnet", MessageButtons.YesNo, MessageIcon.Question) == MessageResult.Yes;
            };
            _notebookHost = new Border { Child = _notebookPanel };
            Grid.SetColumn(_notebookHost, 4);
            grid.Children.Add(_notebookHost);
        }

        // Sonde.
        public NotebookPanel NotebookPanelForProbe { get { return _notebookPanel; } }
        public bool RailHasTabForProbe(RightPanel panel) { return _railTabs.ContainsKey(panel) && _railTabs[panel].IsEffectivelyVisible; }
        public int RailTabRankForProbe(RightPanel panel) { return _railOffered == null ? -1 : System.Array.IndexOf(_railOffered, panel); }
    }
}

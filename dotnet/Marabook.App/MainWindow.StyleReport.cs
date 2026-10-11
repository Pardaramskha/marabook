using System;
using Avalonia.Controls;
using Avalonia.Threading;

using Marabook.Correction;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Le BILAN DE STYLE au rail (09/10) : le bouton du ruban
    /// Révision ouvre un onglet le temps du bilan ; la croix du panneau, le
    /// repli du rail ou un changement d'écrit le ferment — rail compris.
    /// Une ligne qui désigne un paragraphe ramène la vue dessus.</summary>
    public partial class MainWindow
    {
        private StyleReportPanel _styleReportPanel;
        private Border _styleReportHost;
        private BinderItem _styleReportItem; // l'écrit du bilan ouvert, null sans bilan

        /// <summary>L'onglet Bilan existe : un bilan est ouvert pour l'écrit courant.</summary>
        private bool HasStyleReport
        {
            get { return _styleReportItem != null; }
        }

        private void BuildStyleReportPanel(Grid grid)
        {
            _styleReportPanel = new StyleReportPanel();
            _styleReportPanel.CloseRequested += CloseStyleReport;
            _styleReportPanel.ParagraphRequested += GoToReportParagraph;
            _styleReportHost = new Border { Child = _styleReportPanel };
            Grid.SetColumn(_styleReportHost, 4);
            grid.Children.Add(_styleReportHost);
        }

        /// <summary>Le ruban Révision › Bilan de style : l'éditeur a compté,
        /// le rail montre.</summary>
        private void ShowStyleReport(string title, StyleReport report)
        {
            if (_project == null || _current == null || _current.Kind != ItemKind.Text) return;
            _styleReportItem = _current;
            _styleReportPanel.Show(title, report);
            UpdateRail();
            SetRightPanel(RightPanel.StyleReport);
            UpdateRail();
        }

        /// <summary>La croix du panneau : le bilan s'en va, le rail avec.</summary>
        private void CloseStyleReport()
        {
            if (!HasStyleReport) return;
            _styleReportItem = null;
            if (AppSettings.RightPanel == RightPanel.StyleReport) SetRightPanel(RightPanel.None);
            UpdateRail();
        }

        /// <summary>Le bilan ne survit pas à son contexte : le rail replié ou
        /// un autre élément ouvert l'oublient (sans rouvrir le rail).</summary>
        private void DropStyleReportIfStale(BinderItem shown)
        {
            if (!HasStyleReport || ReferenceEquals(shown, _styleReportItem)) return;
            _styleReportItem = null;
            if (AppSettings.RightPanel == RightPanel.StyleReport)
            {
                AppSettings.RightPanel = RightPanel.None;
                AppSettings.Save();
            }
            Ui.Post(DispatcherPriority.Background, new Action(delegate { ApplyPanelVisibility(); UpdateRail(); }));
        }

        private void GoToReportParagraph(int paragraph)
        {
            if (_styleReportItem == null || _current != _styleReportItem || !_editor.IsVisible) return;
            _editor.GoToRange(paragraph, 0, 0);
        }

        /// <summary>Sonde (09/10).</summary>
        public bool HasStyleReportForProbe { get { return HasStyleReport; } }
        public StyleReportPanel StyleReportPanelForProbe { get { return _styleReportPanel; } }
        public void CloseStyleReportForProbe() { CloseStyleReport(); }
        public void SetRightPanelForProbe(RightPanel panel) { SetRightPanel(panel); }
    }
}

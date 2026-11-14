using System.Collections.Generic;
using Avalonia.Controls;

using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Le CALCULATEUR DE DATES au rail (1.2.0, 14/11/2026, Rémi : « on
    /// ne doit pouvoir ouvrir qu'un seul calculateur ») : un onglet comme le
    /// Lexique — absent tant qu'on ne l'a pas demandé (tuile d'un système dans
    /// l'Astrolabe, clic droit sur la frise), épinglable ; sans épingle, sa
    /// croix le referme et l'onglet s'en va.</summary>
    public partial class MainWindow
    {
        private DateCalculatorPanel _calculatorPanel;
        private Border _calculatorHost;
        private bool _calculatorShown; // ouvert sans épingle, jusqu'à la croix

        /// <summary>L'onglet du Calculateur existe : épinglé, ou ouvert à la demande.</summary>
        private bool HasCalculatorPanel
        {
            get { return AppSettings.CalculatorPinned || _calculatorShown; }
        }

        private void BuildCalculatorPanel(Grid grid)
        {
            _calculatorPanel = new DateCalculatorPanel();
            _calculatorPanel.SetPinned(AppSettings.CalculatorPinned);
            _calculatorPanel.SystemsProvider = delegate { return _project == null ? new List<TimeSystem>() : new List<TimeSystem>(_project.TimeSystems); };
            _calculatorPanel.SkyProvider = delegate(TimeSystem system, TimePoint point) { return _project == null ? null : Astro.DayLabel(_project, system, point); };
            _calculatorPanel.PinToggled += SetCalculatorPinned;
            _calculatorPanel.CloseRequested += CloseCalculator;
            _calculatorHost = new Border { Child = _calculatorPanel };
            Grid.SetColumn(_calculatorHost, 4);
            grid.Children.Add(_calculatorHost);
        }

        /// <summary>Le Calculateur prend la colonne, sur ce système, à cette date (null : sans date).</summary>
        private void ShowCalculator(TimeSystem system, TimePoint at)
        {
            if (_project == null || system == null) return;
            _calculatorPanel.Open(system, at);
            _calculatorShown = true;
            SetRightPanel(RightPanel.Calculator);
            UpdateRail();
        }

        private void SetCalculatorPinned(bool pinned)
        {
            AppSettings.CalculatorPinned = pinned;
            AppSettings.Save();
            _calculatorPanel.SetPinned(pinned);
            ApplyPanelVisibility();
        }

        /// <summary>La croix : sans épingle, l'onglet s'en va avec le panneau ; épinglé, la colonne se replie.</summary>
        private void CloseCalculator()
        {
            _calculatorShown = false;
            if (AppSettings.RightPanel == RightPanel.Calculator) SetRightPanel(AppSettings.CalculatorPinned ? RightPanel.None : RightPanel.Inspector);
            ApplyPanelVisibility();
        }

        // Sonde.
        public DateCalculatorPanel CalculatorPanelForProbe { get { return _calculatorPanel; } }
        public bool HasCalculatorForProbe { get { return HasCalculatorPanel; } }
        public void CloseCalculatorForProbe() { CloseCalculator(); }
        public void PinCalculatorForProbe(bool pinned) { SetCalculatorPinned(pinned); }
    }
}

using UniversSale.Settings;

namespace UniversSale.Tests
{
    /// <summary>C18 — la colonne de droite (batch 39) : un seul champ pour
    /// quatre panneaux. La migration des quatre anciens booléens dans leurs
    /// seize combinaisons, la disponibilité selon le contexte, et les
    /// transitions du rail.</summary>
    public static class RightPanelTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C18 — colonne de droite");

            // — Migration : l'ordre de priorité historique, Versions > Recherche
            //   > Correction > Inspecteur, sur les seize combinaisons.
            for (var bits = 0; bits < 16; bits++)
            {
                var inspector = (bits & 1) != 0;
                var correction = (bits & 2) != 0;
                var search = (bits & 4) != 0;
                var versions = (bits & 8) != 0;
                var expected = versions ? RightPanel.Versions
                    : search ? RightPanel.Search
                    : correction ? RightPanel.Correction
                    : inspector ? RightPanel.Inspector : RightPanel.None;
                t.Equal(expected, RightPanels.Migrate(inspector, correction, search, versions),
                    "migration i=" + inspector + " c=" + correction + " s=" + search + " v=" + versions);
            }
            t.Equal(RightPanel.Inspector, RightPanels.Migrate(true, false, false, false), "un settings.json neuf donne l'inspecteur");

            // — Le nom persisté fait l'aller-retour ; un inconnu vaut l'inspecteur.
            foreach (RightPanel panel in System.Enum.GetValues(typeof(RightPanel)))
                t.Equal(panel, RightPanels.Parse(RightPanels.Name(panel)), "aller-retour du nom « " + RightPanels.Name(panel) + " »");
            t.Equal(RightPanel.Inspector, RightPanels.Parse("garbage"), "un nom inconnu vaut l'inspecteur");
            t.Equal(RightPanel.Inspector, RightPanels.Parse(null), "un nom absent vaut l'inspecteur");

            // — Disponibilité : l'inspecteur et Correction demandent un élément
            //   courant, Recherche et Versions un projet seulement, et le mode
            //   calme ou le journal masquent tout.
            t.Check(RightPanels.Available(RightPanel.Inspector, false, true, true), "inspecteur avec élément courant");
            t.Check(!RightPanels.Available(RightPanel.Inspector, false, true, false), "inspecteur sans élément courant : indisponible");
            t.Check(RightPanels.Available(RightPanel.Correction, false, true, true), "correction avec élément courant");
            t.Check(!RightPanels.Available(RightPanel.Correction, false, true, false), "correction sans élément courant : indisponible");
            t.Check(RightPanels.Available(RightPanel.Search, false, true, false), "recherche sans élément courant : disponible (on cherche avant d'avoir cliqué)");
            t.Check(RightPanels.Available(RightPanel.Versions, false, true, false), "versions sans élément courant : disponible");
            t.Check(!RightPanels.Available(RightPanel.Search, false, false, false), "recherche sans projet : indisponible");
            t.Check(!RightPanels.Available(RightPanel.Versions, false, false, false), "versions sans projet : indisponible");
            t.Check(!RightPanels.Available(RightPanel.Inspector, false, false, false), "inspecteur sans projet : indisponible");
            foreach (RightPanel panel in System.Enum.GetValues(typeof(RightPanel)))
                t.Check(!RightPanels.Available(panel, true, true, true), "colonne masquée (calme, journal) : « " + RightPanels.Name(panel) + " » indisponible");
            t.Check(!RightPanels.Available(RightPanel.None, false, true, true), "« aucun » n'est jamais montré");

            // — Transitions du rail : ouvrir, changer, replier sur l'actif.
            t.Equal(RightPanel.Search, RightPanels.Toggle(RightPanel.None, RightPanel.Search), "colonne repliée, clic Recherche : ouvre");
            t.Equal(RightPanel.Versions, RightPanels.Toggle(RightPanel.Search, RightPanel.Versions), "Recherche active, clic Versions : change");
            t.Equal(RightPanel.None, RightPanels.Toggle(RightPanel.Versions, RightPanel.Versions), "clic sur l'actif : replie");
            t.Equal(RightPanel.Inspector, RightPanels.Toggle(RightPanel.None, RightPanel.Inspector), "colonne repliée, clic Inspecteur : ouvre");
        }
    }
}

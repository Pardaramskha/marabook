using System;
using UniversSale.Model;
using UniversSale.Settings;

namespace UniversSale.Tests
{
    /// <summary>C18 — la colonne de droite (batch 39) : un seul champ pour
    /// les panneaux qui s'y excluent. La migration des quatre anciens
    /// booléens dans leurs seize combinaisons, les onglets offerts selon la
    /// nature de l'élément courant, la disponibilité selon le contexte, et
    /// les transitions du rail.</summary>
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
            foreach (RightPanel panel in Enum.GetValues(typeof(RightPanel)))
                t.Equal(panel, RightPanels.Parse(RightPanels.Name(panel)), "aller-retour du nom « " + RightPanels.Name(panel) + " »");
            t.Equal(RightPanel.Inspector, RightPanels.Parse("garbage"), "un nom inconnu vaut l'inspecteur");
            t.Equal(RightPanel.Inspector, RightPanels.Parse(null), "un nom absent vaut l'inspecteur");

            // — Les onglets offerts selon la nature de l'élément courant.
            t.Equal("inspector,correction,search,versions", Join(RightPanels.Offered(ItemKind.Text)), "un écrit : Général, Correction, Recherche, Versions");
            t.Equal("inspector,edition,metadata,publication,search", Join(RightPanels.Offered(ItemKind.Book)), "un livre : Général, Édition, Métadonnées, Publication, Recherche");
            t.Equal("inspector,search,versions", Join(RightPanels.Offered(ItemKind.Sheet)), "une fiche : Général, Recherche, Versions");
            foreach (var kind in new[] { ItemKind.Media, ItemKind.Plan, ItemKind.Category, ItemKind.Folder, ItemKind.PageTemplate })
                t.Equal("inspector,search", Join(RightPanels.Offered(kind)), kind + " : Général et Recherche seulement");
            t.Equal("inspector,search", Join(RightPanels.Offered(null)), "rien de sélectionné : Général et Recherche");
            t.Check(RightPanels.DescribesCurrent(RightPanel.Edition), "Édition décrit l'élément");
            t.Check(RightPanels.DescribesCurrent(RightPanel.Inspector) && RightPanels.DescribesCurrent(RightPanel.Metadata)
                && RightPanels.DescribesCurrent(RightPanel.Publication) && !RightPanels.DescribesCurrent(RightPanel.Search)
                && !RightPanels.DescribesCurrent(RightPanel.Correction) && !RightPanels.DescribesCurrent(RightPanel.Versions),
                "le filet sépare ce qui décrit l'élément (Général, Métadonnées, Publication) des outils");

            // — Disponibilité : offert pour la nature ET un élément courant
            //   (sauf Recherche, qui vit sans), un projet, colonne visible.
            t.Check(RightPanels.Available(RightPanel.Inspector, false, true, ItemKind.Text), "Général sur un écrit");
            t.Check(!RightPanels.Available(RightPanel.Inspector, false, true, null), "Général sans élément courant : indisponible");
            t.Check(RightPanels.Available(RightPanel.Correction, false, true, ItemKind.Text), "Correction sur un écrit");
            t.Check(!RightPanels.Available(RightPanel.Correction, false, true, ItemKind.Book), "Correction sur un livre : indisponible (pas offerte)");
            t.Check(!RightPanels.Available(RightPanel.Correction, false, true, null), "Correction sans élément courant : indisponible");
            t.Check(RightPanels.Available(RightPanel.Metadata, false, true, ItemKind.Book) && RightPanels.Available(RightPanel.Publication, false, true, ItemKind.Book),
                "Métadonnées et Publication sur un livre");
            t.Check(!RightPanels.Available(RightPanel.Metadata, false, true, ItemKind.Text) && !RightPanels.Available(RightPanel.Publication, false, true, ItemKind.Sheet),
                "…et nulle part ailleurs");
            t.Check(RightPanels.Available(RightPanel.Search, false, true, null), "Recherche sans élément courant : disponible (on cherche avant d'avoir cliqué)");
            t.Check(RightPanels.Available(RightPanel.Search, false, true, ItemKind.Plan), "Recherche sur un plan");
            t.Check(RightPanels.Available(RightPanel.Versions, false, true, ItemKind.Text) && RightPanels.Available(RightPanel.Versions, false, true, ItemKind.Sheet),
                "Versions sur un écrit et sur une fiche");
            t.Check(!RightPanels.Available(RightPanel.Versions, false, true, null) && !RightPanels.Available(RightPanel.Versions, false, true, ItemKind.Book),
                "Versions sans élément courant ou sur un livre : indisponible");
            t.Check(!RightPanels.Available(RightPanel.Search, false, false, null), "Recherche sans projet : indisponible");
            t.Check(!RightPanels.Available(RightPanel.Inspector, false, false, ItemKind.Text), "Général sans projet : indisponible");
            foreach (RightPanel panel in Enum.GetValues(typeof(RightPanel)))
                t.Check(!RightPanels.Available(panel, true, true, ItemKind.Text), "colonne masquée (calme, journal) : « " + RightPanels.Name(panel) + " » indisponible");
            t.Check(!RightPanels.Available(RightPanel.None, false, true, ItemKind.Text), "« aucun » n'est jamais montré");

            // — Transitions du rail : ouvrir, changer, replier sur l'actif.
            t.Equal(RightPanel.Search, RightPanels.Toggle(RightPanel.None, RightPanel.Search), "colonne repliée, clic Recherche : ouvre");
            t.Equal(RightPanel.Versions, RightPanels.Toggle(RightPanel.Search, RightPanel.Versions), "Recherche active, clic Versions : change");
            t.Equal(RightPanel.None, RightPanels.Toggle(RightPanel.Versions, RightPanel.Versions), "clic sur l'actif : replie");
            t.Equal(RightPanel.Inspector, RightPanels.Toggle(RightPanel.None, RightPanel.Inspector), "colonne repliée, clic Général : ouvre");
        }

        private static string Join(RightPanel[] panels)
        {
            var names = new string[panels.Length];
            for (var i = 0; i < panels.Length; i++) names[i] = RightPanels.Name(panels[i]);
            return string.Join(",", names);
        }
    }
}

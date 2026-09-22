using System;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.Tests
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
            t.Equal("inspector,correction,search,versions,pinned", Join(RightPanels.Offered(ItemKind.Text)), "un écrit : Général, Correction, Recherche, Versions, Épinglé");
            t.Equal("inspector,search,pinned", Join(RightPanels.Offered(ItemKind.Book)), "un livre : Général, Recherche, Épinglé — Édition, Métadonnées et Publication sont des onglets de la page livre (22/09)");
            t.Equal("inspector,search,pinned", Join(RightPanels.Offered(ItemKind.Sheet)), "une fiche : Général et Recherche (plus de Versions, b43), Épinglé");
            foreach (var kind in new[] { ItemKind.Media, ItemKind.Plan, ItemKind.Folder, ItemKind.PageTemplate })
                t.Equal("inspector,search,pinned", Join(RightPanels.Offered(kind)), kind + " : Général et Recherche seulement (et l'épinglé)");
            t.Equal("search,pinned", Join(RightPanels.Offered(ItemKind.Category)), "une racine de la Pile : Recherche seule (b43), et l'épinglé");
            t.Equal("inspector,search,pinned", Join(RightPanels.Offered(ItemKind.Category, true)), "l'Accueil garde son Général (les raccourcis)");
            t.Equal("inspector,search,pinned", Join(RightPanels.Offered(null)), "rien de sélectionné : Général et Recherche");
            t.Check(RightPanels.DescribesCurrent(RightPanel.Inspector) && !RightPanels.DescribesCurrent(RightPanel.Search)
                && !RightPanels.DescribesCurrent(RightPanel.Correction) && !RightPanels.DescribesCurrent(RightPanel.Versions),
                "le filet sépare ce qui décrit l'élément (Général) des outils");
            t.Equal(RightPanel.Inspector, RightPanels.Parse("metadata"), "l'ancien nom « metadata » d'un settings.json d'avant le 22/09 vaut l'inspecteur");

            // — Disponibilité : offert pour la nature ET un élément courant
            //   (sauf Recherche, qui vit sans), un projet, colonne visible.
            t.Check(RightPanels.Available(RightPanel.Inspector, false, true, ItemKind.Text), "Général sur un écrit");
            t.Check(!RightPanels.Available(RightPanel.Inspector, false, true, null), "Général sans élément courant : indisponible");
            t.Check(RightPanels.Available(RightPanel.Correction, false, true, ItemKind.Text), "Correction sur un écrit");
            t.Check(!RightPanels.Available(RightPanel.Correction, false, true, ItemKind.Book), "Correction sur un livre : indisponible (pas offerte)");
            t.Check(!RightPanels.Available(RightPanel.Correction, false, true, null), "Correction sans élément courant : indisponible");
            t.Check(RightPanels.Available(RightPanel.Search, false, true, null), "Recherche sans élément courant : disponible (on cherche avant d'avoir cliqué)");
            t.Check(RightPanels.Available(RightPanel.Search, false, true, ItemKind.Plan), "Recherche sur un plan");
            t.Check(RightPanels.Available(RightPanel.Versions, false, true, ItemKind.Text) && !RightPanels.Available(RightPanel.Versions, false, true, ItemKind.Sheet),
                "Versions sur un écrit, plus sur une fiche (b43)");
            t.Check(!RightPanels.Available(RightPanel.Inspector, false, true, ItemKind.Category)
                && RightPanels.Available(RightPanel.Inspector, false, true, ItemKind.Category, true)
                && RightPanels.Available(RightPanel.Search, false, true, ItemKind.Category),
                "une racine : Recherche seule, sauf l'Accueil qui garde Général (b43)");
            t.Check(!RightPanels.Available(RightPanel.Versions, false, true, null) && !RightPanels.Available(RightPanel.Versions, false, true, ItemKind.Book),
                "Versions sans élément courant ou sur un livre : indisponible");
            t.Check(!RightPanels.Available(RightPanel.Search, false, false, null), "Recherche sans projet : indisponible");
            t.Check(!RightPanels.Available(RightPanel.Inspector, false, false, ItemKind.Text), "Général sans projet : indisponible");
            foreach (RightPanel panel in Enum.GetValues(typeof(RightPanel)))
                t.Check(!RightPanels.Available(panel, true, true, ItemKind.Text), "colonne masquée (calme, journal) : « " + RightPanels.Name(panel) + " » indisponible");
            t.Check(!RightPanels.Available(RightPanel.None, false, true, ItemKind.Text), "« aucun » n'est jamais montré");
            // — L'épinglé (b47) : partout, mais seulement une épingle posée.
            t.Check(!RightPanels.Available(RightPanel.Pinned, false, true, ItemKind.Text), "épinglé sans épingle : indisponible");
            t.Check(RightPanels.Available(RightPanel.Pinned, false, true, ItemKind.Text, false, true), "épinglé avec une épingle, sur un écrit");
            t.Check(RightPanels.Available(RightPanel.Pinned, false, true, null, false, true), "épinglé sans élément courant : disponible");
            t.Check(RightPanels.Available(RightPanel.Pinned, false, true, ItemKind.Category, false, true), "épinglé sur une racine");
            t.Check(!RightPanels.Available(RightPanel.Pinned, true, true, ItemKind.Text, false, true), "épinglé, colonne masquée : indisponible");
            t.Check(!RightPanels.DescribesCurrent(RightPanel.Pinned), "l'épinglé est un outil (après le filet)");

            // — Le Lexique (18/09) : un onglet seulement quand il est demandé
            //   (épinglé ou ouvert pour une définition), en queue, partout.
            t.Equal("inspector,correction,search,versions,pinned,lexicon", Join(RightPanels.Offered(ItemKind.Text, false, true)), "un écrit, Lexique demandé : l'onglet en queue");
            t.Equal("search,pinned,lexicon", Join(RightPanels.Offered(ItemKind.Category, false, true)), "une racine, Lexique demandé");
            t.Equal("inspector,search,pinned,lexicon", Join(RightPanels.Offered(null, false, true)), "rien de sélectionné, Lexique demandé");
            t.Check(ReferenceEquals(RightPanels.Offered(ItemKind.Text, false, true), RightPanels.Offered(ItemKind.Text, false, true)), "le tableau est le même d'un appel à l'autre (le rail compare par référence)");
            t.Check(!RightPanels.Available(RightPanel.Lexicon, false, true, ItemKind.Text), "Lexique sans épingle ni définition : indisponible");
            t.Check(RightPanels.Available(RightPanel.Lexicon, false, true, ItemKind.Text, false, false, true), "Lexique demandé, sur un écrit");
            t.Check(RightPanels.Available(RightPanel.Lexicon, false, true, null, false, false, true), "Lexique demandé, sans élément courant");
            t.Check(RightPanels.Available(RightPanel.Lexicon, false, true, ItemKind.Category, false, false, true), "Lexique demandé, sur une racine");
            t.Check(!RightPanels.Available(RightPanel.Lexicon, true, true, ItemKind.Text, false, false, true), "Lexique, colonne masquée : indisponible");
            t.Check(!RightPanels.Available(RightPanel.Lexicon, false, false, ItemKind.Text, false, false, true), "Lexique sans projet : indisponible");
            t.Check(!RightPanels.DescribesCurrent(RightPanel.Lexicon), "le Lexique est un outil (après le filet)");
            t.Equal(RightPanel.Lexicon, RightPanels.Parse("lexicon"), "le nom « lexicon » se relit");

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

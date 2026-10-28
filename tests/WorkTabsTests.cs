using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C54 — les ONGLETS de la 1.1.0 (28/10/2026) : la rangée
    /// (WorkTabs) — ouvrir sous plafond, naviguer dans l'actif, fermer à la
    /// manière de Chrome, fermer les autres / à droite, circuler, suivre un
    /// élément disparu, serrer sous un plafond abaissé, reprendre une rangée
    /// enregistrée — et l'aller-retour .plot v42 de « tabs » / « activeTab ».</summary>
    public static class WorkTabsTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C54 — 1.1.0 : onglets (rangée, plafond, fermeture, reprise, .plot v42)");
            OpenAndNavigate(t);
            Closing(t);
            Cycling(t);
            Dropping(t);
            Limits(t);
            Restoring(t);
            RoundTrip(t);
        }

        private static string Ids(WorkTabs tabs)
        {
            return string.Join(" ", tabs.ItemIds().ToArray());
        }

        private static void OpenAndNavigate(Harness t)
        {
            var tabs = new WorkTabs();
            t.Check(tabs.Count == 0 && tabs.Active == null && tabs.ActiveIndex == -1, "rangée neuve : rien, pas d'actif");
            t.Check(tabs.Navigate("a"), "naviguer sans onglet en crée un");
            t.Check(tabs.Count == 1 && tabs.ActiveItemId == "a", "…qui montre l'élément et devient l'actif");
            t.Check(!tabs.Navigate("a"), "naviguer vers l'élément déjà montré ne change rien");
            t.Check(tabs.Navigate("b") && tabs.Count == 1 && tabs.ActiveItemId == "b", "naviguer remplace l'élément de l'actif, sans onglet de plus");
            t.Check(!tabs.Navigate(null) && !tabs.Navigate(""), "un id vide est ignoré");
            var opened = tabs.Open("home");
            t.Check(opened != null && tabs.Count == 2 && tabs.ActiveIndex == 1 && tabs.ActiveItemId == "home", "ouvrir ajoute en bout de rangée et active");
            t.Equal("b home", Ids(tabs), "l'ordre est celui des ouvertures");
            tabs.Open("b");
            t.Check(tabs.Count == 3 && tabs.IndexOf("b") == 0, "deux onglets peuvent montrer le même élément ; IndexOf rend le premier");
            t.Check(tabs.Activate(0) && tabs.ActiveIndex == 0 && !tabs.Activate(0) && !tabs.Activate(9) && !tabs.Activate(-1), "activer : vrai au changement, faux sur place ou hors rangée");
        }

        private static void Closing(Harness t)
        {
            var tabs = new WorkTabs();
            tabs.Open("a"); tabs.Open("b"); tabs.Open("c"); tabs.Open("d");
            tabs.Activate(1);
            t.Check(tabs.Close(1) && tabs.ActiveItemId == "c", "fermer l'actif passe la main à son voisin de droite");
            tabs.Activate(2); // d
            t.Check(tabs.Close(2) && tabs.ActiveItemId == "c", "…ou de gauche quand il était le dernier");
            tabs.Open("e"); tabs.Activate(2); // a c e → e actif
            t.Check(tabs.Close(0) && tabs.ActiveItemId == "e" && tabs.ActiveIndex == 1, "fermer un onglet à gauche de l'actif le fait reculer d'une place");
            t.Check(!tabs.Close(7) && !tabs.Close(-1), "fermer hors rangée : faux, rien ne bouge");
            tabs.Close(0); tabs.Close(0);
            t.Check(tabs.Count == 0 && tabs.Active == null && tabs.ActiveIndex == -1, "la rangée vidée n'a plus d'actif");

            tabs = new WorkTabs();
            tabs.Open("a"); tabs.Open("b"); tabs.Open("c"); tabs.Open("d"); tabs.Activate(3);
            tabs.CloseOthers(1);
            t.Check(Ids(tabs) == "b" && tabs.ActiveIndex == 0, "fermer les autres ne garde que lui, actif");
            tabs = new WorkTabs();
            tabs.Open("a"); tabs.Open("b"); tabs.Open("c"); tabs.Open("d"); tabs.Activate(3);
            tabs.CloseRight(1);
            t.Check(Ids(tabs) == "a b" && tabs.ActiveItemId == "b", "fermer à droite emporte l'actif qui s'y trouvait : celui-ci prend la main");
            tabs.Activate(0);
            tabs.CloseRight(1);
            t.Check(Ids(tabs) == "a b" && tabs.ActiveIndex == 0, "…et ne touche pas à un actif à gauche");
        }

        private static void Cycling(Harness t)
        {
            var tabs = new WorkTabs();
            tabs.Open("a");
            t.Check(!tabs.Next() && !tabs.Previous(), "un onglet seul ne circule pas");
            tabs.Open("b"); tabs.Open("c");
            t.Check(tabs.Next() && tabs.ActiveItemId == "a", "suivant depuis le dernier revient au premier");
            t.Check(tabs.Previous() && tabs.ActiveItemId == "c", "précédent depuis le premier va au dernier");
            t.Check(tabs.Previous() && tabs.ActiveItemId == "b", "précédent recule d'un");
        }

        private static void Dropping(Harness t)
        {
            var tabs = new WorkTabs();
            tabs.Open("a"); tabs.Open("x"); tabs.Open("b"); tabs.Open("x"); tabs.Activate(1);
            t.Check(tabs.Drop("x") && Ids(tabs) == "a b" && tabs.ActiveItemId == "b", "un élément disparu emporte tous ses onglets ; l'actif tombé rend vrai");
            t.Check(!tabs.Drop("zzz") && Ids(tabs) == "a b", "un id absent ne change rien");
            t.Check(!tabs.Drop("a") && Ids(tabs) == "b" && tabs.ActiveItemId == "b", "un inactif tombé rend faux, l'actif reste");
            tabs = new WorkTabs();
            tabs.Open("a"); tabs.Open("b"); tabs.Open("c"); tabs.Activate(0);
            var kept = new HashSet<string> { "a", "c" };
            t.Check(!tabs.Prune(kept.Contains) && Ids(tabs) == "a c" && tabs.ActiveItemId == "a", "élaguer garde les éléments qui existent, l'actif survivant en place");
            kept.Remove("a");
            t.Check(tabs.Prune(kept.Contains) && Ids(tabs) == "c" && tabs.ActiveItemId == "c", "…et dit quand l'actif est parti");
        }

        private static void Limits(Harness t)
        {
            t.Equal(5, WorkTabs.MinLimit, "plafond minimal 5");
            t.Equal(30, WorkTabs.MaxLimit, "plafond maximal 30");
            t.Equal(10, WorkTabs.DefaultLimit, "dix onglets par défaut");
            t.Equal(5, WorkTabs.Clamp(1), "un plafond trop bas remonte à 5");
            t.Equal(30, WorkTabs.Clamp(99), "un plafond trop haut redescend à 30");
            t.Equal(12, WorkTabs.Clamp(12), "entre les deux, tel quel");
            var tabs = new WorkTabs();
            t.Equal(10, tabs.Limit, "la rangée naît au plafond par défaut");
            for (var i = 0; i < 10; i++) tabs.Open("t" + i);
            t.Check(tabs.Count == 10 && !tabs.CanOpen && tabs.Open("de trop") == null && tabs.Count == 10, "au plafond : plus rien ne s'ouvre, Open rend null");
            t.Check(tabs.Navigate("ailleurs") && tabs.Count == 10, "…mais l'actif navigue toujours");
            tabs.Activate(8);
            tabs.Limit = 5;
            t.Check(tabs.Count == 5 && tabs.ActiveItemId == "t8" && tabs.ActiveIndex == 4, "abaisser le plafond ferme par la droite en épargnant l'actif (" + Ids(tabs) + ")");
            tabs.Limit = 2;
            t.Equal(5, tabs.Limit, "le plafond posé est borné");
            tabs.Limit = 40;
            t.Check(tabs.Limit == 30 && tabs.CanOpen, "…des deux côtés, et rouvre la porte");
        }

        private static void Restoring(Harness t)
        {
            var tabs = new WorkTabs();
            var exists = new HashSet<string> { "a", "b", "c" };
            tabs.Restore(new List<string> { "a", "mort", "b", "", "c" }, 2, exists.Contains);
            t.Check(Ids(tabs) == "a b c" && tabs.ActiveItemId == "b", "la reprise écarte les ids disparus ou vides et retrouve l'actif par sa position d'origine");
            tabs.Restore(new List<string> { "mort", "a" }, 0, exists.Contains);
            t.Check(Ids(tabs) == "a" && tabs.ActiveIndex == 0, "un actif disparu laisse la main au premier gardé");
            tabs.Restore(new List<string> { "a", "b" }, 7, exists.Contains);
            t.Check(tabs.ActiveIndex == 0, "un index d'actif hors rangée retombe sur le premier");
            tabs.Restore(null, 0, exists.Contains);
            t.Check(tabs.Count == 0 && tabs.ActiveIndex == -1, "rien à reprendre : rangée vide");
            tabs.Limit = 5;
            var many = new List<string>();
            for (var i = 0; i < 8; i++) { many.Add("m" + i); exists.Add("m" + i); }
            tabs.Restore(many, 7, exists.Contains);
            t.Check(tabs.Count == 5 && tabs.ActiveIndex == 0, "une rangée plus longue que le plafond est tronquée ; l'actif au-delà retombe sur le premier");
            tabs.Restore(new List<string> { "a", "b" }, 1, null);
            t.Check(Ids(tabs) == "a b" && tabs.ActiveIndex == 1, "sans juge, tout est repris");
        }

        private static void RoundTrip(Harness t)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            var one = new BinderItem { Kind = ItemKind.Text, Title = "Un" };
            var two = new BinderItem { Kind = ItemKind.Text, Title = "Deux" };
            writings.Children.Add(one); writings.Children.Add(two);
            project.RelinkParents();
            var home = project.Category(Project.KeyHome);
            project.OpenTabs.Add(one.Id);
            project.OpenTabs.Add(home.Id);
            project.OpenTabs.Add(two.Id);
            project.ActiveTab = 2;

            var path = Path.Combine(Path.GetTempPath(), "marabook-c54-" + Guid.NewGuid().ToString("N") + ".plot");
            try
            {
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                t.Equal(one.Id + " " + home.Id + " " + two.Id, string.Join(" ", loaded.OpenTabs.ToArray()), "les onglets font l'aller-retour, dans l'ordre");
                t.Equal(2, loaded.ActiveTab, "…avec l'index de l'actif");
                t.Check(loaded.LoadedFormatVersion >= 42, "le .plot est en v42 au moins (" + loaded.LoadedFormatVersion + ")");
                var tabs = new WorkTabs();
                tabs.Restore(loaded.OpenTabs, loaded.ActiveTab, delegate(string id) { return loaded.FindById(id) != null; });
                t.Check(tabs.Count == 3 && tabs.ActiveItemId == two.Id, "la rangée se rebâtit depuis le projet relu");

                var empty = Project.CreateNew();
                PlotFile.Save(empty, path);
                var reloaded = PlotFile.Load(path);
                t.Check(reloaded.OpenTabs.Count == 0 && reloaded.ActiveTab == 0, "sans onglet enregistré : rien, actif 0");
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
                try { File.Delete(path + ".bak"); } catch (IOException) { }
            }
        }
    }
}

using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using UniversSale.History;
using UniversSale.Model;
using UniversSale.Persistence;

namespace UniversSale.Tests
{
    /// <summary>C19 — l'Accueil (batch 41) : les récents (ajout, remontée
    /// d'un doublon, plafond à dix, entrées mortes ignorées à l'affichage et
    /// purgées au chargement SEULEMENT), l'épingle (bascule annulable,
    /// persistance v18), la racine « Accueil » (créée en premier, présente
    /// sur un vieux .plot, ni conteneur ni supprimable ni renommable), et le
    /// temps écoulé en clair.</summary>
    public static class HomeTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C19 — l'Accueil");
            RecentsRules(t);
            RecentsPersistence(t);
            Elapsed(t);
            PinPersistence(t);
            HomeRoot(t);
            EmptyBlocks(t);
            PinUndo(t);
        }

        /// <summary>L'épingle passe par l'historique : bascule, Ctrl+Z, Ctrl+Y.</summary>
        private static void PinUndo(Harness t)
        {
            var project = Project.CreateNew();
            var chapter = project.Category(Project.KeyWritings).Children[0];
            var history = new HistoryManager();
            history.Run(new PinItemAction(chapter));
            t.Check(chapter.Pinned, "épingler pose Pinned");
            history.Undo();
            t.Check(!chapter.Pinned, "Ctrl+Z le défait");
            history.Redo();
            t.Check(chapter.Pinned, "Ctrl+Y le refait");
            history.Run(new PinItemAction(chapter));
            t.Check(!chapter.Pinned, "« Ne plus épingler » bascule dans l'autre sens");
            history.Undo();
            t.Check(chapter.Pinned, "…et s'annule aussi");
        }

        /// <summary>Un projet neuf n'a ni récent, ni épingle, ni objectif, ni
        /// livre : chaque bloc montre son invite (Commencer vit dans le
        /// panneau Général du rail depuis le b43).</summary>
        private static void EmptyBlocks(Harness t)
        {
            var project = Project.CreateNew();
            project.Category(Project.KeyWritings).Children.Clear(); // même pas l'écrit d'amorce
            var view = new UniversSale.View.HomeView();
            view.Load(project);
            var prompts = view.Prompts;
            t.Equal(3, prompts.Count, "trois invites d'état vide sur un projet neuf");
            t.Check(prompts.Contains("Les écrits ouverts récemment apparaîtront ici."), "l'invite de Reprendre");
            t.Check(prompts.Contains("Clic droit sur un élément → Épingler."), "l'invite d'Épinglés");
            t.Check(prompts.Contains("Définis un objectif pour suivre ta progression."), "l'invite d'Où j'en suis");
            t.Check(view.ResumeItems.Count == 0 && view.PinnedItems.Count == 0 && view.ProgressBars == 0, "aucune ligne, aucune barre");

            // — Un livre avec objectif, un objectif journalier, un épinglé et
            //   un récent : les invites cèdent la place au contenu.
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo { ChapterGoal = 4 } };
            var chapter = new BinderItem { Kind = ItemKind.Text, Title = "Un", Status = "done" };
            book.Children.Add(chapter);
            project.Category(Project.KeyWritings).Children.Add(book);
            project.RelinkParents();
            project.Journal.DailyGoal = 500;
            chapter.Pinned = true;
            Recents.Touch(project.Recents, chapter.Id, Recents.Now());
            view.Refresh();
            t.Equal(0, view.Prompts.Count, "avec du contenu, plus aucune invite");
            t.Check(view.ResumeItems.Count == 1 && view.ResumeItems[0] == chapter, "Reprendre montre le chapitre récent");
            t.Check(view.PinnedItems.Count == 1 && view.PinnedItems[0] == chapter, "Épinglés montre le chapitre épinglé");
            t.Equal(2, view.ProgressBars, "Où j'en suis : la barre du jour et celle du livre");

            // — Le filtre Corbeille (A3) vaut pour Reprendre ET Épinglés.
            book.Children.Remove(chapter);
            project.Trash.Children.Add(chapter);
            project.RelinkParents();
            view.Refresh();
            t.Check(view.ResumeItems.Count == 0 && view.PinnedItems.Count == 0 && chapter.Pinned,
                "un item jeté disparaît des deux blocs, son épingle voyage avec lui");
        }

        private static void HomeRoot(Harness t)
        {
            var project = Project.CreateNew();
            var home = project.Category(Project.KeyHome);
            t.Check(home != null && project.Roots.IndexOf(home) == 0 && project.Roots[1].CategoryKey == Project.KeyWritings,
                "un projet neuf a l'Accueil en première racine, avant Écrits");
            t.Check(home.IsHomeRoot && home.IsCategory && !home.CanHaveChildren && !home.IsContainer,
                "l'Accueil est une racine qui n'est ni un conteneur ni un dossier (aucun enfant)");
            t.Equal("Accueil", home.Title, "…titrée « Accueil »");

            // — Un vieux .plot (sans racine Accueil) la reçoit au chargement, en premier.
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c19-home");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "vieux.plot");
            try
            {
                project.Roots.Remove(home);
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                var back = loaded.Category(Project.KeyHome);
                t.Check(back != null && loaded.Roots.IndexOf(back) == 0 && loaded.Roots[1].CategoryKey == Project.KeyWritings,
                    "un vieux .plot sans Accueil l'obtient au chargement, avant Écrits");
                t.Equal(0, back.Children.Count, "…vide");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static void RecentsRules(Harness t)
        {
            var recents = new List<RecentEntry>();
            t.Check(Recents.Touch(recents, "a", "2026-08-30 10:00:00") && recents.Count == 1 && recents[0].ItemId == "a",
                "un item ouvert entre en tête");
            Recents.Touch(recents, "b", "2026-08-30 10:01:00");
            Recents.Touch(recents, "c", "2026-08-30 10:02:00");
            t.Equal("c,b,a", Join(recents), "le plus récent en tête");
            Recents.Touch(recents, "a", "2026-08-30 10:03:00");
            t.Equal("a,c,b", Join(recents), "un doublon remonte en tête, sans créer d'entrée");
            t.Equal("2026-08-30 10:03:00", recents[0].Date, "…avec la nouvelle date");
            for (var i = 0; i < 15; i++) Recents.Touch(recents, "x" + i, "2026-08-30 11:" + i.ToString("00") + ":00");
            t.Equal(Recents.Cap, recents.Count, "plafond à dix");
            t.Equal("x14", recents[0].ItemId, "…les plus anciens tombent, le plus récent reste en tête");
            t.Check(!Recents.Touch(recents, null, "2026-08-30 12:00:00") && !Recents.Touch(recents, "", "2026-08-30 12:00:00"),
                "un identifiant vide n'entre pas");

            // — Valid : l'item doit exister et ne pas être à la Corbeille ;
            //   une racine n'est jamais un récent.
            var project = Project.CreateNew();
            var chapter = project.Category(Project.KeyWritings).Children[0];
            var trashed = new BinderItem { Kind = ItemKind.Text, Title = "Jeté" };
            project.Trash.Children.Add(trashed);
            project.RelinkParents();
            project.Recents.Clear();
            Recents.Touch(project.Recents, "mort", "2026-08-30 09:00:00");
            Recents.Touch(project.Recents, trashed.Id, "2026-08-30 09:30:00");
            Recents.Touch(project.Recents, Project.KeyWritings, "2026-08-30 09:40:00");
            Recents.Touch(project.Recents, chapter.Id, "2026-08-30 10:00:00");
            var valid = Recents.Valid(project);
            t.Check(valid.Count == 1 && valid[0].ItemId == chapter.Id,
                "à l'affichage : l'item mort, l'item jeté et la racine sont ignorés, le chapitre reste");
            t.Equal(4, project.Recents.Count, "…sans que la liste soit touchée");
            t.Equal(1, Recents.Purge(project), "la purge retire l'entrée morte seulement");
            t.Equal(3, project.Recents.Count, "…l'item jeté reste (il peut être restauré)");
        }

        private static void RecentsPersistence(Harness t)
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c19");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "recents.plot");
            try
            {
                var project = Project.CreateNew();
                var chapter = project.Category(Project.KeyWritings).Children[0];
                Recents.Touch(project.Recents, "fantome", "2026-08-29 08:00:00");
                Recents.Touch(project.Recents, chapter.Id, "2026-08-30 10:00:00");
                PlotFile.Save(project, path);

                // — La sauvegarde n'a PAS purgé : le manifeste porte l'entrée morte.
                string manifest;
                using (var archive = ZipFile.OpenRead(path))
                using (var reader = new StreamReader(archive.GetEntry("manifest.json").Open()))
                    manifest = reader.ReadToEnd();
                t.Check(manifest.Contains("\"recents\"") && manifest.Contains("fantome"),
                    "la sauvegarde écrit les récents tels quels, entrée morte comprise (jamais purgée à l'enregistrement)");

                // — Le chargement purge, et garde l'ordre.
                var loaded = PlotFile.Load(path);
                t.Check(loaded.Recents.Count == 1 && loaded.Recents[0].ItemId == chapter.Id && loaded.Recents[0].Date == "2026-08-30 10:00:00",
                    "le chargement purge l'entrée morte et relit l'autre, date comprise");

                // — Un projet sans récent n'écrit pas la clé.
                var bare = Project.CreateNew();
                PlotFile.Save(bare, path);
                using (var archive = ZipFile.OpenRead(path))
                using (var reader = new StreamReader(archive.GetEntry("manifest.json").Open()))
                    manifest = reader.ReadToEnd();
                t.Check(!manifest.Contains("\"recents\""), "sans récent, la clé est omise");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static void Elapsed(Harness t)
        {
            var now = new DateTime(2026, 8, 30, 14, 0, 0); // un dimanche
            t.Equal("à l'instant", Recents.Elapsed("2026-08-30 13:59:40", now), "moins d'une minute : à l'instant");
            t.Equal("il y a 20 min", Recents.Elapsed("2026-08-30 13:40:00", now), "vingt minutes");
            t.Equal("il y a 3 h", Recents.Elapsed("2026-08-30 10:30:00", now), "le même jour : en heures");
            t.Equal("hier", Recents.Elapsed("2026-08-29 23:30:00", now), "la veille : hier");
            t.Equal("lundi", Recents.Elapsed("2026-08-24 09:00:00", now), "moins d'une semaine : le jour");
            t.Equal("le 12 août", Recents.Elapsed("2026-08-12 09:00:00", now), "au-delà : la date");
            t.Equal("", Recents.Elapsed("n'importe quoi", now), "une date illisible : rien");
        }

        private static void PinPersistence(Harness t)
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c19-pin");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "pin.plot");
            try
            {
                var project = Project.CreateNew();
                var chapter = project.Category(Project.KeyWritings).Children[0];
                var other = new BinderItem { Kind = ItemKind.Text, Title = "Autre" };
                project.Category(Project.KeyWritings).Children.Add(other);
                project.RelinkParents();
                chapter.Pinned = true;
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                var back = loaded.FindById(chapter.Id);
                var backOther = loaded.FindById(other.Id);
                t.Check(back != null && back.Pinned && backOther != null && !backOther.Pinned,
                    "l'épingle fait l'aller-retour .plot v18, et seulement elle");
                string text;
                using (var archive = ZipFile.OpenRead(path))
                using (var reader = new StreamReader(archive.GetEntry("manifest.json").Open()))
                    text = reader.ReadToEnd();
                t.Check(text.Contains("\"pinned\":true") || text.Contains("\"pinned\": true"), "la clé pinned n'est écrite que vraie");
                t.Equal(1, CountOf(text, "pinned"), "…une seule fois (l'autre item l'omet)");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static int CountOf(string text, string needle)
        {
            var count = 0;
            var at = 0;
            while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
            return count;
        }

        private static string Join(List<RecentEntry> recents)
        {
            var ids = new string[recents.Count];
            for (var i = 0; i < recents.Count; i++) ids[i] = recents[i].ItemId;
            return string.Join(",", ids);
        }
    }
}

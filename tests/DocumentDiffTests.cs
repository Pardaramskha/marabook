using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C17 — les versions d'écrits (batch 38) : le diff à deux
    /// niveaux (paragraphes alignés, caractères sur les modifiés seulement),
    /// déplacement, réécriture au-delà du plafond, style seul, notes et
    /// annotations, documents vides et identiques, alignement grossier ; le
    /// rendu synthétique (barré / souligné, repli des inchangés, index de
    /// navigation) ; CharDiff depuis son nouveau foyer.</summary>
    public static class DocumentDiffTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C17 — versions d'écrits");
            CharDiffHome(t);
            Alignment(t);
            MovedAndRewritten(t);
            StyleOnly(t);
            Sides(t);
            Edges(t);
            Rendering(t);
            Capture(t);
            CapAndPurge(t);
            PlotRoundTrip(t);
            Guards(t);
            Restoration(t);
        }

        private static void Restoration(Harness t)
        {
            BinderItem chapter;
            var project = Cast(out chapter);
            var history = new History.HistoryManager();
            var departure = SnapshotStore.Capture(project, chapter, "Départ", SnapshotOrigin.Manual, 20);
            chapter.Document.Paragraphs[0].Runs[0].Text = "Le marabout s'envole.";
            chapter.Document.Paragraphs.Add(new TextParagraph());
            chapter.Document.Paragraphs[2].Runs.Add(new TextRun { Text = "Un troisième paragraphe." });

            // — La ceinture, puis UNE action annulable.
            var guard = SnapshotStore.GuardBeforeRestore(project, chapter, departure.DisplayLabel, 20);
            t.Check(guard != null && guard.Origin == SnapshotOrigin.Restore, "restaurer fige d'abord l'état courant (« avant restauration »)");
            var action = new History.RestoreSnapshotAction(chapter, departure.Document, departure.DisplayLabel);
            history.Run(action);
            t.Check(!action.Conflict && chapter.Document.Paragraphs.Count == 2 && PivotEdit.FlatText(chapter.Document.Paragraphs[0]) == "Le marabout dort.",
                "la restauration remet le document dans l'état de l'instantané, en place");
            t.Check(!ReferenceEquals(chapter.Document.Paragraphs[0], departure.Document.Paragraphs[0]), "…par des clones (l'instantané reste intact)");
            history.Undo();
            t.Check(!action.Conflict && chapter.Document.Paragraphs.Count == 3 && PivotEdit.FlatText(chapter.Document.Paragraphs[0]) == "Le marabout s'envole.",
                "un Undo rend l'état d'avant, troisième paragraphe compris");
            history.Redo();
            t.Check(chapter.Document.Paragraphs.Count == 2, "Redo rejoue");
            history.Undo();
            t.Check(PivotEdit.FlatText(guard.Document.Paragraphs[0]) == "Le marabout s'envole.", "l'instantané « avant restauration » permet de revenir même après fermeture");

            // — Conflit : l'item a changé entre la construction et l'application.
            var stale = new History.RestoreSnapshotAction(chapter, departure.Document, "Départ");
            chapter.Document.Paragraphs[1].Runs[0].Text = "Quelqu'un a tapé ici.";
            stale.Do();
            t.Check(stale.Conflict && PivotEdit.FlatText(chapter.Document.Paragraphs[1]) == "Quelqu'un a tapé ici.", "un item modifié entre-temps n'est pas écrasé : conflit, rien d'écrit");
            var applied = new History.RestoreSnapshotAction(chapter, departure.Document, "Départ");
            applied.Do();
            chapter.Document.Paragraphs[0].Runs[0].Text = "Modifié après restauration.";
            applied.Undo();
            t.Check(applied.Conflict && PivotEdit.FlatText(chapter.Document.Paragraphs[0]) == "Modifié après restauration.", "…et l'annulation aussi refuse d'écraser une frappe postérieure");

            // — Restauration partielle : un paragraphe, dans chaque sort.
            var older = TextDocument.FromPlainText("Un.\nDeux.\nTrois.\nQuatre.");
            var live = new BinderItem { Kind = ItemKind.Text, Title = "Partiel" };
            live.Document = TextDocument.FromPlainText("Un.\nDeux bis.\nQuatre.\nCinq.");
            var delta = DocumentDiff.Compare(older, live.Document);
            ParagraphDelta modified = null, removed = null, added = null;
            foreach (var p in delta.Paragraphs)
            {
                if (p.Kind == ParagraphChange.Modified) modified = p;
                if (p.Kind == ParagraphChange.Removed) removed = p;
                if (p.Kind == ParagraphChange.Added) added = p;
            }
            t.Check(modified != null && removed != null && added != null, "la fixture partielle : un modifié, un supprimé, un ajouté");
            t.Check(History.RestoreParagraphAction.CanRestore(modified) && !History.RestoreParagraphAction.CanRestore(delta.Paragraphs[0]), "restaurable : un changement, pas un inchangé");
            var partial = new History.RestoreParagraphAction(live, modified);
            history.Run(partial);
            t.Check(!partial.Conflict && PivotEdit.FlatText(live.Document.Paragraphs[1]) == "Deux." && live.Document.Paragraphs.Count == 4, "restaurer un paragraphe modifié le remet, seul");
            history.Undo();
            t.Check(PivotEdit.FlatText(live.Document.Paragraphs[1]) == "Deux bis.", "…annulable");
            var reinsert = new History.RestoreParagraphAction(live, removed);
            history.Run(reinsert);
            t.Check(!reinsert.Conflict && live.Document.Paragraphs.Count == 5 && PivotEdit.FlatText(live.Document.Paragraphs[2]) == "Trois.", "restaurer un supprimé le réinsère à sa place");
            history.Undo();
            t.Check(live.Document.Paragraphs.Count == 4 && PivotEdit.FlatText(live.Document.Paragraphs[2]) == "Quatre.", "…annulable");
            var drop = new History.RestoreParagraphAction(live, added);
            history.Run(drop);
            t.Check(!drop.Conflict && live.Document.Paragraphs.Count == 3, "restaurer un ajouté le retire");
            history.Undo();
            t.Check(live.Document.Paragraphs.Count == 4 && PivotEdit.FlatText(live.Document.Paragraphs[3]) == "Cinq.", "…annulable");
            var conflict = new History.RestoreParagraphAction(live, modified);
            live.Document.Paragraphs[1].Runs[0].Text = "Deux ter.";
            conflict.Do();
            t.Check(conflict.Conflict && PivotEdit.FlatText(live.Document.Paragraphs[1]) == "Deux ter.", "un paragraphe changé entre-temps n'est pas écrasé");
        }

        private static void Guards(Harness t)
        {
            BinderItem chapter;
            var project = Cast(out chapter);

            // — Avant passe typographique : un instantané, et pas deux.
            var typo = SnapshotStore.GuardBeforeTypography(project, chapter, 20);
            t.Check(typo != null && typo.Origin == SnapshotOrigin.Typography && typo.DisplayLabel == "Avant passe typographique", "un instantané automatique avant la passe typographique");
            t.Check(SnapshotStore.GuardBeforeTypography(project, chapter, 20) == null && project.Snapshots.Count == 1, "…aucun en double si rien n'a changé");

            // — Avant remplacement projet : un par item touché (écrits et fiches), libellé.
            var other = new BinderItem { Kind = ItemKind.Text, Title = "Autre" };
            other.Document = TextDocument.FromPlainText("Un marabout ailleurs.");
            project.Category(Project.KeyWritings).Children.Add(other);
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche" };
            sheet.Document = TextDocument.FromPlainText("Un marabout de fiche.");
            project.Category(Project.KeySheets).Children.Add(sheet);
            var plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan", Plan = new PlanInfo() };
            project.Category(Project.KeyPlans).Children.Add(plan);
            project.RelinkParents();
            var taken = SnapshotStore.GuardBeforeReplace(project, new List<BinderItem> { chapter, other, sheet, plan }, "marabout", 20);
            t.Equal(1, taken, "avant remplacement : un instantané par écrit touché non encore capturé tel quel (le chapitre l'était ; les fiches n'ont plus de versions depuis le b43)");
            var replaceLabel = SnapshotStore.Latest(project, other.Id);
            t.Check(replaceLabel != null && replaceLabel.Origin == SnapshotOrigin.Replace && replaceLabel.Label == "Avant remplacement de « marabout »", "…libellé par le motif remplacé");
            t.Equal(0, SnapshotStore.GuardBeforeReplace(project, new List<BinderItem> { chapter, other, sheet }, "marabout", 20), "rejouer sans changement : aucun en double");
            chapter.Document.Paragraphs[0].Runs[0].Text = "Le marabout s'est envolé.";
            t.Equal(1, SnapshotStore.GuardBeforeReplace(project, new List<BinderItem> { chapter, other }, "envolé", 20), "un item changé : un nouvel instantané pour lui seul");

            // — Avant restauration : l'état courant, libellé par la version restaurée.
            chapter.Document.Paragraphs[0].Runs[0].Text = "Encore autre chose.";
            var restore = SnapshotStore.GuardBeforeRestore(project, chapter, "Départ", 20);
            t.Check(restore != null && restore.Origin == SnapshotOrigin.Restore && restore.Label == "Avant restauration de « Départ »", "avant restauration : l'état courant est figé");

            // — La capture quotidienne : l'état d'avant la frappe, une fois par jour, débrayable.
            var fresh = Project.CreateNew();
            var text = fresh.Category(Project.KeyWritings).Children[0];
            text.Document = TextDocument.FromPlainText("Avant la frappe.");
            var atOpen = PivotEdit.Clone(text.Document);
            text.Document.Paragraphs[0].Runs[0].Text = "Avant la frappeX";
            t.Check(SnapshotStore.GuardDaily(fresh, text, atOpen, false, 20) == null && fresh.Snapshots.Count == 0, "débrayée : rien");
            var daily = SnapshotStore.GuardDaily(fresh, text, atOpen, true, 20);
            t.Check(daily != null && daily.Origin == SnapshotOrigin.Daily && PivotEdit.FlatText(daily.Document.Paragraphs[0]) == "Avant la frappe.", "la capture quotidienne fige l'état d'AVANT la première frappe");
            text.Document.Paragraphs[0].Runs[0].Text = "Avant la frappeXY";
            t.Check(SnapshotStore.GuardDaily(fresh, text, null, true, 20) == null && fresh.Snapshots.Count == 1, "…une seule par jour et par item");
            var second = new BinderItem { Kind = ItemKind.Text, Title = "Second" };
            second.Document = TextDocument.FromPlainText("Second texte.");
            fresh.Category(Project.KeyWritings).Children.Add(second);
            t.Check(SnapshotStore.GuardDaily(fresh, second, null, true, 20) != null, "…un autre item a la sienne (sur le document courant faute de mieux)");
            SnapshotStore.Capture(fresh, text, "manuel du jour", SnapshotOrigin.Manual, 20);
            t.Check(SnapshotStore.HasToday(fresh, text.Id, null) && SnapshotStore.GuardDaily(fresh, text, null, true, 20) == null, "toute capture du jour dispense de la quotidienne");
        }

        private static Project Cast(out BinderItem chapter)
        {
            var project = Project.CreateNew();
            chapter = project.Category(Project.KeyWritings).Children[0];
            chapter.Document = TextDocument.FromPlainText("Le marabout dort.\nUn marabout veille.");
            return project;
        }

        private static void Capture(Harness t)
        {
            BinderItem chapter;
            var project = Cast(out chapter);
            var first = SnapshotStore.Capture(project, chapter, "Départ", SnapshotOrigin.Manual, 20);
            t.Check(first != null && project.Snapshots.Count == 1, "une capture manuelle entre dans le projet");
            t.Check(first.ItemId == chapter.Id && first.Label == "Départ" && first.Origin == SnapshotOrigin.Manual && !first.IsAutomatic, "…avec item, libellé, origine");
            t.Equal(6, first.Words, "le compte de mots est FIGÉ à la capture");
            t.Check(first.Date.Length == 19 && first.Json.Length > 0 && first.Bytes > 0, "date, JSON produit une fois, poids connu");
            t.Equal("Départ", first.DisplayLabel, "le libellé affiché est celui de l'auteur");
            t.Check(SnapshotStore.Capture(project, chapter, "", SnapshotOrigin.Manual, 20) == null && project.Snapshots.Count == 1, "même contenu : pas de doublon");
            chapter.Document.Paragraphs[0].Runs[0].Text = "Le marabout dort profondément.";
            var second = SnapshotStore.Capture(project, chapter, "", SnapshotOrigin.Typography, 20);
            t.Check(second != null && second.IsAutomatic && second.DisplayLabel == "Avant passe typographique", "un contenu changé : nouvelle capture, automatique, libellée par son origine");
            t.Equal(7, second.Words, "…son propre compte de mots");
            var restored = second.Document;
            t.Check(!ReferenceEquals(restored, chapter.Document) && PivotEdit.FlatText(restored.Paragraphs[0]) == "Le marabout dort profondément.", "le document décodé paresseusement est une copie fidèle et indépendante");
            t.Check(ReferenceEquals(restored, second.Document), "…décodée une fois");
            t.Check(PivotEdit.FlatText(first.Document.Paragraphs[0]) == "Le marabout dort.", "le premier instantané n'a pas bougé");
            var listed = SnapshotStore.Of(project, chapter.Id);
            t.Check(listed.Count == 2 && listed[0] == second && listed[1] == first, "les instantanés d'un item, du plus récent au plus ancien");
            t.Check(SnapshotStore.Latest(project, chapter.Id) == second, "le dernier");
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche" };
            sheet.Document = TextDocument.FromPlainText("Corps.");
            project.Category(Project.KeySheets).Children.Add(sheet);
            t.Check(SnapshotStore.Capture(project, sheet, "", SnapshotOrigin.Manual, 20) == null, "une fiche ne se capture plus (b43)");
            var plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan", Plan = new PlanInfo() };
            t.Check(SnapshotStore.Capture(project, plan, "", SnapshotOrigin.Manual, 20) == null, "un plan n'a pas de document : rien (limite assumée)");
            t.Check(SnapshotStore.HasToday(project, chapter.Id, SnapshotOrigin.Typography) && !SnapshotStore.HasToday(project, chapter.Id, SnapshotOrigin.Daily), "HasToday sait quelle origine a déjà été prise aujourd'hui");
            t.Equal("Avant remplacement", SnapshotOrigin.Label(SnapshotOrigin.Replace), "les origines ont un libellé");
            var stats = SnapshotStore.Weight(project, null);
            t.Check(stats.StartsWith("2 versions · ") && stats.EndsWith(" o") || stats.EndsWith(" Ko"), "le poids se dit (« " + stats + " »)");
        }

        private static void CapAndPurge(Harness t)
        {
            BinderItem chapter;
            var project = Cast(out chapter);
            // Alternance : manuel, auto, manuel, auto… sur des contenus distincts.
            for (var i = 0; i < 8; i++)
            {
                chapter.Document.Paragraphs[0].Runs[0].Text = "Version " + i;
                SnapshotStore.Capture(project, chapter, "v" + i, i % 2 == 0 ? SnapshotOrigin.Manual : SnapshotOrigin.Daily, 100);
            }
            t.Equal(8, project.Snapshots.Count, "huit captures distinctes");
            t.Equal(3, SnapshotStore.Trim(project, chapter.Id, 5), "plafond 5 : trois évincés");
            var labels = new List<string>();
            foreach (var s in project.Snapshots) labels.Add(s.Label + (s.IsAutomatic ? "a" : "m"));
            t.Equal("v0m v2m v4m v6m v7a", string.Join(" ", labels.ToArray()), "les automatiques partent d'abord, du plus ancien au plus récent ; un manuel reste tant qu'il reste un automatique");
            t.Equal(2, SnapshotStore.Trim(project, chapter.Id, 3), "plafond 3 : encore deux");
            labels.Clear();
            foreach (var s in project.Snapshots) labels.Add(s.Label);
            t.Equal("v4 v6 v7", string.Join(" ", labels.ToArray()), "…v7 est le plus récent (jamais évincé), donc les manuels les plus anciens partent (v0, v2)");
            chapter.Document.Paragraphs[0].Runs[0].Text = "Version neuve";
            SnapshotStore.Capture(project, chapter, "", SnapshotOrigin.Replace, 3);
            labels.Clear();
            foreach (var s in project.Snapshots) labels.Add(s.Label.Length > 0 ? s.Label : "auto");
            t.Equal("v4 v6 auto", string.Join(" ", labels.ToArray()), "une capture au plafond entre et évince le manuel le plus ancien — jamais elle-même");
            t.Check(SnapshotStore.Latest(project, chapter.Id).Origin == SnapshotOrigin.Replace, "…c'est la dernière");
            t.Equal(1, SnapshotStore.PurgeAutomatic(project, chapter.Id), "purge des automatiques de l'item");
            t.Equal(2, SnapshotStore.Count(project, chapter.Id), "…les manuels restent");
            var other = new BinderItem { Kind = ItemKind.Text, Title = "Autre" };
            other.Document = TextDocument.FromPlainText("Autre texte.");
            project.Category(Project.KeyWritings).Children.Add(other);
            SnapshotStore.Capture(project, other, "", SnapshotOrigin.Daily, 20);
            project.Snapshots[project.Snapshots.Count - 1].Date = "2020-01-01 10:00:00";
            t.Equal(1, SnapshotStore.PurgeOlderThan(project, 30), "purge de ce qui a plus de 30 jours");
            t.Equal(2, SnapshotStore.PurgeItem(project, chapter.Id), "purge de tout un item");
            t.Equal(0, project.Snapshots.Count, "…plus rien");
            t.Equal(0, SnapshotStore.PurgeAutomatic(project, null), "purge globale à vide : zéro");
        }

        private static void PlotRoundTrip(Harness t)
        {
            BinderItem chapter;
            var project = Cast(out chapter);
            SnapshotStore.Capture(project, chapter, "Départ", SnapshotOrigin.Manual, 20);
            chapter.Document.Paragraphs[1].Runs[0].Text = "Un marabout veille encore.";
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-c17-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                var path = System.IO.Path.Combine(dir, "v17.plot");
                Persistence.PlotFile.Save(project, path);
                using (var archive = System.IO.Compression.ZipFile.OpenRead(path))
                {
                    var found = 0;
                    foreach (var entry in archive.Entries) if (entry.FullName.StartsWith("snapshots/" + chapter.Id + "/")) found++;
                    t.Equal(1, found, "l'instantané est sa propre entrée snapshots/<item>/<id>.json");
                    var manifest = new System.IO.StreamReader(archive.GetEntry("manifest.json").Open()).ReadToEnd();
                    t.Check(!manifest.Contains("snapshot"), "…et rien dans le manifeste");
                }
                var reloaded = Persistence.PlotFile.Load(path);
                t.Equal(1, reloaded.Snapshots.Count, "relu : un instantané");
                var back = reloaded.Snapshots[0];
                t.Check(back.Label == "Départ" && back.Words == 6 && back.Fingerprint == project.Snapshots[0].Fingerprint && back.Json == project.Snapshots[0].Json
                    && back.Bytes == project.Snapshots[0].Bytes,
                    "…libellé, mots, empreinte et JSON tel quel, octet pour octet (immuable)");
                t.Equal("Un marabout veille.", PivotEdit.FlatText(back.Document.Paragraphs[1]), "…et son document se décode");
                t.Equal("Un marabout veille encore.", PivotEdit.FlatText(reloaded.FindById(chapter.Id).Document.Paragraphs[1]), "le document vivant est celui d'après");

                // — La corbeille (A1) : vider la corbeille, ENREGISTRER, Ctrl+Z → les
                // versions sont toujours là, en mémoire comme dans le fichier.
                var history = new History.HistoryManager();
                var trash = project.Trash;
                var writings = project.Category(Project.KeyWritings);
                writings.Children.Remove(chapter);
                trash.Children.Add(chapter);
                chapter.Parent = trash;
                history.Run(new History.EmptyTrashAction(trash));
                t.Check(project.FindById(chapter.Id) == null, "le chapitre est purgé de la corbeille");
                Persistence.PlotFile.Save(project, path);
                t.Equal(1, project.Snapshots.Count, "enregistrer ne purge PAS les instantanés orphelins");
                history.Undo();
                t.Check(project.FindById(chapter.Id) == chapter && SnapshotStore.Count(project, chapter.Id) == 1, "Ctrl+Z ressuscite le chapitre AVEC ses versions");
                Persistence.PlotFile.Save(project, path);
                t.Equal(1, Persistence.PlotFile.Load(path).Snapshots.Count, "…et le fichier les a toujours");

                // — Un orphelin à l'OUVERTURE (l'item n'existe plus) est abandonné.
                history.Run(new History.EmptyTrashAction(trash));
                Persistence.PlotFile.Save(project, path);
                var warnings = new List<string>();
                var orphaned = Persistence.PlotFile.Load(path, warnings);
                t.Check(orphaned.Snapshots.Count == 0 && warnings.Count == 1 && warnings[0].Contains("instantané"), "à l'ouverture, un instantané sans item est abandonné, et dit");

                // — Un .plot d'avant s'ouvre sans instantané.
                var old = Project.CreateNew();
                var oldPath = System.IO.Path.Combine(dir, "v16.plot");
                Persistence.PlotFile.Save(old, oldPath);
                t.Equal(0, Persistence.PlotFile.Load(oldPath).Snapshots.Count, "un projet sans entrée snapshots/ : aucun instantané");
            }
            finally
            {
                try { System.IO.Directory.Delete(dir, true); } catch (System.IO.IOException) { }
            }
        }

        private static TextDocument Doc(params string[] paragraphs)
        {
            var document = new TextDocument();
            foreach (var text in paragraphs)
            {
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun { Text = text });
                document.Paragraphs.Add(paragraph);
            }
            return document;
        }

        private static string Kinds(DocumentDelta delta)
        {
            var parts = new List<string>();
            foreach (var paragraph in delta.Paragraphs)
            {
                switch (paragraph.Kind)
                {
                    case ParagraphChange.Unchanged: parts.Add("="); break;
                    case ParagraphChange.Added: parts.Add("+"); break;
                    case ParagraphChange.Removed: parts.Add("-"); break;
                    case ParagraphChange.Modified: parts.Add("~"); break;
                    case ParagraphChange.Rewritten: parts.Add("!"); break;
                    case ParagraphChange.Moved: parts.Add(">"); break;
                    case ParagraphChange.StyleOnly: parts.Add("s"); break;
                }
            }
            return string.Join("", parts.ToArray());
        }

        private static void CharDiffHome(Harness t)
        {
            // Les mêmes vérifications que C13, depuis Model.
            var ops = Model.CharDiff.Diff("l'ami...", "l’ami…");
            t.Check(ops != null && ops.Count > 0, "CharDiff vit dans Model et diffe toujours");
            t.Equal(0, Model.CharDiff.Diff("", "").Count, "diff vide");
            t.Check(Model.CharDiff.Diff("abc", "abc").Count == 3, "identiques : trois conservés");
            t.Check(Model.CharDiff.Diff(new string('a', 2000), new string('b', 2000)) == null, "plafond de 800 : null, jamais une boucle");
        }

        private static void Alignment(Harness t)
        {
            var old = Doc("Un", "Deux", "Trois", "Quatre");
            var fresh = Doc("Un", "Deux bis", "Trois", "Cinq", "Quatre");
            var delta = DocumentDiff.Compare(old, fresh);
            t.Equal("=~=+=", Kinds(delta), "inchangé, modifié, inchangé, ajouté, inchangé — dans l'ordre de la version récente");
            t.Check(delta.Modified == 1 && delta.Added == 1 && delta.Removed == 0 && delta.Unchanged == 3, "les comptes");
            var modified = delta.Paragraphs[1];
            t.Check(modified.OldIndex == 1 && modified.NewIndex == 1 && modified.Ops != null, "le paragraphe modifié connaît ses deux places et son diff de caractères");
            var inserted = "";
            foreach (var op in modified.Ops) if (op.Type == '+') inserted += op.Char;
            t.Equal(" bis", inserted, "…qui ne voit que l'insertion");
            t.Check(delta.Paragraphs[3].OldIndex == -1 && delta.Paragraphs[3].NewIndex == 3, "l'ajouté n'a pas d'ancienne place");

            var removed = DocumentDiff.Compare(Doc("Un", "Deux", "Trois"), Doc("Un", "Trois"));
            t.Equal("=-=", Kinds(removed), "un paragraphe supprimé reste à sa place dans la lecture");
            t.Check(removed.Paragraphs[1].NewIndex == -1 && removed.Paragraphs[1].OldIndex == 1, "…sans nouvelle place");
            t.Equal("1 paragraphe supprimé", removed.Summary(), "le compte honnête");
            t.Equal("1 paragraphe modifié, 1 ajouté", delta.Summary(), "…au pluriel des natures");

            // Un bloc de deux supprimés et trois ajoutés : appariés dans l'ordre, le reste ajouté.
            var block = DocumentDiff.Compare(Doc("A", "B x", "C x", "D"), Doc("A", "B y", "C y", "E", "D"));
            t.Equal("=~~+=", Kinds(block), "dans un bloc, supprimés et ajoutés s'apparient dans l'ordre");
        }

        private static void MovedAndRewritten(Harness t)
        {
            var old = Doc("Un", "Deux", "Trois", "Quatre");
            var fresh = Doc("Un", "Trois", "Quatre", "Deux");
            var delta = DocumentDiff.Compare(old, fresh);
            t.Equal("===>", Kinds(delta), "un paragraphe déplacé est reconnu déplacé, pas supprimé puis ajouté");
            t.Check(delta.Moved == 1 && delta.Removed == 0 && delta.Added == 0, "…et compté une fois");
            t.Check(delta.Paragraphs[3].OldIndex == 1 && delta.Paragraphs[3].NewIndex == 3, "…avec ses deux places");
            t.Equal("1 paragraphe déplacé", delta.Summary(), "dit");

            var swapped = DocumentDiff.Compare(Doc("Un", "Deux", "Trois", "Quatre"), Doc("Quatre", "Deux", "Trois", "Un"));
            t.Check(swapped.Moved == 2 && swapped.Unchanged == 2, "deux paragraphes échangés autour d'un cœur stable : deux déplacés");
            var emptyMove = DocumentDiff.Compare(Doc("A", "", "B"), Doc("A", "B", ""));
            t.Check(emptyMove.Moved == 0, "un paragraphe vide n'est jamais « déplacé »");

            var rewritten = DocumentDiff.Compare(Doc("Un", new string('a', 1200), "Trois"), Doc("Un", new string('b', 1200), "Trois"));
            t.Equal("=!=", Kinds(rewritten), "au-delà du plafond de CharDiff : entièrement réécrit");
            t.Check(rewritten.Paragraphs[1].Ops == null && rewritten.Rewritten == 1, "…sans diff de caractères");
            t.Equal("1 paragraphe entièrement réécrit", rewritten.Summary(), "dit");
        }

        private static void StyleOnly(Harness t)
        {
            var old = Doc("Un", "Deux");
            var fresh = Doc("Un", "Deux");
            fresh.Paragraphs[1].StyleId = "heading1";
            var delta = DocumentDiff.Compare(old, fresh);
            t.Equal("=s", Kinds(delta), "même texte, autre style : style seul");
            t.Equal("1 changement de style seul", delta.Summary(), "dit");
            var bold = Doc("Un", "Deux");
            bold.Paragraphs[0].Runs[0].Bold = true;
            t.Equal("s=", Kinds(DocumentDiff.Compare(old, bold)), "une mise en forme de run changée compte aussi comme style seul");
            var split = Doc("Un", "Deux");
            split.Paragraphs[0].Runs.Clear();
            split.Paragraphs[0].Runs.Add(new TextRun { Text = "U" });
            split.Paragraphs[0].Runs.Add(new TextRun { Text = "n" });
            t.Equal("s=", Kinds(DocumentDiff.Compare(old, split)), "un découpage de runs différent (même texte, même format) est signalé — limite assumée");
            var aligned = Doc("Un", "Deux");
            aligned.Paragraphs[1].AlignOverride = "center";
            t.Equal("=s", Kinds(DocumentDiff.Compare(old, aligned)), "un alignement forcé aussi");
        }

        private static void Sides(Harness t)
        {
            var old = Doc("Un");
            var kept = new Footnote { Text = "gardée" };
            var changed = new Footnote { Text = "avant" };
            var dropped = new Footnote { Text = "partie" };
            old.Footnotes.Add(kept); old.Footnotes.Add(changed); old.Footnotes.Add(dropped);
            var pending = new Annotation { Text = "à revoir" };
            var done = new Annotation { Text = "fait" };
            old.Annotations.Add(pending); old.Annotations.Add(done);
            var fresh = Doc("Un");
            fresh.Footnotes.Add(new Footnote { Id = kept.Id, Text = "gardée" });
            fresh.Footnotes.Add(new Footnote { Id = changed.Id, Text = "après" });
            fresh.Footnotes.Add(new Footnote { Text = "neuve" });
            fresh.Annotations.Add(new Annotation { Id = pending.Id, Text = "à revoir", Resolved = true });
            fresh.Annotations.Add(new Annotation { Text = "nouvelle remarque" });
            var delta = DocumentDiff.Compare(old, fresh);
            t.Check(delta.ChangeCount == 0 && !delta.IsIdentical, "aucun paragraphe changé, mais des différences");
            var notes = new List<string>();
            foreach (var note in delta.Footnotes) notes.Add(note.Kind);
            notes.Sort();
            t.Equal("added modified removed", string.Join(" ", notes.ToArray()), "notes : ajoutée, modifiée, supprimée (par identifiant)");
            var annotations = new List<string>();
            foreach (var annotation in delta.Annotations) annotations.Add(annotation.Kind);
            annotations.Sort();
            t.Equal("added removed resolved", string.Join(" ", annotations.ToArray()), "annotations : ajoutée, résolue, supprimée");
            t.Equal("notes : 1 ajoutée, 1 supprimée, 1 modifiée · annotations : 1 ajoutée, 1 supprimée, 1 résolue", delta.Summary(), "le récapitulatif des notes et annotations");
            var reopened = Doc("Un");
            reopened.Annotations.Add(new Annotation { Id = pending.Id, Text = "à revoir", Resolved = false });
            t.Equal("reopened", DocumentDiff.Compare(fresh, reopened).Annotations[0].Kind, "une annotation rouverte");
        }

        private static void Edges(Harness t)
        {
            var same = DocumentDiff.Compare(Doc("Un", "Deux"), Doc("Un", "Deux"));
            t.Check(same.IsIdentical && same.ChangeCount == 0 && same.Unchanged == 2, "documents identiques : aucune différence");
            t.Equal("Aucune différence", same.Summary(), "…dit");
            var empties = DocumentDiff.Compare(new TextDocument(), new TextDocument());
            t.Check(empties.IsIdentical && empties.Paragraphs.Count == 0, "deux documents vides : identiques");
            t.Check(DocumentDiff.Compare(null, null).IsIdentical, "documents nuls : identiques");
            var fromEmpty = DocumentDiff.Compare(new TextDocument(), Doc("Un", "Deux"));
            t.Equal("++", Kinds(fromEmpty), "depuis le vide : tout ajouté");
            var toEmpty = DocumentDiff.Compare(Doc("Un", "Deux"), new TextDocument());
            t.Equal("--", Kinds(toEmpty), "vers le vide : tout supprimé");

            // Alignement grossier : plus d'éditions de paragraphes que la limite.
            var a = new List<string>();
            var b = new List<string>();
            for (var i = 0; i < DocumentDiff.AlignmentLimit; i++) { a.Add("ancien " + i); b.Add("nouveau " + i); }
            var coarse = DocumentDiff.Compare(Doc(a.ToArray()), Doc(b.ToArray()));
            t.Check(coarse.Coarse && coarse.Removed + coarse.Added + coarse.Modified + coarse.Rewritten == 2 * DocumentDiff.AlignmentLimit - DocumentDiff.AlignmentLimit,
                "au-delà de la limite d'alignement : grossier (tout supprimé + ajouté, appariés dans l'ordre), et dit");
            t.Check(coarse.Summary().Contains("trop différents"), "…dans le récapitulatif");
            var fine = new List<string>();
            for (var i = 0; i < 3000; i++) fine.Add("paragraphe " + i);
            var big = Doc(fine.ToArray());
            var edited = Doc(fine.ToArray());
            edited.Paragraphs[1500].Runs[0].Text = "paragraphe 1500 retouché";
            edited.Paragraphs.RemoveAt(10);
            var large = DocumentDiff.Compare(big, edited);
            t.Check(!large.Coarse && large.Modified == 1 && large.Removed == 1 && large.Unchanged == 2998, "3 000 paragraphes, deux retouches : alignement fin et rapide");
        }

        private static void Rendering(Harness t)
        {
            var old = Doc("Un", "Deux", "Trois", "Quatre", "Cinq", "Six", "Sept", "Huit");
            old.Paragraphs[1].Runs[0].Bold = true;
            var fresh = Doc("Un", "Deux et demi", "Trois", "Quatre", "Cinq", "Six", "Huit", "Neuf");
            fresh.Paragraphs[1].Runs[0].Bold = true;
            var delta = DocumentDiff.Compare(old, fresh);
            t.Equal("=~====-=+", Kinds(delta), "la fixture : un modifié, un supprimé, un ajouté");

            var rendered = DocumentDiff.Render(delta, false, 1);
            t.Equal(9, rendered.Paragraphs.Count, "sans repli : un paragraphe rendu par delta");
            var merged = rendered.Paragraphs[1];
            t.Equal("Deux et demi", PivotEdit.FlatText(merged), "le modifié montre l'ancien et le nouveau texte fondus");
            TextRun inserted = null;
            foreach (var run in merged.Runs) if (run.Underline == true) inserted = run;
            t.Check(inserted != null && inserted.Text == " et demi" && inserted.Color == DocumentDiff.AddedColor && inserted.Bold == true,
                "l'inséré est SOULIGNÉ (signal primaire), vert en renfort, et garde le format du run (gras)");
            var removedRun = rendered.Paragraphs[6].Runs[0];
            t.Check(removedRun.Strike == true && removedRun.Color == DocumentDiff.RemovedColor && removedRun.Text == "Sept", "un paragraphe supprimé est BARRÉ, rouge");
            var addedRun = rendered.Paragraphs[8].Runs[0];
            t.Check(addedRun.Underline == true && addedRun.Text == "Neuf", "un paragraphe ajouté est souligné");
            t.Check(delta.Paragraphs[6].RenderIndex == 6 && delta.Paragraphs[8].RenderIndex == 8, "chaque delta connaît sa place rendue");

            var folded = DocumentDiff.Render(delta, true, 1);
            // = ~ = = = = - = +  → « = » puis « ~ », puis 4 inchangés (Trois..Six) : 1 de contexte, 2 repliés, 1 de contexte, puis -, =, +.
            t.Equal(8, folded.Paragraphs.Count, "avec repli : la suite de quatre inchangés devient contexte + marqueur + contexte");
            t.Check(PivotEdit.FlatText(folded.Paragraphs[3]).Contains("2 paragraphes inchangés"), "le marqueur dit combien");
            t.Check(delta.Paragraphs[2].RenderIndex == 2 && delta.Paragraphs[3].RenderIndex == 3 && delta.Paragraphs[4].RenderIndex == 3 && delta.Paragraphs[5].RenderIndex == 4,
                "les repliés pointent le marqueur, le contexte garde sa place");

            // Modifié avec un texte supprimé : barré ; réécrit : deux paragraphes.
            var shrink = DocumentDiff.Compare(Doc("Le grand marabout"), Doc("Le marabout"));
            var shrinkDoc = DocumentDiff.Render(shrink, false, 1);
            var struck = "";
            foreach (var run in shrinkDoc.Paragraphs[0].Runs) if (run.Strike == true) struck += run.Text;
            t.Equal("grand ", struck, "le texte retiré d'un paragraphe modifié est barré, à sa place");
            var rewritten = DocumentDiff.Render(DocumentDiff.Compare(Doc(new string('a', 1200)), Doc(new string('b', 1200))), false, 1);
            t.Check(rewritten.Paragraphs.Count == 2 && rewritten.Paragraphs[0].Runs[0].Strike == true && rewritten.Paragraphs[1].Runs[0].Underline == true,
                "un réécrit : l'ancien barré puis le nouveau souligné");

            // Déplacé et style seul : la mention en tête ; les ancres d'annotation retirées ; les notes recopiées.
            var movedDoc = DocumentDiff.Render(DocumentDiff.Compare(Doc("A", "B"), Doc("B", "A")), false, 1);
            var mention = false;
            foreach (var paragraph in movedDoc.Paragraphs) if (PivotEdit.FlatText(paragraph).Contains("déplacé")) mention = true;
            t.Check(mention, "un déplacé porte sa mention");
            var styled = Doc("A");
            styled.Paragraphs[0].StyleId = "heading1";
            var styleDoc = DocumentDiff.Render(DocumentDiff.Compare(Doc("A"), styled), false, 1);
            t.Check(PivotEdit.FlatText(styleDoc.Paragraphs[0]).Contains("style « body » → « heading1 »"), "un style seul dit lequel");
            var annotated = Doc("A");
            annotated.Paragraphs[0].Runs[0].AnnotationId = "ann";
            annotated.Annotations.Add(new Annotation { Id = "ann", Text = "remarque" });
            var noteOld = Doc("A");
            noteOld.Footnotes.Add(new Footnote { Id = "n1", Text = "note" });
            noteOld.Paragraphs[0].Runs.Add(new TextRun { FootnoteId = "n1" });
            var annotatedDelta = DocumentDiff.Compare(noteOld, annotated);
            var annotatedDoc = DocumentDiff.Render(annotatedDelta, false, 1);
            var anchors = 0;
            foreach (var paragraph in annotatedDoc.Paragraphs) foreach (var run in paragraph.Runs) if (run.AnnotationId != null) anchors++;
            t.Equal(0, anchors, "les ancres d'annotation ne passent pas dans le document synthétique");
            DocumentDiff.CopyFootnotes(annotatedDoc, noteOld, annotated);
            t.Check(annotatedDoc.FindFootnote("n1") != null, "les notes des deux versions sont recopiées (les marqueurs se résolvent)");
            t.Check(!ReferenceEquals(annotatedDoc, annotated) && !ReferenceEquals(annotatedDoc.Paragraphs[0], annotated.Paragraphs[0]), "le document rendu est NEUF : jamais celui d'un item");
            t.Check(DocumentDiff.Render(null, true, 1).Paragraphs.Count == 0, "delta nul : document vide");
        }
    }
}

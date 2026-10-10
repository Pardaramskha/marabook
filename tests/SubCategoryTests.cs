using System;
using System.Collections.Generic;
using Marabook.History;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C50 — 1.0.5 : les SOUS-CATÉGORIES de fiches (modèle hérité
    /// de l'ensemble, champs propres composés, ordre d'affichage, .plot v37,
    /// orphelines) et l'HYGIÈNE DES LIENS (renommage qui recible les
    /// [[liens]], corbeille vidée qui purge relations et fiches liées).</summary>
    public static class SubCategoryTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C50 — sous-catégories de fiches et hygiène des liens (1.0.5)");
            Hierarchy(t);
            Composition(t);
            RoundTrip(t);
            RenameFollowsLinks(t);
            TrashPurgesRelations(t);
        }

        private static void Hierarchy(Harness t)
        {
            var project = Project.CreateNew();
            var character = project.SheetCategories[0];
            var place = project.SheetCategories[1];
            var heroes = project.AddSubCategory(character, "Héros");
            var extras = project.AddSubCategory(character, "Figurants");
            t.Check(heroes != null && heroes.IsSub && heroes.ParentId == character.Id, "une sous-catégorie naît sous son ensemble");
            t.Equal(character, project.ParentOf(heroes), "ParentOf");
            t.Equal(character, project.TopOf(heroes), "TopOf d'une sous-catégorie = l'ensemble");
            t.Equal(place, project.TopOf(place), "TopOf d'une catégorie = elle-même");
            t.Equal(null, project.AddSubCategory(heroes, "Encore"), "pas de sous-sous-catégorie");
            t.Equal(project.SheetCategories.IndexOf(character) + 1, project.SheetCategories.IndexOf(heroes), "posée juste après son parent");
            t.Equal(project.SheetCategories.IndexOf(heroes) + 1, project.SheetCategories.IndexOf(extras), "la deuxième après la première");
            t.Equal("Héros|Figurants", string.Join("|", project.SubCategoriesOf(character).ConvertAll(delegate(SheetCategory c) { return c.Name; }).ToArray()), "SubCategoriesOf dans l'ordre");
            t.Equal(0, project.SubCategoriesOf(place).Count, "l'autre catégorie n'en a pas");
            t.Equal(project.SheetCategories.Count - 2, project.TopCategories().Count, "TopCategories exclut les sous-catégories");
            var ordered = project.OrderedCategories();
            t.Equal(project.SheetCategories.Count, ordered.Count, "OrderedCategories les a toutes");
            t.Equal(character, ordered[0], "ensemble d'abord");
            t.Equal(heroes, ordered[1], "puis ses sous-catégories");
            t.Equal("Personnage › Héros", project.CategoryPath(heroes), "chemin d'une sous-catégorie");
            t.Equal("Lieu", project.CategoryPath(place), "chemin d'une catégorie = son nom");

            // Le modèle est HÉRITÉ : changer celui de l'ensemble change celui
            // de la sous-catégorie.
            t.Equal(character.TemplateId, project.BaseTemplateIdOf(heroes), "modèle de base hérité");
            character.TemplateId = place.TemplateId;
            t.Equal(place.TemplateId, project.BaseTemplateIdOf(heroes), "le modèle de l'ensemble change → celui de la sous-catégorie aussi");
            character.TemplateId = project.CharacterTemplate().Id;

            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", CategoryId = heroes.Id, TemplateId = project.BaseTemplateIdOf(heroes) };
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();
            t.Equal(heroes, project.SheetCategoryOf(sheet), "la fiche est de sa sous-catégorie");
            t.Equal(character, project.TopSheetCategoryOf(sheet), "… et de l'ensemble");
            t.Check(Modules.ForSheet(project, sheet) != null, "les modules regardent l'ensemble (pas de plantage)");

            // Une sous-catégorie orpheline redevient une catégorie.
            var orphan = new SheetCategory { Name = "Perdue", ParentId = "nulle-part" };
            orphan.ExtraFields.Add(new SheetField { Name = "Trace" });
            project.SheetCategories.Add(orphan);
            t.Equal(null, project.ParentOf(orphan), "parent inconnu = pas de parent");
            t.Check(project.TopCategories().Contains(orphan), "une orpheline compte comme catégorie");
            project.EnsureSheetCategories();
            t.Equal(null, orphan.ParentId, "EnsureSheetCategories la tranche (ParentId effacé)");
            t.Equal(1, orphan.ExtraFields.Count, "… sans perdre ses champs");
        }

        private static void Composition(Harness t)
        {
            var project = Project.CreateNew();
            var character = project.SheetCategories[0];
            var template = project.FindTemplate(character.TemplateId);
            var heroes = project.AddSubCategory(character, "Héros");
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", CategoryId = heroes.Id, TemplateId = character.TemplateId };
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();

            t.Equal(template, project.TemplateOf(sheet), "sans champ propre : le modèle de base tel quel (même objet)");
            var power = new SheetField { Name = "Pouvoir", Kind = "text", Group = "Héroïsme" };
            var oath = new SheetField { Name = "Serment", Kind = "multiline", Group = SheetDefaults.GroupPersonality };
            heroes.ExtraFields.Add(power);
            heroes.ExtraFields.Add(oath);
            var composed = project.TemplateOf(sheet);
            t.Check(composed != template, "avec des champs propres : une composition");
            t.Equal(template.Id, composed.Id, "même id que le modèle de base");
            t.Equal(template.Name, composed.Name, "même nom");
            t.Equal(template.Fields.Count + 2, composed.Fields.Count, "les champs du modèle PUIS les propres");
            t.Equal(power, composed.Fields[composed.Fields.Count - 2], "le champ propre est partagé, pas copié");
            t.Check(composed.HasSection("Héroïsme"), "une section nommée par un champ propre est ajoutée");
            t.Equal(template.Sections.Count + 1, composed.Sections.Count, "une seule section de plus (Personnalité existait)");
            t.Equal(template.Fields.Count, project.FindTemplate(character.TemplateId).Fields.Count, "le modèle de base n'a pas bougé");
            t.Equal(template.Relations, composed.Relations, "les réglages (relations…) suivent le modèle de base");

            // Une valeur rangée par id de champ : visible par la recherche.
            sheet.FieldValues[power.Id] = "Lame de givre";
            var found = false;
            foreach (var field in sheet.SearchFields(project))
                if (field.Kind == SearchField.KindField && field.RefId == power.Id && field.Text == "Lame de givre") found = true;
            t.Check(found, "la recherche voit la valeur d'un champ propre");

            // Une fiche de la catégorie d'ensemble ne voit PAS les champs de
            // la sous-catégorie.
            var other = new BinderItem { Kind = ItemKind.Sheet, Title = "Passant", CategoryId = character.Id, TemplateId = character.TemplateId };
            project.Category(Project.KeySheets).Children.Add(other);
            project.RelinkParents();
            t.Equal(template, project.TemplateOf(other), "une fiche de l'ensemble : le modèle de base seul");

            // Sans modèle de base : un modèle au nom de la sous-catégorie.
            sheet.TemplateId = null;
            var bare = project.TemplateOf(sheet);
            t.Check(bare != null && bare.Fields.Count == 2 && bare.Name == "Héros", "sans modèle : les seuls champs propres, au nom de la sous-catégorie");

            // Le clone d'une catégorie copie ses champs.
            var clone = heroes.Clone();
            t.Equal(2, clone.ExtraFields.Count, "Clone : les champs propres");
            t.Check(clone.ExtraFields[0] != power && clone.ExtraFields[0].Id == power.Id, "… copiés, mêmes ids");
        }

        private static void RoundTrip(Harness t)
        {
            var project = Project.CreateNew();
            var character = project.SheetCategories[0];
            var heroes = project.AddSubCategory(character, "Héros");
            var power = new SheetField { Name = "Pouvoir", Kind = "choice", Group = "Héroïsme" };
            power.Options.Add("feu");
            power.Options.Add("givre");
            heroes.ExtraFields.Add(power);
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", CategoryId = heroes.Id, TemplateId = character.TemplateId };
            sheet.FieldValues[power.Id] = "givre";
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();

            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-tests-c50");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "sous-categories.plot");
            try
            {
                Persistence.PlotFile.Save(project, path);
                var loaded = Persistence.PlotFile.Load(path);
                t.Equal(37, loaded.LoadedFormatVersion, "écrit en v37");
                var back = loaded.FindSheetCategory(heroes.Id);
                t.Check(back != null && back.IsSub && back.ParentId == character.Id, "la sous-catégorie revient sous son parent");
                t.Equal(1, back.ExtraFields.Count, "… avec son champ propre");
                t.Equal("choice|Héroïsme|feu,givre", back.ExtraFields[0].Kind + "|" + back.ExtraFields[0].Group + "|" + string.Join(",", back.ExtraFields[0].Options.ToArray()), "nature, section et options du champ propre");
                t.Equal(power.Id, back.ExtraFields[0].Id, "même id de champ");
                var keira = loaded.FindByTitle("Keira");
                t.Equal("givre", loaded.TemplateOf(keira).Fields[loaded.TemplateOf(keira).Fields.Count - 1].Id == power.Id ? keira.FieldValues[power.Id] : "", "la valeur du champ propre est rangée par id, lue par le modèle composé");
                t.Equal(back, loaded.SheetCategoryOf(keira), "la fiche retrouve sa sous-catégorie");
            }
            finally
            {
                try { System.IO.Directory.Delete(dir, true); } catch { }
            }
        }

        private static TextParagraph Paragraph(string text)
        {
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = text });
            return paragraph;
        }

        private static void RenameFollowsLinks(Harness t)
        {
            var project = Project.CreateNew();
            var sheets = project.Category(Project.KeySheets);
            var writings = project.Category(Project.KeyWritings);
            var keira = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", CategoryId = project.SheetCategories[0].Id, TemplateId = project.SheetCategories[0].TemplateId };
            var other = new BinderItem { Kind = ItemKind.Sheet, Title = "Varenh", CategoryId = project.SheetCategories[0].Id, TemplateId = project.SheetCategories[0].TemplateId };
            var text = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre 1" };
            text.Document.Paragraphs.Clear();
            text.Document.Paragraphs.Add(Paragraph("Voici [[Keira]] et [[ keira |la guerrière]] ; [[Varenh]] aussi, et [[KEIRA|elle]]."));
            text.Document.Paragraphs.Add(Paragraph("Un autre paragraphe sans elle."));
            text.Notes = "Penser à [[Keira]].";
            var template = project.FindTemplate(keira.TemplateId);
            var firstText = template.Fields[0];
            other.FieldValues[firstText.Id] = "Sœur de [[Keira]]";
            sheets.Children.Add(keira);
            sheets.Children.Add(other);
            writings.Children.Add(text);
            project.RelinkParents();

            t.Equal(5, LinkHygiene.CountLinksTo(project, "keira"), "cinq liens visent Keira (casse ignorée)");
            var plan = LinkHygiene.Retarget(project, "Keira", "Keira Varenh");
            t.Equal(5, plan.Occurrences, "le plan recible les cinq");
            t.Equal(2, plan.Items.Count, "dans deux items (l'écrit — ses notes comptent avec lui — et l'autre fiche)");
            t.Equal(0, LinkHygiene.Retarget(project, "Keira", "Keira").Occurrences, "même titre : rien");
            t.Equal(5, LinkHygiene.Retarget(project, "Keira", "KEIRA").Occurrences, "une casse corrigée : les liens suivent la graphie");

            var history = new HistoryManager();
            history.Run(new RenameItemAction(keira, "Keira Varenh", project));
            t.Equal("Keira Varenh", keira.Title, "renommée");
            var flat = PivotEdit.FlatText(text.Document.Paragraphs[0]);
            t.Equal("Voici [[Keira Varenh]] et [[Keira Varenh|la guerrière]] ; [[Varenh]] aussi, et [[Keira Varenh|elle]].", flat,
                "[[Keira]] suit le nom, [[Keira|mots]] garde ses mots, [[Varenh]] ne bouge pas");
            t.Equal("Penser à [[Keira Varenh]].", text.Notes, "les notes suivent");
            t.Equal("Sœur de [[Keira Varenh]]", other.FieldValues[firstText.Id], "le champ de l'autre fiche suit");
            t.Equal(0, LinkHygiene.CountLinksTo(project, "Keira"), "plus rien ne vise l'ancien nom");
            t.Equal(5, LinkHygiene.CountLinksTo(project, "Keira Varenh"), "tout vise le nouveau");
            t.Equal(keira, project.FindByTitle("Keira Varenh"), "la cible se résout");

            history.Undo();
            t.Equal("Keira", keira.Title, "Ctrl+Z : le titre revient");
            t.Equal("Voici [[Keira]] et [[ keira |la guerrière]] ; [[Varenh]] aussi, et [[KEIRA|elle]].", PivotEdit.FlatText(text.Document.Paragraphs[0]), "… et les liens tels qu'ils étaient (espaces et casse compris)");
            t.Equal("Sœur de [[Keira]]", other.FieldValues[firstText.Id], "… le champ aussi");
            history.Redo();
            t.Equal("Voici [[Keira Varenh]] et [[Keira Varenh|la guerrière]] ; [[Varenh]] aussi, et [[Keira Varenh|elle]].", PivotEdit.FlatText(text.Document.Paragraphs[0]), "Ctrl+Y : le même plan rejoué");

            // Un item qui n'est PAS la cible du titre (homonyme : la fiche
            // gagne sur l'écrit) ne recible rien.
            var homonym = new BinderItem { Kind = ItemKind.Text, Title = "Varenh" };
            writings.Children.Add(homonym);
            project.RelinkParents();
            history.Run(new RenameItemAction(homonym, "Varenh (écrit)", project));
            t.Check(PivotEdit.FlatText(text.Document.Paragraphs[0]).Contains("[[Varenh]]"), "renommer l'homonyme qui n'est pas la cible laisse les liens");
            // Sans projet : un renommage nu, comme avant.
            history.Run(new RenameItemAction(other, "Varenh la Rouge"));
            t.Check(PivotEdit.FlatText(text.Document.Paragraphs[0]).Contains("[[Varenh]]"), "sans projet : rien ne suit");
        }

        private static void TrashPurgesRelations(Harness t)
        {
            var project = Project.CreateNew();
            var sheets = project.Category(Project.KeySheets);
            var character = project.SheetCategories[0];
            var template = project.FindTemplate(character.TemplateId);
            var mentorField = new SheetField { Name = "Mentor", Kind = FieldKinds.Sheet };
            template.Fields.Add(mentorField);
            var a = new BinderItem { Kind = ItemKind.Sheet, Title = "A", CategoryId = character.Id, TemplateId = template.Id };
            var b = new BinderItem { Kind = ItemKind.Sheet, Title = "B", CategoryId = character.Id, TemplateId = template.Id };
            var c = new BinderItem { Kind = ItemKind.Sheet, Title = "C", CategoryId = character.Id, TemplateId = template.Id };
            a.Relations.Add(new SheetRelation { Kind = "Mentor", TargetId = b.Id });
            a.Relations.Add(new SheetRelation { Kind = "Rivale", TargetId = c.Id });
            a.Relations.Add(new SheetRelation { Kind = "Ami", Name = "Un libre" });
            a.FieldValues[mentorField.Id] = b.Id;
            c.FieldValues[mentorField.Id] = c.Id;
            sheets.Children.Add(a);
            sheets.Children.Add(b);
            sheets.Children.Add(c);
            project.RelinkParents();

            // B part à la corbeille (relations intactes : restaurable), puis
            // la corbeille est vidée.
            sheets.Children.Remove(b);
            b.Parent = project.Trash;
            project.Trash.Children.Add(b);
            t.Equal(3, a.Relations.Count, "à la corbeille : les relations restent");
            var history = new HistoryManager();
            var empty = new EmptyTrashAction(project.Trash, project);
            history.Run(empty);
            t.Equal(0, project.Trash.Children.Count, "corbeille vidée");
            t.Equal(2, a.Relations.Count, "la relation vers B est partie");
            t.Equal("Rivale|Ami", a.Relations[0].Kind + "|" + a.Relations[1].Kind, "… les autres restent, dans l'ordre");
            t.Equal("", a.FieldValues[mentorField.Id], "le champ « Fiche liée » vers B est vidé");
            t.Equal(c.Id, c.FieldValues[mentorField.Id], "un champ vers une fiche vivante reste");
            t.Equal(2, empty.OrphansRemoved, "deux orphelins retirés");

            history.Undo();
            t.Equal(1, project.Trash.Children.Count, "Ctrl+Z : B revient à la corbeille");
            t.Equal(3, a.Relations.Count, "… et la relation vers B aussi");
            t.Equal("Mentor", a.Relations[0].Kind, "… à sa place");
            t.Equal(b.Id, a.FieldValues[mentorField.Id], "… et le champ");
            history.Redo();
            t.Equal(2, a.Relations.Count, "Ctrl+Y : purgée de nouveau");
        }
    }
}

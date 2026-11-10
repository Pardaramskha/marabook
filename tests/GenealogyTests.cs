using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C15 — la généalogie (batch 36) : natures de relation et
    /// réciproques selon le genre, synchronisation miroir entre fiches,
    /// natures personnalisées du projet, arbre par défaut, migration v16 du
    /// modèle Personnage.</summary>
    public static class GenealogyTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C15 — généalogie");
            Kinds(t);
            Genders(t);
            Reciprocals(t);
            CustomKinds(t);
            Sync(t);
            Tree(t);
            ExtendedTree(t);
            Migration(t);
            Schema(t);
        }

        /// <summary>Le schéma des relations (1.1.0, l'éditeur de relations) : le
        /// livré vaut l'ancienne table, un schéma remanié change ce que les
        /// statiques répondent (Active), les natures libres, la réciproque et
        /// la forme genrée, les cases.</summary>
        private static void Schema(Harness t)
        {
            var defaults = RelationSchema.Defaults();
            t.Equal(30, defaults.Kinds.Count, "le schéma livré porte les trente natures");
            t.Equal(15, defaults.Families().Count, "…en quinze familles");
            t.Equal("Fille", defaults.Reciprocal("Mère", 'f'), "la réciproque de Mère pour une fille : Fille");
            t.Equal("Oncle", defaults.Reciprocal("Nièce", 'm'), "la réciproque de Nièce pour un garçon : Oncle");
            t.Equal("Mentor", defaults.Reciprocal("Mentor", 'f'), "une nature inconnue se répond à elle-même");
            t.Check(defaults.SameAs(RelationSchema.Defaults()) && defaults.Clone().SameAs(defaults), "deux schémas livrés sont identiques, la copie aussi");
            t.Equal(0, defaults.Find("Père").Slot, "Père : case 0 de sa voie et sa génération");
            t.Equal(0, defaults.Find("Mère").Slot, "Mère : la même case que Père (même famille)");
            t.Check(defaults.Find("Amant").Slot == 1 && defaults.Find("Mari").Slot == 3, "les partenaires se suivent : Amoureux 0, Amant 1, Ex 2, Mari 3");

            var custom = defaults.Clone();
            custom.Find("Mère").Generation = -2;
            foreach (var kind in custom.OfFamily("sibling")) kind.Lane = RelationLane.Partner;
            t.Equal("Rivale", custom.AddFree("Rivale"), "une nature libre s'ajoute en voie « autres »");
            t.Equal("Rivale", custom.AddFree(" rivale "), "…sans doublon (forme retenue)");
            t.Check(custom.Find("Rivale").Lane == RelationLane.Other && custom.Find("Rivale").Reciprocal == custom.Find("Rivale").Family, "…réciproque d'elle-même, hors généalogie");
            t.Check(!custom.SameAs(defaults), "un schéma remanié diffère du livré");
            t.Check(custom.FreeNames().Count == 1 && custom.FreeNames()[0] == "Rivale", "les natures libres à écrire pour les anciens Marabook : Rivale");

            RelationKinds.Active = custom;
            try
            {
                t.Equal(-2, RelationKinds.GenerationOf("Mère"), "Active : la génération de Mère suit le schéma");
                t.Equal(RelationLane.Partner, RelationKinds.LaneOf("Sœur"), "Active : la voie de Sœur suit le schéma");
                t.Check(RelationKinds.Find("Rivale") == null && RelationKinds.LaneOf("Rivale") == RelationLane.Other, "une nature libre du schéma n'est pas généalogique");
                t.Equal("Rivale", RelationKinds.Canonical("RIVALE"), "…mais a une forme canonique");
                t.Check(new List<string>(RelationKinds.Defaults).Contains("Rivale"), "…et se propose au sélecteur");
            }
            finally { RelationKinds.Active = null; }
            t.Equal(-1, RelationKinds.GenerationOf("Mère"), "sans Active, le livré répond");

            var project = Project.CreateNew();
            project.AddRelationKind("Mentor");
            t.Check(project.RelationSchema.Find("Mentor") != null && project.RelationKinds.Contains("Mentor"), "une nature créée depuis une fiche entre dans le schéma et dans l'ancienne liste");
            var names = new List<string>(project.AllRelationKinds());
            t.Equal(31, names.Count, "le sélecteur du projet : les trente livrées + Mentor, une fois");
        }

        private static void Kinds(Harness t)
        {
            var defaults = new List<string>(RelationKinds.Defaults);
            t.Equal(30, defaults.Count, "trente natures livrées (la liste des 28 + Neveu/Nièce pour répondre aux oncles)");
            t.Equal("Père", defaults[0], "le sélecteur commence par Père");
            t.Check(defaults.Contains("Adelphe") && defaults.Contains("Doppelgänger"), "Adelphe et Doppelgänger livrés");
            t.Check(RelationKinds.Same("Soeur", "Sœur"), "Soeur = Sœur");
            t.Check(RelationKinds.Same("père", "PÈRE") && RelationKinds.Same("grand pere", "Grand-père"), "sans casse, sans accents, tirets libres");
            t.Equal("Sœur", RelationKinds.Canonical("soeur"), "forme canonique d'une nature livrée");
            t.Equal("Mentor", RelationKinds.Canonical("  Mentor "), "une nature libre est rognée, pas renommée");
            t.Equal(-1, RelationKinds.GenerationOf("Mère"), "une mère : génération -1");
            t.Equal(2, RelationKinds.GenerationOf("Petite-fille"), "une petite-fille : +2");
            t.Equal(-3, RelationKinds.GenerationOf("Aïeul"), "un aïeul : -3");
            t.Equal(0, RelationKinds.GenerationOf("Mentor"), "nature libre : génération 0");
            t.Equal(RelationLane.Partner, RelationKinds.LaneOf("Mari"), "un mari : voie des partenaires");
            t.Equal(RelationLane.Side, RelationKinds.LaneOf("Tante"), "une tante : collatérale");
            t.Equal(RelationLane.Direct, RelationKinds.LaneOf("Fils"), "un fils : ligne directe");
            t.Equal(RelationLane.Other, RelationKinds.LaneOf("Mentor"), "nature libre : voie « autres »");
        }

        private static void Genders(Harness t)
        {
            t.Equal('m', RelationKinds.GenderOfValue("Homme"), "Homme → m");
            t.Equal('m', RelationKinds.GenderOfValue("masculin"), "masculin → m");
            t.Equal('f', RelationKinds.GenderOfValue("Femme"), "Femme → f");
            t.Equal('f', RelationKinds.GenderOfValue("F"), "F → f");
            t.Equal('n', RelationKinds.GenderOfValue("Non-binaire"), "non-binaire → neutre");
            t.Equal('n', RelationKinds.GenderOfValue(""), "vide → neutre");
            var template = SheetDefaults.TemplateFor("Personnage");
            var sheet = new BinderItem { Kind = ItemKind.Sheet, TemplateId = template.Id };
            t.Equal('n', RelationKinds.GenderOf(sheet, template), "genre non renseigné : neutre");
            SheetField birthGender = null, gender = null;
            foreach (var field in template.Fields)
            {
                if (field.Name == SheetDefaults.FieldBirthGender) birthGender = field;
                if (field.Name == SheetDefaults.FieldGender) gender = field;
            }
            t.Check(birthGender != null && gender != null
                && birthGender.Kind == FieldKinds.Choice && gender.Kind == FieldKinds.Choice
                && string.Join("|", gender.Options.ToArray()) == "Masculin|Féminin|Neutre|Autre",
                "le modèle porte deux champs de genre à choix (23/09)");
            sheet.FieldValues[birthGender.Id] = "Féminin";
            t.Equal('f', RelationKinds.GenderOf(sheet, template), "le genre de naissance est lu…");
            sheet.FieldValues[gender.Id] = "Masculin";
            t.Equal('m', RelationKinds.GenderOf(sheet, template), "…mais le genre « si différent » prime : c'est lui qui conjugue");
            var free = new BinderItem { Kind = ItemKind.Sheet };
            free.FreeInfo.Add(new InfoEntry { Title = "Genre", Value = "Homme" });
            t.Equal('m', RelationKinds.GenderOf(free, null), "…ou un champ libre « Genre » (nom d'avant)");
            var legacy = new SheetTemplate();
            legacy.Fields.Add(new SheetField { Name = "Sexe" });
            var legacySheet = new BinderItem { Kind = ItemKind.Sheet };
            legacySheet.FieldValues[legacy.Fields[0].Id] = "Femme";
            t.Equal('f', RelationKinds.GenderOf(legacySheet, legacy), "un modèle d'avant à champ « Sexe » se lit toujours");
        }

        private static void Reciprocals(Harness t)
        {
            t.Equal("Fille", RelationKinds.Reciprocal("Père", 'f'), "mon père me tient pour sa fille");
            t.Equal("Fils", RelationKinds.Reciprocal("Mère", 'm'), "ma mère me tient pour son fils");
            t.Equal("Enfant", RelationKinds.Reciprocal("Parent", 'n'), "genre inconnu : Enfant");
            t.Equal("Père", RelationKinds.Reciprocal("Fils", 'm'), "mon fils me tient pour son père");
            t.Equal("Parent", RelationKinds.Reciprocal("Fille", 'n'), "…ou son parent");
            t.Equal("Petit-fils", RelationKinds.Reciprocal("Grand-mère", 'm'), "grand-mère ↔ petit-fils");
            t.Equal("Grand-parent", RelationKinds.Reciprocal("Petite-fille", 'n'), "petite-fille ↔ grand-parent");
            t.Equal("Descendant", RelationKinds.Reciprocal("Aïeul", 'f'), "aïeul ↔ descendant");
            t.Equal("Aïeul", RelationKinds.Reciprocal("Descendant", 'm'), "descendant ↔ aïeul");
            t.Equal("Femme", RelationKinds.Reciprocal("Mari", 'f'), "mon mari me tient pour sa femme");
            t.Equal("Mari", RelationKinds.Reciprocal("Femme", 'm'), "ma femme me tient pour son mari");
            t.Equal("Partenaire", RelationKinds.Reciprocal("Mari", 'n'), "genre inconnu : Partenaire");
            t.Equal("Amant", RelationKinds.Reciprocal("Amant", 'f'), "amant ↔ amant");
            t.Equal("Ex", RelationKinds.Reciprocal("Ex", 'm'), "ex ↔ ex");
            t.Equal("Doppelgänger", RelationKinds.Reciprocal("Doppelgänger", 'f'), "double ↔ double");
            t.Equal("Nièce", RelationKinds.Reciprocal("Oncle", 'f'), "mon oncle me tient pour sa nièce");
            t.Equal("Neveu", RelationKinds.Reciprocal("Tante", 'n'), "genre inconnu : Neveu (pas de neutre en français)");
            t.Equal("Tante", RelationKinds.Reciprocal("Neveu", 'f'), "mon neveu me tient pour sa tante");
            t.Equal("Cousine", RelationKinds.Reciprocal("Cousin", 'f'), "cousin ↔ cousine");
            t.Equal("Sœur", RelationKinds.Reciprocal("Frère", 'f'), "mon frère me tient pour sa sœur");
            t.Equal("Adelphe", RelationKinds.Reciprocal("Sœur", 'n'), "genre inconnu : Adelphe");
            t.Equal("Mentor", RelationKinds.Reciprocal("Mentor", 'f'), "nature libre : symétrique");
            t.Equal("", RelationKinds.Reciprocal("", 'f'), "nature vide : vide");
        }

        private static void CustomKinds(Harness t)
        {
            var project = Project.CreateNew();
            t.Equal("Mentor", project.AddRelationKind(" Mentor "), "la nature personnalisée est rognée");
            t.Equal("Mentor", project.AddRelationKind("mentor"), "…et ne se duplique pas");
            t.Equal(1, project.RelationKinds.Count, "une seule nature personnalisée");
            t.Equal("Sœur", project.AddRelationKind("soeur"), "une nature livrée n'est pas ajoutée…");
            t.Equal(1, project.RelationKinds.Count, "…elle est rendue sous sa forme canonique");
            t.Equal("", project.AddRelationKind("  "), "vide : rien");
            var all = new List<string>(project.AllRelationKinds());
            t.Equal("Mentor", all[all.Count - 1], "les personnalisées suivent les livrées");
        }

        private static Project Cast(out BinderItem kaladin, out BinderItem lirin, out BinderItem tien)
        {
            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            var sheets = project.Category(Project.KeySheets);
            kaladin = Sheet(template, "Kaladin", "Homme");
            lirin = Sheet(template, "Lirin", "Homme");
            tien = Sheet(template, "Tien", "");
            sheets.Children.Add(kaladin);
            sheets.Children.Add(lirin);
            sheets.Children.Add(tien);
            project.RelinkParents();
            return project;
        }

        private static BinderItem Sheet(SheetTemplate template, string title, string gender)
        {
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = title, TemplateId = template.Id };
            foreach (var field in template.Fields)
                if (field.Name == SheetDefaults.FieldGender) sheet.FieldValues[field.Id] = gender;
            return sheet;
        }

        private static void Sync(Harness t)
        {
            BinderItem kaladin, lirin, tien;
            var project = Cast(out kaladin, out lirin, out tien);

            // — Poser : Kaladin déclare Lirin comme Père → Lirin reçoit Fils.
            var relation = new SheetRelation { Kind = "Père", TargetId = lirin.Id };
            kaladin.Relations.Add(relation);
            var mirror = RelationSync.Mirror(project, kaladin, relation, null);
            t.Check(mirror != null && lirin.Relations.Count == 1, "la fiche liée reçoit le reflet");
            t.Equal("Fils", mirror.Kind, "…de nature réciproque selon le genre de la source");
            t.Equal(kaladin.Id, mirror.TargetId, "…pointant vers la source");

            // — Changer la nature : le reflet suit, sans doublon.
            var previous = relation.Kind;
            relation.Kind = "Frère";
            RelationSync.Mirror(project, kaladin, relation, previous);
            t.Equal(1, lirin.Relations.Count, "changer la nature ne duplique pas le reflet");
            t.Equal("Frère", lirin.Relations[0].Kind, "…il suit (Frère ↔ Frère pour un homme)");

            // — Reflet renommé à la main : il reste le seul candidat, il suit.
            lirin.Relations[0].Kind = "Demi-frère";
            relation.Kind = "Oncle";
            RelationSync.Mirror(project, kaladin, relation, "Frère");
            t.Equal(1, lirin.Relations.Count, "un reflet renommé à la main est retrouvé (seul candidat)");
            t.Equal("Neveu", lirin.Relations[0].Kind, "…et réaligné");

            // — Cible neutre : Tien sans genre déclare Kaladin comme Frère → Kaladin reçoit Adelphe.
            var tienRelation = new SheetRelation { Kind = "Frère", TargetId = kaladin.Id };
            tien.Relations.Add(tienRelation);
            RelationSync.Mirror(project, tien, tienRelation, null);
            t.Check(kaladin.Relations.Count == 2 && kaladin.Relations[1].Kind == "Adelphe", "genre inconnu : la forme neutre");

            // — Retirer : le reflet part avec la relation.
            t.Check(RelationSync.Unmirror(project, kaladin, relation.TargetId, relation.Kind), "le reflet est retiré");
            t.Equal(0, lirin.Relations.Count, "…la fiche liée n'en garde rien");
            t.Check(!RelationSync.Unmirror(project, kaladin, relation.TargetId, relation.Kind), "retirer deux fois : rien");

            // — Cibles sans reflet : nom libre, fiche absente, soi-même.
            var free = new SheetRelation { Kind = "Rivale", Name = "La Pie" };
            t.Check(RelationSync.Mirror(project, kaladin, free, null) == null, "un nom libre n'a pas de reflet");
            var ghost = new SheetRelation { Kind = "Père", TargetId = "nulle-part" };
            t.Check(RelationSync.Mirror(project, kaladin, ghost, null) == null, "une fiche absente non plus");
            var self = new SheetRelation { Kind = "Doppelgänger", TargetId = kaladin.Id };
            t.Check(RelationSync.Mirror(project, kaladin, self, null) == null && kaladin.Relations.Count == 2, "soi-même : rien");

            // — Deux relations vers la même fiche (Amant puis Ex) : chacune son reflet.
            var amant = new SheetRelation { Kind = "Amant", TargetId = tien.Id };
            var ex = new SheetRelation { Kind = "Ex", TargetId = tien.Id };
            kaladin.Relations.Add(amant);
            kaladin.Relations.Add(ex);
            RelationSync.Mirror(project, kaladin, amant, null);
            RelationSync.Mirror(project, kaladin, ex, null);
            var kinds = new List<string>();
            foreach (var r in tien.Relations) if (r.TargetId == kaladin.Id) kinds.Add(r.Kind);
            t.Check(kinds.Count == 3 && kinds.Contains("Frère") && kinds.Contains("Amant") && kinds.Contains("Ex"),
                "deux relations NEUVES vers la même fiche : deux reflets, et la relation propre de Tien intacte");
        }

        private static void Tree(Harness t)
        {
            BinderItem kaladin, lirin, tien;
            var project = Cast(out kaladin, out lirin, out tien);
            kaladin.Relations.Add(new SheetRelation { Kind = "Père", TargetId = lirin.Id });
            kaladin.Relations.Add(new SheetRelation { Kind = "Frère", TargetId = tien.Id });
            kaladin.Relations.Add(new SheetRelation { Kind = "mère", Name = "Hesina" });
            kaladin.Relations.Add(new SheetRelation { Kind = "Mentor", Name = "Tukks" });
            kaladin.Relations.Add(new SheetRelation { Kind = "Fille", Name = "" }); // sans nom : ignorée
            var nodes = Genealogy.Build(project, kaladin);
            t.Equal(5, nodes.Count, "le personnage + quatre relations nommées");
            t.Check(nodes[0].IsSelf && nodes[0].Label == "Kaladin", "le personnage d'abord");
            t.Check(nodes[1].Label == "Hesina" && nodes[1].Generation == -1 && nodes[1].Kind == "Mère", "la mère (nature canonisée) en génération -1");
            t.Check(nodes[2].Label == "Lirin" && nodes[2].TargetId == lirin.Id, "le père, lié à sa fiche");
            t.Check(nodes[3].Label == "Tien" && nodes[3].Generation == 0 && nodes[3].Lane == RelationLane.Side, "le frère, collatéral en génération 0");
            t.Check(nodes[4].Label == "Tukks" && nodes[4].Lane == RelationLane.Other, "le mentor dans la voie « autres »");
            var generations = Genealogy.Generations(nodes);
            t.Check(generations.Count == 2 && generations[0] == -1 && generations[1] == 0, "générations présentes : -1 et 0 (les « autres » ne comptent pas)");
            t.Equal(1, Genealogy.Generations(Genealogy.Build(project, tien)).Count, "sans relation : la seule génération 0");
            t.Equal(0, Genealogy.Build(project, null).Count, "fiche nulle : arbre vide");
        }

        /// <summary>Les familles élargies (1.1.1, Rémi) : ce qu'une fiche liée
        /// déclare entre dans l'arbre — le père déclaré sur la fiche du
        /// grand-père est l'arrière-grand-père ; le demi-frère amène son autre
        /// parent ; et chaque fratrie pend du milieu de SES deux parents.</summary>
        private static void ExtendedTree(Harness t)
        {
            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            var sheets = project.Category(Project.KeySheets);
            var theo = Sheet(template, "Théo", "Homme");
            var suzanne = Sheet(template, "Suzanne", "Femme");
            var mathieu = Sheet(template, "Mathieu", "Homme");
            var abel = Sheet(template, "Abel", "Homme");
            var albert = Sheet(template, "Albert", "Homme");
            var gerard = Sheet(template, "Gérard", "Homme");
            var bertrand = Sheet(template, "Bertrand", "Homme");
            var odile = Sheet(template, "Odile", "Femme");
            foreach (var sheet in new[] { theo, suzanne, mathieu, abel, albert, gerard, bertrand, odile }) sheets.Children.Add(sheet);
            project.RelinkParents();
            suzanne.ImageId = "img-suzanne";
            // Théo : mère Suzanne, père Mathieu, frère Abel, grand-père Gérard.
            theo.Relations.Add(new SheetRelation { Kind = "Mère", TargetId = suzanne.Id });
            theo.Relations.Add(new SheetRelation { Kind = "Père", TargetId = mathieu.Id });
            theo.Relations.Add(new SheetRelation { Kind = "Frère", TargetId = abel.Id });
            theo.Relations.Add(new SheetRelation { Kind = "Grand-père", TargetId = gerard.Id });
            // Abel : mère Suzanne, père Albert (le demi-frère amène Albert).
            abel.Relations.Add(new SheetRelation { Kind = "Mère", TargetId = suzanne.Id });
            abel.Relations.Add(new SheetRelation { Kind = "Père", TargetId = albert.Id });
            // Gérard : père Bertrand (l'arrière-grand-père de Théo), sœur Odile (grand-tante).
            gerard.Relations.Add(new SheetRelation { Kind = "Père", TargetId = bertrand.Id });
            gerard.Relations.Add(new SheetRelation { Kind = "Sœur", TargetId = odile.Id });
            // Mathieu : fils Théo (le miroir), père Gérard.
            mathieu.Relations.Add(new SheetRelation { Kind = "Fils", TargetId = theo.Id });
            mathieu.Relations.Add(new SheetRelation { Kind = "Père", TargetId = gerard.Id });

            var nodes = Genealogy.Build(project, theo);
            var byLabel = new Dictionary<string, GenealogyNode>();
            foreach (var node in nodes) byLabel[node.Label] = node;
            t.Equal(8, nodes.Count, "les huit personnes de la famille élargie (" + nodes.Count + ")");
            t.Check(byLabel.ContainsKey("Bertrand") && byLabel["Bertrand"].Generation == -3 && byLabel["Bertrand"].Inferred, "Bertrand, père du grand-père, en génération -3 (par rebond)");
            t.Equal("Arrière-grand-père", byLabel["Bertrand"].Kind, "…nommé arrière-grand-père");
            t.Check(byLabel["Bertrand"].Lane == RelationLane.Direct && byLabel["Bertrand"].Lineal, "…en ligne directe");
            t.Check(byLabel.ContainsKey("Odile") && byLabel["Odile"].Generation == -2 && byLabel["Odile"].Kind == "Grand-tante" && byLabel["Odile"].Lane == RelationLane.Side, "Odile, sœur du grand-père : grand-tante, collatérale en -2 (" + (byLabel.ContainsKey("Odile") ? byLabel["Odile"].Kind : "absente") + ")");
            t.Check(byLabel.ContainsKey("Albert") && byLabel["Albert"].Generation == -1 && byLabel["Albert"].Kind == "Père d'Abel" && byLabel["Albert"].Lane == RelationLane.Side, "Albert, père du demi-frère : « Père d'Abel », collatéral en -1 (" + (byLabel.ContainsKey("Albert") ? byLabel["Albert"].Kind : "absent") + ")");
            t.Check(byLabel["Gérard"].Kind == "Grand-père" && !byLabel["Gérard"].Inferred, "Gérard garde sa nature déclarée");
            t.Check(byLabel["Suzanne"].ImageId == "img-suzanne" && byLabel["Suzanne"].Gender == 'f', "la photo et le genre de la fiche suivent");
            t.Check(byLabel["Théo"].ParentKeys.Contains(suzanne.Id) && byLabel["Théo"].ParentKeys.Contains(mathieu.Id), "Théo connaît ses deux parents");
            t.Check(byLabel["Abel"].ParentKeys.Contains(suzanne.Id) && byLabel["Abel"].ParentKeys.Contains(albert.Id) && !byLabel["Abel"].ParentKeys.Contains(mathieu.Id), "Abel connaît les siens (Suzanne et Albert, pas Mathieu)");
            t.Check(byLabel["Gérard"].ParentKeys.Contains(bertrand.Id) && byLabel["Mathieu"].ParentKeys.Contains(gerard.Id), "les parents se lisent dans les deux sens (déclaré « Père » ou reçu « Fils »)");

            var rows = Genealogy.Rows(nodes);
            t.Equal(4, rows.Count, "quatre rangées : -3, -2, -1, 0");
            var parentsRow = rows[2];
            t.Equal(3, parentsRow.Nodes.Count, "la rangée des parents : Mathieu, Suzanne, Albert");
            t.Equal("Suzanne", parentsRow.Nodes.Count == 3 ? parentsRow.Nodes[1].Label : "?", "…Suzanne AU MILIEU, entre ses deux partenaires (" + Labels(parentsRow.Nodes) + ")");
            var couples = Genealogy.Couples(nodes, rows);
            GenealogyCouple theoCouple = null, abelCouple = null;
            foreach (var couple in couples)
            {
                if (couple.Children.Count == 1 && couple.Children[0].Label == "Théo") theoCouple = couple;
                if (couple.Children.Count == 1 && couple.Children[0].Label == "Abel") abelCouple = couple;
            }
            t.Check(theoCouple != null && theoCouple.Parents.Count == 2 && Has(theoCouple.Parents, "Suzanne") && Has(theoCouple.Parents, "Mathieu"), "Théo pend du couple Suzanne–Mathieu");
            t.Check(abelCouple != null && abelCouple.Parents.Count == 2 && Has(abelCouple.Parents, "Suzanne") && Has(abelCouple.Parents, "Albert"), "Abel pend du couple Suzanne–Albert, pas du parent commun");
            GenealogyCouple gerardCouple = null;
            foreach (var couple in couples) if (couple.Parents.Count == 1 && couple.Parents[0].Label == "Bertrand") gerardCouple = couple;
            t.Check(gerardCouple != null && gerardCouple.Children.Count == 2 && Has(gerardCouple.Children, "Gérard") && Has(gerardCouple.Children, "Odile"), "Gérard et Odile pendent de Bertrand (la fratrie partage ses parents)");
            t.Check(!Has(parentsRow.Nodes, "Odile") && rows[1].Nodes.Count == 2, "la rangée des grands-parents : Gérard et Odile (" + Labels(rows[1].Nodes) + ")");

            // Les présomptions d'avant restent : un enfant du personnage sans
            // autre parent connu pend du personnage et de sa partenaire.
            var syl = Sheet(template, "Syl", "Femme");
            sheets.Children.Add(syl);
            project.RelinkParents();
            theo.Relations.Add(new SheetRelation { Kind = "Partenaire", TargetId = syl.Id });
            theo.Relations.Add(new SheetRelation { Kind = "Fille", Name = "Rock" });
            nodes = Genealogy.Build(project, theo);
            rows = Genealogy.Rows(nodes);
            couples = Genealogy.Couples(nodes, rows);
            GenealogyCouple rockCouple = null;
            foreach (var couple in couples) if (couple.Children.Count == 1 && couple.Children[0].Label == "Rock") rockCouple = couple;
            t.Check(rockCouple != null && rockCouple.WithSelf && Has(rockCouple.Parents, "Syl"), "Rock (nom libre) pend de Théo et Syl, trait double");
            var selfRow = rows[3];
            var theoAt = IndexOf(selfRow.Nodes, "Théo");
            var sylAt = IndexOf(selfRow.Nodes, "Syl");
            t.Check(theoAt >= 0 && sylAt >= 0 && Math.Abs(theoAt - sylAt) == 1, "Syl est collée à Théo dans la rangée (" + Labels(selfRow.Nodes) + ")");
            t.Equal("d'Abel", Genealogy.Of("Abel"), "élision devant une voyelle");
            t.Equal("de Marc", Genealogy.Of("Marc"), "…pas devant une consonne");
            t.Equal("Arrière-grand-mère", Genealogy.LinealWord(-3, 'f'), "le mot de la génération -3 au féminin");
            t.Equal("Aïeule", Genealogy.LinealWord(-4, 'f'), "…et au-delà");
            t.Equal("Arrière-petit-enfant", Genealogy.LinealWord(3, 'n'), "la génération +3, genre inconnu");

            // Le genre d'un nom d'après les fiches (1.1.1, pour « Ajouter au dictionnaire »).
            t.Equal('f', RelationKinds.GenderOfName(project, "Suzanne"), "le genre de la fiche Suzanne");
            t.Equal('m', RelationKinds.GenderOfName(project, "théo"), "…sans casse");
            t.Equal('n', RelationKinds.GenderOfName(project, "Inconnu"), "…'n' sans fiche");
            t.Equal('n', RelationKinds.GenderOfName(null, "Suzanne"), "…'n' sans projet");
            var verlaine = Sheet(template, "Odile Verlaine", "Femme");
            sheets.Children.Add(verlaine);
            project.RelinkParents();
            t.Equal('f', RelationKinds.GenderOfName(project, "Verlaine"), "…un mot du titre composé");
            t.Equal('f', RelationKinds.GenderOfName(project, "Odile"), "…le prénom en tête du titre (la fiche Odile seule dit la même chose)");
            sheets.Children.Add(Sheet(template, "Marc Verlaine", "Homme"));
            project.RelinkParents();
            t.Equal('n', RelationKinds.GenderOfName(project, "Verlaine"), "…deux Verlaine de genres différents : rien");
            t.Equal('m', RelationKinds.GenderOfName(project, "Marc"), "…mais Marc reste un homme");
        }

        private static bool Has(List<GenealogyNode> nodes, string label)
        {
            foreach (var node in nodes) if (node.Label == label) return true;
            return false;
        }

        private static int IndexOf(List<GenealogyNode> nodes, string label)
        {
            for (var i = 0; i < nodes.Count; i++) if (nodes[i].Label == label) return i;
            return -1;
        }

        private static string Labels(List<GenealogyNode> nodes)
        {
            var labels = new List<string>();
            foreach (var node in nodes) labels.Add(node.Label);
            return string.Join(", ", labels.ToArray());
        }

        /// <summary>Le modèle Personnage tel que livré par le batch 31.</summary>
        private static SheetTemplate OldCharacter()
        {
            var t = new SheetTemplate { Name = "Personnage" };
            string[] infos = { "Nom", "Prénom", "Alias", "Genre", "Date de naissance", "Lieu de naissance", "Affiliation", "Religion", "Magie" };
            foreach (var name in infos) t.Fields.Add(new SheetField { Name = name, Group = "Infos" });
            string[] looks = { "Taille", "Poids", "Yeux", "Cheveux", "Couleur de peau", "Sexe de naissance", "Particularités" };
            foreach (var name in looks) t.Fields.Add(new SheetField { Name = name, Group = "Physique", Kind = name == "Particularités" ? "multiline" : "text" });
            return t;
        }

        private static List<string> Names(SheetTemplate template, string group)
        {
            var names = new List<string>();
            foreach (var field in template.Fields)
                if (group == null || field.Group == group) names.Add(field.Name);
            return names;
        }

        /// <summary>Le modèle Personnage tel que livré du b36 au 22/09.</summary>
        private static SheetTemplate RecentCharacter()
        {
            var t = new SheetTemplate { Name = "Personnage", Relations = true };
            t.Sections.Add(SheetDefaults.GroupLooks);
            string[] infos = { "Nom", "Prénom", "Alias", "Genre", "Date de naissance", "Âge", "Lieu de naissance", "Affiliation", "Religion", "Magie" };
            foreach (var name in infos) t.Fields.Add(new SheetField { Name = name });
            string[] looks = { "Taille", "Poids", "Peau", "Yeux", "Traits", "Particularités" };
            foreach (var name in looks) t.Fields.Add(new SheetField { Name = name, Group = SheetDefaults.GroupLooks, Kind = name == "Particularités" ? "multiline" : "text" });
            return t;
        }

        private static SheetField Field(SheetTemplate template, string name)
        {
            foreach (var field in template.Fields) if (field.Name == name) return field;
            return null;
        }

        private static void Migration(Harness t)
        {
            // — Le modèle neuf (liste du 23/09).
            var fresh = SheetDefaults.TemplateFor("Personnage");
            t.Equal(string.Join("|", SheetDefaults.CharacterInfos), string.Join("|", Names(fresh, SheetDefaults.GroupInfos).ToArray()), "infos par défaut : Nom … Affiliation, dans l'ordre de la liste");
            t.Equal(string.Join("|", SheetDefaults.CharacterLooks), string.Join("|", Names(fresh, SheetDefaults.GroupLooks).ToArray()), "apparence par défaut : Taille … Particularités");
            t.Equal(string.Join("|", SheetDefaults.CharacterPersonality), string.Join("|", Names(fresh, SheetDefaults.GroupPersonality).ToArray()), "personnalité par défaut : En un mot, Voix, Gestuelle, Sociabilité");
            t.Equal(string.Join("|", SheetDefaults.CharacterNarration), string.Join("|", Names(fresh, SheetDefaults.GroupNarration).ToArray()), "narration par défaut (1.0.4) : Rôle, Objectif, Défaut central, Expression typique, Niveau de langue");
            foreach (var name in SheetDefaults.CharacterNarration) t.Equal(FieldKinds.Text, FieldKinds.Normalize(Field(fresh, name).Kind), "« " + name + " » en texte court");
            t.Check(fresh.Sections.Count == 3 && fresh.Sections[0] == SheetDefaults.GroupLooks && fresh.Sections[1] == SheetDefaults.GroupPersonality && fresh.Sections[2] == SheetDefaults.GroupNarration && fresh.Relations,
                "sections Apparence, Personnalité puis Narration, paper Relations");
            t.Equal("multiline", Field(fresh, "Particularités").Kind, "Particularités reste multiligne");
            t.Check(!SheetDefaults.UpgradeCharacterTemplate(fresh), "le modèle neuf n'a rien à migrer");
            foreach (var name in new[] { "Lieu", "Événement", "Système", "Peuple", "Bestiaire", "Pays / Gouvernement", "Faction / Organisation" })
            {
                var simple = SheetDefaults.TemplateFor(name);
                t.Check(simple != null && simple.Fields.Count > 0 && simple.Sections.Count == 0 && !simple.Relations && Field(simple, "Description") == null,
                    "« " + name + " » : des champs dans Infos seulement, sans Description ni Relations");
            }
            t.Check(Field(SheetDefaults.TemplateFor("Bestiaire"), "Domestique").Kind == FieldKinds.Choice
                && Field(SheetDefaults.TemplateFor("Lieu"), "Échelle").Options.Count == 7
                && Field(SheetDefaults.TemplateFor("Événement"), "Participants").Kind == FieldKinds.List,
                "les natures de la liste : choix (Échelle, Type, Domestique), liste (Participants)");

            // — Le modèle du 22/09 : renommages à id constant, genre en choix, ordre, Personnalité.
            var recent = RecentCharacter();
            var skin = Field(recent, "Peau"); var genre = Field(recent, "Genre"); var faith = Field(recent, "Religion");
            var skinId = skin.Id;
            t.Check(SheetDefaults.UpgradeCharacterTemplate(recent), "un modèle d'avant est migré");
            t.Equal(string.Join("|", SheetDefaults.CharacterInfos), string.Join("|", Names(recent, SheetDefaults.GroupInfos).ToArray()), "les infos prennent la liste et l'ordre du 23/09");
            t.Equal(string.Join("|", SheetDefaults.CharacterLooks), string.Join("|", Names(recent, SheetDefaults.GroupLooks).ToArray()), "l'apparence aussi (Couleur des cheveux ajoutée)");
            t.Equal(string.Join("|", SheetDefaults.CharacterPersonality), string.Join("|", Names(recent, SheetDefaults.GroupPersonality).ToArray()), "la Personnalité est créée");
            t.Check(skin.Id == skinId && skin.Name == "Teinte de peau", "« Peau » → « Teinte de peau », même id (les valeurs suivent)");
            t.Check(faith.Name == "Croyance" && Field(recent, "Capacités/Magie") != null, "Religion → Croyance, Magie → Capacités/Magie");
            t.Check(genre.Name == SheetDefaults.FieldBirthGender && genre.Kind == FieldKinds.Choice && genre.Options.Count == 4,
                "« Genre » (texte) devient « Genre de naissance », un choix à quatre options");
            t.Check(Field(recent, SheetDefaults.FieldGender) != genre && Field(recent, SheetDefaults.FieldGender).Kind == FieldKinds.Choice,
                "« Genre (si différent) » est créé à côté");
            t.Check(recent.HasSection(SheetDefaults.GroupPersonality) && recent.HasSection(SheetDefaults.GroupNarration) && recent.Sections.Count == 3, "les sections Personnalité et Narration sont ajoutées");
            t.Equal(string.Join("|", SheetDefaults.CharacterNarration), string.Join("|", Names(recent, SheetDefaults.GroupNarration).ToArray()), "la Narration est créée (1.0.4, v35)");
            t.Check(!SheetDefaults.UpgradeCharacterTemplate(recent), "idempotente");

            // — Le modèle du batch 31 (groupes Infos/Physique, Sexe de naissance ET Genre) :
            // Sexe de naissance → Genre de naissance, Genre → Genre (si différent), Cheveux → Couleur des cheveux.
            var old = OldCharacter();
            var sex = Field(old, "Sexe de naissance"); var oldGenre = Field(old, "Genre"); var hair = Field(old, "Cheveux");
            t.Check(SheetDefaults.UpgradeCharacterTemplate(old), "un modèle du b31 est migré");
            t.Check(sex.Name == SheetDefaults.FieldBirthGender && sex.Group == "" && oldGenre.Name == SheetDefaults.FieldGender,
                "Sexe de naissance → Genre de naissance (passé en Infos), Genre → Genre (si différent)");
            t.Check(hair.Name == "Couleur des cheveux" && hair.Group == SheetDefaults.GroupLooks, "Cheveux → Couleur des cheveux, même id");
            t.Equal(string.Join("|", SheetDefaults.CharacterInfos), string.Join("|", Names(old, SheetDefaults.GroupInfos).ToArray()), "infos alignées (Âge inséré, groupe « Infos » → section par défaut)");
            t.Equal(string.Join("|", SheetDefaults.CharacterLooks), string.Join("|", Names(old, SheetDefaults.GroupLooks).ToArray()), "apparence alignée (« Physique » → « Apparence »)");
            t.Check(old.Relations && old.HasSection(SheetDefaults.GroupLooks) && old.HasSection(SheetDefaults.GroupPersonality) && old.HasSection(SheetDefaults.GroupNarration), "sections (Narration comprise) et Relations posées");
            t.Check(!SheetDefaults.UpgradeCharacterTemplate(old), "idempotente aussi");

            // — Un champ maison n'est jamais retiré : il suit les champs par défaut.
            var custom = RecentCharacter();
            custom.Fields.Insert(2, new SheetField { Name = "Devise" });
            custom.Fields.Add(new SheetField { Name = "Cicatrices", Group = SheetDefaults.GroupLooks });
            SheetDefaults.UpgradeCharacterTemplate(custom);
            var infos = Names(custom, SheetDefaults.GroupInfos);
            var looks = Names(custom, SheetDefaults.GroupLooks);
            t.Check(infos[infos.Count - 1] == "Devise" && infos.Count == SheetDefaults.CharacterInfos.Length + 1, "un champ maison d'Infos passe en queue d'Infos");
            t.Check(looks[looks.Count - 1] == "Cicatrices" && looks.Count == SheetDefaults.CharacterLooks.Length + 1, "un champ maison d'Apparence, en queue d'Apparence");

            // — Par le projet : la catégorie Personnage, et rien sans elle.
            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            t.Check(template != null && template.Name == "Personnage", "le modèle de base de la catégorie Personnage");
            t.Check(!project.UpgradeCharacterTemplate(), "un projet neuf n'a rien à migrer");
            project.SheetCategories.Clear();
            project.Templates.Clear();
            t.Check(project.CharacterTemplate() == null && !project.UpgradeCharacterTemplate(), "sans modèle Personnage : rien");
        }
    }
}

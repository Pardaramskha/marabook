using System.Collections.Generic;
using UniversSale.Model;

namespace UniversSale.Tests
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
            Migration(t);
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
            foreach (var field in template.Fields)
                if (field.Name == "Genre") sheet.FieldValues[field.Id] = "Femme";
            t.Equal('f', RelationKinds.GenderOf(sheet, template), "le champ Genre du modèle est lu");
            var free = new BinderItem { Kind = ItemKind.Sheet };
            free.FreeInfo.Add(new InfoEntry { Title = "Genre", Value = "Homme" });
            t.Equal('m', RelationKinds.GenderOf(free, null), "…ou un champ libre « Genre »");
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
                if (field.Name == "Genre") sheet.FieldValues[field.Id] = gender;
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

        private static void Migration(Harness t)
        {
            // — Le modèle neuf.
            var fresh = SheetDefaults.TemplateFor("Personnage");
            var infos = Names(fresh, SheetDefaults.GroupInfos);
            t.Equal(SheetDefaults.FieldAge, infos[infos.IndexOf(SheetDefaults.FieldBirthDate) + 1], "Âge juste sous la date de naissance");
            t.Equal(string.Join("|", SheetDefaults.CharacterLooks), string.Join("|", Names(fresh, SheetDefaults.GroupLooks).ToArray()), "apparence par défaut : Taille, Poids, Peau, Yeux, Traits, Particularités");
            t.Check(!SheetDefaults.UpgradeCharacterTemplate(fresh, new List<BinderItem>()), "le modèle neuf n'a rien à migrer");

            // — Un modèle d'avant, sans valeur dans Cheveux : renommage, insertion, retrait.
            var old = OldCharacter();
            SheetField skin = null;
            foreach (var field in old.Fields) if (field.Name == "Couleur de peau") skin = field;
            var skinId = skin.Id;
            t.Check(SheetDefaults.UpgradeCharacterTemplate(old, new List<BinderItem>()), "un modèle d'avant est migré");
            infos = Names(old, "Infos");
            t.Equal(SheetDefaults.FieldAge, infos[infos.IndexOf("Date de naissance") + 1], "Âge inséré sous la date de naissance");
            t.Equal(string.Join("|", SheetDefaults.CharacterLooks), string.Join("|", Names(old, "Physique").ToArray()), "l'apparence est alignée (Cheveux et Sexe de naissance vides retirés)");
            t.Equal(skinId, skin.Id, "« Couleur de peau » → « Peau » garde son id (les valeurs suivent)");
            t.Equal("Peau", skin.Name, "…renommée");
            t.Check(!SheetDefaults.UpgradeCharacterTemplate(old, new List<BinderItem>()), "idempotente");

            // — Un modèle d'avant dont une fiche a rempli Cheveux : le champ reste, en queue.
            var kept = OldCharacter();
            SheetField hair = null;
            foreach (var field in kept.Fields) if (field.Name == "Cheveux") hair = field;
            var sheet = new BinderItem { Kind = ItemKind.Sheet, TemplateId = kept.Id };
            sheet.FieldValues[hair.Id] = "roux";
            SheetDefaults.UpgradeCharacterTemplate(kept, new List<BinderItem> { sheet });
            var looks = Names(kept, "Physique");
            t.Equal("Cheveux", looks[looks.Count - 1], "un champ rempli n'est jamais retiré (en queue d'apparence)");
            t.Equal(7, looks.Count, "…les six par défaut, puis lui");

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

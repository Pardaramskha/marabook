using System;
using System.Collections.Generic;
using System.IO;
using Marabook.History;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C53 — les correctifs de la 1.1.0 (26/10/2026) : « Date de
    /// décès » sous le lieu de naissance (et sa migration), les tags des
    /// fiches (normalisation, inventaire, annulation), l'objectif et la
    /// limite d'un écrit (TextGoal), l'icône choisie d'une catégorie — et
    /// l'aller-retour .plot v41 de tout cela.</summary>
    public static class Patch110Tests
    {
        public static void Run(Harness t)
        {
            t.Suite("C53 — 1.1.0 : date de décès, tags, objectif et limite, icône de catégorie");
            DeathDate(t);
            Tags(t);
            Goals(t);
            RoundTrip(t);
            UndoTags(t);
        }

        private static List<string> Names(SheetTemplate template)
        {
            var names = new List<string>();
            foreach (var field in template.Fields) names.Add(field.Name);
            return names;
        }

        private static void DeathDate(Harness t)
        {
            var fresh = SheetDefaults.TemplateFor("Personnage");
            var names = Names(fresh);
            t.Check(names.IndexOf(SheetDefaults.FieldDeathDate) == names.IndexOf("Lieu de naissance") + 1,
                "modèle neuf : « Date de décès » juste sous « Lieu de naissance »");
            var death = fresh.Fields[names.IndexOf(SheetDefaults.FieldDeathDate)];
            t.Equal(SheetDefaults.GroupInfos, death.Group, "…dans la section Infos");
            t.Equal(FieldKinds.Text, death.Kind, "…en texte court");
            t.Check(Array.IndexOf(SheetDefaults.CharacterInfos, SheetDefaults.FieldDeathDate) == Array.IndexOf(SheetDefaults.CharacterInfos, "Lieu de naissance") + 1,
                "la liste des champs d'infos la place au même endroit");

            // Un modèle d'avant la 1.1.0 : le champ manque, la migration l'ajoute à sa place, une fois.
            var old = SheetDefaults.TemplateFor("Personnage");
            var custom = new SheetField { Name = "Devise (maison)" };
            old.Fields.Add(custom);
            old.Fields.RemoveAt(Names(old).IndexOf(SheetDefaults.FieldDeathDate));
            var age = old.Fields[Names(old).IndexOf(SheetDefaults.FieldAge)];
            t.Check(SheetDefaults.UpgradeCharacterTemplate(old), "migration : le modèle d'avant change");
            names = Names(old);
            t.Check(names.IndexOf(SheetDefaults.FieldDeathDate) == names.IndexOf("Lieu de naissance") + 1, "…« Date de décès » ajoutée sous le lieu de naissance");
            t.Check(ReferenceEquals(old.Fields[names.IndexOf(SheetDefaults.FieldAge)], age), "…les champs existants gardent leur objet (et leur id)");
            t.Check(names.Contains("Devise (maison)"), "…le champ maison reste");
            t.Check(!SheetDefaults.UpgradeCharacterTemplate(old), "…et la migration est idempotente");
        }

        private static void Tags(Harness t)
        {
            t.Equal("mage noir", SheetTags.Normalize("  mage \t  noir "), "un tag se rogne et ses blancs se réduisent");
            t.Equal("", SheetTags.Normalize(null), "null = rien");
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira" };
            t.Check(SheetTags.Add(sheet, "Héros"), "ajout d'un tag");
            t.Check(!SheetTags.Add(sheet, "héros"), "le même tag, casse différente : refusé");
            t.Check(!SheetTags.Add(sheet, "   "), "un tag vide : refusé");
            t.Check(SheetTags.Add(sheet, "mage noir") && sheet.Tags.Count == 2, "un second tag");
            t.Check(SheetTags.Has(sheet, "HÉROS") && !SheetTags.Has(sheet, "mage"), "Has compare sans casse, sur le tag entier");
            t.Check(SheetTags.Remove(sheet, "héros") && sheet.Tags.Count == 1 && sheet.Tags[0] == "mage noir", "retrait sans casse");
            t.Check(!SheetTags.Remove(sheet, "absent"), "retirer un tag absent ne fait rien");

            var project = Project.CreateNew();
            var sheets = project.Category(Project.KeySheets);
            var a = new BinderItem { Kind = ItemKind.Sheet, Title = "A" };
            var b = new BinderItem { Kind = ItemKind.Sheet, Title = "B" };
            var trashed = new BinderItem { Kind = ItemKind.Sheet, Title = "T" };
            SheetTags.Add(a, "zèbre"); SheetTags.Add(a, "Éléphant");
            SheetTags.Add(b, "antilope"); SheetTags.Add(b, "zèbre");
            SheetTags.Add(trashed, "corbeille");
            sheets.Children.Add(a); sheets.Children.Add(b);
            project.Trash.Children.Add(trashed);
            project.RelinkParents();
            t.Equal("antilope|Éléphant|zèbre", string.Join("|", SheetTags.All(project).ToArray()), "l'inventaire : sans doublon, trié à la française, corbeille exclue");
            var copy = a.Duplicate();
            t.Equal("zèbre|Éléphant", string.Join("|", copy.Tags.ToArray()), "Dupliquer emporte les tags");
            copy.Tags.Clear();
            t.Equal(2, a.Tags.Count, "…en copie, pas en partage");
        }

        private static void Goals(Harness t)
        {
            t.Equal(5000, TextGoal.Parse("5 000"), "un nombre avec séparateur de milliers");
            t.Equal(3, TextGoal.Parse("-3"), "les chiffres seuls comptent");
            t.Equal(0, TextGoal.Parse("abc"), "sans chiffre = 0");
            t.Equal(0, TextGoal.Parse(null), "null = 0");
            t.Equal(TextGoal.Words, TextGoal.Normalize("plop"), "une unité inconnue = mots");
            t.Equal(TextGoal.Pages, TextGoal.Normalize(TextGoal.Pages), "une unité connue reste");
            t.Equal(12, TextGoal.Measure(TextGoal.Chars, 3, 12, 1), "la mesure suit l'unité (signes)");
            t.Equal(1, TextGoal.Measure(TextGoal.Pages, 3, 12, 1), "…(pages)");
            t.Equal(3, TextGoal.Measure(TextGoal.Words, 3, 12, 1), "…(mots)");
            t.Equal(0.5, TextGoal.Ratio(50, 100), "la part de l'objectif");
            t.Equal(1.0, TextGoal.Ratio(150, 100), "…bornée à 1");
            t.Equal(0.0, TextGoal.Ratio(50, 0), "…0 sans objectif");
            t.Check(TextGoal.Exceeded(101, 100) && !TextGoal.Exceeded(100, 100) && !TextGoal.Exceeded(500, 0), "la limite : dépassée au-delà seulement, jamais sans limite");
            t.Equal("1 mot", TextGoal.Count(1, TextGoal.Words), "le singulier");
            t.Check(TextGoal.Count(2, TextGoal.Pages).EndsWith(" pages") && TextGoal.Count(2, TextGoal.Chars).EndsWith(" signes"), "le pluriel par unité");
            t.Equal("Signes", TextGoal.Label(TextGoal.Chars), "le libellé du sélecteur");
            var text = new BinderItem { Kind = ItemKind.Text, Title = "X", Goal = 10, GoalUnit = TextGoal.Pages, Limit = 20, LimitUnit = TextGoal.Chars };
            var twin = text.Duplicate();
            t.Check(twin.Goal == 10 && twin.GoalUnit == TextGoal.Pages && twin.Limit == 20 && twin.LimitUnit == TextGoal.Chars, "Dupliquer emporte l'objectif et la limite");
        }

        private static void RoundTrip(Harness t)
        {
            var project = Project.CreateNew();
            var sheets = project.Category(Project.KeySheets);
            var category = project.SheetCategories[0];
            category.Icon = "fiche-objet";
            var tagged = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", CategoryId = category.Id, TemplateId = category.TemplateId };
            SheetTags.Add(tagged, "héros"); SheetTags.Add(tagged, "mage noir");
            var plain = new BinderItem { Kind = ItemKind.Sheet, Title = "Sans tag", CategoryId = category.Id, TemplateId = category.TemplateId };
            sheets.Children.Add(tagged); sheets.Children.Add(plain);
            var writings = project.Category(Project.KeyWritings);
            var goal = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre", Goal = 1500, GoalUnit = TextGoal.Words, Limit = 12, LimitUnit = TextGoal.Pages };
            var free = new BinderItem { Kind = ItemKind.Text, Title = "Libre" };
            writings.Children.Add(goal); writings.Children.Add(free);
            project.RelinkParents();

            var path = Path.Combine(Path.GetTempPath(), "marabook-c53-" + Guid.NewGuid().ToString("N") + ".plot");
            try
            {
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                var backTagged = loaded.FindById(tagged.Id);
                var backPlain = loaded.FindById(plain.Id);
                var backGoal = loaded.FindById(goal.Id);
                var backFree = loaded.FindById(free.Id);
                t.Check(backTagged != null && string.Join("|", backTagged.Tags.ToArray()) == "héros|mage noir", "les tags d'une fiche font l'aller-retour, dans l'ordre");
                t.Check(backPlain != null && backPlain.Tags.Count == 0, "une fiche sans tag n'en gagne pas");
                t.Check(backGoal != null && backGoal.Goal == 1500 && backGoal.GoalUnit == TextGoal.Words && backGoal.Limit == 12 && backGoal.LimitUnit == TextGoal.Pages,
                    "l'objectif et la limite d'un écrit font l'aller-retour, unités comprises");
                t.Check(backFree != null && backFree.Goal == 0 && backFree.Limit == 0 && backFree.GoalUnit == TextGoal.Words && backFree.LimitUnit == TextGoal.Words,
                    "un écrit sans objectif ni limite reste à zéro, en mots");
                t.Equal("fiche-objet", loaded.FindSheetCategory(category.Id).Icon, "l'icône choisie d'une catégorie fait l'aller-retour");
                t.Check(loaded.FindSheetCategory(project.SheetCategories[1].Id).Icon == null, "…et reste nulle quand rien n'est choisi");
                t.Check(loaded.LoadedFormatVersion >= 41, "le .plot est en v41 au moins (" + loaded.LoadedFormatVersion + ")");
                // Le modèle Personnage d'un projet neuf porte déjà « Date de
                // décès » ; relu, il ne la reçoit pas deux fois.
                var character = loaded.CharacterTemplate();
                t.Check(character != null && Names(character).FindAll(delegate(string n) { return n == SheetDefaults.FieldDeathDate; }).Count == 1, "le modèle Personnage relu porte « Date de décès » une seule fois");
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        private static void UndoTags(Harness t)
        {
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira" };
            SheetTags.Add(sheet, "héros");
            var before = SheetState.Capture(sheet);
            SheetTags.Add(sheet, "mage noir");
            var after = SheetState.Capture(sheet);
            t.Check(SheetState.DiffKey(before, after).Contains("tags-set"), "la clé de différence nomme les tags (jamais fondue)");
            before.ApplyTo(sheet);
            t.Equal("héros", string.Join("|", sheet.Tags.ToArray()), "rejouer l'état d'avant rend les tags d'avant");
            after.ApplyTo(sheet);
            t.Equal("héros|mage noir", string.Join("|", sheet.Tags.ToArray()), "…et l'état d'après les siens");
            t.Equal("", SheetState.DiffKey(after, SheetState.Capture(sheet)), "sans changement, aucune clé");
        }
    }
}

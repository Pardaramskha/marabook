using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C33 — les modules (DLC, 22/09) : le manifeste se lit, le
    /// paquet .mdlc s'écrit, s'installe dans un dossier de test et se retire ;
    /// la fiche de module se crée, se compte, se dit complète ; les succès
    /// d'un module s'intercalent avant les paliers et leurs règles tiennent ;
    /// le vrai module.json de FPDM (dépôt voisin) se lit s'il est là.</summary>
    public static class ModulesTests
    {
        private const string Manifest = @"{
  ""id"": ""demo"", ""name"": ""Démo"", ""title"": ""Module de démonstration"", ""version"": ""1.2.0"",
  ""features"": [""Une fiche"", ""Des succès""],
  ""sheet"": {
    ""categories"": [""Personnage""],
    ""button"": ""Créer une fiche Démo"", ""tab"": ""Démo"",
    ""papers"": [
      { ""title"": ""Général"", ""fields"": [
        { ""id"": ""a"", ""label"": ""Champ A"", ""hint"": ""indication"" },
        { ""id"": ""b"", ""label"": ""Champ B"", ""lines"": 10, ""group"": ""Groupe"" } ] },
      { ""title"": ""Goûts"", ""columns"": [""Aime"", ""Déteste""], ""fields"": [ { ""id"": ""food"", ""label"": ""Nourriture"" } ] }
    ]
  },
  ""achievements"": [
    { ""id"": ""demo-un"", ""name"": ""Un"", ""description"": ""Une fiche complète."", ""rule"": ""advanced-complete"" },
    { ""id"": ""demo-cinq"", ""name"": ""Cinq"", ""description"": ""Cinq fiches."", ""rule"": ""advanced-complete"", ""count"": 5 },
    { ""id"": ""demo-tout"", ""name"": ""Tout"", ""description"": ""Tout rempli."", ""rule"": ""sheet-complete"" }
  ]
}";

        public static void Run(Harness t)
        {
            t.Suite("C33 — modules (DLC)");
            var root = Path.Combine(Path.GetTempPath(), "marabook-c33-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var previousRoot = Modules.RootOverride;
            Modules.RootOverride = Path.Combine(root, "dlc");
            try
            {
                // — Le manifeste.
                var module = Modules.Parse(Manifest);
                t.Equal("demo", module.Id, "id lu");
                t.Equal("1.2.0", module.Version, "version lue");
                t.Equal(2, module.Papers.Count, "deux papers");
                t.Equal(10, module.Papers[0].Fields[1].Lines, "lignes d'un champ long");
                t.Equal(2, module.Papers[0].Fields[0].Lines, "deux lignes par défaut");
                t.Equal("Groupe", module.Papers[0].Fields[1].Group, "le groupe d'un champ");
                t.Equal("a|b|food.0|food.1", string.Join("|", module.ValueIds().ToArray()), "les identifiants de valeur : un par champ, un par colonne");
                t.Equal(3, module.Achievements.Count, "trois succès");
                t.Equal(5, module.Achievements[1].Count, "le seuil d'un succès");
                var failed = false;
                try { Modules.Parse("{\"name\":\"sans id\"}"); } catch { failed = true; }
                t.Check(failed, "un manifeste sans id est refusé");

                // — Le paquet : écrit, relu, installé, listé, retiré.
                var mdlc = Path.Combine(root, "demo.mdlc");
                var extras = Path.Combine(root, "extras");
                Directory.CreateDirectory(Path.Combine(extras, "achievements"));
                File.WriteAllBytes(Path.Combine(extras, "achievements", "demo-un.png"), new byte[] { 1, 2, 3 });
                Modules.Pack(Manifest, extras, mdlc);
                t.Equal("demo", Modules.Read(mdlc).Id, "le paquet se relit");
                Modules.Load();
                t.Equal(0, Modules.Installed.Count, "rien d'installé dans un dossier neuf");
                var installed = Modules.Install(mdlc);
                t.Check(Modules.IsInstalled("demo") && installed.Version == "1.2.0", "installé et listé");
                t.Check(File.Exists(Path.Combine(Modules.DirOf("demo"), "achievements", "demo-un.png")), "l'image du succès est déballée dans le dossier du module");
                var all = new List<Achievement>(Achievements.All);
                var indexUn = all.FindIndex(delegate(Achievement a) { return a.Id == "demo-un"; });
                var indexTier = all.FindIndex(delegate(Achievement a) { return a.Id == "petit-nerd"; });
                t.Check(indexUn >= 0 && indexTier > indexUn && all[indexUn].ModuleId == "demo", "les succès du module s'intercalent avant les paliers, marqués de leur module");
                t.Check(Achievements.Find("demo-cinq") != null, "Find voit un succès de module");

                // — La fiche de module sur une fiche Personnage.
                var project = Project.CreateNew();
                var template = project.CharacterTemplate();
                var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Léa", TemplateId = template.Id };
                foreach (var category in project.SheetCategories)
                    if (category.Name == "Personnage") sheet.CategoryId = category.Id;
                project.Category(Project.KeySheets).Children.Add(sheet);
                var place = new BinderItem { Kind = ItemKind.Sheet, Title = "Paris" };
                foreach (var category in project.SheetCategories)
                    if (category.Name == "Lieu") { place.CategoryId = category.Id; place.TemplateId = category.TemplateId; }
                project.Category(Project.KeySheets).Children.Add(place);
                project.RelinkParents();
                t.Equal(1, Modules.ForSheet(project, sheet).Count, "le module vise le Personnage");
                t.Equal(0, Modules.ForSheet(project, place).Count, "…pas le Lieu");
                t.Check(!Modules.HasSheet(module, sheet), "pas de fiche de module avant création");
                var values = Modules.ValuesOf(module, sheet, true);
                t.Check(Modules.HasSheet(module, sheet) && values.Count == 0, "créée vide");
                t.Equal(0, Modules.FilledCount(module, sheet), "aucune valeur remplie");
                t.Check(!Modules.IsComplete(module, sheet), "pas complète");
                values["a"] = "x"; values["b"] = "y"; values["food.0"] = "pain";
                t.Equal(3, Modules.FilledCount(module, sheet), "trois valeurs sur quatre");
                values["food.1"] = "   ";
                t.Check(!Modules.IsComplete(module, sheet), "une valeur blanche ne compte pas");
                values["food.1"] = "chou";
                t.Check(Modules.IsComplete(module, sheet), "complète");

                // — Les règles de succès.
                var holding = Modules.Holding(project);
                t.Check(holding.Contains("demo-un") && !holding.Contains("demo-cinq") && !holding.Contains("demo-tout"),
                    "une fiche complète : « Un » tient, pas « Cinq » ni « Tout »");
                var facts = new AchievementFacts { ModuleHolds = holding };
                t.Check(Achievements.Holds("demo-un", facts) && !Achievements.Holds("demo-cinq", facts), "Holds lit les succès de module dans les faits");
                var earned = Achievements.Earned(facts, new List<string>());
                t.Check(earned.Contains("demo-un") && !earned.Contains("demo-cinq"), "Earned rend le succès de module gagné");
                // Tout ce qui peut l'être : champs, texte, portrait, relations,
                // graph, suivi + évolution — puis « Tout » tient.
                foreach (var field in template.Fields) sheet.FieldValues[field.Id] = "rempli";
                sheet.Document = TextDocument.FromPlainText("Un texte libre.");
                sheet.ImageId = project.AddImage(new byte[] { 1, 2, 3 }, ".png");
                sheet.Relations.Add(new SheetRelation { Kind = "Sœur", Name = "Zoé" });
                template.Radar = true;
                foreach (var name in SheetTemplate.DefaultRadarAxes) template.RadarAxes.Add(new RadarAxis { Name = name });
                t.Check(!Modules.IsSheetComplete(project, sheet), "sans valeur sur chaque axe, pas complète");
                foreach (var axis in template.RadarAxes) sheet.RadarValues[axis.Id] = 3;
                t.Check(!Modules.IsSheetComplete(project, sheet), "sans suivi ni évolution activés, pas complète");
                template.Tracking = true;
                template.Evolution = true;
                sheet.Evolution.Add(new EvolutionEntry { Note = "grandit" });
                t.Check(Modules.IsSheetComplete(project, sheet), "tout rempli : la fiche est complète");
                t.Check(Modules.Holding(project).Contains("demo-tout"), "« Tout » tient");
                for (var i = 0; i < 4; i++)
                {
                    var other = new BinderItem { Kind = ItemKind.Sheet, Title = "P" + i, TemplateId = template.Id, CategoryId = sheet.CategoryId };
                    var v = Modules.ValuesOf(module, other, true);
                    foreach (var id in module.ValueIds()) v[id] = "ok";
                    project.Category(Project.KeySheets).Children.Add(other);
                }
                project.RelinkParents();
                t.Check(Modules.Holding(project).Contains("demo-cinq"), "cinq fiches complètes : « Cinq » tient");

                // — Retrait : plus de module, plus de succès de module.
                Modules.Uninstall("demo");
                t.Check(!Modules.IsInstalled("demo") && Achievements.Find("demo-un") == null, "désinstallé : listé nulle part");
                t.Check(sheet.ModuleValues.ContainsKey("demo"), "…mais la fiche garde ses valeurs");

                // — Le vrai FPDM, si le dépôt voisin est là.
                var fpdm = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\marabook-dlc-fpdm\module.json"));
                if (File.Exists(fpdm))
                {
                    var real = Modules.Parse(File.ReadAllText(fpdm, System.Text.Encoding.UTF8));
                    var ids = new HashSet<string>();
                    var duplicate = false;
                    foreach (var id in real.ValueIds()) if (!ids.Add(id)) duplicate = true;
                    t.Check(real.Id == "fpdm" && real.Papers.Count == 14 && !duplicate && real.ValueIds().Count > 150,
                        "FPDM : 14 papers, identifiants uniques, " + real.ValueIds().Count + " valeurs attendues");
                    t.Equal(3, real.Achievements.Count, "FPDM : trois succès");
                    t.Equal("Personnage", real.Categories[0], "FPDM vise le Personnage");
                }
                else t.Info("dépôt marabook-dlc-fpdm absent : le vrai manifeste n'est pas vérifié");
            }
            finally
            {
                Modules.RootOverride = previousRoot;
                Modules.Load();
                try { Directory.Delete(root, true); } catch { }
            }
        }
    }
}

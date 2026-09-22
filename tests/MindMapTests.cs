using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C36 — les cartes mentales (22/09) : ce que Marabook sait d'un
    /// .tea de Mental-o sans le module (boîtes, liens, groupes, aperçu, texte
    /// cherchable), la racine « Cartes mentales », l'aller-retour du .tea dans
    /// le .plot (v29), le manifeste d'un module à code, et les règles de
    /// succès « mindmaps » / « mindmap-nodes ».</summary>
    public static class MindMapTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C36 — cartes mentales");
            Inspect(t);
            Root(t);
            Manifest(t);
            Rules(t);
        }

        /// <summary>Un .tea minimal (zip + document.json au format de Mental-o).</summary>
        public static byte[] SampleTea(int nodes, int links, int groups)
        {
            var json = new StringBuilder();
            json.Append("{\"version\":16,\"noeuds\":[");
            for (var i = 0; i < nodes; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"id\":\"n").Append(i).Append("\",\"x\":").Append(i * 100).Append(",\"y\":").Append(i * 40)
                    .Append(",\"riche\":[{\"t\":\"Boîte ").Append(i + 1).Append("\"}]}");
            }
            json.Append("],\"liens\":[");
            for (var i = 0; i < links; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"id\":\"l").Append(i).Append("\",\"sourceId\":\"n0\",\"cibleId\":\"n").Append(i + 1).Append("\"}");
            }
            json.Append("],\"groupes\":[");
            for (var i = 0; i < groups; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("{\"id\":\"g").Append(i).Append("\",\"noeuds\":[\"n0\"],\"titre\":\"Groupe\"}");
            }
            json.Append("],\"nom\":\"Carte\"}");
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    var entry = archive.CreateEntry("document.json");
                    using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(json.ToString());
                }
                return stream.ToArray();
            }
        }

        private static void Inspect(Harness t)
        {
            var summary = MindMaps.Inspect(SampleTea(3, 2, 1));
            t.Check(summary.Readable && summary.Nodes == 3 && summary.Links == 2 && summary.Groups == 1, "trois boîtes, deux liens, un groupe lus dans le document.json");
            t.Equal("3 boîtes · 2 liens · 1 groupe", summary.Label, "le libellé de la tuile");
            t.Equal("Boîte 1 · Boîte 2 · Boîte 3", summary.Preview, "l'aperçu : les textes des boîtes (segments riches)");
            t.Equal("Carte vide", MindMaps.Inspect(SampleTea(0, 0, 0)).Label, "une carte sans boîte");
            var bad = MindMaps.Inspect(Encoding.UTF8.GetBytes("pas un zip"));
            t.Check(!bad.Readable && bad.Label == "Carte illisible", "des octets qui ne sont pas un .tea : illisible, sans exception");
            t.Check(!MindMaps.Inspect(null).Readable && MindMaps.Inspect(new byte[0]).Nodes == 0, "null ou vide : un résumé vide");
            var text = MindMaps.SearchText(SampleTea(2, 0, 0));
            t.Equal("Boîte 1\nBoîte 2\n", text, "le texte cherchable : une ligne par boîte");
            var tea = SampleTea(1, 0, 0);
            t.Check(ReferenceEquals(MindMaps.Inspect(tea), MindMaps.Inspect(tea)), "le résumé est mémorisé par tableau d'octets");
        }

        private static void Root(Harness t)
        {
            var project = Project.CreateNew();
            var root = project.Category(Project.KeyMindMaps);
            t.Check(root != null && root.Title == "Cartes mentales" && root.IsCategory, "un projet neuf a sa racine Cartes mentales");
            var plans = project.Roots.IndexOf(project.Category(Project.KeyPlans));
            var dictionary = project.Roots.IndexOf(project.Category(Project.KeyDictionary));
            var maps = project.Roots.IndexOf(root);
            t.Check(plans < maps && maps < dictionary, "…entre Plans et Dictionnaire");
            root.Children.Add(new BinderItem { Kind = ItemKind.MindMap, Title = "Carte", MapBytes = SampleTea(2, 1, 0) });
            project.RelinkParents();
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c36-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "cartes.plot");
                PlotFile.Save(project, path);
                using (var zip = ZipFile.OpenRead(path))
                    t.Check(zip.GetEntry("maps/" + root.Children[0].Id + ".tea") != null, "le .tea est une entrée maps/<id>.tea du .plot");
                var loaded = PlotFile.Load(path);
                var back = loaded.Category(Project.KeyMindMaps).Children[0];
                t.Check(back.Kind == ItemKind.MindMap && back.MapBytes != null && back.MapBytes.Length == root.Children[0].MapBytes.Length,
                    "relue depuis le .plot : même nature, mêmes octets");
                t.Equal(2, MindMaps.Inspect(back.MapBytes).Nodes, "…et lisible");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void Manifest(Harness t)
        {
            var module = Modules.Parse("{\"id\":\"mental-o\",\"name\":\"Mental-o\",\"version\":\"1.0.0\","
                + "\"entry\":{\"assembly\":\"bin/MentalO.Marabook.dll\",\"type\":\"MentalO.Marabook.Module\"},"
                + "\"achievements\":[{\"id\":\"mo-1\",\"name\":\"Cartographe mental\",\"description\":\"Une carte.\",\"rule\":\"mindmaps\"},"
                + "{\"id\":\"mo-2\",\"name\":\"Toile\",\"description\":\"Cinquante boîtes.\",\"rule\":\"mindmap-nodes\",\"count\":50}]}");
            t.Check(module.HasCode && module.EntryAssembly == "bin/MentalO.Marabook.dll" && module.EntryType == "MentalO.Marabook.Module",
                "le manifeste d'un module à code nomme sa DLL et son type");
            t.Check(!Modules.Parse("{\"id\":\"demo\"}").HasCode, "sans « entry » : un module sans code");
            t.Check(Extensions.ModuleRegistry.Find("mental-o") == null && Extensions.ModuleRegistry.MindMaps == null,
                "aucun module à code chargé dans les tests");
        }

        private static void Rules(Harness t)
        {
            var previousRoot = Modules.RootOverride;
            var root = Path.Combine(Path.GetTempPath(), "marabook-tests-c36-rules-" + Guid.NewGuid().ToString("N"));
            Modules.RootOverride = Path.Combine(root, "dlc");
            try
            {
                Directory.CreateDirectory(Path.Combine(Modules.RootOverride, "mo"));
                File.WriteAllText(Path.Combine(Modules.RootOverride, "mo", Modules.Manifest),
                    "{\"id\":\"mo\",\"name\":\"MO\",\"achievements\":["
                    + "{\"id\":\"mo-une\",\"name\":\"Une\",\"description\":\"\",\"rule\":\"mindmaps\"},"
                    + "{\"id\":\"mo-trois\",\"name\":\"Trois\",\"description\":\"\",\"rule\":\"mindmaps\",\"count\":3},"
                    + "{\"id\":\"mo-grosse\",\"name\":\"Grosse\",\"description\":\"\",\"rule\":\"mindmap-nodes\",\"count\":4}]}",
                    new UTF8Encoding(false));
                Modules.Load();
                t.Check(Modules.IsInstalled("mo"), "le module sans code s'installe comme avant");
                var project = Project.CreateNew();
                var maps = project.Category(Project.KeyMindMaps);
                t.Equal(0, Modules.Holding(project).Count, "sans carte : rien");
                maps.Children.Add(new BinderItem { Kind = ItemKind.MindMap, Title = "A", MapBytes = SampleTea(2, 1, 0) });
                project.RelinkParents();
                var holding = Modules.Holding(project);
                t.Check(holding.Contains("mo-une") && !holding.Contains("mo-trois") && !holding.Contains("mo-grosse"), "une carte : « Une » tient, pas « Trois » ni « Grosse »");
                maps.Children.Add(new BinderItem { Kind = ItemKind.MindMap, Title = "B", MapBytes = SampleTea(5, 4, 0) });
                maps.Children.Add(new BinderItem { Kind = ItemKind.MindMap, Title = "C", MapBytes = SampleTea(1, 0, 0) });
                project.RelinkParents();
                holding = Modules.Holding(project);
                t.Check(holding.Contains("mo-trois") && holding.Contains("mo-grosse"), "trois cartes dont une de cinq boîtes : « Trois » et « Grosse »");
                project.Trash.Children.Add(maps.Children[1]);
                maps.Children.RemoveAt(1);
                project.RelinkParents();
                holding = Modules.Holding(project);
                t.Check(!holding.Contains("mo-grosse"), "une carte à la corbeille ne compte plus");
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

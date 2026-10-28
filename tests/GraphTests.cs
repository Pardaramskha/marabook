using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C55 — le GRAPHE RELATIONNEL (1.1.0, 28/10/2026) : la
    /// photographie du projet (GraphInput.Capture), les arêtes de chaque
    /// nature (relations dédoublonnées, liens [[…]], présences pondérées,
    /// livre → écrits, plan → écrits, tags), les lectures (voisinage,
    /// orphelins, îlots, absences, jamais présents), la disposition
    /// (déterminisme, épinglés, départ chaud) et l'aller-retour .plot v43.</summary>
    public static class GraphTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C55 — 1.1.0 : graphe relationnel (arêtes, lectures, disposition, .plot v43)");
            Edges(t);
            Readings(t);
            Layout(t);
            RoundTrip(t);
        }

        private class World
        {
            public Project Project;
            public BinderItem Keira, Marabout, Ermite, Chapter1, Chapter2, Chapter3, Book, Plan, Orphan;
        }

        /// <summary>Un petit monde : trois personnages (Keira ↔ Le marabout
        /// liés par une relation réciproque ; l'ermite seul), un livre de
        /// trois chapitres où Keira paraît dans les deux premiers et le
        /// marabout dans le premier seulement, un lien [[Keira]] dans le
        /// chapitre 3, un plan relié au chapitre 2, une fiche orpheline.</summary>
        private static World Make()
        {
            var project = Project.CreateNew();
            var world = new World { Project = project };
            var category = project.SheetCategories[0];
            var sheets = project.Category(Project.KeySheets);
            Func<string, BinderItem> sheet = delegate(string title)
            {
                var item = new BinderItem { Kind = ItemKind.Sheet, Title = title, CategoryId = category.Id, TemplateId = category.TemplateId };
                sheets.Children.Add(item);
                return item;
            };
            world.Keira = sheet("Keira");
            world.Marabout = sheet("Le marabout");
            world.Ermite = sheet("Ermite");
            world.Orphan = sheet("Zorglub");
            world.Keira.Relations.Add(new SheetRelation { Kind = "Ami", TargetId = world.Marabout.Id });
            world.Marabout.Relations.Add(new SheetRelation { Kind = "Ami", TargetId = world.Keira.Id });
            SheetTags.Add(world.Keira, "héros"); SheetTags.Add(world.Ermite, "héros");

            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear(); // le « Nouvel écrit » d'un projet neuf n'est pas du monde
            world.Book = new BinderItem { Kind = ItemKind.Book, Title = "Le livre" };
            writings.Children.Add(world.Book);
            Func<string, string, BinderItem> chapter = delegate(string title, string text)
            {
                var item = new BinderItem { Kind = ItemKind.Text, Title = title };
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun { Text = text });
                item.Document.Paragraphs.Add(paragraph);
                world.Book.Children.Add(item);
                return item;
            };
            world.Chapter1 = chapter("Un", "Keira rencontre Le marabout. Keira sourit.");
            world.Chapter2 = chapter("Deux", "Keira marche seule.");
            world.Chapter3 = chapter("Trois", "Personne. Un souvenir de [[Keira]].");
            var plans = project.Category(Project.KeyPlans);
            world.Plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan", Plan = new PlanInfo() };
            world.Plan.Plan.Columns.Add(new PlanColumn { LinkedTextId = world.Chapter2.Id });
            plans.Children.Add(world.Plan);
            project.RelinkParents();
            return world;
        }

        private static RelationGraph Graph(World world, GraphOptions options)
        {
            return RelationGraph.Build(GraphInput.Capture(world.Project), options);
        }

        private static int Count(RelationGraph graph, GraphEdgeKind kind)
        {
            var n = 0;
            foreach (var edge in graph.Edges) if (edge.Kind == kind) n++;
            return n;
        }

        private static GraphEdge Between(RelationGraph graph, string a, string b, GraphEdgeKind kind)
        {
            foreach (var edge in graph.EdgesOf(a))
                if (edge.Kind == kind && edge.Other(a) == b) return edge;
            return null;
        }

        private static void Edges(Harness t)
        {
            var world = Make();
            var entries = GraphInput.Capture(world.Project);
            t.Check(entries.Count == 9, "la photographie prend fiches, écrits, livre et plan — pas les racines ni la corbeille (" + entries.Count + ")");
            var graph = Graph(world, new GraphOptions { Tags = true });
            t.Equal(9, graph.Nodes.Count, "un nœud par élément");
            t.Equal(1, Count(graph, GraphEdgeKind.Relation), "une relation réciproque ne fait qu'une arête");
            var friendship = Between(graph, world.Keira.Id, world.Marabout.Id, GraphEdgeKind.Relation);
            t.Check(friendship != null && friendship.Label == "Ami", "…étiquetée de sa nature");
            t.Equal(4, Count(graph, GraphEdgeKind.Presence), "quatre présences : Keira dans les trois chapitres (le [[lien]] du chapitre 3 porte aussi son nom), le marabout au premier");
            var presence = Between(graph, world.Chapter1.Id, world.Keira.Id, GraphEdgeKind.Presence);
            t.Check(presence != null && presence.Weight == 2, "la présence pèse le nombre d'occurrences (Keira ×2 au chapitre 1)");
            t.Check(Between(graph, world.Chapter3.Id, world.Keira.Id, GraphEdgeKind.Wiki) != null, "un lien [[Keira]] dans un écrit fait une arête de lien");
            t.Equal(3, Count(graph, GraphEdgeKind.Structure), "le livre tient ses trois chapitres");
            t.Check(Between(graph, world.Plan.Id, world.Chapter2.Id, GraphEdgeKind.Plan) != null, "un plan relie l'écrit de sa colonne");
            var tagged = Between(graph, world.Keira.Id, world.Ermite.Id, GraphEdgeKind.Tag);
            t.Check(tagged != null && tagged.Label == "héros", "deux fiches au même tag se relient par lui (option)");
            t.Check(graph.Node(world.Keira.Id).Presence == 3 && graph.Node(world.Chapter1.Id).Presence == 2, "le compte de présences d'un nœud (Keira dans 3 écrits ; 2 fiches au chapitre 1)");
            t.Check(graph.Node(world.Keira.Id).Degree == 1 + 3 + 1 + 1, "le degré compte toutes les natures (Keira : relation, 3 présences, lien, tag)");

            var sheetsOnly = Graph(world, new GraphOptions { Texts = false });
            t.Check(sheetsOnly.Nodes.Count == 4 && Count(sheetsOnly, GraphEdgeKind.Presence) == 0 && Count(sheetsOnly, GraphEdgeKind.Relation) == 1, "fiches seules : plus d'écrits, plus de présences, la relation reste");
            var bare = Graph(world, new GraphOptions { Relations = false, Wiki = false, Presence = false, Structure = false, Plans = false });
            t.Check(bare.Nodes.Count == 9 && bare.Edges.Count == 0, "toutes les natures éteintes : des nœuds, aucune arête");
        }

        private static void Readings(Harness t)
        {
            var world = Make();
            var graph = Graph(world, new GraphOptions());
            var hop1 = graph.Neighborhood(world.Keira.Id, 1, null);
            t.Check(hop1.Count == 5 && hop1.Contains(world.Keira.Id) && hop1.Contains(world.Marabout.Id) && hop1.Contains(world.Chapter1.Id) && hop1.Contains(world.Chapter2.Id) && hop1.Contains(world.Chapter3.Id),
                "voisinage à un saut : elle-même, l'ami, les trois écrits (" + hop1.Count + ")");
            var hop2 = graph.Neighborhood(world.Keira.Id, 2, null);
            t.Check(hop2.Contains(world.Book.Id) && hop2.Contains(world.Plan.Id) && !hop2.Contains(world.Ermite.Id), "à deux sauts : le livre et le plan arrivent, l'ermite non");
            var relationsOnly = graph.Neighborhood(world.Keira.Id, 2, delegate(GraphEdge e) { return e.Kind == GraphEdgeKind.Relation; });
            t.Check(relationsOnly.Count == 2, "le voisinage par les seules relations : elle et l'ami");
            t.Check(graph.Neighborhood("inconnu", 3, null).Count == 0, "un id inconnu : voisinage vide");

            var orphans = graph.Orphans(null);
            t.Check(orphans.Count == 2 && orphans.Exists(delegate(GraphNode n) { return n.Id == world.Orphan.Id; }) && orphans.Exists(delegate(GraphNode n) { return n.Id == world.Ermite.Id; }),
                "orphelins : Zorglub et l'ermite (sans tag ils ne tiennent à rien)");
            var components = graph.Components(null);
            t.Check(components.Count == 1 && components[0].Count == 7, "un seul îlot de sept nœuds (" + components.Count + ")");
            var absences = graph.Absences();
            t.Check(absences.Count == 2 && absences[0].Sheet.Id == world.Marabout.Id && absences[0].Since == 2 && absences[0].Appearances == 1,
                "absences : le marabout d'abord, muet depuis 2 écrits");
            t.Check(absences[1].Sheet.Id == world.Keira.Id && absences[1].Since == 0 && absences[1].LastIndex == 2 && absences[1].Appearances == 3, "…puis Keira, présente jusqu'au dernier");
            var never = graph.NeverPresent();
            t.Check(never.Count == 2 && never.Exists(delegate(GraphNode n) { return n.Id == world.Ermite.Id; }), "jamais présents : l'ermite et Zorglub");
            t.Equal(2, graph.PresenceCount(world.Chapter1.Id, world.Keira.Id), "le compte d'une fiche dans un écrit");
            t.Equal(0, graph.PresenceCount(world.Chapter3.Id, world.Marabout.Id), "…zéro quand elle n'y est pas");
            t.Check(graph.Writings.Count == 3 && graph.Writings[0].Id == world.Chapter1.Id && graph.Writings[2].Id == world.Chapter3.Id, "les écrits du graphe suivent l'ordre du récit");
        }

        private static void Layout(Harness t)
        {
            var world = Make();
            var graph = Graph(world, new GraphOptions());
            var a = GraphLayout.Arrange(graph, null, 100, 7);
            var b = GraphLayout.Arrange(graph, null, 100, 7);
            t.Check(a.Count == graph.Nodes.Count, "une position par nœud");
            var same = true;
            foreach (var pair in a) if (Math.Abs(pair.Value.X - b[pair.Key].X) > 0.001 || Math.Abs(pair.Value.Y - b[pair.Key].Y) > 0.001) same = false;
            t.Check(same, "déterministe à graine égale");
            var apart = true;
            var keys = new List<string>(a.Keys);
            for (var i = 0; i < keys.Count; i++)
                for (var j = i + 1; j < keys.Count; j++)
                {
                    var dx = a[keys[i]].X - a[keys[j]].X;
                    var dy = a[keys[i]].Y - a[keys[j]].Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < 20) apart = false;
                }
            t.Check(apart, "les nœuds ne se chevauchent pas (≥ 20 px entre deux)");
            var ka = a[world.Keira.Id]; var ma = a[world.Marabout.Id]; var oa = a[world.Orphan.Id];
            var friends = Math.Sqrt((ka.X - ma.X) * (ka.X - ma.X) + (ka.Y - ma.Y) * (ka.Y - ma.Y));
            var strangers = Math.Sqrt((ka.X - oa.X) * (ka.X - oa.X) + (ka.Y - oa.Y) * (ka.Y - oa.Y));
            t.Check(friends < strangers, "deux fiches reliées finissent plus proches qu'une fiche et une orpheline (" + friends.ToString("0") + " < " + strangers.ToString("0") + ")");
            double cx = 0, cy = 0;
            foreach (var pair in a) { cx += pair.Value.X; cy += pair.Value.Y; }
            t.Check(Math.Abs(cx / a.Count) < 1 && Math.Abs(cy / a.Count) < 1, "recentré sur l'origine");

            var pinned = new Dictionary<string, GraphPosition> { { world.Keira.Id, new GraphPosition { X = 500, Y = -300, Pinned = true } } };
            var c = GraphLayout.Arrange(graph, pinned, 60, 7);
            t.Check(Math.Abs(c[world.Keira.Id].X - 500) < 0.01 && Math.Abs(c[world.Keira.Id].Y + 300) < 0.01 && c[world.Keira.Id].Pinned, "un nœud épinglé ne bouge pas et reste épinglé");
            var warm = GraphLayout.Arrange(graph, a, GraphLayout.IterationsFor(graph.Nodes.Count, true), 7);
            var drift = 0.0;
            foreach (var pair in warm) drift = Math.Max(drift, Math.Abs(pair.Value.X - a[pair.Key].X) + Math.Abs(pair.Value.Y - a[pair.Key].Y));
            t.Check(drift < 60, "un départ chaud sur une disposition posée la fait peu bouger (dérive max " + drift.ToString("0") + " px)");
            t.Check(GraphLayout.IterationsFor(50, false) == 300 && GraphLayout.IterationsFor(200, false) == 150 && GraphLayout.IterationsFor(500, false) == 80 && GraphLayout.IterationsFor(0, false) == 0,
                "le nombre d'itérations décroît avec la taille");
            t.Check(GraphLayout.Arrange(new RelationGraph(), null, 10, 1).Count == 0, "graphe vide : rien");
        }

        private static void RoundTrip(Harness t)
        {
            var world = Make();
            var project = world.Project;
            project.GraphLayout[world.Keira.Id] = new GraphPosition { X = 12.5, Y = -40, Pinned = true };
            project.GraphLayout[world.Chapter1.Id] = new GraphPosition { X = -3, Y = 7.25 };
            var path = Path.Combine(Path.GetTempPath(), "marabook-c55-" + Guid.NewGuid().ToString("N") + ".plot");
            try
            {
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                t.Check(loaded.LoadedFormatVersion >= 43, "le .plot est en v43 au moins (" + loaded.LoadedFormatVersion + ")");
                GraphPosition k, c;
                t.Check(loaded.GraphLayout.Count == 2 && loaded.GraphLayout.TryGetValue(world.Keira.Id, out k) && Math.Abs(k.X - 12.5) < 0.001 && Math.Abs(k.Y + 40) < 0.001 && k.Pinned,
                    "la position épinglée fait l'aller-retour");
                t.Check(loaded.GraphLayout.TryGetValue(world.Chapter1.Id, out c) && Math.Abs(c.Y - 7.25) < 0.001 && !c.Pinned, "…et la position posée, non épinglée");
                var empty = Project.CreateNew();
                PlotFile.Save(empty, path);
                t.Check(PlotFile.Load(path).GraphLayout.Count == 0, "sans disposition : rien");
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
                try { File.Delete(path + ".bak"); } catch (IOException) { }
            }
        }
    }
}

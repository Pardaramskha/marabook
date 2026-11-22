using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Correction;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C84 — 2.0.0, lot 23 (21/11) : ce qu'un événement relie
    /// (plusieurs éléments, un lieu, des tags, l'âge des fiches), les fiches
    /// en Markdown et en CSV, le texte de la lecture à voix haute, le
    /// LISEZMOI du .plot, la famille « Champs » et les forces du graphe, le
    /// tri de la bibliothèque retenu par catégorie.</summary>
    public static class Lot23Tests
    {
        public static void Run(Harness t)
        {
            t.Suite("C84 — lot 23 : éléments, lieu, tags et âge d'un événement ; fiches en Markdown / CSV ; voix haute ; LISEZMOI ; champs et forces du graphe");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c84-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Events(t, dir);
                SheetsAsData(t, dir);
                ReadAloud(t);
                Readme(t, dir);
                Graph(t);
                LibrarySorts(t);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void Add(BinderItem parent, BinderItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        private static BinderItem Sheet(SheetTemplate template, string title)
        {
            return new BinderItem { Kind = ItemKind.Sheet, Title = title, TemplateId = template.Id, Document = new TextDocument() };
        }

        // ------------------------------------------------------------ les événements

        private static void Events(Harness t, string dir)
        {
            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            var sheets = project.Category(Project.KeySheets);
            var keira = Sheet(template, "Keira");
            var abel = Sheet(template, "Abel");
            var port = Sheet(template, "Port-Gris");
            Add(sheets, keira);
            Add(sheets, abel);
            Add(sheets, port);
            var text = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre 1", Document = TextDocument.FromPlainText("…") };
            Add(project.Category(Project.KeyWritings), text);
            var timeline = new BinderItem { Kind = ItemKind.Timeline, Title = "Histoire", Timeline = new TimelineInfo { Types = TimelineInfo.DefaultTypes() } };
            Add(project.Category(Project.KeyTimelines), timeline);
            var map = new BinderItem { Kind = ItemKind.Map, Title = "Ehnmir", Map = new MapInfo { Width = 400, Height = 200, Levels = 1 } };
            var harbour = new MapMarker { Name = "Port-Gris", LinkId = port.Id, X = 10, Y = 10 };
            var marches = new MapZone { Name = "Les Marches" };
            map.Map.Markers.Add(harbour);
            map.Map.Zones.Add(marches);
            Add(project.Category(Project.KeyMaps), map);
            project.RelinkParents();
            var system = Timelines.SystemOf(project, timeline);

            // — plusieurs éléments
            var battle = new TimelineEvent { Title = "La bataille des Gués", Start = new TimePoint(30, 6, 1) };
            t.Equal(0, Timelines.LinksOf(battle).Count, "un événement neuf ne relie rien");
            Timelines.SetLinks(battle, new[]
            {
                new EventLink(TimelineLinks.Sheet, keira.Id), new EventLink(TimelineLinks.Sheet, abel.Id), new EventLink(TimelineLinks.Sheet, keira.Id),
                new EventLink(TimelineLinks.Text, text.Id), new EventLink(null, "x"), null
            });
            t.Check(battle.LinkId == keira.Id && battle.LinkKind == TimelineLinks.Sheet, "le premier élément reste l'élément principal");
            t.Equal(3, Timelines.LinksOf(battle).Count, "trois éléments : le doublon et les vides sont écartés");
            t.Check(Timelines.HasLink(battle, abel.Id) && Timelines.HasLink(battle, text.Id) && !Timelines.HasLink(battle, port.Id), "chacun se retrouve");
            t.Equal("Keira, Abel et 1 autre", Timelines.ElementsLabel(project, battle), "le libellé court d'une colonne");
            t.Equal("Keira", Timelines.ElementsLabel(project, new TimelineEvent { LinkKind = TimelineLinks.Sheet, LinkId = keira.Id }), "un seul élément : son nom");
            t.Equal("", Timelines.ElementsLabel(project, new TimelineEvent()), "aucun : rien");
            t.Check(Timelines.Concerns(battle, abel) && Timelines.Concerns(battle, keira) && !Timelines.Concerns(battle, port), "le filtre par fiche voit TOUS les éléments");
            var counts = Timelines.SheetsConcerned(project, new[] { battle });
            t.Check(counts.Count == 2 && counts[0].Sheet == abel && counts[1].Sheet == keira, "le menu du filtre les compte tous les deux");
            timeline.Timeline.Events.Add(battle);
            t.Equal(1, Timelines.EventsFor(project, abel.Id).Count, "une fiche retrouve l'événement dont elle est le DEUXIÈME élément");
            var backlinks = Backlinks.Of(project, abel);
            t.Check(backlinks.Exists(delegate(Backlink b) { return b.Group == Backlink.Events && b.RefId == battle.Id; }), "…et ses rétroliens aussi");
            var copy = battle.Clone();
            copy.More[0].Id = "autre";
            t.Check(battle.More[0].Id == abel.Id, "une copie ne partage pas la liste des éléments");
            Timelines.SetLinks(copy, null);
            t.Check(copy.LinkId == null && copy.More.Count == 0, "poser « rien » vide tout");

            // — le lieu
            t.Check(Timelines.PlaceOf(project, battle) == null, "pas de lieu d'office");
            battle.PlaceMapId = map.Id;
            battle.PlaceShapeId = harbour.Id;
            var place = Timelines.PlaceOf(project, battle);
            t.Check(place != null && place.Marker == harbour && place.Map == map && place.Name == "Port-Gris" && place.SheetId == port.Id, "le lieu : un repère d'une carte, et sa fiche");
            t.Equal("Port-Gris (Ehnmir)", place.Label, "son libellé dit la carte");
            var places = Timelines.Places(project);
            t.Check(places.Count == 2 && places[0].Zone == marches && places[1].Marker == harbour, "les lieux proposés : repères et zones de chaque carte, par nom");
            t.Equal(1, Timelines.EventsAt(project, map.Id, harbour.Id).Count, "la carte retrouve les événements posés sur un lieu");
            t.Equal(0, Timelines.EventsAt(project, map.Id, marches.Id).Count, "…et pas ceux d'ailleurs");
            battle.PlaceShapeId = marches.Id;
            t.Check(Timelines.PlaceOf(project, battle).Zone == marches, "une zone fait un lieu comme un repère");
            battle.PlaceShapeId = "disparu";
            t.Check(Timelines.PlaceOf(project, battle) == null, "un lieu supprimé de la carte : plus de lieu, sans erreur");
            battle.PlaceShapeId = harbour.Id;

            // — les tags
            t.Equal("guerre|Nord|été 30", string.Join("|", Timelines.ParseTags(" #guerre, Nord ; GUERRE,, été 30 ").ToArray()), "les tags saisis : séparés, sans croisillon, sans doublon (casse ignorée)");
            t.Equal(12, Timelines.ParseTags("a,b,c,d,e,f,g,h,i,j,k,l,m,n").Count, "douze au plus");
            battle.Tags = Timelines.ParseTags("guerre, Nord");
            var feast = new TimelineEvent { Title = "Le banquet", Start = new TimePoint(31, 1, 1), Tags = Timelines.ParseTags("Guerre") };
            var plain = new TimelineEvent { Title = "Rien", Start = new TimePoint(32, 1, 1) };
            t.Check(Timelines.HasTag(battle, "GUERRE") && Timelines.HasTag(battle, "nord") && !Timelines.HasTag(plain, "guerre") && !Timelines.HasTag(battle, ""), "un tag se retrouve sans casse ni accents");
            var tags = Timelines.TagsOf(new[] { battle, feast, plain });
            t.Check(tags.Count == 2 && tags[0].Tag == "guerre" && tags[0].Count == 2 && tags[1].Tag == "Nord" && tags[1].Count == 1, "le menu des tags : guerre (2), Nord (1)");
            t.Equal("guerre, Nord", Timelines.TagsText(battle), "…et se réécrivent dans le champ");

            // — la recherche de la liste lit tout cela
            var all = new List<TimelineEvent> { battle, feast, plain };
            t.Equal("La bataille des Gués", Titles(Timelines.Search(project, system, timeline.Timeline, all, "abel")), "la recherche lit le DEUXIÈME élément");
            t.Equal("La bataille des Gués", Titles(Timelines.Search(project, system, timeline.Timeline, all, "ehnmir port")), "…le lieu et sa carte");
            t.Equal("La bataille des Gués,Le banquet", Titles(Timelines.Search(project, system, timeline.Timeline, all, "guerre")), "…et les tags");

            // — l'âge des fiches à la date de l'événement
            t.Equal(0, Timelines.AgesOf(project, system, battle).Count, "sans date de naissance : pas d'âge");
            foreach (var field in template.Fields)
                if (field.Name == SheetDefaults.FieldBirthDate) keira.FieldValues[field.Id] = TimeLinks.Markup(timeline.Title, system.Format(new TimePoint(7, 6, 1)), null);
            var ages = Timelines.AgesOf(project, system, battle);
            t.Check(ages.Count == 1 && ages[0].Sheet == keira && ages[0].Label == TimeSystem.YearsLabel(23), "Keira, née en l'an 7, a 23 ans à la bataille de l'an 30 (" + (ages.Count == 1 ? ages[0].Label : "?") + ")");
            t.Equal("Keira : " + TimeSystem.YearsLabel(23), Timelines.AgesLabel(project, system, battle), "le libellé des âges");
            var before = new TimelineEvent { Title = "Avant", Start = new TimePoint(2, 1, 1), LinkKind = TimelineLinks.Sheet, LinkId = keira.Id };
            var early = Timelines.AgesOf(project, system, before);
            t.Check(early.Count == 1 && early[0].Label.StartsWith("Naît dans", StringComparison.Ordinal), "avant sa naissance : « Naît dans… » (" + (early.Count == 1 ? early[0].Label : "?") + ")");
            t.Equal(0, Timelines.AgesOf(project, system, new TimelineEvent { Title = "Écrit", Start = new TimePoint(30, 1, 1), LinkKind = TimelineLinks.Text, LinkId = text.Id }).Count, "un écrit n'a pas d'âge");
            t.Equal(0, Timelines.AgesOf(project, system, new TimelineEvent { Derived = true, Start = new TimePoint(30, 1, 1), LinkKind = TimelineLinks.Sheet, LinkId = keira.Id }).Count, "une naissance née de la fiche ne redit pas l'âge");

            // — le .plot garde tout, et relit un événement d'avant
            timeline.Timeline.Events.Add(feast);
            timeline.Timeline.Events.Add(new TimelineEvent { Title = "Ancien", Start = new TimePoint(1, 1, 1), LinkKind = TimelineLinks.Sheet, LinkId = abel.Id });
            var path = Path.Combine(dir, "evenements.plot");
            PlotFile.Save(project, path);
            var loaded = PlotFile.Load(path);
            TimelineEvent again = null, old = null;
            foreach (var item in Timelines.All(loaded))
                foreach (var entry in item.Timeline.Events)
                {
                    if (entry.Id == battle.Id) again = entry;
                    if (entry.Title == "Ancien") old = entry;
                }
            t.Check(again != null && Timelines.LinksOf(again).Count == 3 && again.LinkId == keira.Id && again.More[0].Id == abel.Id && again.More[1].Kind == TimelineLinks.Text, "le .plot relit les trois éléments, dans l'ordre");
            t.Check(again != null && again.PlaceMapId == map.Id && again.PlaceShapeId == harbour.Id && Timelines.PlaceOf(loaded, again) != null, "…le lieu");
            t.Check(again != null && Timelines.TagsText(again) == "guerre, Nord", "…et les tags");
            t.Check(old != null && old.LinkId == abel.Id && old.More.Count == 0 && old.PlaceMapId == null && old.Tags.Count == 0, "un événement à un seul élément s'écrit et se relit comme avant");
            var json = Json.Write(TimelineJson.BuildTimeline(new TimelineInfo { Events = { new TimelineEvent { Title = "Nu", Start = new TimePoint(1) } } }));
            t.Check(json.IndexOf("\"links\"", StringComparison.Ordinal) < 0 && json.IndexOf("\"place\"", StringComparison.Ordinal) < 0 && json.IndexOf("\"tags\"", StringComparison.Ordinal) < 0, "un événement sans rien n'écrit aucune des nouvelles clés");
        }

        private static string Titles(List<TimelineEvent> events)
        {
            var titles = new List<string>();
            foreach (var entry in events) titles.Add(entry.Title);
            return string.Join(",", titles.ToArray());
        }

        // ------------------------------------------------------------ les fiches en données

        private static void SheetsAsData(Harness t, string dir)
        {
            var project = Project.CreateNew();
            project.Name = "Enerya";
            var template = project.CharacterTemplate();
            var sheets = project.Category(Project.KeySheets);
            var keira = Sheet(template, "Keira");
            keira.Document = TextDocument.FromPlainText("Elle connaît [[Abel]].\nElle dit : « non ; jamais ».");
            var abel = Sheet(template, "Abel");
            SheetTags.Add(keira, "héros");
            keira.Relations.Add(new SheetRelation { Kind = "Ami", TargetId = abel.Id });
            SheetField first = null;
            foreach (var field in template.Fields) if (FieldKinds.Normalize(field.Kind) == FieldKinds.Text && first == null) first = field;
            if (first != null) keira.FieldValues[first.Id] = "Valeur; avec \"guillemets\"";
            var category = project.TopCategories().Count > 0 ? project.TopCategories()[0] : null;
            if (category != null) { keira.CategoryId = category.Id; abel.CategoryId = category.Id; }
            Add(sheets, keira);
            Add(sheets, abel);
            project.RelinkParents();

            var chosen = Encyclopedia.DefaultOptions(project);
            chosen.ShowInfobox = false;
            chosen.IncludeLexicon = true;
            var options = SheetData.DataOptions(chosen);
            t.Check(options.ShowInfobox && options.ShowRelations && options.ShowTags && !options.IncludeLexicon && !options.IncludeMaps && !chosen.ShowInfobox, "les données emportent toujours champs, relations et tags — sans toucher aux options choisies");
            var document = Encyclopedia.Build(project, EncyclopediaSelection.All(), options, null);
            EncyclopediaSheet page = null;
            foreach (var sheet in document.AllSheets()) if (sheet.Title == "Keira") page = sheet;
            t.Check(page != null, "la fiche est dans le document");
            if (page == null) return;

            var markdown = SheetData.Markdown(page);
            t.Check(markdown.StartsWith("# Keira\n", StringComparison.Ordinal), "le Markdown s'ouvre sur le titre");
            t.Check(markdown.IndexOf("#héros", StringComparison.Ordinal) > 0, "…porte les tags");
            t.Check(markdown.IndexOf("[[Abel]]", StringComparison.Ordinal) > 0 && markdown.IndexOf("Elle connaît [[Abel]].", StringComparison.Ordinal) > 0, "…garde les [[liens]] du texte et des relations");
            t.Check(first == null || markdown.IndexOf("- **" + first.Name + "** : Valeur; avec \"guillemets\"", StringComparison.Ordinal) > 0, "…et les champs, un par ligne");

            var md = Path.Combine(dir, "md");
            var written = SheetData.WriteMarkdown(md, document);
            t.Check(written.Count == 3 && File.Exists(Path.Combine(md, SheetData.MarkdownIndex)), "l'export Markdown : un index et un fichier par fiche (" + written.Count + ")");
            var index = File.ReadAllText(Path.Combine(md, SheetData.MarkdownIndex));
            t.Check(index.IndexOf("- [Keira](", StringComparison.Ordinal) > 0 && index.IndexOf(".md)", StringComparison.Ordinal) > 0 && index.IndexOf('\\') < 0, "l'index mène à chaque fiche, par un chemin à barres obliques");
            foreach (var file in written) t.Check(File.Exists(file), "écrit : " + Path.GetFileName(file));

            t.Equal("simple", SheetData.Cell("simple"), "une cellule simple reste nue");
            t.Equal("\"a;b\"", SheetData.Cell("a;b"), "un point-virgule : entre guillemets");
            t.Equal("\"dit \"\"non\"\"\"", SheetData.Cell("dit \"non\""), "les guillemets sont doublés");
            t.Equal("\"deux\nlignes\"", SheetData.Cell("deux\r\nlignes"), "un saut de ligne : entre guillemets");
            var csv = SheetData.Csv(page.Section.Parent ?? page.Section);
            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            t.Check(lines.Length == 3 && lines[0].StartsWith("Titre;Sous-catégorie;Tags;", StringComparison.Ordinal) && lines[0].EndsWith(";Relations", StringComparison.Ordinal), "le CSV : une ligne de titres, une ligne par fiche (" + lines.Length + ")");
            string keiraLine = null;
            foreach (var line in lines) if (line.StartsWith("Keira;", StringComparison.Ordinal)) keiraLine = line;
            t.Check(keiraLine != null && keiraLine.IndexOf("héros", StringComparison.Ordinal) > 0 && keiraLine.EndsWith("Ami : Abel", StringComparison.Ordinal), "la ligne de Keira : ses tags, sa relation");
            t.Check(first == null || (keiraLine != null && keiraLine.IndexOf("\"Valeur; avec \"\"guillemets\"\"\"", StringComparison.Ordinal) > 0), "…et son champ, protégé");
            var folder = Path.Combine(dir, "csv");
            var tables = SheetData.WriteCsv(folder, document);
            t.Check(tables.Count >= 1 && File.Exists(tables[0]), "l'export CSV : un tableau par catégorie");
            var bytes = File.ReadAllBytes(tables[0]);
            t.Check(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "en UTF-8 à BOM (Excel lit les accents)");
            t.Check(EncyclopediaOptions.IsData(EncyclopediaOptions.FormatMarkdown) && EncyclopediaOptions.IsData(EncyclopediaOptions.FormatCsv) && !EncyclopediaOptions.IsData(EncyclopediaOptions.FormatHtml), "Markdown et CSV sont les deux formats de données");
        }

        // ------------------------------------------------------------ la lecture à voix haute

        private static void ReadAloud(Harness t)
        {
            t.Equal("Elle voit Keira.", ReadAloudText.Prepare("Elle voit [[Keira Larme|Keira]]."), "la notation d'un lien ne se lit pas, ses mots si");
            t.Equal("Un.\nDeux.", ReadAloudText.Prepare("  Un.  \r\n\r\n\r\n   Deux.\n"), "les lignes vides et les blancs de bord tombent");
            t.Equal("Avant.\nAprès.", ReadAloudText.Prepare("Avant.\n* * *\n———\nAprès."), "un astérisme ou un filet ne se lit pas");
            t.Equal("Il dit : « non ».", ReadAloudText.Prepare("Il dit : « non »."), "les espaces insécables deviennent des espaces");
            t.Equal("Image là.", ReadAloudText.Prepare("Image ￼là."), "une image ancrée ne se lit pas");
            t.Equal("", ReadAloudText.Prepare(null), "rien : rien");
            var longText = new StringBuilder();
            for (var i = 0; i < 200; i++) longText.Append("Une phrase assez longue pour compter. ");
            var cut = ReadAloudText.Prepare(longText.ToString(), 1000);
            t.Check(cut.Length <= 1000 && cut.Length > 500 && cut.EndsWith(".", StringComparison.Ordinal), "au-delà du plafond, la lecture s'arrête à une fin de phrase (" + cut.Length + ")");
            t.Equal("moins d'une minute", ReadAloudText.Duration("Court."), "une durée courte");
            t.Equal("environ 2 minutes", ReadAloudText.Duration(new string('a', 1800)), "≈ 15 caractères par seconde");
        }

        // ------------------------------------------------------------ le LISEZMOI

        private static void Readme(Harness t, string dir)
        {
            var project = Project.CreateNew();
            project.Name = "Secret de famille";
            var path = Path.Combine(dir, "lisezmoi.plot");
            PlotFile.Save(project, path);
            string readme = null;
            using (var archive = ZipFile.OpenRead(path))
            {
                var entry = archive.GetEntry(PlotReadme.EntryName);
                if (entry != null)
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) readme = reader.ReadToEnd();
            }
            t.Check(readme != null, "un .plot enregistré porte son LISEZMOI.txt");
            if (readme == null) return;
            t.Check(readme.IndexOf("archive ZIP", StringComparison.Ordinal) > 0 && readme.IndexOf("manifest.json", StringComparison.Ordinal) > 0 && readme.IndexOf("texts/<id>.json", StringComparison.Ordinal) > 0, "il dit ce qu'est le fichier et où sont les textes");
            t.Check(readme.IndexOf("Secret de famille", StringComparison.Ordinal) < 0, "rien du projet n'y figure");
            t.Check(readme.IndexOf(AppInfo.Version, StringComparison.Ordinal) > 0, "il dit quelle version l'a écrit");
            var loaded = PlotFile.Load(path);
            t.Equal("Secret de famille", loaded.Name, "le projet se relit comme avant");
            PlotFile.Save(loaded, path);
            var count = 0;
            using (var archive = ZipFile.OpenRead(path))
                foreach (var entry in archive.Entries) if (entry.FullName == PlotReadme.EntryName) count++;
            t.Equal(1, count, "un second enregistrement n'en ajoute pas un deuxième");
        }

        // ------------------------------------------------------------ le graphe

        private static void Graph(Harness t)
        {
            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            var mentor = new SheetField { Name = "Mentor", Kind = FieldKinds.Sheet };
            template.Fields.Add(mentor);
            var sheets = project.Category(Project.KeySheets);
            var keira = Sheet(template, "Keira");
            var abel = Sheet(template, "Abel");
            var lone = Sheet(template, "Seule");
            keira.FieldValues[mentor.Id] = abel.Id;
            lone.FieldValues[mentor.Id] = lone.Id; // une fiche ne se désigne pas elle-même
            Add(sheets, keira);
            Add(sheets, abel);
            Add(sheets, lone);
            project.RelinkParents();

            var entries = GraphInput.Capture(project);
            var graph = RelationGraph.Build(entries, new GraphOptions());
            GraphEdge field = null;
            foreach (var edge in graph.Edges) if (edge.Kind == GraphEdgeKind.Field) field = edge;
            t.Check(field != null && field.From == keira.Id && field.To == abel.Id && field.Label == "Mentor", "un champ « Fiche liée » fait une arête de la famille Champs, au nom du champ");
            t.Equal(1, graph.Edges.FindAll(delegate(GraphEdge e) { return e.Kind == GraphEdgeKind.Field; }).Count, "…et une fiche qui se désigne elle-même n'en fait pas");
            var without = RelationGraph.Build(entries, new GraphOptions { Fields = false });
            t.Equal(0, without.Edges.FindAll(delegate(GraphEdge e) { return e.Kind == GraphEdgeKind.Field; }).Count, "la famille décochée : aucune arête de champ");

            // Les forces : mêmes graine et graphe, l'écartement change la taille de la carte.
            var chain = new List<GraphEntry>();
            for (var i = 0; i < 12; i++)
            {
                var entry = new GraphEntry { Id = "n" + i, Title = "N" + i, Kind = ItemKind.Sheet };
                if (i > 0) entry.Relations.Add(new KeyValuePair<string, string>("n" + (i - 1), "lié"));
                chain.Add(entry);
            }
            var ring = RelationGraph.Build(chain, new GraphOptions());
            var normal = Extent(GraphLayout.Arrange(ring, null, 200, 7));
            var same = Extent(GraphLayout.Arrange(ring, null, 200, 7, new GraphForces()));
            var wide = Extent(GraphLayout.Arrange(ring, null, 200, 7, new GraphForces { Spacing = 2.5 }));
            var tight = Extent(GraphLayout.Arrange(ring, null, 200, 7, new GraphForces { Spacing = 0.5, Gravity = 3 }));
            t.Check(Math.Abs(normal - same) < 1e-6, "des forces à 1 rendent la disposition d'avant, au point près");
            t.Check(wide > normal * 1.4, "un écartement plus fort étale la carte (" + Math.Round(normal) + " → " + Math.Round(wide) + ")");
            t.Check(tight < normal, "moins d'écartement et plus de gravité la resserrent (" + Math.Round(tight) + ")");
            var clean = new GraphForces { Spacing = 99, Repulsion = -4, Gravity = double.NaN }.Clean();
            t.Check(clean.Spacing == GraphForces.Max && clean.Repulsion == GraphForces.Min && clean.Gravity == 1, "des forces hors bornes sont ramenées dans les bornes");
            t.Check(new GraphForces().IsDefault && !new GraphForces { Gravity = 1.5 }.IsDefault, "les forces d'office se reconnaissent");
        }

        private static double Extent(Dictionary<string, GraphPosition> positions)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var at in positions.Values)
            {
                minX = Math.Min(minX, at.X); maxX = Math.Max(maxX, at.X);
                minY = Math.Min(minY, at.Y); maxY = Math.Max(maxY, at.Y);
            }
            return Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        }

        // ------------------------------------------------------------ le tri de la bibliothèque

        private static void LibrarySorts(Harness t)
        {
            var kept = new Dictionary<string, int>(AppSettings.LibrarySorts);
            try
            {
                AppSettings.LibrarySorts.Clear();
                t.Equal(0, AppSettings.LibrarySortFor("personnages"), "sans rien de retenu : le premier tri");
                AppSettings.RememberLibrarySort("", 2);
                t.Equal(2, AppSettings.LibrarySortFor("personnages"), "le tri d'office vaut pour toute catégorie sans tri propre");
                AppSettings.RememberLibrarySort("personnages", 1);
                t.Check(AppSettings.LibrarySortFor("personnages") == 1 && AppSettings.LibrarySortFor("lieux") == 2 && AppSettings.LibrarySortFor(null) == 2, "un tri choisi sous une catégorie ne vaut que pour elle");
                AppSettings.RememberLibrarySort("lieux", -1);
                t.Equal(2, AppSettings.LibrarySortFor("lieux"), "un tri illisible n'est pas retenu");
            }
            finally
            {
                AppSettings.LibrarySorts.Clear();
                foreach (var pair in kept) AppSettings.LibrarySorts[pair.Key] = pair.Value;
            }
        }
    }
}

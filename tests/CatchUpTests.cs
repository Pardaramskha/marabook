using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Correction;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C83 — les petits rattrapages du comparatif (2.0.0, 20/11) :
    /// un fichier par écrit et son modèle de nom, EPUBCheck quand il est
    /// installé, les rétroliens d'une fiche, le filtre de frise par fiche et
    /// la liste des événements, les cartes dans l'export et l'Encyclopédie,
    /// le conjugueur et le lien vers le Wiktionnaire.</summary>
    public static class CatchUpTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C83 — rattrapages : un fichier par écrit, EPUBCheck, rétroliens, liste des événements, cartes exportées, conjugueur");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c83-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Files(t);
                EpubCheckReports(t, dir);
                BacklinksOfSheet(t);
                TimelineListing(t);
                MapsExported(t, dir);
                Conjugator(t);
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

        private static BinderItem Text(string title, string body)
        {
            return new BinderItem { Kind = ItemKind.Text, Title = title, Document = TextDocument.FromPlainText(body) };
        }

        // ------------------------------------------------------------ un fichier par écrit

        private static void Files(Harness t)
        {
            var project = Project.CreateNew();
            project.Name = "Le Serment";
            project.Author = "R. E.";
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Tome 1", Book = new BookInfo() };
            Add(writings, book);
            var part = new BinderItem { Kind = ItemKind.Folder, Title = "Première partie" };
            Add(book, part);
            var a = Text("Prologue", "Il était une fois.");
            a.Status = "done";
            Add(part, a);
            var b = Text("La mer : l'appel ?", "La mer montait.");
            Add(part, b);
            var c = Text("Prologue", "Un autre prologue.");
            Add(writings, c);
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", Document = TextDocument.FromPlainText("Une fiche.") };
            Add(project.Category(Project.KeySheets), sheet);
            var ids = new[] { a.Id, b.Id, c.Id, sheet.Id };
            var today = new DateTime(2026, 11, 20);

            t.Equal("{n} - {titre}", ExportFiles.DefaultPattern, "le modèle de la maison : « {n} - {titre} »");
            t.Equal(ExportFiles.DefaultPattern, ExportFiles.Normalize("   "), "un modèle vide redevient celui de la maison");
            t.Equal("{n} - {livre}", ExportFiles.Normalize("{livre}"), "un modèle sans numéro ni titre reçoit le numéro : deux fichiers ne porteront pas le même nom");
            t.Equal("{titre} ({statut})", ExportFiles.Normalize("{titre} ({statut})"), "un modèle qui porte le titre est gardé tel quel");
            t.Equal("La mer - l'appel _", ExportFiles.Safe("La mer : l'appel ?"), "les caractères interdits dans un nom de fichier sont remplacés");
            t.Equal("document", ExportFiles.Safe(" .. "), "un nom vidé par le nettoyage devient « document »");
            t.Equal("01 - Prologue", ExportFiles.NameOf("{n} - {titre}", a, 1, 4, project, null, today), "« {n} - {titre} » : 01 - Prologue");
            t.Equal("007 - Prologue", ExportFiles.NameOf("{n} - {titre}", a, 7, 120, project, null, today), "le numéro prend autant de chiffres que le compte en demande");
            t.Equal("Tome 1 - Première partie - Prologue (Terminé)", ExportFiles.NameOf("{livre} - {dossier} - {titre} ({statut})", a, 1, 4, project, null, today), "livre, dossier et statut se remplacent");
            t.Equal("R. E. - Le Serment - 2026-11-20 - Prologue", ExportFiles.NameOf("{auteur} - {projet} - {date} - {titre}", a, 1, 4, project, null, today), "auteur, projet et date se remplacent");
            t.Equal("03 - Prologue", ExportFiles.NameOf("{n} - {livre} - {titre}", c, 3, 4, project, null, today), "un jeton vide (pas de livre) ne laisse pas de tiret orphelin");
            t.Equal("Keira", ExportFiles.NameOf("{TITRE} ({statut})", sheet, 4, 4, project, null, today), "une fiche n'a pas de statut ; les jetons se lisent sans égard à la casse");

            var preset = ExportPresets.Find(ExportPresets.BuiltIn(), ExportPresets.WordA4).Clone();
            t.Check(!preset.OneFilePerText, "aucun préréglage de la maison n'écrit un fichier par écrit");
            var plan = ExportPlan.Build(project, ids, preset, null);
            t.Check(!ExportFiles.Applies(preset, plan), "sans l'option, un seul fichier");
            var before = preset.Signature();
            preset.OneFilePerText = true;
            t.Check(ExportFiles.Applies(preset, plan), "l'option cochée et plusieurs éléments : plusieurs fichiers");
            t.Check(preset.Signature() != before, "l'option change l'empreinte du préréglage (« modifié »)");
            var withPattern = preset.Signature();
            preset.FileNamePattern = "{titre}";
            t.Check(preset.Signature() != withPattern, "…et le modèle de nom aussi");
            t.Check(!ExportFiles.Applies(preset, ExportPlan.Build(project, new[] { a.Id }, preset, null)), "un seul élément : un seul fichier, au nom choisi");

            var files = ExportFiles.Split(project, plan, preset, new ExportMeta { Title = "Tome 1", Author = "R. E." }, today);
            t.Equal(4, files.Count, "un fichier par élément : trois écrits et une fiche");
            t.Equal("Prologue.docx", files[0].Name, "le premier fichier porte le titre de son écrit et l'extension du format");
            t.Equal("La mer - l'appel _.docx", files[1].Name, "un titre à caractères interdits donne un nom valide");
            t.Equal("Prologue (2).docx", files[2].Name, "deux noms égaux se départagent : « (2) »");
            t.Equal("Keira.docx", files[3].Name, "les fiches suivent les écrits");
            t.Check(files[0].Plan.Texts.Count == 1 && files[0].Plan.Count == 1 && files[0].Plan.Texts[0] == a, "chaque fichier a un plan qui ne porte que son élément");
            t.Check(files[3].Plan.Sheets.Count == 1 && files[3].Plan.Texts.Count == 0, "…la fiche aussi");
            t.Equal("Prologue", files[0].Meta.Title, "le titre du fichier est celui de l'élément");
            t.Equal("R. E.", files[0].Meta.Author, "…l'auteur·ice reste celui de l'export");
            preset.FileNamePattern = "{n} - {titre}";
            t.Equal("01 - Prologue.md", ExportFiles.Split(project, plan, WithFormat(preset, ExportFormats.Markdown), null, today)[0].Name, "l'extension suit le format");
            t.Equal("01 - Prologue.docx, 02 - La mer - l'appel _.docx…", ExportFiles.Sample(project, plan, preset, null, today, 2), "l'exemple du gestionnaire montre les premiers noms");

            var folder = Path.Combine(Path.GetTempPath(), "marabook-tests-c83-files-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var again = ExportFiles.Split(project, plan, preset, null, today);
                t.Equal(0, ExportFiles.Existing(folder, again).Count, "dossier vide : rien à remplacer");
                File.WriteAllText(Path.Combine(folder, again[1].Name), "x");
                t.Equal(1, ExportFiles.Existing(folder, again).Count, "un fichier déjà là est signalé avant d'être remplacé");
            }
            finally { try { Directory.Delete(folder, true); } catch { } }

            var json = preset.ToJson();
            var back = ExportPreset.FromJson(Json.Parse(Json.Write(json)));
            t.Check(back.OneFilePerText && back.FileNamePattern == "{n} - {titre}", "l'option et le modèle font l'aller-retour d'un préréglage enregistré");
            var old = ExportPreset.FromJson(Json.Parse("{\"id\":\"x\",\"name\":\"Ancien\",\"format\":\"docx\"}"));
            t.Check(!old.OneFilePerText && old.FileNamePattern == ExportFiles.DefaultPattern, "un préréglage d'avant : un seul fichier, le modèle de la maison");
        }

        private static ExportPreset WithFormat(ExportPreset preset, string format)
        {
            var clone = preset.Clone();
            clone.Format = format;
            return clone;
        }

        // ------------------------------------------------------------ EPUBCheck

        private const string SampleReport = "{\"checker\":{\"path\":\"x.epub\",\"checkerVersion\":\"5.1.0\",\"nFatal\":0,\"nError\":1,\"nWarning\":1,\"nUsage\":1},"
            + "\"messages\":["
            + "{\"ID\":\"RSC-005\",\"severity\":\"ERROR\",\"message\":\"Error while parsing file: element \\\"foo\\\" not allowed here\",\"locations\":[{\"path\":\"OEBPS/ch1.xhtml\",\"line\":12,\"column\":7},{\"path\":\"OEBPS/ch2.xhtml\",\"line\":3,\"column\":1}]},"
            + "{\"ID\":\"OPF-003\",\"severity\":\"WARNING\",\"message\":\"Item \\\"x.png\\\" exists in the EPUB, but is not declared in the OPF manifest.\",\"locations\":[{\"path\":\"x.epub\",\"line\":-1,\"column\":-1}]},"
            + "{\"ID\":\"HTM-010\",\"severity\":\"USAGE\",\"message\":\"Namespace uri found.\",\"locations\":[]}"
            + "]}";

        private static void EpubCheckReports(Harness t, string dir)
        {
            var report = EpubCheckTool.Parse(SampleReport);
            t.Check(report.Ran && report.Version == "5.1.0", "le rapport d'EPUBCheck se lit : il a tourné, version 5.1.0");
            t.Equal(1, report.Errors.Count, "une erreur relevée");
            t.Equal(1, report.Warnings.Count, "un avertissement relevé ; les remarques d'usage sont tues");
            t.Check(!report.Valid, "une erreur : l'EPUB n'est pas valide");
            t.Check(report.Errors[0].StartsWith("RSC-005 — ") && report.Errors[0].Contains("OEBPS/ch1.xhtml, ligne 12") && report.Errors[0].Contains("et 1 autre endroit"), "l'erreur dit son code, son fichier, sa ligne et les autres endroits (" + report.Errors[0] + ")");
            t.Check(report.Warnings[0].Contains("OPF-003") && !report.Warnings[0].Contains("ligne"), "sans numéro de ligne, la ligne n'est pas dite");
            var clean = EpubCheckTool.Parse("{\"checker\":{\"checkerVersion\":\"5.2.1\"},\"messages\":[]}");
            t.Check(clean.Ran && clean.Valid && clean.Errors.Count == 0, "un rapport sans message : EPUB valide");
            var broken = EpubCheckTool.Parse("pas du json");
            t.Check(!broken.Ran && !broken.Valid && broken.Failure.Length > 0, "un rapport illisible ne vaut pas validation");
            t.Check(!EpubCheckTool.Run(null, "x.epub").Ran, "sans outil, rien ne tourne — et rien ne lève");

            // La recherche de l'outil, sur de faux dossiers.
            var windows = Path.DirectorySeparatorChar == '\\';
            var bin = Path.Combine(dir, "bin");
            var javaBin = Path.Combine(dir, "java");
            var tools = Path.Combine(dir, "outils");
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(javaBin);
            Directory.CreateDirectory(Path.Combine(tools, "epubcheck-5.0.0"));
            Directory.CreateDirectory(Path.Combine(tools, "epubcheck-5.1.0"));
            var none = new List<string>();
            t.Check(EpubCheckTool.Search(null, new List<string> { bin }, new List<string> { tools }) == null, "ni lanceur ni Java : pas d'EPUBCheck");
            var jarOld = Path.Combine(tools, "epubcheck-5.0.0", "epubcheck.jar");
            var jarNew = Path.Combine(tools, "epubcheck-5.1.0", "epubcheck.jar");
            File.WriteAllText(jarOld, "");
            File.WriteAllText(jarNew, "");
            t.Check(EpubCheckTool.Search(null, new List<string> { bin }, new List<string> { tools }) == null, "un .jar sans Java ne sert à rien");
            var java = Path.Combine(javaBin, windows ? "java.exe" : "java");
            File.WriteAllText(java, "");
            var found = EpubCheckTool.Search(null, new List<string> { bin, javaBin }, new List<string> { tools });
            t.Check(found != null && found.FileName == java && found.Jar == jarNew, "Java et un dossier « epubcheck-… » : le .jar le plus récent, lancé par Java");
            t.Check(EpubCheckTool.Search(jarOld, new List<string> { javaBin }, none).Jar == jarOld, "la variable EPUBCHECK désigne le .jar à employer");
            var launcher = Path.Combine(bin, windows ? "epubcheck.cmd" : "epubcheck");
            File.WriteAllText(launcher, "");
            var byPath = EpubCheckTool.Search(null, new List<string> { bin, javaBin }, new List<string> { tools });
            t.Check(byPath != null && byPath.FileName == launcher && byPath.Jar == null, "le lanceur « epubcheck » du PATH passe avant un .jar");

            // L'outil réel, s'il est sur cette machine : l'EPUB de Marabook doit le passer.
            EpubCheckTool.Forget();
            var real = EpubCheckTool.Find();
            if (real == null) { t.Info("EPUBCheck n'est pas installé ici : la validation réelle n'est pas jouée."); return; }
            foreach (var kindle in new[] { false, true })
            {
                var w = EpubProject();
                var preset = ExportPresets.Find(ExportPresets.BuiltIn(), kindle ? ExportPresets.EpubKindle : ExportPresets.Epub).Clone();
                preset.TitlePage = true;
                preset.NumberChapters = true;
                preset.DropCaps = true;
                preset.OpeningSmallCaps = true;
                preset.SplitFolders = true;
                var meta = new ExportMeta { Title = "Le Serment", Author = "R. E.", Language = "fr", Publisher = "Maison", Year = "2026" };
                var ids = new List<string>();
                foreach (var item in ExportPlan.ReadingOrder(w)) if (item.Kind == ItemKind.Text) ids.Add(item.Id);
                var plan = ExportPlan.Build(w, ids, preset, meta);
                EpubOptions options;
                var epub = ExportFlow.EpubFor(w, plan, preset, meta, out options);
                var path = Path.Combine(dir, kindle ? "kindle.epub" : "livre.epub");
                Epub.Write(path, w, epub, options);
                var verdict = EpubCheckTool.Run(real, path);
                t.Check(verdict.Ran, "EPUBCheck a tourné sur l'EPUB" + (kindle ? " Kindle" : "") + " (" + verdict.Failure + ")");
                t.Check(verdict.Errors.Count == 0, "EPUBCheck " + verdict.Version + " valide l'EPUB" + (kindle ? " Kindle" : "") + " de Marabook" + (verdict.Errors.Count > 0 ? " — " + string.Join(" | ", verdict.Errors.ToArray()) : ""));
                if (verdict.Warnings.Count > 0) t.Info("EPUBCheck, avertissements" + (kindle ? " (Kindle)" : "") + " : " + string.Join(" | ", verdict.Warnings.ToArray()));
            }
        }

        private static Project EpubProject()
        {
            var project = Project.CreateNew();
            project.Name = "Le Serment";
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Le Serment", Book = new BookInfo() };
            Add(writings, book);
            var part = new BinderItem { Kind = ItemKind.Folder, Title = "Première partie" };
            Add(book, part);
            Add(part, Text("L'appel", "Pendant des années, la mer resta calme. Puis vint l'orage.\n— Écoute ! cria le vieux marabout.\n« Œuvre & cœur », disait-il."));
            Add(part, Text("Le départ", "Elle partit à l'aube. Personne ne la vit."));
            var second = new BinderItem { Kind = ItemKind.Folder, Title = "Deuxième partie" };
            Add(book, second);
            Add(second, Text("Le retour", "Dix ans plus tard, elle revint."));
            return project;
        }

        // ------------------------------------------------------------ les rétroliens

        private static BinderItem Sheet(SheetTemplate template, string title)
        {
            return new BinderItem { Kind = ItemKind.Sheet, Title = title, TemplateId = template.Id, Document = new TextDocument() };
        }

        private static void BacklinksOfSheet(Harness t)
        {
            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            var mentor = new SheetField { Name = "Mentor", Kind = FieldKinds.Sheet };
            template.Fields.Add(mentor);
            var sheets = project.Category(Project.KeySheets);
            var keira = Sheet(template, "Keira Varenh");
            var abel = Sheet(template, "Abel");
            var mira = Sheet(template, "Mira");
            var lone = Sheet(template, "Solitaire");
            var gone = Sheet(template, "Jetée");
            foreach (var sheet in new[] { keira, abel, mira, lone }) Add(sheets, sheet);
            Add(project.Trash, gone);
            abel.Relations.Add(new SheetRelation { Kind = "Mère", TargetId = keira.Id });
            keira.Relations.Add(new SheetRelation { Kind = "Fils", TargetId = abel.Id });
            mira.FieldValues[mentor.Id] = keira.Id;
            mira.Document = TextDocument.FromPlainText("Elle doit tout à [[keira varenh|sa maîtresse]].");
            gone.Document = TextDocument.FromPlainText("[[Keira Varenh]] encore.");
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            var cited = Text("Chapitre 1", "Alors [[Keira Varenh]] entra.");
            var silent = Text("Chapitre 2", "Keira Varenh est nommée, sans lien.");
            Add(writings, cited);
            Add(writings, silent);
            var timeline = new BinderItem { Kind = ItemKind.Timeline, Title = "Histoire", Timeline = new TimelineInfo { Types = TimelineInfo.DefaultTypes() } };
            Add(project.Category(Project.KeyTimelines), timeline);
            var assigned = new TimelineEvent { Title = "Le couronnement", Start = new TimePoint(12, 3, 1), LinkKind = TimelineLinks.Sheet, LinkId = keira.Id };
            var mentioned = new TimelineEvent { Title = "La bataille", Description = "Où [[Keira Varenh]] perd un bras.", Start = new TimePoint(14, 1, 1) };
            var other = new TimelineEvent { Title = "Une éclipse", Start = new TimePoint(15, 1, 1), LinkKind = TimelineLinks.Sheet, LinkId = abel.Id };
            timeline.Timeline.Events.AddRange(new[] { assigned, mentioned, other });
            var map = new BinderItem { Kind = ItemKind.Map, Title = "Ehnmir", Map = new MapInfo { Width = 100, Height = 100, Levels = 1 } };
            Add(project.Category(Project.KeyMaps), map);
            map.Map.Markers.Add(new MapMarker { Name = "Tour de Keira", LinkId = keira.Id, X = 10, Y = 10 });
            map.Map.Zones.Add(new MapZone { Name = "Ailleurs", LinkId = abel.Id });
            project.RelinkParents();

            var links = Backlinks.Of(project, keira);
            var bySheet = Backlinks.In(links, Backlink.Sheets);
            t.Equal(2, bySheet.Count, "deux fiches citent Keira : Abel (relation) et Mira (champ et lien) — pas la Corbeille, pas elle-même");
            t.Check(bySheet[0].Item == abel && bySheet[0].Reason.StartsWith("relation"), "Abel la cite par une relation (" + bySheet[0].Reason + ")");
            t.Check(bySheet[1].Item == mira && bySheet[1].Reasons.Count == 2 && bySheet[1].Reason.Contains("champ « Mentor »") && bySheet[1].Reason.Contains(Backlinks.ByLink), "Mira la cite deux fois, sur une seule ligne : le champ « Mentor » et un lien (" + bySheet[1].Reason + ")");
            var byText = Backlinks.In(links, Backlink.Texts);
            t.Check(byText.Count == 1 && byText[0].Item == cited, "un écrit la cite par un [[lien]] ; la nommer sans lien ne suffit pas (c'est le Suivi qui compte les noms)");
            var byEvent = Backlinks.In(links, Backlink.Events);
            t.Equal(2, byEvent.Count, "deux événements la concernent");
            t.Check(byEvent[0].Label == "La bataille" && byEvent[0].RefId == mentioned.Id && byEvent[0].Item == timeline && byEvent[0].Reason.Contains(Backlinks.ByLink), "…l'un par un lien dans sa description, avec l'événement à ouvrir");
            t.Check(byEvent[1].Label == "Le couronnement" && byEvent[1].Reason.StartsWith("assignée") && byEvent[1].Reason.Contains("Histoire"), "…l'autre parce qu'elle lui est assignée ; la frise est dite");
            var byMap = Backlinks.In(links, Backlink.Maps);
            t.Check(byMap.Count == 1 && byMap[0].Item == map && byMap[0].Reason == "repère « Tour de Keira »" && byMap[0].RefId == map.Map.Markers[0].Id, "un repère de carte la désigne");
            var byTree = Backlinks.In(links, Backlink.Trees);
            t.Check(byTree.Count == 1 && byTree[0].Item == abel && byTree[0].Label == "Arbre de Abel", "elle figure dans l'arbre d'Abel");
            t.Equal("2 fiches, 1 écrit, 2 événements, 1 carte, 1 arbre", Backlinks.Summary(links), "le résumé compte par genre");
            t.Equal(0, Backlinks.Of(project, lone).Count, "une fiche que rien ne cite n'a pas de rétrolien");
            t.Equal(0, Backlinks.Of(project, null).Count, "sans fiche, rien");
            // Les groupes sortent dans l'ordre convenu.
            var order = new List<string>();
            foreach (var link in links) if (order.Count == 0 || order[order.Count - 1] != link.Group) order.Add(link.Group);
            t.Equal("sheet,text,event,map,tree", string.Join(",", order.ToArray()), "fiches, écrits, événements, cartes, arbres : dans cet ordre");
        }

        // ------------------------------------------------------------ frises : filtre par fiche, liste

        private static void TimelineListing(Harness t)
        {
            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            var sheets = project.Category(Project.KeySheets);
            var keira = Sheet(template, "Keira");
            var abel = Sheet(template, "Abel");
            var gone = Sheet(template, "Jetée");
            Add(sheets, keira);
            Add(sheets, abel);
            Add(project.Trash, gone);
            var text = Text("Chapitre 1", "…");
            var writings = project.Category(Project.KeyWritings);
            Add(writings, text);
            var timeline = new BinderItem { Kind = ItemKind.Timeline, Title = "Histoire", Timeline = new TimelineInfo { Types = TimelineInfo.DefaultTypes() } };
            Add(project.Category(Project.KeyTimelines), timeline);
            project.RelinkParents();
            var system = Timelines.SystemOf(project, timeline);
            var type = timeline.Timeline.Types[0];
            var late = new TimelineEvent { Title = "Le sacre", Start = new TimePoint(30, 6, 1), LinkKind = TimelineLinks.Sheet, LinkId = keira.Id, TypeId = type.Id };
            var early = new TimelineEvent { Title = "La bataille des Gués", Description = "[[Keira]] y perd un bras ; [[Abel]] y naît.", Start = new TimePoint(10, 1, 1) };
            var span = new TimelineEvent { Title = "Règne d'Abel", Start = new TimePoint(10, 1, 1), End = new TimePoint(40, 1, 1), LinkKind = TimelineLinks.Sheet, LinkId = abel.Id };
            var scene = new TimelineEvent { Title = "Scène d'ouverture", Start = new TimePoint(20, 1, 1), LinkKind = TimelineLinks.Text, LinkId = text.Id };
            var trashed = new TimelineEvent { Title = "Oubliée", Start = new TimePoint(21, 1, 1), LinkKind = TimelineLinks.Sheet, LinkId = gone.Id };
            var undated = new TimelineEvent { Title = "Sans date", Start = null };
            var events = new List<TimelineEvent> { late, early, span, scene, trashed, undated };

            t.Check(Timelines.Concerns(late, keira), "un événement concerne la fiche qui lui est assignée");
            t.Check(Timelines.Concerns(early, keira) && Timelines.Concerns(early, abel), "…et celles que son texte cite par un [[lien]]");
            t.Check(!Timelines.Concerns(scene, keira) && !Timelines.Concerns(span, keira), "…pas les autres");
            t.Check(!Timelines.Concerns(null, keira) && !Timelines.Concerns(late, null), "sans événement ou sans fiche : non");

            var counts = Timelines.SheetsConcerned(project, events);
            t.Check(counts.Count == 2 && counts[0].Sheet == abel && counts[0].Count == 2 && counts[1].Sheet == keira && counts[1].Count == 2,
                "le menu du filtre : Abel (2) puis Keira (2), par ordre de titre — ni l'écrit assigné, ni la fiche à la Corbeille");

            var ordered = Timelines.InOrder(system, events);
            t.Equal(5, ordered.Count, "la liste ne garde que les événements datés");
            t.Equal("Règne d'Abel,La bataille des Gués,Scène d'ouverture,Oubliée,Le sacre", Titles(ordered), "dans l'ordre du temps ; une durée qui commence l'année d'un événement daté au jour le précède");

            t.Equal(5, Timelines.Search(project, system, timeline.Timeline, ordered, "  ").Count, "une recherche vide garde tout");
            t.Equal("La bataille des Gués", Titles(Timelines.Search(project, system, timeline.Timeline, ordered, "GUES")), "la recherche ignore la casse et les accents");
            t.Equal("La bataille des Gués", Titles(Timelines.Search(project, system, timeline.Timeline, ordered, "bras")), "…lit la description");
            t.Equal("La bataille des Gués,Le sacre", Titles(Timelines.Search(project, system, timeline.Timeline, ordered, "keira")), "…le texte des [[liens]] et le titre de l'élément assigné");
            t.Equal("Le sacre", Titles(Timelines.Search(project, system, timeline.Timeline, ordered, Fold(type.Name))), "…et le nom du type");
            t.Equal("Scène d'ouverture", Titles(Timelines.Search(project, system, timeline.Timeline, ordered, "chapitre scene")), "plusieurs mots : tous doivent s'y trouver");
            t.Equal("", Titles(Timelines.Search(project, system, timeline.Timeline, ordered, "dragon")), "rien ne répond : liste vide");
            t.Check(Timelines.LinkedOf(project, scene) == text && Timelines.LinkedOf(project, early) == null, "l'élément assigné se retrouve, ou null");
        }

        private static string Fold(string text) { return TutorialParser.Fold(text); }

        private static string Titles(List<TimelineEvent> events)
        {
            var titles = new List<string>();
            foreach (var entry in events) titles.Add(entry.Title);
            return string.Join(",", titles.ToArray());
        }

        // ------------------------------------------------------------ les cartes exportées

        private static void MapsExported(Harness t, string dir)
        {
            var project = Project.CreateNew();
            project.Name = "Enerya";
            var template = project.CharacterTemplate();
            var port = Sheet(template, "Port-Gris");
            port.Document = TextDocument.FromPlainText("Un port.");
            Add(project.Category(Project.KeySheets), port);
            var maps = project.Category(Project.KeyMaps);
            var drawn = new BinderItem { Kind = ItemKind.Map, Title = "Ehnmir", Map = new MapInfo { Width = 400, Height = 200, Levels = 1 } };
            drawn.Map.Markers.Add(new MapMarker { Name = "Port-Gris", LinkId = port.Id, X = 10, Y = 10 });
            drawn.Map.Markers.Add(new MapMarker { Name = "", X = 20, Y = 20 });
            drawn.Map.Zones.Add(new MapZone { Name = "Les Marches" });
            var blank = new BinderItem { Kind = ItemKind.Map, Title = "Sans image", Map = new MapInfo() };
            var secret = new BinderItem { Kind = ItemKind.Map, Title = "Carte secrète", Map = new MapInfo { Width = 100, Height = 100, Levels = 1 } };
            secret.Tags.Add("spoiler");
            Add(maps, drawn);
            Add(maps, blank);
            Add(maps, secret);
            project.RelinkParents();

            // L'ampleur de l'export : une carte part en image dans un PDF, et là seulement.
            t.Check(ExportFormats.Carries(ExportFormats.Pdf, ItemKind.Map), "un PDF porte les cartes");
            t.Check(!ExportFormats.Carries(ExportFormats.Docx, ItemKind.Map) && !ExportFormats.Carries(ExportFormats.Epub, ItemKind.Map), "Word et EPUB non");
            var pdf = ExportPresets.Find(ExportPresets.BuiltIn(), ExportPresets.PdfA4).Clone();
            var ids = new[] { drawn.Id, blank.Id, secret.Id, port.Id };
            var plan = ExportPlan.Build(project, ids, pdf, null);
            t.Check(plan.Maps.Count == 2 && plan.Maps[0] == drawn, "les cartes qui ont une image entrent dans le plan ; une carte sans image n'a rien à montrer");
            t.Equal("1 fiche, 2 cartes", plan.Summary(), "le résumé les compte");
            t.Equal(1, ExportPlan.Build(project, ids, pdf, null, new ExportFilter { ExceptTag = "spoiler" }).Maps.Count, "« sauf ce tag » écarte une carte comme le reste");
            var word = ExportPlan.Build(project, ids, ExportPresets.Find(ExportPresets.BuiltIn(), ExportPresets.WordA4).Clone(), null);
            t.Check(word.Maps.Count == 0 && word.Skipped.Count == 2, "dans Word, les cartes sont écartées et dites");
            t.Equal("Ehnmir", ExportPlan.Build(project, new[] { drawn.Id }, pdf, null).DefaultName(project, null), "une carte seule donne son nom au fichier");
            var segments = ExportFlow.Build(project, plan, pdf, new ExportMeta());
            var images = 0;
            foreach (var segment in segments) if (segment.IsImage) images++;
            t.Equal(2, images, "chaque carte est une page-image du PDF");
            t.Check(ExportFilter.TagsOf(project).Contains("spoiler"), "les tags des cartes sont proposés au filtre");
            var one = pdf.Clone();
            one.OneFilePerText = true;
            t.Equal(3, ExportFiles.Split(project, plan, one, null, DateTime.Today).Count, "un fichier par élément : les cartes aussi");

            // L'Encyclopédie : une page « Cartes », dessinée par l'application.
            var renderer = Encyclopedia.MapRenderer;
            try
            {
                var asked = new List<string>();
                Encyclopedia.MapRenderer = delegate(Project p, BinderItem item) { asked.Add(item.Title); return item == secret ? null : new byte[] { 0x89, 0x50, 0x4E, 0x47 }; };
                var document = Encyclopedia.Build(project, EncyclopediaSelection.All(), new EncyclopediaOptions());
                t.Check(document.Maps.Count == 2 && document.Maps[0].Title == "Ehnmir", "l'Encyclopédie retient les cartes qui ont une image");
                t.Check(document.Maps[0].Places.Count == 2 && document.Maps[0].Places[0].Name == "Les Marches" && document.Maps[0].Places[0].Zone && document.Maps[0].Places[1].SheetId == port.Id,
                    "ses lieux nommés, triés ; un repère lié à une fiche exportée garde son lien");
                var folder = Path.Combine(dir, "encyclopedie");
                var written = HtmlEncyclopedia.Write(folder, document, project);
                t.Equal("Ehnmir,Carte secrète", string.Join(",", asked.ToArray()), "chaque carte est demandée une fois à l'application");
                t.Check(File.Exists(Path.Combine(folder, HtmlEncyclopedia.ImagesFolder, "carte-1.png")) && !File.Exists(Path.Combine(folder, HtmlEncyclopedia.ImagesFolder, "carte-2.png")), "l'image dessinée est écrite ; une carte que l'application n'a pas pu dessiner est laissée");
                var page = File.ReadAllText(Path.Combine(folder, HtmlEncyclopedia.MapsFile), Encoding.UTF8);
                t.Check(page.Contains("<h2>Ehnmir</h2>") && page.Contains("images/carte-1.png") && !page.Contains("Carte secrète"), "la page « Cartes » montre la carte dessinée, pas l'autre");
                t.Check(page.Contains("\">Port-Gris</a>") && page.Contains("<li>Les Marches</li>"), "un lieu lié mène à sa fiche ; un lieu sans fiche reste du texte");
                var index = File.ReadAllText(Path.Combine(folder, HtmlEncyclopedia.IndexFile), Encoding.UTF8);
                t.Check(index.Contains("href=\"cartes.html\">Cartes</a> (1 carte)"), "l'accueil mène aux cartes et les compte");
                t.Check(written.Contains(Path.Combine(folder, HtmlEncyclopedia.MapsFile)), "la page est rendue parmi les fichiers écrits");

                var except = Encyclopedia.Build(project, EncyclopediaSelection.All(), new EncyclopediaOptions { ExceptTag = "spoiler" });
                t.Equal(1, except.Maps.Count, "« sauf ce tag » écarte la carte secrète de l'Encyclopédie");
                var without = Encyclopedia.Build(project, EncyclopediaSelection.All(), new EncyclopediaOptions { IncludeMaps = false });
                t.Equal(0, without.Maps.Count, "l'option décochée : pas de cartes");
                var folder2 = Path.Combine(dir, "encyclopedie-sans");
                HtmlEncyclopedia.Write(folder2, without, project);
                t.Check(!File.Exists(Path.Combine(folder2, HtmlEncyclopedia.MapsFile)) && !File.ReadAllText(Path.Combine(folder2, HtmlEncyclopedia.IndexFile), Encoding.UTF8).Contains("cartes.html"), "…ni page, ni lien");
                Encyclopedia.MapRenderer = null;
                var folder3 = Path.Combine(dir, "encyclopedie-coeur");
                HtmlEncyclopedia.Write(folder3, Encyclopedia.Build(project, EncyclopediaSelection.All(), new EncyclopediaOptions()), project);
                t.Check(!File.Exists(Path.Combine(folder3, HtmlEncyclopedia.MapsFile)), "sans dessinateur (le cœur seul), pas de page « Cartes »");
            }
            finally { Encyclopedia.MapRenderer = renderer; }

            t.Check(new EncyclopediaOptions().Signature() != new EncyclopediaOptions { IncludeMaps = false }.Signature(), "l'option compte dans l'empreinte des réglages");
            project.Encyclopedia = new EncyclopediaOptions { IncludeMaps = false };
            var path = Path.Combine(dir, "cartes.plot");
            PlotFile.Save(project, path);
            t.Check(!PlotFile.Load(path).Encyclopedia.IncludeMaps, "le .plot garde « sans les cartes »");
            project.Encyclopedia = new EncyclopediaOptions();
            PlotFile.Save(project, path);
            t.Check(PlotFile.Load(path).Encyclopedia.IncludeMaps, "…et les exporte d'office");
        }

        // ------------------------------------------------------------ le conjugueur, le Wiktionnaire

        private static void Conjugator(Harness t)
        {
            var json = "{\"id\":7,\"verb\":\"lever\",\"info\":\"1ᵉʳ groupe · transitif (+ usage pronominal)\",\"lemmas\":[\"lever\",\"lever\"],\"pronominal\":0,\"usedPronominal\":true,"
                + "\"table\":{\"t_infi\":\"Infinitif\",\"infi\":\"se lever\",\"t_ppre\":\"Participe présent\",\"ppre\":\"se levant\",\"t_ppas\":\"Participes passés\",\"ppas1\":\"levé\",\"ppas2\":\"levée\",\"ppas3\":\"levés\",\"ppas4\":\"levées\","
                + "\"t_imp\":\"Impératif\",\"t_impe\":\"Présent\",\"impe1\":\"lève-toi\",\"impe2\":\"levons-nous\",\"impe3\":\"levez-vous\","
                + "\"t_indi\":\"Indicatif\",\"t_ipre\":\"Présent\",\"ipre1\":\"je me lève\",\"ipre2\":\"tu te lèves\",\"ipre3\":\"il se lève\",\"ipre4\":\"nous nous levons\",\"ipre5\":\"vous vous levez\",\"ipre6\":\"ils se lèvent\","
                + "\"t_iimp\":\"Imparfait\",\"iimp1\":\"je me levais\",\"iimp2\":\"\",\"iimp3\":\"il se levait\",\"iimp4\":\"\",\"iimp5\":\"\",\"iimp6\":\"\","
                + "\"t_cond\":\"Conditionnel\",\"t_conda\":\"Présent\",\"conda1\":\"je me lèverais\",\"t_condb\":\"\",\"condb1\":\"\","
                + "\"t_subj\":\"Subjonctif\",\"t_spre\":\"Présent\",\"spre1\":\"que je me lève\"}}";
            var verb = Conjugation.Parse(Json.Parse(json));
            t.Check(verb.Found && verb.Verb == "lever" && verb.UsedPronominal && verb.CanChoosePronominal, "le tableau du pont se lit : « lever », à la forme pronominale, qu'on peut quitter");
            t.Equal(1, verb.Lemmas.Count, "les lemmes sont dédoublonnés");
            t.Equal("Indicatif,Conditionnel,Subjonctif,Impératif,Infinitif et participes", MoodTitles(verb), "les modes, dans l'ordre du tableau");
            t.Check(verb.Moods[0].Tenses.Count == 2 && verb.Moods[0].Tenses[0].Title == "Présent" && verb.Moods[0].Tenses[0].Forms.Count == 6 && verb.Moods[0].Tenses[0].Forms[2] == "il se lève", "l'indicatif présent a ses six personnes");
            t.Equal(2, verb.Moods[0].Tenses[1].Forms.Count, "un verbe défectif ne montre que les formes qui existent");
            t.Equal(1, verb.Moods[1].Tenses.Count, "un temps sans forme (la 2ᵉ forme du conditionnel hors temps composés) n'est pas montré");
            t.Equal(3, verb.Moods[4].Tenses.Count, "infinitif, participe présent, participes passés");
            t.Check(verb.ToPlainText().StartsWith("lever — 1ᵉʳ groupe") && verb.ToPlainText().Contains("INDICATIF\nPrésent : je me lève, tu te lèves"), "le tableau se copie en texte");
            var unknown = Conjugation.Parse(Json.Parse("{\"id\":8,\"verb\":\"\",\"lemmas\":[],\"pronominal\":-1,\"table\":{}}"));
            t.Check(!unknown.Found && !unknown.CanChoosePronominal, "un mot qui n'est pas un verbe : pas de tableau");
            t.Check(!Conjugation.Parse(null).Found, "une réponse vide non plus");
            var odd = Conjugation.Parse(Json.Parse("{\"verb\":\"tabler\",\"info\":\"1ᵉʳ groupe · # erreur - code : 1___x___a\",\"pronominal\":1,\"table\":{\"t_indi\":\"Indicatif\",\"t_ipre\":\"Présent\",\"ipre1\":\"je table\"}}"));
            t.Equal("1ᵉʳ groupe", odd.Info, "une étiquette que Grammalecte ne sait pas lire est tue, le groupe reste");
            t.Check(!odd.CanChoosePronominal, "un verbe toujours pronominal ne laisse pas le choix");
            var options = new ConjugationOptions { Negative = true, Feminine = true }.ToJson();
            t.Check((bool)options["negative"] && (bool)options["feminine"] && !(bool)options["compound"] && options.Count == 5, "les options partent au pont sous leurs cinq clés");

            t.Equal("https://fr.wiktionary.org/wiki/marabout", Wiktionary.UrlFor(" marabout "), "le Wiktionnaire : la page du mot");
            t.Equal("https://fr.wiktionary.org/wiki/%C5%93uvre", Wiktionary.UrlFor("œuvre"), "…les lettres accentuées et les ligatures sont encodées");
            t.Equal("https://fr.wiktionary.org/wiki/aujourd%27hui", Wiktionary.UrlFor("aujourd’hui"), "…l'apostrophe typographique redevient droite");
            t.Equal("https://fr.wiktionary.org/wiki/silence", Wiktionary.UrlFor("SILENCE"), "…un mot tout en capitales est cherché en minuscules");
            t.Equal("https://fr.wiktionary.org/wiki/Keira", Wiktionary.UrlFor("Keira"), "…un nom propre garde sa majuscule");
            t.Check(Wiktionary.UrlFor("") == null && Wiktionary.UrlFor("a/b") == null && Wiktionary.UrlFor(null) == null, "pas de mot, ou un mot qui casserait l'adresse : pas de lien");
        }

        private static string MoodTitles(Conjugation verb)
        {
            var titles = new List<string>();
            foreach (var mood in verb.Moods) titles.Add(mood.Title);
            return string.Join(",", titles.ToArray());
        }
    }
}

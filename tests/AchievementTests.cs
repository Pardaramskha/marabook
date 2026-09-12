using System;
using System.Collections.Generic;
using UniversSale.Model;
using UniversSale.Settings;

namespace UniversSale.Tests
{
    /// <summary>C21 — les succès (12/09/2026, liste complète du soir) : le
    /// catalogue (ids uniques, noms), les conditions mesurables, la mesure
    /// des faits sur un projet synthétique, le livre « fini à 100 % », le
    /// livre minimaliste, les paliers (cinq, vingt, cinquante, l'Empereur
    /// seulement quand tout le reste est acquis), la page blanche, la série
    /// de jours d'usage, la migration des anciens ids.</summary>
    public static class AchievementTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C21 — les succès");
            Catalog(t);
            Conditions(t);
            GatherProject(t);
            BookComplete(t);
            Minimalist(t);
            Tiers(t);
            BlankPage(t);
            UsageStreak(t);
        }

        private static void Catalog(Harness t)
        {
            var ids = new HashSet<string>();
            var duplicates = 0;
            foreach (var achievement in Achievements.All)
            {
                if (!ids.Add(achievement.Id)) duplicates++;
                if (string.IsNullOrEmpty(achievement.Name) || string.IsNullOrEmpty(achievement.Description)) duplicates++;
            }
            t.Equal(68, Achievements.All.Length, "soixante-huit succès, paliers compris");
            t.Equal(0, duplicates, "ids uniques, noms et descriptions remplis");
            t.Check(Achievements.Find(Achievements.Emperor) != null && Achievements.Find("inconnu") == null, "Find retrouve un id, ignore l'inconnu");
            t.Check(Achievements.IsTier("petit-nerd") && Achievements.IsTier(Achievements.Emperor) && !Achievements.IsTier("yolo"), "les paliers se reconnaissent");
            string renamed;
            t.Check(Achievements.RenamedIds.TryGetValue("roi-des-nerds", out renamed) && renamed == Achievements.Emperor, "l'ancien Roi devient l'Empereur");
        }

        private static void Conditions(Harness t)
        {
            var f = new AchievementFacts { FinishedBookParts = -1 };
            t.Check(!Achievements.Holds("ne-quelque-part", f), "zéro mot : rien");
            f.TotalWords = 1;
            t.Check(Achievements.Holds("ne-quelque-part", f) && !Achievements.Holds("clavier-chaud", f), "un mot : né quelque part, pas encore chaud");
            f.TotalWords = 10000000;
            t.Check(Achievements.Holds("time-to-stop", f) && Achievements.Holds("mitrailleur", f), "dix millions : tout l'escalier des mots");
            f.DoneTexts = 100;
            t.Check(Achievements.Holds("overachiever", f) && Achievements.Holds("check-check-check", f), "cent terminés : overachiever");
            f.MaxPlanColumns = 29; f.MaxPlanBricks = 99;
            t.Check(!Achievements.Holds("psychorigide", f), "29 colonnes et 99 briques : pas psychorigide");
            f.MaxPlanBricks = 100;
            t.Check(Achievements.Holds("psychorigide", f), "100 briques suffisent");
            f.MaxAnnotations = 100;
            t.Check(!Achievements.Holds("fouille-merde", f), "100 annotations : il en faut PLUS de 100");
            f.Plans = 6; f.Sheets = 99;
            t.Check(!Achievements.Holds("maitre-planificateur", f), "six plans mais 99 fiches : non");
            f.Sheets = 100;
            t.Check(Achievements.Holds("maitre-planificateur", f) && Achievements.Holds("pas-de-petit-detail", f), "six plans et cent fiches : oui");
            f.Sheets = 11; f.CharacterSheets = 0;
            t.Check(Achievements.Holds("une-vie-a-peindre", f), "onze fiches, aucun personnage : une vie à peindre");
            f.CharacterSheets = 1;
            t.Check(!Achievements.Holds("une-vie-a-peindre", f), "…un seul personnage et c'est fini");
            f.PlotBytes = 1024L * 1024 * 1024 - 1;
            t.Check(!Achievements.Holds("damn-boi", f), "un octet sous le gigaoctet : non");
            f.PlotBytes++;
            t.Check(Achievements.Holds("damn-boi", f), "le gigaoctet atteint : oui");
            f.Books = 5;
            t.Check(!Achievements.Holds("sanderson", f), "cinq livres : il en faut plus de cinq");
            t.Check(!Achievements.Holds("debut-de-la-fin", f), "aucun livre fini (-1) : pas de début de la fin");
            f.FinishedBookParts = 0;
            t.Check(Achievements.Holds("debut-de-la-fin", f) && !Achievements.Holds("flashbacks-philo", f), "livre fini sans partie : début de la fin, pas de flashbacks");
            f.FinishedBookParts = 3;
            t.Check(Achievements.Holds("flashbacks-philo", f), "trois parties : flashbacks de la philo");
            f.DirtyHours = 2; f.OpenHours = 8;
            t.Check(!Achievements.Holds("vivre-dangereusement", f) && !Achievements.Holds("pensez-a-vous-etirer", f), "deux heures pile, huit heures pile : pas encore");
            f.DirtyHours = 2.01; f.OpenHours = 8.01;
            t.Check(Achievements.Holds("vivre-dangereusement", f) && Achievements.Holds("pensez-a-vous-etirer", f), "…juste après : oui");
            f.ShortcutsChanged = 10; f.WordsAtMaxZoom = 100; f.WordsInCalm = 5000; f.LexiconEntries = 200;
            t.Check(Achievements.Holds("picky-eater", f) && Achievements.Holds("besoin-de-lunettes", f)
                && Achievements.Holds("inner-peace", f) && Achievements.Holds("tolkienniste", f), "raccourcis, zoom, calme, dictionnaire à 200");
            t.Check(!Achievements.Holds(Achievements.Yolo, f) && !Achievements.Holds(Achievements.GenZ, f) && !Achievements.Holds(Achievements.Completionist, f)
                && !Achievements.Holds(Achievements.FirstProject, f) && !Achievements.Holds(Achievements.About, f),
                "les succès à événement n'ont pas de condition mesurable");
            t.Check(!Achievements.Holds("petit-nerd", f) && !Achievements.Holds(Achievements.Emperor, f), "les paliers non plus");
        }

        private static BinderItem Text(string title, string status, string body)
        {
            var item = new BinderItem { Kind = ItemKind.Text, Title = title, Status = status };
            item.Document = TextDocument.FromPlainText(body);
            return item;
        }

        private static void GatherProject(Harness t)
        {
            var project = Project.CreateNew();
            project.Author = "Rémi";
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo(), ImageId = "img" };
            var part = new BinderItem { Kind = ItemKind.Folder, Title = "Partie I" };
            part.Children.Add(Text("I", "done", new string('a', 80000)));
            book.Children.Add(part);
            book.Children.Add(Text("II", "done", new string('b', 80000)));
            var extra = Text("Titre", "draft", "x"); extra.IsExtraPage = true;
            book.Children.Add(extra);
            writings.Children.Add(book);
            var beta = new BinderItem { Kind = ItemKind.Book, Title = "Bêta", Book = new BookInfo { ChapterGoal = 2, BleedMm = 5 } };
            beta.Book.Template.PageWidthMm += 30;
            beta.Children.Add(Text("b1", "beta", "un"));
            beta.Children.Add(Text("b2", "beta", "deux"));
            writings.Children.Add(beta);
            var deep1 = new BinderItem { Kind = ItemKind.Folder, Title = "1" };
            var deep2 = new BinderItem { Kind = ItemKind.Folder, Title = "2" };
            var deep3 = new BinderItem { Kind = ItemKind.Folder, Title = "3" };
            deep2.Children.Add(deep3); deep1.Children.Add(deep2); writings.Children.Add(deep1);
            var pinned = Text("Épinglé", "todo", "mots"); pinned.Pinned = true;
            writings.Children.Add(pinned);
            project.Category(Project.KeyResearch).Children.Add(new BinderItem { Kind = ItemKind.Media, Title = "photo", MediaExtension = ".png", MediaBytes = new byte[] { 1 } });
            var plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan", Plan = new PlanInfo() };
            for (var i = 0; i < 3; i++) plan.Plan.Columns.Add(new PlanColumn { Title = "C" + i });
            plan.Plan.Columns[0].Entries.Add(new PlanEntry());
            project.Category(Project.KeyPlans).Children.Add(plan);
            var category = project.SheetCategories[0]; // Personnage
            var sheets = project.Category(Project.KeySheets);
            sheets.Children.Add(new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", CategoryId = category.Id, TemplateId = category.TemplateId });
            sheets.Children.Add(new BinderItem { Kind = ItemKind.Sheet, Title = "rémi", CategoryId = category.Id, TemplateId = category.TemplateId });
            sheets.Children.Add(new BinderItem { Kind = ItemKind.Sheet, Title = "Lieu", CategoryId = project.SheetCategories[1].Id });
            var full = new BinderItem { Kind = ItemKind.Sheet, Title = "Encyclopédique", CategoryId = project.SheetCategories[1].Id };
            for (var i = 0; i < 51; i++) full.FreeInfo.Add(new InfoEntry { Title = "Champ " + i, Value = "valeur" });
            sheets.Children.Add(full);
            project.Lexicon.Add(LexiconEntry.Simple("Keira"));
            project.Journal.Add("2026-09-10", 21000);
            project.Journal.Add("2026-09-11", 4);
            project.RelinkParents();

            var facts = Achievements.Gather(project, null, null, null, new DateTime(2026, 9, 12));
            t.Equal(2, facts.Books, "deux livres");
            t.Check(facts.BookWithImage, "…dont un illustré");
            t.Equal(2, facts.DoneTexts, "deux écrits terminés, la page extra ne compte pas");
            t.Equal(1, facts.FinishedBookParts, "le livre fini (160 000 signes) a une partie");
            t.Check(facts.BetaBook, "le livre bêta : quota de 2 atteint, tout en bêta");
            t.Check(facts.OddBook, "…et hors format avec un fond perdu de 5 mm : sniffeur de papier");
            t.Equal(3, facts.MaxFolderDepth, "trois dossiers imbriqués");
            t.Equal(1, facts.Pinned, "une épingle");
            t.Equal(1, facts.ResearchItems, "un élément de recherche");
            t.Equal(1, facts.Plans, "un plan");
            t.Equal(3, facts.MaxPlanColumns, "trois colonnes");
            t.Equal(1, facts.MaxPlanBricks, "une brique");
            t.Equal(4, facts.Sheets, "quatre fiches");
            t.Equal(2, facts.CharacterSheets, "dont deux personnages");
            t.Check(facts.GodComplex, "« rémi » porte le nom de l'auteur : complexe de dieu");
            t.Check(facts.FullSheet, "51 champs tous remplis : toujours plus");
            t.Equal(1, facts.LexiconEntries, "une entrée de dictionnaire");
            t.Equal(21004, facts.TotalWords, "mots du journal");
            t.Equal(21000, facts.MaxDayWords, "le meilleur jour");
            t.Check(Achievements.IsCharacterCategory("Personnages") && !Achievements.IsCharacterCategory("Lieu"), "la catégorie Personnage se reconnaît au nom");
            t.Equal(2, Achievements.ChapterCount(book), "deux chapitres dans le roman (page extra exclue)");
            t.Equal(2, Achievements.CountAll(deep1.Children), "CountAll compte les descendants (dossier 2 et dossier 3)");

            full.FreeInfo[0].Value = "";
            t.Check(!Achievements.IsFullSheet(project, full), "un champ vidé : plus « toujours plus »");

            var earned = Achievements.Earned(facts, new List<string>());
            t.Check(earned.Contains("ne-quelque-part") && earned.Contains("clavier-chaud") && earned.Contains("speedrunner")
                && earned.Contains("dans-deux-mois") && earned.Contains("et-d-un") && earned.Contains("debut-de-la-fin")
                && earned.Contains("chirurgien") && earned.Contains("illustrateur") && earned.Contains("profiler")
                && earned.Contains("cartographieur") && earned.Contains("archiviste-debutant") && earned.Contains("chercheur-debutant")
                && earned.Contains("gros-beta") && earned.Contains("sniffeur-de-papier") && earned.Contains("complexe-de-dieu") && earned.Contains("toujours-plus"),
                "les succès du projet synthétique tombent d'un coup");
            t.Check(earned.Contains("petit-nerd") && !earned.Contains("poisson-panerd"), "plus de cinq d'un coup : Petit nerd, pas Poisson panerd (moins de vingt)");
            t.Check(!earned.Contains(Achievements.Emperor) && !earned.Contains("masochiste"), "…ni l'Empereur ni ceux qui manquent");
            var already = new List<string> { "ne-quelque-part" };
            t.Check(!Achievements.Earned(facts, already).Contains("ne-quelque-part"), "un succès acquis n'est pas regagné");
        }

        private static void BookComplete(Harness t)
        {
            var project = Project.CreateNew();
            project.Author = "Rémi";
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo() };
            book.Children.Add(Text("I", "done", "un"));
            t.Check(!Achievements.IsBookComplete(project, book), "sans liminaire, TdM, gabarit ni métadonnées : incomplet");
            var extra = Text("Préface", "draft", "p"); extra.IsExtraPage = true;
            var toc = Text("Sommaire", "draft", ""); toc.IsToc = true;
            book.Children.Add(extra);
            book.Children.Add(toc);
            book.Children.Add(new BinderItem { Kind = ItemKind.PageTemplate, Title = "Gabarit" });
            var info = book.Book;
            info.Isbn = "978"; info.Publisher = "Éd."; info.Year = "2026"; info.Genre = "Fantasy"; info.Audience = "Adultes";
            info.Themes.Add("famille"); info.Pitch = "Accroche"; info.BackCover = "Quatrième";
            t.Check(Achievements.IsBookComplete(project, book), "tout rempli : complet (auteur du projet)");
            book.Children.Add(Text("II", "draft", "deux"));
            t.Check(!Achievements.IsBookComplete(project, book), "un écrit non terminé : incomplet");
        }

        private static void Minimalist(Harness t)
        {
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Sobre", Book = new BookInfo() };
            book.Children.Add(Text("I", null, "un"));
            t.Check(Achievements.IsMinimalist(book), "ni couleur, ni objectif, ni état : minimaliste");
            book.Children[0].Status = "draft";
            t.Check(!Achievements.IsMinimalist(book), "un état posé : non");
            book.Children[0].Status = null;
            book.Children[0].CardColor = "#FF0000";
            t.Check(!Achievements.IsMinimalist(book), "une couleur de carte : non");
            book.Children[0].CardColor = null;
            book.Book.ChapterGoal = 3;
            t.Check(!Achievements.IsMinimalist(book), "un objectif de chapitres : non");
        }

        private static void Tiers(Harness t)
        {
            var facts = new AchievementFacts { FinishedBookParts = -1 };
            var four = new List<string> { "yolo", "gen-zer", "profiler", "et-d-un" };
            t.Equal(0, Achievements.Earned(facts, four).Count, "quatre acquis : aucun palier");
            four.Add("sanderson");
            var earned = Achievements.Earned(facts, four);
            t.Check(earned.Count == 1 && earned[0] == "petit-nerd", "cinq acquis : Petit nerd");
            var nineteen = new List<string>();
            foreach (var achievement in Achievements.All)
                if (!Achievements.IsTier(achievement.Id) && nineteen.Count < 19) nineteen.Add(achievement.Id);
            earned = Achievements.Earned(facts, nineteen);
            t.Check(earned.Contains("petit-nerd") && earned.Contains("poisson-panerd") && !earned.Contains("nerdinator"),
                "dix-neuf acquis + Petit nerd = vingt : Poisson panerd tombe dans la foulée");
            var all = new List<string>();
            foreach (var achievement in Achievements.All)
                if (achievement.Id != Achievements.Emperor) all.Add(achievement.Id);
            earned = Achievements.Earned(facts, all);
            t.Check(earned.Count == 1 && earned[0] == Achievements.Emperor, "tous les autres acquis : l'Empereur");
            all.Remove(Achievements.Yolo);
            t.Equal(0, Achievements.Earned(facts, all).Count, "il manque Yolo (événement) : pas d'Empereur");
        }

        private static void BlankPage(Harness t)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            var blank = Text("Vierge", null, "");
            var written = Text("Écrit", null, "des mots");
            writings.Children.Add(blank);
            writings.Children.Add(written);
            project.RelinkParents();
            var since = new Dictionary<string, string>();
            var day = new DateTime(2026, 9, 12);
            var facts = Achievements.Gather(project, null, null, since, day);
            t.Check(since.ContainsKey(blank.Id) && since[blank.Id] == "2026-09-12" && !since.ContainsKey(written.Id), "l'écrit vierge est daté, l'autre non");
            t.Check(!facts.BlankSevenDays, "le jour même : rien");
            facts = Achievements.Gather(project, null, null, since, day.AddDays(6));
            t.Check(!facts.BlankSevenDays, "six jours : pas encore");
            facts = Achievements.Gather(project, null, null, since, day.AddDays(7));
            t.Check(facts.BlankSevenDays, "sept jours vierge : page blanche");
            blank.Document = TextDocument.FromPlainText("enfin");
            facts = Achievements.Gather(project, null, null, since, day.AddDays(8));
            t.Check(!facts.BlankSevenDays && !since.ContainsKey(blank.Id), "écrit : sort du suivi");
            since["fantome"] = "2026-01-01";
            Achievements.Gather(project, null, null, since, day);
            t.Check(!since.ContainsKey("fantome"), "un id disparu est purgé");
        }

        private static void UsageStreak(Harness t)
        {
            var savedDay = AppSettings.UsageLastDay;
            var savedStreak = AppSettings.UsageStreak;
            try
            {
                AppSettings.UsageLastDay = null;
                AppSettings.UsageStreak = 0;
                var day = new DateTime(2026, 9, 12, 10, 0, 0);
                AppSettings.NoteUsage(day);
                t.Equal(1, AppSettings.UsageStreak, "premier jour : série de un");
                AppSettings.NoteUsage(day.AddHours(5));
                t.Equal(1, AppSettings.UsageStreak, "le même jour ne compte pas deux fois");
                AppSettings.NoteUsage(day.AddDays(1));
                t.Equal(2, AppSettings.UsageStreak, "le lendemain prolonge");
                AppSettings.NoteUsage(day.AddDays(3));
                t.Equal(1, AppSettings.UsageStreak, "un jour sauté : la série repart");
            }
            finally
            {
                AppSettings.UsageLastDay = savedDay;
                AppSettings.UsageStreak = savedStreak;
            }
        }
    }
}

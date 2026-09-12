using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversSale.Model
{
    /// <summary>Un succès (12/09/2026) : identité stable (le nom de fichier
    /// de son image, assets/achievements/&lt;Id&gt;.png), nom, description.
    /// Les succès sont GLOBAUX à l'utilisateur (settings.json), à la manière
    /// de Steam : une condition remplie une fois reste acquise.</summary>
    public class Achievement
    {
        public string Id, Name, Description;

        public Achievement(string id, string name, string description)
        {
            Id = id;
            Name = name;
            Description = description;
        }
    }

    /// <summary>Les faits mesurés sur lesquels les conditions se lisent —
    /// remplis par Achievements.Gather (projet) et par la fenêtre (réglages,
    /// taille du fichier, pages, temps). Un modèle pur, testable.</summary>
    public class AchievementFacts
    {
        public int TotalWords;         // journal : mots nets consignés
        public int MaxDayWords;        // journal : meilleur jour
        public int Books;
        public int DoneTexts;          // écrits « terminé »
        public bool BetaBook;          // quota de chapitres rempli, tous « bêta »
        public int FinishedBookParts;  // parties du livre fini qui en a le plus
        public int ResearchItems;      // éléments sous Recherche
        public int MaxBookPages;
        public int Plans;
        public int MaxPlanColumns, MaxPlanBricks;
        public bool BlankSevenDays;    // un écrit vierge depuis sept jours
        public int MaxAnnotations;     // sur un seul écrit
        public int MaxFootnotes;       // sur un seul écrit
        public bool AllProofOptions;   // orthographe + grammaire + typographie + style
        public int MaxDocPages;
        public int LexiconEntries;     // projet + tous les projets
        public int CharacterSheets;    // fiches d'une catégorie « Personnage »
        public int Sheets;             // toutes catégories
        public int SheetCategories;
        public int MaxGenealogy;       // fiches liées dans un arbre
        public bool FullSheet;         // une fiche de plus de 50 champs, tous remplis
        public bool GodComplex;        // une fiche personnage au nom de l'auteur
        public int PermanentlyDeleted; // compteur global (corbeille vidée)
        public int MaxFolderDepth;     // dossiers imbriqués, livres exclus
        public int ShortcutsChanged;   // raccourcis réattribués
        public bool AccentChanged;
        public int UsageStreak;        // jours consécutifs d'ouverture
        public int Pinned;
        public long PlotBytes;         // dernier enregistrement
        public double DirtyHours;      // heures depuis la dernière sauvegarde manuelle, projet modifié
        public int MaxSnapshots;       // sur un seul écrit
        public bool BookWithImage;
        public int MaxTemplatesApplied; // gabarits distincts appliqués dans un livre (≥ 4 créés)
        public bool OddBook;           // format hors défaut ET fond perdu différent
        public int WordsAtMaxZoom;     // mots écrits à 300 %
        public double OpenHours;       // heures depuis le lancement
        public int WordsInCalm;        // mots écrits en mode calme
    }

    public static class Achievements
    {
        // Les paliers : ils se comptent sur les autres succès.
        public const string Emperor = "empereur-des-nerds";
        // Succès à ÉVÉNEMENT : la fenêtre les débloque au moment du geste,
        // aucune condition mesurable ne les porte.
        public const string FirstProject = "belle-aventure";
        public const string Completionist = "completionniste";
        public const string GenZ = "gen-zer";
        public const string DeepClean = "nettoyage-en-profondeur";
        public const string Blunder = "ooh-la-boulette";
        public const string Yolo = "yolo";
        public const string Minimalist = "minimaliste";
        public const string Cretin = "cretin-des-alpes";
        public const string About = "enfin-quelqu-un";
        public const string OldSchool = "a-l-ancienne";

        public static readonly Achievement[] All =
        {
            new Achievement(FirstProject, "Le début d'une belle aventure", "Créez votre premier projet."),

            new Achievement("ne-quelque-part", "Être né quelque part", "Écrivez votre premier mot."),
            new Achievement("clavier-chaud", "Faites chauffer le clavier", "Écrivez 5 000 mots."),
            new Achievement("canal-carpien", "Attention au canal carpien", "Écrivez 150 000 mots."),
            new Achievement("mitrailleur", "Mitrailleur", "Écrivez 500 000 mots."),
            new Achievement("toucher-de-l-herbe", "Et sinon, vous touchez de l'herbe ?", "Écrivez 1 000 000 de mots."),
            new Achievement("time-to-stop", "It's time to stop, okay ?", "Écrivez 10 000 000 de mots."),
            new Achievement("dans-deux-mois", "Promis, il sera fini dans deux mois", "Créez votre premier fichier livre."),
            new Achievement("et-d-un", "Et d'un !", "Estampillez votre premier écrit comme « terminé »."),
            new Achievement("check-check-check", "Check check check", "Estampillez votre vingtième chapitre comme « terminé »."),
            new Achievement("overachiever", "Overachiever", "Estampillez votre centième document comme « terminé »."),
            new Achievement("gros-beta", "Gros bêta", "Ayez un livre avec votre quota de chapitres rempli, chaque chapitre estampillé « bêta »."),
            new Achievement("deux-heures-max", "Non mais ça se lit en deux heures max, promis !", "Atteignez 1 000 pages de texte sur un seul livre."),
            new Achievement("flashbacks-philo", "Flashbacks de la philo", "Votre livre terminé contient trois parties."),
            new Achievement("debut-de-la-fin", "Le début de la fin", "Finissez votre premier livre : tous les documents marqués comme terminés, 150 000 signes au moins."),
            new Achievement(Completionist, "Complétionniste", "Créez un PDF d'un livre fini à 100 % : pages liminaires, sommaire, ISBN, gabarits, informations d'auteur, métadonnées et édition du livre remplies, tous les documents marqués terminés."),
            new Achievement(GenZ, "Gen Zer", "Créez un EPUB pour économiser du papier."),

            new Achievement("chercheur-debutant", "Chercheur débutant", "Épinglez votre premier élément dans le tableau de recherche."),
            new Achievement("c-est-simple", "Je vous jure c'est simple !", "Épinglez votre trentième élément sur le tableau de recherche."),

            new Achievement("cartographieur", "Cartographieur", "Créez votre premier plan."),
            new Achievement("psychorigide", "Psychorigide", "Créez un plan avec au moins 30 colonnes ou 100 briques."),

            new Achievement("page-blanche", "Page blanche", "Créez un document et gardez-le vierge pendant au moins sept jours consécutifs."),
            new Achievement("fouille-merde", "Fouille-merde", "Créez plus de 100 annotations sur un seul document."),
            new Achievement("probleme-de-worldbuilding", "Peut-être un problème de worldbuilding", "Ayez plus de vingt notes de bas de page sur un seul document."),
            new Achievement(DeepClean, "Nettoyage en profondeur", "Sur un document avec plus de cent erreurs orthographiques, corrigez-en l'entièreté."),
            new Achievement("sur-stimulation", "Sur-stimulation", "Activez toutes les options de soulignage du correcteur simultanément."),
            new Achievement("aristote", "Ça va Aristote ?", "Un document dépasse les cent pages. Psychopathe."),

            new Achievement("archiviste-debutant", "Archiviste débutant", "Créez votre première entrée du dictionnaire."),
            new Achievement("academicien", "Académicien", "Créez votre cinquantième entrée dans le dictionnaire."),
            new Achievement("tolkienniste", "Tolkienniste", "Créez votre deux-centième entrée dans le dictionnaire."),

            new Achievement("profiler", "Profiler", "Créez une fiche de personnage."),
            new Achievement("liberticide", "Liberticide", "Créez votre trentième fiche de personnage."),
            new Achievement("fier-parent", "Fier parent", "Créez votre centième fiche de personnage."),
            new Achievement("une-vie-a-peindre", "Une vie à peindre", "Ayez plus de dix fiches dont aucune de personnage."),
            new Achievement("pas-de-petit-detail", "Y'a pas de petit détail", "Créez votre centième fiche, toutes catégories confondues."),
            new Achievement("maitre-planificateur", "Maître planificateur", "Totalisez six plans et au moins cent fiches, toutes catégories confondues."),
            new Achievement("genealogiste", "Généalogiste", "Dans les fiches de personnages, atteignez un arbre généalogique à plus de vingt personnages ayant chacun·e leur fiche."),
            new Achievement("jamais-content", "Jamais content", "Créez dix catégories de fiches."),
            new Achievement("toujours-plus", "Toujours plus !", "Remplissez intégralement une fiche qui contient plus de 50 champs au total."),
            new Achievement("complexe-de-dieu", "Complexe de dieu", "Créez une fiche personnage qui porte le même nom que l'auteur d'un document ou d'un livre."),

            new Achievement("terre-brulee", "Terre brûlée", "Supprimez définitivement un élément."),
            new Achievement("masochiste", "Masochiste", "Supprimez définitivement votre vingtième élément."),
            new Achievement(Blunder, "Ooh la boulette !", "Envoyez à la corbeille un livre contenant au moins cinq chapitres. Vous pouvez toujours l'annuler, pleurez pas !"),

            new Achievement("chirurgien", "Chirurgien de l'écriture", "Atteignez une arborescence d'au moins trois sous-dossiers (livres exclus)."),

            new Achievement("picky-eater", "Picky eater", "Changez l'attribution de dix raccourcis."),
            new Achievement("pimp-my-write", "Pimp my write", "Changez la couleur d'accent de l'app. Elle est belle, eh ?"),

            new Achievement("speedrunner", "Speedrunner", "Écrivez 20 000 mots en l'espace de 24 heures."),
            new Achievement("autre-addiction", "Une autre forme d'addiction", "Utilisez le logiciel pendant 31 jours consécutifs."),

            new Achievement("accessible", "Le tout c'est que ce soit accessible !", "Épinglez dix éléments ou plus à l'écran d'accueil."),

            new Achievement("damn-boi", "Damn boi, he thicc!", "Votre fichier .plot dépasse le giga de poids."),
            new Achievement("vivre-dangereusement", "Vivre dangereusement", "Passez plus de deux heures d'écriture sans sauvegarder manuellement votre fichier."),

            new Achievement(Yolo, "Yolo", "Cliquez sur « Tout remplacer » sur une recherche qui a plus de cent occurrences. Ça se verra bien à la relecture, pas vrai ?"),

            new Achievement("incertitude", "Incertitude", "Cumulez dix versions différentes d'un seul écrit."),

            new Achievement("illustrateur", "Illustrateur", "Attachez une image à un livre. Il est mieux comme ça, hein ?"),
            new Achievement("pointilliste", "Pointilliste", "Sur un seul livre, créez et appliquez au moins quatre gabarits."),
            new Achievement("sniffeur-de-papier", "Sniffeur de papier", "Créez un livre hors-format de marges et de taille avec un fond perdu différent de celui par défaut. L'originalité a du bon."),

            new Achievement("sanderson", "Sanderson serait fier", "Ayez plus de cinq livres dans un seul projet."),

            new Achievement("besoin-de-lunettes", "Besoin de lunettes ?", "Écrivez cent mots avec un zoom de 300 %."),
            new Achievement(Minimalist, "Minimaliste", "Exportez un livre terminé sans avoir utilisé de couleur personnalisée pour les fichiers, d'objectif de chapitre et d'état de finition des documents."),
            new Achievement("pensez-a-vous-etirer", "Pensez à vous étirer", "Gardez l'application ouverte pendant plus de 8 heures consécutives."),
            new Achievement(Cretin, "Crétin des alpes", "Recherchez une fiche alors que vous n'en avez pas encore créée."),
            new Achievement(About, "Enfin quelqu'un qui en a quelque chose à faire !", "Cliquez sur « À propos de Marabook »."),
            new Achievement("inner-peace", "Inner peace", "Écrivez 5 000 mots en mode calme."),
            new Achievement(OldSchool, "À l'ancienne", "Imprimez un document depuis Marabook."),

            new Achievement("petit-nerd", "Petit nerd", "Obtenez cinq succès."),
            new Achievement("poisson-panerd", "Poisson panerd", "Obtenez vingt succès."),
            new Achievement("nerdinator", "Nerdinator", "Obtenez cinquante succès."),
            new Achievement(Emperor, "Empereur des nerds", "Obtenez tous les succès."),
        };

        /// <summary>Anciens identifiants (première liste du 12/09) → nouveaux,
        /// pour les réglages déjà écrits.</summary>
        public static readonly Dictionary<string, string> RenamedIds = new Dictionary<string, string>
        {
            { "roi-des-nerds", Emperor },
            { "c-est-pour-ca-la-pile", "aristote" },
            { "pensez-a-sauvegarder", "damn-boi" },
        };

        public static Achievement Find(string id)
        {
            foreach (var achievement in All)
                if (achievement.Id == id) return achievement;
            return null;
        }

        public static bool IsTier(string id)
        {
            return id == "petit-nerd" || id == "poisson-panerd" || id == "nerdinator" || id == Emperor;
        }

        /// <summary>La condition mesurable d'un succès ; false pour les succès
        /// à événement et pour les paliers (traités à part).</summary>
        public static bool Holds(string id, AchievementFacts f)
        {
            switch (id)
            {
                case "ne-quelque-part": return f.TotalWords >= 1;
                case "clavier-chaud": return f.TotalWords >= 5000;
                case "canal-carpien": return f.TotalWords >= 150000;
                case "mitrailleur": return f.TotalWords >= 500000;
                case "toucher-de-l-herbe": return f.TotalWords >= 1000000;
                case "time-to-stop": return f.TotalWords >= 10000000;
                case "dans-deux-mois": return f.Books >= 1;
                case "et-d-un": return f.DoneTexts >= 1;
                case "check-check-check": return f.DoneTexts >= 20;
                case "overachiever": return f.DoneTexts >= 100;
                case "gros-beta": return f.BetaBook;
                case "deux-heures-max": return f.MaxBookPages >= 1000;
                case "flashbacks-philo": return f.FinishedBookParts >= 3;
                case "debut-de-la-fin": return FinishedBook(f);
                case "chercheur-debutant": return f.ResearchItems >= 1;
                case "c-est-simple": return f.ResearchItems >= 30;
                case "cartographieur": return f.Plans >= 1;
                case "psychorigide": return f.MaxPlanColumns >= 30 || f.MaxPlanBricks >= 100;
                case "page-blanche": return f.BlankSevenDays;
                case "fouille-merde": return f.MaxAnnotations > 100;
                case "probleme-de-worldbuilding": return f.MaxFootnotes > 20;
                case "sur-stimulation": return f.AllProofOptions;
                case "aristote": return f.MaxDocPages > 100;
                case "archiviste-debutant": return f.LexiconEntries >= 1;
                case "academicien": return f.LexiconEntries >= 50;
                case "tolkienniste": return f.LexiconEntries >= 200;
                case "profiler": return f.CharacterSheets >= 1;
                case "liberticide": return f.CharacterSheets >= 30;
                case "fier-parent": return f.CharacterSheets >= 100;
                case "une-vie-a-peindre": return f.Sheets > 10 && f.CharacterSheets == 0;
                case "pas-de-petit-detail": return f.Sheets >= 100;
                case "maitre-planificateur": return f.Plans >= 6 && f.Sheets >= 100;
                case "genealogiste": return f.MaxGenealogy > 20;
                case "jamais-content": return f.SheetCategories >= 10;
                case "toujours-plus": return f.FullSheet;
                case "complexe-de-dieu": return f.GodComplex;
                case "terre-brulee": return f.PermanentlyDeleted >= 1;
                case "masochiste": return f.PermanentlyDeleted >= 20;
                case "chirurgien": return f.MaxFolderDepth >= 3;
                case "picky-eater": return f.ShortcutsChanged >= 10;
                case "pimp-my-write": return f.AccentChanged;
                case "speedrunner": return f.MaxDayWords >= 20000;
                case "autre-addiction": return f.UsageStreak >= 31;
                case "accessible": return f.Pinned >= 10;
                case "damn-boi": return f.PlotBytes >= 1024L * 1024 * 1024;
                case "vivre-dangereusement": return f.DirtyHours > 2;
                case "incertitude": return f.MaxSnapshots >= 10;
                case "illustrateur": return f.BookWithImage;
                case "pointilliste": return f.MaxTemplatesApplied >= 4;
                case "sniffeur-de-papier": return f.OddBook;
                case "sanderson": return f.Books > 5;
                case "besoin-de-lunettes": return f.WordsAtMaxZoom >= 100;
                case "pensez-a-vous-etirer": return f.OpenHours > 8;
                case "inner-peace": return f.WordsInCalm >= 5000;
                default: return false;
            }
        }

        /// <summary>« Le début de la fin » : FinishedBookParts vaut -1 tant
        /// qu'aucun livre n'est fini, sinon le nombre de parties du livre fini.</summary>
        private static bool FinishedBook(AchievementFacts f)
        {
            return f.FinishedBookParts >= 0;
        }

        /// <summary>Les succès à débloquer maintenant : ceux dont la condition
        /// tient et qui manquent encore, puis les paliers (cinq, vingt,
        /// cinquante, tous — les succès à événement comptent, l'Empereur les
        /// exige).</summary>
        public static List<string> Earned(AchievementFacts facts, ICollection<string> unlocked)
        {
            var earned = new List<string>();
            foreach (var achievement in All)
            {
                if (IsTier(achievement.Id) || unlocked.Contains(achievement.Id)) continue;
                if (Holds(achievement.Id, facts)) earned.Add(achievement.Id);
            }
            var count = unlocked.Count + earned.Count;
            AddTier("petit-nerd", 5, ref count, unlocked, earned);
            AddTier("poisson-panerd", 20, ref count, unlocked, earned);
            AddTier("nerdinator", 50, ref count, unlocked, earned);
            if (!unlocked.Contains(Emperor))
            {
                var complete = true;
                foreach (var achievement in All)
                {
                    if (achievement.Id == Emperor) continue;
                    if (!unlocked.Contains(achievement.Id) && !earned.Contains(achievement.Id)) { complete = false; break; }
                }
                if (complete) earned.Add(Emperor);
            }
            return earned;
        }

        private static void AddTier(string id, int threshold, ref int count, ICollection<string> unlocked, List<string> earned)
        {
            if (unlocked.Contains(id) || count < threshold) return;
            earned.Add(id);
            count++;
        }

        /// <summary>Un livre « fini à 100 % » (Complétionniste) : chaque écrit
        /// terminé, au moins une page liminaire et une table des matières, un
        /// gabarit, un auteur, et les champs Métadonnées / Édition remplis.</summary>
        public static bool IsBookComplete(Project project, BinderItem book)
        {
            if (book == null || book.Book == null) return false;
            var texts = 0;
            var extras = 0;
            var toc = false;
            var templates = 0;
            foreach (var item in Descendants(book))
            {
                if (item.Kind == ItemKind.PageTemplate) { templates++; continue; }
                if (item.Kind != ItemKind.Text) continue;
                if (item.IsToc) { toc = true; continue; }
                if (item.IsExtraPage) { extras++; continue; }
                texts++;
                if (item.Status != "done") return false;
            }
            if (texts == 0 || extras == 0 || !toc || templates == 0) return false;
            var info = book.Book;
            var author = Filled(info.AuthorOverride) || (project != null && Filled(project.Author));
            return author && Filled(info.Isbn) && Filled(info.Publisher) && Filled(info.Year)
                && Filled(info.Genre) && Filled(info.Audience) && info.Themes.Count > 0
                && Filled(info.Pitch) && Filled(info.BackCover);
        }

        /// <summary>« Minimaliste » : un livre exporté sans couleur de carte
        /// sur aucun de ses éléments, sans objectif de chapitres, et sans
        /// état de finition posé sur ses documents.</summary>
        public static bool IsMinimalist(BinderItem book)
        {
            if (book == null || book.Book == null) return false;
            if (book.Book.ChapterGoal > 0 || book.CardColor != null) return false;
            var texts = 0;
            foreach (var item in Descendants(book))
            {
                if (item.CardColor != null) return false;
                if (item.Kind != ItemKind.Text) continue;
                texts++;
                if (item.Status != null) return false;
            }
            return texts > 0;
        }

        /// <summary>Chapitres d'un livre : les écrits qui ne sont ni pages
        /// extra ni table des matières.</summary>
        public static int ChapterCount(BinderItem book)
        {
            var count = 0;
            foreach (var item in Descendants(book))
                if (item.Kind == ItemKind.Text && !item.IsExtraPage && !item.IsToc) count++;
            return count;
        }

        /// <summary>Éléments d'une liste, descendants compris (la corbeille vidée).</summary>
        public static int CountAll(IEnumerable<BinderItem> items)
        {
            var count = 0;
            foreach (var item in items)
            {
                count++;
                count += CountAll(item.Children);
            }
            return count;
        }

        private static bool Filled(string value)
        {
            return value != null && value.Trim().Length > 0;
        }

        /// <summary>Les faits lisibles dans le projet seul. Les comptes de pages
        /// viennent de la fenêtre (null = inconnus, jamais bloquants) ;
        /// <paramref name="blankSince"/> (id → « yyyy-MM-dd ») mémorise depuis
        /// quand chaque écrit est vierge et se met à jour ici.</summary>
        public static AchievementFacts Gather(Project project, Func<BinderItem, int> docPages,
            Func<BinderItem, int> bookPages, Dictionary<string, string> blankSince, DateTime now)
        {
            var f = new AchievementFacts { FinishedBookParts = -1 };
            if (project == null) return f;
            f.TotalWords = project.Journal.TotalWords();
            foreach (var day in project.Journal.Days) f.MaxDayWords = Math.Max(f.MaxDayWords, day.Words);
            f.LexiconEntries = project.Lexicon.Count;
            f.SheetCategories = project.SheetCategories.Count;
            var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var weekAgo = now.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var authors = new HashSet<string>();
            if (Filled(project.Author)) authors.Add(Correction.FrenchTokenizer.Fold(project.Author.Trim()));
            var seen = new HashSet<string>();

            foreach (var item in project.AllItems())
            {
                if (item.IsCategory) continue;
                var root = item.RootCategory();
                if (root != null && root.CategoryKey == Project.KeyTrash) continue;
                if (item.Pinned) f.Pinned++;
                if (root != null && root.CategoryKey == Project.KeyResearch) f.ResearchItems++;
                switch (item.Kind)
                {
                    case ItemKind.Book:
                        f.Books++;
                        if (item.ImageId != null) f.BookWithImage = true;
                        if (bookPages != null) f.MaxBookPages = Math.Max(f.MaxBookPages, bookPages(item));
                        f.MaxTemplatesApplied = Math.Max(f.MaxTemplatesApplied, TemplatesApplied(item));
                        if (IsFinished(item)) f.FinishedBookParts = Math.Max(f.FinishedBookParts, PartCount(item));
                        if (IsBetaBook(item)) f.BetaBook = true;
                        if (IsOddBook(item)) f.OddBook = true;
                        if (Filled(item.Book.AuthorOverride)) authors.Add(Correction.FrenchTokenizer.Fold(item.Book.AuthorOverride.Trim()));
                        break;
                    case ItemKind.Text:
                        if (item.Status == "done" && !item.IsExtraPage && !item.IsToc) f.DoneTexts++;
                        if (item.Document != null)
                        {
                            f.MaxAnnotations = Math.Max(f.MaxAnnotations, item.Document.Annotations.Count);
                            f.MaxFootnotes = Math.Max(f.MaxFootnotes, item.Document.Footnotes.Count);
                        }
                        if (docPages != null) f.MaxDocPages = Math.Max(f.MaxDocPages, docPages(item));
                        f.MaxSnapshots = Math.Max(f.MaxSnapshots, SnapshotStore.Of(project, item.Id).Count);
                        if (blankSince != null && !item.IsExtraPage && !item.IsToc)
                        {
                            var blank = item.Document == null || item.Document.ToPlainText().Trim().Length == 0;
                            if (!blank) blankSince.Remove(item.Id);
                            else
                            {
                                seen.Add(item.Id);
                                string since;
                                if (!blankSince.TryGetValue(item.Id, out since)) blankSince[item.Id] = today;
                                else if (string.CompareOrdinal(since, weekAgo) <= 0) f.BlankSevenDays = true;
                            }
                        }
                        break;
                    case ItemKind.Plan:
                        f.Plans++;
                        if (item.Plan != null)
                        {
                            f.MaxPlanColumns = Math.Max(f.MaxPlanColumns, item.Plan.Columns.Count);
                            var bricks = 0;
                            foreach (var column in item.Plan.Columns) bricks += column.Entries.Count;
                            f.MaxPlanBricks = Math.Max(f.MaxPlanBricks, bricks);
                        }
                        break;
                    case ItemKind.Sheet:
                        f.Sheets++;
                        var category = project.SheetCategoryOf(item);
                        var character = category != null && IsCharacterCategory(category.Name);
                        if (character) f.CharacterSheets++;
                        if (character && authors.Contains(Correction.FrenchTokenizer.Fold((item.Title ?? "").Trim()))) f.GodComplex = true;
                        if (IsFullSheet(project, item)) f.FullSheet = true;
                        if (item.Relations.Count > 0)
                        {
                            var linked = 0;
                            foreach (var node in Genealogy.Build(project, item))
                                if (node.IsSelf || node.TargetId != null) linked++;
                            f.MaxGenealogy = Math.Max(f.MaxGenealogy, linked);
                        }
                        break;
                    case ItemKind.Folder:
                        f.MaxFolderDepth = Math.Max(f.MaxFolderDepth, FolderDepth(item));
                        break;
                }
            }
            // Les écrits disparus (corbeille, autre projet) sortent du suivi.
            if (blankSince != null)
            {
                var stale = new List<string>();
                foreach (var id in blankSince.Keys) if (!seen.Contains(id)) stale.Add(id);
                foreach (var id in stale) blankSince.Remove(id);
            }
            return f;
        }

        public static bool IsCharacterCategory(string name)
        {
            return name != null && name.Trim().ToLowerInvariant().StartsWith("personnage");
        }

        /// <summary>« Toujours plus ! » : plus de 50 champs (modèle + libres),
        /// tous remplis.</summary>
        public static bool IsFullSheet(Project project, BinderItem sheet)
        {
            var template = project == null ? null : project.FindTemplate(sheet.TemplateId);
            var total = sheet.FreeInfo.Count + (template == null ? 0 : template.Fields.Count);
            if (total <= 50) return false;
            foreach (var entry in sheet.FreeInfo)
                if (!Filled(entry.Value)) return false;
            if (template != null)
                foreach (var field in template.Fields)
                {
                    string value;
                    if (!sheet.FieldValues.TryGetValue(field.Id, out value) || !Filled(value)) return false;
                }
            return true;
        }

        /// <summary>Profondeur d'un dossier parmi les DOSSIERS seulement (un
        /// livre ne compte pas comme niveau) : dossier à la racine = 1.</summary>
        private static int FolderDepth(BinderItem folder)
        {
            var depth = 0;
            for (var cursor = folder; cursor != null && !cursor.IsCategory; cursor = cursor.Parent)
                if (cursor.Kind == ItemKind.Folder) depth++;
            return depth;
        }

        /// <summary>Gabarits DISTINCTS appliqués aux écrits du livre — 0 si le
        /// livre en a créé moins de quatre (« créez ET appliquez »).</summary>
        private static int TemplatesApplied(BinderItem book)
        {
            var created = 0;
            var applied = new HashSet<string>();
            foreach (var item in Descendants(book))
            {
                if (item.Kind == ItemKind.PageTemplate) created++;
                else if (item.Kind == ItemKind.Text && item.PageTemplateId != null) applied.Add(item.PageTemplateId);
            }
            return created >= 4 ? applied.Count : 0;
        }

        /// <summary>Livre fini : au moins un écrit, tous terminés, 150 000
        /// signes (espaces comprises) au total, pages extra exclues.</summary>
        private static bool IsFinished(BinderItem book)
        {
            var texts = 0;
            var signs = 0;
            foreach (var item in Descendants(book))
            {
                if (item.Kind != ItemKind.Text || item.IsExtraPage || item.IsToc) continue;
                texts++;
                if (item.Status != "done") return false;
                if (item.Document != null) signs += item.Document.ToPlainText().Length;
            }
            return texts > 0 && signs >= 150000;
        }

        /// <summary>Les parties d'un livre : ses dossiers, à tout niveau.</summary>
        private static int PartCount(BinderItem book)
        {
            var parts = 0;
            foreach (var item in Descendants(book))
                if (item.Kind == ItemKind.Folder) parts++;
            return parts;
        }

        /// <summary>« Gros bêta » : objectif de chapitres posé et atteint,
        /// chaque chapitre à l'état « bêta ».</summary>
        private static bool IsBetaBook(BinderItem book)
        {
            if (book.Book == null || book.Book.ChapterGoal <= 0) return false;
            var chapters = 0;
            foreach (var item in Descendants(book))
            {
                if (item.Kind != ItemKind.Text || item.IsExtraPage || item.IsToc) continue;
                if (item.Status != "beta") return false;
                chapters++;
            }
            return chapters >= book.Book.ChapterGoal;
        }

        /// <summary>« Sniffeur de papier » : format et marges hors du gabarit
        /// par défaut, ET un fond perdu différent des 3 mm.</summary>
        private static bool IsOddBook(BinderItem book)
        {
            if (book.Book == null || book.Book.Template == null) return false;
            return !book.Book.Template.SameLayout(BookInfo.DefaultTemplate())
                && Math.Abs(book.Book.BleedMm - 3) > 0.01;
        }

        private static IEnumerable<BinderItem> Descendants(BinderItem item)
        {
            foreach (var child in item.Children)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
}

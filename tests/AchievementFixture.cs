using System;
using System.Collections.Generic;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>Le projet « tout succès » (22/09/2026) : un seul .plot qui,
    /// ouvert dans Marabook, débloque d'un coup chaque succès mesurable sur
    /// le projet (mots du journal, livres, écrits terminés, fiches, plans,
    /// dictionnaire, annotations, versions…). Sert à la sonde
    /// AchievementsProbe, au test C21 et au projet de test remis à Rémi.
    /// Ce qui n'est PAS ici : les succès à événement (À propos, imprimer,
    /// PDF, EPUB, Yolo, corbeille), les compteurs globaux (zoom, mode calme,
    /// raccourcis, accent) et le temps (série de jours, 8 heures, 2 heures
    /// sans sauvegarder, page blanche 7 jours).</summary>
    public static class AchievementFixture
    {
        public const string BigTextTitle = "Chapitre 1 — le pavé";
        public const string StoriesFolder = "Nouvelles";
        public const string SixthBook = "Sixième livre (cinq chapitres, à jeter)";
        public const string BlankTextTitle = "Page blanche (laissez-la vierge sept jours)";

        /// <summary>Les succès attendus dès l'ouverture, quand les pages du
        /// pavé ont été comptées (« Ça va Aristote ? » a besoin du compte).</summary>
        public static readonly string[] ExpectedOnOpen =
        {
            "ne-quelque-part", "clavier-chaud", "canal-carpien", "mitrailleur", "toucher-de-l-herbe", "time-to-stop",
            "dans-deux-mois", "et-d-un", "check-check-check", "overachiever", "gros-beta", "flashbacks-philo", "debut-de-la-fin",
            "chercheur-debutant", "c-est-simple", "cartographieur", "psychorigide",
            "fouille-merde", "probleme-de-worldbuilding", "aristote",
            "archiviste-debutant", "academicien", "tolkienniste",
            "profiler", "liberticide", "fier-parent", "pas-de-petit-detail", "maitre-planificateur", "genealogiste",
            "jamais-content", "toujours-plus", "complexe-de-dieu", "chirurgien", "accessible", "incertitude",
            "illustrateur", "pointilliste", "sniffeur-de-papier", "sanderson", "speedrunner"
        };

        // Un PNG 1×1 valide (l'image de couverture du roman illustré).
        private static readonly byte[] Pixel = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        public static Project Build()
        {
            var project = Project.CreateNew();
            project.Author = "Rémi";
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();

            // — Page blanche : un écrit vierge (le suivi commence à l'ouverture).
            var blank = new BinderItem { Kind = ItemKind.Text, Title = BlankTextTitle };
            blank.Document.Paragraphs.Add(new TextParagraph());
            writings.Children.Add(blank);

            // — Le roman fini : trois parties, tout terminé, plus de 150 000
            //   signes, un pavé de plus de cent pages, quatre gabarits
            //   appliqués, une image de couverture.
            var novel = new BinderItem { Kind = ItemKind.Book, Title = "Roman fini (trois parties)", Book = new BookInfo() };
            novel.ImageId = project.AddImage(Pixel, ".png");
            var templates = new List<BinderItem>();
            for (var i = 0; i < 4; i++)
            {
                var gabarit = new BinderItem { Kind = ItemKind.PageTemplate, Title = "Gabarit " + (i + 1) };
                templates.Add(gabarit);
                novel.Children.Add(gabarit);
            }
            var big = Text(BigTextTitle, "done", Repeat("Le marabout dort sur la rive, et la rivière emporte les mots du soir. ", 7000));
            big.Page = novel.Book.Template.Clone(); // compté au format du livre (140 × 216)
            big.PageTemplateId = templates[0].Id;
            var parts = new[] { "Partie I", "Partie II", "Partie III" };
            for (var p = 0; p < parts.Length; p++)
            {
                var part = new BinderItem { Kind = ItemKind.Folder, Title = parts[p] };
                if (p == 0) part.Children.Add(big);
                var chapter = Text("Chapitre " + (p + 2), "done", Repeat("Une phrase terminée. ", 40));
                chapter.PageTemplateId = templates[p + 1].Id;
                part.Children.Add(chapter);
                novel.Children.Add(part);
            }
            var dedication = Text("Dédicace", null, "À ceux qui testent.");
            dedication.IsExtraPage = true;
            dedication.ExtraSection = "front";
            novel.Children.Add(dedication);
            writings.Children.Add(novel);

            // — Le livre bêta : quota de deux chapitres, les deux en bêta.
            var beta = new BinderItem { Kind = ItemKind.Book, Title = "Livre bêta", Book = new BookInfo { ChapterGoal = 2 } };
            beta.Children.Add(Text("Bêta 1", "beta", "Premier jet relu."));
            beta.Children.Add(Text("Bêta 2", "beta", "Second jet relu."));
            writings.Children.Add(beta);

            // — Hors format : page élargie et fond perdu de 5 mm.
            var odd = new BinderItem { Kind = ItemKind.Book, Title = "Livre hors format", Book = new BookInfo { BleedMm = 5 } };
            odd.Book.Template.PageWidthMm += 30;
            odd.Children.Add(Text("Format libre", null, "Un livre carré, ou presque."));
            writings.Children.Add(odd);

            // — Complet à 100 % (Complétionniste : à publier en PDF).
            var complete = new BinderItem { Kind = ItemKind.Book, Title = "Livre complet (à publier en PDF)", Book = new BookInfo() };
            complete.Book.AuthorOverride = "Rémi";
            complete.Book.Isbn = "978-2-0000-0000-0";
            complete.Book.Publisher = "Éditions de la sonde";
            complete.Book.Year = "2026";
            complete.Book.Genre = "Roman";
            complete.Book.Audience = "Adultes";
            complete.Book.Themes.Add("Vérification");
            complete.Book.Pitch = "Un livre qui coche toutes les cases.";
            complete.Book.BackCover = "Quatrième de couverture remplie.";
            complete.Children.Add(new BinderItem { Kind = ItemKind.PageTemplate, Title = "Gabarit du complet" });
            var titlePage = Text("Page de titre", null, "Livre complet");
            titlePage.IsExtraPage = true;
            titlePage.ExtraSection = "front";
            complete.Children.Add(titlePage);
            var toc = Text("Table des matières", null, "");
            toc.IsToc = true;
            toc.IsExtraPage = true;
            toc.ExtraSection = "front";
            complete.Children.Add(toc);
            complete.Children.Add(Text("Unique chapitre", "done", "Tout est terminé ici."));
            writings.Children.Add(complete);

            // — Minimaliste : ni couleur, ni objectif, ni état (à publier en PDF).
            var minimal = new BinderItem { Kind = ItemKind.Book, Title = "Livre minimaliste (à publier en PDF)", Book = new BookInfo() };
            minimal.Children.Add(Text("Sans état", null, "Un texte sans étiquette."));
            writings.Children.Add(minimal);

            // — Le sixième livre (Sanderson), cinq chapitres : à envoyer à la
            //   corbeille pour « Ooh la boulette ! ».
            var sixth = new BinderItem { Kind = ItemKind.Book, Title = SixthBook, Book = new BookInfo() };
            for (var i = 1; i <= 5; i++) sixth.Children.Add(Text("Chapitre " + i, null, "Chapitre " + i + " du sixième livre."));
            writings.Children.Add(sixth);

            // — Cent nouvelles terminées (Overachiever), les dix premières épinglées.
            var stories = new BinderItem { Kind = ItemKind.Folder, Title = StoriesFolder };
            for (var i = 1; i <= 100; i++)
            {
                var story = Text("Nouvelle " + i, "done", "nouvelle nouvelle");
                story.Pinned = i <= 10;
                stories.Children.Add(story);
            }
            writings.Children.Add(stories);

            // — Trois dossiers imbriqués (Chirurgien).
            var deep1 = new BinderItem { Kind = ItemKind.Folder, Title = "Dossier 1" };
            var deep2 = new BinderItem { Kind = ItemKind.Folder, Title = "Dossier 2" };
            var deep3 = new BinderItem { Kind = ItemKind.Folder, Title = "Dossier 3" };
            deep2.Children.Add(deep3); deep1.Children.Add(deep2); writings.Children.Add(deep1);

            // — Annoté : 101 annotations et 21 notes de bas de page, ancrées.
            var annotated = Text("Écrit annoté", null, "Chaque mot porte un commentaire.");
            var paragraph = annotated.Document.Paragraphs[0];
            for (var i = 1; i <= 101; i++)
            {
                var annotation = new Annotation { Text = "Remarque " + i, Created = "2026-09-22 10:00" };
                annotated.Document.Annotations.Add(annotation);
                paragraph.Runs.Add(new TextRun { Text = " mot" + i, AnnotationId = annotation.Id });
            }
            for (var i = 1; i <= 21; i++)
            {
                var note = new Footnote { Text = "Note " + i };
                annotated.Document.Footnotes.Add(note);
                paragraph.Runs.Add(new TextRun { Text = i.ToString(), FootnoteId = note.Id });
            }
            writings.Children.Add(annotated);

            // — Dix versions d'un même écrit (Incertitude) : dédoublonnées,
            //   donc un texte différent à chaque capture.
            var versioned = Text("Écrit à dix versions", null, "Version 0.");
            writings.Children.Add(versioned);
            project.RelinkParents();
            for (var i = 1; i <= 10; i++)
            {
                versioned.Document = TextDocument.FromPlainText("Version " + i + ".");
                SnapshotStore.Capture(project, versioned, "v" + i, SnapshotOrigin.Manual, 100);
            }

            // — Trente éléments de recherche.
            var research = project.Category(Project.KeyResearch);
            for (var i = 1; i <= 30; i++) research.Children.Add(Text("Note de recherche " + i, null, "Source " + i + "."));

            // — Six plans, le premier à trente colonnes.
            var plans = project.Category(Project.KeyPlans);
            for (var p = 1; p <= 6; p++)
            {
                var plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan " + p, Plan = new PlanInfo() };
                var columns = p == 1 ? 30 : 3;
                for (var c = 0; c < columns; c++)
                {
                    var column = new PlanColumn { Title = "Colonne " + (c + 1) };
                    column.Entries.Add(new PlanEntry());
                    plan.Plan.Columns.Add(column);
                }
                plans.Children.Add(plan);
            }

            // — Deux cents entrées de dictionnaire.
            for (var i = 1; i <= 200; i++) project.Lexicon.Add(LexiconEntry.Simple("terme" + i));

            // — Dix catégories de fiches.
            var extra = 1;
            while (project.SheetCategories.Count < 10)
                project.SheetCategories.Add(new SheetCategory { Name = "Catégorie personnalisée " + (extra++) });

            // — Cent fiches de personnage, l'auteur, une généalogie de 21
            //   liens, une fiche de 51 champs tous remplis.
            var character = project.SheetCategories[0]; // Personnage
            var other = project.SheetCategories[1];
            var sheets = project.Category(Project.KeySheets);
            var people = new List<BinderItem>();
            for (var i = 1; i <= 100; i++)
            {
                var person = new BinderItem { Kind = ItemKind.Sheet, Title = "Personnage " + i, CategoryId = character.Id, TemplateId = character.TemplateId };
                people.Add(person);
                sheets.Children.Add(person);
            }
            sheets.Children.Add(new BinderItem { Kind = ItemKind.Sheet, Title = "Rémi", CategoryId = character.Id, TemplateId = character.TemplateId });
            var ancestor = new BinderItem { Kind = ItemKind.Sheet, Title = "Aïeul (vingt et un liens)", CategoryId = character.Id, TemplateId = character.TemplateId };
            for (var i = 0; i < 21; i++)
                ancestor.Relations.Add(new SheetRelation { Kind = "child", TargetId = people[i].Id, Name = people[i].Title });
            sheets.Children.Add(ancestor);
            var full = new BinderItem { Kind = ItemKind.Sheet, Title = "Encyclopédie (51 champs remplis)", CategoryId = other.Id };
            for (var i = 1; i <= 51; i++) full.FreeInfo.Add(new InfoEntry { Title = "Champ " + i, Value = "valeur " + i });
            sheets.Children.Add(full);

            // — Le journal : dix millions de mots un jour de janvier.
            project.Journal.Add("2026-01-01", 10000000);
            project.RelinkParents();
            return project;
        }

        /// <summary>Onze fiches, aucune de personnage (« Une vie à peindre »).</summary>
        public static Project BuildWithoutCharacters()
        {
            var project = Project.CreateNew();
            var place = project.SheetCategories[1];
            var sheets = project.Category(Project.KeySheets);
            for (var i = 1; i <= 11; i++)
                sheets.Children.Add(new BinderItem { Kind = ItemKind.Sheet, Title = "Lieu " + i, CategoryId = place.Id, TemplateId = place.TemplateId });
            project.RelinkParents();
            return project;
        }

        public static void Save(Project project, string path)
        {
            PlotFile.Save(project, path);
        }

        public static BinderItem Find(Project project, string title)
        {
            foreach (var item in project.AllItems()) if (item.Title == title) return item;
            return null;
        }

        private static BinderItem Text(string title, string status, string body)
        {
            var item = new BinderItem { Kind = ItemKind.Text, Title = title, Status = status };
            item.Document = TextDocument.FromPlainText(body);
            return item;
        }

        private static string Repeat(string text, int times)
        {
            var builder = new System.Text.StringBuilder(text.Length * times);
            for (var i = 0; i < times; i++) builder.Append(text);
            return builder.ToString();
        }
    }
}

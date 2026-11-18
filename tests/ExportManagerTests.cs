using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Print;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C80 — le gestionnaire d'export (2.0.0, 18/11) : les
    /// préréglages de la maison et ceux de l'utilisateur (export.json), le
    /// plan (ordre de lecture, ce que le format écarte, avertissements), les
    /// informations du livre, les flux (segments par mise en page, titres,
    /// liens relevés, fiches), la mise en forme du manuscrit papier, les
    /// sections Word, les noms de styles uniques (bug « Titre 1 »), la page
    /// A5 (bug « A5 exporté en A4 ») et le PDF en plusieurs parties.</summary>
    public static class ExportManagerTests
    {
        private sealed class World
        {
            public Project Project;
            public BinderItem Book, Part, Chapter1, Chapter2, Chapter3, Outside, Single, Toc;
            public BinderItem Keira, Marabout, Plan;
        }

        public static void Run(Harness t)
        {
            t.Suite("C80 — gestionnaire d'export");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c80-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var settingsBefore = AppSettings.PathOverride;
            try
            {
                Presets(t);
                Store(t, dir);
                var world = Fixture();
                Plans(t, world);
                Meta(t, world);
                Flows(t, world);
                Typography(t, world);
                Word(t, world, dir);
                Pdf(t, world);
            }
            finally
            {
                AppSettings.PathOverride = settingsBefore;
                ExportPresetStore.Reset();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ------------------------------------------------------------ fixture

        private static void Add(BinderItem parent, BinderItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        private static BinderItem Text(string title, string body)
        {
            return new BinderItem { Kind = ItemKind.Text, Title = title, Document = TextDocument.FromPlainText(body) };
        }

        private static World Fixture()
        {
            var w = new World { Project = Project.CreateNew() };
            var project = w.Project;
            project.Name = "Projet d'essai";
            project.Author = "Autrice d'essai";
            var writings = project.Category(Project.KeyWritings);
            w.Single = writings.Children[0];
            w.Single.Title = "Nouvelle seule";
            w.Single.Document = TextDocument.FromPlainText("Une nouvelle où passe [[Keira]] puis [[Marabout|le vieux]].");

            w.Book = new BinderItem { Kind = ItemKind.Book, Title = "Le livre", Book = new BookInfo() };
            w.Book.Book.Subtitle = "Roman";
            w.Book.Book.Isbn = "978-2-1234-5680-3";
            w.Book.Book.Publisher = "Éditions de la sonde";
            w.Book.Book.Year = "2026";
            w.Book.Book.BleedMm = 5;
            w.Book.Book.Template = new PageSetup { PageWidthMm = 148, PageHeightMm = 210, MarginTopMm = 15, MarginBottomMm = 15, MarginLeftMm = 18, MarginRightMm = 12 };
            Add(writings, w.Book);
            w.Toc = Text("Table des matières", "");
            w.Toc.IsExtraPage = true; w.Toc.IsToc = true; w.Toc.ExtraSection = ExtraPages.SectionFront; w.Toc.ExtraKind = ExtraPages.KindToc;
            Add(w.Book, w.Toc);
            w.Chapter1 = Text("Chapitre 1", "Premier chapitre, en A5.");
            w.Chapter1.Page = w.Book.Book.Template.Clone();
            Add(w.Book, w.Chapter1);
            w.Part = new BinderItem { Kind = ItemKind.Folder, Title = "Partie II" };
            Add(w.Book, w.Part);
            w.Chapter2 = Text("Chapitre 2", "Dans la partie.");
            w.Chapter2.Page = w.Book.Book.Template.Clone();
            Add(w.Part, w.Chapter2);
            w.Chapter3 = Text("Chapitre 3", "Après la partie, en A4 par erreur.");
            w.Chapter3.Page = new PageSetup(); // A4 : pas la mise en page du gabarit
            Add(w.Book, w.Chapter3);
            var outside = new BinderItem { Kind = ItemKind.Folder, Title = BinderItem.OutOfBookTitle, IsOutOfBook = true };
            Add(w.Book, outside);
            w.Outside = Text("Notes hors livre", "Jamais publié.");
            Add(outside, w.Outside);

            var sheets = project.Category(Project.KeySheets);
            var category = project.SheetCategories.Count > 0 ? project.SheetCategories[0] : null;
            w.Keira = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", CategoryId = category == null ? null : category.Id, TemplateId = category == null ? null : category.TemplateId };
            w.Keira.Document = TextDocument.FromPlainText("Elle connaît [[Marabout]] depuis l'enfance.");
            Add(sheets, w.Keira);
            w.Marabout = new BinderItem { Kind = ItemKind.Sheet, Title = "Marabout", CategoryId = category == null ? null : category.Id, TemplateId = category == null ? null : category.TemplateId };
            w.Marabout.Document = TextDocument.FromPlainText("Un oiseau qui écrit.");
            Add(sheets, w.Marabout);
            w.Keira.Relations.Add(new SheetRelation { Kind = "Ami", TargetId = w.Marabout.Id });

            w.Plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan du roman", Plan = new PlanInfo { LinkedItemId = w.Book.Id } };
            Add(project.Category(Project.KeyPlans), w.Plan);
            return w;
        }

        private static List<string> Ids(params BinderItem[] items)
        {
            var ids = new List<string>();
            foreach (var item in items) ids.Add(item.Id);
            return ids;
        }

        private static ExportPreset BuiltIn(string id)
        {
            return ExportPresets.Find(ExportPresets.BuiltIn(), id).Clone();
        }

        // ------------------------------------------------------------ préréglages

        private static void Presets(Harness t)
        {
            var builtIn = ExportPresets.BuiltIn();
            t.Equal(9, builtIn.Count, "neuf préréglages de la maison");
            var ids = new HashSet<string>();
            foreach (var preset in builtIn)
            {
                ids.Add(preset.Id);
                t.Check(preset.BuiltIn && preset.Note.Length > 40, "« " + preset.Name + " » : de la maison, avec sa ligne d'explication");
                t.Check(Array.IndexOf(ExportPresets.Categories, preset.Category) >= 0 && preset.Category != ExportPresets.CategoryCustom, "« " + preset.Name + " » : rangé dans une catégorie de la maison");
            }
            t.Equal(9, ids.Count, "des identifiants tous distincts");
            t.Equal("Document Word A4", ExportPresets.Find(builtIn, ExportPresets.WordA4).Name, "le premier : Document Word A4");
            t.Equal(ExportPages.Source, ExportPresets.Find(builtIn, ExportPresets.WordLayout).Pages, "Word mis en forme : la mise en page de chaque texte");
            t.Equal(ExportPresets.CategoryMixed, ExportPresets.Find(builtIn, ExportPresets.Bfp).Category, "le BFP : plusieurs types de documents");
            var bat = ExportPresets.Find(builtIn, ExportPresets.Bat);
            t.Check(bat.Cmyk && bat.CropMarks && bat.BookBleed && bat.RectoStarts && bat.CheckConsistency && bat.Pages == ExportPages.Book && !bat.ChapterHeadings, "BAT Imprimeur : ce que faisait « Publier », et il avertit");
            var manuscript = ExportPresets.Find(builtIn, ExportPresets.Manuscript);
            t.Check(manuscript.Pages == ExportPages.A4 && manuscript.PlainMargins && Math.Abs(manuscript.LineSpacing - 1.25) < 0.001 && Math.Abs(manuscript.MaxFontPt - 12) < 0.001 && !manuscript.HeadersFooters, "Manuscrit papier : A4, marges sans miroir, interligne 1,25, 12 points au plus, un seul folio");
            var encyclopedia = ExportPresets.Find(builtIn, ExportPresets.EncyclopediaPdf);
            t.Check(encyclopedia.Links && encyclopedia.Outline && encyclopedia.Format == ExportFormats.Pdf, "PDF Encyclopédique : liens et signets");
            t.Check(ExportPresets.Find(builtIn, ExportPresets.EpubPublish).Cover && !ExportPresets.Find(builtIn, ExportPresets.Epub).Cover, "EPUB Publiable : la couverture ; EPUB simple : sans");

            // Ce que porte chaque format.
            t.Check(ExportFormats.Carries(ExportFormats.Pdf, ItemKind.Plan) && !ExportFormats.Carries(ExportFormats.Docx, ItemKind.Plan), "un plan : dans un PDF seulement");
            t.Check(ExportFormats.Carries(ExportFormats.Docx, ItemKind.Sheet) && !ExportFormats.Carries(ExportFormats.Epub, ItemKind.Sheet), "une fiche : partout sauf dans un EPUB");
            t.Equal("Document Word (*.docx)|*.docx", ExportFormats.Filter(ExportFormats.Docx), "le filtre d'enregistrement Word");
            t.Equal("A5", ExportPages.Describe(new PageSetup { PageWidthMm = 148, PageHeightMm = 210 }), "148 × 210 se dit A5");
            t.Equal("A4 paysage", ExportPages.Describe(new PageSetup { PageWidthMm = 297, PageHeightMm = 210 }), "297 × 210 se dit A4 paysage");

            // L'empreinte et le JSON.
            var copy = bat.Clone();
            t.Equal(bat.Signature(), copy.Signature(), "une copie a la même empreinte");
            copy.Name = "Autre nom"; copy.Note = "Autre note";
            t.Equal(bat.Signature(), copy.Signature(), "le nom et la note ne font pas l'empreinte");
            copy.Cmyk = false;
            t.Check(bat.Signature() != copy.Signature(), "une option changée : une autre empreinte");
            var round = ExportPreset.FromJson(Json.Parse(Json.Write(manuscript.ToJson())));
            t.Equal(manuscript.Signature(), round.Signature(), "aller-retour JSON : mêmes options");
            t.Check(!round.BuiltIn && round.Category == ExportPresets.CategoryCustom, "un préréglage relu est personnalisé");
        }

        private static void Store(Harness t, string dir)
        {
            AppSettings.PathOverride = Path.Combine(dir, "settings.json");
            ExportPresetStore.Reset();
            t.Equal(0, ExportPresetStore.Custom.Count, "un dossier de réglages vierge : aucun préréglage personnalisé");
            t.Equal(9, ExportPresetStore.All().Count, "…et les neuf de la maison");
            var options = BuiltIn(ExportPresets.PdfA4);
            options.TitlePage = true;
            var saved = ExportPresetStore.SaveAs(options, "  Mon PDF  ", "");
            t.Check(!saved.BuiltIn && saved.Name == "Mon PDF" && saved.Id != ExportPresets.PdfA4 && saved.Category == ExportPresets.CategoryCustom, "enregistré : personnalisé, nom nettoyé, identifiant neuf");
            t.Check(File.Exists(ExportPresetStore.FilePath()), "export.json écrit à côté des réglages");
            options.Cmyk = true;
            var again = ExportPresetStore.SaveAs(options, "mon pdf", "");
            t.Check(again.Id == saved.Id && ExportPresetStore.Custom.Count == 1 && ExportPresetStore.Custom[0].Cmyk, "le même nom (sans la casse) : remplacé, pas doublé");
            ExportPresetStore.LastId = saved.Id;
            ExportPresetStore.Reset();
            t.Check(ExportPresetStore.Custom.Count == 1 && ExportPresetStore.Custom[0].TitlePage && ExportPresetStore.Custom[0].Cmyk, "relu du disque : les options y sont");
            t.Equal(saved.Id, ExportPresetStore.LastId, "le dernier préréglage employé est retenu");
            t.Equal("Mon PDF (2)", ExportPresets.FreeName(ExportPresetStore.All(), "Mon PDF"), "un nom libre : « Mon PDF (2) »");
            t.Check(ExportPresetStore.Remove(saved.Id) && ExportPresetStore.Custom.Count == 0 && ExportPresetStore.LastId == null, "supprimé : il part, et le « dernier » avec lui");
            File.WriteAllText(ExportPresetStore.FilePath(), "{ ceci n'est pas du json");
            ExportPresetStore.Reset();
            t.Equal(9, ExportPresetStore.All().Count, "un export.json illisible ne casse rien");
        }

        // ------------------------------------------------------------ plan

        private static void Plans(Harness t, World w)
        {
            var all = Ids(w.Marabout, w.Plan, w.Chapter3, w.Single, w.Chapter2, w.Keira, w.Chapter1, w.Toc);

            var word = ExportPlan.Build(w.Project, all, BuiltIn(ExportPresets.WordA4), null);
            t.Equal(5, word.Texts.Count, "Word : les cinq écrits cochés");
            t.Check(word.Texts[0] == w.Single && word.Texts[1] == w.Toc && word.Texts[2] == w.Chapter1 && word.Texts[3] == w.Chapter2 && word.Texts[4] == w.Chapter3,
                "dans l'ordre de lecture de la Pile, quel que soit l'ordre des coches — la partie traversée à sa place");
            t.Equal(2, word.Sheets.Count, "Word : les deux fiches");
            t.Check(word.Skipped.Count == 1 && word.Skipped[0] == w.Plan && word.Plans.Count == 0, "Word : le plan est écarté");
            t.Check(word.Book == null && word.Books.Count == 1, "un écrit hors du livre : pas de livre unique");
            t.Equal("5 écrits, 2 fiches", word.Summary(), "le résumé du plan");
            t.Check(HasWarning(word, "Écarté par le format Word"), "…et le plan le dit");
            t.Check(HasWarning(word, "recoulé"), "A4 imposé : les écrits en A5 seront recoulés, c'est dit");

            var bfp = ExportPlan.Build(w.Project, all, BuiltIn(ExportPresets.Bfp), null);
            t.Check(bfp.Plans.Count == 1 && bfp.Skipped.Count == 0 && bfp.Count == 8, "BFP : tout part, le plan aussi");
            t.Check(HasWarning(bfp, "Plusieurs formats de page"), "BFP : les formats mêlés sont signalés");

            var epub = ExportPlan.Build(w.Project, all, BuiltIn(ExportPresets.Epub), null);
            t.Check(epub.Sheets.Count == 0 && epub.Skipped.Count == 3, "EPUB : fiches et plan écartés");
            t.Check(epub.Dynamic.Count == 1 && epub.Dynamic[0] == w.Toc && epub.Texts.Count == 4, "EPUB : la table des matières (page dynamique) écartée");

            var bookTexts = Ids(w.Toc, w.Chapter1, w.Chapter2, w.Chapter3);
            var bat = ExportPlan.Build(w.Project, bookTexts, BuiltIn(ExportPresets.Bat), ExportMeta.For(w.Project, w.Book, null));
            t.Check(bat.Book == w.Book, "BAT : le livre de l'ampleur");
            t.Check(HasWarning(bat, "n'a pas la mise en page du gabarit") && bat.WarnCount == 1, "BAT : l'écrit resté en A4 est signalé, et rien d'autre");
            var partial = ExportPlan.Build(w.Project, Ids(w.Chapter1, w.Chapter2), BuiltIn(ExportPresets.Bat), null);
            t.Check(HasWarning(partial, "Le livre n'est pas entier"), "BAT : un livre incomplet est signalé");
            var overflow = ExportPlan.Build(w.Project, Ids(w.Chapter1, w.Single), BuiltIn(ExportPresets.Bat), null);
            t.Check(overflow.Book == null && HasWarning(overflow, "déborde d'un seul livre"), "BAT : une ampleur qui déborde du livre est signalée");

            var publish = ExportPlan.Build(w.Project, Ids(w.Chapter1, w.Chapter2, w.Chapter3), BuiltIn(ExportPresets.EpubPublish), ExportMeta.For(w.Project, w.Book, null));
            t.Check(HasWarning(publish, "Pas de couverture"), "EPUB Publiable : la couverture manquante est signalée");
            t.Check(!HasWarning(publish, "Il manque"), "…le titre et l'auteur·ice viennent du livre");
            var bare = ExportPlan.Build(w.Project, Ids(w.Chapter1, w.Chapter2, w.Chapter3), BuiltIn(ExportPresets.EpubPublish), new ExportMeta());
            t.Check(HasWarning(bare, "Il manque le titre et l'auteur·ice"), "EPUB Publiable sans informations : titre et auteur·ice réclamés");

            var empty = ExportPlan.Build(w.Project, new List<string>(), BuiltIn(ExportPresets.WordA4), null);
            t.Check(empty.IsEmpty && empty.WarnCount == 1, "rien de coché : un plan vide, qui le dit");
            t.Equal("Chapitre 1", ExportPlan.Build(w.Project, Ids(w.Chapter1), BuiltIn(ExportPresets.WordA4), null).DefaultName(w.Project, null), "un écrit seul : le fichier porte son nom");
            t.Equal("Le livre", bat.DefaultName(w.Project, ExportMeta.For(w.Project, w.Book, null)), "un livre : le fichier porte son titre");

            // La mise en page d'un écrit selon le préréglage.
            t.Equal("A4", ExportPages.Describe(ExportPlan.PageOf(w.Chapter1, w.Project, BuiltIn(ExportPresets.WordA4))), "Word A4 : A4, quel que soit l'écrit");
            t.Equal("A5", ExportPages.Describe(ExportPlan.PageOf(w.Chapter1, w.Project, BuiltIn(ExportPresets.WordLayout))), "Word mis en forme : l'écrit en A5 reste en A5");
            t.Equal("A5", ExportPages.Describe(ExportPlan.PageOf(w.Chapter3, w.Project, BuiltIn(ExportPresets.Bat))), "BAT : le gabarit du livre, même pour l'écrit resté en A4");
            var plain = BuiltIn(ExportPresets.WordLayout);
            plain.PlainMargins = true;
            var page = ExportPlan.PageOf(w.Chapter1, w.Project, plain);
            t.Check(Math.Abs(page.MarginLeftMm - 15) < 0.01 && Math.Abs(page.MarginRightMm - 15) < 0.01, "marges sans miroir : petit fond et grand fond à leur moyenne");
        }

        private static bool HasWarning(ExportPlan plan, string fragment)
        {
            foreach (var warning in plan.Warnings) if (warning.Text.IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // ------------------------------------------------------------ informations du livre

        private static void Meta(Harness t, World w)
        {
            var meta = ExportMeta.For(w.Project, w.Book, null);
            t.Check(meta.Title == "Le livre" && meta.Subtitle == "Roman" && meta.Author == "Autrice d'essai" && meta.Isbn == "978-2-1234-5680-3" && meta.Publisher == "Éditions de la sonde" && meta.Year == "2026",
                "pré-remplies depuis le livre : titre, sous-titre, auteur·ice, ISBN, éditeur, année");
            t.Equal("Éditions de la sonde — 2026 — ISBN 978-2-1234-5680-3", meta.Colophon(), "la ligne d'édition de la page de titre");
            var free = ExportMeta.For(w.Project, null, "Nouvelle seule");
            t.Check(free.Title == "Nouvelle seule" && free.Subtitle == "" && free.Author == "Autrice d'essai", "sans livre : le titre donné et l'auteur·ice du projet");

            var copy = meta.Clone();
            t.Check(!copy.ApplyTo(w.Book, w.Project), "reporter sans rien changer : le livre ne bouge pas");
            copy.Subtitle = "Roman d'aventure";
            copy.Year = "2027";
            t.Check(copy.ApplyTo(w.Book, w.Project) && w.Book.Book.Subtitle == "Roman d'aventure" && w.Book.Book.Year == "2027", "reporter : le sous-titre et l'année passent dans le livre");
            t.Check(w.Book.Book.AuthorOverride == "" && w.Book.Book.Title == "", "…sans figer l'auteur·ice ni le titre hérités");
            w.Book.Book.Subtitle = "Roman";
            w.Book.Book.Year = "2026";
        }

        // ------------------------------------------------------------ flux

        private static void Flows(Harness t, World w)
        {
            var ids = Ids(w.Single, w.Chapter1, w.Chapter2, w.Chapter3, w.Keira, w.Marabout, w.Plan);

            // Word mis en forme : un segment par mise en page.
            var layout = BuiltIn(ExportPresets.WordLayout);
            var plan = ExportPlan.Build(w.Project, ids, layout, null);
            var segments = ExportFlow.Build(w.Project, plan, layout, null);
            t.Equal(4, segments.Count, "Word mis en forme : A4, A5, A4, puis les fiches — quatre segments");
            t.Check(ExportPages.Describe(segments[0].Setup) == "A4" && ExportPages.Describe(segments[1].Setup) == "A5" && ExportPages.Describe(segments[2].Setup) == "A4", "chaque suite d'écrits garde son format");
            t.Check(segments[1].Items.Count == 2 && segments[1].Items[0] == w.Chapter1 && segments[1].Items[1] == w.Chapter2, "les deux chapitres en A5 partagent un segment");
            t.Equal("Chapitre 1", segments[1].Document.Paragraphs[0].ToPlainText(), "plusieurs écrits : le titre de chacun en tête");
            t.Equal("title2", segments[1].Document.Paragraphs[0].StyleId, "…en Titre 2");
            t.Check(segments[1].Marks.Count == 2 && segments[1].Marks[0].Paragraph == 0 && segments[1].Marks[0].Name == w.Chapter1.Id && segments[1].Marks[1].Title == "Chapitre 2", "un repère par écrit, à son titre");
            t.Equal("Chapitre 2", segments[1].Document.Paragraphs[segments[1].Marks[1].Paragraph].ToPlainText(), "le repère du second écrit tombe sur son titre");
            t.Check(segments[1].Document.Paragraphs[segments[1].Marks[1].Paragraph].PageBreakBefore, "le titre ouvre la nouvelle page, pas le premier paragraphe");
            t.Check(!segments[1].Document.Paragraphs[segments[1].Marks[1].Paragraph + 1].PageBreakBefore, "…qui ne saute donc pas une page de plus");
            t.Check(segments[0].Document.ToPlainText().IndexOf("[[", StringComparison.Ordinal) < 0, "les marques de [[liens]] tombent");
            t.Check(segments[0].Document.ToPlainText().Contains("où passe Keira puis le vieux."), "…les mots restent");
            t.Equal(0, segments[0].Links.Count, "sans liens au préréglage : aucun lien relevé");
            t.Check(segments[3].IsSheets && !segments[3].IsImage, "les fiches : un flux de texte");
            t.Check(plan.Skipped.Count == 1, "le plan, écarté par Word, ne fait pas de segment");

            // Un écrit seul : ni titre ajouté, ni saut de page.
            var alone = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Chapter1), layout, null), layout, null);
            t.Check(alone.Count == 1 && alone[0].Document.Paragraphs.Count == 1 && alone[0].Document.Paragraphs[0].ToPlainText() == "Premier chapitre, en A5.", "un écrit seul sort tel quel");

            // Un écrit VIDE au milieu (plantage relevé par la sonde, 18/11) : son titre seul, sur sa page.
            var hollow = Text("Chapitre vide", "");
            hollow.Document.Paragraphs.Clear();
            hollow.Page = w.Book.Book.Template.Clone();
            hollow.Parent = w.Book;
            w.Book.Children.Insert(w.Book.Children.IndexOf(w.Part), hollow);
            var holed = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Chapter1, hollow, w.Chapter2), layout, null), layout, null);
            var lines = new List<string>();
            foreach (var p in holed[0].Document.Paragraphs) lines.Add((p.PageBreakBefore ? "§" : "") + p.ToPlainText());
            t.Equal("Chapitre 1|Premier chapitre, en A5.|§Chapitre vide|§Chapitre 2|Dans la partie.", string.Join("|", lines.ToArray()), "un écrit vide : son titre sur sa page, sans voler le saut de page du suivant");
            t.Equal("Chapitre vide", holed[0].Document.Paragraphs[holed[0].Marks[1].Paragraph].ToPlainText(), "…et son repère tombe sur son titre");
            var bare = BuiltIn(ExportPresets.Bat);
            var hollowOnly = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Chapter1, hollow), bare, null), bare, null);
            t.Check(hollowOnly[0].Marks[1].Paragraph < hollowOnly[0].Document.Paragraphs.Count, "sans titres : le repère d'un écrit vide en fin de flux reste dans le document");
            w.Book.Children.Remove(hollow);

            // Word A4 : tout dans un seul flux d'écrits.
            var a4 = BuiltIn(ExportPresets.WordA4);
            var flat = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, a4, null), a4, null);
            t.Check(flat.Count == 2 && flat[0].Items.Count == 4 && ExportPages.Describe(flat[0].Setup) == "A4", "Word A4 : les quatre écrits dans un seul flux en A4, puis les fiches");

            // Numérotation et page de titre.
            var numbered = BuiltIn(ExportPresets.WordA4);
            numbered.NumberChapters = true;
            numbered.TitlePage = true;
            var meta = ExportMeta.For(w.Project, w.Book, null);
            var titled = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Chapter1, w.Chapter2), numbered, meta), numbered, meta);
            var paragraphs = titled[0].Document.Paragraphs;
            t.Check(paragraphs[0].ToPlainText() == "Le livre" && paragraphs[1].ToPlainText() == "Roman" && paragraphs[2].ToPlainText() == "Autrice d'essai", "page de titre : titre, sous-titre, auteur·ice");
            t.Equal("1. Chapitre 1", paragraphs[titled[0].Marks[0].Paragraph].ToPlainText(), "chapitres numérotés");
            t.Check(paragraphs[titled[0].Marks[0].Paragraph].PageBreakBefore, "après la page de titre, le premier chapitre ouvre une page");

            // PDF Encyclopédique : les liens relevés, à leur place.
            var pdf = BuiltIn(ExportPresets.EncyclopediaPdf);
            var linked = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Single, w.Keira, w.Marabout), pdf, null), pdf, null);
            t.Equal(2, linked.Count, "PDF Encyclopédique : l'écrit, puis les fiches");
            t.Equal(2, linked[0].Links.Count, "l'écrit : ses deux liens vers des fiches exportées");
            var text = linked[0].Document.Paragraphs[linked[0].Links[0].Paragraph].ToPlainText();
            t.Equal("Keira", text.Substring(linked[0].Links[0].Start, linked[0].Links[0].Length), "le premier lien couvre « Keira » dans le texte nettoyé");
            t.Equal("le vieux", text.Substring(linked[0].Links[1].Start, linked[0].Links[1].Length), "le second couvre « le vieux »");
            t.Check(linked[0].Links[0].Target == w.Keira.Id && linked[0].Links[1].Target == w.Marabout.Id, "…et visent les bonnes fiches");
            var sheets = linked[1];
            var body = sheets.Document.ToPlainText();
            t.Check(body.Contains("Keira") && body.Contains("Elle connaît Marabout depuis l'enfance.") && body.Contains("Relations") && body.Contains("Ami : Marabout"), "les fiches : titre, texte, relations");
            var toMarabout = 0;
            foreach (var link in sheets.Links)
            {
                if (link.Target != w.Marabout.Id) continue;
                toMarabout++;
                var line = sheets.Document.Paragraphs[link.Paragraph].ToPlainText();
                t.Equal("Marabout", line.Substring(link.Start, link.Length), "un lien de fiche couvre le nom visé");
            }
            t.Equal(2, toMarabout, "Keira mène deux fois à Marabout : dans son texte et dans ses relations");
            var named = 0;
            foreach (var mark in sheets.Marks) if (mark.Name == w.Keira.Id || mark.Name == w.Marabout.Id) named++;
            t.Equal(2, named, "chaque fiche est une destination");
            var top = sheets.Marks[0];
            t.Check(top.Title == "Fiches" && top.Level == 0 && top.Name == null, "avec des écrits : les fiches sous un signet « Fiches »");
            var only = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Keira), pdf, null), pdf, null);
            t.Check(only.Count == 1 && only[0].Marks[0].Title == "Keira" && only[0].Links.Count == 0, "une fiche seule : pas de signet « Fiches », et ses liens vers une fiche absente ne sont pas relevés");

            // BFP : le plan part en image.
            var bfp = BuiltIn(ExportPresets.Bfp);
            var mixed = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, bfp, null), bfp, null);
            var last = mixed[mixed.Count - 1];
            t.Check(last.IsImage && last.Item == w.Plan && last.Marks[0].Title == "Plan du roman", "BFP : le plan fait un segment image, avec son signet");

            // BAT : le gabarit, les belles pages.
            var bat = BuiltIn(ExportPresets.Bat);
            var book = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Chapter1, w.Chapter2, w.Chapter3), bat, null), bat, null);
            t.Check(book.Count == 1 && ExportPages.Describe(book[0].Setup) == "A5", "BAT : un seul flux, au gabarit du livre");
            t.Check(book[0].Document.Paragraphs.Count == 3 && book[0].Document.Paragraphs[1].StartOnRecto && book[0].Document.Paragraphs[1].Decor != null, "BAT : pas de titres ajoutés, chaque écrit en belle page, avec son décor");
        }

        // ------------------------------------------------------------ manuscrit papier

        private static void Typography(Harness t, World w)
        {
            var manuscript = BuiltIn(ExportPresets.Manuscript);
            var styles = ExportTypography.Styles(w.Project.Styles, manuscript);
            t.Check(!ReferenceEquals(styles, w.Project.Styles), "la feuille de l'export est une copie");
            t.Check(Math.Abs(styles.Find("title1").FontSize - 16) < 0.01 && Math.Abs(styles.Find("title2").FontSize - 16) < 0.01, "les titres reviennent à 12 points");
            t.Check(Math.Abs(w.Project.Styles.Find("title1").FontSize - 26) < 0.01, "…ceux du projet ne bougent pas");
            t.Check(Math.Abs(styles.Body.LineHeight - 20) < 0.01, "interligne 1,25 : 15 points pour un corps de 12");
            t.Check(styles.Find("title1").SpaceBefore < w.Project.Styles.Find("title1").SpaceBefore, "les espaces des titres suivent la réduction");
            t.Check(ReferenceEquals(ExportTypography.Styles(w.Project.Styles, BuiltIn(ExportPresets.PdfA4)), w.Project.Styles), "sans plafond ni interligne : la feuille telle quelle");

            var document = new TextDocument { LineSpacing = 1.5 };
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "Énorme", FontSize = 40, Bold = true });
            paragraph.Runs.Add(new TextRun { Text = " normal" });
            document.Paragraphs.Add(paragraph);
            var capped = ExportTypography.Document(document, manuscript);
            t.Check(Math.Abs(capped.Paragraphs[0].Runs[0].FontSize.Value - 16) < 0.01 && capped.Paragraphs[0].Runs[0].Bold == true, "une taille posée à la main revient à 12 points, le gras reste");
            t.Check(Math.Abs(document.Paragraphs[0].Runs[0].FontSize.Value - 40) < 0.01, "…l'écrit lui-même ne bouge pas");
            t.Check(Math.Abs(capped.LineSpacing - 1) < 0.001, "l'interligne du document cède à celui du préréglage");

            var flows = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Chapter1, w.Chapter3), manuscript, null), manuscript, null);
            t.Check(flows.Count == 1 && ExportPages.Describe(flows[0].Setup) == "A4" && flows[0].Setup.FooterPageNumbers, "manuscrit : un seul flux en A4, folio centré");
            t.Check(Math.Abs(flows[0].Setup.MarginLeftMm - flows[0].Setup.MarginRightMm) < 0.01, "manuscrit : marges de côté égales");
            var decorated = false;
            foreach (var p in flows[0].Document.Paragraphs) if (p.Decor != null) decorated = true;
            t.Check(!decorated, "manuscrit : aucun en-tête de gabarit, une seule pagination");
        }

        // ------------------------------------------------------------ Word

        private static string Entry(string path, string name)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var entry = zip.GetEntry(name);
                if (entry == null) return null;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        private static int CountOf(string text, string fragment)
        {
            var count = 0;
            for (var at = text.IndexOf(fragment, StringComparison.Ordinal); at >= 0; at = text.IndexOf(fragment, at + fragment.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        private static void Word(Harness t, World w, string dir)
        {
            // BUG (18/11, Rémi) : un écrit en A5 sortait en A4 — l'export passait la page du PROJET.
            var layout = BuiltIn(ExportPresets.WordLayout);
            var single = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Chapter1), layout, null), layout, null);
            var path = Path.Combine(dir, "a5.docx");
            Docx.Export(single[0].Document, single[0].Styles, path, single[0].Setup, "moi", w.Project);
            var xml = Entry(path, "word/document.xml");
            t.Check(xml.Contains("<w:pgSz w:w=\"8391\" w:h=\"11906\"/>"), "un écrit en A5 sort en A5 dans Word (148 × 210 mm)");
            t.Check(xml.Contains("w:left=\"1020\"") && xml.Contains("w:right=\"680\""), "…avec ses marges (18 et 12 mm)");

            // Les sections : une par mise en page.
            var ids = Ids(w.Single, w.Chapter1, w.Chapter2, w.Chapter3);
            var segments = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, layout, null), layout, null);
            var merged = new TextDocument();
            var extras = new DocxExtras { Title = "Le livre & co", Author = "Autrice d'essai" };
            PageSetup last = null;
            foreach (var segment in segments)
            {
                extras.Sections.Add(new DocxSection { StartParagraph = merged.Paragraphs.Count, Setup = segment.Setup });
                merged.Paragraphs.AddRange(segment.Document.Paragraphs);
                last = segment.Setup;
            }
            path = Path.Combine(dir, "sections.docx");
            Docx.Export(merged, segments[0].Styles, path, last, "moi", w.Project, extras);
            xml = Entry(path, "word/document.xml");
            t.Equal(3, CountOf(xml, "<w:sectPr>"), "trois mises en page : trois sections Word");
            t.Equal(2, CountOf(xml, "<w:pgSz w:w=\"11906\" w:h=\"16838\"/>"), "deux sections en A4");
            t.Equal(1, CountOf(xml, "<w:pgSz w:w=\"8391\" w:h=\"11906\"/>"), "une section en A5");
            t.Check(xml.IndexOf("<w:pgSz w:w=\"11906\"", StringComparison.Ordinal) < xml.IndexOf("<w:pgSz w:w=\"8391\"", StringComparison.Ordinal), "l'A4 de l'écrit seul vient d'abord, l'A5 du livre ensuite");
            t.Check(xml.TrimEnd().EndsWith("</w:sectPr></w:body></w:document>"), "la dernière section clôt le corps");
            t.Equal(2, CountOf(xml, "</w:sectPr></w:pPr>"), "les deux premières sections voyagent dans le dernier paragraphe qu'elles couvrent");
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);
            t.Check(doc.DocumentElement != null, "document.xml reste bien formé");
            var core = Entry(path, "docProps/core.xml");
            t.Check(core != null && core.Contains("<dc:title>Le livre &amp; co</dc:title>") && core.Contains("<dc:creator>Autrice d'essai</dc:creator>"), "le titre et l'auteur·ice du fichier (échappés)");
            t.Check(Entry(path, "[Content_Types].xml").Contains("/docProps/core.xml") && Entry(path, "_rels/.rels").Contains("docProps/core.xml"), "…déclarés au paquet");
            t.Check(Entry(Path.Combine(dir, "a5.docx"), "docProps/core.xml") == null, "sans titre ni auteur : pas de propriétés");

            // BUG (18/11, Rémi) : « un titre en Titre 1, la police ne s'exporte pas » — deux styles du
            // même nom (le Titre 1 global et celui d'un livre, ou d'un import) que Word fusionne.
            var styles = w.Project.Styles.Clone();
            var own = styles.Find("title1").Clone();
            own.Id = "t1livre"; own.Scope = ParagraphStyle.ScopeBook; own.OwnerId = w.Book.Id; own.FontFamily = "Garamond";
            styles.Styles.Add(own);
            var ghost = styles.Find("title2").Clone();
            ghost.Id = "t2fantome"; ghost.FontFamily = "Impact";
            styles.Styles.Add(ghost);
            var titled = new TextDocument();
            var first = new TextParagraph { StyleId = "t1livre" }; first.Runs.Add(new TextRun { Text = "Le titre du livre" }); titled.Paragraphs.Add(first);
            var second = new TextParagraph { StyleId = "title1" }; second.Runs.Add(new TextRun { Text = "Le titre global" }); titled.Paragraphs.Add(second);
            var third = new TextParagraph { StyleId = "title2" }; third.Runs.Add(new TextRun { Text = "Un sous-titre" }); titled.Paragraphs.Add(third);
            var names = Docx.StyleNames(styles, titled);
            t.Check(names["title1"] == "Titre 1" && names["t1livre"] == "Titre 1 (2)", "deux « Titre 1 » employés : le second prend un rang, chacun garde sa police");
            t.Check(names["title2"] == "Titre 2" && !names.ContainsKey("t2fantome"), "un homonyme inemployé n'est pas écrit");
            path = Path.Combine(dir, "titres.docx");
            Docx.Export(titled, styles, path, new PageSetup(), "moi", w.Project);
            var stylesXml = Entry(path, "word/styles.xml");
            t.Equal(1, CountOf(stylesXml, "<w:name w:val=\"Titre 1\"/>"), "styles.xml : un seul style nommé « Titre 1 »");
            t.Check(stylesXml.Contains("w:styleId=\"t1livre\"><w:name w:val=\"Titre 1 (2)\"/>") && stylesXml.Contains("w:ascii=\"Garamond\""), "…le Titre 1 du livre y est, avec sa police");
            t.Check(!stylesXml.Contains("Impact"), "…et le style fantôme n'y est pas");
            // Un exemplaire stable pour le contrôle dans Word, à la main.
            Docx.Export(titled, styles, Path.Combine(Path.GetTempPath(), "marabook-c80-titres.docx"), new PageSetup(), "moi", w.Project);
        }

        // ------------------------------------------------------------ PDF

        private static void Pdf(Harness t, World w)
        {
            var pdf = BuiltIn(ExportPresets.EncyclopediaPdf);
            var segments = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.Single, w.Chapter1, w.Keira, w.Marabout), BuiltIn(ExportPresets.Bfp), null), BuiltIn(ExportPresets.Bfp), null);
            t.Check(pdf.Links, "le préréglage porte les liens");
            var parts = new List<PdfPart>();
            var metrics = new StubGlyphMetrics();
            var pages = 0;
            foreach (var segment in segments)
            {
                var engine = new CompositionEngine(segment.Document, segment.Styles, segment.Setup, w.Project, true, metrics);
                engine.FolioOffset = pages;
                engine.ComposeAll();
                pages += engine.Current.Pages.Count;
                var part = new PdfPart { Composition = engine.Current };
                foreach (var mark in segment.Marks) part.Marks.Add(new PdfMark { Paragraph = mark.Paragraph, Name = mark.Name, Title = mark.Title, Level = mark.Level });
                foreach (var link in segment.Links) part.Links.Add(new PdfLink { Paragraph = link.Paragraph, Start = link.Start, Length = link.Length, Target = link.Target });
                parts.Add(part);
            }
            // Une page-image à sa taille : 2 × 2 pixels sur 100 × 60 mm.
            var image = new PdfPart { ImageRgb = new byte[12], ImageWidth = 2, ImageHeight = 2, PageWidthMm = 100, PageHeightMm = 60 };
            image.Marks.Add(new PdfMark { Name = "plan", Title = "Le plan", Level = 0 });
            parts.Add(image);

            var bytes = PdfWriter.Build(parts, new PdfExportOptions { Title = "Essai", Author = "Autrice d'essai" });
            var text = Encoding.GetEncoding(28591).GetString(bytes);
            t.Check(text.StartsWith("%PDF-1.4") && text.TrimEnd().EndsWith("%%EOF"), "un PDF du début à la fin");
            t.Equal(pages + 1, CountOf(text, "/Type /Page /Parent"), "une page par page composée, plus la page-image");
            t.Check(text.Contains("/MediaBox [0 0 595.276 841.89]"), "des pages en A4");
            t.Check(text.Contains("/MediaBox [0 0 419.528 595.276]"), "…des pages en A5");
            t.Check(text.Contains("/MediaBox [0 0 283.465 170.079]"), "…et la page-image à sa propre taille : un PDF aux formats mêlés");
            t.Check(text.Contains("/Type /Outlines") && text.Contains("/PageMode /UseOutlines"), "les signets, volet ouvert");
            t.Check(CountOf(text, "/Subtype /Link") >= 4, "les liens cliquables : ceux de l'écrit et ceux des fiches (" + CountOf(text, "/Subtype /Link") + ")");
            t.Check(text.Contains("/Dest ["), "…chacun vers sa destination");
            t.Check(text.Contains("/Author <FEFF"), "l'auteur·ice dans les informations du fichier");
            t.Check(text.Contains("/Subtype /Image /Width 2 /Height 2"), "l'image de la page-image");

            // Sans repère ni lien : rien de tout cela, et l'ancienne écriture (une composition) tient toujours.
            var plain = new CompositionEngine(segments[0].Document, segments[0].Styles, segments[0].Setup, w.Project, true, metrics);
            plain.ComposeAll();
            var simple = Encoding.GetEncoding(28591).GetString(PdfWriter.Build(new[] { new PdfPart { Composition = plain.Current } }, new PdfExportOptions()));
            t.Check(!simple.Contains("/Outlines") && !simple.Contains("/Annots"), "un PDF simple : ni signets, ni liens");
        }
    }
}

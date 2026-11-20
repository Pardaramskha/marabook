using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.Tests
{
    /// <summary>C81 — le gestionnaire d'export, suite (2.0.0, 20/11) : l'onglet
    /// Structure (enchaînement des pages : belle page ou pages blanches
    /// retirées ; rendu par niveau : genre, livre, dossier, sous-dossier), le
    /// filtre de l'ampleur (statut, tag), l'alerte de caractère absent, les
    /// réglages d'ebook (lettrine, petites capitales, page de titre, profil
    /// Kindle, titres de partie) et le contrôle de l'EPUB écrit.</summary>
    public static class ExportStructureTests
    {
        private sealed class World
        {
            public Project Project;
            public BinderItem Book, Part1, Act, Part2, A, B, C, D, Secret, Open, Plan;
        }

        /// <summary>Des métriques où une police n'a ni « Œ » ni « → ».</summary>
        private sealed class HoleyMetrics : IGlyphMetrics
        {
            private readonly StubGlyphMetrics _inner = new StubGlyphMetrics();
            public double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, char c) { return _inner.AdvanceWidth(fontFamily, emSize, weight, italic, c); }
            public double AdvanceWidth(string fontFamily, double emSize, int weight, bool italic, string s) { return _inner.AdvanceWidth(fontFamily, emSize, weight, italic, s); }
            public FontMetrics Metrics(string fontFamily, double emSize, int weight, bool italic) { return _inner.Metrics(fontFamily, emSize, weight, italic); }
            public bool HasGlyph(string fontFamily, int weight, bool italic, char c)
            {
                if (fontFamily == "Trouée") return c != 'Œ' && c != '→';
                return true;
            }
        }

        public static void Run(Harness t)
        {
            t.Suite("C81 — export : structure, filtre, caractères absents, ebook");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c81-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Presets(t);
                var world = Fixture();
                Levels(t, world);
                Types(t, world);
                BlankPages(t, world);
                Filter(t, world);
                Glyphs(t, world);
                Ebook(t, world, dir);
            }
            finally
            {
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
            project.Name = "Projet à niveaux";
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            w.Book = new BinderItem { Kind = ItemKind.Book, Title = "Tome 1", Book = new BookInfo() };
            Add(writings, w.Book);
            w.Part1 = new BinderItem { Kind = ItemKind.Folder, Title = "Première partie" };
            Add(w.Book, w.Part1);
            w.Act = new BinderItem { Kind = ItemKind.Folder, Title = "Acte I" };
            Add(w.Part1, w.Act);
            w.A = Text("Chapitre A", "Pendant des années, la mer resta calme. Puis vint l'orage.");
            w.A.Status = "done";
            w.A.Tags.Add("canon");
            Add(w.Act, w.A);
            w.B = Text("Chapitre B", "— Bonjour, dit-elle en entrant.");
            w.B.Status = "draft";
            Add(w.Act, w.B);
            w.Part2 = new BinderItem { Kind = ItemKind.Folder, Title = "Deuxième partie" };
            Add(w.Book, w.Part2);
            w.C = Text("Chapitre C", "« Écoute ! » cria le vieux marabout.");
            w.C.Status = "done";
            w.C.Tags.Add("Secret");
            Add(w.Part2, w.C);
            w.D = Text("Nouvelle D", "Une nouvelle à part.");
            Add(writings, w.D);

            var sheets = project.Category(Project.KeySheets);
            w.Secret = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche secrète", Document = TextDocument.FromPlainText("Chut.") };
            w.Secret.Tags.Add("secret");
            Add(sheets, w.Secret);
            w.Open = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche ouverte", Document = TextDocument.FromPlainText("Tout le monde sait.") };
            Add(sheets, w.Open);
            w.Plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan du tome", Plan = new PlanInfo() };
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

        private static string Flat(TextParagraph paragraph) { return PivotEdit.FlatText(paragraph); }

        private static int IndexOf(TextDocument document, string text)
        {
            for (var i = 0; i < document.Paragraphs.Count; i++) if (Flat(document.Paragraphs[i]) == text) return i;
            return -1;
        }

        private static ExportMark Mark(ExportSegment segment, string title)
        {
            foreach (var mark in segment.Marks) if (mark.Title == title) return mark;
            return null;
        }

        // ------------------------------------------------------------ préréglages

        private static void Presets(Harness t)
        {
            var blank = 0;
            foreach (var preset in ExportPresets.BuiltIn()) if (preset.DropBlankPages) blank++;
            t.Equal(1, blank, "« Retirer les pleines pages blanches » : cochée dans un seul préréglage de la maison");
            t.Check(BuiltIn(ExportPresets.Manuscript).DropBlankPages && BuiltIn(ExportPresets.Manuscript).DropBlanks, "…le Manuscrit papier");
            var kindle = BuiltIn(ExportPresets.EpubKindle);
            t.Check(kindle.Kindle && kindle.Format == ExportFormats.Epub && kindle.Cover && kindle.Category == ExportPresets.CategoryBooks, "« EPUB pour Kindle » : un EPUB de la catégorie Livres, profil Kindle, couverture");
            t.Check(!BuiltIn(ExportPresets.EpubPublish).Kindle, "l'EPUB Publiable n'a pas le profil Kindle");

            var custom = new ExportPreset { DropBlankPages = true, SplitTypes = true, SplitBooks = true, SplitFolders = true, SplitSubfolders = true, SplitOwnPage = false, DropCaps = true, OpeningSmallCaps = true, Kindle = true };
            var back = ExportPreset.FromJson(Json.Parse(Json.Write(custom.ToJson())));
            t.Check(back.DropBlankPages && back.SplitTypes && back.SplitBooks && back.SplitFolders && back.SplitSubfolders && !back.SplitOwnPage && back.DropCaps && back.OpeningSmallCaps && back.Kindle,
                "les réglages de structure et d'ebook font l'aller-retour en JSON");
            t.Equal(custom.Signature(), back.Signature(), "…et gardent la même empreinte");
            t.Check(new ExportPreset().Signature() != custom.Signature(), "un réglage de structure change l'empreinte (« modifié »)");
            var old = ExportPreset.FromJson(Json.Parse("{\"name\":\"Ancien\",\"format\":\"pdf\"}"));
            t.Check(!old.DropBlankPages && !old.SplitsLevels && old.SplitOwnPage && !old.DropCaps && !old.Kindle, "un préréglage enregistré avant le 20/11 : rien de neuf n'est coché");
            var both = new ExportPreset { RectoStarts = true, DropBlankPages = true };
            t.Check(!both.DropBlanks && !ExportPreset.FromJson(Json.Parse(Json.Write(both.ToJson()))).DropBlankPages, "belle page et pages blanches retirées s'excluent : la belle page gagne");
        }

        // ------------------------------------------------------------ rendu par niveau

        private static void Levels(Harness t, World w)
        {
            var ids = Ids(w.A, w.B, w.C, w.D);
            var preset = new ExportPreset { Format = ExportFormats.Docx, SplitBooks = true, SplitFolders = true, SplitSubfolders = true, SplitOwnPage = true };
            var segments = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, preset, null), preset, null);
            t.Equal(1, segments.Count, "quatre écrits en A4 : un seul flux");
            var flow = segments[0];
            var doc = flow.Document;
            int book = IndexOf(doc, "Tome 1"), part1 = IndexOf(doc, "Première partie"), act = IndexOf(doc, "Acte I"), a = IndexOf(doc, "Chapitre A"),
                b = IndexOf(doc, "Chapitre B"), part2 = IndexOf(doc, "Deuxième partie"), c = IndexOf(doc, "Chapitre C"), d = IndexOf(doc, "Nouvelle D");
            t.Check(book == 0 && part1 == 1 && act == 2 && a == 3, "le livre, la partie, le sous-dossier, puis le titre du premier écrit");
            t.Check(b > a && part2 > b && c == part2 + 1 && d > c, "un titre de partie à chaque changement de dossier, rien devant l'écrit hors livre");
            t.Equal(1, Count(doc, "Tome 1"), "le titre du livre ne paraît qu'une fois");
            t.Equal(1, Count(doc, "Acte I"), "le titre du sous-dossier ne se répète pas d'un écrit à l'autre");
            t.Check(doc.Paragraphs[book].StyleId == ExportLevels.StyleBook && doc.Paragraphs[part1].StyleId == ExportLevels.StyleFolder
                && doc.Paragraphs[act].StyleId == ExportLevels.StyleSubfolder && doc.Paragraphs[a].StyleId == "title2", "chaque niveau a son style");
            var folderStyle = flow.Styles.Find(ExportLevels.StyleFolder);
            t.Check(folderStyle.Id == ExportLevels.StyleFolder && folderStyle.Name == "Export — titre de partie" && flow.Styles.Find(ExportLevels.StyleSubfolder).Name == "Export — titre de sous-partie",
                "les styles de niveau sont dans la feuille de l'export, sous leur nom");
            t.Check(w.Project.Styles.Find(ExportLevels.StyleFolder).Id != ExportLevels.StyleFolder, "…et pas dans celle du projet");
            // Sur leur page : pas de saut en tête de flux, un saut après eux, un saut avant la partie suivante.
            t.Check(!doc.Paragraphs[book].PageBreakBefore && !doc.Paragraphs[part1].PageBreakBefore && !doc.Paragraphs[act].PageBreakBefore && doc.Paragraphs[a].PageBreakBefore,
                "les titres qui se suivent partagent une page, l'écrit ouvre la suivante");
            t.Check(doc.Paragraphs[part2].PageBreakBefore && doc.Paragraphs[c].PageBreakBefore, "la partie suivante prend sa page, son premier écrit aussi");
            // Les signets : emboîtés.
            t.Check(Mark(flow, "Tome 1").Level == 0 && Mark(flow, "Première partie").Level == 1 && Mark(flow, "Acte I").Level == 2 && Mark(flow, "Chapitre A").Level == 3
                && Mark(flow, "Chapitre B").Level == 3 && Mark(flow, "Deuxième partie").Level == 1 && Mark(flow, "Chapitre C").Level == 2 && Mark(flow, "Nouvelle D").Level == 0,
                "les signets s'emboîtent : livre, partie, sous-dossier, écrit");
            t.Check(Mark(flow, "Première partie").Paragraph == part1 && Mark(flow, "Chapitre C").Paragraph == c && Mark(flow, "Chapitre C").Name == w.C.Id && Mark(flow, "Deuxième partie").Name == null,
                "chaque signet vise son paragraphe ; seul un écrit est une destination de lien");

            // En tête de l'écrit, sans page à eux.
            preset.SplitOwnPage = false;
            flow = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, preset, null), preset, null)[0];
            doc = flow.Document;
            part2 = IndexOf(doc, "Deuxième partie");
            t.Check(doc.Paragraphs[part2].PageBreakBefore && !doc.Paragraphs[part2 + 1].PageBreakBefore && Flat(doc.Paragraphs[part2 + 1]) == "Chapitre C",
                "sans page à eux : le titre de partie prend le saut de page de l'écrit et le coiffe");

            // Un seul niveau coché.
            var folders = new ExportPreset { Format = ExportFormats.Docx, SplitFolders = true };
            doc = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, folders, null), folders, null)[0].Document;
            t.Check(IndexOf(doc, "Tome 1") < 0 && IndexOf(doc, "Acte I") < 0 && IndexOf(doc, "Première partie") == 0, "« par dossier » seul : ni livre ni sous-dossier");
            var subs = new ExportPreset { Format = ExportFormats.Docx, SplitSubfolders = true };
            doc = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, subs, null), subs, null)[0].Document;
            t.Check(IndexOf(doc, "Acte I") == 0 && doc.Paragraphs[0].StyleId == ExportLevels.StyleSubfolder && IndexOf(doc, "Première partie") < 0, "« par sous-dossier » seul : le sous-dossier garde son style");

            // Rien de coché : rien ne change.
            var plain = new ExportPreset { Format = ExportFormats.Docx };
            flow = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, plain, null), plain, null)[0];
            t.Check(IndexOf(flow.Document, "Tome 1") < 0 && flow.Styles.Find(ExportLevels.StyleFolder).Id != ExportLevels.StyleFolder && Mark(flow, "Chapitre A").Level == 0,
                "sans séparation : ni titre de niveau, ni style en plus, signets à plat");

            // Sans titres de chapitres, les titres de niveau restent.
            var bare = new ExportPreset { Format = ExportFormats.Docx, ChapterHeadings = false, SplitFolders = true };
            doc = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, bare, null), bare, null)[0].Document;
            t.Check(IndexOf(doc, "Première partie") == 0 && IndexOf(doc, "Chapitre A") < 0 && doc.Paragraphs[1].PageBreakBefore, "sans titre de chapitre : le titre de partie reste, le texte ouvre la page suivante");

            // Markdown : les niveaux deviennent des titres.
            var markdown = MarkdownExchange.Export(ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, preset, null), preset, null)[0].Document);
            t.Check(markdown.Contains("# Tome 1") && markdown.Contains("# Première partie") && markdown.Contains("## Acte I"), "en Markdown, les titres de niveau sont des titres");
        }

        private static int Count(TextDocument document, string text)
        {
            var count = 0;
            foreach (var paragraph in document.Paragraphs) if (Flat(paragraph) == text) count++;
            return count;
        }

        // ------------------------------------------------------------ séparer par genre

        private static void Types(Harness t, World w)
        {
            var ids = Ids(w.A, w.Open, w.Plan);
            var preset = new ExportPreset { Format = ExportFormats.Pdf, SplitTypes = true, SplitOwnPage = true, Outline = true };
            var segments = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, preset, null), preset, null);
            t.Equal(4, segments.Count, "écrit, fiches, page « Plans », plan");
            t.Check(Flat(segments[0].Document.Paragraphs[0]) == "Écrits" && segments[0].Document.Paragraphs[0].StyleId == ExportLevels.StyleType && segments[0].Document.Paragraphs[1].PageBreakBefore,
                "« Écrits » ouvre les écrits, sur sa page");
            t.Check(Mark(segments[0], "Écrits").Level == 0 && Mark(segments[0], "Chapitre A").Level == 1, "l'écrit se range sous le signet « Écrits »");
            t.Check(Flat(segments[1].Document.Paragraphs[0]) == "Fiches" && segments[1].Document.Paragraphs[1].PageBreakBefore, "« Fiches » ouvre les fiches, sur sa page");
            t.Check(!segments[2].IsImage && Flat(segments[2].Document.Paragraphs[0]) == "Plans" && segments[2].Marks[0].Title == "Plans", "une page « Plans » précède les pages-images");
            t.Check(segments[3].IsImage && segments[3].Marks.Count == 1 && segments[3].Marks[0].Level == 1, "le plan se range sous « Plans »");

            preset.SplitOwnPage = false;
            segments = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, preset, null), preset, null);
            t.Equal(3, segments.Count, "sans page à eux : pas de page « Plans »");
            t.Check(!segments[0].Document.Paragraphs[1].PageBreakBefore && !segments[1].Document.Paragraphs[1].PageBreakBefore, "« Écrits » et « Fiches » coiffent ce qui suit");
            t.Check(segments[2].IsImage && segments[2].Marks.Count == 2 && segments[2].Marks[0].Title == "Plans" && segments[2].Marks[0].Level == 0 && segments[2].Marks[1].Level == 1,
                "le premier plan porte le signet « Plans », et se range dessous");

            var texts = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, Ids(w.A, w.B), preset, null), preset, null);
            t.Check(IndexOf(texts[0].Document, "Écrits") < 0, "un seul genre : rien à séparer");

            var bfp = BuiltIn(ExportPresets.Bfp);
            t.Check(bfp.SplitTypes && bfp.SplitBooks && bfp.SplitFolders && bfp.SplitSubfolders && !bfp.SplitOwnPage, "le BFP sépare tout, sans ajouter de pages");
        }

        // ------------------------------------------------------------ pages blanches

        private static TextParagraph Para(string style, string text, bool pageBreak)
        {
            var paragraph = new TextParagraph { StyleId = style, PageBreakBefore = pageBreak };
            if (text != null) paragraph.Runs.Add(new TextRun { Text = text });
            return paragraph;
        }

        private static void BlankPages(Harness t, World w)
        {
            var segment = new ExportSegment { Document = new TextDocument(), Styles = w.Project.Styles };
            var p = segment.Document.Paragraphs;
            p.Add(Para("title1", "Le titre", false));        // 0 — la page de titre
            p.Add(Para("body", "L'autrice", false));         // 1
            p.Add(Para("body", "", true));                   // 2 — une page blanche
            p.Add(Para("title1", "Première partie", true));  // 3 — une page qui ne porte qu'un titre
            p.Add(Para("body", null, false));                // 4
            p.Add(Para("body", "Le texte.", true));          // 5
            p.Add(Para("body", "   ", true));                // 6 — une page d'espaces
            p.Add(Para("body", "La suite.", true));          // 7
            p.Add(Para("title2", "Fin", true));              // 8 — un titre seul en dernière page : il reste
            segment.Marks.Add(new ExportMark { Paragraph = 5, Title = "Le texte" });
            segment.Marks.Add(new ExportMark { Paragraph = 7, Title = "La suite" });
            segment.Links.Add(new ExportLink { Paragraph = 7, Start = 0, Length = 2, Target = "x" });
            var removed = ExportBlankPages.Apply(segment, 2);
            t.Equal(3, removed, "trois pages retirées : la blanche, celle d'espaces, celle du titre de partie");
            var doc = segment.Document;
            t.Equal(7, doc.Paragraphs.Count, "les paragraphes des pages blanches tombent, le titre de partie reste");
            t.Check(Flat(doc.Paragraphs[2]) == "Première partie" && doc.Paragraphs[2].PageBreakBefore, "le titre de partie ouvre toujours sa page…");
            t.Check(Flat(doc.Paragraphs[4]) == "Le texte." && !doc.Paragraphs[4].PageBreakBefore, "…et le texte l'y rejoint");
            t.Check(Flat(doc.Paragraphs[5]) == "La suite." && doc.Paragraphs[5].PageBreakBefore && Flat(doc.Paragraphs[6]) == "Fin" && doc.Paragraphs[6].PageBreakBefore, "les pages pleines gardent leur saut");
            t.Check(p[5].PageBreakBefore && p.Count == 9, "le document d'origine n'est pas touché");
            t.Check(segment.Marks[0].Paragraph == 4 && segment.Marks[1].Paragraph == 5 && segment.Links[0].Paragraph == 5, "les signets et les liens suivent leurs paragraphes");

            var clean = new ExportSegment { Document = new TextDocument(), Styles = w.Project.Styles };
            clean.Document.Paragraphs.Add(Para("body", "Un.", false));
            clean.Document.Paragraphs.Add(Para("body", "Deux.", true));
            var same = clean.Document;
            t.Check(ExportBlankPages.Apply(clean, 0) == 0 && ReferenceEquals(same, clean.Document), "rien à retirer : le document reste tel quel");

            var head = new ExportSegment { Document = new TextDocument(), Styles = w.Project.Styles };
            head.Document.Paragraphs.Add(Para("title1", "Titre du livre", false));
            head.Document.Paragraphs.Add(Para("body", "Chapitre.", true));
            t.Equal(0, ExportBlankPages.Apply(head, 1), "la page de titre n'est jamais fondue dans la suivante");

            t.Check(ExportBlankPages.IsTitleStyle(w.Project.Styles, "title1") && ExportBlankPages.IsTitleStyle(w.Project.Styles, ExportLevels.StyleFolder) && !ExportBlankPages.IsTitleStyle(w.Project.Styles, "body"),
                "un titre de partie : un style de titre de la maison ou de niveau");

            // Par le flux : le Manuscrit papier fond les titres de partie et retire l'écrit vide.
            var empty = Text("Page vide", "");
            Add(w.Part2, empty);
            try
            {
                var manuscript = BuiltIn(ExportPresets.Manuscript);
                manuscript.SplitFolders = true;
                var ids = Ids(w.A, w.C, empty);
                var flow = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, manuscript, null), manuscript, null)[0];
                var part = IndexOf(flow.Document, "Deuxième partie");
                t.Check(part > 0 && flow.Document.Paragraphs[part].PageBreakBefore && !flow.Document.Paragraphs[part + 1].PageBreakBefore, "Manuscrit papier : le titre de partie ne garde pas une page pour lui");
                var title = IndexOf(flow.Document, "Tome 1");
                t.Check(title == 0, "…et la page de titre reste en tête");
                manuscript.DropBlankPages = false;
                var kept = ExportFlow.Build(w.Project, ExportPlan.Build(w.Project, ids, manuscript, null), manuscript, null)[0];
                part = IndexOf(kept.Document, "Deuxième partie");
                t.Check(kept.Document.Paragraphs[part + 1].PageBreakBefore, "sans l'option : le titre de partie garde sa page");
            }
            finally
            {
                w.Part2.Children.Remove(empty);
            }
        }

        // ------------------------------------------------------------ filtre de l'ampleur

        private static void Filter(Harness t, World w)
        {
            var ids = Ids(w.A, w.B, w.C, w.D, w.Secret, w.Open, w.Plan);
            var pdf = new ExportPreset { Format = ExportFormats.Pdf };
            var all = ExportPlan.Build(w.Project, ids, pdf, null);
            t.Check(all.Texts.Count == 4 && all.Sheets.Count == 2 && all.Filtered.Count == 0 && all.Filter == null, "sans filtre : tout ce qui est coché part");

            var done = ExportPlan.Build(w.Project, ids, pdf, null, new ExportFilter { OnlyStatus = "done" });
            t.Check(done.Texts.Count == 2 && done.Texts[0] == w.A && done.Texts[1] == w.C, "« seulement Terminé » : les deux écrits terminés");
            t.Check(done.Sheets.Count == 2 && done.Plans.Count == 1 && done.Filtered.Count == 2, "…les fiches et les plans n'ont pas de statut : ils passent");
            var info = done.Warnings.Find(delegate(ExportWarning warning) { return warning.Text.Contains("par le filtre"); });
            t.Check(info != null && info.Level == ExportWarning.Info && info.Text.StartsWith("2 éléments cochés sont écartés") && info.Detail.Contains("Terminé"), "le filtre se dit dans les vérifications");

            var except = ExportPlan.Build(w.Project, ids, pdf, null, new ExportFilter { ExceptTag = "secret" });
            t.Check(except.Texts.Count == 3 && !except.Texts.Contains(w.C) && except.Sheets.Count == 1 && except.Sheets[0] == w.Open, "« sauf ce tag » : écrits et fiches, sans regarder la casse");
            var only = ExportPlan.Build(w.Project, ids, pdf, null, new ExportFilter { OnlyTag = "canon" });
            t.Check(only.Count == 1 && only.Texts[0] == w.A, "« seulement ce tag »");
            var both = ExportPlan.Build(w.Project, ids, pdf, null, new ExportFilter { OnlyStatus = "done", ExceptTag = "secret" });
            t.Check(both.Texts.Count == 1 && both.Texts[0] == w.A, "statut et tag se combinent");

            var none = ExportPlan.Build(w.Project, Ids(w.B), pdf, null, new ExportFilter { OnlyStatus = "done" });
            t.Check(none.IsEmpty && none.WarnCount == 1 && none.Warnings[0].Text == "Le filtre écarte tout ce qui est coché.", "tout écarté : un avertissement qui nomme le filtre");
            t.Check(new ExportFilter().IsEmpty && ExportPlan.Build(w.Project, ids, pdf, null, new ExportFilter()).Filter == null, "un filtre vide ne compte pas");
            t.Equal("écrits au statut « Terminé », sauf le tag « secret »", new ExportFilter { OnlyStatus = "done", ExceptTag = "secret" }.Describe(), "le filtre se décrit");

            var tags = ExportFilter.TagsOf(w.Project);
            t.Check(tags.Count == 2 && tags[0] == "canon" && tags[1] == "Secret", "les tags du projet : chacun une fois, triés");

            // L'Encyclopédie : « sauf ce tag ».
            var sheets = Encyclopedia.SelectSheets(w.Project, EncyclopediaSelection.All(), new EncyclopediaOptions { ExceptTag = "SECRET" });
            t.Check(sheets.Count == 1 && sheets[0] == w.Open, "Encyclopédie : les fiches du tag écarté restent dehors");
            t.Equal(2, Encyclopedia.SelectSheets(w.Project, EncyclopediaSelection.All(), new EncyclopediaOptions()).Count, "…sans tag écarté, toutes partent");
            t.Check(new EncyclopediaOptions { ExceptTag = "x" }.Signature() != new EncyclopediaOptions().Signature(), "le tag écarté compte dans l'empreinte des options");
        }

        // ------------------------------------------------------------ caractères absents

        private static void Glyphs(Harness t, World w)
        {
            var metrics = new HoleyMetrics();
            var styles = w.Project.Styles.Clone();
            styles.Body.FontFamily = "Trouée";
            var text = Text("Le cœur", "L'Œuvre → la suite. Œ encore.");
            text.Document.Paragraphs[0].Runs.Add(new TextRun { Text = " Œ →", FontFamily = "Pleine" });
            var missing = ExportGlyphs.Missing(text, styles, metrics);
            t.Equal(2, missing.Count, "deux caractères absents de la police du style");
            t.Check(missing[0].Character == 'Œ' && missing[0].Count == 2 && missing[0].Family == "Trouée" && missing[0].FirstTitle == "Le cœur", "« Œ » : deux fois, dans cette police, dans cet écrit");
            t.Check(missing[1].Character == '→' && missing[1].Count == 1, "le passage dans une autre police, qui a le caractère, ne compte pas");
            t.Equal(0, ExportGlyphs.Missing(w.A, w.Project.Styles, metrics).Count, "une police complète : rien à signaler");
            t.Equal(0, ExportGlyphs.Missing(text, styles, null).Count, "sans moteur de polices : rien, sans lever");

            var other = Text("Autre", "Œ.");
            var warnings = ExportGlyphs.Warnings(new[] { missing, ExportGlyphs.Missing(other, styles, metrics), new List<MissingGlyph>() });
            t.Equal(1, warnings.Count, "une remarque par police");
            t.Check(warnings[0].Level == ExportWarning.Warn && warnings[0].Text.Contains("« Trouée »") && warnings[0].Text.Contains("« Œ » (U+0152)") && warnings[0].Text.Contains("(U+2192)"),
                "elle nomme la police et les caractères, avec leur code");
            t.Check(warnings[0].Detail.StartsWith("4 occurrences, d'abord dans « Le cœur »"), "elle compte les occurrences et dit où commencer");
            t.Equal(0, ExportGlyphs.Warnings(new List<List<MissingGlyph>>()).Count, "rien d'absent : aucune remarque");
        }

        // ------------------------------------------------------------ ebook

        private static string Entry(string path, string name)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entry = zip.GetEntry(name);
                if (entry == null) return null;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        private static string Write(World w, ExportPreset preset, ExportMeta meta, string path, params BinderItem[] texts)
        {
            var plan = ExportPlan.Build(w.Project, Ids(texts), preset, meta);
            EpubOptions options;
            var epub = ExportFlow.EpubFor(w.Project, plan, preset, meta, out options);
            Epub.Write(path, w.Project, epub, options);
            return path;
        }

        private static void Ebook(Harness t, World w, string dir)
        {
            var meta = new ExportMeta { Title = "Tome 1", Subtitle = "Roman", Author = "Autrice d'essai", Publisher = "Éditions de la sonde", Year = "2026" };

            // L'EPUB sans apprêt passe le contrôle.
            var plain = Write(w, BuiltIn(ExportPresets.Epub), meta, Path.Combine(dir, "plain.epub"), w.A, w.B, w.C);
            var problems = EpubCheck.Verify(plain);
            t.Equal(0, problems.Count, "l'EPUB sans apprêt passe le contrôle" + (problems.Count > 0 ? " — " + problems[0] : ""));
            t.Check(!Entry(plain, "OEBPS/chapter-001.xhtml").Contains("lettrine") && Entry(plain, "OEBPS/title.xhtml") == null && Entry(plain, "OEBPS/part-001.xhtml") == null, "…sans lettrine, ni page de titre, ni titre de partie");

            // Tout coché : page de titre, numéros, niveaux sur leur écran, lettrine et petites capitales.
            var rich = new ExportPreset { Format = ExportFormats.Epub, TitlePage = true, NumberChapters = true, SplitBooks = true, SplitFolders = true, SplitSubfolders = true, DropCaps = true, OpeningSmallCaps = true };
            var path = Write(w, rich, meta, Path.Combine(dir, "rich.epub"), w.A, w.B, w.C);
            problems = EpubCheck.Verify(path);
            t.Equal(0, problems.Count, "l'EPUB tout coché passe le contrôle" + (problems.Count > 0 ? " — " + problems[0] : ""));
            var title = Entry(path, "OEBPS/title.xhtml");
            t.Check(title != null && title.Contains("<h1 class=\"book-title\">Tome 1</h1>") && title.Contains("Roman") && title.Contains("Autrice d'essai") && title.Contains("Éditions de la sonde — 2026"),
                "la page de titre : titre, sous-titre, autrice, ligne d'édition");
            var opf = Entry(path, "OEBPS/content.opf");
            t.Check(opf.IndexOf("idref=\"title-page\"") < opf.IndexOf("idref=\"part1\"") && opf.IndexOf("idref=\"part3\"") < opf.IndexOf("idref=\"ch1\"") && opf.IndexOf("idref=\"ch2\"") < opf.IndexOf("idref=\"part4\""),
                "l'ordre de lecture : page de titre, livre, partie, sous-dossier, chapitres, partie suivante");
            t.Check(Entry(path, "OEBPS/part-002.xhtml").Contains("<h1 class=\"part\">Première partie</h1>") && Entry(path, "OEBPS/part-004.xhtml").Contains("Deuxième partie"), "chaque titre de niveau a son écran");
            var nav = Entry(path, "OEBPS/nav.xhtml");
            t.Check(nav.Contains("<li><a href=\"part-001.xhtml\">Tome 1</a><ol>") && nav.Contains("<li><a href=\"part-003.xhtml\">Acte I</a><ol>\n<li><a href=\"chapter-001.xhtml\">1. Chapitre A</a></li>"),
                "la table s'emboîte : livre, partie, sous-dossier, chapitres numérotés");
            t.Check(Entry(path, "OEBPS/toc.ncx").Contains("dtb:depth\" content=\"4\""), "la table NCX déclare sa profondeur");
            var first = Entry(path, "OEBPS/chapter-001.xhtml");
            t.Check(first.Contains("<h1 class=\"chapter\">1. Chapitre A</h1>") && first.Contains("opening\"><span class=\"lettrine\">P</span><span class=\"ouverture\">endant des années, la</span> mer resta calme."),
                "la lettrine, puis les quatre premiers mots en petites capitales");
            var dialogue = Entry(path, "OEBPS/chapter-002.xhtml");
            t.Check(!dialogue.Contains("lettrine") && dialogue.Contains("<span class=\"ouverture\">— Bonjour, dit-elle en entrant</span>."), "un tiret de dialogue : pas de lettrine, les petites capitales restent");
            var quoted = Entry(path, "OEBPS/chapter-003.xhtml");
            t.Check(quoted.Contains("<span class=\"lettrine\">« É</span><span class=\"ouverture\">coute</span>"), "un guillemet ouvrant entre dans la lettrine");
            t.Check(Entry(path, "OEBPS/style.css").Contains("span.lettrine { float: left;") && Entry(path, "OEBPS/style.css").Contains("p.opening { text-indent: 0; }"), "la feuille de styles porte la lettrine et l'ouverture");

            // Les titres de niveau en tête du chapitre.
            rich.SplitOwnPage = false;
            path = Write(w, rich, meta, Path.Combine(dir, "inline.epub"), w.A, w.B, w.C);
            problems = EpubCheck.Verify(path);
            t.Equal(0, problems.Count, "l'EPUB aux titres de partie en tête de chapitre passe le contrôle" + (problems.Count > 0 ? " — " + problems[0] : ""));
            first = Entry(path, "OEBPS/chapter-001.xhtml");
            t.Check(Entry(path, "OEBPS/part-001.xhtml") == null && first.IndexOf("<h1 class=\"part\">Tome 1</h1>") < first.IndexOf("<h1 class=\"part\">Acte I</h1>") && first.IndexOf("<h1 class=\"part\">Acte I</h1>") < first.IndexOf("<h1 class=\"chapter\">"),
                "sans écran à eux : les titres de niveau coiffent le chapitre");

            // Le profil Kindle.
            var kindle = BuiltIn(ExportPresets.EpubKindle);
            path = Write(w, kindle, meta, Path.Combine(dir, "kindle.epub"), w.A, w.B, w.C);
            problems = EpubCheck.Verify(path);
            t.Equal(0, problems.Count, "l'EPUB pour Kindle passe le contrôle" + (problems.Count > 0 ? " — " + problems[0] : ""));
            var css = Entry(path, "OEBPS/style.css");
            var body = css.Substring(css.IndexOf(".s-body {"), css.IndexOf("}", css.IndexOf(".s-body {")) - css.IndexOf(".s-body {"));
            t.Check(!body.Contains("font-family") && !body.Contains("font-size") && !body.Contains("line-height"), "Kindle : le corps laisse au lecteur sa police, sa taille et son interligne");
            var heading = css.Substring(css.IndexOf(".s-title1 {"), css.IndexOf("}", css.IndexOf(".s-title1 {")) - css.IndexOf(".s-title1 {"));
            t.Check(heading.Contains("em;") && !heading.Contains("pt; font-weight"), "Kindle : les titres se disent en proportion du corps");
            var regular = Entry(plain, "OEBPS/style.css");
            t.Check(regular.Contains(".s-body { font-family:") && regular.Contains("pt; "), "sans le profil : la feuille garde polices et tailles");
            var warn = ExportPlan.Build(w.Project, Ids(w.A, w.B, w.C), kindle, meta);
            t.Check(warn.Warnings.Exists(delegate(ExportWarning x) { return x.Text == "Pas de couverture."; }), "Kindle : sans couverture, un avertissement");
            kindle.Cover = false;
            warn = ExportPlan.Build(w.Project, Ids(w.A, w.B, w.C), kindle, meta);
            t.Check(warn.Warnings.Exists(delegate(ExportWarning x) { return x.Text == "Le profil Kindle veut une couverture."; }), "Kindle : la couverture décochée se signale");

            // Le contrôle attrape un EPUB mal fait.
            var broken = Path.Combine(dir, "broken.epub");
            using (var stream = new FileStream(broken, FileMode.Create))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("META-INF/container.xml").Open())) writer.Write("<container xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\"/></rootfiles></container>");
                using (var writer = new StreamWriter(zip.CreateEntry("mimetype").Open())) writer.Write("application/epub+zip");
            }
            problems = EpubCheck.Verify(broken);
            t.Check(problems.Count == 2 && problems[0].Contains("« mimetype »") && problems[1].Contains("content.opf"), "un zip sans « mimetype » en tête ni paquet : deux défauts nommés");

            var holes = Path.Combine(dir, "holes.epub");
            Rewrite(plain, holes, delegate(string name, string text)
            {
                if (name == "OEBPS/content.opf") return text.Replace("<dc:language>fr</dc:language>", "").Replace("<itemref idref=\"ch2\"/>", "<itemref idref=\"fantome\"/>");
                if (name == "OEBPS/chapter-001.xhtml") return text.Replace("</section>", "<p><a href=\"nulle-part.xhtml\">ici</a> &nbsp;</p></section>");
                if (name == "OEBPS/chapter-003.xhtml") return null; // le fichier disparaît
                return text;
            });
            problems = EpubCheck.Verify(holes);
            var joined = string.Join("\n", problems.ToArray());
            t.Check(joined.Contains("pas de langue") && joined.Contains("« fantome »") && joined.Contains("chapter-003.xhtml » est déclaré dans le manifeste mais absent") && joined.Contains("chapter-001.xhtml » n'est pas un XML bien formé"),
                "langue manquante, ordre de lecture cassé, fichier absent, XML mal formé : tout est relevé");
            t.Check(EpubCheck.Verify(Path.Combine(dir, "absent.epub")).Count == 1, "un fichier illisible : un défaut, pas d'exception");
        }

        /// <summary>Recopie un EPUB en retouchant ses fichiers texte (null = le fichier tombe) ; « mimetype » reste en tête, non compressé.</summary>
        private static void Rewrite(string source, string target, Func<string, string, string> edit)
        {
            using (var input = new ZipArchive(new FileStream(source, FileMode.Open, FileAccess.Read), ZipArchiveMode.Read))
            using (var output = new ZipArchive(new FileStream(target, FileMode.Create), ZipArchiveMode.Create))
                foreach (var entry in input.Entries)
                {
                    string text;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) text = reader.ReadToEnd();
                    var isText = entry.FullName.EndsWith(".xhtml") || entry.FullName.EndsWith(".opf") || entry.FullName.EndsWith(".ncx") || entry.FullName.EndsWith(".css") || entry.FullName.EndsWith(".xml");
                    if (isText) text = edit(entry.FullName, text);
                    if (text == null) continue;
                    var copy = output.CreateEntry(entry.FullName, entry.FullName == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(copy.Open(), new UTF8Encoding(false))) writer.Write(text);
                }
        }
    }
}

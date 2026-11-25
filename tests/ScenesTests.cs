using System;
using System.Collections.Generic;
using System.IO;
using Marabook.History;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C88 — 2.0.0, lot 27 (21/11) : les scènes d'un écrit (le
    /// découpage sur les séparateurs, les fiches qui suivent leur scène, les
    /// fiches sans scène), leur enregistrement et leur annulation, le Tableau
    /// (lignes, totaux, tris, filtre, export), les rétroliens des scènes, et
    /// les modèles de projet.</summary>
    public static class ScenesTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C88 — lot 27 : scènes et fiches de scène, Tableau (outliner), modèles de projet");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c88-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Splitting(t);
                Following(t);
                Persistence(t, dir);
                OutlineRows(t);
                Templates(t, dir);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>Un écrit dont chaque ligne est un paragraphe ; « *** » = un séparateur de scène.</summary>
        private static BinderItem Text(string title, params string[] lines)
        {
            var item = new BinderItem { Kind = ItemKind.Text, Title = title, Document = new TextDocument() };
            foreach (var line in lines) item.Document.Paragraphs.Add(Paragraph(line));
            return item;
        }

        private static TextParagraph Paragraph(string line)
        {
            var paragraph = new TextParagraph();
            if (line == "***") paragraph.StyleId = StyleSheet.SeparatorId;
            else paragraph.Runs.Add(new TextRun { Text = line });
            return paragraph;
        }

        private static void Add(BinderItem parent, BinderItem child)
        {
            child.Parent = parent;
            parent.Children.Add(child);
        }

        private static string Plain(BinderItem item)
        {
            var lines = new List<string>();
            foreach (var paragraph in item.Document.Paragraphs) lines.Add(paragraph.StyleId + ":" + PivotEdit.FlatText(paragraph));
            return string.Join("\n", lines.ToArray());
        }

        // ------------------------------------------------------------ le découpage

        private static void Splitting(Harness t)
        {
            var alone = Text("Seul", "Un seul bloc.", "Deux paragraphes.");
            var scenes = Scenes.Of(alone);
            t.Check(scenes.Count == 1 && scenes[0].First == 0 && scenes[0].Last == 1 && Scenes.CountOf(alone) == 1, "un écrit sans séparateur est une seule scène");
            t.Check(scenes[0].Words == 5 && scenes[0].Opening == "Un seul bloc.", "…avec ses mots (" + scenes[0].Words + ") et ses premiers mots");
            t.Check(!Scenes.HasAny(alone) && alone.Scenes.Count == 1, "…et une fiche vide, qui ne compte pas comme renseignée");

            var three = Text("Trois", "Keira entre.", "Elle parle.", "***", "Abel attend sur le quai.", "***", "Le soir tombe.");
            scenes = Scenes.Of(three);
            t.Check(scenes.Count == 3 && Scenes.CountOf(three) == 3, "deux séparateurs : trois scènes");
            t.Check(scenes[0].First == 0 && scenes[0].Last == 1 && scenes[1].First == 3 && scenes[1].Last == 3 && scenes[2].First == 5 && scenes[2].Last == 5, "…chacune sur ses paragraphes, le séparateur n'appartenant à aucune");
            t.Check(scenes[1].Opening == "Abel attend sur le quai." && scenes[1].Words == 5, "…ses premiers mots et son compte de mots");
            t.Check(Scenes.IndexAt(three.Document, 0) == 0 && Scenes.IndexAt(three.Document, 1) == 0 && Scenes.IndexAt(three.Document, 2) == 0
                && Scenes.IndexAt(three.Document, 3) == 1 && Scenes.IndexAt(three.Document, 5) == 2, "le paragraphe du curseur dit sa scène (un séparateur compte pour celle d'avant)");

            var edge = Text("Bord", "***", "Après.", "***");
            scenes = Scenes.Of(edge);
            t.Check(scenes.Count == 3 && scenes[0].IsBlank && !scenes[1].IsBlank && scenes[2].IsBlank && scenes[0].Words == 0, "un séparateur en tête ou en fin : une scène vide, sans plantage");
            t.Check(Scenes.Of(new BinderItem { Kind = ItemKind.Folder }).Count == 0 && Scenes.Of(null).Count == 0 && Scenes.CountOf(null) == 0, "un dossier, rien : aucune scène");
            var longText = Text("Long", new string('a', 30) + " " + new string('b', 40));
            t.Check(Scenes.Quote(Scenes.Of(longText)[0].Opening).EndsWith("… »") && Scenes.Quote("Court.") == "« Court. »" && Scenes.Quote("") == "(scène vide)", "les premiers mots se citent ; les points de suspension seulement s'ils sont coupés");
            t.Check(Scenes.Of(longText)[0].Opening.Length == Scenes.AnchorLength, "les premiers mots sont bornés (" + Scenes.AnchorLength + " signes)");
        }

        // ------------------------------------------------------------ les fiches suivent leur scène

        private static void Following(Harness t)
        {
            var item = Text("Chapitre", "Keira entre.", "Elle parle.", "***", "Abel attend sur le quai.", "***", "Le soir tombe.");
            var before = Plain(item);
            var scenes = Scenes.Of(item);
            scenes[0].Meta.PovId = "keira";
            scenes[1].Meta.PovId = "abel";
            scenes[1].Meta.Note = "Le quai";
            scenes[2].Meta.When = "Le soir";
            t.Check(Scenes.HasAny(item) && item.Scenes.Count == 3 && item.Scenes[1].PovId == "abel", "chaque scène porte sa fiche, dans l'ordre");
            t.Check(Plain(item) == before, "renseigner des scènes ne touche pas au texte");

            // Un séparateur AJOUTÉ au milieu de la première scène : quatre scènes, chacune garde la sienne.
            item.Document.Paragraphs.Insert(1, Paragraph("***"));
            bool changed;
            scenes = Scenes.Of(item, out changed);
            t.Check(scenes.Count == 4 && changed, "un séparateur ajouté : une scène de plus");
            t.Check(scenes[0].Meta.PovId == "keira" && scenes[1].Meta.IsEmpty && scenes[1].Opening == "Elle parle.", "…la scène coupée garde sa fiche, la nouvelle en reçoit une vide");
            t.Check(scenes[2].Meta.PovId == "abel" && scenes[2].Meta.Note == "Le quai" && scenes[3].Meta.When == "Le soir", "…et les scènes d'après retrouvent les leurs par leurs premiers mots (pas de décalage)");
            Scenes.Of(item, out changed);
            t.Check(!changed, "relire sans rien changer ne bouge rien");

            // Un séparateur RETIRÉ : deux scènes fondues, la fiche de la seconde n'est pas jetée.
            item.Document.Paragraphs.RemoveAt(5); // le séparateur entre « Abel attend » et « Le soir tombe »
            scenes = Scenes.Of(item, out changed);
            t.Check(scenes.Count == 3 && scenes[2].Opening == "Abel attend sur le quai." && scenes[2].Meta.PovId == "abel", "un séparateur retiré : la scène qui absorbe garde sa fiche");
            var orphans = Scenes.Orphans(item);
            t.Check(orphans.Count == 1 && orphans[0].When == "Le soir" && item.Scenes.Count == 4, "…la fiche de la scène absorbée reste, « sans scène » : rien n'est jeté en silence");
            scenes = Scenes.Of(item, out changed);
            t.Check(Scenes.Orphans(item).Count == 1 && !changed, "…et elle y reste d'une lecture à l'autre");

            // Une fiche vide dont la scène disparaît s'en va, elle.
            var plain = Text("Vide", "Un.", "***", "Deux.");
            Scenes.Of(plain)[0].Meta.PovId = "x";
            plain.Document.Paragraphs.RemoveAt(1);
            Scenes.Of(plain);
            t.Check(plain.Scenes.Count == 1 && plain.Scenes[0].PovId == "x" && Scenes.Orphans(plain).Count == 0, "une fiche vide sans scène disparaît d'elle-même");

            // Les premiers mots d'une scène réécrits, même nombre de scènes : rien ne bouge.
            var rewritten = Text("Réécrit", "Début.", "***", "Suite.");
            Scenes.Of(rewritten)[1].Meta.Note = "la suite";
            rewritten.Document.Paragraphs[2].Runs[0].Text = "Tout autre chose.";
            t.Check(Scenes.Of(rewritten)[1].Meta.Note == "la suite" && rewritten.Scenes[1].Anchor == "Tout autre chose.", "réécrire le début d'une scène ne la sépare pas de sa fiche (l'ancre suit)");

            // La fiche d'une scène : copie, égalité, annulation.
            var meta = new SceneMeta { PovId = "a", PlaceSheetId = "b", When = "hier", Note = "n", Anchor = "x" };
            meta.CharacterIds.Add("c");
            var copy = meta.Clone();
            copy.Anchor = "y";
            t.Check(copy.SameAs(meta) && !ReferenceEquals(copy.CharacterIds, meta.CharacterIds), "une fiche se copie à fond ; l'ancre ne compte pas dans l'égalité");
            copy.CharacterIds.Add("d");
            t.Check(!copy.SameAs(meta) && new SceneMeta().IsEmpty && !meta.IsEmpty, "…un personnage de plus la rend différente");

            var undo = Text("Annuler", "Un.", "***", "Deux.");
            Scenes.Of(undo);
            var snapshot = Scenes.CloneAll(undo.Scenes);
            undo.Scenes[1].PovId = "keira";
            var action = new SceneEditAction(undo, snapshot);
            action.Undo();
            t.Check(undo.Scenes[1].PovId == null, "l'annulation rend la fiche d'avant");
            action.Do();
            t.Check(undo.Scenes[1].PovId == "keira", "…et se refait");
            var duplicate = undo.Duplicate();
            duplicate.Scenes[1].PovId = "autre";
            t.Check(undo.Scenes[1].PovId == "keira" && duplicate.Scenes.Count == 2, "dupliquer un écrit copie ses fiches de scène, indépendantes");
        }

        // ------------------------------------------------------------ le .plot

        private static void Persistence(Harness t, string dir)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            var sheets = project.Category(Project.KeySheets);
            var template = project.CharacterTemplate();
            var keira = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", TemplateId = template.Id, Document = new TextDocument() };
            var port = new BinderItem { Kind = ItemKind.Sheet, Title = "Port-Brume", TemplateId = template.Id, Document = new TextDocument() };
            Add(sheets, keira);
            Add(sheets, port);
            var item = Text("Chapitre", "Keira entre.", "***", "Abel attend.");
            Add(writings, item);
            var bare = Text("Nu", "Rien de renseigné.", "***", "Toujours rien.");
            Add(writings, bare);
            var scenes = Scenes.Of(item);
            Scenes.Of(bare);
            scenes[0].Meta.PovId = keira.Id;
            scenes[0].Meta.PlaceSheetId = port.Id;
            scenes[0].Meta.When = "12 Sel E3.8";
            scenes[0].Meta.CharacterIds.Add(keira.Id);
            scenes[0].Meta.Note = "L'arrivée";
            scenes[1].Meta.PlaceMapId = "carte";
            scenes[1].Meta.PlaceShapeId = "repere";

            var path = Path.Combine(dir, "scenes.plot");
            PlotFile.Save(project, path);
            var loaded = PlotFile.Load(path);
            var back = loaded.FindById(item.Id);
            t.Check(back != null && back.Scenes.Count == 2, "les fiches de scène font l'aller-retour par le .plot");
            t.Check(back.Scenes[0].PovId == keira.Id && back.Scenes[0].PlaceSheetId == port.Id && back.Scenes[0].When == "12 Sel E3.8"
                && back.Scenes[0].CharacterIds.Count == 1 && back.Scenes[0].Note == "L'arrivée" && back.Scenes[0].Anchor == "Keira entre.", "…point de vue, lieu, moment, personnages, note et ancre");
            t.Check(back.Scenes[1].PlaceMapId == "carte" && back.Scenes[1].PlaceShapeId == "repere" && back.Scenes[1].PovId == null, "…un lieu de carte aussi");
            t.Check(loaded.FindById(bare.Id).Scenes.Count == 0, "un écrit sans rien de renseigné n'écrit rien dans le fichier");
            t.Check(Plain(back) == Plain(item), "le texte de l'écrit est le même");

            t.Check(Scenes.SheetTitle(loaded, keira.Id) == "Keira" && Scenes.SheetTitle(loaded, "disparue") == "(fiche disparue)" && Scenes.SheetTitle(loaded, null) == "", "une fiche se nomme ; disparue, elle le dit sans planter");
            t.Check(Scenes.PlaceLabel(loaded, back.Scenes[0]) == "Port-Brume" && Scenes.PlaceLabel(loaded, back.Scenes[1]) == "(lieu disparu)", "le lieu se dit : la fiche, ou le lieu de carte (ici disparu)");
            t.Check(Scenes.CharactersLabel(loaded, back.Scenes[0]) == "Keira", "les personnages présents se disent");

            // Les rétroliens : la fiche sait dans quelles scènes elle joue.
            var links = Backlinks.In(Backlinks.Of(loaded, loaded.FindById(keira.Id)), Backlink.Texts);
            t.Check(links.Count == 1 && links[0].Item.Id == item.Id && links[0].Reasons.Contains("point de vue, scène 1") && links[0].Reasons.Contains("présence, scène 1"),
                "rétroliens : la fiche est dite point de vue et présente dans la scène 1 de l'écrit");
            var portLinks = Backlinks.In(Backlinks.Of(loaded, loaded.FindById(port.Id)), Backlink.Texts);
            t.Check(portLinks.Count == 1 && portLinks[0].Reasons.Contains("lieu, scène 1"), "…et le lieu, lieu de la scène");

            // Les personnages nommés dans le texte d'une scène : proposés.
            var detected = Scenes.DetectedCharacters(loaded, back, Scenes.Of(back)[0]);
            t.Check(detected.Count == 1 && detected[0].Title == "Keira", "les fiches nommées dans la scène sont proposées (" + detected.Count + ")");
            t.Check(Scenes.DetectedCharacters(loaded, back, Scenes.Of(back)[1]).Count == 0, "…scène par scène : pas celles de la scène voisine");

            // Le moment se lit dans le calendrier d'une frise, s'il s'y lit.
            var timeline = new BinderItem { Kind = ItemKind.Timeline, Title = "Histoire", Timeline = new TimelineInfo { Types = TimelineInfo.DefaultTypes() } };
            Add(loaded.Category(Project.KeyTimelines), timeline);
            var system = Timelines.SystemOf(loaded, timeline);
            var dated = new SceneMeta { When = system.Format(new TimePoint(1200, 3, 4)) };
            TimeSystem found;
            var point = Scenes.WhenOf(loaded, dated, out found);
            t.Check(point != null && point.Year == 1200 && point.Month == 3 && point.Day == 4 && found != null, "un moment écrit dans la notation de la frise est lu comme une date");
            t.Check(Scenes.WhenOf(loaded, new SceneMeta { When = "au crépuscule" }, out found) == null && Scenes.WhenOf(loaded, new SceneMeta(), out found) == null, "…un texte libre reste un texte");
        }

        // ------------------------------------------------------------ le Tableau

        private static readonly string QuotedChapter = (char)34 + "  Chapitre 1" + (char)34; // le retrait oblige les guillemets du CSV

        private static void OutlineRows(Harness t)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            var sheets = project.Category(Project.KeySheets);
            var template = project.CharacterTemplate();
            var keira = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", TemplateId = template.Id, Document = new TextDocument() };
            var abel = new BinderItem { Kind = ItemKind.Sheet, Title = "Abel", TemplateId = template.Id, Document = new TextDocument() };
            Add(sheets, keira);
            Add(sheets, abel);
            var timeline = new BinderItem { Kind = ItemKind.Timeline, Title = "Histoire", Timeline = new TimelineInfo { Types = TimelineInfo.DefaultTypes() } };
            Add(project.Category(Project.KeyTimelines), timeline);
            var system = Timelines.SystemOf(project, timeline);

            var book = new BinderItem { Kind = ItemKind.Book, Title = "Le livre" };
            Add(writings, book);
            var part = new BinderItem { Kind = ItemKind.Folder, Title = "Première partie" };
            Add(book, part);
            var one = Text("Chapitre 1", "Un deux trois quatre cinq six.");
            one.Status = "draft";
            one.Synopsis = "Tout commence.";
            var two = Text("Chapitre 2", "Un deux.", "***", "Trois quatre cinq.", "***", "Six.");
            two.Status = "done";
            var three = Text("Chapitre 3", "Un.");
            var extra = Text("Page de titre", "Titre");
            extra.IsExtraPage = true;
            var empty = new BinderItem { Kind = ItemKind.Folder, Title = "Partie vide" };
            Add(part, one);
            Add(part, two);
            Add(book, three);
            Add(book, extra);
            Add(book, empty);
            Scenes.Of(one)[0].Meta.PovId = keira.Id;
            Scenes.Of(one)[0].Meta.When = system.Format(new TimePoint(1200, 6, 1));
            var scenes = Scenes.Of(two);
            scenes[0].Meta.PovId = abel.Id;
            scenes[0].Meta.When = system.Format(new TimePoint(1100, 1, 1));
            scenes[1].Meta.PovId = keira.Id;
            scenes[1].Meta.CharacterIds.Add(abel.Id);
            scenes[1].Meta.Note = "Le retour";
            scenes[2].Meta.When = "plus tard";

            var totals = new OutlineTotals();
            var rows = Outline.Build(project, book, new OutlineOptions(), totals);
            var kinds = "";
            foreach (var row in rows) kinds += row.Kind == OutlineRowKind.Folder ? "D" : row.Kind == OutlineRowKind.Text ? "E" : "s";
            t.Check(kinds == "DEEsssE", "le Tableau d'un livre : la partie, ses écrits, les scènes de l'écrit qui en a plusieurs, puis l'écrit hors partie (" + kinds + ")");
            t.Check(rows[0].Title == "Première partie" && rows[0].Words == 12 && rows[0].Texts == 2 && rows[0].Depth == 0 && rows[1].Depth == 1 && rows[3].Depth == 2, "…le dossier totalise ses mots (" + rows[0].Words + ") et les lignes sont en retrait");
            t.Check(totals.Texts == 3 && totals.Scenes == 5 && totals.Words == 13, "…les totaux : 3 écrits, 5 scènes, 13 mots (" + totals.Texts + ", " + totals.Scenes + ", " + totals.Words + ")");
            t.Check(rows[1].Pov == "Keira" && rows[1].Meta != null && rows[1].SceneCount == 1, "un écrit d'une seule scène porte sa fiche sur sa ligne");
            t.Check(rows[2].Pov == "Abel, Keira" && rows[2].Characters == "Abel" && rows[2].Meta == null && rows[2].SceneCount == 3, "un écrit de plusieurs scènes résume : points de vue distincts, présents (" + rows[2].Pov + ")");
            t.Check(rows[4].Kind == OutlineRowKind.Scene && rows[4].Pov == "Keira" && rows[4].Note == "Le retour" && rows[4].SceneIndex == 1 && rows[4].Words == 3 && rows[4].Title == "Scène 2 — « Trois quatre cinq. »", "une ligne de scène : son numéro, ses premiers mots, ses mots, sa fiche");
            var titles = new List<string>();
            foreach (var row in rows) titles.Add(row.Title);
            t.Check(!titles.Contains("Page de titre") && !titles.Contains("Partie vide"), "ni les pages d'éditeur, ni les dossiers sans écrit");

            var without = Outline.Build(project, book, new OutlineOptions { Scenes = false });
            t.Check(without.Count == 4, "sans « une ligne par scène » : les écrits seuls (" + without.Count + ")");

            var byWords = Outline.Build(project, book, new OutlineOptions { Scenes = false, Sort = Outline.SortWordsDown });
            t.Check(byWords.Count == 3 && byWords[0].Item == one && byWords[1].Item == two && byWords[2].Item == three && byWords[0].Depth == 0, "tri « les plus longs d'abord » : à plat, sans les dossiers");
            var byPov = Outline.Build(project, book, new OutlineOptions { Sort = Outline.SortPov });
            t.Check(byPov.Count == 5 && byPov[0].Pov == "Abel" && byPov[1].Pov == "Keira" && byPov[2].Pov == "Keira" && byPov[3].Pov == "" && byPov[0].Title.StartsWith("Chapitre 2 — Scène 1"), "tri par point de vue : les scènes à plat, nommées par leur écrit, les vides à la fin");
            var byWhen = Outline.Build(project, book, new OutlineOptions { Sort = Outline.SortWhen });
            t.Check(byWhen[0].Item == two && byWhen[0].SceneIndex == 0 && byWhen[1].Item == one && byWhen[2].When == "plus tard" && byWhen[3].When == "", "tri par moment : les dates dans l'ordre du temps (1100 avant 1200), puis les textes libres, puis les vides");
            var byStatus = Outline.Build(project, book, new OutlineOptions { Scenes = false, Sort = Outline.SortStatus });
            t.Check(byStatus[0].Item == one && byStatus[1].Item == two && byStatus[2].Item == three, "tri par état : dans l'ordre des états, les sans-état à la fin");
            var order = "";
            foreach (var child in part.Children) order += child.Title + ";";
            t.Check(order == "Chapitre 1;Chapitre 2;", "un tri ne touche pas à l'ordre du livre");

            var done = Outline.Build(project, book, new OutlineOptions { Status = "done" });
            t.Check(done.Count == 5 && done[0].Kind == OutlineRowKind.Folder && done[0].Texts == 1 && done[1].Item == two, "filtre « Terminé » : l'écrit, ses scènes, et son dossier seul");
            var none = Outline.Build(project, book, new OutlineOptions { Status = "", Scenes = false });
            t.Check(none.Count == 1 && none[0].Item == three, "filtre « Sans état »");

            var csv = Outline.Csv(rows);
            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            t.Check(lines.Length == 8 && lines[0] == "Titre;État;Mots;Point de vue;Lieu;Moment;Personnages;Synopsis", "CSV : une ligne d'en-tête et une par ligne du tableau");
            t.Check(lines[2].StartsWith(QuotedChapter + ";Brouillon;6;Keira;") && lines[2].EndsWith(";Tout commence.") && lines[1].StartsWith("Première partie;;12;"), "…l'état, les mots, le point de vue, le synopsis ; le retrait dans le titre");
            var markdown = Outline.Markdown("Le livre", rows);
            t.Check(markdown.StartsWith("# Le livre\n\n| Titre | État |") && markdown.Contains("| **Première partie** |") && markdown.Contains("*Scène 2 — « Trois quatre cinq. »*"), "Markdown : un titre, un tableau, dossiers en gras et scènes en italique");

            t.Check(Outline.HasTexts(book) && !Outline.HasTexts(empty) && !Outline.HasTexts(null) && Outline.Build(project, null, null).Count == 0, "un dossier sans écrit n'a pas de Tableau");
            foreach (var sort in Outline.Sorts) if (Outline.SortLabel(sort).Length == 0) t.Check(false, "tri sans libellé : " + sort);
        }

        // ------------------------------------------------------------ les modèles de projet

        private static void Templates(Harness t, string dir)
        {
            var previous = ProjectTemplates.FolderOverride;
            ProjectTemplates.FolderOverride = Path.Combine(dir, "modeles");
            try
            {
                t.Check(ProjectTemplates.All().Count == 0, "aucun modèle tant que le dossier n'existe pas");
                var project = Project.CreateNew();
                project.Name = "Mon roman";
                var writings = project.Category(Project.KeyWritings);
                writings.Children.Clear();
                var book = new BinderItem { Kind = ItemKind.Book, Title = "Tome 1", Book = new BookInfo { Title = "Le Tome", Isbn = "978-2-0000-0000-0" } };
                book.Book.Template.PageWidthMm = 127;
                Add(writings, book);
                Add(book, new BinderItem { Kind = ItemKind.PageTemplate, Title = "Gabarit roman" });
                var part = new BinderItem { Kind = ItemKind.Folder, Title = "Acte I" };
                Add(book, part);
                var chapter = Text("Chapitre 1", "Un secret bien gardé.", "***", "Et sa suite.");
                chapter.Synopsis = "Le secret";
                chapter.Notes = "À revoir";
                chapter.Status = "draft";
                Scenes.Of(chapter)[0].Meta.Note = "enjeu";
                Add(part, chapter);
                var title = Text("Page de titre", "TITRE DU LIVRE");
                title.IsExtraPage = true;
                Add(book, title);
                var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", TemplateId = project.CharacterTemplate().Id, Document = new TextDocument() };
                Add(project.Category(Project.KeySheets), sheet);
                var research = Text("Notes de recherche", "Confidentiel.");
                Add(project.Category(Project.KeyResearch), research);
                project.Lexicon.Add(new LexiconEntry { Word = "Ehnmir" });
                project.Styles.Find("body").FontSize = 18.5;
                project.Page.PageWidthMm = 148;
                var sheetTemplates = project.Templates.Count;
                project.CreatedAt = "2026-01-02 10:00";
                project.ModifiedAt = "2026-03-04 11:00";
                project.SaveId = "empreinte";
                var textBefore = Plain(chapter);

                var skeleton = ProjectTemplates.Skeleton(project);
                t.Check(project.CreatedAt == "2026-01-02 10:00" && project.ModifiedAt == "2026-03-04 11:00" && project.SaveId == "empreinte" && Plain(chapter) == textBefore && chapter.Synopsis == "Le secret",
                    "« Enregistrer en tant que modèle » ne touche pas au projet (ni ses dates, ni son empreinte, ni son texte)");
                var sw = skeleton.Category(Project.KeyWritings);
                t.Check(sw.Children.Count == 1 && sw.Children[0].Kind == ItemKind.Book && sw.Children[0].Title == "Nouveau livre", "le projet avait un livre : le modèle part avec un « Nouveau livre », pas avec son nom");
                t.Check(sw.Children[0].Children.Count == 1 && sw.Children[0].Children[0].Kind == ItemKind.PageTemplate && sw.Children[0].Children[0].Title == "Gabarit roman", "…sans partie, sans écrit, sans page d'éditeur : seuls ses gabarits de pages suivent");
                t.Check(Math.Abs(sw.Children[0].Book.Template.PageWidthMm - 127) < 0.001 && sw.Children[0].Book.Title == "" && sw.Children[0].Book.Isbn == "", "…au FORMAT du livre d'origine, sans ses métadonnées (titre, ISBN)");
                var anyTitle = false;
                foreach (var item in skeleton.AllItems())
                    if (item.Title == "Chapitre 1" || item.Title == "Acte I" || item.Title == "Tome 1" || item.Title == "Page de titre") anyTitle = true;
                t.Check(!anyTitle, "aucun titre d'écrit, de partie ni de livre ne suit");
                t.Check(skeleton.Category(Project.KeySheets).Children.Count == 0 && skeleton.Category(Project.KeyResearch).Children.Count == 0 && skeleton.Lexicon.Count == 0, "ni fiches, ni recherche, ni dictionnaire (par défaut)");
                var withWords = ProjectTemplates.Skeleton(project, true);
                t.Check(withWords.Lexicon.Count == 1 && withWords.Lexicon[0].Word == "Ehnmir" && project.Lexicon.Count == 1, "à la demande, le dictionnaire du projet suit dans le modèle");
                var plain = Project.CreateNew();
                var bare = ProjectTemplates.Skeleton(plain);
                var bareWritings = bare.Category(Project.KeyWritings);
                t.Check(bareWritings.Children.Count == 1 && bareWritings.Children[0].Kind == ItemKind.Text && bareWritings.Children[0].Title == "Nouvel écrit", "un projet sans livre donne un modèle sans livre : l'écrit vide d'un projet neuf");
                t.Check(Math.Abs(skeleton.Styles.Find("body").FontSize - 18.5) < 0.001 && Math.Abs(skeleton.Page.PageWidthMm - 148) < 0.001 && skeleton.Templates.Count == sheetTemplates && skeleton.SheetCategories.Count == project.SheetCategories.Count,
                    "les styles, la mise en page, les modèles et catégories de fiches suivent");
                t.Check(skeleton.Category(Project.KeyTrash) != null && skeleton.Category(Project.KeyTimelines) != null, "toutes les rubriques de Marabook y sont");

                var path = ProjectTemplates.Save(project, "  Roman en trois actes  ");
                var all = ProjectTemplates.All();
                t.Check(File.Exists(path) && all.Count == 1 && all[0].Name == "Roman en trois actes" && ProjectTemplates.Exists("roman en trois actes"), "le modèle s'enregistre sous son nom (rogné), dans le dossier des modèles");
                t.Check(!File.Exists(path + ".bak") && !File.Exists(path + ".tmp"), "…sans fichier de trop à côté");
                var born = ProjectTemplates.Instantiate(path);
                t.Check(born.Name == "Sans titre" && born.CreatedAt == "" && born.SaveId == "" && born.DiskIndex == null, "un projet né d'un modèle est neuf : sans nom, sans date, sans fichier");
                var bw = born.Category(Project.KeyWritings);
                t.Check(bw.Children.Count == 1 && bw.Children[0].Title == "Nouveau livre" && bw.Children[0].Children.Count == 1 && !born.ReadOnlyNewerFormat, "…avec le livre neuf du modèle, modifiable");
                t.Check(ProjectTemplates.Describe(born).StartsWith("un livre vide à son format, ") && !ProjectTemplates.Describe(born).Contains("dictionnaire"), "le modèle se décrit en une ligne (" + ProjectTemplates.Describe(born) + ")");
                var wordsPath = ProjectTemplates.Save(project, "Avec dictionnaire", true);
                var bornWords = ProjectTemplates.Instantiate(wordsPath);
                t.Check(bornWords.Lexicon.Count == 1 && ProjectTemplates.Describe(bornWords).EndsWith(", 1 mot du dictionnaire"), "un modèle enregistré avec le dictionnaire le rend au projet qui en naît (" + ProjectTemplates.Describe(bornWords) + ")");
                ProjectTemplates.Delete(wordsPath);
                ProjectTemplates.Save(project, "Roman en trois actes");
                t.Check(ProjectTemplates.All().Count == 1, "enregistrer sous le même nom remplace");
                t.Check(ProjectTemplates.CleanName(" a/b:c? ") == "a_b_c_" && ProjectTemplates.CleanName("   ") == "" && !ProjectTemplates.Exists(""), "un nom de modèle devient un nom de fichier sûr");
                // 25/11 : la même liste d'interdits sur les trois systèmes (FileNames), pas celle du système où l'on tourne.
                t.Check(FileNames.Replace("a\\b<c>d|e*f\"g\th.png") == "a_b_c_d_e_f_g_h.png", "les caractères que Windows refuse sont remplacés sur tous les systèmes, l'extension gardée");
                t.Check(FileNames.Clean("Acte 1 : le départ ?", "x") == "Acte 1 _ le départ _" && FileNames.Clean("  fin. . ", "x") == "fin" && FileNames.Clean(" .. ", "x") == "x" && FileNames.Clean(null, "x") == "x", "…les espaces des bords et les points de fin tombent ; un nom vide prend le nom de repli");
                t.Check(FileNames.Clean("con", "x") == "_con" && FileNames.Clean("NUL.txt", "x") == "_NUL.txt" && FileNames.Clean("Console", "x") == "Console" && FileNames.Clean("LPT1", "x") == "_LPT1", "…et un nom réservé par Windows (CON, NUL, LPT1…) est écarté d'un « _ »");
                var threw = false;
                try { ProjectTemplates.Save(project, "  "); } catch (ArgumentException) { threw = true; }
                t.Check(threw, "un modèle sans nom est refusé");
                var outside = Path.Combine(dir, "ailleurs.plot");
                File.WriteAllText(outside, "x");
                ProjectTemplates.Delete(outside);
                t.Check(File.Exists(outside), "supprimer un modèle ne supprime jamais un fichier hors du dossier des modèles");
                ProjectTemplates.Delete(path);
                t.Check(!File.Exists(path) && ProjectTemplates.All().Count == 0, "un modèle se supprime");
            }
            finally
            {
                ProjectTemplates.FolderOverride = previous;
            }
        }
    }
}

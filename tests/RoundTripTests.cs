using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UniversSale.Model;
using UniversSale.Persistence;

namespace UniversSale.Tests
{
    /// <summary>C1 — le test qui compte le plus : un projet exerçant TOUS les
    /// champs du modèle, Save → Load → comparaison structurelle. C'est lui qui
    /// attrape les champs perdus (AllowWidows du batch 24), les ids en
    /// doublon, la version, et tous les futurs oublis de sérialisation.</summary>
    public static class RoundTripTests
    {
        // Transitoires légitimes : jamais persistés, différents par nature.
        private static readonly HashSet<string> Skip = new HashSet<string>
        {
            "BinderItem.Parent",            // cycle, reconstruit par RelinkParents
            "Project.LoadedFormatVersion",  // 0 avant écriture, 6 après lecture
            "TextParagraph.StartOnRecto",   // posés par le compilateur, jamais
            "TextParagraph.Decor"           //   dans le .plot
        };

        public static void Run(Harness t)
        {
            t.Suite("C1 — aller-retour .plot");
            var dir = TestDir();

            FullRoundTrip(t, dir);
            PageBreakRoundTrip(t, dir);
            HyphenSentinel(t, dir);
            FutureVersionReadOnly(t, dir);
            TruncatedEntryTolerated(t, dir);
            DuplicateIdsReassigned(t, dir);
            DuplicateIdsRefusedAtSave(t, dir);
            DeepJsonRejected(t);

            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        private static string TestDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests");
            Directory.CreateDirectory(dir);
            return dir;
        }

        // ------------------------------------------------------------ cas nominal

        private static void FullRoundTrip(Harness t, string dir)
        {
            var project = BuildFullProject();
            var path = Path.Combine(dir, "full.plot");
            PlotFile.Save(project, path);
            var warnings = new List<string>();
            var loaded = PlotFile.Load(path, warnings);

            t.Equal(0, warnings.Count, "aucune réserve au chargement nominal");
            t.Check(!loaded.ReadOnlyNewerFormat, "format courant : pas de lecture seule");
            var diffs = DeepCompare.Diff(project, loaded, Skip);
            foreach (var diff in diffs) t.Info("diff : " + diff);
            t.Equal(0, diffs.Count, "projet identique champ à champ après aller-retour");
        }

        private static void PageBreakRoundTrip(Harness t, string dir)
        {
            // pb est persisté quand l'utilisateur le pose (v4) — vérifié à part
            // car le champ sert aussi de transitoire de compilation.
            var project = Project.CreateNew();
            var item = new BinderItem { Kind = ItemKind.Text, Title = "pb" };
            item.Document = new TextDocument();
            var paragraph = new TextParagraph { PageBreakBefore = true };
            paragraph.Runs.Add(new TextRun { Text = "après le saut" });
            item.Document.Paragraphs.Add(paragraph);
            project.Category(Project.KeyWritings).Children.Add(item);
            project.RelinkParents();

            var path = Path.Combine(dir, "pb.plot");
            PlotFile.Save(project, path);
            var loaded = PlotFile.Load(path);
            var reread = FindItem(loaded, "pb");
            t.Check(reread.Document.Paragraphs[0].PageBreakBefore,
                "PageBreakBefore persiste (clé pb)");
        }

        private static void HyphenSentinel(Harness t, string dir)
        {
            // A7 : le défaut passe à 3, mais la sentinelle de sérialisation
            // reste 2 — un style réglé à 2 doit survivre à l'aller-retour,
            // et un style neuf doit garder 3.
            t.Equal(3, new ParagraphStyle().HyphenMinAfter,
                "défaut français : 3 lettres minimum rejetées");
            var project = Project.CreateNew();
            project.Styles.Find("body").HyphenMinAfter = 2;
            var fresh = new ParagraphStyle { Id = "neuf", Name = "Neuf" };
            project.Styles.Styles.Add(fresh);
            var path = Path.Combine(dir, "hyphen.plot");
            PlotFile.Save(project, path);
            var loaded = PlotFile.Load(path);
            t.Equal(2, loaded.Styles.Find("body").HyphenMinAfter,
                "un style réglé à 2 reste à 2 (pas de migration)");
            t.Equal(3, loaded.Styles.Find("neuf").HyphenMinAfter,
                "un style neuf porte le nouveau défaut 3");
        }

        // ------------------------------------------------------------ cas dégradés

        private static void FutureVersionReadOnly(Harness t, string dir)
        {
            var path = Path.Combine(dir, "future.plot");
            WriteZip(path, new Dictionary<string, string>
            {
                { "manifest.json", "{\"version\":999,\"name\":\"Futur\",\"binder\":[]}" }
            });
            var loaded = PlotFile.Load(path);
            t.Check(loaded.ReadOnlyNewerFormat,
                "version future → ouvert en lecture seule");
            t.Equal(999, loaded.LoadedFormatVersion, "version future lue");

            WriteZip(path, new Dictionary<string, string>
            {
                { "manifest.json", "{\"name\":\"Sans version\",\"binder\":[]}" }
            });
            var old = PlotFile.Load(path);
            t.Check(!old.ReadOnlyNewerFormat, "version absente = v1, chargement normal");
            t.Equal(1, old.LoadedFormatVersion, "version absente lue comme 1");
        }

        private static void TruncatedEntryTolerated(Harness t, string dir)
        {
            var project = BuildFullProject();
            var path = Path.Combine(dir, "truncated.plot");
            PlotFile.Save(project, path);

            // Tronque le texte d'UN document : le projet doit s'ouvrir quand
            // même, l'item devenir vide et marqué, les autres rester intacts.
            string victim = null;
            foreach (var item in project.AllItems())
                if (item.Kind == ItemKind.Text && item.Title == "Chapitre complet")
                { victim = item.Id; break; }
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("texts/" + victim + ".json");
                entry.Delete();
                var broken = archive.CreateEntry("texts/" + victim + ".json");
                using (var writer = new StreamWriter(broken.Open(), new UTF8Encoding(false)))
                    writer.Write("{\"paragraphs\":[{\"runs\":[{\"t\":\"tronqu"); // coupé net
            }

            var warnings = new List<string>();
            var loaded = PlotFile.Load(path, warnings);
            var damaged = FindItem(loaded, "Chapitre complet");
            var intact = FindItem(loaded, "Fiche héroïne");
            t.Check(damaged != null, "l'item au texte tronqué existe toujours");
            t.Check(damaged.LoadDamaged, "l'item tronqué est marqué endommagé");
            t.Equal("Chapitre complet", damaged.Title, "titre préservé malgré le texte perdu");
            t.Check(warnings.Count > 0, "le chargement signale la casse");
            t.Check(intact != null && !intact.LoadDamaged
                && intact.Document.Paragraphs.Count > 0,
                "les autres documents restent intacts");
        }

        private static void DuplicateIdsReassigned(Harness t, string dir)
        {
            var path = Path.Combine(dir, "dup.plot");
            WriteZip(path, new Dictionary<string, string>
            {
                { "manifest.json",
                  "{\"version\":6,\"name\":\"Dup\",\"binder\":[{\"id\":\"writings\","
                  + "\"title\":\"Écrits\",\"kind\":\"category\",\"category\":\"writings\","
                  + "\"children\":[{\"id\":\"AAA\",\"title\":\"Un\",\"kind\":\"text\"},"
                  + "{\"id\":\"AAA\",\"title\":\"Deux\",\"kind\":\"text\"}]}]}" },
                { "texts/AAA.json",
                  "{\"paragraphs\":[{\"runs\":[{\"t\":\"partagé\"}]}]}" }
            });
            var warnings = new List<string>();
            var loaded = PlotFile.Load(path, warnings);
            var ids = new HashSet<string>();
            var clash = false;
            foreach (var item in loaded.AllItems())
                if (!ids.Add(item.Id)) clash = true;
            t.Check(!clash, "plus aucun id en doublon après chargement");
            t.Check(warnings.Count > 0, "la réattribution est signalée");
            var one = FindItem(loaded, "Un");
            var two = FindItem(loaded, "Deux");
            t.Check(one != null && two != null, "les deux items survivent");
            t.Equal("partagé", two.Document.ToPlainText(),
                "le contenu est conservé pour l'item ré-identifié");
        }

        private static void DuplicateIdsRefusedAtSave(Harness t, string dir)
        {
            var project = Project.CreateNew();
            var a = new BinderItem { Kind = ItemKind.Text, Title = "A", Id = "X" };
            var b = new BinderItem { Kind = ItemKind.Text, Title = "B", Id = "X" };
            project.Category(Project.KeyWritings).Children.Add(a);
            project.Category(Project.KeyWritings).Children.Add(b);
            project.RelinkParents();
            var path = Path.Combine(dir, "refused.plot");
            t.Throws<InvalidOperationException>(
                delegate { PlotFile.Save(project, path); },
                "deux items de même id : la sauvegarde refuse d'écrire");
        }

        private static void DeepJsonRejected(Harness t)
        {
            var deep = new StringBuilder();
            for (var i = 0; i < 400; i++) deep.Append('[');
            for (var i = 0; i < 400; i++) deep.Append(']');
            t.Throws<JsonException>(
                delegate { Json.Parse(deep.ToString()); },
                "JSON à 400 niveaux : rejet propre en lecture");

            object nested = "fond";
            for (var i = 0; i < 400; i++)
            {
                var box = new Dictionary<string, object>();
                box["v"] = nested;
                nested = box;
            }
            var captured = nested;
            t.Throws<JsonException>(
                delegate { Json.Write(captured); },
                "structure à 400 niveaux : rejet propre en écriture");
        }

        // ------------------------------------------------------------ fixture

        /// <summary>Un projet qui exerce tous les champs persistés du modèle :
        /// arborescence à étages, styles complets, runs de tous formats,
        /// notes, annotations, images, médias, fiches et modèles, livre,
        /// gabarit de pages, pages extra, en-têtes/pieds, journal.</summary>
        private static Project BuildFullProject()
        {
            var project = Project.CreateNew();
            project.Author = "Autrice d'essai";
            project.SeparatorText = "· · ·";
            project.SeparatorFont = "Georgia";
            project.SeparatorSizePt = 14;
            project.CustomColors.Add("#AA3366");
            project.CustomColors.Add("#004488");
            project.HyphenExceptions.Add("Marabout");
            project.HyphenExceptions.Add("wisteria");
            project.Journal.DailyGoal = 500;
            project.Journal.LastCelebrated = "2026-08-07";
            project.Journal.Days.Add(new JournalDay { Date = "2026-08-06", Words = 812 });
            project.Journal.Days.Add(new JournalDay { Date = "2026-08-07", Words = 43 });
            project.Page = new PageSetup
            {
                PageWidthMm = 148, PageHeightMm = 210,
                MarginTopMm = 21, MarginBottomMm = 22,
                MarginLeftMm = 28, MarginRightMm = 19,
                Columns = 2, ShowMarginGuides = false, LineNumbers = true,
                Hyphenation = true, FooterPageNumbers = false,
                FooterFont = "Georgia", FooterSizePt = 9
            };

            project.Styles.Styles.Add(new ParagraphStyle
            {
                Id = "special", Name = "Spécial", FontFamily = "Palatino Linotype",
                FontSize = 17.5, Bold = true, Italic = true, Color = "#223344",
                Align = "right", SpaceBefore = 6, SpaceAfter = 7,
                FirstLineIndent = 11, LeftIndent = 13, RightIndent = 9,
                LastLineIndent = 21, LineHeight = 24, Ligatures = false,
                HyphenationEnabled = false, HyphenMinWordLength = 6,
                HyphenMinBefore = 3, HyphenMinAfter = 4, HyphenConsecutiveLimit = 2,
                JustifyWordMin = 85, JustifyWordOpt = 98, JustifyWordMax = 130,
                JustifyLetterMin = -2, JustifyLetterOpt = 1, JustifyLetterMax = 4,
                JustifyGlyphMin = 97, JustifyGlyphOpt = 99, JustifyGlyphMax = 103,
                AutoLeadingPercent = 130, KeepWithPrevious = false,
                KeepNextLines = 2, KeepLinesTogether = true
            });

            var template = new SheetTemplate { Name = "Créature" };
            template.Fields.Add(new SheetField { Name = "Espèce", Kind = "text" });
            template.Fields.Add(new SheetField { Name = "Biographie", Kind = "long" });
            project.Templates.Add(template);

            var writings = project.Category(Project.KeyWritings);
            var research = project.Category(Project.KeyResearch);
            var sheets = project.Category(Project.KeySheets);

            // — Le chapitre complet : tous les formats de runs.
            var chapter = new BinderItem
            {
                Kind = ItemKind.Text,
                Title = "Chapitre complet",
                Synopsis = "Le synopsis du chapitre.",
                Notes = "Notes de travail.",
                Icon = "svg:file-text:#AA3366",
                Status = "revise",
                CardColor = "#5588AA",
                Page = new PageSetup { PageWidthMm = 110, PageHeightMm = 180 },
                Header = new HeaderFooter
                {
                    Text = "{titre}", FontFamily = "Georgia", SizePt = 8.5,
                    Bold = true, Italic = false, Align = "left"
                },
                Footer = new HeaderFooter
                {
                    Text = "{page}/{pages}", SizePt = 9, Align = "right",
                    Rich = RichZone("— {page} —")
                }
            };
            chapter.Document = BuildFullDocument(project);
            writings.Children.Add(chapter);

            var child = new BinderItem { Kind = ItemKind.Text, Title = "Scène enfant" };
            child.Document = SimpleDocument("Texte de la scène enfant.");
            chapter.Children.Add(child); // texte-parent, modèle Scrivener

            // — Le livre : métadonnées, gabarit, partie, pages extra, TdM.
            var book = new BinderItem
            {
                Kind = ItemKind.Book,
                Title = "Le Livre",
                Book = new BookInfo
                {
                    Subtitle = "Sous-titre", AuthorOverride = "Nom de plume",
                    Publisher = "Éditions du Marabout", Collection = "Plumes",
                    Isbn = "978-2-1234-5680-3", Year = "2026", BleedMm = 5,
                    Template = new PageSetup
                    {
                        PageWidthMm = 140, PageHeightMm = 216,
                        MarginTopMm = 20, MarginBottomMm = 20,
                        MarginLeftMm = 30, MarginRightMm = 20
                    }
                }
            };
            writings.Children.Add(book);

            var pageTemplate = new BinderItem
            {
                Kind = ItemKind.PageTemplate,
                Title = "Gabarit courant",
                TemplateColor = "#CC8800",
                HeaderRecto = new HeaderFooter { Text = "{livre}", Align = "right" },
                HeaderVerso = new HeaderFooter { Text = "{titre}", Align = "left" },
                FooterRecto = new HeaderFooter { Text = "{page}", Align = "right", Rich = RichZone("{page}") },
                FooterVerso = new HeaderFooter { Text = "{page}", Align = "left" },
                HeaderGapMm = -4, FooterGapMm = 6,
                HeaderHideFirst = true, FooterHideFirst = false
            };
            book.Children.Add(pageTemplate);

            var part = new BinderItem { Kind = ItemKind.Folder, Title = "Partie I", CardColor = "#227755" };
            book.Children.Add(part);
            var inBook = new BinderItem
            {
                Kind = ItemKind.Text,
                Title = "Chapitre du livre",
                PageTemplateId = pageTemplate.Id,
                Page = new PageSetup { PageWidthMm = 140, PageHeightMm = 216 }
            };
            inBook.Document = SimpleDocument("Chapitre dans la partie.");
            part.Children.Add(inBook);
            var extra = new BinderItem { Kind = ItemKind.Text, Title = "Faux-titre", IsExtraPage = true };
            extra.Document = SimpleDocument("LE LIVRE");
            book.Children.Add(extra);
            var toc = new BinderItem { Kind = ItemKind.Text, Title = "Table des matières", IsExtraPage = true, IsToc = true };
            toc.Document = SimpleDocument("Sommaire");
            book.Children.Add(toc);

            // — Fiche : modèle, champs, infos libres, portrait.
            var sheet = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = "Fiche héroïne",
                TemplateId = template.Id,
                Notes = "Fiche importante."
            };
            sheet.FieldValues[template.Fields[0].Id] = "Marabout cendré";
            sheet.FieldValues[template.Fields[1].Id] = "Longue biographie.";
            sheet.FreeInfo.Add(new InfoEntry { Title = "Devise", Value = "Toujours plumer" });
            sheet.Document = SimpleDocument("Corps wiki de la fiche.");
            sheet.ImageId = project.AddImage(new byte[] { 137, 80, 78, 71, 1, 2, 3, 4 }, ".png");
            sheets.Children.Add(sheet);

            // — Média brut.
            var media = new BinderItem
            {
                Kind = ItemKind.Media,
                Title = "Carnet scanné",
                MediaExtension = ".txt",
                MediaBytes = Encoding.UTF8.GetBytes("contenu du média")
            };
            research.Children.Add(media);

            project.RelinkParents();
            return project;
        }

        private static TextDocument BuildFullDocument(Project project)
        {
            var document = new TextDocument();

            var first = new TextParagraph
            {
                StyleId = "special",
                AlignOverride = "center",
                AllowWidows = true
            };
            first.Runs.Add(new TextRun
            {
                Text = "Tous les formats : ",
                Bold = true, Italic = false, Underline = true, Strike = false,
                Weight = "SemiBold", Tracking = 25.5, FontFamily = "Consolas",
                FontSize = 13.5, Color = "#112233", Highlight = "#FFEE99"
            });
            first.Runs.Add(new TextRun { Text = "annoté", AnnotationId = "ann1" });
            first.Runs.Add(new TextRun { IsLineBreak = true });
            first.Runs.Add(new TextRun { Text = "après le saut de ligne" });
            first.Runs.Add(new TextRun { FootnoteId = "note1" });
            document.Paragraphs.Add(first);

            var list = new TextParagraph { ListKind = "bullet", PageBreakBefore = true };
            list.Runs.Add(new TextRun { Text = "une puce" });
            document.Paragraphs.Add(list);
            var numbered = new TextParagraph { ListKind = "number" };
            numbered.Runs.Add(new TextRun { Text = "un numéro" });
            document.Paragraphs.Add(numbered);

            var rule = new TextParagraph();
            rule.Runs.Add(new TextRun { IsRule = true });
            document.Paragraphs.Add(rule);

            var image = new TextParagraph();
            image.Runs.Add(new TextRun
            {
                ImageId = project.AddImage(new byte[] { 9, 8, 7, 6, 5 }, ".jpg")
            });
            document.Paragraphs.Add(image);

            document.Footnotes.Add(new Footnote { Id = "note1", Text = "La note de bas de page." });
            document.Annotations.Add(new Annotation
            {
                Id = "ann1",
                Text = "Revoir cette formulation.",
                Created = "2026-08-07 10:00",
                Resolved = true
            });
            return document;
        }

        private static TextDocument SimpleDocument(string text)
        {
            var document = new TextDocument();
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = text });
            document.Paragraphs.Add(paragraph);
            return document;
        }

        private static TextParagraph RichZone(string text)
        {
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = text, Italic = true, FontSize = 9 });
            return paragraph;
        }

        private static BinderItem FindItem(Project project, string title)
        {
            foreach (var item in project.AllItems())
                if (item.Title == title) return item;
            return null;
        }

        private static void WriteZip(string path, Dictionary<string, string> entries)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var stream = new FileStream(path, FileMode.Create))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                foreach (var kv in entries)
                {
                    var entry = archive.CreateEntry(kv.Key);
                    using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                        writer.Write(kv.Value);
                }
        }
    }
}

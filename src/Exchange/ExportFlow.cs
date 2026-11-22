using System;
using System.Collections.Generic;
using System.Globalization;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Un lien d'un flux exporté : la plage (offsets plats du
    /// paragraphe, marques retirées) et l'élément visé (son identifiant).</summary>
    public class ExportLink
    {
        public int Paragraph, Start, Length;
        public string Target;
    }

    /// <summary>Un repère d'un flux : un paragraphe qui est la destination
    /// d'un lien (Name = l'identifiant de l'élément) et/ou un signet du
    /// document (Title, Level 0 = premier rang).</summary>
    public class ExportMark
    {
        public int Paragraph;
        public string Name;
        public string Title;
        public int Level;
    }

    /// <summary>UN SEGMENT D'EXPORT : un flux de texte composé sur UNE mise en
    /// page (des écrits de même format, ou les fiches), ou un élément qui part
    /// en image à son propre format (un plan, une carte mentale).</summary>
    public class ExportSegment
    {
        public const string KindFlow = "flow", KindImage = "image";
        public string Kind = KindFlow;
        public TextDocument Document;
        public PageSetup Setup;
        public StyleSheet Styles;
        public readonly List<ExportMark> Marks = new List<ExportMark>();
        public readonly List<ExportLink> Links = new List<ExportLink>();
        public readonly List<BinderItem> Items = new List<BinderItem>(); // les éléments du segment, dans l'ordre
        public BinderItem Item { get { return Items.Count > 0 ? Items[0] : null; } }
        public bool IsSheets;
        public bool IsImage { get { return Kind == KindImage; } }
    }

    /// <summary>Du plan aux segments : les écrits regroupés par mise en page
    /// (compilés, titres de chapitres, page de titre), les fiches mises en
    /// document, les plans et cartes mentales en images ; les marques de
    /// [[liens]] tombent, et les liens vers un élément exporté sont relevés.</summary>
    public static class ExportFlow
    {
        public const string StyleSheetTitle = "export-sheet-title", StyleSheetKind = "export-sheet-kind",
            StyleSheetGroup = "export-sheet-group", StyleSheetField = "export-sheet-field", StyleSection = "export-section";

        public static List<ExportSegment> Build(Project project, ExportPlan plan, ExportPreset preset, ExportMeta meta)
        {
            var segments = new List<ExportSegment>();
            if (project == null || plan == null || preset == null) return segments;
            meta = meta ?? new ExportMeta();
            var targets = Targets(plan);
            var mixed = (plan.Texts.Count > 0 ? 1 : 0) + (plan.Sheets.Count > 0 ? 1 : 0) + (plan.Plans.Count + plan.MindMaps.Count + plan.Maps.Count > 0 ? 1 : 0) > 1;

            // ---- les écrits, par mise en page
            var groups = new List<List<BinderItem>>();
            var setups = new List<PageSetup>();
            foreach (var text in plan.Texts)
            {
                var setup = ExportPlan.PageOf(text, project, preset);
                var last = setups.Count - 1;
                if (last >= 0 && SamePage(setups[last], setup)) groups[last].Add(text);
                else { groups.Add(new List<BinderItem> { text }); setups.Add(setup); }
            }
            var headings = preset.ChapterHeadings && plan.Texts.Count > 1;
            var number = 0;
            // Le rendu par niveau (20/11) : ce qui précède chaque écrit — genre, livre, dossier, sous-dossier.
            var splitTypes = preset.SplitTypes && mixed;
            int[] ranks;
            var before = ExportLevels.Before(plan.Texts, preset, splitTypes && plan.Texts.Count > 0 ? ExportLevels.TitleTexts : null, out ranks);
            var leveled = preset.SplitsLevels;
            var ownPage = preset.SplitOwnPage;
            var recto = preset.RectoStarts && preset.PageBreakPerText;
            var seen = 0; // les écrits des segments d'avant
            for (var g = 0; g < groups.Count; g++)
            {
                var texts = groups[g];
                var scope = plan.Book ?? texts[0];
                var options = new CompileOptions
                {
                    TitlePage = preset.TitlePage && g == 0,
                    Title = string.IsNullOrEmpty((meta.Title ?? "").Trim()) ? null : meta.Title.Trim(),
                    Subtitle = (meta.Subtitle ?? "").Trim(),
                    Author = (meta.Author ?? "").Trim(),
                    Colophon = meta.Colophon(),
                    ChapterHeadings = false, // posés ici : la numérotation court d'un segment à l'autre
                    PageBreakPerText = preset.PageBreakPerText,
                    RectoChapterStarts = preset.RectoStarts,
                    Separator = project.Styles.SeparatorFor(scope).Content ?? "***",
                    Decor = preset.HeadersFooters
                };
                var starts = new List<int>();
                var compiled = Compiler.Build(project, plan.Book, texts, options, starts);
                var head = options.TitlePage && starts.Count > 0 ? starts[0] : 0; // la page de titre : intouchable
                // Les annotations des écrits suivent (Word en fait des commentaires).
                foreach (var text in texts)
                    if (text.Document != null) compiled.Annotations.AddRange(text.Document.Annotations);
                var segment = new ExportSegment { Setup = setups[g].Clone() };
                if (!preset.HeadersFooters) segment.Setup.FooterPageNumbers = true; // sans gabarit : un folio centré
                segment.Items.AddRange(texts);

                // Les titres de niveau et de chapitre : insérés de la fin vers le début, les rangs d'avant ne bougent pas.
                var titles = new string[texts.Count];
                for (var i = 0; i < texts.Count; i++)
                {
                    number++;
                    titles[i] = (preset.NumberChapters ? number + ". " : "") + texts[i].Title;
                }
                for (var i = texts.Count - 1; i >= 0; i--)
                {
                    var levels = before[seen + i];
                    if (!headings && levels.Count == 0) continue;
                    var at = Math.Min(starts[i], compiled.Paragraphs.Count);
                    // Un écrit VIDE n'a posé aucun paragraphe : son rang est celui de l'écrit suivant, dont il ne doit rien prendre.
                    var empty = texts[i].Document == null || texts[i].Document.Paragraphs.Count == 0;
                    // Un séparateur de scène ouvre l'écrit : les titres viennent après lui — sauf s'ils prennent
                    // leur propre page, qui sépare mieux que lui : il tombe.
                    if (!empty && at < compiled.Paragraphs.Count && compiled.Paragraphs[at].StyleId == StyleSheet.SeparatorId && !options.PageBreakPerText)
                    {
                        if (levels.Count > 0 && ownPage)
                        {
                            compiled.Paragraphs.RemoveAt(at);
                            for (var j = i + 1; j < texts.Count; j++) starts[j]--;
                        }
                        else at++;
                    }
                    var inserted = new List<TextParagraph>();
                    foreach (var level in levels) inserted.Add(ExportLevels.Paragraph(level));
                    TextParagraph heading = null;
                    if (headings)
                    {
                        heading = new TextParagraph { StyleId = "title2" };
                        heading.Runs.Add(new TextRun { Text = titles[i] });
                        inserted.Add(heading);
                    }
                    var first = inserted[0];
                    var next = !empty && at < compiled.Paragraphs.Count ? compiled.Paragraphs[at] : null;
                    if (next == null)
                    {
                        first.PageBreakBefore = options.PageBreakPerText && (i > 0 || options.TitlePage);
                        first.StartOnRecto = first.PageBreakBefore && options.RectoChapterStarts;
                        if (options.Decor) foreach (var paragraph in inserted) paragraph.Decor = PageDecor.For(texts[i], project);
                    }
                    else
                    {
                        first.PageBreakBefore = next.PageBreakBefore;
                        first.StartOnRecto = next.StartOnRecto;
                        foreach (var paragraph in inserted) paragraph.Decor = next.Decor;
                        if (next.PageBreakBefore || next.StartOnRecto)
                        {
                            var shell = PivotEdit.CloneParagraphShell(next);
                            shell.PageBreakBefore = false;
                            shell.StartOnRecto = false;
                            shell.Runs.AddRange(next.Runs);
                            compiled.Paragraphs[at] = next = shell;
                        }
                    }
                    // Des titres de niveau sur leur propre page : un saut avant eux, un saut après.
                    if (levels.Count > 0 && ownPage)
                    {
                        if (at > 0) { first.PageBreakBefore = true; first.StartOnRecto = recto; } // en tête de flux, la page est déjà neuve
                        var after = heading ?? next;
                        if (after != null)
                        {
                            if (after == next)
                            {
                                var shell = PivotEdit.CloneParagraphShell(next);
                                shell.Runs.AddRange(next.Runs);
                                compiled.Paragraphs[at] = after = shell;
                            }
                            after.PageBreakBefore = true;
                            after.StartOnRecto = recto;
                        }
                    }
                    compiled.Paragraphs.InsertRange(at, inserted);
                    for (var j = i + 1; j < texts.Count; j++) starts[j] += inserted.Count;
                    starts[i] = at + levels.Count; // le repère de l'écrit : son titre, ou son premier paragraphe
                }
                // L'interligne du document, quand tous les écrits du segment le partagent.
                var spacing = texts[0].Document == null ? 1 : texts[0].Document.LineSpacing;
                foreach (var text in texts) if (text.Document == null || Math.Abs(text.Document.LineSpacing - spacing) > 0.001) spacing = 1;
                compiled.LineSpacing = spacing;

                var lastParagraph = Math.Max(0, compiled.Paragraphs.Count - 1);
                for (var i = 0; i < texts.Count; i++)
                {
                    var levels = before[seen + i];
                    for (var k = 0; k < levels.Count; k++)
                        segment.Marks.Add(new ExportMark { Paragraph = Math.Max(0, Math.Min(starts[i] - levels.Count + k, lastParagraph)), Title = levels[k].Title, Level = levels[k].Level });
                    segment.Marks.Add(new ExportMark { Paragraph = Math.Max(0, Math.Min(starts[i], lastParagraph)), Name = texts[i].Id, Title = titles[i], Level = ranks[seen + i] });
                }
                seen += texts.Count;
                var sheet = project.Styles.EffectiveFor(scope);
                segment.Styles = ExportTypography.Styles(leveled ? ExportLevels.WithStyles(sheet) : sheet, preset);
                segment.Document = ExportTypography.Document(StripLinks(compiled, targets, preset.Links ? segment.Links : null), preset);
                if (preset.DropBlanks) ExportBlankPages.Apply(segment, g == 0 ? head : 0);
                segments.Add(segment);
            }

            // ---- les fiches
            if (plan.Sheets.Count > 0)
            {
                var segment = Sheets(project, plan.Sheets, targets, preset, mixed);
                if (segment != null)
                {
                    if (preset.DropBlanks) ExportBlankPages.Apply(segment, 0);
                    segments.Add(segment);
                }
            }

            // ---- les plans et les cartes mentales : une image, une page à sa taille
            Images(project, plan.Plans, ExportLevels.TitlePlans, preset, splitTypes, segments);
            Images(project, plan.MindMaps, ExportLevels.TitleMindMaps, preset, splitTypes, segments);
            Images(project, plan.Maps, ExportLevels.TitleMaps, preset, splitTypes, segments);
            return segments;
        }

        /// <summary>L'EPUB d'un plan (20/11) : ses chapitres, ses titres de
        /// niveau (livre, dossier, sous-dossier — un EPUB ne porte que des
        /// écrits, donc jamais de titre de genre), sa couverture, et les
        /// options tirées du préréglage et des informations du livre.</summary>
        public static EpubPlan EpubFor(Project project, ExportPlan plan, ExportPreset preset, ExportMeta meta, out EpubOptions options)
        {
            meta = meta ?? new ExportMeta();
            var epub = new EpubPlan { Root = plan.Book ?? (plan.Texts.Count > 0 ? plan.Texts[0] : null) };
            epub.Chapters.AddRange(plan.Texts);
            epub.Skipped.AddRange(plan.Dynamic);
            int[] ranks;
            var before = ExportLevels.Before(plan.Texts, preset, null, out ranks);
            for (var i = 0; i < before.Count; i++)
            {
                foreach (var level in before[i])
                    epub.Parts.Add(new EpubPart { BeforeChapter = i, Title = level.Title, Level = level.Level, OwnPage = preset.SplitOwnPage });
                epub.ChapterLevels.Add(ranks[i]);
            }
            var cover = preset.Cover && meta.CoverImageId != null && project.FindImage(meta.CoverImageId) != null ? meta.CoverImageId : null;
            epub.CoverImageId = cover;
            epub.HasCover = cover != null;
            options = new EpubOptions
            {
                Title = (meta.Title ?? "").Trim(), Subtitle = (meta.Subtitle ?? "").Trim(), Author = (meta.Author ?? "").Trim(),
                Publisher = (meta.Publisher ?? "").Trim(), Identifier = (meta.Isbn ?? "").Trim(), Year = (meta.Year ?? "").Trim(),
                Language = string.IsNullOrEmpty((meta.Language ?? "").Trim()) ? "fr" : meta.Language.Trim(),
                IncludeCover = cover != null, ChapterHeadings = preset.ChapterHeadings, NumberChapters = preset.NumberChapters && preset.ChapterHeadings,
                TitlePage = preset.TitlePage, Colophon = meta.Colophon(),
                DropCaps = preset.DropCaps, OpeningSmallCaps = preset.OpeningSmallCaps, Kindle = preset.Kindle
            };
            return epub;
        }

        /// <summary>Les pages-images d'un genre. Avec « séparer par genre » :
        /// une page de titre les précède (si les titres prennent leur page et
        /// qu'on ne retire pas les pages blanches), et elles se rangent sous
        /// un signet à leur nom.</summary>
        private static void Images(Project project, List<BinderItem> items, string title, ExportPreset preset, bool splitTypes, List<ExportSegment> segments)
        {
            if (items.Count == 0) return;
            var page = splitTypes && preset.SplitOwnPage && !preset.DropBlanks;
            if (page)
            {
                var setup = ExportPages.Normalize(preset.Pages) == ExportPages.A4 ? ExportPages.A4Setup() : (project.Page ?? new PageSetup()).Clone();
                if (preset.PlainMargins) setup = ExportPages.Symmetric(setup);
                setup.FooterPageNumbers = true;
                var heading = new ExportSegment { Setup = setup, Styles = ExportTypography.Styles(ExportLevels.WithStyles(project.Styles), preset), Document = new TextDocument() };
                heading.Document.Paragraphs.Add(ExportLevels.Paragraph(new ExportLevelTitle { Title = title, StyleId = ExportLevels.StyleType }));
                heading.Marks.Add(new ExportMark { Paragraph = 0, Title = title, Level = 0 });
                segments.Add(heading);
            }
            for (var i = 0; i < items.Count; i++)
            {
                var segment = new ExportSegment { Kind = ExportSegment.KindImage };
                segment.Items.Add(items[i]);
                if (splitTypes && !page && i == 0) segment.Marks.Add(new ExportMark { Paragraph = -1, Title = title, Level = 0 });
                segment.Marks.Add(new ExportMark { Paragraph = -1, Name = items[i].Id, Title = items[i].Title, Level = splitTypes ? 1 : 0 });
                segments.Add(segment);
            }
        }

        /// <summary>Deux mises en page qui peuvent partager un flux : même
        /// format, mêmes marges, mêmes colonnes.</summary>
        public static bool SamePage(PageSetup a, PageSetup b)
        {
            return a != null && b != null && a.SameLayout(b) && a.Columns == b.Columns && a.LineNumbers == b.LineNumbers;
        }

        // ================================================================ les liens

        /// <summary>Les titres des éléments exportés → leur identifiant (le premier du nom gagne).</summary>
        private static List<KeyValuePair<string, string>> Targets(ExportPlan plan)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var group in new[] { plan.Sheets, plan.Texts, plan.Plans, plan.MindMaps, plan.Maps })
                foreach (var item in group)
                    if (!string.IsNullOrEmpty((item.Title ?? "").Trim())) list.Add(new KeyValuePair<string, string>(item.Title.Trim(), item.Id));
            return list;
        }

        private static string Resolve(List<KeyValuePair<string, string>> targets, string title)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0) return null;
            foreach (var pair in targets) if (Model.Links.SameTitle(pair.Key, title)) return pair.Value;
            return null;
        }

        /// <summary>Une copie du document sans les marques de [[liens]] ;
        /// links (non null) reçoit ceux dont la cible est exportée, en
        /// offsets du texte nettoyé.</summary>
        public static TextDocument StripLinks(TextDocument document, List<KeyValuePair<string, string>> targets, List<ExportLink> links)
        {
            var copy = new TextDocument { Footnotes = document.Footnotes, Annotations = document.Annotations, LineSpacing = document.LineSpacing };
            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var paragraph = document.Paragraphs[p];
                if (links != null)
                {
                    var removed = 0;
                    foreach (var link in Model.Links.Find(PivotEdit.FlatText(paragraph)))
                    {
                        removed += link.TextStart - link.Start;
                        var target = TimeLinks.IsTimeLink(link.Target) ? null : Resolve(targets, link.Target);
                        if (target != null && link.TextEnd > link.TextStart)
                            links.Add(new ExportLink { Paragraph = p, Start = link.TextStart - removed, Length = link.TextEnd - link.TextStart, Target = target });
                        removed += link.End - link.TextEnd;
                    }
                }
                copy.Paragraphs.Add(Model.Links.Strip(paragraph));
            }
            return copy;
        }

        // ================================================================ les fiches

        /// <summary>Les styles propres aux fiches exportées, dérivés de ceux du projet.</summary>
        private static StyleSheet SheetStyles(StyleSheet source)
        {
            var styles = source.Clone();
            var body = styles.Body ?? new ParagraphStyle { Id = "body" };
            var title1 = styles.Find("title1") ?? body;
            var title2 = styles.Find("title2") ?? body;

            var section = title1.Clone();
            section.Id = StyleSection; section.Name = "Export — catégorie de fiches";
            section.Align = "center"; section.FirstLineIndent = 0;
            styles.Styles.Add(section);

            var sheetTitle = title2.Clone();
            sheetTitle.Id = StyleSheetTitle; sheetTitle.Name = "Export — titre de fiche";
            sheetTitle.FontSize = Math.Max(title2.FontSize, body.FontSize * 1.5);
            sheetTitle.LineHeight = Math.Round(sheetTitle.FontSize * 1.25 * 100) / 100;
            sheetTitle.Align = "left"; sheetTitle.FirstLineIndent = 0; sheetTitle.SpaceBefore = 0; sheetTitle.SpaceAfter = 2; sheetTitle.KeepNextLines = 1;
            styles.Styles.Add(sheetTitle);

            var kind = body.Clone();
            kind.Id = StyleSheetKind; kind.Name = "Export — genre de fiche";
            kind.Italic = true; kind.Color = "#6B6B6B"; kind.Align = "left"; kind.FirstLineIndent = 0; kind.SpaceAfter = 10;
            styles.Styles.Add(kind);

            var group = body.Clone();
            group.Id = StyleSheetGroup; group.Name = "Export — groupe de champs";
            group.Bold = true; group.Weight = null; group.FontSize = Math.Round(body.FontSize * 1.12 * 100) / 100;
            group.LineHeight = Math.Round(group.FontSize * 1.25 * 100) / 100;
            group.Align = "left"; group.FirstLineIndent = 0; group.SpaceBefore = 12; group.SpaceAfter = 4; group.KeepNextLines = 1; group.HyphenationEnabled = false;
            styles.Styles.Add(group);

            var field = body.Clone();
            field.Id = StyleSheetField; field.Name = "Export — champ de fiche";
            field.Align = "left"; field.FirstLineIndent = 0; field.SpaceAfter = 2;
            styles.Styles.Add(field);
            return styles;
        }

        private sealed class SheetWriter
        {
            public TextDocument Document = new TextDocument();
            public ExportSegment Segment;
            public List<KeyValuePair<string, string>> Targets;
            public bool Links;
            /// <summary>Le titre de genre « Fiches » quand il coiffe la suite sans prendre sa page : ce qui le suit ne saute pas de page.</summary>
            public TextParagraph FlowHead;

            /// <summary>Vrai si le paragraphe qu'on vient d'ajouter doit ouvrir une page.</summary>
            public bool BreaksHere()
            {
                var count = Document.Paragraphs.Count;
                return count > 1 && !ReferenceEquals(Document.Paragraphs[count - 2], FlowHead);
            }

            public TextParagraph Line(string styleId)
            {
                var paragraph = new TextParagraph { StyleId = styleId };
                Document.Paragraphs.Add(paragraph);
                return paragraph;
            }

            public void Text(TextParagraph paragraph, string text, bool bold = false)
            {
                if (string.IsNullOrEmpty(text)) return;
                paragraph.Runs.Add(new TextRun { Text = text, Bold = bold ? (bool?)true : null });
            }

            /// <summary>Un texte qui mène à un élément exporté (cliquable dans un PDF).</summary>
            public void Linked(TextParagraph paragraph, string text, string targetId)
            {
                if (string.IsNullOrEmpty(text)) return;
                if (targetId == null || !Links) { Text(paragraph, text); return; }
                var start = PivotEdit.FlatLength(paragraph);
                paragraph.Runs.Add(new TextRun { Text = text }); // le PDF trace lui-même le filet du lien
                Segment.Links.Add(new ExportLink { Paragraph = Document.Paragraphs.Count - 1, Start = start, Length = text.Length, Target = targetId });
            }
        }

        private static ExportSegment Sheets(Project project, List<BinderItem> sheets, List<KeyValuePair<string, string>> targets, ExportPreset preset, bool mixed)
        {
            var selection = new EncyclopediaSelection { Scope = EncyclopediaSelection.ScopeSheets };
            foreach (var sheet in sheets) selection.Ids.Add(sheet.Id);
            var options = Encyclopedia.DefaultOptions(project);
            options.IncludeEmptySheets = true; // cochée à la main : elle part, même vide
            options.ExceptTag = "";            // ici, c'est le filtre de l'ampleur qui écarte un tag
            options.IncludeLexicon = false;
            options.CoverImageId = null;
            var encyclopedia = Encyclopedia.Build(project, selection, options);
            if (encyclopedia.SheetCount == 0) return null;

            var pages = ExportPages.Normalize(preset.Pages);
            var setup = pages == ExportPages.A4 ? ExportPages.A4Setup() : (project.Page ?? new PageSetup()).Clone();
            if (preset.PlainMargins) setup = ExportPages.Symmetric(setup);
            setup.FooterPageNumbers = true;
            var splitTypes = preset.SplitTypes && mixed;
            var sheetStyles = SheetStyles(project.Styles);
            var segment = new ExportSegment { Setup = setup, IsSheets = true, Styles = ExportTypography.Styles(splitTypes ? ExportLevels.WithStyles(sheetStyles) : sheetStyles, preset) };
            var writer = new SheetWriter { Segment = segment, Targets = targets, Links = preset.Links };
            var level = 0;
            if (mixed)
            {
                segment.Marks.Add(new ExportMark { Paragraph = 0, Title = ExportLevels.TitleSheets, Level = 0 });
                level = 1;
            }
            if (splitTypes)
            {
                // « Séparer par genre » (20/11) : le titre « Fiches », sur sa page ou en tête de la première.
                var head = ExportLevels.Paragraph(new ExportLevelTitle { Title = ExportLevels.TitleSheets, StyleId = ExportLevels.StyleType });
                writer.Document.Paragraphs.Add(head);
                if (!preset.SplitOwnPage) writer.FlowHead = head;
            }
            var several = encyclopedia.Sections.Count > 1 || (encyclopedia.Sections.Count == 1 && encyclopedia.Sections[0].Subsections.Count > 0);
            foreach (var section in encyclopedia.Sections) WriteSection(writer, section, project, level, several);
            foreach (var sheet in encyclopedia.AllSheets())
            {
                var item = project.FindById(sheet.Id);
                if (item != null) segment.Items.Add(item);
            }
            segment.Document = ExportTypography.Document(writer.Document, preset);
            return segment;
        }

        private static void WriteSection(SheetWriter writer, EncyclopediaSection section, Project project, int level, bool titled)
        {
            var sheetLevel = level;
            if (titled)
            {
                var heading = writer.Line(StyleSection);
                heading.PageBreakBefore = writer.BreaksHere();
                writer.Text(heading, section.Title);
                writer.Segment.Marks.Add(new ExportMark { Paragraph = writer.Document.Paragraphs.Count - 1, Title = section.Title, Level = level });
                sheetLevel = level + 1;
            }
            foreach (var sheet in section.Sheets) WriteSheet(writer, sheet, project, sheetLevel);
            foreach (var sub in section.Subsections) WriteSection(writer, sub, project, sheetLevel, true);
        }

        private static void WriteSheet(SheetWriter writer, EncyclopediaSheet sheet, Project project, int level)
        {
            var title = writer.Line(StyleSheetTitle);
            // Une fiche par page — sauf la première d'une catégorie, qui suit son titre.
            var count = writer.Document.Paragraphs.Count;
            title.PageBreakBefore = writer.BreaksHere() && writer.Document.Paragraphs[count - 2].StyleId != StyleSection;
            writer.Text(title, sheet.Title);
            writer.Segment.Marks.Add(new ExportMark { Paragraph = writer.Document.Paragraphs.Count - 1, Name = sheet.Id, Title = sheet.Title, Level = level });

            var kind = sheet.Subtitle ?? "";
            if (sheet.Tags.Count > 0) kind += (kind.Length > 0 ? " — " : "") + string.Join(", ", sheet.Tags.ToArray());
            if (kind.Length > 0) writer.Text(writer.Line(StyleSheetKind), kind);

            foreach (var group in sheet.Groups)
            {
                writer.Text(writer.Line(StyleSheetGroup), group.Title);
                foreach (var field in group.Fields)
                {
                    var line = writer.Line(StyleSheetField);
                    writer.Text(line, field.Label + " : ", true);
                    writer.Linked(line, field.Value, field.TargetId);
                }
            }

            // Le texte de la fiche (du Markdown) : ses paragraphes, ses [[liens]] relevés.
            var body = (sheet.BodyMarkdown ?? "").Trim();
            if (body.Length > 0)
            {
                if (sheet.Groups.Count > 0) writer.Text(writer.Line(StyleSheetGroup), "Notes");
                var imported = MarkdownExchange.Import(body, null, project);
                var offset = writer.Document.Paragraphs.Count;
                var local = new List<ExportLink>();
                var stripped = StripLinks(imported, writer.Targets, writer.Links ? local : null);
                foreach (var paragraph in stripped.Paragraphs) writer.Document.Paragraphs.Add(paragraph);
                foreach (var note in stripped.Footnotes) writer.Document.Footnotes.Add(note);
                foreach (var link in local) { link.Paragraph += offset; writer.Segment.Links.Add(link); }
            }

            if (sheet.Relations.Count > 0)
            {
                writer.Text(writer.Line(StyleSheetGroup), "Relations");
                foreach (var relation in sheet.Relations)
                {
                    var line = writer.Line(StyleSheetField);
                    if (!string.IsNullOrEmpty(relation.Kind)) writer.Text(line, relation.Kind + " : ", true);
                    writer.Linked(line, relation.Label, relation.TargetId);
                }
            }
            if (sheet.Evolution.Count > 0)
            {
                writer.Text(writer.Line(StyleSheetGroup), "Évolution");
                foreach (var step in sheet.Evolution)
                {
                    var line = writer.Line(StyleSheetField);
                    var target = step.TextId != null && HasTarget(writer.Targets, step.TextId) ? step.TextId : null;
                    if (step.Label.Length > 0) { writer.Linked(line, step.Label, target); if (step.Note.Length > 0) writer.Text(line, " — "); }
                    writer.Text(line, step.Note);
                }
            }
            if (sheet.Presence.Count > 0)
            {
                writer.Text(writer.Line(StyleSheetGroup), "Présence dans les écrits");
                foreach (var row in sheet.Presence)
                {
                    var line = writer.Line(StyleSheetField);
                    writer.Linked(line, row.TextTitle, Resolve(writer.Targets, row.TextTitle));
                    writer.Text(line, " : " + row.Count + (row.Count > 1 ? " mentions" : " mention"));
                }
            }
            if (sheet.Radar != null)
            {
                writer.Text(writer.Line(StyleSheetGroup), string.IsNullOrEmpty(sheet.Radar.Label) ? "Profil" : sheet.Radar.Label);
                foreach (var axis in sheet.Radar.Axes)
                {
                    var line = writer.Line(StyleSheetField);
                    writer.Text(line, axis.Key + " : ", true);
                    writer.Text(line, axis.Value.ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + " / " + sheet.Radar.Max);
                }
            }
        }

        private static bool HasTarget(List<KeyValuePair<string, string>> targets, string id)
        {
            foreach (var pair in targets) if (pair.Value == id) return true;
            return false;
        }
    }
}

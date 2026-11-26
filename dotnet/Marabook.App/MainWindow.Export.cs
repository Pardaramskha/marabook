using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Marabook.Exchange;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>L'EXPORT (refonte du 18/11/2026, Rémi) : le gestionnaire
    /// d'export (Ctrl+E) et ses portes d'entrée — « L'écrit sélectionné »,
    /// « Publier » et « Créer un EPUB » d'un livre, le bouton PDF de
    /// l'éditeur —, les exports rapides (DOCX, PDF, EPUB, sans fenêtre), et
    /// l'écriture des fichiers : Word à sections, PDF en plusieurs parties
    /// (formats mêlés, pages-images, liens, signets), EPUB.</summary>
    public partial class MainWindow
    {
        // ============================================================= les portes d'entrée

        /// <summary>Fichier › Exporter › Gestionnaire d'export… : ce qui est
        /// ouvert est pré-coché (un écrit, une fiche, un plan — ou les écrits
        /// du livre ou du dossier ouvert).</summary>
        private void OpenExportManager()
        {
            OpenExportManager(ScopeOf(_current), null);
        }

        /// <summary>Fichier › Exporter › L'écrit sélectionné… : le gestionnaire, cet écrit déjà coché.</summary>
        private void ExportCurrentItem()
        {
            if (_current == null || (_current.Kind != ItemKind.Text && _current.Kind != ItemKind.Sheet))
            {
                MessageDialog.Show(this, "Sélectionnez d'abord un écrit (ou une fiche) à exporter.",
                    AppName, MessageButtons.OK, MessageIcon.Information);
                return;
            }
            OpenExportManager(ScopeOf(_current), null);
        }

        /// <summary>Le menu ⋮ d'une carte : le gestionnaire, cet élément coché.</summary>
        private void ExportItem(BinderItem item)
        {
            if (item == null) return;
            OpenExportManager(ScopeOf(item), null);
        }

        /// <summary>« Publier » (onglet Publication d'un livre) : le
        /// gestionnaire, le livre coché, le préréglage « BAT Imprimeur ».</summary>
        private void PublishBook(BinderItem book)
        {
            if (book == null) return;
            if (book.Book == null) book.Book = new BookInfo();
            OpenExportManager(ScopeOf(book), ExportPresets.Bat);
        }

        /// <summary>« Créer un EPUB » d'un livre : le gestionnaire, le livre coché, « EPUB Publiable ».</summary>
        private void ExportEpub(BinderItem root)
        {
            if (root == null) return;
            OpenExportManager(ScopeOf(root), root.Kind == ItemKind.Book ? ExportPresets.EpubPublish : ExportPresets.Epub);
        }

        /// <summary>Le bouton PDF du ruban : le gestionnaire, l'écrit coché, « PDF A4 ».</summary>
        private void ExportPdf()
        {
            OpenExportManager(ScopeOf(_current), ExportPresets.PdfA4);
        }

        /// <summary>Ce qu'un élément met dans l'ampleur : lui-même s'il
        /// s'exporte, sinon ce qu'il contient (le Hors-livre et la corbeille
        /// restent dehors).</summary>
        private List<string> ScopeOf(BinderItem item)
        {
            var ids = new List<string>();
            if (item == null || _project == null) return ids;
            var root = item.RootCategory();
            if (root != null && root.CategoryKey == Project.KeyTrash) return ids;
            CollectScope(item, ids, true);
            return ids;
        }

        private static void CollectScope(BinderItem item, List<string> ids, bool top)
        {
            if (!top && item.IsOutOfBook) return;
            if (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet || item.Kind == ItemKind.Plan || item.Kind == ItemKind.MindMap || item.Kind == ItemKind.Map) ids.Add(item.Id);
            if (item.Kind == ItemKind.Category && item.CategoryKey != Project.KeyWritings && item.CategoryKey != Project.KeySheets
                && item.CategoryKey != Project.KeyPlans && item.CategoryKey != Project.KeyMindMaps && item.CategoryKey != Project.KeyMaps) return;
            foreach (var child in item.Children) CollectScope(child, ids, false);
        }

        private async void OpenExportManager(List<string> ids, string presetId)
        {
            if (_project == null) return;
            CommitActive();
            var request = await ExportManagerDialog.Ask(this, _project, ids, presetId, PreviewExport, ComposeForPreview);
            if (request == null) return;
            if (request.WriteBackBook != null && request.Meta.ApplyTo(request.WriteBackBook, _project))
            {
                MarkDirty();
                _binder.Rebuild();
                if (_current == request.WriteBackBook) _bookView.Load(_current, _history, _project);
            }
            // « S'OFFRIR UN MONDE » (26/11) : le livre qui remplit toutes les conditions du dernier succès est salué
            // AVANT son export — « En avant ! » conclut l'export et fait tomber le succès.
            var offered = !AppSettings.Achievements.ContainsKey(Achievements.World) && !TutorialActive ? WorldBookOf(request) : null;
            if (offered != null && WorldMissing(offered).Count == 0)
            {
                await OfferedWorldDialog.Show(this);
                await RunExport(request);
                UnlockAchievement(Achievements.World);
                return;
            }
            await RunExport(request);
        }

        /// <summary>Le livre qu'un export concerne : celui qui contient TOUT ce qui est exporté (null s'il y en a plusieurs, ou aucun).</summary>
        private BinderItem WorldBookOf(ExportRequest request)
        {
            if (request == null || _project == null) return null;
            BinderItem book = null;
            foreach (var id in request.ItemIds)
            {
                var item = _project.FindById(id);
                var its = item == null ? null : item.Kind == ItemKind.Book ? item : item.EnclosingBook();
                if (its == null) continue;
                if (book != null && !ReferenceEquals(book, its)) return null;
                book = its;
            }
            return book;
        }

        /// <summary>Ce qui manque à ce livre pour « S'offrir un monde » (vide : tout y est). Mesure l'orthographe de
        /// tout le livre : à ne demander qu'au moment d'un export, jamais au fil de l'eau.</summary>
        internal List<string> WorldMissing(BinderItem book)
        {
            var check = new Achievements.WorldCheck
            {
                Everything = AppSettings.Profile == Profiles.Everything,
                Pages = BookPageTotal(book),
                ChecklistProblems = BookPublicationTab.ProblemsOf(book, _project, CachedPageCount),
                LexiconEntries = _project.Lexicon.Count + AppSettings.Lexicon.Count,
                SpellingLeft = WorldSpellingLeft(book)
            };
            return Achievements.WorldMissing(_project, book, check);
        }

        /// <summary>Les mots encore inconnus de tout le livre : ni justes, ni appris (projet ou global), ni ignorés.</summary>
        private int WorldSpellingLeft(BinderItem book)
        {
            var engine = Correction.SpellDictionary.Default;
            if (engine == null) return 0; // sans dictionnaire installé, rien ne peut être dit faux
            var checker = new Correction.SpellChecker(engine) { ProjectWords = _project.Lexicon, GlobalWords = AppSettings.Lexicon, Neologisms = AppSettings.NeologismsEnabled };
            var texts = new List<BinderItem>();
            CollectBookTexts(book, texts);
            try { return Correction.UnknownWords.Collect(_project, checker, texts).Count; }
            catch (Exception) { return 0; }
        }

        /// <summary>La fenêtre « Vous avez réussi », puis le succès. En mise au point (« dev-ascension ») : sans export,
        /// et même si le succès est déjà là — il est alors rejoué (retiré, puis redonné) pour voir son toast.</summary>
        private async void OfferWorld(BinderItem book, bool dev)
        {
            await OfferedWorldDialog.Show(this);
            if (dev && AppSettings.Achievements.ContainsKey(Achievements.World)) AppSettings.Achievements.Remove(Achievements.World);
            UnlockAchievement(Achievements.World);
        }

        // ============================================================= les exports rapides

        /// <summary>Export rapide DOCX (Ctrl+Maj+E) : l'écrit ou la fiche
        /// ouverte, en Word, avec sa mise en page — sans passer par la fenêtre.</summary>
        private async void QuickExportDocx()
        {
            if (_current == null || (_current.Kind != ItemKind.Text && _current.Kind != ItemKind.Sheet))
            {
                await MessageDialog.Show(this, "Ouvrez d'abord un écrit (ou une fiche) à exporter.", AppName, MessageButtons.OK, MessageIcon.Information);
                return;
            }
            CommitActive();
            await RunExport(QuickRequest(ExportPresets.WordLayout, _current));
        }

        /// <summary>Export rapide PDF : ce qui est ouvert, sur sa propre mise
        /// en page, avec ses en-têtes et pieds — un PDF de lecture.</summary>
        private async void QuickExportPdf()
        {
            if (_current == null || ScopeOf(_current).Count == 0)
            {
                await MessageDialog.Show(this, "Ouvrez d'abord un écrit, une fiche, un livre ou un dossier à exporter.", AppName, MessageButtons.OK, MessageIcon.Information);
                return;
            }
            CommitActive();
            var request = QuickRequest(null, _current);
            request.Preset = new ExportPreset
            {
                Id = "quick-pdf", Name = "Export rapide PDF", BuiltIn = true,
                Format = ExportFormats.Pdf, Pages = ExportPages.Source, HeadersFooters = true, ChapterHeadings = false, Outline = true
            };
            // Un écrit d'un livre garde les folios du livre.
            if (_current.Kind == ItemKind.Text) request.FolioOffset = ComputeFolioOffset(_current);
            await RunExport(request);
        }

        /// <summary>Export rapide EPUB : le livre ou l'écrit ouvert, avec ses
        /// informations et sa couverture telles qu'elles sont.</summary>
        private async void QuickExportEpub()
        {
            if (_current == null || (_current.Kind != ItemKind.Text && _current.Kind != ItemKind.Book))
            {
                await MessageDialog.Show(this, "Ouvrez d'abord un écrit ou un livre à exporter.", AppName, MessageButtons.OK, MessageIcon.Information);
                return;
            }
            CommitActive();
            var request = QuickRequest(ExportPresets.Epub, _current);
            request.Preset.Cover = true; // la couverture, s'il y en a une
            if (_current.Kind == ItemKind.Text && request.Meta.CoverImageId == null && _current.ImageId != null) request.Meta.CoverImageId = _current.ImageId;
            await RunExport(request);
        }

        private ExportRequest QuickRequest(string presetId, BinderItem item)
        {
            var request = new ExportRequest();
            if (presetId != null) request.Preset = ExportPresets.Find(ExportPresets.BuiltIn(), presetId).Clone();
            request.ItemIds.AddRange(ScopeOf(item));
            var book = item.Kind == ItemKind.Book ? item : null;
            request.Meta = ExportMeta.For(_project, book, item.Title);
            return request;
        }

        // ============================================================= l'écriture

        /// <summary>Demande où enregistrer, écrit le fichier, le dit. Vrai si le fichier est écrit.</summary>
        private async Task<bool> RunExport(ExportRequest request)
        {
            if (_project == null || request == null || request.Preset == null) return false;
            var plan = ExportPlan.Build(_project, request.ItemIds, request.Preset, request.Meta, request.Filter);
            if (plan.IsEmpty)
            {
                await MessageDialog.Show(this, "Rien à exporter : " + (plan.Filtered.Count > 0 ? "le filtre de l'ampleur écarte tout ce qui est sélectionné." : plan.Skipped.Count > 0 ? "le format " + ExportFormats.Short(request.Preset.Format) + " ne porte pas ce qui est sélectionné." : "aucun élément n'est sélectionné."),
                    AppName, MessageButtons.OK, MessageIcon.Information);
                return false;
            }
            var format = ExportFormats.Normalize(request.Preset.Format);
            if (ExportFiles.Applies(request.Preset, plan)) return await RunExportPerFile(request, plan);
            var name = SafeFileName(plan.DefaultName(_project, request.Meta)) + ExportFormats.Extension(format);
            var path = await Ui.PickSaveFile(this, "Exporter — " + request.Preset.Name, ExportFormats.Filter(format), name);
            if (path == null) return false;
            try
            {
                var notes = WriteExport(request, plan, path);
                // Le contrôle de l'EPUB écrit (20/11) : ce que les librairies en ligne refuseraient —
                // par EPUBCheck, l'outil officiel, quand il est installé ; sinon par le contrôle maison.
                var problems = new List<string>();
                var checker = "son contrôle";
                if (format == ExportFormats.Epub) problems = await VerifyEpub(path, notes, delegate(string by) { checker = by; });
                if (format == ExportFormats.Epub) UnlockAchievement(Achievements.GenZ); // « Gen Zer »
                // « Complétionniste », « Minimaliste » (12/09) : le PDF d'un livre entier, à son gabarit.
                if (format == ExportFormats.Pdf && plan.Book != null && request.Preset.Pages == ExportPages.Book && IsWholeBook(plan))
                {
                    if (Achievements.IsBookComplete(_project, plan.Book)) UnlockAchievement(Achievements.Completionist);
                    if (Achievements.IsMinimalist(plan.Book)) UnlockAchievement(Achievements.Minimalist);
                }
                ShowToast(null, "Export terminé", System.IO.Path.GetFileName(path),
                    plan.Summary() + " → " + ExportFormats.Short(format) + (notes.Count > 0 ? " — " + string.Join(" ", notes.ToArray()) : "") + " — " + System.IO.Path.GetDirectoryName(path), 6);
                if (problems.Count > 0)
                {
                    var lines = new List<string>();
                    for (var i = 0; i < problems.Count && i < 8; i++) lines.Add("— " + problems[i]);
                    if (problems.Count > 8) lines.Add("… et " + (problems.Count - 8) + " de plus.");
                    await MessageDialog.Show(this, "L'EPUB est écrit, mais " + checker + " relève " + (problems.Count > 1 ? problems.Count + " défauts" : "un défaut") + " qu'une librairie en ligne pourrait refuser :\n\n" + string.Join("\n", lines.ToArray()),
                        AppName, MessageButtons.OK, MessageIcon.Warning);
                }
                if (request.OpenAfter)
                {
                    try { AppPlatform.RevealInFolder(path); }
                    catch { }
                }
                return true;
            }
            catch (Exception error)
            {
                await MessageDialog.Show(this, "Export impossible :\n" + error.Message, AppName, MessageButtons.OK, MessageIcon.Error);
                return false;
            }
        }

        /// <summary>Contrôle un EPUB écrit : EPUBCheck s'il est installé
        /// (hors du fil de l'interface : Java met une à deux secondes à
        /// partir), le contrôle maison sinon — ou si l'outil ne répond pas.
        /// Rend les défauts ; dit dans les notes ce qui a été vérifié.</summary>
        private async Task<List<string>> VerifyEpub(string path, List<string> notes, Action<string> checkedBy)
        {
            var tool = EpubCheckTool.Find();
            if (tool != null)
            {
                var report = await Task.Run(delegate { return EpubCheckTool.Run(tool, path); });
                if (report.Ran)
                {
                    if (checkedBy != null) checkedBy("EPUBCheck" + (report.Version.Length > 0 ? " " + report.Version : ""));
                    if (report.Errors.Count == 0)
                        notes.Add("Validé par EPUBCheck" + (report.Version.Length > 0 ? " " + report.Version : "") + (report.Warnings.Count > 0 ? " (" + report.Warnings.Count + (report.Warnings.Count > 1 ? " avertissements" : " avertissement") + ")." : "."));
                    return report.Errors;
                }
                notes.Add(report.Failure + " Contrôle maison à la place.");
            }
            var problems = EpubCheck.Verify(path);
            if (problems.Count == 0) notes.Add("Structure de l'EPUB vérifiée.");
            return problems;
        }

        /// <summary>« Un fichier par écrit » (20/11) : demande un dossier, y
        /// écrit un fichier par élément, nommé d'après le modèle du préréglage.</summary>
        private async Task<bool> RunExportPerFile(ExportRequest request, ExportPlan plan)
        {
            var files = ExportFiles.Split(_project, plan, request.Preset, request.Meta, DateTime.Today);
            var folder = await Ui.PickFolder(this, "Exporter — " + request.Preset.Name + " : le dossier des " + files.Count + " fichiers");
            if (folder == null) return false;
            var existing = ExportFiles.Existing(folder, files);
            if (existing.Count > 0)
            {
                var answer = await MessageDialog.Show(this, (existing.Count > 1 ? existing.Count + " fichiers de ce nom existent" : "Un fichier de ce nom existe") + " déjà dans ce dossier (« " + existing[0] + " »" + (existing.Count > 1 ? "…" : "") + ").\n\nLes remplacer ?",
                    AppName, MessageButtons.YesNo, MessageIcon.Warning);
                if (answer != MessageResult.Yes) return false;
            }
            var notes = new List<string>();
            var faults = new List<string>();
            var written = await WriteExportFiles(request, files, folder, notes, faults);
            var format = ExportFormats.Normalize(request.Preset.Format);
            if (written == 0)
            {
                await MessageDialog.Show(this, "Export impossible :\n" + (faults.Count > 0 ? faults[0] : "aucun fichier n'a pu être écrit."), AppName, MessageButtons.OK, MessageIcon.Error);
                return false;
            }
            if (format == ExportFormats.Epub) UnlockAchievement(Achievements.GenZ);
            ShowToast(null, "Export terminé", written + (written > 1 ? " fichiers" : " fichier"),
                plan.Summary() + " → " + written + " " + ExportFormats.Short(format) + (notes.Count > 0 ? " — " + string.Join(" ", notes.ToArray()) : "") + " — " + folder, 6);
            if (faults.Count > 0)
            {
                var lines = new List<string>();
                for (var i = 0; i < faults.Count && i < 8; i++) lines.Add("— " + faults[i]);
                if (faults.Count > 8) lines.Add("… et " + (faults.Count - 8) + " de plus.");
                await MessageDialog.Show(this, written + (written > 1 ? " fichiers sont écrits" : " fichier est écrit") + ", mais :\n\n" + string.Join("\n", lines.ToArray()), AppName, MessageButtons.OK, MessageIcon.Warning);
            }
            if (request.OpenAfter)
            {
                try { AppPlatform.OpenWithShell(folder); }
                catch { }
            }
            return true;
        }

        /// <summary>Écrit ces fichiers dans ce dossier. Rend combien ont été
        /// écrits ; ce qui a manqué rejoint faults, un par ligne.</summary>
        internal async Task<int> WriteExportFiles(ExportRequest request, List<ExportFile> files, string folder, List<string> notes, List<string> faults)
        {
            var format = ExportFormats.Normalize(request.Preset.Format);
            var written = 0;
            foreach (var file in files)
            {
                var path = System.IO.Path.Combine(folder, file.Name);
                try
                {
                    if (file.Plan.IsEmpty) continue;
                    var single = new ExportRequest { Preset = request.Preset, Meta = file.Meta, OpenAfter = false };
                    single.ItemIds.Add(file.Item.Id);
                    // Un écrit d'un livre garde le folio qu'il a dans son livre.
                    if (format == ExportFormats.Pdf && file.Item.Kind == ItemKind.Text && request.Preset.Pages != ExportPages.A4) single.FolioOffset = ComputeFolioOffset(file.Item);
                    foreach (var note in WriteExport(single, file.Plan, path))
                        if (!notes.Contains(note)) notes.Add(note);
                    written++;
                    if (format == ExportFormats.Epub)
                    {
                        var perFile = new List<string>();
                        foreach (var problem in await VerifyEpub(path, perFile, null)) faults.Add(file.Name + " : " + problem);
                        foreach (var note in perFile) if (!notes.Contains(note)) notes.Add(note);
                    }
                }
                catch (Exception error)
                {
                    faults.Add(file.Name + " : " + error.Message);
                }
            }
            return written;
        }

        private static bool IsWholeBook(ExportPlan plan)
        {
            var all = new List<BinderItem>();
            BookFlow.Texts(plan.Book, all);
            return plan.Texts.Count >= all.Count;
        }

        /// <summary>L'aperçu du gestionnaire : le PDF exact dans un fichier
        /// temporaire, ouvert dans le lecteur du système.</summary>
        private bool PreviewExport(ExportRequest request)
        {
            try
            {
                if (request == null || request.Preset == null || request.Preset.Format != ExportFormats.Pdf) return false;
                var plan = ExportPlan.Build(_project, request.ItemIds, request.Preset, request.Meta, request.Filter);
                if (plan.IsEmpty) return false;
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-apercu-" + SafeFileName(plan.DefaultName(_project, request.Meta)) + ".pdf");
                WriteExport(request, plan, path);
                AppPlatform.OpenWithShell(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>L'onglet « Aperçu » du gestionnaire (20/11) : les pages
        /// du PDF, composées comme pour l'export, sans rien écrire. Null si
        /// rien ne peut être mis en pages.</summary>
        private List<Print.PdfPart> ComposeForPreview(ExportRequest request)
        {
            try
            {
                if (_project == null || request == null || request.Preset == null || request.Preset.Format != ExportFormats.Pdf) return null;
                var plan = ExportPlan.Build(_project, request.ItemIds, request.Preset, request.Meta, request.Filter);
                if (plan.IsEmpty) return null;
                foreach (var text in plan.Texts)
                    if (ExtraPages.IsDynamic(text)) RegenerateDynamic(text);
                var segments = ExportFlow.Build(_project, plan, request.Preset, request.Meta ?? new ExportMeta());
                return ComposeParts(request, segments, new List<string>());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Écrit l'export dans ce fichier. Rend ce qu'il y a à dire
        /// de plus (un plan qui n'a pas pu être dessiné…). Lève en cas d'échec.</summary>
        internal List<string> WriteExport(ExportRequest request, ExportPlan plan, string path)
        {
            var notes = new List<string>();
            var preset = request.Preset;
            var meta = request.Meta ?? new ExportMeta();
            var format = ExportFormats.Normalize(preset.Format);
            if (format == ExportFormats.Epub)
            {
                WriteEpubExport(plan, preset, meta, path);
                return notes;
            }
            // Pages dynamiques (table des matières, index, notes, glossaire) à jour avant de composer.
            foreach (var text in plan.Texts)
                if (ExtraPages.IsDynamic(text)) RegenerateDynamic(text);
            var segments = ExportFlow.Build(_project, plan, preset, meta);
            if (format == ExportFormats.Pdf) WritePdfExport(request, plan, segments, path, notes);
            else WriteDocumentExport(plan, preset, meta, segments, path);
            return notes;
        }

        private void WriteEpubExport(ExportPlan plan, ExportPreset preset, ExportMeta meta, string path)
        {
            if (plan.Texts.Count == 0) throw new InvalidOperationException("un EPUB ne porte que des écrits, et aucun n'est sélectionné.");
            EpubOptions options;
            var epub = ExportFlow.EpubFor(_project, plan, preset, meta, out options);
            Epub.Write(path, _project, epub, options);
        }

        /// <summary>Word, OpenDocument, RTF, Markdown, texte brut : les flux
        /// mis bout à bout — dans Word, une section par mise en page.</summary>
        private void WriteDocumentExport(ExportPlan plan, ExportPreset preset, ExportMeta meta, List<ExportSegment> segments, string path)
        {
            var format = ExportFormats.Normalize(preset.Format);
            var merged = new TextDocument();
            var extras = new DocxExtras { Title = (meta.Title ?? "").Trim(), Author = (meta.Author ?? "").Trim(), Comments = preset.Comments };
            if (extras.Title.Length == 0) extras.Title = plan.DefaultName(_project, meta);
            StyleSheet styles = null;
            PageSetup last = null;
            var flows = 0;
            foreach (var segment in segments)
            {
                if (segment.IsImage || segment.Document == null) continue;
                flows++;
                var start = merged.Paragraphs.Count;
                // Une section Word par MISE EN PAGE : deux flux qui se suivent sur la même page la partagent.
                if (last == null || !ExportFlow.SamePage(last, segment.Setup) || last.FooterPageNumbers != segment.Setup.FooterPageNumbers)
                {
                    extras.Sections.Add(new DocxSection { StartParagraph = start, Setup = segment.Setup });
                    last = segment.Setup;
                }
                for (var i = 0; i < segment.Document.Paragraphs.Count; i++)
                {
                    var paragraph = segment.Document.Paragraphs[i];
                    // Un flux qui en suit un autre ouvre une nouvelle page (hors Word, c'est le seul signe du changement).
                    if (i == 0 && start > 0 && !paragraph.PageBreakBefore)
                    {
                        var shell = PivotEdit.CloneParagraphShell(paragraph);
                        shell.PageBreakBefore = true;
                        shell.Runs.AddRange(paragraph.Runs);
                        paragraph = shell;
                    }
                    merged.Paragraphs.Add(paragraph);
                }
                merged.Footnotes.AddRange(segment.Document.Footnotes);
                merged.Annotations.AddRange(segment.Document.Annotations);
                if (flows == 1) merged.LineSpacing = segment.Document.LineSpacing;
                else if (Math.Abs(merged.LineSpacing - segment.Document.LineSpacing) > 0.001) merged.LineSpacing = 1;
                if (styles == null) styles = segment.Styles.Clone();
                else
                    foreach (var style in segment.Styles.Styles)
                    {
                        var known = false;
                        foreach (var have in styles.Styles) if (have.Id == style.Id) { known = true; break; }
                        if (!known) styles.Styles.Add(style.Clone());
                    }
            }
            if (styles == null || merged.Paragraphs.Count == 0) throw new InvalidOperationException("rien de ce qui est sélectionné n'entre dans ce format.");

            var commentsAuthor = Defaults.Or(_project.Author, Defaults.Author); // Préférences › Auteur (22/09)
            if (format == ExportFormats.Docx)
                Docx.Export(merged, styles, path, last, commentsAuthor, _project, extras); // annotations → commentaires Word (b49), images ancrées (0.50.0)
            else if (format == ExportFormats.Odt)
                Odt.Export(Compiler.FlattenLists(merged), styles, path);
            else if (format == ExportFormats.Rtf)
                Rtf.Export(Compiler.FlattenRules(merged), styles, path, _project);
            else if (format == ExportFormats.Markdown)
                File.WriteAllText(path, MarkdownExchange.Export(merged), new System.Text.UTF8Encoding(false));
            else
                File.WriteAllText(path, Compiler.FlattenLists(merged).ToPlainText(), new System.Text.UTF8Encoding(false));
        }

        /// <summary>Le PDF : chaque flux composé sur SA mise en page (les
        /// folios courent d'une partie à l'autre), chaque plan ou carte
        /// mentale en page-image à sa taille ; liens et signets si le
        /// préréglage en veut.</summary>
        private void WritePdfExport(ExportRequest request, ExportPlan plan, List<ExportSegment> segments, string path, List<string> notes)
        {
            var preset = request.Preset;
            var meta = request.Meta ?? new ExportMeta();
            var parts = ComposeParts(request, segments, notes);
            if (parts.Count == 0) throw new InvalidOperationException("rien n'a pu être mis en pages.");

            var bleed = preset.BleedMm;
            if (preset.BookBleed && plan.Book != null && plan.Book.Book != null) bleed = plan.Book.Book.BleedMm;
            var title = (meta.Title ?? "").Trim();
            // En CMJN, le profil FOGRA39 livré avec Marabook est incorporé (21/11 ; à défaut, celui de la machine).
            var icc = preset.Cmyk ? Print.IccProfiles.FindFogra39() : null;
            var pdfx = preset.PdfX && preset.Cmyk;
            var bytes = Print.PdfWriter.Build(parts, new Print.PdfExportOptions
            {
                BleedMm = Math.Max(0, Math.Min(20, bleed)), CropMarks = preset.CropMarks, BleedGuides = preset.BleedGuides, Cmyk = preset.Cmyk, Booklet = preset.Booklet,
                Title = title.Length > 0 ? title : plan.DefaultName(_project, meta), Author = (meta.Author ?? "").Trim(),
                PdfX = pdfx, IccProfile = icc == null ? null : icc.Bytes,
                // Le skin n'habille QUE le « PDF Encyclopédique » (21/11, Rémi) : jamais un BAT, jamais un texte.
                Skin = preset.Id == ExportPresets.EncyclopediaPdf && !preset.Cmyk ? Skin.PdfSkin() : null
            });
            File.WriteAllBytes(path, bytes);
            if (pdfx)
            {
                // Le contrôle du PDF/X écrit : ce qu'un imprimeur vérifie d'abord.
                // Depuis le 21/11, le contrôle LIT le fichier (objets, ressources, flux des pages) et dit ce qu'il a regardé.
                var report = Print.PdfXInspector.Inspect(bytes);
                if (report.Conforms) notes.Add("PDF/X-1a contrôlé par Marabook : " + report.Summary() + (icc != null ? " ; profil « " + icc.Description + " » incorporé." : " ; condition FOGRA39 déclarée par son nom."));
                else notes.Add("PDF/X à revoir : " + string.Join(" ", report.Problems.ToArray()));
                if (report.Notes.Count > 0) notes.Add("Contrôle PDF/X, à savoir : " + string.Join(" ", report.Notes.ToArray()));
            }
        }

        /// <summary>Les parties du PDF, composées : chaque flux sur sa mise
        /// en page, chaque plan ou carte mentale en page-image, avec leurs
        /// liens et leurs signets. Sert l'export comme l'aperçu.</summary>
        private List<Print.PdfPart> ComposeParts(ExportRequest request, List<ExportSegment> segments, List<string> notes)
        {
            var preset = request.Preset;
            var fonts = new AvaloniaFontEngine();
            var parts = new List<Print.PdfPart>();
            var folio = request.FolioOffset;
            var failed = new List<string>();
            foreach (var segment in segments)
            {
                Print.PdfPart part;
                if (segment.IsImage)
                {
                    part = ExportImages.Render(segment.Item, _project);
                    if (part == null) { failed.Add("« " + segment.Item.Title + " »"); continue; }
                }
                else
                {
                    var engine = new Print.CompositionEngine(segment.Document, segment.Styles, segment.Setup, _project, true, fonts);
                    engine.FolioOffset = folio;
                    engine.ComposeAll();
                    folio += engine.Current.Pages.Count;
                    part = new Print.PdfPart { Composition = engine.Current };
                    foreach (var link in segment.Links)
                        part.Links.Add(new Print.PdfLink { Paragraph = link.Paragraph, Start = link.Start, Length = link.Length, Target = link.Target });
                }
                foreach (var mark in segment.Marks)
                    part.Marks.Add(new Print.PdfMark { Paragraph = mark.Paragraph, Name = preset.Links ? mark.Name : null, Title = preset.Outline ? mark.Title : null, Level = mark.Level });
                parts.Add(part);
            }
            if (failed.Count > 0) notes.Add("Non dessiné : " + string.Join(", ", failed.ToArray()) + ".");
            // Un seul signet n'aide personne : le volet reste fermé.
            var marks = 0;
            foreach (var part in parts) foreach (var mark in part.Marks) if (mark.Title != null) marks++;
            if (marks < 2) foreach (var part in parts) foreach (var mark in part.Marks) mark.Title = null;
            return parts;
        }

        // ============================================================= sonde

        /// <summary>Sonde : un export complet, sans dialogue — ce préréglage
        /// (de la maison ou réglé), ces éléments, dans ce fichier.</summary>
        internal List<Print.PdfPart> ComposeForProbe(ExportRequest request) { return ComposeForPreview(request); }
        internal ExportPlan ExportForProbe(ExportPreset preset, IEnumerable<string> ids, string path, ExportMeta meta = null, ExportFilter filter = null)
        {
            var request = new ExportRequest { Preset = preset, Meta = meta ?? new ExportMeta(), Filter = filter };
            request.ItemIds.AddRange(ids);
            var plan = ExportPlan.Build(_project, request.ItemIds, preset, request.Meta, filter);
            WriteExport(request, plan, path);
            return plan;
        }
        internal List<string> ScopeForProbe(BinderItem item) { return ScopeOf(item); }
        internal ExportRequest QuickRequestForProbe(string presetId, BinderItem item) { return QuickRequest(presetId, item); }
    }

    /// <summary>Les éléments qui partent en IMAGE dans un PDF (18/11) : un
    /// plan (ses colonnes, ses briques et leurs intensités, ses notes) dessiné
    /// sur fond blanc à sa taille naturelle, une carte mentale rendue par son
    /// module. La page du PDF prend la taille de l'image.</summary>
    internal static class ExportImages
    {
        private const double Scale = 2;           // 192 ppp : net à l'écran comme sur papier
        private const double MaxSidePx = 4200;    // au-delà, l'image est réduite (la page garde sa taille)

        public static Print.PdfPart Render(BinderItem item, Project project)
        {
            try
            {
                Control view = null;
                if (item.Kind == ItemKind.Map) return MapPart(item, project);
                if (item.Kind == ItemKind.Plan) view = PlanSheet(item, project);
                else if (item.Kind == ItemKind.MindMap)
                {
                    var provider = MindMapModules.Provider;
                    if (provider == null || item.MapBytes == null) return null;
                    var map = provider.Thumbnail(item.MapBytes, 1320, 880);
                    if (map == null) return null;
                    var frame = new StackPanel { Background = Brushes.White };
                    frame.Children.Add(new TextBlock { Text = item.Title, Foreground = Ink, FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new Thickness(40, 32, 40, 12) });
                    frame.Children.Add(new Border { Child = map, Width = 1320, Height = 880, Margin = new Thickness(40, 0, 40, 40), ClipToBounds = true });
                    view = frame;
                }
                if (view == null) return null;
                return ToPart(view);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Une carte du monde (20/11) : la carte entière, à plat,
        /// avec ses zones et ses repères, sur une page à ses proportions —
        /// son grand côté fait celui d'un A4.</summary>
        private static Print.PdfPart MapPart(BinderItem item, Project project)
        {
            if (item.Map == null || !item.Map.HasImage) return null;
            var view = new MapView();
            try
            {
                view.Load(item, project);
                using (var bitmap = view.Capture(true, (int)MaxSidePx))
                {
                    if (bitmap == null) return null;
                    int width, height;
                    var rgb = AvaloniaFontEngine.Rgb24OverWhite(bitmap, out width, out height);
                    if (rgb == null) return null;
                    var mm = 297.0 / Math.Max(width, height);
                    return new Print.PdfPart { ImageRgb = rgb, ImageWidth = width, ImageHeight = height, PageWidthMm = width * mm, PageHeightMm = height * mm };
                }
            }
            finally
            {
                view.Clear();
            }
        }

        private static Print.PdfPart ToPart(Control view)
        {
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = view.DesiredSize;
            if (size.Width < 1 || size.Height < 1) return null;
            view.Arrange(new Rect(size));
            var scale = Math.Min(Scale, MaxSidePx / Math.Max(size.Width, size.Height));
            var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * scale)), Math.Max(1, (int)Math.Ceiling(size.Height * scale)));
            using (var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale)))
            {
                bitmap.Render(view);
                int width, height;
                var rgb = AvaloniaFontEngine.Rgb24OverWhite(bitmap, out width, out height);
                if (rgb == null) return null;
                return new Print.PdfPart
                {
                    ImageRgb = rgb, ImageWidth = width, ImageHeight = height,
                    PageWidthMm = size.Width / PageSetup.PxPerMm, PageHeightMm = size.Height / PageSetup.PxPerMm
                };
            }
        }

        private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1E, 0x21, 0x28));
        private static readonly IBrush Soft = new SolidColorBrush(Color.FromRgb(0x5A, 0x61, 0x72));
        private static readonly IBrush Line = new SolidColorBrush(Color.FromRgb(0xD3, 0xD7, 0xE3));
        private static readonly IBrush ColumnFill = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF8));
        private static readonly IBrush NoteFill = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xA8));
        private static readonly IBrush NoteInk = new SolidColorBrush(Color.FromRgb(0x5C, 0x4A, 0x10));
        private static readonly Color Accent = Color.FromRgb(0x5B, 0x67, 0xD8);

        /// <summary>Un plan sur papier : son titre, ce à quoi il est relié,
        /// ses colonnes côte à côte — chaque brique avec sa couleur et son
        /// intensité, chaque note en jaune.</summary>
        private static Control PlanSheet(BinderItem item, Project project)
        {
            var plan = item.Plan ?? new PlanInfo();
            var sheet = new StackPanel { Background = Brushes.White };
            sheet.Children.Add(new TextBlock { Text = item.Title, Foreground = Ink, FontSize = 24, FontWeight = FontWeight.SemiBold, Margin = new Thickness(40, 34, 40, 0) });
            var linked = plan.LinkedItemId == null || project == null ? null : project.FindById(plan.LinkedItemId);
            sheet.Children.Add(new TextBlock
            {
                Text = linked != null ? "Plan de « " + linked.Title + " »" : "Plan",
                Foreground = Soft, FontSize = 13, Margin = new Thickness(40, 4, 40, 18)
            });
            var columns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(40, 0, 28, 40) };
            foreach (var column in plan.Columns)
            {
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(column.Title) ? "Colonne" : column.Title, Foreground = Ink, FontSize = 14.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                var text = column.LinkedTextId == null || project == null ? null : project.FindById(column.LinkedTextId);
                if (text != null) stack.Children.Add(new TextBlock { Text = text.Title, Foreground = Soft, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                stack.Children.Add(new Border { Height = 8 });
                foreach (var entry in column.Entries) stack.Children.Add(PlanBrick(entry));
                columns.Children.Add(new Border
                {
                    Child = stack, Width = 250, Padding = new Thickness(12, 11, 12, 8), Margin = new Thickness(0, 0, 12, 0), CornerRadius = new CornerRadius(10),
                    Background = ColumnFill, BorderBrush = Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top
                });
            }
            if (plan.Columns.Count == 0) columns.Children.Add(new TextBlock { Text = "Ce plan est vide.", Foreground = Soft, FontSize = 13 });
            sheet.Children.Add(columns);
            return sheet;
        }

        private static Control PlanBrick(PlanEntry entry)
        {
            if (entry.IsNote)
                return new Border
                {
                    Child = new TextBlock { Text = entry.Text ?? "", Foreground = NoteInk, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                    Background = NoteFill, CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 7, 9, 8), Margin = new Thickness(0, 0, 0, 7)
                };
            Color color;
            if (entry.Color == null || !Color.TryParse(entry.Color, out color)) color = Accent;
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = entry.Text ?? "", Foreground = Ink, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var level = PlanIntensity.Clamp(entry.Intensity);
            for (var i = PlanIntensity.Min; i <= PlanIntensity.Max; i++)
                dots.Children.Add(new Border
                {
                    Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
                    Background = i <= level ? new SolidColorBrush(color) : (IBrush)Line
                });
            dots.Children.Add(new TextBlock { Text = PlanIntensity.Label(level), Foreground = Soft, FontSize = 10.5, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(dots);
            return new Border
            {
                Child = body, Background = Brushes.White, BorderBrush = entry.Color != null ? new SolidColorBrush(color) : Line,
                BorderThickness = new Thickness(entry.Color != null ? 4 : 1, 1, 1, 1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 7, 9, 8), Margin = new Thickness(0, 0, 0, 7)
            };
        }
    }
}

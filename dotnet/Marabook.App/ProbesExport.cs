using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde de la refonte de l'export (2.0.0, 18/11/2026) : le
    /// sous-menu Exporter et ses raccourcis ; le GESTIONNAIRE D'EXPORT — ses
    /// onglets en colonne, ses préréglages, l'ampleur, les informations du
    /// livre pré-remplies, l'enregistrement d'un préréglage — ; puis de VRAIS
    /// fichiers écrits par chaque préréglage de la maison (PDF aux formats
    /// mêlés avec pages-images, liens et signets, BAT en CMJN, Word à
    /// sections, EPUB) et par les autres formats. Avec MARABOOK_PROBE_SHOTS,
    /// elle photographie la fenêtre et garde les fichiers.</summary>
    public static partial class Probes
    {
        private static string Latin1(string path)
        {
            return Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(path));
        }

        private static int Occurrences(string text, string fragment)
        {
            var count = 0;
            for (var at = text.IndexOf(fragment, StringComparison.Ordinal); at >= 0; at = text.IndexOf(fragment, at + fragment.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        private static string ZipText(string path, string name)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var entry = zip.GetEntry(name);
                if (entry == null) return null;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        private static bool InOutOfBook(BinderItem item)
        {
            for (var at = item; at != null; at = at.Parent) if (at.IsOutOfBook) return true;
            return false;
        }

        private static ExportPreset HousePreset(string id)
        {
            return ExportPresets.Find(ExportPresets.BuiltIn(), id).Clone();
        }

        private static async Task ProbeExport(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- l'export (2.0.0, 18/11)");

            // ================================================== le sous-menu Exporter
            MenuItem export = null;
            foreach (var item in shell.GetLogicalDescendants().OfType<MenuItem>())
                if ((item.Header as string) == "Exporter") { export = item; break; }
            Check(export != null, "Fichier › Exporter existe");
            if (export != null)
            {
                var layout = new List<string>();
                foreach (var entry in export.Items) layout.Add(entry is Separator ? "—" : ((entry as MenuItem) == null ? "?" : (string)((MenuItem)entry).Header));
                var joined = string.Join(" | ", layout.ToArray());
                Check(joined == "Gestionnaire d'export… | — | Export rapide DOCX | L'écrit sélectionné… | — | Export rapide PDF | Export rapide EPUB | — | Encyclopédie des fiches…",
                    "le sous-menu : le gestionnaire en tête, puis les catégories séparées d'un filet (" + joined + ")");
                Check(!joined.Contains("Compiler"), "« Compiler les écrits » a disparu");
            }
            Check(AppSettings.Definition("export-manager").DefaultGesture == "Ctrl+E", "Ctrl+E ouvre le gestionnaire d'export");
            Check(AppSettings.Definition("export-quick-docx").DefaultGesture == "Ctrl+Shift+E", "Ctrl+Maj+E fait l'export rapide DOCX");
            Check(AppSettings.Definition("export-item").DefaultGesture == null && AppSettings.Definition("compile") == null, "« L'écrit sélectionné » n'a plus de raccourci d'office, « Compiler » plus d'action");

            // ================================================== ce que le projet d'exemple offre
            BinderItem book = null, plan = null, map = null;
            var sheets = new List<BinderItem>();
            var texts = new List<BinderItem>();
            foreach (var item in ExportPlan.ReadingOrder(project))
            {
                var home = item.RootCategory();
                if (home != null && home.CategoryKey == Project.KeyTrash) continue;
                if (item.Kind == ItemKind.Book && book == null) book = item;
                if (item.Kind == ItemKind.Plan && plan == null) plan = item;
                if (item.Kind == ItemKind.MindMap && map == null) map = item;
                if (item.Kind == ItemKind.Sheet) sheets.Add(item);
                if (item.Kind == ItemKind.Text) texts.Add(item);
            }
            Check(book != null && sheets.Count > 0 && texts.Count > 0, "le projet d'exemple a un livre, des fiches et des écrits (" + texts.Count + " écrits, " + sheets.Count + " fiches, plan " + (plan != null) + ", carte mentale " + (map != null) + ")");
            if (book == null) return;
            var bookScope = shell.ScopeForProbe(book);
            Check(bookScope.Count > 0, "l'ampleur d'un livre : ses écrits (" + bookScope.Count + ")");

            // ================================================== la fenêtre
            ExportPresetStore.Reset();
            var customBefore = ExportPresetStore.Custom.Count;
            var task = ExportManagerDialog.Ask(shell, project, bookScope, ExportPresets.Bat, delegate(ExportRequest r) { return true; }, shell.ComposeForProbe);
            await Settle();
            var dialog = OpenWindow<ExportManagerDialog>();
            Check(dialog != null, "« Publier » : le gestionnaire d'export s'ouvre");
            if (dialog != null)
            {
                var tabs = dialog.TabsForProbe;
                var headers = new List<string>();
                foreach (var tab in tabs.Items.OfType<TabItem>()) headers.Add(tab.Header as string);
                Check(string.Join(" | ", headers.ToArray()).StartsWith("Préréglage | Ampleur | Options | Structure | Impression | Livre | Aperçu | Vérifications"),
                    "huit onglets : Préréglage, Ampleur, Options, Structure, Impression, Livre, Aperçu, Vérifications (" + string.Join(" | ", headers.ToArray()) + ")");
                Check(tabs.TabStripPlacement == Dock.Left && tabs.Classes.Contains("column"), "les onglets tiennent en colonne, à gauche");
                Check(dialog.PresetCardsForProbe == 10 + customBefore, "les dix préréglages de la maison sont proposés");

                // L'onglet Structure (20/11) : les réglages de structure y ont déménagé, l'enchaînement est un choix exclusif.
                var structureTab = dialog.ControlForProbe<TabItem>("export-tab-structure");
                var inStructure = structureTab != null && structureTab.GetLogicalDescendants().OfType<CheckBox>().Any(delegate(CheckBox box) { return (box.Tag as string) == "export-title-page"; });
                var optionsTab = dialog.ControlForProbe<TabItem>("export-tab-options");
                var inOptions = optionsTab != null && optionsTab.GetLogicalDescendants().OfType<CheckBox>().Any(delegate(CheckBox box) { return (box.Tag as string) == "export-title-page" || (box.Tag as string) == "export-headings"; });
                Check(inStructure && !inOptions, "onglet Structure : la page de titre et les titres de chapitres y sont, plus dans Options");
                var seqDefault = dialog.ControlForProbe<RadioButton>("export-seq-default");
                var seqRecto = dialog.ControlForProbe<RadioButton>("export-recto");
                var seqNoBlank = dialog.ControlForProbe<RadioButton>("export-no-blank");
                Check(seqDefault != null && seqRecto != null && seqNoBlank != null && seqRecto.IsChecked == true && seqNoBlank.IsChecked != true && seqDefault.IsChecked != true,
                    "enchaînement des pages : trois choix exclusifs — le BAT est en belle page");
                Check(dialog.ControlForProbe<CheckBox>("export-split-types") != null && dialog.ControlForProbe<CheckBox>("export-split-books") != null && dialog.ControlForProbe<CheckBox>("export-split-folders") != null
                    && dialog.ControlForProbe<CheckBox>("export-split-subfolders") != null && dialog.ControlForProbe<CheckBox>("export-split-own-page") != null, "rendu par niveau : genre, livre, dossier, sous-dossier, et la page à eux");
                seqNoBlank.IsChecked = true;
                await Settle();
                Check(dialog.PresetForProbe.DropBlankPages && !dialog.PresetForProbe.RectoStarts && seqRecto.IsChecked != true, "« Retirer les pleines pages blanches » décoche la belle page");
                seqRecto.IsChecked = true;
                await Settle();
                Check(dialog.PresetForProbe.RectoStarts && !dialog.PresetForProbe.DropBlankPages && !dialog.FooterForProbe.Contains("modifié"), "…et revenir à la belle page rend le BAT tel quel");

                // Les infobulles (20/11) : une option grisée dit pourquoi ; une case n'est pas plus large que son libellé.
                var comments = dialog.ControlForProbe<CheckBox>("export-comments");
                Check(!comments.IsEnabled && ToolTip.GetShowOnDisabled(comments) && (dialog.TipForProbe("export-comments") ?? "").Contains("propres à Word"), "option grisée : son infobulle dit pourquoi (« " + dialog.TipForProbe("export-comments") + " »)");
                Check((dialog.TipForProbe("export-drop-caps") ?? "").StartsWith("Ne vaut que pour un EPUB") && !dialog.ControlForProbe<CheckBox>("export-drop-caps").IsEnabled, "la lettrine, hors EPUB : grisée, et elle le dit");
                var decor = dialog.ControlForProbe<CheckBox>("export-decor");
                Check(decor.IsEnabled && (dialog.TipForProbe("export-decor") ?? "").StartsWith("Sinon, un simple folio"), "option modifiable : son infobulle dit à quoi elle sert");
                var grey = 0;
                var mute = new List<string>();
                foreach (var control in dialog.GetLogicalDescendants().OfType<Control>())
                {
                    var tag = control.Tag as string;
                    if (tag == null || !tag.StartsWith("export-") || control.IsEnabled) continue;
                    if (!(control is CheckBox) && !(control is RadioButton) && !(control is ComboBox) && !(control is TextBox) && !(control is Button)) continue;
                    grey++;
                    if (string.IsNullOrEmpty(ToolTip.GetTip(control) as string) || !ToolTip.GetShowOnDisabled(control)) mute.Add(tag);
                }
                Check(grey > 0 && mute.Count == 0, "tout réglage grisé porte son explication (" + grey + " grisés" + (mute.Count > 0 ? ", muets : " + string.Join(", ", mute.ToArray()) : "") + ")");
                Check(decor.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left && seqRecto.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Left, "une case ne s'étire pas sur la ligne : son infobulle ne paraît que sur elle");
                Check(dialog.BaseForProbe.Id == ExportPresets.Bat, "ouvert par « Publier » : le préréglage « BAT Imprimeur » est choisi");
                Check(dialog.PlanForProbe.Book == book && dialog.PlanForProbe.Texts.Count == bookScope.Count, "…et le livre est déjà coché dans l'ampleur");
                Check(dialog.MetaForProbe.Title == BookInfo.TitleOf(book), "onglet Livre : le titre est pré-rempli depuis le livre (« " + dialog.MetaForProbe.Title + " »)");
                Check(dialog.MetaForProbe.Author.Length > 0 || Defaults.Author.Length == 0, "…l'auteur·ice aussi");
                Check((book.ImageId == null) == (dialog.MetaForProbe.CoverImageId == null), "…et la couverture du livre, s'il en a une");
                Check(dialog.WriteBackShownForProbe, "avec un livre : la case « Reporter ces informations dans le livre »");
                Check(dialog.PreviewShownForProbe, "un PDF : le bouton Aperçu est là");
                var marks = dialog.ControlForProbe<CheckBox>("export-marks");
                var profile = dialog.ControlForProbe<ComboBox>("export-profile");
                Check(marks != null && marks.IsChecked == true && profile != null && profile.SelectedIndex == 1, "onglet Impression : traits de coupe et CMJN, comme le bouton Publier d'avant");
                Check(dialog.FooterForProbe.StartsWith("BAT Imprimeur |") && dialog.FooterForProbe.Contains("→ PDF"), "le pied résume : préréglage, ampleur, format (" + dialog.FooterForProbe + ")");
                Shot(dialog, "export-1-preset.png");

                // Une option changée : le préréglage est dit modifié.
                marks.IsChecked = false;
                await Settle();
                Check(dialog.FooterForProbe.StartsWith("BAT Imprimeur — modifié"), "une option changée : « modifié » au pied");
                Check(!dialog.PresetForProbe.CropMarks && dialog.BaseForProbe.CropMarks, "…la copie de travail change, pas le préréglage de la maison");

                // Enregistrer la configuration : un préréglage personnalisé, sans l'ampleur.
                dialog.SavePresetForProbe("Sonde — BAT sans traits");
                await Settle();
                Check(ExportPresetStore.Custom.Count == customBefore + 1 && dialog.PresetCardsForProbe == 11 + customBefore, "enregistré : un onzième préréglage, dans « Mes préréglages »");
                var saved = ExportPresetStore.Custom[ExportPresetStore.Custom.Count - 1];
                Check(dialog.BaseForProbe.Id == saved.Id && !saved.CropMarks && saved.Cmyk && !saved.BuiltIn, "…choisi d'office, avec les options réglées");
                Check(!File.ReadAllText(ExportPresetStore.FilePath()).Contains(book.Id), "…et l'ampleur n'y est pas gardée");
                Check(!dialog.FooterForProbe.Contains("modifié"), "…il n'est plus « modifié »");

                // Le manuscrit papier : ses options se branchent toutes seules.
                dialog.ChooseForProbe(ExportPresets.Manuscript);
                await Settle();
                var cap = dialog.ControlForProbe<CheckBox>("export-cap-font");
                var spacing = dialog.ControlForProbe<TextBox>("export-line-spacing");
                var pages = dialog.ControlForProbe<ComboBox>("export-pages");
                Check(cap.IsChecked == true && spacing.Text == "1,25" && pages.SelectedIndex == 0 && dialog.ControlForProbe<TextBox>("export-max-font").Text == "12",
                    "PDF Manuscrit papier : A4, 12 points au plus, interligne 1,25 — branchés d'office");
                Check(dialog.PlanForProbe.Texts.Count == bookScope.Count, "changer de préréglage ne touche pas l'ampleur");
                Check(dialog.ControlForProbe<RadioButton>("export-no-blank").IsChecked == true && dialog.PresetForProbe.DropBlankPages, "PDF Manuscrit papier : « Retirer les pleines pages blanches » est cochée d'office");
                Shot(dialog, "export-2-manuscrit.png");
                tabs.SelectedIndex = 2;
                await Settle();
                Shot(dialog, "export-3-options.png");
                tabs.SelectedIndex = 3;
                await Settle();
                Shot(dialog, "export-3b-structure.png");

                // L'aperçu paginé (20/11) : les pages du PDF dans la fenêtre, et leur nombre au pied.
                tabs.SelectedIndex = 6;
                await Settle();
                dialog.PreviewForProbe();
                await Settle();
                var previewPages = dialog.PreviewPagesForProbe;
                Check(previewPages > 0 && dialog.PreviewLabelForProbe.StartsWith("Page 1 sur " + previewPages + "  ·  A4"), "onglet Aperçu : le manuscrit mis en pages (" + dialog.PreviewLabelForProbe + ")");
                Check(dialog.FooterForProbe.Contains("  ·  " + previewPages + " page"), "…et le nombre de pages au pied (" + dialog.FooterForProbe + ")");
                Check(!dialog.ControlForProbe<Button>("export-page-previous").IsEnabled && (dialog.TipForProbe("export-page-previous") ?? "") == "C'est la première page.", "à la première page, « précédente » se grise et dit pourquoi");
                Shot(dialog, "export-3c-apercu.png");
                if (previewPages > 1)
                {
                    dialog.ShowPageForProbe(1);
                    await Settle();
                    Check(dialog.PreviewLabelForProbe.StartsWith("Page 2 sur "), "la page suivante se montre");
                    Shot(dialog, "export-3d-apercu-page-2.png");
                }
                dialog.ControlForProbe<CheckBox>("export-title-page").IsChecked = false;
                await Settle();
                dialog.PreviewForProbe();
                Check(dialog.PreviewPagesForProbe == previewPages - 1, "sans page de titre : une page de moins dans l'aperçu (" + dialog.PreviewPagesForProbe + ")");

                // Le filtre de l'ampleur (20/11).
                var statusFilter = dialog.ControlForProbe<ComboBox>("export-filter-status");
                var before = dialog.PlanForProbe.Texts.Count;
                var doneTexts = 0;
                foreach (var text in dialog.PlanForProbe.Texts) if (text.Status == "done") doneTexts++;
                statusFilter.SelectedIndex = TextStatus.Keys.Length; // « Terminé »
                await Settle();
                Check(dialog.PlanForProbe.Texts.Count == doneTexts && dialog.PlanForProbe.Filtered.Count == before - doneTexts && dialog.RequestForProbe().Filter != null,
                    "filtre « Seulement Terminé » : " + doneTexts + " écrits sur " + before + " restent, et la demande porte le filtre");
                statusFilter.SelectedIndex = 0;
                await Settle();
                Check(dialog.PlanForProbe.Texts.Count == before && dialog.RequestForProbe().Filter == null, "filtre retiré : tout revient");

                // Un EPUB : la page de titre et la numérotation s'y règlent désormais ; l'aperçu dit qu'il n'a pas de pages.
                dialog.ChooseForProbe(ExportPresets.EpubKindle);
                await Settle();
                Check(dialog.ControlForProbe<CheckBox>("export-title-page").IsEnabled && dialog.ControlForProbe<CheckBox>("export-drop-caps").IsEnabled && dialog.ControlForProbe<CheckBox>("export-kindle").IsChecked == true,
                    "EPUB pour Kindle : page de titre et lettrine se règlent, le profil Kindle est coché");
                Check(!dialog.ControlForProbe<RadioButton>("export-no-blank").IsEnabled && (dialog.TipForProbe("export-no-blank") ?? "").StartsWith("Un EPUB n'a pas de pages"), "…et ce qui touche aux pages se grise en le disant");
                dialog.PreviewForProbe();
                Check(dialog.PreviewPagesForProbe == 0 && dialog.PreviewLabelForProbe.Contains("Un EPUB n'en a pas"), "aperçu d'un EPUB : pas de pages, et il le dit");
                dialog.ChooseForProbe(ExportPresets.Manuscript);
                await Settle();
                tabs.SelectedIndex = 2;
                await Settle();
                var formats = new List<string>();
                foreach (var entry in dialog.ControlForProbe<ComboBox>("export-format").Items) formats.Add(entry as string);
                Check(formats.Count >= 6 && formats[0].StartsWith("Document Word") && formats[1].StartsWith("PDF") && formats[2].StartsWith("EPUB") && (Rtf.Exporter != null || !formats.Exists(delegate(string f) { return f.Contains(".rtf"); })),
                    "les formats : Word, PDF, EPUB, puis OpenDocument, Markdown, texte brut — le RTF seulement s'il a un convertisseur (" + formats.Count + ")");

                // L'ampleur : tout, rien, une fiche.
                dialog.CheckAllForProbe(false);
                await Settle();
                Check(dialog.PlanForProbe.IsEmpty && !dialog.ControlForProbe<Button>("export-ok").IsEnabled, "rien de coché : « Exporter » se grise");
                dialog.CheckKindForProbe(ItemKind.Sheet);
                await Settle();
                Check(dialog.PlanForProbe.Sheets.Count == sheets.Count && dialog.PlanForProbe.Texts.Count == 0, "« Toutes les fiches » : " + sheets.Count + " fiches, aucun écrit");
                Check(!dialog.WriteBackShownForProbe, "sans livre dans l'ampleur : plus de case de report");
                Check(dialog.MetaForProbe.CoverImageId == null && dialog.MetaForProbe.Title != BookInfo.TitleOf(book) || project.Name == BookInfo.TitleOf(book), "…et l'onglet Livre se vide des informations du livre (titre « " + dialog.MetaForProbe.Title + " », sans couverture)");
                dialog.ChooseForProbe(ExportPresets.Epub);
                await Settle();
                Check(dialog.PlanForProbe.IsEmpty && dialog.PlanForProbe.Skipped.Count == sheets.Count && (dialog.ControlForProbe<TabItem>("export-tab-warnings").Header as string).StartsWith("Vérifications ("),
                    "des fiches dans un EPUB : écartées, et l'onglet Vérifications le compte");
                Check(!dialog.PreviewShownForProbe, "un EPUB : pas d'aperçu");
                dialog.ChooseForProbe(ExportPresets.Bfp);
                dialog.CheckAllForProbe(true);
                await Settle();
                Check(dialog.PlanForProbe.Texts.Count > 0 && dialog.PlanForProbe.Sheets.Count == sheets.Count && (plan == null || dialog.PlanForProbe.Plans.Count > 0), "« Tout » avec le BFP : écrits, fiches et plans partent (" + dialog.PlanForProbe.Summary() + ")");
                var outside = texts.Find(delegate(BinderItem t) { return InOutOfBook(t); });
                Check(outside == null || !dialog.RequestForProbe().ItemIds.Contains(outside.Id), "« Tout » laisse le Hors-livre de côté");
                tabs.SelectedIndex = 1;
                await Settle();
                Shot(dialog, "export-4-ampleur.png");
                Check(dialog.ToggleForProbe(sheets[0].Id) && dialog.PlanForProbe.Sheets.Count == sheets.Count - 1, "décocher une fiche la retire du plan");
                tabs.SelectedIndex = 5;
                await Settle();
                Shot(dialog, "export-5-livre.png");
                tabs.SelectedIndex = 7;
                await Settle();
                Shot(dialog, "export-6-verifications.png");

                // Supprimer le préréglage de la sonde.
                dialog.RemovePresetForProbe(saved.Id);
                await Settle();
                Check(ExportPresetStore.Custom.Count == customBefore && dialog.PresetCardsForProbe == 10 + customBefore, "le préréglage personnalisé se supprime");
                var request = dialog.RequestForProbe();
                Check(request.Preset.Format == ExportFormats.Pdf && request.ItemIds.Count > 0 && request.WriteBackBook == null, "la demande rendue : le préréglage réglé, l'ampleur cochée");
                dialog.Close();
                await Settle();
                Check((await task) == null, "fermer la fenêtre n'exporte rien");
            }

            // « L'écrit sélectionné » : le gestionnaire, cet écrit déjà coché.
            var chapter = project.FindById(bookScope[0]);
            task = ExportManagerDialog.Ask(shell, project, shell.ScopeForProbe(chapter), ExportPresets.WordA4, null);
            await Settle();
            dialog = OpenWindow<ExportManagerDialog>();
            Check(dialog != null && dialog.PlanForProbe.Count == 1 && dialog.PlanForProbe.Texts[0] == chapter, "« L'écrit sélectionné » : le gestionnaire s'ouvre, cet écrit seul coché");
            if (dialog != null)
            {
                Check(!dialog.PreviewShownForProbe, "sans aperçu fourni : pas de bouton");
                dialog.Close();
                await Settle();
                await task;
            }

            // ================================================== de vrais fichiers
            var shots = Environment.GetEnvironmentVariable("MARABOOK_PROBE_SHOTS");
            var folder = string.IsNullOrEmpty(shots) ? Path.Combine(Path.GetTempPath(), "marabook-sonde-export-" + Guid.NewGuid().ToString("N")) : Path.Combine(shots, "export");
            Directory.CreateDirectory(folder);
            try
            {
                var everything = new List<string>();
                foreach (var text in texts) if (!InOutOfBook(text)) everything.Add(text.Id);
                foreach (var sheet in sheets) everything.Add(sheet.Id);
                if (plan != null) everything.Add(plan.Id);
                if (map != null) everything.Add(map.Id);
                var meta = ExportMeta.For(project, book, null);

                // Le BFP : tout, à chaque format.
                var path = Path.Combine(folder, "bfp.pdf");
                var done = shell.ExportForProbe(HousePreset(ExportPresets.Bfp), everything, path, meta);
                var pdf = Latin1(path);
                Check(pdf.StartsWith("%PDF-1.4") && pdf.TrimEnd().EndsWith("%%EOF"), "BFP : un PDF écrit (" + new FileInfo(path).Length / 1024 + " Ko, " + Occurrences(pdf, "/Type /Page /Parent") + " pages)");
                var boxes = new HashSet<string>();
                for (var at = pdf.IndexOf("/MediaBox [", StringComparison.Ordinal); at >= 0; at = pdf.IndexOf("/MediaBox [", at + 1, StringComparison.Ordinal))
                    boxes.Add(pdf.Substring(at, pdf.IndexOf(']', at) - at));
                Check(boxes.Count >= 2 || (plan == null && map == null), "BFP : des pages de tailles différentes (" + boxes.Count + " formats)");
                Check(pdf.Contains("/Type /Outlines"), "BFP : des signets");
                if (plan != null)
                {
                    Check(done.Plans.Count > 0 && pdf.Contains("/Subtype /Image"), "BFP : le plan y est, en page-image");
                    var planOnly = Path.Combine(folder, "plan.pdf");
                    shell.ExportForProbe(HousePreset(ExportPresets.Bfp), new[] { plan.Id }, planOnly, null);
                    var planPdf = Latin1(planOnly);
                    Check(Occurrences(planPdf, "/Type /Page /Parent") == 1 && planPdf.Contains("/Subtype /Image") && !planPdf.Contains("/MediaBox [0 0 595.276 841.89]"),
                        "un plan seul : une page, à la taille du plan (pas un A4)");
                }
                if (map != null && MindMapModules.Provider != null) Check(done.MindMaps.Count > 0, "BFP : la carte mentale y est");

                // Le PDF Encyclopédique : des liens entre fiches.
                path = Path.Combine(folder, "encyclopedie.pdf");
                var sheetIds = new List<string>();
                foreach (var sheet in sheets) sheetIds.Add(sheet.Id);
                shell.ExportForProbe(HousePreset(ExportPresets.EncyclopediaPdf), sheetIds, path, null);
                pdf = Latin1(path);
                var flows = ExportFlow.Build(project, ExportPlan.Build(project, sheetIds, HousePreset(ExportPresets.EncyclopediaPdf), null), HousePreset(ExportPresets.EncyclopediaPdf), null);
                var expected = flows.Count == 0 ? 0 : flows[0].Links.Count;
                Check(pdf.Contains("/MediaBox [0 0 595.276 841.89]") && pdf.Contains("/Type /Outlines"), "PDF Encyclopédique : en A4, un signet par fiche");
                Check(expected == 0 || Occurrences(pdf, "/Subtype /Link") >= expected, "PDF Encyclopédique : les liens entre fiches sont cliquables (" + Occurrences(pdf, "/Subtype /Link") + " zones pour " + expected + " liens)");

                // Le BAT : le livre, à son gabarit, en CMJN, avec ses traits de coupe.
                if (book.Book == null) book.Book = new BookInfo(); // comme l'onglet Publication à son ouverture
                path = Path.Combine(folder, "bat.pdf");
                done = shell.ExportForProbe(HousePreset(ExportPresets.Bat), bookScope, path, meta);
                pdf = Latin1(path);
                var template = book.Book.Template;
                Check(done.Book == book && pdf.Contains("/OutputConditionIdentifier (FOGRA39)"), "BAT Imprimeur : le livre, en CMJN FOGRA39");
                Check(pdf.Contains("/TrimBox [") && pdf.Contains("/BleedBox ["), "BAT : boîtes de coupe et de fond perdu");
                var trimW = template.PageWidthMm * 72 / 25.4;
                var margin = (book.Book.BleedMm + 5) * 72 / 25.4;
                var media = "/MediaBox [0 0 " + (trimW + 2 * margin).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " ";
                Check(pdf.Contains(media), "BAT : le format du gabarit, plus le fond perdu du livre et la zone des traits (" + media.Trim() + "…)");
                Check(shell.CompileForPublishForProbe(book).Paragraphs.Count == ExportFlow.Build(project, done, HousePreset(ExportPresets.Bat), meta)[0].Document.Paragraphs.Count,
                    "BAT : le même manuscrit que l'ancien « Publier », paragraphe pour paragraphe");

                // Le manuscrit papier.
                path = Path.Combine(folder, "manuscrit.pdf");
                shell.ExportForProbe(HousePreset(ExportPresets.Manuscript), bookScope, path, meta);
                pdf = Latin1(path);
                Check(pdf.Contains("/MediaBox [0 0 595.276 841.89]") && !pdf.Contains("FOGRA39"), "PDF Manuscrit papier : en A4, en RVB");

                // PDF A4, un écrit.
                path = Path.Combine(folder, "ecrit.pdf");
                shell.ExportForProbe(HousePreset(ExportPresets.PdfA4), new[] { chapter.Id }, path, null);
                Check(Latin1(path).Contains("/MediaBox [0 0 595.276 841.89]") && !Latin1(path).Contains("/Type /Outlines"), "PDF A4 d'un écrit seul : en A4, sans signets (un seul n'aide personne)");

                // Word.
                path = Path.Combine(folder, "word-a4.docx");
                shell.ExportForProbe(HousePreset(ExportPresets.WordA4), everything, path, meta);
                var xml = ZipText(path, "word/document.xml");
                Check(Occurrences(xml, "<w:sectPr>") == 1 && xml.Contains("<w:pgSz w:w=\"11906\" w:h=\"16838\"/>"), "Document Word A4 : une seule section, en A4");
                Check(ZipText(path, "docProps/core.xml") != null, "…avec le titre et l'auteur·ice du fichier");
                path = Path.Combine(folder, "word-mis-en-forme.docx");
                shell.ExportForProbe(HousePreset(ExportPresets.WordLayout), bookScope, path, meta);
                xml = ZipText(path, "word/document.xml");
                var w = (int)Math.Round(template.PageWidthMm * 1440.0 / 25.4);
                Check(xml.Contains("<w:pgSz w:w=\"" + w + "\""), "Document Word mis en forme : les écrits du livre gardent le format de leur page (" + ExportPages.Describe(template) + ")");

                // EPUB.
                path = Path.Combine(folder, "livre.epub");
                done = shell.ExportForProbe(HousePreset(ExportPresets.EpubPublish), bookScope, path, meta);
                using (var zip = ZipFile.OpenRead(path))
                {
                    Check(zip.GetEntry("mimetype") != null && zip.GetEntry("OEBPS/content.opf") != null, "EPUB Publiable : un EPUB écrit (" + done.Texts.Count + " écrits)");
                    Check((meta.CoverImageId != null) == (zip.GetEntry("OEBPS/cover.xhtml") != null), "…avec la couverture du livre quand il en a une");
                }
                Check(ZipText(path, "OEBPS/content.opf").Contains(Epub.Esc(meta.Title)), "…et le titre du livre dans ses métadonnées");
                var epubProblems = EpubCheck.Verify(path);
                Check(epubProblems.Count == 0, "…et il passe le contrôle de structure" + (epubProblems.Count > 0 ? " — " + string.Join(" / ", epubProblems.ToArray()) : ""));

                // EPUB pour Kindle, tout coché : lettrine, petites capitales, page de titre, titres de partie.
                path = Path.Combine(folder, "livre-kindle.epub");
                var kindle = HousePreset(ExportPresets.EpubKindle);
                kindle.DropCaps = true; kindle.OpeningSmallCaps = true; kindle.TitlePage = true; kindle.SplitFolders = true; kindle.SplitSubfolders = true;
                shell.ExportForProbe(kindle, bookScope, path, meta);
                epubProblems = EpubCheck.Verify(path);
                Check(epubProblems.Count == 0, "EPUB pour Kindle : il passe le contrôle de structure" + (epubProblems.Count > 0 ? " — " + string.Join(" / ", epubProblems.ToArray()) : ""));
                using (var zip = ZipFile.OpenRead(path))
                {
                    Check(zip.GetEntry("OEBPS/cover.xhtml") == null && zip.GetEntry("OEBPS/title.xhtml") != null, "…sans page de couverture en double, avec sa page de titre");
                    var lettrines = 0;
                    foreach (var entry in zip.Entries)
                        if (entry.FullName.StartsWith("OEBPS/chapter-")) lettrines += Occurrences(ZipText(path, entry.FullName), "class=\"lettrine\"");
                    Check(lettrines > 0, "…et des lettrines en ouverture de chapitre (" + lettrines + ")");
                }
                Check((meta.CoverImageId != null) == ZipText(path, "OEBPS/content.opf").Contains("properties=\"cover-image\""), "…la couverture reste déclarée dans le paquet quand le livre en a une");

                // Le rendu par niveau dans un PDF et dans Word ; les pages blanches retirées.
                path = Path.Combine(folder, "niveaux.docx");
                var leveled = HousePreset(ExportPresets.WordA4);
                leveled.SplitBooks = true; leveled.SplitFolders = true; leveled.SplitSubfolders = true;
                shell.ExportForProbe(leveled, bookScope, path, meta);
                Check(ZipText(path, "word/styles.xml").Contains("Export — titre de livre") && ZipText(path, "word/document.xml").Contains(Epub.Esc(BookInfo.TitleOf(book))),
                    "Word, séparé par niveau : le titre du livre y est, dans son style « Export — titre de livre »");
                var recto = HousePreset(ExportPresets.Bat);
                var rectoPages = shell.ComposeForProbe(new ExportRequest { Preset = recto, Meta = meta, ItemIds = new List<string>(bookScope) });
                var flat = HousePreset(ExportPresets.Bat);
                flat.RectoStarts = false; flat.DropBlankPages = true;
                var flatPages = shell.ComposeForProbe(new ExportRequest { Preset = flat, Meta = meta, ItemIds = new List<string>(bookScope) });
                int rectoCount = 0, flatCount = 0, blankLeft = 0;
                foreach (var part in rectoPages ?? new List<Print.PdfPart>()) if (!part.IsImage) rectoCount += part.Composition.Pages.Count;
                foreach (var part in flatPages ?? new List<Print.PdfPart>())
                {
                    if (part.IsImage) continue;
                    flatCount += part.Composition.Pages.Count;
                    foreach (var page in part.Composition.Pages) if (page.Lines.Count == 0 && page.Images.Count == 0) blankLeft++;
                }
                Check(flatCount > 0 && flatCount <= rectoCount && blankLeft == 0, "le livre sans pages blanches : " + flatCount + " pages au lieu de " + rectoCount + " en belle page, aucune vide");

                // Les autres formats, par un préréglage réglé à la main.
                foreach (var format in new[] { ExportFormats.Odt, ExportFormats.Markdown, ExportFormats.Text })
                {
                    var preset = HousePreset(ExportPresets.WordA4);
                    preset.Format = format;
                    path = Path.Combine(folder, "autre" + ExportFormats.Extension(format));
                    shell.ExportForProbe(preset, new[] { chapter.Id }, path, null);
                    Check(new FileInfo(path).Length > 20, ExportFormats.Short(format) + " : toujours exportable depuis le gestionnaire");
                }

                // Les exports rapides : leurs demandes.
                var quick = shell.QuickRequestForProbe(ExportPresets.WordLayout, chapter);
                Check(quick.Preset.Format == ExportFormats.Docx && quick.Preset.Pages == ExportPages.Source && quick.ItemIds.Count == 1 && quick.ItemIds[0] == chapter.Id,
                    "Export rapide DOCX : l'écrit ouvert, en Word, avec SA mise en page");
            }
            finally
            {
                if (string.IsNullOrEmpty(shots)) try { Directory.Delete(folder, true); } catch { }
            }
        }
    }
}

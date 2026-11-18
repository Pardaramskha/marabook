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
            var task = ExportManagerDialog.Ask(shell, project, bookScope, ExportPresets.Bat, delegate(ExportRequest r) { return true; });
            await Settle();
            var dialog = OpenWindow<ExportManagerDialog>();
            Check(dialog != null, "« Publier » : le gestionnaire d'export s'ouvre");
            if (dialog != null)
            {
                var tabs = dialog.TabsForProbe;
                var headers = new List<string>();
                foreach (var tab in tabs.Items.OfType<TabItem>()) headers.Add(tab.Header as string);
                Check(string.Join(" | ", headers.ToArray()) == "Préréglage | Ampleur | Options | Impression | Livre | Vérifications" || string.Join(" | ", headers.ToArray()).StartsWith("Préréglage | Ampleur | Options | Impression | Livre | Vérifications"),
                    "six onglets : Préréglage, Ampleur, Options, Impression, Livre, Vérifications (" + string.Join(" | ", headers.ToArray()) + ")");
                Check(tabs.TabStripPlacement == Dock.Left && tabs.Classes.Contains("column"), "les onglets tiennent en colonne, à gauche");
                Check(dialog.PresetCardsForProbe == 9 + customBefore, "les neuf préréglages de la maison sont proposés");
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
                Check(ExportPresetStore.Custom.Count == customBefore + 1 && dialog.PresetCardsForProbe == 10 + customBefore, "enregistré : un dixième préréglage, dans « Mes préréglages »");
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
                Shot(dialog, "export-2-manuscrit.png");
                tabs.SelectedIndex = 2;
                await Settle();
                Shot(dialog, "export-3-options.png");
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
                tabs.SelectedIndex = 4;
                await Settle();
                Shot(dialog, "export-5-livre.png");
                tabs.SelectedIndex = 5;
                await Settle();
                Shot(dialog, "export-6-verifications.png");

                // Supprimer le préréglage de la sonde.
                dialog.RemovePresetForProbe(saved.Id);
                await Settle();
                Check(ExportPresetStore.Custom.Count == customBefore && dialog.PresetCardsForProbe == 9 + customBefore, "le préréglage personnalisé se supprime");
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

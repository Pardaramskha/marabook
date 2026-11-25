using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde du LOT 27 (2.0.0, 21/11/2026) : un seul caret dans la
    /// vue scindée ; la fiche de scène (rail, fenêtre, annulation, fiches sans
    /// scène) ; le Tableau d'un livre ; l'onglet « Projet » des Préférences et
    /// les modèles de projet. MARABOOK_PROBE_ONLY=scenes la joue seule.</summary>
    public static partial class Probes
    {
        private static async Task ProbeScenes(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- scènes, Tableau, onglet Projet (2.0.0, 21/11)");
            var added = new List<BinderItem>();
            var outlinerShown = AppSettings.OutlinerShown;
            var outlinerScenes = AppSettings.OutlinerScenes;
            var templatesFolder = Path.Combine(Path.GetTempPath(), "marabook-sonde-modeles-" + Guid.NewGuid().ToString("N"));
            var previousFolder = ProjectTemplates.FolderOverride;
            ProjectTemplates.FolderOverride = templatesFolder;
            try
            {
                var writings = project.Category(Project.KeyWritings);
                var book = new BinderItem { Kind = ItemKind.Book, Title = "Livre des scènes (sonde)" };
                book.Parent = writings;
                writings.Children.Add(book);
                added.Add(book);
                var keira = FirstTitled(project, "Keira");
                var name = keira == null ? "Personne" : keira.Title;
                var first = SceneText("Chapitre un (sonde)", name + " entre dans la salle.", "Elle parle peu.", "***", "Le quai est désert ce matin.", "***", "Le soir tombe sur la ville.");
                var second = SceneText("Chapitre deux (sonde)", "Un seul bloc, sans coupure.");
                foreach (var item in new[] { first, second }) { item.Parent = book; book.Children.Add(item); }
                shell.Binder.Rebuild();

                // ------------------------------------------------ un seul caret dans la vue scindée
                shell.Binder.SelectItem(first.Id);
                await Settle();
                shell.OpenSplit(second);
                await Settle();
                await Settle();
                var pane = shell.SplitForProbe;
                var left = shell.Editor.Composed;
                var right = pane.TextForProbe;
                pane.ForcedKeyboard = true; // le clavier est dans le volet
                left.RefreshCaretNow();
                right.RefreshCaretNow();
                Check(right.CaretShownForProbe && !left.CaretShownForProbe, "vue scindée, clavier à droite : le caret de droite se montre, celui de gauche se tait");
                pane.ForcedKeyboard = false; // le clavier est revenu à gauche
                left.RefreshCaretNow();
                right.RefreshCaretNow();
                Check(left.CaretShownForProbe && !right.CaretShownForProbe, "…clavier à gauche : c'est l'inverse — jamais deux carets à la fois");
                pane.ForcedKeyboard = null;
                shell.CloseSplit();
                await Settle();
                Check(left.CaretHidden == null, "la vue scindée fermée, le caret de l'écrit reprend sa vie d'avant");

                // ------------------------------------------------ la fiche de scène, dans le rail
                var panel = shell.ScenePanelForProbe;
                var textBefore = first.Document.ToPlainText();
                left.PlaceCaretForProbe(0, 0);
                await Settle();
                Check(panel.IsVisible && panel.SceneCount == 3 && panel.HeaderForProbe == "Scène 1 sur 3", "le rail montre « Scène 1 sur 3 » : deux séparateurs, trois scènes (" + panel.HeaderForProbe + ")");
                left.PlaceCaretForProbe(3, 2);
                await Settle();
                Check(panel.SceneIndex == 1 && panel.HeaderForProbe == "Scène 2 sur 3", "le curseur passe dans la deuxième scène : la fiche suit (" + panel.HeaderForProbe + ")");
                Check(panel.TextForProbe == "Rien de renseigné.", "…vide pour l'instant");

                var dirtyBefore = shell.DirtyForProbe;
                var editing = shell.EditScene(first, 0);
                await Settle();
                var dialog = SceneDialog.LastForProbe;
                Check(dialog != null && dialog.Title.StartsWith("Scène 1 sur 3"), "« Renseigner la scène… » ouvre la fiche de la scène 1");
                if (dialog != null)
                {
                    if (keira != null)
                    {
                        Check(dialog.SuggestedForProbe >= 1, "la fenêtre propose les personnages nommés dans la scène (" + dialog.SuggestedForProbe + ")");
                        dialog.AcceptSuggestionsForProbe();
                        Check(dialog.CharactersForProbe >= 1 && dialog.SuggestedForProbe == 0, "…un clic les ajoute aux présents");
                        dialog.FillForProbe(keira.Title, null, "au crépuscule", "L'arrivée");
                    }
                    else dialog.FillForProbe(null, null, "au crépuscule", "L'arrivée");
                    await Settle();
                    Check(dialog.WhenEchoForProbe.Contains("gardé tel quel") || dialog.WhenEchoForProbe.Contains("Gardé tel quel"), "un moment qui n'est pas une date est gardé tel quel, et la fenêtre le dit");
                    Shot(dialog, "scenes-1-fiche-de-scene.png");
                    dialog.AcceptForProbe();
                }
                var changed = await editing;
                await Settle();
                Check(changed && first.Scenes.Count == 3 && first.Scenes[0].When == "au crépuscule" && first.Scenes[0].Note == "L'arrivée" && (keira == null || first.Scenes[0].PovId == keira.Id), "la fiche est enregistrée sur la scène 1");
                Check(first.Document.ToPlainText() == textBefore, "…sans qu'un caractère du texte ait changé");
                Check(shell.DirtyForProbe, "…et le projet est marqué modifié");
                left.PlaceCaretForProbe(0, 0);
                await Settle();
                Check(panel.TextForProbe.Contains("au crépuscule") && panel.TextForProbe.Contains("L'arrivée") && (keira == null || panel.TextForProbe.Contains(keira.Title)), "le rail la montre (" + panel.TextForProbe + ")");
                Check(shell.UndoForProbe() && first.Scenes[0].IsEmpty, "Ctrl+Z vide la fiche");
                await Settle();
                Check(panel.TextForProbe == "Rien de renseigné.", "…le rail suit");
                Check(shell.RedoForProbe() && first.Scenes[0].Note == "L'arrivée", "…et Ctrl+Y la rend");
                await Settle();

                // Les flèches du rail mènent le curseur de scène en scène.
                panel.NextForProbe.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                int paragraph, offset;
                left.GetCaret(out paragraph, out offset);
                Check(panel.SceneIndex == 1 && paragraph == 3, "la flèche « Scène suivante » mène le curseur au début de la scène 2 (paragraphe " + paragraph + ")");

                // Un séparateur retiré : la fiche de la scène absorbée n'est pas jetée.
                first.Scenes[2].Note = "Le soir";
                first.Document.Paragraphs.RemoveAt(4);
                shell.Editor.Composed.Attach(first, project.Styles.EffectiveFor(first), first.Page ?? project.Page, project);
                left.PlaceCaretForProbe(0, 0);
                await Settle();
                panel.Refresh();
                Check(panel.SceneCount == 2 && panel.OrphansForProbe == 1, "un séparateur retiré : deux scènes, et une fiche « sans scène » signalée dans le rail");
                var drop = panel.OrphanButtonForProbe("scene-orphan-drop");
                Check(drop != null, "…avec « Reprendre ici » et « Supprimer »");
                if (drop != null) drop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                Check(panel.OrphansForProbe == 0 && first.Scenes.Count == 2 && first.Scenes[0].Note == "L'arrivée", "« Supprimer » la retire ; les autres fiches restent");
                Shot(shell, "scenes-2-rail.png");

                // ------------------------------------------------ le Tableau d'un livre
                first.Status = "draft";
                AppSettings.OutlinerShown = false;
                shell.Binder.SelectItem(book.Id);
                await Settle();
                await Settle();
                var texts = shell.BookViewForProbe.TextsForProbe;
                var toggle = texts.OutlineToggleForProbe;
                Check(shell.VisibleView == "book" || shell.BookViewForProbe.IsVisible, "le livre est ouvert");
                Check(toggle.IsVisible && !texts.OutlinerShownForProbe, "l'onglet Textes d'un livre a un bouton « Curation » (ex-« Tableau ») ; les cartes d'abord");
                toggle.IsChecked = true;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                await Settle();
                var outliner = texts.OutlinerForProbe;
                Check(texts.OutlinerShownForProbe && AppSettings.OutlinerShown, "« Curation » remplace les cartes par le tableau (et s'en souvient)");
                var rows = outliner.RowsForProbe;
                var kinds = "";
                foreach (var row in rows) kinds += row.Kind == OutlineRowKind.Folder ? "D" : row.Kind == OutlineRowKind.Text ? "E" : "s";
                Check(kinds == "EssE", "une ligne par écrit, et une par scène sous l'écrit qui en a deux (" + kinds + ")");
                Check(rows.Count == 4 && rows[1].Note == "L'arrivée" && rows[1].When == "au crépuscule" && rows[3].SceneCount == 1, "…les fiches de scène y sont, colonne par colonne");
                Check(outliner.TotalsForProbe.StartsWith("2 écrits · 3 scènes · "), "le pied totalise : " + outliner.TotalsForProbe);
                Check(outliner.TableWidthForProbe >= OutlinerView.FloorTotalForProbe - 1, "huit colonnes : titre, état, mots, point de vue, lieu, moment, personnages, synopsis (" + outliner.TableWidthForProbe.ToString("0") + " px)");
                Shot(shell, "scenes-3-tableau.png");

                outliner.SetStatus(second, "done");
                outliner.SetSynopsis(second, "Un chapitre d'un seul tenant.");
                await Settle();
                Check(second.Status == "done" && second.Synopsis == "Un chapitre d'un seul tenant." && shell.DirtyForProbe, "l'état et le synopsis se changent sur place");
                outliner.SortForProbe(Outline.SortWordsDown);
                await Settle();
                Check(outliner.RowsForProbe.Count == 3 && outliner.RowsForProbe[0].Words >= outliner.RowsForProbe[1].Words && book.Children[0] == first, "un tri range les lignes à plat sans toucher à l'ordre du livre");
                outliner.SortForProbe(Outline.SortBook);
                outliner.ScenesForProbe(false);
                await Settle();
                Check(outliner.RowsForProbe.Count == 2 && !AppSettings.OutlinerScenes, "« Une ligne par scène » décoché : les écrits seuls");
                outliner.ScenesForProbe(true);
                outliner.StatusForProbe(2); // « Brouillon »
                await Settle();
                Check(outliner.RowsForProbe.Count == 3 && outliner.RowsForProbe[0].Item == first, "le filtre d'état ne garde que les brouillons");
                outliner.StatusForProbe(0);
                await Settle();
                Check(outliner.ExportText("x.csv").StartsWith("Titre;État;Mots;") && outliner.ExportText("x.md").StartsWith("# " + book.Title), "le tableau s'exporte en CSV et en Markdown");

                // Un clic sur une cellule de scène ouvre sa fiche ; le tableau se relit.
                var fromTable = shell.EditScene(second, 0);
                await Settle();
                dialog = SceneDialog.LastForProbe;
                if (dialog != null) { dialog.FillForProbe(null, null, null, "Depuis le tableau"); dialog.AcceptForProbe(); }
                await fromTable;
                await Settle();
                Check(second.Scenes.Count == 1 && second.Scenes[0].Note == "Depuis le tableau" && outliner.RowsForProbe.Count == 4, "la fiche d'une scène se renseigne aussi depuis le Tableau, qui se relit");

                toggle.IsChecked = false;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                Check(!texts.OutlinerShownForProbe && !AppSettings.OutlinerShown, "recliquer « Curation » rend les cartes");

                // ------------------------------------------------ Préférences › Projet et les modèles
                var prefs = new PreferencesDialog(shell, project);
                prefs.Show(shell);
                await Settle();
                var titles = new List<string>();
                foreach (var item in prefs.Tabs.Items) titles.Add(((TabItem)item).Header as string);
                Check(titles.Count > 1 && titles[0] == "Projet" && titles[1] == "Personnalisation" && prefs.Tabs.SelectedIndex == 0, "Préférences : « Projet » est le premier onglet, ouvert d'office");
                var facts = new Dictionary<string, string>();
                foreach (var block in prefs.GetVisualDescendants().OfType<SelectableTextBlock>())
                {
                    var tag = block.Tag as string;
                    if (tag != null && tag.StartsWith("project-fact:")) facts[tag.Substring(13)] = block.Text ?? "";
                }
                Check(facts.ContainsKey("Nom") && facts.ContainsKey("Fichier") && facts.ContainsKey("Créé le") && facts.ContainsKey("Modifié le") && facts.ContainsKey("Contenu"),
                    "…il dit le nom, le fichier, les dates de création et de modification, le contenu (" + (facts.ContainsKey("Contenu") ? facts["Contenu"] : "-") + ")");
                Button reveal = null, analyzer = null, save = null;
                foreach (var button in prefs.GetVisualDescendants().OfType<Button>())
                {
                    if ("project-reveal".Equals(button.Tag)) reveal = button;
                    if ("project-analyzer".Equals(button.Tag)) analyzer = button;
                    if ("project-save-template".Equals(button.Tag)) save = button;
                }
                var path = shell.ProjectPathForPreferences;
                var onDisk = !string.IsNullOrEmpty(path) && File.Exists(path);
                Check(reveal != null && analyzer != null && save != null && reveal.IsEnabled == onDisk, "…« " + PreferencesDialog.RevealLabel + " » (" + (onDisk ? "actif" : "grisé tant que le projet n'a pas de fichier") + "), « Analyseur de .plot… » et « Enregistrer en tant que modèle… »");
                var createdBefore = project.CreatedAt;
                var saveIdBefore = project.SaveId;
                Check(prefs.SaveTemplate("Modèle de la sonde", shell) && prefs.TemplateRowsForProbe == 1 && prefs.TemplateStatusForProbe.Contains("enregistré"), "« Enregistrer en tant que modèle » crée le modèle et le liste (" + prefs.TemplateStatusForProbe + ")");
                Check(project.CreatedAt == createdBefore && project.SaveId == saveIdBefore && first.Document.ToPlainText().Length > 0, "…sans toucher au projet ouvert");
                var entries = ProjectTemplates.All();
                if (entries.Count == 1)
                {
                    var born = ProjectTemplates.Instantiate(entries[0].Path);
                    var bornWritings = born.Category(Project.KeyWritings);
                    Check(FirstTitled(born, "Chapitre un (sonde)") == null && FirstTitled(born, "Livre des scènes (sonde)") == null && bornWritings.Children.Count == 1 && bornWritings.Children[0].Kind == ItemKind.Book && bornWritings.Children[0].Title == "Nouveau livre"
                        && born.Category(Project.KeySheets).Children.Count == 0 && born.Templates.Count == project.Templates.Count && born.Lexicon.Count == 0,
                        "un projet né du modèle n'a ni écrit ni titre du projet d'origine : un « Nouveau livre », les modèles de fiche, pas de fiches, pas de dictionnaire");
                }
                await Settle();
                Shot(prefs, "scenes-4-preferences-projet.png");
                prefs.Close();
                await Settle();
            }
            finally
            {
                ProjectTemplates.FolderOverride = previousFolder;
                try { if (Directory.Exists(templatesFolder)) Directory.Delete(templatesFolder, true); } catch { }
                AppSettings.OutlinerShown = outlinerShown;
                AppSettings.OutlinerScenes = outlinerScenes;
                shell.CloseSplit();
                var home = project.Category(Project.KeyWritings);
                shell.Binder.SelectItem(home.Id);
                await Settle();
                foreach (var item in added)
                    if (item != null && item.Parent != null) item.Parent.Children.Remove(item);
                shell.Binder.Rebuild();
                await Settle();
            }
        }

        private static BinderItem SceneText(string title, params string[] lines)
        {
            var item = new BinderItem { Kind = ItemKind.Text, Title = title, Document = new TextDocument() };
            foreach (var line in lines)
            {
                var paragraph = new TextParagraph();
                if (line == "***") paragraph.StyleId = StyleSheet.SeparatorId;
                else paragraph.Runs.Add(new TextRun { Text = line });
                item.Document.Paragraphs.Add(paragraph);
            }
            return item;
        }
    }
}

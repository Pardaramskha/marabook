using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde de l'application (P1, « --probe ») : de vraies
    /// fenêtres, comme les sondes WPF — la coquille sur le projet d'exemple,
    /// la Pile, l'inspecteur, les Préférences, la boîte de message, le thème.
    /// Chaque vérification écrit OK ou ÉCHEC ; le code de retour du processus
    /// est le nombre d'échecs. Tourne sur Windows et sous WSLg.</summary>
    public static class Probes
    {
        public static int Failures;
        private static int _checks;

        private static void Check(bool condition, string label)
        {
            _checks++;
            if (!condition) Failures++;
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
        }

        private static async Task Settle()
        {
            await Task.Delay(120);
            await Dispatcher.UIThread.InvokeAsync(delegate { }, DispatcherPriority.Background);
        }

        public static async Task Run(MainWindow shell)
        {
            Console.WriteLine("== Sonde Avalonia P1-P2 (" + AppPlatform.OsName + ")");
            try
            {
                await Settle();
                // — La coquille sur le projet d'exemple.
                Check(shell.Project != null, "le projet d'exemple est ouvert");
                var roots = shell.Project.Roots;
                Check(roots != null && roots.Count == 8, "la Pile montre les huit racines (" + (roots == null ? "-" : roots.Count.ToString()) + ")");
                var writings = shell.Project.Category(Project.KeyWritings);
                var book = writings.Children.Count > 1 ? writings.Children[1] : null;
                Check(book != null && book.Kind == ItemKind.Book && book.Children.Count == 3, "le livre d'exemple a trois chapitres");
                var chapter = book == null ? null : book.Children[0];
                if (chapter != null) shell.Binder.SelectItem(chapter.Id, true);
                await Settle();
                Check(shell.InspectorTitle == (chapter == null ? "" : chapter.Title), "sélectionner un écrit : l'inspecteur montre son titre (" + shell.InspectorTitle + ")");
                Check(shell.InspectorKind == "Écrit" && shell.InspectorDetail.Contains("Mots"), "…sa nature et ses mots (" + shell.InspectorDetail.Replace("\n", " · ") + ")");
                Check(shell.Title.Contains("Marabook"), "le titre de la fenêtre nomme l'application (" + shell.Title + ")");
                Check(shell.StatusText.Length > 0, "la barre d'état dit quelque chose (" + shell.StatusText + ")");

                // — P2 : l'éditeur composé sur l'écrit sélectionné.
                var editor = shell.Editor;
                Check(editor.IsVisible && chapter != null && editor.ShowsItem(chapter), "l'écrit s'ouvre dans l'éditeur composé");
                var composed = shell.Composed;
                Check(composed != null && composed.HasItem && composed.IsVisible, "la surface composée est attachée");
                Check(shell.StatusPagesText.Contains("1"), "la barre d'état donne la page du caret (" + shell.StatusPagesText + ")");
                Check(FontCatalog.Entries.Count > 10, "le catalogue de polices énumère les polices installées (" + FontCatalog.Entries.Count + ")");
                Check(!string.IsNullOrEmpty(editor.CurrentFontName), "le sélecteur de police montre la police du caret (" + editor.CurrentFontName + ")");
                var engine = new AvaloniaFontEngine();
                var face = engine.Resolve("Times New Roman", 400, false);
                Check(face.HasGlyphs && face.Baseline > 0.7 && face.Baseline < 1.0, "le moteur de polices résout Times New Roman (ligne de base " + face.Baseline.ToString("0.000") + ")");
                var advance = engine.AdvanceWidth("Times New Roman", 12, 400, false, 'a');
                Check(advance > 4 && advance < 8, "…et mesure l'avance d'un « a » à 12 px (" + advance.ToString("0.00") + ")");
                int faceIndex; string fallbackName;
                var fontFile = engine.FontFile(face, out faceIndex, out fallbackName);
                Check(fontFile != null && fontFile.Length > 100000, "…et lit le fichier de la police pour le PDF (" + (fontFile == null ? "aucun" : fontFile.Length + " octets") + ")");

                // — Frapper, annuler, mettre en gras.
                var document = chapter.Document;
                var original = document.Paragraphs[0].ToPlainText();
                composed.PlaceCaret(0, 0, false);
                composed.TypeText("Sonde ");
                await Settle();
                Check(document.Paragraphs[0].ToPlainText() == "Sonde " + original, "la frappe entre dans le pivot");
                Check(composed.Undo() && document.Paragraphs[0].ToPlainText() == original, "Ctrl+Z la retire");
                composed.SelectAll();
                composed.ToggleBold();
                await Settle();
                var bold = document.Paragraphs[0].Runs.Count > 0 && document.Paragraphs[0].Runs[0].Bold == true;
                Check(bold, "tout sélectionner + gras : le premier run est en gras");
                Check(composed.Undo() && !(document.Paragraphs[0].Runs.Count > 0 && document.Paragraphs[0].Runs[0].Bold == true), "…et Ctrl+Z le rend");
                composed.PlaceCaret(0, 0, false);

                // — Le PDF avec le moteur Avalonia : police embarquée.
                var pdfPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-sonde-p2.pdf");
                try
                {
                    var composition = Marabook.Print.Composer.Compose(document, shell.Project.Styles.EffectiveFor(chapter), shell.Project.Page, shell.Project, engine);
                    Marabook.Print.PdfWriter.Write(pdfPath, composition, new Marabook.Print.PdfExportOptions());
                    var pdf = System.IO.File.ReadAllBytes(pdfPath);
                    var text = System.Text.Encoding.Latin1.GetString(pdf);
                    Check(pdf.Length > 5000 && text.StartsWith("%PDF") && text.Contains("/FontFile"), "le PDF s'écrit avec la police TrueType embarquée (" + pdf.Length + " octets)");
                }
                catch (Exception error) { Check(false, "le PDF s'écrit : " + error.Message); }
                finally { try { System.IO.File.Delete(pdfPath); } catch { } }

                // — P3 : le tour des vues — chaque racine, chaque élément, les
                // panneaux de droite ; une vue qui lève une exception fait
                // tomber le processus, c'est le verdict.
                var visited = 0;
                foreach (var root in shell.Project.Roots)
                {
                    shell.Binder.SelectItem(root.Id, true);
                    await Settle();
                    visited++;
                }
                Check(visited == shell.Project.Roots.Count, "chaque racine de la Pile s'ouvre (" + visited + ")");
                var kinds = new HashSet<ItemKind>();
                var wrong = new List<string>();
                foreach (var item in shell.Project.AllItems())
                {
                    if (item.IsCategory) continue;
                    shell.Binder.SelectItem(item.Id, true);
                    await Settle();
                    kinds.Add(item.Kind);
                    var expected = item.Kind == ItemKind.Text ? "editor" : item.Kind == ItemKind.Sheet ? "sheet"
                        : item.Kind == ItemKind.Book ? "book" : item.Kind == ItemKind.Plan ? "plan" : item.Kind == ItemKind.Media ? "media"
                        : item.Kind == ItemKind.MindMap ? "mindmap" : item.Kind == ItemKind.PageTemplate ? "template"
                        : item.Kind == ItemKind.Folder && item.RootCategory().CategoryKey == Project.KeySheets ? "library" : "corkboard";
                    if (shell.VisibleView != expected) wrong.Add(item.Title + " : " + shell.VisibleView + " au lieu de " + expected);
                }
                Check(wrong.Count == 0, "chaque élément ouvre sa vue" + (wrong.Count == 0 ? "" : " — " + string.Join(" ; ", wrong.ToArray())));
                Check(kinds.Contains(ItemKind.Sheet) && kinds.Contains(ItemKind.Book) && kinds.Contains(ItemKind.Plan)
                    && kinds.Contains(ItemKind.Folder) && kinds.Contains(ItemKind.Media) && kinds.Contains(ItemKind.Text),
                    "écrit, fiche, livre, dossier, plan et média s'affichent (" + kinds.Count + " natures)");
                shell.ShowJournalPublic();
                await Settle();
                Check(true, "le Journal perso s'ouvre");
                foreach (RightPanel panel in Enum.GetValues(typeof(RightPanel)))
                {
                    shell.SetRightPanelPublic(panel);
                    await Settle();
                }
                Check(true, "chaque panneau de droite s'ouvre (" + Enum.GetValues(typeof(RightPanel)).Length + ")");
                shell.SetRightPanelPublic(RightPanel.Inspector);
                if (chapter != null) shell.Binder.SelectItem(chapter.Id, true);
                await Settle();

                // — Le thème bascule et revient.
                var before = Chrome.Ink.Color;
                App.ApplyTheme(!Chrome.Dark);
                await Settle();
                Check(Chrome.Ink.Color != before, "basculer le thème recolore l'encre");
                App.ApplyTheme(!Chrome.Dark);
                await Settle();
                Check(Chrome.Ink.Color == before, "…et revient");

                // — Les Préférences : huit onglets, chacun un contenu.
                var prefs = new PreferencesDialog(shell, shell.Project);
                prefs.Show(shell);
                await Settle();
                Check(prefs.Tabs.Items.Count == 8, "les Préférences ont huit onglets (" + prefs.Tabs.Items.Count + ")");
                var titles = new List<string>();
                foreach (var item in prefs.Tabs.Items) titles.Add(((TabItem)item).Header as string);
                Check(titles.Contains("Raccourcis") && titles.Contains("DLC") && titles.Contains("Catalogue de polices"),
                    "…Raccourcis, DLC et Catalogue de polices en font partie");
                for (var i = 0; i < prefs.Tabs.Items.Count; i++)
                {
                    prefs.Tabs.SelectedIndex = i;
                    await Settle();
                }
                Check(prefs.Tabs.SelectedIndex == prefs.Tabs.Items.Count - 1, "chaque onglet s'ouvre sans erreur");
                prefs.Tabs.SelectedIndex = 0;
                await Settle();
                // Diagnostic de rendu : la graisse et la police que voit un
                // texte courant de l'onglet (le contenu ne doit pas hériter
                // de la graisse de la chip active).
                TextBlock sample = null;
                foreach (var text in prefs.GetVisualDescendants().OfType<TextBlock>())
                    if (text.Text != null && (text.Text ?? "").StartsWith("Boutons, sélections")) { sample = text; break; }
                Check(sample != null && sample.FontWeight == Avalonia.Media.FontWeight.Normal,
                    "un texte courant des Préférences reste en graisse normale (" + (sample == null ? "introuvable" : sample.FontWeight + ", " + sample.FontFamily.Name + " " + sample.FontSize) + ")");
                prefs.Close();
                await Settle();

                // — La boîte de message se montre et se ferme.
                var dialogTask = MessageDialog.Show(shell, "Sonde : cette boîte se ferme toute seule.", "Sonde", MessageButtons.OKCancel, MessageIcon.Information);
                await Settle();
                MessageDialog open = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is MessageDialog) open = (MessageDialog)window;
                Check(open != null && open.IsVisible, "la boîte de message est ouverte");
                if (open != null) open.Close();
                var result = await dialogTask;
                Check(result == MessageResult.Cancel, "fermée par la croix : le refus le plus sûr (Annuler)");

                // — Le dialogue de saisie : Entrée valide.
                var askTask = InputDialog.Ask(shell, "Sonde", "Un nom :", "Chapitre neuf");
                await Settle();
                InputDialog input = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is InputDialog) input = (InputDialog)window;
                Check(input != null, "le dialogue de saisie est ouvert");
                if (input != null) input.Close();
                var answer = await askTask;
                Check(answer == null, "fermé sans valider : null");

                // — Fermer le projet : l'Accueil se pose sur la coquille.
                shell.CloseProjectPublic();
                await Settle();
                Check(shell.Welcome != null && shell.Welcome.IsVisible, "projet fermé : l'accueil est posé sur la coquille");
                Check(shell.Welcome.Width >= 640 && shell.Welcome.Height >= 420, "l'accueil fait au moins 640×420 (" + shell.Welcome.Width.ToString("0") + "×" + shell.Welcome.Height.ToString("0") + ")");
            }
            catch (Exception error)
            {
                Failures++;
                Console.WriteLine("  ÉCHEC  exception : " + error);
            }
            Console.WriteLine(Failures == 0 ? "SONDE OK (" + _checks + " vérifications)" : "*** SONDE : " + Failures + " échec(s) sur " + _checks + " ***");
        }
    }
}

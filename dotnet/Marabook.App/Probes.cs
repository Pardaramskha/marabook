using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
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

        /// <summary>Le premier bouton visible dont le texte contient ce libellé.</summary>
        private static Button FindButton(Visual root, string label)
        {
            foreach (var button in root.GetVisualDescendants().OfType<Button>())
            {
                var text = button.Content as string;
                if (text == null)
                    foreach (var block in button.GetVisualDescendants().OfType<TextBlock>())
                        if (block.Text != null && block.Text.Contains(label)) return button;
                if (text != null && text.Contains(label)) return button;
            }
            return null;
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
                Check(composed != null && composed.Extent.Height > composed.Viewport.Height + 1,
                    "…et elle défile : la page dépasse la fenêtre (" + (composed == null ? "-" : composed.Extent.Height.ToString("0") + " > " + composed.Viewport.Height.ToString("0")) + ")");
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

                // — Glisser-déposer de la sélection (1.0.3) : les cinq premiers
                // caractères déplacés après le douzième, en une étape d'annulation ;
                // un dépôt dans la sélection ne fait rien.
                if (original.Length > 14)
                {
                    composed.PlaceCaret(0, 0, false);
                    composed.PlaceCaret(0, 5, true);
                    Check(!composed.MoveSelectionTo(0, 3), "déposer la sélection sur elle-même ne fait rien");
                    var moved = composed.MoveSelectionTo(0, 12);
                    await Settle();
                    var expected = original.Substring(5, 7) + original.Substring(0, 5) + original.Substring(12);
                    Check(moved && document.Paragraphs[0].ToPlainText() == expected, "glisser-déposer : la sélection se déplace après le douzième caractère");
                    Check(composed.Undo() && document.Paragraphs[0].ToPlainText() == original, "…et Ctrl+Z la ramène");
                    composed.PlaceCaret(0, 0, false);
                }

                // — Copier avec mise en forme, coller (28/09) : le gras voyage
                // par le presse-papiers ; sans mise en forme, il ne voyage pas.
                // Sous Xvfb (CI Ubuntu) le presse-papiers ne rend rien : la
                // vérification est sautée quand un texte nu ne fait pas
                // l'aller-retour (01/10).
                var clipboardWorks = false;
                try
                {
                    var top = TopLevel.GetTopLevel(composed);
                    if (top != null && top.Clipboard != null)
                    {
                        await top.Clipboard.SetTextAsync("sonde-presse-papiers");
                        await Settle();
                        clipboardWorks = await top.Clipboard.GetTextAsync() == "sonde-presse-papiers";
                    }
                }
                catch (Exception) { clipboardWorks = false; }
                if (!clipboardWorks) Console.WriteLine("  (presse-papiers indisponible ici : copier/coller sauté)");
                else try
                {
                    var paragraphs = document.Paragraphs.Count;
                    composed.PlaceCaret(0, 0, false);
                    composed.PlaceCaret(0, Math.Min(5, original.Length), true); // les cinq premiers caractères
                    composed.ToggleBold();
                    await Settle();
                    composed.PlaceCaret(0, 0, false);
                    composed.PlaceCaret(0, Math.Min(5, original.Length), true);
                    composed.Copy(true);
                    await Settle();
                    var end = document.Paragraphs[paragraphs - 1];
                    composed.PlaceCaret(paragraphs - 1, PivotEdit.FlatLength(end), false);
                    await composed.PasteAsync();
                    await Settle();
                    var pasted = end.Runs.Count > 0 ? end.Runs[end.Runs.Count - 1] : null;
                    Check(pasted != null && pasted.Bold == true && pasted.Text == original.Substring(0, Math.Min(5, original.Length)),
                        "copier avec mise en forme puis coller : le gras voyage (" + (pasted == null ? "rien collé" : pasted.Text + ", gras=" + pasted.Bold) + ")");
                    composed.Undo();
                    end = document.Paragraphs[paragraphs - 1]; // l'annulation a pu remplacer le paragraphe
                    composed.PlaceCaret(0, 0, false);
                    composed.PlaceCaret(0, Math.Min(5, original.Length), true);
                    composed.Copy(false);
                    await Settle();
                    composed.PlaceCaret(paragraphs - 1, PivotEdit.FlatLength(end), false);
                    await composed.PasteAsync();
                    await Settle();
                    pasted = end.Runs.Count > 0 ? end.Runs[end.Runs.Count - 1] : null;
                    Check(pasted != null && pasted.Bold != true && end.ToPlainText().EndsWith(original.Substring(0, Math.Min(5, original.Length))),
                        "copier sans mise en forme puis coller : le texte seul voyage");
                    composed.Undo(); // le collage
                    composed.Undo(); // le gras
                    composed.PlaceCaret(0, 0, false);
                }
                catch (Exception error) { Check(false, "copier/coller mis en forme : " + error.Message); }

                // — L'annulation par écrit (28/09) : A modifié, B modifié, A
                // modifié ; deux Ctrl+Z sur A rendent A, B reste tel quel ;
                // un rechargement (styles) ne vide pas la pile.
                try
                {
                    var chapterB = book.Children[1];
                    var originalA = document.Paragraphs[0].ToPlainText();
                    var originalB = chapterB.Document.Paragraphs[0].ToPlainText();
                    composed.PlaceCaret(0, 0, false);
                    composed.TypeText("Un ");
                    await Settle();
                    shell.Binder.SelectItem(chapterB.Id, true);
                    await Settle();
                    composed.PlaceCaret(0, 0, false);
                    composed.TypeText("Bé ");
                    await Settle();
                    shell.Binder.SelectItem(chapter.Id, true);
                    await Settle();
                    composed.PlaceCaret(0, PivotEdit.FlatLength(document.Paragraphs[0]), false);
                    composed.TypeText(" deux");
                    await Settle();
                    editor.Reload(); // la feuille de styles a « changé »
                    await Settle();
                    // Une correction automatique (b45) peut demander un Ctrl+Z
                    // de plus (le premier la refuse) : on recule jusqu'à l'état.
                    var stepsOne = 0;
                    while (stepsOne < 3 && document.Paragraphs[0].ToPlainText() != "Un " + originalA && composed.Undo()) stepsOne++;
                    var afterOne = document.Paragraphs[0].ToPlainText();
                    var stepsTwo = 0;
                    while (stepsTwo < 3 && document.Paragraphs[0].ToPlainText() != originalA && composed.Undo()) stepsTwo++;
                    var afterTwo = document.Paragraphs[0].ToPlainText();
                    Check(stepsOne > 0 && afterOne == "Un " + originalA && stepsTwo > 0 && afterTwo == originalA,
                        "annuler sur A rend « phrase 2 » puis « phrase 1 » (" + stepsOne + " + " + stepsTwo + " Ctrl+Z, après un rechargement)");
                    Check(chapterB.Document.Paragraphs[0].ToPlainText() == "Bé " + originalB, "…et B n'a pas bougé");
                    shell.Binder.SelectItem(chapterB.Id, true);
                    await Settle();
                    Check(composed.Undo() && chapterB.Document.Paragraphs[0].ToPlainText() == originalB, "Ctrl+Z sur B rend B");
                    shell.Binder.SelectItem(chapter.Id, true);
                    await Settle();
                    composed.PlaceCaret(0, 0, false);
                }
                catch (Exception error) { Check(false, "annulation par écrit : " + error.Message); }

                // — L'annulation d'un changement de STYLE (28/09) : la taille
                // des notes de bas de page change, Ctrl+Z la rend, Ctrl+Y la
                // remet ; entre les deux, le texte n'a pas bougé.
                try
                {
                    var note = shell.Project.Styles.FootnoteStyle();
                    var sizeBefore = note.FontSize;
                    var textBefore = document.Paragraphs[0].ToPlainText();
                    note.FontSize = sizeBefore + 4;
                    shell.StylesEditedInPlace();
                    await Settle();
                    Check(Math.Abs(shell.Project.Styles.FootnoteStyle().FontSize - (sizeBefore + 4)) < 0.01, "la taille des notes est changée dans la feuille");
                    var undone = composed.Undo();
                    await Settle();
                    Check(undone && Math.Abs(shell.Project.Styles.FootnoteStyle().FontSize - sizeBefore) < 0.01,
                        "Ctrl+Z rend la taille des notes d'avant (" + shell.Project.Styles.FootnoteStyle().FontSize + ")");
                    Check(document.Paragraphs[0].ToPlainText() == textBefore, "…sans toucher au texte");
                    var redone = composed.Redo();
                    await Settle();
                    Check(redone && Math.Abs(shell.Project.Styles.FootnoteStyle().FontSize - (sizeBefore + 4)) < 0.01, "Ctrl+Y remet le changement de style");
                    composed.Undo();
                    await Settle();
                    Check(Math.Abs(shell.Project.Styles.FootnoteStyle().FontSize - sizeBefore) < 0.01, "…et Ctrl+Z le rend encore");
                    composed.PlaceCaret(0, 0, false);
                }
                catch (Exception error) { Check(false, "annulation d'un style : " + error.Message); }

                // — La mise en forme d'une note depuis le ruban (28/09) : la
                // note ouverte passe en gras (toute la note sans sélection),
                // Ctrl+Z la rend ; le texte n'a pas bougé.
                try
                {
                    var noteIds = composed.MarkerOrder();
                    if (noteIds.Count == 0)
                    {
                        composed.PlaceCaret(0, 0, false);
                        composed.InsertFootnoteAtCaret();
                        await Settle();
                        noteIds = composed.MarkerOrder();
                    }
                    Check(noteIds.Count > 0, "l'écrit a une note de bas de page (" + noteIds.Count + ")");
                    var noteId = noteIds[0];
                    var textBefore = document.Paragraphs[0].ToPlainText();
                    composed.EditNote(noteId);
                    composed.TypeText("note sonde");
                    await Settle();
                    composed.ToggleBold();
                    await Settle();
                    var footnote = document.FindFootnote(noteId);
                    var allBold = footnote != null && footnote.Runs.Count > 0;
                    if (footnote != null) foreach (var run in footnote.Runs) if (!PivotEdit.IsElement(run) && run.Bold != true) allBold = false;
                    Check(allBold, "gras depuis le ruban : toute la note passe en gras (" + (footnote == null ? "note absente" : footnote.Text) + ")");
                    Check(document.Paragraphs[0].ToPlainText() == textBefore, "…sans toucher au texte");
                    composed.Undo();
                    await Settle();
                    footnote = document.FindFootnote(noteId);
                    var anyBold = false;
                    if (footnote != null) foreach (var run in footnote.Runs) if (run.Bold == true) anyBold = true;
                    Check(!anyBold, "Ctrl+Z rend la note sans gras");
                    composed.CloseNoteEditor(true);
                    composed.Undo(); composed.Undo(); // la frappe, puis la note insérée s'il a fallu l'insérer
                    composed.PlaceCaret(0, 0, false);
                }
                catch (Exception error) { Check(false, "mise en forme d'une note : " + error.Message); }

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

                // — Glisser-déposer de fichiers du système (29/09) : un .docx
                // et un .md lâchés sur le livre deviennent ses écrits ; un
                // fichier quelconque lâché dans Recherche devient un média.
                var dropDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-sonde-drop-" + Guid.NewGuid().ToString("N"));
                try
                {
                    System.IO.Directory.CreateDirectory(dropDir);
                    var docxPath = System.IO.Path.Combine(dropDir, "Chapitre déposé.docx");
                    var mdPath = System.IO.Path.Combine(dropDir, "Note déposée.md");
                    var binPath = System.IO.Path.Combine(dropDir, "Pièce déposée.dat");
                    Exchange.Docx.Export(document, shell.Project.Styles, docxPath, shell.Project.Page, null, shell.Project);
                    System.IO.File.WriteAllText(mdPath, "# Une note\n\nDéposée depuis l'Explorateur.");
                    System.IO.File.WriteAllBytes(binPath, new byte[] { 1, 2, 3, 4 });
                    var bookBefore = book.Children.Count;
                    shell.ImportDroppedFiles(book, new[] { docxPath, mdPath });
                    await Settle();
                    var droppedDocx = book.Children.FirstOrDefault(c => c.Title == "Chapitre déposé");
                    var droppedMd = book.Children.FirstOrDefault(c => c.Title == "Note déposée");
                    Check(book.Children.Count == bookBefore + 2 && droppedDocx != null && droppedMd != null
                        && droppedDocx.Kind == ItemKind.Text && droppedMd.Kind == ItemKind.Text,
                        "deux documents déposés sur le livre deviennent ses écrits (" + (book.Children.Count - bookBefore) + ")");
                    Check(droppedDocx != null && droppedDocx.Document.ToPlainText().Contains(document.Paragraphs[0].ToPlainText().Substring(0, 12)),
                        "…le .docx a gardé son texte");
                    Check(droppedMd != null && droppedMd.Document.ToPlainText().Contains("Déposée depuis"), "…le .md aussi");
                    var research = shell.Project.Category(Project.KeyResearch);
                    var researchBefore = research.Children.Count;
                    shell.ImportDroppedFiles(research, new[] { binPath });
                    await Settle();
                    var media = research.Children.FirstOrDefault(c => c.Title == "Pièce déposée");
                    Check(research.Children.Count == researchBefore + 1 && media != null && media.Kind == ItemKind.Media && media.MediaExtension == ".dat",
                        "un fichier déposé dans Recherche devient un média");
                }
                catch (Exception error) { Check(false, "glisser-déposer de fichiers : " + error.Message); }
                finally { try { System.IO.Directory.Delete(dropDir, true); } catch { } }

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
                // — Les onglets du livre, le mode wiki d'une fiche.
                if (book != null)
                {
                    shell.Binder.SelectItem(book.Id, true);
                    await Settle();
                    var tabsSeen = 0;
                    foreach (var tabs in shell.GetVisualDescendants().OfType<TabControl>())
                    {
                        if (!tabs.IsEffectivelyVisible) continue;
                        for (var i = 0; i < tabs.Items.Count; i++) { tabs.SelectedIndex = i; await Settle(); tabsSeen++; }
                        tabs.SelectedIndex = 0;
                    }
                    Check(shell.VisibleView == "book" && tabsSeen >= 5, "le livre ouvre ses onglets (" + tabsSeen + " onglets parcourus)");
                }
                BinderItem sheet = null;
                foreach (var item in shell.Project.AllItems()) if (item.Kind == ItemKind.Sheet) { sheet = item; break; }
                if (sheet != null)
                {
                    shell.Binder.SelectItem(sheet.Id, true);
                    await Settle();
                    if (shell.VisibleView != "sheet") { Console.WriteLine("  [sonde] vue après sélection de la fiche : " + shell.VisibleView + " ; fenêtres ouvertes : " + ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows.Count); }
                    var wiki = FindButton(shell, "Mode wiki");
                    if (wiki != null) { wiki.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Settle(); }
                    Check(shell.VisibleView == "sheet" && wiki != null, "la fiche passe en mode wiki et revient (" + shell.VisibleView + ", bouton " + (wiki == null ? "introuvable parmi " + shell.GetVisualDescendants().OfType<Button>().Count() + " boutons" : "trouvé") + ")");
                    var back = FindButton(shell, "Mode fiche") ?? FindButton(shell, "Mode wiki");
                    if (back != null) { back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); await Settle(); }
                }

                // — Un lien du Texte libre sur une ligne RENVOYÉE répond sous
                // la souris (TextLayout.HitTestPoint d'Avalonia le jugeait
                // « dehors » au-delà de la première ligne — 29/09).
                {
                    var source = "Texte assez long pour être renvoyé sur plusieurs lignes dans une colonne étroite, "
                        + "avec du **gras** et de l'*italique* avant le lien, et enfin [[Le marabout]]";
                    var body = MarkdownRender.Build(source, delegate { }, delegate { });
                    var lab = new Window { Width = 360, Height = 240, Content = new Border { Width = 320, Child = body } };
                    lab.Show();
                    await Settle();
                    TextBlock paragraph = null;
                    foreach (var block in lab.GetVisualDescendants().OfType<TextBlock>())
                        if (block.Tag != null) { paragraph = block; break; }
                    var lines = paragraph == null || paragraph.TextLayout == null ? 0 : paragraph.TextLayout.TextLines.Count;
                    var onLink = false; var offLink = true;
                    if (lines >= 2)
                    {
                        var last = paragraph.TextLayout.TextLines[lines - 1];
                        var y = paragraph.TextLayout.Height - last.Height / 2;
                        onLink = MarkdownRender.HasLinkAt(paragraph, new Point(last.Start + last.Width - 6, y));
                        var first = paragraph.TextLayout.TextLines[0];
                        offLink = MarkdownRender.HasLinkAt(paragraph, new Point(first.Start + 6, first.Height / 2));
                    }
                    Check(lines >= 2 && onLink && !offLink, "le lien d'une ligne renvoyée du Texte libre répond sous la souris (" + lines + " lignes, lien " + onLink + ", texte " + offLink + ")");
                    lab.Close();
                }

                // — Enregistrer puis rouvrir : le .plot fait l'aller-retour.
                var plotPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-sonde-p3.plot");
                try
                {
                    var count = shell.Project.AllItems().Count();
                    Persistence.PlotFile.Save(shell.Project, plotPath);
                    shell.OpenFile(plotPath);
                    await Settle();
                    await Settle();
                    Check(shell.HasProjectPath && shell.Project.AllItems().Count() == count && shell.Title.Contains("marabook-sonde-p3"),
                        "le projet enregistré se rouvre avec ses " + count + " éléments (" + shell.Title + ")");
                    // Fichier › Enregistrer sur le projet rouvert : le vrai
                    // chemin de la fenêtre (rinçage des vues, écriture,
                    // récents, inspecteur, toast) — celui qui tombait sur
                    // « Object reference not set » (27/09).
                    var doSave = typeof(MainWindow).GetMethod("DoSave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var stampBefore = System.IO.File.GetLastWriteTimeUtc(plotPath);
                    await Task.Delay(30);
                    doSave.Invoke(shell, null);
                    await Settle();
                    Check(shell.LastSaveError == null && System.IO.File.GetLastWriteTimeUtc(plotPath) > stampBefore,
                        "Fichier › Enregistrer réécrit le projet rouvert" + (shell.LastSaveError == null ? "" : " : " + shell.LastSaveError.Split('\n')[0]));
                }
                catch (Exception error) { Check(false, "aller-retour .plot : " + error.Message); }
                finally { try { System.IO.File.Delete(plotPath); } catch { } }
                book = null;
                foreach (var item in shell.Project.AllItems()) if (item.Kind == ItemKind.Book) { book = item; break; }
                chapter = book == null ? null : book.Children[0];
                if (chapter != null) shell.Binder.SelectItem(chapter.Id, true);
                await Settle();

                // — La colonne de droite ne s'ouvre que si elle a quelque chose
                //   à montrer (1.0.3) : plus de « Rien à montrer ici » sur une
                //   racine sans Général ; elle revient sur un écrit.
                var sheetsRoot = shell.Project.Category(Project.KeySheets);
                if (sheetsRoot != null && chapter != null)
                {
                    shell.Binder.SelectItem(sheetsRoot.Id, true);
                    await Settle();
                    Check(shell.RightColumnWidth < 1, "racine Fiches sans sélection : la colonne de droite est repliée (" + shell.RightColumnWidth.ToString("0") + ")");
                    shell.Binder.SelectItem(chapter.Id, true);
                    await Settle();
                    Check(shell.RightColumnWidth > 1, "…et revient sur un écrit (" + shell.RightColumnWidth.ToString("0") + ")");
                }

                // — La sélection multiple (1.0.3) : deux chapitres du livre
                //   choisis sur le tableau → le Général passe en mode lot ; la
                //   couleur s'applique aux deux ; UN Ctrl+Z la retire des deux.
                if (book != null && book.Children.Count >= 2 && chapter != null)
                {
                    shell.Binder.SelectItem(book.Id, true);
                    await Settle();
                    var board = shell.BookViewPublic.TextsBoard;
                    var first = book.Children[0];
                    var second = book.Children[1];
                    board.SelectForProbe(new[] { first.Id, second.Id });
                    await Settle();
                    Check(shell.InspectedGroupCount == 2 && shell.InspectorTitle.StartsWith("2 "), "deux cartes choisies : le Général passe en mode lot (" + shell.InspectorTitle + ")");
                    var colorFirst = first.CardColor;
                    var colorSecond = second.CardColor;
                    shell.BatchColorPublic("#AA3366");
                    await Settle();
                    Check(first.CardColor == "#AA3366" && second.CardColor == "#AA3366", "la couleur du lot s'applique aux deux cartes");
                    shell.UndoPublic();
                    await Settle();
                    Check(first.CardColor == colorFirst && second.CardColor == colorSecond, "…et UN Ctrl+Z la retire des deux");
                    board.ClearSelection();
                    await Settle();
                    Check(shell.InspectedGroupCount == 0, "plus de sélection : le Général revient à l'élément");
                    // La Pile : Ctrl+clic sur un second écrit forme un lot ; une
                    // fiche d'une autre famille le fait tomber.
                    shell.Binder.SelectItem(first.Id, true);
                    await Settle();
                    shell.Binder.ToggleMultiForProbe(second.Id);
                    await Settle();
                    Check(shell.Binder.MultiItems().Count == 2 && shell.InspectedGroupCount == 2, "Pile : Ctrl+clic sur un second écrit forme un lot de deux");
                    var anySheet = shell.Project.AllItems().FirstOrDefault(i => i.Kind == ItemKind.Sheet);
                    if (anySheet != null)
                    {
                        shell.Binder.ToggleMultiForProbe(anySheet.Id);
                        await Settle();
                        Check(shell.Binder.MultiItems().Count == 0 && shell.InspectedGroupCount == 0, "…une fiche hors de la famille fait tomber le lot");
                    }
                    shell.Binder.SelectItem(chapter.Id, true);
                    await Settle();
                }

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

                // — Le dialogue du dictionnaire (01/10) : un nom part au
                //   masculin ; un nom propre « Lieu » cache la flexion et
                //   propose le gentilé ; « Gentilé » rend la flexion.
                var lexiconTask = LexiconEntryDialog.AskForWord(shell, "mànis", true);
                await Settle();
                LexiconEntryDialog lexicon = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is LexiconEntryDialog) lexicon = (LexiconEntryDialog)window;
                Check(lexicon != null, "le dialogue du dictionnaire est ouvert");
                if (lexicon != null)
                {
                    Func<string, RadioButton> radio = delegate(string content)
                    {
                        foreach (var candidate in lexicon.GetVisualDescendants().OfType<RadioButton>())
                            if ((candidate.Content as string) == content) return candidate;
                        return null;
                    };
                    Func<string, CheckBox> checkBox = delegate(string prefix)
                    {
                        foreach (var candidate in lexicon.GetVisualDescendants().OfType<CheckBox>())
                            if ((candidate.Content as string ?? "").StartsWith(prefix)) return candidate;
                        return null;
                    };
                    var masculine = lexicon.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault(r => (r.Content as string) == "Masculin" && r.GroupName == "lexicon-genders");
                    var flexionLabel = lexicon.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Flexion :");
                    Check(masculine != null && masculine.IsChecked == true, "un nom neuf part au masculin");
                    var place = radio("Lieu");
                    Check(place != null && !place.IsEffectivelyVisible, "un nom : les natures du nom propre sont cachées");
                    // (02/10) Avalonia coche le nouveau bouton avant de décocher
                    // l'ancien : la nature doit suivre le type dès ce changement.
                    var proper = radio("Nom propre");
                    if (proper != null) proper.IsChecked = true;
                    await Settle();
                    Check(place != null && place.IsEffectivelyVisible, "« Nom propre » : ses natures apparaissent aussitôt");
                    if (place != null) place.IsChecked = true;
                    await Settle();
                    Check(flexionLabel != null && !flexionLabel.IsEffectivelyVisible, "nom propre « Lieu » : la flexion est masquée");
                    var demonym = checkBox("Dériver le gentilé");
                    Check(demonym != null && demonym.IsEffectivelyVisible, "…et le gentilé est proposé");
                    // (1.0.3) Un prénom se genre : neutre par défaut, Masculin / Féminin à côté.
                    var firstName = radio("Prénom");
                    if (firstName != null) firstName.IsChecked = true;
                    await Settle();
                    var neutral = radio("Neutre");
                    Check(neutral != null && neutral.IsEffectivelyVisible && neutral.IsEnabled && neutral.IsChecked == true, "« Prénom » : le genre est proposé, neutre par défaut");
                    var gentile = radio("Gentilé");
                    if (gentile != null) gentile.IsChecked = true;
                    await Settle();
                    Check(flexionLabel != null && flexionLabel.IsEffectivelyVisible, "« Gentilé » : la flexion revient");
                    Check(demonym != null && !demonym.IsEffectivelyVisible, "…et le gentilé dérivé disparaît");
                    lexicon.Close();
                }
                var lexiconAnswer = await lexiconTask;
                Check(lexiconAnswer == null, "fermé sans valider : null");

                // — Fermer le projet : l'Accueil se pose sur la coquille.
                shell.CloseProjectPublic();
                await Settle();
                Check(shell.Welcome != null && shell.Welcome.IsVisible, "projet fermé : l'accueil est posé sur la coquille");
                Check(shell.Welcome.Width >= 640 && shell.Welcome.Height >= 420, "l'accueil fait au moins 640×420 (" + shell.Welcome.Width.ToString("0") + "×" + shell.Welcome.Height.ToString("0") + ")");
                // — Le statut de mise à jour au pied (02/10) : en sonde, la
                //   vérification est sautée et le pied le dit ; l'ancien texte
                //   « la vérification arrive » n'existe plus.
                TextBlock foot = null;
                foreach (var block in shell.Welcome.GetVisualDescendants().OfType<TextBlock>())
                    if (block.Text != null && block.Text.StartsWith("Mise à jour")) foot = block;
                Check(foot != null && foot.Text.Contains("non vérifiée"), "l'accueil dit que la mise à jour n'est pas vérifiée en sonde (" + (foot == null ? "-" : foot.Text) + ")");

                // — La fenêtre des nouveautés (02/10) : le nom de la version,
                //   les patch notes rendues depuis le Markdown, le bouton
                //   d'installation ; « Plus tard » ferme sans installer.
                var installed = false;
                var info = new Updater.Info
                {
                    Version = "9.9.9",
                    PageUrl = Updater.RepositoryUrl + "/releases/tag/v9.9.9",
                    PublishedAt = "2026-10-02T09:12:33Z",
                    Notes = "## Fonctionnalités\n\n* Une **fenêtre** des nouveautés\n  * et sa sous-liste\n\n## Correctifs\n\n* Le pied de l'accueil"
                };
                var notesTask = UpdateNotesDialog.Show(shell.Welcome, info, "1.0.1", false, delegate { installed = true; });
                await Settle();
                UpdateNotesDialog notes = null;
                foreach (var window in ((Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current.ApplicationLifetime).Windows)
                    if (window is UpdateNotesDialog) notes = (UpdateNotesDialog)window;
                Check(notes != null && notes.IsVisible, "la fenêtre des nouveautés est ouverte");
                if (notes != null)
                {
                    var texts = notes.GetVisualDescendants().OfType<TextBlock>().Select(delegate(TextBlock b) { return b.Text ?? ""; }).ToList();
                    var inlineTexts = notes.GetVisualDescendants().OfType<TextBlock>()
                        .SelectMany(delegate(TextBlock b) { return b.Inlines == null ? new string[0] : b.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(delegate(Avalonia.Controls.Documents.Run r) { return r.Text ?? ""; }); }).ToList();
                    Check(texts.Any(delegate(string t) { return t == "Marabook 9.9.9"; }), "le nom de la version est en titre");
                    Check(texts.Any(delegate(string t) { return t.Contains("02/10/2026") && t.Contains("vous avez la 1.0.1"); }), "la date de publication et la version locale sont dites");
                    Check(inlineTexts.Any(delegate(string t) { return t.Contains("sous-liste"); }) && inlineTexts.Any(delegate(string t) { return t == "fenêtre"; }), "les patch notes sont rendues depuis le Markdown (liste imbriquée, gras)");
                    Check(FindButton(notes, "Installer") != null, "le bouton « Installer et redémarrer » est là");
                    var later = FindButton(notes, "Plus tard");
                    Check(later != null, "le bouton « Plus tard » est là");
                    if (later != null) later.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); else notes.Close();
                }
                var chosen = await notesTask;
                Check(!chosen && !installed, "« Plus tard » ferme sans installer");
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

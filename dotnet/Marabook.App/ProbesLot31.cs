using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Sonde du PATCH DU 25/11/2026 (2.0.0, lot 31) — les correctifs GÉNÉRAUX (Journal perso
    /// en fenêtre, ligne de sous-catégorie de la Pile, « Curation », sélecteur de couleur, sous-catégorie
    /// et partie de livre au Général du rail, marqueur de veuve, bilan de style, infobulle du verrou,
    /// interrupteurs des Préférences, chronomètre du sprint à l'Accueil), puis, sous chacun des
    /// quatre skins PREMIUM : l'infobulle à l'accent, la pastille d'onglet qui glisse, le dégradé
    /// fixe des barres, la lueur d'« Ascension », le losange du pied des menus et des listes, les
    /// puces des listes. MARABOOK_PROBE_ONLY=lot31 la joue seule ; MARABOOK_PROBE_SHOTS garde les images.</summary>
    public static partial class Probes
    {
        private static async Task ProbeLot31(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- lot 31 : correctifs généraux, correctifs des skins premium (2.0.0, 25/11)");
            string skinBefore = AppSettings.Skin, accentBefore = AppSettings.AccentColor;
            bool darkBefore = AppSettings.DarkTheme, animationsBefore = AppSettings.Animations, lockedBefore = AppSettings.RailLocked, outlinerBefore = AppSettings.OutlinerShown;
            var rightBefore = AppSettings.RightPanel;
            var added = new List<BinderItem>();
            var addedCategories = new List<SheetCategory>();
            var collapsedBefore = new HashSet<string>(SheetLibraryView.CollapsedCategories);
            try
            {
                AppSettings.RailLocked = false;
                AppSettings.OutlinerShown = false;
                AppSettings.Animations = true;
                await ProbeLot31General(shell, project, added, addedCategories);
                foreach (var id in new[] { Skins.MuseumAscent, Skins.MuseumFire, Skins.MuseumWood, Skins.MuseumOath })
                {
                    AppSettings.Skin = id;
                    AppSettings.AccentColor = null;
                    AppSettings.DarkTheme = Skins.DarkByDefault(id);
                    shell.ApplyAppearanceForProbe();
                    for (var i = 0; i < 4; i++) await Settle();
                    await ProbeLot31Skin(shell, project, id);
                }
            }
            finally
            {
                foreach (var item in added) if (item.Parent != null) item.Parent.Children.Remove(item);
                foreach (var category in addedCategories) project.SheetCategories.Remove(category);
                SheetLibraryView.CollapsedCategories.Clear();
                foreach (var id in collapsedBefore) SheetLibraryView.CollapsedCategories.Add(id);
                shell.CloseSprintForProbe();
                shell.CloseJournalForProbe();
                AppSettings.Skin = skinBefore;
                AppSettings.AccentColor = accentBefore;
                AppSettings.DarkTheme = darkBefore;
                AppSettings.Animations = animationsBefore;
                AppSettings.RailLocked = lockedBefore;
                AppSettings.OutlinerShown = outlinerBefore;
                shell.ApplyAppearanceForProbe();
                shell.Binder.Rebuild();
                var home = project.Category(Project.KeyHome);
                if (home != null) shell.Binder.SelectItem(home.Id);
                shell.SetRightPanelForProbe(rightBefore);
                await Settle();
            }
        }

        // ================================================================ les correctifs généraux

        private static async Task ProbeLot31General(MainWindow shell, Project project, List<BinderItem> added, List<SheetCategory> addedCategories)
        {
            var home = project.Category(Project.KeyHome);
            var writings = project.Category(Project.KeyWritings);
            var sheets = project.Category(Project.KeySheets);

            // ------------------------------------------------ le Journal perso, en fenêtre
            if (home != null) shell.Binder.SelectItem(home.Id);
            for (var i = 0; i < 3; i++) await Settle();
            var viewBefore = shell.VisibleView;
            var currentBefore = shell.CurrentForProbe;
            shell.ShowJournalPublic();
            for (var i = 0; i < 3; i++) await Settle();
            var journal = shell.JournalWindowForProbe;
            Check(shell.JournalWindowShownForProbe && journal is JournalDialog && journal.Title == "Journal perso", "le Journal perso s'ouvre dans une fenêtre, comme les Préférences");
            Check(shell.VisibleView == viewBefore && shell.CurrentForProbe == currentBefore, "…par-dessus l'application : la vue du centre et l'élément ouvert ne bougent pas (" + viewBefore + ")");
            Check(journal != null && ModalChrome.DressedForProbe(journal) && !ModalChrome.Grand(journal) && ModalChrome.Dimmed(shell), "…une fenêtre simple (barre de titre, croix), l'application voilée derrière");
            Check(journal != null && journal.GetVisualDescendants().OfType<JournalView>().Any() && journal.GetVisualDescendants().OfType<Control>().Any(delegate(Control control) { return "modal-close".Equals(control.Tag); }), "…elle porte la vue du Journal, et une icône pour fermer");
            // Une fenêtre voilée ne décide plus des infobulles (26/11) : sinon elle annulait celles de son dialogue.
            Check(!ToolTip.GetServiceEnabled(shell) && (journal == null || ToolTip.GetServiceEnabled(journal)), "…sous le voile, la fenêtre principale ne décide plus des infobulles (elle annulait celles du dialogue) ; le dialogue garde les siennes");
            if (journal != null) Shot(journal, "lot31-journal.png");
            // Échap : la touche arrive à la fenêtre, qui se ferme (ModalChrome).
            if (journal != null) journal.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape, Source = journal });
            for (var i = 0; i < 3; i++) await Settle();
            Check(!shell.JournalWindowShownForProbe && !ModalChrome.Dimmed(shell), "Échap la referme ; le voile se lève");
            Check(ToolTip.GetServiceEnabled(shell), "…et la fenêtre principale retrouve ses infobulles");
            shell.ShowJournalPublic();
            for (var i = 0; i < 2; i++) await Settle();
            Check(shell.JournalWindowShownForProbe && shell.JournalWindowForProbe.GetVisualDescendants().OfType<JournalView>().Any(), "elle se rouvre, avec sa vue (rendue à la fermeture)");
            shell.CloseJournalForProbe();
            for (var i = 0; i < 2; i++) await Settle();
            Check(!shell.JournalWindowShownForProbe, "…et se referme");

            // ------------------------------------------------ le chronomètre du sprint à l'Accueil
            if (home != null)
            {
                var homeView = shell.HomeForProbe;
                shell.BeginSprintForProbe(25, 500);
                for (var i = 0; i < 2; i++) await Settle();
                var first = homeView.SprintClockForProbe;
                Check(homeView.SprintCardShownForProbe && first != null && first.StartsWith("2", StringComparison.Ordinal) && first.Contains(":"), "un sprint lancé depuis l'Accueil : la carte « Sprint en cours » montre le temps restant (" + first + ")");
                shell.AgeSprintForProbe(7);
                shell.TickSprintForProbe();
                await Settle();
                var second = homeView.SprintClockForProbe;
                Check(second != null && second != first && second.StartsWith("24:5", StringComparison.Ordinal), "…et le chronomètre DÉCOMPTE à chaque battement, sans rebâtir l'Accueil (" + first + " → " + second + ")");
                shell.CloseSprintForProbe();
                await Settle();
                Check(!homeView.SprintCardShownForProbe, "le sprint refermé, la carte s'en va");
            }

            // ------------------------------------------------ « Curation » sur la ligne du haut
            BinderItem part = null;
            if (writings != null)
            {
                var book = new BinderItem { Kind = ItemKind.Book, Title = "Livre du lot 31 (sonde)" };
                book.Parent = writings;
                writings.Children.Add(book);
                added.Add(book);
                var chapter = SceneText("Chapitre du lot 31", "Un paragraphe.", "***", "Une seconde scène.");
                chapter.Parent = book;
                book.Children.Add(chapter);
                part = new BinderItem { Kind = ItemKind.Folder, Title = "Première partie (lot 31)" };
                part.Parent = book;
                book.Children.Add(part);
                var inside = SceneText("Chapitre dans la partie", "Un autre paragraphe.");
                inside.Parent = part;
                part.Children.Add(inside);
                shell.Binder.Rebuild();
                shell.Binder.SelectItem(book.Id);
                for (var i = 0; i < 3; i++) await Settle();
                var texts = shell.BookViewForProbe.TextsForProbe;
                var toggle = texts.OutlineToggleForProbe;
                var label = toggle.GetVisualDescendants().OfType<TextBlock>().Select(delegate(TextBlock block) { return block.Text; }).FirstOrDefault();
                Check(toggle.IsVisible && label == "Curation", "le bouton « Tableau » des écrits s'appelle « Curation »");
                // La barre de commande d'un livre (26/11) : UN bouton « Nouveau ▾ », qui déroule ses cinq créations.
                var newMenus = texts.GetVisualDescendants().OfType<Button>().Count(delegate(Button button) { return button.IsVisible && "new-menu".Equals(button.Tag); });
                var created = texts.BookNewLabelsForProbe;
                Check(newMenus == 1 && created.Count == 5 && created[0] == "Écrit" && created.Contains("Partie") && created.Contains("Liminaire…") && created.Contains("Annexe…"),
                    "la barre d'un livre n'a plus qu'un bouton « Nouveau ▾ » : " + string.Join(", ", created.ToArray()));
                var newText = texts.GetVisualDescendants().OfType<Button>().FirstOrDefault(delegate(Button button) { return button.IsVisible && "new-menu".Equals(button.Tag); }); // « Nouveau ▾ » (26/11)
                var toggleAt = toggle.TranslatePoint(new Point(0, 0), texts);
                var newAt = newText == null ? null : newText.TranslatePoint(new Point(0, 0), texts);
                // Le bandeau de la vue (26/11 soir, Rémi) : icône, titre, et toutes les actions à droite — « Curation » puis « Nouveau ▾ », contre le bord.
                var banner = texts.BannerForProbe;
                Check(banner != null && banner.IsVisible && banner.Title == book.Title && toggle.GetVisualAncestors().Contains(banner) && newText != null && newText.GetVisualAncestors().Contains(banner)
                    && toggleAt.HasValue && newAt.HasValue && Math.Abs(toggleAt.Value.Y - newAt.Value.Y) < 6 && newAt.Value.X > toggleAt.Value.X
                    && newAt.Value.X + newText.Bounds.Width > texts.Bounds.Width - 30,
                    "…il est dans le bandeau du tableau (« " + (banner == null ? "" : banner.Title) + " »), à droite avec « Nouveau ▾ » (y " + (toggleAt.HasValue ? toggleAt.Value.Y.ToString("0") : "-") + " / " + (newAt.HasValue ? newAt.Value.Y.ToString("0") : "-") + ")");
                Shot(shell, "lot31-curation.png");

                // ------------------------------------------------ une partie de livre au Général du rail
                shell.SetRightPanelForProbe(RightPanel.None);
                await Settle();
                texts.SelectPartForProbe(part);
                for (var i = 0; i < 3; i++) await Settle();
                Check(texts.SelectedPartIdForProbe == part.Id && texts.PartBoxForProbe(part.Id) != null && texts.PartBoxForProbe(part.Id).BorderThickness.Left > 2, "une partie de livre cliquée (son nom, son fond) est choisie : sa boîte s'épaissit");
                Check(AppSettings.RightPanel == RightPanel.Inspector && shell.GroupSectionShownForProbe && shell.GroupNameForProbe == part.Title && shell.InspectorKind.StartsWith("Partie de", StringComparison.Ordinal),
                    "…le Général du rail s'ouvre et montre son nom (« " + shell.GroupNameForProbe + " », " + shell.InspectorKind + ")");
                var options = shell.GroupOptionsForProbe;
                Check(options.Contains("Ouvrir la partie") && options.Contains("Nouvel écrit dans la partie") && options.Contains("Supprimer la partie…") && !shell.GroupColorShownForProbe, "…et ses options : " + string.Join(", ", options.ToArray()) + " (sa couleur : celle du dossier, au-dessus)");
                shell.RenameGroupForProbe("Partie renommée (lot 31)");
                for (var i = 0; i < 3; i++) await Settle();
                Check(part.Title == "Partie renommée (lot 31)" && shell.GroupNameForProbe == part.Title && texts.SelectedPartIdForProbe == part.Id, "son nom se change depuis le rail (annulable) ; elle reste choisie");
                Shot(shell, "lot31-partie-au-rail.png");
                shell.SelectCardsForProbe(new[] { chapter.Id });
                for (var i = 0; i < 3; i++) await Settle();
                Check(texts.SelectedPartIdForProbe == null && !shell.GroupSectionShownForProbe, "un écrit cliqué la remplace : le Général montre l'écrit");
                texts.SelectPartForProbe(part);
                await Settle();
                texts.ClearSelection();
                shell.SelectCardsForProbe(new string[0]);
                for (var i = 0; i < 2; i++) await Settle();
                Check(texts.SelectedPartIdForProbe == null && !shell.GroupSectionShownForProbe, "…un clic ailleurs aussi");
            }

            // ------------------------------------------------ une sous-catégorie de fiches au Général du rail
            var top = project.OrderedCategories().FirstOrDefault(delegate(SheetCategory category) { return !category.IsSub; });
            if (sheets != null && top != null)
            {
                var sub = project.AddSubCategory(top, "Héros du lot 31");
                addedCategories.Add(sub);
                var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche du lot 31", CategoryId = sub.Id, TemplateId = project.BaseTemplateIdOf(sub) };
                sheet.Parent = sheets;
                sheets.Children.Add(sheet);
                added.Add(sheet);
                shell.Binder.Rebuild();
                shell.Binder.SelectItem(sheets.Id);
                for (var i = 0; i < 3; i++) await Settle();
                // ------------------------------------------------ la Pile : une ligne de sous-catégorie ne se choisit pas
                var row = shell.Binder.CategoryRowForProbe(null);
                if (row != null)
                {
                    var before = shell.CurrentForProbe;
                    var view = shell.VisibleView;
                    shell.Binder.SelectRowForProbe(row);
                    for (var i = 0; i < 3; i++) await Settle();
                    Check(shell.CurrentForProbe == before && shell.VisibleView == view && !row.IsSelected, "Pile : la ligne d'une sous-catégorie de fiches, choisie par mégarde, ne vide plus le centre (" + view + " reste)");
                    Check(BinderView.CategoryRowMark.Equals(row.Tag) && !row.Focusable, "…elle est marquée : ni un élément, ni rien à ouvrir — le clic la replie");
                }
                else Console.WriteLine("  (aucune ligne de sous-catégorie dans la Pile du projet d'essai)");
                var library = shell.SheetLibraryForProbe;
                library.Refresh();
                for (var i = 0; i < 2; i++) await Settle();
                shell.SetRightPanelForProbe(RightPanel.None);
                await Settle();
                Check(library.IsVisible && library.SubBoxForProbe(sub.Id) != null, "la sous-catégorie a sa boîte au tableau des Fiches");
                library.SelectCategory(sub);
                for (var i = 0; i < 3; i++) await Settle();
                Check(shell.InspectedCategoryForProbe == sub && AppSettings.RightPanel == RightPanel.Inspector && shell.GroupSectionShownForProbe && shell.GroupNameForProbe == sub.Name,
                    "une sous-catégorie cliquée ouvre le Général du rail : son nom (« " + shell.GroupNameForProbe + " », " + shell.InspectorKind + ") [" + (shell.InspectedCategoryForProbe == sub) + " " + AppSettings.RightPanel + " " + shell.GroupSectionShownForProbe + "]");
                var options = shell.GroupOptionsForProbe;
                Check(shell.GroupColorShownForProbe && options.Contains("Nouvelle fiche ici") && options.Contains("Champs propres…") && options.Contains("Replier") && options.Contains("Supprimer la sous-catégorie"),
                    "…sa couleur, et ses options : " + string.Join(", ", options.ToArray()));
                Check(library.SubBoxForProbe(sub.Id).BorderThickness.Left > 2, "…sa boîte s'épaissit");
                shell.RenameGroupForProbe("Héros renommés (lot 31)");
                for (var i = 0; i < 3; i++) await Settle();
                Check(sub.Name == "Héros renommés (lot 31)" && shell.InspectedCategoryForProbe == sub && shell.GroupNameForProbe == sub.Name, "son nom se change depuis le rail ; elle reste montrée");
                library.SetCategoryColor(sub, "#C0392B");
                for (var i = 0; i < 2; i++) await Settle();
                Check(sub.Color == "#C0392B" && shell.InspectedCategoryForProbe == sub, "sa couleur aussi");
                library.ToggleCategoryCollapsed(sub);
                shell.RefreshInspectorForProbe();
                for (var i = 0; i < 2; i++) await Settle();
                Check(library.SubBoxCollapsedForProbe(sub.Id) && shell.InspectedCategoryForProbe == sub && shell.GroupOptionsForProbe.Contains("Déplier"), "repliée, elle reste montrée — l'option devient « Déplier »");
                Shot(shell, "lot31-sous-categorie-au-rail.png");
                library.ToggleCategoryCollapsed(sub);
                for (var i = 0; i < 2; i++) await Settle();
                shell.SelectCardsForProbe(new[] { sheet.Id });
                for (var i = 0; i < 3; i++) await Settle();
                Check(shell.InspectedCategoryForProbe == null && library.SelectedCategory == null && !shell.GroupSectionShownForProbe, "une fiche cliquée la remplace : le Général montre la fiche [" + (shell.InspectedCategoryForProbe == null) + " " + (library.SelectedCategory == null) + " " + shell.GroupSectionShownForProbe + "]");
                library.SelectCategory(sub);
                await Settle();
                library.SelectCategory(null);
                for (var i = 0; i < 2; i++) await Settle();
                Check(shell.InspectedCategoryForProbe == null && !shell.GroupSectionShownForProbe, "…un clic ailleurs aussi");
            }

            // ------------------------------------------------ le sélecteur de couleur personnalisée
            var picker = ColorDialog.NewForProbe(shell, "#5B67D8");
            picker.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
            Check(picker.HexForProbe == "#5B67D8" && picker.CodeForProbe == "#5B67D8" && picker.GetVisualDescendants().OfType<ChromaSquare>().Any() && picker.GetVisualDescendants().OfType<HueStrip>().Any(),
                "le sélecteur de couleur : un carré chromatique, une bande de teinte, un champ de code");
            picker.SetModeForProbe(0);
            await Settle();
            Check(picker.ShownSlidersForProbe == 3 && picker.LabelsForProbe == "RVB" && picker.SliderForProbe(0) == 0x5B && picker.FieldForProbe(2) == "216", "curseurs RVB : trois, à la valeur de la couleur, chacun avec son champ (" + picker.FieldForProbe(0) + ", " + picker.FieldForProbe(1) + ", " + picker.FieldForProbe(2) + ")");
            picker.TypeCodeForProbe("rgb(201, 162, 39)");
            await Settle();
            Check(picker.HexForProbe == "#C9A227" && picker.SliderForProbe(0) == 201 && picker.PreviewForProbe == Color.FromRgb(201, 162, 39), "un code RVB tapé (« rgb(201, 162, 39) ») règle la couleur, les curseurs et l'aperçu suivent");
            picker.TypeCodeForProbe("27AE60");
            await Settle();
            Check(picker.HexForProbe == "#27AE60", "un code HEX sans croisillon aussi");
            picker.TypeCodeForProbe("#27AE6");
            await Settle();
            Check(picker.HexForProbe == "#27AE60", "un code incomplet ne change rien (on tape encore)");
            picker.SetModeForProbe(1);
            await Settle();
            Check(picker.ShownSlidersForProbe == 4 && picker.LabelsForProbe == "CMJN" && picker.HexForProbe == "#27AE60", "la liste passe en CMJN : quatre curseurs, la couleur ne bouge pas [" + picker.ShownSlidersForProbe + " " + picker.LabelsForProbe + " " + picker.HexForProbe + "]");
            picker.TypeCodeForProbe("#000000");
            await Settle();
            picker.SetSliderForProbe(3, 0);
            picker.SetSliderForProbe(0, 100);
            await Settle();
            Check(picker.HexForProbe == "#00FFFF" && picker.SliderForProbe(0) == 100 && picker.SliderForProbe(3) == 0, "100 % de cyan, 0 % de noir : le cyan — et les curseurs que l'on tient ne sautent pas");
            picker.SetModeForProbe(2);
            await Settle();
            Check(picker.ShownSlidersForProbe == 3 && picker.LabelsForProbe == "TSL" && Math.Abs(picker.SliderForProbe(0) - 180) < 1.1, "…en HSL : teinte, saturation, luminosité (teinte " + picker.SliderForProbe(0).ToString("0") + "°)");
            picker.TypeFieldForProbe(0, "0");
            picker.TypeFieldForProbe(1, "100");
            picker.TypeFieldForProbe(2, "50");
            await Settle();
            Check(picker.HexForProbe == "#FF0000", "des valeurs tapées à la main dans les champs des curseurs : 0°, 100 %, 50 % = le rouge");
            picker.TypeFieldForProbe(0, "999");
            await Settle();
            Check(picker.HexForProbe == "#FF0000", "une valeur hors des bornes est ignorée");
            picker.PickHueForProbe(240);
            picker.PickSquareForProbe(1, 1);
            await Settle();
            Check(picker.HexForProbe == "#0000FF" && picker.CodeForProbe == "#0000FF", "le carré et la bande de teinte règlent la même couleur (coin haut droit, 240° : le bleu)");
            picker.PickSquareForProbe(0, 0);
            picker.PickSquareForProbe(1, 1);
            await Settle();
            Check(picker.HexForProbe == "#0000FF", "passer par le noir ne perd pas la teinte que l'on réglait");
            Shot(picker, "lot31-couleur.png");
            picker.Close();
            for (var i = 0; i < 2; i++) await Settle();

            // ------------------------------------------------ petits correctifs
            Check(ComposedView.WidowMarkTip == "Marqueur de veuve ou d'orpheline corrigée. Cliquez pour annuler et inviter le mal chez vous.", "le marqueur de veuve ou d'orpheline a son infobulle (et la main au survol)");
            var railLock = shell.RailLockForProbe;
            Check(railLock != null && ToolTip.GetPlacement(railLock) == PlacementMode.Left, "l'infobulle du verrou du rail se pose à gauche de l'icône");
            var report = new StyleReportPanel();
            var host = new Window { Width = 320, Height = 400, Content = report };
            host.Show(shell);
            for (var i = 0; i < 2; i++) await Settle();
            Check(!report.GetVisualDescendants().OfType<Button>().Any(delegate(Button button) { return (ToolTip.GetTip(button) as string ?? "").Contains("presse-papiers"); }), "le bilan de style n'a plus de « Copier le bilan dans le presse-papiers »");
            host.Close();
            await Settle();
            // L'infobulle posée à gauche : la flèche de côté.
            if (railLock != null)
            {
                ToolTip.SetIsOpen(railLock, true);
                for (var i = 0; i < 4; i++) await Settle();
                var tip = TipOf(railLock);
                var side = tip == null ? null : tip.GetVisualDescendants().OfType<Path>().FirstOrDefault(delegate(Path path) { return path.Name == "ArrowSide"; });
                var up = tip == null ? null : tip.GetVisualDescendants().OfType<Path>().FirstOrDefault(delegate(Path path) { return path.Name == "Arrow"; });
                Check(tip != null && side != null && side.IsVisible && up != null && !up.IsVisible, "…sa flèche pointe vers la droite, sur l'icône (plus de flèche en l'air)");
                if (tip != null) Shot(tip, "lot31-infobulle-verrou.png");
                ToolTip.SetIsOpen(railLock, false);
                await Settle();
            }

            // ------------------------------------------------ les interrupteurs des Préférences
            var prefs = new PreferencesDialog(shell, project);
            prefs.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
            int boxes = 0, switches = 0;
            CheckBox sample = null;
            foreach (var entry in prefs.Tabs.Items)
            {
                prefs.Tabs.SelectedItem = entry;
                for (var i = 0; i < 2; i++) await Settle();
                foreach (var box in prefs.GetVisualDescendants().OfType<CheckBox>())
                {
                    if (!box.IsVisible) continue;
                    boxes++;
                    var parts = box.GetVisualDescendants().OfType<Border>().ToList();
                    if (parts.Any(delegate(Border border) { return border.Name == "Track"; }) && parts.Any(delegate(Border border) { return border.Name == "Knob"; }) && !parts.Any(delegate(Border border) { return border.Name == "Box"; }))
                    {
                        switches++;
                        if (sample == null && box.IsEffectivelyEnabled) sample = box;
                    }
                }
                if (sample != null && sample.IsAttachedToVisualTree() && boxes == switches && boxes > 0 && !_lot31SwitchShot) { _lot31SwitchShot = true; Shot(prefs, "lot31-interrupteurs.png"); }
            }
            Check(boxes > 8 && switches == boxes, "Préférences : toutes les cases à cocher sont des interrupteurs (" + switches + " sur " + boxes + ", onglet après onglet)");
            prefs.Close();
            for (var i = 0; i < 2; i++) await Settle();
            // Un interrupteur seul, dans des Préférences neuves : le bouton glisse quand la case change.
            prefs = new PreferencesDialog(shell, project);
            prefs.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
            foreach (var entry in prefs.Tabs.Items)
            {
                prefs.Tabs.SelectedItem = entry;
                for (var i = 0; i < 2; i++) await Settle();
                var box = prefs.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(delegate(CheckBox candidate) { return candidate.IsVisible && candidate.IsEffectivelyEnabled; });
                if (box == null) continue;
                var knob = box.GetVisualDescendants().OfType<Border>().First(delegate(Border border) { return border.Name == "Knob"; });
                var track = box.GetVisualDescendants().OfType<Border>().First(delegate(Border border) { return border.Name == "Track"; });
                var was = box.IsChecked == true;
                var whereBefore = KnobShift(knob);
                box.IsChecked = !was;
                await Task.Delay(260);
                await Settle();
                var whereAfter = KnobShift(knob);
                Check(Math.Abs(Math.Abs(whereAfter - whereBefore) - Theme.SwitchTravel) < 0.6 && (whereAfter > whereBefore) == !was && knob.Transitions != null && knob.Transitions.Count > 0,
                    "un interrupteur basculé : son bouton glisse d'un bord à l'autre (" + whereBefore.ToString("0.#") + " → " + whereAfter.ToString("0.#") + " px), animé");
                Check(track.Background is ISolidColorBrush && ((ISolidColorBrush)track.Background).Color == (!was ? Chrome.Accent.Color : Chrome.FieldBg.Color), "…et sa piste prend l'accent quand il est allumé");
                box.IsChecked = was;
                await Task.Delay(220);
                break;
            }
            prefs.Close();
            for (var i = 0; i < 2; i++) await Settle();
        }

        private static bool _lot31SwitchShot;

        private static double KnobShift(Border knob)
        {
            var transform = knob.RenderTransform;
            return transform == null ? 0 : transform.Value.M31;
        }

        /// <summary>L'infobulle ouverte d'un contrôle (la bulle elle-même, dans sa fenêtre).</summary>
        private static ToolTip TipOf(Control control)
        {
            var field = typeof(ToolTip).GetField("ToolTipProperty", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var property = field == null ? null : field.GetValue(null) as AvaloniaProperty;
            return property == null ? null : control.GetValue(property) as ToolTip;
        }

        // ================================================================ les skins premium

        private static async Task ProbeLot31Skin(MainWindow shell, Project project, string id)
        {
            var name = "« " + Skins.Label(id) + " »";
            var ascent = id == Skins.MuseumAscent;
            var pill = Skins.MuseumPill(id);
            var accent = Chrome.Accent.Color;
            Console.WriteLine("  — " + name);

            // — L'infobulle : l'accent du skin, l'encre qui s'y lit, une ombre.
            var ink = Skin.TipInk(accent);
            Check(Chrome.ContrastRatio(ink, accent) >= 4.5, name + " : l'infobulle prend l'accent (" + Chrome.Hex(accent) + "), son texte l'encre qui s'y lit (" + Chrome.Hex(ink) + ", " + Chrome.ContrastRatio(ink, accent).ToString("0.0") + ":1)");
            var railLock = shell.RailLockForProbe;
            if (railLock != null)
            {
                ToolTip.SetIsOpen(railLock, true);
                for (var i = 0; i < 4; i++) await Settle();
                var tip = TipOf(railLock);
                var bubble = tip == null ? null : tip.GetVisualDescendants().OfType<Border>().FirstOrDefault(delegate(Border border) { return border.Name == "Pill"; });
                var ground = bubble == null ? null : bubble.Background as ISolidColorBrush;
                var text = tip == null ? null : tip.Foreground as ISolidColorBrush;
                if (AscensionArt.On)
                {
                    // « Ascension », lot 2 (25/11 au soir) : plus d'aplat d'accent — une plaque bleu nuit à double filet.
                    Check(bubble != null && bubble.Background is LinearGradientBrush && "tip-plate".Equals(bubble.Tag) && text != null && Chrome.Hex(text.Color) == "#F0E6D2"
                        && tip.GetVisualDescendants().OfType<TipOrnaments>().Any(), "…la bulle ouverte est une plaque bleu nuit (plus un aplat d'accent), arête dorée et losanges aux angles, encre " + (text == null ? "-" : Chrome.Hex(text.Color)));
                    Check(bubble != null && bubble.BoxShadow.Count == 3 && bubble.BoxShadow[0].IsInset && !bubble.BoxShadow[2].IsInset && bubble.BoxShadow[2].Blur > 3, "…un second filet en dedans, et une ombre portée dessous");
                }
                else
                {
                Check(ground != null && ground.Color == accent && text != null && text.Color == ink, "…la bulle ouverte est bien à l'accent, encre " + (text == null ? "-" : Chrome.Hex(text.Color)));
                Check(bubble != null && bubble.BoxShadow.Count == 1 && bubble.BoxShadow[0].Blur > 3 && bubble.BoxShadow[0].OffsetY > 0, "…avec une légère ombre portée dessous");
                }
                if (tip != null) Shot(tip, "lot31-" + id + "-infobulle.png");
                ToolTip.SetIsOpen(railLock, false);
                await Settle();
            }

            // — Les barres : un dégradé FIXE, tendu sur la piste.
            var narrow = Skin.MeterBrush(accent, 300) as LinearGradientBrush;
            Check(narrow != null && narrow.StartPoint.Unit == RelativeUnit.Absolute && Math.Abs(narrow.EndPoint.Point.X - 300) < 0.01 && narrow.GradientStops.Count == (AscensionArt.On ? 2 : 3),
                "…les barres de progression : un dégradé en unités absolues, tendu sur la piste (300 px) — il ne se serre ni ne s'étire avec la part remplie");
            double h0, s0, l0, h1, s1, l1;
            Skins.ToHsl(Chrome.Hex(Skin.MeterFrom(accent)), out h0, out s0, out l0);
            Skins.ToHsl(Chrome.Hex(Skin.MeterTo(accent)), out h1, out s1, out l1);
            Check(s0 < s1 - 0.15 && l0 < l1 - 0.10, "…du désaturé sombre (" + Chrome.Hex(Skin.MeterFrom(accent)) + ") au vif saturé (" + Chrome.Hex(Skin.MeterTo(accent)) + ")");
            var bar = new BookProgressBar { Width = 260 };
            bar.ShowRatio("Essai", 0.8, 0.3);
            var meterHost = new Window { Width = 300, Height = 80, Background = Chrome.RaisedBg, Content = new Border { Padding = new Thickness(20), Child = bar } };
            meterHost.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
            var fills = bar.GetVisualDescendants().OfType<Border>().Where(delegate(Border border) { return Skin.MeterIsGradientForProbe(border); }).ToList();
            Check(fills.Count == 2 && fills.All(delegate(Border border) { return Math.Abs(Skin.MeterSpanForProbe(border) - 260) < 1; }) && fills.Any(delegate(Border border) { return border.Bounds.Width < 100; }),
                "…la barre d'un livre : ses deux parts (présents, terminés) portent le dégradé de la piste entière (260 px), quelle que soit leur largeur");
            Shot(bar, "lot31-" + id + "-barre.png");
            meterHost.Close();
            await Settle();

            // — La lueur d'« Ascension », elle seule.
            var glow = Skin.ResourceForProbe(Skin.GlowKey) as LinearGradientBrush;
            if (ascent)
                Check(glow != null && glow.StartPoint.Point == new Point(1, 1) && glow.EndPoint.Point == new Point(0, 0) && glow.GradientStops[glow.GradientStops.Count - 1].Color.A == 0
                    && Math.Abs(glow.GradientStops[glow.GradientStops.Count - 1].Offset - 0.25) < 0.001 && glow.GradientStops[0].Color.R == accent.R && glow.GradientStops[0].Color.A > 0x20,
                    "…tableaux de liège et éditeur : une lueur dorée part du coin bas droit et rejoint le fond habituel au quart du chemin");
            else
                Check(glow == null && ReferenceEquals(Skin.ResourceForProbe(Skin.GlowKey), Skin.ResourceForProbe(Skin.GroundKey)), "…pas de lueur dorée (réservée à « Ascension ») : le fond des tableaux est celui du skin");

            // Une image du tableau d'un livre et de l'éditeur, pour l'œil.
            var anyBook = project.AllItems().FirstOrDefault(delegate(BinderItem item) { return item.Kind == ItemKind.Book; });
            var anyText = project.AllItems().FirstOrDefault(delegate(BinderItem item) { return item.Kind == ItemKind.Text && !item.IsCategory; });
            if (anyBook != null)
            {
                shell.Binder.SelectItem(anyBook.Id);
                for (var i = 0; i < 3; i++) await Settle();
                Shot(shell, "lot31-" + id + "-tableau.png");
            }
            if (anyText != null)
            {
                shell.Binder.SelectItem(anyText.Id);
                for (var i = 0; i < 4; i++) await Settle();
                Shot(shell, "lot31-" + id + "-editeur.png");
            }

            // — La pastille d'onglet glisse ; les losanges du pied ; les puces des listes.
            var prefs = new PreferencesDialog(shell, project);
            prefs.Show(shell);
            for (var i = 0; i < 4; i++) await Settle();
            var tabs = prefs.Tabs.Items.OfType<TabItem>().ToList();
            if (tabs.Count > 1)
            {
                prefs.Tabs.SelectedItem = tabs[0];
                await Task.Delay(TabSlide.Milliseconds > 0 ? (int)TabSlide.Milliseconds + 120 : 0);
                await Settle();
                var rest = TabSlide.HeaderPlaceForProbe(tabs[1], prefs);
                prefs.Tabs.SelectedItem = tabs[1];
                await Settle();
                await Task.Delay(60);
                var sliding = TabSlide.SlidingForProbe(tabs[1]);
                var during = TabSlide.HeaderPlaceForProbe(tabs[1], prefs);
                Check(sliding && ReferenceEquals(prefs.Tabs.SelectedItem, tabs[1]) && Math.Abs(during.Y - rest.Y) < 1.5 && Math.Abs(during.X - rest.X) < 3,
                    "…changer d'onglet : le contenu change aussitôt, la pastille GLISSE vers l'onglet choisi, son libellé reste en place (" + rest.Y.ToString("0.#") + " / " + during.Y.ToString("0.#") + ")");
                if (AscensionArt.On)
                {
                    var shift = TabSlide.CrestShiftForProbe(tabs[1]);
                    Check(Math.Abs(shift.X) + Math.Abs(shift.Y) > 1, "…sous « Ascension », le losange voyage avec la pastille (encore à " + shift.X.ToString("0") + " ; " + shift.Y.ToString("0") + " de sa place)");
                }
                Check(TabSlide.FreeForProbe(tabs[1]), "…et elle se voit tout du long : rien ne la rogne entre l'onglet quitté et le nouveau");
                Shot(prefs, "lot31-" + id + "-onglet-en-glisse.png");
                await Task.Delay((int)TabSlide.Milliseconds + 160);
                await Settle();
                Check(TabSlide.CrestShiftForProbe(tabs[1]) == new Point(0, 0), "…le losange est posé à sa place");
                Check(!TabSlide.SlidingForProbe(tabs[1]) && tabs[1].ZIndex == 0 && tabs[1].ClipToBounds == tabs[0].ClipToBounds, "…arrivée en " + TabSlide.Milliseconds.ToString("0") + " ms, elle se pose : plus de transformation");
                AppSettings.Animations = false;
                prefs.Tabs.SelectedItem = tabs[0];
                await Settle();
                await Task.Delay(40);
                Check(!TabSlide.SlidingForProbe(tabs[0]), "…animations coupées : l'onglet saute, comme avant");
                AppSettings.Animations = true;
            }
            // Une liste déroulante des Préférences : le losange au milieu de son bord bas.
            ComboBox combo = null;
            foreach (var entry in prefs.Tabs.Items)
            {
                prefs.Tabs.SelectedItem = entry;
                for (var i = 0; i < 2; i++) await Settle();
                combo = prefs.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(delegate(ComboBox candidate) { return candidate.IsVisible && candidate.IsEffectivelyEnabled && candidate.ItemCount > 1; });
                if (combo != null) break;
            }
            if (combo != null)
            {
                combo.IsDropDownOpen = true;
                for (var i = 0; i < 4; i++) await Settle();
                var popup = combo.GetVisualDescendants().OfType<Popup>().FirstOrDefault() ?? Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(combo).OfType<Popup>().FirstOrDefault();
                var frame = popup == null ? null : popup.Child as Border;
                var foot = frame == null ? null : frame.GetVisualDescendants().OfType<FootDiamond>().FirstOrDefault();
                Check(foot != null && Math.Abs(foot.CenterInFrameForProbe.X - frame.Bounds.Width / 2) < 0.6 && Math.Abs(foot.CenterInFrameForProbe.Y - frame.Bounds.Height) < 1 && frame.Margin.Bottom >= FootDiamond.Half,
                    "…une liste déroulante : un losange au milieu de son bord bas, la place de dépasser dessous");
                if (frame != null) Shot((Control)frame.GetVisualParent() ?? frame, "lot31-" + id + "-liste-deroulante.png");
                combo.IsDropDownOpen = false;
                await Settle();
            }
            else Console.WriteLine("  (aucune liste déroulante trouvée dans les Préférences)");
            prefs.Close();
            for (var i = 0; i < 2; i++) await Settle();

            // Le menu contextuel : le losange est peint par son cadre à coins coupés.
            var anyItem = project.Category(Project.KeyWritings);
            var menu = anyItem == null ? null : shell.Binder.BuildContextMenu(anyItem);
            if (menu != null)
            {
                menu.PlacementTarget = shell.Binder;
                menu.Open(shell.Binder);
                for (var i = 0; i < 3; i++) await Settle();
                var cut = menu.GetVisualDescendants().OfType<CutFrame>().FirstOrDefault();
                Check(cut != null && cut.Bounds.Width > 80, "…le menu contextuel porte le sien, au milieu du filet du bas");
                if (cut != null) Shot(cut, "lot31-" + id + "-menu.png");
                menu.Close();
                await Settle();
            }

            // — Les puces de « Ouvrir par le nom » et du gestionnaire de styles.
            var palette = shell.QuickOpenForProbe;
            palette.TypeForProbe("a");
            for (var i = 0; i < 3; i++) await Settle();
            var chosen = shell.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(delegate(ListBoxItem item) { return item.IsSelected && item.IsVisible; });
            CheckListPearl(chosen, pill, "« Ouvrir par le nom »");
            Shot(shell, "lot31-" + id + "-ouvrir-par-le-nom.png");
            palette.Close();
            await Settle();
            var styles = StylesDialog.NewForProbe(shell, project.Styles);
            styles.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
            var styleRow = styles.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault(delegate(ListBoxItem item) { return item.IsSelected; })
                ?? styles.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault();
            if (styleRow != null && !styleRow.IsSelected) { styleRow.IsSelected = true; await Settle(); }
            CheckListPearl(styleRow, pill, "le gestionnaire de styles");
            Shot(styles, "lot31-" + id + "-styles.png");
            styles.Close();
            for (var i = 0; i < 2; i++) await Settle();
        }

        /// <summary>La puce d'une ligne de liste choisie : à cheval sur le bord gauche de la pilule
        /// (skins à pilule) — plus peinte dedans, sur l'icône ; aucune sous « Ascension ».</summary>
        private static void CheckListPearl(ListBoxItem row, bool pill, string where)
        {
            if (row == null) { Console.WriteLine("  (aucune ligne choisie dans " + where + ")"); return; }
            var pearl = row.GetVisualDescendants().OfType<PillPearl>().FirstOrDefault();
            var presenter = row.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(delegate(ContentPresenter candidate) { return candidate.Name == "PART_ContentPresenter"; });
            if (!pill) { Check(pearl == null || pearl.Strength <= 0, "…" + where + " : pas de puce sous ce skin (la ligne choisie garde sa barre)"); return; }
            var content = presenter == null ? null : presenter.GetVisualDescendants().OfType<Control>().FirstOrDefault();
            var contentAt = content == null ? null : content.TranslatePoint(new Point(0, 0), presenter);
            Check(pearl != null && pearl.Strength >= 1 && presenter != null && Math.Abs(presenter.Margin.Left - PillPearl.Room) < 0.01 && !(presenter.Background is DrawingBrush)
                && contentAt.HasValue && contentAt.Value.X >= PillPearl.SizeForProbe / 2 + 2,
                "…" + where + " : la puce de la ligne choisie est à cheval sur le bord gauche de la pilule, hors de l'icône et du texte (contenu à " + (contentAt.HasValue ? contentAt.Value.X.ToString("0.#") : "-") + " px du bord)");
        }
    }
}

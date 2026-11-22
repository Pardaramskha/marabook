using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Print;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde du LOT 30 (2.0.0, 22/11/2026) : le clic droit réorganisé
    /// (groupes, « Tuile… », « Icône… », « Exporter… »), la palette nommée, les
    /// Préférences (Personnalisation en blocs, Sauvegardes dans Projet,
    /// Accessibilité, Dépendances), la section Outils repliée au lancement,
    /// l'Atelier en tuiles (PDFaah, WGYC) — puis, si PDFium est fourni
    /// (MARABOOK_PDFIUM_NUPKG), les neuf outils PDF du lot joués pour de vrai,
    /// leurs résultats relus par PDFium. MARABOOK_PDFPIG_FOLDER (un dossier
    /// PdfPig déjà installé) joue aussi « PDF vers Word ».
    /// MARABOOK_PROBE_ONLY=lot30.</summary>
    public static partial class Probes
    {
        /// <summary>Relevé au tout début de la sonde, avant tout geste.</summary>
        internal static bool ToolsCollapsedAtStart;

        private static List<string> MenuLabels(ContextMenu menu)
        {
            var labels = new List<string>();
            if (menu == null) return labels;
            foreach (var entry in menu.Items)
            {
                if (entry is Separator) { labels.Add("—"); continue; }
                var item = entry as MenuItem;
                if (item != null) labels.Add(item.Header as string ?? "?");
            }
            return labels;
        }

        private static MenuItem MenuEntry(ContextMenu menu, string label)
        {
            if (menu == null) return null;
            foreach (var entry in menu.Items) { var item = entry as MenuItem; if (item != null && label.Equals(item.Header as string)) return item; }
            return null;
        }

        private static string Joined(List<string> labels) { return string.Join(" | ", labels.ToArray()); }

        private static async Task<bool> Answer(Task pending, string button)
        {
            for (var i = 0; i < 40 && !pending.IsCompleted; i++)
            {
                await Settle();
                var box = OpenWindow<MessageDialog>();
                if (box == null) continue;
                if (button == null) { box.Close(); continue; }
                Press(box, button);
            }
            return pending.IsCompleted;
        }

        /// <summary>Presse le bouton qui porte ce libellé (un texte, ou un bloc de texte).</summary>
        private static bool Press(Window window, string label)
        {
            if (window == null) return false;
            foreach (var candidate in window.GetVisualDescendants().OfType<Button>())
            {
                var block = candidate.Content as TextBlock;
                if (label.Equals(candidate.Content as string) || (block != null && label.Equals(block.Text))) { candidate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return true; }
            }
            return false;
        }

        private static async Task ProbeLot30(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- lot 30 : clic droit, palette, Préférences, Atelier en tuiles, outils PDF, WGYC (2.0.0, 22/11)");
            var added = new List<BinderItem>();
            var contrastBefore = AppSettings.HighContrast;
            var darkBefore = AppSettings.DarkTheme;
            var linksBefore = new List<string[]>(AppSettings.UsefulLinks);
            var customBefore = new List<PaletteColor>(ColorPalette.Custom);
            try
            {
                Check(ToolsCollapsedAtStart, "au lancement, avant tout geste, la section Outils de la Pile est repliée");

                // ================================================ le clic droit
                var writings = project.Category(Project.KeyWritings);
                var sheets = project.Category(Project.KeySheets);
                var book = new BinderItem { Kind = ItemKind.Book, Title = "Livre du lot 30 (sonde)", Book = new BookInfo { Template = new PageSetup { PageWidthMm = 148, PageHeightMm = 210 } } };
                book.Parent = writings; writings.Children.Add(book); added.Add(book);
                var chapter = SceneText("Chapitre du lot 30", "Un paragraphe.");
                chapter.Page = new PageSetup(); // A4 : il ne suit pas le gabarit A5 du livre
                chapter.Parent = book; book.Children.Add(chapter);
                var loose = SceneText("Écrit libre du lot 30", "Un paragraphe.");
                loose.Parent = writings; writings.Children.Add(loose); added.Add(loose);
                var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Fiche du lot 30" };
                sheet.Parent = sheets; sheets.Children.Add(sheet); added.Add(sheet);
                shell.Binder.Rebuild();
                await Settle();

                var tab = shell.Binder.CanOpenTab != null && shell.Binder.CanOpenTab();
                Func<string, string> lead = delegate(string rest) { return (tab ? "Ouvrir dans un nouvel onglet | — | " : "") + rest; };

                var labels = MenuLabels(shell.Binder.BuildContextMenu(chapter));
                labels.Remove("Unifier");
                Check(Joined(labels) == lead("Nouvel écrit | Nouvelle partie | — | Exporter… | — | Projet actif | Épingler à l'accueil | Épingler au rail | Vue scindée | — | Appliquer le gabarit du livre | — | Renommer… | Tuile… | Icône… | — | Dupliquer | Envoyer à la corbeille"),
                    "un écrit de livre : les groupes dans l'ordre demandé, « Nouvelle partie » dans un livre, « Appliquer le gabarit du livre » parce qu'il s'en écarte (" + Joined(labels) + ")");
                labels = MenuLabels(shell.Binder.BuildContextMenu(loose));
                labels.Remove("Unifier");
                Check(Joined(labels) == lead("Nouvel écrit | Nouveau dossier | Nouveau livre | — | Exporter… | — | Projet actif | Épingler à l'accueil | Épingler au rail | Vue scindée | — | Renommer… | Tuile… | Icône… | — | Dupliquer | Envoyer à la corbeille"),
                    "un écrit hors livre : « Nouveau dossier » et « Nouveau livre », ni partie ni gabarit à appliquer (" + Joined(labels) + ")");
                labels = MenuLabels(shell.Binder.BuildContextMenu(book));
                Check(labels.Contains("Options du livre…") && labels.IndexOf("Options du livre…") == labels.IndexOf("Renommer…") + 1 && labels.IndexOf("Tuile…") == labels.IndexOf("Options du livre…") + 1 && labels.Contains("Nouvelle partie") && !labels.Contains("Dupliquer") && labels[labels.Count - 1] == "Envoyer à la corbeille",
                    "un livre : « Options du livre… » entre « Renommer… » et « Tuile… » (" + Joined(labels) + ")");
                labels = MenuLabels(shell.Binder.BuildContextMenu(sheet));
                Check(Joined(labels) == lead("Nouvelle fiche | — | Exporter… | Exporter l'Encyclopédie… | — | Épingler à l'accueil | Épingler au rail | Vue scindée | — | Renommer… | Tuile… | Icône… | — | Dupliquer | Envoyer à la corbeille"),
                    "une fiche : la même nomenclature, « Exporter… » et « Exporter l'Encyclopédie… », pas de « Projet actif » (" + Joined(labels) + ")");
                var all = new List<string>();
                foreach (var item in new[] { chapter, loose, book, sheet }) all.AddRange(MenuLabels(shell.Binder.BuildContextMenu(item)));
                Check(!all.Contains("Couleur") && !all.Contains("Couleur de l'icône") && !all.Exists(delegate(string l) { return l.Contains("Image de la carte") || l.Contains("Changer l'icône"); }) && !all.Exists(delegate(string l) { return l.StartsWith("Ne plus"); }),
                    "plus de « Couleur », de « Couleur de l'icône » ni d'« Image de la carte » : tout est dans « Tuile… » et « Icône… »");
                var dangling = false;
                foreach (var item in new[] { chapter, loose, book, sheet, writings, sheets, project.Category(Project.KeyResearch), project.Category(Project.KeyTrash) })
                {
                    var l = MenuLabels(shell.Binder.BuildContextMenu(item));
                    if (l.Count == 0) continue;
                    if (l[0] == "—" || l[l.Count - 1] == "—") dangling = true;
                    for (var i = 1; i < l.Count; i++) if (l[i] == "—" && l[i - 1] == "—") dangling = true;
                }
                Check(!dangling, "aucun menu ne commence ni ne finit par un filet, ni n'en a deux de suite");
                labels = MenuLabels(shell.Binder.BuildContextMenu(sheets));
                Check(Joined(labels) == lead("Nouvelle fiche | Nouveau dossier | — | Exporter… | Exporter l'Encyclopédie…"), "la racine Fiches suit le même ordre (" + Joined(labels) + ")");

                // La sélection multiple suit le même ordre : exporter, les gestes, l'identité, la fin.
                var batch = string.Join(" | ", shell.BatchLabelsForProbe(new List<BinderItem> { loose, chapter }).ToArray());
                Check(batch == "Exporter… | Couleur des tuiles | Couleur des icônes | Icône… | Dupliquer | Envoyer à la corbeille", "le clic droit d'une sélection multiple suit le même ordre (" + batch + ")");

                // Une bascule active : le libellé ne change pas, une coche le dit.
                shell.Binder.TogglePin(sheet);
                await Settle();
                var pinEntry = MenuEntry(shell.Binder.BuildContextMenu(sheet), "Épingler à l'accueil");
                Check(sheet.Pinned && pinEntry != null && "on".Equals(pinEntry.Tag) && pinEntry.Icon != null, "épinglée, la fiche garde « Épingler à l'accueil », cochée");
                shell.Binder.TogglePin(sheet);
                await Settle();
                pinEntry = MenuEntry(shell.Binder.BuildContextMenu(sheet), "Épingler à l'accueil");
                Check(!sheet.Pinned && pinEntry != null && "off".Equals(pinEntry.Tag) && pinEntry.Icon == null, "…et la coche tombe quand on la désépingle");

                // « Exporter… » : le gestionnaire d'export, l'élément coché.
                var exportEntry = MenuEntry(shell.Binder.BuildContextMenu(loose), "Exporter…");
                exportEntry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                for (var i = 0; i < 10 && OpenWindow<ExportManagerDialog>() == null; i++) await Settle();
                var manager = OpenWindow<ExportManagerDialog>();
                Check(manager != null && manager.PlanForProbe != null && manager.PlanForProbe.Texts.Count == 1 && manager.PlanForProbe.Texts[0] == loose, "« Exporter… » ouvre le gestionnaire d'export, cet écrit seul dans l'ampleur");
                if (manager != null) manager.Close();
                await Settle();

                // « Appliquer le gabarit du livre ».
                MenuEntry(shell.Binder.BuildContextMenu(chapter), "Appliquer le gabarit du livre").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Settle();
                Check(chapter.Page != null && chapter.Page.SameLayout(book.Book.Template) && !MenuLabels(shell.Binder.BuildContextMenu(chapter)).Contains("Appliquer le gabarit du livre"), "« Appliquer le gabarit du livre » met l'écrit au format du livre ; l'entrée disparaît ensuite");

                await ProbeRename(shell, loose, chapter, sheet);

                // ================================================ la palette, « Tuile… », « Icône… »
                Check(ColorPalette.Label("#5B67D8") == "Indigo" && ColorPalette.Label("#c0392b") == "Rouge" && ColorPalette.Label("#123456") == "#123456" && ColorPalette.Describe("#16A085") == "Sarcelle — #16A085", "la palette nomme les couleurs d'origine ; une couleur inconnue garde son code");
                var mine = ColorPalette.Add("#1b6e5a", "Vert sapin");
                var fill = new ContextMenu();
                ColorMenus.Fill(fill, shell, project, "#C0392B", delegate { });
                var fillLabels = MenuLabels(fill);
                Check(fillLabels.Contains("Indigo") && fillLabels.Contains("Brun") && fillLabels.Contains("Vert sapin") && !fillLabels.Exists(delegate(string l) { return l.StartsWith("#"); }) && fillLabels[0] == "Aucune couleur" && fillLabels[fillLabels.Count - 1] == "Nouvelle couleur…",
                    "un nuancier montre des NOMS, la couleur personnalisée sous le sien, jamais un code (" + fillLabels.Count + " entrées)");

                shell.Binder.EditTile(loose);
                for (var i = 0; i < 10 && OpenWindow<TileDialog>() == null; i++) await Settle();
                var tile = OpenWindow<TileDialog>();
                Check(tile != null && tile.ColorNameForProbe == "Aucune couleur", "« Tuile… » ouvre la fenêtre de la tuile : couleur et image ensemble");
                if (tile != null)
                {
                    await Settle();
                    await Settle();
                    Check(tile.Bounds.Height < 480 && tile.Bounds.Height > 250, "…elle prend la hauteur de son contenu (" + Math.Round(tile.Bounds.Height) + " px)");
                    Shot(tile, "lot30-tuile.png");
                    tile.PickForProbe("#C0392B");
                    await Settle();
                    Check(loose.CardColor == "#C0392B" && tile.ColorNameForProbe == "Rouge", "une pastille cliquée colore la tuile aussitôt ; la fenêtre dit « Rouge »");
                    tile.Close();
                    await Settle();
                }
                shell.Binder.EditTile(sheet);
                for (var i = 0; i < 10 && OpenWindow<TileDialog>() == null; i++) await Settle();
                tile = OpenWindow<TileDialog>();
                var imageBlock = tile == null ? null : tile.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(delegate(StackPanel p) { return "tile-image".Equals(p.Tag); });
                Check(tile != null && imageBlock != null && !imageBlock.IsVisible, "pour une fiche, la fenêtre n'offre que la couleur (l'image de tuile vaut pour un écrit ou un livre)");
                if (tile != null) tile.Close();
                await Settle();

                shell.Binder.ChangeIcon(loose);
                for (var i = 0; i < 10 && OpenWindow<IconPickerDialog>() == null; i++) await Settle();
                var picker = OpenWindow<IconPickerDialog>();
                Check(picker != null && picker.Title == "Icône", "« Icône… » ouvre le choix de l'icône, couleur comprise");
                if (picker != null)
                {
                    picker.ChooseForProbe("book-bold", "#27AE60");
                    await Settle();
                    Shot(picker, "lot30-icone.png");
                    picker.GetVisualDescendants().OfType<Button>().First(delegate(Button b) { return "icon-apply".Equals(b.Tag); }).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Settle();
                    await Settle();
                    Check(loose.Icon == "svg:book-bold:#27AE60", "l'icône ET sa couleur se choisissent ensemble, puis « Appliquer » (" + loose.Icon + ")");
                }
                shell.Binder.ChangeIcon(loose);
                for (var i = 0; i < 10 && OpenWindow<IconPickerDialog>() == null; i++) await Settle();
                picker = OpenWindow<IconPickerDialog>();
                if (picker != null)
                {
                    Check(picker.Result() == "svg:book-bold:#27AE60", "rouverte, la fenêtre part de l'icône et de la couleur en cours");
                    picker.ChooseForProbe(null, "#C0392B");
                    Check(picker.Result() == "svg:book-bold:#C0392B", "on peut ne changer que la couleur");
                    picker.ResetForProbe();
                    Check(picker.Result() == "", "« Icône par défaut » rend l'icône d'origine, sans couleur");
                    picker.ChooseForProbe(null, "#2980B9");
                    Check(picker.Result() == "svg:" + ItemIcons.DefaultSvg(loose) + ":#2980B9", "…et l'icône d'origine se teinte elle aussi");
                    picker.Close();
                    await Settle();
                }

                // ================================================ les Préférences
                var touched = 0;
                var prefs = new PreferencesDialog(shell, project);
                prefs.AppearanceChanged += shell.ApplyAppearanceForProbe;
                prefs.ProjectTouched += delegate { touched++; };
                prefs.Show(shell);
                await Settle();
                var titles = new List<string>();
                foreach (var entry in prefs.Tabs.Items) { var t = entry as TabItem; if (t != null && t.Header is string) titles.Add((string)t.Header); }
                Check(string.Join(", ", titles.ToArray()).StartsWith("Projet, Personnalisation, Profil, Édition, Correction, Styles globaux, Catalogue de polices, Auteur, Accessibilité, Raccourcis, Dépendances, DLC"),
                    "onglets : « Personnalisation » (ex-Général), « Accessibilité » entre Auteur et Raccourcis, « Dépendances » (" + string.Join(", ", titles.ToArray()) + ")");
                prefs.Tabs.SelectedIndex = 0;
                await Settle();
                Func<string, bool> hasTag = delegate(string tag) { return prefs.GetVisualDescendants().OfType<Control>().Any(delegate(Control c) { return tag.Equals(c.Tag) && c.IsEffectivelyVisible; }); };
                Check(hasTag("backups-enabled") && hasTag("backup-interval"), "les Sauvegardes sont dans l'onglet « Projet »");
                prefs.Tabs.SelectedIndex = 1;
                await Settle();
                await Settle();
                var groups = new List<string>();
                foreach (var border in prefs.GetVisualDescendants().OfType<Border>()) { var tag = border.Tag as string; if (tag != null && tag.StartsWith("prefs-group:") && border.IsEffectivelyVisible) groups.Add(tag.Substring(12)); }
                Check(string.Join(" ", groups.ToArray()) == "skin motion tabs dark palette" && !hasTag("backups-enabled"), "Personnalisation : cinq blocs à bord arrondi — skin et couleurs, défilement/animations/sons, onglets, mode sombre, palette — et plus de Sauvegardes (" + string.Join(" ", groups.ToArray()) + ")");
                var groupBorder = prefs.GetVisualDescendants().OfType<Border>().First(delegate(Border b) { return "prefs-group:skin".Equals(b.Tag); });
                Check(groupBorder.CornerRadius.TopLeft >= 8 && groupBorder.BorderThickness.Left >= 1 && groupBorder.GetVisualDescendants().OfType<WrapPanel>().Any(delegate(WrapPanel w) { return "skins".Equals(w.Tag); }), "le bloc du skin a sa bordure arrondie et contient les skins");
                var chips = prefs.GetVisualDescendants().OfType<Border>().Count(delegate(Border b) { var tag = b.Tag as string; return tag != null && tag.StartsWith("palette-chip:"); });
                var expectedChips = 0;
                foreach (var g in ColorPalette.Groups) expectedChips += g.Colors.Length;
                Check(chips == expectedChips && chips >= 30, "la Palette montre toutes les couleurs d'origine, nommées (" + chips + ")");
                prefs.RebuildPaletteForProbe();
                await Settle();
                Check(prefs.PaletteRowsForProbe >= 1 && prefs.GetVisualDescendants().OfType<TextBox>().Any(delegate(TextBox b) { return "palette-name".Equals(b.Tag) && b.Text == "Vert sapin"; }), "…et les couleurs personnalisées, avec leur nom à modifier");
                Shot(prefs, "lot30-preferences-personnalisation.png");
                var scroller = prefs.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(delegate(ScrollViewer s) { return s.IsEffectivelyVisible && s.Extent.Height > s.Viewport.Height + 50; });
                if (scroller != null) { scroller.Offset = new Vector(0, scroller.Extent.Height); await Settle(); Shot(prefs, "lot30-preferences-palette.png"); }
                ColorPalette.Rename(mine, "Sapin");
                Check(ColorPalette.Label("#1B6E5A") == "Sapin", "renommer une couleur change son nom partout");
                Check(ColorPalette.Recolor(mine, "#1C7A63") && ColorPalette.Label("#1C7A63") == "Sapin" && ColorPalette.Label("#1B6E5A") == "#1B6E5A", "changer une couleur garde son nom, sur le nouveau code");
                project.CustomColors.Add("#1C7A63");
                prefs.RemovePaletteColor(mine);
                Check(ColorPalette.FindCustom("#1C7A63") == null && !project.CustomColors.Contains("#1C7A63") && touched == 1, "retirer une couleur la sort de la palette — et du projet qui la portait, qui se sait modifié");
                Check(!ColorPalette.Absorb(new[] { "#1C7A63" }) && ColorPalette.Absorb(new[] { "#ABCDEF" }) && ColorPalette.FindCustom("#ABCDEF") != null, "une couleur retirée ne revient pas d'elle-même ; une couleur de projet inconnue est reprise");
                ColorPalette.Remove(ColorPalette.FindCustom("#ABCDEF"));

                // L'accessibilité.
                prefs.Tabs.SelectedIndex = titles.IndexOf("Accessibilité");
                await Settle();
                AppSettings.DarkTheme = false;
                shell.ApplyAppearanceForProbe();
                var softBefore = Chrome.ContrastRatio(Chrome.SoftText.Color, Chrome.WindowBg.Color);
                var faintBefore = Chrome.ContrastRatio(Chrome.FaintText.Color, Chrome.RaisedBg.Color);
                prefs.SetContrastForProbe(true);
                await Settle();
                var soft = Chrome.ContrastRatio(Chrome.SoftText.Color, Chrome.WindowBg.Color);
                var faint = Chrome.ContrastRatio(Chrome.FaintText.Color, Chrome.RaisedBg.Color);
                var line = Chrome.ContrastRatio(Chrome.Border.Color, Chrome.RaisedBg.Color);
                var onAccent = Chrome.ContrastRatio(Chrome.Accent.Color, Colors.White);
                var chosenRow = Chrome.ContrastRatio(Chrome.Ink.Color, Chrome.AccentSoft.Color);
                Check(chosenRow >= 7, "…la ligne choisie de la Pile porte l'encre à " + chosenRow.ToString("0.0") + ":1");
                Check(AppSettings.HighContrast && Chrome.Ink.Color == Colors.Black && soft >= 7 && faint >= 7 && soft > softBefore + 2 && faint > faintBefore + 3 && line >= 4.5 && onAccent >= 7,
                    "« Augmenter le contraste » (clair) : encre noire, texte secondaire " + softBefore.ToString("0.0") + " → " + soft.ToString("0.0") + ":1, texte pâle " + faintBefore.ToString("0.0") + " → " + faint.ToString("0.0") + ":1, filets " + line.ToString("0.0") + ":1, blanc sur accent " + onAccent.ToString("0.0") + ":1");
                Shot(shell, "lot30-contraste-clair.png");
                Shot(prefs, "lot30-preferences-accessibilite.png");
                AppSettings.DarkTheme = true;
                shell.ApplyAppearanceForProbe();
                await Settle();
                soft = Chrome.ContrastRatio(Chrome.SoftText.Color, Chrome.RaisedBg.Color);
                faint = Chrome.ContrastRatio(Chrome.FaintText.Color, Chrome.RaisedBg.Color);
                Check(Chrome.Ink.Color == Colors.White && soft >= 7 && faint >= 7 && Chrome.ContrastRatio(Chrome.Accent.Color, Chrome.RaisedBg.Color) >= 7, "…au sombre aussi : encre blanche, texte secondaire " + soft.ToString("0.0") + ":1, texte pâle " + faint.ToString("0.0") + ":1");
                Shot(shell, "lot30-contraste-sombre.png");
                AppSettings.DarkTheme = false;
                prefs.SetContrastForProbe(false);
                shell.ApplyAppearanceForProbe();
                await Settle();
                Check(!AppSettings.HighContrast && Chrome.Ink.Color != Colors.Black, "décochée, la palette ordinaire revient");
                prefs.Close();
                await Settle();

                // ================================================ l'Atelier en tuiles
                await ProbeLot30Atelier(shell);
            }
            finally
            {
                AppSettings.HighContrast = contrastBefore;
                AppSettings.DarkTheme = darkBefore;
                shell.ApplyAppearanceForProbe();
                AppSettings.UsefulLinks = linksBefore;
                ColorPalette.Custom.Clear();
                ColorPalette.Custom.AddRange(customBefore);
                ColorPalette.Dismissed.Clear();
                foreach (var item in added) if (item.Parent != null) item.Parent.Children.Remove(item);
                shell.Binder.Rebuild();
                shell.Binder.SelectItem(shell.Project.Category(Project.KeyWritings).Id);
                await Settle();
            }
        }

        /// <summary>RENOMMER DEPUIS LA PILE (22/11, bug relevé par Rémi) : le champ de renommage se
        /// refermait dès que la vue de l'élément, en s'ouvrant, lui reprenait le clavier — on ne
        /// pouvait renommer qu'un élément déjà ouvert, et pas du tout au double tap d'un pavé tactile.</summary>
        private static async Task ProbeRename(MainWindow shell, BinderItem open, BinderItem other, BinderItem sheet)
        {
            var binder = shell.Binder;
            var editor = shell.GetVisualDescendants().OfType<EditorView>().FirstOrDefault();
            Action<Avalonia.Input.Key> press = delegate(Avalonia.Input.Key key)
            {
                var box = binder.RenameBoxForProbe;
                if (box != null) box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key });
            };
            // Le clavier ne se donne qu'à une fenêtre ACTIVE : si une autre application est devant (la sonde
            // tourne longtemps, l'auteur travaille à côté), ces scénarios ne prouvent rien et ne sont pas joués.
            shell.Activate();
            for (var i = 0; i < 3; i++) await Settle();
            if (!shell.IsActive) { Console.WriteLine("  la fenêtre de Marabook n'est pas au premier plan : le renommage dans la Pile n'est pas joué (MARABOOK_PROBE_ONLY=lot30, fenêtre devant, le joue)."); return; }
            // Dans la sonde complète, le voile de l'accueil (une sonde d'avant) tient la coquille ÉTEINTE : rien n'y
            // prend le clavier. Elle est rallumée le temps de ces scénarios, puis rendue telle quelle.
            var asleep = new List<Control>();
            for (Visual v = binder; v != null; v = v.GetVisualParent()) { var c = v as Control; if (c != null && !c.IsEnabled) asleep.Add(c); }
            foreach (var c in asleep) c.IsEnabled = true;
            try { await ProbeRenameCore(shell, open, other, sheet, binder, editor, press); }
            finally { foreach (var c in asleep) c.IsEnabled = false; }
        }

        private static async Task ProbeRenameCore(MainWindow shell, BinderItem open, BinderItem other, BinderItem sheet, BinderView binder, EditorView editor, Action<Avalonia.Input.Key> press)
        {
            binder.SelectItem(open.Id, true);
            for (var i = 0; i < 4; i++) await Settle();
            Check(shell.CurrentForProbe == open && binder.RenameBoxForProbe == null, "un écrit est ouvert ; rien n'est en cours de renommage");

            // « Renommer… » sur un AUTRE élément, pas ouvert.
            binder.Rename(other);
            for (var i = 0; i < 4; i++) await Settle();
            var field = binder.RenameBoxForProbe;
            Check(field != null && field.IsFocused && field.Text == other.Title, "« Renommer… » sur un élément qui n'est pas ouvert : le champ paraît dans la Pile, le clavier dedans");
            Check(shell.CurrentForProbe == open, "…et l'élément n'est PAS ouvert pour autant : celui d'avant reste à l'écran");
            // La vue reprend le clavier sans geste de l'auteur (ce que fait un élément qui finit de s'ouvrir).
            if (editor != null) editor.FocusEditor();
            for (var i = 0; i < 4; i++) await Settle();
            Check(binder.RenameBoxForProbe == field && field.IsFocused, "une vue qui reprend le clavier ne referme plus le champ : il le reprend");
            field.Text = "Chapitre renommé (sonde)";
            press(Avalonia.Input.Key.Enter);
            for (var i = 0; i < 3; i++) await Settle();
            Check(other.Title == "Chapitre renommé (sonde)" && binder.RenameBoxForProbe == null && shell.CurrentForProbe == open, "Entrée valide le nouveau nom ; l'élément ouvert n'a pas changé");

            // Le double-clic : le premier clic OUVRE l'élément, le second demande le renommage — l'ouverture finit après.
            binder.SelectItem(sheet.Id, true);
            binder.Rename(sheet);
            for (var i = 0; i < 6; i++) await Settle();
            field = binder.RenameBoxForProbe;
            Check(field != null && field.IsFocused && shell.CurrentForProbe == sheet, "renommer un élément EN TRAIN de s'ouvrir (le double-clic, le double tap) : le champ tient, clavier dedans, une fois la vue posée (" + (field == null ? "pas de champ" : field.IsFocused ? "clavier dedans" : "clavier ailleurs") + ", ouvert : " + (shell.CurrentForProbe == null ? "-" : shell.CurrentForProbe.Title) + ")");
            if (field != null) field.Text = "Ne sera pas gardé";
            press(Avalonia.Input.Key.Escape);
            for (var i = 0; i < 3; i++) await Settle();
            Check(sheet.Title == "Fiche du lot 30" && binder.RenameBoxForProbe == null, "Échap abandonne : le nom d'avant reste");

            // Une touche qui mène ailleurs (un raccourci) valide ce qui est tapé.
            binder.Rename(sheet);
            for (var i = 0; i < 4; i++) await Settle();
            field = binder.RenameBoxForProbe;
            if (field != null) field.Text = "Fiche renommée (sonde)";
            press(Avalonia.Input.Key.F6);
            var elsewhere = shell.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(delegate(TextBox t) { return !ReferenceEquals(t, field) && t.IsEffectivelyVisible && t.IsEnabled && t.Focusable; });
            if (elsewhere != null) elsewhere.Focus();
            for (var i = 0; i < 4; i++) await Settle();
            Check(sheet.Title == "Fiche renommée (sonde)" && binder.RenameBoxForProbe == null, "le clavier emmené ailleurs par une touche de l'auteur : le nouveau nom est gardé");
            binder.Rename(sheet);
            for (var i = 0; i < 4; i++) await Settle();
            field = binder.RenameBoxForProbe;
            if (field != null) field.Text = "Fiche du lot 30";
            press(Avalonia.Input.Key.Tab);
            for (var i = 0; i < 3; i++) await Settle();
            Check(sheet.Title == "Fiche du lot 30" && binder.RenameBoxForProbe == null, "Tab valide aussi");
            other.Title = "Chapitre du lot 30";
            binder.Rebuild();
            await Settle();
        }

        private static async Task ProbeLot30Atelier(MainWindow shell)
        {
            var folder = Path.Combine(Path.GetTempPath(), "marabook-sonde-lot30-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var pdfiumBefore = Pdfium.FolderOverride;
            var pigBefore = PdfPigBridge.FolderOverride;
            Pdfium.FolderOverride = Path.Combine(folder, "pdfium");
            PdfPigBridge.FolderOverride = Path.Combine(folder, "pdfpig");
            var atelier = shell.AtelierForProbe;
            string opened = null;
            AtelierView.LinkOpener = delegate(string url) { opened = url; };
            try
            {
                // La section repliée ne montre pas l'Atelier ; l'ouvrir la déplie.
                if (!BinderView.CollapsedSections.Contains("Outils")) shell.Binder.ToggleSectionForProbe("Outils");
                await Settle();
                var rows = shell.Binder.TopRowsForProbe();
                Check(rows.Contains("Outils") && !rows.Contains("Atelier"), "repliée, la section Outils ne montre que son nom");
                shell.ShowAtelier();
                await Settle();
                await Settle();
                rows = shell.Binder.TopRowsForProbe();
                Check(atelier.IsVisible && rows.IndexOf("Atelier") == rows.IndexOf("Outils") + 1, "ouvrir l'Atelier déplie la section");
                atelier.ShowForProbe(AtelierView.ToolHome);
                await Settle();
                Func<string, List<string>> tagged = delegate(string prefix)
                {
                    var found = new List<string>();
                    foreach (var border in atelier.GetVisualDescendants().OfType<Border>()) { var tag = border.Tag as string; if (tag != null && tag.StartsWith(prefix) && border.IsEffectivelyVisible) found.Add(tag.Substring(prefix.Length)); }
                    return found;
                };
                var boxes = tagged("atelier-box:");
                Check(string.Join(" ", boxes.ToArray()) == "pdfaah wgyc" && tagged("atelier-tool:").Count == 0, "l'accueil de l'Atelier : ses boîtes à outils en tuiles — PDFaah, WGYC —, plus d'onglets ni de pastilles");
                var previews = atelier.GetVisualDescendants().OfType<WrapPanel>().Where(delegate(WrapPanel w) { return "atelier-box-preview".Equals(w.Tag); }).ToList();
                Check(previews.Count == 2 && previews[0].Children.Count == 13 && previews[1].Children.Count == 2, "chaque tuile donne l'aperçu de ses outils (13 pour PDFaah ; le Conjugueur et les Liens utiles pour WGYC)");
                Shot(shell, "lot30-atelier-accueil.png");

                Check(await atelier.ChooseAsync(AtelierView.BoxPdfaah) && atelier.Tool == AtelierView.BoxPdfaah, "une tuile ouvre sa boîte");
                await Settle();
                var tools = tagged("atelier-tool:");
                Check(tools.Count == 13 && tools.Contains(AtelierView.ToolSplit) && tools.Contains(AtelierView.ToolMerge) && tools.Contains(AtelierView.ToolBlank) && tools.Contains(AtelierView.ToolRotate) && tools.Contains(AtelierView.ToolWatermark)
                    && tools.Contains(AtelierView.ToolSign) && tools.Contains(AtelierView.ToolCompress) && tools.Contains(AtelierView.ToolConvert) && tools.Contains(AtelierView.ToolLock), "PDFaah : treize outils, dont les neuf du lot (scinder, fusionner, pages blanches, pivoter, filigrane, signer, compresser, convertir, verrouiller)");
                // Les composants : même largeur, état et bouton à la même verticale.
                var components = atelier.GetVisualDescendants().OfType<Border>().Where(delegate(Border b) { var tag = b.Tag as string; return tag != null && tag.StartsWith("atelier-component:"); }).ToList();
                var states = atelier.GetVisualDescendants().OfType<StackPanel>().Where(delegate(StackPanel p) { var tag = p.Tag as string; return tag != null && tag.StartsWith("atelier-component-status:"); }).ToList();
                var actions = atelier.GetVisualDescendants().OfType<Button>().Where(delegate(Button b) { var tag = b.Tag as string; return tag != null && tag.StartsWith("atelier-component-action:"); }).ToList();
                Func<Visual, double> leftOf = delegate(Visual v) { var p = v.TranslatePoint(new Point(0, 0), atelier); return p.HasValue ? Math.Round(p.Value.X) : -1; };
                Check(components.Count == 2 && Math.Abs(components[0].Bounds.Width - components[1].Bounds.Width) < 0.5 && components[0].Bounds.Width > 500
                    && states.Count == 2 && leftOf(states[0]) == leftOf(states[1]) && actions.Count == 2 && leftOf(actions[0]) == leftOf(actions[1]) && Math.Abs(actions[0].Bounds.Width - actions[1].Bounds.Width) < 0.5,
                    "Composants : deux cadres de même largeur (" + (components.Count == 2 ? Math.Round(components[0].Bounds.Width) + " px" : "-") + "), l'état et le bouton à la même verticale de l'un à l'autre");
                Shot(shell, "lot30-pdfaah.png");
                shell.NavigateBackForProbe();
                await Settle();
                Check(atelier.Tool == AtelierView.ToolHome && atelier.IsVisible, "« Précédent » ramène à l'accueil de l'Atelier");

                // ---------------------------------------------- WGYC
                await atelier.ChooseAsync(AtelierView.BoxWgyc);
                await Settle();
                Check(atelier.Tool == AtelierView.BoxWgyc && tagged("atelier-tool:").Count == 1 && tagged("atelier-tool:")[0] == AtelierView.ToolConjugator && atelier.LinkRowsForProbe == 2 + AppSettings.UsefulLinks.Count, "WGYC : le Conjugueur, et les Liens utiles — CNRTL et Wiktionnaire d'office");
                atelier.GetVisualDescendants().OfType<Button>().First(delegate(Button b) { return "atelier-link-open".Equals(b.Tag); }).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(opened == "https://www.cnrtl.fr/", "« Ouvrir » envoie l'adresse au navigateur (" + opened + ")");
                var before = AppSettings.UsefulLinks.Count;
                Check(atelier.AddLink("Piège", "javascript:alert(1)") != null && atelier.AddLink("Piège", "file:///C:/Windows/system32/calc.exe") != null && atelier.AddLink("", "https://exemple.org") != null && AppSettings.UsefulLinks.Count == before, "un lien qui n'est pas une adresse http(s) est refusé ; un lien sans nom aussi");
                Check(atelier.AddLink("Académie française", "www.dictionnaire-academie.fr") == null && AppSettings.UsefulLinks.Count == before + 1 && AppSettings.UsefulLinks[before][1] == "https://www.dictionnaire-academie.fr/" && atelier.AddLink("Encore", "https://www.cnrtl.fr/") != null, "un lien ajouté rejoint la liste (https d'office) ; un doublon est refusé");
                await Settle();
                Shot(shell, "lot30-wgyc.png");
                atelier.GetVisualDescendants().OfType<Button>().First(delegate(Button b) { return "atelier-link-remove".Equals(b.Tag); }).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(AppSettings.UsefulLinks.Count == before, "…et se retire");
                await atelier.ChooseAsync(AtelierView.ToolConjugator);
                await Settle();
                await Settle();
                Check(atelier.Tool == AtelierView.ToolConjugator && atelier.ConjugatorForProbe != null && atelier.ConjugatorForProbe.IsEffectivelyVisible && atelier.ConjugatorForProbe.Bounds.Height > 250 && atelier.ConjugatorForProbe.HeadForProbe == "Saisissez un verbe.", "le Conjugueur s'ouvre DANS la page de WGYC");
                Check(atelier.ConjugatorForProbe.Bounds.Height > atelier.Bounds.Height - 260, "…et prend toute la hauteur de la page (" + Math.Round(atelier.ConjugatorForProbe.Bounds.Height) + " px sur " + Math.Round(atelier.Bounds.Height) + ")");
                var conjugator = atelier.ConjugatorForProbe;
                conjugator.Conjugate("mangeaient");
                for (var i = 0; i < 120 && !conjugator.ShownForProbe.Found && !conjugator.HeadForProbe.StartsWith("Le conjugueur ne répond pas"); i++) await Task.Delay(250);
                await Settle();
                if (conjugator.HeadForProbe.StartsWith("Le conjugueur ne répond pas")) Check(conjugator.MoodSectionsForProbe == 0, "Grammalecte absent de ce build : le Conjugueur de WGYC le dit, sans tableau");
                else Check(conjugator.ShownForProbe.Found && conjugator.HeadForProbe == "manger" && conjugator.MoodSectionsForProbe == 5, "« mangeaient » : le Conjugueur de WGYC dresse le tableau de « manger » (" + conjugator.MoodSectionsForProbe + " modes)");
                Shot(shell, "lot30-conjugueur.png");
                shell.NavigateBackForProbe();
                await Settle();
                Check(atelier.Tool == AtelierView.BoxWgyc, "« Précédent » ramène à WGYC");
                atelier.ShowForProbe(AtelierView.ToolHome);

                // ---------------------------------------------- le compacteur et le verrou, sans PDFium
                var six = Path.Combine(folder, "six-pages.pdf");
                File.WriteAllBytes(six, ProbePdf(6, null, false));
                var two = Path.Combine(folder, "deux-pages.pdf");
                File.WriteAllBytes(two, ProbePdf(2, null, false));
                var bloated = BloatedPdf();
                PdfCompactReport compact;
                var shrunk = PdfCompact.Shrink(bloated, out compact);
                Check(shrunk != null && compact.Orphans >= 1 && compact.Duplicates == 2 && compact.Deflated >= 1 && shrunk.Length < bloated.Length / 3, "le compacteur : objets orphelins écartés (" + compact.Orphans + "), doublons réunis (" + compact.Duplicates + "), flux compressés (" + compact.Deflated + ") — " + bloated.Length + " → " + (shrunk == null ? 0 : shrunk.Length) + " octets");
                string why;
                var sealedPdf = PdfLock.Encrypt(ProbePdf(6, null, false), "sésame ouvre-toi", out why);
                Check(sealedPdf != null && PdfLock.IsLocked(sealedPdf) && PdfLock.Opens(sealedPdf, "sésame ouvre-toi") && !PdfLock.Opens(sealedPdf, "sesame ouvre-toi") && !PdfLock.Opens(sealedPdf, ""), "le verrou : la copie chiffrée reconnaît son mot de passe, et lui seul");
                Check(PdfLock.Encrypt(sealedPdf, "autre", out why) == null && why.Contains("déjà verrouillé") && PdfLock.PasswordProblem("") != null && PdfLock.PasswordProblem("mot€") != null, "…un PDF déjà verrouillé, un mot de passe vide ou à caractère hors du jeu sont refusés, avec la raison");

                // ---------------------------------------------- le vrai moteur
                var nupkg = Environment.GetEnvironmentVariable("MARABOOK_PDFIUM_NUPKG");
                if (string.IsNullOrEmpty(nupkg) || !File.Exists(nupkg))
                {
                    Console.WriteLine("  archive PDFium absente (MARABOOK_PDFIUM_NUPKG) : les outils PDF du lot 30 ne sont pas joués.");
                    return;
                }
                var problem = Pdfium.InstallFromFile(nupkg);
                Check(problem == null && Pdfium.Installed, "PDFium installé dans le dossier d'essai (" + (problem ?? "ok") + ")");
                await ProbeLot30Tools(shell, atelier, folder, six, two, bloated, shrunk, sealedPdf);
            }
            finally
            {
                AtelierView.LinkOpener = null;
                atelier.CloseFilesForProbe();
                atelier.ShowForProbe(AtelierView.ToolHome);
                Pdfium.Remove();
                Pdfium.FolderOverride = pdfiumBefore;
                PdfPigBridge.FolderOverride = pigBefore;
                shell.Binder.SelectItem(shell.Project.Category(Project.KeyWritings).Id);
                await Settle();
                await Settle();
                var late = OpenWindow<MessageDialog>();
                if (late != null) late.Close();
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        private static string Widths(string path, out int pages)
        {
            int error;
            using (var document = Pdfium.Open(path, null, out error))
            {
                pages = document == null ? -1 : document.PageCount;
                if (document == null) return "?";
                var text = "";
                foreach (var size in document.Sizes()) text += Math.Round(size[0]) + "x" + Math.Round(size[1]) + " ";
                return text.Trim();
            }
        }

        private static byte[] Pixels(byte[] pdf, string password, int page, int longest, out int width, out int height)
        {
            int error;
            width = 0;
            height = 0;
            using (var document = Pdfium.Open(pdf, password, out error))
                return document == null ? null : document.Render(page, longest, out width, out height);
        }

        private static int Differences(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return -1;
            var count = 0;
            for (var i = 0; i + 3 < a.Length; i += 4)
                if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 24) count++;
            return count;
        }

        private static async Task ProbeLot30Tools(MainWindow shell, AtelierView atelier, string folder, string six, string two, byte[] bloated, byte[] shrunk, byte[] sealedPdf)
        {
            int pages, w, h, w2, h2;
            var sixBytes = File.ReadAllBytes(six);

            // Le compacteur et le verrou, relus par PDFium.
            Check(Differences(Pixels(bloated, null, 0, 400, out w, out h), Pixels(shrunk, null, 0, 400, out w2, out h2)) == 0 && w == w2 && Pixels(shrunk, null, 2, 400, out w, out h) != null, "le PDF compacté se rouvre : trois pages, la première dessinée au point près comme avant");
            int error;
            using (var closed = Pdfium.Open(sealedPdf, null, out error)) Check(closed == null && error == Pdfium.ErrorPassword, "le PDF verrouillé par Marabook : PDFium refuse de l'ouvrir sans mot de passe");
            Check(Differences(Pixels(sealedPdf, "sésame ouvre-toi", 3, 400, out w, out h), Pixels(sixBytes, null, 3, 400, out w2, out h2)) == 0 && w > 0, "…et l'ouvre avec le bon (accents compris) : la page se dessine comme celle d'origine");

            // ---------------------------------------------- scinder
            await atelier.ChooseAsync(AtelierView.BoxPdfaah);
            await atelier.ChooseAsync(AtelierView.ToolSplit);
            await Settle();
            Check(await atelier.LoadJob(six) && atelier.JobForProbe != null && atelier.JobForProbe.Pages == 6 && atelier.PendingWork() == null, "Scinder : un PDF choisi, 6 pages ; rien n'est encore à perdre");
            await Settle();
            Shot(shell, "lot30-scinder.png");
            var partA = Path.Combine(folder, "a.pdf");
            var partB = Path.Combine(folder, "b.pdf");
            Check(await atelier.RunSplit(2, partA, partB) && atelier.LastToolOk, "…coupé après la page 2 : deux fichiers (" + atelier.LastToolStatus + ")");
            var widthsA = Widths(partA, out pages);
            var pagesA = pages;
            var widthsB = Widths(partB, out pages);
            Check(pagesA == 2 && widthsA == "310x600 320x600" && pages == 4 && widthsB.StartsWith("330x600") && widthsB.EndsWith("360x600"), "…relus : 2 pages (" + widthsA + ") et 4 pages (" + widthsB + ")");
            Check(File.ReadAllBytes(six).Length == sixBytes.Length, "…le PDF d'origine n'a pas changé");

            // ---------------------------------------------- pages blanches
            await atelier.ChooseAsync(AtelierView.BoxPdfaah);
            Check(atelier.JobForProbe == null, "quitter un outil retire son PDF");
            await atelier.ChooseAsync(AtelierView.ToolBlank);
            await atelier.LoadJob(six);
            await Settle();
            int at;
            double bw, bh;
            atelier.BlankForProbe(out at, out bw, out bh);
            Check(at == 6 && Math.Round(bw) == 360 && Math.Round(bh) == 600, "Pages blanches : d'office à la fin, à la taille de la page voisine (" + Math.Round(bw) + " × " + Math.Round(bh) + " pt)");
            atelier.ChooseForProbe("atelier-blank-where", 2);
            atelier.SetForProbe("atelier-blank-after", "2");
            atelier.BlankForProbe(out at, out bw, out bh);
            Check(at == 2 && Math.Round(bw) == 320, "« après la page 2 » : la page blanche prend la taille de la page 2");
            atelier.CustomBlankForProbe();
            atelier.ChooseForProbe("atelier-blank-format", 0);
            atelier.BlankForProbe(out at, out bw, out bh);
            Check(Math.Abs(bw - Number0(PdfPrintCheck.Formats[0][1]) * 72 / 25.4) < 0.5, "« une autre taille » : le format choisi (" + PdfPrintCheck.Formats[0][0] + ")");
            Shot(shell, "lot30-pages-blanches.png");
            var blank = Path.Combine(folder, "blanches.pdf");
            Check(await atelier.RunBlank(2, 2, 200, 300, blank), "…deux pages blanches glissées après la page 2");
            var widths = Widths(blank, out pages);
            Check(pages == 8 && widths.StartsWith("310x600 320x600 200x300 200x300 330x600"), "…relu : 8 pages, les blanches à leur place et à leur taille (" + widths + ")");

            // ---------------------------------------------- fusionner
            await atelier.ChooseAsync(AtelierView.BoxPdfaah);
            await atelier.ChooseAsync(AtelierView.ToolMerge);
            Check(await atelier.AddToMerge(six) && await atelier.AddToMerge(two) && atelier.MergeCountForProbe == 2 && atelier.MergeOrderForProbe() == "six-pages deux-pages", "Fusionner : deux PDF dans la liste");
            atelier.MoveInMerge(1, -1);
            await Settle();
            Check(atelier.MergeOrderForProbe() == "deux-pages six-pages" && atelier.PendingWork() != null, "…réordonnés ; il y a maintenant du travail à perdre");
            Shot(shell, "lot30-fusionner.png");
            var merged = Path.Combine(folder, "fusion.pdf");
            Check(await atelier.RunMerge(merged) && atelier.PendingWork() == null, "…fusionnés (" + atelier.LastToolStatus + ")");
            widths = Widths(merged, out pages);
            Check(pages == 8 && widths.StartsWith("510x600 520x600 310x600"), "…relu : 8 pages, dans l'ordre de la liste (" + widths + ")");

            // ---------------------------------------------- pivoter
            await atelier.ChooseAsync(AtelierView.BoxPdfaah);
            await atelier.ChooseAsync(AtelierView.ToolRotate);
            await atelier.LoadJob(six);
            for (var i = 0; i < 30 && !atelier.LastPreviewDrawn; i++) await Settle();
            Check(atelier.LastPreviewDrawn, "Pivoter : l'aperçu montre la première page tournée");
            Shot(shell, "lot30-pivoter.png");
            var rotated = Path.Combine(folder, "pivote.pdf");
            Check(await atelier.RunRotate(1, new List<int> { 0, 2 }, rotated), "…les pages 1 et 3 tournées d'un quart de tour");
            widths = Widths(rotated, out pages);
            Check(pages == 6 && widths.StartsWith("600x310 320x600 600x330 340x600"), "…relu : elles sont couchées, les autres non (" + widths + ")");
            string problem;
            Check(PdfPages.Parse("1-3, 7", 6, out problem) == null && problem.Contains("6 pages") && PdfPages.Describe(PdfPages.Parse("5, 1-3,3", 6, out problem)) == "1-3, 5", "une liste de pages se lit (« 5, 1-3 ») et dit quand elle sort du PDF");

            // ---------------------------------------------- quitter avec du travail
            atelier.ChooseForProbe("atelier-rotate-turn", 2);
            await Settle();
            Check(atelier.PendingWork() == "« Pivoter »", "un réglage changé après l'enregistrement : il y a de nouveau du travail à perdre");
            var leaving = atelier.ChooseAsync(AtelierView.BoxPdfaah);
            Check(await Answer(leaving, "Non") && !leaving.Result && atelier.Tool == AtelierView.ToolRotate && atelier.JobForProbe != null, "quitter l'outil prévient que le travail sera perdu ; « Non » : on reste, le PDF est toujours là");
            leaving = atelier.ChooseAsync(AtelierView.BoxPdfaah);
            Check(await Answer(leaving, "Oui") && leaving.Result && atelier.Tool == AtelierView.BoxPdfaah && atelier.JobForProbe == null, "…« Oui » : on quitte, le PDF est retiré");
            // Quitter l'Atelier par la Pile.
            await atelier.ChooseAsync(AtelierView.ToolRotate);
            await atelier.LoadJob(six);
            atelier.ChooseForProbe("atelier-rotate-turn", 1);
            await Settle();
            shell.Binder.SelectItem(shell.Project.Category(Project.KeyWritings).Id);
            MessageDialog asked = null;
            for (var i = 0; i < 30 && asked == null; i++) { await Settle(); asked = OpenWindow<MessageDialog>(); }
            Check(asked != null && !atelier.IsVisible && atelier.JobForProbe != null, "quitter l'Atelier par la Pile avec du travail en cours : un avertissement, et tout est encore là");
            Press(asked, "Oui");
            for (var i = 0; i < 20 && !atelier.IsVisible; i++) await Settle();
            Check(atelier.IsVisible && atelier.Tool == AtelierView.ToolRotate && atelier.JobForProbe != null, "…« Oui » y ramène, le PDF et ses réglages intacts");
            shell.Binder.SelectItem(shell.Project.Category(Project.KeyWritings).Id);
            asked = null;
            for (var i = 0; i < 30 && asked == null; i++) { await Settle(); asked = OpenWindow<MessageDialog>(); }
            Press(asked, "Non");
            for (var i = 0; i < 20 && atelier.JobForProbe != null; i++) await Settle();
            Check(!atelier.IsVisible && atelier.JobForProbe == null, "…« Non » : le PDF est retiré");
            shell.ShowAtelier();
            await Settle();
            await Settle();

            // ---------------------------------------------- filigrane
            atelier.ShowForProbe(AtelierView.ToolWatermark);
            await atelier.LoadJob(six);
            for (var i = 0; i < 40 && !atelier.LastPreviewDrawn; i++) await Settle();
            Check(atelier.LastPreviewDrawn, "Filigrane : l'aperçu montre la page avec « BROUILLON » en travers");
            atelier.SetForProbe("atelier-mark-text", "ÉPREUVE non corrigée");
            for (var i = 0; i < 8; i++) await Settle();
            Shot(shell, "lot30-filigrane.png");
            var mark = atelier.WatermarkForProbe();
            mark.Opacity = 0.5;
            var marked = Path.Combine(folder, "filigrane.pdf");
            Check(await atelier.RunWatermark(mark, marked), "…posé sur toutes les pages (" + atelier.LastToolStatus + ")");
            var markedBytes = File.ReadAllBytes(marked);
            var plain = Pixels(sixBytes, null, 4, 500, out w, out h);
            var withMark = Pixels(markedBytes, null, 4, 500, out w2, out h2);
            var changed = Differences(plain, withMark);
            Check(Widths(marked, out pages).StartsWith("310x600") && pages == 6 && changed > 400, "…relu : 6 pages aux mêmes tailles ; la page 5 porte bien le texte (" + changed + " points changés)");
            // En travers : du texte dans le quart bas-gauche ET dans le quart haut-droit, rien dans les deux autres coins.
            Func<int, int, int> zone = delegate(int qx, int qy)
            {
                var n = 0;
                for (var y = qy * h / 4; y < (qy + 1) * h / 4; y++)
                    for (var x = qx * w / 4; x < (qx + 1) * w / 4; x++)
                    {
                        var i = (y * w + x) * 4;
                        if (Math.Abs(plain[i] - withMark[i]) + Math.Abs(plain[i + 1] - withMark[i + 1]) + Math.Abs(plain[i + 2] - withMark[i + 2]) > 24) n++;
                    }
                return n;
            };
            Check(zone(0, 3) + zone(1, 2) > 40 && zone(3, 0) + zone(2, 1) > 40 && zone(0, 0) == 0 && zone(3, 3) == 0, "…il monte du bas-gauche vers le haut-droit, et laisse les deux autres coins vides");
            mark = atelier.WatermarkForProbe();
            mark.Layout = Pdfium.Watermark.Head;
            mark.Pages = delegate(int index, int count) { return index == 0; };
            var headed = Path.Combine(folder, "filigrane-tete.pdf");
            await atelier.RunWatermark(mark, headed);
            var headedBytes = File.ReadAllBytes(headed);
            var top = Differences(Pixels(sixBytes, null, 0, 500, out w, out h), Pixels(headedBytes, null, 0, 500, out w2, out h2));
            Check(top > 20 && Differences(Pixels(sixBytes, null, 1, 500, out w, out h), Pixels(headedBytes, null, 1, 500, out w2, out h2)) == 0, "« en tête, la première page seulement » : la page 1 change, la page 2 pas d'un point");
            // Une page déjà tournée : le filigrane se lit toujours dans le sens de la page.
            var turned = Pdfium.Rotate(sixBytes, delegate(int index) { return 1; });
            var turnedMark = Pdfium.AddWatermark(turned, new Pdfium.Watermark { Text = "SENS", Layout = Pdfium.Watermark.Head, Size = 0.9, Opacity = 1 });
            var tp = Pixels(turned, null, 0, 600, out w, out h);
            var tm = Pixels(turnedMark, null, 0, 600, out w2, out h2);
            int upper = 0, lower = 0;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = (y * w + x) * 4;
                    if (Math.Abs(tp[i] - tm[i]) + Math.Abs(tp[i + 1] - tm[i + 1]) + Math.Abs(tp[i + 2] - tm[i + 2]) > 24) { if (y < h / 4) upper++; else lower++; }
                }
            Check(w > h && upper > 30 && lower == 0, "sur une page couchée, « en tête » reste en haut de ce qu'on voit (" + upper + " points en haut, " + lower + " ailleurs)");

            // ---------------------------------------------- signer
            atelier.ShowForProbe(AtelierView.ToolSign);
            await atelier.LoadJob(six);
            for (var i = 0; i < 30 && !atelier.LastPreviewDrawn; i++) await Settle();
            var pad = SignaturePadDialog.ForProbe(shell);
            var drawn = pad.DrawForProbe(new Point(60, 150), new Point(120, 60), new Point(180, 150), new Point(240, 70), new Point(330, 140));
            pad.Close();
            Check(drawn != null && drawn.Width > 200 && drawn.Width < 1100 && drawn.Height < 400 && drawn.Pixels.Length == drawn.Width * drawn.Height * 4, "le pavé de signature rend le tracé en image, rognée à ce qui est tracé (" + (drawn == null ? "-" : drawn.Width + " × " + drawn.Height) + ")");
            var inked = 0;
            var clear = 0;
            if (drawn != null) for (var i = 0; i + 3 < drawn.Pixels.Length; i += 4) { if (drawn.Pixels[i + 3] > 200) inked++; else if (drawn.Pixels[i + 3] == 0) clear++; }
            Check(inked > 300 && clear > inked * 3, "…à fond transparent : " + inked + " points d'encre, " + clear + " points vides");
            var stamp = new byte[80 * 40 * 4];
            for (var i = 0; i < stamp.Length; i += 4) { stamp[i] = 200; stamp[i + 1] = 30; stamp[i + 2] = 20; stamp[i + 3] = 255; } // BGRA : bleu
            atelier.SetSignatureForProbe(stamp, 80, 40);
            atelier.SetForProbe("atelier-sign-page", "2");
            for (var i = 0; i < 6; i++) await Settle();
            atelier.SetForProbe("atelier-sign-size", "40");
            atelier.PlaceSignature(100, 150);
            await Settle();
            Shot(shell, "lot30-signer.png");
            var signed = Path.Combine(folder, "signe.pdf");
            Check(await atelier.RunSign(signed), "Signer : la signature posée sur la page 2 (" + atelier.LastToolStatus + ")");
            var signedBytes = File.ReadAllBytes(signed);
            var page2 = Pixels(signedBytes, null, 1, 600, out w, out h); // 320 × 600 pt : 1 point = 1 pixel
            var centre = page2 == null ? -1 : (150 * w + 100) * 4;
            var outside = page2 == null ? -1 : (150 * w + 200) * 4;
            var original2 = Pixels(sixBytes, null, 1, 600, out w2, out h2);
            Check(page2 != null && page2[centre] > 150 && page2[centre + 2] < 90 && original2[outside] == page2[outside] && original2[outside + 1] == page2[outside + 1], "…relu : l'image est là où on l'a placée (son centre à 100 × 150 pt du coin haut-gauche), et pas à côté");
            Check(Differences(Pixels(sixBytes, null, 0, 400, out w, out h), Pixels(signedBytes, null, 0, 400, out w2, out h2)) == 0, "…les autres pages n'ont pas bougé");

            // ---------------------------------------------- compresser
            var heavy = Path.Combine(folder, "lourd.pdf");
            File.WriteAllBytes(heavy, ImagePdf());
            atelier.ShowForProbe(AtelierView.ToolCompress);
            await atelier.LoadJob(heavy);
            var lossless = await atelier.RunCompress(0);
            Check(lossless != null && lossless.Problem == null && lossless.After < lossless.Before && lossless.Deflated >= 1, "Compresser, sans perte : " + (lossless == null ? "-" : lossless.Before + " → " + lossless.After + " octets (" + lossless.Deflated + " flux compressé)"));
            var strong = await atelier.RunCompress(2);
            Check(strong != null && strong.Problem == null && atelier.LastDiet != null && atelier.LastDiet.Seen == 1 && atelier.LastDiet.Replaced == 1 && strong.After < strong.Before / 8, "…fort : l'image de 600 × 600 points, posée sur 100 × 100 pt, est réduite — " + (strong == null ? "-" : strong.Before + " → " + strong.After + " octets"));
            await Settle();
            Shot(shell, "lot30-compresser.png");
            var light = Path.Combine(folder, "leger.pdf");
            Check(await atelier.SaveCompressed(light) && atelier.PendingWork() == null, "…et s'enregistre");
            var lightBytes = File.ReadAllBytes(light);
            var heavyPixels = Pixels(File.ReadAllBytes(heavy), null, 0, 400, out w, out h);
            var lightPixels = Pixels(lightBytes, null, 0, 400, out w2, out h2);
            var far = 0;
            if (heavyPixels != null && lightPixels != null && heavyPixels.Length == lightPixels.Length)
                for (var i = 0; i + 3 < heavyPixels.Length; i += 4) if (Math.Abs(heavyPixels[i] - lightPixels[i]) + Math.Abs(heavyPixels[i + 1] - lightPixels[i + 1]) + Math.Abs(heavyPixels[i + 2] - lightPixels[i + 2]) > 90) far++;
            Check(lightPixels != null && w == w2 && far < w * h / 200, "…relu : même page, même image à l'œil (" + far + " points nettement différents sur " + (w * h) + ")");
            await atelier.LoadJob(six);
            var nothing = await atelier.RunCompress(0);
            Check(nothing != null && atelier.PendingWork() == null && nothing.After >= nothing.Before - Math.Max(2048, nothing.Before / 100), "un PDF déjà léger : c'est dit, et rien n'est proposé à l'enregistrement");

            // ---------------------------------------------- convertir
            atelier.ShowForProbe(AtelierView.ToolConvert);
            await atelier.LoadJob(six);
            await Settle();
            Shot(shell, "lot30-convertir.png");
            var images = Path.Combine(folder, "images");
            Directory.CreateDirectory(images);
            var count = await atelier.RunJpeg(images, 72, 85);
            await Settle();
            var files = Directory.GetFiles(images, "*.jpg");
            Array.Sort(files, StringComparer.Ordinal);
            byte[] jp;
            int jw = 0, jh = 0;
            var decoded = files.Length > 0 && PdfImages.Decode(File.ReadAllBytes(files[0]), out jp, out jw, out jh);
            Check(count == 6 && files.Length == 6 && Path.GetFileName(files[0]) == "six-pages-01.jpg" && decoded && jw == 310 && jh == 600, "Convertir en JPEG : six images, une par page, à 72 ppp (" + (files.Length > 0 ? Path.GetFileName(files[0]) + ", " + jw + " × " + jh : "-") + ")");
            var pig = Environment.GetEnvironmentVariable("MARABOOK_PDFPIG_FOLDER");
            if (!string.IsNullOrEmpty(pig) && Directory.Exists(pig))
            {
                Directory.CreateDirectory(PdfPigBridge.Folder);
                foreach (var file in Directory.GetFiles(pig)) File.Copy(file, Path.Combine(PdfPigBridge.Folder, Path.GetFileName(file)), true);
                Check(PdfPigBridge.Installed, "PdfPig posé dans le dossier d'essai, empreintes vérifiées");
                var docx = Path.Combine(folder, "vide.docx");
                Check(!await atelier.RunWord(docx) && !File.Exists(docx) && atelier.LastToolStatus.Contains("pas de texte"), "Convertir en Word un PDF sans texte : refusé, et c'est dit");
                var texted = Path.Combine(folder, "texte.pdf");
                File.WriteAllBytes(texted, TextPdf());
                await atelier.LoadJob(texted);
                docx = Path.Combine(folder, "texte.docx");
                var done = await atelier.RunWord(docx);
                var body = "";
                if (done && File.Exists(docx))
                    using (var zip = ZipFile.OpenRead(docx))
                    using (var reader = new StreamReader(zip.GetEntry("word/document.xml").Open(), Encoding.UTF8)) body = reader.ReadToEnd();
                Check(done && body.Contains("Bonjour") && body.Contains("Marabook"), "…un PDF de texte : le document Word porte son texte (" + atelier.LastToolStatus + ")");
            }
            else Console.WriteLine("  PdfPig absent (MARABOOK_PDFPIG_FOLDER) : « PDF vers Word » n'est pas joué.");

            // ---------------------------------------------- verrouiller, puis déverrouiller
            atelier.ShowForProbe(AtelierView.ToolLock);
            await atelier.LoadJob(six);
            await Settle();
            Shot(shell, "lot30-verrouiller.png");
            var locked = Path.Combine(folder, "verrouille.pdf");
            Check(!await atelier.RunLock("", locked) && !File.Exists(locked), "Verrouiller sans mot de passe : refusé");
            Check(await atelier.RunLock("Une phrase à moi, 2026", locked) && File.Exists(locked), "…avec un mot de passe : la copie s'écrit, après vérification (" + atelier.LastToolStatus + ")");
            using (var closed = Pdfium.Open(locked, null, out error)) Check(closed == null && error == Pdfium.ErrorPassword, "…elle ne s'ouvre pas sans lui");
            using (var open = Pdfium.Open(locked, "Une phrase à moi, 2026", out error)) Check(open != null && open.PageCount == 6 && open.Encrypted, "…et s'ouvre avec, sur ses 6 pages");
            atelier.ShowForProbe(AtelierView.ToolRotate);
            Check(!await atelier.LoadJob(locked) && atelier.JobForProbe == null && atelier.LastToolStatus.Contains("verrouillé"), "un PDF verrouillé donné à un autre outil : refusé, renvoi à l'Unlocker");
            atelier.ShowForProbe(AtelierView.ToolUnlocker);
            await atelier.RunUnlocker(locked, "Une phrase à moi, 2026");
            var free = Path.Combine(folder, "libre.pdf");
            Check(atelier.LastUnlockState == "verrouillé" && await atelier.SaveUnlocked(locked, free), "l'Unlocker rouvre la copie verrouillée par Marabook, avec son mot de passe");
            using (var open = Pdfium.Open(free, null, out error)) Check(open != null && !open.Encrypted && open.PageCount == 6, "…et en écrit une copie libre : la boucle est bouclée");
            atelier.CloseFilesForProbe();

            // ---------------------------------------------- Préférences › Dépendances
            var prefs = new PreferencesDialog(shell, shell.Project);
            prefs.Show(shell);
            await Settle();
            foreach (var entry in prefs.Tabs.Items) { var t = entry as TabItem; if (t != null && "Dépendances".Equals(t.Header)) prefs.Tabs.SelectedItem = t; }
            await Settle();
            var deps = Dependencies.Installed();
            var pdfium = deps.FirstOrDefault(delegate(Dependency d) { return d.Key == "pdfium"; });
            var card = prefs.GetVisualDescendants().OfType<Border>().FirstOrDefault(delegate(Border b) { return "dependency:pdfium".Equals(b.Tag); });
            var folderShown = card == null ? null : card.GetVisualDescendants().OfType<SelectableTextBlock>().FirstOrDefault(delegate(SelectableTextBlock t) { return "dependency-folder".Equals(t.Tag); });
            var loses = card == null ? null : card.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(delegate(TextBlock t) { return "dependency-loses".Equals(t.Tag); });
            Check(pdfium != null && card != null && Dependencies.SizeOf(pdfium.Folder) > 1024 * 1024 && folderShown != null && folderShown.Text == Pdfium.Folder && loses != null && loses.Inlines.Count == 2 && pdfium.Loses.Contains("Chop-Chop") && pdfium.Loses.Contains("filigrane"),
                "Préférences › Dépendances : PDFium, son poids (" + (pdfium == null ? "-" : Dependencies.SizeLabel(Dependencies.SizeOf(pdfium.Folder))) + "), son dossier, et ce qu'on perd à le retirer");
            Shot(prefs, "lot30-preferences-dependances.png");
            // Un PDF resté ouvert dans l'Atelier ne bloque pas la désinstallation : il est rendu d'abord.
            atelier.ShowForProbe(AtelierView.ToolChop);
            await atelier.ChopForProbe.Open(six);
            if (pdfium != null) prefs.RemoveDependency(pdfium);
            await Settle();
            Check(!Pdfium.Installed && prefs.DependencyStatusForProbe == "PDFium est désinstallé." && atelier.ChopForProbe.PageCountForProbe == 0 && !Dependencies.Installed().Any(delegate(Dependency d) { return d.Key == "pdfium"; }), "« Désinstaller » le retire (le PDF ouvert dans Chop-Chop est rendu d'abord) ; la liste ne le montre plus");
            prefs.Close();
            await Settle();
        }

        private static double Number0(string text)
        {
            double value;
            return double.TryParse((text ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        // ================================================================ des PDF d'essai

        /// <summary>Écrit un PDF à partir de ses objets (le n° 1 est le catalogue) ; « extra » : du lest que rien ne cite.</summary>
        private static byte[] RawPdf(List<byte[]> objects)
        {
            var latin = Encoding.GetEncoding(28591);
            var output = new MemoryStream();
            var offsets = new List<long>();
            Action<string> write = delegate(string text) { var bytes = latin.GetBytes(text); output.Write(bytes, 0, bytes.Length); };
            write("%PDF-1.4\n%âãÏÓ\n");
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                write((i + 1) + " 0 obj\n");
                output.Write(objects[i], 0, objects[i].Length);
                write("\nendobj\n");
            }
            var xref = output.Position;
            write("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
            foreach (var offset in offsets) write(offset.ToString("0000000000") + " 00000 n \n");
            write("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            return output.ToArray();
        }

        private static byte[] StreamObject(string dict, byte[] data)
        {
            var latin = Encoding.GetEncoding(28591);
            var buffer = new MemoryStream();
            var head = latin.GetBytes("<< " + dict + " /Length " + data.Length + " >>\nstream\n");
            buffer.Write(head, 0, head.Length);
            buffer.Write(data, 0, data.Length);
            var tail = latin.GetBytes("\nendstream");
            buffer.Write(tail, 0, tail.Length);
            return buffer.ToArray();
        }

        /// <summary>Trois pages au MÊME contenu, écrit trois fois sans compression, plus un gros objet que rien ne cite.</summary>
        private static byte[] BloatedPdf()
        {
            var latin = Encoding.GetEncoding(28591);
            var content = new StringBuilder();
            for (var i = 0; i < 400; i++) content.Append("0.").Append(i % 9 + 1).Append(" 0.3 0.6 rg ").Append(20 + i % 37 * 6).Append(" ").Append(20 + i % 53 * 9).Append(" 14 9 re f\n");
            var bytes = latin.GetBytes(content.ToString());
            var objects = new List<byte[]>();
            objects.Add(latin.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(latin.GetBytes("<< /Type /Pages /Count 3 /Kids [3 0 R 5 0 R 7 0 R] >>"));
            for (var i = 0; i < 3; i++)
            {
                objects.Add(latin.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 520] /Resources << >> /Contents " + (4 + 2 * i) + " 0 R >>"));
                objects.Add(StreamObject("", bytes));
            }
            var junk = new byte[24000];
            for (var i = 0; i < junk.Length; i++) junk[i] = (byte)('A' + i % 23);
            objects.Add(StreamObject("/Marabook /Lest", junk));
            return RawPdf(objects);
        }

        /// <summary>Une page portant une image RVB de 600 × 600 points, non compressée, posée sur 100 × 100 pt.</summary>
        private static byte[] ImagePdf()
        {
            var latin = Encoding.GetEncoding(28591);
            const int side = 600;
            var pixels = new byte[side * side * 3];
            for (var y = 0; y < side; y++)
                for (var x = 0; x < side; x++)
                {
                    var i = (y * side + x) * 3;
                    pixels[i] = (byte)(40 + x * 200 / side + (x * 7 + y * 13) % 11);
                    pixels[i + 1] = (byte)(60 + y * 150 / side + (x * 5 + y * 3) % 9);
                    pixels[i + 2] = (byte)(200 - x * 120 / side + (x + y) % 7);
                }
            var objects = new List<byte[]>();
            objects.Add(latin.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(latin.GetBytes("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"));
            objects.Add(latin.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Resources << /XObject << /Im1 5 0 R >> >> /Contents 4 0 R >>"));
            objects.Add(StreamObject("", latin.GetBytes("q 100 0 0 100 100 100 cm /Im1 Do Q\n0.8 0.1 0.1 rg 20 20 60 20 re f\n")));
            objects.Add(StreamObject("/Type /XObject /Subtype /Image /Width " + side + " /Height " + side + " /ColorSpace /DeviceRGB /BitsPerComponent 8", pixels));
            return RawPdf(objects);
        }

        /// <summary>Une page de vrai texte (Helvetica), pour la conversion vers Word.</summary>
        private static byte[] TextPdf()
        {
            var latin = Encoding.GetEncoding(28591);
            var objects = new List<byte[]>();
            objects.Add(latin.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(latin.GetBytes("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"));
            objects.Add(latin.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 595] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"));
            objects.Add(StreamObject("", latin.GetBytes("BT /F1 12 Tf 60 520 Td (Bonjour le monde, ceci est un texte de sonde.) Tj 0 -16 Td (Il tient sur deux lignes, pour Marabook.) Tj 0 -40 Td (Un second paragraphe vient ensuite, plus bas.) Tj ET\n")));
            objects.Add(latin.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
            return RawPdf(objects);
        }
    }
}

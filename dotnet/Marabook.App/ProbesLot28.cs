using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde du lot 28 (2.0.0, 21/11/2026) : la repasse du Tableau
    /// (largeur ajustée, fond lisible, défilement de côté), le bouton
    /// « Précédent » sur les écrans basculés, la sélection multiple de la Pile
    /// qui n'emporte plus le livre, les SKINS — et, en toute fin de sonde, le
    /// projet neuf qui s'ouvre dès sa création. MARABOOK_PROBE_ONLY=lot28.</summary>
    public static partial class Probes
    {
        private static async Task ProbeLot28(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- lot 28 : repasse du Tableau, « Précédent », sélection multiple, skins (2.0.0, 21/11)");
            if (!Skin.Plain) Console.WriteLine("  [lot28] lancé sous le skin " + Skin.Current + " : fond = " + (Skin.ResourceForProbe(Skin.BackdropKey) ?? "rien").GetType().Name + ", police = " + (Skin.UiFont == null ? "-" : shell.FontFamily.Name));
            var added = new List<BinderItem>();
            var outlinerShown = AppSettings.OutlinerShown;
            var libraryList = AppSettings.LibraryList;
            var skinBefore = AppSettings.Skin;
            var secondBefore = AppSettings.SkinAccent2;
            var darkBefore = AppSettings.DarkTheme;
            var rightBefore = AppSettings.RightPanel;
            try
            {
                var writings = project.Category(Project.KeyWritings);
                var book = new BinderItem { Kind = ItemKind.Book, Title = "Livre du lot 28 (sonde)" };
                book.Parent = writings;
                writings.Children.Add(book);
                added.Add(book);
                var chapters = new List<BinderItem>();
                for (var i = 1; i <= 4; i++)
                {
                    var chapter = SceneText("Chapitre " + i + " (lot 28)", "Un paragraphe du chapitre " + i + ".", "***", "Et sa seconde scène.");
                    chapter.Parent = book;
                    book.Children.Add(chapter);
                    chapters.Add(chapter);
                }
                shell.Binder.Rebuild();

                // ------------------------------------------------ le Tableau : largeur, fond, défilement de côté
                AppSettings.OutlinerShown = false;
                shell.Binder.SelectItem(book.Id);
                await Settle();
                await Settle();
                var texts = shell.BookViewForProbe.TextsForProbe;
                var toggle = texts.OutlineToggleForProbe;
                toggle.IsChecked = true;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                await Settle();
                var outliner = texts.OutlinerForProbe;
                Check(texts.OutlinerShownForProbe, "le Tableau est montré");
                Check(ReferenceEquals(outliner.TableBackgroundForProbe, Chrome.FieldBg) && !ReferenceEquals(outliner.TableBackgroundForProbe, Chrome.PaperBg),
                    "le fond du tableau est celui des champs, pas le papier (qui reste blanc sous « papier blanc malgré le mode sombre »)");
                var offered = outliner.OfferedWidthForProbe;
                var wanted = Math.Max(OutlinerView.FloorTotalForProbe, Math.Floor(offered));
                Check(Math.Abs(outliner.TableWidthForProbe - wanted) < 2, "le tableau prend la largeur offerte (" + outliner.TableWidthForProbe.ToString("0") + " px pour " + offered.ToString("0") + " offerts, plancher " + OutlinerView.FloorTotalForProbe.ToString("0") + ")");
                Shot(shell, "lot28-1-tableau-rail-ouvert.png");
                // Le rail se ferme : le tableau s'élargit, sans blanc à droite.
                shell.ToggleInspectorForProbe();
                for (var i = 0; i < 6; i++) await Settle();
                var wider = outliner.OfferedWidthForProbe;
                Check(wider > offered + 40 || AppSettings.RightPanel == rightBefore, "fermer le rail offre plus de place au centre (" + offered.ToString("0") + " → " + wider.ToString("0") + " px)");
                Check(Math.Abs(outliner.TableWidthForProbe - Math.Max(OutlinerView.FloorTotalForProbe, Math.Floor(wider))) < 2, "…et le tableau la prend toute : pas d'espace blanc à sa droite (" + outliner.TableWidthForProbe.ToString("0") + " px)");
                Shot(shell, "lot28-2-tableau-rail-ferme.png");
                shell.ToggleInspectorForProbe();
                for (var i = 0; i < 6; i++) await Settle();
                // Trop étroit : il défile de côté, à Maj + molette, le pointeur n'importe où dessus.
                outliner.Width = 640;
                await Settle();
                await Settle();
                Check(outliner.TableWidthForProbe >= OutlinerView.FloorTotalForProbe - 1 && outliner.OfferedWidthForProbe < OutlinerView.FloorTotalForProbe, "sous ses planchers, le tableau garde ses colonnes et déborde (" + outliner.OfferedWidthForProbe.ToString("0") + " px offerts)");
                var cell = outliner.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(delegate(TextBlock block) { return "outliner-title".Equals(block.Tag); });
                if (cell != null)
                {
                    var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
                    var wheel = new PointerWheelEventArgs(cell, pointer, shell, new Point(4, 4), 0, new PointerPointProperties(RawInputModifiers.Shift, PointerUpdateKind.Other), Avalonia.Input.KeyModifiers.Shift, new Vector(0, -1))
                    { RoutedEvent = InputElement.PointerWheelChangedEvent };
                    cell.RaiseEvent(wheel);
                    await Settle();
                    Check(outliner.SidewaysOffsetForProbe > 10 && wheel.Handled, "Maj + molette sur une cellule fait défiler le tableau de côté (" + outliner.SidewaysOffsetForProbe.ToString("0") + " px)");
                }
                else Check(false, "une cellule de titre pour la molette");
                outliner.Width = double.NaN;
                await Settle();

                // ------------------------------------------------ « Précédent » rembobine les écrans basculés
                toggle.IsChecked = false;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                toggle.IsChecked = true;
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Settle();
                Check(texts.OutlinerShownForProbe, "le Tableau est de nouveau montré, par un clic");
                shell.NavigateBackForProbe();
                await Settle();
                Check(!texts.OutlinerShownForProbe && !AppSettings.OutlinerShown && shell.CurrentForProbe == book, "« Précédent » sur le Tableau ramène aux cartes, sans quitter le livre");
                shell.NavigateForwardForProbe();
                await Settle();
                Check(texts.OutlinerShownForProbe && shell.CurrentForProbe == book, "« Suivant » rend le Tableau");
                shell.NavigateBackForProbe();
                await Settle();
                Check(!texts.OutlinerShownForProbe, "…et « Précédent » le retire encore");

                var sheets = project.Category(Project.KeySheets);
                shell.Binder.SelectItem(sheets.Id);
                await Settle();
                await Settle();
                var library = shell.SheetLibraryForProbe;
                if (library.IsVisible)
                {
                    var listToggle = library.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault(delegate(ToggleButton button) { return "library-list".Equals(button.Tag); });
                    var listBefore = AppSettings.LibraryList;
                    if (listToggle != null)
                    {
                        listToggle.IsChecked = !listBefore;
                        listToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Settle();
                        Check(AppSettings.LibraryList == !listBefore, "la bibliothèque des fiches bascule entre tuiles et liste");
                        shell.NavigateBackForProbe();
                        await Settle();
                        Check(AppSettings.LibraryList == listBefore && shell.CurrentForProbe == sheets, "« Précédent » rend l'écran d'avant de la bibliothèque, sans la quitter");
                        shell.NavigateBackForProbe();
                        await Settle();
                        Check(shell.CurrentForProbe == book, "…puis, au cran suivant, ramène au livre");
                    }
                    else Check(false, "le bouton de liste de la bibliothèque");
                }
                BinderItem frise = null;
                foreach (var item in project.AllItems()) if (item.Kind == ItemKind.Timeline && item.RootCategory() != null && item.RootCategory().CategoryKey != Project.KeyTrash) { frise = item; break; }
                if (frise != null)
                {
                    shell.Binder.SelectItem(frise.Id);
                    await Settle();
                    await Settle();
                    var timeline = shell.TimelineForProbe;
                    if (timeline != null && timeline.IsVisible)
                    {
                        timeline.ChooseMode(0);
                        timeline.ChooseMode(2);
                        await Settle();
                        Check(timeline.ListMode, "une frise passe en Liste");
                        shell.NavigateBackForProbe();
                        await Settle();
                        Check(!timeline.ListMode && !timeline.CalendarMode && shell.CurrentForProbe == frise, "« Précédent » rend la Frise, sans quitter l'élément");
                    }
                }

                // ------------------------------------------------ la sélection multiple n'emporte plus le livre
                shell.Binder.SelectItem(book.Id);
                await Settle();
                await Settle();
                shell.Binder.ClearMultiSelection();
                var ranged = shell.Binder.RangeMultiForProbe(chapters[2].Id);
                var rangeLot = shell.Binder.MultiItems();
                Check(ranged && rangeLot.Count == 3 && !rangeLot.Contains(book) && rangeLot[0] == chapters[0] && rangeLot[2] == chapters[2], "MAJ+clic du livre ouvert à son troisième chapitre : trois chapitres, sans le livre (" + string.Join(" / ", rangeLot.ConvertAll(delegate(BinderItem i) { return i.Title; }).ToArray()) + ")");
                shell.Binder.ClearMultiSelection();
                shell.Binder.ToggleMultiForProbe(chapters[0].Id);
                var lot = shell.Binder.MultiItems();
                Check(lot.Count == 1 && lot[0] == chapters[0], "le livre ouvert, Ctrl+clic sur un chapitre : le lot, c'est ce chapitre — pas le livre qui le contient (" + lot.Count + ")");
                shell.Binder.ToggleMultiForProbe(chapters[1].Id);
                lot = shell.Binder.MultiItems();
                Check(lot.Count == 2 && !lot.Contains(book), "…un second : deux chapitres, toujours sans le livre");
                shell.Binder.DeleteMany(shell.Binder.MultiItems());
                await Settle();
                Check(book.Parent == writings && book.Children.Count == 2 && chapters[0].Parent == project.Trash && chapters[1].Parent == project.Trash && chapters[2].Parent == book,
                    "« Envoyer à la corbeille » n'envoie que les deux chapitres cochés : le livre et ses autres chapitres restent");
                Check(shell.UndoForProbe() && book.Children.Count == 4, "Ctrl+Z les rend");
                await Settle();
                shell.Binder.Rebuild();
                shell.Binder.SelectItem(book.Id);
                await Settle();
                shell.Binder.ClearMultiSelection();
                // Un lot d'UNE ligne (le livre écarté) : Suppr vise le lot, jamais la ligne ouverte.
                shell.Binder.ToggleMultiForProbe(chapters[3].Id);
                Check(shell.Binder.MultiCountForProbe == 1, "un lot d'une seule ligne existe (le livre ouvert n'y entre plus)");
                shell.Binder.ClearMultiSelection();

                // ------------------------------------------------ la ligne choisie d'une liste, survolée, au sombre
                await ProbeSelectedHover(shell);

                // ------------------------------------------------ les skins
                await ProbeSkins(shell, project, book);
            }
            finally
            {
                AppSettings.Skin = skinBefore;
                AppSettings.SkinAccent2 = secondBefore;
                AppSettings.DarkTheme = darkBefore;
                shell.ApplyAppearanceForProbe();
                AppSettings.OutlinerShown = outlinerShown;
                AppSettings.LibraryList = libraryList;
                var home = project.Category(Project.KeyWritings);
                shell.Binder.ClearMultiSelection();
                shell.Binder.SelectItem(home.Id);
                await Settle();
                foreach (var item in added)
                    if (item != null && item.Parent != null) item.Parent.Children.Remove(item);
                shell.Binder.Rebuild();
                await Settle();
            }
        }

        /// <summary>« Nouveau projet » au sombre (21/11, Rémi) : la ligne choisie, sous le pointeur
        /// et le clavier dedans, ne passe plus à l'accent FORT (clair au sombre, sous une encre
        /// claire). Les états sont posés à la main sur la ligne : la sonde n'a pas de souris.</summary>
        private static async Task ProbeSelectedHover(MainWindow shell)
        {
            var wasDark = AppSettings.DarkTheme;
            AppSettings.DarkTheme = true;
            shell.ApplyAppearanceForProbe();
            var list = new ListBox { Width = 320 };
            list.Items.Add(new TextBlock { Text = "Projet vide", Foreground = Chrome.Ink });
            list.Items.Add(new TextBlock { Text = "Un modèle", Foreground = Chrome.Ink });
            list.SelectedIndex = 0;
            var window = new Window { Width = 360, Height = 160, Content = list, ShowInTaskbar = false, Background = Chrome.RaisedBg };
            window.Show(shell);
            try
            {
                await Settle();
                var line = list.ContainerFromIndex(0) as ListBoxItem;
                var presenter = line == null ? null : line.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault(delegate(Avalonia.Controls.Presenters.ContentPresenter p) { return p.Name == "PART_ContentPresenter"; });
                if (line == null || presenter == null) { Check(false, "la ligne choisie d'une liste et son fond"); return; }
                ((IPseudoClasses)line.Classes).Set(":pointerover", true);
                ((IPseudoClasses)line.Classes).Set(":focus", true);
                await Settle();
                var background = presenter.Background as ISolidColorBrush;
                var ink = Chrome.Ink.Color;
                var contrast = background == null ? 0 : Math.Abs(Chrome.Luma(ink) - Chrome.Luma(background.Color));
                Check(ReferenceEquals(presenter.Background, Chrome.AccentSoft) && !ReferenceEquals(presenter.Background, Chrome.AccentStrong),
                    "au sombre, la ligne choisie d'une liste, survolée, reste sur l'accent doux — plus sur l'accent fort");
                Check(contrast > 90, "…et son texte reste lisible dessus (écart de clarté " + contrast.ToString("0") + " sur 255)");
                var strong = Math.Abs(Chrome.Luma(ink) - Chrome.Luma(Chrome.AccentStrong.Color));
                Check(strong < contrast, "…ce que l'accent fort ne permettait pas (écart " + strong.ToString("0") + ")");
                Shot(window, "lot28-3-ligne-choisie-survolee.png");
            }
            finally
            {
                window.Close();
                AppSettings.DarkTheme = wasDark;
                shell.ApplyAppearanceForProbe();
                await Settle();
            }
        }

        private static async Task ProbeSkins(MainWindow shell, Project project, BinderItem book)
        {
            var groundBefore = Skin.ResourceForProbe(Skin.GroundKey);
            Check(Skin.Plain && ReferenceEquals(groundBefore, Chrome.WindowBg) && ReferenceEquals(Skin.ResourceForProbe(Skin.BarKey), Chrome.BarBg) && ReferenceEquals(Skin.ResourceForProbe(Skin.BackdropKey), Chrome.WindowBg),
                "« Marabook modern » : les fonds sont les pinceaux de toujours (fenêtre, barres), rien n'est habillé");
            var inkBefore = Chrome.Ink.Color;
            var windowBefore = Chrome.WindowBg.Color;

            BinderItem sheet = FirstTitled(project, "Keira");
            var prefs = new PreferencesDialog(shell, project);
            prefs.AppearanceChanged += shell.ApplyAppearanceForProbe;
            prefs.Show(shell);
            await Settle();
            prefs.Tabs.SelectedIndex = 1;
            await Settle();
            Check(prefs.SkinCardsForProbe == 9 && !prefs.SecondRowShownForProbe, "Préférences propose neuf skins ; pas de seconde couleur pour « modern »");
            Border card = null;
            foreach (var border in prefs.GetVisualDescendants().OfType<Border>()) if ("skin:modern".Equals(border.Tag)) card = border;
            Check(card != null && Math.Abs(card.CornerRadius.TopLeft - 8) < 0.01, "une carte de skin a ses coins de 8 px sous « modern »");
            Shot(prefs, "skin-modern-preferences.png");

            foreach (var id in new[] { Skins.Classic, Skins.Gradient, Skins.Eden, Skins.Cyberpunk })
            {
                if (id != Skins.Cyberpunk) { AppSettings.DarkTheme = false; }
                prefs.SetSkin(id);
                for (var i = 0; i < 4; i++) await Settle();
                Check(Skin.Current == id && AppSettings.Skin == id, "« " + Skins.Label(id) + " » est choisi et retenu");
                var backdrop = Skin.ResourceForProbe(Skin.BackdropKey);
                var ground = Skin.ResourceForProbe(Skin.GroundKey);
                if (id == Skins.Classic)
                {
                    Check(ReferenceEquals(ground, Chrome.WindowBg) && Chrome.WindowBg.Color == Color.FromRgb(0x80, 0x80, 0x80) && Chrome.BarBg.Color == Color.FromRgb(0xEC, 0xE9, 0xD8), "…gris d'atelier et beige de 2005, un aplat (pas de fond peint)");
                    Check(Math.Abs(Skin.Round(8)) < 0.01, "…des angles droits");
                }
                else if (id == Skins.Gradient)
                {
                    Check(backdrop is LinearGradientBrush && ReferenceEquals(ground, Brushes.Transparent), "…un dégradé derrière toute la fenêtre ; les vues du centre le laissent voir");
                    Check(prefs.SecondRowShownForProbe, "…la seconde couleur du dégradé se choisit (ou reste automatique)");
                    var auto = Skin.Second;
                    prefs.SetSecond("#16A085");
                    await Settle();
                    Check(AppSettings.SkinAccent2 == "#16A085" && Skin.Second == Color.FromRgb(0x16, 0xA0, 0x85) && Skin.Second != auto, "…choisie : le dégradé va de l'accent à cette couleur");
                    prefs.SetSecond(null);
                    await Settle();
                    Check(AppSettings.SkinAccent2 == null && Skin.Second == auto, "…« Automatique » rend la voisine de l'accent (" + Chrome.Hex(auto) + ")");
                }
                else if (id == Skins.Eden)
                {
                    Check(Skin.TextureFoundForProbe ? backdrop is ImageBrush : ReferenceEquals(backdrop, Chrome.WindowBg), Skin.TextureFoundForProbe ? "…le vieux papier de l'accueil derrière toute la fenêtre" : "…pas de texture à côté de l'exécutable : un aplat de papier");
                    Check(Chrome.Ink.Color == Color.FromRgb(0x3B, 0x2A, 0x1A) && Skin.UiFont != null, "…une encre sépia, des caractères à empattements");
                }
                else
                {
                    Check(AppSettings.DarkTheme && Chrome.Dark, "…sombre par défaut : le choisir passe l'application au sombre");
                    Check(backdrop is LinearGradientBrush && Chrome.WindowBg.Color == Color.FromRgb(0x07, 0x06, 0x0D), "…un fond de nuit");
                    Check(prefs.SecondRowShownForProbe, "…la seconde couleur des néons se choisit aussi");
                }
                card = null;
                foreach (var border in prefs.GetVisualDescendants().OfType<Border>()) if (("skin:" + id).Equals(border.Tag)) card = border;
                Check(card != null && Math.Abs(card.CornerRadius.TopLeft - Skin.Round(8)) < 0.01, "…les cadres faits main suivent (coins de " + Skin.Round(8).ToString("0.#") + " px)");
                var preview = prefs.GetVisualDescendants().OfType<Border>().FirstOrDefault(delegate(Border border) { return "skin-preview".Equals(border.Tag); });
                Check(preview != null && preview.Background != null, "…les vignettes restent dessinées");
                Shot(prefs, "skin-" + id + "-preferences.png");
                prefs.Hide();
                shell.Binder.SelectItem(book.Id);
                await Settle();
                await Settle();
                Shot(shell, "skin-" + id + "-livre.png");
                if (sheet != null)
                {
                    shell.Binder.SelectItem(sheet.Id);
                    await Settle();
                    await Settle();
                    Shot(shell, "skin-" + id + "-fiche.png");
                }
                shell.Binder.SelectItem(book.Children[0].Id);
                await Settle();
                await Settle();
                Shot(shell, "skin-" + id + "-ecrit.png");
                var pdf = Skin.PdfSkin();
                Check(pdf != null && (id != Skins.Eden || !Skin.TextureFoundForProbe || pdf.TextureRgb != null), "…le « PDF Encyclopédique » a son papier" + (id == Skins.Eden && pdf != null && pdf.TextureRgb != null ? " (la texture, " + pdf.TextureWidth + " × " + pdf.TextureHeight + ")" : ""));
                var options = Skin.DressExport(new EncyclopediaOptions());
                Check(options.Skin == id && (id != Skins.Eden || !Skin.TextureFoundForProbe || (options.SkinTexture != null && options.SkinTexture.Length > 1000)), "…l'Encyclopédie en HTML part à ce skin");
            prefs.Show(shell);
                await Settle();
            }

            // L'export de l'Encyclopédie, pour de vrai, sous le dernier skin (Cyberpunk).
            var folder = Path.Combine(Path.GetTempPath(), "marabook-sonde-skin-" + Guid.NewGuid().ToString("N"));
            try
            {
                var written = shell.ExportEncyclopediaForProbe(folder);
                var css = Path.Combine(folder, "style.css");
                string cssText = null;
                foreach (var file in Directory.GetFiles(folder, "*.css", SearchOption.AllDirectories)) cssText = File.ReadAllText(file);
                Check(written > 0 && cssText != null && cssText.Contains("@media screen{") && cssText.Contains("text-shadow"), "l'Encyclopédie exportée porte les néons du skin dans sa feuille de style (" + written + " fichiers)");
            }
            finally { try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { } }

            // Retour à « modern » : tout redevient ce que c'était.
            AppSettings.DarkTheme = false;
            prefs.SetSkin(Skins.Modern);
            for (var i = 0; i < 4; i++) await Settle();
            Check(Skin.Plain && AppSettings.Skin == null && ReferenceEquals(Skin.ResourceForProbe(Skin.GroundKey), Chrome.WindowBg) && ReferenceEquals(Skin.ResourceForProbe(Skin.BackdropKey), Chrome.WindowBg),
                "retour à « Marabook modern » : les fonds redeviennent les pinceaux de toujours");
            Check(Chrome.Ink.Color == inkBefore && Chrome.WindowBg.Color == windowBefore, "…la palette aussi");
            card = null;
            foreach (var border in prefs.GetVisualDescendants().OfType<Border>()) if ("skin:modern".Equals(border.Tag)) card = border;
            Check(card != null && Math.Abs(card.CornerRadius.TopLeft - 8) < 0.01 && Skin.PdfSkin() == null && Skin.DressExport(new EncyclopediaOptions()).Skin == "", "…les coins retrouvent leurs 8 px ; les exports ne sont plus habillés");
            Shot(prefs, "skin-modern-retour.png");
            prefs.Close();
            await Settle();
            shell.Binder.SelectItem(book.Id);
            await Settle();
            Shot(shell, "skin-modern-livre.png");
        }

        /// <summary>EN DERNIER (il change de projet) : un projet neuf, créé depuis
        /// l'accueil, s'ouvre dès qu'il est écrit — vide ou né d'un modèle.</summary>
        private static async Task ProbeNewProject(MainWindow shell)
        {
            Console.WriteLine("-- lot 28 : le projet neuf s'ouvre à sa création (21/11)");
            var folder = Path.Combine(Path.GetTempPath(), "marabook-sonde-neuf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var previousFolder = ProjectTemplates.FolderOverride;
            ProjectTemplates.FolderOverride = Path.Combine(folder, "modeles");
            try
            {
                var path = Path.Combine(folder, "Neuf" + PlotFile.Extension);
                var created = await shell.CreateProjectAt(Project.CreateNew(), path);
                await Settle();
                Check(created && shell.HasProjectPath && File.Exists(path) && new FileInfo(path).Length > 0, "un projet vide créé à un endroit s'ouvre aussitôt : écrit sur le disque, c'est lui qui est ouvert");
                Check(shell.ProjectPathForPreferences == path && !shell.DirtyForProbe, "…à son chemin, sans rien à enregistrer");

                // Né d'un modèle (avec un livre et un mot du dictionnaire).
                var source = Project.CreateNew();
                var writings = source.Category(Project.KeyWritings);
                var book = new BinderItem { Kind = ItemKind.Book, Title = "Tome secret", Book = new BookInfo() };
                book.Parent = writings;
                writings.Children.Add(book);
                var text = SceneText("Chapitre confidentiel", "Rien de ceci ne doit suivre.");
                text.Parent = book;
                book.Children.Add(text);
                source.Lexicon.Add(new LexiconEntry { Word = "Ehnmir" });
                var template = ProjectTemplates.Save(source, "Modèle de la sonde 28", true);
                var born = ProjectTemplates.Instantiate(template);
                var path2 = Path.Combine(folder, "Du modele" + PlotFile.Extension);
                // Le sélecteur de fichier peut avoir posé un fichier vide à cet endroit.
                File.WriteAllBytes(path2, new byte[0]);
                created = await shell.CreateProjectAt(born, path2);
                await Settle();
                Check(created && shell.HasProjectPath && shell.ProjectPathForPreferences == path2 && new FileInfo(path2).Length > 0, "un projet né d'un modèle s'ouvre aussitôt lui aussi (même si le sélecteur avait posé un fichier vide)");
                var opened = shell.Project;
                var openedWritings = opened.Category(Project.KeyWritings);
                Check(openedWritings.Children.Count == 1 && openedWritings.Children[0].Kind == ItemKind.Book && openedWritings.Children[0].Title == "Nouveau livre" && openedWritings.Children[0].Children.Count == 0
                    && FirstTitled(opened, "Chapitre confidentiel") == null && FirstTitled(opened, "Tome secret") == null,
                    "…avec un « Nouveau livre » vide : ni écrit, ni titre, ni nom de livre du projet d'origine");
                Check(opened.Lexicon.Count == 1 && opened.Lexicon[0].Word == "Ehnmir", "…et le dictionnaire du projet, puisque le modèle a été enregistré avec");
                var reread = PlotFile.Load(path2);
                Check(reread.Category(Project.KeyWritings).Children.Count == 1 && reread.Lexicon.Count == 1, "…relu du disque, il est bien celui-là");
            }
            finally
            {
                ProjectTemplates.FolderOverride = previousFolder;
                await shell.SaveCompletion();
                try { Directory.Delete(folder, true); } catch { }
            }
        }
    }
}

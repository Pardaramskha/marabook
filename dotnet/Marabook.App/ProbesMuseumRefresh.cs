using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Sonde de la REPASSE DES SKINS « MUSEUM » (2.0.0, 24/11/2026 au soir) : pour chaque
    /// skin de la famille, la couleur du survol et du « validé », la sélection en pilule (ou la
    /// barre d'« Ascension »), le menu contextuel à coins coupés, la silhouette galbée des grands
    /// dialogues, et le NOUVEL ACCUEIL (anneau du jour, carte du sprint, livres à jalons).
    /// Appelée par ProbeMuseum, Préférences ouvertes, sous le skin en question.</summary>
    public static partial class Probes
    {
        private static async Task ProbeMuseumRefresh(MainWindow shell, PreferencesDialog prefs, Project project, BinderItem book, string id, string name)
        {
            var ascent = id == Skins.MuseumAscent;

            // — Les valeurs d'origine : 65 % de verre, 15 px de flou.
            Check(Skins.MuseumGlassFor(id, AppSettings.SkinGlass) == 65 && Skins.MuseumBlurFor(id, AppSettings.SkinBlur) == 15, "…65 % d'effet verre et 15 px de flou d'origine");
            Check(Math.Abs(ModalChrome.BlurOfForProbe(shell) - 15) < 0.01, "…ce qui est derrière les Préférences est flouté de 15 px");

            // — Le survol et le « validé » : des couleurs du skin.
            Check(Chrome.Hover.Color != Chrome.Accent.Color && Chrome.ContrastRatio(Chrome.Hover.Color, Chrome.BarBg.Color) >= 3, "…le survol a sa couleur, qui ne suit pas l'accent (" + Chrome.Hex(Chrome.Hover.Color) + ")");
            Check(Chrome.ContrastRatio(Chrome.Ok.Color, Chrome.BarBg.Color) >= 3, "…le « validé » du skin se lit sur les barres (" + Chrome.Hex(Chrome.Ok.Color) + ", " + Chrome.ContrastRatio(Chrome.Ok.Color, Chrome.BarBg.Color).ToString("0.0") + ":1)");
            Check(Skin.Caps == ascent && Skin.TitleText("Préférences") == (ascent ? "PRÉFÉRENCES" : "Préférences"), ascent ? "…ses titres sont en capitales" : "…ses titres gardent leur casse");
            Check(Math.Abs(Skin.Round(8) - (ascent ? 0 : Skin.Round(8))) < 0.01 && (!ascent || Skin.Round(12) < 0.01), ascent ? "…angles droits partout" : "…ses coins sont ceux du dessin");

            // — La silhouette galbée des Préférences.
            Control galbe = null, frame = null;
            TextBlock title = null;
            foreach (var visual in prefs.GetVisualDescendants())
            {
                var control = visual as Control;
                if (control == null) continue;
                if ("modal-galbe".Equals(control.Tag)) galbe = control;
                if ("modal-frame".Equals(control.Tag)) frame = control;
                if ("modal-title".Equals(control.Tag)) title = control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
            }
            var moon = prefs.GetVisualDescendants().OfType<MoonFrame>().FirstOrDefault();
            var sky = prefs.GetVisualDescendants().OfType<Planetarium>().FirstOrDefault();
            if (ascent)
                Check(galbe is GalbeFrame && galbe.IsVisible && galbe.Bounds.Width > 400 && (moon == null || !moon.IsVisible) && ModalChrome.WidenedForProbe(prefs) < 0.5, "…les Préférences prennent la silhouette galbée (pas de lune sous « Ascension »)");
            else
            {
                var extra = MoonFrame.Extra(prefs.Bounds.Height);
                Check(moon != null && moon.IsVisible && (galbe == null || !galbe.IsVisible), "…les Préférences prennent la LUNE, à la place de la silhouette galbée");
                Check(ModalChrome.WidenedForProbe(prefs) > 20 && frame is Border && ((Border)frame).Padding.Right >= ModalChrome.WidenedForProbe(prefs), "…elles s'élargissent de ce que la lune déborde (" + ModalChrome.WidenedForProbe(prefs).ToString("0") + " px sur " + extra.ToString("0") + "), le contenu reste dans le corps");
                Check(sky != null && sky.IsVisible && sky.RunningForProbe && sky.Bounds.Width > 20 && sky.Bounds.Width < prefs.Bounds.Width / 3 && Canvas.GetLeft(sky) >= MoonFrame.BodyWidth(prefs.Bounds.Size) - 1.5,
                    "…le planétarium tourne dans la fente, et ne repeint que ce coin (" + sky.Bounds.Width.ToString("0") + " × " + sky.Bounds.Height.ToString("0") + " px)");
                AppSettings.Animations = false;
                Check(!Planetarium.Animated, "…animations coupées : le planétarium se fige");
                AppSettings.Animations = true;
            }
            var framed = frame as Border;
            Check(framed != null && framed.BorderThickness.Left < 0.01 && framed.Padding.Right >= GalbeFrame.Dent, "…leur contenu se tient en retrait du renfoncement (" + (framed == null ? 0 : framed.Padding.Right).ToString("0") + " px à droite)");
            Check(title != null && title.FontFamily == Skin.TitleFont && title.Text == Skin.TitleText(prefs.Title), "…leur titre est dans la police des titres (« " + (title == null ? "" : title.Text) + " »)");
            Check(GalbeFrame.Outline(new Size(900, 660), 0).Bounds.Width > 850 && GalbeFrame.Outline(new Size(360, 240), 0).Bounds.Height > 200, "…le tracé tient pour un grand et pour un petit dialogue");

            // — Le dialogue de sprint : une fenêtre classique (25/11), le panneau droit des autres dialogues.
            var sprint = SprintDialog.NewForProbe(prefs);
            sprint.Show(prefs);
            for (var i = 0; i < 3; i++) await Settle();
            var sprintGalbe = sprint.GetVisualDescendants().OfType<GalbeFrame>().FirstOrDefault();
            var sprintMoon = sprint.GetVisualDescendants().OfType<MoonFrame>().FirstOrDefault();
            Check(sprintGalbe == null && sprintMoon == null && ModalChrome.DressedForProbe(sprint) && sprint.Bounds.Width > 300 && sprint.Bounds.Width < 480, "…« Lancer un sprint » est une fenêtre classique : ni silhouette galbée ni lune, à la taille de son contenu (" + sprint.Bounds.Width.ToString("0") + " px)");
            Shot(sprint, "museum-" + id + "-sprint.png");
            sprint.Close();
            await Settle();
            await Settle();

            // — Une fenêtre ordinaire garde le panneau rectangulaire.
            var plainWindow = new Window { Title = "Un message", Width = 320, Height = 120, CanResize = false, Background = Chrome.RaisedBg, Content = new TextBlock { Text = "Un essai.", Margin = new Thickness(16) } };
            plainWindow.Show(prefs);
            await Settle();
            await Settle();
            Check(!plainWindow.GetVisualDescendants().OfType<GalbeFrame>().Any(), "…un petit message garde son panneau droit");
            plainWindow.Close();
            await Settle();
            await Settle();

            // — Le menu contextuel : coins coupés et double filet.
            var menu = book == null ? null : shell.Binder.BuildContextMenu(book);
            if (menu != null)
            {
                menu.PlacementTarget = shell.Binder;
                menu.Open(shell.Binder);
                for (var i = 0; i < 3; i++) await Settle();
                var cut = menu.GetVisualDescendants().OfType<CutFrame>().FirstOrDefault();
                Check(cut != null && cut.Bounds.Width > 80 && menu.GetVisualDescendants().OfType<MenuItem>().Any(), "…le menu contextuel a ses coins coupés et son second filet, ses entrées dedans");
                if (cut != null) Shot(cut, "museum-" + id + "-menu.png");
                menu.Close();
                await Settle();
            }

            // — La sélection de la Pile : une pilule (une barre sous « Ascension »).
            prefs.Hide();
            await Settle();
            var home = project.Category(Project.KeyHome);
            Border selection = null;
            if (home != null)
            {
                shell.Binder.SelectItem(home.Id);
                for (var i = 0; i < 3; i++) await Settle();
                foreach (var item in shell.Binder.GetVisualDescendants().OfType<TreeViewItem>())
                    if (item.IsSelected) selection = item.GetVisualDescendants().OfType<Border>().FirstOrDefault(delegate(Border border) { return border.Name == "SelectionBorder"; });
            }
            if (ascent)
                Check(selection != null && selection.BorderThickness.Left >= 2 && selection.BorderThickness.Top < 0.01, "…la ligne choisie de la Pile garde une barre d'accent à gauche");
            else
                Check(selection != null && selection.CornerRadius.TopLeft >= 12 && selection.BorderBrush is RadialGradientBrush && selection.BoxShadow.Count == 2,
                    "…la ligne choisie de la Pile est une pilule de verre, à reflet et à lueur (coins de " + (selection == null ? 0 : selection.CornerRadius.TopLeft).ToString("0") + " px)");
            var pearl = selection == null ? null : selection.GetVisualDescendants().OfType<PillPearl>().FirstOrDefault();
            if (ascent) Check(pearl != null && pearl.Strength >= 1 && selection.Background is LinearGradientBrush, "…et, sous « Ascension », un dégradé d'or et un losange à cheval sur la barre");
            else
            {
                Check(pearl != null && pearl.Strength > 0.99 && PillPearl.SizeForProbe >= 8 && Math.Abs(pearl.CenterInBandForProbe.X) < 1 && Math.Abs(selection.Margin.Left - PillPearl.Room) < 0.01,
                    "…sa perle (" + PillPearl.SizeForProbe.ToString("0.#") + " px) a son centre SUR le bord gauche de la pilule, qui lui laisse " + PillPearl.Room + " px de place");
                // Le bouton principal : métal sombre à liseré.
                var primary = shell.GetVisualDescendants().OfType<Button>().FirstOrDefault(delegate(Button button) { return button.Classes.Contains(Theme.Primary) && button.IsEffectivelyVisible; });
                Check(primary != null && primary.BorderBrush is LinearGradientBrush && primary.Background is LinearGradientBrush && Chrome.Luma(((LinearGradientBrush)primary.Background).GradientStops[0].Color) < 110
                    && primary.Foreground is ISolidColorBrush && Chrome.ContrastRatio(((ISolidColorBrush)primary.Foreground).Color, ((LinearGradientBrush)primary.Background).GradientStops[1].Color) >= 4.5,
                    "…le bouton principal est de métal sombre à liseré, son texte se lit dessus");
                Check(Skin.TabFill() != null && Skin.TabRim() is RadialGradientBrush, "…l'onglet choisi garde le verre sombre, bordé de métal");
            }
            Check(Chrome.TabGround.Color == Chrome.BorderStrong.Color && Chrome.TabEdge.Color == Chrome.Accent.Color, "…le filet où s'emboîtent les onglets et le contour de l'onglet actif sont de métal");
            var menuBar = shell.GetVisualDescendants().OfType<Border>().FirstOrDefault(delegate(Border border) { return "menu-bar".Equals(border.Tag); });
            Check(menuBar != null && menuBar.BorderThickness.Bottom >= 1 && ReferenceEquals(menuBar.BorderBrush, Chrome.TabGround), "…un filet sépare les menus du haut de l'application");
            var hint = shell.Binder.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(delegate(StackPanel panel) { return "binder-search-hint".Equals(panel.Tag); });
            Check(hint != null && hint.IsVisible && hint.Children.OfType<TextBlock>().Any(delegate(TextBlock block) { return block.Text == "Rechercher dans la pile"; }) && hint.Children.Count == 2,
                "…la recherche de la Pile porte sa loupe et « Rechercher dans la pile » tant qu'elle est vide");

            // — LE NOUVEL ACCUEIL.
            var view = shell.HomeForProbe;
            var journal = project.Journal;
            var goalBefore = journal.DailyGoal;
            var today = WritingJournal.Today();
            var wordsBefore = journal.WordsOn(today);
            var dayBefore = journal.FindDay(today) != null;
            var states = new System.Collections.Generic.List<BinderItem>();
            var statesBefore = new System.Collections.Generic.List<string>();
            try
            {
                journal.DailyGoal = 2000;
                journal.Add(today, 1240 - wordsBefore);
                // Un livre aux trois quarts : ses premiers chapitres passent à « Terminé », le temps de la sonde.
                var texts = new System.Collections.Generic.List<BinderItem>();
                if (book != null) BookProgress.StoryTexts(book, texts);
                var wanted = Math.Max(1, (int)Math.Floor(texts.Count * 0.7));
                for (var i = 0; i < texts.Count; i++) { states.Add(texts[i]); statesBefore.Add(texts[i].Status); texts[i].Status = i < wanted ? "done" : ""; }
                view.Refresh();
                for (var i = 0; i < 3; i++) await Settle();
                Check(view.IsVisible && view.HeroTitleForProbe == Skin.TitleText((1240).ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " mots aujourd'hui"),
                    "l'Accueil : les mots du jour en grand (« " + view.HeroTitleForProbe + " »)");
                Check(Math.Abs(view.RingRatioForProbe - 0.62) < 0.001 && view.RingTextForProbe.StartsWith("62"), "…l'anneau de l'objectif du jour (" + view.RingTextForProbe + ")");
                Check(view.HeroCardForProbe.GetVisualDescendants().OfType<OrbitLine>().Count() == 1, "…« Où j'en suis » porte son filet en orbite");
                var buttons = view.HeroCardForProbe.GetVisualDescendants().OfType<Button>().ToList();
                Check(buttons.Any(delegate(Button button) { return "home-sprint".Equals(button.Tag); }) && buttons.Any(delegate(Button button) { return "home-goal".Equals(button.Tag); }), "…« Lancer un sprint » et « Régler l'objectif »");
                var books = view.BookItems;
                var bar = book == null ? null : view.MilestonesOfForProbe(book);
                var expected = texts.Count == 0 ? 0 : HomeView.DoneShare(BookProgress.Of(book));
                Check(book == null || (books.Contains(book) && bar != null && Math.Abs(bar.Ratio - expected) < 0.001), "…une carte par livre, à barre à jalons (" + books.Count + " livre(s), « " + (book == null ? "" : book.Title) + " » à " + (expected * 100).ToString("0") + " %)");
                Check(book == null || texts.Count == 0 || Skins.MilestonesPassed(expected) >= 2, "…ce livre a franchi au moins deux jalons");
                Shot(shell, "museum-" + id + "-accueil.png");

                // Un livre TERMINÉ : la plaque.
                foreach (var text in texts) text.Status = "done";
                view.Refresh();
                for (var i = 0; i < 2; i++) await Settle();
                bar = book == null ? null : view.MilestonesOfForProbe(book);
                Check(book == null || texts.Count == 0 || (bar != null && bar.Ratio > 0.999), "…un livre dont tous les chapitres sont terminés est à 100 %");
                Shot(shell, "museum-" + id + "-accueil-termine.png");
            }
            finally
            {
                journal.DailyGoal = goalBefore;
                journal.Add(today, wordsBefore - journal.WordsOn(today));
                if (!dayBefore && journal.FindDay(today) != null) journal.Days.Remove(journal.FindDay(today));
                for (var i = 0; i < states.Count; i++) states[i].Status = statesBefore[i];
                view.Refresh();
            }
            prefs.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde d'« ASCENSION · ART NOUVEAU » (25/11/2026, le cahier d'intégration de Rémi, §10) :
    /// la police embarquée, les trois métaux, les cadres enluminés de l'Accueil, la texture de vitrail,
    /// la magie des progressions — et rien de tout cela sous les trois autres skins « Museum ».
    /// Jouée par ProbeMuseum, sous chaque skin de la série. Sous une sonde l'horloge est figée : les
    /// images de MARABOOK_PROBE_SHOTS (ascension-*.png) sont stables, à comparer à la maquette.</summary>
    public static partial class Probes
    {
        private static int MetalSwatches(PreferencesDialog prefs)
        {
            return prefs.GetVisualDescendants().OfType<Border>().Count(delegate(Border border) { var tag = border.Tag as string; return tag != null && tag.StartsWith("skin-metal:"); });
        }

        /// <summary>Sous un autre skin « Museum » : rien d'« Ascension » n'a fui.</summary>
        private static void ProbeNotAscension(PreferencesDialog prefs, string name)
        {
            Check(!AscensionArt.On && MetalSwatches(prefs) == 0 && ReferenceEquals(Skin.ResourceForProbe(Skin.VitrailKey), Brushes.Transparent) && Skin.TitleWeight == FontWeight.Medium,
                "…rien d'« Ascension · Art nouveau » sous " + name + " : ni métaux, ni vitrail, ni sa police");
            Check(TileFrame.Dress(new Border { Child = new Border() }) == null && !prefs.GetVisualDescendants().OfType<PlanetariumSky>().Any(delegate(PlanetariumSky sky) { return sky.IsVisible; })
                && !(TipText.Titled("Module", "Fiche").Contains("Module")),
                "…ni la double bordure des tuiles, ni le ciel de planétarium, ni les titres d'infobulle");
        }

        private static async Task ProbeAscension(MainWindow shell, PreferencesDialog prefs, Project project, BinderItem text)
        {
            Console.WriteLine("  — Ascension · Art nouveau");
            // 1 et 2. La police des titres : embarquée, résolue sans repli, et tous les caractères d'un titre français.
            var face = new Typeface(Skin.TitleFont, FontStyle.Normal, FontWeight.Normal).GlyphTypeface;
            Check(face != null && face.FamilyName == "Ascension SC" && Skin.TitleWeight == FontWeight.Normal, "la police des titres est « Ascension SC », embarquée (" + (face == null ? "introuvable" : face.FamilyName) + "), en graisse normale");
            var sample = "1 312 MOTS · LIVRE 1 · 62 % — « ÉCRIRE À L’ŒUVRE »";
            var missing = "";
            if (face != null) foreach (var c in sample) { ushort glyph; if (!face.TryGetGlyph(c, out glyph) || glyph == 0) missing += "U+" + ((int)c).ToString("X4") + " "; }
            Check(face != null && missing.Length == 0, "…« " + sample + " » se compose sans glyphe manquant" + (missing.Length > 0 ? " (manquent : " + missing + ")" : ""));
            Check(!AscensionArt.Moving, "…sous une sonde, l'horloge des ornements est figée (images stables)");

            // 3 et 4. Les trois métaux.
            Check(MetalSwatches(prefs) == 3, "trois pastilles de métal après les couleurs communes (or, vermeil, argent)");
            Check(AppSettings.AccentColor == null && Chrome.Hex(Chrome.Accent.Color) == "#C89B3C" && AscensionArt.MetalFor(null).Key == "or", "…sans choix, l'or : l'accent du skin (" + Chrome.Hex(Chrome.Accent.Color) + ")");
            var chosen = prefs.GetVisualDescendants().OfType<Panel>().Where(delegate(Panel panel) { return "skin-metal-chosen".Equals(panel.Tag); }).ToList();
            Check(chosen.Count == 1 && chosen[0].Children.OfType<Border>().Any(delegate(Border border) { return "skin-metal:or".Equals(border.Tag); }), "…c'est la pastille Or qui porte le losange du choix (plus l'Indigo cerclé)");
            var goldLine = Chrome.Hex(Chrome.BorderStrong.Color);

            var home = project.Category(Project.KeyHome);
            foreach (var metal in new[] { "vermeil", "argent" })
            {
                var recipe = AscensionArt.Metals.First(delegate(AscensionArt.MetalRecipe each) { return each.Key == metal; });
                prefs.SetAccentForProbe(recipe.Hex);
                for (var i = 0; i < 3; i++) await Settle();
                Check(AppSettings.AccentColor == recipe.Hex && Chrome.Hex(Chrome.Accent.Color) == recipe.Hex, "choisir " + recipe.Label + " : l'accent est " + recipe.Hex + " (" + Chrome.Hex(Chrome.Accent.Color) + ")");
                Check(Chrome.Hex(Chrome.Hover.Color) == "#CDFAFA" && Chrome.Hex(Chrome.BorderStrong.Color) != goldLine, "…les filets suivent le métal ; le survol (la magie) ne bouge pas");
                prefs.Hide();
                if (home != null) shell.Binder.SelectItem(home.Id);
                for (var i = 0; i < 3; i++) await Settle();
                Shot(shell, "ascension-metal-" + metal + ".png");
                prefs.Show(shell);
                await Settle();
            }
            prefs.SetAccentForProbe(null);
            for (var i = 0; i < 3; i++) await Settle();
            Check(AppSettings.AccentColor == null && Chrome.Hex(Chrome.Accent.Color) == "#C89B3C" && Chrome.Hex(Chrome.BorderStrong.Color) == goldLine, "choisir Or rend l'accent au skin : #C89B3C, la palette d'origine");
            AppSettings.AccentColor = "#16A085";
            shell.ApplyAppearanceForProbe();
            await Settle();
            Check(AscensionArt.MetalFor(AppSettings.AccentColor) == null && Math.Abs(Chrome.HueOf(Chrome.Accent.Color) - Chrome.HueOf(Color.Parse("#16A085"))) < 2.5, "une couleur personnalisée garde le calcul d'avant : sa teinte seule");
            AppSettings.AccentColor = null;
            shell.ApplyAppearanceForProbe();
            for (var i = 0; i < 3; i++) await Settle();

            // 6. La texture de vitrail : une couche à part, à 0,11.
            var vitrail = Skin.ResourceForProbe(Skin.VitrailKey) as DrawingBrush;
            var drawing = vitrail == null ? null : vitrail.Drawing as DrawingGroup;
            Check(vitrail != null && vitrail.TileMode == TileMode.Tile && drawing != null && Math.Abs(drawing.Opacity - 0.11) < 0.0001 && Math.Abs(vitrail.DestinationRect.Rect.Width - 180) < 0.01,
                "le fond porte la texture de vitrail : une mosaïque de 180 px, à l'opacité " + (drawing == null ? 0 : drawing.Opacity).ToString("0.00"));
            Check(shell.GetVisualDescendants().OfType<Border>().Any(delegate(Border border) { return "vitrail".Equals(border.Tag) && ReferenceEquals(border.Background, vitrail); }), "…posée sur la fenêtre, au-dessus du fond peint et sous tout le reste");

            // Les correctifs du 25/11 au soir : verre sur le vitrail des colonnes, lueur de magie à leur pied, arrondi réglable.
            Check(shell.VitrailClippedForProbe && Skin.SideReliefForProbe > 0.05 && Skin.SideReliefForProbe < 0.25, "…rognée sous la Pile et le rail, qui portent la leur, dépolie (au plus " + (Skin.SideReliefForProbe * 100).ToString("0") + " % du trait)");
            var glow = Skin.ResourceForProbe(Skin.SideGlowKey) as LinearGradientBrush;
            Check(glow != null && glow.GradientStops[0].Color.A > 30 && glow.GradientStops[glow.GradientStops.Count - 1].Color.A == 0 && glow.GradientStops[glow.GradientStops.Count - 1].Offset <= 0.25
                && shell.GetVisualDescendants().OfType<Border>().Count(delegate(Border border) { return "side-glow".Equals(border.Tag) && ReferenceEquals(border.Background, glow); }) == 2,
                "une lueur de magie monte du pied de la Pile et du rail, et s'éteint avant le quart de la hauteur");
            Check(AppSettings.CornerScale < 0.001 && Skin.Round(8) < 0.01, "choisir « Ascension » a posé l'arrondi à 0 % : des angles droits");
            prefs.SetCornerScale(1);
            for (var i = 0; i < 3; i++) await Settle();
            var heroCard = shell.HomeForProbe.HeroCardForProbe;
            Check(Skin.Round(8) > 3 && heroCard.CornerRadius.TopLeft < 0.01, "…réglé à 100 %, les rondeurs reviennent (" + Skin.Round(8).ToString("0.#") + " px) — sauf sur les cadres enluminés, qui restent droits");
            if (home != null) { prefs.Hide(); shell.Binder.SelectItem(home.Id); for (var i = 0; i < 3; i++) await Settle(); Shot(shell, "ascension-arrondi-100.png"); prefs.Show(shell); await Settle(); Shot(prefs, "ascension-preferences-arrondi-100.png"); }
            prefs.SetCornerScale(0);
            for (var i = 0; i < 3; i++) await Settle();
            var primary = prefs.GetVisualDescendants().OfType<Button>().FirstOrDefault(delegate(Button button) { return button.Classes.Contains(Theme.Primary); });
            Check(primary == null || (primary.Background is LinearGradientBrush && ((LinearGradientBrush)primary.Background).GradientStops.Count == 4), "le bouton principal est celui de la série : le métal sombre en dégradé, à liseré");

            // 5. L'Accueil : quatre cadres enluminés, un seul riche ; le nimbe ; la magie.
            prefs.Hide();
            if (home != null) shell.Binder.SelectItem(home.Id);
            for (var i = 0; i < 4; i++) await Settle();
            var homeView = shell.HomeForProbe;
            var frames = homeView.GetVisualDescendants().OfType<IlluminatedFrame>().ToList();
            var heroFrame = HomeView.FrameOf(homeView.HeroCardForProbe);
            Check(frames.Count == 4 && frames.Count(delegate(IlluminatedFrame frame) { return frame.Rich; }) == 1 && heroFrame != null && heroFrame.Rich && heroFrame.Crest,
                "les quatre cartes de l'Accueil portent un cadre enluminé (" + frames.Count + ") ; seule « Où j'en suis » est riche, à cimier");
            Check(frames.Count(delegate(IlluminatedFrame frame) { return frame.Foot; }) == 1 && frames.All(delegate(IlluminatedFrame frame) { return !frame.IsHitTestVisible && frame.Bounds.Width > 100; }),
                "…ils couvrent leur carte sans prendre la souris ; un séparateur au pied de la carte Livres");
            var ring = homeView.GetVisualDescendants().OfType<GoalRing>().FirstOrDefault();
            Check(ring != null && Math.Abs(ring.Bounds.Width - GoalRing.NimbeSide) < 0.5, "l'anneau de l'objectif est un nimbe de " + GoalRing.NimbeSide + " px");
            var meter = Skin.MeterBrush(Chrome.Glow.Color, 200) as LinearGradientBrush;
            Check(meter != null && meter.GradientStops[0].Color == AscensionArt.MagicDeep && meter.GradientStops[meter.GradientStops.Count - 1].Color == AscensionArt.Magic, "les progressions prennent la magie bleue (#0596AA → #0AC8B9), quel que soit le métal");
            var launch = homeView.GetVisualDescendants().OfType<Button>().FirstOrDefault(delegate(Button button) { return "home-sprint".Equals(button.Tag); });
            var start = homeView.GetVisualDescendants().OfType<Button>().FirstOrDefault(delegate(Button button) { return "home-goal".Equals(button.Tag); });
            Check(launch != null && launch.Classes.Contains(AscensionArt.MagicClass) && launch.Background is LinearGradientBrush && launch.Foreground is ISolidColorBrush && ((ISolidColorBrush)launch.Foreground).Color == AscensionArt.MagicHi,
                "« Lancer un sprint » est un bouton de magie : fond bleu sourd, texte clair");
            Check(start != null && !start.Classes.Contains(AscensionArt.MagicClass), "…« Régler l'objectif » reste un bouton secondaire (la magie est rare)");
            var rubric = shell.Binder.GetVisualDescendants().OfType<RubricLine>().Count();
            var label = shell.Binder.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(delegate(TextBlock block) { return "section:Écriture".Equals(block.Tag); });
            Check(rubric >= 4 && label != null && label.Text == "ÉCRITURE" && ReferenceEquals(label.Foreground, Chrome.Accent), "les rubriques de la Pile : capitales de métal, un filet qui finit par un losange (" + rubric + ")");
            Check(BinderView.SectionNames().Contains("Écriture") && shell.Binder.TopRowsForProbe().Contains("Écriture"), "…leur nom reste le leur (« Écriture ») pour qui les lit");
            Shot(shell, "ascension-accueil-sombre.png");
            if (text != null)
            {
                shell.Binder.SelectItem(text.Id);
                for (var i = 0; i < 3; i++) await Settle();
                Shot(shell, "ascension-editeur-sombre.png");
            }
            prefs.Show(shell);
            for (var i = 0; i < 2; i++) await Settle();
            var crests = prefs.GetVisualDescendants().OfType<TabCrest>().ToList();
            Check(crests.Count > 3 && crests.Count(delegate(TabCrest crest) { return crest.ShownForProbe; }) == 1, "l'onglet choisi des Préférences porte le filet doré et son losange (un seul)");
            // Le losange ne reste pas sur l'onglet quitté : un seul cimier à l'écran, avant comme après un changement d'onglet.
            var shownCrest = crests.FirstOrDefault(delegate(TabCrest crest) { return crest.IsVisible; });
            Check(crests.Count(delegate(TabCrest crest) { return crest.IsVisible; }) == 1 && shownCrest != null && shownCrest.InsideForProbe, "…un seul cimier existe à l'écran, et il peint dans son cadre (rien ne déborde)");
            var keptTab = prefs.Tabs.SelectedIndex;
            var animationsWere = AppSettings.Animations;
            AppSettings.Animations = false;
            foreach (var hop in new[] { 0, 3, 1, keptTab })
            {
                prefs.Tabs.SelectedIndex = hop;
                for (var i = 0; i < 2; i++) await Settle();
                var visible = crests.Where(delegate(TabCrest crest) { return crest.IsVisible; }).ToList();
                Check(visible.Count == 1 && visible[0].ShownForProbe, "…onglet " + hop + " : le losange a suivi, aucun n'est resté sur l'onglet quitté");
            }
            AppSettings.Animations = animationsWere;
            var chosenTab = prefs.Tabs.SelectedItem as TabItem;
            Check(chosenTab != null && !chosenTab.ClipToBounds, "…ce losange, à cheval sur le bord haut, n'est pas rogné par son onglet");
            // Patch performance (26/11) : le reflet se repose entre deux passages, et rien ne bouge pendant qu'on agit.
            Check(AscensionArt.SweepLasts < AscensionArt.SweepSeconds / 3 && AscensionArt.ShineLasts < AscensionArt.ShineSeconds / 3 && crests.All(delegate(TabCrest crest) { return !AscensionArt.AnimatedForProbe(crest); })
                && AscensionArt.AnimatedCountForProbe(AscensionArt.Kind.Turn) + AscensionArt.AnimatedCountForProbe(AscensionArt.Kind.Shine) + AscensionArt.AnimatedCountForProbe(AscensionArt.Kind.Gilt) > 0,
                "les ornements ne sont repeints que le temps d'un reflet (" + AscensionArt.SweepLasts.ToString("0.0") + " s sur " + AscensionArt.SweepSeconds.ToString("0") + "), chacun pour ce qui le concerne ; les losanges d'onglet, jamais");
            Shot(prefs, "ascension-preferences-sombre.png");

            // L'autre visage : le clair (non maquetté — à juger sur ces images).
            AppSettings.DarkTheme = false;
            shell.ApplyAppearanceForProbe();
            for (var i = 0; i < 3; i++) await Settle();
            var light = Skin.ResourceForProbe(Skin.VitrailKey) as DrawingBrush;
            Check(light != null && !ReferenceEquals(light, vitrail), "au clair, la texture est retracée (à l'encre du skin)");
            Shot(prefs, "ascension-preferences-clair.png");
            prefs.Hide();
            Shot(shell, "ascension-editeur-clair.png");
            if (home != null) shell.Binder.SelectItem(home.Id);
            for (var i = 0; i < 3; i++) await Settle();
            Shot(shell, "ascension-accueil-clair.png");
            AppSettings.DarkTheme = true;
            shell.ApplyAppearanceForProbe();
            prefs.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
            await ProbeAscensionPieces(shell, prefs, project);
        }

        /// <summary>LES SKINS À DÉBLOQUER (25/11/2026 au soir) : trois succès secrets ouvrent chacun un skin
        /// « Museum » ; verrouillé, sa carte ne se choisit pas ; le sésame tapé dans la palette Ctrl+, les
        /// ouvre tous. Les réglages de l'auteur sont rendus à la fin.</summary>
        private static async Task ProbeSkinUnlocks(MainWindow shell, PreferencesDialog prefs)
        {
            Console.WriteLine("  — Les skins à débloquer");
            var keptAchievements = new System.Collections.Generic.Dictionary<string, string>(AppSettings.Achievements);
            var keptAll = AppSettings.EverythingUnlocked;
            Func<string, int> cards = delegate(string prefix) { return prefs.GetVisualDescendants().OfType<Border>().Count(delegate(Border border) { var tag = border.Tag as string; return tag != null && tag.StartsWith(prefix); }); };
            try
            {
                foreach (var id in new[] { Achievements.FireKeeper, Achievements.WhiteWood, Achievements.Sworn }) AppSettings.Achievements.Remove(id);
                AppSettings.EverythingUnlocked = false;
                prefs.RebuildSkinsForProbe();
                await Settle();
                var current = Skin.Current;
                var expected = 3 - (Skins.UnlockedBy(current) != null ? 1 : 0);
                Check(cards("skin-locked:") == expected && cards("skin:") == Skins.Ids.Length - expected, "sans leurs succès, les skins « Museum » à débloquer sont verrouillés (" + cards("skin-locked:") + ") — pas celui en cours, ni « Ascension »");
                AppSettings.Achievements[Achievements.FireKeeper] = "2026-11-25 23:00";
                prefs.RebuildSkinsForProbe();
                await Settle();
                Check(cards("skin-locked:" + Skins.MuseumFire) == 0 && cards("skin:" + Skins.MuseumFire) == 1, "le succès « Gardien du Feu » ouvre son skin");
                AppSettings.Achievements.Remove(Achievements.FireKeeper);

                // Le sésame, dans la palette « Ouvrir par le nom ».
                prefs.Hide();
                var palette = shell.QuickOpenForProbe;
                await Settle();
                palette.TypeForProbe("je veux un monde");
                await Settle();
                var before = palette.FirstResultIdForProbe;
                palette.TypeForProbe(Skins.Sesame);
                await Settle();
                Check(palette.ResultCountForProbe == 1 && palette.FirstResultIdForProbe != null && palette.FirstResultIdForProbe != before && shell.Project.FindById(palette.FirstResultIdForProbe) == null,
                    "« Je veux un monde meilleur » dans la palette Ctrl+, : une seule ligne, secrète, qui demande d'appuyer sur Entrée");
                Check(!AppSettings.EverythingUnlocked, "…rien n'est débloqué avant de valider");
                palette.ChooseForProbe();
                for (var i = 0; i < 2; i++) await Settle();
                Check(AppSettings.EverythingUnlocked && !palette.IsOpen && !AppSettings.Achievements.ContainsKey(Achievements.FireKeeper), "valider débloque tout — sans donner les succès eux-mêmes");
                prefs.Show(shell);
                prefs.RebuildSkinsForProbe();
                await Settle();
                Check(cards("skin-locked:") == 0, "…plus aucun skin verrouillé dans les Préférences");
                prefs.Hide();

                // Un succès obtenu dit, en gras, ce qu'il a débloqué.
                AppSettings.Achievements[Achievements.Sworn] = "2026-11-25 23:00";
                shell.ShowJournalPublic();
                for (var i = 0; i < 3; i++) await Settle();
                var journal = shell.JournalWindowForProbe;
                var rewards = journal == null ? new System.Collections.Generic.List<TextBlock>() : journal.GetLogicalDescendants().OfType<TextBlock>().Where(delegate(TextBlock block) { return "achievement-reward".Equals(block.Tag); }).ToList();
                Check(rewards.Count == 1 && rewards[0].Text == "Vous avez débloqué un skin Marabook !" && rewards[0].FontWeight == FontWeight.Bold, "le succès secret obtenu ajoute, à la ligne et en gras : « Vous avez débloqué un skin Marabook ! »");
                shell.CloseJournalForProbe();
                for (var i = 0; i < 2; i++) await Settle();
            }
            finally
            {
                AppSettings.Achievements.Clear();
                foreach (var pair in keptAchievements) AppSettings.Achievements[pair.Key] = pair.Value;
                AppSettings.EverythingUnlocked = keptAll;
                AppSettings.Save();
            }
            prefs.Show(shell);
            prefs.RebuildSkinsForProbe();
            for (var i = 0; i < 2; i++) await Settle();
        }

        /// <summary>LE LOT 2 (25/11 au soir) : la double bordure des tuiles, la médaille FPDM, la rosace, les
        /// infobulles en plaque, le ciel de planétarium des Préférences et du Journal, les losanges du
        /// plan, les boîtes du Journal. Images ascension2-*.png.</summary>
        private static async Task ProbeAscensionPieces(MainWindow shell, PreferencesDialog prefs, Project project)
        {
            Console.WriteLine("  — Ascension · Art nouveau, lot 2");
            // Le ciel des Préférences : sous la silhouette galbée, rogné à elle ; figé sous une sonde.
            var heaven = prefs.GetVisualDescendants().OfType<PlanetariumSky>().FirstOrDefault();
            var galbe = prefs.GetVisualDescendants().OfType<GalbeFrame>().FirstOrDefault();
            Check(heaven != null && heaven.IsVisible && heaven.Clip != null && galbe != null && galbe.SkyBehind && heaven.Bounds.Width > 300,
                "les Préférences ont un ciel de planétarium pour fond, rogné à la silhouette galbée (qui ne peint plus que ses filets)");
            Shot(prefs, "ascension2-preferences.png");
            // Le ciel seul, à pleine opacité : à juger sur l'image (ascension2-ciel.png).
            var skyAlone = new PlanetariumSky();
            var skyWindow = new Window { Width = 900, Height = 600, Content = skyAlone, Title = "Ciel" };
            skyWindow.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
            Shot(skyAlone, "ascension2-ciel.png");
            Check(!PlanetariumSky.Turning && (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MARABOOK_PROBE_SHOTS")) || skyAlone.ChainsForProbe > 0), "…figé sous une sonde ; ses constellations sont tracées (" + skyAlone.ChainsForProbe + ")");
            skyWindow.Close();
            for (var i = 0; i < 2; i++) await Settle();
            prefs.Hide();

            // Les pièces, sur un établi : trois tuiles habillées, leurs médailles, une infobulle.
            Check(Math.Abs(new FpdmMedal(9, 14, false).RatioForProbe - 9 / 14.0) < 0.001 && new FpdmMedal(14, 14, true).CompleteForProbe && Math.Abs(new FpdmMedal(14, 14, true).RatioForProbe - 1) < 0.001,
                "la médaille FPDM : remplie au prorata des champs (9 / 14), pleine et d'or à 100 %");
            var bench = new WrapPanel { Margin = new Thickness(30) };
            var benchWindow = new Window { Width = 760, Height = 330, Content = bench, Background = Chrome.WindowBg, Title = "Établi" };
            Border lastTile = null;
            Button tipped = null;
            foreach (var filled in new[] { 3, 9, 14 })
            {
                var body = new DockPanel { Width = 132 };
                var picture = new Border { Height = 108, Background = Chrome.BarBgLight, Child = SheetLibraryView.CategoryPlaceholder(null, 44) };
                DockPanel.SetDock(picture, Dock.Top);
                body.Children.Add(picture);
                body.Children.Add(new TextBlock { Text = filled + " / 14", Foreground = Chrome.Ink, TextAlignment = TextAlignment.Center, Margin = new Thickness(6, 6, 6, 8) });
                var tile = lastTile = new Border { Background = Chrome.CardBg, BorderBrush = filled == 9 ? (IBrush)Chrome.Accent : Chrome.Border, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 16, 10), Child = body };
                var layers = TileFrame.Dress(tile);
                TileFrame.PinTop(layers, new FpdmMedal(filled, 14, filled == 14), 4);
                bench.Children.Add(tile);
            }
            tipped = new Button { Content = "Rechercher", Margin = new Thickness(40, 20, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            ToolTip.SetTip(tipped, TipText.Titled("Sprint", "Un compte à rebours, vos mots comptés en direct. (F9)\nDernier sprint : 412 mots en 20 min"));
            bench.Children.Add(tipped);
            benchWindow.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
            ToolTip.SetIsOpen(tipped, true);
            for (var i = 0; i < 3; i++) await Settle();
            var frames = bench.GetVisualDescendants().OfType<TileFrame>().ToList();
            Check(frames.Count == 6 && lastTile.Background is ISolidColorBrush && ((ISolidColorBrush)lastTile.Background).Color.A == 0 && ((ISolidColorBrush)lastTile.BorderBrush).Color.A == 0,
                "une tuile habillée porte la double bordure (un fond aux angles rentrés dessous, les deux filets dessus) ; son propre fond et son filet s'effacent");
            Check(frames.Count(delegate(TileFrame frame) { return frame.LitForProbe; }) == 1, "…le filet d'accent d'une tuile choisie ou survolée devient l'éclat de la bordure galbée");
            Check(bench.GetVisualDescendants().OfType<Rosace>().Count() == 3 && bench.GetVisualDescendants().OfType<FpdmMedal>().Count() == 3, "…sans image, l'icône se pose sur une rosace ; la médaille FPDM est à cheval sur le bord haut");
            var built = TipText.Build("Rechercher dans la Pile (Ctrl+F)") as StackPanel;
            Check(built != null && built.GetLogicalDescendants().OfType<Border>().Any(delegate(Border border) { return "tip-keys".Equals(border.Tag); })
                && TipText.Build("Une fiche (complète)") is StackPanel && !((StackPanel)TipText.Build("Une fiche (complète)")).GetLogicalDescendants().OfType<Border>().Any(),
                "l'infobulle sort le raccourci clavier dans un cartouche (« Ctrl+F ») — et lui seul : « (complète) » reste dans le texte");
            var titled = TipText.Build(TipText.Titled("Module FPDM", "Fiche FPDM — 9 / 14 champs")) as StackPanel;
            Check(titled != null && titled.GetLogicalDescendants().OfType<TextBlock>().Any(delegate(TextBlock block) { return "tip-title".Equals(block.Tag) && block.Text == "MODULE FPDM"; }), "…et pose un titre en capitales quand on lui en donne un");
            Shot(benchWindow, "ascension2-pieces.png");
            ToolTip.SetIsOpen(tipped, false);
            benchWindow.Close();
            for (var i = 0; i < 2; i++) await Settle();

            // Les fiches et le tableau : toutes leurs tuiles sont habillées.
            var sheets = project.Category(Project.KeySheets);
            if (sheets != null)
            {
                shell.Binder.SelectItem(sheets.Id);
                for (var i = 0; i < 4; i++) await Settle();
                var library = shell.SheetLibraryForProbe;
                var tiles = library.GetVisualDescendants().OfType<TileFrame>().Count();
                Check(library.IsVisible && tiles > 0 && tiles % 2 == 0, "les tuiles de la bibliothèque des fiches portent la double bordure (" + tiles / 2 + ")");
                Shot(shell, "ascension2-fiches.png");
            }
            var writings = project.Category(Project.KeyWritings);
            if (writings != null)
            {
                shell.Binder.SelectItem(writings.Id);
                for (var i = 0; i < 4; i++) await Settle();
                var board = shell.CorkboardForProbe;
                var tiles = board == null ? 0 : board.GetVisualDescendants().OfType<TileFrame>().Count();
                Check(board != null && (!board.IsVisible || tiles > 0), "…celles du tableau aussi (" + tiles / 2 + ")");
                Shot(shell, "ascension2-tableau.png");
            }
            var plan = project.AllItems().FirstOrDefault(delegate(BinderItem item) { return item.Kind == ItemKind.Plan; });
            if (plan != null)
            {
                shell.Binder.SelectItem(plan.Id);
                for (var i = 0; i < 4; i++) await Settle();
                var rows = shell.GetVisualDescendants().OfType<DiamondRow>().ToList();
                var dots = shell.GetVisualDescendants().OfType<PlanView>().SelectMany(delegate(PlanView view) { return view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>(); }).Count(delegate(Avalonia.Controls.Shapes.Ellipse dot) { return dot.Width == 7; });
                Check(dots == 0 && rows.All(delegate(DiamondRow row) { return row.CountForProbe == PlanIntensity.Max; }), "l'intensité d'un élément de plan se lit en losanges (" + rows.Count + " rangées), plus en pastilles rondes");
                Shot(shell, "ascension2-plan.png");
            }

            // Le Journal perso : le ciel pour fond, ses boîtes embellies. Quatorze jours d'écriture le temps
            // de la sonde (rendus ensuite), pour que le graphique ait de quoi se montrer.
            var keptDays = new System.Collections.Generic.List<JournalDay>(project.Journal.Days);
            var keptGoal = project.Journal.DailyGoal;
            var sample = new[] { 1700, 2150, 0, 1250, 2500, 1950, 2380, 880, 2080, 2950, 1640, 2040, 2260, 1312 };
            project.Journal.Days.Clear();
            for (var i = 0; i < sample.Length; i++)
                if (sample[i] > 0) project.Journal.Days.Add(new JournalDay { Date = DateTime.Now.Date.AddDays(i - 13).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), Words = sample[i] });
            project.Journal.DailyGoal = 2000;
            shell.ShowJournalPublic();
            for (var i = 0; i < 4; i++) await Settle();
            var journal = shell.JournalWindowForProbe;
            var view = journal == null ? null : journal.GetVisualDescendants().OfType<JournalView>().FirstOrDefault();
            Check(journal != null && journal.GetVisualDescendants().OfType<PlanetariumSky>().Count() == 1 && !journal.GetVisualDescendants().OfType<Border>().Any(delegate(Border border) { return "modal-vitrail".Equals(border.Tag); }),
                "le Journal perso a le même ciel pour fond, à la place du vitrail");
            Check(view != null && view.BuiltForAscension && view.GetVisualDescendants().OfType<GoalRing>().Count() == 1
                && view.GetVisualDescendants().OfType<Border>().Count(delegate(Border border) { return "journal-cartel".Equals(border.Tag); }) == 4
                && view.GetVisualDescendants().OfType<DiamondRow>().Any(delegate(DiamondRow row) { return row.CountForProbe == 7; })
                && view.GetVisualDescendants().OfType<IlluminatedFrame>().Count() >= 2,
                "…ses boîtes : le jour dans un nimbe au cadre enluminé, quatre cartels, la série en sept losanges");
            var bars = view == null ? 0 : view.GetVisualDescendants().OfType<Border>().Count(delegate(Border border) { return "journal-bar".Equals(border.Tag) || "journal-bar-today".Equals(border.Tag); });
            Check(bars == 13 && view.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Line>().Any(delegate(Avalonia.Controls.Shapes.Line line) { return "journal-goal-line".Equals(line.Tag); }),
                "…son graphique : une colonne dorée par jour écrit (" + bars + " sur 14, aujourd'hui en magie), la ligne d'objectif en filet d'or");
            if (journal != null) Shot(journal, "ascension2-journal.png");
            if (view != null) view.SelectTabForTutorial(1);
            for (var i = 0; i < 3; i++) await Settle();
            Check(view != null && view.GetVisualDescendants().OfType<Grid>().Any(delegate(Grid grid) { return "journal-medal-bar".Equals(grid.Tag); }), "…l'onglet Succès ouvre sur le médaillier : le compte et sa barre de magie");
            if (journal != null) Shot(journal, "ascension2-journal-succes.png");
            if (view != null) view.SelectTabForTutorial(0);
            // …et au clair (non maquetté).
            AppSettings.DarkTheme = false;
            shell.ApplyAppearanceForProbe();
            shell.CloseJournalForProbe();
            for (var i = 0; i < 2; i++) await Settle();
            shell.ShowJournalPublic();
            for (var i = 0; i < 4; i++) await Settle();
            if (shell.JournalWindowForProbe != null) Shot(shell.JournalWindowForProbe, "ascension2-journal-clair.png");
            shell.CloseJournalForProbe();
            project.Journal.Days.Clear();
            project.Journal.Days.AddRange(keptDays);
            project.Journal.DailyGoal = keptGoal;
            for (var i = 0; i < 2; i++) await Settle();
            if (sheets != null) { shell.Binder.SelectItem(sheets.Id); for (var i = 0; i < 4; i++) await Settle(); Shot(shell, "ascension2-fiches-clair.png"); }
            AppSettings.DarkTheme = true;
            shell.ApplyAppearanceForProbe();
            prefs.Show(shell);
            for (var i = 0; i < 3; i++) await Settle();
        }
    }
}

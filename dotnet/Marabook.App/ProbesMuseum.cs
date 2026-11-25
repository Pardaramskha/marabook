using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Sonde de la série « Museum » (2.0.0, 24/11/2026) : les trois skins PREMIUM
    /// choisis depuis les Préférences, leur palette, leur fond peint, leurs trois options
    /// (accent, fond, effet verre), la plaque, le filet en orbite — puis le retour à
    /// « modern », qui doit tout rendre. MARABOOK_PROBE_ONLY=museum.</summary>
    public static partial class Probes
    {
        private static async Task ProbeMuseum(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- série « Museum » : Ascension, Gardien du Feu, Bois Blanc, Assermenté (2.0.0, 24/11)");
            string skinBefore = AppSettings.Skin, accentBefore = AppSettings.AccentColor, groundBefore = AppSettings.SkinBackground;
            int glassBefore = AppSettings.SkinGlass;
            int blurBefore = AppSettings.SkinBlur;
            bool darkBefore = AppSettings.DarkTheme, animationsBefore = AppSettings.Animations;
            PreferencesDialog prefs = null;
            var colored = new System.Collections.Generic.List<BinderItem>();
            var colorsBefore = new System.Collections.Generic.List<string>();
            try
            {
                AppSettings.Skin = null;
                AppSettings.AccentColor = null;
                AppSettings.SkinBackground = null;
                AppSettings.SkinGlass = -1;
                AppSettings.SkinBlur = -1;
                AppSettings.DarkTheme = false;
                AppSettings.Animations = true;
                shell.ApplyAppearanceForProbe();
                await Settle();
                var inkBefore = Chrome.Ink.Color;
                var accentModern = Chrome.Accent.Color;
                Check(Skin.Plain && Skin.Plaque() == null && Skin.TitleFont == null && !OrbitLine.Active, "« Marabook modern » : ni plaque, ni police de titres, ni filet en orbite");

                // Le NOUVEL ACCUEIL (24/11 au soir) vaut pour tous les skins : d'abord sous « modern ».
                var homeRoot = project.Category(Project.KeyHome);
                if (homeRoot != null)
                {
                    shell.Binder.SelectItem(homeRoot.Id);
                    for (var i = 0; i < 3; i++) await Settle();
                    var homeView = shell.HomeForProbe;
                    Check(homeView.IsVisible && homeView.RingRatioForProbe >= 0 && homeView.HeroTitleForProbe.Contains("aujourd'hui") && homeView.BookItems.Count > 0,
                        "l'Accueil sous « modern » : l'anneau du jour, les mots du jour, une carte par livre (" + homeView.BookItems.Count + ")");
                    Check(homeView.HeroCardForProbe.GetVisualDescendants().OfType<Button>().Count() >= 2 && homeView.BookItems.All(delegate(BinderItem item) { return homeView.MilestonesOfForProbe(item) != null; }),
                        "…« Lancer un sprint », « Régler l'objectif », et une barre à jalons par livre");
                    var journalShown = false;
                    Action seen = delegate { journalShown = true; };
                    homeView.GoalRequested += seen;
                    homeView.RaiseGoalForProbe();
                    homeView.GoalRequested -= seen;
                    await Settle();
                    // Le Journal est une fenêtre depuis le 25/11 : l'Accueil reste dessous.
                    Check(journalShown && shell.JournalWindowShownForProbe && homeView.IsVisible, "…« Régler l'objectif » ouvre le Journal, où il se règle — dans sa fenêtre, l'Accueil dessous");
                    shell.CloseJournalForProbe();
                    await Settle();
                    shell.Binder.SelectItem(homeRoot.Id);
                    for (var i = 0; i < 3; i++) await Settle();
                    Shot(shell, "accueil-modern.png");
                }

                BinderItem book = null, sheet = FirstTitled(project, "Keira"), text = null;
                foreach (var item in project.AllItems())
                {
                    if (item.RootCategory() != null && item.RootCategory().CategoryKey == Project.KeyTrash) continue;
                    if (book == null && item.Kind == ItemKind.Book) book = item;
                    if (text == null && item.Kind == ItemKind.Text) text = item;
                }
                // Des tuiles colorées, le temps de la sonde : la tranche en S se voit sur les captures.
                var tints = new[] { "#C0392B", "#16A085", "#8E44AD" };
                if (book != null) for (var i = 0; i < book.Children.Count && i < tints.Length; i++) { colored.Add(book.Children[i]); colorsBefore.Add(book.Children[i].CardColor); book.Children[i].CardColor = tints[i]; }

                prefs = new PreferencesDialog(shell, project);
                prefs.AppearanceChanged += shell.ApplyAppearanceForProbe;
                prefs.Show(shell);
                await Settle();
                prefs.Tabs.SelectedIndex = 1;
                await Settle();
                Check(prefs.SkinCardsForProbe == 9 && !prefs.MuseumRowShownForProbe, "Préférences propose neuf skins ; pas d'options « Museum » sous « modern »");

                // ------------------------------------------------ l'habit des fenêtres de dialogue
                Border modalFrame = null, modalVeil = null;
                Panel modalTitle = null;
                Button modalClose = null;
                foreach (var visual in prefs.GetVisualDescendants())
                {
                    var control = visual as Control;
                    if (control == null) continue;
                    if ("modal-frame".Equals(control.Tag)) modalFrame = control as Border;
                    if ("modal-title".Equals(control.Tag)) modalTitle = control as Panel;
                    if ("modal-close".Equals(control.Tag)) modalClose = control as Button;
                    if ("modal-veil".Equals(control.Tag)) modalVeil = control as Border;
                }
                Check(ModalChrome.DressedForProbe(prefs) && prefs.SystemDecorations == SystemDecorations.None && modalFrame != null, "les Préférences ont quitté le cadre du système pour le panneau arrondi");
                Check(modalFrame != null && Math.Abs(modalFrame.CornerRadius.TopLeft - Skin.Round(12)) < 0.01 && modalFrame.CornerRadius.TopLeft > 0, "…ses coins sont arrondis (" + (modalFrame == null ? 0 : modalFrame.CornerRadius.TopLeft).ToString("0.#") + " px)");
                Check(modalTitle != null && modalTitle.Cursor != null && modalTitle.Bounds.Height > 30, "…une barre de titre qu'on tire pour déplacer la fenêtre (curseur main)");
                Check(modalClose != null && modalTitle != null && modalClose.TranslatePoint(new Avalonia.Point(0, 0), modalTitle).GetValueOrDefault().X > modalTitle.Bounds.Width - 60, "…la croix Fermer en haut à droite");
                Check(ModalChrome.Dimmed(shell), "…un voile est posé sur la fenêtre principale tant qu'elles sont ouvertes");
                Check(ModalChrome.BlurOfForProbe(shell) < 0.01, "…sans flou sous « modern » : il n'y a pas de verre");
                Check(modalVeil != null && !modalVeil.IsVisible && !ModalChrome.Dimmed(prefs), "…et pas sur elles : elles n'ont rien ouvert");
                var asked = new Window { Title = "Un message de la sonde", Width = 340, Height = 130, CanResize = false, Background = Chrome.RaisedBg, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new TextBlock { Text = "Un essai de la sonde.", Foreground = Chrome.Ink, Margin = new Avalonia.Thickness(18) } };
                asked.Show(prefs);
                await Settle();
                Check(Math.Abs(asked.Height - (130 + ModalChrome.TitleHeight)) < 0.5, "une fenêtre de taille fixe grandit de la hauteur de sa barre de titre : son contenu garde sa place (" + asked.Height.ToString("0") + " px)");
                await Settle();
                await Settle();
                Check(ModalChrome.DressedForProbe(asked) && ModalChrome.Dimmed(prefs) && modalVeil != null && modalVeil.IsVisible, "un message ouvert depuis les Préférences prend le même habit, et les voile à leur tour");
                Shot(asked, "modal-message.png");
                asked.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
                await Settle();
                await Settle();
                Check(!asked.IsVisible && !ModalChrome.Dimmed(prefs), "Échap le ferme, et le voile des Préférences se lève");
                Shot(prefs, "modal-preferences-modern.png");
                Shot(shell, "modal-voile-principal.png");

                // ------------------------------------------------ l'arrondi des angles
                Border anyCard = null;
                foreach (var border in prefs.GetVisualDescendants().OfType<Border>()) if ("skin:modern".Equals(border.Tag)) anyCard = border;
                prefs.SetCornerScale(0);
                for (var i = 0; i < 3; i++) await Settle();
                Check(Math.Abs(AppSettings.CornerScale) < 0.001 && Skin.Round(8) < 0.01 && anyCard != null && anyCard.CornerRadius.TopLeft < 0.01 && modalFrame != null && modalFrame.CornerRadius.TopLeft < 0.01,
                    "arrondi des angles à 0 % : plus un coin arrondi, sous « modern » non plus (cartes, fenêtres)");
                Shot(prefs, "arrondi-0.png");
                prefs.SetCornerScale(2);
                for (var i = 0; i < 3; i++) await Settle();
                Check(Math.Abs(Skin.Round(8) - 16) < 0.01 && anyCard != null && Math.Abs(anyCard.CornerRadius.TopLeft - 16) < 0.01 && Math.Abs(Skin.Round(4) - 8) < 0.01,
                    "…à 200 % : chaque rayon est doublé (8 → 16, 4 → 8) — chacun garde le sien");
                Shot(prefs, "arrondi-200.png");
                prefs.SetCornerScale(1);
                for (var i = 0; i < 3; i++) await Settle();
                Check(Math.Abs(Skin.Round(8) - 8) < 0.01 && anyCard != null && Math.Abs(anyCard.CornerRadius.TopLeft - 8) < 0.01, "…à 100 % : le dessin d'origine revient");
                Check(Skin.TileFade(Colors.Red) == null, "« modern » garde le dégradé habituel des tuiles");
                Check(prefs.GetVisualDescendants().OfType<Border>().Count(delegate(Border border) { return "skin-premium".Equals(border.Tag); }) == 4, "…quatre d'entre eux portent l'étiquette PREMIUM");

                foreach (var id in new[] { Skins.MuseumAscent, Skins.MuseumFire, Skins.MuseumWood, Skins.MuseumOath })
                {
                    AppSettings.DarkTheme = false;
                    prefs.SetSkin(id);
                    for (var i = 0; i < 4; i++) await Settle();
                    var name = "« " + Skins.Label(id) + " »";
                    var night = Skins.DarkByDefault(id);
                    Check(Skin.Current == id && AppSettings.Skin == id && Skin.Museum, name + " est choisi et retenu");
                    Check(Chrome.Dark == night && AppSettings.DarkTheme == night, night ? "…sombre par défaut : le choisir passe l'application au sombre" : "…clair : le mode ne bouge pas");
                    Check(Skin.ResourceForProbe(Skin.BackdropKey) is LinearGradientBrush && ReferenceEquals(Skin.ResourceForProbe(Skin.GroundKey), Brushes.Transparent), "…un fond peint derrière toute la fenêtre ; les vues du centre le laissent voir");
                    Check(Chrome.Hex(Chrome.WindowBg.Color) == Skins.MuseumBackground(id, night), "…le fond du skin (" + Chrome.Hex(Chrome.WindowBg.Color) + ")");
                    var wanted = Chrome.HueOf(Chrome.ToColor(Ink.Parse(Skins.MuseumAccent(id))));
                    var got = Chrome.HueOf(Chrome.Accent.Color);
                    Check(Math.Abs(got - wanted) < 2.5 && Chrome.Accent.Color != accentModern, "…sans accent choisi, le métal du skin (" + Chrome.Hex(Chrome.Accent.Color) + ", teinte " + got.ToString("0") + "°)");
                    Check(Chrome.ContrastRatio(Chrome.Ink.Color, Chrome.BarBg.Color) >= 7 && Chrome.ContrastRatio(Chrome.SoftText.Color, Chrome.BarBg.Color) >= 4.5, "…l'encre se lit sur les barres (" + Chrome.ContrastRatio(Chrome.Ink.Color, Chrome.BarBg.Color).ToString("0.0") + ":1, texte secondaire " + Chrome.ContrastRatio(Chrome.SoftText.Color, Chrome.BarBg.Color).ToString("0.0") + ":1)");
                    Check(Chrome.ContrastRatio(Chrome.RaisedBg.Color, Chrome.Accent.Color) >= 3, "…le texte du bouton principal se lit sur l'accent (" + Chrome.ContrastRatio(Chrome.RaisedBg.Color, Chrome.Accent.Color).ToString("0.0") + ":1)");
                    var expected = (byte)Math.Round(255 * Skin.BarOpacity(Skins.MuseumGlass(id)));
                    Check(Skin.BarAlphaForProbe == expected, "…des barres de verre (" + Skins.MuseumGlass(id) + " % : opacité " + Skin.BarAlphaForProbe + " sur 255)");
                    Check(Skin.UiFont != null && Skin.TitleFont != null && Skin.Plaque() is LinearGradientBrush && OrbitLine.Active, "…une police d'interface, une garalde pour les titres, la plaque, le filet en orbite");
                    Check(prefs.MuseumRowShownForProbe && !prefs.SecondRowShownForProbe, "…ses options se montrent dans les Préférences (pas de seconde couleur)");
                    Border card = null;
                    foreach (var border in prefs.GetVisualDescendants().OfType<Border>()) if (("skin:" + id).Equals(border.Tag)) card = border;
                    Check(card != null && Math.Abs(card.CornerRadius.TopLeft - Skin.Round(8)) < 0.01, "…les cadres faits main suivent (coins de " + Skin.Round(8).ToString("0.#") + " px)");
                    var pdf = Skin.PdfSkin();
                    var options = Skin.DressExport(new EncyclopediaOptions());
                    Check(pdf != null && options.Skin == id, "…l'Encyclopédie (HTML et PDF) part à ce skin");
                    await ProbeMuseumRefresh(shell, prefs, project, book, id, name);
                    Shot(prefs, "museum-" + id + "-preferences.png");

                    // Un toast : le titre en garalde, un filet en orbite posé dessus.
                    var toast = NoticeToast.Build("warning-bold", "Le fichier a changé sur le disque", "Un essai de la sonde.", "Recharger", null, "Ignorer", null);
                    var layers = toast.Child as Panel;
                    Check(layers != null && layers.Children.OfType<OrbitLine>().Count() == 1, "…un toast porte son filet en orbite");
                    Check(Skin.TileFade(Colors.Red) is DrawingBrush, "…une tuile colorée porte une tranche en S, plus le dégradé");
                    Check(Skin.MuseumDialogGlass < 0.8 && Skin.MuseumDialogGlass > 0.25, "…ses fenêtres de dialogue sont de verre (opacité " + Skin.MuseumDialogGlass.ToString("0.00") + ")");

                    prefs.Hide();
                    if (book != null)
                    {
                        shell.Binder.SelectItem(book.Id);
                        await Settle();
                        await Settle();
                        Shot(shell, "museum-" + id + "-livre.png");
                    }
                    if (sheet != null)
                    {
                        shell.Binder.SelectItem(sheet.Id);
                        await Settle();
                        await Settle();
                        Shot(shell, "museum-" + id + "-fiche.png");
                    }
                    if (text != null)
                    {
                        shell.Binder.SelectItem(text.Id);
                        await Settle();
                        await Settle();
                        Shot(shell, "museum-" + id + "-ecrit.png");
                    }
                    // L'autre visage (clair pour un skin de nuit, sombre pour Bois Blanc) : dérivé, il doit rester lisible.
                    AppSettings.DarkTheme = !night;
                    shell.ApplyAppearanceForProbe();
                    for (var i = 0; i < 3; i++) await Settle();
                    Check(Chrome.Hex(Chrome.WindowBg.Color) == Skins.MuseumBackground(id, !night) && Chrome.ContrastRatio(Chrome.Ink.Color, Chrome.BarBg.Color) >= 7,
                        "…son autre visage (" + (night ? "clair" : "sombre") + ") garde une encre lisible (" + Chrome.ContrastRatio(Chrome.Ink.Color, Chrome.BarBg.Color).ToString("0.0") + ":1)");
                    Shot(shell, "museum-" + id + "-ecrit-" + (night ? "clair" : "sombre") + ".png");
                    AppSettings.DarkTheme = night;
                    shell.ApplyAppearanceForProbe();
                    prefs.Show(shell);
                    await Settle();
                }

                // ------------------------------------------------ les trois options (sous Assermenté, sombre)
                prefs.SetMuseumGround("#14101F");
                for (var i = 0; i < 3; i++) await Settle();
                Check(AppSettings.SkinBackground == "#14101F" && Chrome.Hex(Chrome.WindowBg.Color) == "#14101F", "un fond choisi (prune) devient celui de la fenêtre");
                Check(Chrome.BarBg.Color != Chrome.WindowBg.Color && Chrome.ContrastRatio(Chrome.Ink.Color, Chrome.BarBg.Color) >= 7, "…barres et panneaux s'en déduisent, l'encre reste lisible");
                Shot(prefs, "museum-option-fond.png");
                prefs.SetMuseumGround("#FFFFFF");
                for (var i = 0; i < 3; i++) await Settle();
                Check(AppSettings.SkinBackground == "#FFFFFF" && Chrome.Luma(Chrome.WindowBg.Color) < 70, "un fond blanc choisi au sombre est ramené à une clarté de nuit (" + Chrome.Hex(Chrome.WindowBg.Color) + ")");
                prefs.SetMuseumGround(null);
                await Settle();
                Check(AppSettings.SkinBackground == null && Chrome.Hex(Chrome.WindowBg.Color) == Skins.MuseumBackground(Skins.MuseumOath, true), "« Celle du skin » rend le bordeaux");

                prefs.SetMuseumGlass(0);
                await Settle();
                // Le flou derrière le verre : les Préférences sont ouvertes, la fenêtre principale est derrière.
                Check(Math.Abs(ModalChrome.BlurOfForProbe(shell) - Skins.MuseumBlur(Skins.MuseumOath)) < 0.01, "sous un skin « Museum », ce qui est derrière une fenêtre de verre est flouté (" + ModalChrome.BlurOfForProbe(shell).ToString("0") + " px, le flou du skin)");
                prefs.SetMuseumBlur(0);
                await Settle();
                Check(AppSettings.SkinBlur == 0 && ModalChrome.BlurOfForProbe(shell) < 0.01, "flou réglé à 0 px : net, sans attendre de rouvrir la fenêtre");
                prefs.SetMuseumBlur(20);
                await Settle();
                Check(AppSettings.SkinBlur == 20 && Math.Abs(ModalChrome.BlurOfForProbe(shell) - 20) < 0.01, "…à 20 px : flouté d'autant");
                Shot(shell, "museum-option-flou-20.png");
                prefs.Hide();
                await Settle();
                Check(ModalChrome.BlurOfForProbe(shell) < 0.01 && ((Avalonia.Visual)shell.Content).Effect == null, "la fenêtre fermée, le flou est retiré");
                prefs.Show(shell);
                await Settle();
                prefs.SetMuseumBlur(Skins.MuseumBlur(Skins.MuseumOath));
                await Settle();
                Check(AppSettings.SkinGlass == 0 && Skin.BarAlphaForProbe == 255, "effet verre à 0 % : des barres pleines");
                prefs.SetMuseumGlass(100);
                await Settle();
                Check(AppSettings.SkinGlass == 100 && Skin.BarAlphaForProbe == (byte)Math.Round(255 * Skin.BarOpacity(100)), "…à 100 % : des barres de verre (opacité " + Skin.BarAlphaForProbe + " sur 255)");
                Shot(shell, "museum-option-verre-100.png");

                AppSettings.AccentColor = "#16A085";
                shell.ApplyAppearanceForProbe();
                await Settle();
                Check(Math.Abs(Chrome.HueOf(Chrome.Accent.Color) - Chrome.HueOf(Color.Parse("#16A085"))) < 2.5, "un accent choisi (sarcelle) remplace le laiton : sa teinte, à l'éclat du skin (" + Chrome.Hex(Chrome.Accent.Color) + ")");
                Shot(shell, "museum-option-accent.png");
                prefs.ResetColors();
                for (var i = 0; i < 3; i++) await Settle();
                Check(AppSettings.AccentColor == null && AppSettings.SkinBackground == null && AppSettings.SkinGlass == -1
                    && Math.Abs(Chrome.HueOf(Chrome.Accent.Color) - Chrome.HueOf(Color.Parse(Skins.MuseumAccent(Skins.MuseumOath)))) < 2.5, "« Réinitialiser les couleurs » rend le fond, le verre et le laiton");

                // Animations coupées : le filet dort.
                AppSettings.Animations = false;
                Check(!OrbitLine.Active, "animations coupées dans les Préférences : plus de filet en orbite");
                AppSettings.Animations = true;

                // ------------------------------------------------ retour à « modern »
                AppSettings.DarkTheme = false;
                prefs.SetSkin(Skins.Modern);
                for (var i = 0; i < 4; i++) await Settle();
                Check(Skin.Plain && Chrome.Ink.Color == inkBefore && Chrome.Accent.Color == accentModern && ReferenceEquals(Skin.ResourceForProbe(Skin.BarKey), Chrome.BarBg),
                    "retour à « Marabook modern » : la palette, l'indigo et les barres pleines reviennent");
                Check(Skin.Plaque() == null && Skin.TitleFont == null && !OrbitLine.Active && !prefs.MuseumRowShownForProbe, "…ni plaque, ni filet, ni options « Museum »");
                var plain = NoticeToast.Build("warning-bold", "Titre", "Corps", "Oui", null, null, null);
                Check(!(plain.Child is Panel) || !((Panel)plain.Child).Children.OfType<OrbitLine>().Any(), "…un toast est celui d'avant");
            }
            finally
            {
                if (prefs != null) prefs.Close();
                AppSettings.Skin = skinBefore;
                AppSettings.AccentColor = accentBefore;
                AppSettings.SkinBackground = groundBefore;
                AppSettings.SkinGlass = glassBefore;
                AppSettings.SkinBlur = blurBefore;
                AppSettings.DarkTheme = darkBefore;
                AppSettings.Animations = animationsBefore;
                for (var i = 0; i < colored.Count; i++) colored[i].CardColor = colorsBefore[i];
                shell.ApplyAppearanceForProbe();
                await Settle();
            }
        }
    }
}

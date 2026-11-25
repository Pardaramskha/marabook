using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde de l'ACCESSIBILITÉ (2.0.0, 22/11/2026) : tout est éteint d'office ; le zoom
    /// de l'interface (fenêtres, dialogues, menus), le focus visible, les couleurs pour daltoniens,
    /// la police de l'interface, le curseur épais et fixe, les liens soulignés, et le lecteur
    /// d'écran sur le texte (ce que Marabook annonce, ce que l'arbre d'accessibilité voit).
    /// MARABOOK_PROBE_ONLY=acces. MARABOOK_PROBE_HOLD=&lt;secondes&gt; : en fin de sonde, la fenêtre
    /// reste ouverte, lecteur d'écran allumé, le temps qu'un outil du système lise son arbre.</summary>
    public static partial class Probes
    {
        private static async Task ProbeAccessibility(MainWindow shell)
        {
            Console.WriteLine("-- accessibilité : zoom, focus, daltonisme, police, curseur, liens, lecteur d'écran (2.0.0, 22/11)");
            var project = shell.Project;
            var before = new object[] { AppSettings.UiZoom, AppSettings.FocusVisible, AppSettings.ScreenReader, AppSettings.ColorBlindSafe, AppSettings.UiFont, AppSettings.ThickCaret, AppSettings.SteadyCaret, AppSettings.UnderlineLinks, AppSettings.HighContrast, AppSettings.DarkTheme };
            BinderItem text = null;
            PreferencesDialog prefs = null;
            try
            {
                // ------------------------------------------------ d'office : rien
                Check(AppSettings.UiZoom == 1.0 && !AppSettings.FocusVisible && !AppSettings.ScreenReader && !AppSettings.ColorBlindSafe && AppSettings.UiFont == null && !AppSettings.ThickCaret && !AppSettings.SteadyCaret && !AppSettings.UnderlineLinks && !AppSettings.HighContrast,
                    "d'office, tous les réglages d'accessibilité sont éteints");
                Check(!(shell.Content is LayoutTransformControl) && !Accessibility.ZoomOn, "…et rien n'est posé autour de la fenêtre : l'interface est telle qu'avant");
                var said = ScreenReader.Count;
                ScreenReader.Announce("ceci ne doit pas se dire");
                Check(ScreenReader.Count == said, "…le lecteur d'écran éteint, Marabook n'annonce rien");

                var writings = project.Category(Project.KeyWritings);
                text = SceneText("Écrit de la sonde d'accessibilité", "Voici [[Keira]] au port, ce matin-là.", "Le second paragraphe est un peu plus long, pour qu'on y voie des mots.", "Fin.");
                text.Parent = writings;
                writings.Children.Add(text);
                shell.Binder.Rebuild();
                shell.Binder.SelectItem(text.Id, true);
                for (var i = 0; i < 5; i++) await Settle();
                var view = shell.GetVisualDescendants().OfType<ComposedView>().FirstOrDefault(delegate(ComposedView v) { return v.IsEffectivelyVisible; });
                Check(view != null && shell.CurrentForProbe == text, "un écrit est ouvert dans l'éditeur");
                if (view == null) return;

                // ------------------------------------------------ l'onglet
                prefs = new PreferencesDialog(shell, project);
                prefs.AppearanceChanged += shell.ApplyAppearanceForProbe;
                prefs.Show(shell);
                await Settle();
                foreach (var entry in prefs.Tabs.Items) { var t = entry as TabItem; if (t != null && "Accessibilité".Equals(t.Header)) prefs.Tabs.SelectedItem = t; }
                await Settle();
                await Settle();
                var groups = new List<string>();
                foreach (var border in prefs.GetVisualDescendants().OfType<Border>()) { var tag = border.Tag as string; if (tag != null && tag.StartsWith("prefs-group:") && border.IsEffectivelyVisible) groups.Add(tag.Substring(12)); }
                var unchecked_ = prefs.GetVisualDescendants().OfType<CheckBox>().Where(delegate(CheckBox c) { return c.IsEffectivelyVisible; }).All(delegate(CheckBox c) { return c.IsChecked != true; });
                Check(string.Join(" ", groups.ToArray()) == "see keyboard text reader" && prefs.AccessChecksForProbe == 7 && unchecked_, "Préférences › Accessibilité : quatre blocs (voir, au clavier, dans le texte, lecteur d'écran), sept cases, toutes décochées");
                Shot(prefs, "acces-preferences.png");

                // ------------------------------------------------ le zoom de l'interface
                var prefsWidth = prefs.Width;
                prefs.SetZoomForProbe(1.5);
                for (var i = 0; i < 4; i++) await Settle();
                var host = shell.Content as LayoutTransformControl;
                var scale = host == null ? null : host.LayoutTransform as ScaleTransform;
                Check(AppSettings.UiZoom == 1.5 && scale != null && Math.Abs(scale.ScaleX - 1.5) < 0.001 && Math.Abs(scale.ScaleY - 1.5) < 0.001, "zoom à 150 % : tout le contenu de la fenêtre est mis à l'échelle");
                var menuBar = shell.GetVisualDescendants().OfType<Menu>().FirstOrDefault();
                var topLeft = menuBar == null ? (Point?)null : menuBar.TranslatePoint(new Point(menuBar.Bounds.Width, menuBar.Bounds.Height), shell);
                Check(menuBar != null && topLeft.HasValue && Math.Abs(topLeft.Value.Y - menuBar.Bounds.Height * 1.5) < 1.5, "…la barre de menus occupe une fois et demie sa hauteur (" + (menuBar == null ? "-" : Math.Round(menuBar.Bounds.Height) + " → " + Math.Round(topLeft.Value.Y) + " px") + ")");
                Check(prefs.Content is LayoutTransformControl && prefs.Width > prefsWidth * 1.2, "…les Préférences, déjà ouvertes, suivent : leur contenu grossit et la fenêtre s'élargit (" + Math.Round(prefsWidth) + " → " + Math.Round(prefs.Width) + " px, dans la limite de l'écran)");
                Shot(shell, "acces-zoom-150.png");
                Shot(prefs, "acces-zoom-150-preferences.png");
                // Un dialogue ouvert ensuite, et un menu.
                shell.Binder.EditTile(text);
                for (var i = 0; i < 10 && OpenWindow<TileDialog>() == null; i++) await Settle();
                var tile = OpenWindow<TileDialog>();
                await Settle();
                Check(tile != null && Math.Abs(tile.Width - 660) < 1 && tile.GetVisualDescendants().OfType<LayoutTransformControl>().Any(delegate(LayoutTransformControl l) { return "ui-zoom".Equals(l.Tag); }), "un dialogue ouvert ensuite naît à l'échelle : 440 → " + (tile == null ? "-" : Math.Round(tile.Width).ToString()) + " px de large");
                if (tile != null) { Shot(tile, "acces-zoom-150-tuile.png"); tile.Close(); }
                await Settle();
                var menu = shell.Binder.BuildContextMenu(text);
                Ui.ShowMenu(menu, shell.Binder);
                for (var i = 0; i < 4; i++) await Settle();
                var popup = menu.GetVisualRoot() as PopupRoot;
                var popupScale = popup == null ? null : popup.Transform as ScaleTransform;
                // Sans écran (25/11), un menu n'a pas de fenêtre à lui : il est posé dans celle de l'application.
                if (Program.Silent) Console.WriteLine("  (sonde sans écran : l'échelle de la fenêtre d'un menu n'est pas jouée — « --visible » la joue)");
                else Check(popup != null && popupScale != null && Math.Abs(popupScale.ScaleX - 1.5) < 0.001, "un menu contextuel aussi (" + (popup == null ? "pas de menu" : popupScale == null ? "sans échelle" : "×" + popupScale.ScaleX) + ")");
                menu.Close();
                await Settle();
                prefs.SetZoomForProbe(1.0);
                for (var i = 0; i < 4; i++) await Settle();
                scale = (shell.Content as LayoutTransformControl) == null ? null : ((LayoutTransformControl)shell.Content).LayoutTransform as ScaleTransform;
                Check(AppSettings.UiZoom == 1.0 && scale != null && Math.Abs(scale.ScaleX - 1) < 0.001 && Math.Abs(prefs.Width - prefsWidth) < 2, "revenu à 100 %, tout reprend sa taille (les Préférences : " + Math.Round(prefs.Width) + " px)");
                Check(AppSettings.ClampZoom(0.4) == 1.0 && AppSettings.ClampZoom(3) == 2.0 && AppSettings.ClampZoom(1.23) == 1.25, "le zoom tient entre 100 et 200 %, par pas de 5 %");

                // ------------------------------------------------ le focus visible
                var button = prefs.GetVisualDescendants().OfType<CheckBox>().First(delegate(CheckBox c) { return c.IsEffectivelyVisible; });
                var ringBefore = button.FocusAdorner == null ? null : button.FocusAdorner.Build() as Border;
                prefs.SetAccessForProbe("focus-visible", true);
                for (var i = 0; i < 3; i++) await Settle();
                var ring = button.FocusAdorner == null ? null : button.FocusAdorner.Build() as Border;
                Check(AppSettings.FocusVisible && (ringBefore == null || !"focus-ring".Equals(ringBefore.Tag)) && ring != null && "focus-ring".Equals(ring.Tag) && ring.BorderThickness.Left >= 3, "« Montrer nettement ce qui a le clavier » : le cadre du focus devient un cadre épais (" + (ring == null ? "-" : ring.BorderThickness.Left + " px") + ")");
                prefs.SetAccessForProbe("focus-visible", false);
                for (var i = 0; i < 3; i++) await Settle();
                ring = button.FocusAdorner == null ? null : button.FocusAdorner.Build() as Border;
                Check(ring == null || !"focus-ring".Equals(ring.Tag), "…décochée, le cadre d'origine revient");

                // ------------------------------------------------ les couleurs pour daltoniens
                var ok = Chrome.Ok.Color;
                var danger = Chrome.Danger.Color;
                prefs.SetAccessForProbe("color-blind", true);
                for (var i = 0; i < 3; i++) await Settle();
                Check(AppSettings.ColorBlindSafe && Chrome.Ok.Color != ok && Chrome.Danger.Color != danger && Chrome.Ok.Color.B > Chrome.Ok.Color.G && Chrome.Danger.Color.B > Chrome.Danger.Color.G
                    && Chrome.ContrastRatio(Chrome.Ok.Color, Chrome.RaisedBg.Color) >= 4.5 && Chrome.ContrastRatio(Chrome.Warn.Color, Chrome.RaisedBg.Color) >= 4.5 && Chrome.ContrastRatio(Chrome.Danger.Color, Chrome.RaisedBg.Color) >= 4.5,
                    "« Couleurs adaptées au daltonisme » : le vert devient bleu, le rouge magenta, chacun lisible sur son fond (" + Chrome.Hex(Chrome.Ok.Color) + ", " + Chrome.Hex(Chrome.Warn.Color) + ", " + Chrome.Hex(Chrome.Danger.Color) + ")");
                Check(Chrome.Sign(0) == "✓" && Chrome.Sign(1) == "!" && Chrome.Sign(2) == "✕", "…et un signe double la couleur");
                AppSettings.DarkTheme = true;
                shell.ApplyAppearanceForProbe();
                Check(Chrome.ContrastRatio(Chrome.Ok.Color, Chrome.RaisedBg.Color) >= 4.5 && Chrome.ContrastRatio(Chrome.Danger.Color, Chrome.RaisedBg.Color) >= 4.5, "…au sombre aussi");
                AppSettings.DarkTheme = false;
                prefs.SetAccessForProbe("color-blind", false);
                for (var i = 0; i < 3; i++) await Settle();
                Check(Chrome.Ok.Color == ok && Chrome.Danger.Color == danger, "…décochée, le vert et le rouge reviennent");

                // ------------------------------------------------ la police de l'interface
                var fonts = Accessibility.InstalledFonts();
                Console.WriteLine("  polices proposées sur cette machine : " + (fonts.Count == 0 ? "aucune" : string.Join(", ", fonts.ToArray())));
                if (fonts.Count > 0)
                {
                    var family = shell.FontFamily;
                    AppSettings.UiFont = fonts[0];
                    shell.ApplyAppearanceForProbe();
                    for (var i = 0; i < 3; i++) await Settle();
                    Check(shell.FontFamily.Name.StartsWith(fonts[0], StringComparison.OrdinalIgnoreCase), "la police de l'interface devient « " + fonts[0] + " »");
                    Shot(shell, "acces-police.png");
                    AppSettings.UiFont = null;
                    shell.ApplyAppearanceForProbe();
                    for (var i = 0; i < 3; i++) await Settle();
                    Check(shell.FontFamily.Name == family.Name, "…et redevient celle de Marabook");
                }

                // ------------------------------------------------ le curseur, les liens
                view.MoveCaretForProbe(0, 3);
                await Settle();
                Check(Math.Abs(view.CaretWidthForProbe - 1.4) < 0.01, "le curseur de l'éditeur a son épaisseur d'origine");
                prefs.SetAccessForProbe("thick-caret", true);
                prefs.SetAccessForProbe("steady-caret", true);
                prefs.SetAccessForProbe("underline-links", true);
                for (var i = 0; i < 3; i++) await Settle();
                view.MoveCaretForProbe(0, 4);
                await Settle();
                Check(view.CaretWidthForProbe > 3, "« Curseur de texte épais » : " + view.CaretWidthForProbe + " px");
                view.BlinkForProbe();
                var always = true;
                for (var i = 0; i < 9; i++) { await Task.Delay(200); if (!view.CaretShownForProbe) always = false; }
                Check(always, "« qui ne clignote pas » : il reste allumé près de deux secondes d'affilée");
                var linksShown = AppSettings.ShowLinks;
                AppSettings.ShowLinks = true; // les liens montrés : c'est eux que l'option souligne
                shell.GetVisualDescendants().OfType<EditorView>().First().SetShowLinks(true);
                for (var i = 0; i < 3; i++) await Settle();
                Shot(shell, "acces-curseur-liens.png");
                shell.GetVisualDescendants().OfType<EditorView>().First().SetShowLinks(linksShown);
                prefs.SetAccessForProbe("thick-caret", false);
                prefs.SetAccessForProbe("steady-caret", false);
                prefs.SetAccessForProbe("underline-links", false);
                for (var i = 0; i < 3; i++) await Settle();
                view.MoveCaretForProbe(0, 3);
                view.BlinkForProbe();
                var blinked = false;
                for (var i = 0; i < 12; i++) { await Task.Delay(150); if (!view.CaretShownForProbe) blinked = true; }
                Check(Math.Abs(view.CaretWidthForProbe - 1.4) < 0.01 && blinked, "…décochés, le curseur redevient fin et clignote");

                // ------------------------------------------------ le lecteur d'écran
                Check(ScreenReader.DescribeMove("Voici Keira au port.", 0, 0, 0, 1, 0, 20, false) == "o" && ScreenReader.DescribeMove("Voici Keira au port.", 0, 4, 0, 5, 0, 20, false) == "espace" && ScreenReader.DescribeMove("Voici Keira au port.", 0, 6, 0, 5, 0, 20, false) == "espace"
                    && ScreenReader.DescribeMove("Voici Keira au port.", 0, 19, 0, 20, 0, 20, false) == "fin de paragraphe" && ScreenReader.DescribeMove("Voici Keira au port.", 0, 5, 0, 6, 0, 20, false) == "K majuscule", "au caractère : la lettre, « espace », « K majuscule », « fin de paragraphe »");
                Check(ScreenReader.DescribeMove("Voici Keira au port.", 0, 0, 0, 6, 0, 20, false) == "Keira" && ScreenReader.DescribeMove("Voici Keira au port.", 0, 12, 0, 6, 0, 20, false) == "Keira" && ScreenReader.DescribeMove("Voici Keira au port.", 0, 2, 0, 12, 0, 12, false) == "fin de ligne", "au mot (Ctrl + flèche) : le mot où l'on arrive ; en bout de ligne : « fin de ligne »");
                Check(ScreenReader.DescribeMove("Une ligne, puis une autre.", 0, 3, 0, 14, 11, 26, true) == "puis une autre." && ScreenReader.DescribeMove("", 0, 3, 1, 0, 0, 0, true) == "ligne vide" && ScreenReader.DescribeMove("Abc", 0, 2, 0, 2, 0, 3, false) == null, "à la ligne : la ligne entière ; un paragraphe vide : « ligne vide » ; rien si rien n'a bougé");
                Check(ScreenReader.Spoken("a￼b\nc") == "ab c" && ScreenReader.Spoken(new string('x', 900)).Length <= 601 && ScreenReader.WordAt("l'eau-forte, dit-il", 3) == "l'eau-forte" && ScreenReader.Spoken("Voici [[Keira]] et [[fiche:x|la capitaine]].") == "Voici Keira et la capitaine.", "ce qui se dit est nettoyé et borné, les marques de lien effacées ; un mot garde son apostrophe et son trait d'union");

                prefs.SetAccessForProbe("screen-reader", true);
                await Settle();
                var region = shell.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(delegate(TextBlock t) { return "annonces".Equals(t.Tag); });
                Check(AppSettings.ScreenReader && region != null && AutomationProperties.GetLiveSetting(region) == AutomationLiveSetting.Assertive && region.IsEffectivelyVisible, "le lecteur d'écran allumé : la fenêtre a sa zone d'annonces, « vivante » pour l'arbre d'accessibilité");
                view.ReaderFocusForProbe = true;
                view.MoveCaretForProbe(0, 0);
                view.ReadArrivalForProbe();
                Check(ScreenReader.Last == "Texte de « Écrit de la sonde d'accessibilité », paragraphe 1 sur 3. Voici Keira au port, ce matin-là." && region.Text.Trim() == ScreenReader.Last && AutomationProperties.GetName(region).Trim() == ScreenReader.Last, "un écrit qui prend le clavier s'annonce : « " + ScreenReader.Last + " »");
                var flat = view.ReaderParagraph();
                view.MoveCaretForProbe(0, 1);
                var one = ScreenReader.Last;
                view.MoveCaretForProbe(0, 2);
                Check(one == "o" && ScreenReader.Last == "i", "flèche droite, deux fois : « " + one + " », « " + ScreenReader.Last + " »");
                var word = flat.IndexOf("port", StringComparison.Ordinal);
                view.MoveCaretForProbe(0, word);
                Check(word > 0 && ScreenReader.Last == "port", "un saut au mot : « " + ScreenReader.Last + " »");
                view.MoveCaretForProbe(1, 0);
                Check(ScreenReader.Last.StartsWith("Le second paragraphe"), "flèche bas, paragraphe suivant : sa ligne — « " + ScreenReader.Last + " »");
                var count = ScreenReader.Count;
                view.MoveCaretForProbe(1, 0);
                Check(ScreenReader.Count == count, "rien n'est redit quand le curseur ne bouge pas");
                view.SelectForProbe(1, 0, 9);
                Check(ScreenReader.Last == "sélectionné : Le second", "une sélection : « " + ScreenReader.Last + " »");
                view.MoveCaretForProbe(2, 0);
                Check(ScreenReader.Last == "sélection retirée", "…retirée : « " + ScreenReader.Last + " »");
                view.MoveCaretForProbe(2, 1);
                Check(ScreenReader.Last == "i", "puis le curseur reprend : « " + ScreenReader.Last + " »");

                // Ce que l'arbre d'accessibilité voit de la surface.
                var peer = ControlAutomationPeer.CreatePeerForElement(view);
                var value = peer as IValueProvider;
                Check(peer is ComposedTextPeer && peer.GetAutomationControlType() == AutomationControlType.Edit && peer.GetName() == "Texte de « Écrit de la sonde d'accessibilité »" && value != null && value.Value == "Fin." && !value.IsReadOnly && peer.IsKeyboardFocusable(),
                    "la surface de texte se présente comme une zone de texte : son nom (« " + peer.GetName() + " »), sa valeur (le paragraphe du curseur : « " + (value == null ? "-" : value.Value) + " »)");
                prefs.SetAccessForProbe("screen-reader", false);
                await Settle();
                count = ScreenReader.Count;
                view.MoveCaretForProbe(0, 5);
                Check(ScreenReader.Count == count && peer.GetAutomationControlType() != AutomationControlType.Edit, "éteint, Marabook se tait et la surface se présente comme avant");

                // ------------------------------------------------ pour un outil du système
                int hold;
                if (int.TryParse(Environment.GetEnvironmentVariable("MARABOOK_PROBE_HOLD"), out hold) && hold > 0)
                {
                    prefs.SetAccessForProbe("screen-reader", true);
                    prefs.Close();
                    prefs = null;
                    await Settle();
                    view.Focus();
                    view.MoveCaretForProbe(0, 0);
                    view.ReadArrivalForProbe();
                    Console.WriteLine("  [acces] en attente " + hold + " s : lecteur d'écran allumé, écrit ouvert (processus " + Environment.ProcessId + ")");
                    Console.Out.Flush();
                    for (var i = 0; i < hold * 2; i++)
                    {
                        await Task.Delay(500);
                        // Le curseur avance d'un cran toutes les deux secondes : de quoi voir les annonces changer.
                        if (i % 4 == 3) view.MoveCaretForProbe(0, 1 + (i / 4) % 12);
                    }
                }
            }
            finally
            {
                if (prefs != null) prefs.Close();
                AppSettings.UiZoom = (double)before[0];
                AppSettings.FocusVisible = (bool)before[1];
                AppSettings.ScreenReader = (bool)before[2];
                AppSettings.ColorBlindSafe = (bool)before[3];
                AppSettings.UiFont = (string)before[4];
                AppSettings.ThickCaret = (bool)before[5];
                AppSettings.SteadyCaret = (bool)before[6];
                AppSettings.UnderlineLinks = (bool)before[7];
                AppSettings.HighContrast = (bool)before[8];
                AppSettings.DarkTheme = (bool)before[9];
                shell.ApplyAppearanceForProbe();
                if (text != null && text.Parent != null) text.Parent.Children.Remove(text);
                shell.Binder.Rebuild();
                shell.Binder.SelectItem(project.Category(Project.KeyWritings).Id);
                await Settle();
            }
        }
    }
}

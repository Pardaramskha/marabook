using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LES PRÉFÉRENCES, LOT DU 22/11/2026 (Rémi) :
    ///  - « Personnalisation » (ex-« Général ») rangée en BLOCS à bord arrondi,
    ///    un par famille de réglages — Group ;
    ///  - la PALETTE : toutes les couleurs que Marabook propose, nommées, et
    ///    les couleurs personnalisées de l'auteur (ajouter, nommer, changer,
    ///    retirer) ;
    ///  - les Sauvegardes, passées dans l'onglet « Projet » ;
    ///  - l'onglet « Accessibilité » (entre Auteur et Raccourcis) ;
    ///  - l'onglet « Dépendances » : ce que Marabook a téléchargé, son poids,
    ///    où il est, ce qu'on perd à le retirer.</summary>
    public partial class PreferencesDialog
    {
        /// <summary>Un BLOC : une famille de réglages dans un cadre à bord arrondi.</summary>
        private static StackPanel Group(Panel host, string tag)
        {
            var inner = new StackPanel();
            host.Children.Add(new Border
            {
                BorderBrush = Chrome.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 14, 16, 16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = inner,
                Tag = "prefs-group:" + tag
            });
            return inner;
        }

        // ------------------------------------------------------------ la palette

        private StackPanel _paletteCustom;

        /// <summary>La couleur retirée était aussi dans le projet ouvert : il a changé.</summary>
        public event Action ProjectTouched;

        private void BuildPaletteSection(StackPanel group)
        {
            group.Children.Add(Caption("Palette"));
            group.Children.Add(Note("Les couleurs que Marabook propose partout où une couleur se choisit, sous leur nom. Vos couleurs personnalisées s'ajoutent à toutes ces listes, d'un projet à l'autre. Une couleur déjà posée (une tuile, une icône, un événement) garde la sienne : changer la palette ne recolore rien."));
            foreach (var paletteGroup in ColorPalette.Groups)
            {
                var head = new TextBlock { Margin = new Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap };
                head.Inlines.Add(new Avalonia.Controls.Documents.Run(paletteGroup.Name) { FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
                head.Inlines.Add(new Avalonia.Controls.Documents.Run("  " + paletteGroup.Use) { Foreground = Chrome.SoftText, FontSize = 12 });
                group.Children.Add(head);
                var chips = new WrapPanel { Tag = "palette-defaults" };
                foreach (var color in paletteGroup.Colors) chips.Children.Add(PaletteChip(color.Hex, color.Name));
                group.Children.Add(chips);
            }

            var mine = new TextBlock { Text = "Mes couleurs", FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, Margin = new Thickness(0, 14, 0, 4) };
            group.Children.Add(mine);
            _paletteCustom = new StackPanel { Tag = "palette-custom" };
            group.Children.Add(_paletteCustom);
            var add = Buttons.IconText("plus-bold", "Ajouter une couleur…", "Une couleur de plus dans votre palette : son code, et le nom que vous lui donnez", Buttons.Bar, Buttons.Look.Outline);
            add.Tag = "palette-add";
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 8, 0, 0);
            add.Click += async delegate
            {
                await ColorDialog.Ask(this); // elle rejoint la palette
                RebuildPalette();
            };
            group.Children.Add(add);
            if (_project != null && ColorPalette.Absorb(_project.CustomColors)) AppSettings.Save(); // celles que le projet portait déjà
            RebuildPalette();
        }

        /// <summary>Une couleur d'origine : sa pastille et son nom ; son code au survol.</summary>
        private static Control PaletteChip(string hex, string name)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Ink.Parse(hex).ToColor()), BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1)
            });
            row.Children.Add(new TextBlock { Text = name, Foreground = Chrome.Ink, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            var chip = new Border
            {
                Background = Chrome.FieldBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
                Padding = new Thickness(8, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6), Child = row, Tag = "palette-chip:" + hex
            };
            ToolTip.SetTip(chip, hex);
            return chip;
        }

        private void RebuildPalette()
        {
            if (_paletteCustom == null) return;
            _paletteCustom.Children.Clear();
            if (ColorPalette.Custom.Count == 0)
            {
                _paletteCustom.Children.Add(new TextBlock { Text = "Aucune pour l'instant. « Nouvelle couleur… », dans n'importe quel choix de couleur, en ajoute une ici.", Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                return;
            }
            foreach (var entry in new List<PaletteColor>(ColorPalette.Custom))
            {
                var color = entry;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left, Tag = "palette-row:" + color.Hex };
                var swatch = new Button
                {
                    Width = 30, Height = 30, Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand),
                    Content = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Ink.Parse(color.Hex).ToColor()), BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1) },
                    Tag = "palette-edit"
                };
                ToolTip.SetTip(swatch, "Changer cette couleur");
                swatch.Click += async delegate { await EditPaletteColor(color); };
                DockPanel.SetDock(swatch, Dock.Left);
                row.Children.Add(swatch);
                var remove = Buttons.Icon("x-bold", "Retirer cette couleur de la palette. Ce qui la porte déjà la garde.", Buttons.Compact, Buttons.Look.Calm);
                remove.Tag = "palette-remove";
                remove.Click += delegate { RemovePaletteColor(color); };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var code = new TextBlock { Text = color.Hex, Foreground = Chrome.SoftText, FontSize = 12, FontFamily = new FontFamily("Consolas, Menlo, monospace"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0), Width = 64 };
                DockPanel.SetDock(code, Dock.Right);
                row.Children.Add(code);
                var name = new TextBox { Text = color.Name, Watermark = "Nom de la couleur", Width = 300, Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Tag = "palette-name" };
                Action commit = delegate
                {
                    var text = (name.Text ?? "").Trim();
                    if (text == color.Name) return;
                    ColorPalette.Rename(color, text);
                    AppSettings.Save();
                };
                name.LostFocus += delegate { commit(); };
                name.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.Key == Key.Return || e.Key == Key.Enter) { commit(); e.Handled = true; }
                };
                row.Children.Add(name);
                _paletteCustom.Children.Add(row);
            }
        }

        private async System.Threading.Tasks.Task EditPaletteColor(PaletteColor color)
        {
            var picked = await ColorDialog.Edit(this, "Changer la couleur", color.Hex, color.Name, "Enregistrer");
            if (picked == null) return;
            if (!ColorPalette.Recolor(color, picked[0]))
            {
                await MessageDialog.Show(this, "Cette couleur est déjà dans votre palette.", "Palette", MessageButtons.OK, MessageIcon.Information);
                return;
            }
            ColorPalette.Rename(color, picked[1]);
            AppSettings.Save();
            RebuildPalette();
        }

        internal void RemovePaletteColor(PaletteColor color)
        {
            ColorPalette.Remove(color);
            AppSettings.Save();
            // Le projet ouvert la portait peut-être (d'avant la palette) : elle en sort aussi.
            if (_project != null && _project.CustomColors.RemoveAll(delegate(string hex) { return string.Equals(hex, color.Hex, StringComparison.OrdinalIgnoreCase); }) > 0)
            {
                var handler = ProjectTouched;
                if (handler != null) handler();
            }
            RebuildPalette();
        }

        internal int PaletteRowsForProbe { get { return _paletteCustom == null ? -1 : ColorPalette.Custom.Count; } }
        internal void RebuildPaletteForProbe() { RebuildPalette(); }

        // ------------------------------------------------------------ les sauvegardes (onglet Projet)

        /// <summary>Les COPIES HORODATÉES (1.1.0, 30/10) : la bascule et l'intervalle
        /// minimal, sans infobulle (Rémi). Cinq copies, dans le dossier de
        /// données de Marabook — ni nombre ni dossier à régler. Le
        /// « .plot.bak » de la version d'avant est toujours écrit (caché),
        /// et retiré à la fermeture propre. Dans l'onglet « Projet » depuis le 22/11.</summary>
        private void BuildBackupsSection(Panel panel)
        {
            panel.Children.Add(Caption("Sauvegardes", 18));
            var backupsOn = new CheckBox { Content = "Copies horodatées du projet", IsChecked = AppSettings.BackupsEnabled, Margin = new Thickness(0, 6, 0, 0), Tag = "backups-enabled" };
            backupsOn.IsCheckedChanged += delegate
            {
                if (AppSettings.BackupsEnabled == (backupsOn.IsChecked == true)) return;
                AppSettings.BackupsEnabled = backupsOn.IsChecked == true;
                AppSettings.Save();
            };
            panel.Children.Add(backupsOn);
            var backupRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var intervalBox = new TextBox { Width = 56, Text = AppSettings.BackupIntervalMinutes.ToString(), Tag = "backup-interval" };
            Action commitBackups = delegate
            {
                int interval;
                if (!int.TryParse((intervalBox.Text ?? "").Trim(), out interval)) interval = AppSettings.BackupIntervalMinutes;
                interval = BackupPolicy.ClampInterval(interval);
                intervalBox.Text = interval.ToString();
                if (interval == AppSettings.BackupIntervalMinutes) return;
                AppSettings.BackupIntervalMinutes = interval;
                AppSettings.Save();
            };
            intervalBox.LostFocus += delegate { commitBackups(); };
            intervalBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Return || e.Key == Key.Enter) { commitBackups(); e.Handled = true; }
            };
            backupRow.Children.Add(intervalBox);
            backupRow.Children.Add(new TextBlock { Text = "minutes au moins entre deux copies", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            panel.Children.Add(backupRow);
            panel.Children.Add(Note("Les " + Backups.Keep + " dernières copies de chaque projet sont gardées dans le dossier de Marabook, jamais à côté de vos fichiers ; Fichier › Ouvrir le dossier des sauvegardes y mène. Chaque copie est un .plot complet qui s'ouvre comme un autre. Si le projet est illisible, Marabook propose d'abord la version d'avant, puis ces copies.", 6, 0));
        }

        // ------------------------------------------------------------ accessibilité

        private CheckBox _contrastCheck;
        private Slider _zoomSlider;
        private TextBlock _zoomLabel;
        private DispatcherTimerLite _zoomTimer;
        private readonly Dictionary<string, CheckBox> _accessChecks = new Dictionary<string, CheckBox>();

        /// <summary>Une case d'accessibilité : son libellé, ce qu'elle fait, et le réglage qu'elle tient.
        /// Toutes sont décochées d'office.</summary>
        private CheckBox AccessCheck(Panel host, string tag, string label, string note, Func<bool> read, Action<bool> write)
        {
            var check = new CheckBox { Content = label, IsChecked = read(), Margin = new Thickness(0, host.Children.Count > 1 ? 10 : 6, 0, 0), Tag = tag };
            check.IsCheckedChanged += delegate
            {
                var wanted = check.IsChecked == true;
                if (read() == wanted) return;
                write(wanted);
                RaiseAppearanceChanged();
            };
            host.Children.Add(check);
            if (note != null) host.Children.Add(Note(note, 4, 0, 22));
            _accessChecks[tag] = check;
            return check;
        }

        private Control BuildAccessibilityTab()
        {
            var page = TabPanel();
            page.Children.Add(Note("Tout est éteint d'office : Marabook ne change rien tant que vous n'avez rien coché. Chaque réglage s'applique aussitôt, et ne touche jamais à un texte exporté.", 0, 12));

            // ---- voir
            var see = Group(page, "see");
            see.Children.Add(Caption("Voir"));
            _contrastCheck = AccessCheck(see, "high-contrast", "Augmenter le contraste",
                "Les textes passent au noir franc (au blanc en mode sombre), les textes secondaires et les filets foncent nettement, la couleur d'accent se renforce. Au clair comme au sombre, par-dessus n'importe quel skin.",
                delegate { return AppSettings.HighContrast; }, delegate(bool on) { AppSettings.HighContrast = on; });
            AccessCheck(see, "color-blind", "Couleurs adaptées au daltonisme",
                "Le vert « ça va », l'orange « attention » et le rouge « problème » deviennent bleu, jaune et magenta, que l'on distingue quand on confond le rouge et le vert ; dans les rapports de l'Atelier, un signe (✓, !, ✕) double la couleur.",
                delegate { return AppSettings.ColorBlindSafe; }, delegate(bool on) { AppSettings.ColorBlindSafe = on; });

            var zoomHead = new TextBlock { Text = "Taille de l'interface", Foreground = Chrome.Ink, Margin = new Thickness(0, 14, 0, 0) };
            see.Children.Add(zoomHead);
            var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _zoomSlider = new Slider { Minimum = 100, Maximum = 200, Value = Math.Round(AppSettings.UiZoom * 100), TickFrequency = 5, IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center, Tag = "ui-zoom" };
            _zoomLabel = new TextBlock { Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), Width = 52, Tag = "ui-zoom-label" };
            var zoomReset = Buttons.Text("100 %", "Revenir à la taille d'origine", Buttons.Compact, Buttons.Look.Outline);
            zoomReset.Tag = "ui-zoom-reset";
            zoomReset.VerticalAlignment = VerticalAlignment.Center;
            zoomReset.Click += delegate { _zoomSlider.Value = 100; };
            zoomRow.Children.Add(_zoomSlider);
            zoomRow.Children.Add(_zoomLabel);
            zoomRow.Children.Add(zoomReset);
            see.Children.Add(zoomRow);
            Action label = delegate { _zoomLabel.Text = ((int)Math.Round(_zoomSlider.Value)).ToString() + " %"; };
            label();
            _zoomSlider.ValueChanged += delegate
            {
                label();
                // Le réglage glisse : la fenêtre ne change de taille qu'une fois qu'il s'est posé.
                if (_zoomTimer != null) _zoomTimer.Stop();
                _zoomTimer = DispatcherTimerLite.After(350, delegate { SetZoom(_zoomSlider.Value / 100.0); });
            };
            see.Children.Add(Note("Agrandit tout : menus, boutons, listes, fenêtres, infobulles — de 100 à 200 %. La taille du texte que vous écrivez se règle à part, par le zoom de la barre d'état : les deux se multiplient.", 4, 0));

            see.Children.Add(new TextBlock { Text = "Police de l'interface", Foreground = Chrome.Ink, Margin = new Thickness(0, 14, 0, 4) });
            var fontBox = new ComboBox { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left, Tag = "ui-font" };
            fontBox.Items.Add("Celle de Marabook");
            var fonts = Accessibility.InstalledFonts();
            foreach (var name in fonts) fontBox.Items.Add(name);
            var current = AppSettings.UiFont == null ? -1 : fonts.FindIndex(delegate(string f) { return string.Equals(f, AppSettings.UiFont, StringComparison.OrdinalIgnoreCase); });
            fontBox.SelectedIndex = current < 0 ? 0 : current + 1;
            fontBox.SelectionChanged += delegate
            {
                var chosen = fontBox.SelectedIndex <= 0 ? null : fontBox.SelectedItem as string;
                if (chosen == AppSettings.UiFont) return;
                AppSettings.UiFont = chosen;
                RaiseAppearanceChanged();
            };
            see.Children.Add(fontBox);
            var hasDyslexic = fonts.Exists(delegate(string f) { return f.StartsWith("OpenDyslexic", StringComparison.OrdinalIgnoreCase) || f.StartsWith("Atkinson", StringComparison.OrdinalIgnoreCase) || f.StartsWith("Lexend", StringComparison.OrdinalIgnoreCase); });
            see.Children.Add(Note("Parmi les polices installées sur cette machine, celles qu'on recommande quand la lecture est difficile (dyslexie, basse vision). "
                + (hasDyslexic ? "" : "OpenDyslexic, Atkinson Hyperlegible et Lexend, gratuites, sont faites pour cela : installez-les dans votre système, elles paraîtront ici. ")
                + "La police de vos textes ne change pas : elle se règle par style.", 4, 0));

            // ---- le clavier
            var keyboard = Group(page, "keyboard");
            keyboard.Children.Add(Caption("Au clavier"));
            AccessCheck(keyboard, "focus-visible", "Montrer nettement ce qui a le clavier",
                "Quand vous avancez à la touche Tab ou aux flèches, un cadre épais entoure le bouton, la case ou le champ où vous êtes.",
                delegate { return AppSettings.FocusVisible; }, delegate(bool on) { AppSettings.FocusVisible = on; });

            // ---- le texte
            var text = Group(page, "text");
            text.Children.Add(Caption("Dans le texte"));
            AccessCheck(text, "thick-caret", "Curseur de texte épais", null,
                delegate { return AppSettings.ThickCaret; }, delegate(bool on) { AppSettings.ThickCaret = on; });
            AccessCheck(text, "steady-caret", "Curseur de texte qui ne clignote pas", null,
                delegate { return AppSettings.SteadyCaret; }, delegate(bool on) { AppSettings.SteadyCaret = on; });
            AccessCheck(text, "underline-links", "Souligner les liens",
                "Quand les liens sont montrés (le bouton « Afficher les liens » du ruban), ceux qui mènent à une fiche ou à une date sont soulignés à l'écran, en plus de leur couleur. Rien ne change à l'export.",
                delegate { return AppSettings.UnderlineLinks; }, delegate(bool on) { AppSettings.UnderlineLinks = on; });

            // ---- le lecteur d'écran
            var reader = Group(page, "reader");
            reader.Children.Add(Caption("Lecteur d'écran"));
            AccessCheck(reader, "screen-reader", "Dire le texte des écrits au lecteur d'écran",
                "Le texte de Marabook est dessiné en vraies pages, qu'un lecteur d'écran (Narrateur, NVDA, JAWS, VoiceOver) ne sait pas lire seul. Cochée, Marabook lui dit lui-même où arrive le curseur : le caractère aux flèches gauche et droite, le mot avec Ctrl, la ligne aux flèches haut et bas, ce qui est sélectionné, et le titre de l'écrit quand il prend le clavier.",
                delegate { return AppSettings.ScreenReader; }, delegate(bool on) { AppSettings.ScreenReader = on; });
            reader.Children.Add(Note("Ce n'est pas la lecture native d'un champ de texte : les commandes de lecture propres à votre lecteur d'écran (« lire depuis le curseur », le mode revue) n'agissent pas sur la page ; ce sont les touches de Marabook qui déplacent le curseur et font parler. Les menus, les boutons, les champs et les fiches, eux, se lisent comme partout.", 6, 0, 22));
            return page;
        }

        private void SetZoom(double zoom)
        {
            zoom = AppSettings.ClampZoom(zoom);
            if (Math.Abs(zoom - AppSettings.UiZoom) < 0.001) return;
            AppSettings.UiZoom = zoom;
            RaiseAppearanceChanged();
        }

        internal void SetContrastForProbe(bool on) { _contrastCheck.IsChecked = on; }
        internal void SetAccessForProbe(string tag, bool on) { _accessChecks[tag].IsChecked = on; }
        internal void SetZoomForProbe(double zoom) { SetZoom(zoom); if (_zoomSlider != null) _zoomSlider.Value = Math.Round(AppSettings.UiZoom * 100); }
        internal int AccessChecksForProbe { get { return _accessChecks.Count; } }

        // ------------------------------------------------------------ dépendances

        private StackPanel _dependencyList;
        private TextBlock _dependencyStatus;

        private Control BuildDependenciesTab()
        {
            var panel = TabPanel();
            panel.Children.Add(Caption("Ce que Marabook a téléchargé"));
            panel.Children.Add(Note("Marabook n'est pas livré avec tout : certains outils demandent une bibliothèque tierce, qu'il télécharge la première fois qu'on en a besoin, après votre accord. Voici ce qui est installé sur cette machine — son poids, où il se trouve — et ce que l'application perd si vous le retirez. Rien n'est définitif : tout se réinstalle à la demande."));
            _dependencyStatus = Note("", 4, 8);
            _dependencyStatus.IsVisible = false;
            _dependencyStatus.Tag = "dependency-status";
            panel.Children.Add(_dependencyStatus);
            _dependencyList = new StackPanel { Tag = "dependencies" };
            panel.Children.Add(_dependencyList);
            RebuildDependencies();
            return panel;
        }

        private void RebuildDependencies()
        {
            if (_dependencyList == null) return;
            _dependencyList.Children.Clear();
            var all = Dependencies.Installed();
            if (all.Count == 0)
            {
                _dependencyList.Children.Add(new TextBlock { Text = "Rien pour l'instant. Les composants s'installent depuis l'Atelier (section Outils de la Pile) ou quand un outil en a besoin ; les modules, depuis l'onglet DLC.", Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, Tag = "dependencies-empty" });
                return;
            }
            long total = 0;
            foreach (var entry in all)
            {
                var dependency = entry;
                var size = Dependencies.SizeOf(dependency.Folder);
                total += size;
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var text = new StackPanel();
                var head = new TextBlock { TextWrapping = TextWrapping.Wrap };
                head.Inlines.Add(new Avalonia.Controls.Documents.Run(dependency.Name + (string.IsNullOrEmpty(dependency.Version) ? "" : " " + dependency.Version)) { FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
                head.Inlines.Add(new Avalonia.Controls.Documents.Run("   " + Dependencies.SizeLabel(size)) { Foreground = Chrome.SoftText, FontSize = 12 });
                text.Children.Add(head);
                text.Children.Add(new TextBlock { Text = dependency.Role, Foreground = Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                var loses = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), Tag = "dependency-loses" };
                loses.Inlines.Add(new Avalonia.Controls.Documents.Run("Ce que vous perdez en le retirant : ") { FontWeight = FontWeight.SemiBold, Foreground = Chrome.Warn });
                loses.Inlines.Add(new Avalonia.Controls.Documents.Run(dependency.Loses + "." + (dependency.Keeps.Length > 0 ? " " + dependency.Keeps : "")) { Foreground = Chrome.Ink });
                text.Children.Add(loses);
                text.Children.Add(new TextBlock { Text = dependency.Source, Foreground = Chrome.FaintText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
                text.Children.Add(new SelectableTextBlock { Text = dependency.Folder, Foreground = Chrome.FaintText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0), Tag = "dependency-folder" });
                grid.Children.Add(text);

                var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 0, 0, 0), Width = 132 };
                var show = Buttons.IconText("folder-open", "Montrer", "Ouvrir le dossier où ce composant est installé", Buttons.Bar, Buttons.Look.Outline);
                show.HorizontalAlignment = HorizontalAlignment.Stretch;
                show.Click += delegate
                {
                    try { AppPlatform.RevealInFolder(dependency.Folder); }
                    catch (Exception) { }
                };
                actions.Children.Add(show);
                var remove = Buttons.Text("Désinstaller", "Retire " + dependency.Name + " du disque. Il pourra se réinstaller.", Buttons.Bar, Buttons.Look.Outline);
                remove.HorizontalAlignment = HorizontalAlignment.Stretch;
                remove.Margin = new Thickness(0, 6, 0, 0);
                remove.Tag = "dependency-remove:" + dependency.Key;
                remove.Click += async delegate
                {
                    var question = "Désinstaller " + dependency.Name + " (" + Dependencies.SizeLabel(size) + ") ?\n\nVous perdrez : " + dependency.Loses + "."
                        + (dependency.Keeps.Length > 0 ? "\n" + dependency.Keeps : "") + "\n\nIl se réinstalle à la demande.";
                    if (await MessageDialog.Show(this, question, "Dépendances", MessageButtons.YesNo, MessageIcon.Question) != MessageResult.Yes) return;
                    RemoveDependency(dependency);
                };
                actions.Children.Add(remove);
                Grid.SetColumn(actions, 1);
                grid.Children.Add(actions);
                _dependencyList.Children.Add(new Border
                {
                    Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = grid, Tag = "dependency:" + dependency.Key
                });
            }
            _dependencyList.Children.Add(new TextBlock { Text = "En tout : " + Dependencies.SizeLabel(total) + ".", Foreground = Chrome.SoftText, FontSize = 12, Tag = "dependencies-total" });
        }

        internal void RemoveDependency(Dependency dependency)
        {
            string problem;
            try { problem = dependency.Remove(); }
            catch (Exception error)
            {
                if (!(error is System.IO.IOException) && !(error is UnauthorizedAccessException)) throw;
                problem = dependency.Name + " n'a pas pu être retiré : " + error.Message;
            }
            _dependencyStatus.Text = problem ?? dependency.Name + " est désinstallé.";
            _dependencyStatus.Foreground = problem == null ? Chrome.Ok : Chrome.Danger;
            _dependencyStatus.IsVisible = true;
            RebuildDependencies();
            RefreshModules();
        }

        internal int DependencyRowsForProbe { get { return Dependencies.Installed().Count; } }
        internal void RebuildDependenciesForProbe() { RebuildDependencies(); }
        internal string DependencyStatusForProbe { get { return _dependencyStatus == null ? "" : _dependencyStatus.Text ?? ""; } }
    }
}

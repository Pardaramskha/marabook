using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Les Préférences (portées de View/PreferencesDialog.cs),
    /// appliquées en direct. Onglets : Personnalisation, Édition, Correction,
    /// Styles globaux, Catalogue de polices, Auteur, Raccourcis, DLC. Taille
    /// unique, bornée à l'écran ; les chips se replient sur plusieurs rangées
    /// quand la fenêtre est étroite (WrapPanel). Tout est dans AppSettings.
    /// Styles globaux et Catalogue de polices ont leurs éditeurs (P2).</summary>
    public class PreferencesDialog : Window
    {
        public event Action AppearanceChanged;
        public event Action ProofingChanged;
        public event Action ShortcutsChanged;
        public event Action GlobalStylesChanged;

        private readonly Project _project;
        private readonly WrapPanel _swatches;
        private CheckBox _whitePaper, _darkCheck;

        private static readonly string[][] Accents =
        {
            new[] { Chrome.DefaultAccent, "Indigo (défaut)" },
            new[] { "#2980B9", "Bleu" },
            new[] { "#16A085", "Sarcelle" },
            new[] { "#27AE60", "Vert" },
            new[] { "#C9A227", "Or" },
            new[] { "#E67E22", "Orange" },
            new[] { "#C0392B", "Rouge" },
            new[] { "#C2185B", "Framboise" },
            new[] { "#8E44AD", "Violet" },
            new[] { "#5D6D7E", "Ardoise" },
        };

        public TabControl Tabs { get; private set; }

        public PreferencesDialog(Window owner, Project project)
        {
            _project = project;
            Title = "Préférences";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var area = owner != null && owner.Screens.ScreenFromWindow(owner) != null
                ? owner.Screens.ScreenFromWindow(owner).WorkingArea
                : new PixelRect(0, 0, 1200, 800);
            var scale = owner != null ? owner.RenderScaling : 1.0;
            Width = Math.Min(900, Math.Max(520, area.Width / scale - 40));
            Height = Math.Min(660, Math.Max(420, area.Height / scale - 40));
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            Tabs = new TabControl
            {
                Margin = new Thickness(10),
                ItemsPanel = new FuncTemplate<Panel>(() => new WrapPanel())
            };
            _swatches = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            Tabs.Items.Add(Tab("Personnalisation", BuildPersonalizationTab()));
            Tabs.Items.Add(Tab("Édition", BuildEditingTab()));
            Tabs.Items.Add(Tab("Correction", BuildProofingTab()));
            Tabs.Items.Add(Tab("Styles globaux", BuildGlobalStylesTab()));
            Tabs.Items.Add(Tab("Catalogue de polices", new FontCatalogTab()));
            Tabs.Items.Add(Tab("Auteur", BuildAuthorTab()));
            Tabs.Items.Add(Tab("Raccourcis", BuildShortcutsTab()));
            Tabs.Items.Add(Tab("DLC", BuildModulesTab()));

            var layout = new DockPanel();
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 0, 16, 14)
            };
            var close = Buttons.Text("Fermer", null, Buttons.Bar, Buttons.Look.Outline);
            close.MinWidth = 90;
            close.IsDefault = true;
            close.IsCancel = true;
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            Dialogs.Arrange(buttons, close); // la règle des dialogues (30/09)
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);
            layout.Children.Add(Tabs);
            Content = layout;
        }

        private static TabItem Tab(string header, Control content)
        {
            return new TabItem
            {
                Header = header,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = content
                }
            };
        }

        private static StackPanel TabPanel()
        {
            return new StackPanel { Margin = new Thickness(16, 12, 16, 12), HorizontalAlignment = HorizontalAlignment.Stretch };
        }

        /// <summary>Onglet « Styles globaux » : l'outil de gestion des styles
        /// sur la feuille de tous les projets (portée globale seulement), et
        /// le séparateur de scène global en dessous. Chaque édition est
        /// enregistrée et poussée au projet ouvert.</summary>
        private Control BuildGlobalStylesTab()
        {
            var panel = TabPanel();
            if (AppSettings.GlobalStyles == null)
                AppSettings.GlobalStyles = _project != null ? GlobalStyles.ForNewProject() : StyleSheet.CreateDefault();
            var sheet = AppSettings.GlobalStyles;
            panel.Children.Add(Caption("Styles de paragraphe globaux"));
            panel.Children.Add(new TextBlock
            {
                Text = "Ces styles valent pour tous les projets. Un livre ou un écrit peut y ajouter les siens (portée « livre » ou « document ») depuis l'onglet Styles du livre ou Format › Gérer les styles.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });
            var styles = new StylesPanel(sheet, StyleScopeContext.GlobalOnly()) { Height = 380 };
            styles.Changed += delegate { RaiseGlobalStylesChanged(); };
            panel.Children.Add(styles);

            panel.Children.Add(Caption("Séparateur de texte global", 18));
            panel.Children.Add(new TextBlock
            {
                Text = "Le paragraphe inséré par le bouton « Séparateur de scène » du ruban Texte. Un livre peut le remplacer par le sien (onglet Styles du livre).",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });
            var separator = new SeparatorEditor(sheet.EnsureSeparator(null, null, 0));
            separator.Changed += delegate { RaiseGlobalStylesChanged(); };
            panel.Children.Add(separator);
            return panel;
        }

        private void RaiseGlobalStylesChanged()
        {
            AppSettings.Save();
            var handler = GlobalStylesChanged;
            if (handler != null) handler();
        }

        private static Control Later(string text)
        {
            var panel = TabPanel();
            panel.Children.Add(Note(text));
            return panel;
        }

        private static TextBlock Note(string text, double top = 2, double bottom = 8, double left = 0)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(left, top, 0, bottom)
            };
        }

        private static TextBlock Caption(string text, double topMargin = 0)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, topMargin, 0, 0)
            };
        }

        // ------------------------------------------------------------ DLC

        private StackPanel _modulesPanel;

        private Control BuildModulesTab()
        {
            var panel = TabPanel();
            panel.Children.Add(Note("Les modules (DLC) ajoutent des fonctions à Marabook : une fiche avancée, des succès… Un paquet .mdlc s'installe sans redémarrage ; désinstallés, les projets gardent leurs valeurs. Le téléchargement depuis GitHub arrive avec la livraison.", 0, 12));
            _modulesPanel = new StackPanel();
            panel.Children.Add(_modulesPanel);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var fromFile = Buttons.Text("Installer depuis un fichier .mdlc…", "Un paquet de module obtenu autrement que par GitHub", Buttons.Bar, Buttons.Look.Outline);
            fromFile.Click += async delegate { await InstallModuleFromFile(); };
            actions.Children.Add(fromFile);
            panel.Children.Add(actions);
            if (Modules.LastLoadError.Length > 0)
            {
                var warn = Note("Un module n'a pas pu être chargé au lancement : " + Modules.LastLoadError, 12, 0);
                warn.Foreground = Chrome.Warn;
                warn.FontSize = 11;
                panel.Children.Add(warn);
            }
            var folder = Note("Dossier des modules : " + Modules.Root, 12, 0);
            folder.Foreground = Chrome.FaintText;
            folder.FontSize = 11;
            panel.Children.Add(folder);
            RefreshModules();
            return panel;
        }

        private void RefreshModules()
        {
            if (_modulesPanel == null) return;
            _modulesPanel.Children.Clear();
            var seen = new HashSet<string>();
            foreach (var source in Modules.Catalogue)
            {
                seen.Add(source.Id);
                var installed = Modules.Find(source.Id);
                _modulesPanel.Children.Add(ModuleRow(source.Name, source.Title,
                    installed != null ? "Installé" + (installed.Version.Length > 0 ? " · " + installed.Version : "") : "Disponible avec la livraison",
                    installed != null, installed != null ? source.Id : null));
            }
            foreach (var module in Modules.Installed)
            {
                if (seen.Contains(module.Id)) continue;
                _modulesPanel.Children.Add(ModuleRow(module.Name, "", "Installé depuis un fichier" + (module.Version.Length > 0 ? " · " + module.Version : ""), true, module.Id));
            }
            if (_modulesPanel.Children.Count == 0)
                _modulesPanel.Children.Add(new TextBlock { Text = "Aucun module connu.", Foreground = Chrome.SoftText, FontSize = 12 });
        }

        private Control ModuleRow(string name, string title, string label, bool installed, string uninstallId)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            if (uninstallId != null)
            {
                var remove = Buttons.Text("Désinstaller", "Retire le module ; les projets gardent leurs valeurs", Buttons.Compact, Buttons.Look.Outline);
                remove.Margin = new Thickness(12, 0, 0, 0);
                remove.VerticalAlignment = VerticalAlignment.Center;
                remove.Click += async delegate
                {
                    var answer = await MessageDialog.Show(this, "Désinstaller « " + name + " » ?", "Modules", MessageButtons.YesNo, MessageIcon.Question);
                    if (answer != MessageResult.Yes) return;
                    Modules.Uninstall(uninstallId);
                    RefreshModules();
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
            if (!string.IsNullOrEmpty(title))
                head.Children.Add(new TextBlock { Text = "  " + title, FontSize = 12, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Bottom });
            text.Children.Add(head);
            text.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = installed ? Chrome.Accent : Chrome.SoftText,
                Margin = new Thickness(0, 2, 0, 0)
            });
            row.Children.Add(text);
            return row;
        }

        private async Task InstallModuleFromFile()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Installer un module",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType> { new FilePickerFileType("Module Marabook") { Patterns = new[] { "*.mdlc" } } }
            });
            if (files == null || files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (path == null) return;
            try
            {
                var module = Modules.Install(path);
                RefreshModules();
                await MessageDialog.Show(this, module.Name + (module.Version.Length > 0 ? " " + module.Version : "") + " est installé — prêt, sans redémarrage.",
                    "Modules", MessageButtons.OK, MessageIcon.Information);
            }
            catch (Exception failure)
            {
                await MessageDialog.Show(this, "Installation impossible : " + failure.Message, "Modules", MessageButtons.OK, MessageIcon.Warning);
            }
        }

        // ------------------------------------------------------------ auteur

        private Control BuildAuthorTab()
        {
            var panel = TabPanel();
            panel.Children.Add(Caption("Auteur·ice par défaut"));
            panel.Children.Add(Note("Le nom signé sur ce qui n'est pas un livre : la compilation d'écrits, les commentaires exportés vers Word, les pages liminaires d'un livre qui ne nomme pas son auteur·ice. Chaque livre peut nommer le sien dans son onglet Édition."));
            panel.Children.Add(AuthorRow("Nom", AppSettings.DefaultAuthor, delegate(string value) { AppSettings.DefaultAuthor = value; }));
            panel.Children.Add(Caption("Métadonnées par défaut", 18));
            panel.Children.Add(Note("Pré-remplies dans chaque nouveau livre ; modifiables ensuite livre par livre."));
            panel.Children.Add(AuthorRow("Éditeur", AppSettings.DefaultPublisher, delegate(string value) { AppSettings.DefaultPublisher = value; }));
            panel.Children.Add(AuthorRow("Collection", AppSettings.DefaultCollection, delegate(string value) { AppSettings.DefaultCollection = value; }));
            return panel;
        }

        private static DockPanel AuthorRow(string label, string value, Action<string> store)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var caption = new TextBlock { Text = label, Width = 110, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            var box = new TextBox { Text = value ?? "", MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 300 };
            box.LostFocus += delegate
            {
                var text = (box.Text ?? "").Trim();
                if (text == (value ?? "")) return;
                value = text;
                store(text);
                AppSettings.PublishDefaults();
                AppSettings.Save();
            };
            row.Children.Add(box);
            return row;
        }

        // ------------------------------------------------------------ édition

        private Control BuildEditingTab()
        {
            var panel = TabPanel();
            panel.Children.Add(Caption("Sélection"));
            var autoWord = new CheckBox { Content = "Auto-sélecteur de mot", IsChecked = AppSettings.AutoSelectWord, Margin = new Thickness(0, 6, 0, 0) };
            autoWord.IsCheckedChanged += delegate
            {
                AppSettings.AutoSelectWord = autoWord.IsChecked == true;
                AppSettings.Save();
            };
            panel.Children.Add(autoWord);
            panel.Children.Add(Note("Complète la sélection d'un mot lorsque vous n'en sélectionnez qu'une partie", 4, 0, 22));
            var textDrag = new CheckBox { Content = "Drag and drop de sélection textuelle", IsChecked = AppSettings.TextDragDrop, Margin = new Thickness(0, 6, 0, 0) };
            textDrag.IsCheckedChanged += delegate
            {
                AppSettings.TextDragDrop = textDrag.IsChecked == true;
                AppSettings.Save();
            };
            panel.Children.Add(textDrag);
            panel.Children.Add(Note("Tirer une sélection la déplace là où le caret de dépôt se pose", 4, 0, 22));

            panel.Children.Add(Caption("Versions d'écrits", 16));
            var daily = new CheckBox
            {
                Content = "Instantané automatique à la première modification du jour",
                IsChecked = AppSettings.DailySnapshot,
                Margin = new Thickness(0, 6, 0, 0)
            };
            ToolTip.SetTip(daily, "L'état de l'écrit tel qu'ouvert, figé une fois par jour et par écrit — il compte dans le plafond");
            daily.IsCheckedChanged += delegate
            {
                AppSettings.DailySnapshot = daily.IsChecked == true;
                AppSettings.Save();
            };
            panel.Children.Add(daily);
            var capRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            var capBox = new TextBox { Width = 56, Text = AppSettings.SnapshotCap.ToString() };
            ToolTip.SetTip(capBox, "Entre " + SnapshotStore.MinCap + " et " + SnapshotStore.MaxCap);
            DockPanel.SetDock(capBox, Dock.Left);
            capBox.LostFocus += delegate
            {
                int value;
                if (!int.TryParse((capBox.Text ?? "").Trim(), out value)) value = AppSettings.SnapshotCap;
                value = Math.Max(SnapshotStore.MinCap, Math.Min(SnapshotStore.MaxCap, value));
                capBox.Text = value.ToString();
                if (value == AppSettings.SnapshotCap) return;
                AppSettings.SnapshotCap = value;
                AppSettings.Save();
            };
            capRow.Children.Add(capBox);
            capRow.Children.Add(new TextBlock
            {
                Text = "Instantanés gardés par écrit (les automatiques sont évincés d'abord)",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 0, 8, 0)
            });
            panel.Children.Add(capRow);
            return panel;
        }

        // ------------------------------------------------------------ correction

        private Control BuildProofingTab()
        {
            var panel = TabPanel();
            // Les raccourcis typographiques (30/09) : les séquences tapées que
            // l'éditeur (à la frappe) et la passe changent en signe — réglées
            // pour les deux jeux de règles à la fois.
            panel.Children.Add(Caption("Raccourcis typographiques"));
            panel.Children.Add(Note("Tapez la séquence, le signe la remplace — à la frappe et dans la passe typographique (règles « Point médian » et « Tirets tapés »). Un raccourci vide désactive le signe."));
            panel.Children.Add(TriggerRow("Point médian ·", AppSettings.Typography.MiddleDotTrigger,
                delegate(string value) { AppSettings.Typography.MiddleDotTrigger = value; AppSettings.TypographyLive.MiddleDotTrigger = value; }));
            panel.Children.Add(TriggerRow("Demi-cadratin –", AppSettings.Typography.EnDashTrigger,
                delegate(string value) { AppSettings.Typography.EnDashTrigger = value; AppSettings.TypographyLive.EnDashTrigger = value; }));
            panel.Children.Add(TriggerRow("Cadratin — (le demi-cadratin suivi de « - » le donne aussi)", AppSettings.Typography.EmDashTrigger,
                delegate(string value) { AppSettings.Typography.EmDashTrigger = value; AppSettings.TypographyLive.EmDashTrigger = value; }));

            panel.Children.Add(Caption("Grammaire (Grammalecte)", 18));
            var master = new CheckBox
            {
                Content = "Vérification grammaticale (Grammalecte, en différé)",
                IsChecked = AppSettings.GrammarEnabled,
                Margin = new Thickness(0, 6, 0, 0)
            };
            ToolTip.SetTip(master, "Grammalecte tourne dans un sous-processus : ses signalements arrivent quelques instants après la frappe,\nsans jamais ralentir l'orthographe ni la saisie.");
            master.IsCheckedChanged += delegate
            {
                AppSettings.GrammarEnabled = master.IsChecked == true;
                AppSettings.Save();
                var handler = ProofingChanged;
                if (handler != null) handler();
            };
            panel.Children.Add(master);
            panel.Children.Add(Note("Les options reprennent les noms de Grammalecte. Celles marquées « couvert par Marabook » sont éteintes parce qu'un vérificateur maison tient déjà ce terrain (répétitions réglées pour le roman, typographie du compositeur, mots composés de l'orthographe) — les rallumer produit des signalements en double.", 6, 4));

            var options = new StackPanel();
            string group = null;
            foreach (var option in Correction.Grammalecte.GrammalecteOptions.Catalog)
            {
                if (option.Group != group)
                {
                    group = option.Group;
                    options.Children.Add(Caption(group, 10));
                }
                var optionRef = option;
                bool overridden;
                var value = AppSettings.GrammarOptions.TryGetValue(option.Name, out overridden) ? overridden : option.MarabookDefault;
                var check = new CheckBox
                {
                    Content = option.Label + " (" + option.Name + ")" + (option.OverriddenByPolicy ? " — couvert par Marabook" : ""),
                    IsChecked = value,
                    Margin = new Thickness(0, 3, 0, 0)
                };
                check.IsCheckedChanged += delegate
                {
                    var chosen = check.IsChecked == true;
                    if (chosen == optionRef.MarabookDefault) AppSettings.GrammarOptions.Remove(optionRef.Name);
                    else AppSettings.GrammarOptions[optionRef.Name] = chosen;
                    AppSettings.Save();
                    var handler = ProofingChanged;
                    if (handler != null) handler();
                };
                options.Children.Add(check);
            }
            panel.Children.Add(options);

            return panel;
        }

        /// <summary>Une ligne « séquence → signe » : le champ à gauche, court
        /// et à chasse fixe ; enregistré quand il perd le clavier.</summary>
        private static Control TriggerRow(string label, string value, Action<string> store)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var box = new TextBox { Text = value ?? "", Width = 72, FontFamily = new FontFamily("Consolas"), HorizontalContentAlignment = HorizontalAlignment.Center };
            DockPanel.SetDock(box, Dock.Left);
            box.LostFocus += delegate
            {
                store((box.Text ?? "").Trim());
                AppSettings.Save();
            };
            row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = label, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            return row;
        }

        // ------------------------------------------------------------ personnalisation

        private Control BuildPersonalizationTab()
        {
            var panel = TabPanel();
            panel.Children.Add(Caption("Couleur d'accent"));
            panel.Children.Add(Note("Boutons, sélections, liens et repères prennent cette teinte."));
            RebuildSwatches();
            panel.Children.Add(_swatches);

            var custom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var hexBox = new TextBox { Width = 90, Text = AppSettings.AccentColor ?? Chrome.DefaultAccent };
            ToolTip.SetTip(hexBox, "Une couleur personnalisée : #RRGGBB, puis Entrée");
            hexBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Return && e.Key != Key.Enter) return;
                Ink ink;
                var text = (hexBox.Text ?? "").Trim().ToUpperInvariant();
                if (text.Length == 7 && Ink.TryParse(text, out ink))
                    SetAccent(string.Equals(text, Chrome.DefaultAccent, StringComparison.OrdinalIgnoreCase) ? null : text);
                e.Handled = true;
            };
            custom.Children.Add(hexBox);
            var reset = Buttons.Text("Réinitialiser", "Revenir à l'indigo par défaut", Buttons.Bar, Buttons.Look.Outline);
            reset.Margin = new Thickness(8, 0, 0, 0);
            reset.Click += delegate { SetAccent(null); hexBox.Text = Chrome.DefaultAccent; };
            custom.Children.Add(reset);
            panel.Children.Add(custom);

            panel.Children.Add(Caption("Défilement", 18));
            var speedColumn = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            var speedLabel = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            var speed = new Slider
            {
                Minimum = 0.25,
                Maximum = 3,
                Value = AppSettings.ScrollSpeed,
                TickFrequency = 0.25,
                IsSnapToTickEnabled = true,
                Width = 260,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ToolTip.SetTip(speed, "Vitesse du défilement à la molette : ×0,25 (lent) à ×3 (rapide)"
                + (OperatingSystem.IsMacOS() ? " — sur macOS le système défile lui-même, ce réglage n'y joue pas (07/10)" : ""));
            Action refreshSpeedLabel = delegate
            {
                speedLabel.Text = "Vitesse : ×" + AppSettings.ScrollSpeed.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
                    + (Math.Abs(AppSettings.ScrollSpeed - 1) < 0.01 ? " (le pas du système)" : "");
            };
            speed.ValueChanged += delegate
            {
                AppSettings.ScrollSpeed = Math.Round(speed.Value * 4) / 4;
                refreshSpeedLabel();
                AppSettings.Save();
            };
            refreshSpeedLabel();
            speedColumn.Children.Add(speed);
            speedColumn.Children.Add(speedLabel);
            speedColumn.Children.Add(Note("Le défilement à la molette est fluide partout ; ce réglage en change le pas.", 4, 0));
            panel.Children.Add(speedColumn);

            panel.Children.Add(Caption("Mode sombre", 18));
            _darkCheck = new CheckBox { Content = "Mode sombre", IsChecked = AppSettings.DarkTheme, Margin = new Thickness(0, 6, 0, 0) };
            ToolTip.SetTip(_darkCheck, "Aussi : Affichage › Thème sombre (" + AppSettings.DisplayGesture(AppSettings.Gesture("dark-theme") ?? "") + ")");
            _darkCheck.IsCheckedChanged += delegate
            {
                if (AppSettings.DarkTheme == (_darkCheck.IsChecked == true)) return;
                AppSettings.DarkTheme = _darkCheck.IsChecked == true;
                RaiseAppearanceChanged();
            };
            panel.Children.Add(_darkCheck);
            _whitePaper = new CheckBox { Content = "Garder le papier blanc malgré le mode sombre", IsChecked = AppSettings.WhitePaperInDark, Margin = new Thickness(0, 6, 0, 0) };
            ToolTip.SetTip(_whitePaper, "L'interface reste sombre mais les pages (fiches, gabarits)\ngardent leur papier blanc et leur encre noire, comme à l'impression.");
            _whitePaper.IsCheckedChanged += delegate
            {
                if (AppSettings.WhitePaperInDark == (_whitePaper.IsChecked == true)) return;
                AppSettings.WhitePaperInDark = _whitePaper.IsChecked == true;
                RaiseAppearanceChanged();
            };
            panel.Children.Add(_whitePaper);
            panel.Children.Add(Note("La vue Composition, l'aperçu et le PDF sont toujours noir sur blanc.", 4, 0, 22));
            return panel;
        }

        /// <summary>La grille des pastilles, l'accent courant cerclé d'encre.</summary>
        private void RebuildSwatches()
        {
            _swatches.Children.Clear();
            var current = AppSettings.AccentColor ?? Chrome.DefaultAccent;
            foreach (var entry in Accents)
            {
                var hex = entry[0];
                var selected = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
                var swatch = new Border
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Chrome.ToColor(Ink.Parse(hex))),
                    BorderBrush = selected ? (IBrush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(selected ? 2.5 : 1),
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                ToolTip.SetTip(swatch, entry[1] + " — " + hex);
                var hexRef = hex;
                swatch.PointerReleased += delegate
                {
                    SetAccent(string.Equals(hexRef, Chrome.DefaultAccent, StringComparison.OrdinalIgnoreCase) ? null : hexRef);
                };
                _swatches.Children.Add(swatch);
            }
        }

        private void SetAccent(string hex)
        {
            AppSettings.AccentColor = hex;
            RebuildSwatches();
            RaiseAppearanceChanged();
        }

        private void RaiseAppearanceChanged()
        {
            AppSettings.Save();
            var handler = AppearanceChanged;
            if (handler != null) handler();
        }

        // ------------------------------------------------------------ raccourcis

        private Control BuildShortcutsTab()
        {
            var panel = TabPanel();
            panel.Children.Add(Note("Cliquez un champ puis tapez la combinaison voulue. Retour arrière retire le raccourci ; « Défaut » restaure celui d'origine.", 0, 4));
            var categories = new List<string>();
            foreach (var action in AppSettings.Actions)
                if (!categories.Contains(action.Category)) categories.Add(action.Category);
            foreach (var category in categories)
            {
                panel.Children.Add(Caption(category, 10));
                foreach (var action in AppSettings.Actions)
                    if (action.Category == category)
                        panel.Children.Add(ShortcutRow(action));
            }
            return panel;
        }

        private Control ShortcutRow(ActionDefinition action)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var box = new TextBox
            {
                Width = 150,
                IsReadOnly = true,
                TextAlignment = TextAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Focusable = false,
                IsTabStop = false
            };
            ToolTip.SetTip(box, "Cliquer puis taper la combinaison — Retour arrière : aucun raccourci");
            var reset = Buttons.Text("Défaut", "Revenir au raccourci d'origine"
                + (string.IsNullOrEmpty(action.DefaultGesture) ? " (aucun)" : " : " + AppSettings.DisplayGesture(action.DefaultGesture)),
                Buttons.Compact, Buttons.Look.Outline);
            reset.FontSize = 11;
            reset.Margin = new Thickness(6, 0, 0, 0);
            Action sync = delegate
            {
                var gesture = AppSettings.Gesture(action.Id);
                box.Text = string.IsNullOrEmpty(gesture) ? "—" : AppSettings.DisplayGesture(gesture);
                var custom = AppSettings.Shortcuts.ContainsKey(action.Id);
                box.FontWeight = custom ? FontWeight.SemiBold : FontWeight.Normal;
                reset.Opacity = custom ? 1 : 0;
                reset.IsHitTestVisible = custom;
            };
            Action<string> store = async delegate(string gesture)
            {
                if (gesture == (action.DefaultGesture ?? "")) AppSettings.Shortcuts.Remove(action.Id);
                else AppSettings.Shortcuts[action.Id] = gesture;
                AppSettings.Save();
                sync();
                var handler = ShortcutsChanged;
                if (handler != null) handler();
                if (gesture.Length == 0) return;
                // Le doublon (1.0.5) : une autre action, un style, ou le système — une seule définition.
                var conflict = ShortcutConflicts.Describe(gesture, action.Id,
                    AppSettings.GlobalStyles == null ? null : AppSettings.GlobalStyles.Styles, null);
                if (conflict != null)
                    await MessageDialog.Show(this, conflict, "Raccourcis", MessageButtons.OK, MessageIcon.Information);
            };
            box.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                box.Focusable = true;
                box.Focus();
                e.Handled = true;
            };
            box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                e.Handled = true;
                var key = e.Key;
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift
                    || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin) return;
                if (key == Key.Escape) { ClearFocus(); sync(); return; }
                if (key == Key.Back) { store(""); return; }
                var gesture = "";
                if (Ui.HasCommand(e.KeyModifiers)) gesture += "Ctrl+"; // ⌘ sur macOS, enregistré « Ctrl » (02/10)
                if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0) gesture += "Shift+";
                if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Alt) != 0) gesture += "Alt+";
                store(gesture + key);
            };
            box.GotFocus += delegate { box.Text = "Tapez…"; };
            box.LostFocus += delegate { sync(); box.Focusable = false; };
            reset.Click += delegate
            {
                AppSettings.Shortcuts.Remove(action.Id);
                AppSettings.Save();
                sync();
                var handler = ShortcutsChanged;
                if (handler != null) handler();
            };
            sync();

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(box);
            right.Children.Add(reset);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(new TextBlock
            {
                Text = action.Name,
                Foreground = Chrome.Ink,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        private void ClearFocus()
        {
            var top = TopLevel.GetTopLevel(this);
            if (top != null && top.FocusManager != null) top.FocusManager.ClearFocus();
        }
    }
}

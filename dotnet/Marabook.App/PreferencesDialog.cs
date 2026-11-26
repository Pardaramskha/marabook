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
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Les Préférences (portées de View/PreferencesDialog.cs),
    /// appliquées en direct. Onglets : Projet, Personnalisation (« Général » de la 1.0.6 au 22/11), Profil, Édition, Correction,
    /// Styles globaux, Catalogue de polices, Auteur, Accessibilité, Raccourcis, Dépendances, DLC. Taille
    /// unique, bornée à l'écran ; les chips se replient sur plusieurs rangées
    /// quand la fenêtre est étroite (WrapPanel). Tout est dans AppSettings.
    /// Styles globaux et Catalogue de polices ont leurs éditeurs (P2).</summary>
    public partial class PreferencesDialog : Window
    {
        public event Action AppearanceChanged;
        /// <summary>Les onglets (1.1.0) : la bascule ou le plafond a changé.</summary>
        public event Action TabsChanged;
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
            Classes.Add(Marabook.App.Theme.Switches); // toutes ses cases à cocher sont des interrupteurs animés (25/11)
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var availableWidth = Dialogs.AvailableWidth(owner);
            var availableHeight = Dialogs.AvailableHeight(owner);
            Width = Math.Min(960, Math.Max(520, availableWidth > 0 ? availableWidth : 900));
            // Toute la hauteur de l'écran (24/10, 1.0.6) : la zone de travail
            // moins la barre de titre — la fenêtre ne sort jamais de l'écran.
            Height = Math.Max(420, availableHeight > 0 ? availableHeight : 660);
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            // Les onglets en COLONNE à gauche (24/10, 1.0.6) : huit chips sur
            // deux rangées se lisaient mal ; la colonne (classe « column »,
            // Theme) les aligne et laisse tout le reste au contenu.
            Tabs = new TabControl
            {
                Margin = new Thickness(10),
                TabStripPlacement = Dock.Left,
                ItemsPanel = new FuncTemplate<Panel>(() => new StackPanel { Orientation = Orientation.Vertical })
            };
            Tabs.Classes.Add("column");
            _swatches = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            Tabs.Items.Add(Tab("Projet", BuildProjectTab(owner))); // le projet ouvert et les modèles (21/11) — le premier onglet
            Tabs.Items.Add(Tab("Personnalisation", BuildPersonalizationTab())); // « Général » de la 1.0.6 au 22/11 — l'en-tête seul change, aucun réglage n'est nommé d'après l'onglet
            Tabs.Items.Add(Tab("Profil", BuildProfileTab())); // le profil d'utilisation (1.1.0)
            Tabs.Items.Add(Tab("Édition", BuildEditingTab()));
            Tabs.Items.Add(Tab("Correction", BuildProofingTab()));
            Tabs.Items.Add(Tab("Styles globaux", BuildGlobalStylesTab()));
            Tabs.Items.Add(Tab("Catalogue de polices", new FontCatalogTab()));
            Tabs.Items.Add(Tab("Auteur", BuildAuthorTab()));
            Tabs.Items.Add(Tab("Accessibilité", BuildAccessibilityTab())); // 22/11
            Tabs.Items.Add(Tab("Raccourcis", BuildShortcutsTab()));
            Tabs.Items.Add(Tab("Dépendances", BuildDependenciesTab())); // ce qui a été téléchargé (22/11)
            Tabs.Items.Add(Tab("DLC", BuildModulesTab()));
            AddModuleTabs();
            MindMapModules.ProviderChanged += OnProviderChanged;
            Closed += delegate { MindMapModules.ProviderChanged -= OnProviderChanged; };

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

        /// <summary>Les onglets des modules à code (26/10/2026, Mental-O en
        /// DLC) : sous un filet « DLC » (un onglet inerte, en petites
        /// capitales), un onglet par module qui en apporte un — « Mental-O
        /// (dlc) ». Installé ou désinstallé pendant que le dialogue est
        /// ouvert : la liste se refait.</summary>
        private readonly List<TabItem> _moduleTabs = new List<TabItem>();

        /// <summary>Sonde : les onglets venus des modules (le filet « DLC » compris).</summary>
        public int ModuleTabCount { get { return _moduleTabs.Count; } }

        private void AddModuleTabs()
        {
            foreach (var tab in _moduleTabs) Tabs.Items.Remove(tab);
            _moduleTabs.Clear();
            var modules = new List<IMarabookModule>();
            foreach (var module in CodeModuleHost.Instance.Loaded)
                if (!string.IsNullOrEmpty(module.PreferencesTitle)) modules.Add(module);
            if (modules.Count == 0) return;
            var divider = new TabItem
            {
                Header = new TextBlock
                {
                    Text = "DLC",
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Chrome.FaintText,
                    Margin = new Thickness(0, 6, 0, 0)
                },
                IsEnabled = false,
                IsHitTestVisible = false,
                Focusable = false
            };
            divider.Classes.Add("divider");
            _moduleTabs.Add(divider);
            Tabs.Items.Add(divider);
            foreach (var module in modules)
            {
                Control content;
                try { content = module.PreferencesTab(); }
                catch (Exception failure)
                {
                    content = Note("L'onglet du module n'a pas pu se construire : " + failure.Message);
                }
                if (content == null) continue;
                var tab = Tab(module.PreferencesTitle, content);
                _moduleTabs.Add(tab);
                Tabs.Items.Add(tab);
            }
        }

        private void OnProviderChanged()
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(delegate { AddModuleTabs(); RefreshModules(); });
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

        /// <summary>L'intertitre d'une sous-section ; au-delà du premier
        /// (marge haute), un FIN FILET CONTRASTÉ le précède (1.1.0, Rémi) —
        /// l'encre du thème, éclaircie : noir sur clair, blanc sur sombre.</summary>
        private static Control Caption(string text, double topMargin = 0)
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold
            };
            if (topMargin <= 0) return label;
            var block = new StackPanel { Margin = new Thickness(0, topMargin, 0, 0) };
            block.Children.Add(new Border
            {
                Height = 1,
                Background = Chrome.Ink,
                Opacity = 0.3,
                Margin = new Thickness(0, 0, 0, 10),
                Tag = "caption-divider" // sonde
            });
            block.Children.Add(label);
            return block;
        }

        // ------------------------------------------------------------ DLC

        private StackPanel _modulesPanel;

        private Control BuildModulesTab()
        {
            var panel = TabPanel();
            panel.Children.Add(Note("Les modules (DLC) ajoutent des fonctions à Marabook : une fiche avancée, des succès… Un paquet .mdlc s'installe sans redémarrage ; désinstallés, les projets gardent leurs valeurs. Les DLC du catalogue se téléchargent depuis GitHub d'un clic (1/11), ou s'installent depuis un fichier.", 0, 12));
            _modulesPanel = new StackPanel();
            panel.Children.Add(_modulesPanel);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var fromFile = Buttons.Text("Installer depuis un fichier .mdlc…", null, Buttons.Bar, Buttons.Look.Outline);
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

        /// <summary>Les lignes du catalogue (1/11, Rémi : « pas de bouton
        /// installer ») viennent du magasin (ModuleStore) : l'état croisé
        /// installé / publié sur GitHub, « Installer » ou « Mettre à jour »
        /// quand le paquet est là, « Désinstaller » quand le module est posé ;
        /// la vérification en ligne part à l'ouverture de l'onglet et chaque
        /// réponse redessine les lignes.</summary>
        private bool _modulesWatched;

        private void RefreshModules()
        {
            if (_modulesPanel == null) return;
            if (!_modulesWatched)
            {
                _modulesWatched = true;
                ModuleStore.Changed += RefreshModules;
                Closed += delegate { ModuleStore.Changed -= RefreshModules; };
                ModuleStore.CheckOnline(Avalonia.Threading.Dispatcher.UIThread);
            }
            _modulesPanel.Children.Clear();
            foreach (var state in ModuleStore.Refresh()) _modulesPanel.Children.Add(ModuleRow(state));
            if (_modulesPanel.Children.Count == 0)
                _modulesPanel.Children.Add(new TextBlock { Text = "Aucun module connu.", Foreground = Chrome.SoftText, FontSize = 12 });
        }

        private Control ModuleRow(ModuleState state)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(actions, Dock.Right);
            if (Modules.DownloadsEnabled && state.IsAvailable && (!state.IsInstalled || state.HasUpdate))
            {
                var install = Buttons.Text(state.HasUpdate ? "Mettre à jour" : "Installer", "Télécharge le paquet publié sur GitHub et l'installe, sans redémarrage", Buttons.Compact, Buttons.Look.Primary);
                install.IsEnabled = !state.Busy;
                install.Click += delegate
                {
                    ModuleStore.Install(state, Avalonia.Threading.Dispatcher.UIThread, delegate(string failure)
                    {
                        if (failure != null) MessageDialog.Show(this, "Installation impossible : " + failure, "DLC", MessageButtons.OK, MessageIcon.Warning);
                    });
                };
                actions.Children.Add(install);
            }
            if (state.IsInstalled)
            {
                var remove = Buttons.Text("Désinstaller", "Retire le module ; les projets gardent leurs valeurs", Buttons.Compact, Buttons.Look.Outline);
                remove.IsEnabled = !state.Busy;
                remove.Click += async delegate
                {
                    var answer = await MessageDialog.Show(this, "Désinstaller « " + state.Source.Name + " » ?", "DLC", MessageButtons.YesNo, MessageIcon.Question);
                    if (answer != MessageResult.Yes) return;
                    ModuleStore.Uninstall(state);
                };
                actions.Children.Add(remove);
            }
            if (actions.Children.Count > 0) row.Children.Add(actions);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = state.Source.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
            if (!string.IsNullOrEmpty(state.Source.Title))
                head.Children.Add(new TextBlock { Text = "  " + state.Source.Title, FontSize = 12, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Bottom });
            text.Children.Add(head);
            text.Children.Add(new TextBlock
            {
                Text = state.Label,
                FontSize = 11,
                Foreground = state.IsInstalled ? Chrome.Accent : Chrome.SoftText,
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

        /// <summary>Onglet « Profil » (1.1.0) : le même sélecteur que la fenêtre du
        /// premier lancement — Écrivain·e, Éditeur·ice, Cinglé·e, Moi-même et sa
        /// table de cases, le nom de plume. Chaque clic s'applique aussitôt.</summary>
        private Control BuildProfileTab()
        {
            var panel = TabPanel();
            panel.Children.Add(new ProfileChooser(true) { OpenFeaturesOnChoice = true });
            return panel;
        }

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
            var page = TabPanel();
            // 22/11 (Rémi) : un BLOC à bord arrondi par famille de réglages — le skin et ses
            // couleurs ; le défilement, les animations et les sons ; les onglets ; le mode sombre ; la palette.
            var panel = Group(page, "skin");
            // LES SKINS (2.0.0, 21/11, Rémi) : le look général, en tête de l'onglet.
            panel.Children.Add(Caption("Skin"));
            panel.Children.Add(Note("Le look de toute l'application, des pages wiki des fiches et de l'Encyclopédie exportée (HTML et « PDF Encyclopédique »). Un skin ne change jamais un texte exporté : un PDF BAT, un Word ou un EPUB restent ce qu'ils sont."));
            _skinCards = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, Tag = "skins" };
            panel.Children.Add(_skinCards);
            _secondRow = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = "skin-second" };
            _museumRow = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = "skin-museum" }; // les options de la série « Museum » (24/11)
            RebuildSkins();

            panel.Children.Add(Caption("Couleur d'accent", 18));
            panel.Children.Add(Note("Boutons, sélections, liens et repères prennent cette teinte."));
            RebuildSwatches();
            panel.Children.Add(_swatches);

            var custom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var hexBox = _accentHex = new TextBox { Width = 90, Text = Skins.AccentFor(Skin.Current, AppSettings.AccentColor) };
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
            panel.Children.Add(custom);

            // 24/11 (Rémi) : l'accent juste sous les skins, PUIS les options propres au skin choisi
            // (seconde couleur, options « Museum »), un seul bouton pour rendre toutes les couleurs,
            // et l'arrondi des angles.
            panel.Children.Add(_secondRow);
            panel.Children.Add(_museumRow);
            BuildColorsResetAndCorners(panel);

            panel = Group(page, "motion");
            panel.Children.Add(Caption("Défilement"));
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

            panel.Children.Add(Caption("Animations", 18));
            var animations = new CheckBox { Content = "Animations", IsChecked = AppSettings.Animations, Margin = new Thickness(0, 6, 0, 0), Tag = "animations" };
            ToolTip.SetTip(animations, "L'entrée des tuiles de la bibliothèque, la glissade de la Pile et celle de la colonne de droite.\nDécochée : tout se pose d'un coup.");
            animations.IsCheckedChanged += delegate
            {
                if (AppSettings.Animations == (animations.IsChecked == true)) return;
                AppSettings.Animations = animations.IsChecked == true;
                AppSettings.Save();
            };
            panel.Children.Add(animations);
            panel.Children.Add(Note("Les effets d'interface (1.0.6) : les tuiles arrivent en fondu, la Pile et le rail glissent.", 4, 0));

            // Les SONS (1.1.0, 31/10, Rémi) : une case, sans aide.
            panel.Children.Add(Caption("Sons", 18));
            var sounds = new CheckBox { Content = "Activer les sons : sauvegarde, erreur, succès, notification", IsChecked = AppSettings.SoundsEnabled, Margin = new Thickness(0, 6, 0, 0), Tag = "sounds" };
            sounds.IsCheckedChanged += delegate
            {
                if (AppSettings.SoundsEnabled == (sounds.IsChecked == true)) return;
                AppSettings.SoundsEnabled = sounds.IsChecked == true;
                AppSettings.Save();
            };
            panel.Children.Add(sounds);

            // Les ONGLETS (1.1.0, 28/10, Rémi) : la bascule et le plafond, sans
            // aide — « Onglets maximum » entre WorkTabs.MinLimit et MaxLimit.
            panel = Group(page, "tabs");
            panel.Children.Add(Caption("Onglets"));
            var tabsOn = new CheckBox { Content = "Activer les onglets", IsChecked = AppSettings.TabsEnabled, Margin = new Thickness(0, 6, 0, 0), Tag = "tabs-enabled" };
            tabsOn.IsCheckedChanged += delegate
            {
                if (AppSettings.TabsEnabled == (tabsOn.IsChecked == true)) return;
                AppSettings.TabsEnabled = tabsOn.IsChecked == true;
                AppSettings.Save();
                var handler = TabsChanged;
                if (handler != null) handler();
            };
            panel.Children.Add(tabsOn);
            var tabsRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            var tabsBox = new TextBox { Width = 56, Text = AppSettings.MaxTabs.ToString(), Tag = "max-tabs" };
            ToolTip.SetTip(tabsBox, "Entre " + WorkTabs.MinLimit + " et " + WorkTabs.MaxLimit);
            DockPanel.SetDock(tabsBox, Dock.Left);
            Action commitTabs = delegate
            {
                int value;
                if (!int.TryParse((tabsBox.Text ?? "").Trim(), out value)) value = AppSettings.MaxTabs;
                value = WorkTabs.Clamp(value);
                tabsBox.Text = value.ToString();
                if (value == AppSettings.MaxTabs) return;
                AppSettings.MaxTabs = value;
                AppSettings.Save();
                var handler = TabsChanged;
                if (handler != null) handler();
            };
            tabsBox.LostFocus += delegate { commitTabs(); };
            tabsBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Return || e.Key == Key.Enter) { commitTabs(); e.Handled = true; }
            };
            tabsRow.Children.Add(tabsBox);
            tabsRow.Children.Add(new TextBlock { Text = "Onglets maximum", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            panel.Children.Add(tabsRow);

            panel = Group(page, "dark");
            panel.Children.Add(Caption("Mode sombre"));
            _darkCheck = new CheckBox { Content = "Mode sombre", IsChecked = AppSettings.DarkTheme, Margin = new Thickness(0, 6, 0, 0) };
            ToolTip.SetTip(_darkCheck, "(" + AppSettings.DisplayGesture(AppSettings.Gesture("dark-theme") ?? "") + ")");
            _darkCheck.IsCheckedChanged += delegate
            {
                if (AppSettings.DarkTheme == (_darkCheck.IsChecked == true)) return;
                AppSettings.DarkTheme = _darkCheck.IsChecked == true;
                RaiseAppearanceChanged();
                if (_skinCards != null && Skin.Museum) RebuildSkins(); // les fonds proposés à un skin « Museum » suivent le mode
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

            BuildPaletteSection(Group(page, "palette"));
            return page;
        }

        // ------------------------------------------------------------ skins (21/11)

        private WrapPanel _skinCards;
        private StackPanel _secondRow;

        /// <summary>Les cartes de skin (celle en cours cerclée d'accent ; les PREMIUM étiquetées), les
        /// options d'un skin « Museum » et, pour les skins à deux couleurs, le choix de la seconde.</summary>
        internal void RebuildSkinsForProbe() { RebuildSkins(); }

        private void RebuildSkins()
        {
            _skinCards.Children.Clear();
            var current = Skin.Current;
            foreach (var id in Skins.Ids)
            {
                var skinId = id;
                var selected = id == current;
                // UN SKIN À DÉBLOQUER (25/11/2026, Rémi) : « Gardien du Feu », « Bois Blanc » et « Assermenté »
                // s'ouvrent par un succès secret (ou par le sésame). Verrouillé : aperçu éteint sous un
                // cadenas, description tue, et le clic ne le choisit pas. Le skin EN COURS n'est jamais
                // verrouillé (on ne retire pas son skin à qui l'utilise déjà).
                var locked = !selected && AppSettings.SkinLocked(id);
                var column = new StackPanel();
                var preview = SkinPreview(id);
                if (locked)
                {
                    preview.Opacity = 0.28;
                    var shut = new Panel();
                    shut.Children.Add(preview);
                    var padlock = Icons.Make("lock", 26, Chrome.Ink) as Control;
                    if (padlock != null) { padlock.HorizontalAlignment = HorizontalAlignment.Center; padlock.VerticalAlignment = VerticalAlignment.Center; shut.Children.Add(padlock); }
                    column.Children.Add(shut);
                }
                else column.Children.Add(preview);
                var name = new TextBlock { Text = Skins.Label(id), FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center };
                if (Skins.Premium(id))
                {
                    // Un skin PREMIUM (la série « Museum », 24/11) : une étiquette à côté de son nom.
                    var title = new DockPanel { Margin = new Thickness(0, 7, 0, 1) };
                    var premium = new Border
                    {
                        Background = Chrome.AccentTint, BorderBrush = Chrome.AccentSoft, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 0, 5, 1), VerticalAlignment = VerticalAlignment.Center, Tag = "skin-premium",
                        Child = new TextBlock { Text = "PREMIUM", FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = Chrome.AccentStrong }
                    };
                    DockPanel.SetDock(premium, Dock.Right);
                    title.Children.Add(premium);
                    title.Children.Add(name);
                    column.Children.Add(title);
                }
                else { name.Margin = new Thickness(0, 7, 0, 1); column.Children.Add(name); }
                column.Children.Add(new TextBlock { Text = locked ? "Verrouillé — un succès secret l'ouvre." : Skins.Description(id), Foreground = Chrome.SoftText, FontSize = 11.5, FontStyle = locked ? FontStyle.Italic : FontStyle.Normal, TextWrapping = TextWrapping.Wrap });
                var card = new Border
                {
                    Width = 178,
                    Margin = new Thickness(0, 4, 10, 6),
                    Padding = new Thickness(8),
                    CornerRadius = new CornerRadius(8),
                    Background = Chrome.CardBg,
                    BorderBrush = selected ? (IBrush)Chrome.Accent : Chrome.Border,
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    Cursor = new Cursor(locked ? StandardCursorType.Arrow : StandardCursorType.Hand),
                    Child = column,
                    Tag = (locked ? "skin-locked:" : "skin:") + id
                };
                if (locked) ToolTip.SetTip(card, "Ce skin se débloque en obtenant le succès secret « " + Achievements.Find(Skins.UnlockedBy(skinId)).Name + " ».");
                else card.PointerReleased += delegate { SetSkin(skinId); };
                _skinCards.Children.Add(card);
            }

            RebuildMuseumOptions(current);

            _secondRow.Children.Clear();
            _secondRow.IsVisible = current == Skins.Gradient || current == Skins.Cyberpunk;
            if (!_secondRow.IsVisible) return;
            var accent = AppSettings.AccentColor ?? Chrome.DefaultAccent;
            var auto = AppSettings.SkinAccent2 == null;
            var effective = Skins.SecondFor(accent, AppSettings.SkinAccent2);
            _secondRow.Children.Add(new TextBlock { Text = current == Skins.Gradient ? "Seconde couleur du dégradé" : "Seconde couleur des néons", Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var line = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            var autoCheck = new CheckBox { Content = "Automatique", IsChecked = auto, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Tag = "skin-second-auto" };
            ToolTip.SetTip(autoCheck, "Marabook choisit la couleur qui va avec votre couleur d'accent : sa voisine sur le cercle des teintes.");
            autoCheck.IsCheckedChanged += delegate
            {
                var wanted = autoCheck.IsChecked == true;
                if (wanted == (AppSettings.SkinAccent2 == null)) return;
                SetSecond(wanted ? null : Skins.AutoSecond(AppSettings.AccentColor ?? Chrome.DefaultAccent));
            };
            line.Children.Add(autoCheck);
            foreach (var entry in Accents)
            {
                var hex = entry[0];
                var chosen = !auto && string.Equals(hex, AppSettings.SkinAccent2, StringComparison.OrdinalIgnoreCase);
                var swatch = new Border
                {
                    Width = 24, Height = 24, Margin = new Thickness(2), CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Chrome.ToColor(Ink.Parse(hex))),
                    BorderBrush = chosen ? (IBrush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(chosen ? 2.5 : 1),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = "skin-second-swatch"
                };
                ToolTip.SetTip(swatch, entry[1] + " — " + hex);
                var hexRef = hex;
                swatch.PointerReleased += delegate { SetSecond(hexRef); };
                line.Children.Add(swatch);
            }
            var box = new TextBox { Width = 86, Text = effective, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "skin-second-hex" };
            ToolTip.SetTip(box, "Une couleur personnalisée : #RRGGBB, puis Entrée");
            box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Return && e.Key != Key.Enter) return;
                var text = (box.Text ?? "").Trim().ToUpperInvariant();
                if (Skins.IsHex(text)) SetSecond(text);
                e.Handled = true;
            };
            line.Children.Add(box);
            _secondRow.Children.Add(line);
            var sample = new Border
            {
                Height = 14, Width = 320, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 8, 0, 0),
                Background = Horizontal(Chrome.ToColor(Ink.Parse(accent)), Chrome.ToColor(Ink.Parse(effective))),
                Tag = "skin-second-sample"
            };
            _secondRow.Children.Add(sample);
            _secondRow.Children.Add(Note(auto ? "Automatique : " + effective + ", la voisine de votre couleur d'accent." : "Choisie : " + effective + ".", 4, 0));
        }

        private static IBrush Horizontal(Color from, Color to)
        {
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            brush.GradientStops.Add(new GradientStop(from, 0));
            brush.GradientStops.Add(new GradientStop(to, 1));
            return brush;
        }

        /// <summary>La vignette d'un skin : son fond, une « fenêtre », un bouton — en couleurs FIGÉES
        /// (elle montre le skin, pas celui qui est en cours).</summary>
        private static Control SkinPreview(string id)
        {
            var accent = Chrome.ToColor(Ink.Parse(AppSettings.AccentColor ?? Chrome.DefaultAccent));
            var second = Chrome.ToColor(Ink.Parse(Skins.SecondFor(AppSettings.AccentColor ?? Chrome.DefaultAccent, AppSettings.SkinAccent2)));
            IBrush ground, paper, button;
            IBrush line = new SolidColorBrush(Color.FromRgb(0xD3, 0xD7, 0xE3));
            double radius = 4;
            if (id == Skins.Classic)
            {
                ground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
                paper = new SolidColorBrush(Color.FromRgb(0xEC, 0xE9, 0xD8));
                button = Skin.Linear(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xD6, 0xD0, 0xC0), false);
                line = new SolidColorBrush(Color.FromRgb(0x71, 0x6F, 0x64));
                radius = 0;
            }
            else if (id == Skins.Gradient)
            {
                ground = Skin.Linear(Chrome.Blend(accent, Colors.White, 0.55), Chrome.Blend(second, Colors.White, 0.45), true);
                paper = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
                button = Skin.Linear(accent, second, true);
                line = Brushes.Transparent;
                radius = 7;
            }
            else if (id == Skins.Eden)
            {
                ground = Skin.Linear(Color.FromRgb(0xE9, 0xD9, 0xB5), Color.FromRgb(0xC2, 0x9A, 0x62), true);
                paper = new SolidColorBrush(Color.FromRgb(0xF6, 0xEB, 0xD0));
                button = new SolidColorBrush(Color.FromRgb(0x6B, 0x54, 0x40));
                line = new SolidColorBrush(Color.FromRgb(0xA9, 0x8F, 0x63));
                radius = 2;
            }
            else if (id == Skins.Cyberpunk)
            {
                ground = new SolidColorBrush(Color.FromRgb(0x07, 0x06, 0x0D));
                paper = new SolidColorBrush(Color.FromRgb(0x11, 0x0E, 0x1F));
                button = new SolidColorBrush(Chrome.Tint(Chrome.HueOf(accent), 1.0, 0.64));
                line = new SolidColorBrush(Chrome.Tint(Chrome.HueOf(second), 1.0, 0.60));
                radius = 1;
            }
            else if (Skins.IsMuseum(id))
            {
                // Le visage d'origine du skin (sombre pour deux d'entre eux), son métal en bouton et en filets.
                var night = Skins.DarkByDefault(id);
                var back = Chrome.ToColor(Ink.Parse(Skins.MuseumBackground(id, night)));
                var metal = Chrome.ToColor(Ink.Parse(Skins.MuseumAccent(id)));
                ground = Skin.Linear(Chrome.Blend(back, Colors.Black, night ? 0.22 : 0.08), back, true);
                paper = new SolidColorBrush(Chrome.Blend(back, Colors.White, night ? 0.09 : 0.55));
                button = new SolidColorBrush(metal);
                line = new SolidColorBrush(Chrome.Blend(metal, back, 0.35));
                radius = id == Skins.MuseumAscent ? 0 : id == Skins.MuseumOath ? 2 : 5;
            }
            else
            {
                ground = new SolidColorBrush(Color.FromRgb(0xE2, 0xE5, 0xEE));
                paper = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                button = new SolidColorBrush(accent);
            }
            var inner = new Panel();
            inner.Children.Add(new Border { Width = 46, Height = 9, CornerRadius = new CornerRadius(radius), Background = button, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 7, 6) });
            inner.Children.Add(new Border { Width = 70, Height = 4, Background = line, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 9, 0, 0) });
            inner.Children.Add(new Border { Width = 50, Height = 4, Background = line, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 18, 0, 0), Opacity = 0.7 });
            var window = new Border { Background = paper, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Margin = new Thickness(14, 9, 14, 9), Child = inner };
            // Hauteur ≠ largeur et coins figés : l'habillage du skin EN COURS ne retouche pas la vignette d'un autre.
            return new Border { Height = 64, Background = ground, CornerRadius = new CornerRadius(0), ClipToBounds = true, Child = window, Tag = "skin-preview" };
        }

        internal void SetSkin(string id)
        {
            id = Skins.Normalize(id);
            if (id == Skin.Current) return;
            // « Ascension » (25/11, Rémi) : ses angles droits ne sont plus forcés — le choisir pose l'arrondi à 0 %, et le
            // curseur rend des rondeurs à qui en veut. En le quittant à 0 %, l'arrondi d'origine revient.
            var leaving = Skin.Current;
            if (id == Skins.MuseumAscent) PoseCornerScale(0);
            else if (leaving == Skins.MuseumAscent && AppSettings.CornerScale < 0.001) PoseCornerScale(1);
            AppSettings.Skin = id == Skins.Modern ? null : id;
            // « Sombre par défaut » : choisir le skin passe au sombre ; on en ressort par la case, comme toujours.
            if (Skins.DarkByDefault(id) && !AppSettings.DarkTheme)
            {
                AppSettings.DarkTheme = true;
                if (_darkCheck != null) _darkCheck.IsChecked = true;
            }
            RaiseAppearanceChanged();
            RebuildSkins();
            RebuildSwatches();
        }

        internal void SetSecond(string hex)
        {
            AppSettings.SkinAccent2 = Skins.IsHex(hex) ? hex.ToUpperInvariant() : null;
            RaiseAppearanceChanged();
            RebuildSkins();
        }

        internal int SkinCardsForProbe { get { return _skinCards.Children.Count; } }
        internal bool SecondRowShownForProbe { get { return _secondRow.IsVisible; } }

        /// <summary>La grille des pastilles, l'accent courant cerclé d'encre.</summary>
        private void RebuildSwatches()
        {
            _swatches.Children.Clear();
            // Sans accent choisi, un skin « Museum » porte le SIEN (pas l'indigo) : aucune des pastilles communes n'est cerclée.
            var current = AppSettings.AccentColor ?? (Skin.Museum ? null : Chrome.DefaultAccent);
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
                    SetAccent(!Skin.Museum && string.Equals(hexRef, Chrome.DefaultAccent, StringComparison.OrdinalIgnoreCase) ? null : hexRef);
                };
                _swatches.Children.Add(swatch);
            }
            AddMetalSwatches();
        }

        /// <summary>LES TROIS MÉTAUX d'« Ascension » (25/11) : or, vermeil, argent — après les pastilles
        /// communes, derrière un filet, sous ce skin seulement. Leur bordure est la dorure qui luit (ces
        /// couleurs-là appartiennent au skin) ; le métal en cours porte un losange or pâle au-dessus.
        /// L'or est le défaut : le choisir rend l'accent au skin (AccentColor nul).</summary>
        private void AddMetalSwatches()
        {
            if (!AscensionArt.On) return;
            _swatches.Children.Add(new Border { Width = 1, Height = 18, Margin = new Thickness(6, 0, 6, 0), Background = Chrome.Border, VerticalAlignment = VerticalAlignment.Center, Tag = "skin-metal-rule" });
            var chosen = AscensionArt.MetalFor(AppSettings.AccentColor);
            foreach (var metal in AscensionArt.Metals)
            {
                var swatch = new Border
                {
                    Width = 28,
                    Height = 28,
                    CornerRadius = new CornerRadius(0),
                    Background = new SolidColorBrush(Color.Parse(metal.Hex)),
                    BorderBrush = AscensionArt.GiltLine(),
                    BorderThickness = new Thickness(2.5),
                    BoxShadow = BoxShadows.Parse("inset 0 1 0 0 #59FFFFFF"),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Tag = "skin-metal:" + metal.Key
                };
                ToolTip.SetTip(swatch, metal.Label + " — métal d'Ascension");
                var picked = metal;
                swatch.PointerReleased += delegate { SetAccent(picked.Key == "or" ? null : picked.Hex); };
                var host = new Panel { Margin = new Thickness(2), Tag = ReferenceEquals(metal, chosen) ? "skin-metal-chosen" : "skin-metal" };
                host.Children.Add(swatch);
                if (ReferenceEquals(metal, chosen))
                    host.Children.Add(new Border
                    {
                        Width = 6, Height = 6, Background = Chrome.Glow, IsHitTestVisible = false,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -8, 0, 0),
                        RenderTransform = new RotateTransform(45)
                    });
                _swatches.Children.Add(host);
            }
        }

        internal void SetAccentForProbe(string hex) { SetAccent(hex); }

        private void SetAccent(string hex)
        {
            AppSettings.AccentColor = hex;
            RebuildSwatches();
            RaiseAppearanceChanged();
            if (_skinCards != null) RebuildSkins(); // les vignettes et la seconde couleur automatique suivent l'accent
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
            // Les actions d'une fonctionnalité cachée par le profil (1.1.0) ne se listent pas.
            var categories = new List<string>();
            foreach (var action in AppSettings.Actions)
                if (ActionShown(action) && !categories.Contains(action.Category)) categories.Add(action.Category);
            foreach (var category in categories)
            {
                panel.Children.Add(Caption(category, 10));
                foreach (var action in AppSettings.Actions)
                    if (action.Category == category && ActionShown(action))
                        panel.Children.Add(ShortcutRow(action));
            }
            // Les raccourcis NON configurables (31/10, Rémi) : câblés, listés
            // dans un accordéon replié en fin de page.
            var fixedList = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            var cmd = Ui.CommandKey;
            foreach (var pair in new[]
            {
                new[] { "Ctrl+Tab / Ctrl+Maj+Tab", "Onglet suivant / précédent (cyclique)" },
                new[] { cmd + "+Maj+Droite / " + cmd + "+Maj+Gauche", "Onglet suivant / précédent" },
                new[] { "Clic molette sur un onglet", "Fermer l'onglet" },
                new[] { "Maj+molette", "Défilement horizontal" },
                new[] { cmd + "+molette", "Zoom (éditeur, carte du Gros Graph)" },
                new[] { "Échap", "Fermer la recherche, vider un champ, désélectionner sur la carte, fermer un menu de propositions" },
                new[] { "Entrée, Tab, flèches dans un champ à propositions", "Prendre la proposition, s'y déplacer" },
                new[] { "Ctrl+clic / Maj+clic dans la Pile", "Sélection multiple (le Général en mode lot)" },
                new[] { "F1 / F2 sur une fiche", "Vue wiki / arbre généalogique" },
                new[] { "Double-clic sur un nœud du Gros Graph", "Ouvrir l'élément" }
            })
            {
                var row = new DockPanel();
                var keys = new TextBlock { Text = pair[0], Foreground = Chrome.Ink, FontSize = 12, Width = 300, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(keys, Dock.Left);
                row.Children.Add(keys);
                row.Children.Add(new TextBlock { Text = pair[1], Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                fixedList.Children.Add(row);
            }
            panel.Children.Add(new Expander
            {
                Header = new TextBlock { Text = "Raccourcis non-configurables", Foreground = Chrome.Ink, FontSize = 12, FontWeight = FontWeight.SemiBold },
                Content = fixedList,
                IsExpanded = false,
                Margin = new Thickness(0, 18, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = "fixed-shortcuts"
            });
            return panel;
        }

        private static bool ActionShown(ActionDefinition action)
        {
            switch (action.Id)
            {
                case "session-goal": return Profiles.Enabled(Profiles.Sprints);
                case "new-sheet":
                case "insert-link":
                case "templates": return Profiles.Enabled(Profiles.Sheets);
                case "loose-lines": return Profiles.Enabled(Profiles.LooseLines);
                default: return true;
            }
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

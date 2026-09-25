using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.View
{
    /// <summary>Application preferences (Fichier → Préférences…), applied live.
    /// Onglets (22/09) : Personnalisation (accent, mode sombre, papier blanc),
    /// Édition, Correction, Styles globaux (la feuille de tous les projets et
    /// le séparateur de scène global), Auteur (l'auteur·ice et les métadonnées
    /// par défaut), Raccourcis, DLC. Tous les onglets ont la même largeur.
    /// Everything is stored in AppSettings (per user, all projects).</summary>
    public class PreferencesDialog : Window
    {
        /// <summary>Raised whenever appearance changed — the main window
        /// re-applies Chrome + Theme and refreshes what needs it.</summary>
        public event Action AppearanceChanged;

        /// <summary>Un dictionnaire personnel a changé : l'éditeur doit
        /// oublier ses verdicts en cache et revérifier.</summary>
        public event Action ProofingChanged;

        /// <summary>Un raccourci a changé (onglet Raccourcis, b43) : la
        /// fenêtre principale reconstruit menus et KeyBindings.</summary>
        public event Action ShortcutsChanged;

        /// <summary>Les styles globaux ont été édités (22/09) : le projet
        /// ouvert les reprend.</summary>
        public event Action GlobalStylesChanged;

        private readonly Model.Project _project; // null : aucun projet ouvert

        private readonly WrapPanel _swatches;
        private CheckBox _whitePaper, _darkCheck;

        // Accents proposés : l'indigo maison puis des teintes sages, toutes
        // lisibles en clair comme en sombre (le pas sombre est dérivé).
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

        public PreferencesDialog(Window owner, Model.Project project)
        {
            _project = project;
            Title = "Préférences";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            // Taille UNIQUE pour tous les onglets (b43) — fini la fenêtre qui
            // change de taille à chaque onglet.
            Width = 760;
            Height = 660;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var tabs = new TabControl { Margin = new Thickness(10) };
            _swatches = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            tabs.Items.Add(Tab("Personnalisation", BuildPersonalizationTab()));
            tabs.Items.Add(Tab("Édition", BuildEditingTab()));
            tabs.Items.Add(Tab("Correction", BuildProofingTab()));
            tabs.Items.Add(Tab("Styles globaux", BuildGlobalStylesTab()));
            tabs.Items.Add(Tab("Auteur", BuildAuthorTab()));
            tabs.Items.Add(Tab("Raccourcis", BuildShortcutsTab()));
            tabs.Items.Add(Tab("DLC", BuildModulesTab()));

            var layout = new DockPanel();
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(16, 0, 16, 14)
            };
            var close = new Button { Content = "Fermer", IsDefault = true, IsCancel = true, MinWidth = 90 };
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);
            layout.Children.Add(tabs);

            Content = layout;
        }

        /// <summary>Un onglet : son contenu occupe toute la largeur et toute la
        /// hauteur de la fenêtre (22/09 — Édition et Correction se serraient
        /// au milieu, bornés par un MaxWidth).</summary>
        private static TabItem Tab(string header, UIElement content)
        {
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            host.Children.Add(content);
            return new TabItem
            {
                Header = header,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch,
                    Content = host
                }
            };
        }

        private static StackPanel TabPanel()
        {
            return new StackPanel { Margin = new Thickness(16, 12, 16, 12), HorizontalAlignment = HorizontalAlignment.Stretch };
        }

        // ------------------------------------------------------------ DLC (22/09)

        private StackPanel _modulesPanel;

        /// <summary>Onglet « DLC » : les modules du catalogue et ceux installés
        /// depuis un fichier — leur état, « Détails… » (la fiche : ce qu'il
        /// apporte, Installer / Désinstaller), et « Installer depuis un
        /// fichier .mdlc… » pour un paquet obtenu autrement.</summary>
        private UIElement BuildModulesTab()
        {
            var panel = TabPanel();
            panel.Children.Add(new TextBlock
            {
                Text = Modules.DownloadsEnabled
                    ? "Les modules (DLC) ajoutent des fonctions à Marabook : une fiche avancée, des succès… Ils s'installent depuis GitHub ou depuis un paquet .mdlc, sans redémarrage ; désinstallés, les projets gardent leurs valeurs."
                    : "Les modules (DLC) ajoutent des fonctions à Marabook : une fiche avancée, des cartes mentales, des succès… Leur téléchargement arrivera dans une prochaine version ; un paquet .mdlc obtenu autrement s'installe déjà, sans redémarrage.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
            _modulesPanel = new StackPanel();
            panel.Children.Add(_modulesPanel);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var fromFile = Buttons.Text("Installer depuis un fichier .mdlc…", "Un paquet de module obtenu autrement que par GitHub", Buttons.Bar, Buttons.Look.Outline);
            fromFile.Click += delegate { InstallModuleFromFile(); };
            actions.Children.Add(fromFile);
            var check = Buttons.Text("Vérifier les versions",
                Modules.DownloadsEnabled ? "Interroge GitHub pour chaque module du catalogue" : "Dans une prochaine version",
                Buttons.Bar, Buttons.Look.Outline);
            check.Margin = new Thickness(8, 0, 0, 0);
            check.IsEnabled = Modules.DownloadsEnabled;
            check.Click += delegate
            {
                foreach (var state in ModuleStore.States) state.Checked = false;
                ModuleStore.CheckOnline(Dispatcher);
                RefreshModules();
            };
            actions.Children.Add(check);
            panel.Children.Add(actions);
            if (Modules.LastLoadError.Length > 0)
                panel.Children.Add(new TextBlock
                {
                    Text = "Un module n'a pas pu être chargé au lancement : " + Modules.LastLoadError
                        + "\nVérifiez son dossier (module.json, DLL bloquée par Windows : clic droit › Propriétés › Débloquer).",
                    Foreground = Chrome.Warn,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 12, 0, 0)
                });
            panel.Children.Add(new TextBlock
            {
                Text = "Dossier des modules : " + Modules.Root,
                Foreground = Chrome.FaintText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });
            RefreshModules();
            ModuleStore.CheckOnline(Dispatcher);
            ModuleStore.Changed += RefreshModules;
            Closed += delegate { ModuleStore.Changed -= RefreshModules; };
            return panel;
        }

        private void RefreshModules()
        {
            if (_modulesPanel == null) return;
            _modulesPanel.Children.Clear();
            foreach (var state in ModuleStore.Refresh())
            {
                var stateRef = state;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var details = Buttons.Text(state.IsInstalled ? "Gérer…" : "Détails…",
                    "La fiche du module : ce qu'il apporte, installer ou désinstaller", Buttons.Compact, Buttons.Look.Outline);
                details.Margin = new Thickness(12, 0, 0, 0);
                details.VerticalAlignment = VerticalAlignment.Center;
                details.Click += delegate { ModuleDialog.Show(this, stateRef); };
                DockPanel.SetDock(details, Dock.Right);
                row.Children.Add(details);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var name = new TextBlock { FontSize = 13, Foreground = Chrome.Ink };
                name.Inlines.Add(new System.Windows.Documents.Run(state.Source.Name) { FontWeight = FontWeights.SemiBold });
                if (state.Source.Title.Length > 0)
                    name.Inlines.Add(new System.Windows.Documents.Run("  " + state.Source.Title) { Foreground = Chrome.SoftText, FontSize = 12 });
                text.Children.Add(name);
                text.Children.Add(new TextBlock
                {
                    Text = state.Label,
                    FontSize = 11,
                    Foreground = state.IsInstalled ? Chrome.Accent : Chrome.SoftText,
                    Margin = new Thickness(0, 2, 0, 0)
                });
                row.Children.Add(text);
                _modulesPanel.Children.Add(row);
            }
            if (_modulesPanel.Children.Count == 0)
                _modulesPanel.Children.Add(new TextBlock { Text = "Aucun module connu.", Foreground = Chrome.SoftText, FontSize = 12 });
        }

        private void InstallModuleFromFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Installer un module",
                Filter = "Module Marabook (*.mdlc)|*.mdlc",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var module = ModuleStore.InstallFromFile(dialog.FileName);
                MessageDialog.Show(this, module.Name + (module.Version.Length > 0 ? " " + module.Version : "") + " est installé — prêt, sans redémarrage.",
                    "Modules", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception failure)
            {
                MessageDialog.Show(this, "Installation impossible : " + failure.Message, "Modules", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ------------------------------------------------------ styles globaux (22/09)

        /// <summary>Onglet « Styles globaux » : l'outil de gestion des styles
        /// sur la feuille de tous les projets (portée globale seulement), et
        /// le séparateur de scène global en dessous. Chaque édition est
        /// enregistrée et poussée au projet ouvert.</summary>
        private UIElement BuildGlobalStylesTab()
        {
            var panel = TabPanel();
            if (AppSettings.GlobalStyles == null)
            {
                AppSettings.GlobalStyles = _project != null ? GlobalStyles.ForNewProject() : StyleSheet.CreateDefault();
            }
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

        // ------------------------------------------------------ auteur (22/09)

        /// <summary>Onglet « Auteur » : le nom appliqué par défaut aux documents
        /// qui ne sont pas des livres (compilation, commentaires Word,
        /// liminaires sans auteur de livre), et les métadonnées générales qui
        /// pré-remplissent chaque nouveau livre.</summary>
        private UIElement BuildAuthorTab()
        {
            var panel = TabPanel();
            panel.Children.Add(Caption("Auteur·ice par défaut"));
            panel.Children.Add(new TextBlock
            {
                Text = "Le nom signé sur ce qui n'est pas un livre : la compilation d'écrits, les commentaires exportés vers Word, les pages liminaires d'un livre qui ne nomme pas son auteur·ice. Chaque livre peut nommer le sien dans son onglet Édition.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });
            panel.Children.Add(AuthorRow("Nom", AppSettings.DefaultAuthor, delegate(string value) { AppSettings.DefaultAuthor = value; }));

            panel.Children.Add(Caption("Métadonnées par défaut", 18));
            panel.Children.Add(new TextBlock
            {
                Text = "Pré-remplies dans chaque nouveau livre ; modifiables ensuite livre par livre.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });
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
            box.LostKeyboardFocus += delegate
            {
                var text = box.Text.Trim();
                if (text == (value ?? "")) return;
                value = text;
                store(text);
                AppSettings.PublishDefaults();
                AppSettings.Save();
            };
            row.Children.Add(box);
            return row;
        }

        /// <summary>Onglet « Édition » : les versions d'écrits (le mode de
        /// compatibilité classique a disparu le 13/09).</summary>
        private UIElement BuildEditingTab()
        {
            var panel = TabPanel();
            // Les dictionnaires personnels ont quitté ce volet (batch 34) :
            // l'écran « Dictionnaire » de la Pile tient ce rôle, avec les
            // natures grammaticales et les formes acceptées.

            // — La sélection à la souris (0.50.0).
            panel.Children.Add(Caption("Sélection"));
            var autoWord = new CheckBox
            {
                Content = "Auto-sélecteur de mot",
                IsChecked = AppSettings.AutoSelectWord,
                Margin = new Thickness(0, 6, 0, 0)
            };
            autoWord.Click += delegate
            {
                AppSettings.AutoSelectWord = autoWord.IsChecked == true;
                AppSettings.Save();
            };
            panel.Children.Add(autoWord);
            panel.Children.Add(new TextBlock
            {
                Text = "Complète la sélection d'un mot lorsque vous n'en sélectionnez qu'une partie",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(22, 4, 0, 0)
            });

            // — Les versions d'écrits (batch 38).
            panel.Children.Add(Caption("Versions d'écrits", 16));
            var daily = new CheckBox
            {
                Content = "Instantané automatique à la première modification du jour",
                IsChecked = AppSettings.DailySnapshot,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "L'état de l'écrit tel qu'ouvert, figé une fois par jour et par écrit — il compte dans le plafond"
            };
            daily.Click += delegate
            {
                AppSettings.DailySnapshot = daily.IsChecked == true;
                AppSettings.Save();
            };
            panel.Children.Add(daily);
            var capRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            var capBox = new TextBox { Width = 56, Text = AppSettings.SnapshotCap.ToString(), ToolTip = "Entre " + Model.SnapshotStore.MinCap + " et " + Model.SnapshotStore.MaxCap };
            DockPanel.SetDock(capBox, Dock.Left);
            capBox.LostKeyboardFocus += delegate
            {
                int value;
                if (!int.TryParse(capBox.Text.Trim(), out value)) value = AppSettings.SnapshotCap;
                value = Math.Max(Model.SnapshotStore.MinCap, Math.Min(Model.SnapshotStore.MaxCap, value));
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

        /// <summary>Onglet « Correction » (batch 29, lot C) : la grammaire
        /// Grammalecte — interrupteur maître et les options SOUS LEUR
        /// NOMENCLATURE À LUI (jamais la nôtre), groupées par catégorie.
        /// La politique de recouvrement est visible et honnête : une option
        /// éteinte parce que Marabook couvre déjà le terrain (répétitions,
        /// typographie du compositeur, mots composés) le dit — et reste
        /// rallumable, en connaissance de cause.</summary>
        private UIElement BuildProofingTab()
        {
            var panel = TabPanel();
            panel.Children.Add(Caption("Grammaire (Grammalecte)"));
            var master = new CheckBox
            {
                Content = "Vérification grammaticale (Grammalecte, en différé)",
                IsChecked = AppSettings.GrammarEnabled,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Grammalecte tourne dans un sous-processus : ses "
                    + "signalements arrivent quelques instants après la frappe,\n"
                    + "sans jamais ralentir l'orthographe ni la saisie."
            };
            master.Click += delegate
            {
                AppSettings.GrammarEnabled = master.IsChecked == true;
                AppSettings.Save();
                var handler = ProofingChanged;
                if (handler != null) handler();
            };
            panel.Children.Add(master);
            panel.Children.Add(new TextBlock
            {
                Text = "Les options reprennent les noms de Grammalecte. Celles "
                    + "marquées « couvert par Marabook » sont éteintes parce "
                    + "qu'un vérificateur maison tient déjà ce terrain "
                    + "(répétitions réglées pour le roman, typographie du "
                    + "compositeur, mots composés de l'orthographe) — les "
                    + "rallumer produit des signalements en double.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 4)
            });

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
                var value = AppSettings.GrammarOptions.TryGetValue(
                    option.Name, out overridden)
                    ? overridden : option.MarabookDefault;
                var check = new CheckBox
                {
                    Content = option.Label + " (" + option.Name + ")"
                        + (option.OverriddenByPolicy
                            ? " — couvert par Marabook" : ""),
                    IsChecked = value,
                    Margin = new Thickness(0, 3, 0, 0)
                };
                check.Click += delegate
                {
                    var chosen = check.IsChecked == true;
                    // Revenu au défaut Marabook : l'entrée disparaît (les
                    // défauts futurs de la politique s'appliqueront).
                    if (chosen == optionRef.MarabookDefault)
                        AppSettings.GrammarOptions.Remove(optionRef.Name);
                    else
                        AppSettings.GrammarOptions[optionRef.Name] = chosen;
                    AppSettings.Save();
                    var handler = ProofingChanged;
                    if (handler != null) handler();
                };
                options.Children.Add(check);
            }
            // La liste des options occupe le reste de l'onglet (22/09) — plus
            // de boîte de 260 px au milieu.
            panel.Children.Add(new ScrollViewer
            {
                Content = options,
                Height = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 2, 0, 0)
            });
            return panel;
        }

        private UIElement BuildPersonalizationTab()
        {
            var panel = TabPanel();

            panel.Children.Add(Caption("Couleur d'accent"));
            panel.Children.Add(new TextBlock
            {
                Text = "Boutons, sélections, liens et repères prennent cette teinte.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });

            RebuildSwatches();
            panel.Children.Add(_swatches);

            var custom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var pick = new Button { Content = "Personnalisée…", MinWidth = 110 };
            pick.Click += delegate
            {
                var hex = ColorDialog.Ask(this);
                if (hex == null) return;
                SetAccent(string.Equals(hex, Chrome.DefaultAccent,
                    StringComparison.OrdinalIgnoreCase) ? null : hex);
            };
            custom.Children.Add(pick);
            var reset = new Button
            {
                Content = "Réinitialiser",
                MinWidth = 100,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Revenir à l'indigo par défaut"
            };
            reset.Click += delegate { SetAccent(null); };
            custom.Children.Add(reset);
            panel.Children.Add(custom);

            // La vitesse du défilement (0.50.0) : un curseur, et une mini-
            // fenêtre qui défile à sa droite pour l'essayer tout de suite —
            // elle passe par le même défilement fluide que le reste.
            panel.Children.Add(Caption("Défilement", 18));
            var speedRow = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = true };
            var preview = new ScrollViewer
            {
                Width = 210,
                Height = 100,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                ToolTip = "Essayez la molette ici"
            };
            var previewLines = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
            for (var i = 1; i <= 40; i++)
                previewLines.Children.Add(new TextBlock
                {
                    Text = "Ligne " + i + " — la molette fait défiler ce texte à la vitesse choisie.",
                    Foreground = Chrome.PaperInk,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            preview.Content = previewLines;
            DockPanel.SetDock(preview, Dock.Right);
            speedRow.Children.Add(preview);
            var speedColumn = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var speedLabel = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            var speed = new Slider
            {
                Minimum = 0.25,
                Maximum = 3,
                Value = AppSettings.ScrollSpeed,
                TickFrequency = 0.25,
                IsSnapToTickEnabled = true,
                Width = 260,
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Vitesse du défilement à la molette : ×0,25 (lent) à ×3 (rapide)"
            };
            Action refreshSpeedLabel = delegate
            {
                speedLabel.Text = "Vitesse : ×" + AppSettings.ScrollSpeed.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
                    + (Math.Abs(AppSettings.ScrollSpeed - 1) < 0.01 ? " (le pas de Windows)" : "");
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
            speedColumn.Children.Add(new TextBlock
            {
                Text = "Le défilement à la molette est fluide partout ; ce réglage en change le pas.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            speedRow.Children.Add(speedColumn);
            panel.Children.Add(speedRow);

            panel.Children.Add(Caption("Mode sombre", 18));
            // Le commutateur (22/09) : le raccourci Ctrl+Maj+L et le menu
            // Affichage font la même chose, mais personne ne les apprend.
            _darkCheck = new CheckBox
            {
                Content = "Mode sombre",
                IsChecked = AppSettings.DarkTheme,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "Aussi : Affichage › Thème sombre (" + AppSettings.DisplayGesture(AppSettings.Gesture("dark-theme") ?? "") + ")"
            };
            _darkCheck.Click += delegate
            {
                AppSettings.DarkTheme = _darkCheck.IsChecked == true;
                RaiseAppearanceChanged();
            };
            panel.Children.Add(_darkCheck);
            _whitePaper = new CheckBox
            {
                Content = "Garder le papier blanc malgré le mode sombre",
                IsChecked = AppSettings.WhitePaperInDark,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = "L'interface reste sombre mais les pages (fiches, gabarits)\n"
                    + "gardent leur papier blanc et leur encre noire, comme à l'impression."
            };
            _whitePaper.Click += delegate
            {
                AppSettings.WhitePaperInDark = _whitePaper.IsChecked == true;
                RaiseAppearanceChanged();
            };
            panel.Children.Add(_whitePaper);
            panel.Children.Add(new TextBlock
            {
                Text = "La vue Composition, l'aperçu et le PDF sont toujours noir sur blanc.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(22, 4, 0, 0)
            });

            return panel;
        }

        // ------------------------------------------------------ raccourcis (b43)

        /// <summary>L'onglet « Raccourcis » : toutes les actions de
        /// l'application, groupées par catégorie — cliquer le champ puis
        /// taper la combinaison ; Retour arrière retire le raccourci,
        /// Échap referme sans changer. Personnalisations dans
        /// AppSettings.Shortcuts (settings.json), appliquées aussitôt.</summary>
        private UIElement BuildShortcutsTab()
        {
            var panel = TabPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Cliquez un champ puis tapez la combinaison voulue. "
                     + "Retour arrière retire le raccourci ; « Défaut » restaure celui d'origine.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
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

        private UIElement ShortcutRow(ActionDefinition action)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };

            // Le champ n'est PAS focusable tant qu'on ne l'a pas cliqué (22/09) :
            // à l'arrivée sur l'onglet, WPF donnait le clavier au premier champ
            // de la page, qui passait en captation (« Tapez… ») sans qu'on
            // l'ait demandé — même piège que les champs libres des fiches.
            var box = new TextBox
            {
                Width = 150,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                TextAlignment = TextAlignment.Center,
                Cursor = Cursors.Hand,
                Focusable = false,
                IsTabStop = false,
                ToolTip = "Cliquer puis taper la combinaison — Retour arrière : aucun raccourci"
            };
            var reset = new Button
            {
                Content = "Défaut",
                FontSize = 11,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Revenir au raccourci d'origine"
                    + (string.IsNullOrEmpty(action.DefaultGesture) ? " (aucun)" : " : " + AppSettings.DisplayGesture(action.DefaultGesture))
            };
            Action sync = delegate
            {
                var gesture = AppSettings.Gesture(action.Id);
                box.Text = string.IsNullOrEmpty(gesture) ? "—" : AppSettings.DisplayGesture(gesture);
                var custom = AppSettings.Shortcuts.ContainsKey(action.Id);
                box.FontWeight = custom ? FontWeights.SemiBold : FontWeights.Normal;
                reset.Visibility = custom ? Visibility.Visible : Visibility.Hidden;
            };
            Action<string> store = delegate(string gesture)
            {
                if (gesture == (action.DefaultGesture ?? "")) AppSettings.Shortcuts.Remove(action.Id);
                else AppSettings.Shortcuts[action.Id] = gesture;
                AppSettings.Save();
                sync();
                var handler = ShortcutsChanged;
                if (handler != null) handler();
                // Un doublon n'est pas interdit, mais dit (22/09) : le premier
                // atteint l'emporte — dans l'éditeur, son geste passe avant
                // ceux de la fenêtre.
                if (gesture.Length == 0) return;
                foreach (var other in AppSettings.Actions)
                {
                    if (other.Id == action.Id || AppSettings.Gesture(other.Id) != gesture) continue;
                    MessageDialog.Show(this,
                        AppSettings.DisplayGesture(gesture) + " est déjà le raccourci de « " + other.Name + " » (" + other.Category + ").\n\n"
                        + "Les deux le gardent ; le premier atteint l'emporte. Changez l'un des deux si cela gêne.",
                        "Raccourcis", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }
            };
            box.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                box.Focusable = true;
                box.Focus();
                e.Handled = true;
            };
            box.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                e.Handled = true;
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift
                    || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin) return;
                if (key == Key.Escape) { Keyboard.ClearFocus(); sync(); return; }
                if (key == Key.Back) { store(""); return; }
                var gesture = "";
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) gesture += "Ctrl+";
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) gesture += "Shift+";
                if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) gesture += "Alt+";
                store(gesture + key);
            };
            box.GotKeyboardFocus += delegate { box.Text = "Tapez…"; };
            box.LostKeyboardFocus += delegate { sync(); box.Focusable = false; };
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

        private TextBlock Caption(string text, double topMargin = 0)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, topMargin, 0, 0)
            };
        }

        /// <summary>The swatch grid, the current accent ringed with the ink color
        /// (never the accent itself — it must read on every hue).</summary>
        private void RebuildSwatches()
        {
            _swatches.Children.Clear();
            var current = AppSettings.AccentColor ?? Chrome.DefaultAccent;
            foreach (var entry in Accents)
            {
                var hex = entry[0];
                var selected = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
                var swatch = new Button
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(FlowConverter.ParseColor(hex)),
                    BorderBrush = selected ? (Brush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(selected ? 2.5 : 1),
                    ToolTip = entry[1] + " — " + hex
                };
                // Le gabarit de bouton du thème peint SON fond : un gabarit
                // minimal rend la couleur (règle maison : ne pas forcer le fond
                // d'un contrôle thémé, donc on remplace le gabarit entier).
                swatch.Template = SwatchTemplate();
                var hexRef = hex;
                swatch.Click += delegate
                {
                    SetAccent(string.Equals(hexRef, Chrome.DefaultAccent,
                        StringComparison.OrdinalIgnoreCase) ? null : hexRef);
                };
                _swatches.Children.Add(swatch);
            }
        }

        private static ControlTemplate SwatchTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty,
                new TemplateBindingExtension(BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty,
                new TemplateBindingExtension(BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty,
                new TemplateBindingExtension(BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            return new ControlTemplate(typeof(Button)) { VisualTree = border };
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
    }
}

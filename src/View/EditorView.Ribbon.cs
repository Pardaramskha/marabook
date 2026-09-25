using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>EditorView, partie « ruban » (segmentation b43) : la barre
    /// de format et ses onglets Texte/Gabarit/Composition/Mise en page,
    /// l'axe d'affichage, les fabriques de boutons et les menus de
    /// palette — rien que la construction et la synchronisation du ruban.</summary>
    public partial class EditorView
    {
        // ============================================================= construction

        private void BuildFormatBar()
        {
            var bar = new Border
            {
                Background = Chrome.BarBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            SetDock(bar, Dock.Top);
            // LE RUBAN À DEUX LIGNES (batch 28) : chaque onglet dispose de
            // deux rangées — les blocs denses s'empilent, les séparateurs
            // verticaux courent sur toute la hauteur.
            var panel = TabPanel();
            // SECTION 1 (0.50.0) : le style de paragraphe et l'éditeur de styles,
            // seuls, tout à gauche — le combo en haut, la gestion en bas.
            var styleRows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var styleTop = RibbonRow();
            var styleBottom = RibbonRow();
            styleRows.Children.Add(styleTop);
            styleRows.Children.Add(styleBottom);
            // SECTION 2 : police, taille, variantes en haut ; gras, italique,
            // souligné, barré, un trait, petites majuscules et caractères
            // spéciaux en bas.
            var typeRows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var typeTop = RibbonRow();
            var typeBottom = RibbonRow();
            typeRows.Children.Add(typeTop);
            typeRows.Children.Add(typeBottom);

            _styleCombo = new ComboBox { Width = 150, Margin = new Thickness(0, 0, 2, 0) };
            _styleCombo.SelectionChanged += OnStyleComboChanged;
            // Réappliquer le style courant (« Corps + » → « Corps ») : la liste
            // se referme sur le même item, SelectionChanged ne dit rien.
            _styleCombo.DropDownClosed += delegate
            {
                if (_syncing || _item == null || !ComposedActive) return;
                var chosen = _styleCombo.SelectedItem as ComboBoxItem;
                if (chosen == null || !_composed.CaretHasOverrides()) return;
                var paragraph = _composed.CaretParagraph;
                if (paragraph != null && paragraph.StyleId == (string)chosen.Tag)
                {
                    _composed.ApplyStyle((string)chosen.Tag);
                    _composed.FocusSurface();
                }
            };
            styleTop.Children.Add(_styleCombo);

            var manageStyles = new Button
            {
                Content = new TextBlock { Text = "Aa  Gestion des styles…", FontSize = 12, FontWeight = FontWeights.SemiBold },
                ToolTip = "Gestion des styles…",
                Width = 150,
                Height = 26,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 2, 0),
                Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            manageStyles.Click += delegate
            {
                var handler = StylesRequested;
                if (handler != null) handler();
            };
            styleBottom.Children.Add(manageStyles);
            panel.Children.Add(styleRows);
            panel.Children.Add(VerticalRuleTall());

            // Le sélecteur de police partagé (0.50.0) : récentes, trait,
            // alphabet, aperçu « Marabook » ; la frappe n'applique qu'à
            // Entrée, les flèches font défiler les polices en aperçu vivant.
            _fontCombo = new FontPicker
            {
                Width = 150,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Police — tapez un nom puis Entrée ; flèches haut/bas pour essayer les polices sur la sélection"
            };
            _fontCombo.FontChosen += OnFontChosen;
            typeTop.Children.Add(_fontCombo);

            // Sizes are displayed in points (like every word processor);
            // internally everything stays WPF pixels (1 pt = 4/3 px). Même
            // discipline que la police (0.50.0) : la frappe n'applique qu'à
            // Entrée — l'autocomplétion appliquait « 1 » → 10 pt et rendait le
            // clavier au texte, le reste de la frappe partait dedans.
            _sizeCombo = new ComboBox
            {
                Width = 52,
                Margin = new Thickness(0, 0, 10, 0),
                IsEditable = true,
                ToolTip = "Taille — tapez une valeur puis Entrée ; flèches haut/bas pour l'essayer"
            };
            foreach (var size in new[] { 8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 72 })
                _sizeCombo.Items.Add(size);
            _sizeCombo.SelectionChanged += OnSizeComboChanged;
            _sizeCombo.DropDownClosed += delegate
            {
                if (_syncing || _sizeCombo.SelectedItem == null || !_sizeDropDownChoice) return;
                _sizeDropDownChoice = false;
                ApplyTypedSize(_sizeCombo.SelectedItem.ToString());
            };
            _sizeCombo.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Return)
                {
                    e.Handled = true;
                    if (_sizeCombo.IsDropDownOpen) _sizeCombo.IsDropDownOpen = false;
                    ApplyTypedSize(_sizeCombo.Text);
                }
                else if ((e.Key == Key.Up || e.Key == Key.Down) && !_sizeCombo.IsDropDownOpen)
                    _sizeArrowNav = true;
            };
            typeTop.Children.Add(_sizeCombo);

            // Variantes de caractère (Fin, Normal, Moyen, Demi-gras, Gras, Noir).
            var weightBtn = new Button
            {
                ToolTip = "Variantes de caractère",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("font-variant", 14, Chrome.Ink)
            };
            var weightMenu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = weightBtn };
            weightBtn.ContextMenu = weightMenu;
            weightBtn.Click += delegate
            {
                BuildWeightMenu(weightMenu); // graisses de LA police, coche incluse
                weightMenu.IsOpen = true;
            };
            typeTop.Children.Add(weightBtn);

            _boldBtn = FormatToggle("G", "Gras", true, false, false, false);
            _boldBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleBold(); _composed.FocusSurface(); }
            };
            _italicBtn = FormatToggle("I", "Italique", false, true, false, false);
            _italicBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleItalic(); _composed.FocusSurface(); }
            };
            _underBtn = FormatToggle("S", "Souligné", false, false, true, false);
            _underBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleUnderline(); _composed.FocusSurface(); }
            };
            _strikeBtn = FormatToggle("B", "Barré", false, false, false, true);
            _strikeBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleStrike(); _composed.FocusSurface(); }
            };
            // Icônes Flaticon (batch 28) — les lettres G/I/S/B laissent place
            // aux glyphes universels.
            DressToggle(_boldBtn, "bold", 12);
            DressToggle(_italicBtn, "italic", 12);
            DressToggle(_underBtn, "underline", 12);
            DressToggle(_strikeBtn, "strikethrough", 12);
            typeBottom.Children.Add(_boldBtn);
            typeBottom.Children.Add(_italicBtn);
            typeBottom.Children.Add(_underBtn);
            typeBottom.Children.Add(_strikeBtn);
            // Un trait de la hauteur des boutons, puis les deux nouveaux
            // carrés (0.50.0) : petites majuscules (bascule) et le tiroir des
            // caractères spéciaux.
            typeBottom.Children.Add(VerticalRule());
            _smallCapsBtn = new ToggleButton
            {
                ToolTip = "Petites majuscules",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
            DressToggle(_smallCapsBtn, "smallcaps", 14);
            _smallCapsBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleSmallCaps(); _composed.FocusSurface(); }
            };
            typeBottom.Children.Add(_smallCapsBtn);
            var specialBtn = new Button
            {
                ToolTip = "Caractères spéciaux",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("special-chars", 14, Chrome.Ink)
            };
            _specialDrawer = SpecialCharsDrawer.Build(specialBtn,
                delegate
                {
                    var name = _composed != null && _item != null && ComposedActive ? _composed.GetCaretFontFamily() : null;
                    return name == null ? null : FontCatalog.FamilyOf(name);
                },
                delegate(string text)
                {
                    if (!ComposedActive || _item == null) return;
                    _composed.InsertSpecial(text);
                    _composed.FocusSurface();
                });
            specialBtn.Click += delegate { _specialDrawer.IsOpen = !_specialDrawer.IsOpen; };
            typeBottom.Children.Add(specialBtn);
            panel.Children.Add(typeRows);
            panel.Children.Add(VerticalRuleTall());

            // Bloc alignements (haut) / listes (bas), borne a droite.
            var alignRows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var alignTop = RibbonRow();
            var alignBottom = RibbonRow();
            alignRows.Children.Add(alignTop);
            alignRows.Children.Add(alignBottom);

            _alignLeft = AlignToggle("left", "Aligné à gauche");
            _alignCenter = AlignToggle("center", "Centré");
            _alignRight = AlignToggle("right", "Aligné à droite");
            _alignJustify = AlignToggle("justify", "Justifié");
            _alignLeft.Content = Icons.Make("align-left", 12, Chrome.Ink);
            _alignCenter.Content = Icons.Make("align-center", 12, Chrome.Ink);
            // Pas d'icône « droite » dans le jeu : la gauche, en miroir.
            var alignRightIcon = Icons.Make("align-left", 12, Chrome.Ink) as FrameworkElement;
            if (alignRightIcon != null)
                alignRightIcon.LayoutTransform = new ScaleTransform(-1, 1);
            _alignRight.Content = alignRightIcon;
            _alignJustify.Content = Icons.Make("align-justify", 12, Chrome.Ink);
            alignTop.Children.Add(_alignLeft);
            alignTop.Children.Add(_alignCenter);
            alignTop.Children.Add(_alignRight);
            alignTop.Children.Add(_alignJustify);
            // L'interligne du document (22/09) : un multiplicateur de la
            // valeur d'interligne des styles — 1, 1,25, 1,5, 1,75, 2.
            var leadingIcon = Icons.Make("interligne", 14, Chrome.SoftText) as FrameworkElement;
            if (leadingIcon != null)
            {
                leadingIcon.ToolTip = "Interligne";
                leadingIcon.VerticalAlignment = VerticalAlignment.Center;
                leadingIcon.Margin = new Thickness(6, 0, 3, 0);
                alignTop.Children.Add(leadingIcon);
            }
            _leadingCombo = new ComboBox
            {
                Width = 58,
                Height = Buttons.Compact,
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
                ToolTip = "Interligne"
            };
            foreach (var factor in LeadingFactors)
                _leadingCombo.Items.Add(new ComboBoxItem { Content = factor.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")), Tag = factor });
            _leadingCombo.SelectedIndex = 0;
            _leadingCombo.SelectionChanged += delegate
            {
                if (_syncingPage || _item == null || _item.Document == null) return;
                var chosen = _leadingCombo.SelectedItem as ComboBoxItem;
                if (chosen == null) return;
                var value = (double)chosen.Tag;
                if (Math.Abs(_item.Document.LineSpacing - value) < 0.001) return;
                _item.Document.LineSpacing = value;
                if (ComposedActive) _composed.RefreshComposition();
                var handler = DocumentSettingChanged;
                if (handler != null) handler();
            };
            alignTop.Children.Add(_leadingCombo);

            _bulletBtn = IconToggle("list-bullets", "Liste à puces");
            _bulletBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyList("bullet"); _composed.FocusSurface(); }
            };
            _numberBtn = IconToggle("list-numbers-bold", "Liste numérotée");
            _numberBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyList("number"); _composed.FocusSurface(); }
            };
            _checkBtn = IconToggle("list-check", "Case à cocher");
            _checkBtn.Click += delegate
            {
                if (ComposedActive) _composed.TypeText("☐ ");
            };
            alignBottom.Children.Add(_bulletBtn);
            alignBottom.Children.Add(_numberBtn);
            alignBottom.Children.Add(_checkBtn);
            // Décalage du paragraphe (17/09), façon Word : + pousse le bloc de
            // 0,5 cm, − ramène tout à la marge (alinéa du style et retrait de
            // liste compris ; une seconde fois : le style reprend la main).
            var indentAdd = IconButton("space-add", "Ajouter un décalage");
            indentAdd.Margin = new Thickness(7, 0, 1, 0);
            indentAdd.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyIndent(true); _composed.FocusSurface(); }
            };
            var indentRemove = IconButton("space-remove", "Retirer le décalage");
            indentRemove.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyIndent(false); _composed.FocusSurface(); }
            };
            alignBottom.Children.Add(indentAdd);
            alignBottom.Children.Add(indentRemove);
            panel.Children.Add(alignRows);
            panel.Children.Add(VerticalRuleTall());

            // Bloc couleurs (haut) / insertions (bas) — deux rangées (b43).
            var restRows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var restTop = RibbonRow();
            var restBottom = RibbonRow();
            restRows.Children.Add(restTop);
            restRows.Children.Add(restBottom);
            panel.Children.Add(restRows);

            restTop.Children.Add(PaletteButton("Couleur du texte", true));
            restTop.Children.Add(PaletteButton("Surlignage", false));

            var imageBtn = new Button
            {
                ToolTip = "Insérer une image…",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("image-square-bold", 14, Chrome.Ink)
            };
            imageBtn.Click += delegate { InsertImage(); };
            restBottom.Children.Add(imageBtn);

            var ruleBtn = new Button
            {
                ToolTip = "Ligne horizontale",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("horizontal-rule", 14, Chrome.Ink)
            };
            ruleBtn.Click += delegate { InsertRule(); };
            restBottom.Children.Add(ruleBtn);

            var separatorBtn = new Button
            {
                ToolTip = "Séparateur de scène",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("scene-ellipsis", 14, Chrome.Ink)
            };
            separatorBtn.Click += delegate { InsertSeparator(); };
            restBottom.Children.Add(separatorBtn);
            // Le point médian (22/09) : un bouton à côté du séparateur, et le
            // raccourci « :: » de la typographie à la frappe.
            var middleDotBtn = new Button
            {
                ToolTip = "Point médian (raccourci : « :: »)",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("point-median", 14, Chrome.Ink)
            };
            middleDotBtn.Click += delegate
            {
                if (ComposedActive) { _composed.TypeText("·"); _composed.FocusSurface(); }
            };
            restBottom.Children.Add(middleDotBtn);

            panel.Children.Add(VerticalRuleTall());
            // Les caractères d'impression : un grand carré (13/09), puis
            // l'approche dans sa propre section, centrée verticalement.
            _marksBtn = BigSquareToggle("paragraph", "Caractères d'impression", "Caractères d'impression");
            _marksBtn.Click += delegate
            {
                var handler = MarksToggled;
                if (handler != null) handler(_marksBtn.IsChecked == true);
            };
            panel.Children.Add(_marksBtn);
            panel.Children.Add(VerticalRuleTall());
            var rest = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(rest);

            // Approche (tracking, millièmes de cadratin) — champ de valeur à
            // la Adobe : petits boutons ± verticaux à gauche, valeur absolue
            // lisible et retouchable. Rendue par le compositeur (Composition,
            // aperçu, PDF). Plus de libellé « Approche » (b43) : l'icône et
            // son infobulle suffisent.
            var kerningIcon = Icons.Make("kerning", 13, Chrome.SoftText) as FrameworkElement;
            if (kerningIcon != null)
            {
                kerningIcon.VerticalAlignment = VerticalAlignment.Center;
                kerningIcon.Margin = new Thickness(2, 0, 4, 0);
                kerningIcon.ToolTip = "Approche";
                rest.Children.Add(kerningIcon);
            }
            var spinner = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var trackUp = new RepeatButton
            {
                Content = "▲",
                FontSize = 6,
                Width = 15,
                Height = 11,
                Padding = new Thickness(0, -1, 0, 0),
                Focusable = false,
                ToolTip = "+5"
            };
            trackUp.Click += delegate { ApplyTrackingStep(5); };
            var trackDown = new RepeatButton
            {
                Content = "▼",
                FontSize = 6,
                Width = 15,
                Height = 11,
                Padding = new Thickness(0, -1, 0, 0),
                Focusable = false,
                ToolTip = "−5"
            };
            trackDown.Click += delegate { ApplyTrackingStep(-5); };
            spinner.Children.Add(trackUp);
            spinner.Children.Add(trackDown);
            rest.Children.Add(spinner);
            _trackingBox = new TextBox
            {
                Width = 42,
                Margin = new Thickness(1, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Text = "0",
                ToolTip = "Approche"
            };
            _trackingBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                double value;
                if (double.TryParse(_trackingBox.Text.Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value))
                    ApplyTrackingAbsolute(value);
            };
            rest.Children.Add(_trackingBox);

            // Ribbon: « Texte » (this panel) + « Mise en page » (page setup).
            // Gabarit RibbonTabs (17/09) : l'axe d'affichage vit dans la rangée
            // des chips (Tag), le contenu de l'onglet prend toute la largeur —
            // fini la réserve de 236 px qui rognait les sections de droite.
            var tabs = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };
            tabs.SetResourceReference(StyleProperty, "RibbonTabs");
            tabs.Items.Add(new TabItem { Header = "Texte", Content = panel });
            tabs.Items.Add(new TabItem { Header = "Insertion", Content = BuildInsertTab() });
            tabs.Items.Add(new TabItem { Header = "Formatage", Content = BuildFormatTab() });
            tabs.Items.Add(new TabItem { Header = "Mise en page", Content = BuildPageSetupTab() });
            tabs.Items.Add(new TabItem { Header = "Gabarit", Content = BuildDecorTab() });
            tabs.Items.Add(new TabItem { Header = "Composition", Content = BuildCompositionTab() });
            tabs.Items.Add(new TabItem { Header = "Révision", Content = BuildRevisionTab() });
            tabs.Items.Add(new TabItem { Header = "Correction", Content = BuildCorrectionTab() });

            // L'AXE D'AFFICHAGE (batch 26), collé au bord droit des onglets :
            // comment on VOIT la page — Pages (marges, folios, gabarits),
            // Brouillon (colonne continue sans décor), Calme (rien que le
            // texte). Même moteur, même pivot dessous — jamais un moteur.
            var views = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 6, 0)
            };
            // « Plan » (batch 35) : visible quand une colonne d'un plan est
            // reliée à l'écrit — renvoie au plan.
            _planBtn = new Button
            {
                Content = Icons.Label("arrow-up-right-bold", "Plan", 10, Chrome.Ink),
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(0, 0, 10, 0),
                FontSize = 11,
                Focusable = false,
                Visibility = Visibility.Collapsed,
                ToolTip = "Plan"
            };
            _planBtn.Click += delegate
            {
                var plan = _item == null || PlanLocator == null ? null : PlanLocator(_item);
                var handler = PlanRequested;
                if (plan != null && handler != null) handler(plan);
            };
            views.Children.Add(_planBtn);
            _pagesViewBtn = ViewToggle("Pages", "Pages");
            _pagesViewBtn.Click += delegate { SetDraftView(false); };
            views.Children.Add(_pagesViewBtn);
            _draftViewBtn = ViewToggle("Brouillon", "Brouillon");
            _draftViewBtn.Click += delegate { SetDraftView(true); };
            views.Children.Add(_draftViewBtn);
            _calmViewBtn = ViewToggle("Calme", "Calme");
            _calmViewBtn.Click += delegate
            {
                UpdateViewButtons(); // l'état réel suivra SetCalm
                var handler = CalmRequested;
                if (handler != null) handler();
            };
            views.Children.Add(_calmViewBtn);
            UpdateViewButtons();

            tabs.Tag = views; // rendu par le gabarit, à droite des chips
            // Ruban étroit (17/09) : sous 520 px, l'axe d'affichage passe en
            // icônes seules pour laisser la rangée des chips respirer.
            var compactViews = false;
            bar.SizeChanged += delegate
            {
                var compact = bar.ActualWidth > 0 && bar.ActualWidth < 520;
                if (compact == compactViews) return;
                compactViews = compact;
                CompactViewToggle(_pagesViewBtn, "files-bold", "Pages", compact);
                CompactViewToggle(_draftViewBtn, "article-bold", "Brouillon", compact);
                CompactViewToggle(_calmViewBtn, "book-open-text-bold", "Calme", compact);
            };
            bar.Child = tabs;
            _ribbonBar = bar;
            Children.Add(bar);
        }

        private ToggleButton ViewToggle(string label, string tooltip)
        {
            var button = Buttons.TextToggle(label, tooltip, Buttons.Compact);
            button.Margin = new Thickness(4, 0, 0, 0);
            return button;
        }

        /// <summary>Bascule un bouton de l'axe d'affichage entre son libellé
        /// et une icône seule (ruban étroit) ; l'état d'origine est gardé
        /// dans Tag pour le retour.</summary>
        private static void CompactViewToggle(ToggleButton button, string icon, string label, bool compact)
        {
            if (button == null) return;
            var saved = button.Tag as object[];
            if (compact)
            {
                if (saved == null)
                    button.Tag = saved = new[] { button.Content, button.ToolTip, button.Padding };
                button.Content = Icons.Make(icon, 14, Chrome.Ink);
                button.ToolTip = label;
                button.Padding = new Thickness(7, 0, 7, 0);
            }
            else if (saved != null)
            {
                button.Content = saved[0];
                button.ToolTip = saved[1];
                button.Padding = (Thickness)saved[2];
                button.Tag = null;
            }
        }

        /// <summary>L'état du sélecteur d'affichage — une seule position
        /// enfoncée.</summary>
        private void UpdateViewButtons()
        {
            if (_pagesViewBtn == null) return;
            _pagesViewBtn.IsChecked = !_calm && !_draftView;
            _draftViewBtn.IsChecked = !_calm && _draftView;
            _calmViewBtn.IsChecked = _calm;
        }

        /// <summary>Bascule Pages ↔ Brouillon : ré-attache la surface composée
        /// avec le réglage de page dérivé. Persistant (réglage d'application).</summary>
        private void SetDraftView(bool draft)
        {
            _draftView = draft;
            Settings.AppSettings.DraftView = draft;
            Settings.AppSettings.Save();
            UpdateViewButtons();
            AttachComposed();
        }

        /// <summary>Le réglage de page du BROUILLON : même moteur, même pivot —
        /// colonne continue à mesure confortable, sans marges apparentes,
        /// numéros de ligne ni folio. Les « pages » gardent le RATIO A4
        /// (1 : √2) quel que soit le format du document (batch 34) — une
        /// tranche de 165 × 233 mm. Ce n'est PAS un troisième chemin de
        /// composition, juste un PageSetup dérivé (doctrine du lot B.2).</summary>
        private static PageSetup DraftSetup(PageSetup source)
        {
            var draft = source.Clone();
            draft.PageWidthMm = 165;
            draft.PageHeightMm = Math.Round(165 * 297.0 / 210.0, 1); // 233,4 : ratio A4
            draft.MarginTopMm = 10;
            draft.MarginBottomMm = 10;
            draft.MarginLeftMm = 18;
            draft.MarginRightMm = 18;
            draft.Columns = 1;
            draft.ShowMarginGuides = false;
            draft.LineNumbers = false;
            draft.FooterPageNumbers = false;
            return draft;
        }

        /// <summary>Mode calme : le ruban s'efface, la coquille masque le
        /// reste (Pile, inspecteur, menus, barre d'état).</summary>
        public void SetCalm(bool calm)
        {
            _calm = calm;
            _ribbonBar.Visibility = calm ? Visibility.Collapsed : Visibility.Visible;
            if (calm) _searchBar.Visibility = Visibility.Collapsed;
            // La feuille du calme (13/09) : A4 nue, ombre réelle, air en tête,
            // pas de marqueurs veuves/orphelines — et retour à l'axe choisi
            // (Pages ou Brouillon) en sortant. Même moteur, réglage dérivé.
            ComposedRenderer.ShowWidowMarks = !calm;
            _composed.CalmLook = calm;
            AttachComposed();
            UpdateRulers();      // les règles s'effacent en calme
            UpdateViewButtons(); // le sélecteur d'affichage suit
            RebuildCorrectionPanel();
            RebuildAnnotationsPanel();
        }

        /// <summary>Le réglage de page du MODE CALME (13/09) : une feuille A4
        /// à marges régulières, sans guides, numéros de ligne ni folio —
        /// toujours la même, quel que soit le format du document.</summary>
        private static PageSetup CalmSetup(PageSetup source)
        {
            var calm = source.Clone();
            calm.PageWidthMm = 210;
            calm.PageHeightMm = 297;
            calm.MarginTopMm = 22;
            calm.MarginBottomMm = 22;
            calm.MarginLeftMm = 25;
            calm.MarginRightMm = 25;
            calm.Columns = 1;
            calm.ShowMarginGuides = false;
            calm.LineNumbers = false;
            calm.FooterPageNumbers = false;
            return calm;
        }

        // ============================================================= « Gabarit » tab

        /// <summary>Onglet « Gabarit » (13/09) : en-tête et pied de page en
        /// deux boutons d'une ligne superposés, même largeur ; la note sur
        /// les gabarits de livre dans sa propre section, repliée.</summary>
        private UIElement BuildDecorTab()
        {
            var panel = TabPanel();
            var header = OneLine("sort-descending-bold", "Éditer l'en-tête…", "Éditer l'en-tête");
            header.Click += delegate { EditHeaderFooter(true); };
            var footer = OneLine("sort-ascending-bold", "Éditer le pied de page…", "Éditer le pied de page");
            footer.Click += delegate { EditHeaderFooter(false); };
            panel.Children.Add(Stacked(header, footer));
            panel.Children.Add(VerticalRuleTall());
            panel.Children.Add(new TextBlock
            {
                Text = "Un gabarit de pages appliqué au document (livres) remplace ces réglages.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 210,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2, 3, 0, 0)
            });
            return panel;
        }

        /// <summary>Approche sur la sélection (le réglage s'applique au pivot
        /// et se voit à l'écran, à l'aperçu et au PDF).</summary>
        private void ApplyTrackingStep(double delta)
        {
            if (_item == null || !ComposedActive) return;
            _composed.ApplyTracking(delta);
            SyncTrackingBox();
            _composed.FocusSurface();
        }

        private void ApplyTrackingAbsolute(double value)
        {
            if (_item == null || !ComposedActive) return;
            _composed.SetTracking(value);
            SyncTrackingBox();
            _composed.FocusSurface();
        }

        /// <summary>Combos et bascules du ruban : style, police, taille et
        /// formats au caret de la surface composée.</summary>
        private void SyncToolbarComposed()
        {
            if (!ComposedActive || _item == null || _styleCombo == null) return;
            _syncing = true;
            try
            {
                string styleId, fontFamily;
                double sizePt;
                _composed.CaretFormat(out styleId, out fontFamily, out sizePt);
                ComboBoxItem match = null;
                foreach (ComboBoxItem candidate in _styleCombo.Items)
                    if ((string)candidate.Tag == styleId) { match = candidate; break; }
                _styleCombo.SelectedItem = match;
                // Le suffixe « + » (22/09) : le paragraphe s'écarte de son style
                // (alignement, décalage, alinéa posés à la main) — resélectionner
                // le style efface ces écarts.
                var overridden = _composed.CaretHasOverrides();
                foreach (ComboBoxItem candidate in _styleCombo.Items)
                {
                    var label = StyleLabelOf(candidate);
                    var style = _styles.Find((string)candidate.Tag);
                    if (label != null && style != null) label.Text = style.Name + (candidate == match && overridden ? " +" : "");
                }
                // L'état de la SÉLECTION à trois valeurs (0.50.0) : une police
                // ou une taille qui se mélangent vident leur combo, une
                // bascule qui se mélange reste décochée mais porte un point
                // jaune en haut à droite (« entre-deux »).
                bool? boldState, italicState, underlineState, strikeState;
                string selectionFont;
                double? selectionSize;
                bool mixedFont, mixedSize;
                _composed.SelectionFormatState(out boldState, out italicState, out underlineState, out strikeState,
                    out selectionFont, out selectionSize, out mixedFont, out mixedSize);
                _lastAppliedFont = null; // le caret a bougé : le prochain choix s'applique
                if (mixedFont) _fontCombo.ShowMixed();
                else _fontCombo.Select(selectionFont ?? fontFamily);
                if (mixedSize)
                {
                    _sizeCombo.SelectedItem = null;
                    _sizeCombo.Text = "";
                }
                else
                {
                    var shownPt = selectionSize ?? sizePt;
                    _sizeCombo.SelectedItem = (int)Math.Round(shownPt);
                    if (_sizeCombo.SelectedItem == null)
                        _sizeCombo.Text = shownPt.ToString("0.#",
                            System.Globalization.CultureInfo.CurrentCulture);
                }

                // Les BASCULES aussi (gras/italique/…, alignements exclusifs,
                // listes) — sans cette synchro, un ToggleButton cliqué gardait
                // son état à lui (centré ET justifié actifs à la fois).
                bool bold, italic, underline, strike;
                string align, listKind;
                _composed.SelectionFlags(out bold, out italic, out underline,
                    out strike, out align, out listKind);
                SetToggleState(_boldBtn, boldState);
                SetToggleState(_italicBtn, italicState);
                SetToggleState(_underBtn, underlineState);
                SetToggleState(_strikeBtn, strikeState);
                if (_smallCapsBtn != null) SetToggleState(_smallCapsBtn, _composed.SmallCapsState());
                _alignLeft.IsChecked = align == "left";
                _alignCenter.IsChecked = align == "center";
                _alignRight.IsChecked = align == "right";
                _alignJustify.IsChecked = align == "justify";
                _bulletBtn.IsChecked = listKind == "bullet";
                _numberBtn.IsChecked = listKind == "number";
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>Reflète l'approche de la sélection dans le champ (vide =
        /// valeurs mixtes).</summary>
        private void SyncTrackingBox()
        {
            if (_trackingBox == null || _trackingBox.IsKeyboardFocused) return;
            if (!ComposedActive || _item == null) { _trackingBox.Text = "0"; return; }
            var value = _composed.SelectionTracking();
            _trackingBox.Text = value.HasValue
                ? value.Value.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)
                : "";
        }

        private void EditHeaderFooter(bool isHeader)
        {
            if (_item == null) return;
            var edited = HeaderFooterDialog.Edit(Window.GetWindow(this),
                isHeader ? _item.Header : _item.Footer, isHeader);
            if (edited == null) return; // annulé
            if (isHeader) _item.Header = edited.IsEmpty ? null : edited;
            else _item.Footer = edited.IsEmpty ? null : edited;
            Decor = PageDecor.For(_item, _project);
            if (ComposedActive)
            {
                _composed.Decor = Decor;
                _composed.RefreshComposition();
            }
            NotifyEdited();
        }

        // ============================================================= « Composition » tab

        public event Action PreviewRequested;  // Aperçu des pages
        public event Action PrintRequested;    // Imprimer / PDF
        public event Action ExportRequested;   // Exporter l'écrit
        public event Action PdfRequested;      // PDF prêt à imprimer (4b-2)

        /// <summary>Onglet « Composition » (13/09) : l'aperçu en grand carré
        /// dans sa section ; Imprimer et PDF prêt à imprimer superposés ;
        /// l'export. « Compiler les écrits » a quitté le ruban (menu
        /// Fichier, Ctrl+Maj+E).</summary>
        private UIElement BuildCompositionTab()
        {
            var panel = TabPanel();
            var preview = BigSquare("book-open-text-bold", "Aperçu des pages", "Aperçu des pages");
            preview.Click += delegate { var handler = PreviewRequested; if (handler != null) handler(); };
            panel.Children.Add(preview);
            panel.Children.Add(VerticalRuleTall());
            var print = OneLine("printer-bold", "Imprimer", "Imprimer");
            print.Click += delegate { var handler = PrintRequested; if (handler != null) handler(); };
            var pdf = OneLine("document-file", "PDF prêt à imprimer…", "PDF prêt à imprimer");
            pdf.Click += delegate { var handler = PdfRequested; if (handler != null) handler(); };
            panel.Children.Add(Stacked(print, pdf));
            panel.Children.Add(VerticalRuleTall());
            var export = OneLine("file-arrow-down-bold", "Exporter l'écrit…", "Exporter l'écrit");
            export.Click += delegate { var handler = ExportRequested; if (handler != null) handler(); };
            panel.Children.Add(export);
            return panel;
        }

        // ============================================================= « Mise en page » tab

        private ComboBox _marginsCombo, _sizeComboPage, _columnsCombo;
        private ToggleButton _guidesBtn, _lineNumbersBtn, _hyphenBtn, _folioBtn;
        private ToggleButton _marksBtn;
        private bool _syncingPage;
        private ComboBox _leadingCombo; // l'interligne du document (22/09)
        private static readonly double[] LeadingFactors = { 1, 1.25, 1.5, 1.75, 2 };

        /// <summary>Un réglage du document (l'interligne) a changé : le projet est modifié.</summary>
        public event Action DocumentSettingChanged;

        /// <summary>Onglet « Mise en page » (13/09) : marges, taille et
        /// colonnes en haut à gauche ; guides et numéros de ligne superposés ;
        /// césure et folio superposés. Le saut de page vit dans Insertion.</summary>
        private UIElement BuildPageSetupTab()
        {
            var panel = TabPanel();
            var setup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top
            };
            panel.Children.Add(setup);

            // Marges et Taille superposés (13/09) : une grille de deux
            // rangées, libellés alignés, listes de même largeur.
            var pageGrid = new Grid { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0) };
            pageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pageGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Buttons.Compact) });
            pageGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Buttons.Compact + 2) });
            setup.Children.Add(pageGrid);

            var marginsLabel = PageLabel("Marges");
            pageGrid.Children.Add(marginsLabel);
            _marginsCombo = new ComboBox
            {
                Width = 150,
                Height = Buttons.Compact,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Marges"
            };
            Grid.SetColumn(_marginsCombo, 1);
            _marginsCombo.Items.Add("Livre (20/20/30/20)");
            _marginsCombo.Items.Add("Uniformes (25 mm)");
            _marginsCombo.Items.Add("Étroites (12,7 mm)");
            _marginsCombo.Items.Add("Personnalisées…");
            _marginsCombo.SelectionChanged += OnMarginsComboChanged;
            pageGrid.Children.Add(_marginsCombo);

            var sizeLabel = PageLabel("Taille");
            sizeLabel.Margin = new Thickness(0, 2, 0, 0);
            Grid.SetRow(sizeLabel, 1);
            pageGrid.Children.Add(sizeLabel);
            _sizeComboPage = new ComboBox
            {
                Width = 150,
                Height = Buttons.Compact,
                Margin = new Thickness(4, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(_sizeComboPage, 1);
            Grid.SetColumn(_sizeComboPage, 1);
            _sizeComboPage.Items.Add("A4 (21 × 29,7 cm)");
            _sizeComboPage.Items.Add("A5 (14,8 × 21 cm)");
            _sizeComboPage.Items.Add("Letter (21,6 × 27,9 cm)");
            _sizeComboPage.Items.Add("Livre (14 × 21,6 cm)");
            _sizeComboPage.Items.Add("Personnalisée…");
            _sizeComboPage.SelectionChanged += OnPageSizeComboChanged;
            pageGrid.Children.Add(_sizeComboPage);

            var columnsIcon = Icons.Make("text-columns-bold", 14, Chrome.SoftText) as FrameworkElement;
            if (columnsIcon != null)
            {
                columnsIcon.VerticalAlignment = VerticalAlignment.Top;
                columnsIcon.Margin = new Thickness(0, 6, 0, 0);
                columnsIcon.ToolTip = "Colonnes";
                setup.Children.Add(columnsIcon);
            }
            _columnsCombo = new ComboBox
            {
                Width = 46,
                Height = Buttons.Compact,
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = "Colonnes"
            };
            _columnsCombo.Items.Add(1);
            _columnsCombo.Items.Add(2);
            _columnsCombo.Items.Add(3);
            _columnsCombo.SelectionChanged += delegate
            {
                if (_syncingPage || _project == null || _columnsCombo.SelectedItem == null) return;
                _pageSetup.Columns = (int)_columnsCombo.SelectedItem;
                AfterPageSetupEdit();
            };
            setup.Children.Add(_columnsCombo);

            panel.Children.Add(VerticalRuleTall());
            _guidesBtn = OneLineToggle("margins", "Marges", "Marges");
            _guidesBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.ShowMarginGuides = _guidesBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            _lineNumbersBtn = OneLineToggle("list-numbers-bold", "Numéros de ligne", "Numéros de ligne");
            _lineNumbersBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.LineNumbers = _lineNumbersBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(Stacked(_guidesBtn, _lineNumbersBtn));

            panel.Children.Add(VerticalRuleTall());
            _hyphenBtn = OneLineToggle("minus", "Césure", "Césure");
            _hyphenBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.Hyphenation = _hyphenBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            _folioBtn = OneLineToggle("numbered", "Folio", "Folio");
            _folioBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.FooterPageNumbers = _folioBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(Stacked(_hyphenBtn, _folioBtn));
            return panel;
        }

        private TextBlock PageLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void OnMarginsComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingPage || _project == null || _marginsCombo.SelectedIndex < 0) return;
            var page = _pageSetup;
            if (_marginsCombo.SelectedIndex == 0) SetMarginsMm(page, 20, 20, 30, 20);
            else if (_marginsCombo.SelectedIndex == 1) SetMarginsMm(page, 25, 25, 25, 25);
            else if (_marginsCombo.SelectedIndex == 2) SetMarginsMm(page, 12.7, 12.7, 12.7, 12.7);
            else
            {
                // En millimètres (22/09), comme partout où l'on parle de marges.
                var values = NumbersDialog.Ask(Window.GetWindow(this), "Marges (mm)",
                    new[]
                    {
                        "De tête (marge haute)",
                        "De pied (marge basse)",
                        "Petit fond (côté reliure)",
                        "Grand fond (côté extérieur)"
                    },
                    new[] { page.MarginTopMm, page.MarginBottomMm, page.MarginLeftMm, page.MarginRightMm },
                    5, 100);
                if (values == null) { SyncPageTab(); return; }
                SetMarginsMm(page, values[0], values[1], values[2], values[3]);
            }
            AfterPageSetupEdit();
        }

        private static void SetMarginsMm(PageSetup page, double top, double bottom, double left, double right)
        {
            page.MarginTopMm = top;
            page.MarginBottomMm = bottom;
            page.MarginLeftMm = left;
            page.MarginRightMm = right;
        }

        private void OnPageSizeComboChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingPage || _project == null || _sizeComboPage.SelectedIndex < 0) return;
            var page = _pageSetup;
            if (_sizeComboPage.SelectedIndex == 0) { page.PageWidthMm = 210; page.PageHeightMm = 297; }
            else if (_sizeComboPage.SelectedIndex == 1) { page.PageWidthMm = 148; page.PageHeightMm = 210; }
            else if (_sizeComboPage.SelectedIndex == 2) { page.PageWidthMm = 216; page.PageHeightMm = 279; }
            else if (_sizeComboPage.SelectedIndex == 3) { page.PageWidthMm = 140; page.PageHeightMm = 216; }
            else
            {
                var values = NumbersDialog.Ask(Window.GetWindow(this), "Taille de page (cm)",
                    new[] { "Largeur", "Hauteur" },
                    new[] { page.PageWidthMm / 10, page.PageHeightMm / 10 }, 5, 100);
                if (values == null) { SyncPageTab(); return; }
                page.PageWidthMm = values[0] * 10;
                page.PageHeightMm = values[1] * 10;
            }
            AfterPageSetupEdit();
        }

        private void AfterPageSetupEdit()
        {
            SyncPageTab();
            if (ComposedActive) _composed.RefreshComposition();
            var handler = PageSetupChanged;
            if (handler != null) handler();
        }


        // Boutons du ruban à icône seule : CARRÉS (26 × 26, b43).
        private static Button IconButton(string iconName, string tooltip)
        {
            return new Button
            {
                Content = Icons.Make(iconName, 14, Chrome.Ink),
                ToolTip = tooltip,
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
        }

        private ToggleButton IconToggle(string iconName, string tooltip)
        {
            return new ToggleButton
            {
                Content = Icons.Make(iconName, 14, Chrome.Ink),
                ToolTip = tooltip,
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
        }

        private ToggleButton ListToggle(string label, string tooltip)
        {
            return new ToggleButton
            {
                Content = new TextBlock { Text = label, FontSize = 13 },
                ToolTip = tooltip,
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
        }

        private ToggleButton FormatToggle(string label, string tooltip,
            bool bold, bool italic, bool underline, bool strike)
        {
            // Icônes vectorielles embarquées (jeu Phosphor).
            var icon = bold ? "text-b-bold"
                     : italic ? "text-italic-bold"
                     : underline ? "text-underline-bold"
                     : "text-strikethrough-bold";
            var toggle = new ToggleButton
            {
                ToolTip = tooltip,
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
            DressToggle(toggle, icon, 14);
            return toggle;
        }

        // Le POINT JAUNE des bascules (0.50.0) : visible quand la sélection
        // mélange l'attribut (gras ici, pas là) — le bouton reste décoché.
        private readonly Dictionary<ToggleButton, System.Windows.Shapes.Ellipse> _mixedDots
            = new Dictionary<ToggleButton, System.Windows.Shapes.Ellipse>();

        /// <summary>Le contenu d'une bascule de format : l'icône, et le point
        /// jaune en haut à droite, caché tant que l'état n'est pas mixte.</summary>
        private void DressToggle(ToggleButton toggle, string icon, double size)
        {
            var grid = new Grid();
            grid.Children.Add(Icons.Make(icon, size, Chrome.Ink));
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(Color.FromRgb(0xF2, 0xC2, 0x1B)),
                Stroke = Chrome.PaperBg,
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -3, -3, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            grid.Children.Add(dot);
            toggle.Content = grid;
            _mixedDots[toggle] = dot;
        }

        /// <summary>Coche pour « tout », décoche pour « rien », décoche ET
        /// allume le point pour « mixte » (null).</summary>
        private void SetToggleState(ToggleButton toggle, bool? state)
        {
            toggle.IsChecked = state == true;
            System.Windows.Shapes.Ellipse dot;
            if (_mixedDots.TryGetValue(toggle, out dot))
                dot.Visibility = state.HasValue ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Only the weights the current font really ships, with a
        /// check mark on the (uniform) weight of the selection.</summary>
        private void BuildWeightMenu(ContextMenu menu)
        {
            menu.Items.Clear();
            var family = CurrentFontFamily();
            var available = AvailableWeights(family);
            var current = CurrentWeightName(); // null = Normal, "mixed" = pas de coche

            string[] labels = { "Fin", "Normal", "Moyen", "Demi-gras", "Gras", "Noir" };
            string[] weights = { "Light", null, "Medium", "SemiBold", "Bold", "Black" };
            for (var i = 0; i < labels.Length; i++)
            {
                // Normal et Gras existent toujours (WPF les synthétise au besoin).
                var weight = weights[i];
                if (weight != null && weight != "Bold" && !available.Contains(weight)) continue;
                var entry = new MenuItem
                {
                    Header = labels[i],
                    FontWeight = weight == null ? FontWeights.Normal : FlowConverter.ParseWeight(weight),
                    IsChecked = current != "mixed"
                        && ((weight == null && current == null) || weight == current)
                };
                var weightRef = weight;
                entry.Click += delegate { ApplyWeight(weightRef); };
                menu.Items.Add(entry);
            }
        }

        private static HashSet<string> AvailableWeights(string family)
        {
            var set = new HashSet<string>();
            try
            {
                foreach (var typeface in new FontFamily(family).FamilyTypefaces)
                {
                    var name = FlowConverter.WeightName(typeface.Weight);
                    if (name != null) set.Add(name);
                }
            }
            catch { }
            return set;
        }

        private string CurrentFontFamily()
        {
            if (ComposedActive)
            {
                var family = _composed.GetCaretFontFamily();
                if (family != null) return family;
            }
            return _styles.Body.FontFamily;
        }

        private string CurrentWeightName()
        {
            return ComposedActive ? _composed.GetSelectionWeightName() : "mixed";
        }

        private void ApplyWeight(string weight)
        {
            if (_item == null || !ComposedActive) return;
            _composed.ApplyWeight(weight);
            _composed.FocusSurface();
        }

        private ToggleButton AlignToggle(string align, string tooltip)
        {
            var button = new ToggleButton
            {
                Content = Icons.Make("text-align-" + align + "-bold", 14, Chrome.Ink),
                ToolTip = tooltip,
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Tag = align
            };
            button.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyAlign(align); _composed.FocusSurface(); }
            };
            return button;
        }

        /// <summary>Tiny alignment icon: four bars whose widths sketch the mode.</summary>
        private UIElement AlignIcon(string align)
        {
            var panel = new StackPanel { Width = 14 };
            var widths = align == "center" ? new double[] { 14, 10, 14, 8 }
                       : align == "right" ? new double[] { 14, 10, 14, 8 }
                       : align == "justify" ? new double[] { 14, 14, 14, 14 }
                       : new double[] { 14, 10, 14, 8 };
            var alignment = align == "center" ? HorizontalAlignment.Center
                          : align == "right" ? HorizontalAlignment.Right
                          : HorizontalAlignment.Left;
            foreach (var width in widths)
                panel.Children.Add(new Border
                {
                    Height = 2,
                    Width = width,
                    Background = Chrome.Ink,
                    Margin = new Thickness(0, 1, 0, 1),
                    HorizontalAlignment = alignment
                });
            return panel;
        }

        private static Border VerticalRule()
        {
            return new Border
            {
                Width = 1,
                Background = Chrome.Border,
                Margin = new Thickness(7, 2, 7, 2)
            };
        }

        // --------------------------------------- le ruban à deux lignes (b28)

        /// <summary>Une rangée d'un bloc empilé du ruban.</summary>
        private static StackPanel RibbonRow()
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1, 0, 1)
            };
        }

        /// <summary>LA hauteur du contenu des onglets (13/09) : tous les
        /// onglets la partagent, le ruban ne saute plus d'un onglet à
        /// l'autre ; les grands carrés la prennent entière, deux boutons
        /// d'une ligne (26 px) s'y superposent.</summary>
        public const double RibbonHeight = 56;

        /// <summary>Le panneau d'un onglet (17/09) : une ligne de RibbonHeight
        /// par défaut ; fenêtre étroite, les sections (entre deux séparateurs)
        /// descendent d'une ligne au lieu d'être rognées — voir RibbonPanel.</summary>
        private static RibbonPanel TabPanel()
        {
            return new RibbonPanel
            {
                Margin = new Thickness(8, 3, 8, 3),
                LineHeight = RibbonHeight
            };
        }

        /// <summary>Séparateur vertical courant sur toute la hauteur — et
        /// frontière de section pour le repli du RibbonPanel.</summary>
        private static Border VerticalRuleTall()
        {
            return new Border
            {
                Width = 1,
                Height = RibbonHeight - 6,
                Background = Chrome.Border,
                Margin = new Thickness(7, 3, 7, 3),
                VerticalAlignment = VerticalAlignment.Top,
                Tag = RibbonPanel.SeparatorTag
            };
        }

        /// <summary>Un bouton d'UNE ligne du ruban (13/09) : icône + libellé,
        /// 26 px, fond papier et contour (un bouton se distingue d'un
        /// glyphe), aligné en haut à gauche de sa section.</summary>
        private static Button OneLine(string icon, string label, string tooltip)
        {
            var button = Buttons.IconText(icon, label, tooltip, Buttons.Compact, Buttons.Look.Outline);
            button.Margin = new Thickness(0, 0, 4, 0);
            button.VerticalAlignment = VerticalAlignment.Top;
            button.HorizontalContentAlignment = HorizontalAlignment.Left; // icône + texte à gauche, même dans une pile
            return button;
        }

        private static ToggleButton OneLineToggle(string icon, string label, string tooltip)
        {
            var button = Buttons.IconTextToggle(icon, label, tooltip, Buttons.Compact, Buttons.Look.Outline);
            button.Margin = new Thickness(0, 0, 4, 0);
            button.VerticalAlignment = VerticalAlignment.Top;
            button.HorizontalContentAlignment = HorizontalAlignment.Left; // icône + texte à gauche, même dans une pile
            return button;
        }

        private static ToggleButton OneLineTextToggle(string label, string tooltip)
        {
            var button = Buttons.TextToggle(label, tooltip, Buttons.Compact, Buttons.Look.Outline);
            button.Margin = new Thickness(0, 0, 4, 0);
            button.VerticalAlignment = VerticalAlignment.Top;
            button.HorizontalContentAlignment = HorizontalAlignment.Left; // icône + texte à gauche, même dans une pile
            return button;
        }

        /// <summary>Deux boutons d'une ligne superposés, même largeur (la
        /// pile verticale étire au plus large), collés en haut.</summary>
        private static StackPanel Stacked(FrameworkElement top, FrameworkElement bottom)
        {
            top.Margin = new Thickness(0, 0, 4, 2);
            bottom.Margin = new Thickness(0, 0, 4, 0);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            stack.Children.Add(top);
            stack.Children.Add(bottom);
            return stack;
        }

        /// <summary>Le grand carré du ruban : toute la hauteur.</summary>
        private static Button BigSquare(string icon, string label, string tooltip)
        {
            var button = Buttons.Big(icon, label, tooltip, RibbonHeight, Buttons.Look.Outline);
            button.Margin = new Thickness(0, 0, 4, 0);
            return button;
        }

        private static ToggleButton BigSquareToggle(string icon, string label, string tooltip)
        {
            var button = Buttons.BigToggle(icon, label, tooltip, RibbonHeight, Buttons.Look.Outline);
            button.Margin = new Thickness(0, 0, 4, 0);
            return button;
        }

        private Button PaletteButton(string tooltip, bool isForeground)
        {
            var button = new Button
            {
                ToolTip = tooltip,
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make(isForeground ? "palette" : "highlighter-line", 14, Chrome.Accent)
            };
            var menu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = button };
            button.ContextMenu = menu;
            button.Click += delegate
            {
                BuildPaletteMenu(menu, isForeground); // custom colors live per project
                menu.IsOpen = true;
            };
            return button;
        }

        /// <summary>Rebuilt at each opening: the project's custom colors first,
        /// then « Nouvelle couleur… », then the standard palette.</summary>
        private void BuildPaletteMenu(ContextMenu menu, bool isForeground)
        {
            menu.Items.Clear();
            AddColorEntry(menu, isForeground ? "Automatique" : "Aucun", null, isForeground);

            if (_project != null && _project.CustomColors.Count > 0)
            {
                menu.Items.Add(new Separator());
                foreach (var hex in _project.CustomColors)
                    AddColorEntry(menu, hex, hex, isForeground);
            }
            var custom = new MenuItem { Header = "Nouvelle couleur…" };
            custom.Click += delegate
            {
                var hex = ColorDialog.Ask(Window.GetWindow(this));
                if (hex == null || _project == null) return;
                if (!_project.CustomColors.Contains(hex))
                {
                    _project.CustomColors.Insert(0, hex);
                    NotifyEdited(); // le projet a changé
                }
                ApplyPaletteColor(hex, isForeground);
            };
            menu.Items.Add(custom);
            menu.Items.Add(new Separator());

            var standard = isForeground
                ? new[] { "#C0392B", "#E67E22", "#C9A227", "#27AE60",
                    "#16A085", "#2980B9", "#5B67D8", "#8E44AD", "#7F8C8D", "#703C2F" }
                : new[] { "#FFF3A3", "#FFD9A8", "#D3F8D3", "#D0E8FF",
                    "#FFD6E7", "#E5D4FF", "#E8E8E8" };
            foreach (var hex in standard)
                AddColorEntry(menu, hex, hex, isForeground);
        }

        private void ApplyPaletteColor(string hex, bool isForeground)
        {
            if (!ComposedActive) return;
            if (isForeground) _composed.ApplyColor(hex);
            else _composed.ApplyHighlight(hex);
            _composed.FocusSurface();
        }

        private void AddColorEntry(ContextMenu menu, string label, string hex, bool isForeground)
        {
            var entry = new MenuItem { Header = label };
            if (hex != null)
                entry.Icon = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(FlowConverter.ParseColor(hex)),
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1)
                };
            entry.Click += delegate { ApplyPaletteColor(hex, isForeground); };
            menu.Items.Add(entry);
        }

    }
}

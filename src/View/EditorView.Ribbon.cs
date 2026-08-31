using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UniversSale.Model;

namespace UniversSale.View
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
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 3, 8, 3),
                MinHeight = 52
            };
            var typeRows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var typeTop = RibbonRow();
            var typeBottom = RibbonRow();
            typeRows.Children.Add(typeTop);
            typeRows.Children.Add(typeBottom);

            _styleCombo = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 2, 0) };
            _styleCombo.SelectionChanged += OnStyleComboChanged;
            typeTop.Children.Add(_styleCombo);

            var manageStyles = new Button
            {
                Content = new TextBlock { Text = "Aa", FontSize = 12, FontWeight = FontWeights.SemiBold },
                ToolTip = "Gestion des styles…",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                Focusable = false
            };
            manageStyles.Click += delegate
            {
                var handler = StylesRequested;
                if (handler != null) handler();
            };
            typeTop.Children.Add(manageStyles);

            // Éditables : on peut TAPER un nom de police ou une taille
            // personnalisée (Entrée applique).
            _fontCombo = new ComboBox
            {
                Width = 140,
                Margin = new Thickness(0, 0, 6, 0),
                IsEditable = true,
                ToolTip = "Police — tapez un nom puis Entrée pour une police hors liste"
            };
            foreach (var family in ListFonts()) _fontCombo.Items.Add(family);
            _fontCombo.SelectionChanged += OnFontComboChanged;
            _fontCombo.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                ApplyTypedFont(_fontCombo.Text);
            };
            typeTop.Children.Add(_fontCombo);

            // Sizes are displayed in points (like every word processor);
            // internally everything stays WPF pixels (1 pt = 4/3 px).
            _sizeCombo = new ComboBox
            {
                Width = 52,
                Margin = new Thickness(0, 0, 10, 0),
                IsEditable = true,
                ToolTip = "Taille (points) — tapez une valeur libre puis Entrée"
            };
            foreach (var size in new[] { 8, 9, 10, 11, 12, 13, 14, 16, 18, 20, 22, 24, 28, 32, 36, 48, 72 })
                _sizeCombo.Items.Add(size);
            _sizeCombo.SelectionChanged += OnSizeComboChanged;
            _sizeCombo.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                ApplyTypedSize(_sizeCombo.Text);
            };
            typeTop.Children.Add(_sizeCombo);

            // Variantes de caractère (Fin, Normal, Moyen, Demi-gras, Gras, Noir).
            var weightBtn = new Button
            {
                ToolTip = "Variantes de caractère (graisse)",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("variante-caractere", 14, Chrome.Ink)
            };
            var weightMenu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = weightBtn };
            weightBtn.ContextMenu = weightMenu;
            weightBtn.Click += delegate
            {
                BuildWeightMenu(weightMenu); // graisses de LA police, coche incluse
                weightMenu.IsOpen = true;
            };
            typeTop.Children.Add(weightBtn);

            _boldBtn = FormatToggle("G", "Gras (Ctrl+B)", true, false, false, false);
            _boldBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleBold(); _composed.Focus(); return; }
                EditingCommands.ToggleBold.Execute(null, _box);
                AfterFormat();
            };
            _italicBtn = FormatToggle("I", "Italique (Ctrl+I)", false, true, false, false);
            _italicBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleItalic(); _composed.Focus(); return; }
                EditingCommands.ToggleItalic.Execute(null, _box);
                AfterFormat();
            };
            _underBtn = FormatToggle("S", "Souligné (Ctrl+U)", false, false, true, false);
            _underBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleUnderline(); _composed.Focus(); return; }
                ToggleDecoration(TextDecorationLocation.Underline);
            };
            _strikeBtn = FormatToggle("B", "Barré", false, false, false, true);
            _strikeBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ToggleStrike(); _composed.Focus(); return; }
                ToggleDecoration(TextDecorationLocation.Strikethrough);
            };
            // Icônes Flaticon (batch 28) — les lettres G/I/S/B laissent place
            // aux glyphes universels.
            _boldBtn.Content = Icons.Make("bold", 12, Chrome.Ink);
            _italicBtn.Content = Icons.Make("italic", 12, Chrome.Ink);
            _underBtn.Content = Icons.Make("underline", 12, Chrome.Ink);
            _strikeBtn.Content = Icons.Make("strikethrough", 12, Chrome.Ink);
            typeBottom.Children.Add(_boldBtn);
            typeBottom.Children.Add(_italicBtn);
            typeBottom.Children.Add(_underBtn);
            typeBottom.Children.Add(_strikeBtn);
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

            _bulletBtn = IconToggle("list", "Liste à puces");
            _bulletBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyList("bullet"); _composed.Focus(); return; }
                EditingCommands.ToggleBullets.Execute(null, _box);
                AfterFormat();
            };
            _numberBtn = IconToggle("list-numbers-bold", "Liste numérotée");
            _numberBtn.Click += delegate
            {
                if (ComposedActive) { _composed.ApplyList("number"); _composed.Focus(); return; }
                EditingCommands.ToggleNumbering.Execute(null, _box);
                AfterFormat();
            };
            _checkBtn = IconToggle("list-check", "Case à cocher (☐ → ☑ → retirer)");
            _checkBtn.Click += delegate
            {
                if (ComposedActive) { _composed.TypeText("☐ "); return; }
                ToggleChecklist();
            };
            alignBottom.Children.Add(_bulletBtn);
            alignBottom.Children.Add(_numberBtn);
            alignBottom.Children.Add(_checkBtn);
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
                ToolTip = "Séparateur de scène (texte et police : Fichier → Paramètres du projet)",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false,
                Content = Icons.Make("symbol", 14, Chrome.Ink)
            };
            separatorBtn.Click += delegate { InsertSeparator(); };
            restBottom.Children.Add(separatorBtn);

            panel.Children.Add(VerticalRuleTall());
            // ¶ et approche coulent sur une ligne, centrés verticalement.
            var rest = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(rest);

            _marksBtn = new ToggleButton
            {
                Content = Icons.Make("paragraph", 14, Chrome.Ink),
                ToolTip = "Afficher les caractères d'impression (¶ espaces · insécables ° tabulations →)",
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
            _marksBtn.Click += delegate
            {
                var handler = MarksToggled;
                if (handler != null) handler(_marksBtn.IsChecked == true);
            };
            rest.Children.Add(_marksBtn);

            // Approche (tracking, millièmes de cadratin) — champ de valeur à
            // la Adobe : petits boutons ± verticaux à gauche, valeur absolue
            // lisible et retouchable. Rendue par le compositeur (Composition,
            // aperçu, PDF). Plus de libellé « Approche » (b43) : l'icône et
            // son infobulle suffisent.
            rest.Children.Add(VerticalRule());
            var kerningIcon = Icons.Make("kerning", 13, Chrome.SoftText) as FrameworkElement;
            if (kerningIcon != null)
            {
                kerningIcon.VerticalAlignment = VerticalAlignment.Center;
                kerningIcon.Margin = new Thickness(2, 0, 4, 0);
                kerningIcon.ToolTip = "Approche : espacement entre les caractères, en millièmes "
                    + "de cadratin — valeur de la sélection, pas de 5 aux flèches";
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
                ToolTip = "Approche de la sélection (millièmes de cadratin, −100 à 400) — "
                    + "Entrée pour appliquer ; vide = mixte"
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
            var tabs = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                // Réserve à droite pour l'axe d'affichage (Pages/Brouillon/
                // Calme) : huit onglets ne passent plus dessous (batch 34).
                Margin = new Thickness(0, 0, 236, 0)
            };
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
                ToolTip = "Ouvrir le plan dont une colonne raconte cet écrit"
            };
            _planBtn.Click += delegate
            {
                var plan = _item == null || PlanLocator == null ? null : PlanLocator(_item);
                var handler = PlanRequested;
                if (plan != null && handler != null) handler(plan);
            };
            views.Children.Add(_planBtn);
            _pagesViewBtn = ViewToggle("Pages",
                "La page réelle : marges, folios, gabarits");
            _pagesViewBtn.Click += delegate { SetDraftView(false); };
            views.Children.Add(_pagesViewBtn);
            _draftViewBtn = ViewToggle("Brouillon",
                "Colonne continue sans décor de page ni folio — écrire au fil de l'eau");
            _draftViewBtn.Click += delegate { SetDraftView(true); };
            views.Children.Add(_draftViewBtn);
            _calmViewBtn = ViewToggle("Calme",
                "Ne garder que le texte (Échap pour revenir)");
            _calmViewBtn.Click += delegate
            {
                UpdateViewButtons(); // l'état réel suivra SetCalm
                var handler = CalmRequested;
                if (handler != null) handler();
            };
            views.Children.Add(_calmViewBtn);
            UpdateViewButtons();

            var host = new Grid();
            host.Children.Add(tabs);
            host.Children.Add(views);
            bar.Child = host;
            _ribbonBar = bar;
            Children.Add(bar);
        }

        private ToggleButton ViewToggle(string label, string tooltip)
        {
            var button = Buttons.TextToggle(label, tooltip, Buttons.Compact);
            button.Margin = new Thickness(4, 0, 0, 0);
            return button;
        }

        /// <summary>L'état du sélecteur d'affichage — une seule position
        /// enfoncée ; Pages et Brouillon se grisent en mode de compatibilité
        /// (le repli classique n'a que la vue paginée du miroir).</summary>
        private void UpdateViewButtons()
        {
            if (_pagesViewBtn == null) return;
            _pagesViewBtn.IsChecked = !_calm && !_draftView;
            _draftViewBtn.IsChecked = !_calm && _draftView;
            _calmViewBtn.IsChecked = _calm;
            var composed = !Settings.AppSettings.ClassicCompatibility;
            _pagesViewBtn.IsEnabled = composed;
            _draftViewBtn.IsEnabled = composed;
            if (!composed)
            {
                _pagesViewBtn.ToolTip = "Mode de compatibilité actif (Préférences)";
                _draftViewBtn.ToolTip = _pagesViewBtn.ToolTip;
            }
        }

        /// <summary>Bascule Pages ↔ Brouillon : ré-attache la surface composée
        /// avec le réglage de page dérivé. Persistant (réglage d'application).</summary>
        private void SetDraftView(bool draft)
        {
            _draftView = draft;
            Settings.AppSettings.DraftView = draft;
            Settings.AppSettings.Save();
            UpdateViewButtons();
            if (ComposedActive && _item != null) SetComposition(true);
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

        /// <summary>Mode calme : le ruban et le panneau de notes s'effacent,
        /// la coquille masque le reste (Pile, inspecteur, menus, barre d'état).</summary>
        public void SetCalm(bool calm)
        {
            _calm = calm;
            _ribbonBar.Visibility = calm ? Visibility.Collapsed : Visibility.Visible;
            if (calm) _searchBar.Visibility = Visibility.Collapsed;
            UpdateViewButtons(); // le sélecteur d'affichage suit
            RebuildNotesPanel(); // la visibilité des panneaux suit _calm
            RebuildCorrectionPanel();
            RebuildAnnotationsPanel();
        }

        // ============================================================= « Gabarit » tab

        /// <summary>Header/footer of THIS document — applied to every page.
        /// A page gabarit applied to the document wins over these.</summary>
        private UIElement BuildDecorTab()
        {
            var panel = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };
            var header = new Button
            {
                Content = TabButtonContent("sort-descending-bold", "Éditer l'en-tête…"),
                ToolTip = "Ligne d'en-tête sur toutes les pages du document "
                    + "(jetons : {page}, {pages}, {titre})",
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false
            };
            header.Click += delegate { EditHeaderFooter(true); };
            panel.Children.Add(header);
            var footer = new Button
            {
                Content = TabButtonContent("sort-ascending-bold", "Éditer le pied de page…"),
                ToolTip = "Pied de page sur toutes les pages — c'est ici que se "
                    + "règle le look des numéros de page ({page})",
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false
            };
            footer.Click += delegate { EditHeaderFooter(false); };
            panel.Children.Add(footer);
            panel.Children.Add(new TextBlock
            {
                Text = "Un gabarit de pages appliqué au document (livres) remplace ces réglages.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });
            return panel;
        }

        /// <summary>Approche sur la sélection : effective dans la Composition
        /// (le RichTextBox classique ne rend pas l'interlettrage — le réglage
        /// s'applique au pivot et se voit en Composition/aperçu/PDF).</summary>
        private void ApplyTrackingStep(double delta)
        {
            if (_item == null) return;
            if (ComposedActive)
            {
                _composed.ApplyTracking(delta);
                SyncTrackingBox();
                _composed.Focus();
                return;
            }
            MessageBox.Show(Window.GetWindow(this),
                "L'approche se règle depuis le mode Composition (onglet Composition),\n"
                + "où son effet est visible à l'écran.",
                "Approche", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ApplyTrackingAbsolute(double value)
        {
            if (_item == null) return;
            if (ComposedActive)
            {
                _composed.SetTracking(value);
                SyncTrackingBox();
                _composed.Focus();
                return;
            }
            MessageBox.Show(Window.GetWindow(this),
                "L'approche se règle depuis le mode Composition (onglet Composition),\n"
                + "où son effet est visible à l'écran.",
                "Approche", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Combos du ruban en MODE COMPOSITION : style, police et
        /// taille au caret — la synchro classique (SyncToolbar) s'arrête dès
        /// que la surface composée est active, d'où des combos vides.</summary>
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
                _fontCombo.SelectedItem = fontFamily;
                if (fontFamily != null && _fontCombo.SelectedItem == null)
                    _fontCombo.Text = fontFamily;
                _sizeCombo.SelectedItem = (int)Math.Round(sizePt);
                if (_sizeCombo.SelectedItem == null)
                    _sizeCombo.Text = sizePt.ToString("0.#",
                        System.Globalization.CultureInfo.CurrentCulture);

                // Les BASCULES aussi (gras/italique/…, alignements exclusifs,
                // listes) — sans cette synchro, un ToggleButton cliqué gardait
                // son état à lui (centré ET justifié actifs à la fois).
                bool bold, italic, underline, strike;
                string align, listKind;
                _composed.SelectionFlags(out bold, out italic, out underline,
                    out strike, out align, out listKind);
                _boldBtn.IsChecked = bold;
                _italicBtn.IsChecked = italic;
                _underBtn.IsChecked = underline;
                _strikeBtn.IsChecked = strike;
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
        public event Action CompileRequested;  // Compiler le manuscrit
        public event Action PdfRequested;      // PDF prêt à imprimer (4b-2)

        private UIElement BuildCompositionTab()
        {
            var panel = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };

            // Le bouton « Composition » a disparu avec le gel du classique
            // (batch 26) : l'onglet ne porte plus que les sorties.
            // Icône + libellé quand l'icône existe ; texte seul sinon
            // (« Aperçu », « PDF prêt à imprimer » n'ont pas d'icône — b40).
            panel.Children.Add(CompositionAction(null, "Aperçu des pages",
                "Les pages exactes, prêtes à relire (Ctrl+Alt+P)",
                delegate { var handler = PreviewRequested; if (handler != null) handler(); }));
            panel.Children.Add(CompositionAction("file-text-bold", "Imprimer / PDF…",
                "Impression ou PDF via « Microsoft Print to PDF » (Ctrl+P)",
                delegate { var handler = PrintRequested; if (handler != null) handler(); }));
            panel.Children.Add(CompositionAction(null, "PDF prêt à imprimer…",
                "PDF maison : polices incorporées, fond perdu, traits de coupe",
                delegate { var handler = PdfRequested; if (handler != null) handler(); }));
            panel.Children.Add(CompositionAction("file-arrow-down-bold", "Exporter l'écrit…",
                "docx, odt, RTF, Markdown, texte (Ctrl+E)",
                delegate { var handler = ExportRequested; if (handler != null) handler(); }));
            panel.Children.Add(CompositionAction("files-bold", "Compiler le manuscrit…",
                "Assembler les écrits en un manuscrit exportable (Ctrl+Maj+E)",
                delegate { var handler = CompileRequested; if (handler != null) handler(); }));
            return panel;
        }

        private Button CompositionAction(string icon, string label, string tooltip, Action onClick)
        {
            var button = icon == null
                ? Buttons.Text(label, tooltip, Buttons.Bar, Buttons.Look.Calm)
                : Buttons.IconText(icon, label, tooltip, Buttons.Bar, Buttons.Look.Calm);
            button.Margin = new Thickness(0, 0, 4, 0);
            button.Click += delegate { onClick(); };
            return button;
        }

        private static UIElement TabButtonContent(string iconName, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icons.Make(iconName, 14, Chrome.Ink) as FrameworkElement;
            if (icon != null) icon.Margin = new Thickness(0, 0, 5, 0);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        // ============================================================= « Mise en page » tab

        private ComboBox _marginsCombo, _sizeComboPage, _columnsCombo;
        private ToggleButton _guidesBtn, _lineNumbersBtn, _hyphenBtn, _folioBtn;
        private ToggleButton _marksBtn;
        private bool _syncingPage;

        private UIElement BuildPageSetupTab()
        {
            var panel = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };

            panel.Children.Add(PageLabel("Marges"));
            _marginsCombo = new ComboBox
            {
                Width = 150,
                Margin = new Thickness(4, 0, 10, 0),
                ToolTip = "Nomenclature PAO : de tête (haut), de pied (bas), "
                    + "petit fond (côté reliure), grand fond (côté extérieur)"
            };
            _marginsCombo.Items.Add("Livre (20/20/30/20)");
            _marginsCombo.Items.Add("Uniformes (2,5 cm)");
            _marginsCombo.Items.Add("Étroites (1,27 cm)");
            _marginsCombo.Items.Add("Personnalisées…");
            _marginsCombo.SelectionChanged += OnMarginsComboChanged;
            panel.Children.Add(_marginsCombo);

            panel.Children.Add(PageLabel("Taille"));
            _sizeComboPage = new ComboBox { Width = 150, Margin = new Thickness(4, 0, 10, 0) };
            _sizeComboPage.Items.Add("A4 (21 × 29,7 cm)");
            _sizeComboPage.Items.Add("A5 (14,8 × 21 cm)");
            _sizeComboPage.Items.Add("Letter (21,6 × 27,9 cm)");
            _sizeComboPage.Items.Add("Livre (14 × 21,6 cm)");
            _sizeComboPage.Items.Add("Personnalisée…");
            _sizeComboPage.SelectionChanged += OnPageSizeComboChanged;
            panel.Children.Add(_sizeComboPage);

            var columnsIcon = Icons.Make("text-columns-bold", 14, Chrome.SoftText) as FrameworkElement;
            if (columnsIcon != null)
            {
                columnsIcon.VerticalAlignment = VerticalAlignment.Center;
                columnsIcon.ToolTip = "Colonnes";
                panel.Children.Add(columnsIcon);
            }
            _columnsCombo = new ComboBox { Width = 46, Margin = new Thickness(4, 0, 10, 0), ToolTip = "Colonnes — appliquées à l'export et à l'impression" };
            _columnsCombo.Items.Add(1);
            _columnsCombo.Items.Add(2);
            _columnsCombo.Items.Add(3);
            _columnsCombo.SelectionChanged += delegate
            {
                if (_syncingPage || _project == null || _columnsCombo.SelectedItem == null) return;
                _pageSetup.Columns = (int)_columnsCombo.SelectedItem;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_columnsCombo);

            var breakBtn = new Button
            {
                Content = TabButtonContent("file-arrow-down-bold", "Saut de page"),
                ToolTip = "Commencer une nouvelle page au paragraphe du curseur (Ctrl+Entrée)",
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false
            };
            breakBtn.Click += delegate { InsertPageBreak(); };
            panel.Children.Add(breakBtn);

            _guidesBtn = PageToggle("margins", "Marges", "Cadres de marges sur chaque page");
            _guidesBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.ShowMarginGuides = _guidesBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_guidesBtn);

            _lineNumbersBtn = PageToggle("list-numbers-bold", "Numéros de ligne", "à l'export Word et à l'impression");
            _lineNumbersBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.LineNumbers = _lineNumbersBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_lineNumbersBtn);

            _hyphenBtn = PageToggle("kerning", "Césure", "Coupure des mots en fin de ligne");
            _hyphenBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.Hyphenation = _hyphenBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_hyphenBtn);

            _folioBtn = PageToggle("symbol", "Folio", "Numéro de page centré en pied de page (aperçu, impression, export Word)");
            _folioBtn.Click += delegate
            {
                if (_project == null) return;
                _pageSetup.FooterPageNumbers = _folioBtn.IsChecked == true;
                AfterPageSetupEdit();
            };
            panel.Children.Add(_folioBtn);

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

        /// <summary>Une bascule de la barre « Mise en page » : icône seule,
        /// le libellé passe dans l'infobulle (batch 40).</summary>
        private ToggleButton PageToggle(string icon, string label, string tooltip)
        {
            var button = Buttons.IconToggle(icon, label + " — " + tooltip, Buttons.Bar);
            button.Margin = new Thickness(0, 0, 4, 0);
            return button;
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
                var values = NumbersDialog.Ask(Window.GetWindow(this), "Marges (cm)",
                    new[]
                    {
                        "De tête (marge haute)",
                        "De pied (marge basse)",
                        "Petit fond (côté reliure)",
                        "Grand fond (côté extérieur)"
                    },
                    new[] { page.MarginTopMm / 10, page.MarginBottomMm / 10, page.MarginLeftMm / 10, page.MarginRightMm / 10 },
                    0.5, 10);
                if (values == null) { SyncPageTab(); return; }
                SetMarginsMm(page, values[0] * 10, values[1] * 10, values[2] * 10, values[3] * 10);
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
            ApplyPageVisuals();
            if (ComposedActive) _composed.RefreshComposition();
            var handler = PageSetupChanged;
            if (handler != null) handler();
        }


        // Boutons du ruban à icône seule : CARRÉS (26 × 26, b43).
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

        private static List<string> ListFonts()
        {
            var names = new List<string>();
            foreach (var family in Fonts.SystemFontFamilies) names.Add(family.Source);
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        private ToggleButton FormatToggle(string label, string tooltip,
            bool bold, bool italic, bool underline, bool strike)
        {
            // Icônes vectorielles embarquées (jeu Phosphor).
            var icon = bold ? "text-b-bold"
                     : italic ? "text-italic-bold"
                     : underline ? "text-underline-bold"
                     : "text-strikethrough-bold";
            return new ToggleButton
            {
                Content = Icons.Make(icon, 14, Chrome.Ink),
                ToolTip = tooltip,
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Margin = new Thickness(1, 0, 1, 0),
                Focusable = false
            };
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
            else
            {
                var value = _box.Selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
                if (value != null) return value.Source;
            }
            return _styles.Body.FontFamily;
        }

        private string CurrentWeightName()
        {
            if (ComposedActive) return _composed.GetSelectionWeightName();
            var value = _box.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            if (!(value is FontWeight)) return "mixed";
            var weight = (FontWeight)value;
            var name = FlowConverter.WeightName(weight);
            if (name != null) return name;
            return weight >= FontWeights.Bold ? "Bold" : null;
        }

        private void ApplyWeight(string weight)
        {
            if (_item == null) return;
            if (ComposedActive)
            {
                _composed.ApplyWeight(weight);
                _composed.Focus();
                return;
            }
            _box.Selection.ApplyPropertyValue(TextElement.FontWeightProperty,
                weight == null ? FontWeights.Normal : FlowConverter.ParseWeight(weight));
            AfterFormat();
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
                if (ComposedActive) { _composed.ApplyAlign(align); _composed.Focus(); return; }
                var command = align == "center" ? EditingCommands.AlignCenter
                            : align == "right" ? EditingCommands.AlignRight
                            : align == "justify" ? EditingCommands.AlignJustify
                            : EditingCommands.AlignLeft;
                command.Execute(null, _box);
                AfterFormat();
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

        /// <summary>Séparateur vertical courant sur les deux lignes.</summary>
        private static Border VerticalRuleTall()
        {
            return new Border
            {
                Width = 1,
                MinHeight = 44,
                Background = Chrome.Border,
                Margin = new Thickness(7, 2, 7, 2)
            };
        }

        /// <summary>Un bouton texte du ruban — 32 px, calme (batch 40 : les
        /// « grands » boutons Office à 44 px n'existent plus, deux hauteurs
        /// seulement).</summary>
        private Button TallButton(string label, string tooltip)
        {
            var button = Buttons.Text(label, tooltip, Buttons.Bar, Buttons.Look.Calm);
            button.Margin = new Thickness(0, 0, 4, 0);
            return button;
        }

        /// <summary>Variante icône + libellé.</summary>
        private Button TallButton(string icon, string label, string tooltip)
        {
            var button = Buttons.IconText(icon, label, tooltip, Buttons.Bar, Buttons.Look.Calm);
            button.Margin = new Thickness(0, 0, 4, 0);
            return button;
        }

        private ToggleButton TallToggle(string label, string tooltip)
        {
            var button = Buttons.TextToggle(label, tooltip, Buttons.Bar);
            button.Margin = new Thickness(0, 0, 4, 0);
            return button;
        }

        /// <summary>Précédent / suivant côte à côte : deux boutons à icône
        /// seule (batch 40 — plus d'empilement de deux lignes).</summary>
        private static StackPanel SideBySide(Button previous, Button next)
        {
            previous.Margin = new Thickness(0, 0, 1, 0);
            next.Margin = new Thickness(0, 0, 4, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(previous);
            row.Children.Add(next);
            return row;
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
            if (ComposedActive)
            {
                if (isForeground) _composed.ApplyColor(hex);
                else _composed.ApplyHighlight(hex);
                _composed.Focus();
                return;
            }
            if (isForeground)
                _box.Selection.ApplyPropertyValue(TextElement.ForegroundProperty,
                    hex == null ? (Brush)Chrome.PaperInk : new SolidColorBrush(FlowConverter.ParseColor(hex)));
            else
                _box.Selection.ApplyPropertyValue(TextElement.BackgroundProperty,
                    hex == null ? null : new SolidColorBrush(FlowConverter.ParseColor(hex)));
            AfterFormat();
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

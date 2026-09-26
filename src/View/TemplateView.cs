using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>The page gabarit view: two facing pages (verso | recto) at the
    /// book's margins. The four header/footer zones are REAL rich-text
    /// surfaces — on marque leur contenu aux outils d'édition (gras, italique,
    /// police, taille, approche par les runs) au lieu d'une boîte de dialogue.
    /// Plus : écart en-tête/pied ↔ bloc de texte (mm), masquage sur la
    /// première page, variables documentées. Pas de corps éditable ni de
    /// menu Mise en page : c'est une maquette.</summary>
    public class TemplateView : Border
    {
        private BinderItem _item;   // the PageTemplate
        private BinderItem _book;   // its enclosing book (margins source)
        private Project _project;
        private readonly StackPanel _root;
        private RichTextBox _focusedZone;
        private readonly List<RichTextBox> _zones = new List<RichTextBox>();
        private bool _loading;
        private ComboBox _sizeCombo;   // barre d'outils des zones
        private FontPicker _fontCombo; // le sélecteur partagé (0.50.0)
        private bool _syncingBar;

        public event Action Changed;

        public TemplateView()
        {
            Focusable = true;
            Background = Chrome.WindowBg;
            _root = new StackPanel { Margin = new Thickness(24, 12, 24, 16) };
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _root
            };
        }

        public void Load(BinderItem item, Project project)
        {
            _item = item;
            _project = project;
            _book = item.EnclosingBook();
            Rebuild();
        }

        public void Clear()
        {
            CommitZones();
            _item = null;
            _zones.Clear();
            _focusedZone = null;
            _root.Children.Clear();
        }

        public bool ShowsItem(BinderItem item) { return _item == item; }

        /// <summary>Flushes every zone's rich content back into the model.</summary>
        public void CommitZones()
        {
            if (_item == null) return;
            foreach (var zone in _zones) CommitZone(zone);
        }

        // ============================================================ build

        private void Rebuild()
        {
            CommitZones();
            _root.Children.Clear();
            _zones.Clear();
            if (_item == null) return;
            _loading = true;
            try
            {
                _root.Children.Add(new TextBlock
                {
                    Text = "Gabarit « " + _item.Title + " »",
                    Foreground = Chrome.Ink,
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold
                });
                _root.Children.Add(BuildVariablesExpander());
                _root.Children.Add(BuildOptionsRow());
                _root.Children.Add(BuildFormatBar());

                var setup = _book != null && _book.Book != null
                    ? _book.Book.Template : new PageSetup();
                var spread = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                spread.Children.Add(BuildPage(setup, false));
                spread.Children.Add(BuildPage(setup, true));
                _root.Children.Add(spread);
            }
            finally
            {
                _loading = false;
            }
        }

        private UIElement BuildVariablesExpander()
        {
            var list = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(12, 4, 0, 4),
                Text = "{page} le folio de la page (le numéro de page dans le livre)\n"
                    + "{pages} le nombre total de pages\n"
                    + "{titre} le titre du document auquel le gabarit est appliqué\n"
                    + "{livre} le titre du livre"
            };
            return new Expander
            {
                Header = new TextBlock
                {
                    Text = "Variables insérables dans les zones",
                    Foreground = Chrome.SoftText,
                    FontSize = 12
                },
                Content = list,
                Margin = new Thickness(0, 6, 0, 0)
            };
        }

        private UIElement BuildOptionsRow()
        {
            var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

            // Pastille de couleur.
            row.Children.Add(Label("Pastille :"));
            foreach (var swatch in ItemIcons.TintSwatches)
            {
                var value = swatch;
                var chip = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(2, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = value == null
                        ? Brushes.Transparent
                        : new SolidColorBrush(FlowConverter.ParseColor(value)),
                    BorderBrush = _item.TemplateColor == value ? (Brush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(_item.TemplateColor == value ? 2.2 : 1),
                    ToolTip = value == null ? "Aucune" : value,
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                chip.MouseLeftButtonUp += delegate
                {
                    _item.TemplateColor = value;
                    Rebuild();
                    RaiseChanged();
                };
                row.Children.Add(chip);
            }

            // Écarts (mm), champs à la Adobe (▲▼ + valeur). CONTINUS : un
            // décalage depuis la position centrée dans la marge — 0 = centré,
            // positif = vers le bord de page, négatif = vers le corps.
            row.Children.Add(Label("   En-tête ↔ corps (mm) :"));
            var headerGap = new SpinnerField(_item.HeaderGapMm, -30, 30, 1,
                "Décalage de l'en-tête depuis le centre de la marge\n"
                + "(positif = vers le bord de page, négatif = vers le corps)");
            headerGap.ValueChanged += delegate(double value)
            {
                _item.HeaderGapMm = value;
                Rebuild(); // la maquette reflète l'écart
                RaiseChanged();
            };
            row.Children.Add(headerGap);
            row.Children.Add(Label("   Pied ↔ corps (mm) :"));
            var footerGap = new SpinnerField(_item.FooterGapMm, -30, 30, 1,
                "Décalage du pied de page depuis le centre de la marge\n"
                + "(positif = vers le bord de page, négatif = vers le corps)");
            footerGap.ValueChanged += delegate(double value)
            {
                _item.FooterGapMm = value;
                Rebuild(); // la maquette reflète l'écart
                RaiseChanged();
            };
            row.Children.Add(footerGap);

            // Masquage sur la première page.
            var hideHeader = new CheckBox
            {
                Content = "Sans en-tête page 1",
                Foreground = Chrome.Ink,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = _item.HeaderHideFirst,
                ToolTip = "Ne pas afficher l'en-tête sur la première page du document "
                    + "(évite le titre de chapitre en corps ET en haut de page)"
            };
            hideHeader.Click += delegate
            {
                _item.HeaderHideFirst = hideHeader.IsChecked == true;
                RaiseChanged();
            };
            row.Children.Add(hideHeader);
            var hideFooter = new CheckBox
            {
                Content = "Sans pied page 1",
                Foreground = Chrome.Ink,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = _item.FooterHideFirst,
                ToolTip = "Ne pas afficher le pied de page sur la première page du document"
            };
            hideFooter.Click += delegate
            {
                _item.FooterHideFirst = hideFooter.IsChecked == true;
                RaiseChanged();
            };
            row.Children.Add(hideFooter);
            return row;
        }

        // ------------------------------------------------------------ format bar

        /// <summary>Mini barre d'outils texte, agissant sur la zone focalisée.</summary>
        private UIElement BuildFormatBar()
        {
            var bar = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            bar.Children.Add(FormatButton("text-b-bold", "Gras",
                delegate { ToggleProperty(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal); }));
            bar.Children.Add(FormatButton("text-italic-bold", "Italique",
                delegate { ToggleProperty(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal); }));
            bar.Children.Add(FormatButton("text-underline-bold", "Souligné", ToggleUnderline));

            _sizeCombo = new ComboBox
            {
                Width = 52,
                Margin = new Thickness(6, 0, 0, 0),
                IsEditable = true,
                ToolTip = "Taille (pt) — tapez une valeur libre puis Entrée"
            };
            foreach (var pt in new[] { 7, 8, 9, 10, 11, 12, 14, 16 }) _sizeCombo.Items.Add(pt);
            _sizeCombo.SelectionChanged += delegate
            {
                if (_syncingBar || _focusedZone == null || _sizeCombo.SelectedItem == null) return;
                _focusedZone.Selection.ApplyPropertyValue(TextElement.FontSizeProperty,
                    (int)_sizeCombo.SelectedItem * 4.0 / 3.0);
                ZoneEdited(_focusedZone);
            };
            _sizeCombo.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                e.Handled = true;
                if (_focusedZone == null) return;
                double pt;
                if (!double.TryParse(_sizeCombo.Text.Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out pt)) return;
                pt = Math.Max(4, Math.Min(96, pt));
                _focusedZone.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, pt * 4.0 / 3.0);
                ZoneEdited(_focusedZone);
            };
            bar.Children.Add(_sizeCombo);

            // Vrai sélecteur de polices système (comme le ruban de l'éditeur).
            _fontCombo = new FontPicker
            {
                Width = 150,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Police de la sélection — tapez un nom puis Entrée, ou parcourez aux flèches"
            };
            _fontCombo.FontChosen += delegate(string name, bool preview)
            {
                if (_syncingBar || _focusedZone == null || string.IsNullOrEmpty(name)) return;
                _focusedZone.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, FontCatalog.FamilyOf(name));
                ZoneEdited(_focusedZone);
                if (!preview) _focusedZone.Focus();
            };
            bar.Children.Add(_fontCombo);

            bar.Children.Add(FormatButton("text-align-left-bold", "Aligné à gauche",
                delegate { ApplyAlign(TextAlignment.Left); }));
            bar.Children.Add(FormatButton("text-align-center-bold", "Centré",
                delegate { ApplyAlign(TextAlignment.Center); }));
            bar.Children.Add(FormatButton("text-align-right-bold", "Aligné à droite",
                delegate { ApplyAlign(TextAlignment.Right); }));

            var variables = new ComboBox
            {
                Width = 110,
                Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "Insérer une variable au curseur"
            };
            variables.Items.Add("{page}");
            variables.Items.Add("{pages}");
            variables.Items.Add("{titre}");
            variables.Items.Add("{livre}");
            variables.SelectionChanged += delegate
            {
                if (_focusedZone == null || variables.SelectedItem == null) return;
                _focusedZone.CaretPosition.InsertTextInRun((string)variables.SelectedItem);
                ZoneEdited(_focusedZone);
                variables.SelectedIndex = -1;
            };
            bar.Children.Add(variables);
            return bar;
        }

        private Button FormatButton(string icon, string tooltip, Action action)
        {
            var button = new Button
            {
                Content = Icons.Make(icon, 13, Chrome.Ink),
                ToolTip = tooltip,
                Width = 30,
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 2, 0),
                Focusable = false // le focus reste dans la zone
            };
            button.Click += delegate { action(); };
            return button;
        }

        private void ToggleProperty(DependencyProperty property, object on, object off)
        {
            if (_focusedZone == null) return;
            var current = _focusedZone.Selection.GetPropertyValue(property);
            _focusedZone.Selection.ApplyPropertyValue(property,
                Equals(current, on) ? off : on);
            ZoneEdited(_focusedZone);
        }

        private void ToggleUnderline()
        {
            if (_focusedZone == null) return;
            var current = _focusedZone.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                as TextDecorationCollection;
            _focusedZone.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
                current != null && current.Count > 0 ? null : TextDecorations.Underline);
            ZoneEdited(_focusedZone);
        }

        private void ApplyAlign(TextAlignment alignment)
        {
            if (_focusedZone == null) return;
            _focusedZone.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, alignment);
            ZoneEdited(_focusedZone);
        }

        // ------------------------------------------------------------ pages

        private UIElement BuildPage(PageSetup setup, bool recto)
        {
            var scale = Math.Min(1.0, 430.0 / (setup.PageWidthMm * PageSetup.PxPerMm));
            var width = setup.PageWidthMm * PageSetup.PxPerMm * scale;
            var height = setup.PageHeightMm * PageSetup.PxPerMm * scale;
            var top = setup.MarginTopMm * PageSetup.PxPerMm * scale;
            var bottom = setup.MarginBottomMm * PageSetup.PxPerMm * scale;
            var inner = setup.MarginLeftMm * PageSetup.PxPerMm * scale;
            var outer = setup.MarginRightMm * PageSetup.PxPerMm * scale;
            var left = recto ? inner : outer;
            var right = recto ? outer : inner;

            var grid = new Grid { Width = width, Height = height };
            grid.Children.Add(new Border
            {
                BorderBrush = ComposedRenderer.MarginPen.Brush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(left, top, right, bottom)
            });
            grid.Children.Add(new TextBlock
            {
                Text = recto ? "recto — folio impair" : "verso — folio pair",
                Foreground = Chrome.PaperSoftInk,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(BuildZone(recto, true, top, left, right, scale));
            grid.Children.Add(BuildZone(recto, false, bottom, left, right, scale));

            return new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, recto ? 0 : 1, 0),
                Child = grid
            };
        }

        /// <summary>One editable rich zone, marked with the format bar tools.
        /// Placée là où le rendu la posera : à l'écart réglé du bloc de texte
        /// (négatif = dans le bloc), 0 = centrée dans la marge — changer
        /// l'écart se JUGE sur la maquette.</summary>
        private UIElement BuildZone(bool recto, bool isHeader, double marginPx,
            double left, double right, double scale)
        {
            var hf = ZoneOf(recto, isHeader);
            var setup = _project == null ? new PageSetup()
                : (_book != null && _book.Book != null ? _book.Book.Template : _project.Page);
            const double zoneH = 26;
            var gap = (isHeader ? _item.HeaderGapMm : _item.FooterGapMm)
                * PageSetup.PxPerMm * scale;
            // Décalage CONTINU depuis la position centrée dans la marge — la
            // même règle que ComposedRenderer.DrawDecor (header et footer
            // symétriques une fois exprimés depuis leur bord).
            var edgeOffset = Math.Max(1, marginPx / 2 - zoneH / 2 - gap);
            var zone = new RichTextBox
            {
                Height = zoneH,
                VerticalAlignment = isHeader ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Margin = isHeader
                    ? new Thickness(left, edgeOffset, right, 0)
                    : new Thickness(left, 0, right, edgeOffset),
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0.6),
                Background = Brushes.Transparent,
                Foreground = Chrome.PaperInk,
                CaretBrush = Chrome.PaperInk,
                FontFamily = new FontFamily(setup.FooterFont ?? "Times New Roman"),
                FontSize = 13.3, // 10 pt
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                ToolTip = (isHeader ? "En-tête " : "Pied ") + (recto ? "recto" : "verso")
                    + " — marquez le texte avec la barre d'outils, variables {page}/{pages}/{titre}",
                Tag = new[] { recto ? "r" : "v", isHeader ? "h" : "f" }
            };
            zone.Document = RichToFlow(hf);
            zone.GotKeyboardFocus += delegate { _focusedZone = zone; SyncFormatBar(); };
            zone.LostKeyboardFocus += delegate { CommitZone(zone); };
            zone.TextChanged += delegate { if (!_loading) ScheduleCommit(zone); };
            zone.SelectionChanged += delegate { if (!_loading) SyncFormatBar(); };
            _zones.Add(zone);
            return zone;
        }

        private HeaderFooter ZoneOf(bool recto, bool isHeader)
        {
            if (isHeader) return recto ? _item.HeaderRecto : _item.HeaderVerso;
            return recto ? _item.FooterRecto : _item.FooterVerso;
        }

        private void SetZone(bool recto, bool isHeader, HeaderFooter value)
        {
            if (isHeader) { if (recto) _item.HeaderRecto = value; else _item.HeaderVerso = value; }
            else { if (recto) _item.FooterRecto = value; else _item.FooterVerso = value; }
        }

        // ------------------------------------------------------------ rich ↔ flow

        private FlowDocument RichToFlow(HeaderFooter hf)
        {
            var document = new TextDocument();
            if (hf != null && hf.Rich != null) document.Paragraphs.Add(hf.Rich);
            else if (hf != null && !hf.IsEmpty)
            {
                // Héritage v6 : champs simples convertis en un run.
                var paragraph = new TextParagraph { AlignOverride = hf.Align };
                paragraph.Runs.Add(new TextRun
                {
                    Text = hf.Text,
                    Bold = hf.Bold ? (bool?)true : null,
                    Italic = hf.Italic ? (bool?)true : null,
                    FontFamily = hf.FontFamily,
                    FontSize = hf.SizePt * 4.0 / 3.0
                });
                document.Paragraphs.Add(paragraph);
            }
            else document.Paragraphs.Add(new TextParagraph());
            return FlowConverter.ToFlow(document, _project.Styles, _project);
        }

        private void CommitZone(RichTextBox zone)
        {
            if (_item == null || zone == null || zone.Tag == null) return;
            var tag = (string[])zone.Tag;
            var recto = tag[0] == "r";
            var isHeader = tag[1] == "h";
            var document = FlowConverter.FromFlow(zone.Document, _project.Styles,
                new List<Footnote>(), _project);
            var paragraph = document.Paragraphs.Count > 0 ? document.Paragraphs[0] : null;
            var empty = paragraph == null;
            if (!empty)
            {
                empty = true;
                foreach (var run in paragraph.Runs)
                    if (run.Text != null && run.Text.Trim().Length > 0) { empty = false; break; }
            }
            var existing = ZoneOf(recto, isHeader);
            if (empty)
            {
                if (existing != null) { SetZone(recto, isHeader, null); RaiseChanged(); }
                return;
            }
            // Ne signaler que les VRAIS changements : la perte de focus commite
            // toujours, et un Changed gratuit reconstruisait la Pile en plein
            // clic (le nœud visé disparaissait sous la souris).
            var before = existing == null ? null
                : Json.Write(Persistence.GabaritFile.BuildHeaderFooter(existing));
            var hf = existing ?? new HeaderFooter();
            hf.Rich = paragraph;
            SetZone(recto, isHeader, hf);
            var after = Json.Write(Persistence.GabaritFile.BuildHeaderFooter(hf));
            if (before != after) RaiseChanged();
        }

        /// <summary>A format action touched a zone: schedule its commit.</summary>
        private void ZoneEdited(RichTextBox zone)
        {
            if (zone != null) ScheduleCommit(zone);
        }

        /// <summary>La barre reflète la sélection de la zone focalisée —
        /// police et taille toujours renseignées, jamais de combos vides.</summary>
        private void SyncFormatBar()
        {
            if (_sizeCombo == null || _focusedZone == null) return;
            _syncingBar = true;
            try
            {
                var size = _focusedZone.Selection.GetPropertyValue(TextElement.FontSizeProperty);
                if (size is double)
                {
                    var pt = (int)Math.Round((double)size * 0.75);
                    if (_sizeCombo.Items.Contains(pt)) _sizeCombo.SelectedItem = pt;
                    else
                    {
                        // Valeur hors liste (taille libre) : affichée en texte.
                        _sizeCombo.SelectedIndex = -1;
                        _sizeCombo.Text = pt.ToString();
                    }
                }
                else _sizeCombo.SelectedIndex = -1;
                var family = _focusedZone.Selection
                    .GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
                if (family == null) _fontCombo.ShowMixed(); else _fontCombo.Select(family.Source);
            }
            finally
            {
                _syncingBar = false;
            }
        }

        private System.Windows.Threading.DispatcherTimer _commitTimer;
        private RichTextBox _pendingZone;

        private void ScheduleCommit(RichTextBox zone)
        {
            _pendingZone = zone;
            if (_commitTimer == null)
            {
                _commitTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(600) };
                _commitTimer.Tick += delegate
                {
                    _commitTimer.Stop();
                    if (_pendingZone != null) CommitZone(_pendingZone);
                };
            }
            _commitTimer.Stop();
            _commitTimer.Start();
        }

        private TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private void RaiseChanged()
        {
            if (_loading) return;
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

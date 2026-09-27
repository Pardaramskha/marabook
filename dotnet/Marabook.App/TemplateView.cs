using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Model;

namespace Marabook.App
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
        private TextBox _focusedZone;
        private readonly List<TextBox> _zones = new List<TextBox>();
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
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
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
                    FontWeight = FontWeight.SemiBold
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

        private Control BuildVariablesExpander()
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

        private Control BuildOptionsRow()
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
                        : new SolidColorBrush(Ink.Parse(value).ToColor()),
                    BorderBrush = _item.TemplateColor == value ? (IBrush)Chrome.Ink : Chrome.Border,
                    BorderThickness = new Thickness(_item.TemplateColor == value ? 2.2 : 1),
                    [ToolTip.TipProperty] = value == null ? "Aucune" : value,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                chip.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) {
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
                [ToolTip.TipProperty] = "Ne pas afficher l'en-tête sur la première page du document "
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
                [ToolTip.TipProperty] = "Ne pas afficher le pied de page sur la première page du document"
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

        /// <summary>Mini barre d'outils texte, agissant sur la zone focalisée.
        /// Sur Avalonia la zone est un TextBox (pas de texte riche) : les
        /// attributs valent pour toute la zone — gras, italique, souligné,
        /// taille, police, alignement vivent dans le run unique du
        /// HeaderFooter, comme le rendu les lit.</summary>
        private Control BuildFormatBar()
        {
            var bar = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            bar.Children.Add(FormatButton("text-b-bold", "Gras",
                delegate { ApplyZone(delegate(TextRun run) { run.Bold = run.Bold == true ? (bool?)null : true; }); }));
            bar.Children.Add(FormatButton("text-italic-bold", "Italique",
                delegate { ApplyZone(delegate(TextRun run) { run.Italic = run.Italic == true ? (bool?)null : true; }); }));
            bar.Children.Add(FormatButton("text-underline-bold", "Souligné",
                delegate { ApplyZone(delegate(TextRun run) { run.Underline = run.Underline == true ? (bool?)null : true; }); }));

            _sizeCombo = new ComboBox
            {
                Width = 52,
                Margin = new Thickness(6, 0, 0, 0),
                IsEditable = true,
                [ToolTip.TipProperty] = "Taille (pt) — tapez une valeur libre puis Entrée"
            };
            foreach (var pt in new[] { 7, 8, 9, 10, 11, 12, 14, 16 }) _sizeCombo.Items.Add(pt);
            _sizeCombo.SelectionChanged += delegate
            {
                if (_syncingBar || _focusedZone == null || _sizeCombo.SelectedItem == null) return;
                var chosen = (int)_sizeCombo.SelectedItem * 4.0 / 3.0;
                ApplyZone(delegate(TextRun run) { run.FontSize = chosen; });
            };
            _sizeCombo.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                if (_focusedZone == null) return;
                double pt;
                if (!double.TryParse((_sizeCombo.Text ?? "").Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out pt)) return;
                pt = Math.Max(4, Math.Min(96, pt));
                var chosen = pt * 4.0 / 3.0;
                ApplyZone(delegate(TextRun run) { run.FontSize = chosen; });
            };
            bar.Children.Add(_sizeCombo);

            // Vrai sélecteur de polices système (comme le ruban de l'éditeur).
            _fontCombo = new FontPicker
            {
                Width = 150,
                Margin = new Thickness(6, 0, 0, 0),
                [ToolTip.TipProperty] = "Police de la zone — tapez un nom puis Entrée, ou parcourez aux flèches"
            };
            _fontCombo.FontChosen += delegate(string name, bool preview)
            {
                if (_syncingBar || _focusedZone == null || string.IsNullOrEmpty(name)) return;
                ApplyZone(delegate(TextRun run) { run.FontFamily = name; });
                if (!preview) _focusedZone.Focus();
            };
            bar.Children.Add(_fontCombo);

            bar.Children.Add(FormatButton("text-align-left-bold", "Aligné à gauche",
                delegate { ApplyAlign("left"); }));
            bar.Children.Add(FormatButton("text-align-center-bold", "Centré",
                delegate { ApplyAlign("center"); }));
            bar.Children.Add(FormatButton("text-align-right-bold", "Aligné à droite",
                delegate { ApplyAlign("right"); }));

            var variables = new ComboBox
            {
                Width = 110,
                Margin = new Thickness(10, 0, 0, 0),
                [ToolTip.TipProperty] = "Insérer une variable au curseur"
            };
            variables.Items.Add("{page}");
            variables.Items.Add("{pages}");
            variables.Items.Add("{titre}");
            variables.Items.Add("{livre}");
            variables.SelectionChanged += delegate
            {
                if (_focusedZone == null || variables.SelectedItem == null) return;
                var zone = _focusedZone;
                var text = zone.Text ?? "";
                var at = Math.Max(0, Math.Min(text.Length, zone.CaretIndex));
                var variable = (string)variables.SelectedItem;
                zone.Text = text.Insert(at, variable);
                zone.CaretIndex = at + variable.Length;
                ZoneEdited(zone);
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
                [ToolTip.TipProperty] = tooltip,
                Width = 30,
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 2, 0),
                Focusable = false // le focus reste dans la zone
            };
            button.Click += delegate { action(); };
            return button;
        }

        /// <summary>Un attribut du run de la zone focalisée : posé dans le
        /// modèle, reflété sur le TextBox, signalé.</summary>
        private void ApplyZone(Action<TextRun> change)
        {
            var zone = _focusedZone;
            if (zone == null || _item == null || zone.Tag == null) return;
            var tag = (string[])zone.Tag;
            var recto = tag[0] == "r";
            var isHeader = tag[1] == "h";
            var existing = ZoneOf(recto, isHeader);
            var paragraph = ZoneParagraph(existing, zone.Text ?? "");
            change(paragraph.Runs[0]);
            var hf = existing ?? new HeaderFooter();
            hf.Rich = paragraph;
            SetZone(recto, isHeader, hf);
            ApplyZoneLook(zone, paragraph);
            SyncFormatBar();
            RaiseChanged();
        }

        private void ApplyAlign(string alignment)
        {
            var zone = _focusedZone;
            if (zone == null || _item == null || zone.Tag == null) return;
            var tag = (string[])zone.Tag;
            var recto = tag[0] == "r";
            var isHeader = tag[1] == "h";
            var existing = ZoneOf(recto, isHeader);
            var paragraph = ZoneParagraph(existing, zone.Text ?? "");
            paragraph.AlignOverride = alignment;
            var hf = existing ?? new HeaderFooter();
            hf.Rich = paragraph;
            SetZone(recto, isHeader, hf);
            ApplyZoneLook(zone, paragraph);
            RaiseChanged();
        }

        // ------------------------------------------------------------ pages

        private Control BuildPage(PageSetup setup, bool recto)
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
        private Control BuildZone(bool recto, bool isHeader, double marginPx,
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
            var zone = new TextBox
            {
                Height = zoneH,
                MinHeight = zoneH,
                VerticalAlignment = isHeader ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                Margin = isHeader
                    ? new Thickness(left, edgeOffset, right, 0)
                    : new Thickness(left, 0, right, edgeOffset),
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0.6),
                Background = Brushes.Transparent,
                Foreground = Chrome.PaperInk,
                CaretBrush = Chrome.PaperInk,
                Padding = new Thickness(4, 2, 4, 2),
                FontFamily = new FontFamily(setup.FooterFont ?? "Times New Roman"),
                FontSize = 13.3, // 10 pt
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Hidden,
                [ToolTip.TipProperty] = (isHeader ? "En-tête " : "Pied ") + (recto ? "recto" : "verso")
                    + " — marquez le texte avec la barre d'outils, variables {page}/{pages}/{titre}",
                Tag = new[] { recto ? "r" : "v", isHeader ? "h" : "f" }
            };
            var paragraph = ZoneParagraph(hf, null);
            zone.Text = paragraph.ToPlainText();
            ApplyZoneLook(zone, paragraph);
            zone.GotFocus += delegate { _focusedZone = zone; SyncFormatBar(); };
            zone.LostFocus += delegate { CommitZone(zone); };
            zone.TextChanged += delegate { if (!_loading) ScheduleCommit(zone); };
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

        // ------------------------------------------------------------ zone ↔ modèle

        /// <summary>Le paragraphe d'une zone : le Rich existant ramené à UN
        /// run (les attributs du premier run valent pour tout), sinon les
        /// champs simples (héritage v6), sinon vide. text : le texte tapé à
        /// poser dedans, null = le texte du modèle.</summary>
        private static TextParagraph ZoneParagraph(HeaderFooter hf, string text)
        {
            var paragraph = new TextParagraph();
            TextRun template = null;
            if (hf != null && hf.Rich != null)
            {
                paragraph.AlignOverride = hf.Rich.AlignOverride;
                if (hf.Rich.Runs.Count > 0) template = hf.Rich.Runs[0];
                if (text == null) text = hf.Rich.ToPlainText();
            }
            else if (hf != null && !hf.IsEmpty)
            {
                paragraph.AlignOverride = hf.Align;
                template = new TextRun
                {
                    Bold = hf.Bold ? (bool?)true : null,
                    Italic = hf.Italic ? (bool?)true : null,
                    FontFamily = hf.FontFamily,
                    FontSize = hf.SizePt * 4.0 / 3.0
                };
                if (text == null) text = hf.Text ?? "";
            }
            paragraph.Runs.Add(new TextRun
            {
                Text = text ?? "",
                Bold = template == null ? null : template.Bold,
                Italic = template == null ? null : template.Italic,
                Underline = template == null ? null : template.Underline,
                FontFamily = template == null ? null : template.FontFamily,
                FontSize = template == null ? null : template.FontSize,
                Color = template == null ? null : template.Color
            });
            return paragraph;
        }

        /// <summary>Le TextBox montre les attributs du run (le souligné n'a pas
        /// d'équivalent sur un TextBox : la barre seule le dit).</summary>
        private void ApplyZoneLook(TextBox zone, TextParagraph paragraph)
        {
            var run = paragraph.Runs.Count > 0 ? paragraph.Runs[0] : new TextRun();
            zone.FontWeight = run.Bold == true ? FontWeight.Bold : FontWeight.Normal;
            zone.FontStyle = run.Italic == true ? FontStyle.Italic : FontStyle.Normal;
            if (!string.IsNullOrEmpty(run.FontFamily)) zone.FontFamily = FontCatalog.FamilyOf(run.FontFamily);
            if (run.FontSize.HasValue && run.FontSize.Value > 0) zone.FontSize = run.FontSize.Value;
            zone.TextAlignment = paragraph.AlignOverride == "center" ? TextAlignment.Center
                : paragraph.AlignOverride == "right" ? TextAlignment.Right : TextAlignment.Left;
        }

        private void CommitZone(TextBox zone)
        {
            if (_item == null || zone == null || zone.Tag == null) return;
            var tag = (string[])zone.Tag;
            var recto = tag[0] == "r";
            var isHeader = tag[1] == "h";
            var existing = ZoneOf(recto, isHeader);
            var text = zone.Text ?? "";
            if (text.Trim().Length == 0)
            {
                if (existing != null) { SetZone(recto, isHeader, null); RaiseChanged(); }
                return;
            }
            var paragraph = ZoneParagraph(existing, text);
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
        private void ZoneEdited(TextBox zone)
        {
            if (zone != null) ScheduleCommit(zone);
        }

        /// <summary>La barre reflète la sélection de la zone focalisée —
        /// police et taille toujours renseignées, jamais de combos vides.</summary>
        private void SyncFormatBar()
        {
            if (_sizeCombo == null || _focusedZone == null || _focusedZone.Tag == null) return;
            _syncingBar = true;
            try
            {
                var tag = (string[])_focusedZone.Tag;
                var paragraph = ZoneParagraph(ZoneOf(tag[0] == "r", tag[1] == "h"), null);
                var run = paragraph.Runs[0];
                var px = run.FontSize.HasValue && run.FontSize.Value > 0 ? run.FontSize.Value : 13.3;
                var pt = (int)Math.Round(px * 0.75);
                if (_sizeCombo.Items.Contains(pt)) _sizeCombo.SelectedItem = pt;
                else
                {
                    // Valeur hors liste (taille libre) : affichée en texte.
                    _sizeCombo.SelectedIndex = -1;
                    _sizeCombo.Text = pt.ToString();
                }
                var setup = _project == null ? new PageSetup()
                    : (_book != null && _book.Book != null ? _book.Book.Template : _project.Page);
                _fontCombo.Select(run.FontFamily ?? setup.FooterFont ?? "Times New Roman");
            }
            finally
            {
                _syncingBar = false;
            }
        }

        private DispatcherTimer _commitTimer;
        private TextBox _pendingZone;

        private void ScheduleCommit(TextBox zone)
        {
            _pendingZone = zone;
            if (_commitTimer == null)
            {
                _commitTimer = new DispatcherTimer
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

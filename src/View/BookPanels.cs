using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>« Format d'impression » d'un livre (22/09 — l'ancien panneau
    /// Publication du rail, sans le bouton Publier) : le format des pages
    /// (appliqué à tout nouveau texte), les marges du gabarit, le fond perdu
    /// et l'alerte de divergence. Vit dans l'onglet Gabarits &amp; Format de
    /// la page livre. Le gabarit est ce que tout document du livre hérite.</summary>
    public class BookFormatPanel : StackPanel
    {
        private BinderItem _item;
        private Project _project;
        private bool _syncing;
        private TextBox _bleed;
        private ComboBox _preset;
        private TextBlock _templateSummary, _divergence;

        public event Action Changed;                 // gabarit édité

        // Preset gabarits: name, width, height (mm). Margins stay PAO
        // 20/20/30/20; the bleed is per-book.
        private static readonly object[][] Presets =
        {
            new object[] { "Roman (14 × 21,6 cm)", 140.0, 216.0 },
            new object[] { "Poche (11 × 18 cm)", 110.0, 180.0 },
            new object[] { "Grand format (15,3 × 24 cm)", 153.0, 240.0 },
            new object[] { "A5 (14,8 × 21 cm)", 148.0, 210.0 },
            new object[] { "A4 manuscrit (21 × 29,7 cm)", 210.0, 297.0 },
            new object[] { "Personnalisé…", 0.0, 0.0 }
        };

        public BookFormatPanel()
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal };

            var formatCol = new StackPanel { Margin = new Thickness(0, 0, 24, 0), Width = 260 };
            formatCol.Children.Add(BookPanelParts.Label("Format des pages (appliqué à tout nouveau texte) :"));
            _preset = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var preset in Presets) _preset.Items.Add((string)preset[0]);
            _preset.SelectionChanged += OnPresetChanged;
            formatCol.Children.Add(_preset);
            _templateSummary = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            formatCol.Children.Add(_templateSummary);
            row.Children.Add(formatCol);

            var marginsCol = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
            marginsCol.Children.Add(BookPanelParts.Label("Marges du gabarit :"));
            var marginsBtn = Buttons.Text("Marges du gabarit…", "Nomenclature PAO : de tête (haut), de pied (bas), "
                    + "petit fond (côté reliure), grand fond (côté extérieur)", Buttons.Compact, Buttons.Look.Outline);
            marginsBtn.HorizontalAlignment = HorizontalAlignment.Left;
            marginsBtn.Margin = new Thickness(0, 4, 0, 0);
            marginsBtn.Click += delegate { EditTemplateMargins(); };
            marginsCol.Children.Add(marginsBtn);
            row.Children.Add(marginsCol);

            var bleedCol = new StackPanel();
            bleedCol.Children.Add(BookPanelParts.Label("Fond perdu (mm) :"));
            _bleed = new TextBox
            {
                MaxWidth = 70,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _bleed.TextChanged += delegate
            {
                if (_syncing || _item == null) return;
                double value;
                var text = _bleed.Text.Trim().Replace(',', '.');
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && value >= 0)
                {
                    _item.Book.BleedMm = Math.Min(20, value);
                    RaiseChanged();
                }
            };
            bleedCol.Children.Add(_bleed);
            row.Children.Add(bleedCol);
            Children.Add(row);

            _divergence = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(230, 126, 34)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 2),
                Text = "Des documents ne suivent pas la mise en page du livre "
                    + "(icône orange à côté de leur titre → clic droit pour corriger)."
            };
            Children.Add(_divergence);
        }

        // ============================================================ gabarit

        private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing || _item == null || _preset.SelectedIndex < 0) return;
            var template = _item.Book.Template;
            if (_preset.SelectedIndex == Presets.Length - 1)
            {
                var values = NumbersDialog.Ask(Window.GetWindow(this), "Format du livre (cm)",
                    new[] { "Largeur", "Hauteur" },
                    new[] { template.PageWidthMm / 10, template.PageHeightMm / 10 }, 5, 100);
                if (values == null) { Sync(); return; }
                template.PageWidthMm = values[0] * 10;
                template.PageHeightMm = values[1] * 10;
            }
            else
            {
                template.PageWidthMm = (double)Presets[_preset.SelectedIndex][1];
                template.PageHeightMm = (double)Presets[_preset.SelectedIndex][2];
            }
            RaiseChanged();
            Sync();
        }

        private void EditTemplateMargins()
        {
            if (_item == null) return;
            var template = _item.Book.Template;
            var values = NumbersDialog.Ask(Window.GetWindow(this), "Marges du gabarit (cm)",
                new[]
                {
                    "De tête (marge haute)",
                    "De pied (marge basse)",
                    "Petit fond (côté reliure)",
                    "Grand fond (côté extérieur)"
                },
                new[]
                {
                    template.MarginTopMm / 10, template.MarginBottomMm / 10,
                    template.MarginLeftMm / 10, template.MarginRightMm / 10
                }, 0.5, 10);
            if (values == null) return;
            template.MarginTopMm = values[0] * 10;
            template.MarginBottomMm = values[1] * 10;
            template.MarginLeftMm = values[2] * 10;
            template.MarginRightMm = values[3] * 10;
            RaiseChanged();
            Sync();
        }

        /// <summary>Les textes du livre qui ne suivent pas sa mise en page.</summary>
        public static int DivergentCount(BinderItem book, Project project)
        {
            if (book == null || book.Book == null || project == null) return 0;
            return CountDivergent(book, book.Book.Template, project);
        }

        private static int CountDivergent(BinderItem item, PageSetup template, Project project)
        {
            var count = 0;
            foreach (var child in item.Children)
            {
                if (child.Kind == ItemKind.Text)
                {
                    var effective = child.Page ?? project.Page;
                    if (!effective.SameLayout(template)) count++;
                }
                count += CountDivergent(child, template, project);
            }
            return count;
        }

        // ============================================================ lifecycle

        public void Load(BinderItem book, Project project)
        {
            _item = book;
            _project = project;
            if (book != null && book.Book == null) book.Book = new BookInfo();
            Sync();
        }

        public void Clear() { _item = null; }

        public void Sync()
        {
            if (_item == null) return;
            _syncing = true;
            try
            {
                var book = _item.Book;
                _bleed.Text = book.BleedMm.ToString(CultureInfo.InvariantCulture);

                var index = Presets.Length - 1;
                for (var i = 0; i < Presets.Length - 1; i++)
                    if (Math.Abs(book.Template.PageWidthMm - (double)Presets[i][1]) < 0.05
                        && Math.Abs(book.Template.PageHeightMm - (double)Presets[i][2]) < 0.05)
                    { index = i; break; }
                _preset.SelectedIndex = index;

                _templateSummary.Text = string.Format(CultureInfo.CurrentCulture,
                    "{0} × {1} cm — marges {2}/{3}/{4}/{5} mm (tête/pied/petit fond/grand fond)",
                    book.Template.PageWidthMm / 10, book.Template.PageHeightMm / 10,
                    book.Template.MarginTopMm, book.Template.MarginBottomMm,
                    book.Template.MarginLeftMm, book.Template.MarginRightMm);
            }
            finally
            {
                _syncing = false;
            }
            _divergence.Visibility = DivergentCount(_item, _project) > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }

    internal static class BookPanelParts
    {
        public static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            };
        }

        /// <summary>Un intertitre de section de la page livre.</summary>
        public static TextBlock Caption(string text, double topMargin)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(0, topMargin, 0, 4)
            };
        }
    }
}

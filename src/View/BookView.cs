using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UniversSale.History;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>The Book view: the corkboard of its children (reading order)
    /// beside the metadata & gabarit panel, with the « Publier » action. The
    /// gabarit (page size, margins, bleed) is what every document of the book
    /// inherits.</summary>
    public class BookView : Grid
    {
        private readonly CorkboardView _corkboard;
        private readonly StackPanel _form;
        private BinderItem _item;
        private Project _project;
        private bool _syncing;

        private TextBox _subtitle, _author, _publisher, _collection, _isbn, _year, _bleed;
        private ComboBox _preset;
        private TextBlock _templateSummary, _divergence;

        public event Action<BinderItem> Navigate;
        public event Action Changed;              // metadata/gabarit edited
        public event Action<BinderItem> PublishRequested;
        public event Action<BinderItem> ExportRequested;      // relais corkboard
        public event Action<BinderItem> DeleteRequested;
        public event Action<System.Collections.Generic.List<BinderItem>> ApplyTemplateRequested;
        public event Action<BinderItem> NewTemplateRequested;    // book
        public event Action<BinderItem> ExportTemplateRequested; // gabarit
        public event Action<BinderItem> ImportTemplateRequested; // book
        public event Action<BinderItem> CopyTemplateRequested;   // gabarit
        public event Action<BinderItem, string> NewDocumentRequested; // livre, sorte extra

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

        public BookView()
        {
            Focusable = true; // reçoit le focus logique quand la vue s'affiche
            Background = Chrome.WindowBg;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

            _corkboard = new CorkboardView();
            _corkboard.Navigate += delegate(BinderItem item)
            {
                var handler = Navigate;
                if (handler != null) handler(item);
            };
            _corkboard.Changed += delegate { RaiseChanged(); SyncDivergence(); };
            _corkboard.ExportRequested += delegate(BinderItem item)
            { var h = ExportRequested; if (h != null) h(item); };
            _corkboard.DeleteRequested += delegate(BinderItem item)
            { var h = DeleteRequested; if (h != null) h(item); };
            _corkboard.ApplyTemplateRequested += delegate(System.Collections.Generic.List<BinderItem> items)
            { var h = ApplyTemplateRequested; if (h != null) h(items); };
            _corkboard.NewTemplateRequested += delegate(BinderItem book)
            { var h = NewTemplateRequested; if (h != null) h(book); };
            _corkboard.ImportTemplateRequested += delegate(BinderItem book)
            { var h = ImportTemplateRequested; if (h != null) h(book); };
            _corkboard.ExportTemplateRequested += delegate(BinderItem gabarit)
            { var h = ExportTemplateRequested; if (h != null) h(gabarit); };
            _corkboard.CopyTemplateRequested += delegate(BinderItem gabarit)
            { var h = CopyTemplateRequested; if (h != null) h(gabarit); };
            _corkboard.NewDocumentRequested += delegate(BinderItem book, string kind)
            { var h = NewDocumentRequested; if (h != null) h(book, kind); };
            SetColumn(_corkboard, 0);
            Children.Add(_corkboard);

            _form = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _form
            };
            var panel = new Border
            {
                Background = Chrome.PanelBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Child = scroll
            };
            SetColumn(panel, 1);
            Children.Add(panel);

            BuildForm();
        }

        // ============================================================ form

        private void BuildForm()
        {
            _form.Children.Add(Header("Métadonnées du livre"));
            _subtitle = Field("Sous-titre");
            _author = Field("Auteur (vide = auteur du projet)");
            _publisher = Field("Éditeur");
            _collection = Field("Collection");
            _isbn = Field("ISBN");
            _year = Field("Année");

            _form.Children.Add(Header("Gabarit"));
            _form.Children.Add(Label("Format des pages :"));
            _preset = new ComboBox { Margin = new Thickness(0, 2, 0, 6) };
            foreach (var preset in Presets) _preset.Items.Add((string)preset[0]);
            _preset.SelectionChanged += OnPresetChanged;
            _form.Children.Add(_preset);

            _templateSummary = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _form.Children.Add(_templateSummary);

            var marginsBtn = new Button
            {
                Content = "Marges du gabarit…",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 3, 8, 3),
                ToolTip = "Nomenclature PAO : de tête (haut), de pied (bas), "
                    + "petit fond (côté reliure), grand fond (côté extérieur)"
            };
            marginsBtn.Click += delegate { EditTemplateMargins(); };
            _form.Children.Add(marginsBtn);

            _form.Children.Add(Label("Fond perdu (mm) :"));
            _bleed = new TextBox { MaxWidth = 70, HorizontalAlignment = HorizontalAlignment.Left };
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
            _form.Children.Add(_bleed);

            _divergence = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(230, 126, 34)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 2),
                Text = "Des documents ne suivent pas la mise en page du livre "
                    + "(carte encadrée orange → clic droit pour corriger)."
            };
            _form.Children.Add(_divergence);

            _form.Children.Add(Header("Publication"));
            // NB : le gabarit de bouton du thème peint son propre fond — ne
            // jamais forcer Background/Foreground ici (bouton « tout blanc »).
            var publishRow = new StackPanel { Orientation = Orientation.Horizontal };
            var playIcon = Icons.Make("play-fill", 13, Chrome.Accent) as FrameworkElement;
            if (playIcon != null)
            {
                playIcon.VerticalAlignment = VerticalAlignment.Center;
                playIcon.Margin = new Thickness(0, 0, 7, 0);
                publishRow.Children.Add(playIcon);
            }
            publishRow.Children.Add(new TextBlock
            {
                Text = "Publier…",
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center
            });
            var publish = new Button
            {
                Content = publishRow,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(14, 5, 14, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                ToolTip = "Compiler tout le livre en un PDF prêt à imprimer : pagination "
                    + "continue, gabarits appliqués, CMJN FOGRA39 par défaut"
            };
            publish.Click += delegate
            {
                var handler = PublishRequested;
                if (handler != null && _item != null) handler(_item);
            };
            _form.Children.Add(publish);
        }

        private TextBlock Header(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 6)
            };
        }

        private TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            };
        }

        private TextBox Field(string label)
        {
            _form.Children.Add(Label(label));
            var box = new TextBox { Margin = new Thickness(0, 2, 0, 2) };
            box.TextChanged += delegate
            {
                if (_syncing || _item == null) return;
                PushFields();
                RaiseChanged();
            };
            _form.Children.Add(box);
            return box;
        }

        private void PushFields()
        {
            var book = _item.Book;
            book.Subtitle = _subtitle.Text;
            book.AuthorOverride = _author.Text;
            book.Publisher = _publisher.Text;
            book.Collection = _collection.Text;
            book.Isbn = _isbn.Text;
            book.Year = _year.Text;
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

        private bool HasDivergence()
        {
            if (_item == null || _project == null) return false;
            return DivergesRecursive(_item, _item.Book.Template);
        }

        private bool DivergesRecursive(BinderItem item, PageSetup template)
        {
            foreach (var child in item.Children)
            {
                if (child.Kind == ItemKind.Text)
                {
                    var effective = child.Page ?? _project.Page;
                    if (!effective.SameLayout(template)) return true;
                }
                if (DivergesRecursive(child, template)) return true;
            }
            return false;
        }

        // ============================================================ lifecycle

        public void Load(BinderItem book, HistoryManager history, Project project)
        {
            _item = book;
            _project = project;
            if (book.Book == null) book.Book = new BookInfo();
            _corkboard.Load(book, history, project);
            Sync();
        }

        public void Clear()
        {
            _item = null;
            _corkboard.Clear();
        }

        public bool ShowsItem(BinderItem item) { return _item == item; }

        /// <summary>Redessine les cartes du livre (état/couleur édités dans
        /// l'inspecteur pendant que la vue est affichée).</summary>
        public void RefreshCards()
        {
            _corkboard.Refresh();
        }

        private void Sync()
        {
            if (_item == null) return;
            _syncing = true;
            try
            {
                var book = _item.Book;
                _subtitle.Text = book.Subtitle;
                _author.Text = book.AuthorOverride;
                _publisher.Text = book.Publisher;
                _collection.Text = book.Collection;
                _isbn.Text = book.Isbn;
                _year.Text = book.Year;
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
            SyncDivergence();
        }

        private void SyncDivergence()
        {
            var divergent = HasDivergence();
            _divergence.Visibility = divergent ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

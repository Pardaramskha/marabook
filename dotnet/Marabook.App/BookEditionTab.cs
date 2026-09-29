using System;
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
    /// <summary>L'onglet « Édition » de la page livre (22/09) : les
    /// métadonnées (sous-titre, auteur, éditeur, collection, ISBN, année), la
    /// couverture (l'image du livre — la même que celle de sa tuile), puis la
    /// présentation (genre, public cible, thématiques, synopsis, accroche,
    /// quatrième de couverture), chacune dans son propre champ, sans
    /// accordéon. Chaque frappe modifie le modèle et lève Changed ; rien
    /// n'est resynchronisé pendant la frappe (le curseur resterait où il
    /// est). Remplace les panneaux Métadonnées et Édition du rail.</summary>
    public class BookEditionTab : StackPanel
    {
        private BinderItem _item;
        private Project _project;
        private bool _syncing;
        private TextBox _subtitle, _author, _publisher, _collection, _isbn, _year;
        private TextBox _genre, _audience, _synopsis, _pitch, _backCover, _themeBox;
        private WrapPanel _themeChips;
        private Image _coverImage;
        private Border _coverFrame;
        private Button _removeCover;
        private TextBlock _coverHint;

        public event Action Changed;
        /// <summary>La couverture : choisir une image (false) ou la retirer (true).</summary>
        public event Action<BinderItem, bool> CoverRequested;

        public BookEditionTab()
        {
            Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 24);
            MaxWidth = 760;
            HorizontalAlignment = HorizontalAlignment.Left;

            // — Identité.
            Children.Add(BookPanelParts.Caption("Identité", 0));
            var identity = new Grid();
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = new StackPanel();
            var right = new StackPanel();
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 2);
            identity.Children.Add(left);
            identity.Children.Add(right);
            _subtitle = LineField(left, "Sous-titre");
            _author = LineField(left, "Auteur·ice (vide = auteur·ice par défaut des Préférences)");
            _publisher = LineField(left, "Éditeur");
            _collection = LineField(right, "Collection");
            _isbn = LineField(right, "ISBN");
            _year = LineField(right, "Année");
            BuildColorField(right); // la couleur de la tuile du livre (29/09)
            Children.Add(identity);

            // — Couverture.
            Children.Add(BookPanelParts.Caption("Couverture", 16));
            var cover = new DockPanel();
            _coverFrame = new Border
            {
                Width = 96,
                Height = 128,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                Background = Chrome.PaperBg,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            _coverImage = new Image { Stretch = Stretch.UniformToFill };
            _coverFrame.Child = _coverImage;
            DockPanel.SetDock(_coverFrame, Dock.Left);
            cover.Children.Add(_coverFrame);
            var coverActions = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            _coverHint = new TextBlock
            {
                Text = "L'image du livre : sa tuile dans la Pile et sur les corkboards la montre aussi.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 8)
            };
            coverActions.Children.Add(_coverHint);
            var coverButtons = new StackPanel { Orientation = Orientation.Horizontal };
            var choose = Buttons.IconText("image-square-bold", "Choisir une image…", "PNG, JPEG, GIF ou BMP — 20 Mo au plus", Buttons.Bar, Buttons.Look.Outline);
            choose.Click += delegate { var h = CoverRequested; if (h != null && _item != null) h(_item, false); };
            coverButtons.Children.Add(choose);
            _removeCover = Buttons.Text("Retirer", "Retirer l'image du livre", Buttons.Bar, Buttons.Look.Calm);
            _removeCover.Margin = new Thickness(8, 0, 0, 0);
            _removeCover.Click += delegate { var h = CoverRequested; if (h != null && _item != null) h(_item, true); };
            coverButtons.Children.Add(_removeCover);
            coverActions.Children.Add(coverButtons);
            cover.Children.Add(coverActions);
            Children.Add(cover);

            // — Présentation.
            Children.Add(BookPanelParts.Caption("Présentation", 16));
            var presentation = new Grid();
            presentation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            presentation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            presentation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var pLeft = new StackPanel();
            var pRight = new StackPanel();
            Grid.SetColumn(pLeft, 0);
            Grid.SetColumn(pRight, 2);
            presentation.Children.Add(pLeft);
            presentation.Children.Add(pRight);
            _genre = LineField(pLeft, "Genre");
            _audience = LineField(pRight, "Public cible");
            Children.Add(presentation);
            Children.Add(BookPanelParts.Label("Thématiques (une virgule ou Entrée pose une étiquette)"));
            Children.Add(BuildThemes());
            _synopsis = AreaField("Synopsis", 110);
            _pitch = AreaField("Accroche — la manière dont vous présenteriez votre livre en salon", 72);
            _backCover = AreaField("Quatrième de couverture", 150);
        }

        // --------------------------------------------------------- champs

        private Button _colorButton;
        private Border _colorDot;
        private TextBlock _colorText;

        /// <summary>« Couleur de la tuile » (29/09) : la pastille et le nom de la
        /// couleur du livre, le nuancier partagé au clic — la même couleur
        /// que le bouton du Général et le menu des tuiles.</summary>
        private void BuildColorField(Panel host)
        {
            host.Children.Add(BookPanelParts.Label("Couleur de la tuile"));
            _colorDot = ColorMenus.Dot(null, 1, Chrome.Border);
            _colorText = new TextBlock { Text = "Aucune couleur", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(_colorDot);
            content.Children.Add(_colorText);
            _colorButton = new Button
            {
                Content = content,
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                [ToolTip.TipProperty] = "La couleur de la carte du livre dans Écrits et sur l'Accueil"
            };
            _colorButton.Click += delegate
            {
                if (_item == null) return;
                var menu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = _colorButton };
                ColorMenus.Fill(menu, Ui.OwnerOf(this), _project, _item.CardColor, delegate(string value)
                {
                    _item.CardColor = value;
                    RefreshColorField();
                    RaiseChanged();
                });
                Ui.ShowMenu(menu, _colorButton);
            };
            host.Children.Add(_colorButton);
        }

        private void RefreshColorField()
        {
            if (_colorDot == null || _item == null) return;
            var value = _item.CardColor;
            _colorDot.Background = value == null ? Brushes.Transparent : new SolidColorBrush(Ink.Parse(value).ToColor());
            _colorText.Text = value ?? "Aucune couleur";
        }

        private TextBox LineField(Panel host, string label)
        {
            host.Children.Add(BookPanelParts.Label(label));
            var box = new TextBox { Margin = new Thickness(0, 2, 0, 2) };
            box.TextChanged += delegate { OnFieldEdited(); };
            host.Children.Add(box);
            return box;
        }

        private TextBox AreaField(string label, double height)
        {
            Children.Add(BookPanelParts.Label(label));
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                Height = height,
                VerticalContentAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 2)
            };
            box.TextChanged += delegate { OnFieldEdited(); };
            Children.Add(box);
            return box;
        }

        private void OnFieldEdited()
        {
            if (_syncing || _item == null) return;
            var book = _item.Book;
            book.Subtitle = _subtitle.Text;
            book.AuthorOverride = _author.Text;
            book.Publisher = _publisher.Text;
            book.Collection = _collection.Text;
            book.Isbn = _isbn.Text;
            book.Year = _year.Text;
            book.Genre = _genre.Text;
            book.Audience = _audience.Text;
            _item.Synopsis = _synopsis.Text;
            book.Pitch = _pitch.Text;
            book.BackCover = _backCover.Text;
            RaiseChanged();
        }

        // ----------------------------------------------------- thématiques

        private Control BuildThemes()
        {
            var panel = new StackPanel();
            _themeChips = new WrapPanel();
            panel.Children.Add(_themeChips);
            _themeBox = new TextBox
            {
                Margin = new Thickness(0, 2, 0, 2),
                [ToolTip.TipProperty] = "Une virgule (ou Entrée) transforme le texte en étiquette"
            };
            _themeBox.TextChanged += delegate { HarvestThemes(false); };
            _themeBox.KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Return) { HarvestThemes(true); e.Handled = true; } };
            _themeBox.LostFocus += delegate { HarvestThemes(true); };
            panel.Children.Add(_themeBox);
            return panel;
        }

        /// <summary>Transforme en étiquettes ce qui précède chaque virgule du
        /// champ de saisie (tout le champ si all), sans doublon.</summary>
        private void HarvestThemes(bool all)
        {
            if (_syncing || _item == null) return;
            var text = _themeBox.Text;
            if (!all && text.IndexOf(',') < 0) return;
            var parts = text.Split(',');
            var keep = all ? "" : parts[parts.Length - 1];
            var taken = all ? parts.Length : parts.Length - 1;
            var added = false;
            for (var i = 0; i < taken; i++)
            {
                var theme = parts[i].Trim();
                if (theme.Length == 0) continue;
                var duplicate = false;
                foreach (var existing in _item.Book.Themes)
                    if (string.Equals(existing, theme, StringComparison.CurrentCultureIgnoreCase))
                    { duplicate = true; break; }
                if (duplicate) continue;
                _item.Book.Themes.Add(theme);
                added = true;
            }
            _syncing = true;
            try { _themeBox.Text = keep; _themeBox.CaretIndex = keep.Length; }
            finally { _syncing = false; }
            RebuildChips();
            if (added || all) RaiseChanged();
        }

        private void RebuildChips()
        {
            _themeChips.Children.Clear();
            if (_item == null) return;
            foreach (var theme in _item.Book.Themes)
            {
                var name = theme;
                var chip = new Border
                {
                    Background = Chrome.AccentTint,
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2, 4, 2),
                    Margin = new Thickness(0, 2, 4, 2)
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock
                {
                    Text = name,
                    Foreground = Chrome.Ink,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var remove = new TextBlock
                {
                    Text = "✕",
                    Foreground = Chrome.SoftText,
                    FontSize = 10,
                    Margin = new Thickness(5, 0, 2, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    [ToolTip.TipProperty] = "Retirer cette thématique"
                };
                remove.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) {
                    if (_item == null) return;
                    _item.Book.Themes.Remove(name);
                    RebuildChips();
                    RaiseChanged();
                };
                row.Children.Add(remove);
                chip.Child = row;
                _themeChips.Children.Add(chip);
            }
        }

        // ------------------------------------------------------- lifecycle

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
                _subtitle.Text = book.Subtitle;
                _author.Text = book.AuthorOverride;
                _publisher.Text = book.Publisher;
                _collection.Text = book.Collection;
                _isbn.Text = book.Isbn;
                _year.Text = book.Year;
                RefreshColorField();
                _genre.Text = book.Genre;
                _audience.Text = book.Audience;
                _synopsis.Text = _item.Synopsis;
                _pitch.Text = book.Pitch;
                _backCover.Text = book.BackCover;
                _themeBox.Text = "";
            }
            finally { _syncing = false; }
            RebuildChips();
            RefreshCover();
        }

        /// <summary>L'image du livre (après « Choisir » ou « Retirer »).</summary>
        public void RefreshCover()
        {
            var image = _item == null || _project == null ? null : _project.FindImage(_item.ImageId);
            Avalonia.Media.Imaging.Bitmap source = null;
            if (image != null)
            {
                try { source = MediaView.TryImage(image.Bytes, 200); } catch { source = null; }
            }
            _coverImage.Source = source;
            _removeCover.IsVisible = source != null ? true : false;
            _coverFrame.Background = source != null ? Brushes.Transparent : (IBrush)Chrome.PaperBg;
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

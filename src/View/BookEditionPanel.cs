using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Le panneau « Édition » d'un livre (batch 43) : genre, public
    /// cible, thématiques (étiquettes), synopsis (sorti de l'onglet Général),
    /// accroche et quatrième de couverture — en accordéon, un volet déplié à
    /// la fois. Chaque frappe modifie le modèle et lève Changed.</summary>
    public class BookEditionPanel : StackPanel
    {
        private BinderItem _item;
        private bool _syncing;
        private TextBox _genre, _audience, _synopsis, _pitch, _backCover, _themeBox;
        private WrapPanel _themeChips;
        private readonly System.Collections.Generic.List<ToggleButton> _headers =
            new System.Collections.Generic.List<ToggleButton>();
        private readonly System.Collections.Generic.List<UIElement> _bodies =
            new System.Collections.Generic.List<UIElement>();

        public event Action Changed;

        public BookEditionPanel()
        {
            _genre = LineField();
            Section("Genre", _genre, null);
            _audience = LineField();
            Section("Public cible", _audience, null);
            Section("Thématiques", BuildThemes(), null);
            _synopsis = AreaField(110);
            Section("Synopsis", _synopsis, null);
            _pitch = AreaField(72);
            Section("Accroche", _pitch, "La manière dont vous présenteriez votre livre en salon.");
            _backCover = AreaField(150);
            Section("Quatrième de couverture", _backCover, null);
            Expand(0);
        }

        // ------------------------------------------------------- accordéon

        private void Section(string title, UIElement body, string hint)
        {
            var index = _headers.Count;
            var header = new ToggleButton
            {
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, index == 0 ? 0 : 4, 0, 0),
                Padding = new Thickness(8, 4, 8, 4)
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            if (hint != null)
                row.Children.Add(new TextBlock
                {
                    Text = "ⓘ",
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = hint
                });
            header.Content = row;
            header.Checked += delegate { Expand(index); };
            header.Unchecked += delegate
            {
                // Ne jamais tout replier depuis le bouton déplié : il reste ouvert.
                if (!_syncing) header.IsChecked = true;
            };
            var host = body as FrameworkElement;
            if (host != null) host.Margin = new Thickness(0, 4, 0, 2);
            _headers.Add(header);
            _bodies.Add(body);
            Children.Add(header);
            Children.Add(body);
        }

        /// <summary>Déplie le volet demandé, replie les autres.</summary>
        private void Expand(int index)
        {
            _syncing = true;
            try
            {
                for (var i = 0; i < _headers.Count; i++)
                {
                    _headers[i].IsChecked = i == index;
                    _bodies[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            finally { _syncing = false; }
        }

        // --------------------------------------------------------- champs

        private TextBox LineField()
        {
            var box = new TextBox();
            box.TextChanged += delegate { OnFieldEdited(); };
            return box;
        }

        private TextBox AreaField(double height)
        {
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = height,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            box.TextChanged += delegate { OnFieldEdited(); };
            return box;
        }

        private void OnFieldEdited()
        {
            if (_syncing || _item == null) return;
            var book = _item.Book;
            book.Genre = _genre.Text;
            book.Audience = _audience.Text;
            _item.Synopsis = _synopsis.Text;
            book.Pitch = _pitch.Text;
            book.BackCover = _backCover.Text;
            RaiseChanged();
        }

        // ----------------------------------------------------- thématiques

        private UIElement BuildThemes()
        {
            var panel = new StackPanel();
            _themeChips = new WrapPanel();
            panel.Children.Add(_themeChips);
            _themeBox = new TextBox
            {
                Margin = new Thickness(0, 2, 0, 0),
                ToolTip = "Une virgule (ou Entrée) transforme le texte en étiquette"
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
                    Cursor = Cursors.Hand,
                    ToolTip = "Retirer cette thématique"
                };
                remove.MouseLeftButtonUp += delegate
                {
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

        public void Load(BinderItem book)
        {
            _item = book;
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
                _genre.Text = book.Genre;
                _audience.Text = book.Audience;
                _synopsis.Text = _item.Synopsis;
                _pitch.Text = book.Pitch;
                _backCover.Text = book.BackCover;
                _themeBox.Text = "";
            }
            finally { _syncing = false; }
            RebuildChips();
        }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

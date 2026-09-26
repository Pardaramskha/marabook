using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Marabook.Settings;

namespace Marabook.View
{
    /// <summary>Préférences › « Catalogue de polices » (0.50.0) : chaque police
    /// installée avec un exemple dans sa police (la phrase de narration
    /// française par défaut, ou le texte tapé au-dessus de la liste, sans
    /// aucune mise en forme automatique), son nombre de styles, et deux
    /// options conservées entre les projets : FAVORI (un doublon en tête de
    /// cette liste et de tous les sélecteurs de police) et EXCLURE (la police
    /// disparaît des sélecteurs). Boutons à droite de chaque ligne, et clic
    /// droit. L'en-tête compte favoris et exclusions.</summary>
    public sealed class FontCatalogTab : DockPanel
    {
        public const string DefaultSample = "J’ai marché jusqu’à – la « fin » – des temps";

        private readonly TextBlock _counts;
        private readonly ListBox _list;
        private readonly ObservableCollection<FontRow> _rows = new ObservableCollection<FontRow>();
        private readonly ContextMenu _menu;
        private readonly MenuItem _menuFavorite, _menuExclude;
        private string _sample = DefaultSample;

        /// <summary>Une ligne du catalogue : la police, ou le doublon d'une
        /// favorite en tête. Ses propriétés se relisent dans les réglages.</summary>
        public sealed class FontRow : INotifyPropertyChanged
        {
            private readonly FontCatalogTab _owner;
            private int _styles = -1;

            public FontRow(FontCatalogTab owner, FontCatalog.Entry entry, bool isCopy)
            {
                _owner = owner;
                Entry = entry;
                IsCopy = isCopy;
            }

            public FontCatalog.Entry Entry { get; private set; }
            public bool IsCopy { get; private set; }
            public string Name { get { return Entry.Name; } }
            public FontFamily Family { get { return Entry.Family; } }
            public string Badge { get { return IsCopy ? "★" : ""; } }
            public string Sample { get { return _owner._sample; } }
            public bool IsFavorite { get { return AppSettings.IsFavoriteFont(Name); } }
            public bool IsExcluded { get { return AppSettings.IsExcludedFont(Name); } }
            public double RowOpacity { get { return IsExcluded ? 0.45 : 1.0; } }

            /// <summary>« 4 styles » — lu à l'affichage de la ligne (la lecture
            /// des fichiers de police n'a lieu que pour les lignes visibles).</summary>
            public string StylesLabel
            {
                get
                {
                    if (_styles < 0) _styles = FontCatalog.StyleCount(Family);
                    return _styles <= 1 ? "1 style" : _styles + " styles";
                }
            }

            public object FavoriteIcon { get { return Icons.Make(IsFavorite ? "star-fill" : "star", 15, IsFavorite ? Chrome.Accent : Chrome.Ink); } }
            public object ExcludeIcon { get { return Icons.Make(IsExcluded ? "include" : "exclude", 15, IsExcluded ? Chrome.Accent : Chrome.Ink); } }
            public string FavoriteTip { get { return IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris — un doublon en tête des sélecteurs de police"; } }
            public string ExcludeTip { get { return IsExcluded ? "Ne plus exclure — la police revient dans les sélecteurs" : "Exclure — la police disparaît des sélecteurs de police"; } }

            public event PropertyChangedEventHandler PropertyChanged;

            public void Refresh()
            {
                var handler = PropertyChanged;
                if (handler == null) return;
                foreach (var name in new[] { "Sample", "IsFavorite", "IsExcluded", "RowOpacity", "FavoriteIcon", "ExcludeIcon", "FavoriteTip", "ExcludeTip" })
                    handler(this, new PropertyChangedEventArgs(name));
            }
        }

        public FontCatalogTab()
        {
            Margin = new Thickness(16, 12, 16, 12);
            LastChildFill = true;

            var header = new StackPanel();
            SetDock(header, Dock.Top);
            Children.Add(header);
            header.Children.Add(new TextBlock
            {
                Text = "Catalogue de polices",
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13
            });
            header.Children.Add(new TextBlock
            {
                Text = "Chaque police installée, avec un exemple. Une favorite est doublée en tête des sélecteurs de police ; "
                    + "une police exclue n'y paraît plus. Ces choix valent pour tous les projets.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });
            _counts = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(_counts);

            var sampleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = true };
            var sampleLabel = new TextBlock
            {
                Text = "Texte d'exemple",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            SetDock(sampleLabel, Dock.Left);
            sampleRow.Children.Add(sampleLabel);
            var sampleBox = new TextBox
            {
                Text = DefaultSample,
                ToolTip = "Remplace la phrase d'exemple sous chaque police — tel quel, sans mise en forme automatique "
                    + "(collez vos glyphes) ; vide = la phrase par défaut"
            };
            sampleBox.TextChanged += delegate
            {
                _sample = sampleBox.Text.Length > 0 ? sampleBox.Text : DefaultSample;
                foreach (var row in _rows) row.Refresh();
            };
            sampleRow.Children.Add(sampleBox);
            header.Children.Add(sampleRow);

            // Le clic droit : les mêmes options que les boutons, libellées
            // selon l'état de la police visée.
            _menu = new ContextMenu();
            _menuFavorite = new MenuItem();
            _menuFavorite.Click += delegate { var row = MenuRow(); if (row != null) ToggleFavorite(row); };
            _menuExclude = new MenuItem();
            _menuExclude.Click += delegate { var row = MenuRow(); if (row != null) ToggleExclude(row); };
            _menu.Items.Add(_menuFavorite);
            _menu.Items.Add(_menuExclude);
            _menu.Opened += delegate
            {
                var row = MenuRow();
                if (row == null) return;
                _menuFavorite.Header = row.IsFavorite ? "Retirer des favoris" : "Favoris";
                _menuExclude.Header = row.IsExcluded ? "Ne plus exclure" : "Exclure";
            };

            _list = new ListBox
            {
                Height = 430,
                ItemsSource = _rows,
                ItemTemplate = BuildRowTemplate(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1)
            };
            ScrollViewer.SetCanContentScroll(_list, true);
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            VirtualizingStackPanel.SetIsVirtualizing(_list, true);
            Children.Add(_list);

            RebuildRows();
            Loaded += delegate { FontCatalog.Changed += OnCatalogChanged; };
            Unloaded += delegate { FontCatalog.Changed -= OnCatalogChanged; };
        }

        private FontRow MenuRow()
        {
            var target = _menu.PlacementTarget as FrameworkElement;
            return target == null ? null : target.DataContext as FontRow;
        }

        private void OnCatalogChanged()
        {
            Dispatcher.BeginInvoke(new Action(RebuildRows));
        }

        /// <summary>Les lignes : les doublons des favorites en tête (dans
        /// l'ordre où elles ont été choisies), puis tout le catalogue.</summary>
        private void RebuildRows()
        {
            _rows.Clear();
            foreach (var name in AppSettings.FavoriteFonts)
            {
                var entry = FontCatalog.Find(name);
                if (entry != null) _rows.Add(new FontRow(this, entry, true));
            }
            foreach (var entry in FontCatalog.Entries) _rows.Add(new FontRow(this, entry, false));
            RefreshCounts();
        }

        private void RefreshCounts()
        {
            var favorites = 0;
            var excluded = 0;
            foreach (var entry in FontCatalog.Entries)
            {
                if (AppSettings.IsFavoriteFont(entry.Name)) favorites++;
                if (AppSettings.IsExcludedFont(entry.Name)) excluded++;
            }
            _counts.Text = FontCatalog.Entries.Count + " polices · " + Plural(favorites, "favori", "favoris")
                + " · " + Plural(excluded, "exclusion", "exclusions");
        }

        private static string Plural(int count, string one, string many)
        {
            return count + " " + (count <= 1 ? one : many);
        }

        /// <summary>Le nombre de favoris et d'exclusions affiché (sondes).</summary>
        public string CountsText { get { return _counts.Text; } }

        private void ToggleFavorite(FontRow row)
        {
            var favorite = AppSettings.ToggleFavoriteFont(row.Name);
            if (favorite)
            {
                // Le doublon prend sa place en tête, après les favorites déjà là.
                var index = 0;
                while (index < _rows.Count && _rows[index].IsCopy) index++;
                _rows.Insert(index, new FontRow(this, row.Entry, true));
            }
            else
            {
                for (var i = _rows.Count - 1; i >= 0; i--)
                    if (_rows[i].IsCopy && string.Equals(_rows[i].Name, row.Name, StringComparison.OrdinalIgnoreCase))
                        _rows.RemoveAt(i);
            }
            foreach (var other in _rows)
                if (string.Equals(other.Name, row.Name, StringComparison.OrdinalIgnoreCase)) other.Refresh();
            RefreshCounts();
        }

        private void ToggleExclude(FontRow row)
        {
            AppSettings.ToggleExcludedFont(row.Name);
            foreach (var other in _rows)
                if (string.Equals(other.Name, row.Name, StringComparison.OrdinalIgnoreCase)) other.Refresh();
            RefreshCounts();
        }

        private static FontRow RowOf(object sender)
        {
            var element = sender as FrameworkElement;
            return element == null ? null : element.DataContext as FontRow;
        }

        // ------------------------------------------------------------ la ligne

        private const double RowHeight = 54;

        private DataTemplate BuildRowTemplate()
        {
            var row = new FrameworkElementFactory(typeof(DockPanel));
            row.SetValue(HeightProperty, RowHeight);
            row.SetValue(LastChildFillProperty, true);
            row.SetValue(ContextMenuProperty, _menu);
            row.SetValue(BackgroundProperty, Brushes.Transparent); // toute la ligne reçoit le clic droit
            row.SetBinding(OpacityProperty, new Binding("RowOpacity"));

            var buttons = new FrameworkElementFactory(typeof(StackPanel));
            buttons.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            buttons.SetValue(DockProperty, Dock.Right);
            buttons.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            buttons.SetValue(MarginProperty, new Thickness(8, 0, 4, 0));
            buttons.AppendChild(IconButtonFactory("FavoriteIcon", "FavoriteTip", "favorite",
                new RoutedEventHandler(delegate(object sender, RoutedEventArgs e) { var r = RowOf(sender); if (r != null) ToggleFavorite(r); })));
            buttons.AppendChild(IconButtonFactory("ExcludeIcon", "ExcludeTip", "exclude",
                new RoutedEventHandler(delegate(object sender, RoutedEventArgs e) { var r = RowOf(sender); if (r != null) ToggleExclude(r); })));
            row.AppendChild(buttons);

            var lines = new FrameworkElementFactory(typeof(StackPanel));
            lines.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            lines.SetValue(MarginProperty, new Thickness(4, 0, 0, 0));
            var title = new FrameworkElementFactory(typeof(StackPanel));
            title.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var badge = new FrameworkElementFactory(typeof(TextBlock));
            badge.SetBinding(TextBlock.TextProperty, new Binding("Badge"));
            badge.SetValue(TextBlock.ForegroundProperty, Chrome.Accent);
            badge.SetValue(TextBlock.FontSizeProperty, 11.0);
            badge.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            badge.SetValue(MarginProperty, new Thickness(0, 0, 3, 0));
            title.AppendChild(badge);
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            name.SetValue(TextBlock.ForegroundProperty, Chrome.Ink);
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.FontSizeProperty, 13.0);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            title.AppendChild(name);
            var styles = new FrameworkElementFactory(typeof(TextBlock));
            styles.SetBinding(TextBlock.TextProperty, new Binding("StylesLabel"));
            styles.SetValue(TextBlock.ForegroundProperty, Chrome.FaintText);
            styles.SetValue(TextBlock.FontSizeProperty, 11.0);
            styles.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            styles.SetValue(MarginProperty, new Thickness(8, 1, 0, 0));
            title.AppendChild(styles);
            lines.AppendChild(title);
            var sample = new FrameworkElementFactory(typeof(TextBlock));
            sample.SetBinding(TextBlock.TextProperty, new Binding("Sample"));
            sample.SetBinding(TextBlock.FontFamilyProperty, new Binding("Family"));
            sample.SetValue(TextBlock.FontSizeProperty, 17.0);
            sample.SetValue(TextBlock.ForegroundProperty, Chrome.PaperInk);
            sample.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            sample.SetValue(HeightProperty, 26.0);
            sample.SetValue(UIElement.ClipToBoundsProperty, true);
            sample.SetValue(MarginProperty, new Thickness(0, 1, 0, 0));
            lines.AppendChild(sample);
            row.AppendChild(lines);
            return new DataTemplate { VisualTree = row };
        }

        private static FrameworkElementFactory IconButtonFactory(string iconProperty, string tipProperty, string tag, RoutedEventHandler onClick)
        {
            var button = new FrameworkElementFactory(typeof(Button));
            button.SetBinding(ContentControl.ContentProperty, new Binding(iconProperty));
            button.SetBinding(ToolTipProperty, new Binding(tipProperty));
            button.SetValue(WidthProperty, 28.0);
            button.SetValue(HeightProperty, 28.0);
            button.SetValue(Control.PaddingProperty, new Thickness(0));
            button.SetValue(MarginProperty, new Thickness(2, 0, 2, 0));
            button.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            button.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            button.SetValue(FocusableProperty, false);
            button.SetValue(TagProperty, tag);
            button.AddHandler(ButtonBase.ClickEvent, onClick);
            return button;
        }
    }
}

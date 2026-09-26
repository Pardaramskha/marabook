using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Marabook.Settings;

namespace Marabook.View
{
    /// <summary>LE sélecteur de police (0.50.0), partagé par le ruban,
    /// l'éditeur de styles, le séparateur et les gabarits :
    ///   • les 5 dernières polices employées, un trait, puis toutes les
    ///     polices par ordre alphabétique (FontCatalog) ;
    ///   • chaque rangée montre le nom et « Marabook » écrit dans la police ;
    ///   • éditable : on tape un nom (l'autocomplétion suit), Entrée
    ///     applique — la frappe elle-même n'applique rien et ne rend pas le
    ///     clavier (avant, la première lettre appliquait et le reste de la
    ///     frappe partait dans le texte) ;
    ///   • flèches haut/bas sans ouvrir la liste : la police change à chaque
    ///     pas, aperçu vivant (FontChosen avec preview = vrai), le clavier
    ///     reste ici ;
    ///   • un choix dans la liste ou Entrée : FontChosen avec preview = faux,
    ///     la police entre dans les récentes.
    /// ShowMixed vide le champ quand la sélection mêle plusieurs polices.</summary>
    public sealed class FontPicker : ComboBox
    {
        public const int RecentCount = 5;

        /// <summary>(nom, aperçu) — aperçu = flèches sans ouvrir la liste :
        /// appliquer sans reprendre le clavier ; sinon choix définitif.</summary>
        public event Action<string, bool> FontChosen;

        private static readonly FontCatalog.Entry Separator = new FontCatalog.Entry { Name = "" };

        private bool _syncing, _arrowNav;
        private object _openedWith;
        private bool _announcedWhileOpen;

        public FontPicker()
        {
            // Le style implicite du thème (bords arrondis, popup, flèche…) est
            // clé sur typeof(ComboBox) : une classe dérivée ne le reçoit pas
            // d'elle-même — référence dynamique, qui suit aussi le passage
            // clair/sombre (correctif 0.50.0).
            SetResourceReference(StyleProperty, typeof(ComboBox));
            IsEditable = true;
            MaxDropDownHeight = 440;
            ItemTemplateSelector = new RowTemplateSelector();
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
            Rebuild(null);

            SelectionChanged += OnSelectionChangedInternal;
            PreviewKeyDown += OnPreviewKeyDownInternal;
            DropDownOpened += delegate
            {
                _openedWith = SelectedItem;
                _announcedWhileOpen = false;
                Rebuild(SelectedFontName); // les récentes du moment
            };
            DropDownClosed += delegate
            {
                // Un choix à la souris ou au clavier dans la liste ouverte :
                // définitif à la fermeture (la sélection a bougé).
                if (_syncing) return;
                if (SelectedItem != null && SelectedItem != _openedWith) Announce(SelectedFontName, false);
                else if (_announcedWhileOpen) Announce(SelectedFontName, false);
            };
            Loaded += delegate
            {
                FontCatalog.Changed += OnCatalogChanged;
                AppSettings.FontPrefsChanged += OnCatalogChanged; // favorites / exclues (0.50.0)
            };
            Unloaded += delegate
            {
                FontCatalog.Changed -= OnCatalogChanged;
                AppSettings.FontPrefsChanged -= OnCatalogChanged;
            };
        }

        /// <summary>Le nom de la police montrée : l'entrée choisie, sinon ce
        /// qui est tapé ; null si rien (sélection mixte).</summary>
        public string SelectedFontName
        {
            get
            {
                var entry = SelectedItem as FontCatalog.Entry;
                if (entry != null && !entry.IsSeparator) return entry.Name;
                var typed = (Text ?? "").Trim();
                return typed.Length > 0 ? typed : null;
            }
        }

        /// <summary>Montre cette police sans rien annoncer (synchro depuis le
        /// caret ou un style). Un nom hors catalogue s'affiche en texte.</summary>
        public void Select(string name)
        {
            _syncing = true;
            try
            {
                var entry = FindEntry(name);
                SelectedItem = entry;
                if (entry == null) Text = name ?? "";
            }
            finally { _syncing = false; }
        }

        /// <summary>Sélection mixte : le champ se vide.</summary>
        public void ShowMixed()
        {
            _syncing = true;
            try
            {
                SelectedItem = null;
                Text = "";
            }
            finally { _syncing = false; }
        }

        // ------------------------------------------------------------ liste

        private void OnCatalogChanged()
        {
            Dispatcher.BeginInvoke(new Action(delegate { Rebuild(SelectedFontName); }));
        }

        /// <summary>Favorites, trait, récentes, trait, tout le catalogue sans
        /// les exclues (FontCatalog.Arrange) — en gardant la police montrée.
        /// Favorites et récentes sont des copies : un même objet deux fois
        /// dans Items rendrait SelectedItem ambigu.</summary>
        private void Rebuild(string keep)
        {
            var wasSyncing = _syncing;
            _syncing = true;
            try
            {
                Items.Clear();
                foreach (var entry in FontCatalog.Arrange(FontCatalog.Entries, AppSettings.FavoriteFonts,
                    AppSettings.RecentFonts, AppSettings.ExcludedFonts, RecentCount))
                    Items.Add(entry.IsSeparator ? Separator : entry);
                if (keep != null)
                {
                    var entry = FindEntry(keep);
                    SelectedItem = entry;
                    if (entry == null) Text = keep;
                }
            }
            finally { _syncing = wasSyncing; }
        }

        /// <summary>L'entrée de ce nom dans la partie alphabétique (les objets
        /// du catalogue eux-mêmes ; les récentes sont des copies).</summary>
        private FontCatalog.Entry FindEntry(string name)
        {
            var entry = FontCatalog.Find(name);
            return entry != null && Items.Contains(entry) ? entry : null;
        }

        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);
            var entry = item as FontCatalog.Entry;
            var container = element as ComboBoxItem;
            if (entry != null && entry.IsSeparator && container != null)
            {
                container.IsEnabled = false;
                container.Focusable = false;
                container.Padding = new Thickness(0);
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            // La liste défile par éléments (virtualisation) : quelques
            // centaines de rangées, chacune dans sa police, ne se réalisent
            // qu'à l'affichage.
            var popup = FindChild<Popup>(this);
            if (popup != null && popup.Child != null)
            {
                var scroller = FindChild<ScrollViewer>(popup.Child);
                if (scroller != null) scroller.CanContentScroll = true;
            }
        }

        private static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var typed = child as T;
                if (typed != null) return typed;
                var deeper = FindChild<T>(child);
                if (deeper != null) return deeper;
            }
            var content = root as ContentControl;
            if (content != null && content.Content is DependencyObject) return FindChild<T>((DependencyObject)content.Content);
            var decorator = root as Decorator;
            if (decorator != null && decorator.Child != null) return FindChild<T>(decorator.Child);
            return null;
        }

        // ------------------------------------------------------------ choix

        private void OnPreviewKeyDownInternal(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                // Le nom tapé (ou complété) s'applique ; le clavier repart.
                e.Handled = true;
                if (IsDropDownOpen) IsDropDownOpen = false;
                var name = SelectedFontName;
                if (name != null) Announce(name, false);
                return;
            }
            if ((e.Key == Key.Up || e.Key == Key.Down) && !IsDropDownOpen)
                _arrowNav = true; // parcours sans ouvrir : aperçu vivant
        }

        private void OnSelectionChangedInternal(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            var entry = SelectedItem as FontCatalog.Entry;
            if (entry != null && entry.IsSeparator)
            {
                // Le trait n'est pas une police : on saute par-dessus dans le
                // sens du mouvement.
                var index = Items.IndexOf(entry);
                var previous = e.RemovedItems.Count > 0 ? Items.IndexOf(e.RemovedItems[0]) : -1;
                var target = previous >= 0 && previous > index ? index - 1 : index + 1;
                if (target >= 0 && target < Items.Count) SelectedIndex = target;
                return;
            }
            if (_arrowNav)
            {
                _arrowNav = false;
                Announce(SelectedFontName, true);
                return;
            }
            if (IsDropDownOpen)
            {
                // Flèches dans la liste ouverte : aperçu ; la fermeture
                // tranche (DropDownClosed).
                _announcedWhileOpen = true;
                Announce(SelectedFontName, true);
                return;
            }
            // Liste fermée, clavier dans le champ : l'autocomplétion de la
            // frappe — rien, Entrée décidera. Sans clavier ici, c'est un
            // réglage par code (Select) qui passe par _syncing.
        }

        private void Announce(string name, bool preview)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!preview) AppSettings.NoteRecentFont(name);
            var handler = FontChosen;
            if (handler != null) handler(name, preview);
        }

        // ------------------------------------------------------------ rangées

        private sealed class RowTemplateSelector : DataTemplateSelector
        {
            private readonly DataTemplate _row = BuildRow();
            private readonly DataTemplate _rule = BuildRule();

            public override DataTemplate SelectTemplate(object item, DependencyObject container)
            {
                var entry = item as FontCatalog.Entry;
                return entry != null && entry.IsSeparator ? _rule : _row;
            }
        }

        /// <summary>Le nom à gauche (police d'interface), « Marabook » à
        /// droite dans la police. Largeur FIXE (correctif 0.50.0) : une police
        /// large ou haute ne fait plus respirer la liste — l'exemple est rogné
        /// à droite et la rangée garde sa hauteur.</summary>
        public const double RowWidth = 330;
        private const double NameWidth = 170;
        private const double RowHeight = 24;

        private static DataTemplate BuildRow()
        {
            var row = new FrameworkElementFactory(typeof(DockPanel));
            row.SetValue(FrameworkElement.WidthProperty, RowWidth);
            row.SetValue(FrameworkElement.HeightProperty, RowHeight);
            row.SetValue(UIElement.ClipToBoundsProperty, true);
            row.SetValue(DockPanel.LastChildFillProperty, true);
            // L'étoile d'une favorite (0.50.0), devant le nom.
            var badge = new FrameworkElementFactory(typeof(TextBlock));
            badge.SetBinding(TextBlock.TextProperty, new Binding("Badge"));
            badge.SetValue(DockPanel.DockProperty, Dock.Left);
            badge.SetValue(TextBlock.ForegroundProperty, Chrome.Accent);
            badge.SetValue(TextBlock.FontSizeProperty, 11.0);
            badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            badge.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 3, 0));
            row.AppendChild(badge);
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            name.SetValue(DockPanel.DockProperty, Dock.Left);
            name.SetValue(FrameworkElement.WidthProperty, NameWidth);
            name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            row.AppendChild(name);
            var preview = new FrameworkElementFactory(typeof(TextBlock));
            preview.SetValue(TextBlock.TextProperty, "Marabook");
            preview.SetBinding(TextBlock.FontFamilyProperty, new Binding("Family"));
            preview.SetValue(TextBlock.FontSizeProperty, 15.0);
            preview.SetValue(TextBlock.ForegroundProperty, Chrome.SoftText);
            preview.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.None);
            preview.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            preview.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            preview.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 0, 0));
            preview.SetValue(UIElement.ClipToBoundsProperty, true);
            row.AppendChild(preview);
            return new DataTemplate { VisualTree = row };
        }

        private static DataTemplate BuildRule()
        {
            var rule = new FrameworkElementFactory(typeof(Border));
            rule.SetValue(FrameworkElement.HeightProperty, 1.0);
            rule.SetValue(Border.BackgroundProperty, Chrome.Border);
            rule.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 3, 8, 3));
            return new DataTemplate { VisualTree = rule };
        }
    }
}

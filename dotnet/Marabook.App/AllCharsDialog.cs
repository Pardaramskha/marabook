using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marabook.App
{
    /// <summary>LA FENÊTRE « TOUS LES CARACTÈRES » (1.1.0, 1/11, Rémi : « comme
    /// l'écran Caractères spéciaux de Word ») : à gauche la liste des
    /// sous-ensembles Unicode (lettres accentuées, latin, ponctuation,
    /// monnaies, flèches, mathématiques, formes, symboles, dingbats…), à
    /// droite la grille des carreaux du sous-ensemble choisi (construite à la
    /// sélection, au plus 256), en bas le caractère survolé en grand avec son
    /// nom et son code, et un champ de recherche par code hexadécimal ou par
    /// nom. Un carreau cliqué = le caractère est rendu et la fenêtre se
    /// referme ; « Insérer » rend celui de l'aperçu. Rend null si rien.</summary>
    public class AllCharsDialog : Window
    {
        /// <summary>Un sous-ensemble : une plage de points de code, ou une
        /// liste explicite de caractères (les lettres accentuées).</summary>
        private sealed class Subset
        {
            public string Name;
            public int From, To;
            public string Explicit;
            public Subset(string name, int from, int to) { Name = name; From = from; To = to; }
            public Subset(string name, string chars) { Name = name; Explicit = chars; }
        }

        private static readonly Subset[] Subsets =
        {
            new Subset("Lettres accentuées",
                "àáâãäåÀÁÂÃÄÅ" + "èéêëÈÉÊË" + "ìíîïÌÍÎÏ" + "òóôõöøÒÓÔÕÖØ" + "ùúûüÙÚÛÜ" + "ýÿÝŸ" + "çÇ" + "ñÑ" + "œŒæÆ" + "ß"),
            new Subset("Latin de base", 0x0020, 0x007E),
            new Subset("Latin-1 (accents et symboles)", 0x00A0, 0x00FF),
            new Subset("Latin étendu-A", 0x0100, 0x017F),
            new Subset("Latin étendu-B", 0x0180, 0x024F),
            new Subset("Grec", 0x0370, 0x03FF),
            new Subset("Cyrillique", 0x0400, 0x04FF),
            new Subset("Ponctuation générale", 0x2000, 0x206F),
            new Subset("Exposants et indices", 0x2070, 0x209F),
            new Subset("Symboles monétaires", 0x20A0, 0x20CF),
            new Subset("Symboles de type lettre", 0x2100, 0x214F),
            new Subset("Formes numérales", 0x2150, 0x218F),
            new Subset("Flèches", 0x2190, 0x21FF),
            new Subset("Opérateurs mathématiques", 0x2200, 0x22FF),
            new Subset("Filets et pavés", 0x2500, 0x259F),
            new Subset("Formes géométriques", 0x25A0, 0x25FF),
            new Subset("Symboles divers", 0x2600, 0x26FF),
            new Subset("Dingbats", 0x2700, 0x27BF)
        };

        private const int Columns = 16;
        private const int Rows = 8;
        private const int MaxTiles = 256;

        private readonly FontFamily _font;
        private readonly ListBox _list;
        private readonly WrapPanel _grid;
        private readonly ScrollViewer _scroll;
        private readonly TextBlock _bigGlyph, _nameLabel, _codeLabel, _emptyLabel;
        private readonly TextBox _search;
        private readonly Button _insert;
        private string _chosen;   // le caractère de l'aperçu (survolé, ou tapé en hexadécimal)
        private string _result;   // ce que la fenêtre rend

        private AllCharsDialog(Window owner, FontFamily font)
        {
            _font = font;
            Title = "Tous les caractères";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new StackPanel { Margin = new Thickness(16) };
            var gridHeight = Rows * SpecialCharsDrawer.Cell + 2 * SpecialCharsDrawer.TileGap;

            // ---- la liste des sous-ensembles, à gauche
            var main = new DockPanel();
            var left = new StackPanel { Width = 210, Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(left, Dock.Left);
            left.Children.Add(Caption("Sous-ensemble"));
            _list = new ListBox { Height = gridHeight, Background = Chrome.FieldBg };
            ToolTip.SetTip(_list, "Les plages Unicode : la grille de droite montre le sous-ensemble choisi");
            foreach (var subset in Subsets)
                _list.Items.Add(new ListBoxItem { Content = new TextBlock { Text = subset.Name, FontSize = 13 }, Tag = subset });
            _list.SelectionChanged += delegate { if (string.IsNullOrEmpty((_search.Text ?? "").Trim())) ShowSubset(SelectedSubset()); };
            left.Children.Add(_list);
            main.Children.Add(left);

            // ---- la grille, à droite
            var right = new StackPanel();
            right.Children.Add(Caption("Caractères"));
            _grid = SpecialCharsDrawer.TileGrid(Columns);
            _emptyLabel = new TextBlock
            {
                Text = "Aucun caractère ne correspond.",
                Foreground = Chrome.FaintText,
                FontSize = 12,
                Margin = new Thickness(8),
                IsVisible = false
            };
            var gridBody = new StackPanel { Margin = new Thickness(SpecialCharsDrawer.TileGap) };
            gridBody.Children.Add(_grid);
            gridBody.Children.Add(_emptyLabel);
            _scroll = new ScrollViewer
            {
                Content = gridBody,
                Height = gridHeight,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled
            };
            right.Children.Add(new Border
            {
                Background = Chrome.FieldBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Marabook.App.Theme.Radius), // « Theme » seul = la propriété de la fenêtre (1/11)
                ClipToBounds = true,
                Child = _scroll
            });
            main.Children.Add(right);
            root.Children.Add(main);

            // ---- l'aperçu et la recherche, en bas
            var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var searchBox = new StackPanel { Width = 230, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(searchBox, Dock.Right);
            _search = new TextBox { Watermark = "Code hexadécimal (00E9) ou nom" };
            ToolTip.SetTip(_search, "Un code Unicode en hexadécimal (00E9, U+2192…) ou un bout de nom (« cédille », « flèche »)");
            _search.TextChanged += delegate { ApplySearch(); };
            _search.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if ((e.Key == Key.Return || e.Key == Key.Enter) && _chosen != null) { Accept(_chosen); e.Handled = true; }
            };
            searchBox.Children.Add(_search);
            bottom.Children.Add(searchBox);

            var preview = new StackPanel { Orientation = Orientation.Horizontal };
            _bigGlyph = new TextBlock
            {
                FontSize = 26,
                Foreground = Chrome.Ink,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            preview.Children.Add(new Border
            {
                Width = 48,
                Height = 48,
                Background = Chrome.FieldBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Marabook.App.Theme.Radius),
                ClipToBounds = true,
                Child = _bigGlyph
            });
            var labels = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _nameLabel = new TextBlock { Text = "Survolez un carreau", Foreground = Chrome.Ink, FontSize = 13 };
            _codeLabel = new TextBlock { Text = "", Foreground = Chrome.SoftText, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
            labels.Children.Add(_nameLabel);
            labels.Children.Add(_codeLabel);
            preview.Children.Add(labels);
            bottom.Children.Add(preview);
            root.Children.Add(bottom);

            // ---- les boutons
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var close = new Button { Content = "Fermer", IsCancel = true, MinWidth = 90 };
            close.Click += delegate { Close(); }; // IsCancel ne ferme pas la fenêtre sur Avalonia (28/09)
            _insert = new Button { Content = "Insérer", IsDefault = true, MinWidth = 90, IsEnabled = false };
            ToolTip.SetTip(_insert, "Insère le caractère de l'aperçu (un carreau cliqué s'insère directement)");
            _insert.Click += delegate { if (_chosen != null) Accept(_chosen); };
            buttons.Children.Add(close);
            buttons.Children.Add(_insert);
            Dialogs.Arrange(buttons, _insert); // validation à droite, principale (30/09)
            root.Children.Add(buttons);

            Content = root;
            _list.SelectedIndex = 0;
            Opened += delegate { _search.Focus(); };
        }

        // ---------------------------------------------------------------- grille

        private Subset SelectedSubset()
        {
            var item = _list.SelectedItem as ListBoxItem;
            return item == null ? Subsets[0] : (Subset)item.Tag;
        }

        /// <summary>Les carreaux d'un sous-ensemble, construits à la demande :
        /// les contrôles, substituts, non assignés et séparateurs de ligne
        /// sont écartés (1/11).</summary>
        private void ShowSubset(Subset subset)
        {
            var entries = new List<SpecialCharsDrawer.Entry>();
            if (subset.Explicit != null)
            {
                foreach (var c in subset.Explicit) entries.Add(EntryOf(c.ToString()));
            }
            else
            {
                for (var code = subset.From; code <= subset.To && entries.Count < MaxTiles; code++)
                    if (Allowed(code)) entries.Add(EntryOf(char.ConvertFromUtf32(code)));
            }
            Fill(entries);
        }

        private void Fill(List<SpecialCharsDrawer.Entry> entries)
        {
            _grid.Children.Clear();
            foreach (var entry in entries)
            {
                var tile = SpecialCharsDrawer.Tile(entry, _font, Accept);
                var captured = entry;
                tile.PointerEntered += delegate { Choose(captured); };
                _grid.Children.Add(tile);
            }
            _emptyLabel.IsVisible = entries.Count == 0;
            _scroll.Offset = new Vector(0, 0);
        }

        private static SpecialCharsDrawer.Entry EntryOf(string text)
        {
            var known = SpecialCharsDrawer.Find(text);
            if (known != null) return known;
            var shown = SpecialCharsDrawer.ShownOf(text);
            return new SpecialCharsDrawer.Entry(text, SpecialCharsDrawer.NameOf(text), shown == text ? null : shown);
        }

        /// <summary>Ce qui a sa place sur un carreau : pas de contrôle, pas de
        /// substitut, pas de zone privée ni de point non assigné, pas de
        /// caractère de format (sauf le trait d'union conditionnel, montré
        /// « ¬ »), pas de séparateur de ligne ou de paragraphe.</summary>
        private static bool Allowed(int code)
        {
            if (code <= 0 || code > 0x10FFFF) return false;
            if (code >= 0xD800 && code <= 0xDFFF) return false;
            if (code == 0x00AD) return true;
            var category = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(code), 0);
            switch (category)
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.Surrogate:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- recherche

        /// <summary>Le champ du bas : vide = le sous-ensemble choisi ; un code
        /// hexadécimal (« 00E9 », « U+2192 ») = ce caractère seul, déjà dans
        /// l'aperçu ; sinon les caractères dont le nom contient le texte.</summary>
        private void ApplySearch()
        {
            var text = (_search.Text ?? "").Trim();
            if (text.Length == 0) { ShowSubset(SelectedSubset()); return; }
            var hex = text.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ? text.Substring(2) : text;
            int code;
            if (hex.Length >= 2 && hex.Length <= 6
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
            {
                var entries = new List<SpecialCharsDrawer.Entry>();
                if (Allowed(code)) entries.Add(EntryOf(char.ConvertFromUtf32(code)));
                Fill(entries);
                if (entries.Count > 0) Choose(entries[0]);
                return;
            }
            Fill(SpecialCharsDrawer.Search(text, MaxTiles));
        }

        // ---------------------------------------------------------------- aperçu

        private void Choose(SpecialCharsDrawer.Entry entry)
        {
            _chosen = entry.Text;
            _bigGlyph.Text = entry.Shown ?? entry.Text;
            _bigGlyph.Foreground = entry.Shown != null ? Chrome.SoftText : Chrome.Ink;
            _bigGlyph.FontFamily = _font != null && entry.Shown == null ? _font : FontFamily.Default;
            _nameLabel.Text = entry.Name ?? "Caractère";
            _codeLabel.Text = SpecialCharsDrawer.CodeOf(entry.Text);
            _insert.IsEnabled = true;
        }

        private void Accept(string text)
        {
            _result = text;
            Close();
        }

        // ---------------------------------------------------------------- aides

        private static TextBlock Caption(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        }

        /// <summary>Ouvre la fenêtre et rend le caractère choisi, ou null.</summary>
        public static async Task<string> Ask(Window owner, FontFamily font)
        {
            var dialog = new AllCharsDialog(owner, font);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }
    }
}

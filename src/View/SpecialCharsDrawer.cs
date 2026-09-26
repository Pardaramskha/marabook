using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Marabook.Settings;

namespace Marabook.View
{
    /// <summary>Le tiroir des caractères spéciaux (0.50.0), sous le bouton du
    /// ruban : les derniers insérés d'abord, puis la ponctuation
    /// typographique, les espaces, les lettres, les symboles et les flèches —
    /// à la manière de la galerie de symboles de Word. Chaque carreau montre
    /// le caractère dans la police courante ; un clic l'insère et referme.</summary>
    public static class SpecialCharsDrawer
    {
        private sealed class Entry
        {
            public string Text, Name, Shown;
            public Entry(string text, string name, string shown = null) { Text = text; Name = name; Shown = shown; }
        }

        private static readonly Entry[] Punctuation =
        {
            new Entry("«", "Guillemet ouvrant"), new Entry("»", "Guillemet fermant"),
            new Entry("“", "Guillemet anglais ouvrant"), new Entry("”", "Guillemet anglais fermant"),
            new Entry("‘", "Apostrophe ouvrante"), new Entry("’", "Apostrophe typographique"),
            new Entry("‚", "Virgule basse"), new Entry("„", "Guillemet bas double"),
            new Entry("–", "Tiret demi-cadratin"), new Entry("—", "Tiret cadratin"),
            new Entry("…", "Points de suspension"), new Entry("·", "Point médian"),
            new Entry("•", "Puce"), new Entry("¡", "Point d'exclamation renversé"),
            new Entry("¿", "Point d'interrogation renversé"), new Entry("§", "Paragraphe"),
            new Entry("¶", "Pied-de-mouche"), new Entry("†", "Croix"), new Entry("‡", "Double croix"),
            new Entry("′", "Prime"), new Entry("″", "Double prime"), new Entry("‹", "Guillemet simple ouvrant"),
            new Entry("›", "Guillemet simple fermant"), new Entry("‰", "Pour mille")
        };

        private static readonly Entry[] Spaces =
        {
            new Entry(" ", "Espace insécable", "⍽"), new Entry(" ", "Espace fine insécable", "⸱"),
            new Entry(" ", "Espace fine", "˽"), new Entry(" ", "Espace demi-cadratin", "␣"),
            new Entry(" ", "Espace cadratin", "⎵"), new Entry("‑", "Trait d'union insécable", "‑"),
            new Entry("­", "Trait d'union conditionnel", "¬")
        };

        private static readonly Entry[] Letters =
        {
            new Entry("æ", "e dans l'a"), new Entry("Æ", "E dans l'A"), new Entry("œ", "e dans l'o"), new Entry("Œ", "E dans l'O"),
            new Entry("ß", "Eszett"), new Entry("ø", "o barré"), new Entry("Ø", "O barré"), new Entry("å", "a rond en chef"),
            new Entry("Å", "A rond en chef"), new Entry("ñ", "n tilde"), new Entry("Ñ", "N tilde"), new Entry("ÿ", "y tréma"),
            new Entry("Ÿ", "Y tréma"), new Entry("ª", "Indicateur ordinal féminin"), new Entry("º", "Indicateur ordinal masculin"),
            new Entry("ð", "Eth"), new Entry("þ", "Thorn"), new Entry("ł", "l barré")
        };

        private static readonly Entry[] Symbols =
        {
            new Entry("©", "Copyright"), new Entry("®", "Marque déposée"), new Entry("™", "Marque commerciale"),
            new Entry("°", "Degré"), new Entry("±", "Plus ou moins"), new Entry("×", "Multiplication"), new Entry("÷", "Division"),
            new Entry("≠", "Différent"), new Entry("≈", "Environ égal"), new Entry("≤", "Inférieur ou égal"), new Entry("≥", "Supérieur ou égal"),
            new Entry("∞", "Infini"), new Entry("€", "Euro"), new Entry("£", "Livre"), new Entry("¥", "Yen"), new Entry("¢", "Cent"),
            new Entry("№", "Numéro"), new Entry("½", "Un demi"), new Entry("¼", "Un quart"), new Entry("¾", "Trois quarts"),
            new Entry("★", "Étoile"), new Entry("☆", "Étoile vide"), new Entry("♪", "Note de musique"), new Entry("☞", "Index pointant")
        };

        private static readonly Entry[] Arrows =
        {
            new Entry("←", "Flèche gauche"), new Entry("→", "Flèche droite"), new Entry("↑", "Flèche haut"), new Entry("↓", "Flèche bas"),
            new Entry("↔", "Flèche gauche-droite"), new Entry("⇐", "Double flèche gauche"), new Entry("⇒", "Double flèche droite"),
            new Entry("⇔", "Double flèche gauche-droite"), new Entry("↩", "Retour"), new Entry("↪", "Renvoi")
        };

        /// <summary>Construit le tiroir : un Popup à poser sous son bouton. La
        /// police d'aperçu est demandée à l'ouverture (celle du caret).</summary>
        public static Popup Build(UIElement anchor, Func<FontFamily> previewFont, Action<string> insert)
        {
            var popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
                VerticalOffset = 4
            };
            popup.Opened += delegate { popup.Child = BuildContent(previewFont == null ? null : previewFont(), delegate(string text)
            {
                popup.IsOpen = false;
                AppSettings.NoteSpecialChar(text);
                insert(text);
            }); };
            return popup;
        }

        private static UIElement BuildContent(FontFamily font, Action<string> insert)
        {
            var body = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
            if (AppSettings.RecentSpecialChars.Count > 0)
            {
                var recents = new List<Entry>();
                foreach (var text in AppSettings.RecentSpecialChars)
                {
                    var known = Find(text);
                    recents.Add(known ?? new Entry(text, "U+" + ((int)text[0]).ToString("X4")));
                }
                AddSection(body, "Récents", recents, font, insert);
            }
            AddSection(body, "Ponctuation", Punctuation, font, insert);
            AddSection(body, "Espaces", Spaces, font, insert);
            AddSection(body, "Lettres", Letters, font, insert);
            AddSection(body, "Symboles", Symbols, font, insert);
            AddSection(body, "Flèches", Arrows, font, insert);

            // L'ombre sur un cadre vide dessous (le piège des papers), la carte
            // arrondie par-dessus.
            var card = new Grid { Margin = new Thickness(8, 4, 8, 10), UseLayoutRounding = true };
            card.Children.Add(new Border
            {
                Background = Chrome.RaisedBg,
                CornerRadius = new CornerRadius(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = 0.22, BlurRadius = 10, ShadowDepth = 2 }
            });
            card.Children.Add(new Border
            {
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = new ScrollViewer
                {
                    Content = body,
                    MaxHeight = 420,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                }
            });
            return card;
        }

        private static Entry Find(string text)
        {
            foreach (var group in new[] { Punctuation, Spaces, Letters, Symbols, Arrows })
                foreach (var entry in group)
                    if (entry.Text == text) return entry;
            return null;
        }

        private static void AddSection(Panel body, string title, IEnumerable<Entry> entries, FontFamily font, Action<string> insert)
        {
            body.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Chrome.SoftText,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, body.Children.Count == 0 ? 0 : 10, 0, 4)
            });
            var grid = new WrapPanel { Width = 12 * 30, Orientation = Orientation.Horizontal };
            foreach (var entry in entries)
            {
                var shown = entry.Shown ?? entry.Text;
                var glyph = new TextBlock
                {
                    Text = shown,
                    FontSize = 15,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = entry.Shown != null ? Chrome.SoftText : Chrome.Ink
                };
                if (font != null && entry.Shown == null) glyph.FontFamily = font;
                var tile = new Button
                {
                    Content = glyph,
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(1),
                    Padding = new Thickness(0),
                    Focusable = false,
                    ToolTip = entry.Name + " (U+" + ((int)entry.Text[0]).ToString("X4") + ")"
                };
                var text = entry.Text;
                tile.Click += delegate { insert(text); };
                grid.Children.Add(tile);
            }
            body.Children.Add(grid);
        }
    }
}

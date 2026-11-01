using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>Le tiroir des caractères spéciaux (0.50.0, refondu le 1/11,
    /// Rémi : « garde la fenêtre avec les propositions les plus fréquemment
    /// utilisées puis rajoute l'option Tous les caractères ») : sous le bouton
    /// du ruban, les derniers insérés, puis trois rangées des caractères les
    /// plus utiles en français, et en bas « Tous les caractères… » qui ouvre
    /// la fenêtre <see cref="AllCharsDialog"/>, à la manière de l'écran de
    /// Word. Chaque carreau montre le caractère dans la police courante ; un
    /// clic l'insère et referme.</summary>
    public static class SpecialCharsDrawer
    {
        /// <summary>Un caractère nommé : le texte inséré, son nom lisible et,
        /// pour les invisibles (espaces, trait d'union conditionnel), le glyphe
        /// de substitution montré sur le carreau.</summary>
        internal sealed class Entry
        {
            public string Text, Name, Shown;
            public Entry(string text, string name, string shown = null) { Text = text; Name = name; Shown = shown; }
        }

        /// <summary>La taille d'un carreau et sa marge : partagées avec la
        /// fenêtre pour que les deux grilles se ressemblent (1/11).</summary>
        internal const double TileSize = 30;
        internal const double TileGap = 2;
        internal const double Cell = TileSize + 2 * TileGap;

        private const int Columns = 8;

        /// <summary>LA PETITE VINGTAINE (1/11) : 24 caractères en trois rangées
        /// de huit — guillemets et apostrophes, tirets, points de suspension,
        /// les deux espaces insécables et le trait d'union insécable, les
        /// ligatures, puis les symboles courants.</summary>
        private static readonly Entry[] Frequent =
        {
            new Entry("«", "Guillemet ouvrant"), new Entry("»", "Guillemet fermant"),
            new Entry("“", "Guillemet anglais ouvrant"), new Entry("”", "Guillemet anglais fermant"),
            new Entry("‘", "Apostrophe ouvrante"), new Entry("’", "Apostrophe typographique"),
            new Entry("–", "Tiret demi-cadratin"), new Entry("—", "Tiret cadratin"),

            new Entry("…", "Points de suspension"),
            new Entry(" ", "Espace insécable", "⍽"), new Entry(" ", "Espace fine insécable", "⸱"),
            new Entry("‑", "Trait d'union insécable", "‑"),
            new Entry("œ", "e dans l'o"), new Entry("Œ", "E dans l'O"), new Entry("æ", "e dans l'a"), new Entry("Æ", "E dans l'A"),

            new Entry("©", "Copyright"), new Entry("°", "Degré"), new Entry("€", "Euro"), new Entry("→", "Flèche droite"),
            new Entry("•", "Puce"), new Entry("§", "Paragraphe"), new Entry("×", "Multiplication"), new Entry("½", "Un demi")
        };

        /// <summary>Les autres caractères dont on connaît le nom (l'ancien
        /// tiroir, 0.50.0) : servent aux infobulles des récents et de la
        /// fenêtre, et à la recherche par nom.</summary>
        private static readonly Entry[] Known =
        {
            new Entry("‚", "Virgule basse"), new Entry("„", "Guillemet bas double"),
            new Entry("·", "Point médian"), new Entry("¡", "Point d'exclamation renversé"),
            new Entry("¿", "Point d'interrogation renversé"), new Entry("¶", "Pied-de-mouche"),
            new Entry("†", "Croix"), new Entry("‡", "Double croix"),
            new Entry("′", "Prime"), new Entry("″", "Double prime"), new Entry("‹", "Guillemet simple ouvrant"),
            new Entry("›", "Guillemet simple fermant"), new Entry("‰", "Pour mille"),
            new Entry(" ", "Espace fine", "˽"), new Entry(" ", "Espace demi-cadratin", "␣"),
            new Entry(" ", "Espace cadratin", "⎵"), new Entry("­", "Trait d'union conditionnel", "¬"),
            new Entry(" ", "Espace", "␣"),
            new Entry("ß", "Eszett"), new Entry("ø", "o barré"), new Entry("Ø", "O barré"),
            new Entry("ª", "Indicateur ordinal féminin"), new Entry("º", "Indicateur ordinal masculin"),
            new Entry("ð", "Eth"), new Entry("þ", "Thorn"), new Entry("ł", "l barré"), new Entry("Ł", "L barré"),
            new Entry("®", "Marque déposée"), new Entry("™", "Marque commerciale"),
            new Entry("±", "Plus ou moins"), new Entry("÷", "Division"),
            new Entry("≠", "Différent"), new Entry("≈", "Environ égal"), new Entry("≤", "Inférieur ou égal"), new Entry("≥", "Supérieur ou égal"),
            new Entry("∞", "Infini"), new Entry("£", "Livre"), new Entry("¥", "Yen"), new Entry("¢", "Cent"), new Entry("$", "Dollar"),
            new Entry("№", "Numéro"), new Entry("¼", "Un quart"), new Entry("¾", "Trois quarts"),
            new Entry("★", "Étoile"), new Entry("☆", "Étoile vide"), new Entry("♪", "Note de musique"), new Entry("☞", "Index pointant"),
            new Entry("←", "Flèche gauche"), new Entry("↑", "Flèche haut"), new Entry("↓", "Flèche bas"),
            new Entry("↔", "Flèche gauche-droite"), new Entry("⇐", "Double flèche gauche"), new Entry("⇒", "Double flèche droite"),
            new Entry("⇔", "Double flèche gauche-droite"), new Entry("↩", "Retour"), new Entry("↪", "Renvoi"),
            new Entry("µ", "Micro"), new Entry("¬", "Négation"), new Entry("¦", "Barre brisée"), new Entry("¨", "Tréma"),
            new Entry("¯", "Macron"), new Entry("´", "Accent aigu"), new Entry("¸", "Cédille"), new Entry("¤", "Symbole monétaire"),
            new Entry("¹", "Exposant un"), new Entry("²", "Exposant deux"), new Entry("³", "Exposant trois"),
            new Entry("√", "Racine carrée"), new Entry("∑", "Somme"), new Entry("∏", "Produit"), new Entry("∂", "Dérivée partielle"),
            new Entry("∫", "Intégrale"), new Entry("−", "Moins"), new Entry("∅", "Ensemble vide"),
            new Entry("■", "Carré plein"), new Entry("□", "Carré vide"), new Entry("●", "Rond plein"), new Entry("○", "Rond vide"),
            new Entry("▲", "Triangle plein"), new Entry("△", "Triangle vide"), new Entry("◆", "Losange plein"), new Entry("◇", "Losange vide"),
            new Entry("✓", "Coche"), new Entry("✔", "Coche grasse"), new Entry("✗", "Croix de refus"), new Entry("✘", "Croix de refus grasse"),
            new Entry("❤", "Cœur"), new Entry("☀", "Soleil"), new Entry("☁", "Nuage"), new Entry("☂", "Parapluie"), new Entry("☃", "Bonhomme de neige"),
            new Entry("☎", "Téléphone"), new Entry("☕", "Tasse"), new Entry("♀", "Féminin"), new Entry("♂", "Masculin"),
            new Entry("♠", "Pique"), new Entry("♣", "Trèfle"), new Entry("♥", "Cœur (carte)"), new Entry("♦", "Carreau"),
            new Entry("Ω", "Oméga majuscule"), new Entry("π", "Pi"), new Entry("α", "Alpha"), new Entry("β", "Bêta"), new Entry("γ", "Gamma"), new Entry("δ", "Delta"),
            new Entry("λ", "Lambda"), new Entry("μ", "Mu"), new Entry("σ", "Sigma"), new Entry("φ", "Phi"), new Entry("ω", "Oméga")
        };

        /// <summary>Les noms des signes diacritiques combinants (décomposition
        /// NFD) : « é » → « e accent aigu », « Ç » → « C cédille » (1/11).</summary>
        private static readonly Dictionary<char, string> AccentNames = new Dictionary<char, string>
        {
            { '̀', "accent grave" }, { '́', "accent aigu" }, { '̂', "accent circonflexe" },
            { '̃', "tilde" }, { '̄', "macron" }, { '̆', "brève" }, { '̇', "point suscrit" },
            { '̈', "tréma" }, { '̊', "rond en chef" }, { '̋', "double accent aigu" },
            { '̌', "caron" }, { '̧', "cédille" }, { '̨', "ogonek" }
        };

        // ---------------------------------------------------------------- noms

        internal static Entry Find(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (var entry in Frequent) if (entry.Text == text) return entry;
            foreach (var entry in Known) if (entry.Text == text) return entry;
            return null;
        }

        /// <summary>Le nom lisible d'un caractère : connu dans les tables,
        /// sinon composé pour une lettre accentuée, sinon null.</summary>
        internal static string NameOf(string text)
        {
            var known = Find(text);
            if (known != null) return known.Name;
            if (string.IsNullOrEmpty(text) || text.Length != 1 || !char.IsLetter(text[0])) return null;
            var parts = text.Normalize(NormalizationForm.FormD);
            if (parts.Length < 2 || parts[0] > 0x7F) return null;
            var name = new StringBuilder();
            name.Append(parts[0]);
            for (var i = 1; i < parts.Length; i++)
            {
                string accent;
                if (!AccentNames.TryGetValue(parts[i], out accent)) return null;
                name.Append(' ').Append(accent);
            }
            return name.ToString();
        }

        /// <summary>Le glyphe à montrer sur un carreau : le substitut des
        /// invisibles (espaces, trait d'union conditionnel), le caractère
        /// lui-même sinon.</summary>
        internal static string ShownOf(string text)
        {
            var known = Find(text);
            if (known != null && known.Shown != null) return known.Shown;
            if (string.IsNullOrEmpty(text)) return "";
            var category = CharUnicodeInfo.GetUnicodeCategory(text, 0);
            if (category == UnicodeCategory.SpaceSeparator) return "␣";
            if (category == UnicodeCategory.Format) return "¬";
            if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark) return "◌" + text;
            return text;
        }

        /// <summary>« U+00E9 » : le point de code, paire de substitution comprise.</summary>
        internal static string CodeOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var code = char.IsSurrogatePair(text, 0) ? char.ConvertToUtf32(text, 0) : text[0];
            return "U+" + code.ToString("X4");
        }

        /// <summary>L'infobulle d'un carreau : le nom quand on le connaît, le
        /// code toujours.</summary>
        internal static string TipOf(string text)
        {
            var name = NameOf(text);
            return name == null ? CodeOf(text) : name + " (" + CodeOf(text) + ")";
        }

        /// <summary>La recherche par nom de la fenêtre (1/11) : les tables,
        /// puis les lettres accentuées du latin (U+00C0–U+017F) dont le nom
        /// est composé ; au plus <paramref name="max"/> résultats.</summary>
        internal static List<Entry> Search(string needle, int max)
        {
            var found = new List<Entry>();
            if (string.IsNullOrEmpty(needle)) return found;
            var seen = new HashSet<string>();
            foreach (var table in new[] { Frequent, Known })
                foreach (var entry in table)
                {
                    if (found.Count >= max) return found;
                    if (Matches(entry.Name, needle) && seen.Add(entry.Text)) found.Add(entry);
                }
            for (var code = 0xC0; code <= 0x17F && found.Count < max; code++)
            {
                var text = ((char)code).ToString();
                if (seen.Contains(text)) continue;
                var name = NameOf(text);
                if (name != null && Matches(name, needle)) { seen.Add(text); found.Add(new Entry(text, name)); }
            }
            return found;
        }

        private static bool Matches(string name, string needle)
        {
            return CultureInfo.CurrentCulture.CompareInfo.IndexOf(name, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        }

        // ---------------------------------------------------------------- tiroir

        /// <summary>Construit le tiroir : un Popup à poser sous son bouton. La
        /// police d'aperçu est demandée à l'ouverture (celle du caret).</summary>
        public static Popup Build(Control anchor, Func<FontFamily> previewFont, Action<string> insert)
        {
            var popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                IsLightDismissEnabled = true,
                VerticalOffset = 4
            };
            // Le Popup prend le bouton pour parent LOGIQUE (1/11, Rémi : « une
            // nouvelle fenêtre blanche, infobulles sans style, pas de survol ») :
            // sans parent, sa racine n'hérite pas des styles de la fenêtre —
            // fond blanc, boutons et infobulles nus. Comme le menu des suggestions.
            try { ((ISetLogicalParent)popup).SetParent(anchor); } catch (Exception) { }
            // Le contenu est bâti AVANT la première ouverture (1/11, Rémi : « ne
            // s'ouvre qu'au second clic » — un Popup sans enfant ne se montre
            // pas), puis rebâti à chaque ouverture (récents, police du caret).
            Action refresh = delegate
            {
                var font = previewFont == null ? null : previewFont();
                popup.Child = BuildContent(font,
                    delegate(string text)
                    {
                        popup.IsOpen = false;
                        AppSettings.NoteSpecialChar(text);
                        insert(text);
                    },
                    delegate
                    {
                        // « Tous les caractères… » (1/11) : le tiroir se referme,
                        // la fenêtre s'ouvre au tour suivant du Dispatcher, une
                        // fois le Popup vraiment fermé.
                        popup.IsOpen = false;
                        var owner = TopLevel.GetTopLevel(anchor) as Window;
                        Dispatcher.UIThread.Post(delegate { OpenAll(owner, font, insert); });
                    });
            };
            refresh();
            popup.Opened += delegate { refresh(); };
            return popup;
        }

        private static async void OpenAll(Window owner, FontFamily font, Action<string> insert)
        {
            string text;
            try { text = await AllCharsDialog.Ask(owner, font); }
            catch (Exception) { text = null; }
            if (string.IsNullOrEmpty(text)) return;
            AppSettings.NoteSpecialChar(text);
            insert(text);
        }

        private static Control BuildContent(FontFamily font, Action<string> insert, Action openAll)
        {
            var width = Columns * Cell;
            var body = new StackPanel { Margin = new Thickness(12, 10, 12, 12), Width = width };
            if (AppSettings.RecentSpecialChars.Count > 0)
            {
                var recents = new List<Entry>();
                foreach (var text in AppSettings.RecentSpecialChars)
                    recents.Add(Find(text) ?? new Entry(text, NameOf(text), ShownOf(text) == text ? null : ShownOf(text)));
                AddSection(body, "Récents", recents, font, insert);
            }
            AddSection(body, "Fréquents", Frequent, font, insert);

            // Le filet puis « Tous les caractères… », sur toute la largeur.
            body.Children.Add(new Border
            {
                Height = 1,
                Background = Chrome.Border,
                Margin = new Thickness(0, 12, 0, 10)
            });
            var all = Buttons.Text("Tous les caractères…", "Ouvre la fenêtre de tous les caractères Unicode, par sous-ensemble", Buttons.Compact, Buttons.Look.Calm);
            all.HorizontalAlignment = HorizontalAlignment.Stretch;
            all.HorizontalContentAlignment = HorizontalAlignment.Center;
            all.Click += delegate { openAll(); };
            body.Children.Add(all);

            // Une seule carte, ombre portée dessus (1/11, Rémi : le cadre vide
            // d'ombre dessous se voyait comme une seconde surface) ; la marge
            // laisse la place au flou.
            return new Border
            {
                Margin = new Thickness(12, 4, 12, 14),
                Background = Chrome.RaisedBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 2, Blur = 10, Color = Color.FromArgb(0x38, 0, 0, 0) }),
                Child = body
            };
        }

        private static void AddSection(Panel body, string title, IEnumerable<Entry> entries, FontFamily font, Action<string> insert)
        {
            body.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Chrome.SoftText,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(TileGap, body.Children.Count == 0 ? 0 : 10, 0, 4)
            });
            var grid = TileGrid(Columns);
            foreach (var entry in entries) grid.Children.Add(Tile(entry, font, insert));
            body.Children.Add(grid);
        }

        // ---------------------------------------------------------------- carreaux

        /// <summary>Une grille de carreaux : largeur = colonnes × cellule, les
        /// carreaux se rangent sans reste (1/11).</summary>
        internal static WrapPanel TileGrid(int columns)
        {
            return new WrapPanel { Width = columns * Cell, Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        }

        /// <summary>Un carreau : le glyphe centré dans la police d'aperçu (les
        /// substituts des invisibles en texte atténué, police de l'interface),
        /// rogné à ses bords pour qu'aucun glyphe ne déborde.</summary>
        internal static Button Tile(Entry entry, FontFamily font, Action<string> click)
        {
            var shown = entry.Shown ?? entry.Text;
            var glyph = new TextBlock
            {
                Text = shown,
                FontSize = 15,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = entry.Shown != null ? Chrome.SoftText : Chrome.Ink
            };
            if (font != null && entry.Shown == null) glyph.FontFamily = font;
            var tile = new Button
            {
                Content = glyph,
                Width = TileSize,
                Height = TileSize,
                Margin = new Thickness(TileGap),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                ClipToBounds = true,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), // la main au survol (1/11)
                Focusable = false,
                [ToolTip.TipProperty] = entry.Name == null ? CodeOf(entry.Text) : entry.Name + " (" + CodeOf(entry.Text) + ")"
            };
            var text = entry.Text;
            tile.Click += delegate { click(text); };
            return tile;
        }
    }
}

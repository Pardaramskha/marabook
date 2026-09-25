using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>A named style — character, paragraph, hyphenation and
    /// justification attributes together, InDesign-style. What runs do not
    /// override, they inherit from here. Color null = automatic (theme ink).
    /// Lengths are WPF px (1 pt = 4/3 px; UI edits mm or pt); percentages are
    /// plain numbers (100 = 100 %). WPF renders what it can (fonts, indents,
    /// leading, ligatures, hyphenation on/off); the fine hyphenation and
    /// justification numbers drive the docx export where possible and the
    /// 4b print composer.</summary>
    public class ParagraphStyle
    {
        public const string ScopeGlobal = "global";     // Préférences › Styles globaux : tous les projets
        public const string ScopeBook = "book";         // l'onglet Styles d'un livre
        public const string ScopeDocument = "document"; // un seul écrit

        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Style";

        // --- Portée (22/09, v28) ---
        public string Scope = ScopeGlobal;
        public string OwnerId;   // id du livre (book) ou de l'écrit (document) ; null en global
        // Un style SÉPARATEUR (22/09) : ce texte est inséré d'un clic depuis
        // le bouton du ruban, dans un paragraphe qui porte ce style. Null pour
        // un style de paragraphe ordinaire.
        public string Content;

        public bool IsSeparator { get { return Content != null; } }
        public bool IsGlobal { get { return Scope != ScopeBook && Scope != ScopeDocument; } }

        /// <summary>Vrai si ce style vaut pour l'écrit donné : global, du
        /// livre qui le contient, ou du document lui-même.</summary>
        public bool AppliesTo(BinderItem item)
        {
            if (IsGlobal) return true;
            if (item == null) return false;
            if (Scope == ScopeDocument) return OwnerId == item.Id;
            var book = item.Kind == ItemKind.Book ? item : item.EnclosingBook();
            return book != null && OwnerId == book.Id;
        }

        // --- Caractère ---
        public string FontFamily = "Times New Roman";
        public double FontSize = 16; // px: 16 px = 12 pt
        public bool Bold, Italic;
        public string Color;              // "#RRGGBB", null = automatic
        public bool Ligatures = true;
        public double LineHeight = 19.2;  // px: 14,4 pt — leading, InDesign-style

        // --- Paragraphe ---
        public string Align = "left";     // "left"|"center"|"right"|"justify"
        public double SpaceBefore, SpaceAfter;
        public double FirstLineIndent = 18.9; // 5 mm
        public double LeftIndent;
        public double RightIndent;
        public double LastLineIndent;     // retrait de dernière ligne (composer 4b)

        // --- Césure ---
        public bool HyphenationEnabled = true;
        public int HyphenMinWordLength = 5;
        public int HyphenMinBefore = 2;   // après les X premières lettres
        // L'usage français impose 3 lettres minimum rejetées à la ligne (les
        // fins en « -ce », « -re », « -te » sont fautives). Défaut porté de
        // 2 à 3 au batch 24 — les styles déjà PERSISTÉS gardent leur valeur :
        // la sérialisation garde 2 pour sentinelle (PlotFile écrit la clé
        // « hyphenAfter » dès que la valeur diffère de 2, et lit 2 en absence
        // de clé), un projet mis en page ne change donc jamais sous son auteur.
        public int HyphenMinAfter = 3;    // avant les X dernières lettres
        public int HyphenConsecutiveLimit = 3;

        // --- Justification (composer 4b ; percentages, 100 = 100 %) ---
        public double JustifyWordMin = 80, JustifyWordOpt = 100, JustifyWordMax = 115;
        public double JustifyLetterMin = 0, JustifyLetterOpt = 0, JustifyLetterMax = 0;
        public double JustifyGlyphMin = 100, JustifyGlyphOpt = 100, JustifyGlyphMax = 100;
        public double AutoLeadingPercent = 120;

        // --- Enchaînements (keeps) ---
        public bool KeepWithPrevious = true;   // solidaire avec le précédent
        public int KeepNextLines = 0;          // paragraphes solidaires : X lignes du suivant
        // Lignes solidaires : off par défaut — un paragraphe se coupe entre
        // deux pages au fil des lignes (veuves/orphelines contrôlées par le
        // compositeur), sans laisser de trou en bas de page.
        public bool KeepLinesTogether = false;

        public ParagraphStyle Clone()
        {
            return (ParagraphStyle)MemberwiseClone();
        }
    }

    /// <summary>The project's style sheet. "body" always exists and is the
    /// fallback for unknown style ids (e.g. a style deleted after use).</summary>
    public class StyleSheet
    {
        /// <summary>L'identifiant du séparateur de scène global : le
        /// paragraphe d'un séparateur porte toujours cet id, le livre peut le
        /// remplacer (EffectiveFor).</summary>
        public const string SeparatorId = "separator";

        /// <summary>L'identifiant du style des notes de bas de page (0.50.0) :
        /// un style de paragraphe ordinaire, éditable, que le compositeur et
        /// les exports emploient pour le corps des notes.</summary>
        public const string FootnoteId = "footnote";

        public List<ParagraphStyle> Styles = new List<ParagraphStyle>();

        public ParagraphStyle Body { get { return Find("body"); } }

        public ParagraphStyle Find(string id)
        {
            foreach (var style in Styles)
                if (style.Id == id) return style;
            foreach (var style in Styles)
                if (style.Id == "body") return style;
            return Styles.Count > 0 ? Styles[0] : null;
        }

        /// <summary>Les styles offerts à un écrit, séparateurs exclus :
        /// globaux, ceux de son livre, les siens.</summary>
        public List<ParagraphStyle> VisibleFor(BinderItem item)
        {
            var list = new List<ParagraphStyle>();
            foreach (var style in Styles)
                if (!style.IsSeparator && style.AppliesTo(item)) list.Add(style);
            return list;
        }

        /// <summary>Le séparateur de scène en vigueur pour un écrit : celui
        /// de son livre s'il en a un, sinon le global ; un séparateur de
        /// secours si le projet n'en a aucun.</summary>
        public ParagraphStyle SeparatorFor(BinderItem item)
        {
            var book = item == null ? null : item.Kind == ItemKind.Book ? item : item.EnclosingBook();
            if (book != null)
                foreach (var style in Styles)
                    if (style.IsSeparator && style.Scope == ParagraphStyle.ScopeBook && style.OwnerId == book.Id) return style;
            foreach (var style in Styles)
                if (style.IsSeparator && style.IsGlobal) return style;
            return DefaultSeparator();
        }

        /// <summary>La feuille telle qu'un écrit la voit : la même, sauf si
        /// son livre remplace le séparateur global — alors une copie où le
        /// séparateur du livre prend l'id « separator » (les paragraphes
        /// déjà insérés suivent le nouveau format).</summary>
        public StyleSheet EffectiveFor(BinderItem item)
        {
            var separator = SeparatorFor(item);
            if (separator.Scope != ParagraphStyle.ScopeBook) return this;
            var copy = new StyleSheet();
            foreach (var style in Styles)
            {
                if (style.Id == SeparatorId) continue;
                if (style == separator)
                {
                    var swapped = style.Clone();
                    swapped.Id = SeparatorId;
                    copy.Styles.Add(swapped);
                }
                else copy.Styles.Add(style);
            }
            return copy;
        }

        /// <summary>Le séparateur global, créé s'il manque (projets d'avant
        /// la v28 : depuis les anciens réglages du projet).</summary>
        public ParagraphStyle EnsureSeparator(string text, string font, double sizePt)
        {
            foreach (var style in Styles)
                if (style.IsSeparator && style.IsGlobal) return style;
            var separator = DefaultSeparator();
            if (!string.IsNullOrEmpty(text)) separator.Content = text;
            if (!string.IsNullOrEmpty(font)) separator.FontFamily = font;
            if (sizePt > 0) separator.FontSize = sizePt * 4.0 / 3.0;
            Styles.Add(separator);
            return separator;
        }

        /// <summary>Le style des notes de bas de page en vigueur : celui de la
        /// feuille, sinon (feuille d'avant la 0.50.0 pas encore complétée) le
        /// défaut dérivé du corps — jamais null.</summary>
        public ParagraphStyle FootnoteStyle()
        {
            foreach (var style in Styles)
                if (style.Id == FootnoteId) return style;
            return DefaultFootnote(Body);
        }

        /// <summary>Le style des notes, créé s'il manque (projets et réglages
        /// d'avant la 0.50.0) : dérivé du corps tel qu'il est — même police,
        /// 85 % de la taille, comme le compositeur le faisait en dur.</summary>
        public ParagraphStyle EnsureFootnoteStyle()
        {
            foreach (var style in Styles)
                if (style.Id == FootnoteId) return style;
            var footnote = DefaultFootnote(Body);
            Styles.Add(footnote);
            return footnote;
        }

        public static ParagraphStyle DefaultFootnote(ParagraphStyle body)
        {
            var reference = body ?? new ParagraphStyle();
            return new ParagraphStyle
            {
                Id = FootnoteId,
                Name = "Notes de bas de page",
                FontFamily = reference.FontFamily,
                FontSize = Math.Max(8, Math.Round(reference.FontSize * 0.85 * 100) / 100),
                LineHeight = reference.LineHeight > 1 ? Math.Round(reference.LineHeight * 0.85 * 100) / 100 : 0,
                Color = reference.Color,
                Ligatures = reference.Ligatures,
                Align = "justify",
                FirstLineIndent = 0,
                SpaceBefore = 0,
                SpaceAfter = 0,
                KeepWithPrevious = false,
                HyphenationEnabled = reference.HyphenationEnabled
            };
        }

        public static ParagraphStyle DefaultSeparator()
        {
            return new ParagraphStyle
            {
                Id = SeparatorId,
                Name = "Séparateur de scène",
                Content = "***",
                Align = "center",
                FirstLineIndent = 0,
                SpaceBefore = 12,
                SpaceAfter = 12,
                HyphenationEnabled = false,
                KeepWithPrevious = false
            };
        }

        public StyleSheet Clone()
        {
            var copy = new StyleSheet();
            foreach (var style in Styles) copy.Styles.Add(style.Clone());
            return copy;
        }

        public static StyleSheet CreateDefault()
        {
            var sheet = new StyleSheet();
            sheet.Styles.Add(new ParagraphStyle
            {
                Id = "body",
                Name = "Corps",
                Align = "justify"
                // Standards : Times New Roman 12 pt, retrait de première ligne
                // 5 mm, interligne 14,4 pt, ligatures — les défauts de la classe.
            });
            sheet.Styles.Add(new ParagraphStyle
            {
                Id = "title1",
                Name = "Titre 1",
                FontSize = 26,
                Bold = true,
                Align = "center",
                SpaceBefore = 24,
                SpaceAfter = 18,
                FirstLineIndent = 0,
                LineHeight = 32
            });
            sheet.Styles.Add(new ParagraphStyle
            {
                Id = "title2",
                Name = "Titre 2",
                FontSize = 20,
                Bold = true,
                SpaceBefore = 18,
                SpaceAfter = 10,
                FirstLineIndent = 0,
                LineHeight = 25
            });
            sheet.Styles.Add(new ParagraphStyle
            {
                Id = "quote",
                Name = "Citation",
                Italic = true,
                LeftIndent = 32,
                SpaceBefore = 8,
                SpaceAfter = 8,
                FirstLineIndent = 0
            });
            sheet.Styles.Add(DefaultFootnote(sheet.Body)); // les notes de bas de page (0.50.0)
            sheet.Styles.Add(DefaultSeparator()); // le séparateur de scène (22/09)
            return sheet;
        }
    }
}

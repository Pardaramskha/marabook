using System;
using System.Collections.Generic;

namespace UniversSale.Model
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
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Style";

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
            return sheet;
        }
    }
}

using System;
using System.Collections.Generic;

namespace UniversSale.Model
{
    /// <summary>A named paragraph style. What runs do not override, they inherit
    /// from here. Color null = automatic (theme ink), which keeps documents
    /// readable in both light and dark themes.</summary>
    public class ParagraphStyle
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Style";
        public string FontFamily = "Georgia";
        public double FontSize = 15;
        public bool Bold, Italic;
        public string Color;              // "#RRGGBB", null = automatic
        public string Align = "left";     // "left"|"center"|"right"|"justify"
        public double SpaceBefore, SpaceAfter;
        public double FirstLineIndent;
        public double LeftIndent;

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
                Align = "justify",
                FirstLineIndent = 24,
                SpaceAfter = 2
            });
            sheet.Styles.Add(new ParagraphStyle
            {
                Id = "title1",
                Name = "Titre 1",
                FontSize = 26,
                Bold = true,
                Align = "center",
                SpaceBefore = 24,
                SpaceAfter = 18
            });
            sheet.Styles.Add(new ParagraphStyle
            {
                Id = "title2",
                Name = "Titre 2",
                FontSize = 20,
                Bold = true,
                SpaceBefore = 18,
                SpaceAfter = 10
            });
            sheet.Styles.Add(new ParagraphStyle
            {
                Id = "quote",
                Name = "Citation",
                Italic = true,
                LeftIndent = 32,
                SpaceBefore = 8,
                SpaceAfter = 8
            });
            return sheet;
        }
    }
}

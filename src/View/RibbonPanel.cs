using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Marabook.View
{
    /// <summary>Le panneau d'un onglet du ruban (17/09/2026) : les enfants
    /// sont posés à la suite comme dans le StackPanel horizontal d'origine,
    /// mais découpés en SECTIONS par les séparateurs verticaux (Tag =
    /// <see cref="SeparatorTag"/>). Quand la largeur manque, une section
    /// entière descend sur la ligne suivante au lieu d'être rognée — la
    /// disposition à une ligne de 56 px reste la norme, l'empilement n'est
    /// que le repli des fenêtres étroites. Un séparateur en tête de ligne
    /// ne se dessine pas ; un séparateur en fin de ligne non plus.</summary>
    public sealed class RibbonPanel : Panel
    {
        public const string SeparatorTag = "ribbon-rule";
        public const double LineGap = 6;

        public double LineHeight { get; set; }

        public RibbonPanel()
        {
            LineHeight = 56;
        }

        private static bool IsSeparator(UIElement child)
        {
            var element = child as FrameworkElement;
            return element != null && Equals(element.Tag, SeparatorTag);
        }

        /// <summary>Les sections : listes d'enfants consécutifs entre deux
        /// séparateurs (le séparateur qui PRÉCÈDE une section est mémorisé
        /// avec elle).</summary>
        private List<Section> Sections()
        {
            var sections = new List<Section>();
            var current = new Section();
            foreach (UIElement child in InternalChildren)
            {
                if (child == null) continue;
                if (IsSeparator(child))
                {
                    if (current.Items.Count > 0 || current.Leading != null) sections.Add(current);
                    current = new Section { Leading = child };
                    continue;
                }
                if (child.Visibility == Visibility.Collapsed) { current.Hidden.Add(child); continue; }
                current.Items.Add(child);
            }
            if (current.Items.Count > 0 || current.Leading != null) sections.Add(current);
            return sections;
        }

        private sealed class Section
        {
            public UIElement Leading; // le séparateur qui la précède, ou null
            public readonly List<UIElement> Items = new List<UIElement>();
            public readonly List<UIElement> Hidden = new List<UIElement>();
            public double Width;
            public bool ShowLeading;
            public double X, Y;
        }

        private List<Section> Layout(double availableWidth, out double neededWidth, out double neededHeight)
        {
            var sections = Sections();
            var line = new Size(double.PositiveInfinity, LineHeight);
            foreach (var section in sections)
            {
                section.Width = 0;
                if (section.Leading != null) section.Leading.Measure(line);
                foreach (var item in section.Items) { item.Measure(line); section.Width += item.DesiredSize.Width; }
                foreach (var item in section.Hidden) item.Measure(line);
            }

            var x = 0.0;
            var y = 0.0;
            neededWidth = 0;
            var wrap = !double.IsInfinity(availableWidth);
            foreach (var section in sections)
            {
                if (section.Items.Count == 0)
                {
                    section.ShowLeading = false; // séparateur orphelin
                    continue;
                }
                var leadingWidth = section.Leading == null ? 0 : section.Leading.DesiredSize.Width;
                if (wrap && x > 0 && x + leadingWidth + section.Width > availableWidth + 0.5)
                {
                    x = 0;
                    y += LineHeight + LineGap;
                }
                section.ShowLeading = section.Leading != null && x > 0;
                if (section.ShowLeading) x += leadingWidth;
                section.X = x;
                section.Y = y;
                x += section.Width;
                if (x > neededWidth) neededWidth = x;
            }
            neededHeight = sections.Count == 0 ? LineHeight : y + LineHeight;
            return sections;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width, height;
            Layout(availableSize.Width, out width, out height);
            return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double width, height;
            var sections = Layout(finalSize.Width, out width, out height);
            var nothing = new Rect(0, 0, 0, 0);
            foreach (var section in sections)
            {
                foreach (var item in section.Hidden) item.Arrange(nothing);
                if (section.Leading != null)
                {
                    if (section.ShowLeading)
                        section.Leading.Arrange(new Rect(section.X - section.Leading.DesiredSize.Width, section.Y,
                            section.Leading.DesiredSize.Width, LineHeight));
                    else section.Leading.Arrange(nothing);
                }
                var x = section.X;
                foreach (var item in section.Items)
                {
                    var w = item.DesiredSize.Width;
                    item.Arrange(new Rect(x, section.Y, w, LineHeight));
                    x += w;
                }
            }
            return new Size(finalSize.Width, Math.Max(finalSize.Height, height));
        }
    }
}

namespace UniversSale.Model
{
    /// <summary>Page layout — the project default, a document's own setup, or
    /// a book template ("gabarit"). Lengths are millimeters; PxPerMm converts
    /// to WPF pixels (96 dpi) for the editor surface.
    /// Margins follow the print-shop nomenclature: de tête (top), de pied
    /// (bottom), petit fond (binding side — mapped to the left), grand fond
    /// (outer edge — mapped to the right). Defaults 20/20/30/20 mm.
    /// Line numbers and hyphenation apply to the editor when WPF supports it
    /// and to print/export otherwise.</summary>
    public class PageSetup
    {
        public const double PxPerMm = 96.0 / 25.4;

        public double PageWidthMm = 210;  // A4
        public double PageHeightMm = 297;
        public double MarginTopMm = 20;    // de tête
        public double MarginBottomMm = 20; // de pied
        public double MarginLeftMm = 30;   // petit fond (côté reliure)
        public double MarginRightMm = 20;  // grand fond (côté extérieur)
        public int Columns = 1;
        public bool ShowMarginGuides = true; // « marges apparentes » — visibles par défaut
        public bool LineNumbers;          // printed output (phase 4)
        public bool Hyphenation;          // « césure » — WPF renders it live
        public bool FooterPageNumbers = true;
        public string FooterFont = "Times New Roman";
        public double FooterSizePt = 10;

        public double PageWidthPx { get { return PageWidthMm * PxPerMm; } }
        public double ContentWidthPx
        {
            get { return (PageWidthMm - MarginLeftMm - MarginRightMm) * PxPerMm; }
        }

        public PageSetup Clone()
        {
            return (PageSetup)MemberwiseClone();
        }

        /// <summary>True when both setups share the layout rules a book
        /// template dictates: page size and the four margins.</summary>
        public bool SameLayout(PageSetup other)
        {
            if (other == null) return false;
            return Close(PageWidthMm, other.PageWidthMm)
                && Close(PageHeightMm, other.PageHeightMm)
                && Close(MarginTopMm, other.MarginTopMm)
                && Close(MarginBottomMm, other.MarginBottomMm)
                && Close(MarginLeftMm, other.MarginLeftMm)
                && Close(MarginRightMm, other.MarginRightMm);
        }

        /// <summary>Copies the template-governed rules onto this setup, leaving
        /// the document's own toggles (folio, césure…) untouched.</summary>
        public void ApplyLayout(PageSetup template)
        {
            PageWidthMm = template.PageWidthMm;
            PageHeightMm = template.PageHeightMm;
            MarginTopMm = template.MarginTopMm;
            MarginBottomMm = template.MarginBottomMm;
            MarginLeftMm = template.MarginLeftMm;
            MarginRightMm = template.MarginRightMm;
        }

        private static bool Close(double a, double b)
        {
            return System.Math.Abs(a - b) < 0.05;
        }
    }
}

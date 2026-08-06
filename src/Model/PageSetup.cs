namespace UniversSale.Model
{
    /// <summary>Project-wide page layout (the Word-like "Mise en page" menu).
    /// Lengths are millimeters; PxPerMm converts to WPF pixels (96 dpi) for the
    /// editor surface. Defaults: A4, 2.5 cm margins everywhere, centered
    /// page-number footer in Times New Roman 10, pagination flowing across the
    /// documents of a folder (the compiler already concatenates per folder).
    /// Line numbers and hyphenation apply to the editor when WPF supports it
    /// and to print/export otherwise.</summary>
    public class PageSetup
    {
        public const double PxPerMm = 96.0 / 25.4;

        public double PageWidthMm = 210;  // A4
        public double PageHeightMm = 297;
        public double MarginTopMm = 25;
        public double MarginBottomMm = 25;
        public double MarginLeftMm = 25;
        public double MarginRightMm = 25;
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
    }
}

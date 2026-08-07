namespace UniversSale.Model
{
    /// <summary>Metadata and layout template of a Book — a special container
    /// of Écrits whose documents share one nomenclature and one gabarit, and
    /// which can be « publié » : compiled into a single print-ready PDF with
    /// continuous pagination.</summary>
    public class BookInfo
    {
        public string Subtitle = "";
        public string AuthorOverride = "";  // empty = project author
        public string Publisher = "";
        public string Collection = "";
        public string Isbn = "";
        public string Year = "";

        /// <summary>The gabarit: page size and margins every document of the
        /// book inherits. Margins default to the PAO values (20/20/30/20).</summary>
        public PageSetup Template = DefaultTemplate();

        /// <summary>Fond perdu du livre (mm), proposé à la publication.</summary>
        public double BleedMm = 3;

        public static PageSetup DefaultTemplate()
        {
            // « Roman » 14 × 21,6 cm — the format the app's page-size menu
            // already calls Livre.
            return new PageSetup { PageWidthMm = 140, PageHeightMm = 216 };
        }
    }
}

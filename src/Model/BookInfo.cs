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

        // Édition (batch 43) — le panneau « Édition » du rail droit.
        public string Genre = "";
        public string Audience = "";                                  // public cible
        public System.Collections.Generic.List<string> Themes = new System.Collections.Generic.List<string>();
        public string Pitch = "";                                     // accroche « de salon »
        public string BackCover = "";                                 // quatrième de couverture

        /// <summary>The gabarit: page size and margins every document of the
        /// book inherits. Margins default to the PAO values (20/20/30/20).</summary>
        public PageSetup Template = DefaultTemplate();

        /// <summary>Fond perdu du livre (mm), proposé à la publication.</summary>
        public double BleedMm = 3;

        /// <summary>Objectif du livre : nombre de chapitres visé (0 = aucun).
        /// Rendu par la barre de progression de l'inspecteur (batch 32).</summary>
        public int ChapterGoal;

        public static PageSetup DefaultTemplate()
        {
            // « Roman » 14 × 21,6 cm — the format the app's page-size menu
            // already calls Livre.
            return new PageSetup { PageWidthMm = 140, PageHeightMm = 216 };
        }
    }

    /// <summary>Avancement d'un livre vers son objectif : les chapitres sont
    /// les textes du récit (liminaires et table des matières exclus, dossiers
    /// traversés) ; un chapitre est terminé quand son état est « done ».</summary>
    public struct BookProgress
    {
        public int Goal;     // objectif (0 = aucun)
        public int Present;  // chapitres existants
        public int Done;     // chapitres marqués Terminé

        public bool HasGoal { get { return Goal > 0; } }

        /// <summary>Part des chapitres présents, bornée à 1 (0 sans objectif).</summary>
        public double PresentRatio { get { return Goal <= 0 ? 0 : System.Math.Min(1.0, (double)Present / Goal); } }

        /// <summary>Part des chapitres terminés, bornée à 1 (0 sans objectif).</summary>
        public double DoneRatio { get { return Goal <= 0 ? 0 : System.Math.Min(1.0, (double)Done / Goal); } }

        public static BookProgress Of(BinderItem book)
        {
            var progress = new BookProgress();
            if (book == null) return progress;
            progress.Goal = book.Book != null ? book.Book.ChapterGoal : 0;
            Count(book, ref progress);
            return progress;
        }

        private static void Count(BinderItem item, ref BookProgress progress)
        {
            foreach (var child in item.Children)
            {
                if (child.Kind == ItemKind.Text && !child.IsExtraPage && !child.IsToc)
                {
                    progress.Present++;
                    if (child.Status == "done") progress.Done++;
                }
                Count(child, ref progress);
            }
        }
    }
}

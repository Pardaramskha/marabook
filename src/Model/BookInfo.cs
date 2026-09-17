namespace Marabook.Model
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

        // b48 « objectifs et temps » (v23) : l'ÉCHÉANCE du livre (« yyyy-MM-dd »,
        // vide = aucune) et l'objectif de TAILLE (0 = aucun) en mots ou en
        // caractères — réglés dans les Options du livre, à côté de la
        // projection en chapitres. BookPace en déduit le rythme.
        public string Deadline = "";
        public int SizeGoal;
        public string SizeUnit = "words"; // "words" | "chars"

        public bool CountsChars { get { return SizeUnit == "chars"; } }

        public static PageSetup DefaultTemplate()
        {
            // « Roman » 14 × 21,6 cm — the format the app's page-size menu
            // already calls Livre.
            return new PageSetup { PageWidthMm = 140, PageHeightMm = 216 };
        }
    }

    /// <summary>LE RYTHME d'un livre (b48) : ce qu'il reste à écrire d'ici
    /// l'échéance, et combien par jour. Les comptes (mots ou caractères)
    /// viennent des textes du récit (liminaires et TdM exclus, dossiers
    /// traversés), fournis par l'appelant — le modèle ne compte pas.</summary>
    public struct BookPace
    {
        public bool HasDeadline, HasSizeGoal;
        public System.DateTime Deadline;
        public int DaysLeft;   // jours entiers avant l'échéance (0 = c'est aujourd'hui, négatif = dépassée)
        public int Done, Goal; // dans l'unité du livre
        public bool Chars;

        public string Unit { get { return Chars ? "caractères" : "mots"; } }
        public int Remaining { get { return System.Math.Max(0, Goal - Done); } }
        public bool Overdue { get { return HasDeadline && DaysLeft < 0; } }
        public bool Reached { get { return HasSizeGoal && Done >= Goal; } }
        /// <summary>Part faite de l'objectif de taille, bornée à 1.</summary>
        public double Ratio { get { return Goal <= 0 ? 0 : System.Math.Min(1.0, (double)Done / Goal); } }
        /// <summary>À écrire par jour pour tenir l'échéance, aujourd'hui
        /// compris (0 sans échéance ou sans objectif ; tout le reste si dépassée).</summary>
        public double PerDay
        {
            get
            {
                if (!HasDeadline || !HasSizeGoal) return 0;
                var days = DaysLeft + 1;
                return days <= 0 ? Remaining : (double)Remaining / days;
            }
        }

        public static BookPace Of(BinderItem book, System.Func<BinderItem, int> wordsOf, System.Func<BinderItem, int> charsOf, System.DateTime today)
        {
            var pace = new BookPace();
            if (book == null || book.Book == null) return pace;
            var info = book.Book;
            System.DateTime deadline;
            if (!string.IsNullOrEmpty(info.Deadline) && System.DateTime.TryParseExact(info.Deadline, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out deadline))
            {
                pace.HasDeadline = true;
                pace.Deadline = deadline.Date;
                pace.DaysLeft = (int)(deadline.Date - today.Date).TotalDays;
            }
            pace.Chars = info.CountsChars;
            pace.Goal = System.Math.Max(0, info.SizeGoal);
            pace.HasSizeGoal = pace.Goal > 0;
            var texts = new System.Collections.Generic.List<BinderItem>();
            BookProgress.StoryTexts(book, texts);
            foreach (var text in texts)
                pace.Done += pace.Chars ? (charsOf == null ? 0 : charsOf(text)) : (wordsOf == null ? 0 : wordsOf(text));
            return pace;
        }

        /// <summary>La phrase du rythme : « Échéance le 12/03/2027 — 42 jours,
        /// 21 300 mots restants, ~507 par jour ».</summary>
        public string Describe(System.Globalization.CultureInfo culture)
        {
            if (!HasDeadline && !HasSizeGoal) return "";
            var parts = new System.Collections.Generic.List<string>();
            if (HasDeadline)
            {
                if (DaysLeft < 0) parts.Add("Échéance dépassée de " + (-DaysLeft) + (DaysLeft == -1 ? " jour" : " jours"));
                else if (DaysLeft == 0) parts.Add("Échéance aujourd'hui");
                else parts.Add("Échéance le " + Deadline.ToString("dd/MM/yyyy", culture) + " — " + DaysLeft + (DaysLeft == 1 ? " jour" : " jours"));
            }
            if (HasSizeGoal)
            {
                if (Reached) parts.Add("objectif de " + Goal.ToString("N0", culture) + " " + Unit + " atteint !");
                else
                {
                    parts.Add(Remaining.ToString("N0", culture) + " " + Unit + " restants sur " + Goal.ToString("N0", culture));
                    if (HasDeadline && DaysLeft >= 0) parts.Add("~" + System.Math.Ceiling(PerDay).ToString("N0", culture) + " par jour");
                }
            }
            return string.Join(", ", parts.ToArray());
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

        /// <summary>Les textes du récit d'un livre (liminaires et TdM exclus,
        /// dossiers traversés), dans l'ordre.</summary>
        public static void StoryTexts(BinderItem item, System.Collections.Generic.List<BinderItem> texts)
        {
            foreach (var child in item.Children)
            {
                if (child.Kind == ItemKind.Text && !child.IsExtraPage && !child.IsToc) texts.Add(child);
                StoryTexts(child, texts);
            }
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

namespace UniversSale.Model
{
    /// <summary>One header or footer line, applied to every page of a
    /// document (or to the rectos/versos of a page gabarit). The text may
    /// carry the tokens {page}, {pages} and {titre}, expanded at render time —
    /// c'est là que se règle le look des numéros de page.</summary>
    public class HeaderFooter
    {
        public string Text = "";
        public string FontFamily;       // null = police du pied de page du projet
        public double SizePt = 10;
        public bool Bold, Italic;
        public string Align = "center"; // left | center | right

        /// <summary>Contenu RICHE (zones de gabarit marquées aux outils
        /// d'édition de texte) : quand présent, il remplace les champs
        /// simples ci-dessus. Un paragraphe pivot, jetons dans les runs.</summary>
        public TextParagraph Rich;

        public bool IsEmpty
        {
            get
            {
                if (Rich != null)
                {
                    foreach (var run in Rich.Runs)
                        if (run.Text != null && run.Text.Trim().Length > 0) return false;
                    return true;
                }
                return Text == null || Text.Trim().Length == 0;
            }
        }

        public HeaderFooter Clone()
        {
            var copy = (HeaderFooter)MemberwiseClone();
            if (Rich != null)
            {
                copy.Rich = new TextParagraph
                {
                    StyleId = Rich.StyleId,
                    AlignOverride = Rich.AlignOverride
                };
                foreach (var run in Rich.Runs)
                    copy.Rich.Runs.Add(new TextRun
                    {
                        Text = run.Text,
                        Bold = run.Bold,
                        Italic = run.Italic,
                        Underline = run.Underline,
                        Strike = run.Strike,
                        Weight = run.Weight,
                        Tracking = run.Tracking,
                        FontFamily = run.FontFamily,
                        FontSize = run.FontSize,
                        Color = run.Color
                    });
            }
            return copy;
        }

        public string Expand(int folio, int pages, string title, string book)
        {
            return (Text ?? "")
                .Replace("{page}", folio.ToString())
                .Replace("{pages}", pages.ToString())
                .Replace("{titre}", title ?? "")
                .Replace("{livre}", book ?? "");
        }
    }

    /// <summary>The four header/footer slots a page can draw from, resolved
    /// once per composition: the document's own header/footer (same on every
    /// page), or its gabarit's recto/verso pairs.</summary>
    public class PageDecor
    {
        public HeaderFooter HeaderRecto, HeaderVerso, FooterRecto, FooterVerso;
        public string Title = "";
        public string BookTitle = ""; // jeton {livre} — titre du livre ancêtre

        // Réglages du gabarit : espace en-tête/pied ↔ bloc de texte (mm,
        // 0 = centré dans la marge, comportement historique) et masquage sur
        // la PREMIÈRE page du document (chapitres sans titre rébarbatif).
        public double HeaderGapMm, FooterGapMm;
        public bool HeaderHideFirst, FooterHideFirst;

        // Pages extra : supprime aussi le folio PAR DÉFAUT (le numéro centré
        // que dessine FooterPageNumbers quand aucun pied n'est défini).
        public bool SuppressFolio;

        /// <summary>Resolves a document's decor: applied gabarit first
        /// (recto/verso asymmetry), else its own header/footer on both sides.</summary>
        public static PageDecor For(BinderItem item, Project project)
        {
            var decor = new PageDecor { Title = item.Title, SuppressFolio = item.IsExtraPage };
            for (var ancestor = item.Parent; ancestor != null; ancestor = ancestor.Parent)
                if (ancestor.Kind == ItemKind.Book) { decor.BookTitle = ancestor.Title; break; }
            BinderItem gabarit = null;
            // Pages extra : pas de folio ni de titre courant par défaut — le
            // gabarit (porteur des {page}) est ignoré ; seul un en-tête/pied
            // défini explicitement sur le document lui-même est honoré.
            if (item.PageTemplateId != null && project != null && !item.IsExtraPage)
            {
                gabarit = project.FindById(item.PageTemplateId);
                if (gabarit != null && gabarit.Kind != ItemKind.PageTemplate) gabarit = null;
            }
            if (gabarit != null)
            {
                decor.HeaderRecto = gabarit.HeaderRecto;
                decor.HeaderVerso = gabarit.HeaderVerso;
                decor.FooterRecto = gabarit.FooterRecto;
                decor.FooterVerso = gabarit.FooterVerso;
                decor.HeaderGapMm = gabarit.HeaderGapMm;
                decor.FooterGapMm = gabarit.FooterGapMm;
                decor.HeaderHideFirst = gabarit.HeaderHideFirst;
                decor.FooterHideFirst = gabarit.FooterHideFirst;
            }
            else
            {
                decor.HeaderRecto = item.Header;
                decor.HeaderVerso = item.Header;
                decor.FooterRecto = item.Footer;
                decor.FooterVerso = item.Footer;
            }
            return decor;
        }
    }
}

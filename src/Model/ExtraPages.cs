using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Fabrique des « pages extra » d'un livre : liminaires (pages de
    /// titre, direction d'anthologie, avertissement), table des matières, page
    /// éditeur et page de soutien. Chaque insertion crée UN document marqué
    /// IsExtraPage (sans folio), pré-rempli d'un texte de remplissage disposé
    /// d'après le modèle de référence — le tout reste librement éditable.
    /// Le placement vertical se fait en lignes vides du style « Liminaire »
    /// (centré, sans retrait), calées en fractions de la hauteur utile du
    /// gabarit du livre.</summary>
    public static class ExtraPages
    {
        public const string KindBlank = "blank";
        public const string KindTitle = "title";
        public const string KindDirection = "direction";
        public const string KindWarning = "warning";
        public const string KindToc = "toc";
        public const string KindPublisher = "publisher";
        public const string KindSupport = "support";

        public const string StyleId = "liminaire";

        /// <summary>Builds the extra-page document of the given kind for a
        /// book. The caller parents it and hands it its Page setup (the book
        /// gabarit), like any other new document of the book.</summary>
        public static BinderItem Create(string kind, BinderItem book, Project project)
        {
            EnsureStyle(project);
            var setup = book != null && book.Book != null ? book.Book.Template : project.Page;
            var lines = LinesFor(setup, project);

            var item = new BinderItem { Kind = ItemKind.Text, IsExtraPage = true };
            switch (kind)
            {
                case KindTitle:
                    item.Title = "Pages de titre";
                    item.Document = BuildTitlePages(book, project, lines);
                    break;
                case KindDirection:
                    item.Title = "Direction d'anthologie";
                    item.Document = BuildDirection(book, project, lines);
                    break;
                case KindWarning:
                    item.Title = "Avertissement";
                    item.Document = BuildWarning(lines);
                    break;
                case KindToc:
                    item.Title = "Table des matières";
                    item.IsToc = true;
                    item.Document = new TextDocument();
                    FillToc(item.Document, new List<TocEntry>(), lines);
                    break;
                case KindPublisher:
                    item.Title = "L'éditeur";
                    item.Document = BuildPublisher(book, project, lines);
                    break;
                case KindSupport:
                    item.Title = "Page de soutien";
                    item.Document = BuildSupport(lines);
                    break;
                default: // KindBlank
                    item.Title = "Page vierge";
                    item.Document = TextDocument.FromPlainText("");
                    break;
            }
            return item;
        }

        // ---- Table des matières ------------------------------------------

        public class TocEntry
        {
            public string Title;
            public int Folio;
        }

        /// <summary>Rebuilds the dynamic table of contents in place: caps
        /// title, then one bold entry per (non-extra) document of the book
        /// with a dotted leader and its folio.</summary>
        public static void FillToc(TextDocument document, List<TocEntry> entries, int linesPerPage)
        {
            document.Paragraphs.Clear();
            Fill(document, (int)Math.Round(linesPerPage * 0.20));
            document.Paragraphs.Add(Caps("Table des matières", 26, true));
            Fill(document, 3);
            foreach (var entry in entries)
            {
                var paragraph = new TextParagraph { StyleId = StyleId, AlignOverride = "left" };
                paragraph.Runs.Add(new TextRun { Text = entry.Title, Bold = true });
                var width = entry.Title.Length + entry.Folio.ToString().Length;
                paragraph.Runs.Add(new TextRun
                {
                    Text = new string('.', Math.Max(3, 64 - width)) + " " + entry.Folio
                });
                document.Paragraphs.Add(paragraph);
                document.Paragraphs.Add(P());
            }
            if (entries.Count == 0)
            {
                document.Paragraphs.Add(Center("(La table se remplit d'après les documents du livre.)",
                    null, false, true));
            }
        }

        // ---- Layouts -----------------------------------------------------

        private static TextDocument BuildTitlePages(BinderItem book, Project project, int lines)
        {
            var title = book != null ? book.Title : "Titre du livre";
            var info = book != null && book.Book != null ? book.Book : new BookInfo();
            var subtitle = info.Subtitle.Length > 0 ? info.Subtitle : "Sous-titre du livre";
            var author = Author(info, project);
            var publisher = info.Publisher.Length > 0 ? info.Publisher : "Nom de l'éditeur";
            var year = info.Year.Length > 0 ? info.Year : DateTime.Now.Year.ToString();

            var document = new TextDocument();

            // Pages 1-2 : gardes vierges.
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(PageBreak());

            // Page 3 : faux-titre — le titre seul, en romain, au quart de page.
            var fauxTitre = Fill(BreakTo(document), (int)Math.Round(lines * 0.24));
            fauxTitre.Paragraphs.Add(Center(title, 28, false, false));

            // Page 4 : verso vierge.
            document.Paragraphs.Add(PageBreak());

            // Page 5 : page de titre.
            var titre = Fill(BreakTo(document), (int)Math.Round(lines * 0.28));
            titre.Paragraphs.Add(Caps(title, 32, true));
            var line = Center(subtitle, null, false, true);
            document.Paragraphs.Add(line);
            var by = P();
            by.Runs.Add(new TextRun { Text = "par ", Italic = true });
            by.Runs.Add(new TextRun { Text = author, Bold = true, Italic = true });
            document.Paragraphs.Add(by);
            Fill(document, (int)Math.Round(lines * 0.42));
            document.Paragraphs.Add(Caps(publisher, 20, true));

            // Page 6 : copyright.
            BreakTo(document);
            document.Paragraphs.Add(Center("Des mêmes auteur·ice·s :", null, false, false, true));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Aux mêmes éditions :", null, false, true));
            var works = P();
            works.Runs.Add(new TextRun { Text = author, Bold = true });
            works.Runs.Add(new TextRun { Text = " — Titre d'un autre ouvrage" });
            document.Paragraphs.Add(works);
            Fill(document, (int)Math.Round(lines * 0.52));
            document.Paragraphs.Add(Center("Couverture", null, false, true));
            document.Paragraphs.Add(Center("© Illustrateur·ice, " + year, null, false, false));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Maquette", null, false, true));
            document.Paragraphs.Add(Center("© Maquettiste, " + year, null, false, false));
            document.Paragraphs.Add(P());
            var imprint = P();
            imprint.Runs.Add(new TextRun { Text = publisher, Bold = true });
            imprint.Runs.Add(new TextRun { Text = " © " + year });
            document.Paragraphs.Add(imprint);
            if (info.Collection.Length > 0)
                document.Paragraphs.Add(Center("Collection " + info.Collection, null, false, false));
            document.Paragraphs.Add(Center("ISBN — " +
                (info.Isbn.Length > 0 ? info.Isbn : "000-0-0000000-0-0"), null, false, false));
            document.Paragraphs.Add(Center("Dépôt légal — " + year, null, false, false));
            return document;
        }

        private static TextDocument BuildDirection(BinderItem book, Project project, int lines)
        {
            var info = book != null && book.Book != null ? book.Book : new BookInfo();
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.36));
            var directed = P();
            directed.Runs.Add(new TextRun { Text = "Recueil dirigé par " });
            directed.Runs.Add(new TextRun { Text = Author(info, project), Bold = true });
            document.Paragraphs.Add(directed);
            Fill(document, 3);
            document.Paragraphs.Add(Center("Auteurs ayant participé au recueil :",
                null, false, false, true));
            Fill(document, 2);
            for (var i = 1; i <= 6; i++)
                document.Paragraphs.Add(Center("Prénom Nom " + i, null, true, false));
            BreakTo(document); // verso vierge
            return document;
        }

        private static TextDocument BuildWarning(int lines)
        {
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.34));
            document.Paragraphs.Add(Caps("Avertissement", 26, true));
            Fill(document, 3);
            document.Paragraphs.Add(Center("Cher lecteur,", null, false, false));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Cet ouvrage contient des textes dont le contenu ou " +
                "les thématiques abordées seraient susceptibles de heurter une audience " +
                "non avertie. Gardez à l'esprit qu'il s'agit d'une œuvre de fiction, et " +
                "lisez-la en connaissance de cause.", null, false, false));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Vous trouverez des indications signalant les textes " +
                "concernés dans la table des matières, à la fin de l'ouvrage.",
                null, false, false));
            BreakTo(document); // verso vierge
            return document;
        }

        private static TextDocument BuildPublisher(BinderItem book, Project project, int lines)
        {
            var info = book != null && book.Book != null ? book.Book : new BookInfo();
            var publisher = info.Publisher.Length > 0 ? info.Publisher : "Nom de l'éditeur";
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.18));
            document.Paragraphs.Add(Caps("L'éditeur", 26, true));
            Fill(document, 2);
            document.Paragraphs.Add(Center(publisher + " est une maison d'édition " +
                "indépendante, animée par la passion des littératures de tous horizons.",
                null, false, false));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Nous publions régulièrement des nouvelles, des " +
                "chroniques et des rencontres à retrouver sur notre site, et organisons " +
                "des événements autour de nos ouvrages et de leurs auteur·ice·s.",
                null, false, false));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Piqué·e·s par la curiosité ? Retrouvez-nous sur " +
                "notre site web ou sur les réseaux sociaux et apprenez-en plus sur " +
                "notre aventure !", null, false, false));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Et surtout, n'oubliez pas de rêver.",
                null, false, false));
            Fill(document, 3);
            var site = P();
            site.Runs.Add(new TextRun { Text = "Site internet", Underline = true });
            site.Runs.Add(new TextRun { Text = " : https://www.exemple.fr" });
            document.Paragraphs.Add(site);
            var socials = P();
            socials.Runs.Add(new TextRun { Text = "Réseaux sociaux", Underline = true });
            socials.Runs.Add(new TextRun { Text = " : @exemple" });
            document.Paragraphs.Add(socials);
            return document;
        }

        private static TextDocument BuildSupport(int lines)
        {
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.72));
            document.Paragraphs.Add(Center("Publié avec le soutien de nos partenaires " +
                "et de toutes celles et ceux qui ont accompagné ce projet.",
                null, false, false));
            return document;
        }

        // ---- Helpers -----------------------------------------------------

        private static string Author(BookInfo info, Project project)
        {
            if (info.AuthorOverride.Length > 0) return info.AuthorOverride;
            if (project != null && project.Author.Length > 0) return project.Author;
            return "Nom de l'auteur·ice";
        }

        /// <summary>Ensures the shared « Liminaire » paragraph style exists:
        /// centered, no first-line indent, no hyphenation — the base of every
        /// extra page.</summary>
        public static ParagraphStyle EnsureStyle(Project project)
        {
            foreach (var style in project.Styles.Styles)
                if (style.Id == StyleId) return style;
            var created = new ParagraphStyle
            {
                Id = StyleId,
                Name = "Liminaire",
                Align = "center",
                FirstLineIndent = 0,
                HyphenationEnabled = false
            };
            project.Styles.Styles.Add(created);
            return created;
        }

        /// <summary>Approximate « Liminaire » lines fitting one page of the
        /// given setup — the unit of the proportional vertical placement.</summary>
        public static int LinesFor(PageSetup setup, Project project)
        {
            var body = project.Styles.Find(StyleId);
            var leading = body != null && body.LineHeight > 1 ? body.LineHeight : 19.2;
            var height = (setup.PageHeightMm - setup.MarginTopMm - setup.MarginBottomMm)
                * PageSetup.PxPerMm;
            return Math.Max(10, (int)(height / leading));
        }

        private static TextParagraph P()
        {
            return new TextParagraph { StyleId = StyleId };
        }

        private static TextParagraph PageBreak()
        {
            return new TextParagraph { StyleId = StyleId, PageBreakBefore = true };
        }

        /// <summary>Starts a new page and returns the document (chaining aid).</summary>
        private static TextDocument BreakTo(TextDocument document)
        {
            document.Paragraphs.Add(PageBreak());
            return document;
        }

        private static TextDocument Fill(TextDocument document, int count)
        {
            for (var i = 0; i < count; i++) document.Paragraphs.Add(P());
            return document;
        }

        private static TextParagraph Center(string text, double? sizePx, bool bold, bool italic)
        {
            return Center(text, sizePx, bold, italic, false);
        }

        private static TextParagraph Center(string text, double? sizePx, bool bold,
            bool italic, bool underline)
        {
            var paragraph = P();
            paragraph.Runs.Add(new TextRun
            {
                Text = text,
                FontSize = sizePx,
                Bold = bold ? (bool?)true : null,
                Italic = italic ? (bool?)true : null,
                Underline = underline ? (bool?)true : null
            });
            return paragraph;
        }

        private static TextParagraph Caps(string text, double sizePx, bool bold)
        {
            return Center(text.ToUpperInvariant(), sizePx, bold, false);
        }
    }
}

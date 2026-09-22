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
        // Les liminaires (avant le corps).
        public const string KindBlank = "blank";
        public const string KindTitle = "title";
        public const string KindDirection = "direction";
        public const string KindWarning = "warning";
        public const string KindToc = "toc";
        public const string KindPublisher = "publisher";
        public const string KindSupport = "support";
        public const string KindDedication = "dedication";   // b49
        public const string KindEpigraph = "epigraph";       // b49
        public const string KindPreface = "preface";         // b49 (préface ou avant-propos)
        public const string KindAuthorNote = "authorNote";   // b49
        // Les pages de fin (après le corps) — b49.
        public const string KindBlankBack = "blank-back";
        public const string KindPostface = "postface";
        public const string KindThanks = "thanks";
        public const string KindAboutAuthor = "aboutAuthor";
        public const string KindColophon = "colophon";       // achevé d'imprimer
        public const string KindTocBack = "toc-back";        // la table des matières en fin d'ouvrage
        // Les annexes (tout à la fin) — b49.
        public const string KindBlankAnnex = "blank-annex";
        public const string KindGlossary = "glossary";       // dynamique : le Dictionnaire du projet
        public const string KindIndex = "index";             // dynamique : personnages et lieux, avec leurs folios
        public const string KindChronology = "chronology";
        public const string KindBibliography = "bibliography";
        public const string KindEndnotes = "endnotes";       // dynamique : les notes de bas de page du livre
        public const string KindIllustrations = "illustrations";

        // Les trois SECTIONS (b49) : où la page vit dans le livre. Le
        // placement à la création les suit (InsertIndex) ; l'auteur peut
        // ensuite déplacer la page librement dans la Pile.
        public const string SectionFront = "front";
        public const string SectionBack = "back";
        public const string SectionAnnex = "annex";

        public const string StyleId = "liminaire";

        /// <summary>La section d'une sorte de page.</summary>
        public static string SectionOfKind(string kind)
        {
            switch (kind)
            {
                case KindBlankBack:
                case KindPostface:
                case KindThanks:
                case KindAboutAuthor:
                case KindColophon:
                case KindTocBack:
                    return SectionBack;
                case KindBlankAnnex:
                case KindGlossary:
                case KindIndex:
                case KindChronology:
                case KindBibliography:
                case KindEndnotes:
                case KindIllustrations:
                    return SectionAnnex;
                default:
                    return SectionFront;
            }
        }

        /// <summary>La section d'une page extra : la sienne, ou liminaire pour
        /// une page d'avant les sections (v25 et moins). Null hors page extra.</summary>
        public static string SectionOf(BinderItem item)
        {
            if (item == null || !item.IsExtraPage) return null;
            return item.ExtraSection ?? SectionFront;
        }

        /// <summary>Le libellé d'une section : « Liminaire », « Page de fin », « Annexe ».</summary>
        public static string SectionLabel(string section)
        {
            switch (section)
            {
                case SectionBack: return "Page de fin";
                case SectionAnnex: return "Annexe";
                default: return "Liminaire";
            }
        }

        /// <summary>Vrai pour une sorte de page rebâtie d'après le livre à
        /// chaque ouverture et avant publication : table des matières, index,
        /// notes de fin, glossaire.</summary>
        public static bool IsDynamicKind(string kind)
        {
            return kind == KindToc || kind == KindTocBack || kind == KindIndex
                || kind == KindEndnotes || kind == KindGlossary;
        }

        public static bool IsDynamic(BinderItem item)
        {
            return item != null && item.IsExtraPage && (item.IsToc || IsDynamicKind(item.ExtraKind));
        }

        /// <summary>Où insérer une page neuve dans les enfants du livre selon
        /// sa section (b49) : une liminaire après les liminaires de tête
        /// (avant le premier écrit), une page de fin avant la première annexe
        /// (sinon en queue), une annexe en queue. Un dossier du livre compte
        /// comme du récit.</summary>
        public static int InsertIndex(BinderItem book, string section)
        {
            var children = book.Children;
            if (section == SectionFront)
            {
                for (var i = 0; i < children.Count; i++)
                    if (SectionOf(children[i]) != SectionFront) return i;
                return children.Count;
            }
            if (section == SectionBack)
            {
                for (var i = 0; i < children.Count; i++)
                    if (SectionOf(children[i]) == SectionAnnex) return i;
                return children.Count;
            }
            return children.Count;
        }

        /// <summary>Builds the extra-page document of the given kind for a
        /// book. The caller parents it and hands it its Page setup (the book
        /// gabarit), like any other new document of the book.</summary>
        public static BinderItem Create(string kind, BinderItem book, Project project)
        {
            EnsureStyle(project);
            var setup = book != null && book.Book != null ? book.Book.Template : project.Page;
            var lines = LinesFor(setup, project);

            var item = new BinderItem
            {
                Kind = ItemKind.Text,
                IsExtraPage = true,
                ExtraSection = SectionOfKind(kind),
                ExtraKind = kind ?? KindBlank
            };
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
                case KindTocBack:
                    item.Title = "Table des matières";
                    item.IsToc = true;
                    item.ExtraKind = KindToc;
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
                // — Liminaires (b49).
                case KindDedication:
                    item.Title = "Dédicace";
                    item.Document = BuildDedication(lines);
                    break;
                case KindEpigraph:
                    item.Title = "Épigraphe";
                    item.Document = BuildEpigraph(lines);
                    break;
                case KindPreface:
                    item.Title = "Préface";
                    item.Document = BuildSigned("Préface", "Un texte d'ouverture, signé par un tiers (préface) ou par l'auteur·ice (avant-propos) : ce qu'il faut savoir avant de lire.", lines, "Prénom Nom");
                    break;
                case KindAuthorNote:
                    item.Title = "Note de l'auteur";
                    item.Document = BuildSigned("Note de l'auteur·ice", "Quelques mots sur la genèse du livre, ses partis pris, ses libertés avec l'histoire ou la géographie.", lines, Author(book, project));
                    break;
                // — Pages de fin (b49).
                case KindPostface:
                    item.Title = "Postface";
                    item.Document = BuildSigned("Postface", "Un texte de clôture : le regard porté sur l'œuvre une fois refermée, par un tiers ou par l'auteur·ice.", lines, "Prénom Nom");
                    break;
                case KindThanks:
                    item.Title = "Remerciements";
                    item.Document = BuildSigned("Remerciements", "Merci à celles et ceux qui ont lu, relu, soutenu, patienté — et à tous les autres.", lines, null);
                    break;
                case KindAboutAuthor:
                    item.Title = "À propos de l'auteur";
                    item.Document = BuildAboutAuthor(book, project, lines);
                    break;
                case KindColophon:
                    item.Title = "Achevé d'imprimer";
                    item.Document = BuildColophon(book, project, lines);
                    break;
                // — Annexes (b49).
                case KindGlossary:
                    item.Title = "Glossaire";
                    item.Document = new TextDocument();
                    FillGlossary(item.Document, new List<GlossaryEntry>(), lines);
                    break;
                case KindIndex:
                    item.Title = "Index";
                    item.Document = new TextDocument();
                    FillIndex(item.Document, new List<IndexEntry>(), lines);
                    break;
                case KindChronology:
                    item.Title = "Chronologie";
                    item.Document = BuildChronology(lines);
                    break;
                case KindBibliography:
                    item.Title = "Bibliographie";
                    item.Document = BuildBibliography(lines);
                    break;
                case KindEndnotes:
                    item.Title = "Notes";
                    item.Document = new TextDocument();
                    FillEndnotes(item.Document, new List<EndnoteChapter>(), lines);
                    break;
                case KindIllustrations:
                    item.Title = "Cartes et illustrations";
                    item.Document = BuildIllustrations(lines);
                    break;
                case KindBlankBack:
                    item.Title = "Page de fin";
                    item.Document = TextDocument.FromPlainText("");
                    break;
                case KindBlankAnnex:
                    item.Title = "Annexe";
                    item.Document = TextDocument.FromPlainText("");
                    break;
                default: // KindBlank
                    item.ExtraKind = KindBlank;
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

        // ---- Pages dynamiques (b49) : index, notes de fin, glossaire -------

        public class IndexEntry
        {
            public string Name;
            public string Category;          // « Personnage », « Lieu »… (intertitre)
            public List<int> Folios = new List<int>();
        }

        public class EndnoteChapter
        {
            public string Title;
            public List<string> Notes = new List<string>();
        }

        public class GlossaryEntry
        {
            public string Word;
            public string Definition;
        }

        /// <summary>L'index des personnages et des lieux : par catégorie, les
        /// noms dans l'ordre alphabétique avec les folios des écrits où ils
        /// apparaissent (« Kaladin ........ 12, 45 »).</summary>
        public static void FillIndex(TextDocument document, List<IndexEntry> entries, int linesPerPage)
        {
            document.Paragraphs.Clear();
            Fill(document, (int)Math.Round(linesPerPage * 0.20));
            document.Paragraphs.Add(Caps("Index", 26, true));
            Fill(document, 3);
            string category = null;
            var sorted = new List<IndexEntry>(entries);
            sorted.Sort(delegate(IndexEntry a, IndexEntry b)
            {
                var byCategory = string.Compare(a.Category ?? "", b.Category ?? "", StringComparison.CurrentCultureIgnoreCase);
                return byCategory != 0 ? byCategory : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            foreach (var entry in sorted)
            {
                if (!string.IsNullOrEmpty(entry.Category) && entry.Category != category)
                {
                    if (category != null) document.Paragraphs.Add(P());
                    category = entry.Category;
                    document.Paragraphs.Add(Center(category, null, true, false, true));
                    document.Paragraphs.Add(P());
                }
                var folios = new List<string>();
                foreach (var folio in entry.Folios) folios.Add(folio.ToString());
                var paragraph = new TextParagraph { StyleId = StyleId, AlignOverride = "left" };
                paragraph.Runs.Add(new TextRun { Text = entry.Name });
                var joined = string.Join(", ", folios.ToArray());
                paragraph.Runs.Add(new TextRun { Text = " " + new string('.', Math.Max(3, 60 - entry.Name.Length - joined.Length)) + " " + joined });
                document.Paragraphs.Add(paragraph);
            }
            if (entries.Count == 0)
                document.Paragraphs.Add(Center("(L'index se remplit d'après les fiches Personnage et Lieu nommées dans les écrits du livre.)", null, false, true));
        }

        /// <summary>Les notes de fin : par écrit, ses notes de bas de page
        /// numérotées à la suite. Les notes restent aussi en bas de page dans
        /// les écrits — cette annexe les rassemble, elle ne les déplace pas.</summary>
        public static void FillEndnotes(TextDocument document, List<EndnoteChapter> chapters, int linesPerPage)
        {
            document.Paragraphs.Clear();
            Fill(document, (int)Math.Round(linesPerPage * 0.20));
            document.Paragraphs.Add(Caps("Notes", 26, true));
            Fill(document, 3);
            var number = 0;
            var any = false;
            foreach (var chapter in chapters)
            {
                if (chapter.Notes.Count == 0) continue;
                if (any) document.Paragraphs.Add(P());
                any = true;
                document.Paragraphs.Add(Center(chapter.Title, null, true, false, true));
                document.Paragraphs.Add(P());
                foreach (var note in chapter.Notes)
                {
                    number++;
                    var paragraph = new TextParagraph { StyleId = StyleId, AlignOverride = "left" };
                    paragraph.Runs.Add(new TextRun { Text = number + ". ", Bold = true });
                    paragraph.Runs.Add(new TextRun { Text = note });
                    document.Paragraphs.Add(paragraph);
                }
            }
            if (!any)
                document.Paragraphs.Add(Center("(Les notes se rassemblent d'après les notes de bas de page des écrits du livre.)", null, false, true));
        }

        /// <summary>Le glossaire : les mots du Dictionnaire du projet qui ont
        /// une définition, dans l'ordre alphabétique.</summary>
        public static void FillGlossary(TextDocument document, List<GlossaryEntry> entries, int linesPerPage)
        {
            document.Paragraphs.Clear();
            Fill(document, (int)Math.Round(linesPerPage * 0.20));
            document.Paragraphs.Add(Caps("Glossaire", 26, true));
            Fill(document, 3);
            var sorted = new List<GlossaryEntry>(entries);
            sorted.Sort(delegate(GlossaryEntry a, GlossaryEntry b)
            { return string.Compare(a.Word, b.Word, StringComparison.CurrentCultureIgnoreCase); });
            foreach (var entry in sorted)
            {
                var paragraph = new TextParagraph { StyleId = StyleId, AlignOverride = "left" };
                paragraph.Runs.Add(new TextRun { Text = entry.Word, Bold = true });
                paragraph.Runs.Add(new TextRun { Text = " — " + entry.Definition });
                document.Paragraphs.Add(paragraph);
            }
            if (entries.Count == 0)
                document.Paragraphs.Add(Center("(Le glossaire se remplit d'après les mots du Dictionnaire qui ont une définition.)", null, false, true));
        }

        // ---- Liminaires, pages de fin et annexes à texte (b49) ------------

        private static TextDocument BuildDedication(int lines)
        {
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.36));
            document.Paragraphs.Add(Center("À celles et ceux qui…", null, false, true));
            BreakTo(document); // verso vierge
            return document;
        }

        private static TextDocument BuildEpigraph(int lines)
        {
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.36));
            document.Paragraphs.Add(Center("« La citation qui ouvre le livre. »", null, false, true));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("— Auteur·ice, Titre de l'œuvre", null, false, false));
            BreakTo(document); // verso vierge
            return document;
        }

        /// <summary>Un texte titré, en pleine page, signé en bas si un nom est
        /// donné : préface, note de l'auteur, postface, remerciements.</summary>
        private static TextDocument BuildSigned(string heading, string body, int lines, string signature)
        {
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.18));
            document.Paragraphs.Add(Caps(heading, 26, true));
            Fill(document, 3);
            document.Paragraphs.Add(Left(body));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Left("Le texte se poursuit ici, au fil des paragraphes."));
            if (signature != null)
            {
                Fill(document, 2);
                var signed = P();
                signed.AlignOverride = "right";
                signed.Runs.Add(new TextRun { Text = signature, Italic = true });
                document.Paragraphs.Add(signed);
            }
            return document;
        }

        private static TextDocument BuildAboutAuthor(BinderItem book, Project project, int lines)
        {
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.18));
            document.Paragraphs.Add(Caps("À propos de l'auteur·ice", 26, true));
            Fill(document, 3);
            var name = P();
            name.Runs.Add(new TextRun { Text = Author(book, project), Bold = true });
            document.Paragraphs.Add(name);
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Quelques lignes de présentation : d'où l'auteur·ice écrit, ce qu'il ou elle a déjà publié, ce qui l'anime.", null, false, false));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Du même auteur·ice :", null, false, true));
            document.Paragraphs.Add(Center("Titre d'un autre ouvrage", null, false, false));
            return document;
        }

        private static TextDocument BuildColophon(BinderItem book, Project project, int lines)
        {
            var info = book != null && book.Book != null ? book.Book : new BookInfo();
            var publisher = info.Publisher.Length > 0 ? info.Publisher : "Nom de l'éditeur";
            var year = info.Year.Length > 0 ? info.Year : DateTime.Now.Year.ToString();
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.62));
            document.Paragraphs.Add(Center("Achevé d'imprimer en " + year, null, false, false));
            document.Paragraphs.Add(Center("sur les presses de l'Imprimerie Exemple", null, false, false));
            document.Paragraphs.Add(Center("pour le compte de " + publisher + ".", null, false, false));
            document.Paragraphs.Add(P());
            document.Paragraphs.Add(Center("Dépôt légal — " + year, null, false, false));
            document.Paragraphs.Add(Center("Imprimé en France", null, false, true));
            return document;
        }

        private static TextDocument BuildChronology(int lines)
        {
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.18));
            document.Paragraphs.Add(Caps("Chronologie", 26, true));
            Fill(document, 3);
            foreach (var row in new[] { "An 1 — Un événement fondateur.", "An 12 — Ce qui s'ensuit.", "An 40 — Où commence le récit." })
            {
                var paragraph = new TextParagraph { StyleId = StyleId, AlignOverride = "left" };
                var dash = row.IndexOf(" — ", StringComparison.Ordinal);
                paragraph.Runs.Add(new TextRun { Text = row.Substring(0, dash), Bold = true });
                paragraph.Runs.Add(new TextRun { Text = row.Substring(dash) });
                document.Paragraphs.Add(paragraph);
            }
            return document;
        }

        private static TextDocument BuildBibliography(int lines)
        {
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.18));
            document.Paragraphs.Add(Caps("Bibliographie", 26, true));
            Fill(document, 3);
            var entry = new TextParagraph { StyleId = StyleId, AlignOverride = "left" };
            entry.Runs.Add(new TextRun { Text = "NOM, Prénom. " });
            entry.Runs.Add(new TextRun { Text = "Titre de l'ouvrage", Italic = true });
            entry.Runs.Add(new TextRun { Text = ". Ville : Éditeur, année." });
            document.Paragraphs.Add(entry);
            document.Paragraphs.Add(P());
            var article = new TextParagraph { StyleId = StyleId, AlignOverride = "left" };
            article.Runs.Add(new TextRun { Text = "NOM, Prénom. « Titre de l'article ». " });
            article.Runs.Add(new TextRun { Text = "Revue", Italic = true });
            article.Runs.Add(new TextRun { Text = ", n° 0, année, p. 0-0." });
            document.Paragraphs.Add(article);
            return document;
        }

        private static TextDocument BuildIllustrations(int lines)
        {
            var document = new TextDocument();
            Fill(document, (int)Math.Round(lines * 0.18));
            document.Paragraphs.Add(Caps("Cartes et illustrations", 26, true));
            Fill(document, 3);
            document.Paragraphs.Add(Center("Insérez ici vos cartes et illustrations (Insertion › Image), une par page, avec leur légende.", null, false, true));
            return document;
        }

        private static TextParagraph Left(string text)
        {
            var paragraph = new TextParagraph { StyleId = StyleId, AlignOverride = "left" };
            paragraph.Runs.Add(new TextRun { Text = text });
            return paragraph;
        }

        // ---- Helpers -----------------------------------------------------

        private static string Author(BinderItem book, Project project)
        {
            return Author(book != null && book.Book != null ? book.Book : new BookInfo(), project);
        }

        private static string Author(BookInfo info, Project project)
        {
            if (info.AuthorOverride.Length > 0) return info.AuthorOverride;
            if (project != null && project.Author.Length > 0) return project.Author;
            if (Defaults.Author.Trim().Length > 0) return Defaults.Author.Trim(); // Préférences › Auteur (22/09)
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

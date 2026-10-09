using System;
using System.Text;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.Tests
{
    /// <summary>C4 — le compositeur sur StubGlyphMetrics : chaque caractère
    /// avance d'un demi-cadratin, donc les positions attendues se calculent à
    /// la main, sans dépendre des polices installées. Couvre la coupure de
    /// ligne, le bug A6 (mot plus large que la colonne) et la règle
    /// veuves/orphelines 2/2.</summary>
    public static class ComposerTests
    {
        // Page : colonne de 50 mm = 188,976 px → 37 caractères de 5 px.
        // Hauteur utile : 200 px = 10 lignes de 20 px (LineHeight du style).
        private const double PxPerMm = 96.0 / 25.4;

        private static PageSetup Setup()
        {
            return new PageSetup
            {
                PageWidthMm = 100,
                PageHeightMm = 200.0 / PxPerMm + 40, // contenu = 200 px pile
                MarginTopMm = 20,
                MarginBottomMm = 20,
                MarginLeftMm = 25,
                MarginRightMm = 25,
                Hyphenation = true // le compositeur obéit au bouton du document
            };
        }

        private static StyleSheet Styles()
        {
            var styles = StyleSheet.CreateDefault();
            var body = styles.Find("body");
            body.FontSize = 10;        // stub : 5 px par caractère
            body.LineHeight = 20;
            body.FirstLineIndent = 0;
            body.Align = "left";
            body.HyphenationEnabled = true;
            body.HyphenMinWordLength = 5;
            body.HyphenMinBefore = 2;
            body.HyphenMinAfter = 3;
            body.HyphenConsecutiveLimit = 3;
            body.KeepWithPrevious = false;
            body.KeepLinesTogether = false;
            body.KeepNextLines = 0;
            return styles;
        }

        private static CompositionEngine Compose(TextDocument document)
        {
            var engine = new CompositionEngine(document, Styles(), Setup(),
                null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            return engine;
        }

        private static TextDocument Document(params string[] paragraphs)
        {
            var document = new TextDocument();
            foreach (var text in paragraphs)
            {
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun { Text = text });
                document.Paragraphs.Add(paragraph);
            }
            return document;
        }

        /// <summary>Mot incoupable de <paramref name="length"/> caractères
        /// (un chiffre au milieu neutralise la césure).</summary>
        private static string Unbreakable(int length)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < length; i++)
                sb.Append(i == 1 ? '1' : (char)('a' + i % 3));
            return sb.ToString();
        }

        /// <summary>Mot « français » en syllabes CV : coupable tous les
        /// 2 caractères (V-CV), déterministe.</summary>
        private static string CvWord(int syllables)
        {
            var consonants = "tmnblrdpcsvgf";
            var vowels = "aoiue";
            var sb = new StringBuilder();
            for (var i = 0; i < syllables; i++)
            {
                sb.Append(consonants[i % consonants.Length]);
                sb.Append(vowels[i % vowels.Length]);
            }
            return sb.ToString();
        }

        public static void Run(Harness t)
        {
            t.Suite("C4 — compositeur sur métriques fixes");
            LineBreaking(t);
            OversizedWordHyphenated(t);
            DocumentToggleDisablesHyphenation(t);
            ParagraphHyphenationOverridesPage(t);
            ElidedAndCompoundWordsHyphenated(t);
            LooseLineFlagged(t);
            CompiledBookCarriesHyphenation(t);
            ExceptionWordNeverHyphenated(t);
            WidowControl(t);
            OrphanControl(t);
            IndentOverride(t);
            DocumentLeading(t);
            IncrementalStats(t);
            RunBoundaryGlued(t);
        }

        /// <summary>Une frontière de run au milieu d'un mot (29/09) : « vraiment »
        /// dans un run, « ? » collé par une fine insécable dans le run suivant.
        /// Les deux atomes forment UN mot pour la coupure : le « ? » ne part
        /// jamais seul en tête de ligne — le mot se coupe à la césure, ou
        /// passe entier à la ligne avec sa ponctuation.</summary>
        private static void RunBoundaryGlued(Harness t)
        {
            // Colonne de 37 caractères : « aaaa bbbb cccc dddd eeee ff vraiment »
            // fait 36 — le « ? » (fine + ?) déborde. Sans soudure, la ligne
            // finissait à 36 et « ? » ouvrait la suivante.
            var document = new TextDocument();
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "aaaa bbbb cccc dddd eeee ff vraiment", Italic = true });
            paragraph.Runs.Add(new TextRun { Text = " ? Suite du texte." });
            document.Paragraphs.Add(paragraph);
            var lines = Compose(document).Current.Paragraphs[0].Lines;
            t.Check(lines.Count >= 2, "deux lignes au moins");
            t.Check(lines[0].Hyphenated, "le mot se coupe à la césure (vrai-)");
            t.Equal(32, lines[0].End, "la ligne finit après « vrai », jamais à 36 devant le « ? »");

            // Mot incoupable : le groupe entier passe à la ligne, ponctuation comprise.
            var stuck = new TextDocument();
            var p2 = new TextParagraph();
            p2.Runs.Add(new TextRun { Text = "aaaa bbbb cccc dddd eeee ff vra1ment", Italic = true });
            p2.Runs.Add(new TextRun { Text = " ? Suite du texte." });
            stuck.Paragraphs.Add(p2);
            var lines2 = Compose(stuck).Current.Paragraphs[0].Lines;
            t.Check(lines2.Count >= 2, "deux lignes au moins (incoupable)");
            t.Equal(28, lines2[0].End, "la ligne finit avant le mot : « vra1ment ? » part entier");
            t.Equal(28, lines2[1].Start, "…et la suivante commence sur le mot, pas sur « ? »");
        }

        /// <summary>Le décalage d'un paragraphe (17/09) remplace d'un bloc
        /// retrait gauche, alinéa et retrait de liste ; 0 ramène tout à la
        /// marge, alinéa du style compris.</summary>
        private static void IndentOverride(Harness t)
        {
            var document = Document("un deux trois quatre cinq six sept huit neuf dix onze douze",
                "un deux trois quatre cinq six sept huit neuf dix onze douze",
                "puce");
            document.Paragraphs[2].ListKind = "bullet";
            var styles = Styles();
            styles.Find("body").FirstLineIndent = 10;
            var engine = new CompositionEngine(document, styles, Setup(), null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            var plain = engine.Current.Paragraphs[0].Lines;
            t.Check(plain.Count >= 2, "témoin : deux lignes au moins");
            t.Equal(10.0, plain[0].Pieces[0].Origin.X, "témoin : l'alinéa du style sur la première ligne");
            t.Equal(0.0, plain[1].Pieces[0].Origin.X, "témoin : la suite à la marge");

            document.Paragraphs[1].Indent = 0;
            document.Paragraphs[2].Indent = 0;
            engine.ComposeAll();
            var flush = engine.Current.Paragraphs[1].Lines;
            t.Equal(0.0, flush[0].Pieces[0].Origin.X, "décalage 0 : plus d'alinéa sur la première ligne");
            t.Equal(0.0, engine.Current.Paragraphs[2].Lines[0].Pieces[0].Origin.X,
                "décalage 0 : la puce revient à la marge (plus de retrait de liste)");

            document.Paragraphs[1].Indent = 37.8;
            engine.ComposeAll();
            var shifted = engine.Current.Paragraphs[1].Lines;
            t.Equal(37.8, shifted[0].Pieces[0].Origin.X, "décalage 37,8 : la première ligne suit, sans alinéa");
            t.Equal(37.8, shifted[1].Pieces[0].Origin.X, "décalage 37,8 : la deuxième ligne aussi");

            // — La première ligne seule (21/09) : un alinéa recréé sur un bloc
            //   à la marge, puis un retrait suspendu (le bloc décalé, la
            //   première ligne restée à la marge — elle déborde du bloc).
            document.Paragraphs[1].Indent = 0;
            document.Paragraphs[1].FirstIndent = 18.9;
            engine.ComposeAll();
            var alinea = engine.Current.Paragraphs[1].Lines;
            t.Equal(18.9, alinea[0].Pieces[0].Origin.X, "première ligne seule : l'alinéa recréé à 18,9");
            t.Equal(0.0, alinea[1].Pieces[0].Origin.X, "première ligne seule : la suite reste à la marge");
            document.Paragraphs[1].Indent = 37.8;
            document.Paragraphs[1].FirstIndent = 0;
            engine.ComposeAll();
            var hanging = engine.Current.Paragraphs[1].Lines;
            t.Equal(0.0, hanging[0].Pieces[0].Origin.X, "retrait suspendu : la première ligne à la marge");
            t.Equal(37.8, hanging[1].Pieces[0].Origin.X, "retrait suspendu : les suivantes décalées");
            document.Paragraphs[0].FirstIndent = 0; // sans Indent : l'alinéa du style effacé, le bloc au style
            engine.ComposeAll();
            t.Equal(0.0, engine.Current.Paragraphs[0].Lines[0].Pieces[0].Origin.X, "FirstIndent 0 sans décalage de bloc : plus d'alinéa du style");
        }

        /// <summary>Le bouton « Césure » du document (PageSetup.Hyphenation)
        /// est respecté par le compositeur : désactivé, aucun mot n'est coupé
        /// même si le style l'autorise (le mode composition l'ignorait).</summary>
        private static void DocumentToggleDisablesHyphenation(Harness t)
        {
            var document = Document(CvWord(24));
            var setup = Setup();
            setup.Hyphenation = false;
            var engine = new CompositionEngine(document, Styles(), setup,
                null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            var lines = engine.Current.Paragraphs[0].Lines;
            t.Equal(1, lines.Count, "césure du document coupée : aucune coupe");
            t.Check(!lines[0].Hyphenated, "la ligne ne porte pas de césure");
        }

        /// <summary>La césure portée par le PARAGRAPHE (livre compilé, 09/10)
        /// prime sur celle de la page : coupé malgré une page sans césure,
        /// jamais coupé malgré une page qui césure.</summary>
        private static void ParagraphHyphenationOverridesPage(Harness t)
        {
            var document = Document(CvWord(24));
            var setup = Setup();
            setup.Hyphenation = false;
            document.Paragraphs[0].Hyphenation = true;
            var engine = new CompositionEngine(document, Styles(), setup, null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            var lines = engine.Current.Paragraphs[0].Lines;
            t.Check(lines.Count >= 2 && lines[0].Hyphenated, "césure du paragraphe : coupé malgré la page sans césure");
            setup.Hyphenation = true;
            document.Paragraphs[0].Hyphenation = false;
            engine = new CompositionEngine(document, Styles(), setup, null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            lines = engine.Current.Paragraphs[0].Lines;
            t.Equal(1, lines.Count, "césure coupée au paragraphe : aucune coupe malgré la page");
            document.Paragraphs[0].Hyphenation = null;
            engine = new CompositionEngine(document, Styles(), setup, null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            t.Check(engine.Current.Paragraphs[0].Lines[0].Hyphenated, "sans avis du paragraphe : la page décide");
            // Une page NEUVE (v36) et un style Corps livré : ça coupe d'office —
            // ce que Rémi attendait d'un texte importé de Word (09/10).
            var fresh = new PageSetup { PageWidthMm = Setup().PageWidthMm, PageHeightMm = Setup().PageHeightMm, MarginLeftMm = Setup().MarginLeftMm, MarginRightMm = Setup().MarginRightMm };
            var imported = Document(CvWord(24));
            imported.Paragraphs[0].StyleId = "body";
            engine = new CompositionEngine(imported, StyleSheet.CreateDefault(), fresh, null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            t.Check(StyleSheet.CreateDefault().Body.HyphenationEnabled, "le style Corps livré césure");
            t.Check(engine.Current.Paragraphs[0].Lines[0].Hyphenated, "page neuve + Corps : le mot se coupe sans toucher au bouton");
        }

        /// <summary>Le compilateur relaie la césure de chaque écrit sur ses
        /// paragraphes (09/10), et la page qui COMPTE un écrit d'un livre est
        /// le gabarit du livre avec la césure de l'écrit — hors livre, sa
        /// page propre.</summary>
        private static void CompiledBookCarriesHyphenation(Harness t)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            var book = new BinderItem { Title = "Livre", Kind = ItemKind.Book, Parent = writings, Book = new BookInfo() };
            book.Book.Template = new PageSetup { PageWidthMm = 148, PageHeightMm = 210, Hyphenation = false };
            writings.Children.Add(book);
            var cut = new BinderItem { Title = "Un", Kind = ItemKind.Text, Parent = book, Document = TextDocument.FromPlainText("Un."), Page = new PageSetup { Hyphenation = true } };
            var plain = new BinderItem { Title = "Deux", Kind = ItemKind.Text, Parent = book, Document = TextDocument.FromPlainText("Deux.") };
            book.Children.Add(cut);
            book.Children.Add(plain);
            var compiled = Marabook.Exchange.Compiler.Build(project, book, new Marabook.Exchange.CompileOptions
            {
                TitlePage = false, ChapterHeadings = false, PageBreakPerText = true, RectoChapterStarts = true
            });
            t.Equal(2, compiled.Paragraphs.Count, "deux paragraphes compilés");
            t.Check(compiled.Paragraphs[0].Hyphenation == true, "le chapitre césuré relaie sa césure");
            t.Check(compiled.Paragraphs[1].Hyphenation == null, "le chapitre sans page propre laisse la page décider");
            var counting = BookInfo.CountingPageFor(cut, project);
            t.Check(counting.PageWidthMm == 148 && counting.Hyphenation, "la page qui compte : le gabarit du livre, avec la césure de l'écrit");
            t.Check(!BookInfo.CountingPageFor(plain, project).Hyphenation, "…sans page propre : la césure du gabarit");
            var loose = new BinderItem { Title = "Seul", Kind = ItemKind.Text, Parent = writings, Page = new PageSetup { PageWidthMm = 100 } };
            writings.Children.Add(loose);
            t.Check(BookInfo.CountingPageFor(loose, project) == loose.Page, "hors livre : sa page propre");
        }

        private static string LineText(ComposedLine line)
        {
            var sb = new StringBuilder();
            foreach (var piece in line.Pieces) sb.Append(piece.Text ?? " ");
            return sb.ToString();
        }

        /// <summary>Un mot élidé (« d’incompréhension ») et un mot composé
        /// (« peut-être ») en bout de ligne se coupent (09/10) : le premier
        /// derrière l'apostrophe avec le trait ajouté, le second à son trait
        /// existant, sans en ajouter. Colonne de 37 caractères.</summary>
        private static void ElidedAndCompoundWordsHyphenated(Harness t)
        {
            var engine = Compose(Document("xxxxxxxxxxxxxxxxxxxx d’incompréhension")); // 20 + 1 + 17 = 38 > 37
            var lines = engine.Current.Paragraphs[0].Lines;
            t.Check(lines.Count == 2 && lines[0].Hyphenated, "le mot élidé se coupe en bout de ligne (" + lines.Count + " lignes)");
            var first = LineText(lines[0]);
            t.Check(first.EndsWith("-") && !first.EndsWith("d’-") && first.Contains("d’in"), "…derrière l'apostrophe, trait ajouté (« " + first + " »)");
            engine = Compose(Document("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx peut-être")); // 30 + 1 + 9 = 40 > 37 ; « peut- » : 36
            lines = engine.Current.Paragraphs[0].Lines;
            first = lines.Count > 0 ? LineText(lines[0]) : "";
            t.Check(lines.Count == 2 && first.EndsWith("peut-") && !first.EndsWith("--"), "le mot composé se coupe à son trait, sans en ajouter (« " + first + " »)");
            t.Check(lines.Count == 2 && LineText(lines[1]) == "être", "…la suite repart entière (« " + (lines.Count > 1 ? LineText(lines[1]) : "") + " »)");
        }

        /// <summary>Une ligne justifiée au-delà des tolérances du style (09/10) :
        /// le déversoir d'urgence gonfle ses espaces, la ligne est LÂCHE
        /// (LooseRatio = largeur finale d'un espace / naturelle) ; une ligne
        /// qui se justifie dans les tolérances ne l'est pas.</summary>
        private static void LooseLineFlagged(Harness t)
        {
            var styles = Styles();
            styles.Find("body").Align = "justify";
            // 16 + 1 + 16, puis 29 : le troisième mot (sans voyelle : incoupable)
            // part à la ligne, le seul espace intérieur de la première doit
            // avaler 20 px (quatre espaces).
            var document = Document("bbbbbbbbbbbbbbbb bbbbbbbbbbbbbbbb ccccccccccccccccccccccccccccc");
            var engine = new CompositionEngine(document, styles, Setup(), null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            var lines = engine.Current.Paragraphs[0].Lines;
            t.Check(lines.Count == 2 && lines[0].Loose && lines[0].LooseRatio > 3, "une ligne dont l'espace a quadruplé est lâche (×" + (lines.Count > 0 ? lines[0].LooseRatio.ToString("0.0") : "?") + ")");
            t.Check(lines.Count == 2 && !lines[1].Loose, "la dernière ligne du paragraphe n'est jamais lâche");
            // Treize mots de deux lettres : douze tiennent (35 car.), onze
            // espaces se partagent 10 px — 1,18 espace, dans les tolérances.
            document = Document("bb bb bb bb bb bb bb bb bb bb bb bb bb bb");
            engine = new CompositionEngine(document, styles, Setup(), null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            lines = engine.Current.Paragraphs[0].Lines;
            t.Check(lines.Count == 2 && !lines[0].Loose && lines[0].LooseRatio > 1.0 && lines[0].LooseRatio < 1.5, "une ligne justifiée sans forcer n'est pas lâche (×" + (lines.Count > 0 ? lines[0].LooseRatio.ToString("0.00") : "?") + ")");
            t.Check(ComposedLine.LooseSpaceRatio == 2.0, "le seuil : des espaces doublés");
        }

        /// <summary>Un mot des exceptions de césure du projet n'est jamais
        /// coupé, même coupable — comparaison insensible à la casse.</summary>
        private static void ExceptionWordNeverHyphenated(Harness t)
        {
            var word = CvWord(24);
            var document = Document(word);
            var project = new Project();
            project.HyphenExceptions.Add(word.ToUpperInvariant());
            var engine = new CompositionEngine(document, Styles(), Setup(),
                project, false, new StubGlyphMetrics());
            engine.ComposeAll();
            var lines = engine.Current.Paragraphs[0].Lines;
            t.Equal(1, lines.Count, "mot excepté : placé sans coupe");
            t.Check(!lines[0].Hyphenated, "aucune césure sur le mot excepté");
        }

        private static void LineBreaking(Harness t)
        {
            // 10 mots de 4 caractères : 7 tiennent (25 px pièce), puis 3.
            var words = new string[10];
            for (var i = 0; i < 10; i++) words[i] = "mot" + (char)('a' + i);
            var document = Document(string.Join(" ", words));
            var engine = Compose(document);
            var lines = engine.Current.Paragraphs[0].Lines;
            t.Equal(2, lines.Count, "10 mots de 4 : deux lignes");
            t.Equal(35, lines[0].End, "7 mots + 7 espaces sur la première ligne");
            t.Check(!lines[0].Hyphenated, "aucune césure nécessaire");
        }

        private static void OversizedWordHyphenated(Harness t)
        {
            // A6 — un mot de 48 caractères dans une colonne de 37 : il DOIT
            // être coupé à la césure (coupures tous les 2 caractères, la plus
            // grande qui tient avec son tiret : 36), pas déborder en marge.
            var document = Document(CvWord(24));
            var engine = Compose(document);
            var lines = engine.Current.Paragraphs[0].Lines;
            t.Equal(2, lines.Count, "mot plus large que la colonne : coupé en deux lignes");
            t.Check(lines[0].Hyphenated, "la première ligne porte une césure");
            t.Equal(36, lines[0].End, "coupure au plus grand point qui tient (36)");

            // Dernier recours : le même mot SANS point de coupe déborde, mais
            // la composition avance (jamais de boucle infinie, jamais de
            // ligne vide).
            var stuck = Document(Unbreakable(48));
            var engine2 = Compose(stuck);
            var lines2 = engine2.Current.Paragraphs[0].Lines;
            t.Equal(1, lines2.Count, "mot incoupable : placé en débordement (dernier recours)");
            t.Equal(48, lines2[0].End, "tout le mot est sur la ligne");
        }

        /// <summary>L'interligne du document (22/09) : le multiplicateur
        /// s'applique à la valeur d'interligne du style, ligne par ligne.</summary>
        private static void DocumentLeading(Harness t)
        {
            var single = Compose(Document("Une ligne."));
            t.Equal(20.0, single.Current.Paragraphs[0].Lines[0].Height, "interligne 1 : la valeur du style (20 px)");
            var document = Document("Une ligne.");
            document.LineSpacing = 1.5;
            var wide = Compose(document);
            t.Equal(30.0, wide.Current.Paragraphs[0].Lines[0].Height, "interligne 1,5 : 30 px");
            document.LineSpacing = 2;
            t.Equal(40.0, Compose(document).Current.Paragraphs[0].Lines[0].Height, "interligne 2 : 40 px");
        }

        /// <summary>Les statistiques par la composition (22/09) valent celles
        /// du texte plat entier — sauts de ligne, notes, images et filets
        /// compris — et un paragraphe recomposé se recompte seul.</summary>
        private static void IncrementalStats(Harness t)
        {
            var document = Document("Il l'a dit, dit-il.", "Second paragraphe avec un saut", "Troisième.");
            document.Paragraphs[1].Runs.Add(new TextRun { IsLineBreak = true });
            document.Paragraphs[1].Runs.Add(new TextRun { Text = "de ligne et une note" });
            var note = new Footnote { Text = "Une note" };
            document.Footnotes.Add(note);
            document.Paragraphs[1].Runs.Add(new TextRun { Text = "1", FootnoteId = note.Id });
            document.Paragraphs[2].Runs.Add(new TextRun { IsRule = true });
            var engine = Compose(document);
            var whole = Correction.TextStats.Compute(document.ToPlainText());
            var incremental = engine.Current.Stats();
            t.Check(incremental != null && incremental.Words == whole.Words && incremental.Sec == whole.Sec && incremental.NoSpaces == whole.NoSpaces,
                "les comptes par paragraphe valent le compte du texte entier (" + incremental.Words + " mots, " + incremental.Sec + " SEC)");
            t.Check(engine.Current.Paragraphs[0].Words >= 0 && engine.Current.Paragraphs[2].Words >= 0, "…et restent posés sur les paragraphes");
            document.Paragraphs[0].Runs[0].Text = "Il l'a dit, dit-il, encore et encore.";
            engine.RecomposeParagraph(0);
            t.Check(engine.Current.Paragraphs[0].Words < 0 && engine.Current.Paragraphs[1].Words >= 0, "un paragraphe recomposé oublie son compte, les autres le gardent");
            var again = engine.Current.Stats();
            t.Equal(Correction.TextStats.Compute(document.ToPlainText()).Words, again.Words, "…et le total suit la modification");
            t.Equal(1, Correction.TextStats.From(3, 10, 8).ReadingMinutes, "From dérive les minutes de lecture");
        }

        private static void WidowControl(Harness t)
        {
            // 11 lignes, la page en tient 10 : la règle 2/2 refuse de laisser
            // la dernière ligne seule → 9 + 2.
            var words = new string[11];
            for (var i = 0; i < 11; i++) words[i] = Unbreakable(37);
            var document = Document(string.Join(" ", words));
            var engine = Compose(document);
            t.Equal(11, engine.Current.Paragraphs[0].Lines.Count,
                "un mot de 37 par ligne : 11 lignes");
            var pages = engine.Current.Pages;
            t.Equal(2, pages.Count, "deux pages");
            t.Equal(9, pages[0].Lines.Count, "veuve refusée : 9 lignes restent");
            t.Equal(2, pages[1].Lines.Count, "2 lignes passent ensemble");
            t.Check(pages[0].WidowMarks.Count > 0, "le marqueur de correction est posé");

            // AllowWidows débraye la correction : 10 + 1.
            document.Paragraphs[0].AllowWidows = true;
            var free = Compose(document);
            t.Equal(10, free.Current.Pages[0].Lines.Count,
                "AllowWidows : la page se remplit (10 lignes)");
            t.Equal(1, free.Current.Pages[1].Lines.Count, "la veuve est autorisée");
        }

        private static void OrphanControl(Harness t)
        {
            // Paragraphe A : 9 lignes. Paragraphe B : 5 lignes — sa première
            // ligne tiendrait seule en bas de page 1 : orpheline refusée, B
            // passe entier en page 2.
            var a = new string[9];
            for (var i = 0; i < 9; i++) a[i] = Unbreakable(37);
            var b = new string[5];
            for (var i = 0; i < 5; i++) b[i] = Unbreakable(37);
            var document = Document(string.Join(" ", a), string.Join(" ", b));
            var engine = Compose(document);
            var pages = engine.Current.Pages;
            t.Equal(2, pages.Count, "deux pages");
            t.Equal(9, pages[0].Lines.Count, "l'orpheline est refusée en bas de page 1");
            t.Equal(5, pages[1].Lines.Count, "le paragraphe B part entier en page 2");
        }
    }
}

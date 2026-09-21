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
            ExceptionWordNeverHyphenated(t);
            WidowControl(t);
            OrphanControl(t);
            IndentOverride(t);
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

using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C6 — la recherche pivot (batch 26, lot B.3) : positions
    /// calculées à la main, casse, mot entier, éléments U+FFFC neutres.
    /// La moitié console de la recherche/remplacement — la vue Composition
    /// ne fait que sélectionner et remplacer ce que PivotSearch trouve.</summary>
    public static class SearchTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C6 — recherche pivot");
            Positions(t);
            CaseAndWholeWord(t);
            ElementsAreNeutral(t);
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

        private static void Positions(Harness t)
        {
            var document = Document("Le marabout du marais.", "Un marabout dort.");
            var matches = PivotSearch.FindAll(document, "marabout", false, false);
            t.Equal(2, matches.Count, "deux occurrences, à travers les paragraphes");
            t.Equal(0, matches[0].ParagraphIndex, "la première au paragraphe 0");
            t.Equal(3, matches[0].Start, "à l'offset calculé à la main");
            t.Equal(8, matches[0].Length, "de la longueur du terme");
            t.Equal(1, matches[1].ParagraphIndex, "la seconde au paragraphe 1");
            t.Equal(3, matches[1].Start, "au bon offset aussi");

            var overlap = PivotSearch.FindAll(Document("aaaa"), "aa", false, false);
            t.Equal(3, overlap.Count, "les recouvrements avancent d'un caractère");
        }

        private static void CaseAndWholeWord(Harness t)
        {
            var document = Document("Marabout et MARABOUT et marabout.");
            t.Equal(3, PivotSearch.FindAll(document, "marabout", false, false).Count,
                "sans respecter la casse : trois");
            t.Equal(1, PivotSearch.FindAll(document, "marabout", true, false).Count,
                "casse respectée : une seule");

            var partial = Document("Le marais du marabout.");
            t.Equal(2, PivotSearch.FindAll(partial, "mara", false, false).Count,
                "sous-chaîne : deux départs de mots");
            t.Equal(0, PivotSearch.FindAll(partial, "mara", false, true).Count,
                "mot entier : « mara » seul n'existe pas");
            t.Equal(1, PivotSearch.FindAll(partial, "marais", false, true).Count,
                "mot entier : « marais » borné par l'espace et le « du »");
        }

        private static void ElementsAreNeutral(Harness t)
        {
            // Un appel de note (élément, U+FFFC dans le texte plat) coupe le
            // texte : « mara⬚bout » ne matche pas « marabout ».
            var document = new TextDocument();
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "mara" });
            paragraph.Runs.Add(new TextRun { FootnoteId = "n" });
            paragraph.Runs.Add(new TextRun { Text = "bout" });
            document.Paragraphs.Add(paragraph);
            document.Footnotes.Add(new Footnote { Id = "n", Text = "note" });
            t.Equal(0, PivotSearch.FindAll(document, "marabout", false, false).Count,
                "l'élément U+FFFC ne se traverse pas en silence");
            t.Equal(1, PivotSearch.FindAll(document, "bout", false, false).Count,
                "le texte après l'élément se trouve normalement");
        }
    }
}

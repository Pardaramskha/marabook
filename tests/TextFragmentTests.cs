using System.Collections.Generic;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C45 — le fragment mis en forme du presse-papiers (28/09/2026) :
    /// extraction d'une sélection (formats gardés, éléments du document
    /// retirés), insertion au caret (un ou plusieurs paragraphes, règle de la
    /// marque de paragraphe), aller-retour par le JSON du .plot.</summary>
    public static class TextFragmentTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C45 — fragment du presse-papiers : extraction, insertion, aller-retour");
            ExtractKeepsFormats(t);
            ExtractDropsDocumentElements(t);
            InsertSingleParagraph(t);
            InsertSeveralParagraphs(t);
            RoundTrip(t);
        }

        private static TextDocument Sample()
        {
            var document = new TextDocument();
            var first = new TextParagraph { StyleId = "body" };
            first.Runs.Add(new TextRun { Text = "Il pleuvait " });
            first.Runs.Add(new TextRun { Text = "fort", Bold = true, FontFamily = "EB Garamond", FontSize = 14, Color = "#C0392B" });
            first.Runs.Add(new TextRun { Text = " ce soir-là." });
            var second = new TextParagraph { StyleId = "quote", ListKind = "bullet", AlignOverride = "center" };
            second.Runs.Add(new TextRun { Text = "Note", Italic = true });
            second.Runs.Add(new TextRun { FootnoteId = "n1", Text = "1" });
            second.Runs.Add(new TextRun { IsLineBreak = true });
            second.Runs.Add(new TextRun { Text = "suite", AnnotationId = "a1", Highlight = "#FFF3A3" });
            second.Runs.Add(new TextRun { ImageId = "img1" });
            var third = new TextParagraph { StyleId = "body" };
            third.Runs.Add(new TextRun { Text = "Fin." });
            document.Paragraphs.Add(first);
            document.Paragraphs.Add(second);
            document.Paragraphs.Add(third);
            return document;
        }

        private static void ExtractKeepsFormats(Harness t)
        {
            var document = Sample();
            // « vait fort ce » : du run plat au run gras puis retour au plat.
            var fragment = TextFragment.Extract(document, 0, 7, 0, 19);
            t.Equal(1, fragment.Paragraphs.Count, "un paragraphe extrait");
            t.Equal("vait fort ce", fragment.ToPlainText(), "le texte plat de la tranche");
            var runs = fragment.Paragraphs[0].Runs;
            t.Equal(3, runs.Count, "trois runs : plat, gras, plat");
            t.Equal(true, runs[1].Bold, "le run du milieu reste gras");
            t.Equal("EB Garamond", runs[1].FontFamily, "sa police voyage");
            t.Equal(14.0, runs[1].FontSize, "sa taille voyage");
            t.Equal("#C0392B", runs[1].Color, "sa couleur voyage");
            t.Equal("Il pleuvait fort ce soir-là.", document.Paragraphs[0].ToPlainText(), "le document d'origine n'a pas bougé");
        }

        private static void ExtractDropsDocumentElements(Harness t)
        {
            var document = Sample();
            var fragment = TextFragment.Extract(document, 0, 12, 2, 4);
            t.Equal(3, fragment.Paragraphs.Count, "trois paragraphes");
            t.Equal("quote", fragment.Paragraphs[1].StyleId, "le style du paragraphe voyage");
            t.Equal("bullet", fragment.Paragraphs[1].ListKind, "la liste voyage");
            t.Equal("center", fragment.Paragraphs[1].AlignOverride, "l'alignement voyage");
            var runs = fragment.Paragraphs[1].Runs;
            var hasNote = false; var hasImage = false; var hasBreak = false; var hasAnnotation = false;
            foreach (var run in runs)
            {
                if (run.FootnoteId != null) hasNote = true;
                if (run.ImageId != null) hasImage = true;
                if (run.IsLineBreak) hasBreak = true;
                if (run.AnnotationId != null) hasAnnotation = true;
            }
            t.Check(!hasNote, "le marqueur de note ne voyage pas");
            t.Check(!hasImage, "l'image ne voyage pas");
            t.Check(!hasAnnotation, "l'ancre d'annotation est retirée");
            t.Check(hasBreak, "le saut de ligne reste");
            t.Equal("fort ce soir-là.\nNote\nsuite\nFin.", fragment.ToPlainText(), "le texte plat du fragment");
        }

        private static void InsertSingleParagraph(Harness t)
        {
            var document = TextDocument.FromPlainText("Un début et une fin.");
            document.Paragraphs[0].StyleId = "heading1";
            var fragment = new TextDocument();
            var piece = new TextParagraph { StyleId = "quote" };
            piece.Runs.Add(new TextRun { Text = "GRAS", Bold = true });
            piece.Runs.Add(new TextRun { Text = " puis plat " });
            fragment.Paragraphs.Add(piece);
            var p = 0; var o = 9; // après « Un début »
            var added = TextFragment.Insert(document, fragment, ref p, ref o);
            t.Equal(0, added, "aucun paragraphe ajouté");
            t.Equal(1, document.Paragraphs.Count, "toujours un paragraphe");
            t.Equal("Un début GRAS puis plat et une fin.", document.Paragraphs[0].ToPlainText(), "le texte inséré au caret");
            t.Equal("heading1", document.Paragraphs[0].StyleId, "le style du paragraphe d'accueil est gardé");
            t.Equal(0, p, "le caret reste dans le paragraphe");
            t.Equal(9 + "GRAS puis plat ".Length, o, "le caret est après l'insertion");
            var bold = false;
            foreach (var run in document.Paragraphs[0].Runs) if (run.Text == "GRAS" && run.Bold == true) bold = true;
            t.Check(bold, "le run gras est intact au milieu du paragraphe");
        }

        private static void InsertSeveralParagraphs(Harness t)
        {
            var document = TextDocument.FromPlainText("Avant|après");
            document.Paragraphs[0].StyleId = "body";
            var fragment = TextDocument.FromPlainText("un\ndeux\ntrois");
            fragment.Paragraphs[0].StyleId = "heading2";
            fragment.Paragraphs[1].StyleId = "quote";
            fragment.Paragraphs[2].StyleId = "heading3";
            fragment.Paragraphs[2].Runs[0].Italic = true;
            var p = 0; var o = 5; // sur le « | »
            var added = TextFragment.Insert(document, fragment, ref p, ref o);
            t.Equal(2, added, "deux paragraphes ajoutés");
            t.Equal(3, document.Paragraphs.Count, "trois paragraphes au total");
            t.Equal("Avantun", document.Paragraphs[0].ToPlainText(), "le premier morceau rejoint le début");
            t.Equal("heading2", document.Paragraphs[0].StyleId, "le premier paragraphe prend la marque du fragment");
            t.Equal("deux", document.Paragraphs[1].ToPlainText(), "le paragraphe du milieu est inséré tel quel");
            t.Equal("quote", document.Paragraphs[1].StyleId, "avec son style");
            t.Equal("trois|après", document.Paragraphs[2].ToPlainText(), "le dernier morceau rejoint la fin");
            t.Equal("body", document.Paragraphs[2].StyleId, "la fin garde le style du paragraphe d'accueil");
            t.Equal(true, document.Paragraphs[2].Runs[0].Italic, "le format du dernier morceau est gardé");
            t.Equal(2, p, "le caret est dans le dernier paragraphe");
            t.Equal(5, o, "le caret est après « trois »");
        }

        private static void RoundTrip(Harness t)
        {
            var fragment = TextFragment.Extract(Sample(), 0, 0, 1, 4);
            var json = PlotFile.SerializeDocument(fragment);
            var back = PlotFile.DeserializeDocument(json);
            t.Equal(fragment.ToPlainText(), back.ToPlainText(), "le texte survit au JSON");
            t.Equal(2, back.Paragraphs.Count, "deux paragraphes");
            var runs = back.Paragraphs[0].Runs;
            t.Equal(3, runs.Count, "les trois runs du premier paragraphe");
            t.Equal(true, runs[1].Bold, "gras");
            t.Equal("EB Garamond", runs[1].FontFamily, "police");
            t.Equal(14.0, runs[1].FontSize, "taille");
            t.Equal("#C0392B", runs[1].Color, "couleur");
            t.Equal("quote", back.Paragraphs[1].StyleId, "le style du second paragraphe");
            t.Equal(true, back.Paragraphs[1].Runs[0].Italic, "l'italique du second paragraphe");
        }
    }
}

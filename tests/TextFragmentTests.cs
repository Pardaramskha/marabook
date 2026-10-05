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
            PasteSeveralParagraphsThroughEngine(t);
            PasteLargeSelectionIntoEmptyDocument(t);
            AdoptNotesAndAnnotations(t);
            PackAndAdoptImages(t);
        }

        /// <summary>Le collage tel que l'éditeur le fait (PasteFragment) : Insert,
        /// puis RecomposeParagraph du paragraphe de départ, puis ParagraphInserted
        /// pour chaque paragraphe ajouté. Hotfix 1.0.3-a : la recomposition du
        /// paragraphe de départ parcourait le document AGRANDI avec l'ancienne
        /// liste des paragraphes composés → « Index was out of range ».</summary>
        private static void PasteSeveralParagraphsThroughEngine(Harness t)
        {
            var document = TextDocument.FromPlainText("Avant|après\nSuite");
            var engine = new Print.CompositionEngine(document, StyleSheet.CreateDefault(),
                new PageSetup(), null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            var fragment = TextDocument.FromPlainText("un\ndeux\ntrois");
            var p = 0; var o = 5;
            var added = TextFragment.Insert(document, fragment, ref p, ref o);
            t.Equal(2, added, "deux paragraphes ajoutés au document");
            var crashed = false;
            try { engine.ParagraphsInserted(1, added); }
            catch (System.ArgumentOutOfRangeException) { crashed = true; }
            t.Check(!crashed, "apprendre le bloc inséré d'un coup ne lève pas d'index hors limites");
            t.Equal(document.Paragraphs.Count, engine.Current.Paragraphs.Count, "autant de paragraphes composés que dans le document");
            t.Equal("Avantun".Length, engine.Current.Paragraphs[0].FlatLength, "le premier paragraphe composé est le bon");
            t.Equal("trois|après".Length, engine.Current.Paragraphs[2].FlatLength, "le dernier morceau est composé");
            t.Equal("Suite".Length, engine.Current.Paragraphs[3].FlatLength, "le paragraphe suivant a glissé d'une place");
        }

        /// <summary>Une grande sélection (des dizaines de paragraphes) collée
        /// dans un écrit neuf : l'ordre d'apprentissage ne doit pas dépendre du
        /// nombre de paragraphes.</summary>
        private static void PasteLargeSelectionIntoEmptyDocument(Harness t)
        {
            var source = new TextDocument();
            for (var i = 0; i < 60; i++)
            {
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun { Text = "Paragraphe " + i + " de la source, assez long pour tenir sur quelques lignes de la page." });
                source.Paragraphs.Add(paragraph);
            }
            var fragment = TextFragment.Extract(source, 0, 0, 59, 10);
            var json = PlotFile.SerializeDocument(fragment);
            var back = PlotFile.DeserializeDocument(json);
            t.Equal(60, back.Paragraphs.Count, "soixante paragraphes relus du presse-papiers");
            var document = TextDocument.FromPlainText("");
            var engine = new Print.CompositionEngine(document, StyleSheet.CreateDefault(),
                new PageSetup(), null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            var p = 0; var o = 0;
            var added = TextFragment.Insert(document, back, ref p, ref o);
            t.Equal(59, added, "cinquante-neuf paragraphes ajoutés");
            var crashed = false;
            try { engine.ParagraphsInserted(1, added); }
            catch (System.ArgumentOutOfRangeException) { crashed = true; }
            t.Check(!crashed, "le grand collage ne lève pas d'index hors limites");
            engine.ParagraphsInserted(1, 0); // un bloc vide : simple recomposition du paragraphe d'accueil
            t.Equal(60, engine.Current.Paragraphs.Count, "un bloc vide n'ajoute rien à la composition");
            t.Equal(60, engine.Current.Paragraphs.Count, "soixante paragraphes composés");
            t.Equal(59, p, "le caret est dans le dernier paragraphe collé");
            t.Equal(10, o, "le caret est après le texte collé");
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
            document.Footnotes.Add(new Footnote { Id = "n1", Text = "Première note" });
            document.Annotations.Add(new Annotation { Id = "a1", Text = "Un commentaire", Created = "2026-10-05 12:00" });
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

        /// <summary>Hotfix 1.0.3-a : TOUT voyage — l'appel de note avec sa
        /// note, l'ancre avec son annotation, l'image (ses octets par le
        /// paquet), le saut de ligne. Avant, le fragment les retirait.</summary>
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
            t.Check(hasNote, "l'appel de note voyage");
            t.Equal(1, fragment.Footnotes.Count, "…avec le corps de sa note");
            t.Equal("Première note", fragment.Footnotes[0].Text, "le texte de la note");
            t.Check(hasImage, "l'image voyage");
            t.Check(hasAnnotation, "l'ancre d'annotation voyage");
            t.Equal(1, fragment.Annotations.Count, "…avec son commentaire");
            t.Equal("Un commentaire", fragment.Annotations[0].Text, "le texte de l'annotation");
            t.Check(hasBreak, "le saut de ligne reste");
            t.Equal("fort ce soir-là.\nNote\nsuite\nFin.", fragment.ToPlainText(), "le texte plat du fragment");

            // Une tranche qui ne touche ni la note ni l'annotation n'emporte rien.
            var bare = TextFragment.Extract(document, 0, 0, 0, 5);
            t.Equal(0, bare.Footnotes.Count, "pas de note hors de la tranche");
            t.Equal(0, bare.Annotations.Count, "pas d'annotation hors de la tranche");
        }

        /// <summary>L'adoption à l'insertion : copie dans le même document →
        /// identifiants neufs (l'original est en usage) ; collage après un
        /// couper (la note orpheline est encore là) → identifiant repris ;
        /// autre document → ajoutés tels quels.</summary>
        private static void AdoptNotesAndAnnotations(Harness t)
        {
            var document = Sample();
            var fragment = TextFragment.Extract(document, 1, 0, 1, 8); // « Note¹ ⏎ su » : note + annotation
            var p = 2; var o = 4; // à la fin de « Fin. »
            TextFragment.Insert(document, fragment, ref p, ref o);
            t.Equal(2, document.Footnotes.Count, "copie dans le même écrit : une seconde note");
            t.Check(document.Footnotes[1].Id != "n1", "…sous un identifiant neuf");
            t.Equal("Première note", document.Footnotes[1].Text, "…avec le même texte");
            t.Equal(2, document.Annotations.Count, "copie : une seconde annotation");
            t.Check(document.Annotations[1].Id != "a1", "…sous un identifiant neuf");
            var lastRuns = document.Paragraphs[2].Runs;
            var markerId = ""; var anchorId = "";
            foreach (var run in lastRuns)
            {
                if (run.FootnoteId != null) markerId = run.FootnoteId;
                if (run.AnnotationId != null) anchorId = run.AnnotationId;
            }
            t.Equal(document.Footnotes[1].Id, markerId, "l'appel collé cite la note neuve");
            t.Equal(document.Annotations[1].Id, anchorId, "l'ancre collée cite l'annotation neuve");

            // Couper puis coller : le marqueur a disparu, la note est orpheline → reprise.
            var cut = Sample();
            var piece = TextFragment.Extract(cut, 1, 0, 1, 11); // tout le passage annoté part avec la coupe
            PivotEdit.DeleteInParagraph(cut.Paragraphs[1], 0, 11);
            p = 0; o = 0;
            TextFragment.Insert(cut, piece, ref p, ref o);
            t.Equal(1, cut.Footnotes.Count, "couper-coller : toujours une seule note");
            t.Equal("n1", cut.Footnotes[0].Id, "…celle d'origine, reprise");
            t.Equal(1, cut.Annotations.Count, "couper-coller : toujours une seule annotation");

            // Vers un autre écrit : ajoutées telles quelles.
            var other = TextDocument.FromPlainText("Ailleurs");
            var travel = TextFragment.Extract(Sample(), 1, 0, 1, 8);
            p = 0; o = 8;
            TextFragment.Insert(other, travel, ref p, ref o);
            t.Equal(1, other.Footnotes.Count, "autre écrit : la note arrive");
            t.Equal("n1", other.Footnotes[0].Id, "…sous son identifiant");
            t.Equal(1, other.Annotations.Count, "autre écrit : l'annotation arrive");
        }

        /// <summary>Les images : le paquet porte leurs octets ; dans le même
        /// projet l'identifiant est gardé, dans un autre les octets entrent
        /// au magasin sous un identifiant neuf ; sans projet, le run tombe.</summary>
        private static void PackAndAdoptImages(Harness t)
        {
            var project = new Project();
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            var id = project.AddImage(bytes, ".png");
            var document = Sample();
            foreach (var run in document.Paragraphs[1].Runs) if (run.ImageId == "img1") run.ImageId = id;
            var package = TextFragment.Pack(document, project, 1, 0, 2, 4);
            t.Equal(1, package.Images.Count, "le paquet porte l'image");
            t.Equal(id, package.Images[0].Id, "…sous son identifiant");
            t.Equal(5, package.Images[0].Bytes.Length, "…avec ses octets");

            var json = PlotFile.SerializeFragment(package);
            var back = PlotFile.DeserializeFragment(json);
            t.Equal(1, back.Images.Count, "l'image survit au JSON");
            t.Equal(3, back.Images[0].Bytes[2], "…octet pour octet");
            t.Equal(1, back.Document.Footnotes.Count, "la note survit au JSON du paquet");
            t.Equal(1, back.Document.Annotations.Count, "l'annotation survit au JSON du paquet");
            t.Equal(2, back.Document.Paragraphs.Count, "deux paragraphes");

            var same = TextDocument.FromPlainText("Même projet");
            var p = 0; var o = 11;
            TextFragment.Insert(same, PlotFile.DeserializeFragment(json), project, ref p, ref o);
            t.Equal(id, FirstImageId(same), "même projet : l'identifiant de l'image est gardé");
            t.Equal(1, project.Images.Count, "…et le magasin n'a pas de doublon");

            var otherProject = new Project();
            var elsewhere = TextDocument.FromPlainText("Autre projet");
            p = 0; o = 12;
            TextFragment.Insert(elsewhere, PlotFile.DeserializeFragment(json), otherProject, ref p, ref o);
            var fresh = FirstImageId(elsewhere);
            t.Check(fresh != null && fresh != id, "autre projet : un identifiant neuf");
            t.Check(otherProject.FindImage(fresh) != null && otherProject.FindImage(fresh).Bytes.Length == 5, "…et les octets sont au magasin");

            var nowhere = TextDocument.FromPlainText("Sans projet");
            p = 0; o = 11;
            TextFragment.Insert(nowhere, PlotFile.DeserializeFragment(json), null, ref p, ref o);
            t.Equal(null, FirstImageId(nowhere), "sans projet : pas d'image fantôme");

            var old = PlotFile.DeserializeFragment(PlotFile.SerializeDocument(document));
            t.Equal(3, old.Document.Paragraphs.Count, "le JSON d'un document nu (ancien format) se relit encore");
        }

        private static string FirstImageId(TextDocument document)
        {
            foreach (var paragraph in document.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.ImageId != null) return run.ImageId;
            return null;
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

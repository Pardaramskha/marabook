using System.Collections.Generic;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C41 — le patch 0.50.0 (25/09/2026), côté modèle : l'auto-
    /// sélecteur de mot (bornes de mot, règle du cliquer-glisser façon Word),
    /// le format d'insertion (InsertText avec format), les notes de bas de
    /// page mises en forme (runs, projection texte, style « footnote »,
    /// persistance v31, clonage).</summary>
    public static class Patch050Tests
    {
        public static void Run(Harness t)
        {
            t.Suite("C41 — patch 0.50.0 : sélection par mot, format d'insertion, notes riches");
            WordBounds(t);
            SnapWordSelection(t);
            InsertWithFormat(t);
            FootnoteModel(t);
            FootnoteStyle(t);
            FootnotePersistence(t);
        }

        // ------------------------------------------------------------ mots

        private static void WordBounds(Harness t)
        {
            int start, end;
            var text = "Bonjour le monde.";
            t.Check(PivotEdit.WordBounds(text, 2, out start, out end) && start == 0 && end == 7, "au milieu de « Bonjour » : 0..7");
            t.Check(PivotEdit.WordBounds(text, 7, out start, out end) && start == 0 && end == 7, "un caret en fin de mot appartient au mot");
            t.Check(PivotEdit.WordBounds(text, 0, out start, out end) && start == 0 && end == 7, "au tout début du mot");
            t.Check(PivotEdit.WordBounds(text, 8, out start, out end) && start == 8 && end == 10, "« le » : 8..10");
            t.Check(PivotEdit.WordBounds(text, 11, out start, out end) && start == 11 && end == 16, "« monde » sans le point : 11..16");
            t.Check(PivotEdit.WordBounds(text, 17, out start, out end) && start == 11 && end == 16, "après le point final : le mot d'avant (règle du double-clic d'origine)");
            t.Check(!PivotEdit.WordBounds("monde...", 8, out start, out end), "après deux signes de ponctuation : hors de tout mot");
            t.Check(!PivotEdit.WordBounds("", 0, out start, out end), "texte vide : aucun mot");
            t.Check(!PivotEdit.WordBounds("... ...", 4, out start, out end), "que de la ponctuation : aucun mot");
            t.Check(PivotEdit.WordBounds("l'été 2026", 6, out start, out end) && start == 6 && end == 10, "les chiffres font un mot");
        }

        private static void SnapWordSelection(Harness t)
        {
            var text = "Bonjour le monde entier";
            int anchor, caret;
            // Glisser en avant depuis le milieu de « Bonjour » jusqu'au milieu de « monde ».
            PivotEdit.SnapWordSelection(text, 0, 3, text, 0, 13, false, out anchor, out caret);
            t.Equal(0, anchor, "en avant, hors du mot de départ : l'ancre saute au début de « Bonjour »");
            t.Equal(16, caret, "… et le caret à la fin de « monde »");
            // Tant qu'on reste dans le mot de départ : au caractère.
            PivotEdit.SnapWordSelection(text, 0, 3, text, 0, 5, false, out anchor, out caret);
            t.Check(anchor == 3 && caret == 5, "dans le mot de départ : sélection au caractère (3..5)");
            PivotEdit.SnapWordSelection(text, 0, 3, text, 0, 7, false, out anchor, out caret);
            t.Check(anchor == 3 && caret == 7, "jusqu'à la fin du mot de départ : encore au caractère");
            // En arrière depuis le milieu de « monde » jusqu'au milieu de « le ».
            PivotEdit.SnapWordSelection(text, 0, 13, text, 0, 9, false, out anchor, out caret);
            t.Equal(16, anchor, "en arrière : l'ancre saute à la fin de « monde »");
            t.Equal(8, caret, "… et le caret au début de « le »");
            // Le caret sur un espace : il reste où il est, l'ancre saute.
            PivotEdit.SnapWordSelection(text, 0, 3, text, 0, 10, false, out anchor, out caret);
            t.Check(anchor == 0 && caret == 10, "caret juste après « le » : fin de « le » (10)");
            // Sans mouvement : rien ne bouge.
            PivotEdit.SnapWordSelection(text, 0, 3, text, 0, 3, false, out anchor, out caret);
            t.Check(anchor == 3 && caret == 3, "sans mouvement : ancre et caret inchangés");
            // Mots entiers (glisser depuis un double-clic) : même dans le mot de départ.
            PivotEdit.SnapWordSelection(text, 0, 3, text, 0, 5, true, out anchor, out caret);
            t.Check(anchor == 0 && caret == 7, "mots entiers : « Bonjour » entier même à l'intérieur");
            PivotEdit.SnapWordSelection(text, 0, 3, text, 0, 3, true, out anchor, out caret);
            t.Check(anchor == 0 && caret == 7, "mots entiers sans mouvement : le mot du double-clic");
            PivotEdit.SnapWordSelection(text, 0, 13, text, 0, 9, true, out anchor, out caret);
            t.Check(anchor == 16 && caret == 8, "mots entiers en arrière : « le monde »");
            // Ancre juste après un mot : elle lui appartient (règle du caret en fin de mot).
            PivotEdit.SnapWordSelection(text, 0, 7, text, 0, 13, false, out anchor, out caret);
            t.Check(anchor == 0 && caret == 16, "ancre juste après « Bonjour » : « Bonjour le monde »");
            // Ancre hors de tout mot : pas de mot de départ, le caret s'aligne quand même.
            var dashed = "-- Bonjour le monde";
            PivotEdit.SnapWordSelection(dashed, 0, 1, dashed, 0, 16, false, out anchor, out caret);
            t.Check(anchor == 1 && caret == 19, "ancre dans la ponctuation : l'ancre reste, le caret finit « monde »");
            // D'un paragraphe à l'autre : avant/arrière se lisent sur l'index de paragraphe.
            var next = "Deuxième paragraphe";
            PivotEdit.SnapWordSelection(text, 0, 3, next, 1, 4, false, out anchor, out caret);
            t.Check(anchor == 0 && caret == 8, "vers le paragraphe suivant : « Deuxième » entier");
            PivotEdit.SnapWordSelection(next, 1, 4, text, 0, 13, false, out anchor, out caret);
            t.Check(anchor == 8 && caret == 11, "vers le paragraphe précédent : fin de « Deuxième », début de « monde »");
        }

        // ------------------------------------------------- format d'insertion

        private static void InsertWithFormat(Harness t)
        {
            var empty = new TextParagraph();
            PivotEdit.InsertText(empty, 0, "a", new TextRun { FontFamily = "Garamond", FontSize = 20 });
            t.Equal(1, empty.Runs.Count, "paragraphe vide : un run");
            t.Check(empty.Runs[0].FontFamily == "Garamond" && empty.Runs[0].FontSize == 20, "… qui porte le format d'insertion");
            PivotEdit.InsertText(empty, 1, "b");
            t.Equal(1, empty.Runs.Count, "la frappe suivante (sans format) prolonge le run");
            t.Equal("ab", empty.Runs[0].Text, "texte « ab »");

            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "Bonjour monde" });
            PivotEdit.InsertText(paragraph, 7, " beau", new TextRun { Italic = true });
            t.Equal(3, paragraph.Runs.Count, "au milieu d'un run : trois runs");
            t.Check(paragraph.Runs[0].Text == "Bonjour" && paragraph.Runs[1].Text == " beau" && paragraph.Runs[2].Text == " monde", "le texte est bien coupé");
            t.Check(paragraph.Runs[1].Italic == true && paragraph.Runs[0].Italic == null && paragraph.Runs[2].Italic == null, "seul le milieu est italique");

            var same = new TextParagraph();
            same.Runs.Add(new TextRun { Text = "abc", Bold = true });
            PivotEdit.InsertText(same, 3, "d", new TextRun { Bold = true });
            t.Equal(1, same.Runs.Count, "même format que le voisin : fusionné");
            t.Equal("abcd", same.Runs[0].Text, "« abcd »");

            var plain = new TextParagraph();
            plain.Runs.Add(new TextRun { Text = "abc" });
            PivotEdit.InsertText(plain, 3, "d", null);
            t.Check(plain.Runs.Count == 1 && plain.Runs[0].Text == "abcd", "format null : la règle ordinaire (héritage)");
        }

        // ------------------------------------------------------------ notes

        private static void FootnoteModel(Harness t)
        {
            var note = new Footnote { Text = "Une note." };
            t.Equal(1, note.Runs.Count, "Text posé : un run nu");
            t.Equal("Une note.", note.Text, "la projection rend le texte");
            t.Check(!note.HasFormatting, "un run nu n'a pas de format");

            note.Runs.Clear();
            note.Runs.Add(new TextRun { Text = "Voir " });
            note.Runs.Add(new TextRun { Text = "Dune", Italic = true });
            note.Runs.Add(new TextRun { Text = ", p. 12." });
            t.Equal("Voir Dune, p. 12.", note.Text, "la projection concatène les runs");
            t.Check(note.HasFormatting, "un run italique : formatée");
            var key = note.FormatKey();
            note.Runs[1].Italic = null;
            t.Check(note.FormatKey() != key, "la clé change avec le format, à texte égal");
            note.Runs[1].Italic = true;

            var copy = note.Clone();
            t.Equal(note.Id, copy.Id, "le clone garde l'id");
            copy.Runs[1].Text = "Fondation";
            t.Equal("Dune", note.Runs[1].Text, "le clone est profond");

            var paragraph = note.ToParagraph(StyleSheet.FootnoteId);
            t.Check(paragraph.StyleId == StyleSheet.FootnoteId && paragraph.Runs.Count == 3, "ToParagraph : style footnote, trois runs");

            var mixed = new List<TextRun>
            {
                new TextRun { Text = "a" },
                new TextRun { FootnoteId = "x" },
                new TextRun { ImageId = "img" },
                new TextRun { IsLineBreak = true },
                new TextRun { Text = "b", Bold = true }
            };
            note.SetRuns(mixed);
            t.Equal(3, note.Runs.Count, "SetRuns écarte appels et images, garde le saut de ligne");
            t.Equal("a\nb", note.Text, "la projection rend le saut de ligne");

            var style = new ParagraphStyle { FontFamily = "Garamond", FontSize = 13, Bold = false, Italic = false };
            var imported = new Footnote();
            imported.Runs.Add(new TextRun { Text = "x", FontFamily = "Garamond", FontSize = 13.05, Bold = false, Italic = true });
            imported.NormalizeAgainst(style);
            var run = imported.Runs[0];
            t.Check(run.FontFamily == null && run.FontSize == null && run.Bold == null && run.Italic == true,
                "NormalizeAgainst efface ce que le style dit déjà, garde l'italique");

            var document = new TextDocument();
            document.Paragraphs.Add(new TextParagraph());
            document.Footnotes.Add(note);
            var cloned = PivotEdit.Clone(document);
            t.Check(cloned.Footnotes[0].Runs.Count == 3 && cloned.Footnotes[0].Runs[2].Bold == true, "PivotEdit.Clone garde les runs des notes");
        }

        private static void FootnoteStyle(Harness t)
        {
            var sheet = StyleSheet.CreateDefault();
            var footnote = sheet.Find(StyleSheet.FootnoteId);
            t.Equal(StyleSheet.FootnoteId, footnote.Id, "la feuille par défaut a le style « footnote »");
            t.Equal("Notes de bas de page", footnote.Name, "son nom");
            t.Check(System.Math.Abs(footnote.FontSize - sheet.Body.FontSize * 0.85) < 0.05, "85 % du corps");
            t.Equal(sheet.Body.FontFamily, footnote.FontFamily, "la police du corps");
            t.Equal(0.0, footnote.FirstLineIndent, "sans alinéa");
            t.Check(footnote.IsGlobal, "global");
            t.Check(sheet.VisibleFor(null).Contains(footnote), "offert dans la liste des styles");

            var legacy = new StyleSheet();
            legacy.Styles.Add(new ParagraphStyle { Id = "body", Name = "Corps", FontFamily = "Garamond", FontSize = 20 });
            var fallback = legacy.FootnoteStyle();
            t.Check(fallback.Id == StyleSheet.FootnoteId && fallback.FontFamily == "Garamond" && System.Math.Abs(fallback.FontSize - 17) < 0.05,
                "feuille d'avant : FootnoteStyle() dérive du corps sans l'ajouter");
            t.Check(legacy.Find(StyleSheet.FootnoteId).Id == "body", "… et la feuille n'a pas changé");
            var ensured = legacy.EnsureFootnoteStyle();
            t.Check(legacy.Find(StyleSheet.FootnoteId) == ensured, "EnsureFootnoteStyle l'ajoute");
            t.Check(legacy.EnsureFootnoteStyle() == ensured && legacy.Styles.Count == 2, "… une seule fois");
        }

        private static void FootnotePersistence(Harness t)
        {
            var document = new TextDocument();
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "Texte" });
            var rich = new Footnote();
            rich.Runs.Add(new TextRun { Text = "Voir " });
            rich.Runs.Add(new TextRun { Text = "Dune", Italic = true, FontFamily = "Garamond" });
            var plain = new Footnote { Text = "Nue." };
            paragraph.Runs.Add(new TextRun { FootnoteId = rich.Id });
            paragraph.Runs.Add(new TextRun { FootnoteId = plain.Id });
            document.Paragraphs.Add(paragraph);
            document.Footnotes.Add(rich);
            document.Footnotes.Add(plain);

            var json = PlotFile.SerializeDocument(document);
            var root = Json.AsObject(Json.Parse(json));
            var notes = Json.AsList(Json.Field(root, "footnotes"));
            t.Equal(2, notes.Count, "deux notes écrites");
            var first = Json.AsObject(notes[0]);
            t.Equal("Voir Dune", Json.AsString(Json.Field(first, "text")), "la note riche garde sa projection « text »");
            t.Check(Json.AsList(Json.Field(first, "runs")) != null, "… et porte ses « runs »");
            var second = Json.AsObject(notes[1]);
            t.Check(Json.Field(second, "runs") == null, "la note nue n'écrit pas de « runs »");

            var back = PlotFile.DeserializeDocument(json);
            t.Equal(2, back.Footnotes.Count, "deux notes relues");
            var readRich = back.FindFootnote(rich.Id);
            t.Check(readRich != null && readRich.Runs.Count == 2 && readRich.Runs[1].Italic == true && readRich.Runs[1].FontFamily == "Garamond",
                "la note riche revient avec ses formats");
            t.Equal("Nue.", back.FindFootnote(plain.Id).Text, "la note nue revient telle quelle");

            // Un .plot d'avant (v30) : « text » seul.
            var legacy = "{\"paragraphs\":[{\"runs\":[{\"t\":\"a\"},{\"fn\":\"n1\"}]}],\"footnotes\":[{\"id\":\"n1\",\"text\":\"Ancienne\"}]}";
            var old = PlotFile.DeserializeDocument(legacy);
            t.Check(old.Footnotes.Count == 1 && old.Footnotes[0].Text == "Ancienne" && old.Footnotes[0].Runs.Count == 1,
                "une note d'avant se lit en un run nu");
        }
    }
}

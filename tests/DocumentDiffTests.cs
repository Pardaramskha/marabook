using System;
using System.Collections.Generic;
using UniversSale.Model;

namespace UniversSale.Tests
{
    /// <summary>C17 — les versions d'écrits (batch 38) : le diff à deux
    /// niveaux (paragraphes alignés, caractères sur les modifiés seulement),
    /// déplacement, réécriture au-delà du plafond, style seul, notes et
    /// annotations, documents vides et identiques, alignement grossier ; le
    /// rendu synthétique (barré / souligné, repli des inchangés, index de
    /// navigation) ; CharDiff depuis son nouveau foyer.</summary>
    public static class DocumentDiffTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C17 — versions d'écrits");
            CharDiffHome(t);
            Alignment(t);
            MovedAndRewritten(t);
            StyleOnly(t);
            Sides(t);
            Edges(t);
            Rendering(t);
        }

        private static TextDocument Doc(params string[] paragraphs)
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

        private static string Kinds(DocumentDelta delta)
        {
            var parts = new List<string>();
            foreach (var paragraph in delta.Paragraphs)
            {
                switch (paragraph.Kind)
                {
                    case ParagraphChange.Unchanged: parts.Add("="); break;
                    case ParagraphChange.Added: parts.Add("+"); break;
                    case ParagraphChange.Removed: parts.Add("-"); break;
                    case ParagraphChange.Modified: parts.Add("~"); break;
                    case ParagraphChange.Rewritten: parts.Add("!"); break;
                    case ParagraphChange.Moved: parts.Add(">"); break;
                    case ParagraphChange.StyleOnly: parts.Add("s"); break;
                }
            }
            return string.Join("", parts.ToArray());
        }

        private static void CharDiffHome(Harness t)
        {
            // Les mêmes vérifications que C13, depuis Model.
            var ops = Model.CharDiff.Diff("l'ami...", "l’ami…");
            t.Check(ops != null && ops.Count > 0, "CharDiff vit dans Model et diffe toujours");
            t.Equal(0, Model.CharDiff.Diff("", "").Count, "diff vide");
            t.Check(Model.CharDiff.Diff("abc", "abc").Count == 3, "identiques : trois conservés");
            t.Check(Model.CharDiff.Diff(new string('a', 2000), new string('b', 2000)) == null, "plafond de 800 : null, jamais une boucle");
        }

        private static void Alignment(Harness t)
        {
            var old = Doc("Un", "Deux", "Trois", "Quatre");
            var fresh = Doc("Un", "Deux bis", "Trois", "Cinq", "Quatre");
            var delta = DocumentDiff.Compare(old, fresh);
            t.Equal("=~=+=", Kinds(delta), "inchangé, modifié, inchangé, ajouté, inchangé — dans l'ordre de la version récente");
            t.Check(delta.Modified == 1 && delta.Added == 1 && delta.Removed == 0 && delta.Unchanged == 3, "les comptes");
            var modified = delta.Paragraphs[1];
            t.Check(modified.OldIndex == 1 && modified.NewIndex == 1 && modified.Ops != null, "le paragraphe modifié connaît ses deux places et son diff de caractères");
            var inserted = "";
            foreach (var op in modified.Ops) if (op.Type == '+') inserted += op.Char;
            t.Equal(" bis", inserted, "…qui ne voit que l'insertion");
            t.Check(delta.Paragraphs[3].OldIndex == -1 && delta.Paragraphs[3].NewIndex == 3, "l'ajouté n'a pas d'ancienne place");

            var removed = DocumentDiff.Compare(Doc("Un", "Deux", "Trois"), Doc("Un", "Trois"));
            t.Equal("=-=", Kinds(removed), "un paragraphe supprimé reste à sa place dans la lecture");
            t.Check(removed.Paragraphs[1].NewIndex == -1 && removed.Paragraphs[1].OldIndex == 1, "…sans nouvelle place");
            t.Equal("1 paragraphe supprimé", removed.Summary(), "le compte honnête");
            t.Equal("1 paragraphe modifié, 1 ajouté", delta.Summary(), "…au pluriel des natures");

            // Un bloc de deux supprimés et trois ajoutés : appariés dans l'ordre, le reste ajouté.
            var block = DocumentDiff.Compare(Doc("A", "B x", "C x", "D"), Doc("A", "B y", "C y", "E", "D"));
            t.Equal("=~~+=", Kinds(block), "dans un bloc, supprimés et ajoutés s'apparient dans l'ordre");
        }

        private static void MovedAndRewritten(Harness t)
        {
            var old = Doc("Un", "Deux", "Trois", "Quatre");
            var fresh = Doc("Un", "Trois", "Quatre", "Deux");
            var delta = DocumentDiff.Compare(old, fresh);
            t.Equal("===>", Kinds(delta), "un paragraphe déplacé est reconnu déplacé, pas supprimé puis ajouté");
            t.Check(delta.Moved == 1 && delta.Removed == 0 && delta.Added == 0, "…et compté une fois");
            t.Check(delta.Paragraphs[3].OldIndex == 1 && delta.Paragraphs[3].NewIndex == 3, "…avec ses deux places");
            t.Equal("1 paragraphe déplacé", delta.Summary(), "dit");

            var swapped = DocumentDiff.Compare(Doc("Un", "Deux", "Trois", "Quatre"), Doc("Quatre", "Deux", "Trois", "Un"));
            t.Check(swapped.Moved == 2 && swapped.Unchanged == 2, "deux paragraphes échangés autour d'un cœur stable : deux déplacés");
            var emptyMove = DocumentDiff.Compare(Doc("A", "", "B"), Doc("A", "B", ""));
            t.Check(emptyMove.Moved == 0, "un paragraphe vide n'est jamais « déplacé »");

            var rewritten = DocumentDiff.Compare(Doc("Un", new string('a', 1200), "Trois"), Doc("Un", new string('b', 1200), "Trois"));
            t.Equal("=!=", Kinds(rewritten), "au-delà du plafond de CharDiff : entièrement réécrit");
            t.Check(rewritten.Paragraphs[1].Ops == null && rewritten.Rewritten == 1, "…sans diff de caractères");
            t.Equal("1 paragraphe entièrement réécrit", rewritten.Summary(), "dit");
        }

        private static void StyleOnly(Harness t)
        {
            var old = Doc("Un", "Deux");
            var fresh = Doc("Un", "Deux");
            fresh.Paragraphs[1].StyleId = "heading1";
            var delta = DocumentDiff.Compare(old, fresh);
            t.Equal("=s", Kinds(delta), "même texte, autre style : style seul");
            t.Equal("1 changement de style seul", delta.Summary(), "dit");
            var bold = Doc("Un", "Deux");
            bold.Paragraphs[0].Runs[0].Bold = true;
            t.Equal("s=", Kinds(DocumentDiff.Compare(old, bold)), "une mise en forme de run changée compte aussi comme style seul");
            var split = Doc("Un", "Deux");
            split.Paragraphs[0].Runs.Clear();
            split.Paragraphs[0].Runs.Add(new TextRun { Text = "U" });
            split.Paragraphs[0].Runs.Add(new TextRun { Text = "n" });
            t.Equal("s=", Kinds(DocumentDiff.Compare(old, split)), "un découpage de runs différent (même texte, même format) est signalé — limite assumée");
            var aligned = Doc("Un", "Deux");
            aligned.Paragraphs[1].AlignOverride = "center";
            t.Equal("=s", Kinds(DocumentDiff.Compare(old, aligned)), "un alignement forcé aussi");
        }

        private static void Sides(Harness t)
        {
            var old = Doc("Un");
            var kept = new Footnote { Text = "gardée" };
            var changed = new Footnote { Text = "avant" };
            var dropped = new Footnote { Text = "partie" };
            old.Footnotes.Add(kept); old.Footnotes.Add(changed); old.Footnotes.Add(dropped);
            var pending = new Annotation { Text = "à revoir" };
            var done = new Annotation { Text = "fait" };
            old.Annotations.Add(pending); old.Annotations.Add(done);
            var fresh = Doc("Un");
            fresh.Footnotes.Add(new Footnote { Id = kept.Id, Text = "gardée" });
            fresh.Footnotes.Add(new Footnote { Id = changed.Id, Text = "après" });
            fresh.Footnotes.Add(new Footnote { Text = "neuve" });
            fresh.Annotations.Add(new Annotation { Id = pending.Id, Text = "à revoir", Resolved = true });
            fresh.Annotations.Add(new Annotation { Text = "nouvelle remarque" });
            var delta = DocumentDiff.Compare(old, fresh);
            t.Check(delta.ChangeCount == 0 && !delta.IsIdentical, "aucun paragraphe changé, mais des différences");
            var notes = new List<string>();
            foreach (var note in delta.Footnotes) notes.Add(note.Kind);
            notes.Sort();
            t.Equal("added modified removed", string.Join(" ", notes.ToArray()), "notes : ajoutée, modifiée, supprimée (par identifiant)");
            var annotations = new List<string>();
            foreach (var annotation in delta.Annotations) annotations.Add(annotation.Kind);
            annotations.Sort();
            t.Equal("added removed resolved", string.Join(" ", annotations.ToArray()), "annotations : ajoutée, résolue, supprimée");
            t.Equal("notes : 1 ajoutée, 1 supprimée, 1 modifiée · annotations : 1 ajoutée, 1 supprimée, 1 résolue", delta.Summary(), "le récapitulatif des notes et annotations");
            var reopened = Doc("Un");
            reopened.Annotations.Add(new Annotation { Id = pending.Id, Text = "à revoir", Resolved = false });
            t.Equal("reopened", DocumentDiff.Compare(fresh, reopened).Annotations[0].Kind, "une annotation rouverte");
        }

        private static void Edges(Harness t)
        {
            var same = DocumentDiff.Compare(Doc("Un", "Deux"), Doc("Un", "Deux"));
            t.Check(same.IsIdentical && same.ChangeCount == 0 && same.Unchanged == 2, "documents identiques : aucune différence");
            t.Equal("Aucune différence", same.Summary(), "…dit");
            var empties = DocumentDiff.Compare(new TextDocument(), new TextDocument());
            t.Check(empties.IsIdentical && empties.Paragraphs.Count == 0, "deux documents vides : identiques");
            t.Check(DocumentDiff.Compare(null, null).IsIdentical, "documents nuls : identiques");
            var fromEmpty = DocumentDiff.Compare(new TextDocument(), Doc("Un", "Deux"));
            t.Equal("++", Kinds(fromEmpty), "depuis le vide : tout ajouté");
            var toEmpty = DocumentDiff.Compare(Doc("Un", "Deux"), new TextDocument());
            t.Equal("--", Kinds(toEmpty), "vers le vide : tout supprimé");

            // Alignement grossier : plus d'éditions de paragraphes que la limite.
            var a = new List<string>();
            var b = new List<string>();
            for (var i = 0; i < DocumentDiff.AlignmentLimit; i++) { a.Add("ancien " + i); b.Add("nouveau " + i); }
            var coarse = DocumentDiff.Compare(Doc(a.ToArray()), Doc(b.ToArray()));
            t.Check(coarse.Coarse && coarse.Removed + coarse.Added + coarse.Modified + coarse.Rewritten == 2 * DocumentDiff.AlignmentLimit - DocumentDiff.AlignmentLimit,
                "au-delà de la limite d'alignement : grossier (tout supprimé + ajouté, appariés dans l'ordre), et dit");
            t.Check(coarse.Summary().Contains("trop différents"), "…dans le récapitulatif");
            var fine = new List<string>();
            for (var i = 0; i < 3000; i++) fine.Add("paragraphe " + i);
            var big = Doc(fine.ToArray());
            var edited = Doc(fine.ToArray());
            edited.Paragraphs[1500].Runs[0].Text = "paragraphe 1500 retouché";
            edited.Paragraphs.RemoveAt(10);
            var large = DocumentDiff.Compare(big, edited);
            t.Check(!large.Coarse && large.Modified == 1 && large.Removed == 1 && large.Unchanged == 2998, "3 000 paragraphes, deux retouches : alignement fin et rapide");
        }

        private static void Rendering(Harness t)
        {
            var old = Doc("Un", "Deux", "Trois", "Quatre", "Cinq", "Six", "Sept", "Huit");
            old.Paragraphs[1].Runs[0].Bold = true;
            var fresh = Doc("Un", "Deux et demi", "Trois", "Quatre", "Cinq", "Six", "Huit", "Neuf");
            fresh.Paragraphs[1].Runs[0].Bold = true;
            var delta = DocumentDiff.Compare(old, fresh);
            t.Equal("=~====-=+", Kinds(delta), "la fixture : un modifié, un supprimé, un ajouté");

            var rendered = DocumentDiff.Render(delta, false, 1);
            t.Equal(9, rendered.Paragraphs.Count, "sans repli : un paragraphe rendu par delta");
            var merged = rendered.Paragraphs[1];
            t.Equal("Deux et demi", PivotEdit.FlatText(merged), "le modifié montre l'ancien et le nouveau texte fondus");
            TextRun inserted = null;
            foreach (var run in merged.Runs) if (run.Underline == true) inserted = run;
            t.Check(inserted != null && inserted.Text == " et demi" && inserted.Color == DocumentDiff.AddedColor && inserted.Bold == true,
                "l'inséré est SOULIGNÉ (signal primaire), vert en renfort, et garde le format du run (gras)");
            var removedRun = rendered.Paragraphs[6].Runs[0];
            t.Check(removedRun.Strike == true && removedRun.Color == DocumentDiff.RemovedColor && removedRun.Text == "Sept", "un paragraphe supprimé est BARRÉ, rouge");
            var addedRun = rendered.Paragraphs[8].Runs[0];
            t.Check(addedRun.Underline == true && addedRun.Text == "Neuf", "un paragraphe ajouté est souligné");
            t.Check(delta.Paragraphs[6].RenderIndex == 6 && delta.Paragraphs[8].RenderIndex == 8, "chaque delta connaît sa place rendue");

            var folded = DocumentDiff.Render(delta, true, 1);
            // = ~ = = = = - = +  → « = » puis « ~ », puis 4 inchangés (Trois..Six) : 1 de contexte, 2 repliés, 1 de contexte, puis -, =, +.
            t.Equal(8, folded.Paragraphs.Count, "avec repli : la suite de quatre inchangés devient contexte + marqueur + contexte");
            t.Check(PivotEdit.FlatText(folded.Paragraphs[3]).Contains("2 paragraphes inchangés"), "le marqueur dit combien");
            t.Check(delta.Paragraphs[2].RenderIndex == 2 && delta.Paragraphs[3].RenderIndex == 3 && delta.Paragraphs[4].RenderIndex == 3 && delta.Paragraphs[5].RenderIndex == 4,
                "les repliés pointent le marqueur, le contexte garde sa place");

            // Modifié avec un texte supprimé : barré ; réécrit : deux paragraphes.
            var shrink = DocumentDiff.Compare(Doc("Le grand marabout"), Doc("Le marabout"));
            var shrinkDoc = DocumentDiff.Render(shrink, false, 1);
            var struck = "";
            foreach (var run in shrinkDoc.Paragraphs[0].Runs) if (run.Strike == true) struck += run.Text;
            t.Equal("grand ", struck, "le texte retiré d'un paragraphe modifié est barré, à sa place");
            var rewritten = DocumentDiff.Render(DocumentDiff.Compare(Doc(new string('a', 1200)), Doc(new string('b', 1200))), false, 1);
            t.Check(rewritten.Paragraphs.Count == 2 && rewritten.Paragraphs[0].Runs[0].Strike == true && rewritten.Paragraphs[1].Runs[0].Underline == true,
                "un réécrit : l'ancien barré puis le nouveau souligné");

            // Déplacé et style seul : la mention en tête ; les ancres d'annotation retirées ; les notes recopiées.
            var movedDoc = DocumentDiff.Render(DocumentDiff.Compare(Doc("A", "B"), Doc("B", "A")), false, 1);
            var mention = false;
            foreach (var paragraph in movedDoc.Paragraphs) if (PivotEdit.FlatText(paragraph).Contains("déplacé")) mention = true;
            t.Check(mention, "un déplacé porte sa mention");
            var styled = Doc("A");
            styled.Paragraphs[0].StyleId = "heading1";
            var styleDoc = DocumentDiff.Render(DocumentDiff.Compare(Doc("A"), styled), false, 1);
            t.Check(PivotEdit.FlatText(styleDoc.Paragraphs[0]).Contains("style « body » → « heading1 »"), "un style seul dit lequel");
            var annotated = Doc("A");
            annotated.Paragraphs[0].Runs[0].AnnotationId = "ann";
            annotated.Annotations.Add(new Annotation { Id = "ann", Text = "remarque" });
            var noteOld = Doc("A");
            noteOld.Footnotes.Add(new Footnote { Id = "n1", Text = "note" });
            noteOld.Paragraphs[0].Runs.Add(new TextRun { FootnoteId = "n1" });
            var annotatedDelta = DocumentDiff.Compare(noteOld, annotated);
            var annotatedDoc = DocumentDiff.Render(annotatedDelta, false, 1);
            var anchors = 0;
            foreach (var paragraph in annotatedDoc.Paragraphs) foreach (var run in paragraph.Runs) if (run.AnnotationId != null) anchors++;
            t.Equal(0, anchors, "les ancres d'annotation ne passent pas dans le document synthétique");
            DocumentDiff.CopyFootnotes(annotatedDoc, noteOld, annotated);
            t.Check(annotatedDoc.FindFootnote("n1") != null, "les notes des deux versions sont recopiées (les marqueurs se résolvent)");
            t.Check(!ReferenceEquals(annotatedDoc, annotated) && !ReferenceEquals(annotatedDoc.Paragraphs[0], annotated.Paragraphs[0]), "le document rendu est NEUF : jamais celui d'un item");
            t.Check(DocumentDiff.Render(null, true, 1).Paragraphs.Count == 0, "delta nul : document vide");
        }
    }
}

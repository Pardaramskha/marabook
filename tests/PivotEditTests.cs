using System;
using System.Collections.Generic;
using System.Reflection;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C3 — l'algèbre d'édition du pivot, prouvée : insertion,
    /// suppression, scission/fusion, format aux bornes exactes, purges — et
    /// le verrou du batch 24 : Clone est comparé champ par champ PAR
    /// RÉFLEXION, avec des valeurs non-défaut injectées dans CHAQUE champ,
    /// pour qu'un futur champ oublié fasse échouer le harnais (le sort
    /// qu'AllowWidows a subi au Ctrl+Z).</summary>
    public static class PivotEditTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C3 — PivotEdit");
            Insertion(t);
            Deletion(t);
            SplitAndMerge(t);
            FormatBounds(t);
            Purges(t);
            CloneExhaustive(t);
        }

        // ------------------------------------------------------------ édition

        private static TextParagraph Paragraph(params TextRun[] runs)
        {
            var paragraph = new TextParagraph();
            paragraph.Runs.AddRange(runs);
            return paragraph;
        }

        private static void Insertion(Harness t)
        {
            // La frappe hérite du format du caractère PRÉCÉDENT (règle des
            // traitements de texte).
            var p = Paragraph(
                new TextRun { Text = "gras", Bold = true },
                new TextRun { Text = "ita", Italic = true });
            PivotEdit.InsertText(p, 4, "X");
            t.Equal("grasX", p.Runs[0].Text, "insertion à la frontière : étend le run précédent");
            t.Equal(true, p.Runs[0].Bold, "le format hérité est celui d'avant le caret");

            PivotEdit.InsertText(p, 2, "-");
            t.Equal("gr-asX", p.Runs[0].Text, "insertion au milieu d'un run");

            var empty = new TextParagraph();
            PivotEdit.InsertText(empty, 0, "seul");
            t.Equal(1, empty.Runs.Count, "insertion dans un paragraphe vide crée un run");
            t.Equal("seul", empty.Runs[0].Text, "texte inséré");

            // Insertion d'un élément au milieu d'un run : scission propre.
            var host = Paragraph(new TextRun { Text = "avantaprès", Bold = true });
            PivotEdit.InsertElement(host, 5, new TextRun { IsRule = true });
            t.Equal(3, host.Runs.Count, "l'élément scinde le run");
            t.Equal("avant", host.Runs[0].Text, "tête préservée");
            t.Check(host.Runs[1].IsRule, "l'élément est en place");
            t.Equal("après", host.Runs[2].Text, "queue préservée");
            t.Equal(true, host.Runs[2].Bold, "la queue garde le format");
        }

        private static void Deletion(Harness t)
        {
            var p = Paragraph(
                new TextRun { Text = "abc", Bold = true },
                new TextRun { FootnoteId = "n1" },
                new TextRun { Text = "def", Bold = true });
            PivotEdit.DeleteInParagraph(p, 2, 5); // "c", le marqueur, "d"
            t.Equal(1, p.Runs.Count, "les runs de même format refusionnent après suppression");
            t.Equal("abef", p.Runs[0].Text, "la plage [2,5) est retirée, marqueur compris");

            var whole = Paragraph(new TextRun { Text = "tout" });
            PivotEdit.DeleteInParagraph(whole, 0, 4);
            t.Equal(0, whole.Runs.Count, "suppression totale : paragraphe vide");
        }

        private static void SplitAndMerge(Harness t)
        {
            var p = new TextParagraph
            {
                StyleId = "special",
                AlignOverride = "center",
                ListKind = "bullet",
                Indent = 20
            };
            p.Runs.Add(new TextRun { Text = "unedeux", Italic = true });
            var tail = PivotEdit.Split(p, 3);
            t.Equal("une", p.Runs[0].Text, "scission : la tête garde son texte");
            t.Equal("deux", tail.Runs[0].Text, "scission : la queue part");
            t.Equal(true, tail.Runs[0].Italic, "la queue garde le format");
            t.Equal("special", tail.StyleId, "la queue hérite du style");
            t.Equal("center", tail.AlignOverride, "et de l'alignement");
            t.Equal("bullet", tail.ListKind, "et de la liste");
            t.Equal((double?)20.0, tail.Indent, "et du décalage");

            PivotEdit.MergeInto(p, tail);
            t.Equal(1, p.Runs.Count, "fusion : les runs identiques se recollent");
            t.Equal("unedeux", p.Runs[0].Text, "le texte est recomposé");
        }

        private static void FormatBounds(Harness t)
        {
            var p = Paragraph(new TextRun { Text = "abcdef" });
            PivotEdit.ApplyFormat(p, 2, 4, delegate(TextRun run) { run.Bold = true; });
            t.Equal(3, p.Runs.Count, "le format découpe aux bornes exactes");
            t.Equal("ab", p.Runs[0].Text, "avant la borne : intact");
            t.Equal("cd", p.Runs[1].Text, "la plage exacte est isolée");
            t.Equal(true, p.Runs[1].Bold, "la plage porte le format");
            t.Check(!p.Runs[0].Bold.HasValue && !p.Runs[2].Bold.HasValue,
                "rien ne déborde des bornes");

            // Re-application identique : les runs refusionnent.
            PivotEdit.ApplyFormat(p, 0, 2, delegate(TextRun run) { run.Bold = true; });
            PivotEdit.ApplyFormat(p, 4, 6, delegate(TextRun run) { run.Bold = true; });
            t.Equal(1, p.Runs.Count, "formats identiques : refusion complète");
            t.Equal("abcdef", p.Runs[0].Text, "texte intact après refusion");

            t.Check(PivotEdit.RangeHas(p, 1, 5, null,
                delegate(TextRun run, ParagraphStyle style) { return run.Bold == true; }),
                "RangeHas voit le format sur toute la plage");
        }

        private static void Purges(Harness t)
        {
            var document = new TextDocument();
            var p = new TextParagraph();
            p.Runs.Add(new TextRun { Text = "appel" });
            p.Runs.Add(new TextRun { FootnoteId = "vivante" });
            document.Paragraphs.Add(p);
            document.Footnotes.Add(new Footnote { Id = "vivante", Text = "gardée" });
            document.Footnotes.Add(new Footnote { Id = "orpheline", Text = "sans marqueur" });
            PivotEdit.PurgeFootnotes(document);
            t.Equal(1, document.Footnotes.Count, "la note sans marqueur est purgée");
            t.Equal("vivante", document.Footnotes[0].Id, "la note appelée survit");

            // Annotations : ordre du texte + purge des orphelines des DEUX sens.
            var doc2 = new TextDocument();
            var q = new TextParagraph();
            q.Runs.Add(new TextRun { Text = "b", AnnotationId = "B" });
            q.Runs.Add(new TextRun { Text = "a", AnnotationId = "A" });
            q.Runs.Add(new TextRun { Text = "x", AnnotationId = "fantôme" });
            doc2.Paragraphs.Add(q);
            doc2.Annotations.Add(new Annotation { Id = "A", Text = "seconde" });
            doc2.Annotations.Add(new Annotation { Id = "B", Text = "première" });
            doc2.Annotations.Add(new Annotation { Id = "orpheline", Text = "sans ancre" });
            var order = doc2.AnnotationOrder(true);
            t.Equal(2, order.Count, "l'ordre ne garde que les annotations ancrées");
            t.Equal("B", order[0], "ordre du texte, pas de la liste");
            t.Equal(2, doc2.Annotations.Count, "l'annotation sans ancre est purgée");
            t.Check(q.Runs[2].AnnotationId == null, "l'ancre sans annotation est effacée");
        }

        // ------------------------------------------------------------ Clone

        /// <summary>Injecte une valeur non-défaut dans CHAQUE champ public de
        /// TextParagraph et TextRun (par réflexion), clone, compare champ à
        /// champ. Un champ ajouté au modèle et oublié dans Clone/CloneFormat
        /// échoue ici — sans que le test ait besoin de le connaître.</summary>
        private static void CloneExhaustive(Harness t)
        {
            var document = new TextDocument();
            var paragraph = new TextParagraph();
            FillEveryField(paragraph);
            var run = new TextRun();
            FillEveryField(run);
            paragraph.Runs.Add(run);
            var element = new TextRun { FootnoteId = "note" };
            paragraph.Runs.Add(element);
            document.Paragraphs.Add(paragraph);
            document.Footnotes.Add(new Footnote { Id = "note", Text = "texte de note" });
            document.Annotations.Add(new Annotation
            {
                Id = "ann", Text = "commentaire", Created = "2026-08-07", Resolved = true
            });

            var copy = PivotEdit.Clone(document);
            var diffs = DeepCompare.Diff(document, copy, null);
            foreach (var diff in diffs) t.Info("Clone : " + diff);
            t.Equal(0, diffs.Count,
                "Clone copie chaque champ (réflexion, valeurs non-défaut partout)");

            // Indépendance : muter la copie ne touche pas l'original.
            copy.Paragraphs[0].Runs[0].Text = "muté";
            copy.Footnotes[0].Text = "muté";
            t.Check(document.Paragraphs[0].Runs[0].Text != "muté"
                && document.Footnotes[0].Text != "muté",
                "le clone est indépendant de l'original");
        }

        /// <summary>Valeur non-défaut par type : un champ futur de type connu
        /// reçoit automatiquement une valeur discriminante.</summary>
        private static void FillEveryField(object target)
        {
            foreach (var field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var type = field.FieldType;
                if (type == typeof(string))
                {
                    // Ids d'éléments (note/image) : les laisser nuls, un run de
                    // TEXTE n'en porte pas (exclusifs par construction).
                    if (field.Name == "FootnoteId" || field.Name == "ImageId") continue;
                    field.SetValue(target, "val-" + field.Name);
                }
                else if (type == typeof(bool))
                {
                    if (field.Name == "IsLineBreak" || field.Name == "IsRule") continue;
                    field.SetValue(target, true);
                }
                else if (type == typeof(bool?)) field.SetValue(target, true);
                else if (type == typeof(double)) field.SetValue(target, 12.75);
                else if (type == typeof(double?)) field.SetValue(target, 8.5);
                else if (type == typeof(int)) field.SetValue(target, 7);
                else if (type == typeof(PageDecor)) field.SetValue(target, new PageDecor());
                else if (type == typeof(List<TextRun>)) { } // rempli par l'appelant
                else if (type.IsClass) { } // autres listes/objets : hors périmètre du run
            }
        }
    }
}

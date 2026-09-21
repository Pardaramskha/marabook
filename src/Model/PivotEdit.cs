using System;
using System.Collections.Generic;
using System.Text;

namespace Marabook.Model
{
    /// <summary>Editing operations on the pivot model, addressed by flat
    /// offsets: each text character counts 1, and each element run (footnote
    /// marker, image, rule, line break) counts 1. This is the model half of
    /// the home-grown editing engine — the composed view drives it.</summary>
    public static class PivotEdit
    {
        public static bool IsElement(TextRun run)
        {
            return run.FootnoteId != null || run.ImageId != null || run.IsRule || run.IsLineBreak;
        }

        public static int FlatLength(TextParagraph paragraph)
        {
            var length = 0;
            foreach (var run in paragraph.Runs)
                length += IsElement(run) ? 1 : run.Text.Length;
            return length;
        }

        /// <summary>Plain text with U+FFFC standing for element runs.</summary>
        public static string FlatText(TextParagraph paragraph)
        {
            var sb = new StringBuilder();
            foreach (var run in paragraph.Runs)
                if (IsElement(run)) sb.Append('￼');
                else sb.Append(run.Text);
            return sb.ToString();
        }

        /// <summary>Locates a flat offset: the run containing it and the offset
        /// inside that run. offset == FlatLength → (Runs.Count, 0).</summary>
        public static void Locate(TextParagraph paragraph, int offset,
            out int runIndex, out int inner)
        {
            var cursor = 0;
            for (var i = 0; i < paragraph.Runs.Count; i++)
            {
                var length = IsElement(paragraph.Runs[i]) ? 1 : paragraph.Runs[i].Text.Length;
                if (offset < cursor + length)
                {
                    runIndex = i;
                    inner = offset - cursor;
                    return;
                }
                cursor += length;
            }
            runIndex = paragraph.Runs.Count;
            inner = 0;
        }

        /// <summary>Copies every FORMAT field of a run (not its content).
        /// RÈGLE : tout nouveau champ de format de TextRun doit être ajouté
        /// ici ET dans TextRun.HasSameFormat — sinon il meurt au premier
        /// split de run (SliceRun, Split, ApplyFormat passent tous par là).</summary>
        /// <summary>Copie intégrale d'un run (format, texte, élément).</summary>
        public static TextRun CloneRun(TextRun run)
        {
            var r = CloneFormat(run);
            r.Text = run.Text;
            r.FootnoteId = run.FootnoteId;
            r.ImageId = run.ImageId;
            r.IsRule = run.IsRule;
            r.IsLineBreak = run.IsLineBreak;
            return r;
        }

        /// <summary>Le paragraphe SANS ses runs (style, alignement, liste,
        /// sauts…) — la coquille qu'une redistribution remplit.</summary>
        public static TextParagraph CloneParagraphShell(TextParagraph paragraph)
        {
            return new TextParagraph
            {
                StyleId = paragraph.StyleId,
                AlignOverride = paragraph.AlignOverride,
                ListKind = paragraph.ListKind,
                Indent = paragraph.Indent,
                FirstIndent = paragraph.FirstIndent,
                PageBreakBefore = paragraph.PageBreakBefore,
                AllowWidows = paragraph.AllowWidows,
                StartOnRecto = paragraph.StartOnRecto,
                Decor = paragraph.Decor
            };
        }

        public static TextRun CloneFormat(TextRun source)
        {
            return new TextRun
            {
                Bold = source.Bold,
                Weight = source.Weight,
                Italic = source.Italic,
                Underline = source.Underline,
                Strike = source.Strike,
                Tracking = source.Tracking,
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                Color = source.Color,
                Highlight = source.Highlight,
                AnnotationId = source.AnnotationId,
                NoProof = source.NoProof
            };
        }

        /// <summary>Inserts text, inheriting the format of the character before
        /// the caret (word-processor rule), or of the following text when at
        /// the very start.</summary>
        public static void InsertText(TextParagraph paragraph, int offset, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int runIndex, inner;
            Locate(paragraph, offset, out runIndex, out inner);

            // Inside a text run: straight splice.
            if (runIndex < paragraph.Runs.Count && !IsElement(paragraph.Runs[runIndex]) && inner > 0)
            {
                var run = paragraph.Runs[runIndex];
                run.Text = run.Text.Substring(0, inner) + text + run.Text.Substring(inner);
                return;
            }
            // At a boundary: extend the previous text run when there is one.
            var previous = runIndex - 1;
            if (inner == 0 && previous >= 0 && !IsElement(paragraph.Runs[previous]))
            {
                paragraph.Runs[previous].Text += text;
                return;
            }
            // Element boundary or empty paragraph: new run copying the nearest
            // text format.
            TextRun format = null;
            for (var i = Math.Min(runIndex, paragraph.Runs.Count - 1); i >= 0 && format == null; i--)
                if (i < paragraph.Runs.Count && !IsElement(paragraph.Runs[i])) format = paragraph.Runs[i];
            for (var i = runIndex; i < paragraph.Runs.Count && format == null; i++)
                if (!IsElement(paragraph.Runs[i])) format = paragraph.Runs[i];
            var fresh = format != null ? CloneFormat(format) : new TextRun();
            fresh.Text = text;
            paragraph.Runs.Insert(Math.Min(runIndex, paragraph.Runs.Count), fresh);
        }

        /// <summary>Inserts an element run (image, rule, break, marker) at the
        /// offset, splitting a text run if needed.</summary>
        public static void InsertElement(TextParagraph paragraph, int offset, TextRun element)
        {
            int runIndex, inner;
            Locate(paragraph, offset, out runIndex, out inner);
            if (runIndex < paragraph.Runs.Count && !IsElement(paragraph.Runs[runIndex]) && inner > 0)
            {
                var run = paragraph.Runs[runIndex];
                var tail = CloneFormat(run);
                tail.Text = run.Text.Substring(inner);
                run.Text = run.Text.Substring(0, inner);
                paragraph.Runs.Insert(runIndex + 1, element);
                if (tail.Text.Length > 0) paragraph.Runs.Insert(runIndex + 2, tail);
                return;
            }
            paragraph.Runs.Insert(Math.Min(runIndex, paragraph.Runs.Count), element);
        }

        /// <summary>Remplace [start, end) par un texte qui GARDE le format du
        /// premier caractère remplacé (batch 37) : effacer puis insérer
        /// perdait le gras d'un mot entièrement recouvert (le run mourait, le
        /// texte neuf prenait le format du voisin).</summary>
        public static void ReplaceText(TextParagraph paragraph, int start, int end, string text)
        {
            int runIndex, inner;
            Locate(paragraph, start, out runIndex, out inner);
            TextRun format = null;
            if (runIndex < paragraph.Runs.Count && !IsElement(paragraph.Runs[runIndex]))
                format = CloneFormat(paragraph.Runs[runIndex]);
            DeleteInParagraph(paragraph, start, end);
            if (string.IsNullOrEmpty(text)) return;
            if (format == null)
            {
                InsertText(paragraph, start, text);
                return;
            }
            format.Text = text;
            InsertElement(paragraph, start, format); // insère (en coupant au besoin) n'importe quel run
            MergeAdjacent(paragraph);
        }

        /// <summary>Deletes [start, end) inside one paragraph.</summary>
        public static void DeleteInParagraph(TextParagraph paragraph, int start, int end)
        {
            if (end <= start) return;
            var remaining = new List<TextRun>();
            var cursor = 0;
            foreach (var run in paragraph.Runs)
            {
                var length = IsElement(run) ? 1 : run.Text.Length;
                var runStart = cursor;
                var runEnd = cursor + length;
                cursor = runEnd;
                if (runEnd <= start || runStart >= end) { remaining.Add(run); continue; }
                if (IsElement(run)) continue; // fully covered by the range
                var keepHead = Math.Max(0, start - runStart);
                var keepTail = Math.Max(0, runEnd - end);
                var text = "";
                if (keepHead > 0) text += run.Text.Substring(0, keepHead);
                if (keepTail > 0) text += run.Text.Substring(run.Text.Length - keepTail);
                if (text.Length > 0)
                {
                    run.Text = text;
                    remaining.Add(run);
                }
            }
            paragraph.Runs.Clear();
            paragraph.Runs.AddRange(remaining);
            MergeAdjacent(paragraph);
        }

        /// <summary>Splits at offset; returns the new following paragraph
        /// (same style and list kind).</summary>
        public static TextParagraph Split(TextParagraph paragraph, int offset)
        {
            var tail = new TextParagraph
            {
                StyleId = paragraph.StyleId,
                AlignOverride = paragraph.AlignOverride,
                ListKind = paragraph.ListKind,
                Indent = paragraph.Indent
            };
            int runIndex, inner;
            Locate(paragraph, offset, out runIndex, out inner);
            if (runIndex < paragraph.Runs.Count && !IsElement(paragraph.Runs[runIndex]) && inner > 0)
            {
                var run = paragraph.Runs[runIndex];
                var moved = CloneFormat(run);
                moved.Text = run.Text.Substring(inner);
                run.Text = run.Text.Substring(0, inner);
                if (moved.Text.Length > 0) tail.Runs.Add(moved);
                runIndex++;
            }
            while (paragraph.Runs.Count > runIndex)
            {
                tail.Runs.Add(paragraph.Runs[runIndex]);
                paragraph.Runs.RemoveAt(runIndex);
            }
            return tail;
        }

        /// <summary>Appends every run of <paramref name="next"/> to
        /// <paramref name="paragraph"/> (backspace at paragraph start).</summary>
        public static void MergeInto(TextParagraph paragraph, TextParagraph next)
        {
            paragraph.Runs.AddRange(next.Runs);
            MergeAdjacent(paragraph);
        }

        private static void MergeAdjacent(TextParagraph paragraph)
        {
            for (var i = paragraph.Runs.Count - 1; i > 0; i--)
            {
                var a = paragraph.Runs[i - 1];
                var b = paragraph.Runs[i];
                if (!IsElement(a) && !IsElement(b) && a.HasSameFormat(b))
                {
                    a.Text += b.Text;
                    paragraph.Runs.RemoveAt(i);
                }
            }
            if (paragraph.Runs.Count == 1 && !IsElement(paragraph.Runs[0])
                && paragraph.Runs[0].Text.Length == 0)
                paragraph.Runs.Clear();
        }

        /// <summary>Splits runs at [start, end) borders and applies a mutation
        /// to every fully covered text run (bold, color…).</summary>
        public static void ApplyFormat(TextParagraph paragraph, int start, int end,
            Action<TextRun> setter)
        {
            if (end <= start) return;
            SplitAt(paragraph, end);
            SplitAt(paragraph, start);
            var cursor = 0;
            foreach (var run in paragraph.Runs)
            {
                var length = IsElement(run) ? 1 : run.Text.Length;
                if (cursor >= start && cursor + length <= end && !IsElement(run))
                    setter(run);
                cursor += length;
            }
            MergeAdjacent(paragraph);
        }

        private static void SplitAt(TextParagraph paragraph, int offset)
        {
            int runIndex, inner;
            Locate(paragraph, offset, out runIndex, out inner);
            if (runIndex >= paragraph.Runs.Count || inner == 0) return;
            var run = paragraph.Runs[runIndex];
            if (IsElement(run)) return;
            var tail = CloneFormat(run);
            tail.Text = run.Text.Substring(inner);
            run.Text = run.Text.Substring(0, inner);
            paragraph.Runs.Insert(runIndex + 1, tail);
        }

        /// <summary>True when every text character in the range satisfies the
        /// predicate (empty ranges: the char before the caret).</summary>
        public static bool RangeHas(TextParagraph paragraph, int start, int end,
            ParagraphStyle style, Func<TextRun, ParagraphStyle, bool> predicate)
        {
            var cursor = 0;
            var any = false;
            foreach (var run in paragraph.Runs)
            {
                var length = IsElement(run) ? 1 : run.Text.Length;
                if (cursor + length > start && cursor < end && !IsElement(run))
                {
                    any = true;
                    if (!predicate(run, style)) return false;
                }
                cursor += length;
            }
            return any;
        }

        /// <summary>Drops footnotes whose marker no longer exists.</summary>
        public static void PurgeFootnotes(TextDocument document)
        {
            var referenced = new HashSet<string>();
            foreach (var paragraph in document.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.FootnoteId != null) referenced.Add(run.FootnoteId);
            for (var i = document.Footnotes.Count - 1; i >= 0; i--)
                if (!referenced.Contains(document.Footnotes[i].Id))
                    document.Footnotes.RemoveAt(i);
        }

        /// <summary>Deep clone for the undo stack (strings shared).
        /// RÈGLE : tout nouveau champ de TextParagraph, TextRun, Footnote ou
        /// Annotation DOIT être ajouté ici (et dans CloneFormat pour un champ
        /// de FORMAT de TextRun) — l'oubli est silencieux et se paie en
        /// réglage perdu au premier Ctrl+Z (AllowWidows l'a payé au batch 24).
        /// Le test C3 du harnais compare Clone par réflexion champ à champ :
        /// un futur oubli fera échouer build-tests.bat.</summary>
        public static TextDocument Clone(TextDocument document)
        {
            var copy = new TextDocument();
            foreach (var paragraph in document.Paragraphs)
            {
                var p = new TextParagraph
                {
                    StyleId = paragraph.StyleId,
                    AlignOverride = paragraph.AlignOverride,
                    ListKind = paragraph.ListKind,
                    Indent = paragraph.Indent,
                    FirstIndent = paragraph.FirstIndent,
                    PageBreakBefore = paragraph.PageBreakBefore,
                    AllowWidows = paragraph.AllowWidows,
                    // Transitoires de compilation (jamais persistés) — copiés
                    // quand même : Clone reste exhaustif, champ par champ.
                    StartOnRecto = paragraph.StartOnRecto,
                    Decor = paragraph.Decor
                };
                foreach (var run in paragraph.Runs)
                {
                    var r = CloneFormat(run);
                    r.Text = run.Text;
                    r.FootnoteId = run.FootnoteId;
                    r.ImageId = run.ImageId;
                    r.IsRule = run.IsRule;
                    r.IsLineBreak = run.IsLineBreak;
                    p.Runs.Add(r);
                }
                copy.Paragraphs.Add(p);
            }
            foreach (var note in document.Footnotes)
                copy.Footnotes.Add(new Footnote { Id = note.Id, Text = note.Text });
            // Les annotations sont clonées pour l'exhaustivité, mais
            // RestoreSnapshot ne restaure QUE Paragraphs et Footnotes : les
            // commentaires de révision vivent hors du flux d'annulation
            // (annuler du texte ne doit pas ravaler un commentaire tapé après).
            foreach (var annotation in document.Annotations)
                copy.Annotations.Add(new Annotation
                {
                    Id = annotation.Id,
                    Text = annotation.Text,
                    Created = annotation.Created,
                    Resolved = annotation.Resolved
                });
            return copy;
        }
    }
}

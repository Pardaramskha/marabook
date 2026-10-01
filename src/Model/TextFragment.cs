using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Le fragment mis en forme du presse-papiers (28/09/2026) :
    /// une sélection extraite en un petit TextDocument — paragraphes (style,
    /// alignement, liste, décalages) et runs (police, taille, gras, couleur,
    /// surlignage…) — et son insertion au caret. Les éléments liés au
    /// document d'origine ne voyagent pas : marqueurs de notes, images,
    /// filets, ancres d'annotation ; les sauts de ligne restent.
    /// Règle de la marque de paragraphe (celle de Word) : un fragment d'un
    /// seul paragraphe se glisse dans le paragraphe du caret sans en changer
    /// le style ; un fragment de plusieurs paragraphes apporte le style de
    /// ses paragraphes, sauf pour le dernier, dont le texte rejoint la fin
    /// du paragraphe de destination et en garde le style.</summary>
    public static class TextFragment
    {
        /// <summary>Le nom du format de presse-papiers du fragment (JSON du
        /// document, encodé en UTF-8).</summary>
        public const string ClipboardFormat = "application/x-marabook-fragment";

        /// <summary>Extrait [pa:oa, pb:ob) — bornes déjà ordonnées, offsets
        /// plats — en un document neuf. Jamais null ; au moins un paragraphe.</summary>
        public static TextDocument Extract(TextDocument document, int pa, int oa, int pb, int ob)
        {
            var fragment = new TextDocument();
            if (document == null || document.Paragraphs.Count == 0) { fragment.Paragraphs.Add(new TextParagraph()); return fragment; }
            pa = Math.Max(0, Math.Min(pa, document.Paragraphs.Count - 1));
            pb = Math.Max(pa, Math.Min(pb, document.Paragraphs.Count - 1));
            for (var p = pa; p <= pb; p++)
            {
                var source = document.Paragraphs[p];
                var copy = PivotEdit.CloneParagraphShell(source);
                copy.StartOnRecto = false;
                copy.Decor = null;
                foreach (var run in source.Runs)
                    copy.Runs.Add(PivotEdit.CloneRun(run));
                var length = PivotEdit.FlatLength(copy);
                var from = p == pa ? Math.Max(0, Math.Min(oa, length)) : 0;
                var to = p == pb ? Math.Max(from, Math.Min(ob, length)) : length;
                PivotEdit.DeleteInParagraph(copy, to, length);
                PivotEdit.DeleteInParagraph(copy, 0, from);
                Detach(copy);
                fragment.Paragraphs.Add(copy);
            }
            fragment.LineSpacing = document.LineSpacing;
            return fragment;
        }

        /// <summary>Retire ce qui appartient au document d'origine : notes,
        /// images, filets, ancres d'annotation.</summary>
        private static void Detach(TextParagraph paragraph)
        {
            for (var i = paragraph.Runs.Count - 1; i >= 0; i--)
            {
                var run = paragraph.Runs[i];
                if (run.FootnoteId != null || run.ImageId != null || run.IsRule) { paragraph.Runs.RemoveAt(i); continue; }
                run.AnnotationId = null;
            }
        }

        /// <summary>Insère le fragment au caret ; le caret est déplacé après le
        /// texte inséré. Rend le nombre de paragraphes AJOUTÉS au document,
        /// tous contigus juste après le paragraphe de départ (le compositeur
        /// les apprend un à un).</summary>
        public static int Insert(TextDocument document, TextDocument fragment, ref int paragraph, ref int offset)
        {
            if (document == null || fragment == null || fragment.Paragraphs.Count == 0) return 0;
            if (document.Paragraphs.Count == 0) document.Paragraphs.Add(new TextParagraph());
            paragraph = Math.Max(0, Math.Min(paragraph, document.Paragraphs.Count - 1));
            var target = document.Paragraphs[paragraph];
            offset = Math.Max(0, Math.Min(offset, PivotEdit.FlatLength(target)));
            var pieces = fragment.Paragraphs;
            var tail = PivotEdit.Split(target, offset); // la fin du paragraphe, son style
            tail.FirstIndent = target.FirstIndent;
            tail.AllowWidows = target.AllowWidows;

            if (pieces.Count == 1)
            {
                Append(target, pieces[0]);
                PivotEdit.MergeInto(target, tail);
                offset += PivotEdit.FlatLength(pieces[0]);
                return 0;
            }

            // Plusieurs paragraphes : le premier apporte sa marque au
            // paragraphe du caret, les suivants sont insérés tels quels, le
            // dernier rejoint la fin du paragraphe de destination.
            ApplyShell(pieces[0], target);
            Append(target, pieces[0]);
            PivotEdit.MergeInto(target, new TextParagraph());
            var added = 0;
            for (var i = 1; i < pieces.Count - 1; i++)
            {
                var middle = PivotEdit.CloneParagraphShell(pieces[i]);
                Append(middle, pieces[i]);
                document.Paragraphs.Insert(paragraph + i, middle);
                added++;
            }
            var last = pieces[pieces.Count - 1];
            var landing = new TextParagraph();
            Append(landing, last);
            var caretOffset = PivotEdit.FlatLength(landing);
            PivotEdit.MergeInto(landing, tail);
            landing.StyleId = tail.StyleId;
            landing.AlignOverride = tail.AlignOverride;
            landing.ListKind = tail.ListKind;
            landing.Indent = tail.Indent;
            landing.FirstIndent = tail.FirstIndent;
            landing.AllowWidows = tail.AllowWidows;
            document.Paragraphs.Insert(paragraph + pieces.Count - 1, landing);
            added++;
            paragraph += pieces.Count - 1;
            offset = caretOffset;
            return added;
        }

        private static void Append(TextParagraph paragraph, TextParagraph piece)
        {
            foreach (var run in piece.Runs)
                paragraph.Runs.Add(PivotEdit.CloneRun(run));
        }

        private static void ApplyShell(TextParagraph from, TextParagraph to)
        {
            to.StyleId = from.StyleId;
            to.AlignOverride = from.AlignOverride;
            to.ListKind = from.ListKind;
            to.Indent = from.Indent;
            to.FirstIndent = from.FirstIndent;
            to.PageBreakBefore = from.PageBreakBefore;
            to.AllowWidows = from.AllowWidows;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Marabook.Model;

namespace Marabook.Print
{
    // LA RECOMPOSITION PARTIELLE D'UN TRÈS GROS PARAGRAPHE (21/11/2026).
    //
    // Avant : chaque frappe recomposait le paragraphe édité EN ENTIER — ses
    // mots remesurés, ses coupes recalculées, toutes ses lignes refaites. Sur
    // un paragraphe ordinaire c'est une fraction de milliseconde ; sur un bloc
    // de 490 000 signes (un texte collé d'un seul tenant, un flux importé sans
    // retours), près de 300 ms par touche.
    //
    // Maintenant, pour ces paragraphes-là seulement :
    //  1. on retrouve CE QUI A CHANGÉ en comparant l'ancien texte au nouveau
    //     (préfixe et suffixe communs) ;
    //  2. seuls les mots touchés — élargis jusqu'aux espaces qui les bordent —
    //     sont remesurés ; les autres atomes sont repris tels quels, leurs
    //     positions décalées ;
    //  3. les lignes d'AVANT la frappe sont gardées ; la composition reprend
    //     une ligne plus haut (un mot raccourci peut remonter) et s'arrête dès
    //     qu'une ligne neuve commence là où commençait une ancienne : toutes
    //     les suivantes sont alors les mêmes, à un décalage près.
    //
    // Les lignes neuves passent par NextLine, le code de la composition
    // entière : il n'y a qu'UNE façon de faire une ligne. Au moindre doute
    // (mise en forme changée, liste, note, image, lien, frontière de run), on
    // rend null et le paragraphe est recomposé en entier, comme avant. Les
    // tests (C87) comparent, frappe après frappe, le résultat partiel à une
    // composition entière : ils doivent être identiques.
    public partial class CompositionEngine
    {
        /// <summary>En deçà, un paragraphe se recompose en entier : c'est instantané.</summary>
        public const int IncrementalThreshold = 20000;

        /// <summary>Éteint (tests, diagnostic), tout paragraphe est recomposé en entier.</summary>
        public bool IncrementalEnabled = true;
        /// <summary>Combien de recompositions partielles ont abouti, combien ont cédé la place à l'entière.</summary>
        public int IncrementalHits, IncrementalFallbacks;
        /// <summary>La première ligne refaite par la dernière recomposition partielle.</summary>
        public int LastIncrementalLine { get; private set; }
        /// <summary>Le nombre de lignes refaites par la dernière recomposition partielle.</summary>
        public int LastIncrementalLines { get; private set; }

        private sealed class IncrementalState
        {
            public string Flat;
            public List<Atom> Atoms;        // la liste d'origine, sans les restes de césure
            public List<int> LineStarts;    // l'atome par lequel chaque ligne commence
            public TextParagraph Shell;
            public List<TextRun> Formats;
            public int[] Lengths;
            public ParagraphStyle Style;
            public double ContentWidth;
            public object HyphenExceptions, HyphenExplicit;
        }

        private readonly ConditionalWeakTable<ComposedParagraphLayout, IncrementalState> _incremental =
            new ConditionalWeakTable<ComposedParagraphLayout, IncrementalState>();

        /// <summary>Un paragraphe que la recomposition partielle sait traiter :
        /// du texte seul — ni liste, ni note, ni image, ni filet, ni saut de
        /// ligne forcé, ni [[lien]] (leurs marques deviennent des atomes à part).</summary>
        private static bool IncrementalEligible(TextParagraph paragraph, string flat)
        {
            if (paragraph.ListKind != null || flat.Length < IncrementalThreshold) return false;
            foreach (var run in paragraph.Runs)
                if (PivotEdit.IsElement(run) || run.FootnoteId != null) return false;
            return flat.IndexOf("[[", StringComparison.Ordinal) < 0;
        }

        private void RememberIncremental(ComposedParagraphLayout layout, TextParagraph paragraph, ParagraphStyle style,
            List<Atom> atoms, List<int> lineStarts, string flat, double contentWidth)
        {
            var state = new IncrementalState
            {
                Flat = flat, Atoms = atoms, LineStarts = lineStarts, Shell = PivotEdit.CloneParagraphShell(paragraph),
                Formats = new List<TextRun>(paragraph.Runs.Count), Lengths = new int[paragraph.Runs.Count],
                Style = style, ContentWidth = contentWidth, HyphenExceptions = _hyphenExceptions, HyphenExplicit = _hyphenExplicit
            };
            for (var i = 0; i < paragraph.Runs.Count; i++)
            {
                state.Formats.Add(PivotEdit.CloneFormat(paragraph.Runs[i]));
                state.Lengths[i] = paragraph.Runs[i].Text.Length;
            }
            _incremental.Remove(layout);
            _incremental.Add(layout, state);
        }

        /// <summary>Le premier indice dont la valeur n'est pas inférieure à la cible.</summary>
        private static int LowerBound(List<int> values, int target)
        {
            int low = 0, high = values.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (values[middle] < target) low = middle + 1; else high = middle;
            }
            return low;
        }

        /// <summary>La première ligne qui ne commence pas avant ce signe.</summary>
        private static int LowerBoundStart(List<ComposedLine> lines, int target)
        {
            int low = 0, high = lines.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (lines[middle].Start < target) low = middle + 1; else high = middle;
            }
            return low;
        }

        /// <summary>Combien de lignes coupées par une césure se suivent juste avant celle-ci.</summary>
        private static int HyphensBefore(List<ComposedLine> lines, int index)
        {
            var count = 0;
            for (var j = index - 1; j >= 0 && lines[j].Hyphenated; j--) count++;
            return count;
        }

        private static int LowerBoundSource(List<Atom> atoms, int target)
        {
            int low = 0, high = atoms.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (atoms[middle].SourceStart < target) low = middle + 1; else high = middle;
            }
            return low;
        }

        /// <summary>Le paragraphe recomposé à partir de l'ancien, ou null :
        /// rien n'est alors modifié, et l'appelant le recompose en entier.</summary>
        private ComposedParagraphLayout ComposeIncremental(ComposedParagraphLayout old, TextParagraph paragraph)
        {
            LastIncrementalLine = 0;
            LastIncrementalLines = 0;
            IncrementalState state;
            if (!IncrementalEnabled || old == null || !_incremental.TryGetValue(old, out state)) return null;
            ComposedParagraphLayout layout;
            try { layout = TryIncremental(old, state, paragraph); }
            catch (Exception) { layout = null; } // l'ancien état a pu être entamé : l'entière le refait de zéro
            if (layout == null) IncrementalFallbacks++; else IncrementalHits++;
            return layout;
        }

        private ComposedParagraphLayout TryIncremental(ComposedParagraphLayout old, IncrementalState state, TextParagraph paragraph)
        {
            var style = _styles.Find(paragraph.StyleId);
            var contentWidth = _setup.ContentWidthPx;
            if (!ReferenceEquals(style, state.Style) || Math.Abs(contentWidth - state.ContentWidth) > 1e-9) return null;
            if (!ReferenceEquals(_hyphenExceptions, state.HyphenExceptions) || !ReferenceEquals(_hyphenExplicit, state.HyphenExplicit)) return null;
            if (!PivotEdit.SameShell(paragraph, state.Shell) || paragraph.Runs.Count != state.Formats.Count) return null;

            // — Les runs : même mise en forme, et UN seul dont le texte a changé de longueur.
            var runs = paragraph.Runs;
            var changedRun = -1;
            var newLength = 0;
            for (var k = 0; k < runs.Count; k++)
            {
                var run = runs[k];
                if (PivotEdit.IsElement(run) || run.FootnoteId != null || !run.HasSameFormat(state.Formats[k])) return null;
                if (run.Text.Length != state.Lengths[k])
                {
                    if (changedRun >= 0) return null;
                    changedRun = k;
                }
                newLength += run.Text.Length;
            }
            if (newLength < IncrementalThreshold) return null;
            var oldFlat = state.Flat;
            var delta = newLength - oldFlat.Length;
            if (changedRun >= 0 && runs[changedRun].Text.Length - state.Lengths[changedRun] != delta) return null;
            var flat = PivotEdit.FlatText(paragraph);
            if (flat.Length != newLength || flat.IndexOf("[[", StringComparison.Ordinal) >= 0) return null;

            // — Ce qui a changé : entre le préfixe et le suffixe communs.
            var limit = Math.Min(oldFlat.Length, flat.Length);
            var prefix = 0;
            while (prefix < limit && oldFlat[prefix] == flat[prefix]) prefix++;
            var suffix = 0;
            while (suffix < limit - prefix && oldFlat[oldFlat.Length - 1 - suffix] == flat[flat.Length - 1 - suffix]) suffix++;
            if (prefix == flat.Length && delta == 0) return null; // même texte : autre chose a changé (une annotation résolue…)
            var changeEnd = flat.Length - suffix; // dans le nouveau texte

            // — Le run qui porte le changement, et la tranche de mots à refaire.
            var runStart = 0;
            var target = -1;
            for (var k = 0; k < runs.Count; k++)
            {
                var end = runStart + runs[k].Text.Length;
                if (changedRun >= 0 ? k == changedRun : (prefix < end || k == runs.Count - 1)) { target = k; break; }
                runStart = end;
            }
            if (target < 0) return null;
            var text = runs[target].Text;
            if (prefix < runStart || changeEnd > runStart + text.Length) return null;
            if (oldFlat.Length - suffix > runStart + state.Lengths[target]) return null;
            var left = prefix - runStart;
            var right = changeEnd - runStart;
            while (left > 0 && text[left - 1] != ' ') left--;
            while (right < text.Length && text[right] != ' ') right++;
            // Au bord d'un run, le mot peut tenir au run voisin (une virgule en romain après un mot en italique) : on n'y touche pas.
            if (left == 0 && target > 0) return null;
            if (right == text.Length && target < runs.Count - 1) return null;

            var oldAtoms = state.Atoms;
            var sliceStart = runStart + left;             // même position dans l'ancien et le nouveau texte
            var oldSliceEnd = runStart + right - delta;   // dans l'ancien texte
            if (oldSliceEnd < sliceStart) return null;
            var i0 = LowerBoundSource(oldAtoms, sliceStart);
            var i1 = LowerBoundSource(oldAtoms, oldSliceEnd);
            if (left > 0 && (i0 == 0 || !oldAtoms[i0 - 1].IsSpace || oldAtoms[i0 - 1].SourceStart != sliceStart - 1)) return null;
            if (left == 0 && i0 != 0) return null;
            if (i1 < oldAtoms.Count && (!oldAtoms[i1].IsSpace || oldAtoms[i1].SourceStart != oldSliceEnd)) return null;
            if (i1 == oldAtoms.Count && oldSliceEnd != oldFlat.Length) return null;

            var slice = new List<Atom>();
            if (right > left) AddTextAtoms(slice, text.Substring(left, right - left), style, runs[target], false, sliceStart);
            if (slice.Count > 0 && slice[0].GluedToPrevious) return null;

            // — Les lignes à garder, et celle d'où repartir : deux plus haut que le premier
            //   mot touché (il peut remonter d'une ligne), jamais juste après une césure.
            var oldLines = old.Lines;
            var starts = state.LineStarts;
            if (starts.Count != oldLines.Count) return null;
            var restart = Math.Max(0, LowerBound(starts, i0) - 2);
            while (restart > 0 && oldLines[restart - 1].Hyphenated) restart--;

            // À partir d'ici, plus de retour possible à l'ancienne mise en lignes : les atomes de
            // la suite changent de position. Une erreur plus bas ferait recomposer en entier.
            var atoms = new List<Atom>(oldAtoms.Count + slice.Count - (i1 - i0));
            for (var i = 0; i < i0; i++) atoms.Add(oldAtoms[i]);
            atoms.AddRange(slice);
            for (var i = i1; i < oldAtoms.Count; i++)
            {
                var atom = oldAtoms[i];
                if (delta != 0 && atom.SourceStart >= 0) atom.SourceStart += delta;
                atoms.Add(atom);
            }
            var atomDelta = slice.Count - (i1 - i0);
            var afterSlice = i0 + slice.Count;

            var layout = new ComposedParagraphLayout
            {
                Style = style, SpaceBefore = style.SpaceBefore, SpaceAfter = style.SpaceAfter,
                PageBreakBefore = paragraph.PageBreakBefore, StartOnRecto = paragraph.StartOnRecto, FlatLength = flat.Length
            };
            var align = paragraph.AlignOverride ?? style.Align;
            var leading = Leading(style);
            double leftIndent, firstX;
            paragraph.EffectiveIndents(style, out leftIndent, out firstX);
            var hang = leftIndent;
            var working = new List<Atom>(atoms);
            var lineStarts = new List<int>(starts.Count + 4);
            for (var i = 0; i < restart; i++) { layout.Lines.Add(oldLines[i]); lineStarts.Add(starts[i]); }

            var index = restart < starts.Count ? starts[restart] : 0;
            var cursor = restart < oldLines.Count ? oldLines[restart].Start : 0;
            var first = restart == 0;
            var consecutiveHyphens = 0;
            var made = 0;
            while (index < working.Count || first)
            {
                // Retombé sur l'ancienne mise en lignes ? Passé les mots refaits, sans césure en cours,
                // une ligne neuve qui commence au même atome et au même signe qu'une ancienne : la suite est la même.
                if (made > 0 && index >= afterSlice)
                {
                    var oldIndex = index - atomDelta;
                    // La ligne ancienne qui commence au même signe ; elle doit aussi commencer au même atome
                    // et dans la même suite de césures (le reste d'un mot coupé se déduit de ces deux-là).
                    var m = LowerBoundStart(oldLines, cursor - delta);
                    if (m > 0 && m < starts.Count && oldLines[m].Start == cursor - delta && starts[m] == oldIndex && HyphensBefore(oldLines, m) == consecutiveHyphens)
                    {
                        for (var j = m; j < oldLines.Count; j++)
                        {
                            var line = oldLines[j];
                            if (delta != 0)
                            {
                                line.Start += delta;
                                line.End += delta;
                                foreach (var piece in line.Pieces) if (piece.SourceStart >= 0) piece.SourceStart += delta;
                            }
                            layout.Lines.Add(line);
                            lineStarts.Add(starts[j] + atomDelta);
                        }
                        break;
                    }
                }
                lineStarts.Add(index);
                layout.Lines.Add(NextLine(working, ref index, ref cursor, ref consecutiveHyphens, first, paragraph, style, layout, contentWidth, firstX, ref hang, align, leading));
                made++;
                first = false;
                if (index >= working.Count) break;
            }

            _atomsOf.Remove(layout);
            _atomsOf.Add(layout, atoms);
            RememberIncremental(layout, paragraph, style, atoms, lineStarts, flat, contentWidth);
            LastIncrementalLine = restart;
            LastIncrementalLines = made;
            return layout;
        }
    }
}

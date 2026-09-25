using System;
using System.Collections.Generic;
using System.Text;

namespace Marabook.Model
{
    /// <summary>Ce qu'est devenu un paragraphe entre deux versions.</summary>
    public enum ParagraphChange
    {
        Unchanged,
        Added,
        Removed,
        Modified,   // même paragraphe, texte retouché (diff de caractères disponible)
        Rewritten,  // même place, mais au-delà du plafond du diff de caractères
        Moved,      // même contenu, autre place
        StyleOnly   // même texte, style de paragraphe ou mise en forme des runs changés
    }

    /// <summary>Un paragraphe du diff : son sort, ses deux versions, et — pour
    /// un paragraphe modifié — le diff de caractères ; RenderIndex est sa
    /// place dans le document synthétique (navigation du panneau).</summary>
    public class ParagraphDelta
    {
        public ParagraphChange Kind = ParagraphChange.Unchanged;
        public int OldIndex = -1, NewIndex = -1;
        public TextParagraph Old, New;
        public List<CharOp> Ops;
        public int RenderIndex = -1;
        public bool IsChange { get { return Kind != ParagraphChange.Unchanged; } }
    }

    /// <summary>Une note de bas de page ou une annotation entre deux versions :
    /// "added", "removed", "modified", et pour les annotations "resolved" /
    /// "reopened".</summary>
    public class SideDelta
    {
        public string Kind = "";
        public string Id;
        public string Old = "", New = "";
    }

    /// <summary>Le résultat de la comparaison (batch 38, lot A) : les
    /// paragraphes alignés dans l'ordre de la version récente (les supprimés
    /// à la place de leur bloc), les notes et annotations, les comptes.</summary>
    public class DocumentDelta
    {
        public readonly List<ParagraphDelta> Paragraphs = new List<ParagraphDelta>();
        public readonly List<SideDelta> Footnotes = new List<SideDelta>();
        public readonly List<SideDelta> Annotations = new List<SideDelta>();
        public int Unchanged, Modified, Added, Removed, Rewritten, Moved, StyleOnly;
        public bool Coarse; // l'alignement fin a été abandonné (documents trop différents)

        public int ChangeCount { get { return Modified + Added + Removed + Rewritten + Moved + StyleOnly; } }
        public bool IsIdentical { get { return ChangeCount == 0 && Footnotes.Count == 0 && Annotations.Count == 0; } }

        /// <summary>« 34 paragraphes modifiés, 6 ajoutés, 2 supprimés ».</summary>
        public string Summary()
        {
            if (IsIdentical) return "Aucune différence";
            var parts = new List<string>();
            if (Modified > 0) parts.Add(Modified + (Modified == 1 ? " paragraphe modifié" : " paragraphes modifiés"));
            if (Added > 0) parts.Add(Added + (Added == 1 ? " ajouté" : " ajoutés"));
            if (Removed > 0) parts.Add(Removed + (Removed == 1 ? " supprimé" : " supprimés"));
            if (Rewritten > 0) parts.Add(Rewritten + (Rewritten == 1 ? " entièrement réécrit" : " entièrement réécrits"));
            if (Moved > 0) parts.Add(Moved + (Moved == 1 ? " déplacé" : " déplacés"));
            if (StyleOnly > 0) parts.Add(StyleOnly + (StyleOnly == 1 ? " changement de style seul" : " changements de style seuls"));
            if (parts.Count > 0 && Modified == 0)
                parts[0] = parts[0].Replace(" ajouté", " paragraphe ajouté").Replace(" supprimé", " paragraphe supprimé")
                    .Replace(" entièrement réécrit", " paragraphe entièrement réécrit").Replace(" déplacé", " paragraphe déplacé");
            var text = string.Join(", ", parts.ToArray());
            var notes = SideSummary(Footnotes, "note");
            if (notes.Length > 0) text += (text.Length > 0 ? " · " : "") + "notes : " + notes;
            var annotations = SideSummary(Annotations, "annotation");
            if (annotations.Length > 0) text += (text.Length > 0 ? " · " : "") + "annotations : " + annotations;
            if (Coarse) text += " (documents trop différents pour un alignement fin)";
            return text;
        }

        private static string SideSummary(List<SideDelta> deltas, string noun)
        {
            int added = 0, removed = 0, modified = 0, resolved = 0, reopened = 0;
            foreach (var delta in deltas)
                switch (delta.Kind)
                {
                    case "added": added++; break;
                    case "removed": removed++; break;
                    case "modified": modified++; break;
                    case "resolved": resolved++; break;
                    case "reopened": reopened++; break;
                }
            var parts = new List<string>();
            if (added > 0) parts.Add(added + (added == 1 ? " ajoutée" : " ajoutées"));
            if (removed > 0) parts.Add(removed + (removed == 1 ? " supprimée" : " supprimées"));
            if (modified > 0) parts.Add(modified + (modified == 1 ? " modifiée" : " modifiées"));
            if (resolved > 0) parts.Add(resolved + (resolved == 1 ? " résolue" : " résolues"));
            if (reopened > 0) parts.Add(reopened + (reopened == 1 ? " rouverte" : " rouvertes"));
            return string.Join(", ", parts.ToArray());
        }
    }

    /// <summary>La comparaison de deux versions d'un document (batch 38, lot
    /// A), en DEUX ÉTAGES : d'abord les PARAGRAPHES sont alignés (Myers sur
    /// leurs empreintes de contenu, FNV-1a du texte plat) et classés
    /// inchangé / ajouté / supprimé / modifié ; un supprimé et un ajouté de
    /// même contenu sont reconnus DÉPLACÉS ; un même texte dont le style ou
    /// la mise en forme des runs a changé est « style seul ». Ensuite, SEULS
    /// les paragraphes modifiés passent au diff de caractères (CharDiff, son
    /// plafond de 800 éditions y est raisonnable) ; au-delà, « entièrement
    /// réécrit ». Notes de bas de page et annotations sont comparées par
    /// identifiant (ajoutée, supprimée, modifiée, résolue, rouverte). Modèle
    /// pur, sans WPF ; Render bâtit le document SYNTHÉTIQUE de la lecture en
    /// ligne (barré = supprimé, souligné = ajouté, la couleur en renfort).
    /// Limites : l'alignement fin est abandonné au-delà de AlignmentLimit
    /// éditions de paragraphes (tout est alors supprimé + ajouté, dit par
    /// Coarse) ; un paragraphe déplacé ET retouché est vu comme supprimé +
    /// ajouté ; les paragraphes vides ne sont jamais « déplacés ».</summary>
    public static class DocumentDiff
    {
        public const int AlignmentLimit = 1000;

        public const string RemovedColor = "#C0392B";
        public const string AddedColor = "#27AE60";
        public const string MovedColor = "#2980B9";
        public const string NoteColor = "#6B7280";

        // ------------------------------------------------------------ comparer

        public static DocumentDelta Compare(TextDocument oldDocument, TextDocument newDocument)
        {
            var delta = new DocumentDelta();
            var oldParagraphs = oldDocument == null ? new List<TextParagraph>() : oldDocument.Paragraphs;
            var newParagraphs = newDocument == null ? new List<TextParagraph>() : newDocument.Paragraphs;
            var oldHashes = new long[oldParagraphs.Count];
            var newHashes = new long[newParagraphs.Count];
            var oldTexts = new string[oldParagraphs.Count];
            var newTexts = new string[newParagraphs.Count];
            for (var i = 0; i < oldParagraphs.Count; i++) { oldTexts[i] = PivotEdit.FlatText(oldParagraphs[i]); oldHashes[i] = Hash(oldTexts[i]); }
            for (var j = 0; j < newParagraphs.Count; j++) { newTexts[j] = PivotEdit.FlatText(newParagraphs[j]); newHashes[j] = Hash(newTexts[j]); }

            var ops = SequenceDiff(oldHashes, newHashes, AlignmentLimit);
            if (ops == null)
            {
                delta.Coarse = true;
                ops = new List<SeqOp>();
                for (var i = 0; i < oldHashes.Length; i++) ops.Add(new SeqOp('-', i, -1));
                for (var j = 0; j < newHashes.Length; j++) ops.Add(new SeqOp('+', -1, j));
            }

            // — Les déplacés : un supprimé et un ajouté de même contenu (non vide).
            var removedByHash = new Dictionary<long, Queue<int>>();
            foreach (var op in ops)
                if (op.Type == '-' && oldTexts[op.OldIndex].Length > 0)
                {
                    Queue<int> queue;
                    if (!removedByHash.TryGetValue(oldHashes[op.OldIndex], out queue)) removedByHash[oldHashes[op.OldIndex]] = queue = new Queue<int>();
                    queue.Enqueue(op.OldIndex);
                }
            var movedTo = new Dictionary<int, int>();   // ancien index → nouvel index
            var movedFrom = new Dictionary<int, int>(); // nouvel index → ancien index
            foreach (var op in ops)
            {
                if (op.Type != '+' || newTexts[op.NewIndex].Length == 0) continue;
                Queue<int> queue;
                if (!removedByHash.TryGetValue(newHashes[op.NewIndex], out queue) || queue.Count == 0) continue;
                var from = queue.Dequeue();
                movedTo[from] = op.NewIndex;
                movedFrom[op.NewIndex] = from;
            }

            // — Les blocs : entre deux égaux, les supprimés et ajoutés restants
            // s'apparient dans l'ordre (modifié ou réécrit), le reste est
            // supprimé / ajouté.
            var removed = new List<int>();
            var added = new List<int>();
            foreach (var op in ops)
            {
                if (op.Type == '=')
                {
                    FlushHunk(delta, removed, added, oldParagraphs, newParagraphs, oldTexts, newTexts, movedFrom);
                    var same = SameStyle(oldParagraphs[op.OldIndex], newParagraphs[op.NewIndex]);
                    Add(delta, same ? ParagraphChange.Unchanged : ParagraphChange.StyleOnly,
                        op.OldIndex, op.NewIndex, oldParagraphs[op.OldIndex], newParagraphs[op.NewIndex], null);
                    continue;
                }
                if (op.Type == '-')
                {
                    if (!movedTo.ContainsKey(op.OldIndex)) removed.Add(op.OldIndex);
                    continue;
                }
                added.Add(op.NewIndex);
            }
            FlushHunk(delta, removed, added, oldParagraphs, newParagraphs, oldTexts, newTexts, movedFrom);

            CompareFootnotes(delta, oldDocument, newDocument);
            CompareAnnotations(delta, oldDocument, newDocument);
            return delta;
        }

        private static void FlushHunk(DocumentDelta delta, List<int> removed, List<int> added,
            List<TextParagraph> oldParagraphs, List<TextParagraph> newParagraphs, string[] oldTexts, string[] newTexts,
            Dictionary<int, int> movedFrom)
        {
            // Les ajoutés qui sont des déplacés sortent de l'appariement.
            var plainAdded = new List<int>();
            var movedAdded = new List<int>();
            foreach (var j in added) (movedFrom.ContainsKey(j) ? movedAdded : plainAdded).Add(j);
            var pairs = Math.Min(removed.Count, plainAdded.Count);
            for (var k = 0; k < pairs; k++)
            {
                var i = removed[k];
                var j = plainAdded[k];
                var ops = CharDiff.Diff(oldTexts[i], newTexts[j]);
                Add(delta, ops == null ? ParagraphChange.Rewritten : ParagraphChange.Modified, i, j, oldParagraphs[i], newParagraphs[j], ops);
            }
            for (var k = pairs; k < removed.Count; k++)
                Add(delta, ParagraphChange.Removed, removed[k], -1, oldParagraphs[removed[k]], null, null);
            for (var k = pairs; k < plainAdded.Count; k++)
                Add(delta, ParagraphChange.Added, -1, plainAdded[k], null, newParagraphs[plainAdded[k]], null);
            foreach (var j in movedAdded)
                Add(delta, ParagraphChange.Moved, movedFrom[j], j, oldParagraphs[movedFrom[j]], newParagraphs[j], null);
            removed.Clear();
            added.Clear();
        }

        private static void Add(DocumentDelta delta, ParagraphChange kind, int oldIndex, int newIndex,
            TextParagraph oldParagraph, TextParagraph newParagraph, List<CharOp> ops)
        {
            delta.Paragraphs.Add(new ParagraphDelta { Kind = kind, OldIndex = oldIndex, NewIndex = newIndex, Old = oldParagraph, New = newParagraph, Ops = ops });
            switch (kind)
            {
                case ParagraphChange.Unchanged: delta.Unchanged++; break;
                case ParagraphChange.Added: delta.Added++; break;
                case ParagraphChange.Removed: delta.Removed++; break;
                case ParagraphChange.Modified: delta.Modified++; break;
                case ParagraphChange.Rewritten: delta.Rewritten++; break;
                case ParagraphChange.Moved: delta.Moved++; break;
                case ParagraphChange.StyleOnly: delta.StyleOnly++; break;
            }
        }

        private static void CompareFootnotes(DocumentDelta delta, TextDocument oldDocument, TextDocument newDocument)
        {
            var olds = new Dictionary<string, Footnote>();
            if (oldDocument != null) foreach (var note in oldDocument.Footnotes) olds[note.Id] = note;
            var seen = new HashSet<string>();
            if (newDocument != null)
                foreach (var note in newDocument.Footnotes)
                {
                    seen.Add(note.Id);
                    Footnote old;
                    if (!olds.TryGetValue(note.Id, out old)) delta.Footnotes.Add(new SideDelta { Kind = "added", Id = note.Id, New = note.Text });
                    else if (old.Text != note.Text) delta.Footnotes.Add(new SideDelta { Kind = "modified", Id = note.Id, Old = old.Text, New = note.Text });
                }
            foreach (var pair in olds)
                if (!seen.Contains(pair.Key)) delta.Footnotes.Add(new SideDelta { Kind = "removed", Id = pair.Key, Old = pair.Value.Text });
        }

        private static void CompareAnnotations(DocumentDelta delta, TextDocument oldDocument, TextDocument newDocument)
        {
            var olds = new Dictionary<string, Annotation>();
            if (oldDocument != null) foreach (var annotation in oldDocument.Annotations) olds[annotation.Id] = annotation;
            var seen = new HashSet<string>();
            if (newDocument != null)
                foreach (var annotation in newDocument.Annotations)
                {
                    seen.Add(annotation.Id);
                    Annotation old;
                    if (!olds.TryGetValue(annotation.Id, out old))
                        delta.Annotations.Add(new SideDelta { Kind = "added", Id = annotation.Id, New = annotation.Text });
                    else if (old.Resolved != annotation.Resolved)
                        delta.Annotations.Add(new SideDelta { Kind = annotation.Resolved ? "resolved" : "reopened", Id = annotation.Id, Old = old.Text, New = annotation.Text });
                    else if (old.Text != annotation.Text)
                        delta.Annotations.Add(new SideDelta { Kind = "modified", Id = annotation.Id, Old = old.Text, New = annotation.Text });
                }
            foreach (var pair in olds)
                if (!seen.Contains(pair.Key)) delta.Annotations.Add(new SideDelta { Kind = "removed", Id = pair.Key, Old = pair.Value.Text });
        }

        /// <summary>Même style de paragraphe (style, alignement, liste, saut de
        /// page, veuves) ET même mise en forme des runs (format et texte,
        /// éléments compris).</summary>
        public static bool SameStyle(TextParagraph a, TextParagraph b)
        {
            if (a.StyleId != b.StyleId || a.AlignOverride != b.AlignOverride || a.ListKind != b.ListKind
                || a.Indent != b.Indent
                || a.PageBreakBefore != b.PageBreakBefore || a.AllowWidows != b.AllowWidows) return false;
            if (a.Runs.Count != b.Runs.Count) return false;
            for (var i = 0; i < a.Runs.Count; i++)
            {
                var x = a.Runs[i];
                var y = b.Runs[i];
                if (!x.HasSameFormat(y)) return false;
                if (x.Text != y.Text || x.ImageId != y.ImageId || x.IsRule != y.IsRule || x.IsLineBreak != y.IsLineBreak
                    || x.FootnoteId != y.FootnoteId) return false;
            }
            return true;
        }

        /// <summary>FNV-1a 64 du texte plat d'un paragraphe.</summary>
        public static long Hash(string text)
        {
            var hash = 14695981039346656037UL;
            for (var i = 0; i < text.Length; i++) hash = unchecked((hash ^ text[i]) * 1099511628211UL);
            return unchecked((long)hash);
        }

        // ------------------------------------------------ Myers sur séquences

        private struct SeqOp
        {
            public char Type; // '=' | '-' | '+'
            public int OldIndex, NewIndex;
            public SeqOp(char type, int oldIndex, int newIndex) { Type = type; OldIndex = oldIndex; NewIndex = newIndex; }
        }

        /// <summary>Myers sur deux séquences d'empreintes ; null au-delà de
        /// limit éditions (l'appelant se rabat sur un alignement grossier).</summary>
        private static List<SeqOp> SequenceDiff(long[] a, long[] b, int limit)
        {
            var n = a.Length;
            var m = b.Length;
            var max = n + m;
            if (max == 0) return new List<SeqOp>();
            var cap = Math.Min(max, limit);
            var offset = max;
            var v = new int[2 * max + 2];
            var trace = new List<int[]>();
            for (var d = 0; d <= cap; d++)
            {
                trace.Add((int[])v.Clone());
                for (var k = -d; k <= d; k += 2)
                {
                    int x;
                    if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])) x = v[offset + k + 1];
                    else x = v[offset + k - 1] + 1;
                    var y = x - k;
                    while (x < n && y < m && a[x] == b[y]) { x++; y++; }
                    v[offset + k] = x;
                    if (x >= n && y >= m) return SequenceBacktrack(n, m, trace, offset);
                }
            }
            return null;
        }

        private static List<SeqOp> SequenceBacktrack(int n, int m, List<int[]> trace, int offset)
        {
            var ops = new List<SeqOp>();
            var x = n;
            var y = m;
            for (var d = trace.Count - 1; d >= 0; d--)
            {
                var v = trace[d];
                var k = x - y;
                int prevK;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])) prevK = k + 1;
                else prevK = k - 1;
                var prevX = v[offset + prevK];
                var prevY = prevX - prevK;
                while (x > prevX && y > prevY) { ops.Add(new SeqOp('=', x - 1, y - 1)); x--; y--; }
                if (d > 0)
                {
                    if (x == prevX) { ops.Add(new SeqOp('+', -1, y - 1)); y--; }
                    else { ops.Add(new SeqOp('-', x - 1, -1)); x--; }
                }
            }
            ops.Reverse();
            return ops;
        }

        // ------------------------------------------------------------- rendu

        /// <summary>Le document SYNTHÉTIQUE de la lecture en ligne : jamais le
        /// document d'un item — un document neuf, à consommer en lecture
        /// seule. Supprimé = barré (rouge), ajouté = souligné (vert), déplacé =
        /// mention bleue, style seul = mention grise ; un paragraphe réécrit =
        /// l'ancien barré puis le nouveau souligné. Les suites de paragraphes
        /// inchangés plus longues que 2 × context + 1 sont REPLIÉES en une
        /// ligne « n paragraphes inchangés » quand fold est vrai. Les notes de
        /// bas de page des deux versions sont recopiées (les marqueurs se
        /// résolvent) ; les ancres d'annotation sont retirées ; notes et
        /// annotations changées sont listées en fin de document. RenderIndex
        /// de chaque ParagraphDelta = sa place dans le document rendu.</summary>
        public static TextDocument Render(DocumentDelta delta, bool fold, int context)
        {
            var document = new TextDocument();
            if (delta == null) return document;
            var paragraphs = delta.Paragraphs;
            var i = 0;
            while (i < paragraphs.Count)
            {
                if (paragraphs[i].Kind == ParagraphChange.Unchanged)
                {
                    var end = i;
                    while (end < paragraphs.Count && paragraphs[end].Kind == ParagraphChange.Unchanged) end++;
                    var count = end - i;
                    if (fold && count > 2 * context + 1)
                    {
                        for (var k = i; k < i + context; k++) Emit(document, paragraphs[k]);
                        var folded = count - 2 * context;
                        var marker = new TextParagraph { StyleId = "body", AlignOverride = "center" };
                        marker.Runs.Add(new TextRun { Text = "— " + folded + (folded == 1 ? " paragraphe inchangé —" : " paragraphes inchangés —"), Italic = true, Color = NoteColor });
                        for (var k = i + context; k < end - context; k++) paragraphs[k].RenderIndex = document.Paragraphs.Count;
                        document.Paragraphs.Add(marker);
                        for (var k = end - context; k < end; k++) Emit(document, paragraphs[k]);
                    }
                    else
                        for (var k = i; k < end; k++) Emit(document, paragraphs[k]);
                    i = end;
                    continue;
                }
                Emit(document, paragraphs[i]);
                i++;
            }
            EmitSide(document, delta.Footnotes, "Notes de bas de page");
            EmitSide(document, delta.Annotations, "Annotations");
            return document;
        }

        /// <summary>Recopie dans le document synthétique les notes des deux
        /// versions, pour que les marqueurs se résolvent (la vue, qui connaît
        /// les documents, l'appelle après Render).</summary>
        public static void CopyFootnotes(TextDocument synthetic, TextDocument oldDocument, TextDocument newDocument)
        {
            var seen = new HashSet<string>();
            foreach (var source in new[] { newDocument, oldDocument })
            {
                if (source == null) continue;
                foreach (var note in source.Footnotes)
                    if (seen.Add(note.Id)) synthetic.Footnotes.Add(note.Clone());
            }
        }

        private static void Emit(TextDocument document, ParagraphDelta delta)
        {
            delta.RenderIndex = document.Paragraphs.Count;
            switch (delta.Kind)
            {
                case ParagraphChange.Unchanged:
                    document.Paragraphs.Add(CloneClean(delta.New, null, null));
                    break;
                case ParagraphChange.Added:
                    document.Paragraphs.Add(CloneClean(delta.New, true, AddedColor));
                    break;
                case ParagraphChange.Removed:
                    document.Paragraphs.Add(CloneClean(delta.Old, false, RemovedColor));
                    break;
                case ParagraphChange.Rewritten:
                    document.Paragraphs.Add(CloneClean(delta.Old, false, RemovedColor));
                    document.Paragraphs.Add(CloneClean(delta.New, true, AddedColor));
                    break;
                case ParagraphChange.Moved:
                {
                    var paragraph = CloneClean(delta.New, null, null);
                    paragraph.Runs.Insert(0, new TextRun { Text = "⟵ déplacé (était ¶ " + (delta.OldIndex + 1) + ")  ", Italic = true, Color = MovedColor });
                    document.Paragraphs.Add(paragraph);
                    break;
                }
                case ParagraphChange.StyleOnly:
                {
                    var paragraph = CloneClean(delta.New, null, null);
                    var what = delta.Old.StyleId != delta.New.StyleId
                        ? "style « " + delta.Old.StyleId + " » → « " + delta.New.StyleId + " »"
                        : "mise en forme changée";
                    paragraph.Runs.Insert(0, new TextRun { Text = "⟨" + what + "⟩  ", Italic = true, Color = NoteColor });
                    document.Paragraphs.Add(paragraph);
                    break;
                }
                case ParagraphChange.Modified:
                    document.Paragraphs.Add(Merge(delta));
                    break;
            }
        }

        /// <summary>Un paragraphe modifié : les caractères conservés et
        /// supprimés gardent le format de l'ANCIEN run, les insérés celui du
        /// NOUVEAU ; supprimés barrés rouges, insérés soulignés verts ; les
        /// éléments (U+FFFC) sont recopiés tels quels.</summary>
        private static TextParagraph Merge(ParagraphDelta delta)
        {
            var paragraph = PivotEdit.CloneParagraphShell(delta.New);
            paragraph.Runs.Clear();
            var oldOffset = 0;
            var newOffset = 0;
            TextRun current = null;
            foreach (var op in delta.Ops)
            {
                TextRun source;
                bool? underline = null;
                bool? strike = null;
                string color = null;
                if (op.Type == '+') { source = RunAt(delta.New, newOffset); newOffset++; underline = true; color = AddedColor; }
                else if (op.Type == '-') { source = RunAt(delta.Old, oldOffset); oldOffset++; strike = true; color = RemovedColor; }
                else { source = RunAt(delta.Old, oldOffset); oldOffset++; newOffset++; }
                if (source != null && PivotEdit.IsElement(source))
                {
                    var element = PivotEdit.CloneRun(source);
                    element.AnnotationId = null;
                    if (color != null) element.Color = color;
                    paragraph.Runs.Add(element);
                    current = null;
                    continue;
                }
                var format = source != null ? PivotEdit.CloneFormat(source) : new TextRun();
                format.AnnotationId = null;
                if (underline != null) format.Underline = underline;
                if (strike != null) format.Strike = strike;
                if (color != null) format.Color = color;
                format.Text = op.Char.ToString();
                if (current != null && current.HasSameFormat(format)) current.Text += op.Char;
                else { paragraph.Runs.Add(format); current = format; }
            }
            return paragraph;
        }

        private static TextRun RunAt(TextParagraph paragraph, int offset)
        {
            if (paragraph == null) return null;
            int runIndex, inner;
            PivotEdit.Locate(paragraph, offset, out runIndex, out inner);
            return runIndex < paragraph.Runs.Count ? paragraph.Runs[runIndex] : null;
        }

        /// <summary>Clone d'un paragraphe sans ancres d'annotation, avec —
        /// selon le sort — souligné ou barré et une couleur sur chaque run.</summary>
        private static TextParagraph CloneClean(TextParagraph source, bool? underlineElseStrike, string color)
        {
            var paragraph = PivotEdit.CloneParagraphShell(source);
            paragraph.Runs.Clear();
            foreach (var run in source.Runs)
            {
                var clone = PivotEdit.CloneRun(run);
                clone.AnnotationId = null;
                if (underlineElseStrike == true) clone.Underline = true;
                if (underlineElseStrike == false) clone.Strike = true;
                if (color != null) clone.Color = color;
                paragraph.Runs.Add(clone);
            }
            return paragraph;
        }

        private static void EmitSide(TextDocument document, List<SideDelta> deltas, string caption)
        {
            if (deltas.Count == 0) return;
            var head = new TextParagraph { StyleId = "body" };
            head.Runs.Add(new TextRun { Text = caption, Bold = true, Color = NoteColor });
            document.Paragraphs.Add(head);
            foreach (var delta in deltas)
            {
                var line = new TextParagraph { StyleId = "body" };
                switch (delta.Kind)
                {
                    case "added":
                        line.Runs.Add(new TextRun { Text = "Ajoutée : ", Color = NoteColor });
                        line.Runs.Add(new TextRun { Text = delta.New, Underline = true, Color = AddedColor });
                        break;
                    case "removed":
                        line.Runs.Add(new TextRun { Text = "Supprimée : ", Color = NoteColor });
                        line.Runs.Add(new TextRun { Text = delta.Old, Strike = true, Color = RemovedColor });
                        break;
                    case "modified":
                        line.Runs.Add(new TextRun { Text = "Modifiée : ", Color = NoteColor });
                        line.Runs.Add(new TextRun { Text = delta.Old, Strike = true, Color = RemovedColor });
                        line.Runs.Add(new TextRun { Text = " " });
                        line.Runs.Add(new TextRun { Text = delta.New, Underline = true, Color = AddedColor });
                        break;
                    case "resolved":
                        line.Runs.Add(new TextRun { Text = "Résolue : ", Color = NoteColor });
                        line.Runs.Add(new TextRun { Text = delta.New, Italic = true });
                        break;
                    case "reopened":
                        line.Runs.Add(new TextRun { Text = "Rouverte : ", Color = NoteColor });
                        line.Runs.Add(new TextRun { Text = delta.New, Italic = true });
                        break;
                }
                document.Paragraphs.Add(line);
            }
        }
    }
}

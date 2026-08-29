using System;
using UniversSale.Model;

namespace UniversSale.History
{
    /// <summary>La restauration d'un instantané comme UNE action d'historique
    /// (batch 38, lot E — le motif de ReplaceInProjectAction) : annulable en
    /// un cran ; la coquille, prévenue par HistoryManager.Applied, recharge
    /// la vue du document ouvert (pile locale vidée). Le document est
    /// remplacé EN PLACE (mêmes listes) ; avant d'écrire, l'action vérifie
    /// que le document est bien celui attendu (empreinte) — un item modifié
    /// entre-temps n'est pas écrasé : Conflict le dit, rien n'est écrit.</summary>
    public class RestoreSnapshotAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly TextDocument _restored;   // le contenu de l'instantané (clone privé)
        private readonly TextDocument _previous;   // l'état d'avant (clone privé)
        private readonly long _fingerprintBefore, _fingerprintAfter;
        public readonly string SnapshotLabel;
        public bool Conflict;

        public RestoreSnapshotAction(BinderItem item, TextDocument snapshotDocument, string snapshotLabel)
        {
            _item = item;
            SnapshotLabel = snapshotLabel ?? "";
            _previous = PivotEdit.Clone(item.Document);
            _restored = PivotEdit.Clone(snapshotDocument);
            _fingerprintBefore = SnapshotStore.DocumentFingerprint(item.Document);
            _fingerprintAfter = SnapshotStore.DocumentFingerprint(_restored);
        }

        public BinderItem Item { get { return _item; } }
        public bool Touches(BinderItem item) { return item == _item; }

        public void Do()
        {
            Conflict = SnapshotStore.DocumentFingerprint(_item.Document) != _fingerprintBefore;
            if (Conflict) return;
            Replace(_item.Document, _restored);
        }

        public void Undo()
        {
            Conflict = SnapshotStore.DocumentFingerprint(_item.Document) != _fingerprintAfter;
            if (Conflict) return;
            Replace(_item.Document, _previous);
        }

        /// <summary>Remplace le contenu d'un document par le clone d'un autre,
        /// en place — les vues rechargent depuis le pivot.</summary>
        public static void Replace(TextDocument target, TextDocument source)
        {
            var copy = PivotEdit.Clone(source);
            target.Paragraphs.Clear();
            target.Paragraphs.AddRange(copy.Paragraphs);
            if (target.Paragraphs.Count == 0) target.Paragraphs.Add(new TextParagraph());
            target.Footnotes.Clear();
            target.Footnotes.AddRange(copy.Footnotes);
            target.Annotations.Clear();
            target.Annotations.AddRange(copy.Annotations);
        }
    }

    /// <summary>La restauration PARTIELLE (lot E) : un seul paragraphe, depuis
    /// la vue de comparaison — remplacer un paragraphe modifié / réécrit /
    /// restylé par sa version d'avant, réinsérer un paragraphe supprimé,
    /// retirer un paragraphe ajouté. Vérifie le texte en place avant
    /// d'écrire (Conflict sinon). Annulable en un cran.</summary>
    public class RestoreParagraphAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly ParagraphChange _kind;
        private readonly int _index;                // dans le document vivant
        private readonly TextParagraph _older;      // clone de la version d'avant (null pour Added)
        private readonly TextParagraph _live;       // clone de ce qui était là (null pour Removed)
        private readonly string _liveText, _olderText;
        public bool Conflict;

        public RestoreParagraphAction(BinderItem item, ParagraphDelta delta)
        {
            _item = item;
            _kind = delta.Kind;
            _older = delta.Old == null ? null : CloneParagraph(delta.Old);
            _live = delta.New == null ? null : CloneParagraph(delta.New);
            _liveText = _live == null ? null : PivotEdit.FlatText(_live);
            _olderText = _older == null ? null : PivotEdit.FlatText(_older);
            // Un supprimé se réinsère à la place du paragraphe vivant qui suit
            // son bloc ; sans repère, à la fin.
            _index = delta.NewIndex >= 0 ? delta.NewIndex : InsertionPoint(item, delta);
        }

        public BinderItem Item { get { return _item; } }
        public bool Touches(BinderItem item) { return item == _item; }

        /// <summary>Vrai si ce sort se restaure paragraphe par paragraphe.</summary>
        public static bool CanRestore(ParagraphDelta delta)
        {
            return delta != null && delta.Kind != ParagraphChange.Unchanged && delta.Kind != ParagraphChange.Moved;
        }

        private static int InsertionPoint(BinderItem item, ParagraphDelta delta)
        {
            // L'index d'origine borné par le document vivant : le mieux qu'on
            // sache sans alignement (le repère exact est celui du diff rendu).
            return Math.Max(0, Math.Min(delta.OldIndex, item.Document.Paragraphs.Count));
        }

        public void Do()
        {
            var paragraphs = _item.Document.Paragraphs;
            switch (_kind)
            {
                case ParagraphChange.Added:
                    if (!Matches(paragraphs, _index, _liveText)) { Conflict = true; return; }
                    paragraphs.RemoveAt(_index);
                    if (paragraphs.Count == 0) paragraphs.Add(new TextParagraph());
                    return;
                case ParagraphChange.Removed:
                    if (_index > paragraphs.Count) { Conflict = true; return; }
                    paragraphs.Insert(_index, CloneParagraph(_older));
                    return;
                default:
                    if (!Matches(paragraphs, _index, _liveText)) { Conflict = true; return; }
                    paragraphs[_index] = CloneParagraph(_older);
                    return;
            }
        }

        public void Undo()
        {
            var paragraphs = _item.Document.Paragraphs;
            switch (_kind)
            {
                case ParagraphChange.Added:
                    if (_index > paragraphs.Count) { Conflict = true; return; }
                    paragraphs.Insert(_index, CloneParagraph(_live));
                    return;
                case ParagraphChange.Removed:
                    if (!Matches(paragraphs, _index, _olderText)) { Conflict = true; return; }
                    paragraphs.RemoveAt(_index);
                    if (paragraphs.Count == 0) paragraphs.Add(new TextParagraph());
                    return;
                default:
                    if (!Matches(paragraphs, _index, _olderText)) { Conflict = true; return; }
                    paragraphs[_index] = CloneParagraph(_live);
                    return;
            }
        }

        private static bool Matches(System.Collections.Generic.List<TextParagraph> paragraphs, int index, string text)
        {
            return index >= 0 && index < paragraphs.Count && PivotEdit.FlatText(paragraphs[index]) == text;
        }

        private static TextParagraph CloneParagraph(TextParagraph source)
        {
            var clone = PivotEdit.CloneParagraphShell(source);
            clone.Runs.Clear();
            foreach (var run in source.Runs) clone.Runs.Add(PivotEdit.CloneRun(run));
            return clone;
        }
    }
}

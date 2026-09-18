using Marabook.Model;

namespace Marabook.History
{
    /// <summary>Remplace le contenu d'un document par une version préparée
    /// hors du pivot (b49 : les commentaires d'un docx relu posés en
    /// annotations), en un cran d'annulation — les vues rechargent depuis
    /// le pivot (OnHistoryApplied → ReloadCurrentView).</summary>
    public class ReplaceDocumentAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly TextDocument _previous;
        private readonly TextDocument _replacement;
        public readonly string Label;

        public ReplaceDocumentAction(BinderItem item, TextDocument replacement, string label)
        {
            _item = item;
            _previous = PivotEdit.Clone(item.Document);
            _replacement = PivotEdit.Clone(replacement);
            Label = label ?? "";
        }

        public BinderItem Item { get { return _item; } }

        public void Do() { RestoreSnapshotAction.Replace(_item.Document, _replacement); }
        public void Undo() { RestoreSnapshotAction.Replace(_item.Document, _previous); }
    }
}

using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.History
{
    /// <summary>Plusieurs actions en UNE étape d'annulation (1.0.3, la
    /// sélection multiple) : Do dans l'ordre, Undo à rebours. Les actions
    /// peuvent avoir été exécutées une à une avant d'être poussées
    /// (HistoryManager.Push) — c'est le cas des déplacements, dont les
    /// index se calculent au fil de l'eau.</summary>
    public class CompositeAction : IUndoableAction
    {
        private readonly List<IUndoableAction> _actions;

        public CompositeAction(IEnumerable<IUndoableAction> actions)
        {
            _actions = new List<IUndoableAction>(actions);
        }

        public int Count { get { return _actions.Count; } }

        public void Do()
        {
            foreach (var action in _actions) action.Do();
        }

        public void Undo()
        {
            for (var i = _actions.Count - 1; i >= 0; i--) _actions[i].Undo();
        }
    }

    /// <summary>Une action annulable écrite sur place : appliquer / revenir.
    /// Pour les champs qui n'ont pas d'action dédiée (catégorie d'une fiche,
    /// page extra…) quand on les change en lot.</summary>
    public class DelegateAction : IUndoableAction
    {
        private readonly Action _apply, _revert;

        public DelegateAction(Action apply, Action revert)
        {
            _apply = apply;
            _revert = revert;
        }

        public void Do() { _apply(); }
        public void Undo() { _revert(); }
    }

    /// <summary>Plusieurs éléments à la corbeille d'un coup. La règle de la
    /// corbeille (une suppression neuve la vide d'abord) ne joue qu'UNE fois :
    /// enchaîner des DeleteToTrashAction n'y laisserait que le dernier.
    /// L'annulation remet chacun à sa place, dans l'ordre des index.</summary>
    public class TrashManyAction : IUndoableAction
    {
        private sealed class Slot
        {
            public BinderItem Item, Parent;
            public int Index;
        }

        private readonly BinderItem _trash;
        private readonly List<Slot> _slots = new List<Slot>();
        private List<BinderItem> _purged;

        public TrashManyAction(BinderItem trash, IEnumerable<BinderItem> items)
        {
            _trash = trash;
            foreach (var item in items)
            {
                if (item == null || item.Parent == null || item.IsCategory) continue;
                // Un descendant d'un autre élément du lot part avec son parent.
                var covered = false;
                foreach (var other in items)
                    if (!ReferenceEquals(other, item) && item.IsDescendantOf(other)) { covered = true; break; }
                if (covered) continue;
                _slots.Add(new Slot { Item = item, Parent = item.Parent, Index = item.Parent.Children.IndexOf(item) });
            }
        }

        public int Count { get { return _slots.Count; } }

        public void Do()
        {
            _purged = new List<BinderItem>(_trash.Children);
            _trash.Children.Clear();
            foreach (var slot in _slots)
            {
                slot.Parent.Children.Remove(slot.Item);
                slot.Item.Parent = _trash;
                _trash.Children.Add(slot.Item);
            }
        }

        public void Undo()
        {
            foreach (var slot in _slots) _trash.Children.Remove(slot.Item);
            // Du plus petit index au plus grand : chaque réinsertion retrouve
            // la place qu'elle avait, les suivantes se décalent d'elles-mêmes.
            var ordered = new List<Slot>(_slots);
            ordered.Sort(delegate(Slot a, Slot b) { return a.Index.CompareTo(b.Index); });
            foreach (var slot in ordered)
            {
                slot.Item.Parent = slot.Parent;
                slot.Parent.Children.Insert(Math.Min(slot.Index, slot.Parent.Children.Count), slot.Item);
            }
            _trash.Children.Clear();
            foreach (var purged in _purged)
            {
                purged.Parent = _trash;
                _trash.Children.Add(purged);
            }
        }
    }
}

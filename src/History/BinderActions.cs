using System;
using System.Collections.Generic;
using UniversSale.Model;

namespace UniversSale.History
{
    /// <summary>Adds an item under a parent (new text, new folder).</summary>
    public class AddItemAction : IUndoableAction
    {
        private readonly BinderItem _parent;
        private readonly BinderItem _item;
        private readonly int _index;

        public AddItemAction(BinderItem parent, BinderItem item, int index)
        {
            _parent = parent;
            _item = item;
            _index = index < 0 ? parent.Children.Count : index;
        }

        public BinderItem Item { get { return _item; } }

        public void Do()
        {
            _item.Parent = _parent;
            _parent.Children.Insert(Math.Min(_index, _parent.Children.Count), _item);
        }

        public void Undo()
        {
            _parent.Children.Remove(_item);
        }
    }

    /// <summary>Adds several items at once (multi-file media import) as a single
    /// undoable step.</summary>
    public class AddItemsAction : IUndoableAction
    {
        private readonly BinderItem _parent;
        private readonly List<BinderItem> _items;

        public AddItemsAction(BinderItem parent, List<BinderItem> items)
        {
            _parent = parent;
            _items = items;
        }

        public void Do()
        {
            foreach (var item in _items)
            {
                item.Parent = _parent;
                _parent.Children.Add(item);
            }
        }

        public void Undo()
        {
            foreach (var item in _items)
                _parent.Children.Remove(item);
        }
    }

    public class RenameItemAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly string _oldTitle;
        private readonly string _newTitle;

        public RenameItemAction(BinderItem item, string newTitle)
        {
            _item = item;
            _oldTitle = item.Title;
            _newTitle = newTitle;
        }

        public void Do() { _item.Title = _newTitle; }
        public void Undo() { _item.Title = _oldTitle; }
    }

    /// <summary>Moves an item to another parent (drag and drop, restore from trash).</summary>
    public class MoveItemAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly BinderItem _oldParent;
        private readonly int _oldIndex;
        private readonly BinderItem _newParent;
        private readonly int _newIndex;

        public MoveItemAction(BinderItem item, BinderItem newParent, int newIndex)
        {
            _item = item;
            _oldParent = item.Parent;
            _oldIndex = _oldParent.Children.IndexOf(item);
            _newParent = newParent;
            _newIndex = newIndex < 0 ? newParent.Children.Count : newIndex;
        }

        public void Do()
        {
            _oldParent.Children.Remove(_item);
            _item.Parent = _newParent;
            _newParent.Children.Insert(Math.Min(_newIndex, _newParent.Children.Count), _item);
        }

        public void Undo()
        {
            _newParent.Children.Remove(_item);
            _item.Parent = _oldParent;
            _oldParent.Children.Insert(Math.Min(_oldIndex, _oldParent.Children.Count), _item);
        }
    }

    /// <summary>Deletes an item to the trash. Per the trash rule, a new deletion
    /// first empties the trash; the purged items are kept inside this action so
    /// undo can bring everything back.</summary>
    public class DeleteToTrashAction : IUndoableAction
    {
        private readonly BinderItem _trash;
        private readonly BinderItem _item;
        private readonly BinderItem _oldParent;
        private readonly int _oldIndex;
        private List<BinderItem> _purged;

        public DeleteToTrashAction(BinderItem trash, BinderItem item)
        {
            _trash = trash;
            _item = item;
            _oldParent = item.Parent;
            _oldIndex = _oldParent.Children.IndexOf(item);
        }

        public void Do()
        {
            _purged = new List<BinderItem>(_trash.Children);
            _trash.Children.Clear();
            _oldParent.Children.Remove(_item);
            _item.Parent = _trash;
            _trash.Children.Add(_item);
        }

        public void Undo()
        {
            _trash.Children.Remove(_item);
            _item.Parent = _oldParent;
            _oldParent.Children.Insert(Math.Min(_oldIndex, _oldParent.Children.Count), _item);
            _trash.Children.Clear();
            foreach (var purged in _purged)
            {
                purged.Parent = _trash;
                _trash.Children.Add(purged);
            }
        }
    }

    public class EmptyTrashAction : IUndoableAction
    {
        private readonly BinderItem _trash;
        private List<BinderItem> _purged;

        public EmptyTrashAction(BinderItem trash)
        {
            _trash = trash;
        }

        public void Do()
        {
            _purged = new List<BinderItem>(_trash.Children);
            _trash.Children.Clear();
        }

        public void Undo()
        {
            _trash.Children.Clear();
            foreach (var purged in _purged)
            {
                purged.Parent = _trash;
                _trash.Children.Add(purged);
            }
        }
    }
}

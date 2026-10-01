using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.History
{
    /// <summary>Épingler / ne plus épingler sur l'Accueil (batch 41) : une
    /// bascule, annulable comme le reste des mutations de la Pile.</summary>
    public class PinItemAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly bool _pinned;

        public PinItemAction(BinderItem item)
        {
            _item = item;
            _pinned = !item.Pinned;
        }

        public BinderItem Item { get { return _item; } }

        public void Do() { _item.Pinned = _pinned; }
        public void Undo() { _item.Pinned = !_pinned; }
    }

    /// <summary>La couleur de carte d'un item (29/09) : depuis le sous-menu
    /// « Couleur » des tuiles et de la Pile — annulable, null = aucune.</summary>
    public class ChangeColorAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly string _oldColor, _newColor;

        public ChangeColorAction(BinderItem item, string color)
        {
            _item = item;
            _oldColor = item.CardColor;
            _newColor = color;
        }

        public BinderItem Item { get { return _item; } }

        public void Do() { _item.CardColor = _newColor; }
        public void Undo() { _item.CardColor = _oldColor; }
    }

    /// <summary>Classer les enfants d'un dossier par titre (29/09) — l'ordre
    /// d'avant est gardé pour l'annulation.</summary>
    public class SortChildrenAction : IUndoableAction
    {
        private readonly BinderItem _folder;
        private readonly List<BinderItem> _before, _after;

        public SortChildrenAction(BinderItem folder)
        {
            _folder = folder;
            _before = new List<BinderItem>(folder.Children);
            _after = new List<BinderItem>(folder.Children);
            _after.Sort(delegate(BinderItem a, BinderItem b)
            {
                return string.Compare(a.Title ?? "", b.Title ?? "", StringComparison.CurrentCultureIgnoreCase);
            });
        }

        public bool IsNoOp
        {
            get
            {
                for (var i = 0; i < _before.Count; i++) if (_before[i] != _after[i]) return false;
                return true;
            }
        }

        public void Do() { Apply(_after); }
        public void Undo() { Apply(_before); }

        private void Apply(List<BinderItem> order)
        {
            _folder.Children.Clear();
            _folder.Children.AddRange(order);
        }
    }

    /// <summary>Une action dont l'ANNULATION détruit du contenu que rien ne
    /// rend sûrement (29/09) : défaire la création d'une fiche remplie la
    /// supprime avec tout ce qu'on y a écrit. La coquille demande avant.
    /// Null = rien à craindre.</summary>
    public interface IDestructiveUndo
    {
        string UndoWarning { get; }
    }

    /// <summary>Adds an item under a parent (new text, new folder).</summary>
    /// <summary>Ce que l'annulation d'une création détruirait (29/09) : les
    /// items qui portent du contenu — un texte écrit, une fiche remplie
    /// (champs, notes, image), un dossier ou un livre qui contient quelque
    /// chose. Null si tout est vide (rien à regretter).</summary>
    public static class UndoWarnings
    {
        public static bool HasContent(BinderItem item)
        {
            if (item == null) return false;
            if (item.Children.Count > 0) return true;
            if (item.Document != null && item.Document.ToPlainText().Trim().Length > 0) return true;
            if (!string.IsNullOrEmpty(item.Notes) && item.Notes.Trim().Length > 0) return true;
            if (!string.IsNullOrEmpty(item.Synopsis) && item.Synopsis.Trim().Length > 0) return true;
            if (item.ImageId != null) return true;
            foreach (var value in item.FieldValues.Values)
                if (!string.IsNullOrEmpty(value) && value.Trim().Length > 0) return true;
            foreach (var module in item.ModuleValues.Values)
                foreach (var value in module.Values)
                    if (!string.IsNullOrEmpty(value) && value.Trim().Length > 0) return true;
            return false;
        }

        public static string ForCreation(IList<BinderItem> items)
        {
            var filled = new List<string>();
            foreach (var item in items)
                if (HasContent(item)) filled.Add("« " + item.Title + " »");
            if (filled.Count == 0) return null;
            return (filled.Count == 1
                ? "Annuler cette création supprime " + filled[0] + " avec son contenu."
                : "Annuler cette création supprime " + string.Join(", ", filled.ToArray()) + " avec leur contenu.")
                + "\n\nRétablir (Ctrl+Y) le rendra, tant que vous ne faites rien d'autre entre-temps.";
        }
    }

    public class AddItemAction : IUndoableAction, IDestructiveUndo
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
        public string UndoWarning { get { return UndoWarnings.ForCreation(new[] { _item }); } }

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
    public class AddItemsAction : IUndoableAction, IDestructiveUndo
    {
        private readonly BinderItem _parent;
        private readonly List<BinderItem> _items;

        public AddItemsAction(BinderItem parent, List<BinderItem> items)
        {
            _parent = parent;
            _items = items;
        }

        public string UndoWarning { get { return UndoWarnings.ForCreation(_items); } }

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

    /// <summary>Changes an item's Binder icon (null restores the default).</summary>
    public class ChangeIconAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly string _oldIcon;
        private readonly string _newIcon;

        public ChangeIconAction(BinderItem item, string newIcon)
        {
            _item = item;
            _oldIcon = item.Icon;
            _newIcon = newIcon;
        }

        public void Do() { _item.Icon = _newIcon; }
        public void Undo() { _item.Icon = _oldIcon; }
    }

    /// <summary>L'image de la tuile d'un écrit ou d'un livre (pack du
    /// 12/09/2026) — au tableau, elle remplace l'extrait du texte. Les octets
    /// d'une image abandonnée restent dans le magasin jusqu'à la purge à
    /// l'enregistrement, ce qui rend l'annulation sûre.</summary>
    public class ChangeImageAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly string _oldImage;
        private readonly string _newImage;

        public ChangeImageAction(BinderItem item, string newImage)
        {
            _item = item;
            _oldImage = item.ImageId;
            _newImage = newImage;
        }

        public void Do() { _item.ImageId = _newImage; }
        public void Undo() { _item.ImageId = _oldImage; }
    }

    /// <summary>« Options du livre » (batch 32) : nom, icône et objectif de
    /// chapitres appliqués d'un bloc, annulables d'un bloc.</summary>
    public class BookOptionsAction : IUndoableAction
    {
        private readonly BinderItem _book;
        private readonly string _oldTitle, _newTitle;
        private readonly string _oldIcon, _newIcon;
        private readonly int _oldGoal, _newGoal;
        // b48 : l'échéance et l'objectif de taille voyagent avec les options.
        private readonly string _oldDeadline, _newDeadline, _oldUnit, _newUnit;
        private readonly int _oldSize, _newSize;

        public BookOptionsAction(BinderItem book, string title, string icon, int chapterGoal)
            : this(book, title, icon, chapterGoal, book.Book == null ? "" : book.Book.Deadline,
                  book.Book == null ? 0 : book.Book.SizeGoal, book.Book == null ? "words" : book.Book.SizeUnit)
        {
        }

        public BookOptionsAction(BinderItem book, string title, string icon, int chapterGoal,
            string deadline, int sizeGoal, string sizeUnit)
        {
            _book = book;
            if (book.Book == null) book.Book = new BookInfo();
            _oldTitle = book.Title; _newTitle = title;
            _oldIcon = book.Icon; _newIcon = icon;
            _oldGoal = book.Book.ChapterGoal; _newGoal = Math.Max(0, chapterGoal);
            _oldDeadline = book.Book.Deadline; _newDeadline = deadline ?? "";
            _oldSize = book.Book.SizeGoal; _newSize = Math.Max(0, sizeGoal);
            _oldUnit = book.Book.SizeUnit; _newUnit = sizeUnit == "chars" ? "chars" : "words";
        }

        public bool IsNoOp
        {
            get
            {
                return _oldTitle == _newTitle && _oldIcon == _newIcon && _oldGoal == _newGoal
                    && _oldDeadline == _newDeadline && _oldSize == _newSize && _oldUnit == _newUnit;
            }
        }

        public void Do()
        {
            _book.Title = _newTitle;
            _book.Icon = _newIcon;
            _book.Book.ChapterGoal = _newGoal;
            _book.Book.Deadline = _newDeadline;
            _book.Book.SizeGoal = _newSize;
            _book.Book.SizeUnit = _newUnit;
        }

        public void Undo()
        {
            _book.Title = _oldTitle;
            _book.Icon = _oldIcon;
            _book.Book.ChapterGoal = _oldGoal;
            _book.Book.Deadline = _oldDeadline;
            _book.Book.SizeGoal = _oldSize;
            _book.Book.SizeUnit = _oldUnit;
        }
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

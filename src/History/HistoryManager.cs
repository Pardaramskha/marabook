using System;
using System.Collections.Generic;

namespace UniversSale.History
{
    /// <summary>A reversible action on the project. Ported from Mental-o
    /// (ICommande/GestionnaireHistorique) — battle-tested, zero coupling.</summary>
    public interface IUndoableAction
    {
        void Do();
        void Undo();
    }

    /// <summary>Undo/redo stacks. PLAFONNÉES (batch 37 — dette du batch 24) :
    /// au-delà de Capacity actions, la plus ancienne est oubliée. Applied
    /// prévient la coquille de chaque action posée, défaite ou refaite (le
    /// remplacement projet recharge la vue du document ouvert).</summary>
    public class HistoryManager
    {
        public const int Capacity = 100;

        private readonly LinkedList<IUndoableAction> _undoStack = new LinkedList<IUndoableAction>();
        private readonly Stack<IUndoableAction> _redoStack = new Stack<IUndoableAction>();

        public event Action Changed;
        public event Action<IUndoableAction, bool> Applied; // (action, undone)

        public bool CanUndo { get { return _undoStack.Count > 0; } }
        public bool CanRedo { get { return _redoStack.Count > 0; } }
        public int Count { get { return _undoStack.Count; } }

        public void Run(IUndoableAction action)
        {
            action.Do();
            NotifyApplied(action, false);
            Push(action);
        }

        /// <summary>For actions already applied on screen (e.g. the end of a drag).</summary>
        public void Push(IUndoableAction action)
        {
            _undoStack.AddLast(action);
            while (_undoStack.Count > Capacity) _undoStack.RemoveFirst();
            _redoStack.Clear();
            Notify();
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            var action = _undoStack.Last.Value;
            _undoStack.RemoveLast();
            action.Undo();
            _redoStack.Push(action);
            NotifyApplied(action, true);
            Notify();
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            var action = _redoStack.Pop();
            action.Do();
            _undoStack.AddLast(action);
            NotifyApplied(action, false);
            Notify();
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            Notify();
        }

        private void Notify()
        {
            var handler = Changed;
            if (handler != null) handler();
        }

        private void NotifyApplied(IUndoableAction action, bool undone)
        {
            var handler = Applied;
            if (handler != null) handler(action, undone);
        }
    }
}

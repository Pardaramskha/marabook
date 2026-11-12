using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.History
{
    /// <summary>Une modification d'une frise (1.2.0, lot 2) — une brique posée,
    /// modifiée, retirée, dupliquée, les types remaniés : l'état d'avant et
    /// celui d'après, entiers. Ctrl+Z rend la frise telle qu'elle était.</summary>
    public class TimelineEditAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly TimelineInfo _before, _after;

        public TimelineEditAction(BinderItem item, TimelineInfo before, TimelineInfo after)
        {
            _item = item;
            _before = before;
            _after = after;
        }

        public BinderItem Item { get { return _item; } }
        public void Do() { _item.Timeline = _after.Clone(); }
        public void Undo() { _item.Timeline = _before.Clone(); }
    }

    /// <summary>Un système temporel créé ou modifié (« Temporalité par
    /// segments ») avec tout ce qui a suivi : les briques replacées ou mises
    /// en attente, l'époque, les liens de date réécrits.</summary>
    public class TimeSystemEditAction : IUndoableAction
    {
        private readonly Project _project;
        private readonly TimeSystemChange _change;
        private readonly bool _creating;

        public TimeSystemEditAction(Project project, TimeSystemChange change, bool creating)
        {
            _project = project;
            _change = change;
            _creating = creating;
        }

        public TimeSystemChange Change { get { return _change; } }
        public bool Creating { get { return _creating; } }

        public void Do()
        {
            if (_creating) _project.TimeSystems.Add(_change.After);
            else
            {
                var index = _project.TimeSystems.IndexOf(_change.Before);
                if (index >= 0) _project.TimeSystems[index] = _change.After;
            }
            foreach (var pair in _change.InfosAfter) pair.Key.Timeline = pair.Value.Clone();
            if (_change.EpochAfter != null && _project.Epoch != null) _project.Epoch.Notation = _change.EpochAfter;
            DissonanceChanges.Apply(_change.DissonancesAfter);
            if (_change.Links.Edits.Count > 0) _change.Links.Apply(_project, true);
        }

        public void Undo()
        {
            if (_change.Links.Edits.Count > 0) _change.Links.Apply(_project, false);
            if (_change.EpochBefore != null && _project.Epoch != null) _project.Epoch.Notation = _change.EpochBefore;
            DissonanceChanges.Apply(_change.DissonancesBefore);
            foreach (var pair in _change.InfosBefore) pair.Key.Timeline = pair.Value.Clone();
            if (_creating) _project.TimeSystems.Remove(_change.After);
            else
            {
                var index = _project.TimeSystems.IndexOf(_change.After);
                if (index >= 0) _project.TimeSystems[index] = _change.Before;
            }
        }
    }
}

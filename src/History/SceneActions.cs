using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.History
{
    /// <summary>Les fiches de scène d'un écrit, avant et après (21/11/2026) :
    /// Ctrl+Z rend ce qu'on savait de ses scènes.</summary>
    public class SceneEditAction : IUndoableAction
    {
        private readonly BinderItem _item;
        private readonly List<SceneMeta> _before, _after;

        /// <summary>À bâtir APRÈS la modification, avec la copie prise avant.</summary>
        public SceneEditAction(BinderItem item, List<SceneMeta> before)
        {
            _item = item;
            _before = before;
            _after = Scenes.CloneAll(item.Scenes);
        }

        public BinderItem Item { get { return _item; } }
        public void Do() { _item.Scenes = Scenes.CloneAll(_after); }
        public void Undo() { _item.Scenes = Scenes.CloneAll(_before); }
    }
}

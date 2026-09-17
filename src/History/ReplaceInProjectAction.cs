using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.History
{
    /// <summary>Le remplacement projet comme UNE action d'historique (batch
    /// 37, lot C) : atomique et annulable en un cran, elle connaît tous les
    /// items touchés. Son foyer est HistoryManager — la seule pile de
    /// session qui voit plusieurs items — et non les instantanés locaux de
    /// la vue composée : la coquille, prévenue par HistoryManager.Applied,
    /// RECHARGE la vue du document ouvert s'il est touché (sa pile locale
    /// est vidée : jamais un état qui contredit le reste). Le contenu est un
    /// delta (ReplacePlan), pas un clone du manuscrit.</summary>
    public class ReplaceInProjectAction : IUndoableAction
    {
        private readonly Project _project;
        private readonly ReplacePlan _plan;

        public ReplaceInProjectAction(Project project, ReplacePlan plan)
        {
            _project = project;
            _plan = plan;
        }

        public ReplacePlan Plan { get { return _plan; } }
        public List<BinderItem> Items { get { return _plan.Items; } }
        public int Occurrences { get { return _plan.Occurrences; } }
        public int Conflicts { get { return _plan.Conflicts; } }

        public bool Touches(BinderItem item) { return _plan.Touches(item); }

        public void Do() { _plan.Apply(_project, true); }

        public void Undo() { _plan.Apply(_project, false); }
    }
}

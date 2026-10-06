using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.History
{
    /// <summary>L'état ÉDITABLE d'une fiche à un instant (1.0.4) : les valeurs
    /// des champs du modèle, les champs libres, les relations, le radar, le
    /// portrait, les étapes d'évolution, la catégorie et le modèle — tout ce
    /// que la vue d'une fiche écrit directement dans le modèle sans passer
    /// par une action. Le corps Markdown n'en fait pas partie (sa zone de
    /// texte a sa propre pile). Des COPIES, jamais les objets du projet.</summary>
    public sealed class SheetState
    {
        public string ImageId, CategoryId, TemplateId;
        public Dictionary<string, string> FieldValues = new Dictionary<string, string>();
        public Dictionary<string, double> RadarValues = new Dictionary<string, double>();
        public List<InfoEntry> FreeInfo = new List<InfoEntry>();
        public List<SheetRelation> Relations = new List<SheetRelation>();
        public List<EvolutionEntry> Evolution = new List<EvolutionEntry>();

        public static SheetState Capture(BinderItem item)
        {
            var state = new SheetState
            {
                ImageId = item.ImageId,
                CategoryId = item.CategoryId,
                TemplateId = item.TemplateId,
                FieldValues = new Dictionary<string, string>(item.FieldValues),
                RadarValues = new Dictionary<string, double>(item.RadarValues)
            };
            foreach (var entry in item.FreeInfo) state.FreeInfo.Add(Copy(entry));
            foreach (var relation in item.Relations) state.Relations.Add(Copy(relation));
            foreach (var step in item.Evolution) state.Evolution.Add(Copy(step));
            return state;
        }

        /// <summary>Remet la fiche dans cet état — listes et dictionnaires
        /// remplacés EN PLACE (la vue les relit), par des copies : l'état
        /// reste intact pour être rejoué.</summary>
        public void ApplyTo(BinderItem item)
        {
            item.ImageId = ImageId;
            item.CategoryId = CategoryId;
            item.TemplateId = TemplateId;
            item.FieldValues.Clear();
            foreach (var pair in FieldValues) item.FieldValues[pair.Key] = pair.Value;
            item.RadarValues.Clear();
            foreach (var pair in RadarValues) item.RadarValues[pair.Key] = pair.Value;
            item.FreeInfo.Clear();
            foreach (var entry in FreeInfo) item.FreeInfo.Add(Copy(entry));
            item.Relations.Clear();
            foreach (var relation in Relations) item.Relations.Add(Copy(relation));
            item.Evolution.Clear();
            foreach (var step in Evolution) item.Evolution.Add(Copy(step));
        }

        public static InfoEntry Copy(InfoEntry entry)
        {
            var copy = new InfoEntry { Id = entry.Id, Title = entry.Title, Value = entry.Value, Group = entry.Group, Kind = entry.Kind };
            copy.Options.AddRange(entry.Options);
            return copy;
        }

        public static SheetRelation Copy(SheetRelation relation)
        {
            return new SheetRelation { Id = relation.Id, Kind = relation.Kind, TargetId = relation.TargetId, Name = relation.Name };
        }

        public static EvolutionEntry Copy(EvolutionEntry step)
        {
            return new EvolutionEntry { Id = step.Id, TextId = step.TextId, Title = step.Title, Note = step.Note };
        }

        /// <summary>Ce qui diffère entre deux états, sous forme de CLÉ : une
        /// par « case » touchée (« field:ID », « free:ID », « relation:ID »,
        /// « evolution:ID », « radar », « image », « category »…), jointes par
        /// « | » ; vide si rien ne diffère. La vue s'en sert pour fondre les
        /// frappes successives dans la même case en UNE action.</summary>
        public static string DiffKey(SheetState a, SheetState b)
        {
            var keys = new List<string>();
            if (a.ImageId != b.ImageId) keys.Add("image");
            if (a.CategoryId != b.CategoryId) keys.Add("category");
            if (a.TemplateId != b.TemplateId) keys.Add("template");
            foreach (var id in Union(a.FieldValues.Keys, b.FieldValues.Keys))
            {
                string x, y;
                a.FieldValues.TryGetValue(id, out x);
                b.FieldValues.TryGetValue(id, out y);
                if ((x ?? "") != (y ?? "")) keys.Add("field:" + id);
            }
            foreach (var id in Union(a.RadarValues.Keys, b.RadarValues.Keys))
            {
                double x, y;
                a.RadarValues.TryGetValue(id, out x);
                b.RadarValues.TryGetValue(id, out y);
                if (x != y) { keys.Add("radar"); break; }
            }
            DiffList(keys, "free", a.FreeInfo, b.FreeInfo, delegate(InfoEntry e) { return e.Id; },
                delegate(InfoEntry x, InfoEntry y) { return x.Title == y.Title && x.Value == y.Value && x.Group == y.Group && x.Kind == y.Kind && string.Join("\n", x.Options) == string.Join("\n", y.Options); });
            DiffList(keys, "relation", a.Relations, b.Relations, delegate(SheetRelation r) { return r.Id; },
                delegate(SheetRelation x, SheetRelation y) { return x.Kind == y.Kind && x.TargetId == y.TargetId && x.Name == y.Name; });
            DiffList(keys, "evolution", a.Evolution, b.Evolution, delegate(EvolutionEntry s) { return s.Id; },
                delegate(EvolutionEntry x, EvolutionEntry y) { return x.TextId == y.TextId && x.Title == y.Title && x.Note == y.Note; });
            return string.Join("|", keys);
        }

        private static void DiffList<T>(List<string> keys, string prefix, List<T> a, List<T> b, Func<T, string> id, Func<T, T, bool> same)
        {
            var byId = new Dictionary<string, T>();
            foreach (var x in a) byId[id(x)] = x;
            var seen = new HashSet<string>();
            foreach (var y in b)
            {
                T x;
                seen.Add(id(y));
                if (!byId.TryGetValue(id(y), out x)) { keys.Add(prefix + "-set"); continue; }
                if (!same(x, y)) keys.Add(prefix + ":" + id(y));
            }
            foreach (var x in a)
                if (!seen.Contains(id(x))) { keys.Add(prefix + "-set"); break; }
            if (a.Count != b.Count && !keys.Contains(prefix + "-set")) keys.Add(prefix + "-set");
        }

        private static IEnumerable<string> Union(IEnumerable<string> a, IEnumerable<string> b)
        {
            var set = new HashSet<string>(a);
            foreach (var x in b) set.Add(x);
            return set;
        }
    }

    /// <summary>Une fiche et son VOISINAGE à un instant : son état, plus les
    /// relations des autres fiches qui la visent ou qu'elle vise (les reflets
    /// posés par RelationSync vivent chez elles). Capturé AVANT l'édition
    /// (l'ombre que la vue garde) et APRÈS (à chaque signal Edited).</summary>
    public sealed class SheetSnapshot
    {
        public SheetState State;
        public Dictionary<string, List<SheetRelation>> Related = new Dictionary<string, List<SheetRelation>>();

        public static SheetSnapshot Capture(Project project, BinderItem item)
        {
            var snapshot = new SheetSnapshot { State = SheetState.Capture(item) };
            if (project == null) return snapshot;
            foreach (var other in project.AllItems())
            {
                if (other == item || other.Kind != ItemKind.Sheet) continue;
                var linked = false;
                foreach (var relation in other.Relations) if (relation.TargetId == item.Id) { linked = true; break; }
                if (!linked) foreach (var relation in item.Relations) if (relation.TargetId == other.Id) { linked = true; break; }
                if (!linked) continue;
                var copies = new List<SheetRelation>();
                foreach (var relation in other.Relations) copies.Add(SheetState.Copy(relation));
                snapshot.Related[other.Id] = copies;
            }
            return snapshot;
        }

        public static string DiffKey(SheetSnapshot a, SheetSnapshot b)
        {
            return SheetState.DiffKey(a.State, b.State);
        }
    }

    /// <summary>Une mutation de la STRUCTURE des fiches (1.0.4) : les modèles
    /// remplacés par l'éditeur de modèles, une catégorie créée, renommée,
    /// rebasée ou supprimée — deux délégués, et un libellé ; la coquille
    /// recharge la bibliothèque, la Pile et la fiche ouverte quand l'une
    /// d'elles est défaite ou refaite.</summary>
    public sealed class SheetStructureAction : IUndoableAction
    {
        private readonly Action _apply, _revert;
        public string Label { get; private set; }

        public SheetStructureAction(string label, Action apply, Action revert)
        {
            Label = label;
            _apply = apply;
            _revert = revert;
        }

        public void Do() { _apply(); }
        public void Undo() { _revert(); }
    }

    /// <summary>L'édition d'une fiche, annulable (1.0.4 — « ce manque a créé
    /// beaucoup de friction ») : d'un instantané à l'autre. Les frappes
    /// successives dans la même case se FONDENT (Extend) : Ctrl+Z rend le
    /// champ, pas la lettre. Les reflets chez les fiches liées suivent : une
    /// fiche présente dans un seul des deux instantanés y perd les relations
    /// qui visent celle-ci (c'est la seule trace qu'une édition laisse chez
    /// les autres).</summary>
    public sealed class SheetEditAction : IUndoableAction
    {
        private readonly Project _project;
        private readonly BinderItem _item;
        private readonly SheetSnapshot _before;
        private SheetSnapshot _after;
        private string _key;
        private DateTime _when;

        public SheetEditAction(Project project, BinderItem item, SheetSnapshot before, SheetSnapshot after, string key)
        {
            _project = project;
            _item = item;
            _before = before;
            _after = after;
            _key = key;
            _when = DateTime.UtcNow;
        }

        public BinderItem Item { get { return _item; } }
        public string Key { get { return _key; } }

        /// <summary>Une frappe de plus dans la même case, peu après la
        /// précédente : l'action s'étend au lieu d'en empiler une autre.</summary>
        public bool CanExtend(BinderItem item, string key, TimeSpan within)
        {
            if (item != _item || key != _key || key.Length == 0) return false;
            if (key.EndsWith("-set") || key.Contains("|")) return false; // ajout, retrait, ou plusieurs cases : jamais fondus
            return DateTime.UtcNow - _when <= within;
        }

        public void Extend(SheetSnapshot after)
        {
            _after = after;
            _when = DateTime.UtcNow;
        }

        public void Do() { Apply(_after, _before); }
        public void Undo() { Apply(_before, _after); }

        private void Apply(SheetSnapshot target, SheetSnapshot other)
        {
            target.State.ApplyTo(_item);
            foreach (var pair in target.Related)
            {
                var sheet = _project == null ? null : _project.FindById(pair.Key);
                if (sheet == null) continue;
                sheet.Relations.Clear();
                foreach (var relation in pair.Value) sheet.Relations.Add(SheetState.Copy(relation));
            }
            foreach (var pair in other.Related)
            {
                if (target.Related.ContainsKey(pair.Key)) continue;
                var sheet = _project == null ? null : _project.FindById(pair.Key);
                if (sheet == null) continue;
                sheet.Relations.RemoveAll(delegate(SheetRelation r) { return r.TargetId == _item.Id; });
            }
        }
    }
}

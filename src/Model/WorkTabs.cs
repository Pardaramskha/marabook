using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Un ONGLET de la zone centrale (1.1.0, 28/10) : il retient
    /// l'élément de la Pile qu'il montre — rien d'autre. Les vues du centre
    /// sont uniques (un éditeur, une fiche, un tableau…) : changer d'onglet,
    /// c'est rouvrir son élément dans la vue qui lui revient.</summary>
    public class WorkTab
    {
        public string ItemId;

        public WorkTab(string itemId) { ItemId = itemId; }
    }

    /// <summary>La RANGÉE D'ONGLETS (1.1.0) : une liste ordonnée, un actif,
    /// un plafond. Toute la logique (ouvrir, fermer, naviguer, suivre un
    /// élément supprimé, serrer sous un plafond abaissé) vit ici, sans
    /// interface — testée au cœur. La fenêtre ne fait que refléter.</summary>
    public class WorkTabs
    {
        /// <summary>Les bornes du plafond (Préférences › Général › Onglets).</summary>
        public const int MinLimit = 5, MaxLimit = 30, DefaultLimit = 10;

        private readonly List<WorkTab> _tabs = new List<WorkTab>();
        private int _active = -1;
        private int _limit = DefaultLimit;

        public IList<WorkTab> Tabs { get { return _tabs.AsReadOnly(); } }
        public int Count { get { return _tabs.Count; } }
        public int ActiveIndex { get { return _active; } }
        public WorkTab Active { get { return _active >= 0 && _active < _tabs.Count ? _tabs[_active] : null; } }
        public string ActiveItemId { get { var tab = Active; return tab == null ? null : tab.ItemId; } }

        /// <summary>Le plafond, borné entre MinLimit et MaxLimit ; l'abaisser
        /// ferme les onglets en trop par la droite — jamais l'actif.</summary>
        public int Limit
        {
            get { return _limit; }
            set
            {
                _limit = Clamp(value);
                Trim();
            }
        }

        public static int Clamp(int limit)
        {
            return Math.Max(MinLimit, Math.Min(MaxLimit, limit));
        }

        public bool CanOpen { get { return _tabs.Count < _limit; } }

        public WorkTab this[int index] { get { return _tabs[index]; } }

        /// <summary>Un onglet de plus, en bout de rangée, actif ; null si le
        /// plafond est atteint (rien ne change alors).</summary>
        public WorkTab Open(string itemId)
        {
            if (!CanOpen) return null;
            var tab = new WorkTab(itemId);
            _tabs.Add(tab);
            _active = _tabs.Count - 1;
            return tab;
        }

        /// <summary>L'onglet actif montre désormais cet élément (la Pile a
        /// ouvert autre chose) ; sans onglet, le premier naît ici. True si
        /// quelque chose a changé.</summary>
        public bool Navigate(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            var tab = Active;
            if (tab == null)
            {
                if (_tabs.Count == 0) { _tabs.Add(new WorkTab(itemId)); _active = 0; return true; }
                _active = 0;
                tab = _tabs[0];
            }
            if (tab.ItemId == itemId) return false;
            tab.ItemId = itemId;
            return true;
        }

        public bool Activate(int index)
        {
            if (index < 0 || index >= _tabs.Count || index == _active) return false;
            _active = index;
            return true;
        }

        /// <summary>Ferme cet onglet. L'actif fermé passe la main à son voisin
        /// de droite, sinon de gauche (comme Chrome) ; un autre fermé laisse
        /// l'actif où il est. False si l'index n'existe pas.</summary>
        public bool Close(int index)
        {
            if (index < 0 || index >= _tabs.Count) return false;
            _tabs.RemoveAt(index);
            if (_tabs.Count == 0) { _active = -1; return true; }
            if (index < _active) _active--;
            else if (index == _active) _active = Math.Min(index, _tabs.Count - 1);
            return true;
        }

        /// <summary>Ferme tous les autres onglets ; celui-ci devient l'actif.</summary>
        public void CloseOthers(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            var keep = _tabs[index];
            _tabs.Clear();
            _tabs.Add(keep);
            _active = 0;
        }

        /// <summary>Ferme les onglets à droite de celui-ci ; l'actif, s'il
        /// était parmi eux, devient celui-ci.</summary>
        public void CloseRight(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            while (_tabs.Count > index + 1) _tabs.RemoveAt(_tabs.Count - 1);
            if (_active > index) _active = index;
        }

        /// <summary>L'onglet suivant (cyclique). False sans changement.</summary>
        public bool Next() { return Step(1); }
        public bool Previous() { return Step(-1); }

        private bool Step(int delta)
        {
            if (_tabs.Count < 2) return false;
            _active = (_active + delta + _tabs.Count) % _tabs.Count;
            return true;
        }

        public int IndexOf(string itemId)
        {
            for (var i = 0; i < _tabs.Count; i++) if (_tabs[i].ItemId == itemId) return i;
            return -1;
        }

        /// <summary>Un élément a disparu du projet (corbeille vidée, ajout
        /// annulé) : ses onglets tombent. True si l'actif en faisait partie
        /// — la fenêtre doit alors montrer le nouvel actif.</summary>
        public bool Drop(string itemId)
        {
            var activeDropped = false;
            for (var i = _tabs.Count - 1; i >= 0; i--)
            {
                if (_tabs[i].ItemId != itemId) continue;
                if (i == _active) activeDropped = true;
                Close(i);
            }
            return activeDropped;
        }

        /// <summary>Ne garde que les onglets dont l'élément existe encore
        /// (le juge dit lequel). Rend true si l'actif a été retiré.</summary>
        public bool Prune(Func<string, bool> exists)
        {
            var activeDropped = false;
            for (var i = _tabs.Count - 1; i >= 0; i--)
            {
                if (exists(_tabs[i].ItemId)) continue;
                if (i == _active) activeDropped = true;
                Close(i);
            }
            return activeDropped;
        }

        /// <summary>Les onglets en trop au-delà du plafond, fermés par la
        /// droite, l'actif épargné (il recule d'une place s'il le faut).</summary>
        private void Trim()
        {
            while (_tabs.Count > _limit)
            {
                var victim = _tabs.Count - 1;
                if (victim == _active) victim--;
                if (victim < 0) break;
                Close(victim);
            }
        }

        public void Clear()
        {
            _tabs.Clear();
            _active = -1;
        }

        /// <summary>Les ids, dans l'ordre — ce que le .plot retient.</summary>
        public List<string> ItemIds()
        {
            var ids = new List<string>();
            foreach (var tab in _tabs) ids.Add(tab.ItemId);
            return ids;
        }

        /// <summary>Reprend une rangée enregistrée : ids inconnus écartés par
        /// le juge, doublons gardés (deux onglets peuvent montrer le même
        /// élément), plafond respecté, actif borné.</summary>
        public void Restore(IList<string> ids, int active, Func<string, bool> exists)
        {
            Clear();
            if (ids == null) return;
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (string.IsNullOrEmpty(id) || (exists != null && !exists(id))) continue;
                if (_tabs.Count >= _limit) break;
                _tabs.Add(new WorkTab(id));
                if (i == active) _active = _tabs.Count - 1;
            }
            if (_tabs.Count == 0) _active = -1;
            else if (_active < 0 || _active >= _tabs.Count) _active = 0;
        }
    }
}

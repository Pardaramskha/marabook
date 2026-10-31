using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LES MISES À JOUR DES DLC (26/10/2026, Mental-O en DLC) : à la
    /// même vérification que celle de Marabook (ScheduleUpdateCheck), chaque
    /// module INSTALLÉ qui vient du catalogue interroge sa release GitHub ;
    /// s'il y a plus récent, un toast propre au module le dit — « Une
    /// nouvelle version de Mental-O est disponible » — avec « Mettre à jour »
    /// (téléchargement et installation sans redémarrage) et « Plus tard ».
    /// Rien ne se dit si le réseau manque. Jamais en sonde, démo ou capture.</summary>
    public partial class MainWindow
    {
        private readonly HashSet<string> _moduleUpdatesOffered = new HashSet<string>();
        private bool _moduleUpdatesHooked;

        // ------------------------------------------------------------ le projet vu par les modules (26/10 soir)

        /// <summary>Les éléments liables du projet (ni catégories, ni corbeille).</summary>
        public List<HostItem> ItemsForModules()
        {
            var items = new List<HostItem>();
            if (_project == null) return items;
            foreach (var item in _project.AllItems())
                if (ItemTiles.IsLinkable(item)) items.Add(ItemTiles.Describe(item));
            return items;
        }

        public HostItem FindItemForModules(string id)
        {
            var item = _project == null || string.IsNullOrEmpty(id) ? null : _project.FindById(id);
            return ItemTiles.IsLinkable(item) ? ItemTiles.Describe(item) : null;
        }

        public async System.Threading.Tasks.Task<HostItem> PickItemForModules(string caption, string[] kinds)
        {
            var item = await ItemPickerDialog.Ask(this, _project, caption, kinds);
            return item == null ? null : ItemTiles.Describe(item);
        }

        /// <summary>Ouvre l'élément au centre, comme un clic dans la Pile.</summary>
        public void OpenItemForModules(string id)
        {
            var item = _project == null || string.IsNullOrEmpty(id) ? null : _project.FindById(id);
            if (!ItemTiles.IsLinkable(item)) return;
            _binder.SelectItem(item.Id, true);
        }

        public Avalonia.Controls.Control TileForModules(string id, double width, bool condensed)
        {
            var item = _project == null || string.IsNullOrEmpty(id) ? null : _project.FindById(id);
            return ItemTiles.IsLinkable(item) ? ItemTiles.Build(_project, item, width, condensed) : null;
        }

        private void CheckModuleUpdates()
        {
            if (UpdateCheckSkipped) return;
            if (!_moduleUpdatesHooked)
            {
                _moduleUpdatesHooked = true;
                ModuleStore.Changed += OfferModuleUpdates;
            }
            ModuleStore.CheckOnline(Dispatcher.UIThread);
        }

        private void OfferModuleUpdates()
        {
            foreach (var state in ModuleStore.States)
            {
                if (!state.HasUpdate || state.Busy) continue;
                if (_moduleUpdatesOffered.Contains(state.Source.Id)) continue;
                _moduleUpdatesOffered.Add(state.Source.Id);
                var stateRef = state;
                ShowNotice(NoticeToast.Build("file-arrow-down-bold",
                    "Une nouvelle version de " + state.Source.Name + " est disponible",
                    state.Source.Name + " " + state.Latest.Version + " est publiée (vous avez la " + state.Installed.Version + "). "
                    + "La mettre à jour télécharge le module et le remplace sans redémarrer Marabook ; vos cartes restent dans vos projets.",
                    "Mettre à jour", delegate { InstallModuleUpdate(stateRef); }, "Plus tard", null));
            }
        }

        private void InstallModuleUpdate(ModuleState state)
        {
            ModuleStore.Install(state, Dispatcher.UIThread, delegate(string failure)
            {
                if (failure != null)
                    MessageDialog.Show(this, "La mise à jour de " + state.Source.Name + " a échoué : " + failure,
                        "DLC", MessageButtons.OK, MessageIcon.Warning);
                else
                    MessageDialog.Show(this, state.Source.Name + " " + (state.Installed != null ? state.Installed.Version : "") + " est installé — prêt, sans redémarrage.",
                        "DLC", MessageButtons.OK, MessageIcon.Information);
            });
        }
    }
}

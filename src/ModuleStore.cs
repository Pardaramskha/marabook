using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Threading;
using Marabook.Model;

namespace Marabook
{
    /// <summary>L'état d'un module du catalogue, tel que l'accueil et les
    /// Préférences le montrent : installé (et sa version), publié (la
    /// dernière release et son paquet), ou ce qui empêche de le savoir.</summary>
    public sealed class ModuleState
    {
        public ModuleSource Source;
        public ModuleInfo Installed;     // null si absent
        public Updater.Info Latest;      // null tant que GitHub n'a pas répondu
        public string Message = "";      // « Vérification… », « Dépôt inaccessible »…
        public bool Checked;             // GitHub a répondu (ou refusé)
        public bool Busy;                // téléchargement en cours

        public bool IsInstalled { get { return Installed != null; } }
        public bool IsAvailable { get { return Latest != null && Latest.ZipUrl.Length > 0; } }

        /// <summary>Une version plus récente que celle installée ?</summary>
        public bool HasUpdate
        {
            get { return IsInstalled && IsAvailable && Updater.IsNewer(Latest.Version, Installed.Version); }
        }

        /// <summary>La ligne d'état lisible.</summary>
        public string Label
        {
            get
            {
                if (Busy) return Message.Length > 0 ? Message : "Téléchargement…";
                if (IsInstalled)
                    return "Installé" + (Installed.Version.Length > 0 ? " (" + Installed.Version + ")" : "")
                        + (HasUpdate ? " — " + Latest.Version + " disponible" : "");
                if (IsAvailable) return "Disponible (" + Latest.Version + ")";
                if (!Checked) return "Vérification…";
                return Message.Length > 0 ? Message : "Indisponible";
            }
        }
    }

    /// <summary>LE MAGASIN DES MODULES (DLC, 22/09) : le catalogue de Marabook
    /// croisé avec les modules installés et les releases GitHub de leurs
    /// dépôts. Sans WPF : les vérifications et téléchargements tournent sur
    /// des tâches de fond, les vues sont rappelées sur leur Dispatcher.
    /// Un dépôt privé répond 404 sans jeton (GITHUB_TOKEN) — l'état le dit ;
    /// « Installer depuis un fichier » reste toujours possible.</summary>
    public static class ModuleStore
    {
        public static readonly List<ModuleState> States = new List<ModuleState>();

        /// <summary>Un état a changé : les vues se redessinent (sur le Dispatcher appelant).</summary>
        public static event Action Changed;

        private static bool _built;

        /// <summary>Le catalogue croisé avec les modules installés (synchrone).</summary>
        public static List<ModuleState> Refresh()
        {
            if (!_built)
            {
                foreach (var source in Modules.Catalogue) States.Add(new ModuleState { Source = source });
                _built = true;
            }
            foreach (var state in States) state.Installed = Modules.Find(state.Source.Id);
            // Un module installé hors catalogue (depuis un fichier) apparaît aussi.
            foreach (var module in Modules.Installed)
            {
                var known = false;
                foreach (var state in States) if (state.Source.Id == module.Id) { known = true; break; }
                if (known) continue;
                States.Add(new ModuleState
                {
                    Source = new ModuleSource { Id = module.Id, Name = module.Name, Title = module.Title, Features = module.Features },
                    Installed = module,
                    Checked = true,
                    Message = "Installé depuis un fichier"
                });
            }
            States.RemoveAll(delegate(ModuleState s) { return s.Source.Repository.Length == 0 && s.Installed == null; });
            return States;
        }

        public static ModuleState Find(string id)
        {
            foreach (var state in States) if (state.Source.Id == id) return state;
            return null;
        }

        /// <summary>Interroge GitHub pour chaque module du catalogue, en fond ;
        /// Changed est levé sur le Dispatcher à chaque réponse.</summary>
        public static void CheckOnline(Dispatcher dispatcher)
        {
            Refresh();
            foreach (var state in States)
            {
                if (state.Source.Repository.Length == 0 || state.Checked) continue;
                var stateRef = state;
                Task.Factory.StartNew(delegate
                {
                    try
                    {
                        var info = Updater.LatestOf(stateRef.Source.Repository, stateRef.Source.Asset);
                        if (info.ZipUrl.Length == 0) throw new Exception("La release ne porte pas de paquet " + stateRef.Source.Asset);
                        return new KeyValuePair<Updater.Info, string>(info, "");
                    }
                    catch (Exception failure)
                    {
                        return new KeyValuePair<Updater.Info, string>(null, failure.Message);
                    }
                }).ContinueWith(delegate(Task<KeyValuePair<Updater.Info, string>> done)
                {
                    var result = done.Status == TaskStatus.RanToCompletion
                        ? done.Result : new KeyValuePair<Updater.Info, string>(null, "Vérification impossible");
                    dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        stateRef.Latest = result.Key;
                        stateRef.Message = result.Value;
                        stateRef.Checked = true;
                        RaiseChanged();
                    }));
                });
            }
        }

        /// <summary>Télécharge le paquet publié et l'installe, en fond ;
        /// « done » reçoit null ou le message d'échec, sur le Dispatcher.</summary>
        public static void Install(ModuleState state, Dispatcher dispatcher, Action<string> done)
        {
            if (state == null || !state.IsAvailable || state.Busy) return;
            state.Busy = true;
            state.Message = "Téléchargement de " + state.Source.Name + " " + state.Latest.Version + "…";
            RaiseChanged();
            var url = state.Latest.ZipUrl;
            var apiUrl = state.Latest.AssetApiUrl;
            var asset = state.Source.Asset;
            Task.Factory.StartNew(delegate
            {
                var temp = Path.Combine(Path.GetTempPath(), "marabook-" + Guid.NewGuid().ToString("N") + "-" + asset);
                try
                {
                    Download(url, apiUrl, temp);
                    Modules.Install(temp);
                }
                finally
                {
                    try { File.Delete(temp); } catch { }
                }
            }).ContinueWith(delegate(Task task)
            {
                var failure = task.Exception == null ? null : task.Exception.GetBaseException().Message;
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    state.Busy = false;
                    state.Message = failure ?? "";
                    Refresh();
                    RaiseChanged();
                    if (done != null) done(failure);
                }));
            });
        }

        /// <summary>Installe un paquet .mdlc du disque (synchrone) : le module lu.</summary>
        public static ModuleInfo InstallFromFile(string path)
        {
            var module = Modules.Install(path);
            Refresh();
            RaiseChanged();
            return module;
        }

        public static void Uninstall(ModuleState state)
        {
            if (state == null || !state.IsInstalled) return;
            Modules.Uninstall(state.Source.Id);
            Refresh();
            RaiseChanged();
        }

        /// <summary>Le paquet : par l'URL publique ; avec un jeton (dépôt
        /// privé), par l'API de l'asset — l'URL publique redirige vers un
        /// stockage qui refuse l'en-tête d'autorisation.</summary>
        private static void Download(string url, string apiUrl, string target)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "Marabook";
                var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
                if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(apiUrl))
                {
                    client.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
                    client.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
                    client.DownloadFile(apiUrl, target);
                    return;
                }
                client.DownloadFile(url, target);
            }
        }

        private static void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

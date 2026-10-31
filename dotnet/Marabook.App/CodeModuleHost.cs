using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE REGISTRE DES MODULES À CODE (26/10/2026) : posé sur
    /// Modules.CodeHost avant Modules.Load(). Le cœur charge la DLL du
    /// manifeste et instancie son type d'entrée ; ici on le reconnaît
    /// (IMarabookModule), on lui tend l'hôte (fenêtre, thème, messages) et on
    /// branche ce qu'il apporte — le fournisseur de cartes mentales sur
    /// MindMapModules.Provider. Désinstallé, le module se détache ; sa DLL
    /// reste chargée jusqu'à la fermeture (le cœur le sait : marqueur
    /// .retirer).</summary>
    public class CodeModuleHost : ICodeModuleHost, IModuleHost
    {
        private readonly Dictionary<string, IMarabookModule> _modules = new Dictionary<string, IMarabookModule>();
        private Window _window;
        private bool _dark;

        public static readonly CodeModuleHost Instance = new CodeModuleHost();

        /// <summary>Les modules chargés, dans l'ordre d'enregistrement.</summary>
        public IEnumerable<IMarabookModule> Loaded
        {
            get { return _modules.Values; }
        }

        public IMarabookModule Find(string id)
        {
            IMarabookModule module;
            return _modules.TryGetValue(id ?? "", out module) ? module : null;
        }

        // ------------------------------------------------------------ ICodeModuleHost

        public bool IsLoaded(string id)
        {
            return _modules.ContainsKey(id ?? "");
        }

        public void Register(string id, object instance)
        {
            var module = instance as IMarabookModule;
            if (module == null)
                throw new InvalidCastException("Le type d'entrée du module « " + id + "» n'implémente pas IMarabookModule");
            _modules[id] = module;
            module.Initialize(this);
            if (module.MindMaps != null) MindMapModules.SetProvider(module.MindMaps);
        }

        public void Unregister(string id)
        {
            IMarabookModule module;
            if (!_modules.TryGetValue(id ?? "", out module)) return;
            _modules.Remove(id);
            if (module.MindMaps != null && ReferenceEquals(MindMapModules.Provider, module.MindMaps))
            {
                // Un autre module fournit-il des cartes ? Sinon, plus de fournisseur.
                IMindMapProvider remaining = null;
                foreach (var other in _modules.Values) if (other.MindMaps != null) { remaining = other.MindMaps; break; }
                MindMapModules.SetProvider(remaining);
            }
        }

        // ------------------------------------------------------------ IModuleHost

        public Window MainWindow { get { return _window; } }

        public bool DarkTheme { get { return _dark; } }

        public string SettingsFolder { get { return Marabook.Settings.AppSettings.SettingsFolder(); } }

        public event Action ThemeChanged;

        /// <summary>La fenêtre principale se présente (au démarrage) : les
        /// modules chargés avant elle la reçoivent par ici.</summary>
        public void Attach(Window window)
        {
            _window = window;
        }

        /// <summary>Le thème a changé (Préférences) : les modules en sont avertis.</summary>
        public void SetDarkTheme(bool dark)
        {
            if (_dark == dark) return;
            _dark = dark;
            var handler = ThemeChanged;
            if (handler != null) handler();
        }

        public System.Threading.Tasks.Task<MessageResult> Message(string text, string caption, MessageButtons buttons, MessageIcon icon)
        {
            return MessageDialog.Show(_window, text, caption, buttons, icon);
        }

        // ---- le projet (26/10 soir) : tout passe par la fenêtre principale

        public event Action ProjectChanged;

        private MainWindow Shell { get { return _window as MainWindow; } }

        public List<HostItem> Items()
        {
            return Shell == null ? new List<HostItem>() : Shell.ItemsForModules();
        }

        public HostItem FindItem(string id)
        {
            return Shell == null ? null : Shell.FindItemForModules(id);
        }

        public System.Threading.Tasks.Task<HostItem> PickItem(string caption, string[] kinds)
        {
            if (Shell == null) return System.Threading.Tasks.Task.FromResult<HostItem>(null);
            return Shell.PickItemForModules(caption, kinds);
        }

        public void OpenItem(string id)
        {
            if (Shell != null) Shell.OpenItemForModules(id);
        }

        public Control Tile(string id, double width, bool condensed)
        {
            return Shell == null ? null : Shell.TileForModules(id, width, condensed);
        }

        /// <summary>La fenêtre signale un changement du projet (renommage,
        /// structure, autre projet ouvert).</summary>
        public void NotifyProjectChanged()
        {
            var handler = ProjectChanged;
            if (handler != null) handler();
        }
    }
}

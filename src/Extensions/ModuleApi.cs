using System;
using System.Collections.Generic;
using System.Windows;
using Marabook.Model;

namespace Marabook.Extensions
{
    /// <summary>L'API D'EXTENSION (22/09/2026) : ce qu'un module à CODE
    /// (un .mdlc qui porte une DLL, « entry » du manifeste) peut apporter à
    /// Marabook, et ce que Marabook lui prête. Premier module : Mental-o, les
    /// cartes mentales. Le contrat est volontairement petit et ne bouge plus
    /// sans bonne raison — une DLL compilée contre Marabook.exe compte dessus.</summary>
    public interface IMarabookModule
    {
        /// <summary>L'identifiant du manifeste (« mental-o »).</summary>
        string Id { get; }

        /// <summary>Appelé une fois la fenêtre principale bâtie (ou dès
        /// l'installation à chaud) : l'hôte reste valable toute la session.</summary>
        void Initialize(IModuleHost host);

        /// <summary>Les cartes mentales, si le module en fournit — null sinon.</summary>
        IMindMapProvider MindMaps { get; }
    }

    /// <summary>Ce que Marabook prête à un module : sa fenêtre, ses dialogues,
    /// son thème, et « le projet a changé ».</summary>
    public interface IModuleHost
    {
        Window MainWindow { get; }
        bool DarkTheme { get; }
        /// <summary>Le thème (clair / sombre, accent) vient de changer.</summary>
        event Action ThemeChanged;
        MessageBoxResult Message(string text, string title, MessageBoxButton buttons, MessageBoxImage image);
        /// <summary>Une saisie d'une ligne ; null si annulée.</summary>
        string Ask(string title, string prompt, string initial);
        /// <summary>Le projet ouvert est modifié (l'éditeur du module a changé quelque chose).</summary>
        void MarkDirty();
        /// <summary>Ouvre un élément de la Pile par son id (un lien de carte vers une fiche).</summary>
        void NavigateTo(string itemId);
    }

    /// <summary>Les cartes mentales : un éditeur, une carte neuve, une tuile.</summary>
    public interface IMindMapProvider
    {
        /// <summary>Un éditeur (réutilisé d'une carte à l'autre par l'hôte).</summary>
        IMindMapEditor CreateEditor();
        /// <summary>Les octets .tea d'une carte vierge portant ce titre.</summary>
        byte[] NewMap(string title);
        /// <summary>La vignette schématique d'une carte pour sa tuile (boîtes et liens), aux dimensions données ; null = pas de vignette.</summary>
        FrameworkElement Thumbnail(byte[] tea, double width, double height);
    }

    /// <summary>L'éditeur d'une carte, incrusté dans la fenêtre de Marabook :
    /// il reçoit les octets .tea de l'élément, signale ses modifications, et
    /// rend les octets à l'enregistrement.</summary>
    public interface IMindMapEditor
    {
        FrameworkElement View { get; }
        /// <summary>Charge la carte ; « title » est le titre de l'élément de la Pile.</summary>
        void Load(string itemId, string title, byte[] tea);
        /// <summary>Les octets .tea à jour (l'historique des versions relu depuis « previous »).</summary>
        byte[] Save(byte[] previous);
        bool IsDirty { get; }
        /// <summary>La carte a été modifiée (l'hôte marque le projet).</summary>
        event Action Changed;
        void Clear();
    }

    /// <summary>Les modules à code chargés (une instance par module installé),
    /// rattachés à l'hôte quand il existe.</summary>
    public static class ModuleRegistry
    {
        private static readonly Dictionary<string, IMarabookModule> _modules = new Dictionary<string, IMarabookModule>();
        private static IModuleHost _host;

        /// <summary>Un module a été chargé ou retiré.</summary>
        public static event Action Changed;

        public static void Register(string id, IMarabookModule module)
        {
            _modules[id] = module;
            if (_host != null)
            {
                try { module.Initialize(_host); }
                catch (Exception error) { System.Diagnostics.Debug.WriteLine("Module " + id + " : " + error.Message); }
            }
            RaiseChanged();
        }

        public static void Unregister(string id)
        {
            if (_modules.Remove(id)) RaiseChanged();
        }

        /// <summary>L'hôte est prêt : chaque module se rattache.</summary>
        public static void Attach(IModuleHost host)
        {
            _host = host;
            foreach (var module in new List<IMarabookModule>(_modules.Values))
            {
                try { module.Initialize(host); }
                catch (Exception error) { System.Diagnostics.Debug.WriteLine("Module : " + error.Message); }
            }
        }

        public static IMarabookModule Find(string id)
        {
            IMarabookModule module;
            return _modules.TryGetValue(id, out module) ? module : null;
        }

        public static IEnumerable<IMarabookModule> All { get { return _modules.Values; } }

        /// <summary>Le fournisseur de cartes mentales, s'il y en a un.</summary>
        public static IMindMapProvider MindMaps
        {
            get
            {
                foreach (var module in _modules.Values)
                    if (module.MindMaps != null) return module.MindMaps;
                return null;
            }
        }

        private static void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

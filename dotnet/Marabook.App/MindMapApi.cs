using System;
using Avalonia.Controls;

namespace Marabook.App
{
    /// <summary>L'API des cartes mentales que Marabook prête à un module à
    /// code (l'ex-Marabook.Extensions, redéfinie sur Avalonia). Aucun module
    /// n'est chargé dans le portage : Mental-o revient après la V1. Les
    /// cartes .tea d'un projet sont gardées telles quelles et montrent leur
    /// tuile de remplacement.</summary>
    public interface IMindMapEditor
    {
        Control View { get; }
        event Action Changed;
        bool IsDirty { get; }
        void Load(string itemId, string title, byte[] tea);
        byte[] Save(byte[] previous);
    }

    public interface IMindMapProvider
    {
        IMindMapEditor CreateEditor();
        byte[] NewMap(string title);
        /// <summary>Une carte garnie (quelques boîtes, liens, un groupe) : le
        /// projet d'exemple (--demo) l'emploie pour les captures et les sondes.</summary>
        byte[] SampleMap(string title);
        Control Thumbnail(byte[] tea, double width, double height);
    }

    public static class MindMapModules
    {
        /// <summary>Le fournisseur de cartes mentales ; null = aucun module.</summary>
        public static IMindMapProvider Provider;

        /// <summary>Le fournisseur a changé (module installé, désinstallé) :
        /// la Pile, les tableaux et les Préférences se redessinent.</summary>
        public static event Action ProviderChanged;

        public static void SetProvider(IMindMapProvider provider)
        {
            if (ReferenceEquals(Provider, provider)) return;
            Provider = provider;
            var handler = ProviderChanged;
            if (handler != null) handler();
        }
    }

    /// <summary>Ce que Marabook tend à un module à code (26/10/2026, retour de
    /// Mental-O en DLC Avalonia) : sa fenêtre, son thème, sa boîte de message.</summary>
    public interface IModuleHost
    {
        Window MainWindow { get; }
        bool DarkTheme { get; }
        event Action ThemeChanged;
        System.Threading.Tasks.Task<MessageResult> Message(string text, string caption, MessageButtons buttons, MessageIcon icon);
    }

    /// <summary>LE CONTRAT D'UN MODULE À CODE (26/10/2026) : le type d'entrée
    /// du manifeste (« entry ») l'implémente. Marabook l'instancie, lui tend
    /// son hôte, puis lui demande ce qu'il apporte : des cartes mentales
    /// (MindMaps, ou null), un onglet de Préférences (PreferencesTab, ou
    /// null — montré sous le filet « DLC » avec le titre PreferencesTitle).</summary>
    public interface IMarabookModule
    {
        string Id { get; }
        void Initialize(IModuleHost host);
        IMindMapProvider MindMaps { get; }
        string PreferencesTitle { get; }
        Control PreferencesTab();
    }
}

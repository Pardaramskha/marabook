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
        Control Thumbnail(byte[] tea, double width, double height);
    }

    public static class MindMapModules
    {
        /// <summary>Le fournisseur de cartes mentales ; null = aucun module.</summary>
        public static IMindMapProvider Provider;
    }
}

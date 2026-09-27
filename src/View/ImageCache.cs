using System;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Les images du projet décodées UNE fois (23/09/2026). Avant,
    /// le compositeur redécodait chaque image en pleine résolution à chaque
    /// composition — donc à chaque frappe — sans cache : un mémoire de
    /// 33 photos (82 Mo de PNG, 286 Mo décodés) rendait l'éditeur inutilisable.
    /// La table est faible sur l'objet ProjectImage : une image remplacée ou
    /// purgée libère sa bitmap avec elle. Une image plus large que
    /// MaxDecodeWidth est décodée à cette largeur (assez pour l'impression
    /// d'une image pleine colonne à 300 dpi) ; une image illisible est
    /// mémorisée comme telle pour ne pas réessayer à chaque passage.</summary>
    public static class ImageCache
    {
        public const int MaxDecodeWidth = 1600;

        private static readonly ConditionalWeakTable<ProjectImage, object> Cache = new ConditionalWeakTable<ProjectImage, object>();
        private static readonly object Unreadable = new object();

        public static ImageSource For(ProjectImage stored)
        {
            if (stored == null || stored.Bytes == null) return null;
            object cached;
            if (Cache.TryGetValue(stored, out cached)) return cached as ImageSource;
            var width = PixelWidthOf(stored.Bytes);
            var source = MediaView.TryImage(stored.Bytes, width > MaxDecodeWidth ? MaxDecodeWidth : 0);
            try { Cache.Add(stored, source ?? Unreadable); }
            catch (ArgumentException) { } // décodée en parallèle par un autre fil : la sienne vaut la nôtre
            return source;
        }

        /// <summary>La largeur en pixels lue dans l'en-tête (PNG, JPEG, GIF,
        /// BMP), 0 si inconnue — sans décoder. La lecture vit dans le cœur
        /// (ImageHeader, P0).</summary>
        public static int PixelWidthOf(byte[] b)
        {
            return ImageHeader.PixelWidth(b);
        }
    }
}

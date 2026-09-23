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
        /// BMP), 0 si inconnue — sans décoder.</summary>
        public static int PixelWidthOf(byte[] b)
        {
            if (b == null) return 0;
            try
            {
                if (b.Length > 24 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G')
                    return (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                if (b.Length > 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
                    return b[6] | (b[7] << 8);
                if (b.Length > 22 && b[0] == 'B' && b[1] == 'M')
                    return b[18] | (b[19] << 8) | (b[20] << 16) | (b[21] << 24);
                if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
                {
                    var i = 2;
                    while (i + 9 < b.Length && b[i] == 0xFF)
                    {
                        var marker = b[i + 1];
                        var length = (b[i + 2] << 8) | b[i + 3];
                        if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                            return (b[i + 7] << 8) | b[i + 8];
                        if (length < 2) break;
                        i += 2 + length;
                    }
                }
            }
            catch (IndexOutOfRangeException) { }
            return 0;
        }
    }
}

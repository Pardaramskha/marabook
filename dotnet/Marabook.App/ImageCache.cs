using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>Les images du projet décodées UNE fois (porté de
    /// View/ImageCache.cs) : la table est faible sur l'objet ProjectImage —
    /// une image remplacée ou purgée libère sa bitmap avec elle. Une image
    /// plus large que MaxDecodeWidth est décodée à cette largeur ; une image
    /// illisible est mémorisée comme telle.</summary>
    public static class ImageCache
    {
        public const int MaxDecodeWidth = 1600;

        private static readonly ConditionalWeakTable<ProjectImage, object> Cache = new ConditionalWeakTable<ProjectImage, object>();
        private static readonly object Unreadable = new object();

        public static Bitmap For(ProjectImage stored)
        {
            if (stored == null || stored.Bytes == null) return null;
            object cached;
            if (Cache.TryGetValue(stored, out cached)) return cached as Bitmap;
            var width = ImageHeader.PixelWidth(stored.Bytes);
            var bitmap = TryDecode(stored.Bytes, width > MaxDecodeWidth ? MaxDecodeWidth : 0);
            try { Cache.Add(stored, (object)bitmap ?? Unreadable); }
            catch (ArgumentException) { }
            return bitmap;
        }

        /// <summary>Une VIGNETTE de l'image (14/10, 1.0.6) : décodée une fois
        /// à la largeur demandée et gardée avec l'image (table faible, comme
        /// la bitmap pleine). La bibliothèque des fiches en rebâtit ses tuiles
        /// à chaque recherche, tri ou changement : sur le Serment (vingt
        /// portraits de 3 000 à 5 000 px, 43 Mo), chaque reconstruction
        /// redécodait tout — d'où la bibliothèque qui ramait.</summary>
        public static Bitmap Thumbnail(ProjectImage stored, int width)
        {
            if (stored == null || stored.Bytes == null || width <= 0) return null;
            Dictionary<int, object> sizes;
            lock (Thumbnails)
            {
                if (!Thumbnails.TryGetValue(stored, out sizes))
                {
                    sizes = new Dictionary<int, object>();
                    try { Thumbnails.Add(stored, sizes); }
                    catch (ArgumentException) { }
                }
            }
            object cached;
            lock (sizes)
            {
                if (sizes.TryGetValue(width, out cached)) return cached as Bitmap;
            }
            // Jamais agrandie : une image plus étroite que la vignette est
            // décodée telle quelle.
            var pixelWidth = ImageHeader.PixelWidth(stored.Bytes);
            var bitmap = TryDecode(stored.Bytes, pixelWidth > 0 && pixelWidth <= width ? 0 : width);
            lock (sizes) { sizes[width] = (object)bitmap ?? Unreadable; }
            return bitmap;
        }

        private static readonly ConditionalWeakTable<ProjectImage, Dictionary<int, object>> Thumbnails = new ConditionalWeakTable<ProjectImage, Dictionary<int, object>>();

        /// <summary>Décode des octets d'image ; decodeWidth > 0 = réduite à
        /// cette largeur. Null si illisible.</summary>
        public static Bitmap TryDecode(byte[] bytes, int decodeWidth)
        {
            try
            {
                using (var stream = new MemoryStream(bytes))
                    return decodeWidth > 0 ? Bitmap.DecodeToWidth(stream, decodeWidth) : new Bitmap(stream);
            }
            catch { return null; }
        }

        public static int PixelWidthOf(byte[] bytes)
        {
            return ImageHeader.PixelWidth(bytes);
        }
    }
}

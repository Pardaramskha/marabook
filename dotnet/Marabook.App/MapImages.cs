using System;
using Avalonia.Threading;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>Une carte en image, hors de l'écran (20/11) : pour
    /// l'Encyclopédie, qui la demande au cœur — la carte entière, à plat,
    /// avec ses zones et ses repères.</summary>
    internal static class MapImages
    {
        /// <summary>Le plus grand côté d'une carte de l'Encyclopédie : lisible plein écran, légère à charger.</summary>
        public const int WebMaxSide = 3200;

        public static byte[] Png(Project project, BinderItem item)
        {
            if (item == null || item.Map == null || !item.Map.HasImage) return null;
            // Le dessin appartient au fil de l'interface.
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.Invoke(delegate { return Png(project, item); });
            var view = new MapView();
            try
            {
                view.Load(item, project);
                using (var bitmap = view.Capture(true, WebMaxSide))
                {
                    if (bitmap == null) return null;
                    using (var stream = new System.IO.MemoryStream())
                    {
                        bitmap.Save(stream);
                        return stream.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                try { view.Clear(); } catch { }
            }
        }
    }
}

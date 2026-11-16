using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;

namespace Marabook.Persistence
{
    /// <summary>LES TUILES LUES À LA DEMANDE (lot 12, 16/11/2026). Une carte ouverte ne
    /// garde de ses tuiles que leur adresse dans le .plot ; quand l'écran en réclame une,
    /// elle est lue ici : le répertoire du zip (gardé tant que le fichier n'a pas bougé),
    /// puis l'entrée seule, vérifiée par sa taille et son CRC.
    ///
    /// Gate : le verrou que partagent la lecture d'une tuile et l'échange des fichiers à
    /// l'enregistrement — une tuile n'est jamais lue pendant que le .plot est remplacé.</summary>
    public static class PlotTiles
    {
        public static readonly object Gate = new object();

        private sealed class Known
        {
            public string Stamp;
            public Dictionary<string, PlotZip.Located> Entries;
        }

        private static readonly Dictionary<string, Known> Directories = new Dictionary<string, Known>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Pour la sonde et les tests : le nombre de tuiles lues sur le disque depuis le lancement.</summary>
        public static long Reads;

        static PlotTiles() { MapTileSet.Reader = Read; }

        /// <summary>À appeler une fois avant toute lecture : branche le lecteur sur le modèle.</summary>
        public static void Install() { MapTileSet.Reader = Read; }

        public static bool SamePath(string a, string b)
        {
            if (a == null || b == null) return false;
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Les octets d'une entrée d'un .plot, si elle est bien celle qu'on attend ; sinon null.</summary>
        public static byte[] Read(string path, string entry, uint crc, long size)
        {
            if (path == null || entry == null) return null;
            lock (Gate)
            {
                try
                {
                    var stamp = PlotFile.Stamp(path);
                    if (stamp == null) return null;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        Known known;
                        if (!Directories.TryGetValue(path, out known) || known.Stamp != stamp)
                        {
                            if (Directories.Count > 6) Directories.Clear();
                            known = new Known { Stamp = stamp, Entries = PlotZip.ReadDirectory(stream) };
                            Directories[path] = known;
                        }
                        PlotZip.Located located;
                        if (known.Entries == null || !known.Entries.TryGetValue(entry, out located)) return null;
                        if (located.Crc != crc || located.Size != size) return null;
                        var bytes = PlotZip.ReadEntry(stream, located);
                        if (bytes != null) Reads++;
                        return bytes;
                    }
                }
                catch (IOException) { return null; }
                catch (UnauthorizedAccessException) { return null; }
                catch (NotSupportedException) { return null; }
                catch (ArgumentException) { return null; }
            }
        }
    }
}

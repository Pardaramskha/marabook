using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>LES TUILES D'UNE CARTE (lot 12, 16/11/2026, Rémi : « tuiles lues à la
    /// demande dans le .plot au lieu de tout charger »). Chaque tuile est dans l'un de
    /// deux états :
    ///
    /// — EN MÉMOIRE : ses octets encodés, tels que l'import vient de les produire ;
    ///   c'est le cas d'une carte importée et pas encore enregistrée ;
    ///
    /// — SUR LE DISQUE : seuls son poids, son CRC et l'endroit où elle dort (le .plot,
    ///   le nom de l'entrée) sont gardés ; ses octets ne sont lus que quand l'écran la
    ///   réclame, puis rendus. C'est le cas de toute carte d'un projet ouvert, et d'une
    ///   carte importée dès le premier enregistrement.
    ///
    /// Une carte de 300 Mo n'occupe donc plus 300 Mo de mémoire : seulement les tuiles
    /// décodées que la vue montre. La table est PARTAGÉE entre les clones d'une carte
    /// (l'historique ne recopie jamais l'image). Sûre entre fils : le décodage lit hors
    /// du fil de l'interface, l'enregistrement relie les tuiles depuis son fil de fond.</summary>
    public sealed class MapTileSet
    {
        private sealed class Slot
        {
            public byte[] Bytes;     // en mémoire, ou null = sur le disque
            public long Size;
            public uint Crc;
            public string Path;      // le .plot où elle dort
            public string Entry;     // le nom de son entrée dans ce .plot
        }

        /// <summary>Ce qu'il faut savoir d'une tuile pour l'écrire.</summary>
        public struct Info
        {
            public byte[] Bytes;
            public long Size;
            public uint Crc;
            public string Path, Entry;
        }

        /// <summary>Le lecteur du disque (posé par la persistance) : chemin du .plot, nom de
        /// l'entrée, CRC et taille attendus → les octets, ou null si l'entrée n'est plus
        /// celle-là (fichier disparu, remplacé).</summary>
        public static Func<string, string, uint, long, byte[]> Reader;

        private static readonly List<WeakReference<MapTileSet>> Stored = new List<WeakReference<MapTileSet>>();

        private readonly object _gate = new object();
        private readonly Dictionary<string, Slot> _slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        private bool _tracked;

        public int Count { get { lock (_gate) return _slots.Count; } }

        public List<string> Keys { get { lock (_gate) return new List<string>(_slots.Keys); } }

        public bool Contains(string key) { lock (_gate) return _slots.ContainsKey(key); }

        /// <summary>Le poids de toutes les tuiles : la place de la carte dans le .plot.</summary>
        public long TotalBytes
        {
            get
            {
                long total = 0;
                lock (_gate) foreach (var slot in _slots.Values) total += slot.Size;
                return total;
            }
        }

        /// <summary>Ce qui est tenu en mémoire (les tuiles pas encore enregistrées).</summary>
        public long MemoryBytes
        {
            get
            {
                long total = 0;
                lock (_gate) foreach (var slot in _slots.Values) if (slot.Bytes != null) total += slot.Bytes.LongLength;
                return total;
            }
        }

        public int MemoryCount
        {
            get
            {
                var count = 0;
                lock (_gate) foreach (var slot in _slots.Values) if (slot.Bytes != null) count++;
                return count;
            }
        }

        public long Size(string key)
        {
            lock (_gate) { Slot slot; return _slots.TryGetValue(key, out slot) ? slot.Size : 0; }
        }

        /// <summary>Lire rend les octets (du disque au besoin) ; écrire pose une tuile en mémoire.</summary>
        public byte[] this[string key]
        {
            get { return Read(key); }
            set { Put(key, value); }
        }

        public void Put(string key, byte[] bytes)
        {
            if (bytes == null) return;
            lock (_gate) _slots[key] = new Slot { Bytes = bytes, Size = bytes.LongLength };
        }

        /// <summary>Une tuile qui dort dans un .plot : rien n'est lu.</summary>
        public void PutStored(string key, string path, string entry, uint crc, long size)
        {
            lock (_gate)
            {
                _slots[key] = new Slot { Size = size, Crc = crc, Path = path, Entry = entry };
                Track();
            }
        }

        /// <summary>La tuile vient d'être écrite dans ce .plot : ses octets en mémoire sont rendus,
        /// et c'est là qu'elle sera relue.</summary>
        public void MarkStored(string key, string path, string entry, uint crc, long size)
        {
            lock (_gate)
            {
                Slot slot;
                if (!_slots.TryGetValue(key, out slot) || slot.Size != size) return;
                // Une tuile en mémoire qui n'est pas celle qu'on vient d'écrire (remplacée entre-temps) reste où elle est.
                slot.Bytes = null;
                slot.Crc = crc;
                slot.Path = path;
                slot.Entry = entry;
                Track();
            }
        }

        private void Track()
        {
            if (_tracked) return;
            _tracked = true;
            lock (Stored)
            {
                Stored.RemoveAll(delegate(WeakReference<MapTileSet> reference) { MapTileSet alive; return !reference.TryGetTarget(out alive); });
                Stored.Add(new WeakReference<MapTileSet>(this));
            }
        }

        /// <summary>Les tables encore vivantes qui ont des tuiles sur le disque (celles que
        /// l'historique tient aussi : une image remplacée, une carte supprimée).</summary>
        public static List<MapTileSet> Live()
        {
            var list = new List<MapTileSet>();
            lock (Stored)
                foreach (var reference in Stored)
                {
                    MapTileSet alive;
                    if (reference.TryGetTarget(out alive)) list.Add(alive);
                }
            return list;
        }

        public Info Describe(string key)
        {
            lock (_gate)
            {
                Slot slot;
                if (!_slots.TryGetValue(key, out slot)) return new Info();
                return new Info { Bytes = slot.Bytes, Size = slot.Size, Crc = slot.Crc, Path = slot.Path, Entry = slot.Entry };
            }
        }

        public bool InMemory(string key)
        {
            lock (_gate) { Slot slot; return _slots.TryGetValue(key, out slot) && slot.Bytes != null; }
        }

        /// <summary>Les octets d'une tuile : de la mémoire, sinon du disque. Null si elle n'existe
        /// pas ou ne se relit plus.</summary>
        public byte[] Read(string key)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var info = Describe(key);
                if (info.Bytes != null) return info.Bytes;
                var reader = Reader;
                if (info.Path == null || reader == null) return null;
                var bytes = reader(info.Path, info.Entry, info.Crc, info.Size);
                if (bytes != null) return bytes;
                // Un enregistrement a pu la déplacer pendant la lecture : on relit une fois sa nouvelle adresse.
                var now = Describe(key);
                if (now.Bytes == null && now.Path == info.Path && now.Entry == info.Entry) return null;
            }
            return null;
        }

        public bool TryGetValue(string key, out byte[] bytes)
        {
            bytes = Read(key);
            return bytes != null;
        }

        /// <summary>Ramène en mémoire les tuiles qui dorment dans ce .plot (toutes si path est null) :
        /// avant que le fichier ne soit réécrit sans elles. Rend le nombre de tuiles qui n'ont pas pu être relues.</summary>
        public int Materialize(string path = null, Func<string, string, bool> samePath = null)
        {
            var failed = 0;
            foreach (var key in Keys)
            {
                var info = Describe(key);
                if (info.Bytes != null || info.Path == null) continue;
                if (path != null && !(samePath != null ? samePath(info.Path, path) : string.Equals(info.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                var reader = Reader;
                var bytes = reader == null ? null : reader(info.Path, info.Entry, info.Crc, info.Size);
                if (bytes == null) { failed++; continue; }
                lock (_gate)
                {
                    Slot slot;
                    if (_slots.TryGetValue(key, out slot) && slot.Bytes == null && slot.Entry == info.Entry) slot.Bytes = bytes;
                }
            }
            return failed;
        }

        /// <summary>Y a-t-il une tuile qui dort dans ce .plot ?</summary>
        public bool StoredIn(string path, Func<string, string, bool> samePath)
        {
            lock (_gate)
                foreach (var slot in _slots.Values)
                    if (slot.Bytes == null && slot.Path != null && samePath(slot.Path, path)) return true;
            return false;
        }
    }
}

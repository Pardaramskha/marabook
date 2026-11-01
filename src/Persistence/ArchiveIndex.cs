using System;
using System.Collections.Generic;
using System.IO;

namespace Marabook.Persistence
{
    /// <summary>L'empreinte d'une entrée de .plot telle qu'elle a été lue ou
    /// écrite (1.1.0, 30/10, enregistrement différentiel) : le CRC-32 du zip,
    /// la taille brute et un second condensé 64 bits indépendant. Deux
    /// entrées de même nom qui portent les trois sont tenues pour identiques
    /// octet à octet — une collision demanderait 2^-96, et les deux nombres
    /// sont calculés ici, jamais recopiés du fichier.</summary>
    public struct EntryDigest
    {
        public uint Crc;
        public long Size;
        public ulong Hash;

        public bool Same(EntryDigest other)
        {
            return Crc == other.Crc && Size == other.Size && Hash == other.Hash;
        }
    }

    /// <summary>Ce que le disque porte pour un projet : le chemin du .plot,
    /// son cachet (date + taille) et l'empreinte de chacune de ses entrées.
    /// PlotFile.Load en remplit un en lisant, PlotFile.Write en rend un neuf
    /// après avoir écrit ; l'écriture suivante s'en sert pour RECOPIER sans
    /// les recompresser les entrées qui n'ont pas changé — le gros d'un
    /// enregistrement, puisqu'un auteur touche à un chapitre à la fois.
    /// Un index qui ne désigne plus le fichier sur le disque (autre chemin,
    /// autre cachet) est simplement ignoré : jamais une entrée périmée.</summary>
    public sealed class ArchiveIndex
    {
        public string Path;
        public string Stamp;
        public string SaveId = "";
        public readonly Dictionary<string, EntryDigest> Entries = new Dictionary<string, EntryDigest>(StringComparer.Ordinal);

        /// <summary>L'index désigne-t-il encore le fichier qui est là ?</summary>
        public bool Describes(string path)
        {
            if (Path == null || path == null) return false;
            if (!string.Equals(System.IO.Path.GetFullPath(Path), System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) return false;
            var stamp = PlotFile.Stamp(path);
            return stamp != null && stamp == Stamp;
        }

        public void Note(string name, byte[] bytes)
        {
            Entries[name] = Digest.Of(bytes, 0, bytes.Length, null);
        }
    }

    /// <summary>Les deux condensés, en une passe sur les octets.</summary>
    public static class Digest
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>L'empreinte d'une entrée — « tail » est la suite éventuelle
        /// des octets (les instantanés : en-tête JSON puis données).</summary>
        public static EntryDigest Of(byte[] bytes, int offset, int count, byte[] tail)
        {
            var crc = 0xFFFFFFFFu;
            var hash = 0xcbf29ce484222325UL; // FNV-1a 64, graine
            Mix(bytes, offset, count, ref crc, ref hash);
            long size = count;
            if (tail != null)
            {
                Mix(tail, 0, tail.Length, ref crc, ref hash);
                size += tail.Length;
            }
            return new EntryDigest { Crc = crc ^ 0xFFFFFFFFu, Size = size, Hash = hash };
        }

        public static uint Crc32(byte[] bytes, int offset, int count)
        {
            var crc = 0xFFFFFFFFu;
            var end = offset + count;
            for (var i = offset; i < end; i++) crc = CrcTable[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static void Mix(byte[] bytes, int offset, int count, ref uint crc, ref ulong hash)
        {
            var end = offset + count;
            for (var i = offset; i < end; i++)
            {
                var b = bytes[i];
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
                hash = (hash ^ b) * 0x100000001b3UL;
            }
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}

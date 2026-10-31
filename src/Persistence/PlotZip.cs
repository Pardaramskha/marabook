using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Marabook.Persistence
{
    /// <summary>Le copieur zip maison (1.1.0, 30/10) : un écrivain d'archive
    /// qui sait DEUX choses que System.IO.Compression.ZipArchive ne sait pas —
    /// écrire une entrée « Stored » (méthode 0, pour les images déjà
    /// compressées) et RECOPIER telle quelle, octets deflate compris, une
    /// entrée d'une archive existante sans la décompresser ni la recompresser.
    /// C'est ce second point qui fait l'enregistrement différentiel : les
    /// entrées inchangées passent de l'ancien .plot au .tmp par simple copie
    /// de bloc, le deflate ne tourne que sur ce qui a changé. Le fichier
    /// produit reste un zip ordinaire, relu par ZipArchive comme avant
    /// (en-têtes locaux + répertoire central, version 2.0, noms en UTF-8,
    /// pas de descripteur de données, pas de zip64 : un .plot de plus de
    /// 4 Go repasse par ZipArchive — voir PlotFile.Write).</summary>
    public static class PlotZip
    {
        /// <summary>Une entrée du répertoire central d'une archive existante :
        /// de quoi la retrouver et la recopier.</summary>
        public sealed class Located
        {
            public string Name;
            public ushort Method;
            public uint Crc;
            public long CompressedSize;
            public long Size;
            public long LocalHeaderOffset;
        }

        /// <summary>Lit le répertoire central d'un zip — null si le fichier
        /// n'en a pas d'exploitable (tronqué, zip64, commentaire démesuré…).
        /// Ne lève jamais : un index illisible vaut « rien à recopier ».</summary>
        public static Dictionary<string, Located> ReadDirectory(Stream stream)
        {
            try
            {
                var length = stream.Length;
                if (length < 22) return null;
                // L'enregistrement de fin (EOCD) : signature + 18 octets, suivi
                // d'un commentaire d'au plus 65 535 octets — cherché depuis la fin.
                var span = (int)Math.Min(length, 22 + 65535);
                var tail = new byte[span];
                stream.Position = length - span;
                ReadFully(stream, tail, 0, span);
                var eocd = -1;
                for (var i = span - 22; i >= 0; i--)
                    if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 0x05 && tail[i + 3] == 0x06) { eocd = i; break; }
                if (eocd < 0) return null;
                var count = U16(tail, eocd + 10);
                var directorySize = U32(tail, eocd + 12);
                var directoryOffset = U32(tail, eocd + 16);
                if (count == 0xFFFF || directorySize == 0xFFFFFFFF || directoryOffset == 0xFFFFFFFF) return null; // zip64
                if (directoryOffset + directorySize > length) return null;
                var directory = new byte[directorySize];
                stream.Position = directoryOffset;
                ReadFully(stream, directory, 0, (int)directorySize);
                var result = new Dictionary<string, Located>(StringComparer.Ordinal);
                var at = 0;
                for (var n = 0; n < count; n++)
                {
                    if (at + 46 > directory.Length || U32(directory, at) != 0x02014b50) return null;
                    var flags = U16(directory, at + 8);
                    var entry = new Located
                    {
                        Method = U16(directory, at + 10),
                        Crc = U32(directory, at + 16),
                        CompressedSize = U32(directory, at + 20),
                        Size = U32(directory, at + 24),
                        LocalHeaderOffset = U32(directory, at + 42)
                    };
                    var nameLength = U16(directory, at + 28);
                    var extraLength = U16(directory, at + 30);
                    var commentLength = U16(directory, at + 32);
                    if (at + 46 + nameLength > directory.Length) return null;
                    var utf8 = (flags & 0x0800) != 0;
                    entry.Name = (utf8 ? Encoding.UTF8 : Encoding.ASCII).GetString(directory, at + 46, nameLength);
                    at += 46 + nameLength + extraLength + commentLength;
                    // Chiffrée, ou aux tailles en zip64 : pas recopiable.
                    if ((flags & 1) != 0 || entry.CompressedSize == 0xFFFFFFFF || entry.Size == 0xFFFFFFFF || entry.LocalHeaderOffset == 0xFFFFFFFF) continue;
                    result[entry.Name] = entry;
                }
                return result;
            }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        /// <summary>L'écrivain : les entrées dans l'ordre, puis Finish().</summary>
        public sealed class Writer
        {
            private readonly Stream _stream;
            private readonly List<byte[]> _central = new List<byte[]>();
            private readonly uint _stamp = DosStamp(DateTime.Now);
            public int Copied, Packed;

            public Writer(Stream stream) { _stream = stream; }

            /// <summary>Compresse (ou stocke) des octets neufs.</summary>
            public void Add(string name, byte[] bytes, byte[] tail, CompressionLevel level, uint crc, long size)
            {
                byte[] payload;
                ushort method;
                if (level == CompressionLevel.NoCompression)
                {
                    method = 0;
                    if (tail == null) payload = bytes;
                    else
                    {
                        payload = new byte[bytes.Length + tail.Length];
                        Buffer.BlockCopy(bytes, 0, payload, 0, bytes.Length);
                        Buffer.BlockCopy(tail, 0, payload, bytes.Length, tail.Length);
                    }
                }
                else
                {
                    method = 8;
                    using (var buffer = new MemoryStream(bytes.Length / 2 + 64))
                    {
                        using (var packer = new DeflateStream(buffer, level, true))
                        {
                            packer.Write(bytes, 0, bytes.Length);
                            if (tail != null) packer.Write(tail, 0, tail.Length);
                        }
                        payload = buffer.ToArray();
                    }
                }
                WriteEntry(name, method, crc, payload.Length, size, delegate { _stream.Write(payload, 0, payload.Length); });
                Packed++;
            }

            /// <summary>Recopie une entrée d'une archive existante, données
            /// compressées telles quelles. Faux si l'en-tête local ne répond
            /// pas (fichier abîmé) : rien n'a été écrit, l'appelant compresse.</summary>
            public bool Copy(string name, Stream source, Located located)
            {
                var header = new byte[30];
                try
                {
                    if (located.LocalHeaderOffset + 30 > source.Length) return false;
                    source.Position = located.LocalHeaderOffset;
                    ReadFully(source, header, 0, 30);
                    if (U32(header, 0) != 0x04034b50) return false;
                    var dataStart = located.LocalHeaderOffset + 30 + U16(header, 26) + U16(header, 28);
                    if (dataStart + located.CompressedSize > source.Length) return false;
                    source.Position = dataStart;
                }
                catch (IOException) { return false; }
                var remaining = located.CompressedSize;
                WriteEntry(name, located.Method, located.Crc, located.CompressedSize, located.Size, delegate
                {
                    var buffer = new byte[Math.Min(remaining, 1 << 16)];
                    while (remaining > 0)
                    {
                        var read = source.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                        if (read <= 0) throw new EndOfStreamException("Entrée « " + name + " » tronquée dans l'archive d'origine.");
                        _stream.Write(buffer, 0, read);
                        remaining -= read;
                    }
                });
                Copied++;
                return true;
            }

            private void WriteEntry(string name, ushort method, uint crc, long compressedSize, long size, Action payload)
            {
                if (compressedSize > 0xFFFFFFFEL || size > 0xFFFFFFFEL || _stream.Position > 0xFFFFFFFEL)
                    throw new NotSupportedException("Archive de plus de 4 Go : hors de portée du copieur.");
                var nameBytes = Encoding.UTF8.GetBytes(name);
                var offset = (uint)_stream.Position;
                var local = new byte[30 + nameBytes.Length];
                P32(local, 0, 0x04034b50); P16(local, 4, 20); P16(local, 6, 0x0800); P16(local, 8, method);
                P32(local, 10, _stamp); P32(local, 14, crc); P32(local, 18, (uint)compressedSize); P32(local, 22, (uint)size);
                P16(local, 26, (ushort)nameBytes.Length); P16(local, 28, 0);
                Buffer.BlockCopy(nameBytes, 0, local, 30, nameBytes.Length);
                _stream.Write(local, 0, local.Length);
                payload();
                var central = new byte[46 + nameBytes.Length];
                P32(central, 0, 0x02014b50); P16(central, 4, 20); P16(central, 6, 20); P16(central, 8, 0x0800); P16(central, 10, method);
                P32(central, 12, _stamp); P32(central, 16, crc); P32(central, 20, (uint)compressedSize); P32(central, 24, (uint)size);
                P16(central, 28, (ushort)nameBytes.Length); P16(central, 30, 0); P16(central, 32, 0); P16(central, 34, 0); P16(central, 36, 0);
                P32(central, 38, 0); P32(central, 42, offset);
                Buffer.BlockCopy(nameBytes, 0, central, 46, nameBytes.Length);
                _central.Add(central);
            }

            public void Finish()
            {
                var start = _stream.Position;
                long size = 0;
                foreach (var entry in _central) { _stream.Write(entry, 0, entry.Length); size += entry.Length; }
                if (_central.Count > 0xFFFE || start > 0xFFFFFFFEL || size > 0xFFFFFFFEL)
                    throw new NotSupportedException("Archive trop grande pour le copieur.");
                var end = new byte[22];
                P32(end, 0, 0x06054b50); P16(end, 4, 0); P16(end, 6, 0); P16(end, 8, (ushort)_central.Count); P16(end, 10, (ushort)_central.Count);
                P32(end, 12, (uint)size); P32(end, 16, (uint)start); P16(end, 20, 0);
                _stream.Write(end, 0, end.Length);
                _stream.Flush();
            }
        }

        private static void ReadFully(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                var read = stream.Read(buffer, offset, count);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
                count -= read;
            }
        }

        private static uint DosStamp(DateTime time)
        {
            if (time.Year < 1980) time = new DateTime(1980, 1, 1);
            var date = (uint)(((time.Year - 1980) << 9) | (time.Month << 5) | time.Day);
            var clock = (uint)((time.Hour << 11) | (time.Minute << 5) | (time.Second / 2));
            return (date << 16) | clock;
        }

        private static ushort U16(byte[] b, int at) { return (ushort)(b[at] | (b[at + 1] << 8)); }
        private static uint U32(byte[] b, int at) { return (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24)); }
        private static void P16(byte[] b, int at, ushort v) { b[at] = (byte)(v & 0xFF); b[at + 1] = (byte)(v >> 8); }
        private static void P32(byte[] b, int at, uint v) { P16(b, at, (ushort)(v & 0xFFFF)); P16(b, at + 2, (ushort)(v >> 16)); }
    }
}

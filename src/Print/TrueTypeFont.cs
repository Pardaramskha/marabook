using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media;

namespace UniversSale.Print
{
    /// <summary>Minimal TrueType parser for PDF embedding (4b-2): the metrics
    /// the font descriptor needs, and a SPARSE glyph subset — unused outlines
    /// are emptied but glyph ids keep their values, which is exactly what a
    /// CIDFontType2 with Identity-H encoding requires (our GlyphRuns already
    /// speak in glyph ids). OpenType CFF faces ('OTTO') expose metrics but no
    /// glyf subset: the raw file is embedded whole instead.</summary>
    public sealed class TrueTypeFont
    {
        public byte[] Bytes;                 // whole font file (may be a TTC)
        public bool IsCff;                   // 'OTTO' outlines: no glyf subset
        public double UnitsPerEm = 1000;
        public int XMin, YMin, XMax, YMax;   // font units
        public double Ascender, Descender;   // font units (descender négatif)
        public double CapHeight;             // font units
        public double ItalicAngle;           // degrees
        public string PostScriptName = "";
        public int WeightClass = 400;        // OS/2 usWeightClass — the weight
                                             // the FILE actually carries
        public bool IsVariable;              // fvar present: one file, many
                                             // instances (only the default
                                             // instance's outlines embed)

        private int _face;                            // table directory offset
        private Dictionary<string, int[]> _tables;    // tag → { offset, length }
        private int _numGlyphs;
        private bool _longLoca;

        public static TrueTypeFont Load(GlyphTypeface typeface)
        {
            try
            {
                byte[] data;
                using (var stream = typeface.GetFontStream())
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    data = buffer.ToArray();
                }
                var faceIndex = 0;
                try
                {
                    // Collections address the face in the uri fragment (#N).
                    var fragment = typeface.FontUri.Fragment;
                    if (!string.IsNullOrEmpty(fragment))
                        int.TryParse(fragment.TrimStart('#'), out faceIndex);
                }
                catch { }
                var font = new TrueTypeFont { Bytes = data };
                font.Parse(faceIndex);
                if (font.PostScriptName.Length == 0)
                    font.PostScriptName = FallbackName(typeface);
                return font;
            }
            catch { return null; }
        }

        private static string FallbackName(GlyphTypeface typeface)
        {
            foreach (var name in typeface.FamilyNames.Values)
                return Sanitize(name);
            return "Embedded";
        }

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name)
                if (c > ' ' && c < 127 && "()[]{}<>/%#".IndexOf(c) < 0)
                    sb.Append(c);
            return sb.Length == 0 ? "Embedded" : sb.ToString();
        }

        // ============================================================ parsing

        private void Parse(int faceIndex)
        {
            var offset = 0;
            if (Tag(Bytes, 0) == "ttcf")
            {
                var count = (int)U32(Bytes, 8);
                if (faceIndex < 0 || faceIndex >= count) faceIndex = 0;
                offset = (int)U32(Bytes, 12 + 4 * faceIndex);
            }
            _face = offset;
            IsCff = U32(Bytes, offset) == 0x4F54544F; // 'OTTO'

            var numTables = U16(Bytes, offset + 4);
            _tables = new Dictionary<string, int[]>();
            for (var i = 0; i < numTables; i++)
            {
                var entry = offset + 12 + 16 * i;
                _tables[Tag(Bytes, entry)] = new[]
                {
                    (int)U32(Bytes, entry + 8),
                    (int)U32(Bytes, entry + 12)
                };
            }

            int[] table;
            if (_tables.TryGetValue("head", out table))
            {
                UnitsPerEm = U16(Bytes, table[0] + 18);
                XMin = S16(Bytes, table[0] + 36);
                YMin = S16(Bytes, table[0] + 38);
                XMax = S16(Bytes, table[0] + 40);
                YMax = S16(Bytes, table[0] + 42);
                _longLoca = S16(Bytes, table[0] + 50) == 1;
            }
            if (_tables.TryGetValue("hhea", out table))
            {
                Ascender = S16(Bytes, table[0] + 4);
                Descender = S16(Bytes, table[0] + 6);
            }
            if (_tables.TryGetValue("maxp", out table))
                _numGlyphs = U16(Bytes, table[0] + 4);
            if (_tables.TryGetValue("post", out table))
                ItalicAngle = (int)U32(Bytes, table[0] + 4) / 65536.0;
            if (_tables.TryGetValue("OS/2", out table))
            {
                if (table[1] >= 6) WeightClass = U16(Bytes, table[0] + 4);
                if (U16(Bytes, table[0]) >= 2 && table[1] >= 90)
                    CapHeight = S16(Bytes, table[0] + 88);
            }
            if (CapHeight <= 0) CapHeight = 0.7 * UnitsPerEm;
            IsVariable = _tables.ContainsKey("fvar");
            PostScriptName = ReadName(6);
        }

        private string ReadName(int nameId)
        {
            int[] table;
            if (!_tables.TryGetValue("name", out table)) return "";
            var count = U16(Bytes, table[0] + 2);
            var strings = table[0] + U16(Bytes, table[0] + 4);
            var best = "";
            for (var i = 0; i < count; i++)
            {
                var record = table[0] + 6 + 12 * i;
                if (U16(Bytes, record + 6) != nameId) continue;
                var platform = U16(Bytes, record);
                var length = U16(Bytes, record + 8);
                var start = strings + U16(Bytes, record + 10);
                if (start + length > Bytes.Length) continue;
                if (platform == 3)
                    return Sanitize(Encoding.BigEndianUnicode.GetString(Bytes, start, length));
                if (platform == 1 && best.Length == 0)
                    best = Sanitize(Encoding.ASCII.GetString(Bytes, start, length));
            }
            return best;
        }

        // ============================================================ subset

        /// <summary>Sparse subset: same glyph count, same ids, unused outlines
        /// emptied. Returns null when the face cannot be subsetted (CFF).</summary>
        public byte[] Subset(HashSet<ushort> used)
        {
            if (IsCff) return null;
            int[] glyf, loca;
            if (!_tables.TryGetValue("glyf", out glyf)
                || !_tables.TryGetValue("loca", out loca)
                || !_tables.ContainsKey("head")
                || _numGlyphs <= 0) return null;

            var offsets = new int[_numGlyphs + 1];
            for (var i = 0; i <= _numGlyphs; i++)
                offsets[i] = _longLoca
                    ? (int)U32(Bytes, loca[0] + 4 * i)
                    : U16(Bytes, loca[0] + 2 * i) * 2;

            // Closure: composites pull their component glyphs in.
            var keep = new HashSet<ushort>();
            var stack = new Stack<ushort>();
            keep.Add(0);
            stack.Push(0);
            foreach (var g in used)
                if (g < _numGlyphs && keep.Add(g)) stack.Push(g);
            while (stack.Count > 0)
            {
                var g = stack.Pop();
                var start = glyf[0] + offsets[g];
                var length = offsets[g + 1] - offsets[g];
                if (length < 10 || S16(Bytes, start) >= 0) continue; // simple/empty
                var pos = start + 10;
                while (true)
                {
                    var flags = U16(Bytes, pos);
                    var component = (ushort)U16(Bytes, pos + 2);
                    if (component < _numGlyphs && keep.Add(component)) stack.Push(component);
                    pos += 4;
                    pos += (flags & 0x0001) != 0 ? 4 : 2;       // args
                    if ((flags & 0x0008) != 0) pos += 2;        // scale
                    else if ((flags & 0x0040) != 0) pos += 4;   // x & y scale
                    else if ((flags & 0x0080) != 0) pos += 8;   // 2×2
                    if ((flags & 0x0020) == 0) break;           // more components
                }
            }

            // New glyf (kept outlines only) + long loca.
            var newGlyf = new MemoryStream();
            var newLoca = new byte[4 * (_numGlyphs + 1)];
            for (var g = 0; g < _numGlyphs; g++)
            {
                WriteU32(newLoca, 4 * g, (uint)newGlyf.Length);
                if (!keep.Contains((ushort)g)) continue;
                var length = offsets[g + 1] - offsets[g];
                if (length <= 0) continue;
                newGlyf.Write(Bytes, glyf[0] + offsets[g], length);
                while (newGlyf.Length % 4 != 0) newGlyf.WriteByte(0);
            }
            WriteU32(newLoca, 4 * _numGlyphs, (uint)newGlyf.Length);

            // head: checkSumAdjustment reset, long loca declared.
            var head = Slice("head");
            WriteU32(head, 8, 0);
            head[50] = 0;
            head[51] = 1;

            // post: minimal version 3 (no glyph names), metrics copied.
            byte[] post = null;
            int[] source;
            if (_tables.TryGetValue("post", out source) && source[1] >= 16)
            {
                post = new byte[32];
                WriteU32(post, 0, 0x00030000);
                Array.Copy(Bytes, source[0] + 4, post, 4, 12);
            }

            var output = new List<KeyValuePair<string, byte[]>>();
            foreach (var tag in new[] { "cmap", "cvt ", "fpgm", "prep", "hhea", "hmtx", "maxp" })
                if (_tables.ContainsKey(tag))
                    output.Add(new KeyValuePair<string, byte[]>(tag, Slice(tag)));
            output.Add(new KeyValuePair<string, byte[]>("head", head));
            output.Add(new KeyValuePair<string, byte[]>("glyf", newGlyf.ToArray()));
            output.Add(new KeyValuePair<string, byte[]>("loca", newLoca));
            if (post != null) output.Add(new KeyValuePair<string, byte[]>("post", post));
            output.Sort(delegate(KeyValuePair<string, byte[]> a, KeyValuePair<string, byte[]> b)
            { return string.CompareOrdinal(a.Key, b.Key); });

            return Assemble(output);
        }

        /// <summary>sfnt assembly: directory, aligned tables, checksums, and
        /// the whole-font adjustment stamped back into head.</summary>
        private static byte[] Assemble(List<KeyValuePair<string, byte[]>> tables)
        {
            var count = tables.Count;
            var entrySelector = Log2(count);
            var searchRange = 16 * (1 << entrySelector);

            var directorySize = 12 + 16 * count;
            var total = directorySize;
            foreach (var table in tables) total += (table.Value.Length + 3) & ~3;

            var file = new byte[total];
            WriteU32(file, 0, 0x00010000);
            WriteU16(file, 4, (ushort)count);
            WriteU16(file, 6, (ushort)searchRange);
            WriteU16(file, 8, (ushort)entrySelector);
            WriteU16(file, 10, (ushort)(16 * count - searchRange));

            var offset = directorySize;
            var headOffset = -1;
            for (var i = 0; i < count; i++)
            {
                var entry = 12 + 16 * i;
                var data = tables[i].Value;
                for (var c = 0; c < 4; c++) file[entry + c] = (byte)tables[i].Key[c];
                Array.Copy(data, 0, file, offset, data.Length);
                WriteU32(file, entry + 4, Checksum(file, offset, data.Length));
                WriteU32(file, entry + 8, (uint)offset);
                WriteU32(file, entry + 12, (uint)data.Length);
                if (tables[i].Key == "head") headOffset = offset;
                offset += (data.Length + 3) & ~3;
            }

            if (headOffset >= 0)
            {
                var sum = Checksum(file, 0, file.Length);
                WriteU32(file, headOffset + 8, 0xB1B0AFBA - sum);
            }
            return file;
        }

        private static int Log2(int value)
        {
            var result = 0;
            while (value > 1) { value >>= 1; result++; }
            return result;
        }

        private static uint Checksum(byte[] data, int offset, int length)
        {
            uint sum = 0;
            var end = offset + ((length + 3) & ~3);
            for (var i = offset; i < end; i += 4)
            {
                uint word = 0;
                for (var c = 0; c < 4; c++)
                {
                    word <<= 8;
                    if (i + c < data.Length) word |= data[i + c];
                }
                sum += word;
            }
            return sum;
        }

        private byte[] Slice(string tag)
        {
            var table = _tables[tag];
            var copy = new byte[table[1]];
            Array.Copy(Bytes, table[0], copy, 0, table[1]);
            return copy;
        }

        // ============================================================ readers

        private static string Tag(byte[] data, int offset)
        {
            return new string(new[]
            {
                (char)data[offset], (char)data[offset + 1],
                (char)data[offset + 2], (char)data[offset + 3]
            });
        }

        private static int U16(byte[] data, int offset)
        {
            return (data[offset] << 8) | data[offset + 1];
        }

        private static int S16(byte[] data, int offset)
        {
            return (short)((data[offset] << 8) | data[offset + 1]);
        }

        private static uint U32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        private static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)(value >> 8);
            data[offset + 1] = (byte)value;
        }

        private static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}

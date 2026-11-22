using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Marabook.Print
{
    // UN LECTEUR DE PDF, PETIT (21/11/2026) : juste ce qu'il faut au contrôle
    // PDF/X pour lire un fichier comme un outil de l'imprimeur le lirait — ses
    // objets, ses dictionnaires, ses flux décompressés, les opérateurs de ses
    // pages — au lieu d'y chercher des chaînes. Il ne dessine rien, n'extrait
    // aucun texte, ne suit pas la table des références : il relève chaque
    // « n g obj » du fichier, le dernier l'emportant (mises à jour ajoutées).
    // Ce qu'il ne sait pas lire (flux d'objets, filtres rares), il le DIT.

    public sealed class PdfName
    {
        public readonly string Value;
        public PdfName(string value) { Value = value; }
        public override string ToString() { return "/" + Value; }
    }

    public sealed class PdfRef
    {
        public readonly int Number;
        public PdfRef(int number) { Number = number; }
    }

    public sealed class PdfText
    {
        public readonly byte[] Bytes;
        public PdfText(byte[] bytes) { Bytes = bytes; }
        /// <summary>Le texte : UTF-16 s'il s'ouvre par FE FF, sinon octet pour caractère.</summary>
        public string Value
        {
            get
            {
                if (Bytes.Length >= 2 && Bytes[0] == 0xFE && Bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(Bytes, 2, Bytes.Length - 2);
                return Encoding.GetEncoding(28591).GetString(Bytes);
            }
        }
    }

    public sealed class PdfKeyword
    {
        public readonly string Value;
        public PdfKeyword(string value) { Value = value; }
    }

    public sealed class PdfStream
    {
        public Dictionary<string, object> Dict;
        public int Start, Length;
    }

    /// <summary>Lit des objets PDF dans un tampon d'octets.</summary>
    public sealed class PdfLexer
    {
        private readonly byte[] _data;
        public int Position;
        private readonly bool _references; // « 12 0 R » : dans le fichier, jamais dans un flux de page

        public PdfLexer(byte[] data, int position, bool references)
        {
            _data = data;
            Position = position;
            _references = references;
        }

        public bool AtEnd { get { SkipBlank(); return Position >= _data.Length; } }

        private static bool IsBlank(byte c) { return c == 0 || c == 9 || c == 10 || c == 12 || c == 13 || c == 32; }
        private static bool IsDelimiter(byte c) { return c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']' || c == '{' || c == '}' || c == '/' || c == '%'; }

        public void SkipBlank()
        {
            while (Position < _data.Length)
            {
                var c = _data[Position];
                if (IsBlank(c)) { Position++; continue; }
                if (c == '%') { while (Position < _data.Length && _data[Position] != 10 && _data[Position] != 13) Position++; continue; }
                break;
            }
        }

        /// <summary>L'objet suivant ; un PdfKeyword pour un opérateur ou un
        /// mot (obj, stream, R…) ; null en fin de tampon.</summary>
        public object Read()
        {
            SkipBlank();
            if (Position >= _data.Length) return null;
            var c = _data[Position];
            if (c == '/') return ReadName();
            if (c == '(') return ReadLiteral();
            if (c == '[')
            {
                Position++;
                var list = new List<object>();
                while (true)
                {
                    SkipBlank();
                    if (Position >= _data.Length) break;
                    if (_data[Position] == ']') { Position++; break; }
                    var before = Position;
                    var item = Read();
                    if (Position == before) { Position++; continue; } // un octet illisible : on avance
                    list.Add(item);
                }
                return list;
            }
            if (c == '<')
            {
                if (Position + 1 < _data.Length && _data[Position + 1] == '<') return ReadDictionary();
                return ReadHex();
            }
            if (c == '>' || c == ']' || c == ')' || c == '{' || c == '}') { Position++; return new PdfKeyword(((char)c).ToString()); }
            if ((c >= '0' && c <= '9') || c == '+' || c == '-' || c == '.') return ReadNumber();
            var start = Position;
            while (Position < _data.Length && !IsBlank(_data[Position]) && !IsDelimiter(_data[Position])) Position++;
            if (Position == start) { Position++; return new PdfKeyword("?"); }
            var word = Encoding.ASCII.GetString(_data, start, Position - start);
            if (word == "true") return true;
            if (word == "false") return false;
            if (word == "null") return null;
            return new PdfKeyword(word);
        }

        private PdfName ReadName()
        {
            Position++;
            var sb = new StringBuilder();
            while (Position < _data.Length && !IsBlank(_data[Position]) && !IsDelimiter(_data[Position]))
            {
                var c = _data[Position];
                if (c == '#' && Position + 2 < _data.Length && Uri.IsHexDigit((char)_data[Position + 1]) && Uri.IsHexDigit((char)_data[Position + 2]))
                {
                    sb.Append((char)Convert.ToInt32(Encoding.ASCII.GetString(_data, Position + 1, 2), 16));
                    Position += 3;
                    continue;
                }
                sb.Append((char)c);
                Position++;
            }
            return new PdfName(sb.ToString());
        }

        private PdfText ReadLiteral()
        {
            Position++;
            var bytes = new List<byte>();
            var depth = 1;
            while (Position < _data.Length)
            {
                var c = _data[Position++];
                if (c == '\\' && Position < _data.Length)
                {
                    var e = _data[Position++];
                    switch (e)
                    {
                        case (byte)'n': bytes.Add(10); break;
                        case (byte)'r': bytes.Add(13); break;
                        case (byte)'t': bytes.Add(9); break;
                        case (byte)'b': bytes.Add(8); break;
                        case (byte)'f': bytes.Add(12); break;
                        case 13: if (Position < _data.Length && _data[Position] == 10) Position++; break;
                        case 10: break;
                        default:
                            if (e >= '0' && e <= '7')
                            {
                                var value = e - '0';
                                for (var i = 0; i < 2 && Position < _data.Length && _data[Position] >= '0' && _data[Position] <= '7'; i++) value = value * 8 + (_data[Position++] - '0');
                                bytes.Add((byte)value);
                            }
                            else bytes.Add(e);
                            break;
                    }
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth == 0) break; }
                bytes.Add(c);
            }
            return new PdfText(bytes.ToArray());
        }

        private PdfText ReadHex()
        {
            Position++;
            var bytes = new List<byte>();
            var high = -1;
            while (Position < _data.Length)
            {
                var c = (char)_data[Position++];
                if (c == '>') break;
                if (!Uri.IsHexDigit(c)) continue;
                var digit = Convert.ToInt32(c.ToString(), 16);
                if (high < 0) high = digit;
                else { bytes.Add((byte)(high * 16 + digit)); high = -1; }
            }
            if (high >= 0) bytes.Add((byte)(high * 16));
            return new PdfText(bytes.ToArray());
        }

        private Dictionary<string, object> ReadDictionary()
        {
            Position += 2;
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            while (true)
            {
                SkipBlank();
                if (Position >= _data.Length) break;
                if (_data[Position] == '>') { Position += Position + 1 < _data.Length && _data[Position + 1] == '>' ? 2 : 1; break; }
                var before = Position;
                var key = Read() as PdfName;
                if (key == null) { if (Position == before) Position++; continue; }
                dict[key.Value] = Read();
            }
            return dict;
        }

        private object ReadNumber()
        {
            var start = Position;
            var number = ReadRawNumber();
            if (!_references || number != Math.Floor(number) || number < 0) return number;
            // « n g R » ?
            var after = Position;
            SkipBlank();
            if (Position < _data.Length && _data[Position] >= '0' && _data[Position] <= '9')
            {
                var generation = ReadRawNumber();
                SkipBlank();
                if (generation == Math.Floor(generation) && Position < _data.Length && _data[Position] == 'R'
                    && (Position + 1 >= _data.Length || IsBlank(_data[Position + 1]) || IsDelimiter(_data[Position + 1])))
                {
                    Position++;
                    return new PdfRef((int)number);
                }
            }
            Position = after;
            return number;
        }

        private double ReadRawNumber()
        {
            var start = Position;
            if (Position < _data.Length && (_data[Position] == '+' || _data[Position] == '-')) Position++;
            while (Position < _data.Length && ((_data[Position] >= '0' && _data[Position] <= '9') || _data[Position] == '.')) Position++;
            double value;
            var text = Encoding.ASCII.GetString(_data, start, Position - start);
            if (Position == start) { Position++; return 0; }
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ? value : 0;
        }
    }

    /// <summary>Le fichier lu : ses objets par numéro, son dictionnaire de fin.</summary>
    public sealed class PdfLite
    {
        public readonly byte[] Data;
        public readonly Dictionary<int, object> Objects = new Dictionary<int, object>();
        public Dictionary<string, object> Trailer = new Dictionary<string, object>(StringComparer.Ordinal);
        public string Version = "";
        public bool HasObjectStreams, HasXrefStreams;
        /// <summary>Vrai quand TOUS les flux d'objets (PDF 1.5) ont pu être ouverts : leurs
        /// objets sont alors dans Objects comme les autres (21/11 — presque tous les PDF
        /// d'aujourd'hui rangent ainsi leurs pages). Faux s'il y en a un d'illisible, ou si
        /// le fichier est chiffré (son contenu ne se lit pas sans sa clé).</summary>
        public bool ObjectStreamsRead;

        private PdfLite(byte[] data) { Data = data; }

        private static int IndexOf(byte[] data, byte[] pattern, int from, int to)
        {
            var last = Math.Min(to, data.Length) - pattern.Length;
            for (var i = Math.Max(0, from); i <= last; i++)
            {
                var j = 0;
                while (j < pattern.Length && data[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }

        private static readonly byte[] ObjWord = Encoding.ASCII.GetBytes(" obj");
        private static readonly byte[] EndStream = Encoding.ASCII.GetBytes("endstream");
        private static readonly byte[] TrailerWord = Encoding.ASCII.GetBytes("trailer");

        /// <summary>Lit le fichier ; null si ce n'est pas un PDF.</summary>
        public static PdfLite Read(byte[] data)
        {
            if (data == null || data.Length < 16) return null;
            var head = Encoding.ASCII.GetString(data, 0, Math.Min(1024, data.Length));
            var at = head.IndexOf("%PDF-", StringComparison.Ordinal);
            if (at < 0) return null;
            var pdf = new PdfLite(data);
            var end = at + 5;
            while (end < head.Length && (char.IsDigit(head[end]) || head[end] == '.')) end++;
            pdf.Version = head.Substring(at + 5, end - at - 5);

            var position = 0;
            while (true)
            {
                var found = IndexOf(data, ObjWord, position, data.Length);
                if (found < 0) break;
                position = found + ObjWord.Length;
                // À rebours : « <numéro> <génération> obj », en début de mot.
                var p = found;
                var generationEnd = p;
                while (p > 0 && data[p - 1] >= '0' && data[p - 1] <= '9') p--;
                if (p == generationEnd || p == 0 || data[p - 1] != ' ') continue;
                var q = p - 1;
                var numberEnd = q;
                while (q > 0 && data[q - 1] >= '0' && data[q - 1] <= '9') q--;
                if (q == numberEnd || (q > 0 && data[q - 1] != 10 && data[q - 1] != 13 && data[q - 1] != 32)) continue;
                int number;
                if (!int.TryParse(Encoding.ASCII.GetString(data, q, numberEnd - q), out number)) continue;
                var lexer = new PdfLexer(data, position, true);
                object value;
                try { value = lexer.Read(); }
                catch (Exception) { continue; }
                var dict = value as Dictionary<string, object>;
                if (dict != null)
                {
                    var mark = lexer.Position;
                    var next = lexer.Read() as PdfKeyword;
                    if (next != null && next.Value == "stream")
                    {
                        var start = lexer.Position;
                        if (start < data.Length && data[start] == 13) start++;
                        if (start < data.Length && data[start] == 10) start++;
                        var length = -1;
                        object declared;
                        if (dict.TryGetValue("Length", out declared) && declared is double)
                        {
                            var candidate = (int)(double)declared;
                            if (candidate >= 0 && start + candidate <= data.Length && IndexOf(data, EndStream, start + candidate, Math.Min(data.Length, start + candidate + 16)) >= 0) length = candidate;
                        }
                        if (length < 0)
                        {
                            var close = IndexOf(data, EndStream, start, data.Length);
                            length = close < 0 ? data.Length - start : close - start;
                            while (length > 0 && (data[start + length - 1] == 10 || data[start + length - 1] == 13)) length--;
                        }
                        value = new PdfStream { Dict = dict, Start = start, Length = length };
                        position = start + length; // rien de ce que contient le flux n'est pris pour un objet
                        var type = dict.ContainsKey("Type") ? dict["Type"] as PdfName : null;
                        if (type != null && type.Value == "ObjStm") pdf.HasObjectStreams = true;
                        if (type != null && type.Value == "XRef") { pdf.HasXrefStreams = true; foreach (var pair in dict) pdf.Trailer[pair.Key] = pair.Value; }
                    }
                    else position = mark;
                }
                else position = lexer.Position;
                pdf.Objects[number] = value;
            }

            // Les dictionnaires de fin : le dernier l'emporte, clé par clé.
            var from = 0;
            while (true)
            {
                var found = IndexOf(data, TrailerWord, from, data.Length);
                if (found < 0) break;
                from = found + TrailerWord.Length;
                try
                {
                    var dict = new PdfLexer(data, from, true).Read() as Dictionary<string, object>;
                    if (dict != null) foreach (var pair in dict) pdf.Trailer[pair.Key] = pair.Value;
                }
                catch (Exception) { }
            }
            pdf.ReadObjectStreams();
            return pdf;
        }

        /// <summary>Ouvre les flux d'objets : « N » objets rangés à la suite, précédés de leur
        /// table (numéro, position). Un objet déjà lu hors d'un flux garde sa valeur.</summary>
        private void ReadObjectStreams()
        {
            if (!HasObjectStreams) return;
            if (Trailer.ContainsKey("Encrypt")) return; // chiffré : rien à tirer des flux
            var streams = new List<PdfStream>();
            foreach (var pair in Objects)
            {
                var stream = pair.Value as PdfStream;
                var type = stream == null || !stream.Dict.ContainsKey("Type") ? null : stream.Dict["Type"] as PdfName;
                if (type != null && type.Value == "ObjStm") streams.Add(stream);
            }
            var all = true;
            foreach (var stream in streams)
            {
                try
                {
                    var bytes = Decode(stream);
                    var count = Get(stream.Dict, "N");
                    var first = Get(stream.Dict, "First");
                    if (bytes == null || !(count is double) || !(first is double)) { all = false; continue; }
                    var n = (int)(double)count;
                    var start = (int)(double)first;
                    if (n < 0 || n > 200000 || start < 0 || start > bytes.Length) { all = false; continue; }
                    var table = new PdfLexer(bytes, 0, false);
                    var numbers = new int[n];
                    var offsets = new int[n];
                    var good = true;
                    for (var i = 0; i < n && good; i++)
                    {
                        var number = table.Read();
                        var offset = table.Read();
                        if (!(number is double) || !(offset is double)) { good = false; break; }
                        numbers[i] = (int)(double)number;
                        offsets[i] = (int)(double)offset;
                    }
                    if (!good) { all = false; continue; }
                    for (var i = 0; i < n; i++)
                    {
                        if (Objects.ContainsKey(numbers[i])) continue;
                        var at = start + offsets[i];
                        if (at < 0 || at >= bytes.Length) continue;
                        try { Objects[numbers[i]] = new PdfLexer(bytes, at, true).Read(); }
                        catch (Exception) { all = false; }
                    }
                }
                catch (Exception) { all = false; }
            }
            ObjectStreamsRead = all;
        }

        /// <summary>Suit une référence jusqu'à l'objet (ou rend la valeur telle quelle).</summary>
        public object Resolve(object value)
        {
            for (var i = 0; i < 32; i++)
            {
                var reference = value as PdfRef;
                if (reference == null) return value;
                if (!Objects.TryGetValue(reference.Number, out value)) return null;
            }
            return null;
        }

        /// <summary>Le dictionnaire d'une valeur : le sien, ou celui de son flux.</summary>
        public Dictionary<string, object> Dict(object value)
        {
            var resolved = Resolve(value);
            var stream = resolved as PdfStream;
            return stream != null ? stream.Dict : resolved as Dictionary<string, object>;
        }

        public object Get(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? Resolve(value) : null;
        }

        public string Name(Dictionary<string, object> dict, string key)
        {
            var name = Get(dict, key) as PdfName;
            return name == null ? null : name.Value;
        }

        /// <summary>Les noms des filtres d'un flux, dans l'ordre.</summary>
        public List<string> Filters(Dictionary<string, object> dict)
        {
            var names = new List<string>();
            var filter = Get(dict, "Filter");
            var one = filter as PdfName;
            if (one != null) names.Add(one.Value);
            var many = filter as List<object>;
            if (many != null) foreach (var item in many) { var name = Resolve(item) as PdfName; if (name != null) names.Add(name.Value); }
            return names;
        }

        /// <summary>Les octets décodés d'un flux ; null si un filtre n'est pas lu ici.</summary>
        public byte[] Decode(PdfStream stream)
        {
            if (stream == null || stream.Start < 0 || stream.Start + stream.Length > Data.Length) return null;
            var bytes = new byte[stream.Length];
            Array.Copy(Data, stream.Start, bytes, 0, stream.Length);
            foreach (var filter in Filters(stream.Dict))
            {
                try
                {
                    if (filter == "FlateDecode" || filter == "Fl")
                    {
                        if (bytes.Length < 2) return null;
                        using (var source = new MemoryStream(bytes, 2, bytes.Length - 2))
                        using (var inflate = new DeflateStream(source, CompressionMode.Decompress))
                        using (var buffer = new MemoryStream())
                        {
                            inflate.CopyTo(buffer);
                            bytes = buffer.ToArray();
                        }
                    }
                    else if (filter == "ASCIIHexDecode" || filter == "AHx")
                    {
                        var output = new List<byte>();
                        var high = -1;
                        foreach (var b in bytes)
                        {
                            var c = (char)b;
                            if (c == '>') break;
                            if (!Uri.IsHexDigit(c)) continue;
                            var digit = Convert.ToInt32(c.ToString(), 16);
                            if (high < 0) high = digit; else { output.Add((byte)(high * 16 + digit)); high = -1; }
                        }
                        bytes = output.ToArray();
                    }
                    else return null;
                }
                catch (Exception) { return null; }
            }
            return bytes;
        }
    }
}

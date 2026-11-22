using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Marabook.Print
{
    /// <summary>Ce que le compacteur a fait d'un PDF.</summary>
    public sealed class PdfCompactReport
    {
        public long Before, After;
        /// <summary>Objets que plus rien ne citait (d'anciennes versions, des restes d'édition).</summary>
        public int Orphans;
        /// <summary>Flux identiques ramenés à un seul (polices ou images répétées).</summary>
        public int Duplicates;
        /// <summary>Flux qui n'étaient pas compressés et le sont.</summary>
        public int Deflated;
        public string Problem;

        public long Saved { get { return Math.Max(0, Before - After); } }
        public int Percent { get { return Before <= 0 ? 0 : (int)Math.Round(Saved * 100.0 / Before); } }
    }

    /// <summary>LE COMPACTEUR DE PDF (2.0.0, 22/11/2026, Rémi — l'Atelier,
    /// PDFaah › Compresser). Sans rien changer à ce que le PDF montre, il le
    /// RÉÉCRIT en ne gardant que ce qui sert :
    ///  - les objets que plus rien ne cite tombent (les versions empilées par
    ///    des enregistrements successifs, les restes d'une édition) ;
    ///  - les flux strictement identiques — une police ou une image répétée,
    ///    fréquent après une fusion — ne sont gardés qu'une fois ;
    ///  - les flux laissés sans compression sont compressés (Flate).
    /// Le texte, les polices, les signets, les formulaires restent tels quels :
    /// c'est une compression SANS PERTE. La réduction des images (avec perte)
    /// est l'affaire de PDFium, avant ce passage — qui ramasse alors les
    /// images d'origine devenues orphelines.
    /// Un PDF verrouillé n'est pas lu (son contenu est chiffré) : il passe
    /// d'abord par l'Unlocker.</summary>
    public static class PdfCompact
    {
        public static byte[] Shrink(byte[] pdf, out PdfCompactReport report)
        {
            report = new PdfCompactReport { Before = pdf == null ? 0 : pdf.LongLength };
            var read = PdfLite.Read(pdf);
            if (read == null) { report.Problem = "Ce n'est pas un PDF."; return null; }
            if (read.Trailer.ContainsKey("Encrypt")) { report.Problem = "Ce PDF est verrouillé : faites-en d'abord une copie sans verrou avec l'Unlocker."; return null; }
            if (read.HasObjectStreams && !read.ObjectStreamsRead) { report.Problem = "La structure de ce PDF ne se lit pas entièrement : il n'a pas été réécrit."; return null; }
            object root;
            if (!read.Trailer.TryGetValue("Root", out root) || !(root is PdfRef) || !read.Objects.ContainsKey(((PdfRef)root).Number)) { report.Problem = "Ce PDF n'a pas de catalogue lisible : il n'a pas été réécrit."; return null; }

            // 1. Ce qui est atteint depuis le catalogue et la fiche d'informations.
            var reached = new HashSet<int>();
            var queue = new Stack<object>();
            queue.Push(root);
            object info;
            if (read.Trailer.TryGetValue("Info", out info)) queue.Push(info);
            while (queue.Count > 0)
            {
                var value = queue.Pop();
                var reference = value as PdfRef;
                if (reference != null)
                {
                    object target;
                    if (!reached.Add(reference.Number) || !read.Objects.TryGetValue(reference.Number, out target)) continue;
                    queue.Push(target);
                    continue;
                }
                var stream = value as PdfStream;
                if (stream != null) { queue.Push(stream.Dict); continue; }
                var dict = value as Dictionary<string, object>;
                if (dict != null) { foreach (var pair in dict) if (pair.Value != null) queue.Push(pair.Value); continue; }
                var list = value as List<object>;
                if (list != null) foreach (var item in list) if (item != null) queue.Push(item);
            }

            var numbers = new List<int>();
            var all = 0;
            foreach (var pair in read.Objects)
            {
                if (pair.Key <= 0) continue;
                var stream = pair.Value as PdfStream;
                var type = stream == null ? null : read.Name(stream.Dict, "Type");
                if (type == "ObjStm" || type == "XRef") continue; // la tuyauterie : refaite
                all++;
                if (reached.Contains(pair.Key)) numbers.Add(pair.Key);
            }
            numbers.Sort();
            report.Orphans = all - numbers.Count;

            // 2. Les flux : leurs octets (compressés s'ils ne l'étaient pas), et les doublons.
            var bodies = new Dictionary<int, byte[]>();
            var dicts = new Dictionary<int, Dictionary<string, object>>();
            var alias = new Dictionary<int, int>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                foreach (var number in numbers)
                {
                    var stream = read.Objects[number] as PdfStream;
                    if (stream == null) continue;
                    if (stream.Start < 0 || stream.Length < 0 || stream.Start + stream.Length > pdf.Length) { report.Problem = "Un flux de ce PDF déborde du fichier : il n'a pas été réécrit."; return null; }
                    var raw = new byte[stream.Length];
                    Array.Copy(pdf, stream.Start, raw, 0, stream.Length);
                    var dict = new Dictionary<string, object>(stream.Dict, StringComparer.Ordinal);
                    var packedHere = false;
                    if (!dict.ContainsKey("Filter") && !dict.ContainsKey("F") && raw.Length > 128)
                    {
                        var packed = Deflate(raw);
                        if (packed.Length < raw.Length * 0.95)
                        {
                            raw = packed;
                            dict["Filter"] = new PdfName("FlateDecode");
                            dict.Remove("DecodeParms");
                            report.Deflated++;
                            packedHere = true;
                        }
                    }
                    dict["Length"] = (double)raw.Length;
                    bodies[number] = raw;
                    dicts[number] = dict;
                    // Un flux identique déjà vu (mêmes octets, même dictionnaire) : celui-ci le cite.
                    var head = new MemoryStream();
                    WriteValue(head, dict, null);
                    var signature = Convert.ToBase64String(sha.ComputeHash(head.ToArray())) + ":" + Convert.ToBase64String(sha.ComputeHash(raw)) + ":" + raw.Length.ToString(CultureInfo.InvariantCulture);
                    int first;
                    if (raw.Length >= 256 && seen.TryGetValue(signature, out first)) { alias[number] = first; report.Duplicates++; if (packedHere) report.Deflated--; } // un doublon ne compte pas deux fois
                    else if (raw.Length >= 256) seen[signature] = number;
                }
            }
            if (alias.Count > 0) numbers.RemoveAll(delegate(int number) { return alias.ContainsKey(number); });

            // 3. La réécriture : numéros resserrés, table classique.
            var renumber = new Dictionary<int, int>();
            for (var i = 0; i < numbers.Count; i++) renumber[numbers[i]] = i + 1;
            foreach (var pair in alias)
            {
                int target;
                if (renumber.TryGetValue(pair.Value, out target)) renumber[pair.Key] = target;
            }
            var output = new MemoryStream();
            var offsets = new long[numbers.Count + 1];
            WriteAscii(output, "%PDF-" + (string.CompareOrdinal(read.Version, "1.4") > 0 && read.Version.Length == 3 ? read.Version : "1.4") + "\n%");
            output.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 5);
            for (var i = 0; i < numbers.Count; i++)
            {
                var number = numbers[i];
                offsets[i + 1] = output.Position;
                WriteAscii(output, (i + 1).ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
                byte[] body;
                if (bodies.TryGetValue(number, out body))
                {
                    WriteValue(output, dicts[number], renumber);
                    WriteAscii(output, "\nstream\r\n");
                    output.Write(body, 0, body.Length);
                    WriteAscii(output, "\r\nendstream");
                }
                else WriteValue(output, read.Objects[number], renumber);
                WriteAscii(output, "\nendobj\n");
            }
            var size = numbers.Count + 1;
            var xref = output.Position;
            WriteAscii(output, "xref\n0 " + size.ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
            for (var i = 1; i < size; i++) WriteAscii(output, offsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
            WriteAscii(output, "trailer\n<< /Size " + size.ToString(CultureInfo.InvariantCulture) + " /Root " + renumber[((PdfRef)root).Number].ToString(CultureInfo.InvariantCulture) + " 0 R");
            int infoNumber;
            if (info is PdfRef && renumber.TryGetValue(((PdfRef)info).Number, out infoNumber)) WriteAscii(output, " /Info " + infoNumber.ToString(CultureInfo.InvariantCulture) + " 0 R");
            var ids = read.Trailer.ContainsKey("ID") ? read.Resolve(read.Trailer["ID"]) as List<object> : null;
            if (ids != null && ids.Count == 2 && read.Resolve(ids[0]) is PdfText && read.Resolve(ids[1]) is PdfText)
            {
                WriteAscii(output, " /ID [");
                WriteHex(output, ((PdfText)read.Resolve(ids[0])).Bytes);
                WriteHex(output, ((PdfText)read.Resolve(ids[1])).Bytes);
                WriteAscii(output, "]");
            }
            WriteAscii(output, " >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
            var bytes = output.ToArray();
            report.After = bytes.LongLength;
            return bytes;
        }

        private static byte[] Deflate(byte[] raw)
        {
            using (var buffer = new MemoryStream())
            {
                using (var zlib = new ZLibStream(buffer, CompressionLevel.SmallestSize, true)) zlib.Write(raw, 0, raw.Length);
                return buffer.ToArray();
            }
        }

        // ------------------------------------------------------------ écriture des objets (en clair)

        private static void WriteAscii(Stream output, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        private static void WriteHex(Stream output, byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2 + 2);
            sb.Append('<');
            foreach (var b in bytes) sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            sb.Append('>');
            WriteAscii(output, sb.ToString());
        }

        /// <summary>Une chaîne : entre parenthèses si elle est lisible, sinon en hexadécimal — au plus court.</summary>
        private static void WriteText(Stream output, byte[] bytes)
        {
            var plain = true;
            foreach (var b in bytes) if (b < 32 || b > 126) { plain = false; break; }
            if (!plain) { WriteHex(output, bytes); return; }
            var sb = new StringBuilder(bytes.Length + 2);
            sb.Append('(');
            foreach (var b in bytes)
            {
                var c = (char)b;
                if (c == '(' || c == ')' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            sb.Append(')');
            WriteAscii(output, sb.ToString());
        }

        internal static string NameText(string name)
        {
            var sb = new StringBuilder("/");
            foreach (var c in name)
            {
                if (c < 33 || c > 126 || c == '#' || c == '(' || c == ')' || c == '<' || c == '>' || c == '[' || c == ']' || c == '{' || c == '}' || c == '/' || c == '%')
                    sb.Append('#').Append(((int)c & 0xFF).ToString("X2", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        internal static string NumberText(double value)
        {
            if (value == Math.Floor(value) && Math.Abs(value) < 1e15) return ((long)value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        /// <summary>renumber : les nouveaux numéros (null = ceux d'origine). Une référence vers un objet
        /// qui n'existe plus devient « null », comme le veut la norme.</summary>
        private static void WriteValue(Stream output, object value, Dictionary<int, int> renumber)
        {
            if (value == null) { WriteAscii(output, "null"); return; }
            if (value is bool) { WriteAscii(output, (bool)value ? "true" : "false"); return; }
            if (value is double) { WriteAscii(output, NumberText((double)value)); return; }
            var name = value as PdfName;
            if (name != null) { WriteAscii(output, NameText(name.Value)); return; }
            var reference = value as PdfRef;
            if (reference != null)
            {
                var number = reference.Number;
                if (renumber != null && !renumber.TryGetValue(reference.Number, out number)) { WriteAscii(output, "null"); return; }
                WriteAscii(output, number.ToString(CultureInfo.InvariantCulture) + " 0 R");
                return;
            }
            var text = value as PdfText;
            if (text != null) { WriteText(output, text.Bytes); return; }
            var list = value as List<object>;
            if (list != null)
            {
                WriteAscii(output, "[");
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) WriteAscii(output, " ");
                    WriteValue(output, list[i], renumber);
                }
                WriteAscii(output, "]");
                return;
            }
            var dict = value as Dictionary<string, object>;
            if (dict != null)
            {
                WriteAscii(output, "<<");
                foreach (var pair in dict)
                {
                    WriteAscii(output, " " + NameText(pair.Key) + " ");
                    WriteValue(output, pair.Value, renumber);
                }
                WriteAscii(output, " >>");
                return;
            }
            WriteAscii(output, "null"); // un mot égaré : rien à en tirer
        }
    }
}

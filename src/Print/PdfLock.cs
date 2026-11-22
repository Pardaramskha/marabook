using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Marabook.Print
{
    /// <summary>VERROUILLER UN PDF PAR MOT DE PASSE (2.0.0, 22/11/2026, Rémi —
    /// l'Atelier, PDFaah). PDFium sait rouvrir un PDF verrouillé, pas en
    /// verrouiller un : c'est donc Marabook qui chiffre, ici, sans rien
    /// installer.
    ///
    /// Le fichier est relu objet par objet (PdfLite), puis RÉÉCRIT en entier :
    /// chaque chaîne et chaque flux sont chiffrés en AES-128 (le « filtre de
    /// chiffrement » AESV2 de PDF 1.6 — celui que lisent Acrobat, les
    /// navigateurs, Aperçu et PDFium), avec une clé tirée du mot de passe
    /// selon la norme (ISO 32000-1, § 7.6.3 : algorithmes 2, 3 et 5). Les
    /// objets rangés dans des flux d'objets sont ressortis à plat, les tables
    /// de références refaites. Le fichier d'origine n'est pas touché.
    ///
    /// Ce que c'est : un mot de passe D'OUVERTURE — sans lui, le fichier ne
    /// s'ouvre pas. Ce que ce n'est pas : une protection contre qui a le mot
    /// de passe, ni une signature. AES-128 est le plus lu des chiffrements
    /// PDF ; il protège d'un regard, pas d'un adversaire outillé face à un mot
    /// de passe court — la robustesse est celle du mot de passe.</summary>
    public static class PdfLock
    {
        /// <summary>Tout est permis une fois le fichier ouvert (impression, copie, modification).</summary>
        public const int AllowAll = -4;

        private static readonly byte[] Padding =
        {
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
        };

        /// <summary>Le fichier porte-t-il déjà un verrou ?</summary>
        public static bool IsLocked(byte[] pdf)
        {
            var read = PdfLite.Read(pdf);
            return read != null && read.Trailer.ContainsKey("Encrypt");
        }

        /// <summary>Ce mot de passe peut-il servir ? null si oui, sinon pourquoi pas.</summary>
        public static string PasswordProblem(string password)
        {
            if (string.IsNullOrEmpty(password)) return "Un mot de passe est nécessaire.";
            if (password.Length > 32) return "Le mot de passe ne peut pas dépasser 32 caractères : c'est la limite de ce chiffrement.";
            foreach (var c in password)
                if (c > 255 || c < 32) return "Le caractère « " + c + " » ne peut pas servir dans ce mot de passe : gardez les lettres (accentuées ou non), les chiffres et la ponctuation courante.";
            return null;
        }

        private static byte[] Pad(string password)
        {
            var raw = new byte[Math.Min(32, (password ?? "").Length)];
            for (var i = 0; i < raw.Length; i++) raw[i] = (byte)password[i]; // PDFDocEncoding : Latin-1 pour ces caractères
            var padded = new byte[32];
            Array.Copy(raw, padded, raw.Length);
            Array.Copy(Padding, 0, padded, raw.Length, 32 - raw.Length);
            return padded;
        }

        private static byte[] Rc4(byte[] key, byte[] data)
        {
            var s = new byte[256];
            for (var i = 0; i < 256; i++) s[i] = (byte)i;
            var j = 0;
            for (var i = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 0xFF;
                var t = s[i]; s[i] = s[j]; s[j] = t;
            }
            var output = new byte[data.Length];
            int a = 0, b = 0;
            for (var n = 0; n < data.Length; n++)
            {
                a = (a + 1) & 0xFF;
                b = (b + s[a]) & 0xFF;
                var t = s[a]; s[a] = s[b]; s[b] = t;
                output[n] = (byte)(data[n] ^ s[(s[a] + s[b]) & 0xFF]);
            }
            return output;
        }

        private static byte[] Md5(params byte[][] parts)
        {
            using (var md5 = MD5.Create())
            using (var buffer = new MemoryStream())
            {
                foreach (var part in parts) buffer.Write(part, 0, part.Length);
                return md5.ComputeHash(buffer.ToArray());
            }
        }

        private static byte[] Xor(byte[] key, int value)
        {
            var result = new byte[key.Length];
            for (var i = 0; i < key.Length; i++) result[i] = (byte)(key[i] ^ value);
            return result;
        }

        /// <summary>Algorithme 3 : l'entrée /O (le mot de passe du propriétaire, ici le même).</summary>
        private static byte[] OwnerEntry(string owner, string user)
        {
            var key = Md5(Pad(owner));
            for (var i = 0; i < 50; i++) key = Md5(key);
            var value = Rc4(key, Pad(user));
            for (var i = 1; i <= 19; i++) value = Rc4(Xor(key, i), value);
            return value;
        }

        /// <summary>Algorithme 2 : la clé du fichier (16 octets).</summary>
        private static byte[] FileKey(string user, byte[] ownerEntry, int permissions, byte[] id)
        {
            var p = new[] { (byte)permissions, (byte)(permissions >> 8), (byte)(permissions >> 16), (byte)(permissions >> 24) };
            var key = Md5(Pad(user), ownerEntry, p, id);
            for (var i = 0; i < 50; i++) key = Md5(key);
            return key;
        }

        /// <summary>Algorithme 5 : l'entrée /U (ce à quoi un lecteur reconnaît le bon mot de passe).</summary>
        private static byte[] UserEntry(byte[] key, byte[] id)
        {
            var value = Rc4(key, Md5(Padding, id));
            for (var i = 1; i <= 19; i++) value = Rc4(Xor(key, i), value);
            var entry = new byte[32];
            Array.Copy(value, entry, 16);
            return entry;
        }

        private static readonly byte[] Salt = { 0x73, 0x41, 0x6C, 0x54 }; // « sAlT »

        private static byte[] ObjectKey(byte[] key, int number)
        {
            return Md5(key, new[] { (byte)number, (byte)(number >> 8), (byte)(number >> 16), (byte)0, (byte)0 }, Salt);
        }

        private static byte[] Aes(byte[] key, byte[] data, RandomNumberGenerator random)
        {
            var iv = new byte[16];
            random.GetBytes(iv);
            using (var aes = System.Security.Cryptography.Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;
                using (var encryptor = aes.CreateEncryptor())
                {
                    var body = encryptor.TransformFinalBlock(data, 0, data.Length);
                    var output = new byte[16 + body.Length];
                    Array.Copy(iv, output, 16);
                    Array.Copy(body, 0, output, 16, body.Length);
                    return output;
                }
            }
        }

        /// <summary>Le mot de passe ouvre-t-il ce fichier verrouillé par Encrypt (ou par un autre outil,
        /// au même chiffrement) ? Faux si le fichier n'est pas verrouillé ainsi.</summary>
        public static bool Opens(byte[] pdf, string password)
        {
            var read = PdfLite.Read(pdf);
            if (read == null) return false;
            var encrypt = read.Dict(read.Trailer.ContainsKey("Encrypt") ? read.Trailer["Encrypt"] : null);
            if (encrypt == null) return false;
            var o = read.Get(encrypt, "O") as PdfText;
            var u = read.Get(encrypt, "U") as PdfText;
            var p = read.Get(encrypt, "P");
            var r = read.Get(encrypt, "R");
            var ids = read.Trailer.ContainsKey("ID") ? read.Resolve(read.Trailer["ID"]) as List<object> : null;
            var id = ids != null && ids.Count > 0 ? read.Resolve(ids[0]) as PdfText : null;
            if (o == null || u == null || !(p is double) || !(r is double) || (int)(double)r != 4 || o.Bytes.Length < 32 || u.Bytes.Length < 16) return false;
            var key = FileKey(password ?? "", o.Bytes, (int)(long)(double)p, id == null ? new byte[0] : id.Bytes);
            var expected = UserEntry(key, id == null ? new byte[0] : id.Bytes);
            for (var i = 0; i < 16; i++) if (expected[i] != u.Bytes[i]) return false;
            return true;
        }

        /// <summary>La copie verrouillée de ce PDF : elle ne s'ouvre qu'avec « password ».
        /// null (et « problem » dit pourquoi) si le fichier ne peut pas être verrouillé ici.</summary>
        public static byte[] Encrypt(byte[] pdf, string password, out string problem)
        {
            problem = PasswordProblem(password);
            if (problem != null) return null;
            var read = PdfLite.Read(pdf);
            if (read == null) { problem = "Ce n'est pas un PDF."; return null; }
            if (read.Trailer.ContainsKey("Encrypt")) { problem = "Ce PDF est déjà verrouillé. Pour changer son mot de passe, faites-en d'abord une copie sans verrou avec l'Unlocker."; return null; }
            if (read.HasObjectStreams && !read.ObjectStreamsRead) { problem = "La structure de ce PDF ne se lit pas entièrement : il n'a pas été verrouillé. Passez-le d'abord par Chop-Chop (« Enregistrer sous… »), puis réessayez."; return null; }
            object root;
            if (!read.Trailer.TryGetValue("Root", out root) || !(root is PdfRef) || !read.Objects.ContainsKey(((PdfRef)root).Number)) { problem = "Ce PDF n'a pas de catalogue lisible : il n'a pas été verrouillé."; return null; }

            using (var random = RandomNumberGenerator.Create())
            {
                // L'identifiant du fichier : celui qu'il porte, sinon un neuf.
                byte[] id = null;
                var ids = read.Trailer.ContainsKey("ID") ? read.Resolve(read.Trailer["ID"]) as List<object> : null;
                var first = ids != null && ids.Count > 0 ? read.Resolve(ids[0]) as PdfText : null;
                if (first != null && first.Bytes.Length > 0) id = first.Bytes;
                if (id == null) { id = new byte[16]; random.GetBytes(id); }
                var second = new byte[16];
                random.GetBytes(second);

                var ownerEntry = OwnerEntry(password, password);
                var key = FileKey(password, ownerEntry, AllowAll, id);
                var userEntry = UserEntry(key, id);
                var filler = new byte[16];
                random.GetBytes(filler);
                Array.Copy(filler, 0, userEntry, 16, 16);

                var numbers = new List<int>();
                foreach (var pair in read.Objects)
                {
                    if (pair.Key <= 0) continue;
                    var stream = pair.Value as PdfStream;
                    var type = stream == null ? null : read.Name(stream.Dict, "Type");
                    if (type == "ObjStm" || type == "XRef") continue; // leurs objets sont ressortis à plat ; la table est refaite
                    numbers.Add(pair.Key);
                }
                numbers.Sort();
                var encryptNumber = (numbers.Count == 0 ? 0 : numbers[numbers.Count - 1]) + 1;

                var output = new MemoryStream();
                var offsets = new Dictionary<int, long>();
                WriteAscii(output, "%PDF-1.6\n%");
                output.Write(new byte[] { 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 5);
                foreach (var number in numbers)
                {
                    offsets[number] = output.Position;
                    WriteAscii(output, number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
                    var objectKey = ObjectKey(key, number);
                    var value = read.Objects[number];
                    var stream = value as PdfStream;
                    if (stream != null)
                    {
                        if (stream.Start < 0 || stream.Length < 0 || stream.Start + stream.Length > pdf.Length) { problem = "Un flux de ce PDF déborde du fichier : il n'a pas été verrouillé."; return null; }
                        var raw = new byte[stream.Length];
                        Array.Copy(pdf, stream.Start, raw, 0, stream.Length);
                        var sealedBytes = Aes(objectKey, raw, random);
                        var dict = new Dictionary<string, object>(stream.Dict, StringComparer.Ordinal);
                        dict["Length"] = (double)sealedBytes.Length;
                        WriteValue(output, dict, objectKey, random);
                        WriteAscii(output, "\nstream\r\n");
                        output.Write(sealedBytes, 0, sealedBytes.Length);
                        WriteAscii(output, "\r\nendstream");
                    }
                    else WriteValue(output, value, objectKey, random);
                    WriteAscii(output, "\nendobj\n");
                }

                // Le dictionnaire du verrou : lui n'est jamais chiffré.
                offsets[encryptNumber] = output.Position;
                WriteAscii(output, encryptNumber.ToString(CultureInfo.InvariantCulture) + " 0 obj\n<< /Filter /Standard /V 4 /R 4 /Length 128 /CF << /StdCF << /Type /CryptFilter /CFM /AESV2 /AuthEvent /DocOpen /Length 16 >> >> /StmF /StdCF /StrF /StdCF /O ");
                WriteHex(output, ownerEntry);
                WriteAscii(output, " /U ");
                WriteHex(output, userEntry);
                WriteAscii(output, " /P " + AllowAll.ToString(CultureInfo.InvariantCulture) + " /EncryptMetadata true >>\nendobj\n");

                var size = encryptNumber + 1;
                var xref = output.Position;
                WriteAscii(output, "xref\n0 " + size.ToString(CultureInfo.InvariantCulture) + "\n0000000000 65535 f \n");
                for (var number = 1; number < size; number++)
                {
                    long offset;
                    if (offsets.TryGetValue(number, out offset)) WriteAscii(output, offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
                    else WriteAscii(output, "0000000000 65535 f \n");
                }
                WriteAscii(output, "trailer\n<< /Size " + size.ToString(CultureInfo.InvariantCulture) + " /Root " + ((PdfRef)root).Number.ToString(CultureInfo.InvariantCulture) + " 0 R");
                object info;
                if (read.Trailer.TryGetValue("Info", out info) && info is PdfRef && offsets.ContainsKey(((PdfRef)info).Number))
                    WriteAscii(output, " /Info " + ((PdfRef)info).Number.ToString(CultureInfo.InvariantCulture) + " 0 R");
                WriteAscii(output, " /Encrypt " + encryptNumber.ToString(CultureInfo.InvariantCulture) + " 0 R /ID [");
                WriteHex(output, id);
                WriteHex(output, second);
                WriteAscii(output, "] >>\nstartxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
                return output.ToArray();
            }
        }

        // ------------------------------------------------------------ écriture des objets

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

        private static string NameText(string name)
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

        private static string NumberText(double value)
        {
            if (value == Math.Floor(value) && Math.Abs(value) < 1e15) return ((long)value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        /// <summary>Écrit une valeur ; ses chaînes sont chiffrées avec la clé de l'objet qui les porte.</summary>
        private static void WriteValue(Stream output, object value, byte[] objectKey, RandomNumberGenerator random)
        {
            if (value == null) { WriteAscii(output, "null"); return; }
            if (value is bool) { WriteAscii(output, (bool)value ? "true" : "false"); return; }
            if (value is double) { WriteAscii(output, NumberText((double)value)); return; }
            var name = value as PdfName;
            if (name != null) { WriteAscii(output, NameText(name.Value)); return; }
            var reference = value as PdfRef;
            if (reference != null) { WriteAscii(output, reference.Number.ToString(CultureInfo.InvariantCulture) + " 0 R"); return; }
            var text = value as PdfText;
            if (text != null) { WriteHex(output, Aes(objectKey, text.Bytes, random)); return; }
            var list = value as List<object>;
            if (list != null)
            {
                WriteAscii(output, "[");
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) WriteAscii(output, " ");
                    WriteValue(output, list[i], objectKey, random);
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
                    WriteValue(output, pair.Value, objectKey, random);
                }
                WriteAscii(output, " >>");
                return;
            }
            var keyword = value as PdfKeyword;
            WriteAscii(output, keyword != null ? "null" : "null"); // un mot égaré : rien à en tirer
        }
    }
}

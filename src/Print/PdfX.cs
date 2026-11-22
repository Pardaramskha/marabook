using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Marabook.Print
{
    /// <summary>Un profil ICC trouvé sur la machine.</summary>
    public class IccProfile
    {
        public string Path = "";
        public string Description = "";
        public byte[] Bytes;
    }

    /// <summary>LE PROFIL DE LA CONDITION D'IMPRESSION. Le BAT de Marabook
    /// vise « Coated FOGRA39 » (ISO 12647-2:2004). Depuis le 21/11/2026 son
    /// profil ICC est LIVRÉ AVEC L'APPLICATION : « ISO Coated v2 » de
    /// basICColor, bâti sur les mêmes mesures FOGRA39L, sous licence
    /// zlib/libpng (redistribution libre, notice gardée). Les profils d'Adobe
    /// et de l'ECI ne se redistribuent pas dans un logiciel libre. Si le
    /// profil livré manquait, on prend celui de la machine ; à défaut, la
    /// condition n'est déclarée que par son nom enregistré, ce que PDF/X-1a
    /// admet pour FOGRA39.</summary>
    public static class IccProfiles
    {
        /// <summary>Vrai si ces octets sont un profil d'IMPRIMANTE en CMJN
        /// (le seul qu'une condition d'impression CMJN accepte).</summary>
        public static bool IsCmykPrinterProfile(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 132) return false;
            return Tag(bytes, 36) == "acsp" && Tag(bytes, 12) == "prtr" && Tag(bytes, 16) == "CMYK";
        }

        private static string Tag(byte[] bytes, int offset)
        {
            return offset + 4 <= bytes.Length ? Encoding.ASCII.GetString(bytes, offset, 4) : "";
        }

        private static int U32(byte[] b, int o)
        {
            return o + 4 <= b.Length ? (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3] : 0;
        }

        /// <summary>La description du profil (balise « desc », en ASCII ou en
        /// texte multilingue), ou "".</summary>
        public static string DescriptionOf(byte[] bytes)
        {
            try
            {
                if (bytes == null || bytes.Length < 132) return "";
                var count = U32(bytes, 128);
                for (var i = 0; i < count && i < 200; i++)
                {
                    var entry = 132 + 12 * i;
                    if (Tag(bytes, entry) != "desc") continue;
                    var offset = U32(bytes, entry + 4);
                    var size = U32(bytes, entry + 8);
                    if (offset <= 0 || offset + size > bytes.Length || size < 12) return "";
                    var type = Tag(bytes, offset);
                    if (type == "desc")
                    {
                        var length = Math.Min(U32(bytes, offset + 8), size - 12);
                        return length <= 0 ? "" : Encoding.ASCII.GetString(bytes, offset + 12, length).TrimEnd('\0', ' ');
                    }
                    if (type == "mluc" && U32(bytes, offset + 8) > 0)
                    {
                        var length = U32(bytes, offset + 20);
                        var at = offset + U32(bytes, offset + 24);
                        if (length > 0 && at + length <= bytes.Length) return Encoding.BigEndianUnicode.GetString(bytes, at, length).TrimEnd('\0', ' ');
                    }
                    return "";
                }
            }
            catch { }
            return "";
        }

        /// <summary>Les dossiers où les systèmes rangent leurs profils.</summary>
        public static List<string> SystemFolders()
        {
            var folders = new List<string>();
            Action<string> add = delegate(string path) { if (!string.IsNullOrEmpty(path) && !folders.Contains(path)) folders.Add(path); };
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (!string.IsNullOrEmpty(system)) add(System.IO.Path.Combine(system, "spool", "drivers", "color"));
                foreach (var common in new[] { Environment.GetEnvironmentVariable("CommonProgramFiles(x86)"), Environment.GetEnvironmentVariable("CommonProgramFiles") })
                    if (!string.IsNullOrEmpty(common)) add(System.IO.Path.Combine(common, "Adobe", "Color", "Profiles", "Recommended"));
                add("/Library/ColorSync/Profiles");
                add("/Library/Application Support/Adobe/Color/Profiles/Recommended");
                if (!string.IsNullOrEmpty(home))
                {
                    add(System.IO.Path.Combine(home, "Library", "ColorSync", "Profiles"));
                    add(System.IO.Path.Combine(home, ".local", "share", "icc"));
                    add(System.IO.Path.Combine(home, ".color", "icc"));
                }
                add("/usr/share/color/icc");
                add("/usr/local/share/color/icc");
            }
            catch { }
            return folders;
        }

        /// <summary>Le rang d'un profil pour FOGRA39 : 2 = c'est lui par son
        /// nom, 1 = son jumeau de l'ECI (ISO Coated v2, même caractérisation
        /// FOGRA39L), 0 = un autre.</summary>
        public static int Fogra39Rank(string fileName, string description)
        {
            var text = ((fileName ?? "") + " " + (description ?? "")).ToLowerInvariant().Replace("_", " ").Replace("-", " ");
            if (text.Contains("fogra39") || text.Contains("fogra 39")) return 2;
            if (text.Contains("iso coated v2") || text.Contains("isocoated v2")) return text.Contains("300") ? 0 : 1; // la variante « 300 % » est une autre condition
            return 0;
        }

        /// <summary>Le nom de la ressource et l'empreinte SHA-256 du profil EMBARQUÉ
        /// (21/11/2026) : « ISO Coated v2 (basICColor) », caractérisation FOGRA39L,
        /// licence zlib/libpng — voir APPROVISIONNEMENT.md. L'empreinte est vérifiée
        /// à chaque lecture : un fichier abîmé ou remplacé n'est jamais incorporé.</summary>
        public const string EmbeddedName = "icc/ISOcoated_v2_bas.ICC";
        public const string EmbeddedSha256 = "7cdd1a643a1703fe62c30cd0cd2aba0fb01ae8d5a1ac985c8f8964ff73e4fae0";
        public const string EmbeddedLabel = "ISO Coated v2 (basICColor) — FOGRA39";

        private static IccProfile _embedded;
        private static bool _embeddedRead;

        /// <summary>Le profil livré avec Marabook, ou null s'il manque ou ne répond pas à son empreinte.</summary>
        public static IccProfile Embedded()
        {
            if (_embeddedRead) return _embedded;
            _embeddedRead = true;
            try
            {
                using (var stream = typeof(IccProfiles).Assembly.GetManifestResourceStream(EmbeddedName))
                {
                    if (stream == null) return null;
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        var bytes = buffer.ToArray();
                        string hash;
                        using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        if (hash != EmbeddedSha256 || !IsCmykPrinterProfile(bytes) || !SizeMatches(bytes) || !HasTag(bytes, "A2B1")) return null;
                        _embedded = new IccProfile { Path = "(livré avec Marabook)", Description = EmbeddedLabel, Bytes = bytes };
                    }
                }
            }
            catch { _embedded = null; }
            return _embedded;
        }

        /// <summary>Le profil porte-t-il cette balise (« A2B1 », « desc »…) ?</summary>
        public static bool HasTag(byte[] bytes, string tag)
        {
            if (bytes == null || bytes.Length < 132) return false;
            var count = U32(bytes, 128);
            for (var i = 0; i < count && i < 200; i++)
                if (Tag(bytes, 132 + 12 * i) == tag) return true;
            return false;
        }

        /// <summary>La taille que le profil déclare est-elle celle de ses octets ?</summary>
        public static bool SizeMatches(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 132 && U32(bytes, 0) == bytes.Length;
        }

        private static IccProfile _found;
        private static bool _searched;

        /// <summary>Le profil FOGRA39 de la machine (cherché une fois), ou
        /// null. extraFolder : un dossier de plus, regardé en premier.</summary>
        public static IccProfile FindFogra39(string extraFolder = null)
        {
            // Le profil livré avec Marabook d'abord (21/11) : le même fichier sur toutes les machines.
            if (extraFolder == null && Embedded() != null) return Embedded();
            if (_searched && extraFolder == null) return _found;
            var folders = SystemFolders();
            if (!string.IsNullOrEmpty(extraFolder)) folders.Insert(0, extraFolder);
            var found = Find(folders);
            if (extraFolder == null) { _found = found; _searched = true; }
            return found;
        }

        public static IccProfile Find(IEnumerable<string> folders)
        {
            IccProfile best = null;
            var bestRank = 0;
            foreach (var folder in folders)
            {
                string[] files;
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    files = Directory.GetFiles(folder, "*.ic?", SearchOption.AllDirectories);
                }
                catch { continue; }
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    try
                    {
                        var name = System.IO.Path.GetFileNameWithoutExtension(file);
                        var info = new FileInfo(file);
                        if (info.Length < 132 || info.Length > 16 * 1024 * 1024) continue;
                        // Le nom d'abord (lire chaque profil de la machine serait long) ; la description départage.
                        if (Fogra39Rank(name, "") == 0 && name.IndexOf("coated", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var bytes = File.ReadAllBytes(file);
                        if (!IsCmykPrinterProfile(bytes)) continue;
                        var description = DescriptionOf(bytes);
                        var rank = Fogra39Rank(name, description);
                        if (rank <= bestRank) continue;
                        best = new IccProfile { Path = file, Description = description.Length > 0 ? description : name, Bytes = bytes };
                        bestRank = rank;
                        if (rank == 2) return best;
                    }
                    catch { }
                }
            }
            return best;
        }
    }

    /// <summary>LE CONTRÔLE D'UN PDF/X : la porte d'entrée d'avant le 21/11,
    /// gardée pour ses appelants. Le travail est fait par PdfXInspector, qui
    /// lit les objets du fichier ; ici, la seule liste des défauts.</summary>
    public static class PdfXCheck
    {
        /// <summary>Les défauts du fichier, en clair ; liste vide = rien à redire.</summary>
        public static List<string> Verify(byte[] pdf)
        {
            return PdfXInspector.Inspect(pdf).Problems;
        }
    }
}

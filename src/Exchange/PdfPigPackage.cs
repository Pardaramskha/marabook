using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Marabook.Exchange
{
    /// <summary>PDFPIG, UNE DÉPENDANCE À LA DEMANDE (21/11/2026, Rémi). La
    /// bibliothèque qui lit les PDF n'est PAS livrée avec Marabook : elle se
    /// télécharge la première fois qu'on importe un PDF, après un oui.
    ///
    /// Ce qui garantit ce qu'on installe :
    /// - UNE source, en dur : le dépôt officiel de NuGet (api.nuget.org, en
    ///   HTTPS), UNE version, en dur ;
    /// - l'archive téléchargée doit avoir la taille et l'empreinte SHA-512
    ///   notées ici — celles que publie le catalogue de NuGet pour cette
    ///   version, relevées et recoupées le 21/11/2026 (APPROVISIONNEMENT.md) ;
    /// - seuls les fichiers NOMMÉS ici en sont tirés, et chacun doit avoir
    ///   son empreinte SHA-256 ; rien d'autre de l'archive n'est écrit ;
    /// - à CHAQUE chargement, les fichiers installés sont revérifiés : une
    ///   bibliothèque remplacée ou abîmée sur le disque n'est jamais chargée.
    /// Changer de version, c'est changer ce fichier : jamais « la dernière ».</summary>
    public static class PdfPigPackage
    {
        public const string Name = "PdfPig";
        public const string Version = "0.1.16";
        public const string License = "Apache-2.0";
        public const string Host = "api.nuget.org";
        public const string Url = "https://api.nuget.org/v3-flatcontainer/pdfpig/0.1.16/pdfpig.0.1.16.nupkg";
        public const long PackageSize = 16710625;
        /// <summary>SHA-512 de l'archive, en base64 : le « packageHash » du catalogue NuGet.</summary>
        public const string PackageSha512 = "clPAR660u7oGMGO0+I4JAq3olrsbIcKO2m3JtAuOow+IbS62Pyfh00GFnG10Ngl2nizTS5+DsCSlndW6sLvNBQ==";
        public const string EntryFolder = "lib/net8.0/";
        public const string MainAssembly = "UglyToad.PdfPig.dll";

        /// <summary>Les bibliothèques tirées de l'archive, et l'empreinte SHA-256 de chacune.</summary>
        public static readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "UglyToad.PdfPig.dll", "76b49537658d3ec0a56e33c9551c115136ea71ad3866815c24b34370b1d3861a" },
            { "UglyToad.PdfPig.Core.dll", "35995ff06a995fff5237b25b992becea340d2a85af444f0f5cac6b39dc75af1c" },
            { "UglyToad.PdfPig.Fonts.dll", "f1b8263824330714b9cb7d0876c12103e3af913f9474bc55ced078b5d5e56506" },
            { "UglyToad.PdfPig.Tokenization.dll", "21f8d67bbe60f00f9f6b1bfc347c03db4606338a008e8d9d1dcf1073be7020d2" },
            { "UglyToad.PdfPig.Tokens.dll", "75a1e796a4a25d0dc116b94e4a1bdd4ad1c3785bab362b911d6d26043856fa38" },
            { "UglyToad.PdfPig.Package.dll", "5da6b80a0a23552806726801bf7f0a48521bb394268a6f9f8d4faf8da002682a" }
        };

        /// <summary>Le poids installé, en Mo arrondis (dit à l'auteur avant qu'il accepte).</summary>
        public const int InstalledMegabytes = 6;
        public const int DownloadMegabytes = 17;

        /// <summary>La question posée avant le téléchargement (le texte de Rémi, au poids réel près).</summary>
        public static string Prompt
        {
            get
            {
                return "Afin d'importer des fichiers PDF (de manière encore assez approximative), PdfPig doit être téléchargée comme dépendance de code. "
                    + "Cela rajoute environ " + InstalledMegabytes + " Mo d'installation (" + DownloadMegabytes + " Mo à télécharger, une seule fois), et vous permettra d'importer autant de PDF que souhaité par la suite.\n\n"
                    + "Source : le dépôt officiel NuGet (" + Host + "), version " + Version + ", licence " + License + ". Marabook vérifie l'empreinte de ce qu'il reçoit avant de l'installer.\n\n"
                    + "Voulez-vous continuer ?";
            }
        }

        /// <summary>Le dossier où la dépendance s'installe : un sous-dossier par version.</summary>
        public static string FolderIn(string dataFolder)
        {
            return Path.Combine(dataFolder ?? "", "dependances", "pdfpig-" + Version);
        }

        public static string Sha256Of(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>L'archive reçue est-elle bien celle attendue ? null si
        /// oui, sinon ce qui cloche, dit pour l'auteur.</summary>
        public static string Verify(byte[] package)
        {
            if (package == null || package.Length == 0) return "Le téléchargement est vide.";
            if (package.LongLength != PackageSize) return "L'archive reçue n'a pas la taille attendue (" + package.LongLength + " octets au lieu de " + PackageSize + ") : elle n'a pas été installée.";
            string hash;
            using (var sha = SHA512.Create()) hash = Convert.ToBase64String(sha.ComputeHash(package));
            if (!string.Equals(hash, PackageSha512, StringComparison.Ordinal)) return "L'empreinte de l'archive reçue n'est pas celle que NuGet publie pour PdfPig " + Version + " : elle n'a pas été installée.";
            return null;
        }

        /// <summary>Tire de l'archive (déjà vérifiée ou non : elle l'est ici)
        /// les seules bibliothèques nommées, vérifie chacune, et ne les écrit
        /// qu'une fois TOUTES reconnues. null si c'est fait, sinon l'erreur.</summary>
        public static string Install(byte[] package, string folder)
        {
            var problem = Verify(package);
            if (problem != null) return problem;
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            try
            {
                using (var stream = new MemoryStream(package, false))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    foreach (var pair in Files)
                    {
                        var entry = archive.GetEntry(EntryFolder + pair.Key);
                        if (entry == null) return "L'archive ne contient pas « " + pair.Key + " ».";
                        if (entry.Length > 32 * 1024 * 1024) return "« " + pair.Key + " » est anormalement gros.";
                        using (var source = entry.Open())
                        using (var buffer = new MemoryStream())
                        {
                            source.CopyTo(buffer);
                            var bytes = buffer.ToArray();
                            if (Sha256Of(bytes) != pair.Value) return "« " + pair.Key + " » n'a pas l'empreinte attendue : rien n'a été installé.";
                            contents[pair.Key] = bytes;
                        }
                    }
                Directory.CreateDirectory(folder);
                foreach (var pair in contents) File.WriteAllBytes(Path.Combine(folder, pair.Key), pair.Value);
            }
            catch (Exception error)
            {
                return "L'installation de PdfPig a échoué : " + error.Message;
            }
            return IsInstalled(folder) ? null : "Les fichiers écrits ne se relisent pas correctement.";
        }

        /// <summary>Toutes les bibliothèques sont-elles là, CHACUNE avec son empreinte ?</summary>
        public static bool IsInstalled(string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
                foreach (var pair in Files)
                {
                    var path = Path.Combine(folder, pair.Key);
                    if (!File.Exists(path)) return false;
                    if (Sha256Of(File.ReadAllBytes(path)) != pair.Value) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Retire la dépendance (Préférences, ou un fichier abîmé à remplacer).</summary>
        public static void Remove(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (Exception) { }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Marabook.Exchange
{
    /// <summary>PDFIUM, UNE DÉPENDANCE À LA DEMANDE (21/11/2026, Rémi : l'Atelier).
    /// Le moteur PDF de Chromium — celui qui AFFICHE les pages d'un PDF, les
    /// recopie d'un fichier à l'autre et rouvre un fichier verrouillé. Il n'est
    /// PAS livré avec Marabook : l'Atelier le télécharge la première fois qu'un
    /// de ses outils en a besoin, après un oui.
    ///
    /// Mêmes garanties que pour PdfPig (PdfPigPackage) :
    /// - UNE source, en dur : le dépôt officiel de NuGet (api.nuget.org, HTTPS),
    ///   UNE version, en dur, UN paquet par système (bblanchon.PDFium.*) ;
    /// - l'archive reçue doit avoir la taille et l'empreinte SHA-512 notées ici
    ///   — celles que publie le catalogue de NuGet pour cette version, relevées
    ///   le 21/11/2026 et recoupées avec la publication GitHub du même projet
    ///   (APPROVISIONNEMENT.md) ;
    /// - UN seul fichier en est tiré, la bibliothèque du système et du
    ///   processeur en cours, et il doit avoir son empreinte SHA-256 ;
    /// - à CHAQUE chargement, le fichier installé est revérifié : une
    ///   bibliothèque remplacée ou abîmée sur le disque n'est jamais chargée.
    /// Changer de version, c'est changer ce fichier : jamais « la dernière ».</summary>
    public static class PdfiumPackage
    {
        public const string Name = "PDFium";
        public const string Version = "160.0.8165";
        public const string License = "Apache-2.0 (binaires), BSD-3-Clause (PDFium)";
        public const string Host = "api.nuget.org";

        /// <summary>Ce qui s'installe pour UN système et UN processeur.</summary>
        public sealed class Target
        {
            public string Rid;            // win-x64, linux-arm64, osx-x64…
            public string Package;        // le paquet NuGet, en minuscules
            public long PackageSize;
            public string PackageSha512;  // base64 : le « packageHash » du catalogue NuGet
            public string File;           // pdfium.dll, libpdfium.so, libpdfium.dylib
            public string FileSha256;

            public string Url { get { return "https://" + Host + "/v3-flatcontainer/" + Package + "/" + Version + "/" + Package + "." + Version + ".nupkg"; } }
            public string Entry { get { return "runtimes/" + Rid + "/native/" + File; } }
            public int DownloadMegabytes { get { return (int)Math.Ceiling(PackageSize / 1048576.0); } }
        }

        private const string Win = "bblanchon.pdfium.win32", Linux = "bblanchon.pdfium.linux", Mac = "bblanchon.pdfium.macos";
        private const long WinSize = 11367600, LinuxSize = 43492415, MacSize = 7353296;
        private const string WinHash = "bf5UskRyHdeDXnRX1h5MStuab1U8c4r2vd7JfY6OYn2cJvV/CMVfSknEO7qqmA5anGxQiAVVGEMhvdgNNjfsqw==";
        private const string LinuxHash = "UeHyeQHanssVVZmIHa4XTCvUCfHTt4043LODXv4+BTJqPDvvftr6vOVji/2x3ENKfFMZeawEbsiYARfdHhV4jQ==";
        private const string MacHash = "zwfBKEC/VaZOe5lj4iW8vNRItxpwwnT2lYaHhQ7CF+ySn+ndM4u5Cj/9cGoUNQ4CAPe5muCDZQavmqgASFAmgQ==";

        public static readonly Target[] Targets =
        {
            new Target { Rid = "win-x64", Package = Win, PackageSize = WinSize, PackageSha512 = WinHash, File = "pdfium.dll", FileSha256 = "31d249b8535dd5f351ae7f4b714dc4bb1d76b6069b321a309bc99271dd65f566" },
            new Target { Rid = "win-arm64", Package = Win, PackageSize = WinSize, PackageSha512 = WinHash, File = "pdfium.dll", FileSha256 = "9b1eb281e6f8b2da9f54fc71619198c90142adab2f57f5251a4824a08da621b3" },
            new Target { Rid = "linux-x64", Package = Linux, PackageSize = LinuxSize, PackageSha512 = LinuxHash, File = "libpdfium.so", FileSha256 = "63b70290e3e88eff6b236a4c87cb58461b4fe9fae56217314eeda31aff741479" },
            new Target { Rid = "linux-arm64", Package = Linux, PackageSize = LinuxSize, PackageSha512 = LinuxHash, File = "libpdfium.so", FileSha256 = "963309fe119e4614afb750bc6a4e35272bd5b8bad61fb84df282c9c821f7de8b" },
            new Target { Rid = "osx-x64", Package = Mac, PackageSize = MacSize, PackageSha512 = MacHash, File = "libpdfium.dylib", FileSha256 = "fcbfee9dc7f895289fbfb8f7d1f0d5d643f60fcb790708425daaa0cfa64f330d" },
            new Target { Rid = "osx-arm64", Package = Mac, PackageSize = MacSize, PackageSha512 = MacHash, File = "libpdfium.dylib", FileSha256 = "d421e426bd487de02fa3bd1f2aeaf56947261f790e6116951e26f729ced1b018" }
        };

        /// <summary>Le poids installé, en Mo arrondis (une seule bibliothèque).</summary>
        public const int InstalledMegabytes = 8;

        /// <summary>Ce qui s'installe pour ce système (« win-x64 »…), ou null s'il n'est pas prévu.</summary>
        public static Target For(string rid)
        {
            foreach (var target in Targets) if (target.Rid == rid) return target;
            return null;
        }

        /// <summary>La question posée avant le téléchargement.</summary>
        public static string Prompt(Target target)
        {
            return "Pour afficher les pages d'un PDF, les réorganiser ou rouvrir un fichier verrouillé, l'Atelier a besoin de PDFium, le moteur PDF de Chromium. "
                + "Il n'est pas livré avec Marabook : il se télécharge une fois (" + (target == null ? "?" : target.DownloadMegabytes.ToString()) + " Mo), pour environ " + InstalledMegabytes + " Mo installés.\n\n"
                + "Source : le dépôt officiel NuGet (" + Host + "), paquets bblanchon.PDFium, version " + Version + ", licence " + License + ". Marabook vérifie l'empreinte de ce qu'il reçoit avant de l'installer, et rien de vos fichiers ne part.\n\n"
                + "Voulez-vous continuer ?";
        }

        /// <summary>Le dossier où la dépendance s'installe : un sous-dossier par version.</summary>
        public static string FolderIn(string dataFolder)
        {
            return Path.Combine(dataFolder ?? "", "dependances", "pdfium-" + Version);
        }

        /// <summary>L'archive reçue est-elle bien celle attendue ? null si oui, sinon ce qui cloche.</summary>
        public static string Verify(Target target, byte[] package)
        {
            if (target == null) return "PDFium n'est pas prévu pour ce système.";
            if (package == null || package.Length == 0) return "Le téléchargement est vide.";
            if (package.LongLength != target.PackageSize) return "L'archive reçue n'a pas la taille attendue (" + package.LongLength + " octets au lieu de " + target.PackageSize + ") : elle n'a pas été installée.";
            string hash;
            using (var sha = SHA512.Create()) hash = Convert.ToBase64String(sha.ComputeHash(package));
            if (!string.Equals(hash, target.PackageSha512, StringComparison.Ordinal)) return "L'empreinte de l'archive reçue n'est pas celle que NuGet publie pour PDFium " + Version + " : elle n'a pas été installée.";
            return null;
        }

        /// <summary>Tire de l'archive (vérifiée ici) la SEULE bibliothèque du système,
        /// la vérifie, puis l'écrit. null si c'est fait, sinon l'erreur.</summary>
        public static string Install(Target target, byte[] package, string folder)
        {
            var problem = Verify(target, package);
            if (problem != null) return problem;
            try
            {
                byte[] bytes;
                using (var stream = new MemoryStream(package, false))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var entry = archive.GetEntry(target.Entry);
                    if (entry == null) return "L'archive ne contient pas « " + target.Entry + " ».";
                    if (entry.Length > 64 * 1024 * 1024) return "« " + target.File + " » est anormalement gros.";
                    using (var source = entry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        source.CopyTo(buffer);
                        bytes = buffer.ToArray();
                    }
                }
                if (PdfPigPackage.Sha256Of(bytes) != target.FileSha256) return "« " + target.File + " » n'a pas l'empreinte attendue : rien n'a été installé.";
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, target.File), bytes);
            }
            catch (Exception error)
            {
                return "L'installation de PDFium a échoué : " + error.Message;
            }
            return IsInstalled(target, folder) ? null : "Le fichier écrit ne se relit pas correctement.";
        }

        /// <summary>La bibliothèque est-elle là, AVEC son empreinte ?</summary>
        public static bool IsInstalled(Target target, string folder)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(folder)) return false;
                var path = Path.Combine(folder, target.File);
                return File.Exists(path) && PdfPigPackage.Sha256Of(File.ReadAllBytes(path)) == target.FileSha256;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Retire la dépendance. Vrai si plus rien n'est là.</summary>
        public static bool Remove(string folder)
        {
            try
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) Directory.Delete(folder, true);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}

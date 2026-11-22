using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Marabook.Exchange;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>Une chose que Marabook a TÉLÉCHARGÉE : un composant tiers (une
    /// bibliothèque qu'un outil demande) ou un module (DLC).</summary>
    internal sealed class Dependency
    {
        public string Key;
        public string Name;
        public string Version;
        /// <summary>À quoi elle sert, en une phrase.</summary>
        public string Role;
        /// <summary>D'où elle vient, sous quelle licence.</summary>
        public string Source;
        public string Folder;
        /// <summary>CE QUI S'ÉTEINT si on la désinstalle — dit avant de confirmer.</summary>
        public string Loses;
        /// <summary>Ce qui reste disponible sans elle (peut être vide).</summary>
        public string Keeps = "";
        public bool IsModule;
        /// <summary>Désinstalle. null si c'est fait, sinon ce qui l'a empêché.</summary>
        public Func<string> Remove;
    }

    /// <summary>LES DÉPENDANCES (2.0.0, 22/11/2026, Rémi) : ce que Marabook a
    /// téléchargé à la demande, pour Préférences › Dépendances — son poids, où
    /// il est, et ce que l'application perd si on le retire. Rien de tout cela
    /// n'est livré avec Marabook : un composant s'installe quand un outil en a
    /// besoin (l'Atelier), un module depuis l'onglet DLC. Une dépendance de
    /// plus s'ajoute ICI, et la liste des Préférences la montre.</summary>
    internal static class Dependencies
    {
        /// <summary>Posé par la coquille : rend les fichiers que l'Atelier tient ouverts (sans quoi
        /// PDFium ne se retire pas).</summary>
        public static Action ReleaseFiles;

        /// <summary>Ce qui est installé EN CE MOMENT, composants d'abord.</summary>
        public static List<Dependency> Installed()
        {
            var list = new List<Dependency>();
            if (PdfPigBridge.Installed)
                list.Add(new Dependency
                {
                    Key = "pdfpig",
                    Name = "PdfPig",
                    Version = PdfPigPackage.Version,
                    Role = "Lit le texte d'un PDF.",
                    Source = "NuGet (" + PdfPigPackage.Host + "), licence " + PdfPigPackage.License,
                    Folder = PdfPigBridge.Folder,
                    Loses = "l'import d'un PDF dans un écrit, et « PDF vers Word » de l'Atelier (PDFaah)",
                    Remove = delegate
                    {
                        PdfPigPackage.Remove(PdfPigBridge.Folder);
                        return PdfPigBridge.Installed ? "PdfPig n'a pas pu être retirée : elle sert peut-être en ce moment. Fermez Marabook, rouvrez-le, puis réessayez." : null;
                    }
                });
            if (Pdfium.Installed)
                list.Add(new Dependency
                {
                    Key = "pdfium",
                    Name = "PDFium",
                    Version = PdfiumPackage.Version,
                    Role = "Le moteur PDF de Chromium : il affiche les pages d'un PDF et les réécrit.",
                    Source = "NuGet (" + PdfiumPackage.Host + "), licence " + PdfiumPackage.License,
                    Folder = Pdfium.Folder,
                    Loses = "les outils de PDFaah qui affichent ou réécrivent un PDF : Chop-Chop, l'Unlocker, scinder, fusionner, pages blanches, pivoter, filigrane, signature, compression, verrouillage, PDF vers images",
                    Keeps = "Le contrôle d'impression et le contrôle PDF/X n'en ont pas besoin.",
                    Remove = delegate
                    {
                        var release = ReleaseFiles;
                        if (release != null) release();
                        var removed = Pdfium.Remove();
                        return removed && !Pdfium.Installed ? null : "PDFium n'a pas pu être retiré : un PDF est peut-être encore ouvert. Fermez Marabook, rouvrez-le, puis réessayez.";
                    }
                });
            foreach (var module in new List<ModuleInfo>(Modules.Installed))
            {
                var info = module;
                var features = info.Features.Count > 0 ? string.Join(", ", info.Features.ToArray()) : info.Title;
                list.Add(new Dependency
                {
                    Key = "dlc:" + info.Id,
                    Name = info.Name,
                    Version = info.Version,
                    Role = string.IsNullOrEmpty(info.Title) ? "Un module (DLC)." : info.Title,
                    Source = "Module (DLC) — se réinstalle depuis l'onglet DLC",
                    Folder = Path.Combine(Modules.Root, info.Id),
                    Loses = "ce que ce module apporte" + (string.IsNullOrEmpty(features) ? "" : " (" + features + ")") + " ; les projets gardent leurs valeurs",
                    IsModule = true,
                    Remove = delegate
                    {
                        Modules.Uninstall(info.Id);
                        return null;
                    }
                });
            }
            return list;
        }

        /// <summary>Le poids d'un dossier sur le disque, en octets (0 s'il ne se lit pas).</summary>
        public static long SizeOf(string folder)
        {
            long total = 0;
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
                foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return total;
        }

        public static string SizeLabel(long bytes)
        {
            var culture = CultureInfo.CurrentCulture;
            if (bytes >= 1024 * 1024) return (bytes / 1048576.0).ToString("0.0", culture) + " Mo";
            if (bytes >= 1024) return Math.Max(1, bytes / 1024).ToString("N0", culture) + " Ko";
            return bytes + " octets";
        }
    }
}

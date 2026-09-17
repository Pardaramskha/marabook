using System;
using System.Diagnostics;
using System.IO;

namespace Marabook.Exchange
{
    /// <summary>Bridge to external converters for formats we will never parse by
    /// hand. Binary .doc goes through an installed LibreOffice (headless
    /// convert-to docx); .gdoc is only a Drive pointer, so the user gets
    /// guidance instead. Nothing is downloaded automatically — LibreOffice is
    /// detected, never fetched (300 Mo is an explicit user decision).</summary>
    public static class ExternalBridge
    {
        public static string FindLibreOffice()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "LibreOffice", "program", "soffice.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "LibreOffice", "program", "soffice.exe")
            };
            foreach (var candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return null;
        }

        /// <summary>Converts a legacy .doc to a temp .docx via LibreOffice.
        /// Returns the converted path, or throws with a user-facing message.</summary>
        public static string DocToDocx(string docPath)
        {
            var soffice = FindLibreOffice();
            if (soffice == null)
                throw new InvalidOperationException(
                    "Le format .doc (Word 97-2003) nécessite LibreOffice pour être converti.\n" +
                    "Installez LibreOffice (gratuit), ou enregistrez le fichier en .docx depuis Word.");

            var outDir = Path.Combine(Path.GetTempPath(), "Marabook", "conversions");
            Directory.CreateDirectory(outDir);

            var info = new ProcessStartInfo
            {
                FileName = soffice,
                Arguments = "--headless --convert-to docx --outdir \"" + outDir + "\" \"" + docPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                if (!process.WaitForExit(60000))
                {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException("La conversion LibreOffice a dépassé 60 secondes.");
                }
            }

            var converted = Path.Combine(outDir, Path.GetFileNameWithoutExtension(docPath) + ".docx");
            if (!File.Exists(converted))
                throw new InvalidOperationException("LibreOffice n'a pas produit le .docx attendu.");
            return converted;
        }

        public const string GdocGuidance =
            "Un fichier .gdoc ne contient pas le texte : c'est un raccourci vers Google Drive.\n\n" +
            "Pour l'importer : ouvrez le document dans Google Docs, puis\n" +
            "Fichier → Télécharger → Microsoft Word (.docx), et importez ce fichier.";
    }
}

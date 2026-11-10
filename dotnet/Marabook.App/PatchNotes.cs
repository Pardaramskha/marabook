using System;
using System.IO;
using System.Reflection;

namespace Marabook.App
{
    /// <summary>Les patch notes embarquées (07/10) : patchnotes/&lt;VERSION&gt;.md
    /// est compilé dans l'assembly (Marabook.App.csproj, LogicalName
    /// « patchnotes/… ») pour qu'Aide › Nouveautés relise, hors ligne, le
    /// texte de la version installée — le même que celui de la release et
    /// de la fenêtre de mise à jour.</summary>
    public static class PatchNotes
    {
        /// <summary>Le Markdown de cette version, ou null s'il n'a pas été
        /// écrit (une version bumpée avant ses notes).</summary>
        public static string ForVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("patchnotes/" + version.Trim() + ".md"))
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                    {
                        var text = reader.ReadToEnd().Trim();
                        return text.Length == 0 ? null : text;
                    }
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Les versions dont les notes sont embarquées (1.1.1) : les
        /// noms des ressources patchnotes/*.md, sans le dossier ni l'extension.</summary>
        public static System.Collections.Generic.List<string> KnownVersions()
        {
            var versions = new System.Collections.Generic.List<string>();
            try
            {
                foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                    if (name.StartsWith("patchnotes/", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        versions.Add(name.Substring("patchnotes/".Length, name.Length - "patchnotes/".Length - 3));
            }
            catch (Exception) { }
            return versions;
        }

        /// <summary>L'image d'une note (1/11, Rémi : une image en tête des
        /// patch notes d'une version majeure) : l'adresse écrite dans le
        /// Markdown (une URL raw.githubusercontent pour la release GitHub, ou
        /// un simple nom) est ramenée à son nom de fichier, cherché parmi les
        /// images embarquées (patchnotes/*.jpg, *.png). Null si inconnue.</summary>
        public static Avalonia.Media.Imaging.Bitmap ImageFor(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            try
            {
                var name = address.Trim();
                var cut = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
                if (cut >= 0) name = name.Substring(cut + 1);
                var query = name.IndexOf('?');
                if (query >= 0) name = name.Substring(0, query);
                if (name.Length == 0) return null;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("patchnotes/" + name))
                {
                    if (stream == null) return null;
                    return new Avalonia.Media.Imaging.Bitmap(stream);
                }
            }
            catch (Exception) { return null; }
        }
    }
}

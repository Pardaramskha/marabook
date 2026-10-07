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
    }
}

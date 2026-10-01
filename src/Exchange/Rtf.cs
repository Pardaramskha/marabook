using System;
using System.IO;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Le RTF vu du cœur (portage Avalonia, P0) : le convertisseur
    /// est celui de l'interface — WPF le tient gratuitement (Wpf/Rtf.cs :
    /// TextRange.Save/Load), Avalonia devra en écrire un. L'app branche le
    /// sien au démarrage ; sans convertisseur, l'import et l'export RTF
    /// lèvent une NotSupportedException explicite.</summary>
    public static class Rtf
    {
        public const string Filter = "Texte enrichi (*.rtf)|*.rtf";

        /// <summary>(flux RTF, styles du projet, projet ou null) → document.</summary>
        public static Func<Stream, StyleSheet, Project, TextDocument> Importer;

        /// <summary>(document, styles, chemin, projet ou null) → écrit le fichier.</summary>
        public static Action<TextDocument, StyleSheet, string, Project> Exporter;

        public static bool Available { get { return Importer != null && Exporter != null; } }

        public static void Export(TextDocument document, StyleSheet styles, string path,
            Project project = null)
        {
            if (Exporter == null) throw new NotSupportedException("Aucun convertisseur RTF n'est branché (Exchange.Rtf.Exporter).");
            Exporter(document, styles, path, project);
        }

        /// <summary>project (0.50.0) : les images du RTF (\pict) entrent dans
        /// le magasin du projet, réencodées en PNG ; null = ignorées.</summary>
        public static TextDocument Import(string path, StyleSheet projectStyles, Project project = null)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                return ImportStream(stream, projectStyles, project);
        }

        public static TextDocument ImportStream(Stream stream, StyleSheet projectStyles, Project project = null)
        {
            if (Importer == null) throw new NotSupportedException("Aucun convertisseur RTF n'est branché (Exchange.Rtf.Importer).");
            return Importer(stream, projectStyles, project);
        }
    }
}

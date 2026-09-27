using System.IO;
using System.Windows.Documents;
using Marabook.Model;
using Marabook.View;

namespace Marabook.Wpf
{
    /// <summary>RTF via WPF's own converter (TextRange.Save/Load on a
    /// FlowDocument): free and battle-tested. Named styles flatten to direct
    /// formatting on export and come back as run overrides on import — that is
    /// inherent to WPF's RTF support and acceptable for an interchange format.
    /// Also the road Scrivener import rides on. Côté WPF (P0) : le cœur passe
    /// par Exchange.Rtf, dont Register branche ce convertisseur.</summary>
    public static class WpfRtf
    {
        /// <summary>Branche ce convertisseur sur Exchange.Rtf (au démarrage
        /// de l'app et du harnais de tests).</summary>
        public static void Register()
        {
            Exchange.Rtf.Exporter = Export;
            Exchange.Rtf.Importer = ImportStream;
        }

        public static void Export(TextDocument document, StyleSheet styles, string path,
            Project project)
        {
            var flow = FlowConverter.ToFlow(document, styles, project,
                revisionTints: false); // le RTF exporté reste vierge d'annotations
            var range = new TextRange(flow.ContentStart, flow.ContentEnd);
            using (var stream = new FileStream(path, FileMode.Create))
                range.Save(stream, System.Windows.DataFormats.Rtf);
        }

        /// <summary>project (0.50.0) : les images du RTF (\pict) entrent dans
        /// le magasin du projet, réencodées en PNG ; null = ignorées.</summary>
        public static TextDocument ImportStream(Stream stream, StyleSheet projectStyles, Project project)
        {
            var flow = new FlowDocument();
            var range = new TextRange(flow.ContentStart, flow.ContentEnd);
            range.Load(stream, System.Windows.DataFormats.Rtf);
            return FlowConverter.FromFlow(flow, projectStyles, null, project);
        }
    }
}

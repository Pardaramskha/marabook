using System.IO;
using System.Windows.Documents;
using Marabook.Model;
using Marabook.View;

namespace Marabook.Exchange
{
    /// <summary>RTF via WPF's own converter (TextRange.Save/Load on a
    /// FlowDocument): free and battle-tested. Named styles flatten to direct
    /// formatting on export and come back as run overrides on import — that is
    /// inherent to WPF's RTF support and acceptable for an interchange format.
    /// Also the road Scrivener import rides on.</summary>
    public static class Rtf
    {
        public const string Filter = "Texte enrichi (*.rtf)|*.rtf";

        public static void Export(TextDocument document, StyleSheet styles, string path,
            Project project = null)
        {
            var flow = FlowConverter.ToFlow(document, styles, project,
                revisionTints: false); // le RTF exporté reste vierge d'annotations
            var range = new TextRange(flow.ContentStart, flow.ContentEnd);
            using (var stream = new FileStream(path, FileMode.Create))
                range.Save(stream, System.Windows.DataFormats.Rtf);
        }

        public static TextDocument Import(string path, StyleSheet projectStyles)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                return ImportStream(stream, projectStyles);
        }

        public static TextDocument ImportStream(Stream stream, StyleSheet projectStyles)
        {
            var flow = new FlowDocument();
            var range = new TextRange(flow.ContentStart, flow.ContentEnd);
            range.Load(stream, System.Windows.DataFormats.Rtf);
            return FlowConverter.FromFlow(flow, projectStyles, null);
        }
    }
}

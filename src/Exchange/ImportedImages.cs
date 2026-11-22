using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Marabook.Model;

namespace Marabook.Exchange
{
    /// <summary>Les images d'un document importé (correctif du 23/09/2026,
    /// premier retour de testeur : un .docx importé perdait ses images).
    /// Sert aux lecteurs docx et odt : une entrée de l'archive (word/media/…,
    /// Pictures/…) devient une image du magasin du projet, UNE fois par
    /// entrée (deux appels à la même image partagent l'id) ; les formats que
    /// WPF ne décode pas (emf, wmf, svg…) sont comptés dans Skipped et
    /// laissés de côté plutôt que d'entrer inertes dans le .plot. Sans projet
    /// (lecture des seuls commentaires, tests d'avant), rien n'est stocké et
    /// le texte se lit comme avant.</summary>
    public sealed class ImportedImages
    {
        /// <summary>Les extensions que le compositeur sait afficher (MediaView.TryImage).</summary>
        public static readonly string[] Supported = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff" };

        private readonly Project _project;
        private readonly ZipArchive _zip;
        private readonly Dictionary<string, string> _byEntry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Les images laissées de côté (format illisible ou entrée absente).</summary>
        public int Skipped;

        /// <summary>QUI ÉCOUTE LES IMAGES LAISSÉES DE CÔTÉ (21/11). Un import
        /// écartait en silence les formats que Marabook n'affiche pas (EMF et
        /// WMF, les dessins vectoriels de Windows ; SVG) : le document
        /// arrivait troué sans un mot. L'appelant pose ici de quoi recevoir
        /// le nom de chaque image écartée, puis le DIT à l'auteur.</summary>
        [ThreadStatic] public static Action<string> SkippedSink;

        internal static void ReportSkipped(string name)
        {
            var sink = SkippedSink;
            if (sink != null && !string.IsNullOrEmpty(name)) sink(Path.GetFileName(name.Replace('\\', '/')));
        }

        /// <summary>« 2 images EMF, 1 image SVG » : les images écartées, comptées par format.</summary>
        public static string Describe(IEnumerable<string> names)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            if (names != null)
                foreach (var name in names)
                {
                    var extension = Path.GetExtension(name ?? "").TrimStart('.').ToUpperInvariant();
                    if (extension.Length == 0) extension = "inconnu";
                    int count;
                    counts[extension] = counts.TryGetValue(extension, out count) ? count + 1 : 1;
                }
            var parts = new List<string>();
            foreach (var pair in counts) parts.Add(pair.Value + (pair.Value > 1 ? " images " : " image ") + (pair.Key == "inconnu" ? "de format inconnu" : pair.Key));
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>Ce qu'on dit à l'auteur des formats écartés, et quoi faire.</summary>
        public const string SkippedAdvice = "Marabook n'affiche pas ces formats (EMF et WMF sont des dessins vectoriels de Windows). Dans le logiciel d'origine, enregistrez ces images en PNG ou en JPEG puis réimportez le document — ou ajoutez-les ensuite à la main dans l'écrit.";
        /// <summary>Les images stockées (distinctes).</summary>
        public int Stored;

        public ImportedImages(Project project, ZipArchive zip)
        {
            _project = project;
            _zip = zip;
        }

        public bool Enabled { get { return _project != null && _zip != null; } }

        /// <summary>L'id d'image du projet pour cette entrée de l'archive
        /// (chemin complet, « word/media/image1.png »), ou null : pas de
        /// projet, entrée absente, ou format non pris en charge.</summary>
        public string Store(string entryName)
        {
            if (!Enabled || string.IsNullOrEmpty(entryName)) return null;
            string id;
            if (_byEntry.TryGetValue(entryName, out id)) return id;
            var entry = _zip.GetEntry(entryName);
            var extension = Path.GetExtension(entryName).ToLowerInvariant();
            if (entry == null || Array.IndexOf(Supported, extension) < 0)
            {
                if (entry != null) ReportSkipped(entryName); // un format qu'on n'affiche pas : dit à l'auteur (21/11)
                Skipped++;
                _byEntry[entryName] = null;
                return null;
            }
            byte[] bytes;
            using (var stream = entry.Open())
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }
            if (bytes.Length == 0)
            {
                Skipped++;
                _byEntry[entryName] = null;
                return null;
            }
            // Optimisée à l'import (1.0.6) : réduite et réencodée si le codec le peut.
            string stored;
            bytes = Platform.Images.Optimize(bytes, extension, out stored);
            id = _project.AddImage(bytes, stored);
            _byEntry[entryName] = id;
            Stored++;
            return id;
        }

        /// <summary>Le chemin d'archive d'une cible de relation : relative au
        /// dossier de la partie (« media/image1.png » depuis « word/ »), ou
        /// absolue (« /word/media/image1.png ») ; « ./ » et « ../ » résolus,
        /// séparateurs en « / ».</summary>
        public static string Resolve(string baseDirectory, string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            target = target.Replace('\\', '/');
            var path = target.StartsWith("/") ? target.Substring(1) : (baseDirectory ?? "") + target;
            var parts = new List<string>();
            foreach (var part in path.Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(part);
            }
            return string.Join("/", parts.ToArray());
        }
    }
}

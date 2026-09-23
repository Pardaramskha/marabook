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
            id = _project.AddImage(bytes, extension);
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

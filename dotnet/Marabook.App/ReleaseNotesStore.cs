using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La mémoire des notes de version (1.1.1) : ce que l'application
    /// sait des notes de chaque version — les patch notes EMBARQUÉES (toutes
    /// celles du dépôt jusqu'à la version installée) et les notes des releases
    /// VUES EN LIGNE par le vérificateur de mises à jour, gardées dans
    /// notes-de-version.json à côté des réglages. C'est ce qui permet à
    /// l'onglet « Notes de version » de montrer la majeure du cycle et ses
    /// patchs précédents, même hors ligne, même quand l'application installée
    /// date d'avant la majeure.</summary>
    public static class ReleaseNotesStore
    {
        private static readonly object Gate = new object();
        private static Dictionary<string, string> _online; // version → notes vues en ligne (chargé à la demande)

        public static string FilePath()
        {
            return Path.Combine(AppSettings.SettingsFolder(), "notes-de-version.json");
        }

        /// <summary>Oublie ce qui est chargé (la sonde change de dossier de réglages).</summary>
        public static void Reset()
        {
            lock (Gate) _online = null;
        }

        private static Dictionary<string, string> Online()
        {
            lock (Gate)
            {
                if (_online != null) return _online;
                _online = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var path = FilePath();
                    if (File.Exists(path))
                    {
                        var root = Json.AsObject(Json.Parse(File.ReadAllText(path, System.Text.Encoding.UTF8)));
                        var versions = Json.AsObject(Json.Field(root, "versions"));
                        if (versions != null)
                            foreach (var pair in versions)
                                if (pair.Value is string && !string.IsNullOrEmpty(pair.Key)) _online[pair.Key] = (string)pair.Value;
                    }
                }
                catch (Exception) { }
                return _online;
            }
        }

        /// <summary>Les notes d'une version : embarquées d'abord, sinon vues en
        /// ligne ; null quand rien n'est connu.</summary>
        public static string NotesOf(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;
            var embedded = PatchNotes.ForVersion(version);
            if (embedded != null) return embedded;
            string seen;
            lock (Gate)
                return Online().TryGetValue(version.Trim(), out seen) && !string.IsNullOrEmpty(seen) ? seen : null;
        }

        /// <summary>Retient les notes d'une release vue en ligne (rien si elles
        /// sont déjà embarquées ou déjà retenues telles quelles).</summary>
        public static void Remember(string version, string notes)
        {
            if (string.IsNullOrEmpty(version)) return;
            var text = (notes ?? "").Trim();
            if (text.Length == 0 || PatchNotes.ForVersion(version) != null) return;
            lock (Gate)
            {
                var online = Online();
                string existing;
                if (online.TryGetValue(version.Trim(), out existing) && existing == text) return;
                online[version.Trim()] = text;
                try
                {
                    var versions = new Dictionary<string, object>();
                    foreach (var pair in online) versions[pair.Key] = pair.Value;
                    var root = new Dictionary<string, object> { { "versions", versions } };
                    var path = FilePath();
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, Json.Write(root), new System.Text.UTF8Encoding(false));
                }
                catch (Exception) { }
            }
        }

        /// <summary>Toutes les versions dont on connaît les notes.</summary>
        public static List<string> KnownVersions()
        {
            var known = new List<string>(PatchNotes.KnownVersions());
            lock (Gate)
                foreach (var version in Online().Keys) if (!known.Contains(version)) known.Add(version);
            return known;
        }

        /// <summary>L'onglet « Notes de version » pour une version : les notes
        /// de la majeure de son cycle (pour une majeure, ses propres notes),
        /// puis les patchs précédents du cycle (avant elle, exclue). Null si
        /// rien n'est connu de la majeure ni des patchs.</summary>
        public static string VersionNotes(string version, string ownNotes)
        {
            if (string.IsNullOrEmpty(version)) return null;
            var major = ReleaseNotes.MajorOf(version);
            var majorNotes = ReleaseNotes.IsMajor(version) ? ownNotes ?? NotesOf(version) : NotesOf(major);
            var patches = new List<KeyValuePair<string, string>>();
            foreach (var patch in ReleaseNotes.PatchesBefore(KnownVersions(), version))
            {
                var notes = NotesOf(patch);
                if (notes != null) patches.Add(new KeyValuePair<string, string>(patch, notes));
            }
            return ReleaseNotes.Compose(majorNotes, patches);
        }

        /// <summary>La majeure du cycle d'une version est-elle inconnue ? (Le
        /// vérificateur va alors la chercher sur GitHub.)</summary>
        public static bool MajorMissing(string version)
        {
            if (string.IsNullOrEmpty(version) || !ReleaseNotes.IsPatch(version)) return false;
            return NotesOf(ReleaseNotes.MajorOf(version)) == null;
        }
    }
}

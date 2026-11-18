using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Exchange;

namespace Marabook.Settings
{
    /// <summary>LES PRÉRÉGLAGES D'EXPORT DE L'UTILISATEUR (2.0.0, 18/11/2026,
    /// Rémi) : gardés à l'échelle de l'APPLICATION dans « export.json », à
    /// côté des réglages (il suit --settings) — un préréglage enregistré sert
    /// dans tous les projets. On y retient aussi le dernier préréglage employé.</summary>
    public static class ExportPresetStore
    {
        private static List<ExportPreset> _custom;
        private static string _lastId;

        public static string FilePath()
        {
            return Path.Combine(AppSettings.SettingsFolder(), "export.json");
        }

        /// <summary>Oublie ce qui est chargé (les tests et la sonde changent de dossier de réglages).</summary>
        public static void Reset()
        {
            _custom = null;
            _lastId = null;
        }

        /// <summary>Les préréglages personnalisés, dans leur ordre de création.</summary>
        public static List<ExportPreset> Custom
        {
            get
            {
                if (_custom == null) Load();
                return _custom;
            }
        }

        /// <summary>Tous les préréglages : ceux de la maison, puis les personnalisés.</summary>
        public static List<ExportPreset> All()
        {
            var list = ExportPresets.BuiltIn();
            list.AddRange(Custom);
            return list;
        }

        /// <summary>Le dernier préréglage employé (son identifiant), ou null.</summary>
        public static string LastId
        {
            get
            {
                if (_custom == null) Load();
                return _lastId;
            }
            set
            {
                if (_custom == null) Load();
                if (_lastId == value) return;
                _lastId = value;
                Save();
            }
        }

        /// <summary>Enregistre une configuration sous ce nom : un préréglage
        /// personnalisé du même nom est remplacé (il garde son identifiant),
        /// sinon un nouveau naît. Rend le préréglage gardé.</summary>
        public static ExportPreset SaveAs(ExportPreset options, string name, string note)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "Mon préréglage";
            var preset = options.Clone();
            preset.BuiltIn = false;
            preset.Category = ExportPresets.CategoryCustom;
            preset.Name = name;
            preset.Note = (note ?? "").Trim();
            var custom = Custom;
            var existing = custom.FindIndex(delegate(ExportPreset p) { return string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase); });
            if (existing >= 0)
            {
                preset.Id = custom[existing].Id;
                custom[existing] = preset;
            }
            else
            {
                preset.Id = Guid.NewGuid().ToString("N");
                custom.Add(preset);
            }
            Save();
            return preset;
        }

        public static bool Remove(string id)
        {
            var removed = Custom.RemoveAll(delegate(ExportPreset p) { return p.Id == id; }) > 0;
            if (removed)
            {
                if (_lastId == id) _lastId = null;
                Save();
            }
            return removed;
        }

        public static void Load()
        {
            _custom = new List<ExportPreset>();
            _lastId = null;
            try
            {
                var path = FilePath();
                if (!File.Exists(path)) return;
                var root = Json.AsObject(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                if (root == null) return;
                _lastId = Json.AsString(Json.Field(root, "last"));
                var list = Json.AsList(Json.Field(root, "presets"));
                if (list == null) return;
                var builtIn = ExportPresets.BuiltIn();
                foreach (var node in list)
                {
                    var preset = ExportPreset.FromJson(node);
                    if (preset == null) continue;
                    // Un identifiant de la maison ou déjà pris : le préréglage en reçoit un neuf.
                    if (ExportPresets.Find(builtIn, preset.Id) != null || ExportPresets.Find(_custom, preset.Id) != null) preset.Id = Guid.NewGuid().ToString("N");
                    _custom.Add(preset);
                }
            }
            catch
            {
                // Un fichier illisible ne bloque jamais l'export : on repart des préréglages de la maison.
            }
        }

        public static void Save()
        {
            if (_custom == null) return;
            try
            {
                var presets = new List<object>();
                foreach (var preset in _custom) presets.Add(preset.ToJson());
                var root = new Dictionary<string, object>();
                root["last"] = _lastId;
                root["presets"] = presets;
                var path = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, Json.Write(root), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}

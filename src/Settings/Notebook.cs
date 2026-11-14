using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Marabook.Settings
{
    /// <summary>Une note du Carnet : du Markdown, rien d'autre. Son titre est
    /// sa première ligne.</summary>
    public class NotebookNote
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Text = "";
    }

    /// <summary>LE CARNET (1.2.0, 14/11/2026, Rémi) : des notes en Markdown
    /// gardées à l'échelle de l'APPLICATION — pas du projet — pour avoir une
    /// note précise sous les yeux, quel que soit le projet ouvert. Rangé dans
    /// « carnet.json », à côté des réglages (il suit --settings) : hors de
    /// settings.json pour qu'une longue note n'alourdisse pas chaque
    /// enregistrement des réglages.</summary>
    public static class Notebook
    {
        public const string Untitled = "Note sans titre";

        private static List<NotebookNote> _notes;
        private static string _currentId;

        public static string FilePath()
        {
            return Path.Combine(AppSettings.SettingsFolder(), "carnet.json");
        }

        /// <summary>Oublie ce qui est chargé (les tests et la sonde changent de dossier de réglages).</summary>
        public static void Reset()
        {
            _notes = null;
            _currentId = null;
        }

        /// <summary>Les notes, dans leur ordre de création — jamais vide : un
        /// carnet neuf a sa première page.</summary>
        public static List<NotebookNote> Notes
        {
            get
            {
                if (_notes == null) Load();
                return _notes;
            }
        }

        /// <summary>La note sous les yeux.</summary>
        public static NotebookNote Current
        {
            get
            {
                var notes = Notes;
                foreach (var note in notes) if (note.Id == _currentId) return note;
                _currentId = notes[0].Id;
                return notes[0];
            }
            set { if (value != null && Notes.Contains(value)) _currentId = value.Id; }
        }

        public static NotebookNote Add()
        {
            var note = new NotebookNote();
            Notes.Add(note);
            _currentId = note.Id;
            return note;
        }

        /// <summary>Retire une note ; le carnet garde toujours une page (vide au besoin).</summary>
        public static void Remove(NotebookNote note)
        {
            var notes = Notes;
            var index = notes.IndexOf(note);
            if (index < 0) return;
            notes.RemoveAt(index);
            if (notes.Count == 0) notes.Add(new NotebookNote());
            _currentId = notes[Math.Min(index, notes.Count - 1)].Id;
        }

        /// <summary>Le titre d'une note : sa première ligne écrite, sans ses
        /// marques Markdown (« # », « - », « **gras** »…).</summary>
        public static string TitleOf(string text)
        {
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                // Les marques de bloc en tête : titres, citations, puces, cases.
                var changed = true;
                while (changed && line.Length > 0)
                {
                    changed = false;
                    foreach (var mark in new[] { "#", ">", "- [ ]", "- [x]", "- [X]", "-", "*", "+" })
                    {
                        if (!line.StartsWith(mark, StringComparison.Ordinal)) continue;
                        // « *italique* » et « **gras** » ne sont pas des puces.
                        if ((mark == "*" || mark == "-" || mark == "+") && line.Length > 1 && line[1] != ' ') continue;
                        line = line.Substring(mark.Length).TrimStart();
                        changed = true;
                        break;
                    }
                }
                var sb = new StringBuilder();
                foreach (var c in line)
                    if (c != '*' && c != '_' && c != '`' && c != '~' && c != '[' && c != ']') sb.Append(c);
                var title = sb.ToString().Trim();
                if (title.Length == 0) continue; // une ligne vide, un filet « --- »
                var onlyRule = title.Trim('-', '=', ' ').Length == 0;
                if (onlyRule) continue;
                return title.Length > 60 ? title.Substring(0, 59).TrimEnd() + "…" : title;
            }
            return Untitled;
        }

        public static void Load()
        {
            _notes = new List<NotebookNote>();
            _currentId = null;
            try
            {
                var path = FilePath();
                if (File.Exists(path))
                {
                    var root = Json.AsObject(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                    var list = Json.AsList(Json.Field(root, "notes"));
                    if (list != null)
                        foreach (var entry in list)
                        {
                            var o = Json.AsObject(entry);
                            if (o == null) continue;
                            var note = new NotebookNote { Text = (Json.AsString(Json.Field(o, "text")) ?? "").Replace("\r\n", "\n") };
                            var id = Json.AsString(Json.Field(o, "id"));
                            if (!string.IsNullOrEmpty(id)) note.Id = id;
                            _notes.Add(note);
                        }
                    _currentId = Json.AsString(Json.Field(root, "current"));
                }
            }
            catch (Exception) { } // un carnet illisible ne bloque pas l'application
            if (_notes.Count == 0) _notes.Add(new NotebookNote());
        }

        /// <summary>Écrit le carnet (fichier temporaire puis remplacement : une
        /// coupure ne laisse jamais un carnet à moitié écrit). Faux si l'écriture échoue.</summary>
        public static bool Save()
        {
            if (_notes == null) return true;
            try
            {
                var list = new List<object>();
                foreach (var note in _notes)
                {
                    var o = new Dictionary<string, object>();
                    o["id"] = note.Id;
                    o["text"] = note.Text ?? "";
                    list.Add(o);
                }
                var root = new Dictionary<string, object>();
                root["notes"] = list;
                root["current"] = Current.Id;
                var path = FilePath();
                var folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                var temp = path + ".tmp";
                File.WriteAllText(temp, Json.Write(root), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}

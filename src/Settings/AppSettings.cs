using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Input;

namespace UniversSale.Settings
{
    /// <summary>An action that can be rebound to a shortcut. The rebinding dialog
    /// itself arrives later; the table is the source of truth from day one
    /// (pattern ported from Mental-o's ParametresGlobaux).</summary>
    public class ActionDefinition
    {
        public string Id, Category, Name, DefaultGesture;

        public ActionDefinition(string id, string category, string name, string defaultGesture)
        {
            Id = id;
            Category = category;
            Name = name;
            DefaultGesture = defaultGesture;
        }
    }

    /// <summary>Application-wide settings, shared by all projects
    /// (%APPDATA%\Univers Sale\settings.json).</summary>
    public static class AppSettings
    {
        public static readonly ActionDefinition[] Actions =
        {
            new ActionDefinition("new", "Fichier", "Nouveau projet", "Ctrl+N"),
            new ActionDefinition("open", "Fichier", "Ouvrir", "Ctrl+O"),
            new ActionDefinition("save", "Fichier", "Enregistrer", "Ctrl+S"),
            new ActionDefinition("save-as", "Fichier", "Enregistrer sous", "Ctrl+Shift+S"),
            new ActionDefinition("undo", "Édition", "Annuler", "Ctrl+Z"),
            new ActionDefinition("redo", "Édition", "Rétablir", "Ctrl+Y"),
            new ActionDefinition("new-text", "Pile", "Nouvel écrit", "Ctrl+T"),
            new ActionDefinition("new-folder", "Pile", "Nouveau dossier", "Ctrl+Shift+T"),
            new ActionDefinition("rename", "Pile", "Renommer", "F2"),
            new ActionDefinition("delete", "Pile", "Supprimer vers la corbeille", "Delete"),
            new ActionDefinition("empty-trash", "Pile", "Vider la corbeille", null),
            new ActionDefinition("find", "Édition", "Rechercher dans l'écrit", "Ctrl+F"),
            new ActionDefinition("project-search", "Édition", "Rechercher dans le projet", "Ctrl+Shift+F"),
            new ActionDefinition("new-sheet", "Pile", "Nouvelle fiche", "Ctrl+Shift+K"),
            new ActionDefinition("import-media", "Pile", "Importer dans Recherche", null),
            new ActionDefinition("insert-link", "Format", "Lien vers une fiche", "Ctrl+K"),
            new ActionDefinition("templates", "Format", "Modèles de fiches", null),
            new ActionDefinition("import-docs", "Fichier", "Importer des documents", null),
            new ActionDefinition("import-scrivener", "Fichier", "Importer un projet Scrivener", null),
            new ActionDefinition("export-item", "Fichier", "Exporter l'écrit sélectionné", "Ctrl+E"),
            new ActionDefinition("compile", "Fichier", "Compiler le manuscrit", "Ctrl+Shift+E"),
            new ActionDefinition("export-pdf", "Fichier", "Exporter en PDF prêt à imprimer", null),
            new ActionDefinition("styles", "Format", "Gérer les styles", null),
            new ActionDefinition("insert-footnote", "Format", "Note de bas de page", "Ctrl+Shift+N"),
            new ActionDefinition("insert-image", "Format", "Insérer une image", null),
            new ActionDefinition("insert-rule", "Format", "Ligne horizontale", null),
            new ActionDefinition("insert-separator", "Format", "Séparateur de scène", null),
            new ActionDefinition("page-break", "Mise en page", "Saut de page", "Ctrl+Return"),
            new ActionDefinition("project-settings", "Fichier", "Paramètres du projet", null),
            new ActionDefinition("print-preview", "Fichier", "Aperçu des pages", "Ctrl+Alt+P"),
            new ActionDefinition("print", "Fichier", "Imprimer", "Ctrl+P"),
            new ActionDefinition("session-goal", "Écriture", "Objectif de session", null),
            new ActionDefinition("toggle-binder", "Affichage", "Afficher la Pile", "Ctrl+D1"),
            new ActionDefinition("toggle-inspector", "Affichage", "Afficher l'inspecteur", "Ctrl+D2"),
            new ActionDefinition("dark-theme", "Affichage", "Thème sombre", "Ctrl+Shift+L"),
        };

        public static Dictionary<string, string> Shortcuts = new Dictionary<string, string>();
        public static bool DarkTheme;
        public static bool BinderVisible = true;
        public static bool InspectorVisible = true;
        public static double BinderWidth = 260;
        public static double InspectorWidth = 280;
        public static double Zoom = 100; // page zoom, percent (50–300)
        public static bool ShowFormattingMarks; // ¶ printing characters
        public static bool CompositionMode = true; // write in the composed pages by default
        public static List<string> RecentFiles = new List<string>(); // last 5 .plot files

        public static void AddRecentFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            RecentFiles.RemoveAll(delegate(string existing)
            {
                return string.Equals(existing, path, StringComparison.OrdinalIgnoreCase);
            });
            RecentFiles.Insert(0, path);
            while (RecentFiles.Count > 5)
                RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }

        private static string SettingsPath()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Univers Sale");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "settings.json");
        }

        public static ActionDefinition Definition(string id)
        {
            foreach (var action in Actions)
                if (action.Id == id) return action;
            return null;
        }

        /// <summary>The effective gesture of an action: customized, else default.</summary>
        public static string Gesture(string id)
        {
            string gesture;
            if (Shortcuts.TryGetValue(id, out gesture)) return gesture;
            var definition = Definition(id);
            return definition == null ? null : definition.DefaultGesture;
        }

        public static void Load()
        {
            try
            {
                var path = SettingsPath();
                if (!File.Exists(path)) return;
                var root = Json.AsObject(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                if (root == null) return;

                var shortcuts = Json.AsObject(Json.Field(root, "shortcuts"));
                if (shortcuts != null)
                {
                    Shortcuts = new Dictionary<string, string>();
                    foreach (var kv in shortcuts)
                        if (kv.Value is string) Shortcuts[kv.Key] = (string)kv.Value;
                }
                DarkTheme = Json.AsBool(Json.Field(root, "darkTheme"), false);
                BinderVisible = Json.AsBool(Json.Field(root, "binderVisible"), true);
                InspectorVisible = Json.AsBool(Json.Field(root, "inspectorVisible"), true);
                BinderWidth = Json.AsDouble(Json.Field(root, "binderWidth"), 260);
                InspectorWidth = Json.AsDouble(Json.Field(root, "inspectorWidth"), 280);
                Zoom = Json.AsDouble(Json.Field(root, "zoom"), 100);
                if (Zoom < 50) Zoom = 50;
                if (Zoom > 300) Zoom = 300;
                ShowFormattingMarks = Json.AsBool(Json.Field(root, "formattingMarks"), false);
                CompositionMode = Json.AsBool(Json.Field(root, "compositionMode"), true);
                var recents = Json.AsList(Json.Field(root, "recentFiles"));
                if (recents != null)
                {
                    RecentFiles = new List<string>();
                    foreach (var entry in recents)
                        if (entry is string) RecentFiles.Add((string)entry);
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var root = new Dictionary<string, object>();
                root["shortcuts"] = new Dictionary<string, object>(ToObjectDict(Shortcuts));
                root["darkTheme"] = DarkTheme;
                root["binderVisible"] = BinderVisible;
                root["inspectorVisible"] = InspectorVisible;
                root["binderWidth"] = BinderWidth;
                root["inspectorWidth"] = InspectorWidth;
                root["zoom"] = Zoom;
                root["formattingMarks"] = ShowFormattingMarks;
                root["compositionMode"] = CompositionMode;
                root["recentFiles"] = new List<object>(RecentFiles.ToArray());
                File.WriteAllText(SettingsPath(), Json.Write(root), new UTF8Encoding(false));
            }
            catch { }
        }

        private static Dictionary<string, object> ToObjectDict(Dictionary<string, string> source)
        {
            var result = new Dictionary<string, object>();
            foreach (var kv in source) result[kv.Key] = kv.Value;
            return result;
        }

        // ------------------------------------------------------- gestures

        /// <summary>Invariant storage ("Ctrl+Shift+G") to French display ("Ctrl+Maj+G").</summary>
        public static string DisplayGesture(string gesture)
        {
            if (string.IsNullOrEmpty(gesture)) return "";
            var parts = gesture.Split('+');
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                if (p == "Shift") parts[i] = "Maj";
                else if (p == "Delete") parts[i] = "Suppr";
                else if (p == "Return") parts[i] = "Entrée";
                else if (p == "Add") parts[i] = "+ (pavé)";
                else if (p == "Subtract") parts[i] = "- (pavé)";
                else if (p == "OemPlus") parts[i] = "=";
                else if (p == "OemMinus") parts[i] = "-";
                else if (p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1])) parts[i] = p[1].ToString();
            }
            return string.Join("+", parts);
        }

        public static bool ParseGesture(string gesture, out Key key, out ModifierKeys modifiers)
        {
            key = Key.None;
            modifiers = ModifierKeys.None;
            if (string.IsNullOrEmpty(gesture)) return false;
            var parts = gesture.Split('+');
            foreach (var raw in parts)
            {
                var part = raw.Trim();
                if (part == "Ctrl") modifiers |= ModifierKeys.Control;
                else if (part == "Shift" || part == "Maj") modifiers |= ModifierKeys.Shift;
                else if (part == "Alt") modifiers |= ModifierKeys.Alt;
                else
                {
                    try { key = (Key)Enum.Parse(typeof(Key), part, true); }
                    catch { return false; }
                }
            }
            return key != Key.None;
        }
    }
}

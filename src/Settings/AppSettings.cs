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
            new ActionDefinition("new-book", "Pile", "Nouveau livre", null),
            new ActionDefinition("rename", "Pile", "Renommer", "F2"),
            new ActionDefinition("delete", "Pile", "Supprimer vers la corbeille", "Delete"),
            new ActionDefinition("empty-trash", "Pile", "Vider la corbeille", null),
            new ActionDefinition("find", "Édition", "Rechercher dans l'écrit", "Ctrl+F"),
            new ActionDefinition("project-search", "Édition", "Rechercher dans le projet", "Ctrl+Shift+F"),
            new ActionDefinition("search-next", "Édition", "Occurrence suivante", "F3"),
            new ActionDefinition("search-previous", "Édition", "Occurrence précédente", "Shift+F3"),
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
            new ActionDefinition("preferences", "Fichier", "Préférences de l'application", null),
            new ActionDefinition("print-preview", "Fichier", "Aperçu des pages", "Ctrl+Alt+P"),
            new ActionDefinition("print", "Fichier", "Imprimer", "Ctrl+P"),
            new ActionDefinition("session-goal", "Écriture", "Objectif de session", null),
            new ActionDefinition("toggle-binder", "Affichage", "Afficher la Pile", "Ctrl+D1"),
            new ActionDefinition("toggle-inspector", "Affichage", "Afficher l'inspecteur", "Ctrl+D2"),
            new ActionDefinition("dark-theme", "Affichage", "Thème sombre", "Ctrl+Shift+L"),
            new ActionDefinition("toggle-rulers", "Affichage", "Règles", "Ctrl+R"),
        };

        public static Dictionary<string, string> Shortcuts = new Dictionary<string, string>();
        public static bool DarkTheme;
        public static bool BinderVisible = true;
        public static bool InspectorVisible = true;
        public static double BinderWidth = 260;
        public static double InspectorWidth = 280;
        public static double Zoom = 100; // page zoom, percent (50–300)
        public static bool ShowFormattingMarks; // ¶ printing characters
        public static bool ShowRulers;          // règles cm (Ctrl+R)
        // GEL DU CLASSIQUE (batch 26) : le composé est LA surface d'édition.
        // true = repli « mode de compatibilité » (Préférences) : l'ancienne
        // surface RichTextBox, pour la saisie IME et le SpellCheck Windows —
        // sans correction Marabook, sans approche, sans bulles, sans gabarits
        // à l'écran. L'ancienne clé compositionMode n'est plus lue.
        public static bool ClassicCompatibility;
        public static bool DraftView; // axe d'affichage : Brouillon plutôt que Pages
        public static string AccentColor;    // "#RRGGBB", null = default indigo
        public static bool WhitePaperInDark; // keep white pages under the dark theme
        public static bool StatsExpanded;    // « Statistiques » accordion of the inspector
        public static bool ShowAnnotations = true; // teintes + bulles de révision
        public static bool ProofEnabled = true; // vérification continue (Révision)
        public static bool CorrectionPanelVisible; // « Détails de correction » à droite (b28)
        public static bool SearchPanelVisible;     // le panneau de recherche du projet (b37)
        public static bool VersionsPanelVisible;   // le panneau Versions (b38)
        public static int SnapshotCap = 20;        // instantanés gardés par item (b38, 5–100)
        public static bool DailySnapshot = true;   // capture à la première modification du jour (b38)
        // La grammaire (batch 29) : interrupteur maître de Grammalecte, et
        // les choix d'options de l'utilisateur PAR-DESSUS la politique de
        // recouvrement du lot C (clé = nom d'option Grammalecte). Une entrée
        // absente = le défaut Marabook s'applique.
        public static bool GrammarEnabled = true;
        // Options du correcteur (batch 33) : ce qu'il RELÈVE — orthographe et
        // grammaire actives par défaut, typographie et style à la demande.
        public static bool SpellEnabled = true;
        public static bool TypographyEnabled;
        public static bool StyleEnabled;
        // La passe typographique (batch 34, port de Typonanny) : préréglage et règles.
        public static Correction.TypographyOptions Typography = new Correction.TypographyOptions();
        public static Dictionary<string, bool> GrammarOptions
            = new Dictionary<string, bool>();
        // Mots ignorés par les correcteurs sur TOUS les projets (« ignorer
        // partout ») — le pendant global de Project.ProofIgnored.
        public static List<string> ProofIgnored = new List<string>();
        // Dictionnaire personnel GLOBAL : mots enseignés pour tous les
        // projets — le pendant de Project.LearnedWords.
        public static List<Model.LexiconEntry> Lexicon = new List<Model.LexiconEntry>();
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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "Marabook");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "settings.json");
            // Rebrand : migration douce depuis « Univers Sale » (réglages,
            // récents, icônes persos) — une seule fois, sans rien détruire.
            if (!File.Exists(path))
            {
                var legacy = Path.Combine(appData, "Univers Sale");
                try
                {
                    var legacySettings = Path.Combine(legacy, "settings.json");
                    if (File.Exists(legacySettings)) File.Copy(legacySettings, path);
                    var legacyIcons = Path.Combine(legacy, "icons");
                    var icons = Path.Combine(folder, "icons");
                    if (Directory.Exists(legacyIcons) && !Directory.Exists(icons))
                    {
                        Directory.CreateDirectory(icons);
                        foreach (var file in Directory.GetFiles(legacyIcons))
                            File.Copy(file, Path.Combine(icons, Path.GetFileName(file)), true);
                    }
                }
                catch { }
            }
            return path;
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
                ShowRulers = Json.AsBool(Json.Field(root, "rulers"), false);
                ClassicCompatibility = Json.AsBool(Json.Field(root, "classicCompatibility"), false);
                DraftView = Json.AsBool(Json.Field(root, "draftView"), false);
                AccentColor = Json.AsString(Json.Field(root, "accentColor"));
                WhitePaperInDark = Json.AsBool(Json.Field(root, "whitePaperInDark"), false);
                StatsExpanded = Json.AsBool(Json.Field(root, "statsExpanded"), false);
                ShowAnnotations = Json.AsBool(Json.Field(root, "showAnnotations"), true);
                ProofEnabled = Json.AsBool(Json.Field(root, "proofEnabled"), true);
                CorrectionPanelVisible = Json.AsBool(Json.Field(root, "correctionPanel"), false);
                SearchPanelVisible = Json.AsBool(Json.Field(root, "searchPanel"), false);
                VersionsPanelVisible = Json.AsBool(Json.Field(root, "versionsPanel"), false);
                SnapshotCap = (int)Json.AsDouble(Json.Field(root, "snapshotCap"), 20);
                if (SnapshotCap < 5) SnapshotCap = 5;
                if (SnapshotCap > 100) SnapshotCap = 100;
                DailySnapshot = Json.AsBool(Json.Field(root, "dailySnapshot"), true);
                GrammarEnabled = Json.AsBool(Json.Field(root, "grammarEnabled"), true);
                SpellEnabled = Json.AsBool(Json.Field(root, "spellEnabled"), true);
                TypographyEnabled = Json.AsBool(Json.Field(root, "typographyEnabled"), false);
                StyleEnabled = Json.AsBool(Json.Field(root, "styleEnabled"), false);
                Typography = Correction.TypographyOptions.FromJson(Json.AsObject(Json.Field(root, "typography")));
                var grammarOptions = Json.AsObject(Json.Field(root, "grammarOptions"));
                if (grammarOptions != null)
                {
                    GrammarOptions = new Dictionary<string, bool>();
                    foreach (var pair in grammarOptions)
                        if (pair.Value is bool)
                            GrammarOptions[pair.Key] = (bool)pair.Value;
                }
                var proofIgnored = Json.AsList(Json.Field(root, "proofIgnored"));
                if (proofIgnored != null)
                {
                    ProofIgnored = new List<string>();
                    foreach (var entry in proofIgnored)
                        if (entry is string) ProofIgnored.Add((string)entry);
                }
                Lexicon = Model.LexiconEntry.FromJsonList(Json.AsList(Json.Field(root, "lexicon")));
                var learnedWords = Json.AsList(Json.Field(root, "learnedWords"));
                if (learnedWords != null) // réglages d'avant le batch 33
                {
                    var words = new List<string>();
                    foreach (var entry in learnedWords)
                        if (entry is string) words.Add((string)entry);
                    Model.LexiconEntry.MergeWords(Lexicon, words);
                }
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
                root["rulers"] = ShowRulers;
                root["classicCompatibility"] = ClassicCompatibility;
                root["draftView"] = DraftView;
                if (AccentColor != null) root["accentColor"] = AccentColor;
                root["whitePaperInDark"] = WhitePaperInDark;
                root["statsExpanded"] = StatsExpanded;
                root["showAnnotations"] = ShowAnnotations;
                root["proofEnabled"] = ProofEnabled;
                root["correctionPanel"] = CorrectionPanelVisible;
                root["searchPanel"] = SearchPanelVisible;
                root["versionsPanel"] = VersionsPanelVisible;
                root["snapshotCap"] = SnapshotCap;
                root["dailySnapshot"] = DailySnapshot;
                root["grammarEnabled"] = GrammarEnabled;
                root["spellEnabled"] = SpellEnabled;
                root["typographyEnabled"] = TypographyEnabled;
                root["styleEnabled"] = StyleEnabled;
                root["typography"] = Typography.ToJson();
                if (GrammarOptions.Count > 0)
                {
                    var grammarOptions = new Dictionary<string, object>();
                    foreach (var pair in GrammarOptions)
                        grammarOptions[pair.Key] = pair.Value;
                    root["grammarOptions"] = grammarOptions;
                }
                if (ProofIgnored.Count > 0)
                    root["proofIgnored"] = new List<object>(ProofIgnored.ToArray());
                if (Lexicon.Count > 0)
                    root["lexicon"] = Model.LexiconEntry.ToJsonList(Lexicon);
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

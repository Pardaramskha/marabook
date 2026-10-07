using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Model;

namespace Marabook.Settings
{
    /// <summary>Les modificateurs d'un raccourci, sans System.Windows.Input
    /// (portage Avalonia, P0). La vue traduit ceux de sa plate-forme.</summary>
    [Flags]
    public enum KeyModifiers
    {
        None = 0,
        Control = 1,
        Shift = 2,
        Alt = 4
    }

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
    /// (%APPDATA%\Marabook\settings.json).</summary>
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
            new ActionDefinition("versions-panel", "Édition", "Versions de l'écrit", "Ctrl+Shift+H"),
            new ActionDefinition("new-sheet", "Pile", "Nouvelle fiche", "Ctrl+Shift+K"),
            new ActionDefinition("import-media", "Pile", "Importer dans Recherche", null),
            new ActionDefinition("insert-link", "Format", "Lien vers une fiche", "Ctrl+K"),
            new ActionDefinition("templates", "Format", "Modèles de fiches", null),
            new ActionDefinition("import-docs", "Fichier", "Importer des documents", null),
            new ActionDefinition("import-scrivener", "Fichier", "Importer un projet Scrivener", null),
            new ActionDefinition("export-item", "Fichier", "Exporter l'écrit sélectionné", "Ctrl+E"),
            new ActionDefinition("compile", "Fichier", "Compiler les écrits", "Ctrl+Shift+E"),
            new ActionDefinition("export-pdf", "Fichier", "Exporter en PDF prêt à imprimer", null),
            new ActionDefinition("styles", "Format", "Gérer les styles", null),
            new ActionDefinition("insert-footnote", "Format", "Note de bas de page", "Ctrl+Shift+N"),
            new ActionDefinition("insert-image", "Format", "Insérer une image", null),
            new ActionDefinition("insert-rule", "Format", "Ligne horizontale", null),
            new ActionDefinition("insert-separator", "Format", "Séparateur de scène", null),
            new ActionDefinition("page-break", "Mise en page", "Saut de page", "Ctrl+Return"),
            new ActionDefinition("preferences", "Fichier", "Préférences de l'application", null),
            new ActionDefinition("print-preview", "Composition", "Aperçu des pages", "Ctrl+Alt+P"), // le ruban, plus le menu (22/09)
            new ActionDefinition("print", "Fichier", "Imprimer", "Ctrl+P"),
            new ActionDefinition("export-epub", "Fichier", "Créer un EPUB", null),
            new ActionDefinition("session-goal", "Écriture", "Lancer un sprint", null),
            new ActionDefinition("toggle-binder", "Affichage", "Afficher la Pile", "Ctrl+D1"),
            new ActionDefinition("toggle-inspector", "Affichage", "Afficher l'inspecteur", "Ctrl+D2"),
            new ActionDefinition("dark-theme", "Affichage", "Thème sombre", "Ctrl+Shift+L"),
            new ActionDefinition("toggle-rulers", "Affichage", "Règles", "Ctrl+R"),
            // L'éditeur (22/09) : les gestes de la surface composée, jusque-là
            // câblés (Ctrl+B/I/U), et des fonctions sans raccourci auxquelles
            // on peut en donner un. Résolus par EditorActionFor à la frappe.
            new ActionDefinition("bold", EditorCategory, "Gras", "Ctrl+B"),
            new ActionDefinition("italic", EditorCategory, "Italique", "Ctrl+I"),
            new ActionDefinition("underline", EditorCategory, "Souligné", "Ctrl+U"),
            new ActionDefinition("strike", EditorCategory, "Barré", null),
            new ActionDefinition("align-left", EditorCategory, "Aligner à gauche", null),
            new ActionDefinition("align-center", EditorCategory, "Centrer", null),
            new ActionDefinition("align-right", EditorCategory, "Aligner à droite", null),
            new ActionDefinition("align-justify", EditorCategory, "Justifier", null),
            new ActionDefinition("list-bullets", EditorCategory, "Liste à puces", null),
            new ActionDefinition("list-numbers", EditorCategory, "Liste numérotée", null),
            new ActionDefinition("check-box", EditorCategory, "Case à cocher", null),
            new ActionDefinition("indent-add", EditorCategory, "Ajouter un décalage", null),
            new ActionDefinition("indent-remove", EditorCategory, "Retirer le décalage", null),
            new ActionDefinition("middle-dot", EditorCategory, "Point médian", null),
            new ActionDefinition("formatting-marks", EditorCategory, "Caractères d'impression", null),
        };

        /// <summary>La catégorie des actions que la surface composée résout
        /// elle-même (plus le saut de page, de « Mise en page »).</summary>
        public const string EditorCategory = "Éditeur";

        /// <summary>L'action de l'éditeur que cette touche déclenche, selon
        /// les raccourcis personnalisés puis les défauts — null si aucune.
        /// Les modificateurs doivent correspondre exactement (Ctrl+B ne
        /// répond pas à Ctrl+Maj+B).</summary>
        public static string EditorActionFor(string key, KeyModifiers modifiers)
        {
            if (string.IsNullOrEmpty(key) || SameKey(key, "None")) return null;
            modifiers &= KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt;
            foreach (var action in Actions)
            {
                if (action.Category != EditorCategory && action.Id != "page-break") continue;
                string wanted;
                KeyModifiers wantedModifiers;
                if (!ParseGesture(Gesture(action.Id), out wanted, out wantedModifiers)) continue;
                if (SameKey(wanted, key) && wantedModifiers == modifiers) return action.Id;
            }
            return null;
        }

        /// <summary>Deux noms de touche désignent-ils la même touche ? Les
        /// noms sont ceux de l'énumération Key de WPF (Avalonia reprend les
        /// mêmes), sans la casse ; les doublons de l'énumération (Return et
        /// Enter, Prior et PageUp…) sont réconciliés ici, là où Enum.Parse
        /// le faisait par la valeur.</summary>
        public static bool SameKey(string a, string b)
        {
            return string.Equals(CanonicalKey(a), CanonicalKey(b), StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<string, string> KeyAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Enter", "Return" }, { "CapsLock", "Capital" }, { "PageUp", "Prior" },
                { "PageDown", "Next" }, { "PrintScreen", "Snapshot" }, { "Esc", "Escape" },
                { "Backspace", "Back" }, { "Del", "Delete" }, { "Ins", "Insert" },
                { "OemSemicolon", "Oem1" }, { "OemQuestion", "Oem2" }, { "OemTilde", "Oem3" },
                { "OemOpenBrackets", "Oem4" }, { "OemPipe", "Oem5" }, { "OemCloseBrackets", "Oem6" },
                { "OemQuotes", "Oem7" }, { "OemBackslash", "Oem102" }
            };

        private static string CanonicalKey(string name)
        {
            if (name == null) return "";
            name = name.Trim();
            string canonical;
            return KeyAliases.TryGetValue(name, out canonical) ? canonical : name;
        }

        public static Dictionary<string, string> Shortcuts = new Dictionary<string, string>();
        public static bool DarkTheme;
        public static bool BinderVisible = true;
        // La colonne de droite (batch 39) : UN champ, quatre panneaux qui
        // s'excluent — voir RightPanel.cs. L'inspecteur est le défaut d'un
        // settings.json neuf ; les anciennes clés inspectorVisible,
        // correctionPanel, searchPanel, versionsPanel sont migrées à la
        // lecture et ne sont plus écrites.
        public static RightPanel RightPanel = RightPanel.Inspector;
        // Le rail VERROUILLÉ (hotfix 1.0.3-a) : la colonne de droite ne
        // s'ouvre ni ne se replie plus d'elle-même (fiche cliquée, panneau
        // indisponible à la navigation) — ouvert ou fermé, c'est à la main.
        public static bool RailLocked;
        public static double BinderWidth = 260;
        public static double InspectorWidth = 260; // = BinderWidth à l'ouverture (b43)
        public static double PinnedWidth = 325;    // l'épinglé au rail : 1,25 × la Pile par défaut (14/09)
        public static double Zoom = 100; // page zoom, percent (50–300)
        public static bool ShowFormattingMarks; // ¶ printing characters
        public static bool ShowRulers;          // règles cm (Ctrl+R)
        // Les clés compositionMode et classicCompatibility ne sont plus lues :
        // le mode classique (RichTextBox) a été retiré le 13/09.
        public static bool DraftView; // axe d'affichage : Brouillon plutôt que Pages
        public static string AccentColor;    // "#RRGGBB", null = default indigo
        // Les styles globaux (22/09) : la feuille de Préférences › Styles
        // globaux (séparateur de scène compris) et son empreinte — null tant
        // qu'aucun projet ne l'a semée (Settings.GlobalStyles).
        public static Model.StyleSheet GlobalStyles;
        public static string GlobalStylesStamp = "";
        // Préférences › Auteur (22/09) : l'auteur·ice par défaut des documents
        // qui ne sont pas des livres, et les métadonnées générales par défaut.
        public static string DefaultAuthor = "";
        public static string DefaultPublisher = "";
        public static string DefaultCollection = "";
        public static bool WhitePaperInDark; // keep white pages under the dark theme
        public static bool StatsExpanded;    // « Statistiques » accordion of the inspector
        public static bool ShowAnnotations = true; // teintes + bulles de révision
        public static bool ImageGrid;               // grille de placement des images (0.50.0)
        // Les [[liens]] du texte (18/09) : marques visibles et texte du lien
        // en évidence, ou marques masquées (défaut). Jamais persisté : chaque
        // session repart cachée, l'insertion d'un lien les montre.
        public static bool ShowLinks;
        public static bool LexiconPinned; // le panneau Lexique tient un onglet du rail (18/09)
        public static bool ProofEnabled = true; // vérification continue (Révision)
        public static int SnapshotCap = 20;        // instantanés gardés par item (b38, 5–100)
        public static bool DailySnapshot = true;   // capture à la première modification du jour (b38)
        // L'auto-sélecteur de mot (0.50.0) : un cliquer-glisser qui déborde
        // du mot de départ sélectionne des mots entiers, façon Word.
        public static bool AutoSelectWord = true;
        // Glisser-déposer de la sélection de texte dans l'éditeur (1.0.3) :
        // tirer une sélection la déplace là où le caret de dépôt se pose.
        public static bool TextDragDrop = true;
        // La vitesse du défilement à la molette (0.50.0) : un multiplicateur
        // du pas de Windows, 0,25 à 3 — Préférences › Personnalisation.
        public static double ScrollSpeed = 1;
        // Les derniers caractères spéciaux insérés (tiroir du ruban, 0.50.0).
        public static List<string> RecentSpecialChars = new List<string>();

        public static void NoteSpecialChar(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            RecentSpecialChars.Remove(text);
            RecentSpecialChars.Insert(0, text);
            while (RecentSpecialChars.Count > 12) RecentSpecialChars.RemoveAt(RecentSpecialChars.Count - 1);
            Save();
        }
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
        // L'étage style (batch 44) : sous l'interrupteur Style, ce qu'il
        // relève — répétitions (les nôtres), adverbes en -ment et verbes
        // ternes (le dictionnaire morphologique de Grammalecte) ; la liste
        // des verbes ternes appartient à l'auteur.
        public static bool StyleRepetitions = true;
        public static bool StyleAdverbs = true;
        public static bool StyleDullVerbs = true;
        public static List<string> DullVerbs
            = new List<string>(Correction.Grammalecte.StyleChecker.DefaultDullVerbs);
        // b45 : les verbes de dialogue (incises), le rayon des répétitions
        // (en mots), et la typographie À LA FRAPPE — ses propres règles,
        // à côté de celles de la passe.
        public static bool StyleDialogue = true;
        public static int RepetitionRadius = 100;
        public static bool TypographyLiveEnabled = true;
        public static Correction.TypographyOptions TypographyLive = DefaultLiveTypography();

        /// <summary>Les règles retenues à la frappe par défaut : tout sauf
        /// les espaces (un second espace, un espace en fin de ligne : on ne
        /// l'efface pas sous les doigts) et les majuscules à accentuer (un
        /// signalement, pas une correction).</summary>
        public static Correction.TypographyOptions DefaultLiveTypography()
        {
            var live = new Correction.TypographyOptions();
            live.Spaces = false;
            live.FlagCapitals = false;
            return live;
        }
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
        // Les dernières polices employées (0.50.0) : la tête de liste du
        // sélecteur de police, 5 au plus, la plus récente d'abord.
        public static List<string> RecentFonts = new List<string>();

        // Le catalogue de polices (0.50.0) : les FAVORITES (un doublon en tête
        // de chaque sélecteur) et les EXCLUES (retirées des sélecteurs) —
        // réglages de l'utilisateur, conservés entre les projets.
        public static List<string> FavoriteFonts = new List<string>();
        public static List<string> ExcludedFonts = new List<string>();

        /// <summary>Les remplacements de polices MANQUANTES (07/10) : famille
        /// demandée (absente de cette machine) → famille installée qui la
        /// remplace partout où elle est demandée (composition, PDF,
        /// impression). Réglage de la machine, pas du projet : la police
        /// manque ICI, le .plot ne change pas. Clés sans casse.</summary>
        public static Dictionary<string, string> FontSubstitutions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>La famille à employer pour ce nom : son remplacement s'il
        /// en a un, sinon lui-même.</summary>
        public static string SubstituteFont(string family)
        {
            string replacement;
            if (family != null && FontSubstitutions.TryGetValue(family.Trim(), out replacement) && !string.IsNullOrEmpty(replacement))
                return replacement;
            return family;
        }

        /// <summary>Favorites ou exclusions changées : les sélecteurs se rebâtissent.</summary>
        public static event Action FontPrefsChanged;

        private static bool ContainsFont(List<string> list, string name)
        {
            foreach (var existing in list)
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsFavoriteFont(string name) { return name != null && ContainsFont(FavoriteFonts, name); }
        public static bool IsExcludedFont(string name) { return name != null && ContainsFont(ExcludedFonts, name); }

        /// <summary>Ajoute ou retire une police des favorites ; rend le nouvel état.</summary>
        public static bool ToggleFavoriteFont(string name)
        {
            return ToggleFont(FavoriteFonts, name);
        }

        /// <summary>Exclut ou réintègre une police ; rend le nouvel état (exclue ?).</summary>
        public static bool ToggleExcludedFont(string name)
        {
            return ToggleFont(ExcludedFonts, name);
        }

        private static bool ToggleFont(List<string> list, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var present = ContainsFont(list, name);
            if (present)
                list.RemoveAll(delegate(string existing) { return string.Equals(existing, name, StringComparison.OrdinalIgnoreCase); });
            else list.Add(name);
            Save();
            var handler = FontPrefsChanged;
            if (handler != null) handler();
            return !present;
        }

        public static void NoteRecentFont(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            RecentFonts.RemoveAll(delegate(string existing)
            {
                return string.Equals(existing, name, StringComparison.OrdinalIgnoreCase);
            });
            RecentFonts.Insert(0, name);
            while (RecentFonts.Count > 5) RecentFonts.RemoveAt(RecentFonts.Count - 1);
            Save();
        }
        // Les succès (12/09/2026) : GLOBAUX à l'utilisateur, id → date
        // d'obtention « yyyy-MM-dd HH:mm » ; plus les compteurs que le projet
        // ne porte pas (suppressions, jours d'usage consécutifs).
        public static Dictionary<string, string> Achievements = new Dictionary<string, string>();
        public static int PermanentlyDeleted; // corbeille vidée, descendants compris
        public static string UsageLastDay; // "yyyy-MM-dd"
        public static int UsageStreak;
        public static int WordsAtMaxZoom;  // mots écrits à 300 %
        public static int WordsInCalm;     // mots écrits en mode calme
        // Écrits vierges : id → premier jour vu vierge (« Page blanche »).
        public static Dictionary<string, string> BlankSince = new Dictionary<string, string>();

        /// <summary>L'état d'origine des succès (Aide › Réinitialiser les
        /// succès, 22/09) : la liste vidée, et les compteurs qui n'existent
        /// que pour eux remis à zéro. L'appelant enregistre.</summary>
        public static void ResetAchievements()
        {
            Achievements.Clear();
            PermanentlyDeleted = 0;
            UsageLastDay = null;
            UsageStreak = 0;
            WordsAtMaxZoom = 0;
            WordsInCalm = 0;
            BlankSince.Clear();
        }

        /// <summary>À chaque lancement : prolonge la série de jours d'usage
        /// consécutifs, ou la fait repartir de un.</summary>
        public static void NoteUsage(DateTime now)
        {
            var today = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var yesterday = now.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (UsageLastDay == today) return;
            UsageStreak = UsageLastDay == yesterday ? UsageStreak + 1 : 1;
            UsageLastDay = today;
        }

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

        /// <summary>Une liste de noms de polices du fichier (sans doublon ni vide).</summary>
        private static List<string> ReadFontList(Dictionary<string, object> root, string key)
        {
            var result = new List<string>();
            var list = Json.AsList(Json.Field(root, key));
            if (list == null) return result;
            foreach (var entry in list)
            {
                var name = Json.AsString(entry);
                if (!string.IsNullOrEmpty(name) && !ContainsFont(result, name)) result.Add(name);
            }
            return result;
        }

        /// <summary>Les tests : un autre fichier que celui de l'utilisateur.</summary>
        public static string PathOverride;

        /// <summary>Ce que le lancement doit dire une fois : réglages repris
        /// de la copie de secours, ou illisibles et mis de côté.</summary>
        public static string LoadNotice = "";

        private static string SettingsPath()
        {
            if (!string.IsNullOrEmpty(PathOverride)) return PathOverride;
            return Path.Combine(Platform.Current.DataFolder, "settings.json");
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

        /// <summary>Les défauts de l'auteur, recopiés vers le modèle (qui ne
        /// lit jamais les réglages).</summary>
        public static void PublishDefaults()
        {
            Model.Defaults.Author = DefaultAuthor ?? "";
            Model.Defaults.Publisher = DefaultPublisher ?? "";
            Model.Defaults.Collection = DefaultCollection ?? "";
        }

        /// <summary>Lit settings.json ; s'il est illisible (écriture coupée,
        /// disque plein), la copie de secours .bak ; si tout échoue, le fichier
        /// fautif est mis de côté (.corrompu-date) plutôt qu'écrasé par des
        /// défauts à la prochaine sauvegarde (revue 22/09).</summary>
        public static void Load()
        {
            LoadNotice = "";
            var path = SettingsPath();
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
            if (TryLoad(path)) return;
            if (TryLoad(path + ".bak"))
            {
                LoadNotice = "Le fichier des réglages était illisible : les réglages ont été repris de sa copie de secours (dernier enregistrement précédent).";
                return;
            }
            if (File.Exists(path))
            {
                var aside = path + ".corrompu-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                try { File.Move(path, aside); } catch { }
                LoadNotice = "Le fichier des réglages était illisible et sa copie de secours aussi : ils ont été mis de côté (" + Path.GetFileName(aside) + ") et les réglages repartent des défauts. Dictionnaire personnel global, raccourcis et succès sont à retrouver dans ce fichier.";
            }
        }

        private static bool TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                var root = Json.AsObject(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                if (root == null) return false;

                var shortcuts = Json.AsObject(Json.Field(root, "shortcuts"));
                if (shortcuts != null)
                {
                    Shortcuts = new Dictionary<string, string>();
                    foreach (var kv in shortcuts)
                        if (kv.Value is string) Shortcuts[kv.Key] = (string)kv.Value;
                }
                DarkTheme = Json.AsBool(Json.Field(root, "darkTheme"), false);
                BinderVisible = Json.AsBool(Json.Field(root, "binderVisible"), true);
                RailLocked = Json.AsBool(Json.Field(root, "railLocked"), false);
                var rightPanel = Json.AsString(Json.Field(root, "rightPanel"));
                RightPanel = rightPanel != null
                    ? RightPanels.Parse(rightPanel)
                    : RightPanels.Migrate(
                        Json.AsBool(Json.Field(root, "inspectorVisible"), true),
                        Json.AsBool(Json.Field(root, "correctionPanel"), false),
                        Json.AsBool(Json.Field(root, "searchPanel"), false),
                        Json.AsBool(Json.Field(root, "versionsPanel"), false));
                BinderWidth = Json.AsDouble(Json.Field(root, "binderWidth"), 260);
                // La colonne de droite a sa largeur à elle (30/09) : celle que
                // l'utilisateur a réglée, persistée — la Pile par défaut.
                InspectorWidth = Json.AsDouble(Json.Field(root, "inspectorWidth"), BinderWidth);
                if (InspectorWidth < 120) InspectorWidth = BinderWidth;
                PinnedWidth = Json.AsDouble(Json.Field(root, "pinnedWidth"), BinderWidth * 1.25);
                if (PinnedWidth < 200) PinnedWidth = BinderWidth * 1.25;
                Zoom = Json.AsDouble(Json.Field(root, "zoom"), 100);
                if (Zoom < 50) Zoom = 50;
                if (Zoom > 300) Zoom = 300;
                ShowFormattingMarks = Json.AsBool(Json.Field(root, "formattingMarks"), false);
                ShowRulers = Json.AsBool(Json.Field(root, "rulers"), false);
                DraftView = Json.AsBool(Json.Field(root, "draftView"), false);
                AccentColor = Json.AsString(Json.Field(root, "accentColor"));
                var globalStyles = Json.Field(root, "globalStyles");
                GlobalStyles = globalStyles != null ? Persistence.PlotFile.ReadStylesNode(globalStyles) : null;
                if (GlobalStyles != null) GlobalStyles.EnsureFootnoteStyle(); // réglages d'avant la 0.50.0
                GlobalStylesStamp = Json.AsString(Json.Field(root, "globalStylesStamp")) ?? "";
                DefaultAuthor = Json.AsString(Json.Field(root, "defaultAuthor")) ?? "";
                DefaultPublisher = Json.AsString(Json.Field(root, "defaultPublisher")) ?? "";
                DefaultCollection = Json.AsString(Json.Field(root, "defaultCollection")) ?? "";
                PublishDefaults();
                WhitePaperInDark = Json.AsBool(Json.Field(root, "whitePaperInDark"), false);
                StatsExpanded = Json.AsBool(Json.Field(root, "statsExpanded"), false);
                ShowAnnotations = Json.AsBool(Json.Field(root, "showAnnotations"), true);
                ImageGrid = Json.AsBool(Json.Field(root, "imageGrid"), false);
                LexiconPinned = Json.AsBool(Json.Field(root, "lexiconPinned"), false);
                ProofEnabled = Json.AsBool(Json.Field(root, "proofEnabled"), true);
                SnapshotCap = (int)Json.AsDouble(Json.Field(root, "snapshotCap"), 20);
                if (SnapshotCap < 5) SnapshotCap = 5;
                if (SnapshotCap > 100) SnapshotCap = 100;
                DailySnapshot = Json.AsBool(Json.Field(root, "dailySnapshot"), true);
                AutoSelectWord = Json.AsBool(Json.Field(root, "autoSelectWord"), true);
                TextDragDrop = Json.AsBool(Json.Field(root, "textDragDrop"), true);
                ScrollSpeed = Json.AsDouble(Json.Field(root, "scrollSpeed"), 1);
                if (ScrollSpeed < 0.25 || ScrollSpeed > 3) ScrollSpeed = 1;
                var specials = Json.AsList(Json.Field(root, "recentSpecialChars"));
                if (specials != null)
                {
                    RecentSpecialChars = new List<string>();
                    foreach (var entry in specials)
                    {
                        var text = Json.AsString(entry);
                        if (!string.IsNullOrEmpty(text) && RecentSpecialChars.Count < 12) RecentSpecialChars.Add(text);
                    }
                }
                GrammarEnabled = Json.AsBool(Json.Field(root, "grammarEnabled"), true);
                SpellEnabled = Json.AsBool(Json.Field(root, "spellEnabled"), true);
                TypographyEnabled = Json.AsBool(Json.Field(root, "typographyEnabled"), false);
                StyleEnabled = Json.AsBool(Json.Field(root, "styleEnabled"), false);
                StyleRepetitions = Json.AsBool(Json.Field(root, "styleRepetitions"), true);
                StyleAdverbs = Json.AsBool(Json.Field(root, "styleAdverbs"), true);
                StyleDullVerbs = Json.AsBool(Json.Field(root, "styleDullVerbs"), true);
                // Repliés en minuscules : le pont compare les lemmes tels
                // quels, un « Être » tapé à la main ne relèverait rien.
                var dullVerbs = Correction.Grammalecte.StyleChecker.ParseDullVerbs(
                    string.Join(",", Json.AsStringList(Json.Field(root, "dullVerbs")).ToArray()));
                if (dullVerbs.Count > 0) DullVerbs = dullVerbs;
                Typography = Correction.TypographyOptions.FromJson(Json.AsObject(Json.Field(root, "typography")));
                StyleDialogue = Json.AsBool(Json.Field(root, "styleDialogue"), true);
                RepetitionRadius = (int)Json.AsDouble(Json.Field(root, "repetitionRadius"), 100);
                if (RepetitionRadius < 20) RepetitionRadius = 20;
                if (RepetitionRadius > 500) RepetitionRadius = 500;
                TypographyLiveEnabled = Json.AsBool(Json.Field(root, "typographyLiveEnabled"), true);
                var live = Json.AsObject(Json.Field(root, "typographyLive"));
                TypographyLive = live == null ? DefaultLiveTypography() : Correction.TypographyOptions.FromJson(live);
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
                var recentFonts = Json.AsList(Json.Field(root, "recentFonts")); // 0.50.0
                if (recentFonts != null)
                {
                    RecentFonts = new List<string>();
                    foreach (var entry in recentFonts)
                    {
                        var name = Json.AsString(entry);
                        if (!string.IsNullOrEmpty(name) && RecentFonts.Count < 5) RecentFonts.Add(name);
                    }
                }
                FavoriteFonts = ReadFontList(root, "favoriteFonts"); // 0.50.0, catalogue de polices
                ExcludedFonts = ReadFontList(root, "excludedFonts");
                FontSubstitutions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var substitutions = Json.AsObject(Json.Field(root, "fontSubstitutions"));
                if (substitutions != null)
                    foreach (var kv in substitutions)
                        if (kv.Value is string && !string.IsNullOrEmpty(kv.Key)) FontSubstitutions[kv.Key] = (string)kv.Value;
                var recents = Json.AsList(Json.Field(root, "recentFiles"));
                if (recents != null)
                {
                    RecentFiles = new List<string>();
                    foreach (var entry in recents)
                        if (entry is string) RecentFiles.Add((string)entry);
                }
                var achievements = Json.AsObject(Json.Field(root, "achievements"));
                if (achievements != null)
                {
                    Achievements = new Dictionary<string, string>();
                    foreach (var pair in achievements)
                    {
                        if (!(pair.Value is string)) continue;
                        string renamed; // la première liste du 12/09 avait d'autres ids
                        var id = Model.Achievements.RenamedIds.TryGetValue(pair.Key, out renamed) ? renamed : pair.Key;
                        Achievements[id] = (string)pair.Value;
                    }
                }
                PermanentlyDeleted = (int)Json.AsDouble(Json.Field(root, "permanentlyDeleted"), 0);
                UsageLastDay = Json.AsString(Json.Field(root, "usageLastDay"));
                UsageStreak = (int)Json.AsDouble(Json.Field(root, "usageStreak"), 0);
                WordsAtMaxZoom = (int)Json.AsDouble(Json.Field(root, "wordsAtMaxZoom"), 0);
                WordsInCalm = (int)Json.AsDouble(Json.Field(root, "wordsInCalm"), 0);
                var blank = Json.AsObject(Json.Field(root, "blankSince"));
                if (blank != null)
                {
                    BlankSince = new Dictionary<string, string>();
                    foreach (var pair in blank)
                        if (pair.Value is string) BlankSince[pair.Key] = (string)pair.Value;
                }
                return true;
            }
            catch { return false; }
        }

        public static void Save()
        {
            try
            {
                var root = new Dictionary<string, object>();
                root["shortcuts"] = new Dictionary<string, object>(ToObjectDict(Shortcuts));
                root["darkTheme"] = DarkTheme;
                root["binderVisible"] = BinderVisible;
                root["railLocked"] = RailLocked;
                root["rightPanel"] = RightPanels.Name(RightPanel);
                root["binderWidth"] = BinderWidth;
                root["inspectorWidth"] = InspectorWidth;
                root["pinnedWidth"] = PinnedWidth;
                root["zoom"] = Zoom;
                root["formattingMarks"] = ShowFormattingMarks;
                root["rulers"] = ShowRulers;
                root["draftView"] = DraftView;
                if (AccentColor != null) root["accentColor"] = AccentColor;
                if (GlobalStyles != null) root["globalStyles"] = Persistence.PlotFile.BuildStyles(GlobalStyles);
                if (GlobalStylesStamp.Length > 0) root["globalStylesStamp"] = GlobalStylesStamp;
                if (DefaultAuthor.Length > 0) root["defaultAuthor"] = DefaultAuthor;
                if (DefaultPublisher.Length > 0) root["defaultPublisher"] = DefaultPublisher;
                if (DefaultCollection.Length > 0) root["defaultCollection"] = DefaultCollection;
                PublishDefaults();
                root["whitePaperInDark"] = WhitePaperInDark;
                root["statsExpanded"] = StatsExpanded;
                root["showAnnotations"] = ShowAnnotations;
                root["imageGrid"] = ImageGrid;
                root["lexiconPinned"] = LexiconPinned;
                root["proofEnabled"] = ProofEnabled;
                root["snapshotCap"] = SnapshotCap;
                root["dailySnapshot"] = DailySnapshot;
                root["autoSelectWord"] = AutoSelectWord;
                root["textDragDrop"] = TextDragDrop;
                if (Math.Abs(ScrollSpeed - 1) > 0.001) root["scrollSpeed"] = ScrollSpeed;
                if (RecentSpecialChars.Count > 0) root["recentSpecialChars"] = new List<object>(RecentSpecialChars.ToArray());
                root["grammarEnabled"] = GrammarEnabled;
                root["spellEnabled"] = SpellEnabled;
                root["typographyEnabled"] = TypographyEnabled;
                root["styleEnabled"] = StyleEnabled;
                root["styleRepetitions"] = StyleRepetitions;
                root["styleAdverbs"] = StyleAdverbs;
                root["styleDullVerbs"] = StyleDullVerbs;
                root["dullVerbs"] = new List<object>(DullVerbs.ToArray());
                root["typography"] = Typography.ToJson();
                root["styleDialogue"] = StyleDialogue;
                root["repetitionRadius"] = RepetitionRadius;
                root["typographyLiveEnabled"] = TypographyLiveEnabled;
                root["typographyLive"] = TypographyLive.ToJson();
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
                if (RecentFonts.Count > 0) root["recentFonts"] = new List<object>(RecentFonts.ToArray());
                if (FavoriteFonts.Count > 0) root["favoriteFonts"] = new List<object>(FavoriteFonts.ToArray());
                if (ExcludedFonts.Count > 0) root["excludedFonts"] = new List<object>(ExcludedFonts.ToArray());
                if (FontSubstitutions.Count > 0) root["fontSubstitutions"] = new Dictionary<string, object>(ToObjectDict(FontSubstitutions));
                if (Achievements.Count > 0)
                    root["achievements"] = new Dictionary<string, object>(ToObjectDict(Achievements));
                if (PermanentlyDeleted > 0) root["permanentlyDeleted"] = PermanentlyDeleted;
                if (UsageLastDay != null) root["usageLastDay"] = UsageLastDay;
                if (UsageStreak > 0) root["usageStreak"] = UsageStreak;
                if (WordsAtMaxZoom > 0) root["wordsAtMaxZoom"] = WordsAtMaxZoom;
                if (WordsInCalm > 0) root["wordsInCalm"] = WordsInCalm;
                if (BlankSince.Count > 0)
                    root["blankSince"] = new Dictionary<string, object>(ToObjectDict(BlankSince));
                // Fichier temporaire puis bascule : une coupure en pleine
                // écriture laisse l'ancien fichier intact, et le précédent
                // devient .bak (revue 22/09).
                var path = SettingsPath();
                var temp = path + ".tmp";
                File.WriteAllText(temp, Json.Write(root), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
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

        /// <summary>Les raccourcis s'affichent à la manière de macOS (02/10) :
        /// « Ctrl » des réglages y est la touche Commande, rendue « ⌘ », avec
        /// « ⇧ » et « ⌥ », sans « + » (« ⇧⌘S »). Posé par la vue au démarrage ;
        /// le format ENREGISTRÉ reste « Ctrl+Shift+S » sur les trois systèmes.</summary>
        public static bool MacKeys;

        /// <summary>Invariant storage ("Ctrl+Shift+G") to French display ("Ctrl+Maj+G"),
        /// or macOS display ("⇧⌘G") when MacKeys is set.</summary>
        public static string DisplayGesture(string gesture)
        {
            if (string.IsNullOrEmpty(gesture)) return "";
            if (MacKeys) return DisplayMacGesture(gesture);
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

        /// <summary>« Ctrl+Shift+G » → « ⇧⌘G » : les modificateurs dans l'ordre
        /// de macOS (⌥ ⇧ ⌘), collés à la touche, qui garde ses noms français.</summary>
        private static string DisplayMacGesture(string gesture)
        {
            bool alt = false, shift = false, command = false;
            var key = "";
            foreach (var raw in gesture.Split('+'))
            {
                var p = raw.Trim();
                if (p == "Alt") alt = true;
                else if (p == "Shift" || p == "Maj") shift = true;
                else if (p == "Ctrl") command = true;
                else if (p == "Delete") key = "Suppr";
                else if (p == "Return") key = "Entrée";
                else if (p == "Add") key = "+ (pavé)";
                else if (p == "Subtract") key = "- (pavé)";
                else if (p == "OemPlus") key = "=";
                else if (p == "OemMinus") key = "-";
                else if (p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1])) key = p[1].ToString();
                else if (p.Length > 0) key = p;
            }
            return (alt ? "⌥" : "") + (shift ? "⇧" : "") + (command ? "⌘" : "") + key;
        }

        /// <summary>« Ctrl+Shift+G » → la touche (le nom WPF, tel quel) et ses
        /// modificateurs. La validité du nom de touche est l'affaire de la
        /// vue (MainWindow.TryKey) : le cœur ne connaît pas l'énumération.</summary>
        public static bool ParseGesture(string gesture, out string key, out KeyModifiers modifiers)
        {
            key = null;
            modifiers = KeyModifiers.None;
            if (string.IsNullOrEmpty(gesture)) return false;
            var parts = gesture.Split('+');
            foreach (var raw in parts)
            {
                var part = raw.Trim();
                if (part == "Ctrl") modifiers |= KeyModifiers.Control;
                else if (part == "Shift" || part == "Maj") modifiers |= KeyModifiers.Shift;
                else if (part == "Alt") modifiers |= KeyModifiers.Alt;
                else if (part.Length > 0)
                {
                    if (key != null) return false; // deux touches : geste mal formé
                    key = part;
                }
            }
            return key != null && !SameKey(key, "None");
        }
    }
}

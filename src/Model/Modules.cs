using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Marabook.Model
{
    /// <summary>Un champ d'une fiche de module : libellé en gras, indication
    /// plus petite et grise dessous, zone multiligne (deux lignes par défaut,
    /// extensible à la frappe). Group = un intertitre dans le paper (les
    /// champs qui suivent se rangent dessous).</summary>
    public class ModuleField
    {
        public string Id = "";
        public string Label = "";
        public string Hint = "";
        public string Group = "";
        public int Lines = 2;
    }

    /// <summary>Un paper d'une fiche de module : ses champs ; Columns non vide
    /// = chaque champ se dédouble en une zone par colonne (« Préféré·e·s »,
    /// « Détesté·e·s »), identifiées champ.0, champ.1…</summary>
    public class ModulePaper
    {
        public string Title = "";
        public List<string> Columns = new List<string>();
        public List<ModuleField> Fields = new List<ModuleField>();
    }

    /// <summary>Un succès apporté par un module : sa règle est l'une des
    /// règles connues de Marabook (ModuleRules), son seuil éventuel.</summary>
    public class ModuleAchievement
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public string Rule = "";
        public int Count = 1;
    }

    /// <summary>Un module (DLC) lu de son module.json : identité, ce qu'il
    /// annonce, la fiche qu'il ajoute (aux catégories nommées), ses succès.</summary>
    public class ModuleInfo
    {
        public string Id = "";
        public string Name = "";
        public string Title = "";
        public string Version = "";
        public List<string> Features = new List<string>();
        public List<string> Categories = new List<string>(); // « Personnage »…
        public string Button = "";                           // « Créer une fiche FPDM »
        public string Tab = "";                              // l'onglet de la fiche
        public List<ModulePaper> Papers = new List<ModulePaper>();
        public List<ModuleAchievement> Achievements = new List<ModuleAchievement>();
        // Un module À CODE (22/09) : la DLL du paquet et le type qui
        // implémente Marabook.Extensions.IMarabookModule — null sinon.
        public string EntryAssembly;
        public string EntryType;
        public bool HasCode { get { return !string.IsNullOrEmpty(EntryAssembly) && !string.IsNullOrEmpty(EntryType); } }

        /// <summary>Tous les identifiants de valeur que la fiche attend —
        /// un par champ, ou un par colonne d'un champ à colonnes.</summary>
        public List<string> ValueIds()
        {
            var ids = new List<string>();
            foreach (var paper in Papers)
                foreach (var field in paper.Fields)
                {
                    if (paper.Columns.Count == 0) ids.Add(field.Id);
                    else for (var c = 0; c < paper.Columns.Count; c++) ids.Add(field.Id + "." + c);
                }
            return ids;
        }
    }

    /// <summary>Une entrée du catalogue : un module que Marabook sait aller
    /// chercher (la dernière release GitHub de son dépôt, un asset au nom
    /// stable). Ce que l'accueil montre avant tout téléchargement.</summary>
    public class ModuleSource
    {
        public string Id = "";
        public string Name = "";
        public string Title = "";
        public string Repository = "";   // « Pardaramskha/marabook-dlc-fpdm »
        public string Asset = "";        // « fpdm.mdlc »
        public List<string> Features = new List<string>();
    }

    /// <summary>Les règles de succès qu'un module peut invoquer (le module ne
    /// porte pas de code : il nomme une règle que Marabook sait mesurer).</summary>
    public static class ModuleRules
    {
        /// <summary>Count fiches dont la fiche de module est entièrement remplie.</summary>
        public const string AdvancedComplete = "advanced-complete";
        /// <summary>Une fiche où TOUT est rempli : champs du modèle et libres,
        /// texte libre, relations, graph statistique (activé, chaque axe noté),
        /// suivi et évolution activés (une étape au moins), portrait, et la
        /// fiche de module complète.</summary>
        public const string SheetComplete = "sheet-complete";
        /// <summary>« mindmaps » : au moins N cartes mentales dans le projet (22/09).</summary>
        public const string MindMaps = "mindmaps";
        /// <summary>« mindmap-nodes » : une carte d'au moins N boîtes (22/09).</summary>
        public const string MindMapNodes = "mindmap-nodes";
    }

    /// <summary>LES MODULES (DLC, 22/09/2026) : des paquets .mdlc (un zip :
    /// module.json + images de succès), installés dans le dossier de
    /// l'utilisateur, lus au lancement. Un module n'apporte pas de code —
    /// il déclare une fiche (papers et champs) pour des catégories de fiches,
    /// et des succès sur des règles connues. Sans module installé, rien
    /// n'apparaît nulle part.</summary>
    public static class Modules
    {
        public const string Manifest = "module.json";
        public const string Extension = ".mdlc";

        /// <summary>Le catalogue : les modules connus de cette version.</summary>
        public static readonly ModuleSource[] Catalogue =
        {
            new ModuleSource
            {
                Id = "fpdm",
                Name = "FPDM",
                Title = "Fiches de Personnages pour Dangereux Monomaniaques",
                Repository = "Pardaramskha/marabook-dlc-fpdm",
                Asset = "fpdm.mdlc",
                Features =
                {
                    "Créez des fiches de personnages avancées depuis votre écran d'édition de fiche",
                    "Un système de fiche de personnage particulièrement détaillé avec des champs préconfigurés",
                    "Une gestion du personnage à niveau d'auteur pour mesurer son impact narratif",
                    "Une phénoménale perte de temps pour les plus pointilleux",
                    "De nouveaux succès"
                }
            },
            // Mental-o en DLC (22/09) : un module À CODE — sa DLL apporte les
            // cartes mentales (racine « Cartes mentales », éditeur, tuiles).
            new ModuleSource
            {
                Id = "mental-o",
                Name = "Mental-o",
                Title = "Cartes mentales",
                Repository = "Pardaramskha/marabook-dlc-mental-o",
                Asset = "mental-o.mdlc",
                Features =
                {
                    "Une racine « Cartes mentales » dans la Pile : vos cartes vivent dans le projet",
                    "Le canevas de Mental-o à la sauce Marabook : boîtes, liens, groupes, images, notes",
                    "Chaque carte en tuile sur le corkboard, import et export des .tea",
                    "Trois succès"
                }
            }
        };

        /// <summary>Le dossier des modules installés : %APPDATA%\Marabook\dlc,
        /// ou le dossier posé par les sondes (jamais les données de l'utilisateur).</summary>
        public static string RootOverride;

        public static string Root
        {
            get
            {
                if (RootOverride != null) return RootOverride;
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(Path.Combine(appData, "Marabook"), "dlc");
            }
        }

        public static readonly List<ModuleInfo> Installed = new List<ModuleInfo>();

        /// <summary>Première release (22/09) : les modules ne se téléchargent
        /// pas encore — le catalogue n'interroge pas GitHub, « Installer » et
        /// « Vérifier les versions » sont grisés. « Installer depuis un
        /// fichier .mdlc » et la désinstallation restent. À passer à true
        /// quand les dépôts des modules publient leurs paquets.</summary>
        public static readonly bool DownloadsEnabled = false; // readonly, pas const : le code qui en dépend reste compilé sans avertissement

        /// <summary>Après une installation ou un retrait : la fiche ouverte et
        /// les succès se rafraîchissent.</summary>
        public static event Action Changed;

        // ------------------------------------------------------------ lecture

        public static ModuleInfo Parse(string json)
        {
            var root = Json.AsObject(Json.Parse(json));
            if (root == null) throw new Exception("module.json illisible");
            var module = new ModuleInfo
            {
                Id = Json.AsString(Json.Field(root, "id")) ?? "",
                Name = Json.AsString(Json.Field(root, "name")) ?? "",
                Title = Json.AsString(Json.Field(root, "title")) ?? "",
                Version = Json.AsString(Json.Field(root, "version")) ?? ""
            };
            if (module.Id.Length == 0) throw new Exception("module.json sans identifiant");
            if (module.Name.Length == 0) module.Name = module.Id;
            module.Features.AddRange(Json.AsStringList(Json.Field(root, "features")));
            var entry = Json.AsObject(Json.Field(root, "entry")); // module à code (22/09)
            if (entry != null)
            {
                module.EntryAssembly = Json.AsString(Json.Field(entry, "assembly"));
                module.EntryType = Json.AsString(Json.Field(entry, "type"));
            }
            var sheet = Json.AsObject(Json.Field(root, "sheet"));
            if (sheet != null)
            {
                module.Categories.AddRange(Json.AsStringList(Json.Field(sheet, "categories")));
                module.Button = Json.AsString(Json.Field(sheet, "button")) ?? ("Créer une fiche " + module.Name);
                module.Tab = Json.AsString(Json.Field(sheet, "tab")) ?? module.Name;
                var papers = Json.AsList(Json.Field(sheet, "papers"));
                if (papers != null)
                    foreach (var paperNode in papers)
                    {
                        var p = Json.AsObject(paperNode);
                        if (p == null) continue;
                        var paper = new ModulePaper { Title = Json.AsString(Json.Field(p, "title")) ?? "" };
                        paper.Columns.AddRange(Json.AsStringList(Json.Field(p, "columns")));
                        var fields = Json.AsList(Json.Field(p, "fields"));
                        if (fields != null)
                            foreach (var fieldNode in fields)
                            {
                                var f = Json.AsObject(fieldNode);
                                if (f == null) continue;
                                var field = new ModuleField
                                {
                                    Id = Json.AsString(Json.Field(f, "id")) ?? "",
                                    Label = Json.AsString(Json.Field(f, "label")) ?? "",
                                    Hint = Json.AsString(Json.Field(f, "hint")) ?? "",
                                    Group = Json.AsString(Json.Field(f, "group")) ?? "",
                                    Lines = Math.Max(1, Json.AsInt(Json.Field(f, "lines"), 2))
                                };
                                if (field.Id.Length == 0) throw new Exception("un champ du module n'a pas d'identifiant (" + field.Label + ")");
                                paper.Fields.Add(field);
                            }
                        module.Papers.Add(paper);
                    }
            }
            var achievements = Json.AsList(Json.Field(root, "achievements"));
            if (achievements != null)
                foreach (var node in achievements)
                {
                    var a = Json.AsObject(node);
                    if (a == null) continue;
                    var achievement = new ModuleAchievement
                    {
                        Id = Json.AsString(Json.Field(a, "id")) ?? "",
                        Name = Json.AsString(Json.Field(a, "name")) ?? "",
                        Description = Json.AsString(Json.Field(a, "description")) ?? "",
                        Rule = Json.AsString(Json.Field(a, "rule")) ?? "",
                        Count = Math.Max(1, Json.AsInt(Json.Field(a, "count"), 1))
                    };
                    if (achievement.Id.Length == 0) continue;
                    module.Achievements.Add(achievement);
                }
            return module;
        }

        /// <summary>Lit le manifeste d'un paquet .mdlc sans l'installer.</summary>
        public static ModuleInfo Read(string mdlcPath)
        {
            using (var archive = ZipFile.OpenRead(mdlcPath))
            {
                var entry = archive.GetEntry(Manifest);
                if (entry == null) throw new Exception("Ce fichier n'est pas un module Marabook (" + Manifest + " absent)");
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    return Parse(reader.ReadToEnd());
            }
        }

        /// <summary>Fabrique un paquet : le manifeste, plus les fichiers d'un
        /// dossier optionnel (images de succès). Sert au dépôt du module et
        /// aux tests.</summary>
        public static void Pack(string manifestJson, string extrasDir, string mdlcPath)
        {
            if (File.Exists(mdlcPath)) File.Delete(mdlcPath);
            using (var archive = ZipFile.Open(mdlcPath, ZipArchiveMode.Create))
            {
                var manifest = archive.CreateEntry(Manifest);
                using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                    writer.Write(manifestJson);
                if (extrasDir != null && Directory.Exists(extrasDir))
                    foreach (var file in Directory.GetFiles(extrasDir, "*", SearchOption.AllDirectories))
                    {
                        var relative = file.Substring(extrasDir.Length).TrimStart('\\', '/').Replace('\\', '/');
                        archive.CreateEntryFromFile(file, relative);
                    }
            }
        }

        // ------------------------------------------------------------ installés

        /// <summary>Recharge la liste depuis le dossier : un sous-dossier par
        /// module, son module.json dedans. Un module illisible est ignoré.</summary>
        public static void Load()
        {
            Installed.Clear();
            try
            {
                if (Directory.Exists(Root))
                    foreach (var dir in Directory.GetDirectories(Root))
                    {
                        if (IsCondemned(dir)) continue; // désinstallé, DLL encore verrouillée
                        var manifest = Path.Combine(dir, Manifest);
                        if (!File.Exists(manifest)) continue;
                        try
                        {
                            var module = Parse(File.ReadAllText(manifest, Encoding.UTF8));
                            Installed.Add(module);
                            if (module.HasCode) LoadCode(dir, module);
                        }
                        catch (Exception error) { LastLoadError = error.GetType().Name + " : " + error.Message; }
                    }
            }
            catch { }
            Installed.Sort(delegate(ModuleInfo a, ModuleInfo b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            Achievements.SetModuleAchievements(ModuleAchievements());
        }

        public static ModuleInfo Find(string id)
        {
            foreach (var module in Installed) if (module.Id == id) return module;
            return null;
        }

        public static bool IsInstalled(string id) { return Find(id) != null; }

        /// <summary>Le dossier d'un module installé.</summary>
        public static string DirOf(string id)
        {
            return Path.Combine(Root, id);
        }

        /// <summary>Installe (ou remplace) un module depuis son paquet :
        /// déballé dans dlc\&lt;id&gt;, puis la liste se recharge.</summary>
        public static ModuleInfo Install(string mdlcPath)
        {
            var module = Read(mdlcPath);
            var dir = DirOf(module.Id);
            var fresh = dir + ".tmp";
            if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
            Directory.CreateDirectory(fresh);
            using (var archive = ZipFile.OpenRead(mdlcPath))
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Length == 0) continue; // un dossier
                    var target = Path.GetFullPath(Path.Combine(fresh, entry.FullName.Replace('/', '\\')));
                    if (!target.StartsWith(Path.GetFullPath(fresh), StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.Move(fresh, dir);
            Load();
            RaiseChanged();
            return Find(module.Id) ?? module;
        }

        public static void Uninstall(string id)
        {
            var dir = DirOf(id);
            // La DLL d'un module à code reste chargée jusqu'au redémarrage
            // (un AppDomain ne décharge pas) : le module se retire du
            // registre, ses fichiers partent — sauf verrouillés, alors au
            // prochain lancement.
            Extensions.ModuleRegistry.Unregister(id);
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (IOException) { MarkForRemoval(dir); }
            catch (UnauthorizedAccessException) { MarkForRemoval(dir); }
            Load();
            RaiseChanged();
        }

        /// <summary>Un module dont la DLL, verrouillée, empêche la suppression
        /// du dossier : un marqueur le condamne, le prochain lancement l'efface
        /// (et Load l'ignore d'ici là).</summary>
        public const string RemovalMarker = ".retirer";

        private static void MarkForRemoval(string dir)
        {
            try { File.WriteAllText(Path.Combine(dir, RemovalMarker), DateTime.Now.ToString("s")); } catch { }
        }

        private static bool IsCondemned(string dir)
        {
            if (!File.Exists(Path.Combine(dir, RemovalMarker))) return false;
            try { Directory.Delete(dir, true); } catch { }
            return true;
        }

        /// <summary>Charge la DLL d'un module à code et l'enregistre (une
        /// seule fois par session : recharger la même DLL n'a pas de sens).</summary>
        private static readonly HashSet<string> _loadedAssemblies = new HashSet<string>();

        /// <summary>Le dernier échec de chargement d'un module (manifeste ou DLL) — pour les sondes et le panneau DLC.</summary>
        public static string LastLoadError = "";

        private static bool _resolverInstalled;

        /// <summary>La DLL d'un module est compilée contre « Marabook » ; si le
        /// processus hôte porte un autre nom d'assembly (les exécutables de
        /// tests, qui compilent les mêmes sources), le chargeur reçoit
        /// l'assembly courant à la place — sans quoi le premier échec de
        /// chargement de type serait mémorisé par le CLR pour la session.</summary>
        private static void EnsureResolver()
        {
            if (_resolverInstalled) return;
            _resolverInstalled = true;
            var self = typeof(Modules).Assembly;
            if (self.GetName().Name == "Marabook") return;
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                return args.Name == "Marabook" || args.Name.StartsWith("Marabook,", StringComparison.Ordinal) ? self : null;
            };
        }

        private static void LoadCode(string dir, ModuleInfo module)
        {
            if (Extensions.ModuleRegistry.Find(module.Id) != null) return;
            EnsureResolver();
            var path = Path.GetFullPath(Path.Combine(dir, module.EntryAssembly.Replace('/', '\\')));
            if (!File.Exists(path)) throw new FileNotFoundException("DLL du module introuvable", path);
            var assembly = System.Reflection.Assembly.LoadFrom(path); // la même DLL rend le même assembly
            _loadedAssemblies.Add(path);
            var type = assembly.GetType(module.EntryType, true);
            var instance = Activator.CreateInstance(type) as Extensions.IMarabookModule;
            if (instance == null) throw new InvalidOperationException(module.EntryType + " n'est pas un IMarabookModule");
            Extensions.ModuleRegistry.Register(module.Id, instance);
        }

        private static void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }

        // ------------------------------------------------------------ fiches

        /// <summary>Les modules installés qui visent la catégorie de cette
        /// fiche (« Personnage » vise aussi « Personnages secondaires »).</summary>
        public static List<ModuleInfo> ForSheet(Project project, BinderItem sheet)
        {
            var list = new List<ModuleInfo>();
            if (project == null || sheet == null || sheet.Kind != ItemKind.Sheet) return list;
            var category = project.SheetCategoryOf(sheet);
            if (category == null) return list;
            var name = Correction.FrenchTokenizer.Fold(category.Name.Trim());
            foreach (var module in Installed)
            {
                if (module.Papers.Count == 0) continue;
                foreach (var wanted in module.Categories)
                    if (name.StartsWith(Correction.FrenchTokenizer.Fold(wanted.Trim()), StringComparison.Ordinal))
                    { list.Add(module); break; }
            }
            return list;
        }

        /// <summary>La fiche a-t-elle une fiche de ce module (créée, même vide) ?</summary>
        public static bool HasSheet(ModuleInfo module, BinderItem sheet)
        {
            return sheet != null && module != null && sheet.ModuleValues.ContainsKey(module.Id);
        }

        public static Dictionary<string, string> ValuesOf(ModuleInfo module, BinderItem sheet, bool create)
        {
            Dictionary<string, string> values;
            if (!sheet.ModuleValues.TryGetValue(module.Id, out values))
            {
                if (!create) return null;
                values = new Dictionary<string, string>();
                sheet.ModuleValues[module.Id] = values;
            }
            return values;
        }

        /// <summary>Entièrement remplie : chaque valeur attendue est là, non vide.</summary>
        public static bool IsComplete(ModuleInfo module, BinderItem sheet)
        {
            var values = sheet == null ? null : ValuesOf(module, sheet, false);
            if (values == null) return false;
            var ids = module.ValueIds();
            if (ids.Count == 0) return false;
            foreach (var id in ids)
            {
                string value;
                if (!values.TryGetValue(id, out value) || value == null || value.Trim().Length == 0) return false;
            }
            return true;
        }

        /// <summary>Le nombre de valeurs remplies / attendues (l'indicateur de la fiche).</summary>
        public static int FilledCount(ModuleInfo module, BinderItem sheet)
        {
            var values = sheet == null ? null : ValuesOf(module, sheet, false);
            if (values == null) return 0;
            var count = 0;
            foreach (var id in module.ValueIds())
            {
                string value;
                if (values.TryGetValue(id, out value) && value != null && value.Trim().Length > 0) count++;
            }
            return count;
        }

        // ------------------------------------------------------------ succès

        /// <summary>Les succès de tous les modules installés, dans l'ordre
        /// des modules — la liste que les succès de Marabook intercalent
        /// avant les paliers.</summary>
        public static List<Achievement> ModuleAchievements()
        {
            var list = new List<Achievement>();
            foreach (var module in Installed)
                foreach (var a in module.Achievements)
                    list.Add(new Achievement(a.Id, a.Name, a.Description) { ModuleId = module.Id });
            return list;
        }

        /// <summary>Quels succès de module tiennent sur ce projet.</summary>
        public static HashSet<string> Holding(Project project)
        {
            var holding = new HashSet<string>();
            if (project == null) return holding;
            foreach (var module in Installed)
            {
                if (module.Achievements.Count == 0) continue;
                var complete = 0;
                var fullSheet = false;
                foreach (var item in project.AllItems())
                {
                    if (item.Kind != ItemKind.Sheet || !HasSheet(module, item)) continue;
                    if (project.Trash != null && item.IsDescendantOf(project.Trash)) continue;
                    if (!IsComplete(module, item)) continue;
                    complete++;
                    if (!fullSheet && IsSheetComplete(project, item)) fullSheet = true;
                }
                // Les cartes mentales (22/09) : nombre de cartes, plus grosse carte.
                var maps = 0;
                var biggest = 0;
                foreach (var item in project.AllItems())
                {
                    if (item.Kind != ItemKind.MindMap) continue;
                    if (project.Trash != null && item.IsDescendantOf(project.Trash)) continue;
                    maps++;
                    var summary = MindMaps.Inspect(item.MapBytes);
                    if (summary.Nodes > biggest) biggest = summary.Nodes;
                }
                foreach (var a in module.Achievements)
                {
                    if (a.Rule == ModuleRules.AdvancedComplete && complete >= a.Count) holding.Add(a.Id);
                    else if (a.Rule == ModuleRules.SheetComplete && fullSheet) holding.Add(a.Id);
                    else if (a.Rule == ModuleRules.MindMaps && maps >= a.Count) holding.Add(a.Id);
                    else if (a.Rule == ModuleRules.MindMapNodes && biggest >= a.Count) holding.Add(a.Id);
                }
            }
            return holding;
        }

        /// <summary>« Tout ce qui peut l'être » sur une fiche (la fiche de
        /// module déjà complète) : voir ModuleRules.SheetComplete.</summary>
        public static bool IsSheetComplete(Project project, BinderItem sheet)
        {
            var template = project.FindTemplate(sheet.TemplateId);
            if (template == null) return false;
            foreach (var field in template.Fields)
            {
                string value;
                if (!sheet.FieldValues.TryGetValue(field.Id, out value) || Blank(value)) return false;
            }
            foreach (var entry in sheet.FreeInfo) if (Blank(entry.Value)) return false;
            if (Blank(sheet.Document.ToPlainText())) return false;
            if (sheet.ImageId == null || project.FindImage(sheet.ImageId) == null) return false;
            if (!template.Relations || sheet.Relations.Count == 0) return false;
            if (!template.ShowsRadar) return false;
            foreach (var axis in template.RadarAxes)
            {
                double value;
                if (!sheet.RadarValues.TryGetValue(axis.Id, out value) || value <= 0) return false;
            }
            if (!template.Tracking || !template.Evolution) return false;
            var step = false;
            foreach (var entry in sheet.Evolution) if (!Blank(entry.Note)) { step = true; break; }
            return step;
        }

        private static bool Blank(string value)
        {
            return value == null || value.Trim().Length == 0;
        }
    }
}

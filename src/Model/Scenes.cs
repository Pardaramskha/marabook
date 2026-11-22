using System;
using System.Collections.Generic;
using System.Text;

namespace Marabook.Model
{
    /// <summary>CE QU'ON SAIT D'UNE SCÈNE sans la lire (21/11/2026, Rémi) :
    /// qui la raconte, où et quand elle se passe, qui s'y trouve. Tout est
    /// facultatif. Une fiche disparue laisse son identifiant en place : il
    /// ne mène plus nulle part, rien ne plante.</summary>
    public class SceneMeta
    {
        public string PovId;                       // le point de vue : une fiche
        public string PlaceSheetId;                // le lieu : une fiche…
        public string PlaceMapId, PlaceShapeId;    // …ou un repère, une zone d'une carte
        public string TimelineId;                  // la frise dont le calendrier lit le moment
        public string When = "";                   // le moment, TEL QU'ÉCRIT (« 12 Sel E3.8 »)
        public List<string> CharacterIds = new List<string>(); // les personnages présents, confirmés à la main
        public string Note = "";                   // une ligne libre : l'enjeu de la scène
        /// <summary>Les premiers mots de la scène quand la fiche lui a été
        /// rattachée : c'est par eux qu'elle la retrouve si des séparateurs
        /// sont ajoutés ou retirés AVANT elle. Jamais montré.</summary>
        public string Anchor = "";

        public bool IsEmpty
        {
            get
            {
                return PovId == null && PlaceSheetId == null && PlaceShapeId == null && string.IsNullOrWhiteSpace(When)
                    && (CharacterIds == null || CharacterIds.Count == 0) && string.IsNullOrWhiteSpace(Note);
            }
        }

        public SceneMeta Clone()
        {
            return new SceneMeta
            {
                PovId = PovId, PlaceSheetId = PlaceSheetId, PlaceMapId = PlaceMapId, PlaceShapeId = PlaceShapeId, TimelineId = TimelineId,
                When = When ?? "", CharacterIds = new List<string>(CharacterIds ?? new List<string>()), Note = Note ?? "", Anchor = Anchor ?? ""
            };
        }

        /// <summary>Les mêmes renseignements (l'ancre ne compte pas : elle suit le texte).</summary>
        public bool SameAs(SceneMeta other)
        {
            if (other == null) return false;
            if (PovId != other.PovId || PlaceSheetId != other.PlaceSheetId || PlaceMapId != other.PlaceMapId || PlaceShapeId != other.PlaceShapeId
                || TimelineId != other.TimelineId || (When ?? "") != (other.When ?? "") || (Note ?? "") != (other.Note ?? "")) return false;
            if (CharacterIds.Count != other.CharacterIds.Count) return false;
            for (var i = 0; i < CharacterIds.Count; i++) if (CharacterIds[i] != other.CharacterIds[i]) return false;
            return true;
        }
    }

    /// <summary>Une scène d'un écrit : ses paragraphes [First, Last], ses
    /// premiers mots, son compte de mots, et sa fiche de scène.</summary>
    public class Scene
    {
        public int Index;            // 0 = la première
        public int First, Last;      // paragraphes du texte, bornes comprises ; Last &lt; First pour une scène vide
        public string Opening = "";  // ses premiers mots (un libellé pour la reconnaître)
        public int Words;
        public SceneMeta Meta;
        public bool IsBlank { get { return Last < First; } }
    }

    /// <summary>LES SCÈNES D'UN ÉCRIT. Le découpage suit le texte lui-même :
    /// chaque SÉPARATEUR DE SCÈNE (un paragraphe au style « séparateur ») est
    /// une coupure, ce qui le précède et ce qui le suit sont deux scènes. Un
    /// écrit sans séparateur est une seule scène. RIEN n'est ajouté au texte :
    /// les fiches de scène vivent à côté, sur l'écrit, dans l'ordre.
    ///
    /// Quand des séparateurs bougent, les fiches se RERANGENT d'elles-mêmes :
    /// chacune retrouve sa scène par ses premiers mots. Une scène nouvelle
    /// reçoit une fiche vide. Une fiche remplie dont la scène a disparu (deux
    /// scènes fondues en une) n'est jamais jetée en silence : elle reste en
    /// fin de liste, « sans scène », jusqu'à ce que l'auteur la reprenne ou
    /// la supprime.</summary>
    public static class Scenes
    {
        public const int AnchorLength = 48;

        public static bool IsSeparator(TextParagraph paragraph)
        {
            return paragraph != null && paragraph.StyleId == StyleSheet.SeparatorId;
        }

        private static string PlainOf(TextParagraph paragraph)
        {
            return Links.Strip(PivotEdit.FlatText(paragraph)).Replace("￼", "").Trim();
        }

        private static int CountWords(string text)
        {
            // Comptés comme partout ailleurs : le tokeniseur de la barre d'état.
            return Marabook.Correction.FrenchTokenizer.Tokenize(text).Count;
        }

        /// <summary>Les premiers mots d'une scène, entre guillemets ; « … » seulement s'ils ont été coupés.</summary>
        public static string Quote(string opening)
        {
            if (string.IsNullOrEmpty(opening)) return "(scène vide)";
            return "« " + opening + (opening.Length >= AnchorLength ? "… »" : " »");
        }

        /// <summary>Le découpage seul, sans toucher aux fiches : une scène par
        /// tranche entre deux séparateurs (Meta reste null).</summary>
        public static List<Scene> Split(TextDocument document)
        {
            var scenes = new List<Scene>();
            var count = document == null ? 0 : document.Paragraphs.Count;
            var first = 0;
            for (var i = 0; i <= count; i++)
            {
                if (i < count && !IsSeparator(document.Paragraphs[i])) continue;
                var scene = new Scene { Index = scenes.Count, First = first, Last = i - 1 };
                for (var p = first; p < i; p++)
                {
                    var plain = PlainOf(document.Paragraphs[p]);
                    if (plain.Length == 0) continue;
                    if (scene.Opening.Length == 0) scene.Opening = plain.Length > AnchorLength ? plain.Substring(0, AnchorLength) : plain;
                    scene.Words += CountWords(plain);
                }
                scenes.Add(scene);
                first = i + 1;
            }
            return scenes;
        }

        /// <summary>Le nombre de scènes d'un écrit (1 sans séparateur).</summary>
        public static int CountOf(BinderItem item)
        {
            if (item == null || item.Kind != ItemKind.Text || item.Document == null) return 0;
            var count = 1;
            foreach (var paragraph in item.Document.Paragraphs) if (IsSeparator(paragraph)) count++;
            return count;
        }

        /// <summary>Les scènes de l'écrit, chacune avec SA fiche. Les fiches
        /// de l'écrit sont rerangées au passage (voir la classe) ; rend vrai
        /// dans « changed » si leur ordre ou leur nombre a bougé.</summary>
        public static List<Scene> Of(BinderItem item, out bool changed)
        {
            changed = false;
            if (item == null || item.Kind != ItemKind.Text || item.Document == null) return new List<Scene>();
            var scenes = Split(item.Document);
            var metas = item.Scenes;
            var aligned = new SceneMeta[scenes.Count];
            var used = new bool[metas.Count];

            if (metas.Count == scenes.Count || NoAnchor(metas))
            {
                // Même compte (ou des fiches jamais rattachées) : dans l'ordre.
                for (var i = 0; i < scenes.Count && i < metas.Count; i++) { aligned[i] = metas[i]; used[i] = true; }
            }
            else
            {
                // Le compte a changé : chaque fiche cherche sa scène par ses premiers mots, sans jamais reculer.
                var from = 0;
                for (var m = 0; m < metas.Count; m++)
                {
                    if (string.IsNullOrEmpty(metas[m].Anchor)) continue;
                    for (var s = from; s < scenes.Count; s++)
                    {
                        if (aligned[s] != null || scenes[s].Opening != metas[m].Anchor) continue;
                        aligned[s] = metas[m];
                        used[m] = true;
                        from = s + 1;
                        break;
                    }
                }
                // Ce qui reste, fiche REMPLIE et scène libre : dans l'ordre, entre deux scènes déjà reconnues.
                var free = 0;
                for (var m = 0; m < metas.Count; m++)
                {
                    if (used[m] || metas[m].IsEmpty) continue;
                    var lower = 0;
                    var upper = scenes.Count;
                    for (var k = m - 1; k >= 0; k--) if (used[k]) { lower = Array.IndexOf(aligned, metas[k]) + 1; break; }
                    for (var k = m + 1; k < metas.Count; k++) if (used[k]) { var at = Array.IndexOf(aligned, metas[k]); if (at >= 0) { upper = at; break; } }
                    free = Math.Max(free, lower);
                    while (free < upper && aligned[free] != null) free++;
                    if (free >= upper) continue; // pas de scène pour elle : elle restera « sans scène »
                    aligned[free] = metas[m];
                    used[m] = true;
                    free++;
                }
            }

            var result = new List<SceneMeta>(scenes.Count);
            for (var s = 0; s < scenes.Count; s++)
            {
                var meta = aligned[s] ?? new SceneMeta();
                meta.Anchor = scenes[s].Opening;
                scenes[s].Meta = meta;
                result.Add(meta);
            }
            // Les fiches remplies restées sans scène ferment la liste ; les vides s'en vont.
            for (var m = 0; m < metas.Count; m++)
                if (!used[m] && !metas[m].IsEmpty) result.Add(metas[m]);

            if (result.Count != metas.Count) changed = true;
            else for (var i = 0; i < result.Count; i++) if (!ReferenceEquals(result[i], metas[i])) { changed = true; break; }
            item.Scenes = result;
            return scenes;
        }

        public static List<Scene> Of(BinderItem item)
        {
            bool changed;
            return Of(item, out changed);
        }

        private static bool NoAnchor(List<SceneMeta> metas)
        {
            foreach (var meta in metas) if (!string.IsNullOrEmpty(meta.Anchor)) return false;
            return true;
        }

        /// <summary>Les fiches de scène que le texte ne porte plus (au-delà du nombre de scènes).</summary>
        public static List<SceneMeta> Orphans(BinderItem item)
        {
            var orphans = new List<SceneMeta>();
            if (item == null || item.Kind != ItemKind.Text) return orphans;
            var count = CountOf(item);
            for (var i = count; i < item.Scenes.Count; i++) orphans.Add(item.Scenes[i]);
            return orphans;
        }

        /// <summary>La scène où tombe ce paragraphe (celle d'avant pour un séparateur).</summary>
        public static int IndexAt(TextDocument document, int paragraph)
        {
            var index = 0;
            if (document == null) return 0;
            for (var i = 0; i < paragraph && i < document.Paragraphs.Count; i++) if (IsSeparator(document.Paragraphs[i])) index++;
            return index;
        }

        public static bool HasAny(BinderItem item)
        {
            if (item == null || item.Scenes == null) return false;
            foreach (var meta in item.Scenes) if (!meta.IsEmpty) return true;
            return false;
        }

        public static List<SceneMeta> CloneAll(List<SceneMeta> metas)
        {
            var copy = new List<SceneMeta>();
            if (metas != null) foreach (var meta in metas) copy.Add(meta.Clone());
            return copy;
        }

        // ------------------------------------------------------------ ce qu'on en lit

        public static string SheetTitle(Project project, string id)
        {
            if (project == null || string.IsNullOrEmpty(id)) return "";
            var sheet = project.FindById(id);
            return sheet == null ? "(fiche disparue)" : sheet.Title ?? "";
        }

        /// <summary>Le lieu de la scène, dit : le titre de sa fiche, ou « Gué des Saules (Ehnmir) » pour un lieu de carte.</summary>
        public static string PlaceLabel(Project project, SceneMeta meta)
        {
            if (meta == null) return "";
            if (!string.IsNullOrEmpty(meta.PlaceSheetId)) return SheetTitle(project, meta.PlaceSheetId);
            if (string.IsNullOrEmpty(meta.PlaceShapeId) || project == null) return "";
            var place = Timelines.PlaceOf(project, new TimelineEvent { PlaceMapId = meta.PlaceMapId, PlaceShapeId = meta.PlaceShapeId });
            return place == null ? "(lieu disparu)" : place.Label;
        }

        public static string CharactersLabel(Project project, SceneMeta meta)
        {
            if (meta == null || meta.CharacterIds == null || meta.CharacterIds.Count == 0) return "";
            var names = new List<string>();
            foreach (var id in meta.CharacterIds) names.Add(SheetTitle(project, id));
            return string.Join(", ", names.ToArray());
        }

        /// <summary>La frise dont le calendrier lit le moment de la scène : la
        /// sienne, sinon la première du projet ; null s'il n'y en a aucune.</summary>
        public static BinderItem TimelineOf(Project project, SceneMeta meta)
        {
            if (project == null) return null;
            var chosen = meta == null || meta.TimelineId == null ? null : project.FindById(meta.TimelineId);
            if (chosen != null && chosen.Kind == ItemKind.Timeline) return chosen;
            var all = Timelines.All(project);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>Le moment de la scène, lu dans son calendrier ; null s'il est vide ou ne s'y lit pas.</summary>
        public static TimePoint WhenOf(Project project, SceneMeta meta, out TimeSystem system)
        {
            system = null;
            if (meta == null || string.IsNullOrWhiteSpace(meta.When)) return null;
            var timeline = TimelineOf(project, meta);
            if (timeline == null) return null;
            system = Timelines.SystemOf(project, timeline);
            TimePoint point;
            return system != null && system.TryParse(meta.When.Trim(), out point) ? point : null;
        }

        /// <summary>Les fiches dont un nom paraît dans le texte de la scène :
        /// une SUGGESTION pour « Personnages présents », que l'auteur confirme.</summary>
        public static List<BinderItem> DetectedCharacters(Project project, BinderItem item, Scene scene)
        {
            var found = new List<BinderItem>();
            if (project == null || item == null || scene == null || scene.IsBlank) return found;
            var sb = new StringBuilder();
            for (var p = scene.First; p <= scene.Last && p < item.Document.Paragraphs.Count; p++) sb.Append(PlainOf(item.Document.Paragraphs[p])).Append('\n');
            var text = sb.ToString();
            foreach (var sheet in project.AllItems())
            {
                if (sheet.Kind != ItemKind.Sheet) continue;
                var root = sheet.RootCategory();
                if (root != null && root.CategoryKey == Project.KeyTrash) continue;
                var names = Presence.NamesForGraph(sheet, project.TemplateOf(sheet));
                if (names != null && names.Count > 0 && Presence.CountIn(text, names) > 0) found.Add(sheet);
            }
            found.Sort(delegate(BinderItem a, BinderItem b) { return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase); });
            return found;
        }
    }
}

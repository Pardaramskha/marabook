using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Marabook.Model
{
    public enum OutlineRowKind { Folder, Text, Scene }

    /// <summary>Une ligne du Tableau : un dossier (livre, partie), un écrit,
    /// ou une scène d'un écrit qui en a plusieurs.</summary>
    public class OutlineRow
    {
        public OutlineRowKind Kind;
        public BinderItem Item;   // le dossier, ou l'écrit (celui de la scène)
        public Scene Scene;       // la scène : celle de la ligne, ou l'unique scène d'un écrit sans séparateur
        public int SceneCount;    // sur un écrit : son nombre de scènes
        public int Depth;
        public string Title = "";
        public int Words;
        public int Texts;         // sur un dossier : ses écrits
        public string Pov = "", Place = "", When = "", Characters = "", Note = "";
        public TimePoint Moment;  // le moment lu dans le calendrier, ou null
        /// <summary>La fiche de scène que cette ligne permet de renseigner, ou null (dossier ; écrit à plusieurs scènes).</summary>
        public SceneMeta Meta { get { return Kind == OutlineRowKind.Folder || (Kind == OutlineRowKind.Text && SceneCount > 1) || Scene == null ? null : Scene.Meta; } }
        public int SceneIndex { get { return Scene == null ? 0 : Scene.Index; } }
    }

    public class OutlineOptions
    {
        public bool Scenes = true;        // montrer une ligne par scène
        public string Sort = Outline.SortBook;
        public string Status;             // n'en montrer qu'un ("" = sans état ; null = tous)
    }

    public class OutlineTotals
    {
        public int Texts, Scenes, Words;
    }

    /// <summary>LE TABLEAU D'UN MANUSCRIT — l'outliner (2.0.0, 21/11/2026) :
    /// une ligne par écrit, sous son dossier, et sous chaque écrit à plusieurs
    /// scènes une ligne par scène ; en colonnes, ce qu'on sait de chacun
    /// (état, mots, point de vue, lieu, moment, personnages, synopsis).
    /// L'ORDRE DU LIVRE est celui de la Pile. Trier par une autre colonne
    /// met les lignes à plat, le temps de regarder : rien ne bouge dans le
    /// projet.</summary>
    public static class Outline
    {
        public const string SortBook = "book";
        public const string SortWordsDown = "words-down";
        public const string SortWordsUp = "words-up";
        public const string SortStatus = "status";
        public const string SortPov = "pov";
        public const string SortPlace = "place";
        public const string SortWhen = "when";

        public static readonly string[] Sorts = { SortBook, SortWordsDown, SortWordsUp, SortStatus, SortPov, SortPlace, SortWhen };

        public static string SortLabel(string sort)
        {
            switch (sort)
            {
                case SortWordsDown: return "Les plus longs d'abord";
                case SortWordsUp: return "Les plus courts d'abord";
                case SortStatus: return "Par état";
                case SortPov: return "Par point de vue";
                case SortPlace: return "Par lieu";
                case SortWhen: return "Par moment de l'histoire";
                default: return "Ordre du livre";
            }
        }

        /// <summary>Un écrit du manuscrit : pas une page d'éditeur (liminaire, annexe), pas un gabarit.</summary>
        public static bool Counts(BinderItem item)
        {
            return item != null && item.Kind == ItemKind.Text && !item.IsExtraPage;
        }

        public static bool HasTexts(BinderItem folder)
        {
            if (folder == null) return false;
            foreach (var child in folder.Children)
            {
                if (Counts(child)) return true;
                if (child.Children.Count > 0 && HasTexts(child)) return true;
            }
            return false;
        }

        public static List<OutlineRow> Build(Project project, BinderItem folder, OutlineOptions options, OutlineTotals totals = null)
        {
            options = options ?? new OutlineOptions();
            var rows = new List<OutlineRow>();
            if (folder == null) return rows;
            Walk(project, folder, 0, options, rows);
            if (totals != null)
                foreach (var row in rows)
                {
                    if (row.Kind != OutlineRowKind.Text) continue;
                    totals.Texts++;
                    totals.Scenes += row.SceneCount;
                    totals.Words += row.Words;
                }
            if (options.Sort == null || options.Sort == SortBook) return rows;

            // Un tri : à plat. Les lignes triées sont les scènes si on les montre, sinon les écrits.
            var flat = new List<OutlineRow>();
            foreach (var row in rows)
            {
                if (row.Kind == OutlineRowKind.Folder) continue;
                if (options.Scenes && row.Kind == OutlineRowKind.Text && row.SceneCount > 1) continue; // ses scènes parlent pour lui
                row.Depth = 0;
                if (row.Kind == OutlineRowKind.Scene) row.Title = row.Item.Title + " — " + row.Title;
                flat.Add(row);
            }
            var order = new Dictionary<OutlineRow, int>();
            for (var i = 0; i < flat.Count; i++) order[flat[i]] = i;
            var sort = options.Sort;
            flat.Sort(delegate(OutlineRow a, OutlineRow b)
            {
                var compared = Compare(a, b, sort);
                return compared != 0 ? compared : order[a].CompareTo(order[b]); // stable : l'ordre du livre départage
            });
            return flat;
        }

        private static int Compare(OutlineRow a, OutlineRow b, string sort)
        {
            switch (sort)
            {
                case SortWordsDown: return b.Words.CompareTo(a.Words);
                case SortWordsUp: return a.Words.CompareTo(b.Words);
                case SortStatus: return StatusRank(a.Item.Status).CompareTo(StatusRank(b.Item.Status));
                case SortPov: return Text(a.Pov, b.Pov);
                case SortPlace: return Text(a.Place, b.Place);
                case SortWhen:
                    // Les moments lus comme des dates, dans l'ordre du temps ; puis les textes libres ; les vides à la fin.
                    if (a.Moment != null && b.Moment != null) return Chrono(a.Moment, b.Moment);
                    if (a.Moment != null) return -1;
                    if (b.Moment != null) return 1;
                    return Text(a.When, b.When);
                default: return 0;
            }
        }

        private static int Chrono(TimePoint a, TimePoint b)
        {
            if (a.Year != b.Year) return a.Year.CompareTo(b.Year);
            if (a.Month != b.Month) return a.Month.CompareTo(b.Month);
            if (a.Day != b.Day) return a.Day.CompareTo(b.Day);
            if (a.Hour != b.Hour) return a.Hour.CompareTo(b.Hour);
            if (a.Minute != b.Minute) return a.Minute.CompareTo(b.Minute);
            return a.Second.CompareTo(b.Second);
        }

        /// <summary>Les vides à la fin, quel que soit le tri.</summary>
        private static int Text(string a, string b)
        {
            var emptyA = string.IsNullOrEmpty(a);
            var emptyB = string.IsNullOrEmpty(b);
            if (emptyA || emptyB) return emptyA == emptyB ? 0 : emptyA ? 1 : -1;
            return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
        }

        private static int StatusRank(string status)
        {
            var rank = Array.IndexOf(TextStatus.Keys, status ?? "");
            return rank < 0 ? TextStatus.Keys.Length : rank;
        }

        /// <summary>Rend le nombre de mots posés sous ce dossier.</summary>
        private static void Walk(Project project, BinderItem folder, int depth, OutlineOptions options, List<OutlineRow> rows)
        {
            foreach (var child in folder.Children)
            {
                if (child.Kind == ItemKind.PageTemplate) continue;
                if (Counts(child))
                {
                    if (options.Status != null && (child.Status ?? "") != options.Status) continue;
                    var scenes = Scenes.Of(child);
                    var row = new OutlineRow { Kind = OutlineRowKind.Text, Item = child, Depth = depth, Title = child.Title ?? "", SceneCount = scenes.Count };
                    foreach (var scene in scenes) row.Words += scene.Words;
                    if (scenes.Count == 1) { row.Scene = scenes[0]; Fill(project, row, scenes[0].Meta); }
                    else Merge(project, row, scenes);
                    rows.Add(row);
                    if (options.Scenes && scenes.Count > 1)
                        foreach (var scene in scenes)
                        {
                            var line = new OutlineRow
                            {
                                Kind = OutlineRowKind.Scene, Item = child, Scene = scene, Depth = depth + 1, Words = scene.Words, SceneCount = scenes.Count,
                                Title = "Scène " + (scene.Index + 1) + (scene.Opening.Length > 0 ? " — " + Scenes.Quote(scene.Opening) : " (vide)")
                            };
                            Fill(project, line, scene.Meta);
                            rows.Add(line);
                        }
                }
                if (child.Kind == ItemKind.Folder || child.Kind == ItemKind.Book)
                {
                    if (!HasTexts(child)) continue;
                    var at = rows.Count;
                    var head = new OutlineRow { Kind = OutlineRowKind.Folder, Item = child, Depth = depth, Title = child.Title ?? "" };
                    rows.Add(head);
                    Walk(project, child, depth + 1, options, rows);
                    for (var i = at + 1; i < rows.Count; i++)
                        if (rows[i].Kind == OutlineRowKind.Text) { head.Words += rows[i].Words; head.Texts++; }
                    if (head.Texts == 0) rows.RemoveAt(at); // le filtre d'état n'y a rien laissé
                }
                else if (child.Kind == ItemKind.Text && child.Children.Count > 0)
                    Walk(project, child, depth + 1, options, rows); // un écrit qui porte des enfants
            }
        }

        private static void Fill(Project project, OutlineRow row, SceneMeta meta)
        {
            if (meta == null) return;
            row.Pov = Scenes.SheetTitle(project, meta.PovId);
            row.Place = Scenes.PlaceLabel(project, meta);
            row.When = (meta.When ?? "").Trim();
            row.Characters = Scenes.CharactersLabel(project, meta);
            row.Note = (meta.Note ?? "").Trim();
            TimeSystem system;
            row.Moment = Scenes.WhenOf(project, meta, out system);
        }

        /// <summary>Un écrit à plusieurs scènes : sa ligne résume — les points
        /// de vue et les lieux distincts, le premier moment, tous les présents.</summary>
        private static void Merge(Project project, OutlineRow row, List<Scene> scenes)
        {
            var povs = new List<string>();
            var places = new List<string>();
            var people = new List<string>();
            foreach (var scene in scenes)
            {
                var meta = scene.Meta;
                Add(povs, Scenes.SheetTitle(project, meta.PovId));
                Add(places, Scenes.PlaceLabel(project, meta));
                foreach (var id in meta.CharacterIds) Add(people, Scenes.SheetTitle(project, id));
                if (row.When.Length == 0 && !string.IsNullOrWhiteSpace(meta.When))
                {
                    row.When = meta.When.Trim();
                    TimeSystem system;
                    row.Moment = Scenes.WhenOf(project, meta, out system);
                }
            }
            row.Pov = string.Join(", ", povs.ToArray());
            row.Place = string.Join(", ", places.ToArray());
            row.Characters = string.Join(", ", people.ToArray());
        }

        private static void Add(List<string> list, string value)
        {
            if (!string.IsNullOrEmpty(value) && !list.Contains(value)) list.Add(value);
        }

        // ------------------------------------------------------------ l'export

        public static readonly string[] Columns = { "Titre", "État", "Mots", "Point de vue", "Lieu", "Moment", "Personnages", "Synopsis" };

        public static string[] Cells(OutlineRow row)
        {
            var folder = row.Kind == OutlineRowKind.Folder;
            return new[]
            {
                row.Title,
                row.Kind == OutlineRowKind.Text ? TextStatus.Label(row.Item.Status) : "",
                row.Words.ToString(CultureInfo.InvariantCulture),
                folder ? "" : row.Pov, folder ? "" : row.Place, folder ? "" : row.When, folder ? "" : row.Characters,
                row.Kind == OutlineRowKind.Scene ? row.Note : (row.Item.Synopsis ?? "").Trim()
            };
        }

        /// <summary>Le tableau en CSV (point-virgule, comme les fiches) : s'ouvre dans un tableur.</summary>
        public static string Csv(List<OutlineRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(";", Columns)).Append("\r\n");
            foreach (var row in rows)
            {
                var cells = Cells(row);
                cells[0] = new string(' ', row.Depth * 2) + cells[0];
                for (var i = 0; i < cells.Length; i++) cells[i] = Marabook.Exchange.SheetData.Cell(cells[i]);
                sb.Append(string.Join(";", cells)).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Le tableau en Markdown : un plan à envoyer, à coller.</summary>
        public static string Markdown(string title, List<OutlineRow> rows)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(title).Append("\n\n");
            sb.Append("| ").Append(string.Join(" | ", Columns)).Append(" |\n|");
            for (var i = 0; i < Columns.Length; i++) sb.Append(i == 2 ? " ---: |" : " --- |");
            sb.Append('\n');
            foreach (var row in rows)
            {
                var cells = Cells(row);
                for (var i = 0; i < cells.Length; i++) cells[i] = cells[i].Replace("|", "\\|").Replace("\r", "").Replace("\n", " ");
                var indent = "";
                for (var d = 0; d < row.Depth; d++) indent += "\u2003";
                cells[0] = indent + (row.Kind == OutlineRowKind.Folder ? "**" + cells[0] + "**" : row.Kind == OutlineRowKind.Scene ? "*" + cells[0] + "*" : cells[0]);
                sb.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
            }
            return sb.ToString();
        }
    }
}

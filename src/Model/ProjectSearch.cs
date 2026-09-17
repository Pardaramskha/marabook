using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Marabook.Model
{
    /// <summary>La portée d'une recherche : le document courant, le livre ou
    /// dossier courant (et tout ce qu'il contient), le projet entier.</summary>
    public enum SearchScope { Document, Container, Project }

    /// <summary>Le filtre par nature d'item.</summary>
    public enum SearchKind { All, Texts, Sheets, Plans, Dictionary, Media }

    /// <summary>Un item à fouiller : ses champs cherchables (immuables, pris
    /// sur le fil UI) et son rang dans la Pile.</summary>
    public class SearchTarget
    {
        public BinderItem Item;
        public int Order;
        public List<SearchField> Fields;
    }

    /// <summary>Une occurrence : l'item, le champ, l'empan (offsets du texte
    /// d'origine), l'extrait et la position du terme dedans.</summary>
    public class SearchHit
    {
        public BinderItem Item;
        public SearchField Field;
        public int Order;          // rang de l'item dans la Pile
        public int Start, Length;
        public bool Exact;         // faux = coupe une ligature pliée (non remplaçable)
        public bool NoProof;       // chevauche un passage « ne pas corriger »
        public string Excerpt = "";
        public int ExcerptStart, ExcerptLength;

        public int ParagraphIndex { get { return Field == null ? -1 : Field.ParagraphIndex; } }
        public bool Replaceable { get { return Exact && !NoProof; } }
    }

    /// <summary>Le résultat : les premières occurrences (plafond annoncé),
    /// le total réel, les items touchés, et ce qui a interrompu la recherche.</summary>
    public class SearchResult
    {
        public List<SearchHit> Hits = new List<SearchHit>();
        public int Total;            // toutes les occurrences comptées, plafond compris
        public int ItemCount;        // items portant au moins une occurrence
        public int NoProofCount;     // occurrences dans un passage « ne pas corriger »
        public int InexactCount;     // occurrences qui coupent une ligature
        public bool Capped;          // Hits < Total
        public bool Interrupted;     // budget global ou expression trop coûteuse
        public bool Cancelled;
        public string Message;       // ce qu'il faut dire (interruption, erreur)
        public long ElapsedMs;

        /// <summary>« 200 premières sur 1 340 occurrences dans 23 items ».</summary>
        public string Summary()
        {
            if (Total == 0) return Cancelled ? "" : Interrupted ? "Aucune occurrence avant l'interruption" : "Aucune occurrence";
            var items = ItemCount == 1 ? "1 item" : ItemCount + " items";
            var occurrences = Total == 1 ? "1 occurrence" : Total + " occurrences";
            if (Capped)
                return Hits.Count + " premières sur " + (Interrupted ? "au moins " : "") + occurrences + " dans " + items;
            return (Interrupted ? "Au moins " : "") + occurrences + " dans " + items;
        }
    }

    /// <summary>La recherche à l'échelle du projet (batch 37), sans WPF :
    /// Collect (fil UI : portée, filtre, champs en cache) puis Run (fil de
    /// fond : les occurrences, annulable, budget global). Ordre = Pile, puis
    /// champ, puis texte. La corbeille est EXCLUE par défaut (on y cherche
    /// parfois ce qu'on a supprimé : includeTrash).</summary>
    public static class ProjectSearch
    {
        public const int DefaultCap = 200;
        public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(3);
        private const int ContextChars = 40;

        /// <summary>Vrai si la nature de l'item passe le filtre.</summary>
        public static bool Matches(SearchKind kind, BinderItem item)
        {
            if (item.IsCategory) return item.CategoryKey == Project.KeyDictionary
                && (kind == SearchKind.All || kind == SearchKind.Dictionary);
            switch (kind)
            {
                case SearchKind.All: return true;
                case SearchKind.Texts: return item.Kind == ItemKind.Text || item.Kind == ItemKind.Book || item.Kind == ItemKind.Folder;
                case SearchKind.Sheets: return item.Kind == ItemKind.Sheet;
                case SearchKind.Plans: return item.Kind == ItemKind.Plan;
                case SearchKind.Media: return item.Kind == ItemKind.Media;
                default: return false;
            }
        }

        /// <summary>Le conteneur d'une portée « livre ou dossier courant » :
        /// l'item lui-même s'il en est un, sinon son parent.</summary>
        public static BinderItem ContainerOf(BinderItem current)
        {
            if (current == null) return null;
            if (current.IsCategory || current.Kind == ItemKind.Book || current.Kind == ItemKind.Folder) return current;
            return current.Parent ?? current;
        }

        /// <summary>Les items dans l'ORDRE DE LA PILE (profondeur d'abord, tel
        /// que l'arbre les montre) — Project.AllItems parcourt en largeur.</summary>
        public static IEnumerable<BinderItem> PileOrder(Project project)
        {
            var stack = new Stack<BinderItem>();
            for (var i = project.Roots.Count - 1; i >= 0; i--) stack.Push(project.Roots[i]);
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                yield return item;
                for (var i = item.Children.Count - 1; i >= 0; i--) stack.Push(item.Children[i]);
            }
        }

        /// <summary>Les items à fouiller, dans l'ordre de la Pile, avec leurs
        /// champs (servis du cache). À appeler sur le fil UI.</summary>
        public static List<SearchTarget> Collect(Project project, SearchScope scope, BinderItem current,
            SearchKind kind, bool includeTrash)
        {
            var targets = new List<SearchTarget>();
            if (project == null) return targets;
            var container = scope == SearchScope.Container ? ContainerOf(current) : null;
            var order = 0;
            foreach (var item in PileOrder(project))
            {
                order++;
                if (scope == SearchScope.Document && item != current) continue;
                if (scope == SearchScope.Container && container != null && item != container && !item.IsDescendantOf(container)) continue;
                if (!Matches(kind, item)) continue;
                if (!includeTrash && !item.IsCategory && item.RootCategory().CategoryKey == Project.KeyTrash) continue;
                var fields = item.SearchFields(project);
                if (fields.Count == 0) continue;
                targets.Add(new SearchTarget { Item = item, Order = order, Fields = fields });
            }
            return targets;
        }

        /// <summary>Fouille les cibles. Annulable (token), bornée (budget
        /// global — au-delà, la recherche s'arrête et le dit), plafonnée
        /// (cap occurrences gardées, le total continue d'être compté).
        /// Peut tourner hors du fil UI : ne lit que des chaînes.</summary>
        public static SearchResult Run(List<SearchTarget> targets, SearchQuery query, int cap,
            TimeSpan budget, CancellationToken cancel)
        {
            var result = new SearchResult();
            var watch = Stopwatch.StartNew();
            if (query == null || !query.IsValid)
            {
                result.Message = query == null || query.IsEmpty ? null : query.Error;
                return result;
            }
            var itemCount = 0;
            foreach (var target in targets)
            {
                var found = false;
                foreach (var field in target.Fields)
                {
                    if (cancel.IsCancellationRequested) { result.Cancelled = true; break; }
                    if (watch.Elapsed > budget)
                    {
                        result.Interrupted = true;
                        result.Message = "Recherche interrompue après " + ((int)budget.TotalSeconds) + " s : expression trop coûteuse.";
                        break;
                    }
                    List<SearchQuery.Span> spans;
                    try { spans = query.FindInField(field); }
                    catch (SearchTimeoutException error)
                    {
                        result.Interrupted = true;
                        result.Message = "Recherche interrompue : " + error.Message;
                        break;
                    }
                    foreach (var span in spans)
                    {
                        found = true;
                        result.Total++;
                        var noProof = field.OverlapsNoProof(span.Start, span.Length);
                        if (noProof) result.NoProofCount++;
                        if (!span.Exact) result.InexactCount++;
                        if (result.Hits.Count >= cap) { result.Capped = true; continue; }
                        var hit = new SearchHit
                        {
                            Item = target.Item,
                            Field = field,
                            Order = target.Order,
                            Start = span.Start,
                            Length = span.Length,
                            Exact = span.Exact,
                            NoProof = noProof
                        };
                        int excerptStart, excerptLength;
                        hit.Excerpt = Excerpt(field.Text, span.Start, span.Length, out excerptStart, out excerptLength);
                        hit.ExcerptStart = excerptStart;
                        hit.ExcerptLength = excerptLength;
                        result.Hits.Add(hit);
                    }
                }
                if (found) itemCount++;
                if (result.Cancelled || result.Interrupted) break;
            }
            result.ItemCount = itemCount;
            result.ElapsedMs = watch.ElapsedMilliseconds;
            return result;
        }

        /// <summary>Un extrait autour de l'empan : quelques mots de part et
        /// d'autre (coupé sur des blancs), blancs et retours repliés en une
        /// espace, éléments U+FFFC rendus « ▢ », « … » aux bords tronqués ;
        /// rend la position du terme DANS l'extrait.</summary>
        public static string Excerpt(string text, int start, int length, out int excerptStart, out int excerptLength)
        {
            text = text ?? "";
            start = Math.Max(0, Math.Min(start, text.Length));
            length = Math.Max(0, Math.Min(length, text.Length - start));
            var end = start + length;
            var left = Math.Max(0, start - ContextChars);
            if (left > 0)
            {
                while (left < start && !char.IsWhiteSpace(text[left])) left++; // finir sur un blanc
                while (left < start && char.IsWhiteSpace(text[left])) left++;
            }
            var right = Math.Min(text.Length, end + ContextChars);
            if (right < text.Length)
            {
                while (right > end && !char.IsWhiteSpace(text[right - 1])) right--;
                while (right > end && char.IsWhiteSpace(text[right - 1])) right--;
            }
            var sb = new StringBuilder();
            if (left > 0) sb.Append('…');
            excerptStart = -1;
            excerptLength = 0;
            var pendingSpace = false;
            for (var i = left; i < right; i++)
            {
                if (i == start) { FlushSpace(sb, ref pendingSpace); excerptStart = sb.Length; }
                var c = text[i];
                if (char.IsWhiteSpace(c)) { pendingSpace = true; }
                else
                {
                    FlushSpace(sb, ref pendingSpace);
                    sb.Append(c == '￼' ? '▢' : c);
                }
                if (i == end - 1) { FlushSpace(sb, ref pendingSpace); excerptLength = sb.Length - excerptStart; }
            }
            if (excerptStart < 0) { excerptStart = sb.Length; excerptLength = 0; }
            if (right < text.Length) sb.Append('…');
            return sb.ToString();
        }

        private static void FlushSpace(StringBuilder sb, ref bool pending)
        {
            if (pending && sb.Length > 0) sb.Append(' ');
            pending = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Marabook.Model
{
    /// <summary>Ce que Marabook sait d'une carte mentale SANS le module : le
    /// résumé lu dans le document.json du .tea (une archive zip, format de
    /// Mental-o) — boîtes, liens, groupes, les premiers mots. Sert aux tuiles
    /// du corkboard, aux statistiques et aux succès du module quand il est
    /// absent ; le module, lui, sait la dessiner et l'éditer.</summary>
    public class MindMapSummary
    {
        public int Nodes, Links, Groups;
        public string Preview = "";
        public bool Readable;

        public string Label
        {
            get
            {
                if (!Readable) return "Carte illisible";
                if (Nodes == 0) return "Carte vide";
                return Nodes + (Nodes > 1 ? " boîtes" : " boîte") + " · " + Links + (Links > 1 ? " liens" : " lien")
                    + (Groups > 0 ? " · " + Groups + (Groups > 1 ? " groupes" : " groupe") : "");
            }
        }
    }

    public static class MindMaps
    {
        public const string Extension = ".tea";
        public const string OpenFilter = "Cartes Mental-o (*.tea)|*.tea|Tous les fichiers (*.*)|*.*";
        public const string SaveFilter = "Carte Mental-o (*.tea)|*.tea";

        private static readonly Dictionary<byte[], MindMapSummary> _cache = new Dictionary<byte[], MindMapSummary>();

        /// <summary>Le résumé d'une carte (mémorisé par tableau d'octets : les
        /// octets d'un élément ne changent qu'à l'enregistrement de sa carte).</summary>
        public static MindMapSummary Inspect(byte[] tea)
        {
            var summary = new MindMapSummary();
            if (tea == null || tea.Length == 0) return summary;
            MindMapSummary known;
            if (_cache.TryGetValue(tea, out known)) return known;
            try
            {
                using (var stream = new MemoryStream(tea))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var entry = archive.GetEntry("document.json");
                    if (entry != null)
                    {
                        string json;
                        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) json = reader.ReadToEnd();
                        var root = Json.AsObject(Json.Parse(json));
                        var nodes = Json.AsList(Json.Field(root, "noeuds"));
                        var links = Json.AsList(Json.Field(root, "liens"));
                        var groups = Json.AsList(Json.Field(root, "groupes"));
                        summary.Nodes = nodes == null ? 0 : nodes.Count;
                        summary.Links = links == null ? 0 : links.Count;
                        summary.Groups = groups == null ? 0 : groups.Count;
                        summary.Preview = Preview(nodes);
                        summary.Readable = true;
                    }
                }
            }
            catch { summary.Readable = false; }
            if (_cache.Count > 200) _cache.Clear();
            _cache[tea] = summary;
            return summary;
        }

        /// <summary>Les premiers mots des premières boîtes, pour la tuile.</summary>
        private static string Preview(List<object> nodes)
        {
            if (nodes == null) return "";
            var sb = new StringBuilder();
            foreach (var nodeObject in nodes)
            {
                var node = Json.AsObject(nodeObject);
                if (node == null) continue;
                var text = NodeText(node);
                if (text.Length == 0) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(text);
                if (sb.Length > 160) break;
            }
            var preview = sb.ToString();
            return preview.Length > 180 ? preview.Substring(0, 180).TrimEnd() + "…" : preview;
        }

        /// <summary>Le texte d'une boîte : les segments riches (v3+), sinon le texte plat.</summary>
        public static string NodeText(Dictionary<string, object> node)
        {
            var rich = Json.AsList(Json.Field(node, "riche"));
            if (rich != null && rich.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var segmentObject in rich)
                {
                    var segment = Json.AsObject(segmentObject);
                    if (segment != null) sb.Append(Json.AsString(Json.Field(segment, "t")) ?? "");
                }
                return sb.ToString().Replace('\n', ' ').Trim();
            }
            return (Json.AsString(Json.Field(node, "texte")) ?? "").Replace('\n', ' ').Trim();
        }

        /// <summary>Le texte cherchable d'une carte : toutes ses boîtes.</summary>
        public static string SearchText(byte[] tea)
        {
            if (tea == null || tea.Length == 0) return "";
            try
            {
                using (var stream = new MemoryStream(tea))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var entry = archive.GetEntry("document.json");
                    if (entry == null) return "";
                    string json;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) json = reader.ReadToEnd();
                    var nodes = Json.AsList(Json.Field(Json.AsObject(Json.Parse(json)), "noeuds"));
                    if (nodes == null) return "";
                    var sb = new StringBuilder();
                    foreach (var nodeObject in nodes)
                    {
                        var node = Json.AsObject(nodeObject);
                        if (node == null) continue;
                        var text = NodeText(node);
                        if (text.Length > 0) sb.Append(text).Append('\n');
                    }
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }
    }
}

using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>LA DISPOSITION du graphe (1.1.0, 28/10) : une force dirigée
    /// de Fruchterman-Reingold, en O(N²) par itération — largement suffisant
    /// jusqu'à quelques centaines de nœuds, calculée UNE FOIS à l'ouverture de
    /// la vue (jamais en fond). Déterministe à graine égale. Les positions
    /// connues (retenues par le projet, ou épinglées) servent de départ ; les
    /// épinglées ne bougent pas ; les nœuds neufs naissent près de leurs
    /// voisins déjà placés, sinon sur un cercle.</summary>
    public static class GraphLayout
    {
        public const double Spacing = 90; // la distance « naturelle » entre deux nœuds reliés

        public static Dictionary<string, GraphPosition> Arrange(RelationGraph graph, Dictionary<string, GraphPosition> known, int iterations, int seed)
        {
            var result = new Dictionary<string, GraphPosition>();
            var n = graph.Nodes.Count;
            if (n == 0) return result;
            var random = new Random(seed);
            var ids = new string[n];
            var x = new double[n];
            var y = new double[n];
            var pinned = new bool[n];
            var index = new Dictionary<string, int>();
            for (var i = 0; i < n; i++) { ids[i] = graph.Nodes[i].Id; index[ids[i]] = i; }

            // Départ : positions connues, puis les autres près d'un voisin
            // placé, sinon sur un cercle dont le rayon suit le nombre de nœuds.
            var placed = new bool[n];
            var radius = Spacing * Math.Max(1, Math.Sqrt(n)) * 0.6;
            var knownCount = 0;
            for (var i = 0; i < n; i++)
            {
                GraphPosition at;
                if (known != null && known.TryGetValue(ids[i], out at))
                {
                    x[i] = at.X; y[i] = at.Y; placed[i] = true; pinned[i] = at.Pinned;
                    knownCount++;
                }
            }
            for (var i = 0; i < n; i++)
            {
                if (placed[i]) continue;
                var anchorFound = false;
                foreach (var edge in graph.EdgesOf(ids[i]))
                {
                    int j;
                    if (!index.TryGetValue(edge.Other(ids[i]), out j) || !placed[j]) continue;
                    x[i] = x[j] + (random.NextDouble() - 0.5) * Spacing;
                    y[i] = y[j] + (random.NextDouble() - 0.5) * Spacing;
                    anchorFound = true;
                    break;
                }
                if (!anchorFound)
                {
                    var angle = 2 * Math.PI * i / n + random.NextDouble() * 0.1;
                    x[i] = Math.Cos(angle) * radius + (random.NextDouble() - 0.5) * 4;
                    y[i] = Math.Sin(angle) * radius + (random.NextDouble() - 0.5) * 4;
                }
                placed[i] = true;
            }

            // Les voisins (indices) et le poids de leurs liens.
            var neighbours = new List<KeyValuePair<int, double>>[n];
            for (var i = 0; i < n; i++) neighbours[i] = new List<KeyValuePair<int, double>>();
            foreach (var edge in graph.Edges)
            {
                int a, b;
                if (!index.TryGetValue(edge.From, out a) || !index.TryGetValue(edge.To, out b)) continue;
                var weight = edge.Kind == GraphEdgeKind.Presence ? Math.Min(3, 1 + Math.Log(edge.Weight)) : 1;
                neighbours[a].Add(new KeyValuePair<int, double>(b, weight));
                neighbours[b].Add(new KeyValuePair<int, double>(a, weight));
            }

            var k = Spacing;
            var k2 = k * k;
            // Départ chaud (presque tout est déjà posé) : ce qui est posé ne
            // bouge pas, seuls les nœuds neufs cherchent leur place — le
            // graphe retrouvé est celui qu'on a quitté.
            var warm = knownCount >= n * 0.9;
            var hold = new bool[n];
            for (var i = 0; i < n; i++) hold[i] = pinned[i] || (warm && known != null && known.ContainsKey(ids[i]));
            var temperature = warm ? k * 0.5 : Math.Max(k * 2, radius / 4);
            var cooling = temperature / Math.Max(1, iterations);
            var dx = new double[n];
            var dy = new double[n];
            for (var step = 0; step < iterations; step++)
            {
                Array.Clear(dx, 0, n);
                Array.Clear(dy, 0, n);
                // Répulsion entre toutes les paires.
                for (var i = 0; i < n; i++)
                    for (var j = i + 1; j < n; j++)
                    {
                        var ddx = x[i] - x[j];
                        var ddy = y[i] - y[j];
                        var dist2 = ddx * ddx + ddy * ddy;
                        if (dist2 < 0.01) { ddx = (random.NextDouble() - 0.5); ddy = (random.NextDouble() - 0.5); dist2 = ddx * ddx + ddy * ddy + 0.01; }
                        var force = k2 / dist2; // k² / d, appliqué au vecteur unitaire → k² / d² sur le vecteur
                        dx[i] += ddx * force; dy[i] += ddy * force;
                        dx[j] -= ddx * force; dy[j] -= ddy * force;
                    }
                // Attraction le long des arêtes.
                for (var i = 0; i < n; i++)
                    foreach (var pair in neighbours[i])
                    {
                        var j = pair.Key;
                        var ddx = x[i] - x[j];
                        var ddy = y[i] - y[j];
                        var dist = Math.Sqrt(ddx * ddx + ddy * ddy) + 0.01;
                        var force = dist / k * pair.Value; // d² / k sur le vecteur unitaire
                        dx[i] -= ddx * force; dy[i] -= ddy * force;
                    }
                // Gravité douce vers le centre : les îlots ne dérivent pas à l'infini.
                for (var i = 0; i < n; i++) { dx[i] -= x[i] * 0.02; dy[i] -= y[i] * 0.02; }
                // Déplacement borné par la température.
                for (var i = 0; i < n; i++)
                {
                    if (hold[i]) continue;
                    var len = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                    if (len < 0.001) continue;
                    var move = Math.Min(len, temperature);
                    x[i] += dx[i] / len * move;
                    y[i] += dy[i] / len * move;
                    // Le CADRE (Fruchterman-Reingold) : rien ne sort d'un disque
                    // proportionné au nombre de nœuds — les orphelins, que seule
                    // la répulsion pousse, s'arrêtent au bord au lieu de fuir.
                    var frame = radius * 1.7;
                    var fromCenter = Math.Sqrt(x[i] * x[i] + y[i] * y[i]);
                    if (fromCenter > frame) { x[i] *= frame / fromCenter; y[i] *= frame / fromCenter; }
                }
                temperature = Math.Max(1, temperature - cooling);
            }

            // Recentré sur l'origine (les épinglés ne sont pas déplacés :
            // on ne recentre que si rien n'est épinglé).
            var anyPinned = false;
            foreach (var p in hold) if (p) { anyPinned = true; break; }
            if (!anyPinned)
            {
                double cx = 0, cy = 0;
                for (var i = 0; i < n; i++) { cx += x[i]; cy += y[i]; }
                cx /= n; cy /= n;
                for (var i = 0; i < n; i++) { x[i] -= cx; y[i] -= cy; }
            }
            for (var i = 0; i < n; i++)
                result[ids[i]] = new GraphPosition { X = Math.Round(x[i], 1), Y = Math.Round(y[i], 1), Pinned = pinned[i] };
            return result;
        }

        /// <summary>Le nombre d'itérations raisonnable : plus de nœuds, moins
        /// de tours — le coût reste borné (≈ N² × itérations).</summary>
        public static int IterationsFor(int nodes, bool warmStart)
        {
            if (nodes <= 0) return 0;
            var full = nodes <= 100 ? 300 : nodes <= 300 ? 150 : 80;
            return warmStart ? Math.Max(20, full / 4) : full;
        }
    }
}

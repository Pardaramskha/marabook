using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Marabook
{
    /// <summary>LE CHRONO DE LA MAISON (21/11/2026) : où part le temps d'une
    /// frappe, d'une ouverture. Éteint, il ne coûte qu'un test ; allumé
    /// (MARABOOK_PERF=1, ou la sonde de la dette), chaque section nommée
    /// cumule sa durée et son nombre de passages. Sur le fil d'interface
    /// seulement — ce n'est pas un profileur.</summary>
    public static class Perf
    {
        public static bool Enabled = Environment.GetEnvironmentVariable("MARABOOK_PERF") == "1";

        private sealed class Slot { public long Ticks; public int Count; }
        private static readonly Dictionary<string, Slot> Slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        private static readonly List<string> Order = new List<string>();

        /// <summary>Le départ d'une section : à rendre à End. 0 quand le chrono est éteint.</summary>
        public static long Begin() { return Enabled ? Stopwatch.GetTimestamp() : 0; }

        public static void End(string name, long started)
        {
            if (!Enabled || started == 0) return;
            var elapsed = Stopwatch.GetTimestamp() - started;
            lock (Slots)
            {
                Slot slot;
                if (!Slots.TryGetValue(name, out slot)) { Slots[name] = slot = new Slot(); Order.Add(name); }
                slot.Ticks += elapsed;
                slot.Count++;
            }
        }

        public static void Reset() { lock (Slots) { Slots.Clear(); Order.Clear(); } }

        /// <summary>Les millisecondes cumulées d'une section (0 si jamais vue).</summary>
        public static double Milliseconds(string name)
        {
            lock (Slots)
            {
                Slot slot;
                return Slots.TryGetValue(name, out slot) ? slot.Ticks * 1000.0 / Stopwatch.Frequency : 0;
            }
        }

        /// <summary>« composer 412 ms (10×) · peindre 12 ms (10×) ».</summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            lock (Slots)
                foreach (var name in Order)
                {
                    var slot = Slots[name];
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(name).Append(' ').Append((slot.Ticks * 1000.0 / Stopwatch.Frequency).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(" ms (").Append(slot.Count).Append("×)");
                }
            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Generic;

namespace Marabook.Print
{
    /// <summary>LES TABLES TYPOGRAPHIQUES D'UNE POLICE (20/11/2026), lues à la
    /// main dans le fichier OpenType — comme tout le reste, sans
    /// bibliothèque : le CRÉNAGE (les paires de la fonction « kern » de GPOS,
    /// sinon la vieille table « kern »), les LIGATURES courantes (« liga » de
    /// GSUB : fi, fl, ff, ffi…) et les VRAIES PETITES CAPITALES (« smcp »),
    /// quand la police les dessine. Ce que le compositeur ne lisait pas : il
    /// posait chaque glyphe à son avance nue.
    ///
    /// Ne sont lus que les cas simples, qui font l'essentiel du texte latin :
    /// paires (GPOS 2, formats 1 et 2), ligatures (GSUB 4), substitutions
    /// simples (GSUB 1), et leurs enveloppes d'extension. Les substitutions
    /// contextuelles sont ignorées — une police qui ne ligature que par elles
    /// reste sans ligature, jamais avec une fausse.</summary>
    public sealed class OpenTypeLayout
    {
        public int UnitsPerEm = 1000;
        public bool HasKerning { get { return _pairLookups.Count > 0 || _kernTable.Count > 0; } }
        public bool HasLigatures { get { return _ligatures.Count > 0; } }
        public bool HasSmallCaps { get { return _smallCaps.Count > 0; } }

        private sealed class Ligature { public ushort[] Rest; public ushort Glyph; }

        private sealed class PairSubtable
        {
            public Dictionary<ushort, int> Coverage;           // glyphe → rang
            public Dictionary<uint, int> Pairs;                // format 1 : (gauche, droite) → avance
            public Dictionary<ushort, ushort> Class1, Class2;  // format 2
            public int[] Matrix;                               // classe1 × classe2 → avance
            public int Class2Count;
        }

        private readonly List<List<PairSubtable>> _pairLookups = new List<List<PairSubtable>>();
        private readonly Dictionary<uint, int> _kernTable = new Dictionary<uint, int>();
        private readonly Dictionary<ushort, List<Ligature>> _ligatures = new Dictionary<ushort, List<Ligature>>();
        private readonly Dictionary<ushort, ushort> _smallCaps = new Dictionary<ushort, ushort>();
        private readonly Dictionary<uint, int> _kernCache = new Dictionary<uint, int>();

        /// <summary>Les fonctions que la police déclare (« GSUB:liga », « GPOS:kern »…) — pour le diagnostic.</summary>
        public readonly List<string> Features = new List<string>();

        private byte[] _d;

        /// <summary>Lit les tables d'une police (TTF, OTF, ou une face d'une
        /// collection TTC). Jamais null : une police sans tables, ou
        /// illisible, rend une mise en forme vide.</summary>
        public static OpenTypeLayout Load(byte[] font, int faceIndex)
        {
            var layout = new OpenTypeLayout();
            if (font == null || font.Length < 12) return layout;
            try { layout.Parse(font, faceIndex); }
            catch
            {
                // Une table tordue : on garde ce qui a été lu sans faute — rien de faux, au pire rien.
            }
            return layout;
        }

        // ------------------------------------------------------------ lecture

        private int U16(int o) { return o >= 0 && o + 2 <= _d.Length ? (_d[o] << 8) | _d[o + 1] : 0; }
        private int S16(int o) { var v = U16(o); return v >= 0x8000 ? v - 0x10000 : v; }
        private long U32(int o) { return o >= 0 && o + 4 <= _d.Length ? ((long)_d[o] << 24) | ((long)_d[o + 1] << 16) | ((long)_d[o + 2] << 8) | _d[o + 3] : 0; }
        private string Tag(int o) { return o >= 0 && o + 4 <= _d.Length ? System.Text.Encoding.ASCII.GetString(_d, o, 4) : ""; }

        private void Parse(byte[] font, int faceIndex)
        {
            _d = font;
            var start = 0;
            if (Tag(0) == "ttcf")
            {
                var faces = (int)U32(8);
                start = (int)U32(12 + 4 * Math.Max(0, Math.Min(faces - 1, faceIndex)));
            }
            var tables = new Dictionary<string, int>();
            var count = U16(start + 4);
            for (var i = 0; i < count; i++)
            {
                var entry = start + 12 + 16 * i;
                tables[Tag(entry)] = (int)U32(entry + 8);
            }
            int head, gpos, gsub, kern;
            if (tables.TryGetValue("head", out head)) { var upem = U16(head + 18); if (upem > 0) UnitsPerEm = upem; }
            if (tables.TryGetValue("GPOS", out gpos))
                try { ReadGpos(gpos); } catch { _pairLookups.Clear(); }
            if (_pairLookups.Count == 0 && tables.TryGetValue("kern", out kern))
                try { ReadKern(kern); } catch { _kernTable.Clear(); }
            if (tables.TryGetValue("GSUB", out gsub))
                try { ReadGsub(gsub); } catch { _ligatures.Clear(); _smallCaps.Clear(); }
            _d = null;
        }

        /// <summary>Les tables de consultation (lookups) d'une fonction, pour
        /// l'écriture latine et l'écriture par défaut — toutes les écritures
        /// si la police n'en déclare aucune des deux.</summary>
        private List<int> LookupsOf(int table, string feature)
        {
            var scriptList = table + U16(table + 4);
            var featureList = table + U16(table + 6);
            var wanted = new HashSet<int>();
            var any = new HashSet<int>();
            var scripts = U16(scriptList);
            for (var s = 0; s < scripts; s++)
            {
                var tag = Tag(scriptList + 2 + 6 * s);
                var script = scriptList + U16(scriptList + 2 + 6 * s + 4);
                var systems = new List<int>();
                var fallback = U16(script);
                if (fallback != 0) systems.Add(script + fallback);
                var languages = U16(script + 2);
                for (var l = 0; l < languages; l++) systems.Add(script + U16(script + 4 + 6 * l + 4));
                foreach (var system in systems)
                {
                    var features = U16(system + 4);
                    for (var f = 0; f < features; f++)
                    {
                        var index = U16(system + 6 + 2 * f);
                        any.Add(index);
                        if (tag == "latn" || tag == "DFLT") wanted.Add(index);
                    }
                }
            }
            if (wanted.Count == 0) wanted = any;
            var lookups = new List<int>();
            var featureCount = U16(featureList);
            var owner = feature == "kern" ? "GPOS" : "GSUB";
            for (var f = 0; f < featureCount; f++)
            {
                var name = owner + ":" + Tag(featureList + 2 + 6 * f);
                if (!Features.Contains(name)) Features.Add(name);
            }
            for (var f = 0; f < featureCount; f++)
            {
                if (!wanted.Contains(f) && scripts > 0) continue;
                if (Tag(featureList + 2 + 6 * f) != feature) continue;
                var record = featureList + U16(featureList + 2 + 6 * f + 4);
                var n = U16(record + 2);
                for (var i = 0; i < n; i++)
                {
                    var lookup = U16(record + 4 + 2 * i);
                    if (!lookups.Contains(lookup)) lookups.Add(lookup);
                }
            }
            lookups.Sort();
            return lookups;
        }

        /// <summary>Les sous-tables d'un lookup, l'enveloppe d'extension ôtée :
        /// (type réel, offset).</summary>
        private List<KeyValuePair<int, int>> Subtables(int table, int lookupIndex, int extensionType)
        {
            var result = new List<KeyValuePair<int, int>>();
            var lookupList = table + U16(table + 8);
            if (lookupIndex >= U16(lookupList)) return result;
            var lookup = lookupList + U16(lookupList + 2 + 2 * lookupIndex);
            var type = U16(lookup);
            var count = U16(lookup + 4);
            for (var i = 0; i < count; i++)
            {
                var sub = lookup + U16(lookup + 6 + 2 * i);
                var real = type;
                if (type == extensionType && U16(sub) == 1)
                {
                    real = U16(sub + 2);
                    sub = sub + (int)U32(sub + 4);
                }
                result.Add(new KeyValuePair<int, int>(real, sub));
            }
            return result;
        }

        private Dictionary<ushort, int> Coverage(int o)
        {
            var map = new Dictionary<ushort, int>();
            var format = U16(o);
            var count = U16(o + 2);
            if (format == 1)
                for (var i = 0; i < count; i++) map[(ushort)U16(o + 4 + 2 * i)] = i;
            else if (format == 2)
                for (var i = 0; i < count; i++)
                {
                    int first = U16(o + 4 + 6 * i), last = U16(o + 6 + 6 * i), index = U16(o + 8 + 6 * i);
                    for (var g = first; g <= last && g - first < 65536; g++) map[(ushort)g] = index + g - first;
                }
            return map;
        }

        private Dictionary<ushort, ushort> ClassDef(int o)
        {
            var map = new Dictionary<ushort, ushort>();
            var format = U16(o);
            if (format == 1)
            {
                int first = U16(o + 2), count = U16(o + 4);
                for (var i = 0; i < count; i++) { var c = U16(o + 6 + 2 * i); if (c != 0) map[(ushort)(first + i)] = (ushort)c; }
            }
            else if (format == 2)
            {
                var count = U16(o + 2);
                for (var i = 0; i < count; i++)
                {
                    int first = U16(o + 4 + 6 * i), last = U16(o + 6 + 6 * i), c = U16(o + 8 + 6 * i);
                    if (c == 0) continue;
                    for (var g = first; g <= last && g - first < 65536; g++) map[(ushort)g] = (ushort)c;
                }
            }
            return map;
        }

        private static int Bits(int value)
        {
            var n = 0;
            for (; value != 0; value >>= 1) n += value & 1;
            return n;
        }

        // ------------------------------------------------------------ GPOS : crénage

        private void ReadGpos(int gpos)
        {
            foreach (var index in LookupsOf(gpos, "kern"))
            {
                var subtables = new List<PairSubtable>();
                foreach (var pair in Subtables(gpos, index, 9))
                {
                    if (pair.Key != 2) continue;
                    var o = pair.Value;
                    var format = U16(o);
                    int format1 = U16(o + 4), format2 = U16(o + 6);
                    if ((format1 & 0x0004) == 0) continue;            // pas d'avance sur le premier glyphe : rien pour nous
                    var advanceAt = 2 * Bits(format1 & 0x0003);       // XAdvance vient après XPlacement et YPlacement
                    int size1 = 2 * Bits(format1), size2 = 2 * Bits(format2);
                    var sub = new PairSubtable { Coverage = Coverage(o + U16(o + 2)) };
                    if (format == 1)
                    {
                        sub.Pairs = new Dictionary<uint, int>();
                        var sets = U16(o + 8);
                        var firsts = new ushort[sets];
                        foreach (var covered in sub.Coverage) if (covered.Value < sets) firsts[covered.Value] = covered.Key;
                        for (var s = 0; s < sets; s++)
                        {
                            var set = o + U16(o + 10 + 2 * s);
                            var n = U16(set);
                            var record = 2 + size1 + size2;
                            for (var i = 0; i < n; i++)
                            {
                                var at = set + 2 + record * i;
                                var value = S16(at + 2 + advanceAt);
                                if (value != 0) sub.Pairs[((uint)firsts[s] << 16) | (uint)U16(at)] = value;
                            }
                        }
                    }
                    else if (format == 2)
                    {
                        sub.Class1 = ClassDef(o + U16(o + 8));
                        sub.Class2 = ClassDef(o + U16(o + 10));
                        int count1 = U16(o + 12), count2 = U16(o + 14);
                        if ((long)count1 * count2 > 4000000) continue;
                        sub.Class2Count = count2;
                        sub.Matrix = new int[count1 * count2];
                        var record = size1 + size2;
                        for (var a = 0; a < count1; a++)
                            for (var b = 0; b < count2; b++)
                                sub.Matrix[a * count2 + b] = S16(o + 16 + (a * count2 + b) * record + advanceAt);
                    }
                    else continue;
                    subtables.Add(sub);
                }
                if (subtables.Count > 0) _pairLookups.Add(subtables);
            }
        }

        /// <summary>La vieille table « kern » : ses sous-tables horizontales au format 0.</summary>
        private void ReadKern(int kern)
        {
            if (U16(kern) != 0) return; // la variante d'Apple : ignorée
            var tables = U16(kern + 2);
            var o = kern + 4;
            for (var t = 0; t < tables; t++)
            {
                var length = U16(o + 2);
                var coverage = U16(o + 4);
                if ((coverage >> 8) == 0 && (coverage & 1) == 1 && (coverage & 4) == 0)
                {
                    var pairs = U16(o + 6);
                    for (var i = 0; i < pairs; i++)
                    {
                        var at = o + 14 + 6 * i;
                        var value = S16(at + 4);
                        if (value != 0) _kernTable[((uint)U16(at) << 16) | (uint)U16(at + 2)] = value;
                    }
                }
                if (length <= 0) break;
                o += length;
            }
        }

        // ------------------------------------------------------------ GSUB : ligatures, petites capitales

        private void ReadGsub(int gsub)
        {
            foreach (var index in LookupsOf(gsub, "liga"))
                foreach (var pair in Subtables(gsub, index, 7))
                {
                    if (pair.Key != 4 || U16(pair.Value) != 1) continue;
                    var o = pair.Value;
                    var coverage = Coverage(o + U16(o + 2));
                    var sets = U16(o + 4);
                    foreach (var covered in coverage)
                    {
                        if (covered.Value >= sets) continue;
                        var set = o + U16(o + 6 + 2 * covered.Value);
                        var n = U16(set);
                        for (var i = 0; i < n; i++)
                        {
                            var ligature = set + U16(set + 2 + 2 * i);
                            var components = U16(ligature + 2);
                            if (components < 2 || components > 8) continue;
                            var rest = new ushort[components - 1];
                            for (var k = 0; k < rest.Length; k++) rest[k] = (ushort)U16(ligature + 4 + 2 * k);
                            List<Ligature> list;
                            if (!_ligatures.TryGetValue(covered.Key, out list)) _ligatures[covered.Key] = list = new List<Ligature>();
                            list.Add(new Ligature { Rest = rest, Glyph = (ushort)U16(ligature) });
                        }
                    }
                }
            // La plus longue d'abord : « ffi » avant « ff ».
            foreach (var list in _ligatures.Values)
                list.Sort(delegate(Ligature a, Ligature b) { return b.Rest.Length.CompareTo(a.Rest.Length); });

            foreach (var index in LookupsOf(gsub, "smcp"))
                foreach (var pair in Subtables(gsub, index, 7))
                {
                    if (pair.Key != 1) continue;
                    var o = pair.Value;
                    var format = U16(o);
                    var coverage = Coverage(o + U16(o + 2));
                    if (format == 1)
                    {
                        var delta = S16(o + 4);
                        foreach (var covered in coverage) if (!_smallCaps.ContainsKey(covered.Key)) _smallCaps[covered.Key] = (ushort)((covered.Key + delta) & 0xFFFF);
                    }
                    else if (format == 2)
                    {
                        var n = U16(o + 4);
                        foreach (var covered in coverage)
                            if (covered.Value < n && !_smallCaps.ContainsKey(covered.Key)) _smallCaps[covered.Key] = (ushort)U16(o + 6 + 2 * covered.Value);
                    }
                }
        }

        // ------------------------------------------------------------ service

        /// <summary>Le crénage de la paire, en unités de la police (négatif : les glyphes se rapprochent).</summary>
        public int Kern(ushort left, ushort right)
        {
            var key = ((uint)left << 16) | right;
            int value;
            lock (_kernCache)
            {
                if (_kernCache.TryGetValue(key, out value)) return value;
            }
            value = 0;
            if (_pairLookups.Count > 0)
            {
                foreach (var lookup in _pairLookups)
                    foreach (var sub in lookup)
                    {
                        if (!sub.Coverage.ContainsKey(left)) continue;
                        if (sub.Pairs != null)
                        {
                            int found;
                            if (!sub.Pairs.TryGetValue(key, out found)) continue; // format 1 : la paire n'y est pas, la sous-table suivante a la parole
                            value += found;
                        }
                        else
                        {
                            ushort c1, c2;
                            sub.Class1.TryGetValue(left, out c1);
                            sub.Class2.TryGetValue(right, out c2);
                            var at = c1 * sub.Class2Count + c2;
                            if (at < sub.Matrix.Length) value += sub.Matrix[at];
                        }
                        break; // une sous-table a répondu : ce lookup a fini
                    }
            }
            else _kernTable.TryGetValue(key, out value);
            lock (_kernCache)
            {
                if (_kernCache.Count > 200000) _kernCache.Clear();
                _kernCache[key] = value;
            }
            return value;
        }

        /// <summary>La ligature qui commence à ce glyphe, s'il y en a une : son glyphe et le nombre de glyphes qu'elle remplace.</summary>
        public bool TryLigature(IList<ushort> glyphs, int at, out ushort ligature, out int count)
        {
            ligature = 0;
            count = 0;
            List<Ligature> list;
            if (at < 0 || at >= glyphs.Count || !_ligatures.TryGetValue(glyphs[at], out list)) return false;
            foreach (var candidate in list)
            {
                if (at + candidate.Rest.Length >= glyphs.Count) continue;
                var match = true;
                for (var k = 0; k < candidate.Rest.Length && match; k++)
                    if (glyphs[at + 1 + k] != candidate.Rest[k]) match = false;
                if (!match) continue;
                ligature = candidate.Glyph;
                count = candidate.Rest.Length + 1;
                return true;
            }
            return false;
        }

        /// <summary>La petite capitale dessinée pour ce glyphe, si la police en a une.</summary>
        public bool TrySmallCap(ushort glyph, out ushort small)
        {
            return _smallCaps.TryGetValue(glyph, out small);
        }
    }

    /// <summary>Un moteur de polices qui sait lire les tables typographiques
    /// de ses faces (crénage, ligatures, petites capitales). Le stub des
    /// tests ne l'est pas : la composition y reste nue et déterministe.</summary>
    public interface IFontShaper
    {
        /// <summary>Les tables de la face ; null si elle n'en a pas d'exploitables.</summary>
        OpenTypeLayout Layout(FaceInfo face);
    }
}

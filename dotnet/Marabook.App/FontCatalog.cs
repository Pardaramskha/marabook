using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>La liste des polices installées (0.50.0), UNE pour toute
    /// l'application — le ruban, l'éditeur de styles, le séparateur et les
    /// gabarits énuméraient chacun Fonts.SystemFontFamilies, sans tri sûr ni
    /// rechargement. Triée par nom (culture courante, sans casse) ; se
    /// recharge sur WM_FONTCHANGE (MainWindow) et prévient les sélecteurs.
    /// Une police installée en cours de session vient des dossiers de polices
    /// (Windows et utilisateur) que WPF relit, quand la collection système
    /// ne la voit pas encore : son aperçu passe par la famille ainsi lue.</summary>
    public static class FontCatalog
    {
        public sealed class Entry
        {
            public string Name { get; set; }
            public FontFamily Family { get; set; }
            public bool IsSeparator { get { return string.IsNullOrEmpty(Name); } }
            /// <summary>Le doublon d'une favorite en tête de liste (0.50.0) :
            /// une étoile devant son nom dans les sélecteurs.</summary>
            public bool IsFavoriteCopy { get; set; }
            public string Badge { get { return IsFavoriteCopy ? "★" : ""; } }
            public override string ToString() { return Name ?? ""; }
        }

        /// <summary>L'ordre d'un sélecteur (0.50.0, catalogue de polices) :
        /// les FAVORITES en tête (des doublons marqués), un trait, les
        /// RÉCENTES (doublons, sans celles déjà favorites), un trait, puis le
        /// catalogue entier — les polices EXCLUES n'apparaissent nulle part,
        /// favorites comprises. Pure : testable sans fenêtre.</summary>
        public static List<Entry> Arrange(IList<Entry> catalog, IList<string> favorites, IList<string> recents,
            IList<string> excluded, int recentCount)
        {
            // L'ordre est calculé dans le cœur (FontOrder, testé par C43) ; ici
            // chaque case reçoit sa famille — l'entrée du catalogue reste
            // l'objet d'origine, favorites et récentes sont des copies.
            var names = new List<string>();
            foreach (var entry in catalog) names.Add(entry.Name);
            var result = new List<Entry>();
            foreach (var slot in FontOrder.Arrange(names, favorites, recents, excluded, recentCount))
            {
                if (slot.IsSeparator) { result.Add(new Entry { Name = "" }); continue; }
                var entry = FindIn(catalog, slot.Name);
                if (entry == null) continue;
                result.Add(slot.IsCopy ? new Entry { Name = entry.Name, Family = entry.Family, IsFavoriteCopy = slot.IsFavoriteCopy } : entry);
            }
            return result;
        }

        private static Entry FindIn(IList<Entry> entries, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var entry in entries)
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }

        private static bool IsIn(IList<string> names, string name)
        {
            if (names == null) return false;
            foreach (var candidate in names)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool IsIn(IList<Entry> entries, string name)
        {
            return FindIn(entries, name) != null;
        }

        /// <summary>Le nombre de styles (graisses × italique) d'une famille,
        /// 0 si la police ne se lit pas.</summary>
        public static int StyleCount(FontFamily family)
        {
            if (family == null) return 0;
            var count = 0;
            try
            {
                // Avalonia n'énumère pas les styles : chaque graisse demandée,
                // droite et italique, compte si la face rendue est la vraie.
                foreach (var weight in new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 })
                    foreach (var style in new[] { FontStyle.Normal, FontStyle.Italic })
                    {
                        IGlyphTypeface glyphs;
                        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(family, style, (FontWeight)weight), out glyphs)) continue;
                        if ((int)glyphs.Weight != weight || glyphs.Style != style || glyphs.FontSimulations != FontSimulations.None) continue;
                        count++;
                    }
            }
            catch (Exception) { }
            return count;
        }

        private static readonly Dictionary<string, List<int>> _realWeights = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Les graisses RÉELLEMENT installées d'une famille, droites
        /// (01/10, variante de police des styles) : 400 au moins. Une police
        /// variable n'en offre qu'une — demander une graisse à son axe
        /// empoisonne la face maigre (AvaloniaFontEngine, 29/09), le gras y
        /// reste émulé. Mémorisé par famille ; InvalidateRealWeights après un
        /// rechargement du catalogue.</summary>
        public static List<int> RealWeights(string family)
        {
            if (string.IsNullOrEmpty(family)) return new List<int> { 400 };
            List<int> cached;
            lock (_realWeights)
            {
                if (_realWeights.TryGetValue(family, out cached)) return new List<int>(cached);
            }
            var result = new List<int>();
            try
            {
                var fontFamily = new FontFamily(family);
                var regular = new Typeface(fontFamily, FontStyle.Normal, FontWeight.Normal);
                if (!AvaloniaFontEngine.IsVariableFamily(family, false, regular))
                    foreach (var weight in new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 })
                    {
                        IGlyphTypeface glyphs;
                        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(fontFamily, FontStyle.Normal, (FontWeight)weight), out glyphs)) continue;
                        if (glyphs == null || (int)glyphs.Weight != weight || glyphs.Style != FontStyle.Normal || glyphs.FontSimulations != FontSimulations.None) continue;
                        if (!string.Equals(glyphs.FamilyName, family, StringComparison.OrdinalIgnoreCase)) continue; // la face de repli d'une autre famille ne compte pas
                        result.Add(weight);
                    }
            }
            catch (Exception) { }
            if (!result.Contains(400)) result.Insert(0, 400);
            result.Sort();
            lock (_realWeights) _realWeights[family] = result;
            return new List<int>(result);
        }

        public static void InvalidateRealWeights()
        {
            lock (_realWeights) _realWeights.Clear();
        }

        private static List<Entry> _entries;
        private static DispatcherTimer _refresh;

        /// <summary>La liste a été rechargée : les sélecteurs se rebâtissent.</summary>
        public static event Action Changed;

        public static IList<Entry> Entries
        {
            get
            {
                if (_entries == null) _entries = Enumerate();
                return _entries;
            }
        }

        public static List<string> Names()
        {
            var names = new List<string>();
            foreach (var entry in Entries) names.Add(entry.Name);
            return names;
        }

        /// <summary>L'entrée de ce nom (sans casse), ou null.</summary>
        public static Entry Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var entry in Entries)
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }

        /// <summary>La famille à employer pour dessiner ce nom : celle du
        /// catalogue (une police fraîchement installée y est lue depuis son
        /// fichier), sinon la résolution WPF ordinaire.</summary>
        public static FontFamily FamilyOf(string name)
        {
            var entry = Find(name);
            if (entry != null && entry.Family != null) return entry.Family;
            try { return new FontFamily(name); }
            catch (Exception) { return new FontFamily("Times New Roman"); }
        }

        /// <summary>Recharge la liste tout de suite.</summary>
        public static void Refresh()
        {
            _entries = Enumerate();
            InvalidateRealWeights();
            var handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>Recharge bientôt : Windows envoie WM_FONTCHANGE plusieurs
        /// fois pour une installation, on n'énumère qu'une fois, 600 ms après
        /// la dernière.</summary>
        public static void RequestRefresh()
        {
            if (_refresh == null)
            {
                _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
                _refresh.Tick += delegate
                {
                    _refresh.Stop();
                    Refresh();
                };
            }
            _refresh.Stop();
            _refresh.Start();
        }

        private static List<Entry> Enumerate()
        {
            var byName = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var family in FontManager.Current.SystemFonts)
                    Add(byName, family.Name, family);
            }
            catch (Exception) { }
            var entries = new List<Entry>(byName.Values);
            entries.Sort(delegate(Entry a, Entry b)
            {
                return string.Compare(a.Name, b.Name, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
            });
            return entries;
        }

        private static void Add(Dictionary<string, Entry> byName, string name, FontFamily family)
        {
            if (string.IsNullOrEmpty(name) || byName.ContainsKey(name)) return;
            byName[name] = new Entry { Name = name, Family = family };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Marabook.View
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
            public override string ToString() { return Name ?? ""; }
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
                foreach (var family in Fonts.SystemFontFamilies)
                    Add(byName, NameOf(family), new FontFamily(family.Source));
            }
            catch (Exception) { }
            // Les dossiers de polices, relus à chaque énumération : une police
            // installée pendant la session y est, même si la collection
            // système d'au-dessus ne l'a pas encore.
            foreach (var folder in FontFolders())
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var family in Fonts.GetFontFamilies(new Uri(folder.TrimEnd('\\') + "\\")))
                        Add(byName, NameOf(family), family);
                }
                catch (Exception) { }
            }
            var entries = new List<Entry>(byName.Values);
            entries.Sort(delegate(Entry a, Entry b)
            {
                return string.Compare(a.Name, b.Name, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
            });
            return entries;
        }

        private static IEnumerable<string> FontFolders()
        {
            var list = new List<string>();
            try { list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts")); }
            catch (Exception) { }
            try
            {
                list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine("Microsoft", Path.Combine("Windows", "Fonts"))));
            }
            catch (Exception) { }
            return list;
        }

        private static void Add(Dictionary<string, Entry> byName, string name, FontFamily family)
        {
            if (string.IsNullOrEmpty(name) || byName.ContainsKey(name)) return;
            byName[name] = new Entry { Name = name, Family = family };
        }

        /// <summary>Le nom lisible d'une famille : celui de la culture
        /// d'interface, sinon l'anglais, sinon le premier ; pour une famille
        /// lue depuis un dossier, la partie après « # » de sa source.</summary>
        private static string NameOf(FontFamily family)
        {
            try
            {
                var names = family.FamilyNames;
                string name;
                if (names.TryGetValue(XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag), out name) && name.Length > 0)
                    return name;
                if (names.TryGetValue(XmlLanguage.GetLanguage("en-us"), out name) && name.Length > 0)
                    return name;
                foreach (var value in names.Values)
                    if (!string.IsNullOrEmpty(value)) return value;
            }
            catch (Exception) { }
            var source = family.Source ?? "";
            var hash = source.LastIndexOf('#');
            return hash >= 0 ? source.Substring(hash + 1) : source;
        }
    }
}

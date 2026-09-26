using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests
{
    /// <summary>C43 — le catalogue de polices (0.50.0) : l'ordre des
    /// sélecteurs (favorites en tête en doublons marqués, récentes, tout le
    /// catalogue sans les exclues), les bascules favori/exclure (sans casse,
    /// aller-retour) et leur persistance dans settings.json.</summary>
    public static class FontPrefsTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C43 — catalogue de polices (0.50.0) : favorites, exclusions, ordre des sélecteurs");
            Arrange(t);
            Toggles(t);
        }

        private static List<FontCatalog.Entry> Catalog(params string[] names)
        {
            var list = new List<FontCatalog.Entry>();
            foreach (var name in names) list.Add(new FontCatalog.Entry { Name = name, Family = new FontFamily("Times New Roman") });
            return list;
        }

        private static string Names(List<FontCatalog.Entry> entries)
        {
            var parts = new List<string>();
            foreach (var entry in entries) parts.Add(entry.IsSeparator ? "|" : (entry.IsFavoriteCopy ? "*" : "") + entry.Name);
            return string.Join(" ", parts.ToArray());
        }

        private static void Arrange(Harness t)
        {
            var catalog = Catalog("Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot");
            var arranged = FontCatalog.Arrange(catalog,
                new List<string> { "Charlie", "Inconnue", "Echo", "charlie" },
                new List<string> { "Echo", "Alpha", "Bravo", "Delta", "Foxtrot" },
                new List<string> { "bravo", "Echo" }, 3);
            t.Equal("*Charlie | Alpha Delta Foxtrot | Alpha Charlie Delta Foxtrot", Names(arranged),
                "favorites (doublons marqués, sans l'exclue ni l'inconnue ni le doublon), trait, récentes (3 au plus, sans exclues), trait, catalogue sans exclues");
            t.Check(!ReferenceEquals(arranged[0], catalog[2]) && ReferenceEquals(arranged[7], catalog[2]),
                "la favorite en tête est une copie, celle du catalogue reste l'objet d'origine");
            t.Equal("★", arranged[0].Badge, "le doublon d'une favorite porte l'étoile");
            t.Equal("", arranged[7].Badge, "l'entrée du catalogue n'en porte pas");

            var plain = FontCatalog.Arrange(catalog, new List<string>(), new List<string>(), new List<string>(), 5);
            t.Equal("Alpha Bravo Charlie Delta Echo Foxtrot", Names(plain), "sans favorites ni récentes : le catalogue seul, sans trait");

            var onlyFavorites = FontCatalog.Arrange(catalog, new List<string> { "Delta" }, new List<string> { "Delta" }, new List<string>(), 5);
            t.Equal("*Delta | Alpha Bravo Charlie Delta Echo Foxtrot", Names(onlyFavorites),
                "une récente déjà favorite ne se double pas en récente");
        }

        private static void Toggles(Harness t)
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-c43-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.json");
            var savedFavorites = AppSettings.FavoriteFonts;
            var savedExcluded = AppSettings.ExcludedFonts;
            AppSettings.PathOverride = path;
            try
            {
                AppSettings.FavoriteFonts = new List<string>();
                AppSettings.ExcludedFonts = new List<string>();
                var fired = 0;
                Action handler = delegate { fired++; };
                AppSettings.FontPrefsChanged += handler;
                try
                {
                    t.Check(AppSettings.ToggleFavoriteFont("Garamond"), "ajouter aux favoris rend vrai");
                    t.Check(AppSettings.IsFavoriteFont("garamond"), "favori sans casse");
                    t.Check(!AppSettings.ToggleFavoriteFont("GARAMOND"), "la seconde bascule retire (sans casse)");
                    t.Check(!AppSettings.IsFavoriteFont("Garamond") && AppSettings.FavoriteFonts.Count == 0, "…et la liste est vide");
                    AppSettings.ToggleFavoriteFont("Garamond");
                    AppSettings.ToggleFavoriteFont("Cambria");
                    t.Check(AppSettings.ToggleExcludedFont("Comic Sans MS") && AppSettings.IsExcludedFont("comic sans ms"), "exclure rend vrai, sans casse");
                    t.Equal(5, fired, "chaque bascule prévient les sélecteurs");
                    t.Check(!AppSettings.ToggleFavoriteFont(""), "un nom vide ne fait rien");
                }
                finally { AppSettings.FontPrefsChanged -= handler; }

                var json = File.ReadAllText(path);
                t.Check(json.Contains("\"favoriteFonts\"") && json.Contains("\"Garamond\"") && json.Contains("\"Cambria\"")
                    && json.Contains("\"excludedFonts\"") && json.Contains("\"Comic Sans MS\""), "favorites et exclusions sont dans settings.json");
                AppSettings.FavoriteFonts = new List<string>();
                AppSettings.ExcludedFonts = new List<string>();
                AppSettings.Load();
                t.Check(AppSettings.FavoriteFonts.Count == 2 && AppSettings.FavoriteFonts[0] == "Garamond" && AppSettings.FavoriteFonts[1] == "Cambria",
                    "les favorites reviennent dans leur ordre");
                t.Check(AppSettings.ExcludedFonts.Count == 1 && AppSettings.ExcludedFonts[0] == "Comic Sans MS", "l'exclusion revient");
            }
            finally
            {
                AppSettings.PathOverride = null;
                AppSettings.FavoriteFonts = savedFavorites;
                AppSettings.ExcludedFonts = savedExcluded;
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}

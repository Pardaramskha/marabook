using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>Une couleur de la palette : son code « #RRGGBB » et son nom.</summary>
    public sealed class PaletteColor
    {
        public string Hex;
        public string Name;

        public PaletteColor(string hex, string name)
        {
            Hex = hex;
            Name = name ?? "";
        }
    }

    /// <summary>Un nuancier d'origine : son nom, à quoi il sert, ses couleurs.</summary>
    public sealed class PaletteGroup
    {
        public string Name;
        public string Use;
        public PaletteColor[] Colors;
    }

    /// <summary>LA PALETTE (2.0.0, 22/11/2026, Rémi) : toutes les couleurs que
    /// Marabook propose — tuiles, icônes, catégories, types d'événements,
    /// texte et surlignage, zones de carte, astres — réunies et NOMMÉES, plus
    /// les couleurs personnalisées de l'auteur, qu'il ajoute, renomme, change
    /// et retire dans Préférences › Personnalisation › Palette.
    ///
    /// Une couleur reste enregistrée par son CODE dans le projet : la palette
    /// ne fait que la nommer. Les couleurs d'origine prennent leur nom au lieu
    /// de leur code partout où une couleur s'affiche ; une couleur
    /// personnalisée sans nom garde son code.
    ///
    /// Les couleurs personnalisées vivent dans les réglages (elles suivent
    /// l'auteur d'un projet à l'autre) ; celles qu'un projet portait déjà
    /// (Project.CustomColors, d'avant la palette) y sont reprises à
    /// l'ouverture — Absorb.</summary>
    public static class ColorPalette
    {
        /// <summary>Le nuancier général : tuiles, icônes, catégories, types d'événements, couleur du texte.</summary>
        public static readonly PaletteColor[] General =
        {
            new PaletteColor("#5B67D8", "Indigo"),
            new PaletteColor("#C0392B", "Rouge"),
            new PaletteColor("#E67E22", "Orange"),
            new PaletteColor("#C9A227", "Or"),
            new PaletteColor("#27AE60", "Vert"),
            new PaletteColor("#16A085", "Sarcelle"),
            new PaletteColor("#2980B9", "Bleu"),
            new PaletteColor("#8E44AD", "Violet"),
            new PaletteColor("#7F8C8D", "Gris"),
            new PaletteColor("#703C2F", "Brun")
        };

        /// <summary>Le surlignage du texte : des teintes pâles.</summary>
        public static readonly PaletteColor[] Highlights =
        {
            new PaletteColor("#FFF3A3", "Jaune pâle"),
            new PaletteColor("#FFD9A8", "Pêche"),
            new PaletteColor("#D3F8D3", "Menthe"),
            new PaletteColor("#D0E8FF", "Ciel"),
            new PaletteColor("#FFD6E7", "Rose pâle"),
            new PaletteColor("#E5D4FF", "Lilas"),
            new PaletteColor("#E8E8E8", "Gris clair")
        };

        /// <summary>Les zones et les tracés des cartes.</summary>
        public static readonly PaletteColor[] Maps =
        {
            new PaletteColor("#5B67D8", "Indigo"),
            new PaletteColor("#C0504D", "Brique"),
            new PaletteColor("#3E9C7A", "Jade"),
            new PaletteColor("#B5843A", "Ocre"),
            new PaletteColor("#8E6BBF", "Glycine"),
            new PaletteColor("#0E7C7B", "Pétrole"),
            new PaletteColor("#D9603A", "Corail"),
            new PaletteColor("#6B7280", "Ardoise")
        };

        /// <summary>Les astres de l'Astrolabe : leur teinte et celle de leurs anneaux.</summary>
        public static readonly PaletteColor[] Bodies =
        {
            new PaletteColor("#F5C542", "Soleil"),
            new PaletteColor("#E8873A", "Ambre"),
            new PaletteColor("#D9503A", "Vermillon"),
            new PaletteColor("#E7E3D8", "Ivoire"),
            new PaletteColor("#9DB7E8", "Pervenche"),
            new PaletteColor("#6FC3C0", "Lagon"),
            new PaletteColor("#B48EE0", "Mauve"),
            new PaletteColor("#8FCB7A", "Tilleul")
        };

        public static readonly PaletteGroup[] Groups =
        {
            new PaletteGroup { Name = "Couleurs de Marabook", Use = "Tuiles, icônes, catégories de fiches, types d'événements, couleur du texte", Colors = General },
            new PaletteGroup { Name = "Surlignage", Use = "Le surligneur du ruban Texte", Colors = Highlights },
            new PaletteGroup { Name = "Cartes", Use = "Zones et tracés des cartes", Colors = Maps },
            new PaletteGroup { Name = "Astres", Use = "Teinte des astres et de leurs anneaux, dans l'Astrolabe", Colors = Bodies }
        };

        /// <summary>Les couleurs personnalisées de l'auteur, dans l'ordre où il les a rangées.</summary>
        public static readonly List<PaletteColor> Custom = new List<PaletteColor>();

        /// <summary>Les couleurs de projet que l'auteur a retirées de sa palette : elles n'y reviennent pas
        /// d'elles-mêmes à la prochaine ouverture d'un projet qui les porte encore.</summary>
        public static readonly HashSet<string> Dismissed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>La palette a changé (une couleur ajoutée, renommée, changée, retirée).</summary>
        public static event Action Changed;

        private static void Raise()
        {
            var handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>« #RRGGBB » en capitales, ou null si ce n'est pas une couleur.</summary>
        public static string Normalize(string hex)
        {
            if (hex == null) return null;
            var text = hex.Trim();
            if (text.Length == 6) text = "#" + text;
            if (text.Length != 7 || text[0] != '#') return null;
            for (var i = 1; i < 7; i++) if (!Uri.IsHexDigit(text[i])) return null;
            return text.ToUpperInvariant();
        }

        private static bool Same(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static PaletteColor FindCustom(string hex)
        {
            foreach (var color in Custom) if (Same(color.Hex, hex)) return color;
            return null;
        }

        public static bool IsDefault(string hex)
        {
            foreach (var group in Groups)
                foreach (var color in group.Colors) if (Same(color.Hex, hex)) return true;
            return false;
        }

        /// <summary>Le nom de cette couleur : celui que l'auteur lui a donné, sinon son nom
        /// d'origine ; null si elle n'en a pas.</summary>
        public static string NameOf(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            var custom = FindCustom(hex);
            if (custom != null && custom.Name.Length > 0) return custom.Name;
            foreach (var group in Groups)
                foreach (var color in group.Colors) if (Same(color.Hex, hex)) return color.Name;
            return null;
        }

        /// <summary>Ce qu'on affiche pour cette couleur : son nom, sinon son code.</summary>
        public static string Label(string hex)
        {
            return NameOf(hex) ?? hex ?? "";
        }

        /// <summary>Le nom et le code : pour une infobulle.</summary>
        public static string Describe(string hex)
        {
            var name = NameOf(hex);
            return name == null ? hex ?? "" : name + " — " + hex.ToUpperInvariant();
        }

        /// <summary>Ajoute une couleur personnalisée (en tête). Déjà là : son nom est mis à
        /// jour s'il en vient un. Rend la couleur, ou null si le code n'en est pas un.</summary>
        public static PaletteColor Add(string hex, string name)
        {
            var clean = Normalize(hex);
            if (clean == null) return null;
            Dismissed.Remove(clean);
            var existing = FindCustom(clean);
            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(name) && existing.Name != name.Trim()) { existing.Name = name.Trim(); Raise(); }
                return existing;
            }
            var color = new PaletteColor(clean, (name ?? "").Trim());
            Custom.Insert(0, color);
            Raise();
            return color;
        }

        public static void Rename(PaletteColor color, string name)
        {
            if (color == null) return;
            var clean = (name ?? "").Trim();
            if (color.Name == clean) return;
            color.Name = clean;
            Raise();
        }

        /// <summary>Change le code d'une couleur personnalisée. Faux si le nouveau code n'en est pas un,
        /// ou s'il est déjà celui d'une autre couleur personnalisée.</summary>
        public static bool Recolor(PaletteColor color, string hex)
        {
            var clean = Normalize(hex);
            if (color == null || clean == null) return false;
            if (Same(color.Hex, clean)) return true;
            var other = FindCustom(clean);
            if (other != null && !ReferenceEquals(other, color)) return false;
            color.Hex = clean;
            Dismissed.Remove(clean);
            Raise();
            return true;
        }

        public static void Remove(PaletteColor color)
        {
            if (color == null || !Custom.Remove(color)) return;
            Dismissed.Add(color.Hex);
            Raise();
        }

        /// <summary>Reprend les couleurs personnalisées d'un projet (d'avant la palette) : celles que la
        /// palette n'a pas, et que l'auteur n'en a pas retirées. Vrai si la palette a grandi.</summary>
        public static bool Absorb(IEnumerable<string> projectColors)
        {
            if (projectColors == null) return false;
            var grown = false;
            foreach (var hex in projectColors)
            {
                var clean = Normalize(hex);
                if (clean == null || Dismissed.Contains(clean) || FindCustom(clean) != null) continue;
                Custom.Add(new PaletteColor(clean, ""));
                grown = true;
            }
            if (grown) Raise();
            return grown;
        }

        // ------------------------------------------------------------ réglages

        public static List<object> ToJson()
        {
            var list = new List<object>();
            foreach (var color in Custom)
            {
                var entry = new Dictionary<string, object> { { "hex", color.Hex } };
                if (color.Name.Length > 0) entry["name"] = color.Name;
                list.Add(entry);
            }
            return list;
        }

        public static void FromJson(object node, object dismissed)
        {
            Custom.Clear();
            Dismissed.Clear();
            var list = Json.AsList(node);
            if (list != null)
                foreach (var entry in list)
                {
                    var hex = Normalize(Json.AsString(Json.Field(entry, "hex")));
                    if (hex == null || FindCustom(hex) != null) continue;
                    Custom.Add(new PaletteColor(hex, (Json.AsString(Json.Field(entry, "name")) ?? "").Trim()));
                }
            var gone = Json.AsStringList(dismissed);
            if (gone != null)
                foreach (var hex in gone)
                {
                    var clean = Normalize(hex);
                    if (clean != null) Dismissed.Add(clean);
                }
        }
    }
}

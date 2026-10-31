using System;

namespace Marabook.Model
{
    /// <summary>LES OPTIONS DE L'ENCYCLOPÉDIE (1.1.0, 31/10, Rémi) : l'export des
    /// fiches — une fiche, une catégorie ou toutes — en HTML navigable (et en
    /// PDF, lot suivant). Un réglage de l'œuvre, retenu dans le projet (.plot
    /// v45, clé additive « encyclopedia » : un vieux Marabook l'ignore). Le
    /// titre vide = « Encyclopédie de <projet> », l'auteur vide = celui du
    /// projet, les polices vides = celles du thème HTML (serif / sans).</summary>
    public class EncyclopediaOptions
    {
        public const string FormatHtml = "html", FormatPdf = "pdf";

        public string Format = FormatHtml;
        public string Title = "";
        public string Subtitle = "";
        public string Author = "";
        public string BodyFont = "";       // vide = la pile serif du thème
        public string HeadingFont = "";    // vide = la pile sans-serif du thème
        public double BaseSize = 16;       // px du corps en HTML
        public string Accent = "#5B67D8";  // #RRGGBB
        public bool Dark;
        public string PortraitSide = "right"; // "right" | "left" | "top"
        public bool ShowInfobox = true, ShowRelations = true, ShowPresence = true, ShowRadar = true, ShowTags = true, ShowDates = true;
        public bool IncludeLexicon = true;
        public bool LexiconOnlyDefined = true;  // seuls les mots qui ont une définition
        public bool LexiconAllProjects = true;  // le dictionnaire de tous les projets aussi
        public bool IncludeEmptySheets = true;  // une fiche sans champ ni texte : son titre seul
        public string CoverImageId;             // null = pas de couverture

        public EncyclopediaOptions Clone()
        {
            return (EncyclopediaOptions)MemberwiseClone();
        }

        /// <summary>Une empreinte de tous les réglages : deux options égales
        /// ont la même — la fenêtre ne salit le projet que si elle change.</summary>
        public string Signature()
        {
            return string.Join("|", new[]
            {
                Format, Title, Subtitle, Author, BodyFont, HeadingFont, BaseSize.ToString(System.Globalization.CultureInfo.InvariantCulture), Accent,
                Dark ? "1" : "0", PortraitSide, ShowInfobox ? "1" : "0", ShowRelations ? "1" : "0", ShowPresence ? "1" : "0", ShowRadar ? "1" : "0",
                ShowTags ? "1" : "0", ShowDates ? "1" : "0", IncludeLexicon ? "1" : "0", LexiconOnlyDefined ? "1" : "0", LexiconAllProjects ? "1" : "0",
                IncludeEmptySheets ? "1" : "0", CoverImageId ?? ""
            });
        }

        /// <summary>Le titre effectif : le titre posé, sinon « Encyclopédie de … ».</summary>
        public string TitleFor(Project project)
        {
            if (!string.IsNullOrEmpty((Title ?? "").Trim())) return Title.Trim();
            var name = project == null ? "" : (project.Name ?? "").Trim();
            return name.Length == 0 ? "Encyclopédie" : "Encyclopédie de " + name;
        }

        public string AuthorFor(Project project)
        {
            if (!string.IsNullOrEmpty((Author ?? "").Trim())) return Author.Trim();
            return project == null ? "" : (project.Author ?? "").Trim();
        }

        /// <summary>Une couleur « #RRGGBB » valide, sinon l'accent d'office.</summary>
        public static string SafeColor(string value, string fallback)
        {
            var text = (value ?? "").Trim();
            if (text.Length == 7 && text[0] == '#')
            {
                var ok = true;
                for (var i = 1; i < 7; i++) if (!Uri.IsHexDigit(text[i])) ok = false;
                if (ok) return text.ToUpperInvariant();
            }
            return fallback;
        }
    }
}

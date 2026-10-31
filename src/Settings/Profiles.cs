using System;
using System.Collections.Generic;

namespace Marabook.Settings
{
    /// <summary>LES PROFILS D'UTILISATION (1.1.0, 27/10/2026) : au premier
    /// lancement, Marabook demande « Qui êtes-vous ? » — Écrivain·e,
    /// Éditeur·ice, Cinglé·e ou Moi-même — et le profil choisi CACHE des
    /// fonctionnalités pour rendre l'interface moins intimidante. Caché, jamais
    /// cassé : un projet qui emploie une fonctionnalité masquée la garde telle
    /// quelle dans son fichier, seule l'interface ne la montre plus. Chaque
    /// fonctionnalité masquable a une clé (Feature.Key) ; les vues demandent
    /// Profiles.Enabled(clé). Le profil « Moi-même » laisse cocher chaque
    /// fonctionnalité dans Préférences › Profil (AppSettings.HiddenFeatures).
    /// Sans profil choisi (sondes, captures, anciens réglages), tout est actif.</summary>
    public static class Profiles
    {
        public const string Writer = "ecrivain";
        public const string Publisher = "editeur";
        public const string Everything = "cingle";
        public const string Custom = "perso";

        /// <summary>Une fonctionnalité masquable : sa clé, son libellé et son
        /// détail (la table des cases de « Moi-même »), son groupe.</summary>
        public sealed class Feature
        {
            public string Key;
            public string Group;
            public string Label;
            public string Help;
        }

        public sealed class Profile
        {
            public string Key;
            public string Name;
            public string Help;
            public string Icon;
        }

        public static readonly Profile[] All =
        {
            new Profile { Key = Writer, Name = "Écrivain·e", Icon = "ecrivain", Help = "Pour celleux qui veulent juste écrire et prévoir leurs histoires." },
            new Profile { Key = Publisher, Name = "Éditeur·ice", Icon = "editeur", Help = "Pour celleux qui veulent corriger, mettre en forme et publier des textes : fonctionnalités d'édition avancées, PAO et nombreuses options d'export." },
            new Profile { Key = Everything, Name = "Cinglé·e", Icon = "cingle", Help = "Pour celleux qui veulent tout faire comme des grands : toutes les fonctionnalités activées." },
            new Profile { Key = Custom, Name = "Moi-même", Icon = "personnalise", Help = "Marabook au détail : personnalisez votre expérience." }
        };

        // ---- les clés (les vues les demandent telles quelles)
        public const string BookPublicationDetails = "livre-details-publication";
        public const string BookAudienceDetails = "livre-details-destination";
        public const string BookCommercialDetails = "livre-details-commerciaux";
        public const string BookFormat = "livre-gabarits-format";
        public const string StylePao = "styles-pao";
        public const string BookChecklistEdition = "livre-checklist-edition";
        public const string BookExtraPages = "livre-pages-editeur";
        public const string LooseLines = "lignes-laches"; // « Options d'espacement » : lignes lâches et approche (27/10)
        public const string Widows = "veuves-orphelines";
        public const string Sheets = "fiches";
        public const string Plans = "plans";
        public const string Sprints = "sprints";
        public const string Graph = "graphe"; // le graphe relationnel (28/10) — caché pour l'éditeur·ice

        public static readonly Feature[] Features =
        {
            new Feature { Key = Sheets, Group = "Projet", Label = "Fiches", Help = "La bibliothèque de fiches (personnages, lieux…), leur création, les liens vers les fiches depuis les écrits et la généalogie." },
            new Feature { Key = Plans, Group = "Projet", Label = "Plans", Help = "Les plans en colonnes et briques, et leurs liens avec les livres et les écrits." },
            new Feature { Key = Sprints, Group = "Projet", Label = "Sprints d'écriture", Help = "Le chronomètre de sprint (menu Édition, Journal, Accueil)." },
            new Feature { Key = Graph, Group = "Projet", Label = "Graphe relationnel", Help = "La carte de tout ce qui se relie dans le projet (relations entre fiches, liens, présences dans les écrits, livres, plans), dans la section Encyclopédie de la Pile." },
            new Feature { Key = BookPublicationDetails, Group = "Livres", Label = "Détails de publication", Help = "Collection, ISBN, éditeur, année, série ou cycle dans l'onglet Édition." },
            new Feature { Key = BookAudienceDetails, Group = "Livres", Label = "Détails de destination", Help = "Genre, public cible et thématiques dans l'onglet Édition." },
            new Feature { Key = BookCommercialDetails, Group = "Livres", Label = "Détails commerciaux", Help = "Quatrième de couverture et accroche dans l'onglet Édition." },
            new Feature { Key = BookFormat, Group = "Livres", Label = "Gabarits et format", Help = "L'onglet Gabarits & Format : format d'impression, fond perdu, gabarits de pages, enchaînements de pages. Masqué, un livre neuf reste en A4, marges de 2,5 cm, sans fond perdu ni avertissement d'enchaînement." },
            new Feature { Key = BookChecklistEdition, Group = "Livres", Label = "Check-list d'édition", Help = "Dans Publication : page de titre, table des matières, gabarits de pages, format du livre, enchaînements, métadonnées d'édition, couverture." },
            new Feature { Key = BookExtraPages, Group = "Livres", Label = "Pages éditeur", Help = "Liminaires, pages de fin et annexes d'un livre ; marquer un texte comme page extra." },
            new Feature { Key = StylePao, Group = "Éditeur de texte", Label = "Options PAO des styles", Help = "Césure, justification et enchaînements des styles de paragraphe (ils gardent leurs valeurs par défaut, césure activée)." },
            new Feature { Key = LooseLines, Group = "Éditeur de texte", Label = "Options d'espacement", Help = "Les lignes lâches (ruban Mise en page) et l'approche des caractères (ruban Texte)." },
            new Feature { Key = Widows, Group = "Éditeur de texte", Label = "Détecteur de veuves et orphelines", Help = "Les marqueurs des lignes isolées en haut ou en bas de page, cliquables pour les tolérer." }
        };

        /// <summary>Les fonctionnalités que chaque profil prédéfini cache.</summary>
        public static string[] HiddenBy(string profile)
        {
            switch (profile)
            {
                case Writer:
                    return new[] { BookPublicationDetails, BookAudienceDetails, BookCommercialDetails, BookFormat, StylePao, BookChecklistEdition, BookExtraPages, LooseLines, Widows };
                case Publisher:
                    return new[] { Sheets, Plans, Sprints, Graph };
                default:
                    return new string[0];
            }
        }

        /// <summary>Un profil est choisi (sinon : premier lancement, tout actif).</summary>
        public static bool Chosen { get { return AppSettings.Profile != null; } }

        public static Profile Find(string key)
        {
            foreach (var profile in All) if (profile.Key == key) return profile;
            return null;
        }

        public static Feature FindFeature(string key)
        {
            foreach (var feature in Features) if (feature.Key == key) return feature;
            return null;
        }

        /// <summary>La fonctionnalité se montre-t-elle ? Sans profil, ou pour
        /// « Cinglé·e », toujours ; « Moi-même » suit ses cases ; les autres
        /// profils suivent leur liste.</summary>
        public static bool Enabled(string key)
        {
            var profile = AppSettings.Profile;
            if (profile == null || profile == Everything) return true;
            if (profile == Custom) return !AppSettings.HiddenFeatures.Contains(key);
            return Array.IndexOf(HiddenBy(profile), key) < 0;
        }

        /// <summary>Le profil et ses cases ont changé : les vues se refont.</summary>
        public static event Action Changed;

        public static void Notify()
        {
            var handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>Choisir un profil : « Moi-même » part de tout actif la
        /// première fois (les cases gardent ensuite ce que l'on y décoche).</summary>
        public static void Choose(string key)
        {
            if (Find(key) == null) return;
            AppSettings.Profile = key;
            Notify();
        }

        public static void SetHidden(string featureKey, bool hidden)
        {
            if (hidden) AppSettings.HiddenFeatures.Add(featureKey);
            else AppSettings.HiddenFeatures.Remove(featureKey);
            Notify();
        }

        /// <summary>Les clés en lecture seule du fichier des réglages (inconnues ignorées).</summary>
        public static void LoadHidden(List<string> keys)
        {
            AppSettings.HiddenFeatures.Clear();
            if (keys == null) return;
            foreach (var key in keys) if (FindFeature(key) != null) AppSettings.HiddenFeatures.Add(key);
        }
    }
}

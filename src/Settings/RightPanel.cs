using System;
using Marabook.Model;

namespace Marabook.Settings
{
    /// <summary>Qui occupe la colonne de droite (batch 39). Les panneaux
    /// s'y excluent — Général (l'inspecteur), Correction, Recherche,
    /// Versions, et pour un livre Métadonnées et Publication — et quatre
    /// booléens indépendants encodaient jadis cette seule information,
    /// résolue par une cascade de priorité écrite à la main. Un seul champ
    /// désormais ; « aucun » est la colonne repliée.</summary>
    public enum RightPanel
    {
        None,
        Inspector,
        Correction,
        Search,
        Versions,
        // Métadonnées, Publication et Édition d'un livre ont quitté le rail
        // le 22/09 : ce sont les onglets de la page livre (BookView).
        Pinned,      // l'écrit ou la fiche ÉPINGLÉ SUR LE CÔTÉ, lu en miroir (b47)
        Lexicon,     // la définition d'un mot du dictionnaire personnel (18/09) — épinglable au rail
        StyleReport  // le BILAN DE STYLE de l'écrit ouvert (09/10) : un onglet le temps du bilan, fermé avec le rail ou au changement d'écrit
    }

    /// <summary>Les règles pures autour du panneau de droite — sans WPF,
    /// donc testables en console (C18).</summary>
    public static class RightPanels
    {
        // L'épinglé (b47) est un outil de partout, comme Recherche : en queue.
        private static readonly RightPanel[] ForText =
            { RightPanel.Inspector, RightPanel.Correction, RightPanel.Search, RightPanel.Versions, RightPanel.Pinned };
        private static readonly RightPanel[] ForBook =
            { RightPanel.Inspector, RightPanel.Search, RightPanel.Pinned };
        private static readonly RightPanel[] ForOthers =
            { RightPanel.Inspector, RightPanel.Search, RightPanel.Pinned };
        // Une racine (30/09) : Général OFFERT — l'onglet reste au rail, grisé
        // (Available le refuse) — au lieu de disparaître puis de manquer.
        private static readonly RightPanel[] ForCategory =
            { RightPanel.Inspector, RightPanel.Search, RightPanel.Pinned };
        // Le Lexique (18/09) n'a d'onglet que demandé : épinglé au rail, ou
        // ouvert le temps d'une définition. Mêmes tableaux, un onglet de plus
        // en queue — des instances fixes, le rail compare par référence.
        private static readonly RightPanel[] ForTextLexicon = Append(ForText, RightPanel.Lexicon);
        private static readonly RightPanel[] ForBookLexicon = Append(ForBook, RightPanel.Lexicon);
        private static readonly RightPanel[] ForOthersLexicon = Append(ForOthers, RightPanel.Lexicon);
        private static readonly RightPanel[] ForCategoryLexicon = Append(ForCategory, RightPanel.Lexicon);

        private static RightPanel[] Append(RightPanel[] source, RightPanel extra)
        {
            var result = new RightPanel[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[source.Length] = extra;
            return result;
        }

        // Le Bilan de style (09/10) : un onglet de plus, en queue, le temps du
        // bilan — des instances fixes aussi (le rail compare par référence).
        private static readonly RightPanel[] ForTextReport = Append(ForText, RightPanel.StyleReport);
        private static readonly RightPanel[] ForTextLexiconReport = Append(ForTextLexicon, RightPanel.StyleReport);

        /// <summary>Le nom persisté dans settings.json (« rightPanel »).</summary>
        public static string Name(RightPanel panel)
        {
            switch (panel)
            {
                case RightPanel.Inspector: return "inspector";
                case RightPanel.Correction: return "correction";
                case RightPanel.Search: return "search";
                case RightPanel.Versions: return "versions";
                case RightPanel.Pinned: return "pinned";
                case RightPanel.Lexicon: return "lexicon";
                case RightPanel.StyleReport: return "style-report";
                default: return "none";
            }
        }

        /// <summary>L'inverse de Name ; un nom inconnu vaut l'inspecteur,
        /// comme un settings.json neuf.</summary>
        public static RightPanel Parse(string name)
        {
            switch (name)
            {
                case "none": return RightPanel.None;
                case "correction": return RightPanel.Correction;
                case "search": return RightPanel.Search;
                case "versions": return RightPanel.Versions;
                case "pinned": return RightPanel.Pinned;
                case "lexicon": return RightPanel.Lexicon;
                case "style-report": return RightPanel.StyleReport; // sans bilan au lancement : indisponible, donc grisé
                default: return RightPanel.Inspector;
            }
        }

        /// <summary>Migration des quatre anciens booléens (inspectorVisible,
        /// correctionPanel, searchPanel, versionsPanel) : l'ordre est la
        /// priorité historique de la cascade — Versions, puis Recherche, puis
        /// Correction, puis l'inspecteur — pour qu'un utilisateur retrouve
        /// exactement ce qu'il avait.</summary>
        public static RightPanel Migrate(bool inspector, bool correction, bool search, bool versions)
        {
            if (versions) return RightPanel.Versions;
            if (search) return RightPanel.Search;
            if (correction) return RightPanel.Correction;
            return inspector ? RightPanel.Inspector : RightPanel.None;
        }

        /// <summary>Les onglets du rail selon la nature de l'élément courant
        /// (null = rien de sélectionné), dans l'ordre d'affichage : un écrit
        /// a Correction et Versions ; un livre Édition, Métadonnées et
        /// Publication ; une racine de la Pile n'a que Recherche (batch 43 —
        /// leur Général ne montrait rien), sauf l'Accueil qui garde son
        /// Général (les raccourcis « Commencer ») ; tout le reste (fiches,
        /// plans, dossiers, niveau projet) a Général et Recherche.</summary>
        public static RightPanel[] Offered(ItemKind? kind, bool homeRoot = false, bool lexicon = false, bool report = false)
        {
            if (kind == ItemKind.Text && report) return lexicon ? ForTextLexiconReport : ForTextReport; // le bilan ne vaut que pour un écrit
            if (kind == ItemKind.Text) return lexicon ? ForTextLexicon : ForText;
            if (kind == ItemKind.Book) return lexicon ? ForBookLexicon : ForBook;
            if (kind == ItemKind.Category && !homeRoot) return lexicon ? ForCategoryLexicon : ForCategory;
            return lexicon ? ForOthersLexicon : ForOthers;
        }

        public static bool Offers(ItemKind? kind, RightPanel panel, bool homeRoot = false, bool lexicon = false, bool report = false)
        {
            return Array.IndexOf(Offered(kind, homeRoot, lexicon, report), panel) >= 0;
        }

        /// <summary>Général DÉCRIT l'élément courant ; les autres sont des
        /// outils — le filet du rail les sépare.</summary>
        public static bool DescribesCurrent(RightPanel panel)
        {
            return panel == RightPanel.Inspector;
        }

        /// <summary>Le panneau est-il disponible dans le contexte ? Règles de
        /// domaine, pas d'ordonnancement : il faut un projet ; le mode calme
        /// et le journal masquent toute la colonne ; le panneau doit être
        /// offert pour la nature de l'élément courant ; Recherche seule vit
        /// sans élément courant (« on cherche avant d'avoir cliqué », b37) ;
        /// l'épinglé (b47) ne demande qu'une épingle posée, élément courant
        /// ou non.</summary>
        public static bool Available(RightPanel panel, bool columnHidden, bool hasProject, ItemKind? kind, bool homeRoot = false, bool hasPin = false, bool hasLexicon = false, bool hasReport = false)
        {
            if (columnHidden || !hasProject) return false;
            if (!Offers(kind, panel, homeRoot, panel == RightPanel.Lexicon, panel == RightPanel.StyleReport)) return false;
            if (panel == RightPanel.StyleReport) return hasReport; // le temps d'un bilan (09/10)
            // Le Général d'une racine ne montre rien (b43) : offert pour que
            // l'onglet reste visible, indisponible donc grisé (30/09).
            if (panel == RightPanel.Inspector && kind == ItemKind.Category && !homeRoot) return false;
            if (panel == RightPanel.Pinned) return hasPin;
            // Le Lexique (18/09) : épinglé au rail ou ouvert pour une
            // définition — élément courant ou non, comme l'épinglé.
            if (panel == RightPanel.Lexicon) return hasLexicon;
            return panel == RightPanel.Search || kind != null;
        }

        /// <summary>Un clic sur un onglet du rail : l'onglet actif replie la
        /// colonne, tout autre devient le panneau actif.</summary>
        public static RightPanel Toggle(RightPanel active, RightPanel clicked)
        {
            return active == clicked ? RightPanel.None : clicked;
        }
    }
}

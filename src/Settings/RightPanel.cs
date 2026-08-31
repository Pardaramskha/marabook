using System;
using UniversSale.Model;

namespace UniversSale.Settings
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
        Metadata,    // livre : sous-titre, auteur, éditeur, ISBN… (b32, sorti de l'inspecteur au b39)
        Publication, // livre : gabarit et « Publier… » (idem)
        Edition      // livre : genre, public, thématiques, synopsis, accroche, 4e de couverture (b43)
    }

    /// <summary>Les règles pures autour du panneau de droite — sans WPF,
    /// donc testables en console (C18).</summary>
    public static class RightPanels
    {
        private static readonly RightPanel[] ForText =
            { RightPanel.Inspector, RightPanel.Correction, RightPanel.Search, RightPanel.Versions };
        private static readonly RightPanel[] ForBook =
            { RightPanel.Inspector, RightPanel.Edition, RightPanel.Metadata, RightPanel.Publication, RightPanel.Search };
        private static readonly RightPanel[] ForSheet =
            { RightPanel.Inspector, RightPanel.Search, RightPanel.Versions };
        private static readonly RightPanel[] ForOthers =
            { RightPanel.Inspector, RightPanel.Search };

        /// <summary>Le nom persisté dans settings.json (« rightPanel »).</summary>
        public static string Name(RightPanel panel)
        {
            switch (panel)
            {
                case RightPanel.Inspector: return "inspector";
                case RightPanel.Correction: return "correction";
                case RightPanel.Search: return "search";
                case RightPanel.Versions: return "versions";
                case RightPanel.Metadata: return "metadata";
                case RightPanel.Publication: return "publication";
                case RightPanel.Edition: return "edition";
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
                case "metadata": return RightPanel.Metadata;
                case "publication": return RightPanel.Publication;
                case "edition": return RightPanel.Edition;
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
        /// a Correction et Versions ; un livre Métadonnées et Publication ;
        /// une fiche Versions ; tout le reste (Recherche, Plans, Dictionnaire,
        /// dossiers, niveau projet) n'a que Général et Recherche.</summary>
        public static RightPanel[] Offered(ItemKind? kind)
        {
            if (kind == ItemKind.Text) return ForText;
            if (kind == ItemKind.Book) return ForBook;
            if (kind == ItemKind.Sheet) return ForSheet;
            return ForOthers;
        }

        public static bool Offers(ItemKind? kind, RightPanel panel)
        {
            return Array.IndexOf(Offered(kind), panel) >= 0;
        }

        /// <summary>Général, Métadonnées et Publication DÉCRIVENT l'élément
        /// courant ; les autres sont des outils — le filet du rail les sépare.</summary>
        public static bool DescribesCurrent(RightPanel panel)
        {
            return panel == RightPanel.Inspector || panel == RightPanel.Edition
                || panel == RightPanel.Metadata || panel == RightPanel.Publication;
        }

        /// <summary>Le panneau est-il disponible dans le contexte ? Règles de
        /// domaine, pas d'ordonnancement : il faut un projet ; le mode calme
        /// et le journal masquent toute la colonne ; le panneau doit être
        /// offert pour la nature de l'élément courant ; Recherche seule vit
        /// sans élément courant (« on cherche avant d'avoir cliqué », b37).</summary>
        public static bool Available(RightPanel panel, bool columnHidden, bool hasProject, ItemKind? kind)
        {
            if (columnHidden || !hasProject) return false;
            if (!Offers(kind, panel)) return false;
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

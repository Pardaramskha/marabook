using System;

namespace UniversSale.Settings
{
    /// <summary>Qui occupe la colonne de droite (batch 39). Quatre panneaux
    /// s'y excluent — l'inspecteur, Correction, Recherche, Versions — et
    /// quatre booléens indépendants encodaient jadis cette seule information,
    /// résolue par une cascade de priorité écrite à la main. Un seul champ
    /// désormais ; « aucun » est la colonne repliée.</summary>
    public enum RightPanel
    {
        None,
        Inspector,
        Correction,
        Search,
        Versions
    }

    /// <summary>Les règles pures autour du panneau de droite — sans WPF,
    /// donc testables en console (C18).</summary>
    public static class RightPanels
    {
        /// <summary>Le nom persisté dans settings.json (« rightPanel »).</summary>
        public static string Name(RightPanel panel)
        {
            switch (panel)
            {
                case RightPanel.Inspector: return "inspector";
                case RightPanel.Correction: return "correction";
                case RightPanel.Search: return "search";
                case RightPanel.Versions: return "versions";
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

        /// <summary>Le panneau est-il disponible dans le contexte ? Règles de
        /// domaine, pas d'ordonnancement : l'inspecteur et Correction
        /// décrivent l'élément courant, Recherche et Versions vivent sans
        /// (« on cherche avant d'avoir cliqué », b37) mais demandent un
        /// projet ; le mode calme et le journal masquent toute la colonne.</summary>
        public static bool Available(RightPanel panel, bool columnHidden, bool hasProject, bool hasCurrent)
        {
            if (columnHidden) return false;
            switch (panel)
            {
                case RightPanel.Inspector:
                case RightPanel.Correction:
                    return hasProject && hasCurrent;
                case RightPanel.Search:
                case RightPanel.Versions:
                    return hasProject;
                default:
                    return false;
            }
        }

        /// <summary>Un clic sur un onglet du rail : l'onglet actif replie la
        /// colonne, tout autre devient le panneau actif.</summary>
        public static RightPanel Toggle(RightPanel active, RightPanel clicked)
        {
            return active == clicked ? RightPanel.None : clicked;
        }
    }
}

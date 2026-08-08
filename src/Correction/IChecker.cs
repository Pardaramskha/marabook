using System.Collections.Generic;
using UniversSale.Model;

namespace UniversSale.Correction
{
    /// <summary>Un vérificateur : reçoit le pivot, rend des signalements.
    /// AUCUNE dépendance WPF — c'est la condition pour que toute la chaîne
    /// de correction soit testable en console (suite C5), comme le
    /// compositeur depuis le lot B du batch 24. Le respect de NoProof et
    /// des listes d'ignorés appartient au pilote (CheckerHost), jamais aux
    /// vérificateurs : ils restent bêtes.</summary>
    public interface IChecker
    {
        /// <summary>Identifiant stable (« repetition »…), origine des
        /// signalements produits.</summary>
        string Id { get; }

        /// <summary>Libellé français pour l'interface.</summary>
        string Label { get; }

        FindingCategory Category { get; }

        List<Finding> Check(TextDocument document, StyleSheet styles);
    }
}

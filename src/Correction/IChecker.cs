using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversSale.Model;

namespace UniversSale.Correction
{
    /// <summary>La portée d'un vérificateur — elle décide du cache du pilote
    /// (batch 27, lot B). POUR LE PROCHAIN VÉRIFICATEUR, sans réfléchir :
    /// l'ORTHOGRAPHE et la TYPOGRAPHIE sont locales au paragraphe
    /// (ParagraphLocal — relancées seulement sur les paragraphes dont
    /// l'empreinte a changé) ; les RÉPÉTITIONS et tout ce qui regarde une
    /// fenêtre au-delà du paragraphe sont globales (WholeDocument —
    /// relancées entières à chaque passe).</summary>
    public enum CheckerScope { ParagraphLocal, WholeDocument }

    /// <summary>Un vérificateur : reçoit du pivot, rend des signalements.
    /// AUCUNE dépendance WPF — c'est la condition pour que toute la chaîne
    /// de correction soit testable en console (suites C5/C7/C8). Le respect
    /// de NoProof et des listes d'ignorés appartient au pilote (CheckerHost),
    /// jamais aux vérificateurs : ils restent bêtes.
    ///
    /// Selon Scope, le pilote appelle SOIT Check (WholeDocument, le document
    /// entier), SOIT CheckParagraph (ParagraphLocal, paragraphe par
    /// paragraphe, sous cache d'empreinte — ParagraphIndex des signalements
    /// posé par le pilote). L'autre méthode n'est jamais appelée : un
    /// vérificateur global peut lever NotSupportedException dans
    /// CheckParagraph, et réciproquement.</summary>
    public interface IChecker
    {
        /// <summary>Identifiant stable (« repetition », « spelling »…),
        /// origine des signalements produits et clé du cache.</summary>
        string Id { get; }

        /// <summary>Libellé français pour l'interface.</summary>
        string Label { get; }

        FindingCategory Category { get; }

        CheckerScope Scope { get; }

        /// <summary>Portée WholeDocument : la passe entière.</summary>
        List<Finding> Check(TextDocument document, StyleSheet styles);

        /// <summary>Portée ParagraphLocal : UN paragraphe (les offsets des
        /// signalements sont locaux au paragraphe ; ParagraphIndex est posé
        /// par le pilote au moment du réemploi).</summary>
        List<Finding> CheckParagraph(TextParagraph paragraph, StyleSheet styles);
    }

    /// <summary>Un vérificateur DIFFÉRÉ (batch 29, lot A) : tout ce qui passe
    /// par un processus externe — Grammalecte répond en dizaines ou centaines
    /// de millisecondes, le brancher sur le chemin synchrone gèlerait le fil
    /// UI à chaque cycle. Le pilote ne l'attend JAMAIS : il lance la demande,
    /// rend la passe courante sans elle, et fusionne la réponse au cycle
    /// suivant si le paragraphe n'a pas changé entre-temps (résultat périmé =
    /// jeté). Contraintes : portée ParagraphLocal obligatoire (le cache
    /// d'empreinte est l'arbitre de la péremption) ; CheckParagraph n'est
    /// jamais appelé (NotSupportedException) ; la tâche honore le token —
    /// fermer le projet ou quitter ne doit ni attendre ni planter ; un échec
    /// (processus mort, timeout, réponse illisible) rend une tâche en faute
    /// et le vérificateur SE TAIT — jamais un point de panne.</summary>
    public interface IDeferredChecker : IChecker
    {
        Task<List<Finding>> CheckParagraphAsync(TextParagraph paragraph,
            StyleSheet styles, CancellationToken token);
    }
}

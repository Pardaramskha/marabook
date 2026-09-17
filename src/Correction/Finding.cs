using System.Collections.Generic;

namespace Marabook.Correction
{
    /// <summary>Catégorie d'un signalement — pilote la couleur de l'ondulé et
    /// les filtres du panneau Correction.</summary>
    public enum FindingCategory
    {
        Spelling,    // orthographe (batch 27, Hunspell maison)
        Grammar,     // grammaire (batch 27+, Grammalecte en sous-processus)
        Typography,  // typographie (Typonanny porté)
        Style        // style — répétitions, tics, verbes ternes… (à nous)
    }

    public enum FindingSeverity { Hint, Warning, Error }

    /// <summary>D'où viennent les suggestions d'un signalement (revue du
    /// 13/09) — UN résolveur côté interface au lieu de deux aiguillages par
    /// chaînes : Inline = portées par le signalement (grammaire,
    /// typographie) ; Spelling = le moteur d'orthographe, à la demande ;
    /// Synonyms = le thésaurus, à la demande (répétitions, verbes ternes).</summary>
    public enum SuggestionSource { Inline, Spelling, Synonyms }

    /// <summary>Un signalement de correction : une plage du pivot (offsets
    /// PLATS de PivotEdit — 1 caractère = 1, un élément = 1), une catégorie,
    /// un message, des suggestions. Architecturalement une ANNOTATION du
    /// batch 22 produite par une machine : mêmes unités, même navigation,
    /// même filtrage papier — pas un nouveau sous-système.</summary>
    public class Finding
    {
        public int ParagraphIndex;
        public int Start;   // offset plat dans le paragraphe
        public int Length;
        public FindingCategory Category;
        public FindingSeverity Severity = FindingSeverity.Warning;
        public string Message = "";  // court — panneau, infobulle
        public string Detail;        // long, optionnel — panneau déplié
        public List<string> Suggestions = new List<string>();
        public string RuleId = "";   // stable par règle (« repetition »…)
        public string CheckerId = "";// l'origine : quel vérificateur l'a produit
        public string Word = "";     // le mot signalé TEL QU'AFFICHÉ (casse
                                     // d'origine) — clé des listes d'ignorés
        public SuggestionSource Suggests = SuggestionSource.Inline;

        public int End { get { return Start + Length; } }

        /// <summary>Copie de surface (batch 30) : le cache par CONTENU du
        /// pilote sert la même entrée à deux paragraphes identiques — chaque
        /// consommateur reçoit SA copie, sinon le ParagraphIndex reposé du
        /// second écraserait celui du premier. La liste de suggestions est
        /// partagée (jamais mutée après production).</summary>
        public Finding CloneForParagraph(int paragraphIndex)
        {
            return new Finding
            {
                ParagraphIndex = paragraphIndex,
                Start = Start,
                Length = Length,
                Category = Category,
                Severity = Severity,
                Message = Message,
                Detail = Detail,
                Suggestions = Suggestions,
                RuleId = RuleId,
                CheckerId = CheckerId,
                Word = Word,
                Suggests = Suggests
            };
        }
    }
}

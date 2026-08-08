using System.Collections.Generic;

namespace UniversSale.Correction
{
    /// <summary>Les mots-outils français que le détecteur de répétitions ne
    /// signale jamais : articles, prépositions, conjonctions, pronoms,
    /// auxiliaires et leurs formes conjuguées courantes, adverbes de liaison
    /// omniprésents. Les clés sont NORMALISÉES (minuscules sans accents,
    /// cf. RepetitionChecker.Normalize) — « était » s'y cherche « etait ».</summary>
    public static class FrenchStopWords
    {
        public static bool Contains(string normalizedKey)
        {
            return Words.Contains(normalizedKey);
        }

        private static readonly HashSet<string> Words = new HashSet<string>
        {
            // articles et déterminants
            "le", "la", "les", "un", "une", "des", "du", "de", "au", "aux",
            "ce", "cet", "cette", "ces", "mon", "ma", "mes", "ton", "ta",
            "tes", "son", "sa", "ses", "notre", "nos", "votre", "vos",
            "leur", "leurs", "quel", "quelle", "quels", "quelles", "chaque",
            "tout", "toute", "tous", "toutes", "quelque", "quelques",
            "aucun", "aucune", "nul", "nulle", "certains", "certaines",
            "plusieurs", "meme", "memes", "autre", "autres", "tel", "telle",
            "tels", "telles",
            // pronoms
            "je", "tu", "il", "elle", "on", "nous", "vous", "ils", "elles",
            "me", "te", "se", "moi", "toi", "soi", "lui", "eux", "y", "en",
            "qui", "que", "quoi", "dont", "ou", "lequel", "laquelle",
            "lesquels", "lesquelles", "celui", "celle", "ceux", "celles",
            "ceci", "cela", "ca", "rien", "personne", "chacun", "chacune",
            "quiconque", "autrui",
            // prépositions
            "a", "dans", "par", "pour", "sur", "sous", "vers", "avec",
            "sans", "chez", "entre", "contre", "depuis", "pendant", "avant",
            "apres", "devant", "derriere", "pres", "aupres", "parmi",
            "selon", "malgre", "sauf", "hors", "durant", "envers", "outre",
            "via", "des", "jusque",
            // conjonctions et liaisons
            "et", "mais", "donc", "or", "ni", "car", "si", "comme", "quand",
            "lorsque", "puisque", "quoique", "tandis", "afin", "ainsi",
            "alors", "aussi", "cependant", "pourtant", "neanmoins",
            "toutefois", "ensuite", "puis", "enfin", "encore", "deja",
            "toujours", "jamais", "souvent", "parfois", "peut-etre",
            "bien", "tres", "trop", "peu", "plus", "moins", "assez",
            "beaucoup", "tant", "autant", "plutot", "presque", "environ",
            "seulement", "surtout", "meme", "voici", "voila", "oui", "non",
            "pas", "point", "guere", "ne",
            // être — formes courantes
            "etre", "suis", "es", "est", "sommes", "etes", "sont", "etais",
            "etait", "etions", "etiez", "etaient", "serai", "seras", "sera",
            "serons", "serez", "seront", "serais", "serait", "serions",
            "seriez", "seraient", "sois", "soit", "soyons", "soyez",
            "soient", "fus", "fut", "fumes", "futes", "furent", "ete",
            "etant", "fusse", "fussent",
            // avoir — formes courantes
            "avoir", "ai", "as", "avons", "avez", "ont", "avais", "avait",
            "avions", "aviez", "avaient", "aurai", "auras", "aura",
            "aurons", "aurez", "auront", "aurais", "aurait", "aurions",
            "auriez", "auraient", "aie", "aies", "ait", "ayons", "ayez",
            "aient", "eus", "eut", "eumes", "eutes", "eurent", "eu", "eue",
            "eues", "ayant", "eusse", "eussent",
            // aller / faire / dire — les plus envahissants du récit
            "vais", "vas", "va", "allons", "allez", "vont", "allais",
            "allait", "allaient", "alla", "allerent", "irai", "ira",
            "iront", "irait", "aille", "aillent", "alle", "allee", "alles",
            "allees", "allant",
            "fait", "faits", "faite", "faites", "faisait", "faisaient",
            "fis", "fit", "firent", "fera", "feront", "ferait", "fasse",
            "faisant",
            "dit", "dits", "dite", "dites", "disait", "disaient", "dirent",
            "dira", "dirait", "dise", "disant"
        };
    }
}

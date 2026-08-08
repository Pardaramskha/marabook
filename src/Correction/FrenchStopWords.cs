using System.Collections.Generic;

namespace UniversSale.Correction
{
    /// <summary>Les mots-outils que le détecteur de répétitions ne signale
    /// jamais. PRINCIPE (batch 27, lot 0.2) : SEULS LES MOTS GRAMMATICAUX —
    /// déterminants, pronoms, prépositions, conjonctions, particules de
    /// négation, auxiliaires (être, avoir) et semi-auxiliaires CONJUGUÉS
    /// (aller, faire, pouvoir, vouloir, savoir, devoir, voir, venir,
    /// falloir). Les ADVERBES et les NOMS n'y ont pas leur place : un
    /// adverbe qui revient six fois en une page (« toujours », « déjà »…)
    /// est le signalement le plus utile de l'outil — s'il est trop bruyant,
    /// c'est un réglage de sévérité, pas une liste d'exclusion.
    ///
    /// EXCEPTION DOCUMENTÉE (décision utilisateur, batch 27) : les formes
    /// conjuguées de DIRE restent dans la liste — l'incise « dit-il » est
    /// une convention du français, pas un tic (le futur vérificateur de
    /// verbes de dialogue fera le travail fin) ; « répondit-elle » répété,
    /// lui, est signalé.
    ///
    /// AMBIGUÏTÉS TRANCHÉES : les formes qui sont AUSSI des noms pleins
    /// fréquents sortent de la liste, même quand la grammaire les
    /// réclamerait (« personne », « point », « or », « car », « envers »,
    /// « fait/faits », « vue/vues », « été ») — leur usage nominal
    /// redeviendrait invisible. « pas » reste (la négation écrase le nom au
    /// centuple). Les clés sont NORMALISÉES (minuscules sans accents, cf.
    /// FrenchTokenizer.Fold) — « était » s'y cherche « etait ».</summary>
    public static class FrenchStopWords
    {
        public static bool Contains(string foldedKey)
        {
            return Words.Contains(foldedKey);
        }

        private static readonly HashSet<string> Words = new HashSet<string>
        {
            // --- déterminants et articles
            "le", "la", "les", "un", "une", "des", "du", "de", "au", "aux",
            "ce", "cet", "cette", "ces", "mon", "ma", "mes", "ton", "ta",
            "tes", "son", "sa", "ses", "notre", "nos", "votre", "vos",
            "leur", "leurs", "quel", "quelle", "quels", "quelles", "chaque",
            "quelque", "quelques", "aucun", "aucune", "nul", "nulle",
            "certains", "certaines", "plusieurs", "tel", "telle", "tels",
            "telles",
            // --- pronoms
            "je", "tu", "il", "elle", "on", "nous", "vous", "ils", "elles",
            "me", "te", "se", "moi", "toi", "soi", "lui", "eux", "y", "en",
            "qui", "que", "quoi", "dont", "ou", "lequel", "laquelle",
            "lesquels", "lesquelles", "auquel", "auxquels", "auxquelles",
            "duquel", "desquels", "desquelles", "celui", "celle", "ceux",
            "celles", "ceci", "cela", "ca", "rien", "chacun", "chacune",
            "quiconque", "autrui",
            // --- prépositions
            "a", "dans", "par", "pour", "sur", "sous", "vers", "avec",
            "sans", "chez", "entre", "contre", "depuis", "pendant", "avant",
            "apres", "devant", "derriere", "pres", "aupres", "parmi",
            "selon", "malgre", "sauf", "hors", "durant", "outre", "via",
            "jusque",
            // --- conjonctions (« or », « car » : aussi des noms — exclus)
            "et", "mais", "donc", "ni", "si", "comme", "quand", "lorsque",
            "puisque", "quoique", "tandis", "afin", "parce",
            // --- négation (« pas » assumé : la particule écrase le nom)
            "ne", "pas", "non",
            // --- être
            "etre", "suis", "es", "est", "sommes", "etes", "sont", "etais",
            "etait", "etions", "etiez", "etaient", "serai", "seras", "sera",
            "serons", "serez", "seront", "serais", "serait", "serions",
            "seriez", "seraient", "sois", "soit", "soyons", "soyez",
            "soient", "fus", "fut", "fumes", "futes", "furent", "fusse",
            "fussent", "etant",
            // --- avoir
            "avoir", "ai", "as", "avons", "avez", "ont", "avais", "avait",
            "avions", "aviez", "avaient", "aurai", "auras", "aura",
            "aurons", "aurez", "auront", "aurais", "aurait", "aurions",
            "auriez", "auraient", "aie", "aies", "ait", "ayons", "ayez",
            "aient", "eus", "eut", "eumes", "eutes", "eurent", "eu", "eue",
            "eues", "eusse", "eussent", "ayant",
            // --- aller (semi-auxiliaire)
            "aller", "vais", "vas", "va", "allons", "allez", "vont",
            "allais", "allait", "allions", "alliez", "allaient", "alla",
            "allerent", "irai", "iras", "ira", "irons", "irez", "iront",
            "irais", "irait", "iraient", "aille", "ailles", "aillent",
            "alle", "allee", "alles", "allees", "allant",
            // --- faire (semi-aux ; « fait/faits » exclus : noms pleins)
            "faire", "fais", "faisons", "faites", "font", "faisais",
            "faisait", "faisions", "faisiez", "faisaient", "fis", "fit",
            "fimes", "firent", "ferai", "feras", "fera", "ferons", "ferez",
            "feront", "ferais", "ferait", "feraient", "fasse", "fasses",
            "fassent", "faisant", "faite", "faites",
            // --- pouvoir
            "pouvoir", "peux", "peut", "pouvons", "pouvez", "peuvent",
            "pouvais", "pouvait", "pouvions", "pouviez", "pouvaient",
            "pus", "put", "purent", "pourrai", "pourras", "pourra",
            "pourrons", "pourrez", "pourront", "pourrais", "pourrait",
            "pourraient", "puisse", "puisses", "puissent", "pouvant", "pu",
            // --- vouloir
            "vouloir", "veux", "veut", "voulons", "voulez", "veulent",
            "voulais", "voulait", "voulions", "vouliez", "voulaient",
            "voulus", "voulut", "voulurent", "voudrai", "voudras",
            "voudra", "voudrons", "voudrez", "voudront", "voudrais",
            "voudrait", "voudraient", "veuille", "veuillent", "voulant",
            "voulu", "voulue", "voulus", "voulues",
            // --- savoir
            "savoir", "sais", "sait", "savons", "savez", "savent",
            "savais", "savait", "savions", "saviez", "savaient", "sus",
            "sut", "surent", "saurai", "sauras", "saura", "saurons",
            "saurez", "sauront", "saurais", "saurait", "sauraient",
            "sache", "sachent", "sachant", "su", "sue", "sus", "sues",
            // --- devoir (« dû » plié « du », déjà article)
            "devoir", "dois", "doit", "devons", "devez", "doivent",
            "devais", "devait", "devions", "deviez", "devaient", "dus",
            "dut", "durent", "devrai", "devras", "devra", "devrons",
            "devrez", "devront", "devrais", "devrait", "devraient",
            "doive", "doivent", "devant", "due", "dues",
            // --- voir (« vue/vues » exclues : noms ; « vit » assumé, il
            //     couvre aussi vivre)
            "voir", "vois", "voit", "voyons", "voyez", "voient", "voyais",
            "voyait", "voyions", "voyiez", "voyaient", "vis", "vit",
            "virent", "verrai", "verras", "verra", "verrons", "verrez",
            "verront", "verrais", "verrait", "verraient", "voie", "voies",
            "voyant", "vu", "vus",
            // --- venir (semi-auxiliaire : venir de)
            "venir", "viens", "vient", "venons", "venez", "viennent",
            "venais", "venait", "venions", "veniez", "venaient", "vins",
            "vint", "vinrent", "viendrai", "viendras", "viendra",
            "viendrons", "viendrez", "viendront", "viendrais", "viendrait",
            "viendraient", "vienne", "viennent", "venant", "venu", "venue",
            "venus", "venues",
            // --- falloir
            "falloir", "faut", "fallait", "fallut", "faudra", "faudrait",
            "faille", "fallu",
            // --- dire — L'EXCEPTION DOCUMENTÉE (voir l'en-tête)
            "dire", "dis", "dit", "disons", "dites", "disent", "disais",
            "disait", "disions", "disiez", "disaient", "dirent", "dirai",
            "diras", "dira", "dirons", "direz", "diront", "dirais",
            "dirait", "diraient", "dise", "dises", "disent", "disant",
            "dite", "dits", "dites"
        };
    }
}

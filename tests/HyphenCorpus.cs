namespace UniversSale.Tests
{
    /// <summary>Corpus de césure française : chaque entrée porte ses coupures
    /// ATTENDUES (tirets), établies à la main selon l'usage typographique
    /// français avec les minima du projet (2 lettres avant la coupure,
    /// 3 après, mots de 5 lettres et plus). Un mot sans tiret ne doit PAS
    /// être coupé (monosyllabes, apostrophes, sigles, x/y intervocaliques).
    /// Le test C2 en tire un SCORE, pas un pass/fail : la césure actuelle est
    /// heuristique, l'objectif est de mesurer les progrès — le score courant
    /// est un plancher, toute régression échoue.</summary>
    public static class HyphenCorpus
    {
        public static readonly string[] Words =
        {
            // — V-CV simples
            "mai-son", "pa-role", "sa-lade", "ma-ti-née", "do-maine",
            "ca-bane", "fa-rine", "me-sure", "mi-nute", "na-ture",
            "lec-ture", "pein-ture", "voi-ture", "mé-de-cin", "his-toire",
            "mo-nu-ment", "do-cu-ment", "nu-mé-rique", "po-li-tique", "mu-sique",
            "pra-tique", "cri-tique", "fa-tigue", "col-lègue", "de-mande",
            "re-gar-der", "écou-ter", "par-ler", "tra-vail-ler", "man-ger",
            "chan-ter", "dan-ser", "pen-ser", "tom-ber", "por-ter",
            "fer-mer", "ou-vrir", "dor-mir", "par-tir", "sor-tir",
            "fi-nir", "choi-sir", "gran-dir", "rou-gir", "cou-leur",
            "dou-leur", "bon-heur", "cha-leur", "va-leur", "pro-fon-deur",
            // — attaques insécables (bl, br, ch, cl, cr, dr, fl, fr, gl, gn,
            //   gr, ph, pl, pr, th, tr, vr)
            "ta-bleau", "pro-blème", "pos-sible", "ter-rible", "en-semble",
            "sim-ple", "peu-ple", "souf-fle", "siè-cle", "mus-cle",
            "spec-ta-cle", "obs-ta-cle", "mi-ra-cle", "ar-ti-cle", "cer-cle",
            "jun-gle", "au-tre", "fe-nê-tre", "let-tre", "met-tre",
            "pren-dre", "com-pren-dre", "ap-pren-dre", "at-ten-dre", "des-cen-dre",
            "ré-pon-dre", "ven-dredi", "no-vem-bre", "dé-cem-bre", "cham-bre",
            "nom-bre", "som-bre", "tim-bre", "ar-bre", "mar-bre",
            "li-vre", "vi-vre", "sui-vre", "pau-vre", "ou-vrage",
            "na-vrant", "ma-chine", "re-cher-che", "mar-ché", "ar-chi-tecte",
            "pho-to-graphe", "té-lé-phone", "phi-lo-so-phie", "théâ-tre", "ma-thé-ma-tique",
            "sym-pa-thie", "mon-ta-gne", "cam-pa-gne", "es-pa-gnol", "si-gnal",
            "ma-gni-fique", "di-gnité",
            // — consonnes doubles
            "ap-pe-ler", "com-men-cer", "ap-por-ter", "ar-ri-ver", "at-tra-per",
            "oc-ca-sion", "ac-cep-ter", "ad-di-tion", "af-faire", "al-lu-mer",
            "an-née", "ap-pa-rence", "at-ten-tion", "bril-ler", "col-lec-tion",
            "dif-fi-cile", "ef-fort", "er-reur", "im-mense", "in-tel-li-gent",
            "nour-rir", "oc-cu-per", "pas-sion", "pos-ses-sion", "pro-fes-seur",
            "som-met", "ter-rasse", "vil-lage", "en-nemi", "cous-sin",
            // — groupes de trois consonnes (cause structurelle n° 1 : le
            //   compositeur les abandonne aujourd'hui, FrenchHyphenator.cs)
            "ins-truc-tion", "cons-truc-tion", "abs-trait", "obs-cur", "cir-cons-tance",
            "trans-crip-tion", "trans-port", "trans-for-mer", "sculp-ture", "ab-sor-ber",
            "ob-ser-ver", "ex-pli-quer", "ex-pri-mer", "ex-trême", "ex-terne",
            "fonc-tion", "fonc-tion-ner", "ponc-tua-tion", "ins-tant", "dis-tinct",
            "obs-ti-ner", "ad-met-tre", "tech-nique", "arith-mé-tique", "comp-ter",
            "domp-ter", "sculp-teur", "long-temps", "prin-temps", "mys-tère",
            "sus-pense", "his-to-rique",
            // — x et y intervocaliques (cause structurelle n° 3 : aucune règle
            //   aujourd'hui — on ne coupe ni avant ni après)
            "maxi-mum", "exa-men", "exer-cice", "exis-tence", "exis-tant",
            "taxer", "voyage", "moyen", "crayon", "royaume",
            "es-sayer", "pay-sage", "voya-geur", "ap-puyer", "noyau",
            "joyeux", "ga-laxie",
            // — incoupables : apostrophes, sigles, monosyllabes
            "aujourd'hui", "presqu'île", "quelqu'un", "UNESCO", "vingt",
            "douze", "poids", "temps", "corps", "océan",
            "étoile", "oreille", "abeille", "merci",
            // — mots longs
            "an-ti-cons-ti-tu-tion-nel-le-ment", "dé-ve-lop-pe-ment", "gou-ver-ne-ment",
            "in-ter-na-tio-nal", "res-pon-sa-bi-lité", "par-ti-cu-liè-re-ment",
            "mal-heu-reu-se-ment", "ad-mi-nis-tra-tion", "bi-blio-thèque",
            "uni-ver-sité", "lit-té-ra-ture", "tem-pé-ra-ture", "ex-traor-di-naire",
            "ap-par-te-ment", "en-vi-ron-ne-ment", "in-for-ma-tique", "or-di-na-teur",
            "im-pri-mante", "com-po-si-teur", "ty-po-gra-phie", "jus-ti-fi-ca-tion",
            "pa-ra-graphe", "ma-nus-crit", "cha-pi-tre", "brouil-lon", "écri-ture",
            "per-son-nage", "nar-ra-teur", "dia-lo-gue", "in-tri-gue", "aven-ture",
            // — vie quotidienne
            "ho-ri-zon", "lu-mière", "si-lence", "mur-mure", "tem-pête",
            "froi-dure", "cha-peau", "au-tomne", "hi-ver", "sai-son",
            "jour-née", "soi-rée", "ma-tin", "om-bre", "em-blème",
            "am-pleur", "im-plo-rer", "em-por-ter", "em-bras-ser", "en-trer",
            "en-tre-prise", "en-tre-tien", "in-ter-dire", "in-ter-ro-ger", "in-ter-valle",
            "mer-veille", "mer-veil-leux", "tra-vail", "so-leil", "som-meil",
            "ap-pa-reil", "con-seil", "con-seil-ler", "bou-teille", "cor-beille",
            "mer-credi", "bu-reau", "ca-deau", "cou-teau", "man-teau",
            "châ-teau", "ber-ceau", "cer-veau", "ni-veau", "tra-vaux",
            "jour-naux", "ani-mal", "ani-maux", "rai-son", "poi-gnée",
            "for-mule", "cap-sule", "pé-nin-sule", "ab-sence", "ab-so-lu-ment",
            "ac-ci-dent", "acro-bate", "ado-ra-ble", "af-fiche", "ai-guille",
            "bou-lan-ger", "bou-lan-ge-rie", "pâ-tis-sier", "cui-sine", "cui-si-nier",
            "as-siette", "four-chette", "ser-viette", "ar-moire", "éta-gère",
            "fau-teuil", "ri-deau", "pla-fond", "plan-cher", "es-ca-lier",
            "cou-loir", "gre-nier", "jar-din", "po-ta-ger", "lé-gume",
            "to-mate", "ca-rotte", "poi-reau", "épi-nard", "cour-gette",
            "au-ber-gine", "pom-mier", "ce-ri-sier", "aman-dier", "oli-vier",
            "fo-rêt", "ri-vière", "col-line", "val-lée", "pla-teau",
            "dé-sert", "pla-nète", "co-mète", "sa-tel-lite", "fu-sée",
            "na-vette", "as-tro-naute"
        };
    }
}

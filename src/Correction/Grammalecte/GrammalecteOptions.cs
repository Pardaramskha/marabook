using System.Collections.Generic;

namespace UniversSale.Correction.Grammalecte
{
    /// <summary>Une option de Grammalecte telle que les Préférences la
    /// montrent : le NOM DE GRAMMALECTE (jamais une nomenclature à nous —
    /// exigence du lot C), son libellé français officiel, son groupe.</summary>
    public sealed class GrammalecteOption
    {
        public string Name = "";
        public string Label = "";
        public string Group = "";
        public bool MarabookDefault;
        /// <summary>Vrai si Marabook force ce défaut À REBOURS du défaut de
        /// Grammalecte — la politique de recouvrement du lot C, affichée
        /// honnêtement dans les Préférences.</summary>
        public bool OverriddenByPolicy;
    }

    /// <summary>LA POLITIQUE DE RECOUVREMENT (batch 29, lot C) — qui possède
    /// quoi, écrite en code et dans PLAN.md :
    ///
    /// - RÉPÉTITIONS : les nôtres (RepetitionChecker, réglé pour le roman —
    ///   rayon en mots, mots-outils, casse/accents pliés). redon1/redon2
    ///   restent éteints (ils le sont déjà par défaut chez Grammalecte).
    /// - TYPOGRAPHIE : la nôtre — le port de Typonanny reste en phase 5, il
    ///   est DIFFÉRÉ, pas supprimé. L'argument qui tranche : la typographie
    ///   doit être SYNCHRONE (c'est de la frappe, pas de l'analyse) et elle
    ///   est couplée au compositeur (cadratins de dialogue, insécables que
    ///   la composition pose elle-même — Grammalecte signalerait ce que le
    ///   moteur fait déjà). Donc typo, apos, esp, nbsp, unit, num, nf, chim
    ///   éteints par défaut — VISIBLES et rallumables un à un dans les
    ///   Préférences, en connaissance du recouvrement.
    /// - MOTS COMPOSÉS (mc) : éteint — l'orthographe maison les vérifie
    ///   déjà segment par segment (batch 27).
    /// - GRAMMAIRE ET ACCORDS : Grammalecte, sans discussion (gn, conf,
    ///   loc, conj, ppas, infi, imp, inte, vmode, maj, tu…).
    /// - VIRGULES (virg) : ACTIF — règles grammaticales (virgule manquante
    ///   avant « mais », « car », « etc. »), aucun recouvrement Typonanny
    ///   (amendement A5, confirmé).
    /// - PLÉONASMES, CONFUSIONS, ÉCRITURE ÉPICÈNE : Grammalecte (pleo,
    ///   conf, eepi, bs, eleu).
    ///
    /// Les libellés reprennent _dOptLabel['fr'] de gc_options.py (2.3.0),
    /// les groupes suivent lStructOpt. Les options de contexte html/latex/md
    /// sont forcées à faux (on envoie de la prose, jamais du balisage) et
    /// idrule n'est pas exposée.</summary>
    public static class GrammalecteOptions
    {
        public static readonly List<GrammalecteOption> Catalog = BuildCatalog();

        private static List<GrammalecteOption> BuildCatalog()
        {
            var list = new List<GrammalecteOption>();
            // groupe « Typographie » (basic) — le territoire de Typonanny :
            // tout ce qui recouvre est éteint par politique.
            Add(list, "typo", "Signes typographiques", "Typographie", false, true);
            Add(list, "apos", "Apostrophe typographique", "Typographie", false, true);
            Add(list, "eepi", "Écriture épicène", "Typographie", true, false);
            Add(list, "esp", "Espaces surnuméraires", "Typographie", false, true);
            Add(list, "tab", "Tabulations surnuméraires", "Typographie", false, false);
            Add(list, "nbsp", "Espaces insécables", "Typographie", false, true);
            Add(list, "unit", "Espaces insécables avant unités de mesure", "Typographie", false, true);
            Add(list, "tu", "Traits d'union et soudures", "Typographie", true, false);
            Add(list, "maj", "Majuscules", "Typographie", true, false);
            Add(list, "minis", "Majuscules pour ministères", "Typographie", true, false);
            Add(list, "num", "Nombres", "Typographie", false, true);
            Add(list, "nf", "Normes françaises", "Typographie", false, true);
            Add(list, "virg", "Virgules", "Typographie", true, false);
            Add(list, "poncfin", "Ponctuation finale", "Typographie", false, false);
            Add(list, "ocr", "Erreurs de numérisation (OCR)", "Typographie", false, false);
            Add(list, "chim", "Chimie", "Typographie", false, true);
            Add(list, "liga", "Signaler ligatures typographiques", "Typographie", false, false);
            Add(list, "mapos", "Apostrophe manquante après lettres isolées", "Typographie", false, false);
            // groupe « Noms et adjectifs » (gramm)
            Add(list, "conf", "Confusions et faux-amis", "Noms et adjectifs", true, false);
            Add(list, "loc", "Locutions", "Noms et adjectifs", true, false);
            Add(list, "gn", "Accords (genre et nombre)", "Noms et adjectifs", true, false);
            // groupe « Verbes »
            Add(list, "infi", "Infinitif", "Verbes", true, false);
            Add(list, "conj", "Conjugaisons", "Verbes", true, false);
            Add(list, "ppas", "Participes passés, adjectifs", "Verbes", true, false);
            Add(list, "imp", "Impératif", "Verbes", true, false);
            Add(list, "inte", "Interrogatif", "Verbes", true, false);
            Add(list, "vmode", "Modes verbaux", "Verbes", true, false);
            // groupe « Style »
            Add(list, "bs", "Populaire", "Style", true, false);
            Add(list, "pleo", "Pléonasmes", "Style", true, false);
            Add(list, "eleu", "Élisions et euphonies", "Style", true, false);
            Add(list, "neg", "Adverbe de négation", "Style", false, false);
            Add(list, "redon1", "Répétitions dans le paragraphe", "Style", false, true);
            Add(list, "redon2", "Répétitions dans la phrase", "Style", false, true);
            // groupe « Divers »
            Add(list, "date", "Validité des dates", "Divers", true, false);
            Add(list, "mc", "Mots composés", "Divers", false, true);
            return list;
        }

        private static void Add(List<GrammalecteOption> list, string name,
            string label, string group, bool marabookDefault, bool overridden)
        {
            list.Add(new GrammalecteOption
            {
                Name = name,
                Label = label,
                Group = group,
                MarabookDefault = marabookDefault,
                OverriddenByPolicy = overridden
            });
        }

        /// <summary>Le jeu d'options effectif : les défauts Marabook, puis
        /// les choix de l'utilisateur (réglages) par-dessus. Les options de
        /// contexte sont forcées : on envoie de la prose.</summary>
        public static Dictionary<string, object> Effective(
            Dictionary<string, bool> userChoices)
        {
            var options = new Dictionary<string, object>();
            foreach (var option in Catalog)
            {
                var value = option.MarabookDefault;
                bool chosen;
                if (userChoices != null
                    && userChoices.TryGetValue(option.Name, out chosen))
                    value = chosen;
                options[option.Name] = value;
            }
            options["html"] = false;
            options["latex"] = false;
            options["md"] = false;
            options["idrule"] = false;
            return options;
        }
    }
}

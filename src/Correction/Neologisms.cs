using System;

namespace Marabook.Correction
{
    /// <summary>LES NÉOLOGISMES (1.0.5, Rémi) : un mot que le dictionnaire
    /// ignore mais dont la CONSTRUCTION est régulière — relevé dans sa
    /// catégorie, en indice, avec la raison, jamais en faute. Les règles,
    /// toutes fondées sur un mot CONNU (moteur ou appris) :
    /// — un féminin dérivé d'un masculin connu : gouverneuse (gouverneur),
    ///   -trice, -ière, -ienne, -onne, -ette, -elle, -ive, -euse ;
    /// — un adjectif en -able / -ible formé sur un verbe connu : contenable
    ///   (contenir), apprenable (apprendre), vérifiable… ;
    /// — un mot en -mancie / -mancien / -mancienne / -mantique (les magies
    ///   et divinations : métallomancie, aquamancien) ;
    /// — un -isme / -iste bâti sur un mot connu : emblémisme (emblème) ;
    /// — un adverbe en -ment bâti sur un adjectif connu ;
    /// — un nom en -age / -erie bâti sur un verbe connu ;
    /// — un -issime bâti sur un adjectif connu ;
    /// — un préfixe productif collé à un mot connu : auto-, anti-, re-, dé-,
    ///   pré-, post-, sur-, sous-, hyper-, super-, ultra-, méga-, inter-,
    ///   trans-, contre-, non-, co-, archi-, semi-, pseudo-, néo-, ex- ;
    /// — et le pluriel de tout cela.</summary>
    public static class Neologisms
    {
        public const string Rule = "neologism";

        private static readonly string[] Prefixes =
        {
            "auto", "anti", "ré", "re", "dés", "dé", "pré", "post", "sur", "sous", "hyper", "super", "ultra",
            "méga", "inter", "trans", "contre", "non", "co", "archi", "semi", "pseudo", "néo", "ex", "mi"
        };

        private static readonly string[][] Feminines =
        {
            new[] { "euse", "eur" }, new[] { "trice", "teur" }, new[] { "ière", "ier" }, new[] { "ienne", "ien" },
            new[] { "onne", "on" }, new[] { "ette", "et" }, new[] { "elle", "el" }, new[] { "ive", "if" },
            new[] { "euse", "eux" }, new[] { "esse", "e" }
        };

        /// <summary>La raison (en français) qui fait de ce mot un néologisme
        /// bien formé, ou null s'il n'en est pas un. known : le mot est-il
        /// connu (moteur ou appris) ? Jamais appelé sur un mot connu.</summary>
        public static string Explain(string word, Func<string, bool> known)
        {
            if (string.IsNullOrEmpty(word) || known == null) return null;
            var w = word.ToLowerInvariant();
            if (w.Length < 5) return null;
            var reason = ExplainSingular(w, known);
            if (reason != null) return reason;
            // Le pluriel d'un néologisme (-s, -x) en est un aussi.
            if ((w.EndsWith("s") || w.EndsWith("x")) && w.Length > 5)
            {
                reason = ExplainSingular(w.Substring(0, w.Length - 1), known);
                if (reason != null) return reason + " (au pluriel)";
            }
            return null;
        }

        private static string ExplainSingular(string w, Func<string, bool> known)
        {
            // — féminins
            foreach (var pair in Feminines)
            {
                var suffix = pair[0];
                var masculine = pair[1];
                if (!w.EndsWith(suffix) || w.Length - suffix.Length < 2) continue;
                var stem = w.Substring(0, w.Length - suffix.Length);
                var candidate = stem + masculine;
                if (known(candidate)) return "féminin formé sur « " + candidate + " »";
            }
            // — -able / -ible sur un verbe
            foreach (var suffix in new[] { "able", "ible" })
            {
                if (!w.EndsWith(suffix) || w.Length - suffix.Length < 3) continue;
                var stem = w.Substring(0, w.Length - suffix.Length);
                foreach (var ending in new[] { "er", "ir", "re", "dre", "tre", "oir", "e" })
                {
                    var verb = stem + ending;
                    if (ending != "e" ? known(verb) : known(verb) && IsVerbLike(verb)) return "adjectif en -" + suffix + " formé sur « " + verb + " »";
                }
                // -eable / -çable : mangeable, remplaçable
                if (stem.EndsWith("ge") && known(stem.Substring(0, stem.Length - 1) + "er")) return "adjectif en -" + suffix + " formé sur « " + stem.Substring(0, stem.Length - 1) + "er »";
                if (stem.EndsWith("ç") && known(stem.Substring(0, stem.Length - 1) + "cer")) return "adjectif en -" + suffix + " formé sur « " + stem.Substring(0, stem.Length - 1) + "cer »";
            }
            // — les magies : -mancie, -mancien, -mancienne, -mantique
            foreach (var suffix in new[] { "mancie", "mancien", "mancienne", "mantique" })
                if (w.EndsWith(suffix) && w.Length - suffix.Length >= 3) return "mot en -" + suffix + " (divination, magie)";
            // — -isme / -iste sur un mot connu
            foreach (var suffix in new[] { "isme", "iste" })
            {
                if (!w.EndsWith(suffix) || w.Length - suffix.Length < 3) continue;
                var stem = w.Substring(0, w.Length - suffix.Length);
                // emblém-isme → emblème : l'accent aigu du radical redevient grave devant le e muet.
                var grave = stem.EndsWith("é") && stem.Length > 2 ? stem.Substring(0, stem.Length - 1) + "èe" : null;
                var consonantGrave = stem.Length > 3 && stem[stem.Length - 2] == 'é' ? stem.Substring(0, stem.Length - 2) + "è" + stem[stem.Length - 1] + "e" : null;
                foreach (var baseWord in new[] { stem, stem + "e", stem + "é", stem + "a", stem + "o", grave, consonantGrave })
                    if (baseWord != null && known(baseWord)) return "mot en -" + suffix + " formé sur « " + baseWord + " »";
            }
            // — adverbe en -ment sur un adjectif (féminin) connu
            if (w.EndsWith("ment") && w.Length > 7)
            {
                var stem = w.Substring(0, w.Length - 4);
                foreach (var adjective in new[] { stem, stem + "e" })
                    if (!adjective.EndsWith("ment") && known(adjective)) return "adverbe en -ment formé sur « " + adjective + " »";
                if (w.EndsWith("emment") && known(w.Substring(0, w.Length - 6) + "ent")) return "adverbe en -ment formé sur « " + w.Substring(0, w.Length - 6) + "ent »";
                if (w.EndsWith("amment") && known(w.Substring(0, w.Length - 6) + "ant")) return "adverbe en -ment formé sur « " + w.Substring(0, w.Length - 6) + "ant »";
            }
            // — -age / -erie sur un verbe
            foreach (var suffix in new[] { "age", "erie" })
            {
                if (!w.EndsWith(suffix) || w.Length - suffix.Length < 3) continue;
                var stem = w.Substring(0, w.Length - suffix.Length);
                if (known(stem + "er")) return "nom en -" + suffix + " formé sur « " + stem + "er »";
            }
            // — -issime
            if (w.EndsWith("issime") && w.Length > 8)
            {
                var stem = w.Substring(0, w.Length - 6);
                foreach (var adjective in new[] { stem, stem + "e", stem + "é" })
                    if (known(adjective)) return "superlatif en -issime formé sur « " + adjective + " »";
            }
            // — préfixes productifs
            foreach (var prefix in Prefixes)
            {
                if (!w.StartsWith(prefix) || w.Length - prefix.Length < 4) continue;
                var baseWord = w.Substring(prefix.Length);
                if (known(baseWord)) return "« " + prefix + "- » + « " + baseWord + " »";
            }
            return null;
        }

        private static bool IsVerbLike(string word)
        {
            return word.EndsWith("re") || word.EndsWith("ire");
        }
    }
}

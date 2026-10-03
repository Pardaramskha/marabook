using System;
using System.Collections.Generic;
using System.Text;
using Marabook.Model;

namespace Marabook.Correction.Grammalecte
{
    /// <summary>Le dictionnaire personnel tel que Grammalecte le comprend
    /// (1.0.3) : chaque entrée du dictionnaire de Marabook — projet et
    /// global — devient des triplets [forme, lemme, étiquette] que le pont
    /// charge comme « dictionnaire personnel » du vérificateur. C'est ce qui
    /// permet à Grammalecte d'ACCORDER autour des mots inventés : un prénom
    /// genré (« Shallan est parti » → partie), un nom (« le shardique est
    /// contente »), un adjectif. Les étiquettes sont celles de Grammalecte :
    /// :M1 prénom, :M2 patronyme, :MP autre nom propre, :N nom, :A adjectif,
    /// :W adverbe ; :m/:f/:e genre (e = épicène, jamais de faute d'accord),
    /// :s/:p/:i nombre (i = invariable). Les verbes ne sont pas transmis
    /// (leur conjugaison n'a pas d'étiquette simple) ; le mot reste accepté
    /// par l'orthographe de Marabook comme avant.</summary>
    public static class PersonalLexicon
    {
        /// <summary>Les triplets des deux dictionnaires, sans doublon.</summary>
        public static List<string[]> Build(IEnumerable<LexiconEntry> project, IEnumerable<LexiconEntry> global)
        {
            var result = new List<string[]>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Append(result, seen, project);
            Append(result, seen, global);
            return result;
        }

        /// <summary>Une empreinte stable des triplets : le pont ne renvoie le
        /// dictionnaire que lorsqu'elle change.</summary>
        public static string Hash(List<string[]> entries)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (var entry in entries)
                    foreach (var part in entry)
                    {
                        foreach (var c in part) { hash ^= c; hash *= 1099511628211UL; }
                        hash ^= 0x1F; hash *= 1099511628211UL;
                    }
                return entries.Count + "-" + hash.ToString("x16");
            }
        }

        private static void Append(List<string[]> result, HashSet<string> seen, IEnumerable<LexiconEntry> entries)
        {
            if (entries == null) return;
            foreach (var entry in entries)
                foreach (var triple in Triples(entry))
                    if (seen.Add(triple[0] + "\u0001" + triple[2])) result.Add(triple);
        }

        /// <summary>Les triplets d'UNE entrée (vide quand Grammalecte n'a
        /// rien à en apprendre).</summary>
        public static List<string[]> Triples(LexiconEntry entry)
        {
            var result = new List<string[]>();
            if (entry == null) return result;
            var word = (entry.Word ?? "").Trim();
            if (word.Length == 0 || word.IndexOf(' ') >= 0) return result;
            switch (entry.Class)
            {
                case LexiconEntry.ClassProper:
                    if (entry.HasFlexion) { Inflected(result, entry, ":N"); Inflected(result, entry, ":A", true); break; }
                    switch (entry.ProperKind)
                    {
                        case LexiconEntry.ProperFirstName:
                            result.Add(new[] { word, word, ":M1:" + GenderTag(entry.FirstNameGender()) + ":i" });
                            break;
                        case LexiconEntry.ProperSurname:
                            result.Add(new[] { word, word, ":M2:e:i" });
                            break;
                        default:
                            result.Add(new[] { word, word, ":MP:e:i" });
                            break;
                    }
                    foreach (var form in entry.DemonymForms())
                        result.Add(new[] { form, form, (char.IsUpper(form[0]) ? ":N:" : ":A:") + DemonymGender(form, entry) + ":" + (form.EndsWith("s") || form.EndsWith("x") ? "p" : "s") });
                    break;
                case LexiconEntry.ClassNoun:
                    Inflected(result, entry, ":N");
                    break;
                case LexiconEntry.ClassAdjective:
                    Inflected(result, entry, ":A");
                    break;
                case LexiconEntry.ClassAdverb:
                    result.Add(new[] { word, word, ":W" });
                    break;
            }
            return result;
        }

        /// <summary>Les quatre formes (posées ou dérivées) étiquetées genre
        /// et nombre ; le lemme est la forme masculine singulière.</summary>
        private static void Inflected(List<string[]> result, LexiconEntry entry, string pos, bool lowercase = false)
        {
            var derived = entry.DerivedForms();
            var forms = new[]
            {
                Pick(entry.MascSg, derived[0]), Pick(entry.MascPl, derived[1]),
                Pick(entry.FemSg, derived[2]), Pick(entry.FemPl, derived[3])
            };
            var genders = entry.EffectiveGenders();
            var lemma = forms[0] ?? forms[2] ?? entry.Word.Trim();
            if (lowercase) lemma = LexiconInflector.Uncapitalize(lemma);
            for (var i = 0; i < 4; i++)
            {
                var form = forms[i];
                if (string.IsNullOrEmpty(form)) continue;
                if (lowercase) form = LexiconInflector.Uncapitalize(form);
                // Un nom sans flexion connue (« ») : épicène, pour ne jamais
                // inventer une faute d'accord.
                var gender = genders.Length == 0 ? "e" : i < 2 ? "m" : "f";
                if (genders == LexiconEntry.GendersFeminine && i < 2) gender = "f"; // DerivedForms ne pose rien ici, par sûreté
                result.Add(new[] { form, lemma, pos + ":" + gender + ":" + (i % 2 == 0 ? "s" : "p") });
            }
        }

        private static string Pick(string posed, string derived)
        {
            var p = (posed ?? "").Trim();
            return p.Length > 0 ? p : derived;
        }

        private static string GenderTag(string gender)
        {
            return gender == LexiconEntry.GendersMasculine ? "m" : gender == LexiconEntry.GendersFeminine ? "f" : "e";
        }

        private static string DemonymGender(string form, LexiconEntry entry)
        {
            var masculine = LexiconInflector.Uncapitalize(entry.DemonymBase() ?? "");
            var lower = LexiconInflector.Uncapitalize(form);
            if (lower == masculine || lower == LexiconInflector.Pluralize(masculine, LexiconEntry.PluralS)) return "m";
            return "f";
        }

        /// <summary>Le texte de diagnostic : « forme lemme étiquette » par ligne.</summary>
        public static string Describe(List<string[]> entries)
        {
            var text = new StringBuilder();
            foreach (var e in entries) text.Append(e[0]).Append(' ').Append(e[1]).Append(' ').Append(e[2]).Append('\n');
            return text.ToString();
        }
    }
}

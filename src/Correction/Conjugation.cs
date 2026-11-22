using System;
using System.Collections.Generic;

namespace Marabook.Correction
{
    /// <summary>Ce que le conjugueur doit montrer : la voix pronominale, la
    /// négation, les temps composés, l'interrogation, le féminin.</summary>
    public class ConjugationOptions
    {
        public bool Pronominal, Negative, Compound, Interrogative, Feminine;

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                { "pronominal", Pronominal }, { "negative", Negative }, { "compound", Compound },
                { "interrogative", Interrogative }, { "feminine", Feminine }
            };
        }
    }

    public class ConjugationTense
    {
        public string Title = "";
        public readonly List<string> Forms = new List<string>();
    }

    public class ConjugationMood
    {
        public string Title = "";
        public readonly List<ConjugationTense> Tenses = new List<ConjugationTense>();
    }

    /// <summary>LE TABLEAU D'UN VERBE (20/11), tel que le conjugueur de
    /// Grammalecte le dresse : les modes, leurs temps, leurs formes avec
    /// le pronom. Le mot demandé peut être une forme conjuguée : Lemmas
    /// porte alors tous les verbes qu'elle peut être (« suis » : suivre,
    /// être), le premier est conjugué.</summary>
    public class Conjugation
    {
        public string Verb = "";
        public string Info = "";                       // « 1ᵉʳ groupe · transitif »
        public readonly List<string> Lemmas = new List<string>();
        /// <summary>−1 : jamais pronominal ; 0 : peut l'être ; 1 : toujours ; 2 : cas particuliers.</summary>
        public int Pronominal = -1;
        public bool UsedPronominal;
        public readonly List<ConjugationMood> Moods = new List<ConjugationMood>();

        public bool Found { get { return Verb.Length > 0 && Moods.Count > 0; } }
        public bool CanChoosePronominal { get { return Pronominal == 0 || Pronominal == 2; } }

        // Le plan du tableau : un mode, ses temps (la clé du titre, le préfixe des formes, leur nombre).
        private static readonly string[][] Plan =
        {
            new[] { "t_indi", "t_ipre:ipre:6", "t_iimp:iimp:6", "t_ipsi:ipsi:6", "t_ifut:ifut:6" },
            new[] { "t_cond", "t_conda:conda:6", "t_condb:condb:6" },
            new[] { "t_subj", "t_spre:spre:6", "t_simp:simp:6" },
            new[] { "t_imp", "t_impe:impe:3" },
            new[] { "Infinitif et participes", "t_infi:infi:0", "t_ppre:ppre:0", "t_ppas:ppas:4" }
        };

        /// <summary>Lit la réponse du pont ({"verb", "info", "lemmas",
        /// "pronominal", "table"}). Un mot qui n'est pas un verbe rend un
        /// tableau vide (Found faux).</summary>
        public static Conjugation Parse(object json)
        {
            var result = new Conjugation();
            var o = Json.AsObject(json);
            if (o == null) return result;
            result.Verb = (Json.AsString(Json.Field(o, "verb")) ?? "").Trim();
            var info = (Json.AsString(Json.Field(o, "info")) ?? "").Trim();
            // Grammalecte signale ainsi une étiquette qu'il ne sait pas lire : le groupe reste, le reste tombe.
            var fault = info.IndexOf("# erreur", StringComparison.Ordinal);
            if (fault >= 0) info = info.Substring(0, fault).Trim().TrimEnd('·').Trim();
            result.Info = info;
            var lemmas = Json.AsStringList(Json.Field(o, "lemmas"));
            if (lemmas != null) foreach (var lemma in lemmas) if (!string.IsNullOrEmpty(lemma) && !result.Lemmas.Contains(lemma)) result.Lemmas.Add(lemma);
            result.Pronominal = Json.AsInt(Json.Field(o, "pronominal"), -1);
            result.UsedPronominal = Json.AsBool(Json.Field(o, "usedPronominal"), false);
            var table = Json.AsObject(Json.Field(o, "table"));
            if (table == null || result.Verb.Length == 0) return result;
            foreach (var plan in Plan)
            {
                var mood = new ConjugationMood { Title = plan[0].StartsWith("t_", StringComparison.Ordinal) ? Text(table, plan[0]) : plan[0] };
                for (var i = 1; i < plan.Length; i++)
                {
                    var parts = plan[i].Split(':');
                    var count = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                    var tense = new ConjugationTense { Title = Text(table, parts[0]) };
                    if (count == 0) Add(tense, Text(table, parts[1]));
                    else for (var n = 1; n <= count; n++) Add(tense, Text(table, parts[1] + n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    // Un temps sans titre ni forme n'existe pas dans ces options (la 2ᵉ forme du conditionnel hors temps composés…).
                    if (tense.Forms.Count > 0) mood.Tenses.Add(tense);
                }
                if (mood.Tenses.Count > 0) result.Moods.Add(mood);
            }
            return result;
        }

        private static string Text(Dictionary<string, object> table, string key)
        {
            return (Json.AsString(Json.Field(table, key)) ?? "").Trim();
        }

        private static void Add(ConjugationTense tense, string form)
        {
            if (form.Length > 0) tense.Forms.Add(form);
        }

        /// <summary>Le tableau en texte, pour le presse-papiers.</summary>
        public string ToPlainText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Verb);
            if (Info.Length > 0) sb.Append(" — ").Append(Info);
            sb.Append('\n');
            foreach (var mood in Moods)
            {
                sb.Append('\n').Append(mood.Title.ToUpperInvariant()).Append('\n');
                foreach (var tense in mood.Tenses)
                {
                    if (tense.Title.Length > 0) sb.Append(tense.Title).Append(" : ");
                    sb.Append(string.Join(", ", tense.Forms.ToArray())).Append('\n');
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Le Wiktionnaire (20/11) : Marabook n'embarque pas de
    /// dictionnaire de définitions — un clic ouvre la page du mot dans le
    /// navigateur. Rien ne part sans ce clic.</summary>
    public static class Wiktionary
    {
        public const string Root = "https://fr.wiktionary.org/wiki/";

        /// <summary>L'adresse de la page du mot, ou null s'il n'y a pas de mot.</summary>
        public static string UrlFor(string word)
        {
            word = (word ?? "").Trim().Replace('’', '\'');
            if (word.Length == 0 || word.Length > 80) return null;
            foreach (var c in word) if (c == '\n' || c == '\r' || c == '/' || c == '#' || c == '?') return null;
            // Le Wiktionnaire range les noms communs en minuscules ; un mot tout en capitales y revient.
            if (word.Length > 1 && word == word.ToUpperInvariant() && word != word.ToLowerInvariant()) word = word.ToLowerInvariant();
            return Root + Uri.EscapeDataString(word.Replace(' ', '_'));
        }
    }
}

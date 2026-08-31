using System;
using System.Collections.Generic;
using System.Text;

namespace UniversSale.Model
{
    /// <summary>Une entrée du dictionnaire personnel (batch 33) — à la manière
    /// d'Antidote : le mot ET sa nature grammaticale, d'où le correcteur tire
    /// les FORMES acceptées (pluriel, féminin, conjugaison). Une entrée sans
    /// nature (« autre ») n'accepte que le mot tel quel — c'est la
    /// migration des anciens mots appris (listes de chaînes, .plot v9).</summary>
    public class LexiconEntry
    {
        // Natures : clés stables persistées.
        public const string ClassNoun = "noun";
        public const string ClassProper = "proper";
        public const string ClassAdjective = "adjective";
        public const string ClassVerb = "verb";
        public const string ClassAdverb = "adverb";
        public const string ClassOther = "other";

        public static readonly string[] Classes =
        { ClassNoun, ClassProper, ClassAdjective, ClassVerb, ClassAdverb, ClassOther };

        // Pluriels : "s" (régulier), "x", "inv" (invariable).
        public const string PluralS = "s";
        public const string PluralX = "x";
        public const string PluralInvariable = "inv";

        public string Word = "";
        public string Class = ClassOther;
        public string Gender = "";    // "m" | "f" | "" (noms, noms propres, adjectifs)
        public string Plural = "";    // "s" | "x" | "inv" | "" (= régulier)
        public string Feminine = "";  // forme féminine explicite ("" = dérivée par règle)
        public string Definition = ""; // définition du mot, affichée dans le dictionnaire
        public string Note = "";      // commentaire libre

        public static LexiconEntry Simple(string word)
        {
            return new LexiconEntry { Word = word ?? "", Class = ClassOther };
        }

        public static string ClassLabel(string key)
        {
            switch (key ?? "")
            {
                case ClassNoun: return "Nom commun";
                case ClassProper: return "Nom propre";
                case ClassAdjective: return "Adjectif";
                case ClassVerb: return "Verbe";
                case ClassAdverb: return "Adverbe";
                default: return "Autre (invariable)";
            }
        }

        public static string GenderLabel(string key)
        {
            switch (key ?? "")
            {
                case "m": return "masculin";
                case "f": return "féminin";
                default: return "";
            }
        }

        public static string PluralLabel(string key)
        {
            switch (key ?? "")
            {
                case PluralX: return "pluriel en -x";
                case PluralInvariable: return "invariable";
                default: return "pluriel en -s";
            }
        }

        /// <summary>Un résumé d'une ligne : « Nom commun, masculin, pluriel en -s ».</summary>
        public string Summary()
        {
            var parts = new List<string> { ClassLabel(Class) };
            if (Class == ClassNoun || Class == ClassProper || Class == ClassAdjective)
            {
                var gender = GenderLabel(Gender);
                if (gender.Length > 0) parts.Add(gender);
                parts.Add(PluralLabel(Plural));
                if (Class == ClassAdjective || (Class == ClassNoun && Feminine.Length > 0))
                {
                    var feminine = FeminineForm();
                    if (feminine != null && feminine != Word) parts.Add("féminin " + feminine);
                }
            }
            if (Class == ClassVerb)
                parts.Add(LexiconInflector.VerbGroup(Word) == 0 ? "infinitif seul" : "conjugaison régulière");
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>La forme féminine effective : explicite, sinon dérivée.</summary>
        public string FeminineForm()
        {
            if (Feminine.Trim().Length > 0) return Feminine.Trim();
            if (Class == ClassAdjective) return LexiconInflector.DeriveFeminine(Word);
            return null;
        }

        /// <summary>Toutes les formes que le correcteur accepte pour cette
        /// entrée, le mot compris.</summary>
        public List<string> Forms()
        {
            return LexiconInflector.Forms(this);
        }

        public LexiconEntry Clone()
        {
            return new LexiconEntry
            {
                Word = Word, Class = Class, Gender = Gender,
                Plural = Plural, Feminine = Feminine,
                Definition = Definition, Note = Note
            };
        }

        // ------------------------------------------------------------ JSON

        public Dictionary<string, object> ToJson()
        {
            var node = new Dictionary<string, object>();
            node["word"] = Word;
            node["class"] = Class;
            if (Gender.Length > 0) node["gender"] = Gender;
            if (Plural.Length > 0) node["plural"] = Plural;
            if (Feminine.Length > 0) node["feminine"] = Feminine;
            if (Definition.Length > 0) node["definition"] = Definition;
            if (Note.Length > 0) node["note"] = Note;
            return node;
        }

        public static LexiconEntry FromJson(Dictionary<string, object> node)
        {
            if (node == null) return null;
            var entry = new LexiconEntry
            {
                Word = Json.AsString(Json.Field(node, "word")) ?? "",
                Class = Json.AsString(Json.Field(node, "class")) ?? ClassOther,
                Gender = Json.AsString(Json.Field(node, "gender")) ?? "",
                Plural = Json.AsString(Json.Field(node, "plural")) ?? "",
                Feminine = Json.AsString(Json.Field(node, "feminine")) ?? "",
                Definition = Json.AsString(Json.Field(node, "definition")) ?? "",
                Note = Json.AsString(Json.Field(node, "note")) ?? ""
            };
            if (Array.IndexOf(Classes, entry.Class) < 0) entry.Class = ClassOther;
            return entry.Word.Trim().Length == 0 ? null : entry;
        }

        public static List<object> ToJsonList(List<LexiconEntry> entries)
        {
            var list = new List<object>();
            foreach (var entry in entries) list.Add(entry.ToJson());
            return list;
        }

        /// <summary>Lit une liste d'entrées ; les chaînes nues (anciens mots
        /// appris) deviennent des entrées « autre ».</summary>
        public static List<LexiconEntry> FromJsonList(List<object> list)
        {
            var entries = new List<LexiconEntry>();
            if (list == null) return entries;
            foreach (var node in list)
            {
                if (node is string) { if (((string)node).Trim().Length > 0) entries.Add(Simple((string)node)); continue; }
                var entry = FromJson(Json.AsObject(node));
                if (entry != null) entries.Add(entry);
            }
            return entries;
        }

        /// <summary>Ajoute des mots nus (migration des listes d'avant) sans
        /// doublonner une entrée existante (même mot, casse comprise).</summary>
        public static void MergeWords(List<LexiconEntry> entries, IEnumerable<string> words)
        {
            foreach (var word in words)
            {
                if (word == null || word.Trim().Length == 0) continue;
                if (Find(entries, word) != null) continue;
                entries.Add(Simple(word.Trim()));
            }
        }

        public static LexiconEntry Find(List<LexiconEntry> entries, string word)
        {
            foreach (var entry in entries)
                if (string.Equals(entry.Word, word, StringComparison.Ordinal)) return entry;
            return null;
        }
    }

    /// <summary>Les règles de flexion du dictionnaire personnel — le
    /// français régulier, sans dictionnaire : c'est ce qu'un auteur attend
    /// d'un nom de personnage, d'un peuple inventé ou d'un adjectif forgé.
    /// Testé en console (suite C11).</summary>
    public static class LexiconInflector
    {
        public static List<string> Forms(LexiconEntry entry)
        {
            var forms = new List<string>();
            var word = (entry.Word ?? "").Trim();
            if (word.Length == 0) return forms;
            Add(forms, word);
            switch (entry.Class)
            {
                case LexiconEntry.ClassNoun:
                case LexiconEntry.ClassProper:
                    Add(forms, Pluralize(word, entry.Plural));
                    if (entry.Feminine.Trim().Length > 0)
                    {
                        Add(forms, entry.Feminine.Trim());
                        Add(forms, Pluralize(entry.Feminine.Trim(), LexiconEntry.PluralS));
                    }
                    break;
                case LexiconEntry.ClassAdjective:
                    Add(forms, Pluralize(word, entry.Plural));
                    var feminine = entry.FeminineForm() ?? word;
                    Add(forms, feminine);
                    Add(forms, Pluralize(feminine, LexiconEntry.PluralS));
                    break;
                case LexiconEntry.ClassVerb:
                    foreach (var form in Conjugate(word)) Add(forms, form);
                    break;
            }
            return forms;
        }

        private static void Add(List<string> forms, string form)
        {
            if (form == null || form.Length == 0) return;
            if (!forms.Contains(form)) forms.Add(form);
        }

        /// <summary>Le pluriel selon le réglage : -s (sauf finale s/x/z),
        /// -x (« -al » → « -aux », « -au/-eu » → « -x »), ou invariable.</summary>
        public static string Pluralize(string word, string plural)
        {
            if (word.Length == 0 || plural == LexiconEntry.PluralInvariable) return null;
            var last = char.ToLowerInvariant(word[word.Length - 1]);
            if (last == 's' || last == 'x' || last == 'z') return null;
            if (plural == LexiconEntry.PluralX)
            {
                if (word.EndsWith("al", StringComparison.OrdinalIgnoreCase))
                    return word.Substring(0, word.Length - 2) + "aux";
                if (word.EndsWith("ail", StringComparison.OrdinalIgnoreCase))
                    return word.Substring(0, word.Length - 3) + "aux";
                return word + "x";
            }
            return word + "s";
        }

        /// <summary>Le féminin régulier d'un adjectif masculin : -e → même
        /// forme ; -eux → -euse ; -eur → -euse ; -teur → -trice ; -if → -ive ;
        /// -el/-eil/-en/-on/-et → consonne doublée + e ; -er → -ère ;
        /// -c → -que ; -g → -gue ; -x → -se ; sinon + e.</summary>
        public static string DeriveFeminine(string word)
        {
            if (word.Length == 0) return word;
            var lower = word.ToLowerInvariant();
            if (lower.EndsWith("e")) return word;
            if (lower.EndsWith("eux")) return word.Substring(0, word.Length - 3) + "euse";
            if (lower.EndsWith("teur")) return word.Substring(0, word.Length - 4) + "trice";
            if (lower.EndsWith("eur")) return word.Substring(0, word.Length - 3) + "euse";
            if (lower.EndsWith("if")) return word.Substring(0, word.Length - 2) + "ive";
            if (lower.EndsWith("er")) return word.Substring(0, word.Length - 2) + "ère";
            if (lower.EndsWith("el") || lower.EndsWith("eil") || lower.EndsWith("en")
                || lower.EndsWith("on") || lower.EndsWith("et"))
                return word + word[word.Length - 1] + "e";
            if (lower.EndsWith("c")) return word.Substring(0, word.Length - 1) + "que"; // public → publique
            if (lower.EndsWith("g")) return word + "ue";
            if (lower.EndsWith("x")) return word.Substring(0, word.Length - 1) + "se";
            return word + "e";
        }

        /// <summary>1 = premier groupe (-er), 2 = deuxième (-ir régulier,
        /// type finir), 0 = hors tables (infinitif seul).</summary>
        public static int VerbGroup(string infinitive)
        {
            var lower = (infinitive ?? "").ToLowerInvariant();
            if (lower.Length < 3) return 0;
            if (lower.EndsWith("er") && lower != "aller") return 1;
            if (lower.EndsWith("ir") && !lower.EndsWith("oir")) return 2;
            return 0;
        }

        /// <summary>La conjugaison régulière complète (indicatif, subjonctif,
        /// conditionnel, impératif, participes avec accords) du premier et
        /// du deuxième groupe ; les verbes en -cer/-ger gardent leur son.</summary>
        public static List<string> Conjugate(string infinitive)
        {
            var forms = new List<string>();
            var group = VerbGroup(infinitive);
            if (group == 0) return forms;
            var stem = infinitive.Substring(0, infinitive.Length - 2);
            if (group == 1)
            {
                var lower = stem.ToLowerInvariant();
                var soft = lower.EndsWith("c") ? stem.Substring(0, stem.Length - 1) + "ç"
                         : lower.EndsWith("g") ? stem + "e" : stem; // commençons, mangeons
                // présent
                Add(forms, stem + "e"); Add(forms, stem + "es"); Add(forms, soft + "ons");
                Add(forms, stem + "ez"); Add(forms, stem + "ent");
                // imparfait
                foreach (var end in new[] { "ais", "ait", "ions", "iez", "aient" })
                    Add(forms, (end.StartsWith("i") ? stem : soft) + end);
                // passé simple
                Add(forms, soft + "ai"); Add(forms, soft + "as"); Add(forms, soft + "a");
                Add(forms, soft + "âmes"); Add(forms, soft + "âtes"); Add(forms, stem + "èrent");
                // futur + conditionnel
                foreach (var end in new[] { "ai", "as", "a", "ons", "ez", "ont", "ais", "ait", "ions", "iez", "aient" })
                    Add(forms, infinitive + end);
                // subjonctif présent
                Add(forms, stem + "e"); Add(forms, stem + "es"); Add(forms, stem + "ions");
                Add(forms, stem + "iez"); Add(forms, stem + "ent");
                // subjonctif imparfait
                foreach (var end in new[] { "asse", "asses", "ât", "assions", "assiez", "assent" })
                    Add(forms, soft + end);
                // participes
                Add(forms, soft + "ant");
                Add(forms, stem + "é"); Add(forms, stem + "ée"); Add(forms, stem + "és"); Add(forms, stem + "ées");
            }
            else
            {
                // présent
                Add(forms, stem + "is"); Add(forms, stem + "it"); Add(forms, stem + "issons");
                Add(forms, stem + "issez"); Add(forms, stem + "issent");
                // imparfait
                foreach (var end in new[] { "issais", "issait", "issions", "issiez", "issaient" })
                    Add(forms, stem + end);
                // passé simple
                foreach (var end in new[] { "is", "it", "îmes", "îtes", "irent" })
                    Add(forms, stem + end);
                // futur + conditionnel
                foreach (var end in new[] { "ai", "as", "a", "ons", "ez", "ont", "ais", "ait", "ions", "iez", "aient" })
                    Add(forms, infinitive + end);
                // subjonctif présent
                foreach (var end in new[] { "isse", "isses", "isse", "issions", "issiez", "issent" })
                    Add(forms, stem + end);
                // subjonctif imparfait
                foreach (var end in new[] { "isse", "isses", "ît", "issions", "issiez", "issent" })
                    Add(forms, stem + end);
                // participes
                Add(forms, stem + "issant");
                Add(forms, stem + "i"); Add(forms, stem + "ie"); Add(forms, stem + "is"); Add(forms, stem + "ies");
            }
            return forms;
        }

        /// <summary>Devine la nature d'un mot signalé : une majuscule
        /// initiale suggère un nom propre, une finale en -er/-ir un verbe.</summary>
        public static string GuessClass(string word)
        {
            if (string.IsNullOrEmpty(word)) return LexiconEntry.ClassOther;
            if (char.IsUpper(word[0])) return LexiconEntry.ClassProper;
            if (VerbGroup(word) != 0 && word.Length > 4) return LexiconEntry.ClassVerb;
            return LexiconEntry.ClassNoun;
        }

        /// <summary>Les formes acceptées, sur une ligne, pour l'aperçu.</summary>
        public static string Preview(LexiconEntry entry, int max)
        {
            var forms = Forms(entry);
            var sb = new StringBuilder();
            for (var i = 0; i < forms.Count && i < max; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(forms[i]);
            }
            if (forms.Count > max) sb.Append("… (" + forms.Count + " formes)");
            return sb.ToString();
        }
    }
}

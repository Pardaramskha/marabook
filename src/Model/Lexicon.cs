using System;
using System.Collections.Generic;
using System.Text;

namespace Marabook.Model
{
    /// <summary>Une entrée du dictionnaire personnel (batch 33, refondue le
    /// 30/09 à la manière d'Antidote) : le mot, son TYPE (nom, adjectif,
    /// adverbe, nom propre — verbe conservé pour les verbes forgés), sa
    /// NATURE (des cases qui précisent : personne, fonction sociale, animal,
    /// nom d'habitant, adverbe de temps… ; pour un nom propre, sa sorte et,
    /// pour un lieu, ce qu'il est), et sa FLEXION : masculin et féminin,
    /// masculin, ou féminin, avec les quatre formes — masc. sg., masc. pl.,
    /// fém. sg., fém. pl. — dérivées par la règle quand elles restent vides,
    /// posées à la main sinon. Le correcteur en tire les formes acceptées.
    ///
    /// MIGRATION : une entrée de l'ancien format (sans clé « v ») est
    /// convertie à la lecture — genre → flexion, féminin explicite → fém.
    /// sg. — et marquée « migration nécessaire » (NeedsReview) tant que
    /// l'utilisateur ne l'a pas revue dans le dialogue ; les mots nus des
    /// premiers .plot deviennent des entrées « autre » à revoir de même.</summary>
    public class LexiconEntry
    {
        /// <summary>La version du format persisté ; absente = ancien format.</summary>
        public const int Format = 2;

        // Types : clés stables persistées.
        public const string ClassNoun = "noun";
        public const string ClassProper = "proper";
        public const string ClassAdjective = "adjective";
        public const string ClassVerb = "verb";
        public const string ClassAdverb = "adverb";
        public const string ClassOther = "other";

        /// <summary>Les types dans l'ordre d'affichage : Nom, Adjectif,
        /// Adverbe, Nom propre (la liste d'Antidote), puis Verbe ; « autre »
        /// n'est que le type des mots importés ou migrés sans nature.</summary>
        public static readonly string[] Classes =
        { ClassNoun, ClassAdjective, ClassAdverb, ClassProper, ClassVerb, ClassOther };

        // Pluriels : "s" (régulier), "x", "inv" (invariable).
        public const string PluralS = "s";
        public const string PluralX = "x";
        public const string PluralInvariable = "inv";

        // Flexions : masculin et féminin, masculin, féminin ; "" = inconnue / sans objet.
        public const string GendersBoth = "mf";
        public const string GendersMasculine = "m";
        public const string GendersFeminine = "f";

        // Sortes de nom propre (boutons radio).
        public const string ProperSurname = "surname";
        public const string ProperFirstName = "firstname";
        public const string ProperCompany = "company";
        public const string ProperBrand = "brand";
        public const string ProperPlace = "place";
        public const string ProperDemonym = "demonym";
        public const string ProperTitle = "title";
        public const string ProperOther = "other";
        public static readonly string[] ProperKinds =
        { ProperSurname, ProperFirstName, ProperCompany, ProperBrand, ProperPlace, ProperDemonym, ProperTitle, ProperOther };

        public string Word = "";
        public string Class = ClassOther;
        public string Gender = "";    // ANCIEN format : "m" | "f" | "" — relu, encore écrit en miroir de Genders
        public string Plural = "";    // "s" | "x" | "inv" | "" (= régulier) : la règle qui dérive un pluriel vide
        public string Feminine = "";  // ANCIEN format : forme féminine explicite (migrée dans FemSg)
        public string Definition = ""; // définition du mot, affichée dans le dictionnaire
        public string Note = "";      // commentaire libre

        // ---- le format 2 (30/09)
        public string Genders = "";   // "mf" | "m" | "f" | "" (inconnue, ou sans objet : adverbe, verbe, autre)
        public string MascSg = "";    // les quatre formes ; vide = dérivée par la règle
        public string MascPl = "";
        public string FemSg = "";
        public string FemPl = "";
        public List<string> Traits = new List<string>(); // les natures cochées (clés de TraitDefinition)
        public string ProperKind = ""; // nom propre : sa sorte (ProperKinds), "" = non précisée
        public bool NeedsReview;      // « migration nécessaire » : venue de l'ancien format, pas encore revue

        // ---- le gentilé dérivé (01/10) : d'un lieu (ou d'une raison sociale,
        // d'une marque, d'un nom propre « autre »), la variante d'appartenance
        // — Mànis → mànisien, mànisienne, Mànisiens… — par un suffixe choisi
        // ou tapé ; la forme masculine se pose à la main si la règle se trompe.
        public string DemonymSuffix = ""; // "ien", "ais"… ; "" = pas de gentilé dérivé
        public string DemonymForm = "";   // masc. sg. posé ("" = dérivé du mot et du suffixe)

        /// <summary>Les suffixes de gentilés proposés en préconfiguration.</summary>
        public static readonly string[] DemonymSuffixes =
        { "ien", "ais", "ois", "ain", "éen", "in", "an", "on", "ard", "ite", "ol" };

        /// <summary>Les sortes de nom propre dont on dérive un gentilé : un
        /// lieu, une raison sociale, une marque, un nom propre « autre » (un
        /// peuple, une planète…). Un gentilé, un prénom, un nom de famille ou
        /// un titre n'en ont pas.</summary>
        public static bool AllowsDemonym(string properKind)
        {
            return properKind == ProperPlace || properKind == ProperCompany
                || properKind == ProperBrand || properKind == ProperOther;
        }

        /// <summary>Un gentilé est-il dérivé de cette entrée ?</summary>
        public bool HasDemonym
        {
            get { return Class == ClassProper && AllowsDemonym(ProperKind) && DemonymSuffix.Trim().Length > 0; }
        }

        /// <summary>Le gentilé masculin singulier : posé, sinon dérivé du mot
        /// et du suffixe ; null sans gentilé.</summary>
        public string DemonymBase()
        {
            if (!HasDemonym) return null;
            if (DemonymForm.Trim().Length > 0) return DemonymForm.Trim();
            return LexiconInflector.DeriveDemonym(Word, DemonymSuffix);
        }

        public static LexiconEntry Simple(string word)
        {
            return new LexiconEntry { Word = word ?? "", Class = ClassOther, NeedsReview = true };
        }

        // ------------------------------------------------------------ natures

        /// <summary>Une nature à cocher : sa clé persistée, son libellé, le type
        /// qui l'offre, et sa nature MÈRE quand elle en précise une (« Entité
        /// non comptable » sous « Chose ou concept ») ; pour un nom propre,
        /// la sorte qui l'offre (ProperKind) : les sous-cases de « Lieu ».</summary>
        public sealed class TraitDefinition
        {
            public string Key, Label, Class, Parent, ProperKind;
        }

        public static readonly TraitDefinition[] TraitCatalog =
        {
            new TraitDefinition { Key = "thing", Label = "Chose ou concept", Class = ClassNoun },
            new TraitDefinition { Key = "uncountable", Label = "Entité non comptable", Class = ClassNoun, Parent = "thing" },
            new TraitDefinition { Key = "person", Label = "Personne", Class = ClassNoun },
            new TraitDefinition { Key = "role", Label = "Fonction sociale", Class = ClassNoun, Parent = "person" },
            new TraitDefinition { Key = "animal", Label = "Animal", Class = ClassNoun },
            new TraitDefinition { Key = "unit", Label = "Unité de mesure", Class = ClassNoun },
            new TraitDefinition { Key = "demonym", Label = "Nom d'habitant", Class = ClassAdjective },
            new TraitDefinition { Key = "language", Label = "Langue", Class = ClassAdjective, Parent = "demonym" },
            new TraitDefinition { Key = "manner", Label = "De manière", Class = ClassAdverb },
            new TraitDefinition { Key = "time", Label = "De temps", Class = ClassAdverb },
            new TraitDefinition { Key = "place", Label = "De lieu", Class = ClassAdverb },
            new TraitDefinition { Key = "city", Label = "Ville", Class = ClassProper, ProperKind = ProperPlace },
            new TraitDefinition { Key = "island", Label = "Île", Class = ClassProper, ProperKind = ProperPlace },
            new TraitDefinition { Key = "country", Label = "Pays / Région", Class = ClassProper, ProperKind = ProperPlace },
            new TraitDefinition { Key = "water", Label = "Plan ou cours d'eau", Class = ClassProper, ProperKind = ProperPlace },
            new TraitDefinition { Key = "star", Label = "Astre", Class = ClassProper, ProperKind = ProperPlace },
            new TraitDefinition { Key = "world", Label = "Monde", Class = ClassProper, ProperKind = ProperPlace },
            new TraitDefinition { Key = "proper-language", Label = "Langue", Class = ClassProper, ProperKind = ProperDemonym },
        };

        /// <summary>Les natures offertes par un type, dans l'ordre du catalogue.</summary>
        public static List<TraitDefinition> TraitsFor(string cls)
        {
            var result = new List<TraitDefinition>();
            foreach (var trait in TraitCatalog) if (trait.Class == cls) result.Add(trait);
            return result;
        }

        public static TraitDefinition TraitByKey(string key)
        {
            foreach (var trait in TraitCatalog) if (trait.Key == key) return trait;
            return null;
        }

        public bool HasTrait(string key) { return Traits != null && Traits.Contains(key); }

        public static string ProperKindLabel(string key)
        {
            switch (key ?? "")
            {
                case ProperSurname: return "Nom de famille";
                case ProperFirstName: return "Prénom";
                case ProperCompany: return "Raison sociale";
                case ProperBrand: return "Marque";
                case ProperPlace: return "Lieu";
                case ProperDemonym: return "Gentilé";
                case ProperTitle: return "Titre d'œuvre";
                case ProperOther: return "Autre";
                default: return "";
            }
        }

        // ------------------------------------------------------------ libellés

        public static string ClassLabel(string key)
        {
            switch (key ?? "")
            {
                case ClassNoun: return "Nom";
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

        public static string GendersLabel(string key)
        {
            switch (key ?? "")
            {
                case GendersBoth: return "masculin et féminin";
                case GendersMasculine: return "masculin";
                case GendersFeminine: return "féminin";
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

        /// <summary>Les types qui se fléchissent : nom, adjectif — et, parmi
        /// les noms propres, le seul gentilé (01/10) : un nom de famille, un
        /// prénom, un lieu, une marque ne se déclinent pas.</summary>
        public bool HasFlexion
        {
            get { return Class == ClassNoun || Class == ClassAdjective || (Class == ClassProper && ProperKind == ProperDemonym); }
        }

        /// <summary>Le pluriel effectif : « invariable » quand la nature le
        /// dit (entité non comptable, 01/10), le réglage sinon.</summary>
        public string EffectivePlural()
        {
            if (Class == ClassNoun && HasTrait("uncountable")) return PluralInvariable;
            return Plural;
        }

        /// <summary>La flexion effective : celle posée, sinon celle que
        /// l'ancien format laisse deviner (genre, féminin explicite, adjectif
        /// = les deux) ; "" quand rien ne le dit.</summary>
        public string EffectiveGenders()
        {
            if (!HasFlexion) return "";
            if (Genders == GendersBoth || Genders == GendersMasculine || Genders == GendersFeminine) return Genders;
            if (Feminine.Trim().Length > 0 || FemSg.Trim().Length > 0) return GendersBoth;
            if (Class == ClassAdjective) return GendersBoth;
            if (Gender == "m") return GendersMasculine;
            if (Gender == "f") return GendersFeminine;
            return "";
        }

        /// <summary>Un résumé d'une ligne : « Nom · personne, fonction sociale ·
        /// masculin et féminin ».</summary>
        public string Summary()
        {
            var parts = new List<string> { ClassLabel(Class) };
            var natures = new List<string>();
            if (Class == ClassProper && ProperKind.Length > 0) natures.Add(ProperKindLabel(ProperKind).ToLowerInvariant());
            foreach (var key in Traits)
            {
                var trait = TraitByKey(key);
                if (trait != null && trait.Class == Class) natures.Add(trait.Label.ToLowerInvariant());
            }
            if (natures.Count > 0) parts.Add(string.Join(", ", natures.ToArray()));
            if (HasFlexion)
            {
                var genders = GendersLabel(EffectiveGenders());
                if (genders.Length > 0) parts.Add(genders);
                if (Plural == PluralInvariable) parts.Add("invariable");
                else if (Plural == PluralX) parts.Add("pluriel en -x");
                var feminine = FeminineForm();
                if ((FemSg.Trim().Length > 0 || Feminine.Trim().Length > 0) && feminine != null && feminine != Word)
                    parts.Add("féminin " + feminine);
            }
            if (Class == ClassVerb)
                parts.Add(LexiconInflector.VerbGroup(Word) == 0 ? "infinitif seul" : "conjugaison régulière");
            var demonym = DemonymBase();
            if (!string.IsNullOrEmpty(demonym)) parts.Add("gentilé " + demonym);
            return string.Join(" · ", parts.ToArray());
        }

        /// <summary>La forme féminine singulière effective : posée (fém. sg.,
        /// ou l'ancien féminin explicite), sinon dérivée ; null quand la
        /// flexion n'a pas de féminin.</summary>
        public string FeminineForm()
        {
            if (FemSg.Trim().Length > 0) return FemSg.Trim();
            if (Feminine.Trim().Length > 0) return Feminine.Trim();
            var genders = EffectiveGenders();
            if (genders == GendersFeminine) return MascSg.Trim().Length > 0 ? null : Word.Trim();
            if (genders != GendersBoth) return null;
            return LexiconInflector.DeriveFeminine(MasculineForm());
        }

        /// <summary>La forme masculine singulière : posée, sinon le mot.</summary>
        public string MasculineForm()
        {
            return MascSg.Trim().Length > 0 ? MascSg.Trim() : Word.Trim();
        }

        /// <summary>Les quatre formes telles que la règle les DÉRIVERAIT si les
        /// champs restaient vides (le dialogue les montre en filigrane) :
        /// [masc. sg., masc. pl., fém. sg., fém. pl.], null = sans objet.</summary>
        public string[] DerivedForms()
        {
            var result = new string[4];
            if (!HasFlexion) return result;
            var genders = EffectiveGenders();
            if (genders.Length == 0) genders = GendersMasculine;
            var word = Word.Trim();
            var plural = EffectivePlural();
            if (genders != GendersFeminine)
            {
                result[0] = word;
                result[1] = LexiconInflector.Pluralize(word, plural);
            }
            if (genders != GendersMasculine)
            {
                var feminine = genders == GendersFeminine ? word
                    : Feminine.Trim().Length > 0 ? Feminine.Trim()
                    : LexiconInflector.DeriveFeminine(word);
                result[2] = feminine;
                result[3] = LexiconInflector.Pluralize(feminine, plural == PluralInvariable ? PluralInvariable : PluralS);
            }
            return result;
        }

        /// <summary>Les formes du gentilé dérivé (01/10) : masc. sg., masc.
        /// pl., fém. sg., fém. pl. — en minuscule (l'adjectif : « une rue
        /// mànisienne ») et à majuscule initiale (l'habitant : « les
        /// Mànisiens »). Vide sans gentilé.</summary>
        public List<string> DemonymForms()
        {
            var forms = new List<string>();
            var masculine = DemonymBase();
            if (string.IsNullOrEmpty(masculine)) return forms;
            var feminine = LexiconInflector.DeriveFeminine(masculine);
            foreach (var form in new[] { masculine, LexiconInflector.Pluralize(masculine, PluralS), feminine, LexiconInflector.Pluralize(feminine, PluralS) })
            {
                if (string.IsNullOrEmpty(form)) continue;
                if (!forms.Contains(form)) forms.Add(form);
                var capital = LexiconInflector.Capitalize(form);
                if (!forms.Contains(capital)) forms.Add(capital);
            }
            return forms;
        }

        /// <summary>Toutes les formes que le correcteur accepte pour cette
        /// entrée, le mot compris.</summary>
        public List<string> Forms()
        {
            return LexiconInflector.Forms(this);
        }

        /// <summary>L'entrée est-elle complète au sens du nouveau format : un
        /// type, une flexion quand le type en a une, une sorte pour un nom
        /// propre ? Le dialogue s'en sert pour lever la pastille.</summary>
        public bool IsComplete()
        {
            if (Class == ClassOther) return false;
            if (HasFlexion && EffectiveGenders().Length == 0) return false;
            if (Class == ClassProper && ProperKind.Length == 0) return false;
            return true;
        }

        public LexiconEntry Clone()
        {
            return new LexiconEntry
            {
                Word = Word, Class = Class, Gender = Gender,
                Plural = Plural, Feminine = Feminine,
                Definition = Definition, Note = Note,
                Genders = Genders, MascSg = MascSg, MascPl = MascPl, FemSg = FemSg, FemPl = FemPl,
                Traits = new List<string>(Traits), ProperKind = ProperKind, NeedsReview = NeedsReview,
                DemonymSuffix = DemonymSuffix, DemonymForm = DemonymForm
            };
        }

        // ------------------------------------------------------------ JSON

        public Dictionary<string, object> ToJson()
        {
            var node = new Dictionary<string, object>();
            node["word"] = Word;
            node["class"] = Class;
            node["v"] = Format;
            // Les clés de l'ancien format en miroir : une version d'avant lit
            // encore le genre et le féminin de l'entrée.
            var genders = EffectiveGenders();
            if (genders.Length == 0 && (Genders == GendersMasculine || Genders == GendersFeminine)) genders = Genders; // un nom propre sans flexion garde son genre en miroir (01/10)
            var mirrorGender = genders == GendersMasculine ? "m" : genders == GendersFeminine ? "f" : Gender;
            if (mirrorGender.Length > 0) node["gender"] = mirrorGender;
            if (Plural.Length > 0) node["plural"] = Plural;
            var feminine = Feminine.Trim().Length > 0 ? Feminine.Trim() : FemSg.Trim(); // l'ancien champ tel quel s'il vit encore, sinon la forme posée
            if (feminine.Length > 0) node["feminine"] = feminine;
            if (Definition.Length > 0) node["definition"] = Definition;
            if (Note.Length > 0) node["note"] = Note;
            if (Genders.Length > 0) node["genders"] = Genders;
            if (MascSg.Length > 0) node["ms"] = MascSg;
            if (MascPl.Length > 0) node["mp"] = MascPl;
            if (FemSg.Length > 0) node["fs"] = FemSg;
            if (FemPl.Length > 0) node["fp"] = FemPl;
            if (Traits.Count > 0) node["traits"] = new List<object>(Traits.ToArray());
            if (ProperKind.Length > 0) node["properKind"] = ProperKind;
            if (NeedsReview) node["review"] = true;
            if (DemonymSuffix.Length > 0) node["gentileSuffix"] = DemonymSuffix;
            if (DemonymForm.Length > 0) node["gentile"] = DemonymForm;
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
            if (entry.Word.Trim().Length == 0) return null;
            var raw = Json.Field(node, "v");
            var version = raw is int ? (int)raw : raw is long ? (int)(long)raw : (int)Json.AsDouble(raw, 0);
            if (version < Format)
            {
                Migrate(entry);
                return entry;
            }
            // Relu tel quel, sans filtre (30/09) : une valeur qu'une version
            // plus récente connaît survit à l'aller-retour ; l'inconnu est
            // simplement ignoré par les règles.
            entry.Genders = Json.AsString(Json.Field(node, "genders")) ?? "";
            entry.MascSg = Json.AsString(Json.Field(node, "ms")) ?? "";
            entry.MascPl = Json.AsString(Json.Field(node, "mp")) ?? "";
            entry.FemSg = Json.AsString(Json.Field(node, "fs")) ?? "";
            entry.FemPl = Json.AsString(Json.Field(node, "fp")) ?? "";
            var traits = Json.AsList(Json.Field(node, "traits"));
            if (traits != null)
                foreach (var item in traits)
                {
                    var key = Json.AsString(item);
                    if (!string.IsNullOrEmpty(key) && !entry.Traits.Contains(key)) entry.Traits.Add(key);
                }
            entry.ProperKind = Json.AsString(Json.Field(node, "properKind")) ?? "";
            entry.NeedsReview = Json.AsBool(Json.Field(node, "review"), false);
            entry.DemonymSuffix = Json.AsString(Json.Field(node, "gentileSuffix")) ?? "";
            entry.DemonymForm = Json.AsString(Json.Field(node, "gentile")) ?? "";
            return entry;
        }

        /// <summary>La conversion d'une entrée de l'ANCIEN format (30/09) — le
        /// script de migration, appliqué à la lecture des .plot, des réglages
        /// et des dictionnaires exportés : le genre devient la flexion, le
        /// féminin explicite la forme fém. sg. ; un verbe n'a rien à revoir,
        /// tout le reste porte la pastille « migration nécessaire » jusqu'à
        /// ce que type, nature et flexion aient été confirmés.</summary>
        public static void Migrate(LexiconEntry entry)
        {
            if (entry == null) return;
            if (entry.Feminine.Trim().Length > 0)
            {
                entry.FemSg = entry.Feminine.Trim();
                entry.Genders = GendersBoth;
            }
            else if (entry.Class == ClassAdjective) entry.Genders = GendersBoth;
            else if (entry.Gender == "m") entry.Genders = GendersMasculine;
            else if (entry.Gender == "f") entry.Genders = GendersFeminine;
            entry.Feminine = "";
            entry.NeedsReview = entry.Class != ClassVerb;
        }

        /// <summary>Convertit une liste entière ; rend le nombre d'entrées
        /// qui restent à revoir.</summary>
        public static int CountNeedingReview(List<LexiconEntry> entries)
        {
            var count = 0;
            if (entries != null) foreach (var entry in entries) if (entry.NeedsReview) count++;
            return count;
        }

        public static List<object> ToJsonList(List<LexiconEntry> entries)
        {
            var list = new List<object>();
            foreach (var entry in entries) list.Add(entry.ToJson());
            return list;
        }

        /// <summary>Lit une liste d'entrées ; les chaînes nues (anciens mots
        /// appris) deviennent des entrées « autre » à revoir.</summary>
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

        /// <summary>L'entrée dont le mot OU une forme acceptée (pluriel,
        /// féminin, conjugaison) est ce mot, casse ignorée — ce qu'un clic
        /// droit sur « dragons » doit retrouver (18/09). Le mot exact prime.</summary>
        public static LexiconEntry FindByForm(List<LexiconEntry> entries, string word)
        {
            if (entries == null || string.IsNullOrEmpty(word)) return null;
            foreach (var entry in entries)
                if (string.Equals(entry.Word, word, StringComparison.OrdinalIgnoreCase)) return entry;
            foreach (var entry in entries)
                foreach (var form in entry.Forms())
                    if (string.Equals(form, word, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }
    }

    /// <summary>Les règles de flexion du dictionnaire personnel — le
    /// français régulier, sans dictionnaire : c'est ce qu'un auteur attend
    /// d'un nom de personnage, d'un peuple inventé ou d'un adjectif forgé.
    /// Testé en console (suite C11).</summary>
    public static class LexiconInflector
    {
        /// <summary>Les formes acceptées : le mot, puis — nom, adjectif, nom
        /// propre — les quatre formes de la flexion (posées, sinon dérivées),
        /// — verbe — la conjugaison régulière.</summary>
        public static List<string> Forms(LexiconEntry entry)
        {
            var forms = new List<string>();
            var word = (entry.Word ?? "").Trim();
            if (word.Length == 0) return forms;
            Add(forms, word);
            if (entry.HasFlexion)
            {
                var derived = entry.DerivedForms();
                var explicitForms = new[] { entry.MascSg, entry.MascPl, entry.FemSg, entry.FemPl };
                var flexion = new List<string>();
                for (var i = 0; i < 4; i++)
                {
                    var posed = (explicitForms[i] ?? "").Trim();
                    Add(flexion, posed.Length > 0 ? posed : derived[i]);
                }
                foreach (var form in flexion) Add(forms, form);
                // La nature change les formes (01/10) : un nom d'habitant
                // (adjectif) s'écrit aussi avec la majuscule de l'habitant ;
                // un gentilé (nom propre) s'écrit aussi en minuscule, l'adjectif.
                if (entry.Class == LexiconEntry.ClassAdjective && entry.HasTrait("demonym"))
                    foreach (var form in flexion) Add(forms, Capitalize(form));
                if (entry.Class == LexiconEntry.ClassProper && entry.ProperKind == LexiconEntry.ProperDemonym)
                    foreach (var form in flexion) Add(forms, Uncapitalize(form));
            }
            else if (entry.Class == LexiconEntry.ClassVerb)
                foreach (var form in Conjugate(word)) Add(forms, form);
            foreach (var form in entry.DemonymForms()) Add(forms, form);
            return forms;
        }

        /// <summary>Majuscule initiale (« mànisien » → « Mànisien »).</summary>
        public static string Capitalize(string form)
        {
            if (string.IsNullOrEmpty(form)) return form;
            return char.ToUpperInvariant(form[0]) + form.Substring(1);
        }

        /// <summary>Minuscule initiale (« Mànisien » → « mànisien »).</summary>
        public static string Uncapitalize(string form)
        {
            if (string.IsNullOrEmpty(form)) return form;
            return char.ToLowerInvariant(form[0]) + form.Substring(1);
        }

        /// <summary>Le gentilé masculin singulier dérivé d'un nom de lieu et
        /// d'un suffixe (01/10) : le mot en minuscules, sa voyelle finale
        /// muette retirée (Rome → romain, Nantes → nantais, Mexico →
        /// mexicain), le suffixe collé — Mànis → mànisien. La règle se trompe
        /// sur les gentilés irréguliers (Bordeaux, Saint-Malo…) : la forme se
        /// pose alors à la main.</summary>
        public static string DeriveDemonym(string word, string suffix)
        {
            var stem = (word ?? "").Trim().ToLowerInvariant();
            var ending = (suffix ?? "").Trim().TrimStart('-').ToLowerInvariant();
            if (stem.Length == 0 || ending.Length == 0) return null;
            if (stem.Length > 4 && stem.EndsWith("es")) stem = stem.Substring(0, stem.Length - 2);
            else if (stem.Length > 3 && (stem.EndsWith("e") || stem.EndsWith("a") || stem.EndsWith("o")))
                stem = stem.Substring(0, stem.Length - 1);
            // Deux voyelles identiques à la jointure (Mànisi + ien) : une seule.
            if (stem.Length > 0 && ending.Length > 0 && stem[stem.Length - 1] == ending[0] && "aeiou".IndexOf(ending[0]) >= 0)
                stem = stem.Substring(0, stem.Length - 1);
            return stem + ending;
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
            if (word == null || word.Length == 0 || plural == LexiconEntry.PluralInvariable) return null;
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

        /// <summary>Le féminin régulier d'un masculin : -e → même forme ;
        /// -eux → -euse ; -eur → -euse ; -teur → -trice ; -if → -ive ;
        /// -el/-eil/-en/-on/-et → consonne doublée + e ; -er → -ère ;
        /// -c → -que ; -g → -gue ; -x → -se ; sinon + e.</summary>
        public static string DeriveFeminine(string word)
        {
            if (word == null || word.Length == 0) return word;
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

        /// <summary>Devine le type d'un mot signalé : une majuscule initiale
        /// suggère un nom propre, une finale en -er/-ir un verbe.</summary>
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

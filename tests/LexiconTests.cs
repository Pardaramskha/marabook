using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C11 — le dictionnaire personnel à natures (batch 33) : les
    /// formes que le correcteur accepte d'une entrée (pluriels, féminins,
    /// conjugaisons régulières), la migration des mots nus, l'aller-retour
    /// JSON des entrées, la devinette de nature.</summary>
    public static class LexiconTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C11 — dictionnaire personnel à natures");
            Plurals(t);
            Feminines(t);
            Verbs(t);
            Migration(t);
            JsonRoundTrip(t);
            Guess(t);
            Flexion(t);
            Format2(t);
            Natures(t);
            Demonyms(t);
            Adherents(t);
            InterjectionClasses(t);
        }

        /// <summary>1.0.5 : les pratiquants d'une religion ou doctrine —
        /// -isme tombe, le suffixe prend sa place, nom et adjectif, les deux
        /// genres, minuscule et majuscule ; la forme posée gagne ; un nom
        /// propre « autre » y a droit, un prénom non.</summary>
        private static void Adherents(Harness t)
        {
            t.Equal("rhétanniste", LexiconInflector.DeriveAdherent("rhétannisme", "iste"), "rhétannisme + -iste");
            t.Equal("rhétannien", LexiconInflector.DeriveAdherent("Rhétannisme", "-ien"), "majuscule et tiret du suffixe absorbés");
            t.Equal("sunnite", LexiconInflector.DeriveAdherent("sunnisme", "ite"), "sunnisme → sunnite");
            t.Equal("voduiste", LexiconInflector.DeriveAdherent("vodu", "iste"), "sans -isme : le suffixe s'ajoute");
            t.Equal("rhêtiste", LexiconInflector.DeriveAdherent("Rhêta", "iste"), "une voyelle finale tombe devant le suffixe");
            t.Equal(null, LexiconInflector.DeriveAdherent("rhétannisme", ""), "sans suffixe : rien");

            var faith = new LexiconEntry { Word = "rhétannisme", Class = LexiconEntry.ClassNoun, Genders = LexiconEntry.GendersMasculine, AdherentSuffix = "iste" };
            faith.Traits.Add("thing"); faith.Traits.Add("doctrine");
            t.Check(faith.AllowsAdherents && faith.HasAdherents, "un nom à suffixe a ses pratiquants");
            t.Equal("rhétanniste", faith.AdherentBase(), "la forme dérivée");
            t.Check(Has(faith, "rhétannisme") && Has(faith, "rhétannismes") && Has(faith, "rhétanniste") && Has(faith, "rhétannistes")
                && Has(faith, "Rhétanniste") && Has(faith, "Rhétannistes"), "le mot, son pluriel, les pratiquants aux deux casses (" + LexiconInflector.Preview(faith, 20) + ")");
            t.Equal(4, faith.AdherentForms().Count, "-iste : épicène, quatre formes (sg/pl × casse)");
            t.Check(faith.Summary().Contains("pratiquants rhétanniste"), "le résumé dit les pratiquants");

            var ien = faith.Clone(); ien.AdherentSuffix = "ien";
            t.Check(Has(ien, "rhétannien") && Has(ien, "rhétannienne") && Has(ien, "rhétanniennes") && Has(ien, "Rhétanniens"), "-ien : le féminin se dérive (" + LexiconInflector.Preview(ien, 20) + ")");

            var posed = faith.Clone(); posed.Word = "christianisme"; posed.AdherentForm = "chrétien";
            t.Check(Has(posed, "chrétien") && Has(posed, "chrétienne") && Has(posed, "Chrétiens") && !Has(posed, "christianiste"), "la forme posée remplace la règle");

            var proper = new LexiconEntry { Word = "Rhétannisme", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperOther, AdherentSuffix = "iste" };
            t.Check(proper.HasAdherents && Has(proper, "rhétanniste") && Has(proper, "Rhétannistes"), "un nom propre « autre » dérive aussi ses pratiquants");
            var firstName = new LexiconEntry { Word = "Keira", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperFirstName, AdherentSuffix = "iste" };
            t.Check(!firstName.HasAdherents && firstName.Forms().Count == 1, "un prénom n'a pas de pratiquants, même avec un suffixe oublié");

            var back = LexiconEntry.FromJson(posed.ToJson());
            t.Equal("iste", back.AdherentSuffix, "le suffixe fait l'aller-retour");
            t.Equal("chrétien", back.AdherentForm, "la forme posée aussi");
            t.Check(Has(back, "chrétiennes"), "et les formes avec");

            // Grammalecte reçoit nom et adjectif, genre et nombre.
            var triples = Correction.Grammalecte.PersonalLexicon.Triples(ien);
            var tagged = 0;
            foreach (var triple in triples) if (triple[0] == "rhétanniennes" && triple[2] == ":N:A:f:p") tagged++;
            t.Equal(1, tagged, "le triplet Grammalecte du féminin pluriel");
        }

        /// <summary>1.0.5 : Interjection et Onomatopée — deux types
        /// invariables, libellés, sans flexion ni nature, étiquetés :J pour
        /// Grammalecte ; un type inconnu d'une vieille version vaut « autre ».</summary>
        private static void InterjectionClasses(Harness t)
        {
            t.Equal(8, LexiconEntry.Classes.Length, "huit types");
            t.Equal("Interjection", LexiconEntry.ClassLabel(LexiconEntry.ClassInterjection), "libellé Interjection");
            t.Equal("Onomatopée", LexiconEntry.ClassLabel(LexiconEntry.ClassOnomatopoeia), "libellé Onomatopée");
            var kwak = new LexiconEntry { Word = "kwak", Class = LexiconEntry.ClassOnomatopoeia };
            t.Check(!kwak.HasFlexion && kwak.Forms().Count == 1 && kwak.IsComplete(), "une onomatopée : invariable, complète");
            t.Equal(0, LexiconEntry.TraitsFor(LexiconEntry.ClassInterjection).Count, "pas de nature pour une interjection");
            var back = LexiconEntry.FromJson(kwak.ToJson());
            t.Equal(LexiconEntry.ClassOnomatopoeia, back.Class, "le type fait l'aller-retour");
            var triples = Correction.Grammalecte.PersonalLexicon.Triples(new LexiconEntry { Word = "zou", Class = LexiconEntry.ClassInterjection });
            t.Check(triples.Count == 1 && triples[0][2] == ":J", "Grammalecte : une interjection");
        }

        /// <summary>Les natures changent les formes (01/10) : non comptable =
        /// pas de pluriel, nom d'habitant = aussi la majuscule, gentilé (nom
        /// propre) = aussi la minuscule ; un nom propre d'une autre sorte ne
        /// se fléchit pas.</summary>
        private static void Natures(Harness t)
        {
            var mass = new LexiconEntry { Word = "stormlight", Class = LexiconEntry.ClassNoun, Genders = LexiconEntry.GendersMasculine };
            mass.Traits.Add("thing"); mass.Traits.Add("uncountable");
            t.Equal(1, mass.Forms().Count, "entité non comptable : pas de pluriel");
            t.Equal(LexiconEntry.PluralInvariable, mass.EffectivePlural(), "le pluriel effectif est « invariable »");

            var demonymAdjective = new LexiconEntry { Word = "alethi", Class = LexiconEntry.ClassAdjective, Genders = LexiconEntry.GendersBoth };
            demonymAdjective.Traits.Add("demonym");
            t.Check(Has(demonymAdjective, "alethie") && Has(demonymAdjective, "Alethi") && Has(demonymAdjective, "Alethies"), "nom d'habitant : l'adjectif et l'habitant à majuscule");

            var demonymProper = new LexiconEntry { Word = "Alethi", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperDemonym, Genders = LexiconEntry.GendersBoth };
            t.Check(demonymProper.HasFlexion, "un gentilé se fléchit");
            t.Check(Has(demonymProper, "Alethis") && Has(demonymProper, "alethi") && Has(demonymProper, "alethies"), "gentilé : l'habitant et l'adjectif en minuscule");

            var surname = new LexiconEntry { Word = "Kholin", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperSurname, Genders = LexiconEntry.GendersBoth };
            t.Check(!surname.HasFlexion, "un nom de famille ne se fléchit pas");
            t.Equal(1, surname.Forms().Count, "nom de famille : le mot seul");
            t.Check(surname.IsComplete(), "complet sans flexion");
            var place = new LexiconEntry { Word = "Kholinar", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperPlace };
            t.Check(!place.HasFlexion && place.IsComplete(), "un lieu non plus");
        }

        /// <summary>Le gentilé dérivé (01/10) : Mànis → mànisien, mànisienne,
        /// Mànisiens… par suffixe ; la forme posée prime ; l'aller-retour JSON.</summary>
        private static void Demonyms(Harness t)
        {
            t.Equal("mànisien", LexiconInflector.DeriveDemonym("Mànis", "ien"), "Mànis + -ien");
            t.Equal("romain", LexiconInflector.DeriveDemonym("Rome", "ain"), "la voyelle finale muette tombe");
            t.Equal("nantais", LexiconInflector.DeriveDemonym("Nantes", "-ais"), "-es tombe, le tiret du suffixe aussi");
            t.Equal("mexicain", LexiconInflector.DeriveDemonym("Mexico", "ain"), "-o tombe");
            t.Equal("parisien", LexiconInflector.DeriveDemonym("Paris", "ien"), "Paris + -ien");
            t.Equal(null, LexiconInflector.DeriveDemonym("Mànis", ""), "sans suffixe : rien");

            var city = new LexiconEntry { Word = "Mànis", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperPlace, DemonymSuffix = "ien" };
            city.Traits.Add("city");
            t.Check(city.HasDemonym, "un lieu à suffixe a son gentilé");
            t.Equal("mànisien", city.DemonymBase(), "la forme dérivée");
            t.Check(Has(city, "Mànis") && Has(city, "mànisien") && Has(city, "mànisiens") && Has(city, "mànisienne") && Has(city, "mànisiennes")
                && Has(city, "Mànisien") && Has(city, "Mànisiennes"), "les huit formes du gentilé s'ajoutent au mot (" + LexiconInflector.Preview(city, 20) + ")");
            t.Check(city.Summary().Contains("gentilé mànisien"), "le résumé dit le gentilé");

            var posed = city.Clone(); posed.Word = "Bordeaux"; posed.DemonymForm = "bordelais";
            t.Check(Has(posed, "bordelaise") && Has(posed, "Bordelais") && !Has(posed, "bordeauxien"), "la forme posée remplace la règle");

            var surname = new LexiconEntry { Word = "Kholin", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperSurname, DemonymSuffix = "ien" };
            t.Check(!surname.HasDemonym && surname.Forms().Count == 1, "un nom de famille n'a pas de gentilé, même avec un suffixe oublié");

            var back = LexiconEntry.FromJson(posed.ToJson());
            t.Equal("ien", back.DemonymSuffix, "le suffixe fait l'aller-retour");
            t.Equal("bordelais", back.DemonymForm, "la forme posée aussi");
            t.Check(Has(back, "Bordelaises"), "et les formes avec");
        }

        private static bool Has(LexiconEntry entry, string form)
        {
            return entry.Forms().Contains(form);
        }

        private static void Plurals(Harness t)
        {
            var noun = new LexiconEntry { Word = "spren", Class = LexiconEntry.ClassNoun };
            t.Check(Has(noun, "spren") && Has(noun, "sprens"), "nom : le mot et son pluriel en -s");
            t.Equal(2, noun.Forms().Count, "nom sans féminin : deux formes");

            var x = new LexiconEntry { Word = "Rosharal", Class = LexiconEntry.ClassNoun, Plural = LexiconEntry.PluralX };
            t.Check(Has(x, "Rosharaux"), "pluriel en -x : -al → -aux");
            var eau = new LexiconEntry { Word = "Kholeau", Class = LexiconEntry.ClassNoun, Plural = LexiconEntry.PluralX };
            t.Check(Has(eau, "Kholeaux"), "pluriel en -x : -eau → -eaux");

            var inv = new LexiconEntry { Word = "Alethi", Class = LexiconEntry.ClassProper, Plural = LexiconEntry.PluralInvariable };
            t.Equal(1, inv.Forms().Count, "invariable : le mot seul");

            var endsS = new LexiconEntry { Word = "Urithirus", Class = LexiconEntry.ClassProper };
            t.Equal(1, endsS.Forms().Count, "finale en -s : pas de pluriel ajouté");

            var withFeminine = new LexiconEntry { Word = "Alethi", Class = LexiconEntry.ClassNoun, Feminine = "Alethie" };
            t.Check(Has(withFeminine, "Alethie") && Has(withFeminine, "Alethies"),
                "nom avec féminin explicite : féminin et son pluriel");

            var other = LexiconEntry.Simple("Kaladins");
            t.Equal(1, other.Forms().Count, "entrée « autre » : le mot tel quel, rien d'autre");
        }

        private static void Feminines(Harness t)
        {
            t.Equal("shardique", LexiconInflector.DeriveFeminine("shardique"), "-e : inchangé");
            t.Equal("vorineuse", LexiconInflector.DeriveFeminine("vorineux"), "-eux → -euse");
            t.Equal("brumive", LexiconInflector.DeriveFeminine("brumif"), "-if → -ive");
            t.Equal("kholinière", LexiconInflector.DeriveFeminine("kholinier"), "-er → -ère");
            t.Equal("parshelle", LexiconInflector.DeriveFeminine("parshel"), "-el → -elle");
            t.Equal("herdazienne", LexiconInflector.DeriveFeminine("herdazien"), "-en → -enne");
            t.Equal("thaylonne", LexiconInflector.DeriveFeminine("thaylon"), "-on → -onne");
            t.Equal("veduette", LexiconInflector.DeriveFeminine("veduet"), "-et → -ette");
            t.Equal("azishque", LexiconInflector.DeriveFeminine("azishc"), "-c → -que");
            t.Equal("iriale", LexiconInflector.DeriveFeminine("irial"), "défaut : + e");

            var adjective = new LexiconEntry { Word = "vorin", Class = LexiconEntry.ClassAdjective };
            t.Check(Has(adjective, "vorin") && Has(adjective, "vorins") && Has(adjective, "vorine") && Has(adjective, "vorines"),
                "adjectif : masculin, pluriel, féminin dérivé, féminin pluriel");
            t.Equal(4, adjective.Forms().Count, "adjectif régulier : quatre formes");

            var explicitF = new LexiconEntry { Word = "vorin", Class = LexiconEntry.ClassAdjective, Feminine = "vorinne" };
            t.Check(Has(explicitF, "vorinne") && Has(explicitF, "vorinnes") && !Has(explicitF, "vorine"),
                "féminin explicite : il remplace la règle");
            t.Check(explicitF.Summary().Contains("féminin vorinne"), "le résumé nomme le féminin");
        }

        private static void Verbs(Harness t)
        {
            var er = new LexiconEntry { Word = "sprenner", Class = LexiconEntry.ClassVerb };
            foreach (var form in new[] { "sprenne", "sprennons", "sprennaient", "sprennèrent", "sprennerai",
                "sprenneriez", "sprennassions", "sprennant", "sprennée", "sprennés" })
                t.Check(Has(er, form), "1er groupe : " + form);
            // 39 formes DISTINCTES : présent et subjonctif partagent -e/-es/-ent.
            t.Check(er.Forms().Count >= 36, "1er groupe : la table complète (" + er.Forms().Count + " formes)");

            var cer = new LexiconEntry { Word = "vorincer", Class = LexiconEntry.ClassVerb };
            t.Check(Has(cer, "vorinçons") && Has(cer, "vorinçait") && Has(cer, "vorincions"),
                "-cer : cédille devant a/o, pas devant i");
            var ger = new LexiconEntry { Word = "shardager", Class = LexiconEntry.ClassVerb };
            t.Check(Has(ger, "shardageons") && Has(ger, "shardageait") && Has(ger, "shardagions"),
                "-ger : e devant a/o, pas devant i");

            var ir = new LexiconEntry { Word = "sprenir", Class = LexiconEntry.ClassVerb };
            foreach (var form in new[] { "sprenis", "sprenissons", "sprenissaient", "sprenirent", "sprenirai",
                "sprenissions", "sprenît", "sprenissant", "sprenie", "sprenis" })
                t.Check(Has(ir, form), "2e groupe : " + form);

            var irregular = new LexiconEntry { Word = "sprendre", Class = LexiconEntry.ClassVerb };
            t.Equal(1, irregular.Forms().Count, "hors tables : l'infinitif seul");
            t.Equal(0, LexiconInflector.VerbGroup("aller"), "« aller » n'est pas du 1er groupe");
            t.Equal(0, LexiconInflector.VerbGroup("pouvoir"), "-oir : hors tables");
        }

        private static void Migration(Harness t)
        {
            var entries = new List<LexiconEntry> { LexiconEntry.Simple("Kaladin") };
            LexiconEntry.MergeWords(entries, new[] { "Kaladin", "Syl", "", null, "Syl" });
            t.Equal(2, entries.Count, "les mots nus rejoignent sans doublon ni vide");
            t.Equal(LexiconEntry.ClassOther, entries[1].Class, "un mot migré est « autre »");
            t.Check(entries[1].NeedsReview, "…et porte la pastille « migration nécessaire » (30/09)");
            var mixed = LexiconEntry.FromJsonList(new List<object> { "Dalinar", new Dictionary<string, object> { { "word", "Navani" }, { "class", "proper" } } });
            t.Equal(2, mixed.Count, "une liste mêlant chaînes et objets se lit");
            t.Equal(LexiconEntry.ClassProper, mixed[1].Class, "l'objet garde sa nature");
        }

        private static void JsonRoundTrip(Harness t)
        {
            var entry = new LexiconEntry
            {
                Word = "Kaladin", Class = LexiconEntry.ClassProper, Gender = "m",
                Plural = LexiconEntry.PluralInvariable, Feminine = "", Note = "chef de pont",
                Definition = "Soldat déchu devenu porteur d'éclats."
            };
            var json = Json.Write(new Dictionary<string, object> { { "list", LexiconEntry.ToJsonList(new List<LexiconEntry> { entry }) } });
            var back = LexiconEntry.FromJsonList(Json.AsList(Json.Field(Json.AsObject(Json.Parse(json)), "list")));
            t.Equal(1, back.Count, "une entrée relue");
            t.Equal("Kaladin", back[0].Word, "mot");
            t.Equal(LexiconEntry.ClassProper, back[0].Class, "nature");
            t.Equal("m", back[0].Gender, "genre");
            t.Equal(LexiconEntry.PluralInvariable, back[0].Plural, "pluriel");
            t.Equal("chef de pont", back[0].Note, "note");
            t.Equal("Soldat déchu devenu porteur d'éclats.", back[0].Definition, "définition");
            t.Equal("Soldat déchu devenu porteur d'éclats.", back[0].Clone().Definition, "définition clonée");
            var bogus = LexiconEntry.FromJson(new Dictionary<string, object> { { "word", "x" }, { "class", "pronoun" } });
            t.Equal(LexiconEntry.ClassOther, bogus.Class, "une nature inconnue retombe sur « autre »");
            t.Check(LexiconEntry.FromJson(new Dictionary<string, object> { { "class", "noun" } }) == null,
                "sans mot : pas d'entrée");
        }

        /// <summary>La flexion à quatre formes (30/09) : masculin et féminin,
        /// masculin, féminin ; les formes posées priment sur la règle.</summary>
        private static void Flexion(Harness t)
        {
            var feminine = new LexiconEntry { Word = "licorne", Class = LexiconEntry.ClassNoun, Genders = LexiconEntry.GendersFeminine };
            t.Check(Has(feminine, "licorne") && Has(feminine, "licornes") && feminine.Forms().Count == 2, "nom féminin : le mot et son pluriel, pas de masculin");
            var both = new LexiconEntry { Word = "spren", Class = LexiconEntry.ClassNoun, Genders = LexiconEntry.GendersBoth, FemSg = "sprenne" };
            t.Check(Has(both, "spren") && Has(both, "sprens") && Has(both, "sprenne") && Has(both, "sprennes"), "nom des deux genres : quatre formes, le féminin posé");
            var posedPlural = new LexiconEntry { Word = "vorinal", Class = LexiconEntry.ClassAdjective, Genders = LexiconEntry.GendersBoth, MascPl = "vorinaux" };
            t.Check(Has(posedPlural, "vorinaux") && !Has(posedPlural, "vorinals") && Has(posedPlural, "vorinale"), "une forme posée remplace la règle, les autres restent dérivées");
            var derived = both.Clone(); derived.FemSg = "";
            var forms = derived.DerivedForms();
            t.Check(forms[0] == "spren" && forms[1] == "sprens" && forms[2] == "sprenne" && forms[3] == "sprennes", "les quatre formes dérivées, en filigrane");
            var place = new LexiconEntry { Word = "Kholinar", Class = LexiconEntry.ClassProper, Genders = LexiconEntry.GendersMasculine, Plural = LexiconEntry.PluralInvariable, ProperKind = LexiconEntry.ProperPlace };
            place.Traits.Add("city");
            t.Equal(1, place.Forms().Count, "nom propre invariable : le mot seul");
            t.Check(place.IsComplete(), "type, flexion et sorte : complet");
            t.Check(!new LexiconEntry { Word = "x", Class = LexiconEntry.ClassNoun }.IsComplete(), "un nom sans flexion n'est pas complet");
            t.Check(!new LexiconEntry { Word = "X", Class = LexiconEntry.ClassProper, Genders = LexiconEntry.GendersMasculine }.IsComplete(), "un nom propre sans sorte n'est pas complet");
            var role = new LexiconEntry { Word = "radiant", Class = LexiconEntry.ClassNoun, Genders = LexiconEntry.GendersBoth };
            role.Traits.Add("person"); role.Traits.Add("role");
            t.Check(role.Summary().Contains("personne, fonction sociale") && role.Summary().Contains("masculin et féminin"), "le résumé dit la nature et la flexion (" + role.Summary() + ")");
            t.Check(place.Summary().Contains("lieu, ville"), "le résumé d'un nom propre dit sa sorte et ses cases");
        }

        /// <summary>Le format 2 (30/09) : l'ancien format se convertit à la
        /// lecture et porte la pastille ; le nouveau fait l'aller-retour.</summary>
        private static void Format2(Harness t)
        {
            var legacy = LexiconEntry.FromJson(new Dictionary<string, object> { { "word", "dragon" }, { "class", "noun" }, { "gender", "m" }, { "feminine", "dragonne" } });
            t.Check(legacy.NeedsReview, "une entrée sans « v » : migration nécessaire");
            t.Equal(LexiconEntry.GendersBoth, legacy.Genders, "un féminin explicite : les deux genres");
            t.Equal("dragonne", legacy.FemSg, "le féminin migre dans fém. sg.");
            t.Check(Has(legacy, "dragonnes") && Has(legacy, "dragons"), "les formes d'avant restent acceptées");
            var verb = LexiconEntry.FromJson(new Dictionary<string, object> { { "word", "sprenner" }, { "class", "verb" } });
            t.Check(!verb.NeedsReview, "un verbe n'a rien à revoir");
            var masculine = LexiconEntry.FromJson(new Dictionary<string, object> { { "word", "Kaladin" }, { "class", "proper" }, { "gender", "m" } });
            t.Equal(LexiconEntry.GendersMasculine, masculine.Genders, "le genre d'avant devient la flexion");
            var entry = new LexiconEntry { Word = "Kholinar", Class = LexiconEntry.ClassProper, Genders = LexiconEntry.GendersMasculine, Plural = LexiconEntry.PluralInvariable, ProperKind = LexiconEntry.ProperPlace, MascPl = "Kholinars" };
            entry.Traits.Add("city"); entry.Traits.Add("world");
            var back = LexiconEntry.FromJson(entry.ToJson());
            t.Check(!back.NeedsReview, "le format 2 relu n'a rien à revoir");
            t.Equal(LexiconEntry.ProperPlace, back.ProperKind, "la sorte fait l'aller-retour");
            t.Check(back.HasTrait("city") && back.HasTrait("world") && back.Traits.Count == 2, "les cases aussi");
            t.Equal("Kholinars", back.MascPl, "une forme posée aussi");
            t.Equal("m", back.Gender, "le genre d'avant est écrit en miroir (une version d'avant relit l'entrée)");
            var flagged = entry.Clone(); flagged.NeedsReview = true;
            t.Check(LexiconEntry.FromJson(flagged.ToJson()).NeedsReview, "la pastille se persiste");
            t.Equal(1, LexiconEntry.CountNeedingReview(new List<LexiconEntry> { flagged, entry }), "le compte des entrées à revoir");
            var bogusTrait = LexiconEntry.FromJson(new Dictionary<string, object> { { "word", "x" }, { "class", "noun" }, { "v", 2.0 }, { "traits", new List<object> { "person", "licorne" } } });
            t.Check(bogusTrait.HasTrait("person") && bogusTrait.Traits.Count == 2 && bogusTrait.Summary().Contains("personne") && !bogusTrait.Summary().Contains("licorne"), "une case inconnue est gardée (une version plus récente la connaît) mais ne se dit pas");
        }

        private static void Guess(Harness t)
        {
            t.Equal(LexiconEntry.ClassProper, LexiconInflector.GuessClass("Kaladin"), "majuscule → nom propre");
            t.Equal(LexiconEntry.ClassVerb, LexiconInflector.GuessClass("sprenner"), "-er → verbe");
            t.Equal(LexiconEntry.ClassNoun, LexiconInflector.GuessClass("spren"), "sinon → nom");
            t.Equal(LexiconEntry.ClassOther, LexiconInflector.GuessClass(""), "vide → autre");
        }
    }
}

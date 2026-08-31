using System;
using System.Collections.Generic;
using UniversSale.Model;

namespace UniversSale.Tests
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

        private static void Guess(Harness t)
        {
            t.Equal(LexiconEntry.ClassProper, LexiconInflector.GuessClass("Kaladin"), "majuscule → nom propre");
            t.Equal(LexiconEntry.ClassVerb, LexiconInflector.GuessClass("sprenner"), "-er → verbe");
            t.Equal(LexiconEntry.ClassNoun, LexiconInflector.GuessClass("spren"), "sinon → nom");
            t.Equal(LexiconEntry.ClassOther, LexiconInflector.GuessClass(""), "vide → autre");
        }
    }
}

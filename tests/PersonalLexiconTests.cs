using System.Collections.Generic;
using System.IO;
using System.Threading;
using Marabook.Correction.Grammalecte;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C46 — le dictionnaire personnel vers Grammalecte (1.0.3) :
    /// un prénom se genre (masculin, féminin, neutre par défaut), les entrées
    /// deviennent des triplets étiquetés que le pont charge, et Grammalecte
    /// relève alors « Shallan est parti ». La partie en direct est sautée
    /// sans python/ ou grammalecte/, jamais rouge pour une absence d'outil.</summary>
    public static class PersonalLexiconTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C46 — dictionnaire personnel vers Grammalecte, prénoms genrés (1.0.3)");
            FirstNames(t);
            Triples(t);
            Live(t);
        }

        private static void FirstNames(Harness t)
        {
            var neutral = new LexiconEntry { Word = "Dom", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperFirstName };
            t.Equal("", neutral.FirstNameGender(), "un prénom sans genre posé est neutre");
            t.Check(neutral.Summary().Contains("neutre"), "…et le résumé le dit (" + neutral.Summary() + ")");
            t.Check(neutral.IsComplete(), "un prénom neutre est complet");

            var feminine = new LexiconEntry { Word = "Shallan", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperFirstName, Genders = LexiconEntry.GendersFeminine };
            t.Equal("f", feminine.FirstNameGender(), "un prénom féminin");
            t.Check(feminine.Summary().Contains("féminin"), "…résumé « féminin » (" + feminine.Summary() + ")");
            var back = LexiconEntry.FromJson(feminine.ToJson());
            t.Equal("f", back.FirstNameGender(), "le genre du prénom fait l'aller-retour JSON");
            t.Equal("f", Marabook.Json.AsString(Marabook.Json.Field(feminine.ToJson(), "gender")), "…et l'ancien miroir « gender » le porte aussi");

            var surname = new LexiconEntry { Word = "Kholin", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperSurname, Genders = LexiconEntry.GendersMasculine };
            t.Equal("", surname.FirstNameGender(), "un nom de famille n'a pas de genre de prénom");
            var both = new LexiconEntry { Word = "Camille", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperFirstName, Genders = LexiconEntry.GendersBoth };
            t.Equal("", both.FirstNameGender(), "« mf » sur un prénom vaut neutre");
        }

        private static void Triples(Harness t)
        {
            var m = new LexiconEntry { Word = "Kaladin", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperFirstName, Genders = LexiconEntry.GendersMasculine };
            var f = new LexiconEntry { Word = "Shallan", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperFirstName, Genders = LexiconEntry.GendersFeminine };
            var e = new LexiconEntry { Word = "Dom", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperFirstName };
            t.Equal("Kaladin Kaladin :M1:m:i\n", PersonalLexicon.Describe(PersonalLexicon.Triples(m)), "prénom masculin → :M1:m:i");
            t.Equal("Shallan Shallan :M1:f:i\n", PersonalLexicon.Describe(PersonalLexicon.Triples(f)), "prénom féminin → :M1:f:i");
            t.Equal("Dom Dom :M1:e:i\n", PersonalLexicon.Describe(PersonalLexicon.Triples(e)), "prénom neutre → :M1:e:i (épicène)");

            var surname = new LexiconEntry { Word = "Kholin", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperSurname };
            t.Equal("Kholin Kholin :M2:e:i\n", PersonalLexicon.Describe(PersonalLexicon.Triples(surname)), "nom de famille → :M2:e:i");
            var place = new LexiconEntry { Word = "Kholinar", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperPlace };
            t.Equal("Kholinar Kholinar :MP:e:i\n", PersonalLexicon.Describe(PersonalLexicon.Triples(place)), "lieu → :MP:e:i");

            var noun = new LexiconEntry { Word = "shardique", Class = LexiconEntry.ClassNoun, Genders = LexiconEntry.GendersBoth };
            t.Equal("shardique shardique :N:m:s\nshardiques shardique :N:m:p\nshardique shardique :N:f:s\nshardiques shardique :N:f:p\n",
                PersonalLexicon.Describe(PersonalLexicon.Triples(noun)), "nom aux deux genres : quatre formes étiquetées");
            var feminineNoun = new LexiconEntry { Word = "sprène", Class = LexiconEntry.ClassNoun, Genders = LexiconEntry.GendersFeminine };
            t.Equal("sprène sprène :N:f:s\nsprènes sprène :N:f:p\n", PersonalLexicon.Describe(PersonalLexicon.Triples(feminineNoun)), "nom féminin : deux formes");
            var unknown = new LexiconEntry { Word = "truc", Class = LexiconEntry.ClassNoun };
            t.Check(PersonalLexicon.Describe(PersonalLexicon.Triples(unknown)).Contains(":N:e:s"), "nom sans genre connu : épicène, jamais une faute inventée");
            var adjective = new LexiconEntry { Word = "alethi", Class = LexiconEntry.ClassAdjective, Genders = LexiconEntry.GendersBoth };
            t.Equal("alethi alethi :A:m:s\nalethis alethi :A:m:p\nalethie alethi :A:f:s\nalethies alethi :A:f:p\n",
                PersonalLexicon.Describe(PersonalLexicon.Triples(adjective)), "adjectif : quatre formes");
            var adverb = new LexiconEntry { Word = "shardiquement", Class = LexiconEntry.ClassAdverb };
            t.Equal("shardiquement shardiquement :W\n", PersonalLexicon.Describe(PersonalLexicon.Triples(adverb)), "adverbe → :W");
            var verb = new LexiconEntry { Word = "sprénifier", Class = LexiconEntry.ClassVerb };
            t.Equal(0, PersonalLexicon.Triples(verb).Count, "un verbe n'est pas transmis");
            var spaced = new LexiconEntry { Word = "Vaux le Vicomte", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperPlace };
            t.Equal(0, PersonalLexicon.Triples(spaced).Count, "un mot à espaces non plus");

            var built = PersonalLexicon.Build(new List<LexiconEntry> { m, noun }, new List<LexiconEntry> { m, f });
            t.Equal(1 + 4 + 1, built.Count, "projet + global, sans doublon");
            var hash1 = PersonalLexicon.Hash(built);
            t.Equal(hash1, PersonalLexicon.Hash(PersonalLexicon.Build(new List<LexiconEntry> { m, noun }, new List<LexiconEntry> { m, f })), "l'empreinte est stable");
            t.Check(hash1 != PersonalLexicon.Hash(PersonalLexicon.Build(new List<LexiconEntry> { m, noun }, new List<LexiconEntry> { m, e })), "…et change avec le genre");
            t.Check(PersonalLexicon.Hash(new List<string[]>()) != hash1, "vide ≠ rempli");
        }

        /// <summary>De bout en bout par le vrai pont : avec Shallan au
        /// féminin, « Shallan est parti » est relevé ; le dictionnaire retiré,
        /// plus rien.</summary>
        private static void Live(Harness t)
        {
            if (!File.Exists(GrammalecteBridge.PythonPath) || !File.Exists(GrammalecteBridge.ScriptPath))
            {
                t.Info("pont Grammalecte absent : vérification en direct sautée");
                return;
            }
            var bridge = new GrammalecteBridge();
            try
            {
                Expect(t, bridge, "Shallan est parti.", "sans dictionnaire : Shallan est inconnue, rien n'est relevé");
                var f = new LexiconEntry { Word = "Shallan", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperFirstName, Genders = LexiconEntry.GendersFeminine };
                var m = new LexiconEntry { Word = "Kaladin", Class = LexiconEntry.ClassProper, ProperKind = LexiconEntry.ProperFirstName, Genders = LexiconEntry.GendersMasculine };
                var noun = new LexiconEntry { Word = "shardique", Class = LexiconEntry.ClassNoun, Genders = LexiconEntry.GendersBoth };
                bridge.SetLexicon(PersonalLexicon.Build(new List<LexiconEntry> { f, m, noun }, null));
                Expect(t, bridge, "Shallan est parti.", "prénom féminin : le participe au masculin est relevé", "parti");
                Expect(t, bridge, "Shallan est partie.", "…et l'accord juste passe");
                Expect(t, bridge, "Kaladin est contente.", "prénom masculin : « contente » est relevé", "contente");
                Expect(t, bridge, "Le shardique est contente.", "nom inventé genré : l'accord est vérifié", "contente");
                bridge.SetLexicon(new List<string[]>());
                Expect(t, bridge, "Shallan est parti.", "dictionnaire retiré : plus rien");
            }
            finally
            {
                bridge.Dispose();
            }
        }

        private static void Expect(Harness t, GrammalecteBridge bridge, string text, string label, params string[] expected)
        {
            var task = bridge.CheckAsync(text, GrammalecteOptions.Effective(null, true, false), CancellationToken.None);
            if (!task.Wait(60000))
            {
                t.Check(false, label + " : pas de réponse du pont");
                return;
            }
            var words = new List<string>();
            foreach (var error in task.Result)
                if (error.Start >= 0 && error.End <= text.Length && error.End > error.Start)
                    words.Add(text.Substring(error.Start, error.End - error.Start));
            t.Equal(string.Join("|", expected), string.Join("|", words.ToArray()), label);
        }
    }
}

using System.Collections.Generic;
using Marabook.Print;

namespace Marabook.Tests
{
    /// <summary>C2 — score de césure française sur le corpus. PAS un
    /// pass/fail par mot : un score global (coupures justes − coupures
    /// fautives), affiché à chaque exécution, verrouillé par un PLANCHER.
    /// Améliorer la césure (motifs Liang, dictionnaire d'exceptions — au
    /// backlog) fera monter le score : relever alors le plancher.</summary>
    public static class HyphenCorpusTests
    {
        // Score mesuré à la création du harnais (batch 24) : 446 coupures
        // justes, 25 fautives, 22 manquées → 421. Toute régression sous ce
        // plancher échoue ; toute amélioration doit le relever.
        public const int ScoreFloor = 421;

        public static void Run(Harness t)
        {
            t.Suite("C2 — corpus de césure (" + HyphenCorpus.Words.Length + " mots)");
            int correct = 0, faulty = 0, missed = 0, unbrokenKept = 0, unbrokenTotal = 0;

            foreach (var entry in HyphenCorpus.Words)
            {
                var word = entry.Replace("-", "");
                var expected = new HashSet<int>();
                var cursor = 0;
                foreach (var part in entry.Split('-'))
                {
                    cursor += part.Length;
                    if (cursor < word.Length) expected.Add(cursor);
                }
                var produced = FrenchHyphenator.BreakPoints(word, 5, 2, 3);
                if (expected.Count == 0)
                {
                    unbrokenTotal++;
                    if (produced.Count == 0) unbrokenKept++;
                    else faulty += produced.Count;
                    continue;
                }
                foreach (var cut in produced)
                {
                    if (expected.Contains(cut)) correct++;
                    else faulty++;
                }
                foreach (var cut in expected)
                    if (!produced.Contains(cut)) missed++;
            }

            var score = correct - faulty;
            t.Info("coupures justes : " + correct + " · fautives : " + faulty
                + " · manquées : " + missed);
            t.Info("mots incoupables respectés : " + unbrokenKept + "/" + unbrokenTotal);
            t.Info("SCORE : " + score + " (plancher : " + ScoreFloor + ")");
            t.Check(score >= ScoreFloor,
                "le score de césure ne régresse pas (obtenu " + score
                + ", plancher " + ScoreFloor + ")");

            // Les minima du style sont respectés quoi qu'il arrive : jamais
            // moins de 2 lettres avant la coupure ni 3 après (A7).
            var clean = true;
            foreach (var entry in HyphenCorpus.Words)
            {
                var word = entry.Replace("-", "");
                foreach (var cut in FrenchHyphenator.BreakPoints(word, 5, 2, 3))
                    if (cut < 2 || word.Length - cut < 3) clean = false;
            }
            t.Check(clean, "minima 2/3 respectés sur tout le corpus");

            // Élisions et mots composés (09/10) : « d’incompréhension » en bout
            // de ligne laissait un trou immense (Rémi, chapitre 1 d'Enerya).
            var elided = FrenchHyphenator.BreakPoints("d’incompréhension", 5, 2, 3);
            t.Check(elided.Count > 0 && elided[0] >= 4, "« d’incompréhension » se coupe derrière l'apostrophe (" + string.Join(",", elided.ConvertAll(c => c.ToString()).ToArray()) + ")");
            t.Check(!elided.Contains(2), "…jamais juste après l'apostrophe");
            t.Check(FrenchHyphenator.BreakPoints("l'entourait", 5, 2, 3).Count > 0, "l'apostrophe droite aussi");
            t.Equal(0, FrenchHyphenator.BreakPoints("qu’elle", 5, 2, 3).Count, "« qu’elle » : trop court derrière l'apostrophe");
            t.Equal(0, FrenchHyphenator.BreakPoints("aujourd'hui", 5, 2, 3).Count, "« aujourd'hui » reste soudé");
            t.Equal(0, FrenchHyphenator.BreakPoints("l’Ouest", 5, 2, 3).Count, "« l’Ouest » : la majuscule derrière garde ses règles (court)");
            var compound = FrenchHyphenator.BreakPoints("peut-être", 5, 2, 3);
            t.Check(compound.Count == 1 && compound[0] == -5, "« peut-être » : une coupe après le trait, sans en ajouter (" + string.Join(",", compound.ConvertAll(c => c.ToString()).ToArray()) + ")");
            var rainbow = FrenchHyphenator.BreakPoints("arc-en-ciel", 5, 2, 3);
            t.Check(rainbow.Count == 2 && rainbow[0] == -4 && rainbow[1] == -7, "« arc-en-ciel » : après chaque trait");
        }
    }
}

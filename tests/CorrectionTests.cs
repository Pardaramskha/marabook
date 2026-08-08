using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UniversSale.Correction;
using UniversSale.Model;

namespace UniversSale.Tests
{
    /// <summary>C5 — la chaîne de correction, entièrement en console (aucune
    /// dépendance WPF : c'est la condition de conception d'IChecker). Le
    /// détecteur de répétitions sur des documents construits à la main avec
    /// les positions calculées à la main ; le respect de NoProof ; les
    /// ignorés (ici / projet / global) ; l'agrégation multi-vérificateurs ;
    /// la doctrine des plages après édition (recalcul, pas de survie) ; et
    /// la mesure sur 50 000 mots.</summary>
    public static class CorrectionTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C5 — chaîne de correction");
            BasicRepetition(t);
            RadiusBounds(t);
            CaseAccentAndElision(t);
            StopWordsAndShortWords(t);
            NoProofRespected(t);
            IgnoreLists(t);
            IgnoreHereIsPositional(t);
            AggregationAndOrder(t);
            TotalOrder(t);
            ParagraphCache(t);
            FiftyThousandWords(t);
        }

        /// <summary>Lot B (batch 27) — un vérificateur LOCAL n'est relancé
        /// que sur les paragraphes dont l'empreinte a changé ; ses
        /// signalements gardent des positions justes au réemploi.</summary>
        private sealed class CountingLocalChecker : IChecker
        {
            public int Calls;
            public string Id { get { return "local-jouet"; } }
            public string Label { get { return "Local-jouet"; } }
            public FindingCategory Category { get { return FindingCategory.Spelling; } }
            public CheckerScope Scope { get { return CheckerScope.ParagraphLocal; } }

            public List<Finding> Check(TextDocument document, StyleSheet styles)
            {
                throw new NotSupportedException();
            }

            public List<Finding> CheckParagraph(TextParagraph paragraph, StyleSheet styles)
            {
                Calls++;
                var findings = new List<Finding>();
                var text = PivotEdit.FlatText(paragraph);
                var index = text.IndexOf("faute", StringComparison.Ordinal);
                if (index >= 0)
                    findings.Add(new Finding
                    {
                        Start = index,
                        Length = 5,
                        Category = FindingCategory.Spelling,
                        Message = "faute-jouet",
                        RuleId = "faute",
                        CheckerId = Id,
                        Word = "faute"
                    });
                return findings;
            }
        }

        private static void ParagraphCache(Harness t)
        {
            var document = Document("Un début sans rien.",
                "Une faute au milieu.", "Une fin sans rien.");
            var checker = new CountingLocalChecker();
            var host = Host(checker);

            var first = host.Run(document, null);
            t.Equal(3, checker.Calls, "première passe : les trois paragraphes");
            t.Equal(1, first.Count, "la faute-jouet est signalée");
            t.Equal(1, first[0].ParagraphIndex, "au bon paragraphe");

            host.Run(document, null);
            t.Equal(3, checker.Calls, "rien n'a changé : ZÉRO revérification");

            PivotEdit.InsertText(document.Paragraphs[2], 0, "Or ");
            host.Run(document, null);
            t.Equal(4, checker.Calls,
                "une frappe au paragraphe 2 : LUI SEUL est revérifié");

            // Un paragraphe inséré en tête décale les index : les positions
            // restent justes (ParagraphIndex reposé au réemploi).
            document.Paragraphs.Insert(0, new TextParagraph());
            var shifted = host.Run(document, null);
            t.Equal(1, shifted.Count, "le signalement survit au décalage");
            t.Equal(2, shifted[0].ParagraphIndex,
                "son index suit le paragraphe déplacé");
        }

        // ------------------------------------------------------------ fixtures

        private static TextDocument Document(params string[] paragraphs)
        {
            var document = new TextDocument();
            foreach (var text in paragraphs)
            {
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun { Text = text });
                document.Paragraphs.Add(paragraph);
            }
            return document;
        }

        private static CheckerHost Host(params IChecker[] checkers)
        {
            var host = new CheckerHost();
            foreach (var checker in checkers) host.Add(checker);
            return host;
        }

        // ------------------------------------------------------------ cas de base

        private static void BasicRepetition(Harness t)
        {
            // « marabout » : mots comptés marabout(1) mange(2) marabout(3)
            // → distance 2, seule la SECONDE occurrence est signalée.
            var document = Document("Le marabout mange.", "Le marabout dort.");
            var findings = Host(new RepetitionChecker()).Run(document, null);
            t.Equal(1, findings.Count, "une répétition signalée, pas deux");
            t.Equal(1, findings[0].ParagraphIndex, "sur la seconde occurrence");
            t.Equal(3, findings[0].Start, "position calculée à la main (« Le m… »)");
            t.Equal(8, findings[0].Length, "la longueur du mot");
            t.Equal("marabout", findings[0].Word, "le mot tel qu'affiché");
            t.Check(findings[0].Message.Contains("2 mots plus haut"),
                "le message porte la distance (" + findings[0].Message + ")");
            t.Equal(FindingCategory.Style, findings[0].Category, "catégorie style");
            t.Equal("repetition", findings[0].RuleId, "règle stable");
        }

        private static void RadiusBounds(Harness t)
        {
            // Rayon 3 : quatre mots pleins entre les deux → hors rayon.
            var checker = new RepetitionChecker { Radius = 3 };
            var far = Document("Marabout plume bec aile griffe marabout.");
            t.Equal(0, Host(checker).Run(far, null).Count,
                "hors rayon : silence");
            var near = Document("Marabout plume bec marabout.");
            t.Equal(1, Host(checker).Run(near, null).Count,
                "dans le rayon : signalé");
        }

        private static void CaseAccentAndElision(Harness t)
        {
            var document = Document("Le cœur bat.", "Ce COEUR ment.");
            var findings = Host(new RepetitionChecker()).Run(document, null);
            t.Equal(1, findings.Count, "cœur = COEUR (casse et accents pliés)");
            t.Equal("COEUR", findings[0].Word, "l'affichage garde la casse d'origine");

            var elision = Document("L'homme marche.", "Un homme parle.");
            var strip = Host(new RepetitionChecker()).Run(elision, null);
            t.Equal(1, strip.Count, "l'homme = homme (élision dépouillée)");

            var whole = Document("Aujourd'hui il pleut.", "Aujourd'hui il vente.");
            var kept = Host(new RepetitionChecker()).Run(whole, null);
            t.Equal(1, kept.Count, "aujourd'hui reste un seul mot");
            t.Equal("Aujourd'hui", kept[0].Word, "affiché entier");
        }

        private static void StopWordsAndShortWords(Harness t)
        {
            var stop = Document("Il marche dans la nuit dans le froid.");
            t.Equal(0, Host(new RepetitionChecker()).Run(stop, null).Count,
                "« dans » répété : mot-outil, jamais signalé");
            var conjugated = Document("Elle était partie. Il était tard.");
            t.Equal(0, Host(new RepetitionChecker()).Run(conjugated, null).Count,
                "« était » : auxiliaire conjugué, jamais signalé");
            var shortWord = Document("Ah, la pluie. Ah, la boue.");
            t.Equal(0, Host(new RepetitionChecker()).Run(shortWord, null).Count,
                "« ah » : sous la longueur minimale (3 lettres), silence");
            var threeLetters = Document("Un cri. Un cri encore.");
            t.Equal(1, Host(new RepetitionChecker()).Run(threeLetters, null).Count,
                "« cri » : trois lettres, signalé — la répétition compte");
        }

        // ------------------------------------------------------------ filtres

        private static void NoProofRespected(Harness t)
        {
            // La seconde occurrence vit dans un run « ne pas corriger ».
            var document = new TextDocument();
            var first = new TextParagraph();
            first.Runs.Add(new TextRun { Text = "Le wisteria fleurit." });
            document.Paragraphs.Add(first);
            var second = new TextParagraph();
            second.Runs.Add(new TextRun { Text = "Un " });
            second.Runs.Add(new TextRun { Text = "wisteria", NoProof = true });
            second.Runs.Add(new TextRun { Text = " grimpe." });
            document.Paragraphs.Add(second);

            var findings = Host(new RepetitionChecker()).Run(document, null);
            t.Equal(0, findings.Count, "une plage NoProof n'est jamais signalée");

            // Le même document SANS la marque est bien signalé (contre-épreuve).
            second.Runs[1].NoProof = false;
            t.Equal(1, Host(new RepetitionChecker()).Run(document, null).Count,
                "contre-épreuve : sans NoProof, le signalement revient");
        }

        private static void IgnoreLists(Harness t)
        {
            var document = Document("Le marabout mange.", "Le marabout dort.");
            var host = Host(new RepetitionChecker());
            host.IgnoreInProject("MARABOUT"); // insensible à la casse
            t.Equal(0, host.Run(document, null).Count,
                "ignoré dans le projet : silence, casse indifférente");

            var global = Host(new RepetitionChecker());
            global.GlobalIgnored.Add("marabout");
            t.Equal(0, global.Run(document, null).Count,
                "ignoré partout (réglages) : silence");
        }

        private static void IgnoreHereIsPositional(Harness t)
        {
            // Doctrine des plages : un signalement est RECALCULÉ après chaque
            // édition, jamais suivi. « Ignorer ici » est donc positionnel et
            // de session : si le texte bouge AVANT la plage, les offsets
            // changent et le signalement réapparaît. Tranché, documenté, testé.
            var document = Document("Le marabout mange.", "Le marabout dort.");
            var host = Host(new RepetitionChecker());
            var first = host.Run(document, null)[0];
            host.IgnoreHere(first);
            t.Equal(0, host.Run(document, null).Count,
                "ignoré ici : le signalement précis se tait");

            PivotEdit.InsertText(document.Paragraphs[1], 0, "Or ");
            var after = host.Run(document, null);
            t.Equal(1, after.Count,
                "le texte a bougé avant la plage : recalculé, il réapparaît");
            t.Equal(first.Start + 3, after[0].Start,
                "aux nouveaux offsets (l'ancien « ici » ne colle plus)");
        }

        // ------------------------------------------------------------ pilote

        /// <summary>Un vérificateur-jouet pour l'agrégation : signale chaque
        /// « ! » comme typographie (démonstration multi-catégories).</summary>
        private sealed class BangChecker : IChecker
        {
            public string Id { get { return "bang"; } }
            public string Label { get { return "Points d'exclamation"; } }
            public FindingCategory Category { get { return FindingCategory.Typography; } }
            public CheckerScope Scope { get { return CheckerScope.WholeDocument; } }

            public List<Finding> CheckParagraph(TextParagraph paragraph, StyleSheet styles)
            {
                throw new NotSupportedException();
            }

            public List<Finding> Check(TextDocument document, StyleSheet styles)
            {
                var findings = new List<Finding>();
                for (var p = document.Paragraphs.Count - 1; p >= 0; p--)
                {
                    var text = PivotEdit.FlatText(document.Paragraphs[p]);
                    for (var i = text.Length - 1; i >= 0; i--)
                        if (text[i] == '!')
                            findings.Add(new Finding
                            {
                                ParagraphIndex = p,
                                Start = i,
                                Length = 1,
                                Category = FindingCategory.Typography,
                                Message = "Un point d'exclamation",
                                RuleId = "bang",
                                CheckerId = "bang",
                                Word = "!"
                            });
                }
                return findings; // volontairement à REBOURS : le pilote trie
            }
        }

        private static void AggregationAndOrder(Harness t)
        {
            var document = Document("Oh ! Le marabout crie !", "Le marabout dort.");
            var host = Host(new RepetitionChecker(), new BangChecker());
            var findings = host.Run(document, null);
            t.Equal(3, findings.Count, "deux vérificateurs agrégés (2 ! + 1 répétition)");
            var ordered = true;
            for (var i = 1; i < findings.Count; i++)
            {
                var before = findings[i - 1];
                var after = findings[i];
                if (before.ParagraphIndex > after.ParagraphIndex
                    || (before.ParagraphIndex == after.ParagraphIndex
                        && before.Start > after.Start)) ordered = false;
            }
            t.Check(ordered, "tri global par (paragraphe, position)");
            t.Equal(FindingCategory.Typography, findings[0].Category,
                "le premier signalement est le « ! » de tête");
        }

        // ------------------------------------------------------------ mesure

        /// <summary>Le chapitre de mesure : 50 000 mots. L'ancien générateur
        /// (« seed * 31 % 97 + 1 ») avait une PÉRIODE DE 48 — il mesurait une
        /// boucle de 48 mots répétée mille fois (0.5, batch 27). Celui-ci :
        /// LCG pleine période + vocabulaire de 2 000 formes distinctes tiré
        /// en loi zipfienne (une tête fréquente, une longue queue) — la
        /// physionomie d'un vrai texte.</summary>
        public static TextDocument FiftyThousandWordChapter()
        {
            var onsets = new[]
            {
                "mar", "bel", "cor", "dun", "fer", "gal", "hor", "jal",
                "lum", "nov", "pel", "quar", "ros", "sab", "tor", "vel",
                "arg", "bru", "cha", "dor", "fla", "gri", "mon", "pra",
                "sil", "tan", "ver", "bla", "cre", "dri", "fon", "gue",
                "lan", "mor", "nue", "pil", "rou", "sen", "tul", "vau"
            };
            var rimes = new[]
            {
                "abe", "aile", "ance", "arde", "asse", "atre", "aume", "avre",
                "eche", "eille", "ence", "erne", "esse", "estre", "eule", "euse",
                "iche", "ienne", "igne", "ille", "ine", "ise", "isse", "ithe",
                "oche", "oire", "onde", "onne", "orne", "osse", "ote", "ouche",
                "oule", "ourde", "ouse", "ule", "umes", "ure", "usse", "yre",
                "aison", "ement", "erie", "esque", "iere", "oison", "ude", "ynthe"
            };
            var vocabulary = new List<string>();
            foreach (var onset in onsets)
                foreach (var rime in rimes)
                    vocabulary.Add(onset + rime);

            var document = new TextDocument();
            long seed = 12345;
            for (var p = 0; p < 500; p++)
            {
                var sb = new StringBuilder();
                for (var w = 0; w < 100; w++)
                {
                    if (w > 0) sb.Append(' ');
                    seed = (seed * 1103515245 + 12345) & 0x7FFFFFFF;
                    var uniform = seed / 2147483647.0;
                    // Zipf approché : le carré pousse vers la tête du lexique.
                    var index = (int)(vocabulary.Count * uniform * uniform);
                    sb.Append(vocabulary[Math.Min(index, vocabulary.Count - 1)]);
                }
                sb.Append('.');
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun { Text = sb.ToString() });
                document.Paragraphs.Add(paragraph);
            }
            return document;
        }

        private static void FiftyThousandWords(Harness t)
        {
            var document = FiftyThousandWordChapter();
            var host = Host(new RepetitionChecker());
            host.Run(document, null); // chauffe (JIT)
            var watch = Stopwatch.StartNew();
            var findings = host.Run(document, null);
            watch.Stop();
            t.Info("50 000 mots vérifiés en " + watch.ElapsedMilliseconds
                + " ms (" + findings.Count + " signalements)");
            t.Check(watch.ElapsedMilliseconds < 500,
                "la passe complète tient largement sous la demi-seconde");
            t.Check(findings.Count > 0, "le texte zipfien produit des répétitions");
        }

        /// <summary>0.4 — le tri du pilote est TOTAL : deux signalements au
        /// même (paragraphe, offset) — guillemet droit et espace insécable au
        /// même endroit, le cas normal en typographie — s'ordonnent par
        /// règle puis par longueur, reproductiblement.</summary>
        private sealed class TieChecker : IChecker
        {
            public string Id { get { return "typo-jouet"; } }
            public string Label { get { return "Typographie-jouet"; } }
            public FindingCategory Category { get { return FindingCategory.Typography; } }
            public CheckerScope Scope { get { return CheckerScope.WholeDocument; } }

            public List<Finding> CheckParagraph(TextParagraph paragraph, StyleSheet styles)
            {
                throw new NotSupportedException();
            }

            public List<Finding> Check(TextDocument document, StyleSheet styles)
            {
                // Volontairement dans le MAUVAIS ordre : le pilote trie.
                var findings = new List<Finding>();
                findings.Add(new Finding
                {
                    ParagraphIndex = 0, Start = 3, Length = 2,
                    Category = FindingCategory.Typography,
                    Message = "b", RuleId = "zz-nbsp", CheckerId = Id, Word = "b"
                });
                findings.Add(new Finding
                {
                    ParagraphIndex = 0, Start = 3, Length = 4,
                    Category = FindingCategory.Typography,
                    Message = "c", RuleId = "aa-quote", CheckerId = Id, Word = "c"
                });
                findings.Add(new Finding
                {
                    ParagraphIndex = 0, Start = 3, Length = 1,
                    Category = FindingCategory.Typography,
                    Message = "a", RuleId = "aa-quote", CheckerId = Id, Word = "a"
                });
                return findings;
            }
        }

        private static void TotalOrder(Harness t)
        {
            var document = Document("N'importe quel texte.");
            var first = Host(new TieChecker()).Run(document, null);
            var second = Host(new TieChecker()).Run(document, null);
            t.Equal(3, first.Count, "les trois ex æquo survivent");
            t.Equal("aa-quote", first[0].RuleId, "départage par règle d'abord");
            t.Equal(1, first[0].Length, "puis par longueur (1 avant 4)");
            t.Equal(4, first[1].Length, "le second aa-quote suit");
            t.Equal("zz-nbsp", first[2].RuleId, "la règle zz ferme la marche");
            var same = true;
            for (var i = 0; i < first.Count; i++)
                if (first[i].RuleId != second[i].RuleId
                    || first[i].Length != second[i].Length) same = false;
            t.Check(same, "deux passes rendent exactement le même ordre");
        }
    }
}

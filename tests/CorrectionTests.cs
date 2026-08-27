using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
            ContentAddressedCache(t);
            WarmParagraphPrimes(t);
            DuplicateParagraphsCloned(t);
            DeferredPipeline(t);
            FiftyThousandWords(t);
        }

        // ---------------------------------------- batch 30 : cache par contenu

        /// <summary>Le cache des vérificateurs locaux est adressé par CONTENU
        /// (batch 30) : changer d'écrit puis revenir ne revérifie RIEN — le
        /// coût du clic sur un chapitre déjà vu tombe à la lecture du cache.
        /// Un paragraphe déplacé garde aussi son entrée (l'intention du
        /// batch 27, enfin tenue à la lettre).</summary>
        private static void ContentAddressedCache(Harness t)
        {
            var chapterOne = Document("Une faute ici.", "Un texte propre.");
            var chapterTwo = Document("Un autre écrit.", "Encore une faute.");
            var checker = new CountingLocalChecker();
            var host = Host(checker);

            host.Run(chapterOne, null);
            t.Equal(2, checker.Calls, "chapitre 1 : deux paragraphes vérifiés");
            host.Run(chapterTwo, null);
            t.Equal(4, checker.Calls, "chapitre 2 : deux de plus");
            var back = host.Run(chapterOne, null);
            t.Equal(4, checker.Calls,
                "RETOUR au chapitre 1 : zéro revérification (clé par contenu)");
            t.Equal(1, back.Count, "la faute du chapitre 1 est toujours là");
            t.Equal(0, back[0].ParagraphIndex, "à sa position");

            // Un paragraphe déplacé en tête garde son entrée : seul le
            // NOUVEAU contenu (aucun ici — échange de places) se vérifie.
            var moved = chapterOne.Paragraphs[0];
            chapterOne.Paragraphs.RemoveAt(0);
            chapterOne.Paragraphs.Add(moved);
            var swapped = host.Run(chapterOne, null);
            t.Equal(4, checker.Calls, "échange de paragraphes : cache intact");
            t.Equal(1, swapped[0].ParagraphIndex, "l'index suit le déplacement");
        }

        /// <summary>WarmParagraph (batch 30) — la pompe de l'ouverture :
        /// préchauffer chaque paragraphe remplit le cache, la passe complète
        /// qui suit ne vérifie plus rien, et seuls les signalements FRAIS
        /// sont rendus (matière du préchauffage des suggestions).</summary>
        private static void WarmParagraphPrimes(Harness t)
        {
            var document = Document("Une faute au début.", "Un texte propre.");
            var checker = new CountingLocalChecker();
            var host = Host(checker);

            var fresh = host.WarmParagraph(document, 0, null);
            t.Equal(1, fresh.Count, "le préchauffage rend le signalement frais");
            t.Equal("faute", fresh[0].Word, "le mot signalé");
            t.Equal(0, host.WarmParagraph(document, 0, null).Count,
                "déjà au cache : rien de frais");
            host.WarmParagraph(document, 1, null);
            t.Equal(2, checker.Calls, "deux paragraphes, deux vérifications");

            host.Run(document, null);
            t.Equal(2, checker.Calls,
                "la passe complète après préchauffage : ZÉRO vérification");
            t.Equal(0, host.WarmParagraph(document, 99, null).Count,
                "index hors bornes : silence (l'utilisateur édite déjà)");
        }

        /// <summary>Deux paragraphes au MÊME contenu partagent une entrée de
        /// cache : chaque consommateur reçoit SA copie (CloneForParagraph) —
        /// sans elle, le ParagraphIndex reposé du second écraserait celui du
        /// premier et les deux ondulés tomberaient sur la même ligne.</summary>
        private static void DuplicateParagraphsCloned(Harness t)
        {
            var document = Document("Une faute jumelle.", "Une faute jumelle.");
            var host = Host(new CountingLocalChecker());
            var findings = host.Run(document, null);
            t.Equal(2, findings.Count, "deux signalements, un par jumeau");
            t.Equal(0, findings[0].ParagraphIndex, "le premier au paragraphe 0");
            t.Equal(1, findings[1].ParagraphIndex, "le second au paragraphe 1");
        }

        // ---------------------------------------- lot A (batch 29) : différé

        /// <summary>Un vérificateur différé JOUET : chaque demande rend une
        /// tâche que le test complète lui-même (latence contrôlée à la main,
        /// déterministe — les continuations du pilote sont synchrones).</summary>
        private sealed class FakeDeferredChecker : IDeferredChecker
        {
            public readonly List<TaskCompletionSource<List<Finding>>> Requests
                = new List<TaskCompletionSource<List<Finding>>>();
            public readonly List<CancellationToken> Tokens
                = new List<CancellationToken>();

            public string Id { get { return "differe-jouet"; } }
            public string Label { get { return "Différé-jouet"; } }
            public FindingCategory Category { get { return FindingCategory.Grammar; } }
            public CheckerScope Scope { get { return CheckerScope.ParagraphLocal; } }

            public List<Finding> Check(TextDocument document, StyleSheet styles)
            {
                throw new NotSupportedException();
            }

            public List<Finding> CheckParagraph(TextParagraph paragraph,
                StyleSheet styles)
            {
                throw new NotSupportedException(
                    "Un différé ne se vérifie jamais en synchrone.");
            }

            public Task<List<Finding>> CheckParagraphAsync(
                TextParagraph paragraph, StyleSheet styles,
                CancellationToken token)
            {
                var source = new TaskCompletionSource<List<Finding>>();
                Requests.Add(source);
                Tokens.Add(token);
                return source.Task;
            }
        }

        private static Finding Grammar(int start, int length)
        {
            return new Finding
            {
                Start = start,
                Length = length,
                Category = FindingCategory.Grammar,
                Severity = FindingSeverity.Warning,
                Message = "accord de jouet",
                RuleId = "gn_jouet",
                CheckerId = "differe-jouet"
            };
        }

        /// <summary>Lot A (batch 29) — le pipeline différé, sans WPF et sans
        /// Grammalecte : fusion dans le bon ordre avec les synchrones,
        /// résultat périmé jeté, annulation, échec silencieux, invalidation
        /// de connaissance (génération).</summary>
        private static void DeferredPipeline(Harness t)
        {
            // 1. La passe ne BLOQUE jamais : synchrone servi seul, fusion au
            // cycle que l'événement déclenche, ordre positionnel respecté.
            var fake = new FakeDeferredChecker();
            var host = Host(new RepetitionChecker(), fake);
            var doc = Document("Le manoir dort et le manoir ronfle.");
            var arrived = new ManualResetEvent(false);
            host.DeferredArrived += delegate { arrived.Set(); };
            var first = host.Run(doc, null);
            t.Equal(1, first.Count,
                "le synchrone (répétition) rend SEUL, sans attendre le différé");
            t.Equal("repetition", first[0].CheckerId, "et c'est bien lui");
            t.Equal(1, host.PendingDeferred, "une demande différée en vol");
            fake.Requests[0].SetResult(new List<Finding> { Grammar(0, 2) });
            t.Check(arrived.WaitOne(2000), "l'arrivée lève l'événement");
            var merged = host.Run(doc, null);
            t.Equal(2, merged.Count, "fusion au cycle suivant");
            t.Equal("differe-jouet", merged[0].CheckerId,
                "l'ordre reste positionnel (le grammatical à 0 passe devant)");
            t.Equal(0, host.PendingDeferred, "plus rien en vol");
            var again = host.Run(doc, null);
            t.Equal(1, fake.Requests.Count,
                "paragraphe inchangé : la réponse est en cache, pas de relance");
            t.Equal(2, again.Count, "et elle ressert");

            // 2. Le résultat périmé est JETÉ : la demande A part, le
            // paragraphe change, la demande B part — la réponse d'A arrive
            // en RETARD et doit être ignorée (l'empreinte ne correspond plus).
            var fakeStale = new FakeDeferredChecker();
            var hostStale = Host(fakeStale);
            var docStale = Document("Le paragraphe premier.");
            var arrivedStale = new ManualResetEvent(false);
            hostStale.DeferredArrived += delegate { arrivedStale.Set(); };
            hostStale.Run(docStale, null); // demande A, en vol
            docStale.Paragraphs[0].Runs[0].Text = "Le paragraphe a changé.";
            hostStale.Run(docStale, null); // demande B
            t.Equal(2, fakeStale.Requests.Count,
                "le texte a changé : une seconde demande part");
            fakeStale.Requests[0].SetResult(new List<Finding> { Grammar(5, 3) });
            t.Check(!arrivedStale.WaitOne(150),
                "la VIEILLE réponse est jetée en silence (pas d'événement)");
            fakeStale.Requests[1].SetResult(new List<Finding> { Grammar(3, 6) });
            t.Check(arrivedStale.WaitOne(2000),
                "la réponse à jour, elle, est admise");
            var current = hostStale.Run(docStale, null);
            t.Equal(1, current.Count, "un seul signalement grammatical");
            t.Equal(3, current[0].Start, "celui de la réponse À JOUR");

            // 3. Annulation : changer d'écrit / fermer n'attend rien.
            var fake2 = new FakeDeferredChecker();
            var host2 = Host(fake2);
            var doc2 = Document("Un autre écrit entier.");
            var arrived2 = new ManualResetEvent(false);
            host2.DeferredArrived += delegate { arrived2.Set(); };
            host2.Run(doc2, null);
            t.Equal(1, host2.PendingDeferred, "en vol");
            host2.CancelDeferred();
            t.Equal(0, host2.PendingDeferred,
                "l'annulation vide le vol IMMÉDIATEMENT (sans attendre)");
            t.Check(fake2.Tokens[0].IsCancellationRequested,
                "le token de la tâche en route est bien annulé");
            fake2.Requests[0].SetResult(new List<Finding> { Grammar(0, 2) });
            t.Check(!arrived2.WaitOne(150),
                "une réponse d'avant l'annulation est jetée (génération)");
            host2.Run(doc2, null);
            t.Equal(2, fake2.Requests.Count,
                "après annulation, la demande repart au cycle suivant");

            // 4. Échec du vérificateur : il se TAIT, jamais un point de panne.
            var fake3 = new FakeDeferredChecker();
            var host3 = Host(fake3);
            var doc3 = Document("Texte dont l'analyse va échouer.");
            var arrived3 = new ManualResetEvent(false);
            host3.DeferredArrived += delegate { arrived3.Set(); };
            host3.Run(doc3, null);
            fake3.Requests[0].SetException(
                new InvalidOperationException("processus mort (jouet)"));
            t.Check(!arrived3.WaitOne(150), "l'échec ne lève pas l'événement");
            t.Equal(0, host3.PendingDeferred, "et libère le vol");
            t.Equal(0, host3.Run(doc3, null).Count,
                "la passe suivante vit très bien sans lui");
            t.Equal(2, fake3.Requests.Count,
                "et RETENTE (le processus sera peut-être revenu)");

            // 5. InvalidateCache : une réponse en vol calculée avec la
            // connaissance d'AVANT est jetée même à texte inchangé.
            fake3.Requests[1].SetResult(new List<Finding> { Grammar(0, 5) });
            t.Check(arrived3.WaitOne(2000), "réponse admise (état de référence)");
            host3.InvalidateCache();
            arrived3.Reset();
            host3.Run(doc3, null);
            t.Equal(3, fake3.Requests.Count, "invalidé : relance");
            // La réponse de la relance no 3 arrive APRÈS une nouvelle
            // invalidation : périmée par génération, jetée.
            host3.InvalidateCache();
            fake3.Requests[2].SetResult(new List<Finding> { Grammar(0, 5) });
            t.Check(!arrived3.WaitOne(150),
                "la génération a tourné : réponse d'ancienne connaissance jetée");
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

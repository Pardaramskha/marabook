using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marabook.Correction;
using Marabook.Correction.Grammalecte;

namespace Marabook.Tests
{
    /// <summary>C22 — l'étage style morphologique et les synonymes (batch
    /// 44) : les trames du pont (relevés de style, groupes de synonymes),
    /// la conversion en signalements aux offsets du pivot (NFD, astral,
    /// élément — les pièges de C9, rejoués), les interrupteurs, la liste des
    /// verbes ternes, le mémo de synonymes sur une recherche factice, la
    /// casse des remplacements. Tout en console, sans Python — SAUF la
    /// dernière vérification, de bout en bout par le vrai pont, qui ne
    /// s'exécute que si le runtime embarqué est là (sur le poste d'un
    /// contributeur sans python/, elle est simplement sautée).</summary>
    public static class StyleTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C22 — style morphologique + synonymes (b44)");
            StyleFrameParsing(t);
            ToFindingsMapping(t);
            ToFindingsSwitches(t);
            OptionsShape(t);
            DullVerbList(t);
            SynonymFrameParsing(t);
            ProviderMemo(t);
            ProviderFailureMemorizedUntilReset(t);
            ProviderAnswer(t);
            SuggestionSources(t);
            KeepCase(t);
            LiveBridge(t);
        }

        // ---------------------------------------------------- les trames

        private static void StyleFrameParsing(Harness t)
        {
            var line = "{\"id\": 3, \"findings\": ["
                + "{\"nStart\": 3, \"nEnd\": 10, \"kind\": \"dull\", \"word\": \"faisait\", \"lemma\": \"faire\"},"
                + "{\"nStart\": 11, \"nEnd\": 21, \"kind\": \"adverb\", \"word\": \"rapidement\", \"lemma\": \"rapidement\"},"
                + "{\"nStart\": 5, \"nEnd\": 2, \"kind\": \"dull\", \"word\": \"x\"},"
                + "{\"nStart\": 0, \"nEnd\": 2, \"kind\": \"inconnu\", \"word\": \"Il\"},"
                + "{\"pas\": \"un relevé\"}]}";
            var parsed = Marabook.Json.Parse(line);
            var items = GrammalecteBridge.ParseStyleItems(
                Marabook.Json.Field(parsed, "findings"));
            t.Equal(2, items.Count, "plage inversée, nature inconnue et entrée difforme : sautées");
            t.Equal("dull", items[0].Kind, "verbe terne");
            t.Equal("faire", items[0].Lemma, "le lemme voyage");
            t.Equal("adverb", items[1].Kind, "adverbe");
            t.Equal(11, items[1].Start, "offsets bruts (points de code)");
            t.Equal(21, items[1].End, "fin brute");
        }

        // -------------------------------------------- vers les signalements

        private static void ToFindingsMapping(Harness t)
        {
            // Pivot : emoji astral (2 unités UTF-16, 1 point de code), « e »
            // + accent combinant (NFD), un élément U+FFFC — les trois pièges.
            var original = char.ConvertFromUtf32(0x1F600) + " Il faisait tre"
                + (char) 0x0300 + "s rapidement ￼ tout.";
            var mapper = OffsetMapper.Build(original);
            var sent = mapper.Sent;
            var dullStart = sent.IndexOf("faisait", StringComparison.Ordinal);
            var adverbStart = sent.IndexOf("rapidement", StringComparison.Ordinal);
            // Les offsets « Python » sont en points de code du texte envoyé :
            // l'emoji compte 1, d'où le -1 sur les indices C# du texte envoyé.
            var items = new List<StyleItem>
            {
                new StyleItem { Start = dullStart - 1, End = dullStart - 1 + 7, Kind = "dull", Word = "faisait", Lemma = "faire" },
                new StyleItem { Start = adverbStart - 1, End = adverbStart - 1 + 10, Kind = "adverb", Word = "rapidement", Lemma = "rapidement" },
                new StyleItem { Start = 900, End = 910, Kind = "dull", Word = "hors" }
            };
            var findings = StyleChecker.ToFindings(items, mapper, true, true);
            t.Equal(2, findings.Count, "la plage aberrante est jetée, jamais un plantage");
            t.Equal("faisait", original.Substring(findings[0].Start, findings[0].Length),
                "le verbe terne retombe sur le pivot malgré l'emoji");
            t.Equal("rapidement", original.Substring(findings[1].Start, findings[1].Length),
                "l'adverbe retombe sur le pivot malgré l'accent NFD");
            t.Equal(FindingCategory.Style, findings[0].Category, "catégorie Style");
            t.Equal(FindingSeverity.Hint, findings[0].Severity, "un indice, jamais une faute");
            t.Equal("style", findings[0].CheckerId, "origine");
            t.Equal(StyleChecker.DullVerbRule, findings[0].RuleId, "règle du verbe terne");
            t.Equal(StyleChecker.AdverbRule, findings[1].RuleId, "règle de l'adverbe");
            t.Equal("faisait", findings[0].Word, "Word = clé des ignorés");
            t.Equal(SuggestionSource.Synonyms, findings[0].Suggests, "un verbe terne appelle des synonymes");
            t.Equal(SuggestionSource.Inline, findings[1].Suggests, "un adverbe n'en appelle pas");
            t.Check(findings[0].Message.Contains("faire"), "le lemme est dit dans le message");
            t.Check(findings[1].Message.Contains("rapidement"), "l'adverbe est cité");
            t.Check(!string.IsNullOrEmpty(findings[0].Detail), "un détail explique l'indice");
        }

        private static void ToFindingsSwitches(Harness t)
        {
            var mapper = OffsetMapper.Build("Il faisait rapidement.");
            var items = new List<StyleItem>
            {
                new StyleItem { Start = 3, End = 10, Kind = "dull", Word = "faisait", Lemma = "faire" },
                new StyleItem { Start = 11, End = 21, Kind = "adverb", Word = "rapidement", Lemma = "rapidement" }
            };
            t.Equal(1, StyleChecker.ToFindings(items, mapper, true, false).Count,
                "verbes ternes éteints : l'adverbe seul");
            t.Equal(StyleChecker.AdverbRule,
                StyleChecker.ToFindings(items, mapper, true, false)[0].RuleId, "… et c'est bien lui");
            t.Equal(1, StyleChecker.ToFindings(items, mapper, false, true).Count,
                "adverbes éteints : le verbe seul");
            t.Equal(0, StyleChecker.ToFindings(items, mapper, false, false).Count,
                "tout éteint : silence (une réponse partie avant le changement d'option ne passe pas)");
        }

        private static void OptionsShape(Harness t)
        {
            var checker = new StyleChecker(null);
            t.Check(checker.Wanted, "voulu par défaut (les deux relevés)");
            checker.AdverbsEnabled = false;
            checker.DullVerbs = new List<string> { "être", "faire" };
            var options = checker.BuildOptions();
            var json = Marabook.Json.Write(options);
            var back = Marabook.Json.AsObject(Marabook.Json.Parse(json));
            t.Equal(false, Marabook.Json.AsBool(Marabook.Json.Field(back, "adverbs"), true),
                "adverbs suit l'interrupteur");
            t.Equal(true, Marabook.Json.AsBool(Marabook.Json.Field(back, "dull"), false),
                "dull suit l'interrupteur");
            var verbs = Marabook.Json.AsList(Marabook.Json.Field(back, "dullVerbs"));
            t.Equal(2, verbs.Count, "la liste des verbes ternes voyage");
            t.Equal("être", Marabook.Json.AsString(verbs[0]), "en UTF-8, accents compris");
            checker.DullVerbsEnabled = false;
            t.Check(!checker.Wanted, "plus rien à relever : le vérificateur n'a pas sa place dans le pilote");
            t.Equal(CheckerScope.ParagraphLocal, checker.Scope, "différé = local au paragraphe (contrat du pilote)");
            t.Equal(FindingCategory.Style, checker.Category, "catégorie Style");
        }

        private static void DullVerbList(Harness t)
        {
            var verbs = StyleChecker.ParseDullVerbs("Être, avoir ; faire\n dire,,  Faire   mettre");
            t.Equal(5, verbs.Count, "virgules, points-virgules, retours, espaces ; doublon plié");
            t.Equal("être", verbs[0], "en minuscules");
            t.Equal("mettre", verbs[4], "le dernier");
            t.Equal("être, avoir, faire, dire, mettre", StyleChecker.JoinDullVerbs(verbs),
                "aller-retour vers la zone de saisie");
            t.Equal(0, StyleChecker.ParseDullVerbs("  ").Count, "vide → vide (le dialogue remet le défaut)");
            t.Check(Array.IndexOf(StyleChecker.DefaultDullVerbs, "être") >= 0
                && Array.IndexOf(StyleChecker.DefaultDullVerbs, "faire") >= 0,
                "le défaut contient les classiques");
        }

        // ------------------------------------------------------ synonymes

        private static void SynonymFrameParsing(Harness t)
        {
            var line = "{\"id\": 9, \"groups\": ["
                + "{\"pos\": \"Nom\", \"lemma\": \"cheval\", \"words\": [\"étalons\", \"poneys\", \"étalons\", \"\"]},"
                + "{\"pos\": \"Verbe\", \"lemma\": \"x\", \"words\": []},"
                + "{\"pos\": \"Adjectif\", \"words\": [\"vastes\"]}]}";
            var groups = GrammalecteBridge.ParseSynonymGroups(
                Marabook.Json.Field(Marabook.Json.Parse(line), "groups"));
            t.Equal(2, groups.Count, "le groupe vide est sauté");
            t.Equal("Nom", groups[0].Pos, "nature");
            t.Equal("cheval", groups[0].Lemma, "lemme");
            t.Equal(2, groups[0].Words.Count, "doublon et vide écartés");
            t.Equal("vastes", groups[1].Words[0], "lemme absent : toléré");
        }

        private static void ProviderMemo(Harness t)
        {
            var calls = 0;
            var provider = new SynonymProvider(delegate(string word, CancellationToken token)
            {
                calls++;
                var group = new SynonymGroup { Pos = "Nom", Lemma = word };
                group.Words.Add(word + "-1");
                group.Words.Add(word + "-2");
                group.Words.Add(word + "-3");
                var other = new SynonymGroup { Pos = "Nom", Lemma = word };
                other.Words.Add(word + "-1"); // doublon entre groupes
                other.Words.Add(word + "-4");
                var source = new TaskCompletionSource<List<SynonymGroup>>();
                source.SetResult(new List<SynonymGroup> { group, other });
                return source.Task;
            });
            var arrivals = new List<string>();
            provider.Arrived += delegate(string word) { lock (arrivals) arrivals.Add(word); };

            t.Equal(null, provider.Cached("maison"), "inconnu avant toute demande");
            t.Equal(null, provider.Flat("maison", 3), "à plat : idem");
            provider.Request("maison");
            provider.Request("maison"); // déjà connu : rien
            t.Equal(1, calls, "une recherche, jamais deux pour le même mot");
            t.Equal(1, arrivals.Count, "une arrivée");
            t.Equal("maison", arrivals[0], "… pour ce mot");
            var flat = provider.Flat("maison", 3);
            t.Equal(3, flat.Count, "plafonné à 3");
            t.Equal("maison-1", flat[0], "ordre du pont");
            t.Equal(4, provider.Flat("maison", 10).Count, "sans plafond : 4 distincts (doublon plié)");
            t.Equal(0, provider.PendingCount, "plus rien en vol");
            t.Equal(0, provider.Cached("").Count, "mot vide : réponse vide, pas de demande");
            provider.Request("");
            t.Equal(1, calls, "… et rien n'est parti");
        }

        /// <summary>Revue du 13/09 : un échec est MÉMORISÉ (sinon un pont
        /// indisponible laisse « Recherche… » à jamais et chaque repeinture
        /// relance cent demandes) ; Reset() rouvre la porte ; une annulation
        /// ne laisse rien.</summary>
        private static void ProviderFailureMemorizedUntilReset(Harness t)
        {
            var calls = 0;
            var provider = new SynonymProvider(delegate(string word, CancellationToken token)
            {
                calls++;
                var source = new TaskCompletionSource<List<SynonymGroup>>();
                if (calls == 1) source.SetException(new InvalidOperationException("pont absent"));
                else if (calls == 2) source.SetCanceled();
                else source.SetResult(new List<SynonymGroup>());
                return source.Task;
            });
            var arrivals = 0;
            provider.Arrived += delegate { arrivals++; };
            provider.Request("mot");
            t.Equal(SynonymProvider.LookupState.Failed, provider.StateOf("mot"), "l'échec est mémorisé");
            t.Equal(1, arrivals, "… et annoncé (l'interface remplace « Recherche… »)");
            t.Equal(0, provider.PendingCount, "le vol est libéré");
            provider.Request("mot");
            t.Equal(1, calls, "aucune relance tant que le pont n'est pas revenu");
            var answer = provider.Answer("mot", 5, true);
            t.Equal(0, answer.Words.Count, "réponse vide…");
            t.Check(answer.Notice.Contains("indisponible") && answer.Notice.Contains("pont absent"),
                "… avec la notice et la raison (" + answer.Notice + ")");
            provider.Reset();
            t.Equal(SynonymProvider.LookupState.Unknown, provider.StateOf("mot"), "Reset : on repart");
            provider.Request("mot"); // annulée
            t.Equal(2, calls, "la demande suivante retente");
            t.Equal(SynonymProvider.LookupState.Unknown, provider.StateOf("mot"), "une annulation ne laisse rien");
            t.Equal(1, arrivals, "… ni annonce");
            provider.Request("mot");
            t.Equal(0, provider.Cached("mot").Count, "réponse vide = mot inconnu, mémorisée");
            t.Equal(2, arrivals, "annoncée");
        }

        /// <summary>La réponse pour l'interface : demande ou non selon que
        /// le pont est en service, notice quand il n'y a rien.</summary>
        private static void ProviderAnswer(Harness t)
        {
            var calls = 0;
            var provider = new SynonymProvider(delegate(string word, CancellationToken token)
            {
                calls++;
                var group = new SynonymGroup { Pos = "Nom", Lemma = word };
                if (word != "rien") group.Words.Add(word + "-1");
                var source = new TaskCompletionSource<List<SynonymGroup>>();
                source.SetResult(new List<SynonymGroup> { group });
                return source.Task;
            });
            var quiet = provider.Answer("mot", 3, false);
            t.Check(!quiet.Pending && quiet.Words.Count == 0 && quiet.Notice.Length == 0,
                "pont hors service : réponse vide et muette, rien ne part");
            t.Equal(0, calls, "… vraiment rien");
            var eager = provider.Answer("mot", 3, true);
            t.Equal(1, calls, "geste explicite : la demande part");
            t.Check(!eager.Pending && eager.Words.Count == 1 && eager.Words[0] == "mot-1",
                "réponse synchrone de la recherche factice : servie tout de suite");
            var none = provider.Answer("rien", 3, true);
            t.Equal("Aucun synonyme connu", none.Notice, "mot sans synonyme : la notice");
            t.Check(provider.Answer("", 3, true).Words.Count == 0, "mot vide : réponse vide");
        }

        /// <summary>Chaque vérificateur annonce la SOURCE de ses suggestions
        /// — l'interface n'aiguille plus par chaînes.</summary>
        private static void SuggestionSources(Harness t)
        {
            var document = new Marabook.Model.TextDocument();
            var paragraph = new Marabook.Model.TextParagraph();
            paragraph.Runs.Add(new Marabook.Model.TextRun { Text = "Le marabout regarde le marabout." });
            document.Paragraphs.Add(paragraph);
            var repetitions = new RepetitionChecker().Check(document, null);
            t.Equal(1, repetitions.Count, "une répétition");
            t.Equal(SuggestionSource.Synonyms, repetitions[0].Suggests, "la répétition veut des synonymes");
            t.Equal(SuggestionSource.Synonyms, repetitions[0].CloneForParagraph(3).Suggests,
                "la copie de surface garde la source");
            var finding = new Finding();
            t.Equal(SuggestionSource.Inline, finding.Suggests, "défaut : les suggestions portées");
        }

        private static void KeepCase(Harness t)
        {
            t.Equal("demeure", Typography.KeepCase("maison", "demeure"), "minuscule : tel quel");
            t.Equal("Demeure", Typography.KeepCase("Maison", "demeure"), "capitale initiale suivie");
            t.Equal("DEMEURE", Typography.KeepCase("MAISON", "demeure"), "tout en capitales suivi");
            t.Equal("À", Typography.KeepCase("A", "à"), "une lettre : capitale initiale, pas « tout en capitales »");
            t.Equal("sans tarder", Typography.KeepCase("vite", "sans tarder"), "locution intacte");
        }

        // ------------------------------------------- de bout en bout, réel

        /// <summary>Le vrai pont, si le runtime embarqué est là : la grammaire
        /// après la refonte du canal (non-régression), le style et les
        /// synonymes. Sautée sans python/ ou grammalecte/ — jamais rouge
        /// pour une absence d'outil.</summary>
        private static void LiveBridge(Harness t)
        {
            if (!File.Exists(GrammalecteBridge.PythonPath)
                || !File.Exists(GrammalecteBridge.ScriptPath))
            {
                t.Info("pont Grammalecte absent : vérification de bout en bout sautée");
                return;
            }
            var bridge = new GrammalecteBridge();
            try
            {
                var grammar = bridge.CheckAsync("Les chat mange.",
                    GrammalecteOptions.Effective(null, true, false), CancellationToken.None);
                t.Check(grammar.Wait(60000), "grammaire : réponse dans le délai (initialisation comprise)");
                t.Check(grammar.Result.Count >= 1, "grammaire : l'accord « Les chat » est relevé (canal refondu, même résultat)");

                var style = bridge.AnalyzeStyleAsync(
                    "Il faisait rapidement ses devoirs, puis il avait mangé au moment dit.",
                    new StyleChecker(bridge).BuildOptions(), CancellationToken.None);
                t.Check(style.Wait(30000), "style : réponse dans le délai");
                var kinds = new List<string>();
                foreach (var item in style.Result) kinds.Add(item.Kind + ":" + item.Word);
                t.Check(kinds.Contains("dull:faisait"), "« faisait » est un verbe terne (" + string.Join(", ", kinds.ToArray()) + ")");
                t.Check(kinds.Contains("adverb:rapidement"), "« rapidement » est un adverbe en -ment");
                t.Check(!kinds.Contains("dull:avait"), "« avait mangé » : l'auxiliaire est laissé en paix");
                t.Check(!kinds.Contains("adverb:moment"), "« moment » n'est pas un adverbe");
                t.Check(!kinds.Contains("dull:puis"), "« puis » n'est pas « je puis »");
                t.Check(!kinds.Contains("dull:dit"), "« au moment dit » : participe, pas relevé");
                var composed = bridge.AnalyzeStyleAsync("Il a fait la vaisselle, ils ont dit non, elle est allée au marché.",
                    new StyleChecker(bridge).BuildOptions(), CancellationToken.None);
                t.Check(composed.Wait(30000), "temps composés : réponse");
                var composedKinds = new List<string>();
                foreach (var item in composed.Result) composedKinds.Add(item.Kind + ":" + item.Word);
                t.Equal(0, composedKinds.Count, "« a fait », « ont dit », « est allée » : aucun verbe terne ("
                    + string.Join(", ", composedKinds.ToArray()) + ")");
                var participle = bridge.SynonymsAsync("mangeant", CancellationToken.None);
                t.Check(participle.Wait(30000), "participe présent : réponse");
                var participleWords = new List<string>();
                foreach (var group in participle.Result) participleWords.AddRange(group.Words);
                t.Check(participleWords.Contains("dévorant"), "« mangeant » → « dévorant », fléchi au participe présent");

                var synonyms = bridge.SynonymsAsync("chevaux", CancellationToken.None);
                t.Check(synonyms.Wait(30000), "synonymes : réponse dans le délai (thésaurus chargé à la demande)");
                var words = new List<string>();
                foreach (var group in synonyms.Result) words.AddRange(group.Words);
                t.Check(words.Contains("étalons"), "« chevaux » → « étalons », fléchi au pluriel");
                var verb = bridge.SynonymsAsync("faisait", CancellationToken.None);
                t.Check(verb.Wait(30000), "synonymes d'un verbe conjugué");
                words.Clear();
                foreach (var group in verb.Result) words.AddRange(group.Words);
                t.Check(words.Contains("exécutait"), "« faisait » → « exécutait », conjugué à l'imparfait 3s");
                var unknown = bridge.SynonymsAsync("xyzzyq", CancellationToken.None);
                t.Check(unknown.Wait(30000), "mot inconnu : réponse");
                t.Equal(0, unknown.Result.Count, "… vide");
            }
            finally
            {
                bridge.Dispose();
            }
        }
    }
}

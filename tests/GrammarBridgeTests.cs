using System;
using System.Collections.Generic;
using Marabook.Correction;
using Marabook.Correction.Grammalecte;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C9 — la correspondance des offsets du pont Grammalecte
    /// (batch 29, lot B.2), SEULE : des réponses JSON enregistrées en dur
    /// dans les fixtures, jamais de Python — la suite reste verte sur la
    /// machine d'un contributeur qui n'a pas installé Grammalecte. Les trois
    /// pièges du prompt (NFD, U+FFFC, découpage par paragraphe) plus le
    /// quatrième découvert à la lecture du source : Python compte en POINTS
    /// DE CODE, C# en unités UTF-16 — un caractère astral décale tout.</summary>
    public static class GrammarBridgeTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C9 — pont Grammalecte (offsets et trames)");
            MapperPlainText(t);
            MapperNfd(t);
            MapperAstral(t);
            MapperElement(t);
            MapperAberrant(t);
            FrameParsing(t);
            FixtureEndToEnd(t);
            RuleIgnore(t);
        }

        // ------------------------------------------------------ le mappeur

        private static void MapperPlainText(Harness t)
        {
            var mapper = OffsetMapper.Build("Il mange une pomme.");
            t.Equal("Il mange une pomme.", mapper.Sent,
                "texte ASCII : envoyé tel quel");
            int start;
            int length;
            t.Check(mapper.MapRange(3, 8, out start, out length), "plage valide");
            t.Equal(3, start, "identité sur l'ASCII");
            t.Equal(5, length, "« mange »");
        }

        private static void MapperNfd(Harness t)
        {
            // « après la fête » en NFD : è = e + U+0300, ê = e + U+0302.
            var original = "apre" + (char) 0x0300 + "s la fe" + (char) 0x0302 + "te";
            var mapper = OffsetMapper.Build(original);
            t.Equal("après la fête", mapper.Sent,
                "le texte part en NFC (les règles de Grammalecte le supposent)");
            // Une erreur sur « fête » : points de code 9..13 du texte envoyé.
            int start;
            int length;
            t.Check(mapper.MapRange(9, 13, out start, out length), "plage valide");
            t.Equal(10, start,
                "le début revient en unités D'ORIGINE (l'accent NFD décale)");
            t.Equal(5, length,
                "la longueur d'origine couvre la marque combinante");
            t.Equal("fe" + (char) 0x0302 + "te",
                original.Substring(start, length),
                "la plage d'origine couvre exactement le mot décomposé");
        }

        private static void MapperAstral(Harness t)
        {
            // Un emoji (U+1F600, paire de substitution : 1 point de code,
            // 2 unités UTF-16) avant l'erreur — le piège Python/C#.
            var original = char.ConvertFromUtf32(0x1F600) + " la pomme verte";
            var mapper = OffsetMapper.Build(original);
            // « pomme » : points de code 5..10 (l'emoji en vaut UN).
            int start;
            int length;
            t.Check(mapper.MapRange(5, 10, out start, out length), "plage valide");
            t.Equal("pomme", original.Substring(start, length),
                "le caractère astral décale d'une unité UTF-16, la carte suit");
        }

        private static void MapperElement(Harness t)
        {
            // U+FFFC (un élément du pivot) part TEL QUEL : même longueur.
            var original = "avant ￼ apre" + (char) 0x0300 + "s";
            var mapper = OffsetMapper.Build(original);
            t.Check(mapper.Sent.IndexOf('￼') == 6,
                "U+FFFC est envoyé tel quel, à sa place");
            int start;
            int length;
            t.Check(mapper.MapRange(8, 13, out start, out length), "plage valide");
            t.Equal("apre" + (char) 0x0300 + "s", original.Substring(start, length),
                "après l'élément ET l'accent NFD, la plage retombe juste");
        }

        private static void MapperAberrant(Harness t)
        {
            var mapper = OffsetMapper.Build("court");
            int start;
            int length;
            t.Check(!mapper.MapRange(2, 99, out start, out length),
                "une borne au-delà du texte est refusée (jamais un plantage)");
            t.Check(!mapper.MapRange(-1, 2, out start, out length),
                "une borne négative aussi");
            t.Check(!mapper.MapRange(4, 2, out start, out length),
                "une plage inversée aussi");
        }

        // -------------------------------------------------------- la trame

        /// <summary>Une réponse RÉELLE de Grammalecte (format 2.3.0,
        /// _createErrorAsDict), enregistrée en dur : le lecteur de trame en
        /// tire les erreurs, saute les entrées difformes.</summary>
        private static void FrameParsing(Harness t)
        {
            var line = "{\"id\": 7, \"errors\": ["
                + "{\"nStart\": 3, \"nEnd\": 9, \"sLineId\": \"#3355\", "
                + "\"sRuleId\": \"g2__conf_a_à__b2_a1_1\", \"sType\": \"conf\", "
                + "\"aColor\": [89, 49, 129], "
                + "\"sMessage\": \"Confusion probable : écrivez « à ».\", "
                + "\"aSuggestions\": [\"à\"], \"URL\": \"\"},"
                + "{\"nStart\": -2, \"nEnd\": 1, \"sRuleId\": \"difforme\"},"
                + "{\"pas\": \"une erreur\"}]}";
            var parsed = Marabook.Json.Parse(line);
            var id = Marabook.Json.AsInt(
                Marabook.Json.Field(parsed, "id"), -1);
            t.Equal(7, id, "l'id d'appariement revient TEL QUEL");
            var errors = GrammalecteBridge.ParseErrors(
                Marabook.Json.Field(parsed, "errors"));
            t.Equal(1, errors.Count,
                "les entrées difformes sont sautées, jamais devinées");
            t.Equal("g2__conf_a_à__b2_a1_1", errors[0].RuleId, "sRuleId conservé");
            t.Equal("conf", errors[0].Option, "sType = l'option d'origine");
            t.Equal(1, errors[0].Suggestions.Count, "la suggestion");
            t.Equal("à", errors[0].Suggestions[0], "celle de Grammalecte, brute");
        }

        // ---------------------------------------- de bout en bout, fixture

        /// <summary>Le trajet complet SANS pont : texte du pivot (NFD, emoji,
        /// élément) → texte envoyé → réponse en dur aux offsets « Python »
        /// (points de code du texte envoyé) → signalements aux unités du
        /// pivot. Les offsets attendus sont vérifiés par extraction.</summary>
        private static void FixtureEndToEnd(Harness t)
        {
            // Pivot : « Les chevaux blanc [FFFC] gambadent tre‌◌̀s vite. »
            // (« blanc » sans accord — l'erreur type de Grammalecte.)
            var original = "Les chevaux blanc ￼ gambadent tre"
                + (char) 0x0300 + "s vite.";
            var mapper = OffsetMapper.Build(original);
            t.Equal("Les chevaux blanc ￼ gambadent très vite.",
                mapper.Sent, "envoyé : NFC, élément préservé");
            // La réponse en dur : « blanc » → « blancs » (gn), aux points de
            // code du texte ENVOYÉ (12..17) ; « très » y va de 30 à 34.
            var fixture = "["
                + "{\"nStart\": 12, \"nEnd\": 17, \"sRuleId\": \"g1__gn_2m_accord\", "
                + "\"sType\": \"gn\", \"sMessage\": \"Accord de nombre errone.\", "
                + "\"aSuggestions\": [\"blancs\"]},"
                + "{\"nStart\": 30, \"nEnd\": 34, \"sRuleId\": \"g_test_tres\", "
                + "\"sType\": \"conf\", \"sMessage\": \"Exemple.\", "
                + "\"aSuggestions\": []}]";
            var errors = GrammalecteBridge.ParseErrors(
                Marabook.Json.Parse(fixture));
            var findings = GrammarChecker.ToFindings(errors, mapper);
            // Batch 33 — une règle TYPOGRAPHIQUE (sType « typo », « nbsp »…)
            // signale en Typographie, les autres en Grammaire ; et le jeu
            // d'options suit les deux interrupteurs des Options du correcteur.
            var typoErrors = new List<BridgeError>
            {
                new BridgeError { Start = 0, End = 2, Option = "nbsp", Message = "espace" },
                new BridgeError { Start = 0, End = 2, Option = "conj", Message = "accord" }
            };
            var tagged = GrammarChecker.ToFindings(typoErrors, mapper);
            t.Equal(2, tagged.Count, "deux erreurs converties");
            t.Equal(FindingCategory.Typography, tagged[0].Category, "sType nbsp → Typographie");
            t.Equal(FindingCategory.Grammar, tagged[1].Category, "sType conj → Grammaire");
            var both = GrammalecteOptions.Effective(null, true, true);
            t.Check((bool)both["nbsp"] && (bool)both["conj"], "typographie ON rallume nbsp, la grammaire reste");
            var typoOff = GrammalecteOptions.Effective(null, true, false);
            t.Check(!(bool)typoOff["nbsp"] && (bool)typoOff["conj"], "typographie OFF : nbsp éteint, conj actif");
            var grammarOff = GrammalecteOptions.Effective(null, false, true);
            t.Check((bool)grammarOff["nbsp"] && !(bool)grammarOff["conj"], "grammaire OFF : conj éteint, nbsp actif");
            var forced = new Dictionary<string, bool> { { "conj", false } };
            t.Check(!(bool)GrammalecteOptions.Effective(forced, true, true)["conj"],
                "le choix explicite de l'utilisateur garde le dernier mot");
            t.Equal(2, findings.Count, "deux signalements");
            t.Equal("blanc", original.Substring(findings[0].Start,
                findings[0].Length), "l'ondulé couvre exactement « blanc »");
            t.Equal(FindingCategory.Grammar, findings[0].Category, "catégorie");
            t.Equal("g1__gn_2m_accord", findings[0].RuleId, "la règle");
            t.Equal("blancs", findings[0].Suggestions[0],
                "la suggestion de Grammalecte alimente le menu, brute");
            t.Equal("tre" + (char) 0x0300 + "s",
                original.Substring(findings[1].Start, findings[1].Length),
                "après l'élément, la plage NFD d'origine est exacte");
        }

        // --------------------------------------------- « ignorer la règle »

        private static void RuleIgnore(Harness t)
        {
            var host = new CheckerHost();
            var document = new TextDocument();
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "Les chevaux blanc." });
            document.Paragraphs.Add(paragraph);
            var finding = new Finding
            {
                Start = 12,
                Length = 5,
                Category = FindingCategory.Grammar,
                RuleId = "g1__gn_2m_accord",
                CheckerId = "grammar",
                Message = "Accord."
            };
            var stub = new StubChecker(finding);
            host.Add(stub);
            t.Equal(1, host.Run(document, null).Count, "signalé d'abord");
            host.IgnoreRule("g1__gn_2m_accord");
            t.Equal(0, host.Run(document, null).Count,
                "« ignorer cette règle » tait tous ses signalements");
            host.IgnoredRules.Clear();
            t.Equal(1, host.Run(document, null).Count,
                "retirer la règle de la liste la fait reparler");
        }

        private sealed class StubChecker : IChecker
        {
            private readonly Finding _finding;
            public StubChecker(Finding finding) { _finding = finding; }
            public string Id { get { return "grammar"; } }
            public string Label { get { return "Grammaire-jouet"; } }
            public FindingCategory Category { get { return FindingCategory.Grammar; } }
            public CheckerScope Scope { get { return CheckerScope.ParagraphLocal; } }
            public List<Finding> Check(TextDocument document, StyleSheet styles)
            {
                throw new NotSupportedException();
            }
            public List<Finding> CheckParagraph(TextParagraph paragraph,
                StyleSheet styles)
            {
                return new List<Finding>
                {
                    new Finding
                    {
                        Start = _finding.Start,
                        Length = _finding.Length,
                        Category = _finding.Category,
                        RuleId = _finding.RuleId,
                        CheckerId = _finding.CheckerId,
                        Message = _finding.Message
                    }
                };
            }
        }
    }
}

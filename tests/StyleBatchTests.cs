using System;
using System.Collections.Generic;
using UniversSale.Correction;
using UniversSale.Model;

namespace UniversSale.Tests
{
    /// <summary>C23 — le batch 45 « style » : la typographie à la frappe
    /// (les garde-fous de TypographyLive), les verbes de dialogue
    /// (DialogueChecker : reconnaissance des incises, fenêtre, inventaire),
    /// les racines des mots (SpellEngine.Stems, si le dictionnaire est là)
    /// et les répétitions qui les emploient, et le bilan de style
    /// (phrases, rythme, dialogue, variété, récit en mots simples).</summary>
    public static class StyleBatchTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C23 — b45 : frappe typographique, incises, racines, bilan");
            LiveRestrictWindow(t);
            LiveRestrictTypedDeletion(t);
            LiveRestrictReplacement(t);
            LiveRestrictVeto(t);
            NestedQuotes(t);
            NestedQuotesAcrossParagraphs(t);
            OrphanQuoteAndLongQuote(t);
            DialogueCollect(t);
            DialogueWindow(t);
            DialogueInventory(t);
            Stems(t);
            RepetitionWithLemma(t);
            Sentences(t);
            ReportMetrics(t);
            ReportNarrates(t);
        }

        // ------------------------------------------------ frappe typographique

        private static List<CharOp> Ops(string before, string after)
        {
            return CharDiff.Diff(before, after);
        }

        private static string Apply(List<CharOp> ops)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var op in ops) if (op.Type != '-') sb.Append(op.Char);
            return sb.ToString();
        }

        /// <summary>Une correction loin derrière le curseur attend la passe ;
        /// celle sous le curseur passe.</summary>
        private static void LiveRestrictWindow(Harness t)
        {
            // Deux apostrophes droites : une au début (hors fenêtre de 10),
            // une juste avant le curseur (à la fin).
            var before = "l'un vint, puis ce fut l'autre";
            var after = "l’un vint, puis ce fut l’autre";
            var ops = Ops(before, after);
            int delta;
            bool changed;
            var kept = TypographyLive.Restrict(ops, before.Length, 1, 10, out delta, out changed);
            t.Check(changed, "une correction retenue");
            t.Equal("l'un vint, puis ce fut l’autre", Apply(kept), "seule l'apostrophe proche du curseur est corrigée");
            t.Equal(0, delta, "remplacement 1 pour 1 : le curseur ne bouge pas");
            kept = TypographyLive.Restrict(ops, before.Length, 1, 200, out delta, out changed);
            t.Equal(after, Apply(kept), "fenêtre large : les deux");
        }

        /// <summary>On n'efface jamais ce qui vient d'être tapé quand il n'y a
        /// rien à mettre à la place (l'espace en fin de ligne).</summary>
        private static void LiveRestrictTypedDeletion(Harness t)
        {
            var before = "Bonjour ";
            var after = "Bonjour";
            var ops = Ops(before, after);
            int delta;
            bool changed;
            var kept = TypographyLive.Restrict(ops, before.Length, 1, 80, out delta, out changed);
            t.Check(!changed, "la suppression pure de l'espace tapé est refusée");
            t.Equal(before, Apply(kept), "le texte reste tel quel");
            t.Equal(0, delta, "curseur immobile");
            // La même suppression, plus loin derrière : acceptée (« a  b » → « a b »).
            before = "a  b, puis";
            after = "a b, puis";
            ops = Ops(before, after);
            kept = TypographyLive.Restrict(ops, before.Length, 1, 80, out delta, out changed);
            t.Check(changed, "un double espace plus loin derrière le curseur : corrigé");
            t.Equal(-1, delta, "le curseur recule d'un caractère");
        }

        /// <summary>« ... » → « … » : trois caractères deviennent un, le curseur
        /// recule de deux ; rien n'est touché après le curseur.</summary>
        private static void LiveRestrictReplacement(Harness t)
        {
            var before = "Attends... la suite... arrive";
            var after = "Attends… la suite… arrive";
            var ops = Ops(before, after);
            var caret = "Attends...".Length;
            int delta;
            bool changed;
            var kept = TypographyLive.Restrict(ops, caret, 1, 80, out delta, out changed);
            t.Check(changed, "les points de suspension sous le curseur");
            t.Equal("Attends… la suite... arrive", Apply(kept), "les points APRÈS le curseur ne bougent pas");
            t.Equal(-2, delta, "trois caractères pour un : le curseur recule de deux");
            var options = new TypographyOptions();
            var live = Typography.Clean("Il dit \"bonjour\" et partit...", options);
            t.Check(live.Text.Contains("«") && live.Text.EndsWith("…"), "la passe elle-même sert de moteur à la frappe");
        }

        /// <summary>Un bloc refusé (Ctrl+Z) n'est plus proposé ; les autres
        /// passent ; les blocs retenus sont rendus pour mémoire.</summary>
        private static void LiveRestrictVeto(Harness t)
        {
            var before = "Il dit \"oui\" puis partit...";
            var after = "Il dit « oui » puis partit…";
            var ops = Ops(before, after);
            int delta;
            List<KeyValuePair<int, string>> accepted;
            var kept = TypographyLive.Restrict(ops, before.Length, 1, 200, null, out delta, out accepted);
            t.Equal(after, Apply(kept), "sans refus : tout passe");
            t.Check(accepted.Count >= 2, "les blocs retenus sont rendus (" + accepted.Count + ")");
            var quoteAt = before.IndexOf('"');
            kept = TypographyLive.Restrict(ops, before.Length, 1, 200,
                delegate(int start, string deleted) { return deleted == "\"" && start == quoteAt; },
                out delta, out accepted);
            var text = Apply(kept);
            t.Check(text.StartsWith("Il dit \"oui"), "le guillemet refusé reste droit (" + text + ")");
            t.Check(text.EndsWith("…"), "les autres corrections passent");
        }

        /// <summary>« Elle a dit “non” » : des guillemets droits DANS une
        /// citation ouverte deviennent courbes, pas français.</summary>
        private static void NestedQuotes(Harness t)
        {
            var options = new TypographyOptions();
            var top = Typography.Clean("Il dit \"oui\".", options).Text;
            t.Check(top.Contains("«") && top.Contains("»"), "au premier niveau : « » (" + top + ")");
            var nested = Typography.Clean("« Comment ça, \"pas le faire\" ? »", options).Text;
            t.Check(nested.Contains("“pas le faire”"), "imbriqués dans « » : courbes “ ” (" + nested + ")");
            t.Check(!nested.Contains("««"), "jamais deux « de suite");
            // (Des guillemets droits DANS des guillemets droits : indécidable —
            // le cas réel est celui de l'auteur, où l'extérieur est déjà « ».)
            var closed = Typography.Clean("« Oui. » Puis \"non\".", options).Text;
            t.Check(closed.IndexOf('«', closed.IndexOf('»')) > 0 && !closed.Contains("“"),
                "après la fermante » : de nouveau des « » (" + closed + ")");
            t.Equal(1, Typography.QuoteDepth("« a « b » c", 0), "profondeur : deux ouverts, un fermé = un");
            t.Equal(0, Typography.QuoteDepth("a » b", 0), "jamais négative");
        }

        /// <summary>La réplique ouverte au paragraphe d'avant compte : la
        /// passe enchaîne la profondeur d'un paragraphe à l'autre.</summary>
        private static void NestedQuotesAcrossParagraphs(Harness t)
        {
            var options = new TypographyOptions();
            var document = Document(
                "« Elle n'a pas voulu le faire.",
                "— Comment ça, \"pas le faire\" ? »",
                "Il répondit \"oui\".");
            var result = TypographyPass.Run(document, options);
            var second = PivotEdit.FlatText(result.Paragraphs[1]);
            t.Check(second.Contains("“pas le faire”"), "réplique ouverte plus haut : courbes (" + second + ")");
            var third = PivotEdit.FlatText(result.Paragraphs[2]);
            t.Check(third.Contains("«") && third.Contains("»") && !third.Contains("“"),
                "la réplique est fermée : de nouveau « » (" + third + ")");
            t.Check(Typography.Clean("\"pas le faire\"", options, null, 1).Text.Contains("“"),
                "Clean avec un « ouvert avant : courbes");
        }

        /// <summary>Un guillemet orphelin ne gèle plus le paragraphe : les
        /// paires se convertissent, l'orphelin reste ; une citation longue se
        /// convertit entière à la frappe (fenêtre = le paragraphe) ; les
        /// guillemets de suite ne font pas grimper la profondeur.</summary>
        private static void OrphanQuoteAndLongQuote(Harness t)
        {
            var options = new TypographyOptions();
            var orphan = Typography.Clean("Il dit \"oui\" et \" seul.", options);
            t.Check(orphan.Text.Contains("«") && orphan.Text.Contains("»") && orphan.Text.Contains("\" seul"),
                "orphelin : la paire est convertie, l'orphelin reste (" + orphan.Text + ")");
            t.Check(orphan.Warnings.Count > 0, "… et il est signalé");
            var longInner = new string('a', 150);
            var before = "Il dit \"" + longInner + "\"";
            var after = Typography.Clean(before, options).Text;
            var ops = CharDiff.Diff(before, after);
            int delta;
            bool changed;
            var kept = TypographyLive.Restrict(ops, before.Length, 1, int.MaxValue, out delta, out changed);
            var text = Apply(kept);
            t.Check(!text.Contains("\""), "citation longue à la frappe : l'ouvrant aussi (" + text.Substring(0, 12) + "…)");
            var suite = TypographyPass.Run(Document("« Premier paragraphe de la citation.", "« Second, avec \"un mot\" dedans. »", "Après : \"libre\"."), options);
            var second = PivotEdit.FlatText(suite.Paragraphs[1]);
            t.Check(second.Contains("“un mot”"), "guillemets de suite : dans la citation, courbes (" + second + ")");
            var third = PivotEdit.FlatText(suite.Paragraphs[2]);
            t.Check(third.Contains("«") && !third.Contains("“"), "après la fermante : de nouveau « » (" + third + ")");
        }

        // ----------------------------------------------------- verbes de dialogue

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

        private static void DialogueCollect(Harness t)
        {
            var checker = new DialogueChecker();
            var document = Document(
                "— Viens, dit-il.",
                "— Non, répondit-elle, peut-être plus tard.",
                "Il n'y a personne ici, et celui-là non plus.",
                "« Vraiment ? » lança-t-il.");
            var incises = checker.Collect(document);
            var surfaces = new List<string>();
            foreach (var incise in incises) surfaces.Add(incise.Surface);
            t.Equal("dit, répondit, lança", string.Join(", ", surfaces.ToArray()),
                "les incises des répliques ; « peut-être », « celui-là » et le récit ne comptent pas");
            t.Equal(1, incises[1].Paragraph, "la position : paragraphe");
            t.Equal("— Non, ".Length, incises[1].Start, "la position : début du verbe");
        }

        private static void DialogueWindow(Harness t)
        {
            var checker = new DialogueChecker { Window = 2 };
            var document = Document(
                "— A, dit-il.",
                "— B, répondit-elle.",
                "— C, dit-il.",
                "— D, murmura-t-elle.",
                "— E, cria-t-il.",
                "— F, dit-elle.");
            var findings = checker.Check(document, null);
            t.Equal(1, findings.Count, "« dit » revient à deux incises d'écart (fenêtre 2) ; le dernier « dit » est trop loin");
            t.Equal(2, findings[0].ParagraphIndex, "sur la troisième réplique");
            t.Check(findings[0].Message.Contains("2 incises plus haut"), "le message dit la distance en incises");
            t.Equal(DialogueChecker.Rule, findings[0].RuleId, "règle style-dialogue");
            t.Equal(SuggestionSource.Synonyms, findings[0].Suggests, "des synonymes en suggestion");
            checker.Window = 6;
            t.Equal(2, checker.Check(document, null).Count, "fenêtre 6 : les deux retours de « dit »");
            // La racine : « répondit » et « répondirent » ne font qu'un.
            checker.Lemma = delegate(string word)
            {
                var lower = word.ToLowerInvariant();
                return lower.StartsWith("répond") ? "répondre" : null;
            };
            var again = Document("— A, répondit-il.", "— B, répondirent-ils.");
            t.Equal(1, checker.Check(again, null).Count, "même racine, deux formes : relevé");
        }

        private static void DialogueInventory(Harness t)
        {
            var checker = new DialogueChecker();
            var document = Document("— A, dit-il.", "— B, dit-elle.", "— C, répondit-il.", "Récit.");
            var inventory = checker.Inventory(document);
            t.Equal(2, inventory.Count, "deux verbes");
            t.Equal("dit", inventory[0].Key, "le plus fréquent d'abord");
            t.Equal(2, inventory[0].Value, "deux « dit »");
        }

        // ------------------------------------------------------------ racines

        private static void Stems(Harness t)
        {
            var engine = SpellDictionary.Default;
            if (engine == null)
            {
                t.Info("dictionnaire absent : racines sautées");
                return;
            }
            t.Check(engine.Stems("chevaux").Contains("cheval"), "chevaux → cheval");
            t.Check(engine.Stems("cheval").Contains("cheval"), "cheval → cheval (entrée exacte)");
            t.Check(engine.Stems("répondit").Contains("répondre"), "répondit → répondre (" + string.Join("/", engine.Stems("répondit").ToArray()) + ")");
            t.Check(engine.Stems("Marabouts").Contains("marabout"), "Marabouts → marabout (casse et pluriel)");
            t.Equal(0, engine.Stems("xqzptl").Count, "mot inconnu : rien");
            t.Check(!engine.Stems("refaire").Contains("faire"), "un préfixe ne fait pas une racine (refaire ≠ faire)");
        }

        private static void RepetitionWithLemma(Harness t)
        {
            var checker = new RepetitionChecker { Radius = 50 };
            var document = Document("Un cheval passa. Deux chevaux suivirent.");
            t.Equal(0, checker.Check(document, null).Count, "sans racine : cheval et chevaux sont deux mots");
            checker.Lemma = delegate(string word)
            {
                var lower = word.ToLowerInvariant();
                return lower == "chevaux" || lower == "cheval" ? "cheval" : null;
            };
            var findings = checker.Check(document, null);
            t.Equal(1, findings.Count, "avec la racine : chevaux répète cheval");
            t.Equal("chevaux", findings[0].Word, "le mot signalé est la forme écrite");
            t.Check(findings[0].Message.Contains("« cheval » employé"), "le message nomme la forme d'avant (" + findings[0].Message + ")");
            var same = checker.Check(Document("Le cheval et le cheval."), null);
            t.Check(same.Count == 1 && same[0].Message.Contains("déjà employé"), "même forme : le message d'avant");
        }

        // -------------------------------------------------------------- bilan

        private static void Sentences(Harness t)
        {
            var sentences = StyleReport.SplitSentences("M. Durand arriva. Il dit : « Bonjour ! » Puis… rien. Etc. La fin");
            t.Equal(5, sentences.Count, "« M. » et « Etc. » ne coupent pas ; « ! » suivi de » coupe (" + string.Join(" | ", sentences.ToArray()) + ")");
            t.Equal("Etc. La fin", sentences[4], "« Etc. » ne coupe pas, la queue sans point compte");
        }

        private static void ReportMetrics(Harness t)
        {
            var document = Document(
                "Le marabout veille sur le marais. La nuit tombe sur les roseaux. Les grenouilles se taisent une à une. Un vent léger passe sur les joncs.",
                "— Viens, dit-il.",
                "Oui.");
            var findings = new List<Finding>
            {
                new Finding { ParagraphIndex = 0, Category = FindingCategory.Style, RuleId = "style-adverb", Word = "lentement" },
                new Finding { ParagraphIndex = 0, Category = FindingCategory.Style, RuleId = "style-dull-verb", Word = "faisait" },
                new Finding { ParagraphIndex = 0, Category = FindingCategory.Style, RuleId = "repetition", Word = "marais" },
                new Finding { ParagraphIndex = 0, Category = FindingCategory.Spelling, RuleId = "spelling", Word = "x" }
            };
            var report = StyleReport.Compute(document, findings, null, new DialogueChecker().Inventory(document));
            t.Equal(6, report.Sentences, "six phrases");
            t.Check(report.Words > 25, "les mots sont comptés (" + report.Words + ")");
            t.Equal(1, report.Monotonous.Count, "quatre phrases de longueur voisine : un ronron");
            t.Equal(4, report.Monotonous[0].Sentences, "… de quatre phrases");
            t.Check(report.DialogueShare > 0 && report.DialogueShare < 0.5, "la part du dialogue (" + Math.Round(report.DialogueShare, 2) + ")");
            t.Equal(1, report.Adverbs, "un adverbe compté");
            t.Equal(1, report.DullVerbs, "un verbe terne compté");
            t.Equal(1, report.Repetitions, "une répétition comptée");
            t.Equal(1, report.DialogueVerbs.Count, "l'inventaire des incises est repris");
            t.Equal(1, report.ParagraphsToReview.Count, "le paragraphe qui concentre trois relevés de style");
            t.Equal(3, report.ParagraphsToReview[0].Value, "… la faute d'orthographe ne compte pas");
        }

        private static void ReportNarrates(Harness t)
        {
            var document = Document("Court. Très court. Vraiment court. Encore. Fin.");
            var report = StyleReport.Compute(document, null, null, null);
            var sections = report.Narrate();
            t.Equal(6, sections.Count, "six sections : taille, rythme, dialogue, mots, tics, à revoir");
            var text = report.ToText();
            t.Check(text.Contains("LA TAILLE") && text.Contains("phrases"), "le récit est en français courant");
            t.Check(text.Contains("Trop court pour juger le rythme"), "trop court : on le dit au lieu de juger");
            t.Check(text.Contains("Pas de dialogue"), "pas de dialogue : dit simplement");
            t.Check(text.Contains("Aucun adverbe en -ment"), "les tics absents sont dits absents");
            foreach (var section in sections)
                foreach (var line in section.Lines)
                    t.Check(!line.Contains("lemme") && !line.Contains("token") && !line.Contains("morpholog"),
                        "aucun jargon : " + line);
        }
    }
}

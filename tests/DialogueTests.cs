using System;
using System.Collections.Generic;
using Marabook.Correction;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C56 — la passe des dialogues (1.1.0, 29/10) : les blocs de
    /// répliques, la norme des guillemets (ouvrant, reprises, fermant,
    /// fermant trop tôt), la norme des tirets, les cas laissés en paix, la
    /// redistribution sur les runs et les passages protégés.</summary>
    public static class DialogueTests
    {
        private const string Nbsp = " ";

        public static void Run(Harness t)
        {
            t.Suite("C56 — passe des dialogues");
            Blocks(t);
            ToQuotes(t);
            ToDashes(t);
            Untouched(t);
            Document(t);
        }

        private static TextDocument Doc(params string[] paragraphs)
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

        private static string[] After(DialoguePassResult result)
        {
            var texts = new string[result.Paragraphs.Count];
            for (var i = 0; i < texts.Length; i++) texts[i] = PivotEdit.FlatText(result.Paragraphs[i]);
            return texts;
        }

        private static string[] Pass(TypographyOptions options, params string[] paragraphs)
        {
            return After(DialoguePass.Run(Doc(paragraphs), options));
        }

        private static string Joined(string[] texts)
        {
            return string.Join(" / ", texts);
        }

        private static void Blocks(Harness t)
        {
            var blocks = DialoguePass.Blocks(new[] { "Récit.", "— A", "— B", "Récit.", "— C", "", "— D", "Fin." });
            t.Equal(2, blocks.Count, "deux dialogues : le récit sépare, le paragraphe vide est transparent");
            t.Equal("1,2", string.Join(",", blocks[0].ConvertAll(i => i.ToString()).ToArray()), "le premier bloc = ¶ 2-3");
            t.Equal("4,6", string.Join(",", blocks[1].ConvertAll(i => i.ToString()).ToArray()), "le second = ¶ 5 et 7, sans le vide");
            blocks = DialoguePass.Blocks(new[] { "—" + Nbsp + "«" + Nbsp + "Cadavre exquis", "Vive les funérailles", "Vous en perdrez la tête" + Nbsp + "»", "Récit." });
            t.Equal(1, blocks.Count, "une citation ouverte dans la réplique prolonge le bloc…");
            t.Equal(3, blocks[0].Count, "…jusqu'au fermant (trois paragraphes)");
            blocks = DialoguePass.Blocks(new[] { "«" + Nbsp + "Bonjour.", "—" + Nbsp + "Salut." + Nbsp + "»", "Il partit." });
            t.Equal(1, blocks.Count, "un bloc aux guillemets fermé n'avale pas le récit qui suit");
            t.Equal(2, blocks[0].Count, "…deux répliques");
            t.Equal(0, DialoguePass.Blocks(new[] { "Rien.", "", "Du récit « cité » ici." }).Count, "sans réplique, pas de dialogue");
        }

        private static void ToQuotes(Harness t)
        {
            var o = new TypographyOptions(); // guillemets français, pleine dedans (in)
            // Le cas moonlight : tout au cadratin, aucun guillemet.
            var after = Pass(o, "Elle dit" + Nbsp + ":", "—" + Nbsp + "Moi, c’est Alicia et toi" + " " + "?", "—" + Nbsp + "A…nna.", "Son prénom tomba.");
            t.Equal("«" + Nbsp + "Moi, c’est Alicia et toi ?", after[1], "norme guillemets : le tiret d'ouverture devient «");
            t.Equal("—" + Nbsp + "A…nna." + Nbsp + "»", after[2], "…la dernière réplique prend le fermant");
            t.Equal("Elle dit" + Nbsp + ":", after[0], "…le récit ne bouge pas");
            after = Pass(o, "—" + Nbsp + "Ce soir, tu reviendras ? demanda-t-elle.");
            t.Equal("«" + Nbsp + "Ce soir, tu reviendras ? demanda-t-elle." + Nbsp + "»", after[0], "une réplique seule : ouvrant et fermant, incise comprise");
            after = Pass(o, "- Viens.", "-- Vite.", "Il vint.");
            t.Equal("«" + Nbsp + "Viens. / -- Vite." + Nbsp + "» / Il vint.", Joined(after), "les tirets tapés (- et --) sont des têtes de réplique aussi ; la reprise reste à la typographie");
            // Le tiret devant un guillemet déjà là : il tombe, le bloc court jusqu'au fermant.
            after = Pass(o, "—" + Nbsp + "«" + Nbsp + "Cadavre exquis", "Vive les funérailles", "Vous en perdrez la tête" + Nbsp + "»", "Peu impressionnée…");
            t.Equal("«" + Nbsp + "Cadavre exquis / Vive les funérailles / Vous en perdrez la tête" + Nbsp + "» / Peu impressionnée…", Joined(after), "un tiret devant un guillemet ouvrant tombe, rien d'autre ne bouge");
            // Chaque réplique dans ses guillemets → un seul bloc.
            after = Pass(o, "«" + Nbsp + "Bonjour" + Nbsp + "», dit-il.", "«" + Nbsp + "Salut." + Nbsp + "»", "«" + Nbsp + "Ça va" + Nbsp + "?" + Nbsp + "»");
            t.Equal("«" + Nbsp + "Bonjour, dit-il. / —" + Nbsp + "Salut. / —" + Nbsp + "Ça va" + Nbsp + "?" + Nbsp + "»", Joined(after), "des répliques chacune entre guillemets : fermants retirés, reprises posées, un seul fermant");
            // Fermé trop tôt, puis des tirets.
            after = Pass(o, "«" + Nbsp + "Bonjour." + Nbsp + "»", "—" + Nbsp + "Salut.", "—" + Nbsp + "Ça va" + " " + "?");
            t.Equal("«" + Nbsp + "Bonjour. / —" + Nbsp + "Salut. / —" + Nbsp + "Ça va ?" + Nbsp + "»", Joined(after), "un fermant posé trop tôt est déplacé à la fin du bloc");
            // Le bilan.
            var result = DialoguePass.Run(Doc("Récit.", "—" + Nbsp + "A.", "—" + Nbsp + "B.", "Récit.", "«" + Nbsp + "C.", "—" + Nbsp + "D." + Nbsp + "»"), o);
            t.Equal(2, result.Blocks, "deux dialogues examinés");
            t.Equal(4, result.Lines, "quatre répliques");
            t.Equal(1, result.Wrong, "un seul hors norme");
            t.Equal(2, result.Changes.Count, "deux paragraphes modifiés");
            t.Check(result.Summary.Counters.Count == 2 && result.Summary.Total == 2, "les compteurs : un ouvrant, un fermant");
            // La norme anglaise : “ ” collés.
            var english = new TypographyOptions { DialogueOpen = "anglais" };
            after = Pass(english, "—" + Nbsp + "Hello.", "—" + Nbsp + "Hi.");
            t.Equal("“Hello. / —" + Nbsp + "Hi.”", Joined(after), "norme anglaise : “ ” sans blanc");
            // Le préréglage souple : fine dedans.
            var souple = new TypographyOptions { Preset = "souple" };
            after = Pass(souple, "—" + Nbsp + "Oui.");
            t.Equal("« Oui. »", after[0], "préréglage souple : la fine à l'intérieur des guillemets");
            // La reprise au demi-cadratin.
            var demi = new TypographyOptions { DialogueReprise = "demi" };
            after = Pass(demi, "«" + Nbsp + "A." + Nbsp + "»", "«" + Nbsp + "B." + Nbsp + "»");
            t.Equal("«" + Nbsp + "A. / –" + Nbsp + "B." + Nbsp + "»", Joined(after), "la reprise posée suit la norme (demi-cadratin)");
        }

        private static void ToDashes(Harness t)
        {
            var o = new TypographyOptions { DialogueOpen = "cadratin" };
            var after = Pass(o, "«" + Nbsp + "Bonjour, dit-il.", "—" + Nbsp + "Salut.", "—" + Nbsp + "Ça va ?" + Nbsp + "»", "Il partit.");
            t.Equal("—" + Nbsp + "Bonjour, dit-il. / —" + Nbsp + "Salut. / —" + Nbsp + "Ça va ? / Il partit.", Joined(after), "norme cadratin : les guillemets de bloc tombent, le tiret d'ouverture est posé");
            after = Pass(o, "«" + Nbsp + "Bonjour" + Nbsp + "», dit-il.", "«" + Nbsp + "Salut." + Nbsp + "»");
            t.Equal("—" + Nbsp + "Bonjour, dit-il. / —" + Nbsp + "Salut.", Joined(after), "chaque réplique entre guillemets : tirets d'ouverture et de reprise");
            after = Pass(o, "«" + Nbsp + "Il a dit «" + Nbsp + "non" + Nbsp + "».", "—" + Nbsp + "Ah." + Nbsp + "»");
            t.Equal("—" + Nbsp + "Il a dit «" + Nbsp + "non" + Nbsp + "». / —" + Nbsp + "Ah.", Joined(after), "les guillemets intérieurs restent");
            var demi = new TypographyOptions { DialogueOpen = "demi", DialogueReprise = "cadratin" };
            after = Pass(demi, "«" + Nbsp + "A.", "«" + Nbsp + "B." + Nbsp + "»");
            t.Equal("–" + Nbsp + "A. / —" + Nbsp + "B.", Joined(after), "ouverture au demi-cadratin, reprise au cadratin");
            var result = DialoguePass.Run(Doc("«" + Nbsp + "Une citation seule, pas un dialogue." + Nbsp + "»", "Récit.", "—" + Nbsp + "A.", "—" + Nbsp + "B."), o);
            t.Equal(0, result.Changes.Count, "norme tiret : une citation seule et un bloc au tiret sont en règle");
            t.Equal(2, result.Blocks, "…deux blocs examinés tout de même");
        }

        private static void Untouched(Harness t)
        {
            var o = new TypographyOptions();
            var result = DialoguePass.Run(Doc("Récit.", "«" + Nbsp + "Bonjour.", "—" + Nbsp + "Salut, dit-il.", "—" + Nbsp + "Ça va ?" + Nbsp + "»", "Il partit en disant «" + Nbsp + "adieu" + Nbsp + "»."), o);
            t.Equal(0, result.Changes.Count, "un dialogue en règle ne change pas");
            t.Equal(1, result.Blocks, "…un seul dialogue compté (la citation du récit n'en est pas)");
            result = DialoguePass.Run(Doc("«" + Nbsp + "Bonjour" + Nbsp + "», dit-il en entrant."), o);
            t.Equal(0, result.Changes.Count, "une réplique seule, ouverte et fermée dans sa ligne, est en règle");
            result = DialoguePass.Run(Doc("«" + Nbsp + "Bonjour, dit-il en entrant."), o);
            t.Equal("«" + Nbsp + "Bonjour, dit-il en entrant." + Nbsp + "»", After(result)[0], "…mais jamais fermée, elle prend son fermant");
            result = DialoguePass.Run(Doc(), o);
            t.Check(result.Blocks == 0 && result.Changes.Count == 0, "un écrit vide : rien");
            t.Equal("guillemets français « »", DialoguePass.NormLabel(o), "le libellé de la norme par défaut");
            t.Equal("tiret cadratin — en ouverture", DialoguePass.NormLabel(new TypographyOptions { DialogueOpen = "cadratin" }), "…et de la norme au cadratin");
        }

        private static void Document(Harness t)
        {
            var o = new TypographyOptions();
            // Les runs formatés survivent : le tiret en gras devient un « en gras, le reste garde sa forme.
            var document = new TextDocument();
            var first = new TextParagraph();
            first.Runs.Add(new TextRun { Text = "—" + Nbsp, Bold = true });
            first.Runs.Add(new TextRun { Text = "Viens, ", Italic = true });
            first.Runs.Add(new TextRun { Text = "dit-il." });
            document.Paragraphs.Add(first);
            var second = new TextParagraph();
            second.Runs.Add(new TextRun { Text = "—" + Nbsp + "Non." });
            document.Paragraphs.Add(second);
            var result = DialoguePass.Run(document, o);
            t.Equal(2, result.Changes.Count, "deux paragraphes changés");
            var replaced = result.Paragraphs[0];
            t.Equal("«" + Nbsp + "Viens, dit-il.", PivotEdit.FlatText(replaced), "le premier : « à la place du tiret");
            t.Check(replaced.Runs.Count == 3 && replaced.Runs[0].Bold == true && replaced.Runs[0].Text == "«" + Nbsp && replaced.Runs[1].Italic == true, "…les runs gardent leur forme (gras, italique)");
            t.Equal("—" + Nbsp + "Non." + Nbsp + "»", PivotEdit.FlatText(result.Paragraphs[1]), "le second : le fermant au bout");
            t.Check(result.Changes[0].Ops != null && result.Changes[1].Index == 1, "les changements portent leur diff et leur index");
            // Un passage « ne pas corriger » : le paragraphe est laissé, signalé.
            document = new TextDocument();
            var guarded = new TextParagraph();
            guarded.Runs.Add(new TextRun { Text = "—" + Nbsp + "Tel quel.", NoProof = true });
            document.Paragraphs.Add(guarded);
            result = DialoguePass.Run(document, o);
            t.Equal(0, result.Changes.Count, "un paragraphe « ne pas corriger » n'est pas touché");
            t.Check(result.Summary.Warnings.Count == 1 && result.Summary.Warnings[0].Contains("¶ 1"), "…et il est signalé");
            t.Equal(1, result.Wrong, "…mais compté hors norme");
        }
    }
}

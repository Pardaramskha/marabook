using System;
using System.Collections.Generic;
using System.Text;
using Marabook.Model;

namespace Marabook.Correction
{
    /// <summary>Le bilan de la passe des dialogues : celui de la passe
    /// typographique (paragraphes modifiés, compteurs, signalements), plus le
    /// nombre de dialogues et de répliques examinés — pour dire « tout est
    /// en règle » avec les chiffres.</summary>
    public class DialoguePassResult : TypographyPassResult
    {
        public int Blocks;   // dialogues (blocs de répliques qui se suivent)
        public int Lines;    // répliques
        public int Wrong;    // dialogues qui ne suivaient pas la norme
    }

    /// <summary>La passe des DIALOGUES (1.1.0, 29/10) — le bouton
    /// « Dialogues » du ruban Formatage. Elle repère les dialogues qui ne
    /// suivent pas la NORME d'ouverture et de fermeture choisie dans les
    /// options de formatage (TypographyOptions.DialogueOpen / DialogueReprise)
    /// et propose de les y ramener, dans la même fenêtre comparative que la
    /// passe typographique. Le cas qui l'a fait naître : un écrit dont toutes
    /// les répliques sont au cadratin, sans aucun guillemet, alors que la
    /// norme est « guillemets français » — la passe typographique, qui ne
    /// touche qu'un paragraphe à la fois, ne pouvait pas le voir.
    ///
    /// Un DIALOGUE est une suite de paragraphes-répliques (tiret ou guillemet
    /// ouvrant en tête, voir Typography.IsDialogueLine) qui se suivent ; un
    /// paragraphe qui prolonge une citation ouverte dans la réplique d'avant
    /// (la comptine citée sur dix lignes) en fait partie ; les paragraphes
    /// vides sont transparents ; un paragraphe de récit le ferme.
    ///
    /// Norme « guillemets » (français « » ou anglais “ ”) : le bloc s'ouvre
    /// par le guillemet ouvrant sur la première réplique, les suivantes
    /// prennent le tiret de reprise, le guillemet fermant termine la dernière
    /// réplique (incise comprise — « Je viens, dit-il. ») ; un fermant posé
    /// trop tôt (chaque réplique dans ses propres guillemets) est retiré.
    /// Norme « tiret » (cadratin ou demi-cadratin) : un bloc d'au moins deux
    /// répliques ouvert par un guillemet perd ses guillemets de bloc et prend
    /// le tiret d'ouverture puis ceux de reprise ; un paragraphe seul entre
    /// guillemets est laissé en paix (c'est peut-être une citation).
    ///
    /// Les signes à l'intérieur des répliques (tiret de reprise ramené au
    /// signe choisi, insécables, guillemets courbes imbriqués) restent
    /// l'affaire de la passe typographique.</summary>
    public static class DialoguePass
    {
        private enum Head { None, Dash, Quote }

        public static DialoguePassResult Run(TextDocument document, TypographyOptions options)
        {
            var result = new DialoguePassResult();
            if (options == null) options = new TypographyOptions();
            var count = document.Paragraphs.Count;
            var before = new string[count];
            for (var i = 0; i < count; i++) before[i] = PivotEdit.FlatText(document.Paragraphs[i]);
            var after = (string[])before.Clone();

            foreach (var block in Blocks(before))
            {
                result.Blocks++;
                result.Lines += CountLines(before, block);
                var touched = options.OpeningDash == '\0'
                    ? NormalizeToQuotes(after, block, options, result.Summary)
                    : NormalizeToDashes(after, block, options, result.Summary);
                if (touched) result.Wrong++;
            }

            // Les paragraphes modifiés, redistribués sur leurs runs — comme la
            // passe typographique ; un paragraphe portant un passage « ne pas
            // corriger » est laissé tel quel et signalé.
            for (var i = 0; i < count; i++)
            {
                var paragraph = document.Paragraphs[i];
                if (after[i] == before[i]) { result.Paragraphs.Add(paragraph); continue; }
                if (TypographyPass.NoProofSpans(paragraph) != null)
                {
                    result.Paragraphs.Add(paragraph);
                    result.Summary.Warnings.Add("¶ " + (i + 1) + " porte un passage « ne pas corriger » : laissé tel quel");
                    continue;
                }
                var ops = CharDiff.Diff(before[i], after[i]);
                if (ops == null) { result.Skipped++; result.Paragraphs.Add(paragraph); continue; }
                var replacement = TypographyPass.Redistribute(paragraph, ops);
                result.Paragraphs.Add(replacement);
                result.Changes.Add(new TypographyParagraphChange
                {
                    Index = i, Before = before[i], After = after[i], Ops = ops, Replacement = replacement
                });
            }
            return result;
        }

        /// <summary>Le libellé de la norme, pour les messages : « guillemets
        /// français « » », « tiret cadratin — en ouverture »…</summary>
        public static string NormLabel(TypographyOptions options)
        {
            if (options == null) options = new TypographyOptions();
            switch (options.DialogueOpen)
            {
                case "anglais": return "guillemets anglais “ ”";
                case "demi": return "tiret demi-cadratin – en ouverture";
                case "cadratin": return "tiret cadratin — en ouverture";
                default: return "guillemets français « »";
            }
        }

        // ------------------------------------------------------------ les blocs

        /// <summary>Les dialogues du texte : chaque bloc est la liste des index
        /// de ses paragraphes non vides, dans l'ordre.</summary>
        public static List<List<int>> Blocks(IList<string> texts)
        {
            var blocks = new List<List<int>>();
            List<int> current = null;
            var depth = 0; // guillemets ouverts dans le bloc courant
            for (var i = 0; i < texts.Count; i++)
            {
                var text = texts[i] ?? "";
                if (IsBlank(text)) continue; // transparent : ni ne joint, ni ne rompt
                var isLine = Typography.IsDialogueLine(text);
                var continues = current != null && depth > 0;
                if (isLine || continues)
                {
                    if (current == null) { current = new List<int>(); depth = 0; }
                    current.Add(i);
                    int close;
                    depth = Scan(text, depth, out close);
                    continue;
                }
                if (current != null) { blocks.Add(current); current = null; }
                depth = 0;
            }
            if (current != null) blocks.Add(current);
            return blocks;
        }

        private static int CountLines(string[] texts, List<int> block)
        {
            var n = 0;
            foreach (var index in block) if (HeadOf(texts[index]) != Head.None) n++;
            return n;
        }

        // ------------------------------------------------------------ norme guillemets

        /// <summary>Ramène un bloc à la norme des guillemets ; vrai s'il a
        /// fallu toucher quelque chose. depth = les guillemets de bloc
        /// ouverts avant chaque ligne, tels qu'ils seront après la passe.</summary>
        private static bool NormalizeToQuotes(string[] texts, List<int> block, TypographyOptions o, TypographyResult summary)
        {
            var open = o.QuoteOpen.ToString() + (o.EnglishQuotes ? "" : o.InsideQuotes.ToString());
            var close = (o.EnglishQuotes ? "" : o.InsideQuotes.ToString()) + o.QuoteClose;
            var reprise = o.RepriseDash + " ";
            var touched = false;
            var depth = 0;
            var lastCloseLine = -1; // le paragraphe où le bloc s'est refermé (depth 1 → 0)…
            var lastCloseIndex = -1; // …et l'index de ce fermant
            for (var k = 0; k < block.Count; k++)
            {
                var index = block[k];
                var text = texts[index];
                int start, end;
                var head = HeadOf(text, out start, out end);
                if (k == 0 && head == Head.Dash)
                {
                    // Le tiret d'ouverture devient le guillemet ouvrant — sauf
                    // si la réplique ouvre déjà un guillemet juste après lui
                    // (« — « Cadavre exquis ») : le tiret tombe, c'est tout.
                    var next = end < text.Length ? text[end] : '\0';
                    var already = next == '«' || next == '“';
                    text = text.Substring(0, start) + (already ? "" : open) + text.Substring(end);
                    summary.Count("guillemets ouvrants posés", 1);
                    touched = true;
                }
                else if (k > 0 && head != Head.None)
                {
                    if (depth == 0 && lastCloseLine >= 0)
                    {
                        // Le bloc s'était refermé trop tôt (chaque réplique
                        // dans ses guillemets) : ce fermant-là s'en va.
                        texts[lastCloseLine] = RemoveAt(texts[lastCloseLine], lastCloseIndex);
                        summary.Count("guillemets fermants retirés", 1);
                        touched = true;
                        depth = 1;
                    }
                    if (head == Head.Quote)
                    {
                        // Une réplique ouverte par son propre guillemet : tiret de reprise.
                        text = text.Substring(0, start) + reprise + text.Substring(end);
                        summary.Count("tirets de reprise posés", 1);
                        touched = true;
                    }
                }
                texts[index] = text;
                int closeIndex;
                depth = Scan(text, depth, out closeIndex);
                if (closeIndex >= 0) { lastCloseLine = index; lastCloseIndex = closeIndex; }
            }
            if (depth > 0)
            {
                // Le bloc reste ouvert à sa dernière réplique : le fermant la
                // termine, incise comprise.
                var last = block[block.Count - 1];
                texts[last] = TrimEnd(texts[last]) + close;
                summary.Count("guillemets fermants posés", 1);
                touched = true;
            }
            return touched;
        }

        // ------------------------------------------------------------ norme tiret

        /// <summary>Ramène un bloc à la norme des tirets ; vrai s'il a fallu
        /// toucher quelque chose. Seuls les blocs ouverts par un guillemet et
        /// comptant au moins deux répliques sont concernés.</summary>
        private static bool NormalizeToDashes(string[] texts, List<int> block, TypographyOptions o, TypographyResult summary)
        {
            if (HeadOf(texts[block[0]]) != Head.Quote) return false;
            if (CountLines(texts, block) < 2) return false; // une citation seule, pas un dialogue
            var opening = o.OpeningDash + " ";
            var reprise = o.RepriseDash + " ";
            var touched = false;
            var depth = 0; // les guillemets intérieurs (« Il a dit « non » ») restent comptés
            for (var k = 0; k < block.Count; k++)
            {
                var index = block[k];
                var text = texts[index];
                int start, end;
                var head = HeadOf(text, out start, out end);
                if (head == Head.Quote)
                {
                    text = text.Substring(0, start) + (k == 0 ? opening : reprise) + text.Substring(end);
                    summary.Count(k == 0 ? "tirets d'ouverture posés" : "tirets de reprise posés", 1);
                    touched = true;
                }
                // Le fermant de bloc — celui qui refermerait un guillemet qui
                // n'est plus là — s'en va avec le blanc qui le précède.
                int strayIndex;
                depth = ScanForStray(text, depth, out strayIndex);
                if (strayIndex >= 0)
                {
                    text = RemoveAt(text, strayIndex);
                    summary.Count("guillemets fermants retirés", 1);
                    touched = true;
                }
                texts[index] = text;
            }
            return touched;
        }

        // ------------------------------------------------------------ outils

        private static bool IsBlank(string text)
        {
            foreach (var c in text) if (!char.IsWhiteSpace(c)) return false;
            return true;
        }

        private static bool IsSpace(char c)
        {
            return c == ' ' || c == ' ' || c == ' ' || c == ' ' || c == '\t';
        }

        private static Head HeadOf(string text)
        {
            int start, end;
            return HeadOf(text, out start, out end);
        }

        /// <summary>La tête du paragraphe : tiret (— – - --) ou guillemet
        /// ouvrant (« “), avec les blancs qui le suivent — start = son index,
        /// end = le premier caractère du texte après lui.</summary>
        private static Head HeadOf(string text, out int start, out int end)
        {
            start = 0;
            while (start < text.Length && IsSpace(text[start])) start++;
            end = start;
            if (start >= text.Length) return Head.None;
            var c = text[start];
            if (c == '—' || c == '–') end = start + 1;
            else if (c == '-' && start + 1 < text.Length && (text[start + 1] == ' ' || text[start + 1] == '-'))
                end = text[start + 1] == '-' ? start + 2 : start + 1;
            else if (c == '«' || c == '“') end = start + 1;
            else return Head.None;
            var kind = c == '«' || c == '“' ? Head.Quote : Head.Dash;
            while (end < text.Length && IsSpace(text[end])) end++;
            return kind;
        }

        /// <summary>Compte les guillemets de bloc (« » ou “ ”) sur un
        /// paragraphe à partir de la profondeur d'avant — convention des
        /// guillemets de suite : un ouvrant en tête quand une citation est
        /// déjà ouverte n'ouvre rien. closeIndex = l'index du dernier fermant
        /// qui ramène la profondeur de 1 à 0, ou -1.</summary>
        private static int Scan(string text, int depthBefore, out int closeIndex)
        {
            closeIndex = -1;
            var depth = depthBefore;
            var first = -1;
            if (depthBefore > 0)
            {
                var i = 0;
                while (i < text.Length && IsSpace(text[i])) i++;
                if (i < text.Length && (text[i] == '«' || text[i] == '“')) first = i;
            }
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '«' || c == '“') { if (i != first) depth++; }
                else if (c == '»' || c == '”')
                {
                    if (depth == 1) closeIndex = i;
                    if (depth > 0) depth--;
                }
            }
            return depth;
        }

        /// <summary>Comme Scan, sans la convention de suite, et signale le
        /// premier fermant que RIEN n'ouvre (profondeur 0) : c'est le fermant
        /// du bloc dont l'ouvrant vient d'être remplacé par un tiret.</summary>
        private static int ScanForStray(string text, int depthBefore, out int strayIndex)
        {
            strayIndex = -1;
            var depth = depthBefore;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '«' || c == '“') depth++;
                else if (c == '»' || c == '”')
                {
                    if (depth == 0) { if (strayIndex < 0) strayIndex = i; }
                    else depth--;
                }
            }
            return depth;
        }

        /// <summary>Retire le fermant à cet index et le blanc qui le précède
        /// (l'insécable intérieure) ; « Bonjour », dit-il → « Bonjour, dit-il :
        /// le blanc d'après lui s'efface aussi devant une ponctuation.</summary>
        private static string RemoveAt(string text, int index)
        {
            var from = index;
            while (from > 0 && IsSpace(text[from - 1])) from--;
            var to = index + 1;
            if (to < text.Length && IsSpace(text[to]) && to + 1 < text.Length && ",.;:!?…".IndexOf(text[to + 1]) >= 0) to++;
            var result = text.Substring(0, from) + text.Substring(to);
            // Deux mots que le fermant seul séparait (« Bonjour »dit-il) : un espace.
            if (from > 0 && from < result.Length && !IsSpace(result[from - 1]) && !IsSpace(result[from]) && ",.;:!?…".IndexOf(result[from]) < 0)
                result = result.Substring(0, from) + " " + result.Substring(from);
            return result;
        }

        private static string TrimEnd(string text)
        {
            var end = text.Length;
            while (end > 0 && IsSpace(text[end - 1])) end--;
            return text.Substring(0, end);
        }
    }
}

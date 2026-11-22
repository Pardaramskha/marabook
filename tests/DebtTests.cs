using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.Tests
{
    /// <summary>C87 — 2.0.0 (21/11) : la dette. La recomposition partielle
    /// d'un très gros paragraphe doit rendre, frappe après frappe, EXACTEMENT
    /// ce que rend une composition entière ; la fenêtre de la typographie à
    /// la frappe ; l'index des éléments par identifiant ; les points
    /// d'annulation qui partagent les paragraphes inchangés.</summary>
    public static partial class DebtTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C87 — dette : recomposition partielle, typographie en fenêtre, index par identifiant, annulation par différence");
            Incremental(t, false);
            Incremental(t, true);
            TypographyWindow(t);
            Index(t);
            Undo(t);
            SkippedImages(t);
        }

        private static string Filler(int chars, int seed)
        {
            var words = new[] { "Keira", "descendit", "vers", "le", "port", "avant", "que", "jour", "ne", "se", "lève,", "la", "lettre", "du", "marabout", "serrée", "contre", "elle ;", "personne", "anticonstitutionnellement", "vit", "passer.", "Elle", "attendait", "longtemps", "l'aube", "incommensurablement" };
            var random = new Random(seed);
            var sb = new StringBuilder(chars + 32);
            while (sb.Length < chars) sb.Append(words[random.Next(words.Length)]).Append(' ');
            return sb.ToString(0, chars).TrimEnd() + ".";
        }

        private static string Describe(ComposedLine line)
        {
            var sb = new StringBuilder();
            sb.Append(line.Start).Append('-').Append(line.End).Append(line.Hyphenated ? "h" : "").Append(line.EndsParagraph ? "$" : "").Append(' ').Append(line.X.ToString("0.00")).Append(' ').Append(line.Height.ToString("0.00")).Append(':');
            foreach (var piece in line.Pieces)
                sb.Append('[').Append(piece.SourceStart).Append('+').Append(piece.SourceLength).Append(piece.IsSpace ? "_" + piece.SpaceWidth.ToString("0.000") : piece.Text).Append('@').Append(piece.Origin.X.ToString("0.000")).Append('x').Append(piece.ScaleX.ToString("0.0000")).Append(']');
            return sb.ToString();
        }

        /// <summary>La première différence entre deux compositions du même document, ou null.</summary>
        private static string FirstDifference(Composition a, Composition b)
        {
            if (a.Paragraphs.Count != b.Paragraphs.Count) return "paragraphes " + a.Paragraphs.Count + " / " + b.Paragraphs.Count;
            for (var p = 0; p < a.Paragraphs.Count; p++)
            {
                var la = a.Paragraphs[p].Lines;
                var lb = b.Paragraphs[p].Lines;
                if (a.Paragraphs[p].FlatLength != b.Paragraphs[p].FlatLength) return "longueur du paragraphe " + p;
                for (var i = 0; i < Math.Min(la.Count, lb.Count); i++)
                {
                    var da = Describe(la[i]);
                    var db = Describe(lb[i]);
                    if (da != db) return "paragraphe " + p + ", ligne " + i + " : « " + da + " » au lieu de « " + db + " »";
                }
                if (la.Count != lb.Count) return "paragraphe " + p + " : " + la.Count + " lignes au lieu de " + lb.Count;
            }
            if (a.Pages.Count != b.Pages.Count) return "pages " + a.Pages.Count + " / " + b.Pages.Count;
            for (var k = 0; k < a.Pages.Count; k++)
            {
                if (a.Pages[k].Lines.Count != b.Pages[k].Lines.Count) return "page " + k + " : " + a.Pages[k].Lines.Count + " lignes au lieu de " + b.Pages[k].Lines.Count;
                for (var i = 0; i < a.Pages[k].Lines.Count; i++)
                    if (a.Pages[k].Lines[i].ParagraphIndex != b.Pages[k].Lines[i].ParagraphIndex || a.Pages[k].Lines[i].LineIndex != b.Pages[k].Lines[i].LineIndex || Math.Abs(a.Pages[k].Lines[i].Y - b.Pages[k].Lines[i].Y) > 0.01)
                        return "page " + k + ", ligne posée " + i;
            }
            return null;
        }

        private static void Incremental(Harness t, bool formatted)
        {
            var label = formatted ? " (paragraphe à plusieurs mises en forme)" : "";
            var project = Project.CreateNew();
            var document = TextDocument.FromPlainText("Un début.\n" + Filler(42000, formatted ? 7 : 3) + "\nUne fin, plus courte.");
            var big = document.Paragraphs[1];
            if (formatted)
            {
                PivotEdit.ApplyFormat(big, 9000, 9400, delegate(TextRun run) { run.Italic = true; });
                PivotEdit.ApplyFormat(big, 20000, 20030, delegate(TextRun run) { run.Bold = true; });
            }
            var engine = new CompositionEngine(document, project.Styles, new PageSetup(), project, true, new StubGlyphMetrics());
            engine.ComposeAll();
            var lines = engine.Current.Paragraphs[1].Lines.Count;
            t.Check(lines > 300 && engine.Current.Paragraphs[1].Lines.Exists(delegate(ComposedLine l) { return l.Hyphenated; }), "un paragraphe de 42 000 signes : " + lines + " lignes, des césures" + label);

            var random = new Random(formatted ? 11 : 5);
            string problem = null;
            var edits = 0;
            var maxRedone = 0;
            long redone = 0;
            long partialTicks = 0, fullTicks = 0;
            for (var step = 0; step < 220 && problem == null; step++)
            {
                var length = PivotEdit.FlatLength(big);
                var where = step % 25 == 0 ? 0 : step % 25 == 1 ? length : step % 25 == 2 ? 9000 + random.Next(3) : random.Next(length + 1);
                var kind = random.Next(10);
                string what;
                if (kind < 4) { what = "une lettre à " + where; PivotEdit.InsertText(big, where, "e"); }
                else if (kind < 5) { what = "une espace à " + where; PivotEdit.InsertText(big, where, " "); }
                else if (kind < 6) { what = "des mots à " + where; PivotEdit.InsertText(big, where, " puis vint l'extraordinairement longue nuit "); }
                else if (kind < 8 && length > 10) { where = Math.Min(where, length - 1); what = "un signe ôté à " + where; PivotEdit.DeleteInParagraph(big, where, where + 1); }
                else if (kind < 9 && length > 200) { where = Math.Min(where, length - 60); var n = 1 + random.Next(50); what = n + " signes ôtés à " + where; PivotEdit.DeleteInParagraph(big, where, where + n); }
                else { where = Math.Min(where, Math.Max(0, length - 5)); what = "un mot remplacé à " + where; PivotEdit.ReplaceText(big, where, Math.Min(length, where + 4), "Zut"); }
                edits++;
                var watch = Stopwatch.StartNew();
                engine.RecomposeParagraph(1);
                partialTicks += watch.ElapsedTicks;
                if (engine.LastIncrementalLines > maxRedone) maxRedone = engine.LastIncrementalLines;
                redone += engine.LastIncrementalLines;
                // L'étalon : un moteur neuf, qui compose tout.
                var reference = new CompositionEngine(document, project.Styles, new PageSetup(), project, true, new StubGlyphMetrics());
                reference.IncrementalEnabled = false;
                watch.Restart();
                reference.ComposeAll();
                fullTicks += watch.ElapsedTicks;
                var difference = FirstDifference(engine.Current, reference.Current);
                if (difference != null) problem = "après " + edits + " modifications (" + what + ") : " + difference;
            }
            t.Check(problem == null, edits + " modifications au hasard : la recomposition partielle rend chaque fois ce que rend l'entière" + label + (problem == null ? "" : " — " + problem));
            t.Check(engine.IncrementalHits >= edits * (formatted ? 0.85 : 0.95), "…et c'est bien elle qui a servi (" + engine.IncrementalHits + " fois sur " + edits + ", " + engine.IncrementalFallbacks + " replis sur l'entière)" + label);
            // Le texte d'essai est en chasse fixe et se coupe partout : aucune ligne n'a de jeu, une frappe y fait donc des vagues bien plus longues qu'avec une vraie police (la sonde le mesure).
            t.Check(maxRedone > 0 && redone / (double)edits < lines * 0.45, "…en ne refaisant qu'une partie des lignes (" + (redone / edits) + " en moyenne, " + maxRedone + " au plus, sur " + lines + ")" + label);
            if (!formatted)
                Console.WriteLine("  mesure : recomposer un paragraphe de 42 000 signes après une frappe — " + (partialTicks * 1000.0 / Stopwatch.Frequency / edits).ToString("0.00") + " ms en partiel (pagination comprise), " + (fullTicks * 1000.0 / Stopwatch.Frequency / edits).ToString("0.00") + " ms en entier");

            // Ce que la recomposition partielle refuse : elle cède la place, sans erreur ni écart.
            var before = engine.IncrementalHits;
            big.AlignOverride = "center";
            engine.RecomposeParagraph(1);
            PivotEdit.ApplyFormat(big, 100, 140, delegate(TextRun run) { run.Underline = true; });
            engine.RecomposeParagraph(1);
            PivotEdit.InsertText(big, 500, " [[Keira]] ");
            engine.RecomposeParagraph(1);
            var check = new CompositionEngine(document, project.Styles, new PageSetup(), project, true, new StubGlyphMetrics());
            check.ComposeAll();
            t.Check(engine.IncrementalHits == before && FirstDifference(engine.Current, check.Current) == null, "un alignement, une mise en forme, un [[lien]] : le paragraphe est recomposé en entier, comme avant" + label);
            // Un paragraphe ordinaire ne passe jamais par là.
            var small = new CompositionEngine(TextDocument.FromPlainText(Filler(3000, 1)), project.Styles, new PageSetup(), project, true, new StubGlyphMetrics());
            small.ComposeAll();
            PivotEdit.InsertText(small.Current.Source.Paragraphs[0], 100, "x");
            small.RecomposeParagraph(0);
            t.Check(small.IncrementalHits == 0 && small.IncrementalFallbacks == 0, "un paragraphe ordinaire se recompose en entier : rien ne change pour lui" + label);
        }
    }
}

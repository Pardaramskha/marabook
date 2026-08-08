using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UniversSale.Correction;
using UniversSale.Model;
using UniversSale.Print;
using UniversSale.View;

namespace UniversSale.Tests.Ui
{
    /// <summary>Sonde du rendu des signalements (batch 26, lot A) : la même
    /// composition, avec des signalements posés, rendue deux fois par
    /// ComposedRenderer.DrawPage — écran (screenExtras=true) puis papier
    /// (false). L'ondulé de style (vert) doit exister au pixel dans le rendu
    /// écran et être INTROUVABLE dans le rendu papier : c'est la discipline
    /// de la teinte d'annotation, appliquée à la correction. (Le PDF ne lit
    /// même pas ScreenFindings — PdfWriter ne passe pas par DrawPage.)</summary>
    public static class CorrectionProbe
    {
        public static int Run()
        {
            var failures = 0;
            try
            {
                var document = new TextDocument();
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun
                {
                    Text = "Le marabout regarde le marabout dans le miroir."
                });
                document.Paragraphs.Add(paragraph);

                var styles = StyleSheet.CreateDefault();
                var setup = new PageSetup();
                var engine = new CompositionEngine(document, styles, setup,
                    null, false, new WpfGlyphMetrics());
                engine.ComposeAll();

                var host = new CheckerHost();
                host.Add(new RepetitionChecker());
                var findings = host.Run(document, styles);
                Check(ref failures, findings.Count == 1,
                    "la répétition du texte-jouet est détectée");

                var byParagraph = new System.Collections.Generic
                    .Dictionary<int, System.Collections.Generic.List<Finding>>();
                byParagraph[0] = findings;
                engine.Current.ScreenFindings = byParagraph;

                var screen = CountSquigglePixels(engine.Current, true, 0x2E, 0x9E, 0x6B);
                var paper = CountSquigglePixels(engine.Current, false, 0x2E, 0x9E, 0x6B);
                Check(ref failures, screen > 0,
                    "l'ondulé de style est PRÉSENT au rendu écran ("
                    + screen + " px verts)");
                Check(ref failures, paper == 0,
                    "l'ondulé est ABSENT du rendu papier (aperçu/impression)");

                // — L'orthographe (batch 27) : une faute soulignée en rouge à
                // l'écran, absente du papier — le critère de sortie du lot C.
                var spellEngine = SpellDictionary.Default;
                Check(ref failures, spellEngine != null,
                    "le dictionnaire embarqué se charge (dict/)");
                if (spellEngine != null)
                {
                    var faulty = new TextDocument();
                    var p2 = new TextParagraph();
                    p2.Runs.Add(new TextRun
                    { Text = "Le chateau domine la vallée." });
                    faulty.Paragraphs.Add(p2);
                    var engine2 = new CompositionEngine(faulty, styles, setup,
                        null, false, new WpfGlyphMetrics());
                    engine2.ComposeAll();
                    var spellChecker = new SpellChecker(spellEngine);
                    var host2 = new CheckerHost();
                    host2.Add(spellChecker);
                    var spelling = host2.Run(faulty, styles);
                    Check(ref failures, spelling.Count == 1
                        && spelling[0].Word == "chateau",
                        "la faute est détectée par le vérificateur branché");
                    Check(ref failures,
                        spellChecker.Suggestions("chateau").Contains("château"),
                        "château arrive en suggestion à la demande");
                    var by2 = new System.Collections.Generic
                        .Dictionary<int, System.Collections.Generic.List<Finding>>();
                    by2[0] = spelling;
                    engine2.Current.ScreenFindings = by2;
                    var red = CountSquigglePixels(engine2.Current, true, 0xD6, 0x45, 0x41);
                    var redPaper = CountSquigglePixels(engine2.Current, false, 0xD6, 0x45, 0x41);
                    Check(ref failures, red > 0,
                        "l'ondulé ORTHOGRAPHE est PRÉSENT à l'écran ("
                        + red + " px rouges)");
                    Check(ref failures, redPaper == 0,
                        "et ABSENT du rendu papier (donc du PDF)");
                }
                // — La grammaire (batch 29) : le trajet ENTIER, pont réel —
                // critères de sortie du batch. Sauté proprement si le runtime
                // embarqué manque (contributeur avant le lot E).
                GrammarProbe(ref failures, styles, setup);
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE CORRECTION EN ÉCHEC : " + error);
                failures++;
            }
            return failures;
        }

        /// <summary>Batch 29 — une faute d'accord soulignée dans une page
        /// composée (couleur Grammar, absente du papier), la suggestion de
        /// Grammalecte disponible, « ignorer cette règle » fonctionnel, et
        /// LA MESURE : l'orthographe reste instantanée pendant que la
        /// grammaire travaille (exigence « mesuré, pas supposé »).</summary>
        private static void GrammarProbe(ref int failures, StyleSheet styles,
            PageSetup setup)
        {
            if (!System.IO.File.Exists(Correction.Grammalecte
                .GrammalecteBridge.PythonPath))
            {
                Console.WriteLine("  (grammaire sautée : python/ absent — "
                    + "lot E non déployé sur cette machine)");
                return;
            }
            var bridge = new Correction.Grammalecte.GrammalecteBridge();
            try
            {
                var document = new TextDocument();
                var first = new TextParagraph();
                first.Runs.Add(new TextRun
                { Text = "Les chevaux blanc gambadent dans la prairie." });
                document.Paragraphs.Add(first);
                // Du volume pour la mesure : l'orthographe a du travail
                // pendant que la grammaire attend son sous-processus.
                for (var i = 0; i < 40; i++)
                {
                    var filler = new TextParagraph();
                    filler.Runs.Add(new TextRun
                    {
                        Text = "Le marabout traverse la lande et le vent "
                            + "psalmodie une complainte ancienne numéro " + i + "."
                    });
                    document.Paragraphs.Add(filler);
                }
                var engine = new CompositionEngine(document, styles, setup,
                    null, false, new WpfGlyphMetrics());
                engine.ComposeAll();

                var host = new CheckerHost();
                var spellEngine = SpellDictionary.Default;
                if (spellEngine != null) host.Add(new SpellChecker(spellEngine));
                var grammar = new Correction.Grammalecte.GrammarChecker(bridge);
                host.Add(grammar);
                var arrived = new System.Threading.ManualResetEvent(false);
                host.DeferredArrived += delegate { arrived.Set(); };

                // LA MESURE : la passe synchrone pendant que le différé part.
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var immediate = host.Run(document, styles);
                watch.Stop();
                Check(ref failures, host.PendingDeferred > 0,
                    "la grammaire TRAVAILLE (demandes en vol : "
                    + host.PendingDeferred + ")");
                Check(ref failures, watch.ElapsedMilliseconds < 250,
                    "l'orthographe reste instantanée pendant ce temps ("
                    + watch.ElapsedMilliseconds + " ms la passe, MESURÉ)");
                var grammarImmediate = 0;
                foreach (var finding in immediate)
                    if (finding.CheckerId == "grammar") grammarImmediate++;
                Check(ref failures, grammarImmediate == 0,
                    "la passe n'a PAS attendu la grammaire (0 différé servi)");

                // L'initialisation du premier lancement peut prendre ~2 s.
                Check(ref failures, arrived.WaitOne(30000),
                    "les réponses différées arrivent");
                // Les 41 paragraphes répondent par vagues : on laisse le vol
                // se vider (borné) avant de fusionner.
                for (var spin = 0; spin < 300 && host.PendingDeferred > 0; spin++)
                    System.Threading.Thread.Sleep(100);
                var merged = host.Run(document, styles);
                Finding accord = null;
                foreach (var finding in merged)
                    if (finding.CheckerId == "grammar"
                        && finding.ParagraphIndex == 0)
                        accord = finding;
                Check(ref failures, accord != null,
                    "la faute d'accord est signalée (différé fusionné)");
                if (accord == null) return;
                Check(ref failures, accord.Suggestions.Contains("blancs"),
                    "la suggestion de Grammalecte (« blancs ») est au menu");
                var flat = PivotEdit.FlatText(document.Paragraphs[0]);
                Check(ref failures,
                    flat.Substring(accord.Start, accord.Length) == "blanc",
                    "l'ondulé couvre exactement « blanc »");

                var byParagraph = new System.Collections.Generic
                    .Dictionary<int, System.Collections.Generic.List<Finding>>();
                byParagraph[0] = new System.Collections.Generic
                    .List<Finding> { accord };
                engine.Current.ScreenFindings = byParagraph;
                var blue = CountSquigglePixels(engine.Current, true, 0x3B, 0x7D, 0xD8);
                var bluePaper = CountSquigglePixels(engine.Current, false, 0x3B, 0x7D, 0xD8);
                Check(ref failures, blue > 0,
                    "l'ondulé GRAMMAIRE est PRÉSENT à l'écran ("
                    + blue + " px bleus)");
                Check(ref failures, bluePaper == 0,
                    "et ABSENT du rendu papier (donc du PDF)");

                // « Ignorer cette règle » : le RuleId de Grammalecte suffit.
                host.IgnoreRule(accord.RuleId);
                var silenced = 0;
                foreach (var finding in host.Run(document, styles))
                    if (finding.CheckerId == "grammar"
                        && finding.RuleId == accord.RuleId) silenced++;
                Check(ref failures, silenced == 0,
                    "« ignorer cette règle » tait la règle (" + accord.RuleId + ")");
            }
            finally
            {
                bridge.Dispose();
            }
        }

        /// <summary>Rend la première page et compte les pixels proches de la
        /// couleur d'ondulé donnée (vert style, rouge orthographe…).</summary>
        private static int CountSquigglePixels(Composition composition, bool screenExtras,
            int red, int green, int blue)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0,
                    composition.PageWidthPx, composition.PageHeightPx));
                ComposedRenderer.DrawPage(dc, composition, 0, screenExtras);
            }
            var width = (int)Math.Ceiling(composition.PageWidthPx);
            var height = (int)Math.Ceiling(composition.PageHeightPx);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            var count = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];
                if (Math.Abs(r - red) < 55 && Math.Abs(g - green) < 55
                    && Math.Abs(b - blue) < 55
                    && !(r > 200 && g > 200 && b > 200)  // pas le papier
                    && !(r < 60 && g < 60 && b < 60))    // pas l'encre
                    count++;
            }
            return count;
        }

        private static void Check(ref int failures, bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) failures++;
        }
    }
}

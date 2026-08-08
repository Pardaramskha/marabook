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

                var screen = CountSquigglePixels(engine.Current, true);
                var paper = CountSquigglePixels(engine.Current, false);
                Check(ref failures, screen > 0,
                    "l'ondulé de style est PRÉSENT au rendu écran ("
                    + screen + " px verts)");
                Check(ref failures, paper == 0,
                    "l'ondulé est ABSENT du rendu papier (aperçu/impression)");
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE CORRECTION EN ÉCHEC : " + error);
                failures++;
            }
            return failures;
        }

        /// <summary>Rend la première page et compte les pixels proches du
        /// vert « style » (0x2E9E6B) de l'ondulé.</summary>
        private static int CountSquigglePixels(Composition composition, bool screenExtras)
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
                if (Math.Abs(r - 0x2E) < 60 && Math.Abs(g - 0x9E) < 60
                    && Math.Abs(b - 0x6B) < 60 && g > r + 30 && g > b + 30)
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

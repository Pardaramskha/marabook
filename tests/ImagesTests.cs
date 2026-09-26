using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Print;

namespace Marabook.Tests
{
    /// <summary>C42 — la refonte des images (0.50.0) : le placement d'un
    /// run image (clone, égalité, .plot v33), la géométrie pure (grille,
    /// réduction, bornes, alignements, poignées) et la pagination autour des
    /// images sur StubGlyphMetrics (5 px par caractère, lignes de 20 px,
    /// colonne de 37 caractères, 10 lignes par page) : image attachée à sa
    /// ligne (le texte passe dessous), habillage de part et d'autre (les
    /// lignes se raccourcissent), position explicite en haut de page (même le
    /// paragraphe d'avant descend), bornes de la zone et placement libre,
    /// image qui ne tient pas en bas de page (elle suit sa ligne à la page
    /// suivante), paragraphe réduit à l'image, déterminisme.</summary>
    public static class ImagesTests
    {
        private const double PxPerMm = 96.0 / 25.4;
        private const double Gap = CompositionEngine.ImageGap;

        public static void Run(Harness t)
        {
            t.Suite("C42 — refonte des images (0.50.0) : placement, habillage, pagination");
            Layout(t);
            Geometry(t);
            Attached(t);
            Wrapped(t);
            Explicit(t);
            Bounds(t);
            NextPage(t);
            ImageOnly(t);
            Determinism(t);
        }

        // ------------------------------------------------------------ outillage

        private static PageSetup Setup()
        {
            return new PageSetup
            {
                PageWidthMm = 100,
                PageHeightMm = 200.0 / PxPerMm + 40, // contenu = 200 px pile
                MarginTopMm = 20,
                MarginBottomMm = 20,
                MarginLeftMm = 25,
                MarginRightMm = 25,
                Hyphenation = false
            };
        }

        private static StyleSheet Styles()
        {
            var styles = StyleSheet.CreateDefault();
            var body = styles.Find("body");
            body.FontSize = 10;
            body.LineHeight = 20;
            body.FirstLineIndent = 0;
            body.Align = "left";
            body.HyphenationEnabled = false;
            body.KeepWithPrevious = false;
            body.KeepLinesTogether = false;
            body.KeepNextLines = 0;
            body.SpaceBefore = 0;
            body.SpaceAfter = 0;
            return styles;
        }

        private static CompositionEngine Compose(TextDocument document)
        {
            var engine = new CompositionEngine(document, Styles(), Setup(), null, false, new StubGlyphMetrics());
            engine.ComposeAll();
            return engine;
        }

        /// <summary>n mots de quatre lettres séparés d'espaces : 7 mots par ligne.</summary>
        private static string Words(int n)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < n; i++) { if (i > 0) sb.Append(' '); sb.Append("abcd"); }
            return sb.ToString();
        }

        private static TextParagraph Text(string text)
        {
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = text });
            return paragraph;
        }

        private static TextRun Image(double width, double height, string wrap, double? x, double? y, bool free)
        {
            return new TextRun
            {
                ImageId = "img",
                Image = new ImageLayout { Width = width, Height = height, Wrap = wrap, X = x, Y = y, Free = free }
            };
        }

        private static double Top(CompositionEngine engine) { return engine.Current.TopPx; }
        private static double Left(CompositionEngine engine) { return engine.Current.LeftPxFor(0); }
        private static double ColumnWidth(CompositionEngine engine) { return engine.Current.Setup.ContentWidthPx; }

        private static List<PlacedLine> LinesOf(ComposedPageLayout page, int paragraph)
        {
            var lines = new List<PlacedLine>();
            foreach (var placed in page.Lines) if (placed.ParagraphIndex == paragraph) lines.Add(placed);
            return lines;
        }

        private static double MinX(ComposedLine line)
        {
            var min = double.MaxValue;
            foreach (var piece in line.Pieces)
                if (piece.SourceStart >= 0 && !piece.IsSpace && !piece.IsAnchor) min = Math.Min(min, piece.Origin.X);
            return min;
        }

        // ------------------------------------------------------------ modèle

        private static void Layout(Harness t)
        {
            var run = Image(120, 80, ImageLayout.WrapAround, 10, null, true);
            run.Image.Name = "photo.jpg";
            var copy = PivotEdit.CloneRun(run);
            t.Check(copy.Image != null && !ReferenceEquals(copy.Image, run.Image), "CloneRun copie le placement (un objet neuf)");
            t.Check(copy.Image.SameAs(run.Image), "le placement copié est identique");
            copy.Image.X = 11;
            t.Check(!copy.Image.SameAs(run.Image), "un X différent = placement différent");
            t.Check(run.Image.IsAttached && run.Image.IsWrap, "Y nul = attachée ; wrap = de part et d'autre");
            t.Check(new TextRun { ImageId = "x" }.EnsureImage() != null, "EnsureImage crée le placement");

            var document = new TextDocument();
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "avant " });
            paragraph.Runs.Add(run);
            paragraph.Runs.Add(new TextRun { ImageId = "nu" }); // un run d'avant, sans placement
            document.Paragraphs.Add(paragraph);
            var json = PlotFile.SerializeDocument(document);
            t.Check(json.Contains("\"iw\":120") && json.Contains("\"ix\":10") && json.Contains("\"iwrap\":\"wrap\"")
                && json.Contains("\"ifree\":true") && json.Contains("\"iname\":\"photo.jpg\""),
                "les clés v33 sont écrites (iw, ix, iwrap, ifree, iname)");
            t.Check(!json.Contains("\"iy\""), "Y nul : pas de clé iy");
            var back = PlotFile.DeserializeDocument(json);
            var read = back.Paragraphs[0].Runs[1];
            t.Check(read.ImageId == "img" && read.Image != null && read.Image.SameAs(run.Image), "le placement fait l'aller-retour .plot");
            t.Check(back.Paragraphs[0].Runs[2].Image == null, "un run image sans clés reste sans placement (défauts)");

            // Le diff de versions voit une image déplacée.
            var moved = PivotEdit.Clone(document);
            moved.Paragraphs[0].Runs[1].Image.X = 99;
            t.Check(!DocumentDiff.SameStyle(document.Paragraphs[0], moved.Paragraphs[0]), "une image déplacée = paragraphe changé (versions)");
            t.Equal("avant [photo.jpg][image]", AnnotatedTextOf(document, run), "l'extrait d'une annotation d'image = son nom entre crochets (« image » sans nom)");
        }

        private static string AnnotatedTextOf(TextDocument document, TextRun run)
        {
            foreach (var r in document.Paragraphs[0].Runs) r.AnnotationId = "a";
            return document.AnnotatedText("a");
        }

        // ------------------------------------------------------------ géométrie pure

        private static void Geometry(Harness t)
        {
            var step = ImageLayout.GridStepPx;
            t.Check(Math.Abs(ImageLayout.Snap(step * 2.4, step) - step * 2) < 0.001, "Snap : 2,4 pas → 2 pas");
            t.Check(Math.Abs(ImageLayout.Snap(step * 2.6, step) - step * 3) < 0.001, "Snap : 2,6 pas → 3 pas");
            t.Check(Math.Abs(ImageLayout.GridStepPx - 5 * 96 / 25.4) < 0.001, "le pas de la grille est 0,5 cm");

            double w = 400, h = 200;
            ImageLayout.FitInside(ref w, ref h, 200, 1000);
            t.Check(Math.Abs(w - 200) < 0.001 && Math.Abs(h - 100) < 0.001, "FitInside réduit proportionnellement à la largeur");
            w = 100; h = 50;
            ImageLayout.FitInside(ref w, ref h, 200, 1000);
            t.Check(Math.Abs(w - 100) < 0.001, "FitInside n'agrandit jamais");

            var area = new Rect(10, 20, 100, 100);
            var clamped = ImageLayout.ClampInto(new Rect(-5, 200, 30, 30), area);
            t.Check(Math.Abs(clamped.X - 10) < 0.001 && Math.Abs(clamped.Y - 90) < 0.001, "ClampInto ramène aux bords");

            t.Check(Math.Abs(ImageLayout.AlignedX("right", 30, 100) - 70) < 0.001, "AlignedX right");
            t.Check(Math.Abs(ImageLayout.AlignedX("center", 30, 100) - 35) < 0.001, "AlignedX center");
            t.Check(Math.Abs(ImageLayout.AlignedY("bottom", 30, 100) - 70) < 0.001, "AlignedY bottom");
            t.Equal("left", ImageLayout.HorizontalAlignOf(0, 30, 100), "0 = left");
            t.Equal("center", ImageLayout.HorizontalAlignOf(null, 30, 100), "X nul = center");
            t.Equal("right", ImageLayout.HorizontalAlignOf(70, 30, 100), "70 = right");
            t.Check(ImageLayout.HorizontalAlignOf(12, 30, 100) == null, "12 = aucun alignement");
            t.Check(ImageLayout.VerticalAlignOf(null, 30, 100) == null, "attachée = aucun alignement vertical");
            t.Equal("center", ImageLayout.VerticalAlignOf(35, 30, 100), "35 = center vertical");

            // Poignées : le coin bas-droit, proportionnel — le haut-gauche fixe.
            var start = new Rect(10, 10, 100, 50);
            var resized = ImageLayout.Resize(start, 4, 50, 0, false);
            t.Check(Math.Abs(resized.X - 10) < 0.001 && Math.Abs(resized.Y - 10) < 0.001, "Resize coin bas-droit : le haut-gauche ne bouge pas");
            t.Check(Math.Abs(resized.Width - 150) < 0.001 && Math.Abs(resized.Height - 75) < 0.001, "Resize proportionnel : 150 × 75");
            var freeResize = ImageLayout.Resize(start, 4, 50, 0, true);
            t.Check(Math.Abs(freeResize.Width - 150) < 0.001 && Math.Abs(freeResize.Height - 50) < 0.001, "Resize libre (Maj) : la hauteur ne suit pas");
            var leftHandle = ImageLayout.Resize(start, 7, -20, 0, false);
            t.Check(Math.Abs(leftHandle.Right - 110) < 0.001 && Math.Abs(leftHandle.Width - 120) < 0.001 && Math.Abs(leftHandle.Height - 60) < 0.001,
                "Resize poignée gauche : le bord droit fixe, la hauteur suit");
            var tiny = ImageLayout.Resize(start, 4, -200, -200, true);
            t.Check(tiny.Width >= ImageLayout.MinSizePx && tiny.Height >= ImageLayout.MinSizePx, "Resize ne descend pas sous la taille minimale");
        }

        // ------------------------------------------------------------ pagination

        private static void Attached(Harness t)
        {
            var document = new TextDocument();
            document.Paragraphs.Add(Text(Words(20))); // 3 lignes
            var anchored = Text(Words(3) + " ");
            var image = Image(100, 50, ImageLayout.WrapExclude, null, null, false);
            anchored.Runs.Add(image);
            anchored.Runs.Add(new TextRun { Text = " " + Words(11) }); // 14 mots : 7 + 7
            document.Paragraphs.Add(anchored);
            var engine = Compose(document);
            var page = engine.Current.Pages[0];
            t.Equal(1, page.Images.Count, "une image posée sur la page de son ancre");
            var placed = page.Images[0];
            t.Check(placed.Attached && ReferenceEquals(placed.Run, image), "l'image est attachée, posée pour son run");
            foreach (var line in page.Lines) t.Check(line.Line != null, "chaque ligne posée porte sa ligne composée");
            var lines = LinesOf(page, 1);
            t.Equal(2, lines.Count, "le paragraphe ancré a deux lignes posées (la ligne de l'ancre, une sous l'image)");
            var anchorLine = lines[0];
            t.Check(Math.Abs(anchorLine.Y - (Top(engine) + 60)) < 0.01, "la ligne de l'ancre suit les trois lignes du paragraphe d'avant");
            t.Check(Math.Abs(placed.Rect.Y - (anchorLine.Y + 20 + Gap)) < 0.01, "attachée : l'image pend sous la ligne de l'ancre (avec l'air)");
            t.Check(Math.Abs(placed.Rect.X - (Left(engine) + (ColumnWidth(engine) - 100) / 2)) < 0.01, "X nul : centrée dans la colonne");
            t.Check(lines[1].Y >= placed.Rect.Bottom + Gap - 0.01, "texte au-dessus et en dessous : la ligne suivante passe sous l'image");
            t.Check(lines[1].Line.End - lines[1].Line.Start > 30, "sous l'image, les lignes reprennent toute la colonne");
            // L'ancre : une pièce sans largeur, adressable (le caret y passe).
            var anchorPiece = 0;
            foreach (var piece in anchorLine.Line.Pieces) if (piece.IsAnchor) anchorPiece++;
            t.Equal(1, anchorPiece, "la ligne de l'ancre porte une pièce d'ancre");
            t.Check(anchorLine.Line.Start == 0 && anchorLine.Line.End > 15, "la ligne de l'ancre couvre le texte et l'ancre");
        }

        private static void Wrapped(Harness t)
        {
            var document = new TextDocument();
            var anchored = Text(Words(3) + " ");
            var image = Image(100, 50, ImageLayout.WrapAround, 0, null, false);
            anchored.Runs.Add(image);
            anchored.Runs.Add(new TextRun { Text = " " + Words(40) });
            document.Paragraphs.Add(anchored);
            var engine = Compose(document);
            var page = engine.Current.Pages[0];
            t.Equal(1, page.Images.Count, "wrap : l'image est posée");
            var placed = page.Images[0];
            t.Check(placed.Wrap && Math.Abs(placed.Rect.X - Left(engine)) < 0.01, "X = 0 : collée à gauche de la colonne");
            var beside = 0;
            var full = 0;
            foreach (var line in LinesOf(page, 0))
            {
                var overlaps = line.Y + line.Line.Height > placed.Rect.Y && line.Y < placed.Rect.Bottom;
                var length = line.Line.End - line.Line.Start;
                if (overlaps)
                {
                    beside++;
                    t.Check(length <= 17, "à côté de l'image, la ligne tient dans le segment restant (≤ 17 caractères)");
                    t.Check(MinX(line.Line) >= 100 + Gap - 0.01, "à côté de l'image, le texte commence à droite d'elle");
                }
                else if (line.Y >= placed.Rect.Bottom) full++;
            }
            t.Check(beside >= 2, "au moins deux lignes coulent à côté de l'image");
            t.Check(full >= 1, "après l'image, les lignes reprennent la colonne");

            // Une image qui laisse trop peu de place de part et d'autre : le
            // texte passe dessous, comme en exclusion.
            var wide = new TextDocument();
            var p = Text(Words(2) + " ");
            var big = Image(170, 40, ImageLayout.WrapAround, null, null, false);
            p.Runs.Add(big);
            p.Runs.Add(new TextRun { Text = " " + Words(20) });
            wide.Paragraphs.Add(p);
            var engine2 = Compose(wide);
            var page2 = engine2.Current.Pages[0];
            var rect = page2.Images[0].Rect;
            foreach (var line in LinesOf(page2, 0))
            {
                if (line.Y + line.Line.Height <= rect.Y) continue;
                t.Check(line.Y >= rect.Bottom + Gap - 0.01, "segments trop étroits : la ligne passe sous l'image");
            }
        }

        private static void Explicit(Harness t)
        {
            var document = new TextDocument();
            document.Paragraphs.Add(Text(Words(20))); // sans ancre
            var anchored = Text(Words(3) + " ");
            var image = Image(100, 50, ImageLayout.WrapExclude, 0, 0, false); // en haut à gauche de la zone
            anchored.Runs.Add(image);
            document.Paragraphs.Add(anchored);
            var engine = Compose(document);
            var page = engine.Current.Pages[0];
            t.Equal(1, page.Images.Count, "position explicite : l'image est posée");
            var placed = page.Images[0];
            t.Check(!placed.Attached, "Y explicite : l'image n'est plus attachée");
            t.Check(Math.Abs(placed.Rect.Y - Top(engine)) < 0.01 && Math.Abs(placed.Rect.X - Left(engine)) < 0.01, "en haut à gauche de la zone de texte");
            t.Check(page.Lines.Count > 0 && page.Lines[0].ParagraphIndex == 0, "le paragraphe d'avant reste le premier");
            t.Check(page.Lines[0].Y >= placed.Rect.Bottom + Gap - 0.01, "même le paragraphe d'avant (sans ancre) descend sous l'image");

            // Alignement vertical « bas » : l'image au bas de la zone.
            image.Image.Y = ImageLayout.AlignedY("bottom", 50, 200);
            engine.RecomposeParagraph(1);
            placed = engine.Current.Pages[0].Images[0];
            t.Check(Math.Abs(placed.Rect.Bottom - (Top(engine) + 200)) < 0.01, "alignée en bas : collée au bas de la zone de texte");
            foreach (var line in engine.Current.Pages[0].Lines)
                t.Check(line.Y + line.Line.Height <= placed.Rect.Y - Gap + 0.01, "le texte reste au-dessus d'une image posée en bas");
        }

        private static void Bounds(Harness t)
        {
            var document = new TextDocument();
            var anchored = Text(Words(3) + " ");
            var image = Image(100, 50, ImageLayout.WrapExclude, 500, 0, false);
            anchored.Runs.Add(image);
            document.Paragraphs.Add(anchored);
            var engine = Compose(document);
            var placed = engine.Current.Pages[0].Images[0];
            t.Check(Math.Abs(placed.Rect.Right - (Left(engine) + ColumnWidth(engine))) < 0.01, "X trop grand : ramenée au bord droit de la zone");

            image.Image.Free = true;
            engine.RecomposeParagraph(0);
            placed = engine.Current.Pages[0].Images[0];
            t.Check(placed.Rect.X > Left(engine) + ColumnWidth(engine) - 100 + 1, "placement libre : elle sort de la zone de texte");
            t.Check(Math.Abs(placed.Rect.Right - engine.Current.PageWidthPx) < 0.01, "placement libre : bornée par le bord de la page");

            // Trop grande pour la colonne : réduite en proportion.
            var huge = Image(600, 300, ImageLayout.WrapExclude, null, null, false);
            var p = Text(Words(2) + " ");
            p.Runs.Add(huge);
            var document2 = new TextDocument();
            document2.Paragraphs.Add(p);
            var engine2 = Compose(document2);
            var rect = engine2.Current.Pages[0].Images[0].Rect;
            t.Check(Math.Abs(rect.Width - ColumnWidth(engine2)) < 0.01 && Math.Abs(rect.Height - rect.Width / 2) < 0.01,
                "une image plus large que la colonne est réduite en proportion");
        }

        private static void NextPage(Harness t)
        {
            var document = new TextDocument();
            document.Paragraphs.Add(Text(Words(56))); // 8 lignes
            var anchored = Text(Words(2) + " ");
            var image = Image(100, 100, ImageLayout.WrapExclude, null, null, false);
            anchored.Runs.Add(image);
            anchored.Runs.Add(new TextRun { Text = " " + Words(20) });
            document.Paragraphs.Add(anchored);
            var engine = Compose(document);
            var pages = engine.Current.Pages;
            t.Check(pages.Count >= 2, "l'image qui ne tient pas en bas de page ouvre une page");
            t.Equal(0, pages[0].Images.Count, "page 1 : aucune image (elle ne tenait pas)");
            t.Equal(8, pages[0].Lines.Count, "page 1 : les huit lignes du premier paragraphe seulement");
            t.Equal(1, pages[1].Images.Count, "page 2 : l'image y est, avec sa ligne");
            var first = pages[1].Lines[0];
            t.Check(first.ParagraphIndex == 1 && first.Line.Start == 0 && Math.Abs(first.Y - Top(engine)) < 0.01,
                "page 2 : la ligne de l'ancre ouvre la page");
            t.Check(Math.Abs(pages[1].Images[0].Rect.Y - (Top(engine) + 20 + Gap)) < 0.01, "page 2 : l'image pend sous sa ligne");
        }

        private static void ImageOnly(Harness t)
        {
            var document = new TextDocument();
            document.Paragraphs.Add(Text(Words(7))); // 1 ligne
            var only = new TextParagraph();
            only.Runs.Add(Image(80, 60, ImageLayout.WrapExclude, null, null, false));
            document.Paragraphs.Add(only);
            document.Paragraphs.Add(Text(Words(7)));
            var engine = Compose(document);
            var page = engine.Current.Pages[0];
            var placed = page.Images[0];
            var anchorLine = LinesOf(page, 1)[0];
            t.Check(Math.Abs(placed.Rect.Y - anchorLine.Y) < 0.01, "paragraphe réduit à l'image (import docx) : l'image prend la place de sa ligne vide");
            var after = LinesOf(page, 2)[0];
            t.Check(after.Y >= placed.Rect.Bottom + Gap - 0.01, "le paragraphe suivant passe sous l'image");
        }

        private static void Determinism(Harness t)
        {
            var document = new TextDocument();
            document.Paragraphs.Add(Text(Words(20)));
            var anchored = Text(Words(3) + " ");
            var image = Image(100, 50, ImageLayout.WrapAround, 0, 30, false);
            anchored.Runs.Add(image);
            anchored.Runs.Add(new TextRun { Text = " " + Words(40) });
            document.Paragraphs.Add(anchored);
            document.Paragraphs.Add(Text(Words(20)));
            var engine = Compose(document);
            var before = Snapshot(engine);
            var unchanged = engine.Repaginate();
            t.Equal(int.MaxValue, unchanged, "repaginer sans rien changer ne signale aucune page");
            t.Equal(before, Snapshot(engine), "deux paginations donnent le même placement");
            image.Image.Y = 90;
            var first = engine.Repaginate();
            t.Equal(0, first, "déplacer l'image signale sa page");
            t.Check(before != Snapshot(engine), "le placement a changé avec l'image");
        }

        private static string Snapshot(CompositionEngine engine)
        {
            var sb = new StringBuilder();
            foreach (var page in engine.Current.Pages)
            {
                foreach (var line in page.Lines)
                    sb.Append(line.ParagraphIndex).Append(':').Append(line.Line.Start).Append('-').Append(line.Line.End)
                        .Append('@').Append(Math.Round(line.Y, 2)).Append(' ');
                foreach (var image in page.Images)
                    sb.Append("[img ").Append(Math.Round(image.Rect.X, 2)).Append(',').Append(Math.Round(image.Rect.Y, 2)).Append("] ");
                sb.Append('|');
            }
            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Print;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests.Ui
{
    /// <summary>Sonde de la refonte des images (0.50.0) sur vraie MainWindow
    /// hors écran : un écrit avec une image insérée (un PNG fabriqué ici) —
    /// l'onglet Image du ruban entre Texte et Insertion ; l'image se pose
    /// sur la page de son ancre et le texte passe dessous ; sélectionnée,
    /// elle a ses poignées, le rail Général montre ses propriétés ; les
    /// raccourcis d'alignement, l'habillage de part et d'autre, le placement
    /// libre et la grille ; l'annotation d'une image ; annulation ; le .plot
    /// garde le placement ; la désélection rend le Général. Rend des PNG dans
    /// %TEMP% (marabook-images-*.png). Règle du batch 11 : settings.json est
    /// l'affaire de l'appelant (A1Probe) — ou de Main en solo.</summary>
    public static class ImagesProbe
    {
        private static int _failures;

        [STAThread]
        public static int Main()
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Marabook", "settings.json");
            var backup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            try { Run(); }
            finally
            {
                try
                {
                    if (backup != null) File.WriteAllBytes(settingsPath, backup);
                    else if (File.Exists(settingsPath)) File.Delete(settingsPath);
                }
                catch { }
            }
            if (Application.Current != null) Application.Current.Shutdown();
            Console.WriteLine(_failures == 0 ? "Sonde images OK." : "*** SONDE IMAGES EN ECHEC : " + _failures);
            return _failures == 0 ? 0 : 1;
        }

        public static int Run()
        {
            Console.WriteLine("== Sonde des images (0.50.0)");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-images");
            try
            {
                Directory.CreateDirectory(dir);
                Probe(Path.Combine(dir, "images.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE IMAGES EN ÉCHEC : " + error);
                _failures++;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
            return _failures;
        }

        /// <summary>Un PNG de 900 × 560 (plus large que la colonne A4) : un
        /// dégradé et un disque, fabriqué par WPF.</summary>
        private static byte[] MakePng()
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x5B, 0x67, 0xD8), Color.FromRgb(0xF2, 0xC9, 0x4C), 30), null,
                    new Rect(0, 0, 900, 560));
                dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 5), new Point(450, 280), 170, 170);
            }
            var bitmap = new RenderTargetBitmap(900, 560, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private static string Lorem(int sentences)
        {
            var parts = new List<string>();
            for (var i = 0; i < sentences; i++)
                parts.Add("Le vent portait l'odeur des pins jusqu'au village, et personne ne songeait encore à fermer les volets.");
            return string.Join(" ", parts.ToArray());
        }

        private static void Probe(string path)
        {
            var project = Project.CreateNew();
            var text = project.Category(Project.KeyWritings).Children[0];
            text.Title = "Chapitre illustré";
            var document = new TextDocument();
            for (var i = 0; i < 14; i++) document.Paragraphs.Add(Paragraph(Lorem(4))); // deux pages et plus
            var imageId = project.AddImage(MakePng(), ".png");
            var imageRun = new TextRun { ImageId = imageId, Image = new ImageLayout { Name = "paysage.png" } };
            var anchored = document.Paragraphs[1];
            anchored.Runs.Insert(1, imageRun);
            text.Document = document;
            project.RelinkParents();
            PlotFile.Save(project, path);

            AppSettings.Load();
            AppSettings.DraftView = false;
            AppSettings.RightPanel = RightPanel.Inspector;
            AppSettings.ImageGrid = false;
            AppSettings.DarkTheme = false;
            Chrome.Toggle(false);
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Apply(application);
            var window = new MainWindow
            {
                WindowState = WindowState.Normal,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2600,
                Top = 0,
                Width = 1280,
                Height = 860,
                ShowInTaskbar = false
            };
            try
            {
                window.Show();
                DoEvents();
                window.OpenFile(path);
                DoEvents();
                var opened = (Project)GetField(window, "_project");
                BinderItem target = null;
                foreach (var item in opened.AllItems()) if (item.Title == "Chapitre illustré") target = item;
                Invoke(window, "OnBinderSelection", new object[] { target });
                DoEvents();
                var editor = (EditorView)GetField(window, "_editor");
                var composed = (ComposedView)GetField(editor, "_composed");
                Check(editor.Visibility == Visibility.Visible && editor.ComposedActive, "l'écrit illustré s'ouvre en pages composées");

                // — Le ruban : l'onglet Image entre Texte et Insertion.
                var tabs = FindTabControl(editor);
                var headers = new List<string>();
                foreach (TabItem tab in tabs.Items) headers.Add((string)tab.Header);
                Check(headers.IndexOf("Image") == headers.IndexOf("Texte") + 1 && headers.IndexOf("Insertion") == headers.IndexOf("Image") + 1,
                    "l'onglet Image est entre Texte et Insertion (" + string.Join("/", headers.ToArray()) + ")");
                var imageTab = TabContent(tabs, "Image");
                Check(FindByToolTipStart(imageTab, "Insère une image") != null, "l'onglet Image a son grand carré d'insertion");
                var texteTab = TabContent(tabs, "Texte");
                Check(FindByToolTipStart(texteTab, "Insérer une image") == null, "l'onglet Texte n'a plus le bouton d'image");
                tabs.SelectedIndex = headers.IndexOf("Image");
                DoEvents();

                // — L'image est posée sur la page de son ancre, le texte dessous.
                var composition = composed.CurrentComposition;
                var run = FindImageRun(target.Document);
                int pageIndex;
                var placed = FindPlaced(composition, run, out pageIndex);
                Check(placed != null && placed.Source != null, "l'image est posée sur une page, décodée");
                var textBelow = true;
                var anchorLineSeen = false;
                foreach (var line in composition.Pages[pageIndex].Lines)
                {
                    var overlaps = line.Y + line.Line.Height > placed.Rect.Y + 0.5 && line.Y < placed.Rect.Bottom - 0.5;
                    if (overlaps) textBelow = false;
                    foreach (var piece in line.Line.Pieces) if (piece.IsAnchor) anchorLineSeen = true;
                }
                Check(textBelow && anchorLineSeen, "texte au-dessus et en dessous : aucune ligne ne chevauche l'image, l'ancre est dans une ligne");
                Check(Math.Abs(placed.Rect.Width - composition.Setup.ContentWidthPx) < 0.5
                    && Math.Abs(placed.Rect.Height - placed.Rect.Width * 560 / 900) < 0.5,
                    "900 px de large : réduite à la colonne, en proportion (" + placed.Rect.Width.ToString("0") + " px)");
                var freeButton = FindToggleByLabel(imageTab, "Placement libre");
                Check(freeButton != null && !freeButton.IsEnabled, "sans image sélectionnée, les boutons de l'onglet Image sont inertes");

                // — Sélection : poignées, rail Général remplacé par les propriétés.
                var inspector = (Border)GetField(window, "_inspector");
                var defaultChild = inspector.Child;
                Check(composed.SelectImageRun(run), "l'image se sélectionne");
                DoEvents();
                var overlay = (Canvas)GetField(composed, "_overlay");
                var handles = 0;
                foreach (UIElement child in overlay.Children)
                {
                    var rect = child as System.Windows.Shapes.Rectangle;
                    if (rect != null && Math.Abs(rect.Width - 7) < 0.01 && Math.Abs(rect.Height - 7) < 0.01) handles++;
                }
                Check(handles == 8, "huit poignées autour de l'image sélectionnée (" + handles + ")");
                Check(!ReferenceEquals(inspector.Child, defaultChild) && ContainsLabel(inspector.Child, "Image")
                    && ContainsLabel(inspector.Child, "Format"), "le rail Général montre les propriétés de l'image");
                var nameBox = FindTextBox(inspector.Child);
                Check(nameBox != null && nameBox.Text == "paysage.png", "le nom de l'image est affiché (paysage.png)");
                Check(FindByToolTipStart(inspector.Child, "Enregistre l'image") != null, "le rail propose « Enregistrer l'image… »");
                Check(freeButton.IsEnabled, "avec une image sélectionnée, les boutons de l'onglet Image s'activent");
                var info = editor.SelectedImageInfo();
                Check(info != null && info.PixelWidth == 900 && info.PixelHeight == 560 && info.Extension == ".png",
                    "les propriétés lues : 900 × 560 px, PNG");
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-images-1-selection.png"));

                // — La souris, rejouée par points : la poignée bas-droite réduit
                // l'image en proportion (le haut-gauche ne bouge pas) ; puis un
                // glisser la déplace et la détache de sa ligne.
                var pageTop0 = pageIndex * (composition.PageHeightPx + 18);
                var startRect = placed.Rect;
                Check(composed.ImagePressAt(new Point(startRect.Right, pageTop0 + startRect.Bottom)), "presser la poignée bas-droite");
                composed.ImageDragTo(new Point(startRect.Right - 300, pageTop0 + startRect.Bottom - 100), false);
                composed.ImageReleaseAt(new Point(startRect.Right - 300, pageTop0 + startRect.Bottom - 100));
                DoEvents();
                placed = FindPlaced(composed.CurrentComposition, run, out pageIndex);
                Check(placed != null && Math.Abs(placed.Rect.X - startRect.X) < 0.5 && Math.Abs(placed.Rect.Y - startRect.Y) < 0.5,
                    "redimensionnée par la poignée bas-droite : le coin haut-gauche ne bouge pas");
                Check(placed != null && Math.Abs(placed.Rect.Width - (startRect.Width - 300)) < 0.5
                    && Math.Abs(placed.Rect.Height - placed.Rect.Width * startRect.Height / startRect.Width) < 0.5,
                    "…et les proportions sont gardées (" + placed.Rect.Width.ToString("0") + " × " + placed.Rect.Height.ToString("0") + ")");
                Check(run.Image.IsAttached, "redimensionner ne détache pas l'image de sa ligne");
                // Le rail suit la taille en direct (retour de Rémi, 26/09).
                var sizeShown = FindLabelStartingWith(inspector.Child, "cm");
                var expected = (placed.Rect.Width / PageSetup.PxPerMm / 10).ToString("0.0") + " × " + (placed.Rect.Height / PageSetup.PxPerMm / 10).ToString("0.0") + " cm";
                Check(sizeShown != null && sizeShown.Text == expected, "le rail affiche la nouvelle taille (" + (sizeShown == null ? "-" : sizeShown.Text) + " ; attendu " + expected + ")");
                var center = new Point(placed.Rect.X + placed.Rect.Width / 2, pageTop0 + placed.Rect.Y + placed.Rect.Height / 2);
                var moved = placed.Rect;
                Check(composed.ImagePressAt(center), "presser au milieu de l'image");
                composed.ImageDragTo(new Point(center.X + 40, center.Y + 120), false);
                composed.ImageReleaseAt(new Point(center.X + 40, center.Y + 120));
                DoEvents();
                placed = FindPlaced(composed.CurrentComposition, run, out pageIndex);
                Check(placed != null && Math.Abs(placed.Rect.X - moved.X - 40) < 0.5 && Math.Abs(placed.Rect.Y - moved.Y - 120) < 0.5,
                    "glissée de (40, 120) : l'image a suivi le pointeur");
                Check(!run.Image.IsAttached && run.Image.Y.HasValue, "glisser détache l'image de sa ligne (position explicite)");
                var overlapAfterMove = false;
                foreach (var line in composed.CurrentComposition.Pages[pageIndex].Lines)
                    if (line.Y + line.Line.Height > placed.Rect.Y + 0.5 && line.Y < placed.Rect.Bottom - 0.5) overlapAfterMove = true;
                Check(!overlapAfterMove, "le texte s'est réécarté autour de la nouvelle position");
                Check(composed.CanUndo, "le glisser est un cran d'annulation");

                // — Aligner à droite avec une taille réduite, puis de part et d'autre.
                run.Image.Width = 200;
                run.Image.Height = 125;
                Invoke(composed, "ApplyImageEdit", new object[] { pageIndex });
                composed.AlignSelectedImage("right", null);
                DoEvents();
                placed = FindPlaced(composed.CurrentComposition, run, out pageIndex);
                var left = composed.CurrentComposition.LeftPxFor(pageIndex);
                Check(placed != null && Math.Abs(placed.Rect.Right - (left + composition.Setup.ContentWidthPx)) < 0.5,
                    "alignée à droite : collée au bord droit de la zone de texte");
                string h, v, wrap; bool free;
                composed.SelectedImageState(out h, out v, out wrap, out free);
                Check(h == "right" && wrap == ImageLayout.WrapExclude && !free, "l'état lu pour le ruban : droite, exclusion");
                var rightToggle = FindToggleByTooltip(imageTab, "Image à droite de la zone de texte");
                Check(rightToggle != null && rightToggle.IsChecked == true, "le bouton « à droite » est coché");
                composed.SetSelectedImageWrap(ImageLayout.WrapAround);
                DoEvents();
                composition = composed.CurrentComposition;
                placed = FindPlaced(composition, run, out pageIndex);
                var beside = 0;
                foreach (var line in composition.Pages[pageIndex].Lines)
                {
                    var overlaps = line.Y + line.Line.Height > placed.Rect.Y + 0.5 && line.Y < placed.Rect.Bottom - 0.5;
                    if (!overlaps) continue;
                    beside++;
                    var maxRight = 0.0;
                    foreach (var piece in line.Line.Pieces) maxRight = Math.Max(maxRight, left + piece.Origin.X + piece.VisualWidth());
                    Check(maxRight <= placed.Rect.X - CompositionEngine.ImageGap + 0.5, "de part et d'autre : le texte s'arrête avant l'image");
                }
                Check(beside >= 3, "plusieurs lignes coulent à gauche de l'image (" + beside + ")");
                composed.AlignSelectedImage(null, "top");
                composed.SetImageGrid(true);
                DoEvents();
                var gridPaths = 0;
                foreach (UIElement child in overlay.Children) if (child is System.Windows.Shapes.Path) gridPaths++;
                Check(gridPaths == 2, "la grille de placement est dessinée (trame + repères du centre)");
                placed = FindPlaced(composed.CurrentComposition, run, out pageIndex);
                Check(placed != null && Math.Abs(placed.Rect.Y - composition.TopPx) < 0.5 && !placed.Attached, "alignée en haut : collée au haut de la zone de texte");
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-images-2-wrap-grille.png"));

                // — Une flèche avec la grille : l'image se pose sur le trait de
                // grille précédent (elle est collée à droite : vers la gauche).
                var before = placed.Rect;
                Invoke(composed, "NudgeSelectedImage", new object[] { -ImageLayout.GridStepPx, 0.0 });
                DoEvents();
                placed = FindPlaced(composed.CurrentComposition, run, out pageIndex);
                var onGrid = placed != null && Math.Abs(placed.Rect.X / ImageLayout.GridStepPx - Math.Round(placed.Rect.X / ImageLayout.GridStepPx)) < 0.02;
                Check(onGrid && placed.Rect.X < before.X - 0.5 && placed.Rect.X > before.X - ImageLayout.GridStepPx - 0.5,
                    "une flèche avec la grille : l'image se pose sur le trait précédent");
                Invoke(composed, "NudgeSelectedImage", new object[] { -ImageLayout.GridStepPx, 0.0 });
                DoEvents();
                var again = FindPlaced(composed.CurrentComposition, run, out pageIndex);
                Check(again != null && Math.Abs(placed.Rect.X - again.Rect.X - ImageLayout.GridStepPx) < 0.02,
                    "sur la grille, la flèche suivante avance d'un pas entier (0,5 cm)");
                placed = again;

                // — Glissée sur la page suivante : l'ancre suit, l'image y est posée.
                var pagesBefore = composed.CurrentComposition.Pages.Count;
                Check(pagesBefore >= 2, "l'écrit tient sur au moins deux pages (" + pagesBefore + ")");
                var stride = composed.CurrentComposition.PageHeightPx + 18;
                var grab = new Point(placed.Rect.X + placed.Rect.Width / 2, pageIndex * stride + placed.Rect.Y + placed.Rect.Height / 2);
                Check(composed.ImagePressAt(grab), "presser l'image pour la porter sur la page suivante");
                composed.ImageDragTo(new Point(grab.X, grab.Y + stride), false);
                composed.ImageReleaseAt(new Point(grab.X, grab.Y + stride));
                DoEvents();
                int newPage;
                placed = FindPlaced(composed.CurrentComposition, run, out newPage);
                Check(placed != null && newPage == pageIndex + 1, "lâchée sur la page suivante : l'image y est posée (page " + (newPage + 1) + ")");
                Check(placed != null && Math.Abs(placed.Rect.Y - composed.CurrentComposition.TopPx) < 0.5, "…en haut de sa nouvelle page, comme sur l'ancienne");
                var anchorParagraph = -1;
                for (var p = 0; p < target.Document.Paragraphs.Count; p++)
                    if (target.Document.Paragraphs[p].Runs.Contains(run)) anchorParagraph = p;
                var anchorOnNewPage = false;
                foreach (var line in composed.CurrentComposition.Pages[newPage].Lines)
                    if (line.ParagraphIndex == anchorParagraph) anchorOnNewPage = true;
                Check(anchorOnNewPage, "…et son ancre est passée dans un paragraphe de cette page");
                composed.Undo();
                DoEvents();
                run = FindImageRun(target.Document);
                composed.SelectImageRun(run);
                placed = FindPlaced(composed.CurrentComposition, run, out pageIndex);
                Check(placed != null && pageIndex == newPage - 1, "Ctrl+Z ramène l'image et son ancre sur leur page");
                Check((bool)GetField(window, "_dirty"), "le projet est marqué modifié");

                // — Placement libre : l'image peut sortir de la zone (dans la marge).
                composed.SetSelectedImageFree(true);
                run.Image.X = -60; // dans le petit fond
                Invoke(composed, "ApplyImageEdit", new object[] { pageIndex });
                DoEvents();
                placed = FindPlaced(composed.CurrentComposition, run, out pageIndex);
                Check(placed != null && placed.Rect.X < left - 1, "placement libre : l'image mord dans la marge");
                composed.SetSelectedImageFree(false);
                DoEvents();
                placed = FindPlaced(composed.CurrentComposition, run, out pageIndex);
                Check(placed != null && placed.Rect.X >= left - 0.5, "placement libre éteint : l'image revient dans la zone");
                composed.SetImageGrid(false);

                // — Annoter l'image (onglet Révision) : l'ancre porte l'annotation.
                editor.CreateAnnotation();
                DoEvents();
                Check(run.AnnotationId != null && target.Document.FindAnnotation(run.AnnotationId) != null,
                    "« Annoter » avec une image sélectionnée ancre l'annotation sur l'image");
                Check(editor.SelectedImageInfo() != null, "l'image reste sélectionnée après l'annotation");
                var excerpt = target.Document.AnnotatedText(run.AnnotationId);
                Check(excerpt == "[paysage.png]", "l'extrait de l'annotation est le nom de l'image (" + excerpt + ")");
                Check(composed.AnnotationAnchorY(run.AnnotationId) >= 0, "la bulle trouve la hauteur de l'image");
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-images-3-annotation.png"));

                // — Désélection : le Général revient.
                composed.DeselectImage(true);
                DoEvents();
                Check(ReferenceEquals(inspector.Child, defaultChild), "désélectionnée : le rail Général revient");
                Check(!freeButton.IsEnabled, "désélectionnée : l'onglet Image redevient inerte");
                Check(composed.GoToAnnotation(run.AnnotationId) && editor.SelectedImageInfo() != null,
                    "aller à l'annotation d'une image la resélectionne");

                // — Le .plot garde le placement.
                Invoke(window, "DoSave", null);
                DoEvents();
                var reloaded = PlotFile.Load(path);
                TextRun back = null;
                foreach (var item in reloaded.AllItems())
                    if (item.Title == "Chapitre illustré") back = FindImageRun(item.Document);
                Check(back != null && back.Image != null && back.Image.Wrap == ImageLayout.WrapAround
                    && Math.Abs(back.Image.Width - 200) < 0.01 && back.Image.Y.HasValue && back.AnnotationId == run.AnnotationId
                    && back.Image.Name == "paysage.png", "le .plot rendu garde habillage, taille, position, nom et annotation");

                // — Annuler : les gestes se défont.
                var undone = 0;
                while (composed.CanUndo && undone < 20) { composed.Undo(); undone++; }
                DoEvents();
                var restored = FindImageRun(target.Document);
                Check(restored != null && (restored.Image == null || restored.Image.IsAttached) && restored.AnnotationId == null,
                    "tout annulé : l'image revient attachée à sa ligne, sans annotation");

                // — Supprimer l'image sélectionnée.
                composed.SelectImageRun(restored);
                composed.DeleteSelectedImage();
                DoEvents();
                Check(FindImageRun(target.Document) == null && composed.SelectedImage == null, "Suppr efface l'image et son ancre");
                Check(composed.CurrentComposition.Pages[0].Images.Count == 0, "plus d'image posée après suppression");
                composed.Undo();
                DoEvents();
                Check(FindImageRun(target.Document) != null, "Ctrl+Z ramène l'image");

                SetField(window, "_dirty", false);
            }
            finally
            {
                SetField(window, "_dirty", false);
                window.Close();
                DoEvents();
            }
        }

        private static TextParagraph Paragraph(string text)
        {
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = text });
            return paragraph;
        }

        private static TextRun FindImageRun(TextDocument document)
        {
            foreach (var paragraph in document.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.ImageId != null) return run;
            return null;
        }

        private static PlacedImage FindPlaced(Composition composition, TextRun run, out int pageIndex)
        {
            pageIndex = 0;
            for (var k = 0; k < composition.Pages.Count; k++)
                foreach (var image in composition.Pages[k].Images)
                    if (ReferenceEquals(image.Run, run)) { pageIndex = k; return image; }
            return null;
        }

        // ------------------------------------------------------------ outillage WPF

        private static TabControl FindTabControl(DependencyObject root)
        {
            if (root is TabControl) return (TabControl)root;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindTabControl(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private static UIElement TabContent(TabControl tabs, string header)
        {
            foreach (TabItem tab in tabs.Items)
                if ((string)tab.Header == header) return tab.Content as UIElement;
            return null;
        }

        private static FrameworkElement FindByToolTipStart(DependencyObject root, string start)
        {
            var element = root as FrameworkElement;
            if (element != null && element.ToolTip is string && ((string)element.ToolTip).StartsWith(start)) return element;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var found = child is DependencyObject ? FindByToolTipStart((DependencyObject)child, start) : null;
                if (found != null) return found;
            }
            return null;
        }

        private static System.Windows.Controls.Primitives.ToggleButton FindToggleByTooltip(DependencyObject root, string tip)
        {
            return FindByToolTipStart(root, tip) as System.Windows.Controls.Primitives.ToggleButton;
        }

        private static System.Windows.Controls.Primitives.ToggleButton FindToggleByLabel(DependencyObject root, string label)
        {
            var toggle = root as System.Windows.Controls.Primitives.ToggleButton;
            if (toggle != null && ContainsLabel(toggle, label)) return toggle;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var found = child is DependencyObject ? FindToggleByLabel((DependencyObject)child, label) : null;
                if (found != null) return found;
            }
            return null;
        }

        private static TextBox FindTextBox(DependencyObject root)
        {
            if (root is TextBox) return (TextBox)root;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var found = child is DependencyObject ? FindTextBox((DependencyObject)child) : null;
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Le premier TextBlock dont le texte se termine par un suffixe.</summary>
        private static TextBlock FindLabelStartingWith(DependencyObject root, string suffix)
        {
            var block = root as TextBlock;
            if (block != null && block.Text.EndsWith(suffix)) return block;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                var found = child is DependencyObject ? FindLabelStartingWith((DependencyObject)child, suffix) : null;
                if (found != null) return found;
            }
            return null;
        }

        private static bool ContainsLabel(DependencyObject root, string label)
        {
            if (root == null) return false;
            var block = root as TextBlock;
            if (block != null && block.Text == label) return true;
            foreach (var child in LogicalTreeHelper.GetChildren(root))
                if (child is DependencyObject && ContainsLabel((DependencyObject)child, label)) return true;
            return false;
        }

        private static void RenderPng(FrameworkElement element, string path)
        {
            try
            {
                var width = (int)Math.Ceiling(element.ActualWidth);
                var height = (int)Math.Ceiling(element.ActualHeight);
                if (width <= 0 || height <= 0) return;
                element.UpdateLayout();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                Console.WriteLine("  (rendu : " + path + ")");
            }
            catch (Exception error)
            {
                Console.WriteLine("  (rendu PNG impossible : " + error.Message + ")");
            }
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null) return field.GetValue(target);
                type = type.BaseType;
            }
            throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
        }

        private static void SetField(object target, string name, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }
            throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
        }

        private static object Invoke(object target, string name, object[] args)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            var method = target.GetType().GetMethod(name, flags | BindingFlags.DeclaredOnly)
                ?? target.GetType().GetMethod(name, flags);
            if (method == null)
                throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return method.Invoke(target, args);
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
}

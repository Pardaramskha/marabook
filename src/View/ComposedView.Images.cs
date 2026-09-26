using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.View
{
    /// <summary>ComposedView, partie « images » (refonte des images, 0.50.0) :
    /// la sélection d'une image au clic, son cadre et ses huit poignées, le
    /// glisser-déposer sur la page (et d'une page à l'autre : l'ancre suit),
    /// le redimensionnement proportionnel (Maj = libre), la grille de
    /// placement aimantée (0,5 cm, repères du centre de la page), les
    /// raccourcis d'alignement, l'habillage, le placement libre, la
    /// suppression et l'annotation. Rien de tout cela ne verrouille l'image.
    /// Seul un clic ailleurs sur la surface la désélectionne — le ruban et
    /// le rail la gardent.</summary>
    public partial class ComposedView
    {
        private const double HandleSize = 7;
        private const double DragThreshold = 3;

        private TextRun _selectedImage;

        /// <summary>Le glisser en cours : déplacement (Handle = -1) ou
        /// redimensionnement par une poignée.</summary>
        private sealed class ImageDrag
        {
            public TextRun Run;
            public int Page;
            public int Handle;
            public Point Start;
            public Rect StartRect;
            public bool Moved;
        }
        private ImageDrag _imageDrag;

        /// <summary>L'image sélectionnée a changé (sélection, désélection,
        /// suppression) — le ruban et le rail suivent.</summary>
        public event Action ImageSelectionChanged;

        /// <summary>Le run de l'image sélectionnée, ou null.</summary>
        public TextRun SelectedImage { get { return _selectedImage; } }

        private void RaiseImageSelection()
        {
            var handler = ImageSelectionChanged;
            if (handler != null) handler();
        }

        // ============================================================ recherche

        /// <summary>Le run à un offset plat (celui qui le contient), ou null.</summary>
        private static TextRun RunAtFlat(TextParagraph paragraph, int offset)
        {
            int runIndex, inner;
            PivotEdit.Locate(paragraph, offset, out runIndex, out inner);
            return runIndex < paragraph.Runs.Count ? paragraph.Runs[runIndex] : null;
        }

        /// <summary>L'image posée pour ce run, et sa page ; null si le run
        /// n'est posé nulle part (supprimé, annulé, illisible).</summary>
        private PlacedImage FindPlacedImage(TextRun run, out int pageIndex)
        {
            pageIndex = 0;
            var composition = _engine == null ? null : _engine.Current;
            if (run == null || composition == null) return null;
            for (var k = 0; k < composition.Pages.Count; k++)
                foreach (var image in composition.Pages[k].Images)
                    if (ReferenceEquals(image.Run, run))
                    {
                        pageIndex = k;
                        return image;
                    }
            return null;
        }

        /// <summary>L'image sélectionnée telle que posée (et sa page), ou null.</summary>
        public PlacedImage SelectedPlacedImage(out int pageIndex)
        {
            return FindPlacedImage(_selectedImage, out pageIndex);
        }

        /// <summary>Le paragraphe et l'offset plat de l'ancre d'un run image.</summary>
        private bool LocateRun(TextRun run, out int paragraphIndex, out int offset)
        {
            paragraphIndex = -1;
            offset = 0;
            if (_item == null || run == null) return false;
            for (var p = 0; p < _item.Document.Paragraphs.Count; p++)
            {
                var cursor = 0;
                foreach (var candidate in _item.Document.Paragraphs[p].Runs)
                {
                    if (ReferenceEquals(candidate, run))
                    {
                        paragraphIndex = p;
                        offset = cursor;
                        return true;
                    }
                    cursor += PivotEdit.IsElement(candidate) ? 1 : candidate.Text.Length;
                }
            }
            return false;
        }

        private int PageIndexAt(Point point)
        {
            var composition = _engine.Current;
            var stride = composition.PageHeightPx + PageGapPx;
            return Math.Max(0, Math.Min(composition.Pages.Count - 1, (int)Math.Floor(point.Y / stride)));
        }

        /// <summary>Les huit poignées d'un rectangle (coordonnées de page) :
        /// haut-gauche puis dans le sens horaire.</summary>
        private static Rect[] HandleRects(Rect rect)
        {
            var h = HandleSize;
            var cx = rect.X + rect.Width / 2 - h / 2;
            var cy = rect.Y + rect.Height / 2 - h / 2;
            var x0 = rect.X - h / 2;
            var x1 = rect.Right - h / 2;
            var y0 = rect.Y - h / 2;
            var y1 = rect.Bottom - h / 2;
            return new[]
            {
                new Rect(x0, y0, h, h), new Rect(cx, y0, h, h), new Rect(x1, y0, h, h),
                new Rect(x1, cy, h, h), new Rect(x1, y1, h, h), new Rect(cx, y1, h, h),
                new Rect(x0, y1, h, h), new Rect(x0, cy, h, h)
            };
        }

        /// <summary>Ce qu'il y a sous un point de la colonne : une poignée de
        /// l'image sélectionnée (handle 0–7), sinon une image (handle -1) —
        /// la dernière posée en premier (elle est dessinée dessus).</summary>
        private bool HitImage(Point point, out PlacedImage image, out int pageIndex, out int handle)
        {
            image = null;
            handle = -1;
            pageIndex = 0;
            var composition = _engine == null ? null : _engine.Current;
            if (composition == null || composition.Pages.Count == 0) return false;
            pageIndex = PageIndexAt(point);
            var inPage = new Point(point.X, point.Y - PageTop(pageIndex));
            var page = composition.Pages[pageIndex];
            if (_selectedImage != null)
            {
                var selected = FindRunOnPage(page, _selectedImage);
                if (selected != null)
                {
                    var handles = HandleRects(selected.Rect);
                    for (var i = 0; i < handles.Length; i++)
                    {
                        var zone = handles[i];
                        zone.Inflate(2, 2);
                        if (zone.Contains(inPage)) { image = selected; handle = i; return true; }
                    }
                }
            }
            for (var i = page.Images.Count - 1; i >= 0; i--)
                if (page.Images[i].Rect.Contains(inPage)) { image = page.Images[i]; return true; }
            return false;
        }

        private static PlacedImage FindRunOnPage(ComposedPageLayout page, TextRun run)
        {
            foreach (var image in page.Images)
                if (ReferenceEquals(image.Run, run)) return image;
            return null;
        }

        /// <summary>Le curseur à montrer sur ce point, ou null (texte).</summary>
        private Cursor ImageCursorAt(Point point)
        {
            PlacedImage image;
            int pageIndex, handle;
            if (!HitImage(point, out image, out pageIndex, out handle)) return null;
            switch (handle)
            {
                case 0: case 4: return Cursors.SizeNWSE;
                case 2: case 6: return Cursors.SizeNESW;
                case 1: case 5: return Cursors.SizeNS;
                case 3: case 7: return Cursors.SizeWE;
                default: return Cursors.SizeAll;
            }
        }

        // ============================================================ sélection

        /// <summary>Sélectionne l'image de ce run et l'amène à l'écran. Faux si
        /// le run n'est posé sur aucune page.</summary>
        public bool SelectImageRun(TextRun run)
        {
            int pageIndex;
            var image = FindPlacedImage(run, out pageIndex);
            if (image == null) return false;
            var changed = !ReferenceEquals(_selectedImage, run);
            _selectedImage = run;
            ClearSelection();
            _keepScroll = true;
            try { UpdateCaretVisual(); }
            finally { _keepScroll = false; }
            EnsureCaretVisible(PageTop(pageIndex) + image.Rect.Y, image.Rect.Height);
            if (changed) RaiseImageSelection();
            return true;
        }

        /// <summary>Lâche l'image sélectionnée (rien si aucune).</summary>
        public void DeselectImage(bool redraw)
        {
            if (_selectedImage == null) return;
            _selectedImage = null;
            _imageDrag = null;
            if (redraw && _engine != null)
            {
                _keepScroll = true;
                try { UpdateCaretVisual(); }
                finally { _keepScroll = false; }
            }
            RaiseImageSelection();
        }

        /// <summary>Le cadre, les poignées et la grille de l'image sélectionnée
        /// — redessinés avec le caret (UpdateCaretVisual). Une image qui n'est
        /// plus posée (annulation, suppression) lâche la sélection.</summary>
        private void DrawImageOverlay()
        {
            if (_selectedImage == null) return;
            int pageIndex;
            var image = FindPlacedImage(_selectedImage, out pageIndex);
            if (image == null)
            {
                _selectedImage = null;
                _imageDrag = null;
                RaiseImageSelection();
                return;
            }
            var pageTop = PageTop(pageIndex);
            if (Settings.AppSettings.ImageGrid) DrawGrid(pageTop);
            var rect = image.Rect;
            var frame = new System.Windows.Shapes.Rectangle
            {
                Width = rect.Width + 2,
                Height = rect.Height + 2,
                Stroke = Chrome.Accent,
                StrokeThickness = 1.2,
                SnapsToDevicePixels = true
            };
            Canvas.SetLeft(frame, rect.X - 1);
            Canvas.SetTop(frame, pageTop + rect.Y - 1);
            _overlay.Children.Add(frame);
            foreach (var handle in HandleRects(rect))
            {
                var box = new System.Windows.Shapes.Rectangle
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Fill = Chrome.PaperBg,
                    Stroke = Chrome.Accent,
                    StrokeThickness = 1.2,
                    SnapsToDevicePixels = true
                };
                Canvas.SetLeft(box, handle.X);
                Canvas.SetTop(box, pageTop + handle.Y);
                _overlay.Children.Add(box);
            }
        }

        /// <summary>La grille de placement sur la page de l'image : un trait
        /// tous les 0,5 cm depuis le coin de la page, les repères du centre
        /// horizontal et vertical en accent.</summary>
        private void DrawGrid(double pageTop)
        {
            var composition = _engine.Current;
            var width = composition.PageWidthPx;
            var height = composition.PageHeightPx;
            var step = ImageLayout.GridStepPx;
            var lines = new StreamGeometry();
            using (var ctx = lines.Open())
            {
                for (var x = step; x < width - 0.5; x += step)
                {
                    ctx.BeginFigure(new Point(x, 0), false, false);
                    ctx.LineTo(new Point(x, height), true, false);
                }
                for (var y = step; y < height - 0.5; y += step)
                {
                    ctx.BeginFigure(new Point(0, y), false, false);
                    ctx.LineTo(new Point(width, y), true, false);
                }
            }
            lines.Freeze();
            var grid = new System.Windows.Shapes.Path
            {
                Data = lines,
                Stroke = Chrome.Border,
                StrokeThickness = 0.8,
                Opacity = 0.9,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(grid, 0);
            Canvas.SetTop(grid, pageTop);
            _overlay.Children.Insert(0, grid);
            var centers = new StreamGeometry();
            using (var ctx = centers.Open())
            {
                ctx.BeginFigure(new Point(width / 2, 0), false, false);
                ctx.LineTo(new Point(width / 2, height), true, false);
                ctx.BeginFigure(new Point(0, height / 2), false, false);
                ctx.LineTo(new Point(width, height / 2), true, false);
            }
            centers.Freeze();
            var axes = new System.Windows.Shapes.Path
            {
                Data = centers,
                Stroke = Chrome.Accent,
                StrokeThickness = 1,
                Opacity = 0.7,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(axes, 0);
            Canvas.SetTop(axes, pageTop);
            _overlay.Children.Insert(1, axes);
        }

        // ============================================================ souris

        private bool ImageMouseDown(MouseButtonEventArgs e)
        {
            if (_engine == null) return false;
            if (!ImagePressAt(e.GetPosition(_pages))) return false;
            if (_imageDrag != null) CaptureMouse();
            return true;
        }

        private bool ImageMouseMove(MouseEventArgs e)
        {
            if (_imageDrag == null) return false;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _imageDrag = null;
                ReleaseMouseCapture();
                return false;
            }
            ImageDragTo(e.GetPosition(_pages), (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            return true;
        }

        private bool ImageMouseUp(MouseButtonEventArgs e)
        {
            if (_imageDrag == null) return false;
            ReleaseMouseCapture();
            ImageReleaseAt(e.GetPosition(_pages));
            return true;
        }

        /// <summary>Le bouton pressé sur un point de la colonne : vrai s'il
        /// touche une image (sélectionnée, glisser amorcé) ou une poignée.
        /// Les trois gestes prennent des points (et non des événements) pour
        /// que les sondes les rejouent sans souris.</summary>
        internal bool ImagePressAt(Point point)
        {
            PlacedImage image;
            int pageIndex, handle;
            if (!HitImage(point, out image, out pageIndex, out handle)) return false;
            SelectImageRun(image.Run);
            if (ReadOnly) return true;
            _imageDrag = new ImageDrag
            {
                Run = image.Run,
                Page = pageIndex,
                Handle = handle,
                Start = point,
                StartRect = image.Rect
            };
            return true;
        }

        /// <summary>Le pointeur bouge, bouton enfoncé : déplacement (aimanté à
        /// la grille, borné à la zone ou à la page) ou redimensionnement par
        /// la poignée (proportionnel, sauf <paramref name="shift"/>).</summary>
        internal void ImageDragTo(Point point, bool shift)
        {
            var drag = _imageDrag;
            if (drag == null) return;
            var dx = point.X - drag.Start.X;
            var dy = point.Y - drag.Start.Y;
            if (!drag.Moved && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;
            if (!drag.Moved)
            {
                drag.Moved = true;
                PushUndo(false); // un cran pour tout le glisser
            }
            var layout = drag.Run.EnsureImage();
            var composition = _engine.Current;
            var left = composition.LeftPxFor(drag.Page);
            var top = composition.TopPx;
            var area = AreaOf(drag.Page, layout.Free);
            Rect rect;
            if (drag.Handle < 0)
            {
                rect = new Rect(drag.StartRect.X + dx, drag.StartRect.Y + dy, drag.StartRect.Width, drag.StartRect.Height);
                rect = SnapToGrid(rect);
                rect = ImageLayout.ClampInto(rect, area);
                layout.X = rect.X - left;
                layout.Y = rect.Y - top; // l'image quitte sa ligne : position explicite
            }
            else
            {
                var handle = drag.Handle;
                // Une image attachée à sa ligne grandit vers le bas : ses
                // poignées du haut agissent comme celles du bas.
                if (layout.IsAttached && (handle == 0 || handle == 1 || handle == 2)) handle = 6 - handle;
                rect = ImageLayout.Resize(drag.StartRect, handle, dx, dy, shift);
                var w = rect.Width;
                var h = rect.Height;
                ImageLayout.FitInside(ref w, ref h, area.Width, area.Height);
                rect = ImageLayout.ClampInto(new Rect(rect.X, rect.Y, w, h), area);
                layout.Width = rect.Width;
                layout.Height = rect.Height;
                layout.X = rect.X - left;
                if (!layout.IsAttached) layout.Y = rect.Y - top;
            }
            ApplyImageEdit(drag.Page);
        }

        /// <summary>Le bouton relâché : fin du glisser ; lâchée sur une autre
        /// page, l'ancre de l'image y suit.</summary>
        internal void ImageReleaseAt(Point point)
        {
            var drag = _imageDrag;
            if (drag == null) return;
            _imageDrag = null;
            if (!drag.Moved) return;
            if (drag.Handle < 0)
            {
                var dropPage = PageIndexAt(point);
                if (dropPage != drag.Page)
                    RelocateImage(drag.Run, dropPage,
                        new Point(drag.StartRect.X + point.X - drag.Start.X,
                            drag.StartRect.Y + point.Y - drag.Start.Y - (dropPage - drag.Page) * (_engine.Current.PageHeightPx + PageGapPx)));
            }
            var handler = Edited;
            if (handler != null) handler();
            RaiseSelectionState();
        }

        /// <summary>La zone où l'image peut aller : le bloc de texte, ou la
        /// page entière en placement libre (coordonnées de page).</summary>
        private Rect AreaOf(int pageIndex, bool free)
        {
            var composition = _engine.Current;
            if (free) return new Rect(0, 0, composition.PageWidthPx, composition.PageHeightPx);
            return new Rect(composition.LeftPxFor(pageIndex), composition.TopPx,
                composition.Setup.ContentWidthPx,
                composition.PageHeightPx - composition.TopPx - composition.BottomPx);
        }

        /// <summary>La grille de placement, quand elle est active : le coin de
        /// l'image sur un pas de 0,5 cm depuis le coin de la page — et son
        /// centre sur celui de la page quand il en est à moins d'un demi-pas.</summary>
        private Rect SnapToGrid(Rect rect)
        {
            if (!Settings.AppSettings.ImageGrid) return rect;
            var composition = _engine.Current;
            var step = ImageLayout.GridStepPx;
            var x = ImageLayout.Snap(rect.X, step);
            var y = ImageLayout.Snap(rect.Y, step);
            var centerX = composition.PageWidthPx / 2;
            var centerY = composition.PageHeightPx / 2;
            if (Math.Abs(rect.X + rect.Width / 2 - centerX) < step / 2) x = centerX - rect.Width / 2;
            if (Math.Abs(rect.Y + rect.Height / 2 - centerY) < step / 2) y = centerY - rect.Height / 2;
            return new Rect(x, y, rect.Width, rect.Height);
        }

        /// <summary>Après un changement de placement : repagination, pages
        /// redessinées sans bouger la vue, position du modèle recalée sur ce
        /// qui a été posé (une image ramenée dans la zone garde une position
        /// vraie), ruban resynchronisé.</summary>
        private void ApplyImageEdit(int pageIndex)
        {
            var first = _engine.Repaginate();
            _keepScroll = true;
            try
            {
                RefreshPages(Math.Min(first, pageIndex));
                if (_selectedImage != null)
                {
                    int placedPage;
                    var placed = FindPlacedImage(_selectedImage, out placedPage);
                    var layout = _selectedImage.Image;
                    if (placed != null && layout != null)
                    {
                        var left = _engine.Current.LeftPxFor(placedPage);
                        var top = _engine.Current.TopPx;
                        if (layout.X.HasValue && Math.Abs(left + layout.X.Value - placed.Rect.X) > 0.01) layout.X = placed.Rect.X - left;
                        if (layout.Y.HasValue && Math.Abs(top + layout.Y.Value - placed.Rect.Y) > 0.01) layout.Y = placed.Rect.Y - top;
                    }
                }
                UpdateCaretVisual();
            }
            finally { _keepScroll = false; }
        }

        /// <summary>L'image lâchée sur une autre page : son ancre passe dans
        /// le paragraphe de la ligne la plus proche du point de dépôt, et sa
        /// position se lit dans la zone de texte de cette page.</summary>
        private void RelocateImage(TextRun run, int toPage, Point topLeftOnPage)
        {
            var composition = _engine.Current;
            if (toPage < 0 || toPage >= composition.Pages.Count) return;
            int fromParagraph, fromOffset;
            if (!LocateRun(run, out fromParagraph, out fromOffset)) return;
            var layout = run.EnsureImage();
            var area = AreaOf(toPage, layout.Free);
            var rect = ImageLayout.ClampInto(SnapToGrid(new Rect(topLeftOnPage.X, topLeftOnPage.Y,
                layout.Width > 0 ? layout.Width : area.Width / 2, layout.Height > 0 ? layout.Height : area.Height / 3)), area);
            layout.X = rect.X - composition.LeftPxFor(toPage);
            layout.Y = rect.Y - composition.TopPx;
            var firstTouched = Math.Min(toPage, FirstPageOfParagraph(fromParagraph));
            // L'ancre va d'abord dans la ligne la plus proche du point de dépôt.
            // Mais retirer l'image de sa page d'origine y libère de la place :
            // le texte remonte, et la ligne choisie peut remonter avec lui —
            // l'ancre repart alors dans la PREMIÈRE ligne de la page visée,
            // qui, elle, ne remonte plus (au plus deux fois).
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var page = _engine.Current.Pages[toPage];
                if (page.Lines.Count == 0) break;
                var target = attempt == 0 ? page.Lines[page.Lines.Count - 1] : page.Lines[0];
                if (attempt == 0)
                    foreach (var placed in page.Lines)
                        if (placed.Y + placed.Line.Height >= topLeftOnPage.Y) { target = placed; break; }
                MoveAnchor(run, target.ParagraphIndex, target.Line.Start);
                ApplyImageEdit(firstTouched);
                int placedPage;
                if (FindPlacedImage(run, out placedPage) != null && placedPage == toPage) break;
                if (toPage >= _engine.Current.Pages.Count) break;
            }
            ClampCaret();
        }

        /// <summary>Déplace l'ancre d'un run image dans un autre paragraphe, à
        /// un offset plat ; les deux paragraphes sont recomposés.</summary>
        private void MoveAnchor(TextRun run, int toParagraph, int toOffset)
        {
            int fromParagraph, fromOffset;
            if (!LocateRun(run, out fromParagraph, out fromOffset)) return;
            var document = _item.Document;
            PivotEdit.DeleteInParagraph(document.Paragraphs[fromParagraph], fromOffset, fromOffset + 1);
            if (toParagraph == fromParagraph && toOffset > fromOffset) toOffset--;
            toOffset = Math.Max(0, Math.Min(toOffset, PivotEdit.FlatLength(document.Paragraphs[toParagraph])));
            PivotEdit.InsertElement(document.Paragraphs[toParagraph], toOffset, run);
            _engine.RecomposeParagraph(fromParagraph);
            if (toParagraph != fromParagraph) _engine.RecomposeParagraph(toParagraph);
        }

        private int FirstPageOfParagraph(int paragraphIndex)
        {
            var composition = _engine.Current;
            for (var k = 0; k < composition.Pages.Count; k++)
                foreach (var placed in composition.Pages[k].Lines)
                    if (placed.ParagraphIndex == paragraphIndex) return k;
            return 0;
        }

        // ============================================================ clavier

        /// <summary>Les touches quand une image est sélectionnée : Échap la
        /// lâche, Suppr/Retour l'efface, les flèches la poussent (d'un pas de
        /// grille si la grille est active, sinon d'un pixel ; Maj = dix).</summary>
        private bool ImageKeyDown(KeyEventArgs e)
        {
            if (_selectedImage == null) return false;
            switch (e.Key)
            {
                case Key.Escape:
                    DeselectImage(true);
                    return true;
                case Key.Return:
                    DeselectImage(true); // Entrée rend la main au texte (le saut de paragraphe suit)
                    return false;
                case Key.Delete:
                case Key.Back:
                    if (!ReadOnly) DeleteSelectedImage();
                    return true;
                case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                    if (ReadOnly) return true;
                    var step = Settings.AppSettings.ImageGrid ? ImageLayout.GridStepPx
                        : ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1);
                    NudgeSelectedImage(
                        e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0,
                        e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
                    return true;
            }
            return false;
        }

        private void NudgeSelectedImage(double dx, double dy)
        {
            int pageIndex;
            var image = SelectedPlacedImage(out pageIndex);
            if (image == null) return;
            PushUndo(false);
            var layout = _selectedImage.EnsureImage();
            var composition = _engine.Current;
            var rect = new Rect(image.Rect.X + dx, image.Rect.Y + dy, image.Rect.Width, image.Rect.Height);
            if (Settings.AppSettings.ImageGrid)
            {
                // Avec la grille, une flèche pose l'image sur le trait SUIVANT
                // dans sa direction (un pas quand elle est déjà sur la grille).
                var step = ImageLayout.GridStepPx;
                var x = dx > 0 ? (Math.Floor(image.Rect.X / step + 0.001) + 1) * step
                    : dx < 0 ? (Math.Ceiling(image.Rect.X / step - 0.001) - 1) * step : image.Rect.X;
                var y = dy > 0 ? (Math.Floor(image.Rect.Y / step + 0.001) + 1) * step
                    : dy < 0 ? (Math.Ceiling(image.Rect.Y / step - 0.001) - 1) * step : image.Rect.Y;
                rect = new Rect(x, y, rect.Width, rect.Height);
            }
            rect = ImageLayout.ClampInto(rect, AreaOf(pageIndex, layout.Free));
            layout.X = rect.X - composition.LeftPxFor(pageIndex);
            layout.Y = rect.Y - composition.TopPx;
            ApplyImageEdit(pageIndex);
            var handler = Edited;
            if (handler != null) handler();
            RaiseSelectionState();
        }

        /// <summary>Efface l'image sélectionnée (son ancre quitte le texte, le
        /// caret se pose à sa place). Un cran d'annulation.</summary>
        public void DeleteSelectedImage()
        {
            int paragraphIndex, offset;
            if (_selectedImage == null || !LocateRun(_selectedImage, out paragraphIndex, out offset)) return;
            PushUndo(false);
            PivotEdit.DeleteInParagraph(_item.Document.Paragraphs[paragraphIndex], offset, offset + 1);
            _caretParagraph = paragraphIndex;
            _caretOffset = offset;
            ClearSelection();
            DeselectImage(false);
            AfterEdit(_engine.RecomposeParagraph(paragraphIndex));
        }

        // ============================================================ commandes du ruban

        /// <summary>L'état de l'image sélectionnée pour le ruban : alignement
        /// horizontal et vertical qu'elle traduit (null = aucun), habillage,
        /// placement libre. Faux sans image sélectionnée.</summary>
        public bool SelectedImageState(out string horizontal, out string vertical, out string wrap, out bool free)
        {
            horizontal = vertical = wrap = null;
            free = false;
            int pageIndex;
            var image = SelectedPlacedImage(out pageIndex);
            if (image == null) return false;
            var layout = _selectedImage.Image ?? new ImageLayout();
            var composition = _engine.Current;
            var area = AreaOf(pageIndex, false);
            horizontal = ImageLayout.HorizontalAlignOf(image.Rect.X - area.X, image.Rect.Width, area.Width);
            vertical = layout.IsAttached ? null
                : ImageLayout.VerticalAlignOf(image.Rect.Y - area.Y, image.Rect.Height, area.Height);
            wrap = layout.Wrap;
            free = layout.Free;
            return true;
        }

        /// <summary>Raccourci d'alignement dans la zone de texte : horizontal
        /// (« left », « center », « right ») et/ou vertical (« top »,
        /// « center », « bottom ») — null = inchangé. L'image reste libre de
        /// bouger ensuite.</summary>
        public void AlignSelectedImage(string horizontal, string vertical)
        {
            int pageIndex;
            var image = SelectedPlacedImage(out pageIndex);
            if (image == null || ReadOnly) return;
            PushUndo(false);
            var layout = _selectedImage.EnsureImage();
            var area = AreaOf(pageIndex, false);
            if (layout.Width <= 0) { layout.Width = image.Rect.Width; layout.Height = image.Rect.Height; }
            if (horizontal != null) layout.X = ImageLayout.AlignedX(horizontal, image.Rect.Width, area.Width);
            if (vertical != null) layout.Y = ImageLayout.AlignedY(vertical, image.Rect.Height, area.Height);
            ApplyImageEdit(pageIndex);
            var handler = Edited;
            if (handler != null) handler();
            RaiseSelectionState();
        }

        /// <summary>L'habillage de l'image sélectionnée : « exclude » (texte
        /// au-dessus et en dessous) ou « wrap » (de part et d'autre).</summary>
        public void SetSelectedImageWrap(string wrap)
        {
            int pageIndex;
            if (SelectedPlacedImage(out pageIndex) == null || ReadOnly) return;
            var layout = _selectedImage.EnsureImage();
            var value = wrap == ImageLayout.WrapAround ? ImageLayout.WrapAround : ImageLayout.WrapExclude;
            if (layout.Wrap == value) return;
            PushUndo(false);
            layout.Wrap = value;
            ApplyImageEdit(pageIndex);
            var handler = Edited;
            if (handler != null) handler();
            RaiseSelectionState();
        }

        /// <summary>Le placement libre de l'image sélectionnée : hors de la
        /// zone de texte, jusqu'aux bords de la page.</summary>
        public void SetSelectedImageFree(bool free)
        {
            int pageIndex;
            if (SelectedPlacedImage(out pageIndex) == null || ReadOnly) return;
            var layout = _selectedImage.EnsureImage();
            if (layout.Free == free) return;
            PushUndo(false);
            layout.Free = free;
            ApplyImageEdit(pageIndex);
            var handler = Edited;
            if (handler != null) handler();
            RaiseSelectionState();
        }

        /// <summary>La grille de placement (réglage d'application) : affichée
        /// sur la page de l'image sélectionnée, elle aimante les déplacements.
        /// L'éteindre laisse l'image où elle est.</summary>
        public void SetImageGrid(bool on)
        {
            if (Settings.AppSettings.ImageGrid == on) return;
            Settings.AppSettings.ImageGrid = on;
            Settings.AppSettings.Save();
            if (_engine == null) return;
            _keepScroll = true;
            try { UpdateCaretVisual(); }
            finally { _keepScroll = false; }
        }

        /// <summary>Le nom de l'image sélectionnée (rail Général).</summary>
        public void RenameSelectedImage(string name)
        {
            if (_selectedImage == null || ReadOnly) return;
            var layout = _selectedImage.EnsureImage();
            var value = string.IsNullOrEmpty(name) ? null : name.Trim();
            if (layout.Name == value) return;
            layout.Name = value;
            var handler = Edited;
            if (handler != null) handler();
        }

        /// <summary>Annote l'image sélectionnée (révision) : l'ancre porte
        /// l'annotation, comme un passage de texte.</summary>
        private bool AnnotateSelectedImage(string id)
        {
            int paragraphIndex, offset;
            if (_selectedImage == null || !LocateRun(_selectedImage, out paragraphIndex, out offset)) return false;
            PushUndo(false);
            _selectedImage.AnnotationId = id;
            _engine.RecomposeParagraph(paragraphIndex);
            _keepScroll = true;
            try { AfterEdit(0); }
            finally { _keepScroll = false; }
            return true;
        }

        /// <summary>Insère une image au caret et la sélectionne aussitôt.</summary>
        public void InsertImageAtCaret(TextRun element)
        {
            InsertElementAtCaret(element);
            SelectImageRun(element);
        }

        // ============================================================ menu contextuel

        /// <summary>« Annoter l'image » du menu contextuel : l'éditeur ouvre
        /// une annotation (Révision) sur l'image sélectionnée.</summary>
        public event Action ImageAnnotateRequested;

        /// <summary>« Enregistrer l'image… » du menu contextuel : la coquille
        /// enregistre les octets de l'image sélectionnée.</summary>
        public event Action ImageSaveRequested;

        private bool ImageContextMenu(MouseButtonEventArgs e)
        {
            if (_engine == null) return false;
            PlacedImage image;
            int pageIndex, handle;
            if (!HitImage(e.GetPosition(_pages), out image, out pageIndex, out handle)) return false;
            SelectImageRun(image.Run);
            var menu = BuildImageMenu();
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            return true;
        }

        /// <summary>Le menu d'une image sélectionnée : les mêmes options que
        /// l'onglet Image, cochées selon son état, plus annoter, enregistrer
        /// et supprimer. Public pour les sondes.</summary>
        internal ContextMenu BuildImageMenu()
        {
            var menu = new ContextMenu();
            string horizontal, vertical, wrap;
            bool free;
            SelectedImageState(out horizontal, out vertical, out wrap, out free);
            var editable = !ReadOnly;
            menu.Items.Add(MenuEntry("Aligner à gauche", horizontal == "left", editable, delegate { AlignSelectedImage("left", null); }));
            menu.Items.Add(MenuEntry("Centrer", horizontal == "center", editable, delegate { AlignSelectedImage("center", null); }));
            menu.Items.Add(MenuEntry("Aligner à droite", horizontal == "right", editable, delegate { AlignSelectedImage("right", null); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("En haut de la page", vertical == "top", editable, delegate { AlignSelectedImage(null, "top"); }));
            menu.Items.Add(MenuEntry("Au centre de la page", vertical == "center", editable, delegate { AlignSelectedImage(null, "center"); }));
            menu.Items.Add(MenuEntry("En bas de la page", vertical == "bottom", editable, delegate { AlignSelectedImage(null, "bottom"); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("Texte au-dessus et en dessous", wrap != ImageLayout.WrapAround, editable,
                delegate { SetSelectedImageWrap(ImageLayout.WrapExclude); }));
            menu.Items.Add(MenuEntry("Texte de part et d'autre", wrap == ImageLayout.WrapAround, editable,
                delegate { SetSelectedImageWrap(ImageLayout.WrapAround); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("Placement libre", free, editable, delegate { SetSelectedImageFree(!free); }));
            var grid = Settings.AppSettings.ImageGrid;
            menu.Items.Add(MenuEntry("Grille de placement", grid, true, delegate { SetImageGrid(!grid); RaiseSelectionState(); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("Annoter l'image…", false, editable, delegate
            {
                var handler = ImageAnnotateRequested;
                if (handler != null) handler();
            }));
            menu.Items.Add(MenuEntry("Enregistrer l'image…", false, true, delegate
            {
                var handler = ImageSaveRequested;
                if (handler != null) handler();
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("Supprimer l'image", false, editable, delegate { DeleteSelectedImage(); }));
            return menu;
        }

        private static MenuItem MenuEntry(string header, bool isChecked, bool enabled, Action action)
        {
            var item = new MenuItem { Header = header, IsChecked = isChecked, IsEnabled = enabled };
            item.Click += delegate { action(); };
            return item;
        }

        // ============================================================ dépôt de fichiers

        private void InitImageDrop()
        {
            AllowDrop = true;
            DragOver += delegate(object sender, DragEventArgs e)
            {
                if (_item == null || ReadOnly || _project == null || ImageFilesOf(e.Data).Count == 0) return;
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };
            Drop += delegate(object sender, DragEventArgs e)
            {
                if (_item == null || ReadOnly || _project == null) return;
                var files = ImageFilesOf(e.Data);
                if (files.Count == 0) return;
                DropImageFiles(files.ToArray(), e.GetPosition(_pages));
                e.Handled = true;
            };
        }

        /// <summary>Les fichiers image (formats que WPF lit) d'un glisser de
        /// l'Explorateur, ou une liste vide.</summary>
        private static List<string> ImageFilesOf(IDataObject data)
        {
            var result = new List<string>();
            try
            {
                if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return result;
                var files = data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return result;
                foreach (var file in files)
                {
                    var extension = System.IO.Path.GetExtension(file ?? "").ToLowerInvariant();
                    if (Array.IndexOf(Exchange.ImportedImages.Supported, extension) >= 0) result.Add(file);
                }
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>Pose des fichiers image à un point de la colonne : chaque
        /// image entre dans le magasin, son ancre va dans la ligne sous le
        /// point, sa position est celle du dépôt (les suivantes décalées) ; la
        /// dernière est sélectionnée. Un cran d'annulation. Rend le nombre
        /// d'images posées. Internal : les sondes déposent sans souris.</summary>
        internal int DropImageFiles(string[] files, Point point)
        {
            if (_item == null || _engine == null || _project == null || ReadOnly || files == null) return 0;
            var composition = _engine.Current;
            if (composition.Pages.Count == 0) return 0;
            var pageIndex = PageIndexAt(point);
            var inPage = new Point(point.X, point.Y - PageTop(pageIndex));
            var left = composition.LeftPxFor(pageIndex);
            var top = composition.TopPx;
            int paragraph, offset;
            if (!HitTestAt(point, out paragraph, out offset))
            {
                paragraph = _caretParagraph;
                offset = _caretOffset;
            }
            var placed = 0;
            TextRun last = null;
            foreach (var file in files)
            {
                byte[] bytes;
                try
                {
                    var info = new System.IO.FileInfo(file);
                    if (!info.Exists || info.Length == 0 || info.Length > 20 * 1024 * 1024) continue;
                    bytes = System.IO.File.ReadAllBytes(file);
                }
                catch (Exception) { continue; }
                if (placed == 0) PushUndo(false);
                var id = _project.AddImage(bytes, System.IO.Path.GetExtension(file));
                var run = new TextRun
                {
                    ImageId = id,
                    Image = new ImageLayout
                    {
                        Name = System.IO.Path.GetFileName(file),
                        X = Math.Max(0, inPage.X - left) + placed * 24,
                        Y = Math.Max(0, inPage.Y - top) + placed * 24
                    }
                };
                PivotEdit.InsertElement(_item.Document.Paragraphs[paragraph], offset, run);
                offset++;
                placed++;
                last = run;
            }
            if (placed == 0) return 0;
            ClearSelection();
            _engine.RecomposeParagraph(paragraph);
            ApplyImageEdit(pageIndex);
            var edited = Edited;
            if (edited != null) edited();
            if (last != null) SelectImageRun(last);
            return placed;
        }
    }
}

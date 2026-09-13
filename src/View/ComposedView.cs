using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UniversSale.Model;
using UniversSale.Print;

namespace UniversSale.View
{
    /// <summary>The home-grown editing engine's view: the document composed by
    /// the 4b motor — real justification ranges, French hyphenation, keeps —
    /// page by page, and EDITABLE: caret, click and drag selection, typing
    /// (dead keys included), Enter/Backspace/Delete, clipboard, character
    /// formatting, its own undo/redo on the pivot. « Écrire dans un livre déjà
    /// mis en page. » Black on white: print fidelity.</summary>
    public class ComposedView : ScrollViewer
    {
        private const double PageGapPx = 18;

        private readonly Grid _column;      // pages + overlay, centered
        private readonly StackPanel _pages;

        /// <summary>L'apparence du mode calme (13/09) : une VRAIE ombre portée
        /// sous chaque page (un DropShadowEffect sur un cadre vide SOUS la
        /// page — jamais sur la page, dont le texte deviendrait flou), et de
        /// l'air entre le haut de la fenêtre et la première page.</summary>
        public bool CalmLook
        {
            get { return _calmLook; }
            set
            {
                if (_calmLook == value) return;
                _calmLook = value;
                ApplyLook();
            }
        }
        private bool _calmLook;

        private void ApplyLook()
        {
            if (_column == null) return;
            _column.Margin = new Thickness(24, _calmLook ? 64 : 20, 24, 20);
            foreach (UIElement child in _pages.Children)
            {
                var slot = child as PageSlot;
                if (slot == null) continue;
                slot.Shadow.Visibility = _calmLook ? Visibility.Visible : Visibility.Collapsed;
                slot.Page.InvalidateVisual();
            }
        }

        /// <summary>Une page à l'écran : son ombre (cadre vide, effet) et sa
        /// surface (PageElement) dans la même cellule — la grille les tient
        /// à la même taille.</summary>
        private sealed class PageSlot : Grid
        {
            public readonly Border Shadow;
            public readonly PageElement Page;

            public PageSlot(ComposedView owner, int index)
            {
                Shadow = new Border
                {
                    Background = Chrome.PaperBg,
                    Visibility = owner._calmLook ? Visibility.Visible : Visibility.Collapsed,
                    IsHitTestVisible = false,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 22,
                        ShadowDepth = 4,
                        Direction = 270,
                        Opacity = 0.30,
                        Color = Colors.Black
                    }
                };
                Page = new PageElement(owner, index);
                Children.Add(Shadow);
                Children.Add(Page);
            }
        }

        private PageElement PageAt(int index)
        {
            return ((PageSlot)_pages.Children[index]).Page;
        }
        private readonly Canvas _overlay;   // caret + selection
        private readonly Canvas _bubbleLayer; // bulles d'annotation Word,
                                              // peuplées par EditorView (B.4)
        private readonly System.Windows.Shapes.Rectangle _caretBar;
        private readonly DispatcherTimer _blink;

        // Notes de bas de page éditées EN PLACE (batch 33) : un TextBox posé
        // sur la note, au bas de sa page — plus de panneau du bas.
        private readonly Canvas _noteLayer;
        private TextBox _noteEditor;
        private string _editingNoteId;
        private double _zoom = 1.0;

        private BinderItem _item;
        private StyleSheet _styles;
        private PageSetup _setup;
        private Project _project;
        private CompositionEngine _engine;

        private int _caretParagraph, _caretOffset;
        private int _anchorParagraph = -1, _anchorOffset; // -1 = no selection
        private double _caretDesiredX = -1; // column memory for up/down
        private bool _mouseSelecting;

        // Undo: pivot snapshots; typing bursts coalesce.
        private sealed class Snapshot
        {
            public TextDocument Document;
            public int Paragraph, Offset;
        }
        private readonly List<Snapshot> _undo = new List<Snapshot>();
        private readonly List<Snapshot> _redo = new List<Snapshot>();
        private DateTime _lastTyping = DateTime.MinValue;
        private bool _lastWasTyping;

        public event Action Edited;
        public event Action<string> LinkClicked;
        public event Action<int, int> PageInfoChanged;
        public event Action SelectionStateChanged; // caret/sélection ont bougé
        public event Action<string> NoteEditingStarted; // id de la note ouverte en place

        /// <summary>Pages du livre précédant ce document (0 hors livre) —
        /// folio affiché, parité des marges miroir. Pris en compte à l'Attach
        /// et au RefreshComposition.</summary>
        public int FolioOffset;

        /// <summary>Décor en-tête/pied du document (menu Gabarit, gabarit de
        /// pages appliqué). Pris en compte à l'Attach et au Refresh.</summary>
        public PageDecor Decor;

        public ComposedView()
        {
            Background = Chrome.WindowBg; // ground : le fond derrière les pages (b40)
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = true;
            FocusVisualStyle = null;

            _pages = new StackPanel();
            _overlay = new Canvas { IsHitTestVisible = false };
            _bubbleLayer = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            _caretBar = new System.Windows.Shapes.Rectangle
            {
                Width = 1.4,
                Fill = Brushes.Black,
                Visibility = Visibility.Collapsed
            };
            _overlay.Children.Add(_caretBar);
            _column = new Grid
            {
                Margin = new Thickness(24, 20, 24, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _noteLayer = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            _column.Children.Add(_pages);
            _column.Children.Add(_overlay);
            _column.Children.Add(_bubbleLayer); // bulles portées (batch 26)
            _column.Children.Add(_noteLayer);   // éditeur de note en place (b33)
            Content = _column;

            _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
            _blink.Tick += delegate
            {
                _caretBar.Visibility = _caretBar.Visibility == Visibility.Visible && _item != null
                    ? Visibility.Hidden : (_item != null ? Visibility.Visible : Visibility.Collapsed);
            };

            PreviewMouseLeftButtonDown += OnMouseDown;
            PreviewMouseRightButtonDown += OnMouseRightDown;
            PreviewMouseMove += OnMouseMoveDrag;
            PreviewMouseLeftButtonUp += delegate
            {
                _mouseSelecting = false;
                ReleaseMouseCapture();
                // A click without drag leaves no anchor behind — otherwise the
                // next keystroke would read as a one-character selection and
                // the one after would delete it.
                if (_anchorParagraph == _caretParagraph && _anchorOffset == _caretOffset)
                    ClearSelection();
            };
            PreviewTextInput += OnTextInput;
            PreviewKeyDown += OnKeyDown;
        }

        public bool HasItem { get { return _item != null; } }

        // Lecture seule (batch 38) : l'ERGONOMIE de la vue de comparaison —
        // frappe, touches d'édition, menu contextuel, annulation ignorés. La
        // SÛRETÉ, elle, est structurelle : le document comparé est synthétique
        // et son porteur n'appartient à aucun projet (CompareWindow).
        public bool ReadOnly;

        private static bool IsNavigationKey(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left: case Key.Right: case Key.Up: case Key.Down:
                case Key.Home: case Key.End: case Key.PageUp: case Key.PageDown:
                case Key.Escape: case Key.Tab:
                    return true;
                case Key.A: case Key.C:
                    return (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                default:
                    return false;
            }
        }

        /// <summary>On-screen page rectangles (zoom applied), for the rulers.</summary>
        public List<Rect> PageRects(UIElement reference)
        {
            var result = new List<Rect>();
            foreach (UIElement child in _pages.Children)
            {
                var element = child as FrameworkElement;
                if (element == null || element.ActualWidth < 1) continue;
                try
                {
                    var p0 = element.TranslatePoint(new Point(0, 0), reference);
                    var p1 = element.TranslatePoint(
                        new Point(element.ActualWidth, element.ActualHeight), reference);
                    result.Add(new Rect(p0, p1));
                }
                catch { }
            }
            return result;
        }
        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }

        // ============================================================ lifecycle

        public void Attach(BinderItem item, StyleSheet styles, PageSetup setup, Project project)
        {
            // A freshly created BinderItem carries a ZERO-paragraph document
            // (only FromPlainText seeds one). The classic surface used to
            // repair it through its flush — skipped when the composed editor
            // is already active — and LineOf would index an empty list (the
            // « Composition impossible » crash on adding a document).
            if (item.Document.Paragraphs.Count == 0)
                item.Document.Paragraphs.Add(new TextParagraph());
            _item = item;
            _styles = styles;
            _setup = setup;
            _project = project;
            // appendNotes: footnotes sit at the bottom of their page, like on
            // paper — the composed surface is print-exact.
            _engine = new CompositionEngine(item.Document, styles, setup, project, true,
                new Print.WpfGlyphMetrics());
            _engine.FolioOffset = FolioOffset;
            _engine.DefaultDecor = Decor;
            _engine.ComposeAll();
            CloseNoteEditor(false);
            _undo.Clear();
            _redo.Clear();
            _caretParagraph = 0;
            _caretOffset = 0;
            ClearSelection();
            RebuildPages();
            _blink.Start();
            UpdateCaretVisual();
            RaisePageInfo();
        }

        public void Detach()
        {
            CloseNoteEditor(false);
            _item = null;
            _engine = null;
            _blink.Stop();
            _pages.Children.Clear();
            ClearOverlay();
            _caretBar.Visibility = Visibility.Collapsed;
        }

        public void SetZoom(double factor)
        {
            var next = Math.Max(0.5, Math.Min(3.0, factor));
            if (Math.Abs(next - _zoom) < 0.0001 && _column.LayoutTransform != null == (Math.Abs(next - 1.0) >= 0.001))
                return;
            // Ancrage (batch 34) : le point de contenu au CENTRE de la vue
            // reste au centre après le changement d'échelle — sinon le
            // défilement, exprimé en pixels, glissait vers une autre page.
            // La marge de la colonne n'est pas mise à l'échelle par un
            // LayoutTransform : elle est retranchée avant, rajoutée après.
            var oldZoom = _zoom;
            var anchorY = ViewportHeight / 2;
            var anchorX = ViewportWidth / 2;
            var contentY = (VerticalOffset + anchorY - _column.Margin.Top) / oldZoom;
            var contentX = (HorizontalOffset + anchorX - _column.Margin.Left) / oldZoom;
            _zoom = next;
            _column.LayoutTransform = Math.Abs(_zoom - 1.0) < 0.001
                ? null : new ScaleTransform(_zoom, _zoom);
            if (_item == null || ViewportHeight <= 0) return;
            UpdateLayout();
            ScrollToVerticalOffset(Math.Max(0, contentY * _zoom + _column.Margin.Top - anchorY));
            ScrollToHorizontalOffset(Math.Max(0, contentX * _zoom + _column.Margin.Left - anchorX));
        }

        /// <summary>A footnote's text changed outside the engine (the notes
        /// panel): recompose the page-bottom notes only.</summary>
        public void RefreshNotes()
        {
            if (_engine == null) return;
            var firstChanged = _engine.RefreshNotes();
            if (firstChanged == int.MaxValue) return;
            RefreshPages(firstChanged);
            UpdateCaretVisual();
            RaisePageInfo();
        }

        /// <summary>Page setup or styles changed from outside.</summary>
        public void RefreshComposition()
        {
            if (_engine == null) return;
            _engine.FolioOffset = FolioOffset;
            _engine.DefaultDecor = Decor;
            _engine.ComposeAll();
            RebuildPages();
            ClampCaret();
            UpdateCaretVisual();
            RaisePageInfo();
        }

        // ============================================================ pages

        private void RebuildPages()
        {
            var composition = _engine.Current;
            while (_pages.Children.Count > composition.Pages.Count)
                _pages.Children.RemoveAt(_pages.Children.Count - 1);
            while (_pages.Children.Count < composition.Pages.Count)
                _pages.Children.Add(new PageSlot(this, _pages.Children.Count));
            // PIÈGE (batch 33) : les éléments de page survivants sont RÉUTILISÉS
            // (Attach ne détache pas) — InvalidateVisual seul redessinait la
            // nouvelle géométrie (Brouillon : 600 mm) dans une boîte MESURÉE à
            // l'ancienne (A4) : pages superposées, étendue de défilement
            // fausse. La mesure est invalidée à chaque reconstruction, et la
            // marge (l'écart entre pages) réaffirmée d'après l'index.
            for (var k = 0; k < _pages.Children.Count; k++)
            {
                var slot = (PageSlot)_pages.Children[k];
                slot.Margin = new Thickness(0, k == 0 ? 0 : PageGapPx, 0, 0);
                slot.Page.InvalidateMeasure();
                slot.Page.InvalidateVisual();
            }
            PositionNoteEditor();
        }

        // ============================================================ notes en place (b33)

        /// <summary>Les identifiants de notes dans l'ordre des APPELS (l'ordre
        /// de la liste Footnotes peut différer après couper/coller) — c'est
        /// l'index des NoteParagraphs de la composition.</summary>
        public List<string> MarkerOrder()
        {
            var order = new List<string>();
            if (_item == null) return order;
            foreach (var paragraph in _item.Document.Paragraphs)
                foreach (var run in paragraph.Runs)
                    if (run.FootnoteId != null) order.Add(run.FootnoteId);
            return order;
        }

        /// <summary>La note ouverte en place, ou null.</summary>
        public string EditingNoteId { get { return _editingNoteId; } }

        /// <summary>La note dont le texte est sous ce point (zone des notes au
        /// bas d'une page), ou null.</summary>
        private string NoteAtPoint(Point point)
        {
            var composition = _engine == null ? null : _engine.Current;
            if (composition == null || composition.Pages.Count == 0) return null;
            var stride = composition.PageHeightPx + PageGapPx;
            var pageIndex = Math.Max(0, Math.Min(composition.Pages.Count - 1, (int)(point.Y / stride)));
            var yInPage = point.Y - pageIndex * stride;
            var page = composition.Pages[pageIndex];
            foreach (var placed in page.NoteLines)
            {
                if (placed.ParagraphIndex < 0 || placed.ParagraphIndex >= composition.NoteParagraphs.Count) continue;
                var line = composition.NoteParagraphs[placed.ParagraphIndex].Lines[placed.LineIndex];
                if (yInPage < placed.Y - 1 || yInPage > placed.Y + line.Height + 1) continue;
                var order = MarkerOrder();
                return placed.ParagraphIndex < order.Count ? order[placed.ParagraphIndex] : null;
            }
            return null;
        }

        /// <summary>L'appel de note (exposant) sous le clic : le curseur est
        /// tombé sur l'une des deux bornes de la marque ET le point est dans
        /// l'empreinte horizontale de la marque.</summary>
        private string MarkerAt(int paragraphIndex, int offset, Point point)
        {
            if (_item == null || paragraphIndex < 0 || paragraphIndex >= _item.Document.Paragraphs.Count)
                return null;
            var paragraph = _item.Document.Paragraphs[paragraphIndex];
            var pos = 0;
            foreach (var run in paragraph.Runs)
            {
                var length = PivotEdit.IsElement(run) ? 1 : run.Text.Length;
                if (run.FootnoteId != null && (offset == pos || offset == pos + 1))
                {
                    // Empreinte horizontale de la marque sur sa ligne.
                    int pageIndex; double lineY;
                    var line = LineOf(paragraphIndex, pos, out pageIndex, out lineY);
                    if (line == null) return null;
                    var left = _engine.Current.LeftPxFor(pageIndex);
                    var x0 = CaretX(line, pos, left);
                    var x1 = CaretX(line, pos + 1, left);
                    if (x1 < x0) { var t = x0; x0 = x1; x1 = t; }
                    return point.X >= x0 - 2 && point.X <= x1 + 2 ? run.FootnoteId : null;
                }
                pos += length;
            }
            return null;
        }

        /// <summary>Ouvre la note en place : un champ posé exactement sur son
        /// texte au bas de sa page ; la frappe recompose les notes ; Entrée,
        /// Échap ou un clic ailleurs referment.</summary>
        public void EditNote(string id)
        {
            if (_item == null || _engine == null || id == null) return;
            var note = _item.Document.FindFootnote(id);
            if (note == null) return;
            if (_editingNoteId == id && _noteEditor != null) { _noteEditor.Focus(); return; }
            CloseNoteEditor(false);
            _editingNoteId = id;
            var body = _styles.Body;
            _noteEditor = new TextBox
            {
                Text = note.Text,
                FontFamily = new FontFamily(body.FontFamily),
                FontSize = Math.Max(9, body.FontSize * 0.85),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false,
                Padding = new Thickness(2, 0, 2, 0),
                BorderBrush = Chrome.Accent,
                BorderThickness = new Thickness(1),
                Background = Chrome.PaperBg,
                Foreground = Chrome.PaperInk,
                ToolTip = "Note de bas de page — Entrée ou Échap pour fermer"
            };
            var noteRef = note;
            _noteEditor.TextChanged += delegate
            {
                if (_noteEditor == null || noteRef.Text == _noteEditor.Text) return;
                noteRef.Text = _noteEditor.Text;
                RefreshNotes();
                PositionNoteEditor();
                var handler = Edited;
                if (handler != null) handler();
            };
            _noteEditor.LostKeyboardFocus += delegate
            {
                // Fermé par un clic ailleurs (le focus part) — pas par nous.
                if (_noteEditor != null && !_noteEditor.IsKeyboardFocusWithin) CloseNoteEditor(false);
            };
            _noteLayer.Children.Add(_noteEditor);
            if (!PositionNoteEditor()) { CloseNoteEditor(false); return; }
            _noteEditor.CaretIndex = _noteEditor.Text.Length;
            _noteEditor.Focus();
            var started = NoteEditingStarted;
            if (started != null) started(id);
        }

        /// <summary>Pose (ou repose) l'éditeur de note sur la géométrie
        /// courante de la note. Faux quand la note n'est placée nulle part.</summary>
        private bool PositionNoteEditor()
        {
            if (_noteEditor == null || _engine == null) return false;
            var composition = _engine.Current;
            var index = MarkerOrder().IndexOf(_editingNoteId);
            if (index < 0) return false;
            for (var k = 0; k < composition.Pages.Count; k++)
            {
                var top = double.MaxValue;
                var bottom = double.MinValue;
                foreach (var placed in composition.Pages[k].NoteLines)
                {
                    if (placed.ParagraphIndex != index) continue;
                    var line = composition.NoteParagraphs[index].Lines[placed.LineIndex];
                    top = Math.Min(top, placed.Y);
                    bottom = Math.Max(bottom, placed.Y + line.Height);
                }
                if (top == double.MaxValue) continue;
                var left = composition.LeftPxFor(k);
                // Le numéro « n. » reste visible à gauche : le champ commence après.
                var numberWidth = Math.Max(14, _noteEditor.FontSize * 1.3);
                Canvas.SetLeft(_noteEditor, left + numberWidth);
                Canvas.SetTop(_noteEditor, PageTop(k) + top - 2);
                _noteEditor.Width = Math.Max(60, composition.Setup.ContentWidthPx - numberWidth);
                _noteEditor.MinHeight = Math.Max(16, bottom - top + 4);
                EnsureCaretVisible(PageTop(k) + top, bottom - top);
                return true;
            }
            return false;
        }

        /// <summary>Referme l'éditeur de note (le texte est déjà dans le
        /// modèle, frappe par frappe).</summary>
        public void CloseNoteEditor(bool refocus)
        {
            if (_noteEditor == null) { _editingNoteId = null; return; }
            var editor = _noteEditor;
            _noteEditor = null;
            _editingNoteId = null;
            _noteLayer.Children.Remove(editor);
            if (refocus) Focus();
        }

        private static bool IsInside(DependencyObject source, DependencyObject ancestor)
        {
            while (source != null)
            {
                if (source == ancestor) return true;
                source = source is System.Windows.Media.Visual
                    ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            return false;
        }

        private void RefreshPages(int firstChanged)
        {
            var composition = _engine.Current;
            if (_pages.Children.Count != composition.Pages.Count) { RebuildPages(); return; }
            if (firstChanged == int.MaxValue) return;
            for (var k = firstChanged; k < _pages.Children.Count; k++)
                PageAt(k).InvalidateVisual();
        }

        internal Composition CurrentComposition
        {
            get { return _engine == null ? null : _engine.Current; }
        }

        private double PageTop(int pageIndex)
        {
            return pageIndex * (_engine.Current.PageHeightPx + PageGapPx);
        }

        private sealed class PageElement : FrameworkElement
        {
            private static readonly Brush PageShadow = FrozenShadow();
            private readonly ComposedView _owner;
            private readonly int _index;

            private static Brush FrozenShadow()
            {
                var brush = new SolidColorBrush(Color.FromArgb(0x2A, 0x10, 0x12, 0x1A));
                brush.Freeze();
                return brush;
            }

            public PageElement(ComposedView owner, int index)
            {
                _owner = owner;
                _index = index;
                SnapsToDevicePixels = true;
                Cursor = Cursors.IBeam; // writing surface: text cursor
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                var composition = _owner.CurrentComposition;
                return composition == null ? new Size(0, 0)
                    : new Size(composition.PageWidthPx, composition.PageHeightPx);
            }

            protected override void OnRender(DrawingContext dc)
            {
                var composition = _owner.CurrentComposition;
                if (composition == null || _index >= composition.Pages.Count) return;
                // La page est la surface « paper » (batch 40) : elle suit le
                // thème et l'option « papier blanc en mode sombre », et une
                // ombre portée légère la décolle du fond (ground).
                var w = composition.PageWidthPx;
                var h = composition.PageHeightPx;
                // Mode calme (13/09) : l'ombre vient du cadre à effet dessous,
                // pas de ce rectangle décalé.
                if (!_owner._calmLook) dc.DrawRectangle(PageShadow, null, new Rect(2, 4, w, h));
                dc.DrawRectangle(Chrome.PaperBg, new Pen(Chrome.Border, 1),
                    new Rect(0.5, 0.5, w - 1, h - 1));
                ComposedRenderer.DrawPage(dc, composition, _index, true);
            }
        }

        // ============================================================ geometry

        private ComposedLine LineOf(int paragraphIndex, int offset,
            out int pageIndex, out double lineY)
        {
            pageIndex = 0;
            lineY = 0;
            var composition = _engine.Current;
            if (paragraphIndex < 0 || paragraphIndex >= composition.Paragraphs.Count)
                return null;
            var layout = composition.Paragraphs[paragraphIndex];
            var lineIndex = layout.Lines.Count - 1;
            for (var i = 0; i < layout.Lines.Count; i++)
                if (offset < layout.Lines[i].End || (i == layout.Lines.Count - 1))
                { lineIndex = i; break; }

            for (var k = 0; k < composition.Pages.Count; k++)
                foreach (var placed in composition.Pages[k].Lines)
                    if (placed.ParagraphIndex == paragraphIndex && placed.LineIndex == lineIndex)
                    {
                        pageIndex = k;
                        lineY = placed.Y;
                        return layout.Lines[lineIndex];
                    }
            return layout.Lines.Count > 0 ? layout.Lines[lineIndex] : null;
        }

        /// <summary>left = LeftPxFor(page de la ligne) — mirrored margins make
        /// the text column shift with the folio parity.</summary>
        private double CaretX(ComposedLine line, int offset, double left)
        {
            // Géométrie partagée avec l'ondulé des signalements (batch 26).
            return ComposedRenderer.OffsetX(line, offset, left);
        }

        private int OffsetFromX(ComposedLine line, double xPage, double left)
        {
            var offset = line.Start;
            var lastEnd = line.Start;
            foreach (var piece in line.Pieces)
            {
                if (piece.SourceStart < 0 || piece.SourceLength <= 0) continue;
                var pieceLeft = left + piece.Origin.X;
                for (var c = 0; c < piece.SourceLength; c++)
                {
                    var charLeft = pieceLeft + (c == 0 ? 0 : piece.CharRights[c - 1]);
                    var charRight = pieceLeft + piece.CharRights[Math.Min(c, piece.CharRights.Length - 1)];
                    if (xPage < (charLeft + charRight) / 2)
                        return piece.SourceStart + c;
                }
                lastEnd = piece.SourceStart + piece.SourceLength;
            }
            offset = Math.Max(offset, lastEnd);
            // Clicking past the end of a wrapped line: stay on this line's end.
            return line.EndsParagraph ? Math.Min(offset, line.End)
                : Math.Max(line.Start, Math.Min(offset, line.End > line.Start ? line.End - 0 : line.End));
        }

        // ============================================================ caret & selection visuals

        private void ClearOverlay()
        {
            for (var i = _overlay.Children.Count - 1; i >= 0; i--)
                if (!ReferenceEquals(_overlay.Children[i], _caretBar))
                    _overlay.Children.RemoveAt(i);
        }

        private void UpdateCaretVisual()
        {
            ClearOverlay();
            if (_item == null || _engine == null || _engine.Current.Paragraphs.Count == 0)
            {
                _caretBar.Visibility = Visibility.Collapsed;
                return;
            }
            DrawSelectionOverlay();

            int pageIndex;
            double lineY;
            var line = LineOf(_caretParagraph, _caretOffset, out pageIndex, out lineY);
            if (line == null) { _caretBar.Visibility = Visibility.Collapsed; return; }
            var x = CaretX(line, _caretOffset, _engine.Current.LeftPxFor(pageIndex));
            var y = PageTop(pageIndex) + lineY;
            Canvas.SetLeft(_caretBar, x);
            Canvas.SetTop(_caretBar, y + 1);
            _caretBar.Height = Math.Max(8, line.Height - 2);
            _caretBar.Visibility = Visibility.Visible;

            EnsureCaretVisible(y, line.Height);
            RaisePageInfo();
            var stateHandler = SelectionStateChanged;
            if (stateHandler != null) stateHandler();
        }

        private bool _keepScroll; // un rafraîchissement qui ne doit pas déplacer la vue

        private void EnsureCaretVisible(double y, double height)
        {
            if (_keepScroll) return;
            var topContent = (_column.Margin.Top + y) * _zoom;
            var bottomContent = (_column.Margin.Top + y + height) * _zoom;
            if (topContent < VerticalOffset + 8)
                ScrollToVerticalOffset(Math.Max(0, topContent - 60));
            else if (bottomContent > VerticalOffset + ViewportHeight - 8)
                ScrollToVerticalOffset(bottomContent - ViewportHeight + 60);
        }

        private void DrawSelectionOverlay()
        {
            if (!HasSelection()) return;
            int pa, oa, pb, ob;
            OrderedSelection(out pa, out oa, out pb, out ob);
            var brush = new SolidColorBrush(SystemColors.HighlightColor) { Opacity = 0.45 };
            brush.Freeze();
            var composition = _engine.Current;

            for (var k = 0; k < composition.Pages.Count; k++)
            {
                foreach (var placed in composition.Pages[k].Lines)
                {
                    if (placed.ParagraphIndex < pa || placed.ParagraphIndex > pb) continue;
                    var line = composition.Paragraphs[placed.ParagraphIndex].Lines[placed.LineIndex];
                    var from = placed.ParagraphIndex == pa ? Math.Max(line.Start, oa) : line.Start;
                    var to = placed.ParagraphIndex == pb ? Math.Min(line.End, ob) : line.End;
                    if (from > to) continue;
                    if (from == to && !(placed.ParagraphIndex < pb && line.EndsParagraph)) continue;
                    var pageLeft = composition.LeftPxFor(k);
                    var x1 = CaretX(line, from, pageLeft);
                    var x2 = CaretX(line, to, pageLeft);
                    if (placed.ParagraphIndex < pb && line.EndsParagraph) x2 += 6; // pilcrow
                    if (x2 - x1 < 2) x2 = x1 + 2;
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = x2 - x1,
                        Height = Math.Max(2, line.Height - 1),
                        Fill = brush
                    };
                    Canvas.SetLeft(rect, x1);
                    Canvas.SetTop(rect, PageTop(k) + placed.Y);
                    _overlay.Children.Insert(0, rect);
                }
            }
        }

        private void RaisePageInfo()
        {
            var handler = PageInfoChanged;
            if (handler == null || _engine == null) return;
            int pageIndex;
            double lineY;
            LineOf(_caretParagraph, _caretOffset, out pageIndex, out lineY);
            // Book documents report their REAL folio in the book.
            var offset = _engine.Current.FolioOffset;
            handler(pageIndex + 1 + offset,
                Math.Max(1, _engine.Current.Pages.Count) + offset);
        }

        // ============================================================ selection model

        private bool HasSelection()
        {
            return _anchorParagraph >= 0
                && (_anchorParagraph != _caretParagraph || _anchorOffset != _caretOffset);
        }

        private void ClearSelection()
        {
            _anchorParagraph = -1;
        }

        private void OrderedSelection(out int pa, out int oa, out int pb, out int ob)
        {
            if (_anchorParagraph < _caretParagraph
                || (_anchorParagraph == _caretParagraph && _anchorOffset <= _caretOffset))
            { pa = _anchorParagraph; oa = _anchorOffset; pb = _caretParagraph; ob = _caretOffset; }
            else
            { pa = _caretParagraph; oa = _caretOffset; pb = _anchorParagraph; ob = _anchorOffset; }
        }

        // ============================================================ mouse

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_item == null) return;
            // Les clics destinés aux BARRES DE DÉFILEMENT ne sont pas à nous :
            // le Preview les intercepterait et rendrait le scroll inaccessible.
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is System.Windows.Controls.Primitives.ScrollBar) return;
                source = source is System.Windows.Media.Visual
                    ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            if (_noteEditor != null && IsInside(e.OriginalSource as DependencyObject, _noteEditor))
                return; // le clic est pour l'éditeur de note
            // Les bulles d'annotation vivent dans la même colonne (batch 26) :
            // leurs clics ne sont pas à nous non plus (batch 34 — la bulle
            // se refermait avant d'avoir reçu le focus).
            if (IsInside(e.OriginalSource as DependencyObject, _bubbleLayer)) return;
            Focus();
            if (ToggleWidowMarkAt(e)) { e.Handled = true; return; }
            // Clic sur une note au bas de la page : on l'édite en place.
            var noteId = NoteAtPoint(e.GetPosition(_pages));
            if (noteId != null) { EditNote(noteId); e.Handled = true; return; }
            int paragraph, offset;
            if (!HitTestPosition(e, out paragraph, out offset)) return;
            // Clic sur l'appel de note (l'exposant) : même chose.
            if (e.ClickCount == 1 && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                var marker = MarkerAt(paragraph, offset, e.GetPosition(_pages));
                if (marker != null) { EditNote(marker); e.Handled = true; return; }
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                var title = WikiLinkAt(paragraph, offset);
                if (title != null)
                {
                    var handler = LinkClicked;
                    if (handler != null) handler(title);
                    e.Handled = true;
                    return;
                }
            }

            if (e.ClickCount == 2)
            {
                SelectWordAt(paragraph, offset);
                e.Handled = true;
                UpdateCaretVisual();
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                if (_anchorParagraph < 0) { _anchorParagraph = _caretParagraph; _anchorOffset = _caretOffset; }
            }
            else
            {
                _anchorParagraph = paragraph;
                _anchorOffset = offset;
            }
            _caretParagraph = paragraph;
            _caretOffset = offset;
            _caretDesiredX = -1;
            _mouseSelecting = true;
            CaptureMouse();
            UpdateCaretVisual();
            e.Handled = true;
        }

        /// <summary>Click on a widow/orphan margin marker: toggles the
        /// paragraph's « autoriser l'aberration » flag and repaginates. The
        /// mark stays (orange = correction active, gris = débrayée).</summary>
        private bool ToggleWidowMarkAt(MouseButtonEventArgs e)
        {
            if (!ComposedRenderer.ShowWidowMarks) return false; // invisibles : inertes
            var composition = _engine == null ? null : _engine.Current;
            if (composition == null || composition.Pages.Count == 0) return false;
            var point = e.GetPosition(_pages);
            var stride = composition.PageHeightPx + PageGapPx;
            var pageIndex = Math.Max(0, Math.Min(composition.Pages.Count - 1,
                (int)(point.Y / stride)));
            var yInPage = point.Y - pageIndex * stride;
            var xInPage = point.X;
            var left = composition.LeftPxFor(pageIndex);
            foreach (var mark in composition.Pages[pageIndex].WidowMarks)
            {
                var rect = new Rect(Math.Max(2, left - 22) - 2, mark.Y - 2, 18, 18);
                if (!rect.Contains(new Point(xInPage, yInPage))) continue;
                var paragraph = _item.Document.Paragraphs[mark.ParagraphIndex];
                paragraph.AllowWidows = !paragraph.AllowWidows;
                var firstChanged = _engine.Repaginate();
                // La vue reste où elle est (12/09) : UpdateCaretVisual ramenait
                // le caret — souvent encore en tête du document — dans la
                // fenêtre, donc tout en haut, à chaque clic sur un signet.
                var offset = VerticalOffset;
                RefreshPages(Math.Min(firstChanged, pageIndex));
                _keepScroll = true;
                try { UpdateCaretVisual(); }
                finally { _keepScroll = false; }
                ScrollToVerticalOffset(offset);
                RaisePageInfo();
                var handler = Edited;
                if (handler != null) handler(); // persisté : le projet est sale
                return true;
            }
            return false;
        }

        /// <summary>Clic droit sur un mot : réglage fin de la césure — retire
        /// le mot des coupes du compositeur (exception de projet), ou l'y
        /// autorise de nouveau.</summary>
        private void OnMouseRightDown(object sender, MouseButtonEventArgs e)
        {
            if (ReadOnly) { e.Handled = true; return; }
            if (_item == null || _project == null) return;
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is System.Windows.Controls.Primitives.ScrollBar) return;
                source = source is System.Windows.Media.Visual
                    ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            int paragraph, offset;
            if (!HitTestPosition(e, out paragraph, out offset)) return;
            var word = WordAt(paragraph, offset);
            var findings = FindingsAt(paragraph, offset);
            if ((word == null || word.Length < 2) && findings.Count == 0) return;

            var menu = new ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };

            // — Les signalements de correction sous le pointeur, en tête.
            foreach (var finding in findings)
            {
                var findingRef = finding;
                var header = new MenuItem
                {
                    Header = finding.Message,
                    IsEnabled = false,
                    Foreground = ComposedRenderer.FindingPen(finding).Brush
                };
                menu.Items.Add(header);
                // Suggestions À LA DEMANDE (batch 27) : le calcul n'a pas eu
                // lieu pendant la passe, il a lieu ici, au moment de montrer.
                var provider = SuggestionProvider;
                var suggestions = provider != null
                    ? provider(finding) : finding.Suggestions;
                foreach (var suggestion in suggestions)
                {
                    var suggestionRef = suggestion;
                    var apply = new MenuItem
                    {
                        Header = suggestion,
                        FontWeight = FontWeights.SemiBold
                    };
                    apply.Click += delegate { ApplySuggestion(findingRef, suggestionRef); };
                    menu.Items.Add(apply);
                }
                var here = new MenuItem
                {
                    Header = "Ignorer ici",
                    ToolTip = "Tait CE signalement, pour cette session"
                };
                here.Click += delegate
                {
                    var handler = FindingIgnoreHere;
                    if (handler != null) handler(findingRef);
                };
                menu.Items.Add(here);
                // « Ignorer cette règle » (batch 29) : réservé au
                // grammatical — un RuleId d'orthographe (« spelling ») ou de
                // répétition couvrirait TOUT le vérificateur.
                if (findingRef.CheckerId == "grammar"
                    && findingRef.RuleId.Length > 0)
                {
                    var rule = new MenuItem
                    {
                        Header = "Ignorer cette règle",
                        ToolTip = "Plus aucun signalement de la règle « "
                            + findingRef.RuleId + " » dans ce projet "
                            + "(liste enregistrée avec lui)"
                    };
                    rule.Click += delegate
                    {
                        var handler = FindingIgnoreRule;
                        if (handler != null) handler(findingRef);
                    };
                    menu.Items.Add(rule);
                }
                // Un indice de style morphologique (adverbe, verbe terne) ne
                // propose pas d'ignorer LE MOT dans le projet : cela
                // éteindrait aussi son orthographe et ses répétitions.
                if (findingRef.Word.Length > 0 && findingRef.CheckerId != "style")
                {
                    var inProject = new MenuItem
                    {
                        Header = "Ignorer « " + findingRef.Word + " » dans ce projet",
                        ToolTip = "Le mot ne sera plus signalé dans ce projet "
                            + "(liste enregistrée avec lui)"
                    };
                    inProject.Click += delegate
                    {
                        var handler = FindingIgnoreProject;
                        if (handler != null) handler(findingRef);
                    };
                    menu.Items.Add(inProject);
                }
                // « Ajouter au dictionnaire » ENSEIGNE un mot (dictionnaires
                // personnels du lot D) — rien à voir avec « ignorer », qui
                // TAIT un signalement. Deux portées, comme les ignorés.
                if (findingRef.CheckerId == "spelling" && findingRef.Word.Length > 0)
                {
                    var learn = new MenuItem
                    {
                        Header = "Ajouter « " + findingRef.Word + " » au dictionnaire"
                    };
                    var inProject = new MenuItem
                    {
                        Header = "De ce projet",
                        ToolTip = "Le mot est enseigné pour CE roman "
                            + "(enregistré dans le .plot)"
                    };
                    inProject.Click += delegate
                    {
                        var handler = FindingLearn;
                        if (handler != null) handler(findingRef, true);
                    };
                    learn.Items.Add(inProject);
                    var everywhere = new MenuItem
                    {
                        Header = "De tous les projets",
                        ToolTip = "Le mot est enseigné partout (réglages de "
                            + "l'application)"
                    };
                    everywhere.Click += delegate
                    {
                        var handler = FindingLearn;
                        if (handler != null) handler(findingRef, false);
                    };
                    learn.Items.Add(everywhere);
                    menu.Items.Add(learn);
                }
                menu.Items.Add(new Separator());
            }

            // — Les synonymes du mot (batch 44) : un sous-menu qui ne demande
            // rien tant qu'on ne l'OUVRE pas (revue du 13/09 : un clic droit
            // ne doit pas démarrer Python), puis se remplit quand le pont
            // répond — jamais d'attente sur le fil UI.
            int wordStart, wordLength;
            string wordText;
            if (word != null && word.Length >= 2 && SynonymLookup != null
                && WordRangeAt(paragraph, offset, out wordStart, out wordLength, out wordText))
            {
                var target = new SynonymTarget
                {
                    Menu = new MenuItem
                    {
                        Header = "Synonymes de « " + word + " »",
                        ToolTip = "Thésaurus de Grammalecte, formes fléchies comme le mot"
                    },
                    Word = word,
                    Paragraph = paragraph,
                    Start = wordStart,
                    Length = wordLength
                };
                // Un item de garde : sans enfant, le sous-menu n'aurait pas
                // de flèche et ne s'ouvrirait jamais.
                target.Menu.Items.Add(new MenuItem { Header = "Recherche…", IsEnabled = false });
                target.Menu.SubmenuOpened += delegate
                {
                    _openSynonyms = target;
                    FillSynonymMenu(target);
                };
                menu.Items.Add(target.Menu);
                menu.Closed += delegate
                {
                    if (_openSynonyms == target) _openSynonyms = null;
                };
            }

            // — La césure du mot (batch 25).
            if (word != null && word.Length >= 2)
            {
                var excepted = false;
                foreach (var entry in _project.HyphenExceptions)
                    if (string.Equals(entry, word, StringComparison.OrdinalIgnoreCase))
                    {
                        excepted = true;
                        break;
                    }
                var toggle = new MenuItem
                {
                    Header = excepted
                        ? "Autoriser la césure de « " + word + " »"
                        : "Ne plus couper « " + word + " »",
                    ToolTip = "Exception de césure du projet : le compositeur ne "
                        + "coupe jamais ce mot en fin de ligne"
                };
                var wordRef = word;
                toggle.Click += delegate { ToggleHyphenException(wordRef); };
                menu.Items.Add(toggle);
            }
            else if (menu.Items.Count > 0)
            {
                // Pas de mot sous le clic : retirer le séparateur de queue.
                menu.Items.RemoveAt(menu.Items.Count - 1);
            }
            if (menu.Items.Count == 0) return;
            menu.IsOpen = true;
            e.Handled = true;
        }

        /// <summary>Le mot (lettres/chiffres) sous l'offset, ou null.</summary>
        private string WordAt(int paragraphIndex, int offset)
        {
            int start, length;
            string word;
            return WordRangeAt(paragraphIndex, offset, out start, out length, out word) ? word : null;
        }

        /// <summary>La plage du mot sous l'offset et le mot lui-même (batch
        /// 44 : le remplacement par un synonyme a besoin de la position).</summary>
        private bool WordRangeAt(int paragraphIndex, int offset,
            out int start, out int length, out string word)
        {
            start = 0;
            length = 0;
            word = null;
            var text = PivotEdit.FlatText(_item.Document.Paragraphs[paragraphIndex]);
            if (text.Length == 0) return false;
            var i = Math.Min(offset, text.Length - 1);
            if (!char.IsLetterOrDigit(text[i]) && i > 0) i--;
            if (!char.IsLetterOrDigit(text[i])) return false;
            start = i;
            var end = i;
            while (start > 0 && char.IsLetterOrDigit(text[start - 1])) start--;
            while (end < text.Length && char.IsLetterOrDigit(text[end])) end++;
            length = end - start;
            word = text.Substring(start, length);
            return true;
        }

        private void ToggleHyphenException(string word)
        {
            var removed = false;
            for (var i = _project.HyphenExceptions.Count - 1; i >= 0; i--)
                if (string.Equals(_project.HyphenExceptions[i], word,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _project.HyphenExceptions.RemoveAt(i);
                    removed = true;
                }
            if (!removed) _project.HyphenExceptions.Add(word);
            RefreshComposition();
            var handler = Edited;
            if (handler != null) handler(); // persisté : le projet est sale
        }

        private void OnMouseMoveDrag(object sender, MouseEventArgs e)
        {
            if (!_mouseSelecting || e.LeftButton != MouseButtonState.Pressed) return;
            int paragraph, offset;
            if (!HitTestPosition(e, out paragraph, out offset)) return;
            _caretParagraph = paragraph;
            _caretOffset = offset;
            UpdateCaretVisual();
        }

        private bool HitTestPosition(MouseEventArgs e, out int paragraph, out int offset)
        {
            paragraph = 0;
            offset = 0;
            if (_engine == null || _engine.Current.Pages.Count == 0) return false;
            var composition = _engine.Current;
            var point = e.GetPosition(_pages);
            var stride = composition.PageHeightPx + PageGapPx;
            var pageIndex = Math.Max(0, Math.Min(composition.Pages.Count - 1,
                (int)(point.Y / stride)));
            var yInPage = point.Y - pageIndex * stride;

            var page = composition.Pages[pageIndex];
            if (page.Lines.Count == 0)
            {
                // empty page: land at the nearest paragraph start
                paragraph = _caretParagraph;
                offset = _caretOffset;
                return true;
            }
            var chosen = page.Lines[page.Lines.Count - 1];
            foreach (var placed in page.Lines)
            {
                var line = composition.Paragraphs[placed.ParagraphIndex].Lines[placed.LineIndex];
                if (yInPage < placed.Y) { chosen = placed; break; }
                chosen = placed;
                if (yInPage <= placed.Y + line.Height) break;
            }
            paragraph = chosen.ParagraphIndex;
            var chosenLine = composition.Paragraphs[paragraph].Lines[chosen.LineIndex];
            offset = OffsetFromX(chosenLine, point.X, composition.LeftPxFor(pageIndex));
            return true;
        }

        private string WikiLinkAt(int paragraphIndex, int offset)
        {
            var text = PivotEdit.FlatText(_item.Document.Paragraphs[paragraphIndex]);
            var open = text.LastIndexOf("[[", Math.Min(offset, Math.Max(0, text.Length - 1)),
                StringComparison.Ordinal);
            if (open < 0) return null;
            var close = text.IndexOf("]]", open + 2, StringComparison.Ordinal);
            if (close < 0 || close + 1 < offset) return null;
            var title = text.Substring(open + 2, close - open - 2).Trim();
            return title.Length > 0 && title.Length < 120 ? title : null;
        }

        private void SelectWordAt(int paragraphIndex, int offset)
        {
            var text = PivotEdit.FlatText(_item.Document.Paragraphs[paragraphIndex]);
            if (text.Length == 0) return;
            var i = Math.Min(offset, text.Length - 1);
            if (!char.IsLetterOrDigit(text[i]) && i > 0) i--;
            var start = i;
            var end = i;
            while (start > 0 && char.IsLetterOrDigit(text[start - 1])) start--;
            while (end < text.Length && char.IsLetterOrDigit(text[end])) end++;
            _anchorParagraph = paragraphIndex;
            _anchorOffset = start;
            _caretParagraph = paragraphIndex;
            _caretOffset = end;
        }

        // ============================================================ input

        private void OnTextInput(object sender, TextCompositionEventArgs e)
        {
            if (ReadOnly) { e.Handled = true; return; }
            if (_noteEditor != null && _noteEditor.IsKeyboardFocusWithin) return; // la note tape pour elle
            if (_bubbleLayer.IsKeyboardFocusWithin) return; // une bulle d'annotation tape pour elle (b34)
            if (_item == null || string.IsNullOrEmpty(e.Text)) return;
            var text = e.Text;
            if (text == "\r" || text == "\n" || text == "\t" || text == "\b"
                || (text.Length == 1 && char.IsControl(text[0]))) return;
            e.Handled = true;
            TypeText(text);
        }

        /// <summary>Public for tests and toolbar routing.</summary>
        public void TypeText(string text)
        {
            if (_item == null) return;
            PushUndo(true);
            DeleteSelectionIfAny();
            PivotEdit.InsertText(_item.Document.Paragraphs[_caretParagraph], _caretOffset, text);
            _caretOffset += text.Length;
            _caretDesiredX = -1;
            AfterEdit(_engine.RecomposeParagraph(_caretParagraph));
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (ReadOnly && !IsNavigationKey(e)) { e.Handled = true; return; }
            if (_noteEditor != null && _noteEditor.IsKeyboardFocusWithin)
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Return)
                {
                    CloseNoteEditor(true);
                    e.Handled = true;
                }
                return; // les autres touches vont au TextBox de la note
            }
            if (_bubbleLayer.IsKeyboardFocusWithin) return; // le clavier est à la bulle (b34)
            if (_item == null) return;
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var handled = true;
            switch (e.Key)
            {
                case Key.Left: MoveCaret(-1, false, shift, ctrl); break;
                case Key.Right: MoveCaret(1, false, shift, ctrl); break;
                case Key.Up: MoveCaret(-1, true, shift, false); break;
                case Key.Down: MoveCaret(1, true, shift, false); break;
                case Key.Home: MoveHomeEnd(true, shift, ctrl); break;
                case Key.End: MoveHomeEnd(false, shift, ctrl); break;
                case Key.PageUp: MovePage(-1, shift); break;
                case Key.PageDown: MovePage(1, shift); break;
                case Key.Back: Backspace(); break;
                case Key.Delete: ForwardDelete(); break;
                case Key.Return:
                    if (ctrl) TogglePageBreak();
                    else if (shift) InsertLineBreak();
                    else InsertParagraphBreak();
                    break;
                case Key.Escape:
                    // Échap ne QUITTE plus la composition (13/09) : l'éditeur
                    // classique est gelé depuis le batch 26, y retomber
                    // affichait des « pages » figées par-dessus le Brouillon
                    // ou le mode calme. Ici, Échap ne fait que lâcher la
                    // sélection ; la coquille garde Échap pour sortir du calme.
                    ClearSelection();
                    handled = false; // laisse remonter (mode calme)
                    break;
                case Key.A: if (ctrl) SelectAll(); else handled = false; break;
                case Key.C: if (ctrl) CopySelection(false); else handled = false; break;
                case Key.X: if (ctrl) CopySelection(true); else handled = false; break;
                case Key.V: if (ctrl) Paste(); else handled = false; break;
                case Key.B: if (ctrl) ToggleBold(); else handled = false; break;
                case Key.I: if (ctrl) ToggleItalic(); else handled = false; break;
                case Key.U: if (ctrl) ToggleUnderline(); else handled = false; break;
                case Key.Z: if (ctrl) Undo(); else handled = false; break;
                case Key.Y: if (ctrl) Redo(); else handled = false; break;
                default: handled = false; break;
            }
            if (handled) e.Handled = true;
        }

        /// <summary>Public for tests and the shell: place the caret.</summary>
        public void PlaceCaret(int paragraph, int offset, bool keepAnchor)
        {
            if (!keepAnchor) ClearSelection();
            else if (_anchorParagraph < 0)
            { _anchorParagraph = _caretParagraph; _anchorOffset = _caretOffset; }
            _caretParagraph = paragraph;
            _caretOffset = offset;
            ClampCaret();
            UpdateCaretVisual();
        }

        public void BackspacePublic() { Backspace(); }
        public void DeletePublic() { ForwardDelete(); }

        // ============================================================ caret moves

        private void MoveCaret(int direction, bool vertical, bool extend, bool word)
        {
            if (!extend && HasSelection())
            {
                // Collapse to the selection edge in the move direction.
                int pa, oa, pb, ob;
                OrderedSelection(out pa, out oa, out pb, out ob);
                if (direction < 0) { _caretParagraph = pa; _caretOffset = oa; }
                else { _caretParagraph = pb; _caretOffset = ob; }
                ClearSelection();
                if (!vertical) { UpdateCaretVisual(); return; }
            }
            if (extend && _anchorParagraph < 0)
            {
                _anchorParagraph = _caretParagraph;
                _anchorOffset = _caretOffset;
            }
            if (!extend) ClearSelection();

            if (vertical) MoveVertical(direction);
            else if (word) MoveWord(direction);
            else MoveHorizontal(direction);
            UpdateCaretVisual();
        }

        private void MoveHorizontal(int direction)
        {
            _caretDesiredX = -1;
            var paragraph = _item.Document.Paragraphs[_caretParagraph];
            var length = PivotEdit.FlatLength(paragraph);
            var target = _caretOffset + direction;
            if (target < 0)
            {
                if (_caretParagraph > 0)
                {
                    _caretParagraph--;
                    _caretOffset = PivotEdit.FlatLength(_item.Document.Paragraphs[_caretParagraph]);
                }
            }
            else if (target > length)
            {
                if (_caretParagraph < _item.Document.Paragraphs.Count - 1)
                {
                    _caretParagraph++;
                    _caretOffset = 0;
                }
            }
            else
                _caretOffset = target;
        }

        private void MoveWord(int direction)
        {
            _caretDesiredX = -1;
            var text = PivotEdit.FlatText(_item.Document.Paragraphs[_caretParagraph]);
            var i = _caretOffset;
            if (direction < 0)
            {
                if (i == 0) { MoveHorizontal(-1); return; }
                i--;
                while (i > 0 && !char.IsLetterOrDigit(text[i - 1])) i--;
                while (i > 0 && char.IsLetterOrDigit(text[i - 1])) i--;
            }
            else
            {
                if (i >= text.Length) { MoveHorizontal(1); return; }
                while (i < text.Length && char.IsLetterOrDigit(text[i])) i++;
                while (i < text.Length && !char.IsLetterOrDigit(text[i])) i++;
            }
            _caretOffset = i;
        }

        private void MoveVertical(int direction)
        {
            int pageIndex;
            double lineY;
            var line = LineOf(_caretParagraph, _caretOffset, out pageIndex, out lineY);
            if (line == null) return;
            var composition = _engine.Current;
            // Column memory is COLUMN-relative: with mirrored margins the
            // absolute x of the same column differs between recto and verso.
            if (_caretDesiredX < 0)
                _caretDesiredX = CaretX(line, _caretOffset, composition.LeftPxFor(pageIndex))
                    - composition.LeftPxFor(pageIndex);

            // Find the placed line above/below in reading order.
            var flat = new List<PlacedLine>();
            var pageOf = new List<int>();
            for (var k = 0; k < composition.Pages.Count; k++)
                foreach (var placed in composition.Pages[k].Lines)
                { flat.Add(placed); pageOf.Add(k); }
            var current = -1;
            for (var i = 0; i < flat.Count; i++)
            {
                var candidate = composition.Paragraphs[flat[i].ParagraphIndex].Lines[flat[i].LineIndex];
                if (flat[i].ParagraphIndex == _caretParagraph
                    && ReferenceEquals(candidate, line)) { current = i; break; }
            }
            if (current < 0) return;
            var next = current + direction;
            if (next < 0 || next >= flat.Count) return;
            var targetPlaced = flat[next];
            var targetLine = composition.Paragraphs[targetPlaced.ParagraphIndex].Lines[targetPlaced.LineIndex];
            _caretParagraph = targetPlaced.ParagraphIndex;
            var targetLeft = composition.LeftPxFor(pageOf[next]);
            _caretOffset = OffsetFromX(targetLine, targetLeft + _caretDesiredX, targetLeft);
        }

        private void MoveHomeEnd(bool home, bool extend, bool document)
        {
            if (extend && _anchorParagraph < 0)
            { _anchorParagraph = _caretParagraph; _anchorOffset = _caretOffset; }
            if (!extend) ClearSelection();
            _caretDesiredX = -1;
            if (document)
            {
                _caretParagraph = home ? 0 : _item.Document.Paragraphs.Count - 1;
                _caretOffset = home ? 0
                    : PivotEdit.FlatLength(_item.Document.Paragraphs[_caretParagraph]);
            }
            else
            {
                int pageIndex;
                double lineY;
                var line = LineOf(_caretParagraph, _caretOffset, out pageIndex, out lineY);
                if (line != null) _caretOffset = home ? line.Start : line.End;
                if (!home && line != null && !line.EndsParagraph && _caretOffset > line.Start)
                    _caretOffset = line.End; // caret sits at the wrap point
            }
            UpdateCaretVisual();
        }

        private void MovePage(int direction, bool extend)
        {
            if (extend && _anchorParagraph < 0)
            { _anchorParagraph = _caretParagraph; _anchorOffset = _caretOffset; }
            if (!extend) ClearSelection();
            for (var i = 0; i < 20; i++) MoveVertical(direction);
            UpdateCaretVisual();
        }

        public void SelectAll()
        {
            _anchorParagraph = 0;
            _anchorOffset = 0;
            _caretParagraph = _item.Document.Paragraphs.Count - 1;
            _caretOffset = PivotEdit.FlatLength(_item.Document.Paragraphs[_caretParagraph]);
            UpdateCaretVisual();
        }

        private void ClampCaret()
        {
            if (_caretParagraph >= _item.Document.Paragraphs.Count)
                _caretParagraph = _item.Document.Paragraphs.Count - 1;
            if (_caretParagraph < 0) _caretParagraph = 0;
            var max = PivotEdit.FlatLength(_item.Document.Paragraphs[_caretParagraph]);
            if (_caretOffset > max) _caretOffset = max;
            if (_caretOffset < 0) _caretOffset = 0;
        }

        // ============================================================ edits

        private void AfterEdit(int firstChangedPage)
        {
            PivotEdit.PurgeFootnotes(_item.Document);
            RefreshPages(firstChangedPage);
            UpdateCaretVisual();
            var handler = Edited;
            if (handler != null) handler();
        }

        private void PushUndo(bool typing)
        {
            var now = DateTime.Now;
            if (typing && _lastWasTyping && (now - _lastTyping).TotalMilliseconds < 900)
            {
                _lastTyping = now;
                return; // coalesce the burst
            }
            _lastWasTyping = typing;
            _lastTyping = now;
            _undo.Add(new Snapshot
            {
                Document = PivotEdit.Clone(_item.Document),
                Paragraph = _caretParagraph,
                Offset = _caretOffset
            });
            if (_undo.Count > 100) _undo.RemoveAt(0);
            _redo.Clear();
        }

        /// <summary>Le plus ancien instantané local — l'état du document tel
        /// qu'ouvert, tant que la pile n'a pas débordé (la capture quotidienne
        /// fige l'état d'AVANT la première frappe, b38). Null sans frappe.</summary>
        public TextDocument OldestUndoDocument()
        {
            return _undo.Count > 0 ? _undo[0].Document : null;
        }

        public bool Undo()
        {
            if (ReadOnly || _undo.Count == 0) return false;
            var snapshot = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(new Snapshot
            {
                Document = PivotEdit.Clone(_item.Document),
                Paragraph = _caretParagraph,
                Offset = _caretOffset
            });
            RestoreSnapshot(snapshot);
            _lastWasTyping = false;
            return true;
        }

        public bool Redo()
        {
            if (ReadOnly || _redo.Count == 0) return false;
            var snapshot = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(new Snapshot
            {
                Document = PivotEdit.Clone(_item.Document),
                Paragraph = _caretParagraph,
                Offset = _caretOffset
            });
            RestoreSnapshot(snapshot);
            _lastWasTyping = false;
            return true;
        }

        private void RestoreSnapshot(Snapshot snapshot)
        {
            var document = _item.Document;
            document.Paragraphs.Clear();
            document.Paragraphs.AddRange(snapshot.Document.Paragraphs);
            document.Footnotes.Clear();
            document.Footnotes.AddRange(snapshot.Document.Footnotes);
            _caretParagraph = snapshot.Paragraph;
            _caretOffset = snapshot.Offset;
            ClearSelection();
            _engine.ComposeAll();
            RebuildPages();
            ClampCaret();
            UpdateCaretVisual();
            var handler = Edited;
            if (handler != null) handler();
        }

        private bool DeleteSelectionIfAny()
        {
            if (!HasSelection())
            {
                ClearSelection(); // drop any collapsed anchor before editing
                return false;
            }
            int pa, oa, pb, ob;
            OrderedSelection(out pa, out oa, out pb, out ob);
            var document = _item.Document;
            if (pa == pb)
            {
                PivotEdit.DeleteInParagraph(document.Paragraphs[pa], oa, ob);
            }
            else
            {
                PivotEdit.DeleteInParagraph(document.Paragraphs[pa], oa,
                    PivotEdit.FlatLength(document.Paragraphs[pa]));
                PivotEdit.DeleteInParagraph(document.Paragraphs[pb], 0, ob);
                PivotEdit.MergeInto(document.Paragraphs[pa], document.Paragraphs[pb]);
                for (var i = pb; i > pa; i--)
                {
                    document.Paragraphs.RemoveAt(i);
                    _engine.Current.Paragraphs.RemoveAt(i);
                }
            }
            _caretParagraph = pa;
            _caretOffset = oa;
            ClearSelection();
            _engine.RecomposeParagraph(pa);
            return true;
        }

        private void Backspace()
        {
            PushUndo(false);
            if (DeleteSelectionIfAny()) { AfterEdit(0); return; }
            var document = _item.Document;
            if (_caretOffset > 0)
            {
                PivotEdit.DeleteInParagraph(document.Paragraphs[_caretParagraph],
                    _caretOffset - 1, _caretOffset);
                _caretOffset--;
                AfterEdit(_engine.RecomposeParagraph(_caretParagraph));
            }
            else if (_caretParagraph > 0)
            {
                var previous = document.Paragraphs[_caretParagraph - 1];
                var newOffset = PivotEdit.FlatLength(previous);
                PivotEdit.MergeInto(previous, document.Paragraphs[_caretParagraph]);
                document.Paragraphs.RemoveAt(_caretParagraph);
                var merged = _caretParagraph - 1;
                _caretParagraph = merged;
                _caretOffset = newOffset;
                AfterEdit(_engine.ParagraphRemoved(merged + 1, merged));
            }
        }

        private void ForwardDelete()
        {
            PushUndo(false);
            if (DeleteSelectionIfAny()) { AfterEdit(0); return; }
            var document = _item.Document;
            var length = PivotEdit.FlatLength(document.Paragraphs[_caretParagraph]);
            if (_caretOffset < length)
            {
                PivotEdit.DeleteInParagraph(document.Paragraphs[_caretParagraph],
                    _caretOffset, _caretOffset + 1);
                AfterEdit(_engine.RecomposeParagraph(_caretParagraph));
            }
            else if (_caretParagraph < document.Paragraphs.Count - 1)
            {
                PivotEdit.MergeInto(document.Paragraphs[_caretParagraph],
                    document.Paragraphs[_caretParagraph + 1]);
                document.Paragraphs.RemoveAt(_caretParagraph + 1);
                AfterEdit(_engine.ParagraphRemoved(_caretParagraph + 1, _caretParagraph));
            }
        }

        public void InsertParagraphBreak()
        {
            PushUndo(false);
            DeleteSelectionIfAny();
            var document = _item.Document;
            var tail = PivotEdit.Split(document.Paragraphs[_caretParagraph], _caretOffset);
            document.Paragraphs.Insert(_caretParagraph + 1, tail);
            _caretParagraph++;
            _caretOffset = 0;
            AfterEdit(_engine.ParagraphInserted(_caretParagraph));
        }

        private void InsertLineBreak()
        {
            PushUndo(false);
            DeleteSelectionIfAny();
            PivotEdit.InsertElement(_item.Document.Paragraphs[_caretParagraph], _caretOffset,
                new TextRun { IsLineBreak = true });
            _caretOffset++;
            AfterEdit(_engine.RecomposeParagraph(_caretParagraph));
        }

        public void TogglePageBreak()
        {
            PushUndo(false);
            var paragraph = _item.Document.Paragraphs[_caretParagraph];
            paragraph.PageBreakBefore = !paragraph.PageBreakBefore;
            AfterEdit(_engine.RecomposeParagraph(_caretParagraph));
        }

        /// <summary>Paragraph alignment override for the selection (or the
        /// caret paragraph): null clears back to the style's alignment.</summary>
        public void ApplyAlign(string align)
        {
            PushUndo(false);
            int pa, pb;
            if (HasSelection())
            {
                int oa, ob;
                OrderedSelection(out pa, out oa, out pb, out ob);
            }
            else { pa = _caretParagraph; pb = _caretParagraph; }
            for (var p = pa; p <= pb; p++)
            {
                var paragraph = _item.Document.Paragraphs[p];
                var style = _styles.Find(paragraph.StyleId);
                paragraph.AlignOverride = align == style.Align ? null : align;
                _engine.RecomposeParagraph(p);
            }
            AfterEdit(0);
        }

        // ============================================================ clipboard

        private void CopySelection(bool cut)
        {
            if (!HasSelection()) return;
            int pa, oa, pb, ob;
            OrderedSelection(out pa, out oa, out pb, out ob);
            var sb = new StringBuilder();
            for (var p = pa; p <= pb; p++)
            {
                var text = PivotEdit.FlatText(_item.Document.Paragraphs[p]);
                var from = p == pa ? Math.Min(oa, text.Length) : 0;
                var to = p == pb ? Math.Min(ob, text.Length) : text.Length;
                sb.Append(text.Substring(from, Math.Max(0, to - from)).Replace("￼", ""));
                if (p < pb) sb.AppendLine();
            }
            try { Clipboard.SetText(sb.ToString()); }
            catch { }
            if (cut)
            {
                PushUndo(false);
                DeleteSelectionIfAny();
                AfterEdit(0);
            }
        }

        private void Paste()
        {
            string text;
            try { text = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch { text = null; }
            if (string.IsNullOrEmpty(text)) return;
            PushUndo(false);
            DeleteSelectionIfAny();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            PivotEdit.InsertText(_item.Document.Paragraphs[_caretParagraph], _caretOffset, lines[0]);
            _caretOffset += lines[0].Length;
            var first = _engine.RecomposeParagraph(_caretParagraph);
            for (var i = 1; i < lines.Length; i++)
            {
                var tail = PivotEdit.Split(_item.Document.Paragraphs[_caretParagraph], _caretOffset);
                _item.Document.Paragraphs.Insert(_caretParagraph + 1, tail);
                _caretParagraph++;
                _caretOffset = 0;
                _engine.ParagraphInserted(_caretParagraph);
                if (lines[i].Length > 0)
                {
                    PivotEdit.InsertText(_item.Document.Paragraphs[_caretParagraph], 0, lines[i]);
                    _caretOffset = lines[i].Length;
                    _engine.RecomposeParagraph(_caretParagraph);
                }
            }
            AfterEdit(Math.Min(first, 0));
        }

        // ============================================================ formatting

        private void ApplyToSelection(Action<TextRun> setter)
        {
            if (!HasSelection()) return;
            PushUndo(false);
            int pa, oa, pb, ob;
            OrderedSelection(out pa, out oa, out pb, out ob);
            for (var p = pa; p <= pb; p++)
            {
                var paragraph = _item.Document.Paragraphs[p];
                var from = p == pa ? oa : 0;
                var to = p == pb ? ob : PivotEdit.FlatLength(paragraph);
                PivotEdit.ApplyFormat(paragraph, from, to, setter);
                _engine.RecomposeParagraph(p);
            }
            AfterEdit(0);
        }

        private bool SelectionAll(Func<TextRun, ParagraphStyle, bool> predicate)
        {
            int pa, oa, pb, ob;
            OrderedSelection(out pa, out oa, out pb, out ob);
            for (var p = pa; p <= pb; p++)
            {
                var paragraph = _item.Document.Paragraphs[p];
                var style = _styles.Find(paragraph.StyleId);
                var from = p == pa ? oa : 0;
                var to = p == pb ? ob : PivotEdit.FlatLength(paragraph);
                if (!PivotEdit.RangeHas(paragraph, from, to, style, predicate)) return false;
            }
            return true;
        }

        public void ToggleBold()
        {
            if (!HasSelection()) return;
            var allBold = SelectionAll(delegate(TextRun run, ParagraphStyle style)
            { return run.Bold ?? style.Bold; });
            ApplyToSelection(delegate(TextRun run) { run.Bold = allBold ? (bool?)false : true; });
        }

        public void ToggleItalic()
        {
            if (!HasSelection()) return;
            var all = SelectionAll(delegate(TextRun run, ParagraphStyle style)
            { return run.Italic ?? style.Italic; });
            ApplyToSelection(delegate(TextRun run) { run.Italic = all ? (bool?)false : true; });
        }

        public void ToggleUnderline()
        {
            if (!HasSelection()) return;
            var all = SelectionAll(delegate(TextRun run, ParagraphStyle style)
            { return run.Underline == true; });
            ApplyToSelection(delegate(TextRun run) { run.Underline = all ? (bool?)null : true; });
        }

        /// <summary>Approche (millièmes de cadratin) ajoutée à la sélection —
        /// l'outil fin contre les veuves/orphelines tenaces.</summary>
        public void ApplyTracking(double delta)
        {
            ApplyToSelection(delegate(TextRun run)
            {
                var value = (run.Tracking ?? 0) + delta;
                run.Tracking = Math.Abs(value) < 0.01
                    ? (double?)null
                    : Math.Max(-100, Math.Min(400, value));
            });
        }

        /// <summary>Approche ABSOLUE sur la sélection (champ de valeur).</summary>
        public void SetTracking(double value)
        {
            var clamped = Math.Max(-100, Math.Min(400, value));
            ApplyToSelection(delegate(TextRun run)
            {
                run.Tracking = Math.Abs(clamped) < 0.01 ? (double?)null : clamped;
            });
        }

        /// <summary>Approche de la sélection (ou du run au caret) : valeur
        /// uniforme (0 = aucune), null = mixte.</summary>
        public double? SelectionTracking()
        {
            if (_item == null) return 0;
            int pa, oa, pb, ob;
            if (HasSelection()) OrderedSelection(out pa, out oa, out pb, out ob);
            else
            {
                pa = pb = _caretParagraph;
                oa = Math.Max(0, _caretOffset - 1);
                ob = _caretOffset;
            }
            double? found = null;
            var mixed = false;
            for (var p = pa; p <= pb && !mixed; p++)
            {
                var paragraph = _item.Document.Paragraphs[p];
                var offset = 0;
                foreach (var run in paragraph.Runs)
                {
                    var length = run.IsLineBreak || run.IsRule || run.ImageId != null
                        || run.FootnoteId != null ? 1 : (run.Text ?? "").Length;
                    var from = p == pa ? oa : 0;
                    var to = p == pb ? ob : int.MaxValue;
                    var overlaps = offset < to && offset + length > from;
                    offset += length;
                    if (!overlaps || run.FootnoteId != null || run.ImageId != null
                        || run.IsRule || run.IsLineBreak) continue;
                    var value = run.Tracking ?? 0;
                    if (!found.HasValue) found = value;
                    else if (Math.Abs(found.Value - value) > 0.01) { mixed = true; break; }
                }
            }
            return mixed ? (double?)null : (found ?? 0);
        }

        public void ToggleStrike()
        {
            if (!HasSelection()) return;
            var all = SelectionAll(delegate(TextRun run, ParagraphStyle style)
            { return run.Strike == true; });
            ApplyToSelection(delegate(TextRun run) { run.Strike = all ? (bool?)null : true; });
        }

        public void ApplyFont(string family)
        {
            ApplyToSelection(delegate(TextRun run) { run.FontFamily = family; });
        }

        public void ApplyWeight(string weight) // null = Normal
        {
            ApplyToSelection(delegate(TextRun run)
            {
                run.Weight = weight;
                run.Bold = weight == null ? (bool?)false : null;
            });
        }

        public void ApplySizePx(double px)
        {
            ApplyToSelection(delegate(TextRun run) { run.FontSize = px; });
        }

        public void ApplyColor(string hex) // null = automatic
        {
            ApplyToSelection(delegate(TextRun run) { run.Color = hex; });
        }

        public void ApplyHighlight(string hex) // null = none
        {
            ApplyToSelection(delegate(TextRun run) { run.Highlight = hex; });
        }

        /// <summary>État effectif de la sélection (ou du caret) pour les
        /// bascules du ruban : gras/italique/souligné/barré, alignement et
        /// liste — la synchro qui manquait aux boutons en Composition.</summary>
        public void SelectionFlags(out bool bold, out bool italic, out bool underline,
            out bool strike, out string align, out string listKind)
        {
            bold = italic = underline = strike = false;
            align = "left";
            listKind = null;
            if (_item == null || _caretParagraph >= _item.Document.Paragraphs.Count) return;
            var paragraph = _item.Document.Paragraphs[_caretParagraph];
            var style = _styles.Find(paragraph.StyleId);
            align = paragraph.AlignOverride ?? style.Align ?? "left";
            listKind = paragraph.ListKind;
            if (HasSelection())
            {
                bold = SelectionAll(delegate(TextRun run, ParagraphStyle st)
                { return run.Bold ?? st.Bold; });
                italic = SelectionAll(delegate(TextRun run, ParagraphStyle st)
                { return run.Italic ?? st.Italic; });
                underline = SelectionAll(delegate(TextRun run, ParagraphStyle st)
                { return run.Underline == true; });
                strike = SelectionAll(delegate(TextRun run, ParagraphStyle st)
                { return run.Strike == true; });
                return;
            }
            // Sans sélection : le run sous le caret (ou juste avant — règle du
            // traitement de texte).
            var reference = RunAtCaret(paragraph);
            bold = reference != null ? (reference.Bold ?? style.Bold) : style.Bold;
            italic = reference != null ? (reference.Italic ?? style.Italic) : style.Italic;
            underline = reference != null && reference.Underline == true;
            strike = reference != null && reference.Strike == true;
        }

        private TextRun RunAtCaret(TextParagraph paragraph)
        {
            int runIndex, inner;
            PivotEdit.Locate(paragraph, _caretOffset, out runIndex, out inner);
            if (inner == 0 && _caretOffset > 0)
                PivotEdit.Locate(paragraph, _caretOffset - 1, out runIndex, out inner);
            if (runIndex >= paragraph.Runs.Count) return null;
            var run = paragraph.Runs[runIndex];
            return PivotEdit.IsElement(run) ? null : run;
        }

        // ============================================================= révision

        /// <summary>Ancre une annotation sur la sélection (faux sans sélection).</summary>
        public bool AnnotateSelection(string id)
        {
            if (!HasSelection()) return false;
            ApplyToSelection(delegate(TextRun run) { run.AnnotationId = id; });
            return true;
        }

        /// <summary>« Ne pas corriger » : bascule NoProof sur la sélection —
        /// tout-marqué → démarque, sinon marque tout (règle des bascules).
        /// Rend faux sans sélection (le ruban informe l'utilisateur).</summary>
        public bool ToggleNoProofSelection()
        {
            if (!HasSelection()) return false;
            var all = SelectionAll(delegate(TextRun run, ParagraphStyle style)
            { return run.NoProof; });
            ApplyToSelection(delegate(TextRun run) { run.NoProof = !all; });
            return true;
        }

        // ============================================================ correction

        // Les signalements affichés (posés par EditorView après chaque passe
        // du pilote) — la composition porte l'index par paragraphe du rendu.
        private List<Correction.Finding> _findings;

        /// <summary>« Ignorer ici » / « Ignorer dans ce projet » choisis au
        /// menu contextuel — le pilote (EditorView) applique et relance.</summary>
        public event Action<Correction.Finding> FindingIgnoreHere;
        public event Action<Correction.Finding> FindingIgnoreProject;
        /// <summary>« Ignorer cette règle » — grammatical seulement (batch 29).</summary>
        public event Action<Correction.Finding> FindingIgnoreRule;

        /// <summary>« Ajouter au dictionnaire » (batch 27, lot D) —
        /// portée : true = projet, false = partout.</summary>
        public event Action<Correction.Finding, bool> FindingLearn;

        /// <summary>Les suggestions d'un signalement, calculées À LA DEMANDE
        /// (jamais pendant la passe — posé par EditorView, cache côté
        /// vérificateur). Null : les suggestions portées par le signalement
        /// font foi.</summary>
        public Func<Correction.Finding, List<string>> SuggestionProvider;

        /// <summary>Les synonymes d'un mot tel qu'écrit (batch 44), posé par
        /// EditorView : la réponse PRÊTE, en attente, ou une notice —
        /// RefreshSynonyms remplira le sous-menu ouvert à l'arrivée.</summary>
        public Func<string, Correction.Grammalecte.SynonymAnswer> SynonymLookup;

        /// <summary>Le sous-menu « Synonymes » et ce qu'il vise.</summary>
        private sealed class SynonymTarget
        {
            public MenuItem Menu;
            public string Word;
            public int Paragraph, Start, Length;
        }
        private SynonymTarget _openSynonyms; // le sous-menu ouvert, s'il y en a un

        /// <summary>Les synonymes de ce mot viennent d'arriver : le sous-menu
        /// ouvert qui les attendait se remplit (sur le fil UI) — un autre
        /// mot ne le touche pas.</summary>
        public void RefreshSynonyms(string word)
        {
            var target = _openSynonyms;
            if (target == null || target.Word != word) return;
            FillSynonymMenu(target);
        }

        private void FillSynonymMenu(SynonymTarget target)
        {
            var lookup = SynonymLookup;
            var answer = lookup != null ? lookup(target.Word) : null;
            target.Menu.Items.Clear();
            if (answer == null || answer.Pending)
            {
                target.Menu.Items.Add(new MenuItem { Header = "Recherche…", IsEnabled = false });
                return;
            }
            if (answer.Words.Count == 0)
            {
                target.Menu.Items.Add(new MenuItem
                {
                    Header = answer.Notice.Length > 0 ? answer.Notice : "Aucun synonyme connu",
                    IsEnabled = false
                });
                return;
            }
            foreach (var candidate in answer.Words)
            {
                var replacement = Correction.Typography.KeepCase(target.Word, candidate);
                var item = new MenuItem { Header = replacement };
                item.Click += delegate
                {
                    ReplaceRange(target.Paragraph, target.Start, target.Length, replacement);
                };
                target.Menu.Items.Add(item);
            }
        }

        /// <summary>Affiche ces signalements (ondulés). Liste triée par le
        /// pilote ; null ou vide = plus rien à l'écran.</summary>
        public void SetFindings(List<Correction.Finding> findings)
        {
            _findings = findings;
            var composition = _engine == null ? null : _engine.Current;
            if (composition == null) return;
            if (findings == null || findings.Count == 0)
                composition.ScreenFindings = null;
            else
            {
                var byParagraph = new Dictionary<int, List<Correction.Finding>>();
                foreach (var finding in findings)
                {
                    List<Correction.Finding> list;
                    if (!byParagraph.TryGetValue(finding.ParagraphIndex, out list))
                    {
                        list = new List<Correction.Finding>();
                        byParagraph[finding.ParagraphIndex] = list;
                    }
                    list.Add(finding);
                }
                composition.ScreenFindings = byParagraph;
            }
            for (var k = 0; k < _pages.Children.Count; k++) PageAt(k).InvalidateVisual();
        }

        /// <summary>Sélectionne une plage plate et l'amène à l'écran (même
        /// mécanique que GoToAnnotation) — signalements ET résultats de
        /// recherche passent par là.</summary>
        public void SelectRange(int paragraphIndex, int start, int end)
        {
            if (_item == null
                || paragraphIndex >= _item.Document.Paragraphs.Count) return;
            var paragraph = _item.Document.Paragraphs[paragraphIndex];
            var flatLength = PivotEdit.FlatLength(paragraph);
            _anchorParagraph = paragraphIndex;
            _anchorOffset = Math.Min(start, flatLength);
            _caretParagraph = paragraphIndex;
            _caretOffset = Math.Min(end, flatLength);
            _caretDesiredX = -1;
            UpdateCaretVisual();
            Focus();
        }

        /// <summary>Sélectionne la plage d'un signalement.</summary>
        public void GoToFinding(Correction.Finding finding)
        {
            SelectRange(finding.ParagraphIndex, finding.Start, finding.End);
        }

        // ---------------------------------------------- bulles portées (B.4)

        /// <summary>La couche des bulles d'annotation, dans la colonne (elle
        /// suit le zoom et le défilement) — EditorView la peuple, la vue ne
        /// fait que fournir la géométrie.</summary>
        public Canvas AnnotationBubbleLayer { get { return _bubbleLayer; } }

        /// <summary>Le bord droit des pages dans la colonne (les bulles se
        /// posent au-delà), 0 sans composition.</summary>
        public double PagesRightX()
        {
            var composition = _engine == null ? null : _engine.Current;
            return composition == null ? 0 : composition.PageWidthPx;
        }

        /// <summary>Ordonnée, dans la colonne, de la ligne composée qui porte
        /// le DÉBUT du passage d'une annotation — ou -1 (annotation orpheline,
        /// composition absente).</summary>
        public double AnnotationAnchorY(string id)
        {
            var composition = _engine == null ? null : _engine.Current;
            if (_item == null || composition == null) return -1;
            for (var p = 0; p < _item.Document.Paragraphs.Count; p++)
            {
                var cursor = 0;
                var start = -1;
                foreach (var run in _item.Document.Paragraphs[p].Runs)
                {
                    var length = PivotEdit.IsElement(run) ? 1 : run.Text.Length;
                    if (run.AnnotationId == id) { start = cursor; break; }
                    cursor += length;
                }
                if (start < 0) continue;
                var stride = composition.PageHeightPx + PageGapPx;
                double firstOfParagraph = -1;
                for (var pageIndex = 0; pageIndex < composition.Pages.Count; pageIndex++)
                    foreach (var placed in composition.Pages[pageIndex].Lines)
                    {
                        if (placed.ParagraphIndex != p) continue;
                        var line = composition.Paragraphs[p].Lines[placed.LineIndex];
                        if (firstOfParagraph < 0)
                            firstOfParagraph = pageIndex * stride + placed.Y;
                        if (start >= line.Start && start < Math.Max(line.Start + 1, line.End))
                            return pageIndex * stride + placed.Y;
                    }
                return firstOfParagraph; // repli : la première ligne du paragraphe
            }
            return -1;
        }

        /// <summary>Remplace une plage plate par un texte — une édition
        /// normale (undo, recomposition, projet sale). Le remplacement de la
        /// recherche pivot passe par là.</summary>
        public void ReplaceRange(int paragraphIndex, int start, int length, string text)
        {
            if (ReadOnly || _item == null
                || paragraphIndex >= _item.Document.Paragraphs.Count) return;
            var paragraph = _item.Document.Paragraphs[paragraphIndex];
            if (start + length > PivotEdit.FlatLength(paragraph)) return; // périmé
            PushUndo(false);
            PivotEdit.ReplaceText(paragraph, start, start + length, text); // garde le format (b37)
            _engine.RecomposeParagraph(paragraphIndex);
            _caretParagraph = paragraphIndex;
            _caretOffset = start + (text == null ? 0 : text.Length);
            ClearSelection();
            AfterEdit(0);
        }

        /// <summary>La position du caret (navigation des signalements).</summary>
        public void CaretLocation(out int paragraph, out int offset)
        {
            paragraph = _caretParagraph;
            offset = _caretOffset;
        }

        /// <summary>Remplace la plage d'un signalement par une suggestion.
        /// Public : le panneau Correction (EditorView) applique en un clic.</summary>
        public void ApplySuggestion(Correction.Finding finding, string suggestion)
        {
            ReplaceRange(finding.ParagraphIndex, finding.Start, finding.Length, suggestion);
        }

        /// <summary>« Tout remplacer » de la recherche pivot : UNE étape
        /// d'annulation pour toute la passe, remplacements à REBOURS (les
        /// offsets des matchs précédents restent justes), recomposition des
        /// seuls paragraphes touchés.</summary>
        public int ReplaceAll(List<PivotSearch.Match> matches, string text)
        {
            if (_item == null || matches == null || matches.Count == 0) return 0;
            PushUndo(false);
            var count = 0;
            var touched = new HashSet<int>();
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                if (match.ParagraphIndex >= _item.Document.Paragraphs.Count) continue;
                var paragraph = _item.Document.Paragraphs[match.ParagraphIndex];
                if (match.Start + match.Length > PivotEdit.FlatLength(paragraph)) continue;
                PivotEdit.ReplaceText(paragraph, match.Start, match.Start + match.Length, text); // garde le format (b37)
                touched.Add(match.ParagraphIndex);
                count++;
            }
            foreach (var index in touched)
                _engine.RecomposeParagraph(index);
            ClampCaret();
            ClearSelection();
            AfterEdit(0);
            return count;
        }

        /// <summary>Caractères d'impression (batch 35) : le drapeau du
        /// dessinateur, puis chaque page se redessine.</summary>
        public void SetFormattingMarks(bool visible)
        {
            ComposedRenderer.ShowMarks = visible;
            for (var k = 0; k < _pages.Children.Count; k++) PageAt(k).InvalidateVisual();
        }

        /// <summary>Remplace tous les paragraphes (la passe typographique,
        /// batch 34) : un cran d'annulation, recomposition intégrale.</summary>
        public void ReplaceParagraphs(List<TextParagraph> paragraphs)
        {
            if (ReadOnly || _item == null || paragraphs == null) return;
            PushUndo(false);
            _item.Document.Paragraphs.Clear();
            _item.Document.Paragraphs.AddRange(paragraphs);
            if (_item.Document.Paragraphs.Count == 0) _item.Document.Paragraphs.Add(new TextParagraph());
            _engine.ComposeAll();
            RebuildPages();
            ClampCaret();
            ClearSelection();
            AfterEdit(0);
        }

        /// <summary>Les signalements sous un offset donné (menu contextuel).</summary>
        private List<Correction.Finding> FindingsAt(int paragraph, int offset)
        {
            var result = new List<Correction.Finding>();
            if (_findings == null) return result;
            foreach (var finding in _findings)
                if (finding.ParagraphIndex == paragraph
                    && offset >= finding.Start && offset <= finding.End)
                    result.Add(finding);
            return result;
        }

        /// <summary>L'annotation portée par le caret (le run sous lui, sinon
        /// celui juste avant — règle du traitement de texte), ou null.</summary>
        public string AnnotationAtCaret()
        {
            if (_item == null || _caretParagraph >= _item.Document.Paragraphs.Count) return null;
            var paragraph = _item.Document.Paragraphs[_caretParagraph];
            int runIndex, inner;
            PivotEdit.Locate(paragraph, _caretOffset, out runIndex, out inner);
            if (runIndex < paragraph.Runs.Count && paragraph.Runs[runIndex].AnnotationId != null)
                return paragraph.Runs[runIndex].AnnotationId;
            if (inner == 0 && _caretOffset > 0)
            {
                PivotEdit.Locate(paragraph, _caretOffset - 1, out runIndex, out inner);
                if (runIndex < paragraph.Runs.Count)
                    return paragraph.Runs[runIndex].AnnotationId;
            }
            return null;
        }

        /// <summary>Sélectionne le passage d'une annotation et l'amène à
        /// l'écran. Faux si l'ancre a disparu.</summary>
        public bool GoToAnnotation(string id)
        {
            if (_item == null) return false;
            for (var p = 0; p < _item.Document.Paragraphs.Count; p++)
            {
                var paragraph = _item.Document.Paragraphs[p];
                var cursor = 0;
                var start = -1;
                var end = -1;
                foreach (var run in paragraph.Runs)
                {
                    var length = PivotEdit.IsElement(run) ? 1 : run.Text.Length;
                    if (run.AnnotationId == id)
                    {
                        if (start < 0) start = cursor;
                        end = cursor + length;
                    }
                    cursor += length;
                }
                if (start < 0) continue;
                _anchorParagraph = p;
                _anchorOffset = start;
                _caretParagraph = p;
                _caretOffset = end;
                _caretDesiredX = -1;
                UpdateCaretVisual();
                return true;
            }
            return false;
        }

        /// <summary>Efface l'ancre d'une annotation dans tout le document
        /// (suppression de l'annotation) et recompose les paragraphes touchés.</summary>
        public void ClearAnnotation(string id)
        {
            if (_item == null) return;
            PushUndo(false);
            for (var p = 0; p < _item.Document.Paragraphs.Count; p++)
            {
                var touched = false;
                foreach (var run in _item.Document.Paragraphs[p].Runs)
                    if (run.AnnotationId == id) { run.AnnotationId = null; touched = true; }
                if (touched) _engine.RecomposeParagraph(p);
            }
            AfterEdit(0);
        }

        /// <summary>Recompose les paragraphes porteurs d'une annotation (la
        /// teinte suit l'état résolu/actif).</summary>
        public void RefreshAnnotation(string id)
        {
            if (_item == null || _engine == null) return;
            for (var p = 0; p < _item.Document.Paragraphs.Count; p++)
                foreach (var run in _item.Document.Paragraphs[p].Runs)
                    if (run.AnnotationId == id)
                    {
                        _engine.RecomposeParagraph(p);
                        break;
                    }
            AfterEdit(0);
        }

        public void ApplyStyle(string styleId)
        {
            PushUndo(false);
            int pa, pb;
            if (HasSelection())
            {
                int oa, ob;
                OrderedSelection(out pa, out oa, out pb, out ob);
            }
            else { pa = _caretParagraph; pb = _caretParagraph; }
            for (var p = pa; p <= pb; p++)
            {
                _item.Document.Paragraphs[p].StyleId = styleId;
                _engine.RecomposeParagraph(p);
            }
            AfterEdit(0);
        }

        public void ApplyList(string kind) // "bullet" | "number" | null, toggles
        {
            PushUndo(false);
            int pa, pb;
            if (HasSelection())
            {
                int oa, ob;
                OrderedSelection(out pa, out oa, out pb, out ob);
            }
            else { pa = _caretParagraph; pb = _caretParagraph; }
            var allAlready = true;
            for (var p = pa; p <= pb; p++)
                if (_item.Document.Paragraphs[p].ListKind != kind) allAlready = false;
            for (var p = pa; p <= pb; p++)
            {
                _item.Document.Paragraphs[p].ListKind = allAlready ? null : kind;
                _engine.RecomposeParagraph(p);
            }
            AfterEdit(0);
        }

        public void InsertElementAtCaret(TextRun element)
        {
            PushUndo(false);
            DeleteSelectionIfAny();
            PivotEdit.InsertElement(_item.Document.Paragraphs[_caretParagraph],
                _caretOffset, element);
            _caretOffset++;
            AfterEdit(_engine.RecomposeParagraph(_caretParagraph));
        }

        public void InsertFootnoteAtCaret()
        {
            PushUndo(false);
            DeleteSelectionIfAny();
            var note = new Footnote();
            _item.Document.Footnotes.Add(note);
            PivotEdit.InsertElement(_item.Document.Paragraphs[_caretParagraph],
                _caretOffset, new TextRun { FootnoteId = note.Id });
            _caretOffset++;
            AfterEdit(_engine.RecomposeParagraph(_caretParagraph));
        }

        /// <summary>Caret paragraph and offset, exposed for the shell.</summary>
        public void GetCaret(out int paragraph, out int offset)
        {
            paragraph = _caretParagraph;
            offset = _caretOffset;
        }

        /// <summary>Font family at the caret (run override, else style).</summary>
        /// <summary>Style id, font family and size (pt) at the caret — feeds
        /// the ribbon combos in Composition mode.</summary>
        public void CaretFormat(out string styleId, out string fontFamily, out double sizePt)
        {
            styleId = "body";
            fontFamily = null;
            sizePt = 12;
            if (_item == null || _caretParagraph >= _item.Document.Paragraphs.Count) return;
            var paragraph = _item.Document.Paragraphs[_caretParagraph];
            var style = _styles.Find(paragraph.StyleId);
            styleId = style.Id;
            fontFamily = style.FontFamily;
            var sizePx = style.FontSize;
            int runIndex, inner;
            PivotEdit.Locate(paragraph, Math.Max(0, _caretOffset - 1), out runIndex, out inner);
            if (runIndex < paragraph.Runs.Count && !PivotEdit.IsElement(paragraph.Runs[runIndex]))
            {
                var run = paragraph.Runs[runIndex];
                if (run.FontFamily != null) fontFamily = run.FontFamily;
                if (run.FontSize.HasValue) sizePx = run.FontSize.Value;
            }
            sizePt = sizePx * 0.75;
        }

        public string GetCaretFontFamily()
        {
            if (_item == null) return null;
            var paragraph = _item.Document.Paragraphs[_caretParagraph];
            var style = _styles.Find(paragraph.StyleId);
            int runIndex, inner;
            PivotEdit.Locate(paragraph, Math.Max(0, _caretOffset - 1), out runIndex, out inner);
            if (runIndex < paragraph.Runs.Count && !PivotEdit.IsElement(paragraph.Runs[runIndex])
                && paragraph.Runs[runIndex].FontFamily != null)
                return paragraph.Runs[runIndex].FontFamily;
            return style.FontFamily;
        }

        /// <summary>Weight of the selection (caret char when collapsed):
        /// null = Normal, a weight name, or "mixed".</summary>
        public string GetSelectionWeightName()
        {
            if (_item == null) return "mixed";
            int pa, oa, pb, ob;
            if (HasSelection()) OrderedSelection(out pa, out oa, out pb, out ob);
            else
            {
                pa = _caretParagraph;
                pb = _caretParagraph;
                oa = Math.Max(0, _caretOffset - 1);
                ob = _caretOffset;
            }
            var found = "unset";
            for (var p = pa; p <= pb; p++)
            {
                var paragraph = _item.Document.Paragraphs[p];
                var style = _styles.Find(paragraph.StyleId);
                var from = p == pa ? oa : 0;
                var to = p == pb ? ob : PivotEdit.FlatLength(paragraph);
                var cursor = 0;
                foreach (var run in paragraph.Runs)
                {
                    var length = PivotEdit.IsElement(run) ? 1 : run.Text.Length;
                    if (cursor + length > from && cursor < to && !PivotEdit.IsElement(run))
                    {
                        var name = run.Weight ?? ((run.Bold ?? style.Bold) ? "Bold" : null);
                        if (found == "unset") found = name;
                        else if (found != name) return "mixed";
                    }
                    cursor += length;
                }
            }
            return found == "unset" ? null : found;
        }
    }
}

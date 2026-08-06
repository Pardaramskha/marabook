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
        private readonly Canvas _overlay;   // caret + selection
        private readonly System.Windows.Shapes.Rectangle _caretBar;
        private readonly DispatcherTimer _blink;
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
        public event Action ExitRequested; // Échap : retour à l'éditeur classique

        public ComposedView()
        {
            Background = Brushes.Transparent;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Focusable = true;
            FocusVisualStyle = null;

            _pages = new StackPanel();
            _overlay = new Canvas { IsHitTestVisible = false };
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
            _column.Children.Add(_pages);
            _column.Children.Add(_overlay);
            Content = _column;

            _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
            _blink.Tick += delegate
            {
                _caretBar.Visibility = _caretBar.Visibility == Visibility.Visible && _item != null
                    ? Visibility.Hidden : (_item != null ? Visibility.Visible : Visibility.Collapsed);
            };

            PreviewMouseLeftButtonDown += OnMouseDown;
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
        public bool CanUndo { get { return _undo.Count > 0; } }
        public bool CanRedo { get { return _redo.Count > 0; } }

        // ============================================================ lifecycle

        public void Attach(BinderItem item, StyleSheet styles, PageSetup setup, Project project)
        {
            _item = item;
            _styles = styles;
            _setup = setup;
            _project = project;
            // appendNotes: footnotes sit at the bottom of their page, like on
            // paper — the composed surface is print-exact.
            _engine = new CompositionEngine(item.Document, styles, setup, project, true);
            _engine.ComposeAll();
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
            _item = null;
            _engine = null;
            _blink.Stop();
            _pages.Children.Clear();
            ClearOverlay();
            _caretBar.Visibility = Visibility.Collapsed;
        }

        public void SetZoom(double factor)
        {
            _zoom = Math.Max(0.5, Math.Min(3.0, factor));
            _column.LayoutTransform = Math.Abs(_zoom - 1.0) < 0.001
                ? null : new ScaleTransform(_zoom, _zoom);
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
            {
                var element = new PageElement(this, _pages.Children.Count);
                element.Margin = new Thickness(0, _pages.Children.Count == 0 ? 0 : PageGapPx, 0, 0);
                _pages.Children.Add(element);
            }
            foreach (PageElement element in _pages.Children) element.InvalidateVisual();
        }

        private void RefreshPages(int firstChanged)
        {
            var composition = _engine.Current;
            if (_pages.Children.Count != composition.Pages.Count) { RebuildPages(); return; }
            if (firstChanged == int.MaxValue) return;
            for (var k = firstChanged; k < _pages.Children.Count; k++)
                ((PageElement)_pages.Children[k]).InvalidateVisual();
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
            private readonly ComposedView _owner;
            private readonly int _index;

            public PageElement(ComposedView owner, int index)
            {
                _owner = owner;
                _index = index;
                SnapsToDevicePixels = true;
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
                dc.DrawRectangle(Brushes.White, new Pen(Chrome.Border, 1),
                    new Rect(0.5, 0.5, composition.PageWidthPx - 1, composition.PageHeightPx - 1));
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

        private double CaretX(ComposedLine line, int offset)
        {
            var x = _engine.Current.LeftPx;
            double best = -1;
            foreach (var piece in line.Pieces)
            {
                if (piece.SourceStart < 0 || piece.SourceLength <= 0) continue;
                if (offset <= piece.SourceStart)
                {
                    if (best < 0) best = _engine.Current.LeftPx + piece.Origin.X;
                    continue;
                }
                if (offset <= piece.SourceStart + piece.SourceLength)
                {
                    var into = offset - piece.SourceStart;
                    var dx = into == 0 ? 0 : piece.CharRights[Math.Min(into, piece.CharRights.Length) - 1];
                    return _engine.Current.LeftPx + piece.Origin.X + dx;
                }
                x = _engine.Current.LeftPx + piece.Origin.X
                    + (piece.CharRights != null && piece.CharRights.Length > 0
                        ? piece.CharRights[piece.CharRights.Length - 1]
                        : 0);
            }
            return best >= 0 && offset <= line.Start ? best : x;
        }

        private int OffsetFromX(ComposedLine line, double xPage)
        {
            var left = _engine.Current.LeftPx;
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
            var x = CaretX(line, _caretOffset);
            var y = PageTop(pageIndex) + lineY;
            Canvas.SetLeft(_caretBar, x);
            Canvas.SetTop(_caretBar, y + 1);
            _caretBar.Height = Math.Max(8, line.Height - 2);
            _caretBar.Visibility = Visibility.Visible;

            EnsureCaretVisible(y, line.Height);
            RaisePageInfo();
        }

        private void EnsureCaretVisible(double y, double height)
        {
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
                    var x1 = CaretX(line, from);
                    var x2 = CaretX(line, to);
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
            handler(pageIndex + 1, Math.Max(1, _engine.Current.Pages.Count));
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
            Focus();
            int paragraph, offset;
            if (!HitTestPosition(e, out paragraph, out offset)) return;

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
            offset = OffsetFromX(chosenLine, point.X);
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
                    var exit = ExitRequested;
                    if (exit != null) exit();
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
            if (_caretDesiredX < 0) _caretDesiredX = CaretX(line, _caretOffset);

            // Find the placed line above/below in reading order.
            var composition = _engine.Current;
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
            _caretOffset = OffsetFromX(targetLine, _caretDesiredX);
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

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
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
            if (_redo.Count == 0) return false;
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

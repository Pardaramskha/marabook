using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Model;
using Marabook.Print;

namespace Marabook.App
{
    /// <summary>Les notes de bas de page éditées EN PLACE par le compositeur
    /// maison (27/09/2026, décision de Rémi pour le portage Avalonia — avant,
    /// un RichTextBox WPF posé sur la note). Le caret entre dans la note
    /// composée au bas de sa page, la frappe passe par PivotEdit sur les runs
    /// de la note et la note se recompose à chaque frappe : ce qu'on voit est
    /// la page. « Sans options » : ni gras, ni police, ni style depuis le
    /// ruban tant qu'une note est ouverte — les formats déjà portés par les
    /// runs sont conservés, la frappe hérite du run voisin. Entrée, Échap ou
    /// un clic sur la page referment ; la note reste ouverte quand le clavier
    /// part au ruban ou à la Pile. Les offsets de la note sont ceux de son
    /// texte plat (ses runs) ; le paragraphe composé porte en plus le préfixe
    /// « n. » (CompositionEngine.NotePrefix) : flat = préfixe + offset.</summary>
    public partial class ComposedView
    {
        // La note ouverte : son id, son index dans l'ordre des appels (l'index
        // des NoteParagraphs de la composition), le caret et l'ancre de la
        // sélection (-1 = pas de sélection) en offsets du corps de la note.
        private string _editingNoteId;
        private int _editingNoteIndex = -1;
        private int _noteCaret;
        private int _noteAnchor = -1;
        private double _noteDesiredX = -1; // mémoire de colonne pour haut/bas

        /// <summary>La note ouverte en place, ou null.</summary>
        public string EditingNoteId { get { return _editingNoteId; } }

        /// <summary>Vrai quand une note est ouverte : la frappe, le clavier et
        /// la sélection sont à elle ; les formats du ruban se taisent.</summary>
        private bool NoteEditing { get { return _editingNoteId != null; } }

        private Footnote EditingNote
        {
            get { return _item == null || _editingNoteId == null ? null : _item.Document.FindFootnote(_editingNoteId); }
        }

        private int NotePrefixLength { get { return CompositionEngine.NotePrefix(Math.Max(0, _editingNoteIndex)).Length; } }

        /// <summary>Le corps de la note comme paragraphe (clone des runs) : la
        /// surface sur laquelle PivotEdit travaille avant CommitNote.</summary>
        private static TextParagraph NoteParagraph(Footnote note)
        {
            return note.ToParagraph(StyleSheet.FootnoteId);
        }

        private int NoteLength()
        {
            var note = EditingNote;
            return note == null ? 0 : PivotEdit.FlatLength(NoteParagraph(note));
        }

        private string NoteFlatText()
        {
            var note = EditingNote;
            return note == null ? "" : PivotEdit.FlatText(NoteParagraph(note));
        }

        private bool HasNoteSelection()
        {
            return _noteAnchor >= 0 && _noteAnchor != _noteCaret;
        }

        private void OrderedNoteSelection(out int a, out int b)
        {
            a = Math.Min(_noteAnchor, _noteCaret);
            b = Math.Max(_noteAnchor, _noteCaret);
        }

        // ============================================================ ouverture / fermeture

        /// <summary>Ouvre la note en place, le caret en fin de note ; Entrée,
        /// Échap ou un clic ailleurs referment.</summary>
        public void EditNote(string id)
        {
            EditNoteAt(id, int.MaxValue);
        }

        /// <summary>Ouvre la note en place avec le caret à cet offset du corps
        /// (borné). La même note déjà ouverte ne fait que déplacer le caret.</summary>
        public void EditNoteAt(string id, int offset)
        {
            if (_item == null || _engine == null || id == null) return;
            var note = _item.Document.FindFootnote(id);
            if (note == null) return;
            var index = MarkerOrder().IndexOf(id);
            if (index < 0) return;
            var opening = _editingNoteId != id;
            if (opening) CloseNoteEditor(false);
            DeselectImage(false);
            _editingNoteId = id;
            _editingNoteIndex = index;
            _noteAnchor = -1;
            _noteDesiredX = -1;
            _noteCaret = Math.Max(0, Math.Min(NoteLength(), offset));
            if (opening) ClearSelection(); // la sélection du texte s'efface : le caret est à la note
            Focus();
            UpdateCaretVisual();
            if (opening)
            {
                var started = NoteEditingStarted;
                if (started != null) started(id);
            }
        }

        /// <summary>Referme la note ouverte (le texte est déjà dans le modèle,
        /// frappe par frappe) : le caret revient au texte.</summary>
        public void CloseNoteEditor(bool refocus)
        {
            if (_editingNoteId == null) { _editingNoteIndex = -1; return; }
            _editingNoteId = null;
            _editingNoteIndex = -1;
            _noteAnchor = -1;
            if (_item != null && _engine != null) UpdateCaretVisual(); // le caret du texte revient
            RaiseSelectionState(); // le ruban revient au texte
            if (refocus) Focus();
        }

        /// <summary>Après une annulation ou une recomposition : la note ouverte
        /// existe-t-elle encore, le caret tient-il dans son texte ?</summary>
        private void ClampNoteCaret()
        {
            if (!NoteEditing) return;
            var note = EditingNote;
            var index = note == null ? -1 : MarkerOrder().IndexOf(_editingNoteId);
            if (note == null || index < 0) { CloseNoteEditor(false); return; }
            _editingNoteIndex = index;
            var length = NoteLength();
            _noteCaret = Math.Max(0, Math.Min(length, _noteCaret));
            if (_noteAnchor > length) _noteAnchor = length;
        }

        // ============================================================ géométrie

        /// <summary>Les lignes posées de la note ouverte, dans l'ordre de
        /// lecture, avec la page de chacune.</summary>
        private List<PlacedLine> NoteLines(out List<int> pages)
        {
            var lines = new List<PlacedLine>();
            pages = new List<int>();
            var composition = _engine.Current;
            for (var k = 0; k < composition.Pages.Count; k++)
                foreach (var placed in composition.Pages[k].NoteLines)
                {
                    if (placed.ParagraphIndex != _editingNoteIndex) continue;
                    lines.Add(placed);
                    pages.Add(k);
                }
            return lines;
        }

        /// <summary>La ligne posée de la note qui porte cet offset du corps
        /// (la première dont la fin dépasse l'offset plat, sinon la dernière).</summary>
        private ComposedLine NoteLineOf(int noteOffset, out int pageIndex, out double lineY)
        {
            pageIndex = 0;
            lineY = 0;
            List<int> pages;
            var lines = NoteLines(out pages);
            if (lines.Count == 0) return null;
            var flat = NotePrefixLength + noteOffset;
            for (var i = 0; i < lines.Count; i++)
                if (flat < lines[i].Line.End)
                {
                    pageIndex = pages[i];
                    lineY = lines[i].Y;
                    return lines[i].Line;
                }
            var last = lines.Count - 1;
            pageIndex = pages[last];
            lineY = lines[last].Y;
            return lines[last].Line;
        }

        /// <summary>L'offset du corps de la note sous une abscisse de page, sur
        /// une ligne de la note (jamais dans le préfixe « n. »).</summary>
        private int NoteOffsetFromX(ComposedLine line, double xPage, double left)
        {
            return Math.Max(0, OffsetFromX(line, xPage, left) - NotePrefixLength);
        }

        /// <summary>La note dont le texte est sous ce point (zone des notes au
        /// bas d'une page), ou null.</summary>
        private string NoteAtPoint(Point point)
        {
            string id;
            int offset;
            return NoteHit(point, out id, out offset) ? id : null;
        }

        /// <summary>La note sous ce point ET l'offset de son corps sous
        /// l'abscisse ; false hors de toute note.</summary>
        private bool NoteHit(Point point, out string id, out int offset)
        {
            id = null;
            offset = 0;
            var composition = _engine == null ? null : _engine.Current;
            if (composition == null || composition.Pages.Count == 0) return false;
            var stride = composition.PageHeightPx + PageGapPx;
            var pageIndex = Math.Max(0, Math.Min(composition.Pages.Count - 1, (int)(point.Y / stride)));
            var yInPage = point.Y - pageIndex * stride;
            var page = composition.Pages[pageIndex];
            var order = MarkerOrder();
            foreach (var placed in page.NoteLines)
            {
                if (placed.ParagraphIndex < 0 || placed.ParagraphIndex >= composition.NoteParagraphs.Count) continue;
                var line = placed.Line;
                if (yInPage < placed.Y - 1 || yInPage > placed.Y + line.Height + 1) continue;
                if (placed.ParagraphIndex >= order.Count) return false;
                id = order[placed.ParagraphIndex];
                var prefix = CompositionEngine.NotePrefix(placed.ParagraphIndex).Length;
                offset = Math.Max(0, OffsetFromX(line, point.X, composition.LeftPxFor(pageIndex)) - prefix);
                return true;
            }
            return false;
        }

        // ============================================================ caret et sélection

        /// <summary>Le caret et la sélection de la note ouverte : un trait
        /// d'accent sous chaque ligne de la note (elle est « ouverte »), la
        /// sélection dans ses lignes, la barre du caret à sa place.</summary>
        private void UpdateNoteCaretVisual()
        {
            List<int> pages;
            var lines = NoteLines(out pages);
            if (lines.Count == 0) { CloseNoteEditor(false); return; }
            var composition = _engine.Current;
            var accent = Chrome.Accent;
            for (var i = 0; i < lines.Count; i++)
            {
                var left = composition.LeftPxFor(pages[i]);
                var prefix = NotePrefixLength;
                var x0 = CaretX(lines[i].Line, Math.Max(lines[i].Line.Start, prefix), left);
                var width = Math.Max(24, composition.Setup.ContentWidthPx - (x0 - left));
                var rule = new Rectangle { Width = width, Height = 1, Fill = accent, Opacity = 0.9 };
                Canvas.SetLeft(rule, x0);
                Canvas.SetTop(rule, PageTop(pages[i]) + lines[i].Y + lines[i].Line.Height - 1);
                _overlay.Children.Insert(0, rule);
            }
            if (HasNoteSelection())
            {
                int a, b;
                OrderedNoteSelection(out a, out b);
                var brush = new SolidColorBrush(Chrome.Accent.Color) { Opacity = 0.45 };
                var prefix = NotePrefixLength;
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i].Line;
                    var from = Math.Max(line.Start, prefix + a);
                    var to = Math.Min(line.End, prefix + b);
                    if (from >= to) continue;
                    var left = composition.LeftPxFor(pages[i]);
                    var x1 = CaretX(line, from, left);
                    var x2 = CaretX(line, to, left);
                    if (x2 - x1 < 2) x2 = x1 + 2;
                    var rect = new Rectangle
                    {
                        Width = x2 - x1,
                        Height = Math.Max(2, line.Height - 1),
                        Fill = brush
                    };
                    Canvas.SetLeft(rect, x1);
                    Canvas.SetTop(rect, PageTop(pages[i]) + lines[i].Y);
                    _overlay.Children.Insert(0, rect);
                }
            }
            int pageIndex;
            double lineY;
            var caretLine = NoteLineOf(_noteCaret, out pageIndex, out lineY);
            if (caretLine == null) { _caretBar.IsVisible = false; return; }
            var x = CaretX(caretLine, NotePrefixLength + _noteCaret, composition.LeftPxFor(pageIndex));
            var y = PageTop(pageIndex) + lineY;
            Canvas.SetLeft(_caretBar, x);
            Canvas.SetTop(_caretBar, y + 1);
            _caretBar.Height = Math.Max(8, caretLine.Height - 2);
            _caretBar.IsVisible = true;
            EnsureCaretVisible(y, caretLine.Height);
        }

        /// <summary>Public pour les sondes : le caret de la note ouverte à cet
        /// offset (borné ; int.MaxValue = la fin).</summary>
        public void PlaceNoteCaretPublic(int offset)
        {
            if (NoteEditing) NotePlaceCaret(offset, false);
        }

        /// <summary>Place le caret de la note (Maj étend la sélection).</summary>
        private void NotePlaceCaret(int offset, bool extend)
        {
            var length = NoteLength();
            offset = Math.Max(0, Math.Min(length, offset));
            if (extend) { if (_noteAnchor < 0) _noteAnchor = _noteCaret; }
            else _noteAnchor = -1;
            _noteCaret = offset;
            _noteDesiredX = -1;
            UpdateCaretVisual();
        }

        /// <summary>Double-clic dans la note : le mot sous le caret.</summary>
        private void NoteSelectWordAt(int offset)
        {
            var text = NoteFlatText();
            if (text.Length == 0) return;
            int start, end;
            if (!PivotEdit.WordBounds(text, offset, out start, out end))
            {
                var i = Math.Min(offset, text.Length - 1);
                start = i;
                end = i;
            }
            _noteAnchor = start;
            _noteCaret = end;
            _noteDesiredX = -1;
            UpdateCaretVisual();
        }

        /// <summary>Le glisser de sélection dans la note ouverte : le caret
        /// suit l'abscisse sur la ligne de note survolée (ou la plus proche).</summary>
        private void NoteDragTo(Point point)
        {
            string id;
            int offset;
            if (NoteHit(point, out id, out offset) && id == _editingNoteId)
            {
                if (_noteAnchor < 0) _noteAnchor = _noteCaret;
                _noteCaret = offset;
                UpdateCaretVisual();
                return;
            }
            // Hors de la note : au-dessus = début, au-dessous = fin.
            List<int> pages;
            var lines = NoteLines(out pages);
            if (lines.Count == 0) return;
            var composition = _engine.Current;
            var top = PageTop(pages[0]) + lines[0].Y;
            var last = lines.Count - 1;
            var bottom = PageTop(pages[last]) + lines[last].Y + lines[last].Line.Height;
            if (_noteAnchor < 0) _noteAnchor = _noteCaret;
            if (point.Y < top) _noteCaret = 0;
            else if (point.Y > bottom) _noteCaret = NoteLength();
            else return;
            UpdateCaretVisual();
        }

        // ============================================================ déplacements

        private void NoteMoveHorizontal(int direction, bool extend, bool word)
        {
            if (!extend && HasNoteSelection())
            {
                // Une sélection : flèche gauche va à son début, droite à sa fin.
                int a, b;
                OrderedNoteSelection(out a, out b);
                _noteCaret = direction < 0 ? a : b;
                _noteAnchor = -1;
                _noteDesiredX = -1;
                UpdateCaretVisual();
                return;
            }
            var text = NoteFlatText();
            var target = _noteCaret + direction;
            if (word)
            {
                target = _noteCaret;
                if (direction < 0)
                {
                    while (target > 0 && !char.IsLetterOrDigit(text[target - 1])) target--;
                    while (target > 0 && char.IsLetterOrDigit(text[target - 1])) target--;
                }
                else
                {
                    while (target < text.Length && char.IsLetterOrDigit(text[target])) target++;
                    while (target < text.Length && !char.IsLetterOrDigit(text[target])) target++;
                }
            }
            NotePlaceCaret(target, extend);
        }

        private void NoteMoveVertical(int direction, bool extend)
        {
            List<int> pages;
            var lines = NoteLines(out pages);
            if (lines.Count == 0) return;
            var composition = _engine.Current;
            int pageIndex;
            double lineY;
            var line = NoteLineOf(_noteCaret, out pageIndex, out lineY);
            if (line == null) return;
            var current = -1;
            for (var i = 0; i < lines.Count; i++)
                if (ReferenceEquals(lines[i].Line, line)) { current = i; break; }
            if (current < 0) return;
            var left = composition.LeftPxFor(pageIndex);
            if (_noteDesiredX < 0) _noteDesiredX = CaretX(line, NotePrefixLength + _noteCaret, left) - left;
            var next = current + direction;
            if (next < 0 || next >= lines.Count) return;
            var targetLeft = composition.LeftPxFor(pages[next]);
            var offset = NoteOffsetFromX(lines[next].Line, targetLeft + _noteDesiredX, targetLeft);
            var desired = _noteDesiredX;
            NotePlaceCaret(offset, extend);
            _noteDesiredX = desired; // la mémoire de colonne survit au déplacement
        }

        private void NoteHomeEnd(bool home, bool extend, bool wholeNote)
        {
            if (wholeNote) { NotePlaceCaret(home ? 0 : NoteLength(), extend); return; }
            int pageIndex;
            double lineY;
            var line = NoteLineOf(_noteCaret, out pageIndex, out lineY);
            if (line == null) return;
            var prefix = NotePrefixLength;
            var target = home ? Math.Max(0, line.Start - prefix) : Math.Max(0, line.End - prefix);
            NotePlaceCaret(target, extend);
        }

        private void NoteSelectAll()
        {
            _noteAnchor = 0;
            _noteCaret = NoteLength();
            _noteDesiredX = -1;
            UpdateCaretVisual();
        }

        // ============================================================ édition

        /// <summary>Après une frappe dans la note : les notes se recomposent,
        /// la vue ne bouge pas (la recomposition ramenait le caret du TEXTE
        /// dans la fenêtre et la page sautait à chaque lettre), le caret de la
        /// note se repose, le projet est marqué modifié.</summary>
        private void AfterNoteEdit()
        {
            _keepScroll = true;
            try
            {
                var firstChanged = _engine.RefreshNotes();
                if (firstChanged != int.MaxValue) RefreshPages(firstChanged);
                ClampNoteCaret();
                UpdateCaretVisual();
                RaisePageInfo();
            }
            finally { _keepScroll = false; }
            var handler = Edited;
            if (handler != null) handler();
        }

        /// <summary>Efface la sélection de la note ; vrai si elle existait.</summary>
        private bool NoteDeleteSelectionIfAny(TextParagraph paragraph)
        {
            if (!HasNoteSelection()) { _noteAnchor = -1; return false; }
            int a, b;
            OrderedNoteSelection(out a, out b);
            PivotEdit.DeleteInParagraph(paragraph, a, b);
            _noteCaret = a;
            _noteAnchor = -1;
            return true;
        }

        private void NoteTypeText(string text)
        {
            var note = EditingNote;
            if (note == null || string.IsNullOrEmpty(text)) return;
            PushUndo(true);
            var paragraph = NoteParagraph(note);
            NoteDeleteSelectionIfAny(paragraph);
            // Pas de saut de ligne dans une note (le champ d'avant n'en
            // acceptait pas non plus) : la frappe d'un retour est ignorée.
            text = text.Replace("\r", "").Replace("\n", "");
            if (text.Length > 0) PivotEdit.InsertText(paragraph, _noteCaret, text);
            note.SetRuns(paragraph.Runs);
            _noteCaret += text.Length;
            _noteDesiredX = -1;
            AfterNoteEdit();
        }

        private void NoteBackspace()
        {
            var note = EditingNote;
            if (note == null) return;
            PushUndo(false);
            var paragraph = NoteParagraph(note);
            if (!NoteDeleteSelectionIfAny(paragraph))
            {
                if (_noteCaret <= 0) return;
                PivotEdit.DeleteInParagraph(paragraph, _noteCaret - 1, _noteCaret);
                _noteCaret--;
            }
            note.SetRuns(paragraph.Runs);
            _noteDesiredX = -1;
            AfterNoteEdit();
        }

        private void NoteForwardDelete()
        {
            var note = EditingNote;
            if (note == null) return;
            PushUndo(false);
            var paragraph = NoteParagraph(note);
            if (!NoteDeleteSelectionIfAny(paragraph))
            {
                if (_noteCaret >= PivotEdit.FlatLength(paragraph)) return;
                PivotEdit.DeleteInParagraph(paragraph, _noteCaret, _noteCaret + 1);
            }
            note.SetRuns(paragraph.Runs);
            _noteDesiredX = -1;
            AfterNoteEdit();
        }

        private void NoteCopy(bool cut)
        {
            if (!HasNoteSelection()) return;
            int a, b;
            OrderedNoteSelection(out a, out b);
            var text = NoteFlatText();
            b = Math.Min(b, text.Length);
            a = Math.Min(a, b);
            SetClipboardText(text.Substring(a, b - a).Replace("￼", ""));
            if (cut)
            {
                var note = EditingNote;
                if (note == null) return;
                PushUndo(false);
                var paragraph = NoteParagraph(note);
                NoteDeleteSelectionIfAny(paragraph);
                note.SetRuns(paragraph.Runs);
                AfterNoteEdit();
            }
        }

        private async void NotePaste()
        {
            var text = await ClipboardText();
            if (string.IsNullOrEmpty(text) || !NoteEditing) return;
            // Une note tient sur un paragraphe : les retours deviennent des espaces.
            NoteTypeText(text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' '));
        }

        /// <summary>Le clavier quand une note est ouverte. Vrai = géré. Les
        /// gestes du ruban (Ctrl+B, alignements…) sont absorbés sans effet —
        /// « sans options » ; les raccourcis de la fenêtre (Ctrl+S) remontent.</summary>
        private bool NoteKeyDown(KeyEventArgs e)
        {
            var key = e.Key;
            var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
            var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            switch (key)
            {
                case Key.Escape:
                case Key.Return:
                    CloseNoteEditor(true);
                    return true;
                case Key.Left: NoteMoveHorizontal(-1, shift, ctrl); return true;
                case Key.Right: NoteMoveHorizontal(1, shift, ctrl); return true;
                case Key.Up: NoteMoveVertical(-1, shift); return true;
                case Key.Down: NoteMoveVertical(1, shift); return true;
                case Key.Home: NoteHomeEnd(true, shift, ctrl); return true;
                case Key.End: NoteHomeEnd(false, shift, ctrl); return true;
                case Key.Back: if (!ReadOnly) NoteBackspace(); return true;
                case Key.Delete: if (!ReadOnly) NoteForwardDelete(); return true;
                case Key.Tab: return true;
                case Key.A: if (ctrl) { NoteSelectAll(); return true; } break;
                case Key.C: if (ctrl) { NoteCopy(false); return true; } break;
                case Key.X: if (ctrl) { if (!ReadOnly) NoteCopy(true); return true; } break;
                case Key.V: if (ctrl) { if (!ReadOnly) NotePaste(); return true; } break;
                case Key.Z: if (ctrl) { Undo(); return true; } break;
                case Key.Y: if (ctrl) { Redo(); return true; } break;
            }
            // Un geste de l'éditeur (gras, italique, listes…) : absorbé, la
            // note n'a pas d'options.
            var action = Settings.AppSettings.EditorActionFor(key.ToString(), Geo.ToCore(e.KeyModifiers));
            return action != null;
        }

        /// <summary>L'état de format que le ruban montre pour la note ouverte :
        /// le style « Notes de bas de page », rien de plus (sans options).</summary>
        private void NoteFormatState(out bool? bold, out bool? italic, out bool? underline, out bool? strike,
            out string fontFamily, out double? sizePt)
        {
            var style = _styles.FootnoteStyle();
            bold = style.Bold;
            italic = style.Italic;
            underline = false;
            strike = false;
            fontFamily = style.FontFamily;
            sizePt = style.FontSize * 0.75;
        }
    }
}

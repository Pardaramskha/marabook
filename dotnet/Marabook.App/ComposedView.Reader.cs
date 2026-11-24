using System;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Marabook.Model;
using Marabook.Print;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La surface de texte, côté LECTEUR D'ÉCRAN (22/11/2026 — voir ScreenReader.cs) :
    /// elle se présente comme une zone de texte, et fait dire à Marabook où le curseur arrive.
    /// Rien ne se passe tant que Préférences › Accessibilité › « Lecteur d'écran » est éteint.</summary>
    public partial class ComposedView
    {
        private int _readParagraph = -1, _readOffset = -1, _readLineStart = -1;
        private string _readSelection, _readValue;
        private bool _readTyped;
        private BinderItem _readItem;

        protected override AutomationPeer OnCreateAutomationPeer() { return new ComposedTextPeer(this); }

        private void InitReader()
        {
            SelectionStateChanged += delegate { if (ScreenReader.On) ReadMove(); };
            Edited += delegate { _readTyped = true; }; // la frappe : le lecteur d'écran en fait déjà l'écho
            GotFocus += delegate { if (ScreenReader.On) ReadArrival(); };
        }

        internal string ReaderName
        {
            get { return _item == null ? "Texte" : (ReadOnly ? "Texte, en lecture seule, de « " : "Texte de « ") + _item.Title + " »"; }
        }

        private string ParagraphFlat(int index)
        {
            if (_item == null || _item.Document == null || index < 0 || index >= _item.Document.Paragraphs.Count) return "";
            return PivotEdit.FlatText(_item.Document.Paragraphs[index]);
        }

        /// <summary>Le paragraphe où est le curseur.</summary>
        internal string ReaderParagraph() { return ParagraphFlat(_caretParagraph); }

        private void LineBounds(out int start, out int end)
        {
            start = 0;
            end = ParagraphFlat(_caretParagraph).Length;
            try
            {
                int page;
                double y;
                var line = LineOf(_caretParagraph, _caretOffset, out page, out y);
                if (line != null) { start = line.Start; end = line.End; }
            }
            catch (Exception) { }
        }

        /// <summary>L'écrit prend le clavier (ou vient d'être chargé) : son titre, où l'on est, la ligne.</summary>
        private void ReadArrival()
        {
            if (_item == null || _item.Document == null) return;
            int start, end;
            LineBounds(out start, out end);
            var paragraph = ParagraphFlat(_caretParagraph);
            start = Math.Max(0, Math.Min(paragraph.Length, start));
            end = Math.Max(start, Math.Min(paragraph.Length, end));
            var line = paragraph.Substring(start, end - start).Trim();
            ScreenReader.Announce(ReaderName + ", paragraphe " + (_caretParagraph + 1) + " sur " + _item.Document.Paragraphs.Count + ". " + (line.Length == 0 ? "Ligne vide." : line));
            Remember(start);
        }

        private void Remember(int lineStart)
        {
            _readItem = _item;
            _readParagraph = _caretParagraph;
            _readOffset = _caretOffset;
            _readLineStart = lineStart;
        }

        /// <summary>Le curseur ou la sélection ont bougé : dire ce qu'il y a à dire.</summary>
        private void ReadMove()
        {
            if (_item == null || _item.Document == null) return;
            int start, end;
            LineBounds(out start, out end);
            // La valeur de la zone de texte suit le paragraphe du curseur.
            var value = ReaderParagraph();
            if (value != _readValue)
            {
                var peer = ControlAutomationPeer.FromElement(this) as ComposedTextPeer;
                if (peer != null) peer.Changed(_readValue ?? "", value);
                _readValue = value;
            }
            if (!ReferenceEquals(_readItem, _item))
            {
                // Un autre écrit dans la même surface : on l'annonce s'il a le clavier, sinon on prend note.
                if (IsKeyboardFocusWithin) ReadArrival(); else Remember(start);
                _readSelection = null;
                _readTyped = false;
                return;
            }
            var typed = _readTyped;
            _readTyped = false;
            var selection = HasSelection() ? SelectedTextForReader() : null;
            if (selection != _readSelection)
            {
                _readSelection = selection;
                if (!string.IsNullOrEmpty(selection)) ScreenReader.Announce("sélectionné : " + selection);
                else if (!typed) ScreenReader.Announce("sélection retirée");
                Remember(start);
                return;
            }
            if (typed || !(IsKeyboardFocusWithin || ReaderFocusForProbe)) { Remember(start); return; }
            var said = ScreenReader.DescribeMove(value, _readParagraph, _readOffset, _caretParagraph, _caretOffset, start, end, _readParagraph != _caretParagraph || _readLineStart != start);
            Remember(start);
            if (said != null) ScreenReader.Announce(said);
        }

        /// <summary>Le texte sélectionné, d'un paragraphe à l'autre, tel qu'on le dira.</summary>
        private string SelectedTextForReader()
        {
            int pa, oa, pb, ob;
            OrderedSelection(out pa, out oa, out pb, out ob);
            var sb = new System.Text.StringBuilder();
            for (var p = pa; p <= pb && sb.Length < 700; p++)
            {
                var text = ParagraphFlat(p);
                var from = p == pa ? Math.Min(text.Length, oa) : 0;
                var to = p == pb ? Math.Min(text.Length, ob) : text.Length;
                if (to > from) sb.Append(text, from, to - from);
                if (p < pb) sb.Append(' ');
            }
            return sb.ToString();
        }

        // — Sonde.
        /// <summary>La sonde n'a pas toujours le clavier : elle fait comme si.</summary>
        internal bool ReaderFocusForProbe;
        internal void MoveCaretForProbe(int paragraph, int offset)
        {
            _caretParagraph = paragraph;
            _caretOffset = offset;
            ClearSelection();
            ClampCaret();
            UpdateCaretVisual();
        }
        internal void SelectForProbe(int paragraph, int from, int to)
        {
            _anchorParagraph = paragraph;
            _anchorOffset = from;
            _caretParagraph = paragraph;
            _caretOffset = to;
            UpdateCaretVisual();
        }
        internal double CaretWidthForProbe { get { return _caretBar.Width; } }
        internal void BlinkForProbe() { _blink.Stop(); _blink.Start(); }
        internal void ReadArrivalForProbe() { ReadArrival(); }
    }
}

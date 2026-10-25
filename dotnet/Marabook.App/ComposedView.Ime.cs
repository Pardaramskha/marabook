using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;

using Marabook.Print;

namespace Marabook.App
{
    /// <summary>Le CLIENT DE SAISIE de l'éditeur (15/10, 1.0.6). Sans lui, le
    /// backend mac d'Avalonia envoie les caractères bruts de chaque touche :
    /// « ^ » puis « e » donnaient « ^e », jamais « ê » — tréma, accent grave
    /// et tilde pareil. Avec un client déclaré, la frappe passe par le
    /// contexte de saisie du système : la touche morte arrive en texte EN
    /// COMPOSITION (SetPreeditText, montré souligné au caret), la lettre
    /// composée arrive ensuite en TextInput ordinaire. Windows et Linux
    /// composaient déjà les touches mortes avant d'émettre le caractère ; le
    /// client ne leur change rien.</summary>
    public partial class ComposedView
    {
        private ImeClient _ime;
        private TextBlock _preeditBlock;
        private string _preedit;

        private void InitIme()
        {
            _ime = new ImeClient(this);
            _preeditBlock = new TextBlock
            {
                IsVisible = false,
                TextDecorations = TextDecorations.Underline,
                Foreground = Brushes.Black,
                Background = Brushes.Transparent
            };
            _overlay.Children.Add(_preeditBlock);
            TextInputMethodClientRequested += delegate(object sender, TextInputMethodClientRequestedEventArgs e)
            {
                // Une bulle d'annotation (TextBox) focalisée a déjà posé le
                // sien : on ne le remplace pas.
                if (e.Client == null && !ReadOnly) e.Client = _ime;
            };
        }

        /// <summary>Le texte en composition posé par le système (null ou vide
        /// = plus rien) : affiché au caret, jamais dans le pivot — c'est la
        /// lettre composée qui y entrera, par TextInput.</summary>
        private void SetPreedit(string text)
        {
            var next = string.IsNullOrEmpty(text) ? null : text;
            if (next == _preedit) return;
            _preedit = next;
            if (_item != null && _engine != null) UpdateCaretVisual();
        }

        /// <summary>Pose (ou cache) le bloc de composition devant le caret et
        /// rend sa largeur : le caret se décale d'autant.</summary>
        private double PlacePreedit(double x, double y, ComposedLine line)
        {
            if (_preedit == null || NoteEditing || _selectedImage != null)
            {
                _preeditBlock.IsVisible = false;
                return 0;
            }
            var style = _engine.Current.Paragraphs[_caretParagraph].Style;
            _preeditBlock.Text = _preedit;
            _preeditBlock.FontSize = Math.Max(6, style.FontSize);
            try { _preeditBlock.FontFamily = new FontFamily(style.FontFamily ?? "serif"); }
            catch (Exception) { _preeditBlock.FontFamily = FontFamily.Default; }
            _preeditBlock.FontStyle = style.Italic ? FontStyle.Italic : FontStyle.Normal;
            _preeditBlock.Foreground = _caretBar.Fill;
            _preeditBlock.IsVisible = true;
            _preeditBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var height = _preeditBlock.DesiredSize.Height;
            Canvas.SetLeft(_preeditBlock, x);
            // La ligne de base du bloc approche celle de la ligne : le bloc
            // s'aligne sur l'ascendante composée.
            Canvas.SetTop(_preeditBlock, y + Math.Max(0, line.Ascent - height * 0.8));
            return _preeditBlock.DesiredSize.Width;
        }

        /// <summary>Le rectangle du caret relatif à la vue, pour le système de
        /// saisie (fenêtre des candidats des claviers asiatiques).</summary>
        private Rect CaretRectForIme()
        {
            if (!_caretBar.IsVisible && _preedit == null) return new Rect(0, 0, 1, 16);
            var origin = _overlay.TranslatePoint(new Point(Canvas.GetLeft(_caretBar), Canvas.GetTop(_caretBar)), this);
            if (!origin.HasValue) return new Rect(0, 0, 1, 16);
            return new Rect(origin.Value, new Size(Math.Max(1, _caretBar.Width), Math.Max(8, _caretBar.Height)));
        }

        // — Sonde.
        internal TextInputMethodClient ImeClientForProbe { get { return _ime; } }
        internal void SetPreeditForProbe(string text) { SetPreedit(text); }
        internal void BackspaceForProbe() { Backspace(); }
        internal bool PreeditShownForProbe { get { return _preeditBlock.IsVisible && _preedit != null; } }
        internal double PreeditLeftForProbe { get { return Canvas.GetLeft(_preeditBlock); } }
        internal double CaretLeftForProbe { get { return Canvas.GetLeft(_caretBar); } }
        internal double PageLeftForProbe
        {
            get
            {
                int pageIndex; double lineY;
                var line = LineOf(_caretParagraph, _caretOffset, out pageIndex, out lineY);
                return line == null ? 0 : _engine.Current.LeftPxFor(pageIndex);
            }
        }

        private sealed class ImeClient : TextInputMethodClient
        {
            private readonly ComposedView _view;
            public ImeClient(ComposedView view) { _view = view; }
            public override Visual TextViewVisual { get { return _view; } }
            public override bool SupportsPreedit { get { return true; } }
            public override bool SupportsSurroundingText { get { return false; } }
            public override string SurroundingText { get { return ""; } }
            public override Rect CursorRectangle { get { return _view.CaretRectForIme(); } }
            public override TextSelection Selection { get { return new TextSelection(0, 0); } set { } }
            public override void SetPreeditText(string preeditText) { _view.SetPreedit(preeditText); }
        }
    }
}

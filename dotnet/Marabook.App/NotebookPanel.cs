using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE CARNET de la colonne de droite (1.2.0, 14/11/2026, Rémi) :
    /// des notes en Markdown gardées à l'échelle de l'application
    /// (Settings.Notebook), toujours au rail, sous le Lexique. On écrit du
    /// Markdown brut ; dès que la note perd le clavier, elle se montre MISE EN
    /// FORME (le rendu des fiches). Un clic dans la note rend la plume. Pas de
    /// ruban, pas d'option de mise en forme : le Markdown suffit.</summary>
    public class NotebookPanel : DockPanel
    {
        private readonly ComboBox _notes;
        private readonly Button _remove;
        private readonly ToggleButton _write;
        private readonly TextBox _editor;
        private readonly ScrollViewer _reader;
        private readonly TextBlock _saveProblem;
        private readonly DispatcherTimer _saveTimer;
        private List<string> _titles = new List<string>();
        private bool _filling;
        private bool _dirty;

        public event Action<string> LinkClicked;       // un [[lien]] de la note : la coquille y mène
        public Func<System.Threading.Tasks.Task<bool>> ConfirmRemove; // la coquille demande confirmation

        public NotebookPanel()
        {
            Background = Chrome.BarBgLight;
            LastChildFill = true;

            var head = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
            SetDock(head, Dock.Top);
            _remove = Buttons.Icon("trash-bold", "Supprimer cette note", Buttons.Compact, Buttons.Look.Calm);
            _remove.Tag = "notebook-remove";
            _remove.Click += async delegate
            {
                var ask = ConfirmRemove;
                if (Notebook.Current.Text.Trim().Length > 0 && ask != null && !await ask()) return;
                RemoveCurrent();
            };
            DockPanel.SetDock(_remove, Dock.Right);
            head.Children.Add(_remove);
            var add = Buttons.Icon("plus-bold", "Nouvelle note", Buttons.Compact, Buttons.Look.Calm);
            add.Tag = "notebook-add";
            add.Margin = new Thickness(0, 0, 4, 0);
            add.Click += delegate { AddNote(); };
            DockPanel.SetDock(add, Dock.Right);
            head.Children.Add(add);
            _write = Buttons.IconToggle("pencil-simple-line", "Écrire / lire la note mise en forme", Buttons.Compact, Buttons.Look.Calm);
            _write.Tag = "notebook-write";
            _write.Margin = new Thickness(0, 0, 4, 0);
            _write.Click += delegate { if (_write.IsChecked == true) Write(); else Read(); };
            DockPanel.SetDock(_write, Dock.Right);
            head.Children.Add(_write);
            var glyph = Icons.Make("notebook", 15, Chrome.Accent) as Control;
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Center;
                glyph.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(glyph, Dock.Left);
                head.Children.Add(glyph);
            }
            head.Children.Add(new TextBlock
            {
                Text = "Carnet",
                Foreground = Chrome.Ink,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Children.Add(head);

            _notes = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(12, 0, 12, 8), Tag = "notebook-notes" };
            _notes.SelectionChanged += delegate
            {
                if (_filling) return;
                var index = _notes.SelectedIndex;
                if (index < 0 || index >= Notebook.Notes.Count) return;
                Notebook.Current = Notebook.Notes[index];
                _dirty = true; // la note sous les yeux fait partie du carnet
                ShowCurrent(false);
            };
            SetDock(_notes, Dock.Top);
            Children.Add(_notes);

            _saveProblem = new TextBlock
            {
                Text = "Le carnet n'a pas pu être enregistré (carnet.json, à côté des réglages).",
                Foreground = Chrome.Danger, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 12, 8), IsVisible = false
            };
            SetDock(_saveProblem, Dock.Bottom);
            Children.Add(_saveProblem);

            _editor = new TextBox
            {
                AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
                Watermark = "Écrivez en Markdown : # titre, **gras**, *italique*, - liste, - [ ] case, > citation…",
                FontSize = 13, Margin = new Thickness(12, 0, 12, 12), VerticalContentAlignment = VerticalAlignment.Top,
                Tag = "notebook-editor", IsVisible = false
            };
            _editor.TextChanged += delegate
            {
                if (_filling) return;
                // Le TextBox d'Avalonia rend « \r\n » sous Windows : une seule fin de ligne au carnet.
                // (Chaque frappe est reportée ici : changer de note ne « valide » rien,
                // sinon le texte de la note quittée écrasait celle d'arrivée.)
                Notebook.Current.Text = (_editor.Text ?? "").Replace("\r\n", "\n");
                Touch();
            };
            _editor.LostFocus += delegate { if (!_filling && Notebook.Current.Text.Trim().Length > 0) Read(); };
            _editor.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                if (Notebook.Current.Text.Trim().Length > 0) Read();
            };

            _reader = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent, // tout le panneau rend la plume, pas seulement les lettres
                Cursor = new Cursor(StandardCursorType.Ibeam),
                Tag = "notebook-reader"
            };
            _reader.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                if (e.Handled || e.InitialPressMouseButton != MouseButton.Left) return;
                var block = e.Source as TextBlock;
                if (block != null && MarkdownRender.HasLinkAt(block, e.GetPosition(block))) return; // un lien : on le suit
                Write();
            };

            var body = new Grid();
            body.Children.Add(_reader);
            body.Children.Add(_editor);
            Children.Add(body);

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _saveTimer.Tick += delegate { Flush(); };
            ShowCurrent(false);
        }

        /// <summary>Relit le carnet (un autre dossier de réglages, la sonde).</summary>
        public void Reload()
        {
            _saveTimer.Stop();
            _dirty = false;
            ShowCurrent(false);
        }

        private void Touch()
        {
            _dirty = true;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Écrit le carnet s'il a changé (la minuterie, la fermeture de la fenêtre).</summary>
        public void Flush()
        {
            _saveTimer.Stop();
            if (!_dirty) return;
            _dirty = false;
            _saveProblem.IsVisible = !Notebook.Save();
            RefreshTitles();
        }

        private void RefreshTitles()
        {
            _filling = true;
            var notes = Notebook.Notes;
            // Les mêmes titres : rien à refaire (la liste ne se referme pas sous la main).
            var same = _titles.Count == notes.Count;
            for (var i = 0; same && i < notes.Count; i++) same = _titles[i] == Notebook.TitleOf(notes[i].Text);
            if (!same)
            {
                // Une liste NEUVE à chaque fois : vider les Items d'un ComboBox qui a
                // une sélection lève « Index was out of range » dans Avalonia (sonde, 14/11).
                _titles = new List<string>();
                foreach (var note in notes) _titles.Add(Notebook.TitleOf(note.Text));
                _notes.SelectedIndex = -1;
                _notes.ItemsSource = _titles;
            }
            _notes.SelectedIndex = notes.IndexOf(Notebook.Current);
            _remove.IsEnabled = notes.Count > 1 || Notebook.Current.Text.Length > 0;
            _filling = false;
        }

        /// <summary>Montre la note courante : mise en forme si elle a du texte, la plume sinon.</summary>
        private void ShowCurrent(bool focus)
        {
            RefreshTitles();
            if (Notebook.Current.Text.Trim().Length == 0) Write(focus);
            else Read();
        }

        private void Write() { Write(true); }

        private void Write(bool focus)
        {
            _filling = true;
            _editor.Text = Notebook.Current.Text;
            _filling = false;
            _reader.IsVisible = false;
            _editor.IsVisible = true;
            _write.IsChecked = true;
            if (!focus) return;
            _editor.Focus();
            _editor.CaretIndex = (_editor.Text ?? "").Length;
        }

        private void Read()
        {
            _reader.Content = new Border
            {
                Padding = new Thickness(14, 2, 14, 16),
                Background = Brushes.Transparent,
                Child = MarkdownRender.Build(Notebook.Current.Text,
                    delegate(string target) { var handler = LinkClicked; if (handler != null) handler(target); },
                    ToggleTask)
            };
            _editor.IsVisible = false;
            _reader.IsVisible = true;
            _write.IsChecked = false;
            RefreshTitles();
        }

        /// <summary>Une case « - [ ] » cochée dans la note mise en forme.</summary>
        private void ToggleTask(int index)
        {
            var text = Notebook.Current.Text ?? "";
            var position = MarkdownDialect.FindTask(text, index);
            if (position < 0 || position >= text.Length) return;
            Notebook.Current.Text = text.Substring(0, position) + (text[position] == ' ' ? 'x' : ' ') + text.Substring(position + 1);
            Touch();
            Read();
        }

        private void AddNote()
        {
            Notebook.Add();
            Touch();
            ShowCurrent(true);
        }

        private void RemoveCurrent()
        {
            Notebook.Remove(Notebook.Current);
            Touch();
            ShowCurrent(false);
        }

        // ---- sonde
        internal bool ReadingForProbe { get { return _reader.IsVisible; } }
        internal bool WritingForProbe { get { return _editor.IsVisible; } }
        internal int NoteCountForProbe { get { return _titles.Count; } }
        internal string TitleForProbe { get { return _notes.SelectedIndex < 0 || _notes.SelectedIndex >= _titles.Count ? null : _titles[_notes.SelectedIndex]; } }
        internal void TypeForProbe(string text) { Write(false); _editor.Text = text; }
        internal void ReadForProbe() { Read(); }
        internal void WriteForProbe() { Write(false); }
        internal void AddForProbe() { Notebook.Add(); Touch(); ShowCurrent(false); }
        internal void RemoveForProbe() { RemoveCurrent(); }
        internal void SelectForProbe(int index) { _notes.SelectedIndex = index; }
        internal void ToggleTaskForProbe(int index) { ToggleTask(index); }
        internal Control RenderedForProbe { get { var host = _reader.Content as Border; return host == null ? null : host.Child; } }
        internal bool SaveProblemForProbe { get { return _saveProblem.IsVisible; } }
    }
}

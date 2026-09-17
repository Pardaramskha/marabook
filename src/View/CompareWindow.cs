using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Le paper flottant de COMPARAISON (batch 38, lot D) : deux
    /// versions d'un écrit lues en ligne, façon révision d'éditeur —
    /// supprimé BARRÉ (rouge), ajouté SOULIGNÉ (vert), déplacé et style seul
    /// en mention — rendues par la surface composée elle-même sur un
    /// document SYNTHÉTIQUE. Lecture seule STRUCTURELLE (A2) : le document
    /// rendu est neuf (DocumentDiff.Render), porté par un BinderItem qui
    /// n'appartient à aucun projet et qu'aucun chemin de commit ne connaît ;
    /// le drapeau ReadOnly de la surface n'est que l'ergonomie (frappe,
    /// touches et menu d'édition ignorés), et le bandeau le dit. Aucune
    /// chaîne de correction ne tourne ici (elle vit dans EditorView, pas
    /// dans ComposedView). Navigation différence → différence, repli des
    /// suites inchangées, restauration d'un paragraphe (vers l'état actuel).</summary>
    public class CompareWindow : Window
    {
        private readonly ComposedView _composed;
        private readonly TextBlock _title, _summary, _position;
        private readonly ToggleButton _foldToggle;
        private readonly Button _previous, _next, _restoreParagraph;

        private Project _project;
        private BinderItem _item;
        private Snapshot _from, _to;
        private DocumentDelta _delta;
        private readonly List<ParagraphDelta> _changes = new List<ParagraphDelta>();
        private int _index = -1;
        private BinderItem _synthetic;

        /// <summary>Restaurer UN paragraphe de la version comparée dans l'état
        /// actuel (seulement quand la cible est l'état actuel).</summary>
        public event Action<BinderItem, ParagraphDelta> RestoreParagraphRequested;

        public CompareWindow(Window owner)
        {
            Owner = owner;
            Title = "Comparaison — lecture seule";
            Width = 960;
            Height = 720;
            MinWidth = 520;
            MinHeight = 360;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Chrome.WindowBg;

            var root = new DockPanel();
            var banner = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 8, 16, 8)
            };
            DockPanel.SetDock(banner, Dock.Top);
            var bannerStack = new StackPanel();
            var bannerRow = new DockPanel();
            var badge = new Border
            {
                Background = Chrome.Accent,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 2, 10, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "Comparaison — lecture seule", Foreground = Chrome.PaperBg, FontSize = 11, FontWeight = FontWeights.SemiBold }
            };
            DockPanel.SetDock(badge, Dock.Right);
            bannerRow.Children.Add(badge);
            _title = new TextBlock { Foreground = Chrome.Ink, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            bannerRow.Children.Add(_title);
            bannerStack.Children.Add(bannerRow);
            _summary = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            bannerStack.Children.Add(_summary);

            var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            _previous = new Button { Content = Icons.Label("arrow-left-bold", "Différence précédente", 11, Chrome.Ink), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), Focusable = false };
            _previous.Click += delegate { Previous(); };
            tools.Children.Add(_previous);
            _next = new Button { Content = Icons.Label("arrow-right-bold", "Différence suivante", 11, Chrome.Ink), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0), Focusable = false };
            _next.Click += delegate { Next(); };
            tools.Children.Add(_next);
            _position = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            tools.Children.Add(_position);
            _foldToggle = new ToggleButton { Content = new TextBlock { Text = "Replier les passages inchangés", FontSize = 11 }, IsChecked = true, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0), Focusable = false };
            _foldToggle.Checked += delegate { Render(); };
            _foldToggle.Unchecked += delegate { Render(); };
            tools.Children.Add(_foldToggle);
            _restoreParagraph = new Button
            {
                Content = new TextBlock { Text = "Restaurer ce paragraphe", FontSize = 11 },
                Padding = new Thickness(8, 2, 8, 2),
                Focusable = false,
                IsEnabled = false,
                ToolTip = "Remettre ce seul paragraphe tel qu'il était dans la version comparée (annulable)"
            };
            _restoreParagraph.Click += delegate
            {
                var current = CurrentChange;
                var handler = RestoreParagraphRequested;
                if (current != null && handler != null && _to == null) handler(_item, current);
            };
            tools.Children.Add(_restoreParagraph);
            bannerStack.Children.Add(tools);
            banner.Child = bannerStack;
            root.Children.Add(banner);

            _composed = new ComposedView { ReadOnly = true };
            var paper = new Border
            {
                Background = Chrome.WindowBg,
                Margin = new Thickness(0),
                Child = _composed
            };
            root.Children.Add(paper);
            Content = root;
        }

        public ComposedView Surface { get { return _composed; } }
        public DocumentDelta Delta { get { return _delta; } }
        public ParagraphDelta CurrentChange { get { return _index >= 0 && _index < _changes.Count ? _changes[_index] : null; } }
        public bool Shows(BinderItem item) { return _item == item; }
        public bool ComparesToCurrent { get { return _to == null; } }

        /// <summary>Compare « from » à « to » — to null = l'état actuel de l'item.</summary>
        public void Load(Project project, BinderItem item, Snapshot from, Snapshot to)
        {
            _project = project;
            _item = item;
            _from = from;
            _to = to;
            var older = from == null ? item.Document : from.Document;
            var newer = to == null ? item.Document : to.Document;
            _delta = DocumentDiff.Compare(older, newer);
            _changes.Clear();
            foreach (var paragraph in _delta.Paragraphs) if (paragraph.IsChange) _changes.Add(paragraph);
            _index = -1;
            Title = "Comparaison — " + item.Title + " — lecture seule";
            _title.Text = item.Title + " : " + Describe(from) + "  →  " + Describe(to);
            _summary.Text = _delta.Summary();
            // La composition mesure ses glyphes dans une fenêtre VIVANTE : avant
            // Loaded, la surface composerait une page sans texte.
            if (IsLoaded) Render();
            else if (!_renderOnLoad) { _renderOnLoad = true; Loaded += delegate { _renderOnLoad = false; Render(); if (CurrentChange != null) Reveal(CurrentChange); }; }
            if (_changes.Count > 0) Next();
            else UpdatePosition();
        }

        private bool _renderOnLoad;

        /// <summary>Relit les mêmes versions (après une restauration partielle,
        /// l'état actuel a changé).</summary>
        public void Refresh()
        {
            if (_item != null) Load(_project, _item, _from, _to);
        }

        private static string Describe(Snapshot snapshot)
        {
            if (snapshot == null) return "état actuel";
            return "« " + snapshot.DisplayLabel + " » (" + VersionsPanel.FormatDate(snapshot.Date) + ")";
        }

        private void Render()
        {
            if (_delta == null || _project == null) return;
            var document = DocumentDiff.Render(_delta, _foldToggle.IsChecked == true, 2);
            DocumentDiff.CopyFootnotes(document, _from == null ? _item.Document : _from.Document, _to == null ? _item.Document : _to.Document);
            // Le porteur SYNTHÉTIQUE : jamais dans le projet, jamais commité.
            _synthetic = new BinderItem { Kind = ItemKind.Text, Title = "Comparaison — " + _item.Title, Document = document };
            _composed.Detach();
            _composed.SetZoom(Settings.AppSettings.Zoom / 100.0);
            _composed.Attach(_synthetic, _project.Styles, _item.Page ?? _project.Page, _project);
            if (CurrentChange != null) Reveal(CurrentChange);
        }

        public void Next()
        {
            if (_changes.Count == 0) return;
            _index = (_index + 1) % _changes.Count;
            Reveal(_changes[_index]);
        }

        public void Previous()
        {
            if (_changes.Count == 0) return;
            _index = _index <= 0 ? _changes.Count - 1 : _index - 1;
            Reveal(_changes[_index]);
        }

        private void Reveal(ParagraphDelta change)
        {
            if (change.RenderIndex >= 0 && _synthetic != null && change.RenderIndex < _synthetic.Document.Paragraphs.Count)
            {
                var length = PivotEdit.FlatLength(_synthetic.Document.Paragraphs[change.RenderIndex]);
                _composed.SelectRange(change.RenderIndex, 0, length);
            }
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            _position.Text = _changes.Count == 0 ? "Aucune différence"
                : "Différence " + (_index + 1) + " / " + _changes.Count;
            _previous.IsEnabled = _changes.Count > 1;
            _next.IsEnabled = _changes.Count > 1;
            _restoreParagraph.IsEnabled = _to == null && History.RestoreParagraphAction.CanRestore(CurrentChange);
        }
    }
}

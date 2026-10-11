using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Correction;
using Marabook.Model;
using AppSettings = Marabook.Settings.AppSettings;

namespace Marabook.App
{
    /// <summary>Les mots inconnus du projet (1.0.5, Rémi) : le relevé de
    /// tout ce que l'orthographe rougit dans les écrits et les fiches — un
    /// mot par ligne, son compte, le premier endroit, un bout de phrase, une
    /// case — pour apprendre en lot au lieu de mot à mot. Les néologismes et
    /// le familier, qui ont leur catégorie, n'y sont pas.</summary>
    public class UnknownWordsDialog : Window
    {
        public sealed class Choice
        {
            public List<string> Words = new List<string>();
            public bool ProjectScope = true;
        }

        private readonly StackPanel _rows = new StackPanel();
        private readonly List<KeyValuePair<CheckBox, UnknownWord>> _boxes = new List<KeyValuePair<CheckBox, UnknownWord>>();
        private readonly TextBlock _status;
        private readonly Button _learn;
        private readonly ComboBox _scope;
        private readonly TextBox _filter;
        private List<UnknownWord> _words = new List<UnknownWord>();
        private bool _accepted;

        private UnknownWordsDialog(Window owner, Project project, Correction.Hunspell.SpellEngine engine)
        {
            Title = "Mots inconnus du projet";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 720;
            Height = 560;
            MinWidth = 520;
            MinHeight = 360;
            CanResize = true;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new DockPanel { Margin = new Thickness(16) };
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new TextBlock
            {
                Text = "Tout ce que le correcteur ne connaît pas dans les écrits et les fiches — ni le dictionnaire, ni le vôtre. "
                    + "Cochez ce qui est juste (noms de votre monde, mots voulus) : ces mots entrent au dictionnaire comme entrées « autre », à préciser plus tard.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            var tools = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            _filter = new TextBox { Width = 200, Watermark = "Filtrer…", [ToolTip.TipProperty] = "Ne montrer que les mots contenant ces lettres" };
            _filter.TextChanged += delegate { RebuildRows(); };
            DockPanel.SetDock(_filter, Dock.Right);
            tools.Children.Add(_filter);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var all = Buttons.Text("Tout cocher", "Cocher tous les mots affichés", Buttons.Compact, Buttons.Look.Outline);
            all.Click += delegate { SetAll(true); };
            var none = Buttons.Text("Tout décocher", "Décocher tous les mots affichés", Buttons.Compact, Buttons.Look.Outline);
            none.Margin = new Thickness(6, 0, 0, 0);
            none.Click += delegate { SetAll(false); };
            left.Children.Add(all);
            left.Children.Add(none);
            _status = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            left.Children.Add(_status);
            tools.Children.Add(left);
            head.Children.Add(tools);
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _learn = new Button { Content = "Apprendre", IsDefault = true, MinWidth = 120 };
            _learn.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Fermer", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); }; // IsCancel ne ferme pas la fenêtre sur Avalonia (28/09)
            right.Children.Add(_learn);
            right.Children.Add(cancel);
            Dialogs.Arrange(right, _learn);
            DockPanel.SetDock(right, Dock.Right);
            buttons.Children.Add(right);
            var scopeRow = new StackPanel { Orientation = Orientation.Horizontal };
            scopeRow.Children.Add(new TextBlock { Text = "Dans :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            _scope = new ComboBox { MinWidth = 240, VerticalAlignment = VerticalAlignment.Center };
            _scope.Items.Add("le dictionnaire du projet");
            _scope.Items.Add("le dictionnaire de tous les projets");
            _scope.SelectedIndex = 0;
            scopeRow.Children.Add(_scope);
            buttons.Children.Add(scopeRow);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            root.Children.Add(new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = new ScrollViewer { [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto, Content = _rows }
            });
            Content = root;
            _rows.Children.Add(new TextBlock { Text = "Relevé en cours…", Foreground = Chrome.SoftText, Margin = new Thickness(12) });
            _learn.IsEnabled = false;
            Collect(project, engine);
        }

        private async void Collect(Project project, Correction.Hunspell.SpellEngine engine)
        {
            var checker = new SpellChecker(engine)
            {
                ProjectWords = project.Lexicon,
                GlobalWords = AppSettings.Lexicon,
                Neologisms = AppSettings.NeologismsEnabled
            };
            List<UnknownWord> words;
            try { words = await Task.Run(delegate { return UnknownWords.Collect(project, checker); }); }
            catch (Exception error) { words = new List<UnknownWord>(); Console.Error.WriteLine("[mots inconnus] " + error); }
            _words = words;
            RebuildRows();
        }

        private void RebuildRows()
        {
            _rows.Children.Clear();
            _boxes.Clear();
            var needle = FrenchTokenizer.Fold((_filter.Text ?? "").Trim());
            var shown = 0;
            foreach (var word in _words)
            {
                if (needle.Length > 0 && !FrenchTokenizer.Fold(word.Word).Contains(needle)) continue;
                shown++;
                var row = new DockPanel { Margin = new Thickness(8, 3, 8, 3) };
                var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0) };
                box.IsCheckedChanged += delegate { UpdateStatus(); };
                DockPanel.SetDock(box, Dock.Left);
                row.Children.Add(box);
                var text = new StackPanel();
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(new TextBlock { Text = word.Word, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, FontSize = 13 });
                line.Children.Add(new TextBlock
                {
                    Text = (word.Count == 1 ? "1 fois" : word.Count + " fois") + " · " + (word.Items == 1 ? word.Where : word.Items + " éléments, d'abord " + word.Where),
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Bottom
                });
                text.Children.Add(line);
                text.Children.Add(new TextBlock { Text = word.Sample, Foreground = Chrome.FaintText, FontSize = 11, TextWrapping = TextWrapping.Wrap, FontStyle = FontStyle.Italic });
                row.Children.Add(text);
                _rows.Children.Add(row);
                _boxes.Add(new KeyValuePair<CheckBox, UnknownWord>(box, word));
            }
            if (shown == 0)
                _rows.Children.Add(new TextBlock
                {
                    Text = _words.Count == 0 ? "Aucun mot inconnu — tout est propre." : "Aucun mot ne contient ces lettres.",
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(12)
                });
            UpdateStatus();
        }

        private void SetAll(bool value)
        {
            foreach (var pair in _boxes) pair.Key.IsChecked = value;
            UpdateStatus();
        }

        private int CheckedCount()
        {
            var count = 0;
            foreach (var pair in _boxes) if (pair.Key.IsChecked == true) count++;
            return count;
        }

        private void UpdateStatus()
        {
            var selected = CheckedCount();
            _status.Text = _words.Count == 0 ? "" : (_words.Count == 1 ? "1 mot inconnu" : _words.Count + " mots inconnus") + (selected > 0 ? " · " + selected + (selected == 1 ? " coché" : " cochés") : "");
            _learn.Content = selected > 0 ? "Apprendre (" + selected + ")" : "Apprendre";
            _learn.IsEnabled = selected > 0;
        }

        /// <summary>Sonde : le relevé affiché.</summary>
        internal List<UnknownWord> WordsForProbe { get { return _words; } }

        /// <summary>Les mots cochés et la portée, ou null si fermé sans apprendre.</summary>
        public static async Task<Choice> Ask(Window owner, Project project, Correction.Hunspell.SpellEngine engine)
        {
            var dialog = new UnknownWordsDialog(owner, project, engine);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return null;
            var choice = new Choice { ProjectScope = dialog._scope.SelectedIndex != 1 };
            foreach (var pair in dialog._boxes)
                if (pair.Key.IsChecked == true) choice.Words.Add(pair.Value.Word);
            return choice;
        }
    }
}

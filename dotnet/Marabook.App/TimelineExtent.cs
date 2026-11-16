using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>L'ÉTENDUE D'UNE FRISE (16/11/2026, Rémi) : toute la durée de son
    /// système temporel, ou un FRAGMENT — de telle date à telle date — pour une
    /// mini-frise plus détaillée que l'histoire générale. Les deux bornes
    /// s'écrivent dans la notation du système ; ce que Marabook y lit est redit.</summary>
    internal sealed class FragmentEditor : StackPanel
    {
        private readonly RadioButton _whole, _part;
        private readonly TextBox _from, _to;
        private readonly TextBlock _echo;
        private readonly Grid _bounds;
        private TimeSystem _system;

        public FragmentEditor(TimeSystem system, TimelineInfo timeline)
        {
            _system = system;
            var fragment = timeline != null && timeline.HasFragment;
            _whole = new RadioButton { Content = "Toute la durée du système", GroupName = "fragment-" + GetHashCode(), IsChecked = !fragment, Tag = "fragment-whole" };
            _part = new RadioButton { Content = "Un fragment seulement :", GroupName = "fragment-" + GetHashCode(), IsChecked = fragment, Margin = new Thickness(0, 4, 0, 0), Tag = "fragment-part" };
            _from = new TextBox { Text = fragment && system != null ? system.Format(timeline.From) : "", Tag = "fragment-from" };
            _to = new TextBox { Text = fragment && system != null ? system.Format(timeline.To) : "", Tag = "fragment-to" };
            _bounds = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), Margin = new Thickness(26, 6, 0, 0) };
            var de = new TextBlock { Text = "de", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var a = new TextBlock { Text = "à", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            Grid.SetColumn(_from, 1);
            Grid.SetColumn(a, 2);
            Grid.SetColumn(_to, 3);
            _bounds.Children.Add(de);
            _bounds.Children.Add(_from);
            _bounds.Children.Add(a);
            _bounds.Children.Add(_to);
            _echo = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(26, 5, 0, 0) };
            Children.Add(_whole);
            Children.Add(_part);
            Children.Add(_bounds);
            Children.Add(_echo);
            _whole.IsCheckedChanged += delegate { Describe(); };
            _part.IsCheckedChanged += delegate { Describe(); };
            _from.TextChanged += delegate { Describe(); };
            _to.TextChanged += delegate { Describe(); };
            _from.GotFocus += delegate { _part.IsChecked = true; };
            _to.GotFocus += delegate { _part.IsChecked = true; };
            Describe();
        }

        public void SetSystem(TimeSystem system)
        {
            _system = system;
            Describe();
        }

        public bool IsFragment { get { return _part.IsChecked == true; } }

        /// <summary>Lit l'étendue : vrai si elle est valable (tout, ou deux bornes lues).</summary>
        public bool Read(out TimePoint from, out TimePoint to)
        {
            from = to = null;
            if (!IsFragment) return true;
            return Timelines.ReadFragment(_system, _from.Text, _to.Text, out from, out to) == null;
        }

        private void Describe()
        {
            _bounds.Opacity = IsFragment ? 1 : 0.55;
            if (!IsFragment)
            {
                _echo.Foreground = Chrome.SoftText;
                _echo.Text = "La frise s'étend d'un bout à l'autre du système, et jusqu'à son dernier événement.";
                return;
            }
            if (string.IsNullOrWhiteSpace(_from.Text) && string.IsNullOrWhiteSpace(_to.Text))
            {
                _echo.Foreground = Chrome.SoftText;
                _echo.Text = "Deux dates dans la notation du système" + (_system == null ? "" : " (exemple : " + _system.Example() + ")") + " : la frise ne montrera que ce qui tient entre elles.";
                return;
            }
            TimePoint from, to;
            var error = Timelines.ReadFragment(_system, _from.Text, _to.Text, out from, out to);
            if (error != null) { _echo.Foreground = Chrome.Danger; _echo.Text = error; return; }
            var years = Math.Max(1, (long)Math.Ceiling(_system.AxisEnd(to) - _system.AxisStart(from) - 1e-9));
            _echo.Foreground = Chrome.SoftText;
            _echo.Text = "De " + _system.Format(from) + " à " + _system.Format(to) + " : " + TimeSystem.YearsLabel(years) + ".";
        }

        internal void SetForProbe(string from, string to) { _part.IsChecked = true; _from.Text = from; _to.Text = to; }
        internal string EchoForProbe { get { return _echo.Text; } }
    }

    /// <summary>« Étendue… » : changer l'étendue d'une frise existante.</summary>
    internal sealed class FragmentDialog : Window
    {
        private readonly FragmentEditor _editor;
        private bool _accepted;
        private TimePoint _from, _to;

        private FragmentDialog(Window owner, TimeSystem system, TimelineInfo timeline)
        {
            TimelineUi.Dress(this, owner, "Étendue de la frise");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 440 };
            panel.Children.Add(TimelineUi.Label("Ce que la frise couvre, sur « " + system.Name + " » :", 0));
            _editor = new FragmentEditor(system, timeline);
            panel.Children.Add(_editor);
            panel.Children.Add(TimelineUi.Hint("Les événements posés hors d'un fragment ne sont pas supprimés : ils attendent, hors de la vue, et reparaissent si l'étendue s'élargit."));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Appliquer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { if (_editor.Read(out _from, out _to)) { _accepted = true; Close(); } };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
        }

        /// <summary>Rend vrai si l'auteur a validé ; from et to nuls = toute la durée.</summary>
        public static async Task<Tuple<bool, TimePoint, TimePoint>> Ask(Window owner, TimeSystem system, TimelineInfo timeline)
        {
            var dialog = new FragmentDialog(owner, system, timeline);
            await Dialogs.ShowModal(dialog, owner);
            return Tuple.Create(dialog._accepted, dialog._from, dialog._to);
        }
    }

    /// <summary>« Parallélisme » depuis la page Chronologie : les deux frises à superposer
    /// — la seconde parmi celles qui partagent le système de la première.</summary>
    internal sealed class ParallelDialog : Window
    {
        private readonly Project _project;
        private readonly ComboBox _first, _second;
        private readonly List<BinderItem> _firsts = new List<BinderItem>(), _seconds = new List<BinderItem>();
        private Tuple<BinderItem, BinderItem> _result;

        private ParallelDialog(Window owner, Project project)
        {
            _project = project;
            TimelineUi.Dress(this, owner, "Parallélisme");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 420 };
            panel.Children.Add(TimelineUi.Label("La frise du haut :", 0));
            _first = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(_first);
            panel.Children.Add(TimelineUi.Label("La frise du bas (même système temporel) :"));
            _second = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(_second);
            panel.Children.Add(TimelineUi.Hint("Les deux frises partagent le même axe : leurs années s'alignent, le zoom et le défilement vont ensemble, et le repère du survol paraît sur les deux."));
            // Seules les frises qui ont une compagne possible ouvrent la liste.
            foreach (var timeline in Timelines.All(project))
                if (Companions(project, timeline).Count > 0) { _firsts.Add(timeline); _first.Items.Add(timeline.Title); }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Superposer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate
            {
                if (_first.SelectedIndex < 0 || _second.SelectedIndex < 0) return;
                _result = Tuple.Create(_firsts[_first.SelectedIndex], _seconds[_second.SelectedIndex]);
                Close();
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            _first.SelectionChanged += delegate { FillSecond(); };
            if (_firsts.Count > 0) _first.SelectedIndex = 0;
        }

        private void FillSecond()
        {
            _seconds.Clear();
            var names = new List<string>();
            if (_first.SelectedIndex >= 0)
                foreach (var timeline in Companions(_project, _firsts[_first.SelectedIndex])) { _seconds.Add(timeline); names.Add(timeline.Title); }
            _second.ItemsSource = names;
            _second.SelectedIndex = names.Count > 0 ? 0 : -1;
        }

        /// <summary>Les frises qui peuvent se superposer à celle-ci : les autres du même système.</summary>
        public static List<BinderItem> Companions(Project project, BinderItem timeline)
        {
            var list = new List<BinderItem>();
            if (project == null || timeline == null) return list;
            var system = Timelines.SystemOf(project, timeline);
            foreach (var other in Timelines.All(project))
                if (other != timeline && Timelines.SystemOf(project, other).Id == system.Id) list.Add(other);
            return list;
        }

        public static async Task<Tuple<BinderItem, BinderItem>> Ask(Window owner, Project project)
        {
            var dialog = new ParallelDialog(owner, project);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }
    }
}

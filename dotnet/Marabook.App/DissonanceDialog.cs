using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LA FENÊTRE « DISSONANCES » d'une fiche (1.2.0, 12/11/2026, Rémi) :
    /// les périodes d'exclusion (un saut dans le temps : ces années ne comptent
    /// pas dans l'âge) et les points de référence (un âge donné à une date
    /// d'une autre temporalité ; avant, la fiche est « absente »). Chaque
    /// entrée se date dans un système temporel du projet — sans système, rien
    /// ne s'ajoute.</summary>
    public class DissonanceDialog : Window
    {
        internal const string Intro = "Paramétrez des moments d'absence au sein d'une chronologie ou un point de bascule pour calculer l'âge d'un personnage sur plusieurs chronologies, par exemple s'il voyage d'un univers à l'autre.";

        private sealed class Row
        {
            public Dissonance Entry;
            public ComboBox System;
            public DateField From, To;
            public TextBox Age;
            public Border Card;
        }

        private readonly Project _project;
        private readonly BinderItem _sheet;
        private readonly List<Row> _rows = new List<Row>();
        private readonly StackPanel _list;
        private readonly TextBlock _empty, _error;
        private readonly Button _addExclusion, _addReference;
        private bool _accepted;

        private DissonanceDialog(Window owner, Project project, BinderItem sheet)
        {
            _project = project;
            _sheet = sheet;
            TimelineUi.Dress(this, owner, "Dissonances");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(16), Width = 440 };
            panel.Children.Add(new TextBlock { Text = Intro, Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap });

            _empty = TimelineUi.Hint("");
            _empty.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(_empty);
            _list = new StackPanel();
            panel.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 400, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 0) });

            var adders = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            _addExclusion = Buttons.IconText("plus-bold", "Période d'exclusion", null, Buttons.Compact, Buttons.Look.Outline);
            _addExclusion.Tag = "dissonance-add-exclusion";
            _addExclusion.Click += delegate { Add(Dissonance.Exclusion); };
            _addReference = Buttons.IconText("plus-bold", "Point de référence", null, Buttons.Compact, Buttons.Look.Outline);
            _addReference.Tag = "dissonance-add-reference";
            _addReference.Margin = new Thickness(8, 0, 0, 0);
            _addReference.Click += delegate { Add(Dissonance.Reference); };
            adders.Children.Add(_addExclusion);
            adders.Children.Add(_addReference);
            panel.Children.Add(adders);

            _error = new TextBlock { Foreground = Chrome.Danger, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(_error);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;

            var hasSystem = project != null && project.TimeSystems.Count > 0;
            _addExclusion.IsEnabled = hasSystem;
            _addReference.IsEnabled = hasSystem;
            foreach (var entry in sheet.Dissonances) AddRow(entry.Clone());
            RefreshEmpty();
        }

        private void RefreshEmpty()
        {
            var hasSystem = _project != null && _project.TimeSystems.Count > 0;
            _empty.IsVisible = _rows.Count == 0;
            _empty.Text = hasSystem
                ? "Aucune dissonance : l'âge se compte depuis la date de naissance."
                : "Une dissonance se date dans un système temporel : créez d'abord une frise.";
        }

        private void Add(string kind)
        {
            if (_project == null || _project.TimeSystems.Count == 0) return;
            // Le système de l'époque active d'abord : c'est là qu'on regarde les âges.
            TimeSystem system;
            BinderItem timeline;
            if (Epochs.Current(_project, out system, out timeline) == null) system = _project.TimeSystems[0];
            var row = AddRow(new Dissonance { Kind = kind, SystemId = system.Id });
            RefreshEmpty();
            row.From.Box.Focus();
        }

        private Row AddRow(Dissonance entry)
        {
            var row = new Row { Entry = entry };
            var body = new StackPanel();
            var head = new DockPanel();
            var remove = Buttons.Close(null);
            remove.Tag = "dissonance-remove";
            remove.Click += delegate
            {
                _rows.Remove(row);
                _list.Children.Remove(row.Card);
                RefreshEmpty();
            };
            DockPanel.SetDock(remove, Dock.Right);
            head.Children.Add(remove);
            head.Children.Add(new TextBlock
            {
                Text = entry.IsReference ? "Point de référence" : "Période d'exclusion",
                FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center
            });
            body.Children.Add(head);
            body.Children.Add(TimelineUi.Hint(entry.IsReference
                ? "À cette date, le personnage a l'âge indiqué ; avant, dans cette temporalité, il est absent."
                : "Entre le départ et l'arrivée, le personnage est hors-temps : ces années ne comptent pas dans son âge."));

            row.System = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var system in _project.TimeSystems) row.System.Items.Add(system.Name);
            row.System.SelectedIndex = _project.TimeSystems.FindIndex(delegate(TimeSystem s) { return s.Id == entry.SystemId; });
            body.Children.Add(TimelineUi.Label("Système temporel :", 8));
            body.Children.Add(row.System);

            var chosen = SystemOf(row);
            body.Children.Add(TimelineUi.Label(entry.IsReference ? "Date de référence :" : "Départ :", 8));
            row.From = new DateField(chosen, entry.From);
            body.Children.Add(row.From);
            if (entry.IsReference)
            {
                body.Children.Add(TimelineUi.Label("Âge à cette date :", 8));
                row.Age = new TextBox { Text = entry.Age.ToString(CultureInfo.InvariantCulture), Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
                body.Children.Add(row.Age);
            }
            else
            {
                body.Children.Add(TimelineUi.Label("Arrivée :", 8));
                row.To = new DateField(chosen, entry.To);
                body.Children.Add(row.To);
            }
            row.System.SelectionChanged += delegate
            {
                var system = SystemOf(row);
                row.From.System = system;
                if (row.To != null) row.To.System = system;
            };
            row.Card = new Border
            {
                Child = body, Padding = new Thickness(12, 8, 12, 12), Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(6),
                // FieldBg, pas PaperBg : le papier reste blanc en mode sombre (« papier blanc »), l'encre y devenait illisible (14/11).
                BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Background = Chrome.FieldBg, Tag = "dissonance-row"
            };
            _rows.Add(row);
            _list.Children.Add(row.Card);
            return row;
        }

        private TimeSystem SystemOf(Row row)
        {
            var index = row.System.SelectedIndex;
            return index < 0 || index >= _project.TimeSystems.Count ? null : _project.TimeSystems[index];
        }

        private void Fail(string message, Control focus)
        {
            _error.Text = message;
            _error.IsVisible = true;
            if (focus != null) focus.Focus();
        }

        private void Accept()
        {
            var result = new List<Dissonance>();
            foreach (var row in _rows)
            {
                var system = SystemOf(row);
                var entry = row.Entry.Clone();
                var name = entry.IsReference ? "Point de référence" : "Période d'exclusion";
                if (system == null) { Fail(name + " : choisissez un système temporel.", row.System); return; }
                entry.SystemId = system.Id;
                var from = row.From.Point;
                if (from == null) { Fail(name + " : " + (entry.IsReference ? "la date de référence" : "la date de départ") + " ne se lit pas dans « " + system.Name + " ».", row.From.Box); return; }
                entry.From = system.Format(from);
                if (entry.IsReference)
                {
                    long age;
                    if (!long.TryParse((row.Age.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out age) || age < 0)
                    { Fail(name + " : l'âge est un nombre d'années, zéro ou plus.", row.Age); return; }
                    entry.Age = age;
                    entry.To = "";
                }
                else
                {
                    var to = row.To.Point;
                    if (to == null) { Fail(name + " : la date d'arrivée ne se lit pas dans « " + system.Name + " ».", row.To.Box); return; }
                    if (system.AxisStart(to) <= system.AxisStart(from)) { Fail(name + " : l'arrivée doit suivre le départ.", row.To.Box); return; }
                    entry.To = system.Format(to);
                    entry.Age = 0;
                }
                result.Add(entry);
            }
            _sheet.Dissonances.Clear();
            _sheet.Dissonances.AddRange(result);
            _accepted = true;
            Close();
        }

        /// <summary>Vrai si les dissonances de la fiche ont été validées.</summary>
        public static async Task<bool> Ask(Window owner, Project project, BinderItem sheet)
        {
            var dialog = new DissonanceDialog(owner, project, sheet);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._accepted;
        }

        // ---- sonde
        internal static DissonanceDialog ForProbe(Project project, BinderItem sheet) { return new DissonanceDialog(null, project, sheet); }
        internal bool CanAddForProbe { get { return _addExclusion.IsEnabled && _addReference.IsEnabled; } }
        internal int RowsForProbe { get { return _rows.Count; } }
        internal string ErrorForProbe { get { return _error.IsVisible ? _error.Text : null; } }
        internal void AddForProbe(string kind, string from, string to, string age)
        {
            Add(kind);
            var row = _rows[_rows.Count - 1];
            row.From.Text = from;
            if (row.To != null) row.To.Text = to;
            if (row.Age != null) row.Age.Text = age;
        }
        internal bool AcceptForProbe() { Accept(); return _accepted; }
    }
}

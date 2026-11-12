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
    /// <summary>Un champ de DATE écrit dans la notation d'un système (1.2.0) :
    /// la zone de saisie et, dessous, ce que Marabook y lit — ou, en rouge,
    /// un exemple de ce qui est attendu.</summary>
    internal sealed class DateField : StackPanel
    {
        private readonly TextBox _box;
        private readonly TextBlock _echo;
        private TimeSystem _system;

        public event Action Changed;

        public DateField(TimeSystem system, string text)
        {
            _system = system;
            _box = new TextBox { Text = text ?? "" };
            _echo = new TextBlock { FontSize = 11, Margin = new Thickness(2, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            Children.Add(_box);
            Children.Add(_echo);
            _box.TextChanged += delegate { Echo(); var h = Changed; if (h != null) h(); };
            Echo();
        }

        public TextBox Box { get { return _box; } }
        public string Text { get { return (_box.Text ?? "").Trim(); } set { _box.Text = value ?? ""; } }

        public TimeSystem System
        {
            get { return _system; }
            set { _system = value; Echo(); }
        }

        /// <summary>La date lue, null si le texte n'en est pas une.</summary>
        public TimePoint Point
        {
            get
            {
                TimePoint point;
                return _system != null && _system.TryParse(_box.Text, out point) ? point : null;
            }
        }

        private void Echo()
        {
            if (_system == null) { _echo.Text = ""; return; }
            _box.Watermark = "ex. " + _system.Example();
            var point = Point;
            if (point == null)
            {
                var empty = Text.Length == 0;
                _echo.Foreground = empty ? Chrome.SoftText : Chrome.Danger;
                _echo.Text = (empty ? "Notation : " : "Date illisible — notation : ") + _system.Example()
                    + (_system.Months.Count > 1 ? "  ·  " + _system.Format(new TimePoint(SampleYear(), 1, 1)) : "");
                return;
            }
            _echo.Foreground = Chrome.Time;
            var segment = _system.HasSegments ? _system.SegmentName(_system.SegmentIndexOf(point.Year)) + " · " : "";
            var weekDay = _system.WeekDayOf(point);
            _echo.Text = "→ " + segment + _system.Format(point) + (weekDay != null ? " (" + weekDay + ")" : "");
        }

        private long SampleYear()
        {
            TimePoint sample;
            return _system.TryParse(_system.Example(), out sample) ? sample.Year : 0;
        }
    }

    internal static class TimelineUi
    {
        public static TextBlock Label(string text, double top = 12)
        {
            return new TextBlock { Text = text, Foreground = Chrome.Ink, Margin = new Thickness(0, top, 0, 4) };
        }

        public static TextBlock Hint(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        }

        public static void Dress(Window window, Window owner, string title)
        {
            window.Title = title;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowInTaskbar = false;
            window.Background = Chrome.RaisedBg;
        }

        public static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        public static long ParseLong(string text, long fallback)
        {
            long value;
            var cleaned = (text ?? "").Replace(" ", "").Replace(" ", "").Replace(" ", "").Trim();
            return long.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
    }

    // ================================================================ événement

    public sealed class TimelineEventResult
    {
        public TimelineEvent Event;
        public bool Deleted;
    }

    /// <summary>Une brique de la frise : son titre, son type, sa date (ou son
    /// début et sa fin), l'élément du projet auquel elle est assignée — la
    /// SORTE d'abord (fiche, écrit, livre…), l'élément ensuite — et sa note.</summary>
    public class TimelineEventDialog : Window
    {
        private readonly Project _project;
        private readonly TimeSystem _system;
        private readonly TimelineInfo _timeline;
        private readonly TimelineEvent _entry;
        private readonly TextBox _title;
        private readonly ComboBox _type;
        private readonly CheckBox _span;
        private readonly DateField _start, _end;
        private readonly TextBlock _endLabel, _problem;
        private readonly ComboBox _linkKind, _linkItem;
        private readonly TextBox _description;
        private List<BinderItem> _linkItems = new List<BinderItem>();
        private TimelineEventResult _result;

        private TimelineEventDialog(Window owner, Project project, TimeSystem system, TimelineInfo timeline, TimelineEvent entry, bool creating)
        {
            _project = project;
            _system = system;
            _timeline = timeline;
            _entry = entry;
            TimelineUi.Dress(this, owner, creating ? "Nouvel événement" : "Modifier l'événement");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;

            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 440 };
            panel.Children.Add(TimelineUi.Label("Titre :", 0));
            _title = new TextBox { Text = entry.Title ?? "", Watermark = "Ce qui se passe" };
            panel.Children.Add(_title);

            panel.Children.Add(TimelineUi.Label("Type d'événement :"));
            _type = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var type in timeline.Types) _type.Items.Add(type.Name);
            var typeIndex = timeline.Types.FindIndex(delegate(TimelineEventType t) { return t.Id == entry.TypeId; });
            _type.SelectedIndex = typeIndex >= 0 ? typeIndex : (timeline.Types.Count > 0 ? 0 : -1);
            panel.Children.Add(_type);

            _span = new CheckBox { Content = "Sur la durée (un début et une fin)", IsChecked = entry.IsSpan, Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(_span);
            panel.Children.Add(TimelineUi.Label(entry.IsSpan ? "Début :" : "Date :", 8));
            var startLabel = (TextBlock)panel.Children[panel.Children.Count - 1];
            _start = new DateField(system, entry.Start == null ? "" : system.Format(entry.Start));
            panel.Children.Add(_start);
            _endLabel = TimelineUi.Label("Fin :", 8);
            panel.Children.Add(_endLabel);
            _end = new DateField(system, entry.End == null ? "" : system.Format(entry.End));
            panel.Children.Add(_end);
            _span.IsCheckedChanged += delegate
            {
                var on = _span.IsChecked == true;
                startLabel.Text = on ? "Début :" : "Date :";
                _endLabel.IsVisible = on;
                _end.IsVisible = on;
                if (on && _end.Text.Length == 0) _end.Text = _start.Text;
                Validate();
            };
            _endLabel.IsVisible = entry.IsSpan;
            _end.IsVisible = entry.IsSpan;

            panel.Children.Add(TimelineUi.Label("Assigner à :"));
            var linkRow = new Grid { ColumnDefinitions = new ColumnDefinitions("170,8,*") };
            _linkKind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            _linkKind.Items.Add("Rien");
            foreach (var kind in TimelineLinks.Kinds) _linkKind.Items.Add(TimelineLinks.Label(kind));
            _linkItem = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choisir l'élément" };
            Grid.SetColumn(_linkItem, 2);
            linkRow.Children.Add(_linkKind);
            linkRow.Children.Add(_linkItem);
            panel.Children.Add(linkRow);
            panel.Children.Add(TimelineUi.Hint("La sorte d'abord, l'élément ensuite : la brique y mène d'un clic, et l'élément retrouve ses dates."));
            _linkKind.SelectedIndex = entry.LinkKind == null ? 0 : Math.Max(0, Array.IndexOf(TimelineLinks.Kinds, entry.LinkKind) + 1);
            FillLinkItems(entry.LinkId);
            _linkKind.SelectionChanged += delegate { FillLinkItems(null); };

            panel.Children.Add(TimelineUi.Label("Description :"));
            _description = new TextBox { Text = entry.Description ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 84 };
            panel.Children.Add(_description);

            _problem = new TextBlock { Foreground = Chrome.Danger, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), IsVisible = false };
            panel.Children.Add(_problem);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            if (!creating)
            {
                var remove = new Button { Content = "Retirer de la frise", MinWidth = 80 };
                remove.Click += delegate { _result = new TimelineEventResult { Deleted = true }; Close(); };
                buttons.Children.Add(remove);
            }
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(cancel);
            var ok = new Button { Content = creating ? "Poser sur la frise" : "Enregistrer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            _start.Changed += Validate;
            _end.Changed += Validate;
            Loaded += delegate { _title.Focus(); _title.SelectAll(); };
        }

        private void FillLinkItems(string selectedId)
        {
            _linkItem.Items.Clear();
            _linkItems = new List<BinderItem>();
            var index = _linkKind.SelectedIndex;
            _linkItem.IsEnabled = index > 0;
            if (index <= 0) return;
            _linkItems = TimelineLinks.ItemsOf(_project, TimelineLinks.Kinds[index - 1]);
            foreach (var item in _linkItems) _linkItem.Items.Add(item.Title);
            var found = selectedId == null ? -1 : _linkItems.FindIndex(delegate(BinderItem b) { return b.Id == selectedId; });
            _linkItem.SelectedIndex = found;
        }

        private string Problem()
        {
            var start = _start.Point;
            if (start == null) return "La date " + (_span.IsChecked == true ? "de début " : "") + "n'est pas lisible dans la notation de la frise.";
            if (_span.IsChecked == true)
            {
                var end = _end.Point;
                if (end == null) return "La date de fin n'est pas lisible dans la notation de la frise.";
                if (_system.AxisEnd(end) <= _system.AxisStart(start)) return "La fin précède le début.";
            }
            return null;
        }

        private void Validate()
        {
            if (!_problem.IsVisible) return;
            var problem = Problem();
            _problem.Text = problem ?? "";
            _problem.IsVisible = problem != null;
        }

        private void Accept()
        {
            var problem = Problem();
            if (problem != null)
            {
                _problem.Text = problem;
                _problem.IsVisible = true;
                return;
            }
            _entry.Title = (_title.Text ?? "").Trim();
            if (_entry.Title.Length == 0) _entry.Title = "Événement";
            _entry.TypeId = _type.SelectedIndex >= 0 && _type.SelectedIndex < _timeline.Types.Count ? _timeline.Types[_type.SelectedIndex].Id : null;
            _entry.Start = _start.Point;
            _entry.End = _span.IsChecked == true ? _end.Point : null;
            var kind = _linkKind.SelectedIndex;
            var item = _linkItem.SelectedIndex;
            if (kind > 0 && item >= 0 && item < _linkItems.Count)
            {
                _entry.LinkKind = TimelineLinks.Kinds[kind - 1];
                _entry.LinkId = _linkItems[item].Id;
            }
            else { _entry.LinkKind = null; _entry.LinkId = null; }
            _entry.Description = (_description.Text ?? "").Replace("\r\n", "\n").Trim();
            _result = new TimelineEventResult { Event = _entry };
            Close();
        }

        /// <summary>entry : la brique à remplir (une copie pour une modification).
        /// Null si annulé ; Deleted si l'auteur la retire.</summary>
        public static async Task<TimelineEventResult> Ask(Window owner, Project project, TimeSystem system, TimelineInfo timeline, TimelineEvent entry, bool creating)
        {
            var dialog = new TimelineEventDialog(owner, project, system, timeline, entry, creating);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }

        // Sonde.
        internal void FillForProbe(string title, string start, string end)
        {
            _title.Text = title;
            _start.Text = start;
            _span.IsChecked = end != null;
            if (end != null) _end.Text = end;
        }
        internal void SetLinkForProbe(string kind, string itemId)
        {
            _linkKind.SelectedIndex = Array.IndexOf(TimelineLinks.Kinds, kind) + 1;
            _linkItem.SelectedIndex = _linkItems.FindIndex(delegate(BinderItem b) { return b.Id == itemId; });
        }
        internal int LinkKindCountForProbe { get { return _linkKind.Items.Count; } }
        internal int LinkItemCountForProbe { get { return _linkItem.Items.Count; } }
        internal bool LinkItemEnabledForProbe { get { return _linkItem.IsEnabled; } }
        internal void AcceptForProbe() { Accept(); }
        internal bool ProblemShownForProbe { get { return _problem.IsVisible; } }
    }

    // ==================================================================== types

    /// <summary>Les types d'événement d'une frise : nom et couleur, ajout,
    /// retrait (les briques d'un type retiré restent, sans type).</summary>
    public class TimelineTypesDialog : Window
    {
        private readonly Project _project;
        private readonly List<TimelineEventType> _types = new List<TimelineEventType>();
        private readonly StackPanel _rows;
        private bool _accepted;

        private TimelineTypesDialog(Window owner, Project project, TimelineInfo timeline)
        {
            _project = project;
            foreach (var type in timeline.Types) _types.Add(new TimelineEventType { Id = type.Id, Name = type.Name, Color = type.Color });
            TimelineUi.Dress(this, owner, "Types d'événement");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 360 };
            panel.Children.Add(new TextBlock { Text = "Les types colorent les briques et servent de filtres.", Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            _rows = new StackPanel { Spacing = 6 };
            panel.Children.Add(new ScrollViewer { MaxHeight = 320, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _rows });
            var add = Buttons.IconText("plus-bold", "Ajouter un type", "Un nouveau type d'événement", Buttons.Compact, Buttons.Look.Outline);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 10, 0, 0);
            add.Click += delegate
            {
                var palette = new[] { "#5B67D8", "#C0504D", "#3E9C7A", "#B5843A", "#8E6BBF", "#0E7C7B", "#C08497", "#6E8FB5" };
                _types.Add(new TimelineEventType { Name = "Nouveau type", Color = palette[_types.Count % palette.Length] });
                Rebuild();
            };
            panel.Children.Add(add);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Enregistrer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            Rebuild();
        }

        private void Rebuild()
        {
            _rows.Children.Clear();
            foreach (var type in _types)
            {
                var captured = type;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*,8,Auto") };
                var swatch = new Button
                {
                    Width = 26, Height = 26, Padding = new Thickness(0),
                    Content = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(TimelineView.ColorOf(type)) },
                    [ToolTip.TipProperty] = "Changer la couleur du type"
                };
                swatch.Click += delegate
                {
                    var menu = new ContextMenu();
                    ColorMenus.Fill(menu, this, _project, captured.Color, delegate(string hex) { if (hex != null) { captured.Color = hex; Rebuild(); } }, false);
                    Ui.ShowMenu(menu, swatch);
                };
                var name = new TextBox { Text = type.Name };
                name.TextChanged += delegate { captured.Name = (name.Text ?? "").Trim(); };
                Grid.SetColumn(name, 2);
                var remove = Buttons.Icon("x-bold", "Retirer ce type (ses événements restent, sans type)", Buttons.Compact, Buttons.Look.Calm);
                remove.Click += delegate { _types.Remove(captured); Rebuild(); };
                Grid.SetColumn(remove, 4);
                row.Children.Add(swatch);
                row.Children.Add(name);
                row.Children.Add(remove);
                _rows.Children.Add(row);
            }
        }

        public static async Task<bool> Edit(Window owner, Project project, TimelineInfo timeline)
        {
            var dialog = new TimelineTypesDialog(owner, project, timeline);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return false;
            timeline.Types.Clear();
            foreach (var type in dialog._types)
            {
                if (string.IsNullOrEmpty(type.Name)) type.Name = "Type";
                timeline.Types.Add(type);
            }
            foreach (var entry in timeline.Events)
                if (entry.TypeId != null && timeline.FindType(entry.TypeId) == null) entry.TypeId = null;
            return true;
        }
    }

    // ============================================================= nouvelle frise

    public sealed class NewTimelineChoice
    {
        public string Title;
        public TimeSystem System;   // un système du projet, ou un système neuf (à ajouter au projet)
        public bool SystemIsNew;
    }

    /// <summary>Une nouvelle frise : son nom et — D'ABORD — son système de
    /// notation du temps : un système du projet, celui de notre monde, ou un
    /// système personnalisé à créer sur-le-champ (sa fenêtre s'ouvre avant
    /// que la frise n'existe).</summary>
    public class NewTimelineDialog : Window
    {
        private readonly Project _project;
        private readonly TextBox _title;
        private readonly ComboBox _system;
        private readonly TextBlock _summary;
        private readonly List<TimeSystem> _choices = new List<TimeSystem>();
        private int _customIndex;
        private NewTimelineChoice _result;

        private NewTimelineDialog(Window owner, Project project, string initialTitle)
        {
            _project = project;
            TimelineUi.Dress(this, owner, "Nouvelle frise");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 420 };
            panel.Children.Add(TimelineUi.Label("1. Le système de notation du temps :", 0));
            _system = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(_system);
            _summary = TimelineUi.Hint("");
            panel.Children.Add(_summary);
            panel.Children.Add(TimelineUi.Label("2. Le nom de la frise :", 14));
            _title = new TextBox { Text = initialTitle ?? "Nouvelle frise" };
            panel.Children.Add(_title);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Créer la frise", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            FillSystems(null);
            _system.SelectionChanged += delegate { Describe(); };
            Loaded += delegate { _title.Focus(); _title.SelectAll(); };
        }

        private void FillSystems(TimeSystem select)
        {
            _system.Items.Clear();
            _choices.Clear();
            foreach (var system in _project.TimeSystems) { _choices.Add(system); _system.Items.Add(system.Name); }
            var hasWorld = _project.TimeSystems.Exists(delegate(TimeSystem s) { return s.Name == TimeSystem.GregorianName; });
            if (!hasWorld) { _choices.Add(null); _system.Items.Add(TimeSystem.GregorianName + " (système de base)"); }
            if (select != null && !_choices.Contains(select)) { _choices.Add(select); _system.Items.Add(select.Name + " (nouveau)"); }
            _customIndex = _system.Items.Count;
            _system.Items.Add("Nouveau système personnalisé…");
            _system.SelectedIndex = select != null ? _choices.IndexOf(select) : 0;
            Describe();
        }

        private TimeSystem _world;

        private TimeSystem Chosen()
        {
            var index = _system.SelectedIndex;
            if (index < 0 || index >= _choices.Count) return null;
            if (_choices[index] != null) return _choices[index];
            if (_world == null) _world = TimeSystem.Gregorian();
            return _world;
        }

        private async void Describe()
        {
            if (_system.SelectedIndex == _customIndex && _customIndex > 0 || (_system.SelectedIndex == _customIndex && _choices.Count == 0))
            {
                // Le système d'abord : sa fenêtre, puis retour ici avec lui.
                var draft = TimeSystemDialog.Template(_project);
                var created = await TimeSystemDialog.Edit(this, _project, draft, true);
                FillSystems(created);
                return;
            }
            var system = Chosen();
            _summary.Text = system == null ? "" : TimeSystemDialog.Summary(system);
        }

        private void Accept()
        {
            var system = Chosen();
            if (system == null) return;
            var title = (_title.Text ?? "").Trim();
            if (title.Length == 0) title = "Nouvelle frise";
            _result = new NewTimelineChoice { Title = title, System = system, SystemIsNew = !_project.TimeSystems.Contains(system) };
            Close();
        }

        public static async Task<NewTimelineChoice> Ask(Window owner, Project project, string initialTitle = null)
        {
            var dialog = new NewTimelineDialog(owner, project, initialTitle);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }

        internal int SystemChoiceCountForProbe { get { return _system.Items.Count; } }
        internal void AcceptForProbe(string title) { _title.Text = title; Accept(); }
    }

    // ================================================================ le système

    /// <summary>« TEMPORALITÉ PAR SEGMENTS » : le système temporel d'une frise.
    /// Premier onglet, les segments (ères, âges…) — leur nom, si les années se
    /// cumulent d'un segment à l'autre ou si chaque segment repart de zéro —
    /// et la notation (« E{s}.{a} » : E1.880 = l'an 880 de la première ère).
    /// Puis le calendrier (mois, bissextiles) et, au besoin, la semaine et
    /// l'horloge. Les briques gardent leur place sur l'axe quand le système
    /// change : seule leur écriture suit.</summary>
    public class TimeSystemDialog : Window
    {
        private readonly Project _project;
        private readonly TimeSystem _system;
        private readonly TextBox _name, _word, _yearPattern, _negativePattern, _datePattern, _try, _weekDays, _hours, _minutes;
        private readonly CheckBox _segmented, _yearZero;
        private readonly RadioButton _reset, _cumulative;
        private readonly StackPanel _segments, _months, _leaps, _segmentBlock;
        private readonly TextBlock _example, _tryEcho, _problem, _yearLength;
        private bool _accepted;
        private bool _building;

        /// <summary>Le brouillon d'un système personnalisé : trois ères, douze mois de trente jours.</summary>
        public static TimeSystem Template(Project project)
        {
            var system = new TimeSystem
            {
                Name = "Nouveau système", Segmented = true, SegmentWord = "Ère", Cumulative = false,
                YearPattern = "E{s}.{a}", DatePattern = "{j} {M} {année}"
            };
            var number = 2;
            while (project != null && project.TimeSystems.Exists(delegate(TimeSystem s) { return s.Name == system.Name; }))
                system.Name = "Nouveau système " + number++;
            system.Segments.Add(new TimeSegment { Name = "Première ère", Length = 1000 });
            system.Segments.Add(new TimeSegment { Name = "Deuxième ère", Length = 1000 });
            system.Segments.Add(new TimeSegment { Name = "Troisième ère", Length = 0 });
            for (var i = 1; i <= 12; i++) system.Months.Add(new TimeMonth { Name = "Mois " + i, Days = 30 });
            system.WeekDays.AddRange(new[] { "premier jour", "deuxième jour", "troisième jour", "quatrième jour", "cinquième jour", "sixième jour", "septième jour" });
            return system;
        }

        /// <summary>Un système en une phrase (la liste des frises, le dialogue de création).</summary>
        public static string Summary(TimeSystem system)
        {
            var parts = new List<string>();
            parts.Add("Notation : " + system.Example());
            if (system.HasSegments)
                parts.Add(system.Segments.Count + " " + system.SegmentLabel.ToLowerInvariant() + (system.Segments.Count > 1 ? "s" : "")
                    + (system.Cumulative ? " (années cumulées)" : " (chacune repart de zéro)"));
            parts.Add(system.Months.Count + " mois, " + system.BaseDays + " jours" + (system.Leaps.Count > 0 ? ", bissextiles" : ""));
            if (system.HoursPerDay != 24 || system.MinutesPerHour != 60) parts.Add("jours de " + system.HoursPerDay + " h");
            return string.Join(" · ", parts.ToArray());
        }

        private TimeSystemDialog(Window owner, Project project, TimeSystem system, bool creating)
        {
            _project = project;
            _system = system;
            _building = true;
            TimelineUi.Dress(this, owner, "Temporalité par segments" + (creating ? " — nouveau système" : " — " + system.Name));
            Width = 640;
            SizeToContent = SizeToContent.Height;
            CanResize = false;

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            root.Children.Add(TimelineUi.Label("Nom du système :", 0));
            _name = new TextBox { Text = system.Name };
            root.Children.Add(_name);

            var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0), Height = 430 };

            // ---------------------------------------------- segments et notation
            var first = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            _segmented = new CheckBox { Content = "Découper le temps en segments (ères, âges, cycles…)", IsChecked = system.Segmented };
            first.Children.Add(_segmented);
            _segmentBlock = new StackPanel();
            var wordRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            wordRow.Children.Add(new TextBlock { Text = "Un segment s'appelle :", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _word = new TextBox { Text = system.SegmentWord, Width = 140, Watermark = "ère, âge, cycle…" };
            wordRow.Children.Add(_word);
            _segmentBlock.Children.Add(wordRow);
            _reset = new RadioButton { Content = "Chaque segment repart de zéro (E2.1 suit E1.1000)", GroupName = "cumul", IsChecked = !system.Cumulative, Margin = new Thickness(0, 8, 0, 0) };
            _cumulative = new RadioButton { Content = "Années cumulatives entre segments (E2.1001 suit E1.1000)", GroupName = "cumul", IsChecked = system.Cumulative };
            _segmentBlock.Children.Add(_reset);
            _segmentBlock.Children.Add(_cumulative);
            var segmentHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,70,8,110,8,26"), Margin = new Thickness(0, 10, 0, 2) };
            segmentHead.Children.Add(new TextBlock { Text = "Nom", Foreground = Chrome.SoftText, FontSize = 11 });
            var codeHead = new TextBlock { Text = "Abréviation", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(codeHead, 2);
            var lengthHead = new TextBlock { Text = "Durée (années)", Foreground = Chrome.SoftText, FontSize = 11 };
            Grid.SetColumn(lengthHead, 4);
            segmentHead.Children.Add(codeHead);
            segmentHead.Children.Add(lengthHead);
            _segmentBlock.Children.Add(segmentHead);
            _segments = new StackPanel { Spacing = 4 };
            _segmentBlock.Children.Add(new ScrollViewer { MaxHeight = 118, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _segments });
            var addSegment = Buttons.IconText("plus-bold", "Ajouter un segment", "Un segment de plus, à la suite", Buttons.Compact, Buttons.Look.Outline);
            addSegment.HorizontalAlignment = HorizontalAlignment.Left;
            addSegment.Margin = new Thickness(0, 6, 0, 0);
            addSegment.Click += delegate
            {
                Collect();
                // Le dernier segment ouvert se ferme pour laisser venir le suivant.
                if (_system.Segments.Count > 0 && _system.Segments[_system.Segments.Count - 1].Length <= 0) _system.Segments[_system.Segments.Count - 1].Length = 1000;
                _system.Segments.Add(new TimeSegment { Name = "", Length = 0 });
                RebuildSegments();
                Preview();
            };
            _segmentBlock.Children.Add(addSegment);
            _segmentBlock.Children.Add(TimelineUi.Hint("Durée vide pour le dernier segment : il reste ouvert et court jusqu'au dernier événement."));
            first.Children.Add(_segmentBlock);

            first.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 10, 0, 0) });
            var notation = new Grid { ColumnDefinitions = new ColumnDefinitions("190,8,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(0, 8, 0, 0) };
            _yearPattern = new TextBox { Text = system.YearPattern };
            _negativePattern = new TextBox { Text = system.NegativePattern, Watermark = "vide : E1.-320" };
            _datePattern = new TextBox { Text = system.DatePattern };
            AddRow(notation, 0, "Notation d'une année :", _yearPattern);
            AddRow(notation, 1, "Années négatives :", _negativePattern);
            AddRow(notation, 2, "Notation d'une date :", _datePattern);
            first.Children.Add(notation);
            first.Children.Add(TimelineUi.Hint("{a} l'année · {s} le numéro du segment · {c} son abréviation · {n} son nom · {j} le jour · {m} le numéro du mois · {M} son nom · {année} la notation de l'année."));
            _yearZero = new CheckBox { Content = "La première année est l'an 0 (sinon l'an 1)", IsChecked = system.YearZero, Margin = new Thickness(0, 6, 0, 0) };
            first.Children.Add(_yearZero);
            _example = new TextBlock { Foreground = Chrome.Time, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Tag = "system-example" };
            first.Children.Add(_example);
            var tryRow = new Grid { ColumnDefinitions = new ColumnDefinitions("190,8,*"), Margin = new Thickness(0, 6, 0, 0) };
            tryRow.Children.Add(new TextBlock { Text = "Essayer une date :", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center });
            _try = new TextBox { Watermark = "écrivez une date pour la vérifier" };
            Grid.SetColumn(_try, 2);
            tryRow.Children.Add(_try);
            first.Children.Add(tryRow);
            _tryEcho = new TextBlock { FontSize = 11.5, Margin = new Thickness(198, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            first.Children.Add(_tryEcho);
            tabs.Items.Add(new TabItem { Header = "Segments et notation", Content = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = first } });

            // ----------------------------------------------------------- calendrier
            var second = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            second.Children.Add(new TextBlock { Text = "Les mois de l'année, dans l'ordre :", Foreground = Chrome.Ink });
            _months = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            second.Children.Add(new ScrollViewer { MaxHeight = 190, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _months });
            var monthButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Spacing = 10 };
            var addMonth = Buttons.IconText("plus-bold", "Ajouter un mois", "Un mois de plus, à la fin de l'année", Buttons.Compact, Buttons.Look.Outline);
            addMonth.Click += delegate { Collect(); _system.Months.Add(new TimeMonth { Name = "Mois " + (_system.Months.Count + 1), Days = 30 }); RebuildMonths(); RebuildLeaps(); Preview(); };
            monthButtons.Children.Add(addMonth);
            _yearLength = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            monthButtons.Children.Add(_yearLength);
            second.Children.Add(monthButtons);
            second.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 10, 0, 8) });
            second.Children.Add(new TextBlock { Text = "Années bissextiles :", Foreground = Chrome.Ink });
            _leaps = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            second.Children.Add(_leaps);
            var addLeap = Buttons.IconText("plus-bold", "Ajouter une règle", "Une règle de bissextile de plus", Buttons.Compact, Buttons.Look.Outline);
            addLeap.HorizontalAlignment = HorizontalAlignment.Left;
            addLeap.Margin = new Thickness(0, 6, 0, 0);
            addLeap.Click += delegate { Collect(); _system.Leaps.Add(new LeapRule { Every = 4, Month = 0, Days = 1 }); RebuildLeaps(); Preview(); };
            second.Children.Add(addLeap);
            second.Children.Add(TimelineUi.Hint("Tous les N ans, le mois choisi gagne des jours — sauf les années multiples de « sauf », à moins qu'elles le soient d'« à moins que » (notre monde : 4, sauf 100, à moins que 400)."));
            tabs.Items.Add(new TabItem { Header = "Calendrier", Content = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = second } });

            // ------------------------------------------------------ semaine, horloge
            var third = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            third.Children.Add(new TextBlock { Text = "Les jours de la semaine, séparés par des virgules :", Foreground = Chrome.Ink });
            _weekDays = new TextBox { Text = string.Join(", ", system.WeekDays.ToArray()), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
            third.Children.Add(_weekDays);
            third.Children.Add(TimelineUi.Hint("Autant de jours que vous voulez : une semaine de cinq, de dix jours… Vide : pas de semaine."));
            var clock = new Grid { ColumnDefinitions = new ColumnDefinitions("190,8,90"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(0, 14, 0, 0) };
            _hours = new TextBox { Text = system.HoursPerDay.ToString(CultureInfo.InvariantCulture) };
            _minutes = new TextBox { Text = system.MinutesPerHour.ToString(CultureInfo.InvariantCulture) };
            AddRow(clock, 0, "Heures par jour :", _hours);
            AddRow(clock, 1, "Minutes par heure :", _minutes);
            third.Children.Add(clock);
            third.Children.Add(TimelineUi.Hint("Une date précise au jour peut porter une heure : « 12 Mois 3 E2.880 14h30 »."));
            tabs.Items.Add(new TabItem { Header = "Semaine et horloge", Content = third });

            root.Children.Add(tabs);
            _problem = new TextBlock { Foreground = Chrome.Danger, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), IsVisible = false };
            root.Children.Add(_problem);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = creating ? "Créer le système" : "Enregistrer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            root.Children.Add(buttons);
            Content = root;

            RebuildSegments();
            RebuildMonths();
            RebuildLeaps();
            _building = false;
            EventHandler<TextChangedEventArgs> changed = delegate { Preview(); };
            foreach (var box in new[] { _word, _yearPattern, _negativePattern, _datePattern, _try, _hours, _minutes, _weekDays }) box.TextChanged += changed;
            _segmented.IsCheckedChanged += delegate { Preview(); };
            _yearZero.IsCheckedChanged += delegate { Preview(); };
            _reset.IsCheckedChanged += delegate { Preview(); };
            _cumulative.IsCheckedChanged += delegate { Preview(); };
            Preview();
            Loaded += delegate { Dialogs.FitToScreen(this, owner); _name.Focus(); _name.SelectAll(); };
        }

        private static void AddRow(Grid grid, int row, string label, Control field)
        {
            var text = new TextBlock { Text = label, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 0, 3) };
            Grid.SetRow(text, row);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 2);
            field.Margin = new Thickness(0, 3, 0, 3);
            grid.Children.Add(text);
            grid.Children.Add(field);
        }

        private void RebuildSegments()
        {
            var was = _building;
            _building = true;
            _segments.Children.Clear();
            for (var i = 0; i < _system.Segments.Count; i++)
            {
                var segment = _system.Segments[i];
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,70,8,110,8,26"), Tag = segment };
                var name = new TextBox { Text = segment.Name, Watermark = _system.SegmentName(i) };
                var code = new TextBox { Text = segment.Code, Watermark = _system.SegmentCode(i) };
                var length = new TextBox { Text = segment.Length > 0 ? segment.Length.ToString(CultureInfo.InvariantCulture) : "", Watermark = "ouvert" };
                Grid.SetColumn(code, 2);
                Grid.SetColumn(length, 4);
                var remove = Buttons.Icon("x-bold", "Retirer ce segment", Buttons.Compact, Buttons.Look.Calm);
                Grid.SetColumn(remove, 6);
                var captured = segment;
                remove.Click += delegate { Collect(); _system.Segments.Remove(captured); RebuildSegments(); Preview(); };
                EventHandler<TextChangedEventArgs> changed = delegate { Preview(); };
                name.TextChanged += changed;
                code.TextChanged += changed;
                length.TextChanged += changed;
                row.Children.Add(name);
                row.Children.Add(code);
                row.Children.Add(length);
                row.Children.Add(remove);
                _segments.Children.Add(row);
            }
            _building = was;
        }

        private void RebuildMonths()
        {
            var was = _building;
            _building = true;
            _months.Children.Clear();
            for (var i = 0; i < _system.Months.Count; i++)
            {
                var month = _system.Months[i];
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,8,70,4,Auto,8,26") };
                row.Children.Add(new TextBlock { Text = (i + 1) + ".", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center });
                var name = new TextBox { Text = month.Name };
                Grid.SetColumn(name, 1);
                var days = new TextBox { Text = month.Days.ToString(CultureInfo.InvariantCulture) };
                Grid.SetColumn(days, 3);
                var unit = new TextBlock { Text = "jours", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(unit, 5);
                var remove = Buttons.Icon("x-bold", "Retirer ce mois", Buttons.Compact, Buttons.Look.Calm);
                Grid.SetColumn(remove, 7);
                var captured = month;
                remove.IsEnabled = _system.Months.Count > 1;
                remove.Click += delegate { Collect(); _system.Months.Remove(captured); RebuildMonths(); RebuildLeaps(); Preview(); };
                EventHandler<TextChangedEventArgs> changed = delegate { Preview(); };
                name.TextChanged += changed;
                days.TextChanged += changed;
                row.Children.Add(name);
                row.Children.Add(days);
                row.Children.Add(unit);
                row.Children.Add(remove);
                _months.Children.Add(row);
            }
            _building = was;
        }

        private void RebuildLeaps()
        {
            var was = _building;
            _building = true;
            _leaps.Children.Clear();
            foreach (var leap in _system.Leaps)
            {
                var row = new WrapPanel { Orientation = Orientation.Horizontal };
                Func<string, TextBlock> word = delegate(string text) { return new TextBlock { Text = text, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) }; };
                var every = new TextBox { Text = leap.Every.ToString(CultureInfo.InvariantCulture), Width = 52 };
                var except = new TextBox { Text = leap.Except > 0 ? leap.Except.ToString(CultureInfo.InvariantCulture) : "", Width = 52, Watermark = "—" };
                var unless = new TextBox { Text = leap.Unless > 0 ? leap.Unless.ToString(CultureInfo.InvariantCulture) : "", Width = 52, Watermark = "—" };
                var days = new TextBox { Text = leap.Days.ToString(CultureInfo.InvariantCulture), Width = 40 };
                var month = new ComboBox { MinWidth = 120 };
                foreach (var m in _system.Months) month.Items.Add(m.Name);
                month.SelectedIndex = Math.Max(0, Math.Min(_system.Months.Count - 1, leap.Month));
                var remove = Buttons.Icon("x-bold", "Retirer cette règle", Buttons.Compact, Buttons.Look.Calm);
                remove.Margin = new Thickness(6, 0, 0, 0);
                var captured = leap;
                remove.Click += delegate { Collect(); _system.Leaps.Remove(captured); RebuildLeaps(); Preview(); };
                row.Children.Add(word("Tous les"));
                row.Children.Add(every);
                row.Children.Add(word("ans, sauf"));
                row.Children.Add(except);
                row.Children.Add(word("à moins que"));
                row.Children.Add(unless);
                row.Children.Add(word(": +"));
                row.Children.Add(days);
                row.Children.Add(word("j. en"));
                row.Children.Add(month);
                row.Children.Add(remove);
                row.Tag = new object[] { leap, every, except, unless, days, month };
                EventHandler<TextChangedEventArgs> changed = delegate { Preview(); };
                every.TextChanged += changed;
                except.TextChanged += changed;
                unless.TextChanged += changed;
                days.TextChanged += changed;
                month.SelectionChanged += delegate { Preview(); };
                _leaps.Children.Add(row);
            }
            if (_system.Leaps.Count == 0)
                _leaps.Children.Add(new TextBlock { Text = "Aucune : toutes les années ont la même longueur.", Foreground = Chrome.SoftText, FontSize = 12 });
            _building = was;
        }

        /// <summary>Recopie les champs dans le système (en cours d'édition).</summary>
        private void Collect()
        {
            _system.Name = (_name.Text ?? "").Trim().Length == 0 ? "Système temporel" : _name.Text.Trim();
            _system.Segmented = _segmented.IsChecked == true;
            _system.SegmentWord = (_word.Text ?? "").Trim();
            _system.Cumulative = _cumulative.IsChecked == true;
            _system.YearPattern = (_yearPattern.Text ?? "").Trim();
            _system.NegativePattern = (_negativePattern.Text ?? "").Trim();
            _system.DatePattern = (_datePattern.Text ?? "").Trim();
            _system.YearZero = _yearZero.IsChecked == true;
            for (var i = 0; i < _segments.Children.Count && i < _system.Segments.Count; i++)
            {
                var row = _segments.Children[i] as Grid;
                if (row == null) continue;
                var segment = _system.Segments[i];
                segment.Name = (((TextBox)row.Children[0]).Text ?? "").Trim();
                segment.Code = (((TextBox)row.Children[1]).Text ?? "").Trim();
                segment.Length = Math.Max(0, TimelineUi.ParseLong(((TextBox)row.Children[2]).Text, 0));
            }
            for (var i = 0; i < _months.Children.Count && i < _system.Months.Count; i++)
            {
                var row = _months.Children[i] as Grid;
                if (row == null) continue;
                _system.Months[i].Name = (((TextBox)row.Children[1]).Text ?? "").Trim();
                _system.Months[i].Days = Math.Max(1, Math.Min(10000, TimelineUi.ParseInt(((TextBox)row.Children[2]).Text, 30)));
            }
            foreach (var child in _leaps.Children)
            {
                var row = child as WrapPanel;
                var parts = row == null ? null : row.Tag as object[];
                if (parts == null) continue;
                var leap = (LeapRule)parts[0];
                leap.Every = Math.Max(1, TimelineUi.ParseInt(((TextBox)parts[1]).Text, 4));
                leap.Except = Math.Max(0, TimelineUi.ParseInt(((TextBox)parts[2]).Text, 0));
                leap.Unless = Math.Max(0, TimelineUi.ParseInt(((TextBox)parts[3]).Text, 0));
                leap.Days = TimelineUi.ParseInt(((TextBox)parts[4]).Text, 1);
                leap.Month = Math.Max(0, ((ComboBox)parts[5]).SelectedIndex);
            }
            _system.WeekDays.Clear();
            foreach (var day in (_weekDays.Text ?? "").Split(','))
                if (day.Trim().Length > 0) _system.WeekDays.Add(day.Trim());
            _system.HoursPerDay = Math.Max(1, Math.Min(1000, TimelineUi.ParseInt(_hours.Text, 24)));
            _system.MinutesPerHour = Math.Max(1, Math.Min(1000, TimelineUi.ParseInt(_minutes.Text, 60)));
        }

        private string Problem()
        {
            if (_system.Segmented && _system.Segments.Count == 0) return "Ajoutez au moins un segment, ou décochez le découpage en segments.";
            if (_system.Segmented)
                for (var i = 0; i < _system.Segments.Count - 1; i++)
                    if (_system.Segments[i].Length <= 0) return "Seul le dernier segment peut rester ouvert : donnez une durée à « " + _system.SegmentName(i) + " ».";
            foreach (var month in _system.Months) if (string.IsNullOrEmpty(month.Name)) return "Chaque mois doit porter un nom.";
            return _system.NotationProblem();
        }

        private void Preview()
        {
            if (_building) return;
            Collect();
            _segmentBlock.IsVisible = _system.Segmented;
            var problem = Problem();
            _problem.Text = problem ?? "";
            _problem.IsVisible = problem != null;
            var leapYears = "";
            if (_system.Leaps.Count > 0)
            {
                long sample = 0;
                for (long year = 0; year < 5000; year++) if (_system.IsLeapYear(year)) { sample = year; break; }
                leapYears = " · bissextile : " + _system.DaysInYear(sample) + " jours (" + _system.FormatYear(sample) + ")";
            }
            _yearLength.Text = "Une année : " + _system.BaseDays + " jours" + leapYears;
            if (problem != null && _system.NotationProblem() != null) { _example.Text = ""; _tryEcho.Text = ""; return; }
            TimePoint sampleYear;
            var year0 = _system.TryParse(_system.Example(), out sampleYear) ? sampleYear.Year : 0;
            var parts = new List<string>();
            parts.Add(_system.Example());
            if (_system.HasSegments && _system.Segments.Count > 1)
            {
                var boundary = _system.SegmentStart(1);
                parts.Add(_system.FormatYear(boundary - 1) + " puis " + _system.FormatYear(boundary));
            }
            parts.Add(_system.FormatYear(-320));
            if (_system.Months.Count > 0) parts.Add(_system.Format(new TimePoint(year0, Math.Min(3, _system.Months.Count), 12)));
            _example.Text = "Exemples : " + string.Join("   ·   ", parts.ToArray());
            var tried = (_try.Text ?? "").Trim();
            if (tried.Length == 0) { _tryEcho.Text = ""; return; }
            TimePoint point;
            if (_system.TryParse(tried, out point))
            {
                _tryEcho.Foreground = Chrome.Time;
                var weekDay = _system.WeekDayOf(point);
                _tryEcho.Text = "→ " + (_system.HasSegments ? _system.SegmentName(_system.SegmentIndexOf(point.Year)) + " · " : "") + _system.Format(point) + (weekDay != null ? " (" + weekDay + ")" : "");
            }
            else
            {
                _tryEcho.Foreground = Chrome.Danger;
                _tryEcho.Text = "Cette date ne se lit pas dans la notation.";
            }
        }

        private void Accept()
        {
            Collect();
            var problem = Problem();
            if (problem != null) { _problem.Text = problem; _problem.IsVisible = true; return; }
            _accepted = true;
            Close();
        }

        /// <summary>Édite une COPIE du système ; la rend si l'auteur valide, null sinon.</summary>
        public static async Task<TimeSystem> Edit(Window owner, Project project, TimeSystem system, bool creating)
        {
            var draft = creating ? system : system.Clone();
            var dialog = new TimeSystemDialog(owner, project, draft, creating);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._accepted ? draft : null;
        }

        // Sonde.
        internal string ExampleForProbe { get { return _example.Text ?? ""; } }
        internal void SetCumulativeForProbe(bool on) { _cumulative.IsChecked = on; _reset.IsChecked = !on; }
        internal void SetYearPatternForProbe(string pattern) { _yearPattern.Text = pattern; }
        internal void TryForProbe(string text) { _try.Text = text; }
        internal string TryEchoForProbe { get { return _tryEcho.Text ?? ""; } }
        internal bool ProblemShownForProbe { get { return _problem.IsVisible; } }
        internal void AcceptForProbe() { Accept(); }
        internal void CancelForProbe() { Close(); }
    }

    // ============================================================ le lien de date

    public sealed class TimeLinkChoice
    {
        public string Timeline;
        public string Notation;
        public string Text;
    }

    /// <summary>LE LIEN VERS LA TEMPORALITÉ (Ctrl+!) : la frise, la date dans sa
    /// notation, le texte affiché. Une notation sélectionnée avant le raccourci
    /// (« E5.902 ») arrive ici déjà lue — comme le nom d'une fiche pour Ctrl+K.</summary>
    public class TimeLinkDialog : Window
    {
        private readonly Project _project;
        private readonly List<BinderItem> _timelines;
        private readonly ComboBox _timeline;
        private readonly DateField _date;
        private readonly TextBox _text;
        private TimeLinkChoice _result;

        private TimeLinkDialog(Window owner, Project project, BinderItem timeline, string notation, string text, bool editing)
        {
            _project = project;
            _timelines = Timelines.All(project);
            TimelineUi.Dress(this, owner, editing ? "Modifier le lien de date" : "Lien vers la temporalité");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(16), Width = 380 };
            panel.Children.Add(TimelineUi.Label("Frise :", 0));
            _timeline = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var item in _timelines) _timeline.Items.Add(item.Title + "  —  " + Timelines.SystemOf(project, item).Name);
            _timeline.SelectedIndex = Math.Max(0, timeline == null ? 0 : _timelines.IndexOf(timeline));
            panel.Children.Add(_timeline);
            panel.Children.Add(TimelineUi.Label("Date (dans la notation de la frise) :"));
            _date = new DateField(SelectedSystem(), notation);
            panel.Children.Add(_date);
            panel.Children.Add(TimelineUi.Label("Texte affiché dans le texte :"));
            _text = new TextBox { Text = text ?? "", Watermark = "Vide : la date" };
            panel.Children.Add(_text);
            panel.Children.Add(TimelineUi.Hint("Un lien de date a sa propre couleur. Cliqué, il ouvre la frise à cette date ; dans « Date de naissance » ou « Date de décès » d'une fiche, il sert aux âges de l'Époque."));
            _timeline.SelectionChanged += delegate { _date.System = SelectedSystem(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = editing ? "Modifier" : "Insérer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            Loaded += delegate
            {
                if (_date.Point == null) { _date.Box.Focus(); _date.Box.SelectAll(); }
                else { _text.Focus(); _text.SelectAll(); }
            };
        }

        private TimeSystem SelectedSystem()
        {
            var index = _timeline.SelectedIndex;
            return index < 0 || index >= _timelines.Count ? null : Timelines.SystemOf(_project, _timelines[index]);
        }

        private void Accept()
        {
            var index = _timeline.SelectedIndex;
            var point = _date.Point;
            if (index < 0 || index >= _timelines.Count || point == null) { _date.Box.Focus(); return; }
            // La date est réécrite dans la notation canonique du système.
            _result = new TimeLinkChoice { Timeline = _timelines[index].Title, Notation = SelectedSystem().Format(point), Text = _text.Text ?? "" };
            Close();
        }

        public static async Task<TimeLinkChoice> Ask(Window owner, Project project, BinderItem timeline, string notation, string text, bool editing)
        {
            var dialog = new TimeLinkDialog(owner, project, timeline, notation, text, editing);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }

        // Sonde.
        internal string DateForProbe { get { return _date.Text; } }
        internal string TextForProbe { get { return _text.Text ?? ""; } }
        internal int TimelineIndexForProbe { get { return _timeline.SelectedIndex; } }
        internal bool DateReadForProbe { get { return _date.Point != null; } }
        internal void SetDateForProbe(string notation) { _date.Text = notation; }
        internal void AcceptForProbe() { Accept(); }
        internal void CancelForProbe() { Close(); }
    }

    // ================================================================== l'époque

    /// <summary>L'ÉPOQUE de la bibliothèque des fiches : une frise et une date.
    /// Active, elle dit pour chaque personnage s'il est né, mort, et son âge.</summary>
    public class EpochDialog : Window
    {
        private readonly Project _project;
        private readonly List<BinderItem> _timelines;
        private readonly ComboBox _timeline;
        private readonly DateField _date;
        private bool _accepted;

        private EpochDialog(Window owner, Project project)
        {
            _project = project;
            _timelines = Timelines.All(project);
            TimelineUi.Dress(this, owner, "Époque");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(16), Width = 380 };
            panel.Children.Add(new TextBlock
            {
                Text = "Placez la bibliothèque à une date : chaque fiche dont la date de naissance ou de décès est un lien de date dit qui est né, mort, et son âge.",
                Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(TimelineUi.Label("Frise :"));
            _timeline = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var item in _timelines) _timeline.Items.Add(item.Title + "  —  " + Timelines.SystemOf(project, item).Name);
            var current = project.Epoch == null ? -1 : _timelines.FindIndex(delegate(BinderItem b) { return b.Id == project.Epoch.TimelineId; });
            _timeline.SelectedIndex = current >= 0 ? current : 0;
            panel.Children.Add(_timeline);
            panel.Children.Add(TimelineUi.Label("Date :"));
            _date = new DateField(SelectedSystem(), project.Epoch == null ? "" : project.Epoch.Notation);
            panel.Children.Add(_date);
            _timeline.SelectionChanged += delegate { _date.System = SelectedSystem(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Activer l'époque", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            Loaded += delegate { _date.Box.Focus(); _date.Box.SelectAll(); };
        }

        private TimeSystem SelectedSystem()
        {
            var index = _timeline.SelectedIndex;
            return index < 0 || index >= _timelines.Count ? null : Timelines.SystemOf(_project, _timelines[index]);
        }

        private void Accept()
        {
            var index = _timeline.SelectedIndex;
            var point = _date.Point;
            if (index < 0 || index >= _timelines.Count || point == null) { _date.Box.Focus(); return; }
            if (_project.Epoch == null) _project.Epoch = new EpochState();
            _project.Epoch.TimelineId = _timelines[index].Id;
            _project.Epoch.Notation = SelectedSystem().Format(point);
            _project.Epoch.Enabled = true;
            _accepted = true;
            Close();
        }

        /// <summary>Vrai si l'époque a été réglée (et activée).</summary>
        public static async Task<bool> Ask(Window owner, Project project)
        {
            var dialog = new EpochDialog(owner, project);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._accepted;
        }

        internal void SetForProbe(string notation) { _date.Text = notation; }
        internal void AcceptForProbe() { Accept(); }
    }
}

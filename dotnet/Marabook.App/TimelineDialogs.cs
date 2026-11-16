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

        /// <summary>LE BANDEAU « NOTRE MONDE » (15/11, Rémi) : en tête d'une
        /// fenêtre de création préremplie, une alerte verte, informative — le
        /// dessin d'une snackbar (coins ronds, liseré), mais posée là, immobile.</summary>
        public static Border OurWorldNotice(string text)
        {
            var badge = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Chrome.Ok, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0),
                Child = new TextBlock { Text = "i", Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            var row = new DockPanel();
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);
            row.Children.Add(new TextBlock { Text = text, Foreground = Chrome.Ink, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            return new Border
            {
                Child = row, Background = new SolidColorBrush(Chrome.Ok.Color, 0.13), BorderBrush = Chrome.Ok, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 14, 10), Margin = new Thickness(0, 0, 0, 12), Tag = "our-world-notice"
            };
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
        public bool OpenSheet; // « Aller à la fiche » : rien n'est changé, la fiche s'ouvre
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

        // Une brique NÉE D'UNE FICHE (naissance, décès — 16/11, Rémi) : seule sa date
        // se change ici, et c'est le champ de la fiche qui la reçoit.
        private readonly BinderItem _lifeSheet;

        private TimelineEventDialog(Window owner, Project project, TimeSystem system, TimelineInfo timeline, TimelineEvent entry, bool creating, BinderItem lifeSheet = null, string lifeField = null)
        {
            _project = project;
            _system = system;
            _timeline = timeline;
            _entry = entry;
            _lifeSheet = lifeSheet;
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
            // Une brique EN ATTENTE (lot 2) : ce qui était écrit ne se lit plus — on le montre et on redemande.
            if (entry.IsPending)
                panel.Children.Insert(0, new Border
                {
                    Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Chrome.Danger.Color, 0.10), BorderBrush = new SolidColorBrush(Chrome.Danger.Color, 0.55), BorderThickness = new Thickness(1),
                    Tag = "event-pending-notice",
                    Child = new TextBlock
                    {
                        Text = "Cet événement attend une nouvelle date : « " + entry.PendingStart + (entry.PendingEnd != null ? " → " + entry.PendingEnd : "") + " » ne se lit plus dans la temporalité.",
                        Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, FontSize = 12
                    }
                });
            _start = new DateField(system, entry.IsPending ? entry.PendingStart : entry.Start == null ? "" : system.Format(entry.Start));
            panel.Children.Add(_start);
            _endLabel = TimelineUi.Label("Fin :", 8);
            panel.Children.Add(_endLabel);
            _end = new DateField(system, entry.IsPending ? (entry.PendingEnd ?? "") : entry.End == null ? "" : system.Format(entry.End));
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
            if (lifeSheet != null)
            {
                // Tout ce qui n'est pas la date appartient à la fiche : montré, pas modifiable.
                var typeLabel = (Control)panel.Children[panel.Children.IndexOf(_type) - 1];
                var linkLabel = (Control)panel.Children[panel.Children.IndexOf(linkRow) - 1];
                var linkHint = (Control)panel.Children[panel.Children.IndexOf(linkRow) + 1];
                var descriptionLabel = (Control)panel.Children[panel.Children.IndexOf(_description) - 1];
                foreach (var hidden in new Control[] { typeLabel, _type, _span, linkLabel, linkRow, linkHint, descriptionLabel, _description }) hidden.IsVisible = false;
                _title.IsReadOnly = true;
                _title.Focusable = false;
                _title.Opacity = 0.75;
                panel.Children.Insert(0, new Border
                {
                    Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 12), CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Chrome.Ok.Color, 0.13), BorderBrush = Chrome.Ok, BorderThickness = new Thickness(1), Tag = "event-life-notice",
                    Child = new TextBlock
                    {
                        Text = "Cet événement est né de la fiche « " + lifeSheet.Title + " » : sa date vit dans le champ « " + lifeField + " ». La changer ici la change aussi sur la fiche.",
                        Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, FontSize = 12
                    }
                });
                var toSheet = new Button { Content = "Aller à la fiche", MinWidth = 80, Tag = "event-go-to-sheet" };
                toSheet.Click += delegate { _result = new TimelineEventResult { OpenSheet = true }; Close(); };
                buttons.Children.Add(toSheet);
            }
            else if (!creating)
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
            Loaded += delegate
            {
                if (entry.IsPending || lifeSheet != null) { _start.Box.Focus(); _start.Box.SelectAll(); }
                else { _title.Focus(); _title.SelectAll(); }
            };
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
            if (_lifeSheet != null)
            {
                // Née d'une fiche : la date, rien d'autre — l'appelant la réécrit dans le champ.
                _entry.Start = _start.Point;
                _result = new TimelineEventResult { Event = _entry };
                Close();
                return;
            }
            _entry.Title = (_title.Text ?? "").Trim();
            if (_entry.Title.Length == 0) _entry.Title = "Événement";
            _entry.TypeId = _type.SelectedIndex >= 0 && _type.SelectedIndex < _timeline.Types.Count ? _timeline.Types[_type.SelectedIndex].Id : null;
            _entry.Start = _start.Point;
            _entry.PendingStart = null; // une date lisible : la brique retrouve l'axe
            _entry.PendingEnd = null;
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
        public static async Task<TimelineEventResult> Ask(Window owner, Project project, TimeSystem system, TimelineInfo timeline, TimelineEvent entry, bool creating, BinderItem lifeSheet = null, string lifeField = null)
        {
            var dialog = new TimelineEventDialog(owner, project, system, timeline, entry, creating, lifeSheet, lifeField);
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
        internal bool LifeModeForProbe { get { return _lifeSheet != null && !_type.IsVisible && !_description.IsVisible && _title.IsReadOnly; } }
        internal void GoToSheetForProbe() { _result = new TimelineEventResult { OpenSheet = true }; Close(); }
        internal void SetStartForProbe(string start) { _start.Text = start; }
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
        public TimePoint From, To;  // un fragment (16/11) ; nuls = toute la durée du système
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
            // 16/11 (Rémi) : toute la durée du système, ou un fragment pour une mini-frise détaillée.
            panel.Children.Add(TimelineUi.Label("3. Son étendue :", 14));
            _extent = new FragmentEditor(null, null);
            panel.Children.Add(_extent);
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
            Loaded += delegate
            {
                _title.Focus(); _title.SelectAll();
            };
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
            if (_extent != null) _extent.SetSystem(system);
        }

        private FragmentEditor _extent;
        internal FragmentEditor ExtentForProbe { get { return _extent; } }

        private void Accept()
        {
            var system = Chosen();
            if (system == null) return;
            var title = (_title.Text ?? "").Trim();
            if (title.Length == 0) title = "Nouvelle frise";
            TimePoint from, to;
            if (!_extent.Read(out from, out to)) return; // un fragment illisible : la raison est dite en rouge sous les deux dates
            _result = new NewTimelineChoice { Title = title, System = system, SystemIsNew = !_project.TimeSystems.Contains(system), From = from, To = to };
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
        private readonly CheckBox _segmented, _yearZero, _segmentYearZero;
        private readonly RadioButton _reset, _cumulative;
        private readonly StackPanel _segments, _months, _leaps, _segmentBlock;
        private readonly TextBlock _example, _dateExample, _tryEcho, _problem, _yearLength;
        // Les jours de la semaine en LISTE (14/11, Rémi) : un nom puis « , » l'ajoute ;
        // chaque ligne a ses flèches (monter, descendre) et sa croix.
        private readonly List<string> _days = new List<string>();
        private readonly StackPanel _dayRows;
        private readonly TextBox _weeksPerMonth;
        private readonly TextBlock _distributeEcho, _clockHint, _clockEcho;
        private readonly Button _distribute;
        // Le Répartiteur (16/11) : sa méthode, le nombre de jours de l'année, son accordéon.
        private readonly ComboBox _spreadMode;
        private readonly TextBox _spreadDays, _seconds;
        private readonly Control _spreadDaysLabel, _weeksLabel;
        private readonly Accordion _spreadAccordion;
        private readonly ClockDial _dial;
        // 16/11 soir : le quotient du Répartiteur ; le dessin du cadran (tours, pas des chiffres, style) et ses astuces.
        private readonly TextBox _spreadGap;
        private readonly Control _spreadGapLabel;
        private readonly ComboBox _dialTurns, _dialHourStep, _dialMinuteStep, _dialNumerals;
        private readonly StackPanel _dialTips;
        private List<int> _turnChoices = new List<int>(), _hourStepChoices = new List<int>(), _minuteStepChoices = new List<int>();
        private string _dialKey;
        private bool _accepted;
        private bool _building;
        private readonly Border _notice; // le bandeau « notre monde », à la création seulement
        private readonly Border _foundation; // le bandeau « fondation », quand des frises ou un monde s'appuient sur ce système
        internal bool FoundationNoticeForProbe { get { return _foundation != null; } }
        internal bool NoticeShownForProbe { get { return _notice != null && _notice.IsVisible; } }
        internal bool SpreadOpenForProbe { get { return _spreadAccordion.IsOpen; } set { _spreadAccordion.IsOpen = value; } }
        internal void SpreadForProbe(int mode, string days) { _spreadMode.SelectedIndex = mode; _spreadDays.Text = days; Distribute(); }
        internal void SpreadGapForProbe(string gap, string days) { _spreadMode.SelectedIndex = SpreadAlternate; _spreadGap.Text = gap; _spreadDays.Text = days; Distribute(); }
        internal void SpreadRatiosForProbe(string days) { _spreadMode.SelectedIndex = SpreadRatios; _spreadDays.Text = days; Distribute(); }
        internal bool SpreadGapShownForProbe { get { return _spreadGap.IsVisible; } }
        internal string DialForProbe { get { return _dial.DesignForProbe; } }
        internal int DialTurnChoicesForProbe { get { return _dialTurns.Items.Count; } }
        internal int DialHourChoicesForProbe { get { return _dialHourStep.Items.Count; } }
        internal void SetDialForProbe(int turns, int hourStep, int minuteStep, int numerals) { _dialTurns.SelectedIndex = turns; _dialHourStep.SelectedIndex = hourStep; _dialMinuteStep.SelectedIndex = minuteStep; _dialNumerals.SelectedIndex = numerals; }
        internal void SetHoursForProbe(string hours) { _hours.Text = hours; }
        internal string DialTipsForProbe { get { var tips = new List<string>(); foreach (var row in _dialTips.Children) foreach (var child in ((DockPanel)row).Children) { var text = child as TextBlock; if (text != null) tips.Add(text.Text); } return string.Join(" | ", tips.ToArray()); } }
        internal string ClockForProbe { get { return _dial.MetricsForProbe + " " + (_clockEcho.Text ?? ""); } }
        internal void SetSecondsForProbe(string seconds) { _seconds.Text = seconds; }
        internal string LeapEchoForProbe(int index) { return ((TextBlock)((object[])((Control)_leaps.Children[index]).Tag)[6]).Text ?? ""; }
        internal bool LeapRemoveAlignedForProbe(int index)
        {
            // La croix d'une règle reste sur la première ligne de sa carte, à droite.
            var card = (Border)_leaps.Children[index];
            foreach (var child in ((Grid)card.Child).Children)
            {
                var button = child as Button;
                if (button != null) return button.Bounds.Y < 4 && button.Bounds.Right > ((Grid)card.Child).Bounds.Width - 4;
            }
            return false;
        }
        internal int LeapCountForProbe { get { return _system.Leaps.Count; } }

        /// <summary>Le brouillon d'un système personnalisé : pas de découpage,
        /// donc l'année seule : « {a} » (14/11, Rémi) — trois âges attendent,
        /// prêts, que la case soit cochée ; la notation passe alors à
        /// « A{s}.{a} » (SegmentedPattern). Une date s'écrit jour/mois/année.
        /// 15/11 (Rémi) : mois, bissextiles, semaine et horloge sont ceux de
        /// NOTRE monde (TimeSystem.Gregorian) — un point de départ que chacun
        /// sait lire, et la symétrie avec notre calendrier en un clic.</summary>
        public static TimeSystem Template(Project project)
        {
            var system = new TimeSystem
            {
                Name = "Nouveau système", Segmented = false, SegmentWord = "Âge", Cumulative = false,
                YearPattern = "{a}", NegativePattern = "-{a}", DatePattern = "{j}/{m}/{année}"
            };
            var number = 2;
            while (project != null && project.TimeSystems.Exists(delegate(TimeSystem s) { return s.Name == system.Name; }))
                system.Name = "Nouveau système " + number++;
            system.Segments.Add(new TimeSegment { Name = "Premier âge", Length = 1000 });
            system.Segments.Add(new TimeSegment { Name = "Deuxième âge", Length = 1000 });
            system.Segments.Add(new TimeSegment { Name = "Troisième âge", Length = 0 });
            var ours = TimeSystem.Gregorian().Clone();
            system.Months.AddRange(ours.Months);
            system.Leaps.AddRange(ours.Leaps);
            system.WeekDays.AddRange(ours.WeekDays);
            system.HoursPerDay = ours.HoursPerDay;
            system.MinutesPerHour = ours.MinutesPerHour;
            system.SecondsPerMinute = ours.SecondsPerMinute;
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
            if (creating)
            {
                _notice = TimelineUi.OurWorldNotice("Les données préremplies sont celles de notre monde : ses mois, ses années bissextiles, sa semaine et son horloge. Gardez ce qui vous convient, changez le reste.");
                root.Children.Add(_notice);
            }
            else
            {
                // LA FONDATION (16/11, Rémi) : ce système porte des frises, peut-être un monde —
                // le dire avant qu'un changement de calendrier ne se répercute en cascade.
                var uses = Timelines.UsesOf(project, system);
                var events = 0;
                foreach (var timeline in Timelines.All(project))
                    if (timeline.Timeline != null && timeline.Timeline.SystemId == system.Id) events += timeline.Timeline.Events.Count;
                var home = Astro.WorldOf(project, system);
                if (uses > 0 || home != null)
                {
                    var carried = new List<string>();
                    if (uses > 0) carried.Add(TimeSystem.Count(uses, "frise", "frises") + " (" + TimeSystem.Count(events, "événement", "événements") + ")");
                    if (home != null) carried.Add("le ciel du monde « " + home.Name + " »");
                    _foundation = AstrolabeUi.Notice("Ce système est une fondation : il porte " + string.Join(" et ", carried.ToArray())
                        + ". Changer ses segments, sa notation ou son calendrier relit toutes leurs dates : ce qui ne se lit plus attend dans « Problèmes », au rail, et "
                        + Ui.Keys("Ctrl+Z") + " annule tout le changement.", false, "system-foundation-notice");
                    root.Children.Add(_foundation);
                }
            }
            root.Children.Add(TimelineUi.Label("Nom du système :", 0));
            _name = new TextBox { Text = system.Name };
            root.Children.Add(_name);

            var tabs = new TabControl { Margin = new Thickness(0, 12, 0, 0), Height = 600 }; // 430 avant le 14/11 : la date d'exemple, la case des segments et le testeur encadré tiennent sans défiler

            // ---------------------------------------------- segments et notation
            var first = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            _yearZero = new CheckBox { Content = "La première année est l'an 0 (sinon l'an 1)", IsChecked = system.YearZero };
            var years = new StackPanel();
            years.Children.Add(_yearZero);
            _segmented = new CheckBox { Content = "Découper le temps en segments (âges, ères, cycles…)", IsChecked = system.Segmented, Margin = new Thickness(0, 4, 0, 0) };
            years.Children.Add(_segmented);
            _segmentBlock = new StackPanel();
            var wordRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            wordRow.Children.Add(new TextBlock { Text = "Un segment s'appelle :", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _word = new TextBox { Text = system.SegmentWord, Width = 140, Watermark = "âge, ère, cycle…" };
            wordRow.Children.Add(_word);
            _segmentBlock.Children.Add(wordRow);
            _reset = new RadioButton { Content = ResetLabel, GroupName = "cumul", IsChecked = !system.Cumulative, Margin = new Thickness(0, 8, 0, 0) };
            _cumulative = new RadioButton { Content = CumulativeLabel, GroupName = "cumul", IsChecked = system.Cumulative };
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
            years.Children.Add(_segmentBlock);
            first.Children.Add(AstrolabeUi.Section("Années et segments", "Où commence le compte des années, et s'il se découpe en âges, en ères, en cycles.", years, "system-section-years"));
            var notationBody = new StackPanel();
            var notation = new Grid { ColumnDefinitions = new ColumnDefinitions("190,8,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
            _yearPattern = new TextBox { Text = system.YearPattern };
            _negativePattern = new TextBox { Text = system.NegativePattern };
            _datePattern = new TextBox { Text = system.DatePattern };
            AddRow(notation, 0, "Notation d'une année :", _yearPattern);
            AddRow(notation, 1, "Années négatives :", _negativePattern);
            AddRow(notation, 2, "Notation d'une date :", _datePattern);
            // Sous la notation d'une date : une date d'exemple, mise en forme (14/11, Rémi).
            _dateExample = new TextBlock { FontSize = 12, Tag = "system-date-example" };
            var dateExampleBox = AstrolabeUi.Example(_dateExample);
            dateExampleBox.Margin = new Thickness(0, 2, 0, 3);
            Grid.SetRow(dateExampleBox, 3);
            Grid.SetColumn(dateExampleBox, 2);
            notation.Children.Add(dateExampleBox);
            notationBody.Children.Add(notation);
            notationBody.Children.Add(TimelineUi.Hint("{a} l'année · {s} le numéro du segment · {c} son abréviation · {n} son nom · {j} le jour · {m} le numéro du mois · {M} son nom · {année} la notation de l'année."));
            // L'an 0 des segments suivants : « A1-1 … puis A2-0 ». Ne vaut que
            // découpé, et quand chaque segment repart de zéro — grisée sinon.
            _segmentYearZero = new CheckBox { Content = "La première année de chaque segment est l'an 0", IsChecked = system.SegmentYearZero, Margin = new Thickness(0, 6, 0, 0), Tag = "system-segment-year-zero" };
            notationBody.Children.Add(_segmentYearZero);
            _example = new TextBlock { Tag = "system-example" };
            var exampleBox = AstrolabeUi.Example(_example);
            exampleBox.Margin = new Thickness(0, 9, 0, 0);
            notationBody.Children.Add(exampleBox);
            first.Children.Add(AstrolabeUi.Section("Notation", "Comment une année et une date s'écrivent — et se relisent — dans ce système.", notationBody, "system-section-notation"));
            // Le testeur dans son cadre arrondi : ce n'est pas un champ de configuration.
            var tryBody = new StackPanel();
            var tryRow = new Grid { ColumnDefinitions = new ColumnDefinitions("180,8,*") };
            tryRow.Children.Add(new TextBlock { Text = "Essayer une date :", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center });
            _try = new TextBox { Watermark = "écrivez une date pour la vérifier" };
            Grid.SetColumn(_try, 2);
            tryRow.Children.Add(_try);
            tryBody.Children.Add(tryRow);
            _tryEcho = new TextBlock { FontSize = 11.5, Margin = new Thickness(188, 3, 0, 0), TextWrapping = TextWrapping.Wrap, IsVisible = false };
            tryBody.Children.Add(_tryEcho);
            first.Children.Add(new Border
            {
                Child = tryBody, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 2), CornerRadius = new CornerRadius(10),
                BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Background = Chrome.FieldBg, Tag = "system-try-box"
            });
            tabs.Items.Add(new TabItem { Header = "Segments et notation", Content = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = first } });

            // ----------------------------------------------------------- calendrier
            var second = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            var monthsBody = new StackPanel();
            _months = new StackPanel { Spacing = 4 };
            monthsBody.Children.Add(new ScrollViewer { MaxHeight = 190, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _months });
            var monthButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Spacing = 10 };
            var addMonth = Buttons.IconText("plus-bold", "Ajouter un mois", "Un mois de plus, à la fin de l'année", Buttons.Compact, Buttons.Look.Outline);
            addMonth.Click += delegate { Collect(); _system.Months.Add(new TimeMonth { Name = "Mois " + (_system.Months.Count + 1), Days = 30 }); RebuildMonths(); RebuildLeaps(); Preview(); };
            monthButtons.Children.Add(addMonth);
            _yearLength = new TextBlock { FontSize = 12, Tag = "system-year-length" };
            var yearLengthBox = AstrolabeUi.Example(_yearLength);
            yearLengthBox.VerticalAlignment = VerticalAlignment.Center;
            monthButtons.Children.Add(yearLengthBox);
            monthsBody.Children.Add(monthButtons);
            second.Children.Add(AstrolabeUi.Section("Les mois de l'année", "Dans l'ordre, chacun avec son nombre de jours.", monthsBody, "system-section-months"));

            // LE RÉPARTITEUR (16/11, Rémi — ex-« Répartir automatiquement les jours ») : replié
            // dans son accordéon. Trois méthodes : tant de jours dans l'année, partagés
            // uniformément ou en bascule (+0 j, +1 j, +0 j…) ; ou des semaines entières par mois.
            var spread = new StackPanel();
            var spreadGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("190,8,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
            _spreadMode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "system-spread-mode" };
            _spreadMode.Items.Add("Uniformément");
            _spreadMode.Items.Add("En bascule (+0 j, +1 j, +0 j…)");
            _spreadMode.Items.Add("Par semaines entières");
            _spreadMode.Items.Add("En gardant les proportions actuelles");
            _spreadMode.SelectedIndex = 0;
            AddRow(spreadGrid, 0, "Méthode :", _spreadMode);
            _spreadDays = new TextBox { Text = system.BaseDays.ToString(CultureInfo.InvariantCulture), Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Tag = "system-spread-days" };
            AddRow(spreadGrid, 1, "Jours dans l'année :", _spreadDays);
            _spreadDaysLabel = (Control)spreadGrid.Children[spreadGrid.Children.Count - 2];
            _weeksPerMonth = new TextBox { Text = "4", Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Tag = "system-weeks-per-month" };
            AddRow(spreadGrid, 2, "Semaines par mois :", _weeksPerMonth);
            _weeksLabel = (Control)spreadGrid.Children[spreadGrid.Children.Count - 2];
            // LE QUOTIENT DE RÉPARTITION (16/11 soir, Rémi) : en bascule, de combien de jours les mois longs dépassent les courts.
            _spreadGap = new TextBox { Text = "1", Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Tag = "system-spread-gap" };
            ToolTip.SetTip(_spreadGap, "L'écart, en jours, entre un mois long et un mois court : 1 donne 30 / 31, 2 donne 29 / 31, 4 donne 28 / 32…");
            AddRow(spreadGrid, 3, "Quotient de répartition :", _spreadGap);
            _spreadGapLabel = (Control)spreadGrid.Children[spreadGrid.Children.Count - 2];
            spread.Children.Add(spreadGrid);
            _distributeEcho = new TextBlock { FontSize = 12, Tag = "system-distribute-echo" };
            var distributeBox = AstrolabeUi.Example(_distributeEcho);
            distributeBox.Margin = new Thickness(0, 8, 0, 0);
            spread.Children.Add(distributeBox);
            _distribute = Buttons.Text("Répartir dans les mois", null, Buttons.Compact, Buttons.Look.Primary);
            _distribute.HorizontalAlignment = HorizontalAlignment.Left;
            _distribute.Margin = new Thickness(0, 10, 0, 0);
            _distribute.Tag = "system-distribute";
            _distribute.Click += delegate { Distribute(); };
            spread.Children.Add(_distribute);
            _spreadAccordion = new Accordion("Répartiteur", "partager les jours de l'année entre les mois", spread, false, "system-spread");
            second.Children.Add(_spreadAccordion);

            var leapBody = new StackPanel();
            _leaps = new StackPanel { Spacing = 8 };
            leapBody.Children.Add(_leaps);
            var addLeap = Buttons.IconText("plus-bold", "Ajouter une règle", "Une règle de bissextile de plus", Buttons.Compact, Buttons.Look.Outline);
            addLeap.HorizontalAlignment = HorizontalAlignment.Left;
            addLeap.Margin = new Thickness(0, 8, 0, 0);
            addLeap.Click += delegate { Collect(); _system.Leaps.Add(new LeapRule { Every = 4, Month = 0, Days = 1 }); RebuildLeaps(); Preview(); };
            leapBody.Children.Add(addLeap);
            second.Children.Add(AstrolabeUi.Section("Années bissextiles", "Certaines années, un mois gagne des jours — pour que le calendrier ne glisse pas sous les saisons. Notre monde : tous les 4 ans, pas les multiples de 100, sauf ceux de 400.", leapBody, "system-section-leaps"));
            tabs.Items.Add(new TabItem { Header = "Calendrier", Content = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = second } });

            // ------------------------------------------------------ semaine, horloge
            var third = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
            var weekBody = new StackPanel();
            _days.AddRange(system.WeekDays);
            _dayRows = new StackPanel { Spacing = 4, Tag = "system-week-days" };
            weekBody.Children.Add(new ScrollViewer { MaxHeight = 232, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _dayRows });
            _weekDays = new TextBox { Watermark = "Ajouter un jour : son nom, puis une virgule ou Entrée", Margin = new Thickness(0, 6, 0, 0), AcceptsReturn = false };
            _weekDays.KeyDown += delegate(object sender, Avalonia.Input.KeyEventArgs e)
            {
                if (e.Key == Avalonia.Input.Key.Enter && e.KeyModifiers == Avalonia.Input.KeyModifiers.None && (_weekDays.Text ?? "").Trim().Length > 0)
                {
                    e.Handled = true; // sinon Entrée validait toute la fenêtre
                    _weekDays.Text = _weekDays.Text + ",";
                }
            };
            weekBody.Children.Add(_weekDays);
            third.Children.Add(AstrolabeUi.Section("Les jours de la semaine", "Dans l'ordre. Autant que vous voulez : une semaine de cinq, de dix jours… Aucun : pas de semaine.", weekBody, "system-section-week"));
            RebuildDays();
            // L'HORLOGE (16/11, Rémi) : heures, minutes et — nouveau — secondes ; à droite, le cadran qui les dessine.
            var clock = new Grid { ColumnDefinitions = new ColumnDefinitions("170,8,80"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto") };
            _hours = new TextBox { Text = system.HoursPerDay.ToString(CultureInfo.InvariantCulture), Tag = "system-hours" };
            _minutes = new TextBox { Text = system.MinutesPerHour.ToString(CultureInfo.InvariantCulture), Tag = "system-minutes" };
            _seconds = new TextBox { Text = system.SecondsPerMinute.ToString(CultureInfo.InvariantCulture), Tag = "system-seconds" };
            AddRow(clock, 0, "Heures par jour :", _hours);
            AddRow(clock, 1, "Minutes par heure :", _minutes);
            AddRow(clock, 2, "Secondes par minute :", _seconds);
            var clockLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            clockLeft.Children.Add(clock);
            _clockEcho = new TextBlock { FontSize = 12, Tag = "system-clock-echo" };
            var clockEchoBox = AstrolabeUi.Example(_clockEcho);
            clockEchoBox.Margin = new Thickness(0, 10, 0, 0);
            clockLeft.Children.Add(clockEchoBox);
            _clockHint = TimelineUi.Hint("");
            clockLeft.Children.Add(_clockHint);
            // LE DESSIN DU CADRAN (16/11 soir, Rémi) : hors de 24/60/60, la symétrie se compose.
            var design = new Grid { ColumnDefinitions = new ColumnDefinitions("190,8,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
            _dialTurns = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "system-dial-turns" };
            _dialHourStep = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "system-dial-hour-step" };
            _dialMinuteStep = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "system-dial-minute-step" };
            _dialNumerals = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "system-dial-numerals" };
            _dialNumerals.Items.Add("Chiffres arabes");
            _dialNumerals.Items.Add("Chiffres romains");
            _dialNumerals.Items.Add("Points");
            _dialNumerals.SelectedIndex = Math.Max(0, Math.Min(2, system.DialNumerals));
            AddRow(design, 0, "Tours du cadran par jour :", _dialTurns);
            AddRow(design, 1, "Heures chiffrées :", _dialHourStep);
            AddRow(design, 2, "Minutes chiffrées :", _dialMinuteStep);
            AddRow(design, 3, "Style des repères :", _dialNumerals);
            _dialTips = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = "system-dial-tips" };
            var designBody = new StackPanel();
            designBody.Children.Add(design);
            designBody.Children.Add(_dialTips);
            _dial = new ClockDial { Tag = "system-clock-dial", VerticalAlignment = VerticalAlignment.Center };
            var clockBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,18,Auto") };
            Grid.SetColumn(_dial, 2);
            clockBody.Children.Add(clockLeft);
            clockBody.Children.Add(_dial);
            third.Children.Add(AstrolabeUi.Section("L'horloge", "Ce que dure un jour. Sur le cadran : les heures au bord, les minutes au milieu, les secondes au centre.", clockBody, "system-section-clock"));
            third.Children.Add(AstrolabeUi.Section("Le dessin du cadran", "Rien ici ne change le compte du temps : seulement l'allure de l'horloge. Les pas proposés sont les diviseurs de vos nombres — ceux qui donnent des repères à intervalles égaux.", designBody, "system-section-dial"));
            tabs.Items.Add(new TabItem { Header = "Semaine et horloge", Content = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = third } });

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
            foreach (var box in new[] { _word, _yearPattern, _negativePattern, _datePattern, _try, _hours, _minutes, _seconds, _weekDays, _spreadDays }) box.TextChanged += changed;
            _spreadMode.SelectionChanged += delegate { Preview(); };
            _spreadGap.TextChanged += changed;
            foreach (var combo in new[] { _dialTurns, _dialHourStep, _dialMinuteStep, _dialNumerals }) combo.SelectionChanged += delegate { if (!_building && !_fillingDial) Preview(); };
            _weekDays.TextChanged += delegate { TakeDays(); };
            _weeksPerMonth.TextChanged += changed;
            _segmented.IsCheckedChanged += delegate
            {
                // La notation de base suit le découpage (14/11, Rémi) : « {a} » sans
                // segments, « A{s}.{a} » avec — sans toucher une notation déjà à sa place.
                var pattern = _yearPattern.Text ?? "";
                var names = pattern.Contains("{s}") || pattern.Contains("{c}") || pattern.Contains("{n}");
                if (_segmented.IsChecked == true && !names) _yearPattern.Text = SegmentedPattern();
                else if (_segmented.IsChecked != true && names) _yearPattern.Text = "{a}";
                Preview();
            };
            _yearZero.IsCheckedChanged += delegate { Preview(); };
            _segmentYearZero.IsCheckedChanged += delegate { Preview(); };
            _reset.IsCheckedChanged += delegate { Preview(); };
            _cumulative.IsCheckedChanged += delegate { Preview(); };
            Preview();
            Loaded += delegate { Dialogs.FitToScreen(this, owner); _name.Focus(); _name.SelectAll(); };
        }

        /// <summary>« A{s}.{a} » : l'initiale du mot des segments (sans accent), leur numéro, l'année.</summary>
        private string SegmentedPattern()
        {
            var word = (_word.Text ?? "").Trim();
            var initial = word.Length == 0 ? "S" : word.Substring(0, 1).Normalize(System.Text.NormalizationForm.FormD).Substring(0, 1).ToUpperInvariant();
            return initial + "{s}.{a}";
        }

        private int WeeksPerMonth() { return Math.Max(0, Math.Min(1000, TimelineUi.ParseInt(_weeksPerMonth.Text, 0))); }

        private void Distribute()
        {
            Collect();
            var mode = _spreadMode.SelectedIndex;
            var done = mode == SpreadWeeks ? TimeMath.Distribute(_system, WeeksPerMonth())
                : mode == SpreadRatios ? TimeMath.DistributeKeepingRatios(_system, SpreadDays())
                : TimeMath.Distribute(_system, SpreadDays(), mode == SpreadAlternate, SpreadGap());
            if (!done) return;
            RebuildMonths();
            Preview();
        }

        private const int SpreadEven = 0, SpreadAlternate = 1, SpreadWeeks = 2, SpreadRatios = 3;

        private int SpreadGap() { return Math.Max(1, Math.Min(10000, TimelineUi.ParseInt(_spreadGap.Text, 1))); }

        private bool _fillingDial;
        private int _turnsKey = -1;

        /// <summary>Les listes du dessin du cadran dépendent des nombres de l'horloge : regarnies quand
        /// ils changent (les diviseurs de 20 ne sont pas ceux de 24), en gardant le choix s'il vaut encore.</summary>
        private void FillDial()
        {
            var turns = ClockDial.ValidTurns(_system.HoursPerDay, _system.DialTurns);
            var key = _system.HoursPerDay + "/" + _system.MinutesPerHour + "/" + turns;
            if (key == _dialKey) return;
            _dialKey = key;
            _fillingDial = true;
            // La liste des tours ne dépend que des heures du jour : on ne la regarnit pas quand c'est
            // ELLE qui vient de changer (vider une liste depuis son propre SelectionChanged fait lever Avalonia).
            if (_turnsKey != _system.HoursPerDay)
            {
                _turnsKey = _system.HoursPerDay;
                _turnChoices = ClockDial.TurnChoices(_system.HoursPerDay);
                _dialTurns.Items.Clear();
                foreach (var choice in _turnChoices)
                    _dialTurns.Items.Add(choice == 1 ? "Un tour (" + _system.HoursPerDay + " heures au cadran)" : choice + " tours (" + (_system.HoursPerDay / choice) + " heures au cadran)");
                _dialTurns.SelectedIndex = Math.Max(0, _turnChoices.IndexOf(turns));
            }
            var perTurn = _system.HoursPerDay / turns;
            _hourStepChoices = ClockDial.StepChoices(perTurn);
            _dialHourStep.Items.Clear();
            _dialHourStep.Items.Add("Au mieux (toutes les " + ClockDial.AutoStep(perTurn) + ")");
            foreach (var step in _hourStepChoices) _dialHourStep.Items.Add("Toutes les " + step + " — " + (perTurn / step) + " repères");
            _dialHourStep.SelectedIndex = _hourStepChoices.IndexOf(_system.DialHourStep) + 1;
            _minuteStepChoices = ClockDial.StepChoices(_system.MinutesPerHour);
            _dialMinuteStep.Items.Clear();
            _dialMinuteStep.Items.Add("Au mieux (toutes les " + ClockDial.AutoStep(_system.MinutesPerHour) + ")");
            foreach (var step in _minuteStepChoices) _dialMinuteStep.Items.Add("Toutes les " + step + " — " + (_system.MinutesPerHour / step) + " repères");
            _dialMinuteStep.SelectedIndex = _minuteStepChoices.IndexOf(_system.DialMinuteStep) + 1;
            // Un pas qui ne divise plus le nouveau compte retombe sur « Au mieux ».
            _system.DialTurns = turns;
            if (_hourStepChoices.IndexOf(_system.DialHourStep) < 0) _system.DialHourStep = 0;
            if (_minuteStepChoices.IndexOf(_system.DialMinuteStep) < 0) _system.DialMinuteStep = 0;
            _fillingDial = false;
        }

        private int SpreadDays() { return Math.Max(0, Math.Min(10000000, TimelineUi.ParseInt(_spreadDays.Text, 0))); }

        /// <summary>Ce que le Répartiteur ferait, dit avant de le faire : « 12 mois : 30, 31, 30… = 365 jours ».</summary>
        private string SpreadPreview(out bool possible)
        {
            var months = _system.Months.Count;
            var mode = _spreadMode.SelectedIndex;
            if (mode == SpreadWeeks)
            {
                var weeks = WeeksPerMonth();
                var weekLength = _system.WeekDays.Count;
                possible = weekLength > 0 && weeks > 0;
                return weekLength == 0 ? "Donnez d'abord ses jours à la semaine (onglet « Semaine et horloge »)."
                    : weeks == 0 ? "Indiquez un nombre de semaines par mois."
                    : TimeSystem.Count(weeks, "semaine", "semaines") + " de " + TimeSystem.Count(weekLength, "jour", "jours") + " : "
                        + (weeks * weekLength) + " jours par mois, " + (weeks * weekLength * months) + " par an sur " + TimeSystem.Count(months, "mois", "mois") + ".";
            }
            var current = new List<int>();
            foreach (var month in _system.Months) current.Add(month.Days);
            var days = mode == SpreadRatios ? TimeMath.SpreadKeepingRatios(current, SpreadDays()) : TimeMath.Spread(months, SpreadDays(), mode == SpreadAlternate, SpreadGap());
            if (days == null && mode == SpreadAlternate && TimeMath.Spread(months, SpreadDays(), true, 1) != null)
            {
                possible = false;
                return "Ce quotient est trop grand pour " + TimeSystem.Count(SpreadDays(), "jour", "jours") + " : les mois courts n'auraient plus de jours.";
            }
            possible = days != null;
            if (days == null) return "Indiquez le nombre de jours de l'année : au moins un par mois (" + months + ").";
            var shown = new List<string>();
            for (var i = 0; i < days.Length && i < 16; i++) shown.Add(days[i].ToString(CultureInfo.InvariantCulture));
            return TimeSystem.Count(months, "mois", "mois") + " : " + string.Join(", ", shown.ToArray()) + (days.Length > 16 ? "…" : "") + "  =  " + TimeSystem.Count(SpreadDays(), "jour", "jours") + ".";
        }

        private const string ResetLabel = "Chaque segment repart de zéro";
        private const string CumulativeLabel = "Années cumulatives entre segments";

        // ------------------------------------------------- les jours en pastilles

        /// <summary>Une virgule dans la zone : ce qui la précède devient une
        /// pastille (plusieurs d'un coup si l'on colle « lundi, mardi, »).</summary>
        private void TakeDays()
        {
            var text = _weekDays.Text ?? "";
            var comma = text.LastIndexOf(',');
            if (comma < 0) return;
            foreach (var part in text.Substring(0, comma).Split(','))
                if (part.Trim().Length > 0) _days.Add(part.Trim());
            _weekDays.Text = text.Substring(comma + 1).TrimStart();
            RebuildDays();
            Preview();
        }

        private void MoveDay(int from, int to)
        {
            if (from < 0 || from >= _days.Count) return;
            to = Math.Max(0, Math.Min(_days.Count - 1, to));
            if (from == to) return;
            var day = _days[from];
            _days.RemoveAt(from);
            _days.Insert(to, day);
            RebuildDays();
            Preview();
        }

        private void RebuildDays()
        {
            var was = _building;
            _building = true;
            _dayRows.Children.Clear();
            for (var i = 0; i < _days.Count; i++) _dayRows.Children.Add(DayRow(i));
            _dayRows.IsVisible = _days.Count > 0;
            _building = was;
        }

        /// <summary>La ligne d'un jour : son rang, son nom (modifiable), les
        /// flèches qui le montent et le descendent, la croix qui l'efface.</summary>
        private Grid DayRow(int index)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,8,26,2,26,8,26"), Tag = "system-week-day" };
            row.Children.Add(new TextBlock { Text = (index + 1) + ".", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBox { Text = _days[index] };
            Grid.SetColumn(name, 1);
            name.TextChanged += delegate
            {
                if (_building || index >= _days.Count) return;
                _days[index] = (name.Text ?? "").Trim();
                Preview();
            };
            var up = Buttons.Icon("caret-up-bold", "Monter ce jour", Buttons.Compact, Buttons.Look.Calm);
            up.IsEnabled = index > 0;
            up.Click += delegate { MoveDay(index, index - 1); };
            Grid.SetColumn(up, 3);
            var down = Buttons.Icon("caret-down-bold", "Descendre ce jour", Buttons.Compact, Buttons.Look.Calm);
            down.IsEnabled = index < _days.Count - 1;
            down.Click += delegate { MoveDay(index, index + 1); };
            Grid.SetColumn(down, 5);
            var remove = Buttons.Icon("x-bold", "Effacer ce jour", Buttons.Compact, Buttons.Look.Calm);
            remove.Click += delegate
            {
                if (index < _days.Count) _days.RemoveAt(index);
                RebuildDays();
                Preview();
            };
            Grid.SetColumn(remove, 7);
            row.Children.Add(name);
            row.Children.Add(up);
            row.Children.Add(down);
            row.Children.Add(remove);
            return row;
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
                // UNE RÈGLE = UNE CARTE (16/11, Rémi : l'ancienne ligne passait à la ligne et la
                // croix tombait dessous). Deux phrases qui se lisent, la croix calée en haut à
                // droite, et dessous ce que la règle donne : « Bissextiles : 4, 8, 12… ».
                Func<string, TextBlock> word = delegate(string text) { return new TextBlock { Text = text, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }; };
                var every = new TextBox { Text = leap.Every.ToString(CultureInfo.InvariantCulture), Width = 52, Margin = new Thickness(0, 0, 6, 0) };
                var except = new TextBox { Text = leap.Except > 0 ? leap.Except.ToString(CultureInfo.InvariantCulture) : "", Width = 58, Watermark = "—", Margin = new Thickness(0, 0, 6, 0) };
                var unless = new TextBox { Text = leap.Unless > 0 ? leap.Unless.ToString(CultureInfo.InvariantCulture) : "", Width = 58, Watermark = "—" };
                var days = new TextBox { Text = leap.Days.ToString(CultureInfo.InvariantCulture), Width = 44, Margin = new Thickness(0, 0, 6, 0) };
                var month = new ComboBox { MinWidth = 130, Margin = new Thickness(0, 0, 6, 0) };
                foreach (var m in _system.Months) month.Items.Add(m.Name);
                month.SelectedIndex = Math.Max(0, Math.Min(_system.Months.Count - 1, leap.Month));
                var remove = Buttons.Icon("x-bold", "Retirer cette règle", Buttons.Compact, Buttons.Look.Calm);
                remove.VerticalAlignment = VerticalAlignment.Top;
                remove.Tag = "system-leap-remove";
                var captured = leap;
                remove.Click += delegate { Collect(); _system.Leaps.Remove(captured); RebuildLeaps(); Preview(); };
                var rule = new StackPanel { Orientation = Orientation.Horizontal };
                rule.Children.Add(word("Tous les"));
                rule.Children.Add(every);
                rule.Children.Add(word("ans,"));
                rule.Children.Add(month);
                rule.Children.Add(word("gagne"));
                rule.Children.Add(days);
                rule.Children.Add(word("jour(s)."));
                var exception = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                exception.Children.Add(word("Pas les années multiples de"));
                exception.Children.Add(except);
                exception.Children.Add(word("— sauf celles multiples de"));
                exception.Children.Add(unless);
                var echo = new TextBlock { Foreground = Chrome.AccentStrong, FontSize = 11.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0), Tag = "system-leap-echo" };
                var sentences = new StackPanel();
                sentences.Children.Add(rule);
                sentences.Children.Add(exception);
                sentences.Children.Add(echo);
                var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
                Grid.SetColumn(remove, 2);
                layout.Children.Add(sentences);
                layout.Children.Add(remove);
                var row = new Border
                {
                    Child = layout, Padding = new Thickness(12, 9, 8, 10), CornerRadius = new CornerRadius(8), Background = Chrome.FieldBg,
                    BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = new object[] { leap, every, except, unless, days, month, echo }
                };
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

        /// <summary>Ce qu'une règle de bissextile donne, sur ses premières années :
        /// « Bissextiles : 4, 8, 12, 16… · pas 100, 200 · mais 400 ».</summary>
        private static string LeapEcho(LeapRule leap)
        {
            var every = Math.Max(1, leap.Every);
            var yes = new List<string>();
            for (long year = every; yes.Count < 4 && year <= every * 400L; year += every)
                if (leap.IsLeap(year)) yes.Add(year.ToString(CultureInfo.InvariantCulture));
            if (yes.Count == 0) return "Cette règle ne donne aucune année bissextile.";
            var text = "→ Bissextiles : les années " + string.Join(", ", yes.ToArray()) + "…";
            if (leap.Except > 0)
            {
                var no = new List<string>();
                for (long year = leap.Except; no.Count < 2 && year <= leap.Except * 400L; year += leap.Except)
                    if (year % every == 0 && !leap.IsLeap(year)) no.Add(year.ToString(CultureInfo.InvariantCulture));
                if (no.Count > 0) text += "  ·  pas " + string.Join(", ", no.ToArray());
                if (leap.Unless > 0)
                    for (long year = leap.Unless; year <= leap.Unless * 400L; year += leap.Unless)
                        if (leap.IsLeap(year)) { text += "  ·  mais " + year.ToString(CultureInfo.InvariantCulture) + " oui"; break; }
            }
            return text + "  (" + (leap.Days >= 0 ? "+" : "") + leap.Days + " j)";
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
            _system.SegmentYearZero = _segmentYearZero.IsChecked == true;
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
                var row = child as Control;
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
            _system.WeekDays.AddRange(_days);
            // Un nom écrit sans sa virgule compte aussi : rien ne se perd à l'enregistrement.
            if ((_weekDays.Text ?? "").Trim().Length > 0) _system.WeekDays.Add(_weekDays.Text.Trim());
            _system.HoursPerDay = Math.Max(1, Math.Min(1000, TimelineUi.ParseInt(_hours.Text, 24)));
            _system.MinutesPerHour = Math.Max(1, Math.Min(1000, TimelineUi.ParseInt(_minutes.Text, 60)));
            _system.SecondsPerMinute = Math.Max(1, Math.Min(1000, TimelineUi.ParseInt(_seconds.Text, 60)));
            // Le dessin du cadran : lu dans ses listes tant qu'elles valent pour ces nombres.
            if (!_fillingDial && _dialKey != null)
            {
                if (_dialTurns.SelectedIndex >= 0 && _dialTurns.SelectedIndex < _turnChoices.Count) _system.DialTurns = _turnChoices[_dialTurns.SelectedIndex];
                if (_dialHourStep.SelectedIndex >= 0) _system.DialHourStep = _dialHourStep.SelectedIndex == 0 || _dialHourStep.SelectedIndex > _hourStepChoices.Count ? 0 : _hourStepChoices[_dialHourStep.SelectedIndex - 1];
                if (_dialMinuteStep.SelectedIndex >= 0) _system.DialMinuteStep = _dialMinuteStep.SelectedIndex == 0 || _dialMinuteStep.SelectedIndex > _minuteStepChoices.Count ? 0 : _minuteStepChoices[_dialMinuteStep.SelectedIndex - 1];
                if (_dialNumerals.SelectedIndex >= 0) _system.DialNumerals = _dialNumerals.SelectedIndex;
            }
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
            _segmentYearZero.IsEnabled = _system.Segmented && !_system.Cumulative;
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
            // Le Répartiteur : ses champs selon la méthode, et ce qu'il ferait.
            var byWeeks = _spreadMode.SelectedIndex == SpreadWeeks;
            _spreadDays.IsVisible = !byWeeks;
            _spreadDaysLabel.IsVisible = !byWeeks;
            _spreadGap.IsVisible = _spreadMode.SelectedIndex == SpreadAlternate;
            _spreadGapLabel.IsVisible = _spreadMode.SelectedIndex == SpreadAlternate;
            _weeksPerMonth.IsVisible = byWeeks;
            _weeksLabel.IsVisible = byWeeks;
            bool spreadable;
            _distributeEcho.Text = SpreadPreview(out spreadable);
            _distribute.IsEnabled = spreadable;
            // L'horloge : le cadran et ce qu'un jour contient.
            FillDial();
            _dial.Set(_system.HoursPerDay, _system.MinutesPerHour, _system.SecondsPerMinute, _system.DialTurns, _system.DialHourStep, _system.DialMinuteStep, _system.DialNumerals);
            _dialTips.Children.Clear();
            foreach (var tip in ClockDial.Tips(_system.HoursPerDay, _system.MinutesPerHour, _system.SecondsPerMinute, _system.DialTurns))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
                var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Chrome.Accent, Margin = new Thickness(1, 6, 8, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = tip, Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                _dialTips.Children.Add(row);
            }
            var perDay = (long)_system.HoursPerDay * _system.MinutesPerHour * _system.SecondsPerMinute;
            _clockEcho.Text = "Un jour : " + TimeSystem.Count((long)_system.HoursPerDay * _system.MinutesPerHour, "minute", "minutes") + ", " + TimeSystem.Count(perDay, "seconde", "secondes")
                + (perDay == 86400 ? "" : " (notre monde : 86 400)") + "\nLes aiguilles marquent " + ClockDial.Sample(_system.HoursPerDay, _system.MinutesPerHour, _system.SecondsPerMinute) + ".";
            foreach (var child in _leaps.Children)
            {
                var card = child as Control;
                var cells = card == null ? null : card.Tag as object[];
                if (cells != null) ((TextBlock)cells[6]).Text = LeapEcho((LeapRule)cells[0]);
            }
            if (problem != null && _system.NotationProblem() != null)
            {
                _example.Text = ""; _dateExample.Text = ""; _tryEcho.Text = ""; _tryEcho.IsVisible = false;
                _reset.Content = ResetLabel; _cumulative.Content = CumulativeLabel;
                return;
            }
            _negativePattern.Watermark = "vide : " + NegativeSample();
            // Les deux choix se disent dans la notation en cours : « A2-1 suit A1-1000 ».
            if (_system.HasSegments && _system.Segments.Count > 1)
            {
                _reset.Content = ResetLabel + " (" + Boundary(false) + ")";
                _cumulative.Content = CumulativeLabel + " (" + Boundary(true) + ")";
            }
            else { _reset.Content = ResetLabel; _cumulative.Content = CumulativeLabel; }
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
            _example.Text = "Exemples : " + string.Join("   ·   ", parts.ToArray());
            var sampleMonth = Math.Min(3, _system.Months.Count);
            var sampleDate = _system.Months.Count == 0 ? "" : _system.Format(new TimePoint(year0, sampleMonth, Math.Min(12, _system.DaysInMonth(year0, sampleMonth))));
            _dateExample.Text = sampleDate.Length == 0 ? "" : "→ " + sampleDate;
            _clockHint.Text = "Une date précise au jour peut porter une heure, jusqu'aux secondes : « " + sampleDate + " " + ClockDial.Sample(_system.HoursPerDay, _system.MinutesPerHour, _system.SecondsPerMinute) + " ».";
            var tried = (_try.Text ?? "").Trim();
            _tryEcho.IsVisible = tried.Length > 0;
            if (tried.Length == 0) { _tryEcho.Text = ""; return; }
            TimePoint point;
            if (_system.TryParse(tried, out point))
            {
                _tryEcho.Foreground = Chrome.Ink;
                var weekDay = _system.WeekDayOf(point);
                _tryEcho.Text = "→ " + (_system.HasSegments ? _system.SegmentName(_system.SegmentIndexOf(point.Year)) + " · " : "") + _system.Format(point) + (weekDay != null ? " (" + weekDay + ")" : "");
            }
            else
            {
                _tryEcho.Foreground = Chrome.Danger;
                _tryEcho.Text = "Cette date ne se lit pas dans la notation.";
            }
        }

        /// <summary>Le passage du premier segment au deuxième, lu dans l'un des
        /// deux modes : « A2-1 suit A1-1000 ».</summary>
        private string Boundary(bool cumulative)
        {
            var probe = _system.Clone();
            probe.Cumulative = cumulative;
            var boundary = probe.SegmentStart(1);
            return probe.FormatYear(boundary) + " suit " + probe.FormatYear(boundary - 1);
        }

        /// <summary>Une année négative telle qu'elle s'écrit quand sa notation est laissée vide.</summary>
        private string NegativeSample()
        {
            var probe = _system.Clone();
            probe.NegativePattern = "";
            return probe.FormatYear(-320);
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
        internal void ShowTabForProbe(int index)
        {
            foreach (var tabs in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this))
                if (tabs is TabControl) { ((TabControl)tabs).SelectedIndex = index; return; }
        }
        internal string DateExampleForProbe { get { return _dateExample.Text ?? ""; } }
        internal bool SegmentedForProbe { get { return _segmented.IsChecked == true; } set { _segmented.IsChecked = value; } }
        internal bool SegmentYearZeroEnabledForProbe { get { return _segmentYearZero.IsEnabled; } }
        internal void SetSegmentYearZeroForProbe(bool on) { _segmentYearZero.IsChecked = on; }
        internal string YearPatternForProbe { get { return _yearPattern.Text ?? ""; } }
        internal string WordForProbe { get { return _word.Text ?? ""; } }
        internal void TypeDayForProbe(string text) { _weekDays.Text = text; }
        internal string DaysForProbe { get { return string.Join("|", _days.ToArray()); } }
        internal int DayRowsForProbe { get { return _dayRows.Children.Count; } }
        internal void ClickDayArrowForProbe(int index, bool up) { ((Button)((Grid)_dayRows.Children[index]).Children[up ? 2 : 3]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); }
        internal bool DayArrowEnabledForProbe(int index, bool up) { return ((Button)((Grid)_dayRows.Children[index]).Children[up ? 2 : 3]).IsEnabled; }
        internal void RemoveDayForProbe(int index) { ((Button)((Grid)_dayRows.Children[index]).Children[4]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); }
        internal string DatePatternForProbe { get { return _datePattern.Text ?? ""; } }
        internal void DistributeForProbe(string weeks) { _spreadMode.SelectedIndex = SpreadWeeks; _weeksPerMonth.Text = weeks; Distribute(); }
        internal bool DistributeEnabledForProbe { get { return _distribute.IsEnabled; } }
        internal string DistributeEchoForProbe { get { return _distributeEcho.Text ?? ""; } }
        internal string MonthDaysForProbe { get { var days = new List<string>(); foreach (var month in _system.Months) days.Add(month.Days.ToString(CultureInfo.InvariantCulture)); return string.Join(",", days.ToArray()); } }
        internal void MoveDayForProbe(int from, int to) { MoveDay(from, to); }
        internal string PendingDayForProbe { get { return _weekDays.Text ?? ""; } }
        internal bool TryBoxFramedForProbe
        {
            get { var box = _try.Parent == null ? null : _try.Parent.Parent == null ? null : _try.Parent.Parent.Parent as Border; return box != null && box.CornerRadius.TopLeft > 0 && box.BorderThickness.Left > 0; }
        }
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

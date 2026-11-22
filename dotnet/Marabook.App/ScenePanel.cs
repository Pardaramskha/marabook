using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>« SCÈNE » DANS LE RAIL (2.0.0, 21/11/2026) : la fiche de la
    /// scène où se trouve le curseur — point de vue, lieu, moment, personnages,
    /// note. Un écrit coupé par des séparateurs de scène a autant de fiches ;
    /// les flèches passent de l'une à l'autre (et y mènent le curseur).</summary>
    internal sealed class ScenePanel : StackPanel
    {
        private readonly TextBlock _header, _opening, _hint;
        private readonly Button _previous, _next, _edit;
        private readonly StackPanel _rows, _orphans;
        private Project _project;
        private BinderItem _item;
        private List<Scene> _scenes = new List<Scene>();
        private int _index;

        /// <summary>« Renseigner la scène… » : (écrit, numéro de scène).</summary>
        public event Action<BinderItem, int> EditRequested;
        /// <summary>Aller au début de cette scène : son premier paragraphe.</summary>
        public event Action<int> GoToParagraph;
        public event Action<BinderItem> Navigate;
        /// <summary>Une fiche sans scène reprise ou supprimée : (écrit, les fiches d'avant).</summary>
        public event Action<BinderItem, List<SceneMeta>> OrphansChanged;

        public ScenePanel()
        {
            Margin = new Thickness(0, 0, 0, 10);
            Tag = "scene-panel";
            _header = new TextBlock { Text = "Scène", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Tag = "scene-header" };
            ToolTip.SetTip(_header, "Ce qu'on sait de la scène où se trouve le curseur. Chaque séparateur de scène du texte commence une nouvelle scène.");
            _previous = Buttons.Icon("caret-left-bold", "Scène précédente", Buttons.Compact, Buttons.Look.Calm);
            _next = Buttons.Icon("caret-right-bold", "Scène suivante", Buttons.Compact, Buttons.Look.Calm);
            _previous.Tag = "scene-previous";
            _next.Tag = "scene-next";
            _previous.Click += delegate { Go(_index - 1); };
            _next.Click += delegate { Go(_index + 1); };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            DockPanel.SetDock(_next, Dock.Right);
            DockPanel.SetDock(_previous, Dock.Right);
            head.Children.Add(_next);
            head.Children.Add(_previous);
            head.Children.Add(_header);
            Children.Add(head);
            _opening = new TextBlock { FontSize = 11, FontStyle = FontStyle.Italic, Foreground = Chrome.SoftText, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4) };
            Children.Add(_opening);
            _rows = new StackPanel { Tag = "scene-rows" };
            Children.Add(_rows);
            _edit = Buttons.IconText("pencil-simple-line", "Renseigner la scène…", "Point de vue, lieu, moment, personnages présents et note de cette scène", Buttons.Compact, Buttons.Look.Outline);
            _edit.Tag = "scene-edit";
            _edit.HorizontalAlignment = HorizontalAlignment.Left;
            _edit.Margin = new Thickness(0, 6, 0, 0);
            _edit.Click += delegate { var handler = EditRequested; if (handler != null && _item != null) handler(_item, _index); };
            Children.Add(_edit);
            _hint = new TextBlock { FontSize = 11, Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Text = "Un séparateur de scène dans le texte (ruban Insertion) commence une nouvelle scène, avec sa propre fiche." };
            Children.Add(_hint);
            _orphans = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Tag = "scene-orphans" };
            Children.Add(_orphans);
        }

        public int SceneIndex { get { return _index; } }
        public int SceneCount { get { return _scenes.Count; } }

        /// <summary>Montre les scènes de cet écrit ; « paragraph » = là où est le curseur.</summary>
        public void Load(Project project, BinderItem item, int paragraph)
        {
            _project = project;
            _item = item;
            _scenes = item == null ? new List<Scene>() : Scenes.Of(item);
            _index = item == null ? 0 : Math.Max(0, Math.Min(_scenes.Count - 1, Scenes.IndexAt(item.Document, paragraph)));
            Render();
        }

        /// <summary>Le curseur a bougé : la fiche suit s'il a changé de scène.
        /// Rend vrai si le panneau a été redessiné.</summary>
        public bool Follow(int paragraph)
        {
            if (_item == null) return false;
            var count = Scenes.CountOf(_item);
            var index = Math.Max(0, Math.Min(count - 1, Scenes.IndexAt(_item.Document, paragraph)));
            if (count == _scenes.Count && index == _index) return false;
            Load(_project, _item, paragraph);
            return true;
        }

        public void Refresh()
        {
            if (_item == null) return;
            var keep = _index;
            _scenes = Scenes.Of(_item);
            _index = Math.Max(0, Math.Min(_scenes.Count - 1, keep));
            Render();
        }

        private void Go(int index)
        {
            if (index < 0 || index >= _scenes.Count) return;
            _index = index;
            Render();
            var handler = GoToParagraph;
            if (handler != null) handler(Math.Max(0, Math.Min(_scenes[index].First, _item.Document.Paragraphs.Count - 1)));
        }

        private void Render()
        {
            _rows.Children.Clear();
            _orphans.Children.Clear();
            if (_item == null || _scenes.Count == 0) return;
            var scene = _scenes[_index];
            var many = _scenes.Count > 1;
            _header.Text = many ? "Scène " + (_index + 1) + " sur " + _scenes.Count : "Scène";
            _previous.IsVisible = many;
            _next.IsVisible = many;
            _previous.IsEnabled = _index > 0;
            _next.IsEnabled = _index < _scenes.Count - 1;
            _opening.IsVisible = many;
            _opening.Text = Scenes.Quote(scene.Opening);
            ToolTip.SetTip(_opening, Scenes.Quote(scene.Opening) + " — " + TextGoal.Count(scene.Words, TextGoal.Words));
            _hint.IsVisible = !many;

            var meta = scene.Meta;
            if (meta.IsEmpty)
                _rows.Children.Add(new TextBlock { Text = "Rien de renseigné.", Foreground = Chrome.SoftText, FontSize = 12 });
            else
            {
                if (meta.PovId != null) _rows.Children.Add(Row("Point de vue", Scenes.SheetTitle(_project, meta.PovId), _project.FindById(meta.PovId)));
                var place = Scenes.PlaceLabel(_project, meta);
                if (place.Length > 0)
                {
                    BinderItem target = meta.PlaceSheetId != null ? _project.FindById(meta.PlaceSheetId) : null;
                    if (target == null && meta.PlaceMapId != null) target = _project.FindById(meta.PlaceMapId);
                    _rows.Children.Add(Row("Lieu", place, target));
                }
                if (!string.IsNullOrWhiteSpace(meta.When)) _rows.Children.Add(Row("Moment", meta.When, null));
                foreach (var id in meta.CharacterIds)
                    _rows.Children.Add(Row(id == meta.CharacterIds[0] ? "Présents" : "", Scenes.SheetTitle(_project, id), _project.FindById(id)));
                if (!string.IsNullOrWhiteSpace(meta.Note)) _rows.Children.Add(Row("Note", meta.Note, null));
            }
            _edit.Content = Icons.Label("pencil-simple-line", meta.IsEmpty ? "Renseigner la scène…" : "Modifier…", 12, Chrome.Ink);

            // Les fiches que le texte ne porte plus (deux scènes fondues en une) : jamais jetées en silence.
            var orphans = Scenes.Orphans(_item);
            if (orphans.Count == 0) return;
            _orphans.Children.Add(new TextBlock
            {
                Text = orphans.Count == 1 ? "1 fiche de scène sans scène" : orphans.Count + " fiches de scène sans scène",
                Foreground = Chrome.Warn, FontSize = 12, FontWeight = FontWeight.SemiBold,
                [ToolTip.TipProperty] = "Un séparateur de scène a été retiré : cette fiche n'a plus de scène à elle. Reprenez-la sur la scène affichée, ou supprimez-la."
            });
            foreach (var orphan in orphans)
            {
                var captured = orphan;
                var summary = Summary(orphan);
                _orphans.Children.Add(new TextBlock { Text = summary, FontSize = 11, Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 2) });
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var take = Buttons.Text("Reprendre ici", "Remplace la fiche de la scène affichée par celle-ci", Buttons.Compact, Buttons.Look.Outline);
                take.Tag = "scene-orphan-take";
                take.Click += delegate
                {
                    var before = Scenes.CloneAll(_item.Scenes);
                    var at = _item.Scenes.IndexOf(scene.Meta);
                    if (at < 0) return;
                    captured.Anchor = scene.Opening;
                    _item.Scenes[at] = captured;
                    _item.Scenes.RemoveAt(_item.Scenes.LastIndexOf(captured));
                    RaiseOrphans(before);
                };
                var drop = Buttons.Text("Supprimer", "Jette cette fiche de scène", Buttons.Compact, Buttons.Look.Calm);
                drop.Tag = "scene-orphan-drop";
                drop.Margin = new Thickness(6, 0, 0, 0);
                drop.Click += delegate
                {
                    var before = Scenes.CloneAll(_item.Scenes);
                    _item.Scenes.Remove(captured);
                    RaiseOrphans(before);
                };
                actions.Children.Add(take);
                actions.Children.Add(drop);
                _orphans.Children.Add(actions);
            }
        }

        private void RaiseOrphans(List<SceneMeta> before)
        {
            var handler = OrphansChanged;
            if (handler != null) handler(_item, before);
            Refresh();
        }

        private string Summary(SceneMeta meta)
        {
            var parts = new List<string>();
            if (meta.PovId != null) parts.Add("point de vue : " + Scenes.SheetTitle(_project, meta.PovId));
            var place = Scenes.PlaceLabel(_project, meta);
            if (place.Length > 0) parts.Add("lieu : " + place);
            if (!string.IsNullOrWhiteSpace(meta.When)) parts.Add("moment : " + meta.When);
            if (meta.CharacterIds.Count > 0) parts.Add("présents : " + Scenes.CharactersLabel(_project, meta));
            if (!string.IsNullOrWhiteSpace(meta.Note)) parts.Add(meta.Note);
            if (!string.IsNullOrEmpty(meta.Anchor)) parts.Add("(était : " + Scenes.Quote(meta.Anchor) + ")");
            return string.Join(" · ", parts.ToArray());
        }

        /// <summary>Une ligne « libellé : valeur » ; la valeur ouvre sa fiche (ou sa carte) si elle en a une.</summary>
        private Control Row(string label, string value, BinderItem target)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("78,*"), Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Chrome.SoftText });
            var text = new TextBlock { Text = value, FontSize = 12, Foreground = target != null ? Chrome.Accent : Chrome.Ink, TextWrapping = TextWrapping.Wrap };
            if (target != null)
            {
                text.Cursor = new Cursor(StandardCursorType.Hand);
                text.Background = Brushes.Transparent;
                text.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                {
                    if (!e.GetCurrentPoint(text).Properties.IsLeftButtonPressed) return;
                    var handler = Navigate;
                    if (handler != null) handler(target);
                };
            }
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            return row;
        }

        // — Sonde.
        internal string HeaderForProbe { get { return _header.Text ?? ""; } }
        internal string TextForProbe
        {
            get
            {
                var parts = new List<string>();
                foreach (var child in _rows.Children)
                {
                    var grid = child as Grid;
                    if (grid != null) { foreach (var cell in grid.Children) { var block = cell as TextBlock; if (block != null && !string.IsNullOrEmpty(block.Text)) parts.Add(block.Text); } }
                    var plain = child as TextBlock;
                    if (plain != null) parts.Add(plain.Text);
                }
                return string.Join(" | ", parts.ToArray());
            }
        }
        internal int OrphansForProbe { get { return _item == null ? 0 : Scenes.Orphans(_item).Count; } }
        internal Button NextForProbe { get { return _next; } }
        internal Button EditForProbe { get { return _edit; } }
        internal Button OrphanButtonForProbe(string tag)
        {
            foreach (var child in _orphans.Children)
            {
                var panel = child as StackPanel;
                if (panel == null) continue;
                foreach (var inner in panel.Children) { var button = inner as Button; if (button != null && (button.Tag as string) == tag) return button; }
            }
            return null;
        }
    }
}

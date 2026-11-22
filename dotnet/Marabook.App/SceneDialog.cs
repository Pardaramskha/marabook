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
    /// <summary>LA FICHE D'UNE SCÈNE (2.0.0, 21/11/2026, Rémi) : qui la
    /// raconte, où, quand, qui s'y trouve, et une ligne libre. Tout est
    /// facultatif. La fenêtre ne touche jamais au texte de l'écrit.</summary>
    public class SceneDialog : Window
    {
        private readonly Project _project;
        private readonly BinderItem _item;
        private readonly Scene _scene;
        private readonly SceneMeta _meta; // la copie de travail
        private readonly ComboBox _pov, _place, _timeline, _character;
        private readonly TextBox _when, _note;
        private readonly TextBlock _whenEcho;
        private readonly WrapPanel _chips, _suggested;
        private readonly TextBlock _suggestedLabel;
        private readonly List<BinderItem> _sheets = new List<BinderItem>();      // les fiches, personnages d'abord
        private readonly List<string> _sheetLabels = new List<string>();
        private readonly List<object> _placeTargets = new List<object>();        // BinderItem (fiche) ou EventPlace (carte)
        private readonly List<string> _placeLabels = new List<string>();
        private readonly List<BinderItem> _timelines;
        private bool _accepted;

        private SceneDialog(Window owner, Project project, BinderItem item, Scene scene, int count)
        {
            _project = project;
            _item = item;
            _scene = scene;
            _meta = scene.Meta.Clone();
            TimelineUi.Dress(this, owner, count > 1 ? "Scène " + (scene.Index + 1) + " sur " + count + " — " + item.Title : "Scène — " + item.Title);
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;

            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 440 };
            if (scene.Opening.Length > 0)
                panel.Children.Add(new TextBlock
                {
                    Text = Scenes.Quote(scene.Opening) + "  ·  " + TextGoal.Count(scene.Words, TextGoal.Words),
                    Foreground = Chrome.SoftText, FontSize = 12, FontStyle = FontStyle.Italic, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 2), Tag = "scene-opening"
                });

            // — Les fiches : les personnages d'abord, puis le reste, chacune sous un libellé unique.
            var characters = new List<BinderItem>();
            var others = new List<BinderItem>();
            foreach (var sheet in project.AllItems())
            {
                if (sheet.Kind != ItemKind.Sheet) continue;
                var root = sheet.RootCategory();
                if (root != null && root.CategoryKey == Project.KeyTrash) continue;
                if (IsCharacter(sheet)) characters.Add(sheet); else others.Add(sheet);
            }
            Comparison<BinderItem> byTitle = delegate(BinderItem a, BinderItem b) { return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase); };
            characters.Sort(byTitle);
            others.Sort(byTitle);
            var seen = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var sheet in characters) { _sheets.Add(sheet); _sheetLabels.Add(Unique(seen, Title(sheet))); }
            foreach (var sheet in others) { _sheets.Add(sheet); _sheetLabels.Add(Unique(seen, Title(sheet) + "  —  " + CategoryName(sheet))); }

            panel.Children.Add(TimelineUi.Label("Point de vue :", scene.Opening.Length > 0 ? 10 : 0));
            _pov = Picker("scene-pov", "Taper un nom…", "La fiche du personnage par les yeux de qui la scène est racontée");
            foreach (var label in _sheetLabels) _pov.Items.Add(label);
            _pov.SelectedIndex = IndexOfSheet(_meta.PovId);
            Suggestions.Attach(_pov, delegate { return _sheetLabels; }, delegate(string chosen) { Select(_pov, _sheetLabels, chosen); }, true, true);
            panel.Children.Add(WithClear(_pov, "Aucun point de vue"));

            // — Le lieu : une fiche (hors personnages), ou un repère, une zone d'une carte.
            foreach (var sheet in others) { _placeTargets.Add(sheet); _placeLabels.Add(_sheetLabels[_sheets.IndexOf(sheet)]); }
            foreach (var place in Timelines.Places(project)) { _placeTargets.Add(place); _placeLabels.Add(Unique(seen, place.Label + "  —  carte")); }
            panel.Children.Add(TimelineUi.Label("Lieu :"));
            _place = Picker("scene-place", "Taper un nom…", "Où la scène se passe : une fiche, ou un repère ou une zone d'une carte");
            foreach (var label in _placeLabels) _place.Items.Add(label);
            _place.SelectedIndex = IndexOfPlace();
            Suggestions.Attach(_place, delegate { return _placeLabels; }, delegate(string chosen) { Select(_place, _placeLabels, chosen); }, true, true);
            panel.Children.Add(WithClear(_place, "Aucun lieu"));

            // — Le moment : écrit comme on veut ; s'il se lit dans le calendrier d'une frise, Marabook le dit.
            panel.Children.Add(TimelineUi.Label("Moment :"));
            _timelines = Timelines.All(project);
            _when = new TextBox { Text = _meta.When ?? "", Tag = "scene-when" };
            ToolTip.SetTip(_when, "Quand la scène se passe. Une date du calendrier d'une frise est reconnue ; tout autre texte (« au crépuscule ») est gardé tel quel.");
            _timeline = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8, 0, 0, 0), Tag = "scene-timeline" };
            ToolTip.SetTip(_timeline, "La frise dont le calendrier lit ce moment");
            foreach (var timeline in _timelines) _timeline.Items.Add(timeline.Title);
            var chosenTimeline = _timelines.FindIndex(delegate(BinderItem t) { return t.Id == _meta.TimelineId; });
            _timeline.SelectedIndex = chosenTimeline >= 0 ? chosenTimeline : (_timelines.Count > 0 ? 0 : -1);
            _timeline.IsVisible = _timelines.Count > 1;
            var whenRow = new Grid { ColumnDefinitions = new ColumnDefinitions(_timelines.Count > 1 ? "3*,2*" : "*,0") };
            Grid.SetColumn(_timeline, 1);
            whenRow.Children.Add(_when);
            whenRow.Children.Add(_timeline);
            panel.Children.Add(whenRow);
            _whenEcho = new TextBlock { FontSize = 11, Foreground = Chrome.SoftText, Margin = new Thickness(2, 3, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = "scene-when-echo" };
            panel.Children.Add(_whenEcho);
            _when.TextChanged += delegate { Echo(); };
            _timeline.SelectionChanged += delegate { Echo(); };
            Echo();

            // — Les personnages présents : des puces ; ceux que le texte nomme sont proposés, jamais imposés.
            panel.Children.Add(TimelineUi.Label("Personnages présents :"));
            _chips = new WrapPanel { Tag = "scene-characters" };
            panel.Children.Add(_chips);
            _character = Picker("scene-character-add", "Ajouter une fiche…", "Tapez quelques lettres du nom : Entrée ou un clic l'ajoute à la scène");
            foreach (var label in _sheetLabels) _character.Items.Add(label);
            _character.SelectionChanged += delegate
            {
                var index = _character.SelectedIndex;
                if (index < 0 || index >= _sheets.Count) return;
                AddCharacter(_sheets[index].Id);
                Avalonia.Threading.Dispatcher.UIThread.Post(delegate { _character.SelectedIndex = -1; _character.Text = ""; });
            };
            Suggestions.Attach(_character, delegate { return _sheetLabels; }, delegate(string chosen) { Select(_character, _sheetLabels, chosen); }, true, true);
            panel.Children.Add(_character);
            panel.Children.Add(TimelineUi.Hint("Tapez quelques lettres d'un nom pour ajouter une fiche."));
            _suggestedLabel = TimelineUi.Hint("Nommés dans le texte de la scène (un clic les ajoute) :");
            panel.Children.Add(_suggestedLabel);
            _suggested = new WrapPanel { Margin = new Thickness(0, 4, 0, 0), Tag = "scene-suggested" };
            panel.Children.Add(_suggested);
            RebuildCharacters();

            panel.Children.Add(TimelineUi.Label("Note :"));
            _note = new TextBox { Text = _meta.Note ?? "", Watermark = "Facultatif — l'enjeu de la scène, en une ligne", Tag = "scene-note" };
            panel.Children.Add(_note);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(cancel);
            var ok = new Button { Content = "Enregistrer", IsDefault = true, MinWidth = 80, Tag = "scene-ok" };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
        }

        private bool IsCharacter(BinderItem sheet)
        {
            var category = _project.TopSheetCategoryOf(sheet);
            return category != null && Achievements.IsCharacterCategory(category.Name);
        }

        private string CategoryName(BinderItem sheet)
        {
            var category = _project.SheetCategoryOf(sheet);
            return category == null ? "fiche" : category.Name;
        }

        private static string Title(BinderItem item)
        {
            var title = (item.Title ?? "").Trim();
            return title.Length == 0 ? "(sans titre)" : title;
        }

        private static string Unique(Dictionary<string, int> seen, string label)
        {
            int times;
            seen[label] = seen.TryGetValue(label, out times) ? times + 1 : 1;
            return seen[label] > 1 ? label + "  (" + seen[label] + ")" : label;
        }

        private static ComboBox Picker(string tag, string placeholder, string tip)
        {
            var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = placeholder, IsEditable = true, Tag = tag };
            ToolTip.SetTip(box, tip);
            return box;
        }

        /// <summary>Le champ et, à sa droite, la croix qui le vide.</summary>
        private static Control WithClear(ComboBox box, string tip)
        {
            var clear = Buttons.Icon("x-bold", tip, Buttons.Compact, Buttons.Look.Calm);
            clear.Margin = new Thickness(6, 0, 0, 0);
            clear.Tag = (box.Tag as string) + "-clear";
            clear.Click += delegate { box.SelectedIndex = -1; box.Text = ""; };
            var row = new DockPanel();
            DockPanel.SetDock(clear, Dock.Right);
            row.Children.Add(clear);
            row.Children.Add(box);
            return row;
        }

        private static void Select(ComboBox box, List<string> labels, string chosen)
        {
            var found = labels.FindIndex(delegate(string label) { return string.Equals(label, chosen, StringComparison.CurrentCultureIgnoreCase); });
            if (found >= 0) box.SelectedIndex = found;
        }

        private int IndexOfSheet(string id)
        {
            return id == null ? -1 : _sheets.FindIndex(delegate(BinderItem sheet) { return sheet.Id == id; });
        }

        private int IndexOfPlace()
        {
            for (var i = 0; i < _placeTargets.Count; i++)
            {
                var sheet = _placeTargets[i] as BinderItem;
                if (sheet != null && sheet.Id == _meta.PlaceSheetId) return i;
                var place = _placeTargets[i] as EventPlace;
                if (place != null && _meta.PlaceShapeId != null && place.ShapeId == _meta.PlaceShapeId && place.Map.Id == _meta.PlaceMapId) return i;
            }
            return -1;
        }

        private BinderItem ChosenTimeline()
        {
            var index = _timeline.SelectedIndex;
            return index >= 0 && index < _timelines.Count ? _timelines[index] : null;
        }

        /// <summary>Ce que Marabook lit du moment — ou qu'il le garde tel quel.</summary>
        private void Echo()
        {
            var text = (_when.Text ?? "").Trim();
            var timeline = ChosenTimeline();
            var system = timeline == null ? null : Timelines.SystemOf(_project, timeline);
            if (system == null) { _whenEcho.Text = text.Length == 0 ? "" : "Gardé tel quel (le projet n'a pas de frise pour le lire comme une date)."; return; }
            _when.Watermark = "ex. " + system.Example() + ", ou un texte libre";
            if (text.Length == 0) { _whenEcho.Text = ""; return; }
            TimePoint point;
            _whenEcho.Text = system.TryParse(text, out point)
                ? "Lu dans le calendrier de « " + timeline.Title + " » : " + system.Format(point)
                : "Pas une date de « " + timeline.Title + " » : gardé tel quel, comme un texte.";
        }

        private void AddCharacter(string id)
        {
            if (_meta.CharacterIds.Contains(id)) return;
            _meta.CharacterIds.Add(id);
            RebuildCharacters();
        }

        private void RebuildCharacters()
        {
            _chips.Children.Clear();
            foreach (var id in _meta.CharacterIds.ToArray())
            {
                var captured = id;
                var chip = FieldEditors.Chip(Scenes.SheetTitle(_project, id), delegate { _meta.CharacterIds.Remove(captured); RebuildCharacters(); });
                chip.Tag = "scene-character-chip";
                _chips.Children.Add(chip);
            }
            _chips.IsVisible = _meta.CharacterIds.Count > 0;

            _suggested.Children.Clear();
            foreach (var sheet in Scenes.DetectedCharacters(_project, _item, _scene))
            {
                if (_meta.CharacterIds.Contains(sheet.Id) || !IsCharacter(sheet)) continue;
                var captured = sheet;
                var add = new Button
                {
                    Content = "+ " + Title(sheet), FontSize = 11, Padding = new Thickness(8, 1, 8, 2), Margin = new Thickness(0, 0, 6, 6), MinHeight = 0, CornerRadius = new CornerRadius(10),
                    Tag = "scene-suggestion"
                };
                add.Click += delegate { AddCharacter(captured.Id); };
                _suggested.Children.Add(add);
            }
            _suggestedLabel.IsVisible = _suggested.Children.Count > 0;
            _suggested.IsVisible = _suggested.Children.Count > 0;
        }

        private void Accept()
        {
            var pov = _pov.SelectedIndex;
            _meta.PovId = pov >= 0 && pov < _sheets.Count ? _sheets[pov].Id : null;
            _meta.PlaceSheetId = null;
            _meta.PlaceMapId = null;
            _meta.PlaceShapeId = null;
            var place = _place.SelectedIndex;
            if (place >= 0 && place < _placeTargets.Count)
            {
                var sheet = _placeTargets[place] as BinderItem;
                var onMap = _placeTargets[place] as EventPlace;
                if (sheet != null) _meta.PlaceSheetId = sheet.Id;
                else if (onMap != null) { _meta.PlaceMapId = onMap.Map.Id; _meta.PlaceShapeId = onMap.ShapeId; }
            }
            _meta.When = (_when.Text ?? "").Trim();
            var timeline = ChosenTimeline();
            // La frise n'est notée que si l'auteur a eu à choisir : sinon la première du projet vaut, même changée demain.
            _meta.TimelineId = _meta.When.Length > 0 && timeline != null && _timelines.Count > 1 ? timeline.Id : null;
            _meta.Note = (_note.Text ?? "").Trim();
            _accepted = true;
            Close();
        }

        /// <summary>Ouvre la fiche de la scène ; rend ce qui a été enregistré, ou null (annulé, rien de changé).</summary>
        public static async Task<SceneMeta> Ask(Window owner, Project project, BinderItem item, Scene scene, int count)
        {
            var dialog = new SceneDialog(owner, project, item, scene, count);
            LastForProbe = dialog;
            await Dialogs.ShowModal(dialog, owner);
            LastForProbe = null;
            return dialog._accepted && !dialog._meta.SameAs(scene.Meta) ? dialog._meta : null;
        }

        // — Sonde.
        internal static SceneDialog LastForProbe;
        internal void FillForProbe(string pov, string place, string when, string note)
        {
            if (pov != null) Select(_pov, _sheetLabels, pov);
            if (place != null) _place.SelectedIndex = _placeLabels.FindIndex(delegate(string label) { return label.StartsWith(place, StringComparison.CurrentCultureIgnoreCase); });
            if (when != null) _when.Text = when;
            if (note != null) _note.Text = note;
        }
        internal int SuggestedForProbe { get { return _suggested.Children.Count; } }
        internal void AcceptSuggestionsForProbe()
        {
            while (_suggested.Children.Count > 0) ((Button)_suggested.Children[0]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        }
        internal string WhenEchoForProbe { get { return _whenEcho.Text ?? ""; } }
        internal int CharactersForProbe { get { return _meta.CharacterIds.Count; } }
        internal void AcceptForProbe() { Accept(); }
    }
}

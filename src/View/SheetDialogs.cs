using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>L'éditeur de modèles de fiches : les modèles à gauche ; à
    /// droite, le nom puis deux onglets (batch 42) — « Sections » (Informations
    /// toujours là, les sections nommées du modèle, le paper Relations en case
    /// à cocher) et « Champs » (nom, nature, et la section de chaque champ par
    /// un menu déroulant). Travaille sur des clones ; rend la liste neuve à
    /// Valider, null à Annuler. Les ids de champ sont stables : renommer garde
    /// les valeurs des fiches, supprimer les laisse dormantes.</summary>
    public class TemplatesDialog : Window
    {
        private const string DefaultSectionLabel = "Informations";

        private readonly List<SheetTemplate> _templates;
        private readonly ListBox _list;
        private readonly TextBox _nameBox;
        private readonly StackPanel _sectionsPanel, _fieldsPanel;
        private readonly CheckBox _relationsCheck;
        // Le radar (b47 bis) : activé par modèle, ses axes et son échelle.
        private readonly CheckBox _radarCheck;
        private readonly TextBox _radarName;
        private readonly SpinnerField _radarMax;
        private readonly StackPanel _axesPanel;
        private readonly Button _addAxis;
        private SheetTemplate _current;
        private ListBoxItem _currentEntry; // list row of _current — NOT SelectedItem,
                                           // which already points to the next row
                                           // when SelectionChanged commits the name
        private bool _accepted, _syncing;

        private TemplatesDialog(Window owner, List<SheetTemplate> source)
        {
            _templates = new List<SheetTemplate>();
            foreach (var template in source) _templates.Add(template.Clone());

            Title = "Modèles de fiches";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 920;
            Height = 600;
            MinWidth = 720;
            MinHeight = 440;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new Grid { Margin = new Thickness(14) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // le filet vertical
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // --- left: template list ---
            var left = new DockPanel();
            var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            listButtons.Children.Add(ListButton("plus-bold", "Nouveau modèle", NewTemplate));
            listButtons.Children.Add(ListButton("copy-simple-bold", "Dupliquer le modèle", DuplicateTemplate));
            listButtons.Children.Add(ListButton("trash", "Supprimer le modèle", DeleteTemplate));
            DockPanel.SetDock(listButtons, Dock.Bottom);
            left.Children.Add(listButtons);
            _list = new ListBox();
            _list.SelectionChanged += delegate { CommitName(); ShowTemplate(SelectedTemplate()); };
            left.Children.Add(_list);
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            // Un filet vertical sur les deux rangées : il sépare aussi le bas
            // (boutons de la liste | Valider / Annuler).
            var divider = new Border { Width = 1, Background = Chrome.Border, Margin = new Thickness(14, 0, 14, 0) };
            Grid.SetColumn(divider, 1);
            Grid.SetRowSpan(divider, 2);
            root.Children.Add(divider);

            // --- right: name + tabs ---
            var right = new DockPanel();
            var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var nameLabel = new TextBlock
            {
                Text = "Nom du modèle",
                Width = 110,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(nameLabel, Dock.Left);
            nameRow.Children.Add(nameLabel);
            _nameBox = new TextBox();
            nameRow.Children.Add(_nameBox);
            DockPanel.SetDock(nameRow, Dock.Top);
            right.Children.Add(nameRow);

            var tabs = new TabControl();

            // — Sections : Informations (fixe), les sections nommées, Relations.
            var sectionsDock = new DockPanel();
            var sectionsFoot = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var addSection = Buttons.IconText("plus-bold", "Ajouter une section", null, Buttons.Bar, Buttons.Look.Outline);
            addSection.HorizontalAlignment = HorizontalAlignment.Left;
            addSection.Click += delegate { AddSection(); };
            sectionsFoot.Children.Add(addSection);
            _relationsCheck = new CheckBox
            {
                Content = "Relations — le paper des liens entre fiches (frère, mentor, rivale…)",
                Margin = new Thickness(0, 12, 0, 0)
            };
            _relationsCheck.Checked += delegate { if (_current != null && !_syncing) _current.Relations = true; };
            _relationsCheck.Unchecked += delegate { if (_current != null && !_syncing) _current.Relations = false; };
            sectionsFoot.Children.Add(_relationsCheck);
            DockPanel.SetDock(sectionsFoot, Dock.Bottom);
            sectionsDock.Children.Add(sectionsFoot);
            _sectionsPanel = new StackPanel();
            sectionsDock.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _sectionsPanel
            });
            tabs.Items.Add(new TabItem { Header = "Sections", Content = new Border { Padding = new Thickness(4, 8, 4, 4), Child = sectionsDock } });

            // — Champs : nom, nature, section.
            var fieldsDock = new DockPanel();
            var addField = Buttons.IconText("plus-bold", "Ajouter un champ", null, Buttons.Bar, Buttons.Look.Outline);
            addField.HorizontalAlignment = HorizontalAlignment.Left;
            addField.Margin = new Thickness(0, 8, 0, 0);
            addField.Click += delegate { AddField(); };
            DockPanel.SetDock(addField, Dock.Bottom);
            fieldsDock.Children.Add(addField);
            var fieldsHead = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            fieldsHead.ColumnDefinitions.Add(new ColumnDefinition());
            fieldsHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
            fieldsHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(166) });
            fieldsHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            fieldsHead.Children.Add(HeadLabel("Nom du champ", 0));
            fieldsHead.Children.Add(HeadLabel("Nature", 1));
            fieldsHead.Children.Add(HeadLabel("Section", 2));
            DockPanel.SetDock(fieldsHead, Dock.Top);
            fieldsDock.Children.Add(fieldsHead);
            _fieldsPanel = new StackPanel();
            fieldsDock.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _fieldsPanel
            });
            tabs.Items.Add(new TabItem { Header = "Champs", Content = new Border { Padding = new Thickness(4, 8, 4, 4), Child = fieldsDock } });

            // — Radar (b47 bis) : optionnel par modèle, désactivé par défaut ;
            //   activé, la fiche gagne un onglet « Radar ». Axes et échelle libres.
            var radarDock = new DockPanel();
            var radarHead = new StackPanel();
            _radarCheck = new CheckBox
            {
                Content = "Activer le radar sur ce modèle — la fiche gagne un onglet « Radar » (toile à un axe par ligne ci-dessous)",
                Margin = new Thickness(0, 0, 0, 10)
            };
            _radarCheck.Checked += delegate
            {
                if (_current == null || _syncing) return;
                _current.Radar = true;
                if (_current.RadarAxes.Count == 0)
                    foreach (var name in SheetTemplate.DefaultRadarAxes) _current.RadarAxes.Add(new RadarAxis { Name = name });
                RebuildAxes();
            };
            _radarCheck.Unchecked += delegate { if (_current != null && !_syncing) { _current.Radar = false; RebuildAxes(); } };
            radarHead.Children.Add(_radarCheck);
            var radarNameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            radarNameRow.Children.Add(new TextBlock { Text = "Nom du radar", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _radarName = new TextBox { Width = 220, ToolTip = "Le nom de l'onglet et de la section : « Radar », « Traits », « Aptitudes »…" };
            _radarName.TextChanged += delegate { if (_current != null && !_syncing) _current.RadarName = _radarName.Text; };
            radarNameRow.Children.Add(_radarName);
            radarHead.Children.Add(radarNameRow);
            var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            scaleRow.Children.Add(new TextBlock { Text = "Échelle : de 0 à", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            _radarMax = new SpinnerField(5, SheetTemplate.RadarMaxFloor, SheetTemplate.RadarMaxCeiling, 1, "Le maximum de chaque axe (3 à 10)");
            _radarMax.ValueChanged += delegate(double value) { if (_current != null && !_syncing) _current.RadarMax = (int)value; };
            scaleRow.Children.Add(_radarMax);
            radarHead.Children.Add(scaleRow);
            radarHead.Children.Add(new TextBlock { Text = "Les axes (trois au moins pour une toile)", Foreground = Chrome.FaintText, FontSize = 11, Margin = new Thickness(2, 0, 0, 4) });
            DockPanel.SetDock(radarHead, Dock.Top);
            radarDock.Children.Add(radarHead);
            _addAxis = Buttons.IconText("plus-bold", "Ajouter un axe", null, Buttons.Bar, Buttons.Look.Outline);
            _addAxis.HorizontalAlignment = HorizontalAlignment.Left;
            _addAxis.Margin = new Thickness(0, 8, 0, 0);
            _addAxis.Click += delegate
            {
                if (_current == null) return;
                _current.RadarAxes.Add(new RadarAxis { Name = "Axe " + (_current.RadarAxes.Count + 1) });
                RebuildAxes();
            };
            DockPanel.SetDock(_addAxis, Dock.Bottom);
            radarDock.Children.Add(_addAxis);
            _axesPanel = new StackPanel();
            radarDock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _axesPanel });
            tabs.Items.Add(new TabItem { Header = "Radar", Content = new Border { Padding = new Thickness(4, 8, 4, 4), Child = radarDock } });
            right.Children.Add(tabs);
            Grid.SetColumn(right, 2);
            root.Children.Add(right);

            // --- bottom ---
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 90 };
            ok.Click += delegate { CommitName(); _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetColumn(buttons, 2);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);

            Content = root;
            FillList(null);
        }

        public static List<SheetTemplate> Show(Window owner, List<SheetTemplate> source)
        {
            var dialog = new TemplatesDialog(owner, source);
            Dialogs.ShowModal(dialog);
            return dialog._accepted ? dialog._templates : null;
        }

        private static TextBlock HeadLabel(string text, int column)
        {
            var label = new TextBlock { Text = text, Foreground = Chrome.FaintText, FontSize = 11, Margin = new Thickness(2, 0, 0, 0) };
            Grid.SetColumn(label, column);
            return label;
        }

        /// <summary>Les boutons de la liste des modèles : icône seule
        /// (nouveau, dupliquer, supprimer), infobulle obligatoire.</summary>
        private Button ListButton(string icon, string tooltip, Action onClick)
        {
            var button = Buttons.Icon(icon, tooltip, Buttons.Bar, Buttons.Look.Outline);
            button.Margin = new Thickness(0, 0, 6, 0);
            button.Click += delegate { onClick(); };
            return button;
        }

        private SheetTemplate SelectedTemplate()
        {
            var entry = _list.SelectedItem as ListBoxItem;
            if (entry == null) return null;
            foreach (var template in _templates)
                if (template.Id == (string)entry.Tag) return template;
            return null;
        }

        private void FillList(string selectId)
        {
            _list.Items.Clear();
            foreach (var template in _templates)
            {
                var entry = new ListBoxItem { Content = template.Name, Tag = template.Id };
                _list.Items.Add(entry);
                if (selectId != null && template.Id == selectId) _list.SelectedItem = entry;
            }
            if (_list.SelectedItem == null && _list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void ShowTemplate(SheetTemplate template)
        {
            _current = template;
            _currentEntry = _list.SelectedItem as ListBoxItem;
            _syncing = true;
            _nameBox.Text = template == null ? "" : template.Name;
            _relationsCheck.IsChecked = template != null && template.Relations;
            _relationsCheck.IsEnabled = template != null;
            _radarCheck.IsChecked = template != null && template.Radar;
            _radarCheck.IsEnabled = template != null;
            _radarMax.Value = template == null ? 5 : template.RadarMax;
            _radarName.Text = template == null ? "" : template.RadarName;
            _syncing = false;
            RebuildSections();
            RebuildFields();
            RebuildAxes();
        }

        // ------------------------------------------------------------ radar (b47 bis)

        private void RebuildAxes()
        {
            _axesPanel.Children.Clear();
            var on = _current != null && _current.Radar;
            _addAxis.IsEnabled = on;
            _radarMax.IsEnabled = on;
            _radarName.IsEnabled = on;
            if (_current == null) return;
            foreach (var axis in _current.RadarAxes)
            {
                var axisRef = axis;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4), IsEnabled = on };
                var remove = Buttons.Icon("trash", "Retirer cet axe (les fiches perdent sa valeur)", Buttons.Compact, Buttons.Look.Calm);
                remove.Margin = new Thickness(6, 0, 0, 0);
                remove.Click += delegate { _current.RadarAxes.Remove(axisRef); RebuildAxes(); };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                var nameBox = new TextBox { Text = axis.Name, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
                nameBox.TextChanged += delegate { axisRef.Name = nameBox.Text; };
                row.Children.Add(nameBox);
                _axesPanel.Children.Add(row);
            }
            if (_current.RadarAxes.Count == 0)
                _axesPanel.Children.Add(new TextBlock { Text = on ? "Aucun axe — ajoutez-en trois au moins." : "Radar désactivé sur ce modèle.", Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(2, 4, 0, 0) });
        }

        private void CommitName()
        {
            if (_current == null || _syncing) return;
            var name = _nameBox.Text.Trim();
            if (name.Length > 0) _current.Name = name;
            if (_currentEntry != null) _currentEntry.Content = _current.Name;
        }

        // ------------------------------------------------------------ sections

        private void RebuildSections()
        {
            _sectionsPanel.Children.Clear();
            if (_current == null) return;
            var fixedRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            fixedRow.Children.Add(new TextBlock
            {
                Text = DefaultSectionLabel + "  —  la section par défaut, toujours présente",
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 4, 0, 4)
            });
            _sectionsPanel.Children.Add(fixedRow);
            foreach (var section in _current.Sections)
                _sectionsPanel.Children.Add(BuildSectionRow(section));
        }

        private UIElement BuildSectionRow(string section)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var remove = Buttons.Icon("trash", "Retirer cette section (ses champs rejoignent Informations)", Buttons.Compact, Buttons.Look.Calm);
            remove.Margin = new Thickness(6, 0, 0, 0);
            remove.Click += delegate
            {
                var index = IndexOfSection(section);
                if (index < 0) return;
                var name = _current.Sections[index];
                _current.Sections.RemoveAt(index);
                foreach (var field in _current.Fields)
                    if (string.Equals(field.Group, name, StringComparison.CurrentCultureIgnoreCase)) field.Group = "";
                RebuildSections();
                RebuildFields();
            };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            var nameBox = new TextBox { Text = section, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 220 };
            var previous = section;
            nameBox.TextChanged += delegate
            {
                var next = nameBox.Text.Trim();
                if (next.Length == 0 || string.Equals(next, DefaultSectionLabel, StringComparison.CurrentCultureIgnoreCase)) return;
                var index = IndexOfSection(previous);
                if (index < 0) return;
                _current.Sections[index] = next;
                foreach (var field in _current.Fields)
                    if (string.Equals(field.Group, previous, StringComparison.CurrentCultureIgnoreCase)) field.Group = next;
                previous = next;
            };
            nameBox.LostFocus += delegate { RebuildFields(); }; // les menus déroulants des champs suivent
            row.Children.Add(nameBox);
            return row;
        }

        private int IndexOfSection(string name)
        {
            for (var i = 0; i < _current.Sections.Count; i++)
                if (string.Equals(_current.Sections[i], name, StringComparison.CurrentCultureIgnoreCase)) return i;
            return -1;
        }

        private void AddSection()
        {
            if (_current == null) return;
            var name = "Nouvelle section";
            var n = 2;
            while (_current.HasSection(name)) name = "Nouvelle section " + n++;
            _current.Sections.Add(name);
            RebuildSections();
            RebuildFields();
        }

        // ------------------------------------------------------------ champs

        /// <summary>Les champs, regroupés par section (b42 bis) : un titre de
        /// section et un filet devant chaque groupe — Informations d'abord,
        /// puis les sections dans l'ordre du modèle. L'ordre des champs
        /// dans une section reste celui du modèle.</summary>
        private void RebuildFields()
        {
            _fieldsPanel.Children.Clear();
            if (_current == null) return;
            var sections = new List<string> { "" };
            sections.AddRange(_current.Sections);
            foreach (var section in sections)
            {
                var any = false;
                foreach (var field in _current.Fields)
                {
                    if (!SameSection(field.Group, section)) continue;
                    if (!any)
                    {
                        _fieldsPanel.Children.Add(SectionDivider(section.Length == 0 ? DefaultSectionLabel : section));
                        any = true;
                    }
                    _fieldsPanel.Children.Add(BuildFieldRow(field));
                }
            }
            // Un champ dont la section n'existe plus (modèle édité ailleurs) :
            // montré quand même, sous Informations.
            var orphans = false;
            foreach (var field in _current.Fields)
            {
                if (field.Group.Length == 0 || IndexOfSection(field.Group) >= 0) continue;
                if (!orphans) { _fieldsPanel.Children.Add(SectionDivider("Section inconnue « " + field.Group + " »")); orphans = true; }
                _fieldsPanel.Children.Add(BuildFieldRow(field));
            }
        }

        private static bool SameSection(string group, string section)
        {
            return string.Equals(group ?? "", section, StringComparison.CurrentCultureIgnoreCase);
        }

        private static UIElement SectionDivider(string title)
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 6) };
            var label = new TextBlock
            {
                Text = title,
                Foreground = Chrome.FaintText,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 10, 0)
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);
            row.Children.Add(new Border { Height = 1, Background = Chrome.Border, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private UIElement BuildFieldRow(SheetField field)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(166) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });

            var nameBox = new TextBox { Text = field.Name, Margin = new Thickness(0, 0, 6, 0) };
            nameBox.TextChanged += delegate { field.Name = nameBox.Text; };
            Grid.SetColumn(nameBox, 0);
            row.Children.Add(nameBox);

            // La nature (b47 bis) : les huit de FieldKinds ; un « Choix »
            // déplie ses options sous la rangée.
            var kindCombo = new ComboBox { Margin = new Thickness(0, 0, 6, 0), ToolTip = "Comment ce champ se saisit et se lit sur la fiche" };
            foreach (var kind in FieldKinds.All) kindCombo.Items.Add(FieldKinds.Label(kind));
            kindCombo.SelectedIndex = Array.IndexOf(FieldKinds.All, FieldKinds.Normalize(field.Kind));
            kindCombo.SelectionChanged += delegate
            {
                if (kindCombo.SelectedIndex < 0) return;
                var chosen = FieldKinds.All[kindCombo.SelectedIndex];
                if (chosen == field.Kind) return;
                field.Kind = chosen;
                Dispatcher.BeginInvoke((Action)RebuildFields); // la rangée d'options apparaît ou disparaît
            };
            Grid.SetColumn(kindCombo, 1);
            row.Children.Add(kindCombo);

            // La section du champ : Informations, ou l'une des sections du modèle.
            var sectionCombo = new ComboBox { Margin = new Thickness(0, 0, 6, 0), ToolTip = "La section (le paper) où ce champ s'affiche sur la fiche" };
            sectionCombo.Items.Add(DefaultSectionLabel);
            foreach (var section in _current.Sections) sectionCombo.Items.Add(section);
            var index = field.Group.Length == 0 ? -1 : IndexOfSection(field.Group);
            sectionCombo.SelectedIndex = index < 0 ? 0 : index + 1;
            sectionCombo.SelectionChanged += delegate
            {
                field.Group = sectionCombo.SelectedIndex <= 0 ? "" : _current.Sections[sectionCombo.SelectedIndex - 1];
                // Le champ rejoint la FIN de sa nouvelle section, dans la
                // liste modèle comme à l'écran (b43). Reconstruction différée :
                // on ne détruit pas le ComboBox pendant son propre événement.
                MoveFieldToSectionEnd(field);
                Dispatcher.BeginInvoke((Action)RebuildFields);
            };
            Grid.SetColumn(sectionCombo, 2);
            row.Children.Add(sectionCombo);

            var remove = Buttons.Icon("trash", "Supprimer ce champ (les fiches gardent leur valeur, dormante)", Buttons.Compact, Buttons.Look.Calm);
            remove.Click += delegate
            {
                _current.Fields.Remove(field);
                RebuildFields();
            };
            Grid.SetColumn(remove, 3);
            row.Children.Add(remove);
            if (FieldKinds.Normalize(field.Kind) != FieldKinds.Choice) return row;

            // Les options du choix, sous la rangée.
            var stack = new StackPanel();
            stack.Children.Add(row);
            var optionsRow = new DockPanel { Margin = new Thickness(14, 0, 40, 6) };
            var label = new TextBlock { Text = "Options :", Foreground = Chrome.FaintText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(label, Dock.Left);
            optionsRow.Children.Add(label);
            var optionsBox = new TextBox { Text = FieldKinds.JoinOptions(field.Options), ToolTip = "Les valeurs proposées, séparées par des virgules : « vivant, mort, disparu »" };
            optionsBox.TextChanged += delegate { field.Options = FieldKinds.ListItems(optionsBox.Text); };
            optionsRow.Children.Add(optionsBox);
            stack.Children.Add(optionsRow);
            return stack;
        }

        /// <summary>Replace le champ après le dernier champ de sa section —
        /// l'ordre persisté suit ce que l'éditeur affiche.</summary>
        private void MoveFieldToSectionEnd(SheetField field)
        {
            if (_current == null || !_current.Fields.Remove(field)) return;
            var insert = _current.Fields.Count;
            for (var i = _current.Fields.Count - 1; i >= 0; i--)
                if (SameSection(_current.Fields[i].Group, field.Group)) { insert = i + 1; break; }
            _current.Fields.Insert(insert, field);
        }

        private void AddField()
        {
            if (_current == null) return;
            _current.Fields.Add(new SheetField { Name = "Champ" });
            RebuildFields();
        }

        // ------------------------------------------------------------ modèles

        private void NewTemplate()
        {
            CommitName();
            var template = new SheetTemplate { Name = "Nouveau modèle" };
            template.Fields.Add(new SheetField { Name = "Description", Kind = "multiline" });
            _templates.Add(template);
            FillList(template.Id);
        }

        private void DuplicateTemplate()
        {
            CommitName();
            var source = SelectedTemplate();
            if (source == null) return;
            var copy = source.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = source.Name + " (copie)";
            // Duplicated fields keep their ids: instances migrated from one
            // template to its copy keep their values.
            _templates.Insert(_templates.IndexOf(source) + 1, copy);
            FillList(copy.Id);
        }

        private void DeleteTemplate()
        {
            var template = SelectedTemplate();
            if (template == null) return;
            var answer = MessageDialog.Show(this,
                "Supprimer le modèle « " + template.Name + " » ?\n" +
                "Les fiches existantes garderont leurs valeurs (champs libres uniquement).",
                "Modèles", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            _current = null;
            _templates.Remove(template);
            FillList(null);
        }
    }

    /// <summary>Nouvelle fiche (batch 31) : titre + CATÉGORIE — le modèle
    /// suit (le modèle de base de la catégorie choisie).</summary>
    public class NewSheetDialog : Window
    {
        private readonly TextBox _titleBox;
        private readonly ComboBox _categoryCombo;
        private bool _accepted;

        private NewSheetDialog(Window owner, Project project)
        {
            Title = "Nouvelle fiche";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 320 };
            panel.Children.Add(new TextBlock { Text = "Titre :", Foreground = Chrome.Ink, Margin = new Thickness(0, 0, 0, 4) });
            _titleBox = new TextBox { Text = "Nouvelle fiche" };
            _titleBox.SelectAll();
            panel.Children.Add(_titleBox);

            panel.Children.Add(new TextBlock { Text = "Catégorie :", Foreground = Chrome.Ink, Margin = new Thickness(0, 10, 0, 4) });
            _categoryCombo = new ComboBox();
            foreach (var category in project.SheetCategories)
            {
                var template = project.FindTemplate(category.TemplateId);
                _categoryCombo.Items.Add(new ComboBoxItem
                {
                    Content = category.Name
                        + (template != null ? "  ·  modèle " + template.Name : ""),
                    Tag = category.Id
                });
            }
            if (_categoryCombo.Items.Count > 0)
                _categoryCombo.Items.Add(new Separator());
            _categoryCombo.Items.Add(new ComboBoxItem
            {
                Content = "(sans catégorie ni modèle)",
                Tag = null
            });
            _categoryCombo.SelectedIndex = 0;
            panel.Children.Add(_categoryCombo);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Créer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += delegate { _titleBox.Focus(); };
        }

        public static bool Ask(Window owner, Project project,
            out string title, out string categoryId)
        {
            var dialog = new NewSheetDialog(owner, project);
            Dialogs.ShowModal(dialog);
            title = dialog._titleBox.Text.Trim();
            var chosen = dialog._categoryCombo.SelectedItem as ComboBoxItem;
            categoryId = chosen == null ? null : chosen.Tag as string;
            if (!dialog._accepted || title.Length == 0) { title = null; return false; }
            return true;
        }
    }

    /// <summary>Insert a [[link]]: pick an existing item title or type a new one.</summary>
    public class LinkDialog : Window
    {
        private readonly ComboBox _combo;
        private bool _accepted;

        private LinkDialog(Window owner, List<string> titles)
        {
            Title = "Lien vers une fiche";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 320 };
            panel.Children.Add(new TextBlock
            {
                Text = "Cible du lien (existante ou à créer plus tard) :",
                Foreground = Chrome.Ink,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _combo = new ComboBox { IsEditable = true };
            foreach (var title in titles) _combo.Items.Add(title);
            panel.Children.Add(_combo);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Insérer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += delegate { _combo.Focus(); };
        }

        public static string Ask(Window owner, List<string> titles)
        {
            var dialog = new LinkDialog(owner, titles);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return null;
            var text = (dialog._combo.Text ?? "").Trim();
            return text.Length == 0 ? null : text;
        }
    }
}

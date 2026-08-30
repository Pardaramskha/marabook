using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Sheet template editor: templates on the left, fields (name +
    /// kind, editable in place) on the right. Works on clones; returns the new
    /// list on OK, null on cancel. Field ids are stable, so renames keep every
    /// instance's values and deletions leave them dormant.</summary>
    public class TemplatesDialog : Window
    {
        private readonly List<SheetTemplate> _templates;
        private readonly ListBox _list;
        private readonly TextBox _nameBox;
        private readonly StackPanel _fieldsPanel;
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
            Width = 640;
            Height = 480;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new Grid { Margin = new Thickness(14) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // --- left: template list ---
            var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
            var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            listButtons.Children.Add(SmallButton("Nouveau", NewTemplate));
            listButtons.Children.Add(SmallButton("Dupliquer", DuplicateTemplate));
            listButtons.Children.Add(SmallButton("Supprimer", DeleteTemplate));
            DockPanel.SetDock(listButtons, Dock.Bottom);
            left.Children.Add(listButtons);
            _list = new ListBox();
            _list.SelectionChanged += delegate { CommitName(); ShowTemplate(SelectedTemplate()); };
            left.Children.Add(_list);
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            // --- right: name + fields ---
            var right = new DockPanel();
            var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var nameLabel = new TextBlock
            {
                Text = "Nom",
                Width = 60,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(nameLabel, Dock.Left);
            nameRow.Children.Add(nameLabel);
            _nameBox = new TextBox();
            nameRow.Children.Add(_nameBox);
            DockPanel.SetDock(nameRow, Dock.Top);
            right.Children.Add(nameRow);

            var addField = new Button
            {
                Content = Icons.Label("plus-bold", "Ajouter un champ", 11, Chrome.Ink),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };
            addField.Click += delegate { AddField(); };
            DockPanel.SetDock(addField, Dock.Bottom);
            right.Children.Add(addField);

            _fieldsPanel = new StackPanel();
            right.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _fieldsPanel
            });
            Grid.SetColumn(right, 1);
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
            Grid.SetColumn(buttons, 1);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);

            Content = root;
            FillList(null);
        }

        public static List<SheetTemplate> Show(Window owner, List<SheetTemplate> source)
        {
            var dialog = new TemplatesDialog(owner, source);
            dialog.ShowDialog();
            return dialog._accepted ? dialog._templates : null;
        }

        private Button SmallButton(string label, Action onClick)
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) };
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
            _syncing = false;
            RebuildFields();
        }

        private void CommitName()
        {
            if (_current == null || _syncing) return;
            var name = _nameBox.Text.Trim();
            if (name.Length > 0) _current.Name = name;
            if (_currentEntry != null) _currentEntry.Content = _current.Name;
        }

        private void RebuildFields()
        {
            _fieldsPanel.Children.Clear();
            if (_current == null) return;
            foreach (var field in _current.Fields)
                _fieldsPanel.Children.Add(BuildFieldRow(field));
        }

        private UIElement BuildFieldRow(SheetField field)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            var remove = new Button { Content = "✕", Width = 28, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(remove, Dock.Right);
            remove.Click += delegate
            {
                _current.Fields.Remove(field);
                RebuildFields();
            };
            row.Children.Add(remove);

            var kindCombo = new ComboBox { Width = 110, Margin = new Thickness(6, 0, 0, 0) };
            kindCombo.Items.Add("Texte court");
            kindCombo.Items.Add("Texte long");
            kindCombo.SelectedIndex = field.Kind == "multiline" ? 1 : 0;
            kindCombo.SelectionChanged += delegate
            {
                field.Kind = kindCombo.SelectedIndex == 1 ? "multiline" : "text";
            };
            DockPanel.SetDock(kindCombo, Dock.Right);
            row.Children.Add(kindCombo);

            // Groupe d'affichage (batch 31) : les champs d'un même groupe
            // s'affichent sous un intertitre (« Infos », « Physique »…).
            var groupBox = new TextBox
            {
                Text = field.Group,
                Width = 90,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Groupe d'affichage (ex. Infos, Physique) — vide : aucun"
            };
            groupBox.TextChanged += delegate { field.Group = groupBox.Text.Trim(); };
            DockPanel.SetDock(groupBox, Dock.Right);
            row.Children.Add(groupBox);

            var nameBox = new TextBox { Text = field.Name };
            nameBox.TextChanged += delegate { field.Name = nameBox.Text; };
            row.Children.Add(nameBox);
            return row;
        }

        private void AddField()
        {
            if (_current == null) return;
            _current.Fields.Add(new SheetField { Name = "Champ" });
            RebuildFields();
        }

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
            var answer = MessageBox.Show(this,
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
            dialog.ShowDialog();
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
            dialog.ShowDialog();
            if (!dialog._accepted) return null;
            var text = (dialog._combo.Text ?? "").Trim();
            return text.Length == 0 ? null : text;
        }
    }
}

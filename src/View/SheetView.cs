using System;
using System.Windows;
using System.Windows.Controls;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Wiki-like sheet display: the template's typed fields on top,
    /// the free key/value list, then the rich body (a full EditorView, so
    /// styles, footnotes, search and [[links]] all work inside sheets too).</summary>
    public class SheetView : DockPanel
    {
        private readonly StackPanel _fieldsPanel;
        private readonly StackPanel _infoPanel;
        private readonly TextBlock _templateLabel;
        private readonly EditorView _body;
        private readonly ScrollViewer _topScroll;

        private BinderItem _item;
        private SheetTemplate _template;
        private bool _loading;

        public event Action Edited;
        public event Action<string> LinkClicked;

        public SheetView()
        {
            var top = new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 12, 24, 12)
            };
            SetDock(top, Dock.Top);

            var stack = new StackPanel();
            _templateLabel = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(_templateLabel);

            _fieldsPanel = new StackPanel();
            stack.Children.Add(_fieldsPanel);

            stack.Children.Add(new TextBlock
            {
                Text = "Informations libres",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 4)
            });
            _infoPanel = new StackPanel();
            stack.Children.Add(_infoPanel);

            var addInfo = new Button
            {
                Content = "+ Ajouter une information",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 0)
            };
            addInfo.Click += delegate { AddInfoEntry(); };
            stack.Children.Add(addInfo);

            _topScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 300,
                Content = stack
            };
            top.Child = _topScroll;
            Children.Add(top);

            _body = new EditorView();
            _body.Edited += delegate { NotifyEdited(); };
            _body.LinkClicked += delegate(string title)
            {
                var handler = LinkClicked;
                if (handler != null) handler(title);
            };
            Children.Add(_body); // fills the remaining space
        }

        public bool HasItem { get { return _item != null; } }

        public void SetStyleSheet(StyleSheet styles)
        {
            _body.SetStyleSheet(styles);
        }

        public void LoadItem(BinderItem item, SheetTemplate template)
        {
            _item = item;
            _template = template;
            _loading = true;
            _templateLabel.Text = template == null
                ? "Fiche (modèle introuvable — champs libres uniquement)"
                : "Fiche — " + template.Name;
            RebuildFields();
            RebuildInfo();
            _loading = false;
            _body.LoadItem(item);
        }

        public void Commit()
        {
            if (_item == null) return;
            _body.Commit(); // fields and free info write into the item as they change
        }

        public void Clear()
        {
            _item = null;
            _template = null;
            _body.Clear();
        }

        public void ReloadBody()
        {
            _body.Reload();
        }

        public string BodyPlainText()
        {
            return _body.PlainText();
        }

        public void ShowSearch() { _body.ShowSearch(); }
        public void InsertFootnote() { _body.InsertFootnote(); }
        public void InsertWikiLink(string title) { _body.InsertWikiLink(title); }

        private void NotifyEdited()
        {
            if (_loading) return;
            var handler = Edited;
            if (handler != null) handler();
        }

        // ------------------------------------------------------- fields

        private void RebuildFields()
        {
            _fieldsPanel.Children.Clear();
            if (_template == null || _item == null) return;
            foreach (var field in _template.Fields)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var label = new TextBlock
                {
                    Text = field.Name,
                    Width = 110,
                    Foreground = Chrome.SoftText,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 4, 8, 0)
                };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);

                string value;
                _item.FieldValues.TryGetValue(field.Id, out value);
                var box = new TextBox { Text = value ?? "" };
                if (field.Kind == "multiline")
                {
                    box.AcceptsReturn = true;
                    box.TextWrapping = TextWrapping.Wrap;
                    box.MinHeight = 52;
                    box.VerticalContentAlignment = VerticalAlignment.Top;
                }
                var fieldRef = field;
                box.TextChanged += delegate
                {
                    if (_loading || _item == null) return;
                    _item.FieldValues[fieldRef.Id] = box.Text;
                    NotifyEdited();
                };
                row.Children.Add(box);
                _fieldsPanel.Children.Add(row);
            }
        }

        // ------------------------------------------------------- free info

        private void RebuildInfo()
        {
            _infoPanel.Children.Clear();
            if (_item == null) return;
            foreach (var entry in _item.FreeInfo)
                _infoPanel.Children.Add(BuildInfoRow(entry));
        }

        private UIElement BuildInfoRow(InfoEntry entry)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            var remove = new Button
            {
                Content = "✕",
                Width = 24,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Supprimer cette information"
            };
            DockPanel.SetDock(remove, Dock.Right);
            remove.Click += delegate
            {
                _item.FreeInfo.Remove(entry);
                RebuildInfo();
                NotifyEdited();
            };
            row.Children.Add(remove);

            var titleBox = new TextBox { Text = entry.Title, Width = 130, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(titleBox, Dock.Left);
            titleBox.TextChanged += delegate
            {
                if (_loading) return;
                entry.Title = titleBox.Text;
                NotifyEdited();
            };
            row.Children.Add(titleBox);

            var valueBox = new TextBox { Text = entry.Value };
            valueBox.TextChanged += delegate
            {
                if (_loading) return;
                entry.Value = valueBox.Text;
                NotifyEdited();
            };
            row.Children.Add(valueBox);
            return row;
        }

        private void AddInfoEntry()
        {
            if (_item == null) return;
            var entry = new InfoEntry { Title = "Clé" };
            _item.FreeInfo.Add(entry);
            RebuildInfo();
            NotifyEdited();
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private readonly Image _portrait;
        private readonly Button _removePortrait;
        private readonly ToggleButton _previewToggle;
        private readonly ScrollViewer _preview;

        private BinderItem _item;
        private SheetTemplate _template;
        private Project _project;
        private StyleSheet _styles = StyleSheet.CreateDefault();
        private bool _loading;

        public event Action Edited;
        public event Action<string> LinkClicked;
        public event Action<int> ZoomStepRequested;
        public event Action PageSetupChanged;
        public event Action<bool> MarksToggled;
        public event Action StylesRequested;
        public event Action PreviewRequested, PrintRequested, ExportRequested, CompileRequested;
        public event Action PdfRequested;
        public event Action CalmRequested;

        /// <summary>Mode calme : le corps masque son ruban ; l'entête de la
        /// fiche reste (il fait partie de la « page » d'une fiche).</summary>
        public void SetCalm(bool calm)
        {
            _body.SetCalm(calm);
        }

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

            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var headerButtons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(headerButtons, Dock.Right);
            _previewToggle = new ToggleButton
            {
                Content = "Aperçu wiki",
                ToolTip = "Voir la fiche en lecture, comme une page de wiki",
                Padding = new Thickness(8, 2, 8, 2)
            };
            _previewToggle.Checked += delegate { ShowPreview(); };
            _previewToggle.Unchecked += delegate { HidePreview(); };
            headerButtons.Children.Add(_previewToggle);
            var setPortrait = new Button
            {
                Content = "Image principale…",
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = "Portrait affiché sur la fiche et sur sa carte du corkboard"
            };
            setPortrait.Click += delegate { ChoosePortrait(); };
            headerButtons.Children.Add(setPortrait);
            _removePortrait = new Button
            {
                Content = "✕",
                Width = 28,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Retirer l'image principale",
                Visibility = Visibility.Collapsed
            };
            _removePortrait.Click += delegate { SetPortrait(null); };
            headerButtons.Children.Add(_removePortrait);
            headerRow.Children.Add(headerButtons);

            _templateLabel = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerRow.Children.Add(_templateLabel);
            stack.Children.Add(headerRow);

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

            _portrait = new Image
            {
                MaxWidth = 140,
                MaxHeight = 170,
                Stretch = System.Windows.Media.Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(16, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var topRow = new DockPanel();
            DockPanel.SetDock(_portrait, Dock.Right);
            topRow.Children.Add(_portrait);
            topRow.Children.Add(_topScroll);
            top.Child = topRow;
            Children.Add(top);

            _body = new EditorView();
            _body.Edited += delegate { NotifyEdited(); };
            _body.LinkClicked += delegate(string title)
            {
                var handler = LinkClicked;
                if (handler != null) handler(title);
            };
            _body.ZoomStepRequested += delegate(int step)
            {
                var handler = ZoomStepRequested;
                if (handler != null) handler(step);
            };
            _body.PageSetupChanged += delegate
            {
                var handler = PageSetupChanged;
                if (handler != null) handler();
            };
            _body.MarksToggled += delegate(bool visible)
            {
                var handler = MarksToggled;
                if (handler != null) handler(visible);
            };
            _body.StylesRequested += delegate
            {
                var handler = StylesRequested;
                if (handler != null) handler();
            };
            _body.PreviewRequested += delegate { var h = PreviewRequested; if (h != null) h(); };
            _body.PrintRequested += delegate { var h = PrintRequested; if (h != null) h(); };
            _body.ExportRequested += delegate { var h = ExportRequested; if (h != null) h(); };
            _body.CompileRequested += delegate { var h = CompileRequested; if (h != null) h(); };
            _body.PdfRequested += delegate { var h = PdfRequested; if (h != null) h(); };
            _body.CalmRequested += delegate { var h = CalmRequested; if (h != null) h(); };

            _preview = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed
            };

            var center = new Grid();
            center.Children.Add(_body);
            center.Children.Add(_preview);
            Children.Add(center); // fills the remaining space
        }

        public bool HasItem { get { return _item != null; } }

        public bool ShowsItem(BinderItem item) { return _item == item; }

        public void SetStyleSheet(StyleSheet styles)
        {
            _styles = styles;
            _body.SetStyleSheet(styles);
        }

        public void SetProject(Model.Project project)
        {
            _project = project;
            _body.SetProject(project);
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
            RefreshPortrait();
            _loading = false;
            _body.LoadItem(item);
            if (_previewToggle.IsChecked == true) ShowPreview();
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
            _portrait.Visibility = Visibility.Collapsed;
            _removePortrait.Visibility = Visibility.Collapsed;
            _preview.Content = null;
            _body.Clear();
        }

        // ------------------------------------------------------- main image

        private void ChoosePortrait()
        {
            if (_item == null || _project == null) return;
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                var info = new System.IO.FileInfo(dialog.FileName);
                if (info.Length > 20 * 1024 * 1024)
                    throw new InvalidOperationException("image de plus de 20 Mo — réduisez-la d'abord.");
                var bytes = System.IO.File.ReadAllBytes(dialog.FileName);
                SetPortrait(_project.AddImage(bytes, System.IO.Path.GetExtension(dialog.FileName)));
            }
            catch (Exception error)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "Impossible de charger l'image :\n" + error.Message,
                    "Marabook", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetPortrait(string imageId)
        {
            if (_item == null) return;
            _item.ImageId = imageId; // dropped bytes are purged at save time
            RefreshPortrait();
            NotifyEdited();
            if (_previewToggle.IsChecked == true) ShowPreview();
        }

        private void RefreshPortrait()
        {
            var image = _project == null || _item == null ? null : _project.FindImage(_item.ImageId);
            var source = image == null ? null : MediaView.TryImage(image.Bytes, 320);
            _portrait.Source = source;
            _portrait.Visibility = source == null ? Visibility.Collapsed : Visibility.Visible;
            _removePortrait.Visibility = _portrait.Visibility;
        }

        // ------------------------------------------------------- wiki preview

        private void ShowPreview()
        {
            if (_item == null) return;
            _body.Commit(); // render what is currently typed
            _preview.Content = BuildPreviewContent();
            _preview.Visibility = Visibility.Visible;
            _body.Visibility = Visibility.Collapsed;
        }

        private void HidePreview()
        {
            _preview.Visibility = Visibility.Collapsed;
            _preview.Content = null;
            _body.Visibility = Visibility.Visible;
        }

        /// <summary>Wiki-like rendering: big title, an infobox on the right
        /// (portrait + filled fields + free info), the rich body below.</summary>
        private UIElement BuildPreviewContent()
        {
            var page = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(24, 20, 24, 20),
                Padding = new Thickness(28),
                MaxWidth = 900
            };
            var layout = new DockPanel { LastChildFill = true };

            // --- infobox ---
            var infobox = new StackPanel();
            var image = _project == null ? null : _project.FindImage(_item.ImageId);
            var source = image == null ? null : MediaView.TryImage(image.Bytes, 480);
            if (source != null)
                infobox.Children.Add(new Image
                {
                    Source = source,
                    Stretch = System.Windows.Media.Stretch.Uniform,
                    MaxHeight = 240,
                    Margin = new Thickness(0, 0, 0, 10)
                });
            if (_template != null)
                foreach (var field in _template.Fields)
                {
                    string value;
                    _item.FieldValues.TryGetValue(field.Id, out value);
                    if (string.IsNullOrEmpty(value)) continue;
                    AddInfoboxRow(infobox, field.Name, value);
                }
            foreach (var entry in _item.FreeInfo)
                if (!string.IsNullOrEmpty(entry.Value))
                    AddInfoboxRow(infobox, entry.Title, entry.Value);

            if (infobox.Children.Count > 0)
            {
                var infoboxFrame = new Border
                {
                    Background = Chrome.BarBgLight,
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(12),
                    Width = 250,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(20, 6, 0, 0),
                    Child = infobox
                };
                DockPanel.SetDock(infoboxFrame, Dock.Right);
                layout.Children.Add(infoboxFrame);
            }

            // --- title + body ---
            var main = new StackPanel();
            main.Children.Add(new TextBlock
            {
                Text = _item.Title,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = Chrome.PaperInk,
                TextWrapping = TextWrapping.Wrap
            });
            main.Children.Add(new TextBlock
            {
                Text = _template == null ? "Fiche" : _template.Name,
                FontSize = 12,
                Foreground = Chrome.PaperSoftInk,
                Margin = new Thickness(0, 2, 0, 8)
            });
            main.Children.Add(new Border
            {
                Height = 1,
                Background = Chrome.Border,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var flow = FlowConverter.ToFlow(_item.Document, _styles, _project);
            flow.PagePadding = new Thickness(0);
            main.Children.Add(new FlowDocumentScrollViewer
            {
                Document = flow,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsToolBarVisible = false,
                Focusable = false
            });

            layout.Children.Add(main);
            page.Child = layout;
            return page;
        }

        private static void AddInfoboxRow(StackPanel infobox, string label, string value)
        {
            infobox.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 4, 0, 0)
            });
            infobox.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 12,
                Foreground = Chrome.Ink,
                TextWrapping = TextWrapping.Wrap
            });
        }

        public void ReloadBody()
        {
            _body.Reload();
        }

        public string BodyPlainText()
        {
            return _body.PlainText();
        }

        public void SetZoom(double factor) { _body.SetZoom(factor); }
        public bool TryUndo() { return _body.TryUndo(); }
        public bool TryRedo() { return _body.TryRedo(); }
        public void ApplyPageSetup(PageSetup setup) { _body.ApplyPageSetup(setup); }
        public void SetFormattingMarks(bool visible) { _body.SetFormattingMarks(visible); }
        public void UpdateRulers() { _body.UpdateRulers(); }
        public void ShowSearch() { _body.ShowSearch(); }
        public void InsertFootnote() { _body.InsertFootnote(); }
        public void InsertWikiLink(string title) { _body.InsertWikiLink(title); }
        public void InsertImage() { _body.InsertImage(); }
        public void InsertRule() { _body.InsertRule(); }
        public void InsertSeparator() { _body.InsertSeparator(); }

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
                Width = 28,
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

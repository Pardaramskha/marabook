using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>La fiche (refonte batch 31) : les champs typés du modèle,
    /// GROUPÉS (« Infos », « Physique »…), la liste clé/valeur libre, le
    /// portrait avec ses options DESSOUS dans un cadre séparé — et un corps
    /// en MARKDOWN (dialecte Markdown We Go + [[liens]]), édité en source,
    /// rendu par l'« Aperçu wiki » (bouton proéminent, seul à gauche).
    /// L'éditeur riche à ruban n'existe plus dans les fiches.</summary>
    public class SheetView : DockPanel
    {
        private readonly StackPanel _fieldsPanel;
        private readonly StackPanel _infoPanel;
        private readonly TextBlock _templateLabel;
        private readonly ScrollViewer _topScroll;
        private readonly Image _portrait;
        private readonly Border _portraitFrame;
        private readonly TextBlock _portraitPlaceholder;
        private readonly Button _removePortrait;
        private readonly ToggleButton _previewToggle;
        private readonly ScrollViewer _preview;
        private readonly TextBox _bodyBox;
        private readonly Border _bodyChrome;
        private readonly DockPanel _findBar;
        private readonly TextBox _findBox;

        private BinderItem _item;
        private SheetTemplate _template;
        private Project _project;
        private StyleSheet _styles = StyleSheet.CreateDefault();
        private bool _loading;
        private double _zoom = 1.0;

        public event Action Edited;
        public event Action<string> LinkClicked;
        public event Action<int> ZoomStepRequested;

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

            // — L'en-tête : « Aperçu wiki » PROÉMINENT, seul à gauche (séparé
            // de tout groupe de boutons — batch 31), le libellé à sa droite.
            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            _previewToggle = new ToggleButton
            {
                Content = "📖  Aperçu wiki",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(14, 5, 14, 5),
                ToolTip = "Voir la fiche en lecture, comme une page de wiki "
                    + "(le corps markdown est mis en forme)"
            };
            _previewToggle.Checked += delegate { ShowPreview(); };
            _previewToggle.Unchecked += delegate { HidePreview(); };
            DockPanel.SetDock(_previewToggle, Dock.Left);
            headerRow.Children.Add(_previewToggle);

            _templateLabel = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
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
                FontWeight = FontWeights.SemiBold,
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
                MaxHeight = 320,
                Content = stack
            };

            // — La colonne portrait : l'image (ou son emplacement) EN HAUT,
            // ses options EN DESSOUS dans un cadre séparé (batch 31).
            var portraitColumn = new StackPanel
            {
                Width = 170,
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            _portrait = new Image
            {
                MaxWidth = 160,
                MaxHeight = 190,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Visibility = Visibility.Collapsed
            };
            _portraitPlaceholder = new TextBlock
            {
                Text = "🖼\nAucune image",
                TextAlignment = TextAlignment.Center,
                Foreground = Chrome.SoftText,
                FontSize = 13,
                Margin = new Thickness(0, 28, 0, 28)
            };
            var portraitStack = new StackPanel();
            portraitStack.Children.Add(_portrait);
            portraitStack.Children.Add(_portraitPlaceholder);
            _portraitFrame = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4),
                Child = portraitStack
            };
            portraitColumn.Children.Add(_portraitFrame);

            var portraitOptions = new StackPanel();
            var setPortrait = new Button
            {
                Content = "Image principale…",
                Padding = new Thickness(8, 3, 8, 3),
                ToolTip = "Portrait affiché sur la fiche et sur sa carte"
            };
            setPortrait.Click += delegate { ChoosePortrait(); };
            portraitOptions.Children.Add(setPortrait);
            _removePortrait = new Button
            {
                Content = "Retirer l'image",
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _removePortrait.Click += delegate { SetPortrait(null); };
            portraitOptions.Children.Add(_removePortrait);
            portraitColumn.Children.Add(new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 6, 0, 0),
                Child = portraitOptions
            });

            var topRow = new DockPanel();
            DockPanel.SetDock(portraitColumn, Dock.Right);
            topRow.Children.Add(portraitColumn);
            topRow.Children.Add(_topScroll);
            top.Child = topRow;
            Children.Add(top);

            // — Le corps : la SOURCE markdown, dans un simple champ de texte
            // (batch 31 — l'éditeur riche des fiches est retiré).
            _bodyBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(20, 14, 20, 14),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 13,
                Background = Chrome.PaperBg,
                Foreground = Chrome.PaperInk
            };
            _bodyBox.TextChanged += delegate
            {
                if (_loading || _item == null) return;
                _item.Document = TextDocument.FromPlainText(_bodyBox.Text);
                NotifyEdited();
            };
            _bodyBox.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                e.Handled = true;
                var handler = ZoomStepRequested;
                if (handler != null) handler(e.Delta > 0 ? 10 : -10);
            };

            // Barre de recherche minimale du corps (Ctrl+F) : suivant au fil
            // de l'eau, Échap referme.
            _findBox = new TextBox { Width = 220, Margin = new Thickness(6, 0, 0, 0) };
            _findBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
                else if (e.Key == Key.Escape) { HideSearch(); e.Handled = true; }
            };
            var findNext = new Button
            {
                Content = "Suivant",
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(8, 2, 8, 2)
            };
            findNext.Click += delegate { FindNext(); };
            var findClose = new Button
            {
                Content = "✕",
                Width = 24,
                Margin = new Thickness(6, 0, 0, 0)
            };
            findClose.Click += delegate { HideSearch(); };
            _findBar = new DockPanel
            {
                Margin = new Thickness(20, 6, 20, 6),
                Visibility = Visibility.Collapsed,
                LastChildFill = false
            };
            _findBar.Children.Add(new TextBlock
            {
                Text = "Rechercher :",
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            });
            _findBar.Children.Add(_findBox);
            _findBar.Children.Add(findNext);
            _findBar.Children.Add(findClose);

            var bodyStack = new DockPanel();
            DockPanel.SetDock(_findBar, Dock.Top);
            bodyStack.Children.Add(_findBar);
            bodyStack.Children.Add(_bodyBox);
            _bodyChrome = new Border
            {
                Background = Chrome.PaperBg,
                Child = bodyStack
            };

            _preview = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed
            };

            var center = new Grid();
            center.Children.Add(_bodyChrome);
            center.Children.Add(_preview);
            Children.Add(center); // fills the remaining space
        }

        public bool HasItem { get { return _item != null; } }

        public bool ShowsItem(BinderItem item) { return _item == item; }

        public void SetStyleSheet(StyleSheet styles)
        {
            _styles = styles;
        }

        public void SetProject(Model.Project project)
        {
            _project = project;
        }

        public void LoadItem(BinderItem item, SheetTemplate template)
        {
            _item = item;
            _template = template;
            _loading = true;
            var category = _project == null ? null : _project.SheetCategoryOf(item);
            _templateLabel.Text =
                (category != null ? "Fiche " + category.Name : "Fiche")
                + (template != null ? " — modèle " + template.Name
                    : " (modèle introuvable — champs libres uniquement)");
            RebuildFields();
            RebuildInfo();
            RefreshPortrait();
            _bodyBox.Text = item.Document.ToPlainText();
            _loading = false;
            if (_previewToggle.IsChecked == true) ShowPreview();
        }

        public void Commit()
        {
            if (_item == null) return;
            // Les champs et la source markdown écrivent dans l'élément au fil
            // de la frappe — rien à pousser, mais l'appel reste le point de
            // rendez-vous (la coquille commite avant sauvegarde/bascule).
            _item.Document = TextDocument.FromPlainText(_bodyBox.Text);
        }

        public void Clear()
        {
            _item = null;
            _template = null;
            _loading = true;
            _bodyBox.Text = "";
            _loading = false;
            _portrait.Visibility = Visibility.Collapsed;
            _portraitPlaceholder.Visibility = Visibility.Visible;
            _removePortrait.Visibility = Visibility.Collapsed;
            _preview.Content = null;
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
            _portraitPlaceholder.Visibility = source == null ? Visibility.Visible : Visibility.Collapsed;
            _removePortrait.Visibility = _portrait.Visibility;
        }

        // ------------------------------------------------------- wiki preview

        private void ShowPreview()
        {
            if (_item == null) return;
            _preview.Content = BuildPreviewContent();
            _preview.Visibility = Visibility.Visible;
            _bodyChrome.Visibility = Visibility.Collapsed;
        }

        private void HidePreview()
        {
            _preview.Visibility = Visibility.Collapsed;
            _preview.Content = null;
            _bodyChrome.Visibility = Visibility.Visible;
        }

        /// <summary>Wiki-like rendering: big title, an infobox on the right
        /// (portrait + filled fields + free info), the MARKDOWN body below.</summary>
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
            {
                string lastGroup = null;
                foreach (var field in _template.Fields)
                {
                    string value;
                    _item.FieldValues.TryGetValue(field.Id, out value);
                    if (string.IsNullOrEmpty(value)) continue;
                    if (field.Group.Length > 0 && field.Group != lastGroup)
                        infobox.Children.Add(new TextBlock
                        {
                            Text = field.Group,
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = Chrome.Accent,
                            Margin = new Thickness(0, 8, 0, 0)
                        });
                    lastGroup = field.Group.Length > 0 ? field.Group : lastGroup;
                    AddInfoboxRow(infobox, field.Name, value);
                }
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

            // --- title + markdown body ---
            var main = new StackPanel();
            main.Children.Add(new TextBlock
            {
                Text = _item.Title,
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = Chrome.PaperInk,
                TextWrapping = TextWrapping.Wrap
            });
            var category = _project == null ? null : _project.SheetCategoryOf(_item);
            main.Children.Add(new TextBlock
            {
                Text = category != null ? category.Name
                    : _template != null ? _template.Name : "Fiche",
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

            var flow = MarkdownRender.Build(_bodyBox.Text,
                delegate(string target)
                {
                    var handler = LinkClicked;
                    if (handler != null) handler(target);
                },
                ToggleTask);
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

        /// <summary>Clic sur une case à cocher de l'aperçu : bascule le
        /// caractère d'état dans la SOURCE markdown, puis re-rend.</summary>
        private void ToggleTask(int index)
        {
            var position = MarkdownDialect.FindTask(_bodyBox.Text, index);
            if (position < 0 || position >= _bodyBox.Text.Length) return;
            var text = _bodyBox.Text;
            var current = text[position];
            _bodyBox.Text = text.Substring(0, position)
                + (current == ' ' ? 'x' : ' ')
                + text.Substring(position + 1);
            if (_previewToggle.IsChecked == true) ShowPreview();
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

        // ---------------------------------------------- surface de la coquille
        // L'ancien corps (EditorView) portait pages, styles, impression… Une
        // fiche markdown n'en a plus : la surface reste pour la coquille,
        // réduite à ce qui a du sens.

        public void ReloadBody()
        {
            if (_previewToggle.IsChecked == true) ShowPreview();
        }

        public string BodyPlainText() { return _bodyBox.Text; }

        public void SetZoom(double factor)
        {
            _zoom = Math.Max(0.5, Math.Min(3.0, factor));
            _bodyBox.FontSize = 13 * _zoom;
        }

        public bool TryUndo()
        {
            if (!_bodyBox.IsFocused || !_bodyBox.CanUndo) return false;
            _bodyBox.Undo();
            return true;
        }

        public bool TryRedo()
        {
            if (!_bodyBox.IsFocused || !_bodyBox.CanRedo) return false;
            _bodyBox.Redo();
            return true;
        }

        public void ApplyPageSetup(PageSetup setup) { }
        public void SetFormattingMarks(bool visible) { }
        public void UpdateRulers() { }
        public void SetCalm(bool calm) { }

        public void ShowSearch()
        {
            _findBar.Visibility = Visibility.Visible;
            _findBox.Focus();
            _findBox.SelectAll();
        }

        private void HideSearch()
        {
            _findBar.Visibility = Visibility.Collapsed;
            _bodyBox.Focus();
        }

        private void FindNext()
        {
            var needle = _findBox.Text;
            if (needle.Length == 0) return;
            var from = _bodyBox.SelectionStart + _bodyBox.SelectionLength;
            var index = _bodyBox.Text.IndexOf(needle, from,
                StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) // reboucle en tête
                index = _bodyBox.Text.IndexOf(needle, 0,
                    StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) return;
            _bodyBox.Focus();
            _bodyBox.Select(index, needle.Length);
            var line = _bodyBox.GetLineIndexFromCharacterIndex(index);
            _bodyBox.ScrollToLine(Math.Max(0, line));
            _findBox.Focus(); // la main reste à la recherche (Enter enchaîne)
        }

        public void InsertFootnote() { } // les fiches n'ont plus de notes de bas de page

        public void InsertWikiLink(string title)
        {
            InsertAtCaret("[[" + title + "]]");
        }

        public void InsertImage()
        {
            InsertAtCaret("![description](adresse)");
        }

        public void InsertRule() { InsertAtCaret("\n---\n"); }
        public void InsertSeparator() { InsertAtCaret("\n***\n"); }

        private void InsertAtCaret(string text)
        {
            if (_item == null) return;
            if (_previewToggle.IsChecked == true) _previewToggle.IsChecked = false;
            var at = _bodyBox.SelectionStart;
            _bodyBox.Text = _bodyBox.Text.Substring(0, at) + text
                + _bodyBox.Text.Substring(at + _bodyBox.SelectionLength);
            _bodyBox.SelectionStart = at + text.Length;
            _bodyBox.Focus();
        }

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
            string lastGroup = null;
            foreach (var field in _template.Fields)
            {
                // Intertitre de groupe (batch 31) : « Infos », « Physique »…
                // Les champs sans groupe restent en tête, sans intertitre.
                if (field.Group.Length > 0 && field.Group != lastGroup)
                    _fieldsPanel.Children.Add(new TextBlock
                    {
                        Text = field.Group,
                        Foreground = Chrome.Accent,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, lastGroup == null
                            && _fieldsPanel.Children.Count == 0 ? 0 : 8, 0, 4)
                    });
                if (field.Group.Length > 0) lastGroup = field.Group;

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

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.History;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>The corkboard: one index card per child of the selected folder,
    /// title + editable synopsis (media get a thumbnail). Cards reorder by drag
    /// &amp; drop (undoable), double-click opens the item.</summary>
    public class CorkboardView : Border
    {
        private readonly WrapPanel _cards;
        private BinderItem _folder;
        private HistoryManager _history;

        private BinderItem _dragCandidate;
        private Point _dragStart;

        public event Action<BinderItem> Navigate;
        public event Action Changed; // synopsis edited or cards reordered

        public CorkboardView()
        {
            Background = Chrome.WindowBg;
            _cards = new WrapPanel { Margin = new Thickness(16) };
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _cards
            };
            Child = scroll;
            AllowDrop = true;
            DragOver += OnBoardDragOver;
            Drop += OnBoardDrop;
        }

        public void Load(BinderItem folder, HistoryManager history)
        {
            _folder = folder;
            _history = history;
            Rebuild();
        }

        public void Clear()
        {
            _folder = null;
            _cards.Children.Clear();
        }

        private void Rebuild()
        {
            _cards.Children.Clear();
            if (_folder == null) return;
            if (_folder.Children.Count == 0)
            {
                _cards.Children.Add(new TextBlock
                {
                    Text = "(dossier vide)",
                    Foreground = Chrome.SoftText,
                    Margin = new Thickness(12)
                });
                return;
            }
            foreach (var child in _folder.Children)
                _cards.Children.Add(BuildCard(child));
        }

        private UIElement BuildCard(BinderItem item)
        {
            var card = new Border
            {
                Width = 210,
                MinHeight = 130,
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(8),
                Tag = item,
                AllowDrop = true
            };

            var layout = new DockPanel();

            var titleBar = new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 6, 10, 6)
            };
            DockPanel.SetDock(titleBar, Dock.Top);
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock
            {
                Text = KindGlyph(item),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            titleRow.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 160
            });
            titleBar.Child = titleRow;
            layout.Children.Add(titleBar);

            if (item.Kind == ItemKind.Media && MediaView.IsImage(item.MediaExtension)
                && item.MediaBytes != null)
            {
                var thumb = MediaView.TryImage(item.MediaBytes, 200);
                layout.Children.Add(new Image
                {
                    Source = thumb,
                    Stretch = Stretch.Uniform,
                    MaxHeight = 90,
                    Margin = new Thickness(8)
                });
            }
            else
            {
                var synopsis = new TextBox
                {
                    Text = item.Synopsis ?? "",
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Padding = new Thickness(8),
                    VerticalContentAlignment = VerticalAlignment.Top,
                    ToolTip = "Synopsis — modifiable directement sur la carte"
                };
                var itemRef = item;
                synopsis.TextChanged += delegate
                {
                    itemRef.Synopsis = synopsis.Text;
                    var handler = Changed;
                    if (handler != null) handler();
                };
                layout.Children.Add(synopsis);
            }

            card.Child = layout;
            card.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2)
                {
                    var handler = Navigate;
                    if (handler != null) handler(item);
                    e.Handled = true;
                    return;
                }
                _dragCandidate = item;
                _dragStart = e.GetPosition(this);
            };
            card.MouseMove += OnCardMouseMove;
            card.DragOver += OnBoardDragOver;
            card.Drop += delegate(object sender, DragEventArgs e) { DropOnCard(item, e); };
            return card;
        }

        private static string KindGlyph(BinderItem item)
        {
            if (item.Kind == ItemKind.Folder) return "\uE8B7";
            if (item.Kind == ItemKind.Sheet) return "\uE77B";
            if (item.Kind == ItemKind.Media) return "\uE723";
            return "\uE7C3";
        }

        // ------------------------------------------------------- drag reorder

        private void OnCardMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
            var position = e.GetPosition(this);
            if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var dragged = _dragCandidate;
            _dragCandidate = null;
            DragDrop.DoDragDrop(this, new DataObject("UniversSaleCard", dragged.Id), DragDropEffects.Move);
        }

        private void OnBoardDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent("UniversSaleCard") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropOnCard(BinderItem target, DragEventArgs e)
        {
            var dragged = FindChild((string)e.Data.GetData("UniversSaleCard"));
            if (dragged == null || dragged == target) return;
            var index = _folder.Children.IndexOf(target);
            var oldIndex = _folder.Children.IndexOf(dragged);
            if (oldIndex < index) index--; // account for removal before insertion
            Reorder(dragged, index);
            e.Handled = true;
        }

        private void OnBoardDrop(object sender, DragEventArgs e)
        {
            var dragged = FindChild((string)e.Data.GetData("UniversSaleCard"));
            if (dragged == null) return;
            Reorder(dragged, _folder.Children.Count - 1); // empty space: move to end
            e.Handled = true;
        }

        private BinderItem FindChild(string id)
        {
            if (_folder == null || id == null) return null;
            foreach (var child in _folder.Children)
                if (child.Id == id) return child;
            return null;
        }

        private void Reorder(BinderItem item, int newIndex)
        {
            if (newIndex < 0 || _folder.Children.IndexOf(item) == newIndex) return;
            _history.Run(new MoveItemAction(item, _folder, newIndex));
            Rebuild();
            var handler = Changed;
            if (handler != null) handler();
        }
    }
}

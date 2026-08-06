using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.History;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>The corkboard: one index card per child of the selected folder
    /// (or category). Card text is read-only: notes first, else the start of
    /// the text for written documents, empty for sheets; sheets show their main
    /// image, media a thumbnail. Cards reorder by drag &amp; drop (undoable),
    /// double-click opens the item.</summary>
    public class CorkboardView : Border
    {
        private readonly WrapPanel _cards;
        private BinderItem _folder;
        private HistoryManager _history;
        private Project _project; // image store lookups

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

        public void Load(BinderItem folder, HistoryManager history, Project project)
        {
            _folder = folder;
            _history = history;
            _project = project;
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
            titleRow.Children.Add(ItemIcons.Render(item, 11, Chrome.SoftText));
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

            var body = new StackPanel();

            // Pictures first: media thumbnail, or a sheet's main image.
            byte[] pictureBytes = null;
            if (item.Kind == ItemKind.Media && MediaView.IsImage(item.MediaExtension))
                pictureBytes = item.MediaBytes;
            else if (item.Kind == ItemKind.Sheet && _project != null)
            {
                var image = _project.FindImage(item.ImageId);
                if (image != null) pictureBytes = image.Bytes;
            }
            if (pictureBytes != null)
            {
                var thumb = MediaView.TryImage(pictureBytes, 200);
                if (thumb != null)
                    body.Children.Add(new Image
                    {
                        Source = thumb,
                        Stretch = Stretch.Uniform,
                        MaxHeight = 90,
                        Margin = new Thickness(8, 8, 8, 0)
                    });
            }

            // Card text, read-only: notes first; else the beginning of the text
            // for written documents; sheets without notes stay blank.
            var text = (item.Notes ?? "").Trim();
            if (text.Length == 0 && item.Kind == ItemKind.Text)
            {
                text = item.Document.ToPlainText().Trim();
                if (text.Length > 220) text = text.Substring(0, 220).TrimEnd() + "…";
            }
            if (text.Length > 0)
                body.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Padding = new Thickness(8),
                    MaxHeight = 150,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            layout.Children.Add(body);

            card.Child = layout;
            card.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                _dragCandidate = item;
                _dragStart = e.GetPosition(this);
            };
            // Single click opens (Word/Explorer feel); a drag that actually
            // started clears the candidate, so reordering still works.
            card.MouseLeftButtonUp += delegate
            {
                if (_dragCandidate != item) return;
                _dragCandidate = null;
                var handler = Navigate;
                if (handler != null) handler(item);
            };
            card.MouseMove += OnCardMouseMove;
            card.DragOver += OnBoardDragOver;
            card.Drop += delegate(object sender, DragEventArgs e) { DropOnCard(item, e); };
            return card;
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

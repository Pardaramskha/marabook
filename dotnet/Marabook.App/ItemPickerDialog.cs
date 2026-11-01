using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE CHOIX D'UN ÉLÉMENT DU PROJET (26/10/2026 soir) : une liste
    /// filtrable des fiches, écrits, médias… (un genre, ou tous), chaque ligne
    /// avec son icône et son genre ; double-clic ou Entrée pour choisir. Un
    /// module (Mental-O) s'en sert pour lier une fiche, un écrit ou une tuile
    /// à une boîte de carte.</summary>
    public class ItemPickerDialog : Window
    {
        private readonly TextBox _filter;
        private readonly ListBox _list;
        private readonly List<BinderItem> _items;
        private bool _accepted;

        private ItemPickerDialog(string caption, List<BinderItem> items)
        {
            Title = caption;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            _items = items;

            var panel = new StackPanel { Margin = new Thickness(16), Width = 420 };
            panel.Children.Add(new TextBlock
            {
                Text = caption,
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            _filter = new TextBox { Watermark = "Filtrer par titre…" };
            _filter.TextChanged += delegate { Fill(); };
            _filter.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Down && _list.ItemCount > 0) { _list.SelectedIndex = Math.Max(0, _list.SelectedIndex); _list.Focus(); e.Handled = true; }
                else if (e.Key == Key.Return || e.Key == Key.Enter) { Accept(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
            panel.Children.Add(_filter);

            _list = new ListBox { Height = 320, Margin = new Thickness(0, 8, 0, 0), Background = Chrome.FieldBg };
            _list.DoubleTapped += delegate { Accept(); };
            _list.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Return || e.Key == Key.Enter) { Accept(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
            panel.Children.Add(_list);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = Buttons.Text("Choisir", null, Buttons.Bar, Buttons.Look.Primary);
            ok.MinWidth = 80;
            ok.Click += delegate { Accept(); };
            var cancel = Buttons.Text("Annuler", null, Buttons.Bar, Buttons.Look.Outline);
            cancel.MinWidth = 80;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            Fill();
            Opened += delegate { _filter.Focus(); };
        }

        private void Accept()
        {
            if (_list.SelectedItem == null && _list.ItemCount > 0) _list.SelectedIndex = 0;
            if (_list.SelectedItem == null) return;
            _accepted = true;
            Close();
        }

        private void Fill()
        {
            var needle = (_filter.Text ?? "").Trim();
            _list.SelectedIndex = -1;
            _list.Items.Clear();
            foreach (var item in _items)
            {
                if (needle.Length > 0 && item.Title.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                _list.Items.Add(new ListBoxItem { Content = Row(item), Tag = item });
            }
            if (_list.ItemCount > 0) _list.SelectedIndex = 0;
        }

        private static Control Row(BinderItem item)
        {
            var row = new DockPanel();
            var kind = new TextBlock
            {
                Text = ItemTiles.KindOf(item),
                Foreground = Chrome.FaintText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            DockPanel.SetDock(kind, Dock.Right);
            row.Children.Add(kind);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(ItemIcons.Render(item, 12, Chrome.SoftText));
            left.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = Chrome.Ink,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(left);
            return row;
        }

        /// <summary>L'élément choisi, ou null. « kinds » (genres d'ItemTiles.KindOf) restreint la liste ; null = tous.</summary>
        public static async Task<BinderItem> Ask(Window owner, Project project, string caption, string[] kinds)
        {
            var items = new List<BinderItem>();
            if (project != null)
                foreach (var item in project.AllItems())
                {
                    if (!ItemTiles.IsLinkable(item)) continue;
                    if (kinds != null && Array.IndexOf(kinds, ItemTiles.KindOf(item)) < 0) continue;
                    items.Add(item);
                }
            items.Sort(delegate(BinderItem a, BinderItem b) { return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase); });
            var dialog = new ItemPickerDialog(caption, items);
            Dialogs.FitToScreen(dialog, owner);
            if (owner != null) await dialog.ShowDialog(owner);
            else
            {
                var done = new TaskCompletionSource<bool>();
                dialog.Closed += delegate { done.TrySetResult(true); };
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Show();
                await done.Task;
            }
            if (!dialog._accepted) return null;
            var chosen = dialog._list.SelectedItem as ListBoxItem;
            return chosen == null ? null : chosen.Tag as BinderItem;
        }
    }
}

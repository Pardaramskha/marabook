using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>« TUILE… » (2.0.0, 22/11/2026, Rémi) : la fenêtre qui
    /// personnalise la tuile d'un élément — sa COULEUR et son IMAGE, qui
    /// étaient deux entrées (et un sous-menu) du clic droit. Toute tuile
    /// colorable a la couleur (écrit, fiche, livre, dossier) ; l'image, qui
    /// remplace l'extrait au tableau, vaut pour un écrit ou un livre.
    /// Chaque geste s'applique AUSSITÔT par l'action annulable d'avant
    /// (ChangeColorAction, ChangeImageAction) : la fenêtre n'a qu'un
    /// « Fermer », et Ctrl+Z défait comme toujours. Les couleurs sont celles
    /// de la palette, sous leur nom.</summary>
    public class TileDialog : Window
    {
        private readonly Project _project;
        private readonly BinderItem _item;
        private readonly Action<string> _applyColor;
        private readonly Func<Task> _changeImage;
        private readonly Action _removeImage;
        private readonly WrapPanel _swatches;
        private readonly TextBlock _colorName;
        private readonly Border _band, _picture;
        private readonly Button _remove, _choose;
        private readonly TextBlock _imageNote;

        private TileDialog(Window owner, Project project, BinderItem item, Action<string> applyColor, Func<Task> changeImage, Action removeImage)
        {
            _project = project;
            _item = item;
            _applyColor = applyColor;
            _changeImage = changeImage;
            _removeImage = removeImage;
            Title = "Tuile";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            Tag = "tile-dialog";

            var panel = new StackPanel { Margin = new Thickness(16) };

            // L'aperçu : la tuile en petit — son liseré de couleur, son image, son titre.
            var card = new DockPanel();
            _band = new Border { Height = 6, CornerRadius = new CornerRadius(7, 7, 0, 0), Tag = "tile-band" };
            DockPanel.SetDock(_band, Dock.Top);
            card.Children.Add(_band);
            var title = new TextBlock { Text = item.Title, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 6, 10, 8) };
            DockPanel.SetDock(title, Dock.Bottom);
            card.Children.Add(title);
            _picture = new Border { Background = Chrome.FieldBg, ClipToBounds = true, Tag = "tile-picture" };
            card.Children.Add(_picture);
            panel.Children.Add(new Border
            {
                Width = 200, Height = 132, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(8), ClipToBounds = true,
                Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Child = card, Margin = new Thickness(0, 0, 0, 14), Tag = "tile-preview"
            });

            var colorHead = new DockPanel();
            _colorName = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Tag = "tile-color-name" };
            DockPanel.SetDock(_colorName, Dock.Right);
            colorHead.Children.Add(_colorName);
            colorHead.Children.Add(new TextBlock { Text = "Couleur", FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
            panel.Children.Add(colorHead);
            _swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Tag = "tile-swatches" };
            panel.Children.Add(_swatches);

            var imageBlock = new StackPanel { Margin = new Thickness(0, 12, 0, 0), IsVisible = item.Kind == ItemKind.Text || item.Kind == ItemKind.Book, Tag = "tile-image" };
            imageBlock.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 0, 0, 12) });
            imageBlock.Children.Add(new TextBlock { Text = "Image", FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
            _imageNote = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
            imageBlock.Children.Add(_imageNote);
            var imageButtons = new StackPanel { Orientation = Orientation.Horizontal };
            _choose = Buttons.IconText("image-square-bold", "Choisir une image…", "Une image (PNG, JPG…) : au tableau, elle remplace l'extrait du texte sur la tuile", Buttons.Bar, Buttons.Look.Outline);
            _choose.Tag = "tile-image-choose";
            _choose.Click += async delegate
            {
                if (_changeImage == null) return;
                await _changeImage();
                Refresh();
            };
            imageButtons.Children.Add(_choose);
            _remove = Buttons.Text("Retirer l'image", "La tuile retrouve l'extrait du texte", Buttons.Bar, Buttons.Look.Outline);
            _remove.Tag = "tile-image-remove";
            _remove.Margin = new Thickness(8, 0, 0, 0);
            _remove.Click += delegate
            {
                if (_removeImage != null) _removeImage();
                Refresh();
            };
            imageButtons.Children.Add(_remove);
            imageBlock.Children.Add(imageButtons);
            panel.Children.Add(imageBlock);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var close = Buttons.Text("Fermer", null, Buttons.Bar, Buttons.Look.Outline);
            close.MinWidth = 90;
            close.IsDefault = true;
            close.IsCancel = true;
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            panel.Children.Add(buttons);

            Content = panel;
            Refresh();
        }

        private void Refresh()
        {
            var color = _item.CardColor;
            _band.Background = color == null ? (IBrush)Chrome.Border : new SolidColorBrush(Ink.Parse(color).ToColor());
            _colorName.Text = color == null ? "Aucune couleur" : ColorPalette.Label(color);
            ColorMenus.FillSwatches(_swatches, this, _project, color, true, "Aucune couleur", delegate(string hex)
            {
                if (_applyColor != null) _applyColor(hex);
                Refresh();
            });

            Control shown = null;
            if (_item.ImageId != null && _project != null)
            {
                try
                {
                    var stored = _project.FindImage(_item.ImageId);
                    var source = stored == null || stored.Bytes == null ? null : MediaView.TryImage(stored.Bytes, 400);
                    if (source != null) shown = new Image { Source = source, Stretch = Stretch.UniformToFill };
                }
                catch (Exception) { shown = null; }
            }
            if (shown == null)
                shown = new TextBlock
                {
                    Text = _item.Kind == ItemKind.Text ? "L'extrait du texte" : _item.Kind == ItemKind.Sheet ? "Le portrait de la fiche" : "",
                    Foreground = Chrome.FaintText, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
            _picture.Child = shown;
            _remove.IsVisible = _item.ImageId != null;
            _choose.Content = Icons.Label("image-square-bold", _item.ImageId == null ? "Choisir une image…" : "Changer l'image…", 12, Chrome.Ink);
            _imageNote.Text = _item.ImageId == null
                ? "Sans image, la tuile montre " + (_item.Kind == ItemKind.Book ? "ses notes." : "l'extrait du texte.")
                : "Au tableau, cette image remplace " + (_item.Kind == ItemKind.Book ? "les notes." : "l'extrait du texte.");
        }

        public static async Task Show(Window owner, Project project, BinderItem item, Action<string> applyColor, Func<Task> changeImage, Action removeImage)
        {
            var dialog = new TileDialog(owner, project, item, applyColor, changeImage, removeImage);
            await Dialogs.ShowModal(dialog, owner);
        }

        // — Sonde.
        internal static TileDialog ForProbe(Window owner, Project project, BinderItem item, Action<string> applyColor)
        {
            return new TileDialog(owner, project, item, applyColor, null, null);
        }
        internal void PickForProbe(string hex) { if (_applyColor != null) _applyColor(hex); Refresh(); }
        internal string ColorNameForProbe { get { return _colorName.Text ?? ""; } }
    }
}

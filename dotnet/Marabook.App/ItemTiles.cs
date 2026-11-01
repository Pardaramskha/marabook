using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LES TUILES HORS TABLEAU (26/10/2026 soir) : la tuile d'un élément
    /// telle que le tableau la montre — barre de titre avec l'icône, le titre
    /// et le dégradé de sa couleur, puis l'image, les notes ou l'extrait — mais
    /// inerte : ni menu ⋮, ni sélection, ni glisser-déposer. Un module (Mental-O)
    /// la pose dans une boîte de carte ; « condensée », elle se réduit à la
    /// barre de titre. Et la fiche d'identité d'un élément pour les modules.</summary>
    public static class ItemTiles
    {
        /// <summary>Le genre d'un élément, en français, pour les modules.</summary>
        public static string KindOf(BinderItem item)
        {
            switch (item.Kind)
            {
                case ItemKind.Sheet: return "fiche";
                case ItemKind.Media: return "media";
                case ItemKind.Book: return "livre";
                case ItemKind.Plan: return "plan";
                case ItemKind.Folder: return "dossier";
                case ItemKind.MindMap: return "carte";
                case ItemKind.PageTemplate: return "gabarit";
                default: return "texte";
            }
        }

        public static HostItem Describe(BinderItem item)
        {
            return new HostItem { Id = item.Id, Title = item.Title, Kind = KindOf(item), Color = item.CardColor };
        }

        /// <summary>Vrai pour un élément qu'un module peut viser : ni catégorie,
        /// ni élément de la corbeille.</summary>
        public static bool IsLinkable(BinderItem item)
        {
            if (item == null || item.IsCategory || !ShownByProfile(item)) return false;
            var root = item.RootCategory();
            return root == null || root.CategoryKey != Project.KeyTrash;
        }

        /// <summary>Le profil (1.1.0) cache les fiches ou les plans : leurs tuiles
        /// ne se proposent plus (Accueil, épinglés, modules). L'élément, lui, reste.</summary>
        public static bool ShownByProfile(BinderItem item)
        {
            if (item == null) return false;
            if (item.Kind == ItemKind.Sheet) return Profiles.Enabled(Profiles.Sheets);
            if (item.Kind == ItemKind.Plan) return Profiles.Enabled(Profiles.Plans);
            return true;
        }

        public static Control Build(Project project, BinderItem item, double width, bool condensed)
        {
            var card = new Border
            {
                Width = width,
                Background = Chrome.CardBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true
            };
            var layout = new DockPanel();

            var titleBar = new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 0, condensed ? 0 : 1),
                CornerRadius = condensed ? new CornerRadius(5) : new CornerRadius(5, 5, 0, 0),
                Padding = new Thickness(10, 5, 10, 5)
            };
            if (item.CardColor != null)
            {
                // La couleur personnelle : le même dégradé que la tuile du tableau.
                var accent = Ink.Parse(item.CardColor).ToColor();
                var fade = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
                };
                fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 0.5));
                fade.GradientStops.Add(new GradientStop(accent, 1.0));
                titleBar.Background = fade;
            }
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(ItemIcons.Render(item, 11, Chrome.SoftText));
            titleRow.Children.Add(new TextBlock
            {
                Text = item.Title,
                Foreground = Chrome.Ink,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = width - 50
            });
            titleBar.Child = titleRow;
            DockPanel.SetDock(titleBar, Dock.Top);
            layout.Children.Add(titleBar);
            if (!condensed) layout.Children.Add(BuildBody(project, item, width));
            card.Child = layout;
            return card;
        }

        /// <summary>Le corps de la tuile : l'image, puis les notes ou l'extrait,
        /// comme au tableau (CorkboardView.BuildCard) — en lecture seule.</summary>
        private static Control BuildBody(Project project, BinderItem item, double width)
        {
            var body = new StackPanel();

            byte[] pictureBytes = null;
            if (item.Kind == ItemKind.Media && MediaView.IsImage(item.MediaExtension))
                pictureBytes = item.MediaBytes;
            else if ((item.Kind == ItemKind.Sheet || item.Kind == ItemKind.Text || item.Kind == ItemKind.Book) && project != null)
            {
                var image = project.FindImage(item.ImageId);
                if (image != null) pictureBytes = image.Bytes;
            }
            var imageOnly = false;
            if (pictureBytes != null)
            {
                var thumb = MediaView.TryImage(pictureBytes, 200);
                if (thumb != null)
                {
                    imageOnly = item.Kind == ItemKind.Text || item.Kind == ItemKind.Book;
                    body.Children.Add(new Image
                    {
                        Source = thumb,
                        Stretch = Stretch.Uniform,
                        MaxHeight = imageOnly ? 120 : 80,
                        Margin = new Thickness(8, 8, 8, imageOnly ? 8 : 0)
                    });
                }
            }

            var text = imageOnly ? "" : (item.Notes ?? "").Trim();
            if (item.Kind == ItemKind.Plan)
            {
                var columns = item.Plan == null ? 0 : item.Plan.Columns.Count;
                var bricks = 0;
                if (item.Plan != null) foreach (var column in item.Plan.Columns) bricks += column.Entries.Count;
                text = columns == 0 ? "Plan vide"
                    : columns + (columns > 1 ? " colonnes" : " colonne") + " · " + bricks + (bricks > 1 ? " briques" : " brique");
            }
            if (item.Kind == ItemKind.MindMap)
            {
                var summary = MindMaps.Inspect(item.MapBytes);
                var provider = MindMapModules.Provider;
                var thumbnail = provider == null || item.MapBytes == null ? null : provider.Thumbnail(item.MapBytes, width - 24, 72);
                if (thumbnail != null)
                {
                    thumbnail.Margin = new Thickness(8, 8, 8, 0);
                    body.Children.Add(thumbnail);
                }
                text = summary.Label;
            }
            if (text.Length == 0 && item.Kind == ItemKind.Text && !imageOnly && item.Document != null)
            {
                text = item.Document.ToPlainText().Trim();
                if (text.Length > 160) text = text.Substring(0, 160).TrimEnd() + "…";
            }
            if (text.Length == 0 && item.Kind == ItemKind.Sheet)
                text = (item.Synopsis ?? "").Trim();
            if (text.Length > 0)
                body.Children.Add(new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    Padding = new Thickness(8, 6, 8, 8),
                    MaxHeight = 110,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            else if (body.Children.Count == 0)
                body.MinHeight = 24;
            return body;
        }
    }
}

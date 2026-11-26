using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE BANDEAU D'UNE VUE D'ATTERRISSAGE (26/11/2026, Rémi) : l'icône de la section, son titre, ses
    /// actions calées à droite — le bandeau de l'Astrolabe et des Cartes, donné à toutes les pages où l'on ARRIVE
    /// (les tableaux de la Pile, les Fiches, le Dictionnaire, le Gros Graph, le Profiler, la Chronologie). Il
    /// remplace le cartel en capitales posé au-dessus des vues, jugé de trop ; il ne porte plus de phrase de
    /// description, et il ne paraît JAMAIS sur un élément ouvert (écrit, carte, plan, frise, fiche…).</summary>
    internal sealed class ViewBanner : Border
    {
        public const string BannerTag = "view-banner";
        private readonly Panel _icon = new Panel { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _title;
        /// <summary>Avant l'icône : un « Retour », par exemple.</summary>
        public readonly StackPanel Lead = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        /// <summary>Les actions, calées à droite.</summary>
        public readonly StackPanel Actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

        public ViewBanner()
        {
            Background = Chrome.BarBgLight;
            BorderBrush = Chrome.Border;
            BorderThickness = new Thickness(0, 0, 0, 1);
            Padding = new Thickness(16, 8, 16, 8);
            MinHeight = Buttons.Bar + 16; // une même hauteur partout, que les boutons soient compacts ou non
            Tag = BannerTag;
            var row = new DockPanel();
            DockPanel.SetDock(Actions, Dock.Right);
            row.Children.Add(Actions);
            var left = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(Lead);
            left.Children.Add(_icon);
            _title = new TextBlock
            {
                Foreground = Chrome.Ink, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "view-banner-title"
            };
            left.Children.Add(_title);
            row.Children.Add(left);
            Child = row;
        }

        public ViewBanner(string icon, string title) : this()
        {
            SetIcon(Icons.Make(icon, 20, Chrome.Accent) as Control);
            Title = title;
        }

        public string Title { get { return _title.Text; } set { _title.Text = value ?? ""; } }

        public void SetIcon(Control icon)
        {
            _icon.Children.Clear();
            if (icon == null) return;
            icon.Margin = new Thickness(0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            _icon.Children.Add(icon);
        }

        /// <summary>L'icône et le titre d'un élément de la Pile (une racine, un dossier, un livre).</summary>
        public void SetItem(BinderItem item)
        {
            if (item == null) { SetIcon(null); Title = ""; return; }
            Control icon = null;
            try { icon = ItemIcons.Render(item, 20, Chrome.Accent); } catch (System.Exception) { icon = null; }
            SetIcon(icon);
            Title = item.Title;
        }

        /// <summary>Une rangée de boutons d'une ancienne barre passe dans le bandeau : sans ses marges de barre,
        /// et le bouton principal — premier à gauche dans une barre — en DERNIER, contre le bord droit.</summary>
        public static void Adopt(Panel row)
        {
            row.Margin = new Thickness(0);
            row.VerticalAlignment = VerticalAlignment.Center;
            var children = new List<Control>(row.Children);
            row.Children.Clear();
            for (var i = children.Count - 1; i >= 0; i--)
            {
                children[i].Margin = new Thickness(i == children.Count - 1 ? 0 : 6, 0, 0, 0);
                children[i].VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(children[i]);
            }
        }
    }
}

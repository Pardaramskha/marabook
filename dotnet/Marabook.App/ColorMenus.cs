using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>Le nuancier des couleurs de carte, partagé (29/09) : le
    /// bouton du Général, le sous-menu « Couleur » des tuiles et de la Pile,
    /// l'onglet Édition d'un livre — le nuancier de Marabook, les couleurs
    /// personnalisées du projet, « Nouvelle couleur… », « Aucune couleur ».</summary>
    public static class ColorMenus
    {
        /// <summary>Remplit un ContextMenu ou un MenuItem (sous-menu) ;
        /// apply reçoit l'hex choisi, ou null pour « aucune couleur ».</summary>
        public static void Fill(ItemsControl target, Window owner, Project project, string current, Action<string> apply)
        {
            foreach (var swatch in ItemIcons.TintSwatches) target.Items.Add(Entry(swatch, current, apply));
            if (project != null && project.CustomColors.Count > 0)
            {
                target.Items.Add(new Separator());
                foreach (var hex in project.CustomColors) target.Items.Add(Entry(hex, current, apply));
            }
            target.Items.Add(new Separator());
            var custom = new MenuItem { Header = "Nouvelle couleur…" };
            custom.Click += async delegate
            {
                var hex = await ColorDialog.Ask(owner);
                if (hex == null) return;
                if (project != null && !project.CustomColors.Contains(hex)) project.CustomColors.Insert(0, hex);
                apply(hex);
            };
            target.Items.Add(custom);
            // « Aucune couleur » : le nuancier la porte déjà (entrée nulle en
            // tête) ; sinon on l'ajoute quand il y a une couleur à retirer.
            if (current != null && Array.IndexOf(ItemIcons.TintSwatches, null) < 0)
            {
                var none = new MenuItem { Header = "Aucune couleur" };
                none.Click += delegate { apply(null); };
                target.Items.Add(none);
            }
        }

        private static MenuItem Entry(string value, string current, Action<string> apply)
        {
            var active = current == value;
            var entry = new MenuItem
            {
                Header = value ?? "Aucune couleur",
                FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
                Icon = Dot(value, active ? 2 : 1, active ? (IBrush)Chrome.Ink : Chrome.Border)
            };
            entry.Click += delegate { apply(value); };
            return entry;
        }

        /// <summary>La pastille d'une couleur (transparente pour null).</summary>
        public static Border Dot(string value, double border, IBrush borderBrush)
        {
            return new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                Background = value == null ? Brushes.Transparent : new SolidColorBrush(Ink.Parse(value).ToColor()),
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(border),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
        }
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Marabook.App
{
    /// <summary>LE seul endroit où l'on fabrique un bouton (porté de
    /// View/Buttons.cs) : trois formes — icône seule (carré), texte, icône +
    /// texte — à deux hauteurs (32 px dans les barres principales, 26 px dans
    /// les barres compactes), rayon 6 px, trois apparences : calme, contour,
    /// principal. L'icône suit la couleur du texte (liée au Foreground du
    /// bouton), donc l'état.</summary>
    public static class Buttons
    {
        public const double Bar = 32;
        public const double Compact = 26;
        public const double IconSize = 16;

        public enum Look { Calm, Outline, Primary }

        public static Button Icon(string icon, string tooltip, double height, Look look)
        {
            var button = new Button { Padding = new Thickness(0), Width = height };
            button.Content = IconContent(button, icon);
            Dress(button, tooltip, height, look);
            return button;
        }

        /// <summary>LE « Fermer » des panneaux du rail (27/10, Rémi : « unifier
        /// les boutons Fermer ») : la croix du jeu, calme, compacte — partout la même.</summary>
        public static Button Close(string tooltip)
        {
            var button = Icon("x-bold", tooltip, Compact, Look.Calm); // la croix X (27/10, Rémi), pas « exclude »
            button.Tag = "rail-close"; // sonde
            return button;
        }

        public static Button Text(string label, string tooltip, double height, Look look)
        {
            var button = new Button { Content = TextContent(label), Padding = new Thickness(10, 0, 10, 0) };
            Dress(button, tooltip, height, look);
            return button;
        }

        public static Button IconText(string icon, string label, string tooltip, double height, Look look)
        {
            var button = new Button { Padding = new Thickness(8, 0, 10, 0) };
            button.Content = IconTextContent(button, icon, label);
            Dress(button, tooltip, height, look);
            return button;
        }

        /// <summary>Une entrée du bouton « Nouveau ▾ ».</summary>
        public sealed class MenuEntry
        {
            public string Icon, Label, Tip;
            public Action Run;
            /// <summary>Faux : l'entrée est absente du menu (un profil la cache).</summary>
            public Func<bool> Shown;
            public MenuEntry(string icon, string label, Action run, string tip = null, Func<bool> shown = null) { Icon = icon; Label = label; Run = run; Tip = tip; Shown = shown; }
        }

        /// <summary>LE BOUTON « NOUVEAU ▾ » (26/11/2026, Rémi : la barre de commande des vues) — UN bouton principal
        /// qui déroule ce que la vue sait créer, à la place d'une rangée de « Nouveau… » qui passait sur deux lignes
        /// (cinq sur un livre, cinq sur les Fiches). Les autres actions de la barre deviennent des boutons discrets.</summary>
        public static Button NewMenu(string tooltip, double height, params MenuEntry[] entries)
        {
            var button = IconText("plus-bold", "Nouveau ▾", tooltip, height, Look.Primary);
            button.Tag = "new-menu";
            button.Click += delegate
            {
                var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
                foreach (var entry in entries)
                {
                    if (entry == null || (entry.Shown != null && !entry.Shown())) continue;
                    var run = entry.Run;
                    var item = new MenuItem { Header = entry.Label, Tag = "new:" + entry.Label };
                    if (entry.Icon != null) item.Icon = Icons.Make(entry.Icon, 14, Chrome.Ink);
                    if (entry.Tip != null) ToolTip.SetTip(item, entry.Tip);
                    // Après la fermeture du menu : une entrée peut en ouvrir un autre, sur le même bouton.
                    item.Click += delegate { Avalonia.Threading.Dispatcher.UIThread.Post(delegate { if (run != null) run(); }, Avalonia.Threading.DispatcherPriority.Background); };
                    menu.Items.Add(item);
                }
                Ui.ShowMenu(menu, button);
            };
            return button;
        }

        /// <summary>Sonde : les libellés que « Nouveau ▾ » déroulerait.</summary>
        internal static System.Collections.Generic.List<string> NewMenuLabelsForProbe(MenuEntry[] entries)
        {
            var labels = new System.Collections.Generic.List<string>();
            foreach (var entry in entries) if (entry != null && (entry.Shown == null || entry.Shown())) labels.Add(entry.Label);
            return labels;
        }

        /// <summary>Le GRAND CARRÉ du ruban : icône en haut au centre, libellé
        /// dessous, toute la hauteur du ruban.</summary>
        public static Button Big(string icon, string label, string tooltip, double size, Look look)
        {
            var button = new Button();
            button.Content = BigContent(button, icon, label);
            DressBig(button, tooltip, size, look);
            return button;
        }

        public static ToggleButton BigToggle(string icon, string label, string tooltip, double size, Look look)
        {
            var button = new ToggleButton();
            button.Content = BigContent(button, icon, label);
            DressBig(button, tooltip, size, look);
            return button;
        }

        public static ToggleButton IconToggle(string icon, string tooltip, double height)
        {
            return IconToggle(icon, tooltip, height, Look.Calm);
        }

        public static ToggleButton IconToggle(string icon, string tooltip, double height, Look look)
        {
            var button = new ToggleButton { Padding = new Thickness(0), Width = height };
            button.Content = IconContent(button, icon);
            Dress(button, tooltip, height, look);
            return button;
        }

        public static ToggleButton TextToggle(string label, string tooltip, double height)
        {
            return TextToggle(label, tooltip, height, Look.Calm);
        }

        public static ToggleButton TextToggle(string label, string tooltip, double height, Look look)
        {
            var button = new ToggleButton { Content = TextContent(label), Padding = new Thickness(10, 0, 10, 0) };
            Dress(button, tooltip, height, look);
            return button;
        }

        public static ToggleButton IconTextToggle(string icon, string label, string tooltip, double height)
        {
            return IconTextToggle(icon, label, tooltip, height, Look.Calm);
        }

        public static ToggleButton IconTextToggle(string icon, string label, string tooltip, double height, Look look)
        {
            var button = new ToggleButton { Padding = new Thickness(8, 0, 10, 0) };
            button.Content = IconTextContent(button, icon, label);
            Dress(button, tooltip, height, look);
            return button;
        }

        // ---- habillage

        private static void Dress(TemplatedControl button, string tooltip, double height, Look look)
        {
            button.Height = height;
            button.MinHeight = height;
            button.FontSize = 13;
            button.Focusable = false; // le clavier reste au texte
            if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(button, tooltip);
            ApplyLook(button, look);
        }

        private static void DressBig(TemplatedControl button, string tooltip, double size, Look look)
        {
            button.Height = size;
            button.MinWidth = size;
            // 18 + 2 + deux lignes naturelles (13,3 chacune) = 47, sous les 49
            // disponibles. La hauteur de ligne forcée à 12 px (13/09) rognait
            // les descendantes de la seconde ligne (« d'impression », 28/09).
            button.Padding = new Thickness(6, 3, 6, 2);
            button.VerticalAlignment = VerticalAlignment.Top;
            button.FontSize = 11;
            button.Focusable = false;
            if (!string.IsNullOrEmpty(tooltip)) ToolTip.SetTip(button, tooltip);
            ApplyLook(button, look);
        }

        private static void ApplyLook(TemplatedControl button, Look look)
        {
            if (!button.Classes.Contains(Theme.Owned)) button.Classes.Add(Theme.Owned);
            button.Classes.Remove(Theme.Calm);
            button.Classes.Remove(Theme.Primary);
            if (look == Look.Calm) button.Classes.Add(Theme.Calm);
            else if (look == Look.Primary) button.Classes.Add(Theme.Primary);
        }

        // ---- contenus

        /// <summary>L'icône dans la couleur du texte du bouton (liaison au
        /// Foreground : elle suit l'état actif, principal, désactivé).</summary>
        private static Control IconContent(TemplatedControl owner, string icon)
        {
            var glyph = Icons.Make(icon, IconSize, owner.Foreground);
            var path = glyph as Path;
            if (path != null) path.Bind(Shape.FillProperty, owner.GetObservable(TemplatedControl.ForegroundProperty));
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            return glyph;
        }

        private static Control TextContent(string label)
        {
            return new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        }

        private static Control IconTextContent(TemplatedControl owner, string icon, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = IconContent(owner, icon);
            glyph.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(glyph);
            row.Children.Add(TextContent(label));
            return row;
        }

        private static Control BigContent(TemplatedControl owner, string icon, string label)
        {
            var column = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            var glyph = Icons.Make(icon, 18, owner.Foreground);
            var path = glyph as Path;
            if (path != null) path.Bind(Shape.FillProperty, owner.GetObservable(TemplatedControl.ForegroundProperty));
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 0, 2);
            column.Children.Add(glyph);
            var text = new TextBlock
            {
                Text = label,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 88
            };
            // Avalonia remesure un TextBlock replié à la largeur EXACTE qu'il
            // a annoncée : le dernier mot d'une ligne pouvait alors ne plus y
            // tenir (arrondi) et se faire rogner au bord du bouton
            // (« d'impression » coupé, 27/09). La largeur mesurée est un
            // plancher, avec deux pixels d'air.
            text.Measure(new Size(88, double.PositiveInfinity));
            if (text.DesiredSize.Width > 0) text.MinWidth = Math.Ceiling(text.DesiredSize.Width) + 2;
            column.Children.Add(text);
            return column;
        }
    }
}

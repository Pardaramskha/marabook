using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>Le nuancier des couleurs de carte, partagé (29/09) : le
    /// bouton du Général, la fenêtre « Tuile », l'onglet Édition d'un livre,
    /// les catégories de fiches, les types d'événements — le nuancier de
    /// Marabook, les couleurs personnalisées, « Nouvelle couleur… »,
    /// « Aucune couleur ».
    /// 22/11 (la PALETTE, Rémi) : chaque couleur paraît sous son NOM
    /// (Model.ColorPalette) au lieu de son code, et les couleurs
    /// personnalisées sont celles de la palette de l'auteur (Préférences ›
    /// Personnalisation › Palette) — celles que le projet portait déjà y sont
    /// reprises au passage.</summary>
    public static class ColorMenus
    {
        /// <summary>Le nuancier général de Marabook, « aucune » (null) en tête : tuiles, icônes, pastilles.</summary>
        public static string[] GeneralWithNone()
        {
            var list = new List<string> { null };
            foreach (var color in ColorPalette.General) list.Add(color.Hex);
            return list.ToArray();
        }

        /// <summary>Les couleurs personnalisées de la palette, celles du projet reprises au passage.</summary>
        public static List<string> Customs(Project project)
        {
            if (project != null && ColorPalette.Absorb(project.CustomColors)) Marabook.Settings.AppSettings.Save();
            var list = new List<string>();
            foreach (var color in ColorPalette.Custom) list.Add(color.Hex);
            return list;
        }

        /// <summary>Remplit un ContextMenu ou un MenuItem (sous-menu) ;
        /// apply reçoit l'hex choisi, ou null pour « aucune couleur » —
        /// allowNone faux (1/11, l'accent de l'Encyclopédie) : jamais null,
        /// l'entrée « Aucune couleur » n'est pas proposée.</summary>
        public static void Fill(ItemsControl target, Window owner, Project project, string current, Action<string> apply, bool allowNone = true)
        {
            if (allowNone) target.Items.Add(Entry(null, current, apply));
            foreach (var color in ColorPalette.General) target.Items.Add(Entry(color.Hex, current, apply));
            var customs = Customs(project);
            if (customs.Count > 0)
            {
                target.Items.Add(new Separator());
                foreach (var hex in customs) target.Items.Add(Entry(hex, current, apply));
            }
            target.Items.Add(new Separator());
            var custom = new MenuItem { Header = "Nouvelle couleur…" };
            custom.Click += async delegate
            {
                var hex = await ColorDialog.Ask(owner); // elle rejoint la palette, avec son nom
                if (hex == null) return;
                apply(hex);
            };
            target.Items.Add(custom);
        }

        private static MenuItem Entry(string value, string current, Action<string> apply)
        {
            var active = string.Equals(current, value, StringComparison.OrdinalIgnoreCase);
            var entry = new MenuItem
            {
                Header = value == null ? "Aucune couleur" : ColorPalette.Label(value),
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

        /// <summary>Une rangée de pastilles à cliquer (22/11 — les fenêtres « Tuile » et « Icône »,
        /// les Préférences) : « aucune » si allowNone, le nuancier général, les couleurs
        /// personnalisées, puis « + » = nouvelle couleur. Chaque pastille dit son nom au survol.
        /// rebuild est rappelé après un choix, pour redessiner la rangée.</summary>
        public static void FillSwatches(Panel host, Window owner, Project project, string current, bool allowNone, string noneTip, Action<string> apply)
        {
            host.Children.Clear();
            var values = new List<string>();
            if (allowNone) values.Add(null);
            foreach (var color in ColorPalette.General) values.Add(color.Hex);
            foreach (var hex in Customs(project)) if (!values.Exists(delegate(string v) { return string.Equals(v, hex, StringComparison.OrdinalIgnoreCase); })) values.Add(hex);
            // Une couleur en cours qui n'est plus dans la palette reste montrée : on voit ce qu'on a.
            if (current != null && !values.Exists(delegate(string v) { return string.Equals(v, current, StringComparison.OrdinalIgnoreCase); })) values.Add(current);
            foreach (var value in values)
            {
                var hex = value;
                var active = string.Equals(current, hex, StringComparison.OrdinalIgnoreCase);
                var chip = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(0, 0, 6, 6),
                    Background = hex == null ? Brushes.Transparent : new SolidColorBrush(Ink.Parse(hex).ToColor()),
                    BorderBrush = active ? (IBrush)Chrome.Ink : Chrome.BorderStrong,
                    BorderThickness = new Thickness(active ? 2.5 : 1),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Tag = "swatch:" + (hex ?? "")
                };
                if (hex == null)
                    chip.Child = new Avalonia.Controls.Shapes.Line { StartPoint = new Point(4, 18), EndPoint = new Point(18, 4), Stroke = Chrome.Danger, StrokeThickness = 1.5 };
                ToolTip.SetTip(chip, hex == null ? (noneTip ?? "Aucune couleur") : ColorPalette.Describe(hex));
                chip.PointerReleased += delegate { apply(hex); };
                host.Children.Add(chip);
            }
            var add = Buttons.Icon("plus-bold", "Nouvelle couleur : elle rejoint votre palette (Préférences › Personnalisation)", Buttons.Compact, Buttons.Look.Outline);
            add.Tag = "swatch-new";
            add.Margin = new Thickness(0, 0, 0, 6);
            add.Click += async delegate
            {
                var hex = await ColorDialog.Ask(owner);
                if (hex != null) apply(hex);
            };
            host.Children.Add(add);
        }
    }
}

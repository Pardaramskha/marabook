using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AppSettings = Marabook.Settings.AppSettings;

namespace Marabook.App
{
    /// <summary>Les polices MANQUANTES (07/10) : le projet demande des
    /// familles que cette machine n'a pas — la barre d'état les signale d'une
    /// pastille rouge, et ce dialogue propose UN remplacement par police
    /// manquante, global : il s'applique partout où elle est demandée
    /// (écran, PDF, impression), sans toucher au document, qui garde ses
    /// noms pour la machine qui les a. Réglage de l'application
    /// (AppSettings.FontSubstitutions).</summary>
    public class FontSubstitutionDialog : Window
    {
        private readonly List<KeyValuePair<string, ComboBox>> _choices = new List<KeyValuePair<string, ComboBox>>();
        private bool _accepted;
        private const string None = "— aucun remplacement —";

        private FontSubstitutionDialog(Window owner, List<string> missing)
        {
            Title = "Polices manquantes";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 14), MinWidth = 420 };
            panel.Children.Add(new TextBlock
            {
                Text = (missing.Count > 1 ? missing.Count + " polices demandées par le projet ne sont pas installées sur cet ordinateur."
                    : "Une police demandée par le projet n'est pas installée sur cet ordinateur."),
                Foreground = Chrome.Ink,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Choisissez une police installée pour chacune : le remplacement vaut partout où elle est demandée "
                    + "(écran, PDF, impression) et ne modifie pas le projet, qui garde ses polices pour un ordinateur qui les a.",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                Margin = new Thickness(0, 6, 0, 10)
            });

            var installed = new List<string>();
            foreach (var name in FontCatalog.Names())
                if (!string.IsNullOrEmpty(name) && !AppSettings.IsExcludedFont(name)) installed.Add(name);
            installed.Sort(StringComparer.CurrentCultureIgnoreCase);

            foreach (var family in missing)
            {
                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
                var dot = new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = Chrome.Danger, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                var label = new TextBlock
                {
                    Text = family,
                    Foreground = Chrome.Ink,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 180,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    [ToolTip.TipProperty] = family
                };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);
                var arrow = new TextBlock { Text = "→", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                DockPanel.SetDock(arrow, Dock.Left);
                row.Children.Add(arrow);
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 200 };
                combo.Items.Add(None);
                foreach (var name in installed) combo.Items.Add(name);
                var current = AppSettings.SubstituteFont(family);
                var selected = 0;
                if (!string.Equals(current, family, StringComparison.OrdinalIgnoreCase))
                    for (var i = 1; i < combo.Items.Count; i++)
                        if (string.Equals(combo.Items[i] as string, current, StringComparison.OrdinalIgnoreCase)) { selected = i; break; }
                combo.SelectedIndex = selected;
                row.Children.Add(combo);
                panel.Children.Add(row);
                _choices.Add(new KeyValuePair<string, ComboBox>(family, combo));
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Appliquer", IsDefault = true, MinWidth = 88 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
        }

        /// <summary>Vrai si « Appliquer » : les remplacements sont alors
        /// écrits dans les réglages (une entrée retirée = plus de
        /// remplacement) et enregistrés.</summary>
        public static async Task<bool> Show(Window owner, List<string> missing)
        {
            var dialog = new FontSubstitutionDialog(owner, missing);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return false;
            foreach (var choice in dialog._choices)
            {
                var chosen = choice.Value.SelectedItem as string;
                if (string.IsNullOrEmpty(chosen) || chosen == None) AppSettings.FontSubstitutions.Remove(choice.Key);
                else AppSettings.FontSubstitutions[choice.Key] = chosen;
            }
            AppSettings.Save();
            return true;
        }
    }
}

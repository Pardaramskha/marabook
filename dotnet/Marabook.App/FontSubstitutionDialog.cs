using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Marabook.Model;
using Avalonia.Input;
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

        private FontSubstitutionDialog(Window owner, List<string> missing, Project project)
        {
            var replaced = 0;
            foreach (var family in missing)
                if (!string.Equals(AppSettings.SubstituteFont(family), family, StringComparison.OrdinalIgnoreCase)) replaced++;
            var allReplaced = missing.Count > 0 && replaced == missing.Count;
            Title = allReplaced ? "Polices remplacées" : "Polices manquantes";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 14), MinWidth = 420 };
            panel.Children.Add(new TextBlock
            {
                // Déjà remplacées (10/10, Rémi : rouvrir depuis « Tout va bien »
                // donnait l'impression que rien n'avait été fait) : le dialogue
                // le dit, et chaque ligne remplacée a sa pastille verte.
                Text = allReplaced
                    ? (missing.Count > 1 ? "Les " + missing.Count + " polices manquantes de ce projet sont remplacées — tout va bien. Vous pouvez changer les remplacements."
                        : "La police manquante de ce projet est remplacée — tout va bien. Vous pouvez changer le remplacement.")
                    : (missing.Count > 1 ? missing.Count + " polices demandées par le projet ne sont pas installées sur cet ordinateur."
                        + (replaced > 0 ? " " + replaced + (replaced == 1 ? " est déjà remplacée." : " sont déjà remplacées.") : "")
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
                var substituted = !string.Equals(AppSettings.SubstituteFont(family), family, StringComparison.OrdinalIgnoreCase);
                var dot = new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = substituted ? (IBrush)Chrome.Ok : Chrome.Danger, VerticalAlignment = VerticalAlignment.Center,
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
                // Où elle manque (09/10) : un accordéon replié sous la ligne,
                // les styles puis les écrits et fiches qui la demandent.
                var users = FontAudit.UsersOf(project, family);
                if (users.Count > 0)
                {
                    var list = new StackPanel { Margin = new Thickness(16, 2, 0, 4), IsVisible = false };
                    foreach (var user in users)
                        list.Children.Add(new TextBlock { Text = user, Foreground = Chrome.SoftText, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 440 });
                    var caption = users.Count == 1 ? "1 emplacement" : users.Count + " emplacements";
                    var toggle = new TextBlock
                    {
                        Text = "› " + caption,
                        Foreground = Chrome.Accent,
                        FontSize = 12,
                        Margin = new Thickness(16, 0, 0, 2),
                        Cursor = new Cursor(StandardCursorType.Hand),
                        [ToolTip.TipProperty] = "Les styles, écrits et fiches qui demandent cette police"
                    };
                    toggle.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                    {
                        if (!e.GetCurrentPoint(toggle).Properties.IsLeftButtonPressed) return;
                        e.Handled = true;
                        list.IsVisible = !list.IsVisible;
                        toggle.Text = (list.IsVisible ? "⌄ " : "› ") + caption;
                    };
                    panel.Children.Add(toggle);
                    panel.Children.Add(list);
                }
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
        public static Task<bool> Show(Window owner, List<string> missing)
        {
            return Show(owner, missing, null);
        }

        public static async Task<bool> Show(Window owner, List<string> missing, Project project)
        {
            var dialog = new FontSubstitutionDialog(owner, missing, project);
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

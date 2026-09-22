using System;
using System.Windows;
using System.Windows.Controls;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>« Gestion des styles » (Format › Gérer les styles…, bouton Aa
    /// du ruban) : le StylesPanel sur un clone de la feuille, Valider rend
    /// la feuille éditée, Annuler rend null. Depuis le 22/09 le panneau porte
    /// la portée des styles (global, livre, document) : le contexte dit où
    /// l'on se trouve.</summary>
    public class StylesDialog : Window
    {
        private readonly StylesPanel _panel;
        private bool _accepted;

        private StylesDialog(Window owner, StyleSheet source, StyleScopeContext context)
        {
            Title = "Gestion des styles";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 760;
            Height = 580;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new DockPanel { Margin = new Thickness(14) };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 90 };
            ok.Click += delegate { _panel.Commit(); _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _panel = new StylesPanel(source.Clone(), context);
            root.Children.Add(_panel);
            Content = root;
        }

        public static StyleSheet Show(Window owner, StyleSheet source)
        {
            return Show(owner, source, StyleScopeContext.GlobalOnly());
        }

        public static StyleSheet Show(Window owner, StyleSheet source, StyleScopeContext context)
        {
            var dialog = new StylesDialog(owner, source, context);
            Dialogs.ShowModal(dialog);
            return dialog._accepted ? dialog._panel.Sheet : null;
        }
    }
}

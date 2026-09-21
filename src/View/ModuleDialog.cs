using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>La fiche d'un module (DLC, 22/09) : son nom, son titre, ce
    /// qu'il apporte, son état — et le geste : « Installer » (téléchargé
    /// depuis GitHub, installé, prêt sans redémarrage), ou « Désinstaller ».
    /// Ouverte depuis l'accueil et depuis Préférences › DLC.</summary>
    public class ModuleDialog : Window
    {
        private readonly ModuleState _state;
        private readonly TextBlock _status;
        private readonly Button _install, _remove;

        private ModuleDialog(Window owner, ModuleState state)
        {
            _state = state;
            Title = state.Source.Name;
            if (owner != null && owner.IsVisible)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(22, 18, 22, 16), Width = 460 };
            panel.Children.Add(new TextBlock
            {
                Text = state.Source.Name,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Chrome.Ink
            });
            if (state.Source.Title.Length > 0)
                panel.Children.Add(new TextBlock
                {
                    Text = state.Source.Title,
                    FontSize = 13,
                    Foreground = Chrome.SoftText,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            panel.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 12, 0, 12) });
            panel.Children.Add(new TextBlock { Text = "Ce module apporte", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Chrome.Accent });
            foreach (var feature in state.Source.Features)
            {
                var line = new TextBlock { FontSize = 12.5, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
                line.Inlines.Add(new Run("•  ") { Foreground = Chrome.Accent });
                line.Inlines.Add(new Run(feature));
                panel.Children.Add(line);
            }
            panel.Children.Add(new TextBlock
            {
                Text = "Un module ne modifie rien tant qu'il n'est pas installé ; désinstallé, les fiches gardent leurs valeurs (invisibles) et les succès obtenus restent acquis. Aucun redémarrage n'est nécessaire.",
                FontSize = 11,
                Foreground = Chrome.SoftText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 12, 0, 0)
            });
            _status = new TextBlock { FontSize = 12, Foreground = Chrome.SoftText, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            _install = Buttons.Text("Installer", "Télécharge le paquet publié sur GitHub et l'installe", Buttons.Bar, Buttons.Look.Primary);
            _install.Click += delegate { Install(); };
            buttons.Children.Add(_install);
            _remove = Buttons.Text("Désinstaller", "Retire le module ; les valeurs saisies restent dans le projet", Buttons.Bar, Buttons.Look.Outline);
            _remove.Margin = new Thickness(8, 0, 0, 0);
            _remove.Click += delegate
            {
                var answer = MessageDialog.Show(this,
                    "Désinstaller " + state.Source.Name + " ?\n\nLes fiches gardent leurs valeurs, invisibles jusqu'à une réinstallation.",
                    "Modules", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                ModuleStore.Uninstall(_state);
                Sync();
            };
            buttons.Children.Add(_remove);
            var close = new Button { Content = "Fermer", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            close.Click += delegate { Close(); };
            buttons.Children.Add(close);
            panel.Children.Add(buttons);
            Content = panel;
            ModuleStore.Changed += Sync;
            Closed += delegate { ModuleStore.Changed -= Sync; };
            Sync();
        }

        public static void Show(Window owner, ModuleState state)
        {
            if (state == null) return;
            var dialog = new ModuleDialog(owner, state);
            Dialogs.ShowModal(dialog);
        }

        private void Sync()
        {
            _status.Text = "État : " + _state.Label;
            _install.IsEnabled = _state.IsAvailable && !_state.Busy && (!_state.IsInstalled || _state.HasUpdate);
            _install.Content = _state.HasUpdate ? "Mettre à jour" : "Installer";
            _remove.IsEnabled = _state.IsInstalled && !_state.Busy;
        }

        private void Install()
        {
            var answer = MessageDialog.Show(this,
                "Installer " + _state.Source.Name + " " + (_state.Latest == null ? "" : _state.Latest.Version) + " ?\n\n"
                + "Le paquet est téléchargé depuis GitHub et installé pour cet ordinateur.",
                "Modules", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            ModuleStore.Install(_state, Dispatcher, delegate(string failure)
            {
                if (failure != null) _status.Text = "Installation impossible : " + failure;
                else _status.Text = "État : " + _state.Label + " — prêt, sans redémarrage.";
                Sync();
            });
        }
    }
}

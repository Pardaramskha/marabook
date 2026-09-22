using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Marabook.View
{
    /// <summary>Aide › Rapports de plantage (22/09) : la liste des rapports
    /// écrits par CrashReport, le texte de celui qu'on choisit, et de quoi le
    /// copier, ouvrir le dossier ou tout effacer.</summary>
    public class CrashReportsDialog : Window
    {
        private readonly ListBox _list = new ListBox();
        private readonly TextBox _preview;
        private readonly Button _copy, _clear;
        private List<string> _paths = new List<string>();

        private CrashReportsDialog(Window owner)
        {
            Title = "Rapports de plantage";
            if (owner != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            Width = 760;
            Height = 500;
            MinWidth = 520;
            MinHeight = 320;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new DockPanel { Margin = new Thickness(18, 16, 18, 14) };
            var intro = new TextBlock
            {
                Text = "Quand Marabook rencontre une erreur, il écrit un rapport ici. Copiez-le et joignez-le à votre "
                    + "signalement : il décrit l'erreur et le contexte, jamais le contenu de vos écrits.",
                Foreground = Chrome.Ink,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            var folder = Buttons.Text("Ouvrir le dossier", "Le dossier des rapports dans l'Explorateur", Buttons.Bar, Buttons.Look.Calm);
            folder.Click += delegate { try { Process.Start("explorer.exe", CrashReport.Folder); } catch { } };
            left.Children.Add(folder);
            _clear = Buttons.Text("Tout effacer", "Supprime tous les rapports", Buttons.Bar, Buttons.Look.Calm);
            _clear.Margin = new Thickness(6, 0, 0, 0);
            _clear.Click += delegate
            {
                var answer = MessageDialog.Show(this, "Effacer tous les rapports de plantage ?", Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                CrashReport.DeleteAll();
                Refresh();
            };
            left.Children.Add(_clear);
            DockPanel.SetDock(left, Dock.Left);
            buttons.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _copy = Buttons.Text("Copier le rapport", "Copie le texte du rapport choisi dans le presse-papiers", Buttons.Bar, Buttons.Look.Primary);
            _copy.Click += delegate
            {
                try { Clipboard.SetText(_preview.Text); } catch { }
            };
            var close = Buttons.Text("Fermer", null, Buttons.Bar, Buttons.Look.Calm);
            close.IsCancel = true;
            close.Margin = new Thickness(8, 0, 0, 0);
            close.Click += delegate { Close(); };
            right.Children.Add(_copy);
            right.Children.Add(close);
            buttons.Children.Add(right);
            root.Children.Add(buttons);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _list.SelectionChanged += delegate { ShowSelected(); };
            grid.Children.Add(_list);
            _preview = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            };
            Grid.SetColumn(_preview, 2);
            grid.Children.Add(_preview);
            root.Children.Add(grid);
            Content = root;
            Refresh();
        }

        private void Refresh()
        {
            _paths = CrashReport.List();
            _list.Items.Clear();
            foreach (var path in _paths) _list.Items.Add(CrashReport.Label(path));
            if (_paths.Count > 0) _list.SelectedIndex = 0;
            else
            {
                _preview.Text = "Aucun rapport de plantage — tant mieux.";
                _copy.IsEnabled = false;
            }
            _clear.IsEnabled = _paths.Count > 0;
        }

        private void ShowSelected()
        {
            var index = _list.SelectedIndex;
            if (index < 0 || index >= _paths.Count) return;
            try { _preview.Text = File.ReadAllText(_paths[index]); }
            catch (Exception failure) { _preview.Text = "Rapport illisible : " + failure.Message; }
            _copy.IsEnabled = true;
        }

        public static void Show(Window owner)
        {
            Dialogs.ShowModal(new CrashReportsDialog(owner));
        }
    }
}

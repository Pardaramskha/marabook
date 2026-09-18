using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>« Personnages détectés » (b49) : les noms propres relevés
    /// dans un texte importé, à cocher — une fiche Personnage par nom
    /// retenu. Rien n'est créé sans un clic sur le bouton principal.</summary>
    public class CharactersDialog : Window
    {
        private readonly List<CheckBox> _boxes = new List<CheckBox>();
        private readonly List<NameCandidate> _candidates;
        private readonly Button _create;
        private bool _accepted;

        private CharactersDialog(Window owner, string sourceTitle, List<NameCandidate> candidates)
        {
            _candidates = candidates;
            Title = "Personnages détectés";
            if (owner != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            Width = 560;
            Height = 520;
            MinWidth = 420;
            MinHeight = 320;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new DockPanel { Margin = new Thickness(18, 16, 18, 14) };
            var intro = new TextBlock
            {
                Text = "Ces noms reviennent dans « " + sourceTitle + " » sans figurer parmi vos fiches. "
                    + "Cochez ceux qui sont des personnages : une fiche Personnage sera créée pour chacun.",
                Foreground = Chrome.Ink,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var all = Buttons.Text("Tout cocher", "Cocher tous les noms", Buttons.Bar, Buttons.Look.Calm);
            all.Click += delegate { foreach (var box in _boxes) box.IsChecked = true; };
            var none = Buttons.Text("Tout décocher", "Décocher tous les noms", Buttons.Bar, Buttons.Look.Calm);
            none.Margin = new Thickness(6, 0, 0, 0);
            none.Click += delegate { foreach (var box in _boxes) box.IsChecked = false; };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(all);
            left.Children.Add(none);
            DockPanel.SetDock(left, Dock.Left);
            buttons.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _create = Buttons.Text("Créer les fiches", null, Buttons.Bar, Buttons.Look.Primary);
            _create.IsDefault = true;
            _create.Click += delegate { _accepted = true; Close(); };
            var cancel = Buttons.Text("Ignorer", "Ne créer aucune fiche", Buttons.Bar, Buttons.Look.Calm);
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.Click += delegate { Close(); };
            right.Children.Add(_create);
            right.Children.Add(cancel);
            buttons.Children.Add(right);
            root.Children.Add(buttons);

            var list = new StackPanel();
            foreach (var candidate in candidates)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var box = new CheckBox { IsChecked = true, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 10, 0) };
                box.Checked += delegate { RefreshButton(); };
                box.Unchecked += delegate { RefreshButton(); };
                _boxes.Add(box);
                DockPanel.SetDock(box, Dock.Left);
                row.Children.Add(box);
                var texts = new StackPanel();
                var head = new TextBlock { TextWrapping = TextWrapping.Wrap };
                head.Inlines.Add(new System.Windows.Documents.Run(candidate.Name)
                {
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Foreground = Chrome.Ink
                });
                head.Inlines.Add(new System.Windows.Documents.Run("   × " + candidate.Count)
                {
                    FontSize = 12,
                    Foreground = Chrome.SoftText
                });
                texts.Children.Add(head);
                if (!string.IsNullOrEmpty(candidate.Context))
                    texts.Children.Add(new TextBlock
                    {
                        Text = candidate.Context,
                        Foreground = Chrome.SoftText,
                        FontSize = 12,
                        FontStyle = FontStyles.Italic,
                        TextWrapping = TextWrapping.Wrap
                    });
                row.Children.Add(texts);
                list.Children.Add(row);
            }
            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list
            });
            Content = root;
            RefreshButton();
        }

        private void RefreshButton()
        {
            var count = 0;
            foreach (var box in _boxes) if (box.IsChecked == true) count++;
            _create.Content = count == 0 ? "Créer les fiches" : count == 1 ? "Créer 1 fiche" : "Créer " + count + " fiches";
            _create.IsEnabled = count > 0;
        }

        /// <summary>Les noms retenus, ou null si rien n'est à créer.</summary>
        public static List<string> Ask(Window owner, string sourceTitle, List<NameCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            var dialog = new CharactersDialog(owner, sourceTitle, candidates);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return null;
            var chosen = new List<string>();
            for (var i = 0; i < dialog._boxes.Count; i++)
                if (dialog._boxes[i].IsChecked == true) chosen.Add(dialog._candidates[i].Name);
            return chosen.Count == 0 ? null : chosen;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Un nom retenu par l'indexeur et la catégorie de fiche qu'il
    /// recevra.</summary>
    public class NameChoice
    {
        public string Name = "";
        public SheetCategory Category;
    }

    /// <summary>L'« Indexeur de noms propres » (b49, refondu le 22/09) : les
    /// noms propres relevés dans un texte, à cocher, chacun avec la NATURE de
    /// la fiche à créer (Personnage, Lieu, Objet… — les catégories du projet).
    /// Rien n'est créé sans un clic sur le bouton principal.</summary>
    public class CharactersDialog : Window
    {
        private readonly List<CheckBox> _boxes = new List<CheckBox>();
        private readonly List<ComboBox> _kinds = new List<ComboBox>();
        private readonly List<NameCandidate> _candidates;
        private readonly List<SheetCategory> _categories;
        private readonly Button _create;
        private bool _accepted;

        private CharactersDialog(Window owner, string sourceTitle, List<NameCandidate> candidates,
            List<SheetCategory> categories, SheetCategory preferred)
        {
            _candidates = candidates;
            _categories = categories;
            Title = "Indexeur de noms propres";
            if (owner != null)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            Width = 620;
            Height = 540;
            MinWidth = 480;
            MinHeight = 320;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var root = new DockPanel { Margin = new Thickness(18, 16, 18, 14) };
            var intro = new TextBlock
            {
                Text = "Ces noms reviennent dans « " + sourceTitle + " » sans figurer parmi vos fiches. "
                    + "Cochez ceux qui méritent une fiche et choisissez sa nature : une fiche sera créée pour chacun.",
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

            var preferredIndex = preferred == null ? 0 : Math.Max(0, categories.IndexOf(preferred));
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

                // La nature de la fiche, à droite : les catégories du projet,
                // Personnage (ou la première) proposée.
                var kind = new ComboBox
                {
                    Width = 150,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(12, 0, 0, 0),
                    ToolTip = "Nature de la fiche à créer"
                };
                foreach (var category in categories) kind.Items.Add(category.Name);
                kind.SelectedIndex = categories.Count > 0 ? preferredIndex : -1;
                var boxRef = box;
                kind.SelectionChanged += delegate { if (boxRef.IsChecked != true) boxRef.IsChecked = true; };
                _kinds.Add(kind);
                DockPanel.SetDock(kind, Dock.Right);
                row.Children.Add(kind);

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

        /// <summary>Les noms retenus avec leur catégorie, ou null si rien n'est
        /// à créer. preferred : la catégorie proposée d'office (Personnage).</summary>
        public static List<NameChoice> Ask(Window owner, string sourceTitle, List<NameCandidate> candidates,
            List<SheetCategory> categories, SheetCategory preferred)
        {
            if (candidates == null || candidates.Count == 0) return null;
            if (categories == null) categories = new List<SheetCategory>();
            var dialog = new CharactersDialog(owner, sourceTitle, candidates, categories, preferred);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return null;
            var chosen = new List<NameChoice>();
            for (var i = 0; i < dialog._boxes.Count; i++)
            {
                if (dialog._boxes[i].IsChecked != true) continue;
                var index = dialog._kinds[i].SelectedIndex;
                chosen.Add(new NameChoice
                {
                    Name = dialog._candidates[i].Name,
                    Category = index >= 0 && index < categories.Count ? categories[index] : preferred
                });
            }
            return chosen.Count == 0 ? null : chosen;
        }
    }
}

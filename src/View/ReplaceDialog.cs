using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>La PRÉVISUALISATION obligatoire d'un remplacement projet
    /// (batch 37, lot C) : combien d'occurrences, dans combien d'items, la
    /// liste — chaque item avec sa case (décocher = l'épargner), chaque
    /// occurrence avec son extrait et le texte qu'elle deviendra —, ce qui
    /// est écarté (passages « ne pas corriger », ligatures coupées), et
    /// l'avertissement que l'historique d'annulation du document ouvert sera
    /// réinitialisé. « Remplacer » rend les occurrences retenues ; « Annuler »
    /// rend null.</summary>
    public class ReplacePreviewDialog : Window
    {
        private readonly List<SearchHit> _hits;
        private readonly List<CheckBox> _checks = new List<CheckBox>();
        private readonly List<BinderItem> _checkItems = new List<BinderItem>();
        private readonly TextBlock _summary;
        private readonly TextBlock _warning;
        private readonly Button _replace;
        private List<SearchHit> _chosen;

        public ReplacePreviewDialog(Window owner, SearchResult result, SearchQuery query, string replacement, BinderItem current)
        {
            Owner = owner;
            Title = "Remplacer dans le projet";
            Width = 640;
            Height = 560;
            MinWidth = 420;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            _hits = result.Hits;
            replacement = replacement ?? "";

            var root = new DockPanel { Margin = new Thickness(16) };

            // — Le pied : avertissements, récapitulatif, boutons.
            var foot = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(foot, Dock.Bottom);
            _warning = new TextBlock
            {
                Foreground = Chrome.Accent,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = Visibility.Collapsed
            };
            foot.Children.Add(_warning);
            var skipped = new List<string>();
            if (result.NoProofCount > 0)
                skipped.Add(result.NoProofCount + (result.NoProofCount == 1 ? " occurrence" : " occurrences")
                    + " dans des passages « ne pas corriger » — non remplacée" + (result.NoProofCount == 1 ? "" : "s"));
            if (result.InexactCount > 0)
                skipped.Add(result.InexactCount + (result.InexactCount == 1 ? " occurrence" : " occurrences")
                    + " sur une ligature (« o » dans « œ ») — non remplaçable" + (result.InexactCount == 1 ? "" : "s"));
            if (skipped.Count > 0)
                foot.Children.Add(new TextBlock
                {
                    Text = string.Join("\n", skipped.ToArray()),
                    Foreground = Chrome.SoftText,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            var touchesCurrent = false;
            foreach (var hit in _hits) if (hit.Item == current && hit.Replaceable) { touchesCurrent = true; break; }
            if (touchesCurrent && current != null && (current.Kind == ItemKind.Text || current.Kind == ItemKind.Sheet))
            {
                _warning.Text = "Le document ouvert est concerné : son historique d'annulation sera réinitialisé "
                    + "(le remplacement entier s'annule ensuite en un seul Ctrl+Z).";
                _warning.Visibility = Visibility.Visible;
            }
            _summary = new TextBlock { Foreground = Chrome.Ink, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            foot.Children.Add(_summary);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Annuler", Padding = new Thickness(14, 4, 14, 4), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += delegate { _chosen = null; DialogResult = false; };
            buttons.Children.Add(cancel);
            _replace = new Button { Content = "Remplacer", Padding = new Thickness(14, 4, 14, 4), IsDefault = true };
            _replace.Click += delegate { _chosen = Chosen(); DialogResult = true; };
            buttons.Children.Add(_replace);
            foot.Children.Add(buttons);
            root.Children.Add(foot);

            // — La tête : ce qu'on remplace par quoi.
            var head = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = Chrome.Ink };
            head.Inlines.Add(new Run("Remplacer « "));
            head.Inlines.Add(new Run(query.Pattern) { FontWeight = FontWeights.SemiBold });
            head.Inlines.Add(new Run(query.UseRegex ? " » (expression régulière) par « " : " » par « "));
            head.Inlines.Add(new Run(replacement) { FontWeight = FontWeights.SemiBold });
            head.Inlines.Add(new Run(" »" + (query.UseRegex && replacement.IndexOf('$') >= 0 ? " — les groupes ($1, ${nom}) sont résolus par occurrence" : "")));
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            // — La liste : items cochables, occurrences avec avant → après.
            var list = new StackPanel();
            BinderItem group = null;
            var counts = new Dictionary<BinderItem, int>();
            foreach (var hit in _hits)
                if (hit.Replaceable)
                {
                    int n;
                    counts.TryGetValue(hit.Item, out n);
                    counts[hit.Item] = n + 1;
                }
            foreach (var hit in _hits)
            {
                if (hit.Item != group)
                {
                    group = hit.Item;
                    int n;
                    counts.TryGetValue(hit.Item, out n);
                    var check = new CheckBox
                    {
                        IsChecked = n > 0,
                        IsEnabled = n > 0,
                        Margin = new Thickness(0, 8, 0, 3),
                        Content = new TextBlock
                        {
                            Text = (hit.Item.IsCategory ? "Dictionnaire" : hit.Item.Title) + "  ·  " + (n == 1 ? "1 occurrence" : n + " occurrences"),
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 12,
                            Foreground = Chrome.Ink
                        },
                        ToolTip = "Décocher pour épargner cet item"
                    };
                    check.Checked += delegate { UpdateSummary(); };
                    check.Unchecked += delegate { UpdateSummary(); };
                    _checks.Add(check);
                    _checkItems.Add(hit.Item);
                    list.Children.Add(check);
                }
                var row = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 0, 0, 3), Foreground = hit.Replaceable ? Chrome.Ink : Chrome.SoftText };
                var text = hit.Excerpt ?? "";
                var start = Math.Max(0, Math.Min(hit.ExcerptStart, text.Length));
                var length = Math.Max(0, Math.Min(hit.ExcerptLength, text.Length - start));
                if (start > 0) row.Inlines.Add(new Run(text.Substring(0, start)));
                row.Inlines.Add(new Run(text.Substring(start, length)) { FontWeight = FontWeights.Bold, Foreground = Chrome.Accent });
                if (start + length < text.Length) row.Inlines.Add(new Run(text.Substring(start + length)));
                if (hit.Replaceable)
                {
                    var matched = hit.Field.Text.Substring(hit.Start, Math.Min(hit.Length, hit.Field.Text.Length - hit.Start));
                    row.Inlines.Add(new Run("   →  « " + query.ReplacementFor(matched, replacement) + " »") { Foreground = Chrome.SoftText });
                }
                else row.Inlines.Add(new Run(hit.NoProof ? "   (ne pas corriger)" : "   (ligature)") { Foreground = Chrome.SoftText, FontStyle = FontStyles.Italic });
                row.Inlines.Add(new Run("   " + hit.Field.Label) { Foreground = Chrome.SoftText, FontSize = 10 });
                list.Children.Add(row);
            }
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = list,
                Background = Chrome.PaperBg,
                Padding = new Thickness(10, 4, 10, 8)
            };
            root.Children.Add(new Border { BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = scroll });
            Content = root;
            UpdateSummary();
        }

        /// <summary>Les occurrences remplaçables des items cochés.</summary>
        private List<SearchHit> Chosen()
        {
            var chosen = new List<SearchHit>();
            var excluded = new HashSet<BinderItem>();
            for (var i = 0; i < _checks.Count; i++)
                if (_checks[i].IsChecked != true) excluded.Add(_checkItems[i]);
            foreach (var hit in _hits)
                if (hit.Replaceable && !excluded.Contains(hit.Item)) chosen.Add(hit);
            return chosen;
        }

        private void UpdateSummary()
        {
            var chosen = Chosen();
            var items = new HashSet<BinderItem>();
            foreach (var hit in chosen) items.Add(hit.Item);
            _summary.Text = chosen.Count == 0 ? "Rien à remplacer."
                : "Remplacer " + (chosen.Count == 1 ? "1 occurrence" : chosen.Count + " occurrences")
                + " dans " + (items.Count == 1 ? "1 item" : items.Count + " items") + ".";
            _replace.IsEnabled = chosen.Count > 0;
            _replace.Content = chosen.Count == 0 ? "Remplacer" : "Remplacer " + chosen.Count;
        }

        public int CheckedItems
        {
            get { var n = 0; foreach (var check in _checks) if (check.IsChecked == true) n++; return n; }
        }

        public string Summary { get { return _summary.Text; } }
        public bool WarnsCurrent { get { return _warning.Visibility == Visibility.Visible; } }

        /// <summary>Montre la prévisualisation ; rend les occurrences retenues,
        /// ou null si l'auteur renonce.</summary>
        public static List<SearchHit> Ask(Window owner, SearchResult result, SearchQuery query, string replacement, BinderItem current)
        {
            var dialog = new ReplacePreviewDialog(owner, result, query, replacement, current);
            var ok = Dialogs.ShowModal(dialog);
            return ok == true ? dialog._chosen : null;
        }
    }
}

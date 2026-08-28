using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using UniversSale.Correction;
using UniversSale.Settings;

namespace UniversSale.View
{
    /// <summary>« Options » de la passe typographique (batch 34) : le
    /// préréglage et les règles de Typonanny, sous leurs libellés
    /// d'origine (° = ignorée par le préréglage Minimal).</summary>
    public class TypographyOptionsDialog : Window
    {
        private readonly RadioButton _in, _souple, _minimal;
        private readonly List<KeyValuePair<CheckBox, string>> _rules = new List<KeyValuePair<CheckBox, string>>();
        private bool _accepted;

        private TypographyOptionsDialog(Window owner, TypographyOptions options)
        {
            Title = "Options de la passe typographique";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.WindowBg;

            var panel = new StackPanel { Margin = new Thickness(16), Width = 440 };
            panel.Children.Add(new TextBlock
            {
                Text = "Préréglage",
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            _in = Radio("Imprimerie nationale (strict) — fine avant ; ! ?, pleine avant : et dans « »", options.Preset == "in");
            _souple = Radio("Souple / maison — fine insécable partout", options.Preset == "souple");
            _minimal = Radio("Minimal — évidences seules (apostrophes, …, espaces)", options.Preset == "minimal");
            panel.Children.Add(_in);
            panel.Children.Add(_souple);
            panel.Children.Add(_minimal);

            panel.Children.Add(new TextBlock
            {
                Text = "Règles (le préréglage Minimal ignore celles marquées °)",
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 4)
            });
            Rule(panel, "Espaces (doubles, fins de ligne)", "spaces", options.Spaces);
            Rule(panel, "Apostrophes courbes ’", "apostrophes", options.Apostrophes);
            Rule(panel, "Points de suspension … et « etc. »", "ellipses", options.Ellipses);
            Rule(panel, "Guillemets français « » °", "quotes", options.Quotes);
            Rule(panel, "Tirets de dialogue — °", "dialogueDashes", options.DialogueDashes);
            Rule(panel, "Intervalles 1914–1918 °", "ranges", options.Ranges);
            Rule(panel, "Insécables de ponctuation ; ! ? : « » °", "noBreakPunctuation", options.NoBreakPunctuation);
            Rule(panel, "Insécables d'unités 10 %, 10 €, 12 kg °", "noBreakUnits", options.NoBreakUnits);
            Rule(panel, "Milliers en fine 10 000 °", "thousands", options.Thousands);
            Rule(panel, "Ligatures œ (liste blanche)", "ligaturesOe", options.LigaturesOe);
            Rule(panel, "Ligatures æ (liste blanche)", "ligaturesAe", options.LigaturesAe);
            Rule(panel, "Dimensions 10 × 15 °", "dimensions", options.Dimensions);
            Rule(panel, "Ordinaux 2ème → 2e", "ordinals", options.Ordinals);
            Rule(panel, "Signaler les majuscules à accentuer (État, À…)", "flagCapitals", options.FlagCapitals);

            panel.Children.Add(new TextBlock
            {
                Text = "La passe s'applique à tout l'écrit ouvert ; les passages « ne pas corriger », "
                    + "les liens, le code et les heures sont laissés tels quels. Une fenêtre "
                    + "comparative montre chaque changement avant d'appliquer.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
        }

        private static RadioButton Radio(string label, bool isChecked)
        {
            return new RadioButton
            {
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Foreground = Chrome.Ink },
                IsChecked = isChecked,
                GroupName = "preset",
                Margin = new Thickness(0, 2, 0, 2)
            };
        }

        private void Rule(StackPanel panel, string label, string key, bool value)
        {
            var box = new CheckBox
            {
                Content = new TextBlock { Text = label, Foreground = Chrome.Ink },
                IsChecked = value,
                Margin = new Thickness(0, 2, 0, 2)
            };
            _rules.Add(new KeyValuePair<CheckBox, string>(box, key));
            panel.Children.Add(box);
        }

        private TypographyOptions Build()
        {
            var node = new Dictionary<string, object>();
            node["preset"] = _souple.IsChecked == true ? "souple" : _minimal.IsChecked == true ? "minimal" : "in";
            foreach (var rule in _rules) node[rule.Value] = rule.Key.IsChecked == true;
            return TypographyOptions.FromJson(node);
        }

        /// <summary>Vrai si validé : AppSettings.Typography est mis à jour et enregistré.</summary>
        public static bool Ask(Window owner)
        {
            var dialog = new TypographyOptionsDialog(owner, AppSettings.Typography);
            dialog.ShowDialog();
            if (!dialog._accepted) return false;
            AppSettings.Typography = dialog.Build();
            AppSettings.Save();
            return true;
        }
    }

    /// <summary>La fenêtre comparative de la passe typographique : AVANT à
    /// gauche, APRÈS à droite, en miroir (défilement synchronisé), chaque
    /// paragraphe modifié avec ses suppressions (rouge barré) et ses
    /// insertions (vert) ; les compteurs et signalements en tête ;
    /// « Appliquer » écrit dans l'écrit (annulable).</summary>
    public class TypographyCompareWindow : Window
    {
        private static readonly Brush DeletedBg = new SolidColorBrush(Color.FromArgb(0x55, 0xE7, 0x4C, 0x3C));
        private static readonly Brush InsertedBg = new SolidColorBrush(Color.FromArgb(0x55, 0x2E, 0xCC, 0x71));
        private bool _accepted;
        private bool _syncing;

        private TypographyCompareWindow(Window owner, TypographyPassResult result, string title)
        {
            Title = "Passe typographique — " + title;
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = Math.Max(760, Math.Min(1280, owner == null ? 1000 : owner.ActualWidth - 120));
            Height = Math.Max(480, Math.Min(900, owner == null ? 700 : owner.ActualHeight - 120));
            Background = Chrome.WindowBg;

            var root = new DockPanel { Margin = new Thickness(14) };

            // — Tête : le bilan.
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var summary = new System.Text.StringBuilder();
            summary.Append(result.Changes.Count).Append(result.Changes.Count > 1 ? " paragraphes modifiés" : " paragraphe modifié");
            foreach (var counter in result.Summary.Counters)
                summary.Append("   ·   ").Append(counter.Value).Append(' ').Append(counter.Key);
            if (result.Skipped > 0)
                summary.Append("   ·   ").Append(result.Skipped).Append(" paragraphe(s) trop remanié(s), laissé(s) tels quels");
            head.Children.Add(new TextBlock
            {
                Text = summary.ToString(),
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var warning in result.Summary.Warnings)
                head.Children.Add(new TextBlock
                {
                    Text = "⚠ " + warning,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            // — Pied : les boutons.
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var apply = new Button { Content = "Appliquer", IsDefault = true, MinWidth = 100, FontWeight = FontWeights.SemiBold };
            apply.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // — Le miroir : deux colonnes, défilement lié.
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var left = Column("Avant", result, false);
            var right = Column("Après", result, true);
            Grid.SetColumn(left.Key, 0);
            Grid.SetColumn(right.Key, 2);
            grid.Children.Add(left.Key);
            grid.Children.Add(right.Key);
            var leftScroll = left.Value;
            var rightScroll = right.Value;
            leftScroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
            {
                if (_syncing) return;
                _syncing = true;
                rightScroll.ScrollToVerticalOffset(leftScroll.VerticalOffset);
                _syncing = false;
            };
            rightScroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
            {
                if (_syncing) return;
                _syncing = true;
                leftScroll.ScrollToVerticalOffset(rightScroll.VerticalOffset);
                _syncing = false;
            };
            root.Children.Add(grid);
            Content = root;
        }

        private static KeyValuePair<UIElement, ScrollViewer> Column(string caption, TypographyPassResult result, bool after)
        {
            var panel = new DockPanel();
            var header = new TextBlock
            {
                Text = caption,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);
            var stack = new StackPanel { Margin = new Thickness(12) };
            foreach (var change in result.Changes)
                stack.Children.Add(Paragraph(change, after));
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border
                {
                    Background = Chrome.PaperBg,
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Child = stack
                }
            };
            panel.Children.Add(scroll);
            return new KeyValuePair<UIElement, ScrollViewer>(panel, scroll);
        }

        private static UIElement Paragraph(TypographyParagraphChange change, bool after)
        {
            var block = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Chrome.PaperInk,
                FontFamily = new FontFamily("Georgia"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            };
            block.Inlines.Add(new Run("¶ " + (change.Index + 1) + "   ")
            {
                Foreground = Chrome.PaperSoftInk,
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI")
            });
            var buffer = new System.Text.StringBuilder();
            var marked = false;
            foreach (var op in change.Ops)
            {
                var visible = after ? op.Type != '-' : op.Type != '+';
                if (!visible) continue;
                var changed = op.Type != ' ';
                if (changed != marked)
                {
                    Flush(block, buffer, marked, after);
                    marked = changed;
                }
                buffer.Append(Display(op.Char));
            }
            Flush(block, buffer, marked, after);
            return block;
        }

        private static void Flush(TextBlock block, System.Text.StringBuilder buffer, bool marked, bool after)
        {
            if (buffer.Length == 0) return;
            var run = new Run(buffer.ToString());
            if (marked)
            {
                run.Background = after ? InsertedBg : DeletedBg;
                if (!after) run.TextDecorations = TextDecorations.Strikethrough;
                run.FontWeight = FontWeights.SemiBold;
            }
            block.Inlines.Add(run);
            buffer.Length = 0;
        }

        /// <summary>Les invisibles deviennent lisibles : espace insécable ⍽,
        /// fine ⸱, élément ◌.</summary>
        private static string Display(char c)
        {
            switch (c)
            {
                case '\u00A0': return "␣";
                case '\u202F': return "⸱";
                case '\uFFFC': return "◌";
                default: return c.ToString();
            }
        }

        public static bool Ask(Window owner, TypographyPassResult result, string title)
        {
            var window = new TypographyCompareWindow(owner, result, title);
            window.ShowDialog();
            return window._accepted;
        }
    }
}

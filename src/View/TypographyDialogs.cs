using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Marabook.Correction;
using Marabook.Settings;

namespace Marabook.View
{
    /// <summary>« Options » de la passe typographique (batch 34) : le
    /// préréglage et les règles de Typonanny, sous leurs libellés
    /// d'origine (° = ignorée par le préréglage Minimal).</summary>
    public class TypographyOptionsDialog : Window
    {
        private readonly RadioButton _in, _souple, _minimal;
        private readonly List<KeyValuePair<CheckBox, string>> _rules = new List<KeyValuePair<CheckBox, string>>();
        private readonly List<KeyValuePair<CheckBox, string>> _liveRules = new List<KeyValuePair<CheckBox, string>>();
        private readonly CheckBox _liveMaster;
        private readonly Grid _rulesGrid;
        private bool _accepted;

        private TypographyOptionsDialog(Window owner, TypographyOptions options,
            TypographyOptions live, bool liveEnabled)
        {
            Title = "Options de la passe typographique";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

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
            // Deux colonnes (b45) : la passe complète, et la correction AU
            // MOMENT OÙ L'ON TAPE — chaque règle se règle séparément.
            _liveMaster = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = "Corriger aussi au moment où j'écris (dans les pages composées) — "
                        + "Ctrl+Z annule une correction automatique sans effacer la frappe",
                    Foreground = Chrome.Ink,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400
                },
                IsChecked = liveEnabled,
                Margin = new Thickness(0, 2, 0, 6)
            };
            panel.Children.Add(_liveMaster);
            _rulesGrid = new Grid();
            _rulesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _rulesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _rulesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _rulesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var passHead = new TextBlock { Text = "Passe", FontSize = 11, Foreground = Chrome.SoftText, Margin = new Thickness(0, 0, 8, 2) };
            var liveHead = new TextBlock { Text = "À la frappe", FontSize = 11, Foreground = Chrome.SoftText, Margin = new Thickness(0, 0, 8, 2) };
            Grid.SetColumn(liveHead, 1);
            _rulesGrid.Children.Add(passHead);
            _rulesGrid.Children.Add(liveHead);
            panel.Children.Add(_rulesGrid);
            Rule("Espaces (doubles, fins de ligne)", "spaces", options.Spaces, live.Spaces);
            Rule("Apostrophes courbes ’", "apostrophes", options.Apostrophes, live.Apostrophes);
            Rule("Points de suspension … et « etc. »", "ellipses", options.Ellipses, live.Ellipses);
            Rule("Guillemets français « » °", "quotes", options.Quotes, live.Quotes);
            Rule("Tirets de dialogue — °", "dialogueDashes", options.DialogueDashes, live.DialogueDashes);
            Rule("Intervalles 1914–1918 °", "ranges", options.Ranges, live.Ranges);
            Rule("Point médian auteur::ice → auteur·ice", "middleDot", options.MiddleDot, live.MiddleDot);
            Rule("Insécables de ponctuation ; ! ? : « » °", "noBreakPunctuation", options.NoBreakPunctuation, live.NoBreakPunctuation);
            Rule("Insécables d'unités 10 %, 10 €, 12 kg °", "noBreakUnits", options.NoBreakUnits, live.NoBreakUnits);
            Rule("Milliers en fine 10 000 °", "thousands", options.Thousands, live.Thousands);
            Rule("Ligatures œ (liste blanche)", "ligaturesOe", options.LigaturesOe, live.LigaturesOe);
            Rule("Ligatures æ (liste blanche)", "ligaturesAe", options.LigaturesAe, live.LigaturesAe);
            Rule("Dimensions 10 × 15 °", "dimensions", options.Dimensions, live.Dimensions);
            Rule("Ordinaux en exposants 2ème → 2ᵉ, 1er → 1ᵉʳ (comme Grammalecte)", "ordinals", options.Ordinals, live.Ordinals);
            Rule("Signaler les majuscules à accentuer (État, À…)", "flagCapitals", options.FlagCapitals, live.FlagCapitals);

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

        private void Rule(string label, string key, bool value, bool liveValue)
        {
            var row = _rulesGrid.RowDefinitions.Count;
            _rulesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var pass = new CheckBox { IsChecked = value, Margin = new Thickness(6, 3, 14, 3), VerticalAlignment = VerticalAlignment.Center };
            var live = new CheckBox { IsChecked = liveValue, Margin = new Thickness(18, 3, 14, 3), VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBlock { Text = label, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(pass, row);
            Grid.SetRow(live, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(live, 1);
            Grid.SetColumn(text, 2);
            _rulesGrid.Children.Add(pass);
            _rulesGrid.Children.Add(live);
            _rulesGrid.Children.Add(text);
            _rules.Add(new KeyValuePair<CheckBox, string>(pass, key));
            _liveRules.Add(new KeyValuePair<CheckBox, string>(live, key));
        }

        private TypographyOptions Build()
        {
            return Build(_rules);
        }

        private TypographyOptions Build(List<KeyValuePair<CheckBox, string>> rules)
        {
            var node = new Dictionary<string, object>();
            node["preset"] = _souple.IsChecked == true ? "souple" : _minimal.IsChecked == true ? "minimal" : "in";
            foreach (var rule in rules) node[rule.Value] = rule.Key.IsChecked == true;
            return TypographyOptions.FromJson(node);
        }

        /// <summary>Vrai si validé : AppSettings.Typography est mis à jour et enregistré.</summary>
        public static bool Ask(Window owner)
        {
            var dialog = new TypographyOptionsDialog(owner, AppSettings.Typography,
                AppSettings.TypographyLive, AppSettings.TypographyLiveEnabled);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return false;
            AppSettings.TypographyLive = dialog.Build(dialog._liveRules);
            AppSettings.TypographyLiveEnabled = dialog._liveMaster.IsChecked == true;
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
            Background = Chrome.RaisedBg;

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
            Dialogs.ShowModal(window);
            return window._accepted;
        }
    }
}

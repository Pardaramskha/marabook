using System;
using System.Windows;
using System.Windows.Controls;
using UniversSale.Settings;

namespace UniversSale.View
{
    /// <summary>« Options du correcteur » (batch 33) : ce que le correcteur
    /// RELÈVE — orthographe, grammaire, typographie, style — quatre cases.
    /// Seules l'orthographe et la grammaire sont actives par défaut. Les
    /// changements s'appliquent à la validation ; le détail des règles de
    /// Grammalecte reste dans Préférences → Correction.</summary>
    public class ProofOptionsDialog : Window
    {
        private readonly CheckBox _spell, _grammar, _typography, _style;
        // L'étage style (batch 44) : trois sous-cases et la liste des verbes
        // ternes, sous la case Style — grisées quand elle est décochée.
        private readonly CheckBox _repetitions, _adverbs, _dullVerbs;
        private readonly TextBox _dullList;
        private readonly StackPanel _styleDetail;
        private bool _accepted;

        private ProofOptionsDialog(Window owner)
        {
            Title = "Options du correcteur";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), Width = 360 };
            panel.Children.Add(new TextBlock
            {
                Text = "Le correcteur relève :",
                Foreground = Chrome.Ink,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            // Les pastilles (13/09) reprennent la couleur de l'ondulé de
            // chaque relevé : ce que l'auteur voit dans la page.
            _spell = Option("Orthographe",
                "Mots inconnus du dictionnaire (Hunspell) et du dictionnaire personnel",
                AppSettings.SpellEnabled, Dot(Correction.FindingCategory.Spelling, ""));
            _grammar = Option("Grammaire",
                "Accords, conjugaisons, confusions… (Grammalecte, en différé)",
                AppSettings.GrammarEnabled, Dot(Correction.FindingCategory.Grammar, ""));
            _typography = Option("Typographie",
                "Signes, apostrophes, espaces insécables, nombres… (règles typographiques de Grammalecte)",
                AppSettings.TypographyEnabled, Dot(Correction.FindingCategory.Typography, ""));
            _style = Option("Style",
                "Répétitions, adverbes en -ment, verbes ternes — des indices, jamais des fautes",
                AppSettings.StyleEnabled);
            foreach (var box in new[] { _spell, _grammar, _typography, _style })
                panel.Children.Add(box);

            // Les sous-options du style (batch 44), en retrait sous la case.
            _styleDetail = new StackPanel { Margin = new Thickness(22, 0, 0, 4) };
            _repetitions = SubOption("Répétitions",
                "Un mot qui revient à portée d'oreille (rayon en mots, réglé pour le roman)",
                AppSettings.StyleRepetitions, Dot(Correction.FindingCategory.Style, "repetition"));
            _adverbs = SubOption("Adverbes en -ment",
                "« rapidement », « vraiment »… — le dictionnaire tranche, « moment » n'est pas relevé",
                AppSettings.StyleAdverbs,
                Dot(Correction.FindingCategory.Style, Correction.Grammalecte.StyleChecker.AdverbRule));
            _dullVerbs = SubOption("Verbes ternes",
                "Les verbes passe-partout conjugués ; les auxiliaires (« avait mangé ») sont laissés en paix",
                AppSettings.StyleDullVerbs,
                Dot(Correction.FindingCategory.Style, Correction.Grammalecte.StyleChecker.DullVerbRule));
            _styleDetail.Children.Add(_repetitions);
            _styleDetail.Children.Add(_adverbs);
            _styleDetail.Children.Add(_dullVerbs);
            _dullList = new TextBox
            {
                Text = Correction.Grammalecte.StyleChecker.JoinDullVerbs(AppSettings.DullVerbs),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false,
                MinHeight = 44,
                Margin = new Thickness(22, 2, 0, 2),
                ToolTip = "Les infinitifs des verbes ternes, séparés par des virgules"
            };
            _styleDetail.Children.Add(_dullList);
            _dullVerbs.Checked += delegate { _dullList.IsEnabled = true; };
            _dullVerbs.Unchecked += delegate { _dullList.IsEnabled = false; };
            _dullList.IsEnabled = AppSettings.StyleDullVerbs;
            _style.Checked += delegate { _styleDetail.IsEnabled = true; };
            _style.Unchecked += delegate { _styleDetail.IsEnabled = false; };
            _styleDetail.IsEnabled = AppSettings.StyleEnabled;
            panel.Children.Add(_styleDetail);

            panel.Children.Add(new TextBlock
            {
                Text = "Le détail des règles de grammaire et de typographie se règle dans "
                    + "Fichier → Préférences → Correction.",
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

        /// <summary>La pastille d'un relevé : la couleur de son ondulé.</summary>
        private static Border Dot(Correction.FindingCategory category, string ruleId)
        {
            return ComposedRenderer.FindingDot(ComposedRenderer.FindingPen(category, ruleId), 9);
        }

        private static CheckBox SubOption(string label, string detail, bool value, Border dot)
        {
            var box = Option(label, detail, value, dot);
            box.Margin = new Thickness(0, 2, 0, 2);
            // En retrait de 22 px : le détail se replie plus tôt, sinon il
            // déborde du dialogue (vu au rendu du 13/09).
            var content = (StackPanel)box.Content;
            foreach (var child in content.Children)
            {
                var text = child as TextBlock;
                if (text != null) text.MaxWidth = 296;
            }
            return box;
        }

        private static CheckBox Option(string label, string detail, bool value, Border dot = null)
        {
            var content = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            if (dot != null) title.Children.Add(dot);
            title.Children.Add(new TextBlock { Text = label, Foreground = Chrome.Ink });
            content.Children.Add(title);
            content.Children.Add(new TextBlock
            {
                Text = detail,
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320
            });
            return new CheckBox
            {
                Content = content,
                IsChecked = value,
                Margin = new Thickness(0, 4, 0, 4)
            };
        }

        private static bool SameList(System.Collections.Generic.List<string> a,
            System.Collections.Generic.List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>Vrai si les réglages ont changé (déjà enregistrés).</summary>
        public static bool Ask(Window owner)
        {
            var dialog = new ProofOptionsDialog(owner);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return false;
            var spell = dialog._spell.IsChecked == true;
            var grammar = dialog._grammar.IsChecked == true;
            var typography = dialog._typography.IsChecked == true;
            var style = dialog._style.IsChecked == true;
            var repetitions = dialog._repetitions.IsChecked == true;
            var adverbs = dialog._adverbs.IsChecked == true;
            var dullVerbs = dialog._dullVerbs.IsChecked == true;
            var dullList = Correction.Grammalecte.StyleChecker.ParseDullVerbs(dialog._dullList.Text);
            if (dullList.Count == 0)
                dullList = new System.Collections.Generic.List<string>(
                    Correction.Grammalecte.StyleChecker.DefaultDullVerbs);
            var changed = spell != AppSettings.SpellEnabled || grammar != AppSettings.GrammarEnabled
                || typography != AppSettings.TypographyEnabled || style != AppSettings.StyleEnabled
                || repetitions != AppSettings.StyleRepetitions || adverbs != AppSettings.StyleAdverbs
                || dullVerbs != AppSettings.StyleDullVerbs
                || !SameList(dullList, AppSettings.DullVerbs);
            AppSettings.SpellEnabled = spell;
            AppSettings.GrammarEnabled = grammar;
            AppSettings.TypographyEnabled = typography;
            AppSettings.StyleEnabled = style;
            AppSettings.StyleRepetitions = repetitions;
            AppSettings.StyleAdverbs = adverbs;
            AppSettings.StyleDullVerbs = dullVerbs;
            AppSettings.DullVerbs = dullList;
            AppSettings.Save();
            return changed;
        }
    }
}

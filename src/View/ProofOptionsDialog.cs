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
            _spell = Option("Orthographe",
                "Mots inconnus du dictionnaire (Hunspell) et du dictionnaire personnel",
                AppSettings.SpellEnabled);
            _grammar = Option("Grammaire",
                "Accords, conjugaisons, confusions… (Grammalecte, en différé)",
                AppSettings.GrammarEnabled);
            _typography = Option("Typographie",
                "Signes, apostrophes, espaces insécables, nombres… (règles typographiques de Grammalecte)",
                AppSettings.TypographyEnabled);
            _style = Option("Style",
                "Répétitions à portée d'oreille (réglées pour le roman)",
                AppSettings.StyleEnabled);
            foreach (var box in new[] { _spell, _grammar, _typography, _style })
                panel.Children.Add(box);

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

        private static CheckBox Option(string label, string detail, bool value)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = label, Foreground = Chrome.Ink });
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

        /// <summary>Vrai si les réglages ont changé (déjà enregistrés).</summary>
        public static bool Ask(Window owner)
        {
            var dialog = new ProofOptionsDialog(owner);
            dialog.ShowDialog();
            if (!dialog._accepted) return false;
            var spell = dialog._spell.IsChecked == true;
            var grammar = dialog._grammar.IsChecked == true;
            var typography = dialog._typography.IsChecked == true;
            var style = dialog._style.IsChecked == true;
            var changed = spell != AppSettings.SpellEnabled || grammar != AppSettings.GrammarEnabled
                || typography != AppSettings.TypographyEnabled || style != AppSettings.StyleEnabled;
            AppSettings.SpellEnabled = spell;
            AppSettings.GrammarEnabled = grammar;
            AppSettings.TypographyEnabled = typography;
            AppSettings.StyleEnabled = style;
            AppSettings.Save();
            return changed;
        }
    }
}

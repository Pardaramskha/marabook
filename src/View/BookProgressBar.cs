using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>La barre d'objectif à deux teintes du batch 32 — warn pour
    /// ce qui est présent, ok pour ce qui est terminé — sortie de
    /// l'inspecteur pour servir aussi à l'Accueil (batch 41). Un libellé,
    /// une piste ; les quatre colonnes restent visibles pour les sondes.</summary>
    public class BookProgressBar : StackPanel
    {
        public readonly TextBlock Label;
        public readonly ColumnDefinition Present, Rest, Done, Undone;

        public BookProgressBar()
        {
            Label = new TextBlock
            {
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Children.Add(Label);
            var track = new Grid { Height = 8, Margin = new Thickness(0, 5, 0, 0) };
            Present = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
            Rest = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            track.ColumnDefinitions.Add(Present);
            track.ColumnDefinitions.Add(Rest);
            var trackBg = new Border
            {
                Background = Chrome.Border,
                CornerRadius = new CornerRadius(4)
            };
            Grid.SetColumnSpan(trackBg, 2);
            track.Children.Add(trackBg);
            var presentGrid = new Grid();
            Done = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
            Undone = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            presentGrid.ColumnDefinitions.Add(Done);
            presentGrid.ColumnDefinitions.Add(Undone);
            var presentBar = new Border
            {
                Background = Chrome.Warn, // warn : présents
                CornerRadius = new CornerRadius(4),
                Child = presentGrid
            };
            var doneBar = new Border
            {
                Background = Chrome.Ok, // ok : terminés
                CornerRadius = new CornerRadius(4)
            };
            Grid.SetColumn(doneBar, 0);
            presentGrid.Children.Add(doneBar);
            Grid.SetColumn(presentBar, 0);
            track.Children.Add(presentBar);
            Children.Add(track);
        }

        /// <summary>Le libellé du batch 32 pour un livre.</summary>
        public static string Describe(BookProgress progress)
        {
            var culture = CultureInfo.CurrentCulture;
            return "Objectif : " + progress.Present.ToString("N0", culture)
                + " / " + progress.Goal.ToString("N0", culture) + " chapitres"
                + " — " + progress.Done.ToString("N0", culture)
                + (progress.Done > 1 ? " terminés" : " terminé")
                + (progress.Done >= progress.Goal ? " — atteint !" : "");
        }

        public void Show(BookProgress progress, string noGoalText)
        {
            if (!progress.HasGoal)
            {
                ShowRatio(noGoalText, 0, 0);
                return;
            }
            ShowRatio(Describe(progress), progress.PresentRatio, progress.DoneRatio);
        }

        /// <summary>present et done sont des parts de l'objectif (0–1) ; la
        /// part verte est la part terminée DE l'orange.</summary>
        public void ShowRatio(string label, double present, double done)
        {
            Label.Text = label;
            if (present < 0) present = 0;
            if (present > 1) present = 1;
            if (done < 0) done = 0;
            if (done > present) done = present;
            Present.Width = new GridLength(present, GridUnitType.Star);
            Rest.Width = new GridLength(1 - present, GridUnitType.Star);
            var doneShare = present <= 0 ? 0 : done / present;
            Done.Width = new GridLength(doneShare, GridUnitType.Star);
            Undone.Width = new GridLength(1 - doneShare, GridUnitType.Star);
        }
    }
}

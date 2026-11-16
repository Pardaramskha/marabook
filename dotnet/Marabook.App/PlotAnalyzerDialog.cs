using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.App
{
    /// <summary>AIDE › ANALYSEUR DE .PLOT (16/11/2026, Rémi) : combien d'octets chaque
    /// grande section du projet occupe dans son fichier — écrits, fiches et leurs
    /// illustrations, cartes (une ligne par carte), versions, corbeille… Le calcul est
    /// dans Persistence/PlotAnalysis.cs ; ici, une barre par section, de la plus lourde
    /// à la plus légère, et le détail dessous.</summary>
    public sealed class PlotAnalyzerDialog : Window
    {
        private PlotAnalyzerDialog(Window owner, string path, PlotAnalysis.Report report, bool dirty)
        {
            TimelineUi.Dress(this, owner, "Analyseur de .plot");
            Width = 640;
            Height = 660;
            MinWidth = 480;
            MinHeight = 360;
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var page = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
            page.Children.Add(new TextBlock { Text = Path.GetFileName(path), Foreground = Chrome.Ink, FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            page.Children.Add(new TextBlock
            {
                Text = Maps.SizeLabel(report.FileBytes) + "  ·  " + report.FileBytes.ToString("N0", fr) + " octets  ·  " + TimeSystem.Count(report.Entries, "entrée", "entrées"),
                Foreground = Chrome.SoftText, FontSize = 12.5, Margin = new Thickness(0, 3, 0, 0), Tag = "analyzer-total"
            });
            if (dirty)
            {
                var notice = AstrolabeUi.Notice("Le projet a des modifications non enregistrées : l'analyse porte sur le fichier tel qu'il est sur le disque. Enregistrez, puis rouvrez l'analyseur, pour les compter.", false, "analyzer-dirty");
                notice.Margin = new Thickness(0, 10, 0, 0);
                page.Children.Add(notice);
            }
            // La barre d'ensemble : une tranche par section.
            var tints = new[] { "#5B67D8", "#3E9C7A", "#C0504D", "#B5843A", "#8E6BBF", "#0E7C7B", "#D9603A", "#6E8FB5", "#6B7280", "#C08497", "#9AA1B2" };
            var strip = new Grid { Height = 14, Margin = new Thickness(0, 14, 0, 6) };
            var total = Math.Max(1, report.FileBytes);
            var index = 0;
            var colorOf = new Dictionary<string, IBrush>();
            foreach (var section in report.Sections)
            {
                var brush = new SolidColorBrush(Color.Parse(tints[index++ % tints.Length]));
                colorOf[section.Name] = brush;
                if (section.Bytes <= 0) continue;
                strip.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(section.Bytes, GridUnitType.Star)));
                var slice = new Border { Background = brush, [ToolTip.TipProperty] = section.Name + " : " + Maps.SizeLabel(section.Bytes) };
                Grid.SetColumn(slice, strip.ColumnDefinitions.Count - 1);
                strip.Children.Add(slice);
            }
            page.Children.Add(new Border { Child = strip, CornerRadius = new CornerRadius(7), ClipToBounds = true });

            foreach (var section in report.Sections)
            {
                if (section.Bytes <= 0 && section.Count == 0) continue;
                var share = (double)section.Bytes / total;
                var card = new StackPanel();
                var top = new DockPanel();
                var size = new TextBlock
                {
                    Text = Maps.SizeLabel(section.Bytes) + "  ·  " + share.ToString(share < 0.001 && section.Bytes > 0 ? "0.00 %" : "0.0 %", fr),
                    Foreground = Chrome.Ink, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(size, Dock.Right);
                top.Children.Add(size);
                var name = new StackPanel { Orientation = Orientation.Horizontal };
                name.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = colorOf[section.Name], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) });
                name.Children.Add(new TextBlock { Text = section.Name, Foreground = Chrome.Ink, FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                if (section.Count > 0)
                    name.Children.Add(new TextBlock { Text = "  " + section.Count.ToString("N0", fr), Foreground = Chrome.FaintText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
                top.Children.Add(name);
                card.Children.Add(top);
                card.Children.Add(new TextBlock { Text = section.Hint, Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 2, 0, 0) });
                // La jauge de la section, sur la largeur du fichier.
                var gauge = new Grid { Height = 5, Margin = new Thickness(20, 6, 0, 0), ColumnDefinitions = new ColumnDefinitions() };
                gauge.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(0.0001, share), GridUnitType.Star)));
                gauge.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(0.0001, 1 - share), GridUnitType.Star)));
                gauge.Children.Add(new Border { Background = colorOf[section.Name], CornerRadius = new CornerRadius(2.5), MinWidth = section.Bytes > 0 ? 3 : 0 });
                card.Children.Add(gauge);
                foreach (var detail in section.Details)
                {
                    var line = new DockPanel { Margin = new Thickness(20, 4, 0, 0) };
                    var value = new TextBlock { Text = Maps.SizeLabel(detail.Bytes), Foreground = Chrome.SoftText, FontSize = 12 };
                    DockPanel.SetDock(value, Dock.Right);
                    line.Children.Add(value);
                    line.Children.Add(new TextBlock { Text = detail.Name, Foreground = Chrome.SoftText, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                    card.Children.Add(line);
                }
                page.Children.Add(new Border
                {
                    Child = card, Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 10, 14, 11), Margin = new Thickness(0, 8, 0, 0), Tag = "analyzer-section"
                });
            }
            page.Children.Add(TimelineUi.Hint("Les poids sont ceux du fichier (octets compressés). Le manifeste — une seule entrée pour la Pile, les champs des fiches, les frises, les plans — est réparti entre les sections au prorata de ce que chacune y écrit : ces lignes-là sont des estimations."));
            var close = new Button { Content = "Fermer", IsDefault = true, IsCancel = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            close.Click += delegate { Close(); };
            page.Children.Add(close);
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
        }

        /// <summary>Ouvre l'analyseur sur le fichier du projet ; dit pourquoi si rien n'est à analyser.</summary>
        public static void Show(Window owner, string path, Project project, bool dirty)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageDialog.Show(owner, "Ce projet n'a pas encore de fichier : enregistrez-le d'abord (Fichier › Enregistrer), l'analyseur pèsera ensuite son .plot.",
                    "Analyseur de .plot", MessageButtons.OK, MessageIcon.Information);
                return;
            }
            PlotAnalysis.Report report;
            try { report = PlotAnalysis.Analyze(path, project); }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is InvalidDataException) && !(error is UnauthorizedAccessException)) throw;
                MessageDialog.Show(owner, "Le fichier ne se lit pas : " + error.Message, "Analyseur de .plot", MessageButtons.OK, MessageIcon.Warning);
                return;
            }
            var dialog = new PlotAnalyzerDialog(owner, path, report, dirty);
            Dialogs.ShowModal(dialog, owner);
        }

        internal static PlotAnalyzerDialog BuildForProbe(Window owner, string path, Project project, bool dirty)
        {
            return new PlotAnalyzerDialog(owner, path, PlotAnalysis.Analyze(path, project), dirty);
        }

        internal int SectionCardsForProbe
        {
            get
            {
                var count = 0;
                var page = ((ScrollViewer)Content).Content as StackPanel;
                foreach (var child in page.Children) if (Equals((child as Control).Tag, "analyzer-section")) count++;
                return count;
            }
        }
    }
}

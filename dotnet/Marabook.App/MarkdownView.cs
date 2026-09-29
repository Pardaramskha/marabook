using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marabook.Model;

namespace Marabook.App
{
    // ================================================================ dialecte
    // Le dialecte Markdown de la maison (batch 31) — le MÊME que l'app
    // Markdown We Go de Stargazer, porté du HTML vers un modèle de blocs pur
    // (testable console), plus les [[liens wiki]] propres à Marabook :
    //   blocs : titres # à ######, filets --- *** ___, citations >, listes
    //   imbriquées - * + / 1. 2., cases à cocher - [ ] / - [x], blocs de
    //   code ```, tableaux | a | b | avec alignements :---:, paragraphes ;
    //   en ligne : `code`, ~~barré~~, **gras**, __gras__, *italique*,
    //   _italique_, <u>souligné</u>, ![alt](url), [texte](url), [[fiche]].

    /// <summary>Le rendu Avalonia du dialecte : une pile de blocs (TextBlock à
    /// inlines, cadres, grilles) aux couleurs papier du thème. Liens [[wiki]]
    /// et cases à cocher sont interactifs (navigation de fiche, bascule dans
    /// la source).</summary>
    public static class MarkdownRender
    {
        private static readonly FontFamily Body = new FontFamily("Georgia");
        private static readonly FontFamily Mono = new FontFamily("Consolas, DejaVu Sans Mono, Menlo, monospace");

        /// <summary>wikiClicked : clic sur un [[lien]] (cible). taskToggled :
        /// clic sur une case (index de tâche) — au consommateur de basculer
        /// le caractère dans la source et de re-rendre.</summary>
        public static Control Build(string source,
            Action<string> wikiClicked, Action<int> taskToggled)
        {
            var stack = new StackPanel();
            foreach (var block in MarkdownDialect.Parse(source))
                stack.Children.Add(BuildBlock(block, wikiClicked, taskToggled));
            return stack;
        }

        private static TextBlock Paragraph()
        {
            return new TextBlock
            {
                FontFamily = Body,
                FontSize = 14.5,
                Foreground = Chrome.PaperInk,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left
            };
        }

        private static Control BuildBlock(MdBlock block,
            Action<string> wikiClicked, Action<int> taskToggled)
        {
            switch (block.Kind)
            {
                case MdBlockKind.Heading:
                {
                    var sizes = new[] { 24.0, 20, 17.5, 15.5, 14, 13 };
                    var paragraph = Paragraph();
                    paragraph.FontFamily = FontFamily.Default;
                    paragraph.FontSize = sizes[block.HeadingLevel - 1];
                    paragraph.FontWeight = FontWeight.Bold;
                    paragraph.Margin = new Thickness(0, block.HeadingLevel == 1 ? 6 : 10, 0, 4);
                    FillInlines(paragraph.Inlines, block.Inlines, wikiClicked);
                    if (block.HeadingLevel == 1)
                        return new Border
                        {
                            BorderBrush = Chrome.Border,
                            BorderThickness = new Thickness(0, 0, 0, 1),
                            Padding = new Thickness(0, 0, 0, 4),
                            Child = paragraph
                        };
                    return paragraph;
                }
                case MdBlockKind.Rule:
                    return new Border
                    {
                        Height = 1,
                        Background = Chrome.Border,
                        Margin = new Thickness(0, 6, 0, 6)
                    };
                case MdBlockKind.Quote:
                {
                    var lines = new StackPanel();
                    foreach (var line in block.QuoteLines)
                    {
                        var paragraph = Paragraph();
                        paragraph.Foreground = Chrome.PaperSoftInk;
                        paragraph.Margin = new Thickness(0, 2, 0, 2);
                        FillInlines(paragraph.Inlines, line, wikiClicked);
                        lines.Children.Add(paragraph);
                    }
                    return new Border
                    {
                        BorderBrush = Chrome.Accent,
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(12, 2, 0, 2),
                        Margin = new Thickness(0, 4, 0, 8),
                        Child = lines
                    };
                }
                case MdBlockKind.Code:
                {
                    var paragraph = Paragraph();
                    paragraph.FontFamily = Mono;
                    paragraph.FontSize = 12.5;
                    paragraph.Text = block.CodeText;
                    return new Border
                    {
                        Background = Chrome.BarBgLight,
                        BorderBrush = Chrome.Border,
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 4, 0, 8),
                        Child = paragraph
                    };
                }
                case MdBlockKind.ListItem:
                {
                    var paragraph = Paragraph();
                    paragraph.Margin = new Thickness(18 + block.Indent * 18, 1, 0, 1);
                    if (block.IsTask)
                    {
                        var box = new CheckBox
                        {
                            IsChecked = block.TaskChecked,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 6, -2)
                        };
                        var index = block.TaskIndex;
                        box.Click += delegate
                        {
                            if (taskToggled != null) taskToggled(index);
                        };
                        paragraph.Inlines.Add(new InlineUIContainer(box));
                    }
                    else
                        paragraph.Inlines.Add(new Run(block.Ordered
                            ? block.Number + ". " : "•  ")
                        { FontWeight = FontWeight.SemiBold });
                    FillInlines(paragraph.Inlines, block.Inlines, wikiClicked);
                    return paragraph;
                }
                case MdBlockKind.Table:
                {
                    var grid = new Grid { Margin = new Thickness(0, 6, 0, 10) };
                    var columns = block.TableHeader.Count;
                    for (var c = 0; c < columns; c++)
                        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    var rowIndex = 0;
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    for (var c = 0; c < columns; c++)
                        AddCell(grid, rowIndex, c, block.TableHeader[c], block.TableAligns[c], true, wikiClicked);
                    foreach (var row in block.TableRows)
                    {
                        rowIndex++;
                        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                        for (var c = 0; c < row.Count && c < columns; c++)
                            AddCell(grid, rowIndex, c, row[c], block.TableAligns[c], false, wikiClicked);
                    }
                    return new Border
                    {
                        BorderBrush = Chrome.Border,
                        BorderThickness = new Thickness(1, 1, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Child = grid
                    };
                }
                default:
                {
                    var paragraph = Paragraph();
                    paragraph.Margin = new Thickness(0, 4, 0, 8);
                    FillInlines(paragraph.Inlines, block.Inlines, wikiClicked);
                    return paragraph;
                }
            }
        }

        private static void AddCell(Grid grid, int row, int column, List<MdInline> inlines, int align,
            bool header, Action<string> wikiClicked)
        {
            var paragraph = Paragraph();
            paragraph.TextAlignment = align == 1 ? TextAlignment.Center
                : align == 2 ? TextAlignment.Right : TextAlignment.Left;
            if (header) paragraph.FontWeight = FontWeight.SemiBold;
            FillInlines(paragraph.Inlines, inlines, wikiClicked);
            var cell = new Border
            {
                Background = header ? Chrome.BarBgLight : null,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(8, 4, 8, 4),
                Child = paragraph
            };
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        /// <summary>Un lien cliquable : Avalonia n'a pas d'Hyperlink — un
        /// TextBlock accent dans un conteneur en ligne.</summary>
        private static InlineUIContainer Link(Run run, string tooltip, Action click)
        {
            // Le lien prend la police et le corps DU PARAGRAPHE (29/09) : un
            // Run détaché rend les valeurs par défaut d'Avalonia (Inter 12),
            // et le lien flottait au-dessus de la ligne en cassant
            // l'interligne. Même métrique, centré sur la ligne : il s'aligne.
            var code = ReferenceEquals(run.FontFamily, Mono);
            var text = new TextBlock
            {
                Text = run.Text,
                FontWeight = run.FontWeight,
                FontStyle = run.FontStyle,
                FontFamily = code ? Mono : Body,
                FontSize = code ? 12.5 : 14.5,
                Foreground = Chrome.Accent,
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(text, tooltip);
            text.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(text).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                click();
            };
            return new InlineUIContainer(text) { BaselineAlignment = BaselineAlignment.Center };
        }

        private static void FillInlines(InlineCollection into,
            List<MdInline> inlines, Action<string> wikiClicked)
        {
            foreach (var inline in inlines)
            {
                if (inline.Text == "\n") { into.Add(new LineBreak()); continue; }

                var run = new Run(inline.Text);
                if (inline.Bold) run.FontWeight = FontWeight.Bold;
                if (inline.Italic) run.FontStyle = FontStyle.Italic;
                if (inline.Strike)
                {
                    run.TextDecorations = TextDecorations.Strikethrough;
                    run.Foreground = Chrome.PaperSoftInk;
                }
                if (inline.Underline)
                    run.TextDecorations = TextDecorations.Underline;
                if (inline.Code)
                {
                    run.FontFamily = Mono;
                    run.FontSize = 12.5;
                    run.Background = Chrome.BarBgLight;
                }

                if (inline.WikiTarget != null)
                {
                    var target = inline.WikiTarget;
                    into.Add(Link(run, "Ouvrir « " + target + " »", delegate
                    {
                        if (wikiClicked != null) wikiClicked(target);
                    }));
                }
                else if (inline.LinkUrl != null)
                {
                    var url = inline.LinkUrl;
                    into.Add(Link(run, url, delegate
                    {
                        // Seuls les liens web s'ouvrent (jamais d'exécution
                        // de fichier local depuis un clic de fiche).
                        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            try { AppPlatform.OpenWithShell(url); }
                            catch { }
                    }));
                }
                else if (inline.ImageUrl != null)
                {
                    // Les images de corps restent une référence affichée (le
                    // portrait de la fiche porte l'illustration) — l'alt en
                    // italique doux.
                    run.Text = "🖼 " + ((inline.Text ?? "").Length > 0 ? inline.Text : inline.ImageUrl);
                    run.FontStyle = FontStyle.Italic;
                    run.Foreground = Chrome.PaperSoftInk;
                    into.Add(run);
                }
                else into.Add(run);
            }
        }
    }
}

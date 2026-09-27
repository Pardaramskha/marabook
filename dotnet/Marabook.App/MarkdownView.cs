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

    /// <summary>Un segment en ligne : du texte porteur de styles, ou un lien
    /// (externe, wiki), ou une image. Les styles se cumulent (gras italique).</summary>
    public class MdInline
    {
        public string Text = "";
        public bool Bold, Italic, Strike, Underline, Code;
        public string LinkUrl;    // [texte](url) — Text = le texte
        public string WikiTarget; // [[cible]] — Text = la cible affichée
        public string ImageUrl;   // ![alt](url) — Text = alt
    }

    public enum MdBlockKind { Heading, Paragraph, Rule, Quote, Code, ListItem, Table }

    /// <summary>Un bloc du document : le modèle plat que rend la vue. Les
    /// listes sont une suite de ListItem (l'imbrication est portée par
    /// Indent) — fidèle à l'esprit de Markdown We Go, plus simple à rendre.</summary>
    public class MdBlock
    {
        public MdBlockKind Kind;
        public int HeadingLevel;           // Heading
        public List<MdInline> Inlines = new List<MdInline>(); // Heading, Paragraph, ListItem
        public List<List<MdInline>> QuoteLines;               // Quote
        public string CodeText;            // Code (lignes brutes)
        public int Indent;                 // ListItem (crans de 2 espaces)
        public bool Ordered;               // ListItem
        public int Number;                 // ListItem ordonné
        public bool IsTask;                // ListItem case à cocher
        public bool TaskChecked;
        public int TaskIndex;              // n° de case (bascule au clic)
        public List<List<MdInline>> TableHeader;   // Table
        public List<List<List<MdInline>>> TableRows;
        public List<int> TableAligns;      // 0 gauche, 1 centre, 2 droite
    }

    public static class MarkdownDialect
    {
        public static List<MdBlock> Parse(string source)
        {
            var blocks = new List<MdBlock>();
            var lines = (source ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var paragraph = new List<string>();
            var inCode = false;
            var code = new System.Text.StringBuilder();
            MdBlock quote = null;
            var taskIndex = 0;

            Action closeParagraph = delegate
            {
                if (paragraph.Count == 0) return;
                var block = new MdBlock { Kind = MdBlockKind.Paragraph };
                for (var k = 0; k < paragraph.Count; k++)
                {
                    if (k > 0) block.Inlines.Add(new MdInline { Text = "\n" });
                    ParseInlines(paragraph[k], new MdInline(), block.Inlines);
                }
                blocks.Add(block);
                paragraph.Clear();
            };
            Action closeQuote = delegate { quote = null; };

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // blocs de code ``` : tout est littéral jusqu'à la clôture
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    if (!inCode)
                    {
                        closeParagraph();
                        closeQuote();
                        code.Length = 0;
                        inCode = true;
                    }
                    else
                    {
                        blocks.Add(new MdBlock
                        {
                            Kind = MdBlockKind.Code,
                            CodeText = code.ToString().TrimEnd('\n')
                        });
                        inCode = false;
                    }
                    continue;
                }
                if (inCode)
                {
                    code.Append(line).Append('\n');
                    continue;
                }

                var trimmed = line.Trim();

                if (trimmed.Length == 0)
                {
                    closeParagraph();
                    closeQuote();
                    continue;
                }

                // filet --- / *** / ___
                if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
                {
                    closeParagraph();
                    closeQuote();
                    blocks.Add(new MdBlock { Kind = MdBlockKind.Rule });
                    continue;
                }

                // titres # à ######
                var heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.+?)\s*#*\s*$");
                if (heading.Success)
                {
                    closeParagraph();
                    closeQuote();
                    var block = new MdBlock
                    {
                        Kind = MdBlockKind.Heading,
                        HeadingLevel = heading.Groups[1].Value.Length
                    };
                    ParseInlines(heading.Groups[2].Value, new MdInline(), block.Inlines);
                    blocks.Add(block);
                    continue;
                }

                // citation >
                if (trimmed.StartsWith(">", StringComparison.Ordinal))
                {
                    closeParagraph();
                    if (quote == null)
                    {
                        quote = new MdBlock
                        {
                            Kind = MdBlockKind.Quote,
                            QuoteLines = new List<List<MdInline>>()
                        };
                        blocks.Add(quote);
                    }
                    var content = trimmed.Substring(1).TrimStart();
                    var inlines = new List<MdInline>();
                    ParseInlines(content, new MdInline(), inlines);
                    quote.QuoteLines.Add(inlines);
                    continue;
                }

                // tableau : une rangée | … | suivie de |---|:---:|
                if (trimmed.IndexOf('|') >= 0 && i + 1 < lines.Length
                    && IsTableSeparator(lines[i + 1]))
                {
                    var headers = TableCells(trimmed);
                    var seps = TableCells(lines[i + 1].Trim());
                    if (seps.Count == headers.Count)
                    {
                        closeParagraph();
                        closeQuote();
                        var table = new MdBlock
                        {
                            Kind = MdBlockKind.Table,
                            TableHeader = new List<List<MdInline>>(),
                            TableRows = new List<List<List<MdInline>>>(),
                            TableAligns = new List<int>()
                        };
                        foreach (var sep in seps)
                        {
                            var left = sep.StartsWith(":", StringComparison.Ordinal);
                            var right = sep.EndsWith(":", StringComparison.Ordinal);
                            table.TableAligns.Add(left && right ? 1 : right ? 2 : 0);
                        }
                        foreach (var cell in headers)
                        {
                            var inlines = new List<MdInline>();
                            ParseInlines(cell, new MdInline(), inlines);
                            table.TableHeader.Add(inlines);
                        }
                        i++; // la ligne de séparation est consommée
                        while (i + 1 < lines.Length && lines[i + 1].IndexOf('|') >= 0
                            && lines[i + 1].Trim().Length > 0)
                        {
                            i++;
                            var cells = TableCells(lines[i].Trim());
                            var row = new List<List<MdInline>>();
                            for (var c = 0; c < headers.Count; c++)
                            {
                                var inlines = new List<MdInline>();
                                ParseInlines(c < cells.Count ? cells[c] : "",
                                    new MdInline(), inlines);
                                row.Add(inlines);
                            }
                            table.TableRows.Add(row);
                        }
                        blocks.Add(table);
                        continue;
                    }
                }

                // listes à puces - * + et numérotées 1. 2. — le niveau suit
                // l'indentation (2 espaces par cran, tabulation = 2)
                var bullet = Regex.Match(line, @"^(\s*)([-*+])\s+(.+)$");
                var numbered = Regex.Match(line, @"^(\s*)(\d+)[.)]\s+(.+)$");
                if (bullet.Success || numbered.Success)
                {
                    closeParagraph();
                    closeQuote();
                    var m = bullet.Success ? bullet : numbered;
                    var item = new MdBlock
                    {
                        Kind = MdBlockKind.ListItem,
                        Ordered = numbered.Success,
                        Indent = IndentWidth(m.Groups[1].Value) / 2
                    };
                    if (numbered.Success)
                        item.Number = int.Parse(numbered.Groups[2].Value);
                    var content = m.Groups[3].Value;
                    var task = bullet.Success
                        ? Regex.Match(content, @"^\[( |x|X)\]\s+(.*)$") : Match.Empty;
                    if (task.Success)
                    {
                        item.IsTask = true;
                        item.TaskChecked = task.Groups[1].Value != " ";
                        item.TaskIndex = taskIndex++;
                        content = task.Groups[2].Value;
                    }
                    ParseInlines(content, new MdInline(), item.Inlines);
                    blocks.Add(item);
                    continue;
                }

                // sinon : ligne de paragraphe
                closeQuote();
                paragraph.Add(trimmed);
            }

            if (inCode)
                blocks.Add(new MdBlock
                {
                    Kind = MdBlockKind.Code,
                    CodeText = code.ToString().TrimEnd('\n')
                });
            closeParagraph();
            return blocks;
        }

        /// <summary>Position dans la source du caractère d'état ([ ] ou [x])
        /// de la n-ième case à cocher — même comptage que Parse (les blocs de
        /// code sont sautés). -1 si introuvable. Porté de Markdown We Go.</summary>
        public static int FindTask(string source, int n)
        {
            var text = source ?? "";
            var inCode = false;
            var count = 0;
            var pos = 0;
            while (pos <= text.Length)
            {
                var end = text.IndexOf('\n', pos);
                if (end < 0) end = text.Length;
                var line = text.Substring(pos, end - pos);
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                    inCode = !inCode;
                else if (!inCode)
                {
                    var m = Regex.Match(line, @"^(\s*[-*+]\s+)\[( |x|X)\]\s");
                    if (m.Success)
                    {
                        if (count == n) return pos + m.Groups[1].Value.Length + 1;
                        count++;
                    }
                }
                if (end >= text.Length) break;
                pos = end + 1;
            }
            return -1;
        }

        // Le motif en ligne : l'alternative la plus À GAUCHE gagne ; à
        // position égale, la première listée (donc ** avant *). Même
        // précédence que la chaîne de remplacements de Markdown We Go.
        private static readonly Regex InlinePattern = new Regex(
            "`(?<code>[^`]+)`"
            + @"|~~(?<strike>[^~\n]+)~~"
            // Non-gourmand et tolérant aux étoiles internes (amélioration sur
            // Markdown We Go) : **gras avec *italique* dedans** fonctionne,
            // la récursion pose les styles cumulés.
            + @"|\*\*(?<bold>[^\n]+?)\*\*"
            + @"|__(?<bold2>[^\n]+?)__"
            + @"|!\[(?<imgalt>[^\]]*)\]\((?<imgurl>[^)\s]+)\)"
            + @"|\[\[(?<wiki>[^\]\n]+)\]\]"
            + @"|\[(?<linktext>[^\]]+)\]\((?<linkurl>[^)\s]+)\)"
            + @"|<u>(?<under>.*?)</u>"
            + @"|(?<![\w*])\*(?<em>[^*\n]+)\*(?![\w*])"
            + @"|(?<![\w_])_(?<em2>[^_\n]+)_(?![\w_])",
            RegexOptions.Singleline);

        /// <summary>Découpe une ligne en segments stylés — récursif : le
        /// contenu d'un **gras** repasse au moulin avec le style augmenté
        /// (donc **gras *italique*** fonctionne, comme dans Markdown We Go).</summary>
        public static void ParseInlines(string text, MdInline style, List<MdInline> into)
        {
            var pos = 0;
            while (pos < text.Length)
            {
                var m = InlinePattern.Match(text, pos);
                if (!m.Success)
                {
                    AddText(text.Substring(pos), style, into);
                    return;
                }
                if (m.Index > pos)
                    AddText(text.Substring(pos, m.Index - pos), style, into);

                if (m.Groups["code"].Success)
                {
                    var run = CloneStyle(style);
                    run.Code = true;
                    run.Text = m.Groups["code"].Value;
                    into.Add(run);
                }
                else if (m.Groups["strike"].Success)
                    Recurse(m.Groups["strike"].Value, style, into,
                        delegate(MdInline s) { s.Strike = true; });
                else if (m.Groups["bold"].Success)
                    Recurse(m.Groups["bold"].Value, style, into,
                        delegate(MdInline s) { s.Bold = true; });
                else if (m.Groups["bold2"].Success)
                    Recurse(m.Groups["bold2"].Value, style, into,
                        delegate(MdInline s) { s.Bold = true; });
                else if (m.Groups["imgurl"].Success)
                {
                    var run = CloneStyle(style);
                    run.ImageUrl = m.Groups["imgurl"].Value;
                    run.Text = m.Groups["imgalt"].Value;
                    into.Add(run);
                }
                else if (m.Groups["wiki"].Success)
                {
                    // « [[Cible|texte]] » (18/09) : la cible d'un côté, les
                    // mots affichés de l'autre — comme dans les écrits.
                    var run = CloneStyle(style);
                    var inner = m.Groups["wiki"].Value;
                    var pipe = inner.IndexOf('|');
                    run.WikiTarget = (pipe < 0 ? inner : inner.Substring(0, pipe)).Trim();
                    run.Text = pipe < 0 || inner.Length == pipe + 1 ? run.WikiTarget : inner.Substring(pipe + 1);
                    into.Add(run);
                }
                else if (m.Groups["linkurl"].Success)
                    Recurse(m.Groups["linktext"].Value, style, into,
                        delegate(MdInline s) { s.LinkUrl = m.Groups["linkurl"].Value; });
                else if (m.Groups["under"].Success)
                    Recurse(m.Groups["under"].Value, style, into,
                        delegate(MdInline s) { s.Underline = true; });
                else if (m.Groups["em"].Success)
                    Recurse(m.Groups["em"].Value, style, into,
                        delegate(MdInline s) { s.Italic = true; });
                else if (m.Groups["em2"].Success)
                    Recurse(m.Groups["em2"].Value, style, into,
                        delegate(MdInline s) { s.Italic = true; });

                pos = m.Index + m.Length;
            }
        }

        private static void Recurse(string inner, MdInline style,
            List<MdInline> into, Action<MdInline> augment)
        {
            var augmented = CloneStyle(style);
            augment(augmented);
            ParseInlines(inner, augmented, into);
        }

        private static void AddText(string text, MdInline style, List<MdInline> into)
        {
            if (text.Length == 0) return;
            var run = CloneStyle(style);
            run.Text = text;
            into.Add(run);
        }

        private static MdInline CloneStyle(MdInline style)
        {
            return new MdInline
            {
                Bold = style.Bold,
                Italic = style.Italic,
                Strike = style.Strike,
                Underline = style.Underline,
                LinkUrl = style.LinkUrl
            };
        }

        // Indentation en crans : une tabulation vaut deux espaces.
        private static int IndentWidth(string s)
        {
            var n = 0;
            foreach (var c in s) n += c == '\t' ? 2 : 1;
            return n;
        }

        // |---|:---:|---:| (chaque cellule : tirets, deux-points optionnels)
        private static bool IsTableSeparator(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.IndexOf('|') < 0 || trimmed.IndexOf('-') < 0) return false;
            return Regex.IsMatch(trimmed, @"^\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?$");
        }

        private static List<string> TableCells(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("|", StringComparison.Ordinal))
                trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("|", StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            var cells = new List<string>();
            foreach (var c in trimmed.Split('|')) cells.Add(c.Trim());
            return cells;
        }
    }

    // ================================================================== rendu

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
            var text = new TextBlock
            {
                Text = run.Text,
                FontWeight = run.FontWeight,
                FontStyle = run.FontStyle,
                FontFamily = run.FontFamily,
                FontSize = run.FontSize,
                Foreground = Chrome.Accent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent
            };
            ToolTip.SetTip(text, tooltip);
            text.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(text).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                click();
            };
            return new InlineUIContainer(text) { BaselineAlignment = BaselineAlignment.Baseline };
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

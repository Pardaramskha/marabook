using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Marabook.Model
{
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
}

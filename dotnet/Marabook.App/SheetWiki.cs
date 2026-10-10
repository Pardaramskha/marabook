using System;
using System.Collections.Generic;
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
    /// <summary>LE RENDU WIKI d'une fiche (extrait de SheetView au batch 47) :
    /// grand titre, corps markdown, et des PAPERS de même dessin (21/09) —
    /// l'infobox (portrait, champs remplis groupés, champs libres), puis
    /// Relations, « Évolution et présence », et le graph statistique, dans cet
    /// ordre, chacun seulement s'il a quelque chose à dire. Deux mises en
    /// page : « page » = les papers en colonne à droite du corps (le mode
    /// wiki de la fiche) ; « colonne » = tout empilé (l'épinglé de la colonne
    /// de droite, b47). Lecture seule par nature : rien ici n'écrit dans la
    /// fiche, sauf le basculement d'une case à cocher du corps, rendu au
    /// demandeur.</summary>
    public static class SheetWiki
    {
        public static Control Build(BinderItem item, SheetTemplate template, Project project, string body,
            bool column, Action<BinderItem> navigate, Action<string> linkClicked, Action<int> taskToggled)
        {
            var page = new Border
            {
                Background = Chrome.FieldBg, // une page d'interface, sombre au sombre — pas le papier (30/09)
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = column ? new Thickness(0) : new Thickness(24, 20, 24, 20),
                Padding = column ? new Thickness(16, 14, 16, 16) : new Thickness(28),
                MaxWidth = column ? double.PositiveInfinity : 900
            };
            var papers = BuildPapers(item, template, project, navigate, linkClicked);
            if (column)
            {
                // En colonne : le titre, les papers, puis le corps — empilés.
                var stack = new StackPanel();
                stack.Children.Add(BuildHead(item, template, project, true));
                for (var i = 0; i < papers.Count; i++)
                {
                    papers[i].Margin = new Thickness(0, 10, 0, i == papers.Count - 1 ? 12 : 0);
                    stack.Children.Add(papers[i]);
                }
                stack.Children.Add(BuildBody(body, linkClicked, taskToggled));
                page.Child = stack;
                return page;
            }
            var layout = new DockPanel { LastChildFill = true };
            if (papers.Count > 0)
            {
                var side = new StackPanel { Width = 250, Margin = new Thickness(20, 6, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                foreach (var paper in papers)
                {
                    paper.Margin = new Thickness(0, 0, 0, 10);
                    side.Children.Add(paper);
                }
                DockPanel.SetDock(side, Dock.Right);
                layout.Children.Add(side);
            }
            var main = new StackPanel();
            main.Children.Add(BuildHead(item, template, project, false));
            main.Children.Add(BuildBody(body, linkClicked, taskToggled));
            layout.Children.Add(main);
            page.Child = layout;
            return page;
        }

        private static Control BuildHead(BinderItem item, SheetTemplate template, Project project, bool column)
        {
            var head = new StackPanel();
            head.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = column ? 20 : 26,
                FontWeight = FontWeight.Bold,
                Foreground = Chrome.Ink,
                TextWrapping = TextWrapping.Wrap
            });
            var category = project == null ? null : project.SheetCategoryOf(item);
            head.Children.Add(new TextBlock
            {
                Text = category != null ? project.CategoryPath(category) : template != null ? template.Name : "Fiche",
                FontSize = 12,
                Foreground = Chrome.SoftText,
                Margin = new Thickness(0, 2, 0, 8)
            });
            head.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 0, 0, column ? 0 : 12) });
            return head;
        }

        private static Control BuildBody(string body, Action<string> linkClicked, Action<int> taskToggled)
        {
            var flow = MarkdownRender.Build(body ?? "", linkClicked, taskToggled);
            return new ScrollViewer { Content = flow,
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
                Focusable = false
            };
        }

        // ================================================================ papers

        /// <summary>Les papers, dans l'ordre : infobox, Relations, « Évolution
        /// et présence », graph statistique — vides écartés.</summary>
        private static List<Border> BuildPapers(BinderItem item, SheetTemplate template, Project project, Action<BinderItem> navigate, Action<string> linkClicked = null)
        {
            var papers = new List<Border>();
            foreach (var section in BuildSections(item, template, project, navigate, linkClicked)) papers.Add(Frame(section));
            var relations = BuildRelations(item, project, navigate);
            if (relations != null) papers.Add(Frame(relations));
            var story = BuildEvolutionPresence(item, template, project, navigate);
            if (story != null) papers.Add(Frame(story));
            var graph = BuildGraph(item, template);
            if (graph != null) papers.Add(Frame(graph));
            return papers;
        }

        /// <summary>Le cadre commun des papers : le dessin de l'infobox.</summary>
        private static Border Frame(StackPanel content)
        {
            return new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                VerticalAlignment = VerticalAlignment.Top,
                Child = content
            };
        }

        /// <summary>Les papers des sections (30/09, comme Relations) : le
        /// portrait seul en tête, puis UN paper par section — « Infos » (le
        /// groupe vide), les sections du modèle dans leur ordre, toute section
        /// qu'un champ ou une info libre nomme encore — avec ses champs de
        /// modèle remplis puis ses infos libres ; une section vide n'a pas de
        /// paper. Avant, tout tenait dans une seule infobox, et les infos
        /// libres perdaient leur section.</summary>
        private static List<StackPanel> BuildSections(BinderItem item, SheetTemplate template, Project project, Action<BinderItem> navigate, Action<string> linkClicked = null)
        {
            var result = new List<StackPanel>();
            var image = project == null ? null : project.FindImage(item.ImageId);
            var source = image == null ? null : MediaView.TryImage(image.Bytes, 480);
            if (source != null)
            {
                var portrait = new StackPanel();
                portrait.Children.Add(new Image { Source = source, Stretch = Stretch.Uniform, MaxHeight = 240 });
                result.Add(portrait);
            }
            var names = new List<string> { "" };
            if (template != null)
            {
                foreach (var section in template.Sections)
                    if (SectionKey(names, section) == null) names.Add(section);
                foreach (var field in template.Fields)
                    if (SectionKey(names, field.Group) == null) names.Add(field.Group ?? "");
            }
            foreach (var entry in item.FreeInfo)
                if (SectionKey(names, entry.Group) == null) names.Add(entry.Group ?? "");
            foreach (var name in names)
            {
                var paper = new StackPanel();
                paper.Children.Add(PaperCaption(name.Length == 0 ? SheetDefaults.DefaultSectionLabel : name));
                if (template != null)
                    foreach (var field in template.Fields)
                    {
                        if (!string.Equals(SectionKey(names, field.Group), name, StringComparison.Ordinal)) continue;
                        string value;
                        item.FieldValues.TryGetValue(field.Id, out value);
                        if (string.IsNullOrEmpty(value)) continue;
                        AddRow(paper, field.Name, field.Kind, value, project, navigate, linkClicked);
                    }
                foreach (var entry in item.FreeInfo)
                    if (!string.IsNullOrEmpty(entry.Value) && string.Equals(SectionKey(names, entry.Group), name, StringComparison.Ordinal))
                        AddRow(paper, entry.Title, entry.Kind, entry.Value, project, navigate, linkClicked);
                if (paper.Children.Count > 1) result.Add(paper);
            }
            return result;
        }

        /// <summary>Le nom de section déjà connu qui correspond (sans casse), ou null — comme SheetView.</summary>
        private static string SectionKey(List<string> names, string group)
        {
            var wanted = group ?? "";
            foreach (var name in names)
                if (string.Equals(name, wanted, StringComparison.CurrentCultureIgnoreCase)) return name;
            return null;
        }

        /// <summary>Relations : NOM (nature), le nom cliquable vers une fiche.
        /// Null sans relation à montrer.</summary>
        private static StackPanel BuildRelations(BinderItem item, Project project, Action<BinderItem> navigate)
        {
            if (item.Relations.Count == 0) return null;
            var paper = new StackPanel();
            paper.Children.Add(PaperCaption("Relations"));
            foreach (var relation in item.Relations)
            {
                var target = project == null || relation.TargetId == null ? null : project.FindById(relation.TargetId);
                var label = target != null ? target.Title : relation.Name;
                var kind = RelationKinds.Canonical(relation.Kind);
                if (label.Length == 0 && kind.Length == 0) continue;
                var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                line.Inlines.Add(Anchor(line, label, target, navigate));
                if (kind.Length > 0)
                    line.Inlines.Add(new Run((label.Length > 0 ? " (" : "(") + kind + ")") { Foreground = Chrome.SoftText });
                paper.Children.Add(line);
            }
            return paper.Children.Count > 1 ? paper : null;
        }

        /// <summary>« Évolution et présence » (b47, groupées au 21/09) : les
        /// étapes dans l'ordre du récit (le même que la fiche — écrit ou nom
        /// libre, puis la note), et les écrits suivis où un nom de la fiche
        /// apparaît, avec le compte. Null si rien des deux.</summary>
        private static StackPanel BuildEvolutionPresence(BinderItem item, SheetTemplate template, Project project, Action<BinderItem> navigate)
        {
            var paper = new StackPanel();
            var showSteps = (template != null && template.Evolution) || item.Evolution.Count > 0;
            if (showSteps)
            {
                var any = false;
                foreach (var step in Presence.OrderedSteps(item, project))
                {
                    var label = step.Label(project);
                    var note = step.Note.Trim();
                    if (label.Length == 0 && note.Length == 0) continue;
                    if (!any) { paper.Children.Add(PaperCaption("Évolution")); any = true; }
                    var text = project == null || step.TextId == null ? null : project.FindById(step.TextId);
                    var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                    if (label.Length > 0)
                    {
                        line.Inlines.Add(text != null ? Anchor(line, label, text, navigate) : new Run(label) { Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold });
                        if (note.Length > 0) line.Inlines.Add(new Run(" — ") { Foreground = Chrome.SoftText });
                    }
                    if (note.Length > 0) line.Inlines.Add(new Run(note) { Foreground = Chrome.Ink });
                    paper.Children.Add(line);
                }
            }
            var rows = Presence.Of(item, template, project);
            if (rows.Count > 0)
            {
                paper.Children.Add(GroupCaption("Présence"));
                if (paper.Children.Count == 1) ((TextBlock)paper.Children[0]).Margin = new Thickness(0);
                foreach (var row in rows)
                {
                    var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                    line.Inlines.Add(Anchor(line, row.Text.Title, row.Text, navigate));
                    line.Inlines.Add(new Run(" · " + row.Count) { Foreground = Chrome.SoftText });
                    paper.Children.Add(line);
                }
            }
            return paper.Children.Count > 0 ? paper : null;
        }

        /// <summary>Le graph statistique (b47 bis) : la toile en petit, si le
        /// modèle l'active et que la fiche a une valeur. Null sinon.</summary>
        private static StackPanel BuildGraph(BinderItem item, SheetTemplate template)
        {
            if (template == null || !template.ShowsRadar || !RadarChart.HasValues(template, item.RadarValues)) return null;
            var paper = new StackPanel();
            paper.Children.Add(PaperCaption(template.RadarLabel));
            var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 2) };
            RadarChart.Draw(canvas, template, item.RadarValues, 220, true);
            paper.Children.Add(canvas);
            return paper;
        }

        /// <summary>Un lien vers une fiche, EN LIGNE (29/09) : un Run accent
        /// souligné — le même texte, la même ligne de base que le reste, plus
        /// aucun décalage — et la ligne retrouve le lien sous la souris par
        /// son TextLayout (HitTestPoint) : clic = navigation, main au survol.
        /// (Un InlineUIContainer, même centré, flottait d'un ou deux pixels.)</summary>
        private sealed class LinkSpan { public int Start, Length; public BinderItem Target; public string Title; }

        /// <summary>title/linkClicked (07/10) : un [[lien]] d'un champ dont la
        /// cible n'existe pas encore — le clic passe le titre à la coquille,
        /// qui propose de créer la fiche.</summary>
        private static Inline Anchor(TextBlock line, string label, BinderItem target, Action<BinderItem> navigate,
            string title = null, Action<string> linkClicked = null)
        {
            if ((target == null || navigate == null) && (title == null || linkClicked == null)) return new Run(label) { Foreground = Chrome.Ink };
            var spans = line.Tag as List<LinkSpan>;
            if (spans == null)
            {
                spans = new List<LinkSpan>();
                line.Tag = spans;
                line.Background = Brushes.Transparent;
                line.PointerMoved += delegate(object sender, PointerEventArgs e)
                {
                    line.Cursor = SpanAt(line, e.GetPosition(line)) != null
                        ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
                };
                line.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
                {
                    if (!e.GetCurrentPoint(line).Properties.IsLeftButtonPressed) return;
                    var span = SpanAt(line, e.GetPosition(line));
                    if (span == null) return;
                    e.Handled = true;
                    if (span.Target != null && navigate != null) navigate(span.Target);
                    else if (span.Title != null && linkClicked != null) linkClicked(span.Title);
                };
            }
            spans.Add(new LinkSpan { Start = TextLengthOf(line), Length = label.Length, Target = target, Title = title });
            return new Run(label) { Foreground = Chrome.Accent, TextDecorations = TextDecorations.Underline };
        }

        private static int TextLengthOf(TextBlock line)
        {
            var length = 0;
            foreach (var inline in line.Inlines)
            {
                var run = inline as Run;
                if (run != null) length += (run.Text ?? "").Length;
                else if (inline is LineBreak) length += 1;
            }
            return length;
        }

        private static LinkSpan SpanAt(TextBlock line, Point point)
        {
            var spans = line.Tag as List<LinkSpan>;
            if (spans == null) return null;
            var position = TextHit.PositionAt(line, point);
            if (position < 0) return null;
            foreach (var span in spans)
                if (position >= span.Start && position < span.Start + span.Length) return span;
            return null;
        }

        public static TextBlock GroupCaption(string text)
        {
            return new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Chrome.Accent, Margin = new Thickness(0, 8, 0, 0) };
        }

        /// <summary>La légende de tête d'un paper : sans l'écart du haut.</summary>
        private static TextBlock PaperCaption(string text)
        {
            var caption = GroupCaption(text);
            caption.Margin = new Thickness(0);
            return caption;
        }

        /// <summary>Une ligne de l'infobox selon la nature (b47 bis) : une note
        /// en ronds, une liste en chips, une fiche liée cliquable, le reste
        /// en texte.</summary>
        private static void AddRow(StackPanel infobox, string label, string kind, string value, Project project, Action<BinderItem> navigate, Action<string> linkClicked = null)
        {
            infobox.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Chrome.SoftText, Margin = new Thickness(0, 4, 0, 0) }); // gras franc (29/09) : le demi-gras se lisait comme du maigre
            switch (FieldKinds.Normalize(kind))
            {
                case FieldKinds.List:
                {
                    // Une liste à puces (refonte 07/10 : plus de pastilles en
                    // lecture), chaque élément avec ses [[liens]] cliquables.
                    var list = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
                    foreach (var entry in FieldKinds.ListItems(value))
                    {
                        var line = new DockPanel { Margin = new Thickness(4, 0, 0, 1) };
                        var bullet = new TextBlock { Text = "•", FontSize = 12, Foreground = Chrome.Accent, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Top };
                        DockPanel.SetDock(bullet, Dock.Left);
                        line.Children.Add(bullet);
                        line.Children.Add(LinkedText(entry, project, navigate, linkClicked));
                        list.Children.Add(line);
                    }
                    infobox.Children.Add(list);
                    return;
                }
                case FieldKinds.Sheet:
                {
                    var target = FieldKinds.SheetOf(value, project);
                    var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
                    line.Inlines.Add(Anchor(line, FieldKinds.Display(kind, value, project), target, navigate));
                    infobox.Children.Add(line);
                    return;
                }
                case FieldKinds.Rating:
                    infobox.Children.Add(new TextBlock { Text = FieldKinds.Display(kind, value, project), FontSize = 13, Foreground = Chrome.Accent });
                    return;
                default:
                    // Les champs textuels portent des [[liens]] vers d'autres
                    // fiches (07/10) : rendus comme dans le corps, cliquables.
                    infobox.Children.Add(LinkedText(FieldKinds.Display(kind, value, project), project, navigate, linkClicked));
                    return;
            }
        }

        /// <summary>Un texte de champ avec ses [[liens]] (07/10) : le texte
        /// affiché de chaque lien en accent souligné, les marques cachées ;
        /// la cible existante ouvre la fiche, une cible à créer passe par la
        /// coquille (linkClicked). Sans lien : un TextBlock ordinaire.</summary>
        private static TextBlock LinkedText(string value, Project project, Action<BinderItem> navigate, Action<string> linkClicked)
        {
            var line = new TextBlock { FontSize = 12, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap };
            var links = Links.Find(value);
            if (links.Count == 0) { line.Text = value; return line; }
            var cursor = 0;
            foreach (var link in links)
            {
                if (link.Start > cursor) line.Inlines.Add(new Run(value.Substring(cursor, link.Start - cursor)) { Foreground = Chrome.Ink });
                var target = project == null ? null : project.FindByTitle(link.Target);
                line.Inlines.Add(Anchor(line, link.Text, target, navigate, link.Target, linkClicked));
                cursor = link.End;
            }
            if (cursor < value.Length) line.Inlines.Add(new Run(value.Substring(cursor)) { Foreground = Chrome.Ink });
            return line;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
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
        public static UIElement Build(BinderItem item, SheetTemplate template, Project project, string body,
            bool column, Action<BinderItem> navigate, Action<string> linkClicked, Action<int> taskToggled)
        {
            var page = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = column ? new Thickness(0) : new Thickness(24, 20, 24, 20),
                Padding = column ? new Thickness(16, 14, 16, 16) : new Thickness(28),
                MaxWidth = column ? double.PositiveInfinity : 900
            };
            var papers = BuildPapers(item, template, project, navigate);
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

        private static UIElement BuildHead(BinderItem item, SheetTemplate template, Project project, bool column)
        {
            var head = new StackPanel();
            head.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = column ? 20 : 26,
                FontWeight = FontWeights.Bold,
                Foreground = Chrome.PaperInk,
                TextWrapping = TextWrapping.Wrap
            });
            var category = project == null ? null : project.SheetCategoryOf(item);
            head.Children.Add(new TextBlock
            {
                Text = category != null ? category.Name : template != null ? template.Name : "Fiche",
                FontSize = 12,
                Foreground = Chrome.PaperSoftInk,
                Margin = new Thickness(0, 2, 0, 8)
            });
            head.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 0, 0, column ? 0 : 12) });
            return head;
        }

        private static UIElement BuildBody(string body, Action<string> linkClicked, Action<int> taskToggled)
        {
            var flow = MarkdownRender.Build(body ?? "", linkClicked, taskToggled);
            return new FlowDocumentScrollViewer
            {
                Document = flow,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsToolBarVisible = false,
                Focusable = false
            };
        }

        // ================================================================ papers

        /// <summary>Les papers, dans l'ordre : infobox, Relations, « Évolution
        /// et présence », graph statistique — vides écartés.</summary>
        private static List<Border> BuildPapers(BinderItem item, SheetTemplate template, Project project, Action<BinderItem> navigate)
        {
            var papers = new List<Border>();
            var infobox = BuildInfobox(item, template, project, navigate);
            if (infobox.Children.Count > 0) papers.Add(Frame(infobox));
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

        /// <summary>L'infobox : portrait, champs de modèle remplis (par
        /// groupe), champs libres.</summary>
        private static StackPanel BuildInfobox(BinderItem item, SheetTemplate template, Project project, Action<BinderItem> navigate)
        {
            var infobox = new StackPanel();
            var image = project == null ? null : project.FindImage(item.ImageId);
            var source = image == null ? null : MediaView.TryImage(image.Bytes, 480);
            if (source != null)
                infobox.Children.Add(new Image { Source = source, Stretch = Stretch.Uniform, MaxHeight = 240, Margin = new Thickness(0, 0, 0, 10) });
            if (template != null)
            {
                string lastGroup = null;
                foreach (var field in template.Fields)
                {
                    string value;
                    item.FieldValues.TryGetValue(field.Id, out value);
                    if (string.IsNullOrEmpty(value)) continue;
                    if (field.Group.Length > 0 && field.Group != lastGroup)
                        infobox.Children.Add(GroupCaption(field.Group));
                    lastGroup = field.Group.Length > 0 ? field.Group : lastGroup;
                    AddRow(infobox, field.Name, field.Kind, value, project, navigate);
                }
            }
            foreach (var entry in item.FreeInfo)
                if (!string.IsNullOrEmpty(entry.Value))
                    AddRow(infobox, entry.Title, entry.Kind, entry.Value, project, navigate);
            return infobox;
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
                line.Inlines.Add(Anchor(label, target, navigate));
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
                        line.Inlines.Add(text != null ? Anchor(label, text, navigate) : new Run(label) { Foreground = Chrome.Ink, FontWeight = FontWeights.SemiBold });
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
                    line.Inlines.Add(Anchor(row.Text.Title, row.Text, navigate));
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

        private static Run Anchor(string label, BinderItem target, Action<BinderItem> navigate)
        {
            if (target == null || navigate == null) return new Run(label) { Foreground = Chrome.Ink };
            var anchor = new Run(label) { Foreground = Chrome.Accent, Cursor = Cursors.Hand };
            anchor.MouseLeftButtonDown += delegate { navigate(target); };
            return anchor;
        }

        public static TextBlock GroupCaption(string text)
        {
            return new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Chrome.Accent, Margin = new Thickness(0, 8, 0, 0) };
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
        private static void AddRow(StackPanel infobox, string label, string kind, string value, Project project, Action<BinderItem> navigate)
        {
            infobox.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Chrome.SoftText, Margin = new Thickness(0, 4, 0, 0) });
            switch (FieldKinds.Normalize(kind))
            {
                case FieldKinds.List:
                {
                    var chips = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
                    foreach (var entry in FieldKinds.ListItems(value)) chips.Children.Add(FieldEditors.Chip(entry));
                    infobox.Children.Add(chips);
                    return;
                }
                case FieldKinds.Sheet:
                {
                    var target = FieldKinds.SheetOf(value, project);
                    var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
                    line.Inlines.Add(Anchor(FieldKinds.Display(kind, value, project), target, navigate));
                    infobox.Children.Add(line);
                    return;
                }
                case FieldKinds.Rating:
                    infobox.Children.Add(new TextBlock { Text = FieldKinds.Display(kind, value, project), FontSize = 13, Foreground = Chrome.Accent });
                    return;
                default:
                    infobox.Children.Add(new TextBlock { Text = FieldKinds.Display(kind, value, project), FontSize = 12, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap });
                    return;
            }
        }
    }
}

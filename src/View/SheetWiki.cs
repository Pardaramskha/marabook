using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>LE RENDU WIKI d'une fiche (extrait de SheetView au batch 47) :
    /// grand titre, infobox (portrait, champs remplis groupés, champs libres,
    /// relations, évolution, présence), corps markdown. Deux mises en page :
    /// « page » = l'infobox à droite du corps (le mode wiki de la fiche) ;
    /// « colonne » = tout empilé (l'épinglé de la colonne de droite, b47).
    /// Lecture seule par nature : rien ici n'écrit dans la fiche, sauf le
    /// basculement d'une case à cocher du corps, rendu au demandeur.</summary>
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
            var infobox = BuildInfobox(item, template, project, navigate);
            var infoboxFrame = infobox.Children.Count == 0 ? null : new Border
            {
                Background = Chrome.BarBgLight,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                VerticalAlignment = VerticalAlignment.Top,
                Child = infobox
            };
            if (column)
            {
                // En colonne : le titre, l'infobox, puis le corps — empilés.
                var stack = new StackPanel();
                stack.Children.Add(BuildHead(item, template, project, true));
                if (infoboxFrame != null)
                {
                    infoboxFrame.Margin = new Thickness(0, 10, 0, 12);
                    stack.Children.Add(infoboxFrame);
                }
                stack.Children.Add(BuildBody(body, linkClicked, taskToggled));
                page.Child = stack;
                return page;
            }
            var layout = new DockPanel { LastChildFill = true };
            if (infoboxFrame != null)
            {
                infoboxFrame.Width = 250;
                infoboxFrame.Margin = new Thickness(20, 6, 0, 0);
                DockPanel.SetDock(infoboxFrame, Dock.Right);
                layout.Children.Add(infoboxFrame);
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

        /// <summary>L'infobox : portrait, champs de modèle remplis (par
        /// groupe), champs libres, relations (NOM (nature), le nom cliquable
        /// vers une fiche), l'évolution (écrit — note) et la présence
        /// (écrits où les noms apparaissent, avec le compte) — b47.</summary>
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
            // Le radar (b47 bis) : la toile en petit, si le modèle l'active
            // et que la fiche a une valeur.
            if (template != null && template.ShowsRadar && RadarChart.HasValues(template, item.RadarValues))
            {
                infobox.Children.Add(GroupCaption("Radar"));
                var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 2) };
                RadarChart.Draw(canvas, template, item.RadarValues, 220, true);
                infobox.Children.Add(canvas);
            }
            if (item.Relations.Count > 0)
            {
                infobox.Children.Add(GroupCaption("Relations"));
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
                    infobox.Children.Add(line);
                }
            }
            // Évolution (b47) : une ligne par étape, dans l'ordre du récit.
            var steps = Presence.OrderedSteps(item, project);
            var any = false;
            foreach (var step in steps)
            {
                if (step.Note.Trim().Length == 0) continue;
                if (!any) { infobox.Children.Add(GroupCaption("Évolution")); any = true; }
                var text = project == null || step.TextId == null ? null : project.FindById(step.TextId);
                var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                if (text != null)
                {
                    line.Inlines.Add(Anchor(text.Title, text, navigate));
                    line.Inlines.Add(new Run(" — ") { Foreground = Chrome.SoftText });
                }
                line.Inlines.Add(new Run(step.Note.Trim()) { Foreground = Chrome.Ink });
                infobox.Children.Add(line);
            }
            // Présence (b47) : les écrits où un nom de la fiche apparaît.
            var rows = Presence.Of(item, template, project);
            if (rows.Count > 0)
            {
                infobox.Children.Add(GroupCaption("Présence"));
                foreach (var row in rows)
                {
                    var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                    line.Inlines.Add(Anchor(row.Text.Title, row.Text, navigate));
                    line.Inlines.Add(new Run(" · " + row.Count) { Foreground = Chrome.SoftText });
                    infobox.Children.Add(line);
                }
            }
            return infobox;
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

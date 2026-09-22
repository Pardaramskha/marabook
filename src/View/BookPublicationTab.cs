using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marabook.Correction;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>L'onglet « Publication » de la page livre (22/09) : le bouton
    /// principal « Publier », puis la CHECK-LIST du livre — chapitres
    /// terminés, liminaires obligatoires, gabarit et mise en page, métadonnées
    /// d'édition — et les tailles : mots, signes et pages du récit seul, puis
    /// du livre entier (liminaires, pages de fin et annexes comprises). C'est
    /// ici que vivra le bouton de création d'EPUB. Les pages sont comptées à
    /// l'affichage de l'onglet (Refresh), jamais à la sélection du livre.</summary>
    public class BookPublicationTab : StackPanel
    {
        private BinderItem _item;
        private Project _project;
        private Func<BinderItem, int> _pageCounter;
        private readonly StackPanel _checklist;
        private readonly Grid _sizes;

        public event Action<BinderItem> PublishRequested;

        public BookPublicationTab()
        {
            Margin = new Thickness(24, SheetLibraryView.TopGap, 24, 24);
            MaxWidth = 760;
            HorizontalAlignment = HorizontalAlignment.Left;

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            // NB : le gabarit de bouton du thème peint son propre fond — ne
            // jamais forcer Background/Foreground ici (bouton « tout blanc »).
            var publish = Buttons.IconText("play-fill", "Publier…",
                "Compiler tout le livre en un PDF prêt à imprimer : pagination "
                + "continue, gabarits appliqués, CMJN FOGRA39 par défaut", Buttons.Bar, Buttons.Look.Primary);
            publish.Click += delegate
            {
                var handler = PublishRequested;
                if (handler != null && _item != null) handler(_item);
            };
            actions.Children.Add(publish);
            var epub = Buttons.IconText("book-open-text-bold", "Créer un EPUB", "À venir : le livre en EPUB, pour économiser du papier", Buttons.Bar, Buttons.Look.Outline);
            epub.IsEnabled = false;
            epub.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(epub);
            Children.Add(actions);

            Children.Add(BookPanelParts.Caption("Check-list", 18));
            _checklist = new StackPanel();
            Children.Add(_checklist);

            Children.Add(BookPanelParts.Caption("Tailles", 18));
            _sizes = new Grid();
            for (var c = 0; c < 4; c++)
                _sizes.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(260) : new GridLength(110) });
            Children.Add(_sizes);
        }

        public void Load(BinderItem book, Project project, Func<BinderItem, int> pageCounter)
        {
            _item = book;
            _project = project;
            _pageCounter = pageCounter;
            if (book != null && book.Book == null) book.Book = new BookInfo();
        }

        public void Clear() { _item = null; }

        /// <summary>Recalcule la check-list et les tailles (à l'affichage de l'onglet).</summary>
        public void Refresh()
        {
            _checklist.Children.Clear();
            _sizes.Children.Clear();
            _sizes.RowDefinitions.Clear();
            if (_item == null || _item.Book == null) return;
            var book = _item.Book;
            var culture = CultureInfo.CurrentCulture;

            // — Chapitres.
            var progress = BookProgress.Of(_item);
            var notDone = new List<string>();
            var story = new List<BinderItem>();
            BookProgress.StoryTexts(_item, story);
            foreach (var text in story) if (text.Status != "done") notDone.Add(text.Title);
            Row(story.Count > 0 && notDone.Count == 0,
                story.Count == 0 ? "Aucun chapitre dans le livre"
                    : notDone.Count == 0 ? "Les " + story.Count + " chapitres sont notés « Terminé »"
                    : progress.Done + " / " + story.Count + " chapitres notés « Terminé »",
                notDone.Count == 0 ? null : "À finir : " + Join(notDone, 5));
            if (progress.HasGoal)
                Row(progress.Present >= progress.Goal,
                    "Objectif de chapitres : " + progress.Present + " / " + progress.Goal, null);

            // — Liminaires obligatoires : page de titre et table des matières.
            var front = 0; var back = 0; var annex = 0;
            var hasTitle = false; var hasToc = false;
            foreach (var item in Descendants(_item))
            {
                if (item.Kind != ItemKind.Text || !item.IsExtraPage) continue;
                var section = ExtraPages.SectionOf(item);
                if (section == ExtraPages.SectionBack) back++;
                else if (section == ExtraPages.SectionAnnex) annex++;
                else front++;
                if (item.ExtraKind == ExtraPages.KindTitle) hasTitle = true;
                if (item.IsToc || item.ExtraKind == ExtraPages.KindToc || item.ExtraKind == ExtraPages.KindTocBack) hasToc = true;
            }
            Row(hasTitle, hasTitle ? "Page de titre présente" : "Pas de page de titre", hasTitle ? null : "Textes › Nouvelle liminaire › Page de titre");
            Row(hasToc, hasToc ? "Table des matières présente" : "Pas de table des matières", hasToc ? null : "Textes › Nouvelle liminaire › Table des matières");
            Info(front + " liminaire" + (front > 1 ? "s" : "") + ", " + back + " page" + (back > 1 ? "s" : "") + " de fin, " + annex + " annexe" + (annex > 1 ? "s" : ""));

            // — Gabarit et mise en page.
            var templates = 0;
            foreach (var child in _item.Children) if (child.Kind == ItemKind.PageTemplate) templates++;
            Row(templates > 0, templates > 0 ? templates + " gabarit" + (templates > 1 ? "s" : "") + " de pages" : "Aucun gabarit de pages (en-têtes, pieds, folios)", templates > 0 ? null : "Gabarits & Format › Nouveau gabarit");
            var divergent = BookFormatPanel.DivergentCount(_item, _project);
            Row(divergent == 0, divergent == 0 ? "Tous les textes suivent le format du livre" : divergent + " texte" + (divergent > 1 ? "s ne suivent" : " ne suit") + " pas le format du livre",
                divergent == 0 ? null : "Icône orange à côté de leur titre → clic droit › Appliquer le gabarit du livre");

            // — Métadonnées d'édition.
            var missing = new List<string>();
            var author = book.AuthorOverride.Trim().Length > 0 ? book.AuthorOverride
                : _project != null && _project.Author.Trim().Length > 0 ? _project.Author : Defaults.Author;
            if (author.Trim().Length == 0) missing.Add("auteur·ice");
            if (book.Isbn.Trim().Length == 0) missing.Add("ISBN");
            if (book.Publisher.Trim().Length == 0) missing.Add("éditeur");
            if (book.Year.Trim().Length == 0) missing.Add("année");
            if (book.Genre.Trim().Length == 0) missing.Add("genre");
            if (book.Audience.Trim().Length == 0) missing.Add("public cible");
            if (book.Themes.Count == 0) missing.Add("thématiques");
            if (book.Pitch.Trim().Length == 0) missing.Add("accroche");
            if (book.BackCover.Trim().Length == 0) missing.Add("quatrième de couverture");
            Row(missing.Count == 0, missing.Count == 0 ? "Métadonnées d'édition complètes" : "Métadonnées d'édition incomplètes",
                missing.Count == 0 ? null : "Manque : " + Join(missing, 9) + " (onglet Édition)");
            Row(_item.ImageId != null, _item.ImageId != null ? "Couverture posée" : "Pas de couverture", _item.ImageId != null ? null : "Onglet Édition › Couverture");

            // — Tailles : le récit seul, puis tout ce qui s'imprime.
            var all = new List<BinderItem>();
            foreach (var item in Descendants(_item)) if (item.Kind == ItemKind.Text) all.Add(item);
            SizeHeader();
            SizeRow("Récit (chapitres)", story);
            var extras = new List<BinderItem>();
            foreach (var item in all) if (item.IsExtraPage || item.IsToc) extras.Add(item);
            SizeRow("Liminaires, pages de fin, annexes", extras);
            SizeRow("Livre entier", all);
        }

        private void SizeHeader()
        {
            _sizes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = _sizes.RowDefinitions.Count - 1;
            string[] heads = { "", "Mots", "Signes", "Pages" };
            for (var c = 0; c < 4; c++)
                Cell(row, c, new TextBlock { Text = heads[c], Foreground = Chrome.FaintText, FontSize = 11, Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right });
        }

        private void SizeRow(string label, List<BinderItem> texts)
        {
            var words = 0; var signs = 0; var pages = 0;
            foreach (var text in texts)
            {
                var stats = TextStats.Compute(text.Document.ToPlainText());
                words += stats.Words;
                signs += stats.Sec;
                if (_pageCounter != null)
                {
                    if (pages % 2 == 1) pages++; // chaque document ouvre un recto
                    pages += _pageCounter(text);
                }
            }
            var culture = CultureInfo.CurrentCulture;
            _sizes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = _sizes.RowDefinitions.Count - 1;
            Cell(row, 0, new TextBlock { Text = label, Foreground = Chrome.Ink, Margin = new Thickness(0, 2, 0, 2) });
            Cell(row, 1, Number(words.ToString("N0", culture)));
            Cell(row, 2, Number(signs.ToString("N0", culture)));
            Cell(row, 3, Number(_pageCounter == null ? "—" : pages.ToString("N0", culture)));
        }

        private static TextBlock Number(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.Ink, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 2) };
        }

        private void Cell(int row, int column, UIElement element)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            _sizes.Children.Add(element);
        }

        /// <summary>Une ligne de la check-list : coche verte ou alerte orange,
        /// le constat, et le remède en dessous.</summary>
        private void Row(bool ok, string text, string remedy)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var icon = Icons.Make(ok ? "check-square-bold" : "warning-fill", 14,
                ok ? (Brush)Chrome.Ok : new SolidColorBrush(Color.FromRgb(230, 126, 34))) as FrameworkElement;
            if (icon != null)
            {
                icon.VerticalAlignment = VerticalAlignment.Top;
                icon.Margin = new Thickness(0, 3, 8, 0);
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);
            }
            var lines = new StackPanel();
            lines.Children.Add(new TextBlock { Text = text, Foreground = Chrome.Ink, TextWrapping = TextWrapping.Wrap });
            if (remedy != null)
                lines.Children.Add(new TextBlock { Text = remedy, Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(lines);
            _checklist.Children.Add(row);
        }

        private void Info(string text)
        {
            _checklist.Children.Add(new TextBlock { Text = text, Foreground = Chrome.SoftText, FontSize = 11, Margin = new Thickness(22, 0, 0, 4) });
        }

        private static string Join(List<string> items, int max)
        {
            var shown = items.Count > max ? items.GetRange(0, max) : items;
            return string.Join(", ", shown.ToArray()) + (items.Count > max ? "… (+" + (items.Count - max) + ")" : "");
        }

        private static IEnumerable<BinderItem> Descendants(BinderItem item)
        {
            foreach (var child in item.Children)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
}

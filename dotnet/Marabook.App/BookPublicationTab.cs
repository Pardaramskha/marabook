using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Correction;
using Marabook.Model;

namespace Marabook.App
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
        private readonly StackPanel _stats; // Statistiques : un texte par ligne (18/10), surlignée au survol (24/10)
        private readonly Expander _statsBox;

        public event Action<BinderItem> PublishRequested;
        public event Action<BinderItem> EpubRequested; // « Créer un EPUB » (22/09)

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
            var epub = Buttons.IconText("book-open-text-bold", "Créer un EPUB…", "Le livre en EPUB 3, pour les liseuses — titre, métadonnées et couverture au dialogue", Buttons.Bar, Buttons.Look.Outline);
            epub.Margin = new Thickness(8, 0, 0, 0);
            epub.Click += delegate
            {
                var handler = EpubRequested;
                if (handler != null && _item != null) handler(_item);
            };
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
            // L'accordéon « Statistiques » (18/10, 1.0.6) : mots, signes et
            // pages de CHAQUE texte du livre, dans l'ordre des pages.
            _stats = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            _statsBox = new Expander
            {
                Header = new TextBlock { Text = "Statistiques", Foreground = Chrome.SoftText, FontSize = 12 },
                Content = _stats,
                IsExpanded = false,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = "stats-expander" // sonde
            };
            Children.Add(_statsBox);
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
            _stats.Children.Clear();
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
            // Critique (18/10) : un texte hors gabarit sort en rouge.
            var divergent = BookFormatPanel.DivergentCount(_item, _project);
            Row(divergent == 0, divergent == 0 ? "Tous les textes suivent le format du livre" : divergent + " texte" + (divergent > 1 ? "s ne suivent" : " ne suit") + " pas le format du livre",
                divergent == 0 ? null : "Icône orange à côté de leur titre → clic droit › Appliquer le gabarit du livre", true, "template-row");
            // L'enchaînement des pages (18/10), si le livre veut l'avertissement :
            // critique aussi — un chapitre ou le livre finissant sur une page impaire.
            if (BookFlow.Enabled(_item) && _pageCounter != null)
            {
                var flow = BookFlow.Analyse(_item, _pageCounter);
                var odd = flow.OddChapters;
                var names = new List<string>();
                foreach (var text in odd) names.Add(text.Title + " (p. " + flow.LastPage[text.Id] + ")");
                var constat = odd.Count == 0 ? "Enchaînement des pages : chaque chapitre finit sur une page paire"
                    : odd.Count == 1 ? "Enchaînement des pages : un chapitre finit sur une page impaire"
                    : "Enchaînement des pages : " + odd.Count + " chapitres finissent sur une page impaire";
                if (flow.BookEndsOdd) constat += " — le livre aussi (" + flow.Total + " pages)";
                Row(odd.Count == 0, constat,
                    odd.Count == 0 ? null : "Pastille à côté de leur titre (Pile, Textes) → clic droit › Unifier ajoute une page blanche : " + Join(names, 5),
                    true, "flow-row");
            }

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

            // — Statistiques : un texte par ligne, dans l'ordre des pages.
            StatsHeader();
            var ordered = new List<BinderItem>();
            BookFlow.Texts(_item, ordered);
            foreach (var text in ordered) StatsRow(text);
            _statsBox.IsVisible = ordered.Count > 0;
        }

        /// <summary>Une ligne de l'accordéon : quatre colonnes aux largeurs des
        /// Tailles, dans un cadre qui se surligne au survol (24/10) — la ligne
        /// entière, pour la lire d'un trait.</summary>
        private static Grid StatsLine()
        {
            var grid = new Grid();
            for (var c = 0; c < 4; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c == 0 ? new GridLength(254) : new GridLength(110) });
            return grid;
        }

        private void StatsHeader()
        {
            var line = StatsLine();
            string[] heads = { "Texte", "Mots", "Signes", "Pages" };
            for (var c = 0; c < 4; c++)
                StatsCell(line, c, new TextBlock { Text = heads[c], Foreground = Chrome.FaintText, FontSize = 11, Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = c == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right });
            _stats.Children.Add(new Border { Child = line, Padding = new Thickness(6, 0, 6, 0) });
        }

        private void StatsRow(BinderItem text)
        {
            var stats = TextStats.Compute(text.Document.ToPlainText());
            var culture = CultureInfo.CurrentCulture;
            var line = StatsLine();
            StatsCell(line, 0, new TextBlock { Text = text.Title, Foreground = Chrome.Ink, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 8, 2) });
            StatsCell(line, 1, Number(stats.Words.ToString("N0", culture)));
            StatsCell(line, 2, Number(stats.Sec.ToString("N0", culture)));
            StatsCell(line, 3, Number(_pageCounter == null ? "—" : _pageCounter(text).ToString("N0", culture)));
            var row = new Border
            {
                Child = line,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Tag = "stats-row" // sonde
            };
            row.PointerEntered += delegate { HoverStatsRow(row, true); };
            row.PointerExited += delegate { HoverStatsRow(row, false); };
            _stats.Children.Add(row);
        }

        private static void HoverStatsRow(Border row, bool on)
        {
            row.Background = on ? (IBrush)Chrome.AccentTint : Brushes.Transparent;
        }

        private static void StatsCell(Grid line, int column, Control element)
        {
            Grid.SetColumn(element, column);
            line.Children.Add(element);
        }

        // — Sonde (18/10, 24/10).
        internal int StatsRowsForProbe()
        {
            var count = 0;
            foreach (var child in _stats.Children) if ((child.Tag as string) == "stats-row") count++;
            return count;
        }
        internal bool HoverStatsRowForProbe(int index, bool on)
        {
            var seen = 0;
            foreach (var child in _stats.Children)
            {
                var row = child as Border;
                if (row == null || (row.Tag as string) != "stats-row") continue;
                if (seen++ != index) continue;
                HoverStatsRow(row, on);
                return ReferenceEquals(row.Background, Chrome.AccentTint) == on;
            }
            return false;
        }
        internal int DividersForProbe()
        {
            var count = 0;
            foreach (var child in _checklist.Children) if ((child.Tag as string) == "checklist-divider") count++;
            return count;
        }
        internal int RowsForProbe()
        {
            var count = 0;
            foreach (var child in _checklist.Children) if (child is DockPanel) count++;
            return count;
        }
        /// <summary>La ligne étiquetée : null si absente ; sinon vrai si elle est en rouge (critique et en défaut).</summary>
        internal bool? RowIsRedForProbe(string tag)
        {
            foreach (var child in _checklist.Children)
            {
                var row = child as DockPanel;
                if (row == null || (row.Tag as string) != tag) continue;
                foreach (var part in row.Children)
                {
                    var lines = part as StackPanel;
                    if (lines == null || lines.Children.Count == 0) continue;
                    var text = lines.Children[0] as TextBlock;
                    return text != null && ReferenceEquals(text.Foreground, Chrome.Danger);
                }
                return false;
            }
            return null;
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

        private void Cell(int row, int column, Control element)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            _sizes.Children.Add(element);
        }

        /// <summary>Une ligne de la check-list : coche verte ou alerte orange,
        /// le constat, et le remède en dessous. Les lignes sont séparées par
        /// un filet discret (18/10) ; une ligne CRITIQUE en défaut (gabarit,
        /// enchaînement) sort en rouge — les autres restent orange, elles
        /// n'empêchent pas la publication.</summary>
        private void Row(bool ok, string text, string remedy, bool critical = false, string tag = null)
        {
            if (_checklist.Children.Count > 0)
                _checklist.Children.Add(new Border
                {
                    Height = 1,
                    Background = Chrome.Border,
                    Margin = new Thickness(22, 3, 0, 3),
                    Tag = "checklist-divider"
                });
            var red = critical && !ok;
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3), Tag = tag };
            var icon = Icons.Make(ok ? "check-square-bold" : "warning-fill", 14,
                ok ? (IBrush)Chrome.Ok : red ? Chrome.Danger : new SolidColorBrush(Color.FromRgb(230, 126, 34))) as Control;
            if (icon != null)
            {
                icon.VerticalAlignment = VerticalAlignment.Top;
                icon.Margin = new Thickness(0, 3, 8, 0);
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);
            }
            var lines = new StackPanel();
            lines.Children.Add(new TextBlock { Text = text, Foreground = red ? Chrome.Danger : Chrome.Ink, TextWrapping = TextWrapping.Wrap });
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

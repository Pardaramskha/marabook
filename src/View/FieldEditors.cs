using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>LES ÉDITEURS DE CHAMP (b47 bis) : une nature (FieldKinds) →
    /// le bon petit outil de saisie, la valeur restant une chaîne. Un texte
    /// (une ligne ou long), un nombre (vérifié en douceur : la valeur passe
    /// en avertissement si elle ne commence pas par un nombre, jamais
    /// bloquée), une date (texte libre + calendrier pour une vraie date),
    /// une note sur 5 (ronds cliquables, re-cliquer la note efface), une
    /// liste (chips + saisie « a, b, c »), un choix (sélecteur des options
    /// du modèle), une fiche liée (sélecteur des fiches + ouvrir).
    /// onChanged est appelé à chaque changement VENANT DE L'UTILISATEUR,
    /// jamais à la construction.</summary>
    public static class FieldEditors
    {
        /// <summary>L'élément à poser dans la rangée. Son Tag est le contrôle
        /// à focaliser (la zone de texte d'un éditeur composé), s'il diffère.</summary>
        public static FrameworkElement Build(string kind, string value, List<string> options,
            Project project, BinderItem self, Action<string> onChanged, Action<BinderItem> navigate)
        {
            value = value ?? "";
            switch (FieldKinds.Normalize(kind))
            {
                case FieldKinds.Multiline: return TextEditor(value, true, onChanged);
                case FieldKinds.Number: return NumberEditor(value, onChanged);
                case FieldKinds.Date: return DateEditor(value, onChanged);
                case FieldKinds.Rating: return RatingEditor(value, onChanged);
                case FieldKinds.List: return ListEditor(value, onChanged);
                case FieldKinds.Choice: return ChoiceEditor(value, options, onChanged);
                case FieldKinds.Sheet: return SheetEditor(value, project, self, onChanged, navigate);
                default: return TextEditor(value, false, onChanged);
            }
        }

        /// <summary>Le contrôle à focaliser / sélectionner pour un éditeur (le
        /// TextBox d'un composé, l'élément lui-même sinon).</summary>
        public static Control FocusTarget(FrameworkElement editor)
        {
            if (editor == null) return null;
            var inner = editor.Tag as Control;
            return inner ?? editor as Control;
        }

        // ------------------------------------------------------------ texte

        private static TextBox TextEditor(string value, bool multiline, Action<string> onChanged)
        {
            var box = new TextBox { Text = value };
            if (multiline)
            {
                box.AcceptsReturn = true;
                box.TextWrapping = TextWrapping.Wrap;
                box.MinHeight = 52;
                box.VerticalContentAlignment = VerticalAlignment.Top;
            }
            box.TextChanged += delegate { onChanged(box.Text); };
            return box;
        }

        // ----------------------------------------------------------- nombre

        private static TextBox NumberEditor(string value, Action<string> onChanged)
        {
            var box = new TextBox { Text = value, ToolTip = "Un nombre, avec son unité si vous voulez : « 1,78 m », « 34 »" };
            var normal = box.Foreground;
            Action check = delegate
            {
                double n;
                var bad = box.Text.Trim().Length > 0 && !FieldKinds.TryNumber(box.Text, out n);
                box.Foreground = bad ? Chrome.Warn : normal;
                box.ToolTip = bad ? "Cette valeur ne commence pas par un nombre — elle est gardée telle quelle"
                    : "Un nombre, avec son unité si vous voulez : « 1,78 m », « 34 »";
            };
            check();
            box.TextChanged += delegate { check(); onChanged(box.Text); };
            return box;
        }

        // ------------------------------------------------------------- date

        /// <summary>Texte libre (« an 342 du Cycle » vaut) ; le bouton ouvre
        /// un calendrier qui écrit JJ/MM/AAAA.</summary>
        private static FrameworkElement DateEditor(string value, Action<string> onChanged)
        {
            var row = new DockPanel();
            var box = new TextBox { Text = value, ToolTip = "Une date du monde — réelle (JJ/MM/AAAA, le calendrier aide) ou libre (« an 342 du Cycle »)" };
            box.TextChanged += delegate { onChanged(box.Text); };
            var pick = new Button
            {
                Content = "📅",
                Width = 28,
                Padding = new Thickness(0, 2, 0, 2),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Choisir une date réelle",
                Focusable = false
            };
            var calendar = new System.Windows.Controls.Calendar { SelectionMode = CalendarSelectionMode.SingleDate };
            var popup = new Popup
            {
                PlacementTarget = pick,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Background = Chrome.RaisedBg,
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(4),
                    Child = calendar
                }
            };
            pick.Click += delegate
            {
                DateTime parsed;
                if (DateTime.TryParseExact(box.Text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    calendar.SelectedDate = calendar.DisplayDate = parsed;
                popup.IsOpen = true;
            };
            calendar.SelectedDatesChanged += delegate
            {
                if (calendar.SelectedDate == null) return;
                box.Text = calendar.SelectedDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                popup.IsOpen = false;
            };
            // Le calendrier garde le clic de sélection : sans ça, le premier
            // clic dehors est avalé (piège connu du Calendar WPF).
            calendar.PreviewMouseUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (Mouse.Captured is CalendarItem) Mouse.Capture(null);
            };
            DockPanel.SetDock(pick, Dock.Right);
            row.Children.Add(pick);
            row.Children.Add(box);
            row.Tag = box;
            return row;
        }

        // ------------------------------------------------------------- note

        private static FrameworkElement RatingEditor(string value, Action<string> onChanged)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var rating = FieldKinds.RatingOf(value);
            var dots = new List<Ellipse>();
            Action paint = delegate
            {
                for (var i = 0; i < dots.Count; i++)
                {
                    dots[i].Fill = i < rating ? (Brush)Chrome.Accent : Chrome.Border;
                    dots[i].Stroke = i < rating ? Chrome.AccentStrong : Chrome.BorderStrong;
                }
            };
            for (var i = 1; i <= FieldKinds.RatingMax; i++)
            {
                var dot = new Ellipse { Width = 16, Height = 16, StrokeThickness = 1, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand };
                var n = i;
                dot.ToolTip = n + " / " + FieldKinds.RatingMax;
                dot.MouseLeftButtonUp += delegate
                {
                    rating = rating == n ? 0 : n; // re-cliquer la note l'efface
                    paint();
                    onChanged(rating == 0 ? "" : rating.ToString(CultureInfo.InvariantCulture));
                };
                dots.Add(dot);
                row.Children.Add(dot);
            }
            paint();
            var label = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            Action relabel = delegate { label.Text = rating == 0 ? "—" : rating + " / " + FieldKinds.RatingMax; };
            relabel();
            foreach (var dot in dots) dot.MouseLeftButtonUp += delegate { relabel(); };
            row.Children.Add(label);
            return row;
        }

        // ------------------------------------------------------------ liste

        private static FrameworkElement ListEditor(string value, Action<string> onChanged)
        {
            var stack = new StackPanel();
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 3) };
            Action paint = delegate
            {
                chips.Children.Clear();
                foreach (var item in FieldKinds.ListItems(value)) chips.Children.Add(Chip(item));
                chips.Visibility = chips.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            paint();
            stack.Children.Add(chips);
            var box = new TextBox { Text = value, ToolTip = "Les éléments séparés par des virgules : « escrime, latin, cuisine »" };
            box.TextChanged += delegate { value = box.Text; paint(); onChanged(value); };
            stack.Children.Add(box);
            stack.Tag = box;
            return stack;
        }

        public static Border Chip(string text)
        {
            return new Border
            {
                Background = Chrome.AccentTint,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 1, 8, 2),
                Margin = new Thickness(0, 0, 4, 4),
                Child = new TextBlock { Text = text, FontSize = 11, Foreground = Chrome.AccentStrong }
            };
        }

        // ------------------------------------------------------------ choix

        private static FrameworkElement ChoiceEditor(string value, List<string> options, Action<string> onChanged)
        {
            var combo = new ComboBox { ToolTip = "Une valeur parmi les options du modèle" };
            combo.Items.Add("—");
            var selected = 0;
            if (options != null)
                foreach (var option in options)
                {
                    combo.Items.Add(option);
                    if (option == value) selected = combo.Items.Count - 1;
                }
            // Une valeur qui n'est plus dans les options (modèle édité) reste
            // proposée : rien ne se perd.
            if (value.Length > 0 && selected == 0)
            {
                combo.Items.Add(value);
                selected = combo.Items.Count - 1;
            }
            combo.SelectedIndex = selected;
            combo.SelectionChanged += delegate
            {
                if (combo.SelectedIndex < 0) return;
                onChanged(combo.SelectedIndex == 0 ? "" : (string)combo.SelectedItem);
            };
            return combo;
        }

        // ------------------------------------------------------ fiche liée

        private static FrameworkElement SheetEditor(string value, Project project, BinderItem self,
            Action<string> onChanged, Action<BinderItem> navigate)
        {
            var row = new DockPanel();
            var sheets = new List<BinderItem>();
            if (project != null)
            {
                foreach (var item in project.AllItems())
                    if (item.Kind == ItemKind.Sheet && item != self
                        && item.RootCategory().CategoryKey != Project.KeyTrash)
                        sheets.Add(item);
                SheetLibraryView.SortByTitle(sheets);
            }
            var combo = new ComboBox { ToolTip = "La fiche liée — une fiche du projet" };
            combo.Items.Add("—");
            var selected = 0;
            foreach (var sheet in sheets)
            {
                var category = project.SheetCategoryOf(sheet);
                combo.Items.Add(category != null ? sheet.Title + "  (" + category.Name + ")" : sheet.Title);
                if (sheet.Id == value) selected = combo.Items.Count - 1;
            }
            if (value.Length > 0 && selected == 0)
            {
                combo.Items.Add("(fiche disparue)");
                selected = combo.Items.Count - 1;
            }
            combo.SelectedIndex = selected;
            var open = new Button
            {
                Content = Icons.Make("arrow-up-right-bold", 11, Chrome.Ink),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "Ouvrir la fiche liée",
                Focusable = false,
                Visibility = selected > 0 && selected <= sheets.Count ? Visibility.Visible : Visibility.Collapsed
            };
            open.Click += delegate
            {
                var index = combo.SelectedIndex;
                if (index < 1 || index > sheets.Count || navigate == null) return;
                navigate(sheets[index - 1]);
            };
            combo.SelectionChanged += delegate
            {
                var index = combo.SelectedIndex;
                if (index < 0) return;
                var target = index >= 1 && index <= sheets.Count ? sheets[index - 1] : null;
                open.Visibility = target != null ? Visibility.Visible : Visibility.Collapsed;
                if (index > sheets.Count) return; // « (fiche disparue) » : on ne touche pas
                onChanged(target == null ? "" : target.Id);
            };
            DockPanel.SetDock(open, Dock.Right);
            row.Children.Add(open);
            row.Children.Add(combo);
            row.Tag = combo;
            return row;
        }
    }
}

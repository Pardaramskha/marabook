using System.Windows;
using System.Windows.Controls;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>« Options du livre » (batch 32) : le nom, l'icône de la Pile et
    /// l'objectif — un nombre de chapitres visé, rendu par la barre de
    /// progression de l'inspecteur (0 = pas d'objectif). Le dialogue ne touche
    /// pas au modèle : il rend un résultat que l'appelant applique en une
    /// action annulable.</summary>
    public class BookOptionsDialog : Window
    {
        public class Result
        {
            public string Title;
            public string Icon;      // null = icône par défaut
            public int ChapterGoal;  // 0 = aucun objectif
            public string Deadline;  // "yyyy-MM-dd", "" = aucune (b48)
            public int SizeGoal;     // 0 = aucun
            public string SizeUnit;  // "words" | "chars"
        }

        private readonly BinderItem _book;
        private readonly TextBox _title;
        private readonly SpinnerField _goal;
        private readonly SpinnerField _size;   // objectif de taille (b48)
        private readonly ComboBox _unit;
        private string _deadlineText;          // tel que saisi (JJ/MM/AAAA)
        private readonly Border _iconHost;
        private string _icon;
        private bool _accepted;

        private BookOptionsDialog(Window owner, BinderItem book)
        {
            _book = book;
            _icon = book.Icon;

            Title = "Options du livre";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 340 };

            panel.Children.Add(Label("Nom du livre :"));
            _title = new TextBox { Text = book.Title ?? "", MinWidth = 300 };
            _title.SelectAll();
            panel.Children.Add(_title);

            panel.Children.Add(Label("Icône :"));
            var iconRow = new StackPanel { Orientation = Orientation.Horizontal };
            _iconHost = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(5),
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                Background = Chrome.PaperBg,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            iconRow.Children.Add(_iconHost);
            var change = new Button
            {
                Content = "Changer…",
                MinWidth = 90,
                VerticalAlignment = VerticalAlignment.Center
            };
            change.Click += delegate
            {
                var chosen = IconPickerDialog.Ask(this);
                if (chosen == null) return; // annulé
                _icon = chosen.Length == 0 ? null : chosen;
                RenderIcon();
            };
            iconRow.Children.Add(change);
            var reset = new Button
            {
                Content = "Par défaut",
                MinWidth = 90,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            reset.Click += delegate { _icon = null; RenderIcon(); };
            iconRow.Children.Add(reset);
            panel.Children.Add(iconRow);
            RenderIcon();

            panel.Children.Add(Label("Objectif — nombre de chapitres (0 = aucun) :"));
            var goal = book.Book != null ? book.Book.ChapterGoal : 0;
            _goal = new SpinnerField(goal, 0, 999, 1,
                "Le livre vise ce nombre de chapitres ; la barre de progression de "
                + "l'inspecteur montre les chapitres présents (orange) et terminés (vert)");
            _goal.HorizontalAlignment = HorizontalAlignment.Left;
            _goal.Margin = new Thickness(0, 2, 0, 0);
            panel.Children.Add(_goal);

            var hint = new TextBlock
            {
                Text = "Un chapitre = un écrit du récit (liminaires et table des "
                    + "matières exclus) ; il compte pour terminé à l'état « Terminé ».",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 340,
                Margin = new Thickness(0, 6, 0, 0)
            };
            panel.Children.Add(hint);

            // b48 : la projection en taille et l'échéance, à côté des chapitres.
            panel.Children.Add(Label("Objectif — taille du livre (0 = aucun) :"));
            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
            _size = new SpinnerField(book.Book != null ? book.Book.SizeGoal : 0, 0, 5000000, 1000,
                "Le livre vise ce nombre de mots ou de caractères (textes du récit, liminaires exclus)");
            _size.BoxWidth = 84; // « 120 000 » doit tenir
            sizeRow.Children.Add(_size);
            _unit = new ComboBox { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 110 };
            _unit.Items.Add("mots");
            _unit.Items.Add("caractères");
            _unit.SelectedIndex = book.Book != null && book.Book.CountsChars ? 1 : 0;
            sizeRow.Children.Add(_unit);
            panel.Children.Add(sizeRow);

            panel.Children.Add(Label("Échéance (JJ/MM/AAAA, vide = aucune) :"));
            _deadlineText = book.Book == null || book.Book.Deadline.Length == 0 ? "" : Dates.Display(book.Book.Deadline);
            var deadline = FieldEditors.Build(FieldKinds.Date, _deadlineText, null, null, null,
                delegate(string text) { _deadlineText = text; }, null);
            deadline.MaxWidth = 340;
            deadline.HorizontalAlignment = HorizontalAlignment.Left;
            deadline.MinWidth = 220;
            panel.Children.Add(deadline);
            panel.Children.Add(new TextBlock
            {
                Text = "Avec une échéance et une taille, la carte « Où j'en suis » et le "
                    + "Général du livre disent ce qu'il reste à écrire par jour.",
                Foreground = Chrome.SoftText,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 340,
                Margin = new Thickness(0, 6, 0, 0)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button
            {
                Content = "Annuler",
                IsCancel = true,
                MinWidth = 80,
                Margin = new Thickness(8, 0, 0, 0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += delegate { _title.Focus(); };
        }

        private void RenderIcon()
        {
            // Aperçu à l'icône choisie : un item jetable porte le jeton, le
            // rendu de la Pile fait le reste (défaut, SVG teinté, image).
            var preview = new BinderItem { Kind = ItemKind.Book, Icon = _icon };
            var icon = ItemIcons.Render(preview, 16, Chrome.Ink) as FrameworkElement;
            if (icon != null)
            {
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
            }
            _iconHost.Child = icon;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 4)
            };
        }

        /// <summary>Le résultat validé, ou null (annulé). Un nom vide garde
        /// l'ancien.</summary>
        public static Result Ask(Window owner, BinderItem book)
        {
            var dialog = new BookOptionsDialog(owner, book);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return null;
            var title = dialog._title.Text.Trim();
            // L'échéance saisie JJ/MM/AAAA devient « yyyy-MM-dd » ; illisible = aucune.
            System.DateTime parsed;
            var typed = (dialog._deadlineText ?? "").Trim();
            var deadline = typed.Length > 0 && System.DateTime.TryParseExact(typed, "dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsed)
                ? parsed.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "";
            return new Result
            {
                Title = title.Length == 0 ? book.Title : title,
                Icon = dialog._icon,
                ChapterGoal = System.Math.Max(0, (int)System.Math.Round(dialog._goal.Value)),
                Deadline = deadline,
                SizeGoal = System.Math.Max(0, (int)System.Math.Round(dialog._size.Value)),
                SizeUnit = dialog._unit.SelectedIndex == 1 ? "chars" : "words"
            };
        }
    }
}

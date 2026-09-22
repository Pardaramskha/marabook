using System;
using System.Windows;
using System.Windows.Controls;
using Marabook.Exchange;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Le dialogue de création d'EPUB (22/09) : le titre (pré-rempli
    /// depuis le livre ou l'écrit, toujours modifiable), les métadonnées
    /// (auteur·ice, sous-titre, éditeur, ISBN, année, langue), la couverture,
    /// les titres de chapitres — et, avant de créer, ce que le plan écarte :
    /// les pages dynamiques (table des matières, index, notes de fin,
    /// glossaire), dont les folios n'ont pas de sens dans un texte qui
    /// recoule. Rend les options, ou null.</summary>
    public class EpubExportDialog : Window
    {
        private readonly TextBox _title, _subtitle, _author, _publisher, _identifier, _year, _language;
        private readonly CheckBox _cover, _headings, _openFolder;
        private bool _accepted;

        /// <summary>Ouvrir le dossier du fichier une fois écrit.</summary>
        public bool OpenFolder { get { return _openFolder.IsChecked == true; } }

        private EpubExportDialog(Window owner, EpubPlan plan, EpubOptions defaults)
        {
            Title = plan.IsBook ? "Créer un EPUB du livre" : "Créer un EPUB de l'écrit";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 420, MaxWidth = 520 };
            panel.Children.Add(new TextBlock
            {
                Text = "Un EPUB 3 « reflowable » : le texte recoule à la taille de la liseuse. Les styles de paragraphe deviennent la feuille CSS ; folios, gabarits et marges n'ont pas cours.",
                Foreground = Chrome.SoftText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            _title = Row(panel, "Titre", defaults.Title);
            _subtitle = Row(panel, "Sous-titre", defaults.Subtitle);
            _author = Row(panel, "Auteur·ice", defaults.Author);
            _publisher = Row(panel, "Éditeur", defaults.Publisher);
            _identifier = Row(panel, "ISBN", defaults.Identifier);
            _identifier.ToolTip = "Vide : un identifiant unique est généré";
            _year = Row(panel, "Année", defaults.Year);
            _language = Row(panel, "Langue", defaults.Language);
            _language.ToolTip = "Code de langue : fr, en, es…";

            _cover = new CheckBox
            {
                Content = plan.HasCover ? "Couverture : l'image du " + (plan.IsBook ? "livre" : "texte") : "Couverture — aucune image posée (onglet Édition du livre)",
                IsChecked = plan.HasCover && defaults.IncludeCover,
                IsEnabled = plan.HasCover,
                Margin = new Thickness(0, 10, 0, 0)
            };
            panel.Children.Add(_cover);
            _headings = new CheckBox
            {
                Content = "Le titre de chaque écrit en tête de son chapitre",
                IsChecked = defaults.ChapterHeadings,
                Margin = new Thickness(0, 6, 0, 0)
            };
            panel.Children.Add(_headings);

            var chapters = plan.Chapters.Count;
            panel.Children.Add(new TextBlock
            {
                Text = chapters + (chapters > 1 ? " écrits" : " écrit") + " dans l'ordre du " + (plan.IsBook ? "livre" : "texte") + ".",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                Margin = new Thickness(0, 12, 0, 0)
            });
            if (plan.Skipped.Count > 0)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var page in plan.Skipped) names.Add(page.Title);
                var skipped = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                var icon = Icons.Make("warning-fill", 14, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 126, 34))) as FrameworkElement;
                if (icon != null)
                {
                    icon.VerticalAlignment = VerticalAlignment.Top;
                    icon.Margin = new Thickness(0, 2, 8, 0);
                    skipped.Children.Add(icon);
                }
                skipped.Children.Add(new TextBlock
                {
                    Text = "Pages dynamiques écartées (leurs folios n'ont pas de sens ici) : " + string.Join(", ", names.ToArray()) + ".",
                    Foreground = Chrome.SoftText,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460
                });
                panel.Children.Add(skipped);
            }

            _openFolder = new CheckBox
            {
                Content = "Ouvrir le dossier une fois l'EPUB créé",
                IsChecked = true,
                Margin = new Thickness(0, 12, 0, 0)
            };
            panel.Children.Add(_openFolder);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = new Button { Content = "Créer l'EPUB…", IsDefault = true, MinWidth = 120 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
        }

        private static TextBox Row(Panel panel, string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var caption = new TextBlock
            {
                Text = label,
                Width = 90,
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(caption, Dock.Left);
            row.Children.Add(caption);
            var box = new TextBox { Text = value ?? "" };
            row.Children.Add(box);
            panel.Children.Add(row);
            return box;
        }

        /// <summary>Les options choisies, ou null si annulé.</summary>
        public static EpubOptions Ask(Window owner, EpubPlan plan, EpubOptions defaults, out bool openFolder)
        {
            var dialog = new EpubExportDialog(owner, plan, defaults);
            Dialogs.ShowModal(dialog);
            openFolder = dialog.OpenFolder;
            if (!dialog._accepted) return null;
            return new EpubOptions
            {
                Title = dialog._title.Text.Trim(),
                Subtitle = dialog._subtitle.Text.Trim(),
                Author = dialog._author.Text.Trim(),
                Publisher = dialog._publisher.Text.Trim(),
                Identifier = dialog._identifier.Text.Trim(),
                Year = dialog._year.Text.Trim(),
                Language = dialog._language.Text.Trim().Length == 0 ? "fr" : dialog._language.Text.Trim(),
                IncludeCover = dialog._cover.IsChecked == true,
                ChapterHeadings = dialog._headings.IsChecked == true
            };
        }
    }
}

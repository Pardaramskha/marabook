using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.App
{
    /// <summary>« NOUVEAU PROJET » QUAND IL Y A DES MODÈLES (2.0.0, 21/11/2026) :
    /// un projet vide, ou l'un des modèles enregistrés depuis Préférences ›
    /// Projet. Sans modèle, cette fenêtre ne se montre jamais : « Nouveau
    /// projet » reste le geste d'avant.</summary>
    public class ProjectTemplateDialog : Window
    {
        private readonly ListBox _list;
        private readonly List<ProjectTemplates.Entry> _entries;
        private bool _accepted;

        private ProjectTemplateDialog(Window owner, List<ProjectTemplates.Entry> entries)
        {
            _entries = entries;
            TimelineUi.Dress(this, owner, "Nouveau projet");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 400 };
            panel.Children.Add(new TextBlock { Text = "Partir de :", Foreground = Chrome.Ink, Margin = new Thickness(0, 0, 0, 6) });
            _list = new ListBox { MaxHeight = 280, Tag = "new-project-templates" };
            _list.Items.Add(Line("Projet vide", "Les rubriques de Marabook, un écrit, les styles par défaut"));
            foreach (var entry in entries) _list.Items.Add(Line(entry.Name, "Modèle — enregistré le " + Dates.Display(entry.SavedAt)));
            _list.SelectedIndex = 0;
            _list.DoubleTapped += delegate { _accepted = true; Close(); };
            panel.Children.Add(_list);
            panel.Children.Add(TimelineUi.Hint("Les modèles se créent et se suppriment dans Préférences › Projet."));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(cancel);
            var ok = new Button { Content = "Créer", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { _accepted = true; Close(); };
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
        }

        private static Control Line(string title, string detail)
        {
            var line = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
            line.Children.Add(new TextBlock { Text = title, Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold });
            line.Children.Add(new TextBlock { Text = detail, Foreground = Chrome.SoftText, FontSize = 11.5 });
            return line;
        }

        /// <summary>Le projet neuf à ouvrir : vide s'il n'y a aucun modèle (sans
        /// rien demander) ou si « Projet vide » est choisi ; bâti sur le modèle
        /// choisi ; null si l'on renonce.</summary>
        public static async Task<Project> Ask(Window owner)
        {
            var entries = ProjectTemplates.All();
            if (entries.Count == 0) return Project.CreateNew();
            var dialog = new ProjectTemplateDialog(owner, entries);
            await Dialogs.ShowModal(dialog, owner);
            if (!dialog._accepted) return null;
            var index = dialog._list.SelectedIndex;
            if (index <= 0 || index > entries.Count) return Project.CreateNew();
            try { return ProjectTemplates.Instantiate(entries[index - 1].Path); }
            catch (Exception error)
            {
                if (!(error is System.IO.IOException) && !(error is System.IO.InvalidDataException) && !(error is UnauthorizedAccessException)) throw;
                await MessageDialog.Show(owner, "Ce modèle n'a pas pu être lu (" + error.Message + ") : le projet part vide.", "Nouveau projet", MessageButtons.OK, MessageIcon.Warning);
                return Project.CreateNew();
            }
        }
    }
}

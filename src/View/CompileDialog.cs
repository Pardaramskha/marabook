using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using UniversSale.Exchange;
using UniversSale.Model;

namespace UniversSale.View
{
    public class CompileRequest
    {
        public BinderItem Root;
        public CompileOptions Options;
    }

    /// <summary>Compilation setup: scope (Écrits or one of its folders), title
    /// page, chapter headings/numbering, page breaks or separator.</summary>
    public class CompileDialog : Window
    {
        private readonly Project _project;
        private readonly ComboBox _scopeCombo;
        private readonly CheckBox _titlePageCheck, _headingsCheck, _numberCheck, _pageBreakCheck;
        private readonly TextBox _authorBox;
        private readonly ComboBox _separatorCombo;
        private bool _accepted;

        private CompileDialog(Window owner, Project project)
        {
            _project = project;
            Title = "Compiler le manuscrit";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 360 };

            panel.Children.Add(Label("Contenu à compiler :"));
            _scopeCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            var writings = project.Category(Project.KeyWritings);
            _scopeCombo.Items.Add(new ComboBoxItem { Content = "Écrits (tout)", Tag = writings.Id });
            AddFolders(writings, 1);
            _scopeCombo.SelectedIndex = 0;
            panel.Children.Add(_scopeCombo);

            _titlePageCheck = Check("Page de titre", true);
            panel.Children.Add(_titlePageCheck);

            var authorRow = new DockPanel { Margin = new Thickness(22, 2, 0, 8) };
            var authorLabel = new TextBlock
            {
                Text = "Auteur :",
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(authorLabel, Dock.Left);
            authorRow.Children.Add(authorLabel);
            _authorBox = new TextBox { Text = project.Author ?? "" };
            authorRow.Children.Add(_authorBox);
            panel.Children.Add(authorRow);

            _headingsCheck = Check("Titres des écrits en en-têtes de chapitres", true);
            panel.Children.Add(_headingsCheck);
            _numberCheck = Check("Numéroter les chapitres", false);
            _numberCheck.Margin = new Thickness(22, 2, 0, 2);
            panel.Children.Add(_numberCheck);

            _pageBreakCheck = Check("Chaque écrit commence sur une nouvelle page", true);
            panel.Children.Add(_pageBreakCheck);

            var separatorRow = new DockPanel { Margin = new Thickness(22, 2, 0, 0) };
            var separatorLabel = new TextBlock
            {
                Text = "Sinon, séparateur :",
                Foreground = Chrome.SoftText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            DockPanel.SetDock(separatorLabel, Dock.Left);
            separatorRow.Children.Add(separatorLabel);
            _separatorCombo = new ComboBox();
            _separatorCombo.Items.Add("***");
            _separatorCombo.Items.Add("· · ·");
            _separatorCombo.Items.Add("(ligne vide)");
            _separatorCombo.SelectedIndex = 0;
            separatorRow.Children.Add(_separatorCombo);
            panel.Children.Add(separatorRow);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = new Button { Content = "Compiler…", IsDefault = true, MinWidth = 100 };
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            Content = panel;
        }

        private void AddFolders(BinderItem container, int depth)
        {
            foreach (var child in container.Children)
            {
                if (!child.CanHaveChildren) continue;
                _scopeCombo.Items.Add(new ComboBoxItem
                {
                    Content = new string(' ', depth * 3) + child.Title,
                    Tag = child.Id
                });
                AddFolders(child, depth + 1);
            }
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock { Foreground = Chrome.SoftText, Text = text, Margin = new Thickness(0, 0, 0, 4) };
        }

        private static CheckBox Check(string label, bool value)
        {
            return new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 2, 0, 2) };
        }

        public static CompileRequest Show(Window owner, Project project)
        {
            var dialog = new CompileDialog(owner, project);
            Dialogs.ShowModal(dialog);
            if (!dialog._accepted) return null;

            var chosen = dialog._scopeCombo.SelectedItem as ComboBoxItem;
            var root = chosen == null ? null : project.FindById((string)chosen.Tag);
            if (root == null) return null;

            var separator = dialog._separatorCombo.SelectedIndex == 0 ? "***"
                          : dialog._separatorCombo.SelectedIndex == 1 ? "· · ·" : "";
            project.Author = dialog._authorBox.Text.Trim();
            return new CompileRequest
            {
                Root = root,
                Options = new CompileOptions
                {
                    TitlePage = dialog._titlePageCheck.IsChecked == true,
                    Author = project.Author,
                    ChapterHeadings = dialog._headingsCheck.IsChecked == true,
                    NumberChapters = dialog._numberCheck.IsChecked == true,
                    PageBreakPerText = dialog._pageBreakCheck.IsChecked == true,
                    Separator = separator
                }
            };
        }
    }
}

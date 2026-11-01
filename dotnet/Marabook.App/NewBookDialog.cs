using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>Le dialogue « Nouveau livre » (1.1.0, 29/10, Rémi) : le titre,
    /// le format des pages (A5 par défaut) et les marges miroir (oui par
    /// défaut : petit fond côté reliure, grand fond à l'extérieur — le
    /// compositeur les alterne recto/verso). Pour un profil sans « Gabarits
    /// et format » (Écrivain·e), le titre seul : le livre naît en A4, marges
    /// uniformes de 2,5 cm.</summary>
    public sealed class NewBookDialog : Window
    {
        public sealed class Result
        {
            public string Title;
            public PageSetup Template;
        }

        private readonly TextBox _title;
        private readonly ComboBox _format;
        private readonly CheckBox _mirror;
        private bool _accepted;

        private NewBookDialog(bool withFormat)
        {
            Title = "Nouveau livre";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;

            var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 340 };
            panel.Children.Add(new TextBlock { Text = "Titre du livre :", Foreground = Chrome.Ink, Margin = new Thickness(0, 0, 0, 6) });
            _title = new TextBox { Text = "Nouveau livre", MinWidth = 300 };
            _title.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Return || e.Key == Key.Enter) { _accepted = true; Close(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };
            panel.Children.Add(_title);

            if (withFormat)
            {
                panel.Children.Add(new TextBlock { Text = "Format des pages :", Foreground = Chrome.Ink, Margin = new Thickness(0, 14, 0, 6) });
                _format = new ComboBox { MinWidth = 300, Tag = "new-book-format" };
                var defaultIndex = 0;
                foreach (var preset in BookFormatPanel.Presets)
                {
                    if ((double)preset[1] <= 0) continue; // « Personnalisé… » : après, dans Gabarits & Format
                    _format.Items.Add((string)preset[0]);
                    if (((string)preset[0]).StartsWith("A5")) defaultIndex = _format.Items.Count - 1;
                }
                _format.SelectedIndex = defaultIndex;
                panel.Children.Add(_format);
                _mirror = new CheckBox
                {
                    Content = "Marges miroir (petit fond côté reliure, grand fond à l'extérieur)",
                    IsChecked = true,
                    Margin = new Thickness(0, 10, 0, 0),
                    Tag = "new-book-mirror"
                };
                ToolTip.SetTip(_mirror, "Coché : marges 20 / 20 / 30 / 20 mm (tête, pied, petit fond, grand fond), alternées recto-verso. Décoché : 25 mm partout.");
                panel.Children.Add(_mirror);
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = Buttons.Text("Créer", null, Buttons.Bar, Buttons.Look.Primary);
            ok.MinWidth = 80;
            ok.IsDefault = true;
            ok.Click += delegate { _accepted = true; Close(); };
            var cancel = Buttons.Text("Annuler", null, Buttons.Bar, Buttons.Look.Outline);
            cancel.MinWidth = 80;
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            cancel.Click += delegate { Close(); };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            Opened += delegate { _title.Focus(); _title.SelectAll(); };
        }

        /// <summary>Le gabarit choisi — sans dialogue de format : A4, marges uniformes.</summary>
        private PageSetup BuildTemplate()
        {
            if (_format == null) return WriterTemplate();
            var name = _format.SelectedItem as string;
            double width = 148, height = 210;
            foreach (var preset in BookFormatPanel.Presets)
                if ((string)preset[0] == name) { width = (double)preset[1]; height = (double)preset[2]; }
            return Template(width, height, _mirror.IsChecked == true);
        }

        /// <summary>A4, marges uniformes de 25 mm : le livre d'un profil sans gabarits.</summary>
        public static PageSetup WriterTemplate()
        {
            return Template(210, 297, false);
        }

        public static PageSetup Template(double widthMm, double heightMm, bool mirror)
        {
            return mirror
                ? new PageSetup { PageWidthMm = widthMm, PageHeightMm = heightMm, MarginTopMm = 20, MarginBottomMm = 20, MarginLeftMm = 30, MarginRightMm = 20 }
                : new PageSetup { PageWidthMm = widthMm, PageHeightMm = heightMm, MarginTopMm = 25, MarginBottomMm = 25, MarginLeftMm = 25, MarginRightMm = 25 };
        }

        /// <summary>Titre et gabarit, ou null si annulé (ou titre vide).</summary>
        public static async Task<Result> Ask(Window owner, bool withFormat)
        {
            var dialog = new NewBookDialog(withFormat);
            Dialogs.FitToScreen(dialog, owner);
            if (owner != null) await dialog.ShowDialog(owner);
            else
            {
                var done = new TaskCompletionSource<bool>();
                dialog.Closed += delegate { done.TrySetResult(true); };
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Show();
                await done.Task;
            }
            if (!dialog._accepted) return null;
            var title = (dialog._title.Text ?? "").Trim();
            if (title.Length == 0) return null;
            return new Result { Title = title, Template = dialog.BuildTemplate() };
        }

        // — Sonde (29/10) : le dialogue bâti sans être montré.
        internal static NewBookDialog BuildForProbe(bool withFormat) { return new NewBookDialog(withFormat); }
        internal bool HasFormatForProbe { get { return _format != null; } }
        internal string FormatForProbe { get { return _format == null ? null : _format.SelectedItem as string; } }
        internal bool MirrorForProbe { get { return _mirror != null && _mirror.IsChecked == true; } }
        internal PageSetup TemplateForProbe { get { return BuildTemplate(); } }
    }
}

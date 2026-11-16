using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>L'import d'une carte (16/11/2026) : la fenêtre qui patiente pendant que
    /// l'image est décodée, découpée en tuiles et réencodée — hors du fil de l'interface.</summary>
    internal sealed class MapImportDialog : Window
    {
        private readonly ProgressBar _bar;
        private readonly TextBlock _status;
        private MapInfo _result;
        private string _error;

        private MapImportDialog(Window owner, string path)
        {
            TimelineUi.Dress(this, owner, "Import de la carte");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 18), Width = 420 };
            panel.Children.Add(new TextBlock { Text = Path.GetFileName(path), Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            _status = new TextBlock { Text = "Lecture de l'image…", Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_status);
            _bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 8 };
            panel.Children.Add(_bar);
            panel.Children.Add(TimelineUi.Hint("L'image est découpée en tuiles, un niveau par doublement du zoom : la carte restera fluide et nette jusqu'au plus près, quelle que soit sa taille."));
            Content = panel;
            Opened += async delegate
            {
                try
                {
                    var bytes = await Task.Run(delegate { return File.ReadAllBytes(path); });
                    _status.Text = "Découpage en tuiles et optimisation…";
                    var last = 0.0;
                    _result = await Task.Run(delegate
                    {
                        return MapImport.Slice(bytes, Path.GetFileName(path), delegate(double done)
                        {
                            if (done - last < 0.02 && done < 1) return;
                            last = done;
                            Dispatcher.UIThread.Post(delegate { _bar.Value = done; });
                        });
                    });
                }
                catch (Exception error) { _error = error is InvalidDataException || error is IOException || error is UnauthorizedAccessException ? error.Message : "L'import a échoué : " + error.Message; }
                Close();
            };
        }

        /// <summary>La carte découpée, ou null (et l'erreur est dite).</summary>
        public static async Task<MapInfo> Run(Window owner, string path)
        {
            var dialog = new MapImportDialog(owner, path);
            await Dialogs.ShowModal(dialog, owner);
            if (dialog._error != null)
                await MessageDialog.Show(owner, dialog._error, "Import de la carte", MessageButtons.OK, MessageIcon.Warning);
            return dialog._result;
        }
    }

    internal sealed class MapShapeChoice
    {
        public string Name, Color, LinkId;
        public int Level;
    }

    /// <summary>Une zone ou un repère : son nom, sa couleur, la fiche liée, et à quel zoom il paraît.</summary>
    internal sealed class MapShapeDialog : Window
    {
        public static readonly string[] Palette = { "#5B67D8", "#C0504D", "#3E9C7A", "#B5843A", "#8E6BBF", "#0E7C7B", "#D9603A", "#6B7280" };

        private readonly Project _project;
        private readonly TextBox _name;
        private readonly StackPanel _swatches;
        private readonly TextBlock _linkLabel;
        private readonly ComboBox _level;
        private string _color, _linkId;
        private readonly string _defaultName;
        private MapShapeChoice _result;

        private MapShapeDialog(Window owner, Project project, string title, MapShapeChoice current, string defaultName)
        {
            _project = project;
            _color = current.Color;
            _linkId = current.LinkId;
            _defaultName = defaultName;
            TimelineUi.Dress(this, owner, title);
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 400 };
            panel.Children.Add(TimelineUi.Label("Nom :", 0));
            _name = new TextBox { Text = current.Name ?? "", Tag = "map-shape-name" };
            panel.Children.Add(_name);

            panel.Children.Add(TimelineUi.Label("Couleur :"));
            _swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            panel.Children.Add(_swatches);
            FillSwatches();

            panel.Children.Add(TimelineUi.Label("Fiche liée :"));
            _linkLabel = new TextBlock { Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var pick = Buttons.Text("Choisir…", "Lier à une fiche du projet : un pays, une ville, un lieu…", Buttons.Compact, Buttons.Look.Outline);
            pick.Click += async delegate
            {
                var sheet = await ItemPickerDialog.Ask(this, _project, "Lier à une fiche", new[] { "fiche" });
                if (sheet == null) return;
                _linkId = sheet.Id;
                // Une zone encore sans nom prend celui de sa fiche.
                if (string.IsNullOrWhiteSpace(_name.Text) || _name.Text.Trim() == _defaultName) _name.Text = sheet.Title;
                DescribeLink();
            };
            var none = Buttons.Text("Aucune", null, Buttons.Compact, Buttons.Look.Calm);
            none.Click += delegate { _linkId = null; DescribeLink(); };
            var linkButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            linkButtons.Children.Add(pick);
            linkButtons.Children.Add(none);
            var linkRow = new DockPanel();
            DockPanel.SetDock(linkButtons, Dock.Right);
            linkRow.Children.Add(linkButtons);
            linkRow.Children.Add(_linkLabel);
            panel.Children.Add(linkRow);
            panel.Children.Add(TimelineUi.Hint("Liée, la zone ouvre sa fiche d'un double-clic : la carte devient l'entrée de l'encyclopédie."));

            panel.Children.Add(TimelineUi.Label("Visible :"));
            _level = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Maps.LevelNames, SelectedIndex = Math.Max(0, Math.Min(3, current.Level)) };
            panel.Children.Add(_level);
            panel.Children.Add(TimelineUi.Hint("Les grandes régions de loin, les pays ensuite, les villes de près : la carte ne montre à chaque zoom que ce qui s'y lit."));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { Accept(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            DescribeLink();
            Loaded += delegate { _name.Focus(); _name.SelectAll(); };
        }

        private void FillSwatches()
        {
            _swatches.Children.Clear();
            var colors = new List<string>(Palette);
            if (!colors.Exists(delegate(string c) { return string.Equals(c, _color, StringComparison.OrdinalIgnoreCase); })) colors.Add(_color);
            foreach (var value in colors)
            {
                var color = value;
                Color parsed;
                if (!Color.TryParse(color, out parsed)) continue;
                var chosen = string.Equals(color, _color, StringComparison.OrdinalIgnoreCase);
                var swatch = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(parsed),
                    BorderBrush = chosen ? Chrome.Ink : Chrome.Border, BorderThickness = new Thickness(chosen ? 2.5 : 1), Cursor = new Cursor(StandardCursorType.Hand)
                };
                swatch.PointerPressed += delegate { _color = color; FillSwatches(); };
                _swatches.Children.Add(swatch);
            }
            var other = Buttons.Text("Autre…", "Une autre couleur", Buttons.Compact, Buttons.Look.Calm);
            other.Click += async delegate
            {
                var picked = await ColorDialog.Ask(this);
                if (string.IsNullOrEmpty(picked)) return;
                _color = picked;
                FillSwatches();
            };
            _swatches.Children.Add(other);
        }

        private void DescribeLink()
        {
            var sheet = _linkId == null ? null : _project.FindById(_linkId);
            _linkLabel.Text = sheet == null ? "Aucune" : sheet.Title;
            _linkLabel.Foreground = sheet == null ? Chrome.SoftText : Chrome.Ink;
            if (sheet == null) _linkId = null;
        }

        private void Accept()
        {
            var name = (_name.Text ?? "").Trim();
            if (name.Length == 0) name = _defaultName;
            _result = new MapShapeChoice { Name = name, Color = _color, LinkId = _linkId, Level = Math.Max(0, _level.SelectedIndex) };
            Close();
        }

        public static async Task<MapShapeChoice> Ask(Window owner, Project project, string title, MapShapeChoice current, string defaultName)
        {
            var dialog = new MapShapeDialog(owner, project, title, current, defaultName);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }
    }

    internal sealed class MapScaleChoice
    {
        public double Km;
        public string Note;
    }

    /// <summary>L'ÉCHELLE (16/11, Rémi : « en donnant une distance ou un temps de trajet entre
    /// deux points ») : ce qui sépare les deux points posés sur la carte — des kilomètres, ou
    /// des jours de voyage à une allure donnée. Tout le reste s'en déduit.</summary>
    internal sealed class MapScaleDialog : Window
    {
        private readonly RadioButton _byDistance, _byTravel;
        private readonly TextBox _km, _days;
        private readonly ComboBox _mode;
        private readonly TextBlock _echo;
        private MapScaleChoice _result;

        private MapScaleDialog(Window owner, string between, double currentKm)
        {
            TimelineUi.Dress(this, owner, "Échelle de la carte");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 420 };
            panel.Children.Add(new TextBlock { Text = between, Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            _byDistance = new RadioButton { Content = "Une distance :", GroupName = "map-scale", IsChecked = true };
            _km = new TextBox { Width = 120, Text = currentKm > 0 ? currentKm.ToString("0.##", fr) : "", Tag = "map-scale-km" };
            var kmRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 6, 0, 0) };
            kmRow.Children.Add(_km);
            kmRow.Children.Add(new TextBlock { Text = "km", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            _byTravel = new RadioButton { Content = "Un temps de trajet :", GroupName = "map-scale", Margin = new Thickness(0, 10, 0, 0) };
            _days = new TextBox { Width = 80, Tag = "map-scale-days" };
            var names = new List<string>();
            foreach (var mode in Maps.TravelModes) names.Add(mode.Name + " (" + mode.KmPerDay.ToString("0", fr) + " km par jour)");
            _mode = new ComboBox { ItemsSource = names, SelectedIndex = 0, MinWidth = 220 };
            var travelRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 6, 0, 0) };
            travelRow.Children.Add(_days);
            travelRow.Children.Add(new TextBlock { Text = "jours", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) });
            travelRow.Children.Add(_mode);
            _echo = new TextBlock { FontSize = 12, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(_byDistance);
            panel.Children.Add(kmRow);
            panel.Children.Add(_byTravel);
            panel.Children.Add(travelRow);
            panel.Children.Add(_echo);
            panel.Children.Add(TimelineUi.Hint("Les allures sont des ordres de grandeur d'un long voyage, haltes comprises. Une distance connue reste plus sûre."));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Poser l'échelle", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate
            {
                string note;
                var km = Read(out note);
                if (km <= 0) return;
                _result = new MapScaleChoice { Km = km, Note = note };
                Close();
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
            _km.TextChanged += delegate { Describe(); };
            _days.TextChanged += delegate { Describe(); };
            _mode.SelectionChanged += delegate { Describe(); };
            _byDistance.IsCheckedChanged += delegate { Describe(); };
            _byTravel.IsCheckedChanged += delegate { Describe(); };
            _km.GotFocus += delegate { _byDistance.IsChecked = true; };
            _days.GotFocus += delegate { _byTravel.IsChecked = true; };
            Describe();
            Loaded += delegate { _km.Focus(); _km.SelectAll(); };
        }

        private static double Number(string text)
        {
            double value;
            text = (text ?? "").Trim().Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0 && !double.IsInfinity(value) ? value : 0;
        }

        private double Read(out string note)
        {
            note = null;
            if (_byTravel.IsChecked == true)
            {
                var days = Number(_days.Text);
                var mode = Maps.TravelModes[Math.Max(0, _mode.SelectedIndex)];
                if (days <= 0) return 0;
                note = days.ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")) + (days < 2 ? " jour " : " jours ") + mode.Name;
                return days * mode.KmPerDay;
            }
            return Number(_km.Text);
        }

        private void Describe()
        {
            string note;
            var km = Read(out note);
            if (km <= 0)
            {
                _echo.Foreground = Chrome.SoftText;
                _echo.Text = _byTravel.IsChecked == true ? "Combien de jours pour aller d'un point à l'autre ?" : "Combien de kilomètres séparent les deux points ?";
                return;
            }
            _echo.Foreground = Chrome.Ink;
            _echo.Text = note != null ? note + " : environ " + Maps.KmLabel(km) + " entre les deux points." : Maps.KmLabel(km) + " entre les deux points — " + Maps.TravelLabel(km) + ".";
        }

        internal void SetForProbe(double km) { _km.Text = km.ToString(CultureInfo.InvariantCulture); }

        public static async Task<MapScaleChoice> Ask(Window owner, string between, double currentKm)
        {
            var dialog = new MapScaleDialog(owner, between, currentKm);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }
    }
}

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
    /// l'image est décodée, découpée en tuiles et réencodée — hors du fil de l'interface.
    ///
    /// Lot 12 (Rémi) : pour une NOUVELLE carte, la fenêtre propose aussi, en option, le monde
    /// de la carte et « Carte du monde entier » — on les règle pendant que l'image se découpe,
    /// puis « Créer la carte ». Pour un remplacement d'image, elle se referme seule.</summary>
    internal sealed class MapImportDialog : Window
    {
        private readonly ProgressBar _bar;
        private readonly TextBlock _status;
        private readonly ComboBox _world;
        private readonly CheckBox _whole;
        private readonly Button _create;
        private readonly List<World> _worlds = new List<World>();
        private MapInfo _result;
        private string _error;
        private bool _accepted;

        private MapImportDialog(Window owner, string path, Project project, bool options)
        {
            TimelineUi.Dress(this, owner, "Import de la carte");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 18), Width = 440 };
            panel.Children.Add(new TextBlock { Text = Path.GetFileName(path), Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            _status = new TextBlock { Text = "Lecture de l'image…", Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 6, 0, 8), TextWrapping = TextWrapping.Wrap, Tag = "map-import-status" };
            panel.Children.Add(_status);
            _bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 8 };
            panel.Children.Add(_bar);
            panel.Children.Add(TimelineUi.Hint("L'image est découpée en tuiles, un niveau par doublement du zoom : la carte restera fluide et nette jusqu'au plus près, quelle que soit sa taille."));
            if (options)
            {
                panel.Children.Add(TimelineUi.Label("Monde (facultatif) :"));
                var names = new List<string> { "Aucun (carte autonome)" };
                if (project != null) foreach (var world in project.Worlds) { _worlds.Add(world); names.Add(world.Name); }
                _world = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = names, SelectedIndex = 0, Tag = "map-import-world" };
                panel.Children.Add(_world);
                _whole = new CheckBox { Content = "Carte du monde entier", Margin = new Thickness(0, 8, 0, 0), Tag = "map-import-whole" };
                ToolTip.SetTip(_whole, "L'image est le planisphère de tout le monde : les distances suivent la courbure de la sphère, et le rendu Globe devient possible.");
                panel.Children.Add(_whole);
                panel.Children.Add(TimelineUi.Hint(_worlds.Count == 0
                    ? "Aucun monde dans le projet (ils se créent dans l'Astrolabe). Tout ceci se règle aussi plus tard, dans l'onglet Général de la carte."
                    : "Lié à un monde : jour et nuit, saisons, part de sa surface. Tout ceci se règle aussi plus tard, dans l'onglet Général de la carte."));
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
                var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
                cancel.Click += delegate { _result = null; _accepted = false; Close(); };
                _create = new Button { Content = "Créer la carte", IsDefault = true, MinWidth = 110, IsEnabled = false, Tag = "map-import-create" };
                _create.Click += delegate { Accept(); };
                buttons.Children.Add(cancel);
                buttons.Children.Add(_create);
                Dialogs.Arrange(buttons, _create);
                panel.Children.Add(buttons);
            }
            Content = panel;
            Opened += async delegate
            {
                try
                {
                    var bytes = await Task.Run(delegate { return File.ReadAllBytes(path); });
                    _status.Text = "Découpage en tuiles et optimisation…";
                    var last = 0.0;
                    var sliced = await Task.Run(delegate
                    {
                        return MapImport.Slice(bytes, Path.GetFileName(path), delegate(double done)
                        {
                            if (done - last < 0.02 && done < 1) return;
                            last = done;
                            Dispatcher.UIThread.Post(delegate { _bar.Value = done; });
                        });
                    });
                    Ready(sliced);
                    if (options) return; // on attend « Créer la carte »
                    _accepted = true;
                }
                catch (Exception error) { _error = error is InvalidDataException || error is IOException || error is UnauthorizedAccessException ? error.Message : "L'import a échoué : " + error.Message; }
                Close();
            };
        }

        /// <summary>L'image est découpée : on dit ce qu'elle pèse, et « Créer la carte » s'ouvre.</summary>
        private void Ready(MapInfo sliced)
        {
            _result = sliced;
            _bar.Value = 1;
            _status.Text = sliced.Width + " × " + sliced.Height + " px · " + TimeSystem.Count(sliced.Levels, "niveau de zoom", "niveaux de zoom") + " · " + Maps.SizeLabel(Maps.Bytes(sliced)) + " dans le .plot."
                + (Math.Abs((double)sliced.Width / Math.Max(1, sliced.Height) - 2) < 0.03 ? " Deux fois plus large que haute : sans doute un planisphère entier." : "");
            if (_create != null) { _create.IsEnabled = true; _create.Focus(); }
        }

        private void Accept()
        {
            if (_result == null) return;
            var index = _world == null ? 0 : _world.SelectedIndex;
            _result.WorldId = index <= 0 || index > _worlds.Count ? null : _worlds[index - 1].Id;
            _result.WholeWorld = _whole != null && _whole.IsChecked == true;
            _accepted = true;
            Close();
        }

        internal void ReadyForProbe(MapInfo sliced) { Ready(sliced); }
        internal MapInfo AcceptForProbe(int worldIndex, bool whole)
        {
            if (_world != null) _world.SelectedIndex = worldIndex;
            if (_whole != null) _whole.IsChecked = whole;
            if (_result == null) return null;
            var index = _world == null ? 0 : _world.SelectedIndex;
            _result.WorldId = index <= 0 || index > _worlds.Count ? null : _worlds[index - 1].Id;
            _result.WholeWorld = _whole != null && _whole.IsChecked == true;
            return _result;
        }
        internal static MapImportDialog ForProbe(Window owner, Project project) { return new MapImportDialog(owner, "sonde.jpg", project, true); }

        /// <summary>La carte découpée, ou null (et l'erreur est dite). options : la nouvelle carte choisit
        /// aussi son monde et « Carte du monde entier ».</summary>
        public static async Task<MapInfo> Run(Window owner, string path, Project project = null, bool options = false)
        {
            var dialog = new MapImportDialog(owner, path, project, options);
            await Dialogs.ShowModal(dialog, owner);
            if (dialog._error != null)
                await MessageDialog.Show(owner, dialog._error, "Import de la carte", MessageButtons.OK, MessageIcon.Warning);
            return dialog._accepted ? dialog._result : null;
        }
    }

    internal sealed class MapLayoutChoice
    {
        public bool Fitted;
        public double PoleNorth, PoleSouth = 1;
    }

    /// <summary>LA CONSTRUCTION DU PLANISPHÈRE (lot 12, 16/11, Rémi : « deux options de construction,
    /// dont une qui permet de redéfinir les proportions et les pôles ») : comment une image plate
    /// s'étale sur le monde.
    ///
    /// — ÉTIRÉE : l'image couvre tout, 360° de large, d'un pôle à l'autre. Juste pour une image deux
    ///   fois plus large que haute ; une autre est déformée.
    /// — PROPORTIONS GARDÉES : un degré vaut autant de pixels en large qu'en haut, et l'on dit où
    ///   sont les pôles, en pourcentage de la hauteur de l'image (0 % = bord haut, 100 % = bord bas ;
    ///   moins de 0 ou plus de 100 : le pôle est hors de l'image).</summary>
    internal sealed class MapLayoutDialog : Window
    {
        private readonly MapInfo _map;
        private readonly RadioButton _stretch, _fitted;
        private readonly TextBox _north, _south;
        private readonly TextBlock _echo;
        private readonly StackPanel _poles;
        private readonly Button _ok;
        private MapLayoutChoice _result;

        private MapLayoutDialog(Window owner, MapInfo map)
        {
            _map = map;
            TimelineUi.Dress(this, owner, "Construction du planisphère");
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14), Width = 460 };
            panel.Children.Add(new TextBlock
            {
                Text = "L'image fait " + map.Width.ToString("N0", fr) + " × " + map.Height.ToString("N0", fr) + " px (" + ((double)map.Width / Math.Max(1, map.Height)).ToString("0.00", fr)
                    + " fois plus large que haute). Comment s'étale-t-elle sur le monde ?",
                Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });
            _stretch = new RadioButton { Content = "Étirée : elle couvre tout le monde", GroupName = "map-layout", IsChecked = !Maps.Fitted(map), Tag = "map-layout-stretch" };
            panel.Children.Add(_stretch);
            panel.Children.Add(TimelineUi.Hint("360° de large, d'un pôle à l'autre, quelles que soient ses proportions. Exact pour une image deux fois plus large que haute."));
            _fitted = new RadioButton { Content = "Proportions gardées : je place les pôles", GroupName = "map-layout", IsChecked = Maps.Fitted(map), Margin = new Thickness(0, 10, 0, 0), Tag = "map-layout-fitted" };
            panel.Children.Add(_fitted);
            panel.Children.Add(TimelineUi.Hint("Rien n'est déformé : un degré vaut autant en large qu'en haut. La place des pôles dit ce que l'image couvre ; le reste du globe est de la mer nue."));
            _poles = new StackPanel { Margin = new Thickness(26, 4, 0, 0) };
            _north = new TextBox { Width = 80, Text = (Maps.Fitted(map) ? map.PoleNorth * 100 : 0).ToString("0.##", fr), Tag = "map-pole-north" };
            _south = new TextBox { Width = 80, Text = (Maps.Fitted(map) ? map.PoleSouth * 100 : 100).ToString("0.##", fr), Tag = "map-pole-south" };
            _poles.Children.Add(PoleRow("Pôle nord à", _north));
            _poles.Children.Add(PoleRow("Pôle sud à", _south));
            var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            var edges = Buttons.Text("Pôles aux bords", "L'image va exactement d'un pôle à l'autre : 0 % et 100 %", Buttons.Compact, Buttons.Look.Outline);
            edges.Click += delegate { _north.Text = "0"; _south.Text = "100"; };
            var around = Buttons.Text("Un tour complet", "Place les pôles pour que la largeur de l'image fasse exactement 360°, l'équateur au milieu", Buttons.Compact, Buttons.Look.Outline);
            around.Tag = "map-layout-around";
            around.Click += delegate
            {
                // 360° en large = Width px ; 180° en haut = Width / 2 px, centrés sur le milieu de l'image.
                var half = _map.Width / 4.0 / Math.Max(1, _map.Height);
                _north.Text = ((0.5 - half) * 100).ToString("0.##", fr);
                _south.Text = ((0.5 + half) * 100).ToString("0.##", fr);
            };
            presets.Children.Add(edges);
            presets.Children.Add(around);
            _poles.Children.Add(presets);
            panel.Children.Add(_poles);
            _echo = new TextBlock { FontSize = 12, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = "map-layout-echo" };
            panel.Children.Add(_echo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            _ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            _ok.Click += delegate
            {
                var choice = Read();
                if (choice == null) return;
                _result = choice;
                Close();
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(_ok);
            Dialogs.Arrange(buttons, _ok);
            panel.Children.Add(buttons);
            Content = panel;
            _north.TextChanged += delegate { Describe(); };
            _south.TextChanged += delegate { Describe(); };
            _stretch.IsCheckedChanged += delegate { Describe(); };
            _fitted.IsCheckedChanged += delegate { Describe(); };
            _north.GotFocus += delegate { _fitted.IsChecked = true; };
            _south.GotFocus += delegate { _fitted.IsChecked = true; };
            Describe();
        }

        private static Control PoleRow(string label, TextBox box)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new TextBlock { Text = label, Foreground = Chrome.Ink, Width = 92, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = "% de la hauteur de l'image", Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            return row;
        }

        private static bool Percent(string text, out double value)
        {
            text = (text ?? "").Trim().Replace("%", "").Replace(" ", "").Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= 1000;
        }

        /// <summary>Le choix lu, ou null s'il ne tient pas debout (le pôle sud au-dessus du pôle nord…).</summary>
        private MapLayoutChoice Read()
        {
            if (_fitted.IsChecked != true) return new MapLayoutChoice { Fitted = false };
            double north, south;
            if (!Percent(_north.Text, out north) || !Percent(_south.Text, out south) || south - north < 1) return null;
            return new MapLayoutChoice { Fitted = true, PoleNorth = north / 100, PoleSouth = south / 100 };
        }

        private void Describe()
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            _poles.IsEnabled = true;
            _poles.Opacity = _fitted.IsChecked == true ? 1 : 0.55;
            var choice = Read();
            _ok.IsEnabled = choice != null;
            if (choice == null)
            {
                _echo.Foreground = Chrome.Danger;
                _echo.Text = "Le pôle sud doit être plus bas que le pôle nord (deux pourcentages, le second plus grand).";
                return;
            }
            _echo.Foreground = Chrome.Ink;
            if (!choice.Fitted)
            {
                var ratio = (double)_map.Width / Math.Max(1, _map.Height);
                _echo.Text = Math.Abs(ratio - 2) <= 0.15 ? "L'image est (presque) deux fois plus large que haute : étirée, elle n'est pas déformée."
                    : "Étirée, l'image est " + (ratio < 2 ? "élargie" : "aplatie") + " : elle n'est pas deux fois plus large que haute. Les mesures et le globe s'en ressentent.";
                return;
            }
            var probe = new MapInfo { Width = _map.Width, Height = _map.Height, Layout = Maps.LayoutFitted, PoleNorth = choice.PoleNorth, PoleSouth = choice.PoleSouth };
            var span = Maps.LongitudeSpan(probe);
            double top = Math.PI / 2 - (0 - choice.PoleNorth * _map.Height) * Maps.RadiansPerPixel(probe), bottom = Math.PI / 2 - (_map.Height - choice.PoleNorth * _map.Height) * Maps.RadiansPerPixel(probe);
            var text = "L'image couvre " + span.ToString("0", fr) + "° de longitude" + (span > 360.5 ? " — plus d'un tour : ses bords se recouvrent sur le globe" : span < 359.5 ? " sur 360" : " : un tour complet") + ". ";
            text += choice.PoleNorth > 0.0005 ? "Le pôle nord est dans l'image, à " + (choice.PoleNorth * 100).ToString("0.#", fr) + " % du haut. "
                : choice.PoleNorth < -0.0005 ? "Elle s'arrête à " + (top * 180 / Math.PI).ToString("0", fr) + "° nord. " : "Son bord haut est le pôle nord. ";
            text += choice.PoleSouth < 0.9995 ? "Le pôle sud est dans l'image, à " + (choice.PoleSouth * 100).ToString("0.#", fr) + " % du haut."
                : choice.PoleSouth > 1.0005 ? "Elle s'arrête à " + Math.Abs(bottom * 180 / Math.PI).ToString("0", fr) + "° sud." : "Son bord bas est le pôle sud.";
            _echo.Text = text;
        }

        internal void SetForProbe(bool fitted, string north, string south) { _fitted.IsChecked = fitted; _stretch.IsChecked = !fitted; _north.Text = north; _south.Text = south; Describe(); }
        internal MapLayoutChoice ReadForProbe() { return Read(); }
        internal string EchoForProbe { get { return _echo.Text; } }
        internal void AroundForProbe()
        {
            var half = _map.Width / 4.0 / Math.Max(1, _map.Height);
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            _fitted.IsChecked = true;
            _north.Text = ((0.5 - half) * 100).ToString("0.##", fr);
            _south.Text = ((0.5 + half) * 100).ToString("0.##", fr);
            Describe();
        }
        internal static MapLayoutDialog ForProbe(Window owner, MapInfo map) { return new MapLayoutDialog(owner, map); }

        public static async Task<MapLayoutChoice> Ask(Window owner, MapInfo map)
        {
            var dialog = new MapLayoutDialog(owner, map);
            await Dialogs.ShowModal(dialog, owner);
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
        public static readonly string[] Palette = HexOf(ColorPalette.Maps); // le nuancier des cartes, nommé dans la palette (22/11)

        internal static string[] HexOf(PaletteColor[] colors)
        {
            var list = new string[colors.Length];
            for (var i = 0; i < colors.Length; i++) list[i] = colors[i].Hex;
            return list;
        }

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
            foreach (var custom in ColorMenus.Customs(null)) if (!colors.Exists(delegate(string c) { return string.Equals(c, custom, StringComparison.OrdinalIgnoreCase); })) colors.Add(custom); // la palette de l'auteur (22/11)
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
                    BorderBrush = chosen ? Chrome.Ink : Chrome.Border, BorderThickness = new Thickness(chosen ? 2.5 : 1), Cursor = new Cursor(StandardCursorType.Hand),
                    [ToolTip.TipProperty] = ColorPalette.Describe(color)
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LES MARQUEURS DE NAVIGATION D'UN MONDE (lot 13, 16/11/2026, Rémi : « on doit pouvoir
    /// définir et nommer les tropiques ou autres marqueurs du même type personnalisés, même si la
    /// bascule ira vers les marqueurs par défaut à la création »). Un même éditeur pour deux endroits :
    /// l'onglet « Marqueurs » de la fenêtre du monde, et la fenêtre « Définir les marqueurs » ouverte
    /// depuis le rail d'une carte.
    ///
    /// Par défaut : l'équateur, les deux tropiques et les deux cercles polaires, déduits de l'inclinaison
    /// de l'axe — ils la suivent si elle change. Personnalisés : la liste est celle de l'auteur, un nom
    /// et une latitude chacun (degrés ; le nord en positif, le sud en négatif).</summary>
    internal sealed class GuidesEditor : StackPanel
    {
        private readonly World _world;
        private readonly RadioButton _byDefault, _custom;
        private readonly StackPanel _rows;
        private readonly Button _add, _fromDefaults;
        private readonly TextBlock _note;
        private readonly List<TextBox> _names = new List<TextBox>(), _latitudes = new List<TextBox>();
        private bool _building;
        private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

        public GuidesEditor(World world)
        {
            _world = world;
            _byDefault = new RadioButton { Content = "Marqueurs par défaut", GroupName = "world-guides-" + Guid.NewGuid().ToString("N"), Tag = "guides-default" };
            _custom = new RadioButton { Content = "Marqueurs personnalisés", GroupName = _byDefault.GroupName, Margin = new Thickness(18, 0, 0, 0), Tag = "guides-custom" };
            var choice = new StackPanel { Orientation = Orientation.Horizontal };
            choice.Children.Add(_byDefault);
            choice.Children.Add(_custom);
            Children.Add(choice);
            _note = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8), Tag = "guides-note" };
            Children.Add(_note);
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,110,8,34"), Margin = new Thickness(0, 0, 0, 2) };
            head.Children.Add(new TextBlock { Text = "Nom", Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold });
            var latitudeHead = new TextBlock { Text = "Latitude (°)", Foreground = Chrome.FaintText, FontSize = 11, FontWeight = FontWeight.SemiBold };
            Grid.SetColumn(latitudeHead, 2);
            head.Children.Add(latitudeHead);
            Children.Add(head);
            _rows = new StackPanel { Tag = "guides-rows" };
            Children.Add(_rows);
            _add = Buttons.IconText("plus-bold", "Ajouter un marqueur", "Un parallèle de plus : une latitude, et le nom que vous lui donnez", Buttons.Compact, Buttons.Look.Outline);
            _add.Tag = "guides-add";
            _add.Click += delegate { Collect(); _world.Guides.Add(new WorldGuide { Name = "", Latitude = 0 }); Rebuild(); };
            _fromDefaults = Buttons.Text("Repartir des marqueurs par défaut", "Remplacer la liste par l'équateur, les tropiques et les cercles polaires de ce monde — à renommer ou déplacer ensuite", Buttons.Compact, Buttons.Look.Calm);
            _fromDefaults.Tag = "guides-from-defaults";
            _fromDefaults.Click += delegate { CopyDefaults(); Rebuild(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
            buttons.Children.Add(_add);
            buttons.Children.Add(_fromDefaults);
            Children.Add(buttons);
            _byDefault.IsCheckedChanged += delegate { if (!_building && _byDefault.IsChecked == true) SetCustom(false); };
            _custom.IsCheckedChanged += delegate { if (!_building && _custom.IsChecked == true) SetCustom(true); };
            Rebuild();
        }

        private void CopyDefaults()
        {
            _world.Guides.Clear();
            foreach (var guide in Astro.DefaultGuides(_world)) _world.Guides.Add(new WorldGuide { Name = guide.Name, Latitude = guide.Latitude });
        }

        /// <summary>La bascule : passer aux marqueurs personnalisés part de ceux par défaut (on les renomme, on en ajoute).</summary>
        internal void SetCustom(bool custom)
        {
            if (custom) { if (!_world.CustomGuides && _world.Guides.Count == 0) CopyDefaults(); }
            else Collect();
            _world.CustomGuides = custom;
            Rebuild();
        }

        /// <summary>Relit le monde (son inclinaison a pu changer dans un autre onglet).</summary>
        public void Rebuild()
        {
            _building = true;
            _byDefault.IsChecked = !_world.CustomGuides;
            _custom.IsChecked = _world.CustomGuides;
            _building = false;
            _rows.Children.Clear();
            _names.Clear();
            _latitudes.Clear();
            var custom = _world.CustomGuides;
            var guides = Astro.GuidesOf(_world);
            _note.Text = custom
                ? "La liste est la vôtre : un nom et une latitude par marqueur (le nord en positif, le sud en négatif, de −90 à 90). Elle ne suit plus l'inclinaison de l'axe."
                : _world.HasTilt && _world.AxialTilt >= 0.01 && _world.AxialTilt <= 89.99
                    ? "Déduits de l'inclinaison de l'axe (" + _world.AxialTilt.ToString("0.##", French) + "°) : les tropiques sont à cette latitude, les cercles polaires à son complément. Ils la suivent si elle change."
                    : "Sans inclinaison de l'axe, il n'y a ni tropiques ni cercles polaires : l'équateur seul.";
            for (var i = 0; i < guides.Count; i++)
            {
                var guide = guides[i];
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,110,8,34"), Margin = new Thickness(0, 3, 0, 0), Tag = "guides-row" };
                var name = new TextBox { Text = guide.Name ?? "", IsEnabled = custom, Watermark = "ex. Tropique d'Aube", Tag = "guides-name" };
                var latitude = new TextBox { Text = guide.Latitude.ToString("0.####", French), IsEnabled = custom, Watermark = "ex. 23,4", Tag = "guides-latitude" };
                Grid.SetColumn(latitude, 2);
                row.Children.Add(name);
                row.Children.Add(latitude);
                if (custom)
                {
                    var index = i;
                    var remove = Buttons.Icon("x-bold", "Retirer ce marqueur", Buttons.Compact, Buttons.Look.Calm);
                    remove.Click += delegate { Collect(); if (index < _world.Guides.Count) _world.Guides.RemoveAt(index); Rebuild(); };
                    Grid.SetColumn(remove, 4);
                    row.Children.Add(remove);
                    _names.Add(name);
                    _latitudes.Add(latitude);
                }
                _rows.Children.Add(row);
            }
            if (guides.Count == 0) _rows.Children.Add(new TextBlock { Text = "Aucun marqueur : la carte n'en montrera pas.", Foreground = Chrome.SoftText, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
            _add.IsVisible = _fromDefaults.IsVisible = custom;
        }

        /// <summary>Écrit les champs dans le monde. Rend le problème à dire (une latitude illisible), ou null.</summary>
        public string Collect()
        {
            if (!_world.CustomGuides) return null;
            string problem = null;
            for (var i = 0; i < _names.Count && i < _world.Guides.Count; i++)
            {
                var guide = _world.Guides[i];
                guide.Name = (_names[i].Text ?? "").Trim();
                double value;
                var text = (_latitudes[i].Text ?? "").Trim().Replace("°", "").Replace(" ", "").Replace(',', '.').Replace("−", "-");
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= -90 && value <= 90) guide.Latitude = value;
                else if (problem == null) problem = "Marqueurs : la latitude de « " + (guide.Name.Length == 0 ? "marqueur " + (i + 1) : guide.Name) + " » doit être un nombre de −90 à 90.";
                if (guide.Name.Length == 0) guide.Name = Astro.LatitudeLabel(guide.Latitude);
            }
            return problem;
        }

        internal int RowsForProbe { get { var n = 0; foreach (var child in _rows.Children) if (Equals(child.Tag, "guides-row")) n++; return n; } }
        internal bool CustomForProbe { get { return _custom.IsChecked == true; } }
        internal string NoteForProbe { get { return _note.Text ?? ""; } }
        internal void SetRowForProbe(int index, string name, string latitude) { _names[index].Text = name; _latitudes[index].Text = latitude; }
        internal void AddForProbe() { Collect(); _world.Guides.Add(new WorldGuide { Name = "", Latitude = 0 }); Rebuild(); }
    }

    /// <summary>« Définir les marqueurs », depuis le rail d'une carte : les marqueurs du monde lié,
    /// édités sur une copie ; validés, ils s'écrivent dans le monde (Ctrl+Z les rend).</summary>
    internal sealed class GuidesDialog : Window
    {
        private readonly World _copy;
        private readonly GuidesEditor _editor;
        private readonly TextBlock _problem;
        private World _result;

        private GuidesDialog(Window owner, World world)
        {
            _copy = world.Clone();
            TimelineUi.Dress(this, owner, "Marqueurs de navigation — " + world.Name);
            Width = 520;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            panel.Children.Add(new TextBlock
            {
                Text = "Les parallèles remarquables de « " + world.Name + " » : ils se tracent sur ses cartes du monde entier. Ce sont ceux du monde — on les retrouve dans sa fenêtre, à l'Astrolabe.",
                Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });
            _editor = new GuidesEditor(_copy);
            panel.Children.Add(new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _editor });
            _problem = new TextBlock { Foreground = Chrome.Danger, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), IsVisible = false };
            panel.Children.Add(_problem);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            cancel.Click += delegate { Close(); };
            var ok = new Button { Content = "Valider", IsDefault = true, MinWidth = 80 };
            ok.Click += delegate { if (Accept()) Close(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            Dialogs.Arrange(buttons, ok);
            panel.Children.Add(buttons);
            Content = panel;
        }

        private bool Accept()
        {
            var problem = _editor.Collect();
            if (problem != null) { _problem.Text = problem; _problem.IsVisible = true; return false; }
            _result = _copy;
            return true;
        }

        internal GuidesEditor EditorForProbe { get { return _editor; } }
        internal World AcceptForProbe() { return Accept() ? _result : null; }
        internal string ProblemForProbe { get { return _problem.IsVisible ? _problem.Text : null; } }
        internal static GuidesDialog ForProbe(Window owner, World world) { return new GuidesDialog(owner, world); }

        /// <summary>Le monde, avec ses marqueurs tels que validés ; null si l'auteur renonce.</summary>
        public static async Task<World> Ask(Window owner, World world)
        {
            var dialog = new GuidesDialog(owner, world);
            await Dialogs.ShowModal(dialog, owner);
            return dialog._result;
        }
    }
}

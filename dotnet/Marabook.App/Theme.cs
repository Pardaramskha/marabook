using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Marabook.App
{
    /// <summary>Le thème (porté de View/Theme.cs, P1) : les gabarits des
    /// boutons et bascules — contour, calme, principal, l'état actif —, les
    /// champs, cases, onglets en chip, listes, arbres, infobulles. Écrit en
    /// code : d'abord les RESSOURCES du thème Simple d'Avalonia remplacées
    /// par les pinceaux mutables de Chrome (menus, combos, barres de
    /// défilement, sélections suivent ainsi la palette et le basculement
    /// clair/sombre), puis des styles et quelques gabarits maison par-dessus.
    /// Rayon 6 px partout ; disabled = opacité 0,45.</summary>
    public static class Theme
    {
        public const double Radius = 6;

        /// <summary>La classe des boutons fabriqués par Buttons.cs : les
        /// styles de boutons ne touchent qu'eux — pas les bascules internes
        /// des gabarits du thème de base (l'expanseur d'un TreeViewItem…).</summary>
        public const string Owned = "mb";

        /// <summary>Les classes des trois apparences (Buttons.Look).</summary>
        public const string Calm = "calm";
        public const string Primary = "primary";

        /// <summary>La pilule des infobulles : gris foncé inversé, la même
        /// dans les deux thèmes.</summary>
        private static readonly SolidColorBrush TipBg = new SolidColorBrush(Color.FromArgb(0xE8, 0x61, 0x61, 0x61));

        /// <summary>Les ressources du thème Simple, remplacées par nos
        /// pinceaux : une seule déclaration de chaque couleur (Chrome), et
        /// les contrôles que nous ne re-gabarisons pas suivent quand même.</summary>
        public static void OverrideResources(IResourceDictionary resources)
        {
            resources["ThemeBackgroundBrush"] = Chrome.RaisedBg;
            resources["ThemeBorderLowBrush"] = Chrome.Border;
            resources["ThemeBorderMidBrush"] = Chrome.BorderStrong;
            resources["ThemeBorderHighBrush"] = Chrome.SoftText;
            resources["ThemeControlLowBrush"] = Chrome.BarBg;
            resources["ThemeControlMidBrush"] = Chrome.BarBgLight;
            resources["ThemeControlMidHighBrush"] = Chrome.Border;
            resources["ThemeControlHighBrush"] = Chrome.BorderStrong;
            resources["ThemeControlVeryHighBrush"] = Chrome.SoftText;
            resources["ThemeControlHighlightLowBrush"] = Chrome.AccentTint;
            resources["ThemeControlHighlightMidBrush"] = Chrome.AccentTint;
            resources["ThemeControlHighlightHighBrush"] = Chrome.AccentSoft;
            resources["ThemeForegroundBrush"] = Chrome.Ink;
            resources["ThemeForegroundLowBrush"] = Chrome.SoftText;
            resources["HighlightBrush"] = Chrome.AccentTint;
            resources["HighlightBrush2"] = Chrome.AccentSoft;
            resources["HighlightForegroundBrush"] = Chrome.AccentStrong;
            resources["ThemeAccentBrush"] = Chrome.Accent;
            resources["ThemeAccentBrush2"] = Chrome.AccentStrong;
            resources["ThemeAccentBrush3"] = Chrome.AccentSoft;
            resources["ThemeAccentBrush4"] = Chrome.AccentTint;
            resources["ErrorBrush"] = Chrome.Danger;
            resources["ErrorLowBrush"] = Chrome.Danger;
            resources["ThemeControlTransparentBrush"] = Brushes.Transparent;
            resources["ThemeBorderThickness"] = new Thickness(1);
            resources["ThemeDisabledOpacity"] = 0.45;
            resources["FontSizeSmall"] = 11.0;
            resources["FontSizeNormal"] = 13.0;
            resources["FontSizeLarge"] = 16.0;
            resources["ScrollBarThickness"] = 10.0;
            resources["ScrollBarThumbThickness"] = 6.0;
        }

        public static Styles Build()
        {
            var styles = new Styles();

            // ---- fenêtres et texte
            styles.Add(Style(x => x.OfType<Window>(),
                new Setter(Window.BackgroundProperty, Chrome.RaisedBg),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(TemplatedControl.FontSizeProperty, 13.0)));
            styles.Add(Style(x => x.OfType<TextBlock>(),
                new Setter(TextBlock.ForegroundProperty, Chrome.Ink)));
            styles.Add(Style(x => x.OfType<Menu>(),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.BarBg),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(3, 2))));
            styles.Add(Style(x => x.OfType<MenuItem>(),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4))));

            // Les séparateurs : la couleur du fond de fenêtre (invisibles au
            // repos), l'accent doux sous le pointeur.
            styles.Add(Style(x => x.OfType<GridSplitter>(),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.WindowBg)));
            styles.Add(Style(x => x.OfType<GridSplitter>().Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentSoft)));

            // ---- boutons : le contour est l'implicite (raised + line-strong)
            var buttonTemplate = new FuncControlTemplate<Button>(Face);
            var toggleTemplate = new FuncControlTemplate<ToggleButton>(Face);
            styles.Add(Style(x => x.OfType<Button>().Class(Owned),
                new Setter(TemplatedControl.TemplateProperty, buttonTemplate),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.RaisedBg),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.BorderStrong),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(7, 3)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(Radius)),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand)),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center)));
            styles.Add(Style(x => x.OfType<Button>().Class(Owned).Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.SoftText)));
            styles.Add(Style(x => x.OfType<Button>().Class(Owned).Class(":pressed"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentSoft)));
            styles.Add(Style(x => x.OfType<Button>().Class(Owned).Class(":disabled"),
                new Setter(Visual.OpacityProperty, 0.45)));

            // calme : transparent au repos, accent-tint au survol, accent-soft enfoncé
            styles.Add(Style(x => x.OfType<Button>().Class(Owned).Class(Calm),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent)));
            styles.Add(Style(x => x.OfType<Button>().Class(Owned).Class(Calm).Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent)));
            styles.Add(Style(x => x.OfType<Button>().Class(Owned).Class(Calm).Class(":pressed"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentSoft)));

            // principal : accent plein, texte papier, accent-strong enfoncé
            styles.Add(Style(x => x.OfType<Button>().Class(Owned).Class(Primary),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.Accent),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.Accent),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.RaisedBg),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold)));
            styles.Add(Style(x => x.OfType<Button>().Class(Owned).Class(Primary).Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.Accent),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.AccentStrong)));
            styles.Add(Style(x => x.OfType<Button>().Class(Owned).Class(Primary).Class(":pressed"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentStrong)));

            // ---- bascules : mêmes apparences ; cochée = l'état ACTIF
            styles.Add(Style(x => x.OfType<ToggleButton>().Class(Owned),
                new Setter(TemplatedControl.TemplateProperty, toggleTemplate),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.RaisedBg),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.BorderStrong),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(7, 3)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(Radius)),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand)),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center)));
            styles.Add(Style(x => x.OfType<ToggleButton>().Class(Owned).Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint)));
            styles.Add(Style(x => x.OfType<ToggleButton>().Class(Owned).Class(Calm),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent)));
            styles.Add(Style(x => x.OfType<ToggleButton>().Class(Owned).Class(Calm).Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint)));
            styles.Add(Style(x => x.OfType<ToggleButton>().Class(Owned).Class(":checked"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.AccentSoft),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.AccentStrong)));
            styles.Add(Style(x => x.OfType<ToggleButton>().Class(Owned).Class(":disabled"),
                new Setter(Visual.OpacityProperty, 0.45)));

            // ---- champs : papier, filet, coins 6, accent au clavier
            styles.Add(Style(x => x.OfType<TextBox>(),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.PaperBg),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.Border),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(Radius)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(5, 2)),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(TextBox.CaretBrushProperty, Chrome.Ink),
                new Setter(TextBox.SelectionBrushProperty, Chrome.Accent),
                new Setter(TextBox.SelectionForegroundBrushProperty, Chrome.PaperBg),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center)));
            foreach (var name in new[] { "PART_BorderElement", "border", "PART_ContentPresenter" })
            {
                var border = name;
                styles.Add(Style(x => x.OfType<TextBox>().Class(":pointerover").Template().OfType<Border>().Name(border),
                    new Setter(Border.BorderBrushProperty, Chrome.SoftText),
                    new Setter(Border.BackgroundProperty, Chrome.PaperBg)));
                styles.Add(Style(x => x.OfType<TextBox>().Class(":focus").Template().OfType<Border>().Name(border),
                    new Setter(Border.BorderBrushProperty, Chrome.Accent),
                    new Setter(Border.BackgroundProperty, Chrome.PaperBg)));
            }
            styles.Add(Style(x => x.OfType<TextBox>().Class(":disabled"),
                new Setter(Visual.OpacityProperty, 0.45)));

            // ---- cases à cocher : un carré 16, coins 4, accent plein cochée
            styles.Add(Style(x => x.OfType<CheckBox>(),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<CheckBox>(CheckFace)),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand))));
            styles.Add(Style(x => x.OfType<CheckBox>().Class(":pointerover").Template().OfType<Border>().Name("Box"),
                new Setter(Border.BorderBrushProperty, Chrome.Accent)));
            styles.Add(Style(x => x.OfType<CheckBox>().Class(":checked").Template().OfType<Border>().Name("Box"),
                new Setter(Border.BackgroundProperty, Chrome.Accent),
                new Setter(Border.BorderBrushProperty, Chrome.Accent)));
            styles.Add(Style(x => x.OfType<CheckBox>().Class(":checked").Template().OfType<Path>().Name("Check"),
                new Setter(Visual.IsVisibleProperty, true)));
            styles.Add(Style(x => x.OfType<CheckBox>().Class(":disabled"),
                new Setter(Visual.OpacityProperty, 0.45)));

            // ---- onglets en CHIP : l'actif est une pastille d'accent, texte papier
            styles.Add(Style(x => x.OfType<TabControl>(),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(4))));
            // Encre et graisse sont posées sur l'EN-TÊTE (Bg), jamais sur le
            // TabItem : son contenu en hériterait (règle héritée de WPF, b42).
            styles.Add(Style(x => x.OfType<TabItem>(),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<TabItem>(ChipFace)),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(12)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 4)),
                new Setter(Layoutable.MarginProperty, new Thickness(3, 4, 0, 4)),
                new Setter(TemplatedControl.FontSizeProperty, 13.0),
                new Setter(Layoutable.MinHeightProperty, 0.0)));
            styles.Add(Style(x => x.OfType<TabItem>().Template().OfType<Border>().Name("Bg"),
                new Setter(TextElement.ForegroundProperty, Chrome.SoftText),
                new Setter(TextElement.FontWeightProperty, FontWeight.Normal),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand))));
            styles.Add(Style(x => x.OfType<TabItem>().Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint)));
            styles.Add(Style(x => x.OfType<TabItem>().Class(":selected"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.Accent),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.Accent)));
            styles.Add(Style(x => x.OfType<TabItem>().Class(":selected").Template().OfType<Border>().Name("Bg"),
                new Setter(TextElement.ForegroundProperty, Chrome.RaisedBg),
                new Setter(TextElement.FontWeightProperty, FontWeight.SemiBold)));

            // ---- listes et arbres : la ligne active en accent-tint
            styles.Add(Style(x => x.OfType<ListBox>(),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.PaperBg),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.Border),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(Radius)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(2))));
            styles.Add(Style(x => x.OfType<ListBoxItem>(),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 3)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4))));
            styles.Add(Style(x => x.OfType<TreeView>(),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0))));
            styles.Add(Style(x => x.OfType<TreeViewItem>(),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(Radius))));

            // ---- infobulles : pilule inversée, texte blanc compact
            styles.Add(Style(x => x.OfType<ToolTip>(),
                new Setter(TemplatedControl.BackgroundProperty, TipBg),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(4)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 4, 8, 5)),
                new Setter(TemplatedControl.ForegroundProperty, Brushes.White),
                new Setter(TemplatedControl.FontSizeProperty, 11.0)));

            // ---- curseurs
            styles.Add(Style(x => x.OfType<Slider>(),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand))));

            return styles;
        }

        /// <summary>Le visage commun des boutons : un cadre arrondi qui suit
        /// Background / BorderBrush / CornerRadius du contrôle (donc ses
        /// états), le contenu centré dedans.</summary>
        private static Control Face(TemplatedControl parent, INameScope scope)
        {
            var presenter = new ContentPresenter { Name = "Presenter" };
            presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentControl.ContentProperty), BindingPriority.Template);
            presenter.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(ContentControl.ContentTemplateProperty), BindingPriority.Template);
            presenter.Bind(ContentPresenter.PaddingProperty, new TemplateBinding(TemplatedControl.PaddingProperty), BindingPriority.Template);
            presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty), BindingPriority.Template);
            presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, new TemplateBinding(ContentControl.VerticalContentAlignmentProperty), BindingPriority.Template);
            var border = new Border { Name = "Bg", Child = presenter };
            border.Bind(Border.BackgroundProperty, new TemplateBinding(TemplatedControl.BackgroundProperty), BindingPriority.Template);
            border.Bind(Border.BorderBrushProperty, new TemplateBinding(TemplatedControl.BorderBrushProperty), BindingPriority.Template);
            border.Bind(Border.BorderThicknessProperty, new TemplateBinding(TemplatedControl.BorderThicknessProperty), BindingPriority.Template);
            border.Bind(Border.CornerRadiusProperty, new TemplateBinding(TemplatedControl.CornerRadiusProperty), BindingPriority.Template);
            presenter.RegisterInNameScope(scope);
            border.RegisterInNameScope(scope);
            return border;
        }

        /// <summary>L'onglet en chip : la pastille (Bg) suit Background /
        /// BorderBrush / CornerRadius du TabItem, l'en-tête au centre.</summary>
        private static Control ChipFace(TemplatedControl parent, INameScope scope)
        {
            var presenter = new ContentPresenter { Name = "Header", VerticalAlignment = VerticalAlignment.Center };
            presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(HeaderedContentControl.HeaderProperty), BindingPriority.Template);
            presenter.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(HeaderedContentControl.HeaderTemplateProperty), BindingPriority.Template);
            var border = new Border { Name = "Bg", Child = presenter };
            border.Bind(Border.BackgroundProperty, new TemplateBinding(TemplatedControl.BackgroundProperty), BindingPriority.Template);
            border.Bind(Border.BorderBrushProperty, new TemplateBinding(TemplatedControl.BorderBrushProperty), BindingPriority.Template);
            border.Bind(Border.BorderThicknessProperty, new TemplateBinding(TemplatedControl.BorderThicknessProperty), BindingPriority.Template);
            border.Bind(Border.CornerRadiusProperty, new TemplateBinding(TemplatedControl.CornerRadiusProperty), BindingPriority.Template);
            border.Bind(Border.PaddingProperty, new TemplateBinding(TemplatedControl.PaddingProperty), BindingPriority.Template);
            presenter.RegisterInNameScope(scope);
            border.RegisterInNameScope(scope);
            return border;
        }

        /// <summary>La case à cocher : le carré (Box), la coche (Check, cachée
        /// tant que la case n'est pas cochée), le libellé.</summary>
        private static Control CheckFace(TemplatedControl parent, INameScope scope)
        {
            var check = new Path
            {
                Name = "Check",
                Data = Geometry.Parse("M3,8 L7,12 13,4"),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(3),
                IsVisible = false
            };
            var box = new Border
            {
                Name = "Box",
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = check
            };
            var presenter = new ContentPresenter
            {
                Name = "Label",
                Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentControl.ContentProperty), BindingPriority.Template);
            presenter.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(ContentControl.ContentTemplateProperty), BindingPriority.Template);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
            row.Children.Add(box);
            row.Children.Add(presenter);
            check.RegisterInNameScope(scope);
            box.RegisterInNameScope(scope);
            presenter.RegisterInNameScope(scope);
            return row;
        }

        private static Style Style(System.Func<Selector, Selector> selector, params Setter[] setters)
        {
            var style = new Style(selector);
            foreach (var setter in setters) style.Setters.Add(setter);
            return style;
        }
    }
}

using System;
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
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

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

        /// <summary>L'épaisseur des barres de défilement (le pouce y laisse
        /// 2 px de marge de chaque côté).</summary>
        public const double ScrollBarSize = 12;

        /// <summary>La classe des BASCULES habillées par le thème : Buttons.cs
        /// la pose, et toute ToggleButton fabriquée à la main doit la porter
        /// (Classes = { Theme.Owned }). Les styles de bascules ne touchent
        /// qu'elles — pas les bascules internes des gabarits du thème de base
        /// (l'expanseur d'un TreeViewItem, la flèche d'un ComboBox…). Les
        /// Button, eux, sont TOUS habillés (style implicite, comme en WPF) :
        /// le thème de base n'en glisse aucun dans nos gabarits (28/09).</summary>
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
            resources["ScrollBarThickness"] = ScrollBarSize;
            resources["ScrollBarThumbThickness"] = ScrollBarSize - 4;
        }

        public static Styles Build()
        {
            var styles = new Styles();

            // ---- fenêtres et texte
            styles.Add(Style(x => x.OfType<Window>(),
                new Setter(Window.BackgroundProperty, Chrome.RaisedBg),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(TemplatedControl.FontSizeProperty, 13.0)));
            // Pas de style implicite sur TextBlock : l'encre vient de la
            // fenêtre par héritage, et un texte posé dans un bouton principal,
            // une bascule active ou un onglet choisi prend LEUR couleur (le
            // style forçait l'encre sombre sur l'accent, 28/09).
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

            // ---- boutons : le contour est l'implicite (raised + line-strong),
            // pour TOUT Button — ceux des dialogues et du ruban écrits à la
            // main comme ceux de Buttons.cs (le passage à Avalonia les avait
            // laissés aux angles droits du thème Simple, 28/09).
            var buttonTemplate = new FuncControlTemplate<Button>(Face);
            var toggleTemplate = new FuncControlTemplate<ToggleButton>(Face);
            styles.Add(Style(x => x.OfType<Button>(),
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
            styles.Add(Style(x => x.OfType<Button>().Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.SoftText)));
            styles.Add(Style(x => x.OfType<Button>().Class(":pressed"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentSoft)));
            styles.Add(Style(x => x.OfType<Button>().Class(":disabled"),
                new Setter(Visual.OpacityProperty, 0.45)));

            // calme : transparent au repos, accent-tint au survol, accent-soft enfoncé
            styles.Add(Style(x => x.OfType<Button>().Class(Calm),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent)));
            styles.Add(Style(x => x.OfType<Button>().Class(Calm).Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent)));
            styles.Add(Style(x => x.OfType<Button>().Class(Calm).Class(":pressed"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentSoft)));

            // principal : accent plein, texte papier, accent-strong enfoncé
            styles.Add(Style(x => x.OfType<Button>().Class(Primary),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.Accent),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.Accent),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.RaisedBg),
                new Setter(TemplatedControl.FontWeightProperty, FontWeight.SemiBold)));
            styles.Add(Style(x => x.OfType<Button>().Class(Primary).Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.Accent),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.AccentStrong)));
            styles.Add(Style(x => x.OfType<Button>().Class(Primary).Class(":pressed"),
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
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                // Le curseur texte sur TOUT le champ (29/09) : le thème ne le
                // pose que sur le présentateur, soit les lignes écrites — dans
                // une zone multiligne, le pointeur restait une flèche sous la
                // ligne active. Cursor s'hérite : les enfants le reçoivent.
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Ibeam))));
            // … sauf la barre de défilement d'un champ multiligne, qui reste
            // une flèche comme partout ailleurs.
            styles.Add(Style(x => x.OfType<TextBox>().Descendant().OfType<ScrollBar>(),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Arrow))));
            // Le cadre du gabarit Simple s'appelle « border » (TextBox et
            // ComboBox) ; les coins y sont posés directement (27/09) : le
            // thème de contrôle gardait ses angles droits.
            styles.Add(Style(x => x.OfType<TextBox>().Template().OfType<Border>().Name("border"),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(Radius))));
            styles.Add(Style(x => x.OfType<TextBox>().Class(":pointerover").Template().OfType<Border>().Name("border"),
                new Setter(Border.BorderBrushProperty, Chrome.SoftText),
                new Setter(Border.BackgroundProperty, Chrome.PaperBg)));
            styles.Add(Style(x => x.OfType<TextBox>().Class(":focus").Template().OfType<Border>().Name("border"),
                new Setter(Border.BorderBrushProperty, Chrome.Accent),
                new Setter(Border.BackgroundProperty, Chrome.PaperBg)));
            styles.Add(Style(x => x.OfType<TextBox>().Class(":disabled"),
                new Setter(Visual.OpacityProperty, 0.45)));

            // ---- combos : le même champ, coins 6, flèche discrète
            styles.Add(Style(x => x.OfType<ComboBox>(),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.PaperBg),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.Border),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(Radius)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 2, 2, 2)),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink)));
            styles.Add(Style(x => x.OfType<ComboBox>().Template().OfType<Border>().Name("border"),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(Radius))));
            styles.Add(Style(x => x.OfType<ComboBox>().Class(":pointerover").Template().OfType<Border>().Name("border"),
                new Setter(Border.BorderBrushProperty, Chrome.SoftText),
                new Setter(Border.BackgroundProperty, Chrome.PaperBg)));
            styles.Add(Style(x => x.OfType<ComboBox>().Class(":focus").Template().OfType<Border>().Name("border"),
                new Setter(Border.BorderBrushProperty, Chrome.Accent),
                new Setter(Border.BackgroundProperty, Chrome.PaperBg)));
            styles.Add(Style(x => x.OfType<ComboBox>().Class(":disabled"),
                new Setter(Visual.OpacityProperty, 0.45)));

            // ---- barres de défilement : une piste transparente, un pouce
            // arrondi de 8 px dans une bande de 12, sans flèches (le style
            // WPF) — le gabarit Simple (flèches, pouce carré, piste grise)
            // était hors cadre (27/09).
            var vScroll = new FuncControlTemplate<ScrollBar>(delegate(ScrollBar parent, INameScope scope) { return ScrollBarFace(scope, true); });
            var hScroll = new FuncControlTemplate<ScrollBar>(delegate(ScrollBar parent, INameScope scope) { return ScrollBarFace(scope, false); });
            styles.Add(Style(x => x.OfType<ScrollBar>(),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent)));
            styles.Add(Style(x => x.OfType<ScrollBar>().Class(":vertical"),
                new Setter(TemplatedControl.TemplateProperty, vScroll),
                new Setter(Layoutable.WidthProperty, ScrollBarSize)));
            styles.Add(Style(x => x.OfType<ScrollBar>().Class(":horizontal"),
                new Setter(TemplatedControl.TemplateProperty, hScroll),
                new Setter(Layoutable.HeightProperty, ScrollBarSize)));
            styles.Add(Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>(),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.BorderStrong)));
            styles.Add(Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>().Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.SoftText)));
            styles.Add(Style(x => x.OfType<ScrollBar>().Template().OfType<Thumb>().Class(":pressed"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.SoftText)));
            styles.Add(Style(x => x.OfType<ScrollBar>().Class(":vertical").Template().OfType<Thumb>(),
                new Setter(Layoutable.WidthProperty, ScrollBarSize),
                new Setter(Layoutable.MinHeightProperty, 28.0)));
            styles.Add(Style(x => x.OfType<ScrollBar>().Class(":horizontal").Template().OfType<Thumb>(),
                new Setter(Layoutable.HeightProperty, ScrollBarSize),
                new Setter(Layoutable.MinWidthProperty, 28.0)));

            // ---- cases à cocher : un carré 16, coins 4, accent plein cochée
            styles.Add(Style(x => x.OfType<CheckBox>(),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<CheckBox>(CheckFace)),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand))));
            styles.Add(Style(x => x.OfType<CheckBox>().Template().OfType<Border>().Name("Box"),
                new Setter(Border.BackgroundProperty, Chrome.PaperBg),
                new Setter(Border.BorderBrushProperty, Chrome.Border)));
            styles.Add(Style(x => x.OfType<CheckBox>().Template().OfType<Path>().Name("Check"),
                new Setter(Visual.IsVisibleProperty, false)));
            styles.Add(Style(x => x.OfType<CheckBox>().Class(":pointerover").Template().OfType<Border>().Name("Box"),
                new Setter(Border.BorderBrushProperty, Chrome.Accent)));
            styles.Add(Style(x => x.OfType<CheckBox>().Class(":checked").Template().OfType<Border>().Name("Box"),
                new Setter(Border.BackgroundProperty, Chrome.Accent),
                new Setter(Border.BorderBrushProperty, Chrome.Accent)));
            styles.Add(Style(x => x.OfType<CheckBox>().Class(":checked").Template().OfType<Path>().Name("Check"),
                new Setter(Visual.IsVisibleProperty, true)));
            styles.Add(Style(x => x.OfType<CheckBox>().Class(":disabled"),
                new Setter(Visual.OpacityProperty, 0.45)));

            // ---- boutons radio : un rond 16, point blanc sur accent plein choisi
            styles.Add(Style(x => x.OfType<RadioButton>(),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<RadioButton>(RadioFace)),
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand))));
            styles.Add(Style(x => x.OfType<RadioButton>().Template().OfType<Border>().Name("Box"),
                new Setter(Border.BackgroundProperty, Chrome.PaperBg),
                new Setter(Border.BorderBrushProperty, Chrome.Border)));
            styles.Add(Style(x => x.OfType<RadioButton>().Template().OfType<Ellipse>().Name("Dot"),
                new Setter(Visual.IsVisibleProperty, false)));
            styles.Add(Style(x => x.OfType<RadioButton>().Class(":pointerover").Template().OfType<Border>().Name("Box"),
                new Setter(Border.BorderBrushProperty, Chrome.Accent)));
            styles.Add(Style(x => x.OfType<RadioButton>().Class(":checked").Template().OfType<Border>().Name("Box"),
                new Setter(Border.BackgroundProperty, Chrome.Accent),
                new Setter(Border.BorderBrushProperty, Chrome.Accent)));
            styles.Add(Style(x => x.OfType<RadioButton>().Class(":checked").Template().OfType<Ellipse>().Name("Dot"),
                new Setter(Visual.IsVisibleProperty, true)));
            styles.Add(Style(x => x.OfType<RadioButton>().Class(":disabled"),
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
                new Setter(TextElement.FontWeightProperty, FontWeight.SemiBold), // un cran de plus (28/09)
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand))));
            styles.Add(Style(x => x.OfType<TabItem>().Class(":pointerover"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint)));
            styles.Add(Style(x => x.OfType<TabItem>().Class(":selected"),
                new Setter(TemplatedControl.BackgroundProperty, Chrome.Accent),
                new Setter(TemplatedControl.BorderBrushProperty, Chrome.Accent)));
            styles.Add(Style(x => x.OfType<TabItem>().Class(":selected").Template().OfType<Border>().Name("Bg"),
                new Setter(TextElement.ForegroundProperty, Chrome.RaisedBg),
                new Setter(TextElement.FontWeightProperty, FontWeight.Bold)));

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

            // ---- infobulles : pilule inversée, texte blanc compact, et la
            // FLÈCHE qui pointe le contrôle (28/09 : perdue au portage). La
            // bulle se pose SOUS le contrôle (et non au pointeur) pour que la
            // flèche vise quelque chose : le placement est posé par style sur
            // tous les contrôles (Is<Control> : les dérivés aussi), l'écart
            // vertical avec. PIÈGE : OverrideDefaultValue<Control> lève
            // « Metadata is already set » — la propriété attachée est
            // déclarée pour Control, ses métadonnées existent déjà.
            styles.Add(Style(x => x.Is<Control>(),
                new Setter(ToolTip.PlacementProperty, PlacementMode.Bottom),
                new Setter(ToolTip.VerticalOffsetProperty, 2.0)));
            styles.Add(Style(x => x.OfType<ToolTip>(),
                new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<ToolTip>(TipFace)),
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
            // PIÈGE (27/09) : aucune valeur LOCALE sur ce que les styles
            // d'état doivent changer (visibilité de la coche, fond et filet
            // du carré) — une valeur locale prime sur tout style, et la case
            // cochée restait vide. Le repos est posé par les styles aussi.
            var check = new Path
            {
                Name = "Check",
                Data = Geometry.Parse("M3,8 L7,12 13,4"),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(3)
            };
            var box = new Border
            {
                Name = "Box",
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(4),
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

        /// <summary>L'infobulle : une petite flèche vers le haut (vers le
        /// contrôle, la bulle se pose dessous) puis la pilule ; les deux
        /// suivent Background, la pilule aussi CornerRadius et Padding.</summary>
        private static Control TipFace(TemplatedControl parent, INameScope scope)
        {
            var arrow = new Path
            {
                Name = "Arrow",
                Data = Geometry.Parse("M0,6 L6,0 L12,6 Z"),
                Width = 12,
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(14, 0, 0, 0)
            };
            arrow.Bind(Shape.FillProperty, new TemplateBinding(TemplatedControl.BackgroundProperty), BindingPriority.Template);
            var presenter = new ContentPresenter { Name = "PART_ContentPresenter" };
            presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentControl.ContentProperty), BindingPriority.Template);
            presenter.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(ContentControl.ContentTemplateProperty), BindingPriority.Template);
            presenter.Bind(ContentPresenter.PaddingProperty, new TemplateBinding(TemplatedControl.PaddingProperty), BindingPriority.Template);
            var pill = new Border { Name = "Pill", Child = presenter };
            pill.Bind(Border.BackgroundProperty, new TemplateBinding(TemplatedControl.BackgroundProperty), BindingPriority.Template);
            pill.Bind(Border.CornerRadiusProperty, new TemplateBinding(TemplatedControl.CornerRadiusProperty), BindingPriority.Template);
            var column = new StackPanel { Orientation = Orientation.Vertical };
            column.Children.Add(arrow);
            column.Children.Add(pill);
            arrow.RegisterInNameScope(scope);
            pill.RegisterInNameScope(scope);
            presenter.RegisterInNameScope(scope);
            // La flèche vise le CENTRE du contrôle survolé (28/09 soir : à
            // 14 px fixes, elle pointait le bouton voisin dès que la bulle
            // dépassait le bouton — Avalonia centre la bulle dessous, et la
            // fait glisser au bord de l'écran). Recalculé à chaque passe de
            // mise en page de la bulle ; sans changement, rien n'est reposé.
            parent.LayoutUpdated += delegate { AimArrow(parent, arrow, pill); };
            return column;
        }

        /// <summary>Pose la flèche de l'infobulle sous le milieu du contrôle
        /// qu'elle décrit : la cible est le PlacementTarget du Popup qui
        /// porte la bulle (parent logique de son PopupRoot), l'écart se lit
        /// en pixels d'écran puis revient en unités logiques.</summary>
        private static void AimArrow(TemplatedControl tip, Path arrow, Border pill)
        {
            var root = tip.GetVisualRoot() as PopupRoot;
            if (root == null) return;
            var popup = ((ILogical)root).LogicalParent as Popup;
            var target = popup != null ? popup.PlacementTarget : null;
            if (target == null || !target.IsAttachedToVisualTree() || pill.Bounds.Width <= 0) return;
            PixelPoint targetCenter, tipOrigin;
            try
            {
                targetCenter = target.PointToScreen(new Point(target.Bounds.Width / 2, 0));
                tipOrigin = tip.PointToScreen(new Point(0, 0));
            }
            catch (Exception) { return; } // fenêtre en cours de fermeture
            var scale = root.RenderScaling > 0 ? root.RenderScaling : 1;
            var x = (targetCenter.X - tipOrigin.X) / scale - arrow.Width / 2;
            var max = pill.Bounds.Width - arrow.Width - 6;
            if (x > max) x = max;
            if (x < 6) x = 6;
            if (Math.Abs(arrow.Margin.Left - x) < 0.5) return;
            arrow.Margin = new Thickness(x, 0, 0, 0);
        }

        /// <summary>Le bouton radio : le même visage que la case — un rond
        /// (Box) et un point (Dot, caché tant que le bouton n'est pas choisi),
        /// le libellé. Mêmes règles : aucune valeur locale sur ce que les
        /// styles d'état changent (28/09, le gabarit Simple n'affichait pas
        /// le choix, comme la case avant le 27/09).</summary>
        private static Control RadioFace(TemplatedControl parent, INameScope scope)
        {
            var dot = new Ellipse
            {
                Name = "Dot",
                Width = 6,
                Height = 6,
                Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var box = new Border
            {
                Name = "Box",
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = dot
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
            dot.RegisterInNameScope(scope);
            box.RegisterInNameScope(scope);
            presenter.RegisterInNameScope(scope);
            return row;
        }

        /// <summary>La barre de défilement : une piste transparente qui
        /// porte un Track (boutons de page invisibles, pouce arrondi). Les
        /// bornes, la fenêtre et la valeur suivent la barre (liaisons de
        /// gabarit, la valeur dans les deux sens).</summary>
        private static Control ScrollBarFace(INameScope scope, bool vertical)
        {
            var thumb = new Thumb
            {
                Name = "thumb",
                Template = new FuncControlTemplate<Thumb>(ThumbFace)
            };
            var track = new Track
            {
                Name = "PART_Track",
                Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
                IsDirectionReversed = vertical,
                Thumb = thumb,
                DecreaseButton = PageButton("PART_PageUpButton"),
                IncreaseButton = PageButton("PART_PageDownButton")
            };
            track.Bind(Track.MinimumProperty, new TemplateBinding(RangeBase.MinimumProperty), BindingPriority.Template);
            track.Bind(Track.MaximumProperty, new TemplateBinding(RangeBase.MaximumProperty), BindingPriority.Template);
            track.Bind(Track.ViewportSizeProperty, new TemplateBinding(ScrollBar.ViewportSizeProperty), BindingPriority.Template);
            track.Bind(Track.ValueProperty, new TemplateBinding(RangeBase.ValueProperty) { Mode = BindingMode.TwoWay }, BindingPriority.Template);
            var host = new Border { Name = "Rail", Background = Brushes.Transparent, Child = track };
            host.Bind(Border.BackgroundProperty, new TemplateBinding(TemplatedControl.BackgroundProperty), BindingPriority.Template);
            thumb.RegisterInNameScope(scope);
            track.RegisterInNameScope(scope);
            host.RegisterInNameScope(scope);
            return host;
        }

        /// <summary>Le pouce : un rectangle arrondi dans la couleur du contrôle,
        /// 2 px d'air tout autour.</summary>
        private static Control ThumbFace(TemplatedControl parent, INameScope scope)
        {
            var face = new Border { CornerRadius = new CornerRadius((ScrollBarSize - 4) / 2), Margin = new Thickness(2) };
            face.Bind(Border.BackgroundProperty, new TemplateBinding(TemplatedControl.BackgroundProperty), BindingPriority.Template);
            return face;
        }

        /// <summary>Le bouton de page (clic sur la piste hors du pouce) :
        /// invisible, mais il répond.</summary>
        private static RepeatButton PageButton(string name)
        {
            var button = new RepeatButton
            {
                Name = name,
                Focusable = false,
                Background = Brushes.Transparent,
                Template = new FuncControlTemplate<RepeatButton>(delegate(RepeatButton parent, INameScope scope)
                {
                    return new Border { Background = Brushes.Transparent };
                })
            };
            return button;
        }

        private static Style Style(System.Func<Selector, Selector> selector, params Setter[] setters)
        {
            var style = new Style(selector);
            foreach (var setter in setters) style.Setters.Add(setter);
            return style;
        }
    }
}

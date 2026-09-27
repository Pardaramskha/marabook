using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
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
    /// surfaces des fenêtres, les filets. Écrit en code, pas en XAML : des
    /// styles posés par-dessus le thème Simple d'Avalonia, sur des pinceaux
    /// mutables de Chrome — basculer le thème recolore tout.
    /// Rayon 6 px partout ; disabled = opacité 0,45.</summary>
    public static class Theme
    {
        public const double Radius = 6;

        /// <summary>La classe des boutons fabriqués par Buttons.cs : les
        /// styles ci-dessous ne touchent qu'eux — pas les bascules internes
        /// des gabarits du thème de base (l'expanseur d'un TreeViewItem…).</summary>
        public const string Owned = "mb";

        /// <summary>Les classes des trois apparences (Buttons.Look).</summary>
        public const string Calm = "calm";
        public const string Primary = "primary";

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
                new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink)));
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

        private static Style Style(System.Func<Selector, Selector> selector, params Setter[] setters)
        {
            var style = new Style(selector);
            foreach (var setter in setters) style.Setters.Add(setter);
            return style;
        }
    }
}

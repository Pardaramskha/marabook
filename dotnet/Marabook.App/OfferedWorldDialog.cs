using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>« VOUS AVEZ RÉUSSI » (2.0.0, 26/11/2026, Rémi) — la fenêtre du dernier succès, « S'offrir un
    /// monde » : elle paraît à l'export du livre qui remplit toutes ses conditions (Achievements.WorldMissing),
    /// AVANT l'export, et le bouton « En avant ! » la referme, conclut l'export et fait tomber le succès.
    /// Elle obéit au skin « Ascension » QUEL QUE SOIT le skin en cours — c'est lui qu'elle débloque : nuit
    /// bleue, filets d'or, sa police de titres, un ciel étoilé. Sa silhouette est GALBÉE SUR LE DESSUS (une
    /// arche), et elle ne porte ni barre de titre ni croix : on n'en sort que par son bouton.
    /// Le code « dev-ascension » (palette Ctrl+,) la rejoue sans export, pour la voir.</summary>
    internal sealed class OfferedWorldDialog : Window
    {
        internal const string Heading = "Vous avez réussi";
        internal const string Body =
            "Bravo. Vous avez été plus loin que bien des gens, vous avez planifié, documenté, illustré et écrit un univers entier. "
            + "Vous n'aviez pas besoin d'en arriver là, mais vous l'avez fait. Que ce soit sous la guidance de la passion, de la folie "
            + "ou de l'émerveillement, vous venez de franchir un cap qui restera resplendissant durant toute votre vie. Vous avez utilisé "
            + "toute votre imagination, tous vos moyens et vos connaissances, le tout pour bourrer Marabook jusqu'à la moelle, mais surtout, "
            + "vous vous êtes offert un monde.";
        internal static readonly string[] Lines =
        {
            "Soyez fier.",
            "Cliquez ci-dessous pour finir l'export de votre livre et commencer une toute nouvelle aventure.",
            "Et surtout, n'oubliez pas de rêver."
        };

        private static readonly Color Night = Color.Parse("#0A1428"), Deep = Color.Parse("#030B15"), Gold = Color.Parse("#C89B3C"), Pale = Color.Parse("#F0E6D2"), Bronze = Color.Parse("#785A28");

        /// <summary>La hauteur de l'arche, au-dessus des flancs droits.</summary>
        internal const double Arch = 86;
        private readonly Button _forward;

        public OfferedWorldDialog()
        {
            Title = Heading;
            Width = 620;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            SystemDecorations = SystemDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var title = Skin.AscentTitleFont;

            var column = new StackPanel { Margin = new Thickness(54, Arch + 6, 54, 34) };
            column.Children.Add(new TextBlock
            {
                Text = Heading.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
                FontFamily = title,
                FontSize = 34,
                LetterSpacing = 3,
                Foreground = new SolidColorBrush(Pale),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Tag = "world-heading"
            });
            column.Children.Add(new WorldRule { Margin = new Thickness(40, 12, 40, 18) });
            // Le texte, en lettres d'or.
            var gold = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
            gold.GradientStops.Add(new GradientStop(Color.Parse("#FBEFC8"), 0));
            gold.GradientStops.Add(new GradientStop(Color.Parse("#E3C47A"), 0.45));
            gold.GradientStops.Add(new GradientStop(Gold, 1));
            column.Children.Add(Paragraph(Body, gold, 0));
            foreach (var line in Lines) column.Children.Add(Paragraph(line, gold, 14));

            // « En avant ! » : l'or en relief.
            var face = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            face.GradientStops.Add(new GradientStop(Color.Parse("#FBEFC8"), 0));
            face.GradientStops.Add(new GradientStop(Color.Parse("#E3C47A"), 0.35));
            face.GradientStops.Add(new GradientStop(Gold, 0.7));
            face.GradientStops.Add(new GradientStop(Color.Parse("#8A6420"), 1));
            _forward = new Button
            {
                Classes = { Marabook.App.Theme.Owned },
                Content = new TextBlock { Text = "EN AVANT !", FontFamily = title, FontSize = 17, LetterSpacing = 2.5, Foreground = new SolidColorBrush(Color.Parse("#1D1406")) },
                Background = face,
                BorderBrush = new SolidColorBrush(Color.Parse("#FBEFC8")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(34, 9, 34, 9),
                Margin = new Thickness(0, 26, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = "world-forward"
            };
            _forward.Click += delegate { Close(true); };
            column.Children.Add(_forward);

            var layers = new Panel();
            layers.Children.Add(new WorldFrame());
            layers.Children.Add(column);
            Content = layers;
            // On la déplace en la tirant par son arche ; Échap ne la ferme pas (elle n'a que son bouton).
            layers.PointerPressed += delegate(object sender, PointerPressedEventArgs e) { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.GetPosition(this).Y < Arch + 60) BeginMoveDrag(e); };
            Opened += delegate { _forward.Focus(); };
        }

        private static TextBlock Paragraph(string text, IBrush ink, double above)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = ink,
                FontSize = 14.5,
                LineHeight = 23,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, above, 0, 0),
                Tag = "world-text"
            };
        }

        /// <summary>Montre la fenêtre par-dessus <paramref name="owner"/> et attend « En avant ! ».</summary>
        public static async Task Show(Window owner)
        {
            var dialog = new OfferedWorldDialog();
            if (owner == null) { dialog.Show(); return; }
            await dialog.ShowDialog<bool?>(owner);
        }

        internal void ForwardForProbe() { Close(true); }

        /// <summary>Le filet sous le titre : deux traits d'or qui s'effacent, un losange au milieu.</summary>
        private sealed class WorldRule : Control
        {
            public WorldRule() { Height = 12; IsHitTestVisible = false; }
            public override void Render(DrawingContext context)
            {
                double w = Bounds.Width, y = 6.5, middle = Math.Round(w / 2);
                if (w < 60) return;
                foreach (var side in new[] { -1, 1 })
                {
                    var fade = new LinearGradientBrush { StartPoint = new RelativePoint(middle + side * 12, y, RelativeUnit.Absolute), EndPoint = new RelativePoint(side < 0 ? 0 : w, y, RelativeUnit.Absolute) };
                    fade.GradientStops.Add(new GradientStop(Pale, 0));
                    fade.GradientStops.Add(new GradientStop(Gold, 0.35));
                    fade.GradientStops.Add(new GradientStop(Color.FromArgb(0, Gold.R, Gold.G, Gold.B), 1));
                    context.DrawLine(new Pen(fade, 1), new Point(middle + side * 12, y), new Point(side < 0 ? 0 : w, y));
                }
                AscensionArt.Diamond(context, new Point(middle, y), 5.5, null, new Pen(new SolidColorBrush(Gold), 1.1));
                AscensionArt.Diamond(context, new Point(middle, y), 2.2, new SolidColorBrush(Pale), null);
            }
        }

        /// <summary>La silhouette : des flancs droits, une ARCHE en haut ; la nuit dedans, quelques étoiles (tirées une
        /// fois), un double filet d'or, un cimier au sommet de l'arche et deux coins au pied.</summary>
        internal sealed class WorldFrame : Control
        {
            public WorldFrame() { IsHitTestVisible = false; Tag = "world-frame"; }

            internal static Geometry Outline(Size size, double inner)
            {
                double w = size.Width, h = size.Height, x = inner + 0.6, right = w - inner - 0.6, bottom = h - inner - 0.6, top = inner + 0.6, shoulder = Arch + inner * 0.4;
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(new Point(x, bottom), true);
                    path.LineTo(new Point(x, shoulder));
                    // L'arche : une anse de panier, du flanc gauche au flanc droit.
                    path.CubicBezierTo(new Point(x, top + (shoulder - top) * 0.28), new Point(w * 0.24, top), new Point(w / 2, top));
                    path.CubicBezierTo(new Point(w * 0.76, top), new Point(right, top + (shoulder - top) * 0.28), new Point(right, shoulder));
                    path.LineTo(new Point(right, bottom));
                    path.EndFigure(true);
                }
                return geometry;
            }

            public override void Render(DrawingContext context)
            {
                var size = Bounds.Size;
                if (size.Width < 200 || size.Height < 200) return;
                var outline = Outline(size, 0);
                var sky = new RadialGradientBrush { Center = new RelativePoint(0.5, 0.0, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.0, RelativeUnit.Relative), RadiusX = new RelativeScalar(1.0, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.0, RelativeUnit.Relative) };
                sky.GradientStops.Add(new GradientStop(Color.Parse("#163250"), 0));
                sky.GradientStops.Add(new GradientStop(Night, 0.45));
                sky.GradientStops.Add(new GradientStop(Deep, 1));
                context.DrawGeometry(sky, null, outline);
                using (context.PushGeometryClip(outline))
                {
                    var random = new Random(1126);
                    for (var i = 0; i < 130; i++)
                    {
                        double x = random.NextDouble() * size.Width, y = random.NextDouble() * size.Height, m = random.NextDouble();
                        var alpha = (byte)(255 * (0.10 + 0.45 * m * m));
                        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, Pale.R, Pale.G, Pale.B)), null, new Point(x, y), 0.5 + m, 0.5 + m);
                    }
                }
                var gilt = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
                gilt.GradientStops.Add(new GradientStop(Bronze, 0));
                gilt.GradientStops.Add(new GradientStop(Gold, 0.3));
                gilt.GradientStops.Add(new GradientStop(Color.Parse("#FFF6DC"), 0.46));
                gilt.GradientStops.Add(new GradientStop(Gold, 0.62));
                gilt.GradientStops.Add(new GradientStop(Bronze, 1));
                context.DrawGeometry(null, new Pen(gilt, 1.6), outline);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x55, Gold.R, Gold.G, Gold.B)), 1), Outline(size, 6));
                // Le cimier, au sommet de l'arche ; deux losanges au pied.
                var top = new Point(Math.Round(size.Width / 2), 0.6);
                AscensionArt.Diamond(context, new Point(top.X, top.Y + 7), 7, new SolidColorBrush(Night), new Pen(new SolidColorBrush(Gold), 1.2));
                AscensionArt.Diamond(context, new Point(top.X, top.Y + 7), 2.8, new SolidColorBrush(Pale), null);
                foreach (var x in new[] { 0.6, size.Width - 0.6 })
                    AscensionArt.Diamond(context, new Point(x < 5 ? x + 5 : x - 5, size.Height - 5.6), 4, new SolidColorBrush(Gold), null);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LES SKINS de l'application (2.0.0, 21/11/2026, Rémi) — Marabook
    /// modern, Marabook classic, Gradient Garden, Eden Scriptis, Cyberpunk 2026 ;
    /// et, depuis le 24/11, la série PREMIUM « Museum » (Skin.Museum.cs).
    /// Un skin, c'est quatre choses, toutes posées ici :
    ///  1. la PALETTE : Chrome.Toggle bâtit celle du thème clair ou sombre, puis
    ///     TunePalette la retouche (les pinceaux restent ceux de Chrome, mutables
    ///     et OPAQUES — des fenêtres s'en servent comme fond) ;
    ///  2. le FOND : un dégradé ou une texture, peint UNE fois par la fenêtre
    ///     principale. Les vues de la zone centrale et les barres ne tiennent pas
    ///     leur fond d'un pinceau mais d'une RESSOURCE (Ground, Bar, Backdrop) :
    ///     sous un skin à fond, elles deviennent transparentes ou translucides
    ///     et le laissent voir, sans couture d'une vue à l'autre ;
    ///  3. la FORME : le rayon des coins (Corner), la police de l'interface, et
    ///     des styles par-dessus ceux du thème (boutons en relief, dégradés,
    ///     halos de néon) — Theme.Build les demande ; le thème est rebâti quand
    ///     le skin, le mode ou l'accent changent ;
    ///  4. l'HABILLAGE de ce qui est fabriqué à la main : les coins des cadres
    ///     (Dress, à l'arrivée de chaque Border dans l'arbre) et le fond des
    ///     fenêtres de dialogue (DressWindow, à leur ouverture).
    /// « modern » ne fait RIEN de tout cela : aucun pinceau, aucun style, aucun
    /// coin n'est touché — l'application est celle d'avant.
    /// Un skin ne touche jamais aux pages composées (noir sur blanc), ni à un
    /// export de texte : seuls l'Encyclopédie en HTML et le « PDF
    /// Encyclopédique » le suivent (Model.Skins).</summary>
    internal static partial class Skin // la série « Museum » : Skin.Museum.cs (24/11)
    {
        public const string GroundKey = "MbGround", BarKey = "MbBar", BackdropKey = "MbBackdrop";

        public static string Current { get { return Skins.Normalize(AppSettings.Skin); } }
        public static bool Plain { get { return Current == Skins.Modern; } }

        /// <summary>La seconde couleur (Gradient Garden, les néons de Cyberpunk) : choisie, sinon automatique.</summary>
        public static Color Second
        {
            get
            {
                var accent = Chrome.Hex(Chrome.ParseAccent() ?? Color.Parse(Chrome.DefaultAccent));
                return Color.Parse(Skins.SecondFor(accent, AppSettings.SkinAccent2));
            }
        }

        // ================================================================ 1. la palette

        /// <summary>Retouche la palette que Chrome.Toggle vient de bâtir.</summary>
        public static void TunePalette(bool dark, double hue)
        {
            var id = Current;
            if (id == Skins.Modern) return;
            var whitePaper = dark && AppSettings.WhitePaperInDark;
            if (id == Skins.Classic)
            {
                if (dark) Surfaces(0x1E1E1E, 0x3C3B37, 0x45443F, 0x4A4944, 0x2B2B2B, 0x3C3B37, 0x6B6A63, 0x8A8980, 0xF0EFE8, 0xB5B3A8, 0x86847A);
                else Surfaces(0x808080, 0xECE9D8, 0xF3F1E6, 0xF7F6EE, 0xFFFFFF, 0xFFFFFF, 0xACA899, 0x716F64, 0x000000, 0x444444, 0x7A786C);
            }
            else if (id == Skins.Gradient)
            {
                if (dark)
                    Surfaces(Chrome.Tint(hue, 0.45, 0.10), Chrome.Tint(hue, 0.35, 0.14), Chrome.Tint(hue, 0.30, 0.17), Chrome.Tint(hue, 0.30, 0.19), Chrome.Tint(hue, 0.28, 0.21),
                        Chrome.Tint(hue, 0.35, 0.14), Chrome.Tint(hue, 0.25, 0.27), Chrome.Tint(hue, 0.25, 0.37), Chrome.Ink.Color, Chrome.SoftText.Color, Chrome.FaintText.Color);
                else
                    Surfaces(Chrome.Tint(hue, 0.55, 0.91), Chrome.Tint(hue, 0.45, 0.94), Chrome.Tint(hue, 0.50, 0.965), Chrome.Tint(hue, 0.60, 0.985), Color.FromRgb(0xFF, 0xFF, 0xFF),
                        Color.FromRgb(0xFF, 0xFF, 0xFF), Chrome.Tint(hue, 0.35, 0.85), Chrome.Tint(hue, 0.30, 0.75), Chrome.Ink.Color, Chrome.SoftText.Color, Chrome.FaintText.Color);
            }
            else if (id == Skins.Eden)
            {
                if (dark) Surfaces(0x1C150D, 0x261D12, 0x2F2416, 0x33281A, 0x3A2E1E, 0x261D12, 0x4D3E28, 0x6A573A, 0xEADBC0, 0xB9A483, 0x8C7A5E);
                else Surfaces(0xD9C7A0, 0xE4D4AE, 0xEFE2C2, 0xF4E9CD, 0xFAF3E0, 0xFAF3E0, 0xC9B38C, 0xA98F63, 0x3B2A1A, 0x6B5440, 0x9A8266);
            }
            else if (id == Skins.Cyberpunk)
            {
                if (dark)
                {
                    Surfaces(Rgb(0x07060D), Rgb(0x0C0A16), Rgb(0x110E1F), Rgb(0x141126), Rgb(0x181430), Rgb(0x0C0A16),
                        Chrome.Tint(hue, 0.85, 0.36), Chrome.Tint(hue, 1.0, 0.52), Rgb(0xECE9FF), Rgb(0xA6A2C6), Rgb(0x736F9A));
                    // La seconde couleur : les liens vers la temporalité s'allument d'un autre néon.
                    var hue2 = Chrome.HueOf(Second);
                    Chrome.Time.Color = Chrome.Tint(hue2, 1.0, 0.68);
                    Chrome.TimeTint.Color = Chrome.Tint(hue2, 0.70, 0.17);
                    // Les néons : l'accent poussé à pleine saturation.
                    Chrome.Accent.Color = Chrome.Tint(hue, 1.0, 0.64);
                    Chrome.AccentTint.Color = Chrome.Tint(hue, 0.70, 0.16);
                    Chrome.AccentSoft.Color = Chrome.Tint(hue, 0.75, 0.30);
                    Chrome.AccentStrong.Color = Chrome.Tint(hue, 1.0, 0.74);
                }
                else
                    Surfaces(Rgb(0xDCD9EC), Rgb(0xE6E4F2), Rgb(0xF0EEF9), Rgb(0xF6F5FC), Rgb(0xFFFFFF), Rgb(0xFFFFFF),
                        Chrome.Tint(hue, 0.60, 0.78), Chrome.Tint(hue, 0.70, 0.62), Rgb(0x16122B), Rgb(0x5A5578), Rgb(0x8C88A8));
            }
            else if (Skins.IsMuseum(id)) TuneMuseum(id, dark, hue);
            // Le papier des fiches : blanc et encre sombre si l'auteur le garde blanc au sombre ;
            // sinon celui du skin (le fond des champs, un cran plus clair que les panneaux).
            if (whitePaper)
            {
                Chrome.PaperBg.Color = Rgb(0xFFFFFF);
                Chrome.PaperInk.Color = Rgb(0x1E2128);
                Chrome.PaperSoftInk.Color = Rgb(0x5A6172);
            }
            else
            {
                Chrome.PaperBg.Color = dark ? Chrome.RaisedBg.Color : id == Skins.Eden ? Rgb(0xFBF5E4) : Rgb(0xFFFFFF);
                Chrome.PaperInk.Color = Chrome.Ink.Color;
                Chrome.PaperSoftInk.Color = Chrome.SoftText.Color;
            }
        }

        private static Color Rgb(int rgb) { return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb); }

        private static void Surfaces(int window, int bar, int raised, int card, int field, int canvas, int line, int lineStrong, int ink, int soft, int faint)
        {
            Surfaces(Rgb(window), Rgb(bar), Rgb(raised), Rgb(card), Rgb(field), Rgb(canvas), Rgb(line), Rgb(lineStrong), Rgb(ink), Rgb(soft), Rgb(faint));
        }

        private static void Surfaces(Color window, Color bar, Color raised, Color card, Color field, Color canvas, Color line, Color lineStrong, Color ink, Color soft, Color faint)
        {
            Chrome.WindowBg.Color = window;
            Chrome.BarBg.Color = bar;
            Chrome.BarBgLight.Color = raised;
            Chrome.RaisedBg.Color = raised;
            Chrome.CardBg.Color = card;
            Chrome.FieldBg.Color = field;
            Chrome.CanvasBg.Color = canvas;
            Chrome.Border.Color = line;
            Chrome.BorderStrong.Color = lineStrong;
            Chrome.Ink.Color = ink;
            Chrome.SoftText.Color = soft;
            Chrome.FaintText.Color = faint;
        }

        // ================================================================ 2. le fond

        private static Bitmap _texture, _textureDark;
        private static bool _textureTried;

        /// <summary>Le fichier du vieux papier (celui de l'accueil), à côté de l'exécutable ; null s'il manque.</summary>
        public static string TexturePath
        {
            get
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, System.IO.Path.Combine("assets", "background.jpg"));
                return File.Exists(path) ? path : null;
            }
        }

        private static Bitmap Texture(bool dark)
        {
            if (!_textureTried)
            {
                try
                {
                    var path = TexturePath;
                    if (path != null) _texture = new Bitmap(path);
                    if (_texture != null)
                    {
                        // Le papier de nuit : le même, sous un voile brun sombre.
                        var size = _texture.PixelSize;
                        var target = new RenderTargetBitmap(size, new Vector(96, 96));
                        using (var context = target.CreateDrawingContext())
                        {
                            context.DrawImage(_texture, new Rect(0, 0, size.Width, size.Height));
                            context.FillRectangle(new SolidColorBrush(Color.FromArgb(0xD2, 0x18, 0x11, 0x09)), new Rect(0, 0, size.Width, size.Height));
                        }
                        _textureDark = target;
                    }
                    _textureTried = true; // lue (ou absente) : on n'y revient pas
                }
                // Le moteur de rendu pas encore prêt (tout début du lancement) : on réessaiera.
                catch (Exception) { _texture = null; _textureDark = null; }
            }
            return dark ? _textureDark ?? _texture : _texture;
        }

        /// <summary>Le fond du skin (dégradé, texture), ou null : un aplat.</summary>
        public static IBrush Backdrop()
        {
            var id = Current;
            var dark = Chrome.Dark;
            var hue = Chrome.HueOf(Chrome.ParseAccent() ?? Color.Parse(Chrome.DefaultAccent));
            var hue2 = Chrome.HueOf(Second);
            if (Skins.IsMuseum(id)) return MuseumBackdrop(dark);
            if (id == Skins.Gradient)
                return Linear(dark ? Chrome.Tint(hue, 0.55, 0.11) : Chrome.Tint(hue, 0.80, 0.91), dark ? Chrome.Tint(hue2, 0.55, 0.18) : Chrome.Tint(hue2, 0.80, 0.88), true);
            if (id == Skins.Eden)
            {
                var texture = Texture(dark);
                if (texture == null) return null;
                return new ImageBrush(texture) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
            }
            if (id == Skins.Cyberpunk && dark)
            {
                var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
                brush.GradientStops.Add(new GradientStop(Chrome.Tint(hue, 0.85, 0.09), 0));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x07, 0x06, 0x0D), 0.45));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x07, 0x06, 0x0D), 0.60));
                brush.GradientStops.Add(new GradientStop(Chrome.Tint(hue2, 0.85, 0.10), 1));
                return brush;
            }
            return null;
        }

        /// <summary>Le voile des barres posées sur le fond : translucide, il le laisse deviner.</summary>
        private static IBrush BarVeil()
        {
            var id = Current;
            var dark = Chrome.Dark;
            var bar = Chrome.BarBg.Color;
            if (Skins.IsMuseum(id)) return MuseumVeil(bar);
            if (id == Skins.Gradient) return new SolidColorBrush(dark ? Color.FromArgb(0x70, 0x08, 0x08, 0x10) : Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
            if (id == Skins.Eden) return new SolidColorBrush(Color.FromArgb(dark ? (byte)0x96 : (byte)0x8A, bar.R, bar.G, bar.B));
            return new SolidColorBrush(Color.FromArgb(0xB4, bar.R, bar.G, bar.B));
        }

        public static IBrush Linear(Color from, Color to, bool diagonal)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = diagonal ? new RelativePoint(1, 1, RelativeUnit.Relative) : new RelativePoint(0, 1, RelativeUnit.Relative)
            };
            brush.GradientStops.Add(new GradientStop(from, 0));
            brush.GradientStops.Add(new GradientStop(to, 1));
            return brush;
        }

        /// <summary>Le fond d'une vue de la zone centrale : celui de la fenêtre (Chrome.WindowBg), ou
        /// transparent quand le skin peint un fond derrière tout.</summary>
        public static void Ground(Control control) { BindBackground(control, GroundKey); }
        /// <summary>Le fond d'une barre du chrome : Chrome.BarBg, ou un voile sur le fond du skin.</summary>
        public static void Bar(Control control) { BindBackground(control, BarKey); }
        /// <summary>Le fond de la fenêtre principale : Chrome.WindowBg, ou le fond du skin.</summary>
        public static void BackdropOn(Control control) { BindBackground(control, BackdropKey); }

        /// <summary>Pour un initialiseur : new Border { [!Border.BackgroundProperty] = Skin.BarBinding }.</summary>
        public static IBinding BarBinding { get { return new DynamicResourceExtension(BarKey); } }
        public static IBinding GroundBinding { get { return new DynamicResourceExtension(GroundKey); } }

        private static void BindBackground(Control control, string key)
        {
            AvaloniaProperty property =
                control is Panel ? Panel.BackgroundProperty
                : control is Border ? Border.BackgroundProperty
                : control is TemplatedControl ? TemplatedControl.BackgroundProperty
                : control is ContentPresenter ? (AvaloniaProperty)ContentPresenter.BackgroundProperty : null;
            if (property == null) return;
            control.Bind(property, new DynamicResourceExtension(key));
        }

        private static void PublishResources()
        {
            var app = Application.Current;
            if (app == null) return;
            var backdrop = Backdrop();
            app.Resources[BackdropKey] = backdrop ?? Chrome.WindowBg;
            app.Resources[GroundKey] = backdrop == null ? (IBrush)Chrome.WindowBg : Brushes.Transparent;
            app.Resources[BarKey] = backdrop == null ? (IBrush)Chrome.BarBg : BarVeil();
            // Les fenêtres de dialogue : leurs coins, et leur verre sous un skin « Museum » (24/11).
            ModalChrome.Publish(app, Round(12), Museum ? MuseumDialogGlass : 1.0, Museum ? Skins.MuseumBlurFor(Current, AppSettings.SkinBlur) : 0, !Museum ? 0 : Skins.MuseumPill(Current) ? 2 : 1);
        }

        // ================================================================ 3. la forme

        /// <summary>Le rayon d'un coin sous ce skin : droit en classic, à peine cassé
        /// en néon et en vieux livre, plus rond en dégradé.</summary>
        public static double Round(double radius)
        {
            if (radius <= 0) return radius;
            return SkinRound(radius) * CornerScale;
        }

        /// <summary>« ARRONDI DES ANGLES » (Préférences › Personnalisation, 24/11/2026, Rémi) : chaque
        /// surface garde SON rayon (celui du dessin, retouché par le skin) — et tous sont multipliés
        /// par cette valeur. 1 : le dessin d'origine ; 0 : plus aucun arrondi ; 2 : le double.
        /// Les ronds (pastilles, points d'état) restent ronds (Dress les reconnaît).</summary>
        public static double CornerScale
        {
            get
            {
                var scale = AppSettings.CornerScale;
                return double.IsNaN(scale) ? 1 : Math.Max(0, Math.Min(2, scale));
            }
        }

        /// <summary>Vrai quand RIEN n'est à habiller : « modern », à l'arrondi d'origine.</summary>
        private static bool Bare { get { return Plain && Math.Abs(CornerScale - 1) < 0.001; } }

        private static double SkinRound(double radius)
        {
            var id = Current;
            if (id == Skins.Modern) return radius;
            if (id == Skins.Classic) return 0;
            if (id == Skins.Cyberpunk) return Math.Min(radius, 2);
            if (id == Skins.Eden) return Math.Min(radius, 3);
            if (Skins.IsMuseum(id)) return MuseumRound(id, radius);
            return Math.Min(radius * 1.6, 18);
        }

        public static CornerRadius Corner(double radius) { return new CornerRadius(Round(radius)); }

        /// <summary>La police de l'interface, ou null : celle de Marabook.</summary>
        public static FontFamily UiFont
        {
            get
            {
                var id = Current;
                if (id == Skins.Classic) return new FontFamily("Tahoma, Verdana, Geneva, DejaVu Sans");
                if (id == Skins.Eden) return new FontFamily("Palatino Linotype, Book Antiqua, Palatino, Georgia, DejaVu Serif");
                if (id == Skins.Cyberpunk) return new FontFamily("Bahnschrift, DIN Alternate, Segoe UI, Helvetica Neue, DejaVu Sans");
                if (Skins.IsMuseum(id)) return MuseumUiFont(id);
                return null;
            }
        }

        private static Style Rule(Func<Selector, Selector> selector, params Setter[] setters)
        {
            var style = new Style(selector);
            foreach (var setter in setters) style.Setters.Add(setter);
            return style;
        }

        private static BoxShadows Glow(Color color, double blur, byte alpha)
        {
            return new BoxShadows(new BoxShadow { Blur = blur, Spread = 0, OffsetX = 0, OffsetY = 0, Color = Color.FromArgb(alpha, color.R, color.G, color.B) });
        }

        /// <summary>Les styles du skin, par-dessus ceux du thème (Theme.Build les ajoute en dernier).</summary>
        public static void AddStyles(Styles styles)
        {
            var id = Current;
            if (id == Skins.Modern) return;
            var dark = Chrome.Dark;
            var font = UiFont;
            if (font != null)
            {
                styles.Add(Rule(x => x.Is<Window>(), new Setter(TemplatedControl.FontFamilyProperty, font)));
                styles.Add(Rule(x => x.Is<PopupRoot>(), new Setter(TemplatedControl.FontFamilyProperty, font)));
            }
            var accent = Chrome.Accent.Color;
            var second = Second;
            var hasBackdrop = Backdrop() != null;
            if (hasBackdrop)
                // La barre des menus laisse voir la barre qui la porte.
                styles.Add(Rule(x => x.OfType<Menu>(), new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent)));

            if (id == Skins.Classic)
            {
                // Le bouton en relief : un dégradé clair → beige, un filet sombre ; orangé sous le pointeur (le « Luna » de 2005).
                var face = Linear(dark ? Color.FromRgb(0x62, 0x61, 0x5A) : Color.FromRgb(0xFF, 0xFF, 0xFF), dark ? Color.FromRgb(0x3F, 0x3E, 0x39) : Color.FromRgb(0xD6, 0xD0, 0xC0), false);
                var hot = Linear(dark ? Color.FromRgb(0x7A, 0x6A, 0x45) : Color.FromRgb(0xFF, 0xF8, 0xDF), dark ? Color.FromRgb(0x5A, 0x48, 0x22) : Color.FromRgb(0xF6, 0xCF, 0x74), false);
                var down = Linear(dark ? Color.FromRgb(0x3A, 0x39, 0x34) : Color.FromRgb(0xC9, 0xC3, 0xB3), dark ? Color.FromRgb(0x55, 0x54, 0x4E) : Color.FromRgb(0xEE, 0xEA, 0xDC), false);
                var hotLine = new SolidColorBrush(Color.FromRgb(0xE5, 0x9A, 0x16));
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Not(y => y.Class(Theme.Primary)),
                    new Setter(TemplatedControl.BackgroundProperty, face), new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3))));
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Not(y => y.Class(Theme.Primary)).Class(":pointerover"),
                    new Setter(TemplatedControl.BackgroundProperty, hot), new Setter(TemplatedControl.BorderBrushProperty, hotLine)));
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Not(y => y.Class(Theme.Primary)).Class(":pressed"),
                    new Setter(TemplatedControl.BackgroundProperty, down)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Calm).Class(":pointerover"),
                    new Setter(TemplatedControl.BackgroundProperty, hot), new Setter(TemplatedControl.BorderBrushProperty, hotLine)));
                var blue = Linear(Chrome.Blend(accent, Colors.White, 0.35), Chrome.Blend(accent, Colors.Black, 0.25), false);
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary),
                    new Setter(TemplatedControl.BackgroundProperty, blue), new Setter(TemplatedControl.ForegroundProperty, Brushes.White), new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3))));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Class(":pointerover"), new Setter(TemplatedControl.BackgroundProperty, blue), new Setter(TemplatedControl.BorderBrushProperty, hotLine)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Not(y => y.Class(Theme.Calm)),
                    new Setter(TemplatedControl.BackgroundProperty, face), new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(3))));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":pointerover"),
                    new Setter(TemplatedControl.BackgroundProperty, hot), new Setter(TemplatedControl.BorderBrushProperty, hotLine)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":checked"),
                    new Setter(TemplatedControl.BackgroundProperty, down), new Setter(TemplatedControl.BorderBrushProperty, Chrome.BorderStrong), new Setter(TemplatedControl.ForegroundProperty, Chrome.Ink)));
                // L'onglet choisi : un bandeau bleu en dégradé, texte blanc.
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":selected"), new Setter(TemplatedControl.BackgroundProperty, blue)));
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":selected").Template().OfType<Border>().Name("Bg"), new Setter(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Brushes.White)));
                // La barre de défilement : une piste visible, un pouce en relief.
                styles.Add(Rule(x => x.OfType<ScrollBar>(), new Setter(TemplatedControl.BackgroundProperty, Chrome.BarBg)));
                styles.Add(Rule(x => x.OfType<ScrollBar>().Template().OfType<Thumb>(), new Setter(TemplatedControl.BackgroundProperty, Chrome.BorderStrong)));
                // L'infobulle jaune pâle de l'époque.
                styles.Add(Rule(x => x.OfType<ToolTip>(),
                    new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xE1))),
                    new Setter(TemplatedControl.ForegroundProperty, Brushes.Black)));
                styles.Add(Rule(x => x.OfType<Menu>(), new Setter(TemplatedControl.BackgroundProperty, Chrome.BarBg)));
            }
            else if (id == Skins.Gradient)
            {
                var main = Linear(accent, dark ? Chrome.Tint(Chrome.HueOf(second), 0.73, 0.70) : second, true);
                var soft = Linear(Chrome.AccentTint.Color, Chrome.Blend(Chrome.AccentTint.Color, second, dark ? 0.22 : 0.16), true);
                var softer = Linear(Chrome.RaisedBg.Color, Chrome.Blend(Chrome.RaisedBg.Color, second, dark ? 0.10 : 0.07), true);
                var onMain = dark ? (IBrush)new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x18)) : Brushes.White;
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Not(y => y.Class(Theme.Primary)), new Setter(TemplatedControl.BackgroundProperty, softer)));
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Primary)).Class(":pointerover"), new Setter(TemplatedControl.BackgroundProperty, soft)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary),
                    new Setter(TemplatedControl.BackgroundProperty, main), new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent), new Setter(TemplatedControl.ForegroundProperty, onMain)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Class(":pointerover"),
                    new Setter(TemplatedControl.BackgroundProperty, main), new Setter(TemplatedControl.BorderBrushProperty, Chrome.AccentStrong)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":pointerover"), new Setter(TemplatedControl.BackgroundProperty, soft)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":checked"), new Setter(TemplatedControl.BackgroundProperty, soft)));
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":pointerover"), new Setter(TemplatedControl.BackgroundProperty, soft)));
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":selected"),
                    new Setter(TemplatedControl.BackgroundProperty, main), new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent)));
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":selected").Template().OfType<Border>().Name("Bg"), new Setter(Avalonia.Controls.Documents.TextElement.ForegroundProperty, onMain)));
                styles.Add(Rule(x => x.OfType<CheckBox>().Class(":checked").Template().OfType<Border>().Name("Box"),
                    new Setter(Border.BackgroundProperty, main), new Setter(Border.BorderBrushProperty, Brushes.Transparent)));
                styles.Add(Rule(x => x.OfType<RadioButton>().Class(":checked").Template().OfType<Border>().Name("Box"),
                    new Setter(Border.BackgroundProperty, main), new Setter(Border.BorderBrushProperty, Brushes.Transparent)));
                styles.Add(Rule(x => x.OfType<ScrollBar>().Template().OfType<Thumb>(), new Setter(TemplatedControl.BackgroundProperty, Linear(Chrome.AccentSoft.Color, Chrome.Blend(Chrome.AccentSoft.Color, second, 0.5), true))));
                styles.Add(Rule(x => x.OfType<ScrollBar>().Template().OfType<Thumb>().Class(":pointerover"), new Setter(TemplatedControl.BackgroundProperty, main)));
                styles.Add(Rule(x => x.OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"), new Setter(ContentPresenter.BackgroundProperty, soft)));
                styles.Add(Rule(x => x.OfType<TreeViewItem>().Class(":selected").Template().OfType<Border>().Name("SelectionBorder"),
                    new Setter(Border.BackgroundProperty, Linear(Chrome.AccentSoft.Color, Chrome.Blend(Chrome.AccentSoft.Color, second, 0.35), true))));
                styles.Add(Rule(x => x.OfType<ToolTip>(), new Setter(TemplatedControl.BackgroundProperty, Linear(Chrome.Blend(accent, Colors.Black, 0.35), Chrome.Blend(second, Colors.Black, 0.35), true))));
            }
            else if (id == Skins.Eden)
            {
                var face = Linear(dark ? Color.FromRgb(0x45, 0x37, 0x24) : Color.FromRgb(0xF8, 0xEE, 0xD4), dark ? Color.FromRgb(0x33, 0x28, 0x1A) : Color.FromRgb(0xE6, 0xD5, 0xAC), false);
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Not(y => y.Class(Theme.Primary)), new Setter(TemplatedControl.BackgroundProperty, face)));
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Not(y => y.Class(Theme.Primary)).Class(":pointerover"),
                    new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint), new Setter(TemplatedControl.BorderBrushProperty, Chrome.Ink)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Not(y => y.Class(Theme.Calm)), new Setter(TemplatedControl.BackgroundProperty, face)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":checked"), new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint)));
                styles.Add(Rule(x => x.OfType<ToolTip>(),
                    new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xF0, 0x3B, 0x2A, 0x1A))),
                    new Setter(TemplatedControl.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xF6, 0xEB, 0xD0)))));
                styles.Add(Rule(x => x.OfType<TabItem>().Template().OfType<Border>().Name("Bg"), new Setter(Avalonia.Controls.Documents.TextElement.FontStyleProperty, FontStyle.Italic)));
            }
            else if (id == Skins.Cyberpunk)
            {
                var neon = Chrome.Accent.Color;
                var glow = Glow(neon, 9, dark ? (byte)0xB0 : (byte)0x70);
                var glowStrong = Glow(neon, 14, dark ? (byte)0xE0 : (byte)0x90);
                var glowSecond = Glow(second, 10, dark ? (byte)0xB0 : (byte)0x70);
                var none = new BoxShadows();
                var onNeon = dark ? (IBrush)new SolidColorBrush(Color.FromRgb(0x07, 0x06, 0x0D)) : Brushes.White;
                // Tout bouton : un tube de néon — filet d'accent, halo sous le pointeur.
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Not(y => y.Class(Theme.Primary)),
                    new Setter(TemplatedControl.BackgroundProperty, Chrome.FieldBg), new Setter(TemplatedControl.BorderBrushProperty, Chrome.AccentSoft)));
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Primary)).Class(":pointerover"),
                    new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint), new Setter(TemplatedControl.BorderBrushProperty, Chrome.Accent), new Setter(TemplatedControl.ForegroundProperty, Chrome.AccentStrong)));
                styles.Add(Rule(x => x.OfType<Button>().Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, none)));
                styles.Add(Rule(x => x.OfType<Button>().Not(y => y.Class(Theme.Calm)).Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, Glow(neon, 7, dark ? (byte)0x58 : (byte)0x38))));
                styles.Add(Rule(x => x.OfType<Button>().Class(":pointerover").Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, glowStrong)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary),
                    new Setter(TemplatedControl.BackgroundProperty, Chrome.Accent), new Setter(TemplatedControl.BorderBrushProperty, Chrome.AccentStrong), new Setter(TemplatedControl.ForegroundProperty, onNeon)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, glow)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Class(":pointerover").Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, glowStrong)));
                styles.Add(Rule(x => x.OfType<Button>().Class(Theme.Primary).Class(":pointerover"),
                    new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentStrong), new Setter(TemplatedControl.ForegroundProperty, onNeon)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":checked"),
                    new Setter(TemplatedControl.BorderBrushProperty, Chrome.Accent)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, none)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":checked").Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, glow)));
                styles.Add(Rule(x => x.OfType<ToggleButton>().Class(Theme.Owned).Class(":pointerover").Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, glow)));
                // Les champs s'allument au clavier.
                styles.Add(Rule(x => x.OfType<TextBox>().Template().OfType<Border>().Name("border"), new Setter(Border.BoxShadowProperty, none)));
                styles.Add(Rule(x => x.OfType<TextBox>().Class(":focus").Template().OfType<Border>().Name("border"), new Setter(Border.BoxShadowProperty, glow)));
                styles.Add(Rule(x => x.OfType<ComboBox>().Template().OfType<Border>().Name("border"), new Setter(Border.BoxShadowProperty, none)));
                styles.Add(Rule(x => x.OfType<ComboBox>().Class(":focus").Template().OfType<Border>().Name("border"), new Setter(Border.BoxShadowProperty, glow)));
                // L'onglet choisi : un cartouche allumé de la seconde couleur.
                styles.Add(Rule(x => x.OfType<TabItem>().Template().OfType<Border>().Name("Bg"), new Setter(Border.BoxShadowProperty, none)));
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":selected"),
                    new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentTint), new Setter(TemplatedControl.BorderBrushProperty, Chrome.Accent)));
                styles.Add(Rule(x => x.OfType<TabItem>().Class(":selected").Template().OfType<Border>().Name("Bg"),
                    new Setter(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Chrome.AccentStrong), new Setter(Border.BoxShadowProperty, glow)));
                styles.Add(Rule(x => x.OfType<CheckBox>().Class(":checked").Template().OfType<Border>().Name("Box"), new Setter(Border.BoxShadowProperty, glow)));
                styles.Add(Rule(x => x.OfType<RadioButton>().Class(":checked").Template().OfType<Border>().Name("Box"), new Setter(Border.BoxShadowProperty, glow)));
                styles.Add(Rule(x => x.OfType<ScrollBar>().Template().OfType<Thumb>(), new Setter(TemplatedControl.BackgroundProperty, Chrome.AccentSoft)));
                styles.Add(Rule(x => x.OfType<ScrollBar>().Template().OfType<Thumb>().Class(":pointerover"), new Setter(TemplatedControl.BackgroundProperty, Chrome.Accent)));
                styles.Add(Rule(x => x.OfType<ToolTip>(),
                    new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0xF2, 0x0F, 0x0C, 0x1C))),
                    new Setter(TemplatedControl.ForegroundProperty, new SolidColorBrush(Chrome.Tint(Chrome.HueOf(second), 1.0, 0.72)))));
                styles.Add(Rule(x => x.OfType<TreeViewItem>().Class(":selected").Template().OfType<Border>().Name("SelectionBorder"),
                    new Setter(Border.BorderBrushProperty, Chrome.Accent), new Setter(Border.BorderThicknessProperty, new Thickness(2, 0, 0, 0))));
                styles.Add(Rule(x => x.OfType<Separator>(), new Setter(TemplatedControl.BackgroundProperty, Chrome.Accent), new Setter(Visual.OpacityProperty, 0.6)));
                styles.Add(Rule(x => x.OfType<ContextMenu>(), new Setter(TemplatedControl.BorderBrushProperty, new SolidColorBrush(second))));
                // La ligne choisie d'une liste : le second néon.
                styles.Add(Rule(x => x.OfType<ListBoxItem>().Class(":selected"), new Setter(TemplatedControl.ForegroundProperty, new SolidColorBrush(Chrome.Tint(Chrome.HueOf(second), 1.0, dark ? 0.72 : 0.40)))));
                styles.Add(Rule(x => x.OfType<MenuItem>().Class(":selected").Template().OfType<Border>().Name("root"), new Setter(Border.BoxShadowProperty, glowSecond)));
            }
            else if (Skins.IsMuseum(id)) AddMuseumStyles(styles);
        }

        // ================================================================ 4. l'habillage

        private static readonly AttachedProperty<object> OriginalCornersProperty =
            AvaloniaProperty.RegisterAttached<Border, Border, object>("SkinOriginalCorners");
        private static readonly AttachedProperty<object> OriginalWindowBackgroundProperty =
            AvaloniaProperty.RegisterAttached<Window, Window, object>("SkinOriginalBackground");
        private static readonly AttachedProperty<object> OriginalShadowProperty =
            AvaloniaProperty.RegisterAttached<Border, Border, object>("SkinOriginalShadow");
        private static readonly object NoBrush = new object();

        private static bool _installed, _bare = true;
        private static string _signature;
        private static readonly AttachedProperty<bool> KeepCornersProperty =
            AvaloniaProperty.RegisterAttached<Border, Border, bool>("SkinKeepCorners");

        /// <summary>Ce cadre règle ses coins lui-même (il suit un autre cadre) : l'habillage n'y touche pas.</summary>
        public static void KeepCorners(Border border) { if (border != null) border.SetValue(KeepCornersProperty, true); }
        private static int _themeIndex = -1;

        /// <summary>Au démarrage, après le thème : les ressources de fond, et les deux
        /// guetteurs (chaque cadre qui entre dans l'arbre, chaque fenêtre qui s'ouvre).</summary>
        public static void Install(Application app, int themeIndex)
        {
            _themeIndex = themeIndex;
            PublishResources();
            _signature = Signature();
            _bare = Bare;
            if (_installed) return;
            _installed = true;
            Control.LoadedEvent.AddClassHandler<Border>(delegate(Border border, RoutedEventArgs e) { if (!Bare) Dress(border); });
            Window.WindowOpenedEvent.AddClassHandler<Window>(delegate(Window window, RoutedEventArgs e)
            {
                if (Plain) return;
                // La fenêtre principale s'ouvre : le fond est republié — au tout début du
                // lancement, le moteur de rendu n'avait peut-être pas encore pu lire la texture.
                if (window is MainWindow) PublishResources();
                DressWindow(window);
            });
        }

        private static string Signature()
        {
            return Current + "|" + (Chrome.Dark ? "1" : "0") + "|" + (AppSettings.AccentColor ?? "") + "|" + (AppSettings.SkinAccent2 ?? "") + "|" + (AppSettings.WhitePaperInDark ? "1" : "0")
                + "|" + (AppSettings.SkinBackground ?? "") + "|" + AppSettings.SkinGlass + "|" + AppSettings.SkinBlur + "|" + CornerScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Après un changement d'apparence (Chrome.Toggle est déjà passé) :
        /// le fond, le thème rebâti si le skin en a besoin, et l'habillage de tout
        /// ce qui est déjà à l'écran.</summary>
        public static void Apply()
        {
            var app = Application.Current;
            if (app == null) return;
            var signature = Signature();
            var was = _signature;
            if (signature == was) return;
            _signature = signature;
            PublishResources();
            // « modern » d'un bout à l'autre : les pinceaux mutables suffisent, comme avant.
            var wasBare = _bare;
            _bare = Bare;
            if (_bare && wasBare) return;
            if (_themeIndex >= 0 && _themeIndex < app.Styles.Count) app.Styles[_themeIndex] = Theme.Build();
            var desktop = app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop == null) return;
            foreach (var window in new List<Window>(desktop.Windows))
            {
                DressWindow(window);
                foreach (var visual in window.GetVisualDescendants())
                {
                    var border = visual as Border;
                    if (border != null) Dress(border);
                }
            }
        }

        /// <summary>Rebâtit les styles du thème tels qu'ils doivent être maintenant (22/11 : un réglage
        /// d'accessibilité — police, focus — a changé sans que le skin bouge).</summary>
        internal static void RebuildTheme()
        {
            var app = Application.Current;
            if (app == null || _themeIndex < 0 || _themeIndex >= app.Styles.Count) return;
            app.Styles[_themeIndex] = Theme.Build();
        }

        /// <summary>Les coins d'un cadre fabriqué à la main suivent le skin. Un rond
        /// (pastille, point d'état) reste rond ; un cadre de gabarit suit son contrôle.</summary>
        private static void Dress(Border border)
        {
            if (border.TemplatedParent != null || border.GetValue(KeepCornersProperty)) return;
            var stored = border.GetValue(OriginalCornersProperty);
            var shadow = border.GetValue(OriginalShadowProperty);
            if (Bare)
            {
                if (stored is CornerRadius) { border.CornerRadius = (CornerRadius)stored; border.ClearValue(OriginalCornersProperty); }
                if (shadow is BoxShadows) { border.BoxShadow = (BoxShadows)shadow; border.ClearValue(OriginalShadowProperty); }
                return;
            }
            var original = stored is CornerRadius ? (CornerRadius)stored : border.CornerRadius;
            // Cyberpunk : un cadre fait main (carte, « paper », panneau à filet) est un tube de néon —
            // un halo de l'accent autour de lui. Les autres skins rendent l'ombre d'origine.
            var framed = original.TopLeft >= 6 && border.BorderBrush != null && border.BorderThickness.Left > 0 && border.BorderThickness.Top > 0
                && border.BorderThickness.Right > 0 && border.BorderThickness.Bottom > 0 && !IsRound(border, original);
            if (Current == Skins.Cyberpunk && framed)
            {
                if (!(shadow is BoxShadows)) border.SetValue(OriginalShadowProperty, border.BoxShadow);
                border.BoxShadow = Glow(Chrome.Accent.Color, 12, Chrome.Dark ? (byte)0x4A : (byte)0x30);
            }
            else if (shadow is BoxShadows) { border.BoxShadow = (BoxShadows)shadow; border.ClearValue(OriginalShadowProperty); }
            if (original.TopLeft <= 0 && original.TopRight <= 0 && original.BottomLeft <= 0 && original.BottomRight <= 0) return;
            if (IsRound(border, original)) return;
            if (!(stored is CornerRadius)) border.SetValue(OriginalCornersProperty, original);
            var dressed = new CornerRadius(Round(original.TopLeft), Round(original.TopRight), Round(original.BottomRight), Round(original.BottomLeft));
            if (border.CornerRadius != dressed) border.CornerRadius = dressed;
        }

        private static bool IsRound(Border border, CornerRadius corners)
        {
            var width = border.Width;
            var height = border.Height;
            if (double.IsNaN(width) || double.IsNaN(height) || Math.Abs(width - height) > 0.5) return false;
            return corners.TopLeft * 2 >= width - 1;
        }

        /// <summary>Une fenêtre de dialogue prend le fond du skin (un aplat sinon).
        /// La fenêtre principale tient le sien d'une ressource ; l'accueil a son papier.</summary>
        private static void DressWindow(Window window)
        {
            if (window is MainWindow || window is WelcomeWindow) return;
            var stored = window.GetValue(OriginalWindowBackgroundProperty);
            var backdrop = Plain ? null : Backdrop();
            if (backdrop == null)
            {
                if (stored != null)
                {
                    window.Background = ReferenceEquals(stored, NoBrush) ? null : (IBrush)stored;
                    window.ClearValue(OriginalWindowBackgroundProperty);
                }
                return;
            }
            if (stored == null) window.SetValue(OriginalWindowBackgroundProperty, (object)window.Background ?? NoBrush);
            window.Background = backdrop;
        }

        // ================================================================ l'Encyclopédie exportée

        /// <summary>Pose le skin en cours sur les options d'un export HTML de
        /// l'Encyclopédie (jamais écrit dans le projet). Rien pour « modern ».</summary>
        public static EncyclopediaOptions DressExport(EncyclopediaOptions options)
        {
            if (options == null) return null;
            options.Skin = Plain ? "" : Current;
            options.SkinAccent2 = AppSettings.SkinAccent2 ?? "";
            options.SkinTexture = null;
            if (Current == Skins.Eden)
            {
                var path = TexturePath;
                try { if (path != null) options.SkinTexture = File.ReadAllBytes(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return options;
        }

        /// <summary>L'habillage du « PDF Encyclopédique » sous le skin en cours, ou null (« modern »).
        /// L'accent est celui de l'application.</summary>
        public static Print.PdfSkin PdfSkin()
        {
            if (Plain) return null;
            var skin = Skins.PdfSkinFor(Current, Skins.AccentFor(Current, AppSettings.AccentColor), AppSettings.SkinAccent2);
            if (skin != null && skin.Texture)
            {
                try
                {
                    var path = TexturePath;
                    if (path != null)
                    {
                        int width, height;
                        var rgb = Platform.Images.ToRgb24(File.ReadAllBytes(path), 1400, out width, out height);
                        if (rgb != null) { skin.TextureRgb = rgb; skin.TextureWidth = width; skin.TextureHeight = height; }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return skin;
        }

        // — Sonde.
        internal static double CornerOfForProbe(Border border) { return border.CornerRadius.TopLeft; }
        internal static bool HasBackdropForProbe { get { return !Plain && Backdrop() != null; } }
        internal static object ResourceForProbe(string key) { object value; return Application.Current.Resources.TryGetResource(key, null, out value) ? value : null; }
        internal static bool TextureFoundForProbe { get { return TexturePath != null; } }
    }
}

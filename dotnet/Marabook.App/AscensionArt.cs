using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>ASCENSION · ART NOUVEAU (2.0.0, 25/11/2026, le cahier d'intégration de Rémi) — ce que le
    /// skin « Ascension » ajoute à ce qu'il était (bleu nuit, or, angles droits, capitales) :
    ///  · une TEXTURE DE VITRAIL sur le fond peint, sous le verre des barres (VitrailBrush) ;
    ///  · des ENLUMINURES sur les cadres : coins, cimier, séparateur (IlluminatedFrame, Draw…) ;
    ///  · une DORURE QUI LUIT sur les filets : un seul pinceau (GiltLine), un reflet qui balaie en 9 s ;
    ///  · la MAGIE BLEUE des progressions (barres, nimbe de l'objectif du jour), fixe, rare ;
    ///  · sa police de titres, embarquée : « Ascension SC » (Skin.TitleFont) ;
    ///  · TROIS MÉTAUX (or, vermeil, argent) parmi les couleurs d'accent.
    /// Tout est gardé par On : sous un autre skin, rien d'ici ne peint ni ne tourne.
    /// UNE SEULE HORLOGE pour tout ce qui bouge (reflet des filets, reflet des barres, couronne du
    /// nimbe) : un minuteur de 50 ms qui repeint les éléments inscrits. Elle est FIGÉE — reflet au
    /// milieu des filets, pas de reflet sur les barres, couronne immobile — quand les animations sont
    /// coupées (Préférences), quand aucune fenêtre de l'application n'est active, et sous une sonde ou
    /// une capture (les images doivent être stables).</summary>
    internal static class AscensionArt
    {
        /// <summary>Vrai sous « Ascension ».</summary>
        public static bool On { get { return Skin.Current == Skins.MuseumAscent; } }

        // ---------------------------------------------------------------- 1. les trois métaux

        /// <summary>Un métal : la valeur rangée dans AppSettings.AccentColor, et la recette appliquée aux
        /// lignes 6 à 10 de la palette (filet fort, accent, accent fort, filet chaud, tranche) :
        /// Hsl(teinte + dh, s × SatScale, l + Lift). L'or redonne exactement la palette d'origine.</summary>
        internal sealed class MetalRecipe
        {
            public string Key, Label, Hex;
            public double Hue, SatScale, DarkLift, LightLift;
        }

        internal static readonly MetalRecipe[] Metals =
        {
            new MetalRecipe { Key = "or", Label = "Or", Hex = "#C89B3C", Hue = 40.5, SatScale = 1.00, DarkLift = 0.00, LightLift = 0.00 },
            new MetalRecipe { Key = "vermeil", Label = "Vermeil", Hex = "#C66D4D", Hue = 16.0, SatScale = 0.92, DarkLift = 0.03, LightLift = 0.00 },
            new MetalRecipe { Key = "argent", Label = "Argent", Hex = "#9BA3AB", Hue = 210, SatScale = 0.16, DarkLift = 0.13, LightLift = 0.04 }
        };

        /// <summary>La recette du métal choisi : l'or si rien n'est choisi ; null pour une couleur
        /// personnalisée (elle garde le calcul d'avant : sa teinte seule).</summary>
        internal static MetalRecipe MetalFor(string accentHex)
        {
            if (string.IsNullOrEmpty(accentHex)) return Metals[0];
            foreach (var metal in Metals) if (string.Equals(metal.Hex, accentHex, StringComparison.OrdinalIgnoreCase)) return metal;
            return null;
        }

        internal static Color GoldLo { get { return Chrome.BorderStrong.Color; } }
        internal static Color Gold { get { return Chrome.Accent.Color; } }
        internal static Color GoldHi { get { return Chrome.Glow.Color; } }
        /// <summary>L'or PÂLE d'un reflet : le filet chaud au sombre ; au clair (où ce filet est sombre), l'or éclairci.</summary>
        internal static Color GoldPale { get { return Chrome.Dark ? Chrome.Glow.Color : Chrome.Blend(Chrome.Accent.Color, Colors.White, 0.62); } }

        /// <summary>La MAGIE : fixe, elle ne suit pas le métal. Rare — les progressions, et ce qui « lance ».</summary>
        internal static readonly Color MagicDeep = Color.Parse("#0596AA"), Magic = Color.Parse("#0AC8B9"), MagicHi = Color.Parse("#CDFAFA");

        /// <summary>La classe d'un bouton qui LANCE (un sprint, un export) : bleu de magie sous « Ascension »,
        /// sans effet ailleurs (il garde son allure de bouton principal).</summary>
        public const string MagicClass = "magic";

        internal static Color WithAlpha(Color color, double alpha)
        {
            return Color.FromArgb((byte)Math.Round(255 * Math.Max(0, Math.Min(1, alpha))), color.R, color.G, color.B);
        }

        // ---------------------------------------------------------------- 2. la texture « vitrail »

        /// <summary>L'opacité du trait de la texture (le curseur « Intensité » de la maquette, à mi-course).</summary>
        internal const double TextureOpacity = 0.11;
        /// <summary>Au clair (non maquetté) : le trait est à l'encre, plus marqué sur un fond pâle — un cran de moins, pour ne pas griser le fond.</summary>
        internal const double TextureOpacityLight = 0.08;
        internal const double TextureTile = 180;
        /// <summary>Dans le fond d'un dialogue, la texture est atténuée d'autant (25/11, Rémi : « trop visible »).</summary>
        internal const double DialogTexture = 0.45;

        /// <summary>Le tracé de la tuile (croisée, diagonales d'angle, quatre mandorles) ; les deux cercles s'y ajoutent.</summary>
        internal const string VitrailPath =
            "M90 0v180M0 90h180M0 0l40 40M180 0l-40 40M0 180l40-40M180 180l-40-40" +
            "M90 30c20 20 20 40 0 60-20-20-20-40 0-60zM90 90c20 20 20 40 0 60-20-20-20-40 0-60z" +
            "M30 90c20-20 40-20 60 0-20 20-40 20-60 0zM90 90c20-20 40-20 60 0-20 20-40 20-60 0z";
        /// <summary>La couleur du trait de la texture : or pâle au sombre, l'encre du skin au clair.</summary>
        internal static Color VitrailInk(bool dark) { return dark ? Color.Parse("#E6C77A") : Chrome.Ink.Color; }

        /// <summary>La mosaïque : une tuile de 180 px (croisée, deux cercles, quatre mandorles, diagonales
        /// d'angle), trait de 1 px — or pâle au sombre, l'encre du skin au clair. Ancrée à ce qui la porte.</summary>
        internal static IBrush VitrailBrush(bool dark)
        {
            var pen = new Pen(new SolidColorBrush(VitrailInk(dark)), 1);
            var group = new DrawingGroup { Opacity = dark ? TextureOpacity : TextureOpacityLight };
            group.Children.Add(new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(0, 0, TextureTile, TextureTile)) });
            group.Children.Add(new GeometryDrawing
            {
                Pen = pen,
                Geometry = Geometry.Parse(VitrailPath)
            });
            group.Children.Add(new GeometryDrawing { Pen = pen, Geometry = new EllipseGeometry(new Rect(30, 30, 120, 120)) });
            group.Children.Add(new GeometryDrawing { Pen = pen, Geometry = new EllipseGeometry(new Rect(2, 2, 176, 176)) });
            return new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                SourceRect = new RelativeRect(0, 0, TextureTile, TextureTile, RelativeUnit.Absolute),
                DestinationRect = new RelativeRect(0, 0, TextureTile, TextureTile, RelativeUnit.Absolute)
            };
        }

        // ---------------------------------------------------------------- 3. l'horloge

        /// <summary>LE RYTHME (patch performance, 26/11/2026, Rémi : « j'ai peur que les animations ralentissent
        /// le tout ») — avant, tout ce qui bougeait était repeint vingt fois par seconde, sans répit : les
        /// cadres enluminés et la silhouette des Préférences faisaient redessiner des fenêtres entières en
        /// continu. Désormais :
        ///  · le REFLET des filets passe en 2,6 s puis se repose le reste des 9 s — au repos, plus rien
        ///    n'est repeint ; le reflet des BARRES passe en 1,4 s toutes les 6 s ;
        ///  · la couronne du NIMBE tourne toujours, mais n'est repeinte que huit fois par seconde (elle
        ///    fait un tour en 40 s : personne ne voit la différence) ;
        ///  · chaque élément n'est repeint que pour CE QUI LE CONCERNE (Kind) ;
        ///  · rien ne bouge pendant que l'auteur AGIT (souris, molette, clavier : Idle) ni pendant
        ///    qu'une pastille d'onglet glisse (Quiet) — l'interface garde toute la machine ;
        ///  · le temps est celui que l'horloge a réellement compté : une pause ne fait rien sauter.</summary>
        internal const double SweepSeconds = 9, SweepLasts = 2.6, ShineSeconds = 6, ShineLasts = 1.4, TurnSeconds = 40;
        private const double SweepRest = 0.44; // figé (animations coupées, sonde) : le reflet au milieu du filet
        /// <summary>Après un geste de l'auteur, les ornements attendent ce temps avant de reprendre.</summary>
        internal const double IdleSeconds = 0.7;

        /// <summary>Ce qui fait repeindre un élément : le reflet des filets, celui des barres, la couronne qui tourne.</summary>
        internal enum Kind { Gilt, Shine, Turn }

        private sealed class Mover { public WeakReference<Visual> Visual; public Kind Kind; }

        private static readonly Stopwatch Wall = Stopwatch.StartNew();
        private static readonly List<Mover> Moved = new List<Mover>();
        private static DispatcherTimer _timer;
        private static LinearGradientBrush _gilt;
        private static Color _giltFor, _giltLo;
        private static double _giltAt = -1;
        private static double _time;        // le temps compté par l'horloge, en secondes : il n'avance que quand elle tourne
        private static double _lastTick = -1, _lastInput = -10, _quietUntil;
        private static bool _sweeping, _shining, _hooked;
        private static int _ticks;

        /// <summary>Vrai quand les ornements PEUVENT bouger : « Ascension », animations permises, une fenêtre
        /// active, et ni sonde ni capture.</summary>
        internal static bool Moving
        {
            get
            {
                if (!On || !AppSettings.Animations) return false;
                var launch = App.Launch;
                if (launch != null && launch.Unattended && !ForcedForBench) return false;
                var desktop = Application.Current == null ? null : Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                if (desktop == null) return false;
                foreach (var window in desktop.Windows) if (window.IsActive && window.WindowState != WindowState.Minimized) return true;
                return false;
            }
        }

        /// <summary>Vrai quand l'auteur ne fait rien depuis un instant, et qu'aucune pastille d'onglet ne glisse.</summary>
        /// <summary>Le banc d'essai des infobulles (ProbesTips) laisse tourner les ornements sous la sonde.</summary>
        internal static bool ForcedForBench;
        internal static bool Idle { get { var now = Wall.Elapsed.TotalSeconds; return (_openTips == 0 || now - _tipSince > 20) && now - _lastInput >= IdleSeconds && now >= _quietUntil; } }
        /// <summary>Les infobulles ouvertes : tant qu'il y en a une, rien ne bouge dessous (26/11 — des bulles clignotaient dans les Préférences).</summary>
        private static int _openTips;
        private static double _tipSince; // garde-fou : un compte resté ouvert par erreur ne fige pas tout pour toujours
        internal static int OpenTipsForProbe { get { return _openTips; } }

        /// <summary>Fait taire les ornements pour cette durée (une animation de l'interface a besoin de la machine).</summary>
        internal static void Quiet(double milliseconds) { _quietUntil = Math.Max(_quietUntil, Wall.Elapsed.TotalSeconds + milliseconds / 1000.0); }

        /// <summary>Écoute les gestes de l'auteur, dans toutes les fenêtres (une fois).</summary>
        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            EventHandler<Avalonia.Interactivity.RoutedEventArgs> note = delegate { _lastInput = Wall.Elapsed.TotalSeconds; };
            ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>(delegate(Control control, AvaloniaPropertyChangedEventArgs e)
            {
                _openTips = Math.Max(0, _openTips + (true.Equals(e.NewValue) ? 1 : -1));
                if (true.Equals(e.NewValue)) _tipSince = Wall.Elapsed.TotalSeconds;
                _lastInput = Wall.Elapsed.TotalSeconds; // une bulle qui se ferme : un instant de répit avant de reprendre
            });
            Avalonia.Input.InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(delegate(TopLevel top, Avalonia.Input.PointerEventArgs e) { note(top, e); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            Avalonia.Input.InputElement.PointerPressedEvent.AddClassHandler<TopLevel>(delegate(TopLevel top, Avalonia.Input.PointerPressedEventArgs e) { note(top, e); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            Avalonia.Input.InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>(delegate(TopLevel top, Avalonia.Input.PointerWheelEventArgs e) { note(top, e); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            Avalonia.Input.InputElement.KeyDownEvent.AddClassHandler<TopLevel>(delegate(TopLevel top, Avalonia.Input.KeyEventArgs e) { note(top, e); }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        }

        /// <summary>Le temps des ornements, en secondes : celui que l'horloge a compté (0 tant qu'elle n'a pas tourné).</summary>
        internal static double Time { get { return _time; } }

        private static double Cycle(double seconds) { return _time % seconds; }

        /// <summary>Où en est le reflet des barres, de 0 à 1, pendant son passage ; −1 hors passage ou horloge figée.</summary>
        internal static double ShinePhase { get { return _shining ? Math.Min(1, Cycle(ShineSeconds) / ShineLasts) : -1; } }
        /// <summary>L'angle de la couronne du nimbe, en radians (un tour en 40 s ; 0 tant que l'horloge n'a pas tourné).</summary>
        internal static double TurnAngle { get { return Cycle(TurnSeconds) / TurnSeconds * 2 * Math.PI; } }

        /// <summary>Inscrit un élément : il est repeint quand ce qui le concerne bouge, tant qu'il se voit.</summary>
        internal static void Animate(Visual visual, Kind kind = Kind.Gilt)
        {
            if (visual == null) return;
            Moved.Add(new Mover { Visual = new WeakReference<Visual>(visual), Kind = kind });
            if (_timer != null) return;
            Hook();
            // En priorité de RENDU, comme avant le patch performance (26/11) : passées en priorité d'arrière-plan, cette
            // minuterie et celle du ciel ont coïncidé avec des infobulles qui ne s'ouvraient plus dans les Préférences.
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, delegate { Tick(); });
            _timer.Start();
        }

        private static void Tick()
        {
            var now = Wall.Elapsed.TotalSeconds;
            if (!Moving)
            {
                // Figé : le filet revient à son reflet du milieu, les barres perdent le leur — une fois.
                _lastTick = -1;
                if (_gilt != null && _giltAt != SweepRest) { PlaceGilt(SweepRest); Repaint(Kind.Gilt); }
                if (_shining) { _shining = false; Repaint(Kind.Shine); }
                _sweeping = false;
                return;
            }
            if (!Idle) { _lastTick = -1; return; } // l'auteur agit : tout attend, là où il en est
            if (_lastTick >= 0) _time += Math.Min(0.25, now - _lastTick);
            _lastTick = now;
            _ticks++;
            // Le reflet des filets : un passage, puis le repos (le reflet hors du cadre).
            var sweep = Cycle(SweepSeconds);
            if (sweep < SweepLasts)
            {
                _sweeping = true;
                if (_gilt != null) PlaceGilt(sweep / SweepLasts);
                Repaint(Kind.Gilt);
            }
            else if (_sweeping || (_gilt != null && _giltAt != 1))
            {
                _sweeping = false;
                if (_gilt != null) PlaceGilt(1);
                Repaint(Kind.Gilt);
            }
            // Le reflet des barres.
            var shining = Cycle(ShineSeconds) < ShineLasts;
            if (shining || _shining) { _shining = shining; Repaint(Kind.Shine); }
            // La couronne du nimbe : huit images par seconde lui suffisent.
            if (_ticks % 4 == 0) Repaint(Kind.Turn);
        }

        internal static bool AnimatedForProbe(Visual visual) { foreach (var mover in Moved) { Visual held; if (mover.Visual.TryGetTarget(out held) && ReferenceEquals(held, visual)) return true; } return false; }
        internal static int AnimatedCountForProbe(Kind kind) { var count = 0; foreach (var mover in Moved) { Visual held; if (mover.Kind == kind && mover.Visual.TryGetTarget(out held)) count++; } return count; }

        private static void Repaint(Kind kind)
        {
            for (var i = Moved.Count - 1; i >= 0; i--)
            {
                Visual visual;
                if (!Moved[i].Visual.TryGetTarget(out visual)) Moved.RemoveAt(i);
                else if (Moved[i].Kind == kind && visual.IsEffectivelyVisible) visual.InvalidateVisual();
            }
        }

        // ---------------------------------------------------------------- 4. la dorure « qui luit »

        /// <summary>Le pinceau des filets dorés : bronze → or → bronze, et UN reflet blanc-or. Large de
        /// trois fois l'élément (en unités relatives : chaque cadre à son échelle), il glisse de deux
        /// largeurs en 9 s et boucle sans à-coup. Un seul pinceau tant que la palette ne change pas.</summary>
        internal static IBrush GiltLine()
        {
            if (_gilt != null && _giltFor == Gold && _giltLo == GoldLo) return _gilt;
            _giltFor = Gold;
            _giltLo = GoldLo;
            Color lo = GoldLo, gold = Gold, hi = Chrome.Blend(GoldPale, Colors.White, 0.5);
            var brush = new LinearGradientBrush { SpreadMethod = GradientSpreadMethod.Repeat };
            var colors = new[] { lo, gold, lo, gold, hi, gold, lo, gold, lo };
            var offsets = new[] { 0.0, 0.20, 0.32, 0.40, 0.46, 0.52, 0.64, 0.80, 1.0 };
            for (var i = 0; i < colors.Length; i++) brush.GradientStops.Add(new GradientStop(colors[i], offsets[i]));
            _gilt = brush;
            PlaceGilt(Moving ? 1 : SweepRest);
            return _gilt;
        }

        private static void PlaceGilt(double at)
        {
            _giltAt = at;
            _gilt.StartPoint = new RelativePoint(-2 * at, 0, RelativeUnit.Relative);
            _gilt.EndPoint = new RelativePoint(3 - 2 * at, 0.35, RelativeUnit.Relative);
        }

        /// <summary>Le second filet d'un cadre, à 4 px en dedans : l'or à 28 %.</summary>
        internal static IBrush InnerLine() { return new SolidColorBrush(WithAlpha(Gold, 0.28)); }

        /// <summary>LA LUEUR DE MAGIE au pied de la Pile et du rail (25/11, Rémi) : le bleu de la magie qui
        /// monte du bas et se fond vers rien — bas, pour ne pas voiler les lignes. Null hors d'« Ascension ».</summary>
        internal static IBrush SideGlow()
        {
            if (!On) return null;
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative) };
            brush.GradientStops.Add(new GradientStop(WithAlpha(Magic, Chrome.Dark ? 0.24 : 0.20), 0));
            brush.GradientStops.Add(new GradientStop(WithAlpha(Magic, Chrome.Dark ? 0.09 : 0.07), SideGlowReach * 0.45));
            brush.GradientStops.Add(new GradientStop(WithAlpha(Magic, 0), SideGlowReach));
            return brush;
        }

        /// <summary>Où la lueur du pied a rejoint le rien, en part de la hauteur de la colonne.</summary>
        internal const double SideGlowReach = 0.20;

        /// <summary>Le métal d'un ORNEMENT (coin, volute) : un dégradé en diagonale, pâle → or → bronze — un
        /// trait d'une seule couleur paraissait figé (25/11, Rémi).</summary>
        internal static IBrush Ornament()
        {
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
            brush.GradientStops.Add(new GradientStop(Chrome.Blend(GoldPale, Colors.White, 0.35), 0));
            brush.GradientStops.Add(new GradientStop(Gold, 0.45));
            brush.GradientStops.Add(new GradientStop(GoldLo, 1));
            return brush;
        }

        /// <summary>…et celui d'un ornement EN LARGEUR (cimier, séparateur) : bronze aux bouts, pâle au centre.</summary>
        internal static IBrush OrnamentAcross()
        {
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            brush.GradientStops.Add(new GradientStop(GoldLo, 0));
            brush.GradientStops.Add(new GradientStop(Gold, 0.30));
            brush.GradientStops.Add(new GradientStop(Chrome.Blend(GoldPale, Colors.White, 0.35), 0.5));
            brush.GradientStops.Add(new GradientStop(Gold, 0.70));
            brush.GradientStops.Add(new GradientStop(GoldLo, 1));
            return brush;
        }

        /// <summary>Le dégradé de magie d'une progression (profond → vif).</summary>
        internal static IBrush MagicFill(RelativePoint from, RelativePoint to)
        {
            var brush = new LinearGradientBrush { StartPoint = from, EndPoint = to };
            brush.GradientStops.Add(new GradientStop(MagicDeep, 0));
            brush.GradientStops.Add(new GradientStop(Magic, 1));
            return brush;
        }

        // ---------------------------------------------------------------- 5. les ornements

        private static readonly Geometry CornerFrame = Geometry.Parse("M1 30V1h29");
        private static readonly Geometry CornerArc = Geometry.Parse("M5 22c0-9 4-15 11-17");
        private static readonly Geometry WhipLong = Geometry.Parse("M5 40c2-14 9-24 22-30 6-3 12-3 15-2");
        private static readonly Geometry WhipCurl = Geometry.Parse("M12 30c3-8 9-12 15-11-4 2-6 6-3 8 3 2 6-1 5-4");
        private static readonly Geometry Leaf = Geometry.Parse("M20 12c4-3 9-2 11 1-4 0-7 1-11-1z");
        private static readonly Geometry CrestLines = Geometry.Parse("M2 9h24M54 9h24M26 9c4-6 9-6 10-1M54 9c-4-6-9-6-10-1");
        private static readonly Geometry SeparatorEnds = Geometry.Parse("M0 8c-6 0-8-5-4-6 3 0 3 4 0 4");
        private static readonly Geometry SeparatorWave = Geometry.Parse("M-20 8c6-5 10-5 12 0M20 8c-6-5-10-5-12 0");

        /// <summary>Un losange centré sur <paramref name="center"/>, de demi-diagonale <paramref name="half"/>.</summary>
        internal static void Diamond(DrawingContext context, Point center, double half, IBrush fill, IPen pen)
        {
            var points = new[] { new Point(center.X - half, center.Y), new Point(center.X, center.Y - half), new Point(center.X + half, center.Y), new Point(center.X, center.Y + half) };
            context.DrawGeometry(fill, pen, new PolylineGeometry(points, true));
        }

        /// <summary>Un losange or pâle et son halo (onglet choisi, ligne choisie, jalon franchi, métal choisi).</summary>
        internal static void PaleDiamond(DrawingContext context, Point center, double half)
        {
            var halo = new RadialGradientBrush();
            halo.GradientStops.Add(new GradientStop(WithAlpha(Gold, 0.55), 0));
            halo.GradientStops.Add(new GradientStop(WithAlpha(Gold, 0.16), 0.55));
            halo.GradientStops.Add(new GradientStop(WithAlpha(Gold, 0), 1));
            context.DrawEllipse(halo, null, center, half * 2.3, half * 2.3);
            Diamond(context, center, half, new SolidColorBrush(GoldHi), new Pen(new SolidColorBrush(Gold), 0.8));
        }

        /// <summary>Un coin (repère 46 × 46, coin haut gauche), posé par <paramref name="where"/> : l'angle
        /// carré, un quart de courbe, un losange plein ; « riche » : plus le coup de fouet et la feuille.</summary>
        internal static void Corner(DrawingContext context, Matrix where, bool rich)
        {
            // Le dégradé est mesuré sur chaque tracé : le clair à l'angle, le bronze au bout des volutes.
            var gold = Ornament();
            using (context.PushTransform(where))
            {
                context.DrawGeometry(null, new Pen(gold, 1.4) { LineCap = PenLineCap.Round }, CornerFrame);
                context.DrawGeometry(null, new Pen(gold, 1) { LineCap = PenLineCap.Round }, CornerArc);
                if (rich)
                {
                    context.DrawGeometry(null, new Pen(gold, 0.9) { LineCap = PenLineCap.Round }, WhipLong);
                    context.DrawGeometry(null, new Pen(gold, 0.9) { LineCap = PenLineCap.Round }, WhipCurl);
                    context.DrawGeometry(new SolidColorBrush(WithAlpha(Gold, 0.55)), null, Leaf);
                }
                Diamond(context, new Point(5.4, 5.4), 3.96, new SolidColorBrush(Chrome.Blend(GoldPale, Gold, 0.4)), null);
            }
        }

        /// <summary>Le cimier : un losange à cheval sur un filet, centré sur <paramref name="center"/>, deux
        /// volutes. Son cœur est rempli de <paramref name="ground"/> : il « coupe » le filet.</summary>
        internal static void Crest(DrawingContext context, Point center, IBrush ground)
        {
            var gold = new SolidColorBrush(Gold);
            using (context.PushTransform(Matrix.CreateTranslation(center.X - 40, center.Y - 9)))
            {
                context.DrawGeometry(null, new Pen(OrnamentAcross(), 0.9), CrestLines);
                Diamond(context, new Point(40, 9), 7.07, ground, new Pen(gold, 1.1));
                Diamond(context, new Point(40, 9), 2.83, gold, null);
            }
        }

        /// <summary>Le séparateur : deux filets, des volutes aux bouts, un losange au centre — de
        /// <paramref name="left"/> à <paramref name="right"/>, sur la ligne <paramref name="y"/>. Les filets
        /// s'allongent ; volutes et losange gardent leur taille.</summary>
        internal static void Separator(DrawingContext context, double left, double right, double y)
        {
            if (right - left < 70) return;
            var gold = new SolidColorBrush(Gold);
            var pen = new Pen(gold, 0.9);
            var middle = Math.Round((left + right) / 2);
            // Les deux filets en dégradé : le bronze aux bouts, le clair vers le losange (une ligne n'a pas de hauteur : le pinceau est en unités absolues).
            var pale = Chrome.Blend(GoldPale, Colors.White, 0.35);
            var toRight = new LinearGradientBrush { StartPoint = new RelativePoint(left + 6, y, RelativeUnit.Absolute), EndPoint = new RelativePoint(middle - 20, y, RelativeUnit.Absolute) };
            toRight.GradientStops.Add(new GradientStop(GoldLo, 0));
            toRight.GradientStops.Add(new GradientStop(Gold, 0.5));
            toRight.GradientStops.Add(new GradientStop(pale, 1));
            var toLeft = new LinearGradientBrush { StartPoint = new RelativePoint(right - 6, y, RelativeUnit.Absolute), EndPoint = new RelativePoint(middle + 20, y, RelativeUnit.Absolute) };
            toLeft.GradientStops.Add(new GradientStop(GoldLo, 0));
            toLeft.GradientStops.Add(new GradientStop(Gold, 0.5));
            toLeft.GradientStops.Add(new GradientStop(pale, 1));
            context.DrawLine(new Pen(toRight, 0.9), new Point(left + 6, y), new Point(middle - 20, y));
            context.DrawLine(new Pen(toLeft, 0.9), new Point(middle + 20, y), new Point(right - 6, y));
            using (context.PushTransform(Matrix.CreateTranslation(left + 6, y - 8))) context.DrawGeometry(null, pen, SeparatorEnds);
            using (context.PushTransform(Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(right - 6, y - 8))) context.DrawGeometry(null, pen, SeparatorEnds);
            using (context.PushTransform(Matrix.CreateTranslation(middle, y - 8))) context.DrawGeometry(null, pen, SeparatorWave);
            Diamond(context, new Point(middle, y), 7.07, null, new Pen(gold, 1.1));
            context.DrawEllipse(gold, null, new Point(middle, y), 1.8, 1.8);
        }

        // ---------------------------------------------------------------- 6. les progressions

        /// <summary>Le reflet d'une barre : transparent → clair 70 % → transparent, large de 40 % de la
        /// piste, qui la traverse de gauche à droite ; rogné à la part remplie. Rien quand l'horloge est figée.</summary>
        internal static void Shine(DrawingContext context, Rect track, double filled)
        {
            var phase = ShinePhase;
            if (phase < 0 || filled <= 2) return;
            var width = track.Width * 0.4;
            var x = track.X - width + (track.Width + width) * phase;
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            brush.GradientStops.Add(new GradientStop(WithAlpha(MagicHi, 0), 0));
            brush.GradientStops.Add(new GradientStop(WithAlpha(MagicHi, 0.7), 0.5));
            brush.GradientStops.Add(new GradientStop(WithAlpha(MagicHi, 0), 1));
            using (context.PushClip(new Rect(track.X, track.Y, filled, track.Height)))
                context.FillRectangle(brush, new Rect(x, track.Y, width, track.Height));
        }

        /// <summary>LE NIMBE (l'anneau de l'objectif du jour sous « Ascension »), de l'extérieur vers
        /// l'intérieur : 48 rayons de longueurs alternées, une couronne pointillée et un losange or pâle
        /// à midi qui tournent ensemble, la piste, l'arc de magie et son halo, le médaillon, un filet
        /// intérieur. Les rayons du dessin valent pour un contrôle de 150 px : tout suit sa taille.</summary>
        internal static void Nimbe(DrawingContext context, Size size, double ratio, string text, string caption)
        {
            var center = new Point(size.Width / 2, size.Height / 2);
            var k = Math.Min(size.Width, size.Height) / 150.0;
            if (k < 0.4) return;
            var gold = new SolidColorBrush(Gold);
            // Les rayons.
            var ray = new Pen(new SolidColorBrush(WithAlpha(Gold, 0.55)), 0.8);
            for (var i = 0; i < 48; i++)
            {
                var angle = i * Math.PI / 24;
                var reach = (i % 4 == 0 ? 74 : i % 2 == 0 ? 71.5 : 70) * k;
                context.DrawLine(ray, new Point(center.X + 62 * k * Math.Cos(angle), center.Y + 62 * k * Math.Sin(angle)), new Point(center.X + reach * Math.Cos(angle), center.Y + reach * Math.Sin(angle)));
            }
            // La couronne pointillée et son losange, qui tournent.
            using (context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(TurnAngle) * Matrix.CreateTranslation(center.X, center.Y)))
            {
                var dotted = new Pen(new SolidColorBrush(WithAlpha(Gold, 0.7)), 0.8) { DashStyle = new DashStyle(new[] { 1.25, 5.0 }, 0) };
                context.DrawEllipse(null, dotted, center, 68 * k, 68 * k);
                Diamond(context, new Point(center.X, center.Y - 68 * k), 4.2 * k, new SolidColorBrush(GoldHi), null);
            }
            // La piste, puis l'arc de magie (un trait plus large à 30 % dessous : son halo).
            var radius = 58 * k;
            context.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(Chrome.Ink.Color, 0.12)), 5 * k), center, radius, radius);
            if (ratio > 0.004)
            {
                Geometry arc;
                if (ratio >= 0.999) arc = new EllipseGeometry(new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
                else
                {
                    var angle = ratio * 2 * Math.PI;
                    var stream = new StreamGeometry();
                    using (var path = stream.Open())
                    {
                        path.BeginFigure(new Point(center.X, center.Y - radius), false);
                        path.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)), new Size(radius, radius), 0, ratio > 0.5, SweepDirection.Clockwise);
                        path.EndFigure(false);
                    }
                    arc = stream;
                }
                context.DrawGeometry(null, new Pen(new SolidColorBrush(WithAlpha(Magic, 0.30)), 11 * k) { LineCap = PenLineCap.Round }, arc);
                var lit = new ConicGradientBrush { Center = new RelativePoint(center, RelativeUnit.Absolute), Angle = 0 };
                lit.GradientStops.Add(new GradientStop(MagicDeep, 0));
                lit.GradientStops.Add(new GradientStop(Magic, 0.5));
                lit.GradientStops.Add(new GradientStop(Magic, 1));
                context.DrawGeometry(null, new Pen(lit, 5 * k) { LineCap = PenLineCap.Round }, arc);
            }
            // Le médaillon et son filet intérieur.
            context.DrawEllipse(Chrome.CardBg, new Pen(gold, 1), center, 50 * k, 50 * k);
            context.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(Gold, 0.6)), 0.6), center, 46 * k, 46 * k);
            // Le chiffre : la police des titres, graisse normale (elle n'en a qu'une).
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var big = new FormattedText(text ?? "", culture, FlowDirection.LeftToRight, new Typeface(Skin.TitleFont ?? FontFamily.Default, FontStyle.Normal, FontWeight.Normal), 25 * k, Chrome.Ink);
            var hasCaption = !string.IsNullOrEmpty(caption);
            context.DrawText(big, new Point(center.X - big.Width / 2, center.Y - big.Height / 2 - (hasCaption ? 7 * k : 0)));
            if (!hasCaption) return;
            var small = new FormattedText(caption, culture, FlowDirection.LeftToRight, new Typeface(Skin.UiFont ?? FontFamily.Default, FontStyle.Normal, FontWeight.Medium), 11, Chrome.SoftText);
            context.DrawText(small, new Point(center.X - small.Width / 2, center.Y + 13 * k));
        }
    }

    /// <summary>LE CADRE ENLUMINÉ (« Ascension ») : il PEINT par-dessus une carte déjà en place — le
    /// filet doré qui luit, un second filet à 4 px en dedans, les coins, le cimier au centre du haut,
    /// un séparateur au pied. Il ne prend pas la souris et ne peint rien sous un autre skin.
    /// Niveau « sobre » partout ; « riche » (coup de fouet et feuille) sur la seule carte « Où j'en
    /// suis » de l'Accueil, celle du compteur de mots.</summary>
    internal sealed class IlluminatedFrame : Control
    {
        /// <summary>Coins riches (la carte du compteur de mots).</summary>
        public bool Rich { get; set; }
        /// <summary>Le cimier (les grandes cartes seulement).</summary>
        public bool Crest { get; set; }
        /// <summary>Un séparateur au pied de la carte (la carte Livres).</summary>
        public bool Foot { get; set; }

        public IlluminatedFrame()
        {
            IsHitTestVisible = false;
            Tag = "illuminated";
            AscensionArt.Animate(this);
        }

        /// <summary>Le cadre enluminé d'une carte, à poser dans la carte à côté de son contenu : la marge
        /// négative le ramène sur la bordure.</summary>
        public static IlluminatedFrame Inside(Border card, bool rich, bool crest)
        {
            var padding = card.Padding;
            var line = card.BorderThickness;
            return new IlluminatedFrame { Rich = rich, Crest = crest, Margin = new Thickness(-(padding.Left + line.Left), -(padding.Top + line.Top), -(padding.Right + line.Right), -(padding.Bottom + line.Bottom)) };
        }

        protected override Size MeasureOverride(Size availableSize) { return new Size(0, 0); }

        public override void Render(DrawingContext context)
        {
            if (!AscensionArt.On) return;
            double w = Bounds.Width, h = Bounds.Height;
            if (w < 60 || h < 60) return;
            context.DrawRectangle(null, new Pen(AscensionArt.GiltLine(), 1), new Rect(0.5, 0.5, w - 1, h - 1));
            // Le second filet luit lui aussi, plus bas (25/11) : la même dorure, à 45 %.
            using (context.PushOpacity(0.45)) context.DrawRectangle(null, new Pen(AscensionArt.GiltLine(), 1), new Rect(4.5, 4.5, w - 9, h - 9));
            AscensionArt.Corner(context, Matrix.CreateTranslation(-1, -1), Rich);
            AscensionArt.Corner(context, Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(w + 1, -1), Rich);
            AscensionArt.Corner(context, Matrix.CreateScale(1, -1) * Matrix.CreateTranslation(-1, h + 1), Rich);
            AscensionArt.Corner(context, Matrix.CreateScale(-1, -1) * Matrix.CreateTranslation(w + 1, h + 1), Rich);
            if (Crest) AscensionArt.Crest(context, new Point(Math.Round(w / 2), 0.5), Chrome.BarBgLight);
            if (Foot) AscensionArt.Separator(context, 22, w - 22, h - 15);
        }
    }

    /// <summary>LE FILET D'UNE RUBRIQUE DE LA PILE : après le nom (« Écriture », « Encyclopédie »…), un
    /// filet jusqu'au bord. Sous « Ascension », il est d'or et finit par un losange plein ; ailleurs,
    /// c'est le filet gris de toujours.</summary>
    internal sealed class RubricLine : Control
    {
        public RubricLine() { IsHitTestVisible = false; Height = 9; VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center; Tag = "rubric-line"; }

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, y = Math.Floor(Bounds.Height / 2) + 0.5;
            if (w < 4) return;
            if (!AscensionArt.On) { context.DrawLine(new Pen(Chrome.Border, 1), new Point(0, y), new Point(w, y)); return; }
            var gold = AscensionArt.Gold;
            context.DrawLine(new Pen(new SolidColorBrush(AscensionArt.WithAlpha(gold, 0.7)), 0.9), new Point(0, y), new Point(Math.Max(0, w - 9), y));
            AscensionArt.Diamond(context, new Point(w - 4.5, y), 3.6, new SolidColorBrush(gold), null);
        }
    }

    /// <summary>LE CIMIER D'UN ONGLET CHOISI (« Ascension ») : posé dans le gabarit de chaque onglet
    /// (Theme.ChipFace), il peint — sur l'onglet choisi seulement — le filet doré qui luit, 2 px en haut,
    /// et un losange or pâle à cheval dessus, au centre. Rien sous un autre skin.</summary>
    internal sealed class TabCrest : Control
    {
        private readonly TabItem _tab;
        private readonly Border _face;

        /// <summary>La place laissée au losange au-dessus de l'onglet, dans le cadre du cimier.</summary>
        private const double Above = 6;

        /// <summary>Pour le gabarit d'un onglet (Theme.ChipFace) : <paramref name="face"/> est sa pastille.
        /// LE CIMIER COUVRE SON ONGLET, plus six unités au-dessus (26/11, Rémi : « le losange reste parfois sur
        /// l'onglet de départ ») : il ne mesurait rien et peignait HORS de son cadre — repeint vingt fois par
        /// seconde, cela ne se voyait pas ; depuis le patch performance, un losange éteint pouvait rester à
        /// l'écran. Il peint maintenant dans son cadre, et n'EXISTE à l'écran que sur l'onglet choisi.</summary>
        internal TabCrest(TabItem tab, Border face)
        {
            _tab = tab;
            _face = face;
            IsHitTestVisible = false;
            Tag = "tab-crest";
            tab.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == TabItem.IsSelectedProperty) Place(); };
            face.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Border.PaddingProperty || e.Property == Border.BorderThicknessProperty) Place();
                else if (e.Property == BoundsProperty) InvalidateVisual();
            };
            AttachedToVisualTree += delegate { Place(); };
            Place();
        }

        /// <summary>Étend le cimier jusqu'aux bords de la pastille (il vit dans son rembourrage), et ne le montre que sur l'onglet choisi.</summary>
        private void Place()
        {
            var padding = _face.Padding;
            var line = _face.BorderThickness;
            var margin = new Thickness(-(padding.Left + line.Left), -(padding.Top + line.Top) - Above, -(padding.Right + line.Right), -(padding.Bottom + line.Bottom));
            if (Margin != margin) Margin = margin;
            var shown = AscensionArt.On && _tab.IsSelected;
            if (IsVisible != shown) IsVisible = shown;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) { return new Size(0, 0); }

        internal bool ShownForProbe { get { return AscensionArt.On && _tab.IsSelected && IsVisible; } }
        /// <summary>Sonde : le losange est peint dans le cadre du cimier (rien ne déborde).</summary>
        internal bool InsideForProbe { get { return Bounds.Width > 12 && Bounds.Height > Above + 4; } }

        public override void Render(DrawingContext context)
        {
            if (!AscensionArt.On || !_tab.IsSelected) return;
            Paint(context, new Rect(0, Above, Bounds.Width, Math.Max(0, Bounds.Height - Above)));
        }

        /// <summary>Le losange or pâle, à cheval sur le milieu du bord haut de <paramref name="box"/>. Le filet doré
        /// est le CONTOUR de l'onglet lui-même (25/11) : il en suit l'arrondi, comme celui des autres onglets.</summary>
        internal static void Paint(DrawingContext context, Rect box)
        {
            if (box.Width < 12) return;
            AscensionArt.PaleDiamond(context, new Point(box.X + Math.Round(box.Width / 2), box.Y + 0.5), 3.6);
        }
    }
}

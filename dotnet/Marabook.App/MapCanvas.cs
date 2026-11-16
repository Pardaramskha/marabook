using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LA TOILE D'UNE CARTE (2.0.0, 16/11/2026). Deux rendus de la même image :
    ///
    /// — le PLANISPHÈRE : l'image à plat, promenée et zoomée ; seules les tuiles visibles
    ///   du niveau qui convient au zoom sont décodées, et une tuile pas encore prête est
    ///   remplacée par celle d'un niveau plus grossier — l'image se raffine en place.
    ///   C'est ici qu'on trace : zones, repères, échelle, mesures ;
    ///
    /// — le GLOBE : l'image enroulée sur une sphère (projection orthographique, calculée
    ///   pixel par pixel dans une bitmap), qu'on fait tourner à la souris. Les zones et
    ///   les repères y sont reportés ; le tracé, lui, reste au planisphère.
    ///
    /// Par-dessus, si la carte est le planisphère entier d'un monde : la nuit, à la date
    /// et à l'heure choisies.</summary>
    internal sealed class MapCanvas : Control
    {
        public enum Tool { Pan, Zone, Marker, Scale, Measure }

        private readonly MapView _owner;
        private double _scale = 1, _viewX, _viewY;   // px d'écran par px d'image ; le coin haut-gauche, en px d'image
        private bool _fitPending = true;
        private Tool _tool = Tool.Pan;
        private readonly List<Pos> _draft = new List<Pos>();
        private Pos? _measureA, _measureB;
        private Point? _cursor;
        private bool _panning, _moved;
        private Point _last;
        private int _dragVertex = -1;
        private bool _dragMarker;
        private MapInfo _dragBefore;
        private string _hoverId;
        private MapTileCache _cache;
        private bool _space;       // Espace tenu : la main fermée promène la carte, quel que soit l'outil (lot 12)
        private static Cursor _grabCursor;

        // Le globe.
        private double _globeLon, _globeLat = 0.35, _globeZoom = 1;
        private WriteableBitmap _globeBitmap;
        private int[] _globeBuffer;
        private string _globeKey;

        public MapCanvas(MapView owner)
        {
            _owner = owner;
            ClipToBounds = true;
            Focusable = true;
        }

        private MapInfo Map { get { return _owner.Map; } }
        // Le globe ne vaut que pour la carte d'un monde ENTIER (lot 12) : sinon, toujours à plat.
        private bool Globe { get { return Map != null && Map.WholeWorld && Map.View == Maps.ViewGlobe; } }
        public bool SpaceHeld { get { return _space; } }
        public Tool CurrentTool { get { return _tool; } }
        public double Scale { get { return _scale; } }
        public int DraftCount { get { return _draft.Count; } }

        public double FitScale
        {
            get
            {
                var map = Map;
                if (map == null || !map.HasImage || Bounds.Width <= 0 || Bounds.Height <= 0) return 1;
                return Math.Min(Bounds.Width / map.Width, Bounds.Height / map.Height) * 0.96;
            }
        }

        /// <summary>Le zoom, en multiples de la vue « Tout voir » : ce qui décide de ce qui paraît.</summary>
        public double ZoomRatio { get { return Globe ? _globeZoom : _scale / Math.Max(1e-9, FitScale); } }

        public void Attach()
        {
            var cache = Map == null ? null : MapTileCache.For(Map);
            if (cache == _cache) return;
            if (_cache != null) _cache.Arrived -= OnArrived;
            _cache = cache;
            if (_cache != null) _cache.Arrived += OnArrived;
            _globeKey = null;
        }

        public void Detach()
        {
            if (_cache != null) _cache.Arrived -= OnArrived;
            _cache = null;
            _globeBitmap = null;
            _globeBuffer = null;
            _globeKey = null;
        }

        private void OnArrived() { _globeKey = null; InvalidateVisual(); }

        public void ResetView()
        {
            _fitPending = true;
            _draft.Clear();
            _measureA = _measureB = null;
            _tool = Tool.Pan;
            _globeLon = 0;
            _globeLat = 0.35;
            _globeZoom = 1;
            _globeKey = null;
            if (Bounds.Width > 0) FitAll();
        }

        public void SetTool(Tool tool)
        {
            _tool = tool;
            _draft.Clear();
            _measureA = _measureB = null;
            UpdateCursor();
            InvalidateVisual();
        }

        private void UpdateCursor()
        {
            Cursor = _space ? GrabCursor() : _tool == Tool.Pan || Globe ? Cursor.Default : new Cursor(StandardCursorType.Cross);
        }

        /// <summary>ESPACE (lot 12, Rémi : « toujours un grab, icône main fermée, peu importe le mode ») :
        /// tant que la barre est tenue, glisser promène la carte — on ne trace rien, on ne choisit rien.</summary>
        public void SetSpace(bool held)
        {
            if (_space == held) return;
            _space = held;
            UpdateCursor();
        }

        /// <summary>La main fermée : dessinée une fois (Avalonia n'a pas ce curseur en standard).</summary>
        private static Cursor GrabCursor()
        {
            if (_grabCursor != null) return _grabCursor;
            try
            {
                using (var bitmap = new SkiaSharp.SKBitmap(32, 32, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul))
                {
                    using (var canvas = new SkiaSharp.SKCanvas(bitmap))
                    using (var ink = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(20, 20, 20), IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 3.2f, StrokeJoin = SkiaSharp.SKStrokeJoin.Round })
                    using (var skin = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.White, IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Fill })
                    using (var line = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(20, 20, 20), IsAntialias = true, Style = SkiaSharp.SKPaintStyle.Stroke, StrokeWidth = 1.2f })
                    {
                        canvas.Clear(SkiaSharp.SKColors.Transparent);
                        // Un poing : la paume, quatre phalanges repliées au-dessus, le pouce sur le côté.
                        var parts = new[]
                        {
                            new SkiaSharp.SKRoundRect(new SkiaSharp.SKRect(8, 13, 25, 27), 5, 5),
                            new SkiaSharp.SKRoundRect(new SkiaSharp.SKRect(8, 9, 12.5f, 17), 2.2f, 2.2f),
                            new SkiaSharp.SKRoundRect(new SkiaSharp.SKRect(12.2f, 7.5f, 16.7f, 17), 2.2f, 2.2f),
                            new SkiaSharp.SKRoundRect(new SkiaSharp.SKRect(16.4f, 8, 20.9f, 17), 2.2f, 2.2f),
                            new SkiaSharp.SKRoundRect(new SkiaSharp.SKRect(20.6f, 9.5f, 25, 18), 2.2f, 2.2f),
                            new SkiaSharp.SKRoundRect(new SkiaSharp.SKRect(4.5f, 14.5f, 10, 21), 2.6f, 2.6f)
                        };
                        foreach (var part in parts) canvas.DrawRoundRect(part, ink);
                        foreach (var part in parts) canvas.DrawRoundRect(part, skin);
                        canvas.DrawLine(12.35f, 10, 12.35f, 15, line);
                        canvas.DrawLine(16.55f, 9.5f, 16.55f, 15, line);
                        canvas.DrawLine(20.75f, 10.5f, 20.75f, 15, line);
                        canvas.DrawLine(9.5f, 17.5f, 9.5f, 20.5f, line);
                    }
                    using (var image = SkiaSharp.SKImage.FromBitmap(bitmap))
                    using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                    using (var stream = new System.IO.MemoryStream(data.ToArray()))
                        _grabCursor = new Cursor(new Bitmap(stream), new PixelPoint(16, 16));
                }
            }
            catch (Exception) { _grabCursor = new Cursor(StandardCursorType.SizeAll); }
            return _grabCursor;
        }

        internal bool GrabCursorShownForProbe { get { return _space && ReferenceEquals(Cursor, _grabCursor) && _grabCursor != null; } }

        public void FitAll()
        {
            var map = Map;
            if (map == null || !map.HasImage || Bounds.Width <= 0) { _fitPending = true; return; }
            _fitPending = false;
            _globeZoom = 1;
            _globeKey = null;
            _scale = FitScale;
            _viewX = map.Width / 2.0 - Bounds.Width / 2 / _scale;
            _viewY = map.Height / 2.0 - Bounds.Height / 2 / _scale;
            _owner.ViewChanged();
            InvalidateVisual();
        }

        public void ZoomBy(double factor, Point anchor)
        {
            var map = Map;
            if (map == null || !map.HasImage) return;
            if (Globe)
            {
                _globeZoom = Math.Max(0.6, Math.Min(12, _globeZoom * factor));
                _globeKey = null;
            }
            else
            {
                var ix = _viewX + anchor.X / _scale;
                var iy = _viewY + anchor.Y / _scale;
                _scale = Math.Max(FitScale * 0.5, Math.Min(6, _scale * factor));
                _viewX = ix - anchor.X / _scale;
                _viewY = iy - anchor.Y / _scale;
                Clamp();
            }
            _fitPending = false;
            _owner.ViewChanged();
            InvalidateVisual();
        }

        public void CenterOn(double ix, double iy)
        {
            if (Globe)
            {
                var map = Map;
                _globeLon = Maps.Longitude(map, ix);
                _globeLat = Maps.Latitude(map, iy);
                _globeKey = null;
            }
            else
            {
                _viewX = ix - Bounds.Width / 2 / _scale;
                _viewY = iy - Bounds.Height / 2 / _scale;
                Clamp();
            }
            InvalidateVisual();
        }

        private void Clamp()
        {
            var map = Map;
            if (map == null) return;
            var spanX = Bounds.Width / _scale;
            var spanY = Bounds.Height / _scale;
            // La carte ne quitte jamais tout à fait la toile : il en reste au moins un cinquième.
            _viewX = Math.Max(-spanX * 0.8, Math.Min(map.Width - spanX * 0.2, _viewX));
            _viewY = Math.Max(-spanY * 0.8, Math.Min(map.Height - spanY * 0.2, _viewY));
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            if (_fitPending) FitAll();
            else { Clamp(); InvalidateVisual(); }
        }

        private Point ToScreen(double ix, double iy) { return new Point((ix - _viewX) * _scale, (iy - _viewY) * _scale); }
        private Pos ToImage(Point point) { return new Pos(_viewX + point.X / _scale, _viewY + point.Y / _scale); }

        private FormattedText Text(string text, double size, IBrush brush, FontWeight weight)
        {
            return new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, weight), size, brush);
        }

        private static Color Tint(string hex, Color fallback)
        {
            Color color;
            return Color.TryParse(hex, out color) ? color : fallback;
        }

        // ============================================================ rendu

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
            context.DrawRectangle(Globe ? new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1C)) : Chrome.WindowBg, null, bounds);
            var map = Map;
            if (map == null || !map.HasImage || _cache == null) return;
            if (Globe) { RenderGlobe(context, map); return; }

            DrawTiles(context, map, bounds);
            var imageRect = new Rect(ToScreen(0, 0), ToScreen(map.Width, map.Height));
            context.DrawRectangle(null, new Pen(Chrome.BorderStrong, 1), imageRect);
            if (_owner.SeasonHeatShown) DrawSeasonHeat(context, map);
            if (_owner.NightShown) DrawNight(context, map);
            if (map.WholeWorld && Maps.Fitted(map)) DrawPoles(context, map);

            var zoom = ZoomRatio;
            var selectedZone = _owner.SelectedZone;
            foreach (var zone in map.Zones)
            {
                if (zone.Points.Count < 2 || !_owner.ShowZones) continue;
                var chosen = zone == selectedZone;
                if (!chosen && !Maps.Visible(zone.Level, zoom)) continue;
                var color = Tint(zone.Color, Chrome.Accent.Color);
                var hovered = _hoverId == zone.Id;
                var geometry = new StreamGeometry();
                using (var sink = geometry.Open())
                {
                    sink.BeginFigure(ToScreen(zone.Points[0].X, zone.Points[0].Y), true);
                    for (var i = 1; i < zone.Points.Count; i++) sink.LineTo(ToScreen(zone.Points[i].X, zone.Points[i].Y));
                    sink.EndFigure(true);
                }
                context.DrawGeometry(new SolidColorBrush(color, chosen ? 0.34 : hovered ? 0.30 : 0.16), new Pen(new SolidColorBrush(color), chosen ? 2.6 : 1.8), geometry);
                var centre = Maps.Centroid(zone.Points);
                Label(context, zone.Name, ToScreen(centre.X, centre.Y), color, true);
            }
            if (selectedZone != null && _tool == Tool.Pan)
            {
                // Les poignées : un sommet par point, un demi-point au milieu de chaque côté (cliquer l'ajoute).
                var color = Tint(selectedZone.Color, Chrome.Accent.Color);
                for (var i = 0; i < selectedZone.Points.Count; i++)
                {
                    var p = selectedZone.Points[i];
                    var q = selectedZone.Points[(i + 1) % selectedZone.Points.Count];
                    context.DrawEllipse(new SolidColorBrush(Colors.White, 0.85), new Pen(new SolidColorBrush(color), 1), ToScreen((p.X + q.X) / 2, (p.Y + q.Y) / 2), 3.2, 3.2);
                }
                foreach (var point in selectedZone.Points)
                    context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(color), 2), ToScreen(point.X, point.Y), 5, 5);
            }

            var selectedMarker = _owner.SelectedMarker;
            foreach (var marker in map.Markers)
            {
                var chosen = marker == selectedMarker;
                if (!_owner.ShowMarkers || (!chosen && !Maps.Visible(marker.Level, zoom))) continue;
                var color = Tint(marker.Color, Chrome.Danger.Color);
                var at = ToScreen(marker.X, marker.Y);
                context.DrawEllipse(new SolidColorBrush(color), new Pen(Brushes.White, chosen ? 3 : 2), at, chosen ? 7 : 5.5, chosen ? 7 : 5.5);
                Label(context, marker.Name, new Point(at.X, at.Y + 15), color, false);
            }

            if (map.ScaleKm > 0) Segment(context, map.ScaleA, map.ScaleB, Chrome.Time, "Échelle : " + (map.ScaleNote ?? Maps.KmLabel(map.ScaleKm)));
            DrawDraft(context, map);
            DrawScaleBar(context, map);
        }

        private void DrawTiles(DrawingContext context, MapInfo map, Rect bounds)
        {
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            {
                // Le fond de secours : le niveau 0, toute la carte en une tuile.
                var floor = _cache.Get(map, 0, 0, 0, true);
                if (floor != null) context.DrawImage(floor, new Rect(ToScreen(0, 0), ToScreen(map.Width, map.Height)));
                var z = Maps.LevelFor(map, _scale);
                if (z == 0) return;
                var unit = Math.Pow(2, map.Levels - 1 - z);     // px d'image par px du niveau
                var tile = map.TileSize * unit;                  // px d'image par tuile
                var x0 = Math.Max(0, (int)Math.Floor(_viewX / tile));
                var y0 = Math.Max(0, (int)Math.Floor(_viewY / tile));
                var x1 = Math.Min(Maps.Columns(map, z) - 1, (int)Math.Floor((_viewX + bounds.Width / _scale) / tile));
                var y1 = Math.Min(Maps.Rows(map, z) - 1, (int)Math.Floor((_viewY + bounds.Height / _scale) / tile));
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        var left = x * tile;
                        var top = y * tile;
                        var right = Math.Min(map.Width, left + tile);
                        var bottom = Math.Min(map.Height, top + tile);
                        var a = ToScreen(left, top);
                        var b = ToScreen(right, bottom);
                        // Arrondi au pixel, un demi-pixel de recouvrement : pas de couture entre deux tuiles.
                        var dest = new Rect(Math.Floor(a.X), Math.Floor(a.Y), Math.Ceiling(b.X) - Math.Floor(a.X), Math.Ceiling(b.Y) - Math.Floor(a.Y));
                        var bitmap = _cache.Get(map, z, x, y);
                        if (bitmap != null) { context.DrawImage(bitmap, dest); continue; }
                        // Pas encore décodée : la même région, prise dans un niveau plus grossier déjà prêt.
                        for (var k = 1; z - k >= 1; k++)
                        {
                            var up = _cache.Peek(z - k, x >> k, y >> k);
                            if (up == null) continue;
                            var part = map.TileSize / Math.Pow(2, k);
                            var sx = (x - ((x >> k) << k)) * part;
                            var sy = (y - ((y >> k) << k)) * part;
                            var source = new Rect(sx, sy, Math.Min(part, up.PixelSize.Width - sx), Math.Min(part, up.PixelSize.Height - sy));
                            if (source.Width > 0 && source.Height > 0) context.DrawImage(up, source, dest);
                            break;
                        }
                    }
            }
        }

        /// <summary>La nuit sur le planisphère : tout ce qui est du côté sombre du terminateur.</summary>
        private void DrawNight(DrawingContext context, MapInfo map)
        {
            var declination = _owner.Declination;
            if (Math.Abs(declination) < 1e-4) declination = 1e-4; // aux équinoxes : un terminateur presque vertical
            var noon = Maps.NoonLongitude(_owner.DayFraction);
            const int steps = 180;
            var geometry = new StreamGeometry();
            using (var sink = geometry.Open())
            {
                // Soleil au nord : la nuit est SOUS la courbe (jusqu'au bord sud) ; au sud : au-dessus.
                // (Proportions gardées : le pôle peut être dans l'image, ou au-delà — la nuit s'arrête au bord.)
                var edge = Math.Max(0, Math.Min(map.Height, Maps.YOf(map, declination > 0 ? -Math.PI / 2 : Math.PI / 2)));
                sink.BeginFigure(ToScreen(0, edge), true);
                for (var i = 0; i <= steps; i++)
                {
                    var x = (double)i / steps * map.Width;
                    var latitude = Math.Atan(-Math.Cos(Maps.Longitude(map, x) - noon) / Math.Tan(declination));
                    sink.LineTo(ToScreen(x, Math.Max(0, Math.Min(map.Height, Maps.YOf(map, latitude)))));
                }
                sink.LineTo(ToScreen(map.Width, edge));
                sink.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(8, 12, 30), 0.52), new Pen(new SolidColorBrush(Color.FromRgb(8, 12, 30), 0.35), 3), geometry);
            // Le point où le soleil est au zénith.
            var sunX = Maps.XOf(map, noon);
            if (sunX < 0 || sunX > map.Width) return;
            var sun = ToScreen(sunX, Maps.YOf(map, _owner.Declination));
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x42), 0.35), null, sun, 13, 13);
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x42)), new Pen(Brushes.White, 1.5), sun, 6, 6);
        }

        // ------------------------------------------------------------ les saisons (lot 12)

        private const int HeatColumns = 24, HeatRows = 12;
        private readonly Dictionary<uint, IBrush> _heatBrushes = new Dictionary<uint, IBrush>();

        /// <summary>Un point de chaleur : un halo de la teinte de la saison, plein au centre, nul au bord.</summary>
        private IBrush HeatBrush(Color color, double strength)
        {
            var alpha = (byte)Math.Round(Math.Max(0, Math.Min(1, strength)) * 205);
            var key = ((uint)alpha << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            IBrush brush;
            if (_heatBrushes.TryGetValue(key, out brush)) return brush;
            var gradient = new RadialGradientBrush { Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Radius = 0.5 };
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 0.8), color.R, color.G, color.B), 0.45));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
            if (_heatBrushes.Count > 64) _heatBrushes.Clear();
            _heatBrushes[key] = gradient;
            return gradient;
        }

        /// <summary>La saison (teinte) et sa présence (0 à 1) à une latitude, au jour choisi.</summary>
        private bool SeasonAt(double latitude, out Color color, out double presence)
        {
            color = default(Color);
            presence = 0;
            var world = _owner.World;
            if (world == null) return false;
            var index = Maps.SeasonIndex(world, _owner.DayOfYear, _owner.YearDays, latitude < 0);
            if (index < 0) return false;
            presence = Maps.SeasonPresence(world, latitude);
            color = Tint(Astro.ColorOf(world.Seasons[index], index), Chrome.Accent.Color);
            return presence > 0.04;
        }

        /// <summary>LES POINTS DE CHALEUR DES SAISONS (lot 12, Rémi) : une grille de halos sur le planisphère,
        /// de la teinte de la saison qui règne là au jour choisi ; d'autant plus pâles que la saison
        /// s'efface (vers l'équateur), absents là où il n'y en a pas.</summary>
        private void DrawSeasonHeat(DrawingContext context, MapInfo map)
        {
            var stepY = Math.Abs(Maps.YOf(map, 0) - Maps.YOf(map, Math.PI / HeatRows)) * _scale;
            var radius = Math.Max(5, stepY * 0.47); // des points distincts, qui ne se chevauchent pas : la carte reste lisible dessous
            using (context.PushClip(new Rect(ToScreen(0, 0), ToScreen(map.Width, map.Height))))
                for (var row = 0; row < HeatRows; row++)
                {
                    var latitude = Math.PI / 2 - (row + 0.5) * Math.PI / HeatRows;
                    Color color;
                    double presence;
                    if (!SeasonAt(latitude, out color, out presence)) continue;
                    var y = Maps.YOf(map, latitude);
                    if (y < -map.Height * 0.1 || y > map.Height * 1.1) continue;
                    var brush = HeatBrush(color, presence);
                    for (var column = 0; column < HeatColumns; column++)
                    {
                        var longitude = -Math.PI + (column + 0.5) * 2 * Math.PI / HeatColumns;
                        var x = Maps.XOf(map, longitude);
                        if (x < 0 || x > map.Width) continue;
                        var at = ToScreen(x, y);
                        if (at.X < -radius || at.Y < -radius || at.X > Bounds.Width + radius || at.Y > Bounds.Height + radius) continue;
                        context.DrawEllipse(brush, null, at, radius, radius);
                    }
                }
        }

        /// <summary>Proportions gardées : les pôles et l'équateur, en pointillés — on voit où le monde commence.</summary>
        private void DrawPoles(DrawingContext context, MapInfo map)
        {
            var pen = new Pen(new SolidColorBrush(Colors.White, 0.85), 1.4) { DashStyle = new DashStyle(new double[] { 5, 4 }, 0) };
            var shadow = new Pen(new SolidColorBrush(Colors.Black, 0.45), 3);
            var lines = new[] { Math.PI / 2, 0, -Math.PI / 2 };
            var names = new[] { "Pôle nord", "Équateur", "Pôle sud" };
            for (var i = 0; i < lines.Length; i++)
            {
                var y = Maps.YOf(map, lines[i]);
                if (y < -0.5 || y > map.Height + 0.5) continue;
                var a = ToScreen(0, y);
                var b = ToScreen(map.Width, y);
                context.DrawLine(shadow, a, b);
                context.DrawLine(pen, a, b);
                var text = Text(names[i], 10.5, Brushes.White, FontWeight.SemiBold);
                var box = new Rect(Math.Max(4, a.X + 6), a.Y - text.Height - 5, text.Width + 10, text.Height + 3);
                context.DrawRectangle(new SolidColorBrush(Colors.Black, 0.55), null, box, 4, 4);
                context.DrawText(text, new Point(box.X + 5, box.Y + 1));
            }
        }

        private void Label(DrawingContext context, string name, Point at, Color color, bool centred)
        {
            if (string.IsNullOrEmpty(name)) return;
            var text = Text(name, 12, Brushes.White, FontWeight.SemiBold);
            var box = new Rect(at.X - text.Width / 2 - 6, centred ? at.Y - text.Height / 2 - 2 : at.Y - 2, text.Width + 12, text.Height + 4);
            if (box.Right < 0 || box.X > Bounds.Width || box.Bottom < 0 || box.Y > Bounds.Height) return;
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(color.R * 0.55), (byte)(color.G * 0.55), (byte)(color.B * 0.55)), 0.82), null, box, 5, 5);
            context.DrawText(text, new Point(box.X + 6, box.Y + 2));
        }

        private void Segment(DrawingContext context, Pos a, Pos b, IBrush brush, string caption)
        {
            var p = ToScreen(a.X, a.Y);
            var q = ToScreen(b.X, b.Y);
            context.DrawLine(new Pen(Brushes.White, 4), p, q);
            context.DrawLine(new Pen(brush, 2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, p, q);
            context.DrawEllipse(brush, new Pen(Brushes.White, 1.5), p, 4.5, 4.5);
            context.DrawEllipse(brush, new Pen(Brushes.White, 1.5), q, 4.5, 4.5);
            if (string.IsNullOrEmpty(caption)) return;
            var text = Text(caption, 11.5, Brushes.White, FontWeight.SemiBold);
            var mid = new Point((p.X + q.X) / 2, (p.Y + q.Y) / 2);
            var box = new Rect(mid.X - text.Width / 2 - 6, mid.Y - text.Height - 12, text.Width + 12, text.Height + 5);
            context.DrawRectangle(brush, null, box, 5, 5);
            context.DrawText(text, new Point(box.X + 6, box.Y + 2));
        }

        private void DrawDraft(DrawingContext context, MapInfo map)
        {
            var pen = new Pen(Chrome.Accent, 2);
            if (_tool == Tool.Zone && _draft.Count > 0)
            {
                for (var i = 1; i < _draft.Count; i++) context.DrawLine(pen, ToScreen(_draft[i - 1].X, _draft[i - 1].Y), ToScreen(_draft[i].X, _draft[i].Y));
                if (_cursor != null) context.DrawLine(new Pen(Chrome.Accent, 1.5) { DashStyle = DashStyle.Dash }, ToScreen(_draft[_draft.Count - 1].X, _draft[_draft.Count - 1].Y), _cursor.Value);
                for (var i = 0; i < _draft.Count; i++)
                    context.DrawEllipse(i == 0 ? Chrome.Accent : (IBrush)Brushes.White, new Pen(Chrome.Accent, 2), ToScreen(_draft[i].X, _draft[i].Y), i == 0 ? 6 : 4.5, i == 0 ? 6 : 4.5);
            }
            if ((_tool == Tool.Scale || _tool == Tool.Measure) && _draft.Count == 1 && _cursor != null)
            {
                var b = ToImage(_cursor.Value);
                var caption = _tool == Tool.Measure && Maps.CanMeasure(map, _owner.World) ? Maps.KmLabel(Maps.Distance(map, _owner.World, _draft[0], b)) : null;
                Segment(context, _draft[0], b, Chrome.Accent, caption);
            }
            if (_measureA != null && _measureB != null)
            {
                var km = Maps.Distance(map, _owner.World, _measureA.Value, _measureB.Value);
                Segment(context, _measureA.Value, _measureB.Value, Chrome.Accent, Maps.KmLabel(km));
            }
        }

        /// <summary>La barre d'échelle, en bas à gauche : une longueur ronde, pour une carte plane qui a une échelle.</summary>
        private void DrawScaleBar(DrawingContext context, MapInfo map)
        {
            var perPixel = Maps.KmPerPixel(map);
            if (perPixel <= 0) return;
            var kmPerScreen = perPixel / _scale;
            var target = kmPerScreen * 130;
            var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1e-9, target))));
            var nice = magnitude;
            foreach (var step in new[] { 1.0, 2, 5, 10 }) if (step * magnitude <= target) nice = step * magnitude;
            var width = nice / kmPerScreen;
            var y = Bounds.Height - 22;
            var text = Text(Maps.KmLabel(nice), 11, Chrome.Ink, FontWeight.SemiBold);
            context.DrawRectangle(new SolidColorBrush(Chrome.WindowBg.Color, 0.86), null, new Rect(10, y - text.Height - 8, Math.Max(width, text.Width) + 16, text.Height + 20), 6, 6);
            context.DrawText(text, new Point(18, y - text.Height - 4));
            var pen = new Pen(Chrome.Ink, 2);
            context.DrawLine(pen, new Point(18, y + 4), new Point(18 + width, y + 4));
            context.DrawLine(pen, new Point(18, y), new Point(18, y + 8));
            context.DrawLine(pen, new Point(18 + width, y), new Point(18 + width, y + 8));
        }

        // ============================================================ le globe

        private double GlobeRadius(int side) { return side / 2.0 * 0.92 * _globeZoom; }

        private int GlobeSide { get { return (int)Math.Max(64, Math.Min(640, Math.Min(Bounds.Width, Bounds.Height))); } }

        private void RenderGlobe(DrawingContext context, MapInfo map)
        {
            int tw, th;
            var texture = _cache.Texture(map, out tw, out th);
            var side = GlobeSide;
            var dest = new Rect((Bounds.Width - side) / 2, (Bounds.Height - side) / 2, side, side);
            if (texture == null)
            {
                var wait = Text("Le globe se prépare…", 13, Brushes.White, FontWeight.Normal);
                context.DrawText(wait, new Point((Bounds.Width - wait.Width) / 2, (Bounds.Height - wait.Height) / 2));
                return;
            }
            var night = _owner.NightShown;
            var key = side + "|" + _globeLon.ToString("R", CultureInfo.InvariantCulture) + "|" + _globeLat.ToString("R", CultureInfo.InvariantCulture) + "|" + _globeZoom.ToString("R", CultureInfo.InvariantCulture)
                + "|" + (night ? _owner.Declination.ToString("R", CultureInfo.InvariantCulture) + "/" + _owner.DayFraction.ToString("R", CultureInfo.InvariantCulture) : "-")
                + "|" + map.Layout + map.PoleNorth.ToString("R", CultureInfo.InvariantCulture) + "/" + map.PoleSouth.ToString("R", CultureInfo.InvariantCulture);
            if (_globeBitmap == null || _globeBitmap.PixelSize.Width != side || key != _globeKey)
            {
                PaintGlobe(texture, tw, th, side, night);
                _globeKey = key;
            }
            context.DrawImage(_globeBitmap, dest);

            // Les zones et les repères, reportés sur la sphère (la face visible seulement).
            var zoom = ZoomRatio;
            var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
            var radius = GlobeRadius(side);
            using (context.PushClip(dest))
            {
                if (_owner.SeasonHeatShown)
                    for (var row = 0; row < HeatRows; row++)
                    {
                        var latitude = Math.PI / 2 - (row + 0.5) * Math.PI / HeatRows;
                        Color tint;
                        double presence;
                        if (!SeasonAt(latitude, out tint, out presence)) continue;
                        var brush = HeatBrush(tint, presence);
                        for (var column = 0; column < HeatColumns; column++)
                        {
                            Point spot;
                            double depth;
                            var longitude = -Math.PI + (column + 0.5) * 2 * Math.PI / HeatColumns;
                            if (!ProjectAngles(longitude, latitude, centre, radius, out spot, out depth)) continue;
                            var size = radius * 0.123 * (0.35 + 0.65 * depth);
                            context.DrawEllipse(brush, null, spot, size, size);
                        }
                    }
                foreach (var zone in map.Zones)
                {
                    if (!_owner.ShowZones || zone.Points.Count < 2 || !Maps.Visible(zone.Level, zoom)) continue;
                    var pen = new Pen(new SolidColorBrush(Tint(zone.Color, Chrome.Accent.Color)), 2);
                    for (var i = 0; i < zone.Points.Count; i++)
                    {
                        Point p, q;
                        var a = zone.Points[i];
                        var b = zone.Points[(i + 1) % zone.Points.Count];
                        if (Project(map, a.X, a.Y, centre, radius, out p) && Project(map, b.X, b.Y, centre, radius, out q)) context.DrawLine(pen, p, q);
                    }
                    Point at;
                    var c = Maps.Centroid(zone.Points);
                    if (Project(map, c.X, c.Y, centre, radius, out at)) Label(context, zone.Name, at, Tint(zone.Color, Chrome.Accent.Color), true);
                }
                foreach (var marker in map.Markers)
                {
                    Point at;
                    if (!_owner.ShowMarkers || !Maps.Visible(marker.Level, zoom) || !Project(map, marker.X, marker.Y, centre, radius, out at)) continue;
                    var color = Tint(marker.Color, Chrome.Danger.Color);
                    context.DrawEllipse(new SolidColorBrush(color), new Pen(Brushes.White, 2), at, 5, 5);
                    Label(context, marker.Name, new Point(at.X, at.Y + 14), color, false);
                }
            }
        }

        /// <summary>Un point de l'image sur le globe ; faux s'il est sur la face cachée.</summary>
        private bool Project(MapInfo map, double ix, double iy, Point centre, double radius, out Point at)
        {
            double depth;
            return ProjectAngles(Maps.Longitude(map, ix), Maps.Latitude(map, iy), centre, radius, out at, out depth);
        }

        private bool ProjectAngles(double longitude, double lat, Point centre, double radius, out Point at, out double depth)
        {
            var lon = longitude - _globeLon;
            double sinP = Math.Sin(_globeLat), cosP = Math.Cos(_globeLat);
            depth = sinP * Math.Sin(lat) + cosP * Math.Cos(lat) * Math.Cos(lon);
            at = new Point(centre.X + radius * Math.Cos(lat) * Math.Sin(lon), centre.Y - radius * (cosP * Math.Sin(lat) - sinP * Math.Cos(lat) * Math.Cos(lon)));
            return depth > 0.02;
        }

        private void PaintGlobe(int[] texture, int tw, int th, int side, bool night)
        {
            if (_globeBuffer == null || _globeBuffer.Length != side * side) _globeBuffer = new int[side * side];
            if (_globeBitmap == null || _globeBitmap.PixelSize.Width != side)
                _globeBitmap = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            var buffer = _globeBuffer;
            var radius = GlobeRadius(side);
            double sinP = Math.Sin(_globeLat), cosP = Math.Cos(_globeLat);
            var declination = _owner.Declination;
            double sinD = Math.Sin(declination), cosD = Math.Cos(declination);
            var noon = Maps.NoonLongitude(_owner.DayFraction);
            var half = side / 2.0;
            const double twoPi = 2 * Math.PI;
            // De l'angle au pixel de la texture (lot 12) : l'image étirée sur tout le monde, ou —
            // proportions gardées — posée entre ses pôles ; hors de l'image, la mer nue.
            var map = Map;
            var fitted = Maps.Fitted(map);
            double ax = 0, bx = 0, ay = 0, by = 0;
            if (fitted)
            {
                var perPixel = Maps.RadiansPerPixel(map);
                ax = tw / (map.Width * perPixel);
                bx = tw / 2.0;
                ay = -th / (map.Height * perPixel);
                by = th * map.PoleNorth + Math.PI / 2 * th / (map.Height * perPixel);
            }
            const int bare = (0x2E << 16) | (0x3A << 8) | 0x4A;
            for (var py = 0; py < side; py++)
            {
                var y = (half - py - 0.5) / radius;
                var row = py * side;
                for (var px = 0; px < side; px++)
                {
                    var x = (px + 0.5 - half) / radius;
                    var d = x * x + y * y;
                    if (d > 1) { buffer[row + px] = 0; continue; }
                    var z = Math.Sqrt(1 - d);
                    var sinLat = z * sinP + y * cosP;
                    if (sinLat > 1) sinLat = 1; else if (sinLat < -1) sinLat = -1;
                    var lat = Math.Asin(sinLat);
                    var lon = _globeLon + Math.Atan2(x, z * cosP - y * sinP);
                    int color;
                    if (fitted)
                    {
                        var around = lon - twoPi * Math.Round(lon / twoPi);
                        var fx = ax * around + bx;
                        var fy = ay * lat + by;
                        color = fx < 0 || fy < 0 || fx >= tw || fy >= th ? bare : texture[(int)fy * tw + (int)fx];
                    }
                    else
                    {
                        var u = lon / twoPi + 0.5;
                        u -= Math.Floor(u);
                        var tx = (int)(u * tw);
                        if (tx >= tw) tx = tw - 1;
                        var ty = (int)((0.5 - lat / Math.PI) * th);
                        if (ty < 0) ty = 0; else if (ty >= th) ty = th - 1;
                        color = texture[ty * tw + tx];
                    }
                    // L'ombre du bord (la sphère se lit), puis la nuit.
                    var light = 0.58 + 0.42 * z;
                    if (night)
                    {
                        var sun = sinLat * sinD + Math.Sqrt(1 - sinLat * sinLat) * cosD * Math.Cos(lon - noon);
                        light *= sun > 0.1 ? 1 : sun < -0.1 ? 0.3 : 0.3 + 0.7 * (sun + 0.1) / 0.2;
                    }
                    // Le dernier pixel du disque s'estompe : pas d'escalier au bord.
                    var alpha = Math.Min(1, (1 - Math.Sqrt(d)) * radius);
                    light *= alpha;
                    var blue = (int)((color & 0xFF) * light);
                    var green = (int)(((color >> 8) & 0xFF) * light);
                    var red = (int)(((color >> 16) & 0xFF) * light);
                    buffer[row + px] = ((int)(alpha * 255) << 24) | (red << 16) | (green << 8) | blue;
                }
            }
            using (var frame = _globeBitmap.Lock())
            {
                if (frame.RowBytes == side * 4) Marshal.Copy(buffer, 0, frame.Address, buffer.Length);
                else for (var py = 0; py < side; py++) Marshal.Copy(buffer, py * side, frame.Address + py * frame.RowBytes, side);
            }
        }

        // ============================================================ la souris

        private const double MarkerReach = 10, VertexReach = 8;

        private MapMarker MarkerAt(Point point)
        {
            var map = Map;
            var zoom = ZoomRatio;
            if (!_owner.ShowMarkers) return null;
            for (var i = map.Markers.Count - 1; i >= 0; i--)
            {
                var marker = map.Markers[i];
                if (marker != _owner.SelectedMarker && !Maps.Visible(marker.Level, zoom)) continue;
                var at = ToScreen(marker.X, marker.Y);
                if (Math.Abs(at.X - point.X) <= MarkerReach && Math.Abs(at.Y - point.Y) <= MarkerReach) return marker;
            }
            return null;
        }

        private MapZone ZoneAt(Point point)
        {
            var map = Map;
            var zoom = ZoomRatio;
            var image = ToImage(point);
            if (!_owner.ShowZones) return null;
            for (var i = map.Zones.Count - 1; i >= 0; i--)
            {
                var zone = map.Zones[i];
                if (zone != _owner.SelectedZone && !Maps.Visible(zone.Level, zoom)) continue;
                if (Maps.Contains(zone.Points, image.X, image.Y)) return zone;
            }
            return null;
        }

        private int VertexAt(MapZone zone, Point point)
        {
            if (zone == null) return -1;
            for (var i = 0; i < zone.Points.Count; i++)
            {
                var at = ToScreen(zone.Points[i].X, zone.Points[i].Y);
                if (Math.Abs(at.X - point.X) <= VertexReach && Math.Abs(at.Y - point.Y) <= VertexReach) return i;
            }
            return -1;
        }

        private int MidpointAt(MapZone zone, Point point)
        {
            if (zone == null) return -1;
            for (var i = 0; i < zone.Points.Count; i++)
            {
                var p = zone.Points[i];
                var q = zone.Points[(i + 1) % zone.Points.Count];
                var at = ToScreen((p.X + q.X) / 2, (p.Y + q.Y) / 2);
                if (Math.Abs(at.X - point.X) <= 6 && Math.Abs(at.Y - point.Y) <= 6) return i;
            }
            return -1;
        }

        private Pos InsideImage(Pos point)
        {
            var map = Map;
            return new Pos(Math.Max(0, Math.Min(map.Width, point.X)), Math.Max(0, Math.Min(map.Height, point.Y)));
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (Map == null || !Map.HasImage) return;
            var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            ZoomBy(Math.Pow(1.25, delta), e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var map = Map;
            if (map == null || !map.HasImage) return;
            Focus();
            var point = e.GetPosition(this);
            var properties = e.GetCurrentPoint(this).Properties;
            if (Globe)
            {
                if (!properties.IsLeftButtonPressed) return;
                _panning = true;
                _last = point;
                e.Pointer.Capture(this);
                return;
            }
            // Espace tenu : la main promène la carte, et rien d'autre — ni sommet, ni choix (lot 12).
            if (_space)
            {
                if (properties.IsLeftButtonPressed) { BeginGrab(point); e.Pointer.Capture(this); }
                e.Handled = true;
                return;
            }
            var image = InsideImage(ToImage(point));
            if (properties.IsRightButtonPressed)
            {
                // Clic droit sur un sommet de la zone choisie : il part (une zone garde trois points).
                var zone = _owner.SelectedZone;
                var vertex = _tool == Tool.Pan ? VertexAt(zone, point) : -1;
                if (vertex >= 0 && zone.Points.Count > 3)
                {
                    var before = map.Clone();
                    zone.Points.RemoveAt(vertex);
                    _owner.Commit(before);
                }
                else if (_tool == Tool.Zone && _draft.Count > 0) { _draft.RemoveAt(_draft.Count - 1); InvalidateVisual(); }
                e.Handled = true;
                return;
            }
            if (!properties.IsLeftButtonPressed) return;
            switch (_tool)
            {
                case Tool.Zone:
                    if (e.ClickCount >= 2 && _draft.Count >= 3) { _owner.FinishZone(new List<Pos>(_draft)); return; }
                    if (_draft.Count >= 3)
                    {
                        var first = ToScreen(_draft[0].X, _draft[0].Y);
                        if (Math.Abs(first.X - point.X) <= 9 && Math.Abs(first.Y - point.Y) <= 9) { _owner.FinishZone(new List<Pos>(_draft)); return; }
                    }
                    _draft.Add(image);
                    InvalidateVisual();
                    return;
                case Tool.Marker:
                    _owner.FinishMarker(image);
                    return;
                case Tool.Scale:
                case Tool.Measure:
                    if (_draft.Count == 0) { _measureA = _measureB = null; _draft.Add(image); InvalidateVisual(); return; }
                    var from = _draft[0];
                    _draft.Clear();
                    if (_tool == Tool.Scale) _owner.FinishScale(from, image);
                    else { _measureA = from; _measureB = image; _owner.Measured(from, image); InvalidateVisual(); }
                    return;
            }
            // L'outil « Déplacer » : une poignée de la zone choisie, un repère, une zone — ou le fond.
            var chosenZone = _owner.SelectedZone;
            var handle = VertexAt(chosenZone, point);
            if (handle < 0)
            {
                var middle = MidpointAt(chosenZone, point);
                if (middle >= 0)
                {
                    _dragBefore = map.Clone();
                    chosenZone.Points.Insert(middle + 1, image);
                    handle = middle + 1;
                    _moved = true;
                }
            }
            if (handle >= 0)
            {
                if (_dragBefore == null) _dragBefore = map.Clone();
                _dragVertex = handle;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }
            var marker = MarkerAt(point);
            if (marker != null)
            {
                if (e.ClickCount >= 2) { _owner.Select(null, marker); _owner.OpenOrEdit(null, marker); return; }
                _owner.Select(null, marker);
                _dragBefore = map.Clone();
                _dragMarker = true;
                _moved = false;
                e.Pointer.Capture(this);
                return;
            }
            var hit = ZoneAt(point);
            if (hit != null && e.ClickCount >= 2) { _owner.Select(hit, null); _owner.OpenOrEdit(hit, null); return; }
            _owner.Select(hit, null);
            _panning = true;
            _moved = false;
            _last = point;
            e.Pointer.Capture(this);
        }

        private void BeginGrab(Point point)
        {
            _panning = true;
            _moved = true;
            _last = point;
        }

        internal void GrabForProbe(Point from, Point to)
        {
            BeginGrab(from);
            _viewX -= (to.X - from.X) / _scale;
            _viewY -= (to.Y - from.Y) / _scale;
            Clamp();
            _fitPending = false;
            _panning = false;
            InvalidateVisual();
        }

        internal Pos ViewOriginForProbe { get { return new Pos(_viewX, _viewY); } }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var map = Map;
            if (map == null || !map.HasImage) return;
            var point = e.GetPosition(this);
            _cursor = point;
            if (Globe)
            {
                if (!_panning) return;
                var radius = GlobeRadius(GlobeSide);
                _globeLon -= (point.X - _last.X) / radius;
                _globeLat = Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, _globeLat + (point.Y - _last.Y) / radius));
                _last = point;
                _globeKey = null;
                InvalidateVisual();
                return;
            }
            if (_dragVertex >= 0)
            {
                var zone = _owner.SelectedZone;
                if (zone != null && _dragVertex < zone.Points.Count) { zone.Points[_dragVertex] = InsideImage(ToImage(point)); _moved = true; }
                InvalidateVisual();
                return;
            }
            if (_dragMarker)
            {
                var marker = _owner.SelectedMarker;
                var at = InsideImage(ToImage(point));
                if (marker != null && (Math.Abs(ToScreen(marker.X, marker.Y).X - point.X) > 2 || Math.Abs(ToScreen(marker.X, marker.Y).Y - point.Y) > 2 || _moved)) { marker.X = at.X; marker.Y = at.Y; _moved = true; }
                InvalidateVisual();
                return;
            }
            if (_panning)
            {
                if (Math.Abs(point.X - _last.X) + Math.Abs(point.Y - _last.Y) > 0.5) _moved = true;
                _viewX -= (point.X - _last.X) / _scale;
                _viewY -= (point.Y - _last.Y) / _scale;
                _last = point;
                Clamp();
                _fitPending = false;
                InvalidateVisual();
                return;
            }
            if (_space) return;
            if (_tool != Tool.Pan) { InvalidateVisual(); return; }
            // Le survol : la zone ou le repère sous le curseur s'éclaire ; une main sur ce qui se clique.
            var over = MarkerAt(point);
            var overZone = over == null ? ZoneAt(point) : null;
            var id = over != null ? over.Id : overZone != null ? overZone.Id : null;
            var onHandle = VertexAt(_owner.SelectedZone, point) >= 0 || MidpointAt(_owner.SelectedZone, point) >= 0;
            Cursor = onHandle ? new Cursor(StandardCursorType.SizeAll) : id != null ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            if (id == _hoverId) return;
            _hoverId = id;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Pointer.Capture(null);
            if (_dragVertex >= 0 || _dragMarker)
            {
                var before = _dragBefore;
                var changed = _moved;
                _dragVertex = -1;
                _dragMarker = false;
                _dragBefore = null;
                _moved = false;
                if (changed && before != null) _owner.Commit(before);
                return;
            }
            _panning = false;
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _cursor = null;
            if (_space && !_panning) SetSpace(false);
            if (_hoverId != null || _tool != Tool.Pan) { _hoverId = null; InvalidateVisual(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Map == null) return;
            if (e.Key == Key.Escape && (_tool != Tool.Pan || _measureA != null))
            {
                _owner.ChooseTool(Tool.Pan);
                e.Handled = true;
            }
            else if ((e.Key == Key.Enter || e.Key == Key.Return) && _tool == Tool.Zone && _draft.Count >= 3)
            {
                _owner.FinishZone(new List<Pos>(_draft));
                e.Handled = true;
            }
            else if (e.Key == Key.Back && _tool == Tool.Zone && _draft.Count > 0)
            {
                _draft.RemoveAt(_draft.Count - 1);
                InvalidateVisual();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && _tool == Tool.Pan && (_owner.SelectedZone != null || _owner.SelectedMarker != null))
            {
                _owner.DeleteSelection();
                e.Handled = true;
            }
        }

        // ============================================================ la sonde

        internal Point ScreenOfForProbe(double ix, double iy) { return ToScreen(ix, iy); }
        internal int LevelForProbe { get { return Map == null ? 0 : Maps.LevelFor(Map, _scale); } }
        internal MapZone ZoneHitForProbe(Point point) { return ZoneAt(point); }
        internal bool GlobePaintedForProbe { get { return _globeBitmap != null && _globeKey != null; } }
        internal void RotateGlobeForProbe(double lon, double lat) { _globeLon = lon; _globeLat = lat; _globeKey = null; InvalidateVisual(); }
        internal void DraftForProbe(Pos point) { _draft.Add(point); }
        internal int GlobePixelForProbe(int x, int y) { return _globeBuffer == null ? 0 : _globeBuffer[y * GlobeSide + x]; }
        internal void PaintGlobeForProbe()
        {
            int tw, th;
            var texture = _cache == null ? null : _cache.Texture(Map, out tw, out th, true);
            if (texture == null) return;
            _cache.Texture(Map, out tw, out th, true);
            PaintGlobe(texture, tw, th, GlobeSide, _owner.NightShown);
            _globeKey = "probe";
        }
    }
}

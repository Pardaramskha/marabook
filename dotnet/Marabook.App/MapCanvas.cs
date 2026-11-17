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
            EnsureSkyTimer(false);
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
            if (map.WholeWorld && _owner.GuidesShown) DrawGuides(context, map);
            if (_owner.NightShown) DrawNight(context, map);

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
            DrawLatLon(context);
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

        /// <summary>La teinte de la saison d'un hémisphère au jour choisi, FONDUE vers la saison voisine
        /// (lot 13, Rémi : « un dégradé progressif des couleurs vers celles des autres saisons »).</summary>
        internal bool SeasonTint(bool south, out Color color)
        {
            color = default(Color);
            var world = _owner.World;
            int a, b;
            double share;
            if (world == null || !Maps.SeasonMix(world, _owner.DayOfYear, _owner.YearDays, south, out a, out b, out share)) return false;
            var from = Tint(Astro.ColorOf(world.Seasons[a], a), Chrome.Accent.Color);
            var to = Tint(Astro.ColorOf(world.Seasons[b], b), Chrome.Accent.Color);
            color = Color.FromRgb((byte)Math.Round(from.R + (to.R - from.R) * share), (byte)Math.Round(from.G + (to.G - from.G) * share), (byte)Math.Round(from.B + (to.B - from.B) * share));
            return true;
        }

        private const double HeatStrength = 0.42;   // l'opacité de la saison là où elle est entière

        /// <summary>LES SAISONS SUR LA CARTE (lot 13, Rémi : « plutôt qu'une multitude de points, des masses
        /// unies dégradées ») : un voile continu de la teinte de la saison — celle du nord au nord, celle du
        /// sud au sud —, plein au-delà des tropiques, qui s'éteint vers l'équateur où il n'y a pas de saison.</summary>
        private void DrawSeasonHeat(DrawingContext context, MapInfo map)
        {
            Color north, south;
            var world = _owner.World;
            if (!SeasonTint(false, out north) || !SeasonTint(true, out south)) return;
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            const int steps = 36;
            for (var i = 0; i <= steps; i++)
            {
                var latitude = Math.PI / 2 - i * Math.PI / steps;
                var tint = latitude >= 0 ? north : south;
                var alpha = (byte)Math.Round(Maps.SeasonPresence(world, latitude) * HeatStrength * 255);
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, tint.R, tint.G, tint.B), (double)i / steps));
            }
            // Le voile va d'un pôle à l'autre et d'un bout du monde à l'autre ; l'image le rogne.
            var veil = new Rect(ToScreen(Maps.XOf(map, -Math.PI), Maps.YOf(map, Math.PI / 2)), ToScreen(Maps.XOf(map, Math.PI), Maps.YOf(map, -Math.PI / 2)));
            using (context.PushClip(new Rect(ToScreen(0, 0), ToScreen(map.Width, map.Height))))
                context.DrawRectangle(brush, null, veil);
        }

        // ------------------------------------------------------------ les repères de navigation (lot 13)

        /// <summary>Les marqueurs du monde (équateur, tropiques, cercles polaires, ou ceux de l'auteur) : un
        /// parallèle en pointillés chacun, nommé. Proportions gardées : les deux pôles aussi, là où ils sont.</summary>
        private void DrawGuides(DrawingContext context, MapInfo map)
        {
            var guides = _owner.Guides;
            var shadow = new Pen(new SolidColorBrush(Colors.Black, 0.4), 3);
            for (var i = 0; i < guides.Count; i++)
                GuideLine(context, map, guides[i].Latitude * Math.PI / 180, guides[i].Name + "  " + Astro.LatitudeLabel(guides[i].Latitude), Tint(Astro.ColorOf(guides[i], i), Colors.White), shadow);
            if (!Maps.Fitted(map)) return;
            GuideLine(context, map, Math.PI / 2, "Pôle nord", Colors.White, shadow);
            GuideLine(context, map, -Math.PI / 2, "Pôle sud", Colors.White, shadow);
        }

        private void GuideLine(DrawingContext context, MapInfo map, double latitude, string name, Color color, Pen shadow)
        {
            var y = Maps.YOf(map, latitude);
            if (y < -0.5 || y > map.Height + 0.5) return;
            var a = ToScreen(0, y);
            var b = ToScreen(map.Width, y);
            if (a.Y < -20 || a.Y > Bounds.Height + 20) return;
            context.DrawLine(shadow, a, b);
            context.DrawLine(new Pen(new SolidColorBrush(color, 0.95), 1.5) { DashStyle = new DashStyle(new double[] { 5, 4 }, 0) }, a, b);
            GuideLabel(context, name, new Point(Math.Max(4, a.X + 6), a.Y - 19), color);
        }

        private void GuideLabel(DrawingContext context, string name, Point at, Color color)
        {
            var text = Text(name, 10.5, Brushes.White, FontWeight.SemiBold);
            var box = new Rect(at.X, at.Y, text.Width + 16, text.Height + 3);
            context.DrawRectangle(new SolidColorBrush(Colors.Black, 0.6), null, box, 4, 4);
            context.DrawEllipse(new SolidColorBrush(color), null, new Point(box.X + 6.5, box.Y + box.Height / 2), 2.6, 2.6);
            context.DrawText(text, new Point(box.X + 12, box.Y + 1));
        }

        /// <summary>Les mêmes marqueurs sur le globe : chaque parallèle suit la sphère, la face visible seulement.</summary>
        private void DrawGlobeGuides(DrawingContext context, Point centre, double radius)
        {
            var guides = _owner.Guides;
            for (var i = 0; i < guides.Count; i++)
            {
                var latitude = guides[i].Latitude * Math.PI / 180;
                var color = Tint(Astro.ColorOf(guides[i], i), Colors.White);
                var pen = new Pen(new SolidColorBrush(color, 0.95), 1.4) { DashStyle = new DashStyle(new double[] { 5, 4 }, 0) };
                Point previous = default(Point);
                var has = false;
                for (var step = 0; step <= 120; step++)
                {
                    Point at;
                    double depth;
                    var visible = ProjectAngles(-Math.PI + step * 2 * Math.PI / 120, latitude, centre, radius, out at, out depth);
                    if (visible && has) context.DrawLine(pen, previous, at);
                    previous = at;
                    has = visible;
                }
                Point label;
                double front;
                if (ProjectAngles(_globeLon, latitude, centre, radius, out label, out front) && front > 0.25)
                    GuideLabel(context, guides[i].Name, new Point(label.X + 6, label.Y - 19), color);
            }
        }

        // ------------------------------------------------------------ la latitude et la longitude du curseur (lot 13)

        /// <summary>Où le curseur pointe, sur le monde : faux hors de la carte (ou du disque du globe), ou
        /// si la carte n'est pas celle d'un monde entier — elle n'a alors ni pôles ni équateur.</summary>
        internal bool GeoAt(Point point, out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            var map = Map;
            if (map == null || !map.HasImage || !map.WholeWorld) return false;
            if (Globe)
            {
                var radius = GlobeRadius(GlobeSide);
                var x = (point.X - Bounds.Width / 2) / radius;
                var y = (Bounds.Height / 2 - point.Y) / radius;
                var d = x * x + y * y;
                if (d > 1) return false;
                var z = Math.Sqrt(1 - d);
                double sinP = Math.Sin(_globeLat), cosP = Math.Cos(_globeLat);
                latitude = Math.Asin(Math.Max(-1, Math.Min(1, z * sinP + y * cosP)));
                longitude = _globeLon + Math.Atan2(x, z * cosP - y * sinP);
                longitude -= 2 * Math.PI * Math.Round(longitude / (2 * Math.PI));
                return true;
            }
            var image = ToImage(point);
            if (image.X < 0 || image.Y < 0 || image.X > map.Width || image.Y > map.Height) return false;
            var raw = Maps.YOf(map, Math.PI / 2);
            if (image.Y < raw - 0.5 || image.Y > Maps.YOf(map, -Math.PI / 2) + 0.5) return false; // au-delà d'un pôle : hors du monde
            latitude = Maps.Latitude(map, image.Y);
            longitude = Maps.Longitude(map, image.X);
            return Math.Abs(longitude) <= Math.PI + 1e-9;
        }

        private void DrawLatLon(DrawingContext context)
        {
            if (_cursor == null || !_owner.LatLonShown) return;
            double latitude, longitude;
            if (!GeoAt(_cursor.Value, out latitude, out longitude)) return;
            var at = _cursor.Value;
            var cross = new Pen(new SolidColorBrush(Colors.White, 0.75), 1);
            var dark = new Pen(new SolidColorBrush(Colors.Black, 0.35), 3);
            foreach (var pen in new[] { dark, cross })
            {
                context.DrawLine(pen, new Point(at.X - 14, at.Y), new Point(at.X - 4, at.Y));
                context.DrawLine(pen, new Point(at.X + 4, at.Y), new Point(at.X + 14, at.Y));
                context.DrawLine(pen, new Point(at.X, at.Y - 14), new Point(at.X, at.Y - 4));
                context.DrawLine(pen, new Point(at.X, at.Y + 4), new Point(at.X, at.Y + 14));
            }
            var text = Text(Maps.GeoLabel(latitude, longitude), 11.5, Brushes.White, FontWeight.SemiBold);
            var x = at.X + 16;
            var y = at.Y + 14;
            if (x + text.Width + 14 > Bounds.Width) x = at.X - 16 - text.Width - 12;
            if (y + text.Height + 8 > Bounds.Height) y = at.Y - 14 - text.Height - 6;
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 24, 34), 0.86), null, new Rect(x, y, text.Width + 12, text.Height + 5), 5, 5);
            context.DrawText(text, new Point(x + 6, y + 2));
        }

        // ------------------------------------------------------------ le ciel du globe (lot 13)

        internal sealed class SkyDot
        {
            public double Longitude, Latitude;   // la direction de l'astre, dans le repère du monde
            public Color Color;
            public double Size;                  // rayon à l'écran, en px
            public bool Sun;
            public double Lit = 1;               // la part éclairée d'une lune
            public string Name;
            // Lot 14 : l'allure de l'astre, et sa place quand c'est un satellite.
            public Color Accent;                 // le halo d'un soleil, l'atmosphère d'un satellite
            public bool HasAccent;               // lot 17 : un accent a été choisi (un satellite n'en a pas d'office)
            public bool Rings;
            public Color RingColor;
            public double RingSize = 1, Tilt;    // Tilt : degrés
            public BodyLook Look;                // lot 16 : l'écart, l'épaisseur, l'angle et le grain des anneaux
            public bool Satellite;               // il tourne autour du globe, à portée de vue
            public double Orbit = 2;             // le rayon de son orbite, en rayons du globe
            public SkyDot Parent;                // un satellite de satellite : il tourne autour de celui-ci
            public double Angle;                 // …à cet angle
        }

        /// <summary>Les anneaux d'un astre du ciel (lot 16 : RingArt) : un disque vu un peu de dessus, penché de
        /// l'inclinaison de l'astre et de l'angle de ses anneaux — entier derrière lui, puis sa part de devant.</summary>
        private void DotRings(DrawingContext context, SkyDot body, Point at, bool front)
        {
            if (!body.Rings || body.Look == null) return;
            double nx, ny, nz;
            RingArt.DotNormal(body.Tilt + body.Look.RingAngle, out nx, out ny, out nz);
            // Lot 17 : ils pivotent avec le globe sous la souris ; et un astre qui n'est pas un soleil porte
            // son ombre sur eux — la lumière vient d'où vient sa phase (pleine : de face ; nouvelle : de dos).
            RingArt.Scene scene = null;
            if (!body.Sun)
            {
                var phase = Math.Acos(Math.Max(-1, Math.Min(1, 2 * body.Lit - 1)));
                scene = new RingArt.Scene { HasSun = true, Sx = Math.Sin(phase), Sy = 0, Sz = Math.Cos(phase) };
            }
            RingArt.Draw(context, at, body.Size, nx, ny, nz, body.Look, body.RingColor, _owner.RingSpin - _globeLon, front, 256, scene);
        }

        /// <summary>L'accent d'un satellite (lot 17) : le liseré de son atmosphère, autour de lui.</summary>
        private void DotAccent(DrawingContext context, SkyDot body, Point at)
        {
            if (!body.HasAccent) return;
            var halo = new RadialGradientBrush { Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Radius = 0.5 };
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, body.Accent.R, body.Accent.G, body.Accent.B), 0.5));
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(190, body.Accent.R, body.Accent.G, body.Accent.B), 0.64));
            halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, body.Accent.R, body.Accent.G, body.Accent.B), 1));
            var reach = body.Size * 1.6 + 2;
            context.DrawEllipse(halo, null, at, reach, reach);
            _accentsDrawn++;
        }

        private int _accentsDrawn;
        internal int AccentsDrawnForProbe { get { return _accentsDrawn; } }

        /// <summary>Le soleil du monde, vu de l'écran (x à droite, y en haut, z vers nous).</summary>
        private void SunOnScreen(out double x, out double y, out double z)
        {
            var declination = _owner.Declination;
            var l = Maps.NoonLongitude(_owner.DayFraction) - _globeLon;
            double sinP = Math.Sin(_globeLat), cosP = Math.Cos(_globeLat), sinLat = Math.Sin(declination), cosLat = Math.Cos(declination);
            x = cosLat * Math.Sin(l);
            y = cosP * sinLat - sinP * cosLat * Math.Cos(l);
            z = sinP * sinLat + cosP * cosLat * Math.Cos(l);
        }

        private int _satellitesDrawn;
        internal int SatellitesDrawnForProbe { get { return _satellitesDrawn; } }

        /// <summary>Où est un satellite à l'écran, et s'il passe devant le globe (lot 14) : il tourne autour du
        /// monde à la distance qu'on lui a donnée — ramenée à une orbite qui tient dans la vue.</summary>
        internal bool SatellitePoint(SkyDot body, Point centre, double radius, out Point at, out bool front)
        {
            if (body.Parent != null)
            {
                Point parent;
                if (!SatellitePoint(body.Parent, centre, radius, out parent, out front)) { at = default(Point); return false; }
                var reach = body.Parent.Size * 2.6 + 6;
                at = new Point(parent.X + reach * Math.Cos(body.Angle), parent.Y - reach * 0.5 * Math.Sin(body.Angle));
                return true;
            }
            var l = body.Longitude - _globeLon;
            double sinP = Math.Sin(_globeLat), cosP = Math.Cos(_globeLat), sinLat = Math.Sin(body.Latitude), cosLat = Math.Cos(body.Latitude);
            var x = cosLat * Math.Sin(l);
            var y = cosP * sinLat - sinP * cosLat * Math.Cos(l);
            var z = sinP * sinLat + cosP * cosLat * Math.Cos(l);
            var reachOut = radius * body.Orbit;
            at = new Point(centre.X + reachOut * x, centre.Y - reachOut * y);
            front = z >= 0;
            // Derrière le globe, il est caché tant qu'il ne dépasse pas du disque.
            return front || Math.Sqrt(x * x + y * y) * body.Orbit > 1.02;
        }

        private void DrawSatellites(DrawingContext context, Point centre, double radius, bool frontPass)
        {
            if (!frontPass) { _satellitesDrawn = 0; _accentsDrawn = 0; }
            foreach (var body in _owner.SkyBodies())
            {
                if (!body.Satellite) continue;
                Point at;
                bool front;
                if (!SatellitePoint(body, centre, radius, out at, out front) || front != frontPass) continue;
                if (at.X < -40 || at.Y < -40 || at.X > Bounds.Width + 40 || at.Y > Bounds.Height + 40) continue;
                _satellitesDrawn++;
                DotRings(context, body, at, false);
                DotAccent(context, body, at);
                var dim = Color.FromRgb((byte)(body.Color.R * 0.28), (byte)(body.Color.G * 0.28), (byte)(body.Color.B * 0.3));
                context.DrawEllipse(new SolidColorBrush(dim), new Pen(new SolidColorBrush(body.Color, 0.55), 1), at, body.Size, body.Size);
                if (body.Lit > 0.03)
                    using (context.PushGeometryClip(new EllipseGeometry(new Rect(at.X - body.Size, at.Y - body.Size, body.Size * 2, body.Size * 2))))
                        context.DrawEllipse(new SolidColorBrush(body.Color), null, new Point(at.X + body.Size * 2 * (1 - body.Lit), at.Y), body.Size, body.Size);
                DotRings(context, body, at, true);
                if (!string.IsNullOrEmpty(body.Name))
                {
                    var text = Text(body.Name, 10.5, new SolidColorBrush(Colors.White, 0.8), FontWeight.Normal);
                    context.DrawText(text, new Point(at.X - text.Width / 2, at.Y + body.Size + 4));
                }
            }
        }

        /// <summary>L'allure du monde lui-même (lot 14) : le halo de son atmosphère, et ses anneaux — dans le
        /// plan de son équateur, la moitié lointaine derrière le globe, la moitié proche devant.</summary>
        private void DrawWorldLook(DrawingContext context, Point centre, double radius, bool front)
        {
            var world = _owner.World;
            if (world == null) return;
            var look = world.Look;
            if (!front && !string.IsNullOrEmpty(look.Accent))
            {
                var accent = Tint(look.Accent, Colors.White);
                var halo = new RadialGradientBrush { Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Radius = 0.5 };
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 0.78));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(120, accent.R, accent.G, accent.B), 0.84));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));
                context.DrawEllipse(halo, null, centre, radius * 1.19, radius * 1.19);
            }
            if (!look.Rings) return;
            // LOT 16 : le disque des anneaux, posé dans l'espace (RingArt). Sa normale est l'axe du monde,
            // penché de l'angle des anneaux vers le méridien d'origine — dans le repère du MONDE, donc
            // elle tourne à l'écran avec le globe, comme lui.
            var ring = Tint(look.RingColor, Color.FromRgb(0xD9, 0xC9, 0xA6));
            var lean = look.RingAngle * Math.PI / 180;
            double sinLat = Math.Cos(lean), cosLat = Math.Sin(lean), l = -_globeLon;
            double sinP = Math.Sin(_globeLat), cosP = Math.Cos(_globeLat);
            var nx = cosLat * Math.Sin(l);
            var ny = cosP * sinLat - sinP * cosLat * Math.Cos(l);
            var nz = sinP * sinLat + cosP * cosLat * Math.Cos(l);
            // Lot 17 : un repère du plan des anneaux, attaché au monde (sur son équateur, à 90° de longitude) :
            // le disque pivote avec le globe sous la souris. Et, quand le jour et la nuit sont montrés, le
            // soleil : l'ombre du monde tombe sur ses anneaux.
            var turn = Math.PI / 2 - _globeLon;
            var scene = new RingArt.Scene { HasRef = true, Rx = Math.Sin(turn), Ry = -sinP * Math.Cos(turn), Rz = cosP * Math.Cos(turn) };
            if (_owner.NightShown)
            {
                scene.HasSun = true;
                SunOnScreen(out scene.Sx, out scene.Sy, out scene.Sz);
            }
            RingArt.Draw(context, centre, radius, nx, ny, nz, look, ring, _owner.RingSpin, front, 1024, scene);
        }

        private sealed class Star
        {
            public double SinLat, CosLat, Lon, Size, Glow, Speed, Phase;
            public Color Color;
        }

        private static Star[] _stars;
        private Avalonia.Threading.DispatcherTimer _skyTimer;
        private readonly System.Diagnostics.Stopwatch _skyClock = System.Diagnostics.Stopwatch.StartNew();
        private double _skyTimeForProbe = -1;
        private int _starsDrawn, _bodiesDrawn;

        /// <summary>Le catalogue : des étoiles semées au hasard (toujours le même) sur toute la sphère céleste,
        /// et une voie lactée — une bande plus dense, penchée sur l'équateur céleste.</summary>
        private static Star[] Stars()
        {
            if (_stars != null) return _stars;
            var random = new Random(20261116);
            var list = new List<Star>();
            var tints = new[] { Color.FromRgb(255, 255, 255), Color.FromRgb(214, 226, 255), Color.FromRgb(255, 240, 214), Color.FromRgb(255, 222, 200), Color.FromRgb(196, 214, 255) };
            for (var i = 0; i < 620; i++)
            {
                double sinLat, lon;
                if (i < 400)
                {
                    sinLat = random.NextDouble() * 2 - 1;
                    lon = random.NextDouble() * 2 * Math.PI - Math.PI;
                }
                else
                {
                    // La voie lactée : un grand cercle incliné de 55°, épaissi d'une dizaine de degrés.
                    var along = random.NextDouble() * 2 * Math.PI;
                    var off = (random.NextDouble() + random.NextDouble() + random.NextDouble() - 1.5) * 0.24;
                    double x = Math.Cos(along) * Math.Cos(off), y = Math.Sin(along) * Math.Cos(off), z = Math.Sin(off);
                    const double lean = 0.96;
                    var y2 = y * Math.Cos(lean) - z * Math.Sin(lean);
                    var z2 = y * Math.Sin(lean) + z * Math.Cos(lean);
                    sinLat = Math.Max(-1, Math.Min(1, z2));
                    lon = Math.Atan2(y2, x);
                }
                var bright = Math.Pow(random.NextDouble(), 3.2);    // beaucoup de faibles, quelques brillantes
                list.Add(new Star
                {
                    SinLat = sinLat, CosLat = Math.Sqrt(1 - sinLat * sinLat), Lon = lon,
                    Size = (i < 400 ? 0.55 : 0.4) + bright * 1.5, Glow = (i < 400 ? 0.5 : 0.3) + bright * 0.5,
                    Speed = 0.7 + random.NextDouble() * 2.6, Phase = random.NextDouble() * 2 * Math.PI, Color = tints[random.Next(tints.Length)]
                });
            }
            return _stars = list.ToArray();
        }

        /// <summary>Une direction du ciel vers l'écran : le fond tourne avec le globe (on tourne autour du
        /// monde, les étoiles lointaines défilent derrière lui). Faux si elle est dans notre dos.</summary>
        private bool SkyPoint(double lon, double sinLat, double cosLat, out Point at)
        {
            var l = lon - _globeLon;
            double sinP = Math.Sin(_globeLat), cosP = Math.Cos(_globeLat);
            var x = cosLat * Math.Sin(l);
            var y = cosP * sinLat - sinP * cosLat * Math.Cos(l);
            var z = sinP * sinLat + cosP * cosLat * Math.Cos(l);     // vers nous
            at = default(Point);
            if (z > -0.16) return false;
            var focal = Math.Max(Bounds.Width, Bounds.Height) * 0.62;
            at = new Point(Bounds.Width / 2 + focal * x / -z, Bounds.Height / 2 - focal * y / -z);
            return at.X > -30 && at.Y > -30 && at.X < Bounds.Width + 30 && at.Y < Bounds.Height + 30;
        }

        private bool SkyAnimated { get { return _owner.AnimateSky && Marabook.Settings.AppSettings.Animations; } }

        /// <summary>L'horloge du ciel ne tourne que tant qu'un globe est à l'écran.</summary>
        private void EnsureSkyTimer(bool wanted)
        {
            if (wanted && _skyTimer == null)
            {
                _skyTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
                _skyTimer.Tick += delegate
                {
                    if (!Globe || !IsEffectivelyVisible || !SkyAnimated) { EnsureSkyTimer(false); return; }
                    InvalidateVisual();
                };
                _skyTimer.Start();
            }
            else if (!wanted && _skyTimer != null)
            {
                _skyTimer.Stop();
                _skyTimer = null;
            }
        }

        /// <summary>LE CIEL DERRIÈRE LE GLOBE (lot 13, Rémi) : les étoiles, qui scintillent ; une étoile filante
        /// de temps à autre ; et les astres du monde lié — ses soleils, ses lunes — là où ils sont au jour et
        /// à l'heure choisis.</summary>
        private void DrawSky(DrawingContext context)
        {
            var animated = SkyAnimated;
            EnsureSkyTimer(animated);
            var time = _skyTimeForProbe >= 0 ? _skyTimeForProbe : animated ? _skyClock.Elapsed.TotalSeconds : 0;
            _starsDrawn = 0;
            foreach (var star in Stars())
            {
                Point at;
                if (!SkyPoint(star.Lon, star.SinLat, star.CosLat, out at)) continue;
                var twinkle = 0.74 + 0.26 * Math.Sin(time * star.Speed + star.Phase);
                var glow = Math.Min(1, star.Glow * twinkle);
                context.DrawEllipse(new SolidColorBrush(star.Color, glow), null, at, star.Size, star.Size);
                if (star.Size > 1.55)
                {
                    // Les plus brillantes : un halo, et une croix qui respire avec le scintillement.
                    context.DrawEllipse(new SolidColorBrush(star.Color, glow * 0.16), null, at, star.Size * 3.2, star.Size * 3.2);
                    var arm = star.Size * (2.2 + 2.4 * twinkle);
                    var pen = new Pen(new SolidColorBrush(star.Color, glow * 0.5), 0.8);
                    context.DrawLine(pen, new Point(at.X - arm, at.Y), new Point(at.X + arm, at.Y));
                    context.DrawLine(pen, new Point(at.X, at.Y - arm), new Point(at.X, at.Y + arm));
                }
                _starsDrawn++;
            }
            if (animated || _skyTimeForProbe >= 0) DrawMeteor(context, time);

            _bodiesDrawn = 0;
            foreach (var body in _owner.SkyBodies())
            {
                Point at;
                var latitude = body.Latitude;
                if (body.Satellite) continue; // un satellite tourne autour du globe : DrawSatellites
                if (!SkyPoint(body.Longitude, Math.Sin(latitude), Math.Cos(latitude), out at)) continue;
                _bodiesDrawn++;
                DotRings(context, body, at, false);
                if (body.Sun)
                {
                    var pulse = 1 + 0.04 * Math.Sin(time * 1.3 + body.Longitude);
                    var halo = new RadialGradientBrush { Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Radius = 0.5 };
                    halo.GradientStops.Add(new GradientStop(Color.FromArgb(150, body.Accent.R, body.Accent.G, body.Accent.B), 0));
                    halo.GradientStops.Add(new GradientStop(Color.FromArgb(46, body.Accent.R, body.Accent.G, body.Accent.B), 0.45));
                    halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, body.Accent.R, body.Accent.G, body.Accent.B), 1));
                    context.DrawEllipse(halo, null, at, body.Size * 4.2 * pulse, body.Size * 4.2 * pulse);
                    context.DrawEllipse(new SolidColorBrush(body.Color), null, at, body.Size, body.Size);
                    context.DrawEllipse(new SolidColorBrush(Colors.White, 0.7), null, at, body.Size * 0.55, body.Size * 0.55);
                    DotRings(context, body, at, true);
                }
                else
                {
                    // Une lune : son disque dans l'ombre, et sa part éclairée par-dessus.
                    var dim = Color.FromRgb((byte)(body.Color.R * 0.28), (byte)(body.Color.G * 0.28), (byte)(body.Color.B * 0.3));
                    context.DrawEllipse(new SolidColorBrush(dim), new Pen(new SolidColorBrush(body.Color, 0.55), 1), at, body.Size, body.Size);
                    if (body.Lit > 0.03)
                        using (context.PushGeometryClip(new EllipseGeometry(new Rect(at.X - body.Size, at.Y - body.Size, body.Size * 2, body.Size * 2))))
                            context.DrawEllipse(new SolidColorBrush(body.Color), null, new Point(at.X + body.Size * 2 * (1 - body.Lit), at.Y), body.Size, body.Size);
                }
                if (!string.IsNullOrEmpty(body.Name))
                {
                    var text = Text(body.Name, 10.5, new SolidColorBrush(Colors.White, 0.78), FontWeight.Normal);
                    context.DrawText(text, new Point(at.X - text.Width / 2, at.Y + body.Size + (body.Sun ? 8 : 4)));
                }
            }
        }

        /// <summary>Une étoile filante toutes les sept secondes environ : une traînée qui file et s'éteint.</summary>
        private void DrawMeteor(DrawingContext context, double time)
        {
            const double every = 7;
            var bucket = (int)Math.Floor(time / every);
            var random = new Random(9173 + bucket * 31);
            var start = bucket * every + random.NextDouble() * (every - 1.2);
            var progress = (time - start) / 0.9;
            if (progress < 0 || progress > 1) return;
            var from = new Point(random.NextDouble() * Bounds.Width, random.NextDouble() * Bounds.Height * 0.7);
            var angle = Math.PI * (0.12 + random.NextDouble() * 0.3);
            var length = 150 + random.NextDouble() * 160;
            var direction = random.Next(2) == 0 ? 1 : -1;
            var head = new Point(from.X + direction * Math.Cos(angle) * length * progress, from.Y + Math.Sin(angle) * length * progress);
            var tailAt = Math.Max(0, progress - 0.28);
            var tail = new Point(from.X + direction * Math.Cos(angle) * length * tailAt, from.Y + Math.Sin(angle) * length * tailAt);
            var fade = Math.Sin(progress * Math.PI);
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(tail, RelativeUnit.Absolute), EndPoint = new RelativePoint(head, RelativeUnit.Absolute) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(230 * fade), 255, 255, 255), 1));
            context.DrawLine(new Pen(brush, 1.6) { LineCap = PenLineCap.Round }, tail, head);
            context.DrawEllipse(new SolidColorBrush(Colors.White, 0.9 * fade), null, head, 1.6, 1.6);
        }

        internal int StarsDrawnForProbe { get { return _starsDrawn; } }
        internal int BodiesDrawnForProbe { get { return _bodiesDrawn; } }
        internal bool SkyTimerRunningForProbe { get { return _skyTimer != null; } }
        internal void SetSkyTimeForProbe(double seconds) { _skyTimeForProbe = seconds; InvalidateVisual(); }
        internal bool SkyPointForProbe(double lon, double lat, out Point at) { return SkyPoint(lon, Math.Sin(lat), Math.Cos(lat), out at); }


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

        /// <summary>Le carré d'écran que couvre l'image du globe, et sa finesse (lot 14, bug « on finit sur
        /// un carré ») : tant que le globe tient dans son cadre, rien ne change ; dès qu'on l'approche, le
        /// carré grandit avec lui jusqu'à couvrir toute la toile — la sphère n'est plus jamais rognée.</summary>
        private void GlobeFrame(out double radius, out double cover, out int pixels)
        {
            radius = GlobeRadius(GlobeSide);
            cover = Math.Max(GlobeSide, Math.Min(Math.Max(Bounds.Width, Bounds.Height) + 2, 2 * radius + 6));
            pixels = (int)Math.Max(64, Math.Min(cover, 760));
        }

        private void RenderGlobe(DrawingContext context, MapInfo map)
        {
            int tw, th;
            var texture = _cache.Texture(map, out tw, out th);
            double radius, cover;
            int side;
            GlobeFrame(out radius, out cover, out side);
            var dest = new Rect((Bounds.Width - cover) / 2, (Bounds.Height - cover) / 2, cover, cover);
            var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
            DrawSky(context);
            if (texture == null)
            {
                var wait = Text("Le globe se prépare…", 13, Brushes.White, FontWeight.Normal);
                context.DrawText(wait, new Point((Bounds.Width - wait.Width) / 2, (Bounds.Height - wait.Height) / 2));
                return;
            }
            // Derrière le globe : son halo, la moitié lointaine de ses anneaux, les satellites passés derrière lui.
            DrawWorldLook(context, centre, radius, false);
            DrawSatellites(context, centre, radius, false);
            var night = _owner.NightShown;
            Color heatNorth, heatSouth;
            var heat = _owner.SeasonHeatShown && SeasonTint(false, out heatNorth) & SeasonTint(true, out heatSouth);
            var key = (heat ? _owner.DayOfYear.ToString("R", CultureInfo.InvariantCulture) + "s" : "") + side + "/" + cover.ToString("R", CultureInfo.InvariantCulture) + "|" + _globeLon.ToString("R", CultureInfo.InvariantCulture) + "|" + _globeLat.ToString("R", CultureInfo.InvariantCulture) + "|" + _globeZoom.ToString("R", CultureInfo.InvariantCulture)
                + "|" + (night ? _owner.Declination.ToString("R", CultureInfo.InvariantCulture) + "/" + _owner.DayFraction.ToString("R", CultureInfo.InvariantCulture) + RingKey() : "-")
                + "|" + map.Layout + map.PoleNorth.ToString("R", CultureInfo.InvariantCulture) + "/" + map.PoleSouth.ToString("R", CultureInfo.InvariantCulture);
            if (_globeBitmap == null || _globeBitmap.PixelSize.Width != side || key != _globeKey)
            {
                PaintGlobe(texture, tw, th, side, night, radius * side / cover);
                _globeKey = key;
            }
            context.DrawImage(_globeBitmap, dest);
            DrawWorldLook(context, centre, radius, true);
            DrawSatellites(context, centre, radius, true);

            // Les zones et les repères, reportés sur la sphère (la face visible seulement).
            var zoom = ZoomRatio;
            if (_owner.GuidesShown) using (context.PushClip(dest)) DrawGlobeGuides(context, centre, radius);
            using (context.PushClip(dest))
            {
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
            DrawLatLon(context);
        }

        /// <summary>Ce qui, des anneaux du monde, change l'ombre qu'ils portent sur lui (lot 17).</summary>
        private string RingKey()
        {
            var world = _owner.World;
            if (world == null || !world.Look.Rings) return "";
            double inner, outer;
            world.Look.RingSpan(out inner, out outer);
            return "o" + inner.ToString("R", CultureInfo.InvariantCulture) + "/" + outer.ToString("R", CultureInfo.InvariantCulture) + "/" + world.Look.RingAngle.ToString("R", CultureInfo.InvariantCulture);
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

        // LA GÉOMÉTRIE DU GLOBE, gardée d'une image à l'autre (lot 15, Rémi : « l'animation est un peu laggy,
        // target 30 fps »). Pour chaque pixel du disque : la couleur de la carte à cet endroit, la normale de
        // la sphère dans le repère du monde, sa latitude, et l'ombre du bord. Tant que le globe ne tourne pas
        // et ne change pas de taille — c'est le cas pendant « Animer » —, rien de cela n'est recalculé : une
        // image ne coûte plus qu'un produit scalaire par pixel (la lumière du soleil), réparti sur les cœurs.
        private int[] _geoColor;
        private float[] _geoX, _geoY, _geoZ, _geoLat, _geoEdge;
        private string _geoKey;
        private int _geometryBuilds;
        internal int GeometryBuildsForProbe { get { return _geometryBuilds; } }
        internal double LastPaintMsForProbe { get; private set; }

        private void BuildGeometry(int[] texture, int tw, int th, int side, double radius)
        {
            var count = side * side;
            if (_geoColor == null || _geoColor.Length != count)
            {
                _geoColor = new int[count];
                _geoX = new float[count];
                _geoY = new float[count];
                _geoZ = new float[count];
                _geoLat = new float[count];
                _geoEdge = new float[count];
            }
            int[] colors = _geoColor;
            float[] nx = _geoX, ny = _geoY, nz = _geoZ, lats = _geoLat, edges = _geoEdge;
            double sinP = Math.Sin(_globeLat), cosP = Math.Cos(_globeLat), globeLon = _globeLon;
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
            System.Threading.Tasks.Parallel.For(0, side, delegate(int py)
            {
                var y = (half - py - 0.5) / radius;
                var row = py * side;
                for (var px = 0; px < side; px++)
                {
                    var x = (px + 0.5 - half) / radius;
                    var d = x * x + y * y;
                    if (d > 1) { edges[row + px] = -1; continue; }
                    var z = Math.Sqrt(1 - d);
                    var sinLat = z * sinP + y * cosP;
                    if (sinLat > 1) sinLat = 1; else if (sinLat < -1) sinLat = -1;
                    var lat = Math.Asin(sinLat);
                    var lon = globeLon + Math.Atan2(x, z * cosP - y * sinP);
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
                    var cosLat = Math.Sqrt(1 - sinLat * sinLat);
                    colors[row + px] = color;
                    nx[row + px] = (float)(cosLat * Math.Cos(lon));
                    ny[row + px] = (float)(cosLat * Math.Sin(lon));
                    nz[row + px] = (float)sinLat;
                    lats[row + px] = (float)lat;
                    // L'ombre du bord (la sphère se lit) ; le dernier pixel du disque s'estompe : pas d'escalier.
                    var alpha = Math.Min(1, (1 - Math.Sqrt(d)) * radius);
                    edges[row + px] = (float)((0.58 + 0.42 * z) * alpha + Math.Floor(alpha * 255) * 4); // l'ombre (< 4) et l'opacité, dans le même nombre
                }
            });
            _geometryBuilds++;
        }

        private void PaintGlobe(int[] texture, int tw, int th, int side, bool night, double radius)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (_globeBuffer == null || _globeBuffer.Length != side * side) _globeBuffer = new int[side * side];
            if (_globeBitmap == null || _globeBitmap.PixelSize.Width != side)
                _globeBitmap = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            var buffer = _globeBuffer;
            _globePixels = side;
            var map = Map;
            var geoKey = side + "|" + radius.ToString("R", CultureInfo.InvariantCulture) + "|" + _globeLon.ToString("R", CultureInfo.InvariantCulture) + "|" + _globeLat.ToString("R", CultureInfo.InvariantCulture)
                + "|" + map.Layout + map.PoleNorth.ToString("R", CultureInfo.InvariantCulture) + "/" + map.PoleSouth.ToString("R", CultureInfo.InvariantCulture) + "|" + tw + "x" + th + "#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(texture);
            if (geoKey != _geoKey || _geoColor == null || _geoColor.Length != side * side)
            {
                BuildGeometry(texture, tw, th, side, radius);
                _geoKey = geoKey;
            }
            int[] colors = _geoColor;
            float[] nx = _geoX, ny = _geoY, nz = _geoZ, lats = _geoLat, edges = _geoEdge;
            // Le soleil, dans le repère du monde : à l'aplomb du point où il est midi.
            var declination = _owner.Declination;
            var noon = Maps.NoonLongitude(_owner.DayFraction);
            float sx = (float)(Math.Cos(declination) * Math.Cos(noon)), sy = (float)(Math.Cos(declination) * Math.Sin(noon)), sz = (float)Math.Sin(declination);
            // Les saisons (lot 13) : le même voile dégradé qu'au planisphère, peint dans la sphère.
            Color veilNorth = default(Color), veilSouth = default(Color);
            var world = _owner.World;
            var veil = _owner.SeasonHeatShown && SeasonTint(false, out veilNorth) & SeasonTint(true, out veilSouth);
            var tropic = veil && world != null ? (float)(Math.Abs(world.AxialTilt) * Math.PI / 180) : 0f;
            if (tropic < 1e-6f) veil = false;
            var strength = (float)HeatStrength;
            // Lot 17 : l'ombre des anneaux sur le monde — là où, entre le sol et le soleil, passent les anneaux.
            var ringed = night && world != null && world.Look.Rings;
            float ringX = 0, ringY = 0, ringZ = 1, ringInner = 1, ringOuter = 1;
            float[] bands = null;
            if (ringed)
            {
                double inner, outer;
                world.Look.RingSpan(out inner, out outer);
                var lean = world.Look.RingAngle * Math.PI / 180;
                ringX = (float)Math.Sin(lean);
                ringZ = (float)Math.Cos(lean);
                ringInner = (float)inner;
                ringOuter = (float)outer;
                bands = RingArt.Bands();
            }
            System.Threading.Tasks.Parallel.For(0, side, delegate(int py)
            {
                var row = py * side;
                for (var px = 0; px < side; px++)
                {
                    var i = row + px;
                    var packed = edges[i];
                    if (packed < 0) { buffer[i] = 0; continue; }
                    var alpha = (int)(packed / 4);
                    var light = packed - alpha * 4;
                    var color = colors[i];
                    float red = (color >> 16) & 0xFF, green = (color >> 8) & 0xFF, blue = color & 0xFF;
                    if (veil)
                    {
                        var lat = lats[i];
                        var share = Math.Min(1f, Math.Abs(lat) / tropic) * strength;
                        var tint = lat >= 0 ? veilNorth : veilSouth;
                        var keep = 1 - share;
                        red = red * keep + tint.R * share;
                        green = green * keep + tint.G * share;
                        blue = blue * keep + tint.B * share;
                    }
                    if (night)
                    {
                        var sun = nx[i] * sx + ny[i] * sy + nz[i] * sz;
                        light *= sun > 0.1f ? 1f : sun < -0.1f ? 0.3f : 0.3f + 0.7f * (sun + 0.1f) / 0.2f;
                        if (ringed && sun > 0) light *= RingArt.ShadowOn(nx[i], ny[i], nz[i], sx, sy, sz, ringX, ringY, ringZ, ringInner, ringOuter, bands);
                    }
                    buffer[i] = (alpha << 24) | ((int)(red * light) << 16) | ((int)(green * light) << 8) | (int)(blue * light);
                }
            });
            using (var frame = _globeBitmap.Lock())
            {
                if (frame.RowBytes == side * 4) Marshal.Copy(buffer, 0, frame.Address, buffer.Length);
                else for (var py = 0; py < side; py++) Marshal.Copy(buffer, py * side, frame.Address + py * frame.RowBytes, side);
            }
            LastPaintMsForProbe = clock.Elapsed.TotalMilliseconds;
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

        /// <summary>La vue, pour s'en souvenir d'une visite à l'autre (lot 13) : zoom et position à plat, rotation et zoom du globe.</summary>
        internal double[] ViewState { get { return new[] { _scale, _viewX, _viewY, _globeLon, _globeLat, _globeZoom }; } }

        internal void RestoreView(double[] state)
        {
            if (state == null || state.Length < 6 || Map == null || !Map.HasImage) return;
            foreach (var value in state) if (double.IsNaN(value) || double.IsInfinity(value)) return;
            if (state[0] > 1e-6 && state[0] <= 6)
            {
                _scale = state[0];
                _viewX = state[1];
                _viewY = state[2];
                _fitPending = false;
                if (Bounds.Width > 0) Clamp();
            }
            _globeLon = state[3];
            _globeLat = Math.Max(-Math.PI / 2, Math.Min(Math.PI / 2, state[4]));
            _globeZoom = Math.Max(0.6, Math.Min(12, state[5]));
            _globeKey = null;
            _owner.ViewChanged();
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var map = Map;
            if (map == null || !map.HasImage) return;
            var point = e.GetPosition(this);
            _cursor = point;
            if (_owner.LatLonShown)
            {
                double latitude, longitude;
                var known = GeoAt(point, out latitude, out longitude);
                _owner.CursorGeo(known, latitude, longitude);
                InvalidateVisual();
            }
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
            if (_owner.LatLonShown) { _owner.CursorGeo(false, 0, 0); InvalidateVisual(); }
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
        private int _globePixels;
        /// <summary>Un pixel de l'image du globe, repéré dans le cadre du globe (GlobeSide), quel que soit le zoom.</summary>
        internal int GlobePixelForProbe(int x, int y)
        {
            if (_globeBuffer == null || _globePixels <= 0) return 0;
            double radius, cover;
            int side;
            GlobeFrame(out radius, out cover, out side);
            var k = _globePixels / cover;
            var bx = (int)Math.Round((x - GlobeSide / 2.0) * k + _globePixels / 2.0);
            var by = (int)Math.Round((y - GlobeSide / 2.0) * k + _globePixels / 2.0);
            if (bx < 0 || by < 0 || bx >= _globePixels || by >= _globePixels) return 0;
            return _globeBuffer[by * _globePixels + bx];
        }
        internal int GlobeBitmapPixelsForProbe { get { return _globePixels; } }
        internal double GlobeCoverForProbe { get { double radius, cover; int side; GlobeFrame(out radius, out cover, out side); return cover; } }
        /// <summary>L'opacité de l'image du globe sous un point de l'écran (0 = hors de la sphère).</summary>
        internal int GlobeAlphaAtForProbe(Point screen)
        {
            if (_globeBuffer == null || _globePixels <= 0) return 0;
            double radius, cover;
            int side;
            GlobeFrame(out radius, out cover, out side);
            var k = _globePixels / cover;
            var bx = (int)((screen.X - (Bounds.Width - cover) / 2) * k);
            var by = (int)((screen.Y - (Bounds.Height - cover) / 2) * k);
            if (bx < 0 || by < 0 || bx >= _globePixels || by >= _globePixels) return 0;
            return (_globeBuffer[by * _globePixels + bx] >> 24) & 0xFF;
        }
        internal void PaintGlobeForProbe()
        {
            int tw, th;
            var texture = _cache == null ? null : _cache.Texture(Map, out tw, out th, true);
            if (texture == null) return;
            _cache.Texture(Map, out tw, out th, true);
            double radius, cover;
            int side;
            GlobeFrame(out radius, out cover, out side);
            PaintGlobe(texture, tw, th, side, _owner.NightShown, radius * side / cover);
            _globeKey = "probe";
        }
    }
}

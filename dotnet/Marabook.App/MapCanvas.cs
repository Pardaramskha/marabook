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
        private bool Globe { get { return Map != null && Map.View == Maps.ViewGlobe; } }
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
            Cursor = tool == Tool.Pan ? Cursor.Default : new Cursor(StandardCursorType.Cross);
            InvalidateVisual();
        }

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
            if (_owner.NightShown) DrawNight(context, map);

            var zoom = ZoomRatio;
            var selectedZone = _owner.SelectedZone;
            foreach (var zone in map.Zones)
            {
                if (zone.Points.Count < 2) continue;
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
                if (!chosen && !Maps.Visible(marker.Level, zoom)) continue;
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
                var edge = declination > 0 ? map.Height : 0;
                sink.BeginFigure(ToScreen(0, edge), true);
                for (var i = 0; i <= steps; i++)
                {
                    var x = (double)i / steps * map.Width;
                    var latitude = Math.Atan(-Math.Cos(Maps.Longitude(map, x) - noon) / Math.Tan(declination));
                    sink.LineTo(ToScreen(x, (0.5 - latitude / Math.PI) * map.Height));
                }
                sink.LineTo(ToScreen(map.Width, edge));
                sink.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(8, 12, 30), 0.52), new Pen(new SolidColorBrush(Color.FromRgb(8, 12, 30), 0.35), 3), geometry);
            // Le point où le soleil est au zénith.
            var sun = ToScreen((noon / (2 * Math.PI) + 0.5) * map.Width, (0.5 - _owner.Declination / Math.PI) * map.Height);
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x42), 0.35), null, sun, 13, 13);
            context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x42)), new Pen(Brushes.White, 1.5), sun, 6, 6);
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
                + "|" + (night ? _owner.Declination.ToString("R", CultureInfo.InvariantCulture) + "/" + _owner.DayFraction.ToString("R", CultureInfo.InvariantCulture) : "-");
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
                foreach (var zone in map.Zones)
                {
                    if (zone.Points.Count < 2 || !Maps.Visible(zone.Level, zoom)) continue;
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
                    if (!Maps.Visible(marker.Level, zoom) || !Project(map, marker.X, marker.Y, centre, radius, out at)) continue;
                    var color = Tint(marker.Color, Chrome.Danger.Color);
                    context.DrawEllipse(new SolidColorBrush(color), new Pen(Brushes.White, 2), at, 5, 5);
                    Label(context, marker.Name, new Point(at.X, at.Y + 14), color, false);
                }
            }
        }

        /// <summary>Un point de l'image sur le globe ; faux s'il est sur la face cachée.</summary>
        private bool Project(MapInfo map, double ix, double iy, Point centre, double radius, out Point at)
        {
            var lon = Maps.Longitude(map, ix) - _globeLon;
            var lat = Maps.Latitude(map, iy);
            double sinP = Math.Sin(_globeLat), cosP = Math.Cos(_globeLat);
            var depth = sinP * Math.Sin(lat) + cosP * Math.Cos(lat) * Math.Cos(lon);
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
                    var u = lon / twoPi + 0.5;
                    u -= Math.Floor(u);
                    var tx = (int)(u * tw);
                    if (tx >= tw) tx = tw - 1;
                    var ty = (int)((0.5 - lat / Math.PI) * th);
                    if (ty < 0) ty = 0; else if (ty >= th) ty = th - 1;
                    var color = texture[ty * tw + tx];
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

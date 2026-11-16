using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE PLANÉTARIUM (16/11 soir/2026, Rémi) : le système vu de dessus, un
    /// jour donné. Au centre les soleils centriques ; autour, un cercle par rang
    /// du placement stellaire (Astro.Layout) — un soleil en orbite rapprochée,
    /// le monde, une planète voisine, un soleil lointain ; autour du monde, ses
    /// lunes. Chaque astre est à l'angle que lui donnent sa période et sa date
    /// de référence : le monde fait un tour par année, une lune est pleine
    /// quand elle est à l'opposé du soleil, un compagnon proche passe devant
    /// son soleil quand il s'aligne avec le monde.
    ///
    /// Les distances ne sont PAS à l'échelle : seul l'ordre compte (c'est lui
    /// qui dit si un second soleil reste collé au premier ou fait le tour du
    /// ciel). La vue ne règle rien : elle montre ce que les onglets décrivent.</summary>
    internal sealed class PlanetariumView : Control
    {
        private World _world;
        private TimeSystem _system;
        private double _day;

        public PlanetariumView()
        {
            Width = 300;
            Height = 300;
            ClipToBounds = true;
        }

        public void Show(World world, TimeSystem system, double day)
        {
            _world = world;
            _system = system;
            _day = day;
            InvalidateVisual();
        }

        internal double DayForProbe { get { return _day; } }

        /// <summary>Sonde : les astres dessinés, dans l'ordre du centre vers le dehors, avec leur distance au centre.</summary>
        internal List<string> BodiesForProbe()
        {
            var names = new List<string>();
            if (_world == null) return names;
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            foreach (var spot in Place(center, Math.Min(Bounds.Width, Bounds.Height) / 2 - 22))
                names.Add(spot.Name + "@" + Math.Round(Math.Sqrt(Math.Pow(spot.At.X - center.X, 2) + Math.Pow(spot.At.Y - center.Y, 2))).ToString(CultureInfo.InvariantCulture));
            return names;
        }

        private sealed class Spot
        {
            public string Name;
            public Point At;
            public double Radius;
            public Color Tint;
            public int Kind;        // SkyBody.SunKind, WorldKind, PlanetKind ; 3 = lune
            public double Ring;     // le rayon de son cercle autour du centre ; 0 = pas de cercle
            public Point RingCenter;
        }

        private static Point Polar(Point center, double radius, double angle)
        {
            // L'angle 0 pointe vers la droite ; les astres tournent dans le sens inverse des aiguilles.
            return new Point(center.X + Math.Cos(angle) * radius, center.Y - Math.Sin(angle) * radius);
        }

        private static Color Parse(string hex, Color fallback)
        {
            Color color;
            return Color.TryParse(hex, out color) ? color : fallback;
        }

        private List<Spot> Place(Point center, double reach)
        {
            var spots = new List<Spot>();
            var bodies = Astro.Layout(_world);
            var maxRank = 1;
            foreach (var body in bodies) maxRank = Math.Max(maxRank, body.Rank);
            var year = Math.Max(1, Astro.SolarYear(_world, _system));
            var worldAngle = 2 * Math.PI * _day / year;
            var centric = bodies.FindAll(delegate(SkyBody b) { return b.Rank == 0; });
            var sunSpots = new Dictionary<string, Spot>();
            // Les soleils du centre : un seul au milieu ; plusieurs, serrés autour du milieu.
            for (var i = 0; i < centric.Count; i++)
            {
                var sun = centric[i].Sun;
                var at = centric.Count == 1 ? center : Polar(center, 9, 2 * Math.PI * i / centric.Count + _day / 6.0);
                var spot = new Spot { Name = centric[i].Name, At = at, Radius = SunRadius(sun), Tint = Parse(Astro.ColorOf(sun, _world.Suns.IndexOf(sun)), Colors.Goldenrod), Kind = SkyBody.SunKind };
                spots.Add(spot);
                sunSpots[sun.Id] = spot;
            }
            Point worldAt = center;
            var planetIndex = 0;
            foreach (var body in bodies)
            {
                if (body.Rank == 0) continue;
                var ring = reach * body.Rank / maxRank;
                if (body.Kind == SkyBody.WorldKind)
                {
                    worldAt = Polar(center, ring, worldAngle);
                    spots.Add(new Spot { Name = body.Name, At = worldAt, Radius = 6, Tint = Chrome.Accent.Color, Kind = SkyBody.WorldKind, Ring = ring, RingCenter = center });
                }
                else if (body.Kind == SkyBody.SunKind)
                {
                    var sun = body.Sun;
                    // Vu du monde : à sa date de référence il est dans l'axe du monde (devant son soleil s'il est proche,
                    // au plus haut à minuit s'il est lointain), puis il fait un tour par période.
                    var angle = worldAngle + (sun.Period > 0 ? 2 * Math.PI * (_day - sun.HighAt) / sun.Period : 0.6);
                    // Autour d'un soleil du centre : sur le cercle de son rang. Autour d'un soleil qui orbite lui-même : un petit cercle autour de lui.
                    Spot parent = null;
                    if (sun.OrbitId != null) sunSpots.TryGetValue(sun.OrbitId, out parent);
                    var aroundCenter = parent == null || centric.Exists(delegate(SkyBody b) { return b.Id == sun.OrbitId; });
                    var origin = aroundCenter ? center : parent.At;
                    var radius = aroundCenter ? ring : 15;
                    var spot = new Spot { Name = body.Name, At = Polar(origin, radius, angle), Radius = SunRadius(sun), Tint = Parse(Astro.ColorOf(sun, _world.Suns.IndexOf(sun)), Colors.OrangeRed), Kind = SkyBody.SunKind, Ring = radius, RingCenter = origin };
                    spots.Add(spot);
                    sunSpots[sun.Id] = spot;
                }
                else
                {
                    var planet = body.Planet;
                    // À sa date d'alignement elle est dans l'axe du monde ; sans année, elle attend à une place fixe.
                    var angle = planet.Period > 0 ? 2 * Math.PI * planet.At / year + 2 * Math.PI * (_day - planet.At) / planet.Period : 2.1 + planetIndex * 1.7;
                    spots.Add(new Spot { Name = body.Name, At = Polar(center, ring, angle), Radius = 4.5, Tint = Parse(Astro.ColorOf(planet, _world.Planets.IndexOf(planet)), Colors.SlateGray), Kind = SkyBody.PlanetKind, Ring = ring, RingCenter = center });
                    planetIndex++;
                }
            }
            // Les lunes : autour du monde — pleines à l'opposé du soleil ; une lune de lune autour de sa lune-mère.
            var moonSpots = new Dictionary<string, Spot>();
            var orbit = 0;
            for (var pass = 0; pass < 2; pass++)
                for (var i = 0; i < _world.Moons.Count; i++)
                {
                    var moon = _world.Moons[i];
                    var sub = Astro.IsSubMoon(_world, moon);
                    if (sub != (pass == 1)) continue;
                    var angle = worldAngle + 2 * Math.PI * (_day - moon.FullAt) / Math.Max(0.01, moon.Period);
                    Spot parent = null;
                    if (sub) moonSpots.TryGetValue(moon.OrbitId, out parent);
                    var origin = parent == null ? worldAt : parent.At;
                    var radius = parent == null ? 14 + 7 * orbit++ : 5.5;
                    var spot = new Spot { Name = string.IsNullOrEmpty(moon.Name) ? "Lune" : moon.Name, At = Polar(origin, radius, angle), Radius = sub ? 1.8 : 2.6, Tint = Parse(Astro.ColorOf(moon, i), Colors.Gray), Kind = 3, Ring = radius, RingCenter = origin };
                    spots.Add(spot);
                    moonSpots[moon.Id] = spot;
                }
            return spots;
        }

        private static double SunRadius(Sun sun)
        {
            return 8 * Math.Max(0.55, Math.Min(1.7, Math.Sqrt(Math.Max(0.0001, sun.Size))));
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
            context.DrawRectangle(Chrome.CanvasBg, new Pen(Chrome.Border, 1), bounds.Deflate(0.5), 10, 10);
            if (_world == null || _system == null) return;
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            var spots = Place(center, Math.Min(Bounds.Width, Bounds.Height) / 2 - 22);
            var faint = new Pen(Chrome.Border, 1);
            var worldRing = new Pen(new SolidColorBrush(Chrome.Accent.Color, 0.55), 1.2);
            var moonRing = new Pen(new SolidColorBrush(Chrome.FaintText.Color, 0.5), 0.8);
            foreach (var spot in spots)
                if (spot.Ring > 0) context.DrawEllipse(null, spot.Kind == SkyBody.WorldKind ? worldRing : spot.Kind == 3 ? moonRing : faint, spot.RingCenter, spot.Ring, spot.Ring);
            var labelled = 0;
            foreach (var spot in spots)
            {
                var brush = new SolidColorBrush(spot.Tint);
                if (spot.Kind == SkyBody.SunKind)
                {
                    // Un soleil : un halo, un disque.
                    context.DrawEllipse(new SolidColorBrush(spot.Tint, 0.22), null, spot.At, spot.Radius + 4, spot.Radius + 4);
                    context.DrawEllipse(brush, null, spot.At, spot.Radius, spot.Radius);
                }
                else if (spot.Kind == SkyBody.WorldKind)
                {
                    context.DrawEllipse(brush, new Pen(Chrome.Ink, 1.4), spot.At, spot.Radius, spot.Radius);
                }
                else context.DrawEllipse(brush, null, spot.At, spot.Radius, spot.Radius);
                if (spot.Kind == 3) continue; // les lunes : trop serrées pour un nom
                var text = new FormattedText(spot.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(Typeface.Default.FontFamily, FontStyle.Normal, spot.Kind == SkyBody.WorldKind ? FontWeight.SemiBold : FontWeight.Normal), 10.5,
                    spot.Kind == SkyBody.WorldKind ? Chrome.Ink : Chrome.SoftText);
                // Le nom sous l'astre, ramené dans le cadre.
                var x = Math.Max(4, Math.Min(Bounds.Width - text.Width - 4, spot.At.X - text.Width / 2));
                // Un nom dessous, le suivant dessus : deux astres voisins ne se recouvrent pas.
                var below = labelled++ % 2 == 0;
                var y = below ? spot.At.Y + spot.Radius + 3 : spot.At.Y - spot.Radius - text.Height - 3;
                if (y + text.Height > Bounds.Height - 3) y = spot.At.Y - spot.Radius - text.Height - 3;
                if (y < 3) y = spot.At.Y + spot.Radius + 3;
                context.DrawText(text, new Point(x, y));
            }
        }
    }
}

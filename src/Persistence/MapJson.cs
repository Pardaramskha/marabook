using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Persistence
{
    /// <summary>Une carte dans le manifeste (.plot v46, 16/11/2026) : ses dimensions,
    /// son échelle, ses zones, ses repères. Les TUILES n'y sont pas : une entrée du
    /// zip chacune, « cartes/&lt;id&gt;/&lt;z&gt;/&lt;x&gt;_&lt;y&gt;.jpg ». Tout est facultatif à la lecture.</summary>
    public static class MapJson
    {
        public const string Folder = "cartes/";

        public static string TilePrefix(string itemId) { return Folder + itemId + "/"; }

        private static List<object> Point(Pos point) { return new List<object> { Math.Round(point.X, 2), Math.Round(point.Y, 2) }; }

        private static Pos ReadPos(object node)
        {
            var list = Json.AsList(node);
            if (list == null || list.Count < 2) return new Pos(0, 0);
            return new Pos(Json.AsDouble(list[0], 0), Json.AsDouble(list[1], 0));
        }

        public static Dictionary<string, object> Build(MapInfo map)
        {
            var o = new Dictionary<string, object>();
            if (map.HasImage)
            {
                o["width"] = map.Width;
                o["height"] = map.Height;
                o["tile"] = map.TileSize;
                o["levels"] = map.Levels;
                o["ext"] = map.Extension ?? ".jpg";
                if (!string.IsNullOrEmpty(map.SourceName)) o["source"] = map.SourceName;
                if (map.SourceBytes > 0) o["sourceBytes"] = (double)map.SourceBytes;
            }
            if (map.WorldId != null) o["world"] = map.WorldId;
            if (map.WholeWorld) o["wholeWorld"] = true;
            if (map.View == Maps.ViewGlobe) o["view"] = map.View;
            if (map.ScaleKm > 0)
            {
                o["scaleA"] = Point(map.ScaleA);
                o["scaleB"] = Point(map.ScaleB);
                o["scaleKm"] = map.ScaleKm;
                if (!string.IsNullOrEmpty(map.ScaleNote)) o["scaleNote"] = map.ScaleNote;
            }
            if (map.Zones.Count > 0)
            {
                var zones = new List<object>();
                foreach (var zone in map.Zones)
                {
                    var z = new Dictionary<string, object>();
                    z["id"] = zone.Id;
                    z["name"] = zone.Name ?? "";
                    z["color"] = zone.Color ?? "#5B67D8";
                    if (zone.LinkId != null) z["link"] = zone.LinkId;
                    if (zone.Level != 0) z["level"] = zone.Level;
                    var points = new List<object>();
                    foreach (var point in zone.Points) points.Add(Point(point));
                    z["points"] = points;
                    zones.Add(z);
                }
                o["zones"] = zones;
            }
            if (map.Markers.Count > 0)
            {
                var markers = new List<object>();
                foreach (var marker in map.Markers)
                {
                    var m = new Dictionary<string, object>();
                    m["id"] = marker.Id;
                    m["name"] = marker.Name ?? "";
                    m["color"] = marker.Color ?? "#C0504D";
                    if (marker.LinkId != null) m["link"] = marker.LinkId;
                    if (marker.Level != 0) m["level"] = marker.Level;
                    m["at"] = Point(new Pos(marker.X, marker.Y));
                    markers.Add(m);
                }
                o["markers"] = markers;
            }
            return o;
        }

        public static MapInfo Read(object node)
        {
            var map = new MapInfo();
            var o = Json.AsObject(node);
            if (o == null) return map;
            map.Width = Math.Max(0, Json.AsInt(Json.Field(o, "width"), 0));
            map.Height = Math.Max(0, Json.AsInt(Json.Field(o, "height"), 0));
            map.TileSize = Math.Max(16, Json.AsInt(Json.Field(o, "tile"), MapInfo.DefaultTileSize));
            map.Levels = Math.Max(0, Math.Min(24, Json.AsInt(Json.Field(o, "levels"), 0)));
            var extension = Json.AsString(Json.Field(o, "ext"));
            map.Extension = extension == ".png" ? ".png" : ".jpg";
            map.SourceName = Json.AsString(Json.Field(o, "source"));
            map.SourceBytes = (long)Json.AsDouble(Json.Field(o, "sourceBytes"), 0);
            map.WorldId = Json.AsString(Json.Field(o, "world"));
            map.WholeWorld = Json.AsBool(Json.Field(o, "wholeWorld"), false);
            map.View = Json.AsString(Json.Field(o, "view")) == Maps.ViewGlobe ? Maps.ViewGlobe : Maps.ViewFlat;
            map.ScaleKm = Math.Max(0, Json.AsDouble(Json.Field(o, "scaleKm"), 0));
            if (map.ScaleKm > 0)
            {
                map.ScaleA = ReadPos(Json.Field(o, "scaleA"));
                map.ScaleB = ReadPos(Json.Field(o, "scaleB"));
                map.ScaleNote = Json.AsString(Json.Field(o, "scaleNote"));
            }
            var zones = Json.AsList(Json.Field(o, "zones"));
            if (zones != null)
                foreach (var zoneNode in zones)
                {
                    var z = Json.AsObject(zoneNode);
                    if (z == null) continue;
                    var zone = new MapZone();
                    var id = Json.AsString(Json.Field(z, "id"));
                    if (!string.IsNullOrEmpty(id)) zone.Id = id;
                    zone.Name = Json.AsString(Json.Field(z, "name")) ?? "";
                    zone.Color = Json.AsString(Json.Field(z, "color")) ?? "#5B67D8";
                    zone.LinkId = Json.AsString(Json.Field(z, "link"));
                    zone.Level = Math.Max(0, Math.Min(3, Json.AsInt(Json.Field(z, "level"), 0)));
                    var points = Json.AsList(Json.Field(z, "points"));
                    if (points != null) foreach (var point in points) zone.Points.Add(ReadPos(point));
                    map.Zones.Add(zone);
                }
            var markers = Json.AsList(Json.Field(o, "markers"));
            if (markers != null)
                foreach (var markerNode in markers)
                {
                    var m = Json.AsObject(markerNode);
                    if (m == null) continue;
                    var marker = new MapMarker();
                    var id = Json.AsString(Json.Field(m, "id"));
                    if (!string.IsNullOrEmpty(id)) marker.Id = id;
                    marker.Name = Json.AsString(Json.Field(m, "name")) ?? "";
                    marker.Color = Json.AsString(Json.Field(m, "color")) ?? "#C0504D";
                    marker.LinkId = Json.AsString(Json.Field(m, "link"));
                    marker.Level = Math.Max(0, Math.Min(3, Json.AsInt(Json.Field(m, "level"), 0)));
                    var at = ReadPos(Json.Field(m, "at"));
                    marker.X = at.X;
                    marker.Y = at.Y;
                    map.Markers.Add(marker);
                }
            return map;
        }
    }
}

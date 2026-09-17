using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook;
using Marabook.Model;

namespace Marabook.Persistence
{
    /// <summary>Export/import of a page gabarit (.usgab, JSON) — pour copier
    /// une maquette d'un livre ou d'une série à l'autre. Fournit aussi les
    /// (dé)sérialiseurs d'en-tête/pied partagés avec PlotFile.</summary>
    public static class GabaritFile
    {
        public const string Filter = "Gabarit Marabook (*.usgab)|*.usgab|Tous les fichiers (*.*)|*.*";

        public static Dictionary<string, object> BuildHeaderFooter(HeaderFooter value)
        {
            if (value == null) return null;
            var hf = new Dictionary<string, object>();
            hf["text"] = value.Text ?? "";
            if (value.FontFamily != null) hf["font"] = value.FontFamily;
            hf["sizePt"] = value.SizePt;
            if (value.Bold) hf["bold"] = true;
            if (value.Italic) hf["italic"] = true;
            hf["align"] = value.Align;
            if (value.Rich != null) hf["rich"] = BuildRich(value.Rich);
            return hf;
        }

        public static HeaderFooter ReadHeaderFooter(object node)
        {
            var obj = Json.AsObject(node);
            if (obj == null) return null;
            return new HeaderFooter
            {
                Text = Json.AsString(Json.Field(obj, "text")) ?? "",
                FontFamily = Json.AsString(Json.Field(obj, "font")),
                SizePt = Json.AsDouble(Json.Field(obj, "sizePt"), 10),
                Bold = Json.AsBool(Json.Field(obj, "bold"), false),
                Italic = Json.AsBool(Json.Field(obj, "italic"), false),
                Align = Json.AsString(Json.Field(obj, "align")) ?? "center",
                Rich = ReadRich(Json.Field(obj, "rich"))
            };
        }

        /// <summary>Rich zone (un paragraphe pivot) : runs stylés + alignement,
        /// mêmes clés que le sérialiseur de documents.</summary>
        private static Dictionary<string, object> BuildRich(TextParagraph paragraph)
        {
            var p = new Dictionary<string, object>();
            if (paragraph.AlignOverride != null) p["align"] = paragraph.AlignOverride;
            var runs = new List<object>();
            foreach (var run in paragraph.Runs)
            {
                if (run.IsLineBreak || run.IsRule || run.ImageId != null
                    || run.FootnoteId != null) continue; // hors périmètre décor
                var r = new Dictionary<string, object>();
                r["t"] = run.Text ?? "";
                if (run.Bold.HasValue) r["b"] = run.Bold.Value;
                if (run.Italic.HasValue) r["i"] = run.Italic.Value;
                if (run.Underline.HasValue) r["u"] = run.Underline.Value;
                if (run.Strike.HasValue) r["st"] = run.Strike.Value;
                if (run.Weight != null) r["w"] = run.Weight;
                if (run.Tracking.HasValue) r["trk"] = run.Tracking.Value;
                if (run.FontFamily != null) r["font"] = run.FontFamily;
                if (run.FontSize.HasValue) r["size"] = run.FontSize.Value;
                if (run.Color != null) r["color"] = run.Color;
                runs.Add(r);
            }
            p["runs"] = runs;
            return p;
        }

        private static TextParagraph ReadRich(object node)
        {
            var p = Json.AsObject(node);
            if (p == null) return null;
            var paragraph = new TextParagraph
            {
                AlignOverride = Json.AsString(Json.Field(p, "align"))
            };
            var runs = Json.AsList(Json.Field(p, "runs"));
            if (runs != null)
                foreach (var entry in runs)
                {
                    var r = Json.AsObject(entry);
                    if (r == null) continue;
                    var run = new TextRun { Text = Json.AsString(Json.Field(r, "t")) ?? "" };
                    var b = Json.Field(r, "b"); if (b is bool) run.Bold = (bool)b;
                    var i = Json.Field(r, "i"); if (i is bool) run.Italic = (bool)i;
                    var u = Json.Field(r, "u"); if (u is bool) run.Underline = (bool)u;
                    var st = Json.Field(r, "st"); if (st is bool) run.Strike = (bool)st;
                    run.Weight = Json.AsString(Json.Field(r, "w"));
                    var trk = Json.Field(r, "trk"); if (trk is double) run.Tracking = (double)trk;
                    run.FontFamily = Json.AsString(Json.Field(r, "font"));
                    var size = Json.Field(r, "size"); if (size is double) run.FontSize = (double)size;
                    run.Color = Json.AsString(Json.Field(r, "color"));
                    paragraph.Runs.Add(run);
                }
            return paragraph;
        }

        public static void Export(BinderItem gabarit, string path)
        {
            var root = new Dictionary<string, object>();
            root["type"] = "usgab";
            root["version"] = 1.0;
            root["title"] = gabarit.Title;
            if (gabarit.TemplateColor != null) root["color"] = gabarit.TemplateColor;
            if (gabarit.HeaderRecto != null) root["headerRecto"] = BuildHeaderFooter(gabarit.HeaderRecto);
            if (gabarit.FooterRecto != null) root["footerRecto"] = BuildHeaderFooter(gabarit.FooterRecto);
            if (gabarit.HeaderVerso != null) root["headerVerso"] = BuildHeaderFooter(gabarit.HeaderVerso);
            if (gabarit.FooterVerso != null) root["footerVerso"] = BuildHeaderFooter(gabarit.FooterVerso);
            root["headerGapMm"] = gabarit.HeaderGapMm;
            root["footerGapMm"] = gabarit.FooterGapMm;
            if (gabarit.HeaderHideFirst) root["headerHideFirst"] = true;
            if (gabarit.FooterHideFirst) root["footerHideFirst"] = true;
            File.WriteAllText(path, Json.Write(root), new UTF8Encoding(false));
        }

        /// <summary>Reads a .usgab into a fresh PageTemplate item (new id).</summary>
        public static BinderItem Import(string path)
        {
            var root = Json.AsObject(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
            if (root == null || Json.AsString(Json.Field(root, "type")) != "usgab")
                throw new InvalidDataException("Ce fichier n'est pas un gabarit Marabook.");
            return new BinderItem
            {
                Kind = ItemKind.PageTemplate,
                Title = Json.AsString(Json.Field(root, "title")) ?? "Gabarit importé",
                TemplateColor = Json.AsString(Json.Field(root, "color")),
                HeaderRecto = ReadHeaderFooter(Json.Field(root, "headerRecto")),
                FooterRecto = ReadHeaderFooter(Json.Field(root, "footerRecto")),
                HeaderVerso = ReadHeaderFooter(Json.Field(root, "headerVerso")),
                FooterVerso = ReadHeaderFooter(Json.Field(root, "footerVerso")),
                HeaderGapMm = Json.AsDouble(Json.Field(root, "headerGapMm"), 0),
                FooterGapMm = Json.AsDouble(Json.Field(root, "footerGapMm"), 0),
                HeaderHideFirst = Json.AsBool(Json.Field(root, "headerHideFirst"), false),
                FooterHideFirst = Json.AsBool(Json.Field(root, "footerHideFirst"), false)
            };
        }

        /// <summary>Deep copy for « copier vers un autre livre ».</summary>
        public static BinderItem Duplicate(BinderItem gabarit)
        {
            return new BinderItem
            {
                Kind = ItemKind.PageTemplate,
                Title = gabarit.Title,
                TemplateColor = gabarit.TemplateColor,
                HeaderRecto = gabarit.HeaderRecto == null ? null : gabarit.HeaderRecto.Clone(),
                FooterRecto = gabarit.FooterRecto == null ? null : gabarit.FooterRecto.Clone(),
                HeaderVerso = gabarit.HeaderVerso == null ? null : gabarit.HeaderVerso.Clone(),
                FooterVerso = gabarit.FooterVerso == null ? null : gabarit.FooterVerso.Clone(),
                HeaderGapMm = gabarit.HeaderGapMm,
                FooterGapMm = gabarit.FooterGapMm,
                HeaderHideFirst = gabarit.HeaderHideFirst,
                FooterHideFirst = gabarit.FooterHideFirst
            };
        }
    }
}

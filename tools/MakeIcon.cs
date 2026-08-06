using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>Generates app.ico — the Univers Sale logo: a ringed planet (the
/// universe) on an indigo rounded square, white ink. Multi-size ICO with
/// PNG-compressed entries (fine on Vista+). Run via make-icon.bat.</summary>
public static class MakeIcon
{
    public static void Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "app.ico";
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (var size in sizes)
            pngs.Add(DrawPng(size));
        WriteIco(output, sizes, pngs);
        Console.WriteLine("OK: " + Path.GetFullPath(output));
    }

    private static byte[] DrawPng(int s)
    {
        using (var bitmap = new Bitmap(s, s, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Rounded indigo square, subtle vertical gradient.
                var radius = s * 0.22f;
                using (var path = RoundedRect(0.5f, 0.5f, s - 1f, s - 1f, radius))
                using (var fill = new LinearGradientBrush(
                    new RectangleF(0, 0, s, s),
                    Color.FromArgb(255, 0x63, 0x66, 0xF1),
                    Color.FromArgb(255, 0x3A, 0x3D, 0x99),
                    LinearGradientMode.Vertical))
                    g.FillPath(fill, path);

                // Stars.
                using (var star = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                {
                    Dot(g, star, s * 0.26f, s * 0.24f, Math.Max(1f, s * 0.030f));
                    Dot(g, star, s * 0.76f, s * 0.20f, Math.Max(1f, s * 0.022f));
                    if (s >= 32) Dot(g, star, s * 0.82f, s * 0.62f, Math.Max(1f, s * 0.018f));
                }

                // The planet.
                var cx = s * 0.47f;
                var cy = s * 0.56f;
                var pr = s * 0.21f;
                using (var planet = new SolidBrush(Color.FromArgb(255, 0xF4, 0xF5, 0xFF)))
                    g.FillEllipse(planet, cx - pr, cy - pr, pr * 2, pr * 2);

                // The ring, tilted like an orbit — drawn after the planet so it
                // reads as passing in front.
                var state = g.Save();
                g.TranslateTransform(cx, cy);
                g.RotateTransform(-24f);
                var rw = s * 0.40f; // ring half-width
                var rh = s * 0.135f; // ring half-height
                using (var ringPen = new Pen(Color.FromArgb(255, 0xF4, 0xF5, 0xFF),
                    Math.Max(1f, s * 0.045f)))
                    g.DrawEllipse(ringPen, -rw, -rh, rw * 2, rh * 2);
                g.Restore(state);
            }

            using (var buffer = new MemoryStream())
            {
                bitmap.Save(buffer, ImageFormat.Png);
                return buffer.ToArray();
            }
        }
    }

    private static void Dot(Graphics g, Brush brush, float x, float y, float r)
    {
        g.FillEllipse(brush, x - r, y - r, r * 2, r * 2);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Minimal ICO container around the PNG frames.</summary>
    private static void WriteIco(string path, int[] sizes, List<byte[]> pngs)
    {
        using (var stream = new FileStream(path, FileMode.Create))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((short)0); // reserved
            writer.Write((short)1); // type: icon
            writer.Write((short)sizes.Length);
            var offset = 6 + 16 * sizes.Length;
            for (var i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // width
                writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // height
                writer.Write((byte)0);  // palette
                writer.Write((byte)0);  // reserved
                writer.Write((short)1); // planes
                writer.Write((short)32); // bpp
                writer.Write(pngs[i].Length);
                writer.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var png in pngs) writer.Write(png);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

/// <summary>Generates app.ico — the MARABOOK logo: a marabou stork's head
/// (bald dome, huge conical beak, neck ruff) in white ink on the indigo
/// rounded square. Multi-size ICO with PNG-compressed entries (fine on
/// Vista+). Run via make-icon.bat ; second arg = PNG preview path.</summary>
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
        if (args.Length > 1)
            File.WriteAllBytes(args[1], DrawPng(256));
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

                // La tête de marabout, encre blanche : crâne chauve, énorme
                // bec conique plongeant, collerette de plumes au col.
                var ink = Color.FromArgb(255, 0xF4, 0xF5, 0xFF);
                using (var white = new SolidBrush(ink))
                {
                    // Collerette (bas gauche) : trois bosses de plumes.
                    g.FillEllipse(white, s * 0.10f, s * 0.62f, s * 0.34f, s * 0.30f);
                    g.FillEllipse(white, s * 0.20f, s * 0.68f, s * 0.32f, s * 0.28f);
                    g.FillEllipse(white, s * 0.05f, s * 0.70f, s * 0.28f, s * 0.24f);

                    // Cou, du crâne à la collerette.
                    using (var neck = new GraphicsPath())
                    {
                        neck.AddPolygon(new[]
                        {
                            new PointF(s * 0.24f, s * 0.36f),
                            new PointF(s * 0.46f, s * 0.36f),
                            new PointF(s * 0.44f, s * 0.80f),
                            new PointF(s * 0.16f, s * 0.80f)
                        });
                        g.FillPath(white, neck);
                    }

                    // Crâne chauve, dôme légèrement penché vers le bec.
                    g.FillEllipse(white, s * 0.16f, s * 0.16f, s * 0.36f, s * 0.34f);

                    // Le bec : long cône massif qui plonge vers la droite.
                    using (var beak = new GraphicsPath())
                    {
                        beak.AddPolygon(new[]
                        {
                            new PointF(s * 0.40f, s * 0.22f),  // naissance haute
                            new PointF(s * 0.92f, s * 0.66f),  // pointe
                            new PointF(s * 0.38f, s * 0.46f)   // naissance basse
                        });
                        g.FillPath(white, beak);
                    }
                }

                // La commissure du bec, à l'encre du fond.
                using (var seam = new Pen(Color.FromArgb(255, 0x3F, 0x42, 0xA5),
                    Math.Max(1f, s * 0.022f)))
                    g.DrawLine(seam, s * 0.50f, s * 0.385f, s * 0.89f, s * 0.645f);

                // L'œil.
                using (var eye = new SolidBrush(Color.FromArgb(255, 0x3A, 0x3D, 0x99)))
                {
                    var er = Math.Max(1f, s * 0.042f);
                    g.FillEllipse(eye, s * 0.335f - er, s * 0.28f - er, er * 2, er * 2);
                }
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

using System;
using System.Globalization;

namespace Marabook.Model
{
    /// <summary>Un point (px). Portage Avalonia, lot P0 (27/09/2026) : le cœur
    /// — modèle, composition, PDF, échanges — ne parle plus System.Windows.
    /// Ces trois structures remplacent Point, Rect et Color/Brush dans tout
    /// ce qui n'est pas la vue ; la vue convertit à ses bords (View/Geo.cs).</summary>
    public struct Pos
    {
        public double X;
        public double Y;

        public Pos(double x, double y)
        {
            X = x;
            Y = y;
        }

        public override string ToString()
        {
            return X.ToString("0.##", CultureInfo.InvariantCulture) + ";" + Y.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Un rectangle (px), coin haut-gauche + dimensions.</summary>
    public struct Box
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public Box(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public double Right { get { return X + Width; } }
        public double Bottom { get { return Y + Height; } }

        public bool Contains(double x, double y)
        {
            return x >= X && x <= X + Width && y >= Y && y <= Y + Height;
        }

        public bool Contains(Pos point)
        {
            return Contains(point.X, point.Y);
        }

        /// <summary>Le rectangle agrandi de dx de chaque côté horizontal et
        /// dy de chaque côté vertical (négatif = réduit).</summary>
        public Box Inflate(double dx, double dy)
        {
            return new Box(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);
        }

        public override string ToString()
        {
            return X.ToString("0.##", CultureInfo.InvariantCulture) + ";" + Y.ToString("0.##", CultureInfo.InvariantCulture)
                + ";" + Width.ToString("0.##", CultureInfo.InvariantCulture) + ";" + Height.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Une couleur ARGB — l'« encre » d'une pièce composée, un
    /// surlignage, une couleur de run. Valeur, comparable ; le rendu en fait
    /// un pinceau de sa plate-forme.</summary>
    public struct Ink : IEquatable<Ink>
    {
        public byte A;
        public byte R;
        public byte G;
        public byte B;

        public static readonly Ink Black = Rgb(0, 0, 0);
        public static readonly Ink White = Rgb(0xFF, 0xFF, 0xFF);
        public static readonly Ink Gray = Rgb(0x80, 0x80, 0x80);

        public static Ink Rgb(byte r, byte g, byte b)
        {
            return Argb(0xFF, r, g, b);
        }

        public static Ink Argb(byte a, byte r, byte g, byte b)
        {
            var ink = new Ink();
            ink.A = a;
            ink.R = r;
            ink.G = g;
            ink.B = b;
            return ink;
        }

        /// <summary>Semi-transparente (les rendus papier l'ignorent).</summary>
        public bool IsTranslucent { get { return A < 0xFF; } }

        /// <summary>« #RGB », « #ARGB », « #RRGGBB » ou « #AARRGGBB » ;
        /// autre chose (null, nom de couleur, mal formé) = noir — la règle
        /// qu'avait ColorConverter, exceptions rattrapées.</summary>
        public static Ink Parse(string hex)
        {
            Ink ink;
            return TryParse(hex, out ink) ? ink : Black;
        }

        public static bool TryParse(string hex, out Ink ink)
        {
            ink = Black;
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return false;
            var digits = hex.Substring(1);
            foreach (var c in digits)
                if (!Uri.IsHexDigit(c)) return false;
            switch (digits.Length)
            {
                case 3:
                    ink = Rgb(Nibble(digits[0]), Nibble(digits[1]), Nibble(digits[2]));
                    return true;
                case 4:
                    ink = Argb(Nibble(digits[0]), Nibble(digits[1]), Nibble(digits[2]), Nibble(digits[3]));
                    return true;
                case 6:
                    ink = Rgb(Pair(digits, 0), Pair(digits, 2), Pair(digits, 4));
                    return true;
                case 8:
                    ink = Argb(Pair(digits, 0), Pair(digits, 2), Pair(digits, 4), Pair(digits, 6));
                    return true;
            }
            return false;
        }

        private static byte Nibble(char c)
        {
            var v = Convert.ToInt32(c.ToString(), 16);
            return (byte)(v * 17); // « F » → FF
        }

        private static byte Pair(string digits, int at)
        {
            return Convert.ToByte(digits.Substring(at, 2), 16);
        }

        /// <summary>« #RRGGBB » (l'alpha ne se persiste pas — comme ColorToHex).</summary>
        public string ToHex()
        {
            return "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
        }

        public bool Equals(Ink other)
        {
            return A == other.A && R == other.R && G == other.G && B == other.B;
        }

        public override bool Equals(object obj)
        {
            return obj is Ink && Equals((Ink)obj);
        }

        public override int GetHashCode()
        {
            return (A << 24) | (R << 16) | (G << 8) | B;
        }

        public static bool operator ==(Ink a, Ink b) { return a.Equals(b); }
        public static bool operator !=(Ink a, Ink b) { return !a.Equals(b); }

        public override string ToString()
        {
            return "#" + A.ToString("X2") + R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
        }
    }

    /// <summary>Les graisses nommées du pivot (TextRun.Weight) et leur valeur
    /// OpenType — la table qu'avait FlowConverter, sans FontWeight.</summary>
    public static class TextWeights
    {
        public const int Normal = 400;
        public const int Bold = 700;

        public static int Parse(string name)
        {
            switch (name)
            {
                case "Thin": return 100;
                case "Light": return 300;
                case "Medium": return 500;
                case "SemiBold": return 600;
                case "Bold": return Bold;
                case "Black": return 900;
                default: return Normal;
            }
        }

        /// <summary>Le nom d'une graisse fine ; null pour Normal et Bold, qui
        /// voyagent par le drapeau Bold.</summary>
        public static string Name(int weight)
        {
            switch (weight)
            {
                case 100: return "Thin";
                case 300: return "Light";
                case 500: return "Medium";
                case 600: return "SemiBold";
                case 900: return "Black";
                default: return null;
            }
        }
    }
}

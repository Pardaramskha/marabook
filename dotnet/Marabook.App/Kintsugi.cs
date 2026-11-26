using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE KINTSUGI D'« ASSERMENTÉ » (2.0.0, 25-26/11/2026, Rémi) — l'art de recoller la
    /// céramique à l'or. Cinq FÊLURES D'OR peintes par Rémi (assets/textures/kintsugi-1 à 5.png, des
    /// carrés que le trait traverse d'un bord à l'autre) sont posées là où elles ne gênent pas :
    ///  · aux quatre ANGLES de la zone centrale, sur le fond de la fenêtre — SOUS les vues : une carte,
    ///    une page les recouvrent ;
    ///  · au PIED de la Pile, et au pied du rail (la colonne de droite), PAR-DESSUS leur fond, très
    ///    pâles : du texte peut passer dessus, il doit rester lisible (Legible le mesure, la sonde le
    ///    vérifie).
    /// DEUX RÈGLES (26/11, Rémi) : les bouts de chaque fêlure tombent SUR LES BORDS de sa zone (une
    /// fêlure ne flotte pas au milieu de rien), et une fêlure a une TAILLE FIXE — élargir la Pile ne
    /// l'agrandit pas. Où le trait touche le bord de chaque image (mesuré sur les fichiers) :
    /// n° 1 bas et droite · n° 2 gauche et droite · n° 3 haut et gauche · n° 4 haut, gauche et bas ·
    /// n° 5 haut et bas (elle n'a pas trouvé de zone à sa mesure : non posée).
    /// Le premier jet (un réseau de fêlures tracé par le code, sur tout le fond) est retiré : « meh ».
    /// Une couche ne prend jamais la souris et ne peint rien sous un autre skin.</summary>
    internal sealed class KintsugiLayer : Control
    {
        public enum Zone { Corners, Pile, Rail }

        private readonly Zone _zone;
        /// <summary>La zone centrale, dans le repère de la couche des angles (posée par la coquille).</summary>
        public Rect Center { get; private set; }

        public KintsugiLayer(Zone zone)
        {
            _zone = zone;
            IsHitTestVisible = false;
            ClipToBounds = true;
            Tag = "kintsugi:" + zone.ToString().ToLowerInvariant();
        }

        public void PlaceCenter(Rect center)
        {
            if (Center == center) return;
            Center = center;
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) { return new Size(0, 0); }

        /// <summary>Peint une fêlure dans le carré <paramref name="box"/> — retournée (<paramref name="flipX"/>,
        /// <paramref name="flipY"/>) ou tournée d'un quart de tour à gauche (<paramref name="turn"/>) pour
        /// que ses bouts tombent sur les bords voulus.</summary>
        private static void Piece(DrawingContext context, int number, Rect box, bool flipX, bool flipY, bool turn, double opacity)
        {
            var image = Kintsugi.Image(number);
            if (image == null || box.Width < 8 || box.Height < 8) return;
            var around = Matrix.CreateTranslation(-box.Center.X, -box.Center.Y)
                * (turn ? Matrix.CreateRotation(-Math.PI / 2) : Matrix.Identity)
                * Matrix.CreateScale(flipX ? -1 : 1, flipY ? -1 : 1)
                * Matrix.CreateTranslation(box.Center.X, box.Center.Y);
            using (context.PushTransform(around))
            using (context.PushOpacity(opacity))
                context.DrawImage(image, new Rect(image.Size), box);
        }

        public override void Render(DrawingContext context)
        {
            if (!Kintsugi.On) return;
            double w = Bounds.Width, h = Bounds.Height;
            if (_zone == Zone.Corners)
            {
                // Les fêlures d'ANGLE : chacune dans un carré de taille FIXE calé dans son angle — ses deux
                // bouts tombent sur les deux bords de la zone. La n° 1 touche le bas et la droite de son
                // image, la n° 3 le haut et la gauche : retournées, elles vont dans n'importe quel angle.
                var c = Center;
                if (c.Width < 2 * Kintsugi.CornerSide || c.Height < 2 * Kintsugi.CornerSide) return; // trop étroit : elles se toucheraient
                var opacity = Chrome.Dark ? Kintsugi.GroundOpacity : Kintsugi.GroundOpacityLight;
                double big = Kintsugi.CornerSide, small = Kintsugi.CornerSide * 0.8, chip = Kintsugi.ChipSide;
                using (context.PushClip(c))
                {
                    Piece(context, 3, new Rect(c.X, c.Y, chip, chip), false, false, false, opacity);                         // haut gauche : un éclat
                    Piece(context, 1, new Rect(c.Right - small, c.Y, small, small), false, true, false, opacity);            // haut droit : le haut et la droite
                    Piece(context, 1, new Rect(c.X, c.Bottom - big, big, big), true, false, false, opacity);                 // bas gauche : le bas et la gauche
                    Piece(context, 3, new Rect(c.Right - chip, c.Bottom - chip, chip, chip), true, true, false, opacity);    // bas droit : un éclat
                }
                return;
            }
            // Une COLONNE : une fêlure qui la TRAVERSE, dans un carré de taille fixe calé en bas à gauche et
            // rogné au bord droit de la colonne — ses bouts tombent sur les deux bords tant que la colonne
            // n'est pas plus large que le carré. Plus large, elle ne traverserait plus : un éclat d'angle
            // la remplace. Rien ne s'agrandit jamais avec la colonne.
            if (w < Kintsugi.MinColumn || h < Kintsugi.ColumnSide * 1.5) return;
            var veil = Kintsugi.SideOpacity;
            var side = Kintsugi.ColumnSide;
            if (w > side) { Piece(context, 3, new Rect(0, h - Kintsugi.ChipSide, Kintsugi.ChipSide, Kintsugi.ChipSide), false, true, false, veil); return; }
            var square = new Rect(0, h - side, side, side);
            // La Pile : la n° 2, de la gauche à la droite. Le rail : la n° 4 tournée d'un quart de tour — elle
            // touchait le haut, la gauche et le bas de son image : la voilà sur la gauche, le bas et la droite.
            if (_zone == Zone.Pile) Piece(context, 2, square, false, false, false, veil);
            else Piece(context, 4, square, false, false, true, veil);
        }
    }

    internal static class Kintsugi
    {
        /// <summary>Sur le fond (sous les vues) : franche. Sur une colonne (par-dessus son texte) : un voile.</summary>
        internal const double GroundOpacity = 0.85, GroundOpacityLight = 0.9, SideOpacity = 0.22;
        /// <summary>Les tailles FIXES (en unités d'écran) : le carré d'une fêlure d'angle, celui d'un éclat (la n° 3 n'occupe
        /// qu'un coin de son image), celui d'une fêlure de colonne. Rien ne suit la taille des zones.</summary>
        internal const double CornerSide = 260, ChipSide = 380, ColumnSide = 360, MinColumn = 150;
        /// <summary>L'or le plus clair et le plus sombre des cinq images (mesurés sur les fichiers).</summary>
        internal static readonly Color Bright = Color.Parse("#F0D27C"), Deep = Color.Parse("#7A6220");

        /// <summary>Vrai sous « Assermenté ».</summary>
        public static bool On { get { return Skin.Current == Skins.MuseumOath; } }

        private static readonly Bitmap[] Images = new Bitmap[5];
        private static readonly bool[] Tried = new bool[5];

        /// <summary>La fêlure n° 1 à 5, lue une fois ; null si l'image manque.</summary>
        internal static Bitmap Image(int number)
        {
            var index = number - 1;
            if (index < 0 || index >= Images.Length) return null;
            if (Tried[index]) return Images[index];
            Tried[index] = true;
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("textures/kintsugi-" + number + ".png"))
                    if (stream != null) Images[index] = new Bitmap(stream);
            }
            catch (Exception) { Images[index] = null; }
            return Images[index];
        }

        internal static int ImagesFoundForProbe { get { var found = 0; for (var i = 1; i <= 5; i++) if (Image(i) != null) found++; return found; } }

        /// <summary>LA LISIBILITÉ sous une fêlure de colonne : le pire contraste entre une encre et le fond
        /// de la colonne quand le voile d'or (le plus clair, puis le plus sombre) passe sur les deux.</summary>
        internal static double Legible(Color ink, Color ground)
        {
            var worst = double.MaxValue;
            foreach (var gold in new[] { Bright, Deep })
                worst = Math.Min(worst, Chrome.ContrastRatio(Chrome.Blend(ink, gold, SideOpacity), Chrome.Blend(ground, gold, SideOpacity)));
            return worst;
        }
    }
}

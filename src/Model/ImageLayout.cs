using System;
using System.Windows;

namespace Marabook.Model
{
    /// <summary>Le placement d'une image dans un écrit (refonte des images,
    /// 0.50.0). L'image reste ANCRÉE dans le texte par son run (TextRun.ImageId,
    /// un offset plat, comme un appel de note) : c'est l'ancre qui décide de la
    /// page. Sa position, elle, se donne dans la ZONE DE TEXTE de cette page —
    /// X et Y en pixels depuis le coin haut-gauche du bloc de texte — ou reste
    /// ATTACHÉE à la ligne de l'ancre (Y nul) : l'image suit alors le texte,
    /// juste sous la ligne qui porte l'ancre (à sa place quand la ligne est
    /// vide, cas d'un paragraphe réduit à l'image). Les raccourcis d'alignement
    /// posent X ou Y ; le glisser-déposer aussi. Rien ne verrouille l'image.
    ///
    /// Habillage : « exclude » (le texte au-dessus et en dessous) ou « wrap »
    /// (le texte de part et d'autre, dans les limites de la zone de texte).
    /// Free : le placement libre sort de la zone de texte (jusqu'aux bords de
    /// la page). Les aides de géométrie sont pures (testables sans WPF).</summary>
    public class ImageLayout
    {
        public const string WrapExclude = "exclude";
        public const string WrapAround = "wrap";

        /// <summary>Le pas de la grille de placement : 0,5 cm.</summary>
        public const double GridStepPx = 5 * 96 / 25.4;

        /// <summary>Taille minimale d'une image redimensionnée, en px.</summary>
        public const double MinSizePx = 16;

        /// <summary>Le nom de fichier d'origine (affichage, enregistrement).</summary>
        public string Name;

        /// <summary>Taille affichée en px ; 0 = naturelle (calculée à la
        /// composition depuis les pixels de l'image, réduite à la colonne).</summary>
        public double Width, Height;

        /// <summary>Position dans la zone de texte de la page de l'ancre, en px.
        /// X nul = centrée ; Y nul = attachée à la ligne de l'ancre.</summary>
        public double? X, Y;

        public string Wrap = WrapExclude;
        public bool Free;

        public bool IsAttached { get { return !Y.HasValue; } }
        public bool IsWrap { get { return Wrap == WrapAround; } }

        public ImageLayout Clone()
        {
            return (ImageLayout)MemberwiseClone();
        }

        public bool SameAs(ImageLayout other)
        {
            if (other == null) return false;
            return Name == other.Name && Close(Width, other.Width) && Close(Height, other.Height)
                && CloseOpt(X, other.X) && CloseOpt(Y, other.Y)
                && Wrap == other.Wrap && Free == other.Free;
        }

        private static bool Close(double a, double b) { return Math.Abs(a - b) < 0.01; }

        private static bool CloseOpt(double? a, double? b)
        {
            if (!a.HasValue || !b.HasValue) return a.HasValue == b.HasValue;
            return Close(a.Value, b.Value);
        }

        // ============================================================ géométrie pure

        /// <summary>La valeur aimantée au pas de la grille.</summary>
        public static double Snap(double value, double step)
        {
            if (step <= 0) return value;
            return Math.Round(value / step) * step;
        }

        /// <summary>Réduit proportionnellement une taille pour qu'elle tienne
        /// dans un cadre (jamais agrandie).</summary>
        public static void FitInside(ref double width, ref double height, double maxWidth, double maxHeight)
        {
            if (width <= 0 || height <= 0) return;
            var scale = 1.0;
            if (maxWidth > 0 && width > maxWidth) scale = Math.Min(scale, maxWidth / width);
            if (maxHeight > 0 && height > maxHeight) scale = Math.Min(scale, maxHeight / height);
            if (scale < 1)
            {
                width *= scale;
                height *= scale;
            }
        }

        /// <summary>Ramène un rectangle dans un cadre (position seulement : la
        /// taille est supposée déjà réduite par FitInside).</summary>
        public static Rect ClampInto(Rect rect, Rect area)
        {
            var x = Math.Max(area.X, Math.Min(area.Right - rect.Width, rect.X));
            var y = Math.Max(area.Y, Math.Min(area.Bottom - rect.Height, rect.Y));
            return new Rect(x, y, rect.Width, rect.Height);
        }

        /// <summary>L'abscisse (dans la zone de texte) d'un alignement
        /// horizontal : « left », « center », « right ».</summary>
        public static double AlignedX(string align, double width, double areaWidth)
        {
            switch (align)
            {
                case "left": return 0;
                case "right": return Math.Max(0, areaWidth - width);
                default: return Math.Max(0, (areaWidth - width) / 2);
            }
        }

        /// <summary>L'ordonnée (dans la zone de texte) d'un alignement
        /// vertical : « top », « center », « bottom ».</summary>
        public static double AlignedY(string align, double height, double areaHeight)
        {
            switch (align)
            {
                case "top": return 0;
                case "bottom": return Math.Max(0, areaHeight - height);
                default: return Math.Max(0, (areaHeight - height) / 2);
            }
        }

        /// <summary>L'alignement horizontal que traduit une position (pour
        /// cocher le bouton du ruban), ou null quand elle n'en est aucun.</summary>
        public static string HorizontalAlignOf(double? x, double width, double areaWidth)
        {
            var value = x ?? AlignedX("center", width, areaWidth);
            const double tolerance = 0.75;
            if (Math.Abs(value - AlignedX("left", width, areaWidth)) < tolerance) return "left";
            if (Math.Abs(value - AlignedX("right", width, areaWidth)) < tolerance) return "right";
            if (Math.Abs(value - AlignedX("center", width, areaWidth)) < tolerance) return "center";
            return null;
        }

        /// <summary>L'alignement vertical que traduit une position, ou null
        /// (une image attachée à sa ligne n'en a aucun).</summary>
        public static string VerticalAlignOf(double? y, double height, double areaHeight)
        {
            if (!y.HasValue) return null;
            const double tolerance = 0.75;
            if (Math.Abs(y.Value - AlignedY("top", height, areaHeight)) < tolerance) return "top";
            if (Math.Abs(y.Value - AlignedY("bottom", height, areaHeight)) < tolerance) return "bottom";
            if (Math.Abs(y.Value - AlignedY("center", height, areaHeight)) < tolerance) return "center";
            return null;
        }

        /// <summary>Le rectangle redimensionné par une poignée (0 = haut-gauche,
        /// puis dans le sens horaire : 1 haut, 2 haut-droite, 3 droite, 4
        /// bas-droite, 5 bas, 6 bas-gauche, 7 gauche), le côté opposé fixe.
        /// Proportionnel sauf <paramref name="free"/> (Maj enfoncée) ; jamais
        /// sous MinSizePx.</summary>
        public static Rect Resize(Rect start, int handle, double dx, double dy, bool free)
        {
            var left = start.X;
            var top = start.Y;
            var right = start.Right;
            var bottom = start.Bottom;
            var movesLeft = handle == 0 || handle == 6 || handle == 7;
            var movesRight = handle == 2 || handle == 3 || handle == 4;
            var movesTop = handle == 0 || handle == 1 || handle == 2;
            var movesBottom = handle == 4 || handle == 5 || handle == 6;
            if (movesLeft) left = Math.Min(right - MinSizePx, left + dx);
            if (movesRight) right = Math.Max(left + MinSizePx, right + dx);
            if (movesTop) top = Math.Min(bottom - MinSizePx, top + dy);
            if (movesBottom) bottom = Math.Max(top + MinSizePx, bottom + dy);
            var width = right - left;
            var height = bottom - top;
            if (!free && start.Width > 0 && start.Height > 0)
            {
                var ratio = start.Height / start.Width;
                var horizontalOnly = movesLeft || movesRight;
                var verticalOnly = movesTop || movesBottom;
                if (horizontalOnly && verticalOnly)
                {
                    // Un coin : la plus grande variation relative gagne.
                    var byWidth = width / start.Width;
                    var byHeight = height / start.Height;
                    if (Math.Abs(byWidth - 1) >= Math.Abs(byHeight - 1)) height = width * ratio;
                    else width = height / ratio;
                }
                else if (horizontalOnly) height = width * ratio;
                else if (verticalOnly) width = height / ratio;
                if (width < MinSizePx) { width = MinSizePx; height = width * ratio; }
                if (height < MinSizePx) { height = MinSizePx; width = height / ratio; }
                // Le côté opposé reste fixe.
                if (movesLeft) left = right - width; else right = left + width;
                if (movesTop) top = bottom - height; else bottom = top + height;
            }
            return new Rect(Math.Min(left, right), Math.Min(top, bottom), width, height);
        }
    }
}

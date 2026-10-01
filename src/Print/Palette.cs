using Marabook.Model;

namespace Marabook.Print
{
    /// <summary>L'encre d'une pièce : explicite (run, style, noir), ou un rôle
    /// du thème que le rendu écran résout en direct (la marque estompée d'un
    /// [[lien]], le texte d'accent d'un lien montré). Le PDF prend la valeur
    /// figée dans Ink, celle de la palette au moment de la composition.</summary>
    public enum InkRole
    {
        Explicit,
        /// <summary>Aucune couleur posée (ni run, ni style) : Ink vaut le
        /// noir pour le papier et les exports ; l'écran montre l'encre du
        /// papier du thème (claire en mode sombre sans papier blanc) — un
        /// repère visuel, jamais une couleur du document (30/09).</summary>
        Default,
        Faint,
        Accent
    }

    /// <summary>Le surlignage d'une pièce : aucun, explicite (run), la teinte
    /// d'accent d'un lien montré, ou la teinte semi-transparente d'un passage
    /// annoté — écran seulement, jamais au papier.</summary>
    public enum HighlightRole
    {
        None,
        Explicit,
        Accent,
        Annotation
    }

    /// <summary>Les couleurs du thème que le compositeur pose dans les pièces
    /// (portage Avalonia, P0 : avant, il lisait les pinceaux de Chrome). L'app
    /// recopie les siennes à chaque bascule de thème ; les défauts sont ceux du
    /// thème clair.</summary>
    public sealed class CompositionPalette
    {
        public Ink FaintText = Ink.Rgb(0x8A, 0x91, 0xA3);
        public Ink AccentStrong = Ink.Rgb(0x45, 0x4F, 0xB8);
        public Ink AccentTint = Ink.Rgb(0xED, 0xEF, 0xFC);
        public Ink AnnotationTint = Ink.Argb(0x55, 0xF1, 0xC4, 0x0F);

        public static readonly CompositionPalette Current = new CompositionPalette();
    }
}

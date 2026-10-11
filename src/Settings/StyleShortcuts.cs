using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Settings
{
    /// <summary>Les MACROS DE STYLES (1.0.5, Rémi) : un style peut porter un
    /// raccourci (ParagraphStyle.Shortcut, notation des réglages) ; dans
    /// l'éditeur, la combinaison applique le style. La recherche se fait
    /// APRÈS les actions de l'éditeur (un raccourci d'action l'emporte, le
    /// panneau des styles prévient du doublon) et parmi les styles visibles
    /// de l'écrit ouvert.</summary>
    public static class StyleShortcuts
    {
        /// <summary>Le style que cette touche déclenche, ou null. Les
        /// modificateurs doivent correspondre exactement.</summary>
        public static ParagraphStyle Find(IEnumerable<ParagraphStyle> styles, string key, KeyModifiers modifiers)
        {
            if (styles == null || string.IsNullOrEmpty(key)) return null;
            modifiers &= KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt;
            foreach (var style in styles)
            {
                if (string.IsNullOrEmpty(style.Shortcut)) continue;
                string wanted;
                KeyModifiers wantedModifiers;
                if (!AppSettings.ParseGesture(style.Shortcut, out wanted, out wantedModifiers)) continue;
                if (AppSettings.SameKey(wanted, key) && wantedModifiers == modifiers) return style;
            }
            return null;
        }

        /// <summary>Le style qui porte déjà ce geste (autre que excludeId), ou null.</summary>
        public static ParagraphStyle Holder(IEnumerable<ParagraphStyle> styles, string gesture, string excludeId)
        {
            string key;
            KeyModifiers modifiers;
            if (!AppSettings.ParseGesture(gesture, out key, out modifiers)) return null;
            foreach (var style in styles)
            {
                if (style.Id == excludeId || string.IsNullOrEmpty(style.Shortcut)) continue;
                string other;
                KeyModifiers otherModifiers;
                if (AppSettings.ParseGesture(style.Shortcut, out other, out otherModifiers)
                    && AppSettings.SameKey(other, key) && otherModifiers == modifiers) return style;
            }
            return null;
        }
    }
}

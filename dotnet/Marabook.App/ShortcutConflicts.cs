using System;
using System.Collections.Generic;
using Marabook.Model;
using AppSettings = Marabook.Settings.AppSettings;
using KeyModifiers = Marabook.Settings.KeyModifiers;

namespace Marabook.App
{
    /// <summary>LE SIGNALEMENT D'UN RACCOURCI DÉJÀ PRIS (1.0.5, Rémi : « on
    /// commence à avoir beaucoup de raccourcis ») — une seule définition,
    /// pour les Préférences (actions) et le panneau des styles (macros) :
    /// — une autre action de Marabook ;
    /// — un style qui porte déjà la combinaison ;
    /// — une combinaison RÉSERVÉE PAR LE SYSTÈME, que Marabook ne verra
    ///   jamais (⌘Q, ⌘W, ⌘H, ⌘M, ⌘Tab, ⌘Espace… ; Alt+F4, Ctrl+Échap…).
    /// Rend le message à montrer, ou null.</summary>
    public static class ShortcutConflicts
    {
        // Les combinaisons que le système garde pour lui (notation des
        // réglages : « Ctrl » = ⌘ sur macOS), avec ce qu'elles font.
        private static readonly KeyValuePair<string, string>[] MacReserved =
        {
            new KeyValuePair<string, string>("Ctrl+Q", "quitter l'application"),
            new KeyValuePair<string, string>("Ctrl+W", "fermer la fenêtre"),
            new KeyValuePair<string, string>("Ctrl+H", "masquer l'application"),
            new KeyValuePair<string, string>("Ctrl+Alt+H", "masquer les autres"),
            new KeyValuePair<string, string>("Ctrl+M", "réduire la fenêtre"),
            new KeyValuePair<string, string>("Ctrl+Tab", "changer d'application"),
            new KeyValuePair<string, string>("Ctrl+Shift+Tab", "changer d'application"),
            new KeyValuePair<string, string>("Ctrl+Space", "Spotlight"),
            new KeyValuePair<string, string>("Ctrl+OemComma", "les Préférences"),
            new KeyValuePair<string, string>("Ctrl+Shift+D3", "capture d'écran"),
            new KeyValuePair<string, string>("Ctrl+Shift+D4", "capture d'écran"),
            new KeyValuePair<string, string>("Ctrl+Shift+D5", "capture d'écran"),
            new KeyValuePair<string, string>("Ctrl+Shift+Q", "fermer la session"),
            new KeyValuePair<string, string>("Ctrl+Alt+Escape", "forcer à quitter"),
            new KeyValuePair<string, string>("Ctrl+Alt+D", "afficher ou masquer le Dock"),
        };

        private static readonly KeyValuePair<string, string>[] WindowsReserved =
        {
            new KeyValuePair<string, string>("Alt+F4", "fermer la fenêtre"),
            new KeyValuePair<string, string>("Ctrl+Escape", "le menu Démarrer"),
            new KeyValuePair<string, string>("Alt+Tab", "changer de fenêtre"),
            new KeyValuePair<string, string>("Alt+Shift+Tab", "changer de fenêtre"),
            new KeyValuePair<string, string>("Ctrl+Shift+Escape", "le Gestionnaire des tâches"),
            new KeyValuePair<string, string>("Alt+Space", "le menu de la fenêtre"),
        };

        /// <summary>Ce que le système fait de ce geste, ou null s'il est libre.</summary>
        public static string SystemUse(string gesture)
        {
            if (string.IsNullOrEmpty(gesture)) return null;
            var table = AppPlatform.IsMac ? MacReserved : WindowsReserved;
            foreach (var pair in table)
                if (SameGesture(pair.Key, gesture)) return pair.Value;
            return null;
        }

        public static bool SameGesture(string a, string b)
        {
            string keyA, keyB;
            KeyModifiers modsA, modsB;
            if (!AppSettings.ParseGesture(a, out keyA, out modsA) || !AppSettings.ParseGesture(b, out keyB, out modsB)) return false;
            return AppSettings.SameKey(keyA, keyB) && modsA == modsB;
        }

        /// <summary>Le message d'un doublon pour ce geste — null si libre.
        /// excludeActionId : l'action qu'on règle (pas un doublon avec
        /// elle-même) ; styles / excludeStyleId : les macros de styles.</summary>
        public static string Describe(string gesture, string excludeActionId, IEnumerable<ParagraphStyle> styles, string excludeStyleId)
        {
            if (string.IsNullOrEmpty(gesture)) return null;
            var shown = AppSettings.DisplayGesture(gesture);
            var system = SystemUse(gesture);
            if (system != null)
                return shown + " est réservé par le système (" + system + ") : Marabook ne le recevra jamais. Choisissez une autre combinaison.";
            foreach (var other in AppSettings.Actions)
            {
                if (other.Id == excludeActionId) continue;
                var otherGesture = AppSettings.Gesture(other.Id);
                if (string.IsNullOrEmpty(otherGesture) || !SameGesture(otherGesture, gesture)) continue;
                return shown + " est déjà le raccourci de « " + other.Name + " » (" + other.Category + ").\n\n"
                    + "Les deux le gardent ; l'action l'emporte sur un style, et le premier atteint l'emporte entre actions. Changez l'un des deux si cela gêne.";
            }
            if (styles != null)
            {
                var holder = Marabook.Settings.StyleShortcuts.Holder(styles, gesture, excludeStyleId);
                if (holder != null)
                    return shown + " applique déjà le style « " + holder.Name + " ».\n\nLes deux le gardent ; le premier de la liste l'emporte. Changez l'un des deux si cela gêne.";
            }
            return null;
        }
    }
}

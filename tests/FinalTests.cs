using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C37 — les patchs de la dernière ligne droite (22/09) : le
    /// rapport de plantage (texte, nom de fichier, libellé), la table des
    /// raccourcis de l'éditeur, la remise à zéro des succès.</summary>
    public static class FinalTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C37 — rapports de plantage, raccourcis de l'éditeur, succès (22/09)");
            CrashReports(t);
            EditorShortcuts(t);
            AchievementsReset(t);
        }

        private static void CrashReports(Harness t)
        {
            var when = new DateTime(2026, 9, 22, 21, 4, 37);
            t.Check(CrashReport.FileName(when) == "plantage-2026-09-22-21h04m37.md", "le nom du rapport porte la date, triable");

            Exception error;
            try { throw new InvalidOperationException("Le compositeur a perdu la page", new ArgumentNullException("page")); }
            catch (Exception caught) { error = caught; }
            var context = new List<string> { "Projet : Mon roman", "Élément ouvert : Text", "Modules : (aucun)" };
            var text = CrashReport.Render(error, "0.43.0-beta", context, when);
            t.Check(text.StartsWith("# Rapport de plantage Marabook"), "le rapport est un Markdown titré");
            t.Check(text.Contains("| Marabook | 0.43.0-beta |"), "la version de l'application est dans le tableau");
            t.Check(text.Contains("| Date | 22/09/2026 21:04:37 |"), "la date au format français");
            t.Check(text.Contains("| Projet | Mon roman |") && text.Contains("| Élément ouvert | Text |"), "le contexte devient des lignes du tableau");
            t.Check(text.Contains("**System.InvalidOperationException** — Le compositeur a perdu la page"), "le type et le message de l'erreur");
            t.Check(text.Contains("```") && text.Contains("FinalTests"), "la pile d'appels dans un bloc de code");
            t.Check(text.Contains("### Cause 1") && text.Contains("ArgumentNullException"), "la cause interne suit");
            t.Check(!text.Contains("Mon roman.plot"), "aucun chemin de fichier n'est écrit");

            var nothing = CrashReport.Render(null, "0.43.0-beta", null, when);
            t.Check(nothing.Contains("(erreur inconnue)"), "sans exception, le rapport le dit");

            var folder = Path.Combine(Path.GetTempPath(), "marabook-c37-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, CrashReport.FileName(when));
                File.WriteAllText(path, text);
                t.Check(CrashReport.Label(path) == "22/09/2026 21:04 — InvalidOperationException", "le libellé de la liste : date courte et nom court de l'erreur — " + CrashReport.Label(path));
            }
            finally { try { Directory.Delete(folder, true); } catch { } }
        }

        private static void EditorShortcuts(Harness t)
        {
            var saved = new Dictionary<string, string>(AppSettings.Shortcuts);
            try
            {
                AppSettings.Shortcuts.Clear();
                // Les touches voyagent par leur nom WPF (Key.B.ToString()) — le
                // cœur ne connaît plus l'énumération (P0) ; la vue lui passe
                // e.Key.ToString() et Geo.ToCore(Keyboard.Modifiers).
                // (les noms sont ceux de Key.X.ToString() : « B », « Return », « F7 », « None »)
                t.Check(AppSettings.EditorActionFor("B", KeyModifiers.Control) == "bold", "Ctrl+B : gras (défaut)");
                t.Check(AppSettings.EditorActionFor("I", KeyModifiers.Control) == "italic", "Ctrl+I : italique (défaut)");
                t.Check(AppSettings.EditorActionFor("Return", KeyModifiers.Control) == "page-break", "Ctrl+Entrée : saut de page, de « Mise en page »");
                t.Check(AppSettings.EditorActionFor("Enter", KeyModifiers.Control) == "page-break", "Enter et Return sont la même touche (doublon de l'énumération WPF)");
                t.Check(AppSettings.EditorActionFor("b", KeyModifiers.Control) == "bold", "le nom d'une touche se compare sans la casse (Enum.Parse ignorait la casse)");
                t.Check(AppSettings.EditorActionFor("B", KeyModifiers.Control | KeyModifiers.Shift) == null, "Ctrl+Maj+B : rien — les modificateurs doivent correspondre");
                t.Check(AppSettings.EditorActionFor("B", KeyModifiers.None) == null, "B seul : rien");
                t.Check(AppSettings.EditorActionFor("S", KeyModifiers.Control) == null, "Ctrl+S n'est pas un geste de l'éditeur : il remonte à la fenêtre");
                t.Check(AppSettings.EditorActionFor("F7", KeyModifiers.None) == null, "une fonction sans raccourci (séparateur, point médian…) ne répond à rien");
                t.Check(AppSettings.EditorActionFor("None", KeyModifiers.Control) == null, "Key.None ne déclenche rien");

                AppSettings.Shortcuts["bold"] = "Ctrl+Shift+G";
                AppSettings.Shortcuts["middle-dot"] = "F7";
                t.Check(AppSettings.EditorActionFor("G", KeyModifiers.Control | KeyModifiers.Shift) == "bold", "gras personnalisé en Ctrl+Maj+G");
                t.Check(AppSettings.EditorActionFor("B", KeyModifiers.Control) == null, "Ctrl+B ne répond plus une fois le gras déplacé");
                t.Check(AppSettings.EditorActionFor("F7", KeyModifiers.None) == "middle-dot", "un raccourci créé pour le point médian");
                AppSettings.Shortcuts["bold"] = "";
                t.Check(AppSettings.EditorActionFor("B", KeyModifiers.Control) == null, "raccourci retiré : plus de gras au clavier");

                string keyName;
                KeyModifiers modifiers;
                t.Check(AppSettings.ParseGesture("Ctrl+Maj+G", out keyName, out modifiers) && keyName == "G"
                    && modifiers == (KeyModifiers.Control | KeyModifiers.Shift), "ParseGesture : « Maj » vaut Shift, la touche est le dernier jeton");
                t.Check(!AppSettings.ParseGesture("Ctrl+", out keyName, out modifiers), "ParseGesture : un modificateur sans touche n'est pas un geste");
                t.Check(!AppSettings.ParseGesture("A+B", out keyName, out modifiers), "ParseGesture : deux touches, geste mal formé");

                var editorActions = 0;
                foreach (var action in AppSettings.Actions) if (action.Category == AppSettings.EditorCategory) editorActions++;
                t.Check(editorActions == 16, "seize actions dans la catégorie Éditeur des Préférences (Lignes lâches, 09/10) — " + editorActions);
                t.Check(AppSettings.Definition("compile").Name == "Compiler les écrits", "« Compiler les écrits » (ex-manuscrit)");
                // Le fichier VERSION (release.ps1, installeur) suit AppVersion.
                var versionFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VERSION");
                t.Check(File.Exists(versionFile) && File.ReadAllText(versionFile).Trim() == Marabook.Model.AppInfo.Version,
                    "VERSION vaut AppInfo.Version (" + Marabook.Model.AppInfo.Version + ")");
            }
            finally
            {
                AppSettings.Shortcuts.Clear();
                foreach (var kv in saved) AppSettings.Shortcuts[kv.Key] = kv.Value;
            }
        }

        private static void AchievementsReset(Harness t)
        {
            var savedAchievements = new Dictionary<string, string>(AppSettings.Achievements);
            var savedBlank = new Dictionary<string, string>(AppSettings.BlankSince);
            int deleted = AppSettings.PermanentlyDeleted, streak = AppSettings.UsageStreak,
                zoom = AppSettings.WordsAtMaxZoom, calm = AppSettings.WordsInCalm;
            var lastDay = AppSettings.UsageLastDay;
            try
            {
                AppSettings.Achievements["premier-projet"] = "2026-09-22 10:00";
                AppSettings.PermanentlyDeleted = 12;
                AppSettings.UsageStreak = 4;
                AppSettings.UsageLastDay = "2026-09-22";
                AppSettings.WordsAtMaxZoom = 300;
                AppSettings.WordsInCalm = 500;
                AppSettings.BlankSince["x"] = "2026-09-01";
                AppSettings.ResetAchievements();
                t.Check(AppSettings.Achievements.Count == 0, "réinitialisés : plus aucun succès");
                t.Check(AppSettings.PermanentlyDeleted == 0 && AppSettings.UsageStreak == 0 && AppSettings.UsageLastDay == null
                    && AppSettings.WordsAtMaxZoom == 0 && AppSettings.WordsInCalm == 0 && AppSettings.BlankSince.Count == 0,
                    "et leurs compteurs repartent de zéro");
            }
            finally
            {
                AppSettings.Achievements.Clear();
                foreach (var kv in savedAchievements) AppSettings.Achievements[kv.Key] = kv.Value;
                AppSettings.BlankSince.Clear();
                foreach (var kv in savedBlank) AppSettings.BlankSince[kv.Key] = kv.Value;
                AppSettings.PermanentlyDeleted = deleted;
                AppSettings.UsageStreak = streak;
                AppSettings.UsageLastDay = lastDay;
                AppSettings.WordsAtMaxZoom = zoom;
                AppSettings.WordsInCalm = calm;
            }
        }
    }
}

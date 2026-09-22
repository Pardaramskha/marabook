using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Marabook.Model;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests.Ui
{
    /// <summary>La sonde des succès (22/09/2026) : la vraie MainWindow, hors
    /// écran, ouvre le projet « tout succès » (AchievementFixture) et l'on
    /// vérifie que le PIPELINE de l'application — Gather, Earned, Unlock,
    /// réglages — débloque bien chaque succès, puis les succès à événement
    /// atteignables sans dialogue (boulette, Yolo, crétin, nettoyage), les
    /// compteurs globaux (zoom, calme, accent, raccourcis, correcteur,
    /// corbeille) et le temps (série, 8 h, 2 h, page blanche, gigaoctet)
    /// par leurs champs. Pas atteignables ici : À propos et Imprimer
    /// (dialogues), Complétionniste et Minimaliste (dialogue PDF), Gen Zer
    /// (pas d'EPUB), « deux heures max » (mille pages composées).
    ///
    /// Les succès et compteurs en mémoire sont sauvegardés puis restaurés
    /// (settings.json est l'affaire de l'appelant, A1Probe).</summary>
    public static class AchievementsProbe
    {
        private static int _failures;

        /// <summary>Point d'entrée autonome (/main:Marabook.Tests.Ui.AchievementsProbe)
        /// pour relancer cette sonde seule ; settings.json sauvegardé puis restauré.</summary>
        [STAThread]
        public static int Main()
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook", "settings.json");
            var backup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            try
            {
                Run();
            }
            finally
            {
                try
                {
                    if (backup != null) File.WriteAllBytes(settingsPath, backup);
                    else if (File.Exists(settingsPath)) File.Delete(settingsPath);
                }
                catch { }
            }
            if (Application.Current != null)
            {
                Application.Current.Shutdown();
                DoEvents();
            }
            Console.WriteLine(_failures == 0 ? "SONDE SUCCÈS OK" : "*** SONDE SUCCÈS : " + _failures + " échec(s) ***");
            return _failures == 0 ? 0 : 1;
        }

        public static int Run()
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-succes");
            var savedAchievements = new Dictionary<string, string>(AppSettings.Achievements);
            var savedBlank = new Dictionary<string, string>(AppSettings.BlankSince);
            var savedShortcuts = new Dictionary<string, string>(AppSettings.Shortcuts);
            var savedDeleted = AppSettings.PermanentlyDeleted;
            var savedZoomWords = AppSettings.WordsAtMaxZoom;
            var savedCalmWords = AppSettings.WordsInCalm;
            var savedStreak = AppSettings.UsageStreak;
            var savedAccent = AppSettings.AccentColor;
            var savedZoom = AppSettings.Zoom;
            var savedFlags = new[] { AppSettings.SpellEnabled, AppSettings.GrammarEnabled, AppSettings.TypographyEnabled, AppSettings.StyleEnabled };
            try
            {
                Directory.CreateDirectory(dir);
                AppSettings.Achievements.Clear();
                AppSettings.BlankSince.Clear();
                AppSettings.PermanentlyDeleted = 0;
                AppSettings.WordsAtMaxZoom = 0;
                AppSettings.WordsInCalm = 0;
                Probe(dir);
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE SUCCÈS EN ÉCHEC : " + error);
                _failures++;
            }
            finally
            {
                AppSettings.Achievements.Clear();
                foreach (var pair in savedAchievements) AppSettings.Achievements[pair.Key] = pair.Value;
                AppSettings.BlankSince.Clear();
                foreach (var pair in savedBlank) AppSettings.BlankSince[pair.Key] = pair.Value;
                AppSettings.Shortcuts.Clear();
                foreach (var pair in savedShortcuts) AppSettings.Shortcuts[pair.Key] = pair.Value;
                AppSettings.PermanentlyDeleted = savedDeleted;
                AppSettings.WordsAtMaxZoom = savedZoomWords;
                AppSettings.WordsInCalm = savedCalmWords;
                AppSettings.UsageStreak = savedStreak;
                AppSettings.AccentColor = savedAccent;
                AppSettings.Zoom = savedZoom;
                AppSettings.SpellEnabled = savedFlags[0];
                AppSettings.GrammarEnabled = savedFlags[1];
                AppSettings.TypographyEnabled = savedFlags[2];
                AppSettings.StyleEnabled = savedFlags[3];
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
            return _failures;
        }

        private static void Probe(string dir)
        {
            var path = Path.Combine(dir, "succes.plot");
            AchievementFixture.Save(AchievementFixture.Build(), path);
            var pathWithout = Path.Combine(dir, "sans-personnage.plot");
            AchievementFixture.Save(AchievementFixture.BuildWithoutCharacters(), pathWithout);
            var pathEmpty = Path.Combine(dir, "vide.plot");
            AchievementFixture.Save(Project.CreateNew(), pathEmpty);

            AppSettings.Load();
            AppSettings.Achievements.Clear();
            AppSettings.BlankSince.Clear();
            AppSettings.PermanentlyDeleted = 0;
            AppSettings.WordsAtMaxZoom = 0;
            AppSettings.WordsInCalm = 0;
            AppSettings.AccentColor = null;
            AppSettings.Zoom = 100;
            AppSettings.SpellEnabled = AppSettings.GrammarEnabled = true;
            AppSettings.TypographyEnabled = AppSettings.StyleEnabled = false; // « Sur-stimulation » plus tard
            AppSettings.DarkTheme = false;
            Chrome.Toggle(false);
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Apply(application);
            var window = new MainWindow
            {
                WindowState = WindowState.Normal,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2600,
                Top = 0,
                Width = 1280,
                Height = 800,
                ShowInTaskbar = false
            };
            try
            {
                window.Show();
                DoEvents();

                // — Zéro doublon dans la liste.
                var ids = new HashSet<string>();
                var duplicates = 0;
                foreach (var achievement in Achievements.All) if (!ids.Add(achievement.Id)) duplicates++;
                Check(duplicates == 0, "la liste des succès n'a aucun doublon (" + Achievements.All.Length + " succès)");

                // ============================================ 1. Le projet « tout succès »
                window.OpenFile(path);
                DoEvents();
                var project = (Project)GetField(window, "_project");
                var big = AchievementFixture.Find(project, AchievementFixture.BigTextTitle);
                // Le préchauffage compte les pages de chaque écrit, tranche
                // par tranche : on pompe jusqu'à ce que le pavé soit compté.
                var cache = (Dictionary<string, int>)GetField(window, "_pageCountCache");
                var deadline = DateTime.Now.AddSeconds(180);
                while (!cache.ContainsKey(big.Id) && DateTime.Now < deadline) { DoEvents(); Thread.Sleep(20); }
                int pages;
                Check(cache.TryGetValue(big.Id, out pages) && pages > 100, "le pavé est compté par le préchauffage : " + (cache.ContainsKey(big.Id) ? cache[big.Id] + " pages" : "jamais compté"));

                Invoke(window, "CheckAchievements", null);
                DoEvents();
                var unlocked = AppSettings.Achievements;
                var missing = new List<string>();
                foreach (var id in AchievementFixture.ExpectedOnOpen) if (!unlocked.ContainsKey(id)) missing.Add(id);
                Check(missing.Count == 0, "à l'ouverture, les " + AchievementFixture.ExpectedOnOpen.Length + " succès du projet tombent"
                    + (missing.Count > 0 ? " — manquent : " + string.Join(", ", missing.ToArray()) : ""));
                var extra = new List<string>();
                foreach (var id in unlocked.Keys)
                    if (Array.IndexOf(AchievementFixture.ExpectedOnOpen, id) < 0 && !Achievements.IsTier(id)) extra.Add(id);
                Check(extra.Count == 0, "…et rien d'autre" + (extra.Count > 0 ? " — en trop : " + string.Join(", ", extra.ToArray()) : ""));
                Check(unlocked.ContainsKey("petit-nerd") && unlocked.ContainsKey("poisson-panerd") && !unlocked.ContainsKey("nerdinator"),
                    "Petit nerd et Poisson panerd tombent dans la foulée, pas Nerdinator");
                // Un second passage ne rejoue rien : mêmes succès, mêmes dates.
                var before = new Dictionary<string, string>(unlocked);
                Invoke(window, "CheckAchievements", null);
                DoEvents();
                var same = unlocked.Count == before.Count;
                foreach (var pair in before) if (!unlocked.ContainsKey(pair.Key) || unlocked[pair.Key] != pair.Value) same = false;
                Check(same, "un second passage ne regagne rien");

                // ============================================ 2. Événements atteignables
                // — « Ooh la boulette ! » : le sixième livre (cinq chapitres) à la corbeille.
                var binder = (BinderView)GetField(window, "_binder");
                binder.Delete(AchievementFixture.Find(project, AchievementFixture.SixthBook));
                DoEvents();
                Check(unlocked.ContainsKey(Achievements.Blunder), "un livre de cinq chapitres à la corbeille : Ooh la boulette !");
                Invoke(window, "CheckAchievements", null);
                DoEvents();
                Check(unlocked.ContainsKey("sanderson"), "Sanderson reste acquis une fois le sixième livre parti");

                // — « Yolo » : Tout remplacer sur plus de cent occurrences (200 « nouvelle »).
                var query = SearchQuery.Create("nouvelle", false, false, true, false);
                var targets = ProjectSearch.Collect(project, SearchScope.Project, null, SearchKind.All, false);
                var result = ProjectSearch.Run(targets, query, int.MaxValue, TimeSpan.FromSeconds(30), CancellationToken.None);
                var plan = ReplacePlan.Build(project, result.Hits, query, "conte");
                Check(plan.Edits.Count > 100, "la recherche « nouvelle » trouve plus de cent occurrences (" + plan.Edits.Count + ")");
                Invoke(window, "RunReplace", new object[] { plan, query.Pattern });
                DoEvents();
                Check(unlocked.ContainsKey(Achievements.Yolo), "Tout remplacer sur plus de cent occurrences : Yolo");

                // — « Nettoyage en profondeur » : un écrit vu à plus de cent
                //   fautes puis revu à zéro (le candidat est posé à la main, le
                //   compte réel de l'écrit ouvert vaut zéro : texte vide).
                var blank = AchievementFixture.Find(project, AchievementFixture.BlankTextTitle);
                Invoke(window, "OnBinderSelection", new object[] { blank });
                DoEvents();
                var candidates = (HashSet<string>)GetField(window, "_deepCleanCandidates");
                candidates.Add(blank.Id);
                Invoke(window, "TrackDeepClean", null);
                DoEvents();
                Check(unlocked.ContainsKey(Achievements.DeepClean), "un écrit à plus de cent fautes revu à zéro : Nettoyage en profondeur");

                // ============================================ 3. Compteurs globaux
                AppSettings.Zoom = 300;
                Invoke(window, "AddJournalWords", new object[] { 150 });
                AppSettings.Zoom = 100;
                SetField(window, "_calmMode", true);
                Invoke(window, "AddJournalWords", new object[] { 5000 });
                SetField(window, "_calmMode", false);
                AppSettings.AccentColor = "#AA0000";
                var changed = 0;
                foreach (var action in AppSettings.Actions)
                {
                    if (changed >= 10) break;
                    AppSettings.Shortcuts[action.Id] = "Ctrl+Alt+Shift+F" + (changed + 1);
                    changed++;
                }
                AppSettings.SpellEnabled = AppSettings.GrammarEnabled = AppSettings.TypographyEnabled = AppSettings.StyleEnabled = true;
                AppSettings.PermanentlyDeleted = 20;
                Invoke(window, "CheckAchievements", null);
                DoEvents();
                Check(AppSettings.WordsAtMaxZoom == 150 && unlocked.ContainsKey("besoin-de-lunettes"), "cent cinquante mots à 300 % : Besoin de lunettes ? (compteur : " + AppSettings.WordsAtMaxZoom + ")");
                Check(AppSettings.WordsInCalm == 5000 && unlocked.ContainsKey("inner-peace"), "cinq mille mots en mode calme : Inner peace");
                Check(unlocked.ContainsKey("pimp-my-write"), "un accent personnalisé : Pimp my write");
                Check(unlocked.ContainsKey("picky-eater"), "dix raccourcis réattribués : Picky eater");
                Check(unlocked.ContainsKey("sur-stimulation"), "les quatre soulignages : Sur-stimulation");
                Check(unlocked.ContainsKey("terre-brulee") && unlocked.ContainsKey("masochiste"), "vingt suppressions définitives : Terre brûlée et Masochiste");

                // ============================================ 4. Le temps, par ses champs
                SetField(window, "_appStart", DateTime.Now.AddHours(-9));
                SetField(window, "_dirty", true);
                SetField(window, "_dirtySince", (DateTime?)DateTime.Now.AddHours(-3));
                SetField(window, "_lastSavedBytes", 1024L * 1024 * 1024);
                AppSettings.UsageStreak = 31;
                AppSettings.BlankSince[blank.Id] = DateTime.Now.AddDays(-8).ToString("yyyy-MM-dd");
                Invoke(window, "CheckAchievements", null);
                DoEvents();
                Check(unlocked.ContainsKey("pensez-a-vous-etirer"), "neuf heures d'ouverture : Pensez à vous étirer");
                Check(unlocked.ContainsKey("vivre-dangereusement"), "trois heures sans sauvegarder : Vivre dangereusement");
                Check(unlocked.ContainsKey("damn-boi"), "un .plot d'un gigaoctet : Damn boi");
                Check(unlocked.ContainsKey("autre-addiction"), "trente et un jours de suite : Une autre forme d'addiction");
                Check(unlocked.ContainsKey("page-blanche"), "un écrit vierge depuis huit jours : Page blanche");
                SetField(window, "_dirty", false);

                // — « Le début d'une belle aventure » : le premier enregistrement.
                Invoke(window, "DoSave", null);
                DoEvents();
                Check(unlocked.ContainsKey(Achievements.FirstProject), "le projet enregistré : Le début d'une belle aventure");

                // ============================================ 5. Sans personnage, puis vide
                window.OpenFile(pathWithout);
                DoEvents();
                Invoke(window, "CheckAchievements", null);
                DoEvents();
                Check(unlocked.ContainsKey("une-vie-a-peindre"), "onze fiches sans personnage : Une vie à peindre");

                window.OpenFile(pathEmpty);
                DoEvents();
                var emptyProject = (Project)GetField(window, "_project");
                binder.SelectItem(emptyProject.Category(Project.KeySheets).Id);
                DoEvents();
                var library = (SheetLibraryView)GetField(window, "_sheetLibrary");
                var searchBox = (TextBox)GetField(library, "_searchBox");
                searchBox.Text = "Keira";
                DoEvents();
                Check(unlocked.ContainsKey(Achievements.Cretin), "chercher une fiche sans en avoir : Crétin des alpes");

                // ============================================ 6. Le bilan
                Invoke(window, "CheckAchievements", null);
                DoEvents();
                var count = Achievements.CountKnown(unlocked.Keys);
                Check(unlocked.ContainsKey("nerdinator"), "cinquante succès : Nerdinator (" + count + " obtenus)");
                Check(!unlocked.ContainsKey(Achievements.Emperor), "pas d'Empereur tant qu'il manque À propos, Imprimer, PDF, EPUB…");
                var remaining = new List<string>();
                foreach (var achievement in Achievements.All)
                    if (!unlocked.ContainsKey(achievement.Id) && achievement.ModuleId == null) remaining.Add(achievement.Id);
                Console.WriteLine("  INFO   hors sonde : " + string.Join(", ", remaining.ToArray()));
                var expectedRemaining = new[] { Achievements.GenZ, Achievements.Completionist, Achievements.Minimalist, Achievements.About, Achievements.OldSchool, "deux-heures-max", Achievements.Emperor };
                var onlyExpected = remaining.Count == expectedRemaining.Length;
                foreach (var id in expectedRemaining) if (!remaining.Contains(id)) onlyExpected = false;
                Check(onlyExpected, "il ne reste que les sept succès hors de portée de la sonde");
            }
            finally
            {
                SetField(window, "_dirty", false);
                window.Close();
                DoEvents();
            }
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            field.SetValue(target, value);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            method.Invoke(target, args);
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
}

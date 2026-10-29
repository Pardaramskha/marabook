using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE TUTORIEL (1.1.0, 29/10, Rémi) — le pilote : ouvre le projet
    /// d'exemple (assets/tutoriel/PROJET-TEST.plot copié dans un dossier
    /// temporaire, jamais dans les récents), lit le script
    /// (assets/tutoriel/Tutoriel.md), pose le voile et la fenêtre, et
    /// exécute à chaque étape les actions de sa consigne : naviguer dans la
    /// Pile, choisir un onglet, ouvrir un panneau du rail, enclencher un
    /// mode, mettre un contrôle en valeur. Rien ne se débloque pendant le
    /// tutoriel ; « Tour du proprio » tombe au retour à l'accueil, quand on
    /// l'a terminé (pas quand on le ferme).</summary>
    public partial class MainWindow
    {
        private TutorialScript _tutorialScript;
        private List<TutorialStep> _tutorialSteps; // à plat : l'accueil, puis chaque étape de chaque chapitre jouable
        private int _tutorialIndex = -1;
        private TutorialVeil _tutorialVeil;
        private TutorialWindow _tutorialWindow;
        private TemplatesDialog _tutorialTemplates; // l'éditeur de modèles ouvert par le tuto, à refermer
        private string _tutorialPath;
        private readonly List<string> _tutorialGaps = new List<string>(); // les consignes incomprises (pour Rémi)

        public bool TutorialActive { get { return _tutorialSteps != null; } }

        /// <summary>Le fichier d'une ressource du tutoriel : à côté de l'exécutable
        /// (publication), sinon à la racine du dépôt (Debug, sondes).</summary>
        internal static string TutorialAsset(string name)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "assets", "tutoriel", name),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "tutoriel", name)
            };
            foreach (var candidate in candidates) if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            return null;
        }

        /// <summary>Démarre le tutoriel depuis l'accueil : vrai si le projet
        /// d'exemple et le script sont là et que tout est en place.</summary>
        public bool StartTutorial()
        {
            if (TutorialActive) return true;
            var plot = TutorialAsset("PROJET-TEST.plot");
            var script = TutorialAsset("Tutoriel.md");
            if (plot == null || script == null)
            {
                MessageDialog.Show(this, "Le tutoriel n'est pas installé (assets/tutoriel absent).", AppName, MessageButtons.OK, MessageIcon.Warning);
                return false;
            }
            TutorialScript parsed;
            try { parsed = TutorialParser.Parse(File.ReadAllText(script, Encoding.UTF8)); }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Le script du tutoriel est illisible :\n" + error.Message, AppName, MessageButtons.OK, MessageIcon.Error);
                return false;
            }
            // Le projet d'exemple, copié au chaud : les enregistrements
            // automatiques y vont, rien ne touche la ressource.
            string path;
            try
            {
                var folder = Path.Combine(Path.GetTempPath(), "Marabook", "tutoriel");
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, "Tutoriel.plot");
                foreach (var stale in new[] { path, path + ".bak", path + ".tmp", path + ".lock" })
                    if (File.Exists(stale)) { try { File.Delete(stale); } catch (IOException) { } }
                File.Copy(plot, path, true);
                var warnings = new List<string>();
                var project = PlotFile.Load(path, warnings);
                project.Name = "Tutoriel";
                LoadProject(project, path);
                if (_welcome != null) _welcome.Release();
            }
            catch (Exception error)
            {
                MessageDialog.Show(this, "Impossible d'ouvrir le projet du tutoriel :\n" + error.Message, AppName, MessageButtons.OK, MessageIcon.Error);
                return false;
            }
            _tutorialPath = path;
            _tutorialScript = parsed;
            _tutorialSteps = new List<TutorialStep>();
            _tutorialGaps.Clear();
            // L'accueil du script, comme une étape sans consigne.
            var welcome = new TutorialChapter { Number = 0, Title = "Bienvenue dans Marabook" };
            var intro = new TutorialStep { Chapter = welcome, Text = string.Join("\n\n", parsed.Intro.ToArray()), Cue = new TutorialCue() };
            welcome.Steps.Add(intro);
            _tutorialSteps.Add(intro);
            foreach (var chapter in parsed.Playable)
                foreach (var step in chapter.Steps)
                {
                    _tutorialSteps.Add(step);
                    if (step.Cue != null && step.Cue.Unresolved != null) _tutorialGaps.Add(chapter.Number + ". " + chapter.Title + " — (" + step.Cue.Unresolved + ")");
                }
            if (_tutorialVeil == null) _tutorialVeil = new TutorialVeil(_shellGrid);
            if (!_shellGrid.Children.Contains(_tutorialVeil)) _shellGrid.Children.Add(_tutorialVeil);
            _tutorialVeil.IsVisible = true;
            _tutorialWindow = new TutorialWindow(this);
            _tutorialWindow.NextRequested += TutorialNext;
            _tutorialWindow.PreviousRequested += TutorialPrevious;
            _tutorialWindow.NextChapterRequested += delegate { TutorialJumpChapter(+1); };
            _tutorialWindow.PreviousChapterRequested += delegate { TutorialJumpChapter(-1); };
            _tutorialWindow.CloseRequested += delegate { EndTutorial(false); };
            _tutorialWindow.Closed += delegate { if (TutorialActive) EndTutorial(false); }; // fermée autrement (système) : fin sans succès
            _tutorialWindow.Show(this);
            ShowTutorialStep(0);
            return true;
        }

        private void TutorialNext()
        {
            if (!TutorialActive) return;
            if (_tutorialIndex >= _tutorialSteps.Count - 1) { EndTutorial(true); return; }
            ShowTutorialStep(_tutorialIndex + 1);
        }

        private void TutorialPrevious()
        {
            if (!TutorialActive || _tutorialIndex <= 0) return;
            ShowTutorialStep(_tutorialIndex - 1);
        }

        /// <summary>Les flèches de l'en-tête : la première étape du chapitre
        /// suivant ou précédent, les étapes internes sautées.</summary>
        private void TutorialJumpChapter(int direction)
        {
            if (!TutorialActive) return;
            var current = _tutorialSteps[_tutorialIndex].Chapter;
            if (direction > 0)
            {
                for (var i = _tutorialIndex + 1; i < _tutorialSteps.Count; i++)
                    if (!ReferenceEquals(_tutorialSteps[i].Chapter, current)) { ShowTutorialStep(i); return; }
                EndTutorial(true);
            }
            else
            {
                var firstOfCurrent = _tutorialSteps.IndexOf(current.Steps[0]);
                var target = firstOfCurrent;
                if (firstOfCurrent > 0)
                {
                    var previous = _tutorialSteps[firstOfCurrent - 1].Chapter;
                    target = _tutorialSteps.IndexOf(previous.Steps[0]);
                }
                ShowTutorialStep(Math.Max(0, target));
            }
        }

        private void ShowTutorialStep(int index)
        {
            if (!TutorialActive || index < 0 || index >= _tutorialSteps.Count) return;
            _tutorialIndex = index;
            var step = _tutorialSteps[index];
            var chapter = step.Chapter;
            var inChapter = chapter.Steps.IndexOf(step);
            var playable = _tutorialScript.Playable;
            var chapterIndex = playable.IndexOf(chapter);
            try
            {
                TutorialResetModes();
                var targets = new List<Func<Control>>();
                foreach (var action in step.Cue.Actions) TutorialApply(action, targets);
                _tutorialVeil.SetTargets(targets);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("[tutoriel] étape " + index + " : " + error);
            }
            var title = chapter.Number > 0 ? chapter.Number + ". " + chapter.Title : chapter.Title;
            var progress = chapter.Number > 0
                ? "Étape " + (inChapter + 1) + " sur " + chapter.Steps.Count + " — chapitre " + (chapterIndex + 1) + " sur " + playable.Count
                : "Le tour du propriétaire, en " + playable.Count + " chapitres";
            _tutorialWindow.Present(title, progress, step.Text, index == 0, index == _tutorialSteps.Count - 1,
                chapter.Number == 0, chapterIndex >= playable.Count - 1);
            Ui.Post(DispatcherPriority.Background, new Action(PlaceTutorialWindow));
        }

        /// <summary>Avant chaque étape : les modes enclenchés par une étape
        /// d'avant retombent (mode wiki, vue miroir, généalogie, graphique du
        /// plan, éditeur de modèles) — la consigne de l'étape les remet si elle le veut.</summary>
        private void TutorialResetModes()
        {
            if (_tutorialTemplates != null) { try { _tutorialTemplates.Close(); } catch { } _tutorialTemplates = null; }
            if (_sheetView != null && _sheetView.IsVisible)
            {
                _sheetView.SetGenealogyForTutorial(false);
                _sheetView.SetWikiForTutorial(false);
                _sheetView.SetMirrorForTutorial(false);
            }
            if (_planView != null) _planView.SetChartForTutorial(false);
            if (_journalView != null) _journalView.SelectTabForTutorial(0);
        }

        private BinderItem TutorialItem(ItemKind kind, string titlePrefix)
        {
            BinderItem first = null;
            foreach (var item in _project.AllItems())
            {
                if (item.Kind != kind || item.RootCategory().CategoryKey == Project.KeyTrash) continue;
                if (first == null) first = item;
                if (titlePrefix != null && (item.Title ?? "").StartsWith(titlePrefix, StringComparison.CurrentCultureIgnoreCase)) return item;
            }
            return first;
        }

        private void TutorialSelect(BinderItem item)
        {
            if (item != null) _binder.SelectItem(item.Id, true);
        }

        private BinderItem TutorialBook() { return TutorialItem(ItemKind.Book, null); }

        private BinderItem TutorialChapterText()
        {
            var book = TutorialBook();
            if (book != null)
            {
                var texts = new List<BinderItem>();
                BookFlow.Texts(book, texts);
                if (texts.Count > 0) return texts[0];
            }
            return TutorialItem(ItemKind.Text, "Chapitre");
        }

        private BinderItem TutorialCharacterSheet()
        {
            var character = _project.CharacterTemplate();
            foreach (var item in _project.AllItems())
                if (item.Kind == ItemKind.Sheet && character != null && item.TemplateId == character.Id && item.RootCategory().CategoryKey != Project.KeyTrash) return item;
            return TutorialItem(ItemKind.Sheet, null);
        }

        /// <summary>Une action nommée (TutorialCues) → le geste à l'écran.
        /// Les cibles à mettre en valeur s'ajoutent à la liste.</summary>
        private void TutorialApply(string action, List<Func<Control>> targets)
        {
            var colon = action.IndexOf(':');
            var verb = colon < 0 ? action : action.Substring(0, colon);
            var arg = colon < 0 ? "" : action.Substring(colon + 1);
            switch (verb)
            {
                case "nav":
                    switch (arg)
                    {
                        case "writings": TutorialSelect(_project.Category(Project.KeyWritings)); break;
                        case "book": TutorialSelect(TutorialBook()); break;
                        case "chapter": TutorialSelect(TutorialChapterText()); break;
                        case "research": TutorialSelect(_project.Category(Project.KeyResearch)); break;
                        case "media": TutorialSelect(TutorialItem(ItemKind.Media, "Le pigeon")); break;
                        case "sheets": TutorialSelect(_project.Category(Project.KeySheets)); break;
                        case "sheet": TutorialSelect(TutorialCharacterSheet()); break;
                        case "dictionary": TutorialSelect(_project.Category(Project.KeyDictionary)); break;
                        case "plans": TutorialSelect(_project.Category(Project.KeyPlans)); break;
                        case "plan": TutorialSelect(TutorialItem(ItemKind.Plan, null)); break;
                        case "journal": ShowJournal(); break;
                    }
                    break;
                case "book-tab":
                    if (!_bookView.IsVisible) TutorialSelect(TutorialBook());
                    _bookView.SelectTabForTutorial(arg);
                    break;
                case "ribbon-tab":
                    if (!_editor.IsVisible) TutorialSelect(TutorialChapterText());
                    _editor.SelectRibbonTabForProbe(arg);
                    break;
                case "sheet-tab":
                    if (!_sheetView.IsVisible) TutorialSelect(TutorialCharacterSheet());
                    _sheetView.SelectTabForTutorial(arg);
                    break;
                case "sheet":
                    if (!_sheetView.IsVisible) TutorialSelect(TutorialCharacterSheet());
                    if (arg == "mirror") { _sheetView.SelectTabForTutorial("Texte libre"); _sheetView.SetMirrorForTutorial(true); }
                    else if (arg == "wiki") _sheetView.SetWikiForTutorial(true);
                    else if (arg == "genealogy") _sheetView.SetGenealogyForTutorial(true);
                    break;
                case "rail":
                    if (!_editor.IsVisible) TutorialSelect(TutorialChapterText());
                    SetRightPanel(arg == "correction" ? RightPanel.Correction : arg == "versions" ? RightPanel.Versions : RightPanel.Inspector);
                    break;
                case "templates":
                    if (_tutorialTemplates == null)
                    {
                        _tutorialTemplates = TemplatesDialog.OpenForTutorial(this, _project.Templates, _project);
                        _tutorialTemplates.SelectTemplateForTutorial("Personnage");
                        _tutorialTemplates.Closed += delegate { _tutorialTemplates = null; };
                    }
                    break;
                case "templates-tab":
                    if (_tutorialTemplates == null) TutorialApply("templates:open", targets);
                    if (_tutorialTemplates != null) _tutorialTemplates.SelectTabForTutorial(arg);
                    break;
                case "plan":
                    if (!_planView.IsVisible) TutorialSelect(TutorialItem(ItemKind.Plan, null));
                    if (arg == "chart") _planView.SetChartForTutorial(true);
                    break;
                case "journal":
                    if (!_journalView.IsVisible) ShowJournal();
                    if (arg == "achievements") _journalView.SelectTabForTutorial(1);
                    break;
                case "highlight":
                    switch (arg)
                    {
                        case "pile": targets.Add(delegate { return _binder; }); break;
                        case "rail": targets.Add(delegate { return _rail; }); break;
                        case "tabs": targets.Add(delegate { return _tabStrip; }); break;
                        case "ribbon": targets.Add(delegate { return _editor.RibbonBarForTutorial; }); break;
                        case "book-actions":
                            if (!_bookView.IsVisible) TutorialSelect(TutorialBook());
                            _bookView.SelectTabForTutorial("Textes");
                            targets.Add(delegate { return _bookView.TextsActionsForTutorial; });
                            break;
                        case "relations":
                            if (!_sheetView.IsVisible) TutorialSelect(TutorialCharacterSheet());
                            _sheetView.SelectTabForTutorial("Général");
                            targets.Add(delegate { return _sheetView.RelationsPaperForTutorial; });
                            break;
                        case "relations-editor":
                            if (!_sheetView.IsVisible) TutorialSelect(TutorialCharacterSheet());
                            _sheetView.SelectTabForTutorial("Général");
                            targets.Add(delegate { return _sheetView.RelationsEditorButtonForTutorial; });
                            break;
                        case "templates-button": targets.Add(delegate { return _sheetLibrary.TemplatesButtonForTutorial; }); break;
                        case "dictionary-new": targets.Add(delegate { return _dictionaryView.NewEntryButtonForTutorial; }); break;
                        case "dictionary-unknown": targets.Add(delegate { return _dictionaryView.UnknownButtonForTutorial; }); break;
                        case "plan-chart": targets.Add(delegate { return _planView.ChartToggleForTutorial; }); break;
                    }
                    break;
            }
        }

        /// <summary>La fenêtre du tuto se pose là où elle ne couvre aucun trou
        /// du voile : à droite, sinon à gauche, en bas, en haut — et reste où
        /// l'utilisateur l'a mise tant qu'elle ne gêne pas.</summary>
        private void PlaceTutorialWindow()
        {
            if (!TutorialActive || _tutorialWindow == null || _shellGrid == null) return;
            var scale = DesktopScaling;
            var origin = _shellGrid.PointToScreen(new Point(0, 0));
            var shell = new PixelRect(origin, PixelSize.FromSize(_shellGrid.Bounds.Size, scale));
            var size = PixelSize.FromSize(new Size(_tutorialWindow.Bounds.Width > 0 ? _tutorialWindow.Bounds.Width : TutorialWindow.FixedWidth + 24,
                _tutorialWindow.Bounds.Height > 0 ? _tutorialWindow.Bounds.Height : 320), scale);
            var holes = new List<PixelRect>();
            var pad = (int)(12 * scale);
            foreach (var hole in _tutorialVeil.Holes)
            {
                var at = _shellGrid.PointToScreen(hole.TopLeft);
                var holeSize = PixelSize.FromSize(hole.Size, scale);
                holes.Add(new PixelRect(at.X - pad, at.Y - pad, holeSize.Width + 2 * pad, holeSize.Height + 2 * pad));
            }
            Func<PixelRect, bool> clear = delegate(PixelRect candidate)
            {
                foreach (var hole in holes) if (hole.Intersects(candidate)) return false;
                return true;
            };
            if (_tutorialWindow.UserMoved)
            {
                var current = new PixelRect(_tutorialWindow.Position, size);
                if (clear(current)) return;
            }
            var margin = (int)(24 * scale);
            var centerY = shell.Y + (shell.Height - size.Height) / 2;
            var centerX = shell.X + (shell.Width - size.Width) / 2;
            var candidates = new[]
            {
                new PixelRect(new PixelPoint(shell.Right - size.Width - margin, centerY), size),
                new PixelRect(new PixelPoint(shell.X + margin, centerY), size),
                new PixelRect(new PixelPoint(centerX, shell.Bottom - size.Height - margin), size),
                new PixelRect(new PixelPoint(centerX, shell.Y + margin), size),
                new PixelRect(new PixelPoint(centerX, centerY), size)
            };
            foreach (var candidate in candidates)
                if (clear(candidate)) { _tutorialWindow.Position = candidate.Position; return; }
            _tutorialWindow.Position = candidates[0].Position;
        }

        /// <summary>Fin du tutoriel : tout se range, l'accueil revient ; terminé
        /// (pas fermé), « Tour du proprio » se débloque — le seul succès du tuto.</summary>
        private void EndTutorial(bool completed)
        {
            if (!TutorialActive) return;
            TutorialResetModes();
            var window = _tutorialWindow;
            _tutorialWindow = null;
            _tutorialSteps = null;
            _tutorialScript = null;
            _tutorialIndex = -1;
            if (_tutorialVeil != null) { _tutorialVeil.IsVisible = false; _tutorialVeil.SetTargets(null); }
            if (window != null) { try { window.Close(); } catch { } }
            _dirty = false; // le projet d'exemple ne vaut pas un « Enregistrer ? »
            ShowWelcome();
            if (completed) UnlockAchievement(Achievements.Tutorial);
        }

        // — Sonde (29/10).
        internal TutorialWindow TutorialWindowForProbe { get { return _tutorialWindow; } }
        internal TutorialVeil TutorialVeilForProbe { get { return _tutorialVeil; } }
        internal int TutorialStepCountForProbe { get { return _tutorialSteps == null ? 0 : _tutorialSteps.Count; } }
        internal int TutorialIndexForProbe { get { return _tutorialIndex; } }
        internal string TutorialCurrentActionsForProbe { get { return TutorialActive ? string.Join(" ", _tutorialSteps[_tutorialIndex].Cue.Actions.ToArray()) : ""; } }
        internal List<string> TutorialGapsForProbe { get { return _tutorialGaps; } }
        internal string TutorialPathForProbe { get { return _tutorialPath; } }
        internal bool WelcomeShownForProbe { get { return _welcome != null; } }
        internal bool TemplatesOpenForProbe { get { return _tutorialTemplates != null && _tutorialTemplates.IsVisible; } }
        internal bool PlanChartOnForProbe { get { return _planView != null && _planView.IsVisible && _planView.ChartToggleForTutorial != null && _planView.ChartToggleForTutorial.IsChecked == true; } }
        internal bool JournalAchievementsShownForProbe { get { return _journalView != null && _journalView.IsVisible && _journalView.SelectedTabForProbe == 1; } }
        internal void CloseWelcomeForProbe() { if (_welcome != null) _welcome.Release(); }
    }
}

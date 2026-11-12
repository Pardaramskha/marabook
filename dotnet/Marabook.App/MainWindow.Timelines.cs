using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    // LES FRISES dans la fenêtre (1.2.0, 12/11/2026) : la page de la racine
    // « Frises », l'écran d'une frise, son panneau dans le Général du rail,
    // les systèmes temporels, et le lien vers la temporalité (Ctrl+!).
    public partial class MainWindow
    {
        private TimelineView _timelineView;       // une frise
        private TimelineHubView _timelineHub;     // la racine « Frises »
        private ContentControl _timelineSection;  // le panneau de la frise dans le Général

        private void BuildTimelineViews(Panel center)
        {
            _timelineView = new TimelineView { IsVisible = false };
            _timelineView.Edited += delegate
            {
                MarkDirty();
                UpdateStats();
                if (_sheetLibrary.IsVisible) _sheetLibrary.RefreshEpoch();
            };
            _timelineView.NavigateRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _timelineView.RenameRequested += delegate
            {
                if (_current == null || _current.Kind != ItemKind.Timeline) return;
                _binder.RenameQuiet(_current);
                _timelineView.Refresh();
                UpdateInspector();
            };
            _timelineView.BackRequested += delegate
            {
                var root = _project == null ? null : _project.Category(Project.KeyTimelines);
                if (root != null) _binder.SelectItem(root.Id);
            };
            _timelineView.SystemEditRequested += delegate(TimeSystem system) { EditTimeSystem(system); };
            _timelineView.InspectorChanged += delegate { UpdateStats(); };
            center.Children.Add(_timelineView);

            _timelineHub = new TimelineHubView { IsVisible = false };
            _timelineHub.OpenRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id); };
            _timelineHub.NewTimelineRequested += delegate { _binder.NewTimeline(null); };
            _timelineHub.NewSystemRequested += delegate { EditTimeSystem(null); };
            _timelineHub.EditSystemRequested += delegate(TimeSystem system) { EditTimeSystem(system); };
            _timelineHub.RemoveSystemRequested += delegate(TimeSystem system) { RemoveTimeSystem(system); };
            center.Children.Add(_timelineHub);
        }

        internal TimelineView TimelineForProbe { get { return _timelineView; } }
        internal TimelineHubView TimelineHubForProbe { get { return _timelineHub; } }
        internal void FollowLinkForProbe(string target) { NavigateToTitle(target); }
        internal void ToggleEpochForProbe(bool configure) { ToggleEpoch(configure); }
        internal string InspectorKindForProbe { get { return _inspKind == null ? "" : _inspKind.Text ?? ""; } }

        /// <summary>Le panneau de la frise ouverte dans le Général du rail : le
        /// nombre d'événements, la durée totale de la temporalité, celle de
        /// l'ère — et la brique choisie.</summary>
        private void RefreshTimelineSection(BinderItem item)
        {
            if (_timelineSection == null || _timelineView == null) return;
            var shown = item != null && item.Kind == ItemKind.Timeline && _timelineView.IsVisible && _timelineView.ShowsItem(item);
            var content = shown ? _timelineView.Inspector : null;
            if (!ReferenceEquals(_timelineSection.Content, content)) _timelineSection.Content = content;
            _timelineSection.IsVisible = shown;
        }

        /// <summary>« Temporalité par segments » : crée (system null) ou modifie
        /// un système temporel. Les briques gardent leur place sur l'axe ; les
        /// liens de date, eux, sont du texte — on prévient s'ils existent.</summary>
        private async void EditTimeSystem(TimeSystem system)
        {
            if (_project == null) return;
            var creating = system == null || !_project.TimeSystems.Contains(system);
            var edited = await TimeSystemDialog.Edit(this, _project, system ?? TimeSystemDialog.Template(_project), creating);
            if (edited == null) return;
            if (creating) _project.TimeSystems.Add(edited);
            else _project.TimeSystems[_project.TimeSystems.IndexOf(system)] = edited;
            MarkDirty();
            RefreshTimelineViews();
        }

        private async void RemoveTimeSystem(TimeSystem system)
        {
            if (_project == null || system == null || Timelines.UsesOf(_project, system) > 0) return;
            var answer = await MessageDialog.Show(this, "Retirer le système « " + system.Name + " » du projet ?", "Systèmes temporels",
                MessageButtons.YesNo, MessageIcon.Question);
            if (answer != MessageResult.Yes) return;
            _project.TimeSystems.Remove(system);
            MarkDirty();
            RefreshTimelineViews();
        }

        /// <summary>Tout ce qui montre une frise ou une date se relit.</summary>
        private void RefreshTimelineViews()
        {
            if (_timelineView != null && _timelineView.IsVisible) _timelineView.Refresh();
            if (_timelineHub != null && _timelineHub.IsVisible) _timelineHub.Refresh();
            if (_sheetLibrary != null && _sheetLibrary.IsVisible) _sheetLibrary.RefreshEpoch();
            if (_sheetView != null && _sheetView.IsVisible) _sheetView.RefreshEpoch();
            UpdateInspector();
            UpdateStats();
        }

        /// <summary>Un lien de date cliqué : sa frise s'ouvre, à cette date.</summary>
        private bool FollowTimeLink(string target)
        {
            if (!TimeLinks.IsTimeLink(target)) return false;
            TimeSystem system;
            TimePoint point;
            var timeline = TimeLinks.Resolve(_project, target, out system, out point);
            if (timeline == null)
            {
                string title, notation;
                TimeLinks.Split(target, out title, out notation);
                MessageDialog.Show(this, "La frise « " + (title ?? "?") + " » de ce lien de date n'existe plus (renommée ou supprimée).",
                    "Lien de date", MessageButtons.OK, MessageIcon.Information);
                return true;
            }
            _binder.SelectItem(timeline.Id);
            if (point != null)
                Ui.Post(DispatcherPriority.Background, delegate
                {
                    if (_timelineView.IsVisible && _timelineView.ShowsItem(timeline)) _timelineView.GoTo(point);
                });
            return true;
        }

        /// <summary>Format › Lien vers la temporalité (Ctrl+!) : la fenêtre du
        /// lien de date, dans un écrit comme dans tout champ de fiche qui
        /// accepte un lien. Une notation sélectionnée (« E5.902 ») est lue
        /// d'office ; un lien de date sous le caret est proposé tel quel.</summary>
        private async void InsertTimeLinkInActive()
        {
            if (_project == null || (_editor.IsVisible == false && _sheetView.IsVisible == false)) return;
            if (Timelines.All(_project).Count == 0)
            {
                await MessageDialog.Show(this, "Un lien de date vise une frise : créez-en d'abord une (racine « Frises » de la Pile).",
                    "Lien vers la temporalité", MessageButtons.OK, MessageIcon.Information);
                return;
            }
            var sheet = _sheetView.IsVisible;
            if (sheet && !_sheetView.HasLinkTarget()) return;
            var existing = sheet ? _sheetView.CaptureLinkContext() : _editor.LinkAtCaret();
            if (!sheet) _editor.CaptureLinkContext();
            BinderItem timeline = null;
            string notation = "", text = "";
            var editing = false;
            if (existing != null && TimeLinks.IsTimeLink(existing.Target))
            {
                TimeSystem system;
                TimePoint point;
                string title;
                timeline = TimeLinks.Resolve(_project, existing.Target, out system, out point);
                TimeLinks.Split(existing.Target, out title, out notation);
                text = existing.Text == notation ? "" : existing.Text;
                editing = true;
            }
            else
            {
                text = (sheet ? _sheetView.SelectedBodyText() : _editor.SelectedPlainText()) ?? "";
                if (text.IndexOf('\n') >= 0 || text.IndexOf("]]", StringComparison.Ordinal) >= 0) text = "";
                TimePoint point;
                var last = _project.FindById(_lastTimeLinkTimeline);
                timeline = TimeLinks.MatchNotation(_project, text, last, out point);
                if (timeline != null) { notation = text.Trim(); text = ""; }
                else timeline = last;
            }
            var choice = await TimeLinkDialog.Ask(this, _project, timeline, notation, text, editing);
            if (choice == null) { _sheetView.ForgetLinkContext(); return; }
            var chosen = Timelines.FindByTitle(_project, choice.Timeline);
            if (chosen != null) _lastTimeLinkTimeline = chosen.Id;
            var target = TimeLinks.Target(choice.Timeline, choice.Notation);
            if (sheet) _sheetView.ApplyWikiLink(target, choice.Text);
            else _editor.ApplyWikiLink(target, choice.Text);
            Ui.Post(DispatcherPriority.Background, new Action(delegate
            {
                UpdateLinksPanel();
                if (_sheetView.IsVisible) _sheetView.RefreshEpoch(); // une date de naissance vient peut-être d'être liée
            }));
        }

        private string _lastTimeLinkTimeline; // la frise du dernier lien posé : proposée d'abord au suivant

        /// <summary>Sonde : le dialogue du lien de date comme par le raccourci.</summary>
        public void InsertTimeLinkPublic() { InsertTimeLinkInActive(); }

        /// <summary>Le bouton « Époque » de la bibliothèque : allumer (en réglant
        /// la frise et la date si rien n'est réglé), éteindre, ou régler.</summary>
        private async void ToggleEpoch(bool configure)
        {
            if (_project == null) return;
            if (_project.Epoch == null) _project.Epoch = new EpochState();
            TimeSystem system;
            BinderItem timeline;
            if (!configure && _project.Epoch.Enabled)
            {
                _project.Epoch.Enabled = false;
            }
            else
            {
                if (Timelines.All(_project).Count == 0)
                {
                    await MessageDialog.Show(this, "L'époque se lit sur une frise : créez-en d'abord une (racine « Frises » de la Pile).",
                        "Époque", MessageButtons.OK, MessageIcon.Information);
                    _sheetLibrary.RefreshEpoch();
                    return;
                }
                _project.Epoch.Enabled = true;
                var readable = Epochs.Current(_project, out system, out timeline) != null;
                if (configure || !readable)
                {
                    if (!readable) _project.Epoch.Enabled = false;
                    var was = _project.Epoch.Enabled;
                    if (!await EpochDialog.Ask(this, _project)) { _project.Epoch.Enabled = was && readable; _sheetLibrary.RefreshEpoch(); return; }
                }
            }
            MarkDirty();
            RefreshTimelineViews();
        }
    }
}

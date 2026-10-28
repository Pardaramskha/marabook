using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marabook.Model;
using AppSettings = Marabook.Settings.AppSettings;

namespace Marabook.App
{
    /// <summary>LES ONGLETS de la zone centrale (1.1.0, 28/10, Rémi) : une
    /// rangée en haut du centre, à la hauteur du champ de recherche de la
    /// Pile. Chaque onglet retient l'élément qu'il montre ; les vues du
    /// centre restent uniques — changer d'onglet, c'est sélectionner son
    /// élément dans la Pile, par le chemin normal (OnBinderSelection), qui
    /// ramène ensuite l'onglet actif sur ce qu'il montre (TabsFollow).
    /// Ctrl+T ouvre l'Accueil dans un onglet neuf, le « + » aussi ; Ctrl+W
    /// ferme l'actif ; Ctrl+PageDown/PageUp et Ctrl+Tab/Ctrl+Maj+Tab
    /// circulent. Plafond et bascule : Préférences › Général › Onglets. La
    /// rangée est reprise avec le projet (.plot v42), sans salir.</summary>
    public partial class MainWindow
    {
        private readonly WorkTabs _tabs = new WorkTabs();
        private TabStrip _tabStrip;

        /// <summary>Les onglets sont en service (réglage) — et visibles hors du mode calme.</summary>
        private bool TabsOn() { return AppSettings.TabsEnabled; }

        private Control BuildTabStrip()
        {
            _tabs.Limit = AppSettings.MaxTabs;
            _tabStrip = new TabStrip { IsVisible = TabsOn() };
            _tabStrip.Resolve = delegate(string id) { return _project == null ? null : _project.FindById(id); };
            _tabStrip.Activated += ActivateTab;
            _tabStrip.CloseRequested += CloseTab;
            _tabStrip.CloseOthersRequested += delegate(int index) { TabsRememberPosition(); _tabs.CloseOthers(index); TabsChanged(); ShowActiveTab(); };
            _tabStrip.CloseRightRequested += delegate(int index) { TabsRememberPosition(); _tabs.CloseRight(index); TabsChanged(); ShowActiveTab(); };
            _tabStrip.NewRequested += OpenNewTab;
            _tabStrip.Bind(_tabs);
            // Ctrl+Tab / Ctrl+Maj+Tab circulent aussi, comme dans tout
            // navigateur — câblés (la touche Tab a déjà ses gestes réglables
            // dans l'éditeur : niveaux de liste), en tunnel pour passer avant
            // l'éditeur ou un champ. Sur macOS c'est la touche Contrôle
            // physique, comme Chrome ; ⌘Tab reste au système.
            AddHandler(KeyDownEvent, new EventHandler<KeyEventArgs>(delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Tab || (e.KeyModifiers & KeyModifiers.Control) == 0 || !TabsOn() || _calmMode) return;
                if ((e.KeyModifiers & KeyModifiers.Alt) != 0) return;
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0) PreviousTab(); else NextTab();
                e.Handled = true;
            }), RoutingStrategies.Tunnel);
            return _tabStrip;
        }

        /// <summary>La rangée suit la hauteur de la barre de recherche de la
        /// Pile (même ligne, même hauteur — Rémi), mesurée à l'écran.</summary>
        private void AlignTabStripWith(Control bar)
        {
            if (bar == null || _tabStrip == null) return;
            Action follow = delegate
            {
                var height = bar.Bounds.Height;
                if (height > 20) _tabStrip.Height = height;
            };
            bar.SizeChanged += delegate { follow(); };
            follow();
        }

        /// <summary>La Pile vient d'ouvrir cet élément : l'onglet actif le
        /// montre désormais (sans onglet, le premier naît ici).</summary>
        private void TabsFollow(BinderItem item)
        {
            if (!TabsOn() || item == null) return;
            _tabs.Navigate(item.Id);
            TabsChanged();
        }

        /// <summary>Le modèle a changé : le projet retient la rangée (sans
        /// salir — relire n'est pas modifier, elle part avec la prochaine
        /// vraie sauvegarde, comme les récents), la rangée se redessine.</summary>
        private void TabsChanged()
        {
            if (_project != null)
            {
                _project.OpenTabs = _tabs.ItemIds();
                _project.ActiveTab = Math.Max(0, _tabs.ActiveIndex);
            }
            if (_tabStrip != null) _tabStrip.Rebuild();
        }

        /// <summary>Avant de quitter l'onglet actif (28/10, Rémi) : s'il montre
        /// un écrit ouvert dans l'éditeur, il retient le caret et le
        /// défilement — pour les retrouver au retour.</summary>
        private void TabsRememberPosition()
        {
            var tab = _tabs.Active;
            if (tab == null || _current == null || tab.ItemId != _current.Id) return;
            if (_current.Kind != ItemKind.Text || !_editor.IsVisible) return;
            var composed = Composed;
            if (composed == null || !composed.HasItem) return;
            int paragraph, offset;
            composed.GetCaret(out paragraph, out offset);
            tab.Remember(paragraph, offset, composed.Offset.Y);
        }

        /// <summary>Montre l'élément de l'onglet actif, par la Pile — s'il
        /// n'est pas déjà celui qui est ouvert — puis lui rend sa position
        /// retenue, une fois la vue posée. Un élément disparu fait tomber
        /// son onglet et l'on recommence.</summary>
        private void ShowActiveTab()
        {
            if (_project == null) return;
            var id = _tabs.ActiveItemId;
            if (id == null) { if (_tabs.Count == 0) OpenHomeTab(); return; }
            var item = _project.FindById(id);
            if (item == null)
            {
                _tabs.Drop(id);
                TabsChanged();
                ShowActiveTab();
                return;
            }
            if (_current == item && IsItemViewVisible(item)) return;
            _binder.SelectItem(item.Id, true);
            var tab = _tabs.Active;
            if (tab == null || !tab.HasPosition || item.Kind != ItemKind.Text) return;
            var paragraph = tab.Paragraph;
            var offset = tab.Offset;
            var scrollY = tab.ScrollY;
            Ui.Post(Avalonia.Threading.DispatcherPriority.Background, new Action(delegate
            {
                var composed = Composed;
                if (_current != item || !_editor.IsVisible || composed == null || !composed.HasItem) return;
                composed.RestoreView(paragraph, offset, scrollY);
            }));
        }

        private void ActivateTab(int index)
        {
            if (index == _tabs.ActiveIndex) return;
            TabsRememberPosition();
            if (!_tabs.Activate(index)) return;
            TabsChanged();
            ShowActiveTab();
        }

        /// <summary>Ferme un onglet ; la rangée ne reste jamais vide — le
        /// dernier fermé laisse un Accueil.</summary>
        private void CloseTab(int index)
        {
            if (!_tabs.Close(index)) return;
            if (_tabs.Count == 0) OpenHomeTab();
            else { TabsChanged(); ShowActiveTab(); }
        }

        private void CloseActiveTab()
        {
            if (!TabsOn() || _tabs.ActiveIndex < 0) return;
            CloseTab(_tabs.ActiveIndex);
        }

        /// <summary>Ctrl+T, le « + » : l'Accueil dans un onglet neuf. Au
        /// plafond, un mot — et rien d'autre.</summary>
        private void OpenNewTab()
        {
            if (!TabsOn() || _project == null) return;
            var home = _project.Category(Project.KeyHome);
            if (home == null) return;
            OpenInNewTab(home);
        }

        private void OpenHomeTab()
        {
            if (_project == null) return;
            var home = _project.Category(Project.KeyHome);
            if (home == null) return;
            _tabs.Open(home.Id);
            TabsChanged();
            ShowActiveTab();
        }

        /// <summary>« Ouvrir dans un nouvel onglet » (menu de la Pile), le
        /// « + », Ctrl+T : un onglet de plus montrant cet élément.</summary>
        private void OpenInNewTab(BinderItem item)
        {
            if (!TabsOn() || item == null || _project == null) return;
            if (!_tabs.CanOpen)
            {
                ShowNotice(NoticeToast.Build("tuile", "Onglets maximum atteint",
                    "La rangée compte déjà " + _tabs.Limit + " onglets. Fermez-en un, ou relevez le plafond dans Préférences › Général › Onglets.",
                    null, null, null, null));
                return;
            }
            TabsRememberPosition();
            _tabs.Open(item.Id);
            TabsChanged();
            ShowActiveTab();
        }

        private void NextTab()
        {
            if (!TabsOn() || _tabs.Count < 2) return;
            TabsRememberPosition();
            if (!_tabs.Next()) return;
            TabsChanged();
            ShowActiveTab();
        }

        private void PreviousTab()
        {
            if (!TabsOn() || _tabs.Count < 2) return;
            TabsRememberPosition();
            if (!_tabs.Previous()) return;
            TabsChanged();
            ShowActiveTab();
        }

        /// <summary>La structure a changé (corbeille vidée, ajout annulé,
        /// renommage) : les onglets d'éléments disparus tombent, les titres
        /// se rafraîchissent ; si l'actif est tombé, son voisin prend la vue.</summary>
        private void TabsPrune()
        {
            if (_project == null || _tabs.Count == 0) return;
            var project = _project;
            var activeDropped = _tabs.Prune(delegate(string id) { return project.FindById(id) != null; });
            TabsChanged();
            if (activeDropped) ShowActiveTab();
        }

        /// <summary>À l'ouverture d'un projet : la rangée enregistrée (ids
        /// disparus écartés), et l'élément à montrer d'abord — celui de
        /// l'onglet actif s'il y en a, sinon celui que la fenêtre a choisi
        /// (dernier récent ou Accueil), qui fera le premier onglet.</summary>
        private BinderItem TabsRestore(Project project, BinderItem fallback)
        {
            _tabs.Clear();
            if (!TabsOn()) { TabsChanged(); return fallback; }
            _tabs.Limit = AppSettings.MaxTabs;
            _tabs.Restore(project.OpenTabs, project.ActiveTab, delegate(string id) { return project.FindById(id) != null; });
            var active = _tabs.ActiveItemId == null ? null : project.FindById(_tabs.ActiveItemId);
            TabsChanged();
            return active ?? fallback;
        }

        /// <summary>Les réglages ont changé (Préférences › Général › Onglets,
        /// ou le mode calme) : la rangée paraît ou se retire, le plafond
        /// serre la rangée par la droite, les entrées de menu suivent.</summary>
        private void ApplyTabsSettings()
        {
            if (_tabStrip == null) return;
            if (!TabsOn())
            {
                _tabs.Clear();
                TabsChanged();
            }
            else
            {
                _tabs.Limit = AppSettings.MaxTabs;
                if (_tabs.Count == 0 && _current != null) _tabs.Navigate(_current.Id);
                TabsChanged();
            }
            _tabStrip.IsVisible = TabsOn() && !_calmMode;
            RefreshMenuAvailability();
        }

        // ---------------------------------------------------------------- sonde

        internal BinderItem CurrentForProbe { get { return _current; } }
        /// <summary>La fenêtre telle quelle en PNG (MARABOOK_TABS_CAPTURE).</summary>
        internal void CaptureForProbe(string path)
        {
            var size = new Avalonia.PixelSize(Math.Max(1, (int)Bounds.Width), Math.Max(1, (int)Bounds.Height));
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96)))
            {
                bitmap.Render(_shellRoot ?? (Avalonia.Visual)this); // sous le voile de l'accueil, comme --capture
                bitmap.Save(path);
            }
        }
        internal TabStrip TabStripForProbe { get { return _tabStrip; } }
        internal WorkTabs TabsForProbe { get { return _tabs; } }
        internal void OpenNewTabForProbe() { OpenNewTab(); }
        internal void OpenInNewTabForProbe(BinderItem item) { OpenInNewTab(item); }
        internal void CloseActiveTabForProbe() { CloseActiveTab(); }
        internal void ActivateTabForProbe(int index) { ActivateTab(index); }
        internal void NextTabForProbe() { NextTab(); }
        internal void PreviousTabForProbe() { PreviousTab(); }
        internal void ApplyTabsSettingsForProbe() { ApplyTabsSettings(); }
    }
}

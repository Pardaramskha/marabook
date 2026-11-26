using Avalonia.Controls;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>L'ATELIER dans la coquille (2.0.0, 21/11/2026, Rémi) : le premier
    /// élément de la section OUTILS de la Pile. Comme le Gros Graph, c'est une
    /// PAGE — un item synthétique, hors du projet, que la Pile montre comme une
    /// racine, que les onglets retiennent et que l'historique rouvre. Il ne
    /// dépend d'aucun projet : ses outils travaillent sur des fichiers du disque.</summary>
    public partial class MainWindow
    {
        public const string AtelierItemId = "atelier";
        public const string AtelierKey = "atelier";
        private readonly BinderItem _atelierItem = new BinderItem
        {
            Id = AtelierItemId,
            Kind = ItemKind.Category,
            CategoryKey = AtelierKey,
            Title = "Atelier",
            Icon = "svg:" + AtelierView.IconAtelier // PROVISOIRE (TO-DO générale)
        };
        private AtelierView _atelierView;

        private static bool IsAtelierItem(BinderItem item) { return item != null && item.IsCategory && item.CategoryKey == AtelierKey; }

        /// <summary>Une PAGE de la Pile qui n'est pas du projet (le Gros Graph, l'Atelier) :
        /// rien à y créer, à renommer, à supprimer ni à y déposer.</summary>
        public static bool IsPageKey(string key) { return key == GraphKey || key == ProfilerKey || key == AtelierKey; }

        private Control BuildAtelierView()
        {
            _atelierView = new AtelierView { IsVisible = false };
            _atelierView.GrammarBridge = delegate { return _editor == null ? null : _editor.GrammarBridge; }; // le Conjugueur de WGYC (22/11)
            Dependencies.ReleaseFiles = delegate { if (_atelierView != null) _atelierView.ReleaseFiles(); }; // Préférences › Dépendances
            _binder.AtelierItem = _atelierItem;
            return _atelierView;
        }

        private void ShowAtelierCore()
        {
            _editor.Clear();
            _sheetView.Clear();
            _atelierView.IsVisible = true;
            _atelierView.Refresh();
        }

        /// <summary>La page de l'Atelier vient d'être quittée pour autre chose (22/11) : l'outil rend son
        /// PDF — et prévient d'abord s'il y avait du travail pas enregistré. Appelé une fois la
        /// sélection posée : si c'est l'Atelier qui se rouvre, il n'y a rien à faire.</summary>
        private void AtelierHidden()
        {
            Ui.Post(Avalonia.Threading.DispatcherPriority.Background, delegate
            {
                if (_atelierView == null || _atelierView.IsVisible) return;
                _atelierView.LeftPage(ShowAtelier);
            });
        }

        /// <summary>Ouvre l'Atelier, par la Pile comme toute page.</summary>
        internal void ShowAtelier() { _binder.SelectItem(AtelierItemId, true); }

        // — Sonde.
        internal AtelierView AtelierForProbe { get { return _atelierView; } }
    }
}

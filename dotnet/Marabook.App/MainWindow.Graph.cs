using System;
using Avalonia.Controls;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE GRAPHE RELATIONNEL dans la fenêtre (1.1.0, 28/10) : une
    /// entrée fixe de la Pile (section Encyclopédie, sous le Dictionnaire),
    /// une entrée du menu Édition › Fiches ; la vue au centre, hors de
    /// l'arbre comme le Journal — tout clic dans la Pile reprend la main.
    /// Un nœud cliqué passe au Général (InspectCard), un double-clic ouvre
    /// l'élément dans l'onglet courant, le menu dans un onglet neuf. Caché
    /// pour le profil Éditeur·ice (Profiles.Graph).</summary>
    public partial class MainWindow
    {
        private GraphView _graphView;
        private MenuItem _graphMenu;

        /// <summary>LE GRAPHE EST UNE PAGE (28/10, Rémi) : un item SYNTHÉTIQUE,
        /// hors du projet, que la Pile montre comme une racine (sélection,
        /// surlignage), que les onglets retiennent, que l'historique rouvre.
        /// Même id pour tous les projets ; jamais écrit dans l'arbre du .plot
        /// (seulement comme id d'onglet, écarté par un vieux Marabook).</summary>
        public const string GraphItemId = "graph";
        public const string GraphKey = "graph";
        private readonly BinderItem _graphItem = new BinderItem
        {
            Id = GraphItemId,
            Kind = ItemKind.Category,
            CategoryKey = GraphKey,
            Title = "Gros Graph", // le nom de Rémi dans la Pile et l'onglet (28/10) ; la page et le menu gardent « Graphe relationnel »
            Icon = "svg:gg" // le dessin de Rémi (GG.svg)
        };

        private static bool IsGraphItem(BinderItem item) { return item != null && item.IsCategory && item.CategoryKey == GraphKey; }

        /// <summary>Un item par son id : du projet, ou le graphe.</summary>
        private BinderItem ResolveItem(string id)
        {
            if (id == null) return null;
            if (id == GraphItemId) return Profiles.Enabled(Profiles.Graph) ? _graphItem : null;
            return _project == null ? null : _project.FindById(id);
        }

        private Control BuildGraphView()
        {
            _graphView = new GraphView { IsVisible = false };
            _binder.GraphItem = _graphItem;
            _graphView.NodeSelected += InspectCard;
            _graphView.NavigateRequested += delegate(BinderItem item) { _binder.SelectItem(item.Id, true); };
            _graphView.NewTabRequested += OpenInNewTab;
            _graphView.LayoutChanged += MarkDirty; // un nœud tiré : sa place est au projet
            _graphView.FacetChanged += RefreshGraphSection; // Carte devant : ses outils dans le Général ; Présences : retirés (31/10)
            return _graphView;
        }

        /// <summary>Ouvre le graphe : par la Pile, comme toute page — l'onglet
        /// actif le retient, l'historique le rouvre. Déjà ouvert : se recalcule.</summary>
        private void ShowGraph()
        {
            if (_project == null || !Profiles.Enabled(Profiles.Graph)) return;
            if (_graphView.IsVisible && _current == _graphItem)
            {
                _graphView.Refresh();
                return;
            }
            _binder.SelectItem(GraphItemId, true);
        }

        /// <summary>La branche de ShowItemCore : la vue paraît, le graphe se
        /// recalcule si le projet a changé depuis la dernière fois.</summary>
        private void ShowGraphCore()
        {
            _editor.Clear();
            _sheetView.Clear();
            _graphView.IsVisible = true;
            _graphView.Open(_project);
            RefreshGraphSection();
        }

        /// <summary>« --graphe fichier.plot » (28/10) : le graphe du projet en
        /// console — pour chaque fiche, les noms cherchés et les écrits où elle
        /// paraît (avec le compte) ; puis les orphelins. Rien n'est écrit.</summary>
        private static void GraphDiagnostic(string path)
        {
            var project = Marabook.Persistence.PlotFile.Load(path);
            var entries = GraphInput.Capture(project);
            var graph = RelationGraph.Build(entries, new GraphOptions { Tags = true });
            Console.WriteLine("GRAPHE " + System.IO.Path.GetFileName(path) + " : " + graph.Nodes.Count + " nœuds, " + graph.Edges.Count + " liens, " + graph.Writings.Count + " écrits du récit");
            var names = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
            foreach (var entry in entries) if (entry.Names != null) names[entry.Id] = entry.Names;
            foreach (var node in graph.Nodes)
            {
                if (!node.IsSheet) continue;
                System.Collections.Generic.List<string> list;
                names.TryGetValue(node.Id, out list);
                Console.WriteLine("  FICHE  " + node.Title + "  — noms : " + (list == null || list.Count == 0 ? "(aucun)" : string.Join(" | ", list.ToArray())) + "  — " + node.Degree + " lien(s), présente dans " + node.Presence + " écrit(s)");
                foreach (var edge in graph.EdgesOf(node.Id))
                {
                    var other = graph.Node(edge.Other(node.Id));
                    if (other == null) continue;
                    Console.WriteLine("         " + GraphView.KindName(edge.Kind).PadRight(9) + " " + other.Title + (edge.Kind == GraphEdgeKind.Presence ? " ×" + edge.Weight : edge.Label.Length > 0 ? " (" + edge.Label + ")" : ""));
                }
            }
            var orphans = graph.Orphans(null);
            Console.WriteLine("  ORPHELINS (" + orphans.Count + ") : " + string.Join(", ", orphans.ConvertAll(delegate(GraphNode n) { return n.Title; }).ToArray()));
        }

        /// <summary>« --encyclopedie fichier.plot dossier » (31/10) : l'Encyclopédie
        /// HTML de toutes les fiches du projet, options du projet (ou d'office),
        /// écrite dans le dossier — pour regarder le rendu sans la fenêtre. Le
        /// projet n'est pas modifié.</summary>
        private static void EncyclopediaDiagnostic(string path, string folder)
        {
            var project = Marabook.Persistence.PlotFile.Load(path);
            var document = Exchange.Encyclopedia.Build(project, Exchange.EncyclopediaSelection.All(), Exchange.Encyclopedia.DefaultOptions(project), Settings.AppSettings.Lexicon);
            var written = Exchange.HtmlEncyclopedia.Write(folder, document, project);
            Console.WriteLine("ENCYCLOPÉDIE " + System.IO.Path.GetFileName(path) + " : " + document.SheetCount + " fiches, " + document.Sections.Count + " sections, " + document.Lexicon.Count + " mots, " + document.Images.Count + " images -> " + written.Count + " fichiers dans " + folder);
        }

        // ---------------------------------------------------------------- sonde

        internal GraphView GraphViewForProbe { get { return _graphView; } }
        internal bool DirtyForProbe { get { return _dirty; } }
        internal void GoBackForProbe() { NavigateHistory(true); }
        internal void ShowGraphForProbe() { ShowGraph(); }
        internal bool GraphMenuShownForProbe { get { return _graphMenu != null && _graphMenu.IsVisible; } }
    }
}

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.History;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La SÉLECTION MULTIPLE (1.0.3) : Ctrl+clic sur les tuiles d'un
    /// tableau, de la bibliothèque de fiches ou dans la Pile. Le Général du
    /// rail change alors de visage — plus les champs d'UN élément, mais des
    /// actions en lot : couleur des tuiles, couleur des icônes, icône,
    /// gabarit et pages extra (livre), dupliquer, corbeille, catégorie
    /// (fiches). Le clic droit sur la sélection offre les mêmes entrées.
    /// Chaque lot est UNE étape d'annulation (CompositeAction).</summary>
    public partial class MainWindow
    {
        private List<BinderItem> _inspectedGroup; // ≥ 2 éléments, sinon null
        private StackPanel _batchSection;

        /// <summary>Une entrée du lot : son libellé, ce qu'elle fait (une
        /// action, ou un sous-menu à remplir).</summary>
        private sealed class BatchEntry
        {
            public string Label;
            public Action Run;
            public Action<ItemsControl> Fill;
            public IBrush Foreground;
        }

        /// <summary>La sélection d'une vue a changé : à partir de deux
        /// éléments, le Général passe en mode lot (et s'ouvre s'il était
        /// replié) ; en dessous, il revient à l'élément.</summary>
        private void InspectGroup(List<BinderItem> items)
        {
            _inspectedGroup = items != null && items.Count > 1 ? new List<BinderItem>(items) : null;
            if (_inspectedGroup != null && AppSettings.RightPanel == RightPanel.None)
                SetRightPanel(RightPanel.Inspector);
            UpdateInspector();
        }

        private StackPanel BuildBatchSection()
        {
            _batchSection = new StackPanel { IsVisible = false, Margin = new Thickness(0, 0, 0, 10) };
            return _batchSection;
        }

        /// <summary>Le Général en mode lot : le compte, la nature commune, un
        /// bouton par action applicable.</summary>
        private void ShowBatchInspector(List<BinderItem> items)
        {
            _inspTitle.Text = items.Count + " éléments sélectionnés";
            _inspKind.Text = BatchKindLabel(items);
            _homeStartSection.IsVisible = false;
            _inspTitle.IsVisible = true;
            _inspKind.IsVisible = true;
            _inspPlanLink.IsVisible = false;
            _statusSection.IsVisible = false;
            _planSection.IsVisible = false;
            _presenceSection.IsVisible = false;
            SetInspectorFieldVisibility(false, false);
            _progressSection.IsVisible = false;
            _statsSection.IsVisible = false;
            _linksLabel.IsVisible = false;
            _linksPanel.IsVisible = false;
            _inspDates.IsVisible = false;
            _batchSection.Children.Clear();
            foreach (var entry in BatchEntries(items))
            {
                var e = entry;
                var button = Buttons.Text(e.Label, null, Buttons.Bar, Buttons.Look.Outline);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.Margin = new Thickness(0, 0, 0, 6);
                if (e.Foreground != null) button.Foreground = e.Foreground;
                button.Click += delegate
                {
                    if (e.Fill == null) { e.Run(); return; }
                    var menu = new ContextMenu();
                    e.Fill(menu);
                    menu.PlacementTarget = button;
                    menu.Placement = PlacementMode.Bottom;
                    Ui.ShowMenu(menu, button);
                };
                _batchSection.Children.Add(button);
            }
            _batchSection.IsVisible = true;
        }

        /// <summary>Le menu du clic droit sur une sélection multiple : les
        /// mêmes entrées que le Général en mode lot.</summary>
        private ContextMenu BuildBatchMenu(List<BinderItem> items)
        {
            if (items == null || items.Count < 2) return null;
            var menu = new ContextMenu();
            foreach (var entry in BatchEntries(items))
            {
                var e = entry;
                if (e.Foreground != null && menu.Items.Count > 0) menu.Items.Add(new Separator());
                var item = new MenuItem { Header = e.Label };
                if (e.Foreground != null) item.Foreground = e.Foreground;
                if (e.Fill != null) e.Fill(item);
                else item.Click += delegate { e.Run(); };
                menu.Items.Add(item);
            }
            return menu;
        }

        /// <summary>« Appliquer le gabarit du livre » en lot (09/10) : la mise
        /// en page de chaque écrit suit le gabarit intérieur de son livre —
        /// le geste de la carte (icône orange), pour toute la sélection.</summary>
        private void BatchApplyBookLayout(List<BinderItem> targets)
        {
            var changed = 0;
            foreach (var item in targets)
            {
                var book = item.EnclosingBook();
                if (item.Kind != ItemKind.Text || book == null || book.Book == null || book.Book.Template == null) continue;
                if (item.Page == null) item.Page = book.Book.Template.Clone();
                else item.Page.ApplyLayout(book.Book.Template);
                _pageCountCache.Remove(item.Id);
                changed++;
            }
            if (changed == 0) return;
            MarkDirty();
            _binder.Rebuild();
            if (_current != null && _current.Kind == ItemKind.Book) _bookView.Load(_current, _history, _project);
        }

        /// <summary>Sonde (09/10) : les libellés des actions groupées.</summary>
        internal List<string> BatchLabelsForProbe(List<BinderItem> items)
        {
            var labels = new List<string>();
            foreach (var entry in BatchEntries(items)) labels.Add(entry.Label);
            return labels;
        }

        private static string BatchKindLabel(List<BinderItem> items)
        {
            var kinds = new HashSet<ItemKind>();
            foreach (var item in items) kinds.Add(item.Kind);
            if (kinds.Count != 1) return "Sélection multiple";
            switch (items[0].Kind)
            {
                case ItemKind.Text: return "Écrits";
                case ItemKind.Sheet: return "Fiches";
                case ItemKind.Book: return "Livres";
                case ItemKind.Folder: return "Dossiers";
                case ItemKind.Media: return "Documents";
                case ItemKind.PageTemplate: return "Gabarits de pages";
                case ItemKind.Plan: return "Plans";
                case ItemKind.MindMap: return "Cartes mentales";
                default: return "Sélection multiple";
            }
        }

        private static bool All(List<BinderItem> items, Predicate<BinderItem> test)
        {
            foreach (var item in items) if (!test(item)) return false;
            return true;
        }

        private List<BatchEntry> BatchEntries(List<BinderItem> items)
        {
            var entries = new List<BatchEntry>();
            var targets = new List<BinderItem>(items);
            targets.RemoveAll(delegate(BinderItem i) { return i == null || i.IsCategory || i.IsOutOfBook; });
            if (targets.Count == 0) return entries;
            var inTrash = All(targets, delegate(BinderItem i) { return i.RootCategory().CategoryKey == Project.KeyTrash; });
            if (!inTrash)
            {
                entries.Add(new BatchEntry
                {
                    Label = "Couleur des tuiles",
                    Fill = delegate(ItemsControl target) { ColorMenus.Fill(target, this, _project, Common(targets, delegate(BinderItem i) { return i.CardColor; }), delegate(string value) { BatchColor(targets, value); }); }
                });
                entries.Add(new BatchEntry
                {
                    Label = "Couleur des icônes",
                    Fill = delegate(ItemsControl target) { ColorMenus.Fill(target, this, _project, null, delegate(string value) { BatchIconColor(targets, value); }); }
                });
                entries.Add(new BatchEntry { Label = "Changer l'icône…", Run = delegate { BatchIcon(targets); } });
                var bookTexts = All(targets, delegate(BinderItem i) { return i.Kind == ItemKind.Text && i.EnclosingBook() != null; });
                if (bookTexts)
                {
                    entries.Add(new BatchEntry { Label = "Appliquer un gabarit…", Run = delegate { ApplyPageTemplateTo(targets); } });
                    entries.Add(new BatchEntry { Label = "Appliquer le gabarit du livre", Run = delegate { BatchApplyBookLayout(targets); } }); // la mise en page du livre (09/10)
                    var allExtra = All(targets, delegate(BinderItem i) { return i.IsExtraPage; });
                    entries.Add(new BatchEntry
                    {
                        Label = allExtra ? "Retirer des pages extra" : "Marquer comme pages extra",
                        Run = delegate { BatchExtraPages(targets, !allExtra); }
                    });
                }
                if (All(targets, delegate(BinderItem i) { return i.Kind == ItemKind.Text || i.Kind == ItemKind.Sheet || i.Kind == ItemKind.PageTemplate; }))
                    entries.Add(new BatchEntry { Label = "Dupliquer", Run = delegate { BatchDuplicate(targets); } });
                if (All(targets, delegate(BinderItem i) { return i.Kind == ItemKind.Sheet; }) && _project != null)
                {
                    var commonCategory = Common(targets, delegate(BinderItem i) { return i.CategoryId; });
                    entries.Add(new BatchEntry
                    {
                        Label = "Changer de catégorie",
                        Fill = delegate(ItemsControl target)
                        {
                            foreach (var category in _project.SheetCategories)
                            {
                                var categoryRef = category;
                                var entry = new MenuItem { Header = category.Name, IsChecked = commonCategory == category.Id };
                                entry.Click += delegate { BatchCategory(targets, categoryRef.Id); };
                                target.Items.Add(entry);
                            }
                        }
                    });
                }
                entries.Add(new BatchEntry
                {
                    Label = "Envoyer à la corbeille",
                    Foreground = BinderView.SoftDeleteInk,
                    Run = delegate { TrashMany(targets); }
                });
            }
            else
            {
                entries.Add(new BatchEntry
                {
                    Label = "Supprimer définitivement",
                    Foreground = BinderView.HardDeleteInk,
                    Run = delegate { HardDeleteMany(targets); }
                });
            }
            return entries;
        }

        /// <summary>La valeur commune à tous (null si elle diffère).</summary>
        private static string Common(List<BinderItem> items, Func<BinderItem, string> read)
        {
            string value = null;
            var first = true;
            foreach (var item in items)
            {
                var v = read(item);
                if (first) { value = v; first = false; }
                else if (v != value) return null;
            }
            return value;
        }

        // ------------------------------------------------------------ les lots

        private void RunBatch(IUndoableAction action)
        {
            _history.Run(action);
            AfterBatch();
        }

        /// <summary>Après un lot : tout ce qui montre ces éléments se redessine ;
        /// la sélection reste (le Général en mode lot aussi).</summary>
        private void AfterBatch()
        {
            MarkDirty();
            RefreshOpenCorkboards();
            if (_sheetLibrary.IsVisible) _sheetLibrary.Refresh();
            UpdateInspector();
            UpdateStats();
        }

        private void BatchColor(List<BinderItem> items, string value)
        {
            var actions = new List<IUndoableAction>();
            foreach (var item in items)
                if (item.CardColor != value) actions.Add(new ChangeColorAction(item, value));
            if (actions.Count > 0) RunBatch(new CompositeAction(actions));
        }

        /// <summary>L'icône teintée de la couleur (la règle du sous-menu de
        /// la Pile) ; inchangée pour une icône fichier ou glyphe.</summary>
        private static string TintedIcon(BinderItem item, string value)
        {
            var current = item.Icon;
            string name = null;
            if (current == null) name = ItemIcons.DefaultSvg(item);
            else if (current.StartsWith("svg:", StringComparison.Ordinal))
            {
                var token = current.Substring(4);
                var colon = token.IndexOf(':');
                name = colon < 0 ? token : token.Substring(0, colon);
            }
            if (name == null) return current;
            return value == null ? (current == null ? null : "svg:" + name) : "svg:" + name + ":" + value;
        }

        private void BatchIconColor(List<BinderItem> items, string value)
        {
            var actions = new List<IUndoableAction>();
            foreach (var item in items)
            {
                var icon = TintedIcon(item, value);
                if (icon != item.Icon) actions.Add(new ChangeIconAction(item, icon));
            }
            if (actions.Count > 0) RunBatch(new CompositeAction(actions));
        }

        private async void BatchIcon(List<BinderItem> items)
        {
            var chosen = await IconPickerDialog.Ask(this);
            if (chosen == null) return;
            var icon = chosen.Length == 0 ? null : chosen;
            var actions = new List<IUndoableAction>();
            foreach (var item in items)
                if (icon != item.Icon) actions.Add(new ChangeIconAction(item, icon));
            if (actions.Count > 0) RunBatch(new CompositeAction(actions));
        }

        /// <summary>Pages extra en lot : marquées = liminaires libres ;
        /// démarquées = pages du récit, sans section ni sorte.</summary>
        private void BatchExtraPages(List<BinderItem> items, bool mark)
        {
            var actions = new List<IUndoableAction>();
            foreach (var item in items)
            {
                if (item.IsExtraPage == mark) continue;
                var target = item;
                bool wasExtra = target.IsExtraPage;
                string wasSection = target.ExtraSection, wasKind = target.ExtraKind;
                actions.Add(new DelegateAction(
                    delegate
                    {
                        target.IsExtraPage = mark;
                        target.ExtraSection = mark ? ExtraPages.SectionFront : null;
                        if (!mark) target.ExtraKind = null;
                    },
                    delegate { target.IsExtraPage = wasExtra; target.ExtraSection = wasSection; target.ExtraKind = wasKind; }));
            }
            if (actions.Count == 0) return;
            RunBatch(new CompositeAction(actions));
            _binder.Rebuild();
        }

        /// <summary>Chaque copie juste après son original : l'index se
        /// calcule au moment d'insérer (après les copies déjà posées), appliqué
        /// au fil de l'eau puis poussé en une étape.</summary>
        private void BatchDuplicate(List<BinderItem> items)
        {
            var actions = new List<IUndoableAction>();
            foreach (var item in items)
            {
                if (item.Parent == null) continue;
                var copy = item.Duplicate();
                var action = new AddItemAction(item.Parent, copy, item.Parent.Children.IndexOf(item) + 1);
                action.Do();
                actions.Add(action);
            }
            if (actions.Count == 0) return;
            _history.Push(new CompositeAction(actions));
            AfterBatch();
        }

        private void BatchCategory(List<BinderItem> items, string categoryId)
        {
            var actions = new List<IUndoableAction>();
            foreach (var item in items)
            {
                if (item.CategoryId == categoryId) continue;
                var target = item;
                var was = target.CategoryId;
                actions.Add(new DelegateAction(delegate { target.CategoryId = categoryId; }, delegate { target.CategoryId = was; }));
            }
            if (actions.Count > 0) RunBatch(new CompositeAction(actions));
        }

        private async void TrashMany(List<BinderItem> items)
        {
            if (items == null || items.Count == 0) return;
            var answer = await MessageDialog.Show(this,
                "Envoyer ces " + items.Count + " éléments à la corbeille ?\nLeur contenu part avec.",
                AppName, MessageButtons.YesNo, MessageIcon.Question);
            if (answer != MessageResult.Yes) return;
            var action = new TrashManyAction(_project.Trash, items);
            if (action.Count == 0) return;
            _inspectedGroup = null;
            _corkboard.ClearSelection();
            _bookView.ClearSelection();
            _sheetLibrary.ClearSelection();
            _binder.ClearMultiSelection();
            RunBatch(action);
        }

        /// <summary>Depuis la corbeille : définitif, annulable tout de même
        /// (les éléments reviennent dans la corbeille).</summary>
        private async void HardDeleteMany(List<BinderItem> items)
        {
            var answer = await MessageDialog.Show(this,
                "Supprimer définitivement ces " + items.Count + " éléments ?",
                AppName, MessageButtons.YesNo, MessageIcon.Warning);
            if (answer != MessageResult.Yes) return;
            var trash = _project.Trash;
            var slots = new List<KeyValuePair<BinderItem, int>>();
            foreach (var item in items)
                if (item.Parent == trash) slots.Add(new KeyValuePair<BinderItem, int>(item, trash.Children.IndexOf(item)));
            if (slots.Count == 0) return;
            slots.Sort(delegate(KeyValuePair<BinderItem, int> a, KeyValuePair<BinderItem, int> b) { return a.Value.CompareTo(b.Value); });
            // « Terre brûlée » / « Masochiste » : ce qui part vraiment, descendants
            // compris (comme Vider la corbeille).
            var gone = new List<BinderItem>();
            foreach (var slot in slots) gone.Add(slot.Key);
            AppSettings.PermanentlyDeleted += Achievements.CountAll(gone);
            _inspectedGroup = null;
            _corkboard.ClearSelection();
            _bookView.ClearSelection();
            _sheetLibrary.ClearSelection();
            _binder.ClearMultiSelection();
            RunBatch(new DelegateAction(
                delegate { foreach (var slot in slots) trash.Children.Remove(slot.Key); },
                delegate
                {
                    foreach (var slot in slots)
                    {
                        slot.Key.Parent = trash;
                        trash.Children.Insert(Math.Min(slot.Value, trash.Children.Count), slot.Key);
                    }
                }));
        }
    }
}

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.History;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LES BOÎTES AU RAIL (2.0.0, 25/11/2026, Rémi) : deux sortes de boîtes des tableaux
    /// n'étaient réglables que par leur menu à trois points — « moins intuitif pour certains » :
    ///  · une SOUS-CATÉGORIE DE FICHES (tableau des Fiches) : cliquée — sur son nom ou son fond,
    ///    ouverte ou repliée —, elle ouvre le Général du rail, qui montre son nom, sa couleur et
    ///    quelques options (nouvelle fiche, champs propres, replier, exporter, supprimer) ;
    ///  · une PARTIE DE LIVRE (tableau des écrits d'un livre) : cliquée sur son nom ou son fond,
    ///    elle se montre de même — son nom, sa couleur (celle d'un dossier, déjà là), ses options
    ///    (ouvrir, nouvel écrit, supprimer).
    /// L'une comme l'autre est remplacée dès que l'on clique ailleurs, ou sur une fiche / un écrit.
    /// Une partie est un élément du projet (un dossier) : elle passe par le chemin des tuiles
    /// (InspectCard) et cette section ne fait que s'y AJOUTER. Une sous-catégorie n'en est pas un :
    /// le Général prend alors un visage à part, comme en mode lot.</summary>
    public partial class MainWindow
    {
        private SheetCategory _inspectedCategory; // la sous-catégorie que le Général montre, ou null
        private StackPanel _groupSection, _groupOptions;
        private TextBox _groupName;
        private TextBlock _groupColorLabel, _groupInfo;
        private Button _groupColorButton;
        private Border _groupColorDot;
        private bool _groupCategoryMode, _groupLoading;
        private BinderItem _groupPart; // la partie que la section montre, ou null

        private StackPanel BuildGroupSection()
        {
            _groupSection = new StackPanel { IsVisible = false, Margin = new Thickness(0, 0, 0, 12), Tag = "group-section" };
            _groupSection.Children.Add(InspectorLabel("Nom", null, 0));
            _groupName = new TextBox { Tag = "group-name" };
            _groupName.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter && e.Key != Key.Return) return;
                e.Handled = true;
                CommitGroupName();
            };
            _groupName.LostFocus += delegate { CommitGroupName(); };
            _groupSection.Children.Add(_groupName);

            _groupColorLabel = InspectorLabel("Couleur", null, 10);
            _groupSection.Children.Add(_groupColorLabel);
            _groupColorDot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            Skin.KeepCorners(_groupColorDot);
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
            colorRow.Children.Add(_groupColorDot);
            colorRow.Children.Add(new TextBlock { Text = "Choisir…", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            _groupColorButton = new Button { Content = colorRow, HorizontalAlignment = HorizontalAlignment.Left, Tag = "group-color" };
            _groupColorButton.Click += delegate
            {
                var category = _inspectedCategory;
                if (category == null) return;
                var menu = new ContextMenu();
                ColorMenus.Fill(menu, this, _project, category.Color, delegate(string value) { _sheetLibrary.SetCategoryColor(category, value); });
                menu.PlacementTarget = _groupColorButton;
                menu.Placement = PlacementMode.Bottom;
                Ui.ShowMenu(menu, _groupColorButton);
            };
            _groupSection.Children.Add(_groupColorButton);

            _groupInfo = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Tag = "group-info" };
            _groupSection.Children.Add(_groupInfo);
            _groupSection.Children.Add(InspectorLabel("Options", null, 12));
            _groupOptions = new StackPanel { Tag = "group-options" };
            _groupSection.Children.Add(_groupOptions);
            return _groupSection;
        }

        private void AddGroupOption(string label, string tip, bool danger, Action run)
        {
            var button = Buttons.Text(label, tip, Buttons.Bar, Buttons.Look.Outline);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0, 0, 0, 6);
            if (danger) button.Foreground = Chrome.Danger;
            button.Click += delegate { run(); };
            _groupOptions.Children.Add(button);
        }

        // ------------------------------------------------------------ la sous-catégorie de fiches

        /// <summary>Une sous-catégorie cliquée au tableau des Fiches (null : lâchée).</summary>
        private void InspectCategory(SheetCategory category)
        {
            _inspectedCategory = category;
            if (category != null)
            {
                _inspected = null;
                _inspectedGroup = null;
                OpenGeneralPanel();
            }
            UpdateInspector();
        }

        /// <summary>Le Général s'ouvre pour montrer ce qui vient d'être cliqué — sauf rail verrouillé
        /// (rien ne s'ouvre ni ne change tout seul, hotfix 1.0.3-a).</summary>
        private void OpenGeneralPanel()
        {
            if (AppSettings.RailLocked && AppSettings.RightPanel != RightPanel.None && ShownRightPanel() != RightPanel.None) return;
            if (AppSettings.RailLocked && AppSettings.RightPanel == RightPanel.None) return;
            if (AppSettings.RightPanel != RightPanel.Inspector && IsPanelAvailable(RightPanel.Inspector)) SetRightPanel(RightPanel.Inspector);
        }

        /// <summary>Vrai si le Général doit montrer la sous-catégorie : elle existe encore, et son tableau est devant.</summary>
        private bool CategoryInspected()
        {
            if (_inspectedCategory == null) return false;
            if (_project != null && _sheetLibrary != null && _sheetLibrary.IsVisible && _project.SheetCategories.Contains(_inspectedCategory)
                && ReferenceEquals(_sheetLibrary.SelectedCategory, _inspectedCategory)) return true;
            _inspectedCategory = null; // supprimée, lâchée par le tableau (une fiche choisie), ou l'on est parti
            if (_sheetLibrary != null) _sheetLibrary.ClearCategorySelection();
            return false;
        }

        /// <summary>Le Général au visage d'une sous-catégorie : son nom, sa couleur, ses options — rien d'autre.</summary>
        private void ShowCategoryInspector(SheetCategory category)
        {
            var parent = _project.ParentOf(category);
            var sub = category.IsSub && parent != null;
            _inspTitle.Text = category.Name;
            _inspKind.Text = sub ? "Sous-catégorie de « " + parent.Name + " »" : "Catégorie de fiches";
            _homeStartSection.IsVisible = false;
            _inspTitle.IsVisible = true;
            _inspKind.IsVisible = true;
            _inspPlanLink.IsVisible = false;
            _statusSection.IsVisible = false;
            _planSection.IsVisible = false;
            _goalSection.IsVisible = false;
            _tagsSection.IsVisible = false;
            _presenceSection.IsVisible = false;
            SetInspectorFieldVisibility(false, false);
            _progressSection.IsVisible = false;
            _statsSection.IsVisible = false;
            _linksLabel.IsVisible = false;
            _linksPanel.IsVisible = false;
            _inspDates.IsVisible = false;
            _batchSection.IsVisible = false;
            RefreshModuleSection(null);
            RefreshTimelineSection(null);
            RefreshMapSection(null);

            _groupCategoryMode = true;
            _groupPart = null;
            _groupLoading = true;
            if (!_groupName.IsFocused) _groupName.Text = category.Name;
            _groupLoading = false;
            _groupColorLabel.IsVisible = true;
            _groupColorButton.IsVisible = true;
            _groupColorDot.Background = category.Color == null ? Brushes.Transparent : new SolidColorBrush(Ink.Parse(category.Color).ToColor());
            ToolTip.SetTip(_groupColorButton, category.Color == null ? "Aucune couleur" : ColorPalette.Describe(category.Color));
            var count = _sheetLibrary.CountInCategory(category);
            var info = count == 0 ? "Aucune fiche" : count == 1 ? "1 fiche" : count + " fiches";
            if (sub)
            {
                var inherited = _project.FindTemplate(parent.TemplateId);
                info += "\nModèle hérité : " + (inherited != null ? inherited.Name : "aucun");
                info += "\n" + (category.ExtraFields.Count == 0 ? "Aucun champ propre" : category.ExtraFields.Count == 1 ? "1 champ propre" : category.ExtraFields.Count + " champs propres");
            }
            _groupInfo.Text = info;
            _groupInfo.IsVisible = true;

            _groupOptions.Children.Clear();
            var what = sub ? "la sous-catégorie" : "la catégorie";
            var collapsed = SheetLibraryView.CollapsedCategories.Contains(category.Id);
            AddGroupOption("Nouvelle fiche ici", "Une fiche dans « " + _project.CategoryPath(category) + " », sur son modèle de base", false, delegate { _sheetLibrary.NewSheetInCategory(category); });
            if (sub)
            {
                AddGroupOption("Champs propres…", "Les champs que cette sous-catégorie ajoute au modèle hérité, sur chacune de ses fiches", false, delegate { _sheetLibrary.EditCategoryFields(category); });
                AddGroupOption(collapsed ? "Déplier" : "Replier", collapsed ? "Montrer ses fiches" : "Cacher ses fiches, pour gagner de la place", false, delegate { _sheetLibrary.ToggleCategoryCollapsed(category); UpdateInspector(); });
            }
            AddGroupOption("Exporter " + what + "…", null, false, delegate { _sheetLibrary.ExportCategory(category); });
            AddGroupOption("Supprimer " + what, null, true, delegate { _sheetLibrary.RemoveCategory(category); });
            _groupSection.IsVisible = true;
        }

        // ------------------------------------------------------------ la partie de livre

        /// <summary>Vrai pour une partie : un dossier rangé dans un livre.</summary>
        private static bool IsBookPart(BinderItem item)
        {
            return item != null && item.Kind == ItemKind.Folder && !item.IsCategory && item.EnclosingBook() != null;
        }

        /// <summary>Après le chemin ordinaire du Général : la section s'ajoute pour une partie cliquée
        /// au tableau, et s'efface pour tout le reste.</summary>
        private void RefreshPartSection()
        {
            _groupCategoryMode = false;
            var part = IsBookPart(_inspected) ? _inspected : null;
            _groupPart = part;
            if (part == null) { _groupSection.IsVisible = false; return; }
            _inspKind.Text = "Partie de « " + part.EnclosingBook().Title + " »";
            _groupLoading = true;
            if (!_groupName.IsFocused) _groupName.Text = part.Title;
            _groupLoading = false;
            _groupColorLabel.IsVisible = false; // la couleur d'une partie : celle d'un dossier, juste au-dessus
            _groupColorButton.IsVisible = false;
            var texts = 0;
            foreach (var child in part.Children) if (child.Kind == ItemKind.Text) texts++;
            _groupInfo.Text = texts == 0 ? "Aucun écrit" : texts == 1 ? "1 écrit" : texts + " écrits";
            _groupInfo.IsVisible = true;
            _groupOptions.Children.Clear();
            AddGroupOption("Ouvrir la partie", "Son tableau, à elle seule", false, delegate { _binder.SelectItem(part.Id); });
            AddGroupOption("Nouvel écrit dans la partie", null, false, delegate { _binder.NewText(part); });
            AddGroupOption("Supprimer la partie…", "À la corbeille, avec ses écrits", true, async delegate
            {
                if (!await ConfirmTrash(part)) return;
                _inspected = null;
                _binder.Delete(part);
                RefreshOpenCorkboards();
                UpdateInspector();
                UpdateStats();
            });
            _groupSection.IsVisible = true;
        }

        /// <summary>Le nom tapé dans la section devient celui de la sous-catégorie ou de la partie (annulable).</summary>
        private void CommitGroupName()
        {
            if (_groupLoading || _project == null) return;
            var name = (_groupName.Text ?? "").Trim();
            if (_groupCategoryMode && _inspectedCategory != null)
            {
                if (name.Length == 0) { _groupName.Text = _inspectedCategory.Name; return; }
                if (name != _inspectedCategory.Name) _sheetLibrary.RenameCategory(_inspectedCategory, name);
                return;
            }
            var part = _groupPart;
            if (part == null) return;
            if (name.Length == 0) { _groupName.Text = part.Title; return; }
            if (name == part.Title) return;
            _history.Run(new RenameItemAction(part, name, _project)); // les [[liens]] suivent (1.0.5)
            MarkDirty();
            _binder.Rebuild();
            RefreshOpenCorkboards();
            UpdateInspector();
        }

        // — Sonde.
        internal SheetCategory InspectedCategoryForProbe { get { return _inspectedCategory; } }
        internal bool GroupSectionShownForProbe { get { return _groupSection != null && _groupSection.IsVisible; } }
        internal string GroupNameForProbe { get { return _groupName == null ? null : _groupName.Text; } }
        internal void RenameGroupForProbe(string name) { _groupName.Text = name; CommitGroupName(); }
        internal List<string> GroupOptionsForProbe
        {
            get
            {
                var labels = new List<string>();
                foreach (var child in _groupOptions.Children)
                {
                    var button = child as Button;
                    if (button == null) continue;
                    foreach (var visual in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(button)) { var text = visual as TextBlock; if (text != null) { labels.Add(text.Text); break; } }
                }
                return labels;
            }
        }
        internal void RefreshInspectorForProbe() { UpdateInspector(); }
        /// <summary>Choisit ces tuiles dans le tableau qui est devant (aucune : comme un clic dans le vide).</summary>
        internal void SelectCardsForProbe(IEnumerable<string> ids)
        {
            if (_bookView.IsVisible) _bookView.TextsBoard.SelectForProbe(ids);
            else if (_corkboard.IsVisible) _corkboard.SelectForProbe(ids);
            else if (_sheetLibrary.IsVisible) _sheetLibrary.SelectForProbe(ids);
        }
        internal bool GroupColorShownForProbe { get { return _groupColorButton != null && _groupColorButton.IsVisible; } }
    }
}

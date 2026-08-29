using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UniversSale.Model;

namespace UniversSale.View
{
    /// <summary>Le panneau de RECHERCHE du projet (batch 37, lot B) — le
    /// troisième panneau de la colonne de droite, de la même famille que
    /// Correction et Annotations : en tête la requête et ses options (casse,
    /// mot entier, accents, regex), la portée (document · livre ou dossier ·
    /// projet), le filtre par nature, la corbeille ; puis les occurrences
    /// GROUPÉES PAR ITEM (compte par item, total honnête « 200 premières
    /// sur 1 340 »), chaque ligne = l'extrait avec le terme en évidence et
    /// l'emplacement (paragraphe ou champ). Clic = navigation à
    /// l'occurrence ; F3 / Maj+F3 = suivante / précédente à travers tout le
    /// projet. La recherche tourne hors du fil UI (annulable, anti-rebond,
    /// résultat périmé jeté) ; RunNow la fait de façon synchrone (F3 sans
    /// résultat, sondes).</summary>
    public class SearchPanel : DockPanel
    {
        private readonly TextBox _queryBox;
        private readonly ToggleButton _caseButton, _wordButton, _accentButton, _regexButton;
        private readonly ComboBox _scopeCombo, _kindCombo;
        private readonly CheckBox _trashCheck;
        private readonly TextBlock _summary, _error, _notice;
        private readonly TextBox _replaceBox;
        private readonly Button _replaceOne, _replaceAll;
        private readonly Border _errorFrame;
        private readonly StackPanel _list;
        private readonly ScrollViewer _scroll;

        private Project _project;
        private BinderItem _current;
        private SearchResult _result;
        private readonly List<Border> _rows = new List<Border>();
        private int _currentIndex = -1;

        private DispatcherTimer _debounce;
        private CancellationTokenSource _cancel;
        private int _generation;

        public event Action<SearchHit> NavigateRequested;
        public event Action CloseRequested;
        public event Action ResultsChanged;
        public event Action<SearchHit, string> ReplaceRequested;   // l'occurrence courante (lot C)
        public event Action<string> ReplaceAllRequested;           // toutes, après prévisualisation

        public string ReplaceText
        {
            get { return _replaceBox.Text; }
            set { _replaceBox.Text = value ?? ""; }
        }

        /// <summary>Une ligne d'information sous le récapitulatif (« 12
        /// occurrences remplacées dans 4 items », conflits…) — effacée à la
        /// recherche suivante.</summary>
        public void SetNotice(string text)
        {
            _notice.Text = text ?? "";
            _notice.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public SearchPanel()
        {
            Background = Chrome.BarBgLight;
            var frame = new Border
            {
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(12, 10, 12, 10)
            };
            var panel = new DockPanel();

            // ---------------------------------------------------- en-tête
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(head, Dock.Top);
            var titleRow = new DockPanel();
            var close = new Button { Content = "✕", Width = 22, Padding = new Thickness(0), FontSize = 11, ToolTip = "Fermer le panneau de recherche", Focusable = false };
            close.Click += delegate { OnCloseRequested(); };
            DockPanel.SetDock(close, Dock.Right);
            titleRow.Children.Add(close);
            titleRow.Children.Add(new TextBlock
            {
                Text = "Recherche",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            head.Children.Add(titleRow);

            _errorFrame = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 6, 0, 0) };
            _queryBox = new TextBox { ToolTip = "Le texte (ou l'expression régulière) à chercher — Entrée relance, Échap efface" };
            _queryBox.TextChanged += delegate { SetNotice(null); Schedule(); };
            _queryBox.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { RunNow(); if (_result != null && _result.Hits.Count > 0 && _currentIndex < 0) Next(); e.Handled = true; }
                else if (e.Key == Key.Escape) { _queryBox.Text = ""; e.Handled = true; }
            };
            _errorFrame.Child = _queryBox;
            head.Children.Add(_errorFrame);
            _error = new TextBlock { Foreground = Chrome.Accent, FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 3, 0, 0) };
            head.Children.Add(_error);

            var options = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            _caseButton = Option("Aa", "Respecter la casse");
            _wordButton = Option("Mot", "Mot entier (sans objet en expression régulière)");
            _accentButton = Option("é=e", "Insensible aux accents (« eleve » trouve « élève »)");
            _accentButton.IsChecked = true;
            _regexButton = Option(".*", "Expression régulière (.NET) — casse et accents s'appliquent aussi");
            _regexButton.Checked += delegate { _wordButton.IsEnabled = false; };
            _regexButton.Unchecked += delegate { _wordButton.IsEnabled = true; };
            options.Children.Add(_caseButton);
            options.Children.Add(_wordButton);
            options.Children.Add(_accentButton);
            options.Children.Add(_regexButton);
            head.Children.Add(options);

            var scopeRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            _scopeCombo = new ComboBox { FontSize = 11, MinWidth = 120, Margin = new Thickness(0, 0, 4, 3), ToolTip = "La portée de la recherche" };
            _scopeCombo.Items.Add("Document courant");
            _scopeCombo.Items.Add("Livre ou dossier courant");
            _scopeCombo.Items.Add("Projet entier");
            _scopeCombo.SelectedIndex = 2;
            _scopeCombo.SelectionChanged += delegate { Schedule(); };
            scopeRow.Children.Add(_scopeCombo);
            _kindCombo = new ComboBox { FontSize = 11, MinWidth = 96, Margin = new Thickness(0, 0, 4, 3), ToolTip = "La nature des items fouillés" };
            foreach (var label in new[] { "Tout", "Écrits", "Fiches", "Plans", "Dictionnaire", "Médias" }) _kindCombo.Items.Add(label);
            _kindCombo.SelectedIndex = 0;
            _kindCombo.SelectionChanged += delegate { Schedule(); };
            scopeRow.Children.Add(_kindCombo);
            _trashCheck = new CheckBox { Content = "Corbeille", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 3), ToolTip = "Chercher aussi dans la corbeille (exclue par défaut)" };
            _trashCheck.Checked += delegate { Schedule(); };
            _trashCheck.Unchecked += delegate { Schedule(); };
            scopeRow.Children.Add(_trashCheck);
            head.Children.Add(scopeRow);

            // — Remplacer (lot C) : le texte, l'occurrence courante, tout (prévisualisé).
            var replaceRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var replaceLabel = new TextBlock { Text = "Remplacer par", Foreground = Chrome.SoftText, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(replaceLabel, Dock.Left);
            replaceRow.Children.Add(replaceLabel);
            _replaceBox = new TextBox { ToolTip = "Le texte de remplacement — en expression régulière, $1 ou ${nom} insèrent les groupes capturés" };
            replaceRow.Children.Add(_replaceBox);
            head.Children.Add(replaceRow);
            var replaceButtons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            _replaceOne = new Button
            {
                Content = new TextBlock { Text = "Remplacer", FontSize = 11 },
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(0, 0, 4, 3),
                ToolTip = "Remplacer l'occurrence courante (F3 pour avancer)",
                Focusable = false
            };
            _replaceOne.Click += delegate
            {
                if (CurrentHit == null && !Next()) return;
                var handler = ReplaceRequested;
                if (handler != null) handler(CurrentHit, _replaceBox.Text);
            };
            replaceButtons.Children.Add(_replaceOne);
            _replaceAll = new Button
            {
                Content = new TextBlock { Text = "Tout remplacer…", FontSize = 11 },
                Padding = new Thickness(8, 1, 8, 1),
                Margin = new Thickness(0, 0, 4, 3),
                ToolTip = "Prévisualiser puis remplacer toutes les occurrences de la portée — annulable en un Ctrl+Z",
                Focusable = false
            };
            _replaceAll.Click += delegate
            {
                var handler = ReplaceAllRequested;
                if (handler != null) handler(_replaceBox.Text);
            };
            replaceButtons.Children.Add(_replaceAll);
            head.Children.Add(replaceButtons);

            _summary = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            head.Children.Add(_summary);
            _notice = new TextBlock { Foreground = Chrome.Ink, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            head.Children.Add(_notice);
            panel.Children.Add(head);

            // ---------------------------------------------------- la liste
            _list = new StackPanel();
            _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list };
            panel.Children.Add(_scroll);
            frame.Child = panel;
            Children.Add(frame);
        }

        private void OnCloseRequested()
        {
            var handler = CloseRequested;
            if (handler != null) handler();
        }

        private ToggleButton Option(string label, string tooltip)
        {
            var button = new ToggleButton
            {
                Content = new TextBlock { Text = label, FontSize = 11 },
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(0, 0, 4, 3),
                ToolTip = tooltip,
                Focusable = false
            };
            button.Checked += delegate { Schedule(); };
            button.Unchecked += delegate { Schedule(); };
            return button;
        }

        // ================================================== API de la coquille

        public void SetProject(Project project)
        {
            _project = project;
            _current = null;
            ClearResults();
        }

        /// <summary>L'item courant (portées « document » et « conteneur »).</summary>
        public void SetCurrent(BinderItem current)
        {
            var changed = !ReferenceEquals(_current, current);
            _current = current;
            if (changed && _scopeCombo.SelectedIndex < 2 && _queryBox.Text.Length > 0) Schedule();
        }

        public void FocusQuery()
        {
            _queryBox.Focus();
            _queryBox.SelectAll();
        }

        public string QueryText
        {
            get { return _queryBox.Text; }
            set { _queryBox.Text = value ?? ""; }
        }

        public SearchResult Result { get { return _result; } }
        public int CurrentIndex { get { return _currentIndex; } }
        public SearchHit CurrentHit
        {
            get { return _result != null && _currentIndex >= 0 && _currentIndex < _result.Hits.Count ? _result.Hits[_currentIndex] : null; }
        }

        public SearchScope Scope
        {
            get { return (SearchScope)Math.Max(0, _scopeCombo.SelectedIndex); }
            set { _scopeCombo.SelectedIndex = (int)value; }
        }

        public SearchKind Kind
        {
            get { return (SearchKind)Math.Max(0, _kindCombo.SelectedIndex); }
            set { _kindCombo.SelectedIndex = (int)value; }
        }

        public bool IncludeTrash
        {
            get { return _trashCheck.IsChecked == true; }
            set { _trashCheck.IsChecked = value; }
        }

        public bool MatchCase { get { return _caseButton.IsChecked == true; } set { _caseButton.IsChecked = value; } }
        public bool WholeWord { get { return _wordButton.IsChecked == true; } set { _wordButton.IsChecked = value; } }
        public bool IgnoreAccents { get { return _accentButton.IsChecked == true; } set { _accentButton.IsChecked = value; } }
        public bool UseRegex { get { return _regexButton.IsChecked == true; } set { _regexButton.IsChecked = value; } }

        /// <summary>La requête compilée telle que le panneau la pose.</summary>
        public SearchQuery Query()
        {
            return SearchQuery.Create(_queryBox.Text, MatchCase, WholeWord, IgnoreAccents, UseRegex);
        }

        /// <summary>Occurrence suivante (F3) : à travers tout le projet ; sans
        /// résultat encore, cherche d'abord. Rend faux s'il n'y a rien.</summary>
        public bool Next()
        {
            if (_result == null && _queryBox.Text.Length > 0) RunNow();
            if (_result == null || _result.Hits.Count == 0) return false;
            Select((_currentIndex + 1) % _result.Hits.Count, true);
            return true;
        }

        public bool Previous()
        {
            if (_result == null && _queryBox.Text.Length > 0) RunNow();
            if (_result == null || _result.Hits.Count == 0) return false;
            Select(_currentIndex <= 0 ? _result.Hits.Count - 1 : _currentIndex - 1, true);
            return true;
        }

        /// <summary>Relance la recherche (après un remplacement, une annulation).</summary>
        public void Refresh()
        {
            if (_queryBox.Text.Length > 0) Schedule();
        }

        // ================================================== la recherche

        private void Schedule()
        {
            if (_debounce == null)
            {
                _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _debounce.Tick += delegate { _debounce.Stop(); Run(false); };
            }
            _debounce.Stop();
            _debounce.Start();
        }

        /// <summary>Recherche synchrone, résultat affiché avant de rendre.</summary>
        public void RunNow()
        {
            if (_debounce != null) _debounce.Stop();
            Run(true);
        }

        private void Run(bool synchronous)
        {
            if (_cancel != null) { _cancel.Cancel(); _cancel = null; }
            var generation = ++_generation;
            var query = Query();
            ShowError(query.IsValid || query.IsEmpty ? null : query.Error);
            if (_project == null || !query.IsValid)
            {
                ClearResults();
                return;
            }
            var targets = ProjectSearch.Collect(_project, Scope, _current, Kind, IncludeTrash);
            if (synchronous)
            {
                Show(ProjectSearch.Run(targets, query, ProjectSearch.DefaultCap, ProjectSearch.DefaultBudget, CancellationToken.None));
                return;
            }
            var cancel = new CancellationTokenSource();
            _cancel = cancel;
            var dispatcher = Dispatcher;
            Task.Factory.StartNew<SearchResult>(delegate
            {
                return ProjectSearch.Run(targets, query, ProjectSearch.DefaultCap, ProjectSearch.DefaultBudget, cancel.Token);
            }, cancel.Token).ContinueWith(delegate(Task<SearchResult> done)
            {
                var ignored = done.Exception; // observée : une recherche en faute se tait
                if (done.Status != TaskStatus.RanToCompletion || done.Result.Cancelled) return;
                dispatcher.BeginInvoke(new Action(delegate
                {
                    if (generation != _generation) return; // périmé
                    Show(done.Result);
                }));
            });
        }

        private void ShowError(string message)
        {
            _errorFrame.BorderBrush = message == null ? Brushes.Transparent : Chrome.Accent;
            _error.Text = message ?? "";
            _error.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ClearResults()
        {
            _result = null;
            _currentIndex = -1;
            _rows.Clear();
            _list.Children.Clear();
            _summary.Text = "";
            var handler = ResultsChanged;
            if (handler != null) handler();
        }

        private void Show(SearchResult result)
        {
            _result = result;
            _currentIndex = -1;
            _rows.Clear();
            _list.Children.Clear();
            var summary = result.Summary();
            if (result.NoProofCount > 0) summary += " · " + result.NoProofCount + " dans des passages « ne pas corriger »";
            if (result.InexactCount > 0) summary += " · " + result.InexactCount + " sur une ligature (non remplaçables)";
            if (!string.IsNullOrEmpty(result.Message)) summary += "\n" + result.Message;
            _summary.Text = summary;

            BinderItem group = null;
            var counts = new Dictionary<BinderItem, int>();
            foreach (var hit in result.Hits)
            {
                int n;
                counts.TryGetValue(hit.Item, out n);
                counts[hit.Item] = n + 1;
            }
            for (var i = 0; i < result.Hits.Count; i++)
            {
                var hit = result.Hits[i];
                if (hit.Item != group)
                {
                    group = hit.Item;
                    _list.Children.Add(GroupHeader(hit.Item, counts[hit.Item]));
                }
                var row = HitRow(hit, i);
                _rows.Add(row);
                _list.Children.Add(row);
            }
            var handler = ResultsChanged;
            if (handler != null) handler();
        }

        // ================================================== rendu

        private UIElement GroupHeader(BinderItem item, int count)
        {
            var header = new DockPanel { Margin = new Thickness(0, 8, 0, 3) };
            header.Children.Add(ItemIcons.Render(item, 11, Chrome.SoftText));
            var badge = new TextBlock
            {
                Text = count.ToString(),
                Foreground = Chrome.SoftText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = count == 1 ? "1 occurrence" : count + " occurrences"
            };
            DockPanel.SetDock(badge, Dock.Right);
            header.Children.Add(badge);
            var title = new TextBlock
            {
                Text = item.IsCategory ? "Dictionnaire" : item.Title,
                Foreground = Chrome.Ink,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Aller à la première occurrence de cet item"
            };
            var itemRef = item;
            title.MouseLeftButtonDown += delegate
            {
                for (var i = 0; i < _result.Hits.Count; i++)
                    if (_result.Hits[i].Item == itemRef) { Select(i, true); break; }
            };
            header.Children.Add(title);
            return header;
        }

        private Border HitRow(SearchHit hit, int index)
        {
            var stack = new StackPanel();
            var excerpt = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Chrome.Ink };
            var text = hit.Excerpt ?? "";
            var start = Math.Max(0, Math.Min(hit.ExcerptStart, text.Length));
            var length = Math.Max(0, Math.Min(hit.ExcerptLength, text.Length - start));
            if (start > 0) excerpt.Inlines.Add(new Run(text.Substring(0, start)));
            excerpt.Inlines.Add(new Run(text.Substring(start, length)) { FontWeight = FontWeights.Bold, Foreground = Chrome.Accent });
            if (start + length < text.Length) excerpt.Inlines.Add(new Run(text.Substring(start + length)));
            stack.Children.Add(excerpt);
            var where = hit.Field.Label;
            if (hit.NoProof) where += " · ne pas corriger";
            if (!hit.Exact) where += " · ligature";
            stack.Children.Add(new TextBlock
            {
                Text = where,
                Foreground = Chrome.SoftText,
                FontSize = 10,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var row = new Border
            {
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 2),
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = stack,
                Tag = index,
                ToolTip = "Aller à cette occurrence"
            };
            row.MouseLeftButtonDown += delegate { Select(index, true); };
            return row;
        }

        /// <summary>Met une occurrence en évidence et, si demandé, y navigue.</summary>
        public void Select(int index, bool navigate)
        {
            if (_result == null || index < 0 || index >= _result.Hits.Count) return;
            if (_currentIndex >= 0 && _currentIndex < _rows.Count) _rows[_currentIndex].Background = Brushes.Transparent;
            _currentIndex = index;
            if (index < _rows.Count)
            {
                _rows[index].Background = Chrome.BarBg;
                _rows[index].BringIntoView();
            }
            if (!navigate) return;
            var handler = NavigateRequested;
            if (handler != null) handler(_result.Hits[index]);
        }
    }
}

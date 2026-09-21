using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>L'onglet d'une fiche de MODULE (DLC, 22/09) dans la fiche :
    /// un indicateur de remplissage en tête, puis les papers du module —
    /// même dessin que les papers de la fiche — sur deux colonnes équilibrées
    /// (une seule si la place manque). Chaque champ : son libellé en gras,
    /// son indication plus petite et grise dessous, une zone multiligne de
    /// deux lignes (ou plus) qui grandit à la frappe. Un paper à colonnes
    /// (« Préféré·e·s / Détesté·e·s ») dédouble chaque champ. Les valeurs
    /// vont droit dans BinderItem.ModuleValues[module].</summary>
    public class ModuleSheetView : Grid
    {
        private ModuleInfo _module;
        private BinderItem _item;
        private Action _edited;
        private Dictionary<string, string> _values;
        private readonly StackPanel _left = new StackPanel();
        private readonly StackPanel _right = new StackPanel();
        private readonly List<KeyValuePair<Border, int>> _papers = new List<KeyValuePair<Border, int>>();
        private readonly TextBlock _progressText;
        private readonly Border _progressFill;
        private bool _loading;
        private int _mode = -1;

        public ModuleSheetView()
        {
            Margin = new Thickness(2, 4, 2, 10);
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition());
            var head = new StackPanel { Margin = new Thickness(6, 4, 6, 8) };
            _progressText = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12 };
            head.Children.Add(_progressText);
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Chrome.Border, Margin = new Thickness(0, 4, 0, 0) };
            _progressFill = new Border { CornerRadius = new CornerRadius(2), Background = Chrome.Accent, HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0.8, Width = 0 };
            track.Child = _progressFill;
            track.SizeChanged += delegate { RefreshProgress(); };
            head.Children.Add(track);
            Children.Add(head);
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            SetColumn(_right, 1);
            columns.Children.Add(_left);
            columns.Children.Add(_right);
            SetRow(columns, 1);
            Children.Add(columns);
            _columns = columns;
            SizeChanged += delegate { Layout(false); };
        }

        private readonly Grid _columns;

        public ModuleInfo Module { get { return _module; } }

        public void Load(ModuleInfo module, BinderItem item, Action edited)
        {
            _module = module;
            _item = item;
            _edited = edited;
            _values = Modules.ValuesOf(module, item, true);
            _loading = true;
            _papers.Clear();
            foreach (var paper in module.Papers)
                _papers.Add(new KeyValuePair<Border, int>(SheetView.Paper(paper.Title, BuildPaper(paper), null), Weight(paper)));
            _loading = false;
            Layout(true);
            RefreshProgress();
        }

        private static int Weight(ModulePaper paper)
        {
            var weight = 2;
            foreach (var field in paper.Fields) weight += 2 + field.Lines * Math.Max(1, paper.Columns.Count);
            return weight;
        }

        /// <summary>Deux colonnes dès 760 px, remplies au plus court ; une seule sinon.</summary>
        private void Layout(bool force)
        {
            var wantMode = ActualWidth >= 760 ? 2 : 1;
            if (wantMode == _mode && !force) return;
            _mode = wantMode;
            _left.Children.Clear();
            _right.Children.Clear();
            _columns.ColumnDefinitions[1].Width = wantMode == 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            int leftWeight = 0, rightWeight = 0;
            foreach (var pair in _papers)
            {
                if (wantMode == 1 || leftWeight <= rightWeight) { _left.Children.Add(pair.Key); leftWeight += pair.Value; }
                else { _right.Children.Add(pair.Key); rightWeight += pair.Value; }
            }
        }

        private UIElement BuildPaper(ModulePaper paper)
        {
            var stack = new StackPanel();
            if (paper.Columns.Count > 0)
            {
                var heads = new UniformGrid { Columns = paper.Columns.Count, Margin = new Thickness(0, 0, 0, 4) };
                foreach (var column in paper.Columns)
                    heads.Children.Add(new TextBlock { Text = column, Foreground = Chrome.Accent, FontWeight = FontWeights.Bold, FontSize = 11 });
                stack.Children.Add(heads);
            }
            string group = null;
            foreach (var field in paper.Fields)
            {
                if (field.Group.Length > 0 && field.Group != group)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = field.Group,
                        Foreground = Chrome.Accent,
                        FontWeight = FontWeights.Bold,
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 6, 0, 4)
                    });
                }
                group = field.Group.Length > 0 ? field.Group : group;
                stack.Children.Add(new TextBlock { Text = field.Label, Foreground = Chrome.Ink, FontWeight = FontWeights.SemiBold, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
                if (field.Hint.Length > 0)
                    stack.Children.Add(new TextBlock { Text = field.Hint, Foreground = Chrome.SoftText, FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
                if (paper.Columns.Count == 0) stack.Children.Add(Box(field.Id, field.Lines));
                else
                {
                    var row = new UniformGrid { Columns = paper.Columns.Count };
                    for (var c = 0; c < paper.Columns.Count; c++)
                    {
                        var box = Box(field.Id + "." + c, field.Lines);
                        box.Margin = new Thickness(c == 0 ? 0 : 6, 3, 0, 8);
                        row.Children.Add(box);
                    }
                    stack.Children.Add(row);
                }
            }
            return stack;
        }

        /// <summary>Une zone multiligne : « lines » lignes au moins, pas de
        /// maximum — elle grandit avec le texte (Height auto, retour à la ligne).</summary>
        private TextBox Box(string id, int lines)
        {
            string current;
            var box = new TextBox
            {
                Text = _values.TryGetValue(id, out current) ? current : "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinLines = Math.Max(1, lines),
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 3, 0, 8),
                Tag = id
            };
            box.TextChanged += delegate
            {
                if (_loading || _item == null) return;
                if (box.Text.Length == 0) _values.Remove(id);
                else _values[id] = box.Text;
                RefreshProgress();
                if (_edited != null) _edited();
            };
            return box;
        }

        private void RefreshProgress()
        {
            if (_module == null || _item == null) return;
            var total = _module.ValueIds().Count;
            var filled = Modules.FilledCount(_module, _item);
            _progressText.Text = filled + " / " + total + " champs remplis"
                + (filled >= total && total > 0 ? " — fiche complète" : "");
            var track = _progressFill.Parent as Border;
            if (track != null && track.ActualWidth > 0)
                _progressFill.Width = total == 0 ? 0 : track.ActualWidth * filled / total;
        }
    }
}

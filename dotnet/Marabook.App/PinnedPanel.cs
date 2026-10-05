using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Model;

namespace Marabook.App
{
    /// <summary>L'ÉPINGLÉ SUR LE CÔTÉ (batch 47) : un écrit ou une fiche lu en
    /// MIROIR dans la colonne de droite pendant qu'on travaille ailleurs —
    /// la fiche du personnage sous les yeux en écrivant sa scène, le
    /// chapitre d'avant en écrivant le suivant. Lecture seule et simplifié :
    /// un écrit est rendu en colonne continue (FlowConverter, sans pages ni
    /// décor), une fiche dans sa vue wiki en colonne (SheetWiki). Le miroir
    /// suit les modifications de l'original (Refresh, appelé par la coquille
    /// après la frappe). Un seul épinglé à la fois ; « Ouvrir » y va,
    /// la croix retire l'épingle.</summary>
    public class PinnedPanel : DockPanel
    {
        private readonly TextBlock _title, _kind;
        // Le miroir d'un écrit (hotfix 1.0.3-a) : des blocs sélectionnables
        // en cache — seuls ceux dont le texte a changé sont rebâtis.
        private readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Control>> _mirrorBlocks
            = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Control>>();
        private StackPanel _mirrorStack;
        private Border _mirrorHost;
        private readonly ScrollViewer _scroller;
        private BinderItem _item;
        private SheetTemplate _template;
        private Project _project;
        private StyleSheet _styles;

        public event Action<BinderItem> OpenRequested;
        public event Action UnpinRequested;
        public event Action<BinderItem> NavigateRequested; // un lien de l'infobox (fiche liée, écrit)
        public event Action<string> LinkClicked;           // un [[lien]] du corps

        public BinderItem Item { get { return _item; } }

        public PinnedPanel()
        {
            Background = Chrome.BarBgLight;
            LastChildFill = true;

            var head = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
            SetDock(head, Dock.Top);
            var unpin = Buttons.Text("✕", "Retirer l'épingle", Buttons.Compact, Buttons.Look.Calm);
            unpin.Click += delegate { var h = UnpinRequested; if (h != null) h(); };
            DockPanel.SetDock(unpin, Dock.Right);
            head.Children.Add(unpin);
            var open = Buttons.Icon("arrow-up-right-bold", "Ouvrir l'original (l'épingle reste)", Buttons.Compact, Buttons.Look.Calm);
            open.Margin = new Thickness(0, 0, 4, 0);
            open.Click += delegate { var h = OpenRequested; if (h != null && _item != null) h(_item); };
            DockPanel.SetDock(open, Dock.Right);
            head.Children.Add(open);
            var pin = Icons.Make("push-pin-bold", 14, Chrome.Accent) as Control;
            if (pin != null)
            {
                pin.VerticalAlignment = VerticalAlignment.Center;
                pin.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(pin, Dock.Left);
                head.Children.Add(pin);
            }
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _title = new TextBlock
            {
                Foreground = Chrome.Ink,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titles.Children.Add(_title);
            _kind = new TextBlock { Foreground = Chrome.SoftText, FontSize = 11 };
            titles.Children.Add(_kind);
            head.Children.Add(titles);
            Children.Add(head);

            var chip = new Border
            {
                Background = Chrome.AccentTint,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(12, 0, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = "Miroir — lecture seule",
                    Foreground = Chrome.AccentStrong,
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold
                }
            };
            SetDock(chip, Dock.Top);
            Children.Add(chip);

            _scroller = new ScrollViewer
            {
                [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto,
                [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
                Padding = new Thickness(16, 0, 16, 14) // de l'air entre le papier et le rail (hotfix 1.0.3-a)
            };
            // La molette (14/09) : le FlowDocumentScrollViewer du contenu
            // avalait l'événement sans défiler (son propre ascenseur est
            // désactivé) — le panneau prend la molette en amont.
            _scroller.AddHandler(InputElement.PointerWheelChangedEvent, delegate(object sender, PointerWheelEventArgs e)
            {
                _scroller.Offset = new Vector(_scroller.Offset.X, _scroller.Offset.Y - Ui.Wheel(e));
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            // La largeur du miroir suit le VISEUR (hotfix 1.0.3-a) : quand
            // l'ascenseur vertical apparaît, le viseur rétrécit, mais un bloc
            // réutilisé gardait sa largeur mesurée d'avant et passait sous
            // l'ascenseur (texte rogné à droite). Largeurs posées, pas déduites.
            _scroller.ScrollChanged += delegate { FitMirror(); };
            Children.Add(_scroller);
        }

        private void FitMirror()
        {
            if (_mirrorHost == null || _mirrorStack == null) return;
            var viewport = _scroller.Viewport.Width;
            if (viewport <= 0) return;
            var inner = Math.Max(40, viewport - _scroller.Padding.Left - _scroller.Padding.Right);
            if (Math.Abs(_mirrorHost.Width - inner) < 0.5) return;
            _mirrorHost.HorizontalAlignment = HorizontalAlignment.Left;
            _mirrorHost.Width = inner;
            _mirrorStack.Width = Math.Max(20, inner - _mirrorHost.BorderThickness.Left - _mirrorHost.BorderThickness.Right
                - _mirrorStack.Margin.Left - _mirrorStack.Margin.Right);
        }

        /// <summary>Sonde (hotfix 1.0.3-a) : les largeurs disposées du miroir —
        /// panneau, viseur, cadre, pile, premier bloc — pour traquer un débordement.</summary>
        internal string MirrorLayoutReport(out bool fits)
        {
            fits = true;
            if (_mirrorHost == null || _mirrorStack == null) return "pas de miroir";
            var block = _mirrorStack.Children.Count > 0 ? _mirrorStack.Children[0] : null;
            var blockRight = block == null ? 0 : block.Bounds.Right + _mirrorStack.Margin.Left;
            fits = blockRight <= _mirrorHost.Bounds.Width - _mirrorStack.Margin.Right + 0.5
                && _mirrorHost.Bounds.Width <= _scroller.Viewport.Width + 0.5;
            return "panneau " + Bounds.Width.ToString("0") + ", viseur " + _scroller.Viewport.Width.ToString("0")
                + ", cadre " + _mirrorHost.Bounds.Width.ToString("0") + ", pile " + _mirrorStack.Bounds.Width.ToString("0")
                + ", bloc " + (block == null ? "-" : block.Bounds.Width.ToString("0") + " (droite " + blockRight.ToString("0") + ", voulu " + block.DesiredSize.Width.ToString("0") + ")");
        }

        public void SetProject(Project project, StyleSheet styles)
        {
            _project = project;
            _styles = styles;
        }

        /// <summary>Épingle cet item (null = rien) et le rend.</summary>
        public void Show(BinderItem item)
        {
            _item = item;
            _template = item != null && item.Kind == ItemKind.Sheet && _project != null
                ? _project.FindTemplate(item.TemplateId) : null;
            Refresh();
        }

        public void Clear()
        {
            _item = null;
            _template = null;
            _scroller.Content = null;
            _mirrorBlocks.Clear();
            if (_mirrorStack != null) _mirrorStack.Children.Clear();
            _title.Text = "";
            _kind.Text = "";
        }

        /// <summary>Rend (ou re-rend) l'épinglé depuis le modèle vivant — après
        /// une frappe dans l'original, un renommage, un changement de fiche.</summary>
        public void Refresh()
        {
            if (_item == null) { Clear(); return; }
            _title.Text = _item.Title;
            if (_item.Kind == ItemKind.Sheet)
            {
                var category = _project == null ? null : _project.SheetCategoryOf(_item);
                _kind.Text = category != null ? "Fiche " + category.Name : "Fiche";
                _scroller.Content = SheetWiki.Build(_item, _template, _project, _item.Document.ToPlainText(), true,
                    delegate(BinderItem target) { var h = NavigateRequested; if (h != null) h(target); },
                    delegate(string target) { var h = LinkClicked; if (h != null) h(target); },
                    null);
                return;
            }
            var book = _item.EnclosingBook();
            _kind.Text = book != null ? "Écrit — " + book.Title : "Écrit";
            // Colonne continue : marges du panneau, pas de page ; la police
            // du corps un cran plus petite que l'éditeur, on lit en marge.
            var flow = MirrorFlow(_item.Document);
            if (_mirrorHost == null)
                _mirrorHost = new Border
                {
                    Background = Chrome.PaperBg,
                    BorderBrush = Chrome.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Child = flow // directement : un ScrollViewer intermédiaire donnait aux blocs une largeur sans marge (texte rogné à droite)
                };
            if (!ReferenceEquals(_scroller.Content, _mirrorHost)) _scroller.Content = _mirrorHost;
            FitMirror();
        }

        /// <summary>Les blocs du miroir (hotfix 1.0.3-a) : par tranches de
        /// Ui.MirrorChunk paragraphes, un bloc dont l'empreinte n'a pas changé
        /// est gardé (sa sélection aussi), les autres sont rebâtis.</summary>
        private Control MirrorFlow(TextDocument document)
        {
            if (_mirrorStack == null) _mirrorStack = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
            var fresh = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Control>>();
            var count = Math.Max(1, document.Paragraphs.Count);
            for (var from = 0; from < count; from += Ui.MirrorChunk)
            {
                var to = Math.Min(count, from + Ui.MirrorChunk);
                var key = Ui.MirrorKey(document, from, to);
                Control block = null;
                foreach (var old in _mirrorBlocks)
                {
                    if (old.Key != key) continue;
                    var taken = false;
                    foreach (var f in fresh) if (ReferenceEquals(f.Value, old.Value)) { taken = true; break; }
                    if (!taken) { block = old.Value; break; }
                }
                if (block == null) block = Ui.MirrorBlock(document, from, to, 12.5, Chrome.PaperInk);
                fresh.Add(new System.Collections.Generic.KeyValuePair<string, Control>(key, block));
            }
            var same = fresh.Count == _mirrorStack.Children.Count;
            for (var i = 0; same && i < fresh.Count; i++) same = ReferenceEquals(_mirrorStack.Children[i], fresh[i].Value);
            if (!same)
            {
                _mirrorStack.Children.Clear();
                foreach (var pair in fresh) _mirrorStack.Children.Add(pair.Value);
            }
            _mirrorBlocks.Clear();
            _mirrorBlocks.AddRange(fresh);
            return _mirrorStack;
        }
    }
}

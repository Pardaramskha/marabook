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
                Padding = new Thickness(12, 0, 12, 12)
            };
            // La molette (14/09) : le FlowDocumentScrollViewer du contenu
            // avalait l'événement sans défiler (son propre ascenseur est
            // désactivé) — le panneau prend la molette en amont.
            _scroller.AddHandler(InputElement.PointerWheelChangedEvent, delegate(object sender, PointerWheelEventArgs e)
            {
                _scroller.Offset = new Vector(_scroller.Offset.X, _scroller.Offset.Y - Ui.Wheel(e));
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            Children.Add(_scroller);
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
            var flow = Ui.PlainDocument(_item.Document, 12.5, Chrome.PaperInk);
            _scroller.Content = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = new ScrollViewer { Content = flow,
                    [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled,
                    Focusable = false
                }
            };
        }
    }
}

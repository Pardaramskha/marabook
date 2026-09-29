using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Marabook.App
{
    /// <summary>L'AUTOCOMPLÉTION d'un champ éditable (29/09) : pendant la
    /// frappe, un menu déroulant sous le champ propose les candidats qui
    /// contiennent ce qui est tapé (accents et casse ignorés) ; flèches pour
    /// s'y déplacer, Entrée ou clic pour choisir, Échap pour fermer. Le
    /// ComboBox éditable d'Avalonia n'offre rien de tel : il attend le nom
    /// complet, orthographié juste. Polices, natures de relation, fiches
    /// cibles, cibles de liens passent par ici.</summary>
    public static class Suggestions
    {
        private const int MaxShown = 12;
        private static readonly Dictionary<Control, ListBox> _open = new Dictionary<Control, ListBox>();

        /// <summary>Un menu de suggestions est-il ouvert sous ce champ ? (Les
        /// flèches lui reviennent alors, pas au champ.)</summary>
        public static bool IsOpenFor(Control box)
        {
            return box != null && _open.ContainsKey(box);
        }

        /// <summary>Le menu ouvert sous ce champ a-t-il une ligne choisie
        /// (Entrée la prendra) ?</summary>
        public static bool HasChoice(Control box)
        {
            ListBox list;
            return box != null && _open.TryGetValue(box, out list) && list.SelectedItem != null;
        }

        /// <summary>Branche l'autocomplétion : candidates rend les textes
        /// possibles (relus à chaque frappe), pick reçoit le texte choisi.</summary>
        public static void Attach(ComboBox box, Func<IEnumerable<string>> candidates, Action<string> pick)
        {
            var list = new ListBox
            {
                Background = Chrome.PaperBg,
                BorderThickness = new Thickness(0),
                MaxHeight = 280,
                MinWidth = 160
            };
            var frame = new Border
            {
                Background = Chrome.PaperBg,
                BorderBrush = Chrome.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2),
                Child = list,
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 10, Color = Color.FromArgb(0x30, 0, 0, 0) })
            };
            var popup = new Popup
            {
                PlacementTarget = box,
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                IsLightDismissEnabled = false,
                Child = frame
            };
            ((ISetLogicalParent)popup).SetParent(box);

            var syncing = false;
            Action close = delegate
            {
                if (!popup.IsOpen) return;
                popup.IsOpen = false;
                _open.Remove(box);
            };
            Action<string> choose = delegate(string text)
            {
                close();
                syncing = true;
                try { pick(text); }
                finally { syncing = false; }
            };

            box.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property != ComboBox.TextProperty) return;
                // La liste déroulante du ComboBox ouverte : c'est elle qui
                // répond — le menu de suggestions couvrait ses premières
                // lignes (les polices favorites, injoignables — 29/09).
                if (box.IsDropDownOpen) { close(); return; }
                if (syncing || !box.IsKeyboardFocusWithin) { return; }
                var typed = (box.Text ?? "").Trim();
                if (typed.Length == 0) { close(); return; }
                var needle = Correction.FrenchTokenizer.Fold(typed);
                var matches = new List<string>();
                var exact = false;
                foreach (var candidate in candidates())
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    var folded = Correction.FrenchTokenizer.Fold(candidate);
                    if (folded == needle) { exact = true; continue; }
                    if (folded.Contains(needle)) matches.Add(candidate);
                    if (matches.Count >= MaxShown) break;
                }
                // Le nom exact déjà tapé, et rien d'autre : rien à proposer.
                if (matches.Count == 0) { close(); return; }
                if (exact) matches.Insert(0, typed);
                list.ItemsSource = matches;
                list.SelectedIndex = -1;
                list.MinWidth = Math.Max(160, box.Bounds.Width);
                if (!popup.IsOpen) { popup.IsOpen = true; _open[box] = list; }
            };

            // Un clic dans la liste choisit ; les flèches, Entrée et Échap
            // depuis le champ (en tunnel : avant les gestionnaires du champ).
            list.AddHandler(InputElement.PointerReleasedEvent, delegate(object sender, PointerReleasedEventArgs e)
            {
                var item = (e.Source as Visual).FindAncestorOfType<ListBoxItem>(true);
                if (item == null || item.Content == null) return;
                e.Handled = true;
                choose(item.Content.ToString());
            }, RoutingStrategies.Tunnel);
            box.AddHandler(InputElement.KeyDownEvent, delegate(object sender, KeyEventArgs e)
            {
                if (!popup.IsOpen) return;
                var count = list.ItemCount;
                if (e.Key == Key.Down) { list.SelectedIndex = Math.Min(count - 1, list.SelectedIndex + 1); e.Handled = true; }
                else if (e.Key == Key.Up) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
                else if (e.Key == Key.Escape) { close(); e.Handled = true; }
                else if (e.Key == Key.Enter || e.Key == Key.Tab)
                {
                    if (list.SelectedItem != null) { choose(list.SelectedItem.ToString()); e.Handled = e.Key == Key.Enter; }
                    else close();
                }
            }, RoutingStrategies.Tunnel);
            box.DropDownOpened += delegate { close(); }; // la liste du ComboBox prend la place
            box.LostFocus += delegate
            {
                // Le focus part vers la liste (clic) : on la laisse répondre.
                Avalonia.Threading.Dispatcher.UIThread.Post(delegate
                {
                    if (!box.IsKeyboardFocusWithin && !list.IsKeyboardFocusWithin) close();
                }, Avalonia.Threading.DispatcherPriority.Background);
            };
            box.DetachedFromVisualTree += delegate { close(); };
        }
    }
}

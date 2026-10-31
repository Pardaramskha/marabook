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
    /// cibles, cibles de liens passent par ici. Le mode STRICT (31/10, Rémi)
    /// vaut pour un champ qui n'admet pas de texte libre (polices, amplitude
    /// des Présences) : la frappe qui ne répond à aucun candidat passe en
    /// rouge, et Entrée prend le premier candidat proposé.</summary>
    public static class Suggestions
    {
        private const int MaxShown = 12;
        private static readonly Dictionary<Control, ListBox> _open = new Dictionary<Control, ListBox>();
        /// <summary>Sonde : Focus() ne prend pas sans fenêtre vraiment active ;
        /// vrai, le champ est réputé tenir le clavier.</summary>
        public static bool AssumeFocusForProbe;

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
            Attach(box, candidates, pick, false);
        }

        /// <summary>Le champ tapé répond-il à au moins un candidat (contenu,
        /// accents et casse ignorés) ? Vide : oui.</summary>
        public static bool Matches(string typed, IEnumerable<string> candidates)
        {
            typed = (typed ?? "").Trim();
            if (typed.Length == 0) return true;
            var needle = Correction.FrenchTokenizer.Fold(typed);
            foreach (var candidate in candidates)
                if (!string.IsNullOrEmpty(candidate) && Correction.FrenchTokenizer.Fold(candidate).Contains(needle)) return true;
            return false;
        }

        /// <summary>Teinte le texte d'un champ éditable : rouge quand il ne
        /// répond à rien (strict), sinon la couleur du thème.</summary>
        private static void Tint(ComboBox box, bool wrong)
        {
            if (wrong) box.Foreground = Chrome.Danger; else box.ClearValue(TemplatedControl.ForegroundProperty);
            foreach (var inner in box.GetVisualDescendants())
            {
                var textBox = inner as TextBox;
                if (textBox == null) continue;
                if (wrong) textBox.Foreground = Chrome.Danger; else textBox.ClearValue(TemplatedControl.ForegroundProperty);
            }
        }

        public static void Attach(ComboBox box, Func<IEnumerable<string>> candidates, Action<string> pick, bool strict)
        {
            var list = new ListBox
            {
                Background = Chrome.FieldBg,
                BorderThickness = new Thickness(0),
                MaxHeight = 280,
                MinWidth = 160
            };
            var frame = new Border
            {
                Background = Chrome.FieldBg,
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
            // La frappe de l'utilisateur, reconnue aussi par les touches (31/10,
            // Rémi : le champ des tags ne proposait rien) — le focus clavier
            // d'un ComboBox éditable n'est pas toujours « dedans » au moment où
            // Text change.
            var lastKey = DateTime.MinValue;
            box.AddHandler(InputElement.KeyDownEvent, delegate(object sender, KeyEventArgs e) { lastKey = DateTime.UtcNow; }, RoutingStrategies.Tunnel, true);
            box.AddHandler(InputElement.TextInputEvent, delegate(object sender, TextInputEventArgs e) { lastKey = DateTime.UtcNow; }, RoutingStrategies.Tunnel, true);
            Func<bool> typing = delegate { return box.IsKeyboardFocusWithin || AssumeFocusForProbe || (DateTime.UtcNow - lastKey).TotalMilliseconds < 400; };
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
                // Strict : la frappe sans réponse passe en rouge, quoi qu'il
                // arrive au menu (texte posé par le code compris : il redevient noir).
                if (strict) Tint(box, !syncing && typing() && !Matches(box.Text, candidates()));
                // La liste déroulante du ComboBox ouverte : c'est elle qui
                // répond — le menu de suggestions couvrait ses premières
                // lignes (les polices favorites, injoignables — 29/09).
                if (box.IsDropDownOpen) { close(); return; }
                if (syncing || !typing()) { return; }
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
                    // Strict : Entrée sans ligne choisie prend la première proposée (31/10, Rémi).
                    else if (strict && e.Key == Key.Enter && count > 0) { choose(list.Items[0].ToString()); e.Handled = true; }
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
            // CLIC AILLEURS (31/10, Rémi) : un clic sur une surface qui ne prend
            // pas le clavier (fond de l'app, marge, étiquette) laissait le caret
            // dans le champ. La fenêtre écoute les pressions en tunnel : hors du
            // champ et de son menu, le clavier est rendu (LostFocus commet).
            EventHandler<PointerPressedEventArgs> clickOut = delegate(object sender, PointerPressedEventArgs e)
            {
                if (!box.IsKeyboardFocusWithin) return;
                var source = e.Source as Visual;
                if (source != null && (IsWithin(source, box) || IsWithin(source, list))) return;
                var top = TopLevel.GetTopLevel(box);
                if (top != null && top.FocusManager != null) top.FocusManager.ClearFocus();
            };
            TopLevel listening = null;
            box.AttachedToVisualTree += delegate
            {
                listening = TopLevel.GetTopLevel(box);
                if (listening != null) listening.AddHandler(InputElement.PointerPressedEvent, clickOut, RoutingStrategies.Tunnel, true);
            };
            box.DetachedFromVisualTree += delegate
            {
                close();
                if (listening != null) { listening.RemoveHandler(InputElement.PointerPressedEvent, clickOut); listening = null; }
            };
            // PREMIER CLIC (31/10, Rémi) : le clavier entre dans le champ, tout
            // le texte est choisi — on tape une valeur sans Ctrl+A. Posé après
            // la pression, qui aurait posé le caret au point cliqué.
            TextBox selectOnRelease = null;
            Action<TextBox> selectAll = delegate(TextBox inner)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(delegate
                {
                    if (inner.IsKeyboardFocusWithin && !string.IsNullOrEmpty(inner.Text)) inner.SelectAll();
                }, Avalonia.Threading.DispatcherPriority.Input);
            };
            box.GotFocus += delegate(object sender, GotFocusEventArgs e)
            {
                var inner = e.Source as TextBox;
                if (inner == null || !IsWithin(inner, box)) return;
                // Par la souris : au relâchement (le glisser de la pression
                // reposerait la sélection) ; au clavier (Tab) : tout de suite.
                if (e.NavigationMethod == NavigationMethod.Pointer) selectOnRelease = inner;
                else selectAll(inner);
            };
            box.AddHandler(InputElement.PointerReleasedEvent, delegate(object sender, PointerReleasedEventArgs e)
            {
                var inner = selectOnRelease;
                selectOnRelease = null;
                if (inner != null) selectAll(inner);
            }, RoutingStrategies.Bubble, true);
        }

        /// <summary>Le visuel est-il l'ancêtre, ou l'un de ses descendants ?</summary>
        private static bool IsWithin(Visual visual, Visual ancestor)
        {
            while (visual != null)
            {
                if (ReferenceEquals(visual, ancestor)) return true;
                visual = visual.GetVisualParent();
            }
            return false;
        }
    }
}

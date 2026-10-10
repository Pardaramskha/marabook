using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Marabook.Model;
using Avalonia.Media;
using Avalonia.Animation.Easings;
using Avalonia.Animation;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Marabook.App
{
    /// <summary>Les petits services que WPF donnait sur chaque élément et
    /// qu'Avalonia met ailleurs (P2) : le répartiteur, la fenêtre
    /// propriétaire, le presse-papiers, le clavier dans un sous-arbre. Les
    /// vues portées les appellent à la place de Dispatcher.BeginInvoke,
    /// Window.GetWindow, Clipboard.SetText et IsKeyboardFocusWithin.</summary>
    public static class Ui
    {
        /// <summary>Une zone de texte avec sa CROIX (09/10) : un « × » à droite,
        /// visible dès qu'il y a du texte, qui vide le champ et lui rend le
        /// clavier — champs de recherche de la Pile, du rail et de Ctrl+F.</summary>
        public static Control WithClear(TextBox box, string tip = "Effacer")
        {
            var host = new Panel();
            host.Children.Add(box);
            var cross = new TextBlock
            {
                Text = "×",
                FontSize = 15,
                Foreground = Chrome.SoftText,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, box.Margin.Right + 7, 1),
                Cursor = new Cursor(StandardCursorType.Hand),
                IsVisible = !string.IsNullOrEmpty(box.Text),
                [ToolTip.TipProperty] = tip
            };
            cross.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(cross).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                box.Text = "";
                box.Focus();
            };
            box.TextChanged += delegate { cross.IsVisible = !string.IsNullOrEmpty(box.Text); };
            box.Padding = new Thickness(box.Padding.Left, box.Padding.Top, Math.Max(box.Padding.Right, 22), box.Padding.Bottom);
            host.Children.Add(cross);
            return host;
        }

        public static void Post(DispatcherPriority priority, Action action)
        {
            Dispatcher.UIThread.Post(action, priority);
        }

        // LA TOUCHE DE COMMANDE (02/10) : Ctrl sur Windows et Linux, ⌘ sur
        // macOS — où Ctrl ne porte presque rien (Ctrl+clic y est le clic
        // droit). Les réglages gardent « Ctrl+S » sur les trois systèmes ;
        // c'est ici, au bord de la vue, que « Ctrl » devient la touche du
        // système : Geo.ToCore/ToAvalonia pour les gestes, HasCommand pour
        // les tests directs des vues, CommandKey et Keys pour les textes.
        public static readonly KeyModifiers Command = AppPlatform.IsMac ? KeyModifiers.Meta : KeyModifiers.Control;

        /// <summary>La touche de commande du système est-elle enfoncée ?</summary>
        public static bool HasCommand(KeyModifiers modifiers)
        {
            return (modifiers & Command) != 0;
        }

        /// <summary>Le nom de la touche de commande dans un texte : « ⌘ » ou « Ctrl »
        /// (« Ctrl+clic », « ⌘+molette »).</summary>
        public static readonly string CommandKey = AppPlatform.IsMac ? "⌘" : "Ctrl";

        /// <summary>Un geste au format des réglages (« Ctrl+Shift+N ») tel qu'il
        /// se lit sur ce système : « Ctrl+Maj+N », ou « ⇧⌘N » sur macOS.</summary>
        public static string Keys(string gesture)
        {
            return Settings.AppSettings.DisplayGesture(gesture);
        }

        // UN SEUL MENU CONTEXTUEL À LA FOIS (29/09) : un clic droit dans
        // l'éditeur pouvait ouvrir un second menu sans fermer le premier,
        // qui restait alors planté à l'écran, sans moyen de le fermer. Tout
        // menu ouvert à la main passe ici : le précédent se ferme d'abord.
        private static ContextMenu _openMenu;

        public static void ShowMenu(ContextMenu menu, Control target)
        {
            if (menu == null) return;
            var previous = _openMenu;
            if (previous != null && previous != menu && previous.IsOpen) previous.Close();
            _openMenu = menu;
            EventHandler<Avalonia.Interactivity.RoutedEventArgs> closed = null;
            closed = delegate
            {
                menu.Closed -= closed;
                if (_openMenu == menu) _openMenu = null;
            };
            menu.Closed += closed;
            menu.Open(target);
        }

        /// <summary>LE menu du clic droit des zones de texte ordinaires (10/10,
        /// Rémi : « unifier avec le reste ») — Avalonia en pose un en anglais,
        /// aux angles droits, sur chaque TextBox. Celui-ci, en français et
        /// dans le thème (coins ronds, voir Theme), est partagé par toutes les
        /// zones par un style ; la cible du moment est celle qui l'ouvre.</summary>
        public static MenuFlyout BuildTextBoxFlyout()
        {
            var flyout = new MenuFlyout();
            var cut = new MenuItem { Header = "Couper", InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) };
            var copy = new MenuItem { Header = "Copier", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
            var paste = new MenuItem { Header = "Coller", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
            var all = new MenuItem { Header = "Tout sélectionner", InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) };
            flyout.Items.Add(cut);
            flyout.Items.Add(copy);
            flyout.Items.Add(paste);
            flyout.Items.Add(all);
            Func<TextBox> target = delegate { return flyout.Target as TextBox; };
            flyout.Opening += delegate
            {
                var box = target();
                var hasSelection = box != null && box.SelectionStart != box.SelectionEnd;
                cut.IsEnabled = hasSelection && box != null && !box.IsReadOnly;
                copy.IsEnabled = hasSelection;
                paste.IsEnabled = box != null && !box.IsReadOnly;
                all.IsEnabled = box != null && (box.Text ?? "").Length > 0;
            };
            cut.Click += delegate { var box = target(); if (box != null) box.Cut(); };
            copy.Click += delegate { var box = target(); if (box != null) box.Copy(); };
            paste.Click += delegate { var box = target(); if (box != null) box.Paste(); };
            all.Click += delegate { var box = target(); if (box != null) { box.Focus(); box.SelectAll(); } };
            return flyout;
        }

        /// <summary>Ferme le menu contextuel ouvert à la main, s'il y en a un.</summary>
        public static void CloseOpenMenu()
        {
            var menu = _openMenu;
            if (menu != null && menu.IsOpen) menu.Close();
        }

        /// <summary>La fenêtre qui contient ce visuel, sinon la principale.</summary>
        public static Window OwnerOf(Visual visual)
        {
            var window = visual == null ? null : TopLevel.GetTopLevel(visual) as Window;
            return window ?? App.MainWindowOrNull;
        }

        /// <summary>Écrit dans le presse-papiers sans attendre.</summary>
        public static void SetClipboardText(Visual visual, string text)
        {
            var top = visual == null ? null : TopLevel.GetTopLevel(visual);
            if (top == null || top.Clipboard == null) return;
            try { var _ = top.Clipboard.SetTextAsync(text ?? ""); } catch { }
        }

        public static async Task<string> ClipboardText(Visual visual)
        {
            var top = visual == null ? null : TopLevel.GetTopLevel(visual);
            if (top == null || top.Clipboard == null) return null;
            try { return await top.Clipboard.GetTextAsync(); }
            catch { return null; }
        }

        /// <summary>Les types d'un filtre WPF (« Libellé (*.a;*.b)|*.a;*.b|… »)
        /// pour les sélecteurs de fichiers d'Avalonia.</summary>
        public static System.Collections.Generic.List<FilePickerFileType> FileTypes(string wpfFilter)
        {
            var types = new System.Collections.Generic.List<FilePickerFileType>();
            if (string.IsNullOrEmpty(wpfFilter)) return types;
            var parts = wpfFilter.Split('|');
            for (var i = 0; i + 1 < parts.Length; i += 2)
            {
                var patterns = parts[i + 1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                types.Add(new FilePickerFileType(parts[i]) { Patterns = patterns });
            }
            return types;
        }

        /// <summary>Le fichier choisi à l'ouverture, ou null.</summary>
        public static async Task<string> PickOpenFile(Visual visual, string title, string wpfFilter)
        {
            var top = visual == null ? null : TopLevel.GetTopLevel(visual);
            if (top == null) return null;
            var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = FileTypes(wpfFilter)
            });
            return picked == null || picked.Count == 0 ? null : picked[0].TryGetLocalPath();
        }

        /// <summary>Le fichier choisi à l'enregistrement, ou null.</summary>
        public static async Task<string> PickSaveFile(Visual visual, string title, string wpfFilter, string suggestedName)
        {
            var top = visual == null ? null : TopLevel.GetTopLevel(visual);
            if (top == null) return null;
            var types = FileTypes(wpfFilter);
            var extension = System.IO.Path.GetExtension(suggestedName ?? "");
            var picked = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                DefaultExtension = extension.Length > 1 ? extension.Substring(1) : null,
                FileTypeChoices = types
            });
            return picked == null ? null : picked.TryGetLocalPath();
        }

        /// <summary>Plusieurs fichiers à l'ouverture, ou null.</summary>
        public static async Task<string[]> PickOpenFiles(Visual visual, string title, string wpfFilter)
        {
            var top = visual == null ? null : TopLevel.GetTopLevel(visual);
            if (top == null) return null;
            var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = true,
                FileTypeFilter = FileTypes(wpfFilter)
            });
            if (picked == null) return null;
            var paths = new System.Collections.Generic.List<string>();
            foreach (var item in picked)
            {
                var path = item.TryGetLocalPath();
                if (path != null) paths.Add(path);
            }
            return paths.ToArray();
        }

        /// <summary>Un objet de données d'un seul format (le new DataObject(format, value) de WPF).</summary>
        public static DataObject DataOf(string format, object value)
        {
            var data = new DataObject();
            data.Set(format, value);
            return data;
        }

        /// <summary>Les chemins locaux des fichiers déposés, ou null.</summary>
        public static string[] DroppedPaths(IDataObject data)
        {
            if (data == null) return null;
            var items = data.GetFiles();
            if (items == null) return null;
            var paths = new System.Collections.Generic.List<string>();
            foreach (var item in items)
            {
                var path = item.TryGetLocalPath();
                if (path != null) paths.Add(path);
            }
            return paths.Count == 0 ? null : paths.ToArray();
        }

        /// <summary>Sélectionne length caractères à partir de start (TextBox.Select de WPF).</summary>
        public static void Select(TextBox box, int start, int length)
        {
            var text = box.Text ?? "";
            start = Math.Max(0, Math.Min(text.Length, start));
            var end = Math.Max(start, Math.Min(text.Length, start + length));
            // Le caret D'ABORD (07/10) : sur Avalonia, poser CaretIndex replie
            // la sélection sur lui — posé en dernier, il effaçait la plage
            // qu'on venait d'étendre (la recherche d'une fiche ne surlignait
            // pas, le dialogue du lien ne voyait pas l'expression choisie).
            box.CaretIndex = end;
            box.SelectionStart = start;
            box.SelectionEnd = end;
        }

        /// <summary>IsVisibleChanged de WPF : la propriété IsVisible observée.</summary>
        public static void OnVisibilityChanged(Visual control, Action handler)
        {
            control.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Visual.IsVisibleProperty) handler();
            };
        }

        /// <summary>SizeChanged de WPF (sans arguments) : les bornes observées.</summary>
        public static void OnSizeChanged(Visual control, Action handler)
        {
            control.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Visual.BoundsProperty) handler();
            };
        }

        /// <summary>La molette en pixels signés comme WPF la donnait (120 par
        /// cran) : Avalonia compte en crans (Delta.Y = ±1).</summary>
        public static double Wheel(PointerWheelEventArgs e)
        {
            return e.Delta.Y * 120;
        }

        /// <summary>Le toast glisse et apparaît (les DoubleAnimation de WPF) :
        /// une translation depuis (fromX, fromY) et l'opacité, en transitions.</summary>
        public static void SlideIn(Control toast, double fromX, double fromY, int milliseconds)
        {
            var translate = new TranslateTransform(fromX, fromY);
            toast.RenderTransform = translate;
            toast.Opacity = 0;
            translate.Transitions = new Transitions
            {
                new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(milliseconds), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(milliseconds), Easing = new CubicEaseOut() }
            };
            toast.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(Math.Min(milliseconds, 240)) }
            };
            Dispatcher.UIThread.Post(delegate
            {
                translate.X = 0;
                translate.Y = 0;
                toast.Opacity = 1;
            }, DispatcherPriority.Background);
        }

        /// <summary>Le toast s'efface après afterSeconds, en milliseconds, puis completed.</summary>
        public static void FadeOutLater(Control toast, double afterSeconds, int milliseconds, Action completed)
        {
            var wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.05, afterSeconds)) };
            wait.Tick += delegate
            {
                wait.Stop();
                toast.Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(milliseconds) }
                };
                toast.Opacity = 0;
                var end = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds + 30) };
                end.Tick += delegate { end.Stop(); if (completed != null) completed(); };
                end.Start();
            };
            wait.Start();
        }

        /// <summary>Un dégradé vertical de deux couleurs (le LinearGradientBrush(c1, c2, 90) de WPF).</summary>
        public static IBrush VerticalGradient(Color top, Color bottom)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
            };
            brush.GradientStops.Add(new GradientStop(top, 0));
            brush.GradientStops.Add(new GradientStop(bottom, 1));
            return brush;
        }

        /// <summary>La ligne (retours à la ligne comptés) d'un indice du texte.</summary>
        public static int LineOf(TextBox box, int index)
        {
            var text = box.Text ?? "";
            var line = 0;
            for (var i = 0; i < index && i < text.Length; i++) if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>ScrollToLine de WPF : le TextBox d'Avalonia met son caret en vue de lui-même.</summary>
        public static void ScrollToLine(TextBox box, int line)
        {
        }

        /// <summary>Un document du pivot en colonne de lecture nue : un
        /// TextBlock par paragraphe (le FlowDocument du panneau épinglé).</summary>
        /// <summary>Le miroir d'un écrit (hotfix 1.0.3-a) : des BLOCS
        /// sélectionnables de MirrorChunk paragraphes — on peut tirer une
        /// sélection d'un paragraphe à l'autre et la copier (Ctrl+C, clic
        /// droit › Copier), le miroir reste en lecture seule. Un seul bloc
        /// pour tout l'écrit se recomposait en entier à chaque pause de frappe
        /// (l'application ralentissait) : PinnedPanel ne rebâtit que les blocs
        /// dont le texte a changé (MirrorKey).</summary>
        public const int MirrorChunk = 24;

        /// <summary>L'empreinte d'une tranche de paragraphes : texte, gras et
        /// italique — ce que le bloc montre.</summary>
        public static string MirrorKey(TextDocument document, int from, int to)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = from; i < to && i < document.Paragraphs.Count; i++)
            {
                foreach (var run in document.Paragraphs[i].Runs)
                {
                    if (run.IsLineBreak) { sb.Append('\u0003'); continue; }
                    if (PivotEdit.IsElement(run)) continue;
                    if (run.Bold == true) sb.Append('\u0001');
                    if (run.Italic == true) sb.Append('\u0002');
                    sb.Append(run.Text);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Un bloc du miroir : les paragraphes [from, to), séparés par
        /// un demi-interligne (une ligne de zéro-largeur à demi-corps), le gras
        /// et l'italique des runs gardés ; surlignage d'accent translucide,
        /// l'encre inchangée (lisible en clair comme en sombre).</summary>
        public static SelectableTextBlock MirrorBlock(TextDocument document, int from, int to, double fontSize, IBrush ink)
        {
            var block = new SelectableTextBlock
            {
                FontSize = fontSize,
                Foreground = ink,
                TextWrapping = TextWrapping.Wrap,
                SelectionBrush = new SolidColorBrush(Chrome.Accent.Color) { Opacity = 0.45 },
                SelectionForegroundBrush = ink,
                Cursor = new Cursor(StandardCursorType.Ibeam),
                Margin = new Thickness(0, 0, 0, fontSize * 0.55)
            };
            var first = true;
            for (var i = from; i < to && i < document.Paragraphs.Count; i++)
            {
                var paragraph = document.Paragraphs[i];
                if (!first)
                {
                    block.Inlines.Add(new LineBreak());
                    block.Inlines.Add(new Run("\u200B") { FontSize = fontSize * 0.55 });
                    block.Inlines.Add(new LineBreak());
                }
                first = false;
                foreach (var run in paragraph.Runs)
                {
                    if (run.IsLineBreak) { block.Inlines.Add(new LineBreak()); continue; }
                    if (PivotEdit.IsElement(run) || string.IsNullOrEmpty(run.Text)) continue;
                    var inline = new Run(run.Text);
                    if (run.Bold == true) inline.FontWeight = FontWeight.Bold;
                    if (run.Italic == true) inline.FontStyle = FontStyle.Italic;
                    block.Inlines.Add(inline);
                }
            }
            var copy = new MenuItem { Header = "Copier", InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) };
            copy.Click += delegate { block.Copy(); };
            block.ContextMenu = new ContextMenu { Items = { copy } };
            return block;
        }

        /// <summary>Tout l'écrit en blocs, sans cache (sonde, usages ponctuels).</summary>
        public static Control PlainDocument(TextDocument document, double fontSize, IBrush ink)
        {
            var stack = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };
            if (document == null) return stack;
            var count = Math.Max(1, document.Paragraphs.Count);
            for (var from = 0; from < count; from += MirrorChunk)
                stack.Children.Add(MirrorBlock(document, from, Math.Min(count, from + MirrorChunk), fontSize, ink));
            return stack;
        }

        /// <summary>Le clavier est-il dans ce sous-arbre (IsKeyboardFocusWithin) ?</summary>
        public static bool FocusWithin(Visual host)
        {
            var top = host == null ? null : TopLevel.GetTopLevel(host);
            var focused = top == null || top.FocusManager == null ? null : top.FocusManager.GetFocusedElement() as Visual;
            while (focused != null)
            {
                if (ReferenceEquals(focused, host)) return true;
                focused = focused.GetVisualParent();
            }
            return false;
        }
    }
}

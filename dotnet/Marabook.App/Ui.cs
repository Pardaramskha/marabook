using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
        public static void Post(DispatcherPriority priority, Action action)
        {
            Dispatcher.UIThread.Post(action, priority);
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
            box.SelectionStart = start;
            box.SelectionEnd = end;
            box.CaretIndex = end;
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
        public static Control PlainDocument(TextDocument document, double fontSize, IBrush ink)
        {
            var stack = new StackPanel { Margin = new Thickness(14, 12, 14, 16) };
            if (document == null) return stack;
            foreach (var paragraph in document.Paragraphs)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = paragraph.ToPlainText(),
                    FontSize = fontSize,
                    Foreground = ink,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = paragraph.AlignOverride == "center" ? TextAlignment.Center
                        : paragraph.AlignOverride == "right" ? TextAlignment.Right : TextAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }
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

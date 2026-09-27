using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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

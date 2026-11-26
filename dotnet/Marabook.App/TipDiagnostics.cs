using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Marabook.App
{
    /// <summary>LE JOURNAL DES INFOBULLES (26/11/2026) — un outil de diagnostic, éteint d'office : avec la
    /// variable d'environnement MARABOOK_BULLES=&lt;fichier&gt;, l'application y note chaque ouverture et
    /// chaque fermeture d'infobulle (avec ce qui l'a provoquée), et les entrées / sorties du pointeur
    /// dans ses fenêtres. Pour comprendre une bulle qui clignote sur une vraie souris, ce qu'aucune
    /// sonde ne reproduit. Rien n'est écrit, ni écouté, sans la variable.</summary>
    internal static class TipDiagnostics
    {
        private static string _file;
        private static DateTime _started;
        private static int _moves;

        public static void Install()
        {
            _file = Environment.GetEnvironmentVariable("MARABOOK_BULLES");
            if (string.IsNullOrEmpty(_file)) return;
            _started = DateTime.Now;
            Write("— journal des infobulles, " + _started.ToString("HH:mm:ss") + " ; skin " + Skin.Current);
            ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>(delegate(Control control, AvaloniaPropertyChangedEventArgs e)
            {
                var top = TopLevel.GetTopLevel(control);
                Write("BULLE " + (true.Equals(e.NewValue) ? "OUVERTE" : "FERMÉE ") + " sur " + control.GetType().Name + " [" + control.Tag + "] « " + Short(ToolTip.GetTip(control)) + " » dans " + (top == null ? "-" : top.GetType().Name)
                    + " ; skin " + Skin.Current + " ; déplacements depuis la dernière ligne : " + _moves);
                _moves = 0;
                foreach (var line in Environment.StackTrace.Split('\n').Skip(3).Where(delegate(string each) { return each.Contains("Avalonia") || each.Contains("Marabook"); }).Take(9)) Write("        " + line.Trim());
            });
            InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(delegate(TopLevel top, PointerEventArgs e) { _moves++; }, RoutingStrategies.Bubble, true);
            InputElement.PointerExitedEvent.AddClassHandler<TopLevel>(delegate(TopLevel top, PointerEventArgs e) { if (ReferenceEquals(e.Source, top)) Write("SORTIE du pointeur de " + top.GetType().Name + " (déplacements : " + _moves + ")"); }, RoutingStrategies.Direct, true);
            InputElement.PointerEnteredEvent.AddClassHandler<TopLevel>(delegate(TopLevel top, PointerEventArgs e) { if (ReferenceEquals(e.Source, top)) Write("ENTRÉE du pointeur dans " + top.GetType().Name); }, RoutingStrategies.Direct, true);
            // Les événements BRUTS de la souris, tels que le service des infobulles les reçoit (ce type d'Avalonia
            // n'est pas public : par réflexion). On note chaque changement de fenêtre ou de sorte, et ce que le
            // pointeur survole alors — c'est de là que le service décide d'ouvrir, d'attendre ou de fermer.
            try
            {
                var manager = typeof(InputElement).Assembly.GetType("Avalonia.Input.InputManager");
                var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var instance = manager == null ? null : manager.GetProperty("Instance", flags).GetValue(null);
                var process = instance == null ? null : manager.GetProperty("Process", flags).GetValue(instance) as IObservable<Avalonia.Input.Raw.RawInputEventArgs>;
                if (process != null) process.Subscribe(new RawWatch());
                else Write("(événements bruts inaccessibles)");
            }
            catch (Exception error) { Write("(événements bruts : " + error.Message + ")"); }
            // LA SANTÉ DES MINUTERIES : une infobulle s'ouvre par une minuterie en priorité d'arrière-plan. Deux
            // témoins battent quatre fois par seconde, l'un en arrière-plan, l'autre en priorité de rendu : s'ils ne
            // comptent pas pareil, c'est que les tâches d'arrière-plan ne passent plus.
            var background = 0; var render = 0;
            new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250), Avalonia.Threading.DispatcherPriority.Background, delegate { background++; }).Start();
            new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(250), Avalonia.Threading.DispatcherPriority.Render, delegate { render++; }).Start();
            new Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(2), Avalonia.Threading.DispatcherPriority.Render, delegate
            {
                if (background < 6 || render < 6) Write("SANTÉ en 2 s : arrière-plan " + background + " battements, rendu " + render + " (8 attendus)");
                background = render = 0;
            }).Start();
            Window.WindowOpenedEvent.AddClassHandler<Window>(delegate(Window window, RoutedEventArgs e) { Write("FENÊTRE ouverte : " + window.GetType().Name); });
            Window.WindowClosedEvent.AddClassHandler<Window>(delegate(Window window, RoutedEventArgs e) { Write("FENÊTRE fermée : " + window.GetType().Name); });
        }

        private sealed class RawWatch : IObserver<Avalonia.Input.Raw.RawInputEventArgs>
        {
            private string _last = "";
            private int _same;
            public void OnCompleted() { }
            public void OnError(Exception error) { }
            public void OnNext(Avalonia.Input.Raw.RawInputEventArgs value)
            {
                var pointer = value as Avalonia.Input.Raw.RawPointerEventArgs;
                if (pointer == null) return;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                object root = null, hit = null, kind = null;
                try
                {
                    kind = typeof(Avalonia.Input.Raw.RawPointerEventArgs).GetProperty("Type", flags).GetValue(pointer);
                    root = typeof(Avalonia.Input.Raw.RawInputEventArgs).GetProperty("Root", flags).GetValue(pointer);
                    var result = typeof(Avalonia.Input.Raw.RawPointerEventArgs).GetProperty("InputHitTestResult", flags).GetValue(pointer);
                    if (result != null) { var field = result.GetType().GetField("Item1") ?? result.GetType().GetField("element"); if (field != null) hit = field.GetValue(result); }
                }
                catch (Exception) { }
                // Le contrôle à infobulle que ce déplacement désigne (le plus proche ancêtre qui en porte une).
                string host = "aucune";
                for (var visual = hit as Visual; visual != null; visual = Avalonia.VisualTree.VisualExtensions.GetVisualParent(visual))
                {
                    var control = visual as Control;
                    if (control != null && ToolTip.GetTip(control) != null) { host = control.GetType().Name + "[" + control.Tag + "]"; break; }
                }
                var line = kind + " → " + (root == null ? "-" : root.GetType().Name) + " ; sous le pointeur : " + (hit == null ? "rien" : hit.GetType().Name) + " ; infobulle visée : " + host;
                if (line == _last) { _same++; return; }
                if (_same > 0) Write("        (× " + (_same + 1) + ")");
                _same = 0;
                _last = line;
                Write("BRUT  " + line);
            }
        }

        private static string Short(object tip)
        {
            var text = tip as string ?? (tip == null ? "" : tip.GetType().Name);
            text = text.Replace("\n", " ");
            return text.Length > 40 ? text.Substring(0, 40) + "…" : text;
        }

        private static void Write(string line)
        {
            try { File.AppendAllText(_file, ((DateTime.Now - _started).TotalMilliseconds).ToString("0").PadLeft(7) + " ms  " + line + "\n"); }
            catch (Exception) { }
        }
    }
}

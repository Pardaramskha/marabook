using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>BANC D'ESSAI DES INFOBULLES (26/11/2026) — à jouer AVEC de vraies fenêtres
    /// (MARABOOK_PROBE_ONLY=bulles, --visible) : sans écran, une bulle est posée dans la fenêtre de
    /// l'application et ne passe par aucune fenêtre du système. On survole (événements bruts injectés
    /// dans la fenêtre, comme le ferait la souris) un contrôle à infobulle des Préférences, et l'on
    /// regarde la bulle s'ouvrir et RESTER ouverte.</summary>
    public static partial class Probes
    {
        private static async Task ProbeTips(MainWindow shell)
        {
            Console.WriteLine("  — Les infobulles des Préférences (vraies fenêtres)");
            var log = Environment.GetEnvironmentVariable("MB_TIP_LOG") == "1";
            var started = DateTime.Now;
            Func<string> at = delegate { return ((DateTime.Now - started).TotalMilliseconds).ToString("0").PadLeft(5) + " ms"; };
            var opens = 0; var closes = 0;
            ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>(delegate(Control control, AvaloniaPropertyChangedEventArgs e)
            {
                if (true.Equals(e.NewValue)) opens++; else closes++;
                if (!log) return;
                Console.WriteLine("TIP " + at() + " IsOpen=" + e.NewValue + " sur " + control.GetType().Name + " [" + control.Tag + "]");
                if (!true.Equals(e.NewValue))
                    foreach (var line in Environment.StackTrace.Split('\n').Skip(3).Take(14)) Console.WriteLine("        " + line.Trim());
            });
            var seen = 0; // les déplacements de souris arrivés aux fenêtres : les miens, et ceux de la vraie souris s'il y en a
            InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(delegate(TopLevel top, PointerEventArgs e) { seen++; }, Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            // LE CAS DE RÉMI (26/11) : la souris était sur l'engrenage du pied de Pile quand les Préférences se sont
            // ouvertes — la fenêtre principale se croit toujours survolée là, et se repeint de temps en temps.
            var gear = shell.Binder.GetVisualDescendants().OfType<Border>().FirstOrDefault(delegate(Border border) { return "binder-preferences".Equals(border.Tag); });
            var mainMouse = NewMouse();
            if (gear != null)
            {
                var onGear = gear.TranslatePoint(new Point(gear.Bounds.Width / 2, gear.Bounds.Height / 2), shell);
                if (onGear.HasValue) for (var i = 0; i < 3; i++) { RawMove(shell, mainMouse, new Point(onGear.Value.X + i, onGear.Value.Y)); await Task.Delay(30); }
            }
            var repaint = new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(120), Avalonia.Threading.DispatcherPriority.Render, delegate { if (gear != null) gear.InvalidateVisual(); shell.Binder.InvalidateVisual(); });
            repaint.Start();
            var prefs = new PreferencesDialog(shell, shell.Project);
            // Comme l'application : en dialogue MODAL (Dialogs.ShowModal), pas en simple fenêtre.
            var modal = Dialogs.ShowModal(prefs, shell);
            await Task.Delay(800);
            // MB_TIP_SANS_CORRECTIF=1 rend ses infobulles à la fenêtre voilée : le défaut d'avant (bulles qui clignotent ou ne s'ouvrent pas).
            if (Environment.GetEnvironmentVariable("MB_TIP_SANS_CORRECTIF") == "1") shell.ClearValue(ToolTip.ServiceEnabledProperty);
            Check(ModalChrome.Dimmed(shell) && (!ToolTip.GetServiceEnabled(shell) || Environment.GetEnvironmentVariable("MB_TIP_SANS_CORRECTIF") == "1"), "sous son dialogue, la fenêtre principale est voilée et ne décide plus des infobulles");
            var skin = Environment.GetEnvironmentVariable("MB_TIP_SKIN");
            if (!string.IsNullOrEmpty(skin))
            {
                AscensionArt.ForcedForBench = true;
                prefs.SetSkin(skin);
                await Task.Delay(1200);
                foreach (var entry in prefs.Tabs.Items) { var tab = entry as TabItem; if (tab != null && "Personnalisation".Equals(tab.Header as string)) prefs.Tabs.SelectedItem = tab; }
                await Task.Delay(900);
                Console.WriteLine("TIP skin " + Skin.Current + " ; ornements en mouvement : " + AscensionArt.Moving);
            }
            foreach (var target in new Control[]
            {
                prefs.GetVisualDescendants().OfType<Control>().FirstOrDefault(delegate(Control control) { return "modal-close".Equals(control.Tag); }),
                prefs.GetVisualDescendants().OfType<Slider>().FirstOrDefault(delegate(Slider slider) { return ToolTip.GetTip(slider) != null && slider.IsEffectivelyVisible; }),
                prefs.GetVisualDescendants().OfType<Control>().FirstOrDefault(delegate(Control control) { return !(control is Slider) && !"modal-close".Equals(control.Tag) && ToolTip.GetTip(control) != null && control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.Bounds.Width > 20 && control.TranslatePoint(new Point(0, 0), prefs).HasValue && control.TranslatePoint(new Point(0, 0), prefs).Value.Y < prefs.Bounds.Height - 200; })
            })
            {
                if (target == null) { Console.WriteLine("  (pas de cible)"); continue; }
                var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), prefs);
                if (!centre.HasValue) continue;
                await Task.Delay(120);
                foreach (var stray in prefs.GetVisualDescendants().OfType<Control>().Where(delegate(Control control) { return ToolTip.GetIsOpen(control); }).ToList()) ToolTip.SetIsOpen(stray, false); // une bulle ouverte par la vraie souris, s'il y en avait une
                opens = closes = 0;
                seen = 0;
                if (log) Console.WriteLine("TIP " + at() + " — survol de " + target.GetType().Name + " [" + target.Tag + "] « " + ToolTip.GetTip(target) + " » en " + centre.Value);
                var device = NewMouse();
                if (device == null || !RawMove(prefs, device, new Point(centre.Value.X - 1, centre.Value.Y))) { Check(false, "la fenêtre des Préférences reçoit des événements bruts"); break; }
                for (var i = 0; i < 4; i++)
                {
                    RawMove(prefs, device, new Point(centre.Value.X + i, centre.Value.Y));
                    await Task.Delay(40);
                }
                await Task.Delay(700);
                // La bulle elle-même : sa fenêtre, et ce qui s'y passe pendant qu'elle est ouverte.
                var layouts = 0; var moves = 0; var paints = 0;
                var tipField = typeof(ToolTip).GetField("ToolTipProperty", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                var tip = tipField == null ? null : target.GetValue((AvaloniaProperty)tipField.GetValue(null)) as ToolTip;
                var host = tip == null ? null : TopLevel.GetTopLevel(tip);
                if (tip != null) tip.LayoutUpdated += delegate { layouts++; };
                var popupRoot = host as Avalonia.Controls.Primitives.PopupRoot;
                if (popupRoot != null) popupRoot.PositionChanged += delegate { moves++; };
                if (host != null) host.RequestAnimationFrame(delegate { paints++; });
                if (log) Console.WriteLine("TIP " + at() + " bulle " + (tip == null ? "introuvable" : "trouvée") + ", hôte " + (host == null ? "-" : host.GetType().Name + " visible=" + host.IsVisible + " taille=" + host.Bounds.Size + " opacité=" + host.Opacity) + (popupRoot == null ? "" : " origine écran=" + popupRoot.PointToScreen(new Point(0, 0)) + " ; cible écran=" + target.PointToScreen(new Point(0, 0))));
                // Un survol VIVANT : la main tremble — de petits déplacements continus, au-dessus du même contrôle.
                var hits = new System.Collections.Generic.Dictionary<string, int>();
                var shots = Environment.GetEnvironmentVariable("MB_TIP_SHOTS");
                for (var i = 0; i < 150; i++)
                {
                    // Ce que l'ÉCRAN montre à l'endroit de la bulle, à trois instants (ce coin d'écran seulement).
                    if (!string.IsNullOrEmpty(shots) && popupRoot != null && (i == 20 || i == 70 || i == 130))
                    {
                        var corner = popupRoot.PointToScreen(new Point(0, 0));
                        var region = (corner.X - 30) + "," + (corner.Y - 50) + "," + (int)(popupRoot.Bounds.Width + 60) + "," + (int)(popupRoot.Bounds.Height + 80);
                        var file = System.IO.Path.Combine(shots, "bulle-" + (target.Tag ?? "x").ToString().Replace(":", "-") + "-" + i + ".png");
                        try { using (var capture = System.Diagnostics.Process.Start("screencapture", "-x -R" + region + " \"" + file + "\"")) { if (capture != null) capture.WaitForExit(3000); } }
                        catch (Exception) { }
                    }
                    var jitter = new Point(centre.Value.X + (i % 5) - 2, centre.Value.Y + ((i / 5) % 3) - 1);
                    var under = prefs.InputHitTest(jitter) as Control;
                    var name = under == null ? "rien" : under.GetType().Name + "[" + under.Tag + "]";
                    hits[name] = (hits.ContainsKey(name) ? hits[name] : 0) + 1;
                    RawMove(prefs, device, jitter);
                    await Task.Delay(16);
                }
                if (log) Console.WriteLine("TIP " + at() + " sous le pointeur : " + string.Join(", ", hits.Select(delegate(System.Collections.Generic.KeyValuePair<string, int> pair) { return pair.Key + " ×" + pair.Value; })));
                if (log) Console.WriteLine("TIP " + at() + " en 2,5 s : mises en page " + layouts + ", déplacements " + moves + ", image demandée " + paints + (host == null ? "" : " ; visible=" + host.IsVisible + " taille=" + host.Bounds.Size));
                var open = ToolTip.GetIsOpen(target);
                if (log) Console.WriteLine("TIP " + at() + " déplacements vus : " + seen + " (5 injectés)");
                Check(opens == 1 && closes == 0 && open, "survolé d'une main qui tremble, « " + ToolTip.GetTip(target) + " » s'ouvre une fois et reste ouverte trois secondes (ouvertures " + opens + ", fermetures " + closes + ")");
                RawMove(prefs, device, new Point(3, prefs.Bounds.Height - 3));
                await Task.Delay(500);
            }
            prefs.Close();
            repaint.Stop();
            await Task.Delay(300);
            Check(!ModalChrome.Dimmed(shell) && ToolTip.GetServiceEnabled(shell), "le dialogue fermé, la fenêtre principale retrouve ses infobulles");
        }

        /// <summary>Un déplacement de souris BRUT dans cette fenêtre, par le même chemin que ceux du système
        /// (ces types d'Avalonia ne sont pas publics : par réflexion, pour ce banc d'essai seulement).</summary>
        private static bool RawMove(TopLevel window, object device, Point point)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var impl = window.PlatformImpl;
            if (impl == null) return false;
            System.Reflection.PropertyInfo input = null;
            foreach (var face in impl.GetType().GetInterfaces()) { input = face.GetProperty("Input", flags); if (input != null) break; }
            if (input == null) input = impl.GetType().GetProperty("Input", flags);
            var handler = input == null ? null : input.GetValue(impl) as Delegate;
            if (handler == null) return false;
            foreach (var ctor in typeof(RawPointerEventArgs).GetConstructors(flags))
            {
                var wanted = ctor.GetParameters();
                if (wanted.Length != 6 || wanted[4].ParameterType != typeof(Point)) continue;
                var args = ctor.Invoke(new object[] { device, (ulong)Environment.TickCount64, window, RawPointerEventType.Move, point, RawInputModifiers.None });
                handler.DynamicInvoke(args);
                return true;
            }
            return false;
        }

        private static object NewMouse()
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var ctor in typeof(MouseDevice).GetConstructors(flags))
            {
                var wanted = ctor.GetParameters();
                return ctor.Invoke(new object[wanted.Length]);
            }
            return null;
        }
    }
}

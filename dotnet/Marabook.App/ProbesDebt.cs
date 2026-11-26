using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde de la DETTE (2.0.0, 21/11/2026) : ce qui n'ajoute
    /// rien à l'écran mais use la confiance s'il attend. Elle MESURE (la
    /// frappe dans un très gros paragraphe, l'ouverture d'un long écrit) et
    /// vérifie ce qui a été soldé. MARABOOK_PROBE_ONLY=dette la joue seule.</summary>
    public static partial class Probes
    {
        internal const int HugeParagraphChars = 490000;

        internal static string HugeText(int chars)
        {
            var sentence = "Keira descendit vers le port avant que le jour ne se lève, la lettre du marabout serrée contre elle ; personne ne la vit passer. ";
            var sb = new StringBuilder(chars + sentence.Length);
            while (sb.Length < chars) sb.Append(sentence);
            return sb.ToString(0, chars);
        }

        private static async Task ProbeDebt(MainWindow shell)
        {
            var project = shell.Project;
            Console.WriteLine("-- la dette (2.0.0, 21/11)");
            var added = new List<BinderItem>();
            var perfBefore = Perf.Enabled;
            try
            {
                var writings = project.Category(Project.KeyWritings);

                // ================================================== un très gros paragraphe
                var huge = new BinderItem { Kind = ItemKind.Text, Title = "Très gros paragraphe (sonde)", Document = TextDocument.FromPlainText("Un début.\n" + HugeText(HugeParagraphChars) + "\nUne fin.") };
                huge.Parent = writings;
                writings.Children.Add(huge);
                added.Add(huge);
                shell.Binder.Rebuild();
                var open = Stopwatch.StartNew();
                shell.Binder.SelectItem(huge.Id);
                await Settle();
                open.Stop();
                var composed = shell.Editor.Composed;
                Check(shell.VisibleView == "editor" && composed.CurrentComposition != null && composed.CurrentComposition.Pages.Count > 50, "un écrit d'un seul paragraphe de " + HugeParagraphChars + " signes s'ouvre (" + composed.CurrentComposition.Pages.Count + " pages, " + open.ElapsedMilliseconds + " ms)");
                composed.PlaceCaretForProbe(1, HugeParagraphChars / 2);
                await Settle();
                Perf.Enabled = true;
                Perf.Reset();
                var worst = 0L;
                var total = 0L;
                const int strokes = 12;
                for (var i = 0; i < strokes; i++)
                {
                    var watch = Stopwatch.StartNew();
                    composed.TypeText(i % 5 == 4 ? " " : "e");
                    watch.Stop();
                    worst = Math.Max(worst, watch.ElapsedMilliseconds);
                    total += watch.ElapsedMilliseconds;
                    await Settle();
                }
                Console.WriteLine("  mesure : frappe au milieu d'un paragraphe de " + HugeParagraphChars + " signes — " + (total / strokes) + " ms en moyenne, " + worst + " ms au pire");
                Console.WriteLine("  détail (" + strokes + " frappes) : " + Perf.Report());
                Check(total / strokes < 120, "taper dans un très gros paragraphe reste fluide (" + (total / strokes) + " ms par frappe en moyenne)");
                var flat = PivotEdit.FlatText(huge.Document.Paragraphs[1]);
                Check(flat.Length == HugeParagraphChars + strokes, "…et le texte tapé y est, au bon endroit (" + flat.Length + " signes)");

                // ================================================== un long écrit ordinaire
                var longText = new StringBuilder();
                for (var p = 0; p < 3000; p++) longText.Append(HugeText(120 + (p * 37) % 420)).Append('\n');
                var novel = new BinderItem { Kind = ItemKind.Text, Title = "Long écrit (sonde)", Document = TextDocument.FromPlainText(longText.ToString().TrimEnd('\n')) };
                novel.Parent = writings;
                writings.Children.Add(novel);
                added.Add(novel);
                shell.Binder.Rebuild();
                Perf.Reset();
                open.Restart();
                shell.Binder.SelectItem(novel.Id);
                var selected = open.ElapsedMilliseconds;
                await Settle();
                open.Stop();
                var pages = composed.CurrentComposition.Pages.Count;
                Console.WriteLine("  mesure : ouvrir un écrit de 3 000 paragraphes (" + pages + " pages) — " + selected + " ms pour le montrer, " + open.ElapsedMilliseconds + " ms jusqu'au repos");
                Console.WriteLine("  détail : " + Perf.Report());
                composed.PlaceCaretForProbe(1500, 40);
                await Settle();
                Perf.Reset();
                total = 0; worst = 0;
                shell.DailyAsyncInProbe = true; // la capture du jour d'un long écrit : hors du fil, comme chez l'auteur
                for (var i = 0; i < strokes; i++)
                {
                    var watch = Stopwatch.StartNew();
                    composed.TypeText(i % 5 == 4 ? " " : "e");
                    watch.Stop();
                    worst = Math.Max(worst, watch.ElapsedMilliseconds);
                    total += watch.ElapsedMilliseconds;
                    await Settle();
                }
                Console.WriteLine("  mesure : frappe au milieu d'un écrit de 3 000 paragraphes — " + (total / strokes) + " ms en moyenne, " + worst + " ms au pire");
                Console.WriteLine("  détail (" + strokes + " frappes) : " + Perf.Report());
                Check(total / strokes < 60, "taper au milieu d'un écrit de 250 pages reste fluide (" + (total / strokes) + " ms par frappe en moyenne, " + worst + " ms au pire)");
                if (AppSettings.DailySnapshot)
                {
                    for (var i = 0; i < 40 && !SnapshotStore.HasToday(project, novel.Id, null); i++) await Task.Delay(100);
                    await Settle();
                    Check(SnapshotStore.HasToday(project, novel.Id, null), "la capture du jour de ce long écrit est prise hors du fil d'interface, puis rangée");
                }
                // L'annulation par différence : un second point partage tout ce qui n'a pas bougé.
                await Task.Delay(1000);
                composed.PlaceCaretForProbe(10, 5);
                composed.TypeText("z");
                await Settle();
                Check(composed.LastCaptureTotal == 3000 && composed.LastCaptureShared == 2999, "un point d'annulation ne recopie que le paragraphe touché (" + composed.LastCaptureShared + " partagés sur " + composed.LastCaptureTotal + ")");
                var beforeUndo = PivotEdit.FlatText(novel.Document.Paragraphs[10]);
                Check(shell.Editor.TryUndo() && PivotEdit.FlatText(novel.Document.Paragraphs[10]) == beforeUndo.Remove(5, 1), "…et Ctrl+Z rend bien le texte d'avant");
                Check(shell.Editor.TryRedo() && PivotEdit.FlatText(novel.Document.Paragraphs[10]) == beforeUndo, "…Ctrl+Y le reprend");
                var pin = Stopwatch.StartNew();
                shell.PinToSideForProbe(novel);
                var pinned = pin.ElapsedMilliseconds;
                await Settle();
                pin.Stop();
                Console.WriteLine("  mesure : épingler cet écrit au rail (le miroir) — " + pinned + " ms pour le poser, " + pin.ElapsedMilliseconds + " ms jusqu'au repos");
                int built, waiting;
                shell.PinnedPanelForProbe.MirrorCountsForProbe(out built, out waiting);
                Check(pin.ElapsedMilliseconds < 1500 && built > 0 && waiting > built * 5, "le miroir épinglé ne bâtit que ce qu'on voit (" + built + " tranches bâties, " + waiting + " en attente, " + pin.ElapsedMilliseconds + " ms)");
                shell.PinnedPanelForProbe.ScrollMirrorForProbe(0.5);
                await Settle();
                await Settle();
                int builtAfter, waitingAfter;
                shell.PinnedPanelForProbe.MirrorCountsForProbe(out builtAfter, out waitingAfter);
                Check(builtAfter > built && waitingAfter < waiting, "…et faire défiler bâtit les tranches qui arrivent à l'écran (" + builtAfter + " bâties)");
                shell.UnpinSideForProbe();
                await Settle();

                // ================================================== les annotations dans le flux d'annulation
                var noted = new BinderItem { Kind = ItemKind.Text, Title = "Écrit annoté (sonde)", Document = TextDocument.FromPlainText("Keira descendit vers le port.\nElle attendit longtemps.") };
                noted.Parent = writings;
                writings.Children.Add(noted);
                added.Add(noted);
                shell.Binder.Rebuild();
                shell.Binder.SelectItem(noted.Id);
                await Settle();
                composed = shell.Editor.Composed;
                var depth = composed.UndoDepthForProbe;
                composed.SelectRange(0, 0, 5);
                var annotation = new Annotation { Text = "Vérifier le prénom", Created = "2026-11-21 12:00" };
                Check(composed.AnnotateSelection(annotation.Id), "un passage s'annote");
                noted.Document.Annotations.Add(annotation);
                await Settle();
                Check(composed.UndoDepthForProbe == depth + 1 && noted.Document.Annotations.Count == 1, "annoter pose un cran d'annulation");
                Check(shell.Editor.TryUndo() && noted.Document.Annotations.Count == 0 && noted.Document.Paragraphs[0].Runs.TrueForAll(delegate(TextRun r) { return r.AnnotationId == null; }),
                    "Ctrl+Z retire l'annotation ENTIÈRE : ses ancres et son commentaire");
                Check(shell.Editor.TryRedo() && noted.Document.Annotations.Count == 1 && noted.Document.Annotations[0].Text == "Vérifier le prénom" && noted.Document.Paragraphs[0].Runs.Exists(delegate(TextRun r) { return r.AnnotationId == annotation.Id; }),
                    "Ctrl+Y la rend, commentaire compris");
                composed.PushUndoPoint(false);
                noted.Document.Annotations[0].Resolved = true;
                Check(shell.Editor.TryUndo() && !noted.Document.Annotations[0].Resolved, "résoudre une annotation s'annule");
                composed.PushUndoPoint(true);
                noted.Document.Annotations[0].Text = "Vérifier le prénom, et le nom";
                composed.PushUndoPoint(true); // une frappe de plus dans le commentaire, dans la foulée
                noted.Document.Annotations[0].Text = "Vérifier le prénom, et le nom de famille";
                Check(shell.Editor.TryUndo() && noted.Document.Annotations[0].Text == "Vérifier le prénom", "le commentaire tapé d'une traite ne fait qu'un cran : Ctrl+Z rend celui d'avant");
                // Un commentaire tapé APRÈS une frappe dans le texte : Ctrl+Z défait d'abord le commentaire.
                composed.PlaceCaretForProbe(1, 0);
                await Task.Delay(950);
                composed.TypeText("Puis ");
                await Task.Delay(950);
                composed.PushUndoPoint(false);
                noted.Document.Annotations[0].Text = "Écrit après la frappe";
                Check(shell.Editor.TryUndo() && noted.Document.Annotations[0].Text == "Vérifier le prénom" && PivotEdit.FlatText(noted.Document.Paragraphs[1]).StartsWith("Puis "),
                    "annuler ne ravale pas un commentaire écrit après du texte : le commentaire d'abord, le texte reste");
                Check(shell.Editor.TryUndo() && !PivotEdit.FlatText(noted.Document.Paragraphs[1]).StartsWith("Puis ") && noted.Document.Annotations.Count == 1, "…puis le texte, l'annotation toujours là");

                // ================================================== les idéogrammes dans l'éditeur
                var cjk = new StringBuilder();
                for (var i = 0; i < 300; i++) cjk.Append(i % 19 == 18 ? '。' : (char)(0x4E00 + (i * 29) % 400));
                var eastern = new BinderItem { Kind = ItemKind.Text, Title = "Idéogrammes (sonde)", Document = TextDocument.FromPlainText(cjk.ToString()) };
                eastern.Parent = writings;
                writings.Children.Add(eastern);
                added.Add(eastern);
                shell.Binder.Rebuild();
                shell.Binder.SelectItem(eastern.Id);
                await Settle();
                composed = shell.Editor.Composed;
                var layout = composed.CurrentComposition.Paragraphs[0];
                var contentWidth = composed.CurrentComposition.Setup.ContentWidthPx;
                var widest = 0.0;
                foreach (var line in layout.Lines)
                    foreach (var piece in line.Pieces) widest = Math.Max(widest, piece.Origin.X + piece.VisualWidth());
                Check(layout.Lines.Count >= 4 && widest <= contentWidth + 1, "un paragraphe de 300 idéogrammes sans espace passe à la ligne dans l'éditeur (" + layout.Lines.Count + " lignes, la plus large " + widest.ToString("0") + " px pour " + contentWidth.ToString("0") + ")");
                Shot(shell.Editor, "dette-1-ideogrammes.png");
                var notified = composed.ImeNotifications;
                composed.PlaceCaretForProbe(0, 150);
                await Settle();
                Check(composed.ImeNotifications > notified, "le curseur déplacé, le système de saisie est prévenu : sa fenêtre de candidats suit");
                composed.SetPreeditForProbe("かんじ");
                await Settle();
                Check(composed.PreeditShownForProbe, "un texte en cours de composition se montre au curseur");
                composed.SetPreeditForProbe(null);
                composed.TypeText("漢字");
                await Settle();
                Check(PivotEdit.FlatText(eastern.Document.Paragraphs[0]).Substring(150, 2) == "漢字" && !composed.PreeditShownForProbe, "les idéogrammes validés entrent dans le texte, à la place du curseur");

                // ================================================== un onglet retient où l'on en est dans une fiche
                var keira = FirstTitled(project, "Keira");
                if (keira != null)
                {
                    shell.Binder.SelectItem(keira.Id);
                    await Settle();
                    if (shell.Sheet.PreviewOnForProbe) { shell.Sheet.HandleFunctionKey(new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.F4 }); await Settle(); }
                    shell.Sheet.SetViewPosition(1, -1);
                    await Settle();
                    shell.OpenNewTabForProbe();
                    await Settle();
                    var tabs = shell.TabsForProbe;
                    var sheetTab = tabs.IndexOf(keira.Id);
                    Check(sheetTab >= 0 && tabs.Tabs[sheetTab].HasView && tabs.Tabs[sheetTab].ViewPane == 1, "quitter l'onglet d'une fiche retient où l'on en était (son onglet « Texte libre »)");
                    shell.Sheet.SetViewPosition(0, -1);
                    shell.ActivateTabForProbe(sheetTab);
                    await Settle();
                    await Settle();
                    int pane; double scroll;
                    shell.Sheet.GetViewPosition(out pane, out scroll);
                    Check(shell.CurrentForProbe == keira && pane == 1, "…et le retrouve au retour");
                    shell.Sheet.SetViewPosition(0, 0);
                }
            }
            finally
            {
                Perf.Enabled = perfBefore;
                var home = project.Category(Project.KeyWritings);
                shell.Binder.SelectItem(home.Id);
                await Settle();
                foreach (var item in added)
                    if (item != null && item.Parent != null) item.Parent.Children.Remove(item);
                shell.Binder.Rebuild();
                await Settle();
            }
        }
    }
}

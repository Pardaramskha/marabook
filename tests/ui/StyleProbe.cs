using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UniversSale.Correction;
using UniversSale.Correction.Grammalecte;
using UniversSale.Model;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale.Tests.Ui
{
    /// <summary>Sonde de l'étage style morphologique (batch 44) : le
    /// dialogue « Options du correcteur » avec ses sous-options (construit,
    /// montré hors écran, la case Style grise et dégrise le détail), puis le
    /// trajet ENTIER par le pilote — StyleChecker différé sur le vrai pont,
    /// réponses fusionnées, catégorie Style, « ignorer » par mot — et les
    /// synonymes à la demande (SynonymProvider réel : « faisait » →
    /// « exécutait »). Sautée proprement sans python/. Ne touche pas à
    /// settings.json (le dialogue n'est jamais validé).</summary>
    public static class StyleProbe
    {
        private static int _failures;

        [STAThread]
        public static int Main()
        {
            var failures = Run();
            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "SONDE STYLE OK" : failures + " ÉCHEC(S)");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>Enchaînée par A1Probe (même exe, même campagne) ; rend
        /// le nombre d'échecs.</summary>
        public static int Run()
        {
            _failures = 0;
            try
            {
                DialogProbe();
                PipelineProbe();
                VolumeProbe();
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE STYLE EN ÉCHEC : " + error);
                _failures++;
            }
            return _failures;
        }

        private static void DialogProbe()
        {
            var type = typeof(ProofOptionsDialog);
            var dialog = (Window)Activator.CreateInstance(type,
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { null }, null);
            dialog.Left = -4000;
            dialog.Top = -4000;
            dialog.WindowStartupLocation = WindowStartupLocation.Manual;
            dialog.Show();
            Pump();
            var style = (CheckBox)Field(dialog, "_style");
            var detail = (StackPanel)Field(dialog, "_styleDetail");
            var repetitions = (CheckBox)Field(dialog, "_repetitions");
            var adverbs = (CheckBox)Field(dialog, "_adverbs");
            var dullVerbs = (CheckBox)Field(dialog, "_dullVerbs");
            var dullList = (TextBox)Field(dialog, "_dullList");
            Check(repetitions != null && adverbs != null && dullVerbs != null,
                "les trois sous-options du style existent");
            Check(dullList.Text.Contains("être") && dullList.Text.Contains("faire"),
                "la liste des verbes ternes est préremplie (" + dullList.Text + ")");
            Check(detail.IsEnabled == (style.IsChecked == true),
                "le détail suit la case Style à l'ouverture");
            style.IsChecked = true;
            Pump();
            Check(detail.IsEnabled, "Style coché : le détail s'active");
            style.IsChecked = false;
            Pump();
            Check(!detail.IsEnabled, "Style décoché : le détail se grise");
            style.IsChecked = true;
            dullVerbs.IsChecked = false;
            Pump();
            Check(!dullList.IsEnabled, "verbes ternes décochés : la liste se grise");
            dullVerbs.IsChecked = true;
            Pump();
            Check(dullList.IsEnabled, "… et revient");
            dialog.Close();
            Pump();
        }

        private static void PipelineProbe()
        {
            if (!File.Exists(GrammalecteBridge.PythonPath))
            {
                Console.WriteLine("  (pipeline sauté : python/ absent)");
                return;
            }
            var bridge = new GrammalecteBridge();
            try
            {
                var document = new TextDocument();
                var first = new TextParagraph();
                first.Runs.Add(new TextRun
                { Text = "Il faisait rapidement ses devoirs, puis il avait mangé." });
                document.Paragraphs.Add(first);
                var second = new TextParagraph();
                second.Runs.Add(new TextRun
                { Text = "Le marabout regarde le marabout dans le miroir." });
                document.Paragraphs.Add(second);
                var styles = StyleSheet.CreateDefault();

                var host = new CheckerHost();
                host.Add(new RepetitionChecker());
                var checker = new StyleChecker(bridge);
                host.Add(checker);
                var arrived = new ManualResetEvent(false);
                host.DeferredArrived += delegate { arrived.Set(); };

                var immediate = host.Run(document, styles);
                Check(host.PendingDeferred > 0, "le style TRAVAILLE (demandes en vol : " + host.PendingDeferred + ")");
                var repetition = 0;
                foreach (var finding in immediate)
                    if (finding.CheckerId == "repetition") repetition++;
                Check(repetition == 1, "la répétition (synchrone) est servie tout de suite");

                Check(arrived.WaitOne(60000), "les réponses de style arrivent");
                for (var spin = 0; spin < 300 && host.PendingDeferred > 0; spin++)
                    Thread.Sleep(100);
                var merged = host.Run(document, styles);
                var kinds = new List<string>();
                foreach (var finding in merged)
                    if (finding.CheckerId == "style")
                    {
                        Check(finding.Category == FindingCategory.Style, "catégorie Style (" + finding.Word + ")");
                        Check(finding.Severity == FindingSeverity.Hint, "sévérité indice (" + finding.Word + ")");
                        kinds.Add(finding.RuleId + ":" + finding.Word);
                    }
                Check(kinds.Contains(StyleChecker.DullVerbRule + ":faisait"),
                    "« faisait » relevé comme verbe terne (" + string.Join(", ", kinds.ToArray()) + ")");
                Check(kinds.Contains(StyleChecker.AdverbRule + ":rapidement"),
                    "« rapidement » relevé comme adverbe en -ment");
                Check(!kinds.Contains(StyleChecker.DullVerbRule + ":avait"),
                    "« avait mangé » : auxiliaire laissé en paix");
                var flat = PivotEdit.FlatText(document.Paragraphs[0]);
                foreach (var finding in merged)
                    if (finding.CheckerId == "style" && finding.Word == "rapidement")
                        Check(flat.Substring(finding.Start, finding.Length) == "rapidement",
                            "l'ondulé couvre exactement « rapidement »");

                // « Ignorer dans ce projet » par mot : l'adverbe se tait.
                host.IgnoreInProject("rapidement");
                var still = 0;
                foreach (var finding in host.Run(document, styles))
                    if (finding.CheckerId == "style" && finding.Word == "rapidement") still++;
                Check(still == 0, "« ignorer rapidement dans ce projet » tait l'adverbe");

                // Les interrupteurs : adverbes éteints → plus d'adverbe, sans
                // réponse à attendre (filtre de sortie) après invalidation.
                checker.AdverbsEnabled = false;
                host.InvalidateCache();
                arrived.Reset();
                host.Run(document, styles);
                Check(arrived.WaitOne(60000), "réponses après changement d'option");
                for (var spin = 0; spin < 300 && host.PendingDeferred > 0; spin++)
                    Thread.Sleep(100);
                var adverbs = 0;
                var dull = 0;
                foreach (var finding in host.Run(document, styles))
                {
                    if (finding.RuleId == StyleChecker.AdverbRule) adverbs++;
                    if (finding.RuleId == StyleChecker.DullVerbRule) dull++;
                }
                Check(adverbs == 0 && dull >= 1, "adverbes éteints, verbes ternes toujours là (" + dull + ")");

                // Les synonymes à la demande, sur le pont réel.
                var provider = SynonymProvider.For(bridge);
                var synonymsArrived = new ManualResetEvent(false);
                provider.Arrived += delegate { synonymsArrived.Set(); };
                Check(provider.Flat("faisait", 5) == null, "rien de connu avant la demande");
                provider.Request("faisait");
                Check(synonymsArrived.WaitOne(60000), "les synonymes arrivent (thésaurus chargé à la demande)");
                var words = provider.Flat("faisait", 200);
                Check(words != null && words.Contains("exécutait"),
                    "« faisait » → « exécutait » (conjugué) — " + (words == null ? 0 : words.Count) + " synonymes");
                Check(provider.PendingCount == 0, "plus rien en vol");
            }
            finally
            {
                bridge.Dispose();
            }
        }

        /// <summary>Le gel du 13/09/2026 (premier crash de Marabook), rejoué
        /// en synthétique : un chapitre de 120 paragraphes riches en verbes
        /// ternes et en adverbes — assez de trames dans les deux sens pour
        /// remplir les tubes (4 Ko) du pont. Avant le fil d'écriture, Run()
        /// ne revenait JAMAIS (écriture bloquante sous verrou sur le fil
        /// appelant, lecteur bloqué sur le verrou, Python bloqué sur sa
        /// sortie). Critères : la passe synchrone revient vite, tout le
        /// différé arrive, le pont reste Ready.</summary>
        private static void VolumeProbe()
        {
            if (!File.Exists(GrammalecteBridge.PythonPath))
            {
                Console.WriteLine("  (volume sauté : python/ absent)");
                return;
            }
            var bridge = new GrammalecteBridge();
            try
            {
                var document = new TextDocument();
                for (var i = 0; i < 120; i++)
                {
                    var paragraph = new TextParagraph();
                    paragraph.Runs.Add(new TextRun
                    {
                        Text = "Il faisait rapidement ce qu'il avait à faire, puis il disait "
                            + "vraiment tout ce qu'il savait ; elle mettait lentement la table, "
                            + "voyait le jour se lever, donnait le change et prenait son temps. "
                            + "Nous étions là, ils allaient et venaient, on pouvait enfin dire "
                            + "que tout était fait, réellement, complètement, absolument (" + i + ")."
                    });
                    document.Paragraphs.Add(paragraph);
                }
                var styles = StyleSheet.CreateDefault();
                var host = new CheckerHost();
                host.Add(new RepetitionChecker());
                host.Add(new StyleChecker(bridge));
                var arrived = new ManualResetEvent(false);
                host.DeferredArrived += delegate { arrived.Set(); };

                var watch = System.Diagnostics.Stopwatch.StartNew();
                var returned = false;
                var runner = new Thread(delegate()
                {
                    host.Run(document, styles);
                    returned = true;
                });
                runner.Start();
                var back = runner.Join(20000);
                watch.Stop();
                Check(back && returned, "120 paragraphes : la passe synchrone REVIENT ("
                    + watch.ElapsedMilliseconds + " ms) — le gel du 13/09 ne revient pas");
                if (!back) return; // le pont est bloqué : inutile d'insister
                Check(watch.ElapsedMilliseconds < 1000, "… et vite (" + watch.ElapsedMilliseconds + " ms)");
                Check(arrived.WaitOne(60000), "les réponses arrivent malgré le volume");
                for (var spin = 0; spin < 600 && host.PendingDeferred > 0; spin++)
                    Thread.Sleep(100);
                Check(host.PendingDeferred == 0, "tout le différé est servi (en vol : " + host.PendingDeferred + ")");
                var style = 0;
                foreach (var finding in host.Run(document, styles))
                    if (finding.CheckerId == "style") style++;
                Check(style >= 120 * 10, "les relevés de style sont là (" + style + ")");
                Check(bridge.State == BridgeState.Ready, "le pont est toujours prêt (" + bridge.State + " " + bridge.StateDetail + ")");
            }
            finally
            {
                bridge.Dispose();
            }
        }

        private static object Field(object target, string name)
        {
            var field = target.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            return field == null ? null : field.GetValue(target);
        }

        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }
    }
}

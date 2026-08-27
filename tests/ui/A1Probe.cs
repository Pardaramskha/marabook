using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UniversSale.Model;
using UniversSale.Persistence;
using UniversSale.Settings;
using UniversSale.View;

namespace UniversSale.Tests.Ui
{
    /// <summary>Sonde UI d'A1 (batch 26, lot 0.3) — le bug le plus grave du
    /// batch 24 : le corps d'une fiche (SheetView._body, un second EditorView)
    /// n'était jamais commité par Ctrl+S. La sonde ouvre un vrai projet dans
    /// une vraie MainWindow (thémée, hors écran), tape dans le corps de la
    /// fiche, sauve, recharge depuis le DISQUE et vérifie le texte — sur les
    /// trois chemins : Ctrl+S (DoSave), autosave, et fermeture.
    ///
    /// Limite assumée : le clic « Oui » du dialogue de fermeture n'est pas
    /// automatisable (jamais de clics synthétiques — règle du batch 3) ; le
    /// chemin vérifié est exactement celui que « Oui » emprunte
    /// (ConfirmDiscard → DoSave → CommitActive), suivi d'un Close() réel.
    ///
    /// Règle du batch 11 : settings.json est sauvegardé puis restauré.</summary>
    public static class A1Probe
    {
        private static int _failures;

        [STAThread]
        public static int Main()
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Marabook", "settings.json");
            var backup = File.Exists(settingsPath)
                ? File.ReadAllBytes(settingsPath) : null;
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests");
            try
            {
                Directory.CreateDirectory(dir);
                Run(Path.Combine(dir, "a1.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("SONDE EN ÉCHEC : " + error);
                _failures++;
            }
            finally
            {
                // Règle du batch 11 : l'état utilisateur est restauré tel quel.
                try
                {
                    if (backup != null) File.WriteAllBytes(settingsPath, backup);
                    else if (File.Exists(settingsPath)) File.Delete(settingsPath);
                }
                catch { }
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
            // Les autres sondes UI de la campagne (même exe, même règle
            // settings.json — elles n'y touchent pas).
            Console.WriteLine();
            _failures += CorrectionProbe.Run();
            Console.WriteLine();
            _failures += SheetProbe.Run();
            if (Application.Current != null)
            {
                Application.Current.Shutdown();
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                    new Action(delegate { frame.Continue = false; }));
                Dispatcher.PushFrame(frame);
            }
            Console.WriteLine(_failures == 0
                ? "SONDES UI OK" : "*** SONDES UI : " + _failures + " échec(s) ***");
            return _failures == 0 ? 0 : 1;
        }

        private static void Run(string path)
        {
            // — Le projet sur disque : une fiche avec modèle, corps vide.
            var project = Project.CreateNew();
            var template = new SheetTemplate { Name = "Personnage" };
            template.Fields.Add(new SheetField { Name = "Nom", Kind = "text" });
            project.Templates.Add(template);
            var sheet = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = "Fiche sonde",
                TemplateId = template.Id
            };
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();
            PlotFile.Save(project, path);

            // — L'application réelle, thémée, hors écran. Depuis le batch 31
            //   le corps d'une fiche est la source markdown (TextBox) : le
            //   chemin verrouillé par la sonde reste frappe → Edited →
            //   MarkDirty → Commit → disque, sur les trois sorties.
            AppSettings.Load();
            Chrome.Toggle(false);
            // UNE Application pour toute la campagne (un AppDomain WPF n'en
            // accepte qu'une) : créée ici, réutilisée par les sondes
            // suivantes, éteinte en fin de Main.
            var application = Application.Current ?? new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            Theme.Apply(application);
            var window = new MainWindow
            {
                WindowState = WindowState.Normal,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2600,
                Top = 0,
                Width = 1280,
                Height = 800,
                ShowInTaskbar = false
            };
            window.Show();
            DoEvents();
            window.OpenFile(path);
            DoEvents();

            // — Sélectionner la fiche (le chemin OnBinderSelection réel).
            var opened = (Project)GetField(window, "_project");
            BinderItem target = null;
            foreach (var item in opened.AllItems())
                if (item.Kind == ItemKind.Sheet) { target = item; break; }
            Check(target != null, "la fiche existe dans le projet ouvert");
            Invoke(window, "OnBinderSelection", new object[] { target });
            DoEvents();

            // Batch 31 : le corps d'une fiche est la SOURCE markdown, un
            // TextBox — la sonde tape dedans par le même chemin que l'A1.
            var sheetView = GetField(window, "_sheetView");
            var box = (TextBox)GetField(sheetView, "_bodyBox");

            // ---- Chemin 1 : Ctrl+S (DoSave) --------------------------------
            TypeInBody(box, "SONDE-A1-CTRLS");
            DoEvents();
            Check((bool)GetField(window, "_dirty"),
                "la frappe dans le corps marque le projet sale");
            Invoke(window, "DoSave", null);
            DoEvents();
            Check(BodyOnDisk(path).Contains("SONDE-A1-CTRLS"),
                "Ctrl+S : le corps de la fiche est sur le disque");

            // ---- Chemin 2 : autosave ---------------------------------------
            TypeInBody(box, " SONDE-A1-AUTOSAVE");
            DoEvents();
            Invoke(window, "Autosave", null);
            DoEvents();
            Check(BodyOnDisk(path).Contains("SONDE-A1-AUTOSAVE"),
                "autosave : le corps de la fiche est sur le disque");

            // ---- Chemin 3 : fermeture --------------------------------------
            // La branche « Oui » de ConfirmDiscard = DoSave, puis Close réel
            // (voir la limite assumée dans l'en-tête).
            TypeInBody(box, " SONDE-A1-FERMETURE");
            DoEvents();
            Invoke(window, "DoSave", null);
            window.Close();
            DoEvents();
            var final = BodyOnDisk(path);
            Check(final.Contains("SONDE-A1-CTRLS")
                && final.Contains("SONDE-A1-AUTOSAVE")
                && final.Contains("SONDE-A1-FERMETURE"),
                "fermeture : les trois frappes ont survécu sur le disque");
            // L'Application reste vivante pour les sondes suivantes —
            // extinction en fin de Main.
        }

        /// <summary>Frappe au clavier simulée par l'API du TextBox (jamais
        /// de clics/touches synthétiques — règle du batch 3) : le TextChanged
        /// réel se déclenche, la chaîne Edited → MarkDirty aussi.</summary>
        private static void TypeInBody(TextBox box, string text)
        {
            box.CaretIndex = box.Text.Length;
            box.SelectedText = text;
            box.CaretIndex = box.Text.Length;
        }

        private static string BodyOnDisk(string path)
        {
            var loaded = PlotFile.Load(path);
            foreach (var item in loaded.AllItems())
                if (item.Kind == ItemKind.Sheet) return item.Document.ToPlainText();
            return "";
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new InvalidOperationException(
                    target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return field.GetValue(target);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
                throw new InvalidOperationException(
                    target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            method.Invoke(target, args);
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
}

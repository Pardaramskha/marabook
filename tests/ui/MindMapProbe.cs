using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Marabook.Extensions;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Marabook.View;

namespace Marabook.Tests.Ui
{
    /// <summary>La sonde des cartes mentales (22/09) : le VRAI paquet
    /// mental-o.mdlc du dépôt voisin (marabook-dlc-mental-o) s'installe dans
    /// un dossier de sonde, sa DLL se charge, le module se rattache à la
    /// fenêtre ; un projet avec deux cartes s'ouvre, la racine montre ses
    /// tuiles (vignette du module), la carte s'ouvre dans l'éditeur du
    /// module, une boîte s'y ajoute, les octets reviennent dans l'élément ;
    /// désinstallé, la carte montre la tuile d'invitation. Sans paquet
    /// voisin : la sonde s'abstient. settings.json est l'affaire de
    /// l'appelant (A1Probe) ; le dossier des modules est celui de la sonde.</summary>
    public static class MindMapProbe
    {
        private static int _failures;

        [STAThread]
        public static int Main()
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook", "settings.json");
            var backup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            try { Run(); }
            finally
            {
                try
                {
                    if (backup != null) File.WriteAllBytes(settingsPath, backup);
                    else if (File.Exists(settingsPath)) File.Delete(settingsPath);
                }
                catch { }
            }
            if (Application.Current != null) { Application.Current.Shutdown(); DoEvents(); }
            Console.WriteLine(_failures == 0 ? "SONDE CARTES OK" : "*** SONDE CARTES : " + _failures + " échec(s) ***");
            return _failures == 0 ? 0 : 1;
        }

        public static int Run()
        {
            // Le dépôt voisin, à côté du dépôt marabook : depuis l'exe des
            // sondes (racine du dépôt) ou depuis le dossier courant.
            var package = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "marabook-dlc-mental-o", "mental-o.mdlc"));
            if (!File.Exists(package))
                package = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "marabook-dlc-mental-o", "mental-o.mdlc"));
            if (!File.Exists(package))
            {
                Console.WriteLine("  INFO   paquet mental-o.mdlc absent (" + package + ") : sonde des cartes mentales non jouée");
                return 0;
            }
            var dir = Path.Combine(Path.GetTempPath(), "marabook-ui-tests-cartes");
            var previousRoot = Modules.RootOverride;
            // La DLL du module référence « Marabook » : dans l'exe des sondes,
            // ces types vivent sous un autre nom d'assembly — on le lui tend.
            ResolveEventHandler resolver = delegate(object sender, ResolveEventArgs args)
            {
                return args.Name.StartsWith("Marabook,", StringComparison.Ordinal) || args.Name == "Marabook"
                    ? typeof(Project).Assembly : null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                Modules.RootOverride = Path.Combine(dir, "dlc");
                Probe(package, Path.Combine(dir, "cartes.plot"));
            }
            catch (Exception error)
            {
                Console.WriteLine("  SONDE CARTES EN ÉCHEC : " + error);
                _failures++;
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= resolver;
                ModuleRegistry.Unregister("mental-o");
                Modules.RootOverride = previousRoot;
                Modules.Load();
                try { Directory.Delete(dir, true); } catch { }
            }
            return _failures;
        }

        private static void Probe(string package, string path)
        {
            // — Le module s'installe et se charge.
            Modules.Load();
            var module = Modules.Install(package);
            Check(module != null && module.Id == "mental-o" && module.HasCode, "le paquet mental-o.mdlc s'installe : un module à code");
            var provider = ModuleRegistry.MindMaps;
            Check(provider != null, "sa DLL est chargée : un fournisseur de cartes mentales est enregistré");
            if (provider == null) return;

            // — Un projet : une carte neuve du module, une carte de trois boîtes.
            var project = Project.CreateNew();
            var root = project.Category(Project.KeyMindMaps);
            var fresh = new BinderItem { Kind = ItemKind.MindMap, Title = "Carte neuve", MapBytes = provider.NewMap("Carte neuve") };
            var sample = new BinderItem { Kind = ItemKind.MindMap, Title = "Trois boîtes", MapBytes = MindMapTests.SampleTea(3, 2, 0) };
            root.Children.Add(fresh);
            root.Children.Add(sample);
            project.RelinkParents();
            PlotFile.Save(project, path);
            Check(MindMaps.Inspect(fresh.MapBytes).Readable && MindMaps.Inspect(fresh.MapBytes).Nodes == 0, "la carte neuve du module est un .tea lisible et vide");
            Check(provider.Thumbnail(sample.MapBytes, 186, 96) != null && provider.Thumbnail(fresh.MapBytes, 186, 96) == null,
                "la vignette du module : un schéma pour la carte à boîtes, rien pour la carte vide");

            AppSettings.Load();
            AppSettings.DarkTheme = false;
            Chrome.Toggle(false);
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme.Apply(application);
            var window = new MainWindow
            {
                WindowState = WindowState.Normal,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -2600, Top = 0, Width = 1280, Height = 800, ShowInTaskbar = false
            };
            try
            {
                window.Show();
                DoEvents();
                window.OpenFile(path);
                DoEvents();
                var opened = (Project)GetField(window, "_project");
                var binder = (BinderView)GetField(window, "_binder");
                var host = (Grid)GetField(window, "_mindMapHost");
                BinderItem freshItem = null, sampleItem = null;
                foreach (var item in opened.AllItems())
                {
                    if (item.Title == "Carte neuve") freshItem = item;
                    if (item.Title == "Trois boîtes") sampleItem = item;
                }
                Check(freshItem != null && sampleItem != null && sampleItem.MapBytes != null, "les deux cartes ont survécu au .plot");

                // — La racine : le corkboard et ses tuiles.
                binder.SelectItem(opened.Category(Project.KeyMindMaps).Id);
                DoEvents();
                var corkboard = (CorkboardView)GetField(window, "_corkboard");
                Check(corkboard.Visibility == Visibility.Visible, "la racine Cartes mentales montre son corkboard");
                var cards = (Panel)GetField(corkboard, "_cards");
                Check(cards.Children.Count == 2, "deux tuiles (obtenu : " + cards.Children.Count + ")");
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-2209-cartes-corkboard.png"));

                // — La carte s'ouvre dans l'éditeur du module.
                Invoke(window, "OnBinderSelection", new object[] { sampleItem });
                DoEvents();
                var editor = (IMindMapEditor)GetField(window, "_mindMapEditor");
                Check(host.Visibility == Visibility.Visible && editor != null && host.Children.Count == 1 && host.Children[0] == editor.View,
                    "la carte s'ouvre dans l'éditeur du module, incrusté dans la fenêtre");
                Check(!editor.IsDirty, "rien de modifié à l'ouverture");

                // — Une boîte de plus, par la toile de Mental-o.
                var toile = GetField(editor, "_toile");
                var create = toile.GetType().GetMethod("CreerBoiteLibre");
                Check(create != null, "la toile de Mental-o est là (CreerBoiteLibre)");
                create.Invoke(toile, new object[] { new Point(400, 300) });
                DoEvents();
                Check(editor.IsDirty && (bool)GetField(window, "_dirty"), "une boîte ajoutée : la carte et le projet sont modifiés");
                Invoke(window, "CommitMindMap", null);
                Check(MindMaps.Inspect(sampleItem.MapBytes).Nodes == 4, "les octets reviennent dans l'élément : quatre boîtes (obtenu : " + MindMaps.Inspect(sampleItem.MapBytes).Nodes + ")");
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-2209-cartes-editeur.png"));

                // — L'autre carte, vide, s'ouvre sans bruit ; l'éditeur est le même.
                Invoke(window, "OnBinderSelection", new object[] { freshItem });
                DoEvents();
                Check(ReferenceEquals(GetField(window, "_mindMapEditor"), editor) && host.Visibility == Visibility.Visible, "un seul éditeur par session, réutilisé");
                // — Trois vraies boîtes de la toile (texte, police, taille du
                //   document) : elles se voient, se sauvent, se schématisent.
                // Une boîte vide disparaît quand on la quitte (règle de
                // Mental-o) : chacune reçoit un texte avant la suivante.
                TypeBox(toile, create, new Point(120, 120), "Idée centrale");
                TypeBox(toile, create, new Point(420, 160), "Personnages");
                TypeBox(toile, create, new Point(260, 340), "Lieux");
                Invoke(window, "CommitMindMap", null);
                Check(MindMaps.Inspect(freshItem.MapBytes).Nodes == 3, "trois boîtes créées par la toile (obtenu : " + MindMaps.Inspect(freshItem.MapBytes).Nodes + ")");
                Check(provider.Thumbnail(freshItem.MapBytes, 186, 96) != null, "…et une vignette pour elles");
                RenderPng((FrameworkElement)window.Content, Path.Combine(Path.GetTempPath(), "marabook-2209-cartes-boites.png"));

                // — Enregistré : le .plot relu porte la boîte ajoutée.
                SetField(window, "_dirty", true);
                Invoke(window, "DoSave", null);
                DoEvents();
                var reloaded = PlotFile.Load(path);
                var back = 0;
                foreach (var item in reloaded.AllItems()) if (item.Title == "Trois boîtes") back = MindMaps.Inspect(item.MapBytes).Nodes;
                Check(back == 4, "le .plot enregistré relit quatre boîtes");

                // — Désinstallé : la tuile d'invitation remplace l'éditeur.
                Modules.Uninstall("mental-o");
                DoEvents();
                Check(ModuleRegistry.MindMaps == null, "désinstallé : plus de fournisseur (la DLL reste chargée jusqu'au redémarrage)");
                Invoke(window, "OnBinderSelection", new object[] { sampleItem });
                DoEvents();
                Check(host.Visibility == Visibility.Visible && host.Children.Count == 1 && host.Children[0] is StackPanel,
                    "sans module, la carte montre la tuile d'invitation");
                Check(MindMaps.Inspect(sampleItem.MapBytes).Nodes == 4, "…et garde ses octets");
            }
            finally
            {
                SetField(window, "_dirty", false);
                window.Close();
                DoEvents();
            }
        }

        /// <summary>Une boîte posée par le modèle de Mental-o (Noeud + segment
        /// riche à la taille du document) et ajoutée à la toile — la voie des
        /// commandes d'historique, qui signale la modification.</summary>
        private static void TypeBox(object toile, MethodInfo create, Point position, string text)
        {
            var assembly = toile.GetType().Assembly;
            var document = toile.GetType().GetProperty("Document").GetValue(toile, null);
            var size = (double)document.GetType().GetProperty("TailleParDefaut").GetValue(document, null);
            var nodeType = assembly.GetType("Mentalo.Modele.Noeud");
            var node = Activator.CreateInstance(nodeType);
            nodeType.GetProperty("X").SetValue(node, position.X, null);
            nodeType.GetProperty("Y").SetValue(node, position.Y, null);
            var segmentType = assembly.GetType("Mentalo.Modele.Segment");
            var segment = Activator.CreateInstance(segmentType);
            segmentType.GetProperty("Texte").SetValue(segment, text, null);
            segmentType.GetProperty("Taille").SetValue(segment, size > 0 ? size : 14.0, null);
            var segments = (System.Collections.IList)nodeType.GetProperty("Segments").GetValue(node, null);
            segments.Add(segment);
            toile.GetType().GetMethod("AjouterNoeud").Invoke(toile, new object[] { node });
            DoEvents();
        }

        private static void RenderPng(FrameworkElement element, string path)
        {
            try
            {
                var width = (int)Math.Max(1, element.ActualWidth);
                var height = (int)Math.Max(1, element.ActualHeight);
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                Console.WriteLine("  INFO   rendu : " + path);
            }
            catch (Exception error) { Console.WriteLine("  INFO   rendu impossible : " + error.Message); }
        }

        private static void Check(bool condition, string label)
        {
            Console.WriteLine((condition ? "  OK     " : "  ÉCHEC  ") + label);
            if (!condition) _failures++;
        }

        private static object GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private static void Invoke(object target, string name, object[] args)
        {
            var method = target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " introuvable (sonde à réaligner)");
            method.Invoke(target, args);
        }

        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate { frame.Continue = false; }));
            Dispatcher.PushFrame(frame);
        }
    }
}

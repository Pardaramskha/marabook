using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Simple;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>L'application : la plate-forme du cœur, les réglages, le
    /// thème, la fenêtre principale.</summary>
    public class App : Application
    {
        public static Launch Launch { get; private set; }

        public override void Initialize()
        {
            Launch = Launch.Parse(Program.Args);
            AppPlatform.Install();
            if (Launch.SettingsPath != null) AppSettings.PathOverride = Launch.SettingsPath;
            // Une sonde ou une capture n'écrit jamais dans le secours de l'utilisateur.
            if (Launch.Isolated) Persistence.RecoveryStore.Root =System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marabook-sonde-recovery");
            AppSettings.Load();
            if (Launch.Dark) AppSettings.DarkTheme = true;
            Chrome.Toggle(AppSettings.DarkTheme);
            RequestedThemeVariant = AppSettings.DarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
            // Le thème de base (gabarits des contrôles), ses ressources
            // remplacées par nos pinceaux, puis nos styles par-dessus.
            Styles.Add(new SimpleTheme());
            Theme.OverrideResources(Resources);
            Styles.Add(Theme.Build());
            SmoothScroll.Install(); // le défilement fluide à la molette, partout (0.50.0)
        }

        /// <summary>Bascule clair/sombre en direct (menu, Préférences).</summary>
        public static void ApplyTheme(bool dark)
        {
            Chrome.Toggle(dark);
            if (Current != null)
                Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        /// <summary>La fenêtre principale, propriétaire des dialogues (null
        /// avant son ouverture).</summary>
        public static Window MainWindowOrNull
        {
            get
            {
                var desktop = Current == null ? null : Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                return desktop == null ? null : desktop.MainWindow;
            }
        }

        /// <summary>Quitter (Application.Current.Shutdown() de WPF).</summary>
        public static void Exit()
        {
            var desktop = Current == null ? null : Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null) desktop.Shutdown();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var window = new MainWindow(Launch);
                desktop.MainWindow = window;
                // Fin de processus garantie (1.0.3-patch-b) : sur macOS, ⌘Q
                // fermait les fenêtres mais le processus restait (le point
                // sous l'icône du Dock), à tuer à la main. Quand Avalonia
                // annonce la sortie, tout est déjà écrit (réglages, verrou,
                // secours : la fermeture de la fenêtre) ; si la boucle
                // principale ne rend pas la main dans les trois secondes, le
                // processus s'arrête de lui-même.
                desktop.Exit += delegate(object sender, ControlledApplicationLifetimeExitEventArgs e)
                {
                    var code = e.ApplicationExitCode;
                    var watchdog = new System.Threading.Thread(delegate()
                    {
                        System.Threading.Thread.Sleep(3000);
                        Environment.Exit(code);
                    }) { IsBackground = true, Name = "fin-de-processus" };
                    watchdog.Start();
                };
                // Les .plot que le système demande d'ouvrir (macOS : double-clic
                // dans le Finder, « Ouvrir avec » — le bundle Marabook.app
                // déclare le type, 1.0.3-patch-b) : pas d'argument sur la ligne
                // de commande, mais un événement d'activation.
                var activatable = TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;
                if (activatable != null)
                    activatable.Activated += delegate(object sender, ActivatedEventArgs e)
                    {
                        var files = e as FileActivatedEventArgs;
                        if (files == null || files.Files.Count == 0) return;
                        var path = files.Files[0].TryGetLocalPath();
                        if (path != null) window.OpenFromSystem(path);
                    };
                // Le filet du secours (18/09, rebranché sur Avalonia le 28/09 :
                // il manquait, et tout clic droit fautif emportait le
                // processus sans rapport) : une erreur non rattrapée sur le
                // fil d'interface écrit le secours et le rapport, s'explique,
                // et l'application continue ; une erreur fatale ailleurs écrit
                // au moins le secours avant la chute.
                Avalonia.Threading.Dispatcher.UIThread.UnhandledException += delegate(object sender, Avalonia.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    if (Launch != null && Launch.Isolated) return; // les sondes veulent la chute et la pile
                    window.OnCrash(e.Exception);
                    e.Handled = true;
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    window.OnFatal(e.ExceptionObject as Exception);
                };
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}

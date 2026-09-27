using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
            AppSettings.Load();
            if (Launch.Dark) AppSettings.DarkTheme = true;
            Chrome.Toggle(AppSettings.DarkTheme);
            RequestedThemeVariant = AppSettings.DarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
            // Le thème de base (gabarits des contrôles), ses ressources
            // remplacées par nos pinceaux, puis nos styles par-dessus.
            Styles.Add(new SimpleTheme());
            Theme.OverrideResources(Resources);
            Styles.Add(Theme.Build());
        }

        /// <summary>Bascule clair/sombre en direct (menu, Préférences).</summary>
        public static void ApplyTheme(bool dark)
        {
            Chrome.Toggle(dark);
            if (Current != null)
                Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = new MainWindow(Launch);
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}

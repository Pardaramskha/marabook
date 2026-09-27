using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Simple;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>L'application : la plate-forme du cœur, les réglages (lus,
    /// jamais écrits en P1), le thème, la fenêtre principale.</summary>
    public class App : Application
    {
        public override void Initialize()
        {
            AppPlatform.Install();
            AppSettings.Load();
            Chrome.Toggle(AppSettings.DarkTheme);
            RequestedThemeVariant = AppSettings.DarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
            // Le thème de base (gabarits des contrôles), puis le nôtre par-dessus.
            Styles.Add(new SimpleTheme());
            Styles.Add(Theme.Build());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                var launch = Launch.Parse(Program.Args);
                desktop.MainWindow = new MainWindow(launch);
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}

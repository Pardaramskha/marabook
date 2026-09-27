using System;
using System.IO;
using Avalonia;

namespace Marabook.App
{
    /// <summary>Le point d'entrée de Marabook sur Avalonia (P1). Arguments :
    /// un chemin .plot (ouvert au démarrage), et pour les sondes
    /// « --capture &lt;png&gt; » — la fenêtre principale se rend en PNG une
    /// fois posée, puis quitte : la preuve visuelle sur chaque OS, sans
    /// bureau ni focus à voler.</summary>
    public static class Program
    {
        public static string[] Args = new string[0];

        [STAThread]
        public static int Main(string[] args)
        {
            Args = args ?? new string[0];
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(Args);
        }

        /// <summary>La configuration Avalonia (aussi lue par le concepteur et
        /// les sondes headless). Inter : la même police d'interface sur les
        /// trois OS.</summary>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }

    /// <summary>Ce que la ligne de commande demande.</summary>
    public sealed class Launch
    {
        public string PlotPath;     // un projet à ouvrir
        public string CapturePath;  // rendre la fenêtre en PNG puis quitter
        public bool Demo;           // un projet d'exemple en mémoire (sondes, captures)

        public static Launch Parse(string[] args)
        {
            var launch = new Launch();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "--capture" && i + 1 < args.Length) { launch.CapturePath = args[++i]; continue; }
                if (arg == "--demo") { launch.Demo = true; continue; }
                if (arg.StartsWith("--", StringComparison.Ordinal)) continue;
                if (File.Exists(arg)) launch.PlotPath = Path.GetFullPath(arg);
            }
            return launch;
        }
    }
}

using System;
using System.IO;
using Avalonia;

namespace Marabook.App
{
    /// <summary>Le point d'entrée de Marabook sur Avalonia (P1). Arguments :
    /// un chemin .plot (ouvert au démarrage) ; pour les sondes et captures :
    /// « --demo » (un projet d'exemple en mémoire), « --capture &lt;png&gt; »
    /// (la fenêtre rendue en PNG une fois posée, puis quitte), « --probe »
    /// (les vérifications de la sonde, OK/ÉCHEC sur la sortie, code de retour
    /// = échecs), « --dark », « --settings &lt;fichier&gt; ». Toute exécution
    /// de sonde ou de capture lit des réglages NEUFS dans un fichier
    /// temporaire : jamais ceux de l'utilisateur.</summary>
    public static class Program
    {
        public static string[] Args = new string[0];

        [STAThread]
        public static int Main(string[] args)
        {
            Args = args ?? new string[0];
            // Les .plot s'ouvrent d'un double-clic si rien ne les ouvrait (22/09) —
            // jamais depuis une sonde ou une capture.
            if (!Launch.Parse(Args).Isolated) FileAssociation.EnsureRegistered();
            var code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(Args);
            return Probes.Failures > 0 ? Probes.Failures : code;
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
        public bool Probe;          // la sonde en place, puis quitter
        public bool Dark;           // thème sombre forcé (captures)
        public bool Prefs;          // la capture montre les Préférences
        public string OpenTitle;    // --open <titre> : l'élément ouvert avant la capture (« journal » = le Journal)
        public bool UpdateRolledBack; // --maj-annulee : le script de mise à jour a remis l'ancienne version
        public int PrefsTab = -1;   // …ouvertes sur cet onglet (--tab N)
        public bool Lab;            // la capture montre la fenêtre de diagnostic du rendu
        public double Scale = 1;    // l'échelle de la capture (2 = pixels doublés)
        public string SettingsPath; // un settings.json à part

        public bool Isolated { get { return Demo || Probe || CapturePath != null; } }

        public static Launch Parse(string[] args)
        {
            var launch = new Launch();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "--capture" && i + 1 < args.Length) { launch.CapturePath = args[++i]; continue; }
                if (arg == "--settings" && i + 1 < args.Length) { launch.SettingsPath = args[++i]; continue; }
                if (arg == "--demo") { launch.Demo = true; continue; }
                if (arg == "--probe") { launch.Probe = true; continue; }
                if (arg == "--dark") { launch.Dark = true; continue; }
                if (arg == "--prefs") { launch.Prefs = true; continue; }
                if (arg == "--maj-annulee") { launch.UpdateRolledBack = true; continue; }
                if (arg == "--open" && i + 1 < args.Length) { launch.OpenTitle = args[++i]; continue; }
                if (arg == "--tab" && i + 1 < args.Length) { int.TryParse(args[++i], out launch.PrefsTab); launch.Prefs = true; continue; }
                if (arg == "--lab") { launch.Lab = true; continue; }
                if (arg == "--scale" && i + 1 < args.Length) { double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out launch.Scale); continue; }
                if (arg.StartsWith("--", StringComparison.Ordinal)) continue;
                if (File.Exists(arg)) launch.PlotPath = Path.GetFullPath(arg);
            }
            if (launch.SettingsPath == null && launch.Isolated)
                launch.SettingsPath = Path.Combine(Path.GetTempPath(), "marabook-sonde-" + Guid.NewGuid().ToString("N") + ".json");
            return launch;
        }
    }
}

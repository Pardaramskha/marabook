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
    /// = échecs), « --save-probe » (enregistre le projet ouvert, ou un projet
    /// neuf, après chaque vue : OK / ERREUR + pile), « --dark », « --settings
    /// &lt;fichier&gt; ». Toute exécution
    /// de sonde ou de capture lit des réglages NEUFS dans un fichier
    /// temporaire : jamais ceux de l'utilisateur.</summary>
    public static class Program
    {
        public static string[] Args = new string[0];

        [STAThread]
        public static int Main(string[] args)
        {
            Args = args ?? new string[0];
            // « --reinitialiser-styles-globaux » (28/09) : les styles globaux
            // des réglages reviennent aux défauts de Marabook (nouvelle
            // empreinte : chaque projet les reprend à l'ouverture), sans
            // ouvrir l'application — un outil de support.
            if (Array.IndexOf(Args, "--reinitialiser-styles-globaux") >= 0)
            {
                var launch = Launch.Parse(Args);
                AppPlatform.Install();
                if (launch.SettingsPath != null) Settings.AppSettings.PathOverride = launch.SettingsPath;
                Settings.AppSettings.Load();
                Settings.AppSettings.GlobalStyles = Model.StyleSheet.CreateDefault();
                Settings.GlobalStyles.PushFromSettings(null); // empreinte neuve + enregistrement
                Console.WriteLine("Styles globaux remis aux défauts de Marabook.");
                return 0;
            }
            // « --convertir-dictionnaire <fichier> » (30/09) : le script de
            // migration d'un dictionnaire EXPORTÉ (.json Marabook, ou liste
            // .txt/.dic) vers le format à types, natures et flexion — écrit en
            // .json à côté, l'original gardé en .bak. Les .plot et les réglages
            // se convertissent d'eux-mêmes à l'ouverture (LexiconEntry.Migrate).
            var convertAt = Array.IndexOf(Args, "--convertir-dictionnaire");
            if (convertAt >= 0)
            {
                if (convertAt + 1 >= Args.Length) { Console.Error.WriteLine("Usage : --convertir-dictionnaire <fichier.json|.txt|.dic>"); return 2; }
                var source = Args[convertAt + 1];
                try
                {
                    var entries = Model.LexiconExchange.Read(source);
                    System.IO.File.Copy(source, source + ".bak", true);
                    var target = System.IO.Path.ChangeExtension(source, ".json");
                    Model.LexiconExchange.Export(entries, target);
                    Console.WriteLine(entries.Count + " entrée(s) converties dans " + target + " ; "
                        + Model.LexiconEntry.CountNeedingReview(entries) + " à revoir (pastille « migration nécessaire ») ; original gardé : " + source + ".bak");
                    return 0;
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine("Conversion impossible : " + error.Message);
                    return 1;
                }
            }
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
        public string PinTitle;     // --pin <titre> : l'élément épinglé sur le côté avant la capture (hotfix 1.0.3-a)
        public bool PlanChart;      // --intensite : le plan ouvert montre son graphique d'intensité (hotfix 1.0.3-a)
        public bool SheetText;      // --texte-libre : la fiche ouverte montre son onglet Texte libre (hotfix 1.0.3-a)
        public string SelectTitle;  // --select <titre> : la tuile choisie dans le tableau ou la bibliothèque affichés (diagnostic de la sélection, 1.0.3)
        public string Tint;         // --tint #hex : la couleur posée sur cette tuile avant de la choisir
        public bool TintAfter;      // --tint-apres : …ou APRÈS l'avoir choisie (le tableau se rebâtit avec la carte sélectionnée)
        public bool UpdateRolledBack; // --maj-annulee : le script de mise à jour a remis l'ancienne version
        public int PrefsTab = -1;   // …ouvertes sur cet onglet (--tab N)
        public bool Lab;            // la capture montre la fenêtre de diagnostic du rendu
        public double Scale = 1;    // l'échelle de la capture (2 = pixels doublés)
        public string SettingsPath; // un settings.json à part
        public bool SaveProbe;      // --save-probe : ouvre le .plot donné, le marque modifié, l'enregistre (silencieux), dit OK ou la pile, quitte
        public string FontProbe;    // --police <famille> : dit comment la face se résout (graisse, simulations), puis quitte (29/09)
        public bool UpdateProbe;    // --maj-test : interroge GitHub, télécharge et déballe la dernière release comme au lancement, dit le résultat, quitte (01/10)

        public bool Isolated { get { return Demo || Probe || CapturePath != null || SaveProbe || FontProbe != null || UpdateProbe; } }

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
                if (arg == "--select" && i + 1 < args.Length) { launch.SelectTitle = args[++i]; continue; }
                if (arg == "--pin" && i + 1 < args.Length) { launch.PinTitle = args[++i]; continue; }
                if (arg == "--texte-libre") { launch.SheetText = true; continue; }
                if (arg == "--intensite") { launch.PlanChart = true; continue; }
                if (arg == "--tint" && i + 1 < args.Length) { launch.Tint = args[++i]; continue; }
                if (arg == "--tint-apres") { launch.TintAfter = true; continue; }
                if (arg == "--tab" && i + 1 < args.Length) { int.TryParse(args[++i], out launch.PrefsTab); launch.Prefs = true; continue; }
                if (arg == "--lab") { launch.Lab = true; continue; }
                if (arg == "--save-probe") { launch.SaveProbe = true; continue; }
                if (arg == "--police" && i + 1 < args.Length) { launch.FontProbe = args[++i]; continue; } // diagnostic : comment la face se résout (29/09)
                if (arg == "--maj-test") { launch.UpdateProbe = true; continue; } // diagnostic de la mise à jour (01/10)
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

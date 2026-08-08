using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UniversSale.Correction.Grammalecte
{
    public enum BridgeState { Idle, Starting, Ready, Unavailable }

    /// <summary>Une erreur telle que le pont la rapporte — offsets en POINTS
    /// DE CODE du texte envoyé (la conversion appartient à OffsetMapper).</summary>
    public sealed class BridgeError
    {
        public int Start;
        public int End;
        public string RuleId = "";
        public string Option = "";
        public string Message = "";
        public List<string> Suggestions = new List<string>();
    }

    /// <summary>Le pilotage du sous-processus Grammalecte (batch 29, lot B) :
    /// un processus Python LONG qui lit une requête JSON par ligne sur stdin
    /// et rend une réponse JSON par ligne sur stdout — l'initialisation
    /// (~1 s) se paie une fois, jamais de socket (doctrine).
    ///
    /// Les deux façons classiques de rater ce patron (amendement A2), et
    /// leurs parades ici :
    /// - ENCODAGE : PYTHONIOENCODING=utf-8 posé dans l'environnement ET les
    ///   flux reconfigurés dans le script ; côté C#, .NET 4.8 n'a pas de
    ///   StandardInputEncoding — on écrit soi-même de l'UTF-8 sans BOM sur
    ///   le BaseStream. Sans cela, les accents passent en cp1252 en silence.
    /// - AFFLEUREMENT : flush après chaque écriture des deux côtés, lecture
    ///   sur des THREADS dédiés (stdout ET stderr — un stderr jamais lu
    ///   remplit son tampon et fige le fils de façon non déterministe).
    ///
    /// Toute ligne inanalysable, tout timeout (le risque réel : un ReDoS du
    /// moteur de règles sur un texte pathologique), toute mort du processus
    /// → les demandes en vol échouent (le vérificateur se tait), le
    /// processus est tué et relancé À LA PROCHAINE demande. Trois démarrages
    /// ratés d'affilée → Unavailable, plus aucune tentative (pas de boîte
    /// d'erreur à chaque frappe — un état lisible, c'est tout).</summary>
    public sealed class GrammalecteBridge : IDisposable
    {
        /// <summary>Le délai SANS PROGRÈS avant de tuer le fils (attrapé par
        /// la sonde UI, batch 29) : un timeout PAR REQUÊTE compté depuis la
        /// mise en file était faux — quarante demandes s'empilent derrière
        /// une initialisation à froid (compilation des règles au premier
        /// lancement) et les minuteurs de queue expiraient pendant que
        /// Python progressait normalement… puis le premier timeout TUAIT un
        /// processus sain. Le chien de garde ne court que s'il y a des
        /// demandes en vol ET qu'aucune ligne n'arrive ; chaque réponse le
        /// réarme. Le ReDoS reste couvert : un paragraphe pathologique =
        /// plus aucun progrès = mort au bout de ce délai.</summary>
        public int TimeoutMilliseconds = 30000;

        private readonly object _gate = new object();
        private Process _process;
        private Stream _input;            // le tube stdin BRUT du fils
        private Thread _reader;
        private BridgeState _state = BridgeState.Idle;
        private string _stateDetail = "";
        private string _version = "";
        private int _failedStarts;
        private int _nextId;
        private bool _disposed;

        private sealed class Flight
        {
            public TaskCompletionSource<List<BridgeError>> Source;
        }
        private readonly Dictionary<int, Flight> _flights
            = new Dictionary<int, Flight>();
        // LE chien de garde (un seul pour le pont) : armé quand des vols
        // attendent, réarmé par chaque ligne reçue, désarmé quand le vol est
        // vide. Le jeton de garde invalide un tir tardif après réarmement.
        private Timer _watchdog;
        private int _watchdogToken;

        /// <summary>La version du runtime embarqué (lot E) — pour l'À propos
        /// et APPROVISIONNEMENT.md ; à tenir avec chaque corrective.</summary>
        public const string EmbeddedPythonVersion = "3.13.15";

        public BridgeState State { get { lock (_gate) return _state; } }
        /// <summary>« Python absent », « processus en échec »… — l'état
        /// lisible du lot D, jamais une boîte d'erreur.</summary>
        public string StateDetail { get { lock (_gate) return _stateDetail; } }
        public string Version { get { lock (_gate) return _version; } }

        /// <summary>L'état a changé (Starting → Ready, mort, indisponible) —
        /// levé sur un thread quelconque, au consommateur de marshaler.</summary>
        public event Action StateChanged;

        private static string BaseFolder
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string PythonPath
        {
            get { return Path.Combine(BaseFolder, Path.Combine("python", "python.exe")); }
        }

        public static string ScriptPath
        {
            get { return Path.Combine(BaseFolder, Path.Combine("grammalecte", "marabook_grammar.py")); }
        }

        /// <summary>Vérifie un texte (un PARAGRAPHE, déjà NFC). La tâche
        /// échoue si le pont est indisponible, si la requête expire ou si le
        /// processus meurt — l'appelant (GrammarChecker) se tait alors.</summary>
        public Task<List<BridgeError>> CheckAsync(string text,
            Dictionary<string, object> options, CancellationToken token)
        {
            var source = new TaskCompletionSource<List<BridgeError>>();
            int id;
            lock (_gate)
            {
                if (_disposed || _state == BridgeState.Unavailable)
                {
                    source.SetException(new InvalidOperationException(
                        "Grammalecte indisponible : " + _stateDetail));
                    return source.Task;
                }
                if (_process == null && !StartLocked())
                {
                    source.SetException(new InvalidOperationException(
                        "Grammalecte indisponible : " + _stateDetail));
                    return source.Task;
                }
                id = _nextId++;
                _flights[id] = new Flight { Source = source };
                // Le chien de garde s'arme au premier vol ; il n'est PAS
                // réarmé par les demandes suivantes (seul le PROGRÈS — une
                // ligne reçue — le réarme, sinon un flot de demandes
                // masquerait un moteur figé).
                if (_flights.Count == 1) ArmWatchdogLocked();
                var request = new Dictionary<string, object>
                {
                    { "id", id },
                    { "text", text }
                };
                if (options != null) request["options"] = options;
                try
                {
                    WriteLineLocked(UniversSale.Json.Write(request));
                }
                catch (Exception failure)
                {
                    _flights.Remove(id);
                    if (_flights.Count == 0) DisarmWatchdogLocked();
                    source.SetException(failure);
                    KillLocked("écriture impossible (processus mort ?)");
                    return source.Task;
                }
            }
            if (token.CanBeCanceled)
                token.Register(delegate { source.TrySetCanceled(); });
            return source.Task;
        }

        /// <summary>Démarre le fils — sous verrou. Faux si Python ou le
        /// script manquent (état Unavailable, silencieux).</summary>
        private bool StartLocked()
        {
            if (_failedStarts >= 3)
            {
                SetStateLocked(BridgeState.Unavailable,
                    "trois démarrages en échec");
                return false;
            }
            if (!File.Exists(PythonPath))
            {
                SetStateLocked(BridgeState.Unavailable, "Python absent");
                return false;
            }
            if (!File.Exists(ScriptPath))
            {
                SetStateLocked(BridgeState.Unavailable, "Grammalecte absent");
                return false;
            }
            try
            {
                var info = new ProcessStartInfo
                {
                    // Pas de -I : l'embeddable est déjà isolé par son ._pth
                    // (qui fige sys.path), et -I ferait ignorer
                    // PYTHONIOENCODING (-E est inclus dans -I).
                    FileName = PythonPath,
                    Arguments = "\"" + ScriptPath + "\"",
                    WorkingDirectory = Path.GetDirectoryName(ScriptPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };
                info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                var process = Process.Start(info);
                _process = process;
                // .NET 4.8 n'offre pas StandardInputEncoding — et le BOM
                // s'invite par des chemins retors (attrapé par la sonde UI :
                // la PREMIÈRE requête mourait d'un « Unexpected UTF-8 BOM »
                // côté Python). Donc AUCUN écrivain intermédiaire : les
                // octets UTF-8 sans BOM sont posés sur le tube brut.
                _input = process.StandardInput.BaseStream;
                SetStateLocked(BridgeState.Starting, "initialisation…");
                _reader = new Thread(ReadLoop) { IsBackground = true };
                _reader.Start(process);
                // stderr : lu et jeté sur son propre fil — un tampon plein
                // fige le fils (A2).
                var drain = new Thread(delegate()
                {
                    try { process.StandardError.ReadToEnd(); }
                    catch { }
                }) { IsBackground = true };
                drain.Start();
                return true;
            }
            catch (Exception failure)
            {
                _failedStarts++;
                _process = null;
                _input = null;
                SetStateLocked(_failedStarts >= 3
                    ? BridgeState.Unavailable : BridgeState.Idle,
                    "démarrage impossible : " + failure.Message);
                return false;
            }
        }

        /// <summary>La boucle du fil lecteur : une ligne = un message. Toute
        /// ligne inanalysable est ignorée (« grammaire indisponible », jamais
        /// une tentative de rattrapage — A2) ; fin de flux = processus mort.</summary>
        private void ReadLoop(object state)
        {
            var process = (Process)state;
            StreamReader output;
            try { output = process.StandardOutput; }
            catch { return; }
            while (true)
            {
                string line;
                try { line = output.ReadLine(); }
                catch { break; }
                if (line == null) break; // le fils est mort
                if (line.Length == 0) continue;
                object parsed;
                try { parsed = UniversSale.Json.Parse(line); }
                catch { continue; } // trame illisible : ignorée
                var message = UniversSale.Json.AsObject(parsed);
                if (message == null) continue;
                if (message.ContainsKey("ready"))
                {
                    lock (_gate)
                    {
                        if (_process != process) continue;
                        _failedStarts = 0;
                        _version = UniversSale.Json.AsString(
                            UniversSale.Json.Field(message, "version")) ?? "";
                        SetStateLocked(BridgeState.Ready, "");
                        // La ligne « ready » EST un progrès : réarme.
                        if (_flights.Count > 0) ArmWatchdogLocked();
                        else DisarmWatchdogLocked();
                    }
                    RaiseStateChanged();
                    continue;
                }
                var id = UniversSale.Json.AsInt(
                    UniversSale.Json.Field(message, "id"), -1);
                if (id < 0) continue;
                Flight flight = null;
                lock (_gate)
                {
                    if (_flights.TryGetValue(id, out flight))
                        _flights.Remove(id);
                    // Une réponse = du progrès : le chien de garde repart
                    // pour les vols restants, ou se tait s'il n'y en a plus.
                    if (_flights.Count > 0) ArmWatchdogLocked();
                    else DisarmWatchdogLocked();
                }
                if (flight == null) continue; // annulée ou périmée : jetée
                if (message.ContainsKey("error"))
                {
                    flight.Source.TrySetException(new InvalidOperationException(
                        "Grammalecte : " + UniversSale.Json.AsString(
                            UniversSale.Json.Field(message, "error"))));
                    continue;
                }
                flight.Source.TrySetResult(ParseErrors(
                    UniversSale.Json.Field(message, "errors")));
            }
            // Mort du fils : toutes les demandes en vol échouent, le pont se
            // relancera À LA PROCHAINE demande (jamais en boucle ici).
            List<Flight> orphans;
            lock (_gate)
            {
                if (_process != process) return; // un remplaçant est déjà là
                orphans = new List<Flight>(_flights.Values);
                _flights.Clear();
                DisarmWatchdogLocked();
                _process = null;
                _input = null;
                if (_state != BridgeState.Unavailable)
                    SetStateLocked(BridgeState.Idle, "processus terminé");
            }
            foreach (var orphan in orphans)
                orphan.Source.TrySetException(new InvalidOperationException(
                    "Grammalecte : processus terminé"));
            RaiseStateChanged();
        }

        /// <summary>Le JSON du pont → des BridgeError. Une entrée mal formée
        /// est sautée, jamais interprétée de travers.</summary>
        public static List<BridgeError> ParseErrors(object errorsJson)
        {
            var errors = new List<BridgeError>();
            var list = UniversSale.Json.AsList(errorsJson);
            if (list == null) return errors;
            foreach (var item in list)
            {
                var entry = UniversSale.Json.AsObject(item);
                if (entry == null) continue;
                var start = UniversSale.Json.AsInt(
                    UniversSale.Json.Field(entry, "nStart"), -1);
                var end = UniversSale.Json.AsInt(
                    UniversSale.Json.Field(entry, "nEnd"), -1);
                if (start < 0 || end <= start) continue;
                var error = new BridgeError
                {
                    Start = start,
                    End = end,
                    RuleId = UniversSale.Json.AsString(
                        UniversSale.Json.Field(entry, "sRuleId")) ?? "",
                    Option = UniversSale.Json.AsString(
                        UniversSale.Json.Field(entry, "sType")) ?? "",
                    Message = UniversSale.Json.AsString(
                        UniversSale.Json.Field(entry, "sMessage")) ?? ""
                };
                var suggestions = UniversSale.Json.AsList(
                    UniversSale.Json.Field(entry, "aSuggestions"));
                if (suggestions != null)
                    foreach (var suggestion in suggestions)
                    {
                        var text = UniversSale.Json.AsString(suggestion);
                        if (!string.IsNullOrEmpty(text))
                            error.Suggestions.Add(text);
                    }
                errors.Add(error);
            }
            return errors;
        }

        /// <summary>Sous verrou : une ligne JSON en octets UTF-8 SANS BOM,
        /// LF final, affleurée (A2) — directement sur le tube.</summary>
        private void WriteLineLocked(string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            _input.Write(bytes, 0, bytes.Length);
            _input.Flush();
        }

        /// <summary>Sous verrou : (ré)arme le chien de garde. Un tir tardif
        /// d'un ancien armement est invalidé par le jeton.</summary>
        private void ArmWatchdogLocked()
        {
            _watchdogToken++;
            var token = _watchdogToken;
            if (_watchdog != null) _watchdog.Dispose();
            _watchdog = new Timer(delegate { OnWatchdog(token); }, null,
                TimeoutMilliseconds, System.Threading.Timeout.Infinite);
        }

        private void DisarmWatchdogLocked()
        {
            _watchdogToken++;
            if (_watchdog != null) { _watchdog.Dispose(); _watchdog = null; }
        }

        /// <summary>AUCUN progrès pendant tout le délai alors que des vols
        /// attendent : moteur figé (ReDoS) ou fils muet — on tue, les vols
        /// échouent, la prochaine demande relancera un processus sain.</summary>
        private void OnWatchdog(int token)
        {
            List<Flight> starved = null;
            lock (_gate)
            {
                if (token != _watchdogToken) return; // réarmé/désarmé depuis
                if (_flights.Count > 0)
                {
                    starved = new List<Flight>(_flights.Values);
                    _flights.Clear();
                }
                DisarmWatchdogLocked();
                KillLocked("timeout de vérification");
            }
            if (starved != null)
                foreach (var flight in starved)
                    flight.Source.TrySetException(new TimeoutException(
                        "Grammalecte : délai dépassé (aucun progrès)"));
            RaiseStateChanged();
        }

        private void KillLocked(string reason)
        {
            var process = _process;
            _process = null;
            _input = null;
            if (_state != BridgeState.Unavailable)
                SetStateLocked(BridgeState.Idle, reason);
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); }
            catch { }
        }

        private void SetStateLocked(BridgeState state, string detail)
        {
            _state = state;
            _stateDetail = detail;
        }

        private void RaiseStateChanged()
        {
            var handler = StateChanged;
            if (handler != null) handler();
        }

        /// <summary>Arrêt à la fermeture : demande de sortie propre (une
        /// ligne), puis Kill sans JAMAIS attendre.</summary>
        public void Dispose()
        {
            List<Flight> orphans;
            Process process;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                orphans = new List<Flight>(_flights.Values);
                _flights.Clear();
                DisarmWatchdogLocked();
                process = _process;
                _process = null;
                try
                {
                    if (_input != null) WriteLineLocked("{\"quit\": true}");
                }
                catch { }
                _input = null;
                SetStateLocked(BridgeState.Unavailable, "fermeture");
            }
            foreach (var orphan in orphans)
                orphan.Source.TrySetCanceled();
            if (process != null)
                try { if (!process.HasExited) process.Kill(); }
                catch { }
        }
    }
}

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

    /// <summary>Un relevé de STYLE tel que le pont le rapporte (batch 44) —
    /// offsets en points de code ; Kind = « adverb » (adverbe en -ment) ou
    /// « dull » (verbe terne, Lemma = l'infinitif).</summary>
    public sealed class StyleItem
    {
        public int Start;
        public int End;
        public string Kind = "";
        public string Word = "";
        public string Lemma = "";
    }

    /// <summary>Un groupe de synonymes (batch 44) : la nature (« Verbe »,
    /// « Nom »…), le lemme d'origine et les mots, déjà fléchis.</summary>
    public sealed class SynonymGroup
    {
        public string Pos = "";
        public string Lemma = "";
        public List<string> Words = new List<string>();
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
        private Thread _reader;
        // LE FIL D'ÉCRITURE (13/09/2026, premier gel de Marabook) : les
        // trames partent d'une file, écrites sur le tube par un fil dédié,
        // JAMAIS sous _gate et JAMAIS sur le fil de l'appelant. Avant : le
        // fil UI écrivait sous verrou ; un chapitre de 108 paragraphes
        // remplissait le tube stdin (4 Ko) pendant que Python, lui,
        // remplissait stdout de relevés de style — le fil lecteur, qui
        // prend _gate à chaque ligne, attendait le fil UI, Python attendait
        // le lecteur, le fil UI attendait Python : blocage croisé, AppHang.
        // La grammaire seule n'y tombait pas (réponses courtes) — le style
        // en produit dix fois plus par paragraphe.
        private readonly Queue<string> _outbox = new Queue<string>();
        // Un réveil PAR GÉNÉRATION de fil d'écriture (revue du 13/09) : un
        // ancien fil, réveillé à la mort de son processus, ne consomme jamais
        // le signal destiné au nouveau.
        private AutoResetEvent _outboxSignal;

        /// <summary>Ce qu'un fil d'écriture reçoit : SON processus et SON
        /// réveil — il s'arrête dès que le pont n'est plus sur ce processus.</summary>
        private sealed class WriterState
        {
            public Process Process;
            public AutoResetEvent Signal;
        }
        private BridgeState _state = BridgeState.Idle;
        private string _stateDetail = "";
        private string _version = "";
        private int _failedStarts;
        private int _nextId;
        private bool _disposed;

        private sealed class Flight
        {
            // Depuis le batch 44, un vol rend la TRAME entière (objet JSON) :
            // la grammaire y lit « errors », le style « findings », les
            // synonymes « groups » — le pont ne connaît pas les métiers.
            public TaskCompletionSource<Dictionary<string, object>> Source;
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
        public async Task<List<BridgeError>> CheckAsync(string text,
            Dictionary<string, object> options, CancellationToken token)
        {
            var request = new Dictionary<string, object> { { "text", text } };
            if (options != null) request["options"] = options;
            var message = await SendAsync(request, token).ConfigureAwait(false);
            return ParseErrors(UniversSale.Json.Field(message, "errors"));
        }

        /// <summary>L'étage STYLE (batch 44) : adverbes en -ment et verbes
        /// ternes d'un paragraphe, relevés par le dictionnaire morphologique
        /// de Grammalecte (marabook_style.py). Mêmes offsets en points de
        /// code, même silence en cas d'échec que la grammaire.</summary>
        public async Task<List<StyleItem>> AnalyzeStyleAsync(string text,
            Dictionary<string, object> options, CancellationToken token)
        {
            var request = new Dictionary<string, object> { { "style", text } };
            if (options != null) request["options"] = options;
            var message = await SendAsync(request, token).ConfigureAwait(false);
            return ParseStyleItems(UniversSale.Json.Field(message, "findings"));
        }

        /// <summary>Les synonymes d'un mot TEL QU'ÉCRIT (batch 44), fléchis
        /// comme lui par le pont (thésaurus + conjugueur de Grammalecte) —
        /// groupés par nature. Liste vide : mot inconnu du thésaurus.</summary>
        public async Task<List<SynonymGroup>> SynonymsAsync(string word,
            CancellationToken token)
        {
            var request = new Dictionary<string, object> { { "synonyms", word } };
            var message = await SendAsync(request, token).ConfigureAwait(false);
            return ParseSynonymGroups(UniversSale.Json.Field(message, "groups"));
        }

        /// <summary>Envoie UNE requête (l'id d'appariement est posé ici) et
        /// rend la trame de réponse entière. Toute la mécanique de vol, de
        /// chien de garde et de relance vit ici, une fois pour tous les
        /// métiers du pont.</summary>
        public Task<Dictionary<string, object>> SendAsync(
            Dictionary<string, object> request, CancellationToken token)
        {
            var source = new TaskCompletionSource<Dictionary<string, object>>();
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
                request["id"] = id;
                // Sérialisée AVANT d'enregistrer le vol : une requête
                // inécrivable (type non supporté — erreur de programmation)
                // échoue seule, sans rien laisser derrière elle.
                string line;
                try { line = UniversSale.Json.Write(request); }
                catch (Exception failure)
                {
                    source.SetException(failure);
                    return source.Task;
                }
                _flights[id] = new Flight { Source = source };
                // Le chien de garde s'arme au premier vol ; il n'est PAS
                // réarmé par les demandes suivantes (seul le PROGRÈS — une
                // ligne reçue — le réarme, sinon un flot de demandes
                // masquerait un moteur figé).
                if (_flights.Count == 1) ArmWatchdogLocked();
                EnqueueLocked(line);
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
                // octets UTF-8 sans BOM sont posés sur le tube brut — par
                // le fil d'écriture, hors verrou.
                _outbox.Clear();
                _outboxSignal = new AutoResetEvent(false);
                SetStateLocked(BridgeState.Starting, "initialisation…");
                _reader = new Thread(ReadLoop) { IsBackground = true };
                _reader.Start(process);
                var writer = new Thread(WriteLoop) { IsBackground = true };
                writer.Start(new WriterState { Process = process, Signal = _outboxSignal });
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
                flight.Source.TrySetResult(message);
            }
            // Mort du fils : toutes les demandes en vol échouent, le pont se
            // relancera À LA PROCHAINE demande (jamais en boucle ici).
            List<Flight> orphans;
            lock (_gate)
            {
                if (_process != process) return; // un remplaçant est déjà là
                orphans = DrainLocked();
                KillLocked("processus terminé");
            }
            Fail(orphans, new InvalidOperationException("Grammalecte : processus terminé"));
            RaiseStateChanged();
        }

        /// <summary>Sous verrou : retire TOUS les vols et les trames en
        /// attente, désarme le chien de garde — la séquence commune de la
        /// mort du fils, du timeout, de l'écriture impossible et de la
        /// fermeture. Les vols rendus sont à faire échouer HORS verrou.</summary>
        private List<Flight> DrainLocked()
        {
            var orphans = new List<Flight>(_flights.Values);
            _flights.Clear();
            _outbox.Clear();
            DisarmWatchdogLocked();
            return orphans;
        }

        private static void Fail(List<Flight> flights, Exception reason)
        {
            foreach (var flight in flights) flight.Source.TrySetException(reason);
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

        /// <summary>Les relevés de style du pont → des StyleItem (batch 44).
        /// Une entrée sans plage valide ou sans nature connue est sautée.</summary>
        public static List<StyleItem> ParseStyleItems(object itemsJson)
        {
            var items = new List<StyleItem>();
            var list = UniversSale.Json.AsList(itemsJson);
            if (list == null) return items;
            foreach (var entry in list)
            {
                var obj = UniversSale.Json.AsObject(entry);
                if (obj == null) continue;
                var start = UniversSale.Json.AsInt(UniversSale.Json.Field(obj, "nStart"), -1);
                var end = UniversSale.Json.AsInt(UniversSale.Json.Field(obj, "nEnd"), -1);
                if (start < 0 || end <= start) continue;
                var kind = UniversSale.Json.AsString(UniversSale.Json.Field(obj, "kind")) ?? "";
                if (kind != "adverb" && kind != "dull") continue;
                items.Add(new StyleItem
                {
                    Start = start,
                    End = end,
                    Kind = kind,
                    Word = UniversSale.Json.AsString(UniversSale.Json.Field(obj, "word")) ?? "",
                    Lemma = UniversSale.Json.AsString(UniversSale.Json.Field(obj, "lemma")) ?? ""
                });
            }
            return items;
        }

        /// <summary>Les groupes de synonymes du pont → des SynonymGroup
        /// (batch 44). Un groupe vide est sauté.</summary>
        public static List<SynonymGroup> ParseSynonymGroups(object groupsJson)
        {
            var groups = new List<SynonymGroup>();
            var list = UniversSale.Json.AsList(groupsJson);
            if (list == null) return groups;
            foreach (var entry in list)
            {
                var obj = UniversSale.Json.AsObject(entry);
                if (obj == null) continue;
                var group = new SynonymGroup
                {
                    Pos = UniversSale.Json.AsString(UniversSale.Json.Field(obj, "pos")) ?? "",
                    Lemma = UniversSale.Json.AsString(UniversSale.Json.Field(obj, "lemma")) ?? ""
                };
                var words = UniversSale.Json.AsList(UniversSale.Json.Field(obj, "words"));
                if (words != null)
                    foreach (var word in words)
                    {
                        var text = UniversSale.Json.AsString(word);
                        if (!string.IsNullOrEmpty(text) && !group.Words.Contains(text))
                            group.Words.Add(text);
                    }
                if (group.Words.Count > 0) groups.Add(group);
            }
            return groups;
        }

        /// <summary>Sous verrou : dépose une trame dans la file du fil
        /// d'écriture et le réveille. Ne bloque jamais.</summary>
        private void EnqueueLocked(string line)
        {
            _outbox.Enqueue(line);
            _outboxSignal.Set();
        }

        /// <summary>La boucle du fil d'écriture : une ligne JSON en octets
        /// UTF-8 SANS BOM, LF final, affleurée (A2) — directement sur le tube,
        /// HORS verrou (le tube peut bloquer quand Python est occupé : c'est
        /// ce fil qui attend, jamais l'appelant, jamais le lecteur). Une
        /// écriture impossible = processus mort : les vols échouent, le pont
        /// se relancera à la prochaine demande.</summary>
        private void WriteLoop(object state)
        {
            var writer = (WriterState)state;
            var process = writer.Process;
            Stream input;
            try { input = process.StandardInput.BaseStream; }
            catch { return; }
            while (true)
            {
                string line = null;
                lock (_gate)
                {
                    if (_process != process) return; // remplacé ou mort
                    if (_outbox.Count > 0) line = _outbox.Dequeue();
                }
                if (line == null)
                {
                    // Réveillé par EnqueueLocked, ou par la mort de SON
                    // processus (KillLocked signale) — l'AutoResetEvent
                    // retient un signal posé avant l'attente.
                    writer.Signal.WaitOne();
                    continue;
                }
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(line + "\n");
                    input.Write(bytes, 0, bytes.Length);
                    input.Flush();
                }
                catch
                {
                    List<Flight> orphans;
                    lock (_gate)
                    {
                        if (_process != process) return;
                        orphans = DrainLocked();
                        KillLocked("écriture impossible (processus mort ?)");
                    }
                    Fail(orphans, new InvalidOperationException("Grammalecte : écriture impossible"));
                    RaiseStateChanged();
                    return;
                }
            }
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
            List<Flight> starved;
            lock (_gate)
            {
                if (token != _watchdogToken) return; // réarmé/désarmé depuis
                starved = DrainLocked();
                KillLocked("timeout de vérification");
            }
            Fail(starved, new TimeoutException("Grammalecte : délai dépassé (aucun progrès)"));
            RaiseStateChanged();
        }

        /// <summary>Sous verrou : le pont quitte ce processus — le fil
        /// d'écriture est réveillé (il verra _process changé et sortira),
        /// stdin est fermé (Python sort de lui-même à la fin du flux, même
        /// si Kill échouait), puis Kill sans attendre.</summary>
        private void KillLocked(string reason)
        {
            var process = _process;
            _process = null;
            _outbox.Clear();
            if (_outboxSignal != null) _outboxSignal.Set();
            if (_state != BridgeState.Unavailable)
                SetStateLocked(BridgeState.Idle, reason);
            Terminate(process);
        }

        private static void Terminate(Process process)
        {
            if (process == null) return;
            try { process.StandardInput.Close(); }
            catch { }
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

        /// <summary>Arrêt à la fermeture : la sortie propre par « quit » a
        /// été abandonnée avec le fil d'écriture (elle pouvait bloquer sur
        /// un tube plein) — Kill, sans JAMAIS attendre ; le fils est un
        /// processus sans état, rien à perdre.</summary>
        public void Dispose()
        {
            List<Flight> orphans;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                orphans = DrainLocked();
                KillLocked("fermeture");
                SetStateLocked(BridgeState.Unavailable, "fermeture");
            }
            foreach (var orphan in orphans)
                orphan.Source.TrySetCanceled();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniversSale.Correction.Grammalecte
{
    /// <summary>Ce que l'interface reçoit pour un mot (revue du 13/09) : les
    /// mots PRÊTS, ou null tant que la recherche court, et une notice quand
    /// il n'y a rien à montrer (mot inconnu, thésaurus indisponible). Sans
    /// WPF — le menu et le panneau ne font qu'afficher.</summary>
    public sealed class SynonymAnswer
    {
        /// <summary>Null : pas encore là (demandé). Vide : rien à proposer,
        /// Notice dit pourquoi.</summary>
        public List<string> Words;
        public string Notice = "";

        public bool Pending { get { return Words == null; } }
    }

    /// <summary>Les synonymes À LA DEMANDE (batch 44) : un mémo par mot tel
    /// qu'écrit (la flexion compte — « chevaux » et « cheval » sont deux
    /// entrées), une demande au pont par mot inconnu, jamais deux fois la
    /// même en vol, et un événement à l'arrivée pour que l'interface se
    /// repeigne — exactement le registre des suggestions d'orthographe :
    /// le menu ou le panneau demandent, affichent ce qui est PRÊT, et se
    /// remplissent quand la réponse tombe. Le fil UI n'attend JAMAIS le
    /// pont. Un ÉCHEC est mémorisé lui aussi (revue du 13/09) : sans cela,
    /// un pont indisponible laissait « Recherche… » à jamais et chaque
    /// repeinture du panneau relançait cent demandes vouées à l'échec ;
    /// Reset() (le pont redevenu prêt) rouvre la porte. Sans WPF : la
    /// mécanique se teste en console (C22) sur une recherche factice.</summary>
    public sealed class SynonymProvider
    {
        public delegate Task<List<SynonymGroup>> Lookup(string word, CancellationToken token);

        public enum LookupState { Unknown, Pending, Ready, Failed }

        private readonly Lookup _lookup;
        private readonly object _gate = new object();
        private readonly Dictionary<string, List<SynonymGroup>> _cache
            = new Dictionary<string, List<SynonymGroup>>();
        private readonly HashSet<string> _pending = new HashSet<string>();
        private readonly HashSet<string> _failed = new HashSet<string>();
        private string _failure = "";
        private CancellationTokenSource _cancel = new CancellationTokenSource();

        /// <summary>Plafond du mémo — bien au-delà d'une session d'écriture ;
        /// au plafond, on repart de zéro.</summary>
        private const int CacheCap = 5000;

        /// <summary>Une réponse (ou un échec) vient d'arriver pour ce mot —
        /// LEVÉ SUR UN THREAD DU POOL, au consommateur WPF de passer par son
        /// Dispatcher.</summary>
        public event Action<string> Arrived;

        public SynonymProvider(Lookup lookup)
        {
            _lookup = lookup;
        }

        public static SynonymProvider For(GrammalecteBridge bridge)
        {
            return new SynonymProvider(bridge.SynonymsAsync);
        }

        public LookupState StateOf(string word)
        {
            if (string.IsNullOrEmpty(word)) return LookupState.Ready;
            lock (_gate)
            {
                if (_cache.ContainsKey(word)) return LookupState.Ready;
                if (_pending.Contains(word)) return LookupState.Pending;
                if (_failed.Contains(word)) return LookupState.Failed;
                return LookupState.Unknown;
            }
        }

        /// <summary>Les groupes connus pour ce mot, ou null s'ils ne sont
        /// pas (encore) là. Une liste VIDE est une réponse : mot inconnu.</summary>
        public List<SynonymGroup> Cached(string word)
        {
            if (string.IsNullOrEmpty(word)) return new List<SynonymGroup>();
            lock (_gate)
            {
                List<SynonymGroup> groups;
                return _cache.TryGetValue(word, out groups) ? groups : null;
            }
        }

        /// <summary>Les synonymes à plat (toutes natures confondues, sans
        /// doublon, plafonnés), ou null si pas encore connus.</summary>
        public List<string> Flat(string word, int cap)
        {
            var groups = Cached(word);
            if (groups == null) return null;
            var flat = new List<string>();
            foreach (var group in groups)
                foreach (var candidate in group.Words)
                {
                    if (flat.Count >= cap) return flat;
                    if (!flat.Contains(candidate)) flat.Add(candidate);
                }
            return flat;
        }

        /// <summary>LA réponse pour l'interface : prête, en attente, ou une
        /// notice. requestIfUnknown faux : on ne déclenche rien (le pont
        /// n'est pas en service, on n'allume pas Python pour un bouton du
        /// panneau) — la réponse est alors vide et muette.</summary>
        public SynonymAnswer Answer(string word, int cap, bool requestIfUnknown)
        {
            var answer = new SynonymAnswer();
            switch (StateOf(word))
            {
                case LookupState.Ready:
                    answer.Words = Flat(word, cap);
                    if (answer.Words.Count == 0) answer.Notice = "Aucun synonyme connu";
                    return answer;
                case LookupState.Failed:
                    answer.Words = new List<string>();
                    answer.Notice = "Thésaurus indisponible"
                        + (_failure.Length > 0 ? " (" + _failure + ")" : "");
                    return answer;
                case LookupState.Pending:
                    return answer;
                default:
                    if (!requestIfUnknown)
                    {
                        answer.Words = new List<string>();
                        return answer;
                    }
                    Request(word);
                    return StateOf(word) == LookupState.Pending ? answer : Answer(word, cap, false);
            }
        }

        public bool IsPending(string word)
        {
            lock (_gate) return _pending.Contains(word);
        }

        public int PendingCount
        {
            get { lock (_gate) return _pending.Count; }
        }

        /// <summary>Demande les synonymes d'un mot s'ils ne sont ni connus,
        /// ni en vol, ni en échec. L'échec est mémorisé jusqu'à Reset().</summary>
        public void Request(string word)
        {
            if (string.IsNullOrEmpty(word)) return;
            CancellationToken token;
            lock (_gate)
            {
                if (_cache.ContainsKey(word) || _failed.Contains(word) || !_pending.Add(word)) return;
                token = _cancel.Token;
            }
            Task<List<SynonymGroup>> task = null;
            Exception immediate = null;
            try { task = _lookup(word, token); }
            catch (Exception failure) { immediate = failure; }
            if (task == null)
            {
                Settle(word, null, immediate ?? new InvalidOperationException("recherche impossible"));
                return;
            }
            task.ContinueWith(delegate(Task<List<SynonymGroup>> done)
            {
                var ignored = done.Exception; // observée : le pont se tait
                Settle(word,
                    done.Status == TaskStatus.RanToCompletion ? done.Result : null,
                    done.Status == TaskStatus.Canceled ? null
                        : done.Exception != null ? done.Exception.GetBaseException()
                        : new InvalidOperationException("réponse absente"));
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>Range la réponse (ou l'échec) et prévient. Une tâche
        /// ANNULÉE (fermeture) ne laisse rien : ni mémo, ni échec.</summary>
        private void Settle(string word, List<SynonymGroup> groups, Exception failure)
        {
            lock (_gate)
            {
                _pending.Remove(word);
                if (groups != null)
                {
                    if (_cache.Count >= CacheCap) _cache.Clear();
                    _cache[word] = groups;
                }
                else if (failure != null)
                {
                    _failed.Add(word);
                    _failure = failure.Message ?? "";
                }
                else return; // annulée
            }
            var handler = Arrived;
            if (handler != null) handler(word);
        }

        /// <summary>Le pont est redevenu prêt : les échecs mémorisés sont
        /// oubliés, une prochaine demande retentera.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _failed.Clear();
                _failure = "";
            }
        }

        /// <summary>Annule le vol (fermeture du projet, sortie) — le mémo
        /// reste, il ne dépend pas du texte.</summary>
        public void Cancel()
        {
            CancellationTokenSource retired;
            lock (_gate)
            {
                retired = _cancel;
                _cancel = new CancellationTokenSource();
                _pending.Clear();
            }
            retired.Cancel();
        }
    }
}

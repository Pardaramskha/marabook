using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversSale.Model;

namespace UniversSale.Correction
{
    /// <summary>Le pilote de correction : exécute les vérificateurs actifs,
    /// agrège, trie par position, et filtre — plages NoProof, mots ignorés
    /// dans le projet (persistés au .plot), mots ignorés partout (réglages),
    /// signalements ignorés « ici » (session seulement : une position exacte
    /// ne survit pas honnêtement aux éditions, tranché et documenté).
    /// Aucune dépendance WPF.</summary>
    public class CheckerHost
    {
        private readonly List<IChecker> _checkers = new List<IChecker>();

        /// <summary>« Ignorer dans ce projet » — brancher Project.ProofIgnored
        /// (la liste vit et se persiste avec le projet).</summary>
        public List<string> ProjectIgnored = new List<string>();

        /// <summary>« Ignorer partout » — brancher AppSettings.ProofIgnored.</summary>
        public List<string> GlobalIgnored = new List<string>();

        // « Ignorer ici » : clé règle|paragraphe|début|mot, session seulement.
        private readonly HashSet<string> _here = new HashSet<string>();

        public List<IChecker> Checkers { get { return _checkers; } }

        public void Add(IChecker checker)
        {
            // Un différé est forcément local au paragraphe : c'est le cache
            // d'empreinte qui arbitre la péremption de ses réponses.
            if (checker is IDeferredChecker
                && checker.Scope != CheckerScope.ParagraphLocal)
                throw new InvalidOperationException(
                    "Un vérificateur différé doit être ParagraphLocal.");
            _checkers.Add(checker);
        }

        // Cache des vérificateurs LOCAUX (batch 27, lot B) : par
        // (vérificateur, index de paragraphe), l'empreinte du texte plat et
        // les signalements produits — un paragraphe inchangé n'est jamais
        // revérifié. Les vérificateurs globaux (répétitions) repassent
        // entiers à chaque cycle, par nature.
        private sealed class CacheEntry
        {
            public long Fingerprint;
            public List<Finding> Findings;
        }
        private readonly Dictionary<string, CacheEntry> _cache
            = new Dictionary<string, CacheEntry>();
        private int _cachedParagraphCount;

        // ---- le versant DIFFÉRÉ (batch 29, lot A) --------------------------
        // Les continuations arrivent sur un thread du pool pendant que le fil
        // UI relance Run() : toutes les structures partagées passent sous un
        // même verrou, à grain grossier — chaque section est minuscule.
        private readonly object _gate = new object();
        // Demandes en vol, clé « vérificateur|paragraphe|empreinte » : une
        // même demande n'est jamais relancée tant qu'elle court.
        private readonly HashSet<string> _pending = new HashSet<string>();
        // La DERNIÈRE empreinte demandée par (vérificateur|paragraphe) :
        // c'est elle qui décide de jeter une réponse périmée — si le
        // paragraphe a changé depuis la demande, l'empreinte au retour ne
        // correspond plus, la réponse est ignorée.
        private readonly Dictionary<string, long> _latestRequest
            = new Dictionary<string, long>();
        // La génération de connaissance : InvalidateCache et CancelDeferred
        // l'incrémentent — une réponse partie sous l'ancienne génération est
        // jetée MÊME si le texte n'a pas bougé (elle a été calculée avec une
        // connaissance ou des options périmées), et la demande se relance.
        private int _generation;
        private CancellationTokenSource _cancel = new CancellationTokenSource();

        /// <summary>Des résultats différés viennent d'être admis au cache —
        /// l'interface relance Run() (peu coûteux : tout est en cache) et
        /// repeint. LEVÉ SUR UN THREAD DU POOL : au consommateur WPF de
        /// passer par son Dispatcher, une fois par lot, jamais par
        /// signalement (le pilote reste sans WPF, testable console).</summary>
        public event Action DeferredArrived;

        /// <summary>Nombre de demandes différées en vol — l'état « analyse
        /// en cours » du panneau Correction (lot D).</summary>
        public int PendingDeferred
        {
            get { lock (_gate) return _pending.Count; }
        }

        /// <summary>Annule tout le différé en vol : changement d'écrit,
        /// fermeture du projet, sortie de l'application. N'attend rien — les
        /// tâches encore en route verront leur réponse jetée (leur clé de
        /// demande a disparu).</summary>
        public void CancelDeferred()
        {
            CancellationTokenSource retired;
            lock (_gate)
            {
                retired = _cancel;
                _cancel = new CancellationTokenSource();
                _pending.Clear();
                _latestRequest.Clear();
                _generation++;
            }
            retired.Cancel();
        }

        /// <summary>Tous les signalements du document, triés par position,
        /// filtrés (NoProof, ignorés). C'est LA sortie du pilote.</summary>
        public List<Finding> Run(TextDocument document, StyleSheet styles)
        {
            var findings = new List<Finding>();
            foreach (var checker in _checkers)
                if (checker.Scope == CheckerScope.WholeDocument)
                    findings.AddRange(checker.Check(document, styles));
                else
                    findings.AddRange(RunLocal(checker, document, styles));
            var kept = new List<Finding>();
            foreach (var finding in findings)
                if (!IsFiltered(document, finding)) kept.Add(finding);
            // Tri TOTAL (batch 27, lot 0.4) : List.Sort est un introsort
            // INSTABLE — deux signalements au même (paragraphe, offset), cas
            // normal en typographie, s'ordonnaient arbitrairement. Le
            // départage descend jusqu'à la longueur pour que deux passes
            // rendent toujours le même ordre.
            kept.Sort(delegate(Finding a, Finding b)
            {
                if (a.ParagraphIndex != b.ParagraphIndex)
                    return a.ParagraphIndex.CompareTo(b.ParagraphIndex);
                if (a.Start != b.Start) return a.Start.CompareTo(b.Start);
                var checker = string.CompareOrdinal(a.CheckerId, b.CheckerId);
                if (checker != 0) return checker;
                var rule = string.CompareOrdinal(a.RuleId, b.RuleId);
                if (rule != 0) return rule;
                return a.Length.CompareTo(b.Length);
            });
            return kept;
        }

        /// <summary>La passe d'un vérificateur LOCAL sous cache : seuls les
        /// paragraphes dont l'empreinte a changé sont revérifiés ; les
        /// signalements mis en cache portent des offsets locaux, leur
        /// ParagraphIndex est (re)posé ici — un paragraphe qui se déplace
        /// dans le document garde son cache tant que son texte est le même
        /// à l'index près.</summary>
        private List<Finding> RunLocal(IChecker checker, TextDocument document,
            StyleSheet styles)
        {
            lock (_gate)
            {
                // Le document a rétréci : les entrées au-delà meurent.
                if (document.Paragraphs.Count < _cachedParagraphCount)
                {
                    var stale = new List<string>();
                    foreach (var key in _cache.Keys)
                    {
                        var separator = key.LastIndexOf('|');
                        int index;
                        if (int.TryParse(key.Substring(separator + 1), out index)
                            && index >= document.Paragraphs.Count)
                            stale.Add(key);
                    }
                    foreach (var key in stale) _cache.Remove(key);
                    stale.Clear();
                    foreach (var key in _latestRequest.Keys)
                    {
                        var separator = key.LastIndexOf('|');
                        int index;
                        if (int.TryParse(key.Substring(separator + 1), out index)
                            && index >= document.Paragraphs.Count)
                            stale.Add(key);
                    }
                    foreach (var key in stale) _latestRequest.Remove(key);
                }
                _cachedParagraphCount = document.Paragraphs.Count;
            }

            var deferred = checker as IDeferredChecker;
            var results = new List<Finding>();
            for (var p = 0; p < document.Paragraphs.Count; p++)
            {
                var paragraph = document.Paragraphs[p];
                var fingerprint = FingerprintOf(paragraph);
                var key = checker.Id + "|" + p;
                CacheEntry entry;
                bool hit;
                lock (_gate)
                    hit = _cache.TryGetValue(key, out entry)
                        && entry.Fingerprint == fingerprint;
                if (!hit)
                {
                    if (deferred != null)
                    {
                        // JAMAIS d'attente : la demande part, la passe
                        // courante se rend sans elle, la réponse fusionnera
                        // au cycle que DeferredArrived déclenchera.
                        RequestDeferred(deferred, paragraph, p, fingerprint,
                            styles);
                        continue;
                    }
                    entry = new CacheEntry
                    {
                        Fingerprint = fingerprint,
                        Findings = checker.CheckParagraph(paragraph, styles)
                            ?? new List<Finding>()
                    };
                    lock (_gate) _cache[key] = entry;
                }
                foreach (var finding in entry.Findings)
                {
                    finding.ParagraphIndex = p;
                    results.Add(finding);
                }
            }
            return results;
        }

        /// <summary>Lance une vérification différée d'UN paragraphe. Déduplication
        /// par (vérificateur, paragraphe, empreinte, génération) ; à l'arrivée,
        /// la réponse n'est admise au cache que si elle est encore la DERNIÈRE
        /// demandée (empreinte) ET de la génération courante — sinon elle est
        /// jetée en silence, comme l'est une tâche annulée ou en faute.</summary>
        private void RequestDeferred(IDeferredChecker checker,
            TextParagraph paragraph, int index, long fingerprint,
            StyleSheet styles)
        {
            var key = checker.Id + "|" + index;
            int generation;
            CancellationToken token;
            lock (_gate)
            {
                generation = _generation;
                var pendingKey = key + "|" + fingerprint + "|" + generation;
                if (!_pending.Add(pendingKey)) return; // déjà en vol
                _latestRequest[key] = fingerprint;
                token = _cancel.Token;
            }
            var flight = key + "|" + fingerprint + "|" + generation;
            Task<List<Finding>> task = null;
            try
            {
                task = checker.CheckParagraphAsync(paragraph, styles, token);
            }
            catch { }
            if (task == null)
            {
                // Le vérificateur a levé avant même de rendre sa tâche : il
                // se tait, la demande pourra repartir au prochain cycle.
                lock (_gate) _pending.Remove(flight);
                return;
            }
            task.ContinueWith(delegate(Task<List<Finding>> done)
            {
                var admitted = false;
                lock (_gate)
                {
                    _pending.Remove(flight);
                    long latest;
                    if (done.Status == TaskStatus.RanToCompletion
                        && done.Result != null
                        && generation == _generation
                        && _latestRequest.TryGetValue(key, out latest)
                        && latest == fingerprint)
                    {
                        _cache[key] = new CacheEntry
                        {
                            Fingerprint = fingerprint,
                            Findings = done.Result
                        };
                        admitted = true;
                    }
                }
                // Observer l'exception d'une tâche en faute (sinon le
                // finaliseur de Task la relève) — le vérificateur se tait.
                var ignored = done.Exception;
                if (admitted)
                {
                    var handler = DeferredArrived;
                    if (handler != null) handler();
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>FNV-1a du texte plat (+ nombre de runs). NoProof ne
        /// change pas l'empreinte À DESSEIN : le filtrage NoProof relit le
        /// document VIVANT après cache, jamais les signalements gelés.</summary>
        private static long FingerprintOf(TextParagraph paragraph)
        {
            var text = PivotEdit.FlatText(paragraph);
            unchecked
            {
                var hash = (long)1469598103934665603;
                foreach (var c in text)
                {
                    hash ^= c;
                    hash *= 1099511628211;
                }
                hash ^= paragraph.Runs.Count * 397;
                return hash;
            }
        }

        /// <summary>Vide le cache des vérificateurs locaux — OBLIGATOIRE
        /// quand leur CONNAISSANCE change sans que le texte change (mot
        /// enseigné au dictionnaire personnel) : l'empreinte du paragraphe
        /// est la même, le verdict ne l'est plus. Les ignorés, eux, sont
        /// filtrés APRÈS cache et ne demandent rien.</summary>
        public void InvalidateCache()
        {
            lock (_gate)
            {
                _cache.Clear();
                // Le différé en vol a été calculé avec la connaissance (ou
                // les options) d'avant : sa réponse est désormais périmée
                // même à texte inchangé — changement de génération, et les
                // demandes repartent au prochain cycle.
                _pending.Clear();
                _latestRequest.Clear();
                _generation++;
            }
        }

        /// <summary>« Ignorer ici » : ce signalement précis, cette session.
        /// Un déplacement du texte AVANT la plage change les offsets et fait
        /// réapparaître le signalement — limite assumée, testée en C5.</summary>
        public void IgnoreHere(Finding finding)
        {
            _here.Add(HereKey(finding));
        }

        /// <summary>« Ignorer dans ce projet » : le mot, insensible à la
        /// casse, pour tous les vérificateurs.</summary>
        public void IgnoreInProject(string word)
        {
            if (!ContainsWord(ProjectIgnored, word)) ProjectIgnored.Add(word);
        }

        public void IgnoreEverywhere(string word)
        {
            if (!ContainsWord(GlobalIgnored, word)) GlobalIgnored.Add(word);
        }

        private bool IsFiltered(TextDocument document, Finding finding)
        {
            if (_here.Contains(HereKey(finding))) return true;
            if (finding.Word.Length > 0
                && (ContainsWord(ProjectIgnored, finding.Word)
                    || ContainsWord(GlobalIgnored, finding.Word))) return true;
            return OverlapsNoProof(document, finding);
        }

        /// <summary>Vrai si la plage du signalement touche un run marqué
        /// « ne pas corriger » — mêmes unités plates que PivotEdit.</summary>
        private static bool OverlapsNoProof(TextDocument document, Finding finding)
        {
            if (finding.ParagraphIndex < 0
                || finding.ParagraphIndex >= document.Paragraphs.Count) return false;
            var cursor = 0;
            foreach (var run in document.Paragraphs[finding.ParagraphIndex].Runs)
            {
                var length = PivotEdit.IsElement(run) ? 1 : run.Text.Length;
                if (run.NoProof && cursor < finding.End && finding.Start < cursor + length)
                    return true;
                cursor += length;
            }
            return false;
        }

        private static string HereKey(Finding finding)
        {
            return finding.RuleId + "|" + finding.ParagraphIndex + "|"
                + finding.Start + "|" + finding.Word;
        }

        /// <summary>0.3 (batch 27) : la clé d'ignoré est LA MÊME normalisation
        /// que celle des vérificateurs — FrenchTokenizer.Fold (casse ET
        /// accents pliés). « Ignorer » COEUR fait taire cœur, Cœur et coeur ;
        /// OrdinalIgnoreCase ne pliait que la casse.</summary>
        private static bool ContainsWord(List<string> list, string word)
        {
            var key = FrenchTokenizer.Fold(word);
            foreach (var entry in list)
                if (FrenchTokenizer.Fold(entry) == key)
                    return true;
            return false;
        }
    }
}

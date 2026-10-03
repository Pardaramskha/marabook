using System;
using System.IO;
using System.Threading.Tasks;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.App
{
    /// <summary>La GARDE du .plot (1.0.3) — « la sécurité des écrits avant
    /// tout », pour les projets qui vivent dans un dossier synchronisé
    /// (OneDrive, Dropbox, NAS) ou passent d'une machine à l'autre :
    /// 1. l'EMPREINTE d'enregistrement (manifest « saveId ») est retenue à la
    ///    lecture et après chaque écriture ; avant d'enregistrer, le fichier
    ///    sur le disque est comparé — modifié en dehors de Marabook, on
    ///    DEMANDE (Écraser / Enregistrer sous… / Annuler) ; la sauvegarde
    ///    automatique, elle, se suspend et prévient d'un toast ; et l'écriture
    ///    elle-même revérifie juste avant d'échanger les fichiers ;
    /// 2. un VERROU « nom.plot.lock » dit sur quelle machine le projet est
    ///    ouvert ; un verrou étranger vivant à l'ouverture → « Ouvrir quand
    ///    même / Annuler » ; battement toutes les deux minutes, retiré à la
    ///    fermeture.</summary>
    public partial class MainWindow
    {
        private string _diskSaveId;  // l'empreinte du .plot tel qu'on l'a lu ou écrit ; null = jamais sur le disque
        private string _diskStamp;   // date + taille du fichier, pour ne pas rouvrir le zip à chaque fois
        private bool _conflictWarned; // le toast de la sauvegarde automatique : une fois par conflit
        private string _lockedPath;  // le .plot dont ce processus tient le verrou
        private int _lockTicks;      // battement : un tick du secours sur quatre (30 s × 4)

        private enum DiskState { Same, Changed, Missing }

        /// <summary>Le fichier vient d'être lu ou écrit : c'est lui, la référence.</summary>
        private void RememberDisk(string path, string saveId)
        {
            if (path == null) { _diskSaveId = null; _diskStamp = null; return; }
            _diskSaveId = saveId ?? "";
            _diskStamp = PlotFile.Stamp(path);
            _conflictWarned = false;
        }

        /// <summary>Le .plot a-t-il bougé en dehors de Marabook depuis la
        /// référence ? Même date et taille : non ; sinon on relit son
        /// empreinte — la même (le fichier a été « touché » par une
        /// synchronisation, pas changé) : non ; autre, ou absente : oui.</summary>
        private DiskState DiskStateOf(string path)
        {
            if (path == null || _diskSaveId == null) return DiskState.Same; // jamais écrit : rien à comparer
            if (!File.Exists(path)) return DiskState.Missing;
            var stamp = PlotFile.Stamp(path);
            if (stamp != null && stamp == _diskStamp) return DiskState.Same;
            var onDisk = PlotFile.ReadSaveId(path);
            if (!string.IsNullOrEmpty(onDisk) && onDisk == _diskSaveId)
            {
                _diskStamp = stamp;
                return DiskState.Same;
            }
            return DiskState.Changed;
        }

        /// <summary>Pour la sonde : « Same », « Changed » ou « Missing ».</summary>
        public string DiskStatePublic { get { return _path == null ? "" : DiskStateOf(_path).ToString(); } }

        /// <summary>Le conflit vu avant un enregistrement demandé : on
        /// explique et on laisse choisir.</summary>
        private async void ResolveConflictThenSave(DiskState state)
        {
            var name = Path.GetFileName(_path ?? "");
            string text;
            string[] choices;
            if (state == DiskState.Missing)
            {
                text = "Le fichier « " + name + " » n'est plus à son emplacement : il a été déplacé ou supprimé, "
                    + "peut-être par une synchronisation.\n\n"
                    + "Recréer l'enregistre à nouveau au même endroit ; Enregistrer sous… choisit un autre emplacement.";
                choices = new[] { "Annuler", "Enregistrer sous…", "Recréer" };
            }
            else
            {
                text = "Le fichier « " + name + " » a été modifié en dehors de Marabook depuis votre dernier enregistrement "
                    + "(synchronisation cloud, autre machine, autre programme).\n\n"
                    + "Écraser remplace cette version par la vôtre (l'autre est gardée en .bak) ; "
                    + "Enregistrer sous… conserve les deux.";
                choices = new[] { "Annuler", "Écraser", "Enregistrer sous…" };
            }
            var choice = await MessageDialog.ShowChoices(this, text, AppName, MessageIcon.Warning, choices, choices.Length - 1);
            if (choice <= 0) return;
            var label = choices[choice];
            if (label == "Enregistrer sous…") DoSaveAs();
            else SaveProject(false, true);
        }

        /// <summary>La sauvegarde automatique devant un fichier changé : elle
        /// se suspend (rien n'est écrasé en silence), un toast le dit une
        /// fois ; le secours continue d'écrire les textes.</summary>
        private void WarnAutosaveConflict(DiskState state)
        {
            if (_conflictWarned) return;
            _conflictWarned = true;
            var name = Path.GetFileName(_path ?? "");
            var body = state == DiskState.Missing
                ? "« " + name + " » n'est plus à son emplacement (déplacé ou supprimé, peut-être par une synchronisation)."
                : "« " + name + " » a été modifié en dehors de Marabook (synchronisation cloud, autre machine).";
            ShowNotice(NoticeToast.Build("warning-bold", "Le fichier a changé sur le disque",
                body + "\n\nLa sauvegarde automatique est suspendue pour ne rien écraser ; le secours continue d'écrire vos textes. "
                + "Fichier › Enregistrer vous laissera choisir.",
                "Choisir…", delegate { SaveProject(false); }, "Plus tard", null));
        }

        // ------------------------------------------------------------ le verrou

        /// <summary>Le projet s'installe sur ce chemin : l'ancien verrou tombe,
        /// le nouveau se pose.</summary>
        private void AcquireLock(string path)
        {
            ReleaseLock();
            if (path == null) return;
            PlotLock.Acquire(path);
            _lockedPath = path;
            _lockTicks = 0;
        }

        private void ReleaseLock()
        {
            if (_lockedPath == null) return;
            PlotLock.Release(_lockedPath);
            _lockedPath = null;
        }

        /// <summary>Un tick du secours (30 s) : le battement toutes les deux minutes.</summary>
        private void TickLock()
        {
            if (_lockedPath == null) return;
            if (++_lockTicks < 4) return;
            _lockTicks = 0;
            PlotLock.Heartbeat(_lockedPath);
        }

        /// <summary>Le chemin change (Enregistrer sous, premier enregistrement) :
        /// verrou et référence du disque suivent — pas de référence, l'OS a
        /// déjà confirmé l'écrasement dans le sélecteur.</summary>
        private void AdoptPath(string path)
        {
            AcquireLock(path);
            RememberDisk(null, null);
        }

        /// <summary>Un verrou étranger vivant sur ce projet : on prévient avant
        /// d'ouvrir. Vrai = ouvrir quand même (ou rien à signaler).</summary>
        private async Task<bool> ConfirmForeignLock(string path)
        {
            LockInfo foreign;
            try { foreign = PlotLock.Probe(path); } catch { foreign = null; }
            if (foreign == null) return true;
            var text = "Ce projet est ouvert sur " + foreign.Describe() + ".\n\n"
                + "L'ouvrir ici aussi peut produire des copies en conflit si le dossier est synchronisé "
                + "(OneDrive, Dropbox, NAS…) : les enregistrements de l'une écraseraient ceux de l'autre.\n\n"
                + "Si ce projet n'est plus ouvert là-bas (fermeture brutale), ouvrez-le : le verrou se périme de lui-même.";
            var choice = await MessageDialog.ShowChoices(this, text, AppName, MessageIcon.Warning,
                new[] { "Annuler", "Ouvrir quand même" }, 0);
            return choice == 1;
        }

        /// <summary>Pour la sonde : le verrou de ce processus est posé à côté du projet.</summary>
        public bool LockHeld
        {
            get
            {
                if (_lockedPath == null) return false;
                var info = PlotLock.Read(_lockedPath);
                return info != null && PlotLock.IsOurs(info);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;
using Marabook.View;

namespace Marabook
{
    /// <summary>La SAUVEGARDE DE SECOURS (18/09) : pendant qu'un projet est
    /// ouvert, ses textes (rien de lourd : ni images, ni fichiers de
    /// recherche, ni versions) sont réécrits dans %APPDATA%\Marabook\recovery
    /// trente secondes au plus après une modification, avec un témoin de
    /// session. Une fermeture propre efface les deux ; un témoin retrouvé
    /// au lancement suivant = arrêt brutal, et un toast propose d'ouvrir
    /// le secours, qui se charge comme un .plot ordinaire (sans chemin :
    /// « Enregistrer » demande où le poser). Une erreur non rattrapée écrit
    /// le secours sur-le-champ.</summary>
    public partial class MainWindow
    {
        private RecoverySession _recovery;
        private DispatcherTimer _recoveryTimer;
        private bool _recoveryDirty;      // modifié depuis la dernière écriture du secours
        private bool _inCrash;            // une erreur pendant le secours du plantage : on n'insiste pas
        private DateTime _lastCrashDialog = DateTime.MinValue;
        private DateTime _lastCrashLog = DateTime.MinValue;
        private string _lastCrashKey = ""; // type + message : la même erreur en boucle n'écrit qu'un rapport par dix secondes

        private void StartRecoveryTimer()
        {
            _recoveryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
            _recoveryTimer.Tick += delegate { WriteRecovery(false); };
            _recoveryTimer.Start();
        }

        /// <summary>Un projet vient d'être installé : la session précédente
        /// se ferme proprement, la nouvelle pose son témoin.</summary>
        private void BeginRecovery()
        {
            EndRecovery();
            if (_project == null) return;
            try { _recovery = RecoveryStore.Begin(_path, _project.Name); }
            catch { _recovery = null; }
            _recoveryDirty = false;
        }

        /// <summary>Fermeture propre : témoin et secours retirés.</summary>
        private void EndRecovery()
        {
            if (_recovery == null) return;
            try { RecoveryStore.End(_recovery); }
            catch { }
            _recovery = null;
        }

        /// <summary>Le .plot vient d'être écrit en entier : le secours est
        /// périmé, on le retire (la session continue).</summary>
        private void DropRecovery()
        {
            _recoveryDirty = false;
            if (_recovery == null) return;
            try { RecoveryStore.DropRecovery(_recovery); }
            catch { }
        }

        /// <summary>Écrit le secours s'il y a du neuf (ou de force, au
        /// plantage). Ne doit jamais gêner : toute erreur est avalée.</summary>
        private void WriteRecovery(bool force)
        {
            if (_recovery == null || _project == null || _project.ReadOnlyNewerFormat) return;
            if (!force && !_recoveryDirty) return;
            try
            {
                CommitActive();
                RecoveryStore.Write(_project, _recovery);
                _recoveryDirty = false;
            }
            catch { }
        }

        // ------------------------------------------------------------ au lancement

        /// <summary>Les sessions laissées par un arrêt brutal : un toast par
        /// projet, dans l'accueil s'il couvre la fenêtre, sinon en bas à
        /// droite de celle-ci.</summary>
        private void OfferRecoveries()
        {
            List<RecoverySession> pending;
            try { pending = RecoveryStore.Pending(); }
            catch { return; }
            foreach (var found in pending)
            {
                var session = found;
                var written = session.RecoveryWritten;
                var when = written.HasValue
                    ? " (textes du " + written.Value.ToString("dd/MM/yyyy") + " à " + written.Value.ToString("HH:mm") + ")"
                    : "";
                var toast = NoticeToast.Build("lifebuoy-bold",
                    "Marabook s'est arrêté brutalement",
                    "La dernière session sur « " + session.ProjectName + " » ne s'est pas terminée "
                    + "proprement. Une sauvegarde de secours de ses textes est disponible" + when
                    + ". Voulez-vous la regarder ?",
                    "Ouvrir la sauvegarde", delegate { OpenRecovery(session); },
                    "Ignorer", delegate { try { RecoveryStore.End(session); } catch { } });
                ShowNotice(toast);
            }
        }

        /// <summary>Un toast à boutons : l'accueil s'il est là (il couvre la
        /// fenêtre), sinon l'hôte des toasts de la fenêtre.</summary>
        private void ShowNotice(Border toast)
        {
            if (_welcome != null) _welcome.ShowNotice(toast);
            else NoticeToast.Show(_toastHost, toast);
        }

        /// <summary>Ouvre le secours comme un projet SANS chemin : rien
        /// n'est écrasé tant qu'on n'a pas dit où enregistrer.</summary>
        private void OpenRecovery(RecoverySession session)
        {
            if (!ConfirmDiscard()) return;
            try
            {
                var warnings = new List<string>();
                var project = PlotFile.Load(session.RecoveryPath, warnings);
                LoadProject(project, null);
                if (_welcome != null) _welcome.Release();
                MarkDirty();
                try { RecoveryStore.End(session, true); } catch { } // témoin consommé, secours gardé
                MessageDialog.Show(this,
                    "Sauvegarde de secours de « " + session.ProjectName + " » ouverte.\n\n"
                    + "Elle ne contient que les textes et les cartes mentales : images, fichiers de recherche et versions "
                    + "sont restés dans le .plot d'origine"
                    + (session.ProjectPath != null ? " (" + session.ProjectPath + ")" : "") + ".\n\n"
                    + "Enregistrez-la où vous voulez (Fichier › Enregistrer), par exemple par-dessus "
                    + "le .plot d'origine s'il est abîmé.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                MessageDialog.Show(this,
                    "Impossible d'ouvrir la sauvegarde de secours :\n" + error.Message,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------ plantages

        /// <summary>Une erreur non rattrapée sur le fil d'interface : le
        /// secours d'abord, l'explication ensuite (une par dix secondes au
        /// plus — une erreur en boucle ne doit pas noyer l'écran), et
        /// l'application continue pour laisser enregistrer.</summary>
        public void OnCrash(Exception error)
        {
            LogCrash(error);
            if (_inCrash) return;
            _inCrash = true;
            try { WriteRecovery(true); }
            finally { _inCrash = false; }
            if ((DateTime.Now - _lastCrashDialog).TotalSeconds < 10) return;
            _lastCrashDialog = DateTime.Now;
            try
            {
                MessageDialog.Show(this,
                    "Marabook a rencontré une erreur inattendue :\n" + (error == null ? "?" : error.Message)
                    + "\n\nUne sauvegarde de secours des textes vient d'être écrite. Enregistrez votre "
                    + "projet dès que possible ; si l'application se comporte étrangement, quittez-la "
                    + "et relancez-la — le secours sera proposé au lancement s'il le faut."
                    + "\n\nUn rapport de plantage a été écrit : Aide › Rapports de plantage, à joindre à votre signalement.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch { }
        }

        /// <summary>Une erreur fatale (hors fil d'interface) : le secours,
        /// et le journal — la chute suit.</summary>
        public void OnFatal(Exception error)
        {
            LogCrash(error);
            if (_inCrash) return;
            _inCrash = true;
            try
            {
                if (Dispatcher.CheckAccess()) WriteRecovery(true);
                else Dispatcher.Invoke(new Action(delegate { WriteRecovery(true); }), TimeSpan.FromSeconds(5));
            }
            catch { }
            finally { _inCrash = false; }
        }

        /// <summary>Aide › Rapports de plantage.</summary>
        private void ShowCrashReports()
        {
            CrashReportsDialog.Show(this);
        }

        /// <summary>Ce que le rapport dit du moment (jamais le texte des
        /// écrits) : le projet, l'élément ouvert, le thème, les modules.</summary>
        private List<string> CrashContext()
        {
            var lines = new List<string>();
            try
            {
                lines.Add("Projet : " + (_project == null ? "(aucun)" : _project.Name));
                lines.Add("Élément ouvert : " + (_current == null ? "(aucun)" : _current.Kind.ToString()));
                lines.Add("Thème : " + (AppSettings.DarkTheme ? "sombre" : "clair") + ", zoom " + AppSettings.Zoom + " %");
                var modules = new StringBuilder();
                foreach (var module in Modules.Installed)
                    modules.Append(modules.Length > 0 ? ", " : "").Append(module.Id).Append(module.Version.Length > 0 ? " " + module.Version : "");
                lines.Add("Modules : " + (modules.Length > 0 ? modules.ToString() : "(aucun)"));
            }
            catch { }
            return lines;
        }

        private void LogCrash(Exception error)
        {
            // Une erreur qui se répète à chaque passe de rendu écrirait un
            // rapport par seconde : la même (type + message) n'est journalisée
            // qu'une fois par dix secondes (revue 22/09).
            var key = error == null ? "?" : error.GetType().FullName + "|" + error.Message;
            if (key == _lastCrashKey && (DateTime.Now - _lastCrashLog).TotalSeconds < 10) return;
            _lastCrashKey = key;
            _lastCrashLog = DateTime.Now;
            CrashReport.Write(error, AppVersion, CrashContext());
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Marabook");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + AppVersion + "\n"
                    + (error == null ? "(erreur inconnue)" : error.ToString()) + "\n\n");
            }
            catch { }
        }
    }
}

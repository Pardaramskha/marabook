using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

// setup-stub.cs — l'installeur autonome de Marabook, pour qui n'utilise pas
// Stargazer. Compilé par tools\release.ps1 avec l'archive de la release
// embarquée en ressource (app.zip) : il la déballe dans le dossier choisi
// (par défaut %LOCALAPPDATA%\Programs\Marabook), pose les raccourcis (menu
// Démarrer, Bureau si demandé), associe les fichiers .plot si demandé et
// s'inscrit dans « Applications installées » avec sa désinstallation. Tout
// est réversible : la copie de l'installeur laissée dans le dossier sert de
// désinstalleur (--uninstall). Pas de registre machine, pas d'élévation.
//
//   Marabook-Setup-Windows.exe                     installation guidée
//   Marabook-Setup-Windows.exe --silent [dossier]  sans fenêtre
//   Marabook-Setup-Windows.exe --uninstall [--silent]
//
// Style compatible C# 5 pour compiler avec le csc.exe intégré à Windows.
// Recette partagée avec Mental-o et Typonanny, habillage nuit/or de la
// famille Stargazer. Les clés d'association sont celles de
// src\FileAssociation.cs (l'application s'inscrit elle-même au lancement
// quand rien n'ouvre les .plot).

namespace Marabook.Setup
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var silencieux = false;
            var desinstaller = false;
            string dossier = null;
            foreach (var a in args)
            {
                if (string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)) silencieux = true;
                else if (string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase)) desinstaller = true;
                else if (!a.StartsWith("-")) dossier = a;
            }
            try
            {
                if (desinstaller)
                {
                    var cible = dossier ?? Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                    if (!silencieux)
                    {
                        if (!Dialogue.Confirmer("Désinstaller Marabook ?\n\n" + cible +
                            "\n\nLe dossier et les raccourcis seront retirés, et les fichiers .plot ne lui seront plus associés.\n" +
                            "Vos projets (.plot) et vos réglages (%APPDATA%\\Marabook) ne sont pas touchés.", "Désinstaller"))
                            return 0;
                    }
                    Installeur.Desinstaller(cible);
                    if (!silencieux) Dialogue.Info("Marabook est désinstallé.", null);
                    return 0;
                }
                if (silencieux)
                {
                    Installeur.Installer(dossier ?? Installeur.DossierParDefaut, true, false, true);
                    return 0;
                }
            }
            catch (Exception ex)
            {
                if (!silencieux) Dialogue.Info("Impossible : " + ex.Message, null);
                return 1;
            }
            Application.Run(new SetupForm());
            return 0;
        }
    }

    // ----------------------------------------------------------- le travail

    public static class Installeur
    {
        public const string Nom = "Marabook";
        public const string Cle = "Marabook";   // clé de désinstallation (Updater.UpdateRegistry la relit)
        public const string Exe = "Marabook.exe";
        public const string NomSetup = "Marabook-Setup-Windows.exe";
        public const string ProgId = "Marabook.Project";
        public const string Extension = ".plot";

        public static string DossierParDefaut
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Path.Combine("Programs", Nom));
            }
        }

        // La version embarquée (fichier VERSION de l'archive).
        public static string Version
        {
            get
            {
                try
                {
                    using (var s = Assembly.GetEntryAssembly().GetManifestResourceStream("app.zip"))
                    using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                    {
                        var e = zip.GetEntry("VERSION");
                        if (e == null) return "";
                        using (var r = new StreamReader(e.Open(), Encoding.UTF8)) return r.ReadToEnd().Trim();
                    }
                }
                catch { return ""; }
            }
        }

        public static string RaccourciMenu
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Nom + ".lnk"); }
        }

        public static string RaccourciBureau
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Nom + ".lnk"); }
        }

        public static void Installer(string dossier, bool menu, bool bureau, bool associer)
        {
            dossier = Path.GetFullPath(dossier);
            Deballer(dossier);
            var exe = Path.Combine(dossier, Exe);
            if (!File.Exists(exe)) throw new Exception("l'archive ne contient pas " + Exe);

            // la copie de l'installeur sert de désinstalleur
            var moi = Assembly.GetEntryAssembly().Location;
            var copie = Path.Combine(dossier, NomSetup);
            if (!string.Equals(Path.GetFullPath(moi), copie, StringComparison.OrdinalIgnoreCase))
                File.Copy(moi, copie, true);

            if (menu) Raccourci(RaccourciMenu, exe, dossier);
            else Effacer(RaccourciMenu);
            if (bureau) Raccourci(RaccourciBureau, exe, dossier);
            if (associer) AssocierExtension(exe, dossier);
            InscrireDesinstallation(dossier, exe, copie);
        }

        public static void Desinstaller(string dossier)
        {
            Effacer(RaccourciMenu);
            Effacer(RaccourciBureau);
            DissocierExtension(Path.Combine(dossier, Exe));
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Cle, false); }
            catch { }
            // le dossier contient ce programme : cmd l'efface une fois qu'on est parti
            if (Directory.Exists(dossier))
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dossier + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
        }

        private static void Deballer(string dossier)
        {
            using (var s = Assembly.GetEntryAssembly().GetManifestResourceStream("app.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                Directory.CreateDirectory(dossier);
                var racine = Path.GetFullPath(dossier).TrimEnd('\\') + "\\";
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.EndsWith("/")) continue;
                    var cible = Path.GetFullPath(Path.Combine(dossier, e.FullName.Replace('/', '\\')));
                    if (!cible.StartsWith(racine, StringComparison.OrdinalIgnoreCase)) continue;   // Zip Slip
                    // réinstaller = mettre à jour ; les réglages de l'utilisateur vivent
                    // dans %APPDATA%\Marabook, jamais ici : ils survivent.
                    Directory.CreateDirectory(Path.GetDirectoryName(cible));
                    using (var src = e.Open())
                    using (var dst = File.Create(cible))
                        src.CopyTo(dst);
                }
            }
        }

        // Raccourci .lnk via WScript.Shell (COM par réflexion : aucune référence à ajouter).
        private static void Raccourci(string chemin, string exe, string dossier)
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            var shell = Activator.CreateInstance(t);
            var lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { chemin });
            var tl = lnk.GetType();
            tl.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exe });
            tl.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { dossier });
            tl.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { exe + ",0" });
            tl.InvokeMember("Description", BindingFlags.SetProperty, null, lnk,
                new object[] { "Traitement de texte et construction narrative pour les romans" });
            tl.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }

        private static void Effacer(string chemin)
        {
            try { if (File.Exists(chemin)) File.Delete(chemin); } catch { }
        }

        // La même association HKCU que l'application (src\FileAssociation.cs)
        // et que tools\associate-plot.bat.
        private static void AssocierExtension(string exe, string dossier)
        {
            var icone = Path.Combine(dossier, Path.Combine("assets", "plot.ico"));
            using (var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
            {
                using (var cle = classes.CreateSubKey(Extension)) cle.SetValue(null, ProgId);
                using (var cle = classes.CreateSubKey(ProgId)) cle.SetValue(null, "Projet Marabook");
                using (var cle = classes.CreateSubKey(ProgId + @"\DefaultIcon"))
                    cle.SetValue(null, File.Exists(icone) ? icone + ",0" : exe + ",0");
                using (var cle = classes.CreateSubKey(ProgId + @"\shell\open\command"))
                    cle.SetValue(null, "\"" + exe + "\" \"%1\"");
            }
            RafraichirIcones();
        }

        // On ne retire l'association que si elle pointe encore sur cet exe.
        private static void DissocierExtension(string exe)
        {
            try
            {
                using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true))
                {
                    if (classes == null) return;
                    using (var commande = classes.OpenSubKey(ProgId + @"\shell\open\command"))
                    {
                        var valeur = commande == null ? null : commande.GetValue(null) as string;
                        if (valeur == null || valeur.IndexOf(exe, StringComparison.OrdinalIgnoreCase) < 0) return;
                    }
                    classes.DeleteSubKeyTree(ProgId, false);
                    using (var cle = classes.OpenSubKey(Extension))
                    {
                        if (cle != null && string.Equals(cle.GetValue(null) as string, ProgId))
                        {
                            cle.Close();
                            classes.DeleteSubKeyTree(Extension, false);
                        }
                    }
                }
                RafraichirIcones();
            }
            catch { }
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private static void RafraichirIcones()
        {
            try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }   // SHCNE_ASSOCCHANGED
        }

        // Paramètres > Applications installées : nom, version, icône, taille, désinstallation.
        private static void InscrireDesinstallation(string dossier, string exe, string setup)
        {
            long octets = 0;
            foreach (var f in Directory.GetFiles(dossier, "*", SearchOption.AllDirectories))
                octets += new FileInfo(f).Length;
            using (var k = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Cle))
            {
                k.SetValue("DisplayName", Nom);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Rémi Escamilla");
                k.SetValue("InstallLocation", dossier);
                k.SetValue("DisplayIcon", exe);
                k.SetValue("UninstallString", "\"" + setup + "\" --uninstall");
                k.SetValue("QuietUninstallString", "\"" + setup + "\" --uninstall --silent");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(octets / 1024), RegistryValueKind.DWord);
            }
        }
    }

    // ------------------------------------------------------------- la fenêtre
    // Habillage nuit/or de la famille Stargazer, peint à la main.

    public static class Theme
    {
        public static readonly Color Nuit = Color.FromArgb(11, 16, 38);
        public static readonly Color Panneau = Color.FromArgb(19, 26, 51);
        public static readonly Color Bordure = Color.FromArgb(42, 51, 88);
        public static readonly Color Or = Color.FromArgb(212, 175, 55);
        public static readonly Color Texte = Color.FromArgb(230, 230, 240);
        public static readonly Color TexteDoux = Color.FromArgb(154, 163, 192);
        public static readonly Color Encre = Color.FromArgb(20, 20, 30);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void Sombre(Form f)
        {
            try { var v = 1; DwmSetWindowAttribute(f.Handle, 20, ref v, 4); } catch { }
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    public class RoundedButton : Button
    {
        private bool _hover;
        public bool Primaire;

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var fond = Primaire ? Theme.Or : Theme.Panneau;
            if (!Enabled) fond = Color.FromArgb(200, 203, 210);
            else if (_hover) fond = Primaire ? Color.FromArgb(244, 215, 122) : Theme.Bordure;
            using (var chemin = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            {
                using (var b = new SolidBrush(fond)) g.FillPath(b, chemin);
                using (var p = new Pen(!Enabled ? Color.FromArgb(168, 172, 182) : Primaire ? Theme.Or : Theme.Bordure))
                    g.DrawPath(p, chemin);
            }
            var encre = !Enabled ? Color.FromArgb(58, 61, 70) : Primaire ? Theme.Encre : Theme.Texte;
            var police = Primaire ? new Font(Font, FontStyle.Bold) : Font;
            TextRenderer.DrawText(g, Text, police, ClientRectangle, encre,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Primaire) police.Dispose();
        }
    }

    // Champ de saisie arrondi (bordure d'or au focus).
    public class RoundedField : Control
    {
        private readonly TextBox _tb = new TextBox();

        public RoundedField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 28;
            Cursor = Cursors.IBeam;
            _tb.BorderStyle = BorderStyle.None;
            _tb.BackColor = Theme.Panneau;
            _tb.ForeColor = Theme.Texte;
            _tb.GotFocus += delegate { Invalidate(); };
            _tb.LostFocus += delegate { Invalidate(); };
            Controls.Add(_tb);
        }

        public override string Text { get { return _tb.Text; } set { _tb.Text = value; } }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _tb.Enabled = Enabled;
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); _tb.Font = Font; OnResize(e); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            var h = _tb.PreferredHeight;
            _tb.SetBounds(10, Math.Max(2, (Height - h) / 2), Math.Max(10, Width - 20), h);
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _tb.Focus(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Nuit);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 8))
            {
                using (var b = new SolidBrush(Theme.Panneau)) g.FillPath(b, path);
                using (var p = new Pen(_tb.Focused ? Theme.Or : Theme.Bordure)) g.DrawPath(p, path);
            }
        }
    }

    public class SetupForm : Form
    {
        private readonly RoundedField _dossier;
        private readonly CheckBox _menu;
        private readonly CheckBox _bureau;
        private readonly CheckBox _associer;
        private readonly RoundedButton _installer;
        private readonly RoundedButton _fermer;
        private readonly RoundedButton _parcourir;
        private readonly Label _etat;
        private bool _fait;

        public SetupForm()
        {
            Text = "Installation de Marabook";
            ClientSize = new Size(520, 352);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Nuit;
            ForeColor = Theme.Texte;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var titre = new Label();
            titre.Text = "Marabook";
            titre.Font = new Font("Segoe UI Semibold", 16f);
            titre.ForeColor = Theme.Or;
            titre.SetBounds(24, 18, 470, 34);
            Controls.Add(titre);
            var version = Installeur.Version;
            var sous = new Label();
            sous.Text = (version.Length > 0 ? "Version " + version + " — " : "") +
                "traitement de texte et construction narrative, du premier jet au livre imprimé.";
            sous.ForeColor = Theme.TexteDoux;
            sous.SetBounds(26, 54, 470, 20);
            Controls.Add(sous);

            var libDossier = new Label();
            libDossier.Text = "Installer dans :";
            libDossier.SetBounds(26, 92, 200, 20);
            Controls.Add(libDossier);
            _dossier = new RoundedField();
            _dossier.Text = Installeur.DossierParDefaut;
            _dossier.SetBounds(26, 112, 372, 30);
            Controls.Add(_dossier);
            _parcourir = new RoundedButton();
            _parcourir.Text = "Parcourir…";
            _parcourir.SetBounds(406, 112, 90, 30);
            _parcourir.Click += delegate
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "Dossier d'installation de Marabook";
                    dlg.SelectedPath = _dossier.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        _dossier.Text = Path.Combine(dlg.SelectedPath,
                            Path.GetFileName(dlg.SelectedPath) == Installeur.Nom ? "" : Installeur.Nom);
                }
            };
            Controls.Add(_parcourir);

            _menu = Case("Raccourci dans le menu Démarrer", 156, true);
            _bureau = Case("Raccourci sur le Bureau", 182, false);
            _associer = Case("Ouvrir les fichiers .plot avec Marabook (double-clic dans l'Explorateur)", 208, true);

            _etat = new Label();
            _etat.ForeColor = Theme.TexteDoux;
            _etat.SetBounds(26, 244, 470, 54);
            _etat.Text = "Aucun droit administrateur requis : tout reste dans votre session, " +
                "et se retire depuis Paramètres > Applications installées. " +
                "Vos projets et vos réglages ne sont jamais touchés.";
            Controls.Add(_etat);

            _fermer = new RoundedButton();
            _fermer.Text = "Annuler";
            _fermer.SetBounds(296, 310, 96, 32);
            _fermer.Click += delegate { Close(); };
            Controls.Add(_fermer);
            _installer = new RoundedButton();
            _installer.Text = "Installer";
            _installer.Primaire = true;
            _installer.SetBounds(400, 310, 96, 32);
            _installer.Click += delegate { Lancer(); };
            Controls.Add(_installer);
            AcceptButton = _installer;
            CancelButton = _fermer;
        }

        private CheckBox Case(string texte, int y, bool coche)
        {
            var c = new CheckBox();
            c.Text = texte;
            c.Checked = coche;
            c.ForeColor = Theme.Texte;
            c.SetBounds(26, y, 470, 22);
            Controls.Add(c);
            return c;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.Sombre(this);
        }

        private void Lancer()
        {
            if (_fait)
            {
                // « Lancer » : l'application, et on s'en va
                try
                {
                    var psi = new ProcessStartInfo(Path.Combine(_dossier.Text, Installeur.Exe));
                    psi.WorkingDirectory = _dossier.Text;
                    Process.Start(psi);
                }
                catch { }
                Close();
                return;
            }
            _installer.Enabled = false;
            _etat.Text = "Installation…";
            Refresh();
            try
            {
                Installeur.Installer(_dossier.Text, _menu.Checked, _bureau.Checked, _associer.Checked);
                _fait = true;
                _etat.ForeColor = Theme.Or;
                _etat.Text = "Installé dans " + _dossier.Text;
                _installer.Text = "Lancer";
                _fermer.Text = "Fermer";
                _menu.Enabled = false;
                _bureau.Enabled = false;
                _associer.Enabled = false;
                _dossier.Enabled = false;
                _parcourir.Enabled = false;
            }
            catch (Exception ex)
            {
                _etat.ForeColor = Color.FromArgb(230, 110, 120);
                _etat.Text = "Impossible : " + ex.Message;
            }
            _installer.Enabled = true;
        }
    }

    // Petits dialogues du même habillage (désinstallation, erreurs) : la pastille de sens,
    // le message, les boutons à droite, le principal en or.
    public static class Dialogue
    {
        public static void Info(string texte, string titre)
        {
            using (var f = Boite(texte, titre ?? "Marabook", false)) f.ShowDialog();
        }

        public static bool Confirmer(string texte, string titre)
        {
            using (var f = Boite(texte, titre, true)) return f.ShowDialog() == DialogResult.OK;
        }

        private static Form Boite(string texte, string titre, bool question)
        {
            var f = new Form();
            f.Text = titre;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false; f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.BackColor = Theme.Nuit; f.ForeColor = Theme.Texte;
            f.Font = new Font("Segoe UI", 9.5f);
            f.HandleCreated += delegate { Theme.Sombre(f); };
            var pastille = new Pastille();
            pastille.Couleur = question ? Color.FromArgb(122, 162, 247) : Theme.Or;
            pastille.Signe = question ? "?" : "i";
            pastille.SetBounds(20, 18, 26, 26);
            f.Controls.Add(pastille);
            var l = new Label();
            l.Text = texte;
            l.AutoSize = false;
            var taille = TextRenderer.MeasureText(texte, f.Font, new Size(440, 0),
                TextFormatFlags.WordBreak);
            var hauteur = Math.Max(30, taille.Height + 6);
            l.SetBounds(58, 20, 440, hauteur);
            f.ClientSize = new Size(518, hauteur + 86);
            f.Controls.Add(l);
            var ok = new RoundedButton();
            ok.Text = question ? "Désinstaller" : "OK";
            ok.Primaire = true;
            ok.SetBounds(402, hauteur + 36, 96, 32);
            ok.DialogResult = DialogResult.OK;
            f.Controls.Add(ok);
            f.AcceptButton = ok;
            if (question)
            {
                var non = new RoundedButton();
                non.Text = "Annuler";
                non.SetBounds(298, hauteur + 36, 96, 32);
                non.DialogResult = DialogResult.Cancel;
                f.Controls.Add(non);
                f.CancelButton = non;
            }
            else f.CancelButton = ok;
            return f;
        }

        private class Pastille : Control
        {
            public Color Couleur;
            public string Signe = "";

            public Pastille()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent != null ? Parent.BackColor : Theme.Nuit);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Couleur)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
                using (var f = new Font("Segoe UI", 11f, FontStyle.Bold))
                    TextRenderer.DrawText(g, Signe, f, ClientRectangle, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}

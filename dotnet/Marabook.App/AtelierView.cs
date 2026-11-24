using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Exchange;
using Marabook.Print;

namespace Marabook.App
{
    /// <summary>L'ATELIER (2.0.0, 21/11/2026, Rémi) — le premier élément de la
    /// section OUTILS de la Pile : les outils dont un processus créatif ou
    /// éditorial a besoin, hors du projet ouvert.
    ///
    /// 22/11 (lot 30, Rémi) : plus d'onglets ni de pastilles — l'ACCUEIL de
    /// l'Atelier montre ses boîtes à outils en TUILES, chacune avec l'aperçu
    /// de ce qu'elle contient ; on entre dans une boîte, puis dans un outil,
    /// et « Précédent » (le bouton de la page comme celui de la souris)
    /// remonte d'un cran, jusqu'à l'accueil. Deux boîtes :
    ///  - PDFaah, pour les PDF : Chop-Chop, scinder, fusionner, pages blanches,
    ///    pivoter, filigrane, signature, compression, conversion (Word, JPEG),
    ///    verrouillage, Unlocker, contrôle d'impression, contrôle PDF/X — et
    ///    ses composants téléchargeables (PdfPig, PDFium) ;
    ///  - WGYC (« Write Good You Cunt »), pour la langue : le Conjugueur, et
    ///    les liens utiles.
    /// Un outil qui tient un PDF le RETIRE quand on le quitte ; s'il y a du
    /// travail pas encore enregistré, on est prévenu d'abord (PendingWork).
    /// L'Atelier ne dépend d'aucun projet : il travaille sur des fichiers du
    /// disque, et n'écrit jamais par-dessus celui qu'on lui donne.
    /// ICÔNES PROVISOIRES (notées dans la TO-DO générale) : voir Icon*.</summary>
    public partial class AtelierView : DockPanel
    {
        // Les icônes provisoires, en attendant les dessins de Rémi.
        public const string IconAtelier = "blueprint-bold", IconPdfaah = "document-file", IconPdfPig = "file-arrow-down-bold", IconPdfium = "gear-six-bold",
            IconUnlocker = "lock-open", IconPdfX = "exam-bold", IconPrintCheck = "printer-bold", IconChopChop = "files-bold",
            IconModeList = "list", IconModeGrid = "grid", IconModeSpreads = "book-open-text-bold",
            // lot 30
            IconWgyc = "text-t-bold", IconConjugator = "list-dashes-bold", IconLinks = "magnifying-glass-bold",
            IconSplit = "text-columns-bold", IconBlank = "file-dashed-bold", IconMerge = "books-bold", IconCompress = "file-arrow-down-bold",
            IconConvert = "image-square-bold", IconLock = "lock", IconRotate = "arrow-counter-clockwise", IconWatermark = "paragraph-bold", IconSign = "pencil-simple-line";

        /// <summary>L'accueil de l'Atelier.</summary>
        public const string ToolHome = "";
        // Les boîtes à outils.
        public const string BoxPdfaah = "pdfaah", BoxWgyc = "wgyc";
        // Les outils de PDFaah.
        public const string ToolUnlocker = "unlocker", ToolPdfX = "pdfx", ToolPrint = "print", ToolChop = "chop",
            ToolSplit = "split", ToolBlank = "blank", ToolMerge = "merge", ToolCompress = "compress", ToolConvert = "convert",
            ToolLock = "lock", ToolRotate = "rotate", ToolWatermark = "watermark", ToolSign = "sign";
        // Les outils de WGYC.
        public const string ToolConjugator = "conjugueur";
        private const string PdfFilter = "PDF|*.pdf";

        /// <summary>Un écran de l'Atelier : sa clé, son titre, son icône, ce qu'il fait, sa boîte (null = une boîte, ou l'accueil).</summary>
        internal sealed class Screen
        {
            public string Key, Title, Icon, Text, Box;
            public bool NeedsPdfium, NeedsPdfPig;
        }

        internal static readonly Screen[] Screens =
        {
            new Screen { Key = BoxPdfaah, Title = "PDFaah", Icon = IconPdfaah, Text = "La boîte à outils des PDF : les recomposer, les retoucher, les convertir, les protéger, les contrôler avant l'imprimeur." },
            new Screen { Key = BoxWgyc, Title = "WGYC", Icon = IconWgyc, Text = "Write Good You Cunt — la langue française sous la main, pour mieux écrire ou ne pas se planter en le faisant." },

            new Screen { Key = ToolChop, Box = BoxPdfaah, Title = "Chop-Chop", Icon = IconChopChop, Text = "Voir les pages d'un PDF, en retirer, les réordonner, y ajouter un autre PDF.", NeedsPdfium = true },
            new Screen { Key = ToolSplit, Box = BoxPdfaah, Title = "Scinder", Icon = IconSplit, Text = "Couper un PDF en deux, après la page de votre choix : deux fichiers.", NeedsPdfium = true },
            new Screen { Key = ToolMerge, Box = BoxPdfaah, Title = "Fusionner", Icon = IconMerge, Text = "Mettre plusieurs PDF bout à bout, dans l'ordre que vous voulez : un seul fichier.", NeedsPdfium = true },
            new Screen { Key = ToolBlank, Box = BoxPdfaah, Title = "Pages blanches", Icon = IconBlank, Text = "Glisser des pages blanches dans un PDF, à sa taille ou à celle que vous donnez.", NeedsPdfium = true },
            new Screen { Key = ToolRotate, Box = BoxPdfaah, Title = "Pivoter", Icon = IconRotate, Text = "Remettre d'aplomb des pages couchées ou à l'envers : toutes, ou certaines.", NeedsPdfium = true },
            new Screen { Key = ToolWatermark, Box = BoxPdfaah, Title = "Filigrane", Icon = IconWatermark, Text = "Écrire « BROUILLON », « Épreuve », votre nom… en travers ou en marge des pages.", NeedsPdfium = true },
            new Screen { Key = ToolSign, Box = BoxPdfaah, Title = "Signer", Icon = IconSign, Text = "Apposer votre signature sur une page : une image, ou tracée à la main.", NeedsPdfium = true },
            new Screen { Key = ToolCompress, Box = BoxPdfaah, Title = "Compresser", Icon = IconCompress, Text = "Alléger un PDF : sans perte, ou en réduisant ses images pour l'écran et l'envoi.", NeedsPdfium = true },
            new Screen { Key = ToolConvert, Box = BoxPdfaah, Title = "Convertir", Icon = IconConvert, Text = "Un PDF en images JPEG (une par page), ou son texte en document Word.", NeedsPdfium = true, NeedsPdfPig = true },
            new Screen { Key = ToolLock, Box = BoxPdfaah, Title = "Verrouiller", Icon = IconLock, Text = "Protéger un PDF par un mot de passe : sans lui, le fichier ne s'ouvre pas.", NeedsPdfium = true },
            new Screen { Key = ToolUnlocker, Box = BoxPdfaah, Title = "Unlocker", Icon = IconUnlocker, Text = "Une copie sans verrou d'un PDF verrouillé : impression, copie et modification redeviennent possibles.", NeedsPdfium = true },
            new Screen { Key = ToolPrint, Box = BoxPdfaah, Title = "Contrôle d'impression", Icon = IconPrintCheck, Text = "Fonds perdus, taille des pages, homogénéité, polices incorporées : ce qu'un imprimeur regarde d'abord." },
            new Screen { Key = ToolPdfX, Box = BoxPdfaah, Title = "Contrôle PDF/X", Icon = IconPdfX, Text = "Le fichier est-il conforme à PDF/X-1a:2003 ? Le contrôle de l'export, sur le fichier de votre choix." },

            new Screen { Key = ToolConjugator, Box = BoxWgyc, Title = "Conjugueur", Icon = IconConjugator, Text = "Le tableau d'un verbe, à tous les modes et à tous les temps — à partir de l'infinitif ou d'une forme conjuguée." }
        };

        internal static Screen ScreenOf(string key)
        {
            foreach (var screen in Screens) if (screen.Key == key) return screen;
            return null;
        }

        /// <summary>D'où l'on vient quand on est là : la boîte d'un outil, l'accueil pour une boîte.</summary>
        internal static string ParentOf(string key)
        {
            var screen = ScreenOf(key);
            return screen == null || screen.Box == null ? ToolHome : screen.Box;
        }

        private readonly StackPanel _page;
        private readonly TextBlock _crumb;
        private readonly Button _back;
        private readonly ChopChopView _chop;
        private readonly Panel _body;
        private string _tool = ToolHome;
        private StackPanel _components;

        public AtelierView()
        {
            Skin.Ground(this); // Chrome.WindowBg, ou transparent sur le fond d'un skin
            Tag = "atelier";

            var head = new StackPanel { Margin = new Thickness(28, 20, 28, 0) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Icons.Make(IconAtelier, 22, Chrome.Ink);
            icon.Margin = new Thickness(0, 0, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(icon);
            titleRow.Children.Add(new TextBlock { Text = "Atelier", FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(titleRow);
            _intro = new TextBlock { Text = "Les outils d'à côté du texte : ils travaillent sur les fichiers de votre disque, pas sur le projet ouvert, et n'écrivent jamais par-dessus celui qu'on leur donne.", Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            head.Children.Add(_intro);
            var crumbRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            _back = Buttons.IconText("arrow-left-bold", "Atelier", "Revenir d'un cran", Buttons.Bar, Buttons.Look.Outline);
            _back.Tag = "atelier-back";
            _back.IsVisible = false;
            _back.Click += delegate { Choose(ParentOf(_tool)); };
            crumbRow.Children.Add(_back);
            _crumb = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0), Tag = "atelier-crumb" };
            crumbRow.Children.Add(_crumb);
            _crumbRow = crumbRow;
            head.Children.Add(crumbRow);
            head.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 10, 0, 0) });
            SetDock(head, Dock.Top);
            Children.Add(head);

            _chop = new ChopChopView { IsVisible = false, Margin = new Thickness(28, 12, 28, 16) };
            _chop.PdfiumNeeded = EnsurePdfium;
            _page = new StackPanel { Margin = new Thickness(28, 14, 28, 24) };
            var scroll = new ScrollViewer { Content = _page, [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled, [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto, Tag = "atelier-scroll" };
            var body = new Panel();
            body.Children.Add(scroll);
            body.Children.Add(_chop);
            Children.Add(body);
            _body = body;
            _scroll = scroll;
            Show(ToolHome);
        }

        private readonly ScrollViewer _scroll;
        private readonly TextBlock _intro;
        private readonly Control _crumbRow;

        /// <summary>L'écran montré (« » = l'accueil de l'Atelier).</summary>
        public string Tool { get { return _tool; } }

        /// <summary>Le pont de grammaire, pour le Conjugueur — posé par la coquille.</summary>
        public Func<Marabook.Correction.Grammalecte.GrammalecteBridge> GrammarBridge;

        // ================================================================ aller d'un écran à l'autre

        /// <summary>Un écran choisi À LA MAIN : un pas de plus dans l'historique — « Précédent »
        /// ramène à celui d'avant. Quitter un outil qui tient du travail pas enregistré : on
        /// demande d'abord.</summary>
        internal async void Choose(string tool)
        {
            await ChooseAsync(tool);
        }

        internal async Task<bool> ChooseAsync(string tool)
        {
            var before = _tool;
            if (before == tool) return true;
            if (!await ConfirmLeave()) return false;
            Show(tool);
            ScreenSteps.Record(this, delegate { Travel(before); }, delegate { Travel(tool); });
            return true;
        }

        /// <summary>« Précédent » / « Suivant » de la souris : le même garde-fou, sans nouveau pas.</summary>
        private async void Travel(string tool)
        {
            var from = _tool;
            if (from == tool) return;
            if (await ConfirmLeave()) { Show(tool); return; }
            // On reste : le pas que la coquille vient de défaire est remis.
            ScreenSteps.Record(this, delegate { Travel(tool); }, delegate { Travel(from); });
        }

        /// <summary>Vrai si l'on peut quitter l'écran en cours : rien à perdre, ou l'auteur l'accepte.</summary>
        private async Task<bool> ConfirmLeave()
        {
            var pending = PendingWork();
            if (pending == null) return true;
            var answer = await MessageDialog.Show(Ui.OwnerOf(this), LeaveWarning(pending) + "\n\nQuitter " + pending + " ?", "Atelier", MessageButtons.YesNo, MessageIcon.Warning);
            return answer == MessageResult.Yes;
        }

        internal static string LeaveWarning(string tool)
        {
            return "Le PDF ouvert dans " + tool + " sera retiré, et le travail que vous y avez fait sans l'enregistrer sera perdu.";
        }

        /// <summary>Le nom de l'outil qui tient du travail PAS ENCORE ENREGISTRÉ, ou null.</summary>
        internal string PendingWork()
        {
            if (_tool == ToolChop) return _chop.HasUnsavedWork ? "Chop-Chop" : null;
            if (_tool == ToolUnlocker) return _unlockDocument != null && LastUnlockState == "verrouillé" ? "l'Unlocker" : null;
            var screen = ScreenOf(_tool);
            if (screen == null || screen.Box != BoxPdfaah) return null;
            return ToolHasUnsavedWork() ? "« " + screen.Title + " »" : null;
        }

        /// <summary>Retire tout PDF tenu par un outil : les fichiers sont rendus, les écrans vidés.</summary>
        internal void DropWork()
        {
            CloseUnlockDocument();
            LastUnlockState = "";
            _chop.Reset();
            DropToolWork();
        }

        /// <summary>La coquille a quitté la page de l'Atelier (un autre élément de la Pile) : le PDF
        /// ouvert est retiré. S'il y avait du travail pas enregistré, on prévient — et l'on peut
        /// revenir, tout est encore là tant qu'on n'a pas répondu.</summary>
        internal async void LeftPage(Action comeBack)
        {
            if (_leaving) return;
            var pending = PendingWork();
            if (pending == null) { if (HoldsFiles()) { DropWork(); Show(_tool); } return; }
            _leaving = true;
            try
            {
                var answer = await MessageDialog.Show(Ui.OwnerOf(this), "Vous avez quitté l'Atelier. " + LeaveWarning(pending) + "\n\nRevenir à " + pending + " ?", "Atelier", MessageButtons.YesNo, MessageIcon.Warning);
                if (answer == MessageResult.Yes) { if (comeBack != null) comeBack(); return; }
                DropWork();
                Show(_tool);
            }
            finally { _leaving = false; }
        }

        private bool _leaving;

        private bool HoldsFiles()
        {
            return _unlockDocument != null || _chop.HasFile || ToolHoldsFile();
        }

        /// <summary>À l'ouverture de la page : l'état des composants a pu changer ailleurs (un import de PDF, les Préférences).</summary>
        public void Refresh() { if (_tool == BoxPdfaah || _tool == ToolHome) Show(_tool); }

        private void Show(string tool)
        {
            // Quitter un outil, c'est rendre son PDF (22/11).
            if (tool != _tool) DropWork();
            _tool = tool;
            _page.Children.Clear();
            var screen = ScreenOf(tool);
            _back.IsVisible = tool != ToolHome;
            var parent = ScreenOf(ParentOf(tool));
            _back.Content = Icons.Label("arrow-left-bold", parent == null ? "Atelier" : parent.Title, 12, Chrome.Ink);
            ToolTip.SetTip(_back, parent == null ? "Revenir à l'accueil de l'Atelier" : "Revenir aux outils de " + parent.Title);
            // Dans un outil, la présentation de l'Atelier se retire : toute la place est à l'outil.
            _intro.IsVisible = tool == ToolHome;
            _crumbRow.IsVisible = tool != ToolHome;
            _crumb.Margin = new Thickness(tool == ToolHome ? 0 : 12, 0, 0, 0);
            _crumb.Text = screen == null ? "" : screen.Box == null ? screen.Title : screen.Title;
            _chop.IsVisible = tool == ToolChop;
            _scroll.IsVisible = tool != ToolChop;
            if (tool == ToolHome) BuildAtelierHome();
            else if (tool == BoxPdfaah) BuildHome();
            else if (tool == BoxWgyc) BuildWgyc();
            else if (tool == ToolUnlocker) BuildUnlocker();
            else if (tool == ToolPdfX) BuildPdfX();
            else if (tool == ToolPrint) BuildPrintCheck();
            else if (tool == ToolConjugator) BuildConjugator();
            else if (tool != ToolChop) BuildTool(tool);
            _scroll.Offset = new Vector(0, 0);
        }

        // ================================================================ l'accueil de l'Atelier : les boîtes, en tuiles

        private void BuildAtelierHome()
        {
            var tiles = new WrapPanel { Margin = new Thickness(0, 4, 0, 0), Tag = "atelier-boxes" };
            foreach (var screen in Screens)
                if (screen.Box == null) tiles.Children.Add(BoxTile(screen));
            _page.Children.Add(tiles);
            _page.Children.Add(new TextBlock { Text = "D'autres boîtes à outils viendront.", Foreground = Chrome.FaintText, FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
        }

        /// <summary>La tuile d'une boîte : son nom, ce qu'elle fait, et l'APERÇU de ses outils.</summary>
        private Control BoxTile(Screen box)
        {
            var column = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Icons.Make(box.Icon, 24, Chrome.AccentStrong);
            glyph.Margin = new Thickness(0, 0, 10, 0);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(glyph);
            top.Children.Add(new TextBlock { Text = box.Title, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center });
            column.Children.Add(top);
            column.Children.Add(new TextBlock { Text = box.Text, Foreground = Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10) });
            column.Children.Add(new Border { Height = 1, Background = Chrome.Border, Margin = new Thickness(0, 0, 0, 8) });
            var preview = new WrapPanel { Tag = "atelier-box-preview" };
            foreach (var screen in Screens)
            {
                if (screen.Box != box.Key) continue;
                var line = new StackPanel { Orientation = Orientation.Horizontal, Width = 168, Margin = new Thickness(0, 2, 0, 2) };
                var small = Icons.Make(screen.Icon, 13, Chrome.SoftText);
                small.Margin = new Thickness(0, 0, 6, 0);
                small.VerticalAlignment = VerticalAlignment.Center;
                line.Children.Add(small);
                line.Children.Add(new TextBlock { Text = screen.Title, Foreground = Chrome.Ink, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                preview.Children.Add(line);
            }
            if (box.Key == BoxWgyc)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Width = 168, Margin = new Thickness(0, 2, 0, 2) };
                var small = Icons.Make(IconLinks, 13, Chrome.SoftText);
                small.Margin = new Thickness(0, 0, 6, 0);
                small.VerticalAlignment = VerticalAlignment.Center;
                line.Children.Add(small);
                line.Children.Add(new TextBlock { Text = "Liens utiles", Foreground = Chrome.Ink, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
                preview.Children.Add(line);
            }
            column.Children.Add(preview);
            var card = new Border
            {
                Width = 400, MinHeight = 190, Margin = new Thickness(0, 6, 14, 8), Padding = new Thickness(18, 16, 18, 16), CornerRadius = new CornerRadius(10),
                Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = column, Tag = "atelier-box:" + box.Key
            };
            card.PointerEntered += delegate { card.BorderBrush = Chrome.Accent; };
            card.PointerExited += delegate { card.BorderBrush = Chrome.Border; };
            var key = box.Key;
            card.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton == MouseButton.Left) Choose(key); };
            return card;
        }

        // ================================================================ l'accueil de PDFaah

        private void BuildHome()
        {
            _page.Children.Add(Paragraph("Recomposer un PDF, le retoucher, le convertir, le protéger, le contrôler avant de l'envoyer. Chaque outil travaille sur le fichier que vous choisissez, et en écrit un autre : l'original ne bouge pas."));
            var tiles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), Tag = "atelier-tools" };
            foreach (var screen in Screens)
                if (screen.Box == BoxPdfaah) tiles.Children.Add(Tile(screen));
            _page.Children.Add(tiles);

            _page.Children.Add(Heading("Composants"));
            _page.Children.Add(Paragraph("Ce que PDFaah télécharge à la demande. Rien n'est livré d'office : chaque composant s'installe quand un outil en a besoin, ou ici, et se retire quand vous voulez (ici, ou dans Préférences › Dépendances). Marabook vérifie l'empreinte de ce qu'il reçoit avant de l'installer."));
            _components = new StackPanel { Tag = "atelier-components", MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            _page.Children.Add(_components);
            RebuildComponents(null);
        }

        private Control Tile(Screen screen)
        {
            var column = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Icons.Make(screen.Icon, 20, Chrome.AccentStrong);
            glyph.Margin = new Thickness(0, 0, 9, 0);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(glyph);
            top.Children.Add(new TextBlock { Text = screen.Title, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center });
            column.Children.Add(top);
            column.Children.Add(new TextBlock { Text = screen.Text, Foreground = Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) });
            if (screen.NeedsPdfium) column.Children.Add(new TextBlock { Text = screen.NeedsPdfPig ? "Demande PDFium — et PdfPig, vers Word" : "Demande PDFium", Foreground = Chrome.FaintText, FontSize = 11.5, Margin = new Thickness(0, 7, 0, 0), Tag = "atelier-needs" });
            var card = new Border
            {
                Width = 268, MinHeight = 118, Margin = new Thickness(0, 6, 12, 6), Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(8),
                Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = column, Tag = "atelier-tool:" + screen.Key
            };
            card.PointerEntered += delegate { card.BorderBrush = Chrome.Accent; };
            card.PointerExited += delegate { card.BorderBrush = Chrome.Border; };
            var key = screen.Key;
            card.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton == MouseButton.Left) Choose(key); };
            return card;
        }

        private void RebuildComponents(string status)
        {
            if (_components == null) return;
            _components.Children.Clear();
            var pig = PdfPigBridge.Installed;
            _components.Children.Add(ComponentRow(IconPdfPig, "PdfPig " + PdfPigPackage.Version, "Lit le texte d'un PDF : c'est lui qui sert l'import d'un PDF dans un écrit, et « Convertir » vers Word.",
                "NuGet (" + PdfPigPackage.Host + "), licence " + PdfPigPackage.License + " — " + PdfPigPackage.DownloadMegabytes + " Mo à télécharger, " + PdfPigPackage.InstalledMegabytes + " Mo installés",
                pig, true, "pdfpig", async delegate { await InstallPdfPig(); }, delegate
                {
                    PdfPigPackage.Remove(PdfPigBridge.Folder);
                    RebuildComponents(PdfPigBridge.Installed ? "PdfPig n'a pas pu être retirée (elle sert peut-être en ce moment : fermez Marabook et réessayez)." : "PdfPig est retirée. Elle se réinstallera au prochain import de PDF, si vous l'acceptez.");
                }));
            var target = Pdfium.Target;
            _components.Children.Add(ComponentRow(IconPdfium, "PDFium " + PdfiumPackage.Version, "Le moteur PDF de Chromium : il affiche les pages, les recopie d'un fichier à l'autre, les retouche, rouvre un fichier verrouillé. Tous les outils marqués « Demande PDFium » en ont besoin.",
                target == null ? "Pas prévu pour ce système (" + Pdfium.Rid + ")." : "NuGet (" + PdfiumPackage.Host + "), licence " + PdfiumPackage.License + " — " + target.DownloadMegabytes + " Mo à télécharger, " + PdfiumPackage.InstalledMegabytes + " Mo installés",
                Pdfium.Installed, target != null, "pdfium", async delegate { await InstallPdfium(false); }, delegate
                {
                    DropWork(); // les fichiers ouverts tiennent la bibliothèque
                    var removed = Pdfium.Remove();
                    RebuildComponents(removed && !Pdfium.Installed ? "PDFium est retiré. Il se réinstallera quand un outil en aura besoin, si vous l'acceptez." : "PDFium n'a pas pu être retiré (un fichier est peut-être encore ouvert : fermez Marabook et réessayez).");
                }));
            if (status != null) _components.Children.Add(new TextBlock { Text = status, Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Tag = "atelier-components-status" });
        }

        /// <summary>La ligne d'un composant. 22/11 (Rémi) : tous les cadres ont la MÊME largeur (ils
        /// s'étirent dans leur colonne), et l'état comme le bouton sont dans des colonnes de largeur
        /// fixe — d'un composant à l'autre, ils tombent à la même verticale.</summary>
        private Control ComponentRow(string icon, string name, string role, string source, bool installed, bool available, string key, Action install, Action remove)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,124,132") };
            var glyph = Icons.Make(icon, 18, Chrome.SoftText);
            glyph.Margin = new Thickness(0, 2, 12, 0);
            glyph.VerticalAlignment = VerticalAlignment.Top;
            row.Children.Add(glyph);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink });
            text.Children.Add(new TextBlock { Text = role, Foreground = Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            text.Children.Add(new TextBlock { Text = source, Foreground = Chrome.FaintText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var state = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(16, 0, 0, 0), Tag = "atelier-component-status:" + key };
            state.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = installed ? (IBrush)Chrome.Ok : Chrome.BorderStrong, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            state.Children.Add(new TextBlock { Text = installed ? "Installé" : "Non installé", Foreground = installed ? (IBrush)Chrome.Ink : Chrome.SoftText, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Tag = "atelier-component-state:" + key });
            Grid.SetColumn(state, 2);
            row.Children.Add(state);
            var action = installed
                ? Buttons.Text("Désinstaller", "Retire ce composant du disque. Il pourra se réinstaller.", Buttons.Bar, Buttons.Look.Outline)
                : Buttons.Text("Installer", "Télécharge ce composant depuis NuGet, vérifie son empreinte, puis l'installe.", Buttons.Bar, Buttons.Look.Primary);
            action.Tag = "atelier-component-action:" + key;
            action.IsEnabled = available;
            action.VerticalAlignment = VerticalAlignment.Center;
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
            action.HorizontalContentAlignment = HorizontalAlignment.Center;
            action.Margin = new Thickness(12, 0, 0, 0);
            action.Click += delegate { if (installed) remove(); else install(); };
            Grid.SetColumn(action, 3);
            row.Children.Add(action);
            return new Border { Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch, Child = row, Tag = "atelier-component:" + key };
        }

        private async Task<bool> InstallPdfPig()
        {
            if (PdfPigBridge.Installed) return true;
            var owner = Ui.OwnerOf(this);
            if (await MessageDialog.Show(owner, PdfPigPackage.Prompt, "PdfPig", MessageButtons.YesNo, MessageIcon.Question) != MessageResult.Yes) return false;
            RebuildComponents("Téléchargement de PdfPig…");
            var problem = await Task.Run(async delegate { return await PdfPigBridge.DownloadAndInstall(); });
            RebuildComponents(problem ?? "PdfPig est installée : l'import d'un PDF est prêt.");
            if (problem != null && _tool != BoxPdfaah) await MessageDialog.Show(owner, problem, "PdfPig", MessageButtons.OK, MessageIcon.Warning);
            return problem == null;
        }

        /// <summary>Installe PDFium après accord. Vrai s'il est là au bout du compte.</summary>
        private async Task<bool> InstallPdfium(bool forTool)
        {
            if (Pdfium.Installed) return true;
            var owner = Ui.OwnerOf(this);
            var target = Pdfium.Target;
            if (target == null)
            {
                await MessageDialog.Show(owner, "PDFium n'est pas prévu pour ce système (" + Pdfium.Rid + ") : cet outil n'y est pas disponible.", "PDFium", MessageButtons.OK, MessageIcon.Warning);
                return false;
            }
            if (await MessageDialog.Show(owner, PdfiumPackage.Prompt(target), "PDFium", MessageButtons.YesNo, MessageIcon.Question) != MessageResult.Yes) return false;
            RebuildComponents("Téléchargement de PDFium…");
            Busy(true, "Téléchargement de PDFium (" + target.DownloadMegabytes + " Mo)…");
            string problem;
            try { problem = await Task.Run(async delegate { return await Pdfium.DownloadAndInstall(); }); }
            finally { Busy(false, null); }
            RebuildComponents(problem ?? "PDFium est installé : les outils de PDFaah sont prêts.");
            if (problem != null && forTool) await MessageDialog.Show(owner, problem, "PDFium", MessageButtons.OK, MessageIcon.Warning);
            return problem == null;
        }

        /// <summary>Un outil a besoin de PDFium : il est là, ou on propose de l'installer.</summary>
        internal Task<bool> EnsurePdfium() { return InstallPdfium(true); }

        private TextBlock _busy;
        private void Busy(bool on, string text)
        {
            if (_busy != null) { _busy.IsVisible = on; if (text != null) _busy.Text = text; }
        }

        // ================================================================ briques communes

        private static TextBlock Heading(string text)
        {
            return new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, Margin = new Thickness(0, 22, 0, 4) };
        }

        private static TextBlock Paragraph(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
        }

        private static string Megabytes(long bytes)
        {
            var culture = CultureInfo.CurrentCulture;
            if (bytes >= 1024 * 1024) return (bytes / 1048576.0).ToString("0.0", culture) + " Mo";
            return Math.Max(1, bytes / 1024).ToString("N0", culture) + " Ko";
        }

        /// <summary>La carte d'un rapport : le résumé, puis une ligne par constat (pastille verte, orange ou rouge).</summary>
        private static Border ReportCard(string title, string summary, bool clean, List<string[]> lines, Action copy)
        {
            var column = new StackPanel();
            var top = new DockPanel();
            if (copy != null)
            {
                var button = Buttons.IconText("copy-simple-bold", "Copier le rapport", "Copie ce rapport, en texte, dans le presse-papiers", Buttons.Compact, Buttons.Look.Outline);
                button.Tag = "atelier-report-copy";
                button.Click += delegate { copy(); };
                SetDock(button, Dock.Right);
                top.Children.Add(button);
            }
            top.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            column.Children.Add(top);
            column.Children.Add(new TextBlock { Text = summary, Foreground = clean ? (IBrush)Chrome.Ok : Chrome.Warn, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4), Tag = "atelier-report-summary" });
            foreach (var line in lines)
            {
                var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
                var tone = line[0] == "2" ? (IBrush)Chrome.Danger : line[0] == "1" ? Chrome.Warn : Chrome.Ok;
                // Accessibilité (22/11) : pour qui confond les couleurs, un signe à la place du point.
                Control dot = Marabook.Settings.AppSettings.ColorBlindSafe
                    ? (Control)new TextBlock { Text = Chrome.Sign(line[0] == "2" ? 2 : line[0] == "1" ? 1 : 0), Foreground = tone, FontWeight = FontWeight.Bold, Width = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Top, Tag = "report-sign" }
                    : new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 5, 9, 0), VerticalAlignment = VerticalAlignment.Top, Background = tone };
                SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Chrome.Ink, Tag = "atelier-report-line" };
                if (line[1].Length > 0) text.Inlines.Add(new Avalonia.Controls.Documents.Run(line[1] + " — ") { FontWeight = FontWeight.SemiBold });
                text.Inlines.Add(new Avalonia.Controls.Documents.Run(line[2]));
                row.Children.Add(text);
                column.Children.Add(row);
            }
            return new Border { Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 12, 0, 0), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left, Child = column, Tag = "atelier-report" };
        }

        private Button ChooseButton(string label, Func<string, Task> run)
        {
            var button = Buttons.IconText("folder-open", label, "Choisir un PDF sur le disque. Il n'est jamais modifié.", Buttons.Bar, Buttons.Look.Primary);
            button.Tag = "atelier-choose";
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Click += async delegate
            {
                var path = await Ui.PickOpenFile(this, "Choisir un PDF", PdfFilter);
                if (path != null) await run(path);
            };
            return button;
        }

        private static async Task<byte[]> ReadAll(string path)
        {
            return await Task.Run(delegate { return File.ReadAllBytes(path); });
        }

        // ================================================================ le contrôle PDF/X

        private Panel _pdfxResult;

        private void BuildPdfX()
        {
            _page.Children.Add(Paragraph("Le contrôle que Marabook passe sur ses propres exports PDF/X, appliqué au fichier de votre choix — d'où qu'il vienne. Il lit le fichier (ses objets, ses ressources, le contenu de ses pages) et dit ce qui s'écarte de PDF/X-1a:2003, la norme que demandent la plupart des imprimeurs."));
            _page.Children.Add(Paragraph("C'est un contrôle de Marabook, pas une certification : un imprimeur ou un outil de prépresse peut en demander davantage."));
            _page.Children.Add(ChooseButton("Choisir un PDF à contrôler…", RunPdfX));
            _pdfxResult = new StackPanel { Tag = "atelier-pdfx-result" };
            _page.Children.Add(_pdfxResult);
        }

        internal async Task RunPdfX(string path)
        {
            _pdfxResult.Children.Clear();
            _pdfxResult.Children.Add(new TextBlock { Text = "Lecture de " + Path.GetFileName(path) + "…", Foreground = Chrome.SoftText, Margin = new Thickness(0, 12, 0, 0) });
            PdfXReport report;
            long size = 0;
            try
            {
                var bytes = await ReadAll(path);
                size = bytes.LongLength;
                report = await Task.Run(delegate { return PdfXInspector.Inspect(bytes); });
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException)) throw;
                _pdfxResult.Children.Clear();
                _pdfxResult.Children.Add(new TextBlock { Text = "Le fichier n'a pas pu être lu : " + error.Message, Foreground = Chrome.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
                return;
            }
            var lines = new List<string[]>();
            foreach (var problem in report.Problems) lines.Add(new[] { "2", "", problem });
            foreach (var note in report.Notes) lines.Add(new[] { "1", "À savoir", note });
            var summary = report.Conforms ? "Conforme à PDF/X-1a:2003 — " + report.Summary() : (report.Problems.Count == 1 ? "1 écart" : report.Problems.Count + " écarts") + " à PDF/X-1a:2003.";
            var plain = Path.GetFileName(path) + " (" + Megabytes(size) + ")\n" + summary + "\n" + string.Join("\n", report.Problems.ToArray()) + (report.Notes.Count > 0 ? "\nÀ savoir : " + string.Join(" ", report.Notes.ToArray()) : "");
            _pdfxResult.Children.Clear();
            _pdfxResult.Children.Add(ReportCard(Path.GetFileName(path) + " — " + Megabytes(size), summary, report.Conforms, lines, delegate { Ui.SetClipboardText(this, plain); }));
            LastPdfX = report;
        }

        internal PdfXReport LastPdfX;

        // ================================================================ le contrôle d'impression

        private CheckBox _checkBleed, _checkSize, _checkConsistency, _checkFonts;
        private TextBox _bleedBox, _widthBox, _heightBox;
        private ComboBox _formatBox;
        private Panel _printResult, _customSize;
        private string _printPath;
        internal PrintCheckReport LastPrintCheck;

        private void BuildPrintCheck()
        {
            _page.Children.Add(Paragraph("Ce qu'un imprimeur regarde d'abord dans un PDF, quel que soit le logiciel qui l'a fait. Cochez ce que vous voulez vérifier, choisissez le fichier : le rapport dit ce qui en sort."));
            var options = new StackPanel { Margin = new Thickness(0, 4, 0, 10), Tag = "atelier-print-options" };

            var bleedRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            _checkBleed = new CheckBox { Content = "Fonds perdus : au moins", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Tag = "atelier-print-bleed" };
            ToolTip.SetTip(_checkBleed, "L'image doit déborder de la coupe de chaque côté, sinon un liseré blanc paraît au massicot. 3 mm est l'usage ; certains imprimeurs demandent 5.");
            bleedRow.Children.Add(_checkBleed);
            _bleedBox = new TextBox { Text = "3", Width = 48, Margin = new Thickness(8, 0, 6, 0), HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "atelier-print-bleed-mm" };
            bleedRow.Children.Add(_bleedBox);
            bleedRow.Children.Add(new TextBlock { Text = "mm de chaque côté", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center });
            options.Children.Add(bleedRow);

            var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            _checkSize = new CheckBox { Content = "Taille des pages :", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Tag = "atelier-print-size" };
            ToolTip.SetTip(_checkSize, "Le format fini de chaque page (sa zone de coupe), comparé au format attendu.");
            sizeRow.Children.Add(_checkSize);
            _formatBox = new ComboBox { MinWidth = 230, Margin = new Thickness(8, 0, 0, 0), Tag = "atelier-print-format" };
            _formatBox.Items.Add("Celle de la première page");
            foreach (var format in PdfPrintCheck.Formats) _formatBox.Items.Add(format[0]);
            _formatBox.Items.Add("Autre…");
            _formatBox.SelectedIndex = 0;
            sizeRow.Children.Add(_formatBox);
            var custom = new StackPanel { Orientation = Orientation.Horizontal, IsVisible = false, Margin = new Thickness(10, 0, 0, 0) };
            _widthBox = new TextBox { Width = 58, Text = "148", HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "atelier-print-width" };
            _heightBox = new TextBox { Width = 58, Text = "210", HorizontalContentAlignment = HorizontalAlignment.Center, Tag = "atelier-print-height" };
            custom.Children.Add(_widthBox);
            custom.Children.Add(new TextBlock { Text = "×", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
            custom.Children.Add(_heightBox);
            custom.Children.Add(new TextBlock { Text = "mm", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            _customSize = custom;
            sizeRow.Children.Add(custom);
            _formatBox.SelectionChanged += delegate { _customSize.IsVisible = _formatBox.SelectedIndex == _formatBox.Items.Count - 1; };
            options.Children.Add(sizeRow);

            _checkConsistency = new CheckBox { Content = "Homogénéité : même taille, même sens, mêmes zones d'une page à l'autre", IsChecked = true, Margin = new Thickness(0, 8, 0, 0), Tag = "atelier-print-consistency" };
            options.Children.Add(_checkConsistency);
            _checkFonts = new CheckBox { Content = "Polices incorporées au fichier", IsChecked = true, Margin = new Thickness(0, 8, 0, 0), Tag = "atelier-print-fonts" };
            ToolTip.SetTip(_checkFonts, "Une police qui n'est pas dans le fichier sera remplacée par ce que l'imprimeur a sous la main : le texte peut bouger.");
            options.Children.Add(_checkFonts);
            _page.Children.Add(options);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(ChooseButton("Choisir un PDF à contrôler…", RunPrintCheck));
            var again = Buttons.Text("Contrôler de nouveau", "Refait le contrôle du même fichier avec ce qui est coché maintenant", Buttons.Bar, Buttons.Look.Outline);
            again.Tag = "atelier-print-again";
            again.Margin = new Thickness(8, 0, 0, 0);
            again.Click += async delegate { if (_printPath != null) await RunPrintCheck(_printPath); };
            actions.Children.Add(again);
            _page.Children.Add(actions);
            _printResult = new StackPanel { Tag = "atelier-print-result" };
            _page.Children.Add(_printResult);
        }

        private static double Number(string text, double fallback)
        {
            double value;
            var cleaned = (text ?? "").Trim().Replace(',', '.');
            return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0 && value < 5000 ? value : fallback;
        }

        internal PrintCheckOptions PrintOptions()
        {
            var options = new PrintCheckOptions
            {
                Bleed = _checkBleed.IsChecked == true, MinBleedMm = Number(_bleedBox.Text, 3),
                Size = _checkSize.IsChecked == true, Consistency = _checkConsistency.IsChecked == true, Fonts = _checkFonts.IsChecked == true
            };
            var index = _formatBox.SelectedIndex;
            if (index >= 1 && index <= PdfPrintCheck.Formats.Length)
            {
                options.WidthMm = Number(PdfPrintCheck.Formats[index - 1][1], 0);
                options.HeightMm = Number(PdfPrintCheck.Formats[index - 1][2], 0);
            }
            else if (index == _formatBox.Items.Count - 1)
            {
                options.WidthMm = Number(_widthBox.Text, 0);
                options.HeightMm = Number(_heightBox.Text, 0);
            }
            return options;
        }

        internal async Task RunPrintCheck(string path)
        {
            _printPath = path;
            _printResult.Children.Clear();
            _printResult.Children.Add(new TextBlock { Text = "Lecture de " + Path.GetFileName(path) + "…", Foreground = Chrome.SoftText, Margin = new Thickness(0, 12, 0, 0) });
            var options = PrintOptions();
            PrintCheckReport report;
            long size = 0;
            try
            {
                var bytes = await ReadAll(path);
                size = bytes.LongLength;
                report = await Task.Run(delegate { return PdfPrintCheck.Inspect(bytes, options); });
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException)) throw;
                _printResult.Children.Clear();
                _printResult.Children.Add(new TextBlock { Text = "Le fichier n'a pas pu être lu : " + error.Message, Foreground = Chrome.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
                return;
            }
            var lines = new List<string[]>();
            foreach (var line in report.Lines) lines.Add(new[] { line.Level.ToString(CultureInfo.InvariantCulture), line.Check, line.Text });
            var plain = Path.GetFileName(path) + " (" + Megabytes(size) + ")\n" + report.ToText();
            _printResult.Children.Clear();
            _printResult.Children.Add(ReportCard(Path.GetFileName(path) + " — " + Megabytes(size), report.Summary(), report.Readable && report.Problems == 0, lines, delegate { Ui.SetClipboardText(this, plain); }));
            if (report.Locked)
            {
                var unlock = Buttons.IconText(IconUnlocker, "Ouvrir l'Unlocker", "Faire d'abord une copie sans verrou de ce PDF", Buttons.Bar, Buttons.Look.Outline);
                unlock.Margin = new Thickness(0, 10, 0, 0);
                unlock.HorizontalAlignment = HorizontalAlignment.Left;
                unlock.Click += delegate { Choose(ToolUnlocker); };
                _printResult.Children.Add(unlock);
            }
            LastPrintCheck = report;
        }

        // ================================================================ l'Unlocker

        private Panel _unlockResult;
        private Pdfium.Document _unlockDocument;
        internal string LastUnlockState = "";

        private void BuildUnlocker()
        {
            _page.Children.Add(Paragraph("Un PDF peut interdire l'impression, la copie du texte ou toute modification, ou demander un mot de passe à l'ouverture. L'Unlocker en écrit une COPIE sans verrou ; le fichier d'origine n'est pas touché."));
            _page.Children.Add(Paragraph("Un PDF qui demande un mot de passe pour s'ouvrir ne se déverrouille qu'avec ce mot de passe : l'Unlocker ne le devine pas. Ne déverrouillez que des fichiers que vous avez le droit de modifier."));
            _busy = new TextBlock { Foreground = Chrome.SoftText, IsVisible = false, Margin = new Thickness(0, 0, 0, 8), Tag = "atelier-busy" };
            _toolStatus = null;
            _page.Children.Add(_busy);
            _page.Children.Add(ChooseButton("Choisir un PDF verrouillé…", RunUnlocker));
            _unlockResult = new StackPanel { Tag = "atelier-unlock-result" };
            _page.Children.Add(_unlockResult);
        }

        private void CloseUnlockDocument()
        {
            if (_unlockDocument != null) { _unlockDocument.Dispose(); _unlockDocument = null; }
        }

        internal Task RunUnlocker(string path) { return RunUnlocker(path, null); }

        /// <summary>password : pour la sonde — sinon il est demandé si le fichier en veut un.</summary>
        internal async Task RunUnlocker(string path, string password)
        {
            _unlockResult.Children.Clear();
            CloseUnlockDocument();
            LastUnlockState = "";
            if (!await EnsurePdfium()) return;
            var owner = Ui.OwnerOf(this);
            Pdfium.Document document = null;
            var asked = password != null;
            var secret = password;
            while (true)
            {
                int error;
                try
                {
                    var tried = secret;
                    var opened = await Task.Run(delegate { int code; var d = Pdfium.Open(path, tried, out code); return Tuple.Create(d, code); });
                    document = opened.Item1;
                    error = opened.Item2;
                }
                catch (Exception failure)
                {
                    if (!(failure is IOException) && !(failure is UnauthorizedAccessException) && !(failure is InvalidOperationException) && !(failure is DllNotFoundException) && !(failure is EntryPointNotFoundException)) throw;
                    _unlockResult.Children.Add(new TextBlock { Text = "Le fichier n'a pas pu être ouvert : " + failure.Message, Foreground = Chrome.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
                    LastUnlockState = "erreur";
                    return;
                }
                if (document != null) break;
                if (error != Pdfium.ErrorPassword || password != null)
                {
                    _unlockResult.Children.Add(new TextBlock { Text = Path.GetFileName(path) + " : " + Pdfium.ErrorText(error) + ".", Foreground = Chrome.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
                    LastUnlockState = error == Pdfium.ErrorPassword ? "mot de passe refusé" : "erreur";
                    return;
                }
                secret = await InputDialog.Ask(owner, "Unlocker", asked ? "Ce n'est pas le bon mot de passe. Mot de passe de ce PDF :" : "Ce PDF demande un mot de passe pour s'ouvrir. Mot de passe :", "");
                asked = true;
                if (string.IsNullOrEmpty(secret)) { LastUnlockState = "abandon"; return; }
            }
            _unlockDocument = document;
            var lines = new List<string[]>();
            if (!document.Encrypted)
            {
                LastUnlockState = "libre";
                lines.Add(new[] { "0", "", "Impression, copie du texte, modification et annotation sont permises : il n'y a rien à déverrouiller." });
                _unlockResult.Children.Add(ReportCard(Path.GetFileName(path), "Ce PDF n'est pas verrouillé (" + (document.PageCount == 1 ? "1 page" : document.PageCount + " pages") + ").", true, lines, null));
                CloseUnlockDocument();
                return;
            }
            LastUnlockState = "verrouillé";
            if (asked && !string.IsNullOrEmpty(secret)) lines.Add(new[] { "1", "Ouverture", "le fichier demandait un mot de passe ; celui que vous avez donné l'ouvre." });
            lines.Add(new[] { document.CanPrint ? "0" : "2", "Impression", document.CanPrint ? "permise." : "interdite." });
            lines.Add(new[] { document.CanCopy ? "0" : "2", "Copie du texte", document.CanCopy ? "permise." : "interdite." });
            lines.Add(new[] { document.CanModify ? "0" : "2", "Modification", document.CanModify ? "permise." : "interdite." });
            lines.Add(new[] { document.CanAnnotate ? "0" : "2", "Annotation", document.CanAnnotate ? "permise." : "interdite." });
            _unlockResult.Children.Add(ReportCard(Path.GetFileName(path), "Ce PDF est verrouillé (" + (document.PageCount == 1 ? "1 page" : document.PageCount + " pages") + ").", false, lines, null));
            var save = Buttons.IconText(IconUnlocker, "Enregistrer une copie déverrouillée…", "Écrit une copie de ce PDF sans son verrou. Le fichier d'origine n'est pas modifié.", Buttons.Bar, Buttons.Look.Primary);
            save.Tag = "atelier-unlock-save";
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Margin = new Thickness(0, 12, 0, 0);
            save.Click += async delegate
            {
                var target = await Ui.PickSaveFile(this, "Enregistrer la copie déverrouillée", PdfFilter, Path.GetFileNameWithoutExtension(path) + " (déverrouillé).pdf");
                if (target == null) return;
                await SaveUnlocked(path, target);
            };
            _unlockResult.Children.Add(save);
        }

        /// <summary>Écrit la copie sans verrou du fichier ouvert par RunUnlocker. Vrai si c'est fait.</summary>
        internal async Task<bool> SaveUnlocked(string source, string target)
        {
            var owner = Ui.OwnerOf(this);
            if (_unlockDocument == null) return false;
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            {
                await MessageDialog.Show(owner, "La copie déverrouillée ne peut pas remplacer le fichier d'origine : choisissez un autre nom.", "Unlocker", MessageButtons.OK, MessageIcon.Warning);
                return false;
            }
            try
            {
                var document = _unlockDocument;
                var bytes = await Task.Run(delegate { return Pdfium.Unlock(document); });
                await Task.Run(delegate { File.WriteAllBytes(target, bytes); });
                _unlockResult.Children.Add(new TextBlock { Text = "Copie déverrouillée écrite : " + target + " (" + Megabytes(bytes.LongLength) + ").", Foreground = Chrome.Ok, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Tag = "atelier-unlock-done" });
                LastUnlockState = "déverrouillé";
                return true;
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is InvalidOperationException)) throw;
                _unlockResult.Children.Add(new TextBlock { Text = "La copie n'a pas pu être écrite : " + error.Message, Foreground = Chrome.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });
                return false;
            }
        }

        // — Sonde.
        internal ChopChopView ChopForProbe { get { return _chop; } }
        internal void ShowForProbe(string tool) { Show(tool); }
        internal void RefreshComponentsForProbe() { RebuildComponents(null); }
        internal void CloseFilesForProbe() { DropWork(); }
        /// <summary>Rend tous les fichiers tenus (avant de désinstaller PDFium depuis les Préférences).</summary>
        internal void ReleaseFiles() { DropWork(); if (_tool != ToolHome && _tool != BoxPdfaah && _tool != BoxWgyc) Show(_tool); }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.App
{
    /// <summary>LES OUTILS DE PDFAAH VENUS AVEC LE LOT 30 (2.0.0, 22/11/2026,
    /// Rémi — « le comparatif de base : ilovePDF ») : scinder, fusionner,
    /// pages blanches, pivoter, filigrane, signer, compresser, convertir
    /// (JPEG, Word), verrouiller. Tous suivent le même modèle :
    ///   1. on choisit un PDF — il est lu en mémoire, jamais modifié ;
    ///   2. on règle l'outil (un aperçu montre ce que ça donnera, quand il y a
    ///      quelque chose à voir) ;
    ///   3. « Enregistrer sous… » écrit le résultat dans un AUTRE fichier.
    /// Un PDF verrouillé passe d'abord par l'Unlocker. Quitter l'outil retire
    /// le PDF ; s'il y a un résultat pas encore écrit, on est prévenu.
    /// Chaque outil a sa méthode « Run… » qui fait le travail sans dialogue :
    /// le bouton l'appelle après avoir demandé où écrire, la sonde l'appelle
    /// directement.</summary>
    public partial class AtelierView
    {
        internal sealed class PdfJob
        {
            public string Path;
            public byte[] Bytes;
            public int Pages;
            public List<double[]> Sizes = new List<double[]>();
            /// <summary>Le résultat de l'outil a été écrit depuis le dernier réglage.</summary>
            public bool Saved;
            public string Name { get { return System.IO.Path.GetFileName(Path); } }
            public string Stem { get { return System.IO.Path.GetFileNameWithoutExtension(Path); } }
        }

        private const double PtPerMm = 72.0 / 25.4;
        private PdfJob _job;
        private StackPanel _toolFile, _toolOptions;
        private TextBlock _toolStatus;
        private Image _previewImage;
        private TextBlock _previewNote;
        private int _previewGeneration;
        internal string LastToolStatus = "";
        internal bool LastToolOk;

        private bool ToolHasUnsavedWork()
        {
            if (_tool == ToolMerge) return _mergeDocs.Count > 1 && !_mergeSaved;
            return _job != null && !_job.Saved;
        }

        private bool ToolHoldsFile() { return _job != null || _mergeDocs.Count > 0; }

        private void DropToolWork()
        {
            _job = null;
            _previewGeneration++;
            foreach (var document in _mergeDocs) document.Dispose();
            _mergeDocs.Clear();
            _mergeSaved = false;
            _signPixels = null;
            _compressed = null;
            LastToolStatus = "";
            LastToolOk = false;
        }

        // ================================================================ le gabarit d'un outil

        private static readonly Dictionary<string, string[]> ToolIntro = new Dictionary<string, string[]>
        {
            { ToolSplit, new[] { "Coupe un PDF en deux, après la page que vous indiquez : la première partie d'un côté, le reste de l'autre. Deux fichiers s'écrivent dans le dossier que vous choisissez ; le PDF d'origine reste entier." } },
            { ToolBlank, new[] { "Glisse des pages blanches dans un PDF : au début, à la fin, ou après une page. Elles prennent la taille des pages du PDF, ou celle que vous donnez — une page de garde, un verso vierge pour qu'un chapitre commence en belle page, une feuille de notes." } },
            { ToolRotate, new[] { "Tourne des pages par quarts de tour : un scan couché, une page à l'envers. Toutes les pages, les paires, les impaires, ou celles que vous listez." } },
            { ToolWatermark, new[] { "Écrit un texte par-dessus les pages : « BROUILLON », « Épreuve non corrigée », votre nom, celui du destinataire. En travers, au centre, en tête ou en pied ; plus ou moins grand, plus ou moins discret.", "Le filigrane fait partie de la page écrite : il ne s'enlève pas d'un clic. Il ne remplace pas un verrou." } },
            { ToolSign, new[] { "Appose votre signature sur une page : l'image d'une signature (un PNG à fond transparent, ou la photo d'une signature sur papier blanc), ou une signature tracée ici à la souris ou au stylet. Cliquez dans la page pour la placer.", "C'est une signature MANUSCRITE posée sur la page — ce que demandent un contrat d'édition ou un bon à tirer renvoyé par courriel. Ce n'est pas une signature électronique à certificat : elle ne prouve pas à elle seule qui a signé, ni que le fichier n'a pas changé depuis." } },
            { ToolCompress, new[] { "Allège un PDF. « Sans perte » le réécrit en ne gardant que ce qui sert : rien ne change à ce qu'on voit, et le gain dépend de ce que le fichier traînait. Les deux autres niveaux réduisent en plus la résolution des images : parfait pour un envoi ou une lecture à l'écran.", "N'envoyez pas à un imprimeur un PDF dont les images ont été réduites. Les images en CMJN ou en tons directs, et celles qui ont de la transparence, ne sont jamais touchées." } },
            { ToolConvert, new[] { "Tire d'un PDF des images JPEG — une par page, à la résolution voulue —, ou un document Word.", "Vers Word, c'est le TEXTE qui passe (paragraphes, titres, gras et italique), comme à l'import d'un PDF dans un écrit : pas la mise en page, ni les images, ni les tableaux. Un PDF scanné, fait d'images, n'a pas de texte à donner." } },
            { ToolLock, new[] { "Écrit une copie du PDF qui ne s'ouvre qu'avec un mot de passe. Le contenu est chiffré (AES 128 bits, le chiffrement que lisent tous les lecteurs de PDF) : sans le mot de passe, il ne se lit pas.", "Retenez ce mot de passe : Marabook ne le garde nulle part, et l'Unlocker ne le devine pas. La protection vaut ce que vaut le mot de passe — une phrase longue plutôt qu'un mot court." } }
        };

        private void BuildTool(string tool)
        {
            if (tool == ToolMerge) { BuildMerge(); return; }
            string[] intro;
            if (ToolIntro.TryGetValue(tool, out intro)) foreach (var paragraph in intro) _page.Children.Add(Paragraph(paragraph));
            var choose = Buttons.IconText("folder-open", "Choisir un PDF…", "Choisir un PDF sur le disque. Il n'est jamais modifié.", Buttons.Bar, Buttons.Look.Primary);
            choose.Tag = "atelier-choose";
            choose.HorizontalAlignment = HorizontalAlignment.Left;
            choose.Click += async delegate
            {
                var path = await Ui.PickOpenFile(this, "Choisir un PDF", PdfFilter);
                if (path != null) await LoadJob(path);
            };
            _page.Children.Add(choose);
            _toolFile = new StackPanel { Tag = "atelier-tool-file" };
            _page.Children.Add(_toolFile);
            _toolOptions = new StackPanel { Tag = "atelier-tool-options", MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left };
            _page.Children.Add(_toolOptions);
            _toolStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, Tag = "atelier-tool-status" };
            _page.Children.Add(_toolStatus);
        }

        private void Say(string text, bool ok, bool problem)
        {
            LastToolStatus = text ?? "";
            LastToolOk = ok;
            if (_toolStatus == null) return;
            _toolStatus.Text = text ?? "";
            _toolStatus.Foreground = problem ? (IBrush)Chrome.Danger : ok ? Chrome.Ok : Chrome.SoftText;
            _toolStatus.IsVisible = !string.IsNullOrEmpty(text);
        }

        /// <summary>Un réglage a bougé : le résultat écrit n'est plus celui de l'écran.</summary>
        private void Touch()
        {
            if (_job != null) _job.Saved = false;
        }

        private static bool Expected(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException || error is DllNotFoundException || error is EntryPointNotFoundException;
        }

        /// <summary>Lit ce PDF pour l'outil en cours. Vrai s'il est prêt ; sinon l'écran dit pourquoi.</summary>
        internal async Task<bool> LoadJob(string path)
        {
            _job = null;
            _compressed = null;
            _previewGeneration++;
            if (_toolFile != null) _toolFile.Children.Clear();
            if (_toolOptions != null) _toolOptions.Children.Clear();
            Say(null, false, false);
            if (!await EnsurePdfium()) return false;
            var screen = ScreenOf(_tool);
            try
            {
                var bytes = await ReadAll(path);
                var opened = await Task.Run(delegate { int code; var d = Pdfium.Open(bytes, null, out code); return Tuple.Create(d, code); });
                var document = opened.Item1;
                if (document == null || document.Encrypted)
                {
                    var locked = document != null || opened.Item2 == Pdfium.ErrorPassword;
                    if (document != null) document.Dispose();
                    Say(Path.GetFileName(path) + (locked ? " est verrouillé : faites-en d'abord une copie sans verrou avec l'Unlocker, puis revenez ici avec cette copie." : " : " + Pdfium.ErrorText(opened.Item2) + "."), false, true);
                    if (locked && _toolOptions != null)
                    {
                        var unlock = Buttons.IconText(IconUnlocker, "Ouvrir l'Unlocker", "Faire d'abord une copie sans verrou de ce PDF", Buttons.Bar, Buttons.Look.Outline);
                        unlock.Margin = new Thickness(0, 10, 0, 0);
                        unlock.HorizontalAlignment = HorizontalAlignment.Left;
                        unlock.Click += delegate { Choose(ToolUnlocker); };
                        _toolOptions.Children.Add(unlock);
                    }
                    return false;
                }
                var job = new PdfJob { Path = path, Bytes = bytes, Pages = document.PageCount, Sizes = document.Sizes(), Saved = true };
                document.Dispose();
                if (job.Pages == 0) { Say(job.Name + " n'a aucune page.", false, true); return false; }
                _job = job;
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                Say("Le fichier n'a pas pu être ouvert : " + error.Message, false, true);
                return false;
            }
            FillTool();
            return true;
        }

        private static string Mm(double points)
        {
            return (Math.Round(points / PtPerMm * 10) / 10).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR"));
        }

        private static string PagesLabel(int count) { return count == 1 ? "1 page" : count + " pages"; }

        private void FillTool()
        {
            if (_job == null || _toolFile == null) return;
            _toolFile.Children.Clear();
            _toolOptions.Children.Clear();
            var first = _job.Sizes.Count > 0 ? _job.Sizes[0] : new[] { 595.0, 842.0 };
            var card = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = Icons.Make(IconPdfaah, 16, Chrome.SoftText);
            glyph.Margin = new Thickness(0, 0, 10, 0);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            card.Children.Add(glyph);
            card.Children.Add(new TextBlock { Text = _job.Name, FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420, Tag = "atelier-tool-name" });
            card.Children.Add(new TextBlock { Text = "   " + PagesLabel(_job.Pages) + " · " + Mm(first[0]) + " × " + Mm(first[1]) + " mm · " + Megabytes(_job.Bytes.LongLength), Foreground = Chrome.SoftText, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            _toolFile.Children.Add(new Border { Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 12, 0, 4), HorizontalAlignment = HorizontalAlignment.Left, Child = card, Tag = "atelier-tool-card" });
            if (_tool == ToolSplit) FillSplit();
            else if (_tool == ToolBlank) FillBlank();
            else if (_tool == ToolRotate) FillRotate();
            else if (_tool == ToolWatermark) FillWatermark();
            else if (_tool == ToolSign) FillSign();
            else if (_tool == ToolCompress) FillCompress();
            else if (_tool == ToolConvert) FillConvert();
            else if (_tool == ToolLock) FillLock();
        }

        // ---- briques des réglages

        private static TextBlock Label(string text)
        {
            return new TextBlock { Text = text, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        }

        private static StackPanel Row(double top = 10)
        {
            return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, top, 0, 0) };
        }

        private static TextBox NumberBox(string text, string tag, double width = 56)
        {
            return new TextBox { Text = text, Width = width, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = tag };
        }

        private static ComboBox Choice(string tag, int selected, params string[] items)
        {
            var box = new ComboBox { MinWidth = 190, VerticalAlignment = VerticalAlignment.Center, Tag = tag };
            foreach (var item in items) box.Items.Add(item);
            box.SelectedIndex = selected;
            return box;
        }

        private static int Whole(string text, int fallback)
        {
            int value;
            return int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        /// <summary>Le bouton principal d'un outil : il demande où écrire, puis lance « run(chemin) ».</summary>
        private Button SaveButton(string label, string tip, string suggested, Func<string, Task> run)
        {
            var button = Buttons.IconText("file-arrow-down-bold", label, tip, Buttons.Bar, Buttons.Look.Primary);
            button.Tag = "atelier-tool-save";
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 16, 0, 0);
            button.Click += async delegate
            {
                if (_job == null) return;
                var target = await Ui.PickSaveFile(this, label.TrimEnd('…'), PdfFilter, suggested);
                if (target != null) await run(target);
            };
            return button;
        }

        /// <summary>Écrit le PDF que rend « make », jamais par-dessus le fichier ouvert. Vrai si c'est fait.</summary>
        private async Task<bool> WriteResult(string target, Func<byte[]> make, string done)
        {
            if (_job == null) return false;
            if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(_job.Path), StringComparison.OrdinalIgnoreCase))
            {
                Say("Le résultat ne peut pas remplacer le fichier d'origine : choisissez un autre nom.", false, true);
                return false;
            }
            try
            {
                Say("Écriture de " + Path.GetFileName(target) + "…", false, false);
                var bytes = await Task.Run(make);
                if (bytes == null || bytes.Length == 0) { Say("Le PDF n'a pas pu être préparé.", false, true); return false; }
                await Task.Run(delegate { File.WriteAllBytes(target, bytes); });
                if (_job != null) _job.Saved = true;
                Say(done + " " + target + " (" + Megabytes(bytes.LongLength) + ").", true, false);
                return true;
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                Say("Le PDF n'a pas pu être écrit : " + error.Message, false, true);
                return false;
            }
        }

        /// <summary>Un nom libre dans ce dossier : « nom.pdf », sinon « nom (2).pdf »…</summary>
        private static string FreeName(string folder, string stem, string extension)
        {
            var path = Path.Combine(folder, stem + extension);
            for (var n = 2; File.Exists(path) && n < 1000; n++) path = Path.Combine(folder, stem + " (" + n + ")" + extension);
            return path;
        }

        // ---- l'aperçu

        private Control PreviewBlock(string note)
        {
            _previewImage = new Image { Stretch = Stretch.Uniform, MaxWidth = 300, MaxHeight = 380, Tag = "atelier-preview-image" };
            _previewNote = new TextBlock { Text = note, Foreground = Chrome.FaintText, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };
            var column = new StackPanel { Margin = new Thickness(28, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            column.Children.Add(new Border { Background = Chrome.PrintPaper, BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Child = _previewImage, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 60, MinHeight = 80, Tag = "atelier-preview" });
            column.Children.Add(_previewNote);
            return column;
        }

        /// <summary>Une page d'un PDF, dessinée : pour un aperçu. null si elle ne se dessine pas.</summary>
        internal static Bitmap RenderPage(byte[] pdf, int index, int longest, out double widthPt, out double heightPt)
        {
            widthPt = 0;
            heightPt = 0;
            int error;
            using (var document = Pdfium.Open(pdf, null, out error))
            {
                if (document == null || index < 0 || index >= document.PageCount) return null;
                var sizes = document.Sizes();
                widthPt = sizes[index][0];
                heightPt = sizes[index][1];
                int width, height;
                var pixels = document.Render(index, longest, out width, out height);
                if (pixels == null || width <= 0 || height <= 0) return null;
                var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                using (var locked = bitmap.Lock())
                    for (var y = 0; y < height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(pixels, y * width * 4, IntPtr.Add(locked.Address, y * locked.RowBytes), width * 4);
                return bitmap;
            }
        }

        /// <summary>Montre, dans l'aperçu, la page « page » du PDF que rend « make » (hors du fil de l'interface).</summary>
        private async void Preview(Func<byte[]> make, int page, string note)
        {
            if (_previewImage == null || _job == null) return;
            var generation = ++_previewGeneration;
            var image = _previewImage;
            var label = _previewNote;
            Bitmap bitmap = null;
            string problem = null;
            try
            {
                bitmap = await Task.Run(delegate
                {
                    double w, h;
                    var bytes = make();
                    return bytes == null ? null : RenderPage(bytes, page, 760, out w, out h);
                });
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                problem = error.Message;
            }
            if (generation != _previewGeneration) return; // un autre réglage est passé devant
            image.Source = bitmap;
            if (label != null) label.Text = problem != null ? "L'aperçu n'a pas pu être fait : " + problem : note;
            LastPreviewDrawn = bitmap != null;
        }

        internal bool LastPreviewDrawn;

        // ================================================================ scinder

        private TextBox _splitAt;
        private TextBlock _splitNote;

        private void FillSplit()
        {
            if (_job.Pages < 2)
            {
                _toolOptions.Children.Add(Paragraph("Ce PDF n'a qu'une page : il n'y a rien à couper."));
                return;
            }
            var row = Row();
            row.Children.Add(Label("Couper après la page"));
            _splitAt = NumberBox(Math.Max(1, _job.Pages / 2).ToString(CultureInfo.InvariantCulture), "atelier-split-at");
            row.Children.Add(_splitAt);
            row.Children.Add(new TextBlock { Text = "sur " + _job.Pages, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            _toolOptions.Children.Add(row);
            _splitNote = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = "atelier-split-note" };
            _toolOptions.Children.Add(_splitNote);
            _splitAt.TextChanged += delegate { Touch(); DescribeSplit(); };
            DescribeSplit();
            var go = Buttons.IconText("folder-open", "Choisir le dossier et scinder…", "Écrit les deux parties dans le dossier choisi. Le PDF d'origine reste entier.", Buttons.Bar, Buttons.Look.Primary);
            go.Tag = "atelier-tool-save";
            go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Margin = new Thickness(0, 16, 0, 0);
            go.Click += async delegate
            {
                if (_job == null) return;
                var folder = await Ui.PickFolder(this, "Où écrire les deux parties");
                if (folder == null) return;
                await RunSplit(SplitPoint(), FreeName(folder, _job.Stem + " (1 sur 2)", ".pdf"), FreeName(folder, _job.Stem + " (2 sur 2)", ".pdf"));
            };
            _toolOptions.Children.Add(go);
        }

        private int SplitPoint()
        {
            return Math.Max(1, Math.Min(_job.Pages - 1, Whole(_splitAt == null ? null : _splitAt.Text, _job.Pages / 2)));
        }

        private void DescribeSplit()
        {
            if (_splitNote == null || _job == null) return;
            var at = SplitPoint();
            _splitNote.Text = "Première partie : " + (at == 1 ? "la page 1" : "les pages 1 à " + at) + " (" + PagesLabel(at) + "). Seconde partie : " + (at + 1 == _job.Pages ? "la page " + _job.Pages : "les pages " + (at + 1) + " à " + _job.Pages) + " (" + PagesLabel(_job.Pages - at) + ").";
        }

        /// <summary>Coupe après la page « after » (1 = après la première) : deux fichiers. Vrai si c'est fait.</summary>
        internal async Task<bool> RunSplit(int after, string firstPath, string secondPath)
        {
            if (_job == null || _job.Pages < 2) return false;
            after = Math.Max(1, Math.Min(_job.Pages - 1, after));
            var job = _job;
            try
            {
                Say("Écriture des deux parties…", false, false);
                var parts = await Task.Run(delegate
                {
                    int error;
                    using (var document = Pdfium.Open(job.Bytes, null, out error))
                    {
                        if (document == null) throw new InvalidOperationException(Pdfium.ErrorText(error) + ".");
                        document.Path = job.Path;
                        var head = new List<int>();
                        var tail = new List<int>();
                        for (var i = 0; i < document.PageCount; i++) (i < after ? head : tail).Add(i);
                        return new[] { Pdfium.Extract(document, head), Pdfium.Extract(document, tail) };
                    }
                });
                await Task.Run(delegate { File.WriteAllBytes(firstPath, parts[0]); File.WriteAllBytes(secondPath, parts[1]); });
                job.Saved = true;
                Say("Deux fichiers écrits : " + Path.GetFileName(firstPath) + " (" + PagesLabel(after) + ") et " + Path.GetFileName(secondPath) + " (" + PagesLabel(job.Pages - after) + "), dans " + Path.GetDirectoryName(firstPath) + ".", true, false);
                return true;
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                Say("Le PDF n'a pas pu être scindé : " + error.Message, false, true);
                return false;
            }
        }

        // ================================================================ pages blanches

        private ComboBox _blankWhere, _blankFormat;
        private TextBox _blankAfter, _blankCount, _blankWidth, _blankHeight;
        private RadioButton _blankSame, _blankCustom;

        private void FillBlank()
        {
            var where = Row();
            where.Children.Add(Label("Où :"));
            _blankWhere = Choice("atelier-blank-where", 0, "À la fin", "Au début", "Après la page…");
            where.Children.Add(_blankWhere);
            _blankAfter = NumberBox("1", "atelier-blank-after");
            _blankAfter.Margin = new Thickness(8, 0, 0, 0);
            _blankAfter.IsVisible = false;
            where.Children.Add(_blankAfter);
            _toolOptions.Children.Add(where);

            var count = Row();
            count.Children.Add(Label("Combien :"));
            _blankCount = NumberBox("1", "atelier-blank-count");
            count.Children.Add(_blankCount);
            _toolOptions.Children.Add(count);

            var first = _job.Sizes[0];
            _blankSame = new RadioButton { GroupName = "blank-size", IsChecked = true, Margin = new Thickness(0, 12, 0, 0), Tag = "atelier-blank-same" };
            _toolOptions.Children.Add(_blankSame);
            _blankCustom = new RadioButton { GroupName = "blank-size", Content = "Une autre taille :", Margin = new Thickness(0, 6, 0, 0), Tag = "atelier-blank-custom" };
            var custom = Row(6);
            custom.Children.Add(_blankCustom);
            _blankFormat = new ComboBox { MinWidth = 190, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "atelier-blank-format" };
            foreach (var format in PdfPrintCheck.Formats) _blankFormat.Items.Add(format[0]);
            _blankFormat.Items.Add("Autre…");
            _blankFormat.SelectedIndex = 0;
            custom.Children.Add(_blankFormat);
            _blankWidth = NumberBox(Mm(first[0]), "atelier-blank-width", 62);
            _blankWidth.Margin = new Thickness(10, 0, 0, 0);
            _blankHeight = NumberBox(Mm(first[1]), "atelier-blank-height", 62);
            custom.Children.Add(_blankWidth);
            custom.Children.Add(new TextBlock { Text = "×", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
            custom.Children.Add(_blankHeight);
            custom.Children.Add(new TextBlock { Text = "mm", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            _toolOptions.Children.Add(custom);

            Action sync = delegate
            {
                Touch();
                _blankAfter.IsVisible = _blankWhere.SelectedIndex == 2;
                var size = BlankNeighbour();
                _blankSame.Content = "À la taille du PDF : " + Mm(size[0]) + " × " + Mm(size[1]) + " mm";
                var customOn = _blankCustom.IsChecked == true;
                _blankFormat.IsEnabled = customOn;
                var free = _blankFormat.SelectedIndex == _blankFormat.Items.Count - 1;
                _blankWidth.IsEnabled = customOn && free;
                _blankHeight.IsEnabled = customOn && free;
                if (!free && _blankFormat.SelectedIndex >= 0 && _blankFormat.SelectedIndex < PdfPrintCheck.Formats.Length)
                {
                    _blankWidth.Text = PdfPrintCheck.Formats[_blankFormat.SelectedIndex][1];
                    _blankHeight.Text = PdfPrintCheck.Formats[_blankFormat.SelectedIndex][2];
                }
            };
            _blankWhere.SelectionChanged += delegate { sync(); };
            _blankFormat.SelectionChanged += delegate { sync(); };
            _blankAfter.TextChanged += delegate { sync(); };
            _blankSame.IsCheckedChanged += delegate { sync(); };
            _blankCustom.IsCheckedChanged += delegate { sync(); };
            sync();
            _job.Saved = true; // rien n'a encore été réglé
            _toolOptions.Children.Add(SaveButton("Enregistrer sous…", "Écrit le PDF avec ses pages blanches, dans un nouveau fichier", _job.Stem + " (pages blanches).pdf", async delegate(string target)
            {
                double w, h;
                BlankSize(out w, out h);
                await RunBlank(BlankPosition(), Math.Max(1, Math.Min(500, Whole(_blankCount.Text, 1))), w, h, target);
            }));
        }

        /// <summary>Avant quelle page (0 = au début ; le nombre de pages = à la fin).</summary>
        private int BlankPosition()
        {
            if (_blankWhere.SelectedIndex == 1) return 0;
            if (_blankWhere.SelectedIndex == 2) return Math.Max(1, Math.Min(_job.Pages, Whole(_blankAfter.Text, 1)));
            return _job.Pages;
        }

        /// <summary>La taille de la page voisine de l'endroit choisi : celle que prend une page blanche « à la taille du PDF ».</summary>
        private double[] BlankNeighbour()
        {
            var at = BlankPosition();
            return _job.Sizes[Math.Max(0, Math.Min(_job.Sizes.Count - 1, at == 0 ? 0 : at - 1))];
        }

        private void BlankSize(out double widthPt, out double heightPt)
        {
            var size = BlankNeighbour();
            widthPt = size[0];
            heightPt = size[1];
            if (_blankCustom.IsChecked != true) return;
            var w = Number(_blankWidth.Text, 0);
            var h = Number(_blankHeight.Text, 0);
            if (w >= 10 && h >= 10 && w <= 5000 && h <= 5000) { widthPt = w * PtPerMm; heightPt = h * PtPerMm; }
        }

        internal async Task<bool> RunBlank(int at, int count, double widthPt, double heightPt, string target)
        {
            if (_job == null) return false;
            var job = _job;
            return await WriteResult(target, delegate { return Pdfium.InsertBlank(job.Bytes, at, count, widthPt, heightPt); },
                (count == 1 ? "1 page blanche ajoutée" : count + " pages blanches ajoutées") + " (" + Mm(widthPt) + " × " + Mm(heightPt) + " mm) :");
        }

        // ================================================================ pivoter

        private ComboBox _rotateTurn, _rotateWhich;
        private TextBox _rotateList;

        private void FillRotate()
        {
            var columns = new StackPanel { Orientation = Orientation.Horizontal };
            var options = new StackPanel();
            var turn = Row();
            turn.Children.Add(Label("Tourner de :"));
            _rotateTurn = Choice("atelier-rotate-turn", 0, "90° vers la droite", "90° vers la gauche", "180° (tête en bas)");
            turn.Children.Add(_rotateTurn);
            options.Children.Add(turn);
            var which = Row();
            which.Children.Add(Label("Pages :"));
            _rotateWhich = Choice("atelier-rotate-which", 0, "Toutes", "Les impaires", "Les paires", "Certaines…");
            which.Children.Add(_rotateWhich);
            options.Children.Add(which);
            _rotateList = new TextBox { Watermark = "1-3, 7", Width = 200, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, Tag = "atelier-rotate-list" };
            options.Children.Add(_rotateList);
            options.Children.Add(SaveButton("Enregistrer sous…", "Écrit le PDF aux pages tournées, dans un nouveau fichier", _job.Stem + " (pivoté).pdf", async delegate(string target)
            {
                string problem;
                var pages = RotatePages(out problem);
                if (pages == null) { Say(problem, false, true); return; }
                await RunRotate(RotateTurns(), pages, target);
            }));
            columns.Children.Add(options);
            columns.Children.Add(PreviewBlock(""));
            _toolOptions.Children.Add(columns);
            Action sync = delegate
            {
                Touch();
                _rotateList.IsVisible = _rotateWhich.SelectedIndex == 3;
                string problem;
                var pages = RotatePages(out problem);
                if (pages == null) { if (_previewNote != null) _previewNote.Text = problem; return; }
                var job = _job;
                var turns = RotateTurns();
                var shown = pages[0];
                Preview(delegate { return Pdfium.Rotate(job.Bytes, delegate(int index) { return index == shown ? turns : 0; }); }, shown, "La page " + (shown + 1) + ", une fois tournée — " + PagesLabel(pages.Count) + " à tourner.");
            };
            _rotateTurn.SelectionChanged += delegate { sync(); };
            _rotateWhich.SelectionChanged += delegate { sync(); };
            _rotateList.TextChanged += delegate { sync(); };
            sync();
            _job.Saved = true;
        }

        private int RotateTurns()
        {
            return _rotateTurn.SelectedIndex == 1 ? 3 : _rotateTurn.SelectedIndex == 2 ? 2 : 1;
        }

        private List<int> RotatePages(out string problem)
        {
            problem = null;
            var pages = new List<int>();
            var which = _rotateWhich.SelectedIndex;
            if (which == 3) return PdfPages.Parse(_rotateList.Text, _job.Pages, out problem);
            for (var i = 0; i < _job.Pages; i++)
                if (which == 0 || (which == 1 && i % 2 == 0) || (which == 2 && i % 2 == 1)) pages.Add(i);
            if (pages.Count == 0) problem = "Ce PDF n'a pas de page paire.";
            return pages.Count == 0 ? null : pages;
        }

        /// <summary>turns : quarts de tour vers la droite (1, 2 ou 3) ; pages : indices à partir de 0.</summary>
        internal async Task<bool> RunRotate(int turns, IList<int> pages, string target)
        {
            if (_job == null || pages == null || pages.Count == 0) return false;
            var job = _job;
            var chosen = new HashSet<int>(pages);
            return await WriteResult(target, delegate { return Pdfium.Rotate(job.Bytes, delegate(int index) { return chosen.Contains(index) ? turns : 0; }); },
                PagesLabel(pages.Count) + (pages.Count == 1 ? " tournée :" : " tournées :"));
        }

        // ================================================================ filigrane

        private TextBox _markText;
        private ComboBox _markLayout, _markSize, _markPages;
        private Slider _markOpacity;
        private CheckBox _markBold;
        private WrapPanel _markColors;
        private string _markColor = "#7F8C8D";

        private void FillWatermark()
        {
            var columns = new StackPanel { Orientation = Orientation.Horizontal };
            var options = new StackPanel { Width = 470 };
            var text = Row();
            text.Children.Add(Label("Texte :"));
            _markText = new TextBox { Text = "BROUILLON", Width = 320, Tag = "atelier-mark-text" };
            text.Children.Add(_markText);
            options.Children.Add(text);
            var layout = Row();
            layout.Children.Add(Label("Disposition :"));
            _markLayout = Choice("atelier-mark-layout", 0, "En travers de la page", "Au centre, à l'horizontale", "En tête de page", "En pied de page");
            layout.Children.Add(_markLayout);
            options.Children.Add(layout);
            var size = Row();
            size.Children.Add(Label("Taille :"));
            _markSize = Choice("atelier-mark-size", 1, "Petite", "Moyenne", "Grande");
            _markSize.MinWidth = 120;
            size.Children.Add(_markSize);
            _markBold = new CheckBox { Content = "Gras", IsChecked = true, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = "atelier-mark-bold" };
            size.Children.Add(_markBold);
            options.Children.Add(size);
            var opacity = Row();
            opacity.Children.Add(Label("Opacité :"));
            _markOpacity = new Slider { Minimum = 5, Maximum = 100, Value = 20, Width = 200, VerticalAlignment = VerticalAlignment.Center, Tag = "atelier-mark-opacity" };
            var opacityLabel = new TextBlock { Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), Width = 44 };
            opacity.Children.Add(_markOpacity);
            opacity.Children.Add(opacityLabel);
            options.Children.Add(opacity);
            var color = Row();
            color.Children.Add(Label("Couleur :"));
            _markColors = new WrapPanel { MaxWidth = 380, Tag = "atelier-mark-colors" };
            color.Children.Add(_markColors);
            options.Children.Add(color);
            var pages = Row(4);
            pages.Children.Add(Label("Pages :"));
            _markPages = Choice("atelier-mark-pages", 0, "Toutes", "Toutes sauf la première", "La première seulement");
            pages.Children.Add(_markPages);
            options.Children.Add(pages);
            options.Children.Add(SaveButton("Enregistrer sous…", "Écrit le PDF avec son filigrane, dans un nouveau fichier", _job.Stem + " (filigrane).pdf", async delegate(string target) { await RunWatermark(ReadWatermark(), target); }));
            columns.Children.Add(options);
            columns.Children.Add(PreviewBlock(""));
            _toolOptions.Children.Add(columns);

            DispatcherTimerLite timer = null;
            Action refresh = delegate
            {
                Touch();
                opacityLabel.Text = ((int)Math.Round(_markOpacity.Value)).ToString(CultureInfo.InvariantCulture) + " %";
                // Un réglage qui glisse : l'aperçu attend qu'il se pose.
                if (timer != null) timer.Stop();
                timer = DispatcherTimerLite.After(220, delegate { PreviewWatermark(); });
            };
            Action colors = null;
            colors = delegate
            {
                ColorMenus.FillSwatches(_markColors, Ui.OwnerOf(this), null, _markColor, false, null, delegate(string hex) { if (hex != null) _markColor = hex; colors(); refresh(); });
            };
            colors();
            _markText.TextChanged += delegate { refresh(); };
            _markLayout.SelectionChanged += delegate { refresh(); };
            _markSize.SelectionChanged += delegate { refresh(); };
            _markPages.SelectionChanged += delegate { refresh(); };
            _markBold.IsCheckedChanged += delegate { refresh(); };
            _markOpacity.ValueChanged += delegate { refresh(); };
            opacityLabel.Text = "20 %";
            PreviewWatermark();
            _job.Saved = true;
        }

        private Pdfium.Watermark ReadWatermark()
        {
            var ink = Ink.Parse(ColorPalette.Normalize(_markColor) ?? "#7F8C8D");
            var which = _markPages.SelectedIndex;
            return new Pdfium.Watermark
            {
                Text = (_markText.Text ?? "").Trim(),
                Layout = Math.Max(0, _markLayout.SelectedIndex),
                Size = _markSize.SelectedIndex == 0 ? 0.4 : _markSize.SelectedIndex == 2 ? 0.95 : 0.7,
                Opacity = _markOpacity.Value / 100.0,
                Bold = _markBold.IsChecked == true,
                R = ink.R, G = ink.G, B = ink.B,
                Pages = which == 0 ? (Func<int, int, bool>)null : which == 1 ? delegate(int index, int count) { return index > 0 || count == 1; } : delegate(int index, int count) { return index == 0; }
            };
        }

        private void PreviewWatermark()
        {
            if (_job == null || _markText == null) return;
            var mark = ReadWatermark();
            if (mark.Text.Length == 0) { if (_previewNote != null) _previewNote.Text = "Écrivez le texte du filigrane."; return; }
            var job = _job;
            var shown = _markPages.SelectedIndex == 1 && job.Pages > 1 ? 1 : 0;
            var only = new Pdfium.Watermark { Text = mark.Text, Layout = mark.Layout, Size = mark.Size, Opacity = mark.Opacity, Bold = mark.Bold, R = mark.R, G = mark.G, B = mark.B, Pages = delegate(int index, int count) { return index == shown; } };
            Preview(delegate { return Pdfium.AddWatermark(job.Bytes, only); }, shown, "La page " + (shown + 1) + ", avec son filigrane.");
        }

        internal async Task<bool> RunWatermark(Pdfium.Watermark mark, string target)
        {
            if (_job == null) return false;
            if (mark == null || string.IsNullOrWhiteSpace(mark.Text)) { Say("Écrivez le texte du filigrane.", false, true); return false; }
            var job = _job;
            return await WriteResult(target, delegate { return Pdfium.AddWatermark(job.Bytes, mark); }, "Filigrane « " + mark.Text.Trim() + " » posé :");
        }

        // ================================================================ signer

        private byte[] _signPixels;
        private int _signWidth, _signHeight;
        private double _signX = -1, _signY = -1; // en points, depuis le coin haut-gauche de la page vue
        private TextBox _signPage, _signSize;
        private CheckBox _signWhite;
        private byte[] _signSource;
        private int _signSourceWidth, _signSourceHeight;
        private Canvas _signCanvas;
        private Image _signPageImage, _signOverlay;
        private double _signScale = 1, _signPageWidth = 595, _signPageHeight = 842;
        private Button _signSave;
        private TextBlock _signHint;

        private void FillSign()
        {
            var columns = new StackPanel { Orientation = Orientation.Horizontal };
            var options = new StackPanel { Width = 360 };
            var pick = Row();
            var fromFile = Buttons.IconText("image-square-bold", "Choisir une image…", "L'image de votre signature : un PNG à fond transparent, ou la photo d'une signature sur papier blanc", Buttons.Bar, Buttons.Look.Outline);
            fromFile.Tag = "atelier-sign-file";
            fromFile.Click += async delegate
            {
                var path = await Ui.PickOpenFile(this, "L'image de la signature", "Images (*.png;*.jpg;*.jpeg;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp");
                if (path != null) await LoadSignature(path);
            };
            pick.Children.Add(fromFile);
            var draw = Buttons.IconText(IconSign, "Tracer à la main…", "Tracer votre signature à la souris ou au stylet", Buttons.Bar, Buttons.Look.Outline);
            draw.Tag = "atelier-sign-draw";
            draw.Margin = new Thickness(8, 0, 0, 0);
            draw.Click += async delegate
            {
                var drawn = await SignaturePadDialog.Ask(Ui.OwnerOf(this));
                if (drawn != null) SetSignature(drawn.Pixels, drawn.Width, drawn.Height, false);
            };
            pick.Children.Add(draw);
            options.Children.Add(pick);
            _signWhite = new CheckBox { Content = "Rendre le papier blanc transparent", IsChecked = true, Margin = new Thickness(0, 10, 0, 0), Tag = "atelier-sign-white" };
            ToolTip.SetTip(_signWhite, "Pour la photo d'une signature sur papier : le blanc disparaît, le trait reste.");
            _signWhite.IsCheckedChanged += delegate { ApplySignatureSource(); };
            options.Children.Add(_signWhite);
            var page = Row();
            page.Children.Add(Label("Sur la page"));
            _signPage = NumberBox(_job.Pages.ToString(CultureInfo.InvariantCulture), "atelier-sign-page");
            page.Children.Add(_signPage);
            page.Children.Add(new TextBlock { Text = "sur " + _job.Pages, Foreground = Chrome.SoftText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            options.Children.Add(page);
            var size = Row();
            size.Children.Add(Label("Largeur :"));
            _signSize = NumberBox("50", "atelier-sign-size");
            size.Children.Add(_signSize);
            size.Children.Add(new TextBlock { Text = "mm", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            options.Children.Add(size);
            _signHint = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Tag = "atelier-sign-hint" };
            options.Children.Add(_signHint);
            _signSave = SaveButton("Enregistrer sous…", "Écrit le PDF signé, dans un nouveau fichier", _job.Stem + " (signé).pdf", async delegate(string target) { await RunSign(target); });
            options.Children.Add(_signSave);
            columns.Children.Add(options);

            _signPageImage = new Image { Stretch = Stretch.Fill };
            _signOverlay = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false, IsVisible = false, Tag = "atelier-sign-overlay" };
            _signCanvas = new Canvas { Background = Chrome.PrintPaper, Cursor = new Cursor(StandardCursorType.Cross), Tag = "atelier-sign-canvas" };
            _signCanvas.Children.Add(_signPageImage);
            _signCanvas.Children.Add(_signOverlay);
            _signCanvas.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (_signPixels == null) return;
                var at = e.GetPosition(_signCanvas);
                PlaceSignature(at.X / _signScale, at.Y / _signScale);
            };
            columns.Children.Add(new Border { BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Child = _signCanvas, Margin = new Thickness(24, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top, Tag = "atelier-sign-page" });
            _toolOptions.Children.Add(columns);

            _signPage.TextChanged += delegate { Touch(); _signX = -1; ShowSignPage(); };
            _signSize.TextChanged += delegate { Touch(); LayoutSignature(); };
            ShowSignPage();
            LayoutSignature();
            _job.Saved = true;
        }

        private int SignPageIndex() { return Math.Max(1, Math.Min(_job.Pages, Whole(_signPage == null ? null : _signPage.Text, _job.Pages))) - 1; }

        private double SignWidthPt() { return Math.Max(5, Math.Min(400, Number(_signSize == null ? null : _signSize.Text, 50))) * PtPerMm; }

        private double SignHeightPt() { return _signWidth <= 0 ? 0 : SignWidthPt() * _signHeight / _signWidth; }

        private async void ShowSignPage()
        {
            if (_job == null || _signCanvas == null) return;
            var job = _job;
            var index = SignPageIndex();
            var generation = ++_previewGeneration;
            double w = 595, h = 842;
            Bitmap bitmap = null;
            try { bitmap = await Task.Run(delegate { return RenderPage(job.Bytes, index, 900, out w, out h); }); }
            catch (Exception error) { if (!Expected(error)) throw; }
            if (generation != _previewGeneration || _signCanvas == null) return;
            _signPageWidth = w;
            _signPageHeight = h;
            _signScale = 440.0 / Math.Max(w, h);
            _signCanvas.Width = w * _signScale;
            _signCanvas.Height = h * _signScale;
            _signPageImage.Width = _signCanvas.Width;
            _signPageImage.Height = _signCanvas.Height;
            _signPageImage.Source = bitmap;
            LastPreviewDrawn = bitmap != null;
            LayoutSignature();
        }

        internal async Task<bool> LoadSignature(string path)
        {
            byte[] pixels;
            int width, height;
            try
            {
                var file = await ReadAll(path);
                if (file.LongLength > 30 * 1024 * 1024 || !PdfImages.Decode(file, out pixels, out width, out height)) { Say("Cette image ne se lit pas (PNG, JPG, BMP ou WebP attendus).", false, true); return false; }
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                Say("L'image n'a pas pu être lue : " + error.Message, false, true);
                return false;
            }
            SetSignature(pixels, width, height, true);
            return true;
        }

        /// <summary>fromFile : une image du disque — le réglage « papier blanc transparent » s'y applique.</summary>
        private void SetSignature(byte[] pixels, int width, int height, bool fromFile)
        {
            _signSource = pixels;
            _signSourceWidth = width;
            _signSourceHeight = height;
            if (_signWhite != null) _signWhite.IsVisible = fromFile;
            _signFromFile = fromFile;
            ApplySignatureSource();
        }

        private bool _signFromFile;

        private void ApplySignatureSource()
        {
            if (_signSource == null) return;
            var pixels = _signFromFile && _signWhite != null && _signWhite.IsChecked == true ? PdfImages.WhiteToAlpha(_signSource) : _signSource;
            int width, height;
            _signPixels = PdfImages.Trim(pixels, _signSourceWidth, _signSourceHeight, 4, out width, out height);
            _signWidth = width;
            _signHeight = height;
            Touch();
            Say(null, false, false);
            if (_signOverlay != null)
            {
                // L'aperçu veut de l'alpha prémultiplié.
                var shown = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                var premul = new byte[_signPixels.Length];
                for (var i = 0; i + 3 < premul.Length; i += 4)
                {
                    var a = _signPixels[i + 3];
                    premul[i] = (byte)(_signPixels[i] * a / 255);
                    premul[i + 1] = (byte)(_signPixels[i + 1] * a / 255);
                    premul[i + 2] = (byte)(_signPixels[i + 2] * a / 255);
                    premul[i + 3] = a;
                }
                using (var locked = shown.Lock())
                    for (var y = 0; y < height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(premul, y * width * 4, IntPtr.Add(locked.Address, y * locked.RowBytes), width * 4);
                _signOverlay.Source = shown;
            }
            LayoutSignature();
        }

        /// <summary>Place la signature : son CENTRE à ce point de la page (en points, depuis le haut-gauche).</summary>
        internal void PlaceSignature(double centerX, double centerY)
        {
            _signX = centerX - SignWidthPt() / 2;
            _signY = centerY - SignHeightPt() / 2;
            Touch();
            LayoutSignature();
        }

        private void LayoutSignature()
        {
            if (_signCanvas == null) return;
            var has = _signPixels != null;
            if (_signSave != null) _signSave.IsEnabled = has;
            if (_signHint != null)
                _signHint.Text = has ? "Cliquez dans la page pour placer la signature. Elle est posée telle que vous la voyez." : "Choisissez l'image de votre signature, ou tracez-la.";
            if (_signOverlay == null) return;
            _signOverlay.IsVisible = has;
            if (!has) return;
            double w = SignWidthPt(), h = SignHeightPt();
            if (_signX < 0 || _signY < 0)
            {
                // D'office : en bas à droite, à 20 mm des bords.
                _signX = _signPageWidth - w - 20 * PtPerMm;
                _signY = _signPageHeight - h - 25 * PtPerMm;
            }
            _signX = Math.Max(0, Math.Min(_signPageWidth - w, _signX));
            _signY = Math.Max(0, Math.Min(_signPageHeight - h, _signY));
            _signOverlay.Width = w * _signScale;
            _signOverlay.Height = h * _signScale;
            Canvas.SetLeft(_signOverlay, _signX * _signScale);
            Canvas.SetTop(_signOverlay, _signY * _signScale);
        }

        internal async Task<bool> RunSign(string target)
        {
            if (_job == null) return false;
            if (_signPixels == null) { Say("Choisissez d'abord l'image de votre signature, ou tracez-la.", false, true); return false; }
            var job = _job;
            var pixels = _signPixels;
            int width = _signWidth, height = _signHeight, index = SignPageIndex();
            double x = _signX, y = _signY, w = SignWidthPt(), h = SignHeightPt();
            return await WriteResult(target, delegate { return Pdfium.Stamp(job.Bytes, index, pixels, width, height, x, y, w, h); }, "Signature posée sur la page " + (index + 1) + " :");
        }

        internal void SetSignatureForProbe(byte[] bgra, int width, int height) { SetSignature(bgra, width, height, false); }

        // ================================================================ compresser

        private ComboBox _compressLevel;
        private StackPanel _compressResult;
        private byte[] _compressed;
        internal PdfCompactReport LastCompress;
        internal Pdfium.ImageDiet LastDiet;

        private void FillCompress()
        {
            var level = Row();
            level.Children.Add(Label("Niveau :"));
            _compressLevel = Choice("atelier-compress-level", 0, "Sans perte — rien ne change à l'image", "Équilibré — images à 150 ppp", "Fort — images à 96 ppp");
            _compressLevel.MinWidth = 300;
            level.Children.Add(_compressLevel);
            _toolOptions.Children.Add(level);
            var go = Buttons.IconText(IconCompress, "Compresser", "Prépare la version allégée et dit ce qu'on y gagne. Rien n'est encore écrit.", Buttons.Bar, Buttons.Look.Primary);
            go.Tag = "atelier-compress-go";
            go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Margin = new Thickness(0, 16, 0, 0);
            go.Click += async delegate { await RunCompress(Math.Max(0, _compressLevel.SelectedIndex)); };
            _toolOptions.Children.Add(go);
            _compressResult = new StackPanel { Tag = "atelier-compress-result" };
            _toolOptions.Children.Add(_compressResult);
            _compressLevel.SelectionChanged += delegate { _compressed = null; _compressResult.Children.Clear(); Say(null, false, false); };
        }

        /// <summary>level : 0 sans perte, 1 équilibré, 2 fort. Rend ce qu'on y gagne (After = le poids obtenu).</summary>
        internal async Task<PdfCompactReport> RunCompress(int level)
        {
            if (_job == null) return null;
            var job = _job;
            _compressed = null;
            if (_compressResult != null) _compressResult.Children.Clear();
            Say("Compression de " + job.Name + "…", false, false);
            var diet = level == 0 ? null : new Pdfium.ImageDiet { MaxDpi = level == 1 ? 150 : 96 };
            var quality = level == 1 ? 75 : 55;
            if (diet != null) diet.Encode = delegate(byte[] pixels, int width, int height, int stride, int format, int newWidth, int newHeight) { return PdfImages.EncodeJpeg(pixels, width, height, stride, format, newWidth, newHeight, quality); };
            PdfCompactReport report = null;
            byte[] best = null;
            try
            {
                await Task.Run(delegate
                {
                    var bytes = job.Bytes;
                    if (diet != null) bytes = Pdfium.DietImages(bytes, diet);
                    PdfCompactReport compact;
                    var shrunk = PdfCompact.Shrink(bytes, out compact);
                    report = compact;
                    report.Before = job.Bytes.LongLength;
                    // Sans réécriture possible, on garde au moins ce que PDFium a rendu.
                    var candidate = shrunk ?? (diet != null ? bytes : null);
                    if (candidate == null) return;
                    // Le résultat doit se rouvrir, avec le même nombre de pages : sinon il n'existe pas.
                    int error;
                    using (var check = Pdfium.Open(candidate, null, out error))
                        if (check == null || check.PageCount != job.Pages) { report.Problem = "La version allégée ne se relit pas correctement : elle a été écartée."; return; }
                    report.Problem = null;
                    report.After = candidate.LongLength;
                    best = candidate;
                });
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                Say("Le PDF n'a pas pu être compressé : " + error.Message, false, true);
                return null;
            }
            if (_job != job) return report;
            LastCompress = report;
            LastDiet = diet;
            if (best == null) { Say(report == null || report.Problem == null ? "Le PDF n'a pas pu être compressé." : report.Problem, false, true); return report; }
            var lines = new List<string[]>();
            var gain = report.Before - report.After;
            var worth = gain > Math.Max(2048, report.Before / 100);
            if (diet != null)
                lines.Add(new[] { diet.Replaced > 0 ? "0" : "1", "Images", diet.Seen == 0 ? "ce PDF n'en a pas." : diet.Replaced + " réduite" + (diet.Replaced > 1 ? "s" : "") + " sur " + diet.Seen + (diet.Replaced > 0 ? " (" + Megabytes(diet.Before) + " → " + Megabytes(diet.After) + ")" : "") + (diet.Skipped > 0 ? " ; " + diet.Skipped + " laissée" + (diet.Skipped > 1 ? "s" : "") + " telle" + (diet.Skipped > 1 ? "s" : "") + " quelle" + (diet.Skipped > 1 ? "s" : "") + " (déjà légère, transparente, CMJN ou au trait)." : ".") });
            lines.Add(new[] { "0", "Réécriture", (report.Orphans == 0 ? "aucun objet inutile" : report.Orphans + " objet" + (report.Orphans > 1 ? "s" : "") + " que plus rien ne citait") + ", " + (report.Duplicates == 0 ? "aucun doublon" : report.Duplicates + " doublon" + (report.Duplicates > 1 ? "s" : "")) + ", " + (report.Deflated == 0 ? "aucun flux à compresser." : report.Deflated + " flux compressé" + (report.Deflated > 1 ? "s." : ".")) });
            var summary = worth ? Megabytes(report.Before) + " → " + Megabytes(report.After) + " : " + Math.Max(1, Math.Min(99, (int)Math.Round(gain * 100.0 / report.Before))) + " % de moins."
                : "Ce PDF est déjà aussi léger que ce niveau sait le rendre (" + Megabytes(report.Before) + ").";
            Say(null, false, false);
            if (_compressResult != null)
            {
                _compressResult.Children.Add(ReportCard(job.Name, summary, worth, lines, null));
                if (worth)
                {
                    _compressed = best;
                    job.Saved = false;
                    var save = SaveButton("Enregistrer sous…", "Écrit la version allégée, dans un nouveau fichier", job.Stem + " (compressé).pdf", async delegate(string target) { await SaveCompressed(target); });
                    save.Margin = new Thickness(0, 12, 0, 0);
                    _compressResult.Children.Add(save);
                }
            }
            else if (worth) { _compressed = best; job.Saved = false; }
            return report;
        }

        internal async Task<bool> SaveCompressed(string target)
        {
            var bytes = _compressed;
            if (_job == null || bytes == null) return false;
            return await WriteResult(target, delegate { return bytes; }, "Version allégée écrite :");
        }

        // ================================================================ convertir

        private RadioButton _convertJpeg, _convertWord;
        private ComboBox _convertDpi, _convertQuality;

        private void FillConvert()
        {
            _convertJpeg = new RadioButton { GroupName = "convert", Content = "En images JPEG, une par page", IsChecked = true, Margin = new Thickness(0, 12, 0, 0), Tag = "atelier-convert-jpeg" };
            _toolOptions.Children.Add(_convertJpeg);
            var jpeg = Row(6);
            jpeg.Margin = new Thickness(26, 6, 0, 0);
            jpeg.Children.Add(Label("Résolution :"));
            _convertDpi = Choice("atelier-convert-dpi", 1, "72 ppp — l'écran", "150 ppp — un bon aperçu", "300 ppp — l'impression");
            jpeg.Children.Add(_convertDpi);
            jpeg.Children.Add(new TextBlock { Text = "Qualité :", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
            _convertQuality = Choice("atelier-convert-quality", 1, "Légère", "Bonne", "Maximale");
            _convertQuality.MinWidth = 120;
            jpeg.Children.Add(_convertQuality);
            _toolOptions.Children.Add(jpeg);
            _convertWord = new RadioButton { GroupName = "convert", Content = "En document Word (.docx) — le texte", Margin = new Thickness(0, 12, 0, 0), Tag = "atelier-convert-word" };
            _toolOptions.Children.Add(_convertWord);
            var wordNote = new TextBlock { Text = "Demande PdfPig, le composant qui lit le texte d'un PDF : il vous sera proposé s'il n'est pas installé.", Foreground = Chrome.FaintText, FontSize = 11.5, Margin = new Thickness(26, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            _toolOptions.Children.Add(wordNote);
            var go = Buttons.IconText("file-arrow-down-bold", "Convertir…", "Demande où écrire, puis convertit", Buttons.Bar, Buttons.Look.Primary);
            go.Tag = "atelier-tool-save";
            go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Margin = new Thickness(0, 16, 0, 0);
            go.Click += async delegate
            {
                if (_job == null) return;
                if (_convertWord.IsChecked == true)
                {
                    var target = await Ui.PickSaveFile(this, "Enregistrer le document Word", "Word|*.docx", _job.Stem + ".docx");
                    if (target != null) await RunWord(target);
                }
                else
                {
                    var folder = await Ui.PickFolder(this, "Où écrire les images");
                    if (folder != null) await RunJpeg(folder, _convertDpi.SelectedIndex == 0 ? 72 : _convertDpi.SelectedIndex == 2 ? 300 : 150, _convertQuality.SelectedIndex == 0 ? 70 : _convertQuality.SelectedIndex == 2 ? 95 : 85);
                }
            };
            _toolOptions.Children.Add(go);
            Action sync = delegate { Touch(); _convertDpi.IsEnabled = _convertJpeg.IsChecked == true; _convertQuality.IsEnabled = _convertJpeg.IsChecked == true; };
            _convertJpeg.IsCheckedChanged += delegate { sync(); };
            sync();
            _job.Saved = true;
        }

        /// <summary>Une image JPEG par page, dans ce dossier. Rend le nombre d'images écrites (−1 si rien n'a pu se faire).</summary>
        internal async Task<int> RunJpeg(string folder, int dpi, int quality)
        {
            if (_job == null) return -1;
            var job = _job;
            var digits = Math.Max(2, job.Pages.ToString(CultureInfo.InvariantCulture).Length);
            var written = 0;
            string firstName = null;
            try
            {
                await Task.Run(delegate
                {
                    int error;
                    using (var document = Pdfium.Open(job.Bytes, null, out error))
                    {
                        if (document == null) throw new InvalidOperationException(Pdfium.ErrorText(error) + ".");
                        var sizes = document.Sizes();
                        for (var i = 0; i < document.PageCount; i++)
                        {
                            var longest = (int)Math.Round(Math.Max(sizes[i][0], sizes[i][1]) / 72.0 * dpi);
                            longest = Math.Max(16, Math.Min(12000, longest));
                            int width, height;
                            var pixels = document.Render(i, longest, out width, out height);
                            var jpeg = pixels == null ? null : PdfImages.EncodeJpeg(pixels, width, height, width * 4, 3, 0, 0, quality);
                            if (jpeg == null) throw new InvalidOperationException("la page " + (i + 1) + " n'a pas pu être dessinée.");
                            var path = FreeName(folder, job.Stem + "-" + (i + 1).ToString(new string('0', digits), CultureInfo.InvariantCulture), ".jpg");
                            File.WriteAllBytes(path, jpeg);
                            if (firstName == null) firstName = Path.GetFileName(path);
                            written++;
                            var done = written;
                            Ui.Post(Avalonia.Threading.DispatcherPriority.Background, delegate { if (_job == job) Say("Page " + done + " sur " + job.Pages + "…", false, false); });
                        }
                    }
                });
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                Say("La conversion s'est arrêtée" + (written > 0 ? " après " + written + " image" + (written > 1 ? "s" : "") : "") + " : " + error.Message, false, true);
                return written > 0 ? written : -1;
            }
            job.Saved = true;
            // Le dernier « Page n sur N… » posté peut encore arriver : le bilan passe après lui.
            Ui.Post(Avalonia.Threading.DispatcherPriority.Background, delegate { if (_job == job) Say((written == 1 ? "1 image écrite" : written + " images écrites") + " à " + dpi + " ppp, dans " + folder + " (" + firstName + (written > 1 ? ", …" : "") + ").", true, false); });
            LastToolOk = true;
            LastToolStatus = written + " images";
            return written;
        }

        /// <summary>Le texte du PDF, en document Word. Vrai si c'est fait.</summary>
        internal async Task<bool> RunWord(string target)
        {
            if (_job == null) return false;
            var job = _job;
            if (!await InstallPdfPig()) { Say("PdfPig n'est pas installé : la conversion vers Word n'a pas été faite.", false, true); return false; }
            try
            {
                Say("Lecture du texte de " + job.Name + "…", false, false);
                var result = await Task.Run(delegate { return PdfPigBridge.Import(job.Path); });
                if (result == null || result.NoText || result.Document == null || result.Document.Paragraphs.Count == 0)
                {
                    Say(job.Name + " n'a pas de texte à donner : c'est sans doute un PDF scanné, fait d'images. Il ne se convertit pas en Word.", false, true);
                    return false;
                }
                await Task.Run(delegate { Docx.Export(result.Document, StyleSheet.CreateDefault(), target); });
                job.Saved = true;
                Say("Document Word écrit : " + target + " — " + (result.Paragraphs == 1 ? "1 paragraphe" : result.Paragraphs + " paragraphes") + (result.Headings > 0 ? ", dont " + (result.Headings == 1 ? "1 titre" : result.Headings + " titres") : "") + ", tirés de " + PagesLabel(result.Pages) + ".", true, false);
                return true;
            }
            catch (Exception error)
            {
                if (!Expected(error) && !(error is InvalidDataException) && !(error is NotSupportedException)) throw;
                Say("La conversion vers Word n'a pas pu se faire : " + error.Message, false, true);
                return false;
            }
        }

        // ================================================================ verrouiller

        private TextBox _lockFirst, _lockSecond;

        private void FillLock()
        {
            var first = Row();
            first.Children.Add(new TextBlock { Text = "Mot de passe :", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Width = 130 });
            _lockFirst = new TextBox { Width = 260, PasswordChar = '•', Tag = "atelier-lock-first" };
            first.Children.Add(_lockFirst);
            _toolOptions.Children.Add(first);
            var second = Row();
            second.Children.Add(new TextBlock { Text = "Encore une fois :", Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, Width = 130 });
            _lockSecond = new TextBox { Width = 260, PasswordChar = '•', Tag = "atelier-lock-second" };
            second.Children.Add(_lockSecond);
            _toolOptions.Children.Add(second);
            var show = new CheckBox { Content = "Afficher le mot de passe", Margin = new Thickness(130, 8, 0, 0), Tag = "atelier-lock-show" };
            show.IsCheckedChanged += delegate
            {
                var mask = show.IsChecked == true ? '\0' : '•';
                _lockFirst.PasswordChar = mask;
                _lockSecond.PasswordChar = mask;
            };
            _toolOptions.Children.Add(show);
            _lockFirst.TextChanged += delegate { Touch(); };
            _toolOptions.Children.Add(SaveButton("Enregistrer la copie verrouillée…", "Écrit une copie du PDF qui ne s'ouvre qu'avec ce mot de passe", _job.Stem + " (verrouillé).pdf", async delegate(string target)
            {
                if ((_lockFirst.Text ?? "") != (_lockSecond.Text ?? "")) { Say("Les deux mots de passe ne sont pas les mêmes.", false, true); return; }
                await RunLock(_lockFirst.Text ?? "", target);
            }));
            _job.Saved = true;
        }

        /// <summary>Écrit la copie verrouillée — après avoir vérifié, avec PDFium, qu'elle refuse de s'ouvrir sans le
        /// mot de passe et s'ouvre avec, sur le même nombre de pages. Vrai si c'est fait.</summary>
        internal async Task<bool> RunLock(string password, string target)
        {
            if (_job == null) return false;
            var weak = PdfLock.PasswordProblem(password);
            if (weak != null) { Say(weak, false, true); return false; }
            var job = _job;
            string problem = null;
            var done = await WriteResult(target, delegate
            {
                string why;
                var locked = PdfLock.Encrypt(job.Bytes, password, out why);
                if (locked == null) { problem = why; return null; }
                int error;
                using (var closed = Pdfium.Open(locked, null, out error))
                    if (closed != null || error != Pdfium.ErrorPassword) { problem = "La copie verrouillée s'ouvre sans mot de passe : elle n'a pas été écrite."; return null; }
                using (var open = Pdfium.Open(locked, password, out error))
                    if (open == null || open.PageCount != job.Pages) { problem = "La copie verrouillée ne se rouvre pas avec son mot de passe : elle n'a pas été écrite."; return null; }
                return locked;
            }, "Copie verrouillée écrite (elle ne s'ouvre qu'avec le mot de passe) :");
            if (!done && problem != null) Say(problem, false, true);
            return done;
        }

        // ================================================================ fusionner

        private readonly List<Pdfium.Document> _mergeDocs = new List<Pdfium.Document>();
        private bool _mergeSaved;
        private StackPanel _mergeList;
        private Button _mergeSave;

        private void BuildMerge()
        {
            _page.Children.Add(Paragraph("Met plusieurs PDF bout à bout, dans l'ordre de la liste : un seul fichier s'écrit, les PDF d'origine ne bougent pas. Pour mêler des pages plutôt que des fichiers entiers, c'est Chop-Chop."));
            var add = Buttons.IconText("plus-bold", "Ajouter des PDF…", "Ajoute un ou plusieurs PDF à la suite de la liste", Buttons.Bar, Buttons.Look.Primary);
            add.Tag = "atelier-choose";
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += async delegate
            {
                var paths = await Ui.PickOpenFiles(this, "Ajouter des PDF", PdfFilter);
                if (paths == null) return;
                foreach (var path in paths) await AddToMerge(path);
            };
            _page.Children.Add(add);
            _mergeList = new StackPanel { Margin = new Thickness(0, 12, 0, 0), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Tag = "atelier-merge-list" };
            _page.Children.Add(_mergeList);
            _mergeSave = Buttons.IconText("file-arrow-down-bold", "Enregistrer sous…", "Écrit le PDF fusionné, dans un nouveau fichier", Buttons.Bar, Buttons.Look.Primary);
            _mergeSave.Tag = "atelier-tool-save";
            _mergeSave.HorizontalAlignment = HorizontalAlignment.Left;
            _mergeSave.Margin = new Thickness(0, 14, 0, 0);
            _mergeSave.Click += async delegate
            {
                if (_mergeDocs.Count < 2) return;
                var target = await Ui.PickSaveFile(this, "Enregistrer le PDF fusionné", PdfFilter, Path.GetFileNameWithoutExtension(_mergeDocs[0].Path ?? "document") + " (fusionné).pdf");
                if (target != null) await RunMerge(target);
            };
            _page.Children.Add(_mergeSave);
            _toolFile = null;
            _toolOptions = null;
            _toolStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), MaxWidth = 860, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, Tag = "atelier-tool-status" };
            _page.Children.Add(_toolStatus);
            RebuildMerge();
        }

        /// <summary>Ajoute ce PDF à la fin de la liste. Vrai si c'est fait.</summary>
        internal async Task<bool> AddToMerge(string path)
        {
            if (!await EnsurePdfium()) return false;
            try
            {
                var opened = await Task.Run(delegate { int code; var d = Pdfium.Open(path, null, out code); return Tuple.Create(d, code); });
                var document = opened.Item1;
                if (document == null || document.Encrypted || document.PageCount == 0)
                {
                    var locked = (document != null && document.Encrypted) || opened.Item2 == Pdfium.ErrorPassword;
                    var empty = document != null && document.PageCount == 0;
                    if (document != null) document.Dispose();
                    Say(Path.GetFileName(path) + (locked ? " est verrouillé : passez-le d'abord par l'Unlocker." : empty ? " n'a aucune page." : " : " + Pdfium.ErrorText(opened.Item2) + "."), false, true);
                    return false;
                }
                _mergeDocs.Add(document);
                _mergeSaved = false;
                Say(null, false, false);
                RebuildMerge();
                return true;
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                Say("Ce fichier n'a pas pu être ouvert : " + error.Message, false, true);
                return false;
            }
        }

        private void RebuildMerge()
        {
            if (_mergeList == null) return;
            _mergeList.Children.Clear();
            if (_mergeSave != null) _mergeSave.IsEnabled = _mergeDocs.Count > 1;
            if (_mergeDocs.Count == 0)
            {
                _mergeList.Children.Add(new TextBlock { Text = "Ajoutez au moins deux PDF.", Foreground = Chrome.SoftText });
                return;
            }
            var total = 0;
            for (var i = 0; i < _mergeDocs.Count; i++)
            {
                var index = i;
                var document = _mergeDocs[i];
                total += document.PageCount;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("36,*,90,Auto") };
                row.Children.Add(new TextBlock { Text = (i + 1).ToString(CultureInfo.InvariantCulture), FontWeight = FontWeight.SemiBold, Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center });
                var name = new TextBlock { Text = Path.GetFileName(document.Path ?? ""), Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "atelier-merge-name" };
                ToolTip.SetTip(name, document.Path ?? "");
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var pages = new TextBlock { Text = PagesLabel(document.PageCount), Foreground = Chrome.SoftText, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(pages, 2);
                row.Children.Add(pages);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                var up = Buttons.Icon("arrow-left-bold", "Plus tôt dans le fichier fusionné", Buttons.Compact, Buttons.Look.Calm);
                up.IsEnabled = i > 0;
                up.Click += delegate { MoveInMerge(index, -1); };
                var down = Buttons.Icon("arrow-right-bold", "Plus tard dans le fichier fusionné", Buttons.Compact, Buttons.Look.Calm);
                down.IsEnabled = i < _mergeDocs.Count - 1;
                down.Click += delegate { MoveInMerge(index, 1); };
                var remove = Buttons.Icon("x-bold", "Retirer ce PDF de la liste", Buttons.Compact, Buttons.Look.Calm);
                remove.Click += delegate
                {
                    if (index >= _mergeDocs.Count) return;
                    _mergeDocs[index].Dispose();
                    _mergeDocs.RemoveAt(index);
                    _mergeSaved = false;
                    RebuildMerge();
                };
                buttons.Children.Add(up);
                buttons.Children.Add(down);
                buttons.Children.Add(remove);
                Grid.SetColumn(buttons, 3);
                row.Children.Add(buttons);
                _mergeList.Children.Add(new Border { Background = Chrome.CardBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6), Child = row, Tag = "atelier-merge-row" });
            }
            _mergeList.Children.Add(new TextBlock { Text = (_mergeDocs.Count == 1 ? "1 fichier" : _mergeDocs.Count + " fichiers") + ", " + PagesLabel(total) + " en tout." + (_mergeDocs.Count == 1 ? " Ajoutez-en au moins un autre." : ""), Foreground = Chrome.SoftText, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), Tag = "atelier-merge-total" });
        }

        internal void MoveInMerge(int index, int step)
        {
            var to = index + step;
            if (index < 0 || index >= _mergeDocs.Count || to < 0 || to >= _mergeDocs.Count) return;
            var document = _mergeDocs[index];
            _mergeDocs.RemoveAt(index);
            _mergeDocs.Insert(to, document);
            _mergeSaved = false;
            RebuildMerge();
        }

        internal async Task<bool> RunMerge(string target)
        {
            if (_mergeDocs.Count < 2) return false;
            foreach (var document in _mergeDocs)
                if (string.Equals(Path.GetFullPath(document.Path ?? ""), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    Say("Le PDF fusionné ne peut pas remplacer un des fichiers de la liste : choisissez un autre nom.", false, true);
                    return false;
                }
            var documents = new List<Pdfium.Document>(_mergeDocs);
            try
            {
                Say("Écriture de " + Path.GetFileName(target) + "…", false, false);
                var total = 0;
                var bytes = await Task.Run(delegate
                {
                    var plan = new PdfAssembly();
                    foreach (var document in documents) { plan.Insert(document.Path ?? "", document.Sizes(), -1); total += document.PageCount; }
                    return Pdfium.Assemble(plan, documents);
                });
                await Task.Run(delegate { File.WriteAllBytes(target, bytes); });
                _mergeSaved = true;
                Say(documents.Count + " PDF fusionnés (" + PagesLabel(total) + ") : " + target + " (" + Megabytes(bytes.LongLength) + ").", true, false);
                return true;
            }
            catch (Exception error)
            {
                if (!Expected(error)) throw;
                Say("Les PDF n'ont pas pu être fusionnés : " + error.Message, false, true);
                return false;
            }
        }

        internal int MergeCountForProbe { get { return _mergeDocs.Count; } }
        internal string MergeOrderForProbe()
        {
            var names = new List<string>();
            foreach (var document in _mergeDocs) names.Add(Path.GetFileNameWithoutExtension(document.Path ?? ""));
            return string.Join(" ", names.ToArray());
        }
        internal PdfJob JobForProbe { get { return _job; } }
        internal void SetForProbe(string tag, string text)
        {
            foreach (var box in new[] { _splitAt, _blankAfter, _blankCount, _blankWidth, _blankHeight, _rotateList, _markText, _signPage, _signSize, _lockFirst, _lockSecond })
                if (box != null && Equals(box.Tag, tag)) box.Text = text;
        }
        internal void ChooseForProbe(string tag, int index)
        {
            foreach (var box in new[] { _blankWhere, _blankFormat, _rotateTurn, _rotateWhich, _markLayout, _markSize, _markPages, _compressLevel, _convertDpi, _convertQuality })
                if (box != null && Equals(box.Tag, tag)) box.SelectedIndex = index;
        }
        internal Pdfium.Watermark WatermarkForProbe() { return ReadWatermark(); }
        internal void BlankForProbe(out int at, out double widthPt, out double heightPt) { at = BlankPosition(); BlankSize(out widthPt, out heightPt); }
        internal void CustomBlankForProbe() { _blankCustom.IsChecked = true; }
    }

    /// <summary>Un minuteur d'une seule fois, pour qu'un aperçu attende que le réglage se pose.</summary>
    internal sealed class DispatcherTimerLite
    {
        private Avalonia.Threading.DispatcherTimer _timer;

        public static DispatcherTimerLite After(int milliseconds, Action action)
        {
            var lite = new DispatcherTimerLite();
            lite._timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            lite._timer.Tick += delegate { lite._timer.Stop(); action(); };
            lite._timer.Start();
            return lite;
        }

        public void Stop() { _timer.Stop(); }
    }
}

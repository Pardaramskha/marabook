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
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marabook.Print;

namespace Marabook.App
{
    /// <summary>CHOP-CHOP (2.0.0, 21/11/2026, Rémi — l'Atelier, PDFaah) : les
    /// pages d'un PDF, à la manière du compositeur d'Acrobat. Trois vues — la
    /// liste simple, les aperçus, les aperçus en vis-à-vis (la première page à
    /// droite, puis paire à gauche et impaire à droite, comme le livre ouvert).
    /// On choisit des pages (clic, Ctrl+clic, MAJ+clic), on les retire, on les
    /// avance ou on les recule, on les glisse ailleurs, on ajoute les pages
    /// d'un autre PDF ; « Enregistrer sous… » écrit le PDF recomposé. Les
    /// fichiers ouverts ne sont jamais modifiés : c'est une copie qui s'écrit.
    /// Le plan est un PdfAssembly (cœur) ; PDFium dessine et recopie.</summary>
    public class ChopChopView : DockPanel
    {
        public const int ModeList = 0, ModeGrid = 1, ModeSpreads = 2;
        private const string PdfFilter = "PDF|*.pdf";
        private const double PtToMm = 25.4 / 72.0;

        private readonly PdfAssembly _plan = new PdfAssembly();
        private readonly List<Pdfium.Document> _documents = new List<Pdfium.Document>();
        private readonly HashSet<int> _selected = new HashSet<int>();
        private readonly Dictionary<long, Bitmap> _thumbs = new Dictionary<long, Bitmap>();
        private readonly Dictionary<long, List<Image>> _waiting = new Dictionary<long, List<Image>>();
        private readonly List<long> _queue = new List<long>();
        private readonly List<Border> _cells = new List<Border>();
        private bool _rendering, _changed;
        private int _anchor = -1, _mode = ModeGrid, _originalCount, _generation;
        private int _pressed = -1, _dropAt = -1;
        private Point _pressedAt;

        private readonly ScrollViewer _scroll;
        private readonly Panel _host;
        private readonly TextBlock _status, _empty;
        private readonly ToggleButton[] _modes = new ToggleButton[3];
        private readonly Button _add, _remove, _earlier, _later, _save;

        /// <summary>Posé par l'Atelier : PDFium est là, ou l'auteur accepte de l'installer.</summary>
        public Func<Task<bool>> PdfiumNeeded;

        public ChopChopView()
        {
            Tag = "chopchop";
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            var open = Buttons.IconText("folder-open", "Ouvrir un PDF…", "Ouvre un PDF pour en voir et en réorganiser les pages. Il n'est jamais modifié.", Buttons.Bar, Buttons.Look.Primary);
            open.Tag = "chop-open";
            open.Margin = new Thickness(0, 0, 8, 4);
            open.Click += async delegate
            {
                var path = await Ui.PickOpenFile(this, "Ouvrir un PDF", PdfFilter);
                if (path != null) await Open(path);
            };
            bar.Children.Add(open);
            _add = Buttons.IconText("plus-bold", "Ajouter un PDF…", "Ajoute les pages d'un autre PDF : après la dernière page choisie, ou à la fin", Buttons.Bar, Buttons.Look.Outline);
            _add.Tag = "chop-add";
            _add.Margin = new Thickness(0, 0, 14, 4);
            _add.Click += async delegate
            {
                var path = await Ui.PickOpenFile(this, "Ajouter les pages d'un PDF", PdfFilter);
                if (path != null) await Add(path);
            };
            bar.Children.Add(_add);
            _earlier = Buttons.IconText("arrow-left-bold", "Avancer", "Déplace les pages choisies d'un cran vers le début", Buttons.Bar, Buttons.Look.Outline);
            _earlier.Tag = "chop-earlier";
            _earlier.Margin = new Thickness(0, 0, 6, 4);
            _earlier.Click += delegate { Nudge(-1); };
            bar.Children.Add(_earlier);
            _later = Buttons.IconText("arrow-right-bold", "Reculer", "Déplace les pages choisies d'un cran vers la fin", Buttons.Bar, Buttons.Look.Outline);
            _later.Tag = "chop-later";
            _later.Margin = new Thickness(0, 0, 6, 4);
            _later.Click += delegate { Nudge(1); };
            bar.Children.Add(_later);
            _remove = Buttons.IconText("trash", "Retirer", "Retire les pages choisies du PDF à écrire (Suppr). Le fichier ouvert les garde.", Buttons.Bar, Buttons.Look.Outline);
            _remove.Tag = "chop-remove";
            _remove.Margin = new Thickness(0, 0, 14, 4);
            _remove.Click += delegate { RemoveSelected(); };
            bar.Children.Add(_remove);
            var labels = new[] { "Liste", "Aperçu", "Vis-à-vis" };
            var icons = new[] { AtelierView.IconModeList, AtelierView.IconModeGrid, AtelierView.IconModeSpreads };
            var tips = new[] { "Une ligne par page", "Les pages en vignettes", "Les pages deux par deux, comme le livre ouvert : la première à droite, puis paire à gauche et impaire à droite" };
            for (var i = 0; i < 3; i++)
            {
                var mode = i;
                _modes[i] = Buttons.IconTextToggle(icons[i], labels[i], tips[i], Buttons.Bar);
                _modes[i].Tag = "chop-mode:" + i;
                _modes[i].Margin = new Thickness(0, 0, i == 2 ? 14 : 4, 4);
                _modes[i].Click += delegate { ChooseMode(mode); };
                bar.Children.Add(_modes[i]);
            }
            _save = Buttons.IconText("file-arrow-down-bold", "Enregistrer sous…", "Écrit le PDF tel qu'il est composé ici, dans un nouveau fichier", Buttons.Bar, Buttons.Look.Primary);
            _save.Tag = "chop-save";
            _save.Margin = new Thickness(0, 0, 0, 4);
            _save.Click += async delegate
            {
                if (_plan.Pages.Count == 0) return;
                var name = _plan.Sources.Count == 0 ? "document" : Path.GetFileNameWithoutExtension(_plan.Sources[0]);
                var path = await Ui.PickSaveFile(this, "Enregistrer le PDF recomposé", PdfFilter, name + " (recomposé).pdf");
                if (path != null) await Save(path);
            };
            bar.Children.Add(_save);
            SetDock(bar, Dock.Top);
            Children.Add(bar);

            _status = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Tag = "chop-status" };
            SetDock(_status, Dock.Top);
            Children.Add(_status);

            _host = new StackPanel { Tag = "chop-pages" };
            _scroll = new ScrollViewer { Content = _host, [ScrollViewer.HorizontalScrollBarVisibilityProperty] = ScrollBarVisibility.Disabled, [ScrollViewer.VerticalScrollBarVisibilityProperty] = ScrollBarVisibility.Auto };
            _empty = new TextBlock
            {
                Text = "Ouvrez un PDF : ses pages paraissent ici. Choisissez-en (clic, Ctrl+clic, MAJ+clic), retirez-les, déplacez-les — par les boutons ou en les glissant —, ajoutez les pages d'un autre PDF, puis enregistrez le résultat sous un nouveau nom.",
                Foreground = Chrome.SoftText, TextWrapping = TextWrapping.Wrap, MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0), Tag = "chop-empty"
            };
            var body = new Panel();
            body.Children.Add(_scroll);
            body.Children.Add(_empty);
            Children.Add(new Border { Background = Chrome.FieldBg, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 6, 10), Child = body, Tag = "chop-frame" });

            Focusable = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Delete && e.KeyModifiers == Avalonia.Input.KeyModifiers.None && _selected.Count > 0) { RemoveSelected(); e.Handled = true; }
                else if (e.Key == Key.A && Ui.HasCommand(e.KeyModifiers) && _plan.Pages.Count > 0)
                {
                    _selected.Clear();
                    for (var i = 0; i < _plan.Pages.Count; i++) _selected.Add(i);
                    PaintSelection();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape && _selected.Count > 0) { _selected.Clear(); PaintSelection(); e.Handled = true; }
            };
            Rebuild();
        }

        // ================================================================ ouvrir, ajouter, enregistrer

        /// <summary>Un PDF est ouvert ici.</summary>
        public bool HasFile { get { return _documents.Count > 0; } }

        /// <summary>Des pages retirées, déplacées ou ajoutées, et pas encore enregistrées (22/11 : on prévient avant de quitter).</summary>
        public bool HasUnsavedWork { get { return _plan.Pages.Count > 0 && (_changed || !_plan.Untouched(_originalCount)); } }

        /// <summary>Ferme tout : les fichiers ouverts sont rendus (PDFium peut alors être retiré).</summary>
        public void Reset()
        {
            _generation++;
            foreach (var document in _documents) document.Dispose();
            _documents.Clear();
            _plan.Sources.Clear();
            _plan.Pages.Clear();
            _selected.Clear();
            _thumbs.Clear();
            _waiting.Clear();
            _queue.Clear();
            _changed = false;
            _originalCount = 0;
            _anchor = -1;
            Rebuild();
        }

        private async Task<Pdfium.Document> Load(string path)
        {
            if (PdfiumNeeded != null && !await PdfiumNeeded()) return null;
            var owner = Ui.OwnerOf(this);
            string password = null;
            var asked = false;
            while (true)
            {
                Pdfium.Document document;
                int error;
                try
                {
                    var tried = password;
                    var opened = await Task.Run(delegate { int code; var d = Pdfium.Open(path, tried, out code); return Tuple.Create(d, code); });
                    document = opened.Item1;
                    error = opened.Item2;
                }
                catch (Exception failure)
                {
                    if (!(failure is IOException) && !(failure is UnauthorizedAccessException) && !(failure is InvalidOperationException) && !(failure is DllNotFoundException) && !(failure is EntryPointNotFoundException)) throw;
                    await MessageDialog.Show(owner, "Ce fichier n'a pas pu être ouvert : " + failure.Message, "Chop-Chop", MessageButtons.OK, MessageIcon.Warning);
                    return null;
                }
                if (document != null)
                {
                    if (document.PageCount == 0)
                    {
                        document.Dispose();
                        await MessageDialog.Show(owner, Path.GetFileName(path) + " n'a aucune page.", "Chop-Chop", MessageButtons.OK, MessageIcon.Warning);
                        return null;
                    }
                    return document;
                }
                if (error != Pdfium.ErrorPassword)
                {
                    await MessageDialog.Show(owner, Path.GetFileName(path) + " : " + Pdfium.ErrorText(error) + ".", "Chop-Chop", MessageButtons.OK, MessageIcon.Warning);
                    return null;
                }
                password = await InputDialog.Ask(owner, "Chop-Chop", asked ? "Ce n'est pas le bon mot de passe. Mot de passe de ce PDF :" : "Ce PDF demande un mot de passe pour s'ouvrir. Mot de passe :", "");
                asked = true;
                if (string.IsNullOrEmpty(password)) return null;
            }
        }

        /// <summary>Ouvre ce PDF à la place de ce qui était là. Vrai si c'est fait.</summary>
        public async Task<bool> Open(string path)
        {
            var document = await Load(path);
            if (document == null) return false;
            Reset();
            _documents.Add(document);
            _plan.Insert(path, document.Sizes(), -1);
            _originalCount = document.PageCount;
            Rebuild();
            return true;
        }

        /// <summary>Ajoute les pages de ce PDF : après la dernière page choisie, sinon à la fin.</summary>
        public async Task<bool> Add(string path)
        {
            if (_documents.Count == 0) return await Open(path);
            var document = await Load(path);
            if (document == null) return false;
            var at = -1;
            foreach (var index in _selected) if (index + 1 > at) at = index + 1;
            _documents.Add(document);
            var first = _plan.Insert(path, document.Sizes(), at);
            _selected.Clear();
            for (var i = 0; i < document.PageCount; i++) _selected.Add(first + i);
            _anchor = first;
            _changed = true;
            Rebuild();
            return true;
        }

        /// <summary>Écrit le PDF recomposé. Jamais par-dessus un des fichiers ouverts. Vrai si c'est fait.</summary>
        public async Task<bool> Save(string path)
        {
            var owner = Ui.OwnerOf(this);
            if (_plan.Pages.Count == 0) return false;
            foreach (var source in _plan.Sources)
                if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                {
                    await MessageDialog.Show(owner, "Le PDF recomposé ne peut pas remplacer un des fichiers ouverts ici : choisissez un autre nom.", "Chop-Chop", MessageButtons.OK, MessageIcon.Warning);
                    return false;
                }
            try
            {
                SetStatus("Écriture de " + Path.GetFileName(path) + "…");
                var bytes = await Task.Run(delegate { return Pdfium.Assemble(_plan, _documents); });
                await Task.Run(delegate { File.WriteAllBytes(path, bytes); });
                _changed = false;
                UpdateStatus("Écrit : " + path + " (" + (_plan.Pages.Count == 1 ? "1 page" : _plan.Pages.Count + " pages") + ").");
                return true;
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is InvalidOperationException)) throw;
                UpdateStatus(null);
                await MessageDialog.Show(owner, "Le PDF n'a pas pu être écrit : " + error.Message, "Chop-Chop", MessageButtons.OK, MessageIcon.Warning);
                return false;
            }
        }

        // ================================================================ les gestes

        internal void ChooseMode(int mode)
        {
            var before = _mode;
            if (before == mode) { SyncModes(); return; }
            SetMode(mode);
            ScreenSteps.Record(this, delegate { SetMode(before); }, delegate { SetMode(mode); }); // « Précédent » rend la vue d'avant
        }

        private void SetMode(int mode)
        {
            _mode = mode;
            Rebuild();
        }

        private void SyncModes()
        {
            for (var i = 0; i < 3; i++) _modes[i].IsChecked = i == _mode;
        }

        internal void RemoveSelected()
        {
            if (_selected.Count == 0) return;
            if (_selected.Count >= _plan.Pages.Count)
            {
                UpdateStatus("Un PDF garde au moins une page : toutes ne peuvent pas être retirées.");
                return;
            }
            _plan.Remove(_selected);
            _selected.Clear();
            _anchor = -1;
            _changed = true;
            Rebuild();
        }

        internal void Nudge(int step)
        {
            if (_selected.Count == 0) return;
            var before = new List<int>(_selected);
            before.Sort();
            var moved = _plan.Nudge(_selected, step);
            var same = moved.Count == before.Count;
            for (var i = 0; same && i < moved.Count; i++) if (moved[i] != before[i]) same = false;
            if (same) return;
            _selected.Clear();
            foreach (var index in moved) _selected.Add(index);
            _changed = true;
            Rebuild();
        }

        /// <summary>Glisser : les pages choisies se posent AVANT la page qui est à « before ».</summary>
        internal void MoveSelected(int before)
        {
            if (_selected.Count == 0) return;
            var count = _selected.Count;
            var first = _plan.Move(_selected, before);
            if (first < 0) return;
            _selected.Clear();
            for (var i = 0; i < count; i++) _selected.Add(first + i);
            _anchor = first;
            _changed = true;
            Rebuild();
        }

        private void Click(int index, Avalonia.Input.KeyModifiers modifiers)
        {
            if ((modifiers & Avalonia.Input.KeyModifiers.Shift) != 0 && _anchor >= 0 && _anchor < _plan.Pages.Count)
            {
                if (!Ui.HasCommand(modifiers)) _selected.Clear();
                for (var i = Math.Min(_anchor, index); i <= Math.Max(_anchor, index); i++) _selected.Add(i);
            }
            else if (Ui.HasCommand(modifiers))
            {
                if (!_selected.Remove(index)) _selected.Add(index);
                _anchor = index;
            }
            else
            {
                _selected.Clear();
                _selected.Add(index);
                _anchor = index;
            }
            PaintSelection();
        }

        // ================================================================ le dessin

        private static long KeyOf(PdfPageRef page) { return ((long)page.Source << 32) | (uint)page.Page; }

        private static string SizeLabel(PdfPageRef page)
        {
            var french = CultureInfo.GetCultureInfo("fr-FR");
            return (Math.Round(page.WidthPt * PtToMm * 10) / 10).ToString("0.#", french) + " × " + (Math.Round(page.HeightPt * PtToMm * 10) / 10).ToString("0.#", french) + " mm";
        }

        private string SourceLabel(PdfPageRef page)
        {
            return "page " + (page.Page + 1) + " de " + Path.GetFileName(_plan.Sources[page.Source]);
        }

        private void SetStatus(string text) { _status.Text = text ?? ""; }

        private void UpdateStatus(string note)
        {
            var has = _plan.Pages.Count > 0;
            var text = "";
            if (has)
            {
                text = (_plan.Pages.Count == 1 ? "1 page" : _plan.Pages.Count + " pages");
                if (_plan.Sources.Count == 1) text += " — " + Path.GetFileName(_plan.Sources[0]);
                else text += " — tirées de " + _plan.Sources.Count + " fichiers";
                if (_selected.Count > 0) text += " · " + (_selected.Count == 1 ? "1 choisie" : _selected.Count + " choisies");
                text += _changed || !_plan.Untouched(_originalCount) ? " · modifié, pas encore enregistré" : " · tel que le fichier";
            }
            if (note != null) text = text.Length == 0 ? note : note + "\n" + text;
            SetStatus(text);
            _add.IsEnabled = true;
            _save.IsEnabled = has;
            _remove.IsEnabled = _selected.Count > 0;
            _earlier.IsEnabled = _selected.Count > 0;
            _later.IsEnabled = _selected.Count > 0;
        }

        private void PaintSelection()
        {
            foreach (var cell in _cells)
            {
                var index = (int)cell.Tag;
                var chosen = _selected.Contains(index);
                cell.BorderBrush = index == _dropAt ? Chrome.Warn : chosen ? (IBrush)Chrome.Accent : Brushes.Transparent;
                cell.Background = chosen ? (IBrush)Chrome.AccentTint : Brushes.Transparent;
            }
            UpdateStatus(null);
        }

        private void Rebuild()
        {
            SyncModes();
            _cells.Clear();
            _host.Children.Clear();
            _waiting.Clear();
            var has = _plan.Pages.Count > 0;
            _empty.IsVisible = !has;
            _scroll.IsVisible = has;
            // Une sélection qui pointe hors de la liste (après un retrait) tombe.
            _selected.RemoveWhere(delegate(int index) { return index < 0 || index >= _plan.Pages.Count; });
            if (has)
            {
                if (_mode == ModeList) BuildList();
                else if (_mode == ModeSpreads) BuildSpreads();
                else BuildGrid();
            }
            PaintSelection();
            Pump();
        }

        private Border Cell(int index, Control content)
        {
            var cell = new Border
            {
                Tag = index, Child = content, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent,
                Padding = new Thickness(6), Cursor = new Cursor(StandardCursorType.Hand)
            };
            cell.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                Focus();
                _pressed = index;
                _pressedAt = e.GetPosition(this);
                // Une page déjà choisie, sans touche : on attend de savoir si c'est un glisser.
                if (!(_selected.Contains(index) && e.KeyModifiers == Avalonia.Input.KeyModifiers.None)) Click(index, e.KeyModifiers);
                e.Handled = true;
            };
            cell.PointerMoved += delegate(object sender, PointerEventArgs e)
            {
                if (_pressed < 0 || !e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                var at = e.GetPosition(this);
                if (Math.Abs(at.X - _pressedAt.X) < 6 && Math.Abs(at.Y - _pressedAt.Y) < 6) return;
                var over = CellAt(e.GetPosition(_host));
                if (over == _dropAt) return;
                _dropAt = over;
                PaintSelection();
            };
            cell.PointerReleased += delegate(object sender, PointerReleasedEventArgs e)
            {
                var pressed = _pressed;
                var drop = _dropAt;
                _pressed = -1;
                _dropAt = -1;
                if (pressed < 0) return;
                if (drop >= 0 && !_selected.Contains(drop)) { MoveSelected(drop); return; }
                // Un simple clic sur une page déjà choisie : elle reste seule.
                if (drop < 0 && _selected.Contains(pressed) && e.KeyModifiers == Avalonia.Input.KeyModifiers.None && _selected.Count > 1) Click(pressed, Avalonia.Input.KeyModifiers.None);
                else PaintSelection();
            };
            _cells.Add(cell);
            return cell;
        }

        /// <summary>La page sous ce point (dans la zone des pages), ou −1.</summary>
        private int CellAt(Point point)
        {
            foreach (var cell in _cells)
            {
                var origin = cell.TranslatePoint(new Point(0, 0), _host);
                if (!origin.HasValue) continue;
                if (new Rect(origin.Value, cell.Bounds.Size).Contains(point)) return (int)cell.Tag;
            }
            return -1;
        }

        private void BuildList()
        {
            for (var i = 0; i < _plan.Pages.Count; i++)
            {
                var page = _plan.Pages[i];
                var row = new DockPanel();
                var number = new TextBlock { Text = (i + 1).ToString(), Width = 46, Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                SetDock(number, Dock.Left);
                row.Children.Add(number);
                var size = new TextBlock { Text = SizeLabel(page) + " · " + PdfAssembly.Side(i), Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
                SetDock(size, Dock.Right);
                row.Children.Add(size);
                row.Children.Add(new TextBlock { Text = SourceLabel(page), Foreground = Chrome.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var cell = Cell(i, row);
                cell.Padding = new Thickness(8, 4, 8, 4);
                cell.BorderThickness = new Thickness(1);
                cell.MaxWidth = 760;
                cell.HorizontalAlignment = HorizontalAlignment.Left;
                cell.MinWidth = 520;
                _host.Children.Add(cell);
            }
        }

        private Control Thumb(int index, double longest)
        {
            var page = _plan.Pages[index];
            var scale = longest / Math.Max(1, Math.Max(page.WidthPt, page.HeightPt));
            var image = new Image { Width = Math.Max(8, page.WidthPt * scale), Height = Math.Max(8, page.HeightPt * scale), Stretch = Stretch.Fill };
            var key = KeyOf(page);
            Bitmap ready;
            if (_thumbs.TryGetValue(key, out ready)) image.Source = ready;
            else
            {
                List<Image> list;
                if (!_waiting.TryGetValue(key, out list)) { list = new List<Image>(); _waiting[key] = list; _queue.Remove(key); _queue.Add(key); }
                list.Add(image);
            }
            // Le papier de la page : blanc quel que soit le thème, c'est un document imprimé.
            var sheet = new Border { Background = Chrome.PrintPaper, BorderBrush = Chrome.BorderStrong, BorderThickness = new Thickness(1), Child = image, HorizontalAlignment = HorizontalAlignment.Center, Tag = "chop-sheet" };
            var column = new StackPanel();
            column.Children.Add(sheet);
            column.Children.Add(new TextBlock { Text = (index + 1).ToString(), Foreground = Chrome.Ink, FontWeight = FontWeight.SemiBold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
            if (_plan.Sources.Count > 1)
                column.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(_plan.Sources[page.Source]), Foreground = Chrome.FaintText, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = longest, TextTrimming = TextTrimming.CharacterEllipsis });
            ToolTip.SetTip(column, SourceLabel(page) + " — " + SizeLabel(page));
            return column;
        }

        private void BuildGrid()
        {
            var wrap = new WrapPanel();
            for (var i = 0; i < _plan.Pages.Count; i++)
            {
                var cell = Cell(i, Thumb(i, 150));
                cell.Width = 172;
                cell.Margin = new Thickness(0, 0, 4, 6);
                wrap.Children.Add(cell);
            }
            _host.Children.Add(wrap);
        }

        private void BuildSpreads()
        {
            const double longest = 210, side = 170;
            var wrap = new WrapPanel();
            foreach (var spread in PdfAssembly.Spreads(_plan.Pages.Count))
            {
                var pair = new StackPanel { Orientation = Orientation.Horizontal };
                for (var s = 0; s < 2; s++)
                {
                    var index = spread[s];
                    if (index < 0) pair.Children.Add(new Border { Width = side + 16, Tag = "chop-blank" });
                    else
                    {
                        var thumb = Thumb(index, longest);
                        // Les deux pages se touchent au pli, comme dans le livre ouvert.
                        var column = thumb as StackPanel;
                        if (column != null)
                            foreach (var child in column.Children) child.HorizontalAlignment = s == 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                        var cell = Cell(index, thumb);
                        cell.Width = side + 16;
                        cell.Padding = s == 0 ? new Thickness(6, 6, 0, 6) : new Thickness(0, 6, 6, 6);
                        pair.Children.Add(cell);
                    }
                }
                wrap.Children.Add(new Border { Child = pair, Margin = new Thickness(0, 0, 22, 14), BorderBrush = Chrome.Border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6), Tag = "chop-spread" });
            }
            _host.Children.Add(wrap);
        }

        /// <summary>Dessine les vignettes attendues, une à la fois, hors du fil de l'interface.</summary>
        private async void Pump()
        {
            if (_rendering) return;
            _rendering = true;
            try
            {
                while (_queue.Count > 0)
                {
                    var generation = _generation;
                    var key = _queue[0];
                    _queue.RemoveAt(0);
                    if (_thumbs.ContainsKey(key) || !_waiting.ContainsKey(key)) continue;
                    var source = (int)(key >> 32);
                    var page = (int)(key & 0xFFFFFFFF);
                    if (source < 0 || source >= _documents.Count) continue;
                    var document = _documents[source];
                    int width = 0, height = 0;
                    byte[] pixels = null;
                    try { pixels = await Task.Run(delegate { int w, h; var p = document.Render(page, 420, out w, out h); width = w; height = h; return p; }); }
                    catch (Exception) { pixels = null; }
                    if (generation != _generation) continue; // tout a été refermé entre-temps
                    if (pixels == null || width <= 0 || height <= 0) { _waiting.Remove(key); continue; }
                    var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    using (var locked = bitmap.Lock())
                        for (var y = 0; y < height; y++)
                            System.Runtime.InteropServices.Marshal.Copy(pixels, y * width * 4, IntPtr.Add(locked.Address, y * locked.RowBytes), width * 4);
                    _thumbs[key] = bitmap;
                    List<Image> list;
                    if (_waiting.TryGetValue(key, out list)) { foreach (var image in list) image.Source = bitmap; _waiting.Remove(key); }
                }
            }
            finally { _rendering = false; }
            if (_queue.Count > 0) Pump();
        }

        // — Sonde.
        internal int PageCountForProbe { get { return _plan.Pages.Count; } }
        internal int SourceCountForProbe { get { return _plan.Sources.Count; } }
        internal int ModeForProbe { get { return _mode; } }
        internal int CellCountForProbe { get { return _cells.Count; } }
        internal int ThumbCountForProbe { get { return _thumbs.Count; } }
        internal int PendingForProbe { get { return _queue.Count + (_rendering ? 1 : 0); } }
        internal string StatusForProbe { get { return _status.Text ?? ""; } }
        internal string OrderForProbe()
        {
            var parts = new List<string>();
            foreach (var page in _plan.Pages) parts.Add((char)('A' + page.Source) + (page.Page + 1).ToString());
            return string.Join(" ", parts.ToArray());
        }
        internal void SelectForProbe(params int[] indices)
        {
            _selected.Clear();
            foreach (var index in indices) _selected.Add(index);
            _anchor = indices.Length == 0 ? -1 : indices[0];
            PaintSelection();
        }
        internal int SpreadCountForProbe
        {
            get { var n = 0; foreach (var border in this.GetVisualDescendants()) { var b = border as Border; if (b != null && "chop-spread".Equals(b.Tag)) n++; } return n; }
        }
    }
}

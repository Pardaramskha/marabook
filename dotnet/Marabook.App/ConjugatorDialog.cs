using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Correction;
using Marabook.Correction.Grammalecte;

namespace Marabook.App
{
    /// <summary>LE CONJUGUEUR (20/11) : le tableau d'un verbe — un mode par
    /// bloc, un temps par colonne — dressé par le conjugueur de Grammalecte.
    /// On y saisit un infinitif ou une forme conjuguée (« mangeaient ») ;
    /// quand la forme peut être plusieurs verbes, chacun est proposé. Une
    /// fenêtre à côté du texte : elle ne bloque rien.
    /// 22/11 : le tableau est un PANNEAU (ConjugatorPanel) — la fenêtre le
    /// porte, et WGYC (l'Atelier) le montre dans sa page.</summary>
    public class ConjugatorPanel : DockPanel
    {
        private readonly GrammalecteBridge _bridge;
        private readonly TextBox _input;
        private readonly CheckBox _pronominal, _negative, _compound, _interrogative, _feminine;
        private readonly TextBlock _head, _info;
        private readonly StackPanel _lemmas, _moods;
        private readonly Button _copy;
        private Conjugation _shown = new Conjugation();
        private CancellationTokenSource _pending;
        private bool _building;

        /// <summary>close : ce que font « Fermer » et Échap — null dans une page (pas de bouton).</summary>
        public ConjugatorPanel(GrammalecteBridge bridge, Action close)
        {
            _bridge = bridge;
            var root = this;

            var top = new StackPanel();
            var ask = new DockPanel();
            var go = Buttons.Text("Conjuguer", null, Buttons.Bar, Buttons.Look.Primary);
            go.Tag = "conjugator-go";
            go.Margin = new Thickness(8, 0, 0, 0);
            go.Click += delegate { Ask(); };
            DockPanel.SetDock(go, Dock.Right);
            ask.Children.Add(go);
            _input = new TextBox { Watermark = "Un verbe, ou une forme conjuguée : aimer, s'asseoir, mangeaient…", Tag = "conjugator-input" };
            _input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Return || e.Key == Key.Enter) { Ask(); e.Handled = true; }
                else if (e.Key == Key.Escape && close != null) { close(); e.Handled = true; }
            };
            ask.Children.Add(_input);
            top.Children.Add(ask);

            var options = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            _pronominal = Option(options, "Pronominal", "conjugator-pronominal", "« se lever » plutôt que « lever »");
            _negative = Option(options, "Négation", "conjugator-negative", "« je ne mange pas »");
            _compound = Option(options, "Temps composés", "conjugator-compound", "Passé composé, plus-que-parfait, passé antérieur, futur antérieur…");
            _interrogative = Option(options, "Interrogation", "conjugator-interrogative", "« mangé-je ? », « manges-tu ? »");
            _feminine = Option(options, "Féminin", "conjugator-feminine", "« elle », « elles », et les accords du participe");
            top.Children.Add(options);

            _head = new TextBlock { Foreground = Chrome.Ink, FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 16, 0, 0), Tag = "conjugator-verb" };
            _info = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12.5, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Tag = "conjugator-info" };
            _lemmas = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), IsVisible = false, Tag = "conjugator-lemmas" };
            top.Children.Add(_head);
            top.Children.Add(_info);
            top.Children.Add(_lemmas);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            var foot = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            if (close != null)
            {
                var closeButton = Buttons.Text("Fermer", null, Buttons.Bar, Buttons.Look.Outline);
                closeButton.MinWidth = 80;
                closeButton.Click += delegate { close(); };
                DockPanel.SetDock(closeButton, Dock.Right);
                foot.Children.Add(closeButton);
            }
            _copy = Buttons.Text("Copier le tableau", "Le tableau entier, en texte, dans le presse-papiers", Buttons.Bar, Buttons.Look.Outline);
            _copy.Tag = "conjugator-copy";
            _copy.HorizontalAlignment = HorizontalAlignment.Left;
            _copy.IsEnabled = false;
            _copy.Click += delegate { if (_shown.Found) Ui.SetClipboardText(this, _shown.ToPlainText()); };
            foot.Children.Add(_copy);
            DockPanel.SetDock(foot, Dock.Bottom);
            root.Children.Add(foot);

            _moods = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Tag = "conjugator-moods" };
            root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _moods });

            Say("Saisissez un verbe.", null);
        }

        /// <summary>Le champ prend le clavier.</summary>
        public void FocusInput() { _input.Focus(); _input.SelectAll(); }

        /// <summary>Le panneau s'en va : la demande en cours tombe.</summary>
        public void Cancel() { if (_pending != null) _pending.Cancel(); }

        /// <summary>Montre le tableau de ce mot.</summary>
        public void Conjugate(string word)
        {
            word = (word ?? "").Trim();
            if (word.Length == 0) return;
            _input.Text = word;
            Ask(word);
        }

        private CheckBox Option(Panel panel, string label, string tag, string tip)
        {
            var box = new CheckBox { Content = label, Tag = tag, Margin = new Thickness(0, 0, 18, 0) };
            ToolTip.SetTip(box, tip);
            box.IsCheckedChanged += delegate { if (!_building && _shown.Found) Ask(_shown.Verb); };
            panel.Children.Add(box);
            return box;
        }

        private ConjugationOptions ReadOptions()
        {
            return new ConjugationOptions
            {
                Pronominal = _pronominal.IsChecked == true, Negative = _negative.IsChecked == true, Compound = _compound.IsChecked == true,
                Interrogative = _interrogative.IsChecked == true, Feminine = _feminine.IsChecked == true
            };
        }

        private void Say(string head, string info)
        {
            _head.Text = head ?? "";
            _info.Text = info ?? "";
            _info.IsVisible = !string.IsNullOrEmpty(info);
        }

        private void Ask() { Ask((_input.Text ?? "").Trim()); }

        /// <summary>Demande le tableau de ce mot au pont, et le montre.</summary>
        private async void Ask(string word)
        {
            if (string.IsNullOrEmpty(word)) return;
            if (_pending != null) _pending.Cancel();
            var mine = _pending = new CancellationTokenSource();
            Conjugation answer;
            try
            {
                answer = _bridge == null ? null : await _bridge.ConjugateAsync(word, ReadOptions(), mine.Token);
            }
            catch (Exception error)
            {
                if (mine.IsCancellationRequested) return;
                _shown = new Conjugation();
                _moods.Children.Clear();
                _lemmas.IsVisible = false;
                _copy.IsEnabled = false;
                Say("Le conjugueur ne répond pas.", "Grammalecte, qui le porte, n'a pas pu être joint : " + error.Message);
                return;
            }
            if (mine.IsCancellationRequested || !ReferenceEquals(_pending, mine)) return;
            Show(word, answer ?? new Conjugation());
        }

        private void Show(string asked, Conjugation conjugation)
        {
            _shown = conjugation;
            _moods.Children.Clear();
            _lemmas.Children.Clear();
            _lemmas.IsVisible = false;
            _copy.IsEnabled = conjugation.Found;
            if (!conjugation.Found)
            {
                Say("« " + asked + " » n'est pas un verbe connu.", "Essayez l'infinitif, ou vérifiez l'orthographe. Les verbes inventés d'un univers ne sont pas dans le conjugueur.");
                return;
            }
            Say(conjugation.UsedPronominal ? Pronominal(conjugation.Verb) : conjugation.Verb, conjugation.Info);
            _building = true;
            _pronominal.IsChecked = conjugation.UsedPronominal;
            _pronominal.IsEnabled = conjugation.CanChoosePronominal;
            ToolTip.SetTip(_pronominal, conjugation.CanChoosePronominal ? "« se lever » plutôt que « lever »"
                : conjugation.Pronominal == 1 ? "Ce verbe ne se conjugue qu'à la forme pronominale." : "Ce verbe n'a pas de forme pronominale.");
            ToolTip.SetShowOnDisabled(_pronominal, true);
            _building = false;

            // « suis » : suivre ou être — l'autre verbe est à un clic.
            if (conjugation.Lemmas.Count > 1)
            {
                _lemmas.Children.Add(new TextBlock { Text = "« " + asked + " » peut être :", Foreground = Chrome.SoftText, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                foreach (var lemma in conjugation.Lemmas)
                {
                    var captured = lemma;
                    var pick = Buttons.Text(lemma, null, Buttons.Compact, lemma == conjugation.Verb ? Buttons.Look.Primary : Buttons.Look.Outline);
                    pick.Tag = "conjugator-lemma";
                    pick.Margin = new Thickness(0, 0, 6, 0);
                    pick.Click += delegate { Ask(captured); };
                    _lemmas.Children.Add(pick);
                }
                _lemmas.IsVisible = true;
            }

            foreach (var mood in conjugation.Moods)
            {
                var tenses = new WrapPanel();
                foreach (var tense in mood.Tenses)
                {
                    var column = new StackPanel { Width = 196, Margin = new Thickness(0, 0, 12, 10) };
                    if (tense.Title.Length > 0)
                        column.Children.Add(new TextBlock { Text = tense.Title, Foreground = Chrome.AccentStrong, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
                    foreach (var form in tense.Forms)
                        column.Children.Add(new SelectableTextBlock { Text = form, Foreground = Chrome.Ink, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });
                    tenses.Children.Add(column);
                }
                var section = AstrolabeUi.Section(mood.Title, null, tenses, "conjugator-mood");
                section.Margin = new Thickness(0, 0, 0, 10);
                _moods.Children.Add(section);
            }
        }

        private static string Pronominal(string verb)
        {
            if (verb.Length == 0) return verb;
            return ("aeiouyhéèêâîôûàù".IndexOf(char.ToLowerInvariant(verb[0])) >= 0 ? "s'" : "se ") + verb;
        }

        // ---- sonde
        internal string HeadForProbe { get { return _head.Text; } }
        internal string InfoForProbe { get { return _info.Text; } }
        internal Conjugation ShownForProbe { get { return _shown; } }
        internal int MoodSectionsForProbe { get { return _moods.Children.Count; } }
        internal int LemmaButtonsForProbe { get { var n = 0; foreach (var child in _lemmas.Children) if (child is Button) n++; return _lemmas.IsVisible ? n : 0; } }
        internal void SetOptionForProbe(string tag, bool value)
        {
            foreach (var box in new[] { _pronominal, _negative, _compound, _interrogative, _feminine })
                if (Equals(box.Tag, tag)) box.IsChecked = value;
        }
        internal bool PronominalEnabledForProbe { get { return _pronominal.IsEnabled; } }
    }

    /// <summary>La FENÊTRE du conjugueur : le panneau, à côté du texte.</summary>
    public class ConjugatorDialog : Window
    {
        private static ConjugatorDialog _open;
        private readonly ConjugatorPanel _panel;

        private ConjugatorDialog(GrammalecteBridge bridge)
        {
            Title = "Conjugueur";
            Width = 880;
            Height = 660;
            MinWidth = 560;
            MinHeight = 380;
            ShowInTaskbar = false;
            Background = Chrome.RaisedBg;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _panel = new ConjugatorPanel(bridge, delegate { Close(); }) { Margin = new Thickness(18, 16, 18, 16) };
            Content = _panel;
            Opened += delegate { _panel.FocusInput(); };
            Closed += delegate
            {
                if (ReferenceEquals(_open, this)) _open = null;
                _panel.Cancel();
            };
        }

        /// <summary>Ouvre le conjugueur (ou ramène celui qui est ouvert) sur
        /// ce mot ; sans mot, le champ attend.</summary>
        public static ConjugatorDialog Open(Window owner, GrammalecteBridge bridge, string word)
        {
            var dialog = _open;
            if (dialog == null)
            {
                dialog = _open = new ConjugatorDialog(bridge);
                Dialogs.FitToScreen(dialog, owner);
                if (owner != null) dialog.Show(owner);
                else dialog.Show();
            }
            else dialog.Activate();
            dialog._panel.Conjugate(word);
            return dialog;
        }

        // ---- sonde
        internal string HeadForProbe { get { return _panel.HeadForProbe; } }
        internal string InfoForProbe { get { return _panel.InfoForProbe; } }
        internal Conjugation ShownForProbe { get { return _panel.ShownForProbe; } }
        internal int MoodSectionsForProbe { get { return _panel.MoodSectionsForProbe; } }
        internal int LemmaButtonsForProbe { get { return _panel.LemmaButtonsForProbe; } }
        internal void SetOptionForProbe(string tag, bool value) { _panel.SetOptionForProbe(tag, value); }
        internal bool PronominalEnabledForProbe { get { return _panel.PronominalEnabledForProbe; } }
    }
}

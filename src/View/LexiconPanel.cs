using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Marabook.Model;

namespace Marabook.View
{
    /// <summary>Le panneau LEXIQUE de la colonne de droite (18/09) : la
    /// définition d'un mot du dictionnaire personnel, ouverte par le clic
    /// droit « Afficher la définition » sur le mot dans un écrit. Pas
    /// d'onglet au rail par défaut : l'épingle (dans le panneau, ou depuis
    /// le menu options du Dictionnaire) l'y installe pour de bon ; sans
    /// épingle, la croix le referme et l'onglet s'en va.</summary>
    public class LexiconPanel : DockPanel
    {
        private readonly TextBlock _word, _summary, _scope, _definition, _note, _empty;
        private readonly StackPanel _body;
        private readonly ToggleButton _pin;
        private readonly Button _edit;
        private LexiconEntry _entry;
        private bool _projectScope;

        public event Action<bool> PinToggled;               // épinglé au rail (vrai) ou retiré
        public event Action CloseRequested;
        public event Action<LexiconEntry, bool> EditRequested; // l'entrée, portée projet

        public LexiconEntry Entry { get { return _entry; } }
        public bool ProjectScope { get { return _projectScope; } }

        public LexiconPanel()
        {
            Background = Chrome.BarBgLight;
            LastChildFill = true;

            var head = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
            SetDock(head, Dock.Top);
            var close = Buttons.Text("✕", "Fermer le Lexique", Buttons.Compact, Buttons.Look.Calm);
            close.Click += delegate { var h = CloseRequested; if (h != null) h(); };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            _pin = Buttons.IconToggle("push-pin-bold", "Épingler le Lexique au rail (un onglet permanent)", Buttons.Compact, Buttons.Look.Calm);
            _pin.Margin = new Thickness(0, 0, 4, 0);
            _pin.Click += delegate
            {
                var h = PinToggled;
                if (h != null) h(_pin.IsChecked == true);
                RefreshPinTip();
            };
            DockPanel.SetDock(_pin, Dock.Right);
            head.Children.Add(_pin);
            _edit = Buttons.Icon("pencil-simple-line", "Modifier l'entrée du dictionnaire", Buttons.Compact, Buttons.Look.Calm);
            _edit.Margin = new Thickness(0, 0, 4, 0);
            _edit.Click += delegate
            {
                var h = EditRequested;
                if (h != null && _entry != null) h(_entry, _projectScope);
            };
            DockPanel.SetDock(_edit, Dock.Right);
            head.Children.Add(_edit);
            var glyph = Icons.Make("pile-dictionnaire", 14, Chrome.Accent) as FrameworkElement;
            if (glyph != null)
            {
                glyph.VerticalAlignment = VerticalAlignment.Center;
                glyph.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(glyph, Dock.Left);
                head.Children.Add(glyph);
            }
            head.Children.Add(new TextBlock
            {
                Text = "Lexique",
                Foreground = Chrome.Ink,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            Children.Add(head);

            _body = new StackPanel { Margin = new Thickness(16, 4, 16, 16) };
            _word = new TextBlock { Foreground = Chrome.Ink, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            _summary = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            _scope = new TextBlock { Foreground = Chrome.FaintText, FontSize = 11, Margin = new Thickness(0, 2, 0, 12) };
            _definition = new TextBlock { Foreground = Chrome.Ink, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
            _note = new TextBlock { Foreground = Chrome.SoftText, FontSize = 12, FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
            _body.Children.Add(_word);
            _body.Children.Add(_summary);
            _body.Children.Add(_scope);
            _body.Children.Add(_definition);
            _body.Children.Add(_note);
            _empty = new TextBlock
            {
                Text = "Aucun mot affiché.\n\nDans un écrit, clic droit sur un mot du dictionnaire "
                    + "personnel › « Afficher la définition ».",
                Foreground = Chrome.SoftText,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 4, 16, 16)
            };
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var stack = new StackPanel();
            stack.Children.Add(_empty);
            stack.Children.Add(_body);
            scroller.Content = stack;
            Children.Add(scroller);
            Show(null, true);
        }

        /// <summary>L'état de l'épingle, tenu par la coquille (réglage persisté).</summary>
        public void SetPinned(bool pinned)
        {
            _pin.IsChecked = pinned;
            RefreshPinTip();
        }

        private void RefreshPinTip()
        {
            _pin.ToolTip = _pin.IsChecked == true
                ? "Retirer le Lexique du rail (il se referme avec la croix)"
                : "Épingler le Lexique au rail (un onglet permanent)";
        }

        /// <summary>Affiche une entrée (null = l'état vide).</summary>
        public void Show(LexiconEntry entry, bool projectScope)
        {
            _entry = entry;
            _projectScope = projectScope;
            var has = entry != null;
            _empty.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            _body.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            _edit.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (!has) return;
            _word.Text = entry.Word;
            _summary.Text = entry.Summary();
            _scope.Text = projectScope ? "Dictionnaire du projet" : "Dictionnaire de tous les projets";
            var definition = (entry.Definition ?? "").Trim();
            _definition.Text = definition.Length > 0 ? definition
                : "Aucune définition pour l'instant — le crayon ci-dessus permet de l'écrire.";
            _definition.Foreground = definition.Length > 0 ? Chrome.Ink : Chrome.FaintText;
            var note = (entry.Note ?? "").Trim();
            _note.Text = note;
            _note.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Après une édition de l'entrée affichée.</summary>
        public void Refresh()
        {
            Show(_entry, _projectScope);
        }
    }
}

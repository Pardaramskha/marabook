using System;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;
using Marabook.Print;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>LE LECTEUR D'ÉCRAN SUR LE TEXTE (2.0.0, 22/11/2026, Rémi —
    /// Préférences › Accessibilité, éteint d'office).
    ///
    /// Le texte de Marabook est DESSINÉ par l'application (de vraies pages) :
    /// un lecteur d'écran n'y voyait rien — ni Narrateur, ni NVDA, ni JAWS,
    /// ni VoiceOver. Avalonia ne donne pas aux applications le « motif texte »
    /// qui laisserait un lecteur d'écran parcourir lui-même une surface
    /// dessinée. Marabook fait donc ce que font les éditeurs dans ce cas
    /// (Google Docs en tête) : c'est LUI qui dit le texte, par une « zone
    /// d'annonces » que tout lecteur d'écran prononce —
    ///  - le curseur avance d'un caractère : le caractère ;
    ///  - d'un mot : le mot ;
    ///  - change de ligne ou de paragraphe : la ligne ;
    ///  - une sélection : « sélectionné », et ce qui l'est ;
    ///  - un écrit prend le clavier : son titre, où l'on est, la ligne.
    /// La frappe n'est pas redite : le lecteur d'écran fait déjà l'écho des
    /// touches. La surface se présente aussi comme une zone de texte, avec le
    /// titre de l'écrit pour nom et le paragraphe du curseur pour valeur.
    ///
    /// LIMITES : ce n'est pas la lecture native d'un champ de texte — les
    /// commandes de lecture propres au lecteur d'écran (« lire depuis le
    /// curseur », « mot suivant » en mode revue) n'y valent pas ; ce sont les
    /// touches de Marabook qui déplacent et font parler. La plaque braille
    /// reçoit les annonces, pas un suivi du curseur.</summary>
    internal static class ScreenReader
    {
        private static TextBlock _region;
        private static bool _flip;

        /// <summary>Sonde : la dernière annonce, et leur nombre.</summary>
        internal static string Last = "";
        internal static int Count;

        public static bool On { get { return AppSettings.ScreenReader; } }

        /// <summary>La zone d'annonces : un bloc de texte minuscule et transparent, que l'arbre
        /// d'accessibilité voit comme une « zone vivante ». À poser une fois dans la fenêtre.</summary>
        public static Control Region()
        {
            if (_region != null) return _region;
            _region = new TextBlock
            {
                Width = 1, Height = 1, Opacity = 0.01, IsHitTestVisible = false, Focusable = false,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Foreground = Brushes.Transparent, Tag = "annonces"
            };
            AutomationProperties.SetLiveSetting(_region, AutomationLiveSetting.Assertive);
            AutomationProperties.SetAccessibilityView(_region, AccessibilityView.Content);
            return _region;
        }

        /// <summary>Fait dire ce texte au lecteur d'écran (rien si l'option est éteinte).</summary>
        public static void Announce(string text)
        {
            if (!On || string.IsNullOrWhiteSpace(text)) return;
            var said = Spoken(text);
            Last = said;
            Count++;
            if (_region == null) return;
            // Le même texte deux fois de suite ne changerait rien : une espace insécable de plus fait la différence.
            _flip = !_flip;
            var value = _flip ? said : said + " ";
            _region.Text = value;
            AutomationProperties.SetName(_region, value);
        }

        /// <summary>Le texte tel qu'on le dit : sans caractères de service, borné.</summary>
        public static string Spoken(string text)
        {
            text = Unlinked(text ?? "");
            var sb = new StringBuilder();
            foreach (var c in text)
            {
                if (c == '￼' || c == '​' || c == '­') continue; // objet inséré, espace sans chasse, césure
                sb.Append(c == '\n' || c == '\t' ? ' ' : c);
                if (sb.Length >= 600) { sb.Append('…'); break; }
            }
            return sb.ToString().Trim();
        }

        /// <summary>Les marques de lien s'effacent de ce qui se dit : « [[Keira]] » se dit « Keira »,
        /// « [[fiche:keira|la capitaine]] » se dit « la capitaine ».</summary>
        public static string Unlinked(string text)
        {
            if (text.IndexOf("[[", StringComparison.Ordinal) < 0) return text;
            var sb = new StringBuilder();
            var i = 0;
            while (i < text.Length)
            {
                var open = text.IndexOf("[[", i, StringComparison.Ordinal);
                var close = open < 0 ? -1 : text.IndexOf("]]", open + 2, StringComparison.Ordinal);
                if (open < 0 || close < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, open - i);
                var inner = text.Substring(open + 2, close - open - 2);
                var bar = inner.LastIndexOf('|');
                sb.Append(bar >= 0 ? inner.Substring(bar + 1) : inner);
                i = close + 2;
            }
            return sb.ToString();
        }

        /// <summary>Le NOM d'un caractère seul, quand le dire tel quel ne dirait rien.</summary>
        public static string CharacterName(char c)
        {
            switch (c)
            {
                case ' ': return "espace";
                case ' ': return "espace insécable";
                case ' ': return "espace fine insécable";
                case '\n': return "retour à la ligne";
                case '\t': return "tabulation";
                case '.': return "point";
                case ',': return "virgule";
                case ';': return "point-virgule";
                case ':': return "deux-points";
                case '!': return "point d'exclamation";
                case '?': return "point d'interrogation";
                case '«': return "guillemet ouvrant";
                case '»': return "guillemet fermant";
                case '—': return "tiret cadratin";
                case '–': return "tiret demi-cadratin";
                case '-': return "trait d'union";
                case '\'': case '’': return "apostrophe";
                case '…': return "points de suspension";
                case '(': return "parenthèse ouvrante";
                case ')': return "parenthèse fermante";
                case '￼': return "objet inséré";
                default: return char.IsUpper(c) ? c + " majuscule" : c.ToString();
            }
        }

        /// <summary>Le mot qui contient (ou suit) cette position.</summary>
        public static string WordAt(string text, int offset)
        {
            if (string.IsNullOrEmpty(text)) return "";
            offset = Math.Max(0, Math.Min(text.Length, offset));
            var start = offset;
            while (start < text.Length && !IsWord(text[start])) start++;      // sur un blanc : le mot d'après
            if (start >= text.Length) return "";
            var from = start;
            while (from > 0 && IsWord(text[from - 1])) from--;
            var to = start;
            while (to < text.Length && IsWord(text[to])) to++;
            return text.Substring(from, to - from);
        }

        private static bool IsWord(char c) { return char.IsLetterOrDigit(c) || c == '\'' || c == '’' || c == '-'; }

        /// <summary>CE QU'IL FAUT DIRE quand le curseur est passé de (p0, o0) à (p1, o1) dans un texte —
        /// le cœur de la décision, sans interface : la sonde et les tests le jouent tel quel.
        /// lineStart / lineEnd : les bornes de la ligne où le curseur est arrivé.</summary>
        public static string DescribeMove(string paragraph, int p0, int o0, int p1, int o1, int lineStart, int lineEnd, bool lineChanged)
        {
            paragraph = paragraph ?? "";
            o1 = Math.Max(0, Math.Min(paragraph.Length, o1));
            if (p0 == p1 && !lineChanged)
            {
                var delta = o1 - o0;
                if (delta == 0) return null;
                if (Math.Abs(delta) == 1)
                {
                    if (o1 >= paragraph.Length) return "fin de paragraphe";
                    return CharacterName(paragraph[o1]);
                }
                // Un saut dans la ligne : au mot (Ctrl + flèche), ou au bout (Début, Fin).
                if (o1 >= paragraph.Length) return "fin de paragraphe";
                if (o1 == lineEnd && lineEnd > lineStart) return "fin de ligne";
                var word = WordAt(paragraph, o1);
                return word.Length > 0 ? word : CharacterName(paragraph[o1]);
            }
            lineStart = Math.Max(0, Math.Min(paragraph.Length, lineStart));
            lineEnd = Math.Max(lineStart, Math.Min(paragraph.Length, lineEnd));
            var line = paragraph.Substring(lineStart, lineEnd - lineStart).Trim();
            if (line.Length == 0) return paragraph.Trim().Length == 0 ? "ligne vide" : "ligne vide";
            return line;
        }
    }

    /// <summary>Ce que l'arbre d'accessibilité voit de la surface de texte : une ZONE DE TEXTE qui porte le
    /// titre de l'écrit, et pour valeur le paragraphe où est le curseur. Quand l'option est éteinte, elle se
    /// présente comme avant (une zone qui défile).</summary>
    internal sealed class ComposedTextPeer : ScrollViewerAutomationPeer, IValueProvider
    {
        private readonly ComposedView _view;

        public ComposedTextPeer(ComposedView owner) : base(owner) { _view = owner; }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return ScreenReader.On ? AutomationControlType.Edit : base.GetAutomationControlTypeCore();
        }

        protected override string GetNameCore()
        {
            return ScreenReader.On ? _view.ReaderName : base.GetNameCore();
        }

        protected override string GetHelpTextCore()
        {
            return ScreenReader.On ? "Texte de l'écrit. Les flèches déplacent le curseur ; Marabook annonce le caractère, le mot ou la ligne où il arrive." : base.GetHelpTextCore();
        }

        protected override bool IsContentElementCore() { return true; }
        protected override bool IsControlElementCore() { return true; }
        protected override bool IsKeyboardFocusableCore() { return true; }

        public bool IsReadOnly { get { return _view.ReadOnly; } }

        public string Value { get { return ScreenReader.On ? ScreenReader.Spoken(_view.ReaderParagraph()) : ""; } }

        /// <summary>Un lecteur d'écran n'écrit pas dans le texte par ce chemin : la frappe passe par le clavier.</summary>
        public void SetValue(string value) { }

        internal void Changed(string before, string after)
        {
            if (ScreenReader.On) RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, before, after);
        }
    }
}

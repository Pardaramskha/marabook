using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LES BRIQUES D'INTERFACE DE L'ASTROLABE (16/11/2026, Rémi : « plus
    /// intuitive, moins austère, des séparations de sous-sections plus claires ») :
    /// la SECTION (une carte à titre), l'ACCORDÉON (une carte qui se déplie),
    /// l'encadré d'EXEMPLE (ce que la notation donne, teinté pour ne plus se
    /// fondre dans le texte), le BANDEAU d'avertissement, la carte d'un
    /// COMPORTEMENT du ciel et le CADRAN de l'horloge.</summary>
    internal static class AstrolabeUi
    {
        private static IBrush CardFill { get { return new SolidColorBrush(Chrome.FieldBg.Color, Chrome.Dark ? 0.45 : 0.7); } }

        // ---- LES SECTIONS « OUTIL » (lot 16, 17/11, Rémi) : un calculateur, un testeur, une explication ne
        // sont pas des champs de configuration — une icône avant le titre et une légère teinte bleue le disent.

        /// <summary>Le bleu des outils et des aides.</summary>
        public static Color ToolBlue { get { return Chrome.Dark ? Color.FromRgb(0x6D, 0xB3, 0xF2) : Color.FromRgb(0x2F, 0x7F, 0xD0); } }
        public static IBrush ToolFill { get { return new SolidColorBrush(ToolBlue, Chrome.Dark ? 0.13 : 0.09); } }
        public static IBrush ToolEdge { get { return new SolidColorBrush(ToolBlue, 0.45); } }

        /// <summary>Le titre d'une section, précédé de son icône s'il en a une.</summary>
        public static Control Title(string title, string icon, bool tool)
        {
            var text = new TextBlock { Text = title, Foreground = Chrome.Ink, FontSize = 13.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            if (string.IsNullOrEmpty(icon)) return text;
            var glyph = Icons.Make(icon, 15, tool ? new SolidColorBrush(ToolBlue) : Chrome.SoftText);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            glyph.Margin = new Thickness(0, 0, 7, 0);
            glyph.Tag = "section-icon";
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(glyph);
            row.Children.Add(text);
            return row;
        }

        /// <summary>Donne à une carte la teinte des outils.</summary>
        public static T Tool<T>(T card) where T : Border
        {
            card.Background = ToolFill;
            card.BorderBrush = ToolEdge;
            card.Classes.Add("tool");
            return card;
        }

        /// <summary>UN REPÈRE AU SURVOL (lot 16, Rémi : « créer des repères visuels ») : la ligne ou le bloc
        /// survolé se teinte légèrement d'accent ; s'il n'avait pas de bords arrondis, il en prend.</summary>
        public static T HoverTint<T>(T card) where T : Border
        {
            var rest = card.Background;
            if (card.CornerRadius.TopLeft <= 0) card.CornerRadius = new CornerRadius(8);
            card.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != InputElement.IsPointerOverProperty) return;
                card.Background = card.IsPointerOver ? HoverFill(rest) : rest;
            };
            card.Classes.Add("hover-tint");
            return card;
        }

        /// <summary>Le fond d'un bloc survolé : son fond de repos, mêlé d'un peu d'accent.</summary>
        public static IBrush HoverFill(IBrush rest)
        {
            var accent = Chrome.Accent.Color;
            var solid = rest as ISolidColorBrush;
            if (solid == null || solid.Color.A == 0 || solid.Opacity <= 0) return new SolidColorBrush(accent, Chrome.Dark ? 0.16 : 0.09);
            var from = solid.Color;
            var share = Chrome.Dark ? 0.16 : 0.09;
            return new SolidColorBrush(Color.FromArgb(from.A, (byte)Math.Round(from.R + (accent.R - from.R) * share), (byte)Math.Round(from.G + (accent.G - from.G) * share), (byte)Math.Round(from.B + (accent.B - from.B) * share)), solid.Opacity);
        }

        /// <summary>Les sections « outil » d'une fenêtre (sonde).</summary>
        internal static int CountTools(Control root)
        {
            // Le contenu de l'onglet ouvert est parcouru deux fois (par l'onglet, et par la zone qui le montre).
            var seen = new HashSet<Border>();
            foreach (var control in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root))
            {
                var card = control as Border;
                if (card != null && card.Classes.Contains("tool")) seen.Add(card);
            }
            return seen.Count;
        }

        /// <summary>LES ONGLETS EN COLONNE (lot 16, Rémi : « au lieu d'onglets, une colonne à gauche comme
        /// pour les préférences ») : la même classe « column » du thème que les Préférences.</summary>
        public static TabControl ColumnTabs(double height)
        {
            var tabs = new TabControl
            {
                Margin = new Thickness(0, 12, 0, 0), Height = height, TabStripPlacement = Dock.Left,
                ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel>(() => new StackPanel { Orientation = Orientation.Vertical })
            };
            tabs.Classes.Add("column");
            return tabs;
        }

        /// <summary>Une sous-section : son titre, ce qu'elle règle en une ligne, son contenu — dans une carte.</summary>
        public static Border Section(string title, string subtitle, Control body, string tag = null, string icon = null, bool tool = false)
        {
            var stack = new StackPanel();
            stack.Children.Add(Title(title, icon, tool));
            if (!string.IsNullOrEmpty(subtitle))
                stack.Children.Add(new TextBlock { Text = subtitle, Foreground = Chrome.SoftText, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (body != null)
            {
                body.Margin = new Thickness(0, 9, 0, 0);
                stack.Children.Add(body);
            }
            var card = new Border
            {
                Child = stack, Padding = new Thickness(14, 11, 14, 13), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(10),
                Background = CardFill, BorderBrush = Chrome.Border, BorderThickness = new Thickness(1), Tag = tag
            };
            return tool ? Tool(card) : card;
        }

        /// <summary>L'encadré d'un exemple : teinté d'accent, en demi-gras — il ne se confond plus avec les libellés.</summary>
        public static Border Example(TextBlock text, string tag = null)
        {
            text.Foreground = Chrome.AccentStrong;
            text.FontWeight = FontWeight.SemiBold;
            text.TextWrapping = TextWrapping.Wrap;
            var box = new Border
            {
                Child = text, Padding = new Thickness(10, 5, 10, 6), CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left,
                Background = Chrome.AccentTint, BorderBrush = Chrome.AccentSoft, BorderThickness = new Thickness(1), Tag = tag,
                IsVisible = !string.IsNullOrEmpty(text.Text)
            };
            // Sans texte, pas d'encadré vide.
            text.PropertyChanged += delegate(object sender, AvaloniaPropertyChangedEventArgs e) { if (e.Property == TextBlock.TextProperty) box.IsVisible = !string.IsNullOrEmpty(text.Text); };
            return box;
        }

        /// <summary>Un bandeau qui prévient (ambre) ou qui bloque (rouge) : une icône, un texte.</summary>
        public static Border Notice(string text, bool danger = false, string tag = null)
        {
            var tone = danger ? Chrome.Danger : Chrome.Warn;
            var row = new DockPanel();
            var icon = Icons.Make("warning-fill", 15, tone);
            icon.Margin = new Thickness(0, 1, 9, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = text, Foreground = Chrome.Ink, FontSize = 12.5, TextWrapping = TextWrapping.Wrap });
            return new Border
            {
                Child = row, Background = new SolidColorBrush(tone.Color, 0.12), BorderBrush = new SolidColorBrush(tone.Color, 0.7), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 14, 10), Margin = new Thickness(0, 0, 0, 12), Tag = tag
            };
        }

        /// <summary>La pastille numérotée d'une étape (« 1 », « 2 », « 3 »).</summary>
        public static Border Step(string number, bool done)
        {
            return new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = done ? Chrome.Accent : Brushes.Transparent,
                BorderBrush = done ? Chrome.Accent : Chrome.BorderStrong, BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = number, Foreground = done ? Brushes.White : Chrome.SoftText, FontSize = 12, FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        /// <summary>Les comportements du ciel (Astro.SunBehaviors, MoonBehaviors) :
        /// un par ligne, le titre en gras, une puce d'accent pour ce qui sort de l'ordinaire.</summary>
        public static void FillBehaviors(StackPanel panel, List<SkyBehavior> behaviors)
        {
            panel.Children.Clear();
            foreach (var behavior in behaviors)
            {
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = behavior.Title, Foreground = Chrome.Ink, FontSize = 12.5, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
                text.Children.Add(new TextBlock { Text = behavior.Text, Foreground = Chrome.SoftText, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) });
                var dot = new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 5, 9, 0), VerticalAlignment = VerticalAlignment.Top,
                    Background = behavior.Notable ? Chrome.Accent : Chrome.BorderStrong
                };
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Tag = behavior.Notable ? "sky-behavior-notable" : "sky-behavior" };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);
                row.Children.Add(text);
                panel.Children.Add(row);
            }
        }
    }

    /// <summary>UN ACCORDÉON : une carte aux bords arrondis dont l'en-tête
    /// (un chevron, un titre, un résumé à droite) déplie ou replie le contenu.
    /// Replié par défaut : les outils d'appoint (le Répartiteur, le calculateur
    /// de circonférence) ne chargent plus la fenêtre.</summary>
    internal sealed class Accordion : Border
    {
        private readonly Control _body;
        private readonly Border _caret;
        private readonly TextBlock _summary;
        private bool _open;

        public Accordion(string title, string summary, Control body, bool open = false, string tag = null, string icon = null, bool tool = false)
        {
            _body = body;
            Tag = tag;
            CornerRadius = new CornerRadius(10);
            BorderBrush = Chrome.BorderStrong;
            BorderThickness = new Thickness(1);
            Background = new SolidColorBrush(Chrome.FieldBg.Color, Chrome.Dark ? 0.45 : 0.7);
            Margin = new Thickness(0, 0, 0, 10);
            ClipToBounds = true;

            _caret = new Border { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            _summary = new TextBlock { Text = summary ?? "", Foreground = Chrome.SoftText, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 0, 0) };
            var head = new DockPanel();
            DockPanel.SetDock(_caret, Dock.Left);
            head.Children.Add(_caret);
            var name = AstrolabeUi.Title(title, icon, tool);
            DockPanel.SetDock(name, Dock.Left);
            head.Children.Add(name);
            head.Children.Add(_summary);
            var header = new Border { Child = head, Padding = new Thickness(12, 9, 14, 9), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Tag = "accordion-header" };
            header.PointerPressed += delegate(object sender, PointerPressedEventArgs e)
            {
                if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                IsOpen = !IsOpen;
            };
            header.PointerEntered += delegate { header.Background = Chrome.AccentTint; };
            header.PointerExited += delegate { header.Background = Brushes.Transparent; };
            _body.Margin = new Thickness(14, 2, 14, 13);
            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(_body);
            Child = stack;
            if (tool) AstrolabeUi.Tool(this);
            _open = !open;
            IsOpen = open;
        }

        public string Summary { get { return _summary.Text; } set { _summary.Text = value ?? ""; } }

        public bool IsOpen
        {
            get { return _open; }
            set
            {
                if (_open == value) return;
                _open = value;
                _body.IsVisible = value;
                _caret.Child = Icons.Make(value ? "caret-down-bold" : "caret-right-bold", 12, Chrome.SoftText);
            }
        }
    }

    /// <summary>LE CADRAN DE L'HORLOGE (16/11, Rémi) : la couronne extérieure
    /// porte les heures, la suivante les minutes d'une heure, la plus petite
    /// les secondes d'une minute ; trois aiguilles montrent une heure d'exemple.
    ///
    /// 16/11 soir (Rémi : « quand on sort de la norme 24/60/60, il est difficile
    /// d'envisager une esthétique symétrique ») : le DESSIN se règle — le nombre
    /// de tours du cadran par jour (deux tours de douze heures, comme chez
    /// nous), le pas des chiffres sur les heures et sur les minutes, le style
    /// des chiffres (arabes, romains, points). Au mieux, le pas est un DIVISEUR
    /// du compte : les repères tombent alors à intervalles égaux, et le cadran
    /// est symétrique. Tips() dit ce que les nombres choisis permettent.</summary>
    internal sealed class ClockDial : Control
    {
        private int _hours = 24, _minutes = 60, _seconds = 60, _turns = 1, _hourStep, _minuteStep, _numerals;

        public const int Arabic = 0, Roman = 1, Dots = 2;

        public ClockDial()
        {
            Width = 210;
            Height = 210;
        }

        public void Set(int hours, int minutes, int seconds, int turns = 1, int hourStep = 0, int minuteStep = 0, int numerals = 0)
        {
            _hours = Math.Max(1, hours);
            _minutes = Math.Max(1, minutes);
            _seconds = Math.Max(1, seconds);
            _turns = ValidTurns(_hours, turns);
            _hourStep = Math.Max(0, hourStep);
            _minuteStep = Math.Max(0, minuteStep);
            _numerals = numerals;
            InvalidateVisual();
        }

        internal string MetricsForProbe { get { return _hours + "/" + _minutes + "/" + _seconds; } }
        internal string DesignForProbe { get { return _turns + " tour(s), heures toutes les " + StepFor(_hours / _turns, _hourStep) + ", minutes toutes les " + StepFor(_minutes, _minuteStep) + ", style " + _numerals; } }

        /// <summary>Les diviseurs d'un nombre, du plus petit au plus grand (1 et lui-même compris).</summary>
        public static List<int> Divisors(int count)
        {
            var list = new List<int>();
            for (var d = 1; d <= count; d++) if (count % d == 0) list.Add(d);
            return list;
        }

        /// <summary>Le nombre de tours retenu : celui demandé s'il partage le jour en tours égaux d'au moins deux heures ; sinon un seul.</summary>
        public static int ValidTurns(int hours, int turns)
        {
            return turns > 1 && hours % turns == 0 && hours / turns >= 2 ? turns : 1;
        }

        /// <summary>Le pas des chiffres « au mieux » : le plus petit DIVISEUR du
        /// compte qui laisse au plus douze repères (des intervalles égaux, donc un
        /// cadran symétrique). Pour un nombre premier trop grand, aucun diviseur
        /// ne convient : un pas rond, et un dernier intervalle plus court.</summary>
        public static int AutoStep(int count)
        {
            foreach (var d in Divisors(count)) if (d < count && count / d <= 12) return d;
            if (count <= 12) return 1;
            foreach (var step in new[] { 2, 3, 4, 5, 6, 10, 12, 15, 20, 25, 30, 50, 60, 100, 200, 250, 500 })
                if (count / (double)step <= 12) return step;
            return Math.Max(1, count / 10);
        }

        public static int StepFor(int count, int asked)
        {
            return asked > 0 && asked < count ? asked : AutoStep(count);
        }

        /// <summary>Les pas qu'on peut proposer pour chiffrer une couronne : les diviseurs qui laissent de 2 à 24 repères.</summary>
        public static List<int> StepChoices(int count)
        {
            var list = new List<int>();
            foreach (var d in Divisors(count)) if (count / d >= 2 && count / d <= 24) list.Add(d);
            return list;
        }

        /// <summary>Les nombres de tours qu'on peut proposer : 1, puis les diviseurs du jour jusqu'à 4 tours.</summary>
        public static List<int> TurnChoices(int hours)
        {
            var list = new List<int> { 1 };
            for (var t = 2; t <= 4; t++) if (ValidTurns(hours, t) == t) list.Add(t);
            return list;
        }

        public static string RomanNumeral(int value)
        {
            if (value <= 0 || value >= 4000) return value.ToString(CultureInfo.InvariantCulture);
            var numbers = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            var letters = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var text = "";
            for (var i = 0; i < numbers.Length; i++) while (value >= numbers[i]) { text += letters[i]; value -= numbers[i]; }
            return text;
        }

        private static string Parts(int count, string unit)
        {
            var parts = new List<string>();
            foreach (var d in Divisors(count))
            {
                if (d == 1 || d == count) continue;
                parts.Add(d + " × " + (count / d));
                if (parts.Count >= 5) break;
            }
            return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
        }

        /// <summary>Le voisin le plus riche en diviseurs d'un nombre (à deux près) : ce vers quoi pousser un compte ingrat.</summary>
        private static int RichNeighbour(int count)
        {
            var best = count;
            for (var n = Math.Max(2, count - 2); n <= count + 2; n++)
                if (Divisors(n).Count > Divisors(best).Count) best = n;
            return best;
        }

        /// <summary>LES ASTUCES DU CADRAN (16/11 soir) : ce que les nombres choisis permettent, ligne à ligne.</summary>
        public static List<string> Tips(int hours, int minutes, int seconds, int turns)
        {
            hours = Math.Max(1, hours); minutes = Math.Max(1, minutes); seconds = Math.Max(1, seconds);
            var tips = new List<string>();
            var hourParts = Parts(hours, "heures");
            if (hourParts == null && hours > 3)
            {
                var better = RichNeighbour(hours);
                tips.Add(hours + " heures est un nombre premier : le jour ne se coupe ni en moitiés ni en quarts, le cadran ne peut pas être symétrique."
                    + (better != hours ? " Avec " + better + " heures (" + Parts(better, "heures") + "), il le deviendrait." : ""));
            }
            else if (hourParts != null)
            {
                tips.Add(hours + " heures se partagent en parts égales : " + hourParts + ". Chiffrez à l'un de ces pas pour des repères réguliers."
                    + (hours % 4 == 0 ? " Divisible par 4 : le cadran a ses quatre points cardinaux, comme le nôtre." : hours % 2 == 0 ? " Pair mais pas divisible par 4 : un haut et un bas, pas de quarts." : " Impair : pas de repère en bas du cadran, face au zéro."));
                if (TurnChoices(hours).Count > 1 && ValidTurns(hours, turns) == 1 && hours > 12)
                    tips.Add("Un cadran de " + hours + " heures est chargé : faites-lui faire deux tours par jour (" + (hours / 2) + " heures par tour, comme nos horloges de douze heures)." );
            }
            var minuteParts = Parts(minutes, "minutes");
            if (minuteParts == null && minutes > 3)
                tips.Add(minutes + " minutes par heure est un nombre premier : pas de demi-heure ni de quart d'heure. " + RichNeighbour(minutes) + " en aurait.");
            else if (minuteParts != null)
                tips.Add(minutes + " minutes : " + minuteParts + (minutes % 4 == 0 ? " — le quart d'heure existe (" + (minutes / 4) + " min)." : minutes % 2 == 0 ? " — une demi-heure (" + (minutes / 2) + " min), mais pas de quart d'heure rond." : " — ni demi-heure ni quart d'heure ronds."));
            if (seconds != minutes)
                tips.Add("Minutes et secondes n'ont pas le même compte (" + minutes + " et " + seconds + ") : les deux petites couronnes ne se répondent pas. À l'identique, le cadran gagne en unité.");
            tips.Add("Les nombres riches en diviseurs font les plus beaux cadrans : 12, 24, 36, 48, 60, 72, 120.");
            return tips;
        }

        private static Point At(Point center, double radius, double turn)
        {
            var angle = turn * 2 * Math.PI - Math.PI / 2; // zéro en haut, dans le sens des aiguilles
            return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        }

        private void Ring(DrawingContext context, Point center, double radius, int count, int step, IBrush ink, double tick, double fontSize, bool labels, int numerals, bool topIsCount)
        {
            var pen = new Pen(Chrome.BorderStrong, 1);
            var strong = new Pen(ink, 1.3);
            var cardinal = new Pen(ink, 2.2);
            context.DrawEllipse(null, new Pen(Chrome.Border, 1), center, radius, radius);
            // Trop de graduations pour le périmètre : une sur « thin » seulement.
            var thin = Math.Max(1, (int)Math.Ceiling(count / (radius * 2 * Math.PI / 3.5)));
            for (var i = 0; i < count; i++)
            {
                var major = i % step == 0;
                if (!major && i % thin != 0) continue;
                var turn = i / (double)count;
                // Les quarts du cadran (quand le compte s'y prête) : un trait plus fort — l'ossature de la symétrie.
                var quarter = count % 4 == 0 && i % (count / 4) == 0;
                context.DrawLine(quarter && labels ? cardinal : major ? strong : pen, At(center, radius, turn), At(center, radius - (major ? tick : tick * 0.55), turn));
                if (!major || !labels) continue;
                if (numerals == Dots)
                {
                    context.DrawEllipse(ink, null, At(center, radius - tick - 4.5, turn), quarter ? 2.4 : 1.5, quarter ? 2.4 : 1.5);
                    continue;
                }
                var number = i == 0 && topIsCount ? count : i;
                var label = numerals == Roman ? (number == 0 ? "·" : RomanNumeral(number)) : number.ToString(CultureInfo.InvariantCulture);
                var size = numerals == Roman && label.Length > 3 ? fontSize * 0.82 : fontSize;
                var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, ink);
                var spot = At(center, radius - tick - size * 0.85 - (numerals == Roman ? 2 : 0), turn);
                context.DrawText(text, new Point(spot.X - text.Width / 2, spot.Y - text.Height / 2));
            }
        }

        public override void Render(DrawingContext context)
        {
            var size = Math.Min(Bounds.Width, Bounds.Height);
            if (size < 40) return;
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            var outer = size / 2 - 2;
            var perTurn = _hours / _turns;
            context.DrawEllipse(Chrome.FieldBg, new Pen(Chrome.BorderStrong, 1.5), center, outer, outer);
            Ring(context, center, outer - 3, perTurn, StepFor(perTurn, _hourStep), Chrome.Ink, 8, 10.5, true, _numerals, _turns > 1);
            Ring(context, center, outer * 0.62, _minutes, StepFor(_minutes, _minuteStep), Chrome.Time, 6, 8.5, true, _numerals == Dots ? Dots : Arabic, false);
            Ring(context, center, outer * 0.30, _seconds, StepFor(_seconds, 0), Chrome.Accent, 4, 7, false, Arabic, false);
            // L'heure d'exemple : un peu après la mi-journée, à la demie, aux trois quarts de la minute.
            var hour = Math.Min(_hours - 1, (int)Math.Floor(_hours * 14 / 24.0));
            var minute = _minutes / 2;
            var second = _seconds * 3 / 4;
            var hourTurn = (hour % perTurn + minute / (double)_minutes) / perTurn;
            context.DrawLine(new Pen(Chrome.Ink, 3, lineCap: PenLineCap.Round), center, At(center, outer * 0.80, hourTurn));
            context.DrawLine(new Pen(Chrome.Time, 2, lineCap: PenLineCap.Round), center, At(center, outer * 0.58, minute / (double)_minutes));
            context.DrawLine(new Pen(Chrome.Accent, 1.2, lineCap: PenLineCap.Round), center, At(center, outer * 0.28, second / (double)_seconds));
            context.DrawEllipse(Chrome.Ink, null, center, 3.2, 3.2);
        }

        /// <summary>L'heure d'exemple que montrent les aiguilles : « 14h30m45 ».</summary>
        public static string Sample(int hours, int minutes, int seconds)
        {
            hours = Math.Max(1, hours); minutes = Math.Max(1, minutes); seconds = Math.Max(1, seconds);
            var hour = Math.Min(hours - 1, (int)Math.Floor(hours * 14 / 24.0));
            return hour + "h" + (minutes / 2).ToString("00", CultureInfo.InvariantCulture) + "m" + (seconds * 3 / 4).ToString("00", CultureInfo.InvariantCulture);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UniversSale.Model;

namespace UniversSale.Correction
{
    /// <summary>Les options de la passe typographique (batch 34) — le port
    /// de Typonanny (Unhinged Stargazer Hub), mêmes règles, mêmes noms.
    /// Préréglages : « in » (Imprimerie nationale : fine avant ; ! ?, pleine
    /// avant : et dans « »), « souple » (fine partout), « minimal »
    /// (évidences seules — les règles marquées ° sont ignorées).</summary>
    public class TypographyOptions
    {
        public string Preset = "in";     // "in" | "souple" | "minimal"
        public bool Spaces = true;       // espaces doubles, fins de ligne
        public bool Apostrophes = true;  // ' → ’
        public bool Ellipses = true;     // ... → …, etc… → etc.
        public bool Quotes = true;       // "…" → « … » °
        public bool DialogueDashes = true; // - / -- en tête → — °
        public bool Ranges = true;       // 1914-1918 → 1914–1918 °
        public bool NoBreakPunctuation = true; // ; ! ? : « » °
        public bool NoBreakUnits = true; // 10 %, 10 €, 12 kg °
        public bool Thousands = true;    // 10 000 en fine °
        public bool LigaturesOe = true;  // œ (liste blanche)
        public bool LigaturesAe;         // æ (liste blanche), éteint par défaut
        public bool Dimensions = true;   // 10 x 15 → 10 × 15 °
        public bool Ordinals = true;     // 2ème → 2ᵉ (exposants Unicode, comme Grammalecte)
        public bool FlagCapitals = true; // signaler Etat/A… (jamais corrigé)

        public bool Minimal { get { return Preset == "minimal"; } }

        /// <summary>Avant ; ! ? — fine dans les deux préréglages.</summary>
        public char BeforeHighPunctuation { get { return '\u202F'; } }
        /// <summary>Avant « : » — pleine (in) ou fine (souple).</summary>
        public char BeforeColon { get { return Preset == "souple" ? '\u202F' : '\u00A0'; } }
        /// <summary>Intérieur des guillemets — pleine (in) ou fine (souple).</summary>
        public char InsideQuotes { get { return Preset == "souple" ? '\u202F' : '\u00A0'; } }

        public TypographyOptions Clone() { return (TypographyOptions)MemberwiseClone(); }

        public Dictionary<string, object> ToJson()
        {
            var node = new Dictionary<string, object>();
            node["preset"] = Preset;
            node["spaces"] = Spaces; node["apostrophes"] = Apostrophes; node["ellipses"] = Ellipses;
            node["quotes"] = Quotes; node["dialogueDashes"] = DialogueDashes; node["ranges"] = Ranges;
            node["noBreakPunctuation"] = NoBreakPunctuation; node["noBreakUnits"] = NoBreakUnits;
            node["thousands"] = Thousands; node["ligaturesOe"] = LigaturesOe; node["ligaturesAe"] = LigaturesAe;
            node["dimensions"] = Dimensions; node["ordinals"] = Ordinals; node["flagCapitals"] = FlagCapitals;
            return node;
        }

        public static TypographyOptions FromJson(Dictionary<string, object> node)
        {
            var o = new TypographyOptions();
            if (node == null) return o;
            var preset = Json.AsString(Json.Field(node, "preset"));
            if (preset == "in" || preset == "souple" || preset == "minimal") o.Preset = preset;
            o.Spaces = Json.AsBool(Json.Field(node, "spaces"), o.Spaces);
            o.Apostrophes = Json.AsBool(Json.Field(node, "apostrophes"), o.Apostrophes);
            o.Ellipses = Json.AsBool(Json.Field(node, "ellipses"), o.Ellipses);
            o.Quotes = Json.AsBool(Json.Field(node, "quotes"), o.Quotes);
            o.DialogueDashes = Json.AsBool(Json.Field(node, "dialogueDashes"), o.DialogueDashes);
            o.Ranges = Json.AsBool(Json.Field(node, "ranges"), o.Ranges);
            o.NoBreakPunctuation = Json.AsBool(Json.Field(node, "noBreakPunctuation"), o.NoBreakPunctuation);
            o.NoBreakUnits = Json.AsBool(Json.Field(node, "noBreakUnits"), o.NoBreakUnits);
            o.Thousands = Json.AsBool(Json.Field(node, "thousands"), o.Thousands);
            o.LigaturesOe = Json.AsBool(Json.Field(node, "ligaturesOe"), o.LigaturesOe);
            o.LigaturesAe = Json.AsBool(Json.Field(node, "ligaturesAe"), o.LigaturesAe);
            o.Dimensions = Json.AsBool(Json.Field(node, "dimensions"), o.Dimensions);
            o.Ordinals = Json.AsBool(Json.Field(node, "ordinals"), o.Ordinals);
            o.FlagCapitals = Json.AsBool(Json.Field(node, "flagCapitals"), o.FlagCapitals);
            return o;
        }
    }

    /// <summary>Le résultat d'une passe sur un texte : le texte corrigé, les
    /// compteurs par catégorie (libellés de Typonanny) et les signalements
    /// (jamais corrigés d'office).</summary>
    public class TypographyResult
    {
        public string Text = "";
        public List<KeyValuePair<string, int>> Counters = new List<KeyValuePair<string, int>>();
        public List<string> Warnings = new List<string>();

        public int Total
        {
            get { var n = 0; foreach (var c in Counters) n += c.Value; return n; }
        }

        internal void Count(string label, int n)
        {
            if (n <= 0) return;
            for (var i = 0; i < Counters.Count; i++)
                if (Counters[i].Key == label)
                {
                    Counters[i] = new KeyValuePair<string, int>(label, Counters[i].Value + n);
                    return;
                }
            Counters.Add(new KeyValuePair<string, int>(label, n));
        }

        internal void Merge(TypographyResult other)
        {
            foreach (var c in other.Counters) Count(c.Key, c.Value);
            foreach (var w in other.Warnings) if (!Warnings.Contains(w) && Warnings.Count < 40) Warnings.Add(w);
        }
    }

    /// <summary>Le moteur typographique : Typonanny porté (mêmes regex, même
    /// ordre, mêmes zones protégées, même idempotence par « déjà bon »).
    /// Texte pur, sans état — appelable par paragraphe.</summary>
    public static class Typography
    {
        public static readonly string[] DefaultOe =
        {
            "œuf", "œufs", "bœuf", "bœufs", "cœur", "cœurs", "chœur", "chœurs", "sœur", "sœurs",
            "œuvre", "œuvres", "œuvrer", "manœuvre", "manœuvres", "manœuvrer", "œil", "œillet",
            "œillets", "œillade", "œillades", "œsophage", "œstrogène", "œstrogènes", "fœtus",
            "nœud", "nœuds", "vœu", "vœux", "mœurs", "œcuménique", "œcuméniques", "œnologie",
            "œnologue", "œdème", "œdèmes", "cœliaque", "Œdipe", "cœlacanthe"
        };

        public static readonly string[] DefaultAe =
        { "ex æquo", "curriculum vitæ", "tænia", "nævus", "cæcum", "et cætera" };

        // Zones protégées : masquées avant toute règle, restaurées après.
        private static readonly Regex[] Protected =
        {
            new Regex(@"```[\s\S]*?```"),
            new Regex(@"`[^`\n]+`"),
            new Regex(@"(https?|ftp)://\S+", RegexOptions.IgnoreCase),
            new Regex(@"\bwww\.\S+", RegexOptions.IgnoreCase),
            new Regex(@"[\w.+-]+@[\w-]+\.[\w.-]+"),
            new Regex(@"\b[A-Za-z]:\\\S+"),
            new Regex(@"\\\\\S+"),
            new Regex(@"\b\d{1,2}:\d{2}(?::\d{2})?\b"),
            new Regex(@"\b\d+:\d+\b")
        };

        public static TypographyResult Clean(string text, TypographyOptions o)
        {
            return Clean(text, o, null);
        }

        /// <summary>La passe. extraProtected : plages (début, longueur) à ne
        /// jamais toucher — les passages « ne pas corriger » du pivot.</summary>
        public static TypographyResult Clean(string text, TypographyOptions o, List<int[]> extraProtected)
        {
            return Clean(text, o, extraProtected, 0);
        }

        /// <summary>Le nombre de « » restés ouverts à la fin d'un texte, en
        /// partant de `before` — pour enchaîner les paragraphes : une réplique
        /// ouverte au paragraphe précédent l'est encore ici.</summary>
        public static int QuoteDepth(string text, int before)
        {
            var depth = before;
            if (text == null) return depth;
            var first = FirstQuoteIsContinuation(text, before);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '«') { if (i != first) depth++; }
                else if (c == '»' && depth > 0) depth--;
            }
            return depth;
        }

        /// <summary>La convention des « guillemets de suite » : une citation
        /// ouverte plus haut se poursuit par un « en tête de paragraphe, qui
        /// n'ouvre rien de nouveau. Rend l'index de ce « ou -1.</summary>
        private static int FirstQuoteIsContinuation(string text, int openBefore)
        {
            if (openBefore <= 0) return -1;
            var i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            return i < text.Length && text[i] == '«' ? i : -1;
        }

        /// <summary>La passe, en sachant combien de « » sont déjà ouverts avant
        /// ce texte (openQuotesBefore) : les guillemets droits imbriqués dans
        /// une citation ouverte deviennent des guillemets courbes “ ”, pas
        /// des « » — c'est la règle : « Elle a dit “non” ».</summary>
        public static TypographyResult Clean(string text, TypographyOptions o, List<int[]> extraProtected,
            int openQuotesBefore)
        {
            var r = new TypographyResult();
            if (text == null) { r.Text = ""; return r; }
            if (o == null) o = new TypographyOptions();

            var masks = new List<string>();
            var work = Mask(text, extraProtected, masks);
            int n;

            // (2) espaces
            if (o.Spaces)
            {
                work = Replace(work, @"(?m)[ \t]+(?=\r?$)", "", out n, null); r.Count("espaces en fin de ligne", n);
                work = Replace(work, @"(?<=[^ \n])  +(?=[^ \n])", " ", out n, null); r.Count("espaces doubles", n);
            }
            // (3a) apostrophes
            if (o.Apostrophes)
            {
                var count = 0;
                var sb = new StringBuilder(work.Length);
                foreach (var c in work) { if (c == '\'') { count++; sb.Append('’'); } else sb.Append(c); }
                work = sb.ToString();
                r.Count("apostrophes courbes", count);
            }
            // (3b) points de suspension
            if (o.Ellipses)
            {
                work = Replace(work, @"\betc(\.\.\.+|…)", "etc.", out n, null); r.Count("« etc. »", n);
                var many = Regex.Match(work, @"\.{4,}");
                if (many.Success) r.Warnings.Add("« " + many.Value + " » laissé tel quel (plus de trois points, souvent voulu)");
                work = Replace(work, @"(?<!\.)\.\.\.(?!\.)", "…", out n, null); r.Count("points de suspension", n);
                work = Replace(work, @"(?<!\.)\. \. \.(?!\.)", "…", out n, null); r.Count("points de suspension", n);
            }
            // (3c) guillemets français
            if (o.Quotes && !o.Minimal)
            {
                // Une paire de guillemets droits devient « … » — sauf à
                // l'intérieur d'une citation « déjà ouverte » (dans ce
                // paragraphe ou avant lui) : là, des courbes “ … ”, qui
                // signalent une insistance au milieu d'une réplique. Un
                // guillemet orphelin (nombre impair) reste droit, signalé,
                // MAIS les paires qui le précèdent sont converties : avant le
                // 13/09, un seul orphelin gelait le paragraphe entier — à la
                // frappe, plus aucun guillemet ne se convertissait jamais.
                int count;
                work = ConvertQuotes(work, o.InsideQuotes.ToString(), openQuotesBefore, out count);
                r.Count("guillemets français", count);
                // Signalé seulement s'il en RESTE un : l'ouvrant ou le fermant
                // d'un dialogue sur plusieurs lignes vient d'être converti.
                if (work.IndexOf('"') >= 0)
                    r.Warnings.Add("un guillemet droit orphelin est laissé tel quel (ni paire, ni ouverture ou fermeture de dialogue reconnue)");
            }
            // (4a) tirets de dialogue
            if (o.DialogueDashes && !o.Minimal)
            {
                // Le tiret de dialogue est suivi d'une INSÉCABLE (13/09) : un
                // espace ordinaire après le cadratin, c'est « il manque un
                // espace insécable » chez Grammalecte à chaque réplique — et
                // la passe ne le corrigeait jamais.
                work = Replace(work, @"(?m)^([ \t]*)-{1,2}[ \t]+", "$1— ", out n, null); r.Count("tirets de dialogue", n);
                work = Replace(work, @"(?m)^([ \t]*—)[ \t ]+", "$1 ", out n, null); r.Count("insécable après le tiret", n);
            }
            // (4b) intervalles
            if (o.Ranges && !o.Minimal)
            {
                work = Replace(work, @"(?<![\d\-–])(\d{1,4})-(\d{1,4})(?![\d\-–])", "$1–$2", out n, null);
                r.Count("intervalles (demi-cadratin)", n);
            }
            // (5a) insécables de ponctuation
            if (o.NoBreakPunctuation && !o.Minimal)
            {
                var fine = o.BeforeHighPunctuation.ToString();
                var colon = o.BeforeColon.ToString();
                var inside = o.InsideQuotes.ToString();
                work = Replace(work, "(?<=[^\\s;!?:«\u00A0\u202F])[ \u00A0\u202F]*(?=[;!?])", fine, out n, fine);
                r.Count("insécables avant ; ! ?", n);
                work = Replace(work, "(?<=[^\\s:«\u00A0\u202F])[ \u00A0\u202F]*(?=:(?=\\s|$))", colon, out n, colon);
                r.Count("insécables avant :", n);
                work = Replace(work, "(?<=«)[ \u00A0\u202F]*(?=\\S)", inside, out n, inside);
                r.Count("insécables dans « »", n);
                work = Replace(work, "(?<=\\S)[ \u00A0\u202F]*(?=»)", inside, out n, inside);
                r.Count("insécables dans « »", n);
            }
            // (5b) insécables d'unités
            if (o.NoBreakUnits && !o.Minimal)
            {
                work = Replace(work, "(?<=\\d)[ \u00A0\u202F]*(?=[%€$])", "\u00A0", out n, "\u00A0");
                r.Count("insécables d'unités", n);
                work = Replace(work,
                    "(?<=\\d)[ \u00A0\u202F]+(?=(?:kg|km|cm|mm|mn|min|ans|an|h|g|m|s|l|cl|ml|ko|mo|go|Ko|Mo|Go)\\b)",
                    "\u00A0", out n, "\u00A0");
                r.Count("insécables d'unités", n);
            }
            // (5c) milliers
            if (o.Thousands && !o.Minimal)
            {
                work = Replace(work, "(?<=\\b\\d{1,3}) (?=\\d{3}(?!\\d))", "\u202F", out n, null);
                r.Count("milliers en fine", n);
            }
            // (6a/6b) ligatures
            if (o.LigaturesOe) { work = Ligate(work, DefaultOe, "oe", "œ", out n); r.Count("ligatures œ", n); }
            if (o.LigaturesAe) { work = Ligate(work, DefaultAe, "ae", "æ", out n); r.Count("ligatures æ", n); }
            // (6c) dimensions
            if (o.Dimensions && !o.Minimal)
            {
                work = Replace(work, "(?<=\\d)[ \u00A0\u202F]*[xX][ \u00A0\u202F]*(?=\\d)", "\u00A0×\u00A0", out n, "\u00A0×\u00A0");
                r.Count("dimensions ×", n);
            }
            // (6d) ordinaux
            if (o.Ordinals)
            {
                // Le canon de Grammalecte (décision de Rémi, 13/09) : les
                // ordinaux en EXPOSANTS Unicode — 1ᵉʳ, 1ʳᵉ, 2ⁿᵈ, 2ᵈᵉ, 2ᵉ, et
                // leurs pluriels. Les formes plates (2e, 1er) et fautives
                // (2ème, 1ère) y passent ; une forme déjà en exposant ne
                // bouge plus — Typonanny ne la voit plus comme une erreur.
                var total = 0;
                work = Replace(work, @"\b1(?:ers|iers)\b", "1ᵉʳˢ", out n, null); total += n;
                work = Replace(work, @"\b1(?:er|ier)\b", "1ᵉʳ", out n, null); total += n;
                work = Replace(work, @"\b1(?:ères|res|ières)\b", "1ʳᵉˢ", out n, null); total += n;
                work = Replace(work, @"\b1(?:ère|re|ière)\b", "1ʳᵉ", out n, null); total += n;
                work = Replace(work, @"\b2(?:ndes|des)\b", "2ᵈᵉˢ", out n, null); total += n;
                work = Replace(work, @"\b2(?:nde|de)\b", "2ᵈᵉ", out n, null); total += n;
                work = Replace(work, @"\b2nds\b", "2ⁿᵈˢ", out n, null); total += n;
                work = Replace(work, @"\b2nd\b", "2ⁿᵈ", out n, null); total += n;
                work = Replace(work, @"\b(\d+)(?:i?èmes|es)\b", "$1ᵉˢ", out n, null); total += n;
                work = Replace(work, @"\b(\d+)(?:i?ème|e)\b", "$1ᵉ", out n, null); total += n;
                r.Count("ordinaux", total);
            }
            // (6e) majuscules à accentuer — signalement seul
            if (o.FlagCapitals)
            {
                foreach (Match m in Regex.Matches(work,
                    @"(?<=^|[.!?…]\s+)(Etat|Etats|Ecole|Ecoles|Eglise|Eglises|Elève|Eleve|Ere|Ete|Etude|Etudes|Epoque|Evidemment|Egalement|A)(?=\s)",
                    RegexOptions.Multiline))
                {
                    var word = m.Groups[1].Value;
                    var proposed = word == "A" ? "À" : "É" + word.Substring(1);
                    r.Warnings.Add("majuscule à accentuer ? « " + word + " » → « " + proposed + " » (non corrigé d'office)");
                    if (r.Warnings.Count > 20) break;
                }
            }

            r.Text = Unmask(work, masks);
            return r;
        }

        // ---------------------------------------------------------- helpers

        private static string Replace(string text, string pattern, string replacement, out int n, string alreadyGood)
        {
            var count = 0;
            var result = new Regex(pattern).Replace(text, delegate(Match m)
            {
                var produced = m.Result(replacement);
                if (m.Value != produced && (alreadyGood == null || m.Value != alreadyGood)) count++;
                return produced;
            });
            n = count;
            return result;
        }

        private static string Ligate(string text, string[] list, string digram, string ligature, out int n)
        {
            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in list)
            {
                var flat = word.Replace(ligature, digram)
                    .Replace(ligature.ToUpperInvariant(), digram.ToUpperInvariant());
                table[flat] = word;
            }
            var count = 0;
            var result = Regex.Replace(text, @"[\p{L}]*" + digram + @"[\p{L}]*", delegate(Match m)
            {
                string good;
                if (!table.TryGetValue(m.Value, out good)) return m.Value;
                count++;
                return KeepCase(m.Value, good);
            }, RegexOptions.IgnoreCase);
            n = count;
            return result;
        }

        private static string ConvertQuotes(string work, string nbsp, int openQuotesBefore, out int count)
        {
            count = 0;
            var sb = new StringBuilder(work.Length + 8);
            // Avant ce paragraphe, une citation est ouverte ou ne l'est pas :
            // la convention des « guillemets de suite » (un « en tête de
            // chaque paragraphe d'une longue citation, un seul » à la fin)
            // ferait sinon grimper la profondeur sans fin.
            var depth = Math.Min(1, Math.Max(0, openQuotesBefore));
            var continuation = FirstQuoteIsContinuation(work, depth);
            // Les guillemets droits du paragraphe. UN DIALOGUE S'ÉTALE SUR
            // PLUSIEURS PARAGRAPHES (une réplique par ligne) : son ouvrant est
            // seul dans le premier, son fermant seul dans le dernier. Un
            // guillemet droit orphelin EN TÊTE de paragraphe est donc un
            // ouvrant ; un orphelin EN FIN de paragraphe, citation ouverte,
            // est un fermant ; les autres vont par paires.
            var quotes = new List<int>();
            for (var q = 0; q < work.Length; q++) if (work[q] == '"') quotes.Add(q);
            var odd = quotes.Count % 2 == 1;
            var openAtStart = odd && IsAtParagraphStart(work, quotes[0]);
            // Le fermant du dialogue : le dernier guillemet en fin de
            // paragraphe, OU (le canon des incises) un guillemet qui suit la
            // ponctuation de fin de parole et précède une incise — « mon
            // ami, » rétorqua l'autre. La virgule tapée APRÈS le guillemet
            // (« ami", rétorqua ») passe devant lui.
            var reservedClose = -1;
            var swapPunctuation = false;
            if (odd && !openAtStart && depth >= 1)
            {
                var last = quotes[quotes.Count - 1];
                if (IsAtParagraphEnd(work, last)) reservedClose = last;
                else
                    for (var q = quotes.Count - 1; q >= 0; q--)
                    {
                        bool swap;
                        if (!IsBeforeIncise(work, quotes[q], out swap)) continue;
                        reservedClose = quotes[q];
                        swapPunctuation = swap;
                        break;
                    }
            }
            var i = 0;
            while (i < work.Length)
            {
                var c = work[i];
                if (c == '«') { if (i != continuation) depth++; sb.Append(c); i++; continue; }
                if (c == '»') { if (depth > 0) depth--; sb.Append(c); i++; continue; }
                if (c != '"') { sb.Append(c); i++; continue; }
                if (openAtStart && i == quotes[0])
                {
                    // l'ouvrant d'un dialogue : « et, si rien n'était ouvert,
                    // la citation commence (sinon c'est une suite)
                    sb.Append('«').Append(nbsp);
                    if (depth == 0) depth++;
                    count++;
                    i++;
                    while (i < work.Length && (work[i] == ' ' || work[i] == '\u00A0' || work[i] == '\u202F')) i++;
                    continue;
                }
                if (i == reservedClose)
                {
                    // le fermant du dialogue : » collé au texte par l'insécable ;
                    // une ponctuation tapée juste après passe devant lui
                    TrimTrailingSpaces(sb);
                    i++;
                    if (swapPunctuation)
                    {
                        sb.Append(work[i]);
                        i++;
                    }
                    sb.Append(nbsp).Append('»');
                    if (depth > 0) depth--;
                    count++;
                    continue;
                }
                var close = work.IndexOf('"', i + 1);
                if (close == reservedClose) close = -1; // réservé au fermant
                if (close < 0 || work.IndexOf('\n', i, close - i) >= 0) { sb.Append(c); i++; continue; }
                var inner = work.Substring(i + 1, close - i - 1).Trim(' ', '\u00A0', '\u202F');
                if (depth > 0) sb.Append('\u201C').Append(inner).Append('\u201D');
                else sb.Append('«').Append(nbsp).Append(inner).Append(nbsp).Append('»');
                count++;
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>Le guillemet est le premier signe du paragraphe — rien
        /// avant lui que des blancs, ou un tiret de dialogue et ses blancs.</summary>
        private static bool IsAtParagraphStart(string text, int index)
        {
            for (var i = 0; i < index; i++)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c) || c == '—' || c == '–' || c == '-') continue;
                return false;
            }
            return true;
        }

        private static bool IsSpeechEnd(char c)
        {
            return c == '.' || c == '!' || c == '?' || c == '…' || c == ',' || c == ';' || c == ':';
        }

        /// <summary>Le guillemet est le dernier signe du paragraphe — après
        /// lui, rien que des blancs ou une ponctuation finale — ET il suit une
        /// ponctuation de fin de parole (« pertinent." », « mon ami," ») : à
        /// la frappe, un guillemet tapé après un espace en bout de ligne
        /// (« si " ») est le début d'une paire à venir, pas le fermant.</summary>
        private static bool IsAtParagraphEnd(string text, int index)
        {
            var before = index - 1;
            while (before >= 0 && (text[before] == ' ' || text[before] == '\u00A0' || text[before] == '\u202F')) before--;
            if (before < 0) return false;
            if (!IsSpeechEnd(text[before])) return false;
            for (var i = index + 1; i < text.Length; i++)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c) || c == '.' || c == '!' || c == '?' || c == '…' || c == ',' || c == ';' || c == ':' || c == ')') continue;
                return false;
            }
            return true;
        }

        /// <summary>Le guillemet ferme une réplique AVANT une incise : il suit
        /// la ponctuation de fin de parole et précède un blanc puis une
        /// minuscule (« mon ami," rétorqua »). swap : la ponctuation a été
        /// tapée juste après lui (« ami", rétorqua ») — elle passera devant.</summary>
        private static bool IsBeforeIncise(string text, int index, out bool swap)
        {
            swap = false;
            var after = index + 1;
            var punctuationAfter = after < text.Length && IsSpeechEnd(text[after]);
            var before = index - 1;
            while (before >= 0 && (text[before] == ' ' || text[before] == '\u00A0' || text[before] == '\u202F')) before--;
            if (before < 0) return false;
            var punctuationBefore = IsSpeechEnd(text[before]);
            if (!punctuationBefore && !punctuationAfter) return false;
            if (!punctuationBefore) { swap = true; after++; }
            else if (punctuationAfter) return false; // « ami,", » : on ne devine pas
            var j = after;
            var blanks = 0;
            while (j < text.Length && (text[j] == ' ' || text[j] == '\u00A0' || text[j] == '\u202F')) { j++; blanks++; }
            if (blanks == 0 || j >= text.Length) return false;
            return char.IsLower(text[j]);
        }

        private static void TrimTrailingSpaces(StringBuilder sb)
        {
            while (sb.Length > 0)
            {
                var last = sb[sb.Length - 1];
                if (last != ' ' && last != '\u00A0' && last != '\u202F') break;
                sb.Length--;
            }
        }

        /// <summary>Le remplaçant prend la casse du remplacé : « Maison » →
        /// « Demeure », « MAISON » → « DEMEURE », sinon tel quel. Partagé
        /// avec les synonymes (revue du 13/09 : une seule version).</summary>
        public static string KeepCase(string original, string corrected)
        {
            var allUpper = true;
            foreach (var c in original) if (char.IsLetter(c) && char.IsLower(c)) { allUpper = false; break; }
            if (allUpper && original.Length > 1) return corrected.ToUpperInvariant();
            if (char.IsUpper(original[0]) && !char.IsUpper(corrected[0]))
                return char.ToUpperInvariant(corrected[0]) + corrected.Substring(1);
            return corrected;
        }

        // Masquage : chaque zone protégée devient UN caractère de la zone
        // privée (U+F000 + index) — la table lève toute ambiguïté au retour.
        private const int MaskBase = 0xF000;

        private static string Mask(string text, List<int[]> extra, List<string> masks)
        {
            var spans = new List<int[]>();
            if (extra != null) foreach (var span in extra) if (span[1] > 0) spans.Add(new[] { span[0], span[1] });
            foreach (var rx in Protected)
                foreach (Match m in rx.Matches(text))
                    if (!Overlaps(spans, m.Index, m.Length)) spans.Add(new[] { m.Index, m.Length });
            if (spans.Count == 0) return text;
            spans.Sort(delegate(int[] a, int[] b) { return a[0].CompareTo(b[0]); });
            var sb = new StringBuilder();
            var pos = 0;
            foreach (var span in spans)
            {
                if (span[0] < pos) continue; // chevauchement résiduel
                sb.Append(text, pos, span[0] - pos);
                masks.Add(text.Substring(span[0], span[1]));
                sb.Append((char)(MaskBase + masks.Count - 1));
                pos = span[0] + span[1];
                if (masks.Count >= 0x0F00) break;
            }
            sb.Append(text, pos, text.Length - pos);
            return sb.ToString();
        }

        private static bool Overlaps(List<int[]> spans, int start, int length)
        {
            foreach (var span in spans)
                if (start < span[0] + span[1] && span[0] < start + length) return true;
            return false;
        }

        private static string Unmask(string text, List<string> masks)
        {
            if (masks.Count == 0) return text;
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                var index = c - MaskBase;
                if (index >= 0 && index < masks.Count) sb.Append(masks[index]);
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>La passe typographique appliquée au PIVOT : chaque paragraphe
    /// est nettoyé comme un texte plat (les règles voient le contexte
    /// au-delà des frontières de runs), puis le texte corrigé est REDISTRIBUÉ
    /// sur les runs par le diff de caractères — un caractère conservé garde
    /// son run (donc son format), une insertion rejoint le run du caractère
    /// qui la précède, les éléments (notes, images) restent intacts, les
    /// passages « ne pas corriger » sont protégés.</summary>
    /// <summary>La typographie À LA FRAPPE (b45) : la même passe, mais
    /// appliquée au paragraphe en cours d'écriture après chaque frappe, en
    /// ne gardant que les corrections PROCHES DU CURSEUR et derrière lui.
    /// Trois garde-fous, pour ne jamais se battre avec l'auteur :
    /// - rien après le curseur (ce qu'il n'a pas encore écrit ne bouge pas) ;
    /// - rien à plus de `window` caractères en arrière (le reste du
    ///   paragraphe attend la passe complète) ;
    /// - jamais une pure suppression de ce qu'il vient de taper (un espace
    ///   en fin de ligne, un second espace : on ne l'efface pas sous ses
    ///   doigts — la passe s'en chargera).
    /// Rend les opérations retenues, les autres devenues « conservé », et
    /// le déplacement du curseur.</summary>
    public static class TypographyLive
    {
        public static List<CharOp> Restrict(List<CharOp> ops, int caretOld, int typedLength,
            int window, out int caretDelta, out bool changed)
        {
            List<KeyValuePair<int, string>> accepted;
            var result = Restrict(ops, caretOld, typedLength, window, null, out caretDelta, out accepted);
            changed = accepted.Count > 0;
            return result;
        }

        /// <summary>Comme ci-dessus, avec les corrections REFUSÉES par l'auteur
        /// (Ctrl+Z sur une correction automatique) : `vetoed(début, texte
        /// remplacé)` rend vrai pour un bloc à laisser tel quel. Rend aussi
        /// les blocs retenus (début dans l'ancien texte, texte remplacé) —
        /// c'est ce qu'un Ctrl+Z ultérieur mémorisera.</summary>
        public static List<CharOp> Restrict(List<CharOp> ops, int caretOld, int typedLength,
            int window, Func<int, string, bool> vetoed,
            out int caretDelta, out List<KeyValuePair<int, string>> accepted)
        {
            caretDelta = 0;
            accepted = new List<KeyValuePair<int, string>>();
            var result = new List<CharOp>(ops.Count);
            var i = 0; // position dans l'ancien texte
            var k = 0;
            while (k < ops.Count)
            {
                if (ops[k].Type == ' ')
                {
                    result.Add(ops[k]);
                    i++;
                    k++;
                    continue;
                }
                // un bloc d'éditions contiguës
                var startOld = i;
                var deletes = 0;
                var inserts = 0;
                var end = k;
                while (end < ops.Count && ops[end].Type != ' ')
                {
                    if (ops[end].Type == '-') deletes++;
                    else inserts++;
                    end++;
                }
                var endOld = startOld + deletes;
                var pureDeletionOfTyped = inserts == 0
                    && startOld >= caretOld - typedLength && endOld <= caretOld;
                var deleted = new StringBuilder();
                for (var j = k; j < end; j++) if (ops[j].Type == '-') deleted.Append(ops[j].Char);
                var accept = startOld >= caretOld - window && startOld <= caretOld
                    && endOld <= caretOld && !pureDeletionOfTyped
                    && (vetoed == null || !vetoed(startOld, deleted.ToString()));
                for (var j = k; j < end; j++)
                {
                    if (accept) result.Add(ops[j]);
                    else if (ops[j].Type == '-') result.Add(new CharOp(' ', ops[j].Char));
                    // une insertion refusée disparaît
                }
                if (accept)
                {
                    accepted.Add(new KeyValuePair<int, string>(startOld, deleted.ToString()));
                    caretDelta += inserts - deletes;
                }
                i = endOld;
                k = end;
            }
            return result;
        }
    }

    public class TypographyParagraphChange
    {
        public int Index;
        public string Before = "";
        public string After = "";
        public List<CharOp> Ops;
        public TextParagraph Replacement;
    }

    public class TypographyPassResult
    {
        public TypographyResult Summary = new TypographyResult();
        public List<TypographyParagraphChange> Changes = new List<TypographyParagraphChange>();
        public List<TextParagraph> Paragraphs = new List<TextParagraph>();
        public int Skipped; // paragraphes au diff trop lourd, laissés tels quels
    }

    public static class TypographyPass
    {
        public static TypographyPassResult Run(TextDocument document, TypographyOptions options)
        {
            var result = new TypographyPassResult();
            var openQuotes = 0; // les « » restés ouverts avant le paragraphe
            for (var i = 0; i < document.Paragraphs.Count; i++)
            {
                var paragraph = document.Paragraphs[i];
                var flat = PivotEdit.FlatText(paragraph);
                var cleaned = Typography.Clean(flat, options, NoProofSpans(paragraph), openQuotes);
                openQuotes = Typography.QuoteDepth(cleaned.Text, openQuotes);
                result.Summary.Merge(cleaned);
                if (cleaned.Text == flat) { result.Paragraphs.Add(paragraph); continue; }
                var ops = CharDiff.Diff(flat, cleaned.Text);
                if (ops == null) { result.Skipped++; result.Paragraphs.Add(paragraph); continue; }
                var replacement = Redistribute(paragraph, ops);
                result.Paragraphs.Add(replacement);
                result.Changes.Add(new TypographyParagraphChange
                {
                    Index = i, Before = flat, After = cleaned.Text, Ops = ops, Replacement = replacement
                });
            }
            return result;
        }

        public static List<int[]> NoProofSpans(TextParagraph paragraph)
        {
            List<int[]> spans = null;
            var pos = 0;
            foreach (var run in paragraph.Runs)
            {
                var length = PivotEdit.IsElement(run) ? 1 : run.Text.Length;
                if (run.NoProof && length > 0)
                {
                    if (spans == null) spans = new List<int[]>();
                    spans.Add(new[] { pos, length });
                }
                pos += length;
            }
            return spans;
        }

        /// <summary>Le paragraphe corrigé, runs reconstruits (public : testé).
        /// Les insertions d'un BLOC de changements (suppressions et
        /// insertions contiguës) héritent du format des caractères qu'elles
        /// remplacent : les signes des signes (« pour "), les blancs des
        /// blancs (la fine pour l'espace, appariés par la fin), un blanc
        /// orphelin suit le signe inséré voisin (l'insécable dans « »), le
        /// reste suit le caractère précédent.</summary>
        public static TextParagraph Redistribute(TextParagraph paragraph, List<CharOp> ops)
        {
            var owners = new List<int>();
            for (var r = 0; r < paragraph.Runs.Count; r++)
            {
                var run = paragraph.Runs[r];
                var length = PivotEdit.IsElement(run) ? 1 : run.Text.Length;
                for (var k = 0; k < length; k++) owners.Add(r);
            }
            var copy = PivotEdit.CloneParagraphShell(paragraph);
            var pieces = new List<KeyValuePair<int, StringBuilder>>(); // (run, texte) ; élément = texte null
            var i = 0;          // position dans l'ancien texte
            var lastOwner = owners.Count > 0 ? owners[0] : 0;
            var k0 = 0;
            while (k0 < ops.Count)
            {
                if (ops[k0].Type == ' ')
                {
                    var current = i < owners.Count ? owners[i] : lastOwner;
                    if (i < owners.Count && PivotEdit.IsElement(paragraph.Runs[current])) AppendElement(pieces, current);
                    else AppendChar(pieces, current, ops[k0].Char);
                    lastOwner = current;
                    i++;
                    k0++;
                    continue;
                }
                // Un bloc de changements : ses suppressions, ses insertions.
                var k1 = k0;
                while (k1 < ops.Count && ops[k1].Type != ' ') k1++;
                var deletedOwners = new List<int>();   // propriétaires des supprimés (texte seul)
                var deletedChars = new List<char>();
                var inserted = new List<char>();
                for (var k = k0; k < k1; k++)
                {
                    if (ops[k].Type == '-')
                    {
                        if (i < owners.Count && PivotEdit.IsElement(paragraph.Runs[owners[i]]))
                            AppendElement(pieces, owners[i]); // jamais supprimé par la typographie
                        else if (i < owners.Count) { deletedOwners.Add(owners[i]); deletedChars.Add(ops[k].Char); }
                        i++;
                    }
                    else inserted.Add(ops[k].Char);
                }
                var assigned = AssignInsertions(inserted, deletedChars, deletedOwners,
                    TextOwner(paragraph, owners, lastOwner, i));
                for (var n = 0; n < inserted.Count; n++) AppendChar(pieces, assigned[n], inserted[n]);
                if (deletedOwners.Count > 0) lastOwner = deletedOwners[deletedOwners.Count - 1];
                k0 = k1;
            }
            foreach (var piece in pieces)
            {
                var source = paragraph.Runs[piece.Key];
                if (piece.Value == null) { copy.Runs.Add(PivotEdit.CloneRun(source)); continue; }
                var run = PivotEdit.CloneFormat(source);
                run.Text = piece.Value.ToString();
                copy.Runs.Add(run);
            }
            if (copy.Runs.Count == 0) copy.Runs.Add(new TextRun());
            return copy;
        }

        private static bool IsBlank(char c)
        {
            return c == ' ' || c == '\u00A0' || c == '\u202F' || c == '\t';
        }

        /// <summary>L'appariement des insertions d'un bloc aux suppressions
        /// du même bloc (voir Redistribute).</summary>
        private static int[] AssignInsertions(List<char> inserted, List<char> deletedChars,
            List<int> deletedOwners, int fallback)
        {
            var result = new int[inserted.Count];
            for (var n = 0; n < result.Length; n++) result[n] = -1;
            // Signes ↔ signes, dans l'ordre.
            var d = 0;
            for (var n = 0; n < inserted.Count; n++)
            {
                if (IsBlank(inserted[n])) continue;
                while (d < deletedChars.Count && IsBlank(deletedChars[d])) d++;
                if (d < deletedChars.Count) { result[n] = deletedOwners[d]; d++; }
            }
            // Blancs ↔ blancs, appariés par la FIN (la fine remplace l'espace
            // qui précédait le point d'exclamation, pas l'espace du guillemet).
            d = deletedChars.Count - 1;
            for (var n = inserted.Count - 1; n >= 0; n--)
            {
                if (!IsBlank(inserted[n])) continue;
                while (d >= 0 && !IsBlank(deletedChars[d])) d--;
                if (d >= 0) { result[n] = deletedOwners[d]; d--; }
            }
            // Les orphelins : un blanc suit le signe inséré voisin (le suivant,
            // sinon le précédent) ; un signe suit le premier supprimé ; sinon
            // le caractère précédent.
            for (var n = 0; n < inserted.Count; n++)
            {
                if (result[n] >= 0) continue;
                if (IsBlank(inserted[n]))
                {
                    for (var m = n + 1; m < inserted.Count && result[n] < 0; m++)
                        if (!IsBlank(inserted[m]) && result[m] >= 0) result[n] = result[m];
                    for (var m = n - 1; m >= 0 && result[n] < 0; m--)
                        if (!IsBlank(inserted[m]) && result[m] >= 0) result[n] = result[m];
                }
                if (result[n] < 0 && deletedOwners.Count > 0) result[n] = deletedOwners[0];
                if (result[n] < 0) result[n] = fallback;
            }
            return result;
        }

        /// <summary>Le run TEXTE qui accueille une insertion : celui du
        /// caractère précédent, sinon le prochain run de texte.</summary>
        private static int TextOwner(TextParagraph paragraph, List<int> owners, int lastOwner, int position)
        {
            if (!PivotEdit.IsElement(paragraph.Runs[lastOwner])) return lastOwner;
            for (var k = position; k < owners.Count; k++)
                if (!PivotEdit.IsElement(paragraph.Runs[owners[k]])) return owners[k];
            for (var k = lastOwner; k >= 0; k--)
                if (!PivotEdit.IsElement(paragraph.Runs[k])) return k;
            for (var k = 0; k < paragraph.Runs.Count; k++)
                if (!PivotEdit.IsElement(paragraph.Runs[k])) return k;
            return lastOwner;
        }

        private static void AppendChar(List<KeyValuePair<int, StringBuilder>> pieces, int owner, char c)
        {
            if (pieces.Count > 0 && pieces[pieces.Count - 1].Key == owner && pieces[pieces.Count - 1].Value != null)
            {
                pieces[pieces.Count - 1].Value.Append(c);
                return;
            }
            var sb = new StringBuilder();
            sb.Append(c);
            pieces.Add(new KeyValuePair<int, StringBuilder>(owner, sb));
        }

        private static void AppendElement(List<KeyValuePair<int, StringBuilder>> pieces, int owner)
        {
            // Un élément déjà posé (diff qui le « supprime » puis le garde) ne se double pas.
            if (pieces.Count > 0 && pieces[pieces.Count - 1].Key == owner && pieces[pieces.Count - 1].Value == null) return;
            pieces.Add(new KeyValuePair<int, StringBuilder>(owner, null));
        }
    }
}

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
        public bool Ordinals = true;     // 2ème → 2e
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
                var straight = 0;
                foreach (var c in work) if (c == '"') straight++;
                if (straight % 2 == 1)
                    r.Warnings.Add("guillemets droits en nombre impair : la conversion en « » est laissée de côté");
                else
                {
                    var nbsp = o.InsideQuotes.ToString();
                    var count = 0;
                    work = Regex.Replace(work, "\"[ \u00A0\u202F]*([^\"\n]*?)[ \u00A0\u202F]*\"",
                        delegate(Match m) { count++; return "«" + nbsp + m.Groups[1].Value + nbsp + "»"; });
                    r.Count("guillemets français", count);
                }
            }
            // (4a) tirets de dialogue
            if (o.DialogueDashes && !o.Minimal)
            {
                work = Replace(work, @"(?m)^([ \t]*)-{1,2}[ \t]+", "$1— ", out n, null); r.Count("tirets de dialogue", n);
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
                var total = 0;
                work = Replace(work, @"\b1ères\b", "1res", out n, null); total += n;
                work = Replace(work, @"\b1ère\b", "1re", out n, null); total += n;
                work = Replace(work, @"\b2ndes?\b", "2de", out n, null); total += n;
                work = Replace(work, @"\b(\d+)i?èmes?\b", "$1e", out n, null); total += n;
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

        private static string KeepCase(string original, string corrected)
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
            for (var i = 0; i < document.Paragraphs.Count; i++)
            {
                var paragraph = document.Paragraphs[i];
                var flat = PivotEdit.FlatText(paragraph);
                var cleaned = Typography.Clean(flat, options, NoProofSpans(paragraph));
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

        private static List<int[]> NoProofSpans(TextParagraph paragraph)
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

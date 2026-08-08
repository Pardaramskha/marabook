using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UniversSale.Correction.Hunspell
{
    /// <summary>Une condition d'affixe compilée : suite d'items (littéral,
    /// classe [xyz], classe niée [^xyz], joker .) — appariée au DÉBUT du
    /// radical pour un préfixe, à sa FIN pour un suffixe.</summary>
    public sealed class ConditionItem
    {
        public bool Negated;
        public string Chars; // null = « . » (n'importe quel caractère)

        public bool Matches(char c)
        {
            if (Chars == null) return true;
            var has = Chars.IndexOf(c) >= 0;
            return Negated ? !has : has;
        }
    }

    public sealed class AffixEntry
    {
        public string Strip = "";           // « 0 » du format = vide
        public string Append = "";
        public string[] Continuation = None; // drapeaux de continuation (…/D'Q')
        public ConditionItem[] Condition;    // null = toujours vrai

        public static readonly string[] None = new string[0];

        public bool ContinuationHas(string flag)
        {
            if (flag == null) return false;
            foreach (var f in Continuation) if (f == flag) return true;
            return false;
        }
    }

    public sealed class AffixRule
    {
        public string Flag = "";
        public bool CrossProduct;            // Y = combinable préfixe+suffixe
        public List<AffixEntry> Entries = new List<AffixEntry>();
    }

    /// <summary>Le lecteur du .aff français (batch 27, lot C.1) — SET, FLAG
    /// long, TRY, KEY, MAP, REP, ICONV/OCONV, PFX/SFX et les drapeaux
    /// spéciaux (NEEDAFFIX, FORBIDDENWORD, CIRCUMFIX, KEEPCASE, NOSUGGEST,
    /// FULLSTRIP). Le français n'a NI COMPOUND* NI PHONE — les deux parties
    /// les plus difficiles de Hunspell sont hors sujet : un .aff futur qui
    /// en contiendrait fait LEVER proprement, jamais ignorer. BREAK et
    /// WORDCHARS sont ignorés à dessein : le découpage des mots appartient
    /// au tokeniseur maison (batch 29, 0.6 — un champ mort ne doit pas
    /// passer pour une fonctionnalité).</summary>
    public sealed class AffixFile
    {
        public bool FlagLong;                // FLAG long : drapeaux sur 2 chars
        public bool FullStrip;
        public string TryChars = "";
        public string KeyRows = "";
        public string NeedAffixFlag;
        public string ForbiddenFlag;
        public string CircumfixFlag;
        public string KeepCaseFlag;
        public string NoSuggestFlag;
        public readonly List<string[]> Replacements = new List<string[]>();
        public readonly List<string[]> InputConversions = new List<string[]>();
        public readonly List<string[]> OutputConversions = new List<string[]>();
        public readonly List<string> MapClasses = new List<string>();
        public readonly Dictionary<string, AffixRule> Prefixes
            = new Dictionary<string, AffixRule>();
        public readonly Dictionary<string, AffixRule> Suffixes
            = new Dictionary<string, AffixRule>();

        public static AffixFile Load(string path)
        {
            var file = new AffixFile();
            AffixRule current = null;
            var currentIsSuffix = false;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var tokens = line.Split(new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                switch (tokens[0])
                {
                    case "SET":
                        if (tokens.Length > 1 && tokens[1] != "UTF-8")
                            throw new NotSupportedException(
                                "Encodage .aff non géré : " + tokens[1]);
                        break;
                    case "FLAG":
                        if (tokens.Length > 1 && tokens[1] == "long")
                            file.FlagLong = true;
                        else
                            throw new NotSupportedException(
                                "FLAG non géré : " + line);
                        break;
                    case "TRY": file.TryChars = tokens[1]; break;
                    case "KEY": file.KeyRows = tokens[1]; break;
                    case "WORDCHARS": break; // le tokeniseur maison fait foi
                    case "BREAK": break;     // idem — jamais un champ mort
                    case "FULLSTRIP": file.FullStrip = true; break;
                    case "NEEDAFFIX": file.NeedAffixFlag = tokens[1]; break;
                    case "FORBIDDENWORD": file.ForbiddenFlag = tokens[1]; break;
                    case "CIRCUMFIX": file.CircumfixFlag = tokens[1]; break;
                    case "KEEPCASE": file.KeepCaseFlag = tokens[1]; break;
                    case "NOSUGGEST": file.NoSuggestFlag = tokens[1]; break;
                    case "MAP":
                        if (!IsCount(tokens)) file.MapClasses.Add(tokens[1]);
                        break;
                    case "REP":
                        if (!IsCount(tokens))
                            file.Replacements.Add(new[]
                            { tokens[1], tokens.Length > 2 ? tokens[2] : "" });
                        break;
                    case "ICONV":
                        if (!IsCount(tokens))
                            file.InputConversions.Add(new[] { tokens[1], tokens[2] });
                        break;
                    case "OCONV":
                        if (!IsCount(tokens))
                            file.OutputConversions.Add(new[] { tokens[1], tokens[2] });
                        break;
                    case "PFX":
                    case "SFX":
                        var suffix = tokens[0] == "SFX";
                        var flag = tokens[1];
                        var table = suffix ? file.Suffixes : file.Prefixes;
                        AffixRule rule;
                        if (!table.TryGetValue(flag, out rule))
                        {
                            // Ligne d'en-tête : « SFX X. Y 4 ».
                            rule = new AffixRule
                            {
                                Flag = flag,
                                CrossProduct = tokens.Length > 2 && tokens[2] == "Y"
                            };
                            table[flag] = rule;
                            current = rule;
                            currentIsSuffix = suffix;
                            break;
                        }
                        if (current != rule || currentIsSuffix != suffix)
                        { current = rule; currentIsSuffix = suffix; }
                        rule.Entries.Add(ParseEntry(file, tokens));
                        break;
                    default:
                        if (tokens[0].StartsWith("COMPOUND", StringComparison.Ordinal)
                            || tokens[0] == "PHONE" || tokens[0] == "CHECKCOMPOUNDPATTERN"
                            || tokens[0] == "ONLYINCOMPOUND")
                            throw new NotSupportedException(
                                "Directive hors doctrine (batch 27) : " + tokens[0]
                                + " — le moteur maison ne gère ni la composition"
                                + " ni la phonétique, par décision documentée.");
                        break; // directives cosmétiques inconnues : ignorées
                }
            }
            return file;
        }

        private static bool IsCount(string[] tokens)
        {
            int ignored;
            return tokens.Length == 2 && int.TryParse(tokens[1], out ignored);
        }

        private static AffixEntry ParseEntry(AffixFile file, string[] tokens)
        {
            // « SFX X. il ux/D'Q' ail » : strip, append[/continuation], condition.
            var entry = new AffixEntry();
            entry.Strip = tokens[2] == "0" ? "" : tokens[2];
            var append = tokens[3];
            var slash = append.IndexOf('/');
            if (slash >= 0)
            {
                entry.Continuation = file.ParseFlags(append.Substring(slash + 1));
                append = append.Substring(0, slash);
            }
            entry.Append = append == "0" ? "" : append;
            var condition = tokens.Length > 4 ? tokens[4] : ".";
            entry.Condition = condition == "." ? null : ParseCondition(condition);
            return entry;
        }

        private static ConditionItem[] ParseCondition(string pattern)
        {
            var items = new List<ConditionItem>();
            var i = 0;
            while (i < pattern.Length)
            {
                if (pattern[i] == '[')
                {
                    var close = pattern.IndexOf(']', i);
                    if (close < 0) close = pattern.Length - 1;
                    var body = pattern.Substring(i + 1, close - i - 1);
                    var item = new ConditionItem();
                    if (body.StartsWith("^", StringComparison.Ordinal))
                    {
                        item.Negated = true;
                        body = body.Substring(1);
                    }
                    item.Chars = body;
                    items.Add(item);
                    i = close + 1;
                }
                else
                {
                    items.Add(pattern[i] == '.'
                        ? new ConditionItem()
                        : new ConditionItem { Chars = pattern[i].ToString() });
                    i++;
                }
            }
            return items.ToArray();
        }

        /// <summary>Découpe une chaîne de drapeaux : paires de caractères en
        /// FLAG long (« L'D'Q' » → L', D', Q'), un caractère sinon.</summary>
        public string[] ParseFlags(string flags)
        {
            if (string.IsNullOrEmpty(flags)) return AffixEntry.None;
            if (!FlagLong)
            {
                var single = new string[flags.Length];
                for (var i = 0; i < flags.Length; i++)
                    single[i] = flags[i].ToString();
                return single;
            }
            var count = flags.Length / 2;
            var result = new string[count];
            for (var i = 0; i < count; i++)
                result[i] = flags.Substring(i * 2, 2);
            return result;
        }
    }
}

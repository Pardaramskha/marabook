using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniversSale.Correction
{
    public enum TokenKind
    {
        Word,   // vérifiable en orthographe, comptable en répétition
        Number  // 1914, 4x4, 10h30, n°3, 1er, XIXe, Ⅴ — jamais ni l'un ni l'autre
    }

    /// <summary>La casse du CŒUR du token — Hunspell en a besoin (KEEPCASE,
    /// noms propres) et le vérificateur de sigles aussi.</summary>
    public enum CaseShape { Lower, Capitalized, AllCaps, Mixed }

    /// <summary>Un mot français découpé : le token ENTIER (surface, offsets
    /// dans le texte plat) et son CŒUR — sans préfixe d'élision ni pronom
    /// enclitique — qui est ce que l'orthographe vérifie, ce que la
    /// répétition compare, et ce que « mot entier » borne.</summary>
    public sealed class Token
    {
        public int Start;
        public int Length;
        public string Surface = "";
        public int CoreStart;
        public int CoreLength;
        public string CoreSurface = ""; // NFC, casse d'origine
        public string Folded = "";      // la clé de comparaison unique (Fold)
        public string ElisionPrefix = ""; // « l' », « jusqu' »… (surface)
        public string Enclitic = "";      // « -il », « -t-il », « -t'en »…
        public TokenKind Kind;
        public CaseShape Shape;
        /// <summary>Les segments du cœur d'un composé à trait d'union
        /// (« grand-père » → grand, père), en NFC — l'orthographe vérifie la
        /// forme pleine d'abord, puis les segments. Longueur 1 hors composé.</summary>
        public string[] CoreParts = Empty;

        private static readonly string[] Empty = new string[0];
        public int CoreEnd { get { return CoreStart + CoreLength; } }
    }

    /// <summary>LA définition du mot français — batch 27, lot A. Il en
    /// existait trois divergentes (TextStats, RepetitionChecker,
    /// PivotSearch) ; tous les consommateurs passent désormais par ici, et
    /// le correcteur orthographique s'y alimente. Chaque décision est
    /// commentée sur place et testée en C7. Aucune dépendance WPF.</summary>
    public static class FrenchTokenizer
    {
        // Jointeurs INTÉRIEURS d'un token (entre deux caractères de mot) :
        // apostrophes droite et typographique, trait d'union (et insécable),
        // degré (n°3). Les tirets demi/cadratin (– —) sont TOUJOURS des
        // séparateurs : ce sont les tirets de dialogue.
        private static bool IsJoiner(char c)
        {
            return c == '\'' || c == '’' || c == '-' || c == '‑' || c == '°';
        }

        // Caractère de mot : lettres, chiffres, les numériques « lettrés »
        // (Ⅴ roman U+2164, catégorie Nl — ni lettre ni chiffre)… et les
        // MARQUES COMBINANTES : dans un texte NFD (import ODT/macOS),
        // l'accent de « café » est un caractère séparé que char.IsLetter
        // refuse — sans cette clause, le é décomposé tombait hors du mot
        // (attrapé par C7).
        private static bool IsWordChar(char c)
        {
            if (char.IsLetterOrDigit(c) || char.IsNumber(c)) return true;
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpacingCombiningMark
                || category == UnicodeCategory.EnclosingMark;
        }

        // Préfixes d'élision, PLIÉS, du plus long au plus court. Liste
        // fermée : l'apostrophe hors de ces préfixes ne découpe pas.
        private static readonly string[] Elisions =
        {
            "jusqu'", "lorsqu'", "puisqu'", "quoiqu'", "presqu'", "quelqu'",
            "qu'", "l'", "d'", "j'", "n'", "m'", "t'", "s'", "c'"
        };

        // Élisions LEXICALISÉES : un seul mot, jamais découpé (clé pliée).
        private static readonly HashSet<string> LexicalizedElisions =
            new HashSet<string>
        {
            "aujourd'hui", "presqu'ile", "presqu'iles",
            "prud'homme", "prud'hommes", "prud'homie", "prud'homal",
            "prud'homale", "prud'homaux", "prud'homales"
        };

        // Pronoms enclitiques (pliés) — la grammaire fermée de la queue
        // « (-t)?[-'](pronom) » ancrée en FIN de token : dit-il, va-t-il,
        // va-t'en, rends-toi, est-ce, prends-la, celui-là, penses-y.
        // L'ancrage final protège les composés (arc-en-ciel finit par
        // « -ciel », le « -en- » intérieur n'est jamais consulté).
        private static readonly HashSet<string> Enclitics = new HashSet<string>
        {
            "il", "elle", "on", "je", "tu", "nous", "vous", "ils", "elles",
            "ce", "moi", "toi", "lui", "leur", "y", "en",
            "le", "la", "les", "ci"
            // « là » plié donne « la », déjà couvert.
        };

        /// <summary>LA normalisation de comparaison (0.3) : NFC, minuscules,
        /// diacritiques pliés, œ→oe æ→ae, apostrophe typographique → droite.
        /// Les clés d'ignorés, les mots-outils et la répétition parlent tous
        /// cette langue-là.</summary>
        public static string Fold(string word)
        {
            if (string.IsNullOrEmpty(word)) return "";
            var lowered = word.ToLowerInvariant()
                .Replace("œ", "oe").Replace("æ", "ae").Replace("’", "'");
            var decomposed = lowered.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(c)
                    != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Découpe un texte plat (U+FFFC des éléments = jamais une
        /// lettre, il coupe). Les offsets renvoyés pointent dans le texte
        /// D'ORIGINE, même décomposé (NFD) : seules les chaînes CoreSurface/
        /// Folded sont normalisées NFC — décision documentée : on normalise
        /// à la sortie, jamais le texte source.</summary>
        public static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            if (string.IsNullOrEmpty(text)) return tokens;
            var i = 0;
            while (i < text.Length)
            {
                if (!IsWordChar(text[i])) { i++; continue; }
                var start = i;
                while (i < text.Length && IsWordChar(text[i])) i++;
                // Jointeur intérieur : il prolonge le token seulement entre
                // deux caractères de mot (jamais en bordure).
                while (i < text.Length - 1 && IsJoiner(text[i])
                    && IsWordChar(text[i + 1]))
                {
                    i++;
                    while (i < text.Length && IsWordChar(text[i])) i++;
                }
                tokens.Add(Analyze(text, start, i - start));
            }
            return tokens;
        }

        private static Token Analyze(string text, int start, int length)
        {
            var token = new Token
            {
                Start = start,
                Length = length,
                Surface = text.Substring(start, length)
            };

            // Le pliage AVEC carte de correspondance : chaque caractère plié
            // sait de quel caractère d'origine il vient — les découpes se
            // calculent sur le plié et se reportent sur l'origine, y compris
            // sur un texte NFD (marques combinantes sans poids).
            var folded = new StringBuilder(length);
            var map = new List<int>(length + 1);
            for (var k = 0; k < token.Surface.Length; k++)
            {
                var piece = Fold(token.Surface[k].ToString());
                foreach (var c in piece)
                {
                    folded.Append(c);
                    map.Add(k);
                }
            }
            map.Add(token.Surface.Length); // sentinelle de fin
            var flat = folded.ToString();

            var coreFrom = 0;            // bornes du cœur, en indices PLIÉS
            var coreTo = flat.Length;

            // 1. Élision en tête — sauf forme lexicalisée (aujourd'hui).
            var lexical = LexicalizedElisions.Contains(TrimEncliticForLexical(flat));
            if (!lexical)
                foreach (var prefix in Elisions)
                    if (flat.Length > prefix.Length
                        && flat.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        coreFrom = prefix.Length;
                        break;
                    }

            // 2. Grappe enclitique en FIN de token : « -pronom », précédée au
            // besoin du « -t » euphonique (« -t- » ou « -t' »).
            var lastCut = LastEncliticCut(flat, coreFrom);
            if (lastCut > coreFrom) coreTo = lastCut;

            // Report des bornes pliées sur le texte d'origine.
            var coreStartLocal = map[coreFrom];
            var coreEndLocal = coreTo < flat.Length ? map[coreTo] : token.Surface.Length;
            token.CoreStart = start + coreStartLocal;
            token.CoreLength = coreEndLocal - coreStartLocal;
            token.ElisionPrefix = token.Surface.Substring(0, coreStartLocal);
            token.Enclitic = token.Surface.Substring(coreEndLocal);
            token.CoreSurface = token.Surface
                .Substring(coreStartLocal, token.CoreLength)
                .Normalize(NormalizationForm.FormC);
            token.Folded = flat.Substring(coreFrom, coreTo - coreFrom);

            // 3. Nature : un chiffre quelque part, un numérique « lettré »,
            // ou un romain (≥ 2 lettres IVXLCDM + finale ordinale) → Number.
            token.Kind = ClassifyKind(token.CoreSurface) ? TokenKind.Number : TokenKind.Word;

            // 4. Casse du cœur (sur ses lettres).
            token.Shape = ShapeOf(token.CoreSurface);

            // 5. Segments d'un composé (le cœur seul — les enclitiques et
            // l'élision sont déjà partis).
            token.CoreParts = token.CoreSurface.IndexOf('-') >= 0
                ? token.CoreSurface.Split('-')
                : new[] { token.CoreSurface };
            return token;
        }

        /// <summary>Pour la détection des lexicalisées, la grappe enclitique
        /// éventuelle est d'abord ôtée (« aujourd'hui-même » n'existe pas,
        /// mais la règle reste totale).</summary>
        private static string TrimEncliticForLexical(string flat)
        {
            var cut = LastEncliticCut(flat, 0);
            return cut > 0 && cut < flat.Length ? flat.Substring(0, cut) : flat;
        }

        /// <summary>L'indice plié où commence la grappe enclitique finale,
        /// ou -1. Gère l'empilement « -t » euphonique : dit-il → 3,
        /// va-t-il → 2, va-t'en → 2.</summary>
        private static int LastEncliticCut(string flat, int coreFrom)
        {
            var hyphen = flat.LastIndexOf('-');
            if (hyphen <= coreFrom) return -1;
            var tail = flat.Substring(hyphen + 1);
            if (tail.Length == 0) return -1;
            // « -t'en » : l'apostrophe après le t euphonique.
            var apostrophe = tail.IndexOf('\'');
            if (apostrophe == 1 && tail[0] == 't'
                && Enclitics.Contains(tail.Substring(2)))
                return hyphen;
            if (!Enclitics.Contains(tail)) return -1;
            // Le « -t- » euphonique appartient à la grappe (va-t-il).
            if (hyphen >= 2 && flat[hyphen - 1] == 't' && flat[hyphen - 2] == '-'
                && hyphen - 2 > coreFrom)
                return hyphen - 2;
            return hyphen;
        }

        private static bool ClassifyKind(string core)
        {
            foreach (var c in core)
                if (char.IsDigit(c) || (char.IsNumber(c) && !char.IsLetter(c)))
                    return true; // 1914, 4x4, n°3, 10h30, 1er, Ⅴ (U+2164, Nl)

            // Romain : une tête IVXLCDM (≥ 2 — « Ce » ou « Il » ne sont pas
            // des nombres) suivie d'une finale ordinale vide ou e/er/es/ème(s).
            var romanLength = 0;
            while (romanLength < core.Length
                && "IVXLCDM".IndexOf(core[romanLength]) >= 0)
                romanLength++;
            if (romanLength >= 2)
            {
                var suffix = Fold(core.Substring(romanLength));
                if (suffix == "" || suffix == "e" || suffix == "er"
                    || suffix == "es" || suffix == "eme" || suffix == "emes")
                    return true; // XIX, XIXe, IIIes
            }
            return false;
        }

        private static CaseShape ShapeOf(string core)
        {
            var letters = 0;
            var upper = 0;
            var firstIsUpper = false;
            foreach (var c in core)
            {
                if (!char.IsLetter(c)) continue;
                if (letters == 0) firstIsUpper = char.IsUpper(c);
                letters++;
                if (char.IsUpper(c)) upper++;
            }
            if (letters == 0) return CaseShape.Lower;
            if (upper == 0) return CaseShape.Lower;
            if (upper == letters && letters > 1) return CaseShape.AllCaps;
            if (upper == 1 && firstIsUpper) return CaseShape.Capitalized;
            if (upper == letters) return CaseShape.AllCaps; // 1 lettre haute
            return CaseShape.Mixed;
        }
    }
}

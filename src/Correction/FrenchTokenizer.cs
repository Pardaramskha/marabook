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
        /// <summary>Offsets des segments dans le texte D'ORIGINE (batch 29,
        /// 0.2) : CoreParts est en NFC mais le texte source peut être en NFD
        /// (« après » = 6 unités NFC, 7 en NFD) — additionner des longueurs
        /// NFC posait l'ondulé à côté du mot. Parallèles à CoreParts.</summary>
        public int[] CorePartStarts = EmptyInts;
        public int[] CorePartLengths = EmptyInts;

        private static readonly string[] Empty = new string[0];
        private static readonly int[] EmptyInts = new int[0];
        public int CoreEnd { get { return CoreStart + CoreLength; } }
    }

    /// <summary>LA définition du mot français — batch 27, lot A. Il en
    /// existait trois divergentes (TextStats, RepetitionChecker,
    /// PivotSearch) ; tous les consommateurs passent désormais par ici, et
    /// le correcteur orthographique s'y alimente. Chaque décision est
    /// commentée sur place et testée en C7. Aucune dépendance WPF.</summary>
    public static class FrenchTokenizer
    {
        /// <summary>Le critère composé lexical / grappe enclitique (batch 29,
        /// 0.1) : si le token ENTIER est un mot connu, on ne découpe pas —
        /// « rendez-vous » reste un nom là où « dit-il » perd son pronom.
        /// Branché par l'application sur la MÊME connaissance que
        /// l'orthographe (moteur Hunspell + mots appris projet ET globaux —
        /// amendement A1 : un toponyme enseigné « Vaux-le-Vicomte » ne doit
        /// pas se faire manger son « -le »). Reçoit une surface NFC, casse
        /// d'origine. Null (dictionnaire absent, tests du tokeniseur seul) :
        /// repli sur la liste fermée ProtectedCompounds.</summary>
        public static Func<string, bool> KnownWord;

        // Le repli quand KnownWord est nul : les composés usuels qui
        // FINISSENT par un pronom de la grappe enclitique (clés pliées).
        // Liste fermée, forcément incomplète — le mode nominal passe par le
        // dictionnaire ; celui-ci évite que le mode dégradé soit faux sur
        // les cas les plus courants.
        private static readonly HashSet<string> ProtectedCompounds =
            new HashSet<string>
        {
            "rendez-vous", "par-ci", "par-la", "va-et-vient",
            "celui-ci", "celui-la", "celle-ci", "celle-la",
            "ceux-ci", "ceux-la", "celles-ci", "celles-la"
        };

        // Jointeurs INTÉRIEURS d'un token (entre deux caractères de mot) :
        // apostrophes droite et typographique, trait d'union (le vrai U+2010,
        // l'ASCII et l'insécable U+2011), degré (n°3). Les tirets
        // demi/cadratin (– —) sont TOUJOURS des séparateurs : ce sont les
        // tirets de dialogue.
        private static bool IsJoiner(char c)
        {
            return c == '\'' || c == '’' || c == '-' || c == '‐' || c == '‑'
                || c == '°';
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

        // Élisions LEXICALISÉES : un seul mot, jamais découpé (clé pliée) —
        // ni l'élision ni la grappe enclitique n'y sont retirées
        // (qu'en-dira-t-on finit par « -on » et commence par « qu' »…).
        private static readonly HashSet<string> LexicalizedElisions =
            new HashSet<string>
        {
            "aujourd'hui", "presqu'ile", "presqu'iles",
            "prud'homme", "prud'hommes", "prud'homie", "prud'homal",
            "prud'homale", "prud'homaux", "prud'homales",
            "c'est-a-dire", "qu'en-dira-t-on", "m'as-tu-vu"
        };

        // Pronoms enclitiques (pliés) — la grammaire fermée de la queue
        // « (-t)?[-'](pronom) » ancrée en FIN de token : dit-il, va-t-il,
        // va-t'en, rends-toi, est-ce, prends-la, penses-y. L'ancrage final
        // protège les composés intérieurs (arc-en-ciel finit par « -ciel »,
        // le « -en- » n'est jamais consulté) ; ceux qui FINISSENT par un
        // pronom (rendez-vous, celui-là) sont protégés par le critère du
        // mot connu entier — batch 29, 0.1.
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
            return Fold(word, true);
        }

        /// <summary>LE pli du mot français — une seule définition, paramétrée
        /// (batch 37) : lowerCase = false plie accents, ligatures, apostrophes
        /// et tirets mais GARDE la casse (recherche « respecter la casse »
        /// insensible aux accents). Jamais une seconde fonction de pliage.</summary>
        public static string Fold(string word, bool lowerCase)
        {
            if (string.IsNullOrEmpty(word)) return "";
            // U+2010 (trait d'union) et U+2011 (insécable) se plient en «-» :
            // IsJoiner les accepte, la clé et les découpes doivent les voir
            // comme le tiret ordinaire (batch 29, 0.7 — « grand‑père » en
            // insécable restait un bloc jamais décomposé).
            var lowered = (lowerCase ? word.ToLowerInvariant() : word)
                .Replace("œ", "oe").Replace("æ", "ae")
                .Replace("Œ", "OE").Replace("Æ", "AE").Replace("’", "'")
                .Replace('‐', '-').Replace('‑', '-');
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

            // Forme lexicalisée (aujourd'hui, c'est-à-dire…) : le token EST
            // le cœur — ni élision ni enclitique retirés.
            var lexical = LexicalizedElisions.Contains(flat)
                || LexicalizedElisions.Contains(TrimEncliticForLexical(flat));
            if (!lexical)
            {
                // 1. Élision en tête.
                foreach (var prefix in Elisions)
                    if (flat.Length > prefix.Length
                        && flat.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        coreFrom = prefix.Length;
                        break;
                    }
                // 2. Grappe enclitique en FIN de token, EN BOUCLE (batch 29,
                // 0.1) : avant chaque coupe, le mot restant est présenté au
                // dictionnaire — connu ENTIER, c'est un composé lexical
                // (rendez-vous, par-ci), on ne découpe pas ; inconnu, la
                // grappe tombe et on recommence (donne-le-moi → donne-le →
                // donne). Chaque coupe raccourcit strictement le cœur, la
                // borne est une ceinture (amendement A3).
                for (var guard = 0; guard < 8; guard++)
                {
                    var cut = LastEncliticCut(flat, coreFrom, coreTo);
                    if (cut <= coreFrom) break;
                    if (IsKnownWhole(flat, token.Surface, map, coreFrom, coreTo))
                        break;
                    coreTo = cut;
                }
            }

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
            // l'élision sont déjà partis). Découpés sur le PLIÉ, où tous les
            // traits d'union valent « - » (U+2010/U+2011 compris), et
            // reportés en offsets D'ORIGINE via la carte — batch 29, 0.2 :
            // additionner des longueurs NFC décalait les segments d'un texte
            // NFD, l'ondulé se posait à côté du mot.
            var flatCore = flat.Substring(coreFrom, coreTo - coreFrom);
            if (flatCore.IndexOf('-') >= 0)
            {
                var parts = new List<string>();
                var partStarts = new List<int>();
                var partLengths = new List<int>();
                var segmentFrom = coreFrom;
                for (var k = coreFrom; k <= coreTo; k++)
                    if (k == coreTo || flat[k] == '-')
                    {
                        var origFrom = map[segmentFrom];
                        var origTo = map[k];
                        parts.Add(token.Surface
                            .Substring(origFrom, origTo - origFrom)
                            .Normalize(NormalizationForm.FormC));
                        partStarts.Add(start + origFrom);
                        partLengths.Add(origTo - origFrom);
                        segmentFrom = k + 1;
                    }
                token.CoreParts = parts.ToArray();
                token.CorePartStarts = partStarts.ToArray();
                token.CorePartLengths = partLengths.ToArray();
            }
            else
            {
                token.CoreParts = new[] { token.CoreSurface };
                token.CorePartStarts = new[] { token.CoreStart };
                token.CorePartLengths = new[] { token.CoreLength };
            }
            return token;
        }

        /// <summary>Le token courant (bornes pliées [from, to)) est-il un mot
        /// connu, présenté ENTIER ? Nominal : le prédicat KnownWord, sur la
        /// surface d'origine NFC (le dictionnaire est sensible aux accents —
        /// la clé pliée « par-la » n'y existe pas), traits d'union normalisés
        /// vers l'ASCII. Dégradé (prédicat nul) : la liste fermée, sur la
        /// clé pliée.</summary>
        private static bool IsKnownWhole(string flat, string surface,
            List<int> map, int from, int to)
        {
            var known = KnownWord;
            if (known == null)
                return ProtectedCompounds.Contains(flat.Substring(from, to - from));
            var candidate = surface.Substring(map[from], map[to] - map[from])
                .Normalize(NormalizationForm.FormC)
                .Replace('‐', '-').Replace('‑', '-');
            return known(candidate);
        }

        /// <summary>Pour la détection des lexicalisées, la grappe enclitique
        /// éventuelle est d'abord ôtée (« aujourd'hui-même » n'existe pas,
        /// mais la règle reste totale).</summary>
        private static string TrimEncliticForLexical(string flat)
        {
            var cut = LastEncliticCut(flat, 0, flat.Length);
            return cut > 0 && cut < flat.Length ? flat.Substring(0, cut) : flat;
        }

        /// <summary>L'indice plié où commence la grappe enclitique finale de
        /// la fenêtre [coreFrom, coreTo), ou -1. Gère l'empilement « -t »
        /// euphonique : dit-il → 3, va-t-il → 2, va-t'en → 2. La borne haute
        /// permet à la boucle de 0.1 de dépouiller les grappes empilées
        /// (donne-le-moi) une par une.</summary>
        private static int LastEncliticCut(string flat, int coreFrom, int coreTo)
        {
            if (coreTo <= coreFrom) return -1;
            var hyphen = flat.LastIndexOf('-', coreTo - 1);
            if (hyphen <= coreFrom) return -1;
            var tail = flat.Substring(hyphen + 1, coreTo - hyphen - 1);
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
            // des nombres) qui forme une graphie romaine VALIDE (batch 29,
            // 0.7 : « VILLE » → tête VILL + finale e passait pour un nombre
            // et n'était plus ni vérifié ni compté — idem CIVIL, MIDI, VIDE,
            // MIME), suivie d'une finale ordinale vide ou e/er/es/ème(s).
            var romanLength = 0;
            while (romanLength < core.Length
                && "IVXLCDM".IndexOf(core[romanLength]) >= 0)
                romanLength++;
            if (romanLength >= 2)
            {
                var suffix = Fold(core.Substring(romanLength));
                if ((suffix == "" || suffix == "e" || suffix == "er"
                    || suffix == "es" || suffix == "eme" || suffix == "emes")
                    && IsValidRoman(core.Substring(0, romanLength)))
                    return true; // XIX, XIXe, IIIes, MCMXIV
            }
            return false;
        }

        // Les mots français entièrement composés de chiffres romains et
        // pourtant VALIDES comme nombres (DIX = 509, MI = 1001, CI = 101) :
        // l'ambiguïté est tranchée en faveur du mot — un titre crié « DIX ! »
        // est un mot, un chapitre 509 en romains n'existe pas.
        private static readonly HashSet<string> RomanLookalikes =
            new HashSet<string> { "DIX", "MI", "CI" };

        /// <summary>La grammaire romaine stricte : ordre décroissant par
        /// étages (milliers, centaines, dizaines, unités), pas plus de trois
        /// répétitions, soustractions légales seulement (CM, CD, XC, XL, IX,
        /// IV). Équivaut à M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3}),
        /// à la main pour rester hors regex sur le chemin de chaque token.</summary>
        private static bool IsValidRoman(string head)
        {
            if (RomanLookalikes.Contains(head)) return false;
            var i = 0;
            var thousands = 0;
            while (i < head.Length && head[i] == 'M') { i++; thousands++; }
            if (thousands > 3) return false;
            i = TakeRomanGroup(head, i, 'C', 'D', 'M');
            if (i < 0) return false;
            i = TakeRomanGroup(head, i, 'X', 'L', 'C');
            if (i < 0) return false;
            i = TakeRomanGroup(head, i, 'I', 'V', 'X');
            return i == head.Length;
        }

        /// <summary>Un étage décimal romain — pour les centaines (unit=C,
        /// five=D, ten=M) : CM, CD, ou D optionnel suivi de C×0..3. Rend
        /// l'indice après l'étage, -1 si plus de trois unités.</summary>
        private static int TakeRomanGroup(string s, int i, char unit,
            char five, char ten)
        {
            if (i < s.Length - 1 && s[i] == unit
                && (s[i + 1] == ten || s[i + 1] == five))
                return i + 2; // la soustraction : CM/CD, XC/XL, IX/IV
            if (i < s.Length && s[i] == five) i++;
            var units = 0;
            while (i < s.Length && s[i] == unit) { i++; units++; }
            return units <= 3 ? i : -1;
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

using System.Collections.Generic;
using Marabook.Correction;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C7 — le tokeniseur unique (batch 27, lot A) : des phrases
    /// françaises réelles, les tokens attendus écrits à la main. C'est le
    /// test le plus rentable du batch — l'orthographe, les répétitions, les
    /// stats et « mot entier » s'appuient tous dessus. Ferme aussi les
    /// rattrapages 0.1 (enclitiques) et 0.3 (clé d'ignoré).</summary>
    public static class TokenizerTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C7 — tokeniseur français");
            // Les deux réalités qui expédient (batch 29, amendement A3) : le
            // mode dégradé (prédicat nul, liste fermée — ce que voit un
            // contributeur sans les fichiers dict/, et la plupart des tests
            // ci-dessous) et le mode nominal (prédicat branché). La suite
            // garantit l'état du prédicat à l'entrée ET à la sortie.
            FrenchTokenizer.KnownWord = null;
            try
            {
                Elisions(t);
                Enclitics(t);
                Composites(t);
                CompoundVersusEnclitic(t);
                CeluiLaReversal(t);
                NumbersAndRomans(t);
                CaseShapes(t);
                NfdAndElements(t);
                FoldIsTheKey(t);
                EncliticRepetitions(t);
                IgnoreKeyMatchesFold(t);
            }
            finally
            {
                FrenchTokenizer.KnownWord = null;
            }
        }

        private static List<Token> Tok(string text)
        {
            return FrenchTokenizer.Tokenize(text);
        }

        // ------------------------------------------------------------ élisions

        private static void Elisions(Harness t)
        {
            var tokens = Tok("L'homme d'abord, jusqu'à l'aube.");
            t.Equal(4, tokens.Count, "quatre mots élidés");
            t.Equal("homme", tokens[0].CoreSurface, "l'homme → cœur homme");
            t.Equal("L'", tokens[0].ElisionPrefix, "le préfixe survit en surface");
            t.Equal(2, tokens[0].CoreStart, "le cœur commence après l'apostrophe");
            t.Equal("abord", tokens[1].CoreSurface, "d'abord → abord");
            t.Equal("à", tokens[2].CoreSurface, "jusqu'à → à");
            t.Equal("jusqu'", tokens[2].ElisionPrefix, "le préfixe long est reconnu");
            t.Equal("aube", tokens[3].CoreSurface, "l'aube → aube");

            var typographic = Tok("L’homme");
            t.Equal("homme", typographic[0].CoreSurface,
                "l'apostrophe TYPOGRAPHIQUE découpe pareil");

            var lexical = Tok("Aujourd'hui, la presqu'île.");
            t.Equal("Aujourd'hui", lexical[0].CoreSurface,
                "aujourd'hui : lexicalisé, jamais découpé");
            t.Equal("", lexical[0].ElisionPrefix, "pas de préfixe fantôme");
            t.Equal("presqu'île", lexical[2].CoreSurface,
                "presqu'île : lexicalisé aussi");
        }

        // ---------------------------------------------------------- enclitiques

        private static void Enclitics(Harness t)
        {
            var simple = Tok("dit-il")[0];
            t.Equal("dit", simple.CoreSurface, "dit-il → cœur dit");
            t.Equal("-il", simple.Enclitic, "l'enclitique est détaché");
            t.Equal(0, simple.CoreStart, "le cœur au début");
            t.Equal(3, simple.CoreLength, "trois lettres");

            t.Equal("va", Tok("va-t-il")[0].CoreSurface,
                "va-t-il : le -t- euphonique appartient à la grappe");
            t.Equal("Va", Tok("Va-t'en !")[0].CoreSurface,
                "Va-t'en : l'apostrophe après le t euphonique (casse gardée)");
            t.Equal("rends", Tok("rends-toi")[0].CoreSurface, "rends-toi");
            t.Equal("est", Tok("est-ce")[0].CoreSurface, "est-ce");
            t.Equal("prends", Tok("prends-la")[0].CoreSurface,
                "prends-la : le pronom la est enclitique");
            t.Equal("penses", Tok("penses-y")[0].CoreSurface, "penses-y");
        }

        // ------------------------------- 0.1 : composé lexical vs enclitique

        /// <summary>Batch 29, 0.1 — le critère qui distingue le composé
        /// lexical de la grappe enclitique : le mot connu ENTIER ne se
        /// découpe pas. Testé dans les DEUX modes (amendement A3) : dégradé
        /// (liste fermée) et nominal (prédicat injecté — ici un petit jeu
        /// fermé, le tokeniseur reste testable seul).</summary>
        private static void CompoundVersusEnclitic(Harness t)
        {
            // Mode dégradé : la liste fermée protège les composés usuels.
            t.Equal("rendez-vous", Tok("rendez-vous")[0].CoreSurface,
                "rendez-vous : protégé même sans dictionnaire");
            t.Equal("par-ci", Tok("par-ci")[0].CoreSurface, "par-ci protégé");
            t.Equal("par-là", Tok("par-là")[0].CoreSurface, "par-là protégé");
            t.Equal("va-et-vient", Tok("va-et-vient")[0].CoreSurface,
                "va-et-vient protégé");
            t.Equal("donne", Tok("donne-le-moi")[0].CoreSurface,
                "donne-le-moi : les grappes empilées tombent une à une");
            t.Equal("-le-moi", Tok("donne-le-moi")[0].Enclitic,
                "la grappe complète survit en surface");

            // Mode nominal : le prédicat fait foi — y compris pour un
            // composé APPRIS (amendement A1 : Vaux-le-Vicomte enseigné ne
            // perd pas son -le), et le verbe reste découpé.
            var known = new HashSet<string>
            {
                "rendez-vous", "Vaux-le-Vicomte", "après-midi"
            };
            FrenchTokenizer.KnownWord = delegate(string word)
            {
                return known.Contains(word);
            };
            try
            {
                t.Equal("rendez-vous", Tok("Un rendez-vous manqué.")[1].CoreSurface,
                    "rendez-vous : connu entier, jamais découpé");
                t.Equal("Vaux-le-Vicomte", Tok("Vaux-le-Vicomte")[0].CoreSurface,
                    "un toponyme APPRIS garde son -le (A1)");
                t.Equal("dit", Tok("dit-il")[0].CoreSurface,
                    "dit-il : inconnu entier, la grappe tombe toujours");
                t.Equal("donne", Tok("donne-le-moi")[0].CoreSurface,
                    "donne-le-moi : aucun étage n'est connu, tout tombe");
                t.Equal("homme", Tok("cet homme-là")[1].CoreSurface,
                    "homme-là : le -là démonstratif tombe (inconnu entier)");
            }
            finally
            {
                FrenchTokenizer.KnownWord = null;
            }
        }

        /// <summary>Batch 29, amendement A4 — LE RENVERSEMENT DE CELUI-LÀ,
        /// documenté : jusqu'au batch 28, « celui-là » perdait son « -là »
        /// (cœur « celui », l'exemple vivait dans le commentaire des
        /// enclitiques). Le critère du dictionnaire (0.1) l'a renversé :
        /// « celui-là » est une entrée du .dic — un pronom lexicalisé, de la
        /// même famille que « par-là » — donc il reste ENTIER, dans les deux
        /// modes (il figure aussi dans la liste fermée du repli, pour que la
        /// clé de répétition ne dépende pas du mode). Si ce test rougit, ce
        /// n'est pas une régression du découpage : c'est la protection des
        /// composés démonstratifs qui a sauté.</summary>
        private static void CeluiLaReversal(Harness t)
        {
            t.Equal("celui-là", Tok("celui-là")[0].CoreSurface,
                "celui-là reste entier (renversement 0.1, ex-attendu « celui »)");
            t.Equal("celle-ci", Tok("celle-ci")[0].CoreSurface,
                "celle-ci : même famille, même protection");
        }

        // ------------------------------------------------------------ composés

        private static void Composites(Harness t)
        {
            var tokens = Tok("Le grand-père range son porte-monnaie sous l'arc-en-ciel.");
            t.Equal("grand-père", tokens[1].CoreSurface,
                "grand-père : UN token (pas d'enclitique en queue)");
            t.Equal(2, tokens[1].CoreParts.Length, "deux segments pour l'orthographe");
            t.Equal("père", tokens[1].CoreParts[1], "le second segment");
            t.Equal("porte-monnaie", tokens[4].CoreSurface, "porte-monnaie entier");
            t.Equal("arc-en-ciel", tokens[6].CoreSurface,
                "arc-en-ciel : le -en- INTÉRIEUR n'est jamais consulté");
            t.Equal(3, tokens[6].CoreParts.Length, "trois segments");

            // 0.2 (batch 29) : les offsets des segments sont en unités du
            // texte D'ORIGINE. Sur « après-midi » DÉCOMPOSÉ (è = e + accent
            // combinant, 11 unités au lieu de 10), l'ancienne addition de
            // longueurs NFC posait « midi » une unité trop tôt.
            var nfd = Tok("apre" + (char) 0x0300 + "s-midi")[0];
            t.Equal("après-midi", nfd.CoreSurface, "cœur NFC");
            t.Equal(2, nfd.CoreParts.Length, "deux segments");
            t.Equal("après", nfd.CoreParts[0], "segment 1 en NFC");
            t.Equal(0, nfd.CorePartStarts[0], "segment 1 à l'origine");
            t.Equal(6, nfd.CorePartLengths[0],
                "« après » couvre 6 unités D'ORIGINE (NFD)");
            t.Equal("midi", nfd.CoreParts[1], "segment 2");
            t.Equal(7, nfd.CorePartStarts[1],
                "« midi » commence à 7 (après l'accent combinant ET le tiret)");
            t.Equal(4, nfd.CorePartLengths[1], "quatre unités");

            // 0.7 (batch 29) : le trait d'union INSÉCABLE (U+2011) se plie
            // en « - » — « grand‑père » n'est plus un bloc indécomposable.
            var nonBreaking = Tok("grand" + (char) 0x2011 + "père")[0];
            t.Equal(2, nonBreaking.CoreParts.Length,
                "U+2011 découpe comme le tiret ordinaire");
            t.Equal("père", nonBreaking.CoreParts[1], "second segment");
            t.Equal(6, nonBreaking.CorePartStarts[1],
                "offset d'origine du second segment");
        }

        // ---------------------------------------------------- nombres et romains

        private static void NumbersAndRomans(Harness t)
        {
            var tokens = Tok("En 1914, au XIXe, un 4x4 n°3 part à 10h30, le 1er.");
            var numbers = 0;
            foreach (var token in tokens)
                if (token.Kind == TokenKind.Number) numbers++;
            t.Equal(6, numbers, "1914, XIXe, 4x4, n°3, 10h30, 1er : six nombres");
            t.Equal(TokenKind.Number, Tok("n°3")[0].Kind, "n°3 : un seul token");
            t.Equal(TokenKind.Word, Tok("Ce")[0].Kind,
                "« Ce » n'est pas un romain (tête < 2)");
            t.Equal(TokenKind.Word, Tok("Il")[0].Kind, "« Il » non plus");
            t.Equal(TokenKind.Number, Tok("XIX")[0].Kind, "XIX sans finale");

            // 0.7 (batch 29) : la grammaire romaine est VALIDÉE — les mots
            // en capitales dont la tête est faite de IVXLCDM ne sont plus
            // des « romains fantômes » soustraits à la vérification.
            t.Equal(TokenKind.Word, Tok("VILLE")[0].Kind,
                "VILLE n'est pas un nombre (VILL n'est pas un romain)");
            t.Equal(TokenKind.Word, Tok("CIVIL")[0].Kind, "CIVIL non plus");
            t.Equal(TokenKind.Word, Tok("MIDI")[0].Kind, "MIDI non plus");
            t.Equal(TokenKind.Word, Tok("VIDE")[0].Kind, "VIDE non plus");
            t.Equal(TokenKind.Word, Tok("MIME")[0].Kind, "MIME non plus");
            t.Equal(TokenKind.Word, Tok("DIX")[0].Kind,
                "DIX (romain VALIDE, 509) : l'ambiguïté est tranchée mot");
            t.Equal(TokenKind.Number, Tok("MCMXIV")[0].Kind,
                "MCMXIV : soustractions légales, romain accepté");
            t.Equal(TokenKind.Number, Tok("IIIes")[0].Kind,
                "IIIes : finale ordinale plurielle");
            t.Equal(TokenKind.Word, Tok("XXXX")[0].Kind,
                "XXXX : plus de trois répétitions, invalide");
        }

        // ---------------------------------------------------------------- casse

        private static void CaseShapes(Harness t)
        {
            t.Equal(CaseShape.AllCaps, Tok("SNCF")[0].Shape, "sigle tout-capitales");
            t.Equal(CaseShape.Capitalized, Tok("Marabout")[0].Shape, "initiale");
            t.Equal(CaseShape.Lower, Tok("marabout")[0].Shape, "minuscules");
            t.Equal(CaseShape.Mixed, Tok("McDo")[0].Shape, "casse mêlée");
            t.Equal(CaseShape.Capitalized, Tok("L'Homme")[0].Shape,
                "la casse se lit sur le CŒUR (Homme), pas sur l'élision");
        }

        // ------------------------------------------------------ NFD et éléments

        private static void NfdAndElements(Harness t)
        {
            // « café » décomposé : e + accent combinant (import ODT/macOS).
            var decomposed = "café froid";
            var tokens = Tok(decomposed);
            t.Equal("café", tokens[0].CoreSurface,
                "le cœur sort en NFC quel que soit l'encodage d'entrée");
            t.Equal("cafe", tokens[0].Folded, "la clé pliée est stable");
            t.Equal(5, tokens[0].Length,
                "les offsets restent ceux du texte D'ORIGINE (5 unités NFD)");

            var cut = Tok("mara￼bout");
            t.Equal(2, cut.Count, "U+FFFC coupe : jamais une lettre");
            t.Equal("mara", cut[0].CoreSurface, "avant l'élément");
            t.Equal("bout", cut[1].CoreSurface, "après l'élément");

            var dash = Tok("— Viens, dit-elle — vite.");
            t.Equal("Viens", dash[0].CoreSurface,
                "le tiret cadratin est un séparateur, jamais un jointeur");
        }

        private static void FoldIsTheKey(Harness t)
        {
            t.Equal("coeur", FrenchTokenizer.Fold("Cœur"), "œ plié");
            t.Equal("ete", FrenchTokenizer.Fold("ÉTÉ"), "capitales accentuées pliées");
            t.Equal("l'homme", FrenchTokenizer.Fold("L’homme"),
                "l'apostrophe typographique se plie en droite");
        }

        // ---------------------------------------------------- rattrapages 0.1/0.3

        private static TextDocument Document(params string[] paragraphs)
        {
            var document = new TextDocument();
            foreach (var text in paragraphs)
            {
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun { Text = text });
                document.Paragraphs.Add(paragraph);
            }
            return document;
        }

        /// <summary>0.1 — « dit-il » ne discrédite plus le détecteur : le
        /// cœur « dit » est mot-outil (exception documentée), l'incise se
        /// tait ; « répondit-elle » répété, lui, est signalé SUR LE VERBE.</summary>
        private static void EncliticRepetitions(Harness t)
        {
            var host = new CheckerHost();
            host.Add(new RepetitionChecker());
            var dialogue = Document(
                "« Viens », dit-il. « Pourquoi ? » dit-elle. « Viens donc », dit-il.");
            var noise = 0;
            foreach (var finding in host.Run(dialogue, null))
                if (finding.Word == "dit") noise++;
            t.Equal(0, noise, "les incises dit-il/dit-elle se taisent");

            var host2 = new CheckerHost();
            host2.Add(new RepetitionChecker());
            var tic = Document("« Non », répondit-elle. « Jamais », répondit-elle.");
            var findings = host2.Run(tic, null);
            t.Equal(1, findings.Count, "répondit-elle répété : signalé");
            t.Equal("répondit", findings[0].Word,
                "sur le VERBE, pas sur le pronom");
            var text = PivotEdit.FlatText(tic.Paragraphs[0]);
            t.Equal("répondit", text.Substring(findings[0].Start, findings[0].Length),
                "l'ondulé couvre exactement le cœur");
        }

        /// <summary>0.3 — la clé d'ignoré EST la clé de comparaison :
        /// ignorer « COEUR » tait « cœur », ignorer « homme » tait
        /// « L'homme » (le Word du signalement porte le cœur).</summary>
        private static void IgnoreKeyMatchesFold(Harness t)
        {
            var host = new CheckerHost();
            host.Add(new RepetitionChecker());
            var accents = Document("Le cœur bat.", "Ce cœur ment.");
            t.Equal(1, host.Run(accents, null).Count, "répétition présente");
            host.IgnoreInProject("COEUR");
            t.Equal(0, host.Run(accents, null).Count,
                "ignorer COEUR tait cœur (casse ET accents pliés)");

            var host2 = new CheckerHost();
            host2.Add(new RepetitionChecker());
            var elision = Document("L'homme marche.", "Un homme parle.");
            t.Equal(1, host2.Run(elision, null).Count, "répétition présente");
            host2.IgnoreInProject("homme");
            t.Equal(0, host2.Run(elision, null).Count,
                "ignorer homme tait L'homme (le Word porte le cœur)");
        }
    }
}

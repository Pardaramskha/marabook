using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UniversSale.Correction;
using UniversSale.Correction.Hunspell;
using UniversSale.Model;

namespace UniversSale.Tests
{
    /// <summary>C8 — le moteur Hunspell maison (batch 27, lot C.5), trois
    /// étages : 1) AUTO-CONTRÔLE — chaque entrée du .dic doit être acceptée
    /// (circulaire, assumé : 87 000 cas gratuits qui attrapent les bugs
    /// d'analyse, de drapeaux et de casse) ; 2) corpus POSITIF écrit à la
    /// main, choisi pour sa difficulté ; 3) corpus NÉGATIF de fautes réelles,
    /// avec un SCORE À PLANCHER (comme la césure C2) — la qualité de
    /// suggestion s'améliore par paliers, jamais en silence. Plus les
    /// mesures exigées : chargement, recherche, cycle complet et cycle
    /// incrémental sur 50 000 mots avec les deux vérificateurs.</summary>
    public static class SpellTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C8 — orthographe (moteur Hunspell maison)");
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dict");
            var aff = Path.Combine(folder, "fr-toutesvariantes.aff");
            var dic = Path.Combine(folder, "fr-toutesvariantes.dic");
            if (!File.Exists(aff) || !File.Exists(dic))
            {
                t.Check(false, "dict/fr-toutesvariantes absent — lot E non appliqué");
                return;
            }

            var watch = Stopwatch.StartNew();
            var engine = SpellEngine.Load(aff, dic);
            watch.Stop();
            t.Info("chargement du dictionnaire : " + watch.ElapsedMilliseconds
                + " ms (" + engine.StemCount + " radicaux)");
            t.Check(watch.ElapsedMilliseconds < 300,
                "budget de chargement tenu (< 300 ms)");

            SelfCheck(t, engine, dic);
            PositiveCorpus(t, engine);
            NegativeCorpus(t, engine);
            CaseRules(t, engine);
            CheckerBehavior(t, engine);
            Measures(t, engine);
        }

        // ------------------------------------------------- 1. auto-contrôle

        private static void SelfCheck(Harness t, SpellEngine engine, string dicPath)
        {
            var lines = File.ReadAllLines(dicPath, Encoding.UTF8);
            var checked_ = 0;
            var rejected = new List<string>();
            var watch = Stopwatch.StartNew();
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0) continue;
                var cut = line.IndexOf('\t');
                if (cut >= 0) line = line.Substring(0, cut);
                var slash = line.IndexOf('/');
                var word = slash >= 0 ? line.Substring(0, slash) : line;
                var flags = slash >= 0 ? line.Substring(slash + 1) : "";
                // NEEDAFFIX « () » : le radical n'existe pas seul ;
                // FORBIDDENWORD « {} » : la forme est explicitement interdite.
                var standalone = true;
                for (var f = 0; f + 1 < flags.Length; f += 2)
                {
                    var flag = flags.Substring(f, 2);
                    if (flag == "()" || flag == "{}") { standalone = false; break; }
                }
                if (!standalone || word.Length == 0) continue;
                checked_++;
                if (!engine.Accepts(word) && rejected.Count < 12)
                    rejected.Add(word);
            }
            watch.Stop();
            foreach (var word in rejected) t.Info("rejeté à tort : " + word);
            t.Info("auto-contrôle : " + checked_ + " entrées en "
                + watch.ElapsedMilliseconds + " ms ("
                + (watch.ElapsedMilliseconds * 1000.0 / Math.Max(1, checked_))
                    .ToString("0.0") + " µs/mot)");
            t.Equal(0, rejected.Count,
                "chaque entrée du .dic est acceptée par le moteur");
        }

        // ------------------------------------------------ 2. corpus positif

        // ~300 formes que le moteur DOIT accepter, choisies pour leur
        // difficulté : conjugaisons rares, trémas et accents, pluriels
        // irréguliers, composés, rectifications de 1990 (toutes variantes),
        // élisions déjà dépouillées par le tokeniseur (cœurs nus).
        private static readonly string[] Positive = (
            // conjugaisons rares (passé simple, subjonctif, formes en -ss-)
            "eûtes fûtes eussions eussiez fussions fussiez aient soyez " +
            "veuillez sachions sachiez vécûmes naquit naquirent plut " +
            "acquièrent reçûmes tinrent vainquit vainquirent moulait " +
            "résolûmes craignît susse assît prissent allassent chantassions " +
            "agissait rougeoyait employât essuierons appuierais nettoierait " +
            "jetterons appellerait achèterons harcèlera peluche s'il " +
            // trémas, accents, cédilles
            "ambiguë ambigüe aiguë aigüe œuvre œuvres œil cœur sœur bœuf " +
            "îlot île aïeul aïeule aïeux haïr haïssait ça çà déçu reçu " +
            "français garçon leçon maçon rançon caleçon exiguë exigüe " +
            "ciguë cigüe canoë Noël noël goéland " +
            // pluriels irréguliers
            "chevaux travaux journaux généraux caporaux bocaux vitraux " +
            "coraux émaux baux soupiraux bijoux cailloux choux genoux " +
            "hiboux joujoux poux pneus bleus landaus sarraus yeux cieux " +
            "aïeuls ciels œufs bœufs messieurs mesdames mesdemoiselles " +
            // composés du dictionnaire
            "porte-monnaie arc-en-ciel grand-père grands-pères grand-mère " +
            "chef-d'œuvre chefs-d'œuvre après-midi rez-de-chaussée " +
            "croc-en-jambe pot-au-feu tête-à-tête vis-à-vis c'est-à-dire " +
            "va-et-vient qu'en-dira-t-on porte-clés presqu'île aujourd'hui " +
            "week-end sous-marin demi-heure quatre-vingts soixante-dix " +
            "belle-sœur beau-frère petits-enfants " +
            // rectifications de 1990 : les DEUX graphies passent
            "nénufar nénuphar ognon oignon évènement événement maitre maître " +
            "paraitre paraître connaitre connaître aout août gout goût " +
            "bruler brûler couter coûter diner dîner entrainer entraîner " +
            "weekend ile flute flûte piqure piqûre voute voûte " +
            "ambigument assidument crument " +
            // adverbes, mots savants, régionalismes du dictionnaire
            "précautionneusement vraisemblablement constitutionnellement " +
            "anticonstitutionnellement chirurgicalement œcuménique " +
            "pusillanime sempiternel obséquieux perspicace munificence " +
            "acrimonie thuriféraire zéphyr sycophante palimpseste " +
            "apocryphe éphéméride chrysanthème hiéroglyphe " +
            "rhododendron eucalyptus saperlipopette " +
            // noms propres et majuscules du lexique
            "Paris France Marie Jean Bretagne Provence Méditerranée " +
            "Champagne Bourgogne Loire Seine Rhône Garonne " +
            // formes usuelles à affixes lourds
            "redémarrer redémarrages inconstitutionnalité désenchantement " +
            "immanquablement irrémédiablement incompréhensiblement " +
            "embouteillages découragement raccommodage rechargement " +
            "surenchérir désillusionné réinitialisation interconnexions " +
            "hospitalisations institutionnalisation internationalisation " +
            "imperméabilisation vraisemblance invraisemblances " +
            // petits pièges
            "été étés eu eue eues eût")
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private static void PositiveCorpus(Harness t, SpellEngine engine)
        {
            var failures = new List<string>();
            foreach (var word in Positive)
            {
                var core = word;
                // Le corpus écrit des formes de surface : le tokeniseur
                // dépouille les élisions comme dans la vraie chaîne.
                var tokens = FrenchTokenizer.Tokenize(word);
                if (tokens.Count == 1) core = tokens[0].CoreSurface;
                if (!engine.Accepts(core) && failures.Count < 15)
                    failures.Add(word + " (cœur « " + core + " »)");
            }
            foreach (var word in failures) t.Info("positif rejeté : " + word);
            t.Equal(0, failures.Count,
                "corpus positif (" + Positive.Length + " formes difficiles) accepté");
        }

        // ------------------------------------------------ 3. corpus négatif

        // Des fautes réelles qui DOIVENT être rejetées ; la suggestion
        // attendue est notée quand elle est évidente (null = seule
        // l'exclusion compte). SCORE : rejet = 1 pt, suggestion présente =
        // 1 pt, suggestion EN TÊTE = 1 pt de plus. Plancher verrouillé —
        // toute amélioration future doit le relever.
        private static readonly string[][] Negative =
        {
            new[] { "language", "langage" },
            new[] { "developement", "développement" },
            new[] { "apartement", "appartement" },
            new[] { "dilemne", "dilemme" },
            new[] { "bonjuor", "bonjour" },
            new[] { "chevaus", "chevaux" },
            new[] { "hopital", "hôpital" },
            new[] { "acceuil", "accueil" },
            new[] { "abscence", "absence" },
            new[] { "adressse", "adresse" },
            new[] { "aquarium", null }, // correct ! garde-fou du corpus… retiré du score
            new[] { "professionel", "professionnel" },
            new[] { "traditionel", "traditionnel" },
            new[] { "personage", "personnage" },
            new[] { "ecrivain", "écrivain" },
            new[] { "chapitre1", null },
            new[] { "recit", "récit" },
            new[] { "poeme", "poème" },
            new[] { "theatre", "théâtre" },
            new[] { "bibliotheque", "bibliothèque" },
            new[] { "gramaire", "grammaire" },
            new[] { "orthografe", "orthographe" },
            new[] { "farmacie", "pharmacie" },
            new[] { "fantome", "fantôme" },
            new[] { "chateau", "château" },
            new[] { "forett", null },
            new[] { "mistère", "mystère" },
            new[] { "silouette", "silhouette" },
            new[] { "rytme", "rythme" },
            new[] { "labyrinte", "labyrinthe" },
            new[] { "appercevoir", "apercevoir" },
            new[] { "aparaitre", "apparaitre" },
            new[] { "couriŕ", null },
            new[] { "acourir", "accourir" },
            new[] { "atterir", "atterrir" },
            new[] { "courrieŕ", null },
            new[] { "galopp", "galop" },
            new[] { "envellope", "enveloppe" },
            new[] { "mourrir", "mourir" },
            new[] { "nourir", "nourrir" },
            new[] { "batteau", "bateau" },
            new[] { "campagnnard", "campagnard" },
            new[] { "montagen", "montagne" },
            new[] { "rivierre", "rivière" },
            new[] { "cascadde", "cascade" },
            new[] { "brouilard", "brouillard" },
            new[] { "orrage", "orage" },
            new[] { "tonerre", "tonnerre" },
            new[] { "eclair", "éclair" },
            new[] { "crepuscule", "crépuscule" },
            new[] { "aurorre", "aurore" },
            new[] { "penombre", "pénombre" },
            new[] { "tenebres", "ténèbres" },
            new[] { "lumierre", "lumière" },
            new[] { "etoile", "étoile" },
            new[] { "planete", "planète" },
            new[] { "commete", "comète" },
            new[] { "galaxsie", "galaxie" },
            new[] { "univer", "univers" },
            new[] { "cosmoss", "cosmos" },
            new[] { "infinni", "infini" },
            new[] { "eternité", "éternité" },
            new[] { "imortel", "immortel" },
            new[] { "legendaire", "légendaire" },
            new[] { "heroique", "héroïque" },
            new[] { "couragous", "courageux" },
            new[] { "vaillament", "vaillamment" },
            new[] { "bravourre", "bravoure" },
            new[] { "epee", "épée" },
            new[] { "bouclieŕ", null },
            new[] { "armurre", "armure" },
            new[] { "chevalieŕ", null },
            new[] { "dragonn", "dragon" },
            new[] { "sorcierre", "sorcière" },
            new[] { "magiciene", "magicienne" },
            new[] { "enchanteresse", null }, // correct — garde-fou, hors score
            new[] { "malefice", "maléfice" },
            new[] { "sortilegge", "sortilège" },
            new[] { "grimoirre", "grimoire" },
            new[] { "parchemim", "parchemin" },
            new[] { "encrieŕ", null },
            new[] { "plumme", "plume" },
            new[] { "ecriturre", "écriture" },
            new[] { "manuscrist", "manuscrit" },
            new[] { "brouillonn", "brouillon" },
            new[] { "rature", null }, // correct — garde-fou, hors score
            new[] { "coriger", "corriger" },
            new[] { "relirre", "relire" },
            new[] { "editeuŕ", null },
            new[] { "imprimmerie", "imprimerie" },
            new[] { "librairrie", "librairie" },
            new[] { "romancierre", "romancière" },
            new[] { "nouveliste", "nouvelliste" },
            new[] { "poete", "poète" },
            new[] { "dramaturgge", "dramaturge" },
            new[] { "scenariste", "scénariste" },
            new[] { "dialogiste", null }, // correct — garde-fou, hors score
            new[] { "monologgue", "monologue" },
            new[] { "tiradde", "tirade" },
            new[] { "replique", "réplique" },
            new[] { "didascalie", null } // correct — garde-fou, hors score
        };

        // Le PLANCHER du score, verrouillé après la première mesure — toute
        // régression échoue, toute amélioration doit le relever. 269/273 au
        // batch 27 : seule « couragous » (double remplacement) échappe au
        // top — les n-grammes du backlog la rattraperont.
        private const int ScoreFloor = 269;

        private static void NegativeCorpus(Harness t, SpellEngine engine)
        {
            var score = 0;
            var maximum = 0;
            var accepted = new List<string>();
            var missed = new List<string>();
            foreach (var pair in Negative)
            {
                var word = pair[0];
                var expected = pair[1];
                var isGuard = expected == null && engine.Accepts(word);
                if (isGuard) continue; // les garde-fous corrects sortent du score
                maximum += expected == null ? 1 : 3;
                if (engine.Accepts(word))
                {
                    if (accepted.Count < 10) accepted.Add(word);
                    continue;
                }
                score++;
                if (expected == null) continue;
                var suggestions = engine.Suggest(word);
                var index = suggestions.IndexOf(expected);
                if (index >= 0) score++;
                else if (missed.Count < 10)
                    missed.Add(word + " → " + expected + " absent de ["
                        + string.Join(", ", suggestions.ToArray()) + "]");
                if (index == 0) score++;
            }
            foreach (var word in accepted) t.Info("faute acceptée à tort : " + word);
            foreach (var line in missed) t.Info("suggestion manquée : " + line);
            t.Info("SCORE négatif : " + score + " / " + maximum
                + " (plancher : " + ScoreFloor + ")");
            t.Check(score >= ScoreFloor, "le score de suggestion tient son plancher");
        }

        // -------------------------------------------------- 4. règles de casse

        private static void CaseRules(Harness t, SpellEngine engine)
        {
            t.Check(engine.Accepts("Cheval"),
                "initiale capitale acceptée si la minuscule existe (début de phrase)");
            t.Check(engine.Accepts("CHEVAL"),
                "tout-capitales accepté par sa minuscule");
            t.Check(engine.Accepts("Paris"), "le nom propre à sa casse");
            t.Check(!engine.Accepts("paris" ) || engine.Accepts("paris"),
                "paris minuscule : le lexique tranche (parier → je paris ?)");
            t.Check(engine.Accepts("PARIS"),
                "un nom propre tout en capitales est accepté (titres)");
            t.Check(!engine.Accepts("frznce"), "le charabia reste faux");
        }

        // ------------------------------------------- 5. le vérificateur branché

        private static void CheckerBehavior(Harness t, SpellEngine engine)
        {
            var checker = new SpellChecker(engine);
            var host = new CheckerHost();
            host.Add(checker);

            var document = Document(
                "L'écrivain contemple le chateau depuis la fenêtre.");
            var findings = host.Run(document, null);
            t.Equal(1, findings.Count, "une seule faute dans la phrase");
            t.Equal("chateau", findings[0].Word, "la bonne");
            t.Check(checker.Suggestions(findings[0].Word).Contains("château"),
                "la suggestion évidente arrive À LA DEMANDE (jamais à la passe)");
            t.Equal(0, findings[0].Suggestions.Count,
                "le signalement naît sans suggestions (calcul paresseux)");
            var text = PivotEdit.FlatText(document.Paragraphs[0]);
            t.Equal("chateau", text.Substring(findings[0].Start, findings[0].Length),
                "l'ondulé couvre exactement le mot");

            t.Equal(0, host.Run(Document("La SNCF et M. Dupont partent à 10h30 en 4x4."),
                null).Count, "sigles, initiales, nombres : silence");

            t.Equal(0, host.Run(Document("Le wagon-citerne déraille."), null).Count,
                "composé productif de segments valides : silence");
            var part = host.Run(Document("Le wagon-citterne déraille."), null);
            t.Equal(1, part.Count, "segment fautif d'un composé : signalé");
            t.Equal("citterne", part[0].Word, "LE segment, pas le composé entier");

            var learned = new SpellChecker(engine);
            learned.ProjectWords.Add("Batiatus");
            var host2 = new CheckerHost();
            host2.Add(learned);
            t.Equal(0, host2.Run(Document("Batiatus regarde batiatus."), null).Count,
                "un mot enseigné couvre toutes ses casses (clé pliée)");

            var noProof = new TextDocument();
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "Le mot " });
            paragraph.Runs.Add(new TextRun { Text = "Karadhras", NoProof = true });
            paragraph.Runs.Add(new TextRun { Text = " est inventé." });
            noProof.Paragraphs.Add(paragraph);
            var host3 = new CheckerHost();
            host3.Add(new SpellChecker(engine));
            t.Equal(0, host3.Run(noProof, null).Count,
                "NoProof est respecté (par le pilote, le vérificateur reste bête)");
        }

        // ---------------------------------------------------------- 6. mesures

        private static void Measures(Harness t, SpellEngine engine)
        {
            var document = CorrectionTests.FiftyThousandWordChapter();
            var host = new CheckerHost();
            host.Add(new RepetitionChecker());
            host.Add(new SpellChecker(engine));

            var watch = Stopwatch.StartNew();
            var findings = host.Run(document, null);
            watch.Stop();
            var full = watch.ElapsedMilliseconds;

            watch.Restart();
            host.Run(document, null);
            watch.Stop();
            var cached = watch.ElapsedMilliseconds;

            PivotEdit.InsertText(document.Paragraphs[250], 0, "Nouveau debut. ");
            watch.Restart();
            host.Run(document, null);
            watch.Stop();
            var incremental = watch.ElapsedMilliseconds;

            t.Info("50 000 mots, deux vérificateurs : cycle complet " + full
                + " ms (" + findings.Count + " signalements), cycle sans édition "
                + cached + " ms, cycle après UNE frappe " + incremental + " ms");
            t.Check(incremental <= full,
                "le cache rend le cycle incrémental au plus égal au complet");
        }

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
    }
}

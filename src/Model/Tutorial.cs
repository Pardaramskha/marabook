using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marabook.Model
{
    /// <summary>Le TUTORIEL (1.1.0, 29/10, Rémi) — le modèle pur : le script
    /// lu depuis le Markdown de Rémi (assets/tutoriel/Tutoriel.md), et les
    /// CONSIGNES de chaque étape (le texte entre parenthèses en tête de
    /// puce : « switch vers le tableau écrits », « highlight pile »…)
    /// résolues en actions nommées que la fenêtre exécute. Une consigne
    /// que le résolveur ne comprend pas n'est pas une erreur : l'étape se
    /// montre sans mouvement, et la consigne est listée (les lacunes).</summary>
    public sealed class TutorialScript
    {
        /// <summary>Les paragraphes d'accueil, avant le premier chapitre.</summary>
        public readonly List<string> Intro = new List<string>();
        /// <summary>La consigne de l'accueil, s'il en a une (« (tableau Accueil) » en tête).</summary>
        public string IntroInstruction = "";
        public TutorialCue IntroCue = new TutorialCue();
        public readonly List<TutorialChapter> Chapters = new List<TutorialChapter>();

        /// <summary>Les chapitres qui ont des étapes (les « à venir » sont tus).</summary>
        public List<TutorialChapter> Playable
        {
            get
            {
                var list = new List<TutorialChapter>();
                foreach (var chapter in Chapters) if (chapter.Steps.Count > 0) list.Add(chapter);
                return list;
            }
        }
    }

    public sealed class TutorialChapter
    {
        public int Number;
        public string Title = "";
        public readonly List<TutorialStep> Steps = new List<TutorialStep>();
        /// <summary>« (section à venir, à ne pas implémenter vide) ».</summary>
        public bool Placeholder;
    }

    public sealed class TutorialStep
    {
        public TutorialChapter Chapter;
        public string Instruction = ""; // entre parenthèses, en tête de puce ; vide = rien à faire
        public string Text = "";        // le Markdown de l'étape (gras, italique)
        /// <summary>Les sous-puces de l'étape (« Ctrl+K : Insérer un lien… ») :
        /// de petits blocs, le raccourci en gras, le texte en dessous.</summary>
        public readonly List<string> Items = new List<string>();
        public TutorialCue Cue;         // résolu une fois pour toutes
    }

    /// <summary>Ce qu'une consigne demande : des actions nommées, dans
    /// l'ordre (« nav:writings », « highlight:pile », « ribbon-tab:Texte »…),
    /// et la consigne telle quelle si rien n'a été compris.</summary>
    public sealed class TutorialCue
    {
        public readonly List<string> Actions = new List<string>();
        public string Unresolved; // la consigne incomprise, ou null

        public bool Has(string prefix)
        {
            foreach (var action in Actions) if (action.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        public void Add(string action)
        {
            if (!Actions.Contains(action)) Actions.Add(action);
        }
    }

    public static class TutorialParser
    {
        private static readonly Regex ChapterLine = new Regex(@"^(\d+)\.\s+(.+?)\s*$");
        private static readonly Regex StepLine = new Regex(@"^[\*\-]\s+(.*)$");
        private static readonly Regex SubStepLine = new Regex(@"^\s+[\*\-]\s+(.*)$");

        public static TutorialScript Parse(string markdown)
        {
            var script = new TutorialScript();
            if (string.IsNullOrEmpty(markdown)) return script;
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            TutorialChapter chapter = null;
            TutorialStep last = null;
            var paragraph = new StringBuilder();
            Action flush = delegate
            {
                var text = paragraph.ToString().Trim();
                if (text.Length > 0) script.Intro.Add(text);
                paragraph.Length = 0;
            };
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                var chapterMatch = ChapterLine.Match(line.TrimStart());
                if (chapterMatch.Success && !line.TrimStart().StartsWith("*"))
                {
                    if (chapter == null) flush();
                    chapter = new TutorialChapter
                    {
                        Number = int.Parse(chapterMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                        Title = chapterMatch.Groups[2].Value.Trim()
                    };
                    last = null;
                    script.Chapters.Add(chapter);
                    continue;
                }
                var trimmed = line.Trim();
                if (chapter == null)
                {
                    // L'accueil : une ligne « (consigne) » seule est sa consigne.
                    if (IsDirective(trimmed) && script.IntroInstruction.Length == 0 && paragraph.Length == 0 && script.Intro.Count == 0)
                    {
                        script.IntroInstruction = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        continue;
                    }
                    if (trimmed.Length == 0) flush(); else { if (paragraph.Length > 0) paragraph.Append(' '); paragraph.Append(trimmed); }
                    continue;
                }
                var subMatch = SubStepLine.Match(line);
                if (subMatch.Success && last != null && (line.StartsWith(" ") || line.StartsWith("\t")))
                {
                    last.Items.Add(subMatch.Groups[1].Value.Trim());
                    continue;
                }
                var stepMatch = StepLine.Match(trimmed);
                if (stepMatch.Success)
                {
                    var body = stepMatch.Groups[1].Value.Trim();
                    var step = new TutorialStep { Chapter = chapter };
                    if (body.StartsWith("("))
                    {
                        var close = body.IndexOf(')');
                        if (close > 0)
                        {
                            step.Instruction = body.Substring(1, close - 1).Trim();
                            body = body.Substring(close + 1).Trim();
                        }
                    }
                    step.Text = body;
                    step.Cue = TutorialCues.Resolve(chapter.Title, step.Instruction, step.Text);
                    chapter.Steps.Add(step);
                    last = step;
                    continue;
                }
                // Une ligne « (…) » hors puce : « à venir » tait le chapitre ;
                // les autres sont des notes de mise en forme pour moi, tues.
                if (IsDirective(trimmed) && Fold(trimmed).Contains("a venir")) chapter.Placeholder = true;
            }
            if (chapter == null) flush();
            script.IntroCue = TutorialCues.Resolve("Accueil", script.IntroInstruction, "");
            return script;
        }

        private static bool IsDirective(string line)
        {
            return line.Length > 2 && line.StartsWith("(") && line.EndsWith(")");
        }

        /// <summary>Minuscules sans accents, pour comparer des consignes.</summary>
        public static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c == '’' ? '\'' : c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Les mots en gras du texte (« **Livre** » → Livre), dans l'ordre.</summary>
        public static List<string> BoldWords(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (Match match in Regex.Matches(text, @"\*\*(.+?)\*\*")) list.Add(match.Groups[1].Value.Trim());
            return list;
        }
    }

    /// <summary>Le résolveur des consignes : des mots-clés, pas une grammaire.
    /// Une consigne peut en enchaîner plusieurs (« switch vers rail > versions,
    /// highlight rail + menu Versions ») : chaque mot-clé reconnu ajoute son
    /// action. Les actions rendues sont des noms que la fenêtre connaît ;
    /// voir MainWindow.Tutorial.cs pour ce que chacune fait à l'écran.</summary>
    public static class TutorialCues
    {
        // Les onglets du ruban de l'éditeur, les onglets d'un livre et ceux
        // d'une fiche : le mot en gras du texte choisit l'onglet.
        private static readonly string[][] RibbonTabs =
        {
            new[] { "texte", "Texte" }, new[] { "image", "Image" }, new[] { "insertion", "Insertion" },
            new[] { "formatage", "Formatage" }, new[] { "mise en page", "Mise en page" }, new[] { "mis en page", "Mise en page" },
            new[] { "gabarit", "Gabarit" }, new[] { "revision", "Révision" }, new[] { "correction", "Correction" },
            new[] { "prepresse", "Prépresse" }, new[] { "composition", "Prépresse" }
        };
        private static readonly string[][] BookTabs =
        {
            new[] { "edition", "Édition" }, new[] { "gabarit", "Gabarits & Format" }, new[] { "styles", "Styles" },
            new[] { "publication", "Publication" }, new[] { "textes", "Textes" }
        };

        public static TutorialCue Resolve(string chapterTitle, string instruction, string text)
        {
            var cue = new TutorialCue();
            var n = TutorialParser.Fold(instruction);
            var chapter = TutorialParser.Fold(chapterTitle);
            var bold = new List<string>();
            foreach (var word in TutorialParser.BoldWords(text)) bold.Add(TutorialParser.Fold(word));

            if (n.Length == 0)
            {
                // Sans consigne : le chapitre dit où l'on est (le journal).
                if (chapter.Contains("journal")) cue.Add("nav:journal");
                return cue;
            }

            // — Les navigations (une seule), dans l'ordre des mots les plus précis.
            if (n.Contains("accueil")) cue.Add("nav:home");
            else if (n.Contains("tableau ecrits") || n.Contains("tableau des ecrits")) cue.Add("nav:writings");
            else if (n.Contains("dans un livre") || n.Contains("vers le livre") || n.Contains("vers un livre")) cue.Add("nav:book");
            else if (n.Contains("ouverture chapitre") || n.Contains("ouvrir chapitre") || n.Contains("ouverture du chapitre")) cue.Add("nav:chapter");
            else if (n.Contains("tableau recherche")) cue.Add("nav:research");
            else if (n.Contains("pigeon") || n.Contains("un media") || n.Contains("le media")) cue.Add("nav:media");
            else if (n.Contains("tableau fiches")) cue.Add("nav:sheets");
            else if (n.Contains("fiche de personnage") || n.Contains("fiche personnage")) { cue.Add("nav:sheet"); cue.Add("sheet-tab:Général"); }
            else if (n.Contains("texte libre")) { cue.Add("nav:sheet"); cue.Add("sheet-tab:Texte libre"); }
            else if (n.Contains("vue miroir")) { cue.Add("nav:sheet"); cue.Add("sheet-tab:Texte libre"); cue.Add("sheet:mirror"); }
            else if (n.Contains("mode wiki")) { cue.Add("nav:sheet"); cue.Add("sheet:wiki"); }
            else if (n.Contains("genealogie")) { cue.Add("nav:sheet"); cue.Add("sheet:genealogy"); }
            else if (n.Contains("onglet general")) { cue.Add("nav:sheet"); cue.Add("sheet-tab:Général"); }
            else if (n.Contains("editeur de modele") && (n.Contains("fenetre") || n.Contains(">"))) { cue.Add("nav:sheets"); cue.Add("templates:open"); cue.Add("templates-tab:Sections"); }
            else if (n.Contains("sections extras")) { cue.Add("templates:open"); cue.Add("templates-tab:Sections extras"); }
            else if (n == "champs" || n.StartsWith("champs")) { cue.Add("templates:open"); cue.Add("templates-tab:Champs"); }
            else if ((n == "graph" || n.StartsWith("graph ")) && chapter.Contains("modele")) { cue.Add("templates:open"); cue.Add("templates-tab:Graph statistique"); }
            else if (n.Contains("tableau dictionnaire")) cue.Add("nav:dictionary");
            else if (n.Contains("nouvelle entree")) { cue.Add("nav:dictionary"); cue.Add("dictionary:new"); cue.Add("highlight:dictionary-new"); }
            else if (n.Contains("mots inconnus")) { cue.Add("nav:dictionary"); cue.Add("dictionary:unknown"); cue.Add("highlight:dictionary-unknown"); }
            else if (n.Contains("tableau plans")) cue.Add("nav:plans");
            else if (n.Contains("graph d'intensite")) { cue.Add("nav:plan"); cue.Add("plan:chart"); }
            else if (n.Contains("bouton graph intensite") || n.Contains("graph intensite")) { cue.Add("nav:plan"); cue.Add("highlight:plan-chart"); }
            else if (n.Contains("brique"))
            {
                cue.Add("nav:plan");
                var quoted = Regex.Match(instruction, "[\"«“]([^\"»”]+)[\"»”]");
                cue.Add(quoted.Success ? "plan:entry:" + quoted.Groups[1].Value.Trim() : "plan:entry");
            }
            else if (n.Contains("plan ouvert")) cue.Add("nav:plan");
            else if (n.Contains("vue succes")) { cue.Add("nav:journal"); cue.Add("journal:achievements"); }
            else if (n.Contains("journal")) cue.Add("nav:journal");
            else if (n.Contains("rail") && n.Contains("correction")) { cue.Add("rail:correction"); cue.Add("highlight:rail"); cue.Add("highlight:rail-panel"); }
            else if (n.Contains("rail") && n.Contains("version")) { cue.Add("rail:versions"); cue.Add("highlight:rail"); cue.Add("highlight:rail-panel"); }
            else if (n.Contains("switch onglet") || n.StartsWith("onglet"))
            {
                // L'onglet du ruban (« ruban » dans la consigne) ou celui du livre, nommé en gras.
                if (n.Contains("ruban"))
                {
                    var tab = FirstKnown(bold, RibbonTabs);
                    if (tab != null) { cue.Add("nav:chapter"); cue.Add("ribbon-tab:" + tab); }
                }
                else
                {
                    var tab = FirstKnown(bold, BookTabs);
                    if (tab != null) { cue.Add("nav:book"); cue.Add("book-tab:" + tab); }
                }
            }

            // — Les ouvertures et réglages, cumulables.
            if (n.Contains("ouvrir la pile")) cue.Add("pile:open");
            if (n.Contains("ouvrir le rail")) cue.Add("rail:open");
            if (n.Contains("centrer la fenetre")) cue.Add("window:center");

            // — Les mises en valeur (cumulables avec une navigation).
            if (n.Contains("highlight"))
            {
                var h = n.Substring(n.IndexOf("highlight", StringComparison.Ordinal));
                if (h.Contains("pile") && !h.Contains("bas de la pile")) cue.Add("highlight:pile");
                if (h.Contains("rail")) cue.Add("highlight:rail");
                if (h.Contains("details de correction") || h.Contains("menu versions")) cue.Add("highlight:rail-panel");
                if (h.Contains("ruban")) cue.Add("highlight:ribbon");
                else if (h.Contains("onglets")) cue.Add("highlight:tabs");
                if (h.Contains("boutons mode")) cue.Add("highlight:sheet-modes");
                else if (h.Contains("bouton vue wiki") || h.Contains("bouton mode wiki")) cue.Add("highlight:sheet-wiki");
                else if (h.Contains("bouton editeur de relations")) cue.Add("highlight:relations-editor");
                else if (h.Contains("editeur de modeles") || h.Contains("editeur de modele")) { if (!cue.Has("nav:")) cue.Add("nav:sheets"); cue.Add("highlight:templates-button"); }
                else if (h.Contains("boutons")) cue.Add(chapter.Contains("fiche") ? "highlight:library-actions" : "highlight:book-actions");
                if (h.Contains("relations") && !h.Contains("editeur de relations")) cue.Add("highlight:relations");
                if (h.Contains("arbre")) cue.Add("highlight:genealogy");
                if (h.Contains("colonnes")) cue.Add("highlight:plan-columns");
                if (h.Contains("graph") && !h.Contains("bouton") && chapter.Contains("plan")) cue.Add("highlight:plan-chart-view");
                if (h.Contains("bas de la pile")) cue.Add("highlight:journal-button");
                if (h.Contains("editeur de texte")) cue.Add(chapter.Contains("fiche") ? "highlight:sheet-editor" : "highlight:center");
                // Ce qui occupe le centre : le tableau, la page, la fenêtre, le contenu…
                if (h.Contains("tableau") || h.Contains("page") || h.Contains("fenetre") || h.Contains("contenu")
                    || h.Contains("succes") || h.Contains("journal perso") || h.Contains("trois lignes"))
                    cue.Add("highlight:center");
            }

            if (cue.Actions.Count == 0) cue.Unresolved = instruction;
            return cue;
        }

        private static string FirstKnown(List<string> boldWords, string[][] known)
        {
            foreach (var word in boldWords)
                foreach (var pair in known)
                    if (word == pair[0] || word.StartsWith(pair[0] + " ") || word.Contains(pair[0])) return pair[1];
            return null;
        }
    }
}

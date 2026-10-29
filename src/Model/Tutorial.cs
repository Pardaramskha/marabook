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
    }

    public static class TutorialParser
    {
        private static readonly Regex ChapterLine = new Regex(@"^\s*(\d+)\.\s+(.+?)\s*$");
        private static readonly Regex StepLine = new Regex(@"^\s*[\*\-]\s+(.*)$");

        public static TutorialScript Parse(string markdown)
        {
            var script = new TutorialScript();
            if (string.IsNullOrEmpty(markdown)) return script;
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            TutorialChapter chapter = null;
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
                var chapterMatch = ChapterLine.Match(line);
                if (chapterMatch.Success && !line.TrimStart().StartsWith("*"))
                {
                    if (chapter == null) flush();
                    chapter = new TutorialChapter
                    {
                        Number = int.Parse(chapterMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                        Title = chapterMatch.Groups[2].Value.Trim()
                    };
                    script.Chapters.Add(chapter);
                    continue;
                }
                if (chapter == null)
                {
                    if (line.Trim().Length == 0) flush(); else { if (paragraph.Length > 0) paragraph.Append(' '); paragraph.Append(line.Trim()); }
                    continue;
                }
                var stepMatch = StepLine.Match(line);
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
                    continue;
                }
                var trimmed = line.Trim();
                if (trimmed.StartsWith("(") && Fold(trimmed).Contains("a venir")) chapter.Placeholder = true;
            }
            if (chapter == null) flush();
            return script;
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
    /// Les actions rendues sont des noms que la fenêtre connaît ; voir
    /// MainWindow.Tutorial.cs pour ce que chacune fait à l'écran.</summary>
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
                if (chapter.Contains("journal")) cue.Actions.Add("nav:journal");
                return cue;
            }

            // — Les navigations.
            if (n.Contains("tableau ecrits") || n.Contains("tableau des ecrits")) cue.Actions.Add("nav:writings");
            else if (n.Contains("dans un livre") || n.Contains("vers le livre") || n.Contains("vers un livre")) cue.Actions.Add("nav:book");
            else if (n.Contains("ouverture chapitre") || n.Contains("ouvrir chapitre") || n.Contains("ouverture du chapitre")) cue.Actions.Add("nav:chapter");
            else if (n.Contains("tableau recherche")) cue.Actions.Add("nav:research");
            else if (n.Contains("pigeon") || n.Contains("un media") || n.Contains("le media")) cue.Actions.Add("nav:media");
            else if (n.Contains("tableau fiches")) cue.Actions.Add("nav:sheets");
            else if (n.Contains("fiche de personnage") || n.Contains("fiche personnage")) { cue.Actions.Add("nav:sheet"); cue.Actions.Add("sheet-tab:Général"); }
            else if (n.Contains("texte libre")) { cue.Actions.Add("nav:sheet"); cue.Actions.Add("sheet-tab:Texte libre"); }
            else if (n.Contains("vue miroir")) { cue.Actions.Add("nav:sheet"); cue.Actions.Add("sheet-tab:Texte libre"); cue.Actions.Add("sheet:mirror"); }
            else if (n.Contains("mode wiki")) { cue.Actions.Add("nav:sheet"); cue.Actions.Add("sheet:wiki"); }
            else if (n.Contains("genealogie")) { cue.Actions.Add("nav:sheet"); cue.Actions.Add("sheet:genealogy"); }
            else if (n.Contains("onglet general") || n.Contains("onglet general")) { cue.Actions.Add("nav:sheet"); cue.Actions.Add("sheet-tab:Général"); }
            else if (n.Contains("editeur de modele") && (n.Contains("fenetre") || n.Contains(">"))) { cue.Actions.Add("nav:sheets"); cue.Actions.Add("templates:open"); cue.Actions.Add("templates-tab:Sections"); }
            else if (n.Contains("sections extras")) { cue.Actions.Add("templates:open"); cue.Actions.Add("templates-tab:Sections extras"); }
            else if (n == "champs" || n.StartsWith("champs")) { cue.Actions.Add("templates:open"); cue.Actions.Add("templates-tab:Champs"); }
            else if ((n == "graph" || n.StartsWith("graph ")) && chapter.Contains("modele")) { cue.Actions.Add("templates:open"); cue.Actions.Add("templates-tab:Graph statistique"); }
            else if (n.Contains("tableau dictionnaire")) cue.Actions.Add("nav:dictionary");
            else if (n.Contains("nouvelle entree")) { cue.Actions.Add("nav:dictionary"); cue.Actions.Add("dictionary:new"); cue.Actions.Add("highlight:dictionary-new"); }
            else if (n.Contains("mots inconnus")) { cue.Actions.Add("nav:dictionary"); cue.Actions.Add("dictionary:unknown"); cue.Actions.Add("highlight:dictionary-unknown"); }
            else if (n.Contains("tableau plans")) cue.Actions.Add("nav:plans");
            else if (n.Contains("graph d'intensite ouvert") || n.Contains("graph d'intensite")) { cue.Actions.Add("nav:plan"); cue.Actions.Add("plan:chart"); }
            else if (n.Contains("bouton graph intensite") || n.Contains("graph intensite")) { cue.Actions.Add("nav:plan"); cue.Actions.Add("highlight:plan-chart"); }
            else if (n.Contains("nouvelle brique") || n.Contains("brique ouverte")) { cue.Actions.Add("nav:plan"); cue.Actions.Add("plan:entry"); }
            else if (n.Contains("plan ouvert")) cue.Actions.Add("nav:plan");
            else if (n.Contains("vue succes") || n.Contains("succes")) { cue.Actions.Add("nav:journal"); cue.Actions.Add("journal:achievements"); }
            else if (n.Contains("journal")) cue.Actions.Add("nav:journal");
            else if (n.Contains("rail") && n.Contains("correction")) { cue.Actions.Add("rail:correction"); cue.Actions.Add("highlight:rail"); }
            else if (n.Contains("rail") && n.Contains("version")) { cue.Actions.Add("rail:versions"); cue.Actions.Add("highlight:rail"); }
            else if (n.Contains("switch onglet") || n.StartsWith("onglet"))
            {
                // L'onglet du ruban (« ruban » dans la consigne) ou celui du livre, nommé en gras.
                if (n.Contains("ruban"))
                {
                    var tab = FirstKnown(bold, RibbonTabs);
                    if (tab != null) { cue.Actions.Add("nav:chapter"); cue.Actions.Add("ribbon-tab:" + tab); }
                }
                else
                {
                    var tab = FirstKnown(bold, BookTabs);
                    if (tab != null) { cue.Actions.Add("nav:book"); cue.Actions.Add("book-tab:" + tab); }
                }
            }

            // — Les mises en valeur (cumulables avec une navigation).
            if (n.Contains("highlight"))
            {
                if (n.Contains("pile")) cue.Actions.Add("highlight:pile");
                if (n.Contains("rail") && !cue.Has("highlight:rail")) cue.Actions.Add("highlight:rail");
                if (n.Contains("ruban")) cue.Actions.Add("highlight:ribbon");
                else if (n.Contains("onglets")) cue.Actions.Add("highlight:tabs");
                if (n.Contains("boutons")) cue.Actions.Add("highlight:book-actions");
                if (n.Contains("editeur de relations")) cue.Actions.Add("highlight:relations-editor");
                else if (n.Contains("relations")) cue.Actions.Add("highlight:relations");
                if (n.Contains("editeur de modeles") || n.Contains("editeur de modele")) { if (!cue.Has("nav:")) cue.Actions.Add("nav:sheets"); cue.Actions.Add("highlight:templates-button"); }
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

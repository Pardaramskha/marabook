using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C57 — le tutoriel (1.1.0, 29/10) : le Markdown de Rémi se lit
    /// (accueil, chapitres numérotés, puces à consigne, chapitres « à
    /// venir »), chaque consigne se résout en actions nommées, et le script
    /// embarqué (assets/tutoriel/Tutoriel.md) n'a AUCUNE consigne incomprise
    /// — une consigne nouvelle que le résolveur ignore fait échouer ce test :
    /// c'est le signal pour lui apprendre le mot.</summary>
    public static class TutorialTests
    {
        private const string Sample =
            "Bienvenue dans **Marabook** ! Une appli.\n\n"
            + "Deuxième paragraphe, *en italique*.\n\n"
            + "1. La pile et le rail\n"
            + "* (highlight pile) A gauche, **la pile**.\n"
            + "* (switch vers le tableau écrits) Le tableau.\n"
            + "2. Les écrits\n"
            + "* (switch onglet) L'onglet **édition** sert à tout.\n"
            + "* Une étape sans consigne.\n"
            + "3. Le GG\n"
            + "(section à venir, à ne pas implémenter vide)\n"
            + "4. Le journal perso\n"
            + "* Le journal, c'est votre tableau de bord !\n"
            + "* (switch vue succès) Les succès.\n";

        public static void Run(Harness t)
        {
            t.Suite("C57 — le tutoriel : script Markdown et consignes");

            var script = TutorialParser.Parse(Sample);
            t.Equal(2, script.Intro.Count, "deux paragraphes d'accueil");
            t.Check(script.Intro[0].StartsWith("Bienvenue dans **Marabook**"), "…le premier tel quel, Markdown compris");
            t.Equal(4, script.Chapters.Count, "quatre chapitres numérotés");
            t.Equal(3, script.Playable.Count, "…trois jouables : le GG « à venir » est tu");
            t.Check(script.Chapters[2].Placeholder && script.Chapters[2].Steps.Count == 0, "le chapitre à venir est marqué, sans étape");
            var first = script.Chapters[0];
            t.Equal("La pile et le rail", first.Title, "le titre du chapitre, sans son numéro");
            t.Equal("highlight pile", first.Steps[0].Instruction, "la consigne entre parenthèses");
            t.Equal("A gauche, **la pile**.", first.Steps[0].Text, "…le texte après elle");
            t.Check(first.Steps[0].Cue.Actions.Contains("highlight:pile"), "« highlight pile » → highlight:pile");
            t.Check(first.Steps[1].Cue.Actions.Contains("nav:writings"), "« switch vers le tableau écrits » → nav:writings");
            var edition = script.Chapters[1].Steps[0].Cue;
            t.Check(edition.Actions.Contains("nav:book") && edition.Actions.Contains("book-tab:Édition"), "« switch onglet » + **édition** → l'onglet Édition du livre");
            t.Equal(0, script.Chapters[1].Steps[1].Cue.Actions.Count, "une étape sans consigne ne demande rien");
            t.Check(script.Chapters[1].Steps[1].Cue.Unresolved == null, "…et n'est pas une lacune");
            var journal = script.Chapters[3];
            t.Check(journal.Steps[0].Cue.Actions.Contains("nav:journal"), "sans consigne dans « Le journal perso » : nav:journal");
            t.Check(journal.Steps[1].Cue.Actions.Contains("journal:achievements"), "« switch vue succès » → journal:achievements");

            // Le résolveur, consigne par consigne (celles du script de Rémi).
            Cue(t, "L'éditeur de texte", "switch onglet - highlight onglets + ruban", "L'onglet **Mis en page** permet…", "nav:chapter ribbon-tab:Mise en page highlight:ribbon");
            Cue(t, "L'éditeur de texte", "switch onglet - highlight onglets + ruban", "L'onglet **Prépresse**, c'est…", "nav:chapter ribbon-tab:Prépresse highlight:ribbon");
            Cue(t, "Les écrits", "switch onglet", "L'onglet **Gabarit et format** détermine… des **gabarits**", "nav:book book-tab:Gabarits & Format");
            Cue(t, "Les écrits", "highlight boutons", "Ajoutez…", "highlight:book-actions");
            Cue(t, "L'éditeur de texte", "switch vers rail > détails de correction", "…", "rail:correction highlight:rail");
            Cue(t, "L'éditeur de texte", "switch vers rail > versions", "…", "rail:versions highlight:rail");
            Cue(t, "La recherche", "switch sur Le pigeon", "…", "nav:media");
            Cue(t, "Les fiches", "switch vers une fiche de personnage onglmet général", "…", "nav:sheet sheet-tab:Général");
            Cue(t, "Les fiches", "switch vers le texte libre", "…", "nav:sheet sheet-tab:Texte libre");
            Cue(t, "Les fiches", "switch vue miroir", "…", "nav:sheet sheet-tab:Texte libre sheet:mirror");
            Cue(t, "Les fiches", "switch mode wiki", "…", "nav:sheet sheet:wiki");
            Cue(t, "Les fiches", "switch vers onglet général, highlight Relations", "…", "nav:sheet sheet-tab:Général highlight:relations");
            Cue(t, "Les fiches", "highlight bouton éditeur de relations", "…", "highlight:relations-editor");
            Cue(t, "Les fiches", "switch généalogie", "…", "nav:sheet sheet:genealogy");
            Cue(t, "L'éditeur de modèles", "highlight bouton éditeur de modèles", "…", "nav:sheets highlight:templates-button");
            Cue(t, "L'éditeur de modèles", "fenêtre éditeur de modèle > personnage > sections", "…", "nav:sheets templates:open templates-tab:Sections");
            Cue(t, "L'éditeur de modèles", "sections extras", "…", "templates:open templates-tab:Sections extras");
            Cue(t, "L'éditeur de modèles", "champs", "…", "templates:open templates-tab:Champs");
            Cue(t, "L'éditeur de modèles", "graph", "…", "templates:open templates-tab:Graph statistique");
            Cue(t, "Le dictionnaire", "switch Mots Inconnus", "…", "nav:dictionary highlight:dictionary-unknown");
            Cue(t, "Les plans", "highlight bouton graph intensité", "…", "nav:plan highlight:plan-chart");
            Cue(t, "Les plans", "graph d'intensité ouvert", "…", "nav:plan plan:chart");
            Cue(t, "Les plans", "nouvelle brique ouverte", "…", "nav:plan highlight:plan-add");
            t.Check(TutorialCues.Resolve("Les plans", "faire la vaisselle", "…").Unresolved == "faire la vaisselle", "une consigne inconnue est rendue telle quelle (lacune)");

            // Le script embarqué : tout se résout.
            var path = FindAsset(Path.Combine("assets", Path.Combine("tutoriel", "Tutoriel.md")));
            if (path == null) { t.Check(false, "assets/tutoriel/Tutoriel.md introuvable depuis " + AppContext.BaseDirectory); return; }
            var real = TutorialParser.Parse(File.ReadAllText(path, Encoding.UTF8));
            t.Check(real.Intro.Count >= 3 && real.Chapters.Count >= 9, "le script de Rémi : " + real.Intro.Count + " paragraphes d'accueil, " + real.Chapters.Count + " chapitres");
            var gaps = new List<string>();
            var steps = 0;
            foreach (var chapter in real.Playable)
                foreach (var step in chapter.Steps)
                {
                    steps++;
                    if (step.Cue.Unresolved != null) gaps.Add(chapter.Number + " (" + step.Cue.Unresolved + ")");
                }
            t.Check(steps >= 40, steps + " étapes jouables");
            t.Check(gaps.Count == 0, gaps.Count == 0 ? "aucune consigne incomprise" : "consignes incomprises : " + string.Join(" ; ", gaps.ToArray()));
            var plot = FindAsset(Path.Combine("assets", Path.Combine("tutoriel", "PROJET-TEST.plot")));
            t.Check(plot != null, "le projet d'exemple du tutoriel est embarqué (assets/tutoriel/PROJET-TEST.plot)");
        }

        private static void Cue(Harness t, string chapter, string instruction, string text, string expected)
        {
            var cue = TutorialCues.Resolve(chapter, instruction, text);
            var got = string.Join(" ", cue.Actions.ToArray());
            t.Check(got == expected, "« " + instruction + " » → " + expected + (got == expected ? "" : " (obtenu : " + got + ")"));
        }

        /// <summary>Remonte depuis le dossier de l'exécutable jusqu'à la racine du dépôt.</summary>
        private static string FindAsset(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}

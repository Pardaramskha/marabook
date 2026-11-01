using System;
using System.IO;
using Marabook.Correction;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C59 — « Ignorer partout » PAR ÉCRIT (1.1.0, 30/10, Rémi) :
    /// le mot (ou la règle) d'un signalement se tait dans l'écrit ouvert
    /// seulement — ni dans le projet, ni dans l'application —, la liste vit
    /// sur l'item et fait l'aller-retour .plot (v44) ; « Ignorer » ne tait
    /// que l'occurrence.</summary>
    public static class ItemIgnoreTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C59 — 1.1.0 : « Ignorer partout » par écrit (.plot v44) (30/10)");
            Keys(t);
            Filter(t);
            RoundTrip(t);
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

        private static void Keys(Harness t)
        {
            t.Equal("Marabout", CheckerHost.ItemKey(new Finding { Word = "Marabout", RuleId = "repetition" }), "un signalement à mot : la clé est le mot");
            t.Equal("règle:virg_fin", CheckerHost.ItemKey(new Finding { Word = "", RuleId = "virg_fin" }), "sans mot : la règle, préfixée");
            t.Equal(null, CheckerHost.ItemKey(new Finding()), "ni mot ni règle : rien");
        }

        private static void Filter(Harness t)
        {
            var document = Document("Le marabout mange.", "Le marabout dort.", "Un carabin passe.", "Un carabin repasse.");
            var host = new CheckerHost();
            host.Add(new RepetitionChecker());
            var findings = host.Run(document, null);
            t.Equal(2, findings.Count, "deux répétitions relevées (marabout, carabin)");

            // « Ignorer » = l'occurrence : la même répétition plus loin reste relevée.
            host.IgnoreHere(findings[0]);
            t.Equal(1, host.Run(document, null).Count, "« Ignorer » tait cette occurrence seule");

            // « Ignorer partout » dans l'écrit : le mot, plié (casse, accents).
            var item = new BinderItem { Kind = ItemKind.Text, Title = "Un" };
            host.ItemIgnored = item.ProofIgnored;
            var key = host.IgnoreInItem(findings[1]);
            t.Equal("carabin", key, "la clé inscrite est le mot");
            t.Check(item.ProofIgnored.Count == 1 && item.ProofIgnored[0] == "carabin", "…dans la liste de L'ÉCRIT");
            t.Equal(0, host.ProjectIgnored.Count, "…pas dans celle du projet");
            t.Equal(0, host.GlobalIgnored.Count, "…ni dans celle de l'application");
            t.Equal(0, host.Run(document, null).Count, "plus rien de relevé pour carabin");
            host.IgnoreInItem(findings[1]);
            t.Equal(1, item.ProofIgnored.Count, "inscrire deux fois n'ajoute rien");
            var shouted = Document("Un CARABIN passe.", "Un carabín repasse.");
            host = new CheckerHost();
            host.Add(new RepetitionChecker());
            host.ItemIgnored = item.ProofIgnored;
            t.Equal(0, host.Run(shouted, null).Count, "la clé est pliée : CARABIN et carabín se taisent aussi");

            // Un autre écrit ne porte pas la liste.
            host.ItemIgnored = new BinderItem().ProofIgnored;
            t.Equal(1, host.Run(shouted, null).Count, "un autre écrit relève encore");

            // Une règle (sans mot) : « règle:id » tait tout signalement de cette règle.
            var ruled = new BinderItem { Kind = ItemKind.Text };
            host.ItemIgnored = ruled.ProofIgnored;
            t.Equal("règle:repetition", host.IgnoreInItem(new Finding { RuleId = "repetition" }), "sans mot, la règle est inscrite");
            t.Equal(0, host.Run(document, null).Count, "…et plus aucune répétition dans cet écrit");
            t.Equal(null, host.IgnoreInItem(new Finding()), "un signalement sans mot ni règle n'inscrit rien");
        }

        private static void RoundTrip(Harness t)
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-item-ignore");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            try
            {
                var project = Project.CreateNew();
                var item = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre" };
                item.Document = Document("Le marabout mange.");
                item.ProofIgnored.Add("marabout");
                item.ProofIgnored.Add("règle:virg_fin_dialogue");
                project.Category(Project.KeyWritings).Children.Add(item);
                var plain = new BinderItem { Kind = ItemKind.Text, Title = "Vide" };
                project.Category(Project.KeyWritings).Children.Add(plain);
                project.RelinkParents();
                var path = Path.Combine(dir, "ignores.plot");
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                t.Equal(45, loaded.LoadedFormatVersion, "écrit en v45 (v44 ignorés par écrit, v45 encyclopédie)");
                var back = loaded.FindById(item.Id);
                t.Check(back != null && back.ProofIgnored.Count == 2 && back.ProofIgnored[0] == "marabout" && back.ProofIgnored[1] == "règle:virg_fin_dialogue",
                    "la liste de l'écrit fait l'aller-retour, dans l'ordre");
                var other = loaded.FindById(plain.Id);
                t.Check(other != null && other.ProofIgnored.Count == 0, "un écrit sans ignorés n'en reçoit pas");
                t.Equal(0, loaded.ProofIgnored.Count, "rien ne fuit vers la liste du projet");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}

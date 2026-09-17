using System;
using System.Collections.Generic;

namespace Marabook.Tests
{
    /// <summary>Le harnais maison : trois assertions, une liste de suites, un
    /// code de sortie. Rien de plus — ce n'est pas un framework, c'est un
    /// filet. Règle du projet : TOUT correctif de bug arrive avec le test qui
    /// l'aurait attrapé (voir PLAN.md, batch 24).</summary>
    public sealed class Harness
    {
        public int Total;
        public int Failed;
        private string _suite = "";

        public void Suite(string name)
        {
            _suite = name;
            Console.WriteLine();
            Console.WriteLine("== " + name);
        }

        public void Check(bool condition, string label)
        {
            Total++;
            if (condition) return;
            Failed++;
            Console.WriteLine("  ÉCHEC  [" + _suite + "] " + label);
        }

        public void Equal<T>(T expected, T actual, string label)
        {
            Total++;
            if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
            Failed++;
            Console.WriteLine("  ÉCHEC  [" + _suite + "] " + label
                + " — attendu « " + Text(expected) + " », obtenu « " + Text(actual) + " »");
        }

        public void Throws<TException>(Action action, string label)
            where TException : Exception
        {
            Total++;
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception error)
            {
                Failed++;
                Console.WriteLine("  ÉCHEC  [" + _suite + "] " + label
                    + " — attendu " + typeof(TException).Name
                    + ", obtenu " + error.GetType().Name + " (" + error.Message + ")");
                return;
            }
            Failed++;
            Console.WriteLine("  ÉCHEC  [" + _suite + "] " + label
                + " — attendu " + typeof(TException).Name + ", rien n'a été levé");
        }

        public void Info(string message)
        {
            Console.WriteLine("  " + message);
        }

        private static string Text(object value)
        {
            return value == null ? "null" : value.ToString();
        }
    }

    public static class TestMain
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var harness = new Harness();
            var suites = new List<Action<Harness>>
            {
                RoundTripTests.Run,      // C1 — le .plot, champ par champ
                HyphenCorpusTests.Run,   // C2 — score de césure (plancher)
                PivotEditTests.Run,      // C3 — l'algèbre d'édition
                ComposerTests.Run,       // C4 — coupure de ligne sur métriques fixes
                CorrectionTests.Run,     // C5 — la chaîne de correction
                SearchTests.Run,         // C6 — la recherche pivot
                TokenizerTests.Run,      // C7 — le tokeniseur français unique
                SpellTests.Run,          // C8 — l'orthographe (Hunspell maison)
                GrammarBridgeTests.Run,  // C9 — pont Grammalecte (offsets, trames)
                MarkdownTests.Run,       // C10 — markdown des fiches, catégories
                LexiconTests.Run,        // C11 — dictionnaire personnel à natures (b33)
                ThemeTests.Run,          // C12 — les palettes se parsent (b34)
                TypographyTests.Run,     // C13 — passe typographique (b34)
                PlanTests.Run,           // C14 — plans (b35)
                GenealogyTests.Run,      // C15 — généalogie (b36)
                ProjectSearchTests.Run,  // C16 — recherche projet (b37)
                DocumentDiffTests.Run,   // C17 — versions d'écrits (b38)
                RightPanelTests.Run,     // C18 — la colonne de droite (b39)
                HomeTests.Run,           // C19 — l'Accueil : récents, épingle, racine (b41)
                PackTests.Run,           // C20 — pack de correctifs du 12/09/2026
                AchievementTests.Run,    // C21 — les succès (12/09/2026)
                StyleTests.Run,          // C22 — style morphologique + synonymes (b44)
                StyleBatchTests.Run,     // C23 — b45 : frappe typographique, incises, racines, bilan
                PresenceTests.Run,       // C24 — présence et évolution (b47)
                FieldKindTests.Run,      // C25 — natures de champ (b47 bis)
                PaceTests.Run            // C26 — objectifs et temps (b48)
            };
            foreach (var suite in suites)
            {
                try
                {
                    suite(harness);
                }
                catch (Exception error)
                {
                    harness.Failed++;
                    harness.Total++;
                    Console.WriteLine("  SUITE EN ÉCHEC : " + error);
                }
            }
            Console.WriteLine();
            Console.WriteLine(harness.Total + " tests, " + harness.Failed + " échec"
                + (harness.Failed > 1 ? "s" : "") + ".");
            return harness.Failed == 0 ? 0 : 1;
        }
    }
}

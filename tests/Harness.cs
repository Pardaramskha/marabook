using System;
using System.Collections.Generic;

namespace UniversSale.Tests
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
                PlanTests.Run            // C14 — plans (b35)
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

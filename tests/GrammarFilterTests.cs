using System.Collections.Generic;
using System.IO;
using System.Threading;
using Marabook.Correction.Grammalecte;

namespace Marabook.Tests
{
    /// <summary>C40 — le second regard sur Grammalecte (23/09/2026,
    /// grammalecte/marabook_filters.py) : deux faux positifs de roman ne
    /// remontent plus — l'accord par-dessus un complément (« les roulés au
    /// fromage apocalyptiques ») et le participe nominalisé après « de »
    /// (« un air de déterré ») — tandis que les vraies fautes voisines
    /// restent relevées. De bout en bout par le vrai pont ; sautée sans
    /// python/ ou grammalecte/, jamais rouge pour une absence d'outil.</summary>
    public static class GrammarFilterTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C40 — second regard sur Grammalecte (23/09)");
            if (!File.Exists(GrammalecteBridge.PythonPath)
                || !File.Exists(GrammalecteBridge.ScriptPath))
            {
                t.Info("pont Grammalecte absent : vérification sautée");
                return;
            }
            var bridge = new GrammalecteBridge();
            try
            {
                // — Les deux phrases du projet de Rémi (« La grande aventure de Shady », écrit « test »).
                Expect(t, bridge, "Les roulés au fromage apocalyptiques", "l'adjectif s'accorde avec « les roulés », pas avec « au fromage »");
                Expect(t, bridge, "Les pupilles gonflées comme des ballons, il abordait un air de déterré ou d'échappé d'asile, au choix.",
                    "« un air de déterré ou d'échappé » : des noms, pas des infinitifs");
                // — Variantes qui doivent aussi se taire.
                Expect(t, bridge, "Les petits roulés au fromage apocalyptiques.", "un adjectif entre le déterminant et la tête ne gêne pas");
                Expect(t, bridge, "Des gâteaux au chocolat amers.", "« des gâteaux … amers »");
                Expect(t, bridge, "Elle avait une mine de déterré.", "« une mine de déterré »");
                // — Les vraies fautes voisines restent relevées.
                Expect(t, bridge, "Un roulé au fromage apocalyptiques.", "tête au singulier : l'accord est bien faux", "apocalyptiques");
                Expect(t, bridge, "J'ai envie de mangé une pomme.", "« envie de » n'est pas un nom d'apparence : infinitif attendu", "mangé");
                Expect(t, bridge, "Il a essayé de mangé.", "« essayé de » : autre règle, intacte", "mangé");
                Expect(t, bridge, "Les chat mange.", "l'accord ordinaire est toujours relevé", "chat");
            }
            finally
            {
                bridge.Dispose();
            }
        }

        /// <summary>Les mots relevés par la grammaire (typographie éteinte)
        /// doivent être exactement ceux attendus.</summary>
        private static void Expect(Harness t, GrammalecteBridge bridge, string text, string label, params string[] expected)
        {
            var task = bridge.CheckAsync(text, GrammalecteOptions.Effective(null, true, false), CancellationToken.None);
            if (!task.Wait(60000))
            {
                t.Check(false, label + " : pas de réponse du pont");
                return;
            }
            var words = new List<string>();
            foreach (var error in task.Result)
                if (error.Start >= 0 && error.End <= text.Length && error.End > error.Start)
                    words.Add(text.Substring(error.Start, error.End - error.Start));
            t.Equal(string.Join("|", expected), string.Join("|", words.ToArray()), label);
        }
    }
}

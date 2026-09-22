using System;
using System.Collections.Generic;
using Marabook.Correction;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C13 — la passe typographique (batch 34, port de Typonanny) :
    /// chaque règle, les préréglages, les zones protégées, l'idempotence,
    /// le diff de caractères et la redistribution sur les runs formatés.</summary>
    public static class TypographyTests
    {
        private const string Nbsp = "\u00A0";
        private const string Fine = "\u202F";

        public static void Run(Harness t)
        {
            t.Suite("C13 — passe typographique");
            Rules(t);
            Presets(t);
            Protection(t);
            Idempotence(t);
            Diff(t);
            Redistribution(t);
        }

        private static string Clean(string text)
        {
            return Typography.Clean(text, new TypographyOptions()).Text;
        }

        private static void Rules(Harness t)
        {
            t.Equal("Il l’a dit…", Clean("Il l'a dit..."), "apostrophe courbe et points de suspension");
            t.Equal("etc.", Clean("etc..."), "etc… → etc.");
            t.Equal("«" + Nbsp + "Bonjour" + Nbsp + "»", Clean("\"Bonjour\""), "guillemets français, pleine dedans (in)");
            // Depuis le 13/09, le tiret de dialogue est suivi d'une INSÉCABLE
            // (ce que Grammalecte réclame à chaque réplique).
            t.Equal("— Viens, dit-il.", Clean("- Viens, dit-il."), "tiret de dialogue en tête, suivi d'une insécable");
            t.Equal("— Viens.", Clean("-- Viens."), "double tiret de dialogue");
            t.Equal("1914–1918", Clean("1914-1918"), "intervalle en demi-cadratin");
            t.Equal("auteur·ice et lect·eur·ice", Clean("auteur::ice et lect::eur::ice"), "deux deux-points : point médian (22/09)");
            t.Equal("Oui" + Fine + "! Non" + Fine + "?", Clean("Oui ! Non?"), "fine avant ! et ? (posée ou ajoutée)");
            t.Equal("Note" + Nbsp + ": suite", Clean("Note : suite"), "pleine avant : (in)");
            t.Equal("10" + Nbsp + "% et 12" + Nbsp + "kg", Clean("10 % et 12 kg"), "insécables d'unités");
            t.Equal("10" + Nbsp + "%", Clean("10%"), "% sans espace : insécable ajoutée");
            t.Equal("10" + Fine + "000 habitants", Clean("10 000 habitants"), "milliers en fine");
            t.Equal("un cœur, des bœufs", Clean("un coeur, des boeufs"), "ligatures œ (liste blanche)");
            t.Equal("Coeurville", Clean("Coeurville"), "hors liste blanche : pas de ligature");
            t.Equal("10" + Nbsp + "×" + Nbsp + "15", Clean("10 x 15"), "dimensions ×");
            // Depuis le 13/09 : les exposants Unicode de Grammalecte.
            t.Equal("le 2ᵉ et la 1ʳᵉ", Clean("le 2ème et la 1ère"), "ordinaux fautifs → exposants");
            t.Equal("le 2ᵉ et la 1ʳᵉ", Clean("le 2e et la 1re"), "ordinaux plats → exposants");
            t.Equal("les 1ᵉʳˢ, le 2ⁿᵈ, la 2ᵈᵉ, les 3ᵉˢ", Clean("les 1ers, le 2nd, la 2de, les 3èmes"), "toutes les formes");
            t.Equal("le 2ᵉ et la 1ʳᵉ", Clean("le 2ᵉ et la 1ʳᵉ"), "déjà en exposant : rien ne bouge");
            t.Equal("1e5", Clean("1e5"), "une lettre coincée entre des chiffres n'est pas un ordinal");
            t.Equal("a b", Clean("a   b"), "espaces doubles");
            t.Equal("fin", Clean("fin   "), "espaces en fin de ligne");
            var flagged = Typography.Clean("Il dort. Etat de grâce.", new TypographyOptions());
            t.Check(flagged.Warnings.Count == 1 && flagged.Warnings[0].Contains("État"),
                "majuscule à accentuer : signalée, jamais corrigée");
            t.Equal("Il dort. Etat de grâce.", flagged.Text, "le texte ne bouge pas sur un signalement");
            var odd = Typography.Clean("Il a dit \"bonjour", new TypographyOptions());
            t.Check(odd.Text.Contains("\"") && odd.Warnings.Count == 1, "guillemets impairs : laissés, signalés");
            var counters = Typography.Clean("Oui ! \"Non\" ... l'ami", new TypographyOptions());
            var total = 0;
            foreach (var c in counters.Counters) total += c.Value;
            t.Check(total >= 4, "les compteurs par catégorie comptent chaque correction (" + total + ")");
        }

        private static void Presets(Harness t)
        {
            var souple = new TypographyOptions { Preset = "souple" };
            t.Equal("Note" + Fine + ": «" + Fine + "x" + Fine + "»", Typography.Clean("Note : \"x\"", souple).Text,
                "souple : fine partout");
            var minimal = new TypographyOptions { Preset = "minimal" };
            t.Equal("l’ami… \"x\" 10 % 1914-1918", Typography.Clean("l'ami... \"x\" 10 % 1914-1918", minimal).Text,
                "minimal : apostrophes et points seuls, règles ° ignorées");
            var off = new TypographyOptions { Apostrophes = false, Ellipses = false };
            t.Equal("l'ami...", Typography.Clean("l'ami...", off).Text, "règles décochées : inertes");
            var json = TypographyOptions.FromJson(souple.ToJson());
            t.Equal("souple", json.Preset, "les options font l'aller-retour JSON");
            t.Check(!TypographyOptions.FromJson(new TypographyOptions { LigaturesAe = true, Quotes = false }.ToJson()).Quotes,
                "une règle décochée survit au JSON");
        }

        private static void Protection(Harness t)
        {
            t.Equal("voir https://exemple.fr/a...b?x=1 ici", Clean("voir https://exemple.fr/a...b?x=1 ici"), "une adresse web est intouchable");
            t.Equal("à 10:30 …", Clean("à 10:30 ..."), "une heure est intouchable, le reste corrigé…");
            t.Equal("code `a...b` et suite…", Clean("code `a...b` et suite..."), "un span de code est intouchable");
            var text = "\"a\" et \"b\"";
            var result = Typography.Clean(text, new TypographyOptions(), new List<int[]> { new[] { 0, 3 } });
            t.Equal("\"a\" et «" + Nbsp + "b" + Nbsp + "»", result.Text, "une plage protégée (ne pas corriger) reste telle quelle");
            t.Equal("Il \uFFFC dit...", Clean("Il \uFFFC dit...").Replace("…", "..."), "un élément (U+FFFC) survit à la passe");
        }

        private static void Idempotence(Harness t)
        {
            var once = Typography.Clean("Oui ! \"Non\" : 10 000 x 2 kg, l'ami...", new TypographyOptions());
            var twice = Typography.Clean(once.Text, new TypographyOptions());
            t.Equal(once.Text, twice.Text, "une seconde passe ne change plus rien");
            t.Equal(0, twice.Total, "…et ne compte plus rien (déjà bon)");
        }

        private static void Diff(Harness t)
        {
            var ops = CharDiff.Diff("l'ami...", "l’ami…");
            var rebuilt = new System.Text.StringBuilder();
            var removed = new System.Text.StringBuilder();
            foreach (var op in ops)
            {
                if (op.Type != '-') rebuilt.Append(op.Char);
                if (op.Type != '+') removed.Append(op.Char);
            }
            t.Equal("l’ami…", rebuilt.ToString(), "le diff reconstruit la cible");
            t.Equal("l'ami...", removed.ToString(), "…et la source");
            t.Equal(0, CharDiff.Diff("", "").Count, "diff vide");
            t.Check(CharDiff.Diff("abc", "abc").Count == 3, "identiques : trois conservés");
            t.Check(CharDiff.Diff(new string('a', 2000), new string('b', 2000)) == null, "trop d'éditions : null, jamais une boucle");
        }

        private static void Redistribution(Harness t)
        {
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "Il a dit " });
            paragraph.Runs.Add(new TextRun { Text = "\"bonjour\"", Bold = true });
            paragraph.Runs.Add(new TextRun { Text = " ! Puis" });
            paragraph.Runs.Add(new TextRun { FootnoteId = "n1" });
            paragraph.Runs.Add(new TextRun { Text = " l'ami...", Italic = true });
            var document = new TextDocument();
            document.Paragraphs.Add(paragraph);
            var result = TypographyPass.Run(document, new TypographyOptions());
            t.Equal(1, result.Changes.Count, "un paragraphe modifié");
            var after = result.Paragraphs[0];
            t.Equal("Il a dit «" + Nbsp + "bonjour" + Nbsp + "»" + Fine + "! Puis\uFFFC l’ami…", PivotEdit.FlatText(after),
                "le texte plat du paragraphe reconstruit");
            var bold = after.Runs.Find(delegate(TextRun r) { return r.Bold == true; });
            t.Check(bold != null && bold.Text == "«" + Nbsp + "bonjour" + Nbsp + "»",
                "les guillemets rejoignent le run gras qu'ils entourent");
            var note = after.Runs.Find(delegate(TextRun r) { return r.FootnoteId == "n1"; });
            t.Check(note != null, "l'appel de note survit, intact");
            var italic = after.Runs.Find(delegate(TextRun r) { return r.Italic == true; });
            t.Check(italic != null && italic.Text == " l’ami…", "l'italique garde son texte corrigé");
            t.Equal(5, after.Runs.Count, "autant de runs qu'avant, formats préservés");

            var unchanged = new TextDocument();
            var p = new TextParagraph();
            p.Runs.Add(new TextRun { Text = "Rien à corriger." });
            unchanged.Paragraphs.Add(p);
            var none = TypographyPass.Run(unchanged, new TypographyOptions());
            t.Equal(0, none.Changes.Count, "paragraphe propre : aucun changement");
            t.Check(ReferenceEquals(none.Paragraphs[0], p), "…et le paragraphe d'origine est réutilisé tel quel");

            var noProof = new TextParagraph();
            noProof.Runs.Add(new TextRun { Text = "l'un", NoProof = true });
            noProof.Runs.Add(new TextRun { Text = " et l'autre" });
            var doc2 = new TextDocument();
            doc2.Paragraphs.Add(noProof);
            var guarded = TypographyPass.Run(doc2, new TypographyOptions());
            t.Equal("l'un et l’autre", PivotEdit.FlatText(guarded.Paragraphs[0]), "« ne pas corriger » protège son run");
        }
    }
}

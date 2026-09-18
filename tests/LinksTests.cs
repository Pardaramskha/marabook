using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C27 — les [[liens]] du texte (18/09) : détection, cible et
    /// texte affiché (« [[Cible|texte]] »), notation à insérer, version
    /// propre pour l'export (marques retirées, mots gardés, mise en forme
    /// conservée), plages masquées et déplacements du curseur.</summary>
    public static class LinksTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C27 — liens du texte");

            // — Détection.
            var simple = Links.Find("Voici [[Gandalf]] qui vient.");
            t.Equal(1, simple.Count, "un lien simple");
            t.Equal("Gandalf", simple[0].Target, "cible du lien simple");
            t.Equal("Gandalf", simple[0].Text, "texte du lien simple = la cible");
            t.Equal(6, simple[0].Start, "début sur « [[ »");
            t.Equal(8, simple[0].TextStart, "texte après « [[ »");
            t.Equal(15, simple[0].TextEnd, "texte avant « ]] »");
            t.Equal(17, simple[0].End, "fin après « ]] »");

            var piped = Links.Find("Voici [[Gandalf|le vieux mage]] qui vient.");
            t.Equal(1, piped.Count, "un lien à texte");
            t.Equal("Gandalf", piped[0].Target, "cible d'un lien à texte");
            t.Equal("le vieux mage", piped[0].Text, "texte affiché d'un lien à texte");
            t.Equal("le vieux mage", "Voici [[Gandalf|le vieux mage]] qui vient.".Substring(piped[0].TextStart, piped[0].TextEnd - piped[0].TextStart), "plage du texte affiché");

            t.Equal(0, Links.Find("[[]] et [[ |x]]").Count, "notation vide ou cible vide : pas de lien");
            t.Equal(0, Links.Find("[[" + new string('a', 200) + "]]").Count, "notation démesurée : pas de lien");
            t.Equal(0, Links.Find("[[ouvert sans fin").Count, "sans fermant : rien");
            var nested = Links.Find("[[a [[b]] c");
            t.Equal(1, nested.Count, "un ouvrant dans un ouvrant : le dernier compte");
            t.Equal("b", nested[0].Target, "cible du dernier ouvrant");
            t.Equal(2, Links.Find("[[A]][[B]]").Count, "deux liens collés");
            t.Equal("Gandalf", Links.Find("[[ Gandalf ]]")[0].Target, "cible dégraissée");
            t.Equal("Gandalf", Links.Find("[[Gandalf|]]")[0].Text, "texte vide après la barre : la cible");

            // — At : marques comprises.
            var text = "ab[[G|xy]]cd";
            t.Check(Links.At(text, 2) != null && Links.At(text, 9) != null, "At couvre les marques");
            t.Check(Links.At(text, 1) == null && Links.At(text, 11) == null, "At hors du lien : null");

            // — Notation à insérer.
            t.Equal("[[Gandalf|le vieux mage]]", Links.Markup("Gandalf", "le vieux mage"), "sélection ≠ cible : lien à texte");
            t.Equal("[[Gandalf]]", Links.Markup("Gandalf", "Gandalf"), "sélection = cible : lien simple");
            t.Equal("[[Gandalf]]", Links.Markup("Gandalf", ""), "sans sélection : lien simple");
            t.Equal("[[Gandalf]]", Links.Markup("Gandalf", null), "sélection nulle : lien simple");
            t.Equal("[[Gandalf]]", Links.Markup("Gandalf", "a|b"), "une barre dans la sélection : lien simple");
            t.Equal("[[Gandalf]]", Links.Markup(" Gandalf ", "  "), "cible dégraissée, sélection blanche");

            // — Plages : marques et texte.
            var spans = Links.Spans("[[G|le mage]]");
            t.Equal(3, spans.Count, "trois plages : marque, texte, marque");
            t.Check(spans[0].IsMark && spans[0].Start == 0 && spans[0].Length == 4, "marque ouvrante « [[G| »");
            t.Check(!spans[1].IsMark && spans[1].Start == 4 && spans[1].Length == 7, "texte « le mage »");
            t.Check(spans[2].IsMark && spans[2].Start == 11 && spans[2].Length == 2, "marque fermante « ]] »");
            var simpleSpans = Links.Spans("[[G]]");
            t.Equal(3, simpleSpans.Count, "lien simple : deux marques et le texte (la cible)");
            t.Check(!simpleSpans[1].IsMark && simpleSpans[1].Start == 2 && simpleSpans[1].Length == 1, "le texte d'un lien simple est la cible");

            // — Strip sur du texte.
            t.Equal("Voici le vieux mage qui vient.", Links.Strip("Voici [[Gandalf|le vieux mage]] qui vient."), "export : les mots restent, les marques tombent");
            t.Equal("Voici Gandalf.", Links.Strip("Voici [[Gandalf]]."), "export d'un lien simple : la cible");
            t.Equal("rien", Links.Strip("rien"), "sans lien : inchangé");
            t.Equal("", Links.Strip(""), "vide");

            // — Strip sur un document : mise en forme et éléments conservés,
            //   original intact.
            var document = new TextDocument();
            var paragraph = new TextParagraph { StyleId = "body", Indent = 12 };
            paragraph.Runs.Add(new TextRun { Text = "Voici [[Gan", Bold = true });
            paragraph.Runs.Add(new TextRun { Text = "dalf|le vieux" });
            paragraph.Runs.Add(new TextRun { Text = " mage]] !", Italic = true });
            paragraph.Runs.Add(new TextRun { FootnoteId = "n1", Text = "1" });
            document.Paragraphs.Add(paragraph);
            document.Paragraphs.Add(new TextParagraph());
            document.Footnotes.Add(new Footnote { Id = "n1", Text = "note" });
            var stripped = Links.Strip(document);
            t.Equal(2, stripped.Paragraphs.Count, "autant de paragraphes");
            t.Equal("Voici le vieux mage !￼", PivotEdit.FlatText(stripped.Paragraphs[0]), "texte plat sans marques, élément gardé");
            t.Equal(4, stripped.Paragraphs[0].Runs.Count, "les runs restent (rien de vide)");
            t.Check(stripped.Paragraphs[0].Runs[0].Bold == true && stripped.Paragraphs[0].Runs[2].Italic == true, "mise en forme conservée");
            t.Equal("Voici ", stripped.Paragraphs[0].Runs[0].Text, "premier run : « Gan » tombé avec la marque");
            t.Equal("le vieux", stripped.Paragraphs[0].Runs[1].Text, "deuxième run : « dalf| » tombé");
            t.Equal(" mage !", stripped.Paragraphs[0].Runs[2].Text, "troisième run : « ]] » tombé");
            t.Equal("n1", stripped.Paragraphs[0].Runs[3].FootnoteId, "l'appel de note est là");
            t.Equal(12.0, stripped.Paragraphs[0].Indent, "la coquille du paragraphe suit");
            t.Equal("Voici [[Gandalf|le vieux mage]] !￼", PivotEdit.FlatText(document.Paragraphs[0]), "l'original n'a pas bougé");
            t.Check(ReferenceEquals(document.Footnotes, stripped.Footnotes), "les notes sont partagées");
            var whole = new TextParagraph();
            whole.Runs.Add(new TextRun { Text = "[[X]]" });
            var wholeDoc = new TextDocument();
            wholeDoc.Paragraphs.Add(whole);
            t.Equal("X", PivotEdit.FlatText(Links.Strip(wholeDoc).Paragraphs[0]), "un run tout en lien : la cible");

            // — Curseur : traverser les marques masquées d'un pas.
            t.Equal(6, Links.SkipHidden(text, 3, 1), "→ depuis « [[ » : après la marque ouvrante");
            t.Equal(2, Links.SkipHidden(text, 5, -1), "← dans la marque ouvrante : avant « [[ »");
            t.Equal(10, Links.SkipHidden(text, 9, 1), "→ dans « ]] » : après le lien");
            t.Equal(8, Links.SkipHidden(text, 9, -1), "← dans « ]] » : à la fin du texte du lien");
            t.Equal(7, Links.SkipHidden(text, 7, 1), "dans le texte du lien : inchangé");
            t.Equal(6, Links.SkipHidden(text, 6, -1), "au début du texte du lien vers la gauche : inchangé (un pas de plus traversera)");
            t.Equal(6, Links.SnapOutOfHidden(text, 4), "clic dans la marque ouvrante : début du texte");
            t.Equal(10, Links.SnapOutOfHidden(text, 9), "clic dans « ]] » : après le lien");
            t.Equal(7, Links.SnapOutOfHidden(text, 7), "clic dans le texte : inchangé");
            t.Equal(8, Links.SnapOutOfHidden(text, 8), "clic à la fin du texte : inchangé");

            // — Cibles et liens entrants.
            var targets = Links.Targets("[[Gandalf|x]] et [[gandalf]] et [[Frodon]]");
            t.Equal(2, targets.Count, "cibles distinctes (casse ignorée)");
            t.Equal("Gandalf", targets[0], "première cible");
            t.Equal("Frodon", targets[1], "seconde cible");
            t.Check(Links.LinksTo("il voit [[Gandalf|le mage]]", "GANDALF"), "lien entrant, casse ignorée");
            t.Check(Links.LinksTo("il voit [[Gandalf]]", "Gändalf"), "lien entrant, accents ignorés");
            t.Check(!Links.LinksTo("il voit [[Gandalf]]", "Sam"), "pas de lien entrant");
        }
    }
}

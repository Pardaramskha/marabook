using System;
using System.Collections.Generic;
using System.Text;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C29 — import/export du dictionnaire personnel (18/09) : le
    /// JSON de Marabook fait l'aller-retour avec natures et définitions ;
    /// une liste de mots venue d'ailleurs (Word, LibreOffice, Hunspell,
    /// Scrivener) se lit en ignorant en-têtes et drapeaux ; l'import n'écrase
    /// jamais un mot présent ; la recherche par forme fléchie.</summary>
    public static class LexiconExchangeTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C29 — import/export du dictionnaire");

            // — JSON : aller-retour complet.
            var entries = new List<LexiconEntry>
            {
                new LexiconEntry { Word = "Marabout", Class = LexiconEntry.ClassProper, Gender = "m", Definition = "Un oiseau.", Note = "logo" },
                new LexiconEntry { Word = "elfique", Class = LexiconEntry.ClassAdjective, Feminine = "elfique" },
                new LexiconEntry { Word = "dragon", Class = LexiconEntry.ClassNoun, Gender = "m", Plural = "s" }
            };
            var json = LexiconExchange.ToJson(entries);
            var back = LexiconExchange.Parse(json);
            t.Equal(3, back.Count, "trois entrées relues");
            t.Equal("Un oiseau.", LexiconEntry.Find(back, "Marabout").Definition, "la définition fait l'aller-retour");
            t.Equal(LexiconEntry.ClassAdjective, LexiconEntry.Find(back, "elfique").Class, "la nature aussi");
            t.Equal("logo", LexiconEntry.Find(back, "Marabout").Note, "et la note");
            t.Equal(0, LexiconExchange.Parse("{\"autre\": 1}").Count, "un JSON étranger : rien");

            // — Liste de mots : tri français, doublons ôtés.
            var list = LexiconExchange.ToWordList(new List<LexiconEntry>
            {
                LexiconEntry.Simple("zèbre"), LexiconEntry.Simple("Élan"), LexiconEntry.Simple("abri"), LexiconEntry.Simple("abri")
            });
            t.Equal("abri\r\nÉlan\r\nzèbre\r\n", list, "un mot par ligne, tri français, sans doublon");
            t.Equal("", LexiconExchange.ToWordList(new List<LexiconEntry>()), "liste vide");

            // — Lecture d'une liste venue d'ailleurs.
            var foreign = "OOoUserDict1\nlang: fr\ntype: positive\n---\n# commentaire\n[Words]\nGandalf/S\nMordor=Mordor\n\n  Frodon  \nGandalf\n;fin\n";
            var read = LexiconExchange.ParseWordList(foreign);
            t.Equal(3, read.Count, "en-têtes, commentaires et section ignorés ; doublon ôté");
            t.Equal("Gandalf", read[0].Word, "drapeau Hunspell ôté");
            t.Equal("Mordor", read[1].Word, "remplacement LibreOffice ôté");
            t.Equal("Frodon", read[2].Word, "espaces de bord ôtés");
            t.Equal(LexiconEntry.ClassOther, read[0].Class, "un mot importé est « autre » : la forme exacte seule");
            var counted = LexiconExchange.ParseWordList("12\nmot");
            t.Equal(1, counted.Count, "compte Hunspell en tête ignoré");
            t.Equal("mot", counted[0].Word, "…le mot reste");
            t.Equal(2, LexiconExchange.ParseWordList("mot\n12").Count, "un nombre ailleurs qu'en tête est un mot");

            // — Encodages : BOM UTF-16 (Word), UTF-8, Windows-1252 sans BOM.
            var utf16 = Encoding.Unicode.GetPreamble();
            var body16 = Encoding.Unicode.GetBytes("élan\r\nzèbre");
            var bytes16 = new byte[utf16.Length + body16.Length];
            Array.Copy(utf16, bytes16, utf16.Length);
            Array.Copy(body16, 0, bytes16, utf16.Length, body16.Length);
            t.Equal("élan\r\nzèbre", LexiconExchange.ReadText(bytes16), "UTF-16 avec BOM (le .dic de Word)");
            t.Equal("élan", LexiconExchange.ReadText(new UTF8Encoding(true).GetBytes("élan")), "UTF-8 avec BOM");
            t.Equal("élan", LexiconExchange.ReadText(new UTF8Encoding(false).GetBytes("élan")), "UTF-8 sans BOM");
            t.Equal("élan", LexiconExchange.ReadText(Encoding.GetEncoding(1252).GetBytes("élan")), "Windows-1252 sans BOM (vieux .dic)");

            // — Import : les mots présents restent tels quels.
            var target = new List<LexiconEntry>
            {
                new LexiconEntry { Word = "Gandalf", Class = LexiconEntry.ClassProper, Definition = "Le mage." }
            };
            var added = LexiconExchange.Import(target, read);
            t.Equal(2, added, "deux mots nouveaux ajoutés");
            t.Equal(3, target.Count, "trois entrées au total");
            t.Equal("Le mage.", LexiconEntry.Find(target, "Gandalf").Definition, "l'entrée existante n'est pas écrasée");
            t.Equal(0, LexiconExchange.Import(target, read), "réimporter n'ajoute rien");

            // — Recherche par forme : le clic droit sur « dragons ».
            t.Check(LexiconEntry.FindByForm(entries, "dragons") == LexiconEntry.Find(entries, "dragon"), "pluriel → l'entrée");
            t.Check(LexiconEntry.FindByForm(entries, "MARABOUT") == LexiconEntry.Find(entries, "Marabout"), "casse ignorée");
            t.Check(LexiconEntry.FindByForm(entries, "elfiques") == LexiconEntry.Find(entries, "elfique"), "adjectif fléchi → l'entrée");
            t.Check(LexiconEntry.FindByForm(entries, "hobbit") == null, "mot inconnu : rien");
            t.Check(LexiconEntry.FindByForm(null, "dragon") == null && LexiconEntry.FindByForm(entries, "") == null, "liste ou mot absents : rien");
        }
    }
}

using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C31 — extraction de personnages (b49) : majuscules hors début
    /// de phrase, noms composés (« Jean Valjean », « Anne de Bretagne »),
    /// civilités ôtées, mots ordinaires écartés, fiches existantes exclues,
    /// seuil, tri, contexte.</summary>
    public static class NameExtractorTests
    {
        private const string Text =
            "Gandalf regarda Frodon. Frodon sourit à Gandalf, puis Gandalf partit.\n"
            + "Le soir, Jean Valjean arriva ; Jean Valjean dormait quand Jean Valjean rêva.\n"
            + "Anne de Bretagne salua le roi. Puis Anne de Bretagne et Anne de Bretagne encore.\n"
            + "Paris est belle. Paris dort. Il vit Paris.\n"
            + "Lundi, Lundi et Lundi encore. Oui, Oui, Oui !\n"
            + "Monsieur Dupont vint ; il salua Monsieur Dupont, et Monsieur Dupont répondit.\n"
            + "— Bonjour, dit Sam. Sam ? Sam !";

        public static void Run(Harness t)
        {
            t.Suite("C31 — extraction de personnages");

            var found = NameExtractor.Extract(Text, null);
            var names = new List<string>();
            foreach (var candidate in found) names.Add(candidate.Name);
            t.Check(names.Contains("Gandalf"), "Gandalf : trois fois, dont deux hors début de phrase");
            t.Check(!names.Contains("Frodon"), "Frodon : deux fois seulement, sous le seuil");
            t.Check(names.Contains("Jean Valjean"), "Jean Valjean : un seul nom composé");
            t.Check(!names.Contains("Jean") && !names.Contains("Valjean"), "…ses parts ne comptent pas pour elles-mêmes");
            t.Check(names.Contains("Anne de Bretagne"), "Anne de Bretagne : la particule est admise");
            t.Check(!names.Contains("Paris"), "Paris : trois fois mais deux en tête de phrase — écarté (une seule hors début)");
            t.Check(!names.Contains("Lundi") && !names.Contains("Oui"), "jours et interjections : écartés");
            t.Check(names.Contains("Dupont"), "« Monsieur Dupont » → Dupont");
            t.Check(!names.Contains("Monsieur Dupont"), "…la civilité ne fait pas partie du nom");
            t.Check(!names.Contains("Sam"), "Sam : toujours en tête de phrase ou après un tiret de dialogue — écarté");
            t.Check(!names.Contains("Bonjour"), "Bonjour : mot ordinaire");

            foreach (var candidate in found)
                if (candidate.Name == "Gandalf")
                {
                    t.Equal(3, candidate.Count, "le compte total de Gandalf (début de phrase compris)");
                    t.Check(candidate.Context.Contains("Gandalf"), "le contexte cite le nom");
                    t.Check(candidate.Context.Length <= 100, "le contexte est court");
                }
                else if (candidate.Name == "Jean Valjean")
                    t.Equal(3, candidate.Count, "Jean Valjean : trois occurrences");
                else if (candidate.Name == "Anne de Bretagne")
                    t.Equal(3, candidate.Count, "Anne de Bretagne : trois occurrences");

            // Tri : les plus fréquents d'abord, puis l'ordre alphabétique.
            for (var i = 1; i < found.Count; i++)
                t.Check(found[i - 1].Count >= found[i].Count, "tri par fréquence décroissante (" + found[i].Name + ")");

            // Les fiches existantes sont exclues, parts comprises.
            var known = NameExtractor.Extract(Text, new[] { "Gandalf", "Jean Valjean" });
            var knownNames = new List<string>();
            foreach (var candidate in known) knownNames.Add(candidate.Name);
            t.Check(!knownNames.Contains("Gandalf"), "Gandalf déjà en fiche : exclu");
            t.Check(!knownNames.Contains("Jean Valjean") && !knownNames.Contains("Jean"), "Jean Valjean déjà en fiche : exclu, Jean aussi");
            t.Check(knownNames.Contains("Anne de Bretagne"), "les autres restent");
            var lower = NameExtractor.Extract(Text, new[] { "GANDALF" });
            var lowerNames = new List<string>();
            foreach (var candidate in lower) lowerNames.Add(candidate.Name);
            t.Check(!lowerNames.Contains("Gandalf"), "fiche connue : casse ignorée");

            // Seuil.
            var loose = NameExtractor.Extract(Text, null, 1);
            var looseNames = new List<string>();
            foreach (var candidate in loose) looseNames.Add(candidate.Name);
            t.Check(looseNames.Contains("Frodon"), "seuil à 1 : Frodon passe");
            t.Check(looseNames.Contains("Paris"), "seuil à 1 : Paris aussi (une occurrence hors début suffit)");
            t.Equal(0, NameExtractor.Extract("", null).Count, "texte vide : rien");
            t.Equal(0, NameExtractor.Extract("rien que des minuscules ici", null, 1).Count, "sans majuscule : rien");

            // Début de phrase.
            t.Check(NameExtractor.IsSentenceStart("Bonjour. Gandalf", 9), "après un point");
            t.Check(NameExtractor.IsSentenceStart("Il dit : « Gandalf", 11), "après deux-points et guillemet");
            t.Check(NameExtractor.IsSentenceStart("ligne\n— Gandalf", 8), "après un tiret de dialogue en tête de ligne");
            t.Check(NameExtractor.IsSentenceStart("Gandalf", 0), "en tête de texte");
            t.Check(!NameExtractor.IsSentenceStart("il vit Gandalf", 7), "au cœur d'une phrase");
            t.Check(!NameExtractor.IsSentenceStart("il vit, Gandalf", 8), "après une virgule");

            // Civilités et particules.
            t.Equal("Dupont", NameExtractor.TrimOrdinary("Monsieur Dupont"), "civilité ôtée");
            t.Equal("Anne de Bretagne", NameExtractor.TrimOrdinary("Anne de Bretagne"), "particule au cœur gardée");
            t.Equal("Bretagne", NameExtractor.TrimOrdinary("De Bretagne"), "particule orpheline en tête ôtée");
            t.Check(NameExtractor.TrimOrdinary("Lundi") == null, "jour : rien");
            t.Check(NameExtractor.TrimOrdinary("Jean Bonjour") == null, "mot ordinaire au cœur : rien");
            t.Equal("gandalf", NameExtractor.Key("Gändalf"), "clé pliée");
        }
    }
}

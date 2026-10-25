using Marabook.Correction;

namespace Marabook.Tests
{
    /// <summary>C52 — la majuscule automatique en début de phrase (24/10,
    /// 1.0.6) : la lettre visée et sa capitale, accents compris.</summary>
    public static class AutoCapitalTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C52 — majuscule automatique en début de phrase (1.0.6)");
            Case(t, "bonjour ", 8, 0, 'B', "espace après le premier mot du paragraphe");
            Case(t, "élan", 4, -1, '\0', "en cours de frappe (lettre) : rien encore — la capitale attend la fin du mot");
            Case(t, "élan ", 5, 0, 'É', "…l'espace la pose, accent compris");
            Case(t, "élan,", 5, 0, 'É', "…une ponctuation aussi");
            Case(t, "e ", 2, 0, 'E', "une lettre seule suivie d'un espace");
            Case(t, "Il dort. puis ", 14, 9, 'P', "après un point");
            Case(t, "Il dort ! puis ", 15, 10, 'P', "après un point d'exclamation");
            Case(t, "Oui… non ", 9, 5, 'N', "après des points de suspension");
            Case(t, "Oui ? non ", 10, 6, 'N', "après une fine et un point d'interrogation");
            Case(t, "Il dort, puis ", 14, -1, '\0', "pas après une virgule");
            Case(t, "Il a dit : non ", 15, -1, '\0', "pas après un deux-points");
            Case(t, "« Viens ! » dit ", 16, 12, 'D', "après la fermeture d'une réplique : oui (Ctrl+Z refuse l'incise)");
            Case(t, "— bonjour ", 10, 2, 'B', "une réplique au tiret");
            Case(t, "l'été ", 6, 0, 'L', "l'apostrophe fait partie du mot : L'été");
            Case(t, "peut-être ", 10, 0, 'P', "le trait d'union aussi");
            Case(t, "2024 fut ", 9, -1, '\0', "un chiffre en tête : rien");
            Case(t, "Bonjour ", 8, -1, '\0', "déjà une capitale : rien");
            Case(t, "bonjour tout", 8, 0, 'B', "le caret après l'espace du premier mot, du texte après");
            Case(t, "bonjour tout", 7, -1, '\0', "le caret au bout du mot, sans séparateur tapé : rien");
            Case(t, "Il dort. (puis ", 15, 10, 'P', "une parenthèse ouverte se saute");
            Case(t, "", 0, -1, '\0', "vide : rien");
            Case(t, "ßen ", 4, -1, '\0', "une lettre sans capitale propre (ß) : rien");
        }

        private static void Case(Harness t, string flat, int caret, int expectedIndex, char expectedUpper, string label)
        {
            char upper;
            var index = AutoCapital.Find(flat, caret, out upper);
            t.Equal(expectedIndex, index, label + " — offset");
            if (expectedIndex >= 0) t.Equal(expectedUpper, upper, label + " — capitale");
        }
    }
}

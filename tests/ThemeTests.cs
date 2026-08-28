using UniversSale.View;

namespace UniversSale.Tests
{
    /// <summary>C12 — le thème (batch 34) : les deux palettes SE PARSENT.
    /// Theme.Switch avale toute erreur XAML pour garder l'application
    /// utilisable en style classique — ce filet rend la faute visible ici
    /// plutôt qu'en « tout blanc » chez l'utilisateur.</summary>
    public static class ThemeTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C12 — thème");
            var light = Theme.SelfCheck(false);
            t.Check(light == null, "la palette claire se parse" + (light == null ? "" : " — " + light));
            var dark = Theme.SelfCheck(true);
            t.Check(dark == null, "la palette sombre se parse" + (dark == null ? "" : " — " + dark));
            var accent = Theme.SelfCheck(false, "#E67E22");
            t.Check(accent == null, "un accent personnalisé se parse" + (accent == null ? "" : " — " + accent));
        }
    }
}

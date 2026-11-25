using System;
using System.Collections.Generic;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C93 — 2.0.0 (25/11) : les profils en « choix de difficulté » — chaque
    /// profil prédéfini a sa difficulté, sa couleur, son image et sept fonctionnalités ;
    /// « Moi-même » hérite des cases du profil que l'on quitte.</summary>
    public static class ProfilesTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C93 — profils : difficulté, image et liste de chaque profil, « Moi-même » hérite du profil quitté");
            var profileBefore = AppSettings.Profile;
            var hiddenBefore = new List<string>(AppSettings.HiddenFeatures);
            try
            {
                var writer = Profiles.Find(Profiles.Writer);
                var publisher = Profiles.Find(Profiles.Publisher);
                var everything = Profiles.Find(Profiles.Everything);
                var custom = Profiles.Find(Profiles.Custom);
                t.Check(writer.Difficulty == "Abordable" && publisher.Difficulty == "Laborieux" && everything.Difficulty == "Extrême" && custom.Difficulty == null,
                    "trois difficultés : Abordable, Laborieux, Extrême ; « Moi-même » n'en a pas");
                t.Check(writer.Image == "ecrivain" && publisher.Image == "editeur" && everything.Image == "cingle" && custom.Image == null, "chaque profil prédéfini nomme son image");
                t.Check(writer.Perks.Length == 7 && publisher.Perks.Length == 7 && everything.Perks.Length == 7 && custom.Perks.Length == 0, "…et liste sept fonctionnalités");
                int r, g, b;
                t.Check(Marabook.Model.ColorCodes.TryParse(writer.Color, out r, out g, out b) && Marabook.Model.ColorCodes.TryParse(publisher.Color, out r, out g, out b)
                    && Marabook.Model.ColorCodes.TryParse(everything.Color, out r, out g, out b), "…et la couleur de sa banderole, lisible");

                // — « Moi-même » hérite du profil quitté.
                AppSettings.Profile = null;
                AppSettings.HiddenFeatures.Clear();
                AppSettings.HiddenFeatures.Add(Profiles.Sprints);
                Profiles.Choose(Profiles.Custom);
                t.Check(AppSettings.HiddenFeatures.Count == 1 && AppSettings.HiddenFeatures.Contains(Profiles.Sprints), "sans profil avant lui, « Moi-même » garde ses cases");
                Profiles.Choose(Profiles.Custom);
                t.Check(AppSettings.HiddenFeatures.Count == 1, "…re-choisi, aussi");
                Profiles.Choose(Profiles.Writer);
                Profiles.Choose(Profiles.Custom);
                t.Check(AppSettings.HiddenFeatures.SetEquals(Profiles.HiddenBy(Profiles.Writer)) && !Profiles.Enabled(Profiles.BookFormat) && Profiles.Enabled(Profiles.Sheets) && Profiles.Enabled(Profiles.Sprints),
                    "venu d'Écrivain·e, « Moi-même » cache ce qu'Écrivain·e cachait, et rien d'autre");
                Profiles.Choose(Profiles.Publisher);
                Profiles.Choose(Profiles.Custom);
                t.Check(AppSettings.HiddenFeatures.SetEquals(Profiles.HiddenBy(Profiles.Publisher)) && !Profiles.Enabled(Profiles.Sheets) && Profiles.Enabled(Profiles.BookFormat),
                    "venu d'Éditeur·ice, ce qu'Éditeur·ice cachait");
                Profiles.Choose(Profiles.Everything);
                Profiles.Choose(Profiles.Custom);
                t.Check(AppSettings.HiddenFeatures.Count == 0, "venu de Cinglé·e, tout est coché");
            }
            finally
            {
                AppSettings.Profile = profileBefore;
                Profiles.LoadHidden(hiddenBefore);
            }
        }
    }
}

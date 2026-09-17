using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C25 — les natures de champ (b47 bis) : la liste, la
    /// normalisation (une nature inconnue vaut texte), les nombres avec
    /// unité, les notes, les listes, l'affichage, la fiche liée.</summary>
    public static class FieldKindTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C25 — natures de champ (b47 bis)");

            t.Equal(8, FieldKinds.All.Length, "huit natures");
            t.Equal("text", FieldKinds.Normalize(null), "null vaut texte");
            t.Equal("text", FieldKinds.Normalize("hologramme"), "une nature inconnue vaut texte");
            t.Equal("rating", FieldKinds.Normalize("rating"), "une nature connue reste");
            foreach (var kind in FieldKinds.All)
                t.Check(FieldKinds.Label(kind).Length > 0, "un libellé pour « " + kind + " »");

            double n;
            t.Check(FieldKinds.TryNumber("1,78 m", out n) && Math.Abs(n - 1.78) < 1e-9, "« 1,78 m » = 1.78");
            t.Check(FieldKinds.TryNumber("34", out n) && n == 34, "« 34 »");
            t.Check(FieldKinds.TryNumber("-12.5 °C", out n) && Math.Abs(n + 12.5) < 1e-9, "négatif décimal avec unité");
            t.Check(FieldKinds.TryNumber("12 000 habitants", out n) && n == 12000, "« 12 000 » avec espace de milliers");
            t.Check(!FieldKinds.TryNumber("environ vingt", out n), "des lettres ne sont pas un nombre");
            t.Check(!FieldKinds.TryNumber("", out n) && !FieldKinds.TryNumber(null, out n), "vide ou null");

            t.Equal(3, FieldKinds.RatingOf("3"), "note 3");
            t.Equal(5, FieldKinds.RatingOf("9"), "note bornée à 5");
            t.Equal(0, FieldKinds.RatingOf("-2"), "note plancher 0");
            t.Equal(0, FieldKinds.RatingOf("beaucoup"), "note illisible = 0");
            t.Equal("●●●○○", FieldKinds.RatingText(3), "les ronds");

            t.Equal("escrime|latin|cuisine", string.Join("|", FieldKinds.ListItems(" escrime , latin;cuisine, ").ToArray()), "liste rognée, vides écartés");
            t.Equal(0, FieldKinds.ListItems(null).Count, "liste vide");
            t.Equal("vivant, mort", FieldKinds.JoinOptions(new List<string> { "vivant", "mort" }), "options sur une ligne");

            var project = Project.CreateNew();
            var target = new BinderItem { Kind = ItemKind.Sheet, Title = "Léa" };
            project.Category(Project.KeySheets).Children.Add(target);
            project.RelinkParents();
            t.Equal("Léa", FieldKinds.Display(FieldKinds.Sheet, target.Id, project), "fiche liée affichée par son titre");
            t.Equal("(fiche disparue)", FieldKinds.Display(FieldKinds.Sheet, "nulle-part", project), "id mort");
            t.Equal("", FieldKinds.Display(FieldKinds.Sheet, "", project), "fiche liée vide");
            t.Check(FieldKinds.SheetOf(target.Id, project) == target, "SheetOf retrouve la fiche");
            t.Check(FieldKinds.SheetOf(target.Id, null) == null, "SheetOf sans projet");
            t.Equal("●●○○○", FieldKinds.Display(FieldKinds.Rating, "2", project), "note affichée en ronds");
            t.Equal("a · b", FieldKinds.Display(FieldKinds.List, "a, b", project), "liste affichée");
            t.Equal("1,78 m", FieldKinds.Display(FieldKinds.Number, "1,78 m", project), "un nombre s'affiche tel quel");
            t.Equal("", FieldKinds.Display("text", null, project), "null affiché vide");
            t.Check(!FieldKinds.Searchable(FieldKinds.Sheet) && FieldKinds.Searchable(FieldKinds.Rating), "la fiche liée n'est pas cherchable, la note oui");

            // — Le modèle : radar montré seulement activé ET à trois axes.
            var template = new SheetTemplate { Radar = true };
            t.Check(!template.ShowsRadar, "radar activé sans axe : rien à montrer");
            template.RadarAxes.Add(new RadarAxis { Name = "A" });
            template.RadarAxes.Add(new RadarAxis { Name = "B" });
            t.Check(!template.ShowsRadar, "deux axes : pas une toile");
            template.RadarAxes.Add(new RadarAxis { Name = "C" });
            t.Check(template.ShowsRadar, "trois axes : montré");
            template.Radar = false;
            t.Check(!template.ShowsRadar, "désactivé : caché, les axes restent");
            var copy = template.Clone();
            t.Equal(3, copy.RadarAxes.Count, "le clone copie les axes");
            t.Check(copy.FindAxis(template.RadarAxes[1].Id) != null && copy.RadarAxes[1] != template.RadarAxes[1], "…en profondeur, ids gardés");
            var field = new SheetField { Kind = FieldKinds.Choice };
            field.Options.Add("x");
            var fieldCopy = field.Clone();
            fieldCopy.Options.Add("y");
            t.Equal(1, field.Options.Count, "le clone d'un champ copie ses options");
        }
    }
}

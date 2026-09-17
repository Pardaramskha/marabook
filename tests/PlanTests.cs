using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C14 — les plans (batch 35) : colonnes, éléments et notes,
    /// l'échelle d'intensité, le profil du graphique, les liens vers les
    /// écrits et les conteneurs.</summary>
    public static class PlanTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C14 — plans");
            Intensity(t);
            Profile(t);
            Links(t);
        }

        private static void Intensity(Harness t)
        {
            t.Equal(5, PlanIntensity.Labels.Length, "cinq niveaux");
            t.Equal("Explications, slice of life", PlanIntensity.Label(1), "le plus bas");
            t.Equal("Pinnacle", PlanIntensity.Label(5), "le plus haut");
            t.Equal(1, PlanIntensity.Clamp(0), "borné en bas");
            t.Equal(5, PlanIntensity.Clamp(9), "borné en haut");
            t.Equal("Pinnacle", PlanIntensity.Label(99), "un label hors échelle se borne");
            var note = new PlanEntry { Kind = PlanEntry.KindNote, Text = "post-it" };
            t.Check(note.IsNote && !note.IsElement, "une note n'est pas un élément");
            t.Check(new PlanEntry().IsElement, "par défaut : un élément");
        }

        private static void Profile(Harness t)
        {
            var plan = new PlanInfo();
            var a = new PlanColumn { Title = "Ouverture" };
            a.Entries.Add(new PlanEntry { Text = "Le village", Intensity = 1 });
            a.Entries.Add(new PlanEntry { Text = "L'incendie", Intensity = 4 });
            a.Entries.Add(new PlanEntry { Kind = PlanEntry.KindNote, Text = "revoir le rythme", Intensity = 5 });
            var b = new PlanColumn { Title = "Voyage" };
            b.Entries.Add(new PlanEntry { Text = "La route", Intensity = 2 });
            var c = new PlanColumn { Title = "Vide" };
            plan.Columns.Add(a);
            plan.Columns.Add(b);
            plan.Columns.Add(c);
            t.Equal(4, a.PeakIntensity(), "le pic ignore les notes");
            t.Equal(2.5, a.MeanIntensity(), "la moyenne des éléments seuls");
            t.Equal(0, c.PeakIntensity(), "colonne sans élément : 0");
            t.Equal(0.0, c.MeanIntensity(), "…moyenne 0");
            var profile = PlanIntensity.Profile(plan);
            t.Equal(3, profile.Count, "une valeur par colonne");
            t.Check(profile[0] == 4 && profile[1] == 2 && profile[2] == 0, "le profil suit les pics");
            t.Equal(0, PlanIntensity.Profile(null).Count, "plan nul : profil vide");
        }

        private static void Links(Harness t)
        {
            var project = Project.CreateNew();
            var text = project.Category(Project.KeyWritings).Children[0];
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo() };
            project.Category(Project.KeyWritings).Children.Add(book);
            var plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan du roman", Plan = new PlanInfo { LinkedItemId = book.Id } };
            plan.Plan.Columns.Add(new PlanColumn { Title = "Chapitre 1", LinkedTextId = text.Id });
            project.Category(Project.KeyPlans).Children.Add(plan);
            project.RelinkParents();
            t.Check(project.Category(Project.KeyPlans) != null, "un projet neuf a sa racine Plans");
            var roots = project.Roots;
            t.Check(roots.IndexOf(project.Category(Project.KeyPlans)) == roots.IndexOf(project.Category(Project.KeySheets)) + 1,
                "Plans suit Fiches");
            t.Check(project.PlanForText(text.Id) == plan, "l'écrit relié à une colonne retrouve son plan");
            t.Check(project.PlanForText("nope") == null, "un écrit sans colonne : aucun plan");
            t.Check(project.PlanForContainer(book.Id) == plan, "le livre relié retrouve son plan");
            t.Check(plan.Plan.ColumnOf(text.Id).Title == "Chapitre 1", "la colonne d'un écrit");
            t.Check(plan.Plan.ColumnOf(null) == null, "sans id : aucune colonne");
            t.Check(!plan.CanHaveChildren && !plan.IsContainer, "un plan n'a pas d'enfants dans la Pile");
            t.Equal("Colonne 2", plan.Plan.NextColumnTitle(), "le mot par défaut des colonnes");
            plan.Plan.ColumnWord = "chapitre";
            t.Equal("chapitre 2", plan.Plan.NextColumnTitle(), "le mot choisi numérote les nouvelles colonnes");
            plan.Plan.ColumnWord = "  ";
            t.Equal("Colonne 2", plan.Plan.NextColumnTitle(), "un mot vide retombe sur le défaut");
        }
    }
}

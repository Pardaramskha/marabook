using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C24 — la présence et l'évolution (batch 47) : les noms d'une
    /// fiche (titre, nom, prénom, alias), le comptage en mots entiers,
    /// accents pliés et casse gardée, les noms longs qui masquent les
    /// courts, l'ordre du récit, les étapes triées.</summary>
    public static class PresenceTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C24 — présence et évolution (b47)");

            var project = Project.CreateNew();
            var template = project.CharacterTemplate();
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Léa Martin", TemplateId = template.Id };
            foreach (var field in template.Fields)
            {
                if (field.Name == "Prénom") sheet.FieldValues[field.Id] = "Léa";
                if (field.Name == "Nom") sheet.FieldValues[field.Id] = "Martin";
                if (field.Name == "Alias") sheet.FieldValues[field.Id] = "la Rousse, Petite ; L";
            }
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();

            // — Les noms : titre, prénom, nom, alias découpés ; « L » (une
            //   lettre) écarté ; les plus longs d'abord ; pliés, casse gardée.
            var names = Presence.NamesOf(sheet, template);
            t.Equal("Lea Martin|la Rousse|Martin|Petite|Lea", string.Join("|", names.ToArray()), "les noms, les plus longs d'abord, accents pliés");

            // — Le comptage : mots entiers, casse respectée, le nom complet
            //   masque le prénom, accents indifférents dans le texte.
            t.Equal(1, Presence.CountIn("Léa Martin entra.", names), "« Léa Martin » = 1 (pas aussi « Léa » ni « Martin »)");
            t.Equal(2, Presence.CountIn("Lea sourit à Martin.", names), "prénom et nom séparés = 2, sans accent dans le texte");
            t.Equal(0, Presence.CountIn("La martingale de Léandre.", names), "« martingale » et « Léandre » ne comptent pas (mots entiers)");
            t.Equal(0, Presence.CountIn("la rousse est une couleur", names), "casse respectée : « la rousse » n'est pas « la Rousse »");
            t.Equal(1, Presence.CountIn("« Petite ! » cria-t-il.", names), "un alias entre guillemets et ponctuation compte");
            t.Equal(1, Presence.CountIn("l'Amie de Léa.", names), "après une élision");
            t.Equal(0, Presence.CountIn("", names), "texte vide");
            t.Equal(0, Presence.CountIn("Léa", new List<string>()), "sans nom, rien");

            // — L'ordre du récit : les écrits de la racine Écrits seulement,
            //   pages extra exclues, corbeille exclue.
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear(); // le « Nouvel écrit » du projet neuf
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Tome 1", Book = new BookInfo() };
            var one = new BinderItem { Kind = ItemKind.Text, Title = "Un", Document = TextDocument.FromPlainText("Léa dort. Léa rêve.") };
            var two = new BinderItem { Kind = ItemKind.Text, Title = "Deux", Document = TextDocument.FromPlainText("Personne.") };
            var three = new BinderItem { Kind = ItemKind.Text, Title = "Trois", Document = TextDocument.FromPlainText("Martin et la Rousse.") };
            var extra = new BinderItem { Kind = ItemKind.Text, Title = "Dédicace", IsExtraPage = true, Document = TextDocument.FromPlainText("Pour Léa.") };
            book.Children.Add(extra);
            book.Children.Add(one);
            book.Children.Add(two);
            writings.Children.Add(book);
            writings.Children.Add(three);
            var trashed = new BinderItem { Kind = ItemKind.Text, Title = "Jeté", Document = TextDocument.FromPlainText("Léa Léa Léa") };
            project.Trash.Children.Add(trashed);
            project.RelinkParents();
            var order = Presence.Writings(project);
            t.Equal("Un,Deux,Trois", Titles(order), "les écrits dans l'ordre de la Pile, sans page extra ni corbeille");

            var rows = Presence.Of(sheet, template, project);
            t.Equal(2, rows.Count, "deux écrits nomment la fiche");
            t.Equal("Un", rows[0].Text.Title, "le premier dans l'ordre du récit");
            t.Equal(2, rows[0].Count, "deux occurrences dans « Un »");
            t.Equal("Tome 1", rows[0].Book == null ? "" : rows[0].Book.Title, "le livre du premier");
            t.Equal("Trois", rows[1].Text.Title, "puis « Trois »");
            t.Equal(2, rows[1].Count, "nom + alias dans « Trois »");
            t.Check(rows[1].Book == null, "« Trois » est hors livre");

            // — Qui est présent dans un écrit : les fiches filtrées, les plus
            //   présentes d'abord, la corbeille ignorée.
            var other = new BinderItem { Kind = ItemKind.Sheet, Title = "Martin", TemplateId = template.Id };
            project.Category(Project.KeySheets).Children.Add(other);
            var ghost = new BinderItem { Kind = ItemKind.Sheet, Title = "Léa", TemplateId = template.Id };
            project.Trash.Children.Add(ghost);
            project.RelinkParents();
            var present = Presence.In(three, project, null);
            t.Equal("Léa Martin,Martin", Titles(present), "dans « Trois » : Léa Martin (2) avant Martin (1), la fiche jetée ignorée");
            t.Equal(0, Presence.In(three, project, delegate(BinderItem s) { return false; }).Count, "le filtre écarte tout");
            t.Equal(0, Presence.In(two, project, null).Count, "« Deux » ne nomme personne");

            // — Les étapes : triées par l'ordre du récit, les libres et les
            //   orphelines après, dans leur ordre de saisie ; StepIn.
            sheet.Evolution.Add(new EvolutionEntry { TextId = three.Id, Note = "se révèle" });
            sheet.Evolution.Add(new EvolutionEntry { Note = "libre 1" });
            sheet.Evolution.Add(new EvolutionEntry { TextId = one.Id, Note = "dort" });
            sheet.Evolution.Add(new EvolutionEntry { TextId = "disparu", Note = "orpheline" });
            sheet.Evolution.Add(new EvolutionEntry { Note = "libre 2" });
            var steps = Presence.OrderedSteps(sheet, project);
            t.Equal("dort|se révèle|libre 1|orpheline|libre 2", Notes(steps), "étapes dans l'ordre du récit, puis les autres dans l'ordre de saisie");
            t.Equal("se révèle", Presence.StepIn(sheet, three.Id).Note, "l'étape d'un écrit");
            t.Check(Presence.StepIn(sheet, two.Id) == null, "pas d'étape pour « Deux »");
            sheet.Evolution.Add(new EvolutionEntry { TextId = two.Id, Note = "   " });
            t.Check(Presence.StepIn(sheet, two.Id) == null, "une note vide ne compte pas");
            t.Equal(0, Presence.OrderedSteps(null, project).Count, "sans fiche, rien");
        }

        private static string Titles(List<BinderItem> items)
        {
            var titles = new List<string>();
            foreach (var item in items) titles.Add(item.Title);
            return string.Join(",", titles.ToArray());
        }

        private static string Titles(List<PresenceRow> rows)
        {
            var titles = new List<string>();
            foreach (var row in rows) titles.Add(row.Text.Title);
            return string.Join(",", titles.ToArray());
        }

        private static string Notes(List<EvolutionEntry> steps)
        {
            var notes = new List<string>();
            foreach (var step in steps) notes.Add(step.Note);
            return string.Join("|", notes.ToArray());
        }
    }
}

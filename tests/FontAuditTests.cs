using System;
using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C48 — l'inventaire des polices (07/10) : les familles
    /// employées (styles, runs, notes), distinctes sans casse, et celles qui
    /// manquent à l'installation.</summary>
    public static class FontAuditTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C48 — polices employées et manquantes");

            var project = Project.CreateNew();
            project.Styles.Styles.Add(new ParagraphStyle { Id = "x", Name = "X", FontFamily = "Garamond Premier" });
            project.Styles.Styles.Add(new ParagraphStyle { Id = "y", Name = "Y", FontFamily = " garamond premier " });
            project.Styles.Styles.Add(new ParagraphStyle { Id = "z", Name = "Z", FontFamily = "" });
            var writings = project.Category(Project.KeyWritings);
            var text = new BinderItem { Title = "T", Kind = ItemKind.Text, Parent = writings };
            writings.Children.Add(text);
            var paragraph = new TextParagraph();
            paragraph.Runs.Add(new TextRun { Text = "a", FontFamily = "EB Garamond" });
            paragraph.Runs.Add(new TextRun { Text = "b" });
            paragraph.Runs.Add(new TextRun { Text = "c", FontFamily = "Papyrus" });
            text.Document.Paragraphs.Add(paragraph);
            var note = new Footnote { Id = "n1", Text = "note" };
            note.Runs.Add(new TextRun { Text = "note", FontFamily = "Comic Sans MS" });
            text.Document.Footnotes.Add(note);

            var used = FontAudit.UsedFamilies(project);
            t.Check(used.Contains("Times New Roman"), "le corps par défaut est employé");
            t.Check(used.Contains("Garamond Premier") && !used.Contains(" garamond premier "), "un style : une famille, dégraissée, une seule fois sans casse");
            t.Check(used.Contains("EB Garamond") && used.Contains("Papyrus"), "les runs mis en forme comptent");
            t.Check(used.Contains("Comic Sans MS"), "les notes de bas de page comptent");
            t.Check(!used.Contains(""), "jamais de nom vide");
            t.Check(used.IndexOf("Garamond Premier") < used.IndexOf("EB Garamond"), "les styles d'abord, puis les textes");

            var installed = new List<string> { "times new roman", "EB Garamond", " Papyrus " };
            var missing = FontAudit.Missing(project, installed);
            t.Equal(2, missing.Count, "deux familles manquent");
            t.Equal("Garamond Premier", missing[0], "la première manquante, dans l'ordre de rencontre");
            t.Equal("Comic Sans MS", missing[1], "la seconde");
            t.Equal(0, FontAudit.Missing(Project.CreateNew(), new List<string> { "Times New Roman" }).Count, "projet neuf, Times installée : rien ne manque");
            t.Equal(0, FontAudit.UsedFamilies(null).Count, "projet nul : rien");
            t.Check(FontAudit.Missing(project, null).Count == used.Count, "sans liste installée : tout manque");
        }
    }
}

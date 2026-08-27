using System;
using System.Collections.Generic;
using System.Text;
using UniversSale.Model;
using UniversSale.View;

namespace UniversSale.Tests
{
    /// <summary>C10 — le dialecte Markdown des fiches (batch 31) : le parseur
    /// de blocs porté de Markdown We Go (titres, filets, citations, listes,
    /// cases, tableaux, code) et la couche en ligne (gras, italique, barré,
    /// souligné, code, liens, [[wiki]]), plus les catégories de fiches (le
    /// modèle et sa migration). Le RENDU WPF reste une couche mince non
    /// testée ici — le modèle de blocs est le contrat.</summary>
    public static class MarkdownTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C10 — markdown des fiches et catégories");
            Blocks(t);
            Inlines(t);
            TasksAndCode(t);
            Tables(t);
            CategoriesSeed(t);
            CategoriesMigration(t);
            CategoryOfSheet(t);
        }

        // ------------------------------------------------------------ blocs

        private static void Blocks(Harness t)
        {
            var blocks = MarkdownDialect.Parse(
                "# Titre\n\nUn paragraphe\nsur deux lignes.\n\n---\n\n"
                + "> citée\n> encore\n\n- puce\n  - imbriquée\n1. un\n2. deux");
            t.Equal(MdBlockKind.Heading, blocks[0].Kind, "titre reconnu");
            t.Equal(1, blocks[0].HeadingLevel, "niveau 1");
            t.Equal("Titre", blocks[0].Inlines[0].Text, "texte du titre");
            t.Equal(MdBlockKind.Paragraph, blocks[1].Kind, "paragraphe");
            t.Check(blocks[1].Inlines.Count == 3
                && blocks[1].Inlines[1].Text == "\n",
                "les deux lignes du paragraphe sont jointes par un saut");
            t.Equal(MdBlockKind.Rule, blocks[2].Kind, "filet ---");
            t.Equal(MdBlockKind.Quote, blocks[3].Kind, "citation");
            t.Equal(2, blocks[3].QuoteLines.Count, "deux lignes citées");
            t.Equal(MdBlockKind.ListItem, blocks[4].Kind, "puce");
            t.Equal(0, blocks[4].Indent, "au premier niveau");
            t.Equal(1, blocks[5].Indent, "l'imbriquée est au second (2 espaces)");
            t.Check(blocks[6].Ordered && blocks[6].Number == 1, "1. numérotée");
            t.Check(blocks[7].Ordered && blocks[7].Number == 2, "2. numérotée");

            // Titres 2 à 6, fermetures optionnelles ##.
            var levels = MarkdownDialect.Parse("###### six ######");
            t.Equal(6, levels[0].HeadingLevel, "niveau 6, dièses de clôture avalés");
        }

        // --------------------------------------------------------- en ligne

        private static void Inlines(Harness t)
        {
            var blocks = MarkdownDialect.Parse(
                "Du **gras avec *italique* dedans**, du ~~barré~~, du <u>souligné</u>.");
            var inlines = blocks[0].Inlines;
            var bold = Find(inlines, "gras avec ");
            t.Check(bold != null && bold.Bold && !bold.Italic, "gras seul");
            var nested = Find(inlines, "italique");
            t.Check(nested != null && nested.Bold && nested.Italic,
                "l'italique DANS le gras cumule les deux styles");
            var strike = Find(inlines, "barré");
            t.Check(strike != null && strike.Strike, "barré ~~ ~~");
            var under = Find(inlines, "souligné");
            t.Check(under != null && under.Underline, "souligné <u>");

            var links = MarkdownDialect.Parse(
                "Voir [le site](https://exemple.fr) et [[Kaladin]].");
            var link = Find(links[0].Inlines, "le site");
            t.Check(link != null && link.LinkUrl == "https://exemple.fr",
                "lien [texte](url)");
            var wiki = Find(links[0].Inlines, "Kaladin");
            t.Check(wiki != null && wiki.WikiTarget == "Kaladin",
                "lien [[wiki]] — la navigation de fiches");

            // Les * collés à un mot ne déclenchent pas l'italique (règle MWG).
            var plain = MarkdownDialect.Parse("2*3*4 vaut 24.");
            t.Check(Find(plain[0].Inlines, "3") == null,
                "2*3*4 : pas d'italique parasite (garde-fous de mot)");

            var code = MarkdownDialect.Parse("Le `code **brut**` reste brut.");
            var span = Find(code[0].Inlines, "code **brut**");
            t.Check(span != null && span.Code,
                "un `code` en ligne n'interprète pas son contenu");
        }

        // ------------------------------------------------- cases et blocs code

        private static void TasksAndCode(Harness t)
        {
            var source = "- [ ] ouvrir\n- [x] fermée\n```\n- [ ] pas une case\n```";
            var blocks = MarkdownDialect.Parse(source);
            t.Check(blocks[0].IsTask && !blocks[0].TaskChecked
                && blocks[0].TaskIndex == 0, "case ouverte, index 0");
            t.Check(blocks[1].IsTask && blocks[1].TaskChecked
                && blocks[1].TaskIndex == 1, "case cochée, index 1");
            t.Equal(MdBlockKind.Code, blocks[2].Kind,
                "le bloc ``` est littéral");
            t.Equal("- [ ] pas une case", blocks[2].CodeText,
                "son contenu n'est pas interprété");

            // FindTask rend la POSITION du caractère d'état — la bascule au
            // clic dans l'aperçu écrit à cet endroit précis de la source.
            var p0 = MarkdownDialect.FindTask(source, 0);
            var p1 = MarkdownDialect.FindTask(source, 1);
            t.Equal(' ', source[p0], "tâche 0 : la position pointe l'espace");
            t.Equal('x', source[p1], "tâche 1 : la position pointe le x");
            t.Equal(-1, MarkdownDialect.FindTask(source, 2),
                "la case du bloc de code n'est PAS comptée");

            var toggled = source.Substring(0, p0) + "x" + source.Substring(p0 + 1);
            t.Check(MarkdownDialect.Parse(toggled)[0].TaskChecked,
                "bascule écrite → la case se relit cochée");
        }

        // ---------------------------------------------------------- tableaux

        private static void Tables(Harness t)
        {
            var blocks = MarkdownDialect.Parse(
                "| Nom | Âge |\n|:---:|---:|\n| Kaladin | 20 |\n| Shallan | 17 |");
            t.Equal(MdBlockKind.Table, blocks[0].Kind, "tableau reconnu");
            var table = blocks[0];
            t.Equal(2, table.TableHeader.Count, "deux colonnes");
            t.Equal(1, table.TableAligns[0], ":---: = centré");
            t.Equal(2, table.TableAligns[1], "---: = droite");
            t.Equal(2, table.TableRows.Count, "deux rangées");
            t.Equal("Kaladin", table.TableRows[0][0][0].Text, "cellule 1,1");

            // Une rangée | sans ligne de séparation reste un paragraphe.
            var not = MarkdownDialect.Parse("| a | b |\nrien");
            t.Equal(MdBlockKind.Paragraph, not[0].Kind,
                "pas de séparateur : pas un tableau");
        }

        // ------------------------------------------------------- catégories

        private static void CategoriesSeed(Harness t)
        {
            var project = Project.CreateNew();
            t.Equal(7, project.SheetCategories.Count,
                "un projet neuf porte les sept catégories livrées");
            t.Equal(7, project.Templates.Count, "et leurs sept modèles");
            foreach (var category in project.SheetCategories)
                t.Check(project.FindTemplate(category.TemplateId) != null,
                    "catégorie « " + category.Name + " » : modèle de base présent");
            // Le Personnage est GROUPÉ : Infos + Physique (batch 31).
            var character = project.FindTemplate(
                project.SheetCategories[0].TemplateId);
            var groups = new HashSet<string>();
            foreach (var field in character.Fields) groups.Add(field.Group);
            t.Check(groups.Contains("Infos") && groups.Contains("Physique"),
                "le modèle Personnage porte les groupes Infos et Physique");
        }

        private static void CategoriesMigration(Harness t)
        {
            // Un projet d'AVANT la v11 : deux modèles (l'un porte un nom de
            // catégorie livrée, l'autre non), une fiche sur chacun, aucune
            // catégorie. La migration doit adopter « Lieu » tel quel (mêmes
            // champs, mêmes ids — les valeurs des fiches survivent), créer
            // les six autres, et faire du modèle inconnu sa propre catégorie.
            var project = new Project();
            project.Roots.Add(new BinderItem
            {
                Kind = ItemKind.Category,
                Title = "Fiches",
                CategoryKey = Project.KeySheets,
                Id = Project.KeySheets
            });
            var oldPlace = new SheetTemplate { Name = "Lieu" };
            var oldField = new SheetField { Name = "Région" };
            oldPlace.Fields.Add(oldField);
            var custom = new SheetTemplate { Name = "Vaisseau" };
            project.Templates.Add(oldPlace);
            project.Templates.Add(custom);
            var placeSheet = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = "Kholinar",
                TemplateId = oldPlace.Id
            };
            placeSheet.FieldValues[oldField.Id] = "Alethkar";
            var customSheet = new BinderItem
            {
                Kind = ItemKind.Sheet,
                Title = "Le Vindicateur",
                TemplateId = custom.Id
            };
            project.Category(Project.KeySheets).Children.Add(placeSheet);
            project.Category(Project.KeySheets).Children.Add(customSheet);
            project.RelinkParents();

            project.EnsureSheetCategories();

            t.Equal(8, project.SheetCategories.Count,
                "7 catégories livrées + 1 personnalisée (Vaisseau)");
            SheetCategory place = null, ship = null;
            foreach (var category in project.SheetCategories)
            {
                if (category.Name == "Lieu") place = category;
                if (category.Name == "Vaisseau") ship = category;
            }
            t.Check(place != null && place.TemplateId == oldPlace.Id,
                "« Lieu » ADOPTE le modèle existant (les fiches gardent leurs valeurs)");
            t.Check(ship != null && ship.TemplateId == custom.Id,
                "le modèle inconnu devient sa propre catégorie");
            t.Equal(place.Id, placeSheet.CategoryId,
                "la fiche Lieu rejoint la catégorie de son modèle");
            t.Equal(ship.Id, customSheet.CategoryId,
                "la fiche Vaisseau aussi");
            t.Equal("Alethkar", placeSheet.FieldValues[oldField.Id],
                "les valeurs de champs ont survécu à la migration");

            // Idempotence : un second appel ne crée RIEN de plus.
            project.EnsureSheetCategories();
            t.Equal(8, project.SheetCategories.Count,
                "la migration est idempotente");
        }

        private static void CategoryOfSheet(Harness t)
        {
            var project = Project.CreateNew();
            var character = project.SheetCategories[0];
            var place = project.SheetCategories[1];
            var sheet = new BinderItem
            {
                Kind = ItemKind.Sheet,
                CategoryId = place.Id,
                TemplateId = character.TemplateId // modèle ≠ base de sa catégorie
            };
            t.Equal(place, project.SheetCategoryOf(sheet),
                "la catégorie PROPRE de la fiche prime sur celle du modèle");
            t.Check(sheet.TemplateId != place.TemplateId,
                "…et le modèle divergent est détectable (la puce des cartes)");
        }

        private static MdInline Find(List<MdInline> inlines, string text)
        {
            foreach (var inline in inlines)
                if (inline.Text == text) return inline;
            return null;
        }
    }
}

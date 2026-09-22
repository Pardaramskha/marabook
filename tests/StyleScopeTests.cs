using System;
using System.Collections.Generic;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C34 — les styles à portée et le séparateur de scène (22/09) :
    /// un style vaut pour un écrit selon sa portée ; le séparateur d'un livre
    /// remplace le global sous le même id ; les anciens réglages du projet
    /// migrent en style ; les styles globaux se synchronisent avec les
    /// réglages (poussés d'un vieux projet personnalisé, tirés ensuite).</summary>
    public static class StyleScopeTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C34 — styles à portée, séparateur, styles globaux");
            Scopes(t);
            Separator(t);
            Sync(t);
        }

        private static Project Fixture(out BinderItem book, out BinderItem chapter, out BinderItem loose)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            loose = writings.Children[0];
            book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo() };
            chapter = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre" };
            chapter.Document = TextDocument.FromPlainText("Texte.");
            book.Children.Add(chapter);
            writings.Children.Add(book);
            project.RelinkParents();
            project.Styles.Styles.Add(new ParagraphStyle { Id = "livre-style", Name = "Du livre", Scope = ParagraphStyle.ScopeBook, OwnerId = book.Id });
            project.Styles.Styles.Add(new ParagraphStyle { Id = "doc-style", Name = "Du document", Scope = ParagraphStyle.ScopeDocument, OwnerId = chapter.Id });
            return project;
        }

        private static void Scopes(Harness t)
        {
            BinderItem book, chapter, loose;
            var project = Fixture(out book, out chapter, out loose);
            var forChapter = Ids(project.Styles.VisibleFor(chapter));
            t.Check(forChapter.Contains("body") && forChapter.Contains("livre-style") && forChapter.Contains("doc-style") && !forChapter.Contains(StyleSheet.SeparatorId),
                "le chapitre voit les globaux, le style de son livre, le sien — jamais le séparateur");
            var forLoose = Ids(project.Styles.VisibleFor(loose));
            t.Check(forLoose.Contains("body") && !forLoose.Contains("livre-style") && !forLoose.Contains("doc-style"),
                "un écrit hors livre ne voit que les globaux");
            var forBook = Ids(project.Styles.VisibleFor(book));
            t.Check(forBook.Contains("livre-style") && !forBook.Contains("doc-style"), "le livre lui-même voit ses styles de livre");
            t.Check(project.Styles.Find("body").IsGlobal && !project.Styles.Find("livre-style").IsGlobal, "IsGlobal suit la portée");
            t.Check(StyleSheet.CreateDefault().Find(StyleSheet.SeparatorId).IsSeparator, "la feuille par défaut porte un séparateur");
        }

        private static void Separator(Harness t)
        {
            BinderItem book, chapter, loose;
            var project = Fixture(out book, out chapter, out loose);
            var global = project.Styles.SeparatorFor(chapter);
            t.Check(global.Id == StyleSheet.SeparatorId && global.Content == "***", "sans séparateur de livre : le global, « *** »");
            t.Check(ReferenceEquals(project.Styles.EffectiveFor(chapter), project.Styles), "…et la feuille effective est la feuille elle-même (pas de copie)");

            var own = new ParagraphStyle { Id = "sep-roman", Name = "Séparateur du roman", Content = "~ ~ ~", Scope = ParagraphStyle.ScopeBook, OwnerId = book.Id, FontSize = 24 };
            project.Styles.Styles.Add(own);
            t.Check(ReferenceEquals(project.Styles.SeparatorFor(chapter), own), "le livre a le sien : c'est lui pour ses écrits");
            t.Check(ReferenceEquals(project.Styles.SeparatorFor(book), own), "…et pour le livre lui-même (compilation, PDF)");
            t.Check(project.Styles.SeparatorFor(loose).Id == StyleSheet.SeparatorId, "un écrit hors livre garde le global");
            var effective = project.Styles.EffectiveFor(chapter);
            var swapped = effective.Find(StyleSheet.SeparatorId);
            t.Check(!ReferenceEquals(effective, project.Styles) && swapped.Content == "~ ~ ~" && swapped.FontSize == 24,
                "la feuille effective du chapitre met le séparateur du livre sous l'id « separator »");
            t.Check(effective.Find("body") == project.Styles.Find("body"), "…les autres styles sont les mêmes objets");
            t.Equal(project.Styles.Styles.Count - 1, effective.Styles.Count, "…un style de moins : le global remplacé");

            // Migration : une feuille sans séparateur en reçoit un depuis les
            // anciens réglages du projet (separatorText/Font/SizePt).
            var bare = new StyleSheet();
            bare.Styles.Add(StyleSheet.CreateDefault().Body);
            var migrated = bare.EnsureSeparator("· · ·", "Georgia", 14);
            t.Check(migrated.Id == StyleSheet.SeparatorId && migrated.Content == "· · ·" && migrated.FontFamily == "Georgia" && Math.Abs(migrated.FontSize - 14 * 4.0 / 3.0) < 0.01,
                "les anciens réglages du projet migrent en style séparateur");
            t.Check(ReferenceEquals(bare.EnsureSeparator("xxx", null, 0), migrated), "…une seule fois");
        }

        private static void Sync(Harness t)
        {
            var savedSheet = AppSettings.GlobalStyles;
            var savedStamp = AppSettings.GlobalStylesStamp;
            var savedPersist = GlobalStyles.Persist;
            GlobalStyles.Persist = false; // jamais settings.json depuis les tests
            try
            {
                // — Premier projet d'avant la v28 : « Corps » personnalisé.
                AppSettings.GlobalStyles = null;
                AppSettings.GlobalStylesStamp = "";
                var old = Project.CreateNew();
                old.Styles.Find("body").FontFamily = "Garamond";
                old.Styles.Find("title1").Scope = ParagraphStyle.ScopeBook; // pas global : ne monte pas
                old.Styles.Find("title1").OwnerId = "x";
                var changed = GlobalStyles.Sync(old);
                t.Check(AppSettings.GlobalStyles != null && AppSettings.GlobalStyles.Find("body").FontFamily == "Garamond",
                    "un projet jamais synchronisé pousse son « Corps » personnalisé dans les réglages");
                t.Check(old.GlobalStylesStamp.Length > 0 && old.GlobalStylesStamp == AppSettings.GlobalStylesStamp, "…et prend l'empreinte");
                t.Check(!changed && old.Styles.Find("body").FontFamily == "Garamond", "…sans rien perdre");

                // — Un second vieux projet aux styles par défaut : il TIRE le Corps
                //   personnalisé, sans écraser les réglages.
                var plain = Project.CreateNew();
                changed = GlobalStyles.Sync(plain);
                t.Check(changed && plain.Styles.Find("body").FontFamily == "Garamond", "un projet resté au défaut reprend le « Corps » des réglages");
                t.Check(AppSettings.GlobalStyles.Find("body").FontFamily == "Garamond", "…qui n'ont pas bougé");

                // — Édition poussée depuis un projet : nouvelle empreinte, l'autre
                //   projet la voit à sa prochaine ouverture.
                var stamp = AppSettings.GlobalStylesStamp;
                plain.Styles.Find("body").FontSize = 20;
                GlobalStyles.Push(plain);
                t.Check(AppSettings.GlobalStylesStamp != stamp && AppSettings.GlobalStyles.Find("body").FontSize == 20, "Push : les réglages suivent, nouvelle empreinte");
                changed = GlobalStyles.Sync(old);
                t.Check(changed && old.Styles.Find("body").FontSize == 20 && old.GlobalStylesStamp == AppSettings.GlobalStylesStamp,
                    "l'autre projet, d'empreinte périmée, tire la nouvelle taille");
                t.Check(old.Styles.Find("title1").Scope == ParagraphStyle.ScopeBook, "…ses styles de livre ne bougent pas");
                t.Check(!GlobalStyles.Sync(old), "même empreinte : rien à faire");

                // — Un projet neuf reçoit les globaux.
                var fresh = GlobalStyles.ForNewProject();
                t.Check(fresh.Find("body").FontFamily == "Garamond" && fresh.Find(StyleSheet.SeparatorId).IsSeparator, "un projet neuf part des styles globaux, séparateur compris");
            }
            finally
            {
                AppSettings.GlobalStyles = savedSheet;
                AppSettings.GlobalStylesStamp = savedStamp;
                GlobalStyles.Persist = savedPersist;
            }
        }

        private static List<string> Ids(List<ParagraphStyle> styles)
        {
            var ids = new List<string>();
            foreach (var style in styles) ids.Add(style.Id);
            return ids;
        }
    }
}

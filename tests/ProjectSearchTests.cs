using System;
using System.Collections.Generic;
using System.Threading;
using UniversSale.Model;

namespace UniversSale.Tests
{
    /// <summary>C16 — la recherche projet (batch 37) : requête compilée
    /// (littéral, regex, casse, mot entier, accents avec offsets d'origine,
    /// ligatures, regex invalide, délai, U+FFFC, recouvrements), les champs
    /// cherchables et leur cache, la recherche projet (portées, filtres,
    /// plans, dictionnaire, ordre, extraits, plafond, corbeille, budget).</summary>
    public static class ProjectSearchTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C16 — recherche projet");
            Literal(t);
            Accents(t);
            Ligatures(t);
            Regex(t);
            Elements(t);
            Fields(t);
            Cache(t);
            Scopes(t);
            Excerpts(t);
            Cap(t);
        }

        private static List<SearchQuery.Span> Find(string text, string pattern, bool matchCase, bool wholeWord, bool accents, bool regex)
        {
            return SearchQuery.Create(pattern, matchCase, wholeWord, accents, regex).FindInText(text);
        }

        private static string Spans(List<SearchQuery.Span> spans)
        {
            var parts = new List<string>();
            foreach (var span in spans) parts.Add(span.Start + "+" + span.Length + (span.Exact ? "" : "~"));
            return string.Join(" ", parts.ToArray());
        }

        private static void Literal(Harness t)
        {
            t.Equal("3+8 18+8", Spans(Find("Le marabout et le Marabout.", "marabout", false, false, false, false)), "littéral sans casse : deux empans");
            t.Equal("3+8", Spans(Find("Le marabout et le Marabout.", "marabout", true, false, false, false)), "respecter la casse : un seul");
            t.Equal("2+2", Spans(Find("aaaa", "aa", false, false, false, false).GetRange(1, 1)), "sans recouvrement : le second empan commence après le premier");
            t.Equal(2, Find("aaaa", "aa", false, false, false, false).Count, "« aa » dans « aaaa » : deux empans disjoints");
            t.Equal(3, PivotSearch.FindAll(Doc("aaaa"), "aa", false, false).Count, "le Ctrl+F historique garde ses recouvrements (C6)");
            t.Equal("", Spans(Find("grand-père", "père", false, true, false, false)), "mot entier : « père » ne trouve pas « grand-père »");
            t.Equal("0+3", Spans(Find("dit-il", "dit", false, true, false, false)), "…mais « dit » trouve le cœur de « dit-il »");
            t.Equal(0, Find("texte", "", false, false, false, false).Count, "requête vide : rien");
            t.Check(SearchQuery.Create("", false, false, false, false).IsEmpty, "…et se dit vide");
            t.Equal(0, PivotSearch.FindAll(Doc("marabout"), (SearchQuery)null).Count, "requête nulle : rien");
        }

        private static void Accents(Harness t)
        {
            var text = "L'élève et l'Élève, puis l'eleve.";
            t.Equal("2+5 13+5 27+5", Spans(Find(text, "eleve", false, false, true, false)), "sans accents : « eleve » trouve élève, Élève et eleve aux offsets d'origine");
            t.Equal("2+5 27+5", Spans(Find(text, "eleve", true, false, true, false)), "sans accents mais avec casse : Élève écarté");
            t.Equal("2+5 13+5 27+5", Spans(Find(text, "ÉLÈVE", false, false, true, false)), "la requête est pliée aussi");
            var decomposed = "élève fini"; // NFD : 7 unités pour « élève »
            t.Equal("0+7", Spans(Find(decomposed, "eleve", false, false, true, false)), "texte décomposé (NFD) : l'empan couvre les marques, offsets d'origine");
            t.Equal("0+7", Spans(Find(decomposed, "élève", false, false, true, false)), "…quelle que soit la forme de la requête");
            t.Equal("0+5", Spans(Find("élève", "el", false, false, true, false).Count == 1 ? Find("élève", "élève", false, false, true, false) : new List<SearchQuery.Span>()), "précomposé : cinq unités");
            t.Equal("0+2", Spans(Find("élève", "el", false, false, true, false)), "un empan partiel reste exact sur des séquences entières");
            t.Equal("2+5", Spans(Find("L'élève", "eleve", false, true, true, false)), "mot entier + accents : le token d'origine « élève »");
            t.Equal("", Spans(Find("L'élève", "elev", false, true, true, false)), "mot entier + accents : « elev » n'est pas un mot");
            t.Equal("Eleve", Correction.FrenchTokenizer.Fold("Élève", false), "Fold(word, false) garde la casse");
            t.Equal("eleve", Correction.FrenchTokenizer.Fold("Élève"), "Fold(word) reste le pli historique");
            t.Equal("OEuvre", Correction.FrenchTokenizer.Fold("Œuvre", false), "la ligature majuscule se plie en OE");
        }

        private static void Ligatures(Harness t)
        {
            t.Equal("0+1~", Spans(Find("œuvre", "o", false, false, true, false)), "« o » touche « œ » : l'empan est la ligature entière, marqué INEXACT (non remplaçable)");
            t.Equal("0+1", Spans(Find("œuvre", "oe", false, false, true, false)), "« oe » couvre « œ » en entier : exact");
            t.Equal("0+1", Spans(Find("œuvre", "œ", false, false, true, false)), "« œ » aussi");
            t.Equal("0+2", Spans(Find("œuvre", "oeu", false, false, true, false)), "« oeu » = œ + u : deux unités d'origine");
            t.Equal("1+1~", Spans(Find("cœur", "e", false, false, true, false)), "« e » sur « œ » : la ligature, inexact");
            t.Equal("", Spans(Find("cœur", "o", false, false, false, false)), "sans pli : « o » ne trouve pas « œ »");
            var map = FoldMap.Build("Œil", true);
            t.Equal("oeil", map.Folded, "la carte plie");
            int start, length;
            map.MapRange(0, 2, out start, out length);
            t.Check(start == 0 && length == 1, "deux unités pliées → une unité d'origine");
            map.MapRange(1, 2, out start, out length);
            t.Check(start == 0 && length == 2 && !map.IsExact(1, 2), "un empan qui commence au milieu de « oe » n'est pas exact");
            map.MapRange(2, 0, out start, out length);
            t.Check(start == 1 && length == 0, "empan vide : le début d'origine, longueur 0");
            var plain = FoldMap.Build("abc def", true);
            t.Check(plain.IsIdentity && plain.Folded == "abc def", "ASCII déjà en minuscules : carte identité, rien à plier");
            plain.MapRange(4, 3, out start, out length);
            t.Check(start == 4 && length == 3 && plain.IsExact(4, 3), "…qui reporte tel quel");
            t.Check(!FoldMap.Build("Abc", true).IsIdentity && FoldMap.Build("Abc", false).IsIdentity, "une majuscule ne compte que si la casse est pliée");
            var field = new SearchField { Text = "L'Élève" };
            t.Check(ReferenceEquals(field.FoldMapFor(true), field.FoldMapFor(true)) && !ReferenceEquals(field.FoldMapFor(true), field.FoldMapFor(false)), "un champ garde ses cartes de pli, une par casse");
            t.Equal("2+5", Spans(SearchQuery.Create("eleve", false, false, true, false).FindInField(field)), "FindInField rend les mêmes empans");
        }

        private static void Regex(Harness t)
        {
            t.Equal("3+8 18+8", Spans(Find("Le marabout et le Marabout.", "mara\\w+", false, false, false, true)), "regex sans casse");
            t.Equal("3+8", Spans(Find("Le marabout et le Marabout.", "mara\\w+", true, false, false, true)), "regex avec casse");
            t.Equal("2+5 13+5", Spans(Find("L'élève et l'Élève.", "[eé]l[eè]ve", false, false, false, true)), "regex avec les accents écrits");
            t.Equal("2+5 13+5", Spans(Find("L'élève et l'Élève.", "eleve", false, false, true, true)), "regex insensible aux accents : offsets d'origine");
            t.Equal("3+4", Spans(Find("Abc abc", "\\W?abc", true, false, true, true)), "le pli du motif ne touche pas aux classes (« \\W » reste « \\W », casse respectée)");
            var invalid = SearchQuery.Create("(abc", false, false, false, true);
            t.Check(!invalid.IsValid && invalid.Error != null && invalid.Error.StartsWith("Expression invalide"), "une regex invalide se signale, ne lève pas");
            t.Equal(0, invalid.FindInText("abc").Count, "…et ne trouve rien");
            t.Equal(2, Find("aXbXc", "X*", false, false, false, true).Count, "les empans vides sont ignorés (« X* » ne rend que les X)");
            t.Check(!SearchQuery.Create("a", false, true, false, true).WholeWord, "« mot entier » est sans objet en regex");
            var pathological = SearchQuery.Create("(a+)+$", false, false, false, true);
            var bomb = new string('a', 64) + "b";
            var timedOut = false;
            try { pathological.FindInText(bomb); }
            catch (SearchTimeoutException) { timedOut = true; }
            t.Check(timedOut, "une expression explosive dépasse le délai et le dit (SearchTimeoutException)");
            var targets = new List<SearchTarget> { new SearchTarget { Item = new BinderItem { Title = "x" }, Fields = new List<SearchField> { new SearchField { Text = bomb } } } };
            var result = ProjectSearch.Run(targets, pathological, 200, TimeSpan.FromSeconds(30), CancellationToken.None);
            t.Check(result.Interrupted && result.Message.Contains("interrompue"), "la recherche projet l'attrape et se dit interrompue");
            var budget = ProjectSearch.Run(targets, SearchQuery.Create("a", false, false, false, false), 200, TimeSpan.Zero, CancellationToken.None);
            t.Check(budget.Interrupted && budget.Total == 0, "budget global nul : interrompue avant la première cible");
            var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            t.Check(ProjectSearch.Run(targets, SearchQuery.Create("a", false, false, false, false), 200, TimeSpan.FromSeconds(3), cancelled.Token).Cancelled, "un jeton annulé : résultat annulé");
        }

        private static void Elements(Harness t)
        {
            var text = "abc￼def";
            t.Equal("", Spans(Find(text, "c.d", false, false, false, true)), "« . » ne traverse pas un élément");
            t.Equal("", Spans(Find(text, "c\\nd", false, false, false, true)), "un empan qui contiendrait l'élément est rejeté (garde-fou)");
            t.Equal("0+3 4+3", Spans(Find(text, "\\w+", false, false, false, true)), "« \\w+ » s'arrête de part et d'autre");
            t.Equal("0+3 4+3", Spans(Find(text, "\\b\\w+\\b", false, false, false, true)), "l'élément est une frontière de mot");
            t.Equal("4+3", Spans(Find(text, "def", false, false, true, false)), "avec le pli, l'élément garde sa place (1 pour 1)");
            t.Equal(0, Find("a￼b", "a￼b", false, false, false, false).Count, "un élément ne matche jamais un texte cherché");
        }

        private static TextDocument Doc(params string[] paragraphs)
        {
            var document = new TextDocument();
            foreach (var text in paragraphs)
            {
                var paragraph = new TextParagraph();
                paragraph.Runs.Add(new TextRun { Text = text });
                document.Paragraphs.Add(paragraph);
            }
            return document;
        }

        /// <summary>Un projet de fixture : un livre avec deux chapitres, un
        /// dossier avec un texte, une fiche, un plan, un dictionnaire, un
        /// média, un texte à la corbeille.</summary>
        private static Project Fixture(out BinderItem chapter1, out BinderItem chapter2, out BinderItem loose, out BinderItem sheet, out BinderItem plan, out BinderItem trashed)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Roman", Book = new BookInfo { Subtitle = "Le marabout des marais", Publisher = "Éditions du Pont" } };
            chapter1 = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre un", Synopsis = "Le marabout arrive." };
            chapter1.Document = Doc("Un marabout cendré se pose.", "Rien ici.", "Le marabout, encore, et le marabout.");
            chapter1.Document.Footnotes.Add(new Footnote { Text = "Note : marabout africain." });
            chapter1.Document.Annotations.Add(new Annotation { Text = "Revoir le marabout du début." });
            var protectedRun = chapter1.Document.Paragraphs[0].Runs[0];
            protectedRun.Text = "Un ";
            chapter1.Document.Paragraphs[0].Runs.Add(new TextRun { Text = "marabout", NoProof = true });
            chapter1.Document.Paragraphs[0].Runs.Add(new TextRun { Text = " cendré se pose." });
            chapter2 = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre deux" };
            chapter2.Document = Doc("Le marais sans marabout.");
            book.Children.Add(chapter1);
            book.Children.Add(chapter2);
            writings.Children.Add(book);
            var folder = new BinderItem { Kind = ItemKind.Folder, Title = "Brouillons" };
            loose = new BinderItem { Kind = ItemKind.Text, Title = "Idée" };
            loose.Document = Doc("Un marabout de plus.");
            folder.Children.Add(loose);
            writings.Children.Add(folder);
            var template = project.CharacterTemplate();
            sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Marabout", TemplateId = template.Id, CategoryId = project.SheetCategories[0].Id };
            sheet.Document = Doc("Corps de fiche.");
            sheet.FieldValues[template.Fields[0].Id] = "Marabout cendré";
            sheet.FreeInfo.Add(new InfoEntry { Title = "Devise", Value = "Toujours plumer le marabout" });
            sheet.Relations.Add(new SheetRelation { Kind = "Rival", Name = "Le marabout noir" });
            project.Category(Project.KeySheets).Children.Add(sheet);
            plan = new BinderItem { Kind = ItemKind.Plan, Title = "Plan", Plan = new PlanInfo() };
            var column = new PlanColumn { Title = "Acte du marabout" };
            column.Entries.Add(new PlanEntry { Text = "Le marabout s'envole" });
            column.Entries.Add(new PlanEntry { Kind = PlanEntry.KindNote, Text = "revoir le marabout" });
            plan.Plan.Columns.Add(column);
            project.Category(Project.KeyPlans).Children.Add(plan);
            project.Lexicon.Add(new LexiconEntry { Word = "marabouter", Note = "verbe du marabout" });
            var media = new BinderItem { Kind = ItemKind.Media, Title = "Photo de marabout", MediaExtension = ".png", MediaBytes = new byte[] { 1, 2, 3 } };
            project.Category(Project.KeyResearch).Children.Add(media);
            trashed = new BinderItem { Kind = ItemKind.Text, Title = "Vieux marabout" };
            trashed.Document = Doc("Un marabout jeté.");
            project.Trash.Children.Add(trashed);
            project.RelinkParents();
            return project;
        }

        private static SearchResult Search(Project project, string pattern, SearchScope scope, BinderItem current, SearchKind kind, bool trash, int cap)
        {
            var targets = ProjectSearch.Collect(project, scope, current, kind, trash);
            return ProjectSearch.Run(targets, SearchQuery.Create(pattern, false, false, true, false), cap, ProjectSearch.DefaultBudget, CancellationToken.None);
        }

        private static int CountKind(SearchResult result, string kind)
        {
            var n = 0;
            foreach (var hit in result.Hits) if (hit.Field.Kind == kind) n++;
            return n;
        }

        private static void Fields(Harness t)
        {
            BinderItem chapter1, chapter2, loose, sheet, plan, trashed;
            var project = Fixture(out chapter1, out chapter2, out loose, out sheet, out plan, out trashed);
            var fields = chapter1.SearchFields(project);
            var kinds = new List<string>();
            foreach (var field in fields) kinds.Add(field.Kind);
            t.Equal("title synopsis paragraph paragraph paragraph footnote annotation", string.Join(" ", kinds.ToArray()), "un écrit : titre, synopsis, paragraphes, notes, annotations (les vides omis)");
            t.Equal(2, fields[4].ParagraphIndex, "un paragraphe connaît son index");
            t.Check(fields[2].NoProofRanges != null && fields[2].NoProofRanges[0] == 3 && fields[2].NoProofRanges[1] == 11, "le paragraphe porte ses plages « ne pas corriger »");
            t.Check(fields[2].OverlapsNoProof(3, 8) && fields[2].OverlapsNoProof(0, 4) && !fields[2].OverlapsNoProof(11, 3), "…et sait ce qui les chevauche");
            kinds.Clear();
            foreach (var field in sheet.SearchFields(project)) kinds.Add(field.Kind + ":" + field.Label);
            t.Equal("title:Titre paragraph:¶ 1 field:Nom info:Devise relation:Relation (Rival)", string.Join(" ", kinds.ToArray()), "une fiche : champs de modèle par nom, infos libres, noms libres de relations");
            kinds.Clear();
            foreach (var field in plan.SearchFields(project)) kinds.Add(field.Kind + ":" + field.Label);
            t.Equal("title:Titre column:Colonne 1 entry:Élément, Colonne 1 entry:Note, Colonne 1", string.Join(" ", kinds.ToArray()), "un plan : colonnes et briques");
            var dictionary = project.Category(Project.KeyDictionary).SearchFields(project);
            t.Check(dictionary.Count == 2 && dictionary[0].Kind == SearchField.KindLexicon && dictionary[0].Text == "marabouter", "la racine Dictionnaire offre ses entrées");
            var book = project.Category(Project.KeyWritings).Children[0].SearchFields(project);
            t.Check(book.Count == 3 && book[1].Kind == SearchField.KindBook && book[1].Label == "Sous-titre" && book[2].RefId == "Publisher", "un livre : ses métadonnées remplies");
            var media = project.Category(Project.KeyResearch).Children[0].SearchFields(project);
            t.Check(media.Count == 1 && media[0].Kind == SearchField.KindTitle, "un média : son titre seul (fichier opaque)");
            t.Equal(0, project.Category(Project.KeyWritings).SearchFields(project).Count, "une racine ordinaire n'a rien");
            t.Check(chapter1.SearchText().Contains("Note : marabout africain."), "SearchText() est la concaténation des champs");
        }

        private static void Cache(Harness t)
        {
            BinderItem chapter1, chapter2, loose, sheet, plan, trashed;
            var project = Fixture(out chapter1, out chapter2, out loose, out sheet, out plan, out trashed);
            var first = chapter1.SearchFields(project);
            t.Check(ReferenceEquals(first, chapter1.SearchFields(project)), "un item inchangé rend la même liste (cache)");
            chapter1.Document.Paragraphs[1].Runs[0].Text = "Quelque chose ici.";
            var second = chapter1.SearchFields(project);
            t.Check(!ReferenceEquals(first, second) && second[3].Text == "Quelque chose ici.", "un paragraphe modifié : reconstruit");
            chapter1.Title = "Chapitre premier";
            t.Check(!ReferenceEquals(second, chapter1.SearchFields(project)), "un titre modifié : reconstruit");
            var third = chapter1.SearchFields(project);
            chapter1.Document.Paragraphs[0].Runs[1].NoProof = false;
            t.Check(!ReferenceEquals(third, chapter1.SearchFields(project)), "un « ne pas corriger » levé : reconstruit (les plages changent)");
            var before = Searchable.Fingerprint(sheet, project);
            sheet.FreeInfo[0].Value = "Autre devise";
            t.Check(Searchable.Fingerprint(sheet, project) != before, "l'empreinte d'une fiche suit ses infos libres");
            var dictionary = project.Category(Project.KeyDictionary);
            var entries = dictionary.SearchFields(project);
            project.Lexicon.Add(LexiconEntry.Simple("neuf"));
            t.Check(!ReferenceEquals(entries, dictionary.SearchFields(project)), "la racine Dictionnaire suit le lexique");
        }

        private static void Scopes(Harness t)
        {
            BinderItem chapter1, chapter2, loose, sheet, plan, trashed;
            var project = Fixture(out chapter1, out chapter2, out loose, out sheet, out plan, out trashed);

            var all = Search(project, "marabout", SearchScope.Project, null, SearchKind.All, false, 200);
            // Chapitre un : synopsis 1, ¶0 1, ¶2 2, note 1, annotation 1 = 6 ; chapitre deux 1 ; idée 1 ;
            // livre (sous-titre) 1 ; fiche : titre 1, champ 1, info 1, relation 1 = 4 ; plan : colonne 1, 2 briques = 3 ;
            // dictionnaire : mot 1 + note 1 = 2 ; média 1. Corbeille exclue.
            t.Equal(19, all.Total, "projet entier : 19 occurrences (corbeille exclue)");
            t.Equal(8, all.ItemCount, "…dans 8 items");
            t.Check(!all.Capped && !all.Interrupted, "ni plafonnée ni interrompue");
            t.Equal(1, all.NoProofCount, "une occurrence dans un passage « ne pas corriger »");
            t.Check(all.Hits[0].Item.Kind == ItemKind.Book && all.Hits[1].Item == chapter1 && all.Hits[1].Field.Kind == SearchField.KindSynopsis, "ordre de la Pile : le livre, puis le chapitre un (synopsis d'abord)");
            t.Check(all.Hits[2].Field.ParagraphIndex == 0 && all.Hits[2].NoProof && !all.Hits[2].Replaceable, "puis ¶ 1 — chevauche NoProof : non remplaçable");
            t.Check(all.Hits[3].Field.ParagraphIndex == 2 && all.Hits[3].Start == 3 && all.Hits[4].Start == 27, "puis ¶ 3, dans l'ordre du texte");
            t.Equal(1, CountKind(all, SearchField.KindAnnotation), "les annotations sont cherchées");
            t.Equal(1, CountKind(all, SearchField.KindFootnote), "les notes de bas de page aussi");
            t.Equal(3, CountKind(all, SearchField.KindColumn) + CountKind(all, SearchField.KindEntry), "les plans aussi");
            t.Equal(2, CountKind(all, SearchField.KindLexicon) + CountKind(all, SearchField.KindLexiconNote), "le dictionnaire aussi");

            var withTrash = Search(project, "marabout", SearchScope.Project, null, SearchKind.All, true, 200);
            t.Equal(21, withTrash.Total, "avec la corbeille : + titre et texte du jeté");

            t.Equal(6, Search(project, "marabout", SearchScope.Document, chapter1, SearchKind.All, false, 200).Total, "portée document : le chapitre un seul");
            var container = Search(project, "marabout", SearchScope.Container, chapter1, SearchKind.All, false, 200);
            t.Equal(8, container.Total, "portée livre : le livre et ses deux chapitres");
            t.Equal(1, Search(project, "marabout", SearchScope.Container, loose, SearchKind.All, false, 200).Total, "portée dossier depuis un texte du dossier");
            t.Equal(8, Search(project, "marabout", SearchScope.Container, project.Category(Project.KeyWritings).Children[0], SearchKind.All, false, 200).Total, "portée depuis le livre lui-même");

            t.Equal(9, Search(project, "marabout", SearchScope.Project, null, SearchKind.Texts, false, 200).Total, "filtre écrits : livre + chapitres + idée");
            t.Equal(4, Search(project, "marabout", SearchScope.Project, null, SearchKind.Sheets, false, 200).Total, "filtre fiches");
            t.Equal(3, Search(project, "marabout", SearchScope.Project, null, SearchKind.Plans, false, 200).Total, "filtre plans");
            t.Equal(2, Search(project, "marabout", SearchScope.Project, null, SearchKind.Dictionary, false, 200).Total, "filtre dictionnaire");
            t.Equal(1, Search(project, "marabout", SearchScope.Project, null, SearchKind.Media, false, 200).Total, "filtre médias : le titre");
            t.Equal(0, Search(project, "", SearchScope.Project, null, SearchKind.All, false, 200).Total, "requête vide : rien, sans message");
        }

        private static void Excerpts(Harness t)
        {
            int start, length;
            var excerpt = ProjectSearch.Excerpt("Un marabout cendré se pose.", 3, 8, out start, out length);
            t.Equal("Un marabout cendré se pose.", excerpt, "texte court : l'extrait est le texte");
            t.Check(start == 3 && length == 8, "…et le terme y est à sa place");
            var longText = "Il était une fois, dans un marais lointain que personne ne visitait jamais, un marabout cendré qui regardait passer les nuages sans rien dire à personne.";
            var at = longText.IndexOf("marabout");
            excerpt = ProjectSearch.Excerpt(longText, at, 8, out start, out length);
            t.Check(excerpt.StartsWith("…") && excerpt.EndsWith("…"), "texte long : tronqué des deux côtés avec « … »");
            t.Equal("marabout", excerpt.Substring(start, length), "la position du terme dans l'extrait est juste");
            t.Check(!excerpt.Contains("  "), "…et coupé sur des blancs");
            excerpt = ProjectSearch.Excerpt("a\n\n  b￼c marabout", 9, 8, out start, out length);
            t.Equal("a b▢c marabout", excerpt, "blancs repliés, élément rendu ▢");
            t.Equal("marabout", excerpt.Substring(start, length), "…sans perdre la position");
            excerpt = ProjectSearch.Excerpt("abc", 10, 5, out start, out length);
            t.Check(excerpt == "abc" && length == 0, "un empan hors texte ne plante pas");
        }

        private static void Cap(Harness t)
        {
            BinderItem chapter1, chapter2, loose, sheet, plan, trashed;
            var project = Fixture(out chapter1, out chapter2, out loose, out sheet, out plan, out trashed);
            var capped = Search(project, "marabout", SearchScope.Project, null, SearchKind.All, false, 5);
            t.Check(capped.Capped && capped.Hits.Count == 5 && capped.Total == 19 && capped.ItemCount == 8, "plafond : 5 gardées, 19 comptées, 8 items comptés");
            t.Equal("5 premières sur 19 occurrences dans 8 items", capped.Summary(), "le plafond est annoncé");
            var all = Search(project, "marabout", SearchScope.Project, null, SearchKind.All, false, 200);
            t.Equal("19 occurrences dans 8 items", all.Summary(), "sans plafond : le compte");
            t.Equal("Aucune occurrence", Search(project, "zzz", SearchScope.Project, null, SearchKind.All, false, 200).Summary(), "rien : dit");
            var one = Search(project, "Éditions", SearchScope.Project, null, SearchKind.All, false, 200);
            t.Equal("1 occurrence dans 1 item", one.Summary(), "singulier");
        }
    }
}

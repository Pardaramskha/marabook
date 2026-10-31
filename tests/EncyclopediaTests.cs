using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C60 — L'ENCYCLOPÉDIE (1.1.0, 31/10/2026) : l'export des fiches.
    /// Le document pivot (sections = catégories et sous-catégories, fiches
    /// triées, infobox groupée, relations et liens vers l'exporté seul,
    /// présence, radar, tags, lexique défini), les sélections (tout, une
    /// catégorie, une fiche), les noms de fichiers sûrs, le rendu HTML (dossier,
    /// accueil, pages, lexique, CSS, images, échappement) et les options
    /// dans le .plot v45.</summary>
    public static class EncyclopediaTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C60 — 1.1.0 : l'Encyclopédie — export des fiches (pivot, sélections, HTML, .plot v45) (31/10)");
            Pivot(t);
            Selections(t);
            Slugs(t);
            Html(t);
            RoundTrip(t);
        }

        private class World
        {
            public Project Project;
            public SheetCategory Characters, Heroes, Places;
            public BinderItem Keira, Marabout, Zorglub, Cove, Chapter;
            public string PortraitId;
        }

        /// <summary>Un petit monde : Personnages (Keira, avec champs, tags,
        /// portrait, corps Markdown à liens) et sa sous-catégorie Héros (le
        /// marabout), une catégorie Lieux (la Crique, Zorglub), un chapitre
        /// qui nomme Keira, deux mots au Lexique dont un sans définition.</summary>
        private static World Make()
        {
            var project = Project.CreateNew();
            var world = new World { Project = project };
            world.Characters = project.SheetCategories[0];
            world.Heroes = new SheetCategory { Name = "Héros", ParentId = world.Characters.Id, Color = "#C0392B" };
            project.SheetCategories.Insert(1, world.Heroes);
            world.Places = new SheetCategory { Name = "Lieux" };
            project.SheetCategories.Add(world.Places);
            var sheets = project.Category(Project.KeySheets);
            Func<string, SheetCategory, BinderItem> sheet = delegate(string title, SheetCategory category)
            {
                var item = new BinderItem { Kind = ItemKind.Sheet, Title = title, CategoryId = category.Id, TemplateId = category.TemplateId ?? world.Characters.TemplateId };
                sheets.Children.Add(item);
                return item;
            };
            world.Keira = sheet("Keira Varenh", world.Characters);
            world.Marabout = sheet("Le marabout", world.Heroes);
            world.Zorglub = sheet("Zorglub", world.Places);
            world.Cove = sheet("La crique", world.Places);
            var template = project.TemplateOf(world.Keira);
            if (template != null) template.Tracking = true; // le suivi des noms se demande (21/09)
            if (template != null && template.Fields.Count > 0) world.Keira.FieldValues[template.Fields[0].Id] = "Vingt-deux ans <script>";
            world.Keira.FreeInfo.Add(new InfoEntry { Title = "Devise", Value = "Voir [[Le marabout]] et [[Inconnu]]", Group = "Infos" });
            world.Keira.Relations.Add(new SheetRelation { Kind = "Ami", TargetId = world.Marabout.Id });
            world.Keira.Relations.Add(new SheetRelation { Kind = "Ennemi", TargetId = world.Zorglub.Id });
            SheetTags.Add(world.Keira, "héros");
            world.PortraitId = project.AddImage(TinyPng(), ".png");
            world.Keira.ImageId = world.PortraitId;
            var body = new TextParagraph();
            body.Runs.Add(new TextRun { Text = "# Histoire\n\nElle grandit avec [[Le marabout]] près de **la mer** & du vent.\n\n- une liste\n- [ ] une tâche" });
            world.Keira.Document.Paragraphs.Add(body);
            var writings = project.Category(Project.KeyWritings);
            writings.Children.Clear();
            world.Chapter = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre un" };
            var p = new TextParagraph();
            p.Runs.Add(new TextRun { Text = "Keira Varenh regarde la mer. Keira sourit." });
            world.Chapter.Document.Paragraphs.Add(p);
            writings.Children.Add(world.Chapter);
            project.Lexicon.Add(new LexiconEntry { Word = "Marabout", Class = LexiconEntry.ClassNoun, Definition = "Un sage des grèves." });
            project.Lexicon.Add(new LexiconEntry { Word = "Zorgluber", Class = LexiconEntry.ClassVerb });
            return world;
        }

        /// <summary>Un PNG de 1 × 1 pixel, valide.</summary>
        private static byte[] TinyPng()
        {
            return new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
                0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
                0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
                0x44, 0xAE, 0x42, 0x60, 0x82
            };
        }

        private static EncyclopediaSection SectionOf(EncyclopediaDocument document, string id)
        {
            foreach (var section in document.Sections)
            {
                if (section.Id == id) return section;
                foreach (var sub in section.Subsections) if (sub.Id == id) return sub;
            }
            return null;
        }

        private static void Pivot(Harness t)
        {
            var world = Make();
            var doc = Encyclopedia.Build(world.Project, EncyclopediaSelection.All(), new EncyclopediaOptions());
            t.Check(doc.Title == "Encyclopédie de " + world.Project.Name && doc.SheetCount == 4, "tout : « Encyclopédie de <projet> », quatre fiches (" + doc.SheetCount + ")");
            var characters = SectionOf(doc, world.Characters.Id);
            var heroes = SectionOf(doc, world.Heroes.Id);
            var places = SectionOf(doc, world.Places.Id);
            t.Check(characters != null && heroes != null && places != null && heroes.Parent == characters && characters.Subsections.Contains(heroes), "Personnages porte sa sous-section Héros ; Lieux est une section");
            t.Check(doc.Sections.Count == 2, "les catégories sans fiche ne paraissent pas (" + doc.Sections.Count + " sections)");
            t.Check(heroes.Sheets.Count == 1 && heroes.Sheets[0].Id == world.Marabout.Id && heroes.Color == "#C0392B", "le marabout est dans Héros, avec la couleur de la sous-catégorie");
            t.Check(places.Sheets.Count == 2 && places.Sheets[0].Title == "La crique" && places.Sheets[1].Title == "Zorglub", "les fiches d'une section se rangent par titre");
            var keira = doc.ById[world.Keira.Id];
            t.Check(keira.Section == characters && keira.ImageId == world.PortraitId && doc.Images.ContainsKey(world.PortraitId), "Keira : sa section, son portrait relevé parmi les images");
            var devise = false; var age = false;
            foreach (var group in keira.Groups)
                foreach (var field in group.Fields)
                {
                    if (field.Label == "Devise" && field.Value.Contains("[[Le marabout]]")) devise = true;
                    if (field.Value.StartsWith("Vingt-deux")) age = true;
                }
            t.Check(keira.Groups.Count >= 1 && devise && age, "l'infobox : les champs remplis du modèle et les champs libres, groupés");
            t.Check(keira.Relations.Count == 2 && keira.Relations[0].TargetId == world.Marabout.Id && keira.Relations[0].Kind.Length > 0 && keira.Relations[1].TargetId == world.Zorglub.Id, "les relations visent les fiches exportées (" + keira.Relations[0].Kind + ")");
            t.Check(keira.Presence.Count == 1 && keira.Presence[0].TextTitle == "Chapitre un" && keira.Presence[0].Count >= 1, "la présence : « Chapitre un » · " + (keira.Presence.Count == 0 ? 0 : keira.Presence[0].Count) + " (le titre entier compte ; le prénom seul est l'affaire du graphe)");
            t.Check(keira.Tags.Count == 1 && keira.Tags[0] == "héros" && keira.Subtitle == "Personnage", "tags et nom du modèle");
            t.Check(doc.Lexicon.Count == 1 && doc.Lexicon[0].Word == "Marabout" && doc.Lexicon[0].Letter == "M" && doc.Lexicon[0].ClassLabel.Length > 0, "le Lexique ne garde que les mots définis (" + doc.Lexicon.Count + ")");
            var all = new EncyclopediaOptions { LexiconOnlyDefined = false };
            t.Check(Encyclopedia.Build(world.Project, EncyclopediaSelection.All(), all).Lexicon.Count == 2, "…sauf si on demande tous les mots");
            var global = new List<LexiconEntry> { new LexiconEntry { Word = "Grève", Class = LexiconEntry.ClassNoun, Definition = "La plage." }, new LexiconEntry { Word = "marabout", Definition = "Doublon global, écarté." } };
            var withGlobal = Encyclopedia.Build(world.Project, EncyclopediaSelection.All(), new EncyclopediaOptions(), global);
            t.Check(withGlobal.Lexicon.Count == 2 && withGlobal.Lexicon[0].Word == "Grève" && withGlobal.Lexicon[1].Definition == "Un sage des grèves.", "le dictionnaire de tous les projets s'ajoute, le projet l'emporte sur un doublon, le tout trié");
            var noLexicon = Encyclopedia.Build(world.Project, EncyclopediaSelection.All(), new EncyclopediaOptions { IncludeLexicon = false });
            t.Check(noLexicon.Lexicon.Count == 0, "sans Lexique : rien");
            var bare = Encyclopedia.Build(world.Project, EncyclopediaSelection.All(), new EncyclopediaOptions { ShowInfobox = false, ShowRelations = false, ShowPresence = false, ShowTags = false });
            var k2 = bare.ById[world.Keira.Id];
            t.Check(k2.Groups.Count == 0 && k2.Relations.Count == 0 && k2.Presence.Count == 0 && k2.Tags.Count == 0 && k2.BodyMarkdown.Contains("Histoire"), "les encarts se coupent un à un ; le corps reste");
            var noEmpty = Encyclopedia.Build(world.Project, EncyclopediaSelection.All(), new EncyclopediaOptions { IncludeEmptySheets = false });
            t.Check(noEmpty.SheetCount == 1 && noEmpty.ById.ContainsKey(world.Keira.Id), "sans les fiches vides : Keira seule (" + noEmpty.SheetCount + ")");
        }

        private static void Selections(Harness t)
        {
            var world = Make();
            var category = Encyclopedia.Build(world.Project, EncyclopediaSelection.Category(world.Characters.Id), new EncyclopediaOptions());
            t.Check(category.SheetCount == 2 && category.ById.ContainsKey(world.Marabout.Id) && !category.ById.ContainsKey(world.Zorglub.Id), "une catégorie : ses fiches et celles de ses sous-catégories (" + category.SheetCount + ")");
            var keira = category.ById[world.Keira.Id];
            t.Check(keira.Relations[0].TargetId == world.Marabout.Id && keira.Relations[1].TargetId == null && keira.Relations[1].Label == "Zorglub", "une relation vers une fiche non exportée devient du texte");
            var noSub = EncyclopediaSelection.Category(world.Characters.Id);
            noSub.IncludeSubcategories = false;
            t.Check(Encyclopedia.Build(world.Project, noSub, new EncyclopediaOptions()).SheetCount == 1, "…sans les sous-catégories : la catégorie seule");
            var one = Encyclopedia.Build(world.Project, EncyclopediaSelection.Sheet(world.Marabout.Id), new EncyclopediaOptions());
            t.Check(one.SheetCount == 1 && one.Sections.Count == 1 && one.Sections[0].Id == world.Characters.Id && one.Sections[0].Subsections.Count == 1, "une fiche seule : sa section et sa sous-section, rien d'autre");
            world.Project.Trash.Children.Add(world.Cove);
            world.Project.Category(Project.KeySheets).Children.Remove(world.Cove);
            world.Cove.Parent = world.Project.Trash;
            t.Check(Encyclopedia.Build(world.Project, EncyclopediaSelection.All(), new EncyclopediaOptions()).SheetCount == 3, "une fiche à la corbeille ne s'exporte pas");
        }

        private static void Slugs(Harness t)
        {
            var taken = new HashSet<string>();
            t.Equal("keira-varenh", Encyclopedia.Slug("Keira Varenh", taken), "un titre devient un nom de fichier plié");
            t.Equal("eric-l-ecuyer", Encyclopedia.Slug("Éric l'écuyer !", taken), "accents et ponctuation pliés");
            t.Equal("eric-l-ecuyer-2", Encyclopedia.Slug("Éric l'Écuyer", taken), "un doublon se numérote");
            t.Equal("fiche", Encyclopedia.Slug("…", taken), "rien de lisible : « fiche »");
            t.Equal("M", Encyclopedia.LetterOf("marabout"), "la lettre d'index, pliée en capitale");
            t.Equal("E", Encyclopedia.LetterOf("Étoile"), "…accent plié");
            t.Equal("#", Encyclopedia.LetterOf("42"), "…un chiffre : #");
        }

        private static void Html(Harness t)
        {
            var world = Make();
            var options = new EncyclopediaOptions { Accent = "#AA2211", HeadingFont = "Candara", BodyFont = "Garamond", Subtitle = "Les Gardiens", Author = "R. Auteur" };
            var doc = Encyclopedia.Build(world.Project, EncyclopediaSelection.All(), options);
            var folder = Path.Combine(Path.GetTempPath(), "marabook-c60-" + Guid.NewGuid().ToString("N"));
            try
            {
                var written = HtmlEncyclopedia.Write(folder, doc, world.Project);
                var index = Path.Combine(folder, HtmlEncyclopedia.IndexFile);
                t.Check(written.Count >= 7 && File.Exists(index) && File.Exists(Path.Combine(folder, HtmlEncyclopedia.CssFile)) && File.Exists(Path.Combine(folder, HtmlEncyclopedia.LexiconFile)), "le dossier : accueil, feuille de styles, lexique, pages (" + written.Count + " fichiers)");
                var keira = doc.ById[world.Keira.Id];
                var marabout = doc.ById[world.Marabout.Id];
                var keiraPath = Path.Combine(folder, HtmlEncyclopedia.SheetPath(keira).Replace('/', Path.DirectorySeparatorChar));
                var maraboutPath = Path.Combine(folder, HtmlEncyclopedia.SheetPath(marabout).Replace('/', Path.DirectorySeparatorChar));
                t.Check(File.Exists(keiraPath) && File.Exists(maraboutPath) && HtmlEncyclopedia.SheetPath(marabout).Contains("/heros/"), "une page par fiche, la sous-catégorie dans un sous-dossier (" + HtmlEncyclopedia.SheetPath(marabout) + ")");
                var home = File.ReadAllText(index);
                t.Check(home.Contains("<title>Encyclopédie de ") && home.Contains("Les Gardiens") && home.Contains("R. Auteur") && home.Contains("href=\"" + HtmlEncyclopedia.SheetPath(keira) + "\"") && home.Contains("Keira Varenh") && home.Contains("id=\"" + heroesSlug(doc, world) + "\""), "l'accueil : titre, sous-titre, auteur, le lien de chaque fiche, l'ancre de chaque section");
                t.Check(home.Contains("href=\"" + HtmlEncyclopedia.LexiconFile + "\""), "…et le lien vers le Lexique");
                var page = File.ReadAllText(keiraPath);
                t.Check(page.Contains("<h1>Keira Varenh</h1>") && page.Contains("Vingt-deux ans &lt;script&gt;") && !page.Contains("<script>"), "la page : le titre, les champs, le texte échappé");
                t.Check(page.Contains("href=\"../../" + HtmlEncyclopedia.SheetPath(marabout) + "\"") && page.Contains("Inconnu") && !page.Contains("inconnu.html"), "les [[liens]] vers une fiche exportée deviennent des liens, les autres du texte");
                t.Check(page.Contains("<h2>Histoire</h2>") && page.Contains("<strong>la mer</strong>") && page.Contains("&amp; du vent") && page.Contains("class=\"task\"") && page.Contains("<li>une liste</li>"), "le corps Markdown : titres décalés, gras, échappement, listes et tâches");
                t.Check(page.Contains("class=\"portrait\"") && page.Contains("images/" + world.PortraitId) && File.Exists(Path.Combine(folder, HtmlEncyclopedia.ImagesFolder, world.PortraitId + ".png")), "le portrait est écrit dans images/ et affiché");
                t.Check(page.Contains("<h2>Relations</h2>") && page.Contains("(Ami)") && page.Contains("<h2>Présence</h2>") && page.Contains("Chapitre un") && page.Contains("class=\"tag\">héros"), "relations, présence et tags sur la page");
                t.Check(page.Contains("class=\"prev\"") || page.Contains("class=\"next\""), "la navigation précédente / suivante");
                var lexicon = File.ReadAllText(Path.Combine(folder, HtmlEncyclopedia.LexiconFile));
                t.Check(lexicon.Contains("id=\"l-M\"") && lexicon.Contains("<b>Marabout</b>") && lexicon.Contains("Un sage des grèves.") && lexicon.Contains("<i class=\"class\">nom</i>"), "le Lexique : la lettre, le mot, sa nature, sa définition");
                var css = File.ReadAllText(Path.Combine(folder, HtmlEncyclopedia.CssFile));
                t.Check(css.Contains("--accent:#AA2211") && css.Contains("\"Garamond\"") && css.Contains("\"Candara\"") && css.Contains("--size:16px"), "la feuille de styles porte l'accent, les polices et la taille");
                var dark = HtmlEncyclopedia.Index(Encyclopedia.Build(world.Project, EncyclopediaSelection.All(), new EncyclopediaOptions { Dark = true, ShowDates = false }));
                t.Check(dark.Contains("data-theme=\"dark\"") && !dark.Contains("exportée le"), "sombre : l'attribut du thème ; sans dates : pas de date au pied");
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (IOException) { }
            }
        }

        private static string heroesSlug(EncyclopediaDocument doc, World world)
        {
            var section = SectionOf(doc, world.Heroes.Id);
            return section == null ? "?" : section.Slug;
        }

        private static void RoundTrip(Harness t)
        {
            var world = Make();
            var project = world.Project;
            project.Encyclopedia = new EncyclopediaOptions { Format = EncyclopediaOptions.FormatHtml, Title = "Codex", Author = "Moi", BodyFont = "Garamond", BaseSize = 18, Accent = "#112233", Dark = true, ShowRadar = false, IncludeLexicon = false, PortraitSide = "left", CoverImageId = world.PortraitId };
            var path = Path.Combine(Path.GetTempPath(), "marabook-c60-" + Guid.NewGuid().ToString("N") + ".plot");
            try
            {
                PlotFile.Save(project, path);
                var loaded = PlotFile.Load(path);
                var e = loaded.Encyclopedia;
                t.Check(loaded.LoadedFormatVersion >= 45, "le .plot est en v45 au moins (" + loaded.LoadedFormatVersion + ")");
                t.Check(e != null && e.Title == "Codex" && e.Author == "Moi" && e.BodyFont == "Garamond" && Math.Abs(e.BaseSize - 18) < 0.001 && e.Accent == "#112233" && e.Dark && !e.ShowRadar && !e.IncludeLexicon && e.PortraitSide == "left" && e.CoverImageId == world.PortraitId && e.ShowInfobox,
                    "les options de l'Encyclopédie font l'aller-retour, les absentes gardent leur défaut");
                var empty = Project.CreateNew();
                PlotFile.Save(empty, path);
                t.Check(PlotFile.Load(path).Encyclopedia == null, "jamais réglées : rien dans le .plot");
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }
    }
}

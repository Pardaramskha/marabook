using System;
using System.Collections.Generic;
using System.IO;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C66 — frises, lot 3 (1.2.0, 14/11) : l'an 0 des segments
    /// suivants (« A1-1 … puis A2-0 »), les années comptées dans les segments
    /// seuls, « Décédé » à l'époque, et le Carnet de l'application.</summary>
    public static class TimelineLot3Tests
    {
        public static void Run(Harness t)
        {
            t.Suite("C66 — frises, lot 3 : an 0 par segment, années des segments, carnet");
            SegmentYearZero(t);
            SegmentYears(t);
            Deceased(t);
            NotebookNotes(t);
        }

        private static TimeSystem Ages(bool yearZero, bool segmentYearZero)
        {
            var system = new TimeSystem
            {
                Name = "Âges", Segmented = true, SegmentWord = "Âge", Cumulative = false,
                YearPattern = "A{s}-{a}", YearZero = yearZero, SegmentYearZero = segmentYearZero
            };
            system.Segments.Add(new TimeSegment { Name = "Premier âge", Length = 1000 });
            system.Segments.Add(new TimeSegment { Name = "Deuxième âge", Length = 500 });
            system.Segments.Add(new TimeSegment { Name = "Troisième âge", Length = 0 });
            for (var i = 1; i <= 12; i++) system.Months.Add(new TimeMonth { Name = "Mois " + i, Days = 30 });
            return system;
        }

        private static long Year(TimeSystem system, string notation)
        {
            TimePoint point;
            return system.TryParse(notation, out point) ? point.Year : long.MinValue;
        }

        private static bool Reads(TimeSystem system, string notation)
        {
            TimePoint point;
            return system.TryParse(notation, out point);
        }

        private static void SegmentYearZero(Harness t)
        {
            // Le cas de Rémi : la frise commence à l'an 1, chaque âge suivant à l'an 0.
            var mixed = Ages(false, true);
            t.Equal("A1-1", mixed.FormatYear(0), "le premier âge commence à l'an 1");
            t.Equal("A1-1000", mixed.FormatYear(999), "…et finit à l'an 1 000");
            t.Equal("A2-0", mixed.FormatYear(1000), "le deuxième âge commence à l'an 0");
            t.Equal("A2-499", mixed.FormatYear(1499), "…et finit à l'an 499 (500 années)");
            t.Equal("A3-0", mixed.FormatYear(1500), "le troisième aussi");
            t.Equal(1000L, Year(mixed, "A2-0"), "« A2-0 » se relit : la première année du deuxième âge");
            t.Equal(1499L, Year(mixed, "A2-499"), "« A2-499 » : sa dernière");
            t.Check(!Reads(mixed, "A2-500"), "« A2-500 » déborde du deuxième âge");
            t.Check(!Reads(mixed, "A1-0"), "pas d'an 0 dans le premier âge");
            t.Equal(0L, Year(mixed, "A1-1"), "« A1-1 » reste l'origine");
            for (long year = 990; year < 1510; year += 7)
                t.Check(Year(mixed, mixed.FormatYear(year)) == year, "aller-retour de l'année " + year + " (" + mixed.FormatYear(year) + ")");

            // Comme avant : sans la case, chaque âge commence à l'an 1.
            var plain = Ages(false, false);
            t.Check(plain.FormatYear(1000) == "A2-1" && Year(plain, "A2-1") == 1000 && !Reads(plain, "A2-0"), "case décochée : A2-1, et pas d'« A2-0 »");
            // L'an 0 partout.
            var zero = Ages(true, true);
            t.Check(zero.FormatYear(0) == "A1-0" && zero.FormatYear(1000) == "A2-0" && Year(zero, "A1-0") == 0, "les deux cases : A1-0 et A2-0");
            // L'an 0 pour la frise seulement.
            var first = Ages(true, false);
            t.Check(first.FormatYear(0) == "A1-0" && first.FormatYear(999) == "A1-999" && first.FormatYear(1000) == "A2-1", "an 0 de la frise seul : A1-0 puis A2-1");

            // Années cumulées : la case des segments ne joue pas.
            var cumulative = Ages(false, true);
            cumulative.Cumulative = true;
            t.Check(cumulative.FormatYear(1000) == "A2-1001" && cumulative.FirstYearOf(1) == 1, "années cumulatives : la case des segments est sans effet");
            // Sans segments non plus.
            var flat = Ages(false, true);
            flat.Segmented = false;
            t.Check(flat.FirstYearOf(1) == 1 && flat.DisplayYear(0) == 1, "sans découpage : sans effet");

            // Le clone la garde ; le .plot aussi ; un .plot d'avant relit l'ancienne règle.
            t.Check(mixed.Clone().SegmentYearZero && !plain.Clone().SegmentYearZero, "le clone garde la case");
            foreach (var system in new[] { mixed, plain, zero, first })
            {
                var back = TimelineJson.ReadSystems(Json.Parse(Json.Write(TimelineJson.BuildSystems(new List<TimeSystem> { system }))))[0];
                t.Check(back.YearZero == system.YearZero && back.SegmentYearZero == system.SegmentYearZero,
                    "aller-retour JSON (an 0 : " + system.YearZero + ", segments : " + system.SegmentYearZero + ")");
            }
            var legacy = TimelineJson.ReadSystems(Json.Parse("[{\"id\":\"x\",\"name\":\"Avant\",\"segmented\":true,\"yearZero\":true,\"yearPattern\":\"E{s}.{a}\","
                + "\"segments\":[{\"name\":\"a\",\"length\":10},{\"name\":\"b\"}],\"months\":[{\"name\":\"m\",\"days\":30}]}]"))[0];
            t.Check(legacy.SegmentYearZero && legacy.FormatYear(10) == "E2.0", "un système d'avant avec l'an 0 : ses segments gardent leur an 0 (E2.0)");
            var legacyOne = TimelineJson.ReadSystems(Json.Parse("[{\"id\":\"y\",\"name\":\"Avant\",\"segmented\":true,\"yearPattern\":\"E{s}.{a}\","
                + "\"segments\":[{\"name\":\"a\",\"length\":10},{\"name\":\"b\"}],\"months\":[{\"name\":\"m\",\"days\":30}]}]"))[0];
            t.Check(!legacyOne.SegmentYearZero && legacyOne.FormatYear(10) == "E2.1", "…et sans l'an 0 : E2.1, comme avant");

            // Cocher la case sur un système existant : la date garde ce qui est ÉCRIT.
            var moved = TimeSystemChange.Convert(plain, mixed, new TimePoint(1000 + 4)); // A2-5
            t.Check(moved != null && mixed.FormatYear(moved.Year) == "A2-5", "la case cochée après coup : « A2-5 » reste « A2-5 »");
            var firstAge = TimeSystemChange.Convert(plain, mixed, new TimePoint(41)); // A1-42
            t.Check(firstAge != null && mixed.FormatYear(firstAge.Year) == "A1-42", "…et le premier âge ne bouge pas");
            var lost = TimeSystemChange.Convert(mixed, plain, new TimePoint(1000)); // A2-0 n'existe plus
            t.Check(lost == null, "la case décochée : « A2-0 » n'existe plus (la brique attendra)");
        }

        private static void SegmentYears(Harness t)
        {
            var system = Ages(false, false);
            var events = new List<TimelineEvent>
            {
                new TimelineEvent { Title = "Avant tout", Start = new TimePoint(-300) },
                new TimelineEvent { Title = "Milieu", Start = new TimePoint(700) },
                new TimelineEvent { Title = "Dernier", Start = new TimePoint(1620) }
            };
            var stats = Timelines.Stats(system, events);
            // 1 000 + 500 + l'âge ouvert jusqu'au dernier événement (1 500 → 1 621) = 1 621.
            t.Equal(1621L, stats.SegmentYears, "les années dans les segments : 1 000 + 500 + 121 de l'âge ouvert");
            t.Equal(1921L, stats.TotalYears, "la durée totale compte aussi les 300 années négatives");
            t.Check(stats.SegmentYears == stats.TotalYears - 300, "…l'écart est exactement ce qui précède l'origine");
            var closed = Ages(false, false);
            closed.Segments[2].Length = 250;
            t.Equal(1750L, Timelines.Stats(closed, new List<TimelineEvent>()).SegmentYears, "tous les segments fermés : la somme des durées");
            var flat = Ages(false, false);
            flat.Segmented = false;
            t.Equal(0L, Timelines.Stats(flat, events).SegmentYears, "sans segments : rien à compter");
        }

        private static void Deceased(Harness t)
        {
            var project = Project.CreateNew();
            var timeline = TimelineSamples.Seed(project);
            project.RelinkParents();
            var system = Timelines.SystemOf(project, timeline);
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Aïeul", TemplateId = project.CharacterTemplate().Id };
            project.Category(Project.KeySheets).Children.Add(sheet);
            project.RelinkParents();
            foreach (var field in project.TemplateOf(sheet).Fields)
            {
                if (field.Name == SheetDefaults.FieldBirthDate) sheet.FieldValues[field.Id] = TimeLinks.Markup(timeline.Title, "E2.10", null);
                if (field.Name == SheetDefaults.FieldDeathDate) sheet.FieldValues[field.Id] = TimeLinks.Markup(timeline.Title, "E2.60", null);
            }
            TimePoint at;
            system.TryParse("E2.80", out at);
            var status = Epochs.Evaluate(project, sheet, system, at);
            t.Check(status.State == LifeState.Dead && status.Label.StartsWith("Décédé·e à 50 ans"), "à l'époque, la fiche dit « Décédé·e » et plus « Mort·e » (" + status.Label + ")");
            t.Check(status.Label.IndexOf("Mort", StringComparison.OrdinalIgnoreCase) < 0, "…nulle part « mort »");
            project.Epoch = new EpochState { Enabled = true, TimelineId = timeline.Id, Notation = "E2.80" };
            var note = Epochs.AgeNote(project, sheet) ?? "";
            t.Check(note.StartsWith("(décédé·e à 50 ans"), "la vue wiki aussi : « " + note + " »");
        }

        private static void NotebookNotes(Harness t)
        {
            t.Equal("Courses", Notebook.TitleOf("# Courses\n- pain"), "le titre d'une note : sa première ligne, sans le « # »");
            t.Equal("Idée pour le tome 2", Notebook.TitleOf("\n\n**Idée** pour le _tome 2_\nsuite"), "…sans gras ni italique, lignes vides sautées");
            t.Equal("acheter du sel", Notebook.TitleOf("- [ ] acheter du sel"), "…sans la case à cocher");
            t.Equal("Raeza", Notebook.TitleOf("> [[Raeza]]"), "…sans citation ni crochets de lien");
            t.Equal("Vrai titre", Notebook.TitleOf("---\nVrai titre"), "un filet n'est pas un titre");
            t.Equal(Notebook.Untitled, Notebook.TitleOf("   \n"), "une note vide : « Note sans titre »");
            t.Check(Notebook.TitleOf(new string('a', 200)).Length == 60, "un long titre est abrégé");

            var dir = Path.Combine(Path.GetTempPath(), "marabook-c66-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var savedOverride = AppSettings.PathOverride;
            AppSettings.PathOverride = Path.Combine(dir, "settings.json");
            Notebook.Reset();
            try
            {
                t.Check(Notebook.Notes.Count == 1 && Notebook.Current.Text == "", "un carnet neuf a sa première page, vide");
                t.Check(!File.Exists(Notebook.FilePath()), "…et n'écrit rien tant qu'on n'enregistre pas");
                Notebook.Current.Text = "# Première\nligne « accentuée » — é";
                var second = Notebook.Add();
                second.Text = "Seconde note";
                t.Check(Notebook.Notes.Count == 2 && ReferenceEquals(Notebook.Current, second), "une note ajoutée devient la note sous les yeux");
                t.Check(Notebook.Save() && File.Exists(Notebook.FilePath()) && Path.GetFileName(Notebook.FilePath()) == "carnet.json", "le carnet s'écrit dans carnet.json, à côté des réglages");
                t.Check(!File.Exists(Notebook.FilePath() + ".tmp"), "…sans fichier temporaire oublié");
                Notebook.Reset();
                t.Check(Notebook.Notes.Count == 2 && Notebook.Notes[0].Text == "# Première\nligne « accentuée » — é" && Notebook.Current.Text == "Seconde note",
                    "relu : les deux notes, leur texte, et la note courante");
                Notebook.Current = Notebook.Notes[0];
                t.Check(Notebook.Save(), "un second enregistrement remplace le premier");
                Notebook.Reset();
                t.Equal("Première", Notebook.TitleOf(Notebook.Current.Text), "la note courante est retenue d'une session à l'autre");
                Notebook.Remove(Notebook.Current);
                t.Check(Notebook.Notes.Count == 1 && Notebook.Current.Text == "Seconde note", "une note retirée : la voisine prend sa place");
                Notebook.Remove(Notebook.Current);
                t.Check(Notebook.Notes.Count == 1 && Notebook.Current.Text == "", "la dernière retirée : le carnet garde une page vide");
                File.WriteAllText(Notebook.FilePath(), "{ ceci n'est pas du json");
                Notebook.Reset();
                t.Check(Notebook.Notes.Count == 1 && Notebook.Current.Text == "", "un carnet illisible ne bloque rien : une page vide");
            }
            finally
            {
                AppSettings.PathOverride = savedOverride;
                Notebook.Reset();
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }

            // Le rail : le Carnet est toujours offert, sous le Lexique, et toujours disponible avec un projet.
            foreach (var kind in new ItemKind?[] { ItemKind.Text, ItemKind.Book, ItemKind.Sheet, ItemKind.Category, ItemKind.Timeline, null })
            {
                var offered = RightPanels.Offered(kind, false, true);
                var lexicon = Array.IndexOf(offered, RightPanel.Lexicon);
                t.Check(lexicon >= 0 && Array.IndexOf(offered, RightPanel.Notebook) == lexicon + 1, "le Carnet suit le Lexique au rail (" + (kind == null ? "rien" : kind.ToString()) + ")");
                t.Check(RightPanels.Offers(kind, RightPanel.Notebook) && RightPanels.Available(RightPanel.Notebook, false, true, kind), "…offert et disponible sans Lexique aussi");
            }
            t.Check(!RightPanels.Available(RightPanel.Notebook, true, true, ItemKind.Text) && !RightPanels.Available(RightPanel.Notebook, false, false, null), "colonne masquée ou pas de projet : indisponible");
            t.Check(RightPanels.Parse("notebook") == RightPanel.Notebook && !RightPanels.DescribesCurrent(RightPanel.Notebook), "son nom persisté ; c'est un outil (après le filet)");
        }
    }
}

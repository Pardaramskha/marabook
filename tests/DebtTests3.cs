using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.Tests
{
    public static partial class DebtTests
    {
        // ------------------------------------------------------------ l'index par identifiant

        private static void Index(Harness t)
        {
            var project = Project.CreateNew();
            var writings = project.Category(Project.KeyWritings);
            var items = new List<BinderItem>();
            for (var f = 0; f < 40; f++)
            {
                var folder = new BinderItem { Kind = ItemKind.Folder, Title = "Dossier " + f, Parent = writings };
                writings.Children.Add(folder);
                for (var i = 0; i < 100; i++)
                {
                    var text = new BinderItem { Kind = ItemKind.Text, Title = "Écrit " + f + "." + i, Parent = folder, Document = new TextDocument() };
                    folder.Children.Add(text);
                    items.Add(text);
                }
            }
            var before = project.IndexRebuilds;
            var all = true;
            foreach (var item in items) if (!ReferenceEquals(project.FindById(item.Id), item)) all = false;
            t.Check(all, "4 000 éléments se retrouvent par leur identifiant");
            t.Check(project.IndexRebuilds - before == 1, "…la table n'a été bâtie qu'une fois (" + (project.IndexRebuilds - before) + ")");
            t.Check(project.FindById(null) == null && project.FindById("inconnu") == null, "un identifiant nul ou inconnu : rien");

            // L'arbre change sans prévenir : la table n'est jamais crue sur parole.
            var moved = items[1234];
            moved.Parent.Children.Remove(moved);
            t.Check(project.FindById(moved.Id) == null, "un élément retiré de l'arbre n'est plus trouvé, bien qu'il soit encore dans la table");
            var fresh = new BinderItem { Kind = ItemKind.Text, Title = "Nouveau", Parent = writings, Document = new TextDocument() };
            writings.Children.Add(fresh);
            t.Check(ReferenceEquals(project.FindById(fresh.Id), fresh), "un élément ajouté sans prévenir est trouvé");
            moved.Parent = writings;
            writings.Children.Add(moved);
            t.Check(ReferenceEquals(project.FindById(moved.Id), moved), "un élément rattaché ailleurs est retrouvé");
            var renamed = items[7];
            var oldId = renamed.Id;
            renamed.Id = "identifiant-change";
            t.Check(project.FindById(oldId) == null && ReferenceEquals(project.FindById("identifiant-change"), renamed), "un identifiant changé : l'ancien ne mène plus nulle part, le nouveau oui");
            var trash = project.Trash;
            var thrown = items[50];
            thrown.Parent.Children.Remove(thrown);
            thrown.Parent = trash;
            trash.Children.Add(thrown);
            t.Check(ReferenceEquals(project.FindById(thrown.Id), thrown), "à la Corbeille, un élément reste trouvable");

            // La mesure : mille recherches, avec la table et par le parcours d'avant.
            project.FindById(items[0].Id);
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 1000; i++) project.FindById(items[(i * 37) % items.Count].Id);
            var indexed = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            for (var i = 0; i < 1000; i++)
            {
                var id = items[(i * 37) % items.Count].Id;
                foreach (var item in project.AllItems()) if (item.Id == id) break;
            }
            var walked = watch.Elapsed.TotalMilliseconds;
            Console.WriteLine("  mesure : 1 000 recherches par identifiant parmi 4 000 éléments — " + indexed.ToString("0.0") + " ms avec la table, " + walked.ToString("0.0") + " ms par parcours");
            t.Check(indexed < walked / 5, "la table rend les recherches au moins cinq fois plus rapides (" + indexed.ToString("0.0") + " ms contre " + walked.ToString("0.0") + ")");
            project.ForgetIndex();
            t.Check(ReferenceEquals(project.FindById(fresh.Id), fresh), "la table oubliée se refait à la demande");
        }

        // ------------------------------------------------------------ l'annulation par différence

        private static void Undo(Harness t)
        {
            // L'égalité de deux paragraphes couvre TOUS leurs champs : chaque champ public est modifié à son tour.
            var paragraph = new TextParagraph { StyleId = "body" };
            paragraph.Runs.Clear();
            paragraph.Runs.Add(new TextRun { Text = "Un texte" });
            t.Check(PivotEdit.SameParagraph(paragraph, PivotEdit.CloneParagraph(paragraph)), "un paragraphe et sa copie sont identiques");
            var missed = new List<string>();
            foreach (var field in typeof(TextRun).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var copy = PivotEdit.CloneParagraph(paragraph);
                var run = copy.Runs[0];
                object value;
                var type = field.FieldType;
                if (type == typeof(string)) value = (string)field.GetValue(run) + "x";
                else if (type == typeof(bool)) value = !(bool)field.GetValue(run);
                else if (type == typeof(bool?)) value = true;
                else if (type == typeof(double?)) value = 3.5;
                else if (type == typeof(ImageLayout)) value = new ImageLayout();
                else { missed.Add(field.Name + " (type non prévu par le test)"); continue; }
                field.SetValue(run, value);
                if (PivotEdit.SameParagraph(paragraph, copy)) missed.Add(field.Name);
            }
            foreach (var field in typeof(TextParagraph).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.Name == "Runs" || field.IsLiteral) continue;
                var copy = PivotEdit.CloneParagraph(paragraph);
                object value;
                var type = field.FieldType;
                if (type == typeof(string)) value = (string)field.GetValue(copy) + "x";
                else if (type == typeof(bool)) value = !(bool)field.GetValue(copy);
                else if (type == typeof(int)) value = (int)field.GetValue(copy) + 1;
                else if (type == typeof(double?)) value = 12.5;
                else if (!type.IsValueType)
                {
                    try { value = Activator.CreateInstance(type); }
                    catch (Exception) { missed.Add(field.Name + " (type non prévu par le test)"); continue; }
                }
                else { missed.Add(field.Name + " (type non prévu par le test)"); continue; }
                field.SetValue(copy, value);
                if (PivotEdit.SameParagraph(paragraph, copy)) missed.Add(field.Name);
            }
            t.Check(missed.Count == 0, "changer n'importe quel champ d'un run ou d'un paragraphe le rend différent" + (missed.Count == 0 ? "" : " — oubliés : " + string.Join(", ", missed.ToArray())));

            // Un point d'annulation partage les paragraphes inchangés avec le précédent.
            var document = TextDocument.FromPlainText("Un.\nDeux.\nTrois.\nQuatre.\nCinq.");
            document.Annotations.Add(new Annotation { Text = "À revoir" });
            int shared;
            var first = PivotEdit.CloneSharing(document, null, out shared);
            t.Check(shared == 0 && first.Paragraphs.Count == 5 && !ReferenceEquals(first.Paragraphs[0], document.Paragraphs[0]), "le premier point recopie tout");
            PivotEdit.InsertText(document.Paragraphs[2], 5, " Et demi");
            var second = PivotEdit.CloneSharing(document, first, out shared);
            t.Check(shared == 4 && ReferenceEquals(second.Paragraphs[0], first.Paragraphs[0]) && ReferenceEquals(second.Paragraphs[4], first.Paragraphs[4]) && !ReferenceEquals(second.Paragraphs[2], first.Paragraphs[2]),
                "après une frappe, le point suivant ne recopie que le paragraphe touché (" + shared + " partagés sur 5)");
            t.Check(PivotEdit.FlatText(first.Paragraphs[2]) == "Trois." && PivotEdit.FlatText(second.Paragraphs[2]) == "Trois Et demi.", "chaque point garde son propre texte");
            document.Paragraphs.Insert(1, PivotEdit.CloneParagraph(document.Paragraphs[0]));
            PivotEdit.InsertText(document.Paragraphs[1], 0, "Entre deux. ");
            var third = PivotEdit.CloneSharing(document, second, out shared);
            t.Check(shared == 5 && third.Paragraphs.Count == 6 && ReferenceEquals(third.Paragraphs[5], second.Paragraphs[4]), "un paragraphe inséré : ceux qui suivent sont reconnus à leur place décalée (" + shared + " partagés sur 6)");
            document.Paragraphs.RemoveAt(3);
            var fourth = PivotEdit.CloneSharing(document, third, out shared);
            t.Check(shared == 5 && fourth.Paragraphs.Count == 5, "un paragraphe ôté : tous les autres sont partagés (" + shared + " sur 5)");

            // Rendre un point : le document reprend son état, sans jamais toucher aux paragraphes partagés.
            document.Annotations[0].Text = "Changé depuis";
            document.Annotations[0].Resolved = true;
            PivotEdit.RestoreFrom(document, first);
            t.Check(document.Paragraphs.Count == 5 && PivotEdit.FlatText(document.Paragraphs[2]) == "Trois.", "rendre le premier point remet le texte d'origine");
            t.Check(document.Annotations.Count == 1 && document.Annotations[0].Text == "À revoir" && !document.Annotations[0].Resolved, "…et ses annotations, telles qu'elles étaient");
            t.Check(!ReferenceEquals(document.Paragraphs[0], first.Paragraphs[0]), "…par des copies : le point reste intact");
            PivotEdit.InsertText(document.Paragraphs[0], 0, "Modifié. ");
            document.Annotations[0].Text = "Modifiée";
            t.Check(PivotEdit.FlatText(first.Paragraphs[0]) == "Un." && PivotEdit.FlatText(second.Paragraphs[0]) == "Un." && first.Annotations[0].Text == "À revoir", "modifier le document vivant ne touche ni ce point ni ceux qui partagent ses paragraphes");

            // La capture du jour en deux temps : préparer (lourd, hors du fil), ranger (léger).
            var project = Project.CreateNew();
            var item = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre", Document = TextDocument.FromPlainText("Un texte de cinq mots.") };
            item.Parent = project.Category(Project.KeyWritings);
            item.Parent.Children.Add(item);
            var prepared = SnapshotStore.Prepare(item, item.Document, "", SnapshotOrigin.Daily);
            t.Check(prepared != null && prepared.Words == 5 && prepared.Data.Length > 0 && project.Snapshots.Count == 0, "préparer une capture ne touche pas au projet");
            t.Check(SnapshotStore.Commit(project, prepared, 20) && project.Snapshots.Count == 1 && SnapshotStore.HasToday(project, item.Id, null), "la ranger l'y fait entrer");
            t.Check(!SnapshotStore.Commit(project, SnapshotStore.Prepare(item, item.Document, "", SnapshotOrigin.Daily), 20) && project.Snapshots.Count == 1, "une capture qui redit la dernière n'entre pas");
            t.Check(SnapshotStore.Prepare(new BinderItem { Kind = ItemKind.Sheet }, item.Document, "", null) == null && !SnapshotStore.Commit(project, null, 20), "une fiche, ou rien : rien");
        }

        // ------------------------------------------------------------ les images écartées, les idéogrammes

        private static void SkippedImages(Harness t)
        {
            t.Equal("2 images EMF, 1 image SVG", ImportedImages.Describe(new[] { "image1.emf", "dessin.svg", "image2.EMF" }), "les images écartées, comptées par format");
            t.Equal("1 image de format inconnu", ImportedImages.Describe(new[] { "sansextension" }), "un fichier sans extension");
            var project = Project.CreateNew();
            var seen = new List<string>();
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    foreach (var name in new[] { "word/media/image1.emf", "word/media/image2.wmf" })
                        using (var entry = zip.CreateEntry(name).Open()) entry.Write(new byte[] { 1, 2, 3 }, 0, 3);
                }
                stream.Position = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var images = new ImportedImages(project, zip);
                    ImportedImages.SkippedSink = seen.Add;
                    try
                    {
                        t.Check(images.Store("word/media/image1.emf") == null && images.Store("word/media/image2.wmf") == null && images.Store("word/media/absente.png") == null, "une image EMF ou WMF n'entre pas dans le projet");
                        images.Store("word/media/image1.emf");
                    }
                    finally { ImportedImages.SkippedSink = null; }
                    t.Check(images.Skipped == 3 && seen.Count == 2 && seen[0] == "image1.emf" && seen[1] == "image2.wmf", "…mais elle est SIGNALÉE, une fois, par son nom ; une entrée absente ne l'est pas (" + string.Join(", ", seen.ToArray()) + ")");
                }
            }
            t.Check(ImportedImages.SkippedAdvice.Contains("PNG"), "le message dit quoi faire");

            // Les idéogrammes : où la ligne peut se couper.
            t.Check(CjkBreaks.Split("bonjour") == null && CjkBreaks.Split("") == null && !CjkBreaks.Contains("été"), "un mot latin reste entier");
            var cuts = CjkBreaks.Split("私は「本」を読む。");
            var cutsText = cuts == null ? "-" : string.Join(",", cuts.ConvertAll(delegate(int i) { return i.ToString(); }).ToArray());
            t.Check(cutsText == "0,1,2,5,6,7", "entre deux idéogrammes, jamais devant une fermante ni après une ouvrante (" + cutsText + ")");
            var mixed = CjkBreaks.Split("Tokyo東京2026");
            t.Check(mixed != null && mixed.Count == 4 && mixed[1] == 5 && mixed[2] == 6 && mixed[3] == 7, "du latin mêlé : il reste d'un bloc, les idéogrammes se séparent");

            // Composé : un paragraphe de 400 idéogrammes passe à la ligne, et aucune ligne ne déborde.
            var cjk = new StringBuilder();
            for (var i = 0; i < 400; i++) cjk.Append(i % 17 == 16 ? '。' : (char)(0x4E00 + (i * 31) % 500));
            var cjkProject = Project.CreateNew();
            var setup = new PageSetup();
            var engine = new CompositionEngine(TextDocument.FromPlainText(cjk.ToString()), cjkProject.Styles, setup, cjkProject, true, new StubGlyphMetrics());
            engine.ComposeAll();
            var lines = engine.Current.Paragraphs[0].Lines;
            var overflow = false;
            var badStart = false;
            var covered = 0;
            foreach (var line in lines)
            {
                double right = 0;
                var first = true;
                foreach (var piece in line.Pieces)
                {
                    right = Math.Max(right, piece.Origin.X + piece.VisualWidth());
                    if (piece.SourceStart >= 0) covered += piece.SourceLength;
                    if (first && piece.HasText) { if (piece.Text.StartsWith("。", StringComparison.Ordinal)) badStart = true; first = false; }
                }
                if (right > setup.ContentWidthPx + 1) overflow = true;
            }
            t.Check(lines.Count > 5 && !overflow, "un paragraphe de 400 idéogrammes sans espace : " + lines.Count + " lignes, aucune ne déborde de la page");
            t.Check(!badStart, "…et aucune ligne ne commence par un point");
            t.Equal(400, covered, "chaque signe garde sa place dans le texte (le curseur s'y pose)");
        }
    }
}

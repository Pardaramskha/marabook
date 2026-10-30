using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C58 — solidité de l'enregistrement (1.1.0, 30/10) :
    /// l'écriture différentielle (les entrées inchangées recopiées telles
    /// quelles de l'ancien .plot, le reste recompressé, le fichier relu à
    /// l'identique par ZipArchive), l'index posé au chargement, un fichier
    /// étranger ou bougé qui annule l'index, et les copies horodatées
    /// (cadence, rotation, dossier commun, nom reconnu).</summary>
    public static class BackupTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C58 — enregistrement différentiel et copies horodatées (1.1.0, 30/10)");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-backups");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            try
            {
                Digests(t);
                Differential(t, dir);
                IndexFromLoad(t, dir);
                ForeignFile(t, dir);
                RecoveryUntouched(t, dir);
                Timestamped(t, dir);
                SharedFolder(t, dir);
                Timing(t, dir);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static Project BuildProject(int chapters)
        {
            var project = Project.CreateNew();
            project.Name = "Solidité";
            var writings = project.Category(Project.KeyWritings);
            var book = new BinderItem { Kind = ItemKind.Book, Title = "Livre", ImageId = "couv" };
            writings.Children.Add(book);
            var text = new StringBuilder();
            for (var i = 0; i < 40; i++) text.Append("Il était une fois, au chapitre {0}, une phrase qui se répète pour peser son poids. ");
            for (var i = 0; i < chapters; i++)
            {
                var chapter = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre " + (i + 1) };
                chapter.Document = TextDocument.FromPlainText(text.ToString().Replace("{0}", (i + 1).ToString()));
                book.Children.Add(chapter);
            }
            // Une « image » : des octets déjà compressés (un PNG l'est), donc
            // stockée sans deflate.
            var bytes = new byte[40_000];
            new Random(7).NextBytes(bytes);
            project.Images["couv"] = new ProjectImage { Bytes = bytes, Extension = ".png" };
            // Un média de recherche et un instantané : les autres natures d'entrée.
            var media = new BinderItem { Kind = ItemKind.Media, Title = "Note", MediaExtension = ".txt", MediaBytes = Encoding.UTF8.GetBytes("un média de recherche, texte compressible compressible compressible") };
            project.Category(Project.KeyResearch).Children.Add(media);
            project.RelinkParents();
            SnapshotStore.Capture(project, book.Children[0], "première", "manuel", 20);
            return project;
        }

        /// <summary>Le livre du projet de test (CreateNew sème déjà un écrit dans Écrits).</summary>
        private static BinderItem Book(Project project)
        {
            foreach (var item in project.Category(Project.KeyWritings).Children)
                if (item.Kind == ItemKind.Book) return item;
            return null;
        }

        private static List<string> EntryNames(string path)
        {
            var names = new List<string>();
            using (var stream = File.OpenRead(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                foreach (var entry in archive.Entries) names.Add(entry.FullName);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static Dictionary<string, PlotZip.Located> Directory_(string path)
        {
            using (var stream = File.OpenRead(path))
                return PlotZip.ReadDirectory(stream);
        }

        private static void Digests(Harness t)
        {
            var a = Encoding.UTF8.GetBytes("bonjour");
            var b = Encoding.UTF8.GetBytes("bonjour");
            var c = Encoding.UTF8.GetBytes("bonjouR");
            t.Check(Digest.Of(a, 0, a.Length, null).Same(Digest.Of(b, 0, b.Length, null)), "deux octets égaux ont la même empreinte");
            t.Check(!Digest.Of(a, 0, a.Length, null).Same(Digest.Of(c, 0, c.Length, null)), "un octet de différence change l'empreinte");
            var head = Encoding.UTF8.GetBytes("bon");
            var tail = Encoding.UTF8.GetBytes("jour");
            t.Check(Digest.Of(head, 0, head.Length, tail).Same(Digest.Of(a, 0, a.Length, null)), "tête + queue = la même empreinte que les octets réunis");
            t.Equal(0x00000000u, Digest.Crc32(new byte[0], 0, 0), "CRC-32 du vide : 0");
            var check = Encoding.ASCII.GetBytes("123456789");
            t.Equal(0xCBF43926u, Digest.Crc32(check, 0, check.Length), "CRC-32 de « 123456789 » : la valeur de référence");
        }

        private static void Differential(Harness t, string dir)
        {
            var path = Path.Combine(dir, "diff.plot");
            var project = BuildProject(12);
            var first = PlotFile.Write(PlotFile.Prepare(project), path);
            t.Check(first.Copied == 0 && first.Packed == first.Index.Entries.Count && first.Packed > 15, "première écriture : tout compressé (" + first.Packed + " entrées)");
            t.Check(first.Index.Describes(path), "l'index rendu décrit le fichier écrit");
            var loaded = PlotFile.Load(path);
            t.Equal(12, Book(loaded).Children.Count, "le fichier du copieur se relit par ZipArchive");
            var located = Directory_(path);
            t.Check(located != null && located["images/couv.png"].Method == 0, "l'image est STOCKÉE (méthode 0), pas deflatée");
            t.Check(located["texts/" + Book(loaded).Children[0].Id + ".json"].Method == 8, "un texte est deflaté (méthode 8)");

            // Un seul chapitre change : lui et le manifeste sont recompressés,
            // tout le reste est recopié.
            var chapter = Book(project).Children[3];
            chapter.Document = TextDocument.FromPlainText("Le chapitre quatre, réécrit.");
            var second = PlotFile.Write(PlotFile.Prepare(project), path, false, null, first.Index);
            t.Equal(2, second.Packed, "deuxième écriture : le manifeste et le chapitre touché recompressés");
            t.Equal(first.Index.Entries.Count - 2, second.Copied, "…tout le reste recopié tel quel (" + second.Copied + ")");
            var reloaded = PlotFile.Load(path);
            var fourth = Book(reloaded).Children[3];
            t.Equal("Le chapitre quatre, réécrit.", fourth.Document.Paragraphs[0].ToPlainText(), "le chapitre réécrit est dans le fichier");
            t.Equal(Book(project).Children[5].Document.Paragraphs[0].ToPlainText(),
                Book(reloaded).Children[5].Document.Paragraphs[0].ToPlainText(), "un chapitre recopié se relit à l'identique");
            t.Check(reloaded.Images.ContainsKey("couv") && reloaded.Images["couv"].Bytes.Length == 40_000, "l'image recopiée est entière");
            t.Equal(1, reloaded.Snapshots.Count, "l'instantané recopié est là");
            t.Equal(project.SaveId, PlotFile.ReadSaveId(path), "l'empreinte du fichier est celle du plan");
            t.Check(File.Exists(path + ".bak"), "le .bak roulant est toujours écrit");
            t.Check(EntryNames(path).Count == first.Index.Entries.Count, "aucune entrée perdue ni doublée");

            // Rien ne change : tout recopié sauf le manifeste (date, empreinte).
            var third = PlotFile.Write(PlotFile.Prepare(project), path, false, null, second.Index);
            t.Equal(1, third.Packed, "sans changement, seul le manifeste est recompressé");
            t.Check(second.Index.Entries.Count - 1 == third.Copied, "…le reste recopié");

            // L'index d'un AUTRE chemin n'est pas suivi.
            var elsewhere = Path.Combine(dir, "ailleurs.plot");
            var moved = PlotFile.Write(PlotFile.Prepare(project), elsewhere, false, null, third.Index);
            t.Check(moved.Copied == 0 && moved.Packed == third.Index.Entries.Count, "écrit sous un autre chemin : l'index de l'ancien est ignoré, tout compressé");
            t.Equal(12, Book(PlotFile.Load(elsewhere)).Children.Count, "…et le fichier est complet");
        }

        private static void IndexFromLoad(Harness t, string dir)
        {
            var path = Path.Combine(dir, "charge.plot");
            var project = BuildProject(6);
            PlotFile.Save(project, path);
            var loaded = PlotFile.Load(path);
            t.Check(loaded.DiskIndex != null && loaded.DiskIndex.Describes(path), "le chargement pose l'index du fichier lu sur le projet");
            t.Equal(loaded.SaveId, loaded.DiskIndex.SaveId, "…avec son empreinte");
            t.Check(loaded.DiskIndex.Entries.ContainsKey("manifest.json") && loaded.DiskIndex.Entries.ContainsKey("images/couv.png"), "…et l'empreinte de chaque entrée");
            var result = PlotFile.Write(PlotFile.Prepare(loaded), path, false, null, loaded.DiskIndex);
            t.Equal(1, result.Packed, "le premier enregistrement après lecture ne recompresse que le manifeste (la sérialisation est stable)");
            var again = PlotFile.Load(path);
            t.Equal(6, Book(again).Children.Count, "…et le fichier se relit");
            t.Equal(null, Project.CreateNew().DiskIndex, "un projet neuf n'a pas d'index");
        }

        private static void ForeignFile(Harness t, string dir)
        {
            var path = Path.Combine(dir, "etranger.plot");
            var project = BuildProject(4);
            var first = PlotFile.Write(PlotFile.Prepare(project), path);
            // Le fichier est remplacé par une AUTRE version (synchronisation) :
            // l'index ne le décrit plus, rien n'en est recopié.
            var other = BuildProject(5);
            PlotFile.Save(other, path + ".autre");
            File.Copy(path + ".autre", path, true);
            t.Check(!first.Index.Describes(path), "un fichier remplacé n'est plus décrit par l'index");
            var result = PlotFile.Write(PlotFile.Prepare(project), path, false, null, first.Index);
            t.Check(result.Copied == 0, "rien n'est recopié d'un fichier qui n'est plus le nôtre");
            t.Equal(4, Book(PlotFile.Load(path)).Children.Count, "…le projet est écrit entier");

            // Un fichier « touché » (même contenu, autre date) : même chose, sans mal.
            var second = PlotFile.Write(PlotFile.Prepare(project), path, false, null, result.Index);
            t.Check(second.Copied > 0, "index à jour : recopie");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
            var third = PlotFile.Write(PlotFile.Prepare(project), path, false, null, second.Index);
            t.Check(third.Copied == 0 && Book(PlotFile.Load(path)).Children.Count == 4, "fichier touché : tout recompressé, fichier complet");

            // Un fichier tronqué sous l'index : le copieur ne lit rien de faux.
            var fourth = PlotFile.Write(PlotFile.Prepare(project), path, false, null, third.Index);
            var bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, new ArraySegment<byte>(bytes, 0, bytes.Length / 2).ToArray());
            var stamp = fourth.Index.Stamp;
            fourth.Index.Stamp = PlotFile.Stamp(path); // on force l'index à « décrire » le fichier tronqué
            var fifth = PlotFile.Write(PlotFile.Prepare(project), path, false, null, fourth.Index);
            t.Check(fifth.Copied == 0 && Book(PlotFile.Load(path)).Children.Count == 4, "fichier tronqué : répertoire illisible, tout recompressé, fichier complet (" + stamp.Length + ")");

            // La garde (1.0.3) tient toujours : empreinte attendue ≠ empreinte sur le disque.
            var refused = false;
            try { PlotFile.Write(PlotFile.Prepare(project), path, false, "pas-celle-la", fifth.Index); }
            catch (PlotFile.PlotConflictException) { refused = true; }
            t.Check(refused && !File.Exists(path + ".tmp"), "l'écriture gardée refuse encore, sans .tmp orphelin");
        }

        private static void RecoveryUntouched(Harness t, string dir)
        {
            var path = Path.Combine(dir, "secours.plot");
            var project = BuildProject(3);
            var full = PlotFile.Write(PlotFile.Prepare(project), path);
            var rescue = PlotFile.Write(PlotFile.Prepare(project, true), path, true, null, full.Index);
            t.Check(rescue.Copied == 0, "le secours (textes seuls) ne recopie rien");
            t.Check(!File.Exists(path + ".bak"), "…et n'écrit pas de .bak");
            t.Check(!EntryNames(path).Contains("images/couv.png"), "…ni l'image");
        }

        private static void Timestamped(Harness t, string dir)
        {
            var path = Path.Combine(dir, "roman.plot");
            var project = BuildProject(2);
            PlotFile.Save(project, path);
            var policy = new BackupPolicy { Keep = 3, IntervalMinutes = 30 };
            t.Equal(Path.Combine(dir, "roman" + Backups.FolderSuffix), Backups.FolderFor(path, policy), "le dossier des copies est « roman.sauvegardes » à côté du projet");
            string error;
            var t0 = new DateTime(2026, 10, 30, 14, 12, 5);
            var made = Backups.Record(path, policy, t0, out error);
            t.Check(made != null && error == null && File.Exists(made), "une première copie est faite (" + (error ?? Path.GetFileName(made ?? "")) + ")");
            t.Equal("roman-2026-10-30-14h12m05.plot", Path.GetFileName(made ?? ""), "…nommée par la date");
            t.Check(Backups.IsBackupName(made) && !Backups.IsBackupName(path), "le nom d'une copie se reconnaît, celui du projet non");
            t.Equal(t0, Backups.DateOf(made), "la date se relit du nom");
            t.Check(Book(PlotFile.Load(made)).Children.Count == 2, "la copie est un .plot complet qui s'ouvre");
            t.Equal(null, Backups.Record(path, policy, t0.AddMinutes(10), out error), "dix minutes plus tard : pas de nouvelle copie (intervalle de 30)");
            t.Check(Backups.Record(path, policy, t0.AddMinutes(30), out error) != null, "trente minutes plus tard : une copie");
            Backups.Record(path, policy, t0.AddMinutes(61), out error);
            Backups.Record(path, policy, t0.AddMinutes(92), out error);
            var list = Backups.List(path, policy);
            t.Equal(3, list.Count, "au-delà de Keep (3), les plus anciennes sont retirées");
            t.Check(list[0].Date == t0.AddMinutes(92) && list[2].Date == t0.AddMinutes(30), "…la liste va de la plus récente à la plus ancienne, la première copie est partie");
            t.Check(!File.Exists(made), "…sur le disque aussi");
            policy.Enabled = false;
            t.Equal(null, Backups.Record(path, policy, t0.AddHours(5), out error), "désactivé : rien");
            t.Equal(null, Backups.Record(path, null, t0.AddHours(5), out error), "sans politique : rien, sans erreur");
            policy.Enabled = true;
            policy.IntervalMinutes = 0;
            t.Check(Backups.Record(path, policy, t0.AddMinutes(93), out error) != null, "intervalle 0 : une copie à chaque enregistrement");
            t.Equal(3, Backups.List(path, policy).Count, "…toujours plafonné");
            t.Equal(BackupPolicy.MaxKeep, BackupPolicy.ClampKeep(10_000), "Keep se borne");
            t.Equal(BackupPolicy.MinIntervalMinutes, BackupPolicy.ClampInterval(-5), "l'intervalle aussi");
            // Un projet sans copie : la liste est vide, sans dossier créé.
            t.Equal(0, Backups.List(Path.Combine(dir, "vierge.plot"), policy).Count, "projet sans copies : liste vide");
            t.Check(!Directory.Exists(Backups.FolderFor(Path.Combine(dir, "vierge.plot"), policy)), "…sans dossier créé");
        }

        private static void SharedFolder(Harness t, string dir)
        {
            var shared = Path.Combine(dir, "toutes-mes-sauvegardes");
            var policy = new BackupPolicy { Folder = shared, Keep = 5, IntervalMinutes = 0 };
            var a = Path.Combine(dir, "alpha.plot");
            var b = Path.Combine(dir, "beta.plot");
            PlotFile.Save(BuildProject(1), a);
            PlotFile.Save(BuildProject(1), b);
            t.Equal(Path.Combine(shared, "alpha"), Backups.FolderFor(a, policy), "dossier commun : un sous-dossier au nom du projet");
            string error;
            var now = new DateTime(2026, 10, 30, 9, 0, 0);
            Backups.Record(a, policy, now, out error);
            Backups.Record(a, policy, now.AddMinutes(1), out error);
            Backups.Record(b, policy, now, out error);
            t.Equal(2, Backups.List(a, policy).Count, "alpha a ses deux copies");
            t.Equal(1, Backups.List(b, policy).Count, "beta la sienne, à part");
            t.Check(File.Exists(Path.Combine(shared, "beta", "beta-2026-10-30-09h00m00.plot")), "…au bon endroit");
        }

        /// <summary>Pas une assertion : la mesure, pour PLAN.md.</summary>
        private static void Timing(Harness t, string dir)
        {
            var path = Path.Combine(dir, "mesure.plot");
            var project = BuildProject(300);
            // Des chapitres de romancier (~20 000 signes) et trois versions
            // de chacun, comme une saison d'écriture.
            var seed = new Random(11);
            var words = new[] { "le", "vent", "portait", "l'odeur", "des", "cendres", "et", "Keira", "marchait", "sans", "se", "retourner", "vers", "la", "citadelle", "où", "dormaient", "les", "gardiens", "du", "feu" };
            for (var i = 0; i < 300; i++)
            {
                var chapter = Book(project).Children[i];
                for (var s = 0; s < 3; s++)
                {
                    var text = new StringBuilder();
                    for (var w = 0; w < 3600; w++) text.Append(words[seed.Next(words.Length)]).Append(w % 17 == 16 ? ". " : " ");
                    chapter.Document = TextDocument.FromPlainText(text.ToString());
                    SnapshotStore.Capture(project, chapter, "v" + s, "manuel", 20);
                }
            }
            var plan = PlotFile.Prepare(project);
            var watch = Stopwatch.StartNew();
            var full = PlotFile.Write(plan, path);
            var fullMs = watch.ElapsedMilliseconds;
            Book(project).Children[7].Document = TextDocument.FromPlainText("Un chapitre touché.");
            plan = PlotFile.Prepare(project);
            watch.Restart();
            var diff = PlotFile.Write(plan, path, false, null, full.Index);
            var diffMs = watch.ElapsedMilliseconds;
            t.Info("mesure : 300 chapitres de ~20 000 signes + 900 instantanés (" + new FileInfo(path).Length / 1024 + " Ko) — écriture complète " + fullMs + " ms ("
                + full.Packed + " entrées compressées), différentielle " + diffMs + " ms (" + diff.Copied + " recopiées, " + diff.Packed + " compressées) ; hors sérialisation");
            t.Check(diff.Packed == 2 && diff.Copied == full.Packed - 2, "la mesure recompresse deux entrées et recopie le reste");
        }
    }
}

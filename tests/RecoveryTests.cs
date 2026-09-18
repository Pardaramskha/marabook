using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C28 — la sauvegarde de secours (18/09) : un .plot « textes
    /// seuls » (ni images, ni fichiers de recherche, ni versions) qui se
    /// rouvre comme un projet ; le témoin de session, les sessions en
    /// attente après un arrêt brutal, le nettoyage.</summary>
    public static class RecoveryTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C28 — sauvegarde de secours");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-recovery");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var previousRoot = RecoveryStore.Root;
            RecoveryStore.Root = Path.Combine(dir, "recovery");
            try
            {
                TextsOnly(t, dir);
                Sessions(t, dir);
            }
            finally
            {
                RecoveryStore.Root = previousRoot;
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        private static Project BuildProject()
        {
            var project = Project.CreateNew();
            project.Name = "Roman";
            var writings = project.Category(Project.KeyWritings);
            var text = new BinderItem { Kind = ItemKind.Text, Title = "Chapitre 1" };
            text.Document = TextDocument.FromPlainText("Il était une fois [[Gandalf|le mage]].\nFin.");
            text.Document.Paragraphs[0].Runs.Add(new TextRun { ImageId = "img1" });
            writings.Children.Add(text);
            writings.RelinkChildren();
            project.Images["img1"] = new ProjectImage { Extension = ".png", Bytes = new byte[4000] };
            var research = project.Category(Project.KeyResearch);
            var media = new BinderItem { Kind = ItemKind.Media, Title = "Photo", MediaExtension = ".jpg", MediaBytes = new byte[9000] };
            research.Children.Add(media);
            research.RelinkChildren();
            project.Snapshots.Add(new Snapshot
            {
                ItemId = text.Id,
                Date = "2026-09-18 10:00",
                Origin = SnapshotOrigin.Manual,
                Data = Encoding.UTF8.GetBytes(PlotFile.SerializeDocument(text.Document))
            });
            return project;
        }

        private static void TextsOnly(Harness t, string dir)
        {
            var project = BuildProject();
            var full = Path.Combine(dir, "full.plot");
            var lean = Path.Combine(dir, "lean.plot");
            PlotFile.Save(project, full);
            PlotFile.Save(project, lean, true);
            t.Check(new FileInfo(lean).Length < new FileInfo(full).Length, "le secours pèse moins que le .plot complet");
            t.Check(!File.Exists(lean + ".bak"), "pas de .bak pour le secours");
            PlotFile.Save(project, lean, true);
            t.Check(File.Exists(lean) && !File.Exists(lean + ".bak"), "réécrit sur place, toujours sans .bak");

            var warnings = new List<string>();
            var loaded = PlotFile.Load(lean, warnings);
            t.Equal(0, warnings.Count, "le secours se rouvre sans réserve");
            var text = loaded.FindByTitle("Chapitre 1");
            t.Check(text != null && text.Kind == ItemKind.Text, "l'écrit est là");
            t.Equal("Il était une fois [[Gandalf|le mage]].￼\nFin.", text == null ? "" : string.Join("\n", Flat(text.Document)), "le texte est intact (l'image inline reste un élément sans octets)");
            t.Equal(0, loaded.Images.Count, "aucune image dans le secours");
            var media = loaded.FindByTitle("Photo");
            t.Check(media != null && media.Kind == ItemKind.Media && media.MediaBytes == null, "le fichier de recherche est listé, sans ses octets");
            t.Equal(0, loaded.Snapshots.Count, "aucune version dans le secours");
            t.Equal("Roman", loaded.Name, "le nom du projet suit");

            var complete = PlotFile.Load(full, new List<string>());
            t.Equal(1, complete.Images.Count, "le .plot complet garde ses images");
            t.Equal(1, complete.Snapshots.Count, "…et ses versions");
        }

        private static List<string> Flat(TextDocument document)
        {
            var lines = new List<string>();
            foreach (var paragraph in document.Paragraphs) lines.Add(PivotEdit.FlatText(paragraph));
            return lines;
        }

        private static void Sessions(Harness t, string dir)
        {
            var project = BuildProject();
            var path = Path.Combine(dir, "Mon Roman.plot");
            var session = RecoveryStore.Begin(path, project.Name);
            t.Check(File.Exists(session.SessionPath), "le témoin est posé à l'ouverture");
            t.Check(!session.HasRecovery, "pas de secours avant la première écriture");
            t.Equal(RecoveryStore.Hash(path), session.Id, "l'id est l'empreinte du chemin");
            t.Equal(RecoveryStore.Hash(path.ToUpperInvariant()), session.Id, "empreinte insensible à la casse");
            t.Equal(0, RecoveryStore.Pending().Count, "notre propre session n'est pas « en attente »");
            t.Check(File.Exists(session.SessionPath), "…et son témoin n'est pas nettoyé");

            RecoveryStore.Write(project, session);
            t.Check(session.HasRecovery, "le secours est écrit");
            t.Check(session.RecoveryWritten.HasValue, "sa date est lisible");
            RecoveryStore.DropRecovery(session);
            t.Check(!session.HasRecovery && File.Exists(session.SessionPath), "après un enregistrement complet : secours retiré, témoin gardé");
            RecoveryStore.Write(project, session);
            RecoveryStore.End(session);
            t.Check(!File.Exists(session.SessionPath) && !session.HasRecovery, "fermeture propre : témoin et secours retirés");

            // Un arrêt brutal : un témoin d'un processus mort, avec secours.
            var dead = RecoveryStore.Begin(path, "Roman");
            RecoveryStore.Write(project, dead);
            Orphan(dead);
            var pending = RecoveryStore.Pending();
            t.Equal(1, pending.Count, "une session en attente après un arrêt brutal");
            t.Equal("Roman", pending.Count == 0 ? "" : pending[0].ProjectName, "…qui connaît son projet");
            t.Equal(path, pending.Count == 0 ? "" : pending[0].ProjectPath, "…et son chemin");
            t.Check(pending.Count > 0 && pending[0].HasRecovery, "…avec son secours");
            RecoveryStore.End(pending[0], true);
            t.Check(!File.Exists(dead.SessionPath) && File.Exists(dead.RecoveryPath), "ouvert : témoin consommé, secours gardé");
            t.Equal(0, RecoveryStore.Pending().Count, "plus rien en attente");
            File.Delete(dead.RecoveryPath);

            // Un témoin sans secours (rien n'avait été modifié) : nettoyé en silence.
            var idle = RecoveryStore.Begin(path, "Roman");
            Orphan(idle);
            t.Equal(0, RecoveryStore.Pending().Count, "témoin sans secours : pas proposé");
            t.Check(!File.Exists(idle.SessionPath), "…et nettoyé");

            // Un projet jamais enregistré a aussi son secours.
            var untitled = RecoveryStore.Begin(null, "Sans titre");
            t.Check(untitled.Id.StartsWith("sans-titre-"), "projet sans chemin : id dédié");
            t.Check(untitled.ProjectPath == null, "…sans chemin");
            RecoveryStore.End(untitled);

            // Un témoin illisible est jeté.
            var junk = Path.Combine(RecoveryStore.Folder(), "junk.session");
            File.WriteAllText(junk, "{ pas du json");
            t.Equal(0, RecoveryStore.Pending().Count, "témoin illisible : ignoré");
            t.Check(!File.Exists(junk), "…et supprimé");
        }

        /// <summary>Réécrit le témoin avec un pid mort : la session d'un
        /// Marabook qui n'est plus.</summary>
        private static void Orphan(RecoverySession session)
        {
            var text = File.ReadAllText(session.SessionPath);
            var node = Json.AsObject(Json.Parse(text));
            node["pid"] = 0;
            File.WriteAllText(session.SessionPath, Json.Write(node), new UTF8Encoding(false));
        }
    }
}

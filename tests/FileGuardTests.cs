using System;
using System.IO;
using Marabook.Model;
using Marabook.Persistence;

namespace Marabook.Tests
{
    /// <summary>C47 — la garde du .plot (1.0.3) pour les dossiers synchronisés :
    /// l'empreinte d'enregistrement fait l'aller-retour et se relit sans
    /// charger le projet ; une écriture gardée refuse d'écraser un fichier
    /// dont l'empreinte a changé (le .plot reste intact, pas de .tmp) ; le
    /// verrou dit qui tient le projet, se périme et se retire.</summary>
    public static class FileGuardTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C47 — garde du .plot : empreinte, conflit, verrou (1.0.3)");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-guard");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            try
            {
                SaveId(t, dir);
                GuardedWrite(t, dir);
                Lock(t, dir);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static Project BuildProject()
        {
            var project = Project.CreateNew();
            project.Name = "Garde";
            return project;
        }

        private static void SaveId(Harness t, string dir)
        {
            var path = Path.Combine(dir, "empreinte.plot");
            var project = BuildProject();
            t.Equal("", project.SaveId, "un projet neuf n'a pas d'empreinte");
            PlotFile.Save(project, path);
            t.Check(project.SaveId.Length == 32, "Prepare pose une empreinte sur le projet (" + project.SaveId + ")");
            t.Equal(project.SaveId, PlotFile.ReadSaveId(path), "ReadSaveId la relit sans charger le projet");
            var loaded = PlotFile.Load(path);
            t.Equal(project.SaveId, loaded.SaveId, "…et le chargement complet aussi");
            var first = project.SaveId;
            PlotFile.Save(project, path);
            t.Check(project.SaveId != first && PlotFile.ReadSaveId(path) == project.SaveId, "chaque enregistrement a une empreinte neuve");
            t.Equal(null, PlotFile.ReadSaveId(Path.Combine(dir, "absent.plot")), "fichier absent : null");
            File.WriteAllText(Path.Combine(dir, "bidon.plot"), "pas un zip");
            t.Equal(null, PlotFile.ReadSaveId(Path.Combine(dir, "bidon.plot")), "fichier illisible : null");
            t.Check(PlotFile.Stamp(path) != null && PlotFile.Stamp(Path.Combine(dir, "absent.plot")) == null, "Stamp : date + taille, null si absent");
        }

        private static void GuardedWrite(Harness t, string dir)
        {
            var path = Path.Combine(dir, "conflit.plot");
            var project = BuildProject();
            PlotFile.Save(project, path);
            var known = project.SaveId;

            // Même empreinte : l'écriture gardée passe.
            var plan = PlotFile.Prepare(project);
            PlotFile.Write(plan, path, false, known);
            t.Equal(plan.SaveId, PlotFile.ReadSaveId(path), "écriture gardée avec la bonne empreinte : écrite");
            known = plan.SaveId;

            // Une autre machine a réécrit le fichier entre-temps.
            var other = BuildProject();
            other.Name = "L'autre machine";
            PlotFile.Save(other, path);
            var onDisk = other.SaveId;
            var size = new FileInfo(path).Length;
            var mine = PlotFile.Prepare(project);
            t.Throws<PlotFile.PlotConflictException>(delegate { PlotFile.Write(mine, path, false, known); },
                "écriture gardée sur un fichier modifié ailleurs : refusée");
            t.Equal(onDisk, PlotFile.ReadSaveId(path), "…le fichier de l'autre est intact");
            t.Equal(size, new FileInfo(path).Length, "…octet pour octet");
            t.Check(!File.Exists(path + ".tmp"), "…et aucun .tmp ne traîne");
            t.Equal("L'autre machine", PlotFile.Load(path).Name, "…il se relit");

            // Sans garde (Écraser) : la nôtre passe, l'autre devient .bak.
            PlotFile.Write(mine, path, false, null);
            t.Equal(mine.SaveId, PlotFile.ReadSaveId(path), "écraser : la nôtre est écrite");
            t.Equal(onDisk, PlotFile.ReadSaveId(path + ".bak"), "…l'autre est gardée en .bak");

            // Un fichier d'avant l'empreinte (manifest sans saveId) vaut « ».
            var legacy = Path.Combine(dir, "ancien.plot");
            PlotFile.Save(project, legacy);
            using (var zip = System.IO.Compression.ZipFile.Open(legacy, System.IO.Compression.ZipArchiveMode.Update))
            {
                var entry = zip.GetEntry("manifest.json");
                string manifest;
                using (var reader = new StreamReader(entry.Open())) manifest = reader.ReadToEnd();
                entry.Delete();
                var stripped = zip.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(stripped.Open()))
                    writer.Write(manifest.Replace(",\"saveId\":\"" + project.SaveId + "\"", "").Replace("\"saveId\":\"" + project.SaveId + "\",", ""));
            }
            t.Equal("", PlotFile.ReadSaveId(legacy), "fichier d'avant l'empreinte : « »");
            PlotFile.Write(PlotFile.Prepare(project), legacy, false, "");
            t.Check(PlotFile.ReadSaveId(legacy).Length == 32, "…une écriture gardée sur « » passe et pose l'empreinte");
        }

        private static void Lock(Harness t, string dir)
        {
            var path = Path.Combine(dir, "verrou.plot");
            var savedProbe = PlotLock.AliveProbe;
            var savedMachine = PlotLock.MachineNameProvider;
            try
            {
                PlotLock.MachineNameProvider = delegate { return "PC-BUREAU"; };
                PlotLock.AliveProbe = delegate(int pid) { return pid == 4242; };

                t.Equal(null, PlotLock.Read(path), "pas de verrou au départ");
                t.Equal(null, PlotLock.Probe(path), "…donc libre");
                PlotLock.Acquire(path);
                var ours = PlotLock.Read(path);
                t.Check(ours != null && PlotLock.IsOurs(ours) && File.Exists(PlotLock.PathFor(path)), "Acquire pose « verrou.plot.lock » à notre nom");
                t.Equal(null, PlotLock.Probe(path), "notre propre verrou ne gêne pas");
                t.Check(ours.Describe().StartsWith("PC-BUREAU"), "Describe nomme la machine (" + ours.Describe() + ")");
                PlotLock.Heartbeat(path);
                t.Check(PlotLock.Read(path).Started == ours.Started, "le battement garde la date de début");
                PlotLock.Release(path);
                t.Check(!File.Exists(PlotLock.PathFor(path)), "Release retire notre verrou");

                // Un autre processus de la même machine : vivant → occupé, mort → libre.
                var now = DateTime.UtcNow;
                var alive = new LockInfo { Machine = "PC-BUREAU", Pid = 4242, HeartbeatUtc = now };
                var dead = new LockInfo { Machine = "PC-BUREAU", Pid = 99, HeartbeatUtc = now };
                t.Check(PlotLock.IsForeignAndAlive(alive, now), "même machine, autre Marabook vivant : occupé");
                t.Check(!PlotLock.IsForeignAndAlive(dead, now), "même machine, processus mort : libre (plantage)");
                var reused = new LockInfo { Machine = "PC-BUREAU", Pid = 4242, HeartbeatUtc = now.AddMinutes(-40) };
                t.Check(!PlotLock.IsForeignAndAlive(reused, now), "même machine, pid vivant mais sans battement depuis 40 min : périmé (pid réattribué)");

                // Une autre machine : battement frais → occupé ; périmé → libre.
                var fresh = new LockInfo { Machine = "PC-SALON", Pid = 1, HeartbeatUtc = now.AddMinutes(-3) };
                var stale = new LockInfo { Machine = "PC-SALON", Pid = 1, HeartbeatUtc = now.AddMinutes(-40) };
                t.Check(PlotLock.IsForeignAndAlive(fresh, now), "autre machine, battement de 3 min : occupé");
                t.Check(!PlotLock.IsForeignAndAlive(stale, now), "autre machine, battement de 40 min : périmé, libre");

                // Le verrou d'une autre machine sur le disque : on ne le retire pas, on ne le bat pas.
                File.WriteAllText(PlotLock.PathFor(path),
                    "{\"machine\":\"PC-SALON\",\"user\":\"remi\",\"pid\":7,\"started\":\"2026-10-03 09:00\",\"heartbeat\":\""
                    + now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) + "\"}");
                var foreign = PlotLock.Probe(path);
                t.Check(foreign != null && foreign.Machine == "PC-SALON" && foreign.User == "remi", "le verrou de PC-SALON est vu (" + (foreign == null ? "-" : foreign.Describe()) + ")");
                PlotLock.Heartbeat(path);
                t.Equal(7, PlotLock.Read(path).Pid, "notre battement n'écrase pas le verrou d'un autre");
                PlotLock.Release(path);
                t.Check(File.Exists(PlotLock.PathFor(path)), "Release ne retire pas le verrou d'un autre");
                PlotLock.Acquire(path);
                t.Check(PlotLock.IsOurs(PlotLock.Read(path)), "Acquire (« ouvrir quand même ») le reprend");
                PlotLock.Release(path);

                File.WriteAllText(PlotLock.PathFor(path), "{ pas du json");
                t.Equal(null, PlotLock.Read(path), "un verrou illisible vaut absent");
                t.Equal(null, PlotLock.Probe(path), "…et libre");
                PlotLock.Release(path);
                t.Check(File.Exists(PlotLock.PathFor(path)), "…mais Release ne le supprime pas (il peut être en cours d'écriture ailleurs)");
                File.Delete(PlotLock.PathFor(path));
                PlotLock.Acquire(path);
                File.Delete(PlotLock.PathFor(path));
                PlotLock.Heartbeat(path);
                t.Check(PlotLock.IsOurs(PlotLock.Read(path)), "le battement repose un verrou disparu");
                PlotLock.Release(path);
            }
            finally
            {
                PlotLock.AliveProbe = savedProbe;
                PlotLock.MachineNameProvider = savedMachine;
            }
        }
    }
}

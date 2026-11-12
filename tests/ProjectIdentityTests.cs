using System.Collections.Generic;
using Marabook.Model;

namespace Marabook.Tests
{
    /// <summary>C62 — la signature d'un projet récent (1.1.1) : date de
    /// création, nom, instantanés horodatés ; le verdict quand l'auteur
    /// relie un projet introuvable à un autre fichier ; l'aller-retour JSON.</summary>
    public static class ProjectIdentityTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C62 — signature des projets récents");
            Signature(t);
            Verdicts(t);
            Json(t);
        }

        private static Project Sample(string name, string createdAt, params string[] stamps)
        {
            var project = Project.CreateNew();
            project.Name = name;
            project.CreatedAt = createdAt;
            foreach (var stamp in stamps)
            {
                var parts = stamp.Split('@');
                project.Snapshots.Add(new Snapshot { Id = parts[0], Date = parts[1], ItemId = "x" });
            }
            return project;
        }

        private static void Signature(Harness t)
        {
            var project = Sample("Roman", "2026-10-01 10:00", "a@2026-10-02 10:00:00", "b@2026-10-05 10:00:00", "c@2026-10-03 10:00:00");
            var signature = ProjectSignature.Of(project);
            t.Equal("2026-10-01 10:00", signature.CreatedAt, "la date de création");
            t.Equal("Roman", signature.Name, "le nom");
            t.Equal("b@2026-10-05 10:00:00, c@2026-10-03 10:00:00, a@2026-10-02 10:00:00", string.Join(", ", signature.Snapshots.ToArray()), "les instantanés horodatés, les plus récents d'abord");
            t.Check(!signature.IsEmpty, "…pas vide");
            var many = Sample("Gros", "2026-01-01 10:00");
            for (var i = 0; i < 100; i++) many.Snapshots.Add(new Snapshot { Id = "s" + i, Date = "2026-01-" + (1 + i % 28).ToString("00") + " 10:00:00" });
            t.Equal(ProjectSignature.MaxSnapshots, ProjectSignature.Of(many).Snapshots.Count, "au plus " + ProjectSignature.MaxSnapshots + " instantanés retenus");
            t.Check(ProjectSignature.Of(null).IsEmpty && new ProjectSignature().IsEmpty, "sans projet : vide");
        }

        private static void Verdicts(Harness t)
        {
            var original = Sample("Roman", "2026-10-01 10:00", "a@2026-10-02 10:00:00", "b@2026-10-05 10:00:00");
            var signature = ProjectSignature.Of(original);
            t.Check(signature.Verdict(original) == null, "le même projet : accepté");
            var moved = Sample("Roman renommé", "2026-10-01 10:00");
            t.Check(signature.Verdict(moved) == null, "même date de création, autre nom, sans instantané : accepté (déplacé et renommé)");
            var shared = Sample("Copie", "2026-11-01 09:00", "zz@2026-11-01 09:00:00", "b@2026-10-05 10:00:00");
            t.Check(signature.Verdict(shared) == null, "un instantané horodaté en commun : accepté (une copie qui a vécu)");
            var other = Sample("Autre", "2026-11-01 09:00", "zz@2026-11-01 09:00:00");
            var verdict = signature.Verdict(other);
            t.Check(verdict != null && verdict.Contains("Autre") && verdict.Contains("01/11/2026") && verdict.Contains("Roman") && verdict.Contains("01/10/2026"), "un autre projet : refusé, avec les deux dates (" + verdict + ")");
            var sameDayNoStamp = Sample("Autre", "", "zz@2026-11-01 09:00:00");
            var noDate = signature.Verdict(sameDayNoStamp);
            t.Check(noDate != null && noDate.Contains("aucun instantané"), "sans date ni instantané commun : refusé pour les instantanés (" + noDate + ")");
            t.Check(signature.Verdict(null) != null, "fichier illisible : refusé");
            // Une entrée d'avant la 1.1.1 : rien de retenu, le nom tranche.
            var legacy = new ProjectSignature { Name = "Roman" };
            t.Check(legacy.Verdict(original) == null, "entrée sans signature : le même nom suffit");
            t.Check(legacy.Verdict(Sample("ROMAN", "2000-01-01 00:00")) == null, "…sans casse");
            t.Check(legacy.Verdict(other) != null, "…un autre nom est refusé");
            t.Check(new ProjectSignature().Verdict(other) == null, "rien de retenu du tout : accepté (on ne peut pas contredire)");
        }

        private static void Json(Harness t)
        {
            var signature = ProjectSignature.Of(Sample("Roman", "2026-10-01 10:00", "a@2026-10-02 10:00:00"));
            var text = Marabook.Json.Write(signature.ToJson());
            var back = ProjectSignature.FromJson(Marabook.Json.Parse(text));
            t.Equal(signature.CreatedAt, back.CreatedAt, "la date revient");
            t.Equal(signature.Name, back.Name, "le nom revient");
            t.Equal("a@2026-10-02 10:00:00", string.Join(", ", back.Snapshots.ToArray()), "les instantanés reviennent");
            t.Check(ProjectSignature.FromJson(null).IsEmpty && ProjectSignature.FromJson("n'importe quoi").IsEmpty, "un JSON absent ou faux : signature vide, sans plantage");
        }
    }
}

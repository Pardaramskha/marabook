using System;
using System.Collections.Generic;

namespace Marabook.Model
{
    /// <summary>La signature d'un projet récent (1.1.1, Rémi) : de quoi
    /// reconnaître un .plot déplacé quand l'accueil propose de RELIER un
    /// projet introuvable — sa date de création, son nom, et ses instantanés
    /// horodatés (les identifiants sont uniques : un seul en commun suffit).
    /// Retenue dans les réglages à chaque ouverture ou enregistrement ; comparée
    /// au fichier désigné par l'auteur avant de remplacer le chemin.</summary>
    public class ProjectSignature
    {
        public const int MaxSnapshots = 40;

        public string CreatedAt = "";
        public string Name = "";
        public List<string> Snapshots = new List<string>(); // « id@date », les plus récents d'abord

        /// <summary>La signature d'un projet chargé.</summary>
        public static ProjectSignature Of(Project project)
        {
            var signature = new ProjectSignature();
            if (project == null) return signature;
            signature.CreatedAt = project.CreatedAt ?? "";
            signature.Name = project.Name ?? "";
            var snapshots = new List<Snapshot>(project.Snapshots);
            snapshots.Sort(delegate(Snapshot a, Snapshot b) { return string.CompareOrdinal(b.Date, a.Date); });
            foreach (var snapshot in snapshots)
            {
                if (signature.Snapshots.Count >= MaxSnapshots) break;
                signature.Snapshots.Add(Stamp(snapshot));
            }
            return signature;
        }

        public static string Stamp(Snapshot snapshot)
        {
            return (snapshot.Id ?? "") + "@" + (snapshot.Date ?? "");
        }

        public bool IsEmpty
        {
            get { return CreatedAt.Length == 0 && Snapshots.Count == 0; }
        }

        /// <summary>Le fichier candidat est-il le même projet ? Null quand oui ;
        /// sinon la raison, écrite pour l'auteur. Même date de création = oui ;
        /// un instantané horodaté en commun = oui ; sans rien de retenu (une
        /// entrée d'avant la 1.1.1), le même nom de projet suffit.</summary>
        public string Verdict(Project candidate)
        {
            if (candidate == null) return "Le fichier n'a pas pu être lu.";
            if (IsEmpty)
            {
                if (Name.Length == 0 || string.Equals(Name, candidate.Name ?? "", StringComparison.CurrentCultureIgnoreCase)) return null;
                return "Ce fichier est le projet « " + candidate.Name + "», pas « " + Name + " ».";
            }
            if (CreatedAt.Length > 0 && CreatedAt == (candidate.CreatedAt ?? "")) return null;
            var theirs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var snapshot in candidate.Snapshots) theirs.Add(Stamp(snapshot));
            foreach (var stamp in Snapshots) if (theirs.Contains(stamp)) return null;
            if (CreatedAt.Length > 0 && (candidate.CreatedAt ?? "").Length > 0 && CreatedAt != candidate.CreatedAt)
                return "Ce fichier est un autre projet : « " + candidate.Name + " », créé le " + Dates.Display(candidate.CreatedAt)
                    + ", alors que « " + Name + " » a été créé le " + Dates.Display(CreatedAt) + ".";
            return "Ce fichier ne partage aucun instantané avec « " + Name + " » : ce n'est pas le même projet.";
        }

        public Dictionary<string, object> ToJson()
        {
            var json = new Dictionary<string, object>();
            json["createdAt"] = CreatedAt ?? "";
            json["name"] = Name ?? "";
            json["snapshots"] = new List<object>(Snapshots.ToArray());
            return json;
        }

        public static ProjectSignature FromJson(object json)
        {
            var signature = new ProjectSignature();
            var obj = Json.AsObject(json);
            if (obj == null) return signature;
            signature.CreatedAt = Json.AsString(Json.Field(obj, "createdAt")) ?? "";
            signature.Name = Json.AsString(Json.Field(obj, "name")) ?? "";
            var stamps = Json.AsList(Json.Field(obj, "snapshots"));
            if (stamps != null)
                foreach (var stamp in stamps)
                    if (stamp is string && ((string)stamp).Length > 0) signature.Snapshots.Add((string)stamp);
            return signature;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace UniversSale.Model
{
    /// <summary>D'où vient un instantané : pris à la main, ou automatiquement
    /// avant une opération qui réécrit massivement (passe typographique,
    /// remplacement projet, restauration), ou à la première modification de
    /// la journée.</summary>
    public static class SnapshotOrigin
    {
        public const string Manual = "manual";
        public const string Typography = "auto:typography";
        public const string Replace = "auto:replace";
        public const string Restore = "auto:restore";
        public const string Daily = "auto:daily";

        public static bool IsAutomatic(string origin)
        {
            return origin != null && origin.StartsWith("auto", StringComparison.Ordinal);
        }

        public static string Label(string origin)
        {
            switch (origin ?? "")
            {
                case Manual: return "Manuel";
                case Typography: return "Avant passe typographique";
                case Replace: return "Avant remplacement";
                case Restore: return "Avant restauration";
                case Daily: return "Première modification du jour";
                default: return IsAutomatic(origin) ? "Automatique" : "Manuel";
            }
        }
    }

    /// <summary>Un instantané (batch 38) : le document d'un écrit ou d'une
    /// fiche capturé à un instant — identifiant, date, libellé optionnel,
    /// origine, compte de mots FIGÉ à la capture, empreinte du contenu (pour
    /// ne jamais doubler), et le document SÉRIALISÉ UNE FOIS (Json, immuable :
    /// écrit tel quel dans sa propre entrée du .plot à chaque sauvegarde,
    /// jamais resérialisé) — décodé paresseusement à la comparaison ou à la
    /// restauration. Le JSON est gardé en OCTETS UTF-8 (Data), pas en chaîne
    /// .NET : mesuré (A3), 2 000 instantanés d'un manuscrit de 308 000 mots
    /// pèsent 42 Mo en UTF-8 contre 84 Mo en UTF-16.</summary>
    public class Snapshot
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string ItemId = "";
        public string Date = "";      // "yyyy-MM-dd HH:mm:ss"
        public string Label = "";
        public string Origin = SnapshotOrigin.Manual;
        public int Words;
        public long Fingerprint;
        public byte[] Data = new byte[0]; // le document sérialisé, UTF-8, immuable

        private TextDocument _document;

        /// <summary>Le JSON du document (décodé des octets à la demande).</summary>
        public string Json
        {
            get { return Data == null || Data.Length == 0 ? "" : Encoding.UTF8.GetString(Data); }
            set { Data = string.IsNullOrEmpty(value) ? new byte[0] : Encoding.UTF8.GetBytes(value); _document = null; }
        }

        public bool IsAutomatic { get { return SnapshotOrigin.IsAutomatic(Origin); } }

        /// <summary>Le document, décodé au premier usage et gardé.</summary>
        public TextDocument Document
        {
            get
            {
                if (_document == null)
                    _document = Data != null && Data.Length > 0 ? Persistence.PlotFile.DeserializeDocument(Json) : new TextDocument();
                return _document;
            }
        }

        /// <summary>Le poids de l'instantané tel qu'écrit (octets UTF-8 du JSON).</summary>
        public int Bytes
        {
            get { return Data == null ? 0 : Data.Length; }
        }

        /// <summary>Le libellé affiché : celui de l'auteur, sinon l'origine.</summary>
        public string DisplayLabel
        {
            get { return Label.Length > 0 ? Label : SnapshotOrigin.Label(Origin); }
        }

        /// <summary>Capture le document d'un item — sérialisé maintenant, une
        /// fois pour toutes.</summary>
        public static Snapshot Capture(BinderItem item, string label, string origin)
        {
            var document = item.Document ?? new TextDocument();
            return new Snapshot
            {
                ItemId = item.Id,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Label = label ?? "",
                Origin = origin ?? SnapshotOrigin.Manual,
                Words = Correction.TextStats.Compute(document.ToPlainText()).Words,
                Fingerprint = SnapshotStore.DocumentFingerprint(document),
                Data = Encoding.UTF8.GetBytes(Persistence.PlotFile.SerializeDocument(document))
            };
        }
    }

    /// <summary>Les instantanés d'un projet (batch 38) : capture dédoublonnée
    /// par empreinte, plafond par item avec éviction (les automatiques
    /// d'abord, du plus ancien au plus récent ; un manuel n'est jamais
    /// évincé tant qu'il reste un automatique), purges EXPLICITES seulement
    /// — jamais à la sauvegarde (A1 : vider la corbeille puis enregistrer
    /// puis Ctrl+Z doit rendre le chapitre AVEC ses versions). Un instantané
    /// suit son item à la corbeille et n'est abandonné qu'à l'ouverture d'un
    /// projet où l'item n'existe plus (l'historique de session ne peut plus
    /// le ressusciter).</summary>
    public static class SnapshotStore
    {
        public const int DefaultCap = 20;
        public const int MinCap = 5;
        public const int MaxCap = 100;

        /// <summary>Empreinte du CONTENU d'un document : textes plats et styles
        /// des paragraphes, notes, annotations (texte et résolution).</summary>
        public static long DocumentFingerprint(TextDocument document)
        {
            var hash = 14695981039346656037UL;
            if (document == null) return unchecked((long)hash);
            foreach (var paragraph in document.Paragraphs)
            {
                hash = Mix(hash, PivotEdit.FlatText(paragraph));
                hash = Mix(hash, paragraph.StyleId);
                hash = Mix(hash, paragraph.AlignOverride);
                hash = Mix(hash, paragraph.ListKind);
                foreach (var run in paragraph.Runs)
                {
                    if (run.Bold == true) hash = Mix(hash, "b");
                    if (run.Italic == true) hash = Mix(hash, "i");
                    if (run.Underline == true) hash = Mix(hash, "u");
                    if (run.Strike == true) hash = Mix(hash, "s");
                    hash = Mix(hash, run.Color);
                    hash = Mix(hash, run.Highlight);
                    hash = Mix(hash, run.FontFamily);
                    hash = Mix(hash, run.Weight);
                }
                hash = Mix(hash, "\n");
            }
            foreach (var note in document.Footnotes) { hash = Mix(hash, note.Id); hash = Mix(hash, note.Text); }
            foreach (var annotation in document.Annotations)
            {
                hash = Mix(hash, annotation.Id);
                hash = Mix(hash, annotation.Text);
                hash = Mix(hash, annotation.Resolved ? "r" : "o");
            }
            return unchecked((long)hash);
        }

        private static ulong Mix(ulong hash, string text)
        {
            if (text != null)
                for (var i = 0; i < text.Length; i++) hash = unchecked((hash ^ text[i]) * 1099511628211UL);
            return unchecked((hash ^ 0xFFFFUL) * 1099511628211UL);
        }

        /// <summary>Les instantanés d'un item, du plus récent au plus ancien.</summary>
        public static List<Snapshot> Of(Project project, string itemId)
        {
            var list = new List<Snapshot>();
            if (project == null || itemId == null) return list;
            foreach (var snapshot in project.Snapshots)
                if (snapshot.ItemId == itemId) list.Add(snapshot);
            list.Reverse(); // insérés dans l'ordre chronologique
            return list;
        }

        /// <summary>Le dernier instantané d'un item (le plus récent), ou null.</summary>
        public static Snapshot Latest(Project project, string itemId)
        {
            Snapshot latest = null;
            if (project == null || itemId == null) return null;
            foreach (var snapshot in project.Snapshots)
                if (snapshot.ItemId == itemId) latest = snapshot;
            return latest;
        }

        /// <summary>Capture le document de l'item — rien si son contenu est
        /// celui du dernier instantané (jamais de doublon) — puis applique le
        /// plafond. Rend l'instantané créé, ou null.</summary>
        public static Snapshot Capture(Project project, BinderItem item, string label, string origin, int cap)
        {
            return CaptureDocument(project, item, item == null ? null : item.Document, label, origin, cap);
        }

        /// <summary>Capture un document DONNÉ au nom de l'item (la capture
        /// quotidienne fige l'état d'avant la première frappe, pas celui
        /// d'après) — mêmes règles : dédoublonnée, plafonnée.</summary>
        public static Snapshot CaptureDocument(Project project, BinderItem item, TextDocument document, string label, string origin, int cap)
        {
            if (project == null || item == null || document == null) return null;
            if (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet) return null;
            var latest = Latest(project, item.Id);
            var fingerprint = DocumentFingerprint(document);
            if (latest != null && latest.Fingerprint == fingerprint) return null;
            var snapshot = new Snapshot
            {
                ItemId = item.Id,
                Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Label = label ?? "",
                Origin = origin ?? SnapshotOrigin.Manual,
                Words = Correction.TextStats.Compute(document.ToPlainText()).Words,
                Fingerprint = fingerprint,
                Data = Encoding.UTF8.GetBytes(Persistence.PlotFile.SerializeDocument(document))
            };
            project.Snapshots.Add(snapshot);
            Trim(project, item.Id, cap);
            return snapshot;
        }

        /// <summary>Vrai si un instantané existe déjà aujourd'hui pour l'item
        /// — de cette origine, ou de n'importe laquelle si origin est null
        /// (la capture quotidienne s'efface devant toute capture du jour).</summary>
        public static bool HasToday(Project project, string itemId, string origin)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            foreach (var snapshot in project.Snapshots)
                if (snapshot.ItemId == itemId && (origin == null || snapshot.Origin == origin)
                    && snapshot.Date.StartsWith(today, StringComparison.Ordinal))
                    return true;
            return false;
        }

        // ------------------------------------------------ les automatiques (lot C)

        /// <summary>La ceinture avant la passe typographique : un instantané
        /// automatique de l'écrit (rien s'il est déjà capturé tel quel).</summary>
        public static Snapshot GuardBeforeTypography(Project project, BinderItem item, int cap)
        {
            return Capture(project, item, "", SnapshotOrigin.Typography, cap);
        }

        /// <summary>La ceinture avant un remplacement projet : un instantané
        /// automatique de CHAQUE item touché (écrits et fiches), libellé
        /// « Avant remplacement de « X » ». Rend le nombre pris.</summary>
        public static int GuardBeforeReplace(Project project, IEnumerable<BinderItem> items, string pattern, int cap)
        {
            var taken = 0;
            if (project == null || items == null) return 0;
            var label = "Avant remplacement de « " + (pattern ?? "") + " »";
            foreach (var item in items)
                if (Capture(project, item, label, SnapshotOrigin.Replace, cap) != null) taken++;
            return taken;
        }

        /// <summary>La ceinture avant une restauration : l'état courant, pour
        /// que restaurer ne soit jamais destructeur — même après fermeture.</summary>
        public static Snapshot GuardBeforeRestore(Project project, BinderItem item, string restoredLabel, int cap)
        {
            return Capture(project, item, "Avant restauration de « " + (restoredLabel ?? "") + " »", SnapshotOrigin.Restore, cap);
        }

        /// <summary>La capture quotidienne : à la première modification du
        /// jour, l'état d'AVANT la frappe (documentAtOpen si la vue le
        /// connaît, sinon le document courant) — une fois par jour et par
        /// item, jamais si une capture du jour existe déjà, débrayable.</summary>
        public static Snapshot GuardDaily(Project project, BinderItem item, TextDocument documentAtOpen, bool enabled, int cap)
        {
            if (!enabled || project == null || item == null) return null;
            if (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet) return null;
            if (HasToday(project, item.Id, null)) return null;
            return CaptureDocument(project, item, documentAtOpen ?? item.Document, "", SnapshotOrigin.Daily, cap);
        }

        /// <summary>Ramène l'item sous le plafond : évince les automatiques du
        /// plus ancien au plus récent, puis seulement les manuels les plus
        /// anciens — jamais le plus récent (celui qu'on vient de prendre).
        /// Rend le nombre d'évincés.</summary>
        public static int Trim(Project project, string itemId, int cap)
        {
            cap = Math.Max(1, cap);
            var removed = 0;
            while (true)
            {
                var mine = new List<Snapshot>();
                foreach (var snapshot in project.Snapshots) if (snapshot.ItemId == itemId) mine.Add(snapshot);
                if (mine.Count <= cap) return removed;
                Snapshot victim = null;
                for (var i = 0; i < mine.Count - 1 && victim == null; i++)
                    if (mine[i].IsAutomatic) victim = mine[i];
                if (victim == null) victim = mine[0];
                project.Snapshots.Remove(victim);
                removed++;
            }
        }

        /// <summary>Purge explicite : les automatiques d'un item (ou de tout le
        /// projet si itemId est null). Rend le nombre retiré.</summary>
        public static int PurgeAutomatic(Project project, string itemId)
        {
            return project.Snapshots.RemoveAll(delegate(Snapshot s) { return s.IsAutomatic && (itemId == null || s.ItemId == itemId); });
        }

        /// <summary>Purge explicite : tous les instantanés d'un item.</summary>
        public static int PurgeItem(Project project, string itemId)
        {
            return project.Snapshots.RemoveAll(delegate(Snapshot s) { return s.ItemId == itemId; });
        }

        /// <summary>Purge explicite : tout ce qui a plus de days jours.</summary>
        public static int PurgeOlderThan(Project project, int days)
        {
            var limit = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd HH:mm:ss");
            return project.Snapshots.RemoveAll(delegate(Snapshot s) { return string.CompareOrdinal(s.Date, limit) < 0; });
        }

        /// <summary>Le poids en octets des instantanés d'un item (ou de tous).</summary>
        public static long Bytes(Project project, string itemId)
        {
            long total = 0;
            foreach (var snapshot in project.Snapshots)
                if (itemId == null || snapshot.ItemId == itemId) total += snapshot.Bytes;
            return total;
        }

        public static int Count(Project project, string itemId)
        {
            var count = 0;
            foreach (var snapshot in project.Snapshots)
                if (itemId == null || snapshot.ItemId == itemId) count++;
            return count;
        }

        /// <summary>« 312 versions · 3,1 Mo ».</summary>
        public static string Weight(Project project, string itemId)
        {
            var count = Count(project, itemId);
            var bytes = Bytes(project, itemId);
            var size = bytes < 1024 ? bytes + " o"
                : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("F0") + " Ko"
                : (bytes / 1024.0 / 1024.0).ToString("F1") + " Mo";
            return count + (count == 1 ? " version" : " versions") + " · " + size;
        }
    }
}

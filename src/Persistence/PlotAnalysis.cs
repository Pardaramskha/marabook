using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Marabook.Model;

namespace Marabook.Persistence
{
    /// <summary>L'ANALYSEUR DE .PLOT (16/11/2026, Rémi : « combien d'octets sont
    /// occupés par chaque grande section du fichier ») : le poids, DANS LE
    /// FICHIER (octets compressés), de chaque partie du projet.
    ///
    /// Un .plot est un zip : une entrée par texte, par image, par média, par
    /// tuile de carte, par instantané — celles-là se pèsent telles quelles. Le
    /// manifeste, lui, porte en une seule entrée la Pile, les champs des fiches,
    /// les frises, l'Astrolabe, les plans : son poids est RÉPARTI entre les
    /// sections au prorata du texte que chacune y écrit (une estimation, dite
    /// comme telle).</summary>
    public static class PlotAnalysis
    {
        public const string Writings = "Écrits", Research = "Recherche", Sheets = "Fiches", Timeline = "Chronologie et Astrolabe", Plans = "Plans",
            MindMaps = "Cartes mentales", Maps = "Cartes", Snapshots = "Versions des écrits", Trash = "Corbeille", Settings = "Styles, modèles et réglages",
            Envelope = "Enveloppe du fichier";

        public sealed class Detail
        {
            public string Name;
            public long Bytes;
        }

        public sealed class Section
        {
            public string Name;
            public string Hint;       // ce que la section contient, en une ligne
            public long Bytes;
            public int Count;         // le nombre d'éléments (écrits, fiches, cartes…), 0 = sans objet
            public List<Detail> Details = new List<Detail>();

            public Detail Add(string name, long bytes)
            {
                Bytes += bytes;
                if (bytes == 0) return null;
                foreach (var detail in Details) if (detail.Name == name) { detail.Bytes += bytes; return detail; }
                var created = new Detail { Name = name, Bytes = bytes };
                Details.Add(created);
                return created;
            }
        }

        public sealed class Report
        {
            public long FileBytes;
            public int Entries;
            public List<Section> Sections = new List<Section>();   // de la plus lourde à la plus légère, l'enveloppe en dernier

            public Section Find(string name) { foreach (var section in Sections) if (section.Name == name) return section; return null; }
        }

        private static readonly string[] Order = { Writings, Research, Sheets, Timeline, Plans, MindMaps, Maps, Snapshots, Trash, Settings, Envelope };
        private static readonly string[] Hints =
        {
            "Le texte des écrits, leurs images, leur place dans la Pile",
            "Les médias importés et les notes de recherche",
            "Les textes des fiches, leurs champs, leurs illustrations",
            "Les frises et leurs événements, les systèmes temporels, les mondes",
            "Les colonnes et les briques des plans",
            "Les cartes de Mental-O",
            "Les tuiles des cartes (l'image découpée), leurs zones, leurs repères",
            "Les instantanés gardés par « Versions »",
            "Ce qui attend dans la Corbeille (le vider l'en retire)",
            "Les styles, les modèles de fiche, le lexique, le journal, les réglages du projet",
            "Les en-têtes du zip : ce que coûte le contenant"
        };

        /// <summary>Pèse le .plot enregistré à ce chemin, lu avec le projet ouvert (qui dit à
        /// quelle section appartient chaque entrée). Lève IOException si le fichier ne se lit pas.</summary>
        public static Report Analyze(string path, Project project)
        {
            var report = new Report();
            for (var i = 0; i < Order.Length; i++) report.Sections.Add(new Section { Name = Order[i], Hint = Hints[i] });
            report.FileBytes = new FileInfo(path).Length;

            // À qui est chaque élément, chaque image.
            var sectionOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var titleOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var imageOwner = new Dictionary<string, string>(StringComparer.Ordinal);
            if (project != null)
            {
                foreach (var item in project.AllItems())
                {
                    var section = SectionOf(item);
                    sectionOf[item.Id] = section;
                    titleOf[item.Id] = item.Title ?? "";
                    if (!item.IsCategory && item.Kind != ItemKind.Folder) report.Find(section).Count++;
                    if (item.Kind != ItemKind.Text && item.Kind != ItemKind.Sheet && item.ImageId == null) continue;
                    var owner = item.Kind == ItemKind.Sheet ? Sheets : section == Trash ? Trash : section == Research ? Research : Writings;
                    if (item.ImageId != null) Own(imageOwner, item.ImageId, owner);
                    if (item.Document == null) continue;
                    foreach (var paragraph in item.Document.Paragraphs)
                        foreach (var run in paragraph.Runs)
                            if (run.ImageId != null) Own(imageOwner, run.ImageId, owner);
                    if (item.Kind == ItemKind.Sheet)
                        foreach (var id in MarkdownDialect.StoredImageIds(item.Document.ToPlainText())) Own(imageOwner, id, owner);
                }
                if (project.Encyclopedia != null && project.Encyclopedia.CoverImageId != null) Own(imageOwner, project.Encyclopedia.CoverImageId, Sheets);
            }

            long packed = 0;
            string manifestText = null;
            long manifestPacked = 0;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    report.Entries++;
                    var size = entry.CompressedLength;
                    packed += size;
                    var name = entry.FullName;
                    if (name == "manifest.json")
                    {
                        manifestPacked = size;
                        using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) manifestText = reader.ReadToEnd();
                    }
                    else if (name.StartsWith("texts/", StringComparison.Ordinal))
                    {
                        var id = Stem(name.Substring("texts/".Length));
                        string section;
                        if (!sectionOf.TryGetValue(id, out section)) section = Writings;
                        report.Find(section).Add(section == Sheets ? "Textes des fiches" : section == Trash ? "Textes jetés" : "Textes", size);
                    }
                    else if (name.StartsWith("images/", StringComparison.Ordinal))
                    {
                        string owner;
                        if (!imageOwner.TryGetValue(Stem(name.Substring("images/".Length)), out owner)) owner = Writings;
                        report.Find(owner).Add(owner == Sheets ? "Illustrations des fiches" : "Images", size);
                    }
                    else if (name.StartsWith("research/", StringComparison.Ordinal))
                    {
                        string section;
                        if (!sectionOf.TryGetValue(Stem(name.Substring("research/".Length)), out section)) section = Research;
                        report.Find(section == Trash ? Trash : Research).Add(section == Trash ? "Médias jetés" : "Médias importés", size);
                    }
                    else if (name.StartsWith(MapJson.Folder, StringComparison.Ordinal))
                    {
                        // cartes/<id>/<z>/<x>_<y>.jpg : une ligne par carte.
                        var rest = name.Substring(MapJson.Folder.Length);
                        var slash = rest.IndexOf('/');
                        var id = slash < 0 ? rest : rest.Substring(0, slash);
                        string title, section;
                        if (!titleOf.TryGetValue(id, out title)) title = "Carte retirée du projet";
                        if (!sectionOf.TryGetValue(id, out section)) section = Maps;
                        report.Find(section == Trash ? Trash : Maps).Add(section == Trash ? "Carte jetée « " + title + " »" : "« " + title + " »", size);
                    }
                    else if (name.StartsWith("maps/", StringComparison.Ordinal)) report.Find(MindMaps).Add("Cartes mentales", size);
                    else if (name.StartsWith("snapshots/", StringComparison.Ordinal)) report.Find(Snapshots).Add("Instantanés", size);
                    else if (name == "styles.json") report.Find(Settings).Add("Styles", size);
                    else if (name == "sheets/templates.json") report.Find(Settings).Add("Modèles de fiche", size);
                    else report.Find(Settings).Add("Autres entrées", size);
                }
            }

            if (manifestText != null) SplitManifest(report, manifestText, manifestPacked, sectionOf);
            report.Find(Envelope).Add("En-têtes et répertoire du zip", Math.Max(0, report.FileBytes - packed));

            foreach (var section in report.Sections)
                section.Details.Sort(delegate(Detail a, Detail b) { return b.Bytes.CompareTo(a.Bytes); });
            var envelope = report.Find(Envelope);
            report.Sections.Remove(envelope);
            report.Sections.Sort(delegate(Section a, Section b) { var by = b.Bytes.CompareTo(a.Bytes); return by != 0 ? by : Array.IndexOf(Order, a.Name).CompareTo(Array.IndexOf(Order, b.Name)); });
            report.Sections.Add(envelope);
            return report;
        }

        private static void Own(Dictionary<string, string> owners, string imageId, string owner)
        {
            string current;
            // Une image citée par une fiche ET un écrit compte pour les fiches.
            if (!owners.TryGetValue(imageId, out current) || owner == Sheets) owners[imageId] = owner;
        }

        private static string Stem(string file)
        {
            var dot = file.IndexOf('.');
            return dot < 0 ? file : file.Substring(0, dot);
        }

        /// <summary>La section d'un élément : la Corbeille d'abord, puis sa nature.</summary>
        public static string SectionOf(BinderItem item)
        {
            var root = item.RootCategory();
            var key = root == null ? null : root.CategoryKey;
            if (key == Project.KeyTrash) return Trash;
            switch (item.Kind)
            {
                case ItemKind.Sheet: return Sheets;
                case ItemKind.Timeline: return Timeline;
                case ItemKind.Plan: return Plans;
                case ItemKind.MindMap: return MindMaps;
                case ItemKind.Map: return Maps;
                case ItemKind.Media: return Research;
            }
            if (key == Project.KeySheets) return Sheets;
            if (key == Project.KeyResearch) return Research;
            if (key == Project.KeyTimelines || key == Project.KeyAstrolabe) return Timeline;
            if (key == Project.KeyPlans) return Plans;
            if (key == Project.KeyMindMaps) return MindMaps;
            if (key == Project.KeyMaps) return Maps;
            if (key == Project.KeyDictionary) return Settings;
            return Writings;
        }

        /// <summary>Le manifeste réparti : chaque nœud de la Pile compte pour sa section (son
        /// JSON propre, enfants déduits), les systèmes temporels et les mondes pour l'Astrolabe,
        /// le reste pour les réglages — au prorata du poids compressé de l'entrée.</summary>
        private static void SplitManifest(Report report, string text, long packed, Dictionary<string, string> sectionOf)
        {
            var shares = new Dictionary<string, long>(StringComparer.Ordinal);
            Dictionary<string, object> manifest;
            try { manifest = Json.AsObject(Json.Parse(text)); }
            catch (Exception) { manifest = null; }
            if (manifest == null) { report.Find(Settings).Add("Manifeste", packed); return; }
            foreach (var pair in manifest)
            {
                if (pair.Key == "binder")
                {
                    var roots = Json.AsList(pair.Value);
                    if (roots != null) foreach (var root in roots) Weigh(root, sectionOf, shares);
                    continue;
                }
                var length = (long)Json.Write(pair.Value).Length + pair.Key.Length + 4;
                var section = pair.Key == "timeSystems" || pair.Key == "worlds" || pair.Key == "epoch" ? Timeline
                            : pair.Key == "relationSchema" || pair.Key == "relationKinds" || pair.Key == "graphLayout" || pair.Key == "encyclopedia" ? Sheets
                            : Settings;
                Share(shares, section, length);
            }
            long total = 0;
            foreach (var value in shares.Values) total += value;
            if (total <= 0) { report.Find(Settings).Add("Manifeste", packed); return; }
            long given = 0;
            Section heaviest = null;
            foreach (var pair in shares)
            {
                var bytes = (long)Math.Floor((double)packed * pair.Value / total);
                given += bytes;
                var section = report.Find(pair.Key);
                section.Add(ManifestLine(pair.Key), bytes);
                if (heaviest == null || bytes > 0 && pair.Value > shares[heaviest.Name]) heaviest = section;
            }
            if (heaviest != null && packed > given) heaviest.Add(ManifestLine(heaviest.Name), packed - given); // les arrondis
        }

        private static string ManifestLine(string section)
        {
            return section == Sheets ? "Champs, relations et tags des fiches (estimé)"
                 : section == Timeline ? "Frises, systèmes temporels, mondes (estimé)"
                 : section == Maps ? "Zones, repères et échelles (estimé)"
                 : section == Plans ? "Colonnes et briques (estimé)"
                 : section == Settings ? "Réglages du projet, lexique, journal (estimé)"
                 : "Place dans la Pile, synopsis, notes (estimé)";
        }

        private static void Share(Dictionary<string, long> shares, string section, long length)
        {
            long current;
            shares.TryGetValue(section, out current);
            shares[section] = current + length;
        }

        private static long Weigh(object node, Dictionary<string, string> sectionOf, Dictionary<string, long> shares)
        {
            var o = Json.AsObject(node);
            if (o == null) return 0;
            var full = (long)Json.Write(node).Length;
            long below = 0;
            var children = Json.AsList(Json.Field(o, "children"));
            if (children != null) foreach (var child in children) below += Weigh(child, sectionOf, shares);
            string section;
            var id = Json.AsString(Json.Field(o, "id"));
            if (id == null || !sectionOf.TryGetValue(id, out section)) section = Settings;
            Share(shares, section, Math.Max(0, full - below));
            return full;
        }
    }
}

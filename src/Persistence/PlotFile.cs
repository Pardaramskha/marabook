using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UniversSale.Model;

namespace UniversSale.Persistence
{
    /// <summary>Reads/writes .plot files: a ZIP archive holding manifest.json
    /// (version, metadata, Binder tree), styles.json (the project style sheet)
    /// and one texts/&lt;id&gt;.json pivot document per text item.
    /// v1 stored plain .txt texts and no styles — still loads forever.
    /// Doctrine inherited from Mental-o's .tea: integer version field, perpetual
    /// backward compatibility, atomic writes with a rolling .bak.</summary>
    public static class PlotFile
    {
        public const string Extension = ".plot";
        public const string OpenFilter = "Projets Univers Sale (*.plot)|*.plot|Tous les fichiers (*.*)|*.*";
        public const string SaveFilter = "Projet Univers Sale (*.plot)|*.plot|Tous les fichiers (*.*)|*.*";
        // DOCTRINE DU FORMAT — tout changement de format passe par ici :
        //   1. incrémenter FormatVersion ;
        //   2. documenter ci-dessous ce que la version AJOUTE ;
        //   3. donner un défaut à chaque nouveau champ (rétrocompatibilité
        //      descendante perpétuelle : un vieux .plot se charge toujours) ;
        //   4. ne JAMAIS réutiliser une clé existante pour un autre sens.
        // À l'ouverture, un manifeste de version SUPÉRIEURE passe le projet en
        // lecture seule (Project.ReadOnlyNewerFormat) : le réécrire avec cette
        // version détruirait silencieusement les champs inconnus.
        // v2: pivot + styles; v3: sheets, templates, media;
        // v4: notes, per-item icons, image store, lists, page breaks, page setup;
        // v5: books (metadata + gabarit), per-document page setup;
        // v6: en-têtes/pieds, gabarits de pages, veuves/orphelines débrayées;
        // v7: exceptions de césure du projet (hyphenExceptions);
        // v8: « ne pas corriger » sur les runs (np) + ignorés de correction
        //     du projet (proofIgnored);
        // v9: dictionnaire personnel du projet (learnedWords);
        // v10: règles de correction ignorées du projet (ignoredRules —
        //      « ignorer cette règle » d'un signalement Grammalecte).
        // v11: catégories de fiches (sheets/templates.json : "categories",
        //      chacune {id, name, template}) ; groupe d'affichage des champs
        //      de modèle ("group") ; catégorie d'une fiche ("sheetCategory").
        //      Un .plot d'avant est migré au chargement par
        //      Project.EnsureSheetCategories (défauts + adoption par nom).
        // v12: objectif de chapitres d'un livre (book.chapterGoal, 0 = aucun —
        //      la barre de progression de l'inspecteur).
        // v13: dictionnaire personnel à ENTRÉES (manifeste "lexicon" : {word,
        //      class, gender, plural, feminine, note}) — l'ancienne liste
        //      "learnedWords" est lue et migrée en entrées « autre » ; racine
        //      « Dictionnaire » de la Pile (category "dictionary", créée au
        //      chargement avant la Corbeille par EnsureCategory).
        // v14: fiches — groupe d'un champ libre ("group" d'une entrée "info",
        //      "" = Informations, "Physique" = Apparence) et RELATIONS
        //      ("relations" : {id, kind, target, name}).
        // v15: les PLANS (batch 35) — racine « Plans » (category "plans",
        //      créée au chargement après Fiches), items kind "plan" avec
        //      "plan" : {link, columnWord, columns:[{id, title, text,
        //      entries:[{id, kind, text, color, intensity}]}]}.
        // v16: généalogie (batch 36) — natures de relation personnalisées du
        //      projet (manifeste "relationKinds" : [string]) ; le modèle de
        //      base Personnage d'un .plot d'avant est migré au chargement
        //      (Project.UpgradeCharacterTemplate : « Âge » sous la date de
        //      naissance, apparence Taille/Poids/Peau/Yeux/Traits/
        //      Particularités) — une seule fois, gardé par la version lue.
        // v17: INSTANTANÉS (batch 38) — chaque instantané est SA PROPRE ENTRÉE
        //      snapshots/<itemId>/<id>.json : une ligne de métadonnées JSON
        //      {id, item, date, label, origin, words, fingerprint}, un saut de
        //      ligne, puis le document sérialisé (même forme que texts/) —
        //      jamais dans le manifeste (sinon chaque autosauvegarde
        //      réécrirait l'historique entier). Immuable : le JSON du document
        //      est écrit tel quel. Un .plot d'avant s'ouvre sans instantané ;
        //      un instantané dont l'item n'existe plus à l'ouverture est
        //      abandonné (jamais purgé à la sauvegarde — A1 du batch 38).
        private const int FormatVersion = 17;

        // Garde symétrique de Json.MaxDepth : l'arborescence de la Pile est
        // récursive à l'écriture (BuildNode) comme à la lecture.
        private const int MaxTreeDepth = 256;

        // ------------------------------------------------------- writing

        public static void Save(Project project, string path)
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (string.IsNullOrEmpty(project.CreatedAt)) project.CreatedAt = now;
            project.ModifiedAt = now;

            // Write to a temporary file first, then swap in atomically so a crash
            // mid-save can never corrupt the project. The previous version becomes .bak.
            var tempPath = path + ".tmp";
            project.PurgeUnusedImages();

            // Deux items de même id créeraient deux entrées texts/<id>.json :
            // le zip les accepte, GetEntry n'en relit qu'une — un document
            // serait perdu SANS ERREUR. C'est toujours un bug en amont :
            // refuser d'écrire plutôt que de persister la perte.
            var ids = new HashSet<string>();
            foreach (var item in project.AllItems())
                if (!ids.Add(item.Id))
                    throw new InvalidOperationException(
                        "Bug interne : deux éléments partagent l'identifiant « "
                        + item.Id + " » (dont « " + item.Title + " »). "
                        + "Enregistrement refusé pour ne perdre aucun document.");
            using (var stream = new FileStream(tempPath, FileMode.Create))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.json", Json.Write(BuildManifest(project)));
                WriteEntry(archive, "styles.json", Json.Write(BuildStyles(project.Styles)));
                WriteEntry(archive, "sheets/templates.json", Json.Write(BuildTemplates(project)));
                foreach (var kv in project.Images)
                {
                    if (kv.Value.Bytes == null) continue;
                    var imageEntry = archive.CreateEntry("images/" + kv.Key + (kv.Value.Extension ?? ""));
                    using (var imageStream = imageEntry.Open())
                        imageStream.Write(kv.Value.Bytes, 0, kv.Value.Bytes.Length);
                }
                foreach (var item in project.AllItems())
                {
                    if (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet)
                        WriteEntry(archive, "texts/" + item.Id + ".json",
                            Json.Write(BuildDocument(item.Document)));
                    if (item.Kind == ItemKind.Media && item.MediaBytes != null)
                    {
                        var media = archive.CreateEntry("research/" + item.Id + (item.MediaExtension ?? ""));
                        using (var mediaStream = media.Open())
                            mediaStream.Write(item.MediaBytes, 0, item.MediaBytes.Length);
                    }
                }
                // Les instantanés (v17) : une entrée chacun, JSON du document
                // écrit tel quel — jamais resérialisé, jamais purgé ici.
                foreach (var snapshot in project.Snapshots)
                {
                    // Compression rapide : un instantané se relit rarement, et
                    // c'est le deflate qui coûte à chaque sauvegarde (A3).
                    var entry = archive.CreateEntry("snapshots/" + snapshot.ItemId + "/" + snapshot.Id + ".json",
                        System.IO.Compression.CompressionLevel.Fastest);
                    using (var entryStream = entry.Open())
                    {
                        var head = new UTF8Encoding(false).GetBytes(Json.Write(BuildSnapshotMeta(snapshot)) + "\n");
                        entryStream.Write(head, 0, head.Length);
                        if (snapshot.Data != null) entryStream.Write(snapshot.Data, 0, snapshot.Data.Length);
                    }
                }
            }

            if (File.Exists(path))
                File.Replace(tempPath, path, path + ".bak");
            else
                File.Move(tempPath, path);
        }

        private static Dictionary<string, object> BuildSnapshotMeta(Snapshot snapshot)
        {
            var meta = new Dictionary<string, object>();
            meta["id"] = snapshot.Id;
            meta["item"] = snapshot.ItemId;
            meta["date"] = snapshot.Date;
            if (snapshot.Label.Length > 0) meta["label"] = snapshot.Label;
            meta["origin"] = snapshot.Origin;
            meta["words"] = snapshot.Words;
            meta["fingerprint"] = snapshot.Fingerprint.ToString();
            return meta;
        }

        /// <summary>Le document d'un item sous sa forme .plot (texts/) — LA
        /// sérialisation, aussi celle des instantanés (produite une fois).</summary>
        public static string SerializeDocument(TextDocument document)
        {
            return Json.Write(BuildDocument(document ?? new TextDocument()));
        }

        /// <summary>L'inverse : un document depuis son JSON (instantané décodé
        /// paresseusement).</summary>
        public static TextDocument DeserializeDocument(string json)
        {
            return ParseDocument(json ?? "");
        }

        /// <summary>Lit les entrées snapshots/ d'une archive (v17). Un
        /// instantané dont l'item n'existe pas dans le projet est abandonné.</summary>
        private static void ReadSnapshots(ZipArchive archive, Project project, List<string> warnings)
        {
            var known = new HashSet<string>();
            foreach (var item in project.AllItems()) known.Add(item.Id);
            var loaded = new List<Snapshot>();
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith("snapshots/", StringComparison.Ordinal)) continue;
                try
                {
                    byte[] bytes;
                    using (var stream = entry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        bytes = buffer.ToArray();
                    }
                    var cut = Array.IndexOf(bytes, (byte)'\n');
                    if (cut < 0) continue;
                    var meta = Json.AsObject(Json.Parse(Encoding.UTF8.GetString(bytes, 0, cut)));
                    if (meta == null) continue;
                    var data = new byte[bytes.Length - cut - 1];
                    Array.Copy(bytes, cut + 1, data, 0, data.Length);
                    var snapshot = new Snapshot
                    {
                        ItemId = Json.AsString(Json.Field(meta, "item")) ?? "",
                        Date = Json.AsString(Json.Field(meta, "date")) ?? "",
                        Label = Json.AsString(Json.Field(meta, "label")) ?? "",
                        Origin = Json.AsString(Json.Field(meta, "origin")) ?? SnapshotOrigin.Manual,
                        Words = (int)Json.AsDouble(Json.Field(meta, "words"), 0),
                        Data = data
                    };
                    var id = Json.AsString(Json.Field(meta, "id"));
                    if (!string.IsNullOrEmpty(id)) snapshot.Id = id;
                    long fingerprint;
                    if (long.TryParse(Json.AsString(Json.Field(meta, "fingerprint")) ?? "", out fingerprint)) snapshot.Fingerprint = fingerprint;
                    if (!known.Contains(snapshot.ItemId))
                    {
                        Warn(warnings, "Un instantané (" + snapshot.Date + ") appartient à un élément qui n'existe plus : abandonné.");
                        continue;
                    }
                    loaded.Add(snapshot);
                }
                catch (Exception error)
                {
                    Warn(warnings, "Instantané « " + entry.FullName + " » illisible : " + error.Message);
                }
            }
            // L'ordre chronologique (l'ordre des entrées du zip n'en garantit aucun).
            loaded.Sort(delegate(Snapshot a, Snapshot b) { return string.CompareOrdinal(a.Date, b.Date); });
            project.Snapshots.AddRange(loaded);
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }

        private static Dictionary<string, object> BuildManifest(Project project)
        {
            var manifest = new Dictionary<string, object>();
            manifest["version"] = FormatVersion;
            manifest["name"] = project.Name;
            manifest["author"] = project.Author;
            manifest["separatorText"] = project.SeparatorText;
            if (project.SeparatorFont != null) manifest["separatorFont"] = project.SeparatorFont;
            manifest["separatorSizePt"] = project.SeparatorSizePt;
            if (project.CustomColors.Count > 0)
                manifest["customColors"] = new List<object>(project.CustomColors.ToArray());
            if (project.HyphenExceptions.Count > 0)
                manifest["hyphenExceptions"] = new List<object>(project.HyphenExceptions.ToArray());
            if (project.ProofIgnored.Count > 0)
                manifest["proofIgnored"] = new List<object>(project.ProofIgnored.ToArray());
            if (project.Lexicon.Count > 0)
                manifest["lexicon"] = LexiconEntry.ToJsonList(project.Lexicon);
            if (project.IgnoredRules.Count > 0)
                manifest["ignoredRules"] = new List<object>(project.IgnoredRules.ToArray());
            if (project.RelationKinds.Count > 0)
                manifest["relationKinds"] = new List<object>(project.RelationKinds.ToArray());
            manifest["createdAt"] = project.CreatedAt;
            manifest["modifiedAt"] = project.ModifiedAt;
            manifest["page"] = BuildPageSetup(project.Page);
            if (project.Journal.DailyGoal > 0 || project.Journal.Days.Count > 0)
            {
                var journal = new Dictionary<string, object>();
                if (project.Journal.DailyGoal > 0)
                    journal["goal"] = (double)project.Journal.DailyGoal;
                if (project.Journal.LastCelebrated != null)
                    journal["praised"] = project.Journal.LastCelebrated;
                var days = new List<object>();
                foreach (var day in project.Journal.Days)
                {
                    if (day.Words <= 0) continue;
                    var entry = new Dictionary<string, object>();
                    entry["d"] = day.Date;
                    entry["w"] = (double)day.Words;
                    days.Add(entry);
                }
                if (days.Count > 0) journal["days"] = days;
                manifest["journal"] = journal;
            }
            var roots = new List<object>();
            foreach (var root in project.Roots) roots.Add(BuildNode(root, 0));
            manifest["binder"] = roots;
            return manifest;
        }

        private static Dictionary<string, object> BuildPageSetup(PageSetup page)
        {
            var p = new Dictionary<string, object>();
            p["widthMm"] = page.PageWidthMm;
            p["heightMm"] = page.PageHeightMm;
            p["marginTopMm"] = page.MarginTopMm;
            p["marginBottomMm"] = page.MarginBottomMm;
            p["marginLeftMm"] = page.MarginLeftMm;
            p["marginRightMm"] = page.MarginRightMm;
            p["columns"] = (double)page.Columns;
            p["showMargins"] = page.ShowMarginGuides;
            p["lineNumbers"] = page.LineNumbers;
            p["hyphenation"] = page.Hyphenation;
            p["footerNumbers"] = page.FooterPageNumbers;
            p["footerFont"] = page.FooterFont;
            p["footerSizePt"] = page.FooterSizePt;
            return p;
        }

        private static Dictionary<string, object> BuildNode(BinderItem item, int depth)
        {
            // Une arborescence pathologique (cycle Parent/Children, fichier
            // trafiqué) ferait déborder la pile — irrattrapable en .NET : le
            // processus meurt sans message ET sans sauvegarde. On lève propre.
            if (depth > MaxTreeDepth)
                throw new InvalidOperationException(
                    "L'arborescence de la Pile dépasse " + MaxTreeDepth
                    + " niveaux : probable cycle. Enregistrement interrompu.");
            var node = new Dictionary<string, object>();
            node["id"] = item.Id;
            node["title"] = item.Title;
            node["kind"] = item.Kind == ItemKind.Category ? "category"
                         : item.Kind == ItemKind.Folder ? "folder"
                         : item.Kind == ItemKind.Sheet ? "sheet"
                         : item.Kind == ItemKind.Media ? "media"
                         : item.Kind == ItemKind.Book ? "book"
                         : item.Kind == ItemKind.Plan ? "plan"
                         : item.Kind == ItemKind.PageTemplate ? "pagetpl" : "text";
            if (item.CategoryKey != null) node["category"] = item.CategoryKey;
            if (item.Page != null) node["page"] = BuildPageSetup(item.Page);
            if (item.Header != null) node["header"] = GabaritFile.BuildHeaderFooter(item.Header);
            if (item.Footer != null) node["footer"] = GabaritFile.BuildHeaderFooter(item.Footer);
            if (item.PageTemplateId != null) node["pageTemplate"] = item.PageTemplateId;
            if (item.IsExtraPage) node["extra"] = true;
            if (item.IsToc) node["toc"] = true;
            if (item.Kind == ItemKind.PageTemplate)
            {
                if (item.TemplateColor != null) node["chip"] = item.TemplateColor;
                if (item.HeaderRecto != null) node["headerRecto"] = GabaritFile.BuildHeaderFooter(item.HeaderRecto);
                if (item.FooterRecto != null) node["footerRecto"] = GabaritFile.BuildHeaderFooter(item.FooterRecto);
                if (item.HeaderVerso != null) node["headerVerso"] = GabaritFile.BuildHeaderFooter(item.HeaderVerso);
                if (item.FooterVerso != null) node["footerVerso"] = GabaritFile.BuildHeaderFooter(item.FooterVerso);
                if (item.HeaderGapMm != 0) node["headerGapMm"] = item.HeaderGapMm;
                if (item.FooterGapMm != 0) node["footerGapMm"] = item.FooterGapMm;
                if (item.HeaderHideFirst) node["headerHideFirst"] = true;
                if (item.FooterHideFirst) node["footerHideFirst"] = true;
            }
            if (item.Kind == ItemKind.Plan && item.Plan != null)
            {
                var plan = new Dictionary<string, object>();
                if (item.Plan.LinkedItemId != null) plan["link"] = item.Plan.LinkedItemId;
                if (item.Plan.ColumnWord != PlanInfo.DefaultColumnWord) plan["columnWord"] = item.Plan.ColumnWord;
                var columns = new List<object>();
                foreach (var column in item.Plan.Columns)
                {
                    var c = new Dictionary<string, object>();
                    c["id"] = column.Id;
                    c["title"] = column.Title;
                    if (column.LinkedTextId != null) c["text"] = column.LinkedTextId;
                    var entries = new List<object>();
                    foreach (var entry in column.Entries)
                    {
                        var e = new Dictionary<string, object>();
                        e["id"] = entry.Id;
                        e["kind"] = entry.Kind;
                        e["text"] = entry.Text;
                        if (entry.Color != null) e["color"] = entry.Color;
                        e["intensity"] = entry.Intensity;
                        entries.Add(e);
                    }
                    c["entries"] = entries;
                    columns.Add(c);
                }
                plan["columns"] = columns;
                node["plan"] = plan;
            }
            if (item.Kind == ItemKind.Book && item.Book != null)
            {
                var book = new Dictionary<string, object>();
                if (item.Book.Subtitle.Length > 0) book["subtitle"] = item.Book.Subtitle;
                if (item.Book.AuthorOverride.Length > 0) book["author"] = item.Book.AuthorOverride;
                if (item.Book.Publisher.Length > 0) book["publisher"] = item.Book.Publisher;
                if (item.Book.Collection.Length > 0) book["collection"] = item.Book.Collection;
                if (item.Book.Isbn.Length > 0) book["isbn"] = item.Book.Isbn;
                if (item.Book.Year.Length > 0) book["year"] = item.Book.Year;
                book["bleedMm"] = item.Book.BleedMm;
                if (item.Book.ChapterGoal > 0) book["chapterGoal"] = item.Book.ChapterGoal;
                book["template"] = BuildPageSetup(item.Book.Template);
                node["book"] = book;
            }
            if (!string.IsNullOrEmpty(item.Synopsis)) node["synopsis"] = item.Synopsis;
            if (!string.IsNullOrEmpty(item.Notes)) node["notes"] = item.Notes;
            if (item.Icon != null) node["icon"] = item.Icon;
            if (item.Status != null) node["status"] = item.Status;
            if (item.CardColor != null) node["cardColor"] = item.CardColor;
            if (item.ImageId != null) node["image"] = item.ImageId;
            if (item.Kind == ItemKind.Sheet)
            {
                if (item.TemplateId != null) node["template"] = item.TemplateId;
                if (item.CategoryId != null) node["sheetCategory"] = item.CategoryId;
                if (item.FieldValues.Count > 0)
                {
                    var fields = new Dictionary<string, object>();
                    foreach (var kv in item.FieldValues)
                        if (!string.IsNullOrEmpty(kv.Value)) fields[kv.Key] = kv.Value;
                    if (fields.Count > 0) node["fields"] = fields;
                }
                if (item.FreeInfo.Count > 0)
                {
                    var info = new List<object>();
                    foreach (var entry in item.FreeInfo)
                    {
                        var e = new Dictionary<string, object>();
                        e["id"] = entry.Id;
                        e["title"] = entry.Title;
                        e["value"] = entry.Value;
                        if (entry.Group.Length > 0) e["group"] = entry.Group;
                        info.Add(e);
                    }
                    node["info"] = info;
                }
                if (item.Relations.Count > 0)
                {
                    var relations = new List<object>();
                    foreach (var relation in item.Relations)
                    {
                        var r = new Dictionary<string, object>();
                        r["id"] = relation.Id;
                        r["kind"] = relation.Kind;
                        if (relation.TargetId != null) r["target"] = relation.TargetId;
                        if (relation.Name.Length > 0) r["name"] = relation.Name;
                        relations.Add(r);
                    }
                    node["relations"] = relations;
                }
            }
            if (item.Kind == ItemKind.Media && item.MediaExtension != null)
                node["mediaExt"] = item.MediaExtension;
            if (item.Children.Count > 0)
            {
                var children = new List<object>();
                foreach (var child in item.Children) children.Add(BuildNode(child, depth + 1));
                node["children"] = children;
            }
            return node;
        }

        private static Dictionary<string, object> BuildStyles(StyleSheet sheet)
        {
            var root = new Dictionary<string, object>();
            var list = new List<object>();
            foreach (var style in sheet.Styles)
            {
                var s = new Dictionary<string, object>();
                s["id"] = style.Id;
                s["name"] = style.Name;
                s["font"] = style.FontFamily;
                s["size"] = style.FontSize;
                if (style.Bold) s["bold"] = true;
                if (style.Italic) s["italic"] = true;
                if (style.Color != null) s["color"] = style.Color;
                s["align"] = style.Align;
                if (style.SpaceBefore != 0) s["spaceBefore"] = style.SpaceBefore;
                if (style.SpaceAfter != 0) s["spaceAfter"] = style.SpaceAfter;
                if (style.FirstLineIndent != 0) s["firstIndent"] = style.FirstLineIndent;
                if (style.LeftIndent != 0) s["leftIndent"] = style.LeftIndent;
                if (style.RightIndent != 0) s["rightIndent"] = style.RightIndent;
                if (style.LastLineIndent != 0) s["lastIndent"] = style.LastLineIndent;
                s["lineHeight"] = style.LineHeight;
                if (!style.Ligatures) s["ligatures"] = false;
                if (!style.HyphenationEnabled) s["hyphen"] = false;
                if (style.HyphenMinWordLength != 5) s["hyphenWord"] = (double)style.HyphenMinWordLength;
                if (style.HyphenMinBefore != 2) s["hyphenBefore"] = (double)style.HyphenMinBefore;
                if (style.HyphenMinAfter != 2) s["hyphenAfter"] = (double)style.HyphenMinAfter;
                if (style.HyphenConsecutiveLimit != 3) s["hyphenLimit"] = (double)style.HyphenConsecutiveLimit;
                if (style.JustifyWordMin != 80) s["jWordMin"] = style.JustifyWordMin;
                if (style.JustifyWordOpt != 100) s["jWordOpt"] = style.JustifyWordOpt;
                if (style.JustifyWordMax != 115) s["jWordMax"] = style.JustifyWordMax;
                if (style.JustifyLetterMin != 0) s["jLetterMin"] = style.JustifyLetterMin;
                if (style.JustifyLetterOpt != 0) s["jLetterOpt"] = style.JustifyLetterOpt;
                if (style.JustifyLetterMax != 0) s["jLetterMax"] = style.JustifyLetterMax;
                if (style.JustifyGlyphMin != 100) s["jGlyphMin"] = style.JustifyGlyphMin;
                if (style.JustifyGlyphOpt != 100) s["jGlyphOpt"] = style.JustifyGlyphOpt;
                if (style.JustifyGlyphMax != 100) s["jGlyphMax"] = style.JustifyGlyphMax;
                if (style.AutoLeadingPercent != 120) s["autoLeading"] = style.AutoLeadingPercent;
                if (!style.KeepWithPrevious) s["keepPrev"] = false;
                if (style.KeepNextLines != 0) s["keepNext"] = (double)style.KeepNextLines;
                if (style.KeepLinesTogether) s["keepLines"] = true;
                list.Add(s);
            }
            root["styles"] = list;
            return root;
        }

        private static Dictionary<string, object> BuildTemplates(Project project)
        {
            var root = new Dictionary<string, object>();
            var list = new List<object>();
            foreach (var template in project.Templates)
            {
                var t = new Dictionary<string, object>();
                t["id"] = template.Id;
                t["name"] = template.Name;
                var fields = new List<object>();
                foreach (var field in template.Fields)
                {
                    var f = new Dictionary<string, object>();
                    f["id"] = field.Id;
                    f["name"] = field.Name;
                    if (field.Kind != "text") f["kind"] = field.Kind;
                    if (field.Group.Length > 0) f["group"] = field.Group;
                    fields.Add(f);
                }
                t["fields"] = fields;
                list.Add(t);
            }
            root["templates"] = list;
            // v11 — les catégories de fiches, dans leur ordre d'affichage.
            var categories = new List<object>();
            foreach (var category in project.SheetCategories)
            {
                var c = new Dictionary<string, object>();
                c["id"] = category.Id;
                c["name"] = category.Name;
                if (category.TemplateId != null) c["template"] = category.TemplateId;
                categories.Add(c);
            }
            root["categories"] = categories;
            return root;
        }

        private static Dictionary<string, object> BuildDocument(TextDocument document)
        {
            var root = new Dictionary<string, object>();
            var paragraphs = new List<object>();
            foreach (var paragraph in document.Paragraphs)
            {
                var p = new Dictionary<string, object>();
                if (paragraph.StyleId != "body") p["style"] = paragraph.StyleId;
                if (paragraph.AlignOverride != null) p["align"] = paragraph.AlignOverride;
                if (paragraph.ListKind != null) p["list"] = paragraph.ListKind;
                if (paragraph.PageBreakBefore) p["pb"] = true;
                if (paragraph.AllowWidows) p["wo"] = true; // veuves/orphelines autorisées ici
                var runs = new List<object>();
                foreach (var run in paragraph.Runs)
                {
                    var r = new Dictionary<string, object>();
                    if (run.IsLineBreak) { r["br"] = true; runs.Add(r); continue; }
                    if (run.FootnoteId != null) { r["fn"] = run.FootnoteId; runs.Add(r); continue; }
                    if (run.ImageId != null) { r["img"] = run.ImageId; runs.Add(r); continue; }
                    if (run.IsRule) { r["hr"] = true; runs.Add(r); continue; }
                    r["t"] = run.Text;
                    if (run.Bold.HasValue) r["b"] = run.Bold.Value;
                    if (run.Italic.HasValue) r["i"] = run.Italic.Value;
                    if (run.Underline.HasValue) r["u"] = run.Underline.Value;
                    if (run.Strike.HasValue) r["st"] = run.Strike.Value;
                    if (run.Weight != null) r["w"] = run.Weight;
                    if (run.Tracking.HasValue) r["trk"] = run.Tracking.Value;
                    if (run.FontFamily != null) r["font"] = run.FontFamily;
                    if (run.FontSize.HasValue) r["size"] = run.FontSize.Value;
                    if (run.Color != null) r["color"] = run.Color;
                    if (run.Highlight != null) r["hl"] = run.Highlight;
                    if (run.AnnotationId != null) r["ann"] = run.AnnotationId;
                    if (run.NoProof) r["np"] = true; // « ne pas corriger » (v8)
                    runs.Add(r);
                }
                p["runs"] = runs;
                paragraphs.Add(p);
            }
            root["paragraphs"] = paragraphs;
            if (document.Footnotes.Count > 0)
            {
                var notes = new List<object>();
                foreach (var note in document.Footnotes)
                {
                    var n = new Dictionary<string, object>();
                    n["id"] = note.Id;
                    n["text"] = note.Text;
                    notes.Add(n);
                }
                root["footnotes"] = notes;
            }
            if (document.Annotations.Count > 0)
            {
                var annotations = new List<object>();
                foreach (var annotation in document.Annotations)
                {
                    var a = new Dictionary<string, object>();
                    a["id"] = annotation.Id;
                    a["text"] = annotation.Text;
                    if (annotation.Created.Length > 0) a["created"] = annotation.Created;
                    if (annotation.Resolved) a["resolved"] = true;
                    annotations.Add(a);
                }
                root["annotations"] = annotations;
            }
            return root;
        }

        // ------------------------------------------------------- reading

        public static Project Load(string path)
        {
            return Load(path, null);
        }

        /// <summary>Loads a .plot. The manifest is the only fatal entry: any
        /// other unreadable entry (text, image, media, styles) degrades to a
        /// warning so a 119/120-chapters project still opens. Warnings are
        /// collected into <paramref name="warnings"/> when provided.</summary>
        public static Project Load(string path, List<string> warnings)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry == null)
                    throw new InvalidDataException(
                        "Le fichier ne contient pas de manifest.json : ce n'est pas un projet Univers Sale valide.");

                var manifest = Json.AsObject(Json.Parse(ReadEntry(manifestEntry)));
                if (manifest == null)
                    throw new InvalidDataException("Le manifeste du projet est illisible.");

                var project = new Project();

                // A2 — le champ version, écrit depuis la v1, est enfin LU.
                // Absent ou illisible = v1 (fichiers de la phase 0). Supérieur
                // à FormatVersion = fichier d'un Marabook plus récent : ouvert
                // en lecture seule (la sauvegarde perdrait les champs inconnus).
                project.LoadedFormatVersion = (int)Json.AsDouble(Json.Field(manifest, "version"), 1);
                if (project.LoadedFormatVersion > FormatVersion)
                    project.ReadOnlyNewerFormat = true;
                project.Name = Json.AsString(Json.Field(manifest, "name")) ?? "Sans titre";
                project.Author = Json.AsString(Json.Field(manifest, "author")) ?? "";
                project.SeparatorText = Json.AsString(Json.Field(manifest, "separatorText")) ?? "***";
                project.SeparatorFont = Json.AsString(Json.Field(manifest, "separatorFont"));
                project.SeparatorSizePt = Json.AsDouble(Json.Field(manifest, "separatorSizePt"), 12);
                var customColors = Json.AsList(Json.Field(manifest, "customColors"));
                if (customColors != null)
                    foreach (var entry in customColors)
                        if (entry is string) project.CustomColors.Add((string)entry);
                var hyphenExceptions = Json.AsList(Json.Field(manifest, "hyphenExceptions"));
                if (hyphenExceptions != null)
                    foreach (var entry in hyphenExceptions)
                        if (entry is string) project.HyphenExceptions.Add((string)entry);
                var proofIgnored = Json.AsList(Json.Field(manifest, "proofIgnored"));
                if (proofIgnored != null)
                    foreach (var entry in proofIgnored)
                        if (entry is string) project.ProofIgnored.Add((string)entry);
                project.Lexicon = LexiconEntry.FromJsonList(Json.AsList(Json.Field(manifest, "lexicon")));
                var learnedWords = Json.AsList(Json.Field(manifest, "learnedWords"));
                if (learnedWords != null) // v9-v12 : mots nus → entrées « autre »
                {
                    var words = new List<string>();
                    foreach (var entry in learnedWords)
                        if (entry is string) words.Add((string)entry);
                    LexiconEntry.MergeWords(project.Lexicon, words);
                }
                var ignoredRules = Json.AsList(Json.Field(manifest, "ignoredRules"));
                if (ignoredRules != null)
                    foreach (var entry in ignoredRules)
                        if (entry is string) project.IgnoredRules.Add((string)entry);
                var relationKinds = Json.AsList(Json.Field(manifest, "relationKinds"));
                if (relationKinds != null)
                    foreach (var entry in relationKinds)
                        if (entry is string) project.AddRelationKind((string)entry);
                project.CreatedAt = Json.AsString(Json.Field(manifest, "createdAt")) ?? "";
                project.ModifiedAt = Json.AsString(Json.Field(manifest, "modifiedAt")) ?? "";

                var journal = Json.AsObject(Json.Field(manifest, "journal"));
                if (journal != null)
                {
                    project.Journal.DailyGoal = (int)Json.AsDouble(Json.Field(journal, "goal"), 0);
                    project.Journal.LastCelebrated = Json.AsString(Json.Field(journal, "praised"));
                    var days = Json.AsList(Json.Field(journal, "days"));
                    if (days != null)
                        foreach (var rawDay in days)
                        {
                            var dayObj = Json.AsObject(rawDay);
                            if (dayObj == null) continue;
                            var date = Json.AsString(Json.Field(dayObj, "d"));
                            var words = (int)Json.AsDouble(Json.Field(dayObj, "w"), 0);
                            if (date != null && words > 0)
                                project.Journal.Days.Add(new JournalDay { Date = date, Words = words });
                        }
                }

                var stylesEntry = archive.GetEntry("styles.json");
                if (stylesEntry != null)
                    try { project.Styles = ReadStyles(ReadEntry(stylesEntry)); }
                    catch (Exception error)
                    {
                        project.Styles = StyleSheet.CreateDefault();
                        Warn(warnings, "Feuille de styles illisible ("
                            + error.Message + ") : styles par défaut appliqués.");
                    }

                var templatesEntry = archive.GetEntry("sheets/templates.json");
                if (templatesEntry != null)
                    try { ReadTemplates(ReadEntry(templatesEntry), project); }
                    catch (Exception error)
                    {
                        Warn(warnings, "Modèles de fiches illisibles ("
                            + error.Message + ") : les modèles par défaut "
                            + "seront recréés.");
                    }

                var page = Json.AsObject(Json.Field(manifest, "page"));
                if (page != null) project.Page = ReadPageSetup(page);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith("images/", StringComparison.Ordinal)) continue;
                    var file = entry.FullName.Substring("images/".Length);
                    var dot = file.IndexOf('.');
                    var id = dot < 0 ? file : file.Substring(0, dot);
                    if (id.Length == 0) continue;
                    try
                    {
                        using (var imageStream = entry.Open())
                        using (var buffer = new MemoryStream())
                        {
                            imageStream.CopyTo(buffer);
                            project.Images[id] = new ProjectImage
                            {
                                Bytes = buffer.ToArray(),
                                Extension = dot < 0 ? "" : file.Substring(dot)
                            };
                        }
                    }
                    catch (Exception error)
                    {
                        Warn(warnings, "Image « " + entry.FullName
                            + " » illisible (" + error.Message + ") : ignorée.");
                    }
                }

                var roots = Json.AsList(Json.Field(manifest, "binder"));
                if (roots != null)
                    foreach (var root in roots)
                    {
                        var item = ReadNode(root, archive, warnings);
                        if (item != null) project.Roots.Add(item);
                    }

                // A valid project always has its four categories, whatever the file says.
                EnsureCategory(project, "Écrits", Project.KeyWritings);
                EnsureCategory(project, "Recherche", Project.KeyResearch);
                EnsureCategory(project, "Fiches", Project.KeySheets);
                EnsureCategory(project, "Plans", Project.KeyPlans, Project.KeyDictionary); // b35, après Fiches
                EnsureCategory(project, "Dictionnaire", Project.KeyDictionary); // b33, avant la Corbeille
                EnsureCategory(project, "Corbeille", Project.KeyTrash);

                // A5 — deux items de même id : GetEntry n'aurait relu qu'un
                // texte pour les deux. Le second reçoit un id neuf (ordre et
                // contenu conservés — les octets sont déjà en mémoire) et la
                // sauvegarde suivante ré-écrira deux entrées distinctes.
                var seenIds = new HashSet<string>();
                foreach (var item in project.AllItems())
                    if (!seenIds.Add(item.Id))
                    {
                        var old = item.Id;
                        item.Id = Guid.NewGuid().ToString("N");
                        Warn(warnings, "Deux éléments partageaient l'identifiant « "
                            + old + " » : « " + item.Title
                            + " » a reçu un identifiant neuf.");
                    }

                project.RelinkParents();
                ReadSnapshots(archive, project, warnings); // v17 ; rien dans un .plot d'avant
                // Batch 31 : un projet d'avant les catégories de fiches est
                // migré ici (défauts + adoption des modèles par nom), et les
                // fiches orphelines rejoignent la catégorie de leur modèle.
                project.EnsureSheetCategories();
                // Batch 36 : le modèle Personnage d'un .plot d'avant la v16
                // reçoit « Âge » et l'apparence par défaut — une fois.
                if (project.LoadedFormatVersion < 16) project.UpgradeCharacterTemplate();
                return project;
            }
        }

        private static void Warn(List<string> warnings, string message)
        {
            if (warnings != null) warnings.Add(message);
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static StyleSheet ReadStyles(string json)
        {
            var sheet = new StyleSheet();
            var list = Json.AsList(Json.Field(Json.Parse(json), "styles"));
            if (list != null)
                foreach (var entry in list)
                {
                    var s = Json.AsObject(entry);
                    if (s == null) continue;
                    var style = new ParagraphStyle();
                    var id = Json.AsString(Json.Field(s, "id"));
                    if (!string.IsNullOrEmpty(id)) style.Id = id;
                    style.Name = Json.AsString(Json.Field(s, "name")) ?? "Style";
                    style.FontFamily = Json.AsString(Json.Field(s, "font")) ?? "Georgia";
                    style.FontSize = Json.AsDouble(Json.Field(s, "size"), 15);
                    style.Bold = Json.AsBool(Json.Field(s, "bold"), false);
                    style.Italic = Json.AsBool(Json.Field(s, "italic"), false);
                    style.Color = Json.AsString(Json.Field(s, "color"));
                    style.Align = Json.AsString(Json.Field(s, "align")) ?? "left";
                    style.SpaceBefore = Json.AsDouble(Json.Field(s, "spaceBefore"), 0);
                    style.SpaceAfter = Json.AsDouble(Json.Field(s, "spaceAfter"), 0);
                    style.FirstLineIndent = Json.AsDouble(Json.Field(s, "firstIndent"), 0);
                    style.LeftIndent = Json.AsDouble(Json.Field(s, "leftIndent"), 0);
                    style.RightIndent = Json.AsDouble(Json.Field(s, "rightIndent"), 0);
                    style.LastLineIndent = Json.AsDouble(Json.Field(s, "lastIndent"), 0);
                    // v4 files carry no lineHeight: 0 = "auto" (WPF default),
                    // preserving their look; new sheets get the 14.4 pt default.
                    style.LineHeight = Json.AsDouble(Json.Field(s, "lineHeight"), 0);
                    style.Ligatures = Json.AsBool(Json.Field(s, "ligatures"), true);
                    style.HyphenationEnabled = Json.AsBool(Json.Field(s, "hyphen"), true);
                    style.HyphenMinWordLength = (int)Json.AsDouble(Json.Field(s, "hyphenWord"), 5);
                    style.HyphenMinBefore = (int)Json.AsDouble(Json.Field(s, "hyphenBefore"), 2);
                    style.HyphenMinAfter = (int)Json.AsDouble(Json.Field(s, "hyphenAfter"), 2);
                    style.HyphenConsecutiveLimit = (int)Json.AsDouble(Json.Field(s, "hyphenLimit"), 3);
                    style.JustifyWordMin = Json.AsDouble(Json.Field(s, "jWordMin"), 80);
                    style.JustifyWordOpt = Json.AsDouble(Json.Field(s, "jWordOpt"), 100);
                    style.JustifyWordMax = Json.AsDouble(Json.Field(s, "jWordMax"), 115);
                    style.JustifyLetterMin = Json.AsDouble(Json.Field(s, "jLetterMin"), 0);
                    style.JustifyLetterOpt = Json.AsDouble(Json.Field(s, "jLetterOpt"), 0);
                    style.JustifyLetterMax = Json.AsDouble(Json.Field(s, "jLetterMax"), 0);
                    style.JustifyGlyphMin = Json.AsDouble(Json.Field(s, "jGlyphMin"), 100);
                    style.JustifyGlyphOpt = Json.AsDouble(Json.Field(s, "jGlyphOpt"), 100);
                    style.JustifyGlyphMax = Json.AsDouble(Json.Field(s, "jGlyphMax"), 100);
                    style.AutoLeadingPercent = Json.AsDouble(Json.Field(s, "autoLeading"), 120);
                    style.KeepWithPrevious = Json.AsBool(Json.Field(s, "keepPrev"), true);
                    style.KeepNextLines = (int)Json.AsDouble(Json.Field(s, "keepNext"), 0);
                    style.KeepLinesTogether = Json.AsBool(Json.Field(s, "keepLines"), false);
                    sheet.Styles.Add(style);
                }
            if (sheet.Styles.Count == 0) return StyleSheet.CreateDefault();
            if (sheet.Find("body") == null) // pathological file: still guarantee a body
                sheet.Styles.Insert(0, StyleSheet.CreateDefault().Body);
            return sheet;
        }

        private static PageSetup ReadPageSetup(Dictionary<string, object> p)
        {
            var page = new PageSetup();
            page.PageWidthMm = Json.AsDouble(Json.Field(p, "widthMm"), 210);
            page.PageHeightMm = Json.AsDouble(Json.Field(p, "heightMm"), 297);
            page.MarginTopMm = Json.AsDouble(Json.Field(p, "marginTopMm"), 25);
            page.MarginBottomMm = Json.AsDouble(Json.Field(p, "marginBottomMm"), 25);
            page.MarginLeftMm = Json.AsDouble(Json.Field(p, "marginLeftMm"), 25);
            page.MarginRightMm = Json.AsDouble(Json.Field(p, "marginRightMm"), 25);
            page.Columns = (int)Json.AsDouble(Json.Field(p, "columns"), 1);
            if (page.Columns < 1) page.Columns = 1;
            if (page.Columns > 3) page.Columns = 3;
            page.ShowMarginGuides = Json.AsBool(Json.Field(p, "showMargins"), true);
            page.LineNumbers = Json.AsBool(Json.Field(p, "lineNumbers"), false);
            page.Hyphenation = Json.AsBool(Json.Field(p, "hyphenation"), false);
            page.FooterPageNumbers = Json.AsBool(Json.Field(p, "footerNumbers"), true);
            page.FooterFont = Json.AsString(Json.Field(p, "footerFont")) ?? "Times New Roman";
            page.FooterSizePt = Json.AsDouble(Json.Field(p, "footerSizePt"), 10);
            return page;
        }

        private static void ReadTemplates(string json, Project project)
        {
            var templates = new List<SheetTemplate>();
            var root = Json.Parse(json);
            var list = Json.AsList(Json.Field(root, "templates"));
            if (list != null)
                foreach (var entry in list)
                {
                    var t = Json.AsObject(entry);
                    if (t == null) continue;
                    var template = new SheetTemplate();
                    var id = Json.AsString(Json.Field(t, "id"));
                    if (!string.IsNullOrEmpty(id)) template.Id = id;
                    template.Name = Json.AsString(Json.Field(t, "name")) ?? "Modèle";
                    var fields = Json.AsList(Json.Field(t, "fields"));
                    if (fields != null)
                        foreach (var fieldEntry in fields)
                        {
                            var f = Json.AsObject(fieldEntry);
                            if (f == null) continue;
                            var field = new SheetField();
                            var fieldId = Json.AsString(Json.Field(f, "id"));
                            if (!string.IsNullOrEmpty(fieldId)) field.Id = fieldId;
                            field.Name = Json.AsString(Json.Field(f, "name")) ?? "Champ";
                            field.Kind = Json.AsString(Json.Field(f, "kind")) ?? "text";
                            field.Group = Json.AsString(Json.Field(f, "group")) ?? "";
                            template.Fields.Add(field);
                        }
                    templates.Add(template);
                }
            // an empty list is legitimate (user deleted them all)
            project.Templates = templates;
            // v11 — les catégories ; absentes d'un vieux .plot, la migration
            // EnsureSheetCategories (fin du chargement) les comblera.
            var categories = new List<SheetCategory>();
            var categoryList = Json.AsList(Json.Field(root, "categories"));
            if (categoryList != null)
                foreach (var entry in categoryList)
                {
                    var c = Json.AsObject(entry);
                    if (c == null) continue;
                    var category = new SheetCategory();
                    var id = Json.AsString(Json.Field(c, "id"));
                    if (!string.IsNullOrEmpty(id)) category.Id = id;
                    category.Name = Json.AsString(Json.Field(c, "name")) ?? "Catégorie";
                    category.TemplateId = Json.AsString(Json.Field(c, "template"));
                    categories.Add(category);
                }
            project.SheetCategories = categories;
        }

        private static BinderItem ReadNode(object node, ZipArchive archive, List<string> warnings)
        {
            var obj = Json.AsObject(node);
            if (obj == null) return null;

            var item = new BinderItem();
            var id = Json.AsString(Json.Field(obj, "id"));
            if (!string.IsNullOrEmpty(id)) item.Id = id;
            item.Title = Json.AsString(Json.Field(obj, "title")) ?? "";
            item.Synopsis = Json.AsString(Json.Field(obj, "synopsis")) ?? "";
            item.Notes = Json.AsString(Json.Field(obj, "notes")) ?? "";
            item.Icon = Json.AsString(Json.Field(obj, "icon"));
            item.Status = Json.AsString(Json.Field(obj, "status"));
            item.CardColor = Json.AsString(Json.Field(obj, "cardColor"));
            item.ImageId = Json.AsString(Json.Field(obj, "image"));
            item.CategoryKey = Json.AsString(Json.Field(obj, "category"));

            var kind = Json.AsString(Json.Field(obj, "kind"));
            item.Kind = kind == "category" ? ItemKind.Category
                      : kind == "folder" ? ItemKind.Folder
                      : kind == "sheet" ? ItemKind.Sheet
                      : kind == "media" ? ItemKind.Media
                      : kind == "book" ? ItemKind.Book
                      : kind == "plan" ? ItemKind.Plan
                      : kind == "pagetpl" ? ItemKind.PageTemplate : ItemKind.Text;

            var ownPage = Json.AsObject(Json.Field(obj, "page"));
            if (ownPage != null) item.Page = ReadPageSetup(ownPage);
            item.Header = GabaritFile.ReadHeaderFooter(Json.Field(obj, "header"));
            item.Footer = GabaritFile.ReadHeaderFooter(Json.Field(obj, "footer"));
            item.PageTemplateId = Json.AsString(Json.Field(obj, "pageTemplate"));
            item.IsExtraPage = Json.AsBool(Json.Field(obj, "extra"), false);
            item.IsToc = Json.AsBool(Json.Field(obj, "toc"), false);
            if (item.Kind == ItemKind.PageTemplate)
            {
                item.TemplateColor = Json.AsString(Json.Field(obj, "chip"));
                item.HeaderRecto = GabaritFile.ReadHeaderFooter(Json.Field(obj, "headerRecto"));
                item.FooterRecto = GabaritFile.ReadHeaderFooter(Json.Field(obj, "footerRecto"));
                item.HeaderVerso = GabaritFile.ReadHeaderFooter(Json.Field(obj, "headerVerso"));
                item.FooterVerso = GabaritFile.ReadHeaderFooter(Json.Field(obj, "footerVerso"));
                item.HeaderGapMm = Json.AsDouble(Json.Field(obj, "headerGapMm"), 0);
                item.FooterGapMm = Json.AsDouble(Json.Field(obj, "footerGapMm"), 0);
                item.HeaderHideFirst = Json.AsBool(Json.Field(obj, "headerHideFirst"), false);
                item.FooterHideFirst = Json.AsBool(Json.Field(obj, "footerHideFirst"), false);
            }

            if (item.Kind == ItemKind.Plan)
            {
                item.Plan = new PlanInfo();
                var plan = Json.AsObject(Json.Field(obj, "plan"));
                if (plan != null)
                {
                    item.Plan.LinkedItemId = Json.AsString(Json.Field(plan, "link"));
                    item.Plan.ColumnWord = Json.AsString(Json.Field(plan, "columnWord")) ?? PlanInfo.DefaultColumnWord;
                    var columns = Json.AsList(Json.Field(plan, "columns"));
                    if (columns != null)
                        foreach (var columnNode in columns)
                        {
                            var c = Json.AsObject(columnNode);
                            if (c == null) continue;
                            var column = new PlanColumn();
                            var columnId = Json.AsString(Json.Field(c, "id"));
                            if (!string.IsNullOrEmpty(columnId)) column.Id = columnId;
                            column.Title = Json.AsString(Json.Field(c, "title")) ?? "";
                            column.LinkedTextId = Json.AsString(Json.Field(c, "text"));
                            var entries = Json.AsList(Json.Field(c, "entries"));
                            if (entries != null)
                                foreach (var entryNode in entries)
                                {
                                    var e = Json.AsObject(entryNode);
                                    if (e == null) continue;
                                    var entry = new PlanEntry();
                                    var entryId = Json.AsString(Json.Field(e, "id"));
                                    if (!string.IsNullOrEmpty(entryId)) entry.Id = entryId;
                                    entry.Kind = Json.AsString(Json.Field(e, "kind")) == PlanEntry.KindNote
                                        ? PlanEntry.KindNote : PlanEntry.KindElement;
                                    entry.Text = Json.AsString(Json.Field(e, "text")) ?? "";
                                    entry.Color = Json.AsString(Json.Field(e, "color"));
                                    entry.Intensity = PlanIntensity.Clamp(Json.AsInt(Json.Field(e, "intensity"), 1));
                                    column.Entries.Add(entry);
                                }
                            item.Plan.Columns.Add(column);
                        }
                }
            }
            if (item.Kind == ItemKind.Book)
            {
                item.Book = new BookInfo();
                var book = Json.AsObject(Json.Field(obj, "book"));
                if (book != null)
                {
                    item.Book.Subtitle = Json.AsString(Json.Field(book, "subtitle")) ?? "";
                    item.Book.AuthorOverride = Json.AsString(Json.Field(book, "author")) ?? "";
                    item.Book.Publisher = Json.AsString(Json.Field(book, "publisher")) ?? "";
                    item.Book.Collection = Json.AsString(Json.Field(book, "collection")) ?? "";
                    item.Book.Isbn = Json.AsString(Json.Field(book, "isbn")) ?? "";
                    item.Book.Year = Json.AsString(Json.Field(book, "year")) ?? "";
                    item.Book.BleedMm = Json.AsDouble(Json.Field(book, "bleedMm"), 3);
                    item.Book.ChapterGoal = Math.Max(0, Json.AsInt(Json.Field(book, "chapterGoal"), 0));
                    var template = Json.AsObject(Json.Field(book, "template"));
                    if (template != null) item.Book.Template = ReadPageSetup(template);
                }
            }

            if (item.Kind == ItemKind.Text || item.Kind == ItemKind.Sheet)
                item.Document = ReadDocument(item, archive, warnings);

            if (item.Kind == ItemKind.Sheet)
            {
                item.TemplateId = Json.AsString(Json.Field(obj, "template"));
                item.CategoryId = Json.AsString(Json.Field(obj, "sheetCategory"));
                var fields = Json.AsObject(Json.Field(obj, "fields"));
                if (fields != null)
                    foreach (var kv in fields)
                        if (kv.Value is string) item.FieldValues[kv.Key] = (string)kv.Value;
                var info = Json.AsList(Json.Field(obj, "info"));
                if (info != null)
                    foreach (var infoEntry in info)
                    {
                        var e = Json.AsObject(infoEntry);
                        if (e == null) continue;
                        var entry = new InfoEntry();
                        var entryId = Json.AsString(Json.Field(e, "id"));
                        if (!string.IsNullOrEmpty(entryId)) entry.Id = entryId;
                        entry.Title = Json.AsString(Json.Field(e, "title")) ?? "";
                        entry.Value = Json.AsString(Json.Field(e, "value")) ?? "";
                        entry.Group = Json.AsString(Json.Field(e, "group")) ?? "";
                        item.FreeInfo.Add(entry);
                    }
                var relations = Json.AsList(Json.Field(obj, "relations"));
                if (relations != null)
                    foreach (var relationNode in relations)
                    {
                        var r = Json.AsObject(relationNode);
                        if (r == null) continue;
                        var relation = new SheetRelation();
                        var relationId = Json.AsString(Json.Field(r, "id"));
                        if (!string.IsNullOrEmpty(relationId)) relation.Id = relationId;
                        relation.Kind = Json.AsString(Json.Field(r, "kind")) ?? "";
                        relation.TargetId = Json.AsString(Json.Field(r, "target"));
                        relation.Name = Json.AsString(Json.Field(r, "name")) ?? "";
                        item.Relations.Add(relation);
                    }
            }

            if (item.Kind == ItemKind.Media)
            {
                item.MediaExtension = Json.AsString(Json.Field(obj, "mediaExt"));
                var media = archive.GetEntry("research/" + item.Id + (item.MediaExtension ?? ""));
                if (media != null)
                    try
                    {
                        using (var mediaStream = media.Open())
                        using (var buffer = new MemoryStream())
                        {
                            mediaStream.CopyTo(buffer);
                            item.MediaBytes = buffer.ToArray();
                        }
                    }
                    catch (Exception error)
                    {
                        item.LoadDamaged = true;
                        Warn(warnings, "Média « " + item.Title + " » illisible ("
                            + error.Message + ") : carte conservée, contenu absent.");
                    }
            }

            var children = Json.AsList(Json.Field(obj, "children"));
            if (children != null)
                foreach (var child in children)
                {
                    var childItem = ReadNode(child, archive, warnings);
                    if (childItem != null) item.Children.Add(childItem);
                }
            return item;
        }

        /// <summary>Reads one item's text entry. An unreadable entry (truncated
        /// JSON, corrupt deflate…) must never fail the whole project: the item
        /// opens as an empty document, title preserved, flagged LoadDamaged.</summary>
        private static TextDocument ReadDocument(BinderItem item, ZipArchive archive,
            List<string> warnings)
        {
            try
            {
                var jsonEntry = archive.GetEntry("texts/" + item.Id + ".json");
                if (jsonEntry != null)
                    return ParseDocument(ReadEntry(jsonEntry));

                // v1 fallback: plain text entry.
                var txtEntry = archive.GetEntry("texts/" + item.Id + ".txt");
                if (txtEntry != null)
                    return TextDocument.FromPlainText(ReadEntry(txtEntry));
            }
            catch (Exception error)
            {
                item.LoadDamaged = true;
                Warn(warnings, "Texte de « " + item.Title + " » illisible ("
                    + error.Message + ") : document ouvert vide. "
                    + "N'enregistrez pas si vous espérez récupérer ce texte "
                    + "d'une copie de secours.");
            }
            return new TextDocument { Paragraphs = { new TextParagraph() } };
        }

        private static TextDocument ParseDocument(string json)
        {
            var document = new TextDocument();
            var root = Json.Parse(json);
            var paragraphs = Json.AsList(Json.Field(root, "paragraphs"));
            if (paragraphs != null)
                foreach (var entry in paragraphs)
                {
                    var p = Json.AsObject(entry);
                    if (p == null) continue;
                    var paragraph = new TextParagraph();
                    paragraph.StyleId = Json.AsString(Json.Field(p, "style")) ?? "body";
                    paragraph.AlignOverride = Json.AsString(Json.Field(p, "align"));
                    paragraph.ListKind = Json.AsString(Json.Field(p, "list"));
                    paragraph.PageBreakBefore = Json.AsBool(Json.Field(p, "pb"), false);
                    paragraph.AllowWidows = Json.AsBool(Json.Field(p, "wo"), false);
                    var runs = Json.AsList(Json.Field(p, "runs"));
                    if (runs != null)
                        foreach (var runEntry in runs)
                        {
                            var r = Json.AsObject(runEntry);
                            if (r == null) continue;
                            var run = new TextRun();
                            if (Json.AsBool(Json.Field(r, "br"), false))
                            {
                                run.IsLineBreak = true;
                            }
                            else if (Json.Field(r, "fn") != null)
                            {
                                run.FootnoteId = Json.AsString(Json.Field(r, "fn"));
                            }
                            else if (Json.Field(r, "img") != null)
                            {
                                run.ImageId = Json.AsString(Json.Field(r, "img"));
                            }
                            else if (Json.AsBool(Json.Field(r, "hr"), false))
                            {
                                run.IsRule = true;
                            }
                            else
                            {
                                run.Text = Json.AsString(Json.Field(r, "t")) ?? "";
                                run.Bold = OptBool(r, "b");
                                run.Italic = OptBool(r, "i");
                                run.Underline = OptBool(r, "u");
                                run.Strike = OptBool(r, "st");
                                run.Weight = Json.AsString(Json.Field(r, "w"));
                                var tracking = Json.Field(r, "trk");
                                if (tracking is double) run.Tracking = (double)tracking;
                                run.FontFamily = Json.AsString(Json.Field(r, "font"));
                                var size = Json.Field(r, "size");
                                if (size is double) run.FontSize = (double)size;
                                run.Color = Json.AsString(Json.Field(r, "color"));
                                run.Highlight = Json.AsString(Json.Field(r, "hl"));
                                run.AnnotationId = Json.AsString(Json.Field(r, "ann"));
                                run.NoProof = Json.AsBool(Json.Field(r, "np"), false);
                            }
                            paragraph.Runs.Add(run);
                        }
                    document.Paragraphs.Add(paragraph);
                }
            if (document.Paragraphs.Count == 0)
                document.Paragraphs.Add(new TextParagraph());

            var notes = Json.AsList(Json.Field(root, "footnotes"));
            if (notes != null)
                foreach (var entry in notes)
                {
                    var n = Json.AsObject(entry);
                    if (n == null) continue;
                    var note = new Footnote();
                    var noteId = Json.AsString(Json.Field(n, "id"));
                    if (!string.IsNullOrEmpty(noteId)) note.Id = noteId;
                    note.Text = Json.AsString(Json.Field(n, "text")) ?? "";
                    document.Footnotes.Add(note);
                }

            var annotations = Json.AsList(Json.Field(root, "annotations"));
            if (annotations != null)
                foreach (var entry in annotations)
                {
                    var a = Json.AsObject(entry);
                    if (a == null) continue;
                    var annotation = new Annotation();
                    var annotationId = Json.AsString(Json.Field(a, "id"));
                    if (!string.IsNullOrEmpty(annotationId)) annotation.Id = annotationId;
                    annotation.Text = Json.AsString(Json.Field(a, "text")) ?? "";
                    annotation.Created = Json.AsString(Json.Field(a, "created")) ?? "";
                    annotation.Resolved = Json.AsBool(Json.Field(a, "resolved"), false);
                    document.Annotations.Add(annotation);
                }
            return document;
        }

        private static bool? OptBool(Dictionary<string, object> obj, string name)
        {
            object v;
            if (obj.TryGetValue(name, out v) && v is bool) return (bool)v;
            return null;
        }

        private static void EnsureCategory(Project project, string title, string key)
        {
            EnsureCategory(project, title, key, null);
        }

        /// <summary>beforeKey : la racine devant laquelle s'insérer (si elle
        /// existe), sinon avant la Corbeille.</summary>
        private static void EnsureCategory(Project project, string title, string key, string beforeKey)
        {
            if (project.Category(key) != null) return;
            var category = new BinderItem();
            category.Kind = ItemKind.Category;
            category.Title = title;
            category.CategoryKey = key;
            category.Id = key;
            var before = beforeKey == null ? null : project.Category(beforeKey);
            if (before == null) before = project.Trash;
            // Keep the canonical order: insert trash last, others before it.
            if (key == Project.KeyTrash || before == null)
                project.Roots.Add(category);
            else
                project.Roots.Insert(project.Roots.IndexOf(before), category);
        }
    }
}

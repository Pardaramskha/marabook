using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Persistence;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C38 — la revue du 22/09 : ce que les correctifs garantissent.
    /// Styles globaux (réglages vierges, sentinelle de césure, style encore
    /// employé, lecture seule), settings.json (bascule, repli .bak, mise de
    /// côté), .plot (images non purgées, cartes dans le secours), export
    /// aplati (interligne), EPUB (police en attribut), instantanés (interligne).</summary>
    public static class ReviewTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C38 — revue du 22/09 : styles globaux, réglages, .plot, exports");
            GlobalStylesSeed(t);
            GlobalStylesSentinel(t);
            GlobalStylesInUse(t);
            SettingsFile(t);
            PlotImagesAndMaps(t);
            FlattenKeepsLineSpacing(t);
            EpubFontAttribute(t);
            SnapshotLineSpacing(t);
        }

        /// <summary>Présence stricte d'un style (Find retombe sur « Corps »).</summary>
        private static bool Has(StyleSheet sheet, string id)
        {
            foreach (var style in sheet.Styles) if (style.Id == id) return true;
            return false;
        }

        private static void WithGlobalStyles(Action body)
        {
            var savedSheet = AppSettings.GlobalStyles;
            var savedStamp = AppSettings.GlobalStylesStamp;
            var savedPersist = GlobalStyles.Persist;
            GlobalStyles.Persist = false;
            try { body(); }
            finally
            {
                AppSettings.GlobalStyles = savedSheet;
                AppSettings.GlobalStylesStamp = savedStamp;
                GlobalStyles.Persist = savedPersist;
            }
        }

        /// <summary>Réglages vierges (nouveau poste) + projet déjà estampillé :
        /// le projet sème, ses styles personnalisés et créés survivent.</summary>
        private static void GlobalStylesSeed(Harness t)
        {
            WithGlobalStyles(delegate
            {
                AppSettings.GlobalStyles = null;
                AppSettings.GlobalStylesStamp = "";
                var project = Project.CreateNew();
                project.Styles.Find("body").FontFamily = "Garamond";
                project.Styles.Styles.Add(new ParagraphStyle { Id = "dialogue", Name = "Dialogue", FontFamily = "Garamond", FontSize = 11 });
                project.GlobalStylesStamp = "20260901000000000-abcdef"; // synchronisé sur l'autre poste
                GlobalStyles.Sync(project);
                t.Check(project.Styles.Find("body").FontFamily == "Garamond", "réglages vierges : le Corps personnalisé du projet n'est pas remis au défaut");
                t.Check(Has(project.Styles, "dialogue"), "…ni son style créé supprimé");
                t.Check(AppSettings.GlobalStyles.Find("body").FontFamily == "Garamond" && Has(AppSettings.GlobalStyles, "dialogue"),
                    "…et les réglages du poste reçoivent ses styles");
                t.Check(project.GlobalStylesStamp == AppSettings.GlobalStylesStamp, "…empreintes alignées");

                // Lecture seule : rien ne bouge, dans aucun sens.
                var newer = Project.CreateNew();
                newer.ReadOnlyNewerFormat = true;
                newer.Styles.Find("body").FontFamily = "Futura";
                var stamp = AppSettings.GlobalStylesStamp;
                t.Check(!GlobalStyles.Sync(newer) && AppSettings.GlobalStyles.Find("body").FontFamily == "Garamond" && stamp == AppSettings.GlobalStylesStamp,
                    "un projet en lecture seule (format plus récent) ne pousse ni ne tire");
            });
        }

        /// <summary>Un vieux projet aux styles par défaut, sauf la sentinelle de
        /// césure (2), ne passe pas pour personnalisé : il ne renverse pas les
        /// réglages.</summary>
        private static void GlobalStylesSentinel(Harness t)
        {
            WithGlobalStyles(delegate
            {
                AppSettings.GlobalStyles = StyleSheet.CreateDefault();
                AppSettings.GlobalStyles.Find("body").FontFamily = "Garamond";
                AppSettings.GlobalStylesStamp = "20260901000000000-aaaaaa";
                var old = Project.CreateNew();
                foreach (var style in old.Styles.Styles) style.HyphenMinAfter = 2; // relu d'un .plot d'avant le batch 24
                var changed = GlobalStyles.Sync(old);
                t.Check(AppSettings.GlobalStyles.Find("body").FontFamily == "Garamond", "la sentinelle de césure ne fait pas passer le Corps pour personnalisé : les réglages gardent Garamond");
                t.Check(changed && old.Styles.Find("body").FontFamily == "Garamond", "…et le vieux projet tire Garamond");
                t.Check(AppSettings.GlobalStylesStamp == "20260901000000000-aaaaaa", "…sans nouvelle empreinte (rien n'a été poussé)");
            });
        }

        /// <summary>Un style global disparu des réglages mais encore employé
        /// dans le projet y reste.</summary>
        private static void GlobalStylesInUse(Harness t)
        {
            WithGlobalStyles(delegate
            {
                AppSettings.GlobalStyles = StyleSheet.CreateDefault();
                AppSettings.GlobalStylesStamp = "20260901000000000-bbbbbb";
                var project = Project.CreateNew();
                project.Styles.Styles.Add(new ParagraphStyle { Id = "dialogue", Name = "Dialogue" });
                project.Styles.Styles.Add(new ParagraphStyle { Id = "encart", Name = "Encart" });
                var text = project.Category(Project.KeyWritings).Children[0];
                text.Document = TextDocument.FromPlainText("Une réplique.");
                text.Document.Paragraphs[0].StyleId = "dialogue";
                project.GlobalStylesStamp = "20260801000000000-cccccc"; // périmée : le projet tire
                GlobalStyles.Sync(project);
                t.Check(Has(project.Styles, "dialogue"), "un style global retiré des réglages mais employé par un paragraphe reste au projet");
                t.Check(!Has(project.Styles, "encart"), "…celui que rien n'emploie s'en va");
                t.Check(GlobalStyles.InUse(project, "dialogue") && !GlobalStyles.InUse(project, "encart"), "InUse regarde les paragraphes");
            });
        }

        /// <summary>settings.json : bascule par fichier temporaire, .bak,
        /// repli sur le .bak quand le fichier est tronqué, mise de côté quand
        /// tout est illisible.</summary>
        private static void SettingsFile(Harness t)
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-c38-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.json");
            var savedZoom = AppSettings.Zoom;
            var savedNotice = AppSettings.LoadNotice;
            AppSettings.PathOverride = path;
            try
            {
                AppSettings.Zoom = 137;
                AppSettings.Save();
                t.Check(File.Exists(path) && !File.Exists(path + ".tmp"), "Save : le fichier est écrit, le temporaire a disparu");
                AppSettings.Zoom = 142;
                AppSettings.Save();
                t.Check(File.Exists(path + ".bak") && File.ReadAllText(path + ".bak").Contains("137"), "la seconde sauvegarde garde la précédente en .bak");

                File.WriteAllText(path, "{\"zoom\": 14", Encoding.UTF8); // écriture coupée
                AppSettings.Zoom = 100;
                AppSettings.Load();
                t.Check(Math.Abs(AppSettings.Zoom - 137) < 0.01, "fichier tronqué : les réglages reviennent du .bak — zoom " + AppSettings.Zoom);
                t.Check(AppSettings.LoadNotice.Contains("copie de secours"), "…et le lancement le dira");

                File.WriteAllText(path, "{\"zoom\": 14", Encoding.UTF8);
                File.WriteAllText(path + ".bak", "pas du json", Encoding.UTF8);
                AppSettings.Zoom = 100;
                AppSettings.Load();
                var aside = Directory.GetFiles(dir, "settings.json.corrompu-*");
                t.Check(!File.Exists(path) && aside.Length == 1, "tout illisible : le fichier est mis de côté (.corrompu-date), pas écrasé");
                t.Check(AppSettings.LoadNotice.Contains("mis de côté") && Math.Abs(AppSettings.Zoom - 100) < 0.01, "…les défauts s'appliquent et le lancement le dira");
                AppSettings.Save();
                t.Check(File.Exists(path), "…la sauvegarde suivante recrée un fichier sain");
            }
            finally
            {
                AppSettings.PathOverride = null;
                AppSettings.Zoom = savedZoom;
                AppSettings.LoadNotice = savedNotice;
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static readonly byte[] Pixel = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        /// <summary>Une image que plus rien ne cite n'est pas écrite mais reste en
        /// mémoire (Ctrl+Z la retrouve) ; le secours textes seuls garde les cartes.</summary>
        private static void PlotImagesAndMaps(Harness t)
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-c38-plot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var project = Project.CreateNew();
                var orphan = project.AddImage(Pixel, ".png");
                var used = project.AddImage(Pixel, ".png");
                var text = project.Category(Project.KeyWritings).Children[0];
                text.Document = TextDocument.FromPlainText("Voici une image :");
                text.Document.Paragraphs[0].Runs.Add(new TextRun { ImageId = used });
                var map = new BinderItem { Kind = ItemKind.MindMap, Title = "Carte", MapBytes = MindMapTests.SampleTea(2, 1, 0) };
                project.Category(Project.KeyMindMaps).Children.Add(map);

                var path = Path.Combine(dir, "p.plot");
                PlotFile.Save(project, path);
                t.Check(project.Images.ContainsKey(orphan), "l'image orpheline reste en mémoire après la sauvegarde (Ctrl+Z possible)");
                var back = PlotFile.Load(path, new List<string>());
                t.Check(!back.Images.ContainsKey(orphan) && back.Images.ContainsKey(used), "…mais seule l'image citée est dans le .plot");

                var recovery = Path.Combine(dir, "secours.plot");
                PlotFile.Save(project, recovery, true);
                var recovered = PlotFile.Load(recovery, new List<string>());
                var recoveredMap = recovered.Category(Project.KeyMindMaps).Children[0];
                t.Check(recoveredMap.MapBytes != null && recoveredMap.MapBytes.Length == map.MapBytes.Length, "le secours textes seuls garde les cartes mentales");
                t.Check(recovered.Images.Count == 0, "…et toujours pas les images");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static void FlattenKeepsLineSpacing(Harness t)
        {
            var document = TextDocument.FromPlainText("Un item\nUn autre\nAprès la liste.");
            document.LineSpacing = 2;
            document.Paragraphs[0].ListKind = "bullet";
            document.Paragraphs[1].ListKind = "bullet";
            var flat = Compiler.FlattenLists(document);
            t.Check(!ReferenceEquals(flat, document) && Math.Abs(flat.LineSpacing - 2) < 0.001, "FlattenLists (export ODT) garde l'interligne du document");
            var withRule = TextDocument.FromPlainText("Avant\n\nAprès");
            withRule.LineSpacing = 1.5;
            withRule.Paragraphs[1].Runs.Clear();
            withRule.Paragraphs[1].Runs.Add(new TextRun { IsRule = true });
            var flatRules = Compiler.FlattenRules(withRule);
            t.Check(!ReferenceEquals(flatRules, withRule) && Math.Abs(flatRules.LineSpacing - 1.5) < 0.001, "FlattenRules (export RTF) aussi");
        }

        /// <summary>Un passage en police explicite donne un chapitre XHTML bien formé.</summary>
        private static void EpubFontAttribute(Harness t)
        {
            var dir = Path.Combine(Path.GetTempPath(), "marabook-c38-epub-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var project = Project.CreateNew();
                var text = project.Category(Project.KeyWritings).Children[0];
                text.Title = "Police";
                text.Document = TextDocument.FromPlainText("Un mot en Garamond ici.");
                text.Document.Paragraphs[0].Runs[0].FontFamily = "Garamond";
                var path = Path.Combine(dir, "police.epub");
                Epub.Write(path, project, text, Epub.DefaultOptions(project, text));
                var wellFormed = true;
                var chapters = 0;
                var fontSeen = false;
                using (var zip = ZipFile.OpenRead(path))
                    foreach (var entry in zip.Entries)
                    {
                        if (!entry.FullName.Contains("chapter-") || !entry.FullName.EndsWith(".xhtml")) continue;
                        chapters++;
                        string xml;
                        using (var stream = entry.Open())
                        using (var reader = new StreamReader(stream, Encoding.UTF8)) xml = reader.ReadToEnd();
                        if (xml.Contains("font-family: 'Garamond'")) fontSeen = true;
                        try { new XmlDocument().LoadXml(xml); } catch (XmlException) { wellFormed = false; }
                    }
                t.Check(chapters == 1 && wellFormed, "EPUB : le chapitre reste du XML bien formé avec une police sur un passage");
                t.Check(fontSeen, "…la police est en apostrophes dans l'attribut style");
                t.Check(!File.Exists(path + ".tmp"), "…pas de fichier temporaire laissé");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        private static void SnapshotLineSpacing(Harness t)
        {
            var a = TextDocument.FromPlainText("Même texte.");
            var b = TextDocument.FromPlainText("Même texte.");
            b.LineSpacing = 1.5;
            t.Check(SnapshotStore.DocumentFingerprint(a) != SnapshotStore.DocumentFingerprint(b), "l'interligne entre dans l'empreinte d'un document (un instantané naît quand il change)");
            var target = TextDocument.FromPlainText("Cible.");
            History.RestoreSnapshotAction.Replace(target, b);
            t.Check(Math.Abs(target.LineSpacing - 1.5) < 0.001, "restaurer une version rend aussi son interligne");
        }
    }
}

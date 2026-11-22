using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>La sonde de l'ATELIER (2.0.0, lot 29, 21/11/2026) : la page dans
    /// la section Outils de la Pile, PDFaah et ses composants, le contrôle
    /// d'impression et le contrôle PDF/X sur un fichier choisi — puis, si
    /// l'archive de PDFium est fournie (MARABOOK_PDFIUM_NUPKG : la sonde ne
    /// télécharge jamais rien), le vrai moteur : Chop-Chop (ouvrir, vignettes,
    /// vis-à-vis, retirer, déplacer, ajouter, écrire) et l'Unlocker sur des PDF
    /// verrouillés fabriqués ici. MARABOOK_PROBE_ONLY=atelier.</summary>
    public static partial class Probes
    {
        private static async Task ProbeAtelier(MainWindow shell)
        {
            Console.WriteLine("-- lot 29 : l'Atelier, PDFaah (2.0.0, 21/11)");
            var folder = Path.Combine(Path.GetTempPath(), "marabook-sonde-atelier-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var pdfiumBefore = Pdfium.FolderOverride;
            var pigBefore = PdfPigBridge.FolderOverride;
            Pdfium.FolderOverride = Path.Combine(folder, "pdfium");
            PdfPigBridge.FolderOverride = Path.Combine(folder, "pdfpig");
            var atelier = shell.AtelierForProbe;
            try
            {
                // ------------------------------------------------ la page, dans la Pile
                if (BinderView.CollapsedSections.Contains("Outils")) shell.Binder.ToggleSectionForProbe("Outils"); // lot 30 : la section est repliée au lancement
                var rows = shell.Binder.TopRowsForProbe();
                Check(rows.IndexOf("Atelier") == rows.IndexOf("Outils") + 1, "la section Outils de la Pile a son premier élément : l'Atelier");
                shell.ShowAtelier();
                await Settle();
                await Settle();
                Check(atelier.IsVisible && shell.CurrentForProbe != null && shell.CurrentForProbe.Id == MainWindow.AtelierItemId, "un clic l'ouvre au centre, comme toute page");
                atelier.ShowForProbe(AtelierView.BoxPdfaah); // lot 30 : l'accueil de l'Atelier montre ses boîtes ; PDFaah en est une
                await Settle();
                Check(atelier.Tool == AtelierView.BoxPdfaah, "…puis sur l'accueil de PDFaah");
                var tiles = new List<string>();
                var states = new Dictionary<string, string>();
                foreach (var visual in atelier.GetVisualDescendants())
                {
                    var border = visual as Border;
                    var tag = border == null ? null : border.Tag as string;
                    if (tag != null && tag.StartsWith("atelier-tool:")) tiles.Add(tag.Substring(13));
                    var text = visual as TextBlock;
                    var textTag = text == null ? null : text.Tag as string;
                    if (textTag != null && textTag.StartsWith("atelier-component-state:")) states[textTag.Substring(24)] = text.Text ?? "";
                }
                Check(tiles.Count == 13 && tiles.Contains(AtelierView.ToolChop) && tiles.Contains(AtelierView.ToolPrint) && tiles.Contains(AtelierView.ToolPdfX) && tiles.Contains(AtelierView.ToolUnlocker), "PDFaah : ses outils en tuiles — dont Chop-Chop, contrôle d'impression, contrôle PDF/X, Unlocker (" + tiles.Count + ")");
                Check(states.Count == 2 && states.ContainsKey("pdfpig") && states.ContainsKey("pdfium") && states["pdfium"] == "Non installé" && states["pdfpig"] == "Non installé", "…et deux composants téléchargeables, PdfPig et PDFium, chacun avec son état (ici : non installés)");
                Shot(shell, "atelier-1-accueil.png");

                // ------------------------------------------------ des fichiers d'essai
                var six = Path.Combine(folder, "six-pages.pdf");
                File.WriteAllBytes(six, ProbePdf(6, null, false));
                var two = Path.Combine(folder, "deux-pages.pdf");
                File.WriteAllBytes(two, ProbePdf(2, null, false));
                var restricted = Path.Combine(folder, "restreint.pdf");
                File.WriteAllBytes(restricted, ProbePdf(3, "", true));
                var secret = Path.Combine(folder, "secret.pdf");
                File.WriteAllBytes(secret, ProbePdf(2, "sesame", true));

                // ------------------------------------------------ le contrôle d'impression
                atelier.Choose(AtelierView.ToolPrint);
                await Settle();
                Check(atelier.Tool == AtelierView.ToolPrint, "un clic sur une tuile ouvre l'outil");
                await atelier.RunPrintCheck(six);
                await Settle();
                var print = atelier.LastPrintCheck;
                Check(print != null && print.Readable && print.Pages == 6 && print.Lines.Count >= 4, "le contrôle d'impression lit le fichier choisi : 6 pages, un constat par vérification cochée (" + (print == null ? "-" : print.Summary()) + ")");
                var hasBleed = false;
                var hasFonts = false;
                if (print != null) foreach (var line in print.Lines) { if (line.Check == Print.PdfPrintCheck.CheckBleed && line.Level == Print.PrintCheckLine.Problem) hasBleed = true; if (line.Check == Print.PdfPrintCheck.CheckFonts) hasFonts = true; }
                Check(hasBleed && hasFonts, "…ce PDF de lecture n'a pas de zone de coupe : c'est dit ; les polices sont regardées");
                Shot(shell, "atelier-2-controle-impression.png");
                foreach (var box in atelier.GetVisualDescendants().OfType<CheckBox>())
                    if ("atelier-print-bleed".Equals(box.Tag) || "atelier-print-fonts".Equals(box.Tag) || "atelier-print-consistency".Equals(box.Tag)) box.IsChecked = false;
                await atelier.RunPrintCheck(six);
                var onlySize = true;
                foreach (var line in atelier.LastPrintCheck.Lines) if (line.Check != Print.PdfPrintCheck.CheckSize) onlySize = false;
                Check(onlySize && atelier.LastPrintCheck.Lines.Count >= 1, "on ne vérifie que ce qui est coché : la taille seule");
                await atelier.RunPrintCheck(restricted);
                Check(atelier.LastPrintCheck.Locked && !atelier.LastPrintCheck.Readable, "un PDF verrouillé ne se contrôle pas : le rapport renvoie à l'Unlocker");

                shell.NavigateBackForProbe();
                await Settle();
                Check(atelier.Tool == AtelierView.BoxPdfaah && atelier.IsVisible, "« Précédent » ramène à l'accueil de PDFaah, sans quitter l'Atelier");

                // ------------------------------------------------ le contrôle PDF/X
                atelier.Choose(AtelierView.ToolPdfX);
                await Settle();
                await atelier.RunPdfX(six);
                await Settle();
                Check(atelier.LastPdfX != null && !atelier.LastPdfX.Conforms && atelier.LastPdfX.Problems.Count > 0 && atelier.LastPdfX.Pages == 6, "le contrôle PDF/X se passe sur le fichier choisi : ce PDF ordinaire n'est pas un PDF/X-1a, et le rapport dit pourquoi (" + (atelier.LastPdfX == null ? "-" : atelier.LastPdfX.Problems.Count + " écarts") + ")");
                Shot(shell, "atelier-3-controle-pdfx.png");
                atelier.ShowForProbe(AtelierView.BoxPdfaah);

                // ------------------------------------------------ le vrai moteur
                var nupkg = Environment.GetEnvironmentVariable("MARABOOK_PDFIUM_NUPKG");
                if (string.IsNullOrEmpty(nupkg) || !File.Exists(nupkg))
                {
                    Console.WriteLine("  archive PDFium absente (MARABOOK_PDFIUM_NUPKG) : Chop-Chop et l'Unlocker ne sont pas joués.");
                    return;
                }
                Check(!Pdfium.Installed && Pdfium.Supported, "PDFium n'est pas installé (dossier d'essai) ; il est prévu pour ce système (" + Pdfium.Rid + ")");
                // MARABOOK_PDFIUM_DOWNLOAD=1 : le vrai téléchargement depuis NuGet (jamais dans la sonde ordinaire).
                var problem = Environment.GetEnvironmentVariable("MARABOOK_PDFIUM_DOWNLOAD") == "1" ? await Task.Run(async delegate { return await Pdfium.DownloadAndInstall(); }) : Pdfium.InstallFromFile(nupkg);
                if (Environment.GetEnvironmentVariable("MARABOOK_PDFIUM_DOWNLOAD") == "1") Console.WriteLine("  [atelier] PDFium téléchargé depuis NuGet : " + (problem ?? "installé"));
                Check(problem == null && Pdfium.Installed, "PDFium s'installe depuis l'archive de NuGet, empreintes vérifiées (" + (problem ?? "installé") + ")");
                Check(Directory.GetFiles(Pdfium.Folder).Length == 1, "…un seul fichier écrit : la bibliothèque de ce système");
                atelier.RefreshComponentsForProbe();
                await Settle();
                var state = atelier.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(delegate(TextBlock block) { return "atelier-component-state:pdfium".Equals(block.Tag); });
                Check(state != null && state.Text == "Installé", "…et l'accueil de PDFaah le dit installé, avec « Désinstaller »");

                await ProbeChopChop(shell, atelier, folder, six, two);
                await ProbeUnlocker(shell, atelier, folder, six, restricted, secret);

                // Retirer : les fichiers rendus, la bibliothèque déchargée, le dossier vidé.
                atelier.CloseFilesForProbe();
                Check(Pdfium.Remove() && !Pdfium.Installed && !Directory.Exists(Pdfium.Folder), "« Désinstaller » rend la bibliothèque au système et retire son dossier");
            }
            finally
            {
                atelier.CloseFilesForProbe();
                atelier.ShowForProbe(AtelierView.ToolHome);
                Pdfium.Remove();
                Pdfium.FolderOverride = pdfiumBefore;
                PdfPigBridge.FolderOverride = pigBefore;
                var home = shell.Project.Category(Project.KeyWritings);
                shell.Binder.SelectItem(home.Id);
                await Settle();
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        private static async Task ProbeChopChop(MainWindow shell, AtelierView atelier, string folder, string six, string two)
        {
            atelier.Choose(AtelierView.ToolChop);
            await Settle();
            var chop = atelier.ChopForProbe;
            Check(chop.IsVisible && chop.PageCountForProbe == 0, "Chop-Chop s'ouvre vide, avec son mode d'emploi");
            Check(await chop.Open(six) && chop.PageCountForProbe == 6 && chop.OrderForProbe() == "A1 A2 A3 A4 A5 A6", "un PDF ouvert : ses six pages, dans l'ordre");
            for (var i = 0; i < 60 && chop.PendingForProbe > 0; i++) await Settle();
            Check(chop.ThumbCountForProbe == 6 && chop.CellCountForProbe == 6 && chop.ModeForProbe == ChopChopView.ModeGrid, "PDFium dessine les six vignettes (vue Aperçu)");
            Check(chop.StatusForProbe.Contains("6 pages") && chop.StatusForProbe.Contains("tel que le fichier"), "l'état dit : « " + chop.StatusForProbe + " »");
            Shot(shell, "atelier-4-chopchop-apercu.png");

            chop.ChooseMode(ChopChopView.ModeSpreads);
            await Settle();
            Check(chop.ModeForProbe == ChopChopView.ModeSpreads && chop.SpreadCountForProbe == 4 && chop.CellCountForProbe == 6, "vis-à-vis : quatre doubles pages pour six pages — la première seule à droite, la dernière seule à gauche");
            Shot(shell, "atelier-5-chopchop-vis-a-vis.png");
            chop.ChooseMode(ChopChopView.ModeList);
            await Settle();
            Check(chop.ModeForProbe == ChopChopView.ModeList && chop.CellCountForProbe == 6, "liste : une ligne par page");
            shell.NavigateBackForProbe();
            await Settle();
            Check(chop.ModeForProbe == ChopChopView.ModeSpreads && atelier.Tool == AtelierView.ToolChop, "« Précédent » rend la vue d'avant (vis-à-vis), sans quitter Chop-Chop");
            chop.ChooseMode(ChopChopView.ModeGrid);
            await Settle();

            chop.SelectForProbe(1, 3);
            chop.RemoveSelected();
            await Settle();
            Check(chop.OrderForProbe() == "A1 A3 A5 A6" && chop.StatusForProbe.Contains("modifié"), "retirer les pages 2 et 4 : " + chop.OrderForProbe() + " — « modifié, pas encore enregistré »");
            chop.SelectForProbe(3);
            chop.Nudge(-1);
            await Settle();
            Check(chop.OrderForProbe() == "A1 A3 A6 A5", "« Avancer » la dernière page d'un cran : " + chop.OrderForProbe());
            chop.SelectForProbe(2, 3);
            chop.MoveSelected(0);
            await Settle();
            Check(chop.OrderForProbe() == "A6 A5 A1 A3", "glisser deux pages tout au début : " + chop.OrderForProbe());
            chop.SelectForProbe(1);
            Check(await chop.Add(two) && chop.OrderForProbe() == "A6 A5 B1 B2 A1 A3" && chop.SourceCountForProbe == 2, "ajouter un autre PDF après la page choisie : " + chop.OrderForProbe());
            for (var i = 0; i < 60 && chop.PendingForProbe > 0; i++) await Settle();
            Shot(shell, "atelier-6-chopchop-recompose.png");
            chop.SelectForProbe(0, 1, 2, 3, 4, 5);
            chop.RemoveSelected();
            Check(chop.PageCountForProbe == 6 && chop.StatusForProbe.Contains("au moins une page"), "tout retirer est refusé : un PDF garde au moins une page");

            var output = Path.Combine(folder, "recompose.pdf");
            Check(await chop.Save(output) && File.Exists(output), "« Enregistrer sous… » écrit le PDF recomposé");
            var sourceBefore = File.ReadAllBytes(six).Length;
            int error;
            using (var written = Pdfium.Open(output, null, out error))
            {
                var sizes = written == null ? new List<double[]>() : written.Sizes();
                // Les pages d'essai ont chacune leur largeur : 300 + 10 × rang pour le premier fichier, 500 + 10 × rang pour le second.
                var widths = "";
                foreach (var size in sizes) widths += Math.Round(size[0]) + " ";
                Check(written != null && written.PageCount == 6 && widths.Trim() == "360 350 510 520 310 330", "…relu par PDFium : six pages, dans l'ordre composé (largeurs " + widths.Trim() + ")");
            }
            Check(File.ReadAllBytes(six).Length == sourceBefore, "…le fichier ouvert n'a pas changé d'un octet");
            Check(!await ChopSaveOver(chop, six), "écrire par-dessus un des fichiers ouverts est refusé");
            chop.Reset();
            atelier.ShowForProbe(AtelierView.BoxPdfaah);
        }

        /// <summary>Tente d'écrire par-dessus un fichier ouvert : le refus est une boîte de message, que la sonde referme.</summary>
        private static async Task<bool> ChopSaveOver(ChopChopView chop, string path)
        {
            var saving = chop.Save(path);
            for (var i = 0; i < 20 && !saving.IsCompleted; i++)
            {
                await Settle();
                var box = OpenWindow<MessageDialog>();
                if (box != null) box.Close();
            }
            return saving.IsCompleted && await saving;
        }

        private static async Task ProbeUnlocker(MainWindow shell, AtelierView atelier, string folder, string free, string restricted, string secret)
        {
            atelier.Choose(AtelierView.ToolUnlocker);
            await Settle();
            await atelier.RunUnlocker(free);
            await Settle();
            Check(atelier.LastUnlockState == "libre", "l'Unlocker devant un PDF libre : « ce PDF n'est pas verrouillé », rien à faire");

            await atelier.RunUnlocker(restricted);
            await Settle();
            Check(atelier.LastUnlockState == "verrouillé", "un PDF à restrictions (impression, copie, modification interdites) s'ouvre sans mot de passe et se dit verrouillé");
            Shot(shell, "atelier-7-unlocker.png");
            var before = File.ReadAllBytes(restricted);
            var opened = Path.Combine(folder, "restreint (déverrouillé).pdf");
            Check(await atelier.SaveUnlocked(restricted, opened) && atelier.LastUnlockState == "déverrouillé", "« Enregistrer une copie déverrouillée » l'écrit");
            int error;
            using (var copy = Pdfium.Open(opened, null, out error))
                Check(copy != null && !copy.Encrypted && copy.PageCount == 3 && copy.CanPrint && copy.CanCopy && copy.CanModify, "…la copie se rouvre sans verrou : 3 pages, impression, copie et modification permises");
            var after = File.ReadAllBytes(restricted);
            Check(after.Length == before.Length && Encoding.ASCII.GetString(after).Contains("/Encrypt"), "…le fichier d'origine n'a pas bougé : toujours verrouillé");
            var report = Print.PdfPrintCheck.Inspect(File.ReadAllBytes(opened), new Print.PrintCheckOptions());
            Check(report.Readable && !report.Locked && report.Pages == 3, "…et la copie passe maintenant au contrôle d'impression");

            await atelier.RunUnlocker(secret, "pas-le-bon");
            await Settle();
            Check(atelier.LastUnlockState == "mot de passe refusé", "un PDF à mot de passe d'ouverture, avec un mauvais mot de passe : refusé — l'Unlocker ne devine rien");
            await atelier.RunUnlocker(secret, "sesame");
            await Settle();
            Check(atelier.LastUnlockState == "verrouillé", "…avec le bon mot de passe, il s'ouvre");
            var clear = Path.Combine(folder, "secret (déverrouillé).pdf");
            Check(await atelier.SaveUnlocked(secret, clear), "…et sa copie déverrouillée s'écrit");
            using (var copy = Pdfium.Open(clear, null, out error))
                Check(copy != null && !copy.Encrypted && copy.PageCount == 2, "…elle se rouvre SANS mot de passe");
            using (var still = Pdfium.Open(secret, null, out error))
                Check(still == null && error == Pdfium.ErrorPassword, "…l'original, lui, demande toujours le sien");
            atelier.CloseFilesForProbe();
            atelier.ShowForProbe(AtelierView.BoxPdfaah);
        }

        // ================================================================ des PDF d'essai

        private static readonly byte[] PdfPadding =
        {
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
        };

        private static byte[] Rc4(byte[] key, byte[] data)
        {
            var s = new byte[256];
            for (var i = 0; i < 256; i++) s[i] = (byte)i;
            var j = 0;
            for (var i = 0; i < 256; i++) { j = (j + s[i] + key[i % key.Length]) & 255; var swap = s[i]; s[i] = s[j]; s[j] = swap; }
            var output = new byte[data.Length];
            int a = 0, b = 0;
            for (var n = 0; n < data.Length; n++)
            {
                a = (a + 1) & 255;
                b = (b + s[a]) & 255;
                var swap = s[a]; s[a] = s[b]; s[b] = swap;
                output[n] = (byte)(data[n] ^ s[(s[a] + s[b]) & 255]);
            }
            return output;
        }

        private static byte[] Padded(string password)
        {
            var raw = Encoding.GetEncoding(28591).GetBytes(password ?? "");
            var padded = new byte[32];
            var length = Math.Min(32, raw.Length);
            Array.Copy(raw, padded, length);
            Array.Copy(PdfPadding, 0, padded, length, 32 - length);
            return padded;
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder("<");
            foreach (var b in bytes) sb.Append(b.ToString("X2"));
            return sb.Append(">").ToString();
        }

        /// <summary>Un PDF d'essai de « pages » pages (chacune a SA largeur, pour se reconnaître
        /// après recomposition), avec sa table de références. « encrypt » : verrouillé par le
        /// verrou standard de la norme PDF (RC4 40 bits, révision 2), impression, copie et
        /// modification interdites ; userPassword non vide = un mot de passe à l'ouverture.</summary>
        private static byte[] ProbePdf(int pages, string userPassword, bool encrypt)
        {
            var latin = Encoding.GetEncoding(28591);
            var id = new byte[16];
            for (var i = 0; i < 16; i++) id[i] = (byte)(17 * i + pages);
            const int permissions = -3904; // ni impression, ni modification, ni copie, ni annotation
            byte[] fileKey = null, ownerEntry = null, userEntry = null;
            if (encrypt)
            {
                using (var md5 = MD5.Create())
                {
                    var ownerKey = new byte[5];
                    Array.Copy(md5.ComputeHash(Padded("proprietaire")), ownerKey, 5);
                    ownerEntry = Rc4(ownerKey, Padded(userPassword));
                    var seed = new List<byte>();
                    seed.AddRange(Padded(userPassword));
                    seed.AddRange(ownerEntry);
                    seed.AddRange(BitConverter.GetBytes(permissions));
                    seed.AddRange(id);
                    fileKey = new byte[5];
                    Array.Copy(md5.ComputeHash(seed.ToArray()), fileKey, 5);
                    userEntry = Rc4(fileKey, PdfPadding);
                }
            }
            var output = new MemoryStream();
            var offsets = new List<long>();
            Action<string> write = delegate(string text) { var bytes = latin.GetBytes(text); output.Write(bytes, 0, bytes.Length); };
            write("%PDF-1.4\n%âãÏÓ\n");
            var objects = new List<byte[]>();
            var first = pages <= 2 ? 500 : 300;
            var kids = new StringBuilder();
            for (var i = 0; i < pages; i++) kids.Append(3 + 2 * i).Append(" 0 R ");
            objects.Add(latin.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(latin.GetBytes("<< /Type /Pages /Count " + pages + " /Kids [" + kids + "] >>"));
            for (var i = 0; i < pages; i++)
            {
                var width = first + 10 * (i + 1);
                var pageNumber = 3 + 2 * i;
                objects.Add(latin.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + width + " 600] /Resources << >> /Contents " + (pageNumber + 1) + " 0 R >>"));
                var content = latin.GetBytes("2 w 40 40 m " + (width - 40) + " 560 l S 40 560 m " + (width - 40) + " 40 l S 0.2 0.3 0.8 rg 60 " + (80 + 60 * i) + " " + (width - 120) + " 30 re f\n");
                if (encrypt)
                {
                    var objectNumber = pageNumber + 1;
                    using (var md5 = MD5.Create())
                    {
                        var seed = new List<byte>(fileKey);
                        seed.Add((byte)objectNumber); seed.Add((byte)(objectNumber >> 8)); seed.Add((byte)(objectNumber >> 16)); seed.Add(0); seed.Add(0);
                        var key = new byte[10];
                        Array.Copy(md5.ComputeHash(seed.ToArray()), key, 10);
                        content = Rc4(key, content);
                    }
                }
                var stream = new MemoryStream();
                var head = latin.GetBytes("<< /Length " + content.Length + " >>\nstream\n");
                stream.Write(head, 0, head.Length);
                stream.Write(content, 0, content.Length);
                var tail = latin.GetBytes("\nendstream");
                stream.Write(tail, 0, tail.Length);
                objects.Add(stream.ToArray());
            }
            var encryptNumber = 0;
            if (encrypt)
            {
                objects.Add(latin.GetBytes("<< /Filter /Standard /V 1 /R 2 /O " + Hex(ownerEntry) + " /U " + Hex(userEntry) + " /P " + permissions + " >>"));
                encryptNumber = objects.Count;
            }
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(output.Position);
                write((i + 1) + " 0 obj\n");
                output.Write(objects[i], 0, objects[i].Length);
                write("\nendobj\n");
            }
            var xref = output.Position;
            write("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n");
            foreach (var offset in offsets) write(offset.ToString("0000000000") + " 00000 n \n");
            write("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R /ID [" + Hex(id) + " " + Hex(id) + "]" + (encrypt ? " /Encrypt " + encryptNumber + " 0 R" : "") + " >>\nstartxref\n" + xref + "\n%%EOF\n");
            return output.ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Print;
using Marabook.Settings;

namespace Marabook.Tests
{
    /// <summary>C89 — 2.0.0, lot 28 (21/11) : les skins vus du cœur — les
    /// identifiants et la seconde couleur automatique, le réglage retenu,
    /// l'habillage de l'Encyclopédie en HTML (rien pour « modern », le papier
    /// d'Eden Scriptis, le sombre d'office de Cyberpunk) et celui du « PDF
    /// Encyclopédique » (le papier sous la page, l'encre traduite, les couleurs
    /// de l'auteur intactes).</summary>
    public static class SkinsTests
    {
        public static void Run(Harness t)
        {
            t.Suite("C89 — lot 28 : skins (identifiants, seconde couleur, Encyclopédie en HTML et en PDF)");
            var dir = Path.Combine(Path.GetTempPath(), "marabook-tests-c89-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var settingsBefore = AppSettings.PathOverride;
            string skinBefore = AppSettings.Skin, secondBefore = AppSettings.SkinAccent2;
            try
            {
                Identity(t);
                Colors(t);
                Settings(t, dir);
                Html(t, dir);
                Pdf(t);
            }
            finally
            {
                AppSettings.PathOverride = settingsBefore;
                AppSettings.Skin = skinBefore;
                AppSettings.SkinAccent2 = secondBefore;
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static void Identity(Harness t)
        {
            t.Equal(5, Skins.Ids.Length, "cinq skins");
            t.Check(Skins.Ids[0] == Skins.Modern && Skins.Label(Skins.Modern) == "Marabook modern" && Skins.Label(Skins.Classic) == "Marabook classic" && Skins.Label(Skins.Gradient) == "Gradient Garden"
                && Skins.Label(Skins.Eden) == "Eden Scriptis" && Skins.Label(Skins.Cyberpunk) == "Cyberpunk 2026", "leurs noms : Marabook modern (le premier), Marabook classic, Gradient Garden, Eden Scriptis, Cyberpunk 2026");
            t.Check(Skins.Normalize(null) == Skins.Modern && Skins.Normalize("") == Skins.Modern && Skins.Normalize("un-skin-de-demain") == Skins.Modern && Skins.Normalize("eden") == Skins.Eden, "un réglage absent ou inconnu vaut « modern »");
            t.Check(Skins.DarkByDefault(Skins.Cyberpunk) && !Skins.DarkByDefault(Skins.Eden) && !Skins.DarkByDefault(Skins.Modern), "seul Cyberpunk 2026 est sombre par défaut");
            foreach (var id in Skins.Ids) t.Check(Skins.Description(id).Length > 10, "« " + Skins.Label(id) + " » se décrit");
        }

        private static void Colors(Harness t)
        {
            var auto = Skins.AutoSecond("#5B67D8");
            double h, s, l, h2, s2, l2;
            Skins.ToHsl("#5B67D8", out h, out s, out l);
            Skins.ToHsl(auto, out h2, out s2, out l2);
            t.Check(Skins.IsHex(auto) && Math.Abs(((h2 - h + 360) % 360) - 55) < 3, "la seconde couleur automatique est la voisine de l'accent, 55° plus loin (" + auto + ")");
            // Un orangé (30°) : 55° plus loin tomberait dans les jaunes-verts ternes — on part de l'autre côté.
            var warm = Skins.AutoSecond("#E67E22");
            Skins.ToHsl("#E67E22", out h, out s, out l);
            Skins.ToHsl(warm, out h2, out s2, out l2);
            t.Check(!(h2 >= 55 && h2 <= 105), "…jamais un jaune-vert terne : un orangé va vers le rouge (" + warm + ", teinte " + h2.ToString("0") + "°)");
            t.Check(Skins.AutoSecond("#27AE60") != Skins.AutoSecond("#C0392B"), "chaque accent a sa voisine");
            t.Equal("#16A085", Skins.SecondFor("#5B67D8", "#16a085"), "une seconde couleur choisie est gardée (en capitales)");
            t.Equal(auto, Skins.SecondFor("#5B67D8", null), "…sinon l'automatique");
            t.Equal(auto, Skins.SecondFor("#5B67D8", "pas une couleur"), "…une valeur illisible aussi");
            t.Check(Skins.IsHex("#A1B2C3") && !Skins.IsHex("A1B2C3") && !Skins.IsHex("#A1B2C") && !Skins.IsHex("#GGGGGG") && !Skins.IsHex(null), "« #RRGGBB » et rien d'autre");
            t.Equal("#5B67D8", Skins.FromHsl(h = 234.2, 0.616, 0.602).Length == 7 ? "#5B67D8" : "", "une teinte se rend en #RRGGBB");
            Skins.ToHsl("#808080", out h, out s, out l);
            t.Check(s < 0.001 && Skins.Shade("#808080", 0.8, 0.5) == "#808080", "un gris reste gris quand on le nuance");
        }

        private static void Settings(Harness t, string dir)
        {
            AppSettings.PathOverride = Path.Combine(dir, "settings.json");
            AppSettings.Skin = Skins.Eden;
            AppSettings.SkinAccent2 = "#C2185B";
            AppSettings.Save();
            AppSettings.Skin = null;
            AppSettings.SkinAccent2 = null;
            AppSettings.Load();
            t.Check(AppSettings.Skin == Skins.Eden && AppSettings.SkinAccent2 == "#C2185B", "le skin et la seconde couleur sont retenus dans les réglages");
            AppSettings.Skin = null;
            AppSettings.SkinAccent2 = null;
            AppSettings.Save();
            var json = File.ReadAllText(AppSettings.PathOverride);
            t.Check(!json.Contains("\"skin\"") && !json.Contains("skinAccent2"), "« modern » et l'automatique n'écrivent rien : le fichier de réglages est celui d'avant");
            File.WriteAllText(AppSettings.PathOverride, json.TrimEnd().TrimEnd('}') + ", \"skin\": \"hologramme\", \"skinAccent2\": \"rose\" }");
            AppSettings.Load();
            t.Check(AppSettings.Skin == null && AppSettings.SkinAccent2 == null, "un skin inconnu (d'un Marabook de demain) et une couleur illisible retombent sur « modern » et l'automatique");
        }

        private static void Html(Harness t, string dir)
        {
            var plain = HtmlEncyclopedia.Css(new EncyclopediaOptions());
            t.Check(Skins.HtmlCss(Skins.Modern, "#5B67D8", "", false, false) == "" && Skins.HtmlCss(null, "#5B67D8", "", false, false) == "", "« modern » n'ajoute rien à la feuille de style de l'Encyclopédie");
            t.Check(HtmlEncyclopedia.Css(new EncyclopediaOptions { Skin = Skins.Modern }) == plain && !plain.Contains("@media screen{"), "…le fichier est, au caractère près, celui d'avant");
            foreach (var id in new[] { Skins.Classic, Skins.Gradient, Skins.Eden, Skins.Cyberpunk })
            {
                var css = HtmlEncyclopedia.Css(new EncyclopediaOptions { Skin = id });
                t.Check(css.StartsWith(plain.Substring(0, plain.IndexOf("@media print", StringComparison.Ordinal))) && css.Contains("@media screen{") && css.IndexOf("@media screen{", StringComparison.Ordinal) < css.IndexOf("@media print", StringComparison.Ordinal),
                    "« " + Skins.Label(id) + " » s'ajoute après la feuille de base, à l'écran seulement, avant les règles d'impression");
            }
            var classic = Skins.HtmlCss(Skins.Classic, "#5B67D8", "", false, false);
            t.Check(classic.Contains("border-radius:0!important") && classic.Contains("Tahoma") && classic.Contains("#ECE9D8"), "classic : angles droits, Tahoma, beige de 2005");
            t.Check(!Skins.HtmlCss(Skins.Classic, "#5B67D8", "", false, true).Contains("Tahoma"), "…une police choisie dans la fenêtre d'export prime sur celle du skin");
            var gradient = Skins.HtmlCss(Skins.Gradient, "#5B67D8", "#16A085", false, false);
            t.Check(gradient.Contains("--accent2:#16A085") && gradient.Contains("linear-gradient(135deg") && gradient.Contains("background-clip:text"), "Gradient Garden : la seconde couleur choisie, un fond et des titres en dégradé");
            t.Check(Skins.HtmlCss(Skins.Gradient, "#5B67D8", "", false, false).Contains("--accent2:" + Skins.AutoSecond("#5B67D8")), "…ou la seconde couleur automatique");
            var eden = Skins.HtmlCss(Skins.Eden, "#5B67D8", "", true, false);
            t.Check(eden.Contains("url(" + Skins.TextureFile + ")") && eden.Contains("Palatino") && eden.Contains("::first-letter"), "Eden Scriptis : le papier en fond, Palatino, une lettrine");
            t.Check(!Skins.HtmlCss(Skins.Eden, "#5B67D8", "", false, false).Contains("url("), "…sans texture fournie, pas d'image appelée : un aplat de papier");
            var cyber = Skins.HtmlCss(Skins.Cyberpunk, "#5B67D8", "", false, false);
            t.Check(cyber.Contains("text-shadow") && cyber.Contains("box-shadow") && cyber.Contains("#07060D"), "Cyberpunk 2026 : des halos, un fond de nuit");
            t.Check(Skins.HtmlStartsDark(Skins.Cyberpunk) && !Skins.HtmlStartsDark(Skins.Eden) && !Skins.HtmlStartsDark(null), "…et la page s'ouvre au sombre");

            // L'export, pour de vrai.
            var project = Project.CreateNew();
            project.Name = "Essai";
            var sheet = new BinderItem { Kind = ItemKind.Sheet, Title = "Keira", TemplateId = project.CharacterTemplate().Id, Document = new TextDocument() };
            sheet.Parent = project.Category(Project.KeySheets);
            project.Category(Project.KeySheets).Children.Add(sheet);
            var texture = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 0xFF, 0xD9 };
            var folder = Path.Combine(dir, "eden");
            var options = new EncyclopediaOptions { Skin = Skins.Eden, SkinTexture = texture };
            var written = HtmlEncyclopedia.Write(folder, Encyclopedia.Build(project, EncyclopediaSelection.All(), options), project);
            var paper = Path.Combine(folder, Skins.TextureFile);
            t.Check(File.Exists(paper) && File.ReadAllBytes(paper).Length == texture.Length && written.Contains(paper), "Eden Scriptis : le papier est écrit à côté de la feuille de style, et compté dans les fichiers");
            var folder2 = Path.Combine(dir, "cyber");
            HtmlEncyclopedia.Write(folder2, Encyclopedia.Build(project, EncyclopediaSelection.All(), new EncyclopediaOptions { Skin = Skins.Cyberpunk }), project);
            t.Check(File.ReadAllText(Path.Combine(folder2, "index.html")).Contains("data-theme=\"dark\"") && !File.Exists(Path.Combine(folder2, Skins.TextureFile)), "Cyberpunk : l'accueil s'ouvre au sombre sans que l'option soit cochée ; pas de papier");
            var folder3 = Path.Combine(dir, "modern");
            HtmlEncyclopedia.Write(folder3, Encyclopedia.Build(project, EncyclopediaSelection.All(), new EncyclopediaOptions()), project);
            t.Check(File.ReadAllText(Path.Combine(folder3, "index.html")).Contains("<html lang=\"fr\">") &&!File.Exists(Path.Combine(folder3, Skins.TextureFile)), "modern : ni sombre d'office, ni papier");

            // Le skin est un réglage de la MACHINE : il n'entre ni dans l'empreinte des options, ni dans le projet.
            var a = new EncyclopediaOptions();
            var b = new EncyclopediaOptions { Skin = Skins.Eden, SkinAccent2 = "#16A085", SkinTexture = texture };
            t.Check(a.Signature() == b.Signature(), "le skin n'entre pas dans l'empreinte des options de l'Encyclopédie : changer de skin ne salit pas le projet");
            project.Encyclopedia = b;
            var path = Path.Combine(dir, "skin.plot");
            Persistence.PlotFile.Save(project, path, true);
            var back = Persistence.PlotFile.Load(path);
            t.Check(back.Encyclopedia == null || (back.Encyclopedia.Skin == "" && back.Encyclopedia.SkinTexture == null), "…et le .plot ne le retient pas");
        }

        private static void Pdf(Harness t)
        {
            t.Check(Skins.PdfSkinFor(Skins.Modern, "#5B67D8", null) == null && Skins.PdfSkinFor(null, "#5B67D8", null) == null, "« modern » n'habille pas le PDF Encyclopédique");
            var eden = Skins.PdfSkinFor(Skins.Eden, "#5B67D8", null);
            t.Check(eden != null && eden.Texture && eden.Text == Ink.Parse("#3B2A1A"), "Eden Scriptis : un papier à texture, une encre sépia");
            t.Check(eden.Map(Ink.Black) == eden.Text && eden.Map(Ink.Rgb(0x20, 0x22, 0x28)) == eden.Text, "le noir et les gris foncés du texte prennent l'encre du skin");
            t.Check(eden.Map(Ink.Rgb(0x80, 0x80, 0x80)) == eden.Soft, "…les gris moyens, son encre douce");
            t.Check(eden.Map(Ink.Rgb(0xC0, 0x39, 0x2B)) == Ink.Rgb(0xC0, 0x39, 0x2B) && eden.Map(Ink.Rgb(0xFF, 0xFF, 0xFF)) == Ink.Rgb(0xFF, 0xFF, 0xFF), "…une couleur posée par l'auteur et le blanc ne bougent pas");
            var cyber = Skins.PdfSkinFor(Skins.Cyberpunk, "#5B67D8", null);
            t.Check(cyber.HasFrame && cyber.Paper == Ink.Parse("#0B0914") && cyber.Text.R > 200, "Cyberpunk : une page de nuit, une encre claire, un filet de néon");
            var gradient = Skins.PdfSkinFor(Skins.Gradient, "#5B67D8", "#16A085");
            t.Check(gradient.Banded && gradient.Paper != gradient.PaperEnd, "Gradient Garden : un papier qui glisse d'une teinte à l'autre");
            t.Check(Skins.PdfSkinFor(Skins.Classic, "#5B67D8", null).Paper == Ink.Parse("#F1EFE2"), "classic : un papier beige");

            var latin = Encoding.GetEncoding(28591);
            var plain = PdfWriter.Build(FineTypographyTests.PartsForTests("Un texte d'essai."), new PdfExportOptions { Title = "Essai" });
            var again = PdfWriter.Build(FineTypographyTests.PartsForTests("Un texte d'essai."), new PdfExportOptions { Title = "Essai", Skin = null });
            t.Equal(plain.Length, again.Length, "sans skin, le PDF est celui d'avant");
            var dark = PdfWriter.Build(FineTypographyTests.PartsForTests("Un texte d'essai."), new PdfExportOptions { Title = "Essai", Skin = cyber });
            t.Check(dark.Length > plain.Length && latin.GetString(dark).StartsWith("%PDF-"), "sous un skin, la page porte en plus son papier et son cadre (" + plain.Length + " → " + dark.Length + " octets)");
            t.Check(!latin.GetString(plain).Contains("/Subtype /Image") && !latin.GetString(plain).Contains("/Subtype/Image"), "un PDF de texte sans skin n'a aucune image");
            eden.TextureRgb = new byte[4 * 3 * 3];
            eden.TextureWidth = 4;
            eden.TextureHeight = 3;
            var aged = latin.GetString(PdfWriter.Build(FineTypographyTests.PartsForTests("Un texte d'essai."), new PdfExportOptions { Title = "Essai", Skin = eden }));
            t.Check(aged.Contains("/Subtype /Image") || aged.Contains("/Subtype/Image"), "Eden Scriptis : la texture du papier est incorporée au PDF, une seule fois");
        }
    }
}

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
                Museum(t, dir);
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
            t.Equal(9, Skins.Ids.Length, "neuf skins : les cinq d'origine et la famille « Museum » (Ascension compris)");
            t.Check(Skins.Ids[0] == Skins.Modern && Skins.Label(Skins.Modern) == "Marabook modern" && Skins.Label(Skins.Classic) == "Marabook classic" && Skins.Label(Skins.Gradient) == "Gradient Garden"
                && Skins.Label(Skins.Eden) == "Eden Scriptis" && Skins.Label(Skins.Cyberpunk) == "Cyberpunk 2026", "leurs noms : Marabook modern (le premier), Marabook classic, Gradient Garden, Eden Scriptis, Cyberpunk 2026");
            t.Check(Skins.Normalize(null) == Skins.Modern && Skins.Normalize("") == Skins.Modern && Skins.Normalize("un-skin-de-demain") == Skins.Modern && Skins.Normalize("eden") == Skins.Eden, "un réglage absent ou inconnu vaut « modern »");
            t.Check(Skins.DarkByDefault(Skins.Cyberpunk) && !Skins.DarkByDefault(Skins.Eden) && !Skins.DarkByDefault(Skins.Modern), "Cyberpunk 2026 est sombre par défaut, pas les autres skins d'origine");
            foreach (var id in Skins.Ids) t.Check(Skins.Description(id).Length > 10, "« " + Skins.Label(id) + " » se décrit");
        }

        /// <summary>La série « Museum » (24/11) : trois skins PREMIUM, leurs trois options
        /// (accent, fond, effet verre), leur habillage de l'Encyclopédie.</summary>
        private static void Museum(Harness t, string dir)
        {
            var series = new[] { Skins.MuseumFire, Skins.MuseumWood, Skins.MuseumOath, Skins.MuseumAscent };
            t.Check(Skins.Label(Skins.MuseumFire) == "Gardien du Feu" && Skins.Label(Skins.MuseumWood) == "Bois Blanc" && Skins.Label(Skins.MuseumOath) == "Assermenté", "la série « Museum » : Gardien du Feu, Bois Blanc, Assermenté");
            foreach (var id in series)
                t.Check(Skins.IsMuseum(id) && Skins.Premium(id) && Skins.Normalize(id) == id && Skins.Description(id).StartsWith("Museum"), "« " + Skins.Label(id) + " » est un skin PREMIUM de la série");
            t.Check(!Skins.IsMuseum(Skins.Modern) && !Skins.Premium(Skins.Eden) && !Skins.IsMuseum(null) && !Skins.IsMuseum("museum"), "…les skins d'origine n'en sont pas");
            t.Check(Skins.DarkByDefault(Skins.MuseumFire) && Skins.DarkByDefault(Skins.MuseumOath) && !Skins.DarkByDefault(Skins.MuseumWood), "Gardien du Feu et Assermenté sont sombres par défaut, Bois Blanc est clair");
            // « Ascension » (24/11 au soir) : la Citadelle de l'Atelier, renommée.
            t.Check(Skins.Label(Skins.MuseumAscent) == "Ascension" && Skins.DarkByDefault(Skins.MuseumAscent) && Skins.AccentFor(Skins.MuseumAscent, null) == "#C89B3C" && Skins.MuseumBackground(Skins.MuseumAscent, true) == "#010A13",
                "« Ascension » : sombre par défaut, or sur bleu nuit");
            t.Check(Skins.MuseumCaps(Skins.MuseumAscent) && !Skins.MuseumPill(Skins.MuseumAscent) && Skins.MuseumPill(Skins.MuseumFire) && Skins.MuseumPill(Skins.MuseumWood) && Skins.MuseumPill(Skins.MuseumOath) && !Skins.MuseumCaps(Skins.MuseumFire) && !Skins.MuseumPill(Skins.Modern),
                "…titres en capitales et pas de pilule de sélection ; les trois autres ont la pilule");
            // La barre à jalons d'un livre.
            t.Check(Skins.MilestonesPassed(0) == 0 && Skins.MilestonesPassed(0.24) == 0 && Skins.MilestonesPassed(0.25) == 1 && Skins.MilestonesPassed(0.72) == 2 && Skins.MilestonesPassed(0.95) == 3 && Skins.MilestonesPassed(1) == 3,
                "les jalons d'un livre : 25 % premier jet, 60 % bêta, 95 % relu");
            t.Check(Skins.BookMilestones.Length == 3 && Skins.BookMilestoneNames.Length == 3 && Skins.BookMilestonePassed.Length == 3, "…trois jalons, chacun son nom et sa mention");

            // L'accent : celui choisi, sinon celui du skin, sinon l'indigo.
            t.Equal("#E0915A", Skins.AccentFor(Skins.MuseumFire, null), "sans accent choisi, Gardien du Feu apporte son cuivre");
            t.Equal("#4F7D5B", Skins.AccentFor(Skins.MuseumWood, ""), "…Bois Blanc son vert-de-gris");
            t.Equal("#C9A45C", Skins.AccentFor(Skins.MuseumOath, "pas une couleur"), "…Assermenté son laiton");
            t.Equal("#16A085", Skins.AccentFor(Skins.MuseumFire, "#16a085"), "un accent choisi prime sur celui du skin");
            t.Check(Skins.AccentFor(Skins.Modern, null) == "#5B67D8" && Skins.MuseumAccent(Skins.Eden) == null, "…et hors de la série, l'indigo de Marabook");

            // Le fond : celui du skin, ou celui choisi, borné par le mode.
            double h, s, l;
            foreach (var id in series)
            {
                t.Equal(Skins.MuseumBackground(id, true), Skins.MuseumBackgroundFor(id, true, null), "« " + Skins.Label(id) + " » : sans choix, le fond de nuit du skin");
                t.Equal(Skins.MuseumBackground(id, false), Skins.MuseumBackgroundFor(id, false, null), "…et son fond de jour");
                Skins.ToHsl(Skins.MuseumBackground(id, true), out h, out s, out l);
                var night = l;
                Skins.ToHsl(Skins.MuseumBackground(id, false), out h, out s, out l);
                t.Check(night < 0.24 && l > 0.78, "…l'un sombre, l'autre clair");
            }
            Skins.ToHsl(Skins.MuseumBackgroundFor(Skins.MuseumFire, true, "#FFFFFF"), out h, out s, out l);
            t.Check(l <= 0.245, "un fond blanc choisi au sombre est ramené à 24 % de clarté (" + (l * 100).ToString("0") + " %)");
            Skins.ToHsl(Skins.MuseumBackgroundFor(Skins.MuseumWood, false, "#102030"), out h, out s, out l);
            t.Check(l >= 0.775, "un fond de nuit choisi au clair est ramené à 78 % (" + (l * 100).ToString("0") + " %)");
            t.Equal("#14101F", Skins.MuseumBackgroundFor(Skins.MuseumOath, true, "#14101F"), "un fond dans la plage du mode est gardé tel quel");
            t.Check(Skins.MuseumBackground(Skins.Modern, true) == null, "pas de fond « Museum » hors de la série");

            // L'effet verre.
            foreach (var id in series) t.Check(Skins.MuseumGlass(id) == 65, "« " + Skins.Label(id) + " » : 65 % d'effet verre d'origine");
            t.Check(Skins.MuseumGlassFor(Skins.MuseumFire, -1) == 65 && Skins.MuseumGlassFor(Skins.MuseumFire, 0) == 0 && Skins.MuseumGlassFor(Skins.MuseumFire, 100) == 100 && Skins.MuseumGlassFor(Skins.MuseumFire, 250) == 65,
                "l'effet verre choisi (0 à 100) prime ; hors plage, celui du skin");

            // Le flou derrière le verre.
            foreach (var id in series) t.Check(Skins.MuseumBlur(id) == 15, "« " + Skins.Label(id) + " » : 15 px de flou d'origine derrière le verre");
            t.Check(Skins.MuseumBlurFor(Skins.MuseumFire, -1) == 15 && Skins.MuseumBlurFor(Skins.MuseumFire, 0) == 0 && Skins.MuseumBlurFor(Skins.MuseumFire, 24) == 24 && Skins.MuseumBlurFor(Skins.MuseumFire, 90) == 15,
                "le flou choisi (0 à 24 px) prime ; hors plage, celui du skin");
            var blurBefore = AppSettings.SkinBlur;
            try
            {
                AppSettings.PathOverride = Path.Combine(dir, "settings-flou.json");
                AppSettings.SkinBlur = 14;
                AppSettings.Save();
                AppSettings.SkinBlur = -1;
                AppSettings.Load();
                t.Check(AppSettings.SkinBlur == 14, "le flou choisi est retenu dans les réglages");
                AppSettings.SkinBlur = -1;
                AppSettings.Save();
                var flou = File.ReadAllText(AppSettings.PathOverride);
                t.Check(!flou.Contains("skinBlur"), "…celui du skin n'écrit rien");
                File.WriteAllText(AppSettings.PathOverride, flou.TrimEnd().TrimEnd('}') + ", \"skinBlur\": 300 }");
                AppSettings.Load();
                t.Check(AppSettings.SkinBlur == -1, "…un flou hors plage retombe sur celui du skin");
            }
            finally { AppSettings.SkinBlur = blurBefore; }

            // Les réglages.
            string backgroundBefore = AppSettings.SkinBackground;
            var glassBefore = AppSettings.SkinGlass;
            try
            {
                AppSettings.PathOverride = Path.Combine(dir, "settings-museum.json");
                AppSettings.Skin = Skins.MuseumOath;
                AppSettings.SkinBackground = "#14101F";
                AppSettings.SkinGlass = 70;
                AppSettings.Save();
                AppSettings.Skin = null; AppSettings.SkinBackground = null; AppSettings.SkinGlass = -1;
                AppSettings.Load();
                t.Check(AppSettings.Skin == Skins.MuseumOath && AppSettings.SkinBackground == "#14101F" && AppSettings.SkinGlass == 70, "le skin, son fond et son verre sont retenus dans les réglages");
                AppSettings.SkinBackground = null; AppSettings.SkinGlass = -1;
                AppSettings.Save();
                var json = File.ReadAllText(AppSettings.PathOverride);
                t.Check(!json.Contains("skinBackground") && !json.Contains("skinGlass"), "le fond et le verre du skin n'écrivent rien");
                File.WriteAllText(AppSettings.PathOverride, json.TrimEnd().TrimEnd('}') + ", \"skinBackground\": \"bleu\", \"skinGlass\": 400 }");
                AppSettings.Load();
                t.Check(AppSettings.SkinBackground == null && AppSettings.SkinGlass == -1, "un fond illisible et un verre hors plage retombent sur ceux du skin");
            }
            finally { AppSettings.SkinBackground = backgroundBefore; AppSettings.SkinGlass = glassBefore; }

            // « Arrondi des angles » (24/11) : un réglage général, retenu, borné de 0 à 2.
            var cornersBefore = AppSettings.CornerScale;
            try
            {
                AppSettings.PathOverride = Path.Combine(dir, "settings-arrondi.json");
                AppSettings.CornerScale = 0;
                AppSettings.Save();
                AppSettings.CornerScale = 1;
                AppSettings.Load();
                t.Check(Math.Abs(AppSettings.CornerScale) < 0.001, "l'arrondi des angles à 0 % est retenu dans les réglages");
                AppSettings.CornerScale = 1.5;
                AppSettings.Save();
                AppSettings.CornerScale = 1;
                AppSettings.Load();
                t.Check(Math.Abs(AppSettings.CornerScale - 1.5) < 0.001, "…à 150 % aussi");
                AppSettings.CornerScale = 1;
                AppSettings.Save();
                var json = File.ReadAllText(AppSettings.PathOverride);
                t.Check(!json.Contains("cornerScale"), "…à 100 %, rien n'est écrit : le fichier de réglages est celui d'avant");
                File.WriteAllText(AppSettings.PathOverride, json.TrimEnd().TrimEnd('}') + ", \"cornerScale\": 9 }");
                AppSettings.Load();
                t.Check(Math.Abs(AppSettings.CornerScale - 1) < 0.001, "…une valeur hors de 0 à 2 retombe sur le dessin d'origine");
            }
            finally { AppSettings.CornerScale = cornersBefore; }

            // L'Encyclopédie.
            var plain = HtmlEncyclopedia.Css(new EncyclopediaOptions());
            foreach (var id in series)
            {
                var css = Skins.HtmlCss(id, Skins.AccentFor(id, null), "", false, false);
                t.Check(css.StartsWith("@media screen{") && css.Contains("--bg:" + Skins.MuseumBackground(id, false)) && css.Contains("[data-theme=dark]{") && css.Contains("--bg:" + Skins.MuseumBackground(id, true)) && css.Contains("small-caps"),
                    "« " + Skins.Label(id) + " » habille l'Encyclopédie : son fond de jour, son fond de nuit, des titres en petites capitales");
                t.Check(!css.Contains("text-shadow") && !css.Contains("Tahoma"), "…sans rien emprunter aux autres skins (ni néons, ni Tahoma)");
                t.Check(css.Contains("--heading:") && !Skins.HtmlCss(id, "#5B67D8", "", false, true).Contains("--heading:"), "…une police choisie dans la fenêtre d'export prime sur sa garalde");
                var full = HtmlEncyclopedia.Css(new EncyclopediaOptions { Skin = id });
                t.Check(full.Contains("@media screen{") && full.IndexOf("@media screen{", StringComparison.Ordinal) < full.IndexOf("@media print", StringComparison.Ordinal) && full.StartsWith(plain.Substring(0, plain.IndexOf("@media print", StringComparison.Ordinal))),
                    "…après la feuille de base, à l'écran seulement");
                t.Check(Skins.HtmlStartsDark(id) == Skins.DarkByDefault(id), "…la page s'ouvre au sombre si le skin l'est");
                var pdf = Skins.PdfSkinFor(id, Skins.AccentFor(id, null), null);
                t.Check(pdf != null && pdf.Paper != pdf.Text && pdf.HasFrame == Skins.DarkByDefault(id), "…et le « PDF Encyclopédique » a son papier" + (pdf != null && pdf.HasFrame ? ", cerné d'un filet de métal" : ""));
            }
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

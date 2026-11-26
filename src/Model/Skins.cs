using System;
using System.Globalization;
using System.Text;

namespace Marabook.Model
{
    /// <summary>LES SKINS (2.0.0, 21/11/2026, Rémi) : le look général de
    /// l'application — et de ce qui ne se lit qu'avec elle ou depuis elle :
    /// les pages wiki des fiches, l'Encyclopédie en HTML et en PDF. Un skin
    /// ne touche JAMAIS à la manière dont un texte est exporté (PDF BAT,
    /// Word, EPUB…) : la mise en pages d'un livre n'est pas une affaire de
    /// goût d'interface. Ici, le cœur : les identifiants, les libellés, la
    /// seconde couleur de « Gradient Garden », et l'habillage des exports de
    /// l'Encyclopédie. La palette et les gabarits de l'interface vivent dans
    /// l'application (Skin.cs).</summary>
    public static class Skins
    {
        public const string Modern = "modern", Classic = "classic", Gradient = "gradient", Eden = "eden", Cyberpunk = "cyberpunk";

        /// <summary>LA SÉRIE « MUSEUM » (2.0.0, 24/11/2026, Rémi) — trois skins PREMIUM d'une même
        /// famille : verre fumé, filets de métal, titres à empattements. « Gardien du Feu »
        /// (la nuit, bleu canard et cuivre), « Bois Blanc » (le jour, parchemin et vert-de-gris),
        /// « Assermenté » (bordeaux et laiton). Chacun a trois options : la couleur d'accent
        /// (celle de l'application ; sans choix de l'auteur, celle du skin), la couleur de fond
        /// et l'effet verre.</summary>
        public const string MuseumFire = "museum-feu", MuseumWood = "museum-bois", MuseumOath = "museum-serment";

        /// <summary>« ASCENSION » (24/11/2026 au soir, Rémi — la « Citadelle » de l'Atelier, renommée) : le
        /// quatrième skin de la famille. Bleu nuit, filets d'or en dégradé, survol bleu clair, angles
        /// droits, titres en capitales ; pas de pilule de sélection (une barre d'accent à gauche).</summary>
        public const string MuseumAscent = "museum-ascension";

        public static readonly string[] Ids = { Modern, Classic, Gradient, Eden, Cyberpunk, MuseumFire, MuseumWood, MuseumOath, MuseumAscent };

        /// <summary>Vrai pour un skin de la série « Museum ».</summary>
        public static bool IsMuseum(string id)
        {
            id = Normalize(id);
            return id == MuseumFire || id == MuseumWood || id == MuseumOath || id == MuseumAscent;
        }

        /// <summary>LES SKINS À DÉBLOQUER (25/11/2026, Rémi) : « Gardien du Feu », « Bois Blanc » et
        /// « Assermenté » s'ouvrent chacun par un succès secret ; l'identifiant de ce succès, null pour
        /// un skin libre. « Ascension » s'ouvre par le dernier : « S'offrir un monde ».</summary>
        public static string UnlockedBy(string id)
        {
            switch (Normalize(id))
            {
                case MuseumFire: return Achievements.FireKeeper;
                case MuseumWood: return Achievements.WhiteWood;
                case MuseumOath: return Achievements.Sworn;
                case MuseumAscent: return Achievements.World; // « S'offrir un monde » (26/11)
                default: return null;
            }
        }

        /// <summary>Vrai tant qu'un skin n'est pas débloqué : son succès manque, et le sésame
        /// (« Je veux un monde meilleur », <paramref name="everything"/>) n'a pas été dit.</summary>
        public static bool Locked(string id, System.Collections.Generic.ICollection<string> achievements, bool everything)
        {
            var key = UnlockedBy(id);
            return key != null && !everything && (achievements == null || !achievements.Contains(key));
        }

        /// <summary>LE SÉSAME (25/11/2026, Rémi) : tapée seule dans « Ouvrir par le nom » (Ctrl+,) puis
        /// validée, cette phrase débloque tous les skins — et, à terme, tout ce qui se débloque dans
        /// l'application, hors succès. Sans casse, ni accents, ni ponctuation finale.</summary>
        public const string Sesame = "Je veux un monde meilleur";
        /// <summary>Le code de mise au point (26/11) : tapé seul dans la même palette, il rejoue la fenêtre
        /// « Vous avez réussi » de « S'offrir un monde », sans export.</summary>
        public const string DevAscension = "dev-ascension";
        public static bool IsDevAscension(string typed) { return string.Equals((typed ?? "").Trim(), DevAscension, StringComparison.OrdinalIgnoreCase); }

        public static bool IsSesame(string typed)
        {
            var folded = Correction.FrenchTokenizer.Fold((typed ?? "").Trim().TrimEnd('.', '!', ' ')).ToLowerInvariant();
            return string.Join(" ", folded.Split(new[] { ' ', '\u00A0' }, StringSplitOptions.RemoveEmptyEntries)) == "je veux un monde meilleur";
        }

        /// <summary>Vrai pour un skin PREMIUM (la série « Museum », pour l'heure).</summary>
        public static bool Premium(string id) { return IsMuseum(id); }

        /// <summary>L'accent d'un skin « Museum » tant que l'auteur n'en a pas choisi ; null pour les autres skins.</summary>
        public static string MuseumAccent(string id)
        {
            switch (Normalize(id))
            {
                case MuseumFire: return "#E0915A";
                case MuseumWood: return "#4F7D5B";
                case MuseumOath: return "#C9A45C";
                case MuseumAscent: return "#C89B3C";
                default: return null;
            }
        }

        /// <summary>L'accent effectif : celui choisi, sinon celui du skin (« Museum »), sinon l'indigo de Marabook.</summary>
        public static string AccentFor(string id, string chosen)
        {
            if (IsHex(chosen)) return chosen.ToUpperInvariant();
            return MuseumAccent(id) ?? "#5B67D8";
        }

        /// <summary>Le fond d'origine d'un skin « Museum », au sombre ou au clair ; null pour les autres skins.</summary>
        public static string MuseumBackground(string id, bool dark)
        {
            switch (Normalize(id))
            {
                case MuseumFire: return dark ? "#0A1A21" : "#D4E6EF";
                case MuseumWood: return dark ? "#19150B" : "#D9D2C2";
                case MuseumOath: return dark ? "#1A080C" : "#EFD4D9";
                case MuseumAscent: return dark ? "#010A13" : "#D4DDEF";
                default: return null;
            }
        }

        /// <summary>Le fond effectif d'un skin « Museum » : celui choisi, sinon celui du skin. Sa clarté
        /// est ramenée dans la plage du mode (sombre 3 à 24 %, clair 78 à 97 %) : l'encre reste lisible
        /// quoi qu'on choisisse.</summary>
        public static string MuseumBackgroundFor(string id, bool dark, string chosen)
        {
            var hex = IsHex(chosen) ? chosen : MuseumBackground(id, dark) ?? (dark ? "#14161B" : "#E2E5EE");
            double h, s, l;
            ToHsl(hex, out h, out s, out l);
            return FromHsl(h, s, Math.Max(dark ? 0.03 : 0.78, Math.Min(dark ? 0.24 : 0.97, l)));
        }

        /// <summary>L'effet verre d'origine d'un skin « Museum » (0 = barres opaques, 100 = très transparentes).
        /// Le même pour toute la famille : 65 % (24/11 au soir, Rémi — 44, 31 et 36 ne se voyaient pas assez).</summary>
        public static int MuseumGlass(string id) { return 65; }

        /// <summary>Le flou d'origine derrière le verre d'un skin « Museum », en pixels : 15 pour toute
        /// la famille (24/11 au soir, Rémi — 8 et 6 auparavant).</summary>
        public static int MuseumBlur(string id) { return 15; }

        /// <summary>Vrai pour un skin dont la ligne choisie ou survolée (Pile, menus, listes) est une
        /// PILULE de verre à perle ; « Ascension » garde une barre d'accent à gauche.</summary>
        public static bool MuseumPill(string id) { return IsMuseum(id) && Normalize(id) != MuseumAscent; }

        /// <summary>Vrai pour un skin dont les TITRES sont en capitales espacées (« Ascension »).</summary>
        public static bool MuseumCaps(string id) { return Normalize(id) == MuseumAscent; }

        /// <summary>LA BARRE À JALONS d'un livre (24/11 au soir, d'après le menu « Musée » du jeu) : ce
        /// qu'un livre a franchi quand la part de ses chapitres TERMINÉS atteint 25, 60 puis 95 %.
        /// Part, nom court du jalon, mention une fois franchi.</summary>
        public static readonly double[] BookMilestones = { 0.25, 0.60, 0.95 };
        public static readonly string[] BookMilestoneNames = { "Premier jet", "Bêta", "Relu" };
        public static readonly string[] BookMilestonePassed = { "Premier jet bouclé", "Bêta lancée", "Prêt pour le BAT" };

        /// <summary>Le nombre de jalons franchis à cette part (0 à 1).</summary>
        public static int MilestonesPassed(double ratio)
        {
            var count = 0;
            foreach (var mark in BookMilestones) if (ratio + 1e-9 >= mark) count++;
            return count;
        }

        /// <summary>Le flou effectif derrière le verre : celui choisi (0 à 24 px), sinon celui du skin.</summary>
        public static int MuseumBlurFor(string id, int chosen) { return chosen >= 0 && chosen <= 24 ? chosen : MuseumBlur(id); }

        /// <summary>L'effet verre effectif : celui choisi (0 à 100), sinon celui du skin.</summary>
        public static int MuseumGlassFor(string id, int chosen) { return chosen >= 0 && chosen <= 100 ? chosen : MuseumGlass(id); }

        /// <summary>L'identifiant connu, ou « modern » (réglage absent, ou d'un Marabook plus récent).</summary>
        public static string Normalize(string id)
        {
            foreach (var known in Ids) if (known == id) return known;
            return Modern;
        }

        public static string Label(string id)
        {
            switch (Normalize(id))
            {
                case Classic: return "Marabook classic";
                case Gradient: return "Gradient Garden";
                case Eden: return "Eden Scriptis";
                case Cyberpunk: return "Cyberpunk 2026";
                case MuseumFire: return "Gardien du Feu";
                case MuseumWood: return "Bois Blanc";
                case MuseumOath: return "Assermenté";
                case MuseumAscent: return "Ascension";
                default: return "Marabook modern";
            }
        }

        public static string Description(string id)
        {
            switch (Normalize(id))
            {
                case Classic: return "Le logiciel de bureau de 2005 : gris beige, angles droits, boutons en relief, Tahoma.";
                case Gradient: return "Des dégradés partout, entre votre couleur d'accent et une seconde couleur.";
                case Eden: return "Le vieux livre : papier jauni, encre sépia, caractères à empattements.";
                case Cyberpunk: return "Sombre par défaut, des néons partout.";
                case MuseumFire: return "Museum — le musée la nuit : verre fumé bleu canard, filets cuivrés, titres à empattements.";
                case MuseumWood: return "Museum — le musée le jour : parchemin clair, vert-de-gris, bois blond.";
                case MuseumOath: return "Museum — le cabinet du conservateur : bordeaux profond, laiton, angles nets.";
                case MuseumAscent: return "Museum — la citadelle : bleu nuit, filets d'or, angles droits, titres en capitales.";
                default: return "Le look de Marabook.";
            }
        }

        /// <summary>Vrai pour un skin qui passe l'application au sombre quand on le choisit.</summary>
        public static bool DarkByDefault(string id) { id = Normalize(id); return id == Cyberpunk || id == MuseumFire || id == MuseumOath || id == MuseumAscent; }

        // ================================================================ couleurs

        /// <summary>La seconde couleur d'un dégradé, « automatique » : la
        /// voisine de l'accent sur le cercle des teintes (55° plus loin), en
        /// évitant les jaunes-verts ternes — un indigo va vers le magenta, un
        /// rouge vers l'orangé, un vert vers le turquoise.</summary>
        public static string AutoSecond(string accentHex)
        {
            double h, s, l;
            ToHsl(SafeHex(accentHex, "#5B67D8"), out h, out s, out l);
            var forward = (h + 55) % 360;
            var hue = forward >= 55 && forward <= 105 ? (h + 305) % 360 : forward;
            return FromHsl(hue, Math.Max(0.55, Math.Min(0.85, s)), Math.Max(0.45, Math.Min(0.62, l)));
        }

        /// <summary>La seconde couleur effective : celle choisie, sinon l'automatique.</summary>
        public static string SecondFor(string accentHex, string chosen)
        {
            var hex = (chosen ?? "").Trim();
            return IsHex(hex) ? hex.ToUpperInvariant() : AutoSecond(accentHex);
        }

        public static bool IsHex(string hex)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
            for (var i = 1; i < 7; i++) if (!Uri.IsHexDigit(hex[i])) return false;
            return true;
        }

        public static string SafeHex(string hex, string fallback) { return IsHex(hex) ? hex : fallback; }

        public static void ToHsl(string hex, out double h, out double s, out double l)
        {
            var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
            var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
            var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            var delta = max - min;
            if (delta < 1e-6) { h = 0; s = 0; return; }
            s = delta / (1 - Math.Abs(2 * l - 1));
            if (max == r) h = 60 * (((g - b) / delta) % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
            if (h < 0) h += 360;
        }

        public static string FromHsl(double h, double s, double l)
        {
            var c = (1 - Math.Abs(2 * l - 1)) * s;
            var hp = ((h % 360) + 360) % 360 / 60.0;
            var x = c * (1 - Math.Abs(hp % 2 - 1));
            double r, g, b;
            if (hp < 1) { r = c; g = x; b = 0; }
            else if (hp < 2) { r = x; g = c; b = 0; }
            else if (hp < 3) { r = 0; g = c; b = x; }
            else if (hp < 4) { r = 0; g = x; b = c; }
            else if (hp < 5) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            var m = l - c / 2;
            return "#" + Channel(r + m) + Channel(g + m) + Channel(b + m);
        }

        private static string Channel(double value)
        {
            var v = (int)Math.Round(value * 255);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return v.ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>La même teinte, à cette saturation et cette clarté.</summary>
        public static string Shade(string hex, double saturation, double lightness)
        {
            double h, s, l;
            ToHsl(SafeHex(hex, "#5B67D8"), out h, out s, out l);
            return FromHsl(h, s < 0.02 ? 0 : saturation, lightness);
        }

        // ================================================================ l'Encyclopédie en HTML

        /// <summary>Le nom du fichier de texture posé à côté de la feuille de style (Eden Scriptis).</summary>
        public const string TextureFile = "papier.jpg";

        /// <summary>Vrai si l'Encyclopédie s'ouvre au sombre sous ce skin, quoi que dise l'option.</summary>
        public static bool HtmlStartsDark(string id) { return DarkByDefault(id); }

        /// <summary>Ce qu'un skin ajoute à la feuille de style de l'Encyclopédie —
        /// vide pour « modern » : le fichier est alors, au caractère près, celui
        /// d'avant. accent2 = la seconde couleur ; texture = le papier est fourni.
        /// Tout est sous « @media screen » : à l'impression depuis le navigateur,
        /// la page reste noire sur blanc.</summary>
        public static string HtmlCss(string id, string accent, string accent2, bool texture, bool customFonts)
        {
            id = Normalize(id);
            if (id == Modern) return "";
            accent = SafeHex(accent, "#5B67D8");
            accent2 = SecondFor(accent, accent2);
            if (IsMuseum(id)) return MuseumCss(id, accent, customFonts);
            var sb = new StringBuilder();
            sb.Append("@media screen{\n");
            if (id == Classic)
            {
                sb.Append(":root{--bg:#ECE9D8;--paper:#FFFFFF;--ink:#000000;--soft:#444444;--line:#ACA899;--tint:#D8E4F8");
                if (!customFonts) sb.Append(";--body:Tahoma,Verdana,Arial,sans-serif;--heading:Tahoma,Verdana,Arial,sans-serif");
                sb.Append("}\n[data-theme=dark]{--bg:#2B2B2B;--paper:#3C3B37;--ink:#F0EFE8;--soft:#B5B3A8;--line:#6B6A63;--tint:#4A4E5C}\n");
                sb.Append("*{border-radius:0!important}\n");
                sb.Append("a{text-decoration:underline}\n");
                sb.Append(".category,.infobox,.letter,.maps-page .map,.apercu{border:2px solid;border-color:#FFFFFF #716F64 #716F64 #FFFFFF;box-shadow:1px 1px 0 #404040}\n");
                sb.Append("[data-theme=dark] .category,[data-theme=dark] .infobox,[data-theme=dark] .letter,[data-theme=dark] .maps-page .map,[data-theme=dark] .apercu{border-color:#8A8980 #1B1B1B #1B1B1B #8A8980;box-shadow:1px 1px 0 #000}\n");
                sb.Append(".hero h1,.sheet-page header h1{background:linear-gradient(90deg,").Append(Shade(accent, 0.75, 0.24)).Append(",").Append(Shade(accent, 0.62, 0.70)).Append(");color:#FFFFFF;font-size:1.35rem;font-weight:700;padding:5px 12px;text-align:left}\n");
                sb.Append("h2{border-bottom:2px groove var(--line)}\n");
                sb.Append(".search,button.theme{border:2px inset #D4D0C8}.tag{border:1px solid var(--line);background:var(--bg);color:var(--ink)}\n");
            }
            else if (id == Gradient)
            {
                var a1 = Shade(accent, 0.80, 0.93); var b1 = Shade(accent2, 0.80, 0.90);
                var a2 = Shade(accent, 0.55, 0.13); var b2 = Shade(accent2, 0.55, 0.19);
                sb.Append(":root{--accent2:").Append(accent2).Append(";--bg:").Append(a1).Append(";--paper:rgba(255,255,255,.72);--line:rgba(255,255,255,.75);--tint:rgba(255,255,255,.6)}\n");
                sb.Append("[data-theme=dark]{--bg:").Append(a2).Append(";--paper:rgba(20,22,30,.62);--line:rgba(255,255,255,.14);--tint:rgba(255,255,255,.09)}\n");
                sb.Append("body{background:linear-gradient(135deg,").Append(a1).Append(",").Append(b1).Append(") fixed}\n");
                sb.Append("[data-theme=dark] body{background:linear-gradient(135deg,").Append(a2).Append(",").Append(b2).Append(") fixed}\n");
                sb.Append("h1{background:linear-gradient(90deg,var(--accent),var(--accent2));-webkit-background-clip:text;background-clip:text;color:transparent}\n");
                sb.Append(".category,.infobox,.letter,.maps-page .map{border-radius:18px;box-shadow:0 8px 28px rgba(0,0,0,.08)}\n");
                sb.Append(".category h2,.category h3,.category h4{border-image:linear-gradient(180deg,var(--accent),var(--accent2)) 1}\n");
                sb.Append(".tag{background:linear-gradient(90deg,var(--accent),var(--accent2));color:#FFFFFF}.rating{color:var(--accent2)}\n");
                sb.Append("h2{border-image:linear-gradient(90deg,var(--accent),var(--accent2),transparent) 1}\n");
                sb.Append(".radar .value{fill:var(--accent2);stroke:var(--accent)}\n");
            }
            else if (id == Eden)
            {
                sb.Append(":root{--bg:#E6D5AE;--paper:rgba(250,241,219,.80);--ink:#3B2A1A;--soft:#6B5440;--line:#BFA677;--tint:rgba(214,190,140,.45)");
                if (!customFonts) sb.Append(";--body:\"Palatino Linotype\",\"Book Antiqua\",Palatino,Georgia,serif;--heading:\"Palatino Linotype\",\"Book Antiqua\",Palatino,Georgia,serif");
                sb.Append("}\n[data-theme=dark]{--bg:#2A2015;--paper:rgba(48,37,24,.84);--ink:#EADBC0;--soft:#B9A483;--line:#5C4A30;--tint:rgba(120,95,55,.35)}\n");
                if (texture)
                {
                    sb.Append("body{background:#E6D5AE url(").Append(TextureFile).Append(") center/cover fixed}\n");
                    sb.Append("[data-theme=dark] body{background:linear-gradient(rgba(24,17,9,.82),rgba(24,17,9,.82)),url(").Append(TextureFile).Append(") center/cover fixed}\n");
                }
                sb.Append("h1{font-variant:small-caps;letter-spacing:.04em;font-weight:400}h2{border-bottom:3px double var(--line);font-variant:small-caps;letter-spacing:.03em}\n");
                sb.Append(".category,.infobox,.letter,.maps-page .map{border:3px double var(--line);border-radius:4px}\n");
                sb.Append(".body>p:first-of-type::first-letter{float:left;font-size:3.1em;line-height:.85;padding:.06em .08em 0 0;color:var(--accent)}\n");
                sb.Append(".tag{border-radius:3px;font-style:italic}.infobox .portrait,.body img{border-radius:2px;border:1px solid var(--line)}\n");
            }
            else
            {
                // Cyberpunk 2026 : le sombre est le visage par défaut ; le clair, un « jour » lavé.
                var neon = Shade(accent, 1.0, 0.62); var neon2 = Shade(accent2, 1.0, 0.60);
                sb.Append(":root{--accent2:").Append(Shade(accent2, 0.9, 0.42)).Append(";--bg:#ECEAF6;--paper:#F8F7FD;--ink:#16122B;--soft:#5A5578;--line:").Append(Shade(accent, 0.7, 0.72)).Append(";--tint:").Append(Shade(accent, 0.8, 0.92));
                if (!customFonts) sb.Append(";--heading:Bahnschrift,\"DIN Alternate\",\"Segoe UI\",Arial,sans-serif;--body:\"Segoe UI\",Helvetica,Arial,sans-serif");
                sb.Append("}\n[data-theme=dark]{--accent:").Append(neon).Append(";--accent2:").Append(neon2).Append(";--bg:#07060D;--paper:#0F0C1C;--ink:#ECE9FF;--soft:#9A96B8;--line:").Append(Shade(accent, 0.9, 0.34)).Append(";--tint:").Append(Shade(accent, 0.8, 0.13)).Append("}\n");
                sb.Append("body{background-image:linear-gradient(rgba(128,128,160,.07) 1px,transparent 1px),linear-gradient(90deg,rgba(128,128,160,.07) 1px,transparent 1px);background-size:36px 36px}\n");
                sb.Append("*{border-radius:2px!important}\n");
                sb.Append("h1,h2,h3{text-transform:uppercase;letter-spacing:.08em}h1{text-shadow:0 0 6px var(--accent),0 0 22px var(--accent)}h2{border-bottom:1px solid var(--accent);text-shadow:0 0 8px var(--accent2)}\n");
                sb.Append("a{text-shadow:0 0 8px var(--accent)}\n");
                sb.Append(".category,.infobox,.letter,.maps-page .map,.apercu{border:1px solid var(--accent);box-shadow:0 0 14px -3px var(--accent),inset 0 0 18px -12px var(--accent2)}\n");
                sb.Append(".category h2,.category h3,.category h4{border-left-color:var(--accent2);text-shadow:none}\n");
                sb.Append(".tag{background:transparent;border:1px solid var(--accent2);color:var(--accent2);box-shadow:0 0 8px -2px var(--accent2)}\n");
                sb.Append(".search:focus{box-shadow:0 0 12px -2px var(--accent)}.radar .value{fill:var(--accent2);stroke:var(--accent)}\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>La série « Museum » dans l'Encyclopédie : le fond et l'encre du skin (clair et
        /// sombre), un filet de métal autour des encarts, des titres à empattements en petites
        /// capitales espacées. L'accent est celui qu'on lui passe (celui de l'application).</summary>
        private static string MuseumCss(string id, string accent, bool customFonts)
        {
            string light = MuseumBackground(id, false), dark = MuseumBackground(id, true);
            string inkLight, softLight, inkDark, softDark, serif;
            var radius = "12";
            if (id == MuseumWood) { inkLight = "#2B2620"; softLight = "#5A5247"; inkDark = "#DEDAD5"; softDark = "#B7B0A6"; serif = "'EB Garamond',Garamond,Georgia,serif"; radius = "10"; }
            else if (id == MuseumOath) { inkLight = "#1A130A"; softLight = "#46352A"; inkDark = "#F6EEE4"; softDark = "#D8C3B6"; serif = "'Libre Caslon Text','Book Antiqua',Georgia,serif"; radius = "6"; }
            else if (id == MuseumAscent) { inkLight = "#2B2211"; softLight = "#726D61"; inkDark = "#F0E6D2"; softDark = "#A09B8C"; serif = "'Marcellus SC',Cinzel,'Trajan Pro','Palatino Linotype',Georgia,serif"; radius = "0"; }
            else { inkLight = "#020F1C"; softLight = "#1F2C34"; inkDark = "#FBFDFF"; softDark = "#C9D8E2"; serif = "'Cormorant Garamond',Garamond,Georgia,serif"; }
            double h, s, l;
            ToHsl(accent, out h, out s, out l);
            var nl = ((char)10).ToString();
            var sb = new StringBuilder();
            sb.Append("@media screen{").Append(nl);
            sb.Append(":root{--accent:").Append(FromHsl(h, Math.Min(s, 0.45), 0.42)).Append(";--bg:").Append(light).Append(";--paper:rgba(255,255,255,.62);--ink:").Append(inkLight).Append(";--soft:").Append(softLight)
              .Append(";--line:").Append(FromHsl(h, 0.28, 0.62)).Append(";--tint:rgba(255,255,255,.45)");
            if (!customFonts) sb.Append(";--heading:").Append(serif);
            sb.Append("}").Append(nl).Append("[data-theme=dark]{--accent:").Append(FromHsl(h, Math.Min(s, 0.68), 0.62)).Append(";--bg:").Append(dark).Append(";--paper:rgba(255,255,255,.055);--ink:").Append(inkDark).Append(";--soft:").Append(softDark)
              .Append(";--line:").Append(FromHsl(h, 0.33, 0.44)).Append(";--tint:rgba(255,255,255,.08)}").Append(nl);
            sb.Append("h1{font-weight:500;letter-spacing:.02em}h2{font-variant:small-caps;letter-spacing:.08em;border-bottom:1px solid var(--line)}").Append(nl);
            if (id == MuseumAscent) sb.Append("h1,h2{text-transform:uppercase;letter-spacing:.06em}*{border-radius:0!important}").Append(nl);
            sb.Append(".category,.infobox,.letter,.maps-page .map,.apercu{border:1px solid var(--line);border-radius:").Append(radius).Append("px;box-shadow:inset 0 1px 0 rgba(255,255,255,.10)}").Append(nl);
            sb.Append(".infobox{border-left:6px solid var(--accent)}").Append(nl);
            sb.Append(".tag{background:transparent;border:1px solid var(--line);color:var(--accent);text-transform:uppercase;letter-spacing:.12em;font-size:.72rem}").Append(nl);
            sb.Append("}").Append(nl);
            return sb.ToString();
        }

        // ================================================================ le PDF encyclopédique

        /// <summary>L'habillage du « PDF Encyclopédique » : la couleur du papier,
        /// celle de l'encre et de l'encre douce. Null pour « modern » — le PDF
        /// est alors, à l'octet près, celui d'avant. Un PDF n'a pas de bascule
        /// clair/sombre : chaque skin y a UN visage.</summary>
        public static Print.PdfSkin PdfSkinFor(string id, string accent, string accent2)
        {
            id = Normalize(id);
            accent = SafeHex(accent, "#5B67D8");
            if (id == Classic) return new Print.PdfSkin { Paper = Ink.Parse("#F1EFE2"), Text = Ink.Parse("#000000"), Soft = Ink.Parse("#444444") };
            if (id == Gradient) return new Print.PdfSkin { Paper = Ink.Parse(Shade(accent, 0.80, 0.955)), Text = Ink.Parse(Shade(accent, 0.45, 0.16)), Soft = Ink.Parse(Shade(accent, 0.25, 0.40)), PaperEnd = Ink.Parse(Shade(SecondFor(accent, accent2), 0.80, 0.93)), Banded = true };
            if (id == Eden) return new Print.PdfSkin { Paper = Ink.Parse("#F1E4C4"), Text = Ink.Parse("#3B2A1A"), Soft = Ink.Parse("#6B5440"), Texture = true };
            if (id == MuseumFire) return new Print.PdfSkin { Paper = Ink.Parse("#0E2530"), Text = Ink.Parse("#FBFDFF"), Soft = Ink.Parse("#C9D8E2"), Frame = Ink.Parse(Shade(accent, 0.45, 0.54)), HasFrame = true };
            if (id == MuseumWood) return new Print.PdfSkin { Paper = Ink.Parse("#F3EEE3"), Text = Ink.Parse("#2B2620"), Soft = Ink.Parse("#5A5247") };
            if (id == MuseumOath) return new Print.PdfSkin { Paper = Ink.Parse("#22090E"), Text = Ink.Parse("#F6EEE4"), Soft = Ink.Parse("#D8C3B6"), Frame = Ink.Parse(Shade(accent, 0.50, 0.57)), HasFrame = true };
            if (id == MuseumAscent) return new Print.PdfSkin { Paper = Ink.Parse("#0A1428"), Text = Ink.Parse("#F0E6D2"), Soft = Ink.Parse("#A09B8C"), Frame = Ink.Parse(Shade(accent, 0.56, 0.51)), HasFrame = true };
            if (id == Cyberpunk) return new Print.PdfSkin { Paper = Ink.Parse("#0B0914"), Text = Ink.Parse("#ECE9FF"), Soft = Ink.Parse("#9A96B8"), Frame = Ink.Parse(Shade(accent, 1.0, 0.60)), HasFrame = true };
            return null;
        }
    }
}

namespace Marabook.Print
{
    /// <summary>L'habillage d'un PDF par un skin (le « PDF Encyclopédique »
    /// seulement) : le papier peint sous chaque page, l'encre noire et les gris
    /// du texte remplacés ; les couleurs posées par l'auteur ne bougent pas.</summary>
    public class PdfSkin
    {
        public Model.Ink Paper, Text, Soft;
        /// <summary>Gradient Garden : le papier glisse de Paper (haut) à PaperEnd (bas), en bandes.</summary>
        public bool Banded;
        public Model.Ink PaperEnd;
        /// <summary>Eden Scriptis : la texture de papier, si TextureRgb est fourni par l'application.</summary>
        public bool Texture;
        public byte[] TextureRgb;
        public int TextureWidth, TextureHeight;
        /// <summary>Cyberpunk : un filet néon autour de la page (Frame.A > 0).</summary>
        public Model.Ink Frame;
        public bool HasFrame;

        /// <summary>L'encre d'un skin pour cette couleur : le noir et les gris
        /// foncés deviennent l'encre, les gris moyens l'encre douce ; le reste
        /// (couleurs, blancs) est rendu tel quel.</summary>
        public Model.Ink Map(Model.Ink color)
        {
            if (color.A == 0) return color;
            var max = Math.Max(color.R, Math.Max(color.G, color.B));
            var min = Math.Min(color.R, Math.Min(color.G, color.B));
            if (max - min > 28) return color;
            var luma = (color.R + color.G + color.B) / 3;
            if (luma < 72) return Text;
            if (luma < 176) return Soft;
            return color;
        }
    }
}

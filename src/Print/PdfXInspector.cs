using System;
using System.Collections.Generic;
using System.Text;

namespace Marabook.Print
{
    /// <summary>Le compte rendu du contrôle : les défauts (bloquants pour la
    /// norme), les remarques (ce qui n'a pas pu être lu, ce qui est conseillé),
    /// et ce qui a été regardé.</summary>
    public class PdfXReport
    {
        public readonly List<string> Problems = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public int Pages, Fonts, Images, Streams, Rules;
        public bool Conforms { get { return Problems.Count == 0; } }

        /// <summary>« 12 pages, 3 polices, 2 images — 41 règles vérifiées ».</summary>
        public string Summary()
        {
            return Pages + (Pages > 1 ? " pages, " : " page, ") + Fonts + (Fonts > 1 ? " polices, " : " police, ") + Images + (Images > 1 ? " images" : " image")
                + " — " + Rules + " règles de PDF/X-1a:2003 vérifiées";
        }
    }

    /// <summary>LE CONTRÔLE PDF/X-1a:2003, REFAIT (21/11/2026). Il LIT le
    /// fichier (PdfLite) au lieu d'y chercher des chaînes : le catalogue,
    /// les informations, l'intention de sortie et son profil, puis chaque
    /// page — ses boîtes, ses annotations, ses ressources (polices, images,
    /// espaces de couleur, états graphiques, motifs, formulaires emboîtés) et
    /// les opérateurs de ses flux de contenu, une fois décompressés. Il vaut
    /// donc aussi pour un PDF que Marabook n'a pas écrit.
    ///
    /// Les règles suivent les clauses 6.1 à 6.17 de l'ISO 15930-4 et les
    /// notes d'application du CGATS. Ce contrôle reste celui de la maison :
    /// il atteste « conforme selon Marabook », pas « certifié » — seul un
    /// outil de l'imprimeur (Acrobat, pdfToolbox, PitStop) fait foi.</summary>
    public static class PdfXInspector
    {
        /// <summary>Le nombre de règles distinctes que le contrôle applique.</summary>
        public const int RuleCount = 40;

        private sealed class Run
        {
            public PdfLite Pdf;
            public PdfXReport Report = new PdfXReport();
            public readonly Dictionary<string, int> FirstPage = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> Count = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly List<string> Order = new List<string>();
            public readonly HashSet<object> Seen = new HashSet<object>(ReferenceComparer.Instance);
            public readonly HashSet<object> SeenFonts = new HashSet<object>(ReferenceComparer.Instance);
            public readonly HashSet<object> SeenImages = new HashSet<object>(ReferenceComparer.Instance);
            public int Page;

            public void Problem(string message)
            {
                int count;
                if (!Count.TryGetValue(message, out count)) { Order.Add(message); FirstPage[message] = Page; }
                Count[message] = count + 1;
            }

            public void Note(string message) { if (!Report.Notes.Contains(message)) Report.Notes.Add(message); }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            bool IEqualityComparer<object>.Equals(object a, object b) { return ReferenceEquals(a, b); }
            int IEqualityComparer<object>.GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }

        public static PdfXReport Inspect(byte[] bytes)
        {
            var run = new Run();
            var report = run.Report;
            report.Rules = RuleCount;
            if (bytes == null || bytes.Length < 64) { report.Problems.Add("Le fichier est vide."); return report; }
            PdfLite pdf;
            try { pdf = PdfLite.Read(bytes); }
            catch (Exception) { pdf = null; }
            if (pdf == null || pdf.Objects.Count == 0) { report.Problems.Add("Ce n'est pas un PDF."); return report; }
            run.Pdf = pdf;
            try
            {
                Document(run);
                Pages(run);
                WholeFile(run);
            }
            catch (Exception error)
            {
                run.Note("Le contrôle s'est arrêté avant la fin (" + error.GetType().Name + ") : ce qui suit n'a pas été regardé.");
            }
            foreach (var message in run.Order)
            {
                var page = run.FirstPage[message];
                var count = run.Count[message];
                var where = page <= 0 ? "" : count > 1 ? " (page " + page + ", et " + (count - 1) + " autre" + (count > 2 ? "s" : "") + " fois)" : " (page " + page + ")";
                report.Problems.Add(message + where);
            }
            return report;
        }

        // ------------------------------------------------------------ le document

        private static void Document(Run run)
        {
            var pdf = run.Pdf;
            // 6.1 — la structure : PDF 1.4 au plus, ni flux d'objets ni table de références compressée (PDF 1.5).
            double version;
            if (double.TryParse(pdf.Version, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out version) && version > 1.45)
                run.Problem("PDF/X-1a:2003 veut un PDF 1.4 au plus (trouvé : " + pdf.Version + ").");
            if (pdf.HasObjectStreams || pdf.HasXrefStreams)
                run.Problem("Le fichier range ses objets dans des flux compressés (PDF 1.5), que PDF/X-1a:2003 n'admet pas.");
            if (pdf.HasObjectStreams && !pdf.ObjectStreamsRead) run.Note("Les objets rangés dans des flux compressés n'ont pas été lus : le contrôle est incomplet pour ce fichier.");
            // 6.11 — pas de chiffrement.
            if (pdf.Trailer.ContainsKey("Encrypt")) run.Problem("Le fichier est chiffré.");
            // 6.7 — l'identifiant.
            var id = pdf.Resolve(pdf.Trailer.ContainsKey("ID") ? pdf.Trailer["ID"] : null) as List<object>;
            if (id == null || id.Count < 2) run.Problem("Le fichier n'a pas d'identifiant (/ID).");

            // 6.7 — le dictionnaire d'informations.
            var info = pdf.Dict(pdf.Trailer.ContainsKey("Info") ? pdf.Trailer["Info"] : null);
            var declared = info == null ? null : pdf.Get(info, "GTS_PDFXVersion") as PdfText;
            if (declared == null || !declared.Value.StartsWith("PDF/X-1a", StringComparison.Ordinal))
                run.Problem("Le fichier ne se déclare pas PDF/X-1a (GTS_PDFXVersion).");
            else if (declared.Value != "PDF/X-1a:2003")
                run.Note("Le fichier se déclare « " + declared.Value + " » : les règles appliquées ici sont celles de la révision 2003.");
            var title = info == null ? null : pdf.Get(info, "Title") as PdfText;
            if (title == null || title.Value.Trim().Length == 0) run.Problem("Le fichier n'a pas de titre.");
            if (info == null || !(pdf.Get(info, "CreationDate") is PdfText) || !(pdf.Get(info, "ModDate") is PdfText)) run.Problem("Il manque la date de création ou de modification.");
            // 6.6 — le recouvrement : un NOM, True ou False (jamais Unknown, jamais une chaîne).
            var trapped = info == null ? null : pdf.Get(info, "Trapped");
            var trappedName = trapped as PdfName;
            if (trappedName == null || (trappedName.Value != "True" && trappedName.Value != "False")) run.Problem("Le piégeage n'est pas déclaré (/Trapped doit valoir /True ou /False).");

            var catalog = pdf.Dict(pdf.Trailer.ContainsKey("Root") ? pdf.Trailer["Root"] : null);
            if (catalog == null) { run.Problem("Le catalogue du document est introuvable."); return; }

            // 6.2 — l'intention de sortie : une seule de sorte GTS_PDFX, nommée, avec son registre ou son profil.
            var intents = pdf.Get(catalog, "OutputIntents") as List<object>;
            var pdfx = new List<Dictionary<string, object>>();
            if (intents != null)
                foreach (var entry in intents)
                {
                    var intent = pdf.Dict(entry);
                    if (intent != null && pdf.Name(intent, "S") == "GTS_PDFX") pdfx.Add(intent);
                }
            if (pdfx.Count == 0) run.Problem("La condition d'impression n'est pas déclarée (OutputIntent).");
            else
            {
                if (pdfx.Count > 1) run.Problem("Le fichier déclare plusieurs conditions d'impression PDF/X : il n'en faut qu'une.");
                var intent = pdfx[0];
                var identifier = pdf.Get(intent, "OutputConditionIdentifier") as PdfText;
                if (identifier == null || identifier.Value.Trim().Length == 0) run.Problem("La condition d'impression n'a pas d'identifiant (OutputConditionIdentifier).");
                var profile = pdf.Resolve(intent.ContainsKey("DestOutputProfile") ? intent["DestOutputProfile"] : null) as PdfStream;
                var registry = pdf.Get(intent, "RegistryName") as PdfText;
                if (profile == null)
                {
                    if (registry == null || registry.Value.Trim().Length == 0)
                        run.Problem("La condition d'impression ne donne ni son registre (RegistryName) ni son profil ICC : un outil d'impression ne saurait pas la retrouver.");
                }
                else
                {
                    var icc = pdf.Decode(profile);
                    var channels = pdf.Get(profile.Dict, "N");
                    if (icc == null) run.Note("Le profil ICC de la condition d'impression n'a pas pu être décompressé : il n'a pas été vérifié.");
                    else
                    {
                        if (!IccProfiles.IsCmykPrinterProfile(icc)) run.Problem("Le profil ICC de la condition d'impression n'est pas un profil d'imprimante CMJN.");
                        else if (!IccProfiles.HasTag(icc, "A2B1")) run.Problem("Le profil ICC de la condition d'impression n'a pas la table colorimétrique (AtoB1) que PDF/X exige.");
                        if (!IccProfiles.SizeMatches(icc)) run.Problem("Le profil ICC de la condition d'impression est tronqué ou abîmé (sa taille déclarée n'est pas celle du flux).");
                    }
                    if (!(channels is double) || (int)(double)channels != 4) run.Problem("Le profil ICC de la condition d'impression ne se déclare pas à quatre composantes (/N 4).");
                }
                if (!(pdf.Get(intent, "OutputCondition") is PdfText) && !(pdf.Get(intent, "Info") is PdfText))
                    run.Note("La condition d'impression n'est pas décrite en clair (OutputCondition) : c'est conseillé, pas exigé.");
            }

            // 6.14 — ni action ni JavaScript à l'ouverture.
            if (catalog.ContainsKey("AA")) run.Problem("Le document porte des actions automatiques (AA), que PDF/X interdit.");
            var open = pdf.Dict(catalog.ContainsKey("OpenAction") ? catalog["OpenAction"] : null);
            if (open != null && pdf.Name(open, "S") == "JavaScript") run.Problem("Le document lance du JavaScript à l'ouverture.");
            var names = pdf.Dict(catalog.ContainsKey("Names") ? catalog["Names"] : null);
            if (names != null && names.ContainsKey("JavaScript")) run.Problem("Le document contient du JavaScript.");
            // 6.4 — pas de fichier joint.
            if (names != null && names.ContainsKey("EmbeddedFiles")) run.Problem("Le document contient des fichiers joints.");
        }

        // ------------------------------------------------------------ les pages

        private static void Pages(Run run)
        {
            var pdf = run.Pdf;
            var catalog = pdf.Dict(pdf.Trailer.ContainsKey("Root") ? pdf.Trailer["Root"] : null);
            if (catalog == null) return;
            var root = pdf.Dict(catalog.ContainsKey("Pages") ? catalog["Pages"] : null);
            var pages = new List<Dictionary<string, object>>();
            var inherited = new List<Dictionary<string, object>>();
            Collect(run, root, new Dictionary<string, object>(StringComparer.Ordinal), pages, inherited, 0);
            run.Report.Pages = pages.Count;
            if (pages.Count == 0) { run.Problem("Le fichier n'a aucune page."); return; }
            for (var i = 0; i < pages.Count; i++)
            {
                run.Page = i + 1;
                Page(run, pages[i], inherited[i]);
            }
            run.Page = 0;
        }

        private static void Collect(Run run, Dictionary<string, object> node, Dictionary<string, object> inherited, List<Dictionary<string, object>> pages, List<Dictionary<string, object>> inheritedOut, int depth)
        {
            if (node == null || depth > 64 || !run.Seen.Add(node)) return;
            var own = new Dictionary<string, object>(inherited, StringComparer.Ordinal);
            foreach (var key in new[] { "MediaBox", "CropBox", "Resources", "Rotate" })
                if (node.ContainsKey(key)) own[key] = node[key];
            var kids = run.Pdf.Get(node, "Kids") as List<object>;
            if (kids == null || run.Pdf.Name(node, "Type") == "Page") { pages.Add(node); inheritedOut.Add(own); return; }
            foreach (var kid in kids) Collect(run, run.Pdf.Dict(kid), own, pages, inheritedOut, depth + 1);
        }

        private static double[] Box(Run run, object value)
        {
            var list = run.Pdf.Resolve(value) as List<object>;
            if (list == null || list.Count != 4) return null;
            var box = new double[4];
            for (var i = 0; i < 4; i++)
            {
                var number = run.Pdf.Resolve(list[i]);
                if (!(number is double)) return null;
                box[i] = (double)number;
            }
            return new[] { Math.Min(box[0], box[2]), Math.Min(box[1], box[3]), Math.Max(box[0], box[2]), Math.Max(box[1], box[3]) };
        }

        private static bool Inside(double[] inner, double[] outer)
        {
            const double slack = 0.01;
            return inner[0] >= outer[0] - slack && inner[1] >= outer[1] - slack && inner[2] <= outer[2] + slack && inner[3] <= outer[3] + slack;
        }

        private static bool Overlaps(double[] a, double[] b)
        {
            return a[0] < b[2] && a[2] > b[0] && a[1] < b[3] && a[3] > b[1];
        }

        private static void Page(Run run, Dictionary<string, object> page, Dictionary<string, object> inherited)
        {
            var pdf = run.Pdf;
            // 6.8 — les boîtes : MediaBox, et TrimBox OU ArtBox (jamais les deux), contenue dans les autres.
            var media = Box(run, inherited.ContainsKey("MediaBox") ? inherited["MediaBox"] : null);
            if (media == null) run.Problem("Une page n'a pas de format (MediaBox).");
            var trim = Box(run, page.ContainsKey("TrimBox") ? page["TrimBox"] : null);
            var art = Box(run, page.ContainsKey("ArtBox") ? page["ArtBox"] : null);
            if (trim == null && art == null) run.Problem("Toutes les pages n'ont pas de boîte de coupe (TrimBox).");
            if (trim != null && art != null) run.Problem("Une page porte à la fois une boîte de coupe (TrimBox) et une boîte d'illustration (ArtBox) : PDF/X n'en veut qu'une.");
            var finished = trim ?? art;
            var bleed = Box(run, page.ContainsKey("BleedBox") ? page["BleedBox"] : null);
            var crop = Box(run, inherited.ContainsKey("CropBox") ? inherited["CropBox"] : null);
            if (finished != null)
            {
                if (bleed != null && !Inside(finished, bleed)) run.Problem("La boîte de coupe d'une page dépasse sa boîte de fond perdu (BleedBox).");
                if (crop != null && !Inside(finished, crop)) run.Problem("La boîte de coupe d'une page dépasse sa boîte de rognage (CropBox).");
                if (media != null && !Inside(finished, media)) run.Problem("La boîte de coupe d'une page dépasse son format (MediaBox).");
            }
            if (bleed != null && media != null && !Inside(bleed, media)) run.Problem("La boîte de fond perdu d'une page dépasse son format (MediaBox).");

            // 6.13 — les annotations : hors de la page imprimée (sauf TrapNet ; les repères d'impression hors de la coupe).
            var annotations = pdf.Get(page, "Annots") as List<object>;
            if (annotations != null)
                foreach (var entry in annotations)
                {
                    var annotation = pdf.Dict(entry);
                    if (annotation == null) continue;
                    var subtype = pdf.Name(annotation, "Subtype");
                    if (subtype == "TrapNet") continue;
                    var rect = Box(run, annotation.ContainsKey("Rect") ? annotation["Rect"] : null);
                    var keepOut = subtype == "PrinterMark" ? finished : (bleed ?? finished);
                    if (rect == null || keepOut == null || Overlaps(rect, keepOut))
                        run.Problem(subtype == "Link" ? "Le fichier porte des annotations (liens cliquables) : PDF/X-1a les interdit dans la page." : "Une annotation (" + (subtype ?? "sans sorte") + ") se trouve dans la page : PDF/X-1a ne les admet qu'en dehors.");
                    if (annotation.ContainsKey("A") || annotation.ContainsKey("AA")) CheckAction(run, annotation);
                }
            // 6.14 — pas d'action sur la page ; 6.16 — pas de groupe de transparence.
            if (page.ContainsKey("AA")) run.Problem("Une page porte des actions automatiques (AA), que PDF/X interdit.");
            TransparencyGroup(run, page);

            var resources = pdf.Dict(inherited.ContainsKey("Resources") ? inherited["Resources"] : null);
            Resources(run, resources, 0);
            // Les flux de contenu de la page.
            var contents = pdf.Resolve(page.ContainsKey("Contents") ? page["Contents"] : null);
            var one = contents as PdfStream;
            if (one != null) Content(run, one, resources);
            var many = contents as List<object>;
            if (many != null) foreach (var part in many) Content(run, pdf.Resolve(part) as PdfStream, resources);
        }

        private static void CheckAction(Run run, Dictionary<string, object> holder)
        {
            var action = run.Pdf.Dict(holder.ContainsKey("A") ? holder["A"] : null);
            if (action != null && run.Pdf.Name(action, "S") == "JavaScript") run.Problem("Le document contient du JavaScript.");
        }

        private static void TransparencyGroup(Run run, Dictionary<string, object> holder)
        {
            var group = run.Pdf.Dict(holder.ContainsKey("Group") ? holder["Group"] : null);
            if (group != null && run.Pdf.Name(group, "S") == "Transparency") run.Problem("Le fichier emploie la transparence, que PDF/X-1a interdit.");
        }

        // ------------------------------------------------------------ les ressources

        private static void Resources(Run run, Dictionary<string, object> resources, int depth)
        {
            var pdf = run.Pdf;
            if (resources == null || depth > 12 || !run.Seen.Add(resources)) return;

            var spaces = pdf.Dict(resources.ContainsKey("ColorSpace") ? resources["ColorSpace"] : null);
            if (spaces != null) foreach (var pair in spaces) ColorSpace(run, pair.Value, 0);

            var states = pdf.Dict(resources.ContainsKey("ExtGState") ? resources["ExtGState"] : null);
            if (states != null) foreach (var pair in states) GraphicsState(run, pdf.Dict(pair.Value));

            var fonts = pdf.Dict(resources.ContainsKey("Font") ? resources["Font"] : null);
            if (fonts != null) foreach (var pair in fonts) Font(run, pdf.Dict(pair.Value), depth);

            var patterns = pdf.Dict(resources.ContainsKey("Pattern") ? resources["Pattern"] : null);
            if (patterns != null)
                foreach (var pair in patterns)
                {
                    var resolved = pdf.Resolve(pair.Value);
                    var pattern = pdf.Dict(resolved);
                    if (pattern == null || !run.Seen.Add(pattern)) continue;
                    var shading = pdf.Dict(pattern.ContainsKey("Shading") ? pattern["Shading"] : null);
                    if (shading != null && shading.ContainsKey("ColorSpace")) ColorSpace(run, shading["ColorSpace"], 0);
                    var state = pdf.Dict(pattern.ContainsKey("ExtGState") ? pattern["ExtGState"] : null);
                    if (state != null) GraphicsState(run, state);
                    var own = pdf.Dict(pattern.ContainsKey("Resources") ? pattern["Resources"] : null);
                    Resources(run, own, depth + 1);
                    var stream = resolved as PdfStream;
                    if (stream != null) Content(run, stream, own ?? resources);
                }

            var shadings = pdf.Dict(resources.ContainsKey("Shading") ? resources["Shading"] : null);
            if (shadings != null)
                foreach (var pair in shadings)
                {
                    var shading = pdf.Dict(pair.Value);
                    if (shading != null && shading.ContainsKey("ColorSpace")) ColorSpace(run, shading["ColorSpace"], 0);
                }

            var objects = pdf.Dict(resources.ContainsKey("XObject") ? resources["XObject"] : null);
            if (objects != null)
                foreach (var pair in objects)
                {
                    var stream = pdf.Resolve(pair.Value) as PdfStream;
                    if (stream == null) continue;
                    var subtype = pdf.Name(stream.Dict, "Subtype");
                    if (subtype == "Image") Image(run, stream);
                    else if (subtype == "PS" || pdf.Name(stream.Dict, "Subtype2") == "PS") run.Problem("Le fichier contient du PostScript incorporé, que PDF/X interdit."); // 6.10
                    else if (subtype == "Form")
                    {
                        if (!run.Seen.Add(stream)) continue;
                        if (stream.Dict.ContainsKey("Ref")) run.Problem("Une page fait appel à un PDF externe (XObject de référence), que PDF/X-1a interdit."); // 6.4
                        if (stream.Dict.ContainsKey("OPI")) run.Problem("Le fichier porte des renvois OPI, que PDF/X-1a:2003 interdit.");
                        TransparencyGroup(run, stream.Dict);
                        var own = pdf.Dict(stream.Dict.ContainsKey("Resources") ? stream.Dict["Resources"] : null);
                        Resources(run, own, depth + 1);
                        Content(run, stream, own ?? resources);
                    }
                }
        }

        /// <summary>6.2 — les espaces de couleur admis : DeviceCMYK, DeviceGray,
        /// Separation et DeviceN (repliés sur du CMJN ou du gris), Indexed et
        /// Pattern sur ces bases. Ni RVB, ni Lab, ni espace calibré ou à profil.</summary>
        private static void ColorSpace(Run run, object value, int depth)
        {
            var pdf = run.Pdf;
            if (depth > 8) return;
            var resolved = pdf.Resolve(value);
            var name = resolved as PdfName;
            var list = resolved as List<object>;
            var family = name != null ? name.Value : list != null && list.Count > 0 && pdf.Resolve(list[0]) is PdfName ? ((PdfName)pdf.Resolve(list[0])).Value : null;
            switch (family)
            {
                case "DeviceCMYK": case "CMYK": case "DeviceGray": case "G": case null:
                    return;
                case "DeviceRGB": case "RGB": case "CalRGB":
                    run.Problem("Une image ou une couleur est en RVB : PDF/X-1a ne veut que du CMJN, du gris ou des tons directs.");
                    return;
                case "Lab":
                    run.Problem("Une couleur est exprimée en Lab : PDF/X-1a ne veut que du CMJN, du gris ou des tons directs.");
                    return;
                case "ICCBased":
                    run.Problem("Une image ou une couleur porte son propre profil ICC : PDF/X-1a ne l'admet pas (tout doit être en CMJN, en gris ou en ton direct).");
                    return;
                case "CalGray":
                    run.Problem("Une couleur est en gris calibré (CalGray) : PDF/X-1a ne veut que du gris simple.");
                    return;
                case "Indexed": case "I":
                    if (list != null && list.Count > 1) ColorSpace(run, list[1], depth + 1);
                    return;
                case "Pattern":
                    if (list != null && list.Count > 1) ColorSpace(run, list[1], depth + 1);
                    return;
                case "Separation":
                    if (list != null && list.Count > 2) Alternate(run, list[2]);
                    return;
                case "DeviceN":
                    if (list != null && list.Count > 2) Alternate(run, list[2]);
                    return;
                default:
                    run.Note("Un espace de couleur inconnu (« " + family + " ») n'a pas été jugé.");
                    return;
            }
        }

        private static void Alternate(Run run, object value)
        {
            var name = run.Pdf.Resolve(value) as PdfName;
            if (name == null || (name.Value != "DeviceCMYK" && name.Value != "DeviceGray"))
                run.Problem("Un ton direct se replie sur autre chose que du CMJN ou du gris : PDF/X-1a ne l'admet pas.");
        }

        /// <summary>6.9 et 6.16 — l'état graphique : aucune transparence, ni
        /// courbe de transfert, ni trame hors des types simples.</summary>
        private static void GraphicsState(Run run, Dictionary<string, object> state)
        {
            var pdf = run.Pdf;
            if (state == null || !run.Seen.Add(state)) return;
            var transparent = false;
            if (state.ContainsKey("SMask")) { var mask = pdf.Resolve(state["SMask"]) as PdfName; if (mask == null || mask.Value != "None") transparent = true; }
            foreach (var key in new[] { "CA", "ca" })
            {
                var alpha = pdf.Get(state, key);
                if (alpha is double && (double)alpha < 0.9999) transparent = true;
            }
            var blend = pdf.Get(state, "BM");
            var blendName = blend as PdfName;
            if (blend is List<object> && ((List<object>)blend).Count > 0) blendName = pdf.Resolve(((List<object>)blend)[0]) as PdfName;
            if (blendName != null && blendName.Value != "Normal" && blendName.Value != "Compatible") transparent = true;
            if (transparent) run.Problem("Le fichier emploie la transparence, que PDF/X-1a interdit.");
            if (state.ContainsKey("TR")) run.Problem("Un état graphique porte une courbe de transfert (TR), que PDF/X interdit.");
            if (state.ContainsKey("TR2")) { var transfer = pdf.Resolve(state["TR2"]) as PdfName; if (transfer == null || transfer.Value != "Default") run.Problem("Un état graphique porte une courbe de transfert (TR2), que PDF/X interdit."); }
            if (state.ContainsKey("HTP")) run.Problem("Un état graphique déplace l'origine de la trame (HTP), ce que PDF/X interdit.");
            var halftone = pdf.Dict(state.ContainsKey("HT") ? state["HT"] : null);
            if (halftone != null)
            {
                var type = pdf.Get(halftone, "HalftoneType");
                if (type is double && (int)(double)type != 1 && (int)(double)type != 5) run.Problem("Une trame d'un type que PDF/X n'admet pas est demandée (seuls les types 1 et 5 le sont).");
                if (halftone.ContainsKey("HalftoneName")) run.Problem("Une trame est demandée par son nom (HalftoneName), ce que PDF/X interdit.");
            }
        }

        /// <summary>6.3 — toute police est incorporée (un sous-ensemble suffit).</summary>
        private static void Font(Run run, Dictionary<string, object> font, int depth)
        {
            var pdf = run.Pdf;
            if (font == null || !run.SeenFonts.Add(font)) return;
            run.Report.Fonts++;
            var subtype = pdf.Name(font, "Subtype");
            var name = pdf.Name(font, "BaseFont") ?? pdf.Name(font, "Name") ?? "sans nom";
            if (subtype == "Type3")
            {
                // Dessinée dans le fichier même : rien à incorporer, mais ses dessins se contrôlent.
                var own = pdf.Dict(font.ContainsKey("Resources") ? font["Resources"] : null);
                Resources(run, own, depth + 1);
                var glyphs = pdf.Dict(font.ContainsKey("CharProcs") ? font["CharProcs"] : null);
                if (glyphs != null) foreach (var pair in glyphs) Content(run, pdf.Resolve(pair.Value) as PdfStream, own);
                return;
            }
            var described = font;
            if (subtype == "Type0")
            {
                var descendants = pdf.Get(font, "DescendantFonts") as List<object>;
                described = descendants == null || descendants.Count == 0 ? null : pdf.Dict(descendants[0]);
            }
            var descriptor = described == null ? null : pdf.Dict(described.ContainsKey("FontDescriptor") ? described["FontDescriptor"] : null);
            var embedded = descriptor != null && (pdf.Resolve(descriptor.ContainsKey("FontFile") ? descriptor["FontFile"] : null) is PdfStream
                || pdf.Resolve(descriptor.ContainsKey("FontFile2") ? descriptor["FontFile2"] : null) is PdfStream
                || pdf.Resolve(descriptor.ContainsKey("FontFile3") ? descriptor["FontFile3"] : null) is PdfStream);
            if (!embedded) run.Problem("Toutes les polices ne sont pas incorporées (« " + name + " »).");
        }

        /// <summary>6.2, 6.5, 6.12, 6.16 — une image : sa couleur, sa
        /// compression, pas de masque de transparence, pas de 16 bits.</summary>
        private static void Image(Run run, PdfStream image)
        {
            var pdf = run.Pdf;
            if (!run.SeenImages.Add(image)) return;
            run.Report.Images++;
            var dict = image.Dict;
            var mask = pdf.Get(dict, "ImageMask");
            if (!(mask is bool && (bool)mask) && dict.ContainsKey("ColorSpace")) ColorSpace(run, dict["ColorSpace"], 0);
            if (dict.ContainsKey("SMask")) { var soft = pdf.Resolve(dict["SMask"]) as PdfName; if (soft == null || soft.Value != "None") run.Problem("Le fichier emploie la transparence, que PDF/X-1a interdit."); }
            var bits = pdf.Get(dict, "BitsPerComponent");
            if (bits is double && (double)bits > 8) run.Problem("Une image est en 16 bits par composante : PDF/X-1a:2003 s'arrête à 8.");
            if (dict.ContainsKey("OPI")) run.Problem("Le fichier porte des renvois OPI, que PDF/X-1a:2003 interdit.");
            var alternates = pdf.Get(dict, "Alternates") as List<object>;
            if (alternates != null)
                foreach (var entry in alternates)
                {
                    var alternate = pdf.Dict(entry);
                    var printing = alternate == null ? null : pdf.Get(alternate, "DefaultForPrinting");
                    if (printing is bool && (bool)printing) run.Problem("Une image de remplacement est désignée pour l'impression, ce que PDF/X interdit.");
                }
        }

        // ------------------------------------------------------------ les flux de contenu

        private static readonly HashSet<string> ForbiddenFilters = new HashSet<string>(StringComparer.Ordinal) { "LZWDecode", "LZW", "JBIG2Decode", "JPXDecode" };

        private static string FilterProblem(string filter)
        {
            return filter == "LZWDecode" || filter == "LZW" ? "Un flux est compressé en LZW, que PDF/X interdit."
                : filter == "JBIG2Decode" ? "Une image est compressée en JBIG2, que PDF/X-1a:2003 interdit."
                : "Une image est en JPEG 2000, que PDF/X-1a:2003 n'admet pas.";
        }

        private static void Content(Run run, PdfStream stream, Dictionary<string, object> resources)
        {
            var pdf = run.Pdf;
            if (stream == null) return;
            var bytes = pdf.Decode(stream);
            if (bytes == null)
            {
                foreach (var filter in pdf.Filters(stream.Dict)) if (ForbiddenFilters.Contains(filter)) return; // déjà relevé par le tour du fichier
                run.Note("Le contenu d'une page n'a pas pu être décompressé : ses couleurs n'ont pas été vérifiées.");
                return;
            }
            var spaces = resources == null ? null : pdf.Dict(resources.ContainsKey("ColorSpace") ? resources["ColorSpace"] : null);
            var lexer = new PdfLexer(bytes, 0, false);
            var operands = new List<object>();
            var guard = 0;
            while (!lexer.AtEnd && guard++ < 20000000)
            {
                var before = lexer.Position;
                var token = lexer.Read();
                if (lexer.Position == before) lexer.Position++;
                var keyword = token as PdfKeyword;
                if (keyword == null) { if (operands.Count < 64) operands.Add(token); continue; }
                switch (keyword.Value)
                {
                    case "rg": case "RG":
                        run.Problem("Une page est dessinée avec des couleurs RVB.");
                        break;
                    case "cs": case "CS":
                    {
                        var name = operands.Count > 0 ? operands[operands.Count - 1] as PdfName : null;
                        if (name == null) break;
                        if (name.Value == "DeviceRGB") run.Problem("Une page est dessinée avec des couleurs RVB.");
                        else if (name.Value != "DeviceCMYK" && name.Value != "DeviceGray" && name.Value != "Pattern" && (spaces == null || !spaces.ContainsKey(name.Value)))
                            run.Note("Une page appelle un espace de couleur (« " + name.Value + " ») que ses ressources ne déclarent pas.");
                        break;
                    }
                    case "PS":
                        run.Problem("Le fichier contient du PostScript incorporé, que PDF/X interdit.");
                        break;
                    case "BI":
                        InlineImage(run, lexer, bytes);
                        break;
                }
                operands.Clear();
            }
        }

        /// <summary>Une image écrite dans le flux même (BI … ID … EI) : sa
        /// couleur et son filtre se lisent dans son en-tête, puis on saute ses octets.</summary>
        private static void InlineImage(Run run, PdfLexer lexer, byte[] bytes)
        {
            run.Report.Images++;
            while (!lexer.AtEnd)
            {
                var before = lexer.Position;
                var token = lexer.Read();
                if (lexer.Position == before) { lexer.Position++; continue; }
                var word = token as PdfKeyword;
                if (word != null && word.Value == "ID") break;
                var key = token as PdfName;
                if (key == null) continue;
                var value = lexer.Read();
                if (key.Value == "CS" || key.Value == "ColorSpace") ColorSpace(run, value, 0);
                if (key.Value == "F" || key.Value == "Filter")
                {
                    var names = new List<object>();
                    if (value is List<object>) names.AddRange((List<object>)value); else names.Add(value);
                    foreach (var entry in names) { var name = entry as PdfName; if (name != null && ForbiddenFilters.Contains(name.Value)) run.Problem(FilterProblem(name.Value)); }
                }
                if ((key.Value == "BPC" || key.Value == "BitsPerComponent") && value is double && (double)value > 8) run.Problem("Une image est en 16 bits par composante : PDF/X-1a:2003 s'arrête à 8.");
            }
            // Les octets de l'image : jusqu'au « EI » isolé qui suit.
            var at = lexer.Position;
            while (at + 2 < bytes.Length)
            {
                if (bytes[at] == 'E' && bytes[at + 1] == 'I' && (at == 0 || bytes[at - 1] <= 32) && (at + 2 >= bytes.Length || bytes[at + 2] <= 32)) break;
                at++;
            }
            lexer.Position = Math.Min(bytes.Length, at + 2);
        }

        // ------------------------------------------------------------ le tour du fichier

        /// <summary>Ce qui se juge sur tout objet, relié ou non à une page :
        /// les compressions interdites, le JavaScript, les fichiers joints.</summary>
        private static void WholeFile(Run run)
        {
            var pdf = run.Pdf;
            foreach (var pair in pdf.Objects)
            {
                var stream = pair.Value as PdfStream;
                var dict = stream != null ? stream.Dict : pair.Value as Dictionary<string, object>;
                if (dict == null) continue;
                if (stream != null)
                {
                    run.Report.Streams++;
                    foreach (var filter in pdf.Filters(dict)) if (ForbiddenFilters.Contains(filter)) run.Problem(FilterProblem(filter)); // 6.5
                }
                var kind = pdf.Name(dict, "S");
                if (kind == "JavaScript" || dict.ContainsKey("JS")) run.Problem("Le document contient du JavaScript."); // 6.14
                var type = pdf.Name(dict, "Type");
                if (type == "EmbeddedFile" || (type == "Filespec" && dict.ContainsKey("EF"))) run.Problem("Le document contient des fichiers joints."); // 6.4
            }
        }
    }
}

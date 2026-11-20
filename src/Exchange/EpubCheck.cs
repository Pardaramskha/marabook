using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Marabook.Exchange
{
    /// <summary>LE CONTRÔLE D'UN EPUB (20/11/2026) : ce que les librairies en
    /// ligne refusent le plus souvent, vérifié sur le fichier écrit — le
    /// « mimetype » en tête et non compressé, le conteneur, le paquet (titre,
    /// langue, identifiant, date de modification), le manifeste (tout fichier
    /// déclaré, tout déclaré présent, identifiants uniques), l'ordre de
    /// lecture, la table de navigation, le XML de chaque page, et chaque lien
    /// ou image qui doit mener quelque part. Un contrôle maison : il ne
    /// remplace pas EPUBCheck, l'outil officiel, mais attrape l'essentiel
    /// sans rien installer.</summary>
    public static class EpubCheck
    {
        private const string OpfNs = "http://www.idpf.org/2007/opf";
        private const string DcNs = "http://purl.org/dc/elements/1.1/";
        private const string OpsNs = "http://www.idpf.org/2007/ops";
        private const string ContainerNs = "urn:oasis:names:tc:opendocument:xmlns:container";

        private sealed class ManifestItem { public string Id, Href, Path, MediaType, Properties; }

        /// <summary>Les défauts du fichier, en clair ; liste vide = rien à redire.</summary>
        public static List<string> Verify(string path)
        {
            var problems = new List<string>();
            try
            {
                var bytes = File.ReadAllBytes(path);
                Mimetype(bytes, problems);
                using (var stream = new MemoryStream(bytes))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                    Package(zip, problems);
            }
            catch (Exception error)
            {
                problems.Add("Le fichier ne se lit pas comme un EPUB : " + error.Message);
            }
            return problems;
        }

        /// <summary>La première entrée du zip : « mimetype », non compressée, sans champ en plus.</summary>
        private static void Mimetype(byte[] b, List<string> problems)
        {
            const string expected = "application/epub+zip";
            if (b.Length < 30 + 8 + expected.Length || b[0] != 0x50 || b[1] != 0x4B || b[2] != 3 || b[3] != 4)
            {
                problems.Add("Ce n'est pas une archive zip.");
                return;
            }
            var method = b[8] | (b[9] << 8);
            var nameLength = b[26] | (b[27] << 8);
            var extraLength = b[28] | (b[29] << 8);
            var name = Encoding.ASCII.GetString(b, 30, Math.Min(nameLength, b.Length - 30));
            if (name != "mimetype") { problems.Add("Le premier fichier de l'archive doit être « mimetype » (trouvé : « " + name + " »)."); return; }
            if (method != 0) problems.Add("Le fichier « mimetype » doit être rangé sans compression.");
            if (extraLength != 0) problems.Add("Le fichier « mimetype » ne doit porter aucun champ supplémentaire.");
            var at = 30 + nameLength + extraLength;
            if (method == 0 && (at + expected.Length > b.Length || Encoding.ASCII.GetString(b, at, expected.Length) != expected))
                problems.Add("Le fichier « mimetype » doit contenir exactement « " + expected + " ».");
        }

        private static XmlDocument Load(ZipArchive zip, string name, List<string> problems)
        {
            var entry = zip.GetEntry(name);
            if (entry == null) return null;
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using (var stream = entry.Open())
                using (var reader = XmlReader.Create(stream, settings))
                    document.Load(reader);
                return document;
            }
            catch (XmlException error)
            {
                problems.Add("« " + name + " » n'est pas un XML bien formé (ligne " + error.LineNumber + ") : " + error.Message);
                return null;
            }
        }

        private static string Resolve(string folder, string href)
        {
            href = Uri.UnescapeDataString(href ?? "");
            var parts = new List<string>();
            foreach (var part in (folder.Length > 0 ? folder + "/" + href : href).Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(part);
            }
            return string.Join("/", parts.ToArray());
        }

        private static string FolderOf(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? "" : path.Substring(0, slash);
        }

        private static void Package(ZipArchive zip, List<string> problems)
        {
            var names = new HashSet<string>();
            foreach (var entry in zip.Entries) if (!entry.FullName.EndsWith("/", StringComparison.Ordinal)) names.Add(entry.FullName);

            // ---- le conteneur
            if (!names.Contains("META-INF/container.xml")) { problems.Add("Il manque « META-INF/container.xml »."); return; }
            var container = Load(zip, "META-INF/container.xml", problems);
            if (container == null) return;
            var ns = new XmlNamespaceManager(container.NameTable);
            ns.AddNamespace("c", ContainerNs);
            var rootfile = container.SelectSingleNode("//c:rootfile", ns) as XmlElement;
            var opfPath = rootfile == null ? "" : rootfile.GetAttribute("full-path");
            if (opfPath.Length == 0) { problems.Add("Le conteneur ne désigne aucun paquet (rootfile)."); return; }
            if (!names.Contains(opfPath)) { problems.Add("Le paquet « " + opfPath + " » désigné par le conteneur est absent."); return; }

            // ---- le paquet
            var opf = Load(zip, opfPath, problems);
            if (opf == null) return;
            var folder = FolderOf(opfPath);
            ns = new XmlNamespaceManager(opf.NameTable);
            ns.AddNamespace("o", OpfNs);
            ns.AddNamespace("dc", DcNs);
            var package = opf.DocumentElement;
            if (package == null || package.LocalName != "package" || package.NamespaceURI != OpfNs) { problems.Add("Le paquet n'a pas de racine « package »."); return; }
            if (!package.GetAttribute("version").StartsWith("3", StringComparison.Ordinal)) problems.Add("Le paquet ne se déclare pas en EPUB 3 (version « " + package.GetAttribute("version") + " »).");

            var uidRef = package.GetAttribute("unique-identifier");
            string uid = null;
            foreach (XmlElement identifier in opf.SelectNodes("//o:metadata/dc:identifier", ns))
                if (identifier.GetAttribute("id") == uidRef && uidRef.Length > 0) uid = identifier.InnerText.Trim();
            if (string.IsNullOrEmpty(uid)) problems.Add("Le paquet n'a pas d'identifiant unique (dc:identifier désigné par unique-identifier).");
            if (IsEmpty(opf.SelectSingleNode("//o:metadata/dc:title", ns))) problems.Add("Le livre n'a pas de titre (dc:title).");
            if (IsEmpty(opf.SelectSingleNode("//o:metadata/dc:language", ns))) problems.Add("Le livre n'a pas de langue (dc:language).");
            var modified = false;
            foreach (XmlElement meta in opf.SelectNodes("//o:metadata/o:meta", ns))
                if (meta.GetAttribute("property") == "dcterms:modified")
                {
                    DateTime when;
                    modified = DateTime.TryParseExact(meta.InnerText.Trim(), "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out when);
                    if (!modified) problems.Add("La date de modification (dcterms:modified) n'est pas au format AAAA-MM-JJTHH:MM:SSZ.");
                    modified = true;
                }
            if (!modified) problems.Add("Il manque la date de modification (dcterms:modified).");

            // ---- le manifeste
            var items = new Dictionary<string, ManifestItem>();
            var byPath = new Dictionary<string, ManifestItem>();
            var navs = 0;
            var covers = 0;
            foreach (XmlElement node in opf.SelectNodes("//o:manifest/o:item", ns))
            {
                var item = new ManifestItem { Id = node.GetAttribute("id"), Href = node.GetAttribute("href"), MediaType = node.GetAttribute("media-type"), Properties = node.GetAttribute("properties") };
                item.Path = Resolve(folder, item.Href);
                if (item.Id.Length == 0) problems.Add("Un élément du manifeste n'a pas d'identifiant (« " + item.Href + " »).");
                else if (items.ContainsKey(item.Id)) problems.Add("L'identifiant « " + item.Id + " » figure deux fois dans le manifeste.");
                else items[item.Id] = item;
                if (item.MediaType.Length == 0) problems.Add("« " + item.Href + " » n'a pas de type (media-type).");
                if (byPath.ContainsKey(item.Path)) problems.Add("« " + item.Href + " » est déclaré deux fois dans le manifeste.");
                else byPath[item.Path] = item;
                if (!names.Contains(item.Path)) problems.Add("« " + item.Href + " » est déclaré dans le manifeste mais absent de l'archive.");
                var properties = " " + item.Properties + " ";
                if (properties.Contains(" nav ")) navs++;
                if (properties.Contains(" cover-image "))
                {
                    covers++;
                    if (!item.MediaType.StartsWith("image/", StringComparison.Ordinal)) problems.Add("La couverture déclarée (« " + item.Href + " ») n'est pas une image.");
                }
            }
            foreach (var name in names)
            {
                if (name == "mimetype" || name == opfPath || name.StartsWith("META-INF/", StringComparison.Ordinal)) continue;
                if (!byPath.ContainsKey(name)) problems.Add("« " + name + " » est dans l'archive mais n'est pas déclaré dans le manifeste.");
            }
            if (navs == 0) problems.Add("Aucun document de navigation (propriété « nav ») dans le manifeste.");
            else if (navs > 1) problems.Add("Plusieurs documents de navigation sont déclarés : il n'en faut qu'un.");
            if (covers > 1) problems.Add("Plusieurs images sont déclarées comme couverture : il n'en faut qu'une.");

            // ---- l'ordre de lecture
            var spine = opf.SelectSingleNode("//o:spine", ns) as XmlElement;
            var read = 0;
            if (spine != null)
            {
                foreach (XmlElement itemref in spine.SelectNodes("o:itemref", ns))
                {
                    read++;
                    ManifestItem target;
                    var idref = itemref.GetAttribute("idref");
                    if (!items.TryGetValue(idref, out target)) problems.Add("L'ordre de lecture cite « " + idref + " », qui n'est pas dans le manifeste.");
                    else if (target.MediaType != "application/xhtml+xml") problems.Add("L'ordre de lecture cite « " + target.Href + " », qui n'est pas une page.");
                }
                var toc = spine.GetAttribute("toc");
                ManifestItem ncx;
                if (toc.Length > 0 && (!items.TryGetValue(toc, out ncx) || ncx.MediaType != "application/x-dtbncx+xml"))
                    problems.Add("La table NCX citée par l'ordre de lecture (« " + toc + " ») est introuvable.");
            }
            if (read == 0) problems.Add("L'ordre de lecture (spine) est vide.");

            // ---- les pages : XML bien formé, identifiants uniques, puis liens et images
            var documents = new Dictionary<string, XmlDocument>();
            var ids = new Dictionary<string, HashSet<string>>();
            foreach (var item in byPath.Values)
            {
                if (item.MediaType != "application/xhtml+xml" && item.MediaType != "application/x-dtbncx+xml") continue;
                if (!names.Contains(item.Path)) continue;
                var document = Load(zip, item.Path, problems);
                if (document == null) continue;
                documents[item.Path] = document;
                var found = new HashSet<string>();
                foreach (XmlElement element in document.SelectNodes("//*[@id]"))
                    if (!found.Add(element.GetAttribute("id"))) problems.Add("« " + item.Href + " » : l'identifiant « " + element.GetAttribute("id") + " » y figure deux fois.");
                ids[item.Path] = found;
            }
            foreach (var item in byPath.Values)
            {
                XmlDocument document;
                if (!documents.TryGetValue(item.Path, out document)) continue;
                var here = FolderOf(item.Path);
                if (item.MediaType == "application/x-dtbncx+xml")
                {
                    var ncxNs = new XmlNamespaceManager(document.NameTable);
                    ncxNs.AddNamespace("n", "http://www.daisy.org/z3986/2005/ncx/");
                    var declared = document.SelectSingleNode("//n:head/n:meta[@name='dtb:uid']", ncxNs) as XmlElement;
                    if (declared != null && uid != null && declared.GetAttribute("content") != uid) problems.Add("L'identifiant de la table NCX diffère de celui du paquet.");
                    foreach (XmlElement content in document.SelectNodes("//n:content", ncxNs))
                        Target(content.GetAttribute("src"), here, item, names, ids, problems);
                    continue;
                }
                var root = document.DocumentElement;
                if (root == null || root.LocalName != "html" || root.NamespaceURI != "http://www.w3.org/1999/xhtml")
                    problems.Add("« " + item.Href + " » n'est pas une page XHTML (racine « html » attendue).");
                foreach (XmlElement link in document.SelectNodes("//*[local-name()='a'][@href]"))
                    Target(link.GetAttribute("href"), here, item, names, ids, problems);
                foreach (XmlElement image in document.SelectNodes("//*[local-name()='img']"))
                {
                    var src = image.GetAttribute("src");
                    if (src.Length == 0) problems.Add("« " + item.Href + " » : une image n'a pas de source.");
                    else
                    {
                        var target = Resolve(here, src);
                        if (!names.Contains(target)) problems.Add("« " + item.Href + " » : l'image « " + src + " » est introuvable.");
                        else if (!byPath.ContainsKey(target)) problems.Add("« " + item.Href + " » : l'image « " + src + " » n'est pas déclarée dans le manifeste.");
                    }
                    if (!image.HasAttribute("alt")) problems.Add("« " + item.Href + " » : une image n'a pas de texte de remplacement (alt).");
                }
                foreach (XmlElement sheet in document.SelectNodes("//*[local-name()='link'][@href]"))
                    if (!names.Contains(Resolve(here, sheet.GetAttribute("href")))) problems.Add("« " + item.Href + " » : la feuille de styles « " + sheet.GetAttribute("href") + " » est introuvable.");
                if ((" " + item.Properties + " ").Contains(" nav "))
                {
                    var hasToc = false;
                    foreach (XmlElement nav in document.SelectNodes("//*[local-name()='nav']"))
                        if (nav.GetAttribute("type", OpsNs) == "toc") hasToc = nav.SelectNodes(".//*[local-name()='a'][@href]").Count > 0;
                    if (!hasToc) problems.Add("Le document de navigation n'a pas de table des matières (nav epub:type=\"toc\") avec au moins une entrée.");
                }
            }
        }

        /// <summary>Un lien interne doit mener à un fichier de l'archive, et son ancre à un identifiant qui existe.</summary>
        private static void Target(string href, string here, ManifestItem from, HashSet<string> names, Dictionary<string, HashSet<string>> ids, List<string> problems)
        {
            if (string.IsNullOrEmpty(href)) return;
            if (href.Contains("://") || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return; // un lien vers l'extérieur
            var hash = href.IndexOf('#');
            var file = hash < 0 ? href : href.Substring(0, hash);
            var anchor = hash < 0 ? "" : href.Substring(hash + 1);
            var target = file.Length == 0 ? from.Path : Resolve(here, file);
            if (!names.Contains(target)) { problems.Add("« " + from.Href + " » : le lien « " + href + " » ne mène nulle part."); return; }
            HashSet<string> known;
            if (anchor.Length > 0 && ids.TryGetValue(target, out known) && !known.Contains(anchor))
                problems.Add("« " + from.Href + " » : l'ancre « #" + anchor + " » n'existe pas dans « " + target + " ».");
        }

        private static bool IsEmpty(XmlNode node)
        {
            return node == null || node.InnerText.Trim().Length == 0;
        }
    }
}

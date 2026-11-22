using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Marabook.Exchange;
using Marabook.Model;

namespace Marabook.App
{
    /// <summary>LE PONT VERS PDFPIG (21/11/2026). Marabook ne référence pas
    /// PdfPig à la compilation : la bibliothèque est téléchargée à la demande
    /// (PdfPigPackage dit d'où, et vérifie quoi), chargée dans son propre
    /// contexte, et appelée par son nom. Ce pont fait trois choses : dire si
    /// elle est installée, l'installer, et lire les mots d'un PDF.</summary>
    public static class PdfPigBridge
    {
        private static Assembly _assembly;
        private static string _loadedFrom;

        /// <summary>Sonde : un dossier d'essai, pour ne rien installer chez l'auteur.</summary>
        public static string FolderOverride;

        public static string Folder { get { return FolderOverride ?? PdfPigPackage.FolderIn(Platform.Current.DataFolder); } }

        /// <summary>Installée ET intacte (chaque fichier a son empreinte) ?</summary>
        public static bool Installed { get { return PdfPigPackage.IsInstalled(Folder); } }

        /// <summary>Télécharge l'archive depuis NuGet, la vérifie, installe ses
        /// bibliothèques. null si c'est fait, sinon ce qui a manqué.</summary>
        public static async Task<string> DownloadAndInstall()
        {
            byte[] package;
            try
            {
                var uri = new Uri(PdfPigPackage.Url);
                if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, PdfPigPackage.Host, StringComparison.OrdinalIgnoreCase)) return "La source de PdfPig n'est pas celle attendue.";
                // Aucune redirection suivie hors de l'hôte attendu ; rien du projet ni de l'auteur ne part : une simple lecture.
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Marabook");
                    using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) return "NuGet a répondu « " + (int)response.StatusCode + " » : PdfPig n'a pas pu être téléchargée.";
                        var announced = response.Content.Headers.ContentLength;
                        if (announced.HasValue && announced.Value != PdfPigPackage.PackageSize) return "L'archive annoncée n'a pas la taille attendue : elle n'a pas été téléchargée.";
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var buffer = new MemoryStream())
                        {
                            var chunk = new byte[81920];
                            int read;
                            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
                            {
                                buffer.Write(chunk, 0, read);
                                if (buffer.Length > PdfPigPackage.PackageSize) return "L'archive reçue dépasse la taille attendue : elle n'a pas été installée.";
                            }
                            package = buffer.ToArray();
                        }
                    }
                }
            }
            catch (Exception error)
            {
                return "PdfPig n'a pas pu être téléchargée (" + error.Message + "). Vérifiez la connexion, puis réessayez.";
            }
            return PdfPigPackage.Install(package, Folder);
        }

        /// <summary>Installe depuis une archive déjà sur le disque (sonde, hors ligne) — mêmes vérifications.</summary>
        public static string InstallFromFile(string nupkgPath)
        {
            try { return PdfPigPackage.Install(File.ReadAllBytes(nupkgPath), Folder); }
            catch (Exception error) { return "L'archive ne se lit pas : " + error.Message; }
        }

        private sealed class Context : AssemblyLoadContext
        {
            private readonly string _folder;
            public Context(string folder) : base("PdfPig") { _folder = folder; }
            protected override Assembly Load(AssemblyName name)
            {
                // Seules les bibliothèques NOMMÉES et vérifiées se chargent d'ici ; le reste vient de Marabook.
                var file = name.Name + ".dll";
                if (!PdfPigPackage.Files.ContainsKey(file)) return null;
                var path = Path.Combine(_folder, file);
                return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
            }
        }

        private static Assembly Load()
        {
            var folder = Folder;
            if (_assembly != null && _loadedFrom == folder) return _assembly;
            if (!PdfPigPackage.IsInstalled(folder)) throw new InvalidOperationException("PdfPig n'est pas installée, ou l'un de ses fichiers a changé : relancez l'import pour la réinstaller.");
            var context = new Context(folder);
            _assembly = context.LoadFromAssemblyPath(Path.Combine(folder, PdfPigPackage.MainAssembly));
            _loadedFrom = folder;
            return _assembly;
        }

        /// <summary>Les mots d'un PDF, page par page (boîte, corps, graisse devinée du nom de la police).</summary>
        public static List<PdfPageWords> ReadWords(string path)
        {
            var assembly = Load();
            var documentType = assembly.GetType("UglyToad.PdfPig.PdfDocument", true);
            MethodInfo open = null;
            foreach (var method in documentType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var parameters = method.GetParameters();
                if (method.Name == "Open" && parameters.Length == 2 && parameters[0].ParameterType == typeof(string)) { open = method; break; }
            }
            if (open == null) throw new InvalidOperationException("Cette version de PdfPig n'a pas la fonction d'ouverture attendue.");
            object opened;
            try { opened = open.Invoke(null, new object[] { path, null }); }
            catch (TargetInvocationException error) { throw new InvalidOperationException("ce PDF ne se lit pas (" + (error.InnerException ?? error).Message + ")"); }
            var pages = new List<PdfPageWords>();
            try
            {
                dynamic document = opened;
                foreach (dynamic page in document.GetPages())
                {
                    var words = new PdfPageWords { Width = (double)page.Width, Height = (double)page.Height };
                    foreach (dynamic word in page.GetWords())
                    {
                        string text = word.Text;
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        var box = word.BoundingBox;
                        var entry = new PdfWord { Text = text.Trim(), Left = (double)box.Left, Bottom = (double)box.Bottom, Right = (double)box.Right, Top = (double)box.Top };
                        try
                        {
                            dynamic letters = word.Letters;
                            if (letters.Count > 0)
                            {
                                dynamic first = letters[0];
                                entry.Size = (double)first.PointSize;
                                entry.Bottom = (double)first.StartBaseLine.Y; // le pied de la ligne, pas le bas d'un « p »
                                bool bold, italic;
                                PdfImport.StyleOfFont((string)first.FontName, out bold, out italic);
                                entry.Bold = bold;
                                entry.Italic = italic;
                            }
                        }
                        catch (Exception) { }
                        if (entry.Size <= 0) entry.Size = Math.Max(1, entry.Top - entry.Bottom);
                        words.Words.Add(entry);
                    }
                    pages.Add(words);
                }
            }
            finally
            {
                var disposable = opened as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
            return pages;
        }

        /// <summary>Un PDF devenu document, et le compte rendu de ce qui a été déduit.</summary>
        public static PdfImport.Result Import(string path)
        {
            return PdfImport.Build(ReadWords(path));
        }
    }
}

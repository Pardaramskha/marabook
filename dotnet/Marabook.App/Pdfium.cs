using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Marabook.Exchange;
using Marabook.Model;
using Marabook.Print;

namespace Marabook.App
{
    /// <summary>LE PONT VERS PDFIUM (2.0.0, 21/11/2026 — l'Atelier). Marabook ne
    /// livre pas PDFium : la bibliothèque native est téléchargée à la demande
    /// (PdfiumPackage dit d'où, et vérifie quoi), chargée par son chemin
    /// VÉRIFIÉ — jamais cherchée dans le système —, et appelée fonction par
    /// fonction. Ce pont installe, charge, et fait quatre choses : ouvrir un
    /// PDF (avec son mot de passe s'il en a un), dessiner une page, recomposer
    /// un PDF à partir de pages d'autres, et en écrire une copie sans verrou.
    /// PDFium n'est pas fait pour plusieurs fils : tout passe sous un verrou.</summary>
    internal static partial class Pdfium
    {
        public const int ErrorPassword = 4;

        /// <summary>Sonde : un dossier d'essai, pour ne rien installer chez l'auteur.</summary>
        public static string FolderOverride;

        public static string Folder { get { return FolderOverride ?? PdfiumPackage.FolderIn(Platform.Current.DataFolder); } }

        /// <summary>« win-x64 », « osx-arm64 »… : le système et le processeur en cours.</summary>
        public static string Rid
        {
            get
            {
                var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
                var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
                return os + "-" + arch;
            }
        }

        public static PdfiumPackage.Target Target { get { return PdfiumPackage.For(Rid); } }

        /// <summary>Prévu pour ce système ?</summary>
        public static bool Supported { get { return Target != null; } }

        /// <summary>Installée ET intacte (le fichier a son empreinte) ?</summary>
        public static bool Installed { get { return PdfiumPackage.IsInstalled(Target, Folder); } }

        // ================================================================ installation

        /// <summary>Télécharge une archive depuis UN hôte, à UNE taille : rien d'autre n'est accepté.
        /// Aucune redirection suivie ; rien du projet ni de l'auteur ne part — une simple lecture.</summary>
        internal static async Task<Tuple<byte[], string>> Fetch(string url, string host, long size, string name)
        {
            try
            {
                var uri = new Uri(url);
                if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)) return Tuple.Create<byte[], string>(null, "La source de " + name + " n'est pas celle attendue.");
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) })
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Marabook");
                    using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) return Tuple.Create<byte[], string>(null, "NuGet a répondu « " + (int)response.StatusCode + " » : " + name + " n'a pas pu être téléchargée.");
                        var announced = response.Content.Headers.ContentLength;
                        if (announced.HasValue && announced.Value != size) return Tuple.Create<byte[], string>(null, "L'archive annoncée n'a pas la taille attendue : elle n'a pas été téléchargée.");
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var buffer = new MemoryStream())
                        {
                            var chunk = new byte[81920];
                            int read;
                            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
                            {
                                buffer.Write(chunk, 0, read);
                                if (buffer.Length > size) return Tuple.Create<byte[], string>(null, "L'archive reçue dépasse la taille attendue : elle n'a pas été installée.");
                            }
                            return Tuple.Create<byte[], string>(buffer.ToArray(), null);
                        }
                    }
                }
            }
            catch (Exception error)
            {
                return Tuple.Create<byte[], string>(null, name + " n'a pas pu être téléchargée (" + error.Message + "). Vérifiez la connexion, puis réessayez.");
            }
        }

        /// <summary>Télécharge l'archive depuis NuGet, la vérifie, installe la bibliothèque. null si c'est fait.</summary>
        public static async Task<string> DownloadAndInstall()
        {
            var target = Target;
            if (target == null) return "PDFium n'est pas prévu pour ce système (" + Rid + ").";
            var fetched = await Fetch(target.Url, PdfiumPackage.Host, target.PackageSize, "PDFium").ConfigureAwait(false);
            if (fetched.Item2 != null) return fetched.Item2;
            return PdfiumPackage.Install(target, fetched.Item1, Folder);
        }

        /// <summary>Installe depuis une archive déjà sur le disque (sonde, hors ligne) — mêmes vérifications.</summary>
        public static string InstallFromFile(string nupkgPath)
        {
            try { return PdfiumPackage.Install(Target, File.ReadAllBytes(nupkgPath), Folder); }
            catch (Exception error) { return "L'archive ne se lit pas : " + error.Message; }
        }

        /// <summary>Désinstalle : la bibliothèque est d'abord rendue au système (sinon son fichier reste tenu).</summary>
        public static bool Remove()
        {
            lock (Gate)
            {
                if (_library != IntPtr.Zero)
                {
                    if (_openDocuments > 0) return false;
                    try { _destroy(); } catch (Exception) { }
                    try { NativeLibrary.Free(_library); } catch (Exception) { }
                    _library = IntPtr.Zero;
                    _loadedFrom = null;
                }
                return PdfiumPackage.Remove(Folder);
            }
        }

        // ================================================================ la bibliothèque native

        private static readonly object Gate = new object();
        private static IntPtr _library;
        private static string _loadedFrom;
        private static int _openDocuments;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void VoidCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate CULong LastErrorCall();
        // SetLastError : sous Windows, PDFium range son code d'erreur dans la « dernière erreur » du fil —
        // que tout appel système fait entre-temps efface. On la relève donc à l'instant du retour.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)] private delegate IntPtr LoadMemCall(IntPtr data, UIntPtr size, byte[] password);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DocCall(IntPtr document);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DocIntCall(IntPtr document);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate CULong DocULongCall(IntPtr document);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PageSizeCall(IntPtr document, int index, out SizeF size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr LoadPageCall(IntPtr document, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PageCall(IntPtr page);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr BitmapCreateCall(int width, int height, int format, IntPtr firstScan, int stride);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BitmapFillCall(IntPtr bitmap, int left, int top, int width, int height, CULong color);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RenderCall(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr BitmapBufferCall(IntPtr bitmap);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BitmapIntCall(IntPtr bitmap);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BitmapCall(IntPtr bitmap);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewDocCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ImportCall(IntPtr destination, IntPtr source, int[] indices, CULong length, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WriteBlockCall(IntPtr self, IntPtr data, CULong size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SaveCall(IntPtr document, IntPtr fileWrite, CULong flags);

        [StructLayout(LayoutKind.Sequential)] private struct SizeF { public float Width, Height; }
        [StructLayout(LayoutKind.Sequential)] private struct FileWrite { public int Version; public IntPtr WriteBlock; }

        private static VoidCall _init, _destroy;
        private static LastErrorCall _lastError;
        private static LoadMemCall _loadMem;
        private static DocCall _closeDocument;
        private static DocIntCall _pageCount, _securityRevision;
        private static DocULongCall _permissions;
        private static PageSizeCall _pageSize;
        private static LoadPageCall _loadPage;
        private static PageCall _closePage;
        private static BitmapCreateCall _bitmapCreate;
        private static BitmapFillCall _bitmapFill;
        private static RenderCall _render;
        private static BitmapBufferCall _bitmapBuffer;
        private static BitmapIntCall _bitmapStride;
        private static BitmapCall _bitmapDestroy;
        private static NewDocCall _newDocument;
        private static ImportCall _import;
        private static SaveCall _save;

        private static T Bind<T>(string name) where T : Delegate
        {
            return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
        }

        /// <summary>Charge la bibliothèque — seulement si son fichier a l'empreinte attendue.</summary>
        private static void EnsureLoaded()
        {
            var folder = Folder;
            if (_library != IntPtr.Zero && _loadedFrom == folder) return;
            var target = Target;
            if (!PdfiumPackage.IsInstalled(target, folder)) throw new InvalidOperationException("PDFium n'est pas installé, ou son fichier a changé : réinstallez-le depuis l'Atelier.");
            if (_library != IntPtr.Zero) throw new InvalidOperationException("PDFium est déjà chargé depuis un autre dossier.");
            _library = NativeLibrary.Load(Path.Combine(folder, target.File));
            _loadedFrom = folder;
            _init = Bind<VoidCall>("FPDF_InitLibrary");
            _destroy = Bind<VoidCall>("FPDF_DestroyLibrary");
            _lastError = Bind<LastErrorCall>("FPDF_GetLastError");
            _loadMem = Bind<LoadMemCall>("FPDF_LoadMemDocument64");
            _closeDocument = Bind<DocCall>("FPDF_CloseDocument");
            _pageCount = Bind<DocIntCall>("FPDF_GetPageCount");
            _securityRevision = Bind<DocIntCall>("FPDF_GetSecurityHandlerRevision");
            _permissions = Bind<DocULongCall>("FPDF_GetDocPermissions");
            _pageSize = Bind<PageSizeCall>("FPDF_GetPageSizeByIndexF");
            _loadPage = Bind<LoadPageCall>("FPDF_LoadPage");
            _closePage = Bind<PageCall>("FPDF_ClosePage");
            _bitmapCreate = Bind<BitmapCreateCall>("FPDFBitmap_CreateEx");
            _bitmapFill = Bind<BitmapFillCall>("FPDFBitmap_FillRect");
            _render = Bind<RenderCall>("FPDF_RenderPageBitmap");
            _bitmapBuffer = Bind<BitmapBufferCall>("FPDFBitmap_GetBuffer");
            _bitmapStride = Bind<BitmapIntCall>("FPDFBitmap_GetStride");
            _bitmapDestroy = Bind<BitmapCall>("FPDFBitmap_Destroy");
            _newDocument = Bind<NewDocCall>("FPDF_CreateNewDocument");
            _import = Bind<ImportCall>("FPDF_ImportPagesByIndex");
            _save = Bind<SaveCall>("FPDF_SaveAsCopy");
            _init();
        }

        // ================================================================ un document ouvert

        /// <summary>Un PDF ouvert par PDFium. Ses octets restent épinglés tant qu'il vit.</summary>
        internal sealed class Document : IDisposable
        {
            internal IntPtr Handle;
            private GCHandle _pin;
            public int PageCount;
            /// <summary>Le fichier est chiffré (un mot de passe d'ouverture, ou de simples restrictions).</summary>
            public bool Encrypted;
            /// <summary>Les droits accordés (bits de la norme PDF) ; tout à 1 si le fichier est libre.</summary>
            public uint Permissions;
            public string Path;

            public bool CanPrint { get { return (Permissions & 4) != 0; } }
            public bool CanModify { get { return (Permissions & 8) != 0; } }
            public bool CanCopy { get { return (Permissions & 16) != 0; } }
            public bool CanAnnotate { get { return (Permissions & 32) != 0; } }

            /// <summary>{ largeur, hauteur } en points, page par page.</summary>
            public List<double[]> Sizes()
            {
                var sizes = new List<double[]>();
                lock (Gate)
                {
                    if (Handle == IntPtr.Zero) return sizes;
                    for (var i = 0; i < PageCount; i++)
                    {
                        SizeF size;
                        if (_pageSize(Handle, i, out size) == 0) size = new SizeF { Width = 595, Height = 842 };
                        sizes.Add(new[] { (double)size.Width, (double)size.Height });
                    }
                }
                return sizes;
            }

            /// <summary>La page dessinée sur blanc, en BGRA (4 octets par point), son plus grand côté à
            /// « longest » points d'écran. null si la page ne s'ouvre pas.</summary>
            public byte[] Render(int index, int longest, out int width, out int height)
            {
                width = 0;
                height = 0;
                lock (Gate)
                {
                    if (Handle == IntPtr.Zero || index < 0 || index >= PageCount) return null;
                    SizeF size;
                    if (_pageSize(Handle, index, out size) == 0 || size.Width <= 0 || size.Height <= 0) return null;
                    var scale = longest / Math.Max(size.Width, size.Height);
                    width = Math.Max(1, (int)Math.Round(size.Width * scale));
                    height = Math.Max(1, (int)Math.Round(size.Height * scale));
                    var page = _loadPage(Handle, index);
                    if (page == IntPtr.Zero) return null;
                    var bitmap = IntPtr.Zero;
                    try
                    {
                        bitmap = _bitmapCreate(width, height, 4, IntPtr.Zero, 0); // FPDFBitmap_BGRA
                        if (bitmap == IntPtr.Zero) return null;
                        _bitmapFill(bitmap, 0, 0, width, height, new CULong(0xFFFFFFFFu));
                        _render(bitmap, page, 0, 0, width, height, 0, 0x01); // FPDF_ANNOT : les annotations aussi
                        var stride = _bitmapStride(bitmap);
                        var buffer = _bitmapBuffer(bitmap);
                        var pixels = new byte[width * height * 4];
                        for (var y = 0; y < height; y++) Marshal.Copy(IntPtr.Add(buffer, y * stride), pixels, y * width * 4, width * 4);
                        return pixels;
                    }
                    finally
                    {
                        if (bitmap != IntPtr.Zero) _bitmapDestroy(bitmap);
                        _closePage(page);
                    }
                }
            }

            public void Dispose()
            {
                lock (Gate)
                {
                    if (Handle != IntPtr.Zero)
                    {
                        _closeDocument(Handle);
                        Handle = IntPtr.Zero;
                        _openDocuments--;
                    }
                    if (_pin.IsAllocated) _pin.Free();
                }
            }

            internal void Pin(GCHandle pin) { _pin = pin; }
        }

        /// <summary>Ouvre un PDF. error = 0 si c'est fait ; ErrorPassword s'il demande un mot de
        /// passe (ou que celui donné n'est pas le bon) ; un autre code de PDFium sinon.</summary>
        public static Document Open(string path, string password, out int error)
        {
            var bytes = File.ReadAllBytes(path);
            var document = Open(bytes, password, out error);
            if (document != null) document.Path = path;
            return document;
        }

        public static Document Open(byte[] bytes, string password, out int error)
        {
            lock (Gate)
            {
                EnsureLoaded();
                var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                var secret = string.IsNullOrEmpty(password) ? null : Terminated(password);
                var handle = _loadMem(pin.AddrOfPinnedObject(), new UIntPtr((ulong)bytes.LongLength), secret);
                if (handle == IntPtr.Zero)
                {
                    var captured = Marshal.GetLastPInvokeError();
                    error = OperatingSystem.IsWindows() ? captured : (int)_lastError().Value;
                    if (error <= 0 || error > 8) error = (int)_lastError().Value;
                    if (error <= 0 || error > 8) error = 1;
                    pin.Free();
                    return null;
                }
                error = 0;
                _openDocuments++;
                var document = new Document { Handle = handle };
                document.Pin(pin);
                document.PageCount = Math.Max(0, _pageCount(handle));
                document.Encrypted = _securityRevision(handle) >= 0;
                document.Permissions = (uint)_permissions(handle).Value;
                return document;
            }
        }

        private static byte[] Terminated(string text)
        {
            var raw = Encoding.UTF8.GetBytes(text);
            var bytes = new byte[raw.Length + 1];
            Array.Copy(raw, bytes, raw.Length);
            return bytes;
        }

        public static string ErrorText(int error)
        {
            switch (error)
            {
                case 2: return "le fichier ne s'ouvre pas";
                case 3: return "ce n'est pas un PDF, ou il est abîmé";
                case ErrorPassword: return "il demande un mot de passe";
                case 5: return "son verrou est d'une sorte que PDFium ne connaît pas";
                default: return "il ne se lit pas";
            }
        }

        // ================================================================ écrire

        private static byte[] Save(IntPtr document, uint flags)
        {
            var output = new MemoryStream();
            WriteBlockCall write = delegate(IntPtr self, IntPtr data, CULong size)
            {
                var length = (int)size.Value;
                if (length <= 0) return 1;
                var chunk = new byte[length];
                Marshal.Copy(data, chunk, 0, length);
                output.Write(chunk, 0, length);
                return 1;
            };
            var block = new FileWrite { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(write) };
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<FileWrite>());
            try
            {
                Marshal.StructureToPtr(block, memory, false);
                var done = _save(document, memory, new CULong(flags));
                GC.KeepAlive(write);
                return done == 0 ? null : output.ToArray();
            }
            finally { Marshal.FreeHGlobal(memory); }
        }

        /// <summary>Le PDF recomposé selon le plan : les pages voulues, tirées des fichiers ouverts
        /// (un par source du plan), dans l'ordre voulu. Les fichiers d'origine ne sont pas touchés.</summary>
        public static byte[] Assemble(PdfAssembly plan, IList<Document> sources)
        {
            lock (Gate)
            {
                EnsureLoaded();
                var destination = _newDocument();
                if (destination == IntPtr.Zero) throw new InvalidOperationException("PDFium n'a pas pu créer le nouveau document.");
                try
                {
                    var at = 0;
                    foreach (var run in plan.Runs())
                    {
                        var source = run.Source >= 0 && run.Source < sources.Count ? sources[run.Source] : null;
                        if (source == null || source.Handle == IntPtr.Zero) throw new InvalidOperationException("un des fichiers d'origine n'est plus ouvert.");
                        var indices = run.Pages.ToArray();
                        if (_import(destination, source.Handle, indices, new CULong((uint)indices.Length), at) == 0)
                            throw new InvalidOperationException("des pages de « " + System.IO.Path.GetFileName(source.Path ?? "") + " » n'ont pas pu être recopiées.");
                        at += indices.Length;
                    }
                    var bytes = Save(destination, 2); // FPDF_NO_INCREMENTAL
                    if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("le nouveau PDF n'a pas pu être écrit.");
                    return bytes;
                }
                finally { _closeDocument(destination); }
            }
        }

        /// <summary>Une copie SANS verrou d'un PDF ouvert (donc d'un PDF qu'on a pu ouvrir : libre
        /// d'accès, ou dont on a donné le mot de passe). Le fichier d'origine n'est pas touché.
        /// La copie est rouverte pour vérifier qu'elle est bien libre.</summary>
        public static byte[] Unlock(Document document)
        {
            byte[] bytes;
            lock (Gate)
            {
                EnsureLoaded();
                if (document == null || document.Handle == IntPtr.Zero) throw new InvalidOperationException("le fichier n'est plus ouvert.");
                bytes = Save(document.Handle, 3); // FPDF_REMOVE_SECURITY (= 3 : une valeur, pas un bit — « 2 | 4 » sortait de la plage et ne retirait rien, 22/11)
            }
            if (bytes != null && bytes.Length > 0 && IsFree(bytes, document.PageCount)) return bytes;
            // La copie garde son verrou (ou ne se relit pas) : on recopie les pages dans un document neuf.
            var plan = new PdfAssembly();
            plan.Insert(document.Path ?? "", document.Sizes(), -1);
            bytes = Assemble(plan, new[] { document });
            if (!IsFree(bytes, document.PageCount)) throw new InvalidOperationException("la copie déverrouillée ne se relit pas correctement.");
            return bytes;
        }

        private static bool IsFree(byte[] bytes, int pages)
        {
            int error;
            using (var copy = Open(bytes, null, out error))
                return copy != null && !copy.Encrypted && copy.PageCount == pages;
        }
    }
}

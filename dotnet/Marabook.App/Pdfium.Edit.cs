using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Marabook.App
{
    /// <summary>PDFIUM, CÔTÉ RETOUCHE (2.0.0, 22/11/2026 — les outils de PDFaah
    /// venus avec le lot 30) : pivoter des pages, glisser des pages blanches,
    /// poser un filigrane de texte ou une image (la signature), alléger les
    /// images. Chaque retouche travaille sur une COPIE ouverte pour elle
    /// (Retouch) : ni le fichier du disque ni le document affiché ne bougent,
    /// et c'est le PDF retouché, en octets, qui est rendu.
    /// Les fonctions natives de ce fichier sont liées À PART et sans exiger
    /// qu'elles existent toutes : une version de PDFium qui en manquerait
    /// garde Chop-Chop et l'Unlocker, et l'outil concerné le dit.</summary>
    internal static partial class Pdfium
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PageIntCall(IntPtr page);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void PageSetIntCall(IntPtr page, int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PageNewCall(IntPtr document, int index, double width, double height);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int PageBoxCall(IntPtr page, out float left, out float bottom, out float right, out float top);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewTextCall(IntPtr document, [MarshalAs(UnmanagedType.LPStr)] string font, float size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetTextCall(IntPtr textObject, byte[] utf16);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetColorCall(IntPtr pageObject, uint r, uint g, uint b, uint a);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TransformCall(IntPtr pageObject, double a, double b, double c, double d, double e, double f);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BoundsCall(IntPtr pageObject, out float left, out float bottom, out float right, out float top);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void InsertCall(IntPtr page, IntPtr pageObject);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr NewImageCall(IntPtr document);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetBitmapCall(IntPtr pages, int count, IntPtr imageObject, IntPtr bitmap);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetObjectCall(IntPtr page, int index);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ObjectIntCall(IntPtr pageObject);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ObjectPtrCall(IntPtr pageObject);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjectCall(IntPtr pageObject);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate CULong RawDataCall(IntPtr imageObject, IntPtr buffer, CULong length);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int MetadataCall(IntPtr imageObject, IntPtr page, out ImageMetadata metadata);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LoadJpegCall(IntPtr pages, int count, IntPtr imageObject, IntPtr fileAccess);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetBlockCall(IntPtr param, CULong position, IntPtr buffer, CULong size);

        [StructLayout(LayoutKind.Sequential)]
        private struct ImageMetadata
        {
            public uint Width, Height;
            public float HorizontalDpi, VerticalDpi;
            public uint BitsPerPixel;
            public int ColorSpace;
            public int MarkedContentId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileAccess
        {
            public CULong Length;
            public IntPtr GetBlock;
            public IntPtr Param;
        }

        private static PageIntCall _getRotation, _generateContent, _countObjects;
        private static PageSetIntCall _setRotation;
        private static PageNewCall _newPage;
        private static PageBoxCall _mediaBox, _cropBox;
        private static NewTextCall _newText;
        private static SetTextCall _setText;
        private static SetColorCall _setFill;
        private static TransformCall _transform;
        private static BoundsCall _bounds;
        private static InsertCall _insertObject;
        private static NewImageCall _newImage;
        private static SetBitmapCall _setBitmap;
        private static GetObjectCall _getObject;
        private static ObjectIntCall _objectType, _hasTransparency;
        private static ObjectPtrCall _imageBitmap;
        private static ObjectCall _destroyObject;
        private static RawDataCall _imageRaw;
        private static MetadataCall _imageMetadata;
        private static LoadJpegCall _loadJpeg;
        private static BitmapIntCall _bitmapWidth, _bitmapHeight, _bitmapFormat;
        private static IntPtr _editingFor;

        private static T Optional<T>(string name) where T : Delegate
        {
            IntPtr address;
            return NativeLibrary.TryGetExport(_library, name, out address) ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;
        }

        /// <summary>Lie les fonctions de retouche (une fois par bibliothèque chargée). Appelé sous le verrou.</summary>
        private static void EnsureEditing()
        {
            EnsureLoaded();
            if (_editingFor == _library) return;
            _getRotation = Optional<PageIntCall>("FPDFPage_GetRotation");
            _setRotation = Optional<PageSetIntCall>("FPDFPage_SetRotation");
            _generateContent = Optional<PageIntCall>("FPDFPage_GenerateContent");
            _countObjects = Optional<PageIntCall>("FPDFPage_CountObjects");
            _newPage = Optional<PageNewCall>("FPDFPage_New");
            _mediaBox = Optional<PageBoxCall>("FPDFPage_GetMediaBox");
            _cropBox = Optional<PageBoxCall>("FPDFPage_GetCropBox");
            _newText = Optional<NewTextCall>("FPDFPageObj_NewTextObj");
            _setText = Optional<SetTextCall>("FPDFText_SetText");
            _setFill = Optional<SetColorCall>("FPDFPageObj_SetFillColor");
            _transform = Optional<TransformCall>("FPDFPageObj_Transform");
            _bounds = Optional<BoundsCall>("FPDFPageObj_GetBounds");
            _insertObject = Optional<InsertCall>("FPDFPage_InsertObject");
            _newImage = Optional<NewImageCall>("FPDFPageObj_NewImageObj");
            _setBitmap = Optional<SetBitmapCall>("FPDFImageObj_SetBitmap");
            _getObject = Optional<GetObjectCall>("FPDFPage_GetObject");
            _objectType = Optional<ObjectIntCall>("FPDFPageObj_GetType");
            _hasTransparency = Optional<ObjectIntCall>("FPDFPageObj_HasTransparency");
            _imageBitmap = Optional<ObjectPtrCall>("FPDFImageObj_GetBitmap");
            _destroyObject = Optional<ObjectCall>("FPDFPageObj_Destroy");
            _imageRaw = Optional<RawDataCall>("FPDFImageObj_GetImageDataRaw");
            _imageMetadata = Optional<MetadataCall>("FPDFImageObj_GetImageMetadata");
            _loadJpeg = Optional<LoadJpegCall>("FPDFImageObj_LoadJpegFileInline");
            _bitmapWidth = Optional<BitmapIntCall>("FPDFBitmap_GetWidth");
            _bitmapHeight = Optional<BitmapIntCall>("FPDFBitmap_GetHeight");
            _bitmapFormat = Optional<BitmapIntCall>("FPDFBitmap_GetFormat");
            _editingFor = _library;
        }

        private static void Need(Delegate function, string what)
        {
            if (function == null) throw new InvalidOperationException("cette version de PDFium ne sait pas " + what + ".");
        }

        /// <summary>Ouvre une copie de travail de ce PDF, la retouche, et rend le PDF retouché.
        /// Le fichier doit être libre d'accès (un PDF verrouillé passe d'abord par l'Unlocker).</summary>
        private static byte[] Retouch(byte[] source, Action<Document> work)
        {
            if (source == null) throw new InvalidOperationException("le fichier n'est plus là.");
            int error;
            using (var document = Open(source, null, out error))
            {
                if (document == null) throw new InvalidOperationException(ErrorText(error) + ".");
                if (document.Encrypted) throw new InvalidOperationException("ce PDF est verrouillé : faites-en d'abord une copie sans verrou avec l'Unlocker.");
                lock (Gate)
                {
                    EnsureEditing();
                    work(document);
                    var bytes = Save(document.Handle, 2); // FPDF_NO_INCREMENTAL
                    if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("le PDF retouché n'a pas pu être écrit.");
                    return bytes;
                }
            }
        }

        /// <summary>Ce qu'on VOIT d'une page : sa boîte (rognage, sinon support) et son quart de tour.</summary>
        private struct PageFrame
        {
            public double Left, Bottom, Right, Top;
            public int Turns; // quarts de tour, sens des aiguilles
            public double VisualWidth { get { return Turns % 2 == 0 ? Right - Left : Top - Bottom; } }
            public double VisualHeight { get { return Turns % 2 == 0 ? Top - Bottom : Right - Left; } }

            /// <summary>Un point tel qu'on le voit (x depuis la gauche, y depuis le HAUT) → l'espace de la page.</summary>
            public void ToUser(double x, double y, out double u, out double v)
            {
                switch (Turns)
                {
                    case 1: u = Left + y; v = Bottom + x; break;
                    case 2: u = Right - x; v = Bottom + y; break;
                    case 3: u = Right - y; v = Top - x; break;
                    default: u = Left + x; v = Top - y; break;
                }
            }
        }

        private static PageFrame FrameOf(IntPtr page)
        {
            float left, bottom, right, top;
            if ((_cropBox == null || _cropBox(page, out left, out bottom, out right, out top) == 0)
                && (_mediaBox == null || _mediaBox(page, out left, out bottom, out right, out top) == 0))
            {
                left = 0; bottom = 0; right = 595; top = 842;
            }
            if (right < left) { var t = left; left = right; right = t; }
            if (top < bottom) { var t = bottom; bottom = top; top = t; }
            return new PageFrame { Left = left, Bottom = bottom, Right = right, Top = top, Turns = _getRotation == null ? 0 : ((_getRotation(page) % 4) + 4) % 4 };
        }

        // ================================================================ pivoter

        /// <summary>Tourne des pages par quarts de tour (sens des aiguilles) : turns(index) = 0 à 3.</summary>
        public static byte[] Rotate(byte[] source, Func<int, int> turns)
        {
            return Retouch(source, delegate(Document document)
            {
                Need(_getRotation, "tourner une page");
                Need(_setRotation, "tourner une page");
                for (var i = 0; i < document.PageCount; i++)
                {
                    var quarter = ((turns(i) % 4) + 4) % 4;
                    if (quarter == 0) continue;
                    var page = _loadPage(document.Handle, i);
                    if (page == IntPtr.Zero) continue;
                    try { _setRotation(page, (_getRotation(page) + quarter) % 4); }
                    finally { _closePage(page); }
                }
            });
        }

        // ================================================================ pages blanches

        /// <summary>Glisse « count » pages blanches de cette taille (en points) AVANT la page
        /// qui est à « at » (le nombre de pages = à la fin).</summary>
        public static byte[] InsertBlank(byte[] source, int at, int count, double widthPt, double heightPt)
        {
            return Retouch(source, delegate(Document document)
            {
                Need(_newPage, "créer une page");
                if (at < 0 || at > document.PageCount) at = document.PageCount;
                for (var i = 0; i < count; i++)
                {
                    var page = _newPage(document.Handle, at + i, widthPt, heightPt);
                    if (page == IntPtr.Zero) throw new InvalidOperationException("une page blanche n'a pas pu être créée.");
                    _closePage(page);
                }
            });
        }

        // ================================================================ filigrane

        internal sealed class Watermark
        {
            public const int Diagonal = 0, Center = 1, Head = 2, Foot = 3;
            public string Text = "";
            public int Layout = Diagonal;
            /// <summary>0 à 1 : la part de la page que le texte occupe (diagonale, centre) ou sa taille (tête, pied).</summary>
            public double Size = 0.6;
            public byte R = 0x7F, G = 0x8C, B = 0x8D;
            /// <summary>0 à 1.</summary>
            public double Opacity = 0.2;
            public bool Bold = true;
            /// <summary>Quelles pages : toutes (null), ou celles pour qui c'est vrai.</summary>
            public Func<int, int, bool> Pages;
        }

        /// <summary>Pose ce texte en filigrane, par-dessus le contenu des pages voulues.</summary>
        public static byte[] AddWatermark(byte[] source, Watermark mark)
        {
            if (mark == null || string.IsNullOrWhiteSpace(mark.Text)) throw new InvalidOperationException("le filigrane n'a pas de texte.");
            return Retouch(source, delegate(Document document)
            {
                Need(_newText, "écrire un texte");
                Need(_setText, "écrire un texte");
                Need(_setFill, "colorer un texte");
                Need(_transform, "placer un objet");
                Need(_bounds, "mesurer un objet");
                Need(_insertObject, "poser un objet");
                Need(_generateContent, "récrire une page");
                var raw = Encoding.Unicode.GetBytes(mark.Text.Trim());
                var text = new byte[raw.Length + 2];
                Array.Copy(raw, text, raw.Length);
                var alpha = (uint)Math.Max(1, Math.Min(255, (int)Math.Round(mark.Opacity * 255)));
                for (var i = 0; i < document.PageCount; i++)
                {
                    if (mark.Pages != null && !mark.Pages(i, document.PageCount)) continue;
                    var page = _loadPage(document.Handle, i);
                    if (page == IntPtr.Zero) continue;
                    try
                    {
                        var frame = FrameOf(page);
                        var label = _newText(document.Handle, mark.Bold ? "Helvetica-Bold" : "Helvetica", 100f);
                        if (label == IntPtr.Zero) throw new InvalidOperationException("le texte du filigrane n'a pas pu être créé.");
                        if (_setText(label, text) == 0) { if (_destroyObject != null) _destroyObject(label); throw new InvalidOperationException("ce texte ne peut pas être écrit avec la police du filigrane."); }
                        _setFill(label, mark.R, mark.G, mark.B, alpha);
                        float left, bottom, right, top;
                        if (_bounds(label, out left, out bottom, out right, out top) == 0 || right - left < 0.01f) { left = 0; bottom = 0; right = 100; top = 70; }
                        double width = right - left, height = top - bottom;
                        double w = frame.VisualWidth, h = frame.VisualHeight;
                        // Où, à quelle taille, sous quel angle — dans ce qu'on VOIT de la page.
                        double scale, angle, cx, cy;
                        var size = Math.Max(0.05, Math.Min(1, mark.Size));
                        if (mark.Layout == Watermark.Diagonal)
                        {
                            angle = Math.Atan2(h, w);
                            scale = Math.Sqrt(w * w + h * h) * 0.85 * size / width;
                            cx = w / 2; cy = h / 2;
                        }
                        else if (mark.Layout == Watermark.Center)
                        {
                            angle = 0;
                            scale = w * 0.9 * size / width;
                            cx = w / 2; cy = h / 2;
                        }
                        else
                        {
                            angle = 0;
                            scale = Math.Min((8 + 22 * size) / height, w * 0.9 / width);
                            cx = w / 2;
                            var margin = 28 + height * scale / 2; // 10 mm du bord
                            cy = mark.Layout == Watermark.Head ? margin : h - margin;
                        }
                        // Le texte, centré sur son milieu, mis à l'échelle, tourné, puis porté à sa place.
                        _transform(label, 1, 0, 0, 1, -(left + right) / 2.0, -(bottom + top) / 2.0);
                        _transform(label, scale, 0, 0, scale, 0, 0);
                        var total = angle + frame.Turns * Math.PI / 2; // la page tourne à l'affichage : le texte tourne avec
                        _transform(label, Math.Cos(total), Math.Sin(total), -Math.Sin(total), Math.Cos(total), 0, 0);
                        double u, v;
                        frame.ToUser(cx, cy, out u, out v);
                        _transform(label, 1, 0, 0, 1, u, v);
                        _insertObject(page, label);
                        if (_generateContent(page) == 0) throw new InvalidOperationException("la page " + (i + 1) + " n'a pas pu être récrite.");
                    }
                    finally { _closePage(page); }
                }
            });
        }

        // ================================================================ une image posée (la signature)

        /// <summary>Pose une image (BGRA, alpha droit, haut en premier) sur une page : son coin haut-gauche
        /// à (x, y) points du coin haut-gauche de la page telle qu'on la voit, sur « width » × « height » points.</summary>
        public static byte[] Stamp(byte[] source, int pageIndex, byte[] bgra, int pixelWidth, int pixelHeight, double x, double y, double width, double height)
        {
            if (bgra == null || pixelWidth <= 0 || pixelHeight <= 0 || bgra.Length < pixelWidth * pixelHeight * 4) throw new InvalidOperationException("l'image ne se lit pas.");
            return Retouch(source, delegate(Document document)
            {
                Need(_newImage, "poser une image");
                Need(_setBitmap, "poser une image");
                Need(_transform, "placer un objet");
                Need(_insertObject, "poser un objet");
                Need(_generateContent, "récrire une page");
                if (pageIndex < 0 || pageIndex >= document.PageCount) throw new InvalidOperationException("cette page n'existe pas.");
                var page = _loadPage(document.Handle, pageIndex);
                if (page == IntPtr.Zero) throw new InvalidOperationException("la page ne s'ouvre pas.");
                var pin = GCHandle.Alloc(bgra, GCHandleType.Pinned);
                var bitmap = IntPtr.Zero;
                try
                {
                    bitmap = _bitmapCreate(pixelWidth, pixelHeight, 4, pin.AddrOfPinnedObject(), pixelWidth * 4); // FPDFBitmap_BGRA
                    if (bitmap == IntPtr.Zero) throw new InvalidOperationException("l'image n'a pas pu être préparée.");
                    var image = _newImage(document.Handle);
                    if (image == IntPtr.Zero) throw new InvalidOperationException("l'image n'a pas pu être créée.");
                    if (_setBitmap(IntPtr.Zero, 0, image, bitmap) == 0) { if (_destroyObject != null) _destroyObject(image); throw new InvalidOperationException("l'image n'a pas pu être posée."); }
                    var frame = FrameOf(page);
                    double ou, ov, pu, pv, qu, qv;
                    frame.ToUser(x, y + height, out ou, out ov);        // le coin bas-gauche de l'image
                    frame.ToUser(x + width, y + height, out pu, out pv); // son coin bas-droit
                    frame.ToUser(x, y, out qu, out qv);                  // son coin haut-gauche
                    _transform(image, pu - ou, pv - ov, qu - ou, qv - ov, ou, ov);
                    _insertObject(page, image);
                    if (_generateContent(page) == 0) throw new InvalidOperationException("la page n'a pas pu être récrite.");
                }
                finally
                {
                    if (bitmap != IntPtr.Zero) _bitmapDestroy(bitmap);
                    pin.Free();
                    _closePage(page);
                }
            });
        }

        // ================================================================ alléger les images

        internal sealed class ImageDiet
        {
            /// <summary>La résolution au-delà de laquelle une image est réduite (points par pouce, à sa taille sur la page).</summary>
            public double MaxDpi = 150;
            /// <summary>Reçoit l'image (octets, largeur, hauteur, format PDFium : 1 gris, 2 BGR, 3 BGRx, 4 BGRA), la largeur
            /// et la hauteur voulues, et rend son JPEG — ou null pour la laisser telle quelle.</summary>
            public Func<byte[], int, int, int, int, int, int, byte[]> Encode;
            public int Seen, Replaced, Skipped;
            public long Before, After;
        }

        /// <summary>Réécrit en JPEG, à la résolution voulue, les images RVB et grises qui y gagnent.
        /// Ne touche ni aux images à transparence, ni aux images CMJN ou en tons directs (un fichier
        /// d'imprimeur garde ses couleurs), ni aux images au trait. Rend le PDF retouché ; le poids
        /// réellement gagné se mesure après le passage du compacteur (les anciennes images y tombent).</summary>
        public static byte[] DietImages(byte[] source, ImageDiet diet)
        {
            return Retouch(source, delegate(Document document)
            {
                Need(_countObjects, "lire le contenu d'une page");
                Need(_getObject, "lire le contenu d'une page");
                Need(_objectType, "lire le contenu d'une page");
                Need(_imageBitmap, "lire une image");
                Need(_imageMetadata, "lire une image");
                Need(_imageRaw, "lire une image");
                Need(_loadJpeg, "remplacer une image");
                Need(_generateContent, "récrire une page");
                Need(_bounds, "mesurer un objet");
                Need(_bitmapWidth, "lire une image");
                Need(_bitmapHeight, "lire une image");
                Need(_bitmapFormat, "lire une image");
                for (var i = 0; i < document.PageCount; i++)
                {
                    var page = _loadPage(document.Handle, i);
                    if (page == IntPtr.Zero) continue;
                    try
                    {
                        var changed = false;
                        var count = _countObjects(page);
                        for (var n = 0; n < count; n++)
                        {
                            var item = _getObject(page, n);
                            if (item == IntPtr.Zero || _objectType(item) != 3) continue; // FPDF_PAGEOBJ_IMAGE
                            diet.Seen++;
                            ImageMetadata meta;
                            if (_imageMetadata(item, page, out meta) == 0) { diet.Skipped++; continue; }
                            // RVB et gris seulement : DeviceGray 1, DeviceRGB 2, CalGray 4, CalRGB 5, ICC 7 (à 8 ou 24 bits).
                            var plain = (meta.ColorSpace == 1 || meta.ColorSpace == 2 || meta.ColorSpace == 4 || meta.ColorSpace == 5 || meta.ColorSpace == 7) && (meta.BitsPerPixel == 8 || meta.BitsPerPixel == 24);
                            if (!plain || (_hasTransparency != null && _hasTransparency(item) != 0)) { diet.Skipped++; continue; }
                            var rawLength = (long)_imageRaw(item, IntPtr.Zero, new CULong(0)).Value;
                            if (rawLength < 8 * 1024) { diet.Skipped++; continue; } // trop petite pour y gagner
                            float left, bottom, right, top;
                            if (_bounds(item, out left, out bottom, out right, out top) == 0 || right - left < 1 || top - bottom < 1) { diet.Skipped++; continue; }
                            var bitmap = _imageBitmap(item);
                            if (bitmap == IntPtr.Zero) { diet.Skipped++; continue; }
                            byte[] jpeg = null;
                            try
                            {
                                int width = _bitmapWidth(bitmap), height = _bitmapHeight(bitmap), stride = _bitmapStride(bitmap), format = _bitmapFormat(bitmap);
                                if (width <= 0 || height <= 0 || stride <= 0 || format < 1 || format > 3) { diet.Skipped++; continue; }
                                // La taille voulue : celle qui donne MaxDpi sur la page (jamais plus grande que l'image).
                                var wantedWidth = (int)Math.Ceiling((right - left) / 72.0 * diet.MaxDpi);
                                var wantedHeight = (int)Math.Ceiling((top - bottom) / 72.0 * diet.MaxDpi);
                                var ratio = Math.Min(1.0, Math.Max(wantedWidth / (double)width, wantedHeight / (double)height));
                                var newWidth = Math.Max(1, (int)Math.Round(width * ratio));
                                var newHeight = Math.Max(1, (int)Math.Round(height * ratio));
                                var pixels = new byte[stride * height];
                                Marshal.Copy(_bitmapBuffer(bitmap), pixels, 0, pixels.Length);
                                jpeg = diet.Encode(pixels, width, height, stride, format, newWidth, newHeight);
                            }
                            finally { _bitmapDestroy(bitmap); }
                            if (jpeg == null || jpeg.Length == 0 || jpeg.Length >= rawLength * 0.9) { diet.Skipped++; continue; } // pas de gain qui vaille
                            if (!ReplaceWithJpeg(item, jpeg)) { diet.Skipped++; continue; }
                            diet.Replaced++;
                            diet.Before += rawLength;
                            diet.After += jpeg.Length;
                            changed = true;
                        }
                        if (changed && _generateContent(page) == 0) throw new InvalidOperationException("la page " + (i + 1) + " n'a pas pu être récrite.");
                    }
                    finally { _closePage(page); }
                }
            });
        }

        private static bool ReplaceWithJpeg(IntPtr image, byte[] jpeg)
        {
            var pin = GCHandle.Alloc(jpeg, GCHandleType.Pinned);
            GetBlockCall read = delegate(IntPtr param, CULong position, IntPtr buffer, CULong size)
            {
                var at = (long)position.Value;
                var length = (long)size.Value;
                if (at < 0 || length < 0 || at + length > jpeg.LongLength) return 0;
                Marshal.Copy(jpeg, (int)at, buffer, (int)length);
                return 1;
            };
            var access = new FileAccess { Length = new CULong((uint)jpeg.Length), GetBlock = Marshal.GetFunctionPointerForDelegate(read), Param = IntPtr.Zero };
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<FileAccess>());
            try
            {
                Marshal.StructureToPtr(access, memory, false);
                var done = _loadJpeg(IntPtr.Zero, 0, image, memory);
                GC.KeepAlive(read);
                return done != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
                pin.Free();
            }
        }

        /// <summary>Le même PDF, récrit proprement par PDFium (sans ses mises à jour empilées).</summary>
        public static byte[] Resave(byte[] source)
        {
            return Retouch(source, delegate { });
        }

        /// <summary>Un PDF fait de ces pages (indices dans le fichier), dans cet ordre.</summary>
        public static byte[] Extract(Document source, IList<int> pages)
        {
            var plan = new Marabook.Print.PdfAssembly();
            plan.Insert(source.Path ?? "", source.Sizes(), -1);
            var keep = new HashSet<int>(pages);
            var drop = new List<int>();
            for (var i = 0; i < source.PageCount; i++) if (!keep.Contains(i)) drop.Add(i);
            plan.Remove(drop);
            return Assemble(plan, new[] { source });
        }
    }
}

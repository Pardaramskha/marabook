using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marabook.Model;
using SkiaSharp;

namespace Marabook.App
{
    /// <summary>L'IMPORT D'UNE CARTE (2.0.0, 16/11/2026, Rémi : « le découpage doit se
    /// faire à l'import, en même temps que l'optimisation ») : l'image est décodée une
    /// fois, puis découpée en PYRAMIDE DE TUILES — le dernier niveau est l'image
    /// entière, pixel pour pixel ; chaque niveau en dessous la divise par deux, jusqu'à
    /// tenir dans une tuile. À l'écran, on ne décode que les tuiles visibles du niveau
    /// qui convient au zoom : une carte de 16 000 px se promène comme une de 1 000.
    ///
    /// L'optimisation : chaque tuile est réencodée (JPEG de qualité TileQuality pour une
    /// image opaque, PNG pour une image à transparence). Le plein format n'est JAMAIS
    /// réduit — c'est lui qu'on regarde au plus fort zoom. Les niveaux réduits ajoutent
    /// environ un tiers au poids du dernier.</summary>
    public static class MapImport
    {
        public const int TileQuality = 88;
        public const string OpenFilter = "Images (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp|Tous les fichiers (*.*)|*.*";

        /// <summary>Découpe une image. Lève InvalidDataException si elle ne se décode pas
        /// (format inconnu, ou trop grande pour la mémoire). progress : de 0 à 1.</summary>
        public static MapInfo Slice(byte[] bytes, string sourceName, Action<double> progress = null, int tileSize = MapInfo.DefaultTileSize)
        {
            if (bytes == null || bytes.Length == 0) throw new InvalidDataException("Le fichier est vide.");
            SKBitmap current;
            try { current = SKBitmap.Decode(bytes); }
            catch (Exception error) { throw new InvalidDataException("L'image ne se décode pas (" + error.Message + ").", error); }
            if (current == null) throw new InvalidDataException("Ce fichier n'est pas une image que Marabook sait lire (JPEG, PNG, WebP, BMP), ou elle est trop grande pour la mémoire.");
            try
            {
                var transparent = current.AlphaType != SKAlphaType.Opaque && HasTransparency(current);
                var map = new MapInfo
                {
                    Width = current.Width, Height = current.Height, TileSize = tileSize, Extension = transparent ? ".png" : ".jpg",
                    SourceName = sourceName, SourceBytes = bytes.LongLength
                };
                map.Levels = Maps.LevelCount(map.Width, map.Height, tileSize);
                var total = Math.Max(1, Maps.TileCount(map.Width, map.Height, tileSize));
                var done = 0;
                for (var z = map.Levels - 1; z >= 0; z--)
                {
                    var columns = Maps.Columns(map, z);
                    var rows = Maps.Rows(map, z);
                    for (var y = 0; y < rows; y++)
                        for (var x = 0; x < columns; x++)
                        {
                            var rect = new SKRectI(x * tileSize, y * tileSize, Math.Min(current.Width, (x + 1) * tileSize), Math.Min(current.Height, (y + 1) * tileSize));
                            map.Tiles[Maps.TileKey(z, x, y)] = EncodeTile(current, rect, transparent);
                            done++;
                            if (progress != null) progress((double)done / total);
                        }
                    if (z == 0) break;
                    // Le niveau du dessous : moitié moins large, moitié moins haut.
                    var info = new SKImageInfo(Maps.LevelSide(map.Width, map.Levels, z - 1), Maps.LevelSide(map.Height, map.Levels, z - 1), current.ColorType, current.AlphaType);
                    var smaller = current.Resize(info, SKFilterQuality.High);
                    if (smaller == null) throw new InvalidDataException("La réduction de l'image a échoué (mémoire insuffisante ?).");
                    current.Dispose();
                    current = smaller;
                }
                return map;
            }
            finally { current.Dispose(); }
        }

        private static byte[] EncodeTile(SKBitmap source, SKRectI rect, bool transparent)
        {
            using (var tile = new SKBitmap(rect.Width, rect.Height, source.ColorType, source.AlphaType))
            {
                using (var canvas = new SKCanvas(tile))
                {
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawBitmap(source, new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), new SKRect(0, 0, rect.Width, rect.Height));
                }
                using (var image = SKImage.FromBitmap(tile))
                using (var data = image.Encode(transparent ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, transparent ? 100 : TileQuality))
                {
                    if (data == null) throw new InvalidDataException("Une tuile ne s'encode pas.");
                    return data.ToArray();
                }
            }
        }

        /// <summary>L'image a-t-elle vraiment des pixels transparents ? (Échantillonnée : une grille de 64 × 64.)</summary>
        private static bool HasTransparency(SKBitmap bitmap)
        {
            var stepX = Math.Max(1, bitmap.Width / 64);
            var stepY = Math.Max(1, bitmap.Height / 64);
            for (var y = 0; y < bitmap.Height; y += stepY)
                for (var x = 0; x < bitmap.Width; x += stepX)
                    if (bitmap.GetPixel(x, y).Alpha < 250) return true;
            return false;
        }

        /// <summary>Une carte de synthèse (sonde, tests) : un dégradé et quelques « continents ».</summary>
        public static byte[] Synthetic(int width, int height)
        {
            using (var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque))
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(new SKColor(70, 120, 190));
                using (var paint = new SKPaint { Color = new SKColor(96, 150, 90), IsAntialias = true })
                {
                    canvas.DrawOval(width * 0.3f, height * 0.4f, width * 0.16f, height * 0.22f, paint);
                    canvas.DrawOval(width * 0.7f, height * 0.6f, width * 0.2f, height * 0.16f, paint);
                }
                using (var paint = new SKPaint { Color = new SKColor(235, 240, 245) })
                {
                    canvas.DrawRect(0, 0, width, height * 0.06f, paint);
                    canvas.DrawRect(0, height * 0.94f, width, height * 0.06f, paint);
                }
                using (var image = SKImage.FromBitmap(bitmap))
                using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
                    return data.ToArray();
            }
        }
    }

    /// <summary>Les tuiles DÉCODÉES d'une carte : gardées avec elle (table faible), décodées
    /// hors du fil de l'interface, les moins récentes rendues quand il y en a trop. Une tuile
    /// pas encore prête rend null — la vue dessine à sa place la tuile d'un niveau plus
    /// grossier, puis se redessine quand la bonne arrive : l'image se RAFFINE.</summary>
    internal sealed class MapTileCache
    {
        private const int MaxTiles = 72; // 512 × 512 × 4 octets : un Mo pièce
        private static readonly ConditionalWeakTable<Dictionary<string, byte[]>, MapTileCache> Caches = new ConditionalWeakTable<Dictionary<string, byte[]>, MapTileCache>();

        private readonly Dictionary<string, Bitmap> _ready = new Dictionary<string, Bitmap>(StringComparer.Ordinal);
        private readonly LinkedList<string> _recent = new LinkedList<string>();
        private readonly HashSet<string> _loading = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _broken = new HashSet<string>(StringComparer.Ordinal);
        private int[] _texture;
        private int _textureWidth, _textureHeight;
        private bool _textureLoading;

        public event Action Arrived; // une tuile (ou la texture du globe) vient d'être décodée

        /// <summary>Le cache des tuiles de cette carte (ses clones partagent la table des tuiles, donc le cache).</summary>
        public static MapTileCache For(MapInfo map)
        {
            return Caches.GetValue(map.Tiles, delegate { return new MapTileCache(); });
        }

        public int ReadyCount { get { return _ready.Count; } }
        public bool Pending { get { return _loading.Count > 0; } }

        /// <summary>La tuile si elle est prête ; sinon null, et son décodage est lancé.</summary>
        public Bitmap Get(MapInfo map, int z, int x, int y, bool wait = false)
        {
            var key = Maps.TileKey(z, x, y);
            Bitmap bitmap;
            if (_ready.TryGetValue(key, out bitmap))
            {
                _recent.Remove(key);
                _recent.AddFirst(key);
                return bitmap;
            }
            byte[] bytes;
            if (_broken.Contains(key) || !map.Tiles.TryGetValue(key, out bytes) || bytes == null) return null;
            if (wait) { Store(key, ImageCache.TryDecode(bytes, 0)); return _ready.TryGetValue(key, out bitmap) ? bitmap : null; }
            if (!_loading.Add(key)) return null;
            Task.Run(delegate
            {
                var decoded = ImageCache.TryDecode(bytes, 0);
                Dispatcher.UIThread.Post(delegate
                {
                    _loading.Remove(key);
                    Store(key, decoded);
                    var handler = Arrived;
                    if (handler != null) handler();
                });
            });
            return null;
        }

        /// <summary>Prête sans attendre ? (Pour chercher une tuile de remplacement.)</summary>
        public Bitmap Peek(int z, int x, int y)
        {
            Bitmap bitmap;
            return _ready.TryGetValue(Maps.TileKey(z, x, y), out bitmap) ? bitmap : null;
        }

        private void Store(string key, Bitmap bitmap)
        {
            if (bitmap == null) { _broken.Add(key); return; }
            _ready[key] = bitmap;
            _recent.AddFirst(key);
            while (_recent.Count > MaxTiles)
            {
                // Le niveau 0 reste : c'est le fond de secours de toute la carte.
                var node = _recent.Last;
                while (node != null && node.Value.StartsWith("0/", StringComparison.Ordinal)) node = node.Previous;
                if (node == null) break;
                _recent.Remove(node);
                // La bitmap n'est pas détruite ici : un rendu en cours peut encore la tenir ; le ramasse-miettes s'en charge.
                _ready.Remove(node.Value);
            }
        }

        // ------------------------------------------------------------ la texture du globe

        public const int TextureMaxWidth = 2048;

        /// <summary>L'image entière, réduite (au plus TextureMaxWidth de large), en pixels BGRA : la peau
        /// du globe. Null tant qu'elle n'est pas prête (son assemblage est lancé).</summary>
        public int[] Texture(MapInfo map, out int width, out int height, bool wait = false)
        {
            width = _textureWidth;
            height = _textureHeight;
            if (_texture != null) return _texture;
            if (!map.HasImage) return null;
            if (wait) { Assemble(map); width = _textureWidth; height = _textureHeight; return _texture; }
            if (_textureLoading) return null;
            _textureLoading = true;
            Task.Run(delegate
            {
                try { Assemble(map); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    _textureLoading = false;
                    var handler = Arrived;
                    if (handler != null) handler();
                });
            });
            return null;
        }

        private void Assemble(MapInfo map)
        {
            var z = map.Levels - 1;
            while (z > 0 && Maps.LevelSide(map.Width, map.Levels, z) > TextureMaxWidth) z--;
            var w = Maps.LevelSide(map.Width, map.Levels, z);
            var h = Maps.LevelSide(map.Height, map.Levels, z);
            using (var whole = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul)))
            {
                using (var canvas = new SKCanvas(whole))
                {
                    canvas.Clear(new SKColor(40, 44, 52));
                    for (var y = 0; y < Maps.Rows(map, z); y++)
                        for (var x = 0; x < Maps.Columns(map, z); x++)
                        {
                            byte[] bytes;
                            if (!map.Tiles.TryGetValue(Maps.TileKey(z, x, y), out bytes) || bytes == null) continue;
                            using (var tile = SKBitmap.Decode(bytes))
                                if (tile != null) canvas.DrawBitmap(tile, x * map.TileSize, y * map.TileSize);
                        }
                }
                var pixels = new int[w * h];
                Marshal.Copy(whole.GetPixels(), pixels, 0, pixels.Length);
                _textureWidth = w;
                _textureHeight = h;
                _texture = pixels;
            }
        }
    }
}

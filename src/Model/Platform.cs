using System;
using System.IO;

namespace Marabook.Model
{
    /// <summary>Ce qui varie d'un système à l'autre et que le cœur doit
    /// pourtant connaître (portage Avalonia, lot P0) : le dossier des données
    /// de l'utilisateur, l'interpréteur Python livré, LibreOffice. L'app pose
    /// la sienne au démarrage ; sans rien, les règles Windows d'aujourd'hui.</summary>
    public interface IPlatform
    {
        /// <summary>Le dossier des données (%APPDATA%\Marabook sur Windows,
        /// ~/.config/marabook ou ~/Library ailleurs). Créé s'il manque.</summary>
        string DataFolder { get; }

        /// <summary>L'exécutable Python de la distribution embarquée sous
        /// <paramref name="baseFolder"/> (python\python.exe sur Windows).</summary>
        string PythonExecutable(string baseFolder);

        /// <summary>Le binaire LibreOffice installé, ou null.</summary>
        string FindLibreOffice();
    }

    /// <summary>Le point d'accès du cœur à sa plate-forme et à son codec
    /// d'images. Statique : le cœur n'a pas de conteneur, et les tests
    /// console remplacent ce qu'ils veulent.</summary>
    public static class Platform
    {
        public static IPlatform Current = new DefaultPlatform();

        /// <summary>Le décodeur d'images : celui de l'interface (WPF, puis
        /// Skia) une fois l'app lancée ; sans interface, les en-têtes seuls.</summary>
        public static IImageCodec Images = new HeaderImageCodec();
    }

    /// <summary>Les règles Windows historiques, en .NET nu.</summary>
    public sealed class DefaultPlatform : IPlatform
    {
        public string DataFolder
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var folder = Path.Combine(appData, "Marabook");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public string PythonExecutable(string baseFolder)
        {
            return Path.Combine(baseFolder, Path.Combine("python", "python.exe"));
        }

        public string FindLibreOffice()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Path.Combine("LibreOffice", Path.Combine("program", "soffice.exe"))),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Path.Combine("LibreOffice", Path.Combine("program", "soffice.exe")))
            };
            foreach (var candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return null;
        }
    }

    /// <summary>Ce que le cœur demande à un décodeur d'images : les dimensions,
    /// des pixels RVB pour le PDF, un réencodage PNG pour l'EPUB. L'interface
    /// fournit le sien (WPF : BitmapDecoder ; Avalonia : Skia).</summary>
    public interface IImageCodec
    {
        /// <summary>Les dimensions en pixels — l'en-tête d'abord, le décodage
        /// sinon ; false = illisible.</summary>
        bool TryGetSize(byte[] bytes, out int width, out int height);

        /// <summary>Les pixels RVB24 sur fond blanc (le papier), l'image
        /// réduite à au plus <paramref name="maxWidthPx"/> de large (jamais
        /// agrandie) ; null = illisible.</summary>
        byte[] ToRgb24(byte[] bytes, double maxWidthPx, out int width, out int height);

        /// <summary>L'image réencodée en PNG ; null = impossible.</summary>
        byte[] ToPng(byte[] bytes);
    }

    /// <summary>Le codec sans interface : il lit les en-têtes, ne décode rien.</summary>
    public sealed class HeaderImageCodec : IImageCodec
    {
        public bool TryGetSize(byte[] bytes, out int width, out int height)
        {
            return ImageHeader.TryReadSize(bytes, out width, out height) && height > 0;
        }

        public byte[] ToRgb24(byte[] bytes, double maxWidthPx, out int width, out int height)
        {
            width = 0;
            height = 0;
            return null;
        }

        public byte[] ToPng(byte[] bytes)
        {
            return null;
        }
    }

    /// <summary>Les dimensions lues dans l'en-tête d'un PNG, GIF, BMP ou JPEG —
    /// sans décoder (l'ancien ImageCache.PixelWidthOf, hauteur en plus).</summary>
    public static class ImageHeader
    {
        public static int PixelWidth(byte[] bytes)
        {
            int width, height;
            return TryReadSize(bytes, out width, out height) ? width : 0;
        }

        /// <summary>Vrai dès que la largeur est lue ; la hauteur peut rester
        /// à 0 sur un en-tête tronqué (l'appelant décode alors, ou renonce).</summary>
        public static bool TryReadSize(byte[] b, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (b == null) return false;
            try
            {
                if (b.Length > 24 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G')
                {
                    width = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                    height = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                    return width > 0;
                }
                if (b.Length > 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
                {
                    width = b[6] | (b[7] << 8);
                    height = b[8] | (b[9] << 8);
                    return width > 0;
                }
                if (b.Length > 22 && b[0] == 'B' && b[1] == 'M')
                {
                    width = b[18] | (b[19] << 8) | (b[20] << 16) | (b[21] << 24);
                    if (b.Length > 26)
                        height = Math.Abs(b[22] | (b[23] << 8) | (b[24] << 16) | (b[25] << 24)); // négatif = de haut en bas
                    return width > 0;
                }
                if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
                {
                    var i = 2;
                    while (i + 9 < b.Length && b[i] == 0xFF)
                    {
                        var marker = b[i + 1];
                        var length = (b[i + 2] << 8) | b[i + 3];
                        if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                        {
                            height = (b[i + 5] << 8) | b[i + 6];
                            width = (b[i + 7] << 8) | b[i + 8];
                            return width > 0;
                        }
                        if (length < 2) break;
                        i += 2 + length;
                    }
                }
            }
            catch (IndexOutOfRangeException) { }
            return false;
        }
    }
}

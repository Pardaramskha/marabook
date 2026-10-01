using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Marabook.Model;

namespace Marabook.App
{
    /// <summary>L'image d'un succès (12/09/2026) : le PNG
    /// assets\achievements\&lt;id&gt;.png à côté de l'exécutable s'il existe
    /// (les visuels arrivent plus tard), sinon un écusson dessiné. Verrouillé
    /// = en gris et atténué ; débloqué = en couleurs.</summary>
    public static class AchievementBadge
    {
        public static string ImagePath(string id)
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                System.IO.Path.Combine("assets", System.IO.Path.Combine("achievements", id + ".png")));
        }

        /// <summary>L'image d'un succès de module (DLC) : achievements\&lt;id&gt;.png
        /// dans le dossier du module installé.</summary>
        public static string ModuleImagePath(string moduleId, string id)
        {
            return System.IO.Path.Combine(Modules.DirOf(moduleId), System.IO.Path.Combine("achievements", id + ".png"));
        }

        public static Control Build(Achievement achievement, bool unlocked, double size)
        {
            var path = achievement.ModuleId != null
                ? ModuleImagePath(achievement.ModuleId, achievement.Id)
                : ImagePath(achievement.Id);
            Avalonia.Media.Imaging.Bitmap source = null;
            if (File.Exists(path))
            {
                try
                {
                    using (var stream = File.OpenRead(path))
                        source = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, (int)(size * 2));
                }
                catch (Exception) { source = null; }
            }

            var frame = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 0.22),
                ClipToBounds = true,
                Opacity = unlocked ? 1 : 0.45
            };
            if (source != null)
            {
                // Pas de conversion en gris chez Avalonia : l'opacité du cadre suffit.
                frame.Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
                return frame;
            }
            // L'écusson provisoire : dégradé accent (ou gris) et le trophée
            // (icône « achievement » de Rémi, 14/09 — plus l'étoile en texte).
            frame.Background = unlocked
                ? (IBrush)Ui.VerticalGradient(Chrome.Accent.Color, Chrome.AccentStrong.Color)
                : new SolidColorBrush(Chrome.Blend(Chrome.SoftText.Color, Chrome.WindowBg.Color, 0.5));
            var trophy = Icons.Make("achievement", size * 0.56, Brushes.White) as Control;
            if (trophy != null)
            {
                trophy.HorizontalAlignment = HorizontalAlignment.Center;
                trophy.VerticalAlignment = VerticalAlignment.Center;
                frame.Child = trophy;
            }
            return frame;
        }
    }
}

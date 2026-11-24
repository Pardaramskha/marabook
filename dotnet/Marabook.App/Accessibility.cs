using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Marabook.Settings;

namespace Marabook.App
{
    /// <summary>L'ACCESSIBILITÉ (2.0.0, 22/11/2026, Rémi) — ce que règle
    /// Préférences › Accessibilité, TOUT ÉTEINT D'OFFICE : tant que rien n'est
    /// coché, rien ici ne touche à l'interface.
    ///  - le ZOOM de l'interface (100 à 200 %) : le contenu de chaque fenêtre
    ///    est posé dans un LayoutTransformControl à l'échelle voulue, les
    ///    menus, listes déroulantes et infobulles (des PopupRoot) reçoivent la
    ///    même par leur propriété Transform ; une fenêtre à taille fixe grandit
    ///    d'autant, dans la limite de l'écran. Le zoom du TEXTE de l'éditeur
    ///    reste celui de la barre d'état : les deux se multiplient ;
    ///  - le FOCUS VISIBLE : un cadre épais autour de ce qui a le clavier,
    ///    quand on s'y est rendu au clavier (Tab, flèches) ;
    ///  - la POLICE de l'interface, parmi celles du système qu'on recommande
    ///    aux lecteurs dyslexiques (Marabook n'en télécharge aucune) ;
    ///  - les COULEURS DE SENS pour daltoniens : voir Chrome.SafeMeaning ;
    ///  - le curseur épais, le curseur fixe, les liens soulignés : lus par
    ///    l'éditeur (ComposedView, ComposedRenderer) ;
    ///  - le lecteur d'écran : voir ScreenReader.cs.</summary>
    internal static class Accessibility
    {
        // ================================================================ le zoom

        /// <summary>L'échelle partagée : la changer redimensionne tout ce qui la porte.</summary>
        private static readonly ScaleTransform Scale = new ScaleTransform(1, 1);
        private const string ZoomTag = "ui-zoom";
        private static bool _watching;
        private static string _styleSignature;

        /// <summary>Les dimensions d'ORIGINE d'une fenêtre à taille fixe (celles d'avant tout zoom) :
        /// { largeur, hauteur, largeur min, hauteur min, largeur max, hauteur max }.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, double[]> Base =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Window, double[]>();

        public static double Zoom { get { return AppSettings.ClampZoom(AppSettings.UiZoom); } }

        public static bool ZoomOn { get { return Zoom > 1.001; } }

        /// <summary>Au lancement, et à chaque changement d'apparence.</summary>
        public static void Apply()
        {
            var zoom = Zoom;
            if (!_watching && zoom > 1.001)
            {
                // Posés seulement le jour où le zoom sert : toute fenêtre qui s'ouvre prend l'échelle, et tout
                // menu, toute liste déroulante, toute infobulle (des PopupRoot) à l'instant où ils se montent.
                _watching = true;
                Window.WindowOpenedEvent.AddClassHandler<Window>(delegate(Window window, RoutedEventArgs e) { Dress(window, null); });
                TemplatedControl.TemplateAppliedEvent.AddClassHandler<PopupRoot>(delegate(PopupRoot popup, TemplateAppliedEventArgs e) { popup.Transform = Scale; });
            }
            Scale.ScaleX = zoom;
            Scale.ScaleY = zoom;
            if (!_watching) return;
            var lifetime = Application.Current == null ? null : Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime == null) return;
            foreach (var window in new List<Window>(lifetime.Windows)) Dress(window, null);
        }

        /// <summary>Après les styles du skin : ceux de l'accessibilité ont-ils changé (police, focus) ? Le thème est alors rebâti.</summary>
        public static void ApplyStyles()
        {
            var signature = (AppSettings.FocusVisible ? "focus" : "") + "|" + (AppSettings.UiFont ?? "");
            var was = _styleSignature;
            _styleSignature = signature;
            if (was == null && signature == "|") return; // rien n'a jamais été coché : le thème n'est pas touché
            if (was == signature) return;
            Skin.RebuildTheme();
        }

        /// <summary>Pose le contenu de la fenêtre dans l'échelle (une fois), et règle sa taille si elle est fixe.
        /// Appelé avant l'ouverture par Dialogs.FitToScreen, et à l'ouverture pour les autres.</summary>
        public static void Dress(Window window, Window owner)
        {
            if (window == null || (!ZoomOn && !_watching)) return;
            var content = window.Content as Control;
            var scrolled = content is ScrollViewer ? ((ScrollViewer)content).Content as Control : null; // le défilement de Dialogs.Fit, posé par-dessus
            if (content != null && !ZoomTag.Equals(content.Tag) && !(scrolled != null && ZoomTag.Equals(scrolled.Tag)))
            {
                window.Content = null;
                window.Content = new LayoutTransformControl { LayoutTransform = Scale, Child = content, Tag = ZoomTag };
            }
            if (window is MainWindow) return; // sa taille est celle que l'auteur lui donne
            ModalChrome.Reserve(window); // la barre de titre du nouvel habit (24/11) : comptée AVANT de relever les dimensions d'origine
            double[] original;
            if (!Base.TryGetValue(window, out original))
            {
                original = new[] { window.Width, window.Height, window.MinWidth, window.MinHeight, window.MaxWidth, window.MaxHeight };
                Base.Add(window, original);
            }
            Resize(window, original, Zoom, owner);
        }

        /// <summary>Les dimensions FIXES d'une fenêtre (celles que son contenu ne décide pas) : celles d'origine, fois le zoom, sans sortir de l'écran.</summary>
        private static void Resize(Window window, double[] original, double zoom, Window owner)
        {
            double maxWidth = double.PositiveInfinity, maxHeight = double.PositiveInfinity;
            try
            {
                var reference = window.IsVisible ? window : owner ?? App.MainWindowOrNull ?? window;
                var screen = reference.Screens.ScreenFromWindow(reference) ?? reference.Screens.Primary;
                if (screen != null)
                {
                    var scaling = screen.Scaling > 0.5 ? screen.Scaling : 1.0;
                    maxWidth = screen.WorkingArea.Width / scaling - 16;
                    maxHeight = screen.WorkingArea.Height / scaling - 40;
                }
            }
            catch (Exception) { }
            var byWidth = window.SizeToContent == SizeToContent.Width || window.SizeToContent == SizeToContent.WidthAndHeight;
            var byHeight = window.SizeToContent == SizeToContent.Height || window.SizeToContent == SizeToContent.WidthAndHeight;
            // À 100 %, les valeurs d'origine reviennent telles quelles (l'écran ne les avait pas bornées).
            var capWidth = zoom > 1.001 ? maxWidth : double.PositiveInfinity;
            var capHeight = zoom > 1.001 ? maxHeight : double.PositiveInfinity;
            if (!double.IsNaN(original[2]) && original[2] > 0) window.MinWidth = Math.Min(capWidth, original[2] * zoom);
            if (!double.IsNaN(original[3]) && original[3] > 0) window.MinHeight = Math.Min(capHeight, original[3] * zoom);
            if (!double.IsInfinity(original[4]) && !double.IsNaN(original[4])) window.MaxWidth = original[4] * zoom;
            if (!double.IsInfinity(original[5]) && !double.IsNaN(original[5])) window.MaxHeight = original[5] * zoom;
            if (!byWidth && !double.IsNaN(original[0])) window.Width = Math.Min(capWidth, original[0] * zoom);
            if (!byHeight && !double.IsNaN(original[1])) window.Height = Math.Min(capHeight, original[1] * zoom);
        }

        // ================================================================ les styles

        private static Style Rule(Func<Selector, Selector> selector, params Setter[] setters)
        {
            var style = new Style(selector);
            foreach (var setter in setters) style.Setters.Add(setter);
            return style;
        }

        /// <summary>Les styles d'accessibilité, par-dessus ceux du thème et du skin (Theme.Build les ajoute en tout dernier).
        /// Rien n'est ajouté tant que rien n'est coché.</summary>
        public static void AddStyles(Styles styles)
        {
            var font = UiFont;
            if (font != null)
            {
                styles.Add(Rule(x => x.Is<Window>(), new Setter(TemplatedControl.FontFamilyProperty, font)));
                styles.Add(Rule(x => x.Is<PopupRoot>(), new Setter(TemplatedControl.FontFamilyProperty, font)));
            }

            if (AppSettings.FocusVisible)
            {
                // Le cadre du focus au clavier : deux tons, pour se voir sur un fond clair comme sur l'accent.
                var ring = new FuncTemplate<Control>(delegate
                {
                    return new Border
                    {
                        Margin = new Thickness(-3),
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(3),
                        BorderBrush = Chrome.Ink,
                        IsHitTestVisible = false,
                        Tag = "focus-ring",
                        Child = new Border { CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1.5), BorderBrush = Chrome.RaisedBg, IsHitTestVisible = false }
                    };
                });
                styles.Add(Rule(x => x.Is<Control>(), new Setter(Control.FocusAdornerProperty, ring)));
                // Un champ de saisie n'a pas de cadre de focus dans le thème : sa bordure le dit.
                styles.Add(Rule(x => x.OfType<TextBox>().Class(":focus"), new Setter(TemplatedControl.BorderBrushProperty, Chrome.Ink), new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(2.5))));
            }
        }

        // ================================================================ la police de l'interface

        /// <summary>Les polices qu'on propose quand le système les a : dessinées pour la lecture difficile
        /// (OpenDyslexic, Atkinson Hyperlegible, Lexend), ou recommandées faute de mieux (Verdana, Comic Sans…).</summary>
        public static readonly string[] FontCandidates =
        {
            "OpenDyslexic", "OpenDyslexic3", "OpenDyslexicAlta", "Atkinson Hyperlegible", "Atkinson Hyperlegible Next", "Lexend", "Lexend Deca",
            "Verdana", "Comic Sans MS", "Trebuchet MS", "Century Gothic", "Tahoma", "Arial"
        };

        /// <summary>Celles de la liste qui sont installées sur cette machine, dans l'ordre de la liste.</summary>
        public static List<string> InstalledFonts()
        {
            var found = new List<string>();
            try
            {
                var system = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var family in FontManager.Current.SystemFonts) system.Add(family.Name);
                foreach (var candidate in FontCandidates) if (system.Contains(candidate)) found.Add(candidate);
            }
            catch (Exception) { }
            return found;
        }

        /// <summary>La police choisie, ou null : celle de Marabook (ou du skin).</summary>
        public static FontFamily UiFont
        {
            get
            {
                var name = AppSettings.UiFont;
                if (string.IsNullOrWhiteSpace(name)) return null;
                try { return new FontFamily(name + ", Inter, Segoe UI, Helvetica Neue, DejaVu Sans"); }
                catch (Exception) { return null; }
            }
        }

        // ================================================================ le curseur de l'éditeur

        public static double CaretWidth { get { return AppSettings.ThickCaret ? 3.6 : 1.4; } }
    }
}

using System;
using System.Windows;
using System.Windows.Threading;

namespace Marabook.View
{
    /// <summary>L'ouverture des dialogues (13/09/2026) — contre le « flash »
    /// à l'apparition d'une fenêtre. Mesuré au crochet WinEvent : un
    /// dialogue à SizeToContent est CRÉÉ par Windows à la taille par défaut
    /// (CW_USEDEFAULT : 1440 × 753 sur le poste de Rémi, en cascade), puis
    /// redimensionné à son contenu et montré. Entre les deux, le compositeur
    /// possède une surface de la grande taille, pas encore peinte : selon la
    /// cadence, une image de ce grand rectangle vide peut passer — le
    /// « shell » entrevu. Deux parades, dans l'ordre :
    /// 1. PRÉ-DIMENSIONNER : le contenu est mesuré avant la création du
    ///    handle et Width/Height sont posés (contenu + cadre) — le HWND naît
    ///    à peu près à sa taille finale, SizeToContent ne fait plus
    ///    qu'ajuster de quelques pixels.
    /// 2. PEINDRE AVANT DE MONTRER : à SourceInitialized (handle créé, pas
    ///    encore visible), une passe de disposition puis une opération vide
    ///    à la priorité Render force le premier rendu dans la surface avant
    ///    ShowWindow — le compositeur n'a jamais de surface vide à montrer.
    /// Un seul point d'entrée, ShowModal, à la place de ShowDialog().</summary>
    public static class Dialogs
    {
        public static bool? ShowModal(Window window)
        {
            Prepare(window);
            return window.ShowDialog();
        }

        public static void Prepare(Window window)
        {
            if (window == null) return;
            Presize(window);
            window.SourceInitialized += delegate
            {
                try
                {
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { }));
                }
                catch { }
            };
        }

        /// <summary>Width/Height posés depuis la mesure du contenu — seulement
        /// pour les dimensions que SizeToContent gouverne et que l'auteur du
        /// dialogue n'a pas fixées.</summary>
        private static void Presize(Window window)
        {
            var mode = window.SizeToContent;
            if (mode == SizeToContent.Manual) return;
            var content = window.Content as UIElement;
            if (content == null) return;
            try
            {
                var area = SystemParameters.WorkArea;
                content.Measure(new Size(Math.Max(200, area.Width), Math.Max(200, area.Height)));
            }
            catch { return; }
            var desired = content.DesiredSize;
            if (desired.Width <= 0 || desired.Height <= 0) return;
            var frame = SystemParameters.WindowNonClientFrameThickness;
            var wantsWidth = mode == SizeToContent.Width || mode == SizeToContent.WidthAndHeight;
            var wantsHeight = mode == SizeToContent.Height || mode == SizeToContent.WidthAndHeight;
            if (wantsWidth && double.IsNaN(window.Width))
                window.Width = desired.Width + frame.Left + frame.Right;
            if (wantsHeight && double.IsNaN(window.Height))
                window.Height = desired.Height + frame.Top + frame.Bottom;
        }
    }
}

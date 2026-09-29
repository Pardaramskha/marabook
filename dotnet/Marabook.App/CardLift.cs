using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation.Easings;
using Avalonia.Animation;
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
    /// <summary>Le « soulèvement » d'une carte au survol (batch 35) : léger
    /// agrandissement autour du centre, translation vers le haut compensée
    /// pour que le BORD BAS ne bouge pas (sinon la souris sort par le bas,
    /// la carte redescend, rentre… clignotement), ombre portée qui se
    /// creuse. Tout est en RenderTransform et en effet : aucune incidence
    /// sur la mise en page ni sur la zone cliquable des boutons de la
    /// carte (le ⋮ suit la carte et reste sous le pointeur).
    ///
    /// L'ombre est portée par un HÔTE SANS TEXTE glissé sous le contenu de
    /// la carte : un effet bitmap posé sur la carte elle-même faisait
    /// passer son texte par une surface intermédiaire, floue le temps de
    /// la mise à l'échelle. Le texte reste vectoriel, net à chaque image.</summary>
    public static class CardLift
    {
        private const double Scale = 1.025;
        private const double RaisePx = 3;
        private static readonly TimeSpan Up = TimeSpan.FromMilliseconds(140);
        private static readonly TimeSpan Down = TimeSpan.FromMilliseconds(180);

        public static void Attach(Border card)
        {
            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            card.RenderTransform = group;
            card.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            // Les transitions d'Avalonia remplacent les DoubleAnimation de WPF :
            // chaque changement de valeur s'anime avec sa durée et sa courbe.
            scale.Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = Up, Easing = new CubicEaseOut() },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = Up, Easing = new CubicEaseOut() }
            };
            translate.Transitions = new Transitions
            {
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = Up, Easing = new CubicEaseOut() }
            };

            // L'hôte de l'ombre : la silhouette de la carte (fond + coins),
            // débordant du rembourrage jusqu'au bord extérieur, sous le
            // contenu. C'est lui — jamais le texte — qui reçoit l'ombre.
            var content = card.Child;
            card.Child = null;
            var host = new Grid();
            var shadowHost = new Border
            {
                Background = card.Background ?? Chrome.CardBg,
                CornerRadius = card.CornerRadius,
                Margin = new Thickness(
                    -(card.Padding.Left + card.BorderThickness.Left),
                    -(card.Padding.Top + card.BorderThickness.Top),
                    -(card.Padding.Right + card.BorderThickness.Right),
                    -(card.Padding.Bottom + card.BorderThickness.Bottom)),
                IsHitTestVisible = false
            };
            // L'ombre au repos est la MÊME ombre, transparente — et non
            // « aucune ombre » : entre zéro et une ombre, l'animateur de
            // BoxShadows d'Avalonia ne sait pas interpoler et bascule d'un
            // coup à mi-parcours (l'ombre arrivait en retard, puis sèche —
            // 29/09). De même forme des deux côtés, elle se fond.
            shadowHost.BoxShadow = Rest;
            shadowHost.Transitions = new Transitions
            {
                new BoxShadowsTransition { Property = Border.BoxShadowProperty, Duration = Down, Easing = new CubicEaseOut() }
            };
            host.Children.Add(shadowHost);
            if (content != null) host.Children.Add(content);
            card.Child = host;

            card.PointerEntered += delegate
            {
                scale.ScaleX = Scale;
                scale.ScaleY = Scale;
                // Le bord bas reste en place : la moitié de la croissance
                // verticale compense la montée.
                var growth = card.Bounds.Height * (Scale - 1) / 2;
                translate.Y = -(RaisePx - growth);
                shadowHost.BoxShadow = Lifted;
            };
            card.PointerExited += delegate
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
                translate.Y = 0;
                shadowHost.BoxShadow = Rest;
            };
        }

        // L'ombre soulevée et son double au repos (mêmes décalage et flou,
        // alpha nul) : seule la couleur s'anime.
        private static readonly BoxShadows Lifted = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 4, Blur = 14, Color = Color.FromArgb(0x38, 0, 0, 0) });
        private static readonly BoxShadows Rest = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 4, Blur = 14, Color = Color.FromArgb(0x00, 0, 0, 0) });
    }
}

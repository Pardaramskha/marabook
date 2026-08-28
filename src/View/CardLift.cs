using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace UniversSale.View
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
        private static readonly Duration Up = new Duration(TimeSpan.FromMilliseconds(140));
        private static readonly Duration Down = new Duration(TimeSpan.FromMilliseconds(180));

        public static void Attach(Border card)
        {
            var scale = new ScaleTransform(1, 1);
            var translate = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            card.RenderTransform = group;
            card.RenderTransformOrigin = new Point(0.5, 0.5);

            // L'hôte de l'ombre : la silhouette de la carte (fond + coins),
            // débordant du rembourrage jusqu'au bord extérieur, sous le
            // contenu. C'est lui — jamais le texte — qui reçoit l'effet.
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
            host.Children.Add(shadowHost);
            if (content != null) host.Children.Add(content);
            card.Child = host;

            card.MouseEnter += delegate
            {
                if (shadowHost.Effect == null)
                    shadowHost.Effect = new DropShadowEffect { BlurRadius = 0, ShadowDepth = 0, Opacity = 0, Color = Colors.Black, Direction = 270 };
                var shadow = (DropShadowEffect)shadowHost.Effect;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(Scale, Up) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(Scale, Up) { EasingFunction = ease });
                // Le bord bas reste en place : la moitié de la croissance
                // verticale compense la montée.
                var growth = card.ActualHeight * (Scale - 1) / 2;
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-(RaisePx - growth), Up) { EasingFunction = ease });
                shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(14, Up) { EasingFunction = ease });
                shadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(4, Up) { EasingFunction = ease });
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.22, Up) { EasingFunction = ease });
            };
            card.MouseLeave += delegate
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, Down) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, Down) { EasingFunction = ease });
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Down) { EasingFunction = ease });
                var shadow = shadowHost.Effect as DropShadowEffect;
                if (shadow == null) return;
                var fade = new DoubleAnimation(0, Down) { EasingFunction = ease };
                fade.Completed += delegate
                {
                    // L'effet ne reste pas posé sur cent cartes au repos.
                    if (!card.IsMouseOver && shadowHost.Effect == shadow)
                    {
                        shadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                        shadowHost.Effect = null;
                    }
                };
                shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(0, Down) { EasingFunction = ease });
                shadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(0, Down) { EasingFunction = ease });
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Marabook.View
{
    /// <summary>L'indicateur d'activité (0.50.0) : un anneau qui tourne en
    /// haut à droite de la fenêtre, sur la ligne des menus, tant que quelque
    /// chose travaille — un projet ou un écrit qui charge, une sauvegarde, le
    /// correcteur en fond. Deux sources : un compteur Begin/End pour les
    /// travaux du fil d'interface (Run montre l'anneau, laisse WPF le
    /// dessiner, fait le travail, le retire), et des sondes (Watch)
    /// interrogées toutes les 250 ms pour les travaux de fond. Couleurs de
    /// Chrome par référence : l'anneau suit le thème et l'accent.</summary>
    public sealed class BusyIndicator : Grid
    {
        private readonly RotateTransform _spin = new RotateTransform();
        private readonly DoubleAnimation _turn;
        private readonly DispatcherTimer _poll;
        private readonly List<Func<bool>> _watches = new List<Func<bool>>();
        private int _depth;
        private bool _spinning;

        public BusyIndicator() : this(14) { }

        public BusyIndicator(double size)
        {
            Width = size;
            Height = size;
            VerticalAlignment = VerticalAlignment.Center;
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            Children.Add(new Ellipse
            {
                Stroke = Chrome.Border,
                StrokeThickness = 2,
                Margin = new Thickness(1)
            });
            var arc = new Path
            {
                Stroke = Chrome.Accent,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = Arc(size),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _spin
            };
            Children.Add(arc);
            _turn = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _poll.Tick += delegate { Refresh(); };
        }

        /// <summary>Trois quarts de cercle, du haut vers la gauche (sens
        /// horaire) — le quart manquant fait lire la rotation.</summary>
        private static Geometry Arc(double size)
        {
            var radius = size / 2 - 1;
            var center = size / 2;
            var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false };
            figure.Segments.Add(new ArcSegment(new Point(center - radius, center), new Size(radius, radius),
                0, true, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        /// <summary>Vrai quand l'anneau a une raison de tourner.</summary>
        public bool IsBusy
        {
            get
            {
                if (_depth > 0) return true;
                foreach (var watch in _watches)
                {
                    try { if (watch()) return true; }
                    catch (Exception) { } // une sonde ne doit jamais faire tomber l'anneau
                }
                return false;
            }
        }

        public void Begin()
        {
            _depth++;
            Refresh();
        }

        public void End()
        {
            if (_depth > 0) _depth--;
            Refresh();
        }

        /// <summary>Une sonde de travail de fond (correcteur, préchauffage) :
        /// tant qu'elle rend vrai, l'anneau tourne.</summary>
        public void Watch(Func<bool> busy)
        {
            _watches.Add(busy);
            if (!_poll.IsEnabled) _poll.Start();
        }

        /// <summary>Un travail SYNCHRONE du fil d'interface : l'anneau paraît,
        /// WPF le dessine (Pump), le travail se fait, l'anneau se retire.
        /// Il ne tourne pas pendant (le fil est pris) mais il est là.</summary>
        public void Run(Action work)
        {
            Begin();
            Pump();
            try { work(); }
            finally { End(); }
        }

        /// <summary>Laisse WPF rendre ce qui est en attente (dont l'anneau qui
        /// vient d'apparaître) avant un travail qui va bloquer le fil.</summary>
        public void Pump()
        {
            try { Dispatcher.Invoke(DispatcherPriority.Render, new Action(delegate { })); }
            catch (Exception) { } // en arrêt d'application, le Dispatcher peut refuser
        }

        private void Refresh()
        {
            var busy = IsBusy;
            if (busy == _spinning) return;
            _spinning = busy;
            Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            // L'animation ne tourne que visible : un anneau caché ne coûte rien.
            if (busy) _spin.BeginAnimation(RotateTransform.AngleProperty, _turn);
            else _spin.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}

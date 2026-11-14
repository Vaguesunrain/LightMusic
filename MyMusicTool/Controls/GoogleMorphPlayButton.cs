using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace MyMusicTool.Controls
{
    public class GoogleMorphPlayButton : Button
    {
        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(GoogleMorphPlayButton),
                new FrameworkPropertyMetadata(false, OnIsPlayingChanged));

        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            set => SetValue(IsPlayingProperty, value);
        }

        private double _morphProgress; // 0.0 = 12齿齿轮(暂停态), 1.0 = 圆角正方形(播放态)
        private double _targetProgress;
        private double _gearRotation;
        private bool _isAnimating;
        private DateTime _lastTick = DateTime.UtcNow;


        public GoogleMorphPlayButton()
        {
            Cursor = System.Windows.Input.Cursors.Hand;
            Unloaded += (_, _) => StopAnimation();
        }

        private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GoogleMorphPlayButton btn)
            {
                btn._targetProgress = (bool)e.NewValue ? 1.0 : 0.0;
                btn.StartAnimation();
            }
        }

        private void StartAnimation()
        {
            if (_isAnimating) return;
            _lastTick = DateTime.UtcNow;
            CompositionTarget.Rendering += OnRendering;
            _isAnimating = true;
        }

        private void StopAnimation()
        {
            if (_isAnimating)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isAnimating = false;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double dt = Math.Min(0.05, (now - _lastTick).TotalSeconds);
            _lastTick = now;

            double diff = _targetProgress - _morphProgress;
            if (Math.Abs(diff) < 0.005)
            {
                _morphProgress = _targetProgress;
                StopAnimation();
            }
            else
            {
                _morphProgress += diff * Math.Min(1.0, dt * 14.0);
                _gearRotation += dt * 45.0;
            }

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 4) return;

            double cx = ActualWidth / 2.0;
            double cy = ActualHeight / 2.0;
            double baseRadius = size * 0.44;
            if (IsMouseOver) baseRadius *= 1.05;

            // t=0: 12齿波浪齿轮；t=1: 圆角正方形(Squircle)
            double t = _morphProgress;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                const int samples = 144;
                for (int i = 0; i <= samples; i++)
                {
                    double theta = (i / (double)samples) * Math.PI * 2.0;

                    // 齿轮轮廓半径 (12个圆润波齿)
                    double gearR = baseRadius * (0.93 + 0.085 * Math.Cos(12.0 * (theta + _gearRotation * Math.PI / 180.0)));

                    // 圆角正方形超椭圆轮廓半径 (n = 4.2)
                    double cosT = Math.Abs(Math.Cos(theta));
                    double sinT = Math.Abs(Math.Sin(theta));
                    double squircleR = (baseRadius * 0.96) / Math.Pow(Math.Pow(cosT, 4.2) + Math.Pow(sinT, 4.2), 1.0 / 4.2);

                    double r = gearR * (1.0 - t) + squircleR * t;
                    var pt = new Point(cx + r * Math.Cos(theta), cy + r * Math.Sin(theta));

                    if (i == 0) ctx.BeginFigure(pt, true, true);
                    else ctx.LineTo(pt, true, true);
                }
            }
            geo.Freeze();

            dc.DrawGeometry(Background ?? Brushes.DodgerBlue, null, geo);
            base.OnRender(dc);
        }
    }
}
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyMusicTool.Controls
{
    public enum ProgressLineStyle
    {
        Wavy,       
        Straight,  
        Dashed,     
        ThickPill  
    }

    public class ExpressiveProgressSlider : Slider
    {
        public static readonly DependencyProperty LineStyleProperty =
            DependencyProperty.Register(nameof(LineStyle), typeof(ProgressLineStyle), typeof(ExpressiveProgressSlider),
                new FrameworkPropertyMetadata(ProgressLineStyle.Wavy, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualStateChanged));

        public static readonly DependencyProperty ActiveBrushProperty =
            DependencyProperty.Register(nameof(ActiveBrush), typeof(Brush), typeof(ExpressiveProgressSlider),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, OnPenPropertyChanged));

        public static readonly DependencyProperty InactiveBrushProperty =
            DependencyProperty.Register(nameof(InactiveBrush), typeof(Brush), typeof(ExpressiveProgressSlider),
                new FrameworkPropertyMetadata(CreateDefaultInactiveBrush(), FrameworkPropertyMetadataOptions.AffectsRender, OnPenPropertyChanged));

        public static readonly DependencyProperty ThumbBrushProperty =
            DependencyProperty.Register(nameof(ThumbBrush), typeof(Brush), typeof(ExpressiveProgressSlider),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsAnimatingProperty =
            DependencyProperty.Register(nameof(IsAnimating), typeof(bool), typeof(ExpressiveProgressSlider),
                new FrameworkPropertyMetadata(false, OnVisualStateChanged));

        public ProgressLineStyle LineStyle
        {
            get => (ProgressLineStyle)GetValue(LineStyleProperty);
            set => SetValue(LineStyleProperty, value);
        }

        public Brush ActiveBrush
        {
            get => (Brush)GetValue(ActiveBrushProperty);
            set => SetValue(ActiveBrushProperty, value);
        }

        public Brush InactiveBrush
        {
            get => (Brush)GetValue(InactiveBrushProperty);
            set => SetValue(InactiveBrushProperty, value);
        }

        public Brush ThumbBrush
        {
            get => (Brush)GetValue(ThumbBrushProperty);
            set => SetValue(ThumbBrushProperty, value);
        }

        public bool IsAnimating
        {
            get => (bool)GetValue(IsAnimatingProperty);
            set => SetValue(IsAnimatingProperty, value);
        }

        private Pen? _cachedActivePen;
        private Pen? _cachedInactivePen;

        private double _wavePhase;
        private bool _isHookedRendering;
        private DateTime _lastRenderTime = DateTime.UtcNow;

        private static Brush CreateDefaultInactiveBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255));
            brush.Freeze();
            return brush;
        }

        public ExpressiveProgressSlider()
        {
            IsVisibleChanged += (_, _) => UpdateAnimationHook();
            Unloaded += (_, _) => DetachRendering();
        }

        private static void OnPenPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ExpressiveProgressSlider slider)
            {
                slider.InvalidatePens();
            }
        }

        private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ExpressiveProgressSlider slider)
            {
                slider.InvalidatePens();
                slider.UpdateAnimationHook();
                slider.InvalidateVisual();
            }
        }

        private void InvalidatePens()
        {
            _cachedActivePen = null;
            _cachedInactivePen = null;
        }

        private void EnsurePens()
        {
            if (_cachedInactivePen == null)
            {
                double inactiveThickness = LineStyle == ProgressLineStyle.ThickPill ? 6.5 : 3.0;
                _cachedInactivePen = new Pen(InactiveBrush, inactiveThickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                if (_cachedInactivePen.CanFreeze) _cachedInactivePen.Freeze();
            }

            if (_cachedActivePen == null)
            {
                _cachedActivePen = LineStyle switch
                {
                    ProgressLineStyle.ThickPill => new Pen(ActiveBrush, 6.5)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    },
                    ProgressLineStyle.Dashed => new Pen(ActiveBrush, 3.8)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        DashCap = PenLineCap.Round,
                        DashStyle = new DashStyle(new double[] { 2.2, 2.0 }, 0)
                    },
                    ProgressLineStyle.Wavy => new Pen(ActiveBrush, 3.0)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round
                    },
                    _ => new Pen(ActiveBrush, 3.2) // Straight
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    }
                };
                if (_cachedActivePen.CanFreeze) _cachedActivePen.Freeze();
            }
        }

        private void UpdateAnimationHook()
        {
            bool shouldAnimate = IsVisible && IsAnimating && LineStyle == ProgressLineStyle.Wavy;
            if (shouldAnimate && !_isHookedRendering)
            {
                _lastRenderTime = DateTime.UtcNow;
                CompositionTarget.Rendering += OnCompositionRendering;
                _isHookedRendering = true;
            }
            else if (!shouldAnimate && _isHookedRendering)
            {
                DetachRendering();
            }
        }

        private void DetachRendering()
        {
            if (_isHookedRendering)
            {
                CompositionTarget.Rendering -= OnCompositionRendering;
                _isHookedRendering = false;
            }
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double dt = (now - _lastRenderTime).TotalSeconds;
            if (dt < 0.025) return; // 限制约 40fps 刷新，兼顾丝滑与低 CPU 占用
            _lastRenderTime = now;

            _wavePhase = (_wavePhase + dt * 4.2) % (Math.PI * 2.0);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 2 || height <= 2) return;

            EnsurePens();

            double centerY = height / 2.0;
            double range = Maximum - Minimum;
            double ratio = range > 0 ? Math.Clamp((Value - Minimum) / range, 0.0, 1.0) : 0.0;
            double thumbX = ratio * width;

            // 1. 绘制未播放轨道
            if (thumbX < width - 1)
            {
                dc.DrawLine(_cachedInactivePen!, new Point(Math.Max(0, thumbX), centerY), new Point(width, centerY));
            }

            // 2. 按所选线型绘制已播放进度
            if (thumbX > 0.5)
            {
                if (LineStyle == ProgressLineStyle.Wavy)
                {
                    var waveGeo = new StreamGeometry();
                    using (var ctx = waveGeo.Open())
                    {
                        const double wavelength = 22.0;
                        const double amplitude = 3.2;
                        ctx.BeginFigure(new Point(0, centerY + Math.Sin(-_wavePhase) * amplitude), false, false);

                        for (double x = 1.5; x <= thumbX; x += 1.5)
                        {
                            // 靠近滑块末端 8px 平滑收敛回中心线，避免端点翘起
                            double edgeFade = Math.Min(1.0, (thumbX - x) / 8.0);
                            double y = centerY + Math.Sin((x / wavelength) * Math.PI * 2.0 - _wavePhase) * amplitude * edgeFade;
                            ctx.LineTo(new Point(x, y), true, true);
                        }
                    }
                    waveGeo.Freeze();
                    dc.DrawGeometry(null, _cachedActivePen!, waveGeo);
                }
                else
                {
                    dc.DrawLine(_cachedActivePen!, new Point(0, centerY), new Point(thumbX, centerY));
                }
            }

            // 3. 绘制竖向胶囊滑块指示器
            double pillWidth = IsMouseOver ? 5.0 : 4.0;
            double pillHeight = IsMouseOver ? 17.0 : 14.5;
            var thumbRect = new Rect(
                Math.Clamp(thumbX - pillWidth / 2.0, 0, width - pillWidth),
                centerY - pillHeight / 2.0,
                pillWidth,
                pillHeight);
            dc.DrawRoundedRectangle(ThumbBrush, null, thumbRect, pillWidth / 2.0, pillWidth / 2.0);
        }
    }
}
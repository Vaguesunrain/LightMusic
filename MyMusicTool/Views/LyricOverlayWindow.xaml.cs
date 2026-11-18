using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;

namespace MyMusicTool.Views
{
    /// <summary>
    /// 任务栏歌词外观与位置配置模型
    /// </summary>
    public class LyricSettings
    {
        public string FontFamilyName { get; set; } = "Microsoft YaHei UI";
        public double FontSize { get; set; } = 13.5;
        public bool EnableColorProgress { get; set; } = true;
        public bool ShowMiniCover { get; set; } = true;
        public double BackgroundOpacity { get; set; } = 0.80; // 0.0 ~ 1.0
        public double OffsetX { get; set; } = 0;
        public double OffsetY { get; set; } = 0;
        public string Language { get; set; } = "zh-CN";
    }

    public partial class LyricOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private static readonly Brush HoverFallbackBgBrush = CreateFrozenBrush(215, 26, 28, 30);

        private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public event Action? PreviousClicked;
        public event Action? PlayPauseClicked;
        public event Action? NextClicked;

        private string _lastDisplayedRaw = string.Empty;
        private string _currentCoverPath = string.Empty;
        private Brush _normalBgBrush = CreateFrozenBrush(204, 26, 28, 30);
        private bool _isHovered;

        public LyricSettings Settings { get; private set; } = new();

        public LyricOverlayWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdatePosition();
            SizeChanged += (_, _) => UpdatePosition();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            // 移除 WS_EX_TRANSPARENT 以便接收鼠标悬停与点击；加入 WS_EX_NOACTIVATE 防止点击抢焦点
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        }

        /// <summary>
        /// 同步主播放器的播放/暂停图标状态
        /// </summary>
        public void UpdatePlayState(bool isPlaying)
            => OverlayPlayPauseIcon.Kind = isPlaying ? PackIconKind.Pause : PackIconKind.Play;

        // ==================== 鼠标悬停切换播控按钮 ====================

        private void LyricPillBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            _isHovered = true;
            LyricTextPanel.Opacity = 0;
            LyricTextPanel.IsHitTestVisible = false;
            HoverControlsPanel.Visibility = Visibility.Visible;

            if (Settings.BackgroundOpacity < 0.35)
            {
                LyricPillBorder.Background = HoverFallbackBgBrush;
            }
        }

        private void LyricPillBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            _isHovered = false;
            HoverControlsPanel.Visibility = Visibility.Collapsed;
            LyricTextPanel.Opacity = 1;
            LyricTextPanel.IsHitTestVisible = true;
            LyricPillBorder.Background = _normalBgBrush;
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e) => PreviousClicked?.Invoke();
        private void PlayPauseBtn_Click(object sender, RoutedEventArgs e) => PlayPauseClicked?.Invoke();
        private void NextBtn_Click(object sender, RoutedEventArgs e) => NextClicked?.Invoke();

        // ==================== 外观设置与歌词刷新 ====================

        /// <summary>
        /// 应用外观配置（字体、字号、底色透明度、低清封面开关、坐标偏移）
        /// </summary>
        public void ApplySettings(LyricSettings settings)
        {
            Settings = settings;

            try
            {
                var family = new FontFamily(settings.FontFamilyName);
                PrimaryLyricText.FontFamily = family;
                SecondaryLyricText.FontFamily = family;
            }
            catch { }

            PrimaryLyricText.FontSize = settings.FontSize;
            SecondaryLyricText.FontSize = Math.Max(9.5, settings.FontSize - 2.5);

            // 更新胶囊背景透明度（0 时完全透明）
            byte alpha = (byte)(Math.Clamp(settings.BackgroundOpacity, 0, 1) * 255);
            _normalBgBrush = CreateFrozenBrush(alpha, 26, 28, 30);
            if (!_isHovered)
            {
                LyricPillBorder.Background = _normalBgBrush;
            }

            // 若关闭颜色过渡，重置渐变偏移为全白
            if (!settings.EnableColorProgress)
            {
                PlayedColorStop.Offset = 0;
                UnplayedColorStop.Offset = 0;
            }

            RefreshMiniCoverDisplay();
            UpdatePosition();
        }

        /// <summary>
        /// 更新当前歌曲的低清封面（复用 .cache 缩略图并按 36px 解码，仅占约 5KB 内存）
        /// </summary>
        public void UpdateCover(string? coverCachePath)
        {
            _currentCoverPath = coverCachePath ?? string.Empty;
            RefreshMiniCoverDisplay();
        }

        private void RefreshMiniCoverDisplay()
        {
            if (!Settings.ShowMiniCover || string.IsNullOrEmpty(_currentCoverPath) || !File.Exists(_currentCoverPath))
            {
                MiniCoverBorder.Visibility = Visibility.Collapsed;
                MiniCoverImage.Source = null;
                return;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 36; // 极小解码尺寸，零内存压力
                bitmap.UriSource = new Uri(_currentCoverPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                MiniCoverImage.Source = bitmap;
                MiniCoverBorder.Visibility = Visibility.Visible;
            }
            catch
            {
                MiniCoverBorder.Visibility = Visibility.Collapsed;
                MiniCoverImage.Source = null;
            }
        }

        /// <summary>
        /// 更新当前歌词文本（仅在换行时触发）
        /// </summary>
        public void UpdateLyric(string rawText)
        {
            if (Visibility != Visibility.Visible) return;
            if (string.Equals(_lastDisplayedRaw, rawText, StringComparison.Ordinal)) return;

            _lastDisplayedRaw = rawText;
            ResetColorProgress();

            if (string.IsNullOrWhiteSpace(rawText))
            {
                PrimaryLyricText.Text = "♪ ...";
                SecondaryLyricText.Visibility = Visibility.Collapsed;
                return;
            }

            int newlineIdx = rawText.IndexOf('\n');
            if (newlineIdx > 0)
            {
                PrimaryLyricText.Text = rawText[..newlineIdx].Trim();
                SecondaryLyricText.Text = rawText[(newlineIdx + 1)..].Trim();
                SecondaryLyricText.Visibility = Visibility.Visible;
            }
            else
            {
                PrimaryLyricText.Text = rawText.Trim();
                SecondaryLyricText.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 更新单句歌词随时间的颜色过渡进度（0.0 ~ 1.0），仅修改 Offset 数值，零内存分配
        /// </summary>
        public void UpdateColorProgress(double progress)
        {
            if (!Settings.EnableColorProgress || Visibility != Visibility.Visible) return;

            double clamped = Math.Clamp(progress, 0.0, 1.0);
            PlayedColorStop.Offset = clamped;
            UnplayedColorStop.Offset = Math.Min(1.0, clamped + 0.02); // 2% 柔和过渡边缘
        }

        public void ResetColorProgress()
        {
            PlayedColorStop.Offset = 0;
            UnplayedColorStop.Offset = 0;
        }

        /// <summary>
        /// 随专辑主色调同步高亮歌词已唱部分颜色与边框
        /// </summary>
        public void UpdateAccentColor(Color accentColor)
        {
            Color brightAccent = Color.FromRgb(
                (byte)Math.Min(255, accentColor.R + 85),
                (byte)Math.Min(255, accentColor.G + 85),
                (byte)Math.Min(255, accentColor.B + 85));

            PlayedColorStop.Color = brightAccent;

            if (Settings.BackgroundOpacity > 0.05)
            {
                LyricPillBorder.BorderBrush = CreateFrozenBrush(140, accentColor.R, accentColor.G, accentColor.B);
            }
            else
            {
                LyricPillBorder.BorderBrush = Brushes.Transparent;
            }
        }

        public void UpdatePosition()
        {
            Rect workArea = SystemParameters.WorkArea;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double taskbarHeight = screenHeight - workArea.Bottom;

            if (taskbarHeight >= 36)
            {
                Left = 175 + Settings.OffsetX;
                Top = workArea.Bottom + (taskbarHeight - ActualHeight) / 2.0 + Settings.OffsetY;
            }
            else
            {
                Left = (workArea.Width - ActualWidth) / 2.0 + Settings.OffsetX;
                Top = workArea.Bottom - ActualHeight - 8 + Settings.OffsetY;
            }
        }
    }
}
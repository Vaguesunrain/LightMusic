using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyMusicTool.Services;

namespace MyMusicTool.Models
{
    public class CollectionItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string? _backgroundImagePath;
        public string? BackgroundImagePath
        {
            get => _backgroundImagePath;
            set
            {
                _backgroundImagePath = value;
                _cachedCustomCover = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCustomBackground));
                OnPropertyChanged(nameof(CoverImage));
            }
        }

        // 默认 Material You 柔和中性色调，设定自定义背景后自动提取 MD3 色阶覆盖
        private string _cardBgHex = "#EEF2F6";
        public string CardBgHex
        {
            get => _cardBgHex;
            set { _cardBgHex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CardBgBrush)); }
        }

        private string _accentColorHex = "#475569";
        public string AccentColorHex
        {
            get => _accentColorHex;
            set { _accentColorHex = value; OnPropertyChanged(); OnPropertyChanged(nameof(AccentBrush)); }
        }

        private string _textColorHex = "#1E293B";
        public string TextColorHex
        {
            get => _textColorHex;
            set { _textColorHex = value; OnPropertyChanged(); OnPropertyChanged(nameof(TextBrush)); }
        }

        public List<string> SongPaths { get; set; } = new();

        [JsonIgnore]
        public bool HasCustomBackground => !string.IsNullOrWhiteSpace(BackgroundImagePath) && File.Exists(BackgroundImagePath);

        [JsonIgnore]
        public string SongCountText => Lang.Format("Fmt_CollectionCount", SongPaths.Count);

        [JsonIgnore]
        public Brush CardBgBrush => CreateFrozenBrush(CardBgHex, Color.FromRgb(238, 242, 246));

        [JsonIgnore]
        public Brush AccentBrush => CreateFrozenBrush(AccentColorHex, Color.FromRgb(71, 85, 105));

        [JsonIgnore]
        public Brush TextBrush => CreateFrozenBrush(TextColorHex, Color.FromRgb(30, 41, 59));

        private string? _fallbackCoverPath;
       
        //当未设置自定义背景时，自动绑定第一首歌的封面缓存路径；空收藏夹则为 null（滞空）
       
        [JsonIgnore]
        public string? FallbackCoverPath
        {
            get => _fallbackCoverPath;
            set
            {
                if (string.Equals(_fallbackCoverPath, value, StringComparison.OrdinalIgnoreCase)) return;
                _fallbackCoverPath = value;
                _cachedFallbackCover = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CoverImage));
            }
        }

        private ImageSource? _cachedCustomCover;
        private ImageSource? _cachedFallbackCover;

        [JsonIgnore]
        public ImageSource? CoverImage
        {
            get
            {
                // 1. 优先展示自定义背景图
                if (HasCustomBackground)
                {
                    _cachedCustomCover ??= LoadFrozenThumbnail(BackgroundImagePath!, 360);
                    if (_cachedCustomCover != null) return _cachedCustomCover;
                }

                // 2. 其次展示第一首歌的封面缓存（为空则滞空返回 null）
                if (!string.IsNullOrWhiteSpace(FallbackCoverPath) && File.Exists(FallbackCoverPath))
                {
                    _cachedFallbackCover ??= LoadFrozenThumbnail(FallbackCoverPath, 160);
                    return _cachedFallbackCover;
                }

                return null;
            }
        }

        private static ImageSource? LoadFrozenThumbnail(string path, int decodeWidth)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelWidth = decodeWidth;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public void RefreshTexts() => OnPropertyChanged(nameof(SongCountText));

        public void ReleaseCoverCache()
        {
            _cachedCustomCover = null;
            _cachedFallbackCover = null;
            OnPropertyChanged(nameof(CoverImage));
        }

        private static Brush CreateFrozenBrush(string hex, Color fallback)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                var brush = new SolidColorBrush(fallback);
                brush.Freeze();
                return brush;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
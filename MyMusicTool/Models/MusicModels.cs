using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyMusicTool.Models
{
    //单首歌曲信息（纯文本元数据，极低内存占用）
    public class SongItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = "未知曲目";
        public string Artist { get; set; } = "未知艺术家";
        public string Album { get; set; } = "未知专辑";
        public uint Year { get; set; }
        public TimeSpan Duration { get; set; }
        public string DurationText => Duration.TotalHours >= 1
            ? Duration.ToString(@"hh\:mm\:ss")
            : Duration.ToString(@"mm\:ss");

        // 音质参数
        public string AudioInfoText { get; set; } = string.Empty;

        // 对应的 160px 本地缓存封面路径
        public string CoverCachePath { get; set; } = string.Empty;
    }
    // 专辑聚合信息（用于虚拟化封面墙展示）
    public class AlbumItem : INotifyPropertyChanged
    {
        public string Title { get; set; } = "未知专辑";
        public string Artist { get; set; } = "未知艺术家";
        public uint Year { get; set; }
        public string Subtitle => Year > 0 ? $"{Artist} • {Year}" : Artist;
        public string CoverCachePath { get; set; } = string.Empty;
        public List<SongItem> Songs { get; set; } = new();

        private ImageSource? _coverThumbnail;
        private bool _isCoverLoaded;

        // 按需懒加载 160px 缩略图，仅在屏幕滚动到该卡片时触发解码
        public ImageSource? CoverThumbnail
        {
            get
            {
                if (!_isCoverLoaded)
                {
                    _isCoverLoaded = true;
                    _coverThumbnail = LoadSmallThumbnail(CoverCachePath);
                }
                return _coverThumbnail;
            }
        }

        private static ImageSource? LoadSmallThumbnail(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 160; // 强制限制 160px，单张仅占约 100KB 内存
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze(); // 冻结对象以减少 WPF 渲染追踪开销
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // LRC 歌词单行模型（支持双语合并）
    public class LyricLine
    {
        public TimeSpan Time { get; set; }
        public string Text { get; set; } = string.Empty;
        
    }
}
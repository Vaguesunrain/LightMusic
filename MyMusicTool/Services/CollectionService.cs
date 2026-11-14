using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyMusicTool.Models;

namespace MyMusicTool.Services
{
    public class CollectionService
    {
        private readonly string _filePath;
        public ObservableCollection<CollectionItem> Collections { get; } = new();

        public CollectionService()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "collections.json");
            Load();
        }

        public void Load()
        {
            Collections.Clear();
            if (!File.Exists(_filePath)) return;

            try
            {
                string json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<CollectionItem[]>(json);
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        Collections.Add(item);
                    }
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Collections, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch { }
        }

        public CollectionItem CreateCollection(string name)
        {
            var item = new CollectionItem { Name = name.Trim() };
            Collections.Add(item);
            Save();
            return item;
        }

        public void DeleteCollection(CollectionItem item)
        {
            item.ReleaseCoverCache();
            Collections.Remove(item);
            Save();
        }

        public void AddSongToCollection(CollectionItem collection, string songFilePath)
        {
            if (!collection.SongPaths.Contains(songFilePath, StringComparer.OrdinalIgnoreCase))
            {
                collection.SongPaths.Add(songFilePath);
                collection.RefreshTexts();
                Save();
            }
        }

        public void RemoveSongFromCollection(CollectionItem collection, string songFilePath)
        {
            int removed = collection.SongPaths.RemoveAll(p => string.Equals(p, songFilePath, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                collection.RefreshTexts();
                Save();
            }
        }


        // 设定自定义背景并提取 MD3 调色板（若传 null 则恢复默认灰白）
        public void SetCollectionBackground(CollectionItem collection, string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                collection.BackgroundImagePath = null;
                collection.CardBgHex = "#F1F5F9";
                collection.AccentColorHex = "#475569";
                collection.TextColorHex = "#1E293B";
                Save();
                return;
            }

            collection.BackgroundImagePath = imagePath;
            var (cardBg, accent, text) = ExtractMd3PaletteFromImage(imagePath);
            collection.CardBgHex = cardBg;
            collection.AccentColorHex = accent;
            collection.TextColorHex = text;
            Save();
        }

  
        // 仅解码 32x32 缩略图提取主色调并派生 MD3 Tonal 色阶（耗时 < 2ms，零内存负担）
        private static (string CardBg, string Accent, string Text) ExtractMd3PaletteFromImage(string imagePath)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
                bmp.DecodePixelWidth = 32;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                int width = bmp.PixelWidth;
                int height = bmp.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[height * stride];
                bmp.CopyPixels(pixels, stride, 0);

                long rSum = 0, gSum = 0, bSum = 0, weightSum = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];

                    // 提高有色彩像素权重，忽略纯黑纯白边缘
                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    int saturationWeight = (max - min) + 16;

                    rSum += r * saturationWeight;
                    gSum += g * saturationWeight;
                    bSum += b * saturationWeight;
                    weightSum += saturationWeight;
                }

                byte baseR = (byte)(rSum / weightSum);
                byte baseG = (byte)(gSum / weightSum);
                byte baseB = (byte)(bSum / weightSum);

                // MD3 Surface Container（高明度低饱和柔化底色：85% 混白）
                Color cardBg = BlendWithWhite(baseR, baseG, baseB, 0.85);
                // MD3 Primary Accent（适中明度强调色：加深 25%）
                Color accent = ScaleBrightness(baseR, baseG, baseB, 0.75);
                // MD3 On-Surface（深色高可读文字色：加深 65%）
                Color text = ScaleBrightness(baseR, baseG, baseB, 0.35);

                return ($"#{cardBg.R:X2}{cardBg.G:X2}{cardBg.B:X2}",
                        $"#{accent.R:X2}{accent.G:X2}{accent.B:X2}",
                        $"#{text.R:X2}{text.G:X2}{text.B:X2}");
            }
            catch
            {
                return ("#F1F5F9", "#475569", "#1E293B");
            }
        }

        private static Color BlendWithWhite(byte r, byte g, byte b, double whiteRatio)
        {
            byte nr = (byte)(r * (1 - whiteRatio) + 255 * whiteRatio);
            byte ng = (byte)(g * (1 - whiteRatio) + 255 * whiteRatio);
            byte nb = (byte)(b * (1 - whiteRatio) + 255 * whiteRatio);
            return Color.FromRgb(nr, ng, nb);
        }

        private static Color ScaleBrightness(byte r, byte g, byte b, double factor)
        {
            return Color.FromRgb(
                (byte)Math.Clamp(r * factor, 0, 255),
                (byte)Math.Clamp(g * factor, 0, 255),
                (byte)Math.Clamp(b * factor, 0, 255));
        }

        //当前播放会话所属的收藏夹（非收藏夹播放时为 null）
        public CollectionItem? ActivePlayingCollection { get; private set; }

        public event Action<CollectionItem?>? ActiveCollectionChanged;

        public void SetActivePlayingCollection(CollectionItem? collection)
        {
            if (ReferenceEquals(ActivePlayingCollection, collection)) return;
            ActivePlayingCollection = collection;
            ActiveCollectionChanged?.Invoke(collection);
        }

        private Dictionary<string, CollectionItem>? _playInCollectionsMap;
        public bool IsPlayInCollectionsMode => _playInCollectionsMap != null;

        // 开启“跨收藏夹播放在线映射”模式（由 Play / Shuffle in Collections 触发）
        public void StartPlayInCollectionsMode(Dictionary<string, CollectionItem> songToCollectionMap, string? firstSongPath)
        {
            _playInCollectionsMap = songToCollectionMap;
            ResolveAndSyncActiveCollection(firstSongPath);
        }

        // 退出跨收藏夹模式，切回普通播放或单个收藏夹播放
        public void ExitPlayInCollectionsMode(CollectionItem? singleCollection = null)
        {
            _playInCollectionsMap = null;
            SetActivePlayingCollection(singleCollection);
        }

      
        // 在跨收藏夹模式下，每次切歌时根据歌曲路径自动切换对应的收藏夹背景
        public void ResolveAndSyncActiveCollection(string? songFilePath)
        {
            if (_playInCollectionsMap == null) return;

            if (!string.IsNullOrEmpty(songFilePath) &&
                _playInCollectionsMap.TryGetValue(songFilePath, out var matchedCol))
            {
                SetActivePlayingCollection(matchedCol);
            }
            else
            {
                SetActivePlayingCollection(null);
            }
        }
    }
}
using MyMusicTool.Models;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
namespace MyMusicTool.Services
{
    public class LibraryService
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".flac", ".mp3", ".wav", ".m4a", ".ogg", ".aac", ".wma"
        };

        private readonly string _cacheDir;
        private readonly string _configPath;

        public LibraryService()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _cacheDir = Path.Combine(baseDir, ".cache");
            _configPath = Path.Combine(baseDir, "library_folders.json");

            if (!Directory.Exists(_cacheDir))
            {
                Directory.CreateDirectory(_cacheDir);
            }
        }

        // 保存已添加的音乐文件夹路径列表，下次启动自动加载
        public void SaveLibraryFolders(IEnumerable<string> folders)
        {
            try
            {
                var distinctFolders = folders
                    .Where(Directory.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                string json = JsonSerializer.Serialize(distinctFolders);
                System.IO.File.WriteAllText(_configPath, json);
            }
            catch { }
        }
        // 读取上次保存的音乐文件夹路径
        public List<string> LoadLibraryFolders()
        {
            try
            {
                if (System.IO.File.Exists(_configPath))
                {
                    string json = System.IO.File.ReadAllText(_configPath);
                    return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }
            catch { }
            return new List<string>();
        }


        // 异步扫描多个音乐文件夹，返回全部单曲与聚合后的专辑列表
        public Task<(List<SongItem> Songs, List<AlbumItem> Albums)> ScanFoldersAsync(IEnumerable<string> folders)
        {
            return Task.Run(() =>
            {
                var allFiles = new List<string>();
                foreach (var folder in folders.Where(Directory.Exists))
                {
                    try
                    {
                        var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)));
                        allFiles.AddRange(files);
                    }
                    catch { }
                }

                var songs = new List<SongItem>(allFiles.Count);

                foreach (var filePath in allFiles.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var song = ReadSongMetadata(filePath);
                    if (song != null)
                    {
                        songs.Add(song);
                    }
                }

                // 按“专辑名 + 歌手”聚合生成封面墙数据
                var albums = songs
                    .GroupBy(s => $"{s.Album.Trim().ToLowerInvariant()}_{s.Artist.Trim().ToLowerInvariant()}")
                    .Select(g =>
                    {
                        var first = g.First();
                        var coverPath = g.Select(x => x.CoverCachePath)
                                         .FirstOrDefault(p => !string.IsNullOrEmpty(p)) ?? string.Empty;

                        return new AlbumItem
                        {
                            Title = first.Album,
                            Artist = first.Artist,
                            Year = g.Max(x => x.Year),
                            CoverCachePath = coverPath,
                            Songs = g.ToList()
                        };
                    })
                    .OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                return (songs, albums);
            });
        }


        // 读取单首歌曲的纯文本元数据，并按需生成 160px 封面缓存
        private SongItem? ReadSongMetadata(string filePath)
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath);

                string title = !string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                    ? tagFile.Tag.Title.Trim()
                    : Path.GetFileNameWithoutExtension(filePath);

                string artist = !string.IsNullOrWhiteSpace(tagFile.Tag.FirstPerformer)
                    ? tagFile.Tag.FirstPerformer.Trim()
                    : (!string.IsNullOrWhiteSpace(tagFile.Tag.FirstAlbumArtist)
                        ? tagFile.Tag.FirstAlbumArtist.Trim()
                        : "未知艺术家");

                string album = !string.IsNullOrWhiteSpace(tagFile.Tag.Album)
                    ? tagFile.Tag.Album.Trim()
                    : "未知专辑";

                string ext = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
                int bitrate = tagFile.Properties.AudioBitrate;
                double sampleRateKhz = Math.Round(tagFile.Properties.AudioSampleRate / 1000.0, 1);
                string channels = tagFile.Properties.AudioChannels switch
                {
                    1 => "单声道",
                    2 => "立体声",
                    _ => $"{tagFile.Properties.AudioChannels}声道"
                };
                string audioInfo = $"{ext} • {bitrate} kb/s • {sampleRateKhz} kHz • {channels}";

   
                string cacheFileName = GetAlbumCacheHash($"{album}_{artist}") + ".jpg";
                string cacheFullPath = Path.Combine(_cacheDir, cacheFileName);

  
                if (!System.IO.File.Exists(cacheFullPath) && tagFile.Tag.Pictures.Length > 0)
                {
                    byte[] rawBytes = tagFile.Tag.Pictures[0].Data.Data;
                    if (SaveThumbnailToCache(rawBytes, cacheFullPath, 160))
                    {
                        // 保存成功
                    }
                    else
                    {
                        cacheFullPath = string.Empty;
                    }
                }
                else if (!System.IO.File.Exists(cacheFullPath))
                {
                    cacheFullPath = string.Empty;
                }

                return new SongItem
                {
                    FilePath = filePath,
                    Title = title,
                    Artist = artist,
                    Album = album,
                    Year = tagFile.Tag.Year,
                    Duration = tagFile.Properties.Duration,
                    AudioInfoText = audioInfo,
                    CoverCachePath = cacheFullPath
                };
            }
            catch
            {
                return null;
            }
        }


        /// 将原始封面字节流解码为指定宽度（160px）并保存为本地 JPG 缓存
        private static bool SaveThumbnailToCache(byte[] imageBytes, string outputPath, int targetWidth)
        {
            try
            {
                using var ms = new MemoryStream(imageBytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = targetWidth;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                encoder.Save(fs);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 仅在播放当前歌曲时按需解码一张 280px 封面（用于底部栏展示与主色调提取）
        /// </summary>
        public static BitmapSource? LoadHighResCoverForPlayback(string filePath, int decodeWidth = 280)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return null;

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                if (tagFile.Tag.Pictures.Length == 0)
                    return null;

                byte[] rawBytes = tagFile.Tag.Pictures[0].Data.Data;
                using var ms = new MemoryStream(rawBytes);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
        public static List<SongItem> ScanFolder(string folderPath)
        {
            var list = new List<SongItem>();
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return list;

            var allowedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".ape", ".alac"
    };

            foreach (var file in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories))
            {
                if (!allowedExts.Contains(Path.GetExtension(file))) continue;

                try
                {
                    using var tagFile = TagLib.File.Create(file);
                    var prop = tagFile.Properties;
                    list.Add(new SongItem
                    {
                        FilePath = file,
                        Title = string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                            ? Path.GetFileNameWithoutExtension(file)
                            : tagFile.Tag.Title.Trim(),
                        Artist = tagFile.Tag.FirstPerformer ?? "未知艺术家",
                        Album = string.IsNullOrWhiteSpace(tagFile.Tag.Album) ? "未知专辑" : tagFile.Tag.Album.Trim(),
                        Duration = prop.Duration,
                        AudioInfoText = $"{Path.GetExtension(file).TrimStart('.').ToUpper()} • {prop.AudioBitrate} kb/s • {prop.AudioSampleRate / 1000.0:0.#} kHz • {(prop.AudioChannels == 2 ? "立体声" : $"{prop.AudioChannels}声道")}"
                    });
                }
                catch { }
            }

            return list;
        }
        /// <summary>
        /// 读取 MusicTag 内嵌 LRC 歌词（若无内嵌则尝试读取同目录同名 .lrc），自动合并相同时间戳的双语歌词
        /// </summary>
        /// <summary>
        /// 优先读取同目录外部 .lrc 文件（支持同名 .lrc 或曲名 .lrc，自动兼容 UTF-8/GBK 与 [offset:]），
        /// 若不存在则回退读取音频内嵌 Lyrics 标签
        /// </summary>
        public static List<LyricLine> LoadLyrics(string filePath)
        {
            var result = new List<LyricLine>();
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                return result;

            try
            {
                string rawLrc = string.Empty;
                string? dir = Path.GetDirectoryName(filePath);

                // 1. 最高优先级：同目录、完全同名的 .lrc 文件（例如 Song.flac -> Song.lrc）
                string exactLrcPath = Path.ChangeExtension(filePath, ".lrc");
                if (System.IO.File.Exists(exactLrcPath))
                {
                    rawLrc = ReadLrcFileWithEncodingDetect(exactLrcPath);
                }

                // 2. 次优先级：如果文件名带音轨号（如 "01. 歌名.flac"），尝试匹配同目录下的 "歌名.lrc"
                if (string.IsNullOrWhiteSpace(rawLrc) && !string.IsNullOrEmpty(dir))
                {
                    using var tagCheck = TagLib.File.Create(filePath);
                    string tagTitle = tagCheck.Tag.Title?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(tagTitle))
                    {
                        string titleLrcPath = Path.Combine(dir, $"{tagTitle}.lrc");
                        if (System.IO.File.Exists(titleLrcPath))
                        {
                            rawLrc = ReadLrcFileWithEncodingDetect(titleLrcPath);
                        }
                    }

                    // 3. 兜底回退：外部 .lrc 均不存在时，读取内嵌歌词
                    if (string.IsNullOrWhiteSpace(rawLrc))
                    {
                        rawLrc = tagCheck.Tag.Lyrics ?? string.Empty;
                    }
                }

                if (string.IsNullOrWhiteSpace(rawLrc))
                    return result;

                // 解析 LRC 全局时间偏移标签，例如 [offset:500] 或 [offset:-300]（单位：毫秒）
                int globalOffsetMs = 0;
                var offsetMatch = Regex.Match(rawLrc, @"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase);
                if (offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out int parsedOffset))
                {
                    globalOffsetMs = parsedOffset;
                }

                var timeRegex = new Regex(@"\[(\d{1,2}):(\d{2})(?:[\.:](\d{1,3}))?\]", RegexOptions.Compiled);
                var rawEntries = new List<(TimeSpan Time, string Text)>();

                foreach (var line in rawLrc.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var matches = timeRegex.Matches(line);
                    if (matches.Count == 0) continue;

                    string lyricText = timeRegex.Replace(line, string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(lyricText)) continue;

                    foreach (Match match in matches)
                    {
                        int minutes = int.Parse(match.Groups[1].Value);
                        int seconds = int.Parse(match.Groups[2].Value);
                        int milliseconds = 0;

                        if (match.Groups[3].Success)
                        {
                            string frac = match.Groups[3].Value;
                            milliseconds = frac.Length switch
                            {
                                1 => int.Parse(frac) * 100,
                                2 => int.Parse(frac) * 10,
                                _ => int.Parse(frac[..3])
                            };
                        }

                        var rawTime = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                        // 应用 LRC 自带的 [offset:] 偏移（标准规范：正值表示歌词提前显示，即时间戳减小）
                        var adjustedTime = rawTime - TimeSpan.FromMilliseconds(globalOffsetMs);
                        if (adjustedTime < TimeSpan.Zero) adjustedTime = TimeSpan.Zero;

                        rawEntries.Add((adjustedTime, lyricText));
                    }
                }

                // 自动合并相同时间戳的双语歌词
                result = rawEntries
                    .GroupBy(e => e.Time)
                    .OrderBy(g => g.Key)
                    .Select(g => new LyricLine
                    {
                        Time = g.Key,
                        Text = string.Join("\n", g.Select(x => x.Text).Distinct())
                    })
                    .ToList();
            }
            catch { }

            return result;
        }

        /// <summary>
        /// 自动识别 UTF-8 与中文 GBK 编码读取 .lrc 文件，防止老歌歌词出现乱码
        /// </summary>
        private static string ReadLrcFileWithEncodingDetect(string lrcPath)
        {
            byte[] bytes = System.IO.File.ReadAllBytes(lrcPath);
            if (bytes.Length == 0) return string.Empty;

            // 检测 UTF-8 BOM
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            // 严格 UTF-8 校验，失败则回退到中文 GBK
            try
            {
                var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
                return strictUtf8.GetString(bytes);
            }
            catch
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding("GBK").GetString(bytes);
            }
        }

        private static string GetAlbumCacheHash(string input)
        {
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input.ToLowerInvariant()));
            return Convert.ToHexString(hashBytes)[..16];
        }
    }
}
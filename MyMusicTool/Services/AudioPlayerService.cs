using MaterialDesignThemes.Wpf;
using MyMusicTool.Models;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyMusicTool.Services
{
    public enum RepeatMode
    {
        ListLoop,   
        SingleLoop, 
        Sequence    
    }

    public class AudioPlayerService : IDisposable
    {
        #region libmpv 原生 P/Invoke 绑定（仅暴露纯音频播放所需最小接口）
        private const string MpvDll = "libmpv-2.dll";
        private const int MPV_FORMAT_DOUBLE = 5;
        private const int MPV_EVENT_END_FILE = 7;
        private const int MPV_END_FILE_REASON_EOF = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct MpvEvent
        {
            public int EventId;
            public int Error;
            public ulong ReplyUserdata;
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MpvEventEndFile
        {
            public int Reason;
            public int Error;
        }

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_create();

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_initialize(IntPtr ctx);

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_option_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_property_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_get_property(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            int format,
            out double data);

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_command(IntPtr ctx, IntPtr[] args);

        [DllImport(MpvDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
        #endregion

        private readonly object _mpvLock = new();
        private IntPtr _mpvHandle;
        private Thread? _eventThread;
        private volatile bool _isDisposed;
        private bool _isPlaying;
        private readonly Random _random = new();

        public ObservableCollection<SongItem> Queue { get; } = new();
        public int CurrentIndex { get; private set; } = -1;
        public SongItem? CurrentSong => (CurrentIndex >= 0 && CurrentIndex < Queue.Count) ? Queue[CurrentIndex] : null;

        public BitmapSource? CurrentCover { get; private set; }
        public List<LyricLine> CurrentLyrics { get; private set; } = new();

        public bool IsShuffle { get; set; }
        public RepeatMode RepeatMode { get; set; } = RepeatMode.ListLoop;

        private float _volume = 0.8f;
        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0f, 1f);
                SetMpvProperty("volume", (_volume * 100.0).ToString("F1", CultureInfo.InvariantCulture));
            }
        }

        private bool _isMuted;
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                _isMuted = value;
                SetMpvProperty("mute", _isMuted ? "yes" : "no");
            }
        }

        public bool IsPlaying => _isPlaying;

        // 直接读取 libmpv 硬件输出补偿后的精确时间轴
  
        public TimeSpan CurrentTime
        {
            get
            {
                lock (_mpvLock)
                {
                    if (_mpvHandle == IntPtr.Zero) return TimeSpan.Zero;
                    if (mpv_get_property(_mpvHandle, "time-pos", MPV_FORMAT_DOUBLE, out double seconds) >= 0 && seconds >= 0)
                    {
                        return TimeSpan.FromSeconds(seconds);
                    }
                    return TimeSpan.Zero;
                }
            }
        }

        public TimeSpan TotalTime
        {
            get
            {
                lock (_mpvLock)
                {
                    if (_mpvHandle == IntPtr.Zero) return TimeSpan.Zero;
                    if (mpv_get_property(_mpvHandle, "duration", MPV_FORMAT_DOUBLE, out double seconds) >= 0 && seconds > 0)
                    {
                        return TimeSpan.FromSeconds(seconds);
                    }
                    return CurrentSong?.Duration ?? TimeSpan.Zero;
                }
            }
        }

        public event Action<SongItem?>? SongChanged;
        public event Action<bool>? PlayStateChanged;
        public event Action<Color, Color>? ThemeColorsChanged;

        public AudioPlayerService()
        {
            InitMpvEngine();
        }

        private void InitMpvEngine()
        {
            _mpvHandle = mpv_create();
            if (_mpvHandle == IntPtr.Zero) return;

            // 纯音频模式：彻底关闭视频输出、视频轨、封面视频流与字幕引擎
            mpv_set_option_string(_mpvHandle, "vo", "null");
            mpv_set_option_string(_mpvHandle, "vid", "no");
            mpv_set_option_string(_mpvHandle, "audio-display", "no");
            mpv_set_option_string(_mpvHandle, "sub", "no");

            // 限制预读缓存为 2MB，兼顾低内存与本地秒开
            mpv_set_option_string(_mpvHandle, "demuxer-max-bytes", "2M");
            mpv_set_option_string(_mpvHandle, "demuxer-max-back-bytes", "1M");

            // 开启帧级高精度跳转与无缝切歌
            mpv_set_option_string(_mpvHandle, "hr-seek", "yes");
            mpv_set_option_string(_mpvHandle, "gapless-audio", "yes");
            mpv_set_option_string(_mpvHandle, "keep-open", "no");
            mpv_set_option_string(_mpvHandle, "volume", (_volume * 100.0).ToString("F1", CultureInfo.InvariantCulture));

            if (mpv_initialize(_mpvHandle) < 0)
            {
                mpv_terminate_destroy(_mpvHandle);
                _mpvHandle = IntPtr.Zero;
                return;
            }

            // 启动轻量事件监听线程（在 C 内部休眠等待，平时 0% CPU 占用）
            _eventThread = new Thread(MpvEventLoop)
            {
                IsBackground = true,
                Name = "MpvAudioEventLoop"
            };
            _eventThread.Start();
        }

        private void MpvEventLoop()
        {
            while (!_isDisposed && _mpvHandle != IntPtr.Zero)
            {
                IntPtr evPtr = mpv_wait_event(_mpvHandle, 0.5);
                if (evPtr == IntPtr.Zero || _isDisposed) continue;

                var ev = Marshal.PtrToStructure<MpvEvent>(evPtr);
                if (ev.EventId == MPV_EVENT_END_FILE && ev.Data != IntPtr.Zero)
                {
                    var endInfo = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                    // 仅当曲目自然播放到末尾 (EOF) 时自动切下一首，手动切歌/停止不会误触发
                    if (endInfo.Reason == MPV_END_FILE_REASON_EOF)
                    {
                        PlayNext(isAutoAdvance: true);
                    }
                }
            }
        }

        public void PlayQueue(IEnumerable<SongItem> songs, int startIndex = 0)
        {
            if (songs == null) return;
            var list = songs.ToList();
            if (list.Count == 0) return;

            Queue.Clear();
            foreach (var item in list)
            {
                Queue.Add(item);
            }
            PlayAt(Math.Clamp(startIndex, 0, Queue.Count - 1));
        }

        public void RemoveFromQueue(int index)
        {
            if (index < 0 || index >= Queue.Count) return;

            bool removingCurrent = (index == CurrentIndex);
            Queue.RemoveAt(index);

            if (Queue.Count == 0)
            {
                StopInternal();
                CurrentIndex = -1;
                CurrentCover = null;
                CurrentLyrics.Clear();
                SongChanged?.Invoke(null);
                return;
            }

            if (index < CurrentIndex)
            {
                CurrentIndex--;
            }
            else if (removingCurrent)
            {
                PlayAt(Math.Min(index, Queue.Count - 1));
            }
        }

        public void ClearQueue()
        {
            Queue.Clear();
            StopInternal();
            CurrentIndex = -1;
            CurrentCover = null;
            CurrentLyrics.Clear();
            SongChanged?.Invoke(null);
        }

        public void PlayAt(int index)
        {
            if (index < 0 || index >= Queue.Count || _mpvHandle == IntPtr.Zero) return;

            CurrentIndex = index;
            var song = Queue[CurrentIndex];

            ExecuteMpvCommand("loadfile", song.FilePath, "replace");
            SetMpvProperty("pause", "no");
            _isPlaying = true;

            CurrentCover = null;
            CurrentCover = LibraryService.LoadHighResCoverForPlayback(song.FilePath, 280);
            CurrentLyrics = LibraryService.LoadLyrics(song.FilePath);

            UpdateDynamicThemeFromCover(CurrentCover);

            SongChanged?.Invoke(song);
            PlayStateChanged?.Invoke(true);
        }

        public void TogglePlayPause()
        {
            if (_mpvHandle == IntPtr.Zero) return;

            if (CurrentSong == null)
            {
                if (Queue.Count > 0) PlayAt(Math.Max(0, CurrentIndex));
                return;
            }

            _isPlaying = !_isPlaying;
            SetMpvProperty("pause", _isPlaying ? "no" : "yes");
            PlayStateChanged?.Invoke(_isPlaying);
        }

        public void Seek(TimeSpan position)
        {
            if (_mpvHandle == IntPtr.Zero || CurrentSong == null) return;

            double maxSec = Math.Max(0, TotalTime.TotalSeconds - 0.05);
            double targetSec = Math.Clamp(position.TotalSeconds, 0, maxSec);
            ExecuteMpvCommand("seek", targetSec.ToString("F3", CultureInfo.InvariantCulture), "absolute+exact");
        }

        public void PlayNext(bool isAutoAdvance = false)
        {
            if (Queue.Count == 0) return;

            if (isAutoAdvance && RepeatMode == RepeatMode.SingleLoop)
            {
                PlayAt(CurrentIndex);
                return;
            }

            int nextIndex;
            if (IsShuffle && Queue.Count > 1)
            {
                do
                {
                    nextIndex = _random.Next(Queue.Count);
                } while (nextIndex == CurrentIndex);
            }
            else
            {
                nextIndex = CurrentIndex + 1;
                if (nextIndex >= Queue.Count)
                {
                    if (isAutoAdvance && RepeatMode == RepeatMode.Sequence)
                    {
                        StopInternal();
                        return;
                    }
                    nextIndex = 0;
                }
            }

            PlayAt(nextIndex);
        }

        public void PlayPrevious()
        {
            if (Queue.Count == 0) return;

            if (CurrentTime.TotalSeconds > 3)
            {
                Seek(TimeSpan.Zero);
                return;
            }

            int prevIndex = CurrentIndex - 1;
            if (prevIndex < 0) prevIndex = Queue.Count - 1;
            PlayAt(prevIndex);
        }

        private void StopInternal()
        {
            ExecuteMpvCommand("stop");
            _isPlaying = false;
            PlayStateChanged?.Invoke(false);
        }

        private void SetMpvProperty(string name, string value)
        {
            lock (_mpvLock)
            {
                if (_mpvHandle != IntPtr.Zero && !_isDisposed)
                {
                    mpv_set_property_string(_mpvHandle, name, value);
                }
            }
        }

        private void ExecuteMpvCommand(params string[] args)
        {
            lock (_mpvLock)
            {
                if (_mpvHandle == IntPtr.Zero || _isDisposed) return;

                IntPtr[] ptrs = new IntPtr[args.Length + 1];
                try
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
                    }
                    ptrs[args.Length] = IntPtr.Zero;
                    mpv_command(_mpvHandle, ptrs);
                }
                finally
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (ptrs[i] != IntPtr.Zero)
                        {
                            Marshal.FreeCoTaskMem(ptrs[i]);
                        }
                    }
                }
            }
        }

        public int GetCurrentLyricIndex(TimeSpan position)
        {
            if (CurrentLyrics.Count == 0) return -1;

            int left = 0;
            int right = CurrentLyrics.Count - 1;
            int result = -1;

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                if (CurrentLyrics[mid].Time <= position)
                {
                    result = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }
            return result;
        }

        public void ReleaseCoverCacheForBackground()
        {
            CurrentCover = null;
            GC.Collect(2, GCCollectionMode.Optimized);
        }

        private void UpdateDynamicThemeFromCover(BitmapSource? bitmap)
        {
            Color primaryColor = Color.FromRgb(74, 101, 114);

            if (bitmap != null)
            {
                try
                {
                    var formatted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                    int width = formatted.PixelWidth;
                    int height = formatted.PixelHeight;
                    int stride = width * 4;
                    byte[] pixels = new byte[height * stride];
                    formatted.CopyPixels(pixels, stride, 0);

                    long rSum = 0, gSum = 0, bSum = 0, count = 0;

                    for (int y = 0; y < height; y += 4)
                    {
                        int rowOffset = y * stride;
                        for (int x = 0; x < width; x += 4)
                        {
                            int idx = rowOffset + x * 4;
                            byte b = pixels[idx];
                            byte g = pixels[idx + 1];
                            byte r = pixels[idx + 2];

                            int max = Math.Max(r, Math.Max(g, b));
                            int min = Math.Min(r, Math.Min(g, b));
                            int delta = max - min;

                            if (max > 45 && min < 225 && delta > 28)
                            {
                                rSum += r;
                                gSum += g;
                                bSum += b;
                                count++;
                            }
                        }
                    }

                    if (count > 10)
                    {
                        byte avgR = (byte)(rSum / count);
                        byte avgG = (byte)(gSum / count);
                        byte avgB = (byte)(bSum / count);
                        primaryColor = AdjustForMd3Primary(Color.FromRgb(avgR, avgG, avgB));
                    }
                }
                catch { }
            }

            Color bottomBarBg = Color.FromRgb(
                (byte)(255 - (255 - primaryColor.R) * 0.14),
                (byte)(255 - (255 - primaryColor.G) * 0.14),
                (byte)(255 - (255 - primaryColor.B) * 0.14));

            try
            {
                var paletteHelper = new PaletteHelper();
                var theme = paletteHelper.GetTheme();
                theme.SetPrimaryColor(primaryColor);
                theme.SetSecondaryColor(primaryColor);
                paletteHelper.SetTheme(theme);
            }
            catch { }

            ThemeColorsChanged?.Invoke(primaryColor, bottomBarBg);
        }

        private static Color AdjustForMd3Primary(Color c)
        {
            double brightness = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            if (brightness > 0.62)
            {
                double factor = 0.62 / brightness;
                return Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
            }
            if (brightness < 0.25)
            {
                return Color.FromRgb(
                    (byte)Math.Min(255, c.R + 45),
                    (byte)Math.Min(255, c.G + 45),
                    (byte)Math.Min(255, c.B + 45));
            }
            return c;
        }

        public void Dispose()
        {
            lock (_mpvLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                if (_mpvHandle != IntPtr.Zero)
                {
                    mpv_terminate_destroy(_mpvHandle);
                    _mpvHandle = IntPtr.Zero;
                }
            }
            CurrentCover = null;
        }
    }
}
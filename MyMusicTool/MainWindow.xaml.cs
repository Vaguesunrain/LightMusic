using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using MyMusicTool.Controls;
using MyMusicTool.Models;
using MyMusicTool.Services;
using MyMusicTool.Views;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace MyMusicTool
{
    public partial class MainWindow : Window
    {
        private static readonly Brush ActiveDarkBrush = CreateFrozenBrush(15, 23, 42);
        private static readonly Brush InactiveGrayBrush = CreateFrozenBrush(148, 163, 184);

        private static Brush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private readonly LibraryService _library = new();
        private readonly AudioPlayerService _player = new();
        private readonly LyricOverlayWindow _lyricWindow = new();
        private readonly DispatcherTimer _progressTimer;
        private readonly DispatcherTimer _releaseFullPlayerTimer;

        private List<string> _libraryFolders = new();
        private List<SongItem> _allSongs = new();
        private List<AlbumItem> _allAlbums = new();
        private List<SongItem> _currentSongListContext = new();
        private NowPlayingView? _nowPlayingView;

        private bool _isDraggingSlider;
        private bool _justFinishedSliderDrag;
        private int _lastLyricIndex = -2;
        private bool _isInSubDetailView;

        // 全屏状态记忆
        private bool _isFullscreen;
        private WindowState _preFullscreenWindowState = WindowState.Normal;
        private ProgressLineStyle _currentLineStyle = ProgressLineStyle.Wavy;

        private readonly string _lyricSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lyric_settings.json");
        private LyricSettings _lyricSettings = new();
        private bool _isInitializingSettings;

        public MainWindow()
        {
            InitializeComponent();

            ProgressSlider.AddHandler(PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(MainProgressSlider_PreviewMouseDown), true);
            ProgressSlider.AddHandler(PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(MainProgressSlider_PreviewMouseUp), true);

            // 绑定播放器事件（使用 Dispatcher 确保跨线程自动切歌时安全刷新 UI）
            _player.SongChanged += song => Dispatcher.InvokeAsync(() => OnSongChanged(song));
            _player.PlayStateChanged += playing => Dispatcher.InvokeAsync(() => OnPlayStateChanged(playing));
            _player.ThemeColorsChanged += (primary, bg) => Dispatcher.InvokeAsync(() => OnThemeColorsChanged(primary, bg));

            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _progressTimer.Tick += ProgressTimer_Tick;
            _progressTimer.Start();

            _releaseFullPlayerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _releaseFullPlayerTimer.Tick += ReleaseFullPlayerTimer_Tick;

            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }
        // lauguage change
        private void LangToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            bool toEnglish = Lang.CurrentLanguage == "zh-CN";
            string targetLang = toEnglish ? "en-US" : "zh-CN";

            Lang.SwitchLanguage(targetLang);
            LangBtnText.Text = toEnglish ? "中" : "EN";
            _lyricSettings.Language = targetLang;
            ApplyAndSaveLyricSettings(saveToDisk: true);
        }
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitTrayIcon();
            InitLyricSettingsUi();
            _lyricWindow.Show();

            _libraryFolders = _library.LoadLibraryFolders();
            FolderPathsListBox.ItemsSource = _libraryFolders.ToList();

            if (_libraryFolders.Count > 0)
            {
                await RefreshLibraryAsync();
            }
        }

        private void InitLyricSettingsUi()
        {
            _isInitializingSettings = true;

            var fontNames = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            LyricFontCombo.ItemsSource = fontNames;

            try
            {
                if (File.Exists(_lyricSettingsPath))
                {
                    string json = File.ReadAllText(_lyricSettingsPath);
                    _lyricSettings = JsonSerializer.Deserialize<LyricSettings>(json) ?? new LyricSettings();
                }
            }
            catch { }

            LyricFontCombo.SelectedItem = fontNames.Contains(_lyricSettings.FontFamilyName)
                ? _lyricSettings.FontFamilyName
                : fontNames.FirstOrDefault();
            LyricFontSizeSlider.Value = _lyricSettings.FontSize;
            LyricOpacitySlider.Value = _lyricSettings.BackgroundOpacity;
            LyricColorProgressCheck.IsChecked = _lyricSettings.EnableColorProgress;
            LyricShowCoverCheck.IsChecked = _lyricSettings.ShowMiniCover;
            OffsetXSlider.Value = _lyricSettings.OffsetX;
            OffsetYSlider.Value = _lyricSettings.OffsetY;
            
            string savedLang = _lyricSettings.Language == "en-US" ? "en-US" : "zh-CN";
            Lang.SwitchLanguage(savedLang);
            LangBtnText.Text = savedLang == "en-US" ? "中" : "EN";
            _isInitializingSettings = false;
            ApplyAndSaveLyricSettings(saveToDisk: false);
        }

        private void LyricSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingSettings || !IsLoaded) return;
            ApplyAndSaveLyricSettings(saveToDisk: true);
        }

        private void ApplyAndSaveLyricSettings(bool saveToDisk)
        {
            _lyricSettings.FontFamilyName = LyricFontCombo.SelectedItem as string ?? "Microsoft YaHei UI";
            _lyricSettings.FontSize = Math.Round(LyricFontSizeSlider.Value, 1);
            _lyricSettings.BackgroundOpacity = Math.Round(LyricOpacitySlider.Value, 2);
            _lyricSettings.EnableColorProgress = LyricColorProgressCheck.IsChecked == true;
            _lyricSettings.ShowMiniCover = LyricShowCoverCheck.IsChecked == true;
            _lyricSettings.OffsetX = OffsetXSlider.Value;
            _lyricSettings.OffsetY = OffsetYSlider.Value;

            if (FullPlayerHost.Visibility != Visibility.Visible)
            {
                _progressTimer.Interval = _lyricSettings.EnableColorProgress
                    ? TimeSpan.FromMilliseconds(50)
                    : TimeSpan.FromMilliseconds(250);
            }

            _lyricWindow.ApplySettings(_lyricSettings);

            if (saveToDisk)
            {
                try
                {
                    File.WriteAllText(_lyricSettingsPath, JsonSerializer.Serialize(_lyricSettings));
                }
                catch { }
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _progressTimer.Stop();
            _releaseFullPlayerTimer.Stop();
            RemoveTrayIcon();
            _lyricWindow.Close();
            _player.Dispose();
        }

        // ==================== 全屏与快捷键控制 ====================

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _isFullscreen)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
        }

        private void ToggleFullscreen()
        {
            // 如果主界面已经最大化了，在播放页点全屏按钮视为“退出全屏/还原窗口”
            bool targetFullscreen = WindowState == WindowState.Maximized ? false : true;
            _isFullscreen = targetFullscreen;

            var chrome = WindowChrome.GetWindowChrome(this);

            if (_isFullscreen)
            {
                _preFullscreenWindowState = WindowState;
                if (chrome != null)
                {
                    chrome.CaptionHeight = 0;
                    chrome.ResizeBorderThickness = new Thickness(0);
                }
                WindowState = WindowState.Maximized;
            }
            else
            {
                if (chrome != null)
                {
                    chrome.CaptionHeight = 32;
                    chrome.ResizeBorderThickness = new Thickness(5);
                }
                WindowState = WindowState.Normal;
            }

            _nowPlayingView?.UpdateFullscreenState(_isFullscreen);
        }

        // ==================== 曲库扫描与视图刷新 ====================

        private async Task RefreshLibraryAsync()
        {
            var (songs, albums) = await _library.ScanFoldersAsync(_libraryFolders);
            _allSongs = songs;
            _allAlbums = albums;

            EmptyStatePanel.Visibility = _allSongs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            if (_allSongs.Count == 0 && TabFolders.IsChecked != true)
            {
                AlbumsGridView.Visibility = Visibility.Collapsed;
                SongsListView.Visibility = Visibility.Collapsed;
                ArtistsListView.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
                return;
            }

            EmptyStatePanel.Visibility = Visibility.Collapsed;
            string keyword = SearchBox.Text.Trim();
            int sortBy = SortByCombo?.SelectedIndex ?? 0;
            bool descending = (SortOrderCombo?.SelectedIndex ?? 0) == 1;

            if (TabAlbums.IsChecked == true && !_isInSubDetailView)
            {
                IEnumerable<AlbumItem> query = _allAlbums;
                if (!string.IsNullOrEmpty(keyword))
                {
                    query = query.Where(a =>
                        a.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        a.Artist.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                }

                query = sortBy switch
                {
                    1 => descending ? query.OrderByDescending(a => a.Year) : query.OrderBy(a => a.Year),
                    2 => descending ? query.OrderByDescending(a => a.Artist) : query.OrderBy(a => a.Artist),
                    _ => descending ? query.OrderByDescending(a => a.Title) : query.OrderBy(a => a.Title)
                };

                AlbumsGridView.ItemsSource = query.ToList();
                ShowOnlyView(AlbumsGridView);
            }
            else if (TabArtists.IsChecked == true && !_isInSubDetailView)
            {
                var artists = _allSongs
                    .Select(s => s.Artist)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(a => string.IsNullOrEmpty(keyword) || a.Contains(keyword, StringComparison.OrdinalIgnoreCase));

                artists = descending
                    ? artists.OrderByDescending(a => a, StringComparer.CurrentCultureIgnoreCase)
                    : artists.OrderBy(a => a, StringComparer.CurrentCultureIgnoreCase);

                ArtistsListView.ItemsSource = artists.ToList();
                ShowOnlyView(ArtistsListView);
            }
            else if (TabFolders.IsChecked == true)
            {
                FolderPathsListBox.ItemsSource = _libraryFolders.ToList();
                ShowOnlyView(FoldersView);
            }
            else if (TabQueue.IsChecked == true)
            {
                _currentSongListContext = _player.Queue.ToList();
                SongsListView.ItemsSource = _currentSongListContext;
                ShowOnlyView(SongsListView);
            }
            else
            {
                IEnumerable<SongItem> source = _isInSubDetailView ? _currentSongListContext : _allSongs;
                if (!string.IsNullOrEmpty(keyword))
                {
                    source = source.Where(s =>
                        s.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        s.Artist.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        s.Album.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                }

                if (!_isInSubDetailView)
                {
                    source = sortBy switch
                    {
                        1 => descending ? source.OrderByDescending(s => s.Year) : source.OrderBy(s => s.Year),
                        2 => descending ? source.OrderByDescending(s => s.Artist) : source.OrderBy(s => s.Artist),
                        _ => descending ? source.OrderByDescending(s => s.Title) : source.OrderBy(s => s.Title)
                    };
                    _currentSongListContext = source.ToList();
                }

                SongsListView.ItemsSource = source.ToList();
                ShowOnlyView(SongsListView);
            }
        }

        private void ShowOnlyView(UIElement target)
        {
            AlbumsGridView.Visibility = target == AlbumsGridView ? Visibility.Visible : Visibility.Collapsed;
            SongsListView.Visibility = target == SongsListView ? Visibility.Visible : Visibility.Collapsed;
            ArtistsListView.Visibility = target == ArtistsListView ? Visibility.Visible : Visibility.Collapsed;
            FoldersView.Visibility = target == FoldersView ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==================== 导航、点击与拖拽交互 ====================

        private void NavTab_Click(object sender, RoutedEventArgs e)
        {
            _isInSubDetailView = false;
            BackBtn.Visibility = Visibility.Collapsed;
            ApplyFilterAndSort();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilterAndSort();
        private void SortOption_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilterAndSort();
        }

        private void AlbumsGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AlbumsGridView.SelectedItem is AlbumItem album)
            {
                AlbumsGridView.SelectedItem = null;
                _isInSubDetailView = true;
                BackBtnTitle.Text = Lang.Format("Fmt_BackAlbum", album.Title);
                BackBtn.Visibility = Visibility.Visible;
                _currentSongListContext = album.Songs;
                SongsListView.ItemsSource = _currentSongListContext;
                ShowOnlyView(SongsListView);
            }
        }

        private void ArtistsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArtistsListView.SelectedItem is string artist)
            {
                ArtistsListView.SelectedItem = null;
                _isInSubDetailView = true;
                //BackBtnTitle.Text = $"返回歌手  |  {artist}";
                BackBtnTitle.Text = Lang.Format("Fmt_BackArtist", artist);
                BackBtn.Visibility = Visibility.Visible;
                _currentSongListContext = _allSongs
                    .Where(s => string.Equals(s.Artist, artist, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                SongsListView.ItemsSource = _currentSongListContext;
                ShowOnlyView(SongsListView);
            }
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            _isInSubDetailView = false;
            BackBtn.Visibility = Visibility.Collapsed;
            ApplyFilterAndSort();
        }

        private void SongsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SongsListView.SelectedItem is SongItem selected && SongsListView.ItemsSource is List<SongItem> list)
            {
                int index = list.IndexOf(selected);
                if (index >= 0) _player.PlayQueue(list, index);
            }
        }

        private void PlayAll_Click(object sender, RoutedEventArgs e)
        {
            var list = _isInSubDetailView ? _currentSongListContext : _allSongs;
            if (list.Count == 0) return;
            _player.IsShuffle = false;
            UpdateShuffleIcon();
            _player.PlayQueue(list, 0);
        }

        private void ShuffleAll_Click(object sender, RoutedEventArgs e)
        {
            var list = _isInSubDetailView ? _currentSongListContext : _allSongs;
            if (list.Count == 0) return;
            _player.IsShuffle = true;
            UpdateShuffleIcon();
            _player.PlayQueue(list, Random.Shared.Next(list.Count));
        }

        private async void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = Lang.Get("Dlg_SelectMusicFolder"),
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var folder in dialog.FolderNames)
                {
                    if (!_libraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                    {
                        _libraryFolders.Add(folder);
                    }
                }
                _library.SaveLibraryFolders(_libraryFolders);
                await RefreshLibraryAsync();
            }
        }

        private async void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string folder)
            {
                _libraryFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
                _library.SaveLibraryFolders(_libraryFolders);
                await RefreshLibraryAsync();
            }
        }

        private async void RescanFolders_Click(object sender, RoutedEventArgs e) => await RefreshLibraryAsync();

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] droppedPaths) return;

            bool addedFolder = false;
            var audioFiles = new List<string>();

            foreach (var path in droppedPaths)
            {
                if (Directory.Exists(path))
                {
                    if (!_libraryFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        _libraryFolders.Add(path);
                        addedFolder = true;
                    }
                }
                else if (File.Exists(path))
                {
                    audioFiles.Add(path);
                }
            }

            if (addedFolder)
            {
                _library.SaveLibraryFolders(_libraryFolders);
                await RefreshLibraryAsync();
            }
            else if (audioFiles.Count > 0)
            {
                var matched = _allSongs.Where(s => audioFiles.Contains(s.FilePath, StringComparer.OrdinalIgnoreCase)).ToList();
                if (matched.Count > 0) _player.PlayQueue(matched, 0);
            }
        }

        // ==================== 播放状态与动态主题刷新 ====================

        private void OnSongChanged(SongItem? song)
        {
            _lastLyricIndex = -2;
            if (song == null) return;

            NowPlayingTitle.Text = song.Title;
            NowPlayingArtist.Text = song.Artist;
            NowPlayingAudioInfo.Text = song.AudioInfoText;
            NowPlayingCover.Source = _player.CurrentCover;

            ProgressSlider.Maximum = Math.Max(1, _player.TotalTime.TotalSeconds);
            ProgressSlider.Value = 0;
            TotalTimeText.Text = song.DurationText;
            _lyricWindow.UpdateCover(song.CoverCachePath);
            _lyricWindow.UpdateLyric(_player.CurrentLyrics.Count > 0
    ? $"♪ {song.Title} - {song.Artist}"
    : Lang.Format("Fmt_NoLyricsOverlay", song.Title));
        }

        private void OnPlayStateChanged(bool isPlaying)
        {
            PlayPauseIcon.Kind = isPlaying ? PackIconKind.Pause : PackIconKind.Play;
            PlayPauseBtn.IsPlaying = isPlaying;
            ProgressSlider.IsAnimating = isPlaying;
        }

        private void OnThemeColorsChanged(Color primary, Color bottomBarBg)
        {
            var bgBrush = new SolidColorBrush(bottomBarBg);
            bgBrush.Freeze();
            BottomBarBorder.Background = bgBrush;

            var borderBrush = new SolidColorBrush(Color.FromArgb(45, primary.R, primary.G, primary.B));
            borderBrush.Freeze();
            BottomBarBorder.BorderBrush = borderBrush;

            _lyricWindow.UpdateAccentColor(primary);

            try
            {
                var paletteHelper = new PaletteHelper();
                var theme = paletteHelper.GetTheme();
                theme.SetPrimaryColor(primary);
                theme.SetSecondaryColor(primary);
                paletteHelper.SetTheme(theme);
            }
            catch { }
        }

        private void ProgressTimer_Tick(object? sender, EventArgs e)
        {
            if (!_player.IsPlaying) return;

            var current = _player.CurrentTime;
            if (!_isDraggingSlider)
            {
                ProgressSlider.Value = current.TotalSeconds;
                CurrentTimeText.Text = current.ToString(@"mm\:ss");
            }

            SyncLyricsForPosition(current, snapFullView: false);
        }

        private void SyncLyricsForPosition(TimeSpan current, bool snapFullView)
        {
            int lyricIndex = _player.GetCurrentLyricIndex(current);
            if (lyricIndex != _lastLyricIndex)
            {
                _lastLyricIndex = lyricIndex;
                if (lyricIndex >= 0 && lyricIndex < _player.CurrentLyrics.Count)
                {
                    _lyricWindow.UpdateLyric(_player.CurrentLyrics[lyricIndex].Text);
                }
            }

            if (_lyricSettings.EnableColorProgress && lyricIndex >= 0 && lyricIndex < _player.CurrentLyrics.Count)
            {
                TimeSpan lineStart = _player.CurrentLyrics[lyricIndex].Time;
                TimeSpan lineEnd = (lyricIndex + 1 < _player.CurrentLyrics.Count)
                    ? _player.CurrentLyrics[lyricIndex + 1].Time
                    : _player.TotalTime;

                double lineDurationMs = (lineEnd - lineStart).TotalMilliseconds;
                if (lineDurationMs > 100)
                {
                    double progress = (current - lineStart).TotalMilliseconds / lineDurationMs;
                    _lyricWindow.UpdateColorProgress(progress);
                }
            }

            _nowPlayingView?.UpdatePlaybackTick(current, lyricIndex, snapFullView);
        }

        private void MainProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
            if (e.OriginalSource is not Thumb && ProgressSlider.ActualWidth > 0)
            {
                double ratio = Math.Clamp(e.GetPosition(ProgressSlider).X / ProgressSlider.ActualWidth, 0.0, 1.0);
                ProgressSlider.Value = ProgressSlider.Minimum + ratio * (ProgressSlider.Maximum - ProgressSlider.Minimum);
            }
        }

        private void MainProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingSlider) return;
            _isDraggingSlider = false;
            _justFinishedSliderDrag = true;

            var targetPos = TimeSpan.FromSeconds(ProgressSlider.Value);
            _player.Seek(targetPos);
            _lastLyricIndex = -2;
            SyncLyricsForPosition(targetPos, snapFullView: true);
        }

        private void BottomBarBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_justFinishedSliderDrag)
            {
                _justFinishedSliderDrag = false;
                return;
            }

            DependencyObject? dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep != BottomBarBorder)
            {
                if (dep is ButtonBase || dep is Slider) return;
                dep = VisualTreeHelper.GetParent(dep);
            }

            OpenFullPlayerView();
        }

        private void OpenFullPlayerView()
        {
            _releaseFullPlayerTimer.Stop();

            if (_nowPlayingView == null)
            {
                _nowPlayingView = new NowPlayingView(_player);
                _nowPlayingView.RequestClose += CloseFullPlayerView;
                _nowPlayingView.RequestToggleFullscreen += ToggleFullscreen;
                _nowPlayingView.LineStyleChanged += style => _currentLineStyle = style;
                FullPlayerHost.Content = _nowPlayingView;
            }
            else
            {
                _nowPlayingView.SyncFullState();
            }

            // 同步主页面当前的最大化状态到播放页
            _isFullscreen = (WindowState == WindowState.Maximized);
            if (_isFullscreen)
            {
                var chrome = WindowChrome.GetWindowChrome(this);
                if (chrome != null)
                {
                    chrome.CaptionHeight = 0;
                    chrome.ResizeBorderThickness = new Thickness(0);
                }
            }

            _nowPlayingView.ApplyProgressLineStyle(_currentLineStyle);
            _nowPlayingView.UpdateFullscreenState(_isFullscreen);

            BottomBarBorder.Visibility = Visibility.Collapsed;
            FullPlayerHost.Visibility = Visibility.Visible;
            _progressTimer.Interval = TimeSpan.FromMilliseconds(50);
            SyncLyricsForPosition(_player.CurrentTime, snapFullView: true);
        }

        private void CloseFullPlayerView()
        {
            // 保持当前窗口大屏或小屏状态不变，仅恢复主界面顶部栏的拖拽与调整边框
            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome != null)
            {
                chrome.CaptionHeight = 32;
                chrome.ResizeBorderThickness = new Thickness(5);
            }

            FullPlayerHost.Visibility = Visibility.Collapsed;
            BottomBarBorder.Visibility = Visibility.Visible;

            _progressTimer.Interval = _lyricSettings.EnableColorProgress
                ? TimeSpan.FromMilliseconds(50)
                : TimeSpan.FromMilliseconds(250);

            _releaseFullPlayerTimer.Stop();
            _releaseFullPlayerTimer.Start();
        }

        private void ReleaseFullPlayerTimer_Tick(object? sender, EventArgs e)
        {
            _releaseFullPlayerTimer.Stop();
            if (FullPlayerHost.Visibility == Visibility.Visible) return;

            if (_nowPlayingView != null)
            {
                _nowPlayingView.RequestClose -= CloseFullPlayerView;
                _nowPlayingView.RequestToggleFullscreen -= ToggleFullscreen;
                _nowPlayingView.CleanupResources();
                FullPlayerHost.Content = null;
                _nowPlayingView = null;
            }
        }

        // ==================== 底部控制栏交互 ====================

        private void PlayPause_Click(object sender, RoutedEventArgs e) => _player.TogglePlayPause();
        private void Prev_Click(object sender, RoutedEventArgs e) => _player.PlayPrevious();
        private void Next_Click(object sender, RoutedEventArgs e) => _player.PlayNext();

        private void ShuffleToggle_Click(object sender, RoutedEventArgs e)
        {
            _player.IsShuffle = !_player.IsShuffle;
            UpdateShuffleIcon();
        }

        private void UpdateShuffleIcon()
            => ShuffleIcon.Foreground = _player.IsShuffle ? ActiveDarkBrush : InactiveGrayBrush;

        private void RepeatToggle_Click(object sender, RoutedEventArgs e)
        {
            _player.RepeatMode = _player.RepeatMode switch
            {
                RepeatMode.ListLoop => RepeatMode.SingleLoop,
                RepeatMode.SingleLoop => RepeatMode.Sequence,
                _ => RepeatMode.ListLoop
            };

            RepeatIcon.Kind = _player.RepeatMode == RepeatMode.SingleLoop
                ? PackIconKind.RepeatOnce
                : PackIconKind.Repeat;

            RepeatIcon.Foreground = _player.RepeatMode == RepeatMode.Sequence
                ? InactiveGrayBrush
                : ActiveDarkBrush;
        }

        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider)
            {
                CurrentTimeText.Text = TimeSpan.FromSeconds(ProgressSlider.Value).ToString(@"mm\:ss");
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player != null) _player.Volume = (float)VolumeSlider.Value;
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            _player.IsMuted = !_player.IsMuted;
            VolumeIcon.Kind = _player.IsMuted ? PackIconKind.VolumeOff : PackIconKind.VolumeHigh;
        }

        private void QuickQueue_Click(object sender, RoutedEventArgs e)
        {
            TabQueue.IsChecked = true;
            NavTab_Click(sender, e);
        }

        private void LyricToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_lyricWindow.Visibility == Visibility.Visible)
            {
                _lyricWindow.Hide();
                LyricToggleIcon.Foreground = InactiveGrayBrush;
            }
            else
            {
                _lyricWindow.Show();
                LyricToggleIcon.Foreground = ActiveDarkBrush;
            }
        }

        private void LyricOffsetBtn_Click(object sender, RoutedEventArgs e) => LyricOffsetPopup.IsOpen = !LyricOffsetPopup.IsOpen;

        // ==================== 窗口控制与零依赖 Win32 系统托盘 ====================

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            NowPlayingCover.Source = null;
            _player.ReleaseCoverCacheForBackground();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_player.CurrentSong != null)
            {
                NowPlayingCover.Source = LibraryService.LoadHighResCoverForPlayback(_player.CurrentSong.FilePath, 280);
            }
        }

        #region 轻量原生 Win32 托盘图标实现（无需引入 WinForms 依赖）
        private const int WM_TRAYICON = 0x8000 + 1;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int NIM_ADD = 0x00000000;
        private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        private NOTIFYICONDATA _trayData;
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);
        private void InitTrayIcon()
        {
            var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            hwndSource?.AddHook(WndProc);
            string? exePath = Environment.ProcessPath;
            IntPtr appIcon = !string.IsNullOrEmpty(exePath)
                ? ExtractIcon(IntPtr.Zero, exePath, 0)
                : IntPtr.Zero;

            // 如果提取失败（如返回 0 或 1），回退到系统默认应用图标
            if (appIcon == IntPtr.Zero || appIcon == new IntPtr(1))
            {
                appIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
            }
            _trayData = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = new WindowInteropHelper(this).Handle,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = appIcon,
                szTip = Lang.Get("Tray_Tooltip")
            };
            Shell_NotifyIcon(NIM_ADD, ref _trayData);
        }

        private void RemoveTrayIcon() => Shell_NotifyIcon(NIM_DELETE, ref _trayData);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int mouseMsg = lParam.ToInt32();
                if (mouseMsg == WM_LBUTTONDBLCLK || mouseMsg == WM_RBUTTONUP)
                {
                    RestoreFromTray();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
        #endregion
    }
}
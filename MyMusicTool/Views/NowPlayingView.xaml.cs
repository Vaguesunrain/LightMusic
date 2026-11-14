using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using MyMusicTool.Controls;
using MyMusicTool.Models;
using MyMusicTool.Services;

namespace MyMusicTool.Views
{
    public class LyricDisplayItem : INotifyPropertyChanged
    {
        private static readonly Brush ActiveBrush = Brushes.White;
        private static readonly Brush InactiveBrush = CreateInactiveBrush();

        private static Brush CreateInactiveBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));
            brush.Freeze();
            return brush;
        }

        public string Text { get; set; } = string.Empty;
        public static double ActiveFontSize { get; set; } = 28;
        public double FontSize => IsActive ? ActiveFontSize : Math.Max(15, ActiveFontSize - 9);

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FontSize));
                OnPropertyChanged(nameof(FontWeight));
                OnPropertyChanged(nameof(Foreground));
            }
        }

        public FontWeight FontWeight => IsActive ? FontWeights.Bold : FontWeights.Normal;
        public Brush Foreground => IsActive ? ActiveBrush : InactiveBrush;

        public void NotifyFontSizeChanged() => OnPropertyChanged(nameof(FontSize));

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class NowPlayingView : UserControl
    {
        private static readonly string BgConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom_bg.txt");
        private static readonly Brush MutedIconBrush = CreateMutedIconBrush();

        private static Brush CreateMutedIconBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            brush.Freeze();
            return brush;
        }

        private readonly AudioPlayerService _player;
        private List<LyricDisplayItem> _lyricItems = new();
        private int _activeLyricIndex = -1;
        private bool _isDraggingSlider;

        private double _currentScrollOffset;
        private double _targetScrollOffset;

        public event Action? RequestClose;
        public event Action? RequestToggleFullscreen;
        public event Action<ProgressLineStyle>? LineStyleChanged;

        public NowPlayingView(AudioPlayerService player)
        {
            InitializeComponent();
            _player = player;

            ProgressSlider.AddHandler(PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(ProgressSlider_PreviewMouseDown), true);
            ProgressSlider.AddHandler(PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(ProgressSlider_PreviewMouseUp), true);

            SizeChanged += NowPlayingView_SizeChanged;

            _player.SongChanged += OnSongChanged;
            _player.PlayStateChanged += OnPlayStateChanged;
            _player.Queue.CollectionChanged += Queue_CollectionChanged;

            QueueListBox.ItemsSource = _player.Queue;
            UpdateQueueCountText();

            LoadCustomBackground();
            SyncFullState();
        }

        public void ApplyLyricVisualSettings(double activeFontSize)
        {
            LyricDisplayItem.ActiveFontSize = activeFontSize;
            foreach (var item in _lyricItems)
            {
                item.NotifyFontSizeChanged();
            }
        }

        private void NowPlayingView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            double scale = Math.Clamp(Math.Min(ActualWidth / 1280.0, ActualHeight / 820.0), 0.85, 1.85);
            RootScaleTransform.ScaleX = scale;
            RootScaleTransform.ScaleY = scale;
        }

        public void UpdateFullscreenState(bool isFullscreen)
            => FullscreenIcon.Kind = isFullscreen ? PackIconKind.FullscreenExit : PackIconKind.Fullscreen;

        public void ApplyProgressLineStyle(ProgressLineStyle style)
            => ProgressSlider.LineStyle = style;

        public void SyncFullState()
        {
            OnSongChanged(_player.CurrentSong);
            OnPlayStateChanged(_player.IsPlaying);
            VolumeSlider.Value = _player.Volume;
            VolumeIcon.Kind = _player.IsMuted ? PackIconKind.VolumeOff : PackIconKind.VolumeHigh;
            UpdateShuffleAndRepeatIcons();
            UpdateQueueCountText();
        }

        private void SetActiveLyricIndex(int newIndex, bool snapImmediately = false)
        {
            if (newIndex == _activeLyricIndex && !snapImmediately) return;

            if (_activeLyricIndex >= 0 && _activeLyricIndex < _lyricItems.Count)
            {
                _lyricItems[_activeLyricIndex].IsActive = false;
            }

            _activeLyricIndex = newIndex;

            if (_activeLyricIndex >= 0 && _activeLyricIndex < _lyricItems.Count)
            {
                _lyricItems[_activeLyricIndex].IsActive = true;
                int targetIdx = _activeLyricIndex;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    () => RecalculateTargetLyricCenter(targetIdx, snapImmediately));
            }
        }

        public void UpdatePlaybackTick(TimeSpan current, int lyricIndex, bool snapScroll = false)
        {
            if (Visibility != Visibility.Visible) return;

            if (!_isDraggingSlider)
            {
                ProgressSlider.Value = current.TotalSeconds;
                CurrentTimeText.Text = current.ToString(@"mm\:ss");
            }

            SetActiveLyricIndex(lyricIndex, snapScroll);

            if (Math.Abs(_targetScrollOffset - _currentScrollOffset) > 0.5)
            {
                _currentScrollOffset += (_targetScrollOffset - _currentScrollOffset) * 0.48;
                LyricsScrollViewer.ScrollToVerticalOffset(_currentScrollOffset);
            }
        }

        private void LyricsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double halfHeight = Math.Max(0, LyricsScrollViewer.ViewportHeight / 2.0);
            LyricsItemsControl.Margin = new Thickness(0, halfHeight, 0, halfHeight);

            if (_activeLyricIndex >= 0)
            {
                int idx = _activeLyricIndex;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    () => RecalculateTargetLyricCenter(idx, snapImmediately: true));
            }
        }

        private void RecalculateTargetLyricCenter(int index, bool snapImmediately = false)
        {
            if (index < 0 || index >= _lyricItems.Count) return;

            if (LyricsItemsControl.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            {
                try
                {
                    Point posInList = container.TransformToAncestor(LyricsItemsControl).Transform(new Point(0, 0));
                    double itemCenterY = LyricsItemsControl.Margin.Top + posInList.Y + (container.ActualHeight / 2.0);
                    double centeredOffset = itemCenterY - (LyricsScrollViewer.ViewportHeight / 2.0);

                    _targetScrollOffset = Math.Clamp(centeredOffset, 0, LyricsScrollViewer.ScrollableHeight);

                    if (snapImmediately)
                    {
                        _currentScrollOffset = _targetScrollOffset;
                        LyricsScrollViewer.ScrollToVerticalOffset(_currentScrollOffset);
                    }
                }
                catch { }
            }
        }

        private void OnSongChanged(SongItem? song)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _activeLyricIndex = -1;
                _currentScrollOffset = 0;
                _targetScrollOffset = 0;
                LyricsScrollViewer.ScrollToVerticalOffset(0);

                if (song == null)
                {
                    TitleText.Text = Lang.Get("Status_NotPlaying");
                    ArtistAlbumText.Text = Lang.Get("Status_ArtistAlbumPlaceholder");
                    AudioInfoText.Text = "LOCAL • PURE AUDIO";
                    LargeCoverImage.Source = null;
                    ProgressSlider.Value = 0;
                    CurrentTimeText.Text = "00:00";
                    TotalTimeText.Text = "00:00";
                    LyricsItemsControl.ItemsSource = null;
                    _lyricItems.Clear();
                    return;
                }

                TitleText.Text = song.Title;
                ArtistAlbumText.Text = $"{song.Artist} • {song.Album}";
                AudioInfoText.Text = song.AudioInfoText;
                TotalTimeText.Text = song.DurationText;
                ProgressSlider.Maximum = Math.Max(1, _player.TotalTime.TotalSeconds);
                ProgressSlider.Value = _player.CurrentTime.TotalSeconds;
                LargeCoverImage.Source = LibraryService.LoadHighResCoverForPlayback(song.FilePath, 360);

                if (_player.CurrentLyrics.Count > 0)
                {
                    _lyricItems = _player.CurrentLyrics.Select(l => new LyricDisplayItem { Text = l.Text }).ToList();
                }
                else
                {
                    _lyricItems = new List<LyricDisplayItem> { new() { Text =Lang.Get("Lyric_InstrumentalOrNone"), IsActive = true } };
                    _activeLyricIndex = 0;
                }

                LyricsItemsControl.ItemsSource = _lyricItems;
            });
        }

        private void OnPlayStateChanged(bool isPlaying)
        {
            Dispatcher.InvokeAsync(() =>
            {
                PlayPauseIcon.Kind = isPlaying ? PackIconKind.Pause : PackIconKind.Play;
                ProgressSlider.IsAnimating = isPlaying;
            });
        }

        // ==================== 进度条线型菜单与全屏按钮 ====================

        private void OpenLineStyleMenu_Click(object sender, RoutedEventArgs e)
        {
            if (LineStyleButton.ContextMenu != null)
            {
                LineStyleButton.ContextMenu.PlacementTarget = LineStyleButton;
                LineStyleButton.ContextMenu.IsOpen = true;
            }
        }

        private void SelectLineStyle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string tag &&
                Enum.TryParse<ProgressLineStyle>(tag, out var style))
            {
                ProgressSlider.LineStyle = style;
                LineStyleChanged?.Invoke(style);
            }
        }

        private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
            => RequestToggleFullscreen?.Invoke();

        // ==================== 进度条点击秒跳 + 拖拽双生效控制 ====================

        private void ProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
            if (e.OriginalSource is not Thumb && ProgressSlider.ActualWidth > 0)
            {
                double ratio = Math.Clamp(e.GetPosition(ProgressSlider).X / ProgressSlider.ActualWidth, 0.0, 1.0);
                ProgressSlider.Value = ProgressSlider.Minimum + ratio * (ProgressSlider.Maximum - ProgressSlider.Minimum);
            }
        }

        private void ProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingSlider) return;
            _isDraggingSlider = false;

            var targetPos = TimeSpan.FromSeconds(ProgressSlider.Value);
            _player.Seek(targetPos);

            int idx = _player.GetCurrentLyricIndex(targetPos);
            SetActiveLyricIndex(idx, snapImmediately: true);
        }

        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider)
            {
                CurrentTimeText.Text = TimeSpan.FromSeconds(ProgressSlider.Value).ToString(@"mm\:ss");
            }
        }

        // ==================== 播放队列抽屉与背景图控制 ====================

        private void Queue_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => Dispatcher.InvokeAsync(UpdateQueueCountText);

        private void UpdateQueueCountText()
    => QueueCountText.Text = Lang.Format("Fmt_QueueCount", _player.Queue.Count);

        private void ToggleQueueDrawer_Click(object sender, RoutedEventArgs e)
            => QueueDrawerBorder.Visibility = QueueDrawerBorder.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        private void CloseQueueDrawer_Click(object sender, RoutedEventArgs e)
            => QueueDrawerBorder.Visibility = Visibility.Collapsed;

        private void QueueListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (QueueListBox.SelectedItem is SongItem song)
            {
                int idx = _player.Queue.IndexOf(song);
                if (idx >= 0) _player.PlayAt(idx);
            }
        }

        private void RemoveQueueItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is SongItem song)
            {
                int idx = _player.Queue.IndexOf(song);
                if (idx >= 0) _player.RemoveFromQueue(idx);
            }
        }

        private void ClearQueue_Click(object sender, RoutedEventArgs e) => _player.ClearQueue();

        private void LoadCustomBackground()
        {
            try
            {
                if (!File.Exists(BgConfigPath)) return;
                string imgPath = File.ReadAllText(BgConfigPath).Trim();
                if (!File.Exists(imgPath)) return;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 1920;
                bitmap.UriSource = new Uri(imgPath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                CustomBgImage.Source = bitmap;
            }
            catch { }
        }

        private void SelectCustomBg_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Lang.Get("Dlg_SelectBgImage"),
                Filter = Lang.Get("Dlg_ImageFilter")
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(BgConfigPath, dialog.FileName);
                    CustomBgImage.Source = null;
                    LoadCustomBackground();
                }
                catch { }
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            QueueDrawerBorder.Visibility = Visibility.Collapsed;
            RequestClose?.Invoke();
        }

        private void Prev_Click(object sender, RoutedEventArgs e) => _player.PlayPrevious();
        private void PlayPause_Click(object sender, RoutedEventArgs e) => _player.TogglePlayPause();
        private void Next_Click(object sender, RoutedEventArgs e) => _player.PlayNext();

        private void Shuffle_Click(object sender, RoutedEventArgs e)
        {
            _player.IsShuffle = !_player.IsShuffle;
            UpdateShuffleAndRepeatIcons();
        }

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            _player.RepeatMode = _player.RepeatMode switch
            {
                RepeatMode.ListLoop => RepeatMode.SingleLoop,
                RepeatMode.SingleLoop => RepeatMode.Sequence,
                _ => RepeatMode.ListLoop
            };
            UpdateShuffleAndRepeatIcons();
        }

        private void UpdateShuffleAndRepeatIcons()
        {
            ShuffleIcon.Foreground = _player.IsShuffle ? Brushes.White : MutedIconBrush;
            RepeatIcon.Kind = _player.RepeatMode == RepeatMode.SingleLoop ? PackIconKind.RepeatOnce : PackIconKind.Repeat;
            RepeatIcon.Foreground = _player.RepeatMode == RepeatMode.Sequence ? MutedIconBrush : Brushes.White;
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            _player.IsMuted = !_player.IsMuted;
            VolumeIcon.Kind = _player.IsMuted ? PackIconKind.VolumeOff : PackIconKind.VolumeHigh;
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_player != null) _player.Volume = (float)VolumeSlider.Value;
        }

        public void CleanupResources()
        {
            SizeChanged -= NowPlayingView_SizeChanged;
            _player.SongChanged -= OnSongChanged;
            _player.PlayStateChanged -= OnPlayStateChanged;
            _player.Queue.CollectionChanged -= Queue_CollectionChanged;
            QueueListBox.ItemsSource = null;
            CustomBgImage.Source = null;
            LargeCoverImage.Source = null;
            LyricsItemsControl.ItemsSource = null;
            _lyricItems.Clear();
        }
    }
}
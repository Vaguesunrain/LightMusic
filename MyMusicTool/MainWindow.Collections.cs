using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using MyMusicTool.Models;
using MyMusicTool.Services;
using MyMusicTool.Views;

namespace MyMusicTool
{
    public partial class MainWindow
    {
        private readonly CollectionService _collections = new();
        private CollectionItem? _activeBrowsingCollection;
        private DispatcherTimer? _toastTimer;

        private void InitCollectionsSupport()
        {
            Lang.LanguageChanged += () =>
            {
                foreach (var c in _collections.Collections)
                    c.RefreshTexts();

                if (_activeBrowsingCollection != null && _isInSubDetailView)
                    BackBtnTitle.Text = Lang.Format("Fmt_BackCollection", _activeBrowsingCollection.Name);
            };
        }

        // 曲库扫描或增删歌曲后，同步每个收藏夹的第一首歌封面（无自定义背景时生效，空夹滞空）
        private void SyncCollectionFirstSongCovers()
        {
            if (_collections.Collections.Count == 0) return;

            var songMap = _allSongs
                .GroupBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var col in _collections.Collections)
            {
                string? firstCoverPath = col.SongPaths
                    .Select(p => songMap.TryGetValue(p, out var s) ? s.CoverCachePath : null)
                    .FirstOrDefault(path => !string.IsNullOrEmpty(path) && System.IO.File.Exists(path));

                col.FallbackCoverPath = firstCoverPath;
            }
        }

        private List<SongItem> ResolveCollectionSongs(CollectionItem col)
        {
            var songMap = _allSongs
                .GroupBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            return col.SongPaths
                .Select(p => songMap.TryGetValue(p, out var s) ? s : null)
                .Where(s => s != null)
                .Cast<SongItem>()
                .ToList();
        }

        private void ShowToast(string message)
        {
            ToastText.Text = message;
            ToastBorder.Visibility = Visibility.Visible;

            _toastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            _toastTimer.Stop();
            _toastTimer.Tick -= ToastTimer_Tick;
            _toastTimer.Tick += ToastTimer_Tick;
            _toastTimer.Start();
        }

        private void ToastTimer_Tick(object? sender, EventArgs e)
        {
            _toastTimer?.Stop();
            ToastBorder.Visibility = Visibility.Collapsed;
        }

        // ==================== 收藏夹增删与背景设置 ====================

        private void NewCollection_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewCollectionDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _collections.CreateCollection(dlg.CollectionName);
                ApplyFilterAndSort();
            }
        }

        private void SetCollectionBg_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not CollectionItem col) return;

            var dialog = new OpenFileDialog
            {
                Title = Lang.Get("Dlg_SelectCollectionBg"),
                Filter = Lang.Get("Dlg_ImageFilter")
            };

            if (dialog.ShowDialog() == true)
            {
                _collections.SetCollectionBackground(col, dialog.FileName);
            }
        }

        private void ResetCollectionBg_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not CollectionItem col) return;

            _collections.SetCollectionBackground(col, null);
            SyncCollectionFirstSongCovers();
        }

        private void DeleteCollection_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button btn || btn.Tag is not CollectionItem col) return;

            if (ReferenceEquals(_collections.ActivePlayingCollection, col))
                _collections.SetActivePlayingCollection(null);

            _collections.DeleteCollection(col);
            ApplyFilterAndSort();
        }

        private void CollectionsGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        
            if (CollectionsGridView.SelectedItem is not CollectionItem col) return;
            CollectionsGridView.SelectedItem = null;

            _activeBrowsingCollection = col;
            _isInSubDetailView = true;
            UpdateActionButtonsVisibility();
            NewCollectionBtn.Visibility = Visibility.Collapsed;
            BackBtnTitle.Text = Lang.Format("Fmt_BackCollection", col.Name);
            BackBtn.Visibility = Visibility.Visible;
            ColAddSongsBtn.Visibility = Visibility.Visible;
ColRemoveSongsBtn.Visibility = Visibility.Visible;
SongsListView.SelectionMode = SelectionMode.Extended;
            _currentSongListContext = ResolveCollectionSongs(col);
            SongsListView.ItemsSource = _currentSongListContext;
            ShowOnlyView(SongsListView);
        }

        // ==================== 歌曲列表右键菜单（添加 / 移除收藏夹） ====================

        private void SongsListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (SongsListView.SelectedItem is not SongItem song || SongsListView.ContextMenu == null)
            {
                e.Handled = true;
                return;
            }

            var menu = SongsListView.ContextMenu;
            menu.Items.Clear();

            // 1. “添加到收藏夹” 二级菜单
            var addSubMenu = new MenuItem { Header = Lang.Get("Menu_AddToCollection") };
            foreach (var col in _collections.Collections)
            {
                var colItem = new MenuItem { Header = col.Name };
                colItem.Click += (_, _) =>
                {
                    _collections.AddSongToCollection(col, song.FilePath);
                    SyncCollectionFirstSongCovers();
                    ShowToast(Lang.Format("Toast_AddedToCollection", col.Name));
                };
                addSubMenu.Items.Add(colItem);
            }

            if (_collections.Collections.Count > 0)
                addSubMenu.Items.Add(new Separator());

            var newAndAddItem = new MenuItem { Header = Lang.Get("Menu_NewCollectionAndAdd") };
            newAndAddItem.Click += (_, _) =>
            {
                var dlg = new NewCollectionDialog { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    var created = _collections.CreateCollection(dlg.CollectionName);
                    _collections.AddSongToCollection(created, song.FilePath);
                    SyncCollectionFirstSongCovers();
                    ShowToast(Lang.Format("Toast_AddedToCollection", created.Name));
                }
            };
            addSubMenu.Items.Add(newAndAddItem);
            menu.Items.Add(addSubMenu);

            // 2. 如果当前正处于某个收藏夹详情页内，显示“从此收藏夹移除”
            if (_isInSubDetailView && _activeBrowsingCollection != null)
            {
                var targetCol = _activeBrowsingCollection;
                var removeItem = new MenuItem { Header = Lang.Get("Menu_RemoveFromCollection") };
                removeItem.Click += (_, _) =>
                {
                    _collections.RemoveSongFromCollection(targetCol, song.FilePath);
                    SyncCollectionFirstSongCovers();
                    _currentSongListContext = ResolveCollectionSongs(targetCol);
                    SongsListView.ItemsSource = _currentSongListContext;
                };
                menu.Items.Add(removeItem);
            }
           
        }
        private void ColAddSongs_Click(object sender, RoutedEventArgs e)
        {
            if (_activeBrowsingCollection == null) return;

            var dlg = new SelectSongsDialog(
                _activeBrowsingCollection.Name,
                _allSongs,
                _activeBrowsingCollection.SongPaths)
            {
                Owner = this
            };

            if (dlg.ShowDialog() == true && dlg.SelectedSongs.Count > 0)
            {
                foreach (var song in dlg.SelectedSongs)
                {
                    _collections.AddSongToCollection(_activeBrowsingCollection, song.FilePath);
                }

                SyncCollectionFirstSongCovers();
                _currentSongListContext = ResolveCollectionSongs(_activeBrowsingCollection);
                SongsListView.ItemsSource = _currentSongListContext;
                ShowToast(Lang.Format("Toast_AddedMultiToCollection", dlg.SelectedSongs.Count, _activeBrowsingCollection.Name));
            }
        }

        private void ColRemoveSongs_Click(object sender, RoutedEventArgs e)
        {
            if (_activeBrowsingCollection == null) return;

            var selected = SongsListView.SelectedItems.Cast<SongItem>().ToList();
            if (selected.Count == 0) return;

            foreach (var song in selected)
            {
                _collections.RemoveSongFromCollection(_activeBrowsingCollection, song.FilePath);
            }

            SyncCollectionFirstSongCovers();
            _currentSongListContext = ResolveCollectionSongs(_activeBrowsingCollection);
            SongsListView.ItemsSource = _currentSongListContext;
            ShowToast(Lang.Format("Toast_RemovedFromCollection", selected.Count));
        }
        // 汇总所有收藏夹的可用歌曲，并生成 FilePath -> CollectionItem 临时查找表
        private (List<SongItem> Songs, Dictionary<string, CollectionItem> Map) BuildAllCollectionsPlaybackTable()
        {
            var songMap = _allSongs
                .GroupBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var aggregatedSongs = new List<SongItem>();
            var songToColMap = new Dictionary<string, CollectionItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var col in _collections.Collections)
            {
                foreach (var path in col.SongPaths)
                {
                    if (!songMap.TryGetValue(path, out var songItem)) continue;

                    // 若同一首歌存在于多个收藏夹，优先锁定第一个匹配到的收藏夹
                    if (songToColMap.TryAdd(songItem.FilePath, col))
                    {
                        aggregatedSongs.Add(songItem);
                    }
                }
            }

            return (aggregatedSongs, songToColMap);
        }

        private void PlayInCollections_Click(object sender, RoutedEventArgs e)
        {
            var (songs, map) = BuildAllCollectionsPlaybackTable();
            if (songs.Count == 0)
            {
                ShowToast(Lang.Get("Toast_NoSongsInCollections"));
                return;
            }

            _player.IsShuffle = false;
            UpdateShuffleIcon();
            _collections.StartPlayInCollectionsMode(map, songs[0].FilePath);
            _player.PlayQueue(songs, 0);
        }

        private void ShuffleInCollections_Click(object sender, RoutedEventArgs e)
        {
            var (songs, map) = BuildAllCollectionsPlaybackTable();
            if (songs.Count == 0)
            {
                ShowToast(Lang.Get("Toast_NoSongsInCollections"));
                return;
            }

            int startIndex = Random.Shared.Next(songs.Count);
            _player.IsShuffle = true;
            UpdateShuffleIcon();
            _collections.StartPlayInCollectionsMode(map, songs[startIndex].FilePath);
            _player.PlayQueue(songs, startIndex);
        }
        private void UpdateActionButtonsVisibility()
        {
            // 只有停留在【收藏夹】一级卡片墙时，才显示全局跨收藏夹播放按钮
            bool isCollectionsRoot = TabCollections.IsChecked == true && !_isInSubDetailView;
            // 点进具体某个收藏夹详情页时，显示加减歌曲按钮，并恢复普通 Play All / Shuffle All
            bool isCollectionDetail = _isInSubDetailView && _activeBrowsingCollection != null;

            NewCollectionBtn.Visibility = isCollectionsRoot ? Visibility.Visible : Visibility.Collapsed;
            PlayInCollectionsBtn.Visibility = isCollectionsRoot ? Visibility.Visible : Visibility.Collapsed;
            ShuffleInCollectionsBtn.Visibility = isCollectionsRoot ? Visibility.Visible : Visibility.Collapsed;

            PlayAllBtn.Visibility = isCollectionsRoot ? Visibility.Collapsed : Visibility.Visible;
            ShuffleAllBtn.Visibility = isCollectionsRoot ? Visibility.Collapsed : Visibility.Visible;

            ColAddSongsBtn.Visibility = isCollectionDetail ? Visibility.Visible : Visibility.Collapsed;
            ColRemoveSongsBtn.Visibility = isCollectionDetail ? Visibility.Visible : Visibility.Collapsed;
            SongsListView.SelectionMode = isCollectionDetail ? SelectionMode.Extended : SelectionMode.Single;
        }
    }
}
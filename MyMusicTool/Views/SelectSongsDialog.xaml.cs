using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MyMusicTool.Models;
using MyMusicTool.Services;

namespace MyMusicTool.Views
{
    public partial class SelectSongsDialog : Window
    {
        private readonly List<SongItem> _candidates;
        public List<SongItem> SelectedSongs { get; private set; } = new();

        public SelectSongsDialog(string collectionName, IEnumerable<SongItem> allSongs, IEnumerable<string> existingPaths)
        {
            InitializeComponent();
            DialogTitleText.Text = Lang.Format("Dlg_SelectSongsTitle", collectionName);

            var existingSet = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
            _candidates = allSongs
                .Where(s => !existingSet.Contains(s.FilePath))
                .ToList();

            CandidateListBox.ItemsSource = _candidates;
            UpdateSelectedCount();
        }

        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string kw = SearchInput.Text.Trim();
            CandidateListBox.ItemsSource = string.IsNullOrEmpty(kw)
                ? _candidates
                : _candidates.Where(s =>
                    s.Title.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    s.Artist.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                    s.Album.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void CandidateListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateSelectedCount();

        private void UpdateSelectedCount()
            => SelectedCountText.Text = Lang.Format("Fmt_SelectedCount", CandidateListBox.SelectedItems.Count);

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (CandidateListBox.SelectedItems.Count == CandidateListBox.Items.Count)
                CandidateListBox.UnselectAll();
            else
                CandidateListBox.SelectAll();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            SelectedSongs = CandidateListBox.SelectedItems.Cast<SongItem>().ToList();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
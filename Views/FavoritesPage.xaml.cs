using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;
using OfflineWinDict.Services;

namespace OfflineWinDict.Views
{
    public sealed partial class FavoritesPage : Page
    {
        private sealed class FolderRow
        {
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public bool IsAll { get; set; }

            public override string ToString() => $"{Name}  ({Count})";
        }

        private const string AllFolderId = "\0All";
        private string _selectedFolder = AllFolderId;

        public FavoritesPage()
        {
            try { InitializeComponent(); }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesPage.InitializeComponent");
                throw;
            }
            Loaded += OnPageLoaded;
            UserDataService.Instance.FavoritesChanged += OnFavoritesChanged;
            Unloaded += (_, _) => UserDataService.Instance.FavoritesChanged -= OnFavoritesChanged;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            _ = LoadAsync();
        }

        private void OnFavoritesChanged()
        {
            DispatcherQueue.TryEnqueue(async () => await LoadAsync());
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            try
            {
                await UserDataService.Instance.EnsureLoadedAsync();

                // Folders list
                var rows = new List<FolderRow>
                {
                    new() { Name = "All words", Count = UserDataService.Instance.Favorites.Count, IsAll = true },
                };
                rows.AddRange(UserDataService.Instance.Folders.Select(f => new FolderRow
                {
                    Name = f,
                    Count = UserDataService.Instance.FolderCount(f),
                }));
                FolderList.ItemsSource = rows;

                var selected = rows.FirstOrDefault(r => r.IsAll && _selectedFolder == AllFolderId)
                               ?? rows.FirstOrDefault(r => r.Name == _selectedFolder)
                               ?? rows[0];
                _selectedFolder = selected.IsAll ? AllFolderId : selected.Name;
                FolderList.SelectedItem = selected;

                RefreshWordList();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesPage.Load");
            }
        }

        private void RefreshWordList()
        {
            var favorites = _selectedFolder == AllFolderId
                ? UserDataService.Instance.Favorites.ToList()
                : UserDataService.Instance.Favorites.Where(f => f.Folder == _selectedFolder).ToList();

            favorites.Sort((a, b) => string.CompareOrdinal(b.AddedAtUtc.ToString("o"), a.AddedAtUtc.ToString("o")));

            FavoritesList.ItemsSource = favorites;
            FavoritesCountLabel.Text = UserDataService.Instance.Favorites.Count == 0
                ? ""
                : $"{UserDataService.Instance.Favorites.Count} word{(UserDataService.Instance.Favorites.Count == 1 ? "" : "s")}";

            var isEmpty = favorites.Count == 0;
            FavoritesList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
            FavoritesEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            if (isEmpty)
            {
                var anyFavoritesAtAll = UserDataService.Instance.Favorites.Count > 0;
                FavoritesEmptyTitle.Text = anyFavoritesAtAll ? "This folder is empty" : "No favorites yet";
                FavoritesEmptyText.Text = anyFavoritesAtAll
                    ? "Move words into this folder, or favorite new words from the search page."
                    : "Tap the star next to a word to save it here for quick access.";
            }

            FolderHintLabel.Text = _selectedFolder == AllFolderId
                ? "Right-click a folder to rename or delete it."
                : $"Folder \u201C{_selectedFolder}\u201D · right-click to rename or delete.";
        }

        private void FolderList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FolderRow row)
            {
                _selectedFolder = row.IsAll ? AllFolderId : row.Name;
                RefreshWordList();
            }
        }

        private void FolderList_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            try
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is not FolderRow row || row.IsAll) return;
                if (string.Equals(row.Name, UserDataService.GeneralFolder, StringComparison.Ordinal)) return;

                var flyout = new MenuFlyout();
                var rename = new MenuFlyoutItem { Text = "Rename folder", Icon = new FontIcon { Glyph = "\uE70F" } };
                rename.Click += async (_, _) => await PromptRenameFolderAsync(row.Name);
                flyout.Items.Add(rename);

                var delete = new MenuFlyoutItem { Text = "Delete folder", Icon = new FontIcon { Glyph = "\uE74D" } };
                delete.Click += async (_, _) => await ConfirmDeleteFolderAsync(row.Name);
                flyout.Items.Add(delete);

                flyout.ShowAt((FrameworkElement)e.OriginalSource, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesPage.FolderRightTap");
            }
        }

        private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = await PromptTextAsync("New folder", "Folder name", "", "Create");
                if (string.IsNullOrWhiteSpace(name)) return;
                var created = await UserDataService.Instance.CreateFolderAsync(name);
                if (!created)
                {
                    await ShowMessageAsync("A folder with that name already exists.", "New folder");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesPage.NewFolder");
            }
        }

        private async System.Threading.Tasks.Task PromptRenameFolderAsync(string oldName)
        {
            try
            {
                var newName = await PromptTextAsync("Rename folder", "Folder name", oldName, "Rename");
                if (string.IsNullOrWhiteSpace(newName)) return;
                await UserDataService.Instance.RenameFolderAsync(oldName, newName);
                if (_selectedFolder == oldName) _selectedFolder = newName;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesPage.RenameFolder");
            }
        }

        private async System.Threading.Tasks.Task ConfirmDeleteFolderAsync(string name)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Delete folder?",
                    Content = $"The folder \u201C{name}\u201D will be deleted. Its words move to General.",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await UserDataService.Instance.DeleteFolderAsync(name);
                    if (_selectedFolder == name) _selectedFolder = AllFolderId;
                    await LoadAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesPage.DeleteFolder");
            }
        }

        private void FavoritesList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FavoriteItem item)
            {
                MainWindow.OpenWordInHome(item.Word, item.LanguageCode);
            }
        }

        private void MoveItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button { Tag: FavoriteItem item }) return;

                var flyout = new MenuFlyout();
                foreach (var folder in UserDataService.Instance.Folders)
                {
                    if (folder == item.Folder) continue;
                    var target = folder;
                    var moveItem = new MenuFlyoutItem
                    {
                        Text = $"Move to {folder}",
                        Icon = new FontIcon { Glyph = "\uE8DE" },
                        Tag = item,
                    };
                    moveItem.Click += async (_, _) => await UserDataService.Instance.MoveFavoriteAsync(item, target);
                    flyout.Items.Add(moveItem);
                }

                if (flyout.Items.Count == 0)
                {
                    flyout.Items.Add(new MenuFlyoutItem { Text = "No other folders yet", IsEnabled = false });
                }

                flyout.ShowAt((FrameworkElement)sender, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesPage.MoveItem");
            }
        }

        private async void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button { Tag: FavoriteItem item })
                {
                    await UserDataService.Instance.RemoveFavoriteAsync(item);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "FavoritesPage.RemoveItem");
            }
        }

        private void GoSearch_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.Navigate("Home");
        }

        // ───────────────── Dialog helpers ─────────────────

        private async System.Threading.Tasks.Task<string?> PromptTextAsync(string title, string header, string initial, string primaryText)
        {
            var input = new TextBox
            {
                Header = header,
                Text = initial,
                PlaceholderText = header,
                Width = 300,
            };
            var dialog = new ContentDialog
            {
                Title = title,
                Content = input,
                PrimaryButtonText = primaryText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                return input.Text?.Trim();
            }
            return null;
        }

        private async System.Threading.Tasks.Task ShowMessageAsync(string message, string title)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
    }
}

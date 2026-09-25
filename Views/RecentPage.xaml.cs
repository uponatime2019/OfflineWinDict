using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;
using OfflineWinDict.Services;

namespace OfflineWinDict.Views
{
    public sealed partial class RecentPage : Page
    {
        public RecentPage()
        {
            try { InitializeComponent(); }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "RecentPage.InitializeComponent");
                throw;
            }
            Loaded += OnPageLoaded;
            UserDataService.Instance.HistoryChanged += OnHistoryChanged;
            Unloaded += (_, _) => UserDataService.Instance.HistoryChanged -= OnHistoryChanged;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e) => _ = LoadAsync();

        private void OnHistoryChanged()
        {
            DispatcherQueue.TryEnqueue(async () => await LoadAsync());
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            try
            {
                await UserDataService.Instance.EnsureLoadedAsync();
                var now = DateTime.UtcNow;
                var rows = UserDataService.Instance.History
                    .Select(h =>
                    {
                        h.DisplayTime = RelativeTime(h.SearchedAtUtc, now);
                        return h;
                    })
                    .ToList();

                HistoryList.ItemsSource = rows;
                var isEmpty = rows.Count == 0;
                HistoryList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
                HistoryEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
                ClearAllButton.IsEnabled = !isEmpty;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "RecentPage.Load");
            }
        }

        internal static string RelativeTime(DateTime utc, DateTime nowUtc)
        {
            var delta = nowUtc - utc;
            if (delta.TotalMinutes < 1) return "just now";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} min ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} hour{((int)delta.TotalHours == 1 ? "" : "s")} ago";
            if (delta.TotalDays < 7) return $"{(int)delta.TotalDays} day{((int)delta.TotalDays == 1 ? "" : "s")} ago";
            return utc.ToLocalTime().ToString("MMM d, yyyy");
        }

        private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is HistoryItem item)
            {
                MainWindow.OpenWordInHome(item.Word, item.LanguageCode);
            }
        }

        private async void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button { Tag: HistoryItem item })
                {
                    await UserDataService.Instance.RemoveHistoryAsync(item);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "RecentPage.RemoveItem");
            }
        }

        private async void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Clear recent searches?",
                    Content = "Your search history will be removed from this device. Favorites are not affected.",
                    PrimaryButtonText = "Clear",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await UserDataService.Instance.ClearHistoryAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "RecentPage.ClearAll");
            }
        }

        private void GoSearch_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.Instance?.Navigate("Home");
        }
    }
}

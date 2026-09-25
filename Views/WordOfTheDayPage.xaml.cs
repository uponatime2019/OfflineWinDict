using System;
using System.Globalization;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OfflineWinDict.Controls;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;
using OfflineWinDict.Services;

namespace OfflineWinDict.Views
{
    public sealed partial class WordOfTheDayPage : Page
    {
        private WordEntry? _entry;
        private string _word = "";
        private int _loadGeneration;

        public WordOfTheDayPage()
        {
            try { InitializeComponent(); }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "WordOfTheDayPage.InitializeComponent");
                throw;
            }
            Loaded += OnPageLoaded;
            ActualThemeChanged += (_, _) => { _ = LoadAsync(); };
            UserDataService.Instance.FavoritesChanged += OnFavoritesChanged;
            Unloaded += (_, _) => UserDataService.Instance.FavoritesChanged -= OnFavoritesChanged;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            _ = LoadAsync();
        }

        private void OnFavoritesChanged()
        {
            DispatcherQueue.TryEnqueue(UpdateFavoriteButton);
        }

        private async System.Threading.Tasks.Task LoadAsync()
        {
            try
            {
                var generation = ++_loadGeneration;
                _word = WordOfTheDayService.TodayWord();

                DateLabel.Text = DateTime.Today.ToString("D", CultureInfo.CurrentUICulture);
                WotdWord.Text = _word;
                WotdPhonetic.Text = "";
                WotdPos.Text = "";
                WotdDefinition.Text = "";
                WotdExampleBorder.Visibility = Visibility.Collapsed;
                WotdContent.Visibility = Visibility.Collapsed;
                WotdOffline.Visibility = Visibility.Collapsed;
                WotdLoading.Visibility = Visibility.Visible;
                UpdateFavoriteButton();

                _ = LoadArchiveAsync();

                // Serve from cache when possible (offline-friendly), otherwise fetch live.
                var entry = await WordOfTheDayService.GetCachedEntryAsync(DateTime.Today);
                if (entry == null)
                {
                    var outcome = await DictionaryService.LookupAsync(_word, "en");
                    if (generation != _loadGeneration) return;

                    if (outcome.Entry != null)
                    {
                        entry = outcome.Entry;
                        await WordOfTheDayService.StoreEntryAsync(entry);
                    }
                    else
                    {
                        WotdLoading.Visibility = Visibility.Collapsed;
                        WotdOffline.Visibility = Visibility.Visible;
                        return;
                    }
                }

                if (generation != _loadGeneration) return;
                _entry = entry;
                Render(entry);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "WotdPage.Load");
            }
        }

        private void Render(WordEntry entry)
        {
            WotdLoading.Visibility = Visibility.Collapsed;
            WotdOffline.Visibility = Visibility.Collapsed;
            WotdContent.Visibility = Visibility.Visible;

            WotdWord.Text = entry.Word;
            WotdPhonetic.Text = string.IsNullOrWhiteSpace(entry.PhoneticText) ? "" : entry.PhoneticText;
            WotdPhonetic.Visibility = string.IsNullOrWhiteSpace(entry.PhoneticText) ? Visibility.Collapsed : Visibility.Visible;

            var firstGroup = entry.Meanings.FirstOrDefault();
            WotdPos.Text = firstGroup?.PartOfSpeech ?? "";
            WotdPos.Visibility = string.IsNullOrWhiteSpace(WotdPos.Text) ? Visibility.Collapsed : Visibility.Visible;

            var firstSense = firstGroup?.Senses.FirstOrDefault();
            WotdDefinition.Text = firstSense?.Definition ?? "";

            if (firstSense != null && !string.IsNullOrWhiteSpace(firstSense.Example))
            {
                WotdExample.Text = $"\u201C{firstSense.Example}\u201D";
                WotdExampleBorder.Visibility = Visibility.Visible;
            }

            UpdateFavoriteButton();
        }

        private async System.Threading.Tasks.Task LoadArchiveAsync()
        {
            try
            {
                var archive = await WordOfTheDayService.GetArchiveAsync();
                DispatcherQueue.TryEnqueue(() =>
                {
                    ArchivePanel.Children.Clear();
                    ArchiveEmptyHint.Visibility = archive.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    foreach (var record in archive.Take(7))
                    {
                        if (!DateTime.TryParseExact(record.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;

                        var card = new Button
                        {
                            MinWidth = 190,
                            MaxWidth = 240,
                            Padding = new Thickness(16, 12, 16, 12),
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Background = ThemeHelper.GetBrush("CardBrush", this),
                            BorderBrush = ThemeHelper.GetBrush("CardBorderBrush", this),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(12),
                        };
                        var panel = new StackPanel { Spacing = 3 };
                        panel.Children.Add(new TextBlock
                        {
                            Text = record.Word,
                            FontFamily = new FontFamily("Georgia"),
                            FontSize = 17,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                            Foreground = ThemeHelper.GetBrush("BrandNavyBrush", this),
                        });
                        panel.Children.Add(new TextBlock
                        {
                            Text = date.ToString("MMM d", CultureInfo.CurrentUICulture),
                            FontSize = 12,
                            Foreground = ThemeHelper.GetBrush("TextTertiaryBrush", this),
                        });
                        card.Content = panel;
                        var word = record.Word;
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card, $"Word of the day {record.Date}: {word}");
                        card.Click += (_, _) => MainWindow.OpenWordInHome(word, "en");
                        ArchivePanel.Children.Add(card);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "WotdPage.Archive");
            }
        }

        private void UpdateFavoriteButton()
        {
            try
            {
                var isFavorite = _entry != null && UserDataService.Instance.IsFavorite(_entry.Word, _entry.LanguageCode);
                WotdFavoriteIcon.Glyph = isFavorite ? "\uE735" : "\uE734";
                WotdFavoriteIcon.Foreground = ThemeHelper.GetBrush(isFavorite ? "BrandGreenBrush" : "TextSecondaryBrush", this);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WotdFavoriteButton,
                    isFavorite ? "Remove from favorites" : "Add to favorites");
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "WotdPage.UpdateFavorite");
            }
        }

        private void AudioButton_Click(object sender, RoutedEventArgs e)
        {
            if (_entry == null)
            {
                PronunciationService.Speak(_word, "en", AppSession.Settings.TtsVoiceId);
                return;
            }
            PronunciationService.PlayEntry(_entry, AppSession.Settings.PreferTtsPronunciation, AppSession.Settings.TtsVoiceId);
        }

        private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var word = _entry?.Word ?? _word;
                await UserDataService.Instance.ToggleFavoriteAsync(word, _entry?.LanguageCode ?? "en");
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "WotdPage.Favorite");
            }
        }

        private void OpenFull_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.OpenWordInHome(_entry?.Word ?? _word, _entry?.LanguageCode ?? "en");
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadAsync();
        }
    }
}

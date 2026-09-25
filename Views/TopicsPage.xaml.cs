using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;
using OfflineWinDict.Services;

namespace OfflineWinDict.Views
{
    public sealed partial class TopicsPage : Page
    {
        private TopicDef? _activeTopic;
        private int _loadGeneration;

        public TopicsPage()
        {
            try { InitializeComponent(); }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "TopicsPage.InitializeComponent");
                throw;
            }
            Loaded += OnPageLoaded;
            ActualThemeChanged += (_, _) =>
            {
                if (_activeTopic != null) _ = LoadTopicAsync(_activeTopic);
            };
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TopicList.Items.Count == 0)
                {
                    TopicList.ItemsSource = new List<TopicDef>(TopicsService.All);
                }

                if (_activeTopic == null && TopicList.SelectedItem == null)
                {
                    TopicList.SelectedItem = TopicsService.All[0];
                    _ = LoadTopicAsync(TopicsService.All[0]);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "TopicsPage.Loaded");
            }
        }

        private void TopicList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is TopicDef topic)
            {
                _ = LoadTopicAsync(topic);
            }
        }

        private void RetryTopic_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTopic != null)
            {
                _ = LoadTopicAsync(_activeTopic);
            }
        }

        private async System.Threading.Tasks.Task LoadTopicAsync(TopicDef topic)
        {
            try
            {
                var generation = ++_loadGeneration;
                _activeTopic = topic;
                TopicHeader.Text = topic.Title;
                ShowState("loading");

                var result = await ThesaurusService.TopicWordsAsync(topic);
                if (generation != _loadGeneration) return;

                if (result.Offline)
                {
                    ShowState("error");
                    return;
                }

                TopicWordsHost.Children.Clear();
                if (result.Value.Count == 0)
                {
                    ShowState("empty");
                    return;
                }

                foreach (var w in result.Value)
                {
                    TopicWordsHost.Children.Add(CreateTopicWordRow(w));
                }
                ShowState("words");

                AppLogger.LogAction("TopicsPage_Load", new Dictionary<string, object> { { "topic", topic.Id }, { "count", result.Value.Count } });
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "TopicsPage.LoadTopic");
                ShowState("error");
            }
        }

        private Button CreateTopicWordRow(TopicWord word)
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var main = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            main.Children.Add(new TextBlock
            {
                Text = word.Word,
                FontSize = 14.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ThemeHelper.GetBrush("TextPrimaryBrush", this),
            });
            main.Children.Add(new TextBlock
            {
                Text = word.Definition,
                FontSize = 12.5,
                Foreground = ThemeHelper.GetBrush("TextSecondaryBrush", this),
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(main, 0);
            content.Children.Add(main);

            var posChip = new Border
            {
                Background = ThemeHelper.GetBrush("ChipBrush", this),
                BorderBrush = ThemeHelper.GetBrush("ChipBorderBrush", this),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 3, 10, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 2, 0),
                Child = new TextBlock
                {
                    Text = word.PartOfSpeech,
                    FontSize = 11.5,
                    Foreground = ThemeHelper.GetBrush("TextSecondaryBrush", this),
                },
            };
            Grid.SetColumn(posChip, 1);
            content.Children.Add(posChip);

            var button = new Button
            {
                Content = content,
                MinHeight = 56,
                Padding = new Thickness(14, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = ThemeHelper.GetBrush("SubCardBrush", this),
                BorderBrush = ThemeHelper.GetBrush("CardBorderBrush", this),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{word.Word}: {word.Definition}");
            button.Click += (_, _) => MainWindow.OpenWordInHome(word.Word, "en");
            return button;
        }

        private void ShowState(string state)
        {
            TopicPlaceholder.Visibility = state == "placeholder" ? Visibility.Visible : Visibility.Collapsed;
            TopicLoading.Visibility = state == "loading" ? Visibility.Visible : Visibility.Collapsed;
            TopicError.Visibility = state == "error" ? Visibility.Visible : Visibility.Collapsed;
            TopicEmpty.Visibility = state == "empty" ? Visibility.Visible : Visibility.Collapsed;
            TopicWordsHost.Visibility = state == "words" ? Visibility.Visible : Visibility.Collapsed;
            TopicSpinner.IsActive = state == "loading";
            TopicSpinner.Visibility = state == "loading" ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}

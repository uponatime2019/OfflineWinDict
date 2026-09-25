using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using OfflineWinDict.Controls;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;
using OfflineWinDict.Services;
using Windows.System;

namespace OfflineWinDict.Views
{
    public sealed partial class HomePage : Page
    {
        public static HomePage? Instance { get; private set; }

        private sealed class NavEntry
        {
            public string Word = "";
            public string Lang = "en";
        }

        private readonly List<NavEntry> _navStack = new();
        private int _navIndex = -1;
        private bool _stackNavigating;
        private string _lastFailedWord = "";
        private string _lastFailedLang = "en";

        private WordEntry? _entry;
        private readonly DispatcherTimer _suggestTimer;
        private int _lookupGeneration;
        private int _suggestGeneration;
        private bool _comboSyncing;
        private bool _browsingFirstWords;

        public HomePage()
        {
            try { InitializeComponent(); }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.InitializeComponent");
                throw;
            }

            Instance = this;
            _suggestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            _suggestTimer.Tick += (_, _) => { _suggestTimer.Stop(); _ = RunSuggestAsync(); };

            SearchBox.TextMemberPath = nameof(SuggestionItem.Word);
            SearchBox.DisplayMemberPath = nameof(SuggestionItem.Word);

            RootGrid.ActualThemeChanged += OnActualThemeChanged;

            Loaded += OnPageLoaded;
            Unloaded += (_, _) =>
            {
                RootGrid.ActualThemeChanged -= OnActualThemeChanged;
                PronunciationService.PlaybackFailed -= OnPlaybackFailed;
                AppSession.DictionaryChanged -= OnAppDictionaryChanged;
                UserDataService.Instance.HistoryChanged -= OnHistoryChanged;
                UserDataService.Instance.FavoritesChanged -= OnFavoritesChanged;
            };

            PronunciationService.PlaybackFailed += OnPlaybackFailed;
            AppSession.DictionaryChanged += OnAppDictionaryChanged;
            UserDataService.Instance.HistoryChanged += OnHistoryChanged;
            UserDataService.Instance.FavoritesChanged += OnFavoritesChanged;
        }

        private void OnActualThemeChanged(FrameworkElement? sender, object? args)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (_entry != null)
                    {
                        RenderEntry(_entry);
                    }
                    HighlightSelectedWord(_entry?.Word);
                    UpdateFavoriteButton();
                    _ = RefreshRecentChipsAsync();
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "HomePage.OnActualThemeChanged");
                }
            });
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Re-subscribe in case page was cached and previously unloaded
                AppSession.DictionaryChanged -= OnAppDictionaryChanged;
                AppSession.DictionaryChanged += OnAppDictionaryChanged;
                PronunciationService.PlaybackFailed -= OnPlaybackFailed;
                PronunciationService.PlaybackFailed += OnPlaybackFailed;
                UserDataService.Instance.HistoryChanged -= OnHistoryChanged;
                UserDataService.Instance.HistoryChanged += OnHistoryChanged;
                UserDataService.Instance.FavoritesChanged -= OnFavoritesChanged;
                UserDataService.Instance.FavoritesChanged += OnFavoritesChanged;

                InitDictionaryCombo();
                _ = RefreshRecentChipsAsync();
                _ = LoadFirstWordsAsync(AppSession.DictionaryCode, selectFirst: _entry == null && string.IsNullOrWhiteSpace(SearchBox.Text));
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.Loaded");
            }
        }

        // ───────────────────────── Dictionary picker ─────────────────────────

        private void InitDictionaryCombo()
        {
            if (HomeDictionaryCombo.Items.Count > 0)
            {
                _comboSyncing = true;
                HomeDictionaryCombo.SelectedIndex = DictionaryCatalog.IndexOf(AppSession.DictionaryCode);
                _comboSyncing = false;
                return;
            }
            HomeDictionaryCombo.ItemsSource = new List<DictionaryDef>(DictionaryCatalog.All);
            HomeDictionaryCombo.DisplayMemberPath = nameof(DictionaryDef.Name);
            _comboSyncing = true;
            HomeDictionaryCombo.SelectedIndex = DictionaryCatalog.IndexOf(AppSession.DictionaryCode);
            _comboSyncing = false;
        }

        public void ChangeDictionary(string code)
        {
            try
            {
                _comboSyncing = true;
                var index = DictionaryCatalog.IndexOf(code);
                if (HomeDictionaryCombo.SelectedIndex != index)
                {
                    HomeDictionaryCombo.SelectedIndex = index;
                }
                _comboSyncing = false;

                SearchBox.Text = "";
                _entry = null;
                _ = LoadFirstWordsAsync(code, selectFirst: true);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.ChangeDictionary");
            }
        }

        private void HomeDictionaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_comboSyncing) return;
            if (HomeDictionaryCombo.SelectedItem is DictionaryDef def)
            {
                AppSession.DictionaryCode = def.Code;
                SearchBox.Text = "";
                _entry = null;
                _ = LoadFirstWordsAsync(def.Code, selectFirst: true);
            }
        }

        private void OnAppDictionaryChanged(string code)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _comboSyncing = true;
                    var index = DictionaryCatalog.IndexOf(code);
                    if (HomeDictionaryCombo.SelectedIndex != index)
                    {
                        HomeDictionaryCombo.SelectedIndex = index;
                    }
                    _comboSyncing = false;

                    SearchBox.Text = "";
                    _entry = null;
                    _ = LoadFirstWordsAsync(code, selectFirst: true);
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "HomePage.OnDictionaryChanged");
                }
            });
        }

        // ───────────────────────── Search & suggestions ─────────────────────────

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
                _suggestTimer.Stop();
                if (string.IsNullOrWhiteSpace(sender.Text))
                {
                    SearchBox.ItemsSource = null;
                    _ = LoadFirstWordsAsync(AppSession.DictionaryCode, selectFirst: false);
                    return;
                }
                if (sender.Text.Trim().Length < 2)
                {
                    SearchBox.ItemsSource = null;
                    return;
                }
                _browsingFirstWords = false;
                _suggestTimer.Start();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.TextChanged");
            }
        }

        private async Task RunSuggestAsync()
        {
            try
            {
                var text = (SearchBox.Text ?? "").Trim();
                if (text.Length < 2) return;
                var generation = ++_suggestGeneration;

                var suggestions = new List<SuggestionItem>();
                await UserDataService.Instance.EnsureLoadedAsync();
                foreach (var h in UserDataService.Instance.History)
                {
                    if (h.Word.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    {
                        suggestions.Add(new SuggestionItem { Word = h.Word, Kind = "history" });
                    }
                    if (suggestions.Count >= 3) break;
                }

                var prefixMatches = await ThesaurusService.PrefixMatchesAsync(text, AppSession.DictionaryCode);
                foreach (var w in prefixMatches)
                {
                    if (suggestions.All(s => !string.Equals(s.Word, w, StringComparison.OrdinalIgnoreCase)))
                    {
                        suggestions.Add(new SuggestionItem { Word = w, Kind = "prefix" });
                    }
                }

                if (generation != _suggestGeneration) return;

                SearchBox.ItemsSource = suggestions;
                if (suggestions.Count > 0)
                {
                    RenderSuggestionList(suggestions);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.RunSuggest");
            }
        }

        private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            try
            {
                var word = args.ChosenSuggestion switch
                {
                    SuggestionItem s => s.Word,
                    _ => (args.QueryText ?? "").Trim(),
                };
                if (word.Length == 0) return;
                _browsingFirstWords = false;
                _ = LookupAsync(word, AppSession.DictionaryCode);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.QuerySubmitted");
            }
        }

        // ───────────────────────── Lookup pipeline ─────────────────────────

        /// <summary>Public entry point used by other pages to open a word here.</summary>
        public void OpenEntry(string word, string languageCode)
        {
            try
            {
                SearchBox.Text = word;
                _ = LookupAsync(word, languageCode);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.OpenEntry");
            }
        }

        private async Task LookupAsync(string word, string lang, bool pushToStack = true)
        {
            try
            {
                var clean = (word ?? "").Trim();
                if (clean.Length == 0) return;
                var generation = ++_lookupGeneration;

                _lastFailedWord = clean;
                _lastFailedLang = lang;

                if (!_browsingFirstWords)
                {
                    ShowResultsState("loading");
                    ResultsHeader.Text = "Searching";
                }
                ShowDetailState("loading");
                UpdateNavButtons();

                var outcome = await DictionaryService.LookupAsync(clean, lang);

                if (generation != _lookupGeneration) return;

                if (outcome.Entry != null)
                {
                    _entry = outcome.Entry;

                    if (pushToStack && !_stackNavigating)
                    {
                        if (_navIndex < _navStack.Count - 1)
                        {
                            _navStack.RemoveRange(_navIndex + 1, _navStack.Count - _navIndex - 1);
                        }
                        var last = _navStack.LastOrDefault();
                        if (last == null || !last.Word.Equals(outcome.Entry.Word, StringComparison.OrdinalIgnoreCase) || last.Lang != lang)
                        {
                            _navStack.Add(new NavEntry { Word = outcome.Entry.Word, Lang = lang });
                            if (_navStack.Count > 60)
                            {
                                _navStack.RemoveAt(0);
                            }
                        }
                        _navIndex = _navStack.Count - 1;
                    }

                    RenderEntry(outcome.Entry);
                    ShowResultsState("list");
                    ShowDetailState("entry");
                    if (_browsingFirstWords && string.IsNullOrWhiteSpace(SearchBox.Text))
                    {
                        var dictDef = DictionaryCatalog.ByCode(lang);
                        ResultsHeader.Text = $"{dictDef.Name} words";
                        HighlightSelectedWord(outcome.Entry.Word);
                    }
                    else
                    {
                        _browsingFirstWords = false;
                        ResultsHeader.Text = "Results";
                        RenderBestMatchRow(outcome.Entry);
                    }
                    UpdateNavButtons();
                    UpdateFavoriteButton();
                    _ = LoadRelatedWordsAsync(outcome.Entry.Word, lang, generation);

                    _ = UserDataService.Instance.AddHistoryAsync(outcome.Entry.Word, lang);
                    AppLogger.LogAction("HomePage_Lookup", new Dictionary<string, object> { { "word", outcome.Entry.Word }, { "lang", lang } });

                    if (AppSession.Settings.AutoPlayPronunciation)
                    {
                        PronunciationService.PlayEntry(outcome.Entry, AppSession.Settings.PreferTtsPronunciation, AppSession.Settings.TtsVoiceId);
                    }
                }
                else if (outcome.Offline)
                {
                    _entry = null;
                    ResultsHeader.Text = "Offline";
                    ShowResultsState("error");
                    ShowDetailState("error");
                    UpdateNavButtons();
                    UpdateFavoriteButton();
                }
                else
                {
                    _entry = null;
                    ResultsHeader.Text = "No results";
                    ShowResultsState("notfound");
                    ShowDetailState("notfound");
                    ResultsNotFoundText.Text = outcome.Message;
                    DetailNotFoundTitle.Text = $"No definitions for \u201C{clean}\u201D";
                    DetailNotFoundText.Text = "The word may be misspelled, or this dictionary may not include it yet.";
                    PopulateChipPanel(ResultsSuggestionPanel, outcome.Suggestions);
                    PopulateChipPanel(DetailSuggestionPanel, outcome.Suggestions);
                    UpdateNavButtons();
                    UpdateFavoriteButton();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.Lookup");
                ResultsHeader.Text = "Error";
                ShowResultsState("error");
                ShowDetailState("error");
            }
        }

        private async Task LoadRelatedWordsAsync(string word, string lang, int generation)
        {
            try
            {
                RelatedListHost.Children.Clear();
                RelatedHeader.Visibility = Visibility.Collapsed;
                if (lang != "en") return;

                var related = await ThesaurusService.MeansLikeAsync(word);
                if (generation != _lookupGeneration) return;
                if (related.Count == 0) return;

                related = related
                    .Where(w => !w.Equals(word, StringComparison.OrdinalIgnoreCase))
                    .Take(14)
                    .ToList();
                if (related.Count == 0) return;

                RelatedHeader.Visibility = Visibility.Visible;
                foreach (var w in related)
                {
                    RelatedListHost.Children.Add(CreateWordRow(w, "related word"));
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.RelatedWords");
            }
        }

        // ───────────────────────── Rendering ─────────────────────────

        private void RenderEntry(WordEntry entry)
        {
            try
            {
                WordText.Text = entry.Word;
                PhoneticText.Text = string.IsNullOrWhiteSpace(entry.PhoneticText) ? "" : entry.PhoneticText;
                PhoneticText.Visibility = string.IsNullOrWhiteSpace(entry.PhoneticText) ? Visibility.Collapsed : Visibility.Visible;

                var dictDef = DictionaryCatalog.ByCode(entry.LanguageCode);
                DetailDictLabel.Text = $"{dictDef.Name}  ·  {entry.SourceName}";

                EntryChipsPanel.Children.Clear();
                EntryChipsPanel.Children.Add(CreateInfoChip(dictDef.Name, brand: true));
                EntryChipsPanel.Children.Add(CreateInfoChip(entry.SourceName, brand: false));

                BuildMeanings(entry);
                BuildChipSection(SynonymsSection, SynonymsPanel, entry.Synonyms);
                BuildChipSection(AntonymsSection, AntonymsPanel, entry.Antonyms);

                OriginSection.Visibility = string.IsNullOrWhiteSpace(entry.Origin) ? Visibility.Collapsed : Visibility.Visible;
                OriginText.Text = entry.Origin;

                SourceText.Text = $"Source: {entry.SourceName}" + (string.IsNullOrWhiteSpace(entry.LicenseName) ? "" : $"  ·  {entry.LicenseName}");
                if (entry.SourceUrls.Count > 0)
                {
                    SourceLinkButton.Visibility = Visibility.Visible;
                    SourceLinkButton.Tag = entry.SourceUrls[0];
                }
                else
                {
                    SourceLinkButton.Visibility = Visibility.Collapsed;
                }
                SourcesSection.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.RenderEntry");
            }
        }

        private void BuildMeanings(WordEntry entry)
        {
            MeaningsPanel.Children.Clear();
            foreach (var group in entry.Meanings)
            {
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var posText = new TextBlock
                {
                    Text = group.PartOfSpeech,
                    FontSize = 15,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = ThemeHelper.GetBrush("AccentBlueBrush", this),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                };
                var line = new Rectangle
                {
                    Height = 1,
                    Fill = ThemeHelper.GetBrush("DividerBrush", this),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(posText, 0);
                Grid.SetColumn(line, 1);
                header.Children.Add(posText);
                header.Children.Add(line);
                MeaningsPanel.Children.Add(header);

                var senseNumber = 1;
                foreach (var sense in group.Senses)
                {
                    var block = new StackPanel { Spacing = 5, Margin = new Thickness(2, 0, 0, 0) };

                    var defText = new TextBlock
                    {
                        Text = $"{senseNumber++}.  {sense.Definition}",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 15,
                        LineHeight = 22,
                        Foreground = ThemeHelper.GetBrush("TextPrimaryBrush", this),
                    };
                    block.Children.Add(defText);

                    if (!string.IsNullOrWhiteSpace(sense.Example))
                    {
                        var exampleBorder = new Border
                        {
                            Background = ThemeHelper.GetBrush("SubCardBrush", this),
                            BorderBrush = ThemeHelper.GetBrush("ExampleBarBrush", this),
                            BorderThickness = new Thickness(2.5, 0, 0, 0),
                            CornerRadius = new CornerRadius(4),
                            Padding = new Thickness(12, 7, 12, 7),
                            Margin = new Thickness(14, 2, 0, 4),
                        };
                        exampleBorder.Child = new TextBlock
                        {
                            Text = $"\u201C{sense.Example}\u201D",
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 13.5,
                            FontStyle = Windows.UI.Text.FontStyle.Italic,
                            Foreground = ThemeHelper.GetBrush("TextSecondaryBrush", this),
                        };
                        block.Children.Add(exampleBorder);
                    }

                    MeaningsPanel.Children.Add(block);
                }
            }
        }

        private void BuildChipSection(StackPanel section, WrapPanel panel, List<string> words)
        {
            panel.Children.Clear();
            var shown = words.Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToList();
            section.Visibility = shown.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var w in shown)
            {
                var chip = new Button
                {
                    Content = w,
                    Style = (Style)Application.Current.Resources["ChipButtonStyle"],
                };
                if (ReferenceEquals(panel, AntonymsPanel))
                {
                    chip.Foreground = ThemeHelper.GetBrush("BrandRedBrush", this);
                }
                chip.Click += (_, _) => _ = LookupAsync(w, AppSession.DictionaryCode);
                panel.Children.Add(chip);
            }
        }

        private void PopulateChipPanel(WrapPanel panel, List<string> words)
        {
            panel.Children.Clear();
            panel.Visibility = words.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var w in words.Take(10))
            {
                var chip = new Button
                {
                    Content = w,
                    Style = (Style)Application.Current.Resources["ChipButtonStyle"],
                    Foreground = ThemeHelper.GetBrush("AccentBlueBrush", this),
                };
                chip.Click += (_, _) => _ = LookupAsync(w, AppSession.DictionaryCode);
                panel.Children.Add(chip);
            }
        }

        private Border CreateInfoChip(string text, bool brand)
        {
            return new Border
            {
                Background = ThemeHelper.GetBrush(brand ? "AccentBlueSoftBrush" : "ChipBrush", this),
                BorderBrush = ThemeHelper.GetBrush(brand ? "AccentBlueFaintBrush" : "ChipBorderBrush", this),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(11, 4, 11, 4),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = ThemeHelper.GetBrush(brand ? "AccentBlueBrush" : "TextSecondaryBrush", this),
                },
            };
        }

        private Button CreateWordRow(string word, string subtitle, bool bestMatch = false)
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var wordText = new TextBlock
            {
                Text = word,
                FontSize = 14.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ThemeHelper.GetBrush(bestMatch ? "AccentBlueBrush" : "TextPrimaryBrush", this),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(wordText, 0);
            content.Children.Add(wordText);

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var subText = new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11.5,
                    Foreground = ThemeHelper.GetBrush("TextTertiaryBrush", this),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(8, 0, 2, 0),
                };
                Grid.SetColumn(subText, 1);
                content.Children.Add(subText);
            }

            var button = new Button
            {
                Content = content,
                Tag = word,
                MinHeight = 42,
                Padding = new Thickness(14, 8, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = ThemeHelper.GetBrush(bestMatch ? "AccentBlueSoftBrush" : "SubCardBrush", this),
                BorderBrush = ThemeHelper.GetBrush(bestMatch ? "AccentBlueFaintBrush" : "CardBorderBrush", this),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, word);
            button.Click += (_, _) => _ = LookupAsync(word, AppSession.DictionaryCode);
            return button;
        }

        private async Task LoadFirstWordsAsync(string languageCode, bool selectFirst = true)
        {
            try
            {
                var dictDef = DictionaryCatalog.ByCode(languageCode);
                ResultsHeader.Text = $"{dictDef.Name} words";
                ResultsSpinner.IsActive = true;
                ResultsSpinner.Visibility = Visibility.Visible;

                var words = await OfflineDatabaseService.GetFirstWordsAsync(languageCode, 100);

                ResultsSpinner.IsActive = false;
                ResultsSpinner.Visibility = Visibility.Collapsed;

                if (words.Count == 0)
                {
                    return;
                }

                _browsingFirstWords = true;
                ResultsListHost.Children.Clear();
                RelatedListHost.Children.Clear();
                RelatedHeader.Visibility = Visibility.Collapsed;
                ShowResultsState("list");

                for (var i = 0; i < words.Count; i++)
                {
                    var word = words[i];
                    var isSelected = (selectFirst && i == 0) || (_entry != null && string.Equals(_entry.Word, word, StringComparison.OrdinalIgnoreCase));
                    var row = CreateWordRow(word, subtitle: "", bestMatch: isSelected);
                    ResultsListHost.Children.Add(row);
                }

                if (selectFirst && words.Count > 0)
                {
                    _ = LookupAsync(words[0], languageCode, pushToStack: true);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.LoadFirstWords");
            }
        }

        private void HighlightSelectedWord(string? word)
        {
            try
            {
                var accentSoft = ThemeHelper.GetBrush("AccentBlueSoftBrush", this);
                var subCard = ThemeHelper.GetBrush("SubCardBrush", this);
                var accentFaint = ThemeHelper.GetBrush("AccentBlueFaintBrush", this);
                var cardBorder = ThemeHelper.GetBrush("CardBorderBrush", this);
                var accentBlue = ThemeHelper.GetBrush("AccentBlueBrush", this);
                var textPrimary = ThemeHelper.GetBrush("TextPrimaryBrush", this);
                var textTertiary = ThemeHelper.GetBrush("TextTertiaryBrush", this);

                void StyleList(Panel host, string? matchWord)
                {
                    foreach (var child in host.Children)
                    {
                        if (child is Button btn)
                        {
                            var isMatch = !string.IsNullOrEmpty(matchWord) && string.Equals(btn.Tag as string, matchWord, StringComparison.OrdinalIgnoreCase);
                            btn.Background = isMatch ? accentSoft : subCard;
                            btn.BorderBrush = isMatch ? accentFaint : cardBorder;
                            if (btn.Content is Grid g)
                            {
                                if (g.Children.Count > 0 && g.Children[0] is TextBlock tb)
                                {
                                    tb.Foreground = isMatch ? accentBlue : textPrimary;
                                }
                                if (g.Children.Count > 1 && g.Children[1] is TextBlock subTb)
                                {
                                    subTb.Foreground = textTertiary;
                                }
                            }
                        }
                    }
                }

                StyleList(ResultsListHost, word);
                StyleList(RelatedListHost, word);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.HighlightSelectedWord");
            }
        }

        private void RenderBestMatchRow(WordEntry entry)
        {
            ResultsListHost.Children.Clear();
            var posSummary = string.Join(", ", entry.Meanings.Select(m => m.PartOfSpeech).Distinct().Take(3));
            var subtitle = string.IsNullOrWhiteSpace(entry.PhoneticText)
                ? (posSummary.Length > 0 ? posSummary : entry.SourceName)
                : entry.PhoneticText;
            ResultsListHost.Children.Add(CreateWordRow(entry.Word, subtitle, bestMatch: true));
        }

        private void RenderSuggestionList(List<SuggestionItem> suggestions)
        {
            try
            {
                ResultsListHost.Children.Clear();
                RelatedListHost.Children.Clear();
                RelatedHeader.Visibility = Visibility.Collapsed;
                ResultsHeader.Text = "Suggestions";
                ShowResultsState("list");
                foreach (var s in suggestions.Take(12))
                {
                    var kindLabel = s.Kind == "history" ? "recent" : "";
                    ResultsListHost.Children.Add(CreateWordRow(s.Word, kindLabel));
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.RenderSuggestions");
            }
        }

        // ───────────────────────── State helpers ─────────────────────────

        private void ShowResultsState(string state)
        {
            ResultsLoadingState.Visibility = state == "loading" ? Visibility.Visible : Visibility.Collapsed;
            ResultsErrorState.Visibility = state == "error" ? Visibility.Visible : Visibility.Collapsed;
            ResultsNotFoundState.Visibility = state == "notfound" ? Visibility.Visible : Visibility.Collapsed;
            ResultsListState.Visibility = state == "loading" || state == "error" || state == "notfound" ? Visibility.Collapsed : Visibility.Visible;
            ResultsSpinner.IsActive = state == "loading";
            ResultsSpinner.Visibility = state == "loading" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowDetailState(string state)
        {
            DetailPlaceholderState.Visibility = state == "placeholder" ? Visibility.Visible : Visibility.Collapsed;
            DetailLoadingState.Visibility = state == "loading" ? Visibility.Visible : Visibility.Collapsed;
            DetailErrorState.Visibility = state == "error" ? Visibility.Visible : Visibility.Collapsed;
            DetailNotFoundState.Visibility = state == "notfound" ? Visibility.Visible : Visibility.Collapsed;
            EntryPanel.Visibility = state == "entry" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateNavButtons()
        {
            BackButton.IsEnabled = _navIndex > 0;
            ForwardButton.IsEnabled = _navIndex >= 0 && _navIndex < _navStack.Count - 1;
        }

        private void UpdateFavoriteButton()
        {
            try
            {
                var isFavorite = _entry != null && UserDataService.Instance.IsFavorite(_entry.Word, _entry.LanguageCode);
                FavoriteIcon.Glyph = isFavorite ? "\uE735" : "\uE734";
                FavoriteIcon.Foreground = ThemeHelper.GetBrush(isFavorite ? "BrandGreenBrush" : "TextSecondaryBrush", this);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FavoriteButton,
                    isFavorite ? "Remove from favorites" : "Add to favorites");
                ToolTipService.SetToolTip(FavoriteButton, isFavorite ? "Remove from favorites" : "Add to favorites");
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.UpdateFavorite");
            }
        }

        // ───────────────────────── Toolbar actions ─────────────────────────

        private void BackButton_Click(object sender, RoutedEventArgs e) => NavigateBack();
        private void ForwardButton_Click(object sender, RoutedEventArgs e) => NavigateForward();

        private async void NavigateBack()
        {
            if (_navIndex <= 0) return;
            _navIndex--;
            _stackNavigating = true;
            try
            {
                await LookupAsync(_navStack[_navIndex].Word, _navStack[_navIndex].Lang, pushToStack: false);
            }
            finally
            {
                _stackNavigating = false;
            }
        }

        private async void NavigateForward()
        {
            if (_navIndex >= _navStack.Count - 1) return;
            _navIndex++;
            _stackNavigating = true;
            try
            {
                await LookupAsync(_navStack[_navIndex].Word, _navStack[_navIndex].Lang, pushToStack: false);
            }
            finally
            {
                _stackNavigating = false;
            }
        }

        private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_entry == null) return;
                var isFavorite = await UserDataService.Instance.ToggleFavoriteAsync(_entry.Word, _entry.LanguageCode);
                AppLogger.LogAction("HomePage_Favorite", new Dictionary<string, object> { { "word", _entry.Word }, { "added", isFavorite } });
                UpdateFavoriteButton();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.Favorite");
            }
        }

        private void AudioButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_entry == null) return;
                PronunciationService.PlayEntry(_entry, AppSession.Settings.PreferTtsPronunciation, AppSession.Settings.TtsVoiceId);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.Audio");
            }
        }

        private void TtsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_entry == null) return;
                PronunciationService.Speak(_entry.Word, _entry.LanguageCode, AppSession.Settings.TtsVoiceId);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.Tts");
            }
        }

        private void ShareButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_entry == null) return;
                var flyout = new MenuFlyout();

                var copyItem = new MenuFlyoutItem { Text = "Copy definition", Icon = new FontIcon { Glyph = "\uE8C8" } };
                copyItem.Click += (_, _) =>
                {
                    var ok = ShareService.CopyToClipboard(ShareService.FormatEntry(_entry));
                    AppLogger.LogAction("HomePage_ShareCopy", new Dictionary<string, object> { { "ok", ok } });
                };
                flyout.Items.Add(copyItem);

                var windowsShareItem = new MenuFlyoutItem { Text = "Windows share", Icon = new FontIcon { Glyph = "\uE72D" } };
                windowsShareItem.Click += (_, _) =>
                {
                    var ok = ShareService.TryShowShare(App.CurrentWindow!, _entry);
                    if (!ok)
                    {
                        ShareService.CopyToClipboard(ShareService.FormatEntry(_entry));
                    }
                    AppLogger.LogAction("HomePage_ShareWindows", new Dictionary<string, object> { { "ok", ok } });
                };
                flyout.Items.Add(windowsShareItem);

                flyout.ShowAt(ShareButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom });
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.Share");
            }
        }

        private async void SourceLinkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    await Launcher.LaunchUriAsync(uri);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.SourceLink");
            }
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            var word = _lastFailedWord;
            var lang = _lastFailedLang;
            if (string.IsNullOrWhiteSpace(word)) word = SearchBox.Text?.Trim() ?? "";
            if (word.Length == 0) return;
            _ = LookupAsync(word, lang);
        }

        // ───────────────────────── Keyboard accelerators ─────────────────────────

        private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            SearchBox.Focus(FocusState.Keyboard);
            args.Handled = true;
        }

        private void Back_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            NavigateBack();
            args.Handled = true;
        }

        private void Forward_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            NavigateForward();
            args.Handled = true;
        }

        // ───────────────────────── Service events ─────────────────────────

        private void OnPlaybackFailed(string message)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Pronunciation unavailable",
                        Content = message,
                        CloseButtonText = "OK",
                        XamlRoot = Content.XamlRoot,
                    };
                    await dialog.ShowAsync();
                }
                catch
                {
                    // A failing notification must never crash the lookup flow.
                }
            });
        }

        private async Task RefreshRecentChipsAsync()
        {
            try
            {
                await UserDataService.Instance.EnsureLoadedAsync();
                var recents = UserDataService.Instance.History.Take(6).ToList();
                RecentChipsPanel.Children.Clear();
                RecentChipsContainer.Visibility = recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                RecentChipsPanel.Visibility = recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var h in recents)
                {
                    var chip = new Button
                    {
                        Content = h.Word,
                        Style = (Style)Application.Current.Resources["ChipButtonStyle"],
                        Foreground = ThemeHelper.GetBrush("AccentBlueBrush", this),
                    };
                    var lang = h.LanguageCode;
                    chip.Click += (_, _) => _ = LookupAsync(h.Word, lang);
                    RecentChipsPanel.Children.Add(chip);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "HomePage.RefreshRecentChips");
            }
        }

        private void OnHistoryChanged()
        {
            DispatcherQueue.TryEnqueue(async () => await RefreshRecentChipsAsync());
        }

        private void OnFavoritesChanged()
        {
            DispatcherQueue.TryEnqueue(UpdateFavoriteButton);
        }
    }
}

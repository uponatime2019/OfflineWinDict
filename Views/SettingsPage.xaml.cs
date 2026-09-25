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
    public sealed partial class SettingsPage : Page
    {
        private bool _syncing;

        public SettingsPage()
        {
            try { InitializeComponent(); }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SettingsPage.InitializeComponent");
                throw;
            }
            Loaded += OnPageLoaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (DefaultDictionaryCombo.Items.Count == 0)
                {
                    DefaultDictionaryCombo.ItemsSource = new List<DictionaryDef>(DictionaryCatalog.All);
                    DefaultDictionaryCombo.DisplayMemberPath = nameof(DictionaryDef.Name);
                    DefaultDictionaryCombo.SelectedIndex = DictionaryCatalog.IndexOf(AppSession.Settings.DictionaryCode);
                }

                _syncing = true;
                var theme = AppSession.Settings.Theme;
                ThemeSelector.SelectedIndex = theme switch
                {
                    "Dark" => 1,
                    "System" => 2,
                    _ => 0,
                };
                AutoPlayToggle.IsOn = AppSession.Settings.AutoPlayPronunciation;
                PreferTtsToggle.IsOn = AppSession.Settings.PreferTtsPronunciation;
                _syncing = false;

                LoadVoices();
                LoadOfflineStatus();
                LoadVersion();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SettingsPage.Loaded");
            }
        }

        private void LoadOfflineStatus()
        {
            try
            {
                var installed = OfflineDatabaseService.GetInstalledLanguages();
                if (installed.Count > 0)
                {
                    var names = installed.Select(c => DictionaryCatalog.ByCode(c).Name).ToList();
                    OfflineStatusLabel.Text = $"Status: {installed.Count} offline dictionary language(s) active";
                    OfflinePathLabel.Text = $"Installed languages: {string.Join(", ", names)} (over 1,100,000 entries available offline)";
                }
                else
                {
                    OfflineStatusLabel.Text = "Status: No offline databases detected (using online fallback)";
                    OfflinePathLabel.Text = "Place Dictionary.db into Assets/Data/ to enable full offline lookup.";
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SettingsPage.LoadOfflineStatus");
            }
        }

        private void LoadVoices()
        {
            try
            {
                var voices = PronunciationService.InstalledVoices;
                if (voices.Count == 0)
                {
                    VoiceCombo.IsEnabled = false;
                    TestVoiceButton.IsEnabled = false;
                    VoiceHint.Text = "No Windows voices installed.";
                    return;
                }

                var display = voices
                    .Select(v => $"{v.DisplayName} ({v.Language})")
                    .ToList();
                display.Insert(0, "Default voice for each language");
                VoiceCombo.ItemsSource = display;

                var selected = 0;
                if (!string.IsNullOrWhiteSpace(AppSession.Settings.TtsVoiceId))
                {
                    var index = voices.ToList().FindIndex(v => v.Id == AppSession.Settings.TtsVoiceId);
                    if (index >= 0) selected = index + 1;
                }
                VoiceCombo.SelectedIndex = selected;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SettingsPage.LoadVoices");
            }
        }

        private void LoadVersion()
        {
            try
            {
                var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);
                VersionLabel.Text = $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            catch
            {
                VersionLabel.Text = "Version 1.0.0.0 (unpackaged)";
            }
        }

        private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            var theme = ThemeSelector.SelectedIndex switch
            {
                1 => "Dark",
                2 => "System",
                _ => "Light",
            };
            AppSession.Settings.Theme = theme;
            _ = AppSession.SaveSettingsAsync();
            MainWindow.Instance?.ApplyTheme(theme);
            AppLogger.LogAction("Settings_Theme", new Dictionary<string, object> { { "theme", theme } });
        }

        private void DefaultDictionaryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            if (DefaultDictionaryCombo.SelectedItem is DictionaryDef def)
            {
                AppSession.DictionaryCode = def.Code;
            }
        }

        private void AutoPlayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            AppSession.Settings.AutoPlayPronunciation = AutoPlayToggle.IsOn;
            _ = AppSession.SaveSettingsAsync();
        }

        private void PreferTtsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            AppSession.Settings.PreferTtsPronunciation = PreferTtsToggle.IsOn;
            _ = AppSession.SaveSettingsAsync();
        }

        private void VoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (VoiceCombo.SelectedIndex <= 0)
                {
                    if (!_syncing)
                    {
                        AppSession.Settings.TtsVoiceId = "";
                        _ = AppSession.SaveSettingsAsync();
                    }
                    return;
                }

                var voices = PronunciationService.InstalledVoices;
                var voice = voices.Count > VoiceCombo.SelectedIndex - 1 ? voices[VoiceCombo.SelectedIndex - 1] : null;
                if (voice != null && !_syncing)
                {
                    AppSession.Settings.TtsVoiceId = voice.Id;
                    _ = AppSession.SaveSettingsAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SettingsPage.VoiceChanged");
            }
        }

        private void TestVoice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var voiceId = AppSession.Settings.TtsVoiceId;
                PronunciationService.Speak("Hello, this is your dictionary pronunciation voice.", "en",
                    string.IsNullOrWhiteSpace(voiceId) ? null : voiceId);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SettingsPage.TestVoice");
            }
        }

        private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Clear recent searches?",
                    Content = "Your search history will be removed from this device.",
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
                AppLogger.LogException(ex, "SettingsPage.ClearHistory");
            }
        }

        private async void ClearFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Clear all favorites?",
                    Content = "Every favorited word and folder will be removed. This cannot be undone.",
                    PrimaryButtonText = "Clear favorites",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await UserDataService.Instance.ClearFavoritesAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "SettingsPage.ClearFavorites");
            }
        }
    }
}

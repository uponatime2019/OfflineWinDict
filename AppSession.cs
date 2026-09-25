using System;
using System.Threading.Tasks;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;

namespace OfflineWinDict
{
    /// <summary>Session-wide state shared across pages (settings + active dictionary).</summary>
    public static class AppSession
    {
        /// <summary>Raised when the active dictionary (language) changes.</summary>
        public static event Action<string>? DictionaryChanged;

        /// <summary>Raised after settings were mutated and saved.</summary>
        public static event Action? SettingsChanged;

        public static OfflineWinDictSettings Settings { get; private set; } = new();

        private static string _dictionaryCode = "en";

        public static string DictionaryCode
        {
            get => _dictionaryCode;
            set
            {
                if (string.Equals(_dictionaryCode, value, StringComparison.Ordinal)) return;
                _dictionaryCode = value;
                Settings.DictionaryCode = value;
                _ = SaveSettingsAsync();
                DictionaryChanged?.Invoke(value);
            }
        }

        public static async Task LoadAsync()
        {
            try
            {
                Settings = await OfflineWinDictSettings.LoadAsync() ?? new OfflineWinDictSettings();
                _dictionaryCode = DictionaryCatalog.ByCode(Settings.DictionaryCode).Code;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "AppSession.Load");
                Settings = new OfflineWinDictSettings();
            }
        }

        public static async Task SaveSettingsAsync()
        {
            try
            {
                await Settings.SaveAsync();
                SettingsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "AppSession.Save");
            }
        }
    }

    /// <summary>Applies the Light/Dark/System theme to the window content and title bar.</summary>
    public static class ThemeService
    {
        public static void Apply(string theme, Microsoft.UI.Xaml.FrameworkElement? root)
        {
            try
            {
                var requested = theme switch
                {
                    "Dark" => Microsoft.UI.Xaml.ElementTheme.Dark,
                    "Light" => Microsoft.UI.Xaml.ElementTheme.Light,
                    _ => Microsoft.UI.Xaml.ElementTheme.Default,
                };
                if (root != null)
                {
                    root.RequestedTheme = requested;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "ThemeService.Apply");
            }
        }
    }
}

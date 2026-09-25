using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace OfflineWinDict.Helpers
{
    public static class ThemeHelper
    {
        public static bool IsDark(FrameworkElement? element = null)
        {
            try
            {
                if (element != null && element.ActualTheme != ElementTheme.Default)
                {
                    return element.ActualTheme == ElementTheme.Dark;
                }

                if (MainWindow.Instance != null && MainWindow.Instance.CurrentActualTheme != ElementTheme.Default)
                {
                    return MainWindow.Instance.CurrentActualTheme == ElementTheme.Dark;
                }

                var setting = AppSession.Settings?.Theme;
                if (string.Equals(setting, "Dark", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(setting, "Light", StringComparison.OrdinalIgnoreCase)) return false;

                return Application.Current.RequestedTheme == ApplicationTheme.Dark;
            }
            catch
            {
                return false;
            }
        }

        public static Brush GetBrush(string key, FrameworkElement? element = null)
        {
            try
            {
                var isDark = IsDark(element);
                var dictName = isDark ? "Dark" : "Light";

                if (Application.Current.Resources.ThemeDictionaries.TryGetValue(dictName, out var dictObj) &&
                    dictObj is ResourceDictionary dict &&
                    dict.TryGetValue(key, out var brushObj) &&
                    brushObj is Brush brush)
                {
                    return brush;
                }

                if (Application.Current.Resources.ThemeDictionaries.TryGetValue("Default", out var defDictObj) &&
                    defDictObj is ResourceDictionary defDict &&
                    defDict.TryGetValue(key, out var defBrushObj) &&
                    defBrushObj is Brush defBrush)
                {
                    return defBrush;
                }

                if (Application.Current.Resources.TryGetValue(key, out var appObj) && appObj is Brush appBrush)
                {
                    return appBrush;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "ThemeHelper.GetBrush");
            }

            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }
}

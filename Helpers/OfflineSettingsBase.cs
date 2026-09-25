using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OfflineWinDict.Helpers
{
    /// <summary>
    /// Local application paths rooted at %LOCALAPPDATA%\OfflineWinDict (unpackaged-safe).
    /// </summary>
    public static class AppPaths
    {
        public static string Root { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OfflineWinDict");

        public static string FilePath(string fileName)
        {
            Directory.CreateDirectory(Root);
            return Path.Combine(Root, fileName);
        }
    }

    /// <summary>
    /// JSON-file-backed settings base persisted to %LOCALAPPDATA%\OfflineWinDict\Settings.dat.
    /// </summary>
    public abstract class OfflineSettingsBase<T> where T : class, new()
    {
        private const string FileName = "Settings.dat";

        public bool IsFirstTimeRun { get; set; } = true;
        public int RunCount { get; set; }
        public bool IsActivated { get; set; }
        public bool RateClicked { get; set; }
        public abstract string AppId { get; set; }

        private static string SettingsPath() => AppPaths.FilePath(FileName);

        public static async Task<T> LoadAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var path = SettingsPath();
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var settings = JsonConvert.DeserializeObject<T>(json);
                        if (settings != null) return settings;
                    }
                }
                catch
                {
                    // Corrupt settings fall back to defaults.
                }
                return new T();
            });
        }

        public async Task SaveAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    File.WriteAllText(SettingsPath(), JsonConvert.SerializeObject(this, Formatting.Indented));
                }
                catch
                {
                    // Saving must never crash the app.
                }
            });
        }
    }
}

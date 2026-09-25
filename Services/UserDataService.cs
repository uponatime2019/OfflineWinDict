using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;
using Windows.Storage;

namespace OfflineWinDict.Services
{
    /// <summary>
    /// Persisted user data: favorited words organized into folders and recent search history.
    /// Stored as JSON (Newtonsoft) in the app's LocalFolder.
    /// </summary>
    public sealed class UserDataService
    {
        public const string GeneralFolder = "General";

        private const string FavoritesFile = "favorites.json";
        private const string HistoryFile = "history.json";
        private const int HistoryCap = 200;

        public static UserDataService Instance { get; } = new();

        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private bool _loaded;

        private List<string> _folders = new() { GeneralFolder };
        private List<FavoriteItem> _favorites = new();
        private List<HistoryItem> _history = new();

        public event Action? FavoritesChanged;
        public event Action? HistoryChanged;

        private UserDataService() { }

        private static string FolderPath(string fileName) =>
            AppPaths.FilePath(fileName);

        public async Task EnsureLoadedAsync()
        {
            if (_loaded) return;
            await _loadLock.WaitAsync();
            try
            {
                if (_loaded) return;

                try
                {
                    var favPath = FolderPath(FavoritesFile);
                    if (File.Exists(favPath))
                    {
                        var data = JsonConvert.DeserializeObject<FavoritesData>(await File.ReadAllTextAsync(favPath));
                        if (data != null)
                        {
                            _favorites = data.Items ?? new List<FavoriteItem>();
                            _folders = data.Folders?.Count > 0 ? data.Folders : new List<string> { GeneralFolder };
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "UserDataService.LoadFavorites");
                }

                try
                {
                    var histPath = FolderPath(HistoryFile);
                    if (File.Exists(histPath))
                    {
                        var data = JsonConvert.DeserializeObject<HistoryData>(await File.ReadAllTextAsync(histPath));
                        if (data?.Items != null) _history = data.Items;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, "UserDataService.LoadHistory");
                }

                if (!_folders.Contains(GeneralFolder)) _folders.Insert(0, GeneralFolder);
                _favorites.RemoveAll(f => string.IsNullOrWhiteSpace(f.Folder));
                _loaded = true;
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public async Task ReloadAsync()
        {
            _loaded = false;
            await EnsureLoadedAsync();
            FavoritesChanged?.Invoke();
            HistoryChanged?.Invoke();
        }

        private async Task SaveFavoritesAsync()
        {
            try
            {
                var data = new FavoritesData { Folders = _folders, Items = _favorites };
                await File.WriteAllTextAsync(FolderPath(FavoritesFile), JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "UserDataService.SaveFavorites");
            }
        }

        private async Task SaveHistoryAsync()
        {
            try
            {
                var data = new HistoryData { Items = _history };
                await File.WriteAllTextAsync(FolderPath(HistoryFile), JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "UserDataService.SaveHistory");
            }
        }

        // ─────────── Favorites ───────────

        public IReadOnlyList<FavoriteItem> Favorites => _favorites;
        public IReadOnlyList<string> Folders => _folders;

        public int FolderCount(string folder) =>
            string.IsNullOrEmpty(folder) ? _favorites.Count : _favorites.Count(f => f.Folder == folder);

        public bool IsFavorite(string word, string languageCode) =>
            _favorites.Any(f => f.Word.Equals(word, StringComparison.OrdinalIgnoreCase) && f.LanguageCode == languageCode);

        /// <summary>Toggles favorite status; returns the new state.</summary>
        public async Task<bool> ToggleFavoriteAsync(string word, string languageCode, string? folder = null)
        {
            await EnsureLoadedAsync();
            var existing = _favorites.FirstOrDefault(f => f.Word.Equals(word, StringComparison.OrdinalIgnoreCase) && f.LanguageCode == languageCode);
            if (existing != null)
            {
                _favorites.Remove(existing);
                await SaveFavoritesAsync();
                FavoritesChanged?.Invoke();
                return false;
            }

            _favorites.Insert(0, new FavoriteItem
            {
                Word = word,
                LanguageCode = languageCode,
                Folder = string.IsNullOrWhiteSpace(folder) || !_folders.Contains(folder) ? GeneralFolder : folder!,
                AddedAtUtc = DateTime.UtcNow,
            });
            await SaveFavoritesAsync();
            FavoritesChanged?.Invoke();
            return true;
        }

        public async Task RemoveFavoriteAsync(FavoriteItem item)
        {
            await EnsureLoadedAsync();
            if (_favorites.Remove(item))
            {
                await SaveFavoritesAsync();
                FavoritesChanged?.Invoke();
            }
        }

        public async Task MoveFavoriteAsync(FavoriteItem item, string folder)
        {
            await EnsureLoadedAsync();
            if (!_folders.Contains(folder)) return;
            if (!string.Equals(item.Folder, folder, StringComparison.Ordinal))
            {
                item.Folder = folder;
                await SaveFavoritesAsync();
                FavoritesChanged?.Invoke();
            }
        }

        public async Task ClearFavoritesAsync()
        {
            await EnsureLoadedAsync();
            _favorites.Clear();
            _folders = new List<string> { GeneralFolder };
            await SaveFavoritesAsync();
            FavoritesChanged?.Invoke();
        }

        // ─────────── Folders ───────────

        public async Task<bool> CreateFolderAsync(string name)
        {
            await EnsureLoadedAsync();
            var clean = (name ?? "").Trim();
            if (clean.Length == 0 || _folders.Contains(clean, StringComparer.OrdinalIgnoreCase)) return false;
            _folders.Add(clean);
            await SaveFavoritesAsync();
            FavoritesChanged?.Invoke();
            return true;
        }

        public async Task RenameFolderAsync(string oldName, string newName)
        {
            await EnsureLoadedAsync();
            var clean = (newName ?? "").Trim();
            if (clean.Length == 0 || string.Equals(oldName, clean, StringComparison.Ordinal)) return;
            if (_folders.Contains(clean, StringComparer.OrdinalIgnoreCase)) return;

            var index = _folders.IndexOf(oldName);
            if (index < 0) return;

            _folders[index] = clean;
            foreach (var item in _favorites.Where(f => f.Folder == oldName))
            {
                item.Folder = clean;
            }
            await SaveFavoritesAsync();
            FavoritesChanged?.Invoke();
        }

        /// <summary>Deletes a folder; its words move to General. General itself cannot be deleted.</summary>
        public async Task DeleteFolderAsync(string name)
        {
            await EnsureLoadedAsync();
            if (string.Equals(name, GeneralFolder, StringComparison.Ordinal)) return;
            if (!_folders.Remove(name)) return;

            foreach (var item in _favorites.Where(f => f.Folder == name))
            {
                item.Folder = GeneralFolder;
            }
            await SaveFavoritesAsync();
            FavoritesChanged?.Invoke();
        }

        // ─────────── History ───────────

        public IReadOnlyList<HistoryItem> History => _history;

        public async Task AddHistoryAsync(string word, string languageCode)
        {
            await EnsureLoadedAsync();
            var clean = (word ?? "").Trim();
            if (clean.Length == 0) return;

            // Move to head, keep only latest per word+language pair.
            _history.RemoveAll(h => h.Word.Equals(clean, StringComparison.OrdinalIgnoreCase) && h.LanguageCode == languageCode);
            _history.Insert(0, new HistoryItem { Word = clean, LanguageCode = languageCode, SearchedAtUtc = DateTime.UtcNow });
            if (_history.Count > HistoryCap) _history.RemoveRange(HistoryCap, _history.Count - HistoryCap);

            await SaveHistoryAsync();
            HistoryChanged?.Invoke();
        }

        public async Task RemoveHistoryAsync(HistoryItem item)
        {
            await EnsureLoadedAsync();
            if (_history.Remove(item))
            {
                await SaveHistoryAsync();
                HistoryChanged?.Invoke();
            }
        }

        public async Task ClearHistoryAsync()
        {
            await EnsureLoadedAsync();
            _history.Clear();
            await SaveHistoryAsync();
            HistoryChanged?.Invoke();
        }

        // ─────────── Storage DTOs ───────────

        private sealed class FavoritesData
        {
            [JsonProperty("folders")]
            public List<string>? Folders { get; set; }

            [JsonProperty("items")]
            public List<FavoriteItem>? Items { get; set; }
        }

        private sealed class HistoryData
        {
            [JsonProperty("items")]
            public List<HistoryItem>? Items { get; set; }
        }
    }
}

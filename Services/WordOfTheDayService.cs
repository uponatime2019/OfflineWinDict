using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;
using Windows.Storage;

namespace OfflineWinDict.Services
{
    /// <summary>
    /// Word of the Day: a deterministic daily pick from a curated list of real English
    /// words; the definition itself is always fetched live from the dictionary sources
    /// and cached locally so recent days remain viewable offline.
    /// </summary>
    public static class WordOfTheDayService
    {
        private const string CacheFile = "wotd_cache.json";
        private const int ArchiveSize = 10;

        private static readonly DateTime Epoch = new(2024, 1, 1);

        // Curated list of real English words chosen for interest level.
        private static readonly string[] Words =
        {
            "serendipity", "ephemeral", "petrichor", "luminous", "eloquent", "resilience",
            "wanderlust", "nostalgia", "mellifluous", "tranquility", "epiphany", "vivacious",
            "audacity", "catharsis", "ineffable", "solitude", "halcyon", "susurrus",
            "aurora", "sonorous", "venerable", "quintessential", "magnanimous", "benevolent",
            "ethereal", "zenith", "cascade", "labyrinth", "sanctuary", "reverie",
            "ethos", "jubilant", "solace", "verdant", "ardent", "lucid",
            "candor", "diligent", "exquisite", "gregarious", "harbinger", "idyllic",
            "kindle", "languid", "meander", "nuance", "oblivion", "panacea",
            "quiver", "radiant", "sincere", "tenacity", "ubiquitous", "veracity",
            "whimsical", "yearn", "zephyr", "abundance", "brevity", "clarity",
            "dexterity", "empathy", "flourish", "gallant", "honor", "intrepid",
            "jovial", "keen", "luminance", "merit", "nurture", "optimism",
            "prosperity", "quietude", "resolute", "serene", "threshold", "unison",
            "vitality", "wonder", "aspiration", "benevolence", "cognizant", "dexterity",
            "ebullient", "felicity", "grandeur", "harmonious", "illuminate", "journey",
            "kindness", "lyrical", "mellowness", "noble", "opulent", "parable",
            "quaint", "resplendent", "sublime", "tranquil", "unveil", "venerate",
            "wistful", "zealous", "aplomb", "bucolic", "cacophony", "demure",
        };

        private sealed class CacheModel
        {
            [JsonProperty("records")]
            public List<CachedWotd>? Records { get; set; }
        }

        private sealed class CachedWotd
        {
            [JsonProperty("date")]
            public string Date { get; set; } = "";

            [JsonProperty("word")]
            public string Word { get; set; } = "";

            [JsonProperty("entryJson")]
            public string? EntryJson { get; set; }
        }

        public static string WordForDate(DateTime localDate)
        {
            var days = (int)(localDate.Date - Epoch).TotalDays;
            var index = ((days % Words.Length) + Words.Length) % Words.Length;
            return Words[index];
        }

        public static string TodayWord() => WordForDate(DateTime.Today);

        private static string CachePath => AppPaths.FilePath(CacheFile);

        private static async Task<List<CachedWotd>> ReadCacheAsync()
        {
            try
            {
                if (!File.Exists(CachePath)) return new List<CachedWotd>();
                var cache = JsonConvert.DeserializeObject<CacheModel>(await File.ReadAllTextAsync(CachePath));
                return cache?.Records ?? new List<CachedWotd>();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "Wotd.ReadCache");
                return new List<CachedWotd>();
            }
        }

        private static async Task WriteCacheAsync(List<CachedWotd> records)
        {
            try
            {
                records = records
                    .OrderByDescending(r => r.Date, StringComparer.Ordinal)
                    .GroupBy(r => r.Date)
                    .Select(g => g.First())
                    .Take(ArchiveSize)
                    .ToList();
                await File.WriteAllTextAsync(CachePath, JsonConvert.SerializeObject(new CacheModel { Records = records }, Formatting.Indented));
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "Wotd.WriteCache");
            }
        }

        /// <summary>Returns today's (or a recent day's) cached entry, if it was fetched before.</summary>
        public static async Task<WordEntry?> GetCachedEntryAsync(DateTime localDate)
        {
            var date = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var records = await ReadCacheAsync();
            var record = records.FirstOrDefault(r => r.Date == date && !string.IsNullOrEmpty(r.EntryJson));
            if (record == null) return null;
            try
            {
                return JsonConvert.DeserializeObject<WordEntry>(record.EntryJson!);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "Wotd.ParseCached");
                return null;
            }
        }

        /// <summary>Stores a fetched entry under today's date and records the word in the archive.</summary>
        public static async Task StoreEntryAsync(WordEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Word)) return;
            var date = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var records = await ReadCacheAsync();
            var existing = records.FirstOrDefault(r => r.Date == date);
            if (existing == null)
            {
                records.Add(new CachedWotd { Date = date, Word = entry.Word, EntryJson = JsonConvert.SerializeObject(entry) });
            }
            else
            {
                existing.Word = entry.Word;
                existing.EntryJson = JsonConvert.SerializeObject(entry);
            }
            await WriteCacheAsync(records);
        }

        /// <summary>Recently used words (for the archive strip), newest first, excluding today.</summary>
        public static async Task<List<WotdRecord>> GetArchiveAsync()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var records = await ReadCacheAsync();
            return records
                .Where(r => r.Date != today && !string.IsNullOrWhiteSpace(r.Word))
                .OrderByDescending(r => r.Date, StringComparer.Ordinal)
                .Select(r => new WotdRecord { Date = r.Date, Word = r.Word })
                .ToList();
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;

namespace OfflineWinDict.Services
{
    /// <summary>
    /// Provides fast, local, 100% offline dictionary lookups using SQLite databases.
    /// Supports English (Dictionary.db) and additional languages (Dictionary_{lang}.db).
    /// </summary>
    public static class OfflineDatabaseService
    {
        private static readonly ConcurrentDictionary<string, string?> DbPathCache = new(StringComparer.OrdinalIgnoreCase);

        public static string? GetDatabasePath(string? languageCode)
        {
            var code = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
            return DbPathCache.GetOrAdd(code, ResolveDbPath);
        }

        public static bool IsLanguageAvailable(string? languageCode)
        {
            var path = GetDatabasePath(languageCode);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public static bool IsAvailable => IsLanguageAvailable("en");

        public static IReadOnlyList<string> GetInstalledLanguages()
        {
            var codes = new List<string>();
            foreach (var def in DictionaryCatalog.All)
            {
                if (IsLanguageAvailable(def.Code))
                {
                    codes.Add(def.Code);
                }
            }
            return codes;
        }

        public static void ResetCache()
        {
            DbPathCache.Clear();
        }

        private static string? ResolveDbPath(string languageCode)
        {
            try
            {
                var candidateNames = languageCode == "en"
                    ? new[] { "Dictionary.db", "Dictionary_en.db" }
                    : new[] { $"Dictionary_{languageCode}.db" };

                foreach (var fileName in candidateNames)
                {
                    // 1. Output directory Assets/Data/{fileName}
                    var basePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Data", fileName);
                    if (File.Exists(basePath)) return basePath;

                    // 2. Direct base directory
                    var baseDirect = Path.Combine(AppContext.BaseDirectory, fileName);
                    if (File.Exists(baseDirect)) return baseDirect;

                    // 3. Project directory during development
                    var devPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Data", fileName);
                    if (File.Exists(devPath)) return Path.GetFullPath(devPath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, $"OfflineDatabaseService.ResolveDbPath_{languageCode}");
            }

            return null;
        }

        public static async Task<WordEntry?> LookupAsync(string rawWord, string languageCode = "en")
        {
            var word = (rawWord ?? "").Trim();
            var code = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
            var dbPath = GetDatabasePath(code);

            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath)) return null;

            return await Task.Run(() =>
            {
                try
                {
                    using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                    connection.Open();

                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT word, wordtype, definition FROM entries WHERE word = $word COLLATE NOCASE";
                    command.Parameters.AddWithValue("$word", word);

                    using var reader = command.ExecuteReader();
                    var langDef = DictionaryCatalog.ByCode(code);
                    var entry = new WordEntry
                    {
                        Word = word,
                        LanguageCode = code,
                        SourceName = $"Offline {langDef.Name} Dictionary",
                        LicenseName = "CC BY-SA / Public Domain"
                    };

                    var groupsByPos = new Dictionary<string, MeaningGroup>(StringComparer.OrdinalIgnoreCase);

                    while (reader.Read())
                    {
                        var exactWord = reader.GetString(0);
                        if (!string.IsNullOrEmpty(exactWord) && string.Equals(entry.Word, word, StringComparison.OrdinalIgnoreCase))
                        {
                            entry.Word = exactWord;
                        }

                        var rawType = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        var rawDef = reader.IsDBNull(2) ? "" : reader.GetString(2);

                        var pos = NormalizePartOfSpeech(rawType);
                        if (!groupsByPos.TryGetValue(pos, out var group))
                        {
                            group = new MeaningGroup { PartOfSpeech = pos };
                            groupsByPos[pos] = group;
                            entry.Meanings.Add(group);
                        }

                        var cleanDef = CleanDefinition(rawDef);
                        if (!string.IsNullOrWhiteSpace(cleanDef))
                        {
                            group.Senses.Add(new SenseDefinition
                            {
                                Definition = cleanDef
                            });
                        }
                    }

                    return entry.HasContent ? entry : null;
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, $"OfflineDatabaseService.LookupAsync_{code}");
                    return null;
                }
            }).ConfigureAwait(false);
        }

        public static async Task<List<string>> PrefixMatchesAsync(string prefix, string languageCode = "en", int max = 8)
        {
            var p = (prefix ?? "").Trim();
            var code = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
            var dbPath = GetDatabasePath(code);

            if (p.Length < 1 || string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath)) return new List<string>();

            return await Task.Run(() =>
            {
                try
                {
                    using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                    connection.Open();

                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT DISTINCT word FROM entries WHERE word LIKE $prefix || '%' COLLATE NOCASE ORDER BY word LIMIT $max";
                    command.Parameters.AddWithValue("$prefix", p);
                    command.Parameters.AddWithValue("$max", max);

                    var results = new List<string>();
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var w = reader.GetString(0);
                        if (!string.IsNullOrWhiteSpace(w)) results.Add(w);
                    }
                    return results;
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, $"OfflineDatabaseService.PrefixMatchesAsync_{code}");
                    return new List<string>();
                }
            }).ConfigureAwait(false);
        }

        private static readonly Dictionary<string, string[]> CuratedStarterWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ar"] = new[]
            {
                "أب", "أبد", "أثر", "أجر", "أجل", "أحد", "أخذ", "أخ", "أدب", "أذن",
                "أرض", "أزرق", "أستاذ", "أسبوع", "أصل", "أكل", "ألف", "أم", "أمل", "أمر",
                "أمن", "أنا", "أنت", "أهل", "أول", "أي", "أيضا", "ابتسم", "ابتكر", "ابتدأ",
                "اتحد", "اتصل", "اتفق", "اختار", "اختلاف", "استمع", "استمر", "اشتغل", "اقتصاد", "اكتشف"
            },
            ["hi"] = new[]
            {
                "अकबर", "अकेला", "अक्षर", "अक्सर", "अखबार", "अगस्त", "अगाड़ी", "अग्नि", "अंगूर", "अंगूठी",
                "अंग्रेजी", "अंचल", "अचानक", "अच्छा", "अजनबी", "अजीब", "अटकल", "अटल", "अटूट", "अड्डा",
                "अति", "अतिथि", "अतीत", "अद्भुत", "अधिकार", "अधिकारी", "अधिक", "अध्यक्ष", "अनंत", "अनपढ़",
                "अनादर", "अनाज", "अनाथ", "अनार", "अनोखा", "अन्वेषण", "अपेक्षा", "अभ्यास", "अमीर", "अर्थ"
            },
            ["tr"] = new[]
            {
                "aba", "abadi", "abajur", "abaküs", "abandone", "abani", "abanoz", "abartmak", "abartı", "abecesel",
                "abdest", "abes", "abide", "abiye", "ablak", "abone", "aborjin", "abullabut", "acaba", "acar",
                "acele", "acemi", "acı", "acıkmak", "acımak", "açık", "açıklama", "açmak", "ad", "ada",
                "adalet", "adam", "adım", "adres", "af", "afet", "afiş", "ağ", "ağaç", "ağır"
            },
            ["ko"] = new[]
            {
                "가게", "가격", "가구", "가깝다", "가끔", "가난", "가는날", "가능", "가득", "가르치다",
                "가리키다", "가방", "가볍다", "가수", "가슴", "가요", "가운데", "가을", "가장", "가족",
                "가죽", "가지", "가지다", "각각", "각자", "간격", "간단하다", "간식", "간장", "간호사",
                "갈비", "갈색", "갈증", "감", "감기", "감동", "감사", "감소", "감자", "감정"
            }
        };

        public static async Task<List<string>> GetFirstWordsAsync(string languageCode = "en", int max = 100)
        {
            var code = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
            var dbPath = GetDatabasePath(code);

            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                if (CuratedStarterWords.TryGetValue(code, out var curated))
                {
                    return curated.Take(max).ToList();
                }
                return new List<string>();
            }

            return await Task.Run(() =>
            {
                try
                {
                    using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                    connection.Open();

                    using var command = connection.CreateCommand();
                    var startChar = code switch
                    {
                        "ru" => "А",
                        "ja" => "あ",
                        "zh" => "一",
                        _ => "A"
                    };

                    command.CommandText = "SELECT DISTINCT word FROM entries WHERE word >= $start ORDER BY word COLLATE NOCASE LIMIT $limit";
                    command.Parameters.AddWithValue("$start", startChar);
                    command.Parameters.AddWithValue("$limit", max * 4);

                    var results = new List<string>();
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var w = reader.GetString(0)?.Trim();
                        if (string.IsNullOrWhiteSpace(w)) continue;
                        if (!char.IsLetter(w[0])) continue;
                        if (!results.Contains(w, StringComparer.OrdinalIgnoreCase))
                        {
                            results.Add(w);
                        }
                        if (results.Count >= max) break;
                    }

                    if (results.Count == 0)
                    {
                        using var fallbackCmd = connection.CreateCommand();
                        fallbackCmd.CommandText = "SELECT DISTINCT word FROM entries LIMIT $limit";
                        fallbackCmd.Parameters.AddWithValue("$limit", max * 4);
                        using var fallbackReader = fallbackCmd.ExecuteReader();
                        while (fallbackReader.Read())
                        {
                            var w = fallbackReader.GetString(0)?.Trim();
                            if (string.IsNullOrWhiteSpace(w)) continue;
                            if (!char.IsLetter(w[0])) continue;
                            if (!results.Contains(w, StringComparer.OrdinalIgnoreCase))
                            {
                                results.Add(w);
                            }
                            if (results.Count >= max) break;
                        }
                    }

                    if (results.Count == 0 && CuratedStarterWords.TryGetValue(code, out var curatedList))
                    {
                        return curatedList.Take(max).ToList();
                    }

                    return results;
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, $"OfflineDatabaseService.GetFirstWordsAsync_{code}");
                    if (CuratedStarterWords.TryGetValue(code, out var curatedList))
                    {
                        return curatedList.Take(max).ToList();
                    }
                    return new List<string>();
                }
            }).ConfigureAwait(false);
        }

        public static async Task<List<string>> SpellSuggestionsAsync(string word, string languageCode = "en", int max = 6)
        {
            var w = (word ?? "").Trim();
            var code = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim().ToLowerInvariant();
            var dbPath = GetDatabasePath(code);

            if (w.Length < 2 || string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath)) return new List<string>();

            return await Task.Run(() =>
            {
                try
                {
                    using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                    connection.Open();

                    using var command = connection.CreateCommand();
                    var prefix = w.Length >= 3 ? w.Substring(0, 3) : w.Substring(0, 2);
                    command.CommandText = "SELECT DISTINCT word FROM entries WHERE word LIKE $prefix || '%' COLLATE NOCASE AND word != $word ORDER BY word LIMIT $max";
                    command.Parameters.AddWithValue("$prefix", prefix);
                    command.Parameters.AddWithValue("$word", w);
                    command.Parameters.AddWithValue("$max", max);

                    var results = new List<string>();
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var item = reader.GetString(0);
                        if (!string.IsNullOrWhiteSpace(item)) results.Add(item);
                    }
                    return results;
                }
                catch (Exception ex)
                {
                    AppLogger.LogException(ex, $"OfflineDatabaseService.SpellSuggestionsAsync_{code}");
                    return new List<string>();
                }
            }).ConfigureAwait(false);
        }

        private static string NormalizePartOfSpeech(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "general";
            var trimmed = raw.Trim().ToLowerInvariant().TrimEnd('.');
            return trimmed switch
            {
                "n" => "noun",
                "v" or "v. t" or "v. i" or "v. t. & i" => "verb",
                "a" or "adj" or "superl" => "adjective",
                "adv" => "adverb",
                "prep" => "preposition",
                "conj" => "conjunction",
                "pron" => "pronoun",
                "interj" or "interj. & n" => "interjection",
                _ => raw.Trim()
            };
        }

        private static string CleanDefinition(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var clean = string.Join(" ", lines.Select(l => l.Trim())).Trim();
            return clean;
        }
    }
}

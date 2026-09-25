using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;

namespace OfflineWinDict.Services
{
    /// <summary>
    /// English thesaurus and suggestion lookups on the Datamuse API
    /// (prefix completions, means-like, synonyms, antonyms, topic glossaries).
    /// Network failures return empty lists; callers render their own states.
    /// </summary>
    public static class ThesaurusService
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OfflineWinDict/1.0 (Windows desktop dictionary app)");
            return client;
        }

        /// <summary>Outcome flag for calls that must distinguish offline from empty.</summary>
        public sealed class OnlineResult<T>
        {
            public bool Offline { get; init; }
            public T Value { get; init; } = default!;

            public static OnlineResult<T> Ok(T value) => new() { Offline = false, Value = value };
            public static OnlineResult<T> Fail() => new() { Offline = true };
        }

        private static async Task<string?> GetBodyAsync(string url)
        {
            try
            {
                using var response = await Http.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return null;
            }
        }

        private static async Task<List<string>> QueryWordsAsync(string query)
        {
            var body = await GetBodyAsync("https://api.datamuse.com/words?" + query).ConfigureAwait(false);
            if (body == null) return new List<string>();
            try
            {
                var arr = JArray.Parse(body);
                return arr.OfType<JObject>()
                    .Select(o => ((string?)o["word"])?.Trim())
                    .Where(w => !string.IsNullOrWhiteSpace(w) && !w!.Any(char.IsDigit))
                    .Select(w => w!)
                    .ToList();
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "ThesaurusService.Parse");
                return new List<string>();
            }
        }

        /// <summary>Prefix completions ("succ" → success, successful…), frequency-ordered.</summary>
        public static async Task<List<string>> PrefixMatchesAsync(string prefix, string languageCode = "en", int max = 8)
        {
            var p = (prefix ?? "").Trim();
            if (p.Length < 1) return new List<string>();

            if (OfflineDatabaseService.IsLanguageAvailable(languageCode))
            {
                var local = await OfflineDatabaseService.PrefixMatchesAsync(p, languageCode, max).ConfigureAwait(false);
                if (local.Count > 0) return local;
            }

            if (languageCode == "en")
            {
                var words = await QueryWordsAsync($"sp={Uri.EscapeDataString(p.ToLowerInvariant())}*&max={max}").ConfigureAwait(false);
                return words;
            }
            return new List<string>();
        }

        /// <summary>Spelling suggestions for a misspelled word.</summary>
        public static async Task<List<string>> SpellSuggestionsAsync(string word, string languageCode = "en", int max = 6)
        {
            var w = (word ?? "").Trim();
            if (w.Length < 2) return new List<string>();

            if (languageCode == "en")
            {
                var words = await QueryWordsAsync($"sp={Uri.EscapeDataString(w.ToLowerInvariant())}&max={max}").ConfigureAwait(false);
                var results = words.Where(s => !string.Equals(s, w, StringComparison.OrdinalIgnoreCase)).ToList();
                if (results.Count > 0) return results;
            }

            if (OfflineDatabaseService.IsLanguageAvailable(languageCode))
            {
                return await OfflineDatabaseService.SpellSuggestionsAsync(w, languageCode, max).ConfigureAwait(false);
            }

            return new List<string>();
        }

        /// <summary>Words with a similar meaning (results column "Related words").</summary>
        public static async Task<List<string>> MeansLikeAsync(string word, int max = 12)
        {
            var w = (word ?? "").Trim();
            if (w.Length < 2) return new List<string>();
            return await QueryWordsAsync($"ml={Uri.EscapeDataString(w.ToLowerInvariant())}&max={max}").ConfigureAwait(false);
        }

        /// <summary>Synonyms and antonyms in one round trip.</summary>
        public static async Task<(List<string> Synonyms, List<string> Antonyms)> SynonymsAntonymsAsync(string word)
        {
            var w = (word ?? "").Trim();
            if (w.Length < 2) return (new List<string>(), new List<string>());
            var syn = await QueryWordsAsync($"rel_syn={Uri.EscapeDataString(w.ToLowerInvariant())}&max=15").ConfigureAwait(false);
            var ant = await QueryWordsAsync($"rel_ant={Uri.EscapeDataString(w.ToLowerInvariant())}&max=15").ConfigureAwait(false);
            return (syn, ant);
        }

        /// <summary>Real word list for a topic, with WordNet definitions (md=d).</summary>
        public static async Task<OnlineResult<List<TopicWord>>> TopicWordsAsync(TopicDef topic, int max = 24)
        {
            var url = $"https://api.datamuse.com/words?ml={Uri.EscapeDataString(topic.Seed)}&topics={Uri.EscapeDataString(topic.Hints)}&md=d&max={max * 2}";
            var body = await GetBodyAsync(url).ConfigureAwait(false);
            if (body == null) return OnlineResult<List<TopicWord>>.Fail();

            try
            {
                var arr = JArray.Parse(body);
                var result = new List<TopicWord>();
                foreach (var o in arr.OfType<JObject>())
                {
                    var word = ((string?)o["word"])?.Trim();
                    if (string.IsNullOrWhiteSpace(word) || word!.Any(char.IsDigit) || word.Contains(' ') || word.Contains('-') || word.Contains('.'))
                        continue;

                    var defs = o["defs"] as JArray;
                    if (defs == null || defs.Count == 0) continue;

                    var first = ((string?)defs[0]) ?? "";
                    var tabIndex = first.IndexOf('\t');
                    var posLetter = tabIndex > 0 ? first[..tabIndex] : "n";
                    var definition = tabIndex > 0 ? first[(tabIndex + 1)..].Trim() : first.Trim();

                    result.Add(new TopicWord
                    {
                        Word = word,
                        PartOfSpeech = posLetter switch
                        {
                            "n" => "noun",
                            "v" => "verb",
                            "adj" => "adjective",
                            "adv" => "adverb",
                            "u" => "other",
                            _ => "other",
                        },
                        Definition = definition,
                    });

                    if (result.Count >= max) break;
                }

                return OnlineResult<List<TopicWord>>.Ok(result);
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "ThesaurusService.TopicWords");
                return OnlineResult<List<TopicWord>>.Ok(new List<TopicWord>());
            }
        }
    }
}

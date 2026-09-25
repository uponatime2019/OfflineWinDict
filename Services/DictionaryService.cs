using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OfflineWinDict.Helpers;
using OfflineWinDict.Models;

namespace OfflineWinDict.Services
{
    /// <summary>
    /// Resolves dictionary entries from live, freely-licensed sources:
    ///  - Free Dictionary API (dictionaryapi.dev, English, CC BY-SA Wiktionary data)
    ///  - Wiktionary REST definitions (all app languages, CC BY-SA)
    ///  - Datamuse (English thesaurus enrichment)
    /// </summary>
    public static class DictionaryService
    {
        private const string FreeDictBase = "https://api.dictionaryapi.dev/api/v2/entries";
        private const string WiktionaryDefBase = "https://en.wiktionary.org/api/rest_v1/page/definition/";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("OfflineWinDict/1.0 (Windows desktop dictionary app)");
            return client;
        }

        private sealed class HttpResult
        {
            public bool NetworkError;
            public int Status;
            public string? Body;
        }

        private static async Task<HttpResult> GetAsync(string url)
        {
            try
            {
                using var response = await Http.GetAsync(url).ConfigureAwait(false);
                var body = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : null;
                return new HttpResult { NetworkError = false, Status = (int)response.StatusCode, Body = body };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                AppLogger.LogAction("DictionaryService.NetworkError", new() { { "url", url }, { "error", ex.GetType().Name } });
                return new HttpResult { NetworkError = true };
            }
        }

        /// <summary>Looks a word up in the given dictionary, falling back across sources.</summary>
        public static async Task<LookupOutcome> LookupAsync(string rawWord, string languageCode)
        {
            var word = (rawWord ?? "").Trim();
            if (word.Length == 0)
            {
                return LookupOutcome.NotFound("Type a word to search.");
            }

            // Offline database primary source when available for the requested language.
            if (OfflineDatabaseService.IsLanguageAvailable(languageCode))
            {
                var offlineEntry = await OfflineDatabaseService.LookupAsync(word, languageCode).ConfigureAwait(false);
                if (offlineEntry != null && offlineEntry.HasContent)
                {
                    return LookupOutcome.Found(offlineEntry);
                }
            }

            // Primary source for English: Free Dictionary API (audio, phonetics, origin, thesaurus fields).
            if (languageCode == "en")
            {
                var free = await GetAsync($"{FreeDictBase}/en/{Uri.EscapeDataString(word.ToLowerInvariant())}").ConfigureAwait(false);
                if (free.NetworkError)
                {
                    if (OfflineDatabaseService.IsLanguageAvailable(languageCode))
                    {
                        var offlineEntry = await OfflineDatabaseService.LookupAsync(word, languageCode).ConfigureAwait(false);
                        if (offlineEntry != null && offlineEntry.HasContent)
                        {
                            return LookupOutcome.Found(offlineEntry);
                        }
                    }
                    return LookupOutcome.OfflineResult();
                }
                if (free.Status == 200 && !string.IsNullOrEmpty(free.Body))
                {
                    var entry = TryParseFreeDictionary(word, free.Body);
                    if (entry != null && entry.HasContent)
                    {
                        await EnrichEnglishEntryAsync(entry).ConfigureAwait(false);
                        return LookupOutcome.Found(entry);
                    }
                }
            }

            // Wiktionary serves every language in the catalog.
            var wiki = await GetAsync(WiktionaryDefBase + Uri.EscapeDataString(word.Replace(' ', '_'))).ConfigureAwait(false);
            if (wiki.NetworkError)
            {
                if (OfflineDatabaseService.IsLanguageAvailable(languageCode))
                {
                    var offlineEntry = await OfflineDatabaseService.LookupAsync(word, languageCode).ConfigureAwait(false);
                    if (offlineEntry != null && offlineEntry.HasContent)
                    {
                        return LookupOutcome.Found(offlineEntry);
                    }
                }
                return LookupOutcome.OfflineResult();
            }
            if (wiki.Status == 200 && !string.IsNullOrEmpty(wiki.Body))
            {
                var wikiEntry = TryParseWiktionary(word, languageCode, wiki.Body);
                if (wikiEntry != null && wikiEntry.HasContent)
                {
                    return LookupOutcome.Found(wikiEntry);
                }
            }

            // Nothing found: offer real spelling suggestions when we can.
            var suggestions = await ThesaurusService.SpellSuggestionsAsync(word, languageCode).ConfigureAwait(false);
            return LookupOutcome.NotFound(
                $"No definitions found for \u201C{word}\u201D in the {DictionaryCatalog.ByCode(languageCode).Name} dictionary.",
                suggestions);
        }

        private static WordEntry? TryParseFreeDictionary(string requestedWord, string json)
        {
            try
            {
                var root = JArray.Parse(json);
                if (root.Count == 0) return null;

                var entry = new WordEntry
                {
                    Word = (string?)root[0]?["word"] ?? requestedWord,
                    LanguageCode = "en",
                    SourceName = "Free Dictionary API",
                    LicenseName = "CC BY-SA",
                };

                foreach (var item in root.OfType<JObject>())
                {
                    if (string.IsNullOrWhiteSpace(entry.PhoneticText) && !string.IsNullOrWhiteSpace((string?)item["phonetic"]))
                    {
                        entry.PhoneticText = (string)item["phonetic"]!;
                    }

                    if (item["phonetics"] is JArray phonetics)
                    {
                        foreach (var ph in phonetics.OfType<JObject>())
                        {
                            var audio = ((string?)ph["audio"])?.Trim();
                            if (!string.IsNullOrWhiteSpace(audio) && audio.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!entry.AudioUrls.Contains(audio)) entry.AudioUrls.Add(audio);
                            }
                            var text = ((string?)ph["text"])?.Trim();
                            if (!string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(entry.PhoneticText))
                            {
                                entry.PhoneticText = text;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(entry.Origin))
                    {
                        entry.Origin = ((string?)item["origin"])?.Trim() ?? "";
                    }

                    if (item["sourceUrls"] is JArray urls)
                    {
                        foreach (var u in urls.OfType<JValue>())
                        {
                            var s = (string?)u;
                            if (!string.IsNullOrWhiteSpace(s) && !entry.SourceUrls.Contains(s!)) entry.SourceUrls.Add(s!);
                        }
                    }

                    if (item["meanings"] is JArray meanings)
                    {
                        foreach (var m in meanings.OfType<JObject>())
                        {
                            var group = new MeaningGroup
                            {
                                PartOfSpeech = (((string?)m["partOfSpeech"]) ?? "").Trim(),
                            };

                            if (m["definitions"] is JArray defs)
                            {
                                foreach (var d in defs.OfType<JObject>())
                                {
                                    var sense = new SenseDefinition
                                    {
                                        Definition = (((string?)d["definition"]) ?? "").Trim(),
                                        Example = (((string?)d["example"]) ?? "").Trim(),
                                    };
                                    CollectStrings(d["synonyms"], sense.Synonyms);
                                    CollectStrings(d["antonyms"], sense.Antonyms);
                                    if (!string.IsNullOrWhiteSpace(sense.Definition)) group.Senses.Add(sense);
                                }
                            }

                            CollectStrings(m["synonyms"], entry.Synonyms);
                            CollectStrings(m["antonyms"], entry.Antonyms);
                            foreach (var sense in group.Senses)
                            {
                                foreach (var s in sense.Synonyms) if (!entry.Synonyms.Contains(s)) entry.Synonyms.Add(s);
                                foreach (var a in sense.Antonyms) if (!entry.Antonyms.Contains(a)) entry.Antonyms.Add(a);
                            }

                            if (group.Senses.Count > 0) entry.Meanings.Add(group);
                        }
                    }

                    if (item["license"] is JObject license)
                    {
                        entry.LicenseName = ((string?)license["name"]) ?? entry.LicenseName;
                        entry.LicenseUrl = ((string?)license["url"]) ?? "";
                    }
                }

                if (!entry.SourceUrls.Any())
                {
                    entry.SourceUrls.Add($"https://en.wiktionary.org/wiki/{Uri.EscapeDataString(entry.Word.Replace(' ', '_'))}");
                }

                return entry.HasContent ? entry : null;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "DictionaryService.ParseFreeDictionary");
                return null;
            }
        }

        private static WordEntry? TryParseWiktionary(string requestedWord, string languageCode, string json)
        {
            try
            {
                var root = JObject.Parse(json);
                if (root[languageCode] is not JArray groups || groups.Count == 0) return null;

                var entry = new WordEntry
                {
                    Word = requestedWord,
                    LanguageCode = languageCode,
                    SourceName = "Wiktionary",
                    LicenseName = "CC BY-SA 4.0",
                    LicenseUrl = "https://creativecommons.org/licenses/by-sa/4.0/",
                };
                entry.SourceUrls.Add($"https://en.wiktionary.org/wiki/{Uri.EscapeDataString(requestedWord.Replace(' ', '_'))}");

                foreach (var g in groups.OfType<JObject>())
                {
                    var group = new MeaningGroup
                    {
                        PartOfSpeech = (((string?)g["partOfSpeech"]) ?? "").Trim(),
                    };

                    if (g["definitions"] is JArray defs)
                    {
                        foreach (var d in defs.OfType<JObject>())
                        {
                            var sense = new SenseDefinition
                            {
                                Definition = StripHtml((string?)d["definition"]),
                            };
                            if (d["examples"] is JArray exs && exs.Count > 0)
                            {
                                sense.Example = StripHtml((string?)exs[0]);
                            }
                            else if (d["parsedExamples"] is JArray parsed && parsed.Count > 0)
                            {
                                sense.Example = StripHtml((string?)parsed[0]?["example"]);
                            }
                            if (!string.IsNullOrWhiteSpace(sense.Definition)) group.Senses.Add(sense);
                        }
                    }

                    if (group.Senses.Count > 0) entry.Meanings.Add(group);
                }

                return entry.HasContent ? entry : null;
            }
            catch (Exception ex)
            {
                AppLogger.LogException(ex, "DictionaryService.ParseWiktionary");
                return null;
            }
        }

        /// <summary>English-only thesaurus enrichment from Datamuse when the primary source returned none.</summary>
        private static async Task EnrichEnglishEntryAsync(WordEntry entry)
        {
            if (entry.Synonyms.Count == 0 || entry.Antonyms.Count == 0)
            {
                var (syn, ant) = await ThesaurusService.SynonymsAntonymsAsync(entry.Word).ConfigureAwait(false);
                foreach (var s in syn) if (!entry.Synonyms.Contains(s)) entry.Synonyms.Add(s);
                foreach (var a in ant) if (!entry.Antonyms.Contains(a)) entry.Antonyms.Add(a);
            }
        }

        private static void CollectStrings(JToken? token, List<string> target)
        {
            if (token is not JArray arr) return;
            foreach (var v in arr.OfType<JValue>())
            {
                var s = ((string?)v)?.Trim();
                if (!string.IsNullOrWhiteSpace(s) && !target.Contains(s!)) target.Add(s!);
            }
        }

        private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

        internal static string StripHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            var text = TagRegex.Replace(html, " ");
            return WebUtility.HtmlDecode(text).Replace("\u00A0", " ").Trim();
        }
    }
}

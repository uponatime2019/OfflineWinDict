using System;
using System.Collections.Generic;
using System.Linq;

namespace OfflineWinDict.Models
{
    /// <summary>A dictionary language offered by the app.</summary>
    public sealed class DictionaryDef
    {
        public string Code { get; }
        public string Name { get; }
        public string NativeName { get; }

        public DictionaryDef(string code, string name, string nativeName)
        {
            Code = code;
            Name = name;
            NativeName = nativeName;
        }

        public string DisplayName => string.IsNullOrWhiteSpace(NativeName) ? Name : $"{Name} · {NativeName}";

        public override string ToString() => DisplayName;
    }

    public static class DictionaryCatalog
    {
        public static readonly IReadOnlyList<DictionaryDef> All = new DictionaryDef[]
        {
            new("en", "English", ""),
            new("es", "Spanish", "español"),
            new("fr", "French", "français"),
            new("de", "German", "Deutsch"),
            new("it", "Italian", "italiano"),
            new("pt", "Portuguese", "português"),
            new("nl", "Dutch", "Nederlands"),
            new("ru", "Russian", "русский"),
            new("tr", "Turkish", "Türkçe"),
            new("hi", "Hindi", "हिन्दी"),
            new("ar", "Arabic", "العربية"),
            new("zh", "Chinese", "中文"),
            new("ja", "Japanese", "日本語"),
            new("ko", "Korean", "한국어"),
        };

        public static DictionaryDef ByCode(string? code) =>
            All.FirstOrDefault(d => d.Code == code) ?? All[0];

        public static int IndexOf(string? code)
        {
            for (var i = 0; i < All.Count; i++)
            {
                if (All[i].Code == code) return i;
            }
            return 0;
        }
    }

    /// <summary>One numbered sense of a word within a part-of-speech group.</summary>
    public sealed class SenseDefinition
    {
        public string Definition { get; set; } = "";
        public string Example { get; set; } = "";
        public List<string> Synonyms { get; } = new();
        public List<string> Antonyms { get; } = new();
    }

    /// <summary>All senses of a word sharing one part of speech.</summary>
    public sealed class MeaningGroup
    {
        public string PartOfSpeech { get; set; } = "";
        public List<SenseDefinition> Senses { get; } = new();
    }

    /// <summary>A fully resolved dictionary entry rendered in the detail panel.</summary>
    public sealed class WordEntry
    {
        public string Word { get; set; } = "";
        public string LanguageCode { get; set; } = "en";
        public string PhoneticText { get; set; } = "";
        public List<string> AudioUrls { get; } = new();
        public string Origin { get; set; } = "";
        public List<MeaningGroup> Meanings { get; } = new();
        public List<string> Synonyms { get; } = new();
        public List<string> Antonyms { get; } = new();
        public List<string> SourceUrls { get; } = new();
        public string SourceName { get; set; } = "";
        public string LicenseName { get; set; } = "";
        public string LicenseUrl { get; set; } = "";

        public bool HasContent => Meanings.Any(m => m.Senses.Any(s => !string.IsNullOrWhiteSpace(s.Definition)))
                                 || !string.IsNullOrWhiteSpace(Origin);
    }

    /// <summary>Outcome of a dictionary lookup, including the offline / not-found states.</summary>
    public sealed class LookupOutcome
    {
        public WordEntry? Entry { get; private init; }
        public bool Offline { get; private init; }
        public string Message { get; private init; } = "";
        public List<string> Suggestions { get; private init; } = new();

        public static LookupOutcome Found(WordEntry entry) => new() { Entry = entry };
        public static LookupOutcome OfflineResult(string message = "You appear to be offline. Check your connection and try again.")
            => new() { Offline = true, Message = message };
        public static LookupOutcome NotFound(string message, List<string>? suggestions = null)
            => new() { Message = message, Suggestions = suggestions ?? new List<string>() };
    }

    /// <summary>An item in the suggestion list (prefix match, history hit, or related word).</summary>
    public sealed class SuggestionItem
    {
        public string Word { get; set; } = "";
        /// <summary>history | prefix | related</summary>
        public string Kind { get; set; } = "prefix";

        public string Glyph => Kind switch
        {
            "history" => "\uE81C",
            "related" => "\uE8A5",
            _ => "\uE721",
        };
    }

    /// <summary>A specialized topic (glossary) listed on the Topics page.</summary>
    public sealed class TopicDef
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Glyph { get; set; } = "\uE8F1";
        public string Seed { get; set; } = "";
        public string Hints { get; set; } = "";
    }

    /// <summary>A word returned for a topic, with its real WordNet definition from Datamuse.</summary>
    public sealed class TopicWord
    {
        public string Word { get; set; } = "";
        public string PartOfSpeech { get; set; } = "";
        public string Definition { get; set; } = "";
    }

    /// <summary>A favorited word, optionally filed into a folder.</summary>
    public sealed class FavoriteItem
    {
        public string Word { get; set; } = "";
        public string LanguageCode { get; set; } = "en";
        public string Folder { get; set; } = "General";
        public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;

        public string Key => Word.ToLowerInvariant() + "|" + LanguageCode;
    }

    /// <summary>A recent search.</summary>
    public sealed class HistoryItem
    {
        public string Word { get; set; } = "";
        public string LanguageCode { get; set; } = "en";
        public DateTime SearchedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>View-only relative time, never persisted.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string DisplayTime { get; set; } = "";
    }

    /// <summary>A remembered Word of the Day.</summary>
    public sealed class WotdRecord
    {
        public string Date { get; set; } = "";
        public string Word { get; set; } = "";
    }
}

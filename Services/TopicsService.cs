using System;
using System.Collections.Generic;
using System.Linq;
using OfflineWinDict.Models;

namespace OfflineWinDict.Services
{
    /// <summary>Catalog of specialized topic glossaries, resolved live through Datamuse.</summary>
    public static class TopicsService
    {
        public static readonly IReadOnlyList<TopicDef> All = new TopicDef[]
        {
            new()
            {
                Id = "science", Title = "Science & Nature", Glyph = "\uE9D9",
                Description = "Words from physics, chemistry, biology and the natural world.",
                Seed = "science", Hints = "physics,chemistry,biology,nature",
            },
            new()
            {
                Id = "technology", Title = "Technology", Glyph = "\uE7F8",
                Description = "Computing, engineering and the vocabulary of the digital age.",
                Seed = "computer", Hints = "computing,software,engineering,internet",
            },
            new()
            {
                Id = "business", Title = "Business & Finance", Glyph = "\uE7BE",
                Description = "Terms from commerce, markets and the workplace.",
                Seed = "business", Hints = "finance,commerce,economics,market",
            },
            new()
            {
                Id = "art", Title = "Art & Literature", Glyph = "\uE771",
                Description = "The language of painting, poetry and prose.",
                Seed = "literature", Hints = "art,poetry,books,writing",
            },
            new()
            {
                Id = "music", Title = "Music", Glyph = "\uEC4F",
                Description = "Notation, instruments and the grammar of sound.",
                Seed = "music", Hints = "melody,instruments,rhythm,song",
            },
            new()
            {
                Id = "sports", Title = "Sports", Glyph = "\uE90E",
                Description = "Words from the field, the court and the track.",
                Seed = "sport", Hints = "games,athletics,competition,team",
            },
            new()
            {
                Id = "food", Title = "Food & Cooking", Glyph = "\uF16D",
                Description = "Kitchen verbs, ingredients and culinary terms.",
                Seed = "cooking", Hints = "food,recipes,kitchen,ingredients",
            },
            new()
            {
                Id = "travel", Title = "Travel & Places", Glyph = "\uE703",
                Description = "Geography, journeys and destinations.",
                Seed = "travel", Hints = "journey,geography,places,adventure",
            },
            new()
            {
                Id = "law", Title = "Law & Government", Glyph = "\uE7EF",
                Description = "Legal and civic terminology.",
                Seed = "law", Hints = "legal,courts,government,justice",
            },
            new()
            {
                Id = "medicine", Title = "Medicine & Health", Glyph = "\uE95E",
                Description = "Medical, anatomical and wellbeing vocabulary.",
                Seed = "medicine", Hints = "health,doctor,disease,treatment",
            },
        };

        public static TopicDef? ById(string? id) => All.FirstOrDefault(t => t.Id == id);
    }
}

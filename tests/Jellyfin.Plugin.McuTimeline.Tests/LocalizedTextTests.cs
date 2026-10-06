using Jellyfin.Plugin.McuTimeline.Model;
using Xunit;

namespace Jellyfin.Plugin.McuTimeline.Tests;

public class LocalizedTextTests
{
    private static readonly LocalizedText _thor = new(new Dictionary<string, string>
    {
        ["fr"] = "Thor : Le Monde des ténèbres",
        ["en"] = "Thor: The Dark World"
    });

    [Theory]
    [InlineData("fr", "Thor : Le Monde des ténèbres")]
    [InlineData("fr-CA", "Thor : Le Monde des ténèbres")]
    [InlineData("en-US", "Thor: The Dark World")]
    [InlineData("de", "Thor: The Dark World")]
    [InlineData(null, "Thor: The Dark World")]
    public void For_PicksTheLanguageElseEnglish(string? language, string expected)
    {
        Assert.Equal(expected, _thor.For(language));
    }

    [Fact]
    public void Parse_TitleObject_ReadsEachLanguage()
    {
        const string Json = """
            {
              "version": "1",
              "items": [
                {
                  "id": "thor-dark-world",
                  "title": { "fr": "Thor : Le Monde des ténèbres", "en": "Thor: The Dark World" },
                  "type": "movie",
                  "tmdbId": 76338,
                  "releaseDate": "2013-11-08",
                  "chronoOrder": 10,
                  "phase": 2,
                  "saga": "infinity",
                  "era": "avengers",
                  "note": { "fr": "Note.", "en": "Note in English." }
                }
              ]
            }
            """;

        var entry = TimelineParser.Parse(Json).Items[0];

        Assert.Equal("Thor : Le Monde des ténèbres", entry.Title.For("fr"));
        Assert.Equal("Thor: The Dark World", entry.Title.Default);
        Assert.Equal("Note in English.", entry.Note?.For("en"));
    }

    [Fact]
    public void Parse_EmptyLanguage_Fails()
    {
        const string Json = """
            { "version": "1", "items": [ { "id": "x", "title": { "fr": "" }, "type": "movie", "tmdbId": 1,
              "releaseDate": "2013-11-08", "chronoOrder": 10, "phase": 2, "saga": "infinity", "era": "avengers" } ] }
            """;

        var ex = Assert.Throws<TimelineDataException>(() => TimelineParser.Parse(Json));

        Assert.Contains("\"title.fr\" must be a non empty string", ex.Message, StringComparison.Ordinal);
    }
}

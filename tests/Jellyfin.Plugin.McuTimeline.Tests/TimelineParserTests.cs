using Jellyfin.Plugin.McuTimeline.Model;
using Xunit;

namespace Jellyfin.Plugin.McuTimeline.Tests;

public class TimelineParserTests
{
    private const string Valid = """
        {
          "version": "2026.10.1",
          "items": [
            {
              "id": "captain-america-first-avenger",
              "title": "Captain America : First Avenger",
              "type": "movie",
              "tmdbId": 1771,
              "releaseDate": "2011-07-22",
              "chronoOrder": 10,
              "storyYear": "1943-1945",
              "phase": 1,
              "saga": "infinity",
              "era": "origins"
            },
            {
              "id": "loki-s1",
              "title": "Loki",
              "type": "series",
              "tmdbId": 84958,
              "seasons": [1],
              "releaseDate": "2021-06-09",
              "chronoOrder": 20,
              "phase": 4,
              "saga": "multiverse",
              "era": "post-blip",
              "note": "Hors du temps."
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ValidFile_ReadsEveryField()
    {
        var data = TimelineParser.Parse(Valid);

        Assert.Equal("2026.10.1", data.Version);
        Assert.Equal(2, data.Items.Count);
        var cap = data.Items[0];
        Assert.Equal(EntryType.Movie, cap.Type);
        Assert.Equal(1771, cap.TmdbId);
        Assert.Equal(new DateOnly(2011, 7, 22), cap.ReleaseDate);
        Assert.Equal("1943-1945", cap.StoryYear);
        Assert.Equal("origins", cap.Era);
        Assert.Empty(cap.Seasons);
        Assert.Equal([1], data.Items[1].Seasons);
        Assert.Equal("Hors du temps.", data.Items[1].Note);
    }

    [Fact]
    public void Parse_EmbeddedFile_IsValid()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "mcu-timeline.json"));

        var data = TimelineParser.Parse(json);

        Assert.NotEmpty(data.Items);
    }

    [Theory]
    [InlineData("\"era\": \"origins\"", "\"era\": \"renaissance\"", "unknown era \"renaissance\"")]
    [InlineData("\"era\": \"origins\"", "\"eraName\": \"origins\"", "\"era\" is required")]
    [InlineData("\"saga\": \"infinity\"", "\"saga\": \"multiverse-2\"", "unknown saga")]
    [InlineData("\"type\": \"movie\"", "\"type\": \"film\"", "unknown type \"film\"")]
    [InlineData("\"phase\": 1,", "\"phase\": 7,", "phase must be between 1 and 6")]
    [InlineData("\"releaseDate\": \"2011-07-22\"", "\"releaseDate\": \"22/07/2011\"", "is not a yyyy-MM-dd date")]
    [InlineData("\"tmdbId\": 1771", "\"tmdbId\": \"1771\"", "\"tmdbId\" must be an integer")]
    [InlineData("\"chronoOrder\": 10,", "", "\"chronoOrder\" is required")]
    [InlineData("\"id\": \"loki-s1\"", "\"id\": \"captain-america-first-avenger\"", "Duplicate id")]
    [InlineData("\"storyYear\": \"1943-1945\",", "\"storyYear\": \"1943-1945\", \"seasons\": [1],", "seasons is only allowed on a series")]
    public void Parse_InvalidEntry_FailsWithAClearMessage(string from, string to, string expected)
    {
        var json = ReplaceFirst(Valid, from, to);

        var ex = Assert.Throws<TimelineDataException>(() => TimelineParser.Parse(json));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_BrokenJson_ReportsTheLine()
    {
        var ex = Assert.Throws<TimelineDataException>(() => TimelineParser.Parse("{\n  \"version\": \"1\",\n  \"items\": [ }"));

        Assert.Contains("line 3", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_MissingItems_Fails()
    {
        Assert.Throws<TimelineDataException>(() => TimelineParser.Parse("{\"version\": \"1\"}"));
    }

    [Fact]
    public void IsUpcoming_DependsOnTheDay()
    {
        var entry = TestData.Entry("x", "2027-05-01", 10);

        Assert.True(entry.IsUpcoming(new DateOnly(2027, 4, 30)));
        Assert.False(entry.IsUpcoming(new DateOnly(2027, 5, 1)));
    }

    private static string ReplaceFirst(string text, string from, string to)
    {
        var index = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(index >= 0, $"test setup: '{from}' not found");
        return string.Concat(text.AsSpan(0, index), to, text.AsSpan(index + from.Length));
    }
}

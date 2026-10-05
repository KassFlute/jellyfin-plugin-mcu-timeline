using Jellyfin.Plugin.McuTimeline.Model;
using Xunit;

namespace Jellyfin.Plugin.McuTimeline.Tests;

public class MatchIndexTests
{
    private static readonly Guid _ironMan = Guid.NewGuid();
    private static readonly Guid _loki = Guid.NewGuid();
    private static readonly Guid _imdbOnly = Guid.NewGuid();

    private readonly MatchIndex _index = new(
    [
        new LibraryCandidate(_ironMan, false, "1726", "tt0371746"),
        new LibraryCandidate(_loki, true, "84958", null),
        new LibraryCandidate(_imdbOnly, false, null, "tt0800080")
    ]);

    [Fact]
    public void Find_ByTmdbId()
    {
        Assert.Equal(_ironMan, _index.Find(TestData.Entry("iron-man", "2008-04-30", 10, tmdbId: 1726)));
    }

    [Fact]
    public void Find_KeepsMoviesAndSeriesApart()
    {
        // TMDB numbers movies and series separately
        Assert.Null(_index.Find(TestData.Entry("movie", "2020-01-01", 10, EntryType.Movie, tmdbId: 84958)));
        Assert.Equal(_loki, _index.Find(TestData.Entry("loki", "2021-06-09", 10, EntryType.Series, tmdbId: 84958)));
    }

    [Fact]
    public void Find_ShortsMatchMovies()
    {
        Assert.Equal(_ironMan, _index.Find(TestData.Entry("short", "2011-01-01", 10, EntryType.Short, tmdbId: 1726)));
    }

    [Fact]
    public void Find_FallsBackOnImdbId_IgnoringCase()
    {
        var entry = TestData.Entry("hulk", "2008-06-13", 10, tmdbId: 1724, imdbId: "TT0800080");

        Assert.Equal(_imdbOnly, _index.Find(entry));
    }

    [Fact]
    public void Find_UnknownIds_ReturnsNull()
    {
        Assert.Null(_index.Find(TestData.Entry("iron-man", "2008-04-30", 10, tmdbId: 1, imdbId: "tt1")));
    }

    [Fact]
    public void Find_NeverMatchesByTitle()
    {
        var index = new MatchIndex([new LibraryCandidate(Guid.NewGuid(), false, null, null)]);

        Assert.Null(index.Find(TestData.Entry("iron-man", "2008-04-30", 10, tmdbId: 1726)));
    }

    [Fact]
    public void Find_OnDuplicates_KeepsTheFirstItem()
    {
        var first = Guid.NewGuid();
        var index = new MatchIndex(
        [
            new LibraryCandidate(first, false, "1726", null),
            new LibraryCandidate(Guid.NewGuid(), false, "1726", null)
        ]);

        Assert.Equal(first, index.Find(TestData.Entry("iron-man", "2008-04-30", 10, tmdbId: 1726)));
    }
}

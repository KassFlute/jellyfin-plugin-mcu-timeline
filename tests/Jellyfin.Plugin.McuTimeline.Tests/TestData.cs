using Jellyfin.Plugin.McuTimeline.Model;

namespace Jellyfin.Plugin.McuTimeline.Tests;

internal static class TestData
{
    public static TimelineEntry Entry(
        string id,
        string releaseDate,
        int chronoOrder,
        EntryType type = EntryType.Movie,
        int tmdbId = 1,
        string? imdbId = null) => new()
    {
        Id = id,
        Title = id,
        Type = type,
        TmdbId = tmdbId,
        ImdbId = imdbId,
        ReleaseDate = DateOnly.Parse(releaseDate, System.Globalization.CultureInfo.InvariantCulture),
        ChronoOrder = chronoOrder,
        Phase = 1,
        Saga = "infinity",
        Era = "avengers"
    };
}

namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// One title of the timeline, as read from the data file.
/// </summary>
public sealed class TimelineEntry
{
    /// <summary>
    /// Gets the stable identifier, e.g. iron-man-2008.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the displayed title.
    /// </summary>
    public required LocalizedText Title { get; init; }

    /// <summary>
    /// Gets the content type.
    /// </summary>
    public required EntryType Type { get; init; }

    /// <summary>
    /// Gets the TMDB id, the main key to find the title in the library.
    /// </summary>
    public required int TmdbId { get; init; }

    /// <summary>
    /// Gets the IMDb id, tried when the TMDB id finds nothing.
    /// </summary>
    public string? ImdbId { get; init; }

    /// <summary>
    /// Gets the seasons this entry covers. Empty means every regular season.
    /// </summary>
    public IReadOnlyList<int> Seasons { get; init; } = [];

    /// <summary>
    /// Gets the release date.
    /// </summary>
    public required DateOnly ReleaseDate { get; init; }

    /// <summary>
    /// Gets the position in story order.
    /// </summary>
    public required int ChronoOrder { get; init; }

    /// <summary>
    /// Gets the in-universe year shown on the card. Never used for grouping.
    /// </summary>
    public LocalizedText? StoryYear { get; init; }

    /// <summary>
    /// Gets the phase, 1 to 6.
    /// </summary>
    public required int Phase { get; init; }

    /// <summary>
    /// Gets the saga.
    /// </summary>
    public required string Saga { get; init; }

    /// <summary>
    /// Gets the in-universe era, which drives the separators in story order.
    /// </summary>
    public required string Era { get; init; }

    /// <summary>
    /// Gets the accent colour, #RRGGBB, used by the timeline for the selected title.
    /// </summary>
    public string? AccentColor { get; init; }

    /// <summary>
    /// Gets the one line explanation for a debatable placement.
    /// </summary>
    public LocalizedText? Note { get; init; }

    /// <summary>
    /// Gets a value indicating whether the title is matched against series rather than movies.
    /// </summary>
    public bool IsSeries => Type == EntryType.Series;

    /// <summary>
    /// Gets a value indicating whether the title is not released yet on the given day.
    /// </summary>
    /// <param name="today">Current date.</param>
    /// <returns>True when the release date is still ahead.</returns>
    public bool IsUpcoming(DateOnly today) => ReleaseDate > today;
}

namespace Jellyfin.Plugin.McuTimeline.Api;

/// <summary>
/// Timeline as seen by the calling user.
/// </summary>
public sealed class TimelineResponse
{
    /// <summary>
    /// Gets the data version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the server id, needed for the item links of the web client.
    /// </summary>
    public required string ServerId { get; init; }

    /// <summary>
    /// Gets the calling user id, used to keep the page choices per user.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Gets a value indicating whether titles can be requested through Jellyseerr.
    /// </summary>
    public bool CanRequest { get; init; }

    /// <summary>
    /// Gets the entries, in file order. Each one carries its rank in both orders, so the
    /// page switches order without sorting.
    /// </summary>
    public required IReadOnlyList<TimelineItemDto> Items { get; init; }
}

/// <summary>
/// One entry merged with the library and the user's play state.
/// </summary>
public sealed record TimelineItemDto
{
    /// <summary>Gets the entry id.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the type: movie, series or short.</summary>
    public required string Type { get; init; }

    /// <summary>Gets the TMDB id.</summary>
    public required int TmdbId { get; init; }

    /// <summary>Gets the release date, yyyy-MM-dd.</summary>
    public required string ReleaseDate { get; init; }

    /// <summary>Gets the story order position.</summary>
    public required int ChronoOrder { get; init; }

    /// <summary>Gets the in-universe year.</summary>
    public string? StoryYear { get; init; }

    /// <summary>Gets the phase.</summary>
    public required int Phase { get; init; }

    /// <summary>Gets the saga.</summary>
    public required string Saga { get; init; }

    /// <summary>Gets the era.</summary>
    public required string Era { get; init; }

    /// <summary>Gets the accent colour, #RRGGBB.</summary>
    public string? AccentColor { get; init; }

    /// <summary>Gets the position in release order, from 0.</summary>
    public int ReleaseRank { get; init; }

    /// <summary>Gets the position in story order, from 0.</summary>
    public int ChronoRank { get; init; }

    /// <summary>Gets the placement note.</summary>
    public string? Note { get; init; }

    /// <summary>Gets the seasons covered by a series entry.</summary>
    public IReadOnlyList<int> Seasons { get; init; } = [];

    /// <summary>Gets the status: owned, absent or upcoming.</summary>
    public required string Status { get; init; }

    /// <summary>Gets the library item, movie or series, when owned.</summary>
    public string? JellyfinId { get; init; }

    /// <summary>Gets the movie runtime in ticks.</summary>
    public long? RunTimeTicks { get; init; }

    /// <summary>Gets the number of episodes of a series entry.</summary>
    public int? EpisodeCount { get; init; }

    /// <summary>Gets a value indicating whether the user has seen the whole entry, in Jellyfin or, for a title the library does not have, elsewhere.</summary>
    public bool Played { get; init; }

    /// <summary>Gets a value indicating whether the user left the entry out of the progression.</summary>
    public bool Skipped { get; init; }

    /// <summary>Gets a value indicating whether the user started but did not finish it.</summary>
    public bool InProgress { get; init; }

    /// <summary>Gets the progress, 0 to 1.</summary>
    public double Progress { get; init; }
}

/// <summary>
/// Administrator view of the matching and the playlists.
/// </summary>
public sealed class StatusResponse
{
    /// <summary>Gets the data version in use.</summary>
    public required string DataVersion { get; init; }

    /// <summary>Gets where the data comes from: embedded or override.</summary>
    public required string DataSource { get; init; }

    /// <summary>Gets the path where an override file is read from.</summary>
    public required string OverridePath { get; init; }

    /// <summary>Gets why the override was rejected, if it was.</summary>
    public string? OverrideError { get; init; }

    /// <summary>Gets the number of released titles.</summary>
    public required int Total { get; init; }

    /// <summary>Gets the number of released titles found in the library.</summary>
    public required int Found { get; init; }

    /// <summary>Gets the number of announced titles, not counted in the total.</summary>
    public required int Upcoming { get; init; }

    /// <summary>Gets the released titles not found.</summary>
    public required IReadOnlyList<MissingTitleDto> Missing { get; init; }

    /// <summary>Gets the last playlist synchronisation, UTC.</summary>
    public DateTime? LastSyncUtc { get; init; }

    /// <summary>Gets the error of the last synchronisation.</summary>
    public string? LastSyncError { get; init; }
}

/// <summary>
/// A released title missing from the library.
/// </summary>
/// <param name="Id">Entry id.</param>
/// <param name="Title">Title.</param>
/// <param name="Type">movie, series or short.</param>
/// <param name="TmdbId">TMDB id.</param>
/// <param name="ImdbId">IMDb id.</param>
public sealed record MissingTitleDto(string Id, string Title, string Type, int TmdbId, string? ImdbId);

/// <summary>
/// Outcome of a Jellyseerr request.
/// </summary>
/// <param name="Status">pending, processing or available, when the request went through.</param>
/// <param name="Error">notLinked, unreachable or refused, the page shows the matching text.</param>
/// <param name="SeerrMessage">Jellyseerr's own message for a refusal, in its language.</param>
public sealed record RequestResultDto(string? Status, string? Error, string? SeerrMessage);

/// <summary>
/// Menu entry setting, read by the menu script.
/// </summary>
/// <param name="Enabled">Whether the entry is shown under Media.</param>
public sealed record MenuDto(bool Enabled);

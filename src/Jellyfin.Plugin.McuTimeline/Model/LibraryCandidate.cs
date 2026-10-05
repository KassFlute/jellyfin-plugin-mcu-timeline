namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// A library movie or series reduced to what matching needs.
/// </summary>
/// <param name="ItemId">Jellyfin item id.</param>
/// <param name="IsSeries">True for a series, false for a movie.</param>
/// <param name="TmdbId">TMDB provider id, if any.</param>
/// <param name="ImdbId">IMDb provider id, if any.</param>
public sealed record LibraryCandidate(Guid ItemId, bool IsSeries, string? TmdbId, string? ImdbId);

using System.Globalization;

namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// Finds timeline entries among library items by provider id. Never by title, too many
/// false positives.
/// </summary>
public sealed class MatchIndex
{
    // TMDB numbers movies and series independently, so the same id can be both a movie and
    // a series. Kept apart by keying on the kind
    private readonly Dictionary<(bool IsSeries, string Id), Guid> _byTmdb = [];
    private readonly Dictionary<(bool IsSeries, string Id), Guid> _byImdb = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MatchIndex"/> class.
    /// </summary>
    /// <param name="candidates">Library items. On duplicates, the first one wins.</param>
    public MatchIndex(IEnumerable<LibraryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.TmdbId))
            {
                _byTmdb.TryAdd((candidate.IsSeries, candidate.TmdbId.Trim()), candidate.ItemId);
            }

            if (!string.IsNullOrWhiteSpace(candidate.ImdbId))
            {
                _byImdb.TryAdd((candidate.IsSeries, candidate.ImdbId.Trim().ToLowerInvariant()), candidate.ItemId);
            }
        }
    }

    /// <summary>
    /// Looks an entry up, by TMDB id first and IMDb id second.
    /// </summary>
    /// <param name="entry">Timeline entry.</param>
    /// <returns>The library item id, or null when the title is not in the library.</returns>
    public Guid? Find(TimelineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var tmdb = entry.TmdbId.ToString(CultureInfo.InvariantCulture);
        if (_byTmdb.TryGetValue((entry.IsSeries, tmdb), out var id))
        {
            return id;
        }

        if (entry.ImdbId is not null
            && _byImdb.TryGetValue((entry.IsSeries, entry.ImdbId.ToLowerInvariant()), out id))
        {
            return id;
        }

        return null;
    }
}

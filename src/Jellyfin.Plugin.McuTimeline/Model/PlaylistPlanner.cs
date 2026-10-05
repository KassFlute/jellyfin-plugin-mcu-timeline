namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// Computes the content of a playlist.
/// </summary>
public static class PlaylistPlanner
{
    /// <summary>
    /// Lists the library items of a playlist, in order.
    /// </summary>
    /// <param name="entries">Entries the playlist may contain.</param>
    /// <param name="order">Playlist order.</param>
    /// <param name="itemsOf">Library items of an entry: the movie, or the episodes of the
    /// listed seasons in airing order. Empty when the entry is not owned.</param>
    /// <returns>Item ids, each one once.</returns>
    public static IReadOnlyList<Guid> Plan(
        IEnumerable<TimelineEntry> entries,
        TimelineOrder order,
        Func<TimelineEntry, IReadOnlyList<Guid>> itemsOf)
    {
        ArgumentNullException.ThrowIfNull(itemsOf);
        var seen = new HashSet<Guid>();
        var result = new List<Guid>();
        foreach (var entry in TimelineSorter.Sort(entries, order))
        {
            foreach (var id in itemsOf(entry))
            {
                // Jellyfin drops nothing on its side, a repeated item would play twice
                if (seen.Add(id))
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }
}

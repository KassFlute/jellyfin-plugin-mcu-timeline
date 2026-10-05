namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// The two orderings. timeline.js sorts the same way, keep them in step.
/// </summary>
public static class TimelineSorter
{
    /// <summary>
    /// Sorts entries in the given order.
    /// </summary>
    /// <param name="entries">Entries to sort.</param>
    /// <param name="order">Wanted order.</param>
    /// <returns>A new sorted list.</returns>
    public static IReadOnlyList<TimelineEntry> Sort(IEnumerable<TimelineEntry> entries, TimelineOrder order)
    {
        // ties fall back on the other key, then on the id, so the result never depends on
        // file order
        var sorted = order == TimelineOrder.Release
            ? entries.OrderBy(e => e.ReleaseDate).ThenBy(e => e.ChronoOrder)
            : entries.OrderBy(e => e.ChronoOrder).ThenBy(e => e.ReleaseDate);
        return sorted.ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
    }
}

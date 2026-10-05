namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// A loaded and validated data file.
/// </summary>
/// <param name="Version">Data version, e.g. 2026.10.1.</param>
/// <param name="Items">Entries, in file order.</param>
public sealed record TimelineData(string Version, IReadOnlyList<TimelineEntry> Items);

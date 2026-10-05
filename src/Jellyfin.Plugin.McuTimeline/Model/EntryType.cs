namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// Content type of a timeline entry.
/// </summary>
public enum EntryType
{
    /// <summary>
    /// Feature film.
    /// </summary>
    Movie,

    /// <summary>
    /// Series, or some of its seasons.
    /// </summary>
    Series,

    /// <summary>
    /// One-Shot or Special Presentation. Stored as a movie in Jellyfin.
    /// </summary>
    Short
}

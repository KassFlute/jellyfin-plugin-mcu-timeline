namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// The two viewing orders.
/// </summary>
public enum TimelineOrder
{
    /// <summary>
    /// By release date.
    /// </summary>
    Release,

    /// <summary>
    /// By position in the story.
    /// </summary>
    Chronological
}

using Jellyfin.Plugin.McuTimeline.Model;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Where the data in use comes from.
/// </summary>
public enum TimelineSourceKind
{
    /// <summary>
    /// The file shipped in the plugin.
    /// </summary>
    Embedded,

    /// <summary>
    /// The local override file.
    /// </summary>
    Override
}

/// <summary>
/// The data in use.
/// </summary>
/// <param name="Data">Validated data.</param>
/// <param name="Kind">Origin.</param>
/// <param name="OverrideError">Why an existing override was rejected, if it was.</param>
public sealed record TimelineSource(TimelineData Data, TimelineSourceKind Kind, string? OverrideError);

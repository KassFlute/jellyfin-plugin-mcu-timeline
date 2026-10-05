using Jellyfin.Plugin.McuTimeline.Configuration;

namespace Jellyfin.Plugin.McuTimeline.Services;

/// <summary>
/// Access to the plugin settings from services.
/// </summary>
public static class PluginSettings
{
    /// <summary>
    /// Gets the current settings, defaults when the plugin is not loaded yet.
    /// </summary>
    public static PluginConfiguration Current => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Saves the settings.
    /// </summary>
    public static void Save() => Plugin.Instance?.SaveConfiguration();
}

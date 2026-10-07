using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.McuTimeline.Configuration;

/// <summary>
/// Persisted plugin settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the libraries searched for the titles. Empty means every movie and
    /// show library.
    /// </summary>
    public string[] LibraryIds { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether One-Shots and Special Presentations are shown
    /// and added to the playlists and the collection.
    /// </summary>
    public bool IncludeShorts { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether announced titles are shown.
    /// </summary>
    public bool IncludeUpcoming { get; set; } = true;

    /// <summary>
    /// Gets or sets the settings layout version, see <see cref="Migrate"/>.
    /// </summary>
    public int SettingsVersion { get; set; }

    /// <summary>
    /// Gets or sets the single playlist switch of version 1, only read by <see cref="Migrate"/>.
    /// </summary>
    public bool? EnablePlaylists { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the release order playlist is maintained.
    /// </summary>
    public bool EnableReleasePlaylist { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the story order playlist is maintained.
    /// </summary>
    public bool EnableChronologicalPlaylist { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the collection is maintained.
    /// </summary>
    public bool EnableCollection { get; set; }

    /// <summary>
    /// Gets or sets the name of the collection. Empty means Marvel Cinematic Universe.
    /// </summary>
    public string CollectionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user who owns the playlists. Empty means the first administrator.
    /// </summary>
    public string PlaylistOwnerId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the release order playlist. Empty means a default in the
    /// server language.
    /// </summary>
    public string ReleasePlaylistName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the story order playlist. Empty means a default in the
    /// server language.
    /// </summary>
    public string ChronologicalPlaylistName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the timeline is listed in the side menu
    /// through the Plugin Pages plugin.
    /// </summary>
    public bool ShowInPluginPages { get; set; }

    /// <summary>
    /// Gets or sets the Jellyseerr address. Empty hides the request button.
    /// </summary>
    public string JellyseerrUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Jellyseerr API key.
    /// </summary>
    public string JellyseerrApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the id of the release order playlist. The plugin only ever touches the
    /// playlists and the collection recorded here.
    /// </summary>
    public string ReleasePlaylistId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the id of the story order playlist.
    /// </summary>
    public string ChronologicalPlaylistId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the id of the collection.
    /// </summary>
    public string CollectionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the playlists and the collection were last synchronised, UTC.
    /// </summary>
    public DateTime? LastSyncUtc { get; set; }

    /// <summary>
    /// Gets or sets the error of the last synchronisation, empty when it succeeded.
    /// </summary>
    public string LastSyncError { get; set; } = string.Empty;

    /// <summary>
    /// Brings settings saved by an older version up to date.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public bool Migrate()
    {
        if (SettingsVersion >= 2)
        {
            return false;
        }

        // version 1 had one switch for both playlists, on by default. A playlist it made
        // stays, else the next sync would delete it
        var playlists = EnablePlaylists ?? true;
        EnableReleasePlaylist |= playlists && !string.IsNullOrEmpty(ReleasePlaylistId);
        EnableChronologicalPlaylist |= playlists && !string.IsNullOrEmpty(ChronologicalPlaylistId);
        EnablePlaylists = null;
        SettingsVersion = 2;
        return true;
    }
}
